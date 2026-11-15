using System.Diagnostics;
using System.Runtime.Versioning;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Vpx.Net;

namespace ClassInsight.Agent.Services;

/// <summary>
/// The student (sender) side of one WebRTC screen stream to a single teacher. It captures
/// one display, encodes each frame as VP8 and pushes it over the peer connection, while
/// exchanging SDP/ICE with the teacher through the SignalR hub (the server only relays, and
/// only for a pair it authorised).
///
/// The student is the offerer here, mirroring the teacher's <c>ScreenStreamReceiver</c>
/// (answerer). One sender exists per watching teacher, keyed by that teacher's connection
/// id, so a student can be viewed by several teachers at once (FR-AGENT-08); a re-request
/// from the same teacher (quality or display switch) replaces its sender.
///
/// Media is DTLS-SRTP encrypted by the WebRTC stack (NFR-SEC-03). Frames whose pixels haven't
/// changed are not re-encoded (a 1 s keep-alive still flows), which keeps an idle desktop
/// nearly free to stream (NFR-PERF-04, R-3).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenStreamSender : IAsyncDisposable
{
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(1);

    private readonly HubConnection _hub;
    private readonly string _teacherConnectionId;
    private readonly StreamQuality _quality;
    private readonly IReadOnlyList<IceServerConfig> _iceServers;
    private readonly ILogger _logger;

    private VP8Codec _encoder = new();
    private readonly ScreenCapture _capture;
    private RTCPeerConnection? _pc;
    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private volatile bool _disposed;
    private bool _warnedEncode;
    private int _lastW, _lastH;

    public ScreenStreamSender(
        HubConnection hub,
        string teacherConnectionId,
        StreamQuality quality,
        CaptureDisplay display,
        IReadOnlyList<IceServerConfig> iceServers,
        ILogger logger)
    {
        _hub = hub;
        _teacherConnectionId = teacherConnectionId;
        _quality = quality;
        _iceServers = iceServers;
        _logger = logger;
        _capture = new ScreenCapture(display);
    }

    public StreamQuality Quality => _quality;
    public int DisplayIndex => _capture.Display.Info.Index;

    // Quality → how aggressively we scale and how fast we send (FR-STREAM-04, NFR-PERF-02).
    // Thumbnails are kept small: a teacher's grid may decode 40 of them at once.
    public static int MaxWidthFor(StreamQuality q) => q == StreamQuality.Focus ? 1280 : 480;
    public static int FpsFor(StreamQuality q) => q == StreamQuality.Focus ? 15 : 5;

    public async Task StartAsync()
    {
        var pc = new RTCPeerConnection(BuildConfig(_iceServers));
        pc.addTrack(new MediaStreamTrack(_encoder.SupportedFormats, MediaStreamStatusEnum.SendOnly));

        pc.onicecandidate += candidate =>
        {
            if (candidate is null) return;
            _ = SafeSend(Protocol.ServerMethods.SendIceCandidate, new IceCandidateMessage
            {
                TargetConnectionId = _teacherConnectionId,
                Candidate = candidate.ToString(),
                SdpMid = candidate.sdpMid,
                SdpMLineIndex = candidate.sdpMLineIndex,
            });
        };

        pc.onconnectionstatechange += state =>
        {
            _logger.LogDebug("Stream to {Teacher}: {State}", _teacherConnectionId, state);
            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    StartLoop();
                    break;
                case RTCPeerConnectionState.failed:
                case RTCPeerConnectionState.disconnected:
                case RTCPeerConnectionState.closed:
                    StopLoop();
                    break;
            }
        };

        _pc = pc;

        var offer = pc.createOffer(null);
        await pc.setLocalDescription(offer);

        await SafeSend(Protocol.ServerMethods.SendOffer, new SdpMessage
        {
            TargetConnectionId = _teacherConnectionId,
            Type = "offer",
            Sdp = offer.sdp,
        });
        _logger.LogInformation("Offered screen stream to teacher {Teacher} ({Quality}, display {Display}).",
            _teacherConnectionId, _quality, DisplayIndex);
    }

    internal static RTCConfiguration? BuildConfig(IReadOnlyList<IceServerConfig> servers)
    {
        if (servers.Count == 0) return null;   // flat LAN: host candidates are enough
        return new RTCConfiguration
        {
            iceServers = servers.Select(s => new RTCIceServer
            {
                urls = s.Urls,
                username = s.Username,
                credential = s.Credential,
            }).ToList(),
        };
    }

    public void HandleAnswer(SdpMessage answer)
    {
        if (_pc is null) return;
        var result = _pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = answer.Sdp,
        });
        if (result != SetDescriptionResultEnum.OK)
            _logger.LogWarning("Teacher {Teacher} answer rejected: {Result}", _teacherConnectionId, result);
    }

    public void HandleIceCandidate(IceCandidateMessage message)
    {
        _pc?.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = message.Candidate,
            sdpMid = message.SdpMid,
            sdpMLineIndex = (ushort)(message.SdpMLineIndex ?? 0),
        });
    }

    private void StartLoop()
    {
        if (_loop is not null || _disposed) return;
        _loopCts = new CancellationTokenSource();
        _loop = Task.Run(() => CaptureLoopAsync(_loopCts.Token));
    }

    private void StopLoop()
    {
        try { _loopCts?.Cancel(); } catch { }
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        var maxWidth = MaxWidthFor(_quality);
        var interval = TimeSpan.FromMilliseconds(1000.0 / FpsFor(_quality));

        _encoder.ForceKeyFrame();   // first frame a teacher sees must be a keyframe
        var clock = Stopwatch.StartNew();
        TimeSpan? lastSent = null;
        long frames = 0, skipped = 0;

        // Two-stage pipeline: while frame N is being VP8-encoded, frame N+1 is already being
        // captured on another thread, so throughput is bounded by the slower stage rather than
        // their sum (capture ≈ 30–45 ms, 1280×720 encode ≈ 30 ms on a typical PC). The capture
        // keeps two buffers, so frame N stays intact while N+1 is written (see ScreenCapture.Frame).
        async Task<(ScreenCapture.Frame? Frame, TimeSpan At)> CaptureAt(TimeSpan due)
        {
            var wait = due - clock.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            var at = clock.Elapsed;
            return (_capture.Capture(maxWidth), at);
        }

        var due = TimeSpan.Zero;
        var next = Task.Run(() => CaptureAt(TimeSpan.Zero), ct);

        while (!ct.IsCancellationRequested && !_disposed)
        {
            ScreenCapture.Frame? frame;
            TimeSpan at;
            try
            {
                (frame, at) = await next;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Capture for {Teacher} failed.", _teacherConnectionId);
                frame = null;
                at = clock.Elapsed;
            }

            // Start capturing the next frame (paced to the target fps) before encoding this one.
            // Frames are scheduled on a fixed timeline (start + N·interval) rather than "last + interval":
            // Task.Delay rounds up to the ~15.6 ms Windows timer tick, and chaining delays would
            // accumulate that error (15 fps target → ~13 fps measured). If we fall a whole frame
            // behind (slow machine), re-anchor instead of bursting to catch up.
            due += interval;
            if (due < clock.Elapsed - interval) due = clock.Elapsed;
            var nextDue = due;
            next = Task.Run(() => CaptureAt(nextDue), ct);

            try
            {
                if (frame is { } f && _pc is { connectionState: RTCPeerConnectionState.connected })
                {
                    var send = lastSent is null || f.Changed || at - lastSent.Value >= KeepAlive;
                    if (send)
                    {
                        // A resolution change (display settings changed mid-stream) needs a new
                        // encoder: the managed VP8 codec sizes its buffers on first use and can't
                        // be resized, even with a keyframe. A fresh codec starts with a keyframe.
                        if (f.Width != _lastW || f.Height != _lastH)
                        {
                            if (_lastW != 0)
                            {
                                var old = _encoder;
                                _encoder = new VP8Codec();
                                old.Dispose();
                            }
                            _lastW = f.Width; _lastH = f.Height;
                        }
                        var encoded = _encoder.EncodeVideo(f.Width, f.Height, f.Bgra, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.VP8);
                        if (encoded is { Length: > 0 })
                        {
                            // RTP timestamp advances by real elapsed time (90 kHz clock), so skipped
                            // unchanged frames don't compress the timeline.
                            var elapsed = lastSent is null ? interval : at - lastSent.Value;
                            _pc.SendVideo((uint)Math.Max(1, elapsed.TotalSeconds * 90000), encoded);
                            lastSent = at;
                            frames++;
                        }
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }
            catch (Exception ex)
            {
                // Surface the first failure loudly: a stream that silently never sends looks
                // "connected" to the teacher but stays black.
                if (!_warnedEncode)
                {
                    _warnedEncode = true;
                    _logger.LogWarning(ex, "Encoding/sending a frame to {Teacher} failed (further failures logged at Debug).", _teacherConnectionId);
                }
                else
                {
                    _logger.LogDebug(ex, "Frame send to {Teacher} failed.", _teacherConnectionId);
                }
            }
        }

        try { await next; } catch { /* cancelled */ }

        var secs = Math.Max(0.001, clock.Elapsed.TotalSeconds);
        _logger.LogInformation(
            "Stream to {Teacher} ({Quality}) ended after {Seconds:0}s: {Frames} frames sent ({Fps:0.0} fps), {Skipped} unchanged frames skipped.",
            _teacherConnectionId, _quality, secs, frames, frames / secs, skipped);
    }

    private async Task SafeSend(string method, object arg)
    {
        try
        {
            if (!_disposed && _hub.State == HubConnectionState.Connected)
                await _hub.SendAsync(method, arg);
        }
        catch { /* link state surfaces via connection callbacks */ }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        StopLoop();
        if (_loop is not null)
        {
            try { await _loop; } catch { }
        }
        _pc?.close();
        _pc = null;
        _loopCts?.Dispose();
        _capture.Dispose();
        _encoder.Dispose();
    }
}

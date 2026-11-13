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
/// the screen, encodes each frame as VP8 and pushes it over the peer connection, while
/// exchanging SDP/ICE with the teacher through the SignalR hub (the server only relays).
///
/// The student is the offerer here, mirroring the teacher's <c>ScreenStreamReceiver</c>
/// (answerer). One sender exists per watching teacher, keyed by that teacher's connection
/// id, so a student can be viewed by more than one teacher at once.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenStreamSender : IAsyncDisposable
{
    private readonly HubConnection _hub;
    private readonly string _teacherConnectionId;
    private readonly StreamQuality _quality;
    private readonly ILogger _logger;

    private readonly VP8Codec _encoder = new();
    private readonly ScreenCapture _capture = new();
    private RTCPeerConnection? _pc;
    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private volatile bool _disposed;

    public ScreenStreamSender(HubConnection hub, string teacherConnectionId, StreamQuality quality, ILogger logger)
    {
        _hub = hub;
        _teacherConnectionId = teacherConnectionId;
        _quality = quality;
        _logger = logger;
    }

    // Quality → how aggressively we scale and how fast we send.
    private int MaxWidth => _quality == StreamQuality.Focus ? 1280 : 640;
    private int Fps => _quality == StreamQuality.Focus ? 15 : 5;
    private uint FrameDurationRtp => (uint)(90000 / Fps);   // VP8 clock is 90 kHz

    public async Task StartAsync()
    {
        var pc = new RTCPeerConnection(null);
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
        _logger.LogInformation("Offered screen stream to teacher {Teacher} ({Quality}).", _teacherConnectionId, _quality);
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
        _encoder.ForceKeyFrame();   // first frame a teacher sees must be a keyframe
        var interval = TimeSpan.FromMilliseconds(1000.0 / Fps);

        while (!ct.IsCancellationRequested && !_disposed)
        {
            var started = DateTime.UtcNow;
            try
            {
                var frame = _capture.Capture(MaxWidth);
                if (frame is { } f && _pc is { connectionState: RTCPeerConnectionState.connected })
                {
                    var encoded = _encoder.EncodeVideo(f.Width, f.Height, f.Bgra, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.VP8);
                    if (encoded is { Length: > 0 })
                        _pc.SendVideo(FrameDurationRtp, encoded);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Frame send to {Teacher} failed.", _teacherConnectionId);
            }

            var elapsed = DateTime.UtcNow - started;
            var wait = interval - elapsed;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
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

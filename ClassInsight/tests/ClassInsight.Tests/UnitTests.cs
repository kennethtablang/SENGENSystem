using System.Net;
using System.Text;
using ClassInsight.Agent.Services;
using ClassInsight.Server.Services;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;
using ClassInsight.Shared.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace ClassInsight.Tests;

/// <summary>Discovery encode/decode (plan §10) and the NFR-SEC-05 "dial only the responder" rule.</summary>
public class DiscoveryCodecTests
{
    private static readonly IPAddress Source = IPAddress.Parse("192.168.1.50");

    private static DiscoveryResponse Response() => new()
    {
        Kind = "response",
        ServerId = "abc",
        RoomName = "Lab 3",
        Host = "192.168.1.50",
        Port = 5080,
    };

    [Fact]
    public void Response_round_trips()
    {
        var decoded = DiscoveryCodec.TryDecodeResponse(DiscoveryCodec.Encode(Response()), Source);
        Assert.NotNull(decoded);
        Assert.Equal("abc", decoded!.ServerId);
        Assert.Equal("Lab 3", decoded.RoomName);
        Assert.Equal("http://192.168.1.50:5080/hub/classroom", decoded.HubUrl);
    }

    [Fact]
    public void Response_host_and_path_are_forced_to_the_datagram_source()
    {
        var forged = Response() with { Host = "evil.example", HubPath = "/steal" };
        var decoded = DiscoveryCodec.TryDecodeResponse(DiscoveryCodec.Encode(forged), Source);
        Assert.Equal("192.168.1.50", decoded!.Host);
        Assert.Equal(Protocol.HubPath, decoded.HubPath);
    }

    [Fact]
    public void Rejects_wrong_magic_version_port_and_own_probe()
    {
        var json = Encoding.UTF8.GetString(DiscoveryCodec.Encode(Response()))[Protocol.DiscoveryMagic.Length..];
        Assert.Null(DiscoveryCodec.TryDecodeResponse(Encoding.UTF8.GetBytes("OTHER/1" + json), Source));
        Assert.Null(DiscoveryCodec.TryDecodeResponse(DiscoveryCodec.Encode(Response() with { ProtocolVersion = 999 }), Source));
        Assert.Null(DiscoveryCodec.TryDecodeResponse(DiscoveryCodec.Encode(Response() with { Port = 0 }), Source));
        Assert.Null(DiscoveryCodec.TryDecodeResponse(DiscoveryCodec.Encode(new DiscoveryProbe()), Source));
        Assert.Null(DiscoveryCodec.TryDecodeResponse(Encoding.UTF8.GetBytes(Protocol.DiscoveryMagic + "{not json"), Source));
    }

    [Fact]
    public void Probe_detection()
    {
        Assert.True(DiscoveryCodec.IsProbe(DiscoveryCodec.Encode(new DiscoveryProbe { Role = "teacher" })));
        Assert.False(DiscoveryCodec.IsProbe(DiscoveryCodec.Encode(Response())));
        Assert.False(DiscoveryCodec.IsProbe(Encoding.UTF8.GetBytes("garbage")));
    }

    [Fact]
    public void Room_name_is_sanitised()
    {
        var decoded = DiscoveryCodec.TryDecodeResponse(
            DiscoveryCodec.Encode(Response() with { RoomName = "Lab\u0007 " + new string('x', 200) }), Source);
        Assert.DoesNotContain('\u0007', decoded!.RoomName);
        Assert.True(decoded.RoomName.Length <= 64);
    }

    [Theory]
    [InlineData("10.0.0.5", "10.0.0.5", 5080)]
    [InlineData("10.0.0.5:6000", "10.0.0.5", 6000)]
    [InlineData("http://10.0.0.5:7000/hub/classroom", "10.0.0.5", 7000)]
    [InlineData("lab-server", "lab-server", 5080)]
    public void Manual_address_parses(string text, string host, int port)
    {
        Assert.True(ManualServer.TryParse(text, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10.0.0.5:99999")]
    [InlineData("bad host name")]
    public void Manual_address_rejects_garbage(string text) => Assert.False(ManualServer.TryParse(text, out _, out _));
}

/// <summary>Roster, pair authorisation and teardown (FR-SEC-04, FR-SRV-04, FR-STREAM-06).</summary>
public class RoomRegistryTests
{
    private static RoomRegistry Room(out string t1, out string t2, out string s1)
    {
        var room = new RoomRegistry();
        t1 = "t1"; t2 = "t2"; s1 = "s1";
        room.AddOrUpdateTeacher(t1, new TeacherInfo());
        room.AddOrUpdateTeacher(t2, new TeacherInfo());
        room.AddOrUpdateStudent(s1, new StudentInfo { MachineName = "PC1" });
        return room;
    }

    [Fact]
    public void Pairs_require_a_registered_teacher_and_student()
    {
        var room = Room(out var t1, out _, out var s1);
        Assert.False(room.AuthorizePair(s1, t1));          // roles reversed
        Assert.False(room.AuthorizePair("ghost", s1));
        Assert.False(room.AuthorizePair(t1, "ghost"));
        Assert.True(room.AuthorizePair(t1, s1));
        Assert.True(room.IsPaired(t1, s1));
        Assert.False(room.IsPaired(s1, t1));
    }

    [Fact]
    public void Viewer_count_tracks_each_teacher()
    {
        var room = Room(out var t1, out var t2, out var s1);
        room.AuthorizePair(t1, s1);
        room.AuthorizePair(t1, s1);                        // re-request is idempotent
        room.AuthorizePair(t2, s1);
        Assert.Equal(2, room.ViewerCount(s1));
        Assert.True(room.RevokePair(t1, s1));
        Assert.False(room.RevokePair(t1, s1));
        Assert.Equal(1, room.ViewerCount(s1));
    }

    [Fact]
    public void Removing_either_end_purges_its_pairs()
    {
        var room = Room(out var t1, out var t2, out var s1);
        room.AuthorizePair(t1, s1);
        room.AuthorizePair(t2, s1);

        Assert.True(room.RemoveTeacher(t1, out var watched));
        Assert.Equal([s1], watched);
        Assert.Equal(1, room.ViewerCount(s1));

        Assert.True(room.RemoveStudent(s1, out _));
        Assert.Equal(0, room.PairCount);
    }

    [Fact]
    public void State_change_reports_only_real_changes()
    {
        var room = Room(out _, out _, out var s1);
        Assert.True(room.SetStudentState(s1, StudentState.Streaming));
        Assert.False(room.SetStudentState(s1, StudentState.Streaming));
        Assert.False(room.SetStudentState("ghost", StudentState.Online));
    }
}

/// <summary>Teacher-code brute-force protection (FR-SEC-06).</summary>
public class TeacherAuthThrottleTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Per_source_lockout_after_free_attempts_and_escalates()
    {
        var clock = new ManualClock();
        var t = new TeacherAuthThrottle(clock);
        for (var i = 0; i < TeacherAuthThrottle.FreeAttempts; i++) t.RegisterFailure("ip");
        Assert.Null(t.GetLockout("ip"));

        t.RegisterFailure("ip");
        var first = t.GetLockout("ip");
        Assert.NotNull(first);

        clock.Now += first!.Value;
        t.RegisterFailure("ip");
        Assert.True(t.GetLockout("ip") > first);           // doubled
        Assert.Null(t.GetLockout("other-ip"));             // other sources unaffected
    }

    [Fact]
    public void Global_lockout_stops_address_rotation()
    {
        var t = new TeacherAuthThrottle(new ManualClock());
        for (var i = 0; i < TeacherAuthThrottle.GlobalBudget; i++) t.RegisterFailure($"ip{i}");
        Assert.NotNull(t.GetLockout("fresh-ip"));
    }

    [Fact]
    public void Success_clears_the_source()
    {
        var t = new TeacherAuthThrottle(new ManualClock());
        for (var i = 0; i <= TeacherAuthThrottle.FreeAttempts; i++) t.RegisterFailure("ip");
        t.RegisterSuccess("ip");
        Assert.Null(t.GetLockout("ip"));
    }
}

/// <summary>Join-code generation and constant-time verification (FR-SEC-02/06, NFR-SEC-04).</summary>
public class ServerIdentityTests
{
    private static ServerIdentity Identity(params (string Key, string? Value)[] settings) =>
        new(new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
                .Build(),
            NullLogger<ServerIdentity>.Instance);

    [Fact]
    public void Generated_code_has_at_least_64_bits()
    {
        var code = ServerIdentity.GenerateCode();
        var symbols = ServerIdentity.NormalizeCode(code);
        Assert.Equal(13, symbols.Length);
        Assert.True(symbols.Length * Math.Log2(32) >= 64);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{5}$", code);
        Assert.NotEqual(code, ServerIdentity.GenerateCode());
    }

    [Fact]
    public void Closed_mode_without_a_code_generates_one_and_fails_closed()
    {
        var id = Identity(("ClassInsight:AllowOpenTeacherMode", "false"));
        Assert.False(id.AllowOpenTeacherMode);
        Assert.False(string.IsNullOrEmpty(id.TeacherCode));
        Assert.False(id.VerifyTeacherCode(null));
        Assert.False(id.VerifyTeacherCode(""));
        Assert.True(id.VerifyTeacherCode(id.TeacherCode));
        Assert.True(id.VerifyTeacherCode(id.TeacherCode.Replace("-", " ").ToLowerInvariant()));
    }

    [Fact]
    public void Configured_code_and_ice_servers_are_read()
    {
        var id = Identity(
            ("ClassInsight:TeacherCode", "lab-42"),
            ("ClassInsight:IceServers:0:Urls", "turn:10.0.0.9:3478"),
            ("ClassInsight:IceServers:0:Username", "u"),
            ("ClassInsight:IceServers:0:Credential", "p"));
        Assert.True(id.VerifyTeacherCode("LAB42"));
        Assert.False(id.VerifyTeacherCode("lab-43"));
        var ice = Assert.Single(id.IceServers);
        Assert.Equal("turn:10.0.0.9:3478", ice.Urls);
    }

    [Fact]
    public void Open_mode_accepts_no_code_at_all()
    {
        var id = Identity(("ClassInsight:AllowOpenTeacherMode", "true"));
        Assert.True(id.AllowOpenTeacherMode);
        Assert.False(id.VerifyTeacherCode("anything"));   // the hub skips verification entirely in open mode
    }
}

/// <summary>Telemetry math (plan §10): bandwidth deltas and session totals.</summary>
public class TelemetryMathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rate_is_delta_over_interval_and_totals_count_from_start()
    {
        var m = new RateMeter();
        var first = m.Sample(1_000_000, 500_000, T0);
        Assert.Equal(0, first.DownloadBytesPerSec);
        Assert.Equal(0, first.TotalDownloadBytes);

        var s = m.Sample(1_200_000, 550_000, T0.AddSeconds(2));
        Assert.Equal(100_000, s.DownloadBytesPerSec);
        Assert.Equal(25_000, s.UploadBytesPerSec);
        Assert.Equal(200_000, s.TotalDownloadBytes);
        Assert.Equal(50_000, s.TotalUploadBytes);
    }

    [Fact]
    public void Counter_reset_rebaselines_without_a_spike_and_keeps_totals()
    {
        var m = new RateMeter();
        m.Sample(1_000, 1_000, T0);
        m.Sample(3_000, 2_000, T0.AddSeconds(1));
        var reset = m.Sample(100, 50, T0.AddSeconds(2));        // adapter reset
        Assert.Equal(0, reset.DownloadBytesPerSec);
        Assert.Equal(2_000, reset.TotalDownloadBytes);
        var after = m.Sample(600, 150, T0.AddSeconds(3));
        Assert.Equal(500, after.DownloadBytesPerSec);
        Assert.Equal(2_500, after.TotalDownloadBytes);
    }

    [Theory]
    [InlineData("github.com/anthropics/x?q=1", "github.com")]
    [InlineData("https://Docs.Google.com/document/d/abc", "docs.google.com")]
    [InlineData("http://localhost:3000/", "localhost")]
    [InlineData("how to cheat on a test", null)]
    [InlineData("chrome://settings", null)]
    [InlineData("", null)]
    public void Browser_probe_reports_host_only(string address, string? host) =>
        Assert.Equal(host, BrowserActivityProbe.ExtractHost(address));
}

/// <summary>Capture sizing: the managed VP8 encoder only accepts whole 16-px macroblocks.</summary>
public class CaptureSizingTests
{
    [Theory]
    [InlineData(1920, 1080, 480, 480, 272)]
    [InlineData(1920, 1080, 1280, 1280, 720)]
    [InlineData(1366, 768, 1280, 1280, 720)]
    [InlineData(1280, 1024, 1280, 1280, 1024)]
    [InlineData(800, 600, 1280, 800, 608)]
    public void Sizes_are_macroblock_aligned(int w, int h, int max, int ew, int eh)
    {
        var (tw, th) = ScreenCapture.TargetSize(w, h, max);
        Assert.Equal((ew, eh), (tw, th));
        Assert.Equal(0, tw % 16);
        Assert.Equal(0, th % 16);
    }

    [Theory]
    [InlineData(1920, 1080, 480, 272, 2)]   // thumbnail: 480x270 mip, 2 rows of tolerance
    [InlineData(1920, 1080, 1280, 720, 0)]  // focus: full size, no mip is big enough
    [InlineData(3840, 2160, 1280, 720, 1)]  // 4K focus: 1920x1080 mip
    [InlineData(3840, 2160, 480, 272, 3)]
    [InlineData(1366, 768, 480, 272, 1)]    // 683x384 is the smallest mip >= target
    public void Dxgi_reads_the_smallest_mip_that_covers_the_target(int w, int h, int tw, int th, int level) =>
        Assert.Equal(level, DxgiDuplicator.LevelFor(w, h, tw, th));

    [Fact]
    public void Every_aligned_size_encodes()
    {
        foreach (var max in new[] { 480, 1280 })
        {
            // One codec per size: it can't be resized (the sender recreates it on a resolution change).
            using var codec = new Vpx.Net.VP8Codec();
            var (w, h) = ScreenCapture.TargetSize(1920, 1080, max);
            var frame = codec.EncodeVideo(w, h, new byte[w * h * 4], VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.VP8);
            Assert.NotEmpty(frame);
        }
    }
}

/// <summary>NFR-SEC-03: the WebRTC stack negotiates DTLS-SRTP, so media is encrypted.</summary>
public class MediaEncryptionTests
{
    [Fact]
    public void Offer_requires_dtls_srtp()
    {
        using var codec = new Vpx.Net.VP8Codec();
        var pc = new RTCPeerConnection(null);
        try
        {
            pc.addTrack(new MediaStreamTrack(codec.SupportedFormats, MediaStreamStatusEnum.SendOnly));
            var sdp = pc.createOffer(null).sdp;
            Assert.Contains("UDP/TLS/RTP/SAVP", sdp);       // SRTP profile, not plain RTP/AVP
            Assert.Contains("a=fingerprint:sha-256", sdp);   // DTLS certificate fingerprint
            Assert.DoesNotContain("RTP/AVP ", sdp);
        }
        finally
        {
            pc.close();
        }
    }
}

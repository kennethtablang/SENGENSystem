using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;

namespace ClassInsight.Agent.Services;

/// <summary>
/// Reports the host of the page open in the foreground browser window (FR-AGENT-06, "may").
///
/// It reads the browser's address bar through Windows UI Automation — the same accessibility
/// interface screen readers use — so no browser extension or proxy is needed. Only the
/// <b>host</b> (e.g. <c>docs.google.com</c>) is reported, never the path or query string: it is
/// enough for a teacher to see "on task / off task" without collecting what was typed or searched
/// (NFR-PRIV). UIA calls can be slow on a busy browser, so probing runs on its own thread and
/// the telemetry loop only ever reads the last result.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BrowserActivityProbe : IDisposable
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore",
    };

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    // Cached address-bar element for the last browser window seen, so steady state is one
    // cheap ValuePattern read per second instead of a tree search.
    private IntPtr _cachedWindow;
    private AutomationElement? _cachedBar;

    private volatile string? _host;

    public BrowserActivityProbe(ILogger logger)
    {
        _logger = logger;
        _thread = new Thread(Run) { IsBackground = true, Name = "ClassInsight browser probe" };
        _thread.Start();
    }

    /// <summary>Host of the foreground browser tab, or null when the foreground app isn't a browser.</summary>
    public string? CurrentHost => _host;

    private void Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _host = Probe();
            }
            catch (Exception ex)
            {
                // A browser closing mid-read throws ElementNotAvailable etc.; just retry next tick.
                _cachedWindow = IntPtr.Zero;
                _cachedBar = null;
                _host = null;
                _logger.LogDebug(ex, "Browser probe failed.");
            }

            if (_cts.Token.WaitHandle.WaitOne(Interval)) break;
        }
    }

    private string? Probe()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        string name;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch
        {
            return null;
        }
        if (!Browsers.Contains(name)) return null;

        if (hwnd != _cachedWindow || _cachedBar is null)
        {
            _cachedWindow = hwnd;
            _cachedBar = FindAddressBar(AutomationElement.FromHandle(hwnd), name);
        }
        if (_cachedBar is null) return null;

        if (!_cachedBar.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)) return null;
        return ExtractHost(((ValuePattern)pattern).Current.Value);
    }

    private static AutomationElement? FindAddressBar(AutomationElement window, string browser)
    {
        if (browser.Equals("firefox", StringComparison.OrdinalIgnoreCase))
        {
            var byId = window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "urlbar-input"));
            if (byId is not null) return byId;
        }

        // Chromium browsers (Chrome, Edge, Brave, Opera, Vivaldi): the omnibox is the first
        // editable text field in the window's toolbar.
        return window.FindFirst(TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true)));
    }

    /// <summary>
    /// Pull just the host out of an address-bar string ("github.com/x", "https://a.b/c?q").
    /// Returns null for anything that doesn't look like an address (e.g. a half-typed search).
    /// </summary>
    public static string? ExtractHost(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.Contains(' ')) return null;

        var candidate = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        if (string.IsNullOrEmpty(uri.Host) || (!uri.Host.Contains('.') && uri.Host != "localhost")) return null;
        return uri.Host.ToLowerInvariant();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}

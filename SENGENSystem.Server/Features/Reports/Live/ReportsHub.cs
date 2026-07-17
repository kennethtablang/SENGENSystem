using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SENGENSystem.Server.Features.Reports.Live
{
    /// <summary>
    /// SignalR hub behind the live Reports &amp; Analytics pages. Clients only listen — the
    /// server pushes a lightweight "reportsChanged" signal whenever report-relevant data
    /// mutates, and the page refetches whichever report it is showing.
    /// </summary>
    [Authorize]
    public sealed class ReportsHub : Hub
    {
    }

    /// <summary>
    /// Fire-and-forget announcer injected into mutation endpoints. Deliberately never throws
    /// and never blocks the request: real-time refresh is best-effort sugar on top of the
    /// committed change.
    /// </summary>
    public sealed class ReportsBroadcaster
    {
        private readonly IHubContext<ReportsHub> _hub;
        private readonly ILogger<ReportsBroadcaster> _logger;

        public ReportsBroadcaster(IHubContext<ReportsHub> hub, ILogger<ReportsBroadcaster> logger)
        {
            _hub = hub;
            _logger = logger;
        }

        /// <summary>Announce that data feeding the given report area changed (e.g. "enlistment").</summary>
        public void Announce(string area)
        {
            _ = AnnounceAsync(area);
        }

        private async Task AnnounceAsync(string area)
        {
            try
            {
                await _hub.Clients.All.SendAsync("reportsChanged", new
                {
                    area,
                    atUtc = DateTime.UtcNow.ToString("o")
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Reports broadcast for {Area} failed.", area);
            }
        }
    }
}

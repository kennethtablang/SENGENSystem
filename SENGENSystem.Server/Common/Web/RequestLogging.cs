using System.Diagnostics;

namespace SENGENSystem.Server.Common.Web
{
    /// <summary>
    /// One structured log line per API request — method, path, status, duration, user, and the
    /// trace id. Before this only schedule generation and the global exception handler logged
    /// anything, so "the Registrar says approvals were slow this morning" or "someone keeps getting
    /// 409s on the board" had no operational record at all: the audit trail records <i>domain</i>
    /// events, and a refused or slow request is usually not one.
    /// <para>
    /// Levels carry meaning so a log filter can do the triage: 5xx is Error (the exception handler
    /// has already logged the stack under the same trace id), a request over
    /// <see cref="SlowThreshold"/> is Warning whatever its status, and everything else is
    /// Information. 4xx stays Information on purpose — a wrong password or a full section is the
    /// system working, and logging them as warnings would bury the real ones.
    /// </para>
    /// <para>
    /// Only <c>/api</c> is logged. Static assets, the SignalR hub's long-lived connection, and the
    /// health probes would otherwise drown the requests a person actually made.
    /// </para>
    /// </summary>
    public static class RequestLogging
    {
        public static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(3);

        // The JWT carries the user's id and email, not a mapped Name claim, so Identity.Name is
        // null for every signed-in request — the email is the label a person can act on.
        private static string UserLabel(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated != true) return "anonymous";
            return context.User.Identity.Name
                ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? context.User.FindFirst("email")?.Value
                ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "authenticated";
        }

        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments("/api"))
                {
                    await next();
                    return;
                }

                var started = Stopwatch.GetTimestamp();
                var threw = false;
                try
                {
                    await next();
                }
                catch
                {
                    // The exception handler sits outside this middleware and has not set the 500
                    // yet — the response still reads 200 here, so record what it is about to become.
                    threw = true;
                    throw;
                }
                finally
                {
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    var status = threw ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("SENGENSystem.Requests");

                    var level = status >= 500 ? LogLevel.Error
                        : elapsed >= SlowThreshold ? LogLevel.Warning
                        : LogLevel.Information;

                    if (logger.IsEnabled(level))
                    {
                        // Path only, never the query string: search terms and tokens travel there.
                        logger.Log(level,
                            "{Method} {Path} → {Status} in {ElapsedMs:F0} ms [user {User}] [trace {TraceId}]",
                            context.Request.Method,
                            context.Request.Path.Value,
                            status,
                            elapsed.TotalMilliseconds,
                            UserLabel(context),
                            context.TraceIdentifier);
                    }
                }
            });
    }
}

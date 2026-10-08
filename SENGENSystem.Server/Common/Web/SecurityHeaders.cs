namespace SENGENSystem.Server.Common.Web
{
    /// <summary>
    /// The response headers a browser needs in order to defend the SPA, none of which were set —
    /// only <c>UseHttpsRedirection</c> was configured, so SEN-GEN shipped with no clickjacking
    /// defence, no MIME-sniffing protection, and a referrer policy that leaked full URLs to any
    /// third party a user navigated to.
    ///
    /// <para>
    /// Deliberately applied to <i>every</i> response rather than only the SPA's HTML: an API
    /// response rendered directly in a tab (a downloaded report, an error body) is just as
    /// sniffable, and a header that is only sometimes present is a header nobody can rely on.
    /// </para>
    /// </summary>
    public static class SecurityHeaders
    {
        /// <summary>
        /// The Content-Security-Policy the served SPA can actually run under.
        ///
        /// <para>
        /// Two relaxations are load-bearing and are called out rather than left to be discovered.
        /// <c>'unsafe-inline'</c> on <c>style-src</c> is required because the app sets inline styles
        /// (FullCalendar computes them, and the subject-colour palette is applied per element);
        /// removing it would need a nonce threaded through every one of those. <c>data:</c> on
        /// <c>img-src</c> is required by the bundled icons and the PSGC dataset's inline assets.
        /// </para>
        ///
        /// <para>
        /// Everything else is closed: no external script, style, font, or frame source, which is
        /// exactly right for an app that has no CDN dependency by design (the Artifact-style
        /// self-contained build). <c>frame-ancestors 'none'</c> is the clickjacking defence and is
        /// the modern replacement for X-Frame-Options, which is sent alongside it for older browsers.
        /// </para>
        /// </summary>
        private const string ContentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'";

        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;

                // Stop the browser second-guessing a declared content type — the vector that turns
                // an uploaded or generated file into executable script.
                headers["X-Content-Type-Options"] = "nosniff";

                // Clickjacking: the app must never be framed. CSP's frame-ancestors is authoritative
                // where supported; X-Frame-Options covers what is left.
                headers["X-Frame-Options"] = "DENY";

                // Send the origin to other sites, the full URL only to ourselves. Without this a
                // student number or registration id sitting in a path leaves with the referrer.
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

                // No feature of this app needs the camera, microphone, or location, and saying so
                // explicitly stops an injected script asking on the user's behalf.
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

                // Swagger UI is dev-only and needs inline script and styles of its own, so the policy
                // would break the very tool used to explore the API. It never ships to production,
                // and every other header above still applies there.
                if (!context.Request.Path.StartsWithSegments("/swagger"))
                {
                    headers["Content-Security-Policy"] = ContentSecurityPolicy;
                }

                await next();
            });
    }
}

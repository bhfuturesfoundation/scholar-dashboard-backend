using System.Diagnostics;

namespace Auth.API.Middleware
{
    public class RequestLoggingMiddleware
    {
        private const long SlowRequestMs = 1000;

        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await _next(context);
            }
            finally
            {
                sw.Stop();

                if (!context.Request.Path.StartsWithSegments("/healthz"))
                {
                    // Path only: query strings carry SignalR access tokens and email links.
                    // Successful, quick requests drop to Debug so the log keeps what someone
                    // would actually look for (errors and slow calls) without paying for the rest.
                    var notable = context.Response.StatusCode >= 400 || sw.ElapsedMilliseconds >= SlowRequestMs;

                    _logger.Log(
                        notable ? LogLevel.Information : LogLevel.Debug,
                        "{Method} {Path} → {StatusCode} in {ElapsedMs}ms",
                        context.Request.Method,
                        context.Request.Path,
                        context.Response.StatusCode,
                        sw.ElapsedMilliseconds);
                }
            }
        }
    }
}

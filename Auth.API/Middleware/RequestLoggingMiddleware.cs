using System.Diagnostics;

namespace Auth.API.Middleware
{
    public class RequestLoggingMiddleware
    {
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

                // Skip health check noise in logs
                if (!context.Request.Path.StartsWithSegments("/healthz"))
                {
                    _logger.LogInformation(
                        "{Method} {Path}{Query} → {StatusCode} in {ElapsedMs}ms",
                        context.Request.Method,
                        context.Request.Path,
                        context.Request.QueryString,
                        context.Response.StatusCode,
                        sw.ElapsedMilliseconds);
                }
            }
        }
    }
}

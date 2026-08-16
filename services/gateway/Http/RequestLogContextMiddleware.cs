namespace BidRisk.Gateway.Http;

public class RequestLogContextMiddleware(RequestDelegate next, ILogger<RequestLogContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        using (logger.BeginScope("CorrelationId:{CorrelationId}", httpContext.TraceIdentifier))
        {
            await next(httpContext);
        }
    }
}

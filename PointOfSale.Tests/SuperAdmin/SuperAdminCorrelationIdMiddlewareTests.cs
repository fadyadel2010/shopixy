using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Middleware;

namespace PointOfSale.Tests.SuperAdmin;

public class SuperAdminCorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Invoke_Prefers_XRequestId_And_Propagates_To_Response()
    {
        var observedTraceIdentifier = string.Empty;
        var middleware = new CorrelationIdMiddleware(context =>
        {
            observedTraceIdentifier = context.TraceIdentifier;
            return Task.CompletedTask;
        }, NullLogger<CorrelationIdMiddleware>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.Request.Headers[CorrelationIdMiddleware.RequestIdHeaderName] = "req-hardening-123";
        httpContext.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeaderName] = "legacy-correlation-id";

        await middleware.Invoke(httpContext);
        await httpContext.Response.StartAsync();

        Assert.Equal("req-hardening-123", observedTraceIdentifier);
        Assert.Equal("req-hardening-123", httpContext.TraceIdentifier);
        Assert.Equal("req-hardening-123", httpContext.Response.Headers[CorrelationIdMiddleware.RequestIdHeaderName]);
        Assert.Equal("req-hardening-123", httpContext.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeaderName]);
    }
}

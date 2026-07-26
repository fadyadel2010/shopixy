using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Infrastructure;

public class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCorrelationId()
    {
        var traceIdentifier = _httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(traceIdentifier))
        {
            return traceIdentifier;
        }

        return System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString();
    }
}

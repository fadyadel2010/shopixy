using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;

namespace PointOfSale.Tests.Middleware;

internal sealed class NotFoundDomainResolver : IDomainResolver
{
    public ValueTask<DomainResolutionResult> ResolveAsync(
        string? normalizedHost,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<DomainResolutionResult>(DomainResolutionResult.NotFound(normalizedHost));
    }
}

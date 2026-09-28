using Microsoft.EntityFrameworkCore.Storage;

namespace PointOfSale.Tests.Infrastructure;

internal static class SharedInMemoryDatabaseRoot
{
    // Root identity participates in EF's internal service-provider cache key. Unique
    // database names still isolate stores while compatible tests reuse this root.
    internal static InMemoryDatabaseRoot Instance { get; } = new();
}

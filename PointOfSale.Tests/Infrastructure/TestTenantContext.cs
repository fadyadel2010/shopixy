using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Infrastructure;

/// <summary>
/// A test stub implementation of <see cref="ITenantContext"/> for use in unit tests.
/// Provides a simple way to inject a specific tenant ID into services that depend on tenant context.
/// </summary>
public sealed class TestTenantContext : ITenantContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="TestTenantContext"/> with the specified tenant ID.
    /// </summary>
    /// <param name="tenantId">The tenant ID to use in tests.</param>
    public TestTenantContext(Guid? tenantId)
    {
        TenantId = tenantId;
    }

    /// <summary>
    /// Gets the tenant ID for the current test context.
    /// </summary>
    public Guid? TenantId { get; }
}

using MassTransit;
using Moq;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Infrastructure;

/// <summary>
/// Base fixture for integration tests that provides common setup for database scope,
/// tenant context, DbContext, and mock dependencies.
/// </summary>
public class IntegrationTestFixture : IDisposable
{
    internal TestDatabaseScope DatabaseScope { get; }
    public Guid TenantId { get; }
    public TestTenantContext TenantContext { get; }
    public ApplicationDbContext Context { get; }
    public Mock<IEventPublisher> PublishEndpointMock { get; }

    public IntegrationTestFixture()
    {
        DatabaseScope = TestDatabaseScope.Open();
        TenantId = Guid.NewGuid();
        TenantContext = new TestTenantContext(TenantId);
        Context = DatabaseScope.CreateDbContext(TenantContext);
        PublishEndpointMock = new Mock<IEventPublisher>();
    }

    public void Dispose()
    {
        Context?.Dispose();
        DatabaseScope?.Dispose();
    }
}

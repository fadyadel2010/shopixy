using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services;

namespace PointOfSale.Tests.AbandonedCart;

public sealed class CheckoutAbandonmentPhase4Tests
{
    [Fact]
    public async Task ScannerCandidateRefreshedAfterSelection_IsNotAbandoned()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var session = new CheckoutSession { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), CartId = Guid.NewGuid(),
            SessionId = "P4QA", RecoveryToken = "test-only", LastActivityAtUtc = DateTime.UtcNow, Status = CheckoutSessionStatus.Active };
        db.CheckoutSessions.Add(session);
        await db.SaveChangesAsync();
        var publisher = new Mock<IEventPublisher>(MockBehavior.Strict);
        var service = new CheckoutSessionService(db, publisher.Object, new CommerceReliabilityTelemetry(), NullLogger<CheckoutSessionService>.Instance);
        await service.MarkAsAbandonedAsync(session.Id);
        db.ChangeTracker.Clear();
        Assert.Equal(CheckoutSessionStatus.Active, (await db.CheckoutSessions.IgnoreQueryFilters().SingleAsync()).Status);
        publisher.VerifyNoOtherCalls();
    }
}

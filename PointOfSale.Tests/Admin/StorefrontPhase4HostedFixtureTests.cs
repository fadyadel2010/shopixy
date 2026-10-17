using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed class StorefrontPhase4HostedFixtureTests
{
    [Phase4HostedFixtureFact]
    public async Task AgeOnlyExplicitOwnedSyntheticCheckout_ThenExecuteCanonicalAbandonment()
    {
        var directory = Environment.GetEnvironmentVariable("STOREFRONT_PHASE4_EVIDENCE")!;
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "browser-prepare.json")));
        var id = manifest.RootElement.GetProperty("sessionId").GetGuid();
        var prefix = manifest.RootElement.GetProperty("prefix").GetString()!;
        Assert.StartsWith("P4QA", prefix);
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var session = await db.CheckoutSessions.IgnoreQueryFilters().SingleAsync(x => x.Id == id && x.Source == prefix && x.CustomerName == prefix);
        Assert.Equal(CheckoutSessionStatus.Active, session.Status);
        session.LastActivityAtUtc = DateTime.UtcNow.AddMinutes(-31);
        await db.SaveChangesAsync();
        var publisher = new Mock<IEventPublisher>(); publisher.SetReturnsDefault(Task.CompletedTask);
        await new CheckoutSessionService(db, publisher.Object, new CommerceReliabilityTelemetry(), NullLogger<CheckoutSessionService>.Instance).MarkAsAbandonedAsync(id);
        db.ChangeTracker.Clear();
        Assert.Equal(CheckoutSessionStatus.Abandoned, (await db.CheckoutSessions.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).Status);
        await File.WriteAllTextAsync(Path.Combine(directory, "hosted-fixture-aged.json"), JsonSerializer.Serialize(new { id, prefix, controlledTimestamp = true, canonicalService = true, status = "Abandoned", publisher = "controlled; no recovery message" }));
    }
}
public sealed class Phase4HostedFixtureFactAttribute : FactAttribute
{
    public Phase4HostedFixtureFactAttribute() { if (Environment.GetEnvironmentVariable("STOREFRONT_PHASE4_HOSTED_AGE") != "1") Skip = "Explicit owned hosted fixture aging opt-in required."; }
}

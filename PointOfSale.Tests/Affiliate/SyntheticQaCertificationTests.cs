using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services;

namespace PointOfSale.Tests.Affiliate;

public sealed class SyntheticQaCertificationTests
{
    private static SyntheticQaOptions Enabled() => new() { Enabled = true, Database = "isolated-test", Server = "127.0.0.1",
        SigningKey = new string('a', 64), TenantId = SyntheticQaCertificationAuthority.AhramStoreId, VerifierIds = [Guid.NewGuid()] };

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void NonDevelopmentRuntimeFailsClosed(string runtime) => Assert.False(SyntheticQaCertificationAuthority.IsRuntimeAllowed(runtime, Enabled()));

    [Fact]
    public void DisabledByDefault_AndMissingIdentityOrKeyFailsClosed()
    {
        Assert.False(SyntheticQaCertificationAuthority.IsRuntimeAllowed("Development", new SyntheticQaOptions()));
        var value = Enabled(); value.Enabled = false;
        Assert.False(SyntheticQaCertificationAuthority.IsRuntimeAllowed("Development", value));
        value = Enabled(); value.Database = "";
        Assert.False(SyntheticQaCertificationAuthority.IsRuntimeAllowed("Development", value));
        value = Enabled(); value.Server = "";
        Assert.False(SyntheticQaCertificationAuthority.IsRuntimeAllowed("Development", value));
        value = Enabled(); value.SigningKey = "short";
        Assert.False(SyntheticQaCertificationAuthority.IsRuntimeAllowed("Development", value));
        value = Enabled(); value.TenantId = Guid.NewGuid();
        Assert.False(SyntheticQaCertificationAuthority.IsRuntimeAllowed("Development", value));
        Assert.True(SyntheticQaCertificationAuthority.IsRuntimeAllowed("Development", Enabled()));
        Assert.True(SyntheticQaCertificationAuthority.IsRuntimeAllowed("Test", Enabled()));
    }

    private static (SyntheticQaRun run, Order order) NewOrder()
    {
        var now = DateTime.UtcNow;
        var run = new SyntheticQaRun { Id = Guid.NewGuid(), TenantId = SyntheticQaCertificationAuthority.AhramStoreId,
            ProductId = SyntheticQaCertificationAuthority.TargetProductId, CustomerId = Guid.NewGuid(), PromotionLinkId = Guid.NewGuid(),
            VerifierId = Guid.NewGuid(), CreatedAtUtc = now.AddMinutes(-2), ExpiresAtUtc = now.AddMinutes(20) };
        var order = new Order { Id = Guid.NewGuid(), TenantId = run.TenantId, OnlineCustomerId = run.CustomerId,
            PromotionLinkId = run.PromotionLinkId, CustomerNotes = SyntheticQaCertificationAuthority.OrderMarker(run.Id),
            CreatedAt = now.AddMinutes(-1), Total = 25m, Currency = "EGP", PaymentMethodCode = "COD", FulfillmentMode = FulfillmentMode.Manual };
        order.Items.Add(new OrderItem { ProductId = run.ProductId, Quantity = 1, Total = 25m });
        return (run, order);
    }

    [Fact]
    public void AdmitsOnlyExactNewRunOwnedOrder()
    {
        var (run, order) = NewOrder();
        SyntheticQaCertificationAuthority.ValidateRunOrder(run, order, DateTime.UtcNow);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("futureRun")]
    [InlineData("historical")]
    [InlineData("futureOrder")]
    [InlineData("tenant")]
    [InlineData("customer")]
    [InlineData("link")]
    [InlineData("product")]
    [InlineData("quantity")]
    [InlineData("marker")]
    [InlineData("carrier")]
    [InlineData("gateway")]
    [InlineData("cancelled")]
    [InlineData("zero")]
    public void RejectsWrongOrExpiredRunTargets(string mutation)
    {
        var (run, order) = NewOrder();
        switch (mutation)
        {
            case "expired": run.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1); break;
            case "futureRun": run.CreatedAtUtc = DateTime.UtcNow.AddHours(1); break;
            case "historical": order.CreatedAt = run.CreatedAtUtc.AddDays(-1); break;
            case "futureOrder": order.CreatedAt = DateTime.UtcNow.AddHours(1); break;
            case "tenant": order.TenantId = Guid.NewGuid(); break;
            case "customer": order.OnlineCustomerId = Guid.NewGuid(); break;
            case "link": order.PromotionLinkId = Guid.NewGuid(); break;
            case "product": order.Items.Single().ProductId = Guid.NewGuid(); break;
            case "quantity": order.Items.Single().Quantity = 2; break;
            case "marker": order.CustomerNotes = "manually delivered"; break;
            case "carrier": order.FulfillmentMode = FulfillmentMode.Carrier; break;
            case "gateway": order.PaymentMethodCode = "STRIPE"; break;
            case "cancelled": order.Status = OrderStatus.Cancelled; break;
            case "zero": order.Total = 0; break;
        }
        Assert.Throws<InvalidOperationException>(() => SyntheticQaCertificationAuthority.ValidateRunOrder(run, order, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("d1102ed5-4256-4cef-80ca-41fe57c6163e", "ORD-83B3-2026-01091")]
    [InlineData("32a82d41-2ae6-47f9-aa4f-cd5d17b1e8d6", "ORD-C43A-2026-01093")]
    [InlineData("40b22f7f-09ac-4c60-b779-f14cf3734fe2", "ORD-83B3-2026-01094")]
    [InlineData("2654c8f2-c859-41ad-b637-212affd7b1b4", "ORD-C43A-2026-01095")]
    public void ProtectedOrdersCanNeverQualify_EvenWithFreshDatesAndValidMarker(string id, string number)
    {
        var (run, order) = NewOrder(); order.Id = Guid.Parse(id);
        Assert.Throws<InvalidOperationException>(() => SyntheticQaCertificationAuthority.ValidateRunOrder(run, order, DateTime.UtcNow));
        order.Id = Guid.NewGuid(); order.OrderNumber = number;
        Assert.Throws<InvalidOperationException>(() => SyntheticQaCertificationAuthority.ValidateRunOrder(run, order, DateTime.UtcNow));
    }

    private static SyntheticQaCertificationAuthority Authority(ApplicationDbContext db, SyntheticQaOptions options, string runtime = "Development")
    {
        var environment = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == runtime);
        var monitor = Mock.Of<IOptionsMonitor<SyntheticQaOptions>>(m => m.CurrentValue == options);
        return new(db, environment, monitor);
    }

    [Fact]
    public async Task NonPostgresDatabaseCannotBeApproved_AndOrdinaryProofIsNotAuthority()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var authority = Authority(db, Enabled());
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.EnsureEnabledAsync());
        Assert.False(await authority.HasDeliveryAuthorityAsync(NewOrder().order, "tracking123", "https://example.invalid/proof", default));
        Assert.False(await authority.HasDeliveryAuthorityAsync(NewOrder().order, "SYNTHETIC-QA:forged", "https://example.invalid/proof", default));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("tenant")]
    [InlineData("shipment")]
    [InlineData("verifier")]
    [InlineData("signature")]
    [InlineData("run")]
    [InlineData("provenance")]
    public void ForgedOrMismatchedEvidenceCannotAuthorizeAnything(string mutation)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var authority = Authority(db, Enabled());
        var (run, order) = NewOrder();
        var evidence = new SyntheticQaEvidence { Id = Guid.NewGuid(), RunId = run.Id, TenantId = run.TenantId,
            OrderId = order.Id, ShipmentId = Guid.NewGuid(), VerifierId = run.VerifierId, Kind = "Collection",
            IssuedAtUtc = DateTime.UtcNow.AddSeconds(-1), Amount = order.Total, Currency = order.Currency, Signature = new string('0', 64) };
        var shipment = evidence.ShipmentId;
        switch (mutation) {
            case "amount": evidence.Amount++; break;
            case "tenant": evidence.TenantId = Guid.NewGuid(); break;
            case "shipment": shipment = Guid.NewGuid(); break;
            case "verifier": evidence.VerifierId = Guid.NewGuid(); break;
            case "run": evidence.RunId = Guid.NewGuid(); break;
            case "provenance": evidence.Provenance = "real delivery"; break;
        }
        Assert.Throws<InvalidOperationException>(() => authority.ValidateEvidence(evidence, order, run, shipment, "Collection"));
    }

    [Fact]
    public async Task AuditRowsAreImmutable_AndConsumptionIsOneWay()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var evidence = new SyntheticQaEvidence { Id = Guid.NewGuid(), Kind = "Delivery" };
        db.SyntheticQaEvidence.Add(evidence); await db.SaveChangesAsync();
        evidence.ConsumedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        evidence.ConsumedAtUtc = DateTime.UtcNow.AddHours(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); evidence = await db.SyntheticQaEvidence.SingleAsync(); evidence.Amount++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); evidence = await db.SyntheticQaEvidence.SingleAsync(); db.Remove(evidence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void CollectionCanPrecedeShipment_ButDeliveryCannotUseMissingShipment()
    {
        // Model-level FK is optional solely because collection precedes confirmation.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var property = db.Model.FindEntityType(typeof(SyntheticQaEvidence))!.FindProperty(nameof(SyntheticQaEvidence.ShipmentId))!;
        Assert.True(property.IsNullable);
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            PointOfSale.Tests.Infrastructure.TestConfiguration.GetSolutionRoot(),
            "PointOfSale.Infrastructure", "Services", "SyntheticQaCertificationAuthority.cs"));
        Assert.Contains("kind == \"Delivery\" || shipmentId != Guid.Empty", source);
        Assert.Contains("kind == \"Delivery\" || evidence.ShipmentId.HasValue", source);
    }
}

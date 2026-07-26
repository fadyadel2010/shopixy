using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Projection;

namespace PointOfSale.Tests.Architecture;

public sealed class OrderRuntimeStage3CertificationTests
{
    [Fact]
    public void OrderStatusAuthority_MustNotExposePublicRuntimeEnforcementBypass()
    {
        var interceptor = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/OrderStatusSaveChangesInterceptor.cs");

        Assert.Contains("public static IDisposable BeginAuthorizedContext()", interceptor, StringComparison.Ordinal);
        Assert.DoesNotContain("public static void DisableEnforcement", interceptor, StringComparison.Ordinal);
        Assert.DoesNotContain("public static void EnableEnforcement", interceptor, StringComparison.Ordinal);
        Assert.DoesNotContain("public static IDisposable WithEnforcementDisabled", interceptor, StringComparison.Ordinal);
        Assert.DoesNotContain("_enforcementEnabled", interceptor, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontProjectionRepair_MustCompareSqlAuthorityIncludingTotals()
    {
        var builder = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Projection/StorefrontOrderProjectionBuilder.cs");
        var repair = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontOrderProjectionRepairService.cs");
        var reconciliation = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontOrderProjectionReconciliationService.cs");

        Assert.Contains("document.Total", builder, StringComparison.Ordinal);
        Assert.Contains("StorefrontOrderProjectionBuilder.ComputeAuthorityFingerprint(existing)", repair, StringComparison.Ordinal);
        Assert.Contains("StorefrontOrderProjectionBuilder.ComputeAuthorityFingerprint(replacement)", repair, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessGuard(order.TenantId, order.Id, sourceTimestamp, sourceVersion)", repair, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessGuard(order.TenantId, order.Id, sourceTimestamp, sourceVersion)", reconciliation, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAuthorityFingerprint_ChangesWhenProjectedTotalDriftsFromSqlTruth()
    {
        var authoritative = CreateProjection(total: 125.50m);
        var drifted = CreateProjection(total: 99.00m);

        Assert.NotEqual(
            StorefrontOrderProjectionBuilder.ComputeAuthorityFingerprint(authoritative),
            StorefrontOrderProjectionBuilder.ComputeAuthorityFingerprint(drifted));
    }

    [Fact]
    public void OrderProjectionConsumer_MustCollapseReplayPayloadsAgainstSqlAuthority()
    {
        var consumer = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs");

        Assert.Contains("OrderPaymentLineageAuthority.LoadCurrentVisibilityAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("paymentAuthority.PaymentStatus", consumer, StringComparison.Ordinal);
        Assert.Contains("DeletePaymentProjectionWithoutSqlAuthorityAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshFacetEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("HandleNoOrderProjectionMutationMatchAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("TryRehydrateOrderProjectionAsync", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void TransactionalOutbox_MustRequireTenantAggregateVersionAndBlockOvertaking()
    {
        var outbox = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs");
        var configuration = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/Configurations/OutboxMessageConfiguration.cs");

        Assert.Contains(".ThenBy(m => m.EventVersion)", outbox, StringComparison.Ordinal);
        Assert.Contains("HasUnprocessedEarlierCriticalAggregateMessageAsync(context, message)", outbox, StringComparison.Ordinal);
        Assert.Contains("EnsureOutboxAuthorityScope(message, eventType, integrationEvent, isCriticalOutboxEvent)", outbox, StringComparison.Ordinal);
        Assert.Contains("@event.EventVersion <= 0", outbox, StringComparison.Ordinal);
        Assert.Contains("message.AggregateId.Value != payloadAggregateId.Value", outbox, StringComparison.Ordinal);
        Assert.Contains("CK_OutboxMessages_CriticalEvents_TenantId_Required", configuration, StringComparison.Ordinal);
        Assert.Contains("CK_OutboxMessages_CriticalEvents_AggregateId_Required", configuration, StringComparison.Ordinal);
        Assert.Contains("CK_OutboxMessages_CriticalEvents_EventVersion_Required", configuration, StringComparison.Ordinal);
        Assert.Contains("IX_OutboxMessages_CriticalAggregateOrdering", configuration, StringComparison.Ordinal);
    }

    private static OrderDocument CreateProjection(decimal total) => new()
    {
        TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        OrderNumber = "ORD-STAGE3-001",
        OnlineCustomerId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        CustomerEmail = "customer@example.test",
        CustomerName = "Stage Three",
        Status = "Pending",
        PaymentStatus = "Pending",
        ShippingStatus = "Pending",
        Subtotal = total,
        Total = total,
        Currency = "EGP",
        PaymentMethodCode = "COD",
        PaymentMethodName = "Cash on Delivery",
        ShippingMethodName = "Standard",
        FulfillmentMode = "Manual",
        CreatedAt = DateTime.UnixEpoch,
        UpdatedAt = DateTime.UnixEpoch,
        Items =
        [
            new OrderItemDocument
            {
                ProductId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                VariantId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                SKU = "SKU-STAGE3",
                ProductName = "Stage 3 Product",
                Quantity = 1,
                UnitPrice = total,
                Total = total
            }
        ]
    };
}

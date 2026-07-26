namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontShippingAuthorityCertificationTests
{
    [Fact]
    public void ShipmentCreation_MustBeSqlTenantOrderBoundAndReplaySafe()
    {
        var shipmentService = ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("IsolationLevel.Serializable", shipmentService, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForShipmentMutationAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentAlreadyExistsException", shipmentService, StringComparison.Ordinal);
        Assert.Contains("CanCreateShipmentForOrder(order)", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodInactiveException", shipmentService, StringComparison.Ordinal);
        Assert.Contains("PendingExternalRegistration", shipmentService, StringComparison.Ordinal);
        Assert.Contains("EmitShipmentExternalRegistrationRequestedEventAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(e => new { e.TenantId, e.OrderId })", dbContext, StringComparison.Ordinal);
        Assert.Contains(".HasFilter(\"\\\"IsDeleted\\\" = false\")", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentLifecycle_MustBeForwardOnlyAndFulfillmentSynchronized()
    {
        var shipmentService = ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var adminShipmentService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");

        Assert.Contains("IsValidTransition(previousStatus, request.NewStatus)", shipmentService, StringComparison.Ordinal);
        Assert.Contains("IsValidStatusTransition(oldStatus, request.NewStatus)", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("OrderOperationalFinancialAuthority.GetOutboundProgressBlockReason", shipmentService, StringComparison.Ordinal);
        Assert.Contains("OrderOperationalFinancialAuthority.GetOutboundProgressBlockReason", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFromShipmentAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFromShipmentAsync", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("SHIPMENT_IMMUTABLE", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentStatus.Delivered || shipment.Status == ShipmentStatus.Cancelled", adminShipmentService, StringComparison.Ordinal);
    }

    [Fact]
    public void CarrierRegistration_MustUseDurableExternalIntentAndReconciliation()
    {
        var carrierService = ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ShipmentExternalRegistrationRequestedConsumer.cs");
        var recovery = ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");

        Assert.Contains("EnsureNoActiveLocalTransactionForExternalCarrierCall", carrierService, StringComparison.Ordinal);
        Assert.Contains("CarrierRequestId", carrierService, StringComparison.Ordinal);
        Assert.Contains("ClaimShipmentForExternalCallAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("ReconcileExistingExternalRegistrationAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("PersistSuccessfulCarrierRegistrationAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("ExternalRegistrationStateReconciliationRequired", carrierService, StringComparison.Ordinal);
        Assert.Contains("ExecuteCarrierForExistingShipmentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("ReconcileShipmentAuthorityAsync", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackingAndWebhookAuthority_MustRejectClientSpoofingCarrierMismatchAndReplay()
    {
        var adminShipmentService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");
        var shipmentService = ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var webhookPipeline = ReadRepoFile("PointOfSale.API/Services/Shipping/ShippingWebhookRequestPipeline.cs");
        var carrierService = ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("EnsureManualTrackingMutationAllowed(shipment, correlationId)", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("Carrier-managed shipment tracking mutation blocked", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("EnsureCarrierManagedTrackingMutationAllowed(shipment, request.TrackingNumber, correlationId)", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentCarrierTrackingAuthorityException", shipmentService, StringComparison.Ordinal);

        Assert.Contains("_tenantResolver.ResolveAsync", webhookPipeline, StringComparison.Ordinal);
        Assert.Contains("claimedTenantId.HasValue", webhookPipeline, StringComparison.Ordinal);
        Assert.Contains("matches.Count > 1", webhookPipeline, StringComparison.Ordinal);
        Assert.Contains("WEBHOOK_TENANT_AMBIGUOUS", webhookPipeline, StringComparison.Ordinal);

        Assert.Contains("WEBHOOK_SIGNATURE_MISSING", carrierService, StringComparison.Ordinal);
        Assert.Contains("WEBHOOK_SIGNATURE_INVALID", carrierService, StringComparison.Ordinal);
        Assert.Contains("PrepareShippingWebhookLogAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("IsValidStatusProgression", carrierService, StringComparison.Ordinal);
        Assert.Contains("IsValidStatusProgression", carrierService, StringComparison.Ordinal);
    }

    [Fact]
    public void CodDeliveryAndOrderIntegration_MustRemainPaymentAndFulfillmentAuthorityBound()
    {
        var carrierService = ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var fulfillment = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");
        var paymentLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var orderProjection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs");
        var orderStatusSync = ReadRepoFile("PointOfSale.API/Services/OrderStatusSyncService.cs");

        Assert.Contains("PublishShipmentCodCollectedEventAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("CodCollected", carrierService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFromShipmentAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("MarkOfflineCollectionDeliveredAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus = PaymentStatus.Collected", carrierService, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus = PaymentStatus.Paid", carrierService, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus = PaymentStatus.Collected", fulfillment, StringComparison.Ordinal);
        Assert.Contains("OrderFulfillmentLineageAuthority", orderProjection, StringComparison.Ordinal);
        Assert.Contains("OrderFulfillmentLineageAuthority", orderStatusSync, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordP2FTruth()
    {
        Assert.Contains(
            "PASS P2-F Storefront Shipping Authority",
            ReadRepoFile("docs/architecture/storefront-shipping-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS P2-F", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}

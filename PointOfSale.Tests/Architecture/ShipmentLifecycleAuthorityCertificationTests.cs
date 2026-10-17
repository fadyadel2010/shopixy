using PointOfSale.Core.Enums;
using PointOfSale.Core.Policies;

namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentLifecycleAuthorityCertificationTests
{
    [Theory]
    [InlineData(ShipmentStatus.PendingExternalRegistration, ShipmentStatus.ReadyForPickup)]
    [InlineData(ShipmentStatus.PendingExternalRegistration, ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.PendingExternalRegistration, ShipmentStatus.Failed)]
    [InlineData(ShipmentStatus.Pending, ShipmentStatus.ReadyForPickup)]
    [InlineData(ShipmentStatus.Pending, ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.ReadyForPickup, ShipmentStatus.PickedUp)]
    [InlineData(ShipmentStatus.ReadyForPickup, ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.PickedUp, ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery)]
    [InlineData(ShipmentStatus.InTransit, ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.InTransit, ShipmentStatus.Failed)]
    [InlineData(ShipmentStatus.InTransit, ShipmentStatus.Returned)]
    [InlineData(ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.OutForDelivery, ShipmentStatus.Failed)]
    [InlineData(ShipmentStatus.OutForDelivery, ShipmentStatus.Returned)]
    [InlineData(ShipmentStatus.Delivered, ShipmentStatus.Returned)]
    public void CanonicalLifecycle_AllowsOnlyDeclaredTransitions(ShipmentStatus current, ShipmentStatus requested)
    {
        Assert.True(ShipmentLifecycleTransitionPolicy.CanTransition(current, requested));
    }

    [Theory]
    [InlineData(ShipmentStatus.PendingExternalRegistration, ShipmentStatus.PickedUp)]
    [InlineData(ShipmentStatus.Pending, ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.ReadyForPickup, ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.PickedUp, ShipmentStatus.OutForDelivery)]
    [InlineData(ShipmentStatus.OutForDelivery, ShipmentStatus.PickedUp)]
    [InlineData(ShipmentStatus.InTransit, ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.Failed, ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.Delivered, ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Returned, ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.Cancelled, ShipmentStatus.ReadyForPickup)]
    public void CanonicalLifecycle_RejectsSkippedAndTerminalOverwriteTransitions(ShipmentStatus current, ShipmentStatus requested)
    {
        Assert.False(ShipmentLifecycleTransitionPolicy.CanTransition(current, requested));
    }

    [Fact]
    public void EveryShipmentRuntimeWriter_UsesTheSharedCanonicalPolicy()
    {
        var policy = CertificationSource.ReadRepoFile("PointOfSale.Core/Policies/ShipmentLifecycleTransitionPolicy.cs");
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var adminShipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");
        var carrierShipmentService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("ShipmentStatus.PendingExternalRegistration, ShipmentStatus.Failed", policy, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition(from, to)", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition(current, requested)", adminShipmentService, StringComparison.Ordinal);
        var cancellation = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/ShipmentCancellationService.cs");
        Assert.Contains("authority.CancelForAdminAsync", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition(shipment.Status, ShipmentStatus.Cancelled)", cancellation, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition(currentStatus, newStatus)", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("_cancellation.CancelForOrderAsync", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition(previousStatus, result.InitialStatus)", carrierShipmentService, StringComparison.Ordinal);
    }

    [Fact]
    public void PostDeliveryReturns_MustRemainReachableThroughAllShipmentMutationPaths()
    {
        var policy = CertificationSource.ReadRepoFile("PointOfSale.Core/Policies/ShipmentLifecycleTransitionPolicy.cs");
        var adminShipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");
        var carrierShipmentService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("(ShipmentStatus.Delivered, ShipmentStatus.Returned) => true", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment.Status is ShipmentStatus.Delivered or ShipmentStatus.Cancelled", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("case ShipmentStatus.Returned:", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("new ShipmentReturnedEvent", adminShipmentService, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment.Status == ShipmentStatus.Delivered ||", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("Delivered remains open to carrier return evidence.", carrierShipmentService, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnedShipmentStatus_MustNotCollapseToCancelledFulfillmentSemantics()
    {
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var adminShipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");
        var carrierShipmentService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        foreach (var source in new[] { shipmentService, adminShipmentService, carrierShipmentService })
        {
            Assert.Contains("ShipmentStatus.Returned => FulfillmentStatus.Returned", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ShipmentStatus.Returned => FulfillmentStatus.Cancelled", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PersistedShipmentStatuses_MustRemainTheCanonicalBackendStateSet()
    {
        var enumSource = CertificationSource.ReadRepoFile("PointOfSale.Core/Enums/ShipmentStatus.cs");

        Assert.Contains("PendingExternalRegistration", enumSource, StringComparison.Ordinal);
        Assert.Contains("ReadyForPickup", enumSource, StringComparison.Ordinal);
        Assert.Contains("OutForDelivery", enumSource, StringComparison.Ordinal);
        Assert.Contains("Failed", enumSource, StringComparison.Ordinal);
        Assert.Contains("Returned", enumSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReturnRequested", enumSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Completed", enumSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadyToShip", enumSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AwaitingPickup", enumSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryFailed", enumSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateAndReplayTransitions_MustNotPersistSecondLifecycleMutation()
    {
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var adminShipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");
        var carrierShipmentService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("if (previousStatus == request.NewStatus)", shipmentService, StringComparison.Ordinal);
        Assert.Contains("await transaction.RollbackAsync(cancellationToken);", shipmentService, StringComparison.Ordinal);
        Assert.Contains("await StoreIdempotencyResultAsync(idempotencyKey, noOpResponse, correlationId);", shipmentService, StringComparison.Ordinal);

        Assert.Contains("if (oldStatus == request.NewStatus)", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("await transaction.RollbackAsync(cancellationToken);", adminShipmentService, StringComparison.Ordinal);

        Assert.Contains("var statusChanged = shipment.Status != trackingResult.CurrentStatus;", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("var statusChanged = shipment.Status != parseResult.Status;", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("TryResolveShippingWebhookConflictAsync", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("CreateDuplicateWebhookResult", carrierShipmentService, StringComparison.Ordinal);
    }

    [Fact]
    public void CarrierStatusMapping_MustRemainEvidenceBeforeLifecycleAdmission()
    {
        var adapterContract = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs");
        var carrierShipmentService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("public ShipmentStatus InitialStatus { get; set; } = ShipmentStatus.ReadyForPickup;", adapterContract, StringComparison.Ordinal);
        Assert.Contains("public ShipmentStatus CurrentStatus { get; set; }", adapterContract, StringComparison.Ordinal);
        Assert.Contains("public required ShipmentStatus Status { get; set; }", adapterContract, StringComparison.Ordinal);

        Assert.Contains("CARRIER_STATUS_MAPPING_INVALID", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_STATUS_TRANSITION_INVALID", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("adapter.ParseWebhook(payload, null, null)", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("ValidateWebhookTrust(payload, signature, sourceIp", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("PrepareShippingWebhookLogAsync(", carrierShipmentService, StringComparison.Ordinal);
        Assert.Contains("WEBHOOK_TENANT_AMBIGUOUS", carrierShipmentService, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCacheAndProjection_MustRemainPresentationOnly()
    {
        var shipmentsApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js");
        var actionAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.js");
        var storefrontStatus = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/hooks/useShippingApi.js");
        var storefrontTracking = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/order/ShipmentTracking.tsx");
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");

        Assert.Contains("Shipment status transitions require current backend fulfillment action authority.", shipmentsApi, StringComparison.Ordinal);
        Assert.Contains("requireShipmentActionAuthority", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("hasShipmentActionAuthority", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("getStatusDisplay(status)", storefrontStatus, StringComparison.Ordinal);
        Assert.Contains("Read-only display using order timeline data.", storefrontTracking, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", shipmentsApi, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", shipmentsApi, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment.Status = ShipmentStatus", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordShipmentLifecycleAuthorityTruth()
    {
        // The dedicated findings are the historical certification evidence;
        // general context files are not required to duplicate its verdict marker.
        var findings = CertificationSource.ReadRepoFile("docs/architecture/shipment-lifecycle-authority-certification-findings.md");
        foreach (var evidence in new[]
                 {
                     "PASS SF-SHP5 Shipment Lifecycle Authority Certification Findings",
                     "Shipment Lifecycle owner: backend SQL shipment aggregate plus `ShipmentLifecycleTransitionPolicy`.",
                     "Tenant Scope owner: trusted backend tenant context and tenant-scoped SQL predicates.",
                     "Can frontend fabricate Shipment Lifecycle? No.",
                     "Same-status direct/admin replays are no-op, carrier duplicates collapse, and recovery recomposes SQL truth.",
                     "Carrier evidence must resolve an existing shipment, pass trust/idempotency, and pass backend transition policy."
                 })
        {
            Assert.Contains(evidence, findings, StringComparison.Ordinal);
        }

        // Cancellation's stronger, current boundary supersedes the older
        // policy-only description without inventing another historical PASS.
        var cancellation = CertificationSource.ReadRepoFile(".ai/reports/SHIPPING_CARRIER_CANCELLATION_SAFETY_CERTIFICATION.md");
        foreach (var evidence in new[]
                 {
                     "IShipmentCancellationService.CancelForAdminAsync",
                     "IShipmentCancellationService.CancelForOrderAsync",
                     "Only typed Confirmed with exact tracking target, evidence reference",
                     "Unknown → ReconciliationRequired",
                     "unique `(TenantId, ShipmentId)`",
                     "Canonical order cancellation/payment cancellation/refund/inventory release remains",
                     "WaitingRegistrationClaim_SeesCancellationCommittedWhileItsSqlSnapshotWasWaiting",
                     "WaitingCancellation_SeesRegistrationCommittedWhileWaitingForShipmentLock"
                 })
        {
            Assert.Contains(evidence, cancellation, StringComparison.Ordinal);
        }
    }
}

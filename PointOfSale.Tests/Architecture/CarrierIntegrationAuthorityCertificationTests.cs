namespace PointOfSale.Tests.Architecture;

public sealed class CarrierIntegrationAuthorityCertificationTests
{
    [Fact]
    public void CarrierProviders_MustBeRegisteredAdaptersBehindBackendRegistryOnly()
    {
        var carrierCodes = CertificationSource.ReadRepoFile("PointOfSale.Core/Constants/CarrierCodes.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var registry = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs");

        Assert.Contains("public const string Manual = \"manual\";", carrierCodes, StringComparison.Ordinal);
        Assert.Contains("public const string Bosta = \"bosta\";", carrierCodes, StringComparison.Ordinal);
        Assert.Contains("public const string Aramex = \"aramex\";", carrierCodes, StringComparison.Ordinal);
        Assert.Contains("public const string Mylerz = \"mylerz\";", carrierCodes, StringComparison.Ordinal);
        Assert.Contains("public const string Dhl = \"dhl\";", carrierCodes, StringComparison.Ordinal);
        Assert.Contains("public const string FedEx = \"fedex\";", carrierCodes, StringComparison.Ordinal);
        Assert.DoesNotContain("ups", carrierCodes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shipblu", carrierCodes, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("AddSingleton<PointOfSale.Infrastructure.Shipping.IShippingCarrierRegistry", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.ManualCarrierDefinition", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.BostaAdapter", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.AramexAdapter", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.MylerzAdapter", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.DhlAdapter", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.FedExAdapter", program, StringComparison.Ordinal);

        Assert.Contains("IEnumerable<IShippingCarrierAdapter> adapters", registry, StringComparison.Ordinal);
        Assert.Contains("Duplicate carrier adapter registration", registry, StringComparison.Ordinal);
        Assert.Contains("Keeping first", registry, StringComparison.Ordinal);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void CarrierCreation_MustUseBackendOrderShippingMethodConfigurationAndSqlPersistence()
    {
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("method.Id == order.ShippingMethodId.Value", carrierService, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", carrierService, StringComparison.Ordinal);
        Assert.Contains("var authoritativeCarrierCode = CarrierCodes.Normalize(shippingMethod.CarrierCode);", carrierService, StringComparison.Ordinal);
        Assert.Contains("ORDER_SHIPPING_METHOD_CARRIER_MISMATCH", carrierService, StringComparison.Ordinal);
        Assert.Contains("var authoritativeServiceLevel = shippingMethod.ServiceLevel;", carrierService, StringComparison.Ordinal);
        Assert.Contains("c.TenantId == tenantId &&", carrierService, StringComparison.Ordinal);
        Assert.Contains("c.CarrierCode == authoritativeCarrierCode", carrierService, StringComparison.Ordinal);
        Assert.Contains("BuildCarrierRequestId(tenantId, shipmentId)", carrierService, StringComparison.Ordinal);
        Assert.Contains("Persisted idempotency claim", carrierService, StringComparison.Ordinal);
        Assert.Contains("EnsureNoActiveLocalTransactionForExternalCarrierCall", carrierService, StringComparison.Ordinal);
        Assert.Contains("BeginCarrierPersistenceTransactionAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", carrierService, StringComparison.Ordinal);
    }

    [Fact]
    public void CarrierResponseAdmission_MustRejectForgedTrackingLabelsMetadataAndStatusBeforeSqlTruth()
    {
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("ValidateCarrierCreateResultForPersistence(result)", carrierService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_SHIPMENT_ID_MISSING", carrierService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_TRACKING_NUMBER_INVALID", carrierService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_TRACKING_URL_INVALID", carrierService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_LABEL_URL_INVALID", carrierService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_LABEL_DATA_INVALID", carrierService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_LABEL_FORMAT_INVALID", carrierService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_METADATA_INVALID", carrierService, StringComparison.Ordinal);
        Assert.Contains("CARRIER_STATUS_MAPPING_INVALID", carrierService, StringComparison.Ordinal);
        Assert.Contains("Convert.FromBase64String(value.Trim())", carrierService, StringComparison.Ordinal);
        Assert.Contains("JsonDocument.Parse(value)", carrierService, StringComparison.Ordinal);
        Assert.Contains("ShipmentStatus.ReadyForPickup", carrierService, StringComparison.Ordinal);
        Assert.Contains("ApplySuccessfulCarrierRegistration", carrierService, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhooksCallbacksAndTracking_MustResolveOneConfiguredTenantCarrierShipment()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShippingWebhooksController.cs");
        var pipeline = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShippingWebhookRequestPipeline.cs");
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("[AllowAnonymousTenant]", controller, StringComparison.Ordinal);
        Assert.Contains("WebhookContentTypeValidationFilter", controller, StringComparison.Ordinal);
        Assert.Contains("WebhookRateLimiterPolicy.PolicyName", controller, StringComparison.Ordinal);
        Assert.Contains("WebhookRequestBodyReader.ReadJsonBodyAsync", controller, StringComparison.Ordinal);
        Assert.Contains("_carrierRegistry.IsCarrierSupported(carrierCode)", controller, StringComparison.Ordinal);
        Assert.Contains("_requestPipeline.PrepareAsync", controller, StringComparison.Ordinal);

        Assert.Contains("_tenantContext.SetTenantId(resolution.TenantId);", pipeline, StringComparison.Ordinal);
        Assert.Contains("s.CarrierCode == carrierCode && s.TrackingNumber == trackingNumber && !s.IsDeleted", pipeline, StringComparison.Ordinal);
        Assert.Contains("WEBHOOK_TENANT_AMBIGUOUS", pipeline, StringComparison.Ordinal);
        Assert.Contains("manual_carrier_not_allowed", pipeline, StringComparison.Ordinal);

        Assert.Contains("ValidateWebhookTrust(payload, signature, sourceIp", carrierService, StringComparison.Ordinal);
        Assert.Contains("carrierConfig.SupportsWebhooks", carrierService, StringComparison.Ordinal);
        Assert.Contains("PrepareShippingWebhookLogAsync(", carrierService, StringComparison.Ordinal);
        Assert.Contains("TryResolveShippingWebhookConflictAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", carrierService, StringComparison.Ordinal);
        Assert.Contains("!shipment.Order.AllowsCarrierWebhooks", carrierService, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCachesAndPresentation_MustNotBecomeCarrierAuthority()
    {
        var setupApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/hooks/useShippingSetupApi.js");
        var shipmentsApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js");
        var storefrontTracking = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/order/ShipmentTracking.tsx");
        var adminShipmentActions = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/components/ShipmentActions.jsx");

        Assert.DoesNotContain("localStorage", setupApi, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", setupApi, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", shipmentsApi, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", shipmentsApi, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", storefrontTracking, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", storefrontTracking, StringComparison.Ordinal);
        Assert.Contains("Read-only display using order timeline data.", storefrontTracking, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", storefrontTracking, StringComparison.Ordinal);
        Assert.Contains("refetchActionAuthority", adminShipmentActions, StringComparison.Ordinal);
        Assert.Contains("hasShipmentActionAuthority(currentAuthority, shipment, 'cancel')", adminShipmentActions, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordCarrierIntegrationTruth()
    {
        Assert.Contains(
            "PASS SF-SHP4 Carrier Integration Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/carrier-integration-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-SHP4", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}

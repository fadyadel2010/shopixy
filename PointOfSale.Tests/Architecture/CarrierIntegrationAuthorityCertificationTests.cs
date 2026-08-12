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

        Assert.DoesNotContain("_dbContext.ShippingMethods", carrierService, StringComparison.Ordinal);
        Assert.Contains("var authoritativeCarrierCode = CarrierCodes.Normalize(order.FulfillmentCarrierCode);", carrierService, StringComparison.Ordinal);
        Assert.Contains("ORDER_SHIPPING_METHOD_CARRIER_MISMATCH", carrierService, StringComparison.Ordinal);
        Assert.Contains("var authoritativeServiceLevel = order.ShippingServiceLevel.Trim().ToLowerInvariant();", carrierService, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = order.ShippingMethodId.Value", carrierService, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodName = order.ShippingMethodName", carrierService, StringComparison.Ordinal);
        Assert.Contains("_carrierIntegrationAuthority.ResolveCredentialsAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("CarrierIntegrationPurpose.ShipmentCreation", carrierService, StringComparison.Ordinal);
        Assert.Contains("CarrierIntegrationPurpose.ExternalRegistration", carrierService, StringComparison.Ordinal);
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

        Assert.Contains("_carrierIntegrationAuthority.ResolveWebhookTrustAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("ValidateWebhookTrust(payload, signature, sourceIp", carrierService, StringComparison.Ordinal);
        Assert.Contains("webhookTrust.ValidationMethod", carrierService, StringComparison.Ordinal);
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
    public void Step2K_RuntimeAndAdmissionCallers_MustUseSingleCarrierIntegrationAuthority()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierIntegrationAuthority.cs");
        var quote = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/ShippingQuoteService.cs");
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var storefront = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");
        var warehouseResolution = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs");
        var commerceHub = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");

        Assert.Contains("AddScoped<PointOfSale.Infrastructure.Shipping.ICarrierIntegrationAuthority", program, StringComparison.Ordinal);
        Assert.Contains("ICarrierCredentialEncryptionService", authority, StringComparison.Ordinal);
        Assert.Contains("_credentialEncryption.Decrypt(config.ApiKey)", authority, StringComparison.Ordinal);
        Assert.Contains("_credentialEncryption.Decrypt(config.ApiSecret)", authority, StringComparison.Ordinal);
        Assert.Contains("ResolveCredentialsAsync(", authority, StringComparison.Ordinal);
        Assert.Contains("GetIntegrationReadinessAsync(", authority, StringComparison.Ordinal);
        Assert.Contains("ValidateAndStoreIntegrationAsync(", authority, StringComparison.Ordinal);
        Assert.Contains("ResolveWebhookTrustAsync(", authority, StringComparison.Ordinal);

        Assert.Contains("_carrierIntegrationAuthority.ListRuntimeConfigurationsAsync", quote, StringComparison.Ordinal);
        Assert.Contains("_carrierIntegrationAuthority.ResolveCredentialsAsync", quote, StringComparison.Ordinal);
        Assert.DoesNotContain("Set<CarrierConfiguration>", quote, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildCredentials", quote, StringComparison.Ordinal);

        Assert.DoesNotContain("Set<CarrierConfiguration>", shipment, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierConfigurations", shipment, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildCredentials", shipment, StringComparison.Ordinal);
        Assert.Contains("CarrierIntegrationPurpose.Cancellation", shipment, StringComparison.Ordinal);
        Assert.Contains("CarrierIntegrationPurpose.Tracking", shipment, StringComparison.Ordinal);

        foreach (var source in new[] { shipmentService, checkout, storefront, warehouseResolution, commerceHub })
        {
            Assert.Contains("_carrierIntegrationAuthority.GetIntegrationReadinessAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Set<CarrierConfiguration>", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CarrierWarehouseAssignments", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CarrierAccounts", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Step2K_Capabilities_MustRepresentActualCarrierRuntimeRequirements()
    {
        var capabilities = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierCapabilityRegistry.cs");

        Assert.Contains("CarrierCodes.Bosta", capabilities, StringComparison.Ordinal);
        Assert.Contains("\"API key / token\"", capabilities, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierCodes.Bosta => new CarrierCapabilityDescriptor(\r\n                CarrierCodes.Bosta,\r\n                displayName,\r\n                CarrierProductionCapability.Partial,\r\n                new[]\r\n                {\r\n                    new CarrierCredentialFieldDescriptor(\"apiKey\", \"API key / token\", true, true, \"ApiKey\"),\r\n                    new CarrierCredentialFieldDescriptor(\"apiSecret\"", capabilities, StringComparison.Ordinal);

        Assert.Contains("\"ExtraConfig.AccountPin\"", capabilities, StringComparison.Ordinal);
        Assert.Contains("\"ExtraConfig.AccountEntity\"", capabilities, StringComparison.Ordinal);
        Assert.Contains("\"ExtraConfig.AccountCountryCode\"", capabilities, StringComparison.Ordinal);
        Assert.Contains("CarrierConnectionTestMode.LocalOnly", capabilities, StringComparison.Ordinal);
        Assert.Contains("CarrierProductionCapability.Unavailable", capabilities, StringComparison.Ordinal);
        Assert.Contains("CarrierProductionCapability.NotCertified", capabilities, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingMethodsStep2B_MustUseCarrierCodeAndBackendRegistryForCarrierIdentity()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShippingMethodsController.cs");
        var shippingMethod = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/ShippingMethod.cs");
        var methodsTab = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx");
        var workspace = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx");

        Assert.Contains("ValidateCarrierCode(dto.CarrierCode)", controller, StringComparison.Ordinal);
        Assert.Contains("CarrierName = ResolveCarrierDisplayName(normalizedCarrierCode)", controller, StringComparison.Ordinal);
        Assert.Contains("ServiceLevel = normalizedServiceLevel", controller, StringComparison.Ordinal);
        Assert.Contains("fetchSupportedCarriers", methodsTab, StringComparison.Ordinal);
        Assert.Contains("carrierCode: normalizedCarrierCode", methodsTab, StringComparison.Ordinal);
        Assert.DoesNotContain("value={formData.carrierName}", methodsTab, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierAccountId", shippingMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierWarehouseAssignmentId", shippingMethod, StringComparison.Ordinal);
        Assert.Contains("FulfillmentWarehouseId", shippingMethod, StringComparison.Ordinal);
        Assert.Contains("accountRequired !== false", workspace, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingMethodsStep2L_MustSuppressMerchantFacingCarrierWarehouseAssignment()
    {
        var shippingMethod = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/ShippingMethod.cs");
        var runtimeEntities = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs");
        var card = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx");
        var workspace = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx");
        var apiBoundary = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping-administration/shippingAdministrationApi.js");
        var runtime = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs");

        Assert.DoesNotContain("public Guid WarehouseId", shippingMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("WarehouseAssignmentId", shippingMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierWarehouseAssignmentId", shippingMethod, StringComparison.Ordinal);
        Assert.Contains("public sealed class CarrierWarehouseAssignment", runtimeEntities, StringComparison.Ordinal);
        Assert.Contains("public Guid CarrierAccountId", runtimeEntities, StringComparison.Ordinal);
        Assert.Contains("public Guid WarehouseId", runtimeEntities, StringComparison.Ordinal);

        Assert.Contains("fetchWarehouses", apiBoundary, StringComparison.Ordinal);
        Assert.Contains("api.get('/warehouses'", apiBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("getWarehouseDisplayName", card + workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("assignCarrierWarehouse", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Origin readiness published", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Readiness evidence reference", card, StringComparison.Ordinal);
        Assert.DoesNotContain("warehouses' && <WarehousesTab", workspace, StringComparison.Ordinal);
        Assert.Contains("warehouse-assignments/${warehouseId}", apiBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder=\"Warehouse ID\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Warehouse Assignment", card, StringComparison.Ordinal);

        Assert.Contains("MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED", runtime, StringComparison.Ordinal);
        Assert.Contains("warehouse.TenantId == tenantId", runtime, StringComparison.Ordinal);
        Assert.Contains("warehouse.IsActive", runtime, StringComparison.Ordinal);
        Assert.Contains("!warehouse.IsDeleted", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordCarrierIntegrationTruth()
    {
        Assert.Contains(
            "Option C - Single Carrier Integration Authority Provider",
            CertificationSource.ReadRepoFile("docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2K_EXTERNAL_CARRIER_AUTHORITY_DESIGN_2026-08-11.md"),
            StringComparison.Ordinal);
    }
}

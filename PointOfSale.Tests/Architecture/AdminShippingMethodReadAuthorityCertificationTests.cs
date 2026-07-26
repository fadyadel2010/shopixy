namespace PointOfSale.Tests.Architecture;

public sealed class AdminShippingMethodReadAuthorityCertificationTests
{
    [Fact]
    public void ShippingMethodReadRuntime_MustRemainBackendOwnedAndTenantScoped()
    {
        var adminController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShippingMethodsController.cs");
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs");
        var storefrontService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");
        var storefrontDto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs");
        var projectionConsumer = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs");
        var adminPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/ShippingMethods.jsx");
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutTypes = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var shippingStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx");
        var reviewStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");

        Assert.Contains("[Route(\"api/[controller]\")]", adminController, StringComparison.Ordinal);
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", adminController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", adminController, StringComparison.Ordinal);
        Assert.Contains(".Where(s => s.TenantId == tenantId && !s.IsDeleted)", adminController, StringComparison.Ordinal);
        Assert.Contains("query = query.Where(s => s.IsActive);", adminController, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(s => s.SortOrder)", adminController, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && !s.IsDeleted)", adminController, StringComparison.Ordinal);
        Assert.Contains("CarrierCode = s.CarrierCode", adminController, StringComparison.Ordinal);
        Assert.Contains("AvailableCountries = s.AvailableCountries", adminController, StringComparison.Ordinal);
        Assert.Contains("Response.Headers[\"Cache-Control\"] = \"no-store, no-cache, max-age=0\"", adminController, StringComparison.Ordinal);

        Assert.Contains("[Route(\"api/storefront/shipping-methods\")]", storefrontController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantHeader(traceId)", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthenticatedTenantClaim", storefrontController, StringComparison.Ordinal);
        Assert.Contains("_cartService.GetCartAsync(", storefrontController, StringComparison.Ordinal);
        Assert.Contains("cart.Subtotal", storefrontController, StringComparison.Ordinal);
        Assert.Contains("cart.TotalWeight", storefrontController, StringComparison.Ordinal);
        Assert.Contains("_shippingMethodService.GetShippingMethodsAsync(", storefrontController, StringComparison.Ordinal);

        Assert.Contains("TryValidateCachedTenantScope(tenantId, cached", storefrontService, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", storefrontService, StringComparison.Ordinal);
        Assert.Contains("GetActiveShippingMethodsAsync(tenantId", storefrontService, StringComparison.Ordinal);
        Assert.Contains("TenantId = doc.TenantId", storefrontService, StringComparison.Ordinal);
        Assert.Contains("ApplyCalculatedPrices(shippingMethods, cartSubtotal, totalWeight)", storefrontService, StringComparison.Ordinal);
        Assert.Contains("shippingMethod.Price = _shippingCalculationService.Calculate(", storefrontService, StringComparison.Ordinal);

        Assert.Contains("public Guid TenantId { get; set; }", storefrontDto, StringComparison.Ordinal);
        Assert.Contains("public Guid Id { get; set; }", storefrontDto, StringComparison.Ordinal);
        Assert.Contains("public decimal Price { get; set; }", storefrontDto, StringComparison.Ordinal);
        Assert.Contains("public string Currency { get; set; }", storefrontDto, StringComparison.Ordinal);
        Assert.Contains("public int? EstimatedDays { get; set; }", storefrontDto, StringComparison.Ordinal);
        Assert.Contains("public string? CarrierName { get; set; }", storefrontDto, StringComparison.Ordinal);

        Assert.Contains("Consumer ALWAYS re-fetches from PostgreSQL", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("x => x.Id == @event.ShippingMethodId && x.TenantId == @event.TenantId.Value", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("if (shippingMethod.IsDeleted)", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCurrencyAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("TombstoneInMongoAndInvalidateCacheAsync", projectionConsumer, StringComparison.Ordinal);

        Assert.Contains("function admitAdminShippingMethods(payload)", adminPage, StringComparison.Ordinal);
        Assert.Contains("Shipping method read authority rejected duplicate or malformed identity.", adminPage, StringComparison.Ordinal);
        Assert.Contains("Shipping method read authority rejected malformed enabled state.", adminPage, StringComparison.Ordinal);
        Assert.Contains("requireInteger(method.estimatedDeliveryDaysMin, 'estimated delivery minimum', 0)", adminPage, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", adminPage, StringComparison.Ordinal);
        Assert.Contains("setMethods(admitAdminShippingMethods(response.data))", adminPage, StringComparison.Ordinal);

        Assert.Contains("tenantId: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("function admitStorefrontShippingMethods(payload: unknown): ShippingMethod[]", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const expectedTenantId = getRequestHeaders()['X-Tenant-Id'];", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("Shipping method response contained foreign tenant evidence.", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("Shipping method response contained duplicate identity evidence.", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("Shipping method response contained invalid currency evidence.", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("Shipping method response contained invalid ETA evidence.", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const methods = admitStorefrontShippingMethods(response.data);", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: state.shippingMethodId", checkoutStore, StringComparison.Ordinal);
        Assert.DoesNotContain("shippingMethods: state.shippingMethods", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("onSelect={() => setShippingMethod(method.id)}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("price={method.price}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("shippingMethod.name", reviewStep, StringComparison.Ordinal);
        Assert.Contains("pricing.shipping > 0 ? formatMoney(pricing.shipping, currency) : 'Free'", reviewStep, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingMethodReadAuthorityDocumentation_MustBeFrozen()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/admin-shipping-method-read-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-SHIP.READ Shipping Method Read Authority Certification", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SHIP.READ Shipping Method Read Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SHIP.READ Shipping Method Read Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SHIP.READ", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SHIP.READ Shipping Method Read Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SHIP.READ Shipping Method Read Authority Freeze", codexRules, StringComparison.Ordinal);
    }
}

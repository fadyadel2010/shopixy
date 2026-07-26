namespace PointOfSale.Tests.Architecture;

public sealed class AdminPaymentMethodReadAuthorityCertificationTests
{
    [Fact]
    public void PaymentMethodReadRuntime_MustRemainBackendOwnedAndTenantScoped()
    {
        var adminController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PaymentMethodsAdminController.cs");
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs");
        var repository = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs");
        var projectionConsumer = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs");
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/TenantPaymentMethod.cs");
        var entityConfiguration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs");
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var paymentStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx");
        var checkoutTypes = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var responseDtoStart = adminController.IndexOf("public class PaymentMethodResponseDto", StringComparison.Ordinal);
        Assert.True(responseDtoStart >= 0, "PaymentMethodResponseDto must remain inspectable.");
        var responseDto = adminController[responseDtoStart..];

        Assert.Contains("[Route(\"api/admin/payment-methods\")]", adminController, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", adminController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", adminController, StringComparison.Ordinal);
        Assert.Contains("var tenantId = TenantId;", adminController, StringComparison.Ordinal);
        Assert.Contains(".Where(m => m.TenantId == tenantId)", adminController, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(m => m.SortOrder)", adminController, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(m => m.Code)", adminController, StringComparison.Ordinal);
        Assert.Contains("IsValidCode(string code)", adminController, StringComparison.Ordinal);
        Assert.Contains("code is \"COD\" or \"STRIPE\" or \"PAYMOB\" or \"FAWRY\" or \"MANUALWALLET\"", adminController, StringComparison.Ordinal);
        Assert.Contains("return Ok(MapToDto(method));", adminController, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigEncrypted", responseDto, StringComparison.Ordinal);

        Assert.Contains("[Route(\"api/storefront/payment-methods\")]", storefrontController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", storefrontController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.TenantId is not Guid tenantId", storefrontController, StringComparison.Ordinal);
        Assert.Contains("GetEnabledPaymentMethodsAsync", storefrontController, StringComparison.Ordinal);
        Assert.Contains("document.IsConfigurationValid == true", storefrontController, StringComparison.Ordinal);
        Assert.Contains("IsSupportedStorefrontPaymentMethod(document.Code)", storefrontController, StringComparison.Ordinal);
        Assert.Contains("IsAvailable = true", storefrontController, StringComparison.Ordinal);
        Assert.Contains("Response.Headers[\"Cache-Control\"] = \"no-store, no-cache, max-age=0\"", storefrontController, StringComparison.Ordinal);

        Assert.Contains("x.TenantId == tenantId", repository, StringComparison.Ordinal);
        Assert.Contains("x.IsEnabled", repository, StringComparison.Ordinal);
        Assert.Contains("!x.IsDeleted", repository, StringComparison.Ordinal);
        Assert.Contains("OrderBy(x => x.SortOrder)", repository, StringComparison.Ordinal);
        Assert.Contains("_dbContext.TenantPaymentMethods", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", repository, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", repository, StringComparison.Ordinal);
        Assert.Contains("ConfigEncrypted is NEVER exposed", repository, StringComparison.Ordinal);

        Assert.Contains("ProjectFromSqlAuthorityAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("LoadSqlPaymentMethodAuthorityAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("x.TenantId == tenantId && x.Id == paymentMethodId", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(eventId, occurredAtUtc, version)", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("BuildTombstoneDocument", projectionConsumer, StringComparison.Ordinal);

        Assert.Contains("public Guid TenantId { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public string Code { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public bool IsEnabled { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public decimal? MinOrderAmount { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public decimal? MaxOrderAmount { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public string? ConfigEncrypted { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("builder.HasIndex(x => new { x.TenantId, x.Code })", entityConfiguration, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", entityConfiguration, StringComparison.Ordinal);

        Assert.Contains("export interface PaymentMethodDto", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("id: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("code: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("displayName: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("isAvailable: boolean;", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("function admitStorefrontPaymentMethods(payload: unknown): PaymentMethodDto[]", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("UUID_REGEX", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_METHOD_CODE_REGEX", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("seenIds.has(id) || seenCodes.has(code)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("Payment method response failed admission.", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const methods = admitStorefrontPaymentMethods(response.data);", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethods: methods", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: state.paymentMethod", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: state.selectedPaymentMethodCode", checkoutStore, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentMethods: state.paymentMethods", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("paymentMethods.map((method)", paymentStep, StringComparison.Ordinal);
        Assert.Contains("selectPaymentMethod(method.code)", paymentStep, StringComparison.Ordinal);
        Assert.Contains("method.isAvailable", paymentStep, StringComparison.Ordinal);
        Assert.Contains("orderAmountForValidation = pricing?.total ?? cartSubtotal", paymentStep, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentMethodReadAuthorityDocumentation_MustBeFrozen()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/payment-method-read-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-PM.READ Payment Method Read Authority Certification", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PM.READ Payment Method Read Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PM.READ Payment Method Read Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PM.READ", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PM.READ Payment Method Read Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PM.READ Payment Method Read Authority Freeze", codexRules, StringComparison.Ordinal);
    }
}

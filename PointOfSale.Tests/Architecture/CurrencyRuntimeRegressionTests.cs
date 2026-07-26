using PointOfSale.API.Services.Payments.Cod;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class CurrencyRuntimeRegressionTests
{
    [Fact]
    public void SettingsRead_MustUseTenantCurrencyAuthorityWithoutPlatformFallback()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SettingsController.cs");
        var getSettings = Slice(source, "public async Task<ActionResult<SettingsDto>> GetSettings()", "[HttpPut]");

        Assert.Contains("FinancialCurrencyGuard.RequireCurrency", getSettings, StringComparison.Ordinal);
        Assert.Contains("tenant.currency", getSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("var fallbackCurrency = FinancialCurrencyGuard.PlatformBaseCurrencyCode", getSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantCurrencyProjection_MustIsoValidateSqlTenantCurrency()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/TenantCurrencyProjectionConsumer.cs");
        var resolver = Slice(source, "private async Task<string> ResolveCurrentSqlTenantCurrencyAsync", "private async Task InvalidateRedisCacheAsync");

        Assert.Contains("NormalizeCurrencyOrThrow", resolver, StringComparison.Ordinal);
        Assert.Contains("validateIso: true", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentLifecycle_MustNotRevalidateHistoricalOrderCurrencyAgainstCurrentTenantCurrency()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var resolver = Slice(source, "private async Task<(PaymentTransitionResult? Failure, string? Currency)> TryResolveLifecycleCurrencyAsync", "catch (FinancialCurrencyMismatchException ex)");

        Assert.Contains("FinancialCurrencyGuard.RequireCurrency", resolver, StringComparison.Ordinal);
        Assert.Contains("order.currency", resolver, StringComparison.Ordinal);
        Assert.Contains("payment.currency", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.currency", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("_db.Tenants", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderAdmission_MustNotDefaultMissingCurrencyToEgp()
    {
        var fawry = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookRuntimeIntegrationService.cs");
        var cod = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionBridge.cs");

        Assert.DoesNotContain("fallback: \"EGP\"", fawry, StringComparison.Ordinal);
        Assert.DoesNotContain(": \"EGP\"", Slice(cod, "public CodRuntimeAdmissionBridgeRequest TranslateRequest", "public CodRuntimeAdmissionOutcome TranslateResponse"), StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireCurrency", cod, StringComparison.Ordinal);
    }

    [Fact]
    public void CodAdmissionBridge_MustFailClosedWhenCurrencyMissing()
    {
        var bridge = new CodRuntimeAdmissionBridge();
        var request = ValidCodRequestWithoutCurrency();

        var exception = Assert.Throws<FinancialCurrencyMissingException>(() => bridge.TranslateRequest(request));

        Assert.Equal("cod.currency", exception.Component);
    }

    [Fact]
    public void FinancialCurrencyGuard_MustNormalizeValidateAndRejectMismatchWithoutConversion()
    {
        Assert.Equal("USD", FinancialCurrencyGuard.RequireCurrency(" usd ", "order.currency", "test"));
        Assert.Equal("EGP", FinancialCurrencyGuard.RequireEgpCurrency("egp", "commercehub.currency", "test"));

        Assert.Throws<FinancialCurrencyMismatchException>(() =>
            FinancialCurrencyGuard.RequireMatch("USD", "order.currency", "EGP", "payment.currency", "test"));
        Assert.Throws<FinancialCurrencyInvalidException>(() =>
            FinancialCurrencyGuard.RequireCurrency("BAD", "order.currency", "test"));
    }

    [Fact]
    public void MoneyRuntime_MustKeepAmountAndCurrencyInseparable()
    {
        var money = new Money(25.50m, new Currency("usd"));

        Assert.Equal(25.50m, money.Amount);
        Assert.Equal("USD", money.Currency.Code);
        Assert.Throws<ArgumentNullException>(() => new Money(1m, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-0.01m, new Currency("EGP")));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker not found: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found: {endMarker}");
        return source[start..end];
    }

    private static CodRuntimeAdmissionRequest ValidCodRequestWithoutCurrency()
    {
        return new CodRuntimeAdmissionRequest(
            new CodAdmissionId("admission-currency-missing"),
            new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1"),
            new CodAdmissionEvidenceIdentity("evidence-1", "cash_collection", "driver"),
            new CodAdmissionEvidenceVersion(1, 0, "cod_collection_evidence", "rev-1"),
            new CodAdmissionMetadata(
                new Dictionary<string, string>
                {
                    ["amount"] = "100.00",
                    ["channel"] = "driver_app"
                },
                "source-reference",
                "actor-reference",
                "Currency regression certification."),
            new CodAdmissionCollectionReference(
                "collection-1",
                "order-1",
                null,
                "shipment-1",
                null),
            CodRequestedAdmissionOutcome.CollectionAccepted,
            DateTimeOffset.Parse("2026-07-17T09:00:00Z"),
            new CodAdmissionRuntimeContext(
                "tenant-1",
                "Sandbox",
                "payment-1",
                "evidence-1",
                new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1")));
    }
}

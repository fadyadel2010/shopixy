namespace PointOfSale.Tests.Architecture;

public sealed class PaymobSecurityHotfix1LogRedactionTests
{
    [Fact]
    public void PaymobProductionLogs_MustNotEmitPaymentSessionArtifacts()
    {
        var sources = ReadPaymobProductionLoggingSources();

        Assert.DoesNotContain("{PaymentKey}", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("{IframeUrl}", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentKey:", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("IframeUrl:", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentKey: {", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("IframeUrl: {", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymobProductionLogs_MustNotEmitSecretsOrRawProviderArtifacts()
    {
        var sources = ReadPaymobProductionLoggingSources();

        foreach (var forbiddenTemplate in new[]
        {
            "{ApiKey}",
            "{HmacSecret}",
            "{Authorization}",
            "{AccessToken}",
            "{AuthToken}",
            "{RawPayload}",
            "{CallbackPayload}",
            "{PaymentToken}",
            "{PaymentUrl}"
        })
        {
            Assert.DoesNotContain(forbiddenTemplate, sources, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PaymobCreatePath_MustRetainSafeOperationalLoggingOnly()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");

        Assert.Contains(
            "Paymob intent created | TenantId: {TenantId} | OrderId: {OrderId} | PaymobOrderId: {PaymobOrderId} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
            service,
            StringComparison.Ordinal);
        Assert.DoesNotContain("paymentKey.Substring", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentKey: {PaymentKey}", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IframeUrl: {IframeUrl}", service, StringComparison.Ordinal);
    }

    [Fact]
    public void HotfixDocumentation_MustRecordFindingClosureAndArchitecturePreservation()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/PAYMOB_SECURITY_HOTFIX_1_LOG_REDACTION.md");

        foreach (var section in new[]
        {
            "Executive Summary",
            "Security Finding",
            "Affected Log Sites",
            "Changes Applied",
            "Redaction Strategy",
            "Architecture Impact",
            "Certification Decision",
            "Success Answers"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("Security finding closed", document, StringComparison.Ordinal);
        Assert.Contains("Runtime unchanged", document, StringComparison.Ordinal);
        Assert.Contains("Provider Adapter unchanged", document, StringComparison.Ordinal);
        Assert.Contains("PAYMOB-FINAL", document, StringComparison.Ordinal);
    }

    private static string ReadPaymobProductionLoggingSources()
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs",
                "PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs",
                "PointOfSale.API/Services/Payments/Paymob/PaymobWebhookRuntimeIntegrationService.cs",
                "PointOfSale.API/Services/Payments/Paymob/PaymobWebhookEvidenceAdapter.cs",
                "PointOfSale.API/Controllers/PaymobWebhooksController.cs"
            }.Select(CertificationSource.ReadRepoFile));
    }
}

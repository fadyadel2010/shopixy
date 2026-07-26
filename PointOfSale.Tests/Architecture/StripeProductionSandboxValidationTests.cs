namespace PointOfSale.Tests.Architecture;

public sealed class StripeProductionSandboxValidationTests
{
    [Fact]
    public void SandboxValidationDocument_MustRecordEnvironmentScenariosEvidenceAndCertificationLimits()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/STRIPE_PRODUCTION_2_SANDBOX_VALIDATION.md");

        foreach (var section in new[]
        {
            "Environment",
            "Scenario Matrix",
            "Runtime And Persistence Validation",
            "Security",
            "Observability",
            "Failure Injection",
            "Performance",
            "Test Evidence",
            "Certification Limits",
            "Success Answers"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("SANDBOX EXECUTION NOT CERTIFIED", document, StringComparison.Ordinal);
        Assert.Contains("Live Stripe sandbox execution is **not certified**", document, StringComparison.Ordinal);
        Assert.Contains("Production Sandbox certified? | **No.", document, StringComparison.Ordinal);
    }

    [Fact]
    public void ScenarioMatrix_MustCoverAllRequiredSandboxDrillsAndScopeLimits()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/STRIPE_PRODUCTION_2_SANDBOX_VALIDATION.md");

        foreach (var scenario in new[]
        {
            "1. Successful Card Payment",
            "2. Failed Card",
            "3. Webhook Replay",
            "4. Webhook Delay",
            "5. Refund",
            "6. Partial Refund",
            "7. Cancel",
            "8. Recovery",
            "9. Retry",
            "10. Tenant Isolation"
        })
        {
            Assert.Contains(scenario, document, StringComparison.Ordinal);
        }

        Assert.Contains("webhook-driven refund", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("command-initiated refund", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POS production partial-refund command E2E not certified", document, StringComparison.Ordinal);
        Assert.Contains("two-merchant live sandbox drill not executed", document, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourcePipeline_MustKeepRuntimeAdmissionReplayAndLifecycleOrderingForWebhookValidation()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeWebhookRuntimeIntegrationService.cs");

        var admissionIndex = service.IndexOf("_stripeWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync", StringComparison.Ordinal);
        var lifecycleIndex = service.IndexOf("_paymentLifecycleService.HandlePaymentResultAsync", admissionIndex, StringComparison.Ordinal);
        var refundIndex = service.IndexOf("_paymentLifecycleService.RecordRefundAsync", admissionIndex, StringComparison.Ordinal);
        var replayPersistIndex = service.IndexOf("_stripeWebhookRuntimeIntegrationService.PersistReplayRecordAsync", admissionIndex, StringComparison.Ordinal);

        Assert.True(admissionIndex >= 0);
        Assert.True(lifecycleIndex > admissionIndex);
        Assert.True(refundIndex > admissionIndex);
        Assert.True(replayPersistIndex > admissionIndex);

        Assert.Contains("StripeWebhookPriorEvidenceLoader.LoadAsync", integration, StringComparison.Ordinal);
        Assert.Contains("replayContext.IsDuplicate || replayContext.IsReplay", integration, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookOutcomeAdmissionService.Evaluate", integration, StringComparison.Ordinal);
        Assert.Contains("_webhookEvidenceAdapter.ProcessAsync", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentDefaults_MustRemainSandboxDisabledAndFailClosedUntilSecretsAreProvisioned()
    {
        var appsettings = CertificationSource.ReadRepoFile("PointOfSale.API/appsettings.json");
        var credentialValidator = CertificationSource.ReadRepoFile("PointOfSale.API/Configuration/StripeOptions.cs");
        var project = CertificationSource.ReadRepoFile("PointOfSale.API/PointOfSale.API.csproj");

        Assert.Contains("\"Stripe\":", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": false", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"SecretKey\": \"\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"PublishableKey\": \"\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"WebhookSecret\": \"\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"ApiVersion\": \"2026-06-24.dahlia\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"Environment\": \"Sandbox\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"SandboxMode\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"SandboxEnabled\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"ProductionEnabled\": false", appsettings, StringComparison.Ordinal);

        Assert.Contains("pk_test_", credentialValidator, StringComparison.Ordinal);
        Assert.Contains("rk_test_", credentialValidator, StringComparison.Ordinal);
        Assert.Contains("whsec_", credentialValidator, StringComparison.Ordinal);
        Assert.Contains("CertifiedApiVersion", credentialValidator, StringComparison.Ordinal);
        Assert.Contains("PackageReference Include=\"Stripe.net\" Version=\"52.1.0\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityAndObservability_MustExcludeSensitiveValuesAndExposeRequiredMetrics()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/STRIPE_PRODUCTION_2_SANDBOX_VALIDATION.md");
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/StripeOperationalTelemetry.cs");
        var operationalReadiness = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/StripeOperationalReadinessHealthCheck.cs");
        var stripeSources = string.Join(
            Environment.NewLine,
            new[]
            {
                "PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs",
                "PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs",
                "PointOfSale.API/Services/Payments/Stripe/StripeWebhookRuntimeIntegrationService.cs",
                "PointOfSale.API/Controllers/StripeWebhooksController.cs",
                "PointOfSale.API/Controllers/StorefrontStripePaymentsController.cs"
            }.Select(CertificationSource.ReadRepoFile));

        foreach (var metric in new[]
        {
            "stripe_payment_created_total",
            "stripe_payment_succeeded_total",
            "stripe_payment_failed_total",
            "stripe_webhook_received_total",
            "stripe_webhook_duplicate_total",
            "stripe_replay_prevented_total",
            "stripe_refund_success_total",
            "stripe_refund_failure_total",
            "stripe_capture_success_total",
            "stripe_capture_failure_total",
            "stripe_runtime_decision_total"
        })
        {
            Assert.Contains(metric, document, StringComparison.Ordinal);
            Assert.Contains(metric, telemetry, StringComparison.Ordinal);
        }

        Assert.Contains("tenant_bucket", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant_id", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{ClientSecret}", stripeSources, StringComparison.Ordinal);
        Assert.DoesNotContain("{SecretKey}", stripeSources, StringComparison.Ordinal);
        Assert.DoesNotContain("{WebhookSecret}", stripeSources, StringComparison.Ordinal);
        Assert.Contains("outcomeAdmissionHealth", operationalReadiness, StringComparison.Ordinal);
        Assert.Contains("replayStoreHealth", operationalReadiness, StringComparison.Ordinal);
    }
}

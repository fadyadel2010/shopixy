namespace PointOfSale.Tests.Architecture;

public sealed class FawryPaymentAuthorityCertificationTests
{
    [Fact]
    public void StorefrontFawryRuntime_MustKeepFrontendSelectionPresentationReferenceDisplayRetryAndRefreshOnly()
    {
        var paymentStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx");
        var checkoutSuccess = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/CheckoutSuccess.tsx");
        var paymentSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");

        Assert.Contains("case 'FAWRY':", paymentStep, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePaymentIntent", paymentStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("referenceCode =", paymentStep, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("referenceCode: order.referenceCode", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expiresAtUtc: order.expiresAtUtc", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentIntentId: order.paymentIntentId", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("checkoutToken: order.checkoutToken", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("provider !== 'FAWRY'", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(paymentIntentId)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(checkoutToken)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isFutureIsoDate(expiresAtUtc)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.referenceCode)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!FAWRY_PENDING_STATUSES.has(fawryStatus)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("complete Fawry payment-session handoff", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Backend checkout response mixed provider payment-session artifacts.", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Terminal payment response unexpectedly included reusable provider payment-session artifacts.", paymentSessionAuthority, StringComparison.Ordinal);

        Assert.Contains("order.paymentMethod?.toUpperCase() === 'FAWRY' && order.referenceCode", checkoutSuccess, StringComparison.Ordinal);
        Assert.Contains("{order.referenceCode}", checkoutSuccess, StringComparison.Ordinal);
        Assert.Contains("order.expiresAtUtc", checkoutSuccess, StringComparison.Ordinal);
        Assert.Contains("'FAWRY'", checkoutTypes, StringComparison.Ordinal);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.DoesNotContain("orderResult", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("referenceCode", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentIntentId", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkoutToken", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendFawryCreation_MustOwnIntentMerchantReferenceReferenceNumberCheckoutTokenAmountCurrencyExpirationAndRecovery()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");
        var runtimeApplication = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs");
        var recoveryService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("case \"FAWRY\":", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_paymentGateway.CreateSessionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ProviderCode = new ProviderCode(paymentMethodCode)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentId = paymentIntent?.Id", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus = order.PaymentStatus.ToString()", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ReferenceCode = paymentIntent?.ReferenceCode", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc = paymentIntent?.ExpiresAtUtc", checkoutService, StringComparison.Ordinal);
        Assert.Contains("string.Equals(order.PaymentMethodCode, \"FAWRY\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.ReferenceCode = null", checkoutService, StringComparison.Ordinal);
        Assert.Contains("replayResponse.ReferenceCode = null", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"FAWRY\" => PaymentProvider.Fawry", checkoutService, StringComparison.Ordinal);

        Assert.Contains("Amount = paymentIntent.Amount", paymentService, StringComparison.Ordinal);
        Assert.Contains("Currency = paymentIntent.Currency", paymentService, StringComparison.Ordinal);
        Assert.Contains("ReferenceCode = paymentIntent.ReferenceCode", paymentService, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc = paymentIntent.ExpiresAtUtc", paymentService, StringComparison.Ordinal);
        Assert.Contains("IFawryRuntimeApplicationService", paymentService, StringComparison.Ordinal);
        Assert.Contains("InitiateFawryPersistedPaymentIntentAsync(paymentIntent", paymentService, StringComparison.Ordinal);
        Assert.Contains("FawryRuntimePaymentIntentApplicationRequest", paymentService, StringComparison.Ordinal);
        Assert.Contains("BeginChargeRegistrationAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("PersistAcceptedReferenceAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("ApplyAcceptedReference", paymentService, StringComparison.Ordinal);
        Assert.Contains("intent.Provider == PaymentProvider.Fawry && string.IsNullOrWhiteSpace(intent.ReferenceCode)", paymentService, StringComparison.Ordinal);

        Assert.Contains("FawryMerchantReference.Build(action)", adapter, StringComparison.Ordinal);
        Assert.Contains("FawryEvidenceFactory.FromChargeResponse(action, officialResponse)", adapter, StringComparison.Ordinal);
        Assert.Contains("FawryChargeResponseMapper.ProviderReference(response)", adapter, StringComparison.Ordinal);
        Assert.Contains("FawryChargeResponseMapper.Timestamp(response)", adapter, StringComparison.Ordinal);
        Assert.Contains("request.TenantId", runtimeApplication, StringComparison.Ordinal);
        Assert.Contains("request.OrderId", runtimeApplication, StringComparison.Ordinal);
        Assert.Contains("request.IdempotencyKey", runtimeApplication, StringComparison.Ordinal);

        Assert.Contains("case \"FAWRY\":", recoveryService, StringComparison.Ordinal);
        Assert.Contains("_paymentGateway.CreateSessionAsync", recoveryService, StringComparison.Ordinal);
        Assert.Contains("intent.Provider == PaymentProvider.Fawry && string.IsNullOrWhiteSpace(intent.ReferenceCode)", recoveryService, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void FawryWebhook_MustOwnCallbackWebhookAdmissionSignatureVerificationStatusAuditReplayAndSqlPersistence()
    {
        var webhookController = ReadRepoFile("PointOfSale.API/Controllers/FawryWebhookController.cs");
        var webhookIntegration = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookRuntimeIntegrationService.cs");
        var fawryTenantResolver = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs");
        var tenantResolver = ReadRepoFile("PointOfSale.API/Services/Payments/IPaymentWebhookTenantResolver.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("[Route(\"api/webhooks/fawry\")]", webhookController, StringComparison.Ordinal);
        Assert.Contains("[AllowAnonymousTenant]", webhookController, StringComparison.Ordinal);
        Assert.Contains("RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)", webhookController, StringComparison.Ordinal);
        Assert.Contains("WebhookRateLimiterPolicy.PolicyName", webhookController, StringComparison.Ordinal);
        Assert.Contains("request.Headers[FawrySignatureHeader].FirstOrDefault()", webhookController, StringComparison.Ordinal);
        Assert.Contains("_webhookRequestPipeline.PrepareAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("IFawryWebhookRuntimeIntegrationService", webhookController, StringComparison.Ordinal);
        Assert.Contains("EvaluateAndExecuteAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("return Unauthorized(new ApiErrorResponse(\"INVALID_SIGNATURE\"", webhookController, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifySignature(", webhookController, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWebhookSecretAsync", webhookController, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", webhookController, StringComparison.Ordinal);
        Assert.Contains("ResolveWebhookAuthorityAsync", webhookIntegration, StringComparison.Ordinal);
        Assert.Contains("FawryOperationalCapability.WebhookAuthentication", fawryTenantResolver, StringComparison.Ordinal);

        Assert.Contains("IsolationLevel.Serializable", webhookController, StringComparison.Ordinal);
        Assert.Contains("PrepareWebhookLogAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("ProcessingStatus == WebhookProcessingStatus.Completed", webhookController, StringComparison.Ordinal);
        Assert.Contains("ProcessingStatus == WebhookProcessingStatus.Processing", webhookController, StringComparison.Ordinal);
        Assert.Contains("Duplicate event ignored", webhookController, StringComparison.Ordinal);
        Assert.Contains("webhookLog.PaymentIntentId = admissionResult.PaymentIntentId", webhookController, StringComparison.Ordinal);
        Assert.Contains("webhookLog.OrderId = admissionResult.OrderId", webhookController, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookLifecycleAdmission.CreateRequest", webhookController, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.HandlePaymentResultAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("PersistReplayRecordAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("StorefrontSuspenseRecoveryPolicy.ApplyManualReviewMarker", webhookController, StringComparison.Ordinal);
        Assert.Contains("PersistWebhookFailureAsync", webhookController, StringComparison.Ordinal);

        Assert.Contains("\"fawry\" => await ResolveFawryAsync(root", tenantResolver, StringComparison.Ordinal);
        Assert.Contains("GetString(root, \"fawryRefNumber\", \"referenceNumber\")", tenantResolver, StringComparison.Ordinal);
        Assert.Contains("GetString(root, \"merchantRefNumber\", \"merchantRefNum\")", tenantResolver, StringComparison.Ordinal);
        Assert.Contains("ResolveByReferenceCodeAsync(referenceCode", tenantResolver, StringComparison.Ordinal);
        Assert.Contains("ResolveByPaymentReferenceAsync(merchantReference", tenantResolver, StringComparison.Ordinal);
        Assert.Contains("paymentIntent.ReferenceCode == referenceCode", tenantResolver, StringComparison.Ordinal);

        Assert.Contains("TryQuarantineProviderAuthorityMismatchAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsTerminalSqlPaymentTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus can ONLY be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPay7Truth()
    {
        Assert.Contains(
            "Fawry Payment Authority Certification (PASS SF-PAY7)",
            ReadRepoFile("docs/architecture/fawry-payment-authority-certification-findings.md"),
            StringComparison.Ordinal);

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

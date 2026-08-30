namespace PointOfSale.Tests.Architecture;

public sealed class PaymobPaymentAuthorityCertificationTests
{
    [Fact]
    public void StorefrontPaymobRuntime_MustKeepFrontendPresentationRedirectIframePendingAndRefreshOnly()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var paymobForm = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx");
        var paymentSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");

        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("provider !== 'PAYMOB'", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(paymentIntentId)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("!isNonEmptyString(checkoutToken)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("GuestOrderAccessCookie.SetCheckoutToken", checkoutController, StringComparison.Ordinal);
        Assert.Contains("response.CheckoutToken = null", checkoutController, StringComparison.Ordinal);
        Assert.Contains("!isFutureIsoDate(expiresAtUtc)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isPaymobUnifiedCheckoutUrl(response.paymobIframeUrl)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("!isNonEmptyString(response.paymobPaymentKey)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("complete Paymob handoff", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Backend checkout response mixed provider payment-session artifacts.", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Terminal payment response unexpectedly included reusable provider payment-session artifacts.", paymentSessionAuthority, StringComparison.Ordinal);

        Assert.Contains("orderResult.paymobIframeUrl", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("<PaymobPaymentForm", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("window.location.href = buildAffiliateUrl(buildPendingCheckoutUrl(orderResult));", checkoutPage, StringComparison.Ordinal);
        Assert.DoesNotContain("/confirmation", checkoutPage[checkoutPage.IndexOf("PAYMOB: Show Paymob payment form", StringComparison.Ordinal)..], StringComparison.OrdinalIgnoreCase);

        Assert.Contains("window.location.assign(checkoutUrl.toString())", paymobForm, StringComparison.Ordinal);
        Assert.Contains("Provider redirect values are evidence only", paymobForm, StringComparison.Ordinal);
        Assert.Contains("onPending();", paymobForm, StringComparison.Ordinal);
        Assert.DoesNotContain("onPaymentSuccess", paymobForm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onSuccess", paymobForm, StringComparison.OrdinalIgnoreCase);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.DoesNotContain("orderResult", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("paymobPaymentKey", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentIntentId", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkoutToken", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendPaymobCreation_MustOwnIntentSessionPaymentKeyCheckoutTokenAmountCurrencyIntegrationAndRecovery()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymobService = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var paymobGateway = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentGateway.cs");
        var paymobController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs");
        var recoveryService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("_paymentGateway.CreateSessionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ProviderCode = new ProviderCode(paymentMethodCode)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("paymentIntentId = session.PaymentIntentId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("paymobIframeUrl = GetCompatibilityField(session, nameof(CheckoutResponseDto.PaymobIframeUrl))", checkoutService, StringComparison.Ordinal);
        Assert.Contains("paymobPaymentKey = GetCompatibilityField(session, nameof(CheckoutResponseDto.PaymobPaymentKey))", checkoutService, StringComparison.Ordinal);
        Assert.Contains("expiresAtUtc = await ResolvePaymentIntentExpirationAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentId = paymentIntentId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus = order.PaymentStatus.ToString()", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CheckoutToken = checkoutToken", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymobIframeUrl = paymobIframeUrl", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymobPaymentKey = paymobPaymentKey", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShouldIssueCheckoutToken(order)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("string.Equals(order.PaymentMethodCode, \"PAYMOB\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.PaymobPaymentKey = null", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.PaymobIframeUrl = null", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GetPaymobPaymentSessionAsync(paymentIntent, cancellationToken)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureReplayPaymentIntentAsync", checkoutService, StringComparison.Ordinal);

        Assert.Contains("OrderLockingHelper.LoadOrderWithLockAsync", paymobService, StringComparison.Ordinal);
        Assert.Contains("ValidateCustomerOwnershipAsync(order", paymobService, StringComparison.Ordinal);
        Assert.Contains("order.PaymentMethodCode.Equals(\"PAYMOB\"", paymobService, StringComparison.Ordinal);
        Assert.Contains("Amount = order.Total", paymobService, StringComparison.Ordinal);
        Assert.Contains("Currency = order.Currency", paymobService, StringComparison.Ordinal);
        Assert.Contains("IPaymobRuntimeApplicationService _paymobRuntimeApplicationService", paymobService, StringComparison.Ordinal);
        Assert.Contains("CreatePaymobProviderIntentAsync", paymobService, StringComparison.Ordinal);
        Assert.Contains("_paymobRuntimeApplicationService.CreatePaymentIntentAsync", paymobService, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateClient(\"Paymob\")", paymobService, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobPaymentKeyUrl", paymobService, StringComparison.Ordinal);
        Assert.DoesNotContain("PostAsync(", paymobService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentRoutingMetadataSerializer.Serialize", paymobService, StringComparison.Ordinal);
        Assert.Contains("_artifactProtector.ProtectPaymentSession", paymobService, StringComparison.Ordinal);
        Assert.Contains("PaymentKey = paymentKey", paymobService, StringComparison.Ordinal);
        Assert.Contains("var newSessionExpiresAtUtc = nowUtc.Add(StorefrontPaymentSessionTtl)", paymobService, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc = newSessionExpiresAtUtc", paymobService, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(60)", paymobService, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_PAYMENT_SESSION_EXPIRED", paymobService, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_PAYMENT_SESSION_EXPIRY_MISSING", paymobService, StringComparison.Ordinal);
        Assert.Contains("paymentIntent.ExpiresAtUtc ??= newSessionExpiresAtUtc", paymobService, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(existingSession?.IframeUrl)", paymobService, StringComparison.Ordinal);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(existingIntent.RedirectUrl)", paymobService, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc = response.ExpiresAtUtc", paymobGateway, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(pi => pi.CreatedAt)", paymobService, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_INTENT_IN_PROGRESS", paymobService, StringComparison.Ordinal);
        Assert.Contains("concurrent_claim", paymobService, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.ResetFailedPaymentForRetryAsync", paymobService, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", paymobService, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", dbContext, StringComparison.Ordinal);

        Assert.Contains("AuthorizeOrderAccessAsync", paymobController, StringComparison.Ordinal);
        Assert.Contains("GetCheckoutToken()", paymobController, StringComparison.Ordinal);
        Assert.Contains("_paymobPaymentService.CreatePaymentIntentAsync", paymobController, StringComparison.Ordinal);
        Assert.Contains("_paymobPaymentService.GetPaymentStatusAsync", paymobController, StringComparison.Ordinal);
        Assert.Contains("authorization.AuthenticatedCustomerId", paymobController, StringComparison.Ordinal);

        Assert.Contains("case \"PAYMOB\":", recoveryService, StringComparison.Ordinal);
        Assert.Contains("_paymentGateway.CreateSessionAsync", recoveryService, StringComparison.Ordinal);
        Assert.Contains("ProviderCode = new ProviderCode(paymentMethodCode)", recoveryService, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymobCallbacksAndWebhooks_MustOwnVerificationSuccessFailureStatusAuditReplayAndSqlPersistence()
    {
        var webhookController = ReadRepoFile("PointOfSale.API/Controllers/PaymobWebhooksController.cs");
        var paymobService = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var callbackTests = ReadRepoFile("PointOfSale.Tests/Payments/PaymobCallbackValidationTests.cs");
        var webhookTests = ReadRepoFile("PointOfSale.Tests/Payments/WebhookSecurityHardeningTests.cs");

        Assert.Contains("[Route(\"api/webhooks/paymob\")]", webhookController, StringComparison.Ordinal);
        Assert.Contains("[AllowAnonymousTenant]", webhookController, StringComparison.Ordinal);
        Assert.Contains("RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)", webhookController, StringComparison.Ordinal);
        Assert.Contains("WebhookRateLimiterPolicy.PolicyName", webhookController, StringComparison.Ordinal);
        Assert.Contains("_webhookRequestPipeline.PrepareAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookSignatureException", webhookController, StringComparison.Ordinal);
        Assert.Contains("_paymobPaymentService.ProcessWebhookAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("return Unauthorized(new ApiErrorResponse", webhookController, StringComparison.Ordinal);
        Assert.Contains("return StatusCode(StatusCodes.Status500InternalServerError", webhookController, StringComparison.Ordinal);
        Assert.Contains("ValidateCallbackAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("RedirectTo: {RedirectUrl}", webhookController, StringComparison.Ordinal);
        Assert.Contains("/checkout/pending", webhookController, StringComparison.Ordinal);
        Assert.Contains("Final success/failure is still decided by backend payment state polling.", webhookController, StringComparison.Ordinal);

        Assert.Contains("BuildWebhookHmacPayload(rawPayload)", paymobService, StringComparison.Ordinal);
        Assert.Contains("VerifyHmacSignature(canonicalHmacPayload, paymobConfig.HmacSecret, hmacSignature)", paymobService, StringComparison.Ordinal);
        Assert.Contains("throw new PaymobWebhookSignatureException", paymobService, StringComparison.Ordinal);
        Assert.Contains("PrepareWebhookLogAsync", paymobService, StringComparison.Ordinal);
        Assert.Contains("Duplicate event ignored", paymobService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", paymobService, StringComparison.Ordinal);
        Assert.Contains("Outcome = PaymentLifecycleOutcome.Succeeded", paymobService, StringComparison.Ordinal);
        Assert.Contains("? PaymentLifecycleOutcome.Cancelled", paymobService, StringComparison.Ordinal);
        Assert.Contains(": PaymentLifecycleOutcome.Failed", paymobService, StringComparison.Ordinal);
        Assert.Contains("Source = PaymentLifecycleSource.GatewayWebhook", paymobService, StringComparison.Ordinal);
        Assert.Contains("ProviderAmount = providerAmount", paymobService, StringComparison.Ordinal);
        Assert.Contains("ProviderCurrency = providerCurrency", paymobService, StringComparison.Ordinal);
        Assert.Contains("webhookLog.PaymentIntentId = intent.Id", paymobService, StringComparison.Ordinal);
        Assert.Contains("webhookLog.OrderId = intent.OrderId", paymobService, StringComparison.Ordinal);
        Assert.Contains("pi.Provider == PaymentProvider.Paymob", paymobService, StringComparison.Ordinal);
        Assert.Contains("PersistWebhookFailureAsync", paymobService, StringComparison.Ordinal);
        Assert.Contains("StoreWebhookIdempotencyAsync", paymobService, StringComparison.Ordinal);
        Assert.Contains("VerifyCallbackHmac(callbackParams, paymobConfig.HmacSecret, hmacSignature)", paymobService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentRoutingMetadataSerializer.MatchesStoredIntent", paymobService, StringComparison.Ordinal);

        Assert.Contains("TryQuarantineProviderAuthorityMismatchAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsTerminalSqlPaymentTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.GatewayWebhook", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus can ONLY be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId", dbContext, StringComparison.Ordinal);

        Assert.Contains("ValidateCallbackAsync_ResolvesTenantFromStoredPaymentIntentContext", callbackTests, StringComparison.Ordinal);
        Assert.Contains("ValidateCallbackAsync_WhenStoredRoutingMetadataMissing_ReturnsInvalid", callbackTests, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhook_WhenClaimedTenantDoesNotMatchResolvedTenant_ReturnsUnauthorized", webhookTests, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhook_WhenHmacMissing_ReturnsUnauthorized", webhookTests, StringComparison.Ordinal);
        Assert.Contains("PaymobCallback_WhenStoredContextResolved_RedirectsToResolvedTenantPendingPage", webhookTests, StringComparison.Ordinal);
        Assert.Contains("PaymobCallback_WhenStoredContextMissing_ReturnsSafeErrorPage", webhookTests, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPay5Truth()
    {
        Assert.Contains(
            "Paymob Payment Authority Certification (PASS SF-PAY5)",
            ReadRepoFile("docs/architecture/paymob-payment-authority-certification-findings.md"),
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

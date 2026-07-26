namespace PointOfSale.Tests.Architecture;

public sealed class VodafoneCashPaymentAuthorityCertificationTests
{
    [Fact]
    public void StorefrontVodafoneCashRuntime_MustKeepFrontendPresentationWalletDisplayCandidateSubmissionAndRefreshOnly()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var paymentStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx");
        var manualFields = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var paymentSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");

        Assert.Contains("code: 'VodafoneCash'", manualFields, StringComparison.Ordinal);
        Assert.Contains("Vodafone Cash", manualFields, StringComparison.Ordinal);
        Assert.Contains("merchantWalletNumber", manualFields, StringComparison.Ordinal);
        Assert.Contains("navigator.clipboard.writeText(merchantWalletNumber)", manualFields, StringComparison.Ordinal);
        Assert.Contains("setManualWalletType(wallet.code)", manualFields, StringComparison.Ordinal);
        Assert.Contains("setManualWalletPhoneNumber(event.target.value)", manualFields, StringComparison.Ordinal);
        Assert.Contains("setManualWalletProofFileKey(response.data.fileKey)", manualFields, StringComparison.Ordinal);
        Assert.Contains("/api/storefront/payments/manual-wallet/upload-proof", manualFields, StringComparison.Ordinal);
        Assert.Contains("X-Manual-Wallet-Upload-Token", manualFields, StringComparison.Ordinal);

        Assert.Contains("method.code === 'MANUALWALLET'", paymentStep, StringComparison.Ordinal);
        Assert.Contains("<ManualWalletFields />", paymentStep, StringComparison.Ordinal);
        Assert.Contains("manualWalletType", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("manualWalletPhoneNumber", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("manualWalletProofFileKey", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("validateManualWalletFields", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("walletType: latestState.manualWalletType", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("walletPhoneNumber: latestState.manualWalletPhoneNumber", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("proofFileKey: latestState.manualWalletProofFileKey", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("provider === 'MANUALWALLET'", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_PENDING_STATUSES", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(paymentIntentId)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("response.isManualWallet !== true", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.merchantWalletNumber)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.walletType)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.walletInstructions)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("pending ManualWallet payment-session evidence", paymentSessionAuthority, StringComparison.Ordinal);

        Assert.Contains("requiresPendingRoute", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("window.location.replace(buildAffiliateUrl(buildPendingCheckoutUrl(orderResult)));", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("requiresManualWalletReentry", recoverPage, StringComparison.Ordinal);
        Assert.Contains("manualWalletType: null", recoverPage, StringComparison.Ordinal);
        Assert.Contains("manualWalletProofFileKey: null", recoverPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendVodafoneCashAdmission_MustOwnIntentWalletIdentityMerchantWalletReferenceEvidenceAmountCurrencyCustomerAndPersistence()
    {
        var storefrontController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontManualWalletController.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var manualAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/ManualWalletPaymentAdapter.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("_tenantContext.RequiredTenantId", storefrontController, StringComparison.Ordinal);
        Assert.Contains("GetByCodeAsync", storefrontController, StringComparison.Ordinal);
        Assert.Contains("MANUALWALLET", storefrontController, StringComparison.Ordinal);
        Assert.Contains("IsConfigurationValid != true", storefrontController, StringComparison.Ordinal);
        Assert.Contains("Authorize(AuthenticationSchemes = ManualWalletUploadTokenAuthenticationDefaults.SchemeName)", storefrontController, StringComparison.Ordinal);
        Assert.Contains("authenticatedCheckoutSessionId != request.CheckoutSessionId", storefrontController, StringComparison.Ordinal);
        Assert.Contains("session.Status != Core.Enums.CheckoutSessionStatus.Active", storefrontController, StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals(session.RecoveryToken, tokenRecoveryNonce)", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ValidateFileAsync", storefrontController, StringComparison.Ordinal);
        Assert.Contains("TryNormalizeTenantScopedObjectKey", storefrontController, StringComparison.Ordinal);
        Assert.Contains("SetManualWalletProofAsync", storefrontController, StringComparison.Ordinal);

        Assert.Contains("VODAFONECASH", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("ResolveValidatedManualWalletProofFileKeyAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ManualWalletProofStorage.IsExternalAbsoluteUrl(proofFileKey)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("session.TenantId == tenantId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("session.CartId == cartId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("session.OnlineCustomerId == customerId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentId = paymentIntentId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus = order.PaymentStatus.ToString()", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.MerchantWalletNumber = manualWalletDetails.MerchantWalletNumber", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.WalletInstructions = manualWalletDetails.WalletInstructions", checkoutService, StringComparison.Ordinal);

        Assert.Contains("OrderLockingHelper.LoadOrderWithLockAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("EnsurePaymentCurrencyConsistencyAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("Amount = order.Total", paymentService, StringComparison.Ordinal);
        Assert.Contains("Currency = validatedCurrency", paymentService, StringComparison.Ordinal);
        Assert.Contains("Provider = provider", paymentService, StringComparison.Ordinal);
        Assert.Contains("PaymentReference = paymentReference", paymentService, StringComparison.Ordinal);
        Assert.Contains("WalletType = request.WalletType", paymentService, StringComparison.Ordinal);
        Assert.Contains("WalletPhoneNumber = request.WalletPhoneNumber", paymentService, StringComparison.Ordinal);
        Assert.Contains("ProofImageUrl = normalizedManualWalletProofFileKey", paymentService, StringComparison.Ordinal);
        Assert.Contains("DeriveManualWalletDetailsAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("MerchantWalletNumber = walletDetails.Value.MerchantWalletNumber", paymentService, StringComparison.Ordinal);
        Assert.Contains("WalletInstructions = walletDetails.Value.WalletInstructions", paymentService, StringComparison.Ordinal);
        Assert.Contains("PaymentManualWalletNotConfiguredException", paymentService, StringComparison.Ordinal);
        Assert.Contains("DUPLICATE_PAYMENT_INTENT", paymentService, StringComparison.Ordinal);

        Assert.Contains("intent.Status = PaymentIntentStatus.AwaitingVerification", manualAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentIntentStatus.Paid", manualAdapter, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationStatusRecoveryCacheAndConcurrency_MustReenterBackendVodafoneCashAuthority()
    {
        var adminManual = ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var recoveryService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var cleanupJob = ReadRepoFile("PointOfSale.API/Jobs/ManualWalletProofCleanupJob.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", adminManual, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewApproval", adminManual, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewRejection", adminManual, StringComparison.Ordinal);
        Assert.Contains("ManualWalletStateMachine.EnsureValidTransition(paymentIntent.Status, PaymentIntentStatus.Paid)", adminManual, StringComparison.Ordinal);
        Assert.Contains("ManualWalletStateMachine.EnsureValidTransition(paymentIntent.Status, PaymentIntentStatus.Rejected)", adminManual, StringComparison.Ordinal);
        Assert.Contains("pi.TenantId == tenantId", adminManual, StringComparison.Ordinal);
        Assert.Contains("pi.Provider == PaymentProvider.ManualWallet", adminManual, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", adminManual, StringComparison.Ordinal);

        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewApproval", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewRejection", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsTerminalSqlPaymentTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus can ONLY be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.DoesNotContain("orderResult", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentIntentId", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("merchantWalletNumber", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manualWalletProofFileKey", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletUploadToken", localStoragePartialize, StringComparison.Ordinal);

        Assert.Contains("clearSensitiveCheckoutSessionStorage();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("if (state.isSubmitting)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_IN_PROGRESS", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("requiresManualWalletReentry", recoverPage, StringComparison.Ordinal);
        Assert.Contains("Provider = PaymentProvider.ManualWallet.ToString()", recoveryService, StringComparison.Ordinal);
        Assert.Contains("intent.Provider == PaymentProvider.ManualWallet", recoveryService, StringComparison.Ordinal);
        Assert.Contains("PaymentProvider.ManualWallet", cleanupJob, StringComparison.Ordinal);
        Assert.Contains("TryNormalizeTenantScopedObjectKey", cleanupJob, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossSystemAndStorefrontSurfaces_MustNotExposeVodafoneCashAcceptedPaymentAuthority()
    {
        var storefrontRuntime = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts")
            + ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx")
            + ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx")
            + ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");

        Assert.DoesNotContain("/api/admin/payments/manual-wallet", storefrontRuntime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manual-wallet/{paymentIntentId}/confirm", storefrontRuntime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manual-wallet/{paymentIntentId}/reject", storefrontRuntime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PaymentLifecycleSource.PaymentReviewApproval", storefrontRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentIntentStatus.Paid", storefrontRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus.Collected", storefrontRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("acceptedPayment", storefrontRuntime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verifiedPayment", storefrontRuntime, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPay6Truth()
    {
        Assert.Contains(
            "Vodafone Cash Payment Authority Certification (PASS SF-PAY6)",
            ReadRepoFile("docs/architecture/vodafone-cash-payment-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-PAY6", ReadRepoFile(path), StringComparison.Ordinal);
        }
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

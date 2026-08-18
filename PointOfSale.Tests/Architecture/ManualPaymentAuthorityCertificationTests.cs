namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentAuthorityCertificationTests
{
    [Fact]
    public void StorefrontManualPaymentRuntime_MustKeepFrontendCandidateOnly()
    {
        var paymentStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx");
        var manualFields = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var paymentSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");

        Assert.Contains("method.code === 'MANUALWALLET'", paymentStep, StringComparison.Ordinal);
        Assert.Contains("<ManualWalletFields />", paymentStep, StringComparison.Ordinal);
        Assert.Contains("selectPaymentMethod(method.code)", paymentStep, StringComparison.Ordinal);

        Assert.Contains("manualWalletType", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("manualWalletPhoneNumber", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("manualWalletProofFileKey", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("validateManualWalletFields", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("proofFileKey: latestState.manualWalletProofFileKey", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("manualWalletProofFileKey: null", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("/api/storefront/payments/manual-wallet/config", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("/api/storefront/payments/manual-wallet/upload-proof", manualFields, StringComparison.Ordinal);
        Assert.Contains("X-Manual-Wallet-Upload-Token", manualFields, StringComparison.Ordinal);
        Assert.Contains("checkoutSessionId", manualFields, StringComparison.Ordinal);
        Assert.Contains("setManualWalletProofFileKey(response.data.fileKey)", manualFields, StringComparison.Ordinal);
        Assert.Contains("/api/storefront/payments/manual-wallet/clear-proof", manualFields, StringComparison.Ordinal);

        Assert.Contains("provider === 'MANUALWALLET'", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_PENDING_STATUSES", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(paymentIntentId)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("response.isManualWallet !== true", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.walletType)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("!isNonEmptyString(response.merchantWalletNumber)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("!isNonEmptyString(response.walletInstructions)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("pending ManualWallet payment-session evidence", paymentSessionAuthority, StringComparison.Ordinal);

        Assert.Contains("walletType?: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("walletPhoneNumber?: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("proofFileKey?: string;", checkoutTypes, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendManualPaymentAdmission_MustOwnIntentEvidenceStatusAmountCurrencyAndTenantScope()
    {
        var storefrontController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontManualWalletController.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("_tenantContext.RequiredTenantId", storefrontController, StringComparison.Ordinal);
        Assert.Contains("GetByCodeAsync", storefrontController, StringComparison.Ordinal);
        Assert.Contains("MANUALWALLET", storefrontController, StringComparison.Ordinal);
        Assert.Contains("IsConfigurationValid != true", storefrontController, StringComparison.Ordinal);
        Assert.Contains("Authorize(AuthenticationSchemes = ManualWalletUploadTokenAuthenticationDefaults.SchemeName)", storefrontController, StringComparison.Ordinal);
        Assert.Contains("authenticatedCheckoutSessionId != request.CheckoutSessionId", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ResolveActiveSessionAsync", storefrontController, StringComparison.Ordinal);
        Assert.Contains("session.Status != Core.Enums.CheckoutSessionStatus.Active", storefrontController, StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals(session.RecoveryToken, tokenRecoveryNonce)", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ValidateFileAsync", storefrontController, StringComparison.Ordinal);
        Assert.Contains("TryNormalizeTenantScopedObjectKey", storefrontController, StringComparison.Ordinal);
        Assert.Contains("SetManualWalletProofAsync", storefrontController, StringComparison.Ordinal);

        Assert.Contains("ResolveValidatedManualWalletProofFileKeyAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ManualWalletProofStorage.IsExternalAbsoluteUrl(proofFileKey)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("session.TenantId == tenantId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("session.CartId == cartId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("session.ConvertedOrderId == orderId.Value", checkoutService, StringComparison.Ordinal);
        Assert.Contains("session.OnlineCustomerId == customerId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("session.SessionId == effectiveSessionId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ProofFileKey = proofFileKey", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_paymentGateway.CreateSessionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentId = paymentIntentId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus = order.PaymentStatus.ToString()", checkoutService, StringComparison.Ordinal);

        Assert.Contains("OrderLockingHelper.LoadOrderWithLockAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("EnsurePaymentCurrencyConsistencyAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("Amount = order.Total", paymentService, StringComparison.Ordinal);
        Assert.Contains("Currency = validatedCurrency", paymentService, StringComparison.Ordinal);
        Assert.Contains("Provider = provider", paymentService, StringComparison.Ordinal);
        Assert.Contains("PaymentReference = paymentReference", paymentService, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey = requestId", paymentService, StringComparison.Ordinal);
        Assert.Contains("ProofImageUrl = normalizedManualWalletProofFileKey", paymentService, StringComparison.Ordinal);
        Assert.Contains("DUPLICATE_PAYMENT_INTENT", paymentService, StringComparison.Ordinal);

        Assert.Contains("_manualPaymentRuntimeApplicationService.InitializeReferencePaymentAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentEvidenceApplicationService.AdmitEvidenceAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("AdmitPaymentReviewAsync", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentIntent.Status = PaymentIntentStatus.AwaitingVerification", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualWalletPaymentAdapter", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminApprovalAndRejection_MustBeBackendOnly()
    {
        var adminManual = ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");
        var storefrontRoot = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts")
            + ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx")
            + ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", adminManual, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", adminManual, StringComparison.Ordinal);
        Assert.Contains("IManualPaymentReviewApplicationService", adminManual, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentReviewApplicationService.ApproveAsync", adminManual, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentReviewApplicationService.RejectAsync", adminManual, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualWalletStateMachine.EnsureValidTransition", adminManual, StringComparison.Ordinal);
        Assert.Contains("pi.TenantId == tenantId", adminManual, StringComparison.Ordinal);
        Assert.Contains("pi.Provider == PaymentProvider.ManualWallet", adminManual, StringComparison.Ordinal);

        var reviewApplication = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        Assert.Contains("PaymentLifecycleSource.PaymentReviewApproval", reviewApplication, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewRejection", reviewApplication, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewApproval", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewRejection", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus can ONLY be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);

        Assert.DoesNotContain("/api/admin/payments/manual-wallet", storefrontRoot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manual-wallet/{paymentIntentId}/confirm", storefrontRoot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manual-wallet/{paymentIntentId}/reject", storefrontRoot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PaymentLifecycleSource.PaymentReviewApproval", storefrontRoot, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentIntentStatus.Paid", storefrontRoot, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheRecoveryConcurrencyAndCrossSystemPaths_MustReenterBackendManualPaymentAuthority()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var recoveryService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var cleanupJob = ReadRepoFile("PointOfSale.API/Jobs/ManualWalletProofCleanupJob.cs");

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.DoesNotContain("manualWalletProofFileKey", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletUploadToken", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("orderResult", localStoragePartialize, StringComparison.Ordinal);

        Assert.Contains("clearSensitiveCheckoutSessionStorage();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("if (state.isSubmitting)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_IN_PROGRESS", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("DUPLICATE_PAYMENT_INTENT", ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs"), StringComparison.Ordinal);

        Assert.Contains("requiresManualWalletReentry", recoverPage, StringComparison.Ordinal);
        Assert.Contains("requiresManualWalletReentry", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_RECOVERY_FACTS_MISSING", recoveryService, StringComparison.Ordinal);
        Assert.Contains("\"MANUALWALLET\" => false", recoveryService, StringComparison.Ordinal);
        Assert.Contains("intent.Provider == PaymentProvider.ManualWallet", recoveryService, StringComparison.Ordinal);
        Assert.Contains("PaymentProvider.ManualWallet", cleanupJob, StringComparison.Ordinal);
        Assert.Contains("TryNormalizeTenantScopedObjectKey", cleanupJob, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPay3Truth()
    {
        Assert.Contains(
            "Manual Payment Authority Certification (PASS SF-PAY3)",
            ReadRepoFile("docs/architecture/manual-payment-authority-certification-findings.md"),
            StringComparison.Ordinal);

        Assert.Contains(
            "IManualPaymentReviewApplicationService",
            ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs"),
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

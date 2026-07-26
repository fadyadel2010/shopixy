using System.Text.RegularExpressions;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class SystemBoundaryWeaknessHardeningTests
{
    [Fact]
    public void AffiliateSettlementActions_MustUseSqlLedgerNotMongoProjectionsForFinancialDecisions()
    {
        var source = File.ReadAllText(ApiPath("Controllers", "AdminAffiliateController.cs"));

        Assert.Contains("MongoDB projections are display-only", source, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionLedger is the financial authority", source, StringComparison.Ordinal);
        Assert.Contains("GetAffiliateOrderIdsFromCommissionLedgerAsync", source, StringComparison.Ordinal);
        Assert.Contains("CalculateApprovedCommissionPayoutFromLedgerAsync", source, StringComparison.Ordinal);

        var approveMethod = ExtractMethodBody(source, "ApproveCommissions");
        var payMethod = ExtractMethodBody(source, "RecordPayment");
        Assert.NotNull(approveMethod);
        Assert.NotNull(payMethod);

        Assert.DoesNotContain("_mongoDbContext.AffiliateOrders", approveMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateOrderDocument", approveMethod, StringComparison.Ordinal);
        Assert.Contains("CommissionEntryType.Earned", approveMethod, StringComparison.Ordinal);
        Assert.Contains("invalidOrderIds", approveMethod, StringComparison.Ordinal);

        Assert.DoesNotContain("_mongoDbContext.AffiliateOrders", payMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateOrderDocument", payMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalCommission", payMethod, StringComparison.Ordinal);
        Assert.Contains("CommissionEntryType.Approved", payMethod, StringComparison.Ordinal);
        Assert.Contains("CalculateApprovedCommissionPayoutFromLedgerAsync", payMethod, StringComparison.Ordinal);
        Assert.Contains("invalidOrderIds", payMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalProviderSideEffects_MustFailClosedInsideActiveEfTransactions()
    {
        var refundSource = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundGatewayService.cs"));
        var carrierSource = File.ReadAllText(InfrastructurePath("Shipping", "CarrierShipmentService.cs"));

        var refundMethod = ExtractMethodBody(refundSource, "RefundAsync");
        Assert.NotNull(refundMethod);
        Assert.Contains("ApplicationDbContext dbContext", refundSource, StringComparison.Ordinal);
        Assert.Contains("EnsureNoActiveLocalTransactionForExternalRefund", refundSource, StringComparison.Ordinal);
        Assert.Contains("Database.CurrentTransaction", refundSource, StringComparison.Ordinal);
        Assert.Matches(
            @"payment == null[\s\S]+EnsureNoActiveLocalTransactionForExternalRefund\(payment\.Provider\)[\s\S]+payment\.Provider switch",
            refundMethod);

        Assert.Contains("EnsureNoActiveLocalTransactionForExternalCarrierCall", carrierSource, StringComparison.Ordinal);
        Assert.Contains("Database.CurrentTransaction", carrierSource, StringComparison.Ordinal);
        Assert.True(
            Regex.Matches(carrierSource, @"EnsureNoActiveLocalTransactionForExternalCarrierCall\(carrierCode\)").Count >= 2,
            "Carrier registration must guard every adapter.CreateShipmentAsync external call.");
        Assert.Matches(
            @"EnsureNoActiveLocalTransactionForExternalCarrierCall\(carrierCode\)[\s\S]+adapter\.CreateShipmentAsync",
            carrierSource);
    }

    [Fact]
    public void CanonicalIdentityBoundary_MustHaveHistoricalBackfillAndTenantAwareUniqueness()
    {
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260424163000_S31_CanonicalCrossSystemIdentityLinks.sql"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CanonicalIdentityLinkConfiguration.cs"));

        Assert.Contains("UX_CanonicalIdentityLinks_Tenant_Subject_Active", migration, StringComparison.Ordinal);
        Assert.Contains("UX_CanonicalIdentityLinks_Global_Subject_Active", migration, StringComparison.Ordinal);
        Assert.Contains("UX_CanonicalIdentityLinks_Tenant_Subject_Active", configuration, StringComparison.Ordinal);
        Assert.Contains("UX_CanonicalIdentityLinks_Global_Subject_Active", configuration, StringComparison.Ordinal);
        Assert.Contains("NormalizeIdentityScope", dbContext, StringComparison.Ordinal);

        foreach (var tableName in new[]
                 {
                     "Users",
                     "Customers",
                     "OnlineCustomers",
                     "CommerceHubCustomers",
                     "AffiliateAccounts",
                     "AffiliateUsers",
                     "Tenants"
                 })
        {
            Assert.Contains($"FROM \"{tableName}\"", migration, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AuthoritativeEventPublication_MustBeRuntimeGuardedNotJustCommentDriven()
    {
        var source = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));

        Assert.Contains("EnsureTransactionalPublishBoundary", source, StringComparison.Ordinal);
        Assert.Contains("HasPendingBusinessMutation", source, StringComparison.Ordinal);
        Assert.Contains("IsAuthoritativeEventType", source, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Database.CurrentTransaction != null", source, StringComparison.Ordinal);
        Assert.Contains("must be published before the source mutation is committed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceInvariantInterceptor_MustBeRegisteredAtRuntime()
    {
        // This is an architecture test that verifies the interceptor is registered in the real application
        // by checking the Program.cs DI configuration rather than building a test service provider.
        
        var programSource = File.ReadAllText(ApiPath("Program.cs"));

        // Assert: Verify the interceptor is registered in the production DI container
        Assert.Contains("AddScoped<MarketplaceInvariantInterceptor>", programSource, StringComparison.Ordinal);
        
        // Verify it's registered alongside other SaveChanges interceptors for consistency
        Assert.Contains("AddScoped<PaymentStatusSaveChangesInterceptor>", programSource, StringComparison.Ordinal);
        Assert.Contains("AddScoped<FulfillmentStatusSaveChangesInterceptor>", programSource, StringComparison.Ordinal);
        
        // S40: Verify interceptors are actually attached to EF via AddInterceptors
        // Registration alone is insufficient - they must be added to DbContext options
        Assert.Contains("AddInterceptors(", programSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<MarketplaceInvariantInterceptor>()", programSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<PaymentStatusSaveChangesInterceptor>()", programSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<FulfillmentStatusSaveChangesInterceptor>()", programSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentRefundPayoutBoundaries_MustRequireExplicitMutationScope()
    {
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var scope = File.ReadAllText(InfrastructurePath("Data", "CommerceHubBoundaryMutationScope.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var refundService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var superAdminPayoutService = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutsService.cs"));
        var projectionConsumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubOrderProjectionConsumer.cs"));
        var financialWorkflowService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinancialWorkflowService.cs"));
        var orderManagementService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderManagementService.cs"));

        Assert.Contains("CommerceHubBoundaryMutationScope", scope, StringComparison.Ordinal);
        Assert.Contains("private static readonly AsyncLocal<ScopeState>", scope, StringComparison.Ordinal);
        Assert.Contains("public static bool IsOrderLifecycleAuthorized =>", scope, StringComparison.Ordinal);
        Assert.Contains("CurrentState.Value?.OrderLifecycleDepth > 0;", scope, StringComparison.Ordinal);
        Assert.Contains("public static bool IsPayoutAuthorized =>", scope, StringComparison.Ordinal);
        Assert.Contains("CurrentState.Value?.PayoutDepth > 0;", scope, StringComparison.Ordinal);
        Assert.Contains("public static bool IsFinancialWorkflowAuthorized =>", scope, StringComparison.Ordinal);
        Assert.Contains("CurrentState.Value?.FinancialWorkflowDepth > 0;", scope, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPaymentAuthorized ||", scope, StringComparison.Ordinal);
        Assert.DoesNotContain("IsRefundAuthorized ||", scope, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubBoundaryOwnership", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubPayment>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubPaymentAttempt>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubRefund>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubPayout>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubVendorEarning>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubMarketplaceOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedCreationState<CommerceHubPayment>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedCreationState<CommerceHubPaymentAttempt>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedCreationState<CommerceHubPaymentLog>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedCreationState<CommerceHubRefund>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedCreationState<CommerceHubPayout>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedCreationState<CommerceHubPayoutItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedCreationState<CommerceHubVendorEarning>", dbContext, StringComparison.Ordinal);
        Assert.Contains("cannot be created with payment-initiation state", dbContext, StringComparison.Ordinal);
        Assert.Contains("cannot be created with refund lifecycle state", dbContext, StringComparison.Ordinal);
        Assert.Contains("cannot be created with payout lifecycle state", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubFinancialLifecycleRecordImmutability", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPayment>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPaymentAttempt>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPaymentLog>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPaymentItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubRefund>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPayout>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPayoutItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubVendorEarning>", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub projection markers can only be changed by the CommerceHub projection boundary", dbContext, StringComparison.Ordinal);

        // Verify specific boundary scopes are used in their respective services
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayment()", paymentService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", paymentService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refundService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", refundService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", refundService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", payoutService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", superAdminPayoutService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", superAdminPayoutService, StringComparison.Ordinal);
        Assert.Contains("await EnsureEarningsHaveLedgerConfirmationAsync(", superAdminPayoutService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginProjection()", projectionConsumer, StringComparison.Ordinal);

        // Verify financial workflow boundary exists and is used in orchestration services
        Assert.Contains("BeginFinancialWorkflow()", scope, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", financialWorkflowService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", financialWorkflowService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", orderManagementService, StringComparison.Ordinal);

        var boundaryTests = File.ReadAllText(TestsPath("CommerceHub", "CommerceHubDataAuthorityBoundaryTests.cs"));
        Assert.Contains("PaymentFinancialIdentity_CannotBeChangedEvenInsidePaymentBoundary", boundaryTests, StringComparison.Ordinal);
        Assert.Contains("RefundFinancialIdentity_CannotBeChangedEvenInsideRefundBoundary", boundaryTests, StringComparison.Ordinal);
        Assert.Contains("PayoutFinancialIdentity_CannotBeChangedEvenInsidePayoutBoundary", boundaryTests, StringComparison.Ordinal);
        Assert.Contains("PaymentItemAllocation_CannotBeChangedAfterCreation", boundaryTests, StringComparison.Ordinal);
        Assert.Contains("PaymentCannotBeCreatedAsPaidOutsidePaymentBoundary", boundaryTests, StringComparison.Ordinal);
        Assert.Contains("RefundCannotBeCreatedAsCompletedOutsideRefundBoundary", boundaryTests, StringComparison.Ordinal);
        Assert.Contains("PayoutCannotBeCreatedAsCompletedOutsidePayoutBoundary", boundaryTests, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeStoredCheckoutCartItems_MustLogSanitizedJsonSnippetOnFailure()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));

        var deserializeMethod = ExtractMethodBody(source, "DeserializeStoredCheckoutCartItems");
        Assert.NotNull(deserializeMethod);

        // Verify the catch block includes sanitized JSON snippet in logging
        Assert.Contains("catch (JsonException ex)", deserializeMethod, StringComparison.Ordinal);
        Assert.Contains("SanitizeJsonForLogging", deserializeMethod, StringComparison.Ordinal);
        Assert.Contains("JsonSnippet", deserializeMethod, StringComparison.Ordinal);
        Assert.Contains("_logger.LogWarning", deserializeMethod, StringComparison.Ordinal);

        // Verify sanitization method exists and redacts sensitive fields
        Assert.Contains("private static string SanitizeJsonForLogging(string json)", source, StringComparison.Ordinal);
        Assert.Matches(@"email[\s\S]+\[REDACTED\]", source);
        Assert.Matches(@"phone[\s\S]+\[REDACTED\]", source);
        Assert.Matches(@"cardNumber[\s\S]+\[REDACTED\]", source);
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API", Path.Combine(parts)));

    private static string InfrastructurePath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Infrastructure", Path.Combine(parts)));

    private static string TestsPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Tests", Path.Combine(parts)));

    private static string? ExtractMethodBody(string source, string methodName)
    {
        var methodPattern = $@"\b{Regex.Escape(methodName)}\s*\(";
        foreach (Match match in Regex.Matches(source, methodPattern))
        {
            var openParenIndex = source.IndexOf('(', match.Index);
            if (openParenIndex == -1)
            {
                continue;
            }

            var closeParenIndex = FindMatching(source, openParenIndex, '(', ')');
            if (closeParenIndex == -1)
            {
                continue;
            }

            var openBraceIndex = FindMethodOpeningBrace(source, closeParenIndex + 1);
            if (openBraceIndex == -1)
            {
                continue;
            }

            var closeBraceIndex = FindMatching(source, openBraceIndex, '{', '}');
            if (closeBraceIndex != -1)
            {
                return source.Substring(match.Index, closeBraceIndex - match.Index + 1);
            }
        }

        return null;
    }

    private static int FindMatching(string source, int openIndex, char open, char close)
    {
        var depth = 1;
        for (var i = openIndex + 1; i < source.Length; i++)
        {
            if (source[i] == open)
            {
                depth++;
            }
            else if (source[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindMethodOpeningBrace(string source, int startIndex)
    {
        for (var i = startIndex; i < source.Length; i++)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                continue;
            }

            return source[i] == '{' ? i : -1;
        }

        return -1;
    }
}

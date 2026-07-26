using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PointOfSale.Tests.Architecture;

public sealed class CouponRecoveryAuditAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCouponRecoveryPresentation_MustRemainBackendDtoOnly()
    {
        var couponInput = ReadRepoFile("PointOfSale.Storefront/src/components/cart/CouponInput.tsx");
        var cartStore = ReadRepoFile("PointOfSale.Storefront/src/store/cartStore.ts");
        var storefrontApi = ReadRepoFile("PointOfSale.Storefront/src/services/api/storefrontApi.ts");

        Assert.Contains("const success = await applyCoupon(normalizedCode);", couponInput, StringComparison.Ordinal);
        Assert.Contains("const cart = await applyCouponApi(code, smartOfferRedeemToken);", cartStore, StringComparison.Ordinal);
        Assert.Contains("const cart = await removeCouponApi();", cartStore, StringComparison.Ordinal);
        Assert.Contains("detectCartStateMismatch(cart)", cartStore, StringComparison.Ordinal);
        Assert.Contains("hydrationSource: 'api'", cartStore, StringComparison.Ordinal);
        Assert.Contains("hydrationSource: 'recovery'", cartStore, StringComparison.Ordinal);
        Assert.Contains("skipNextFetch: false", cartStore, StringComparison.Ordinal);
        Assert.Contains("httpClient.post<CartDto>(ENDPOINTS.CART_APPLY_COUPON, request)", storefrontApi, StringComparison.Ordinal);
        Assert.Contains("httpClient.post<CartDto>(ENDPOINTS.CART_REMOVE_COUPON, {})", storefrontApi, StringComparison.Ordinal);

        Assert.DoesNotContain("CouponReservation", couponInput, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponUsage", couponInput, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponReservation", cartStore, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponUsage", cartStore, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", couponInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", couponInput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CouponReservationUsageAndRelease_MustBeTransactionalSqlOwnedAndReplaySafe()
    {
        var couponAuthority = ReadRepoFile("PointOfSale.Infrastructure/Discounts/CouponValidationService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");

        Assert.Contains("EnsureTransactionalMutation(\"Coupon reservation\")", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon confirmation\")", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon release\")", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("await LockCouponForCapacityMutationAsync", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("ValidateCouponByIdAsync(", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateReservationOrderException", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateReservationCustomerException", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateUsageOrderException", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("CouponUsages.Add(couponUsage)", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("CouponConsumedEvent", couponAuthority, StringComparison.Ordinal);

        Assert.Contains("IX_CouponReservations_TenantId_OrderId", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_CouponReservations_TenantId_CouponId_CustomerId", dbContext, StringComparison.Ordinal);
        Assert.Contains("cu => new { cu.TenantId, cu.OrderId }", dbContext, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", dbContext, StringComparison.Ordinal);

        var createOrder = ExtractMethodSource(checkoutService, "CreateOrderAsync");
        var transactionIndex = createOrder.IndexOf("BeginTransactionAsync(", StringComparison.Ordinal);
        var reserveIndex = createOrder.IndexOf("await _couponValidationService.ReserveCouponAsync(", StringComparison.Ordinal);
        var commitIndex = createOrder.IndexOf("await transaction.CommitAsync(cancellationToken);", StringComparison.Ordinal);
        Assert.True(transactionIndex >= 0, "Checkout must open a SQL transaction.");
        Assert.True(reserveIndex > transactionIndex, "Coupon reservation must occur inside checkout SQL transaction.");
        Assert.True(commitIndex > reserveIndex, "Coupon reservation must commit atomically with order persistence.");
        Assert.Contains("IsolationLevel.Serializable", createOrder, StringComparison.Ordinal);

        Assert.Contains("ConfirmCouponUsageAsync(", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("ReleaseCouponReservationAsync(", paymentLifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void CouponProjectionRecovery_MustRebuildOnlyFromSqlTruthAndCollapseConsumerReplay()
    {
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs");
        var repair = ReadRepoFile("PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CouponsController.cs");

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("Coupon projection duplicate or active replay collapsed before MongoDB writes", projection, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCouponAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("MapSqlAuthorityToDocument(sqlCoupon, @event)", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectCouponUsageFromSqlAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CouponUsages", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(@event)", projection, StringComparison.Ordinal);
        Assert.Contains("BuildTombstoneFromSqlAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentUsage = @event.CurrentUsage", projection, StringComparison.Ordinal);

        Assert.Contains("RebuildCouponsAsync", controller, StringComparison.Ordinal);
        Assert.Contains("Coupons rebuilt from SQL authority into MongoDB", controller, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Coupons", repair, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", repair, StringComparison.Ordinal);
        Assert.Contains("BuildCouponDocument(coupon)", repair, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync(staleFilter", repair, StringComparison.Ordinal);
    }

    [Fact]
    public void CouponAuditTrailReads_MustRemainBackendOwnedTenantScopedAndNoStore()
    {
        var auditController = ReadRepoFile("PointOfSale.API/Controllers/AuditLogsController.cs");
        var auditPage = ReadRepoFile("PointOfSale.Web/src/pages/AuditLog.jsx");

        Assert.Contains("[Route(\"api/audit-logs\")]", auditController, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", auditController, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", auditController, StringComparison.Ordinal);
        Assert.Contains("_context.AuditLogs", auditController, StringComparison.Ordinal);
        Assert.Contains(".AsNoTracking()", auditController, StringComparison.Ordinal);
        Assert.Contains(".Where(x => x.TenantId == tenantId)", auditController, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(x => x.TimestampUtc)", auditController, StringComparison.Ordinal);
        Assert.Contains("BeforeDataJson", auditController, StringComparison.Ordinal);
        Assert.Contains("AfterDataJson", auditController, StringComparison.Ordinal);

        Assert.Contains("api.get('/audit-logs'", auditPage, StringComparison.Ordinal);
        Assert.Contains("api.get('/audit-logs/export'", auditPage, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", auditPage, StringComparison.Ordinal);
        Assert.Contains("Pragma: 'no-cache'", auditPage, StringComparison.Ordinal);
        Assert.DoesNotContain("api.post('/audit-logs", auditPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordCouponRecoveryAuditAuthorityTruth()
    {
        Assert.Contains(
            "PASS SF-CPN6 Coupon Recovery and Audit Authority Certification",
            ReadRepoFile("docs/architecture/coupon-recovery-audit-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-CPN6", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ExtractMethodSource(string source, string methodName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var root = syntaxTree.GetRoot();
        var methodDeclaration = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.Text == methodName);

        Assert.True(methodDeclaration != null, $"Expected source to contain method {methodName}.");
        return methodDeclaration!.ToFullString();
    }
}

using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.Promotions;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class ManualWalletCheckoutValidationForensicTests
{
    private static readonly MethodInfo ResolveProofMethod =
        typeof(CheckoutService).GetMethod(
            "ResolveValidatedManualWalletProofFileKeyAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Manual wallet proof resolver was not found.");
    private static readonly MethodInfo ParsePaymentMethodMethod =
        typeof(CheckoutService).GetMethod(
            "ParsePaymentMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Checkout payment method parser was not found.");

    [Fact]
    public async Task ManualWalletPaymentMethod_MapsToBankTransferInsteadOfCard()
    {
        await using var context = CreateInMemoryContext();
        var service = CreateCheckoutService(context);

        var paymentMethod = Assert.IsType<PaymentMethod>(
            ParsePaymentMethodMethod.Invoke(service, new object[] { "MANUALWALLET" }));

        Assert.Equal(PaymentMethod.BankTransfer, paymentMethod);
    }

    [Fact]
    public async Task ManualWalletProofSessionOrdering_Postgres_TranslatesCanonicalMappedOrdering()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext))
        {
            return;
        }

        await using (databaseScope!)
        {
            var cartId = Guid.NewGuid();
            var nowUtc = DateTime.UtcNow;
            var expectedSessionId = "manual-wallet-pg-session";
            var deterministicLowerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            var deterministicHigherId = Guid.Parse("f0000000-0000-0000-0000-000000000002");

            await using var context = databaseScope.CreateDbContext(tenantContext!);
            await context.Database.ExecuteSqlRawAsync(
                """DROP INDEX IF EXISTS "IX_CheckoutSessions_TenantId_CartId_Active_Unique";""");

            context.Tenants.Add(NewTenant(tenantId, "manual-wallet-pg"));
            context.CheckoutSessions.AddRange(
                NewCheckoutSession(
                    tenantId,
                    cartId,
                    expectedSessionId,
                    CheckoutSessionStatus.Abandoned,
                    nowUtc.AddMinutes(10),
                    nowUtc.AddMinutes(-10),
                    proofImageUrl: TenantProofKey(tenantId, "abandoned-newer.png")),
                NewCheckoutSession(
                    tenantId,
                    cartId,
                    expectedSessionId,
                    CheckoutSessionStatus.Active,
                    nowUtc,
                    nowUtc,
                    proofImageUrl: TenantProofKey(tenantId, "active-lower.png"),
                    id: deterministicLowerId),
                NewCheckoutSession(
                    tenantId,
                    cartId,
                    expectedSessionId,
                    CheckoutSessionStatus.Active,
                    nowUtc,
                    nowUtc,
                    proofImageUrl: TenantProofKey(tenantId, "active-higher.png"),
                    id: deterministicHigherId));
            await context.SaveChangesAsync();

            var checkoutSessionEntity = context.Model.FindEntityType(typeof(CheckoutSession));
            Assert.NotNull(checkoutSessionEntity);
            Assert.Null(checkoutSessionEntity!.FindProperty(nameof(CheckoutSession.UpdatedAt)));
            Assert.NotNull(checkoutSessionEntity.FindProperty(nameof(CheckoutSession.LastActivityAtUtc)));
            Assert.NotNull(checkoutSessionEntity.FindProperty(nameof(CheckoutSession.CreatedAtUtc)));

            var candidateQuery = context.CheckoutSessions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(session =>
                    session.TenantId == tenantId &&
                    session.CartId == cartId &&
                    (session.Status == CheckoutSessionStatus.Active ||
                     session.Status == CheckoutSessionStatus.Abandoned) &&
                    session.SessionId == expectedSessionId);

            var selected = await CheckoutSessionService
                .OrderCanonicalSessions(candidateQuery)
                .FirstOrDefaultAsync();

            Assert.NotNull(selected);
            Assert.Equal(deterministicHigherId, selected!.Id);
            Assert.Equal(CheckoutSessionStatus.Active, selected.Status);
        }
    }

    [Fact]
    public async Task ResolveValidatedManualWalletProofFileKeyAsync_AcceptsLatestActiveMatchingProof()
    {
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var sessionId = "guest-manual-wallet";
        var nowUtc = DateTime.UtcNow;
        var expectedProof = TenantProofKey(tenantId, "latest-active.png");

        context.CheckoutSessions.AddRange(
            NewCheckoutSession(
                tenantId,
                cartId,
                sessionId,
                CheckoutSessionStatus.Active,
                nowUtc.AddMinutes(-5),
                nowUtc.AddMinutes(-10),
                proofImageUrl: TenantProofKey(tenantId, "older-active.png")),
            NewCheckoutSession(
                tenantId,
                cartId,
                sessionId,
                CheckoutSessionStatus.Abandoned,
                nowUtc.AddMinutes(5),
                nowUtc.AddMinutes(-20),
                proofImageUrl: TenantProofKey(tenantId, "newer-abandoned.png")),
            NewCheckoutSession(
                tenantId,
                cartId,
                sessionId,
                CheckoutSessionStatus.Active,
                nowUtc,
                nowUtc.AddMinutes(-1),
                proofImageUrl: expectedProof));
        await context.SaveChangesAsync();

        var service = CreateCheckoutService(context);
        var resolvedProof = await InvokeResolveProofAsync(
            service,
            tenantId,
            sessionId,
            "session",
            sessionId,
            customerId: null,
            cartId,
            expectedProof,
            orderId: null);

        Assert.Equal(expectedProof, resolvedProof);
    }

    [Fact]
    public async Task ResolveValidatedManualWalletProofFileKeyAsync_EnforcesTenantCartOwnerStatusAndOrderBinding()
    {
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var otherCartId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var validProof = TenantProofKey(tenantId, "customer-proof.png");
        var convertedProof = TenantProofKey(tenantId, "converted-proof.png");
        var invalidatedProof = TenantProofKey(tenantId, "invalidated-proof.png");
        var nowUtc = DateTime.UtcNow;

        context.CheckoutSessions.AddRange(
            NewCheckoutSession(
                tenantId,
                cartId,
                sessionId: null,
                CheckoutSessionStatus.Active,
                nowUtc,
                nowUtc,
                validProof,
                onlineCustomerId: customerId),
            NewCheckoutSession(
                tenantId,
                cartId,
                sessionId: null,
                CheckoutSessionStatus.Active,
                nowUtc.AddMinutes(1),
                nowUtc.AddMinutes(1),
                invalidatedProof,
                onlineCustomerId: customerId,
                invalidatedAtUtc: nowUtc),
            NewCheckoutSession(
                tenantId,
                otherCartId,
                "guest-wrong-cart",
                CheckoutSessionStatus.Active,
                nowUtc,
                nowUtc,
                TenantProofKey(tenantId, "wrong-cart.png")),
            NewCheckoutSession(
                otherTenantId,
                cartId,
                "guest-wrong-tenant",
                CheckoutSessionStatus.Active,
                nowUtc,
                nowUtc,
                TenantProofKey(otherTenantId, "wrong-tenant.png")),
            NewCheckoutSession(
                tenantId,
                cartId,
                "converted-session",
                CheckoutSessionStatus.ConvertedToOrder,
                nowUtc,
                nowUtc,
                convertedProof,
                convertedOrderId: orderId));
        await context.SaveChangesAsync();

        var service = CreateCheckoutService(context);

        var acceptedCustomerProof = await InvokeResolveProofAsync(
            service,
            tenantId,
            customerId.ToString("N"),
            "customer",
            sessionId: null,
            customerId,
            cartId,
            validProof,
            orderId: null);
        Assert.Equal(validProof, acceptedCustomerProof);

        await Assert.ThrowsAsync<CheckoutManualWalletProofException>(() =>
            InvokeResolveProofAsync(
                service,
                tenantId,
                customerId.ToString("N"),
                "customer",
                sessionId: null,
                customerId,
                cartId,
                invalidatedProof,
                orderId: null));

        await Assert.ThrowsAsync<CheckoutManualWalletProofException>(() =>
            InvokeResolveProofAsync(
                service,
                tenantId,
                customerId.ToString("N"),
                "customer",
                sessionId: null,
                otherCustomerId,
                cartId,
                validProof,
                orderId: null));

        await Assert.ThrowsAsync<CheckoutManualWalletProofException>(() =>
            InvokeResolveProofAsync(
                service,
                tenantId,
                "guest-wrong-cart",
                "session",
                "guest-wrong-cart",
                customerId: null,
                cartId,
                TenantProofKey(tenantId, "wrong-cart.png"),
                orderId: null));

        await Assert.ThrowsAsync<CheckoutManualWalletProofException>(() =>
            InvokeResolveProofAsync(
                service,
                tenantId,
                "converted-session",
                "session",
                "converted-session",
                customerId: null,
                cartId,
                convertedProof,
                orderId: null));

        await Assert.ThrowsAsync<CheckoutManualWalletProofException>(() =>
            InvokeResolveProofAsync(
                service,
                tenantId,
                "converted-session",
                "session",
                "converted-session",
                customerId: null,
                cartId,
                convertedProof,
                otherOrderId));

        var acceptedConvertedProof = await InvokeResolveProofAsync(
            service,
            tenantId,
            "converted-session",
            "session",
            "converted-session",
            customerId: null,
            cartId,
            convertedProof,
            orderId);
        Assert.Equal(convertedProof, acceptedConvertedProof);

        await Assert.ThrowsAsync<CheckoutManualWalletProofException>(() =>
            InvokeResolveProofAsync(
                service,
                otherTenantId,
                "guest-wrong-tenant",
                "session",
                "guest-wrong-tenant",
                customerId: null,
                cartId,
                TenantProofKey(tenantId, "wrong-tenant.png"),
                orderId: null));
    }

    [Fact]
    public async Task ResolveValidatedManualWalletProofFileKeyAsync_RejectsMissingOrReplacedProof()
    {
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var sessionId = "guest-replaced-proof";
        var storedProof = TenantProofKey(tenantId, "stored.png");
        var replacedProof = TenantProofKey(tenantId, "browser-stale.png");

        context.CheckoutSessions.Add(NewCheckoutSession(
            tenantId,
            cartId,
            sessionId,
            CheckoutSessionStatus.Active,
            DateTime.UtcNow,
            DateTime.UtcNow,
            storedProof));
        await context.SaveChangesAsync();

        var service = CreateCheckoutService(context);

        await Assert.ThrowsAsync<CheckoutManualWalletProofException>(() =>
            InvokeResolveProofAsync(
                service,
                tenantId,
                sessionId,
                "session",
                sessionId,
                customerId: null,
                cartId,
                proofFileKey: null,
                orderId: null));

        await Assert.ThrowsAsync<CheckoutManualWalletProofException>(() =>
            InvokeResolveProofAsync(
                service,
                tenantId,
                sessionId,
                "session",
                sessionId,
                customerId: null,
                cartId,
                replacedProof,
                orderId: null));
    }

    [Fact]
    public void ManualWalletProofResolver_DoesNotUseUnmappedUpdatedAtOrClientSideEvaluation()
    {
        var checkoutService = PointOfSale.Tests.Architecture.CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutSessionService = PointOfSale.Tests.Architecture.CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Services/CheckoutSessionService.cs");
        var dbContext = PointOfSale.Tests.Architecture.CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        var start = checkoutService.IndexOf(
            "private async Task<string> ResolveValidatedManualWalletProofFileKeyAsync",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "Manual wallet proof resolver was not found in CheckoutService.");

        var end = checkoutService.IndexOf("#endregion", start, StringComparison.Ordinal);
        Assert.True(end > start, "Manual wallet proof resolver region end was not found.");
        var resolverSource = checkoutService[start..end];

        Assert.DoesNotContain("session.UpdatedAt", resolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AsEnumerable", resolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToList(", resolverSource, StringComparison.Ordinal);
        Assert.Contains("CheckoutSessionService.OrderCanonicalSessions", resolverSource, StringComparison.Ordinal);
        Assert.Contains("session.InvalidatedAtUtc == null", resolverSource, StringComparison.Ordinal);
        Assert.Contains("session.LastActivityAtUtc", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("entity.Ignore(e => e.UpdatedAt);", dbContext, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(e => new { e.Status, e.LastActivityAtUtc })", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualWalletCheckoutResponse_DoesNotExposeInternalReferenceAsFawryArtifact()
    {
        const string internalReference = "MW-ORDER-INTERNAL-REFERENCE";

        Assert.Null(CheckoutService.GetStorefrontReferenceCode("MANUALWALLET", internalReference));
        Assert.Equal(internalReference, CheckoutService.GetStorefrontReferenceCode("FAWRY", internalReference));

        var checkoutService = PointOfSale.Tests.Architecture.CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains(
            "ReferenceCode = GetStorefrontReferenceCode(paymentMethod.Code, referenceCode)",
            checkoutService,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReferenceCode = GetStorefrontReferenceCode(paymentMethodCode, paymentIntent?.ReferenceCode)",
            checkoutService,
            StringComparison.Ordinal);
        Assert.Contains(
            "replayResponse.ReferenceCode = GetStorefrontReferenceCode(paymentMethodCode, paymentIntent.ReferenceCode)",
            checkoutService,
            StringComparison.Ordinal);
    }

    private static async Task<string> InvokeResolveProofAsync(
        CheckoutService service,
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        Guid cartId,
        string? proofFileKey,
        Guid? orderId)
    {
        var task = (Task<string>)ResolveProofMethod.Invoke(
            service,
            new object?[]
            {
                tenantId,
                ownerId,
                ownerType,
                sessionId,
                customerId,
                cartId,
                proofFileKey,
                orderId,
                CancellationToken.None
            })!;

        return await task;
    }

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static CheckoutService CreateCheckoutService(ApplicationDbContext dbContext)
    {
        var runtimeReadiness = new Mock<IStorefrontPaymentRuntimeReadinessService>();
        runtimeReadiness
            .Setup(service => service.EvaluateAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorefrontPaymentRuntimeReadiness.Ready);

        return new CheckoutService(
            dbContext,
            Mock.Of<IProductVariantReadRepository>(),
            Mock.Of<IProductReadRepository>(),
            Mock.Of<IShippingMethodReadRepository>(),
            Mock.Of<IPaymentMethodReadRepository>(),
            Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<IMongoDatabase>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ICouponValidationService>(),
            Mock.Of<IPromotionEngine>(),
            Mock.Of<PointOfSale.API.Services.Payments.ProviderContracts.IPaymentGateway>(),
            runtimeReadiness.Object,
            Mock.Of<IStorefrontPricingService>(),
            Mock.Of<ICheckoutExecutionLockManager>(),
            Mock.Of<ICheckoutRequestIdempotencyCoordinator>(),
            Mock.Of<IMarketplaceEventOutboxService>(),
            Mock.Of<IPhoneRiskProfileService>(),
            Mock.Of<IOrderVelocityService>(),
            new EphemeralDataProtectionProvider(),
            Mock.Of<IPaymentIntentClientSecretProtector>(),
            Mock.Of<IPaymobSensitiveArtifactProtector>(),
            Mock.Of<ICheckoutSessionTokenService>(),
            Mock.Of<ICheckoutAttachmentService>(),
            Mock.Of<IWarehouseResolutionService>(),
            Options.Create(new R2Options()),
            Options.Create(new PaymentLifecycleSettings { RequireRowLevelLocking = false }),
            NullLogger<CheckoutService>.Instance,
            new HttpContextAccessor(),
            Mock.Of<ICartService>(),
            reliabilityTelemetry: null,
            addressAdmissionService: Mock.Of<IAddressAdmissionService>(),
            carrierIntegrationAuthority: Mock.Of<ICarrierIntegrationAuthority>());
    }

    private static Tenant NewTenant(Guid tenantId, string slugPrefix) => new()
    {
        Id = tenantId,
        Name = "Manual Wallet Validation Tenant",
        Slug = $"{slugPrefix}-{tenantId:N}",
        Currency = "EGP",
        IsActive = true,
        Status = TenantStatus.Active,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static CheckoutSession NewCheckoutSession(
        Guid tenantId,
        Guid cartId,
        string? sessionId,
        CheckoutSessionStatus status,
        DateTime lastActivityAtUtc,
        DateTime createdAtUtc,
        string? proofImageUrl,
        Guid? id = null,
        Guid? onlineCustomerId = null,
        Guid? convertedOrderId = null,
        DateTime? invalidatedAtUtc = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = tenantId,
        CartId = cartId,
        SessionId = sessionId,
        OnlineCustomerId = onlineCustomerId,
        Status = status,
        CreatedAtUtc = createdAtUtc,
        LastActivityAtUtc = lastActivityAtUtc,
        ProofImageUrl = proofImageUrl,
        ConvertedOrderId = convertedOrderId,
        ConvertedAtUtc = convertedOrderId.HasValue ? lastActivityAtUtc : null,
        InvalidatedAtUtc = invalidatedAtUtc,
        ItemsSnapshotJson = "[]",
        RecoveryToken = Guid.NewGuid().ToString("N")
    };

    private static string TenantProofKey(Guid tenantId, string fileName) =>
        $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{Guid.NewGuid():D}/{fileName}";
}

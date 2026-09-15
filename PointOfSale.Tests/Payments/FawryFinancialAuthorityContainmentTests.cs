using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

/// <summary>
/// T-09 Fawry financial authority containment.
///
/// Fawry payment transport exists, but the application has no certified Fawry
/// clearing account, accounting mapping, settlement feed, or settlement
/// authority. Every new Fawry financial admission must fail closed with
/// FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE before any business or financial
/// mutation, while Stripe, Paymob/Card/Visa, COD, and Manual Wallet keep
/// their current behavior.
/// </summary>
public sealed class FawryFinancialAuthorityContainmentTests
{
    private const string AuthorityCode = FawryFinancialAuthorityPolicy.Code;

    // ── Admin catalog ────────────────────────────────────────────────

    [Fact]
    public void AdminCatalog_MarksFawryUnsupportedWithStableCode()
    {
        var controller = CreateAdminController(Guid.NewGuid(), out _);

        var result = Assert.IsType<OkObjectResult>(controller.GetProviderCatalog());
        var catalog = Assert.IsAssignableFrom<List<PaymentMethodCatalogProviderDto>>(result.Value);

        var fawry = Assert.Single(catalog, item => item.Code == "FAWRY");
        Assert.False(fawry.IsSupported);
        Assert.Equal(AuthorityCode, fawry.UnsupportedReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(fawry.UnsupportedMessage));

        Assert.All(
            catalog.Where(item => item.Code is "STRIPE" or "PAYMOB" or "COD" or "MANUALWALLET"),
            item => Assert.True(item.IsSupported));
    }

    [Theory]
    [InlineData("FAWRY")]
    [InlineData("fawry")]
    [InlineData("  fawry  ")]
    public async Task AdminMutations_RejectFawryWithoutPersistence(string code)
    {
        var tenantId = Guid.NewGuid();
        var publisher = new Mock<IEventPublisher>();
        var controller = CreateAdminController(tenantId, out var dbContext, publisher);

        var upsert = await controller.UpsertPaymentMethod(code, new UpsertPaymentMethodDto(), CancellationToken.None);
        var configure = await controller.UpdateConfiguration(code, new UpsertPaymentMethodDto(), CancellationToken.None);
        var enable = await controller.EnablePaymentMethod(code, CancellationToken.None);

        AssertAuthorityRejection(upsert);
        AssertAuthorityRejection(configure);
        AssertAuthorityRejection(enable);
        Assert.Empty(await dbContext.TenantPaymentMethods.ToListAsync());

        publisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        publisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodUpdatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        publisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodEnabledEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AdminGet_ExistingEnabledFawryRowRendersUnsupportedWithoutRewrite()
    {
        var tenantId = Guid.NewGuid();
        var controller = CreateAdminController(tenantId, out var dbContext);
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "FAWRY",
            DisplayName = "Fawry Pay",
            IsEnabled = true,
            SupportsOnline = true,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetPaymentMethod("FAWRY", CancellationToken.None));
        var dto = Assert.IsType<PaymentMethodResponseDto>(result.Value);
        Assert.False(dto.IsSupported);
        Assert.Equal(AuthorityCode, dto.UnsupportedReasonCode);

        var stored = await dbContext.TenantPaymentMethods.SingleAsync();
        Assert.True(stored.IsEnabled);
    }

    // ── Storefront runtime readiness ─────────────────────────────────

    [Fact]
    public async Task StorefrontReadiness_BlocksFawryEvenWhenTenantRowIsEnabled()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateReadinessContext();
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "FAWRY",
            DisplayName = "Fawry Pay",
            IsEnabled = true,
            SupportsOnline = true,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = CreateReadinessService(dbContext);
        var result = await service.EvaluateAsync(tenantId, "FAWRY");

        Assert.False(result.IsReady);
        Assert.Equal(AuthorityCode, result.FirstFailedGate);
    }

    [Fact]
    public async Task StorefrontReadiness_BlocksFawryBeforeTenantMethodLookup()
    {
        await using var dbContext = CreateReadinessContext();

        var result = await CreateReadinessService(dbContext).EvaluateAsync(Guid.NewGuid(), "FAWRY");

        Assert.False(result.IsReady);
        Assert.Equal(AuthorityCode, result.FirstFailedGate);
    }

    [Fact]
    public async Task StorefrontReadiness_KeepsCodAvailable()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateReadinessContext();
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "COD",
            DisplayName = "Cash on Delivery",
            IsEnabled = true,
            SupportsOffline = true,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = CreateReadinessService(dbContext);
        var result = await service.EvaluateAsync(tenantId, "COD");

        Assert.True(result.IsReady);
    }

    // ── Accounting resolver ──────────────────────────────────────────

    [Theory]
    [InlineData("Fawry")]
    [InlineData("FAWRY")]
    [InlineData("fawry")]
    public async Task OnlineCapture_FawryFailsWithStableCodeBeforeResolverOrPosting(string gateway)
    {
        var tenantId = Guid.NewGuid();
        var postingService = new Mock<IFinancialPostingService>(MockBehavior.Strict);
        var accountResolver = new Mock<IAccountResolver>(MockBehavior.Strict);
        var service = CreateCommerceAccountingService(postingService, accountResolver);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = "ORD-T09-FAWRY",
            Total = 100m,
            Currency = "EGP"
        };

        var result = await service.PostOnlinePaymentCapturedAsync(order, "pi_t09", gateway);

        Assert.False(result.Success);
        Assert.Contains(AuthorityCode, result.ErrorMessage, StringComparison.Ordinal);
        accountResolver.Verify(
            resolver => resolver.GetPaymentAccountIdAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        postingService.Verify(
            poster => poster.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("MysteryPay")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OnlineCapture_UnknownGatewayNeverFallsBackToStripe(string gateway)
    {
        var tenantId = Guid.NewGuid();
        var postingService = new Mock<IFinancialPostingService>(MockBehavior.Strict);
        var accountResolver = new Mock<IAccountResolver>(MockBehavior.Strict);
        var service = CreateCommerceAccountingService(postingService, accountResolver);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = "ORD-T09-UNKNOWN",
            Total = 100m,
            Currency = "EGP"
        };

        var result = await service.PostOnlinePaymentCapturedAsync(order, "pi_t09", gateway);

        Assert.False(result.Success);
        Assert.DoesNotContain(AuthorityCode, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        accountResolver.Verify(
            resolver => resolver.GetPaymentAccountIdAsync(
                It.IsAny<Guid>(), It.Is<string>(code => code == "STRIPE"), It.IsAny<CancellationToken>()),
            Times.Never);
        postingService.Verify(
            poster => poster.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnlineCapture_StripeKeepsExplicitMapping()
    {
        var tenantId = Guid.NewGuid();
        var clearingId = Guid.NewGuid();
        var salesId = Guid.NewGuid();
        var postingService = new Mock<IFinancialPostingService>(MockBehavior.Strict);
        var accountResolver = new Mock<IAccountResolver>(MockBehavior.Strict);
        accountResolver
            .Setup(resolver => resolver.GetPaymentAccountIdAsync(tenantId, "STRIPE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clearingId);
        accountResolver
            .Setup(resolver => resolver.GetByCodeAsync(tenantId, AccountCodes.EcommerceSales, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccount(tenantId, salesId, AccountCodes.EcommerceSales));
        postingService
            .Setup(poster => poster.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostingRequest request, CancellationToken _) => JournalEntry.Create(
                request.TenantId, "JE-T09", request.JournalDate, request.SourceType,
                request.SourceId, request.SourceReference, request.Description,
                request.IdempotencyKey, null));
        var service = CreateCommerceAccountingService(postingService, accountResolver);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = "ORD-T09-STRIPE",
            Total = 100m,
            Currency = "EGP"
        };

        var result = await service.PostOnlinePaymentCapturedAsync(order, "pi_t09", "Stripe");

        Assert.True(result.Success, result.ErrorMessage);
        accountResolver.Verify(
            resolver => resolver.GetPaymentAccountIdAsync(tenantId, "STRIPE", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("Paymob", "CARD")]
    [InlineData("Card", "CARD")]
    [InlineData("Visa", "CARD")]
    [InlineData("ManualWallet", "WALLET")]
    public async Task OnlineCapture_SupportedAliasesKeepExplicitMappings(string gateway, string expectedCode)
    {
        var tenantId = Guid.NewGuid();
        var clearingId = Guid.NewGuid();
        var salesId = Guid.NewGuid();
        var postingService = new Mock<IFinancialPostingService>(MockBehavior.Strict);
        var accountResolver = new Mock<IAccountResolver>(MockBehavior.Strict);
        accountResolver
            .Setup(resolver => resolver.GetPaymentAccountIdAsync(tenantId, expectedCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(clearingId);
        accountResolver
            .Setup(resolver => resolver.GetByCodeAsync(tenantId, AccountCodes.EcommerceSales, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccount(tenantId, salesId, AccountCodes.EcommerceSales));
        postingService
            .Setup(poster => poster.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostingRequest request, CancellationToken _) => JournalEntry.Create(
                request.TenantId, "JE-T09-ALIAS", request.JournalDate, request.SourceType,
                request.SourceId, request.SourceReference, request.Description,
                request.IdempotencyKey, null));
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = "ORD-T09-ALIAS",
            PaymentMethodCode = gateway,
            PaymentGateway = gateway,
            Total = 100m,
            Currency = "EGP"
        };

        var result = await CreateCommerceAccountingService(postingService, accountResolver)
            .PostOnlinePaymentCapturedAsync(order, "pi_t09_alias", gateway);

        Assert.True(result.Success, result.ErrorMessage);
        accountResolver.Verify(
            resolver => resolver.GetPaymentAccountIdAsync(tenantId, expectedCode, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnlineCapture_DisguisedFawryOrderCannotPostToStripe()
    {
        var postingService = new Mock<IFinancialPostingService>(MockBehavior.Strict);
        var accountResolver = new Mock<IAccountResolver>(MockBehavior.Strict);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderNumber = "ORD-T09-DISGUISED",
            PaymentMethodCode = "FAWRY",
            PaymentGateway = "Fawry",
            Total = 100m,
            Currency = "EGP"
        };

        var result = await CreateCommerceAccountingService(postingService, accountResolver)
            .PostOnlinePaymentCapturedAsync(order, "pi_t09_disguised", "Stripe");

        Assert.False(result.Success);
        Assert.Contains(AuthorityCode, result.ErrorMessage, StringComparison.Ordinal);
        accountResolver.VerifyNoOtherCalls();
        postingService.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MysteryPay")]
    public async Task SuspenseClearance_RequiresExplicitSupportedProvider(string? gateway)
    {
        var postingService = new Mock<IFinancialPostingService>(MockBehavior.Strict);
        var accountResolver = new Mock<IAccountResolver>(MockBehavior.Strict);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderNumber = "ORD-T09-SUSPENSE",
            Total = 100m,
            Currency = "EGP"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateCommerceAccountingService(postingService, accountResolver)
                .PostResolvedUnresolvedPaymentAsync(order, 100m, gateway, "evt-t09"));

        accountResolver.VerifyNoOtherCalls();
        postingService.VerifyNoOtherCalls();
    }

    // ── Settlement ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Fawry")]
    [InlineData("FAWRY")]
    public async Task GatewaySettlement_RejectsFawryBeforeMutation(string gateway)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateLifecycleService(context, commerceAccounting, eventPublisher);
        var order = await SeedCollectedOrderAsync(context, tenantId, total: 100m, paymentMethodCode: "FAWRY", paymentGateway: "Fawry");

        var result = await service.RecordGatewaySettlementAsync(new GatewaySettlementRequest
        {
            TenantId = tenantId,
            SettlementId = "fawry-settlement-001",
            Gateway = gateway,
            GrossAmount = 100m,
            Fees = 1m,
            Currency = "EGP",
            OrderIds = new List<Guid> { order.Id }
        });

        Assert.False(result.Success);
        Assert.Equal(AuthorityCode, result.FailureCode);
        Assert.Empty(await context.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await context.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(
            PaymentStatus.Collected,
            (await context.Orders.SingleAsync(item => item.Id == order.Id)).PaymentStatus);
        commerceAccounting.Verify(
            dependency => dependency.PostGatewaySettlementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        eventPublisher.Verify(
            dependency => dependency.PublishAsync(
                It.IsAny<OrderPaymentStatusChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GatewaySettlement_RejectsDisguisedFawryIntentBeforeMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var service = CreateLifecycleService(
            context,
            new Mock<ICommerceAccountingService>(MockBehavior.Strict),
            new Mock<IEventPublisher>());
        var order = await SeedCollectedOrderAsync(context, tenantId, 100m, "STRIPE", "Stripe");
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Amount = order.Total,
            Currency = order.Currency,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            Provider = PaymentProvider.Fawry,
            Status = PaymentIntentStatus.Paid,
            PaymentReference = "PAY-T09-DISGUISED",
            IdempotencyKey = "t09-disguised",
            CreatedAt = DateTime.UtcNow
        });
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }

        var result = await service.RecordGatewaySettlementAsync(new GatewaySettlementRequest
        {
            TenantId = tenantId,
            SettlementId = "fawry-disguised-001",
            Gateway = "Stripe",
            GrossAmount = 100m,
            Currency = "EGP",
            OrderIds = new List<Guid> { order.Id }
        });

        Assert.False(result.Success);
        Assert.Equal(AuthorityCode, result.FailureCode);
        Assert.Empty(await context.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await context.PaymentSettlementOrders.ToListAsync());
    }

    // ── Refund ───────────────────────────────────────────────────────

    [Fact]
    public async Task Refund_RejectsFawryOrderBeforeMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var accountingOutbox = new Mock<IAccountingOutboxService>(MockBehavior.Strict);
        var commerceAccounting = new Mock<ICommerceAccountingService>(MockBehavior.Strict);
        var service = CreateLifecycleService(context, commerceAccounting, new Mock<IEventPublisher>(), accountingOutbox.Object);
        var order = await SeedCollectedOrderAsync(context, tenantId, total: 100m, paymentMethodCode: "FAWRY", paymentGateway: "Fawry");

        var result = await service.RecordRefundAsync(order.Id, 25m, "refund-fawry-001", "Fawry refund");

        Assert.False(result.Success);
        Assert.Equal(AuthorityCode, result.FailureCode);
        Assert.Empty(await context.OrderRefunds.Where(item => item.OrderId == order.Id).ToListAsync());
        Assert.Equal(
            PaymentStatus.Collected,
            (await context.Orders.SingleAsync(item => item.Id == order.Id)).PaymentStatus);
    }

    [Fact]
    public async Task Refund_RejectsFawryIntentLineageWhenOrderFieldsAreDisguised()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var service = CreateLifecycleService(
            context,
            new Mock<ICommerceAccountingService>(MockBehavior.Strict),
            new Mock<IEventPublisher>(),
            new Mock<IAccountingOutboxService>(MockBehavior.Strict).Object);
        var order = await SeedCollectedOrderAsync(context, tenantId, 100m, "STRIPE", "Stripe");
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Amount = order.Total,
            Currency = order.Currency,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            Provider = PaymentProvider.Fawry,
            Status = PaymentIntentStatus.Paid,
            PaymentReference = "PAY-T09-REFUND-DISGUISED",
            IdempotencyKey = "t09-refund-disguised",
            CreatedAt = DateTime.UtcNow
        });
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }

        var result = await service.RecordRefundAsync(order.Id, 25m, "refund-t09-disguised", "test");

        Assert.False(result.Success);
        Assert.Equal(AuthorityCode, result.FailureCode);
        Assert.Empty(await context.OrderRefunds.Where(item => item.OrderId == order.Id).ToListAsync());
    }

    // ── Webhook lifecycle admission ──────────────────────────────────

    [Theory]
    [InlineData("Fawry")]
    [InlineData("fawry")]
    public async Task WebhookSuccess_RejectsFawryGatewayBeforeMutation(string gateway)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var commerceAccounting = new Mock<ICommerceAccountingService>(MockBehavior.Strict);
        var service = CreateLifecycleService(context, commerceAccounting, new Mock<IEventPublisher>());
        var order = await SeedPendingOrderWithIntentAsync(context, tenantId);

        var result = await service.HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            TenantId = tenantId,
            OrderId = order.Id,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.GatewayWebhook,
            Gateway = gateway,
            PaymentReference = "PAY-T09",
            ProviderAmount = order.Total,
            ProviderCurrency = order.Currency
        });

        Assert.False(result.Success);
        Assert.Equal(AuthorityCode, result.FailureCode);
        Assert.Equal(
            PaymentStatus.Pending,
            (await context.Orders.SingleAsync(item => item.Id == order.Id)).PaymentStatus);
        Assert.Equal(
            PaymentIntentStatus.Pending,
            (await context.PaymentIntents.SingleAsync(item => item.OrderId == order.Id)).Status);
        Assert.Empty(await context.AccountingOutboxMessages.ToListAsync());
        Assert.Empty(await context.OrderRefunds.ToListAsync());
    }

    [Fact]
    public async Task WebhookAuthorized_RejectsFawryOrderBeforeMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var commerceAccounting = new Mock<ICommerceAccountingService>(MockBehavior.Strict);
        var service = CreateLifecycleService(context, commerceAccounting, new Mock<IEventPublisher>());
        var order = await SeedPendingOrderWithIntentAsync(context, tenantId);

        var result = await service.HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            TenantId = tenantId,
            OrderId = order.Id,
            Outcome = PaymentLifecycleOutcome.Authorized,
            Source = PaymentLifecycleSource.GatewayWebhook,
            Gateway = "Fawry",
            PaymentReference = "PAY-T09",
            ProviderAmount = order.Total,
            ProviderCurrency = order.Currency
        });

        Assert.False(result.Success);
        Assert.Equal(AuthorityCode, result.FailureCode);
        Assert.Equal(
            PaymentStatus.Pending,
            (await context.Orders.SingleAsync(item => item.Id == order.Id)).PaymentStatus);
    }

    [Fact]
    public async Task WebhookSuccess_RejectsUnknownGatewayBeforeMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var service = CreateLifecycleService(
            context,
            new Mock<ICommerceAccountingService>(MockBehavior.Strict),
            new Mock<IEventPublisher>());
        var order = await SeedPendingOrderWithIntentAsync(
            context,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentGateway: "Stripe",
            provider: PaymentProvider.Stripe);

        var result = await service.HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            TenantId = tenantId,
            OrderId = order.Id,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.GatewayWebhook,
            Gateway = "MysteryPay",
            ProviderAmount = order.Total,
            ProviderCurrency = order.Currency
        });

        Assert.False(result.Success);
        Assert.Equal("PAYMENT_GATEWAY_UNSUPPORTED", result.FailureCode);
        Assert.Equal(PaymentStatus.Pending, (await context.Orders.SingleAsync()).PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Pending, (await context.PaymentIntents.SingleAsync()).Status);
        Assert.Empty(await context.AccountingOutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task WebhookFailure_RejectsUnknownGatewayBeforeMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var service = CreateLifecycleService(
            context,
            new Mock<ICommerceAccountingService>(MockBehavior.Strict),
            new Mock<IEventPublisher>());
        var order = await SeedPendingOrderWithIntentAsync(
            context,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentGateway: "Stripe",
            provider: PaymentProvider.Stripe);

        var result = await service.HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            TenantId = tenantId,
            OrderId = order.Id,
            Outcome = PaymentLifecycleOutcome.Failed,
            Source = PaymentLifecycleSource.GatewayWebhook,
            Gateway = "MysteryPay"
        });

        Assert.False(result.Success);
        Assert.Equal("PAYMENT_GATEWAY_UNSUPPORTED", result.FailureCode);
        Assert.Equal(PaymentStatus.Pending, (await context.Orders.SingleAsync()).PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Pending, (await context.PaymentIntents.SingleAsync()).Status);
    }

    [Fact]
    public async Task WebhookSuccess_RejectsFawryIntentWhenOrderAndRequestAreDisguised()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var service = CreateLifecycleService(
            context,
            new Mock<ICommerceAccountingService>(MockBehavior.Strict),
            new Mock<IEventPublisher>());
        var order = await SeedPendingOrderWithIntentAsync(
            context,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentGateway: "Stripe",
            provider: PaymentProvider.Fawry);

        var result = await service.HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            TenantId = tenantId,
            OrderId = order.Id,
            PaymentIntentId = await context.PaymentIntents.Select(intent => intent.Id).SingleAsync(),
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.GatewayWebhook,
            Gateway = "Stripe",
            ProviderAmount = order.Total,
            ProviderCurrency = order.Currency
        });

        Assert.False(result.Success);
        Assert.Equal(AuthorityCode, result.FailureCode);
        Assert.Equal(PaymentStatus.Pending, (await context.Orders.SingleAsync()).PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Pending, (await context.PaymentIntents.SingleAsync()).Status);
        Assert.Empty(await context.AccountingOutboxMessages.ToListAsync());
    }

    // ── Checkout recovery ──────────────────────────────────────────

    [Fact]
    public async Task CheckoutRecovery_NeverInitiatesFawryAndExpiresWithoutFinancialFacts()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..16],
            PaymentMethodCode = "FAWRY",
            PaymentGateway = "Fawry",
            PaymentStatus = PaymentStatus.Pending,
            Status = OrderStatus.Pending,
            Total = 100m,
            Currency = "EGP"
        };
        order.TransitionCheckoutState(CheckoutState.PendingPaymentIntent);
        context.Orders.Add(order);
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }

        // Backdate past the reservation-expiry window. (SaveChanges stamps
        // CreatedAt on insert, so the backdate needs its own round-trip.)
        order.CreatedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30));
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();

        var paymentService = new Mock<IPaymentService>(MockBehavior.Strict);
        var paymentGateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var recovery = new CheckoutPaymentIntentRecoveryService(
            context,
            paymentService.Object,
            paymentGateway.Object,
            Mock.Of<ICheckoutSessionTokenService>(),
            Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<ICouponValidationService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IManualPaymentDurableEvidenceAuthority>(),
            NullLogger<CheckoutPaymentIntentRecoveryService>.Instance);

        var repaired = await recovery.RepairPendingPaymentIntentOrdersAsync();

        Assert.Equal(1, repaired);
        var reloaded = await context.Orders.IgnoreQueryFilters().SingleAsync(item => item.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);
        Assert.Equal(CheckoutState.Expired, reloaded.CheckoutState);
        Assert.Empty(await context.PaymentIntents.IgnoreQueryFilters().ToListAsync());
        paymentGateway.Verify(
            gateway => gateway.CreateSessionAsync(
                It.IsAny<CanonicalPaymentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        paymentService.Verify(
            service => service.CreatePaymentIntentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreatePaymentIntentRequestDto>(),
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        paymentService.Verify(
            service => service.ResumeExistingPaymentIntentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<PaymentProvider>(),
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckoutRecovery_ExpiresFawryOrderWithActiveIntentWithoutProviderExecution()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateLifecycleContext(tenantId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..16],
            PaymentMethodCode = "FAWRY",
            PaymentGateway = "Fawry",
            PaymentStatus = PaymentStatus.Pending,
            Status = OrderStatus.Pending,
            Total = 100m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30))
        };
        order.TransitionCheckoutState(CheckoutState.PaymentPending);
        context.Orders.Add(order);
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Provider = PaymentProvider.Fawry,
            ProviderIntentId = "FWY-T09-ACTIVE",
            ReferenceCode = "123456789",
            PaymentReference = "PAY-T09-ACTIVE",
            IdempotencyKey = "t09-active-fawry",
            Amount = order.Total,
            Currency = order.Currency,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            Status = PaymentIntentStatus.Pending,
            CreatedAt = order.CreatedAt
        });
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }
        order.CreatedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30));
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();

        var paymentService = new Mock<IPaymentService>(MockBehavior.Strict);
        var paymentGateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var recovery = new CheckoutPaymentIntentRecoveryService(
            context,
            paymentService.Object,
            paymentGateway.Object,
            Mock.Of<ICheckoutSessionTokenService>(),
            Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<ICouponValidationService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IManualPaymentDurableEvidenceAuthority>(),
            NullLogger<CheckoutPaymentIntentRecoveryService>.Instance);

        var repaired = await recovery.RepairPendingPaymentIntentOrdersAsync();

        Assert.Equal(1, repaired);
        var reloaded = await context.Orders.IgnoreQueryFilters().SingleAsync(item => item.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);
        Assert.Equal(CheckoutState.Expired, reloaded.CheckoutState);
        Assert.Single(await context.PaymentIntents.IgnoreQueryFilters().ToListAsync());
        paymentGateway.VerifyNoOtherCalls();
        paymentService.VerifyNoOtherCalls();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static void AssertAuthorityRejection(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        var payload = JsonSerializer.Serialize(objectResult.Value);
        Assert.Contains(AuthorityCode, payload, StringComparison.Ordinal);
    }

    private static PaymentMethodsAdminController CreateAdminController(
        Guid tenantId,
        out ApplicationDbContext dbContext,
        Mock<IEventPublisher>? publisher = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        dbContext = new ApplicationDbContext(options, tenantContext);

        return new PaymentMethodsAdminController(
            dbContext,
            (publisher ?? new Mock<IEventPublisher>()).Object,
            new EphemeralDataProtectionProvider(),
            new CommerceReliabilityTelemetry(),
            NullLogger<PaymentMethodsAdminController>.Instance,
            tenantContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static ApplicationDbContext CreateReadinessContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static StorefrontPaymentRuntimeReadinessService CreateReadinessService(ApplicationDbContext dbContext)
    {
        return new StorefrontPaymentRuntimeReadinessService(
            dbContext,
            new EphemeralDataProtectionProvider(),
            Mock.Of<IStripeTenantConfigurationResolver>(),
            Mock.Of<IPaymobTenantConfigurationResolver>(),
            Mock.Of<IFawryTenantConfigurationResolver>(),
            Mock.Of<IManualPaymentCapabilityAdmissionAuthority>(),
            Options.Create(new StripeOptions()),
            Options.Create(new PaymobOptions()),
            Options.Create(new FawryOptions()),
            NullLogger<StorefrontPaymentRuntimeReadinessService>.Instance);
    }

    private static CommerceAccountingService CreateCommerceAccountingService(
        Mock<IFinancialPostingService> postingService,
        Mock<IAccountResolver> accountResolver)
    {
        return new CommerceAccountingService(
            postingService.Object,
            accountResolver.Object,
            Mock.Of<ICashboxLedgerAccountService>(),
            Mock.Of<ILogger<CommerceAccountingService>>());
    }

    private static Account CreateAccount(Guid tenantId, Guid id, string code)
    {
        var account = Account.Create(tenantId, code, $"Test Account {code}", AccountType.Asset);
        account.Id = id;
        return account;
    }

    private static ApplicationDbContext CreateLifecycleContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"T09FawryAuthority_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static PaymentLifecycleService CreateLifecycleService(
        ApplicationDbContext context,
        Mock<ICommerceAccountingService> commerceAccounting,
        Mock<IEventPublisher> eventPublisher,
        IAccountingOutboxService? accountingOutbox = null)
    {
        var correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor
            .Setup(accessor => accessor.GetCorrelationId())
            .Returns("t09-fawry-authority");

        accountingOutbox ??= new AccountingOutboxService(
            context,
            correlationIdAccessor.Object,
            NullLogger<AccountingOutboxService>.Instance);

        return new PaymentLifecycleService(
            context,
            accountingOutbox,
            commerceAccounting.Object,
            eventPublisher.Object,
            Mock.Of<IMarketplaceEventOutboxService>(),
            correlationIdAccessor.Object,
            Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<IInventoryReservationMetricsService>(),
            Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance,
            Options.Create(new PaymentLifecycleSettings { RequireRowLevelLocking = false }));
    }

    private static async Task<Order> SeedCollectedOrderAsync(
        ApplicationDbContext context,
        Guid tenantId,
        decimal total,
        string paymentMethodCode,
        string? paymentGateway)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..16],
            PaymentMethodCode = paymentMethodCode,
            PaymentGateway = paymentGateway,
            PaymentStatus = PaymentStatus.Collected,
            FulfillmentStatus = FulfillmentStatus.Delivered,
            FulfillmentMode = FulfillmentMode.Manual,
            Total = total,
            Currency = "EGP",
            Status = OrderStatus.Completed,
            IsPaid = true,
            PaidAt = DateTime.UtcNow,
            FinancialPostingStatus = FinancialPostingStatus.Posted,
            FinancialPostingJournalId = Guid.NewGuid(),
            FinancialPostedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        context.Orders.Add(order);
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }
        return order;
    }

    private static async Task<Order> SeedPendingOrderWithIntentAsync(
        ApplicationDbContext context,
        Guid tenantId,
        string paymentMethodCode = "FAWRY",
        string? paymentGateway = "Fawry",
        PaymentProvider provider = PaymentProvider.Fawry)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..16],
            PaymentMethodCode = paymentMethodCode,
            PaymentGateway = paymentGateway,
            PaymentStatus = PaymentStatus.Pending,
            Total = 100m,
            Currency = "EGP",
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        var paymentIntent = new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Amount = order.Total,
            Currency = order.Currency,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            Provider = provider,
            Status = PaymentIntentStatus.Pending,
            PaymentReference = "PAY-T09",
            IdempotencyKey = $"t09-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        context.Orders.Add(order);
        context.PaymentIntents.Add(paymentIntent);
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }
        return order;
    }
}

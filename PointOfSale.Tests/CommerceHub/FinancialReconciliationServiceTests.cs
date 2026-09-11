using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class FinancialReconciliationServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubDailyFinancialAggregateService _aggregateService;
    private readonly CommerceHubFinanceReconciliationReadService _readService;
    private readonly CommerceHubFinancialAuditService _auditService;
    private readonly TestAlertHook _alertHook;
    private readonly FinancialReconciliationService _service;
    private readonly SuperAdminFinancialDashboardService _dashboardService;

    public FinancialReconciliationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        var vendorDebtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));

        _aggregateService = new CommerceHubDailyFinancialAggregateService(
            _dbContext,
            NullLogger<CommerceHubDailyFinancialAggregateService>.Instance);
        _readService = new CommerceHubFinanceReconciliationReadService(_dbContext, vendorDebtService);
        _auditService = new CommerceHubFinancialAuditService(_dbContext);
        _alertHook = new TestAlertHook();
        _service = new FinancialReconciliationService(
            _dbContext,
            _readService,
            _aggregateService,
            _auditService,
            _alertHook,
            NullLogger<FinancialReconciliationService>.Instance);
        _dashboardService = new SuperAdminFinancialDashboardService(
            _dbContext,
            _readService,
            _aggregateService,
            NullLogger<SuperAdminFinancialDashboardService>.Instance);
    }

    [Fact]
    public async Task RunAsync_ShouldProduceHealthyReport_ForBalancedSingleVendorOrder()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(9);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-1001",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Alpha",
            "CH-RECON-1001-A",
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "Balanced order commission");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, createdAtUtc.Date);

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Healthy", execution.Report.Status);
        Assert.Equal(0, execution.Report.MismatchCount);
        Assert.Empty(details.Errors);
        Assert.Empty(details.Warnings);
        Assert.Equal("Healthy", execution.Snapshot.LatestReportStatus);
        Assert.Equal(0, _alertHook.CallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldDetectMissingOrderCommissionLedgerEntry()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(10);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-1002",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Beta",
            "CH-RECON-1002-A",
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        await SaveCommerceHubFixtureAsync();

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Critical", execution.Report.Status);
        Assert.Contains(details.Errors, error => error.IssueType == "ORDER_COMMISSION_LEDGER_MISMATCH");
        Assert.Contains(details.Errors, error => error.IssueType == "NET_COMMISSION_LEDGER_MISMATCH");
        Assert.Equal(1, _alertHook.CallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldDetectDuplicatePromoUsage()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(11);
        var promo = CreatePromoCode("DUPL-RECON", 4m, createdAtUtc);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-1003",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m,
            promoCode: promo.Code,
            promoDiscountTotal: 4m);
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Gamma",
            "CH-RECON-1003-A",
            grossCommission: 10m,
            finalCommission: 6m,
            promoDiscount: 4m);

        _dbContext.PromoCodes.Add(promo);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        _dbContext.PromoCodeUsages.AddRange(
            CreatePromoUsage(promo.Id, order, 2m, createdAtUtc),
            CreatePromoUsage(promo.Id, order, 2m, createdAtUtc.AddMinutes(1)));
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "Promo order commission");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Promotion,
            CommerceHubFinancialTransactionDirection.Debit,
            4m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc.AddMinutes(1),
            "Promo order funding");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, createdAtUtc.Date);

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Critical", execution.Report.Status);
        Assert.Contains(details.Errors, error => error.IssueType == "DUPLICATE_PROMO_USAGE");
        Assert.DoesNotContain(details.Errors, error => error.IssueType == "PROMO_USAGE_AMOUNT_MISMATCH");
        Assert.Equal(1, _alertHook.CallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldDetectRefundProportionalMismatch()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-3).Date.AddHours(9);
        var refundedAtUtc = createdAtUtc.AddDays(1);
        var promo = CreatePromoCode("REFUND-RECON", 2m, createdAtUtc);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-1004",
            totalAmount: 100m,
            subtotal: 100m,
            shippingTotal: 0m,
            promoCode: promo.Code,
            promoDiscountTotal: 2m);
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Delta",
            "CH-RECON-1004-A",
            grossCommission: 10m,
            finalCommission: 8m,
            promoDiscount: 2m,
            total: 100m,
            subtotal: 100m,
            shipping: 0m);
        var item = CreateSubOrderItem(
            Guid.NewGuid(),
            subOrder,
            quantity: 4,
            unitPrice: 25m,
            grossCommission: 10m,
            finalCommission: 8m,
            promoDiscount: 2m);
        var refund = CreateRefund(
            Guid.NewGuid(),
            subOrder,
            item.Id,
            quantity: 1,
            processedAtUtc: refundedAtUtc,
            amount: 25m,
            vendorEarningReversal: 23m,
            commissionReversal: 1.5m,
            promoReversal: 0.5m,
            isFullRefund: false);

        _dbContext.PromoCodes.Add(promo);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        _dbContext.CommerceHubSubOrderItems.Add(item);
        _dbContext.CommerceHubRefunds.Add(refund);
        _dbContext.PromoCodeUsages.Add(CreatePromoUsage(promo.Id, order, 2m, createdAtUtc));
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "Refund test order commission");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Promotion,
            CommerceHubFinancialTransactionDirection.Debit,
            2m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc.AddMinutes(1),
            "Refund test promo funding");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            1.5m,
            CommerceHubFinancialReferenceType.Refund,
            refund.Id,
            refundedAtUtc,
            "Refund test commission reversal");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Promotion,
            CommerceHubFinancialTransactionDirection.Credit,
            0.5m,
            CommerceHubFinancialReferenceType.Refund,
            refund.Id,
            refundedAtUtc.AddMinutes(1),
            "Refund test promo reversal");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, refundedAtUtc.Date);

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Critical", execution.Report.Status);
        Assert.Contains(details.Errors, error => error.IssueType == "REFUND_COMMISSION_PROPORTION_MISMATCH");
        Assert.Contains(details.Errors, error => error.IssueType == "REFUND_VENDOR_EARNING_MISMATCH");
        Assert.Contains(details.Errors, error => error.IssueType == "REFUND_PROPORTIONAL_GROSS_MISMATCH");
    }

    [Fact]
    public async Task GetSnapshotAsync_CodCommissionReversalPair_ShouldNotBeOrphaned()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-1);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-COD-REFUND",
            totalAmount: 400m,
            subtotal: 400m,
            shippingTotal: 0m);
        order.PaymentMethodCode = "COD";
        order.PaymentMethodName = "Cash on Delivery";
        var vendorId = Guid.NewGuid();
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            vendorId,
            "Vendor COD",
            "CH-RECON-COD-REFUND-A",
            grossCommission: 40m,
            finalCommission: 40m,
            promoDiscount: 0m,
            total: 400m,
            subtotal: 400m,
            shipping: 0m);
        subOrder.PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery;
        subOrder.PaymentMethodCode = "COD";
        subOrder.PaymentMethodName = "Cash on Delivery";
        var refund = CreateRefund(
            Guid.NewGuid(),
            subOrder,
            orderItemId: null,
            quantity: null,
            processedAtUtc: createdAtUtc.AddHours(1),
            amount: 400m,
            vendorEarningReversal: 360m,
            commissionReversal: 40m,
            promoReversal: 0m,
            isFullRefund: true);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        _dbContext.CommerceHubRefunds.Add(refund);
        AddVendorTransaction(
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            400m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "COD collection");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            40m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "COD commission");
        AddVendorTransaction(
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            40m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "COD commission debt");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            40m,
            CommerceHubFinancialReferenceType.Refund,
            refund.Id,
            createdAtUtc.AddHours(1),
            "COD commission reversal");
        AddVendorTransaction(
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            40m,
            CommerceHubFinancialReferenceType.Refund,
            refund.Id,
            createdAtUtc.AddHours(1),
            "COD debt reduction");
        await SaveCommerceHubFixtureAsync();

        var snapshot = await _readService.GetSnapshotAsync();

        Assert.Equal("OK", snapshot.Status);
        Assert.Equal(0, snapshot.RefundFlowMismatchCount);
        Assert.Equal(0, snapshot.OrphanTransactionsCount);
        Assert.Equal(0, snapshot.MissingPairCount);
    }

    [Fact]
    public async Task RunAsync_ShouldDetectPaidExternalPaymentWithoutProviderTransactionReference()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(10);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-EXTPAY-1001",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor External Pay",
            "CH-RECON-EXTPAY-1001-A",
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m);
        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = 110m,
            Currency = "EGP",
            Provider = PaymentProvider.Stripe,
            ProviderReference = "ch-extpay-ref-1001",
            ProviderIntentId = "pi_extpay_1001",
            ProviderTransactionId = null,
            Status = CommerceHubPaymentRecordStatus.Paid,
            IsProcessed = true,
            PaidAtUtc = createdAtUtc.AddMinutes(5),
            ProcessedAtUtc = createdAtUtc.AddMinutes(5),
            CreatedAt = createdAtUtc
        };

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        _dbContext.CommerceHubPayments.Add(payment);
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "External payment order commission");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, createdAtUtc.Date);

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Critical", execution.Report.Status);
        Assert.Contains(details.Errors, error => error.IssueType == "EXTERNAL_PAYMENT_REFERENCE_MISSING");
        Assert.True(details.CompensationActionCount > 0);
        Assert.Equal(1, _alertHook.CallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldAcceptRefundedOrderAsPreviouslyCollectedExternalPayment()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(10);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-REFUNDED-1001",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        order.Status = OrderStatus.Refunded;
        order.PaymentStatus = PaymentStatus.Refunded;
        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = 110m,
            Currency = "EGP",
            Provider = PaymentProvider.Paymob,
            ProviderReference = "ch-refunded-ref-1001",
            ProviderIntentId = "provider-order-refunded-1001",
            ProviderTransactionId = "provider-transaction-refunded-1001",
            Status = CommerceHubPaymentRecordStatus.Paid,
            IsProcessed = true,
            PaidAtUtc = createdAtUtc.AddMinutes(5),
            ProcessedAtUtc = createdAtUtc.AddMinutes(5),
            CreatedAt = createdAtUtc
        };

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubPayments.Add(payment);
        await SaveCommerceHubFixtureAsync();

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.DoesNotContain(details.Errors, error => error.IssueType == "EXTERNAL_PAYMENT_ORDER_STATE_MISMATCH");
    }

    [Fact]
    public async Task RunAsync_ShouldAcceptProcessedProviderDeclineWithMatchingAttemptAndImmutableEvent()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-1).Date.AddHours(11);
        var processedAtUtc = createdAtUtc.AddMinutes(5);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-DECLINE-1001",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        order.Status = OrderStatus.OnHold;
        order.PaymentStatus = PaymentStatus.Pending;

        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = 110m,
            Currency = "EGP",
            Provider = PaymentProvider.Paymob,
            ProviderReference = "ch-decline-ref-1001",
            ProviderIntentId = "provider-order-1001",
            ProviderTransactionId = "provider-transaction-1001",
            Status = CommerceHubPaymentRecordStatus.Failed,
            FailureReason = "Provider declined the transaction.",
            IsProcessed = true,
            ProcessedAtUtc = processedAtUtc,
            CreatedAt = createdAtUtc
        };
        payment.Attempts.Add(new CommerceHubPaymentAttempt
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            Provider = payment.Provider,
            AttemptNumber = 1,
            ProviderReference = payment.ProviderReference,
            ProviderIntentId = payment.ProviderIntentId,
            ProviderTransactionId = payment.ProviderTransactionId,
            Status = CommerceHubPaymentRecordStatus.Failed,
            FailureReason = payment.FailureReason,
            CompletedAtUtc = processedAtUtc,
            CreatedAt = createdAtUtc
        });
        payment.Logs.Add(new CommerceHubPaymentLog
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            EventType = "CALLBACK",
            ProviderEventId = payment.ProviderTransactionId,
            RawResponse = "paymob-webhook:v1:protected-evidence",
            CreatedAt = processedAtUtc
        });

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubPayments.Add(payment);
        await SaveCommerceHubFixtureAsync();

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Healthy", execution.Report.Status);
        Assert.DoesNotContain(details.Errors, error => error.IssueType == "FAILED_EXTERNAL_PAYMENT_HAS_TRANSACTION");
        Assert.Equal(0, _alertHook.CallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldQuarantineFailedPaymentTransactionWithoutMatchingProviderEvidence()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-1).Date.AddHours(12);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-UNPROVEN-FAILURE-1001",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        order.Status = OrderStatus.OnHold;
        order.PaymentStatus = PaymentStatus.Pending;
        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = 110m,
            Currency = "EGP",
            Provider = PaymentProvider.Paymob,
            ProviderReference = "ch-unproven-failure-1001",
            ProviderTransactionId = "provider-transaction-unproven",
            Status = CommerceHubPaymentRecordStatus.Failed,
            FailureReason = "Locally failed without retained provider event.",
            IsProcessed = true,
            ProcessedAtUtc = createdAtUtc.AddMinutes(5),
            CreatedAt = createdAtUtc
        };

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubPayments.Add(payment);
        await SaveCommerceHubFixtureAsync();

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Critical", execution.Report.Status);
        Assert.Contains(details.Errors, error => error.IssueType == "FAILED_EXTERNAL_PAYMENT_HAS_TRANSACTION");
        Assert.Equal(1, _alertHook.CallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldDetectCompletedPayoutWithoutExternalEvidenceOrLedger()
    {
        var processedAtUtc = DateTime.UtcNow.AddDays(-1).Date.AddHours(12);
        var payout = new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Amount = 500m,
            Currency = "EGP",
            Method = CommerceHubPayoutMethod.BankTransfer,
            Status = CommerceHubPayoutStatus.Completed,
            IdempotencyKey = "payout-ext-money-1001",
            ExternalReferenceId = null,
            BankTransactionId = null,
            ProcessedAtUtc = processedAtUtc,
            CreatedAt = processedAtUtc.AddMinutes(-10)
        };

        _dbContext.CommerceHubPayouts.Add(payout);
        await SaveCommerceHubFixtureAsync();

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Critical", execution.Report.Status);
        Assert.Contains(details.Errors, error => error.IssueType == "EXTERNAL_PAYOUT_REFERENCE_MISSING");
        Assert.Contains(details.Errors, error => error.IssueType == "EXTERNAL_PAYOUT_LEDGER_MISMATCH");
        Assert.True(details.CompensationActionCount >= 2);
        Assert.Equal(1, _alertHook.CallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldInvalidateDriftedAggregateRows()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(12);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-1005",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Epsilon",
            "CH-RECON-1005-A",
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "Aggregate drift order commission");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, createdAtUtc.Date);

        var storedRow = await _dbContext.CommerceHubDailyFinancialAggregates.SingleAsync();
        storedRow.GrossCommission = 9m;
        storedRow.NetRevenue = 9m;
        await SaveCommerceHubFixtureAsync();

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);
        var refreshedRow = await _dbContext.CommerceHubDailyFinancialAggregates.SingleAsync();

        Assert.Equal("Critical", execution.Report.Status);
        Assert.Equal(1, execution.InvalidAggregateCount);
        Assert.False(refreshedRow.IsValid);
        Assert.NotNull(refreshedRow.LastValidatedAtUtc);
        Assert.Contains("Gross commission drift detected", refreshedRow.ValidationError, StringComparison.Ordinal);
        Assert.Contains(details.Errors, error => error.IssueType == "AGGREGATE_DRIFT");
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldFallbackToLiveLedger_WhenAggregateValidationFailed()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(13);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-1006",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Zeta",
            "CH-RECON-1006-A",
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "Fallback order commission");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, createdAtUtc.Date);

        var storedRow = await _dbContext.CommerceHubDailyFinancialAggregates.SingleAsync();
        storedRow.GrossCommission = 8m;
        storedRow.NetRevenue = 8m;
        await SaveCommerceHubFixtureAsync();

        await _service.RunAsync();

        var overview = await _dashboardService.GetOverviewAsync(new SuperAdminFinancialDashboardFilterDto
        {
            FromDateUtc = createdAtUtc.Date,
            ToDateUtc = createdAtUtc.Date.AddHours(23)
        });

        Assert.Equal("LIVE_LEDGER", overview.DataSource);
        Assert.Equal("LIVE_LEDGER", overview.DataFreshness.Source);
        Assert.Equal("Critical", overview.FinancialHealth.Status);
        Assert.Contains(overview.IntegrityPanel.Errors, error => error.IssueType == "AGGREGATE_DRIFT");
        Assert.Equal(10m, overview.TotalGrossCommission);
    }

    [Fact]
    public async Task RunAsync_ShouldRemainHealthy_ForBalancedMultiVendorOrder()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(14);
        var promo = CreatePromoCode("MULTI-RECON", 5m, createdAtUtc);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-1007",
            totalAmount: 330m,
            subtotal: 300m,
            shippingTotal: 30m,
            promoCode: promo.Code,
            promoDiscountTotal: 5m);
        var firstSubOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Eta",
            "CH-RECON-1007-A",
            grossCommission: 12m,
            finalCommission: 9m,
            promoDiscount: 3m,
            total: 110m,
            subtotal: 100m,
            shipping: 10m);
        var secondSubOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Theta",
            "CH-RECON-1007-B",
            grossCommission: 22m,
            finalCommission: 20m,
            promoDiscount: 2m,
            total: 220m,
            subtotal: 200m,
            shipping: 20m);

        _dbContext.PromoCodes.Add(promo);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.AddRange(firstSubOrder, secondSubOrder);
        _dbContext.PromoCodeUsages.Add(CreatePromoUsage(promo.Id, order, 5m, createdAtUtc));
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            12m,
            CommerceHubFinancialReferenceType.Order,
            firstSubOrder.Id,
            createdAtUtc,
            "First vendor commission");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Promotion,
            CommerceHubFinancialTransactionDirection.Debit,
            3m,
            CommerceHubFinancialReferenceType.Order,
            firstSubOrder.Id,
            createdAtUtc.AddMinutes(1),
            "First vendor promo funding");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            22m,
            CommerceHubFinancialReferenceType.Order,
            secondSubOrder.Id,
            createdAtUtc.AddMinutes(2),
            "Second vendor commission");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Promotion,
            CommerceHubFinancialTransactionDirection.Debit,
            2m,
            CommerceHubFinancialReferenceType.Order,
            secondSubOrder.Id,
            createdAtUtc.AddMinutes(3),
            "Second vendor promo funding");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, createdAtUtc.Date);

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Healthy", execution.Report.Status);
        Assert.Equal(0, execution.Report.MismatchCount);
        Assert.Equal(2, details.CheckedSubOrders);
        Assert.Equal(1, details.CheckedPromoUsages);
        Assert.Empty(details.Errors);
        Assert.Empty(details.Warnings);
    }

    [Fact]
    public async Task RunAsync_ShouldNotRequireLedgerForPendingCodSiblingOfPartiallyRefundedOrder()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(14);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-PARTIAL-COD",
            totalAmount: 220m,
            subtotal: 200m,
            shippingTotal: 20m);
        order.PaymentStatus = PaymentStatus.PartiallyRefunded;

        var recognizedSubOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Recognized",
            "CH-RECON-PARTIAL-COD-A",
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m);
        var pendingCodSubOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Pending COD",
            "CH-RECON-PARTIAL-COD-B",
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m);
        pendingCodSubOrder.Status = OrderStatus.Pending;
        pendingCodSubOrder.PaymentStatus = PaymentStatus.CashOnDelivery;
        pendingCodSubOrder.PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery;
        pendingCodSubOrder.PaymentMethodCode = "COD";
        pendingCodSubOrder.PaymentMethodName = "Cash on Delivery";

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.AddRange(recognizedSubOrder, pendingCodSubOrder);
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            recognizedSubOrder.Id,
            createdAtUtc,
            "Recognized sibling commission");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, createdAtUtc.Date);

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Equal("Healthy", execution.Report.Status);
        Assert.Equal(0, execution.Report.MismatchCount);
        Assert.DoesNotContain(details.Errors, error =>
            error.IssueType is "ORDER_COMMISSION_LEDGER_MISMATCH" or "NET_COMMISSION_LEDGER_MISMATCH");
    }

    [Fact]
    public async Task RunAsync_ShouldBeDeterministicAndAppendOnly_OnRepeatExecution()
    {
        var createdAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(15);
        var order = CreateMarketplaceOrder(
            Guid.NewGuid(),
            createdAtUtc,
            "CH-RECON-1008",
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        var subOrder = CreateSubOrder(
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            "Vendor Iota",
            "CH-RECON-1008-A",
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            createdAtUtc,
            "Idempotent order commission");

        await SaveCommerceHubFixtureAsync();
        await StoreMatchingAggregatesAsync(createdAtUtc.Date, createdAtUtc.Date);

        var first = await _service.RunAsync();
        var firstReportId = first.Report.Id;
        var firstTimestamp = first.Report.TimestampUtc;

        await Task.Delay(5);

        var second = await _service.RunAsync();
        var reports = await _dbContext.CommerceHubFinancialReconciliationReports
            .OrderBy(report => report.TimestampUtc)
            .ThenBy(report => report.Id)
            .ToListAsync();

        Assert.Equal("Healthy", first.Report.Status);
        Assert.Equal("Healthy", second.Report.Status);
        Assert.Equal(first.Report.MismatchCount, second.Report.MismatchCount);
        Assert.Equal(2, reports.Count);
        Assert.Equal(firstReportId, reports[0].Id);
        Assert.Equal(firstTimestamp, reports[0].TimestampUtc);
        Assert.NotEqual(first.Report.Id, second.Report.Id);
        Assert.True(reports[1].TimestampUtc >= reports[0].TimestampUtc);
    }

    [Fact]
    public async Task RunAsync_ShouldIncludeDirectVendorFinancialTransactionsInCheckedScope()
    {
        var vendorId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow.AddDays(-1).Date.AddHours(14);

        AddVendorTransaction(
            vendorId,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialTransactionDirection.Credit,
            25m,
            CommerceHubFinancialReferenceType.Adjustment,
            Guid.NewGuid(),
            createdAtUtc,
            "Direct vendor adjustment coverage");

        await SaveCommerceHubFixtureAsync();

        var execution = await _service.RunAsync();
        var details = DeserializeDetails(execution.Report);

        Assert.Contains(vendorId, details.CheckedVendorIds);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task SaveCommerceHubFixtureAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        using (CommerceHubBoundaryMutationScope.BeginPayout())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task StoreMatchingAggregatesAsync(DateTime fromDateUtc, DateTime toDateUtc)
    {
        var rows = await _aggregateService.RecalculateWindowAsync(fromDateUtc, toDateUtc);
        if (rows.Count == 0)
        {
            return;
        }

        _dbContext.CommerceHubDailyFinancialAggregates.AddRange(rows.Select(CloneAggregate));
        await SaveCommerceHubFixtureAsync();
    }

    private void AddPlatformTransaction(
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        DateTime createdAtUtc,
        string description)
    {
        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Platform,
                null,
                transactionType,
                direction,
                amount,
                "EGP",
                referenceType,
                referenceId,
                description,
                createdAtUtc: createdAtUtc));
    }

    private void AddVendorTransaction(
        Guid vendorId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        DateTime createdAtUtc,
        string description)
    {
        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                transactionType,
                direction,
                amount,
                "EGP",
                referenceType,
                referenceId,
                description,
                createdAtUtc: createdAtUtc));
    }

    private static CommerceHubDailyFinancialAggregate CloneAggregate(CommerceHubDailyFinancialAggregate row) =>
        new()
        {
            DateUtc = row.DateUtc,
            CurrencyCode = row.CurrencyCode,
            BaseCurrencyCode = row.BaseCurrencyCode,
            ExchangeRateToBase = row.ExchangeRateToBase,
            GrossSales = row.GrossSales,
            GrossCommission = row.GrossCommission,
            PromoCost = row.PromoCost,
            PromoReversalAmount = row.PromoReversalAmount,
            RefundAmount = row.RefundAmount,
            RefundImpactOnRevenue = row.RefundImpactOnRevenue,
            NetRevenue = row.NetRevenue,
            GrossSalesBase = row.GrossSalesBase,
            GrossCommissionBase = row.GrossCommissionBase,
            PromoCostBase = row.PromoCostBase,
            PromoReversalAmountBase = row.PromoReversalAmountBase,
            RefundAmountBase = row.RefundAmountBase,
            RefundImpactOnRevenueBase = row.RefundImpactOnRevenueBase,
            NetRevenueBase = row.NetRevenueBase,
            OrdersCount = row.OrdersCount,
            RefundCount = row.RefundCount,
            RefundedOrdersCount = row.RefundedOrdersCount,
            IsValid = row.IsValid,
            LastValidatedAtUtc = row.LastValidatedAtUtc,
            ValidationError = row.ValidationError,
            AggregatedThroughUtc = row.AggregatedThroughUtc,
            CalculatedAtUtc = row.CalculatedAtUtc
        };

    private static ReportDetailsPayload DeserializeDetails(CommerceHubFinancialReconciliationReport report) =>
        JsonSerializer.Deserialize<ReportDetailsPayload>(report.DetailsJson, ReportJsonOptions) ??
        new ReportDetailsPayload();

    private static PromoCode CreatePromoCode(string code, decimal discountValue, DateTime createdAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            DiscountType = PromoDiscountType.Fixed,
            DiscountValue = discountValue,
            IsActive = true,
            CreatedAt = createdAtUtc
        };

    private static PromoCodeUsage CreatePromoUsage(
        Guid promoCodeId,
        CommerceHubMarketplaceOrder order,
        decimal discountAmount,
        DateTime appliedAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            PromoCodeId = promoCodeId,
            MarketplaceOrderId = order.Id,
            CustomerId = order.CustomerId,
            PromoCodeSnapshot = order.PromoCode ?? string.Empty,
            DiscountAmount = discountAmount,
            EligibleBaseCommissionAmount = discountAmount,
            AppliedAtUtc = appliedAtUtc,
            CreatedAt = appliedAtUtc
        };

    private static CommerceHubMarketplaceOrder CreateMarketplaceOrder(
        Guid customerId,
        DateTime createdAtUtc,
        string orderNumber,
        decimal totalAmount,
        decimal subtotal,
        decimal shippingTotal,
        string? promoCode = null,
        decimal promoDiscountTotal = 0m) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            OrderNumber = orderNumber,
            FullName = "Reconciliation Test Customer",
            PhoneNumber = "01000000000",
            AddressLine = "1 Reconciliation Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "CARD",
            PaymentMethodName = "Card",
            Subtotal = subtotal,
            ShippingTotal = shippingTotal,
            TotalAmount = totalAmount,
            PromoCode = promoCode,
            PromoDiscountTotal = promoDiscountTotal,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            CreatedAt = createdAtUtc
        };

    private static CommerceHubSubOrder CreateSubOrder(
        Guid id,
        CommerceHubMarketplaceOrder order,
        Guid vendorId,
        string vendorName,
        string orderNumber,
        decimal grossCommission,
        decimal finalCommission,
        decimal promoDiscount,
        decimal total = 110m,
        decimal subtotal = 100m,
        decimal shipping = 10m) =>
        new()
        {
            Id = id,
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = orderNumber,
            StoreId = vendorId,
            StoreName = vendorName,
            Currency = "EGP",
            Subtotal = subtotal,
            Shipping = shipping,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "CARD",
            PaymentMethodName = "Card",
            PlatformCommissionAmount = grossCommission,
            FinalPlatformCommissionAmount = finalCommission,
            PromoDiscountAmount = promoDiscount,
            VendorEarningAmount = Math.Round(total - grossCommission, 2, MidpointRounding.AwayFromZero),
            PlatformCommissionRate = total == 0m
                ? 0m
                : Math.Round(grossCommission / total, 6, MidpointRounding.AwayFromZero),
            Total = total,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            CreatedAt = order.CreatedAt
        };

    private static CommerceHubSubOrderItem CreateSubOrderItem(
        Guid id,
        CommerceHubSubOrder subOrder,
        int quantity,
        decimal unitPrice,
        decimal grossCommission,
        decimal finalCommission,
        decimal promoDiscount) =>
        new()
        {
            Id = id,
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            ProductName = "Reconciliation Item",
            VariantName = "Default",
            Quantity = quantity,
            UnitPrice = unitPrice,
            PlatformCommissionRate = quantity == 0 || unitPrice == 0m
                ? 0m
                : Math.Round(grossCommission / (quantity * unitPrice), 6, MidpointRounding.AwayFromZero),
            PlatformCommissionAmount = grossCommission,
            FinalPlatformCommissionAmount = finalCommission,
            PromoDiscountAmount = promoDiscount,
            Total = quantity * unitPrice,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = subOrder.CreatedAt.AddDays(1),
            CreatedAt = subOrder.CreatedAt
        };

    private static CommerceHubRefund CreateRefund(
        Guid id,
        CommerceHubSubOrder subOrder,
        Guid? orderItemId,
        int? quantity,
        DateTime processedAtUtc,
        decimal amount,
        decimal vendorEarningReversal,
        decimal commissionReversal,
        decimal promoReversal,
        bool isFullRefund) =>
        new()
        {
            Id = id,
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            OrderItemId = orderItemId,
            Quantity = quantity,
            Amount = amount,
            VendorEarningReversalAmount = vendorEarningReversal,
            CommissionReversalAmount = commissionReversal,
            PromoDiscountReversalAmount = promoReversal,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Completed,
            Reason = isFullRefund ? "Full refund" : "Partial refund",
            IdempotencyKey = $"refund:{id:N}",
            IsFullRefund = isFullRefund,
            IsPostPayoutAdjustment = false,
            ProcessedAtUtc = processedAtUtc,
            CreatedAt = processedAtUtc
        };

    private sealed class TestAlertHook : IFinancialReconciliationAlertHook
    {
        public int CallCount { get; private set; }

        public Task TriggerAsync(
            CommerceHubFinancialReconciliationReport report,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ReportDetailsPayload
    {
        public int CheckedSubOrders { get; init; }
        public int CheckedPromoUsages { get; init; }
        public List<Guid> CheckedVendorIds { get; init; } = [];
        public int CompensationActionCount { get; init; }
        public List<IssueGroupPayload> Errors { get; init; } = [];
        public List<IssueGroupPayload> Warnings { get; init; } = [];
    }

    private sealed class IssueGroupPayload
    {
        public string IssueType { get; init; } = string.Empty;
        public int Count { get; init; }
        public string Summary { get; init; } = string.Empty;
    }
}

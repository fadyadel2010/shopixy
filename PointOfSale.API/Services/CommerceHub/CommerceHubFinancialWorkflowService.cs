using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubFinancialWorkflowService
{
    Task ApplyOnlinePaymentSuccessAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task ApplyDeliveredFinancialsAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}

public class CommerceHubFinancialWorkflowService : ICommerceHubFinancialWorkflowService
{
    private const int CurrencyScale = 2;
    private const string FinancialSnapshotMismatchCode = "COMMERCEHUB_FINANCIAL_SNAPSHOT_MISMATCH";
    private const string FinancialArithmeticMismatchCode = "COMMERCEHUB_FINANCIAL_ARITHMETIC_MISMATCH";
    private const string EarningAmountLockedCode = "COMMERCEHUB_EARNING_AMOUNT_LOCKED";
    private const string EarningPaymentMismatchCode = "COMMERCEHUB_EARNING_PAYMENT_MISMATCH";
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubPayoutSettings _payoutSettings;
    private readonly ICommerceHubFinancialLedgerService _financialLedgerService;
    private readonly ICommerceHubFinancialAuditService _auditService;
    private readonly ILogger<CommerceHubFinancialWorkflowService> _logger;

    public CommerceHubFinancialWorkflowService(
        ApplicationDbContext dbContext,
        IOptions<CommerceHubPaymentsOptions> options,
        IOptions<CommerceHubPayoutSettings> payoutSettings,
        ICommerceHubFinancialLedgerService financialLedgerService,
        ICommerceHubFinancialAuditService auditService,
        ILogger<CommerceHubFinancialWorkflowService> logger)
    {
        _dbContext = dbContext;
        _payoutSettings = payoutSettings.Value;
        _financialLedgerService = financialLedgerService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task ApplyOnlinePaymentSuccessAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        ArgumentNullException.ThrowIfNull(marketplaceOrder);
        ArgumentNullException.ThrowIfNull(payment);

        var subOrderIds = marketplaceOrder.SubOrders.Select(subOrder => subOrder.Id).ToList();
        var earningsBySubOrderId = await _dbContext.CommerceHubVendorEarnings
            .Where(earning =>
                subOrderIds.Contains(earning.SubOrderId) &&
                earning.OriginalEarningId == null &&
                !earning.IsDeleted)
            .ToDictionaryAsync(earning => earning.SubOrderId, cancellationToken);

        foreach (var subOrder in marketplaceOrder.SubOrders)
        {
            if (subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery)
            {
                continue;
            }

            ApplyFinancialSnapshot(subOrder, nowUtc);
            subOrder.Status = CommerceHubOrderStateHelper.ConfirmAfterPayment(subOrder.Status);
            EnsureEarningRecord(
                earningsBySubOrderId,
                subOrder,
                paymentId: payment.Id,
                nowUtc);
        }

        marketplaceOrder.Status = CommerceHubOrderStateHelper.AggregateMarketplaceStatus(
            marketplaceOrder.SubOrders,
            CommerceHubOrderStateHelper.ConfirmAfterPayment(marketplaceOrder.Status));

        _logger.LogInformation(
            "CommerceHub online payment financials applied | MarketplaceOrderId: {MarketplaceOrderId} | PaymentId: {PaymentId} | SubOrderCount: {SubOrderCount} | AppliedAtUtc: {AppliedAtUtc}",
            marketplaceOrder.Id,
            payment.Id,
            marketplaceOrder.SubOrders.Count,
            nowUtc);

        foreach (var subOrder in marketplaceOrder.SubOrders.Where(s => s.PaymentMethod != CommerceHubPaymentMethod.CashOnDelivery))
        {
            await _financialLedgerService.RecordOrderAsync(
                marketplaceOrder,
                subOrder,
                cancellationToken);

            _auditService.RecordPaymentCollected(
                subOrder.StoreId,
                marketplaceOrder.Id,
                subOrder.Id,
                payment.Id,
                subOrder.Total,
                subOrder.Currency,
                null);

            _auditService.RecordCommissionDeducted(
                subOrder.StoreId,
                marketplaceOrder.Id,
                subOrder.Id,
                subOrder.PlatformCommissionAmount,
                subOrder.Currency,
                null);
        }
    }

    public async Task ApplyDeliveredFinancialsAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        ArgumentNullException.ThrowIfNull(marketplaceOrder);
        ArgumentNullException.ThrowIfNull(subOrder);

        ApplyFinancialSnapshot(subOrder, nowUtc);
        var paymentId = subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery
            ? null
            : await ResolvePaidPaymentIdAsync(marketplaceOrder.Id, cancellationToken);

        var earning = await _dbContext.CommerceHubVendorEarnings
            .FirstOrDefaultAsync(
                item => item.SubOrderId == subOrder.Id &&
                        item.OriginalEarningId == null &&
                        !item.IsDeleted,
                cancellationToken);

        var earningsBySubOrderId = earning == null
            ? new Dictionary<Guid, CommerceHubVendorEarning>()
            : new Dictionary<Guid, CommerceHubVendorEarning> { [subOrder.Id] = earning };
        var pendingAdjustments = await _dbContext.CommerceHubVendorEarnings
            .Where(item =>
                item.SubOrderId == subOrder.Id &&
                item.OriginalEarningId != null &&
                item.Status == CommerceHubVendorEarningStatus.Pending &&
                !item.IsDeleted)
            .ToListAsync(cancellationToken);

        if (subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery)
        {
            CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected(
                marketplaceOrder,
                subOrder,
                nowUtc);
        }

        earning = EnsureEarningRecord(
            earningsBySubOrderId,
            subOrder,
            paymentId,
            nowUtc);

        if (earning == null)
        {
            return;
        }

        if (earning.Status == CommerceHubVendorEarningStatus.Pending)
        {
            if (subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery)
            {
                _auditService.RecordCommissionDeducted(
                    subOrder.StoreId,
                    marketplaceOrder.Id,
                    subOrder.Id,
                    subOrder.PlatformCommissionAmount,
                    subOrder.Currency,
                    null);
            }

            earning.Status = CommerceHubVendorEarningStatus.Available;
            earning.AvailableAtUtc ??= GetAvailableAtUtc(nowUtc);

            _auditService.RecordVendorEarningAvailable(
                subOrder.StoreId,
                marketplaceOrder.Id,
                subOrder.Id,
                earning.Id,
                earning.Amount,
                subOrder.Currency,
                null);
        }

        if (subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery)
        {
            _auditService.RecordCodCollected(
                subOrder.StoreId,
                marketplaceOrder.Id,
                subOrder.Id,
                subOrder.Total,
                subOrder.Currency,
                null);
        }

        foreach (var adjustment in pendingAdjustments)
        {
            adjustment.Status = CommerceHubVendorEarningStatus.Available;
            adjustment.AvailableAtUtc ??= GetAvailableAtUtc(nowUtc);

            _auditService.RecordVendorEarningAvailable(
                subOrder.StoreId,
                marketplaceOrder.Id,
                subOrder.Id,
                adjustment.Id,
                adjustment.Amount,
                subOrder.Currency,
                null);
        }

        if (subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery)
        {
            await _financialLedgerService.RecordOrderAsync(
                marketplaceOrder,
                subOrder,
                cancellationToken);
        }
    }

    private void ApplyFinancialSnapshot(CommerceHubSubOrder subOrder, DateTime nowUtc)
    {
        // Only skip if snapshot exists AND contains the required promotional/commission fields
        if (HasCompleteFinancialSnapshot(subOrder))
        {
            ValidateCommissionSnapshotJsonMatchesFields(subOrder);
            EnsureFinancialSnapshotIntegrity(subOrder, FinancialSnapshotMismatchCode);
            return;
        }

        decimal rate;
        decimal commission;
        decimal promoDiscount;
        decimal finalCommission;
        decimal vendorEarning;
        var snapshotMetadata = new CommissionSnapshotMetadata(
            CommissionCalculator.LegacySource,
            null,
            null,
            null,
            null);

        if (HasExistingFinancialSnapshotValues(subOrder))
        {
            rate = NormalizeCommissionRate(subOrder.PlatformCommissionRate);
            commission = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.PlatformCommissionAmount));
            promoDiscount = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.PromoDiscountAmount));
            finalCommission = ResolveFinalCommissionAmount(subOrder.FinalPlatformCommissionAmount, commission, promoDiscount);
            vendorEarning = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.VendorEarningAmount));
            snapshotMetadata = new CommissionSnapshotMetadata(
                CommissionCalculator.HistoricalFieldsSource,
                null,
                null,
                null,
                null);
        }
        else if (HasItemLevelFinancialSnapshotValues(subOrder))
        {
            // Commission applies only to product subtotal. Shipping remains pass-through.
            commission = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.Items.Sum(item => item.PlatformCommissionAmount)));
            promoDiscount = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.Items.Sum(item => item.PromoDiscountAmount)));
            finalCommission = CommissionCalculator.RoundAmount(Math.Max(
                0m,
                subOrder.Items.Sum(item => ResolveFinalCommissionAmount(
                    item.FinalPlatformCommissionAmount,
                    item.PlatformCommissionAmount,
                    item.PromoDiscountAmount))));
            rate = subOrder.Subtotal <= 0m
                ? 0m
                : NormalizeCommissionRate(commission / subOrder.Subtotal);
            vendorEarning = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.Total - commission));
            snapshotMetadata = new CommissionSnapshotMetadata(
                CommissionCalculator.ItemSnapshotFallbackSource,
                null,
                null,
                null,
                null);
        }
        else
        {
            throw new InvalidOperationException(
                $"CommerceHub sub-order {subOrder.Id} cannot enter financial workflow because its commission snapshot is incomplete. " +
                "Historical vendor economics must be recovered from stored sub-order or item commission fields before earnings are created.");
        }

        subOrder.PlatformCommissionRate = rate;
        subOrder.PlatformCommissionAmount = commission;
        subOrder.PromoDiscountAmount = promoDiscount;
        subOrder.FinalPlatformCommissionAmount = finalCommission;
        subOrder.VendorEarningAmount = vendorEarning;
        EnsureFinancialSnapshotIntegrity(subOrder, FinancialArithmeticMismatchCode);
        subOrder.CommissionSnapshotJson = BuildCommissionSnapshotJson(subOrder, snapshotMetadata, nowUtc);

        _logger.LogInformation(
            "CommerceHub commission snapshot backfilled during financial workflow | SubOrderId: {SubOrderId} | OrderNumber: {OrderNumber} | Rate: {Rate} | Amount: {Amount} | Source: {Source} | RuleId: {RuleId} | Priority: {Priority} | CommissionBase: {CommissionBase} | ShippingExcluded: true",
            subOrder.Id,
            subOrder.OrderNumber,
            subOrder.PlatformCommissionRate,
            subOrder.PlatformCommissionAmount,
            snapshotMetadata.Source,
            snapshotMetadata.RuleId,
            snapshotMetadata.Priority,
            CommissionCalculator.CommissionBase);
    }

    private decimal NormalizeCommissionRate(decimal configuredRate)
    {
        if (configuredRate < 0)
        {
            _logger.LogWarning(
                "CommerceHub commission rate was negative and has been clamped to zero | ConfiguredRate: {ConfiguredRate}",
                configuredRate);

            return 0m;
        }

        if (configuredRate > 1m && configuredRate <= 100m)
        {
            return CommissionCalculator.RoundRate(configuredRate / 100m);
        }

        if (configuredRate > 1m)
        {
            _logger.LogWarning(
                "CommerceHub commission rate exceeded 100%% and has been clamped to one | ConfiguredRate: {ConfiguredRate}",
                configuredRate);

            return 1m;
        }

        return CommissionCalculator.RoundRate(configuredRate);
    }

    private static decimal RoundCurrency(decimal value)
    {
        return CommissionCalculator.RoundAmount(value);
    }

    private DateTime GetAvailableAtUtc(DateTime nowUtc)
    {
        var payoutDelayDays = Math.Clamp(_payoutSettings.PayoutDelayDays, 0, 30);
        return payoutDelayDays == 0
            ? nowUtc
            : nowUtc.AddDays(payoutDelayDays);
    }

    private static bool HasCompleteFinancialSnapshot(CommerceHubSubOrder subOrder)
    {
        // Check if snapshot JSON exists and contains the required fields
        if (string.IsNullOrWhiteSpace(subOrder.CommissionSnapshotJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(subOrder.CommissionSnapshotJson);
            var root = doc.RootElement;

            // Ensure the root element is a JSON object before attempting property access
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // Verify that the snapshot contains the essential promotional/commission fields
            // A complete snapshot must have gross/final commission, promo discount, vendor earning, and source.
            return root.TryGetProperty("rate", out _) &&
                   root.TryGetProperty("amount", out _) &&
                   root.TryGetProperty("grossCommissionAmount", out _) &&
                   root.TryGetProperty("finalCommissionAmount", out _) &&
                   root.TryGetProperty("promoDiscountAmount", out _) &&
                   root.TryGetProperty("vendorEarning", out _) &&
                   root.TryGetProperty("source", out _);
        }
        catch (JsonException)
        {
            // If JSON is malformed, treat as incomplete
            return false;
        }
    }

    private static bool HasExistingFinancialSnapshotValues(CommerceHubSubOrder subOrder)
    {
        // S38C: Check if sub-order level financial fields have been explicitly set.
        // For legacy orders, we consider the snapshot present if any commission/earning field is non-zero.
        // This prevents treating orders with only Subtotal > 0 as having a valid snapshot
        // when commission/earning fields are uninitialized.
        var hasNonZeroFinancialFields = subOrder.PlatformCommissionRate != 0m ||
                                        subOrder.PlatformCommissionAmount != 0m ||
                                        subOrder.FinalPlatformCommissionAmount != 0m ||
                                        subOrder.VendorEarningAmount != 0m;

        return hasNonZeroFinancialFields;
    }

    private static bool HasItemLevelFinancialSnapshotValues(CommerceHubSubOrder subOrder)
    {
        // Check if item-level financial fields have been explicitly set. Price fields
        // are not a financial snapshot; treating them as one can silently finalize a
        // missing commission as zero. Legitimate zero-commission orders must carry a
        // complete sub-order CommissionSnapshotJson and pass HasCompleteFinancialSnapshot.
        // S40: Only check monetary amounts, not rates - a non-zero rate alone doesn't indicate
        // a snapshot exists since downstream logic relies on actual monetary values.
        return subOrder.Items.Any(item =>
            item.PlatformCommissionAmount != 0m ||
            item.FinalPlatformCommissionAmount != 0m ||
            item.PromoDiscountAmount != 0m);
    }

    private static decimal ResolveFinalCommissionAmount(
        decimal storedFinalAmount,
        decimal grossAmount,
        decimal promoDiscountAmount)
    {
        var storedFinal = CommissionCalculator.RoundAmount(Math.Max(0m, storedFinalAmount));
        if (storedFinal > 0m || grossAmount == 0m)
        {
            return storedFinal;
        }

        return CommissionCalculator.RoundAmount(Math.Max(0m, grossAmount - promoDiscountAmount));
    }

    private static string BuildCommissionSnapshotJson(
        CommerceHubSubOrder subOrder,
        CommissionSnapshotMetadata snapshotMetadata,
        DateTime nowUtc)
    {
        return JsonSerializer.Serialize(new
        {
            rate = subOrder.PlatformCommissionRate,
            amount = subOrder.PlatformCommissionAmount,
            grossCommissionAmount = subOrder.PlatformCommissionAmount,
            finalCommissionAmount = subOrder.FinalPlatformCommissionAmount,
            promoDiscountAmount = subOrder.PromoDiscountAmount,
            source = snapshotMetadata.Source,
            ruleId = snapshotMetadata.RuleId,
            priority = snapshotMetadata.Priority,
            scopeType = snapshotMetadata.ScopeType?.ToString(),
            scopeId = snapshotMetadata.ScopeId,
            vendorEarning = subOrder.VendorEarningAmount,
            commissionBase = CommissionCalculator.CommissionBase,
            commissionBaseAmount = subOrder.Subtotal,
            shippingExcluded = true,
            subtotal = subOrder.Subtotal,
            shipping = subOrder.Shipping,
            total = subOrder.Total,
            currency = subOrder.Currency,
            calculatedAtUtc = nowUtc
        });
    }

    private sealed record CommissionSnapshotMetadata(
        string Source,
        Guid? RuleId,
        int? Priority,
        CommissionRuleScopeType? ScopeType,
        Guid? ScopeId);

    private CommerceHubVendorEarning? EnsureEarningRecord(
        IDictionary<Guid, CommerceHubVendorEarning> earningsBySubOrderId,
        CommerceHubSubOrder subOrder,
        Guid? paymentId,
        DateTime nowUtc)
    {
        earningsBySubOrderId.TryGetValue(subOrder.Id, out var earning);
        SyncEarningLock(subOrder, earning);

        if (!CanCreateOrProgressEarning(subOrder, out var blockedReason))
        {
            _logger.LogWarning(
                "CommerceHub earning skipped | SubOrderId: {SubOrderId} | OrderNumber: {OrderNumber} | Reason: {Reason}",
                subOrder.Id,
                subOrder.OrderNumber,
                blockedReason);

            return null;
        }

        if (earning == null)
        {
            if (subOrder.IsEarningCreated)
            {
                _logger.LogWarning(
                    "CommerceHub earning creation skipped because sub-order lock is already set without an earning row | SubOrderId: {SubOrderId} | OrderNumber: {OrderNumber}",
                    subOrder.Id,
                    subOrder.OrderNumber);

                return null;
            }

            if (paymentId == null && subOrder.PaymentMethod != CommerceHubPaymentMethod.CashOnDelivery)
            {
                _logger.LogWarning(
                    "CommerceHub earning creation skipped because no traceable payment was found for an online sub-order | SubOrderId: {SubOrderId} | OrderNumber: {OrderNumber}",
                    subOrder.Id,
                    subOrder.OrderNumber);

                return null;
            }

            earning = new CommerceHubVendorEarning
            {
                Id = Guid.NewGuid(),
                SubOrderId = subOrder.Id,
                PaymentId = paymentId,
                TenantId = subOrder.StoreId,
                Amount = subOrder.VendorEarningAmount,
                Status = CommerceHubVendorEarningStatus.Pending
            };

            _dbContext.CommerceHubVendorEarnings.Add(earning);
            earningsBySubOrderId[subOrder.Id] = earning;
            subOrder.IsEarningCreated = true;
            subOrder.EarningCreatedAtUtc ??= nowUtc;

            _logger.LogInformation(
                "CommerceHub earning created | SubOrderId: {SubOrderId} | OrderNumber: {OrderNumber} | Amount: {Amount} | PaymentId: {PaymentId}",
                subOrder.Id,
                subOrder.OrderNumber,
                earning.Amount,
                paymentId);

            _auditService.RecordVendorEarningCreated(
                subOrder.StoreId,
                subOrder.MarketplaceOrderId,
                subOrder.Id,
                earning.Id,
                earning.Amount,
                subOrder.Currency,
                null);

            return earning;
        }

        subOrder.IsEarningCreated = true;
        subOrder.EarningCreatedAtUtc ??= earning.CreatedAt;
        EnsureExistingEarningMatchesFinancialSnapshot(earning, subOrder, paymentId);

        if (earning.Status == CommerceHubVendorEarningStatus.Pending)
        {
            earning.Amount = subOrder.VendorEarningAmount;
            earning.PaymentId ??= paymentId;
        }

        return earning;
    }

    private static void ValidateCommissionSnapshotJsonMatchesFields(CommerceHubSubOrder subOrder)
    {
        try
        {
            using var doc = JsonDocument.Parse(subOrder.CommissionSnapshotJson!);
            var root = doc.RootElement;

            EnsureSnapshotAmountMatchesField(
                root,
                "amount",
                subOrder.PlatformCommissionAmount,
                subOrder,
                FinancialSnapshotMismatchCode);
            EnsureSnapshotAmountMatchesField(
                root,
                "grossCommissionAmount",
                subOrder.PlatformCommissionAmount,
                subOrder,
                FinancialSnapshotMismatchCode);
            EnsureSnapshotAmountMatchesField(
                root,
                "finalCommissionAmount",
                subOrder.FinalPlatformCommissionAmount,
                subOrder,
                FinancialSnapshotMismatchCode);
            EnsureSnapshotAmountMatchesField(
                root,
                "promoDiscountAmount",
                subOrder.PromoDiscountAmount,
                subOrder,
                FinancialSnapshotMismatchCode);
            EnsureSnapshotAmountMatchesField(
                root,
                "vendorEarning",
                subOrder.VendorEarningAmount,
                subOrder,
                FinancialSnapshotMismatchCode);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{FinancialSnapshotMismatchCode}: CommerceHub sub-order {subOrder.Id} has an unreadable commission snapshot.",
                exception);
        }
    }

    private static void EnsureSnapshotAmountMatchesField(
        JsonElement root,
        string propertyName,
        decimal expectedAmount,
        CommerceHubSubOrder subOrder,
        string code)
    {
        if (!TryGetDecimalProperty(root, propertyName, out var actualAmount))
        {
            throw new InvalidOperationException(
                $"{code}: CommerceHub sub-order {subOrder.Id} commission snapshot is missing numeric '{propertyName}'.");
        }

        EnsureMoneyEquals(
            expectedAmount,
            actualAmount,
            code,
            $"CommerceHub sub-order {subOrder.Id} commission snapshot '{propertyName}' does not match the persisted financial field.");
    }

    private static bool TryGetDecimalProperty(JsonElement root, string propertyName, out decimal value)
    {
        value = 0m;
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDecimal(out value);
    }

    private static void EnsureFinancialSnapshotIntegrity(CommerceHubSubOrder subOrder, string code)
    {
        var total = RoundCurrency(subOrder.Total);
        var subtotal = RoundCurrency(subOrder.Subtotal);
        var shipping = RoundCurrency(subOrder.Shipping);
        var grossCommission = RoundCurrency(subOrder.PlatformCommissionAmount);
        var promoDiscount = RoundCurrency(subOrder.PromoDiscountAmount);
        var finalCommission = RoundCurrency(subOrder.FinalPlatformCommissionAmount);
        var vendorEarning = RoundCurrency(subOrder.VendorEarningAmount);

        if (total <= 0m)
        {
            throw new InvalidOperationException(
                $"{code}: CommerceHub sub-order {subOrder.Id} total must be positive before vendor earnings or ledger entries are created.");
        }

        if (subtotal < 0m || shipping < 0m || grossCommission < 0m || promoDiscount < 0m || finalCommission < 0m || vendorEarning < 0m)
        {
            throw new InvalidOperationException(
                $"{code}: CommerceHub sub-order {subOrder.Id} financial amounts cannot be negative.");
        }

        if (grossCommission > subtotal)
        {
            throw new InvalidOperationException(
                $"{code}: CommerceHub sub-order {subOrder.Id} gross commission cannot exceed the commission base.");
        }

        if (promoDiscount > grossCommission)
        {
            throw new InvalidOperationException(
                $"{code}: CommerceHub sub-order {subOrder.Id} promo discount cannot exceed gross platform commission.");
        }

        EnsureMoneyEquals(
            RoundCurrency(grossCommission - promoDiscount),
            finalCommission,
            code,
            $"CommerceHub sub-order {subOrder.Id} final commission must equal gross commission minus promo discount.");

        EnsureMoneyEquals(
            RoundCurrency(total - grossCommission),
            vendorEarning,
            code,
            $"CommerceHub sub-order {subOrder.Id} vendor earning must equal sub-order total minus gross platform commission.");
    }

    private static void EnsureExistingEarningMatchesFinancialSnapshot(
        CommerceHubVendorEarning earning,
        CommerceHubSubOrder subOrder,
        Guid? paymentId)
    {
        if (earning.Status == CommerceHubVendorEarningStatus.Pending)
        {
            return;
        }

        EnsureMoneyEquals(
            subOrder.VendorEarningAmount,
            earning.Amount,
            EarningAmountLockedCode,
            $"CommerceHub earning {earning.Id} for sub-order {subOrder.Id} is already {earning.Status} and cannot be repriced.");

        if (paymentId.HasValue && earning.PaymentId.HasValue && earning.PaymentId.Value != paymentId.Value)
        {
            throw new InvalidOperationException(
                $"{EarningPaymentMismatchCode}: CommerceHub earning {earning.Id} for sub-order {subOrder.Id} is locked to payment {earning.PaymentId.Value:D} and cannot be reassigned to payment {paymentId.Value:D}.");
        }
    }

    private static void EnsureMoneyEquals(
        decimal expected,
        decimal actual,
        string code,
        string message)
    {
        if (RoundCurrency(expected) == RoundCurrency(actual))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{code}: {message} Expected {RoundCurrency(expected):0.00}, actual {RoundCurrency(actual):0.00}.");
    }

    private static void SyncEarningLock(
        CommerceHubSubOrder subOrder,
        CommerceHubVendorEarning? earning)
    {
        if (earning == null)
        {
            return;
        }

        subOrder.IsEarningCreated = true;
        subOrder.EarningCreatedAtUtc ??= earning.CreatedAt;
    }

    private static bool CanCreateOrProgressEarning(
        CommerceHubSubOrder subOrder,
        out string reason)
    {
        if (subOrder.Status == OrderStatus.Cancelled)
        {
            reason = "Cancelled vendor orders cannot create or unlock earnings.";
            return false;
        }

        if (subOrder.Status == OrderStatus.Refunded)
        {
            reason = "Refunded vendor orders cannot create or unlock earnings.";
            return false;
        }

        if (subOrder.PaymentStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            reason = "Refunded payments cannot create or unlock vendor earnings.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private async Task<Guid?> ResolvePaidPaymentIdAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubPayments
            .AsNoTracking()
            .Where(payment =>
                payment.OrderId == marketplaceOrderId &&
                payment.Status == CommerceHubPaymentRecordStatus.Paid &&
                !payment.IsDeleted)
            .OrderByDescending(payment => payment.PaidAtUtc ?? payment.CreatedAt)
            .Select(payment => (Guid?)payment.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

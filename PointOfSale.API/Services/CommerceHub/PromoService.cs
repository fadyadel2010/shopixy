using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public sealed record PromoValidationResult(
    PromoCode Promo,
    decimal EligibleBaseCommissionAmount,
    int TotalUsageCount,
    int CustomerUsageCount);

public sealed record PromoConsumptionResult(
    bool IsApplied,
    bool WasAlreadyConsumed,
    string? SkipCode,
    string? SkipReason)
{
    public static PromoConsumptionResult Applied(bool wasAlreadyConsumed = false) =>
        new(true, wasAlreadyConsumed, null, null);

    public static PromoConsumptionResult Skipped(string code, string reason) =>
        new(false, false, code, reason);
}

public interface IPromoService
{
    string? NormalizeCode(string? code);

    Task<PromoValidationResult?> ValidatePromoAsync(
        string? code,
        IReadOnlyCollection<PromoCommissionLine> cartContext,
        Guid? customerId = null,
        CancellationToken cancellationToken = default);

    Task<PromoValidationResult?> ValidatePromoForCheckoutAsync(
        string? code,
        IReadOnlyCollection<PromoCommissionLine> cartContext,
        Guid? customerId = null,
        CancellationToken cancellationToken = default);

    decimal CalculateDiscount(decimal baseCommission, PromoCode promo);

    Task<PromoConsumptionResult> ConsumePromoAsync(
        PromoCode promo,
        Guid marketplaceOrderId,
        Guid? customerId,
        decimal discountAmount,
        decimal eligibleBaseCommissionAmount,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}

public sealed class PromoService : IPromoService
{
    private readonly ApplicationDbContext _dbContext;

    public PromoService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    public async Task<PromoValidationResult?> ValidatePromoAsync(
        string? code,
        IReadOnlyCollection<PromoCommissionLine> cartContext,
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode == null)
        {
            return null;
        }

        var promo = await _dbContext.PromoCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => !item.IsDeleted && item.Code == normalizedCode,
                cancellationToken);

        return await ValidatePromoCoreAsync(promo, normalizedCode, cartContext, customerId, cancellationToken);
    }

    public async Task<PromoValidationResult?> ValidatePromoForCheckoutAsync(
        string? code,
        IReadOnlyCollection<PromoCommissionLine> cartContext,
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode == null)
        {
            return null;
        }

        PromoCode? promo;
        if (_dbContext.Database.IsRelational())
        {
            promo = await _dbContext.PromoCodes
                .FromSqlInterpolated($@"
SELECT *
FROM ""PromoCodes""
WHERE ""Code"" = {normalizedCode}
  AND NOT ""IsDeleted""
FOR UPDATE")
                .AsTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            promo = await _dbContext.PromoCodes
                .FirstOrDefaultAsync(
                    item => !item.IsDeleted && item.Code == normalizedCode,
                    cancellationToken);
        }

        return await ValidatePromoCoreAsync(promo, normalizedCode, cartContext, customerId, cancellationToken);
    }

    public decimal CalculateDiscount(decimal baseCommission, PromoCode promo)
    {
        ArgumentNullException.ThrowIfNull(promo);

        if (baseCommission <= 0m)
        {
            return 0m;
        }

        var normalizedBase = CommissionCalculator.RoundAmount(baseCommission);
        var rawDiscount = promo.DiscountType switch
        {
            PromoDiscountType.Percentage => normalizedBase * (promo.DiscountValue / 100m),
            PromoDiscountType.Fixed => promo.DiscountValue,
            _ => throw new ArgumentOutOfRangeException(nameof(promo.DiscountType), "Promo discount type is invalid.")
        };

        var cappedDiscount = promo.MaxDiscountAmount.HasValue
            ? Math.Min(rawDiscount, promo.MaxDiscountAmount.Value)
            : rawDiscount;

        return CommissionCalculator.RoundAmount(Math.Min(normalizedBase, Math.Max(0m, cappedDiscount)));
    }

    public async Task<PromoConsumptionResult> ConsumePromoAsync(
        PromoCode promo,
        Guid marketplaceOrderId,
        Guid? customerId,
        decimal discountAmount,
        decimal eligibleBaseCommissionAmount,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(promo);

        if (marketplaceOrderId == Guid.Empty)
        {
            throw new ArgumentException("MarketplaceOrderId is required.", nameof(marketplaceOrderId));
        }

        var authoritativePromo = await LoadPromoForConsumptionAuthorityAsync(promo.Id, cancellationToken);
        if (authoritativePromo == null)
        {
            return PromoConsumptionResult.Skipped(
                "PROMO_NOT_FOUND",
                "Promo claim skipped because the promo no longer exists.");
        }

        var normalizedDiscountAmount = CommissionCalculator.RoundAmount(Math.Max(0m, discountAmount));
        var normalizedEligibleBaseCommissionAmount = CommissionCalculator.RoundAmount(Math.Max(0m, eligibleBaseCommissionAmount));

        if (normalizedDiscountAmount <= 0m || normalizedEligibleBaseCommissionAmount <= 0m)
        {
            return PromoConsumptionResult.Skipped(
                "PROMO_NOT_APPLIED",
                "Promo claim skipped because the order has no eligible commission discount to apply.");
        }

        var existingUsage = await _dbContext.PromoCodeUsages
            .FirstOrDefaultAsync(
                usage => usage.PromoCodeId == authoritativePromo.Id && usage.MarketplaceOrderId == marketplaceOrderId,
                cancellationToken);

        if (existingUsage != null)
        {
            authoritativePromo.UsedCount = Math.Max(
                authoritativePromo.UsedCount,
                await GetTotalUsageCountAsync(authoritativePromo.Id, cancellationToken));
            return PromoConsumptionResult.Applied(wasAlreadyConsumed: true);
        }

        var existingOrderUsage = await _dbContext.PromoCodeUsages
            .FirstOrDefaultAsync(
                usage => usage.MarketplaceOrderId == marketplaceOrderId,
                cancellationToken);

        if (existingOrderUsage != null)
        {
            return PromoConsumptionResult.Skipped(
                "PROMO_ALREADY_CONSUMED_FOR_ORDER",
                "A promo has already been consumed for this order.");
        }

        var nowUtc = DateTime.UtcNow;
        if (TryGetPromoValidationFailure(
                authoritativePromo,
                await GetUsageSnapshotAsync(authoritativePromo.Id, customerId, cancellationToken),
                nowUtc,
                out var failureCode,
                out var failureMessage))
        {
            return PromoConsumptionResult.Skipped(
                failureCode!,
                failureMessage!.Replace("{code}", authoritativePromo.Code, StringComparison.Ordinal));
        }

        _dbContext.PromoCodeUsages.Add(new PromoCodeUsage
        {
            Id = Guid.NewGuid(),
            PromoCodeId = authoritativePromo.Id,
            MarketplaceOrderId = marketplaceOrderId,
            CustomerId = customerId,
            PromoCodeSnapshot = authoritativePromo.Code,
            DiscountAmount = normalizedDiscountAmount,
            EligibleBaseCommissionAmount = normalizedEligibleBaseCommissionAmount,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            AppliedAtUtc = nowUtc,
            CreatedAt = nowUtc,
            IsDeleted = false
        });

        authoritativePromo.UsedCount = await GetTotalUsageCountAsync(authoritativePromo.Id, cancellationToken) + 1;
        authoritativePromo.UpdatedAt = nowUtc;

        return PromoConsumptionResult.Applied();
    }

    private async Task<PromoCode?> LoadPromoForConsumptionAuthorityAsync(
        Guid promoCodeId,
        CancellationToken cancellationToken)
    {
        if (promoCodeId == Guid.Empty)
        {
            return null;
        }

        foreach (var entry in _dbContext.ChangeTracker
                     .Entries<PromoCode>()
                     .Where(entry => entry.Entity.Id == promoCodeId)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }

        if (_dbContext.Database.IsRelational())
        {
            return await _dbContext.PromoCodes
                .FromSqlInterpolated($@"
SELECT *
FROM ""PromoCodes""
WHERE ""Id"" = {promoCodeId}
  AND NOT ""IsDeleted""
FOR UPDATE")
                .AsTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.PromoCodes
            .FirstOrDefaultAsync(
                item => item.Id == promoCodeId && !item.IsDeleted,
                cancellationToken);
    }

    private async Task<PromoValidationResult> ValidatePromoCoreAsync(
        PromoCode? promo,
        string normalizedCode,
        IReadOnlyCollection<PromoCommissionLine> cartContext,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        if (promo == null)
        {
            throw new CommerceHubCheckoutException(
                "PROMO_NOT_FOUND",
                $"Promo code '{normalizedCode}' was not found.",
                StatusCodes.Status400BadRequest);
        }

        var usageSnapshot = await GetUsageSnapshotAsync(promo.Id, customerId, cancellationToken);
        ThrowIfPromoInvalid(promo, usageSnapshot, normalizedCode, DateTime.UtcNow);

        var eligibleBaseCommission = CommissionCalculator.RoundAmount(
            cartContext
                .GroupBy(line => line.StoreId)
                .Select(group => PromoCalculationEngine.Apply(
                    promo,
                    group.Key,
                    group.ToList(),
                    CalculateDiscount).EligibleBaseCommissionAmount)
                .Sum());

        if (eligibleBaseCommission <= 0m)
        {
            throw new CommerceHubCheckoutException(
                "PROMO_NOT_APPLICABLE",
                $"Promo code '{normalizedCode}' does not apply to the current CommerceHub cart.",
                StatusCodes.Status400BadRequest);
        }

        return new PromoValidationResult(
            promo,
            eligibleBaseCommission,
            usageSnapshot.TotalUsageCount,
            usageSnapshot.CustomerUsageCount);
    }

    private async Task<PromoUsageSnapshot> GetUsageSnapshotAsync(
        Guid promoCodeId,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        var totalUsageCount = await GetTotalUsageCountAsync(promoCodeId, cancellationToken);
        var customerUsageCount = customerId.HasValue
            ? await _dbContext.PromoCodeUsages
                .CountAsync(
                    usage => usage.PromoCodeId == promoCodeId && usage.CustomerId == customerId.Value,
                    cancellationToken)
            : 0;

        return new PromoUsageSnapshot(totalUsageCount, customerUsageCount);
    }

    private async Task<int> GetTotalUsageCountAsync(Guid promoCodeId, CancellationToken cancellationToken)
    {
        return await _dbContext.PromoCodeUsages
            .CountAsync(usage => usage.PromoCodeId == promoCodeId, cancellationToken);
    }

    private static void ThrowIfPromoInvalid(
        PromoCode promo,
        PromoUsageSnapshot usageSnapshot,
        string normalizedCode,
        DateTime nowUtc)
    {
        if (TryGetPromoValidationFailure(promo, usageSnapshot, nowUtc, out var failureCode, out var failureMessage))
        {
            throw new CommerceHubCheckoutException(
                failureCode!,
                failureMessage!.Replace("{code}", normalizedCode, StringComparison.Ordinal),
                StatusCodes.Status400BadRequest);
        }
    }

    private static bool TryGetPromoValidationFailure(
        PromoCode promo,
        PromoUsageSnapshot usageSnapshot,
        DateTime nowUtc,
        out string? failureCode,
        out string? failureMessage)
    {
        if (!promo.IsActive)
        {
            failureCode = "PROMO_INACTIVE";
            failureMessage = "Promo code '{code}' is not active.";
            return true;
        }

        if (promo.ExpiryDate.HasValue && promo.ExpiryDate.Value <= nowUtc)
        {
            failureCode = "PROMO_EXPIRED";
            failureMessage = "Promo code '{code}' has expired.";
            return true;
        }

        if (promo.UsageLimit.HasValue && usageSnapshot.TotalUsageCount >= promo.UsageLimit.Value)
        {
            failureCode = "PROMO_USAGE_LIMIT_REACHED";
            failureMessage = "Promo code '{code}' has reached its usage limit.";
            return true;
        }

        if (promo.MaxUsagePerCustomer.HasValue && usageSnapshot.CustomerUsageCount >= promo.MaxUsagePerCustomer.Value)
        {
            failureCode = "PROMO_CUSTOMER_USAGE_LIMIT_REACHED";
            failureMessage = "Promo code '{code}' has already been used the maximum number of times for this customer.";
            return true;
        }

        failureCode = null;
        failureMessage = null;
        return false;
    }

    private sealed record PromoUsageSnapshot(int TotalUsageCount, int CustomerUsageCount);
}

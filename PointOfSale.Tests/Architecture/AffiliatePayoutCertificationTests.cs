namespace PointOfSale.Tests.Architecture;

using System;
using Xunit;

/// <summary>
/// PASS A4 — Affiliate Payout Authority Certification
/// Regression-freeze tests for balance authority, payout eligibility, and tenant isolation.
/// DO NOT DELETE OR RELAX THESE TESTS — they are production financial authority guards.
/// </summary>
public sealed class AffiliatePayoutCertificationTests
{
    [Fact]
    public void MinimumThreshold_MustBeHardcodedTo100()
    {
        var requestEntity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs");

        // Hard threshold must be exactly 100m, no exceptions
        Assert.Contains("public const decimal MinimumThreshold = 100m;", requestEntity, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutRequestCreation_MustValidateThresholdAndAvailableBalance()
    {
        var requestEntity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs");

        // Request amount must be >= 100
        Assert.Contains("if (requestedAmount < MinimumThreshold)", requestEntity, StringComparison.Ordinal);
        
        // Request amount must not exceed AvailableForWithdrawal
        Assert.Contains("if (requestedAmount > availableForWithdrawal)", requestEntity, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceCalculation_CorrectFormula()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Formula uses earned commission, payout reductions, withdrawals, and reservations.
        Assert.Contains("netApproved +", service, StringComparison.Ordinal);
        Assert.Contains("totalPayoutReductions +", service, StringComparison.Ordinal);
        Assert.Contains("totalWithdrawals +", service, StringComparison.Ordinal);
        Assert.Contains("(-totalPendingRequests)", service, StringComparison.Ordinal);
        Assert.Contains("l.Amount < 0m", service, StringComparison.Ordinal);
        Assert.Contains("positive adjustments/bonuses: Never payout authority", service, StringComparison.Ordinal);

        // Correct documentation explaining why CommissionLedger.Paid is excluded (to avoid double deduction)
        Assert.Contains("CommissionLedger.Paid does NOT reduce available balance", service, StringComparison.Ordinal);
        Assert.Contains("Only BalanceLedger affects available balance calculation", service, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceCalculation_ClampsDisplayOnly()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Real balance for validation is RawAvailable, display is clamped
        Assert.Contains("var rawAvailable =", service, StringComparison.Ordinal);
        Assert.Contains("var displayAvailable = Math.Max(0, rawAvailable);", service, StringComparison.Ordinal);
        Assert.Contains("Guards MUST use RawAvailable to prevent over-withdrawal.", service, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceCalculation_RejectsBlankCurrency()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Checks for blank, empty or whitespace-only currencies on Commission, Balance and Payout requests
        Assert.Contains("hasBlankCommissionCurrency", service, StringComparison.Ordinal);
        Assert.Contains("hasBlankBalanceCurrency", service, StringComparison.Ordinal);
        Assert.Contains("hasBlankPayoutCurrency", service, StringComparison.Ordinal);
        Assert.Contains("l.Currency.Trim() == string.Empty", service, StringComparison.Ordinal);
        Assert.Contains("AffiliatePayoutAuthorityException", service, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceCalculation_DetectsMultiCurrencyConflict()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Deduplicates and normalizes currencies, checking for multiple active currencies
        Assert.Contains("ResolveAuthoritativeBalanceCurrenciesAsync", service, StringComparison.Ordinal);
        Assert.Contains("currencies.Count > 1", service, StringComparison.Ordinal);
        Assert.Contains("multiple currencies exist in the authoritative balance stream", service, StringComparison.Ordinal);
        Assert.Contains("throw new AffiliatePayoutAuthorityException", service, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceCalculation_MustMatchCurrentStoreBaseCurrency()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        Assert.Contains("ResolveStoreBaseCurrencyAsync", service, StringComparison.Ordinal);
        Assert.Contains("tenant.Currency", service, StringComparison.Ordinal);
        Assert.Contains("does not match store base currency", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutPersistence_MustProtectImmutableAuthorityFields()
    {
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("GuardAffiliatePayoutAuthority();", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<AffiliateBalanceLedger>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<AffiliatePayoutTransferAllocation>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("identity, tenant, amount, reference, and currency are immutable", dbContext, StringComparison.Ordinal);
        Assert.Contains("identity, amount, currency, tenant, affiliate, and payout destination snapshot are immutable", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutRequests_DataIsolationFilter()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Query must filter by BOTH TenantId (storeTenantId) and AffiliateAccountId
        Assert.Contains("r.StoreTenantId == storeTenantId &&", service, StringComparison.Ordinal);
        Assert.Contains("r.AffiliateAccountId == affiliateAccountId", service, StringComparison.Ordinal);

        // Security check if any items returned leak other affiliates
        Assert.Contains("items.Any(r => r.AffiliateAccountId != affiliateAccountId)", service, StringComparison.Ordinal);
        Assert.Contains("throw new SecurityException(\"Cross-affiliate data access detected\")", service, StringComparison.Ordinal);
    }
}

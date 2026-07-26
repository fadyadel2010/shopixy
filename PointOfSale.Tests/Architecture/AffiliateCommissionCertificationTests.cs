namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS A3 — Affiliate Commission Authority Certification
/// Regression-freeze tests for commission amount, ownership, and database constraint enforcement.
/// DO NOT DELETE OR RELAX THESE TESTS — they are production financial authority guards.
/// </summary>
public sealed class AffiliateCommissionCertificationTests
{
    [Fact]
    public void CommissionAmount_MustBeDerivedFromCheckoutSnapshots()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var orderItem = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/OrderItem.cs");

        // OrderItem holds all required immutable snapshot fields
        Assert.Contains("AffiliateCommissionSnapshotCapturedAtUtc", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionRateSnapshot", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionBaseAmountSnapshot", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionAmountSnapshot", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionEligible", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionPolicyIdSnapshot", orderItem, StringComparison.Ordinal);

        // Commission recording uses snapshot exclusively — never re-queries live policy
        Assert.Contains("CreateEarnedFromSnapshot", service, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionRateSnapshot.Value", service, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionAmountSnapshot.Value", service, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionBaseAmountSnapshot", service, StringComparison.Ordinal);

        // Checkout locks policy rows before capturing snapshot (FOR UPDATE / UPDLOCK)
        Assert.Contains("LoadAffiliateCommissionPolicySnapshotsAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRows: true", checkout, StringComparison.Ordinal);
        Assert.Contains("LockAffiliateCommissionPolicyAuthorityAsync", checkout, StringComparison.Ordinal);

        // Snapshot persist condition — eligible + positive commission + resolved affiliate
        Assert.Contains("AffiliateCommissionSnapshotCapturedAtUtc = shouldPersistAffiliateCommissionSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionAmountSnapshot = shouldPersistAffiliateCommissionSnapshot", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionAmount_MustFailClosedOnMissingSnapshot()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // If item is marked eligible but snapshot is incomplete, recording must throw — not skip silently
        Assert.Contains("is marked affiliate-eligible but is missing immutable commission snapshot values", service, StringComparison.Ordinal);
        Assert.Contains("!item.AffiliateCommissionRateSnapshot.HasValue", service, StringComparison.Ordinal);
        Assert.Contains("!item.AffiliateCommissionAmountSnapshot.HasValue", service, StringComparison.Ordinal);
        Assert.Contains("!item.AffiliateCommissionBaseAmountSnapshot.HasValue", service, StringComparison.Ordinal);
        Assert.Contains("!hasImmutableSnapshot", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionAmount_PolicyConflict_MustBlockCheckout()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        // Multiple active policies for same product → checkout must be blocked (fail-closed)
        Assert.Contains("AFFILIATE_POLICY_AUTHORITY_CONFLICT", checkout, StringComparison.Ordinal);
        Assert.Contains("groupPolicies.Count > 1", checkout, StringComparison.Ordinal);
        Assert.Contains("Checkout cannot continue because affiliate commission policy authority is ambiguous", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionOwnership_MustBeLockedToAttributedAffiliate()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var order = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Order.cs");

        // Order holds attribution snapshot fields
        Assert.Contains("public Guid? AffiliateAccountId { get; set; }", order, StringComparison.Ordinal);
        Assert.Contains("public string? AffiliateReferralCode { get; set; }", order, StringComparison.Ordinal);

        // Commission recording resolves ownership from the canonical attribution row only
        Assert.Contains("var attribution = await _context.AffiliateAttributions", service, StringComparison.Ordinal);
        Assert.Contains("a.Id == attribution.AffiliateAccountId", service, StringComparison.Ordinal);
        Assert.Contains("affiliate.Id", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionOwnership_Recovery_MustValidateSnapshotBeforeRebuild()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // Recovery path must reject mismatched affiliate or referral code — not silently accept corrupt data
        Assert.Contains("Attribution conflict for Order", service, StringComparison.Ordinal);
        Assert.Contains("does not match order snapshot affiliate", service, StringComparison.Ordinal);
        Assert.Contains("does not match order snapshot referral code", service, StringComparison.Ordinal);
        Assert.Contains("snapshot rebuild", service, StringComparison.Ordinal);

        // Race recovery also validates snapshot match
        Assert.Contains("race recovery, snapshot rebuild", service, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseConstraints_MustEnforceLedgerRanges()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateCommissionLedgerConfiguration.cs");

        // CHECK: commission amount > 0 (no zero or negative commissions in ledger)
        Assert.Contains("CK_AffiliateCommLedger_Amount", config, StringComparison.Ordinal);
        // CHECK: base amount > 0
        Assert.Contains("CK_AffiliateCommLedger_BaseAmount", config, StringComparison.Ordinal);
        // CHECK: rate valid (0 < rate <= 0.50)
        Assert.Contains("CK_AffiliateCommLedger_Rate", config, StringComparison.Ordinal);
        // CHECK: reversal/approved/paid reference original, earned does not
        Assert.Contains("CK_AffiliateCommLedger_ReversalRef", config, StringComparison.Ordinal);
        // CHECK: entry type is a known lifecycle state
        Assert.Contains("CK_AffiliateCommLedger_EntryType", config, StringComparison.Ordinal);
        // CHECK: idempotency key is not blank/whitespace
        Assert.Contains("CK_AffiliateCommLedger_IdempotencyKey_NotBlank", config, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseConstraints_OrderItemSnapshot_MustBeEnforced()
    {
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        // DB-level guard on OrderItem snapshot consistency
        Assert.Contains("CK_OrderItems_AffiliateCommissionSnapshot_Valid", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalMarketplace_TenantIdMustBeNull_StoreTenantIdMustBeSaleStore()
    {
        var ledger = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // AF-19B: TenantId is always NULL — global affiliate has no tenant binding
        // StoreTenantId is the operative store boundary (Order.TenantId)
        Assert.Contains("StoreTenantId", ledger, StringComparison.Ordinal);
        Assert.Contains("AF-19B", service, StringComparison.Ordinal);
        Assert.Contains("StoreTenantId = order.TenantId", service, StringComparison.Ordinal);

        // Config must use IdempotencyKey-only unique index.
        // The comment in the config explains WHY a composite (TenantId, IdempotencyKey) index would be WRONG:
        // PostgreSQL NULL != NULL means the unique constraint would be defeated.
        // Assert that the comment with that explanation is present (certifying the design decision is documented)
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateCommissionLedgerConfiguration.cs");
        Assert.Contains("UQ_AffiliateCommLedger_Idempotency", config, StringComparison.Ordinal);
        Assert.Contains("NULL \u2260 NULL in unique constraints", config, StringComparison.Ordinal);
        // The unique index is declared on IdempotencyKey alone (not as part of a composite)
        Assert.Contains("builder.HasIndex(e => e.IdempotencyKey)", config, StringComparison.Ordinal);
    }
}

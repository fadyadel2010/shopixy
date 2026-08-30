using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-1 Domain tests: Entity-level invariants (factory methods, validation, guard logic).
/// These are pure unit tests — no EF/DB required.
/// </summary>
public class AffiliateDomainTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    // ═══════════════════════════════════════════════════════════════════
    // AffiliateAccount
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateAccount_Create_ValidInput_Succeeds()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test User");

        Assert.Equal(_tenantId, account.TenantId);
        Assert.Equal(_userId, account.UserId);
        Assert.Equal("ABC123", account.ReferralCode); // auto-uppercased
        Assert.Equal("Test User", account.DisplayName);
        Assert.Equal(AffiliateStatus.Pending, account.Status);
        Assert.False(account.IsActive);
    }

    [Fact]
    public void AffiliateAccount_Create_EmptyUserId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateAccount.Create(_tenantId, Guid.Empty, "ABC123", "Test"));
    }

    [Fact]
    public void AffiliateAccount_Create_ShortReferralCode_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateAccount.Create(_tenantId, _userId, "AB", "Test"));
    }

    [Fact]
    public void AffiliateAccount_Create_LongReferralCode_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateAccount.Create(_tenantId, _userId, "1234567890123", "Test")); // 13 chars
    }

    [Fact]
    public void AffiliateAccount_Create_ReferralCode_IsUppercased()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "abcdef", "Test");
        Assert.Equal("ABCDEF", account.ReferralCode);
    }

    [Fact]
    public void AffiliateAccount_Activate_TransitionFromPending()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test");
        Assert.Equal(AffiliateStatus.Pending, account.Status);

        account.Activate();

        Assert.Equal(AffiliateStatus.Active, account.Status);
        Assert.True(account.IsActive);
        Assert.NotNull(account.ActivatedAtUtc);
    }

    [Fact]
    public void AffiliateAccount_Activate_IsIdempotent()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test");
        account.Activate();
        var firstActivatedAt = account.ActivatedAtUtc;

        account.Activate(); // idempotent

        Assert.Equal(AffiliateStatus.Active, account.Status);
        Assert.Equal(firstActivatedAt, account.ActivatedAtUtc); // not re-stamped
    }

    [Fact]
    public void AffiliateAccount_Suspend_RequiresReason()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test");
        account.Activate();

        Assert.Throws<ArgumentException>(() => account.Suspend(""));
        Assert.Throws<ArgumentException>(() => account.Suspend(null!));
    }

    [Fact]
    public void AffiliateAccount_Suspend_SetsStatusAndReason()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test");
        account.Activate();
        account.Suspend("Fraud detected");

        Assert.Equal(AffiliateStatus.Suspended, account.Status);
        Assert.False(account.IsActive);
        Assert.Equal("Fraud detected", account.SuspensionReason);
        Assert.NotNull(account.SuspendedAtUtc);
    }

    [Fact]
    public void AffiliateAccount_Reactivate_AfterSuspension()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test");
        account.Activate();
        account.Suspend("Test");
        account.Activate();

        Assert.Equal(AffiliateStatus.Active, account.Status);
        Assert.True(account.IsActive);
        Assert.Null(account.SuspensionReason); // cleared on activate
    }

    [Fact]
    public void AffiliateAccount_Suspend_FromPending_Throws()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test");

        Assert.Throws<InvalidOperationException>(() => account.Suspend("Not active"));
    }

    [Fact]
    public void AffiliateAccount_Close_FromActive_IsTerminal()
    {
        var account = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test");
        account.Activate();
        account.Close("Affiliate requested closure");

        Assert.Equal(AffiliateStatus.Closed, account.Status);
        Assert.False(account.IsActive);
        Assert.NotNull(account.ClosedAtUtc);
        Assert.Equal("Affiliate requested closure", account.ClosureReason);
        Assert.Throws<InvalidOperationException>(() => account.Activate());
        Assert.Throws<InvalidOperationException>(() => account.Suspend("Cannot suspend closed account"));
    }

    [Fact]
    public void AffiliateAccount_Close_FromPendingOrSuspended_Throws()
    {
        var pending = AffiliateAccount.Create(_tenantId, _userId, "ABC123", "Test");
        Assert.Throws<InvalidOperationException>(() => pending.Close("Not active"));

        var suspended = AffiliateAccount.Create(_tenantId, _userId, "XYZ123", "Other Test");
        suspended.Activate();
        suspended.Suspend("Suspended first");

        Assert.Throws<InvalidOperationException>(() => suspended.Close("Must reactivate before closure"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // AffiliateProductPolicy
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateProductPolicy_Create_ValidRate_Succeeds()
    {
        var policy = AffiliateProductPolicy.Create(
            _tenantId, Guid.NewGuid(), 0.10m, categoryId: Guid.NewGuid());

        Assert.Equal(0.10m, policy.CommissionRate);
        Assert.True(policy.IsEnabled);
        Assert.Null(policy.MaxCommissionPerItem);
    }

    [Theory]
    [InlineData(0.00001)]     // Below minimum 0.0001
    [InlineData(0.51)]        // Above maximum 0.50
    [InlineData(0)]
    [InlineData(-0.01)]
    public void AffiliateProductPolicy_Create_InvalidRate_Throws(decimal rate)
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateProductPolicy.Create(
                _tenantId, Guid.NewGuid(), rate, categoryId: Guid.NewGuid()));
    }

    [Fact]
    public void AffiliateProductPolicy_Create_BoundaryRates_Succeed()
    {
        var policyMin = AffiliateProductPolicy.Create(
            _tenantId, Guid.NewGuid(), 0.0001m, categoryId: Guid.NewGuid());
        Assert.Equal(0.0001m, policyMin.CommissionRate);

        var policyMax = AffiliateProductPolicy.Create(
            _tenantId, Guid.NewGuid(), 0.50m, categoryId: Guid.NewGuid());
        Assert.Equal(0.50m, policyMax.CommissionRate);
    }

    [Fact]
    public void AffiliateProductPolicy_Create_ZeroMaxCap_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateProductPolicy.Create(
                _tenantId, Guid.NewGuid(), 0.10m, 0, Guid.NewGuid()));
    }

    [Fact]
    public void AffiliateProductPolicy_Create_NegativeMaxCap_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateProductPolicy.Create(
                _tenantId, Guid.NewGuid(), 0.10m, -5m, Guid.NewGuid()));
    }

    [Fact]
    public void AffiliateProductPolicy_Create_ExcessivePersistedPrecision_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateProductPolicy.Create(
                _tenantId, Guid.NewGuid(), 0.1234567m, categoryId: Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() =>
            AffiliateProductPolicy.Create(
                _tenantId, Guid.NewGuid(), 0.10m, 1.23456m, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() =>
            AffiliateProductPolicy.Create(
                _tenantId, Guid.NewGuid(), 0.10m, 100_000_000_000_000m, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AffiliateProductPolicy_Create_WithoutCategory_Throws(bool useEmptyCategory)
    {
        Guid? categoryId = useEmptyCategory ? Guid.Empty : null;

        Assert.Throws<ArgumentException>(() =>
            AffiliateProductPolicy.Create(
                _tenantId, Guid.NewGuid(), 0.10m, categoryId: categoryId));
    }

    [Fact]
    public void AffiliateProductPolicy_UpdateRate_Succeeds()
    {
        var policy = AffiliateProductPolicy.Create(
            _tenantId, Guid.NewGuid(), 0.10m, categoryId: Guid.NewGuid());
        policy.UpdateRate(0.15m, 100m);

        Assert.Equal(0.15m, policy.CommissionRate);
        Assert.Equal(100m, policy.MaxCommissionPerItem);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AffiliateAttribution
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateAttribution_Create_ValidInput_Succeeds()
    {
        var attribution = AffiliateAttribution.Create(
            _tenantId, Guid.NewGuid(), Guid.NewGuid(), "REFCODE1", AttributionSource.ReferralLink);

        Assert.Equal("REFCODE1", attribution.ReferralCode);
        Assert.Equal(AttributionSource.ReferralLink, attribution.Source);
        Assert.NotEqual(default, attribution.AttributedAtUtc);
    }

    [Fact]
    public void AffiliateAttribution_Create_EmptyOrderId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateAttribution.Create(_tenantId, Guid.Empty, Guid.NewGuid(), "REF123", AttributionSource.ReferralLink));
    }

    [Fact]
    public void AffiliateAttribution_Create_EmptyReferralCode_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateAttribution.Create(_tenantId, Guid.NewGuid(), Guid.NewGuid(), "", AttributionSource.ReferralLink));
    }

    // ═══════════════════════════════════════════════════════════════════
    // AffiliateCommissionLedger
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CommissionLedger_CreateEarned_ValidInput_Succeeds()
    {
        var orderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();

        var entry = AffiliateCommissionLedger.CreateEarned(
            _tenantId, Guid.NewGuid(), Guid.NewGuid(),
            orderId, orderItemId,
            paidAmount: 1000m, commissionRate: 0.10m, maxPerItem: null,
            currency: "USD");

        Assert.Equal(1000m, entry.BaseAmount);
        Assert.Equal(0.10m, entry.CommissionRate);
        Assert.Equal(100m, entry.CommissionAmount); // 1000 * 0.10
        Assert.Equal(CommissionEntryType.Earned, entry.EntryType);
        Assert.Null(entry.OriginalEntryId);
        Assert.Equal($"AFFILIATE_COMM:EARNED:{orderItemId}", entry.IdempotencyKey);
    }

    [Fact]
    public void CommissionLedger_CreateEarned_WithMaxCap_CapsCommission()
    {
        var entry = AffiliateCommissionLedger.CreateEarned(
            _tenantId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            paidAmount: 1000m, commissionRate: 0.10m, maxPerItem: 50m,
            currency: "USD");

        Assert.Equal(50m, entry.CommissionAmount); // capped at 50, not 100
    }

    [Fact]
    public void CommissionLedger_CreateEarned_ZeroPaidAmount_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateCommissionLedger.CreateEarned(
                _tenantId, Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(),
                paidAmount: 0m, commissionRate: 0.10m, maxPerItem: null,
                currency: "USD"));
    }

    [Fact]
    public void CommissionLedger_CreateEarned_NegativePaidAmount_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateCommissionLedger.CreateEarned(
                _tenantId, Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(),
                paidAmount: -100m, commissionRate: 0.10m, maxPerItem: null,
                currency: "USD"));
    }

    [Fact]
    public void CommissionLedger_CreateEarned_InvalidRate_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateCommissionLedger.CreateEarned(
                _tenantId, Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(),
                paidAmount: 100m, commissionRate: 0m, maxPerItem: null,
                currency: "USD"));
    }

    [Fact]
    public void CommissionLedger_CreateReversal_BaseAmountIsOriginal_NotCommission()
    {
        // This test enforces the MANDATORY CORRECTION:
        // Reversal BaseAmount = original BaseAmount, not original commission

        var originalBaseAmount = 1000m;
        var originalCommission = 100m;     // 10% of 1000
        var originalRate = 0.10m;

        var reversal = AffiliateCommissionLedger.CreateReversal(
            _tenantId, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            originalBaseAmount, originalCommission, originalRate,
            "USD");

        Assert.Equal(CommissionEntryType.Reversed, reversal.EntryType);
        Assert.Equal(originalBaseAmount, reversal.BaseAmount);  // MUST be 1000, not 100
        Assert.Equal(originalCommission, reversal.CommissionAmount);
        Assert.Equal(originalRate, reversal.CommissionRate);
        Assert.NotNull(reversal.OriginalEntryId);
    }

    [Fact]
    public void CommissionLedger_CreateReversal_ZeroOriginalCommission_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateCommissionLedger.CreateReversal(
                _tenantId, Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(),
                originalBaseAmount: 1000m, originalCommission: 0m, originalRate: 0.10m,
                "USD"));
    }

    [Fact]
    public void CommissionLedger_CreateReversal_ZeroBaseAmount_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AffiliateCommissionLedger.CreateReversal(
                _tenantId, Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(),
                originalBaseAmount: 0m, originalCommission: 100m, originalRate: 0.10m,
                "USD"));
    }

    [Fact]
    public void CommissionLedger_EarnedEntry_HasNullOriginalEntryId()
    {
        var entry = AffiliateCommissionLedger.CreateEarned(
            _tenantId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            paidAmount: 500m, commissionRate: 0.05m, maxPerItem: null,
            currency: "EGP");

        Assert.Null(entry.OriginalEntryId);
    }

    [Fact]
    public void CommissionLedger_ReversalEntry_HasNonNullOriginalEntryId()
    {
        var originalId = Guid.NewGuid();
        var reversal = AffiliateCommissionLedger.CreateReversal(
            _tenantId, originalId,
            Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            originalBaseAmount: 500m, originalCommission: 25m, originalRate: 0.05m,
            "EGP");

        Assert.Equal(originalId, reversal.OriginalEntryId);
    }

    [Fact]
    public void CommissionLedger_IdempotencyKey_FormatMatchesDesign()
    {
        var tenantId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();

        var earned = AffiliateCommissionLedger.CreateEarned(
            tenantId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), orderItemId,
            paidAmount: 100m, commissionRate: 0.10m, maxPerItem: null,
            currency: "USD");

        Assert.Equal($"AFFILIATE_COMM:EARNED:{orderItemId}", earned.IdempotencyKey);

        var reversal = AffiliateCommissionLedger.CreateReversal(
            tenantId, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), orderItemId,
            originalBaseAmount: 100m, originalCommission: 10m, originalRate: 0.10m,
            "USD");

        Assert.Equal($"AFFILIATE_COMM:REVERSED:{orderItemId}", reversal.IdempotencyKey);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AffiliateCommissionLedger has NO IsDeleted
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CommissionLedger_DoesNotInheritFromBaseEntity()
    {
        // MANDATORY CORRECTION: AffiliateCommissionLedger must NOT have IsDeleted
        var type = typeof(AffiliateCommissionLedger);

        Assert.False(typeof(PointOfSale.Core.Entities.BaseEntity).IsAssignableFrom(type),
            "AffiliateCommissionLedger must NOT inherit from BaseEntity (no IsDeleted, no UpdatedAt)");

        // Ensure no IsDeleted property
        var isDeletedProp = type.GetProperty("IsDeleted");
        Assert.Null(isDeletedProp);

        // Ensure no UpdatedAt property
        var updatedAtProp = type.GetProperty("UpdatedAt");
        Assert.Null(updatedAtProp);
    }
}

# AF-1: Affiliate System — Write-Side Domain Model

> **Phase**: AF-1 (Write-Side Only)
> **Status**: Design Complete
> **Scope**: Domain Aggregates, PostgreSQL Schema, Domain Services, Events, Idempotency
> **Out of Scope**: UI, MongoDB, Elasticsearch, Payouts, Background Jobs

---

## 1. Aggregates

### 1.1 AffiliateAccount

The tenant-scoped identity of an affiliate. One per `User` per `Tenant`.

```csharp
namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// Aggregate root: A user's affiliate identity within a tenant.
///
/// INVARIANTS:
/// - Exactly ONE AffiliateAccount per (TenantId, UserId) — enforced DB + domain
/// - Status transitions: Pending → Active → Suspended → Active (cyclic except Pending)
/// - IsActive computed from Status == Active
/// - Cannot earn commission unless Status == Active
/// - ReferralCode is globally unique within tenant, immutable after creation
/// </summary>
public class AffiliateAccount : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// The platform User who owns this affiliate account.
    /// FK → Users.Id. UNIQUE per tenant.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Unique referral code used in tracking URLs.
    /// Format: 6-12 alphanumeric, case-insensitive.
    /// IMMUTABLE after creation. Unique per tenant.
    /// </summary>
    public string ReferralCode { get; private set; } = string.Empty;

    /// <summary>
    /// Current status of the affiliate account.
    /// </summary>
    public AffiliateStatus Status { get; private set; } = AffiliateStatus.Pending;

    /// <summary>
    /// Display name for admin/reporting.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the affiliate can earn commission right now.
    /// </summary>
    public bool IsActive => Status == AffiliateStatus.Active;

    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? SuspendedAtUtc { get; private set; }
    public string? SuspensionReason { get; private set; }

    // --- Factory ---
    public static AffiliateAccount Create(
        Guid tenantId, Guid userId, string referralCode, string displayName)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId required");
        if (userId == Guid.Empty) throw new ArgumentException("UserId required");
        if (string.IsNullOrWhiteSpace(referralCode)) throw new ArgumentException("ReferralCode required");
        if (referralCode.Length < 6 || referralCode.Length > 12)
            throw new ArgumentException("ReferralCode must be 6-12 characters");

        return new AffiliateAccount
        {
            TenantId = tenantId,
            UserId = userId,
            ReferralCode = referralCode.ToUpperInvariant(),
            DisplayName = displayName,
            Status = AffiliateStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Activate()
    {
        if (Status == AffiliateStatus.Active) return; // idempotent
        Status = AffiliateStatus.Active;
        ActivatedAtUtc = DateTime.UtcNow;
        SuspensionReason = null;
    }

    public void Suspend(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason required");
        Status = AffiliateStatus.Suspended;
        SuspendedAtUtc = DateTime.UtcNow;
        SuspensionReason = reason;
    }
}

public enum AffiliateStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2
}
```

### 1.2 AffiliateProductPolicy

Per-product commission configuration. The **tenant admin** controls commission rates — never the affiliate.

```csharp
/// <summary>
/// Aggregate root: Commission policy for a specific product within a tenant.
///
/// INVARIANTS:
/// - One active policy per (TenantId, ProductId) — enforced DB unique constraint
/// - CommissionRate: 0.0001 – 0.50 (0.01% – 50%) — enforced DB CHECK
/// - Only tenant admin can create/update — affiliate NEVER controls rate
/// - IsEnabled can be toggled; disabled policies produce zero commission
/// </summary>
public class AffiliateProductPolicy : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// FK → Products.Id. The product this policy applies to.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Commission rate as a decimal fraction.
    /// 0.10 = 10%.  Range: [0.0001, 0.50].
    /// </summary>
    public decimal CommissionRate { get; private set; }

    /// <summary>
    /// Whether this policy is active. Disabled = no commission earned.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// Optional: fixed cap on commission per order-item.
    /// Null = no cap.
    /// </summary>
    public decimal? MaxCommissionPerItem { get; private set; }

    // --- Factory ---
    public static AffiliateProductPolicy Create(
        Guid tenantId, Guid productId, decimal commissionRate, decimal? maxPerItem = null)
    {
        ValidateRate(commissionRate);
        if (maxPerItem.HasValue && maxPerItem.Value <= 0)
            throw new ArgumentException("MaxCommissionPerItem must be positive");

        return new AffiliateProductPolicy
        {
            TenantId = tenantId,
            ProductId = productId,
            CommissionRate = commissionRate,
            MaxCommissionPerItem = maxPerItem,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateRate(decimal newRate, decimal? newMaxPerItem)
    {
        ValidateRate(newRate);
        CommissionRate = newRate;
        MaxCommissionPerItem = newMaxPerItem;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Enable()  { IsEnabled = true;  UpdatedAt = DateTime.UtcNow; }
    public void Disable() { IsEnabled = false; UpdatedAt = DateTime.UtcNow; }

    private static void ValidateRate(decimal rate)
    {
        if (rate < 0.0001m || rate > 0.50m)
            throw new ArgumentException("CommissionRate must be between 0.01% and 50%");
    }
}
```

### 1.3 AffiliateAttribution

Links an **Order** to an **Affiliate**. One attribution per order — ever.

```csharp
/// <summary>
/// Aggregate root: Immutable record that an order was attributed to an affiliate.
///
/// INVARIANTS:
/// - Exactly ONE attribution per (TenantId, OrderId) — enforced DB UNIQUE
/// - Immutable after creation (append-only)
/// - No attribution → No commission. This is the gateway entity.
/// - AffiliateAccount must be Active at attribution time
/// - AttributionSource is an audit field, not a business decision field
/// </summary>
public class AffiliateAttribution : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// FK → Orders.Id. The order being attributed.
    /// UNIQUE per tenant — an order can only be attributed once.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// FK → AffiliateAccounts.Id. The affiliate who gets credit.
    /// </summary>
    public Guid AffiliateAccountId { get; private set; }

    /// <summary>
    /// The referral code used at the time of attribution (denormalized snapshot).
    /// </summary>
    public string ReferralCode { get; private set; } = string.Empty;

    /// <summary>
    /// How the attribution was established.
    /// </summary>
    public AttributionSource Source { get; private set; }

    /// <summary>
    /// UTC timestamp of attribution.
    /// </summary>
    public DateTime AttributedAtUtc { get; private set; }

    // --- Factory ---
    public static AffiliateAttribution Create(
        Guid tenantId, Guid orderId, Guid affiliateAccountId,
        string referralCode, AttributionSource source)
    {
        if (orderId == Guid.Empty) throw new ArgumentException("OrderId required");
        if (affiliateAccountId == Guid.Empty) throw new ArgumentException("AffiliateAccountId required");

        return new AffiliateAttribution
        {
            TenantId = tenantId,
            OrderId = orderId,
            AffiliateAccountId = affiliateAccountId,
            ReferralCode = referralCode,
            Source = source,
            AttributedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }
}

public enum AttributionSource
{
    ReferralLink = 0,
    CouponCode = 1,
    ManualAdmin = 2
}
```

### 1.4 AffiliateCommissionLedger

Append-only ledger of commission entries at the **OrderItem (SKU) level**.

```csharp
/// <summary>
/// Aggregate root: Append-only commission ledger entry.
///
/// INVARIANTS:
/// - APPEND ONLY — no updates, no deletes (enforced by DB trigger)
/// - One Earned entry per (TenantId, OrderItemId) — enforced DB UNIQUE
/// - Reversal references original via OriginalEntryId
/// - CommissionAmount > 0 always (CHECK constraint)
/// - CommissionAmount = OrderItem.PaidAmount × Policy.CommissionRate
///   (capped by Policy.MaxCommissionPerItem if set)
/// - EntryType: Earned or Reversed
/// - BaseAmount = OrderItem.PaidAmount at time of calculation (snapshot)
/// </summary>
public class AffiliateCommissionLedger : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// FK → AffiliateAttributions.Id
    /// </summary>
    public Guid AttributionId { get; private set; }

    /// <summary>
    /// FK → AffiliateAccounts.Id (denormalized for fast queries)
    /// </summary>
    public Guid AffiliateAccountId { get; private set; }

    /// <summary>
    /// FK → Orders.Id (denormalized for fast queries)
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// FK → OrderItems.Id — commission is per-SKU.
    /// </summary>
    public Guid OrderItemId { get; private set; }

    /// <summary>
    /// The OrderItem.PaidAmount at the time of commission calculation.
    /// Snapshot — does not change if OrderItem changes.
    /// </summary>
    public decimal BaseAmount { get; private set; }

    /// <summary>
    /// The commission rate applied (snapshot from AffiliateProductPolicy).
    /// </summary>
    public decimal CommissionRate { get; private set; }

    /// <summary>
    /// The calculated commission: min(BaseAmount × Rate, MaxPerItem ?? ∞).
    /// Always > 0. CHECK constraint at DB level.
    /// </summary>
    public decimal CommissionAmount { get; private set; }

    /// <summary>
    /// Currency code (snapshot from Order).
    /// </summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>
    /// Earned or Reversed.
    /// </summary>
    public CommissionEntryType EntryType { get; private set; }

    /// <summary>
    /// For Reversed entries: the original Earned entry being reversed.
    /// Null for Earned entries.
    /// </summary>
    public Guid? OriginalEntryId { get; private set; }

    /// <summary>
    /// Idempotency key to prevent duplicate entries.
    /// Format: AFFILIATE_COMM:{EntryType}:{TenantId}:{OrderItemId}
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTime RecordedAtUtc { get; private set; }

    // --- Factory: Earned ---
    public static AffiliateCommissionLedger CreateEarned(
        Guid tenantId, Guid attributionId, Guid affiliateAccountId,
        Guid orderId, Guid orderItemId,
        decimal paidAmount, decimal commissionRate, decimal? maxPerItem,
        string currency)
    {
        if (paidAmount <= 0) throw new ArgumentException("PaidAmount must be positive");
        if (commissionRate <= 0) throw new ArgumentException("CommissionRate must be positive");

        var raw = paidAmount * commissionRate;
        var commission = maxPerItem.HasValue ? Math.Min(raw, maxPerItem.Value) : raw;
        commission = Math.Round(commission, 4, MidpointRounding.ToZero);

        if (commission <= 0) throw new InvalidOperationException("Calculated commission must be positive");

        return new AffiliateCommissionLedger
        {
            TenantId = tenantId,
            AttributionId = attributionId,
            AffiliateAccountId = affiliateAccountId,
            OrderId = orderId,
            OrderItemId = orderItemId,
            BaseAmount = paidAmount,
            CommissionRate = commissionRate,
            CommissionAmount = commission,
            Currency = currency,
            EntryType = CommissionEntryType.Earned,
            OriginalEntryId = null,
            IdempotencyKey = $"AFFILIATE_COMM:EARNED:{tenantId}:{orderItemId}",
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    // --- Factory: Reversed ---
    public static AffiliateCommissionLedger CreateReversal(
        Guid tenantId, Guid originalEntryId,
        Guid attributionId, Guid affiliateAccountId,
        Guid orderId, Guid orderItemId,
        decimal originalCommission, decimal originalRate,
        string currency)
    {
        return new AffiliateCommissionLedger
        {
            TenantId = tenantId,
            AttributionId = attributionId,
            AffiliateAccountId = affiliateAccountId,
            OrderId = orderId,
            OrderItemId = orderItemId,
            BaseAmount = originalCommission, // the amount being reversed
            CommissionRate = originalRate,
            CommissionAmount = originalCommission,
            Currency = currency,
            EntryType = CommissionEntryType.Reversed,
            OriginalEntryId = originalEntryId,
            IdempotencyKey = $"AFFILIATE_COMM:REVERSED:{tenantId}:{orderItemId}",
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }
}

public enum CommissionEntryType
{
    Earned = 0,
    Reversed = 1
}
```

---

## 2. PostgreSQL Schema

```sql
-- ============================================================================
-- AF-1: AFFILIATE SYSTEM — WRITE-SIDE SCHEMA
-- ============================================================================
-- All tables are tenant-scoped.
-- Append-only ledger enforced by trigger on AffiliateCommissionLedger.
-- ============================================================================

-- 2.1 AffiliateAccounts
CREATE TABLE IF NOT EXISTS "AffiliateAccounts" (
    "Id"                uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId"          uuid            NOT NULL,
    "UserId"            uuid            NOT NULL,
    "ReferralCode"      varchar(12)     NOT NULL,
    "Status"            integer         NOT NULL DEFAULT 0,     -- 0=Pending,1=Active,2=Suspended
    "DisplayName"       varchar(200)    NOT NULL DEFAULT '',
    "ActivatedAtUtc"    timestamptz,
    "SuspendedAtUtc"    timestamptz,
    "SuspensionReason"  varchar(500),
    "CreatedAt"         timestamptz     NOT NULL DEFAULT now(),
    "UpdatedAt"         timestamptz,
    "CreatedBy"         uuid,
    "UpdatedBy"         uuid,
    "IsDeleted"         boolean         NOT NULL DEFAULT false,

    -- FK: must be valid user and tenant
    CONSTRAINT "FK_AffiliateAccounts_Tenants"
        FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id"),
    CONSTRAINT "FK_AffiliateAccounts_Users"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id"),

    -- UNIQUE: one affiliate account per user per tenant
    CONSTRAINT "UQ_AffiliateAccounts_Tenant_User"
        UNIQUE ("TenantId", "UserId"),

    -- UNIQUE: referral code unique per tenant
    CONSTRAINT "UQ_AffiliateAccounts_Tenant_ReferralCode"
        UNIQUE ("TenantId", "ReferralCode"),

    -- CHECK: status is valid enum
    CONSTRAINT "CK_AffiliateAccounts_Status"
        CHECK ("Status" IN (0, 1, 2))
);

CREATE INDEX "IX_AffiliateAccounts_TenantId" ON "AffiliateAccounts"("TenantId");
CREATE INDEX "IX_AffiliateAccounts_ReferralCode" ON "AffiliateAccounts"("TenantId", "ReferralCode");


-- 2.2 AffiliateProductPolicies
CREATE TABLE IF NOT EXISTS "AffiliateProductPolicies" (
    "Id"                    uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId"              uuid            NOT NULL,
    "ProductId"             uuid            NOT NULL,
    "CommissionRate"        decimal(8,6)    NOT NULL,   -- e.g. 0.100000 = 10%
    "IsEnabled"             boolean         NOT NULL DEFAULT true,
    "MaxCommissionPerItem"  decimal(18,4),
    "CreatedAt"             timestamptz     NOT NULL DEFAULT now(),
    "UpdatedAt"             timestamptz,
    "CreatedBy"             uuid,
    "UpdatedBy"             uuid,
    "IsDeleted"             boolean         NOT NULL DEFAULT false,

    CONSTRAINT "FK_AffiliateProductPolicies_Tenants"
        FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id"),
    CONSTRAINT "FK_AffiliateProductPolicies_Products"
        FOREIGN KEY ("ProductId") REFERENCES "Products"("Id"),

    -- UNIQUE: one policy per product per tenant
    CONSTRAINT "UQ_AffiliateProductPolicies_Tenant_Product"
        UNIQUE ("TenantId", "ProductId"),

    -- CHECK: rate between 0.01% and 50%
    CONSTRAINT "CK_AffiliateProductPolicies_Rate"
        CHECK ("CommissionRate" >= 0.0001 AND "CommissionRate" <= 0.50),

    -- CHECK: max cap must be positive if set
    CONSTRAINT "CK_AffiliateProductPolicies_MaxCap"
        CHECK ("MaxCommissionPerItem" IS NULL OR "MaxCommissionPerItem" > 0)
);

CREATE INDEX "IX_AffiliateProductPolicies_TenantProduct"
    ON "AffiliateProductPolicies"("TenantId", "ProductId");


-- 2.3 AffiliateAttributions
CREATE TABLE IF NOT EXISTS "AffiliateAttributions" (
    "Id"                    uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId"              uuid            NOT NULL,
    "OrderId"               uuid            NOT NULL,
    "AffiliateAccountId"    uuid            NOT NULL,
    "ReferralCode"          varchar(12)     NOT NULL,
    "Source"                integer         NOT NULL DEFAULT 0,  -- 0=ReferralLink,1=Coupon,2=Manual
    "AttributedAtUtc"       timestamptz     NOT NULL DEFAULT now(),
    "CreatedAt"             timestamptz     NOT NULL DEFAULT now(),
    "UpdatedAt"             timestamptz,
    "CreatedBy"             uuid,
    "UpdatedBy"             uuid,
    "IsDeleted"             boolean         NOT NULL DEFAULT false,

    CONSTRAINT "FK_AffiliateAttributions_Tenants"
        FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id"),
    CONSTRAINT "FK_AffiliateAttributions_Orders"
        FOREIGN KEY ("OrderId") REFERENCES "Orders"("Id"),
    CONSTRAINT "FK_AffiliateAttributions_AffiliateAccounts"
        FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts"("Id"),

    -- CRITICAL: one attribution per order — ever
    CONSTRAINT "UQ_AffiliateAttributions_Tenant_Order"
        UNIQUE ("TenantId", "OrderId"),

    CONSTRAINT "CK_AffiliateAttributions_Source"
        CHECK ("Source" IN (0, 1, 2))
);

CREATE INDEX "IX_AffiliateAttributions_AffiliateAccountId"
    ON "AffiliateAttributions"("AffiliateAccountId");
CREATE INDEX "IX_AffiliateAttributions_TenantOrder"
    ON "AffiliateAttributions"("TenantId", "OrderId");


-- 2.4 AffiliateCommissionLedger (APPEND-ONLY)
CREATE TABLE IF NOT EXISTS "AffiliateCommissionLedger" (
    "Id"                    uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId"              uuid            NOT NULL,
    "AttributionId"         uuid            NOT NULL,
    "AffiliateAccountId"    uuid            NOT NULL,
    "OrderId"               uuid            NOT NULL,
    "OrderItemId"           uuid            NOT NULL,
    "BaseAmount"            decimal(18,4)   NOT NULL,
    "CommissionRate"        decimal(8,6)    NOT NULL,
    "CommissionAmount"      decimal(18,4)   NOT NULL,
    "Currency"              varchar(3)      NOT NULL DEFAULT 'USD',
    "EntryType"             integer         NOT NULL,           -- 0=Earned, 1=Reversed
    "OriginalEntryId"       uuid,
    "IdempotencyKey"        varchar(255)    NOT NULL,
    "RecordedAtUtc"         timestamptz     NOT NULL DEFAULT now(),
    "CreatedAt"             timestamptz     NOT NULL DEFAULT now(),
    "UpdatedAt"             timestamptz,
    "CreatedBy"             uuid,
    "UpdatedBy"             uuid,
    "IsDeleted"             boolean         NOT NULL DEFAULT false,

    CONSTRAINT "FK_AffiliateCommLedger_Tenants"
        FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id"),
    CONSTRAINT "FK_AffiliateCommLedger_Attributions"
        FOREIGN KEY ("AttributionId") REFERENCES "AffiliateAttributions"("Id"),
    CONSTRAINT "FK_AffiliateCommLedger_AffiliateAccounts"
        FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts"("Id"),
    CONSTRAINT "FK_AffiliateCommLedger_Orders"
        FOREIGN KEY ("OrderId") REFERENCES "Orders"("Id"),
    CONSTRAINT "FK_AffiliateCommLedger_OrderItems"
        FOREIGN KEY ("OrderItemId") REFERENCES "OrderItems"("Id"),
    CONSTRAINT "FK_AffiliateCommLedger_OriginalEntry"
        FOREIGN KEY ("OriginalEntryId") REFERENCES "AffiliateCommissionLedger"("Id"),

    -- Idempotency: unique per tenant
    CONSTRAINT "UQ_AffiliateCommLedger_Idempotency"
        UNIQUE ("TenantId", "IdempotencyKey"),

    -- One Earned entry per OrderItem per tenant
    CONSTRAINT "UQ_AffiliateCommLedger_Earned_OrderItem"
        UNIQUE ("TenantId", "OrderItemId") WHERE ("EntryType" = 0),

    -- Commission must be positive
    CONSTRAINT "CK_AffiliateCommLedger_Amount"
        CHECK ("CommissionAmount" > 0),

    -- BaseAmount must be positive for Earned entries
    CONSTRAINT "CK_AffiliateCommLedger_BaseAmount"
        CHECK ("BaseAmount" > 0),

    -- Rate must be valid
    CONSTRAINT "CK_AffiliateCommLedger_Rate"
        CHECK ("CommissionRate" > 0 AND "CommissionRate" <= 0.50),

    -- Reversal must reference original
    CONSTRAINT "CK_AffiliateCommLedger_ReversalRef"
        CHECK (("EntryType" = 1 AND "OriginalEntryId" IS NOT NULL)
            OR ("EntryType" = 0 AND "OriginalEntryId" IS NULL)),

    CONSTRAINT "CK_AffiliateCommLedger_EntryType"
        CHECK ("EntryType" IN (0, 1))
);

CREATE INDEX "IX_AffiliateCommLedger_AffiliateAccountId"
    ON "AffiliateCommissionLedger"("AffiliateAccountId");
CREATE INDEX "IX_AffiliateCommLedger_OrderId"
    ON "AffiliateCommissionLedger"("OrderId");
CREATE INDEX "IX_AffiliateCommLedger_TenantRecorded"
    ON "AffiliateCommissionLedger"("TenantId", "RecordedAtUtc" DESC);


-- 2.5 APPEND-ONLY TRIGGER (blocks UPDATE and DELETE on ledger)
CREATE OR REPLACE FUNCTION fn_affiliate_commission_ledger_immutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'AffiliateCommissionLedger is append-only. UPDATE and DELETE are forbidden.';
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_affiliate_commission_ledger_immutable
    ON "AffiliateCommissionLedger";

CREATE TRIGGER trg_affiliate_commission_ledger_immutable
    BEFORE UPDATE OR DELETE ON "AffiliateCommissionLedger"
    FOR EACH ROW
    EXECUTE FUNCTION fn_affiliate_commission_ledger_immutable();
```

---

## 3. Domain Services

### 3.1 IAffiliateService

```csharp
namespace PointOfSale.Core.Interfaces.Affiliate;

public interface IAffiliateService
{
    // ─── Registration ───────────────────────────────────────────────
    /// <summary>
    /// Registers a new affiliate account for a user.
    ///
    /// Validation:
    /// - User must exist and belong to tenant
    /// - User must NOT already have an AffiliateAccount in this tenant
    /// - ReferralCode must be unique within tenant
    ///
    /// Failure cases:
    /// - DuplicateAffiliateException: user already registered
    /// - DuplicateReferralCodeException: code collision
    /// - UserNotFoundException: invalid user
    ///
    /// Emits: (none — account starts as Pending; activation is separate)
    /// </summary>
    Task<AffiliateAccount> RegisterAffiliateAccountAsync(
        Guid userId, string referralCode, string displayName,
        CancellationToken ct = default);

    /// <summary>
    /// Activates a pending or suspended affiliate account.
    ///
    /// Validation:
    /// - Account must exist
    /// - Status must be Pending or Suspended
    ///
    /// Failure cases:
    /// - AffiliateNotFoundException
    /// - Already active (idempotent — returns success)
    /// </summary>
    Task ActivateAffiliateAsync(Guid affiliateAccountId, CancellationToken ct = default);

    /// <summary>
    /// Suspends an active affiliate account.
    ///
    /// Validation:
    /// - Account must exist
    /// - Reason is required
    ///
    /// Failure cases:
    /// - AffiliateNotFoundException
    /// - Already suspended (idempotent — returns success)
    /// </summary>
    Task SuspendAffiliateAsync(
        Guid affiliateAccountId, string reason, CancellationToken ct = default);

    // ─── Product Policy ─────────────────────────────────────────────
    /// <summary>
    /// Enables affiliate commission for a product.
    ///
    /// Validation:
    /// - Product must exist and belong to tenant
    /// - CommissionRate: [0.0001, 0.50]
    /// - If policy already exists: updates rate (upsert semantics)
    ///
    /// Failure cases:
    /// - ProductNotFoundException
    /// - InvalidCommissionRateException
    /// </summary>
    Task<AffiliateProductPolicy> EnableAffiliateForProductAsync(
        Guid productId, decimal commissionRate, decimal? maxPerItem = null,
        CancellationToken ct = default);

    // ─── Attribution ────────────────────────────────────────────────
    /// <summary>
    /// Attributes an order to an affiliate.
    ///
    /// Validation:
    /// - Order must exist and belong to tenant
    /// - Order must NOT already be attributed (UNIQUE constraint)
    /// - AffiliateAccount must exist and Status == Active
    /// - ReferralCode must match the AffiliateAccount
    ///
    /// Failure cases:
    /// - OrderAlreadyAttributedException (idempotent if same affiliate)
    /// - AffiliateNotActiveException
    /// - OrderNotFoundException
    ///
    /// Emits: AffiliateAttributedEvent
    /// </summary>
    Task<AffiliateAttribution> AttributeOrderToAffiliateAsync(
        Guid orderId, string referralCode, AttributionSource source,
        CancellationToken ct = default);

    // ─── Commission Recording ───────────────────────────────────────
    /// <summary>
    /// Records commission for all eligible items in an attributed order.
    /// Called when OrderItem.PaidAmount becomes positive (payment captured).
    ///
    /// Validation:
    /// - Order must have an attribution
    /// - Each OrderItem must have PaidAmount > 0
    /// - Product must have an enabled AffiliateProductPolicy
    /// - Commission entry must not already exist for this OrderItem (idempotent)
    /// - AffiliateAccount must be Active
    ///
    /// Calculation: commission = min(PaidAmount × Rate, MaxPerItem)
    ///
    /// Failure cases:
    /// - NoAttributionException: order not attributed → skip silently
    /// - NoPolicyException: product has no affiliate policy → skip item
    /// - AlreadyRecordedException: idempotent → skip item
    ///
    /// Emits: AffiliateCommissionEarnedEvent (per order, aggregated)
    /// </summary>
    Task RecordCommissionForOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Reverses commission for refunded order items.
    /// Called when OrderItem.RefundedAmount changes.
    ///
    /// Validation:
    /// - Original Earned entry must exist for the OrderItem
    /// - Reversal must not already exist (idempotent)
    ///
    /// Emits: AffiliateCommissionReversedEvent
    /// </summary>
    Task ReverseCommissionForOrderItemAsync(
        Guid orderId, Guid orderItemId, CancellationToken ct = default);
}
```

---

## 4. Event Contracts

All events extend `IntegrationEvent` (inheriting `EventId`, `TenantId`, `OccurredAtUtc`, `IdempotencyKey`, `CorrelationId`).

```csharp
namespace PointOfSale.Core.Events.Affiliate;

/// <summary>
/// Emitted when an order is attributed to an affiliate.
/// Downstream: MongoDB projection for affiliate dashboard.
/// </summary>
public class AffiliateAttributedEvent : IntegrationEvent
{
    public Guid AttributionId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public AttributionSource Source { get; set; }
    public DateTime AttributedAtUtc { get; set; }
}

/// <summary>
/// Emitted when commission is earned for an order (aggregated across items).
/// Downstream: MongoDB commission summary, affiliate notifications.
/// </summary>
public class AffiliateCommissionEarnedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public decimal TotalCommission { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public List<CommissionLineItem> Items { get; set; } = new();
}

public class CommissionLineItem
{
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal CommissionRate { get; set; }
    public decimal CommissionAmount { get; set; }
}

/// <summary>
/// Emitted when commission is reversed (refund).
/// Downstream: MongoDB projection update, affiliate notifications.
/// </summary>
public class AffiliateCommissionReversedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid OrderItemId { get; set; }
    public Guid AffiliateAccountId { get; set; }
    public Guid OriginalLedgerEntryId { get; set; }
    public Guid ReversalLedgerEntryId { get; set; }
    public decimal ReversedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
```

---

## 5. Idempotency Strategy

### 5.1 Idempotency Keys (DB-enforced)

| Operation | Key Format | Unique Constraint |
|-----------|-----------|-------------------|
| Commission Earned | `AFFILIATE_COMM:EARNED:{TenantId}:{OrderItemId}` | `UQ_AffiliateCommLedger_Idempotency` |
| Commission Reversed | `AFFILIATE_COMM:REVERSED:{TenantId}:{OrderItemId}` | `UQ_AffiliateCommLedger_Idempotency` |
| Attribution | N/A — `UQ_AffiliateAttributions_Tenant_Order` prevents duplicates | UNIQUE on `(TenantId, OrderId)` |

### 5.2 Duplicate Event Handling

**OrderPlaced / OrderPaymentCaptured (duplicate):**
1. `AttributeOrderToAffiliateAsync` is called → checks `UQ_AffiliateAttributions_Tenant_Order`. If row exists with same affiliate → return success (idempotent). If different affiliate → reject.
2. `RecordCommissionForOrderAsync` iterates order items → for each item, attempts INSERT. The `UQ_AffiliateCommLedger_Idempotency` catches duplicates → skip with log, no error.

**OrderRefunded (duplicate):**
1. `ReverseCommissionForOrderItemAsync` → attempts INSERT with reversal idempotency key. Unique constraint catches duplicate → skip.

### 5.3 Implementation Pattern

```csharp
// In RecordCommissionForOrderAsync — per OrderItem:
try
{
    var entry = AffiliateCommissionLedger.CreateEarned(...);
    _context.AffiliateCommissionLedger.Add(entry);
    await _context.SaveChangesAsync(ct);
}
catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
    && pg.SqlState == "23505" /* unique_violation */)
{
    _logger.LogInformation(
        "Commission already recorded for OrderItem {OrderItemId} — idempotent skip",
        orderItemId);
    // Not an error. Move to next item.
}
```

### 5.4 Partial-Failure Safety

`RecordCommissionForOrderAsync` processes items **individually** within a loop. Each item INSERT is its own save. If item 3 of 5 fails (non-idempotency reasons), items 1-2 are committed. Retry of the entire order will skip 1-2 (idempotent) and retry 3-5.

---

## 6. Kill List

### Explicitly Forbidden Patterns

| # | Pattern | Reason |
|---|---------|--------|
| 1 | **Affiliate sets own commission rate** | Commission rate is admin-only, enforced at domain + DB CHECK |
| 2 | **Commission without attribution** | `AffiliateCommissionLedger.AttributionId` is NOT NULL FK |
| 3 | **Commission without positive PaidAmount** | Domain factory throws if `paidAmount <= 0`; DB CHECK on BaseAmount |
| 4 | **Multiple attributions per order** | `UQ_AffiliateAttributions_Tenant_Order` — hard unique constraint |
| 5 | **UPDATE/DELETE on commission ledger** | DB trigger blocks both operations |
| 6 | **Soft-delete on commission ledger** | `IsDeleted` is kept for EF compatibility but trigger blocks any UPDATE |
| 7 | **Commission on order total** | Commission is per-OrderItem.PaidAmount — never on Order.Total |
| 8 | **Frontend-only validation** | All rules enforced at Domain (factory), Service (guard), and DB (CHECK/UNIQUE) |
| 9 | **Direct SQL writes to commission ledger** | Trigger blocks, but code reviews must also enforce service-only writes |
| 10 | **Commission on unpaid items** | `RecordCommission` skips items where `PaidAmount <= 0` |
| 11 | **Commission for suspended affiliates** | Domain service checks `AffiliateAccount.IsActive` before recording |
| 12 | **Mutable referral codes** | `ReferralCode` has private setter, no update method exists |
| 13 | **Commission rate > 50%** | DB CHECK + domain validation: `[0.0001, 0.50]` |
| 14 | **Negative commission amounts** | DB CHECK: `CommissionAmount > 0` |
| 15 | **Cross-tenant data access** | All tables have `TenantId`; EF global query filters enforce isolation |
| 16 | **Commission without an order** | FK constraint `OrderId → Orders.Id` |
| 17 | **Reversal without original entry** | DB CHECK: `EntryType=1 → OriginalEntryId IS NOT NULL` |
| 18 | **Decimal precision loss** | `decimal(18,4)` for money, `decimal(8,6)` for rates |
| 19 | **Payouts in this phase** | Out of scope — payout is a separate bounded context |
| 20 | **Background jobs in this phase** | Out of scope — event-driven only via MassTransit consumers |

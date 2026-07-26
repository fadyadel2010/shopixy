# PASS P3-AA Affiliate Attribution Authority Certification

Certification date: 2026-06-09

Status: Production-certifiable for the inspected runtime paths.

## Authority Decision

PostgreSQL order state and the canonical `AffiliateAttributions` row own affiliate
conversion attribution. Referral URLs initiate candidate evidence. The server validates
that evidence against current SQL affiliate and promotion-link ownership, then issues a
tenant-bound, expiring, Data Protection protected proof. Cookies, sessions, cart fields,
checkout DTOs, events, MongoDB, Redis, Elasticsearch, dashboards, and frontend state do
not own affiliate attribution.

The final durability boundary is the serializable checkout transaction:

1. Middleware validates the public referral identifier against current SQL identity.
2. Middleware issues protected first-click evidence bound to tenant, affiliate account,
   referral code, promotion link, and a 30-day expiration.
3. Checkout accepts affiliate ownership only from middleware-validated evidence.
4. Checkout reloads and locks current SQL affiliate and promotion-link authority.
5. The order attribution snapshot and canonical attribution row commit together.
6. One `(TenantId, OrderId)` can have only one canonical affiliate attribution.
7. Later replay, recovery, analytics, or admin activity cannot reassign ownership.

## Findings

### Affiliate identity

- Affiliate account identity and referral-code ownership are current SQL authority.
- Global affiliate identity does not remove store ownership from promotion links or
  order attribution.
- Duplicate referral-code matches fail closed.
- Inactive or deleted affiliates cannot receive new checkout attribution.

### Referral links

- Query, header, and route values are candidate click evidence only.
- Promotion links must belong to the checkout tenant.
- A protected proof preserves first-click ownership across navigation.
- A protected promotion-link proof cannot fall back to a different account or link.
- A valid public referral URL can be shared intentionally; it cannot directly write an
  order or attribution row.

### Cookie and session

- `AFF_ATTR` is HttpOnly, Secure, SameSite Lax, and contains protected server evidence,
  not a raw affiliate code.
- Evidence is bound to the tenant and expires after 30 days.
- Tampered, foreign-tenant, or expired evidence is rejected and removed.
- `AFF_SESSION` and analytics session identifiers remain derivative tracking state.
- Checkout DTO and cart metadata cannot replace the protected proof.

### Order attribution

- Final checkout revalidates active affiliate and promotion-link SQL rows under locking.
- The order snapshot and `AffiliateAttribution` are created in the same transaction.
- `ApplicationDbContext` rejects incomplete order snapshots, attribution rows that do
  not match their tenant-owned order snapshot, attribution mutation/deletion, and order
  attribution reassignment after creation.
- Recovery can rebuild a missing canonical row only from the existing order snapshot.

### Attribution window

- New affiliate conversion ownership requires unexpired protected evidence.
- Expired evidence cannot claim a checkout.
- Expiration does not delete or change an already committed historical conversion.

### Analytics and ROI

- Affiliate clicks, analytics sessions, UTM values, and attribution projections are
  behavioral evidence and labels only.
- They cannot create or reassign canonical affiliate conversion ownership.
- Purchase counts and net revenue are recomposed from P2-H SQL/ledger authority.
- Campaign cost currency is checked against current tenant currency.
- Analytics may group authoritative revenue by derivative labels, but cannot create
  revenue, commission, payout, or conversion ownership truth.

### Update authority

- No public API owns attribution correction or reassignment.
- Canonical attribution and order attribution fields are append-only after conversion.
- Conflicting duplicate writes fail closed; matching duplicate writes converge.

### Tenant isolation

- Protected evidence is tenant-bound.
- Promotion-link attribution requires current checkout-tenant ownership.
- Canonical attribution is unique and queried by tenant plus order.
- Consumer and recovery paths derive store tenant from the SQL order and reject event
  tenant disagreement.

### Concurrency and idempotency

- Serializable checkout and row locks close identity changes during finalization.
- The unique `(TenantId, OrderId)` constraint prevents multiple affiliate owners.
- Duplicate consumer and recovery execution validate the winning row against the order
  snapshot before treating the result as idempotent.

### Historical consistency

- Committed conversion ownership does not follow later affiliate status, referral-code,
  promotion-link, policy, analytics, or projection changes.
- Recovery and replay use the immutable order snapshot, not stale click/session/event
  payloads.
- Expired evidence cannot resurrect an uncommitted attribution.

### Adversarial review

- Cookie tampering: rejected cryptographically.
- Cross-tenant cookie replay: rejected by tenant binding.
- Session tampering: cannot create conversion ownership.
- DTO/cart tampering: ignored for affiliate ownership selection.
- Referral replacement after first click: protected first-click evidence wins.
- Stale/expired proof replay: rejected.
- Duplicate conversion replay: collapsed by SQL uniqueness and conflict validation.
- Foreign promotion link: rejected at checkout.
- Order reassignment: rejected by the DbContext authority guard.
- Analytics poisoning: cannot alter canonical ownership or authoritative revenue.

## Runtime Hardening

- Added `AffiliateAttributionEvidenceTokenService`.
- Replaced raw `AFF_ATTR` values with protected, tenant-bound, expiring evidence.
- Removed checkout ownership resolution from request DTO and cart metadata.
- Restamped attribution from final locked SQL authority inside the checkout transaction.
- Added append-only and order-snapshot consistency enforcement in
  `ApplicationDbContext`.

Exact runtime files modified for P3-AA:

- `PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs`
- `PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

Analytics/ROI authority hardening already present in the reviewed worktree was also
validated in:

- `PointOfSale.API/Controllers/AdminAnalyticsAttributionController.cs`
- `PointOfSale.API/Services/Analytics/AnalyticsEventValidator.cs`
- `PointOfSale.Infrastructure/Analytics/StorefrontAnalyticsAuthorityReader.cs`
- `PointOfSale.Infrastructure/Attribution/AttributionContextResolver.cs`
- `PointOfSale.Infrastructure/Attribution/OrderAttributionStore.cs`
- `PointOfSale.Infrastructure/CampaignCost/CampaignRoiService.cs`
- `PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/AuthoritativePurchaseAnalyticsConsumer.cs`

## Regression Freeze

- `AffiliateAttributionEvidenceTokenServiceTests`
- `AffiliateAttributionCertificationTests`
- `AffiliateAttributionRecoveryCertificationTests`
- Existing commission lifecycle and recovery certification suites remain authoritative.

Focused result: 10 passed, 0 failed.

API build: passed with existing repository warnings and no errors.

## Explicit Answers

- Who owns affiliate attribution authority? PostgreSQL order attribution snapshots plus
  the canonical `AffiliateAttributions` row.
- Can attribution be stolen? Not through later links, cookies, sessions, DTOs, carts,
  projections, replay, or tenant substitution on the inspected paths.
- Can cookies become authority? No. They carry protected candidate evidence only.
- Can multiple affiliates claim one order? No.
- Can attribution survive expiration? Uncommitted evidence cannot; committed historical
  attribution remains immutable.
- Can analytics become attribution authority? No.
- Can tenant leakage occur? The inspected paths fail closed on tenant mismatch.
- Can replay corrupt attribution? No; matching replay converges and conflicting replay
  fails.
- Can attribution be reassigned after conversion? No.
- Were runtime changes required? Yes.
- Is Affiliate Attribution Authority production-certifiable? Yes, for the inspected
  runtime paths.

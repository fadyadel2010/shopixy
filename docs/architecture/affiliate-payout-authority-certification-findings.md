# PASS P3-AC Affiliate Payout Authority Certification Findings

**Status:** CERTIFIED - production runtime authority  
**Date:** 2026-06-10  
**Scope:** payout requests, approval/rejection, execution, balances, withdrawals, settlements, transfer evidence, treasury/accounting, currency, tenant isolation, replay, and history

## Authority conclusion

Affiliate payout authority is owned by PostgreSQL payout requests, append-only commission and balance ledgers, settlement allocations, trusted transfer evidence, and the treasury/accounting postings committed by `AffiliateSettlementOrchestrator`. Payouts consume net approved commission after reversals. They never create earned value.

The portal, admin DTOs, routes, dashboards, projections, caches, analytics, and displayed balances are derivative only.

## Domain findings

### Payout identity

- Payout request identity is server-generated and tenant/affiliate bound.
- Withdrawal identity is the payout request or deterministic settlement reference.
- Global rail/reference evidence uniqueness and allocation idempotency prevent duplicate transfer lineage.
- Payout request identity, amount, tenant, affiliate, currency, request time, and wallet snapshot are centrally immutable after insertion.

### Balance

- Balance derives from net approved commission ledger rows after reversals, negative non-withdrawal payout reductions, executed withdrawal rows, and active request reservations.
- Positive adjustments and bonuses are excluded because payout authority may not manufacture earned value.
- UI clamping is display behavior only; request and execution guards use raw authoritative balance.
- Balance, transfer allocation, and evidence history cannot be edited or deleted through normal EF persistence.

### Payout request

- Creation is serializable, lock-protected, threshold checked, tenant scoped, currency scoped, and bounded by raw available balance.
- Approval/rejection uses locked lifecycle transitions.
- Execution requires an approved request, exact approved amount, unchanged payout-wallet snapshot, trusted transfer evidence, and current commission availability.
- A selected request is closed only by its own transfer; its execution no longer auto-reconciles unrelated open requests.

### Settlement

- Settlement builds an exact FIFO plan from approved commission rows net of reversals.
- Paid commission rows, withdrawal rows, transfer allocations, request completion, accounting posts, and events share the orchestrated transaction boundary.
- Over-allocation, changed commission authority, duplicate paid rows, or insufficient transfer evidence fail closed.
- Serializable isolation, unique constraints, deterministic references, and conflict checks prevent replay-driven settlement.

### Transfer evidence

- Normal execution accepts only evidence registered by the tenant-derived admin endpoint after HMAC-SHA256 verification.
- The signed canonical payload binds tenant, rail, normalized reference, amount, currency, and timestamp.
- Signature comparison is fixed-time; timestamps are freshness bounded; rail/reference is globally unique.
- Same-authority replay is idempotent. Changed tenant, amount, currency, or fingerprint is a replay conflict.
- Legacy evidence and manual break-glass evidence are not trusted normal evidence.
- Production startup rejects missing/short evidence secrets and enabled manual evidence override.

### Treasury and accounting

- Execution passes the outward-money readiness gate and resolves the authoritative treasury cashbox account.
- Earned affiliate accounting must already exist before payout accounting.
- Payout accounting posts affiliate payable debit and cash/treasury credit through financial posting authority.
- Payout execution cannot mark commission paid or close a request while bypassing these gates.

### Currency

- Commission, balance, request, withdrawal, allocation, and evidence streams must resolve to one ISO currency.
- That currency must equal the store base currency used by accounting.
- Mixed, blank, foreign, or malformed currency streams fail closed.

### Tenant isolation

- Portal affiliate identity comes from the authenticated principal.
- Admin payout and evidence routes derive tenant from `ITenantContext`; DTOs and route values cannot choose another tenant.
- Runtime queries re-check store tenant, affiliate account, payout request, commission rows, evidence, and allocations.
- Foreign evidence references are detected as tenant conflicts rather than treated as absent.

### Concurrency and idempotency

- Request reservation and payout execution use serializable transactions and authority locks.
- Withdrawal and paid-commission uniqueness prevent concurrent duplication.
- Transfer allocation idempotency binds evidence to settlement reference and source.
- Same replay converges or fails before mutation; conflicting replay fails closed.

### Historical consistency

- Commission, balance, transfer allocation, and accounting histories are append-only.
- Payout request lineage fields and wallet destination snapshots cannot drift.
- Reversals reduce future availability; stale requests cannot execute against changed wallet or insufficient current commission.
- Historical display is reconstructed from authoritative rows, not mutable dashboard totals.

### Adversarial review

- Duplicate execution: blocked by request status, deterministic identity, unique withdrawal/paid rows, and evidence allocation.
- Evidence replay/forgery: blocked by HMAC, freshness, tenant/currency binding, global uniqueness, and fingerprint conflict.
- Balance inflation: positive adjustments are not payoutable; DTO/display values are ignored.
- Settlement replay: exact-plan and unique-ledger checks block duplicate allocation.
- Treasury/accounting bypass: readiness and posting prerequisites fail closed inside execution.
- Currency manipulation: single-stream and store-base-currency checks block it.
- Tenant guessing/route tampering: tenant and affiliate authority are server-derived and revalidated.

## Runtime hardening performed

- Excluded positive balance adjustments and bonuses from payoutable balance.
- Bound payout currency to store accounting currency.
- Prevented a selected payout request from reconciling other requests.
- Required paid requests to carry an external reference and close their remaining amount.
- Added central persistence immutability guards for payout ledgers, allocations, evidence, and request identity fields.
- Added HMAC-verified external transfer evidence registration with freshness, replay, tenant, amount, and currency binding.
- Required normal execution to accept only `HMAC_RECONCILIATION` evidence.
- Added production startup validation for the evidence verifier secret and disabled break-glass flags.

## Exact runtime files modified

- `PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs`
- `PointOfSale.Core/Entities/Affiliate/AffiliatePayoutTransferEvidence.cs`
- `PointOfSale.Core/Interfaces/Affiliate/IAffiliatePayoutEvidenceService.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutEvidenceService.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOptions.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliatePayoutTransferEvidenceConfiguration.cs`
- `PointOfSale.API/Controllers/AffiliatePayoutEvidenceController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Infrastructure/Migrations/20260605120000_AffiliatePayoutWalletSnapshotAuthority.cs`
- `PointOfSale.Infrastructure/Migrations/20260610140000_AffiliatePayoutVerifiedEvidenceAuthority.cs`

## Regression freezes

- `PointOfSale.Tests/Architecture/AffiliatePayoutCertificationTests.cs`
- `PointOfSale.Tests/Architecture/AffiliatePayoutExecutionCertificationTests.cs`
- `PointOfSale.Tests/Architecture/AffiliatePayoutRecoveryCertificationTests.cs`
- `PointOfSale.Tests/Affiliate/AffiliatePayoutRequestWalletSnapshotTests.cs`
- `PointOfSale.Tests/Affiliate/AffiliateSettlementProductionGuardTests.cs`

Focused result: 26 passed, 0 failed.

## Explicit answers

- Who owns payout authority? PostgreSQL payout/ledger/settlement/evidence records coordinated by `AffiliateSettlementOrchestrator`, with commission, treasury, and accounting remaining their own authorities.
- Can balances diverge from ledger truth? Not through certified runtime paths; displays are derivative and positive manual credits are not payoutable.
- Can payouts exceed earned commissions? No.
- Can payouts replay? No duplicate value can be created; matching replay converges or fails before mutation, and conflicts fail closed.
- Can transfer evidence be forged? Not without the trusted HMAC secret; legacy/manual evidence is not accepted as normal verified authority.
- Can treasury be bypassed? No.
- Can accounting be bypassed? No.
- Can tenant leakage occur? Not through certified runtime paths.
- Can currency corruption occur? Not through certified runtime paths.
- Were runtime code changes required? Yes.
- Is Affiliate Payout Authority production-certifiable? Yes, after applying the migrations and configuring a production HMAC secret of at least 32 characters. Startup fails closed otherwise.

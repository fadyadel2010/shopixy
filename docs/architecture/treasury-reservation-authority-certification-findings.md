# PASS T4 Treasury Reservation Authority Certification

Date: 2026-06-12

Verdict: Treasury Reservation Authority is production-certifiable for inspected runtime paths after pending-payout release and reservation-boundary hardening.

## Reservation Findings

Treasury reservation authority is owned by PostgreSQL liability and payout/request rows plus the SQL earning, payout-item, payout-request, transfer-allocation, financial-transaction, and ledger truth that bounds them. CommerceHub payout reservations are `CommerceHubVendorEarnings.PayoutReservedAmount`, `Status=InPayout`, `PayoutId`, and `CommerceHubPayoutItems`. Affiliate payout reservations are active SQL `AffiliatePayoutRequests` in `Pending` or `Approved` status.

Reservations cannot be created from DTO totals, dashboards, projections, caches, analytics, route ids, or stale events. Runtime reservation creation requires SQL earning/request authority, serializable mutation, locks, idempotency, ledger confirmation, currency scope, and payout readiness.

## Escrow Findings

Escrow and settlement holds remain SQL-derived read and workflow state. CommerceHub holdbacks derive from delivery, refund-window, active-refund, payout-delay, ledger payable, and authority-conflict checks in SQL-backed payout readiness. Settlement code does not create earned value; it may only release ledger-confirmed eligible value.

## Refund Findings

Refund execution blocks externally processing or reconciliation-required payouts. Refund-triggered release of a pending payout now releases both reservation identity and reserved amount, preventing stale `PayoutReservedAmount` from suppressing future available earnings after the payout row is failed for refund authority.

## Payout Findings

Payout creation reserves only available SQL earnings after serializable locking, readiness gates, refund locks, payout-delay checks, currency validation, and ledger confirmation. Completed payouts consume existing reservations and ledger-confirmed payable. Failed payouts and refund-required pending-payout release return unused reservation amounts to availability.

## Release Findings

Reservation release authority is limited to payout workflow completion/failure and refund-controlled pending-payout failure. `PayoutReservedAmount` mutation is now rejected by `ApplicationDbContext` unless the CommerceHub payout boundary is active. Direct financial/refund/projection scopes cannot forge, inflate, suppress, or release reserved amounts.

## Replay Findings

Payout idempotency, row locks, external transfer intent keys, evidence uniqueness, completion fingerprints, payout-item lineage, and SQL check constraints prevent duplicate reservations and duplicate releases. Release replay is bounded by current SQL payout/earning state and clamps reservation amounts to zero without recreating value.

## Cross-System Findings

CommerceHub, Affiliate, Storefront, Service Marketplace, accounting, and treasury projections cannot create or release treasury reservations. CommerceHub and affiliate may reserve only through certified SQL payout workflows. Storefront and Service Marketplace have no inspected treasury reservation write authority.

## Isolation Findings

Reservation reads and writes are tenant/vendor scoped through trusted context or Super Admin platform authority. CommerceHub payout and earning queries require matching tenant/vendor ids. Affiliate request reservations are store-tenant and affiliate-account scoped. Foreign route ids cannot reserve or release local treasury value.

## Historical Findings

Reservation history is reconstructable from payout rows, payout items, earning reserved/paid amounts, payout status transitions, affiliate payout requests, transfer allocations, financial transactions, audit rows, and ledger postings. Released reservations do not resurrect from stale events because release paths reload current SQL payout/earning state.

## Adversarial Findings

- Reservation replay cannot duplicate held funds because payout creation locks SQL earnings and idempotency converges.
- Release replay cannot corrupt reservations because current SQL state gates release and amount subtraction is clamped.
- Escrow cannot release early because payout eligibility requires delivery/refund-window/refund-lock/payout-delay/ledger truth.
- Refund bypass cannot consume unreserved cash because refund execution remains M4 authority and blocks/release pending payout reservations through SQL.
- Payout bypass cannot consume unavailable earnings because reserved, paid, delayed, refund-held, or ledger-unconfirmed earnings fail closed.
- DTO tampering, route tampering, tenant guessing, projection poisoning, and stale analytics cannot create, inflate, suppress, or release reserved funds.

## Runtime Hardening Performed

- Fixed refund-triggered pending payout release to decrement `PayoutReservedAmount` in `CommerceHubRefundService`.
- Fixed refund-execution pending payout release to decrement `PayoutReservedAmount` in `RefundExecutionService`.
- Fixed provider-refund compensation pending payout release to decrement `PayoutReservedAmount` in `CommerceHubPaymentCompensationOutboxWorker`.
- Tightened `ApplicationDbContext` so `CommerceHubVendorEarnings.PayoutReservedAmount` can be changed only under `CommerceHubBoundaryMutationScope.BeginPayout()`.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs`
- `PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentCompensationOutboxWorker.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/TreasuryReservationAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/CommerceHub/CommerceHubDataAuthorityBoundaryTests.cs`

## Architecture Updates

- Added this T4 findings document.
- Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md` with the T4 authority rule.

## Required Answers

1. Who owns treasury reservation authority? PostgreSQL liability, payout, earning, payout-item, payout-request, transfer-allocation, financial-transaction, and ledger truth under certified payout/refund/settlement workflows.
2. Can reservations be forged? No on inspected runtime paths; DTOs, routes, projections, caches, analytics, and stale events are not reservation authority.
3. Can reserved cash be spent? No on inspected runtime paths; availability subtracts active reservations and payout execution consumes only SQL-reserved, ledger-confirmed earnings.
4. Can escrow release early? No; release and payout eligibility require SQL delivery, refund-window, active-refund, payout-delay, and ledger/payable truth.
5. Can replay corrupt reservations? No; idempotency, locks, payout-item lineage, evidence uniqueness, fingerprints, check constraints, and clamped release prevent duplicate reserve/release corruption.
6. Can tenant leakage occur? No inspected path can use foreign tenant/vendor/affiliate reservations to affect local cash.
7. Were runtime code changes required? Yes.
8. Is Treasury Reservation Authority production-certifiable? Yes, for inspected runtime authority paths.

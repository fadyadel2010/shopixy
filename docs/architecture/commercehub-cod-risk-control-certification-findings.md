# PASS 16.5C COD Risk & Debt Control Certification

Date: 2026-06-02

Scope: PASS 16.5C

Verdict: CommerceHub COD risk-control runtime is production-certifiable for inspected code paths. Public launch remains not ready until inherited PASS 15.6 and PASS 16.1B deployment, alerting, backup/restore, SMTP/session, external retention, and PostgreSQL-backed chaos-proof blockers are closed.

## Risk Inventory

| Entity or surface | Authority owner | Source of truth | Derivative consumers |
|---|---|---|---|
| `CommerceHubFinancialTransaction` | CommerceHub finance ledger | PostgreSQL append-only financial transaction rows | Vendor debt snapshots, wallets, dashboards, reconciliation |
| `CommerceHubVendorDebtSnapshot` | `CommerceHubVendorDebtService` | Runtime SQL recomposition from ledger, settlement, refund, and adjustment facts | Checkout, discovery suppression, dashboards, Super Admin |
| `CommerceHubVendorSettlement` | Super Admin financial control | PostgreSQL settlement evidence with vendor scope, idempotency, external reference, actor, notes | Debt snapshot recent settlements, dashboards, audit |
| `CommerceHubVendorPaymentRequest` / `CommerceHubVendorPaymentProof` | Vendor repayment workflow plus Super Admin approval | PostgreSQL request state, proof hash, review state, ledger correlation/idempotency | Merchant debt UI, Super Admin approval queue |
| `CommerceHubVendorPaymentAuditLog` | Vendor payment workflow | Append-only SQL audit evidence | Incident reconstruction |
| `CommerceHubFinancialOperationAudit` / `CommerceHubFinancialAuditLog` | Finance services | Append-only SQL audit evidence | Forensics, dashboards, reconciliation |
| `CommerceHubMarketplaceOrder` / `CommerceHubSubOrder` | Checkout and order lifecycle | PostgreSQL order/sub-order snapshots, payment method, commission, status | Existing-order fulfillment, settlement, dashboards |
| Vendor suppression visibility | Governance plus financial control | SQL user governance profiles plus SQL debt snapshots | Search, autocomplete, homepage, product detail, recommendations |

## Debt Accumulation Findings

Vendor debt is calculated by `CommerceHubVendorDebtService` from SQL financial facts. Direct vendor commission rows increase COD receivable debt when debited to the vendor. Legacy platform commission rows are included only when no direct vendor commission exists for the same vendor/reference key. Post-payout refund impacts and financial-control adjustments participate through the same SQL transaction stream.

Debt can drift only if a future path bypasses SQL ledger/idempotency or posts incompatible legacy/direct commission facts. The inspected service de-duplicates legacy fallback commission by `(VendorId, ReferenceType, ReferenceId)`, orders events deterministically by timestamp and id, rounds to currency scale, and clamps displayed debt to zero.

## Debt Reduction Findings

Debt reduction happens through settlement payments, approved debt clearances, approved vendor payment requests, and reviewed credit adjustments. Settlement and clearance paths require positive amount, vendor scope, currency, idempotency key, unique external reference, notes, actor evidence, mirrored ledger posting, serializable state capture, and post-write delta verification.

Double reduction is guarded by idempotency replay matching and external-reference uniqueness. Negative displayed debt is clamped to zero; overpayment settlement and clearance paths fail closed when the submitted amount exceeds current debt.

## Threshold Findings

The hard threshold source is `CommerceHubVendorDebtOptions.DefaultSuspensionThresholdAmount = 1000m`, configurable through `CommerceHubVendorDebtOptions.SuspensionThresholdAmount` with fallback to the default when invalid. The authoritative evaluation is in `CommerceHubVendorDebtService`: `var isSuspended = amountOwed >= threshold;`.

Runtime hardening in PASS 16.5C aligned `ThresholdCrossedAtUtc` with the hard limit by treating `effectiveDebt >= threshold` as the crossing condition. Exact-threshold debt now has suspension and crossing evidence instead of being suspended without a crossing timestamp.

## Suspension Findings

Suspension is computed from current SQL debt snapshots, not persisted as an independent shadow flag. `CommerceHubVendorFinancialControlService.GetVendorsSuspendedFromNewOrdersAsync` returns vendors whose snapshots are `IsSuspendedFromNewOrders`.

This avoids suspension drift from stale stored flags. The tradeoff is that every enforcement path must call the control service before accepting orders or returning public visibility.

## Reactivation Findings

Reactivation is automatic after current SQL debt drops below the hard threshold. There is no separate manual reactivation flag that can resurrect unsafe ordering. `MinimumPaymentToRestoreOrdering` requires reducing debt below the threshold, not merely to the threshold.

Premature reactivation would require a future debt-reduction path to undercount debt or bypass ledger validation. No such inspected path was validated.

## Checkout Findings

`CommerceHubCheckoutService` loads current listing, product, variant, tenant, payment, shipping, promo, price, inventory, governance, and financial-readiness authority inside serializable order creation. It then calls `GetVendorsSuspendedFromNewOrdersAsync` and rejects with `VENDOR_SUSPENDED_FOR_SETTLEMENT` before order creation if any vendor is suspended.

No inspected checkout bypass was found. A vendor already at or above 1000 EGP COD debt cannot receive new CommerceHub orders through the certified checkout path.

## Discovery Findings

CommerceHub public discovery suppresses financially unsafe vendors through `CommerceHubPublicVendorVisibilityService` and `CommerceHubQueryService`. Search, browse, autocomplete, suggestions, popular query revalidation, zero-result suggestions, product details, homepage sections, and recommendation-like surfaces recompose current SQL listing/product/category authority and filter vendor governance plus COD-debt suspension.

Derivative Elasticsearch, MongoDB, Redis cache, homepage JSON, popular-search analytics, and frontend state are candidate inputs only. They cannot restore public visibility after current SQL debt marks the vendor suspended.

## Existing-Order Findings

Suspension blocks new order acceptance and public vendor/listing visibility. It does not cancel, orphan, or rewrite existing orders. Existing orders continue through PASS 16.5A SQL order/fulfillment lifecycle, return/refund policy, inventory side effects, and PASS 16.5B settlement/refund accounting.

## Concurrency Findings

Checkout uses per-customer and idempotency locks plus serializable SQL order creation. Debt settlement and clearance use serializable financial state capture and ledger delta verification. This narrows concurrent checkout/settlement gaps on inspected paths.

Remaining debt: there is no full PostgreSQL-backed chaos harness proving simultaneous checkout, COD delivery/debt creation, vendor repayment, refund reversal, replay, and recovery races under production isolation.

## Recovery Findings

Recovery and replay are SQL-first. Ledger idempotency and financial fingerprints collapse duplicate facts or fail closed on divergent replay. Discovery recovery, reindex, and auto-healing remain derivative convergence operations and must recompose current SQL visibility before writing MongoDB or Elasticsearch.

Visibility resurrection was not validated on inspected paths because query-time filtering and SQL overlay checks run after derivative reads.

## Dashboard Findings

Merchant and Super Admin debt dashboards derive from `CommerceHubFinancialDashboardService`, wallet ledger queries, vendor debt snapshots, settlement rows, vendor payment requests, and reconciliation read models. Dashboard counters are not authority.

Dashboard drift can occur only if a future UI/API reads shadow balances or derivative counters as truth. Current inspected dashboard paths are SQL-ledger-derived and mixed-currency-aware.

## Auditability Findings

Debt incidents can be reconstructed from financial transactions, settlement evidence, vendor payment request/proof/audit logs, financial operation audits, reconciliation reports, order/sub-order snapshots, and append-only evidence controls. Actor, reason, external reference, idempotency, correlation id, before/after state, and reference identifiers are available on inspected financial-control paths.

External WORM/SIEM retention and deployment-proven evidence roles remain inherited launch blockers.

## Chaos Findings

The certification actively checked threshold bypass, suspension bypass, checkout bypass, search bypass, recommendation bypass, negative debt display, double settlement, double debt reduction, concurrent threshold crossing posture, visibility resurrection, debt resurrection, and dashboard mismatch.

No active runtime authority inversion was validated after the exact-threshold crossing hardening. The remaining chaos gap is execution proof, not source-level authority: production-scale PostgreSQL race harnesses are still required.

## Monitoring Findings

Existing telemetry and evidence include finance dashboards, reconciliation reports, financial audit logs, readiness-gate failures, settlement/debt audit rows, and Super Admin finance views. Dedicated production alert packs for debt-threshold crossing, suspended-vendor checkout attempts, visibility suppression drift, settlement anomalies, repayment approval failures, dashboard parity, and recovery resurrection are still required before public launch approval.

## Runtime Hardening Performed

- `CommerceHubVendorDebtService` now records threshold crossing when effective debt reaches the hard limit (`effectiveDebt >= threshold`), matching the `amountOwed >= threshold` suspension rule.
- Debt below the hard limit is the only state that clears `ThresholdCrossedAtUtc`.

## Regression Freeze Added

- `CommerceHubCodRiskCertificationTests`
- `CommerceHubVendorDebtCertificationTests`
- `CommerceHubVendorSuspensionCertificationTests`

Frozen behavior: vendor debt authority, debt accumulation, debt reduction, threshold enforcement, vendor suspension, vendor reactivation, checkout enforcement, discovery enforcement, recovery behavior, dashboard authority, auditability, chaos posture, and monitoring debt.

## Architecture Updates

This finding file, `.ai-context` PASS 16.5C entries, and the PASS 16.5C regression tests are the current authority record for COD risk-control certification.

## Remaining Debt

- PASS 15.6 public-launch blockers remain: deployment-applied evidence roles, external WORM/SIEM retention, outage-surviving audit handoff, verified alert packs and dashboards, backup/restore, and operator rehearsal evidence.
- PASS 16.1B identity/session deployment blockers remain.
- Production alert packs for COD debt threshold, suspended checkout attempts, visibility suppression, repayment approvals, dashboard drift, and recovery resurrection remain required.
- PostgreSQL-backed chaos tests for concurrent checkout, COD debt creation, threshold crossing, repayment, settlement, refund, replay, and recovery remain required.
- Production query-plan/capacity review for debt dashboards and global suspended-vendor discovery filtering remains required.

## Required Answers

1. How is vendor debt calculated? It is recomposed by `CommerceHubVendorDebtService` from SQL financial transaction rows, settlement/payment evidence, approved adjustments, approved payment requests, and eligible refund impacts.
2. Can vendor debt drift? No drift was validated on inspected paths; drift remains possible only if future code bypasses SQL ledger/idempotency or creates shadow balances.
3. Is the 1000 EGP threshold authoritative? Yes. `amountOwed >= 1000 EGP` is hard authority for new-order suspension and public vendor/listing suppression.
4. Where is the threshold enforced? It is evaluated in `CommerceHubVendorDebtService`, exposed through `CommerceHubVendorFinancialControlService`, enforced in checkout by `CommerceHubCheckoutService`, and applied to discovery by `CommerceHubPublicVendorVisibilityService` and `CommerceHubQueryService`.
5. Can suspended vendors receive new orders? Not through inspected CommerceHub checkout paths; checkout rejects with `VENDOR_SUSPENDED_FOR_SETTLEMENT`.
6. Can suspended vendors remain discoverable? Not through inspected public discovery paths; search, suggestions, autocomplete, homepage, product detail, and recommendation-like surfaces filter financially suspended vendors.
7. Can existing orders continue safely? Yes. Suspension blocks new acceptance and visibility but preserves existing order fulfillment, returns, refunds, and settlement under SQL lifecycle authority.
8. Can vendors reactivate incorrectly? No inspected premature reactivation path was found; reactivation requires current SQL debt below the hard threshold.
9. Can the threshold be bypassed? No inspected threshold bypass was found after exact-threshold crossing hardening. Future bypass risk is guarded by the PASS 16.5C freezes.
10. Is CommerceHub COD risk control production-ready? Runtime COD risk control is production-certifiable for inspected paths, but CommerceHub public launch remains not ready until inherited deployment, monitoring, retention, backup/restore, session, and PostgreSQL chaos blockers are closed.

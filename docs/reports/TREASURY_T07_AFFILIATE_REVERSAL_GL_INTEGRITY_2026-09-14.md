# Treasury T-07 Affiliate Reversal GL Integrity - 2026-09-14

Certification date: 2026-09-14

Primary evidence tenant: `Shopixy` (`83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`)

Control tenants: `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`) and `greatone` (`c43aebf2-4cc4-406a-a2db-4df06d40bce5`)

Final verdict: **PASS**

## 1. Executive Verdict

**PASS.** T-07 closes the forward-looking General Ledger gap for affiliate commission reversals. A registered `AffiliateCommissionReversedAccountingConsumer` now revalidates each tenant-owned immutable reversal row and posts the required non-cash compensating journal through `FinancialPostingService`: Dr `2105 Affiliate Payable` and Cr `6110 Affiliate Marketing Expense`.

The implementation is tenant-scoped, source-bound, idempotent by reversal-ledger identity, retry-safe, and protected against changed payloads, out-of-order earned accounting, cumulative over-reversal, cross-tenant reads, and negative affiliate payable. Eleven focused scenarios and 140 related regression tests pass. API Release and Web production builds also pass.

The historical Shopixy discrepancy remains unchanged by design: EGP 120 is recorded as reversed in the affiliate subledger while the pre-existing GL still carries EGP 120 in `2105` and EGP 120 in `6110`. No historical replay, repair journal, ledger edit, outbox fabrication, payout, refund, or other financial mutation was performed.

## 2. Scope And Decision Rule

This certification covers the affiliate reversal source, event/outbox boundary, accounting-consumer registration, source validation, GL shape, replay behavior, ordering, full and partial reversal, approval and payout interaction, paid-then-refunded containment, reports, cash isolation, tenant isolation, and build/test gates.

PASS required all of the following:

1. A future valid `AffiliateCommissionReversedEvent` must have a registered accounting consumer.
2. The consumer must derive authority from the persisted tenant-owned reversal and original earned rows, not trust mutable event amounts alone.
3. Each reversal row must authorize exactly one Dr `2105` / Cr `6110` journal for the exact reversed commission.
4. Duplicate delivery must have one GL effect; changed payload reuse must fail.
5. Reversal must not move Cashbox, Bank, or clearing accounts.
6. Partial reversal must reduce GL expense, GL payable, and payout eligibility to the same net amount.
7. Missing earned accounting, over-reversal, cross-tenant access, and insufficient payable must fail without a partial reversal journal.
8. The existing Shopixy EGP 120 discrepancy must not be rewritten or silently repaired.

This is a T-07 certification, not a repository-wide Treasury production-readiness waiver.

## 3. Safety And Evidence Controls

| Control | Result |
| --- | --- |
| Historical financial mutation | None. |
| Direct SQL business write | None. |
| Manual journal or ledger line | None. |
| Balance-cache patch | None. |
| Outbox fabrication or replay | None. |
| SQL inspection | PostgreSQL `REPEATABLE READ` with `SET TRANSACTION READ ONLY`, followed by rollback. |
| Tenant boundary | Business, journal, ledger, payout, and outbox checks were tenant-scoped. |
| Browser mutation | None; an authenticated browser session was unavailable. |
| Runtime substitute | Source tracing, focused in-memory financial scenarios, related regressions, and Release builds. |

Credentials, connection strings, cookies, and tokens are excluded from this report.

## 4. Pre-Change Defect Baseline

The Phase 2A source matrix identified a one-sided lifecycle:

| Stage | Affiliate authority | GL state before T-07 |
| --- | --- | --- |
| Earned | Immutable `Earned` commission rows | Registered consumer posts Dr `6110` / Cr `2105`. |
| Approved | Immutable `Approved` rows | Eligibility only; no new GL journal expected. |
| Reversed | Immutable `Reversed` rows | Projection consumers only; no GL reversal journal. |
| Paid | Evidence, allocations, and `Paid` rows | Synchronous orchestrator posts Dr `2105` / Cr authorized Cashbox. |

The defect was not reversal calculation or event creation. The reversal source and integration event already existed. The missing component was a registered consumer that translated the event back to immutable SQL authority and then into GL authority.

## 5. Tenant Baseline

| Tenant | Affiliate earned | Affiliate approved | Affiliate reversed | Affiliate paid | Reversal GL journals |
| --- | ---: | ---: | ---: | ---: | ---: |
| Shopixy | EGP `120.00` | EGP `120.00` | EGP `120.00` | EGP `0.00` | `0` historical |
| AhramStore | EGP `0.00` | EGP `0.00` | EGP `0.00` | EGP `0.00` | `0` |
| greatone | EGP `0.00` | EGP `0.00` | EGP `0.00` | EGP `0.00` | `0` |

All observed affiliate amounts are EGP. Cross-currency behavior beyond the source/event currency consistency checks is not claimed from live data.

## 6. Shopixy Reversal Timeline

| Order | Earned | Approved | Reversal rows | Total reversed | Net payout eligibility |
| --- | ---: | ---: | --- | ---: | ---: |
| `ORD-83B3-2026-01022` | EGP `40.00` | EGP `40.00` | EGP `20.00` + EGP `20.00` | EGP `40.00` | EGP `0.00` |
| `ORD-83B3-2026-01030` | EGP `40.00` | EGP `40.00` | EGP `40.00` | EGP `40.00` | EGP `0.00` |
| `ORD-83B3-2026-01032` | EGP `40.00` | EGP `40.00` | EGP `40.00` | EGP `40.00` | EGP `0.00` |
| **Total** | **EGP `120.00`** | **EGP `120.00`** | **4 immutable rows** | **EGP `120.00`** | **EGP `0.00`** |

There are no Shopixy affiliate `Paid` rows, payout requests, external transfer-evidence rows, or payout allocations in this baseline. The discrepancy therefore comes from the missing GL reversal bridge, not from an affiliate cash payment.

## 7. Historical GL Integrity

Before deployment of the T-07 consumer, Shopixy retains:

| Account | Historical earned effect | Historical reversal effect | Retained balance |
| --- | ---: | ---: | ---: |
| `6110 Affiliate Marketing Expense` | Dr EGP `120.00` | EGP `0.00` | EGP `120.00` debit |
| `2105 Affiliate Payable` | Cr EGP `120.00` | EGP `0.00` | EGP `120.00` credit |

The four historical reversal rows have zero journals sourced by reversal-ledger identity. Existing refund journals affected sales and COD-clearing authority, not Affiliate Payable or affiliate expense. T-07 intentionally does not infer or create historical accounting from an audit session.

## 8. Source-Of-Truth Hierarchy

The certified reversal hierarchy is:

1. The tenant-owned append-only `AffiliateCommissionLedger` reversal row authorizes reversal identity, amount, currency, order, item, affiliate, and original earned linkage.
2. The original tenant-owned `Earned` row authorizes the maximum commission and commercial lineage.
3. `AffiliateCommissionReversedEvent` carries durable asynchronous intent and ordering metadata.
4. The registered accounting consumer re-reads and validates both SQL rows.
5. The existing earned journal proves that the liability and expense being reversed reached the GL first.
6. `FinancialPostingService` remains the sole journal-materialization boundary.
7. Journal entries and ledger lines remain accounting/reporting authority.
8. Projections, event payloads, UI values, and cached balances cannot independently authorize the posting.

## 9. Reversal Source And Event Durability

`AffiliateService` creates reversals with `CreateReversalWithCustomKey`, preserves `OriginalEntryId`, and publishes `AffiliateCommissionReversedEvent` with `StoreTenantId`, `OrderId`, `OrderItemId`, `AffiliateAccountId`, both ledger identities, amount, and currency.

The authoritative path uses `MassTransitEventPublisher` and the EF MassTransit outbox. Where a transaction is open, the reversal row and outbox state commit together. The publisher rejects authoritative events that have neither a current transaction nor a pending business mutation. This preserves the existing domain-event architecture and avoids a second, competing `AccountingOutboxMessages` path.

Consumer failure is rethrown for broker retry. A successfully posted journal is protected against duplicate delivery by its stable business idempotency key.

## 10. Registered Accounting Bridge

`PointOfSale.API/Program.cs` now registers:

```csharp
x.AddConsumer<AffiliateCommissionReversedAccountingConsumer>();
```

MassTransit subsequently calls `ConfigureEndpoints(context)`, so the consumer receives its own configured endpoint under the existing bus conventions. Projection consumption remains unchanged; accounting is an additional consumer of the same durable event.

The bridge is implemented in `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateAccountingConsumer.cs` beside the earned and paid accounting boundaries.

## 11. Journal Contract

Each immutable reversal row authorizes one journal with this exact financial shape:

| Attribute | Contract |
| --- | --- |
| Source type | `AffiliateCommissionReversed` |
| Source ID | `ReversalLedgerEntryId` |
| Source reference | `AFFILIATE_COMM_REVERSED:{ReversalLedgerEntryId}` |
| Journal date | Persisted reversal `RecordedAtUtc` accounting date |
| Debit | `2105 Affiliate Payable`, exact `CommissionAmount` |
| Credit | `6110 Affiliate Marketing Expense`, exact `CommissionAmount` |
| Cash/Bank/clearing lines | None |
| Posting service | `IFinancialPostingService.PostJournalAsync` |

For a full EGP 120 reversal after the corresponding earned posting, the net result is EGP 0 payable and EGP 0 affiliate expense, with no cash movement.

## 12. Idempotency And Replay

The journal idempotency key is:

```text
AFFILIATE_COMM_REVERSED:{TenantId}:{ReversalLedgerEntryId}
```

Duplicate delivery re-reads the persisted source and accepts only an equivalent existing journal. Equivalence includes tenant, source type, source ID, source reference, immutable source date, and ordered debit/credit shape.

If the same event identity is redelivered with a changed amount, currency, source linkage, order, item, affiliate, or tenant, source validation fails before a second accounting effect. If an idempotency key is already bound to another financial shape, `InvalidJournalException` fails the delivery rather than treating it as an idempotent success.

## 13. Source Validation And Over-Reversal

The consumer rejects incomplete identity, empty tenant/order/item/affiliate/source IDs, nonpositive amount, empty currency, and disagreement between `TenantId` and `StoreTenantId`.

After loading the reversal, it validates:

| Invariant | Enforcement |
| --- | --- |
| Tenant | Reversal and original rows must match `StoreTenantId`. |
| Entry type | Source must be `Reversed`; original must be `Earned`. |
| Linkage | `OriginalEntryId` must match the event and loaded earned row. |
| Commercial identity | Order, item, and affiliate must match event and original. |
| Amount and currency | Event must exactly match immutable reversal; reversal currency must match original. |
| Cumulative bound | Sum of reversals for the original cannot exceed original commission. |

This prevents a broker payload or projection from escalating the accounting amount beyond SQL authority.

## 14. Ordering And Earned-Journal Guard

A reversal can be delivered before the earned accounting consumer because they are separate event consumers. T-07 therefore requires the earned journal before posting the reversal.

The guard loads `AFFILIATE_COMM_EARNED:{TenantId}:{OrderId}` and verifies Posted status, earned source type and order identity, and exact aggregate Dr `6110` / Cr `2105` agreement with immutable earned commission rows for the order. If it is absent, the consumer records the out-of-order block and throws `AFFILIATE_REVERSAL_EARNED_JOURNAL_NOT_READY` for retry.

The focused ordering test proves no journal and no account mutation when earned accounting is not ready.

## 15. Full And Partial Reversals

Focused executable scenarios prove:

| Scenario | Expected and observed result |
| --- | --- |
| Full EGP 120 reversal | One Posted balanced journal; Dr `2105` EGP 120 / Cr `6110` EGP 120; payable and expense net to zero. |
| Partial EGP 30 of EGP 100 | Payable and expense both reduce to EGP 70. |
| Two partial reversals, EGP 30 + EGP 20 | Two source-specific journals; payable and expense both reduce to EGP 50. |
| Duplicate first reversal | One journal and one EGP 40 effect. |
| Changed replay amount | Rejected; no second journal or account effect. |

The original earned journal remains Posted and immutable. T-07 creates a separate compensating journal and does not mark, edit, or delete the earned journal.

## 16. Approval And Payout Eligibility

Approval is a settlement-eligibility stage and has no separate GL effect. Reversal accounting is therefore identical whether the commission has an `Approved` row or not.

`AffiliatePayoutService.GetAvailableBalanceAsync` already derives availability as Approved less Reversed. The focused EGP 100 approved / EGP 30 reversed scenario returns EGP 70 for total approved, raw available, and available for withdrawal, matching the EGP 70 balances in both `2105` and `6110`.

No payout code change was required. The fix aligns the GL with the existing net payout rule rather than introducing a second payout calculation.

## 17. Paid-Then-Refunded Policy

No supported post-payout affiliate clawback, receivable, negative affiliate balance, or recovery-settlement policy was found. T-07 does not invent one.

If commission has already cleared `2105` through a payout, a later reversal would attempt to debit an insufficient payable balance. Because `2105` prevents negative balance, `FinancialPostingService` throws `InsufficientFundsException`; no reversal journal is created. This is fail-closed behavior pending an explicitly designed and approved recovery policy.

The focused already-paid scenario proves the rejection and zero reversal-journal effect.

## 18. Financial Reports And Cash Isolation

The report scenario posts EGP 100 earned and EGP 30 reversed, then queries the normal reporting services:

| Report/control | Result |
| --- | --- |
| Trial Balance | Balanced. |
| Balance Sheet | Balanced. |
| Profit and Loss | `6110` expense is EGP `70.00`. |
| General Ledger `2105` | Closing balance is EGP `70.00`, with earned and reversal entries. |
| Cash Flow net change | EGP `0.00`. |
| Cash Flow operating amount | EGP `0.00`. |
| Cash Flow reconciliation | True. |

The full-reversal test separately proves that Cashbox balance is unchanged. The journal contract contains no Cashbox, Bank, payment-clearing, customer, supplier, or vendor account.

## 19. Tenant Isolation

The consumer does not load a reversal by globally unique ID alone. It uses `IgnoreQueryFilters` only with explicit `StoreTenantId` predicates for reversal, original, reversal totals, earned rows, and journals. Account resolution is also tenant-scoped.

The cross-tenant scenario changes both event tenant fields to a foreign tenant while leaving the source rows in the original tenant. Consumption fails, creates zero reversal journals, and leaves original-tenant payable and expense balances unchanged.

Read-only baseline controls show no affiliate financial activity in AhramStore or greatone. T-07 performed no cross-tenant mutation in any environment.

## 20. Failure Atomicity And Observability

Account resolution, source validation, and earned-journal validation occur before reversal posting. Posting itself remains inside `FinancialPostingService`, which owns balanced-entry validation, account locking, serializable execution for relational providers, immutable journal creation, ledger-line creation, balance-cache updates, and idempotency conflict handling.

A forced account-resolution failure leaves journal and ledger-line counts unchanged and leaves `2105` at its pre-attempt balance. All consumer exceptions are logged, counted in affiliate telemetry, and rethrown for MassTransit retry. Successful processing records duration and reversal-ledger identity.

No source ledger row is marked posted by the consumer. The immutable reversal row and unique journal identity remain the reconciliation relationship.

## 21. Verification Evidence

| Gate | Result |
| --- | --- |
| Focused `AffiliateReversalAccountingConsumerTests` | PASS, `11/11`. |
| Affiliate/accounting/settlement/report/payment-lifecycle regression filter | PASS, `140/140`. |
| API Release build | PASS, `0` errors; existing repository warnings remain. |
| Web production build | PASS; existing large-chunk warning remains. |
| `git diff --check` | PASS; line-ending notices only. |
| Shopixy/AhramStore/greatone baseline inspection | PASS under read-only repeatable-read SQL. |
| Historical Shopixy mutation check | PASS; no repair or replay was performed. |
| Live browser execution | Not run; authenticated browser automation was unavailable and no UI change was required. |
| Live future reversal mutation | Not run; the certification constraints prohibited creating or altering financial business data. |

The test project explicitly includes the new accounting test because the repository temporarily excludes `Accounting\*.cs` by default. One pre-existing architecture assertion was narrowed to its intended earned-equivalence method so reversal source-date validation cannot produce a false positive.

## 22. Final Certification

T-07 provides the missing forward accounting bridge without replacing the existing append-only source, MassTransit EF outbox, projection consumers, payout orchestrator, or posting service. For each future valid reversal, SQL source authority now produces exactly one immutable, balanced, non-cash Dr `2105` / Cr `6110` journal. Full, partial, repeated, changed, out-of-order, approved, paid, reporting, failure, and cross-tenant cases are covered by passing executable evidence.

The historical Shopixy EGP 120 mismatch is retained as disclosed evidence and requires a separately authorized remediation if the business later chooses to repair it. Deployment of this consumer prevents the same omission for future reversal events; it does not claim broker replay of events published before the endpoint existed.

**FINAL VERDICT: PASS**

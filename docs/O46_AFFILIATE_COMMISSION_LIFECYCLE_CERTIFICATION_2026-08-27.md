# Shopixy Order O-4.6 — Affiliate Commission Lifecycle Certification

Date: 2026-08-27  
Mode: direct Codex forensic review and remediation; no delegation; no browser automation  
Protected order: `ORD-83B3-2026-01019` — not read through an authenticated API and never mutated  
Verdict: **CONDITIONAL PASS**

## Executive conclusion

The production model is an append-only, per-order-item SQL commission ledger. It is not an `Order.Status`, payment, fulfillment, Mongo projection, payout-request, or frontend-calculated value. Payment recognition plus accounting eligibility creates `Earned`; physical delivery plus a second financial check creates `Approved`; refund authority appends proportional `Reversed` entries; payout execution appends `Paid` entries while the separate balance ledger records the monetary withdrawal.

Four related P1 defects were found and fixed:

1. Any reversal, including a partial refund, was treated as a full reversal by approval selection, admin payout selection, payout total calculation, and admin/portal reporting. The unreversed liability could become unapprovable or unpayable and was displayed incorrectly.
2. Payout execution re-read the current product policy cap instead of the immutable checkout-time cap snapshot.
3. Delivery approval used an incomplete payment blacklist and did not explicitly fail closed for cancelled or fully refunded order authority.
4. Affiliate financial mutation endpoints returned raw exception messages that could contain internal ledger, tenant, order, provider, or database details and did not consistently return stable error codes.

All identified O-4.6 P0/P1 defects are remediated. Live authenticated runtime/provider evidence was unavailable, so live `Earned`, `Approved`, and `Paid` outcomes remain **NOT VERIFIED** and the verdict cannot be promoted to an unconditional pass.

## 1. Worktree baseline and scope control

The pre-edit worktree was already substantially dirty: 103 changed/untracked paths, approximately 6,239 insertions and 1,225 deletions. The inventory included ongoing O-4.1 through O-4.5 work, Affiliate Refactor A/B/C follow-on work, order/payment/fulfillment/shipping hardening, frontend affiliate discovery/identity work, tests, and certification documents. Relevant files such as `AffiliateService`, refund consumers, controllers, and several tests already contained user changes.

No unrelated change was reverted. Remediation was limited to the commission ledger, approval/payment selection, payout cap authority, commission read contracts, financial error mapping, the two affected displays, and deterministic tests. `git diff --check` reports no whitespace errors; only existing LF-to-CRLF conversion notices are emitted.

No schema was changed and no migration was generated. No dependency was added or updated.

## 2. Domain and artifact inventory

### Canonical write model

- `AffiliateAttribution`: one tenant/order attribution, with `ReferralLink=0`, `CouponCode=1`, or `ManualAdmin=2`; optional promotion-link evidence.
- `Order` and `OrderItem`: canonical order ownership plus immutable affiliate eligibility/rate/cap/base/amount snapshots captured at checkout.
- `AffiliateCommissionLedger`: append-only commission lifecycle entries (`Earned`, `Reversed`, `Approved`, `Paid`).
- `AffiliateBalanceLedger`: append-only withdrawals, adjustments, bonuses, and penalties.
- `AffiliatePayoutRequest`: separate withdrawal workflow (`Pending`, `Approved`, `Rejected`, `Paid`).
- `AffiliatePayoutTransferEvidence` and `AffiliatePayoutTransferAllocation`: immutable external-evidence authority and its allocation.
- `OrderRefund` / `OrderRefundLine`: durable per-line refund allocation used to prove partial reversals.

### Services and mutation authority

- `CheckoutService`: resolves affiliate/product/category authority and captures immutable order-item snapshots.
- `AffiliateService`: attribution reconciliation, earning, approval, reversal, and legacy facade methods.
- `AffiliatePayoutService`: balance and payout-request lifecycle; payout execution is deprecated here.
- `AffiliateSettlementOrchestrator`: the single live payout/settlement execution authority.
- `AffiliateAttributionReconciliationHostedService`: attribution recovery, not a second commission-money authority.

### Event consumers and integrations

- Earn/recovery: `OnlinePaymentCapturedAffiliateConsumer`, `CodDeliveredAffiliateConsumer` (despite the historical class name it consumes `OfflinePaymentCollectedEvent`), `WalletPaymentApprovedAffiliateConsumer`, `OrderCollectionAccountingPostedAffiliateConsumer`, and `OrderCompletedAffiliateConsumer`.
- Approval: `AffiliateSettlementConsumers` consumes `OrderDeliveredEvent` and `FulfillmentDeliveredEvent`.
- Reversal: `OrderRefundedAffiliateConsumer` and `OrderItemRefundedAffiliateConsumer`.
- Accounting: `AffiliateCommissionEarnedAccountingConsumer` and `AffiliateCommissionPaidAccountingConsumer`.
- Derivative projections: affiliate attribution/earned/reversed/approved/paid Mongo consumers and daily analytics consumers.
- Event publication uses the existing event/outbox infrastructure; consumer replay is fenced by ledger idempotency keys and projection inbox/event identities.

### APIs and frontend

- POS Admin: `AdminAffiliateController` (`Owner,Admin`) exposes ledger-backed orders/earnings, automatic-delivery revalidation, legacy batch settlement through the orchestrator, and payout-request actions.
- Affiliate portal: `AffiliatePortalController` (`AffiliateOnly`) derives the current affiliate identity and returns ledger-backed orders, earnings, balance, and payout history/request operations.
- Frontend: `AdminAffiliateDetail.jsx` and portal `OrdersTab.jsx` display backend-issued values; they do not calculate authoritative commission from price and rate.
- Mongo documents are presentation/analytics projections only. No export or report was found that legitimately supersedes the SQL ledgers.

## 3. Canonical authority findings

| Data | Canonical authority | Projection | Frontend allowed to calculate? |
|---|---|---|---|
| Attribution | SQL `AffiliateAttribution` plus canonical order affiliate fields | Mongo affiliate/order projection | No |
| Commission rate | Checkout-time `OrderItem.AffiliateCommissionRateSnapshot`; copied to ledger | Read DTOs/Mongo | Preview only, never settlement |
| Commission base | Checkout-time `OrderItem.AffiliateCommissionBaseAmountSnapshot` | Read DTOs/Mongo | No |
| Commission amount | Checkout-time amount snapshot, then immutable SQL ledger amounts | Admin/portal/Mongo summaries | No |
| Commission state | Presence and amounts of SQL ledger entry types | Admin/portal status labels | No |
| Reversal amount | Sum of append-only SQL `Reversed` entries referencing the earning | Admin/portal/Mongo | No |
| Affiliate balance | SQL approved/reversal amounts + balance-ledger adjustments/withdrawals − active payout reservations | Dashboard DTO | No |
| Payout | SQL payout request + orchestrator + transfer evidence/allocation + balance withdrawal | Admin/portal history | No |
| Settlement | `AffiliateSettlementOrchestrator` transaction and SQL ledgers/outbox | UI status/event projections | No |

The display balance clamps a negative amount to zero for usability, while `RawAvailable` preserves negative liability. A `Paid` commission row is lifecycle evidence; it is not subtracted a second time because the balance-ledger withdrawal is the monetary debit authority.

## 4. Commission states

The ledger represents a graph of immutable entries, not an in-place mutable status row.

| State | Stored value | Meaning | Terminal? |
|---|---:|---|---|
| Earned | 0 | Recognized commission liability for one eligible order item | No |
| Reversed | 1 | Append-only reduction tied to the original earning | Terminal entry; additional refund entries may coexist |
| Approved | 2 | Delivery and financial authority revalidated; amount remains the immutable earned amount | No |
| Paid | 3 | Unreversed approved remainder included in a completed settlement | Terminal lifecycle entry |

`Pending` is a derived absence/read label, not a commission-ledger enum. `Cancelled`, `Disputed`, and `Rejected` are not commission-ledger states. Payout-request states are a separate enum: `Pending=1`, `Approved=2`, `Rejected=3`, `Paid=4`.

## 5. Valid transitions

| From | To | Trigger | Authority | Preconditions | Financial effect | Event |
|---|---|---|---|---|---|---|
| No ledger entry | Earned | Online capture, COD collection, reviewed wallet approval, accounting-posted recovery, or completed-order replay | `AffiliateService` re-reads SQL order/payment/accounting/attribution and item snapshots | Attribution; non-terminal order; payment `Collected`, `Settled`, or `PartiallyRefunded`; accounting `Posted` or `NotRequired`; positive eligible snapshot | Append one earning per eligible item | `AffiliateCommissionEarnedEvent` |
| Earned | Approved | Delivered event or admin-triggered revalidation | `AffiliateService` inside serializable transaction | Physical delivery; tenant match; manual-evidence rules; recognized payment; accounting eligible; order not cancelled/refunded; not fully reversed | Append approval copying earned terms | `AffiliateCommissionApprovedEvent` |
| Earned (+ any later lifecycle entries) | Reversed | Durable full/item refund event | Refund record/line + `AffiliateService` | Same tenant/order/item; original earning exists; positive remaining base/commission | Append proportional reversal capped at unreversed remainder | `AffiliateCommissionReversedEvent` |
| Approved | Paid | Approved payout request mark-paid or legacy batch settlement | `AffiliateSettlementOrchestrator` | Exact FIFO approved remainder; no prior paid item; balance; currency; verified external evidence; store/affiliate/payout authority | Append paid remainder; append balance withdrawal; allocate transfer evidence | `AffiliateCommissionPaidEvent` plus accounting/outbox |
| Payout Pending | Payout Approved | Admin approval | `AffiliatePayoutService` with row lock | Current admin, store scope, balance/wallet snapshot checks | Reserve/confirm approved withdrawal amount | Payout lifecycle/audit |
| Payout Pending | Payout Rejected | Admin rejection | `AffiliatePayoutService` with row lock | Current admin and store scope | Releases pending reservation; no paid commission | Payout lifecycle/audit |
| Payout Approved | Payout Paid | Mark-paid | Orchestrator serializable transaction | Immutable transfer evidence and exact commission allocation | Settlement and ledger effects commit together | Paid/accounting events |

### Illegal transitions

- Attribution, delivery, completion, or provider-attempt state alone → `Earned`.
- Pending/authorized/failed/expired/amount-mismatch/refunded payment → `Earned` or `Approved`.
- Not physically delivered/failed delivery/pre-delivery return → `Approved`.
- `Earned` → `Paid` without an `Approved` predecessor.
- Payout request creation or approval → commission `Paid`.
- Any destructive update of an existing commission ledger amount/type.
- Any second earning/approval/paid semantic outcome for the same order item.
- Any reversal beyond the original earning's remaining base or commission.
- Any payout recomputation from current product price, category, rate, or cap.

### Trigger summary

- **Earn:** canonical payment recognition plus accounting `Posted`/`NotRequired`; event names are merely wake-up signals and never substitute for the SQL re-read.
- **Approve:** physical delivery plus payment/accounting/terminal-order revalidation. Automatic delivery consumers own the normal path; the admin endpoint invokes the same guard.
- **Reverse:** authoritative full or per-line refund allocation. Cancellation alone does not fabricate a refund reversal.
- **Payout:** request/approval is separate from execution; only the orchestrator with external evidence appends `Paid` and the withdrawal.
- **Settlement:** the orchestrator transaction coordinates payout request, commission ledger, balance ledger, transfer allocation, accounting, and outbox work.

## 6. Actual commission formula

For each eligible order line at checkout:

```text
line subtotal = roundCurrency(current authoritative unit price × quantity)
line base     = roundCurrency(line subtotal − allocated order discount + allocated tax)
raw           = line base × historical rate
cap           = nullable historical max per item × quantity
commission    = round4TowardZero(cap is null ? raw : min(raw, cap))
```

| Component | Included? | Authority |
|---|---|---|
| Authoritative item/variant unit price at checkout | Yes | Checkout cart/product authority; persisted on order item |
| Quantity | Yes | Canonical cart/order item |
| Allocated order discount | Subtracted | Checkout allocation by line subtotal |
| Allocated tax | Added | Checkout allocation by line subtotal |
| Shipping | No | Not part of the line allocation/base |
| Current product price after order | No | Historical item snapshot wins |
| Current policy/category after order | No | Historical item snapshot wins |

Currency totals use the existing two-decimal currency rounding (`AwayFromZero`); commission uses decimal arithmetic and four decimals with `MidpointRounding.ToZero`. Rate is decimal `(8,6)`, valid from `0.0001` through `0.50`. Cap is nullable positive decimal `(18,4)`; `null` means no cap. The ledger copies the canonical order currency. Payout is store/currency scoped and blocks ambiguous or mismatched currency authority.

Multi-line orders create one earning per eligible order item. Mixed eligible/ineligible lines are supported, each eligible line can carry its own rate and cap, and the order has a single enforced attribution (`TenantId + OrderId` unique). No multi-affiliate allocation exists for one order.

## 7. Order × payment × fulfillment × commission matrix

| Scenario | Order | Payment | Fulfillment | Commission | Expected |
|---|---|---|---|---|---|
| New affiliate order | Active | Pending | New/unfulfilled | None/Pending label | Attribution and snapshots only |
| COD pending | Active | Pending | May progress | None | Shipping is allowed; earning is not |
| COD delivered pending | Delivered/active | Pending | Delivered | None | Delivery cannot substitute for collection |
| COD collected | Active | Collected | Any | Earned after accounting | `Posted`/`NotRequired` is also required |
| Online pending/authorized | Active | Pending/authorized | Any | None | Provider attempt is not authority |
| Online collected | Active | Collected/Settled | Not delivered | Earned | Approval waits for delivery |
| Delivered + paid | Active/delivered | Collected/Settled | Delivered | Approved | Earn if absent, then delivery replay can approve |
| Failed delivery | Active | Any | Failed/OnHold | Not Approved | A pre-existing online earning remains until refund authority reverses it |
| Pre-delivery return/RTO | Active | Usually uncollected/refunded | Not physically delivered | Not Approved | Refund event reverses any prior earning |
| Post-delivery return | Active/returned | Collected/PartiallyRefunded/Refunded | Returned | Net approved or reversed | Partial refund preserves remainder; full refund blocks approval |
| Partial refund | Active | PartiallyRefunded | Any | Proportional `Reversed`; net remainder survives | Append-only, line allocation required |
| Full refund | Refunded or payment refunded | Refunded | Any | Fully reversed | No new earn/approve/payout remainder |
| Cancelled | Cancelled | Any non-qualifying/refund flow | Any | No new earn/approve | Reversal requires actual refund authority |
| Completed | Completed | Recognized | Completed/delivered | Earned/Approved as guards permit | Completion event is recovery only, not authority |
| Paid commission | Any historical order state | Previously recognized | Previously delivered | Paid plus any later reversal evidence | Paid transfer is not destructively rewritten |

## 8. Affiliate policy change matrix

| Change after order | Historical rate | Historical cap | Current product | Expected commission |
|---|---|---|---|---|
| Rate changed | Preserved | Preserved | Irrelevant | Original snapshot amount |
| Max cap changed | Preserved | Preserved | Irrelevant | Original snapshot amount/cap |
| Category disabled | Preserved | Preserved | No new admission | Existing admitted order unchanged |
| Product policy disabled | Preserved | Preserved | No new admission | Existing admitted order unchanged |
| Affiliate account disabled/suspended | Historical attribution preserved | Preserved | Irrelevant | Existing amount is not recalculated; current payout/account governance may still block new actions |
| Product price changed | Preserved | Preserved | New price ignored | Original line base/amount snapshot |

The payout defect that violated this matrix was fixed: both orchestrator payout paths now load `OrderItem.AffiliateMaxCommissionPerItemSnapshot`, not `AffiliateProductPolicies.MaxCommissionPerItem`.

## 9. Refund and reversal semantics

- A refund never overwrites `Earned`, `Approved`, or `Paid`.
- Partial refund uses durable `OrderRefundLine` amounts; missing allocation fails closed.
- Reversal base is the lesser of the current refund allocation and the remaining unreversed base.
- Reversal commission is the original earned commission multiplied by the refunded-base/original-base ratio, truncated to four decimals and capped at the remaining commission; the final reversal clears rounding residue.
- Idempotency includes refund identity, order-item identity, and the four-decimal refunded base. The global unique ledger key turns replay into one semantic outcome.
- Multiple rounded partial refunds are supported. Payout subtracts the exact append-only reversal totals and does not reconstruct a second answer from the remaining base.
- A refund after payout appends reversal liability; it does not pretend the external transfer never happened. `RawAvailable` can become negative.

## 10. Payout and balance separation

`AffiliatePayoutRequest` is a reservation/workflow object. It is not commission state. Creating a request reserves availability; approving it does not append `Paid`. Mark-paid passes through the single orchestrator, which locks/validates SQL authority, checks exact available balance and currency, requires immutable external evidence (or explicitly audited break-glass configuration), selects exact FIFO approved net remainders, appends `Paid`, records the negative balance-ledger withdrawal, allocates evidence, reconciles the request, and publishes accounting/projection events in the transaction/outbox boundary.

Available balance is derived per store and currency from approved commission, reversal liability, balance-ledger adjustments/penalties/bonuses/withdrawals, and active payout reservations. Mongo summaries do not authorize withdrawal.

## 11. Tenant, affiliate, authorization, and database review

| Operation | Tenant A | Tenant B | Affiliate A | Affiliate B | Result |
|---|---|---|---|---|---|
| Read attribution | Own tenant/order | Different tenant | Bound attribution | Different affiliate | Scoped result or not found |
| Read commission | `StoreTenantId=A` | `StoreTenantId=B` excluded | Bound ledger account | Other account excluded | Scoped result or not found |
| Create earning | Canonical order tenant | Event mismatch rejected | Order/attribution account | Substitution rejected | One scoped earning per item |
| Approve | Order tenant re-read | Event tenant mismatch rejected | Ledger/order affiliate | Other affiliate not selected | Guarded serializable append |
| Reverse | Refund/order tenant re-read | Mismatch cannot resolve row | Original earning affiliate | Other affiliate not selected | Scoped append only |
| Payout | Store-scoped balance/evidence | Other store excluded | Requested account | Other account excluded | Conflict/not found |
| Settlement | Store/affiliate locked | Cross-store evidence rejected | Exact approved rows | Other account excluded | Atomic or rolled back |

Admin endpoints require `Owner,Admin`; portal endpoints require `AffiliateOnly` and derive the affiliate account from authenticated backend context. The frontend supplies intent, order IDs already in scope, and external reference/evidence fields; it does not supply authoritative commission amounts or tenant authority.

Database controls include positive amount/base checks, rate range checks, valid entry-type checks, original-entry requirements, restrict-delete FKs, a globally unique commission idempotency key, one attribution per tenant/order, globally unique balance-ledger idempotency, unique external rail/reference evidence, and unique transfer-allocation idempotency. Application queries that use `IgnoreQueryFilters` explicitly bind `StoreTenantId`, order, affiliate, and/or payout identity.

Defense-in-depth note (P2): commission/order/attribution FKs are principally single-ID FKs rather than composite tenant FKs. GUID identity plus explicit service query binding prevents the reviewed runtime paths from crossing tenants, but a future migration could add composite alternate-key FKs so the database itself independently rejects every cross-tenant reference.

## 12. Error and security contract

Affiliate financial mutation errors now pass through `AffiliateFinancialErrorContract`. It preserves a syntactically safe `AFFILIATE_*` domain code while replacing the detail with a category-safe message. It separately maps validation, not found, concurrency, duplicate/already-applied, refund/reversal conflict, payout/provider/evidence conflict, persisted financial conflict, and unexpected failure. Detailed exceptions remain server-side structured logs; SQL, stack traces, ledger IDs, tenant IDs, order IDs, and provider payload details are not returned by the remediated mutation catches.

The controllers remain protected by authorization policies. No authoritative amount calculation, tenant selection, payout credential authority, `localStorage`, `sessionStorage`, or cookie value was introduced in the affected frontend. The portal/admin displays consume the backend `NetCommissionAmount`, `ReversedAmount`, and partial/full-reversal flags.

## 13. Production fix table

| Production file | Area | Finding | Severity | Root cause | Fix applied | Verification |
|---|---|---|---|---|---|---|
| `AffiliateService.cs` | Approval | Any reversal blocked approval | P1 | Existence test collapsed partial and full reversals | Aggregate reversal amounts and skip only a fully reversed earning | Focused lifecycle and source certification |
| `AffiliateService.cs` | Approval authority | Payment blacklist omitted terminal/non-recognized states and terminal order guard | P1 | Negative-list gate drifted from earn authority | Reuse recognized-payment whitelist; reject cancelled/refunded orders | O-4.6 source test + existing approval tests |
| `AffiliateService.cs` | Legacy paid selection | Any reversal removed the whole approved line | P1 | Boolean reversal filter | Calculate approved minus reversal net amount | Focused/source tests |
| `AffiliateSettlementOrchestrator.cs` | Historical cap | Payout queried today's product policy cap | P1 | Current policy join in both payout paths | Load immutable order-item cap snapshot | O-4.6 source test + payout suites |
| `AffiliateCommissionLedger.cs` | Paid remainder | Reapplying cap/rate to reduced base broke capped and multi-rounded partial refunds | P1 | Historical liability was recalculated instead of subtracting ledger entries | Full payment still validates snapshot formula; partial payment uses exact bounded ledger remainder | New capped and multi-refund unit tests |
| `AdminAffiliateController.cs` | Eligibility/total | Partial reversal made order ineligible and payout total zero | P1 | `Any(Reversed)` treated as terminal | Compare earned/approved and reversed sums; pay net remainder | Focused/source tests |
| `AdminAffiliateController.cs` | Reporting | Partial reversal displayed as fully reversed | P1 | Boolean reversal projection | Return gross, reversed, net, and partial/full flags | Build + frontend tests |
| `AffiliatePortalController.cs` | Reporting | Portal collapsed partial into full reversal | P1 | Boolean reversal projection | Same authoritative partial/full/net contract as admin | Build + frontend tests |
| `AffiliateFinancialErrorContract.cs` and controller catches | Error/security | Raw exception details escaped financial mutation APIs | P1 | Direct `ex.Message` responses | Stable safe codes/messages with server-side detailed logging | O-4.6 certification test + build |
| `AdminAffiliateDetail.jsx`, `OrdersTab.jsx` | Presentation | UI showed gross commission after partial refund | P1 consequence | DTO lacked/use ignored net value | Display backend net amount; full-only strike-through; partial badge retained | 35 affiliate frontend tests + production build |

No production P0 was found. No known production P1 remains in the reviewed O-4.6 scope.

## 14. Test evidence

### Focused and relevant passing evidence

| Command/scope | Result |
|---|---|
| Focused settlement/lifecycle/O-4.6/withdrawable-balance pack | **61 passed, 0 failed** |
| Event pipeline, completed-order recovery, earning/approval authority, financial replay, payout execution/recovery pack | **80 passed, 0 failed** |
| Admin/portal API and tenant-scope pack | **96 passed, 2 baseline failures** |
| Affiliate frontend Vitest directory | **8 files, 35 tests passed** |

New deterministic evidence covers historical cap source, partial approval/payment/read contracts, recognized-payment and terminal-order approval guards, safe mutation errors, capped partial payout, and multiple independently rounded partial refunds.

### Broad regression evidence and classification

| Broad filter | Passed | Failed | Classification |
|---|---:|---:|---|
| `Affiliate` excluding integration | 857 | 13 | 3 missing obsolete migration/source fixtures; 5 stale documentation markers; 3 stale source-shape assertions; 1 stale controller endpoint count; 1 invalid test setup state transition |
| Order/Payment/Fulfillment/Refund/Payout/CommerceHub excluding affiliate/integration | 1,666 | 179 | Existing broad dirty-worktree baseline: missing migration/docs fixtures, stale architecture source assertions, relational/provider/environment-dependent failures, and unrelated O-4.x/CommerceHub changes. No failure identified as caused by the O-4.6 diff. |

The two API-pack failures are the same baseline items: the test expects exactly 19 admin endpoints but the dirty controller has 20, and a tenant-scope test attempts to suspend a non-active account before reaching the assertion. The initial commission-named baseline run also exposed the absent `20260429163000_S58_OrderItemAffiliateSnapshotMutualExclusivity.sql` fixture. These failures are reported, not hidden or weakened.

## 15. Build and runtime evidence

| Verification | Result |
|---|---|
| `dotnet build PointOfSale.sln --no-restore` | **PASS — 0 errors, 0 warnings** on final incremental build |
| `npm run build` | **PASS** — 5,200 modules transformed; one existing >5 MB chunk advisory |
| `git diff --check` on remediation files | **PASS** — line-ending notices only |
| Database migration | N/A — no schema change |
| Dependency restore/install | N/A — no dependency change |

Runtime discovery found three `dotnet` processes but no TCP listener owned by them. Process command-line inspection was denied by the host, and no authenticated development API session was available. Therefore no direct affiliate/order/payment/fulfillment/ledger/balance API read was possible. No browser automation was used, no provider call was made, and `ORD-83B3-2026-01019` was never mutated.

## 16. Required financial test-matrix disposition

| Area | Disposition |
|---|---|
| Attribution validity, tenant mismatch, invalid/disabled affiliate, policy/category admission | Covered by existing attribution, governance, checkout, and eligibility suites; current-state changes gate new admission |
| Historical rate/cap/product price | Covered; payout cap defect remediated |
| Recognized payment/accounting, COD, online, wallet | Covered by payment consumers and authority/recovery tests; runtime provider evidence not available |
| Failed/expired/amount-mismatch payment | Fail closed through shared recognized-payment whitelist |
| Delivery, failed delivery, retry, pre/post-delivery return | Covered by delivery revalidation/idempotency/refund tests; live carrier evidence not available |
| Partial/full/repeated refund, after approval/payout | Append-only proportional reversal and balance-liability behavior covered; partial remainder defects remediated |
| Duplicate earn/approve/reverse/pay | Global unique semantic keys plus serializable/row-lock paths covered by existing tests |
| Concurrency/stale events | Serializable approval/payout, unique constraints, recovery and replay suites pass |
| Tenant/affiliate isolation and authorization | Explicit query binding/policies covered; composite-FK hardening remains a P2 note |
| Safe errors | Remediated with stable safe contract; detailed exception remains server-side |
| Rate precision/cap/rounding/snapshot | Decimal, four-decimal truncation, nullable cap, historical snapshots, and multi-refund rounding covered |

## 17. Final certification matrix

| Area | Backend | Affiliate Portal | POS Admin | Order | Payment | Fulfillment | Refund | Payout | Status |
|---|---|---|---|---|---|---|---|---|---|
| Attribution | SQL authority | Derived | Derived | Bound | N/A | N/A | N/A | N/A | PASS |
| Calculation | Snapshot authority | Displays | Displays | Item snapshots | Gate | N/A | Net effect | Consumes snapshot | PASS |
| Earned | Append-only | Derived | Derived | Non-terminal | Recognized | Not sufficient | May reverse | Balance input | PASS |
| Approved | Append-only | Derived | Action revalidates | Non-terminal | Revalidated | Delivered | Net remainder | Payable input | PASS |
| Reversed | Append-only | Gross/net/partial | Gross/net/partial | Independent | Refund authority | Independent | Trigger/lines | Reduces payable | PASS |
| Paid | Append-only | Derived | Orchestrator only | Historical | Historical | Historical | Later liability possible | External evidence | PASS |
| Partial refund | Proportional remainder | Corrected | Corrected | Preserved | PartiallyRefunded | Revalidated | Durable lines | Net remainder | PASS |
| Full refund | Full reversal | Reversed | Reversed | Terminal guard | Refunded blocked | N/A | Append-only | No remainder | PASS |
| COD | Collection consumer | Derived | Derived | Delivery may precede pay | Collection required | Delivery alone insufficient | Normal rules | After approval | PASS |
| Online payment | Capture consumer | Derived | Derived | Canonical order | SQL status re-read | Approval later | Normal rules | After approval | PASS |
| Concurrency | Serializable/unique | Read-only | Backend owned | Re-read | Re-read | Re-read | Remaining caps | Locks/serializable | PASS |
| Idempotency | Unique semantic keys | Projection inbox | Replay-safe | Item key | Event replay | Event replay | Refund key | Evidence/allocation keys | PASS |
| Tenant isolation | Explicit scoped queries | Claim-derived | Role + tenant | Canonical tenant | Canonical tenant | Event tenant | Refund tenant | Store tenant | PASS |
| Authorization | Policies/services | AffiliateOnly | Owner/Admin | N/A | N/A | N/A | N/A | Admin/affiliate lanes | PASS |
| Security | Safe codes; logs internal | Backend amounts | Backend amounts | No second authority | No provider shortcut | No delivery shortcut | No destructive update | Evidence gated | PASS |
| Error handling | Stable categories | Safe payout errors | Safe mutation errors | N/A | N/A | N/A | Conflict code | Provider/payout code | PASS |
| Live runtime | Not available | Not exercised | Not exercised | Protected | Not exercised | Not exercised | Not exercised | Not exercised | NOT VERIFIED |

## 18. Final verdict

**CONDITIONAL PASS**

No known P0/P1 commission authority, calculation, attribution, tenant, refund/reversal, payout, concurrency, idempotency, lifecycle, presentation, or error-disclosure defect remains after remediation. Static forensic evidence, deterministic focused tests, relevant event/recovery/payout tests, frontend tests, and both builds pass.

The only certification condition is the absence of an authenticated Development runtime and real provider/fulfillment evidence. Live `Earned`, `Approved`, and `Paid` states are therefore explicitly **NOT VERIFIED**. Existing broad-suite failures are recorded above as baseline/fixture/documentation/infrastructure work and were not concealed or modified to force a green result.

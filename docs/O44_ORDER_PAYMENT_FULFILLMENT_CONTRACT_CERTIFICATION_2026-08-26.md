# Shopixy O-4.4 — Order ↔ Payment ↔ Fulfillment Contract Certification

Date: 2026-08-26  
Reviewer: Codex direct forensic review (no delegation, no browser automation)  
Verdict: **CONDITIONAL PASS**

No P0 remains. Two P1 tenant-authority defects were found and remediated. Focused regression and production builds pass. The verdict remains conditional because real PostgreSQL concurrency, development API transitions, and Stripe/Paymob carrier/provider runtime transitions were not exercised against disposable infrastructure.

## 1. Scope, baseline, and worktree ownership

The initial worktree was already materially dirty: 87 tracked files were modified, with additional untracked O-4.1/O-4.2/O-4.3 certification files. The initial tracked diff was 5,335 insertions and 1,137 deletions. Changes spanned Order, Payment, Fulfillment/Shipping, Affiliate, CommerceHub, Admin UI, and unrelated work. No existing change was reverted.

Baseline `git diff --check` had no whitespace errors; it emitted only Git line-ending conversion warnings. After O-4.4, the combined user + O-4.4 tracked diff covers 100 files, 5,539 insertions and 1,165 deletions. This report distinguishes O-4.4 remediation from pre-existing work; the aggregate diff must not be interpreted as authored solely by O-4.4.

The protected reference `ORD-83B3-2026-01019` was not queried or mutated. Its semantic state remains valid: COD + Pending + OutForDelivery may be delivered, but may not be completed until collection/accounting authority exists.

## 2. Canonical authorities

| Lifecycle | Canonical authority | May read | May mutate | Must never mutate |
|---|---|---|---|---|
| Order | `Order.Status`; guarded Order/Fulfillment/Payment services | Payment, fulfillment, shipment evidence | Its commercial/operational status through authorized scopes | Payment provider attempt state; fabricated shipment/delivery evidence |
| Payment | `Order.PaymentStatus` via `PaymentLifecycleService`; PaymentIntent is evidence | Order, intent, settlement/accounting evidence | PaymentStatus; narrowly Pending→Confirmed, guarded failure/cancel outcomes | Processing, Shipped, Delivered; fulfillment/shipment state |
| Fulfillment | `Order.FulfillmentStatus` via `OrderFulfillmentService` | Order financial gate and shipment evidence | FulfillmentStatus and synchronous monotonic Order mapping | Payment/refund state; carrier/provider evidence |
| Shipment | `Shipment.Status` via shipment/carrier services | Order and fulfillment readiness | Shipment evidence; calls fulfillment authority for canonical convergence | Arbitrary Order status; PaymentStatus; direct FulfillmentStatus assignment |
| Affiliate Commission | Append-only affiliate ledger | Payment/accounting, delivery, refund allocations | Earned/Approved/Reversal ledger entries | Order, Payment, Fulfillment, Shipment |
| CommerceHub | Marketplace parent/suborder and CommerceHub financial services | Store/vendor/payment/fulfillment evidence | Its own parent/suborder aggregate | Standard tenant Order/Payment/Fulfillment state |

## 3. Production call graph and cross-lifecycle contracts

### Order → Payment

- Checkout/order creation creates or skips a PaymentIntent according to the selected method. COD may skip provider intent creation and starts canonical PaymentStatus Pending.
- Dedicated Admin cancellation owns Order cancellation. If payment is Pending/Authorized/under review, it stages `PaymentCancellationOutboxMessage`; `PaymentLifecycleService` later performs the guarded payment cancellation and finalizes the Order outcome. Recognized money cannot be bypass-cancelled.
- Completion does not collect money; it only reads the financial gate.
- Return/cancellation does not silently refund. Refund is a dedicated Payment lifecycle action.

### Payment → Order

| Payment event/state | Order transition | Guard | Authority | Transaction |
|---|---|---|---|---|
| Collected/settled success | Pending → Confirmed | Canonical success, tenant/order lineage, current SQL state | `PaymentLifecycleService`; projection sync is secondary | Same payment aggregate transaction/outbox |
| Offline/COD collection after delivery | Pending/Delivered convergence only | COD method, FulfillmentStatus Delivered, tenant, idempotency | `PaymentLifecycleService` | Same transaction as PaymentStatus/outbox |
| Failure/expiry | Eligible non-terminal order → OnHold/recovery outcome | Current intent/order state; late/terminal guards | `PaymentLifecycleService` | Same transaction/outbox |
| Cancellation completion | Eligible order → Cancelled | Durable cancellation request; no recognized-payment bypass | cancellation outbox + payment lifecycle | Atomic canonical mutation when finalized |
| Partial/full refund | No implicit Order.Status assignment | Refund authority and cumulative amount | `PaymentLifecycleService` | Payment/refund transaction/outbox |
| Reconciliation/recovery | Same guarded transitions | Tenant, provider evidence, current state, idempotency | canonical payment service | Lock + transaction/outbox |

Payment cannot assign Processing, Shipped, or Delivered. Those states require fulfillment/shipment authority.

### Order ↔ Fulfillment

- `OrderFulfillmentService` owns all FulfillmentStatus writes.
- Fulfillment transitions synchronously map, inside the same DB transaction, as follows:

| Fulfillment transition | Order transition | Preconditions | Authority |
|---|---|---|---|
| Confirmed | Confirmed | source/mode, financial gate, legal transition | fulfillment service |
| Packed | Processing | Confirmed, financial gate | fulfillment service |
| OutForDelivery | Shipped | Packed, financial gate | fulfillment service |
| Delivered | Delivered | OFD, source authority, delivery evidence | fulfillment service |
| Completed | Completed | Delivered plus recognized collection and Posted/NotRequired accounting | fulfillment service |
| FailedDelivery | OnHold | OFD and failure evidence | fulfillment service |
| RetryDelivery | Shipped remains monotonic | FailedDelivery → OFD | fulfillment service |
| Returned | no automatic refund/cancel | legal pre/post-delivery return path | fulfillment service |

The fulfillment service uses a relational serializable transaction and row-level locking, enables guarded SaveChanges interceptor scopes, stages its event/outbox with the canonical mutation, and collapses same-state/current-or-newer replays. Carrier and generic shipment services call this authority; carrier-managed changes are rejected from generic Admin mutation paths.

### Payment ↔ Fulfillment

- Payment influences fulfillment only as a blocking/read gate. It does not write FulfillmentStatus.
- Fulfillment influences payment only through the explicit COD delivery/collection contract. Delivery evidence is required; failed delivery and return do not collect.
- Refund changes payment/affiliate financial state but does not itself cancel shipment or fulfillment. A full refund blocks later outbound progression because Refunded is not recognized collection authority.

## 4. Exact answers to the twelve contract questions

1. **When can Payment influence Order.Status?** Pending→Confirmed on canonical collection/success; guarded OnHold/Cancelled recovery paths on failure/expiry/cancellation. Refund has no implicit Order mutation.
2. **When can Payment influence Fulfillment?** Only as the outbound/completion financial gate; it never writes FulfillmentStatus.
3. **When can Fulfillment influence Order.Status?** Synchronously through the certified mapping Confirmed/Processing/Shipped/Delivered/Completed/OnHold in `OrderFulfillmentService`.
4. **When can Order influence Payment?** Creation can create/skip an intent; dedicated cancellation can request payment cancellation; completion only reads payment authority. Order return/cancel does not mean refund.
5. **When can Order influence Fulfillment?** Admin/carrier actions address the Order aggregate but must delegate every transition to `OrderFulfillmentService`; cancellation cannot bypass it.
6. **When can Fulfillment influence Payment?** Only delivered COD/offline collection evidence may invoke canonical collection. No other fulfillment outcome writes PaymentStatus.
7. **Informational-only transitions?** Shipment tracking/evidence, provider PaymentIntent status, returns/failed delivery relative to refund, projection events, and CommerceHub parent aggregation.
8. **Blocking gates?** Non-COD recognized payment + Posted/NotRequired accounting for outbound; all positive-value completion requires recognized payment + Posted/NotRequired; terminal/full-refunded orders cannot progress outbound.
9. **Event-driven transitions?** Provider results, delivery/COD collection consumers, shipment projection synchronization, cancellation outbox processing, affiliate earned/approval/reversal, and CommerceHub projections.
10. **Synchronous transitions?** Fulfillment→Order status mapping, financial gate evaluation, source/evidence validation, and canonical payment mutation within each owning service.
11. **Transactional transitions?** Canonical status plus its integration event/outbox; fulfillment→Order mapping; payment/refund/accounting authority; cancellation finalization; commission ledger/reversal.
12. **Must never occur automatically?** Delivery from payment, payment collection from failed delivery/return, refund from return/cancel, Order cancellation from shipment cancellation, Payment refund from shipment return, fulfillment state from Payment, Order state from Affiliate/CommerceHub parent, or frontend/provider direct status writes.

## 5. Payment method × fulfillment matrix

`Recognized` means Collected, Settled, legacy Paid, or PartiallyRefunded. For positive non-COD orders, recognized collection must also have accounting Posted or NotRequired before any outbound step.

| Method | Payment state | Confirm | Pack | OFD | Deliver | Complete |
|---|---|---|---|---|---|---|
| COD | Pending | Allow | Allow | Allow | Allow | Block |
| COD | Collected + Posted/NotRequired | Allow/idempotent | Allow | Allow | Allow | Allow after Delivered |
| Manual Wallet / Vodafone Cash | Pending/Authorized/UnderReview | Block | Block | Block | Block | Block |
| Manual Wallet / Vodafone Cash | Collected + Posted/NotRequired | Allow | Allow | Allow | Allow | Allow after Delivered |
| Paymob | Pending/Authorized | Block | Block | Block | Block | Block |
| Paymob | Collected/Settled + Posted/NotRequired | Allow | Allow | Allow | Allow | Allow after Delivered |
| Stripe | Pending/Authorized | Block | Block | Block | Block | Block |
| Stripe | Collected/Settled + Posted/NotRequired | Allow | Allow | Allow | Allow | Allow after Delivered |
| Fawry/other online discovered methods | Pending/Authorized | Block | Block | Block | Block | Block |
| Fawry/other online discovered methods | Recognized + Posted/NotRequired | Allow | Allow | Allow | Allow | Allow after Delivered |
| Any non-COD | PartiallyRefunded + Posted/NotRequired | Allow if lifecycle permits | Allow | Allow | Allow | Allow after Delivered |
| Any | Refunded/Cancelled/Failed | Block | Block | Block | Block | Block |

Authorized is not Collected. A zero-value order is exempt from the positive-value financial gate.

## 6. Payment × Fulfillment × Order matrix

| Scenario | Payment | Fulfillment | Order | Allowed? | Reason |
|---|---|---|---|---|---|
| COD Pending before delivery | Pending | Created→OFD | Confirmed→Shipped | Yes | COD outbound exemption |
| COD Delivered before collection | Pending | Delivered | Delivered | Yes | delivered is not paid |
| COD Collected | Collected + accounting | Delivered→Completed | Delivered→Completed | Yes | completion gate now satisfied |
| Online Pending | Pending | outbound target | unchanged | No | recognized payment missing |
| Online Collected | Collected + Posted/NotRequired | outbound | monotonic mapping | Yes | financial authority satisfied |
| Partial Refund | PartiallyRefunded | active/delivered | unchanged by refund | Yes, if accounting/lifecycle permit | partial refund stays recognized |
| Full Refund | Refunded | no new outbound progress | not auto-cancelled | No outbound | Refunded is not recognized authority |
| Failed Delivery | unchanged | FailedDelivery | OnHold | Yes | explicit operational failure; no refund |
| Returned | unchanged unless separate refund | Returned/FailedDelivery pre-delivery | not auto-refunded/cancelled | Yes | return and refund are separate |
| Cancelled | payment cancellation only if dedicated workflow requires | cancelled separately | Cancelled | Guarded only | separate owners and compensation |
| Completed | recognized + accounting | Completed | Completed | Only after Delivered | strict completion gate |

## 7. Cancellation, delivery, return, refund, and commission

- **Order cancellation:** dedicated Admin workflow; rejects delivered/completed and recognized-payment bypass; durable payment cancellation request when required.
- **Payment cancellation:** canonical Payment lifecycle; may finalize a previously authorized Order cancellation request.
- **Shipment cancellation:** Shipment evidence only; never silently sets Order Cancelled.
- **Fulfillment cancellation:** fulfillment authority; the generic endpoint cannot substitute for dedicated commercial Order cancellation.
- **Manual delivery:** Manual mode + OutForDelivery + non-empty delivery reference + absolute HTTP/HTTPS proof URL.
- **Carrier delivery:** authenticated/signed carrier path → shipment authority → fulfillment authority; no arbitrary Order assignment.
- **Failed delivery:** FailedDelivery + Order OnHold; no collection, refund, commission approval, cancellation, or completion.
- **Pre-delivery return-to-origin:** operational failure/FailedDelivery path. **Post-delivery return:** Returned. Neither is payment refund.
- **Partial refund:** PaymentStatus becomes/remains PartiallyRefunded, fulfillment/shipment/Order are not implicitly mutated, affiliate reversal uses durable per-item allocations.
- **Full refund:** PaymentStatus Refunded, blocks outbound, reverses eligible commission, but does not automatically cancel Order/Shipment/Fulfillment.
- **Affiliate:** recognized payment + posted accounting earns; delivered event revalidates payment/accounting before approval; item refund allocations append reversals. Creation, failure, cancellation, return, or delivery alone cannot fabricate Earned commission.

## 8. CommerceHub boundary

CommerceHub uses `CommerceHubMarketplaceOrder`/SubOrder, its own payment readiness, store/vendor ownership, serializable mutation scopes, and parent aggregation. Suborder state influences only the CommerceHub parent aggregate. No inspected CommerceHub path directly overwrites standard `Order.Status`, `Order.PaymentStatus`, or `Order.FulfillmentStatus`. Standard Order and CommerceHub are therefore parallel bounded contexts, not competing authorities.

## 9. Event, ordering, concurrency, and idempotency

- Canonical Order/Payment/Fulfillment mutations stage their owned event/outbox transactionally. Cross-process consumers do not require a distributed transaction.
- Events carry event ID, tenant, correlation/idempotency data, occurrence time, and version where the contract requires ordering.
- Shipment synchronization reads current SQL shipment lineage instead of trusting an old event snapshot. Terminal/regression/Pending-promotion guards prevent stale Order overwrite.
- Fulfillment and Payment lock the current aggregate and collapse same-state/current-or-newer replay. Provider webhook IDs, refund IDs, shipment request IDs, payment references, and ledger idempotency keys enforce duplicate suppression.
- Late payment success versus cancellation is quarantined/guarded; full refund blocks an older shipment update from advancing outbound state.
- COD delivery and collection converge through the canonical payment service. Collection is idempotent and requires current Delivered fulfillment evidence; FailedDelivery/Returned cannot trigger it.
- Real PostgreSQL multi-connection race execution was not available; concurrency proof is therefore code + deterministic fixtures, not a live database stress claim.

## 10. P0/P1 production remediation

| Production file | Cross-lifecycle area | Finding | Severity | Root cause | Fix applied | Verification |
|---|---|---|---|---|---|---|
| `IPaymentLifecycleService.cs`, `PaymentLifecycleService.cs` | Payment background/system mutations | `IgnoreQueryFilters` flows could resolve an Order/PaymentIntent by global ID without explicit tenant authority | P1 | Event/job contract omitted tenant while bypassing query filters | Added TenantId to offline collection/expiry and system request contract; reject missing tenant with `PAYMENT_TENANT_AUTHORITY_REQUIRED`; validate loaded Order and PaymentIntent tenant before mutation | Cross-tenant COD regression + focused 67/67 + build |
| `OrderDeliveredEventConsumer.cs`, `ShipmentCodCollectedConsumer.cs` | Fulfillment/Shipment → COD Payment | Misrouted or forged event could target another tenant aggregate | P1 | Event tenant was not mandatory/forwarded | Reject empty tenant; forward event tenant into canonical payment service | consumer forwarding/no-mutation tests |
| `PaymentCancellationOutboxProcessor.cs`, `InventoryReservationCleanupJob.cs`, `ManualPaymentRuntimeApplicationService.cs`, `PaymentLifecycleController.cs` | Order cancellation/expiry/manual review/COD API → Payment | System callers did not bind the canonical payment mutation to their known tenant | P1 | Tenant was available but absent from lifecycle call | Pass durable candidate/request/current tenant through every call | focused tests and compilation |
| `IAffiliateService.cs`, `AffiliateRefundConsumers.cs`, `AffiliateService.cs` | Refund → Affiliate reversal | Refund consumers/service used `IgnoreQueryFilters` and globally resolved Order/refund/ledger IDs | P1 | Tenant not part of reversal authority contract | Require tenant, reject tenantless events, filter Order/OrderRefund/Earned ledger/retry helper by tenant + order + item | tenantless event regression; Affiliate suite in 67/67 and broad runs |

No P0 was found. No unrelated production redesign was introduced.

### Non-blocking findings

- **P2 database defense-in-depth:** PaymentIntent→Order and Shipment→Order FKs are single-column `OrderId`, although both children carry TenantId and Order exposes `(TenantId, Id)` as an alternate key. Public/service mutation paths are now tenant-bound, but a future isolated migration should replace these with composite tenant FKs after auditing existing data. It was not generated in this dirty model/snapshot because EF could capture unrelated pending model work.
- **P2 performance:** Fulfillment serializable scopes and authoritative re-reads are justified for lifecycle correctness; observe contention before narrowing. No benchmark was invented.
- **P2 frontend hygiene:** generic shipping setup hooks log caught error objects to `console.error`. No reviewed lifecycle path logs provider secrets, Authorization headers, wallet proof contents, or carrier credentials.
- **P2 bundle:** Vite reports a ~5.0 MB main minified chunk; code splitting is outside O-4.4.

## 11. Admin action contract

| Action | Owner | Preconditions | Endpoint | Backend guard | Side effects |
|---|---|---|---|---|---|
| Confirm | Fulfillment | Created, source/mode, financial gate | `POST /api/orders/{id}/fulfillment/confirm` | fulfillment state machine | Order Confirmed + event/outbox |
| Pack | Fulfillment | Confirmed + financial gate | `.../pack` | fulfillment authority | Order Processing |
| OFD | Fulfillment | Packed + financial gate | `.../out-for-delivery` | fulfillment authority | Shipment sync + Order Shipped |
| Delivered | Fulfillment | OFD + manual/carrier authority + evidence | `.../delivered` | fulfillment authority | Order Delivered; COD collection event path |
| Failed Delivery | Fulfillment | OFD + failure evidence | `.../failed-delivery` | fulfillment authority | Order OnHold; no refund/collection |
| Retry | Fulfillment | FailedDelivery | `.../retry-delivery` | fulfillment authority | back to OFD; Order does not regress |
| Complete | Fulfillment/Order | Delivered + recognized payment + accounting | `.../complete` | financial completion gate | Fulfillment/Order Completed |
| Cancel Order | Order | non-terminal, no recognized-payment bypass | `POST /api/admin/orders/{id}/cancel` | Admin Order service | cancellation outbox/compensation if needed |
| Cancel Shipment | Shipment | shipment transition/source authority | `POST /api/admin/shipments/{id}/cancel` | shipment service | Shipment only |
| Refund | Payment | Owner/Admin, amount/idempotency/line allocations | `POST /api/payments/lifecycle/{id}/refund` | payment lifecycle | Payment/refund outbox + affiliate reversal |
| Payment verification | Payment | Owner/Admin, proof/review/current intent | `/api/admin/payments/manual-wallet/...` | manual-payment application + payment lifecycle | canonical collection/rejection/accounting |
| COD collection | Payment | Delivered COD + tenant + idempotency | `POST /api/payments/lifecycle/{id}/cod-delivered` or delivery event | payment lifecycle | collection/accounting; not delivery fabrication |

Frontend `Orders.jsx` consumes backend `availableActions`; it may render a candidate action but cannot bypass these endpoints/guards.

## 12. Authority direction matrix

| Source | Target | Allowed? | Exact contract |
|---|---|---|---|
| Payment | Order | Limited | Pending→Confirmed; guarded OnHold/Cancelled recovery only |
| Payment | Fulfillment | Read gate only | recognized payment/accounting blocks or permits outbound/completion |
| Order | Payment | Limited | create/skip intent; durable cancellation request; never silent refund |
| Order | Fulfillment | Limited | action request must delegate to fulfillment authority |
| Fulfillment | Order | Yes | synchronous monotonic certified mapping in same transaction |
| Fulfillment | Payment | COD only | Delivered evidence may request offline collection |
| Shipment | Fulfillment | Yes, mediated | shipment/carrier services call fulfillment service |
| Shipment | Order | Informational/mediated | SQL lineage sync with guards; no arbitrary direct assignment |
| Affiliate | Order | No | ledger consumer only |
| Affiliate | Payment | No | reads financial evidence only |
| Affiliate | Fulfillment | No | reads delivery evidence only |
| CommerceHub | Order | No | separate bounded context |
| CommerceHub | Fulfillment | No | owns CommerceHub suborder fulfillment only |
| CommerceHub | Payment | No | owns CommerceHub payment only |

## 13. Cross-lifecycle contract matrix

| Contract | Order | Payment | Fulfillment | Shipment | Affiliate | CommerceHub |
|---|---|---|---|---|---|---|
| Creation | PASS | PASS | PASS | PASS | N/A | PASS |
| Confirmation | PASS | PASS | PASS | PASS | N/A | PASS |
| Processing | PASS | PASS | PASS | PASS | N/A | PASS |
| Shipping | PASS | PASS | PASS | PASS | N/A | PASS |
| Delivery | PASS | PASS | PASS | PASS | PASS | PASS |
| Completion | PASS | PASS | PASS | PASS | PASS | PASS |
| Cancellation | PASS | PASS | PASS | PASS | PASS | PASS |
| Refund | PASS | PASS | PASS | PASS | PASS | PASS |
| Return | PASS | PASS | PASS | PASS | PASS | PASS |
| Retry | PASS | PASS | PASS | PASS | N/A | PASS |
| Concurrency | PASS | PASS | PASS | PASS | PASS | PASS |
| Idempotency | PASS | PASS | PASS | PASS | PASS | PASS |
| Events | PASS | PASS | PASS | PASS | PASS | PASS |
| Tenant | PASS | PASS | PASS | PASS | PASS | PASS |
| Authorization | PASS | PASS | PASS | PASS | PASS | PASS |

`PASS` here means code path + deterministic test certification. Provider/runtime distinctions appear in the final matrix below.

## 14. Final certification matrix

| Area | Backend | Admin UI | Payment | Fulfillment | Affiliate | CommerceHub | Status |
|---|---|---|---|---|---|---|---|
| Creation | PASS | PASS | PASS | PASS | N/A | PASS | PASS |
| Confirmation | PASS | PASS | PASS | PASS | N/A | PASS | PASS |
| Processing | PASS | PASS | PASS | PASS | N/A | PASS | PASS |
| Shipping | PASS | PASS | PASS | PASS | N/A | PASS | PASS |
| Delivery | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Completion | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Cancellation | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Refund | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Return | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| COD | PASS | PASS | PASS | PASS | PASS | N/A | PASS |
| Manual Wallet | PASS | PASS | PASS | PASS | PASS | N/A | PASS |
| Paymob | PASS | PASS | NOT VERIFIED | PASS | PASS | N/A | CONDITIONAL |
| Stripe | PASS | PASS | NOT VERIFIED | PASS | PASS | N/A | CONDITIONAL |
| Concurrency | PASS | PASS | PASS | PASS | PASS | PASS | PASS (fixtures) |
| Idempotency | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Events | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Tenant Isolation | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Authorization | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Error Handling | PASS | PASS | PASS | PASS | PASS | PASS | PASS |

Paymob/Stripe `NOT VERIFIED` refers only to real provider sandbox/runtime transitions, not their static adapters or deterministic contract tests.

## 15. Verification evidence and failure classification

| Command/slice | Result | Classification |
|---|---|---|
| prerequisite focused lifecycle slice | 93/93 passed | clean |
| first remediation focused slice | 53/53 passed | clean |
| final changed-contract focused slice | 67/67 passed | clean |
| broad Order/Payment/Fulfillment/Shipment/Affiliate/CommerceHub slice | 185 passed, 10 failed | 7 SQLite/PostgreSQL `btrim` mismatch; 3 stale source/doc assertions |
| provider/COD/manual/concurrency/CommerceHub slice | 219 passed, 1 failed | stale historical documentation marker |
| Admin Orders/shipment/manual-payment frontend | 31/31 passed | clean |
| wider selected frontend slice | 34 passed, 4 failed | stale Storefront payment mocks/storage fixture against stricter shared contract |
| `dotnet build PointOfSale.sln --no-restore` | success, 0 warnings, 0 errors | clean final incremental build |
| `npm run build` in `PointOfSale.Web` | success | existing main-chunk size advisory only |
| final `git diff --check` | no whitespace errors | line-ending warnings only |

The stale backend assertions expected removed implementation strings or historical `PASS SF-*` markers. The shipment source assertion specifically expected a mutable ShippingMethod query; production now uses the immutable Order shipping snapshot plus tenant-scoped carrier readiness, which is stronger authority. The Storefront test failures are outside the Admin O-4.4 surface and fail before network assertions because their mocked response omits newly required evidence/currency.

No migrations or dependency changes were created by O-4.4. No live provider transaction was run. No browser automation was used.

## 16. Runtime status and verdict

Direct Development API verification was not attempted because no confirmed disposable tenant/order or provider sandbox was supplied, and the named Order was explicitly protected. Real carrier callbacks, Paymob/Stripe sandbox transitions, and PostgreSQL multi-connection races remain runtime verification work.

**Final verdict: CONDITIONAL PASS.**

Order, Payment, Fulfillment, and Shipment remain independently authoritative. Their permitted influence is explicit, guarded, tenant-bound, transactional where canonical state and events are co-owned, and idempotent. No P0/P1 cross-lifecycle defect remains in the inspected production paths. The condition is limited to critical provider/database runtime transition verification, not a known unresolved authority defect.

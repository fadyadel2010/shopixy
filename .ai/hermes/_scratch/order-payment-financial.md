# Order Payment + Financial Runtime — Evidence Report

Repo: `C:\Users\fadya\source\repos\PointOfSale` (Shopixy/PointOfSale, .NET 9, C#)
Scope: storefront `Order` payment lifecycle, financial posting, refunds, suspense.
Method: every claim below is cited `file:line` to the actual code (comments NOT trusted; verified in implementation).

---

## 1. IPaymentLifecycleService — the ONLY mutator of `Order.PaymentStatus`

The interceptor only inspects `Order` entries (`PaymentStatusSaveChangesInterceptor.cs:75-79`), and the only places that set `order.PaymentStatus =` inside the storefront lifecycle are in `PaymentLifecycleService.cs`. Other direct `.PaymentStatus =` writes exist in `CommerceHubPaymentLifecycleAuthority.cs` but operate on **different entity types** (`CommerceHubMarketplaceOrder` / `CommerceHubSubOrder`, see `CommerceHubPaymentLifecycleAuthority.cs:10-27`) which are NOT `Order`, so the interceptor does not cover them. The storefront `Order.PaymentStatus` is therefore mutated solely by `PaymentLifecycleService`.

### Canonical entry / dispatch
- `HandlePaymentResultAsync` is the single funnel: maps `PaymentLifecycleOutcome` → handler (`PaymentLifecycleService.cs:105-117`).
- `AmountMismatch` outcome is **system-only** — external callers cannot pass it; it is auto-detected and quarantined (`PaymentLifecycleService.cs:111-113`).

### Transition methods and their `Order.PaymentStatus` writes

| Method | Trigger / public entry | Target status | Code |
|---|---|---|---|
| `HandleAuthorizedAsync` | `Outcome.Authorized` | `Authorized` | `PaymentLifecycleService.cs:1782` |
| `HandleSuccessAsync` | `Outcome.Succeeded` (capture/collect) | `Collected` | `PaymentLifecycleService.cs:2053` |
| `HandleFailureAsync` | `Outcome.Failed/Cancelled/Rejected/Expired` | `Failed`/`Rejected`/`Expired` (target switch :2228-2233) | `PaymentLifecycleService.cs:2385` |
| `AdmitPaymentReviewAsync` | manual-wallet review admit | `AwaitingVerification` | `PaymentLifecycleService.cs:268` |
| `RecordRefundAsync` | explicit refund | `Refunded` or `PartiallyRefunded` | `PaymentLifecycleService.cs:967` + decision :935 |
| `RecordOfflineSettlementAsync` | COD bank settlement | `Settled` | `PaymentLifecycleService.cs:454` |
| `RecordGatewaySettlementAsync` | gateway settlement | `Settled` | `PaymentLifecycleService.cs:714` |
| `QuarantineLateSuccessfulPaymentAsync` | late success after reservation expiry | `Expired` (with `OnHold`) | `PaymentLifecycleService.cs:3557` |
| `TryQuarantineProviderAuthorityMismatchAsync` | amount/currency mismatch | `AmountMismatch` (with `OnHold`) | `PaymentLifecycleService.cs:3869` |

### Public facades (delegate to the handlers above)
- `MarkOfflineCollectionDeliveredAsync` → `HandlePaymentResultAsync(Succeeded, OfflineCollection)` (`PaymentLifecycleService.cs:281-296`).
- `CaptureOnlinePaymentAsync` → `HandlePaymentResultAsync(Succeeded, GatewayWebhook)` (`PaymentLifecycleService.cs:534-552`).
- `ApproveReviewedPaymentAsync` → `HandlePaymentResultAsync(Succeeded, PaymentReviewApproval)` (`PaymentLifecycleService.cs:806-821`).
- `RejectReviewedPaymentAsync` → `HandlePaymentResultAsync(Rejected, PaymentReviewRejection)` (`PaymentLifecycleService.cs:823-837`).
- `MarkPaymentFailedAsync` → `HandlePaymentResultAsync(Failed, GatewayWebhook)` (`PaymentLifecycleService.cs:1097-1109`).
- `MarkPaymentExpiredAsync` → expires each active `PaymentIntent` via `HandlePaymentResultAsync(Expired, ReservationCleanup)` (`PaymentLifecycleService.cs:1111-1193`; request builder `:1165-1183`).

### Authorization / Capture / Collect / Settle / Refund / Chargeback handling
- **Authorize (pre-auth only):** `HandleAuthorizedAsync` requires current status `Pending` or `Authorized` (`:1767-1772`), sets `Authorized` (`:1782`), no journal/ledger yet. Idempotent replay guard for already-`Collected/Settled/Paid` (`:1732-1740`).
- **Capture/Collect (money recognized at `Collected`):** `HandleSuccessAsync` is the capture path. Validates success state, runs quarantine/currency checks, then sets `Collected` (`:2053`), sets `IsPaid` from item-level `IsFullyPaid` (`:2055`), `PaidAt = now` (`:2056`). Money is allocated item-by-item into `OrderItem.PaidAmount` via `ApplySuccessfulPaymentAllocation` (`:2049`, `:4156-4182`). COD collect requires `FulfillmentStatus.Delivered` (`:1944-1948`); wallet requires review-approval source (`:1951-1954`).
- **Settle (bank received):** `RecordOfflineSettlementAsync` (`:298`) and `RecordGatewaySettlementAsync` (`:554`) both require all target orders in `Collected` (`:414`, `:672`), create a `PaymentSettlementBatch`, set `Settled` (`:454`, `:714`), and enqueue an `AccountingOutboxMessage` (`EnqueueCodSettlementAsync` `:465`, `EnqueueGatewaySettlementAsync` `:725`). They also **gate on collection accounting being already posted** before settling (`EnsureCollectionAccountingPostedForCodSettlementAsync` `:419`, `EnsureCollectionAccountingPostedForGatewaySettlementAsync` `:677`; rejection codes `collection_accounting_not_posted` `:1489`/`:1542`).
- **Refund:** `RecordRefundAsync` (`:839`). Allowed only from `Collected`/`Settled`/`PartiallyRefunded` (`:888-894`). Full vs partial decided by `totalRefundedAfterThisRefund + tolerance >= refundableAmount` (`:934-935`). Over-refund blocked: `normalizedRefundAmount - remainingRefundableAmount > tolerance` → fail (`:927-931`); zero/negative amount rejected (`:912-915`). Refund amount is allocated across items into `OrderRefundLine` via `AllocateRefundAcrossItems` (`:961`, `:4332-4375`) which also bumps `OrderItem.RefundedAmount` (`:4362`). Pre/post-settlement routing branches the outbox (`EnqueueSettledEcommerceRefundAsync` vs `EnqueueEcommerceRefundAsync`, `:971-985`).
- **Chargeback:** NOT handled in the storefront `PaymentLifecycleService`. Grep for chargeback in `PaymentLifecycleService.cs` returns nothing. Chargeback enums/runtimes exist only in the **CommerceHub** bounded context (`PointOfSale.Core/Payments/Runtime/Domain/PaymentSettlementRuntime.cs:13,452` and `PaymentTransactionRuntime.cs:16`). **Missing evidence (flag):** there is no storefront `Order` chargeback transition method in this service.
- **Replay idempotency:** every handler returns `wasIdempotent:true` when the order is already in a terminal state for that outcome (`:1732-1740`, `:2247-2275`, `:879-886` refund dedup via `FindExistingRefund`).

### How it clears `Order.AcceptPaymentStatusChange()`
Uniform pattern in every transition: enable the interceptor bypass, set the status, then clear the change-tracking flag, then `SaveChanges`, then disable the bypass — e.g. `HandleSuccessAsync` (`:2043-2131`):
```
PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();   // :2043
try {
    order.PaymentStatus = PaymentStatus.Collected;               // :2053
    order.AcceptPaymentStatusChange();                           // :2054  (clears _originalPaymentStatus)
    ...
    await _db.SaveChangesAsync(ct);                              // :2126
} finally {
    PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext(); // :2130
}
```
Same pattern: `:1778-1793` (Authorized), `:2370-2437` (Failure), `:265-276` (Review admit), `:431-490` (Offline settle), `:690-736` (Gateway settle), `:3552-3627` (Late-success quarantine), `:3864-3918` (Amount-mismatch quarantine), `:963-1029` (Refund).

`AcceptPaymentStatusChange()` is defined on `Order` and simply sets `_originalPaymentStatus = null` (`Order.cs:149`). `Order.PaymentStatus` setter records the prior value once on first change (`Order.cs:126-131`). The interceptor's gate first checks `IsAuthorizedContext` (`:73`) — so the authorized-context flag is the primary bypass; `AcceptPaymentStatusChange()` is defense-in-depth that resets the dirty flag so later saves of the same tracked entity are not re-flagged.

---

## 2. PaymentStatusSaveChangesInterceptor — blocks unauthorized writes

File: `PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs`.
- Hooked on both `SavingChanges` (`:51-57`) and `SavingChangesAsync` (`:59-66`); both call `ValidatePaymentStatusChanges`.
- `ValidatePaymentStatusChanges` (`:68-110`): if `_authorizedContext` is set, returns immediately (`:73`) — i.e. only `PaymentLifecycleService` (which calls `EnableAuthorizedContext`, `:34`) may pass.
- Otherwise it scans `ChangeTracker.Entries<Order>().Where(State==Modified && PaymentStatusChanged)` (`:75-79`). `PaymentStatusChanged` is `true` only when `_originalPaymentStatus` differs from current `_paymentStatus` (`Order.cs:144`).
- On violation it logs a `LogCritical` `[SECURITY_VIOLATION]` with a captured 10-frame stack trace (`:89-101`) and throws `InvalidOperationException` (`:103-108`) instructing callers to use `IPaymentLifecycleService`.
- The authorized flag is an `AsyncLocal<bool>` (`:28`), enabling cross-await bypass within the service's call flow.
- Note: it only protects the storefront `Order` entity. `CommerceHubMarketplaceOrder`/`CommerceHubSubOrder` `PaymentStatus` writes (`CommerceHubPaymentLifecycleAuthority.cs`) are not `Order` and are therefore not covered (same enum type, different aggregate). **Flag:** the "ONLY mutator" guarantee is strictly true for `Order` only.

---

## 3. PaymentStatus enum values

File: `PointOfSale.Core/Enums/PaymentStatus.cs:24-107`. Values (verified, not relying on comments):
- `Pending = 0` (initial) `:27`
- `Authorized = 1` (pre-auth, no money recognized) `:33`
- `Collected = 2` (money received — **only** revenue-recognizing state) `:40`
- `Paid = 2` (`[Obsolete]` alias of `Collected`) `:46-47`
- `PartiallyPaid = 3` (`[Obsolete]`, tracked at item level) `:52-53`
- `Failed = 4` `:56`
- `Refunded = 5` (full refund) `:62`
- `PartiallyRefunded = 6` `:68`
- `AwaitingVerification = 7` (manual wallet, no journal) `:74`
- `Rejected = 8` `:80`
- `Settled = 9` (bank received) `:87`
- `CashOnDelivery = 10` (fulfillment allowed, no revenue) `:93`
- `Expired = 11` `:100`
- `AmountMismatch = 12` (quarantined, no journal) `:106`

Numeric collisions to note: `Paid (2)` and `Collected (2)` are identical; code treats both as success-terminal (e.g. `HandleAuthorizedAsync` replay guard `:1733-1734`).

---

## 4. FinancialPostingStatus lifecycle on Order + storefront → GL posting

Enum `FinancialPostingStatus` (`PointOfSale.Core/Enums/FinancialPostingStatus.cs:3-9`): `NotRequired=0, Pending=1, Posted=2, PostingFailed=3`.

`Order` fields (`Order.cs:542-546`): `FinancialPostingStatus` (default `NotRequired`, `:542`), `FinancialPostingPendingAtUtc`, `FinancialPostedAtUtc`, `FinancialPostingJournalId`, `FinancialPostingFailureReason`. The field comment states it is the "ledger truth gate" for settlement/commission flows and that `Collected` can precede the async outbox post (`:537-541`).

How storefront money reaches the GL:
1. On capture, `EnqueueSuccessOutboxAsync` stages an `AccountingOutboxMessage` in the same DB transaction (`PaymentLifecycleService.cs:5207-5276`): `EnqueueCodDeliveredAsync` (`:5222`), `EnqueueWalletApprovedAsync` (`:5231`), `EnqueueUnresolvedOnlinePaymentAsync` (suspense route, `:5242`), or `EnqueueOnlinePaymentAsync` (`:5263`).
2. After `SaveChanges`+commit, `PostCommittedCollectionAccountingFromSqlTruthAsync` posts best-effort from "SQL truth" (`:5278`). On success it sets `Order.FinancialPostingStatus = Posted` and stamps `FinancialPostingPendingAtUtc ??= now`, `FinancialPostedAtUtc ??= now`, `FinancialPostingJournalId = journalId`, clears failure reason, and marks the outbox `MarkProcessed(journalId)` (`:5341-5346`).
3. Settlement posting mirrors this: `PostCommittedGatewaySettlementAccountingFromSqlTruthAsync` (`:5468`) / `PostCommittedCodSettlementAccountingFromSqlTruthAsync` (`:5525`). Refund posting: `PostCommittedRefundAccountingFromSqlTruthAsync` (`:5374`), which stamps `OrderRefund.FinancialPostingStatus = Posted` (`:5408-5409` gate, `:5435+`).
4. `AccountingOutboxMessage` (`PointOfSale.Core/Entities/Accounting/AccountingOutboxMessage.cs`): `EventType`, `SourceType` (mirrors `JournalEntry.SourceType`, `:35-39`), `BusinessEntityId`, `PayloadJson`, `IdempotencyKey`, `JournalId` (`:86`), `ProcessedAtUtc` (`:81`), `ClaimedUntilUtc` lease (`:97`, `TryClaim` `:211-224`), `RecordFailure` with exponential backoff / `IsFailed` (`:229-246`), and `RecordNonTerminalFinancialFailure` that keeps retrying without going dead-letter (`:252-261`). `MarkProcessed(Guid journalId)` (`:200-206`) writes `JournalId`+`ProcessedAtUtc`.
5. Settlement is **gated** on collection accounting already being `Posted`: `EnsureCollectionAccountingPostedForCodSettlementAsync` (`:1464`) / `ForGatewaySettlementAsync` (`:1495`) reject with `collection_accounting_not_posted` if any required outbox key is unposted (`:1489`, `:1542`).

**Flag:** the `PaymentSettlementBatch` GL posting mechanics (the actual `CommerceAccountingService` journal creation) are referenced via `_commerceAccounting.PostEcommerceRefundAsync` (`:5437`) and `PostSuccessfulCollectionAccountingAsync` (`:5829`); the journal-entry leg (DEBIT/CREDIT accounts named in enum comments) is implemented in `CommerceAccountingService`, not in this file — out of scope of the requested files but the wiring points are cited.

---

## 5. OrderRefund / OrderRefundLine entities — full/partial model, constraints, authors

`OrderRefund` (`PointOfSale.Core/Entities/OrderRefund.cs:6-30`):
- Fields: `TenantId, OrderId, RefundId, Amount, Currency, IsFullRefund, Reason, PaymentMethod, IsPostSettlementRefund, SettlementBatchId, SettlementType, SettlementId, PayoutBatchId, SettledAtUtc, RefundedAtUtc` (`:8-28`).
- Financial-posting fields mirror `Order`: `FinancialPostingStatus`, `FinancialPostingPendingAtUtc`, `FinancialPostedAtUtc`, `FinancialPostingJournalId`, `FinancialPostingFailureReason` (`:23-27`).
- `Lines` collection of `OrderRefundLine` (`:29`).
- **No negative-amount guard on `OrderRefund.Amount`** itself (plain auto-property `:12`). Full/partial is computed at creation: `IsFullRefund` set by `RecordRefundAsync` from the cumulative-refund decision (`:934-935`, `:947`).

`OrderRefundLine` (`PointOfSale.Core/Entities/OrderRefundLine.cs:5-37`):
- Backing-field `_amount` with a setter that throws `ArgumentOutOfRangeException` if `value < 0` (`:22-36`) — so line amounts can never be negative in code.
- `OrderId` is denormalized for a composite FK (`:17`, comment `:13-16`) ensuring the line's `OrderItem` belongs to the same order.
- Created by `AllocateRefundAcrossItems` which adds one line per affected item (`:4365-4373`).

Migration constraints:
- **S22** (`Migrations/20260424090000_S22_OrderRefundLifecycleHardening.cs`): creates `OrderRefunds` and `OrderRefundLines` tables; `OrderRefunds.Amount` is `numeric(18,2) NOT NULL` (`:27`), `RefundId` `varchar(120)` unique per tenant+order (`:95-99`); `OrderRefundLines.Amount` `numeric(18,2) NOT NULL` (`:65`); unique `(OrderRefundId, OrderItemId)` where not deleted (`:122-127`).
- **S23** (`Migrations/20260424100000_S23_OrderRefundLineAmountNonNegative.cs`): adds check constraint `CK_OrderRefundLines_Amount_NonNegative` = `"Amount" >= 0` (`:25-28`). This is the DB-level non-negative guarantee for line amounts.

Who creates refunds (callers of `RecordRefundAsync`):
- Storefront gateway webhooks: `StripePaymentService.cs:1742`, `PaymobPaymentService.cs:1323` (both call `_paymentLifecycleService.RecordRefundAsync`).
- CommerceHub marketplace ledger: `CommerceHubRefundService.cs:1359`, `CommerceHubFinancialLedgerService.cs:270`, `RefundExecutionService.cs:1292`, `CommerceHubPaymentCompensationOutboxWorker.cs:472` (these call `financialLedger.RecordRefundAsync`).
- REST: `PaymentLifecycleController.cs:186`.
- Note: the CommerceHub `RecordRefundAsync` overloads (e.g. `CommerceHubFinancialLedgerService.RecordRefundAsync(order, subOrder, refund)`) are a **different method on a different service** (`ICommerceHubFinancialLedgerService`, `CommerceHubFinancialLedgerService.cs:41,270`) — distinct from the storefront `IPaymentLifecycleService.RecordRefundAsync(orderId, amount, refundId, reason)`. **Flag:** two `RecordRefundAsync` APIs coexist (storefront vs CommerceHub); this report concerns the storefront one.

---

## 6. Money-on-order-items model (Phase 11.4.2.A) vs order-level

`OrderItem` money fields (`OrderItem.cs:154-186`):
- `PaidAmount` (`:154`), `RefundedAmount` (`:161`, "never exceeds PaidAmount" per comment `:159`), computed `IsFullyPaid => PaidAmount >= Subtotal` (`:167`), `IsFullyRefunded => RefundedAmount >= PaidAmount` (`:173`), `NetPaidAmount` (`:179`), `RemainingAmount => Max(0, Subtotal - PaidAmount)` (`:186`).
- On capture, `ApplySuccessfulPaymentAllocation` writes into `item.PaidAmount` (`:4179`); on refund, `AllocateRefundAcrossItems` writes into `item.RefundedAmount` (`:4362`). So money is tracked at **item (variant) granularity**.

`Order` derived (order-level) view (Phase 11.4.2.A, `Order.cs:557-605`):
- `TotalPaidAmount => Sum(Items.PaidAmount)` (`:563`), `TotalRefundedAmount => Sum(Items.RefundedAmount)` (`:569`), `NetPaidAmount` (`:575`), `BalanceDue => Max(0, Total - TotalPaidAmount)` (`:581`), `IsFullyPaidComputed` (`:587`), `IsPartiallyPaid` (`:593`), `IsFullyRefundedComputed` (`:599`), `IsPartiallyRefunded` (`:605`).
- Legacy order-level `IsPaid` field still exists (`Order.cs:531`) but the comment warns it "may be out of sync" and to use `IsFullyPaidComputed` (`:525`). On capture, `order.IsPaid` is recomputed from `Items.All(i => i.IsFullyPaid)` (`:2055`).

**So:** authoritative money is item-level (`OrderItem.PaidAmount/RefundedAmount`); order-level figures are derivations. The obsolete `PartiallyPaid`/`Paid` enum members (`:52-53`, `:46-47`) predate this model and are deprecated in favor of item-level tracking.

---

## 7. Suspense / quarantine of overpayment & mismatch

Two quarantine terminal states are routed to a suspense outbox rather than normal GL:

- **Amount/currency mismatch (`AmountMismatch`):** `TryQuarantineProviderAuthorityMismatchAsync` (`:3798`). Triggers only for `GatewayWebhook` successes (`RequiresProviderAuthority` `:3961-3969`). Detects missing provider amount, amount ≠ stored, missing currency, or currency ≠ stored (`:3819-3834`). On mismatch it sets `PaymentStatus = AmountMismatch`, `OrderStatus.OnHold`, marks `PaymentIntent.AmountMismatch`, then calls `EnqueueQuarantinedCapturedPaymentSuspenseAsync` (`:3890`). The `HandleSuccessAsync` path also short-circuits already-mismatched orders to `ManualReviewRequired` (`:1900-1906`).
- **Late successful payment (overpayment after reservation expiry / admin cancellation):** `QuarantineLateSuccessfulPaymentAsync` (`:3526`) sets `PaymentStatus = Expired`, `OrderStatus.OnHold`, releases reservations, marks `PaymentIntent.Suspicious`, and also enqueues suspense (`:3600`). Reached from `HandleSuccessAsync` via `IsLateSuccessfulPaymentQuarantineTerminal` (`:1882-1888`) and `IsAdminCancellationPendingQuarantineTerminal` (`:1890-1896`).
- **Suspense enqueue:** `EnqueueQuarantinedCapturedPaymentSuspenseAsync` (`:3928`) calls `_accountingOutbox.EnqueueUnresolvedOnlinePaymentAsync(...)` (`:3950`) — i.e. an `UnresolvedOnlinePayment` outbox message routed to the suspense/pending-reconciliation ledger, carrying provider amount, currency, gateway, webhook event id, and business entity id. This is distinct from normal `EnqueueOnlinePaymentAsync`. Also, a captured online payment that had *previously* been routed to suspense is re-enqueued as `ResolvedUnresolvedOnlinePayment` instead of `OnlinePayment` (`EnqueueSuccessOutboxAsync` `:5240-5258`, `WasPreviouslyRoutedToSuspenseAsync` `:2096`).

**Overpayment (provider paid more than order total) handling:** no separate `Overpayment` status. An overpayment that matches intended amount is just captured into `Collected`; excess is absorbed by `ApplySuccessfulPaymentAllocation` stopping at item balances (`:4172-4181`). A genuine **amount mismatch** (provider amount ≠ stored amount, not merely larger) is quarantined to `AmountMismatch` (`:3823-3826`) and sent to suspense as above. **Flag:** there is no explicit "overpayment" category or dedicated overpayment suspense bucket beyond the generic `UnresolvedOnlinePayment` suspense message; over-vs-under is not distinguished in code — any provider-amount divergence is treated as a mismatch.

---

## Gaps / flags (missing evidence)
1. **Chargeback** has no storefront `Order` handling in `PaymentLifecycleService` (only CommerceHub runtimes). A storefront chargeback state transition could not be located.
2. **CommerceHub `RecordRefundAsync`** is a separate API (different service/overload) from the storefront lifecycle; the two should not be conflated.
3. The interceptor's "ONLY mutator" guarantee applies to `Order` specifically; `CommerceHub*` `PaymentStatus` writes bypass it by being non-`Order` entities.
4. The actual journal-entry GL leg (DEBIT/CREDIT account mapping named in enum comments) lives in `CommerceAccountingService`/`ICommerceAccountingService`, outside the requested files; only its call sites and outbox wiring are evidenced here.
5. No dedicated "overpayment" suspense bucket — amount mismatch (any direction) is the only suspense trigger for captured funds.

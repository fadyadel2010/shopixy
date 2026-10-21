# Manual Wallet Clearing → Main Cashbox — Remediation Report (2026-10-21)

## A. Executive verdict

**PASS** for the implemented scope, with two documented limitations in section G.

A dedicated, authoritative **Manual Wallet Settlement** workflow now exists end to end: a Storefront Manual Wallet order was created and approved through the real Admin UI, produced its Wallet Clearing (`1140`) approval journal, was then settled through a new dedicated Admin screen, and posted a balanced `Dr 1010 Main Cashbox / Cr 1140 Wallet Clearing` journal exactly once. Replay, concurrent confirmation, already-settled, foreign-tenant and pending-payment attempts all failed safely with no duplicate or partial financial posting. Existing Manual COD settlement behaviour is unchanged and remains an independent workflow. All 306 executed focused backend tests, 18 Admin Treasury tests, 29 Admin payment regressions and 70 Storefront checkout tests passed.

This verdict covers the **focused selection** executed for this task. The full repository test suite was **not** executed and is not claimed.

---

## B. Baseline

### Workspace, processes, tenancy

| Item | Value |
|---|---|
| Workspace | `C:\Users\fadya\source\repos\PointOfSale` (primary dev machine; tester machine never accessed) |
| Development API | `PointOfSale.API` on `http://localhost:5010` (restarted by the user after the rebuild) |
| Admin web | Vite `http://localhost:5173` |
| Storefront | Vite `http://ahramstore.localhost:5174` (AhramStore; Nissan was not used) |
| Configured DB | PostgreSQL `192.168.1.5:5432/PosEcommerceDb_Baseline` (from `appsettings.Development.local.json`) |
| Tenant | **AhramStore**, `c9c5c1ae-949e-498e-bc63-07242daeb49d`, currency EGP |
| Authenticated actor | Owner `1bb114d3-a9a3-4a9d-ace3-3690361637a8` (credentials supplied by the user; intentionally not reproduced here) |
| Browser | Registered headed Edge session `opencode-ui-test` (per `AGENTS.md`; no new orphan window) |

### Canonical chart of accounts (authoritative SQL, not screenshots)

| Role | Code | Ledger account ID | Balance before task |
|---|---|---|---:|
| Default Main Cashbox | 1010 | `01a0828d-734b-7385-9659-ef041fa3e836` | 107.35 |
| COD Clearing | 1130 | `01a0828d-734b-7f91-a220-8757c0148304` | 40.00 |
| Wallet Clearing | 1140 | `01a0828d-734b-7b72-825c-86d0a85d8c2b` | 3,390.00 |
| E-commerce Sales | 4020 | `01a0828d-734b-706b-b44b-b4eb15863502` | 3,527.35 (credit) |

Treasury default Main Cashbox mapping `01a0828d-7502-71c0-b3d5-d0cd09fcbb42` → account `1010`; `1130`/`1140` are active clearing Assets; `1010` is a non-clearing Asset. Ledger line sums equal stored balances for all four accounts.
Evidence: `baseline.json`, `balances-before.json`.

The screenshot totals in the brief (45,025 / 15,000 / 30,025) are **not** this tenant's current totals and were not hardcoded anywhere.

### `ORD-F577-2026-01039`

Exact read-only SQL lookup returned **no row in this configured database**. Its approval/history therefore cannot be verified here, and it was **not** altered, settled, or used as a substitute. This is recorded as a limitation, not worked around.

### Existing wallet approval accounting (verified, unchanged)

`PaymentLifecycleService` → `CommerceAccountingService.PostWalletApprovedAsync`:
- SourceType `Order_Wallet`, idempotency `WALLET_APPROVED:{tenantId}:{orderId}`
- `Dr 1140 Wallet Clearing / Cr 4020 E-commerce Sales` for the whole order total
- Order → `PaymentStatus Collected`, `FinancialPostingStatus Posted`, journal id stored on the order; intent stamped `Paid`, `VerifiedAtUtc`, `VerifiedByAdmin`, `CapturedAt`
- **No** cashbox journal is produced by approval.

The approval journal was **not** modified by this task.

### Existing Manual COD settlement architecture (reused, not duplicated)

`PaymentLifecycleService.ManualCodSettlement.cs` + `ManualCodSettlementAuthority` + `FinancialPostingService` source validation: dedicated endpoints, `storefront.payments.manage` + Owner/Admin, SQL actor/tenant revalidation, sorted order row locks, Serializable transaction, whole-order permanent `PaymentSettlementOrders` allocations, atomic journal/balance/order/outbox commit, durable replay identity, read-only receipt recovery, posted-destination refund lineage.

---

## C. Root cause and implementation

### Confirmed gaps

1. **No wallet settlement executor.** Wallet approval produced `1140` balances, but no authoritative operation could move them to `1010`. Clearing Aging offered only a disabled "Provider workflow" button, and its boundary text asserted that clearing settlement was unavailable.
2. **Aging mis-stated wallet age.** `FinancialReportService.GetClearingAgingAsync` counted *all* debit and credit lines of a clearing account and aged the *oldest* line, so historic settled receipts kept `1140` looking late and inflated transaction counts.
3. **Settled wallet payments re-entered the approval queue.** `AdminManualPaymentController.GetPendingPayments` matched any `Paid` intent whose order payment status was not `Collected`, so a *settled* wallet order would reappear as a pending approval.

### Implemented (source changes)

**Core**
- `PointOfSale.Core/Interfaces/Payments/ManualWalletSettlement.cs` *(new)* — request/result/selection/preview DTOs; operator selection only, no money/tenant/account fields.
- `PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs` — wallet settlement contract.
- `PointOfSale.Core/Interfaces/Accounting/ICommerceAccountingService.cs` — `PostManualWalletSettlementAsync`.

**Infrastructure**
- `PointOfSale.Infrastructure/Accounting/ManualWalletSettlementAuthority.cs` *(new)* — SQL authority: resolves the canonical default Main Cashbox (via the shared `ManualCodSettlementAuthority.ResolveMainCashboxAsync`) and `1140`; validates tenant/account/currency/amount, a **single accepted** `ManualWallet` `Paid` intent with verification stamps, the posted balanced `Order_Wallet` approval journal (Dr 1140 / Cr 4020), the **positive whole outstanding `1140` balance for that source**, no refund, no prior allocation, no reversal, and builds the exact Dr 1010 / Cr 1140 posting request.
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.ManualWalletSettlement.cs` *(new)* — the lifecycle owner: eligibility list, server preview with an opaque snapshot token, and the settlement operation (Serializable transaction, sorted order locks, permanent `PaymentSettlementOrders` allocation with `PaymentIntentId`, journal + outbox receipt + `Collected → Settled` in one commit, replay by operation identity, conflict mapping).
- `PointOfSale.Infrastructure/Accounting/ManualCodSettlementAuthority.cs` — refactor only: `ResolveMainCashboxAsync` extracted so both workflows share one cashbox authority. COD behaviour unchanged.
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs` — `PostManualWalletSettlementAsync` (delegates to the established posting engine).
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs` — independent re-validation of any `ManualWalletSettlement` posting against its SQL source authority (source/reference/key/user/date/lines must match exactly), plus cash-inflow authorization.
- `PointOfSale.Infrastructure/Accounting/SettlementRefundLineage.cs` — wallet settlement batches join the refund-destination lineage contract (actual posted debit account, `1140` credit, accepted intent identity, immutable actor/key).
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs` — `ManualWalletSettlement` is a critical financial truth event and has a read-only receipt-recovery handler.
- `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs` — wallet aging now allocates posted settlement credits back to their original receipts, reconciles against the authoritative ledger net, and excludes settled/reversed sources from count, age and buckets. COD aging untouched.

**API**
- `PointOfSale.API/Controllers/PaymentLifecycleController.cs` — `GET /api/payments/wallet/manual-settlements/eligible-payments`, `POST .../preview`, `POST .../manual-settlements` (FinancialWrite rate limit), `GET .../manual-settlements/{settlementId}`; strict `[JsonUnmappedMemberHandling(Disallow)]` request records so caller-supplied money/currency/tenant/destination fields are rejected at the contract boundary.
- `PointOfSale.API/Controllers/AdminManualPaymentController.cs` — paid-intent recovery now only matches an *incomplete* approval (`Pending`/`AwaitingVerification`), so settled/refunded wallet payments no longer reappear for approval.
- `PointOfSale.API/Security/PosEndpointPermissionCatalog.cs` — the four new wallet endpoints require `storefront.payments.manage` (Owner/Admin).

**Admin web**
- `PointOfSale.Web/src/pages/treasury/TreasuryManualWalletSettlement.jsx` *(new)* + `.d.ts` + `.test.tsx` — dedicated screen: server-read balances, eligible list (order, intent reference, customer, amount, unsettled amount, approval time/age, currency, blocked reason), server-verified posting preview, explicit receipt attestation, truthful success with journal reference, session-persisted operation identity for safe retry/recovery, honest failure messages that never authorize a second posting.
- `PointOfSale.Web/src/services/manualWalletSettlementApi.js` *(new)*.
- `PointOfSale.Web/src/pages/treasury/TreasuryClearingAging.jsx` — `Manual Wallet Settlement` action (header + per-`1140`-row) and corrected boundary wording; COD button and provider workflow unchanged.
- `PointOfSale.Web/src/App.jsx`, `components/Sidebar.jsx`, `pages/treasury/index.js` — route `/treasury/manual-wallet-settlements` (permission-guarded) and a Treasury navigation entry.
- `PointOfSale.Web/src/i18n/locales/en.json` / `ar.json` — `manualWalletSettlement.*` (English **Manual Wallet Settlement** / Arabic **تسوية المحفظة الإلكترونية**) and the boundary texts.

**Tests**
- `PointOfSale.Tests/Integration/ManualWalletSettlementTests.cs` *(new)* — 31 focused SQL-backed cases.
- `PointOfSale.Tests/Security/PosPermissionAuthorizationTests.cs` — new role-matrix case for all four wallet endpoints.

### Eligibility and authorization rules

Requires: same tenant; `MANUALWALLET` method; order `Collected` + `IsPaid` + `PaidAt`; exactly one accepted `Paid` `ManualWallet` intent whose amount/currency match the order and which carries `VerifiedAtUtc` + `VerifiedByAdmin` + `CapturedAt`; posted `FinancialPostingStatus`; exactly one posted balanced `Order_Wallet` journal (Dr 1140 / Cr 4020); positive whole outstanding `1140` balance for that source; no prior settlement allocation; no refund; no reversal; order not cancelled/refunded/on hold; currency equal to the tenant currency; whole-payment settlement only.

Excluded: pending/unapproved, rejected, voided/cancelled intents, unposted approval, unbalanced or reversed approval journals, refunded orders, already-settled payments, zero balances, other tenants' orders.

Wallet eligibility deliberately does **not** require delivery — COD's delivery prerequisite is a COD collection rule, not a wallet rule.

Authorization: `storefront.payments.manage` at the endpoint (Owner/Admin) **and** an independent SQL re-check of an active same-tenant Owner/Admin actor inside the settlement transaction. Tenant comes from the server-side tenant context only.

Atomicity/idempotency: Serializable transaction with sorted order row locks; batch + allocation + journal + order status + outbox receipt commit together; a failed attempt leaves no batch, allocation, journal, outbox row or status change; the same operation identity replays to the original result (`wasIdempotent=true`) instead of posting again; a stale preview token or changed balance returns `409 WALLET_MANUAL_BALANCE_CHANGED`.

No schema/migration change was required — the existing `PaymentSettlementBatches` / `PaymentSettlementOrders` (with `PaymentIntentId`) / `LedgerJournalEntries` / `LedgerLines` / `AccountingOutboxMessages` model was reused.

**No Sandbox or provider setting was changed by this task.** The Manual Wallet method's *tenant* recipient was temporarily changed through the supported Admin UI for the test and restored afterwards (section F).

---

## D. Financial proof

### D.1 Primary synthetic wallet order

| Item | Value |
|---|---|
| Order | `ORD-C9C5-2026-01088` / `251e3da8-df21-48b3-9da7-5d0a9ff29e5f` |
| Customer | `MWSET20261021-PRIMARY TEST ONLY` (Storefront guest) |
| Total | **EGP 35.00** (subtotal 25.00 + shipping 10.00, no coupon, no tax) |
| Payment intent | `cc610cae-7a84-44b0-a323-294163050eb7`, reference `PAY-20261021125216-251E3DA8`, provider `ManualWallet` |
| Proof | PNG containing "TEST-ONLY — NOT A REAL RECEIPT … NO real funds transferred" |

**Approval (Admin UI → `POST /api/admin/payments/manual-wallet/{id}/confirm` → 200 "Payment confirmed successfully")**

- Journal `JE-2026-000038` (`01a15e87-8da4-739e-94e6-5e40c8e400ae`), SourceType `Order_Wallet`, key `WALLET_APPROVED:c9c5c1ae-…:251e3da8-…`
- `Dr 1140` 35.00 (balance after 3,425.00) / `Cr 4020` 35.00 (balance after 3,562.35) — balanced, tenant-correct
- Order → `Collected`, `FinancialPostingStatus Posted`, `FinancialPostingJournalId` set; intent → `Paid` + `VerifiedAtUtc` + `VerifiedByAdmin`
- **No** Main Cashbox entry was created by approval (`1010` stayed 107.35)

**Settlement (Admin UI → `POST /api/payments/wallet/manual-settlements` → 200)**

- Batch `0bd4a7a0-3a97-4400-9020-e8617bfb8867`, `SettlementType MANUAL_WALLET_COLLECTION`, `SettlementId fb337683-3208-4112-bd90-c5d9ecdc905f`, gross 35.00, fees 0, EGP, `CreatedBy` = Owner actor
- Allocation `3fcc2a58-…`: `OrderId 251e3da8-…`, `PaymentIntentId cc610cae-…`, `OrderTotal 35.00`, EGP
- Journal **`JE-2026-000039`** (`01a15e88-c240-7aab-a502-fbeaf0d05b50`), SourceType `ManualWalletSettlement`, key `WALLET_MANUAL_SETTLEMENT:c9c5c1ae-…:fb337683-…`
  - `Dr 1010 Cashbox Main` **35.00** (balance after 142.35)
  - `Cr 1140 Wallet Clearing` **35.00** (balance after **3,390.00**)
  - Two lines, equal amounts, same tenant, same currency, posted, `CreatedByUserId` = the Owner actor
- Outbox receipt `ManualWalletSettlement` processed, pointing at the same journal

**Balances (authoritative readback)**

| Account | Before task | After approval | After wallet settlement |
|---|---:|---:|---:|
| 1010 Main Cashbox | 107.35 | 107.35 | **142.35** |
| 1140 Wallet Clearing | 3,390.00 | 3,425.00 | **3,390.00** |
| 1130 COD Clearing | 40.00 | 40.00 | 40.00 |
| 4020 E-commerce Sales | 3,527.35 | 3,562.35 | 3,562.35 |

`1140` returned to exactly its pre-task value (the +35 receipt and its −35 settlement offset), `1010` rose by exactly 35.00, and revenue `4020` rose by 35.00 **only at approval** — settlement recognized no second sale, no second payment capture and no stock movement.

**History separation:** the order detail screen shows payment status `Settled` while the shipment status is still `Pending` and the order is `Processing` — the approval and the later treasury settlement are separate recorded events.

### D.2 Second synthetic wallet order (concurrency)

`ORD-C9C5-2026-01089` / `c794d62c-3a44-4cbf-b7b2-aab9e16a49f4`, EGP 35.00, intent `55ecc6d0-d468-4612-8596-f6c47da029e0`.
Approval `JE-2026-000040` (`Dr 1140` / `Cr 4020` 35.00). Two Admin browser tabs confirmed the settlement simultaneously: one `200` → `JE-2026-000041` (`Dr 1010` 35.00 / `Cr 1140` 35.00, balance after 177.35), one `409 WALLET_MANUAL_ALREADY_SETTLED`. **Exactly one** wallet settlement journal and one allocation exist for this source.

### D.3 COD regression synthetic order

`ORD-C9C5-2026-01090` / `026bd21d-f083-4eee-8e8c-bc2e7cea0fb5`, EGP 35.00, COD.
Delivered through the real Admin lifecycle → collection evidence → `JE-2026-000042` (`Order_COD`, `Dr 1130` / `Cr 4020` 35.00, `1130` → 75.00) → Manual COD Settlement screen → `JE-2026-000043` (`ManualCodSettlement`, `Dr 1010` 35.00 / `Cr 1130` 35.00, `1130` → **40.00**, key `COD_MANUAL_SETTLEMENT:…`). The COD screen listed only the COD order; the wallet screen listed only wallet orders; neither contained the other's entries.

### D.4 Final state of the tenant

| Account | Final balance | Pre-existing history |
|---|---:|---|
| 1010 Main Cashbox | 212.35 | preserved (107.35 + 2×35 wallet + 35 COD, all from new synthetic sales) |
| 1130 COD Clearing | 40.00 | **unchanged** |
| 1140 Wallet Clearing | 3,390.00 | **unchanged** |
| 4020 E-commerce Sales | 3,632.35 | +105.00 = 3 new sales recognized once each |

No pre-existing clearing balance was settled, altered, or written off.

---

## E. Browser and automated evidence

All UI evidence is from the registered **headed Edge** session `opencode-ui-test` against the updated application on the configured development database. Evidence files live in `.ai/evidence/manual-wallet-settlement-20261021/`.

### Scenario A — approval creates the clearing balance
| Step | Evidence |
|---|---|
| Wallet method enabled, recipient configured through Admin UI | `wallet-qa-recipient.png`, `wallet-configuration-before.yaml` |
| Storefront checkout with Manual Wallet + Vodafone Cash + proof upload | `primary-checkout-review.png`, `TEST-ONLY-NOT-A-REAL-RECEIPT.png` |
| Storefront pending state | `primary-storefront-pending.png` |
| Payment pending in Manual Payments, not approved | `pending-negative-response.json` (pending order is **not** a settlement candidate) |
| Proof reviewed in Admin | `primary-proof-reviewed.png` |
| Approval through Admin UI | `primary-approval-response.json` (200) |
| `1140` journal + balance | `primary-after-approval.json`, `balances-after-primary-approval.json` |
| **No** automatic cashbox settlement | `1010` unchanged at 107.35 after approval |
| Clearing Aging shows the new wallet receipt | `aging-after-primary-approval.png` (3,425.00 / 11 txns) |

### Scenario B — settlement through the actual UI
| Step | Evidence |
|---|---|
| Clearing Aging → `Manual Wallet Settlement` | `aging-after-primary-approval.png` |
| Dedicated screen, candidate, source `1140`, destination `1010`, server amount | `primary-wallet-posting-preview.png`, `primary-preview-response.json` |
| Explicit attestation + confirm | `primary-settlement-response.json` (200, `JE-2026-000039`) |
| Truthful success with journal reference | `primary-wallet-success-banner.png` |
| Authoritative balances | `balances-after-primary-settlement.json` |
| Settled entry no longer eligible | `wallet-settlement-final-eligible.png` (`ORD-C9C5-2026-01088` absent) |
| Ledger readback of both accounts | `main-cashbox-ledger.png`, `wallet-clearing-ledger.png` |
| Cannot settle again | `primary-already-settled-response.json` (409 `WALLET_MANUAL_ALREADY_SETTLED`) |
| Replay of the identical request | `primary-replay-response.json` (200, `wasIdempotent=true`, same journal) |
| Order history keeps approval and settlement separate | `primary-order-settled-separate-fulfillment.png` |

### Scenario C — COD regression
| Step | Evidence |
|---|---|
| COD order created and delivered through the real Admin lifecycle | `cod-before-delivery.json` → `cod-after-checkout.json` |
| COD collection evidence recorded | `cod-order-collected-in-ui.png` |
| COD settlement screen lists only the COD order | `cod-settlement-screen.png` |
| COD settlement posts Dr 1010 / Cr 1130 | `cod-settlement-response.json` (`JE-2026-000043`), `cod-settlement-success.png` |
| `1130` restored to 40.00 | `balances-after-cod-settlement.json` |

### Scenario D — negative and concurrency cases
| Case | Result | Evidence |
|---|---|---|
| Settle a pending/unapproved wallet payment (preview + settle) | `409 WALLET_MANUAL_APPROVAL_REQUIRED`; not a candidate | `pending-negative-response.json` |
| Replay the same settlement request after success | `200 wasIdempotent=true`, same journal, no new journal | `primary-replay-response.json` |
| New operation identity against an already-settled source | `409 WALLET_MANUAL_ALREADY_SETTLED` | `primary-already-settled-response.json` |
| Two concurrent/stale confirmations for the same source | one `200`, one `409`; exactly one journal | `concurrent-wallet-responses.json`, `concurrent-wallet-tab-*.png` |
| Foreign-tenant order id in a wallet settlement | `409 WALLET_MANUAL_ORDER_NOT_FOUND` | `foreign-source-response.json` |
| Foreign-tenant context on a settlement read | `403 TENANT_FORBIDDEN` | `foreign-context-read-response.json` |
| Wallet/COD cross-selection | rejected atomically; no partial posting | `WalletAndCod_AreIndependent_AndMixedSelectionCannotPartiallyPost` |
| Post-commit read failure after a committed settlement | committed outcome reported truthfully; no repeat | `SettledPayment_DoesNotReappearInManualApprovalQueue`, Admin UI test "keeps posted success truthful…" |

After every failure the authoritative records were re-read (`after-*.json` files) — no extra batch, allocation, journal or outbox row was produced.

### Automated tests, builds, typechecks, lint

| Gate | Result |
|---|---|
| Backend focused selection (306 tests): wallet settlement, COD settlement, COD lifecycle, COD settlement lineage, e-commerce refunds, POS permission authorization, commerce accounting, financial reports, accounting outbox, treasury settlement authority, T-10 containment, manual wallet concurrency, manual payment foundation contract | **306 / 306 passed**, 0 failed, 0 skipped (`backend-regression-final.trx`) |
| Wallet + authorization focused selection (78 tests) | **78 / 78 passed** (`wallet-and-authorization-final.trx`) |
| Admin Treasury tests (new wallet screen + existing COD + General Ledger) | **18 / 18 passed** (`admin-tests.json`) |
| Admin Payment Methods / Manual Payments regressions | **29 / 29 passed** (`admin-payment-regressions.json`) |
| Storefront checkout payment-selection / session / closeout | **70 / 70 passed** (`storefront-tests.json`) |
| Backend build (`PointOfSale.API`) | succeeded, 0 errors (warnings pre-existing) |
| Admin build (`vite build`) | succeeded (existing chunk-size warning only) |
| Storefront build (`tsc && vite build`) | succeeded (existing circular-chunk warnings only) |
| Admin typecheck (`npx tsc --noEmit`) | clean |
| Focused lint on changed Admin files | **0 errors**, 4 warnings — all pre-existing in `App.jsx`, `Sidebar.jsx`, `TreasuryClearingAging.jsx` |
| Full repository test suite | **not executed** — no full-certification claim is made |

New backend coverage (31 cases in `ManualWalletSettlementTests.cs`) proves: approval→`1140` without any cashbox entry; balanced Dr 1010 / Cr 1140 settlement; no second revenue recognition; `1140` falls by exactly the settled amount and `1010` rises by it; clearing aging totals/buckets update; pending/rejected/voided/cancelled/refunded/unposted/reversed/unbalanced/intent-amount/zero/wrong-currency/COD/foreign-tenant sources are all ineligible and atomic; stale preview, already-settled and identity collision fail closed; replay converges; concurrent confirmations produce at most one journal; posting failure and outbox failure both roll back everything and the same request then succeeds; actor role is revalidated in SQL; strict DTO rejects caller money/tenant/destination; settled payments leave the approval queue; wallet and COD remain independent.

---

## F. Preservation and cleanup

- **Original/final Manual Wallet configuration:** before the task the method was *Enabled / Valid* with stored recipient `01206203408` (`wallet-configuration-before.yaml`). For the test it was disabled, re-enabled, and temporarily set to the synthetic `01000000000`; afterwards the recipient was **restored to `01206203408`** and the method left **Enabled / Valid** — the original state (`wallet-recipient-restored.png`). No synthetic recipient remains.
- **Synthetic records retained** (safe rollback is not supported by the accounting model, and the task requires preserved QA journals): orders `ORD-C9C5-2026-01088` (wallet, settled), `ORD-C9C5-2026-01089` (wallet, settled), `ORD-C9C5-2026-01090` (COD, settled); intents `cc610cae-…`, `55ecc6d0-…`; batches `0bd4a7a0-…` and `a96f4b8d-…` (`MANUAL_WALLET_COLLECTION`), `4b89cae9-…` (`MANUAL_COD_COLLECTION`); journals `JE-2026-000038`, `JE-2026-000039`, `JE-2026-000040`, `JE-2026-000041`, `JE-2026-000042`, `JE-2026-000043`; two outbox receipts. All are explicitly TEST-ONLY labelled.
- **Existing clearing balances and history preserved:** `1130` = 40.00 and `1140` = 3,390.00 are exactly their pre-task values; no pre-existing order, intent, journal, allocation, outbox row, coupon, price, stock quantity or customer record was modified. The pre-existing pending wallet order `ORD-C9C5-2026-01087` ("MWPARITY QA") was left pending and untouched.
- **No real external action:** no Vodafone Cash / Instapay transfer, no provider charge, no carrier booking, no external API integration, no DB migration/reset/restore. The confirmation is an explicit internal QA attestation of the configured treasury movement, not a real-world bank or wallet transaction, and the UI states this.
- **Git:** nothing staged or committed; no `reset`, `clean`, `stash`, checkout-discard or broad restore. The pre-existing dirty working tree (Manual Payment foundation work, smart-navigation/localization work) was preserved untouched; only the files listed in section C were modified or added. The two throwaway test build-output folders this task created under `.artifacts/` were removed; the pre-existing `.artifacts/manual-wallet-visibility-20261020` was left alone.
- **Read-only SQL evidence:** a dedicated helper (`read_sql.py`) connects with `default_transaction_read_only=on` and never prints connection credentials. All configured-database writes were performed through the application's own APIs and Admin UI.

---

## G. Remaining limitations

1. **`ORD-F577-2026-01039` does not exist in the configured development database.** Its approval/history could not be inspected, and the brief's screenshot totals do not match this tenant. No other database, tenant, machine or pre-existing order was substituted.
2. **The full repository test suite was not executed.** Certification covers the focused selection listed in section E only. The focused selection is broad (306 backend tests spanning wallet/COD settlement, accounting, outbox, refunds, authorization and treasury authority) but is not a full-repository pass.
3. **Two browser-checkout response captures failed for tooling reasons** (language query string on the request URL, and a navigation that discarded an already-read response body). Neither was retried as a submission — SQL and UI confirmed exactly one committed order per attempt — and the collectors were corrected afterwards. Evidence is retained as `*_capture_error.txt`.
4. **Storefront checkout still labels the Manual Wallet method "Pay on Delivery"** (pre-existing display/description text from earlier work). It is cosmetic and outside this task's scope, but it is misleading for a wallet method and was deliberately not changed here.
5. **Clearing Aging for `1130`/`1140` still ages the oldest historic line** for accounts other than `1140`; the allocation-aware ageing implemented in this task is scoped to Wallet Clearing, where the settlement-allocation lineage exists. COD ageing was intentionally left exactly as-is to avoid changing established COD behaviour.
6. **Real-world settlement is operator-attested.** The workflow records that the operator observed the funds in Main Cashbox; it does not and cannot verify an actual Vodafone Cash/Instapay receipt, and it must not be read as an automated provider remittance.

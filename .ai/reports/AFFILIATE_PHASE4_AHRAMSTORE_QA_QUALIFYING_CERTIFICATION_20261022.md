# Affiliate Phase 4 — AhramStore QA qualifying certification (synthetic)

Date: 2026-10-22. Evidence root: `.ai/evidence/affiliate-phase4-ahramstore-20261022/`.
Runtime: local Windows development workspace, headed Edge (Playwright session `opencode-ui-test`),
POS Admin `http://localhost:5173`, Storefront `http://ahramstore.localhost:5174`, Affiliate Portal
`http://localhost:5173/affiliate`. Database: authorized development host `192.168.1.5`,
database `PosEcommerceDb_Baseline` (identity confirmed by the running application, not assumed).

## 1. Executive verdict

**QUALIFIED — synthetic QA certification only.** Two new AhramStore orders were created through the
real Storefront referral link, reached **authoritative Earned and Approved commission** through the
existing Phase 4 commission service, and the Affiliate Portal balance agrees with the authoritative
SQL ledger with **no duplicate financial effects**.

This is a **synthetic QA certification, not a real-world delivery or payment**. No customer paid,
no cash was received, no carrier was called, no wallet transfer was made, no payout or withdrawal
was requested or executed. The two test orders are permanently labelled
`SYNTHETIC QA — NO REAL DELIVERY OR MONEY` in their own order notes, in their QA evidence rows, in
their virtual commission journals, and in the Admin order drawer.

The previous Phase 4 closure report (`.ai/reports/AFFILIATE_PHASE4_LIVE_QUALIFYING_CERTIFICATION_CLOSURE_20261021.md`)
remains the record of the earlier **BLOCKED** state. This report supersedes it only for the
qualification outcome; it does not rewrite that history.

## 2. Identity and baseline authority

| Item | Value | Evidence |
|---|---|---|
| POS Admin session | `ahramco.software@gmail.com`, Owner, tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d` (AhramStore) | `browser-authority` session read, `affiliate-baseline.txt` |
| Global affiliate | `fadyadel2060@gmail.com`, account `01a03e11-e234-7d40-b77e-99f9463c7a40`, referral `MEMOXP155504` | `affiliate-baseline.txt` |
| Storefront customer | `fadyadel2060@gmail.com`, OnlineCustomer `3e6129bf-5bac-4d26-86da-c7c42cb53833` | `wallet-created.json`, Storefront `/api/storefront/auth/me` |
| Product | `CERT Media Simple 20261007` (`15e044bc-eaa1-c905-31a4-658aac4ad14b`), AhramStore, EGP 25.00, published, in stock (5) | `baseline.json`, `affiliate-baseline.txt`, Storefront product page |
| Commission policy | `01a1601c-d03a-7df8-bdae-9b4b8fddee6c`, rate **0.10**, cap **null**, enabled | `baseline.json` |
| Promotion link | `01a16027-13af-76aa-b066-57d65cff9240`, code `LF9V1HCEQ3`, created in the headed portal via **Promote** | `affiliate-baseline.txt` |
| Payment methods | `COD` enabled; `MANUALWALLET` (`P5QA20261017-817 TEST ONLY Wallet`) enabled; Stripe/Paymob untouched and disabled | `baseline.json`, `runtime-final.json` |

**No product, policy, price, stock, payment-method or promotion-link configuration was modified.**
The product and policy are the pre-existing eligible ones the user supplied; only the promotion link
was created, through the supported Affiliate Portal "Promote" action.

Baseline affiliate position before this task: `totalApproved = 0`, `availableForWithdrawal = 0`,
`totalEarned = 80`, `totalReversed = 80`, `history = []` (`affiliate-baseline.txt`).

## 3. Implementation added (smallest safe correction)

The Phase 4 commission engine, ledger, recognition and reversal logic were **not** rewritten. The
missing pieces were (a) a trusted delivery attestation that the canonical fulfillment/commission
authority can consume, and (b) a way to exercise collection without real money.

### 3.1 Development-only synthetic QA certification authority

New: `PointOfSale.Infrastructure/Services/SyntheticQaCertificationAuthority.cs`,
`PointOfSale.Core/Entities/SyntheticQaCertification.cs`,
`PointOfSale.Infrastructure/Data/Configurations/SyntheticQaCertificationConfiguration.cs`,
`PointOfSale.API/Controllers/SyntheticQaController.cs`,
`PointOfSale.Infrastructure/Payments/PaymentLifecycleService.SyntheticQa.cs`,
`PointOfSale.Infrastructure/Consumers/Affiliate/SyntheticQaCollectionAffiliateConsumer.cs`,
`PointOfSale.Core/Events/SyntheticQaCollectionRecordedEvent.cs`.

Durable audit tables (migration `20261021203055_SyntheticQaAffiliateCertification` +
`20261021221817_SyntheticQaPreFulfillmentCollection`, additive only): `SyntheticQaRuns`,
`SyntheticQaOrders`, `SyntheticQaEvidence`, `SyntheticQaCommissionPostings`.

Protections actually enforced (all covered by executable tests in
`PointOfSale.Tests/Affiliate/SyntheticQaCertificationTests.cs` and
`SyntheticQaDatabaseBoundaryTests.cs`):

- **Disabled by default**; opt-in only through the local `SyntheticQa` configuration section.
- **Fail closed outside Development/Test** runtime, and for a database whose *actual* connected
  identity (`current_database()`, `host(inet_server_addr())`) differs from the approved one.
- **Explicitly authorized QA verifier only.** Merchant ownership alone is not authority: with the
  verifier allowlist empty, the same Owner session received HTTP 403
  `SYNTHETIC_QA_VERIFIER_REQUIRED` (`ordinary-owner-not-verifier.txt`).
- **Only new orders created inside the current authorized run** are admitted: run binding, tenant,
  customer, product, promotion link, quantity, payment method, manual fulfillment mode, creation
  timestamp inside the run window, and the exact synthetic order-note marker are all validated.
- **The four protected Phase 3/4 orders are structurally rejected** (IDs and order numbers are
  hard-denied, and are additionally covered by a dedicated test).
- Evidence is **server-issued, HMAC-signed, durable before consumption, nonce-bound**, and
  **immutable** (rows cannot be deleted; only one-way `ConsumedAtUtc` may be set, enforced in the
  DbContext save guard). Collection evidence is order-bound; delivery evidence additionally binds to
  the **original manual shipment**.
- The **production delivery guard was not weakened**: `RequireVerifiedManualDeliveryEvidence` stays
  `true`, `AllowManualDeliveryEvidenceOverride` stays `false`, and a typed operator tracking number
  or proof URL is still insufficient. An ordinary manual delivery attempt with operator-typed proof
  on the attributed order was **rejected** (`untrusted-proof-rejected.txt`).
- Real carrier trust rules are unchanged; no carrier was called.

### 3.2 Synthetic collection that cannot move money

`PaymentLifecycleService.RecordSyntheticQaCollectionAsync` runs inside the canonical payment
lifecycle service. It uses the **authoritative order amount and currency**, keeps existing payment
eligibility and reservation validation, and commits the reservation. It deliberately:

- sets a distinct payment status `SyntheticQaCollected` (never `Collected`/`Paid`),
- **never** allocates `PaidAmount`, never sets `IsPaid`/`PaidAt`, never touches Treasury or the
  general ledger, never creates a real journal,
- writes a separate **virtual** journal row (`QA-VIRTUAL-AFFILIATE-EXPENSE` /
  `QA-VIRTUAL-AFFILIATE-PAYABLE`) in `SyntheticQaCommissionPostings`,
- posts **no** real accounting outbox row (authoritative readback: `AccountingOutboxMessages` count
  for both QA orders = 0, and `LedgerJournalEntries` / `LedgerLines` totals unchanged at 220 / 459).

The real-GL commission consumer (`AffiliateCommissionEarnedAccountingConsumer`) explicitly detects a
QA order and posts only to the virtual journal, so even delayed broker delivery after the QA opt-in
is disabled cannot post fictitious money to the real ledger.

### 3.3 Normal Admin Orders read contract (production fix)

`/ecommerce/orders` could not display the synthetic orders, and the drawer failed with
*"Orders response could not be verified. Please retry or sign in again."* Two real defects were
fixed rather than worked around:

1. **Backend**: the Admin order list excluded wallet orders unless they were `Collected`/`Settled`/
   `Refunded`/`PartiallyRefunded`, so a QA order was invisible; and the list `UNION` failed to
   translate because the new `IsSyntheticQa` column was only assigned on the Storefront side of the
   set operation (`Unable to translate set operations when both sides don't assign values to the
   same properties`). Both sides now assign the same properties, and QA orders are admitted by
   durable `SyntheticQaOrders` provenance rather than by weakening the ordinary wallet rule.
2. **Frontend**: the lifecycle authority now admits the exact `SyntheticQaCollected` enum **only**
   for a non-Marketplace, `source === 'Ecommerce'` order whose backend says `isSyntheticQa === true`.
   Unknown future payment values, Marketplace rows and non-provenanced rows are still rejected.

The Admin drawer now shows the order normally with an explicit
**"Synthetic QA collection — NO MONEY — SYNTHETIC QA — NO REAL DELIVERY OR MONEY"** provenance note
(`normal-admin-wallet-fixed.yml`, `normal-admin-cod-completed-qa-disabled.yml`). The QA panel was
used **only** to issue the restricted synthetic evidence; all order lifecycle work was then driven
from the **normal** Admin Orders drawer and the normal fulfillment API.

## 4. The qualifying orders

Both orders used the **real referral URL**
`http://ahramstore.localhost:5174/products/cert-media-simple-20261007?ref=LF9V1HCEQ3` in a fresh
Storefront customer session, standard shipping (EGP 10.00), and normal checkout.

| | Order A (wallet) | Order B (COD) |
|---|---|---|
| Order number | `ORD-C9C5-2026-01096` | `ORD-C9C5-2026-01097` |
| Order ID | `d2f9967a-80a7-4e5a-859f-016e018f198b` | `bee1f561-e55f-4f2c-816c-bb8193427edc` |
| Payment method | `MANUALWALLET` (test-only wallet) | `COD` |
| Subtotal / shipping / tax / discount | 25.00 / 10.00 / 0.00 / 0.00 | 25.00 / 10.00 / 0.00 / 0.00 |
| **Order total** | **EGP 35.00** | **EGP 35.00** |
| Commission base (item total) | EGP 25.00 | EGP 25.00 |
| Captured rate / cap | 0.10 / none | 0.10 / none |
| **Commission** | **EGP 2.50** | **EGP 2.50** |
| Attribution | `01a16089-d0f1-72b8-aaff-121b502aa0cb` | `01a16095-a936-73ac-a7b2-5cb906ffa978` |
| Promotion link / affiliate | `01a16027-…` / `01a03e11-…` | same |
| Item / snapshot | item `3802e66c-…`, rate 0.1, base 25.00, amount 2.50, cap null, policy `01a1601c-…` | item `037f2545-…`, same terms |
| Final order / fulfillment status | Completed / Completed | Completed / Completed |
| Final payment status | `SyntheticQaCollected` (13) | `SyntheticQaCollected` (13) |
| `IsPaid` / `PaidAt` / paid amount | false / null / 0 | false / null / 0 |
| `FinancialPostingStatus` | NotRequired | NotRequired |

Rate, cap, base, tax, discount and totals are the checkout-captured immutable snapshots; **nothing
was re-invented or re-rated**, and the existing policy was not altered.

## 5. Collection and delivery evidence (synthetic provenance)

| Evidence | ID | Bound to | Issued | Consumed | Amount |
|---|---|---|---|---|---|
| Collection (A) | `5405ad52-ee29-41d8-9780-8f96c2152b7c` | order A, run `597a3ec9-…` | 01:24:06 | 01:26:02 | EGP 35.00 (virtual) |
| Delivery (A) | `e2dc7fe0-cdea-4da2-81f6-b84b64d16a7d` | order A + shipment `32c3362b-a2d1-4e11-b9e6-233d777759e6` | 01:28:03 | 01:28:09 | – |
| Delivery (B) | `c9017111-3a5f-4030-8e12-8b9498422d01` | order B + shipment `b560574b-aef2-41df-b73b-f91e6eb9be20` | 01:31:09 | 01:31:17 | – |
| Collection (B) | `c75ae217-f2fa-4cf7-81b4-b91677a8a147` | order B, run `597a3ec9-…` | 01:32:54 | 01:33:02 | EGP 35.00 (virtual) |

QA run: `597a3ec9-0c21-401b-9a15-bfd17ea6dae0` (tenant AhramStore, verifier
`1bb114d3-a9a3-4a9d-ace3-3690361637a8`, product `15e044bc-…`, link `01a16027-…`, customer
`3e6129bf-…`, provenance `SYNTHETIC QA — NO REAL DELIVERY OR MONEY`).

Virtual commission journals (no real money): `6307261d-ff08-4ce5-b138-7533c8f8fa02` (A) and
`819c1401-62fe-43ce-9056-9f80d43e8c53` (B), EGP 2.50 each, debit `QA-VIRTUAL-AFFILIATE-EXPENSE`,
credit `QA-VIRTUAL-AFFILIATE-PAYABLE`.

The wallet flow used a **generated image explicitly labelled "NOT A TRANSFER RECEIPT. NO WALLET
TRANSFER WAS MADE."** (`SYNTHETIC-QA-NOT-A-RECEIPT.png`) as the checkout transfer-proof artifact,
and a synthetic wallet number. It was never treated as a real customer payment; collection was
performed **only** by the development-only virtual simulator.

**COD semantics proved as required:** the COD order was created and stayed unpaid
(`PaymentStatus = Pending` at creation, `IsPaid = false`, `PaidAt = null`, no `PaidAmount`), and its
attribution survived the whole lifecycle; it earned nothing while uncollected.

## 6. Commission lifecycle (authoritative records)

| Stage | Order A | Order B |
|---|---|---|
| Earned entry | `f00543d7-f4db-4b6c-8801-512beb6f6ba6` (EntryType Earned, EGP 2.50) | `f18f37cd-7793-45b5-b1af-e4029c6f866c` (Earned, EGP 2.50) |
| Approved entry | `7ff7c000-e5d2-48d5-b17c-a9ed3df7aa44` (EntryType Approved, EGP 2.50, `OriginalEntryId` → Earned) | `6c0d4d44-8abe-4761-b028-ac558828a6b5` (Approved, EGP 2.50) |
| Idempotency keys | `AFFILIATE_COMM:EARNED:3802e66c-…` / `AFFILIATE_COMM:APPROVED:3802e66c-…` | `AFFILIATE_COMM:EARNED:037f2545-…` / `AFFILIATE_COMM:APPROVED:037f2545-…` |
| Recognized by | canonical `AffiliateService.RecordCommissionForOrderAsync` | same |
| Approved by | canonical approval path (delivered-event consumer / revalidation) | same |

Commission ledger totals for the affiliate account after the run (`authoritative-balance-final.json`):
Earned 4 rows / EGP 85.00 (80.00 historical + 5.00 new), Approved 4 rows / EGP 85.00, Reversed
3 rows / EGP 80.00, Paid 0, Payout requests 0, `AffiliateBalanceLedger` rows 0.

### `availableForWithdrawal` before and after

| | Before | After |
|---|---|---|
| Portal "Available for Withdrawal" | EGP 0.00 | **EGP 5.00** |
| Portal "Total Approved" | EGP 0.00 | EGP 5.00 |
| Portal "Total Paid" | EGP 0.00 | EGP 0.00 |
| Independent SQL (`Approved − Reversed − Paid`) | 0.00 | **5.0000** |

**Agreement confirmed:** the portal value (EGP 5.00) equals the independently computed SQL ledger
value (5.0000) = 2.50 + 2.50 from the two new orders. The portal UI snapshot
`portal-final-balance.yml` shows Available for Withdrawal EGP 5.00, Total Approved EGP 5.00, Total
Paid EGP 0.00, Pending Reserved EGP 0.00, "Request Withdrawal" **disabled** ("Below minimum
threshold", minimum EGP 100.00) and **no withdrawal history** — no payout was requested.

## 7. Negative, replay and concurrency evidence

| Scenario | Result | Evidence |
|---|---|---|
| Replayed collection (6 parallel, wallet) | all HTTP 200, `wasIdempotent: true`, `stateChanged: false`, no second inventory action | `wallet-concurrency-final.txt` |
| Replayed delivery (6 parallel, wallet) | all HTTP 200, `oldStatus = newStatus = Delivered`, no duplicate shipment transition | `wallet-concurrency-final.txt` |
| Replayed collection (6 parallel, COD) | all HTTP 200, idempotent, no state change | `cod-concurrency.txt` |
| Replayed delivery after completion (6 parallel, COD) | all HTTP 200, `success: false`, "Cannot advance fulfillment for terminal order status Completed." — no mutation | `cod-concurrency.txt` |
| Replayed commission revalidation (6 parallel, each order) | no duplicate Earned/Approved rows; ledger stayed at 2 entries per order | `wallet-concurrency-final.txt`, `cod-concurrency.txt` |
| Evidence used against the **wrong order** | HTTP 409 `SYNTHETIC_QA_EVIDENCE_FORGED_OR_MISMATCHED` | `cod-concurrency.txt` |
| Wrong shipment id | HTTP 409 `SYNTHETIC_QA_SHIPMENT_INVALID` | `live-negatives-final.txt` |
| Invalid / unknown run | rejected (validation/authority failure, no records) | `live-negatives-final.txt` |
| Forged attestation / forged collection ids | rejected, no records | `live-negatives-final.txt` |
| All four protected orders targeted for evidence | rejected, no records, no fulfillment effect | `live-negatives-final.txt`, `preservation.json` |
| Ordinary manual delivery proof (typed tracking/proof) | **rejected** by the production affiliate delivery guard | `untrusted-proof-rejected.txt` |
| Ordinary merchant owner without verifier authority | HTTP 403 `SYNTHETIC_QA_VERIFIER_REQUIRED` | `ordinary-owner-not-verifier.txt` |
| Non-Development runtime / unapproved DB identity | unit-covered fail-closed paths (`IsRuntimeAllowed`, actual server identity check) | `SyntheticQaDatabaseBoundaryTests.cs` |
| QA feature disabled | all five QA endpoints HTTP 404 `SYNTHETIC_QA_UNAVAILABLE`; Admin QA panel renders "QA certification unavailable" | `qa-disabled-endpoints-verified.txt`, `qa-ui-disabled-final.yml` |

Duplicate-effect check after all replays: Earned rows = 2 per order, Approved rows = 1 per order,
virtual journals = 1 per order, real `AccountingOutboxMessages` for QA orders = 0, real
`LedgerJournalEntries` / `LedgerLines` unchanged (220 / 459), `AffiliatePayoutRequests` = 0.

## 8. Headed browser actions actually performed (Edge, `opencode-ui-test`)

1. POS Admin: signed in as the AhramStore Owner; `GET /api/auth/session` confirmed tenant, role and
   effective permissions.
2. Affiliate Portal: signed in as the global affiliate; opened **Promote** → product drawer for
   `CERT Media Simple 20261007` → **Promote** → product link `LF9V1HCEQ3` created.
3. Storefront: opened the **actual referral URL** in a fresh Storefront customer session, signed in as
   the customer, added the product to cart, filled shipping (labelled "SYNTHETIC QA - DO NOT
   DISPATCH"), selected shipping, selected payment method, reviewed and placed the order.
4. Storefront checkout pending page confirmed order creation for both A and B.
5. Admin Orders: opened the normal drawer for both orders, observed the payment lifecycle section,
   provenance note, shipment evidence and the **Complete order** action; completed both orders from
   the normal drawer.
6. Affiliate Portal: opened Dashboard and **Withdrawals** tabs and read the live balance
   (`portal-final-balance.yml`, `affiliate-final-balance.txt`).

## 9. Focused regression, build, typecheck and lint

| Check | Result | Evidence |
|---|---|---|
| QA security + boundary suite (new) | **33 passed / 0 failed** | `qa.trx` |
| Affiliate Phase 1–4 / referral / promotion | **174 passed / 0 failed** | `affiliate.trx` |
| Checkout idempotency | **38 passed / 0 failed** | `checkout.trx` |
| Fulfillment / delivery evidence trust | **40 passed / 0 failed** | `fulfillment.trx` |
| Accounting / outbox | **83 passed / 0 failed** | `accounting.trx` |
| Payment lifecycle / wallet / COD | **71 passed / 0 failed** | `payments.trx` |
| Runtime-focused final selection (QA + Phase 4 + delivery convergence + promotion + referral) | **154 passed / 0 failed** | `runtime-focused-final.trx` |
| Admin Orders real-Postgres suite (with `SHIPPING_CONFIGURED_ROLLBACK_QUALIFY=1`) | **4 passed / 0 failed** | `configured-admin-orders.trx` |
| Admin Web (Vitest: QA panel, orders closure, affiliate portal closure, promotion workflow, admin orders API, lifecycle authority) | **46 passed / 0 failed**, 6 files | `admin-tests-verified.log` |
| Storefront Vitest (affiliate ref tracker, privacy client, checkout components, cart context, checkout API) | passed | `storefront-tests.log` |
| Backend build (isolated + normal output) | PASS, 0 errors | `backend-build-*.log` |
| Admin `tsc --noEmit` | PASS, no diagnostics | `admin-typecheck-verified.log` |
| Admin production build | PASS (`✓ built in 55.24s`) | `admin-build-qualified-final.log` |
| Storefront build | PASS | `storefront-build.log` |
| Focused lint | exit 0 — 0 errors, 9 pre-existing `react-refresh/only-export-components` warnings | `focused-lint-verified.log`, `lint-exit.json` |

### Failures that remain (pre-existing, unrelated, NOT modified to reach green)

`qualified-final.trx`: 204 total, 197 passed, 6 failed, 1 not executed.

- `FinalCertificationArtifacts_MustFreezeEndToEndOrderManagementAuthority`,
  `CertificationArtifacts_MustRecordSfAdm33AdminOrderPaymentAuthorityTruth`,
  `SharedAdminOrderPresentationCertificationArtifacts_MustBeFrozen` — historical certification-artifact
  string assertions (missing `PASS SF-ORD.FINAL`, `PASS SF-ADM3.3 …`, `PASS SF-ORD.XCHANNEL …`).
- `AdminOrderList_SearchSortPaginationAndCurrency_MustComeFromBackendDto` and
  `AdminOrderPaymentPresentation_MustRemainBackendOwnedReadOnlyAndNoStore` — expect specific
  `formatCurrency(...)` call shapes that the current source does not use.
- `AdminOrderPaymentRuntime_MustNotExposeFrontendPaymentVerificationOrAcceptedPaymentMutation` —
  expects no `"/payments/"` substring in the orders page; the current page legitimately contains it.

**Proof these are pre-existing:** re-running the same Admin Orders selection with my `Orders.jsx`
change temporarily stashed produced the **identical 6 failures** (`pre-existing-admin-order.trx`,
6 failed / 5 passed). No assertion was weakened, rewritten or excluded to obtain a green count.

`ConfiguredPostgres_AdminOrderListAndOwnership_OnlySyntheticRollback` was reported as failed only
because it is gated by `SHIPPING_CONFIGURED_ROLLBACK_QUALIFY=1` (a deliberate harness guard, not a
product failure). With that flag it **passes** (`configured-admin-orders.trx`, 4/4), which also
exercises the modified Admin Orders list query against real PostgreSQL.

**Not executed / out of scope:** the full repository test suite, CommerceHub suites, the WhatsApp
suite and the broad Phase 1–4 source-regex set were not re-run; the previously documented
stale-assertion failures from the 2026-10-21 closure report remain documented there and were not
re-litigated. Withdrawal/payout implementation was not started, per the stop condition.

## 10. Preservation of protected and unrelated data

`preservation.json` (row-level digests over 20 tables, baseline → final):

- **All four protected orders are byte-identical**: `ORD-83B3-2026-01091`
  (`d1102ed5-4256-4cef-80ca-41fe57c6163e`), `ORD-C43A-2026-01093`
  (`32a82d41-2ae6-47f9-aa4f-cd5d17b1e8d6`), `ORD-83B3-2026-01094`
  (`40b22f7f-09ac-4c60-b779-f14cf3734fe2`), `ORD-C43A-2026-01095`
  (`2654c8f2-c859-41ad-b637-212affd7b1b4`). They were never targeted for delivery, collection,
  cancellation, refund, fulfillment, customer contact or reservation release. Targeted evidence
  attempts against them were rejected.
- **No original row changed and no original row went missing** in any inventoried table.
- Table deltas are exactly the intended new records: Orders 64→66, OrderItems 64→66,
  AffiliateAttributions 11→13, AffiliatePromotionLinks 5→6 (the new product link),
  AffiliateCommissionLedger 10→14 (2 Earned + 2 Approved), Shipments 28→30, PaymentIntents 34→35.
  `LedgerJournalEntries` 220→220, `LedgerLines` 459→459, `AccountingOutboxMessages` 30→30,
  `AffiliateBalanceLedger` 0→0, `AffiliatePayoutRequests` 0→0, `InventoryReservations` 57→57,
  `ProductVariantStocks` 127→127, `OrderRefunds` 6→6 — **no real money moved**.
- No Git stage/commit/stash/reset/clean was performed on the workspace. Two short-lived `git stash
  push`/`pop` cycles were used **only** on `PointOfSale.Web/src/pages/Orders.jsx` to prove the six
  architecture failures are pre-existing; the file was restored immediately and its diff is
  unchanged (4 added lines). `git log`/HEAD unchanged.
- One prior artifact shows a changed hash:
  `.ai/reports/AFFILIATE_PHASE4_LIVE_QUALIFYING_CERTIFICATION_CLOSURE_20261021.md`. It was **not**
  edited by this task; its trailing "Post-report runtime status" note was appended by a pre-existing
  background process from the earlier phase at 23:44:57 (after this task's 23:23:50 baseline
  capture) when the earlier session's API launcher exited. No earlier report content was removed or
  rewritten. This is recorded rather than hidden.
- No credentials, tokens or passwords were written into evidence files. The QA signing key was
  generated locally, never printed, and **removed from the configuration when QA was disabled**.

## 11. Final configuration state and QA disablement

| Item | Final state |
|---|---|
| `SyntheticQa.Enabled` | **false** (`appsettings.Development.local.json`) |
| `SyntheticQa.VerifierIds` | **[]** (no verifier authority for anyone) |
| `SyntheticQa.SigningKey` | **removed** from the configuration |
| QA endpoints (`status`, `runs`, `evidence`, `collect`, `deliver`) | HTTP **404 `SYNTHETIC_QA_UNAVAILABLE`** for the authorized merchant session |
| Admin QA panel `/qa/affiliate-certification` | renders "QA certification unavailable"; no actions offered |
| Admin QA panel route | `import.meta.env.DEV` only, so it is not part of a production build |
| COD payment method | enabled (unchanged) |
| MANUALWALLET payment method | enabled, unchanged display name (test-only) |
| Stripe / Paymob | untouched and disabled |
| Product / policy / price / stock | unchanged |
| Promotion link `LF9V1HCEQ3` | **left in place**, because it is a legitimate affiliate link created through the supported portal flow and deleting it would destroy audit history. It is inert until a real customer uses it. |
| New test orders | **kept**, auditable and clearly labelled synthetic |
| QA audit tables | **kept** (append-only history) |
| Database | not reset, not broadly restored |

## 12. Acceptance criteria

| Criterion | Result |
|---|---|
| A new AhramStore order created through the real Storefront referral link | **PASS** — `ORD-C9C5-2026-01096` and `ORD-C9C5-2026-01097` |
| Persisted attribution references the correct global affiliate and product link | **PASS** — both attributed to account `01a03e11-…` via link `01a16027-…` (`LF9V1HCEQ3`) |
| Authoritative Earned commission | **PASS** — EGP 2.50 each, from the canonical commission service |
| Authoritative Approved commission | **PASS** — EGP 2.50 each, `OriginalEntryId` linked to the Earned entry |
| Affiliate Portal balance matches the authoritative ledger | **PASS** — EGP 5.00 = EGP 5.0000 (Approved − Reversed − Paid) |
| No duplicate financial effects | **PASS** — replays/concurrency produced no extra Earned/Approved rows, journals, postings or balance changes |
| Both payment methods exercised | **PASS** — `MANUALWALLET` and `COD`, both through the supported checkout; COD proven unpaid until collection |
| Synthetic provenance explicit everywhere | **PASS** — order notes, evidence rows, virtual journals, Admin drawer, portal-free balance; no real delivery, payment, carrier, wallet transfer, cash receipt, payout or withdrawal |
| QA opt-in disabled and endpoints unavailable | **PASS** |
| Withdrawal / payout not started | **PASS** — stop condition respected |

## 13. Residual risk and honest limitations

- The commission amounts certified here were produced by a **synthetic virtual collection**. They
  prove the Phase 4 recognition, approval, ledger and balance-readback machinery end-to-end for
  AhramStore, but they do **not** prove real-world cash collection, real carrier delivery, or
  real-world affiliate payout behaviour. Those remain unproven and unclaimed.
- The QA endpoints and the `SyntheticQaCollected` enum remain in the codebase as a disabled,
  fail-closed, Development/Test-only capability. A production deployment would additionally require
  `SyntheticQa.Enabled` (default false) and an explicit verifier allowlist; both are absent now.
- Six pre-existing Admin Orders architecture-source assertions and one harness-gated Postgres
  selection remain red/unexecuted in the default configuration, as documented in §9.
- `MANUALWALLET` was exercised as the primary method, but its **real** operator approval path was
  deliberately not used: approving a synthetic transfer-proof image as a genuine customer payment
  would have misrepresented test data as a real payment, which the task forbids. The virtual
  simulator is the correct substitute and is clearly labelled as such.
- This certification covers AhramStore only. Shopixy and Greatone were out of scope and their
  protected orders were untouched.

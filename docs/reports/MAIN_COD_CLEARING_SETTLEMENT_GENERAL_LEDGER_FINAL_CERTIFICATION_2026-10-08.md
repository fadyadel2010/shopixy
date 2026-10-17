# MAIN Treasury / COD Clearing / General Ledger — forensic audit and final certification

**Date:** 2026-10-08 (UTC evidence timestamps)  
**Repository HEAD:** `2563396`  
**Runtime:** MAIN POS `http://localhost:5173`; AhramStore tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`; PostgreSQL `PosEcommerceDb_Baseline`.  
**Browser:** headed Microsoft Edge, registered Playwright CLI session `opencode-ui-test`.  
**Execution:** no subagents; no commit. Initial worktree was clean.

## 1. Verdict and exact root causes

**PASS — acceptance branch B, current accounting contract and General Ledger remediation.**

**Positive COD/carrier settlement execution is NOT CERTIFIED and remains unavailable.** This report does not certify an Order → Clearing → executed settlement → Cashbox financial journey.

There are three distinct findings:

1. **COD Clearing missing from standard transfers is intentional and correct.** Both Treasury transfer services restrict source and destination to active, same-tenant mapped Cashbox/Bank Asset accounts. The browser applies the same exact account-type rule. An Owner can see Clearing without gaining transfer authority.
2. **The historical cashbox shortcut was misleading.** The old Clearing Aging “Settle to Cashbox” action navigated to the generic transfer form with destination `1010`; it did not invoke the payment-settlement boundary. Historical payment settlement posts to Bank `1030`, not Cashbox `1010`. Generic manual settlement is disabled with `410`; COD/gateway settlement ingress is deliberately contained with `409` because authenticated remittance/payout evidence is not integrated.
3. **General Ledger account discovery was genuinely broken.** `TreasuryGeneralLedger.jsx` used the selected month's Trial Balance movements as its account catalog, loaded only on mount. AhramStore has 13 September journals but no October journals. On October 8 the Trial Balance returned `lines: []`, leaving the ledger selector with only its placeholder. Changing the report dates did not reload that catalog. This prevented ordinary selection of accounts whose earlier history existed and completely excluded zero-activity Clearing accounts.

The smallest correction uses the existing official ledger-account summary API for account discovery, keeps account/date context in the URL, handles read failures honestly, and explicitly discloses the existing settlement unavailability. No financial mutation boundary was enabled or weakened.

## 2. Evidence sources and investigative scope

Primary source evidence:

| Concern | Authoritative source |
|---|---|
| Storefront order admission | `PointOfSale.API/Services/Checkout/CheckoutService.cs`, especially initial `PaymentStatus.Pending` and immutable payment/fulfillment snapshots |
| Payment state and collection/settlement | `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` |
| Current settlement HTTP contract | `PointOfSale.API/Controllers/PaymentLifecycleController.cs:206–259` |
| Manual settlement retirement | `PointOfSale.API/Controllers/AdminAccountingController.cs:114–126` |
| Ledger journal generation | `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs` |
| Posting, validation, balance mutation | `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`; `PointOfSale.Core/Entities/Accounting/Account.cs` |
| Settlement/order replay authority | `PointOfSale.Infrastructure/Reliability/AccountingLedgerAuthority.cs`; `AccountingOutboxProcessor.cs` |
| Treasury transfer execution | `TreasuryController.LedgerTransfer`; `PointOfSale.Infrastructure/Treasury/TreasuryOperationService.cs`; `Accounting/TreasuryTransferService.cs` |
| Treasury reads | `PointOfSale.Infrastructure/Treasury/TreasuryReadService.cs` |
| Official financial reports | `FinancialReportsController`; `FinancialReportService` |
| Accounts/mappings/codes | `Account`, `TreasuryAccount`, `PaymentMethod`, `TenantAccountSeeder`, `SystemAccountTemplates` |
| Permissions | `Core/Security/PosPermissions.cs`; `API/Security/PosEndpointPermissionCatalog.cs`; permission convention/handler and SQL-current administrative authority |
| Browser routes/projection | `PointOfSale.Web/src/App.jsx`, `components/Sidebar.jsx`, `services/treasuryAccountCatalog.js`, Treasury pages |

Repository searches covered COD/clearing settlement, clearing transfers, settlement accounts/journals, Cashbox, ledger posting, account-source/destination filters, tests, archived migrations, old routes, flags and reports. Crucially, a call-site search found **no production caller executing** `RecordOfflineSettlementAsync`, `RecordGatewaySettlementAsync`, or `SettleClearingAsync` through current HTTP ingress. Remaining callers are tests; existing SQL outbox recovery uses the journal-generation methods for historical authoritative batches.

Historical documents were checked against current source and Git, rather than treated as runtime truth. Old reports assigning COD to `1140` are stale: current constants, seeded mappings, posting code and SQL all prove **COD = `1130`; Wallet = `1140`**.

SQL investigation used `REPEATABLE READ`, `SET TRANSACTION READ ONLY`, and rollback. No shared accounting-table write, manual journal, real collection confirmation, external payment, refund, bank transfer or customer-order mutation was performed.

## 3. Current accounting model and ownership

`Account.AccountType` is the financial category. `TreasuryAccount.Type` is a separate treasury role on a linked account. They must not be conflated.

COD Clearing is:

- a debit-normal **Asset** (`AccountType = 1`);
- a **Clearing/holding account for money in transit**, `IsClearing = true`;
- treasury role **Clearing** (`TreasuryAccountType = 3`);
- not available cash, not a physical cashbox and not a Bank account;
- an economic claim on collected funds pending remittance, rather than an arbitrary technical-only balance;
- seeded with deferred COD payment mapping, no direct `TargetAccountId`, and an expected settlement-age configuration;
- `PreventNegative = false` in current data, unlike the cashbox and bank. This does not grant transfer or settlement authority.

Service ownership:

| Responsibility | Owner |
|---|---|
| Payment/order financial transition | `PaymentLifecycleService`, with authorized state changes and SQL lifecycle transaction |
| Carrier collection ingress | `ShipmentCodCollectedConsumer` → payment lifecycle; event amounts do not become ledger authority independently of current SQL order truth |
| Delivery state | `OrderFulfillmentService` / carrier shipment authority; collection is not created by checkout |
| Payment settlement batches/membership | `PaymentLifecycleService` (current external ingress disabled) |
| Generic treasury transfer | UI-used `TreasuryOperationService`; older ID-based admin route uses `TreasuryTransferService` |
| Journal shape | `CommerceAccountingService`, or the Treasury operation service for internal transfers |
| Atomic journal/line/balance mutation | `FinancialPostingService` |
| Balance aggregate | `Account.ApplyEntry`: debit-normal assets use debits minus credits; updated with the journal/lines in the posting transaction |
| Treasury current balances | `TreasuryReadService`, linked `Account.CurrentBalance` aggregate |
| GL, Trial Balance, other official financial reports | `FinancialReportService`, immutable posted/reversed journal lines, same tenant |
| SQL-recomposed accounting retry authority | `AccountingLedgerAuthority` and `AccountingOutboxProcessor` |

The actual database tables are `LedgerAccounts`, `LedgerJournalEntries`, and `LedgerLines`, not the unrelated party `AccountLedgers` table. The General Ledger API reads the correct source.

## 4. Historical settlement behavior and why it disappeared

Git evidence:

- `git log -S "toAccountCode: '1010'" -- .../TreasuryClearingAging.jsx` identifies initial repository `3fb3d5c` and removal commit **`f939e6b`**, dated September 16, 2026 local time.
- `git show f939e6b -- .../TreasuryClearingAging.jsx` proves the removed `handleSettleToCashbox` only navigated to `/treasury/transfers` with source clearing code, target `1010`, the displayed pending amount and a generated text reference.
- The same commit replaced COD/gateway settlement controller execution with `SETTLEMENT_EVIDENCE_REQUIRED` conflicts and replaced the aging action with a disabled provider-workflow explanation.
- Both generic-transfer Cashbox/Bank restrictions and manual-settlement `410` containment are already present in initial repository `3fb3d5c`. Even `f939e6b^` rejects a Clearing account in `TreasuryOperationService`. The old UI shortcut and backend contract were already inconsistent; Git does **not** prove that it was ever a legitimate successful clearing-to-cashbox workflow in this repository.

The rationale is corroborated by:

- `docs/reports/TREASURY_T10_SETTLEMENT_DELEGATION_BRIEF_2026-09-15.md`;
- `docs/reports/TREASURY_T10_GLOBAL_SETTLEMENT_EXECUTION_CERTIFICATION_2026-09-15.md`;
- `docs/reports/TREASURY_UI_ACCOUNT_CATALOG_DELEGATION_BRIEF_2026-09-15.md`;
- `.ai/hermes/accounts/FLOW-ACC-02-TREASURY.md`.

These changes contain operator assertions that otherwise could fabricate carrier remittance or provider payout facts. Old service mechanics, batch entities, SQL lineage, refund context and outbox replay remain for historical accounting recovery. They are not an unlinked working settlement UI waiting to be restored.

The newer COD Reference Workflow also does not provide an alternate executor: its settlement authority interface is a foundation contract; `CodCollectionEvidenceCatalog.SettlementEnabled` and `CodRuntimeAdmissionCatalog.SettlementEnabled` are false, and configuration validation explicitly rejects enabling foundation settlement execution.

## 5. Transfer versus settlement contract

The correct accounting classification for legitimate carrier remittance is **B: Clearing Settlement**, not a standard Treasury Transfer. Its historical canonical destination is **Bank `1030`**.

| Property | Standard Treasury transfer | Historical COD payment settlement |
|---|---|---|
| Source | Active mapped Cashbox or Bank Asset | Collected COD orders and their Clearing `1130` lineage |
| Destination | Active mapped Cashbox or Bank Asset | Canonical Bank `1030` |
| Journal | Dr destination, Cr source | Dr Bank net, Dr Shipping Fees if positive, Cr COD Clearing gross |
| Business identity | Required operation/transfer ID at ingress | Tenant + settlement type + external settlement ID, batch ID and unique order membership |
| Order status | No payment-finality transition | `Collected` → `Settled` for included orders |
| Reference | Operator description/reference | Settlement reference, provider/carrier, currency, gross/fees and bank reference in SQL batch |
| Idempotency | `TREASURY_TRANSFER:{tenant}:{operationId}` or older `TRANSFER:{tenant}:{id}` | `COD_SETTLEMENT:{tenant}:{settlementId}` |
| Availability today | Executable for authorized roles and valid accounts | HTTP execution unavailable: authenticated remittance evidence missing |
| Audit provenance | Actor supplied to UI-used posting request; journal source/ID/reference | Batch membership, accounting outbox, `OfflineSettlementCompletedEvent`; no successful current settlement audit event |

The lower-level retained `TreasuryTransferService.SettleClearingAsync` permits **Clearing → Bank only**, creates source type `ClearingSettlement` and key `SETTLEMENT:{tenant}:{id}`, but lacks the payment batch/order-membership boundary. The disabled manual admin endpoint must not be reopened to use it as a bypass.

For gross `G` and fees `F`, the canonical dormant journal is:

```text
Dr 1030 Bank                 G - F
Dr 6020 Shipping Fees        F       (if F > 0)
Cr 1130 COD Clearing         G
```

Therefore `Clearing decrease = Bank increase + fees`, not necessarily `Clearing decrease = Bank increase`. With zero fees, the decrease and increase are equal. Cashbox receives nothing from this journal. A subsequent authorized Bank → Cashbox standard transfer would be a separate business operation after legitimate bank settlement.

Current settlement rejects before trusting settlement confirmation, carrier name, reference, caller amount, order selector or fees. An operator confirmation dialog is not authenticated supporting evidence. No COD-specific dropdown exception was introduced.

## 6. COD lifecycle, live order trace and eligibility

Canonical storefront flow:

```text
Checkout admission (COD, Pending, IsPaid=false)
  → fulfillment under manual/carrier ownership
  → canonical COD collection transition with required delivery/collection truth
  → Collected / IsPaid=true / posting pending
  → transactional accounting outbox and domain event
  → immediate SQL-authority posting or outbox retry
  → Dr 1130 COD Clearing, Cr 4020 E-commerce Sales
  → financial posting Posted with journal ID
  → potential remittance eligibility only after collection accounting is posted
  → current settlement ingress rejects: authenticated remittance evidence unavailable
```

Initial order creation does **not** debit COD Clearing. Replay requires recognized payment truth and delivery truth; retained settlement requires collected orders, same tenant/currency, matching gross total, valid fees and posted collection accounting.

Live SQL trace, without customer contact/address data:

| Field | Order 1 | Order 2 |
|---|---|---|
| ID | `a1f201e7-6124-4d63-bd88-fd2fe1cc8e88` | `5ca9b832-e702-43cb-aed1-587ed2b01b9f` |
| Reference | `ORD-C9C5-2026-01039` | `ORD-C9C5-2026-01037` |
| Created UTC | `2026-09-29T14:16:50.413193Z` | `2026-09-28T19:54:29.732902Z` |
| Method | COD / gateway `cod` | COD |
| Amount/currency | 560.00 EGP | 560.00 EGP |
| Payment state | Pending (`0`) | Failed (`4`) |
| Fulfillment | Confirmed (`1`), not delivered | Created (`0`), not delivered |
| Order state | Processing (`2`) | Cancelled (`6`) |
| Financial posting | NotRequired (`0`), no journal | NotRequired (`0`), no journal |
| Settlement eligibility | No | No |

Both belong to the AhramStore tenant above. There are **zero `Order_COD` journals**, **zero COD Clearing lines**, **zero settlement batches/members**, and **zero accounting outbox rows** in that tenant. COD Clearing is correctly 0.00 EGP; an order's displayed COD total is not proof of a clearing asset.

## 7. Account-type and authority matrix

All rows below have tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`, active/non-deleted ledger account and active/non-deleted Treasury mapping. SQL aggregates and API balances agree.

| Treasury display name | Code | Financial type | Treasury type | Transfer source | Transfer destination | Settlement source | Settlement destination | Balance EGP | Journal support / existing lines |
|---|---|---|---|---|---|---|---|---:|---|
| Main Cashbox | 1010 | Asset | Cashbox (1) | Yes | Yes | No | No | 10.00 | Yes / 4 |
| Primary Bank Account | 1030 | Asset | Bank (2) | Yes | Yes | No | Historical canonical destination; current execution disabled | 0.00 | Yes / 0 |
| Visa Clearing | 1110 | Asset, IsClearing | Clearing (3) | No | No | Historical PAYMOB/CARD/VISA lineage; disabled ingress | No | 0.00 | Yes / 0 |
| Stripe Clearing | 1120 | Asset, IsClearing | Clearing (3) | No | No | Historical Stripe lineage; disabled ingress | No | 0.00 | Yes / 0 |
| COD Clearing | 1130 | Asset, IsClearing | Clearing (3) | No | No | Historical COD lineage; disabled ingress | No | 0.00 | Yes / 0 |
| Wallet Clearing | 1140 | Asset, IsClearing | Clearing (3) | No | No | Low-level retained Clearing→Bank shape only; no supported wallet payment-settlement executor | No | 0.00 | Yes / 0 |

Exact identity matrix (IDs are tenant-scoped; codes do not grant treasury authority):

| Code | Ledger Account ID | Treasury Mapping ID |
|---|---|---|
| 1010 | `01a0828d-734b-7385-9659-ef041fa3e836` | `01a0828d-7502-71c0-b3d5-d0cd09fcbb42` |
| 1030 | `01a0828d-734b-7bdd-b50c-cc124e5b3688` | `01a0828d-7512-7c3c-a65c-c579e16c68d1` |
| 1110 | `01a0828d-734b-75cb-9ce7-1a92038207a4` | `01a0828d-7512-7ab3-b175-a4025efec092` |
| 1120 | `01a0828d-734b-7934-932e-5d1014eaf211` | `01a0828d-7512-7184-9a86-da6a562b8a8f` |
| 1130 | `01a0828d-734b-7f91-a220-8757c0148304` | `01a0828d-7512-784a-a6f7-299113d85a6d` |
| 1140 | `01a0828d-734b-7b72-825c-86d0a85d8c2b` | `01a0828d-7512-728f-afd8-a5cd264467fc` |

Report-only accounts, including `1020` POS Visa, hierarchy parents, reserves and supplier-card funding assets, are not made Treasury-transfer eligible by being visible in the GL. All Clearing mappings share the same transfer exclusion. The frontend has no COD whitelist or exception.

## 8. Cross-payment-method comparison

The ledger `PaymentMethods` table is distinct from storefront `TenantPaymentMethods`. AhramStore's only configured/enabled storefront method is **COD** (`IsSandbox=true`); Stripe/Paymob/Wallet were inspected in source and tests, not submitted externally.

| Method | Collection authority | Initial ledger destination | Revenue/source | Subsequent settlement |
|---|---|---|---|---|
| COD | Canonical offline collection after required delivery/collection truth | 1130 COD Clearing | Cr 4020; `Order_COD` | Historical `CODSettlement` → 1030 net + 6020 fees; ingress 409 |
| Stripe | Validated capture/webhook/payment-intent authority | 1120 Stripe Clearing | Cr 4020; online capture journal | Historical `GatewaySettlement` → 1030 net + 6010 fees; ingress 409 |
| Paymob / CARD / VISA | Validated gateway capture or POS payment authority | 1110 Visa Clearing; ecommerce normalization maps provider to CARD | E-commerce collection uses 4020; POS uses POS revenue contract | Historical gateway clearing → Bank shape; ingress 409 |
| Manual Wallet / WALLET | Existing admin manual payment review workflow; old lifecycle wallet approve route is NonAction | 1140 Wallet Clearing | Cr 4020; wallet/collection journal | Wallet is not an accepted gateway-settlement provider; no positive wallet settlement executor |
| CASH | POS source-owned collection | Mapped cashbox | POS revenue/tax/COGS model | Already cash; not a clearing settlement |
| BANK_TRANSFER | Immediate ledger payment mapping | 1030 Bank | Source-owned payment workflow | Direct mapped bank receipt; no clearing batch inferred from a name |
| Fawry / unknown provider | Explicit financial-authority fail-closed policy | No fallback account authority | No unknown-to-Stripe fallback | Unsupported; not repurposed as COD/Stripe/Visa |

This is a generic account-role model with provider-specific payment lineage, not a COD-only transfer feature. Dormant settlement code supports specific providers; having `TreasuryAccountType.Clearing` alone does not establish a usable settlement business workflow for every clearing account.

## 9. General Ledger investigation and correction

Direct SQL proves **13 journals / 30 lines**, with 12 Posted and 1 Reversed journal, spanning September 13–16. Debits and credits both total **3,880.00 EGP**, with **0 unbalanced journals**. October 1–8 genuinely has no activity.

The pre-fix API result:

```text
GET /api/financialreports/trial-balance?from=2026-10-01&to=2026-10-08
200; tenantId matches AhramStore; lines=[]; debits=credits=0
```

This was incorrectly used as the account catalog. The screen intentionally requires an account before requesting a ledger; that requirement itself is valid. The defect was that no account could be chosen when the mount period was inactive. The load failure path also only logged errors, leaving an apparently empty selector, and selected account/date context was not retained for reload.

Corrected behavior:

- `GET /api/financialreports/accounts` supplies **33** tenant-scoped ledger accounts independently of report dates and current-period movement.
- Dates still constrain ledger entries; September history appears only when the selected interval includes it.
- October Cashbox report has no period entries, but correctly retains its 10.00 EGP opening/closing balance.
- Account, `from` and `to` persist in query parameters.
- Failed/foreign reads show a retryable error and do not display invented zero balances or stale previous-account reports.
- Obsolete requests are aborted/ignored.
- The perimeter note explains account/date filters and opening balances; the account header is labelled Closing Balance, since it is the report's as-of balance.

The Owner was permitted (`/api/auth/session` returned Owner and matching tenant). No account-scope or tenant mismatch explains the original empty selector. A genuine foreign-tenant account request was separately rejected with 404.

## 10. Exact existing General Ledger evidence

Official GL API for account `1010`, range `2026-09-01` to `2026-10-08`, returns opening 0.00, debits 1,000.00, credits 990.00, closing 10.00, and exactly these four lines. All belong to the AhramStore tenant.

| Journal / UUID | Date UTC | Source / reference | Debit EGP | Credit EGP | Balance after EGP | Created-by user ID |
|---|---|---|---:|---:|---:|---|
| JE-2026-000001 / `01a09cc3-fe61-749a-b7b1-5746d4e353ba` | 2026-09-13 | TreasuryInitialCapitalization / `TIC-20260913-AA6DABE5` | 1,000.00 | 0 | 1,000.00 | `1bb114d3-a9a3-4a9d-ace3-3690361637a8` |
| JE-2026-000002 / `01a09ce1-3c2a-7153-beb2-b58385b6a22d` | 2026-09-13 | SupplierCardFunding / `T06C-FUND-001-RERUN` | 0 | 100.00 | 900.00 | null |
| JE-2026-000005 / `01a09fd5-1220-7078-87ab-7be006db7bc7` | 2026-09-14 | PurchaseInvoice / `PUR-20260914-1011` | 0 | 10.00 | 890.00 | null |
| JE-2026-000010 / `01a0aa05-a3a4-72ba-8e8e-ebee3d2e0d1d` | 2026-09-16 | PurchaseInvoice / `PUR-20260916-1014` | 0 | 880.00 | 10.00 | null |

The browser rendered all four journal numbers, dates, source types, debit/credit amounts and balances after widening the date interval. The entries persisted after reload in all four language/viewport variants. They are existing accounting history, not newly generated display rows.

## 11. Disposable COD financial lifecycle and exact journal

`CodPaymentLifecycleTests.CodCollection_WithRealPosting_ConservesJournalTruthAndCannotBecomeCashboxTransfer` uses the managed **real PostgreSQL** test database, real account seeder/resolver, real `CommerceAccountingService`, real `FinancialPostingService`, and real `FinancialReportService`. Event transport is mocked and the test outbox adapter persists its row. Delivery is a synthetic manual-fulfillment fixture; no carrier/provider is contacted.

Evidence from the final broader run, captured in `.artifacts/cod-ledger-audit/certification.trx`:

| Field | Exact value |
|---|---|
| Disposable database | `pointofsale_tests_codpaymentlifecycletests_0b_daf1959f_85911ce3` |
| Tenant | `037a3224-77fb-43ac-b625-ac6a688a1cf6` |
| Order | `194e103c-0c8a-403f-b36d-2fe1928088df` |
| Reference / amount | `ORD-d7ad9d7f` / 100.00 EGP |
| Journal | `01a11d2d-4f77-7b21-8c23-af38273e30d2`, `JE-2026-000001` |
| Journal date / source | `2026-10-08T00:00:00Z` / `Order_COD` |
| Idempotency | `COD_DELIVERED:037a3224-77fb-43ac-b625-ac6a688a1cf6:194e103c-0c8a-403f-b36d-2fe1928088df` |
| COD Clearing account | `01a11d2d-4762-7c7d-a4e2-ae35f9ec0c68` |
| Cashbox account | `01a11d2d-4761-7241-babd-81e9a44ce7e6` |
| Bank account | `01a11d2d-4762-7a99-bf22-5c271619c2c4` |
| Actor | System/test lifecycle; `CreatedByUserId=null`, consistent with this collection posting path |

Exact collection journal:

```text
Dr 1130 COD Clearing       100.00 EGP
Cr 4020 E-commerce Sales   100.00 EGP
SourceId = disposable OrderId; SourceReference = ORD-d7ad9d7f
```

Before creation/collection: Clearing 0, Cashbox 0, Bank 0. Creation and attempted collection while OutForDelivery generate no journal. After delivered-fixture collection: Clearing **100**, Cashbox **0**, Bank **0**; payment Collected, financial posting Posted, journal ID attached to order. Repeated collection retains **one journal**, one collection event, and the same balances. A fresh DbContext/official GL service returns the exact Clearing debit and closing balance 100.

Clearing→Cashbox and Clearing→Bank generic transfers are rejected; low-level Clearing→Cashbox settlement is rejected; foreign tenant and zero/negative settlement cases are rejected. No settlement batch is created. This certifies collection and boundary preservation, not positive settlement execution.

## 12. Live settlement probes, balance before/after and journal absence

Owner-authenticated requests used a disposable **1 EGP reject-only** amount, fabricated fixture ID `77777777-7777-4777-8777-777777777777` (not a customer order), and marker `FORENSIC-20261008-REJECT-ONLY`.

| Probe | Result | Evidence |
|---|---|---|
| Two concurrent identical COD settlement requests | Both 409 `SETTLEMENT_EVIDENCE_REQUIRED` | Trace IDs `a65f0c64-48af-43e8-9a61-b3e81a51db9c`, `2f7b2d76-c6c9-44c5-9eba-329d5fea1f1b` |
| Gateway settlement request | 409 `SETTLEMENT_EVIDENCE_REQUIRED` | Trace `9ef3834d-74f5-4012-bafc-6a42780a9e4a` |
| COD settlement, null body | 409, same code | Trace `eded23ab-f779-47a0-a2e9-afb6129a745e` |
| Manual admin clearing settlement to Cashbox | 410 `SETTLEMENT_AUTHORITY_REQUIRED` | Trace `9fd2fd2d-c2ad-4f74-b868-bfd4c7877ebe` |
| Generic transfer 1130→1010 | 400; active same-tenant Cashbox/Bank authority required | Trace `37cac52e-dc67-4226-82d3-962bfef680ba` |
| Official GL, actual foreign-tenant account | 404 | Foreign account `01a059c3-fb92-75b4-9d1f-89d674cb47de`; SQL tenant differs |
| Treasury catalog without authentication cookies | 400 `TENANT_REQUIRED` before usable tenant context | No financial data returned |

Shared database verification before/after the probes and browser journey:

| Measure | Before | After |
|---|---:|---:|
| COD Clearing | 0.00 | 0.00 |
| Main Cashbox | 10.00 | 10.00 |
| Bank | 0.00 | 0.00 |
| Other three clearing balances | 0.00 each | 0.00 each |
| Journals / lines | 13 / 30 | 13 / 30 |
| Payment settlement batches / members | 0 / 0 | 0 / 0 |
| Accounting outbox messages | 0 | 0 |
| Total debits / credits | 3,880.00 / 3,880.00 | 3,880.00 / 3,880.00 |
| Unbalanced journals | 0 | 0 |

**Executed settlements: zero. New settlement journals: zero. Settlement balance movement: zero.** No success event or settlement journal was inferred from a changed UI. SQL found no relevant Treasury/Journal/Settlement `AuditLogs` records in this tenant; no successful settlement audit event is claimed.

## 13. Permission and role matrix

Server permission policies are assigned by the application-model convention; inspecting only `[Authorize]` attributes is insufficient. Grants are SQL-current role/session-authoritative. No policy, role grant, tenant check or permission filter was removed.

| Capability | Owner | Admin | Manager | Cashier | Warehouse |
|---|---|---|---|---|---|
| Treasury Hub/Center, catalog, COD Clearing, Treasury balances/movements (`finance.view`) | Yes | Yes | Yes | No | No |
| Finance Dashboard / Clearing Aging / General Ledger and official journal report (`reports.financial.view`) | Yes | Yes | No | No | No |
| Standard Treasury transfers (`finance.operate` + role gate) | Yes | Yes | Yes | No | No |
| Call COD/gateway settlement ingress (`storefront.payments.manage`, Owner/Admin role gate) | Reaches 409 | Reaches 409 | Denied | Denied | Denied |
| Initiate or execute an actual COD/gateway settlement | Unavailable | Unavailable | No | No | No |
| Manual admin settlement | Reaches 410 | Reaches 410 | Denied by `finance.admin` | Denied | Denied |

For an Owner the Clearing selector exclusion is **not** a role restriction: all six Treasury accounts are visible in `/treasury/accounts`, while the form filters exact types to Cashbox/Bank. The backend independently rejects the same Clearing source.

`PosPermissionAuthorizationTests.TreasuryAndLedger_EnforceCanonicalRoleMatrixOnServer` verifies resolved server permissions and handler authorization for all five roles. Existing role/claim-forgery tests also passed. Live headed login was Owner; separate real employee credentials were not used to impersonate or alter staff roles.

## 14. Frontend, sidebar and route trace

| Screen | Route | Permission / data |
|---|---|---|
| Finance Dashboard | `/treasury` | reports.financial.view |
| Treasury Hub/Center | `/treasury/hub` | finance.view; canonical Treasury catalog |
| COD account | `/treasury/account/1130` | finance.view; catalog-backed identity and movement APIs |
| Transfers | `/treasury/transfers` | finance.operate; `getTreasuryAccounts().filter(isTransferEligibleAccount)` |
| Clearing Aging | `/treasury/clearing-aging` | reports.financial.view; financial report with disabled settlement action |
| General Ledger | `/treasury/general-ledger` | reports.financial.view; all-account summary catalog, account/date-specific ledger |

Both Sidebar and `App.jsx` guards apply those permissions. `isTransferEligibleAccount` delegates to exact `Cashbox`/`Bank` types in `treasuryAccountCatalog.js`; names, numeric code prefixes, `IsSystem`, `IsClearing` alone and balance signs cannot confer authority. The backend catalog filters mappings for tenant, activity, deletion, linked Asset classification and supported treasury role.

No active Clearing Settlement route, hidden provider-execution UI, wallet-settlement navigation, feature-flag toggle or alternative COD cashbox endpoint was found. Foundation configuration is not an escape hatch.

## 15. Idempotency, concurrency and accounting invariants

| Invariant | Evidence / certification scope |
|---|---|
| No cash creation/destruction from rejected settlement | Real API parallel probes + identical SQL balances, journals, lines, batches and outbox counts |
| Collection journal balanced and matches balance | Real PostgreSQL lifecycle/posting test: Dr1130=100, Cr4020=100, Clearing=100; Cashbox/Bank unchanged |
| Duplicate collection does not double-post | Same test repeats lifecycle collection; exactly one journal and one collection event |
| Duplicate/concurrent settlement cannot post through current ingress | Two real concurrent identical requests return 409; new test sends eight repeated submissions with strict lifecycle mock and verifies no calls |
| Wrong tenant rejected | Live foreign GL account404; existing COD tenant-authority test and new transfer guard |
| Unauthorized roles rejected | Server policy-handler role matrix and forged-claim tests; frontend guards remain secondary |
| Inactive mappings/accounts rejected | Real PostgreSQL guard test disables mapping, then account; no journals or changed balances |
| Invalid source/destination types rejected | All four Clearing codes tested as source and destination of standard transfers |
| Zero/negative amount rule | Retained settlement service rejects nonpositive amount; current ingress rejects all execution |
| Already-settled order not settled twice | Unique tenant/order membership and Collected-only gate in dormant SQL model; existing gateway idempotent replay/mismatched evidence test passed; current ingress cannot create a first or second settlement |
| Existing report monetary conservation | FinancialReportService internal transfer/settlement/fee/Cash Flow tests passed |

Posting mechanics use a serializable transaction when owning one, in-transaction idempotency recheck, sorted account loads, tenant/account validation, immutable journal/line controls, atomic balance updates and unique `(TenantId, IdempotencyKey)` / journal-number indexes. Lifecycle locks orders/settlement SQL truth for mutation/replay and derives journal authority from current batch membership, not payload amounts.

**Scope distinction:** source mechanics are not a positive distributed-settlement concurrency certification. No claim is made that the dormant settlement executor has passed a new concurrent-success/remittance/reversal E2E test. Current executable containment has been verified instead. Generic transfer UI creates a new operation ID per submission; ambiguous network retry identity across separate user resubmissions was not redesigned here.

## 16. Settlement lifecycle, reversal, audit and remaining dormant gaps

The retained service's intended steps are: validate tenant/order set/currency/amount/fees → locked collected orders with posted collection accounting → SQL batch/membership → authorized Settled transition + outbox/domain events → commit → SQL-reloaded immediate accounting or recovery → balanced Bank/fees/Clearing journal.

It does not establish today's authenticated carrier/provider remittance ingress. There is no certified carrier-remittance statement adapter or usable settlement execution confirmation/evidence UI. Provider/carrier assertion, supporting file name, text bank reference or operator confirmation is insufficient.

`FinancialPostingService.ReverseJournalAsync` exists as a ledger engine capability, but no certified COD/gateway settlement reversal command or provider-reversal reconciliation lifecycle was found. It was not exercised against shared history.

Two additional limitations were found in dormant paths:

- Collection/settlement CommerceAccounting posting requests do not populate `UserId`; the controlled collection and several existing business journals have `CreatedByUserId=null`. Source, order/batch ID, reference and domain events remain lineage, but a full actor-complete settlement certification cannot be claimed.
- Settlement bounds permit `fees == gross`, while `PostingLine.Debit` requires a positive Bank net amount. A zero-net historical settlement would fail journal generation. This is source-proven dormant behavior, not a successful test settlement or an active-ingress regression; positive integration must resolve this contract before certification.

## 17. Remediation delivered

Production changes are limited to existing Treasury presentation/report reading:

1. General Ledger account selector reads `getAccountSummaries`, not date-filtered Trial Balance.
2. Account/date filters are query-backed and survive reload/deep links.
3. Catalog/report failures are explicit and retryable; aborted obsolete reads cannot replace a current report.
4. GL explanatory text reflects selected account/date range and opening history; the header displays Closing Balance.
5. Treasury Hub, Clearing account viewer, Transfers and Clearing Aging explicitly explain that COD/gateway settlement is currently unavailable because authenticated remittance/payout evidence is not integrated.
6. English and Arabic strings added/updated.

Regression additions:

- six behavioral General Ledger UI tests;
- real PostgreSQL collection/journal/GL/balance and account-authority tests;
- repeated settlement containment test;
- five-role server permission matrix test;
- existing Treasury architecture assertion updated to follow the shared catalog helper rather than obsolete inline filtering;
- reproducible headed browser journey `tests/browser/main-cod-ledger-certification.js`.

This is **Outcome D / acceptance B**. No settlement executor, account reclassification, COD exception, backdoor direct ledger mutation, new permission grant or migration was added.

## 18. Database and migration safety

- Shared DB inspected before testing and after rejected probes/browser verification.
- `__EFMigrationsHistory`: **45 applied migrations**, latest `20261007190000_MAIN_HomepageMediaAssets`.
- `dotnet ef migrations list --project PointOfSale.Infrastructure/... --startup-project PointOfSale.API/... --no-build`: no pending marker.
- `dotnet ef migrations has-pending-model-changes ... --no-build`: **No changes have been made to the model since the last migration.**
- No model, mapping, migration, snapshot or shared accounting data change was required.
- Managed disposable PostgreSQL tests successfully provisioned fresh test databases with the existing EF chain and supplemental test schema machinery. This validates that test harness setup; it is not a new production migration Up/Down certification.
- Up/Down/rollback for a new migration: not applicable; none created or applied. No shared rollback attempted.

## 19. Headed browser, language and viewport evidence

CLI help and session ownership were checked. `open` completed successfully on this run; `playwright-cli list` identified `opencode-ui-test`, msedge, in-memory, **headed=true**. The existing registered session was reused; no repeated open, persistent profile, default-profile attachment or unrelated browser termination was used.

Real Owner login was performed through the login form. Treasury sidebar navigation was expanded normally. Initial attempts to click collapsed/duplicate sidebar links required the expanded Treasury section and scoped selector; this was a locator/navigation issue, not a missing accounting route.

The read-only browser certification was run four times:

| Variant | Viewport | Document/content direction | Result |
|---|---|---|---|
| English desktop | 1440×1000 | LTR | PASS |
| English mobile | 390×844 | LTR | PASS |
| Arabic mobile | 390×844 | RTL | PASS |
| Arabic desktop | 1440×1000 | RTL | PASS |

Each variant verified Hub COD in-transit card, click-through COD account, settlement-unavailability text, source/destination options exactly `1010` and `1030`, Clearing Aging disclosure, default GL account prompt, 33-account catalog, COD zero-history report, October Cashbox empty period, September four-line history, and exact account/date/history persistence after reload.

No document-level horizontal overflow occurred: mobile client/scroll width both375 (viewport390 with scrollbar), desktop1425 or1440 depending on vertical scrollbar. The ledger table keeps its local horizontal-scroll container. Screenshots were inspected, including Arabic mobile ledger and English desktop Hub. Currency, dates, direction and source labels localize; account names and historical descriptions retain server English fallback because this tenant's Treasury rows do not supply Arabic names.

The Hub's real **Refresh** action was also clicked after restoring English; balances remained Cashbox10, Bank0, all Clearing0. Session-local language was restored to English/LTR; merchant configuration was not edited.

Local screenshots: `.artifacts/cod-ledger-audit/{en-desktop,en-mobile,ar-mobile,ar-desktop}-{hub,transfers,ledger}.png`. CLI snapshots include pre-fix `.playwright-cli/page-2026-10-08T20-07-40-453Z.yml` and post-fix/reload snapshots. Test outputs and probes remain in local ignored artifacts; this report preserves exact financial observations and identifiers.

No new live storefront checkout was submitted. The two existing live COD orders were traced read-only; controlled collection was exercised in an isolated relational fixture. A real UI settlement was not performed because audit evidence proves that current ingress must reject it. Browser certification therefore covers the **correct branch-B workflow/readability and persistence**, not a positive provider-settlement journey.

## 20. Tests, builds, unrelated failures and cleanup

### Tests and builds

| Check | Result |
|---|---|
| Selected Treasury Web UI/catalog/architecture slice (7 files) | **33 passed**, 0 failed |
| Final General Ledger behavioral slice | **6 passed**, 0 failed (included in the33; rerun after final label correction) |
| Initial backend selected run | **79 passed**, 0 failed |
| Controlled real posting test | **1 passed**, PostgreSQL |
| Broader backend accounting/COD/settlement/security slice | **176 passed, 2 unrelated failures**, 178 total; see below |
| MAIN Web production build (final source) | PASS; Vite5240 modules, existing large-bundle warning |
| API build | PASS; 0 warnings, 0 errors on final incremental build |
| Pending model / migrations | No model change, no pending migration |
| Headed browser matrix | Four variants passed actual assertions |
| `git diff --check` | PASS; only Windows LF→CRLF notices |

Backend broader selection: T10SettlementContainmentTests, TreasurySettlementAuthorityCertificationTests, FinancialReportServiceTests, PaymentLifecycleSettlementAuthorityTests, CodPaymentLifecycleTests, PosPermissionAuthorizationTests, PosEndpointPermissionManifestTests, CommerceAccountingServiceTests. Results: `.artifacts/cod-ledger-audit/certification.trx`.

Important test-discovery limitation: `PointOfSale.Tests.csproj` excludes most `Accounting/*.cs` and selectively re-includes supported files. The old `TreasuryTransferServiceTests` and `FinancialPostingServiceTests` are **not compiled** despite their names being present in the initial filter. They are not counted as executed. Real posting/treasury guard coverage added to the compiled PostgreSQL COD integration suite supplies the evidence described here.

### Unrelated failures and diagnostic corrections

The two broader permission-manifest failures are outside changed Treasury authority:

1. `LegacyRoleAttributes_MustNotNarrowCanonicalPermissionGrants`: `AdminOrdersController.DownloadCheckoutAttachment` grants `storefront.orders.view` to Manager but legacy Owner/Admin role metadata narrows it.
2. `SensitivePosActions_MustResolveToKnownPermissionPolicies`: `HomepageMediaController.Preview` and `.Upload` have no classified POS permission/separate documented authority.

No changed Treasury/COD/GL assertion failed. Existing backend analyzer warnings and Vite's large-chunk warning remain. Initial browser auth/bootstrap failures disappeared after login/reload; deliberate reject-only requests naturally generated console HTTP errors. No positive settlement result was inferred from these logs.

Read-only scratch discovery initially queried nonexistent `TenantPaymentMethods.IsActive`; it was corrected to `IsEnabled` after inspecting the entity. An older unrelated test database lacked `Tenants`; fixture inventory was changed to skip absent schemas. Neither error mutated data or establishes a production schema mismatch. One simultaneous browser diagnostic read was interrupted by navigation and was rerun separately. These are audit-tool corrections, not financial remediation failures.

### Fixture cleanup

The managed test harness resets disposable fixture tables between tests; the final broader run's exact recorded collection fixture was removed by a subsequent harness reset. Its journal/ID evidence remains in the TRX and this report.

All **three** PostgreSQL COD databases created during this session were subsequently torn down as whole disposable test environments. Cleanup used an exact database/tenant allow-list, required an exclusive `COD Payment Lifecycle Tenant` created after this audit started, and used ordinary `DROP DATABASE` without FORCE or terminating connections. Each post-cleanup `pg_database` query returned **remaining=0**:

| Disposable database | Final fixture tenant before teardown |
|---|---|
| `pointofsale_tests_codpaymentlifecycletests_0b_daf1959f_6f3bc8c0` | `4ce96df1-dc33-4cb5-b068-1867da08c333` |
| `pointofsale_tests_codpaymentlifecycletests_0b_daf1959f_b780f3f1` | `a98f9ecf-b78f-4d88-b4f0-25a60022ef99` |
| `pointofsale_tests_codpaymentlifecycletests_0b_daf1959f_85911ce3` | `8d122132-60df-4797-a97a-eef7552f07f3` |

No shared order/account/journal was deleted or altered. No real accounting history was repaired manually. Other pre-existing test databases were left alone. Shared AhramStore data remains 13journals/30lines, Clearing0, Cashbox10, Bank0; no disposable accounting record was inserted into that tenant.

### Remaining limitations and final acceptance

- Authenticated carrier remittance/provider payout integration and positive settlement execution remain unavailable.
- No positive COD→Bank or COD→Cashbox financial E2E PASS, reversal PASS, or actor-complete settlement audit PASS is claimed.
- Full employee HTTP/browser login matrix was not run with real staff accounts; server policy-handler matrix is tested and Owner runtime authority is verified.
- Dormant zero-net/fee-equals-gross contract and missing posting actor lineage need resolution before future positive settlement certification.
- Two unrelated permission-manifest failures prevent a repository-wide all-tests-green claim.

**Acceptance B is satisfied:** current architecture and live rejection prove there should be no direct COD Clearing→Main Cashbox settlement/transfer in the current application contract; ledger selection and UI disclosure now reflect actual accounting truth without fabricating money movement. The source-owned future Bank settlement shape is documented, not invented or re-enabled. No commit was made.

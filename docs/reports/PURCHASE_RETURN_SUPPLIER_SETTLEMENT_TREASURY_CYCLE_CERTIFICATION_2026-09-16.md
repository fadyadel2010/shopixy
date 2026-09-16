# Purchase → Purchase Return → Supplier Settlement → Treasury → GL → Cash Flow Certification - 2026-09-16

## 1. Executive Verdict

**Purchase-return supplier-settlement cycle: PASS for the controlled AhramStore remediation.**

The pending purchase-return cash-settlement model change was applied safely to the configured controlled database, EF reports no pending model changes, and three read-only AhramStore captures share one deterministic fingerprint. The controlled baseline is financially balanced, fully journal-linked, and unchanged by migration, final tests, runtime health checks, and authenticated browser verification.

Migration `20260916120000_POS_PurchaseReturnCashSettlement` is now the terminal migration. Focused tests passed 55/55, distributed purchase-return reliability passed 13/13, the Release solution build passed with 0 errors, the Vite production build passed with 5217 modules transformed, and Edge passed 2/2 purchase-return settlement viewports with zero business mutations. No Shopixy or greatone data was modified and no arbitrary AhramStore mutation was performed.

## 2. Certification Scope

This certification covers the complete purchase → purchase-return → supplier-settlement → Treasury → General Ledger → Cash Flow path for the controlled AhramStore tenant only: immutable settlement snapshots, authoritative cash-cap validation, deterministic supplier ordering, supplier cash-receipt offsets, warehouse cashbox resolution, balanced purchase-return journals, Treasury inflow authority, outbox linkage, replay protection, concurrency protection, PostgreSQL cash-settlement integration, UI settlement choice, migration safety, read-only reconciliation, runtime health, and desktop/mobile browser proof.

Sales-return settlement, customer-ledger semantics, provider/card acquiring, marketplace/CommerceHub finance, affiliate finance, inventory valuation redesign, tax-account redesign, historical rewrites, manual journals, and unrelated frontend work remain out of scope.

## 3. Safety Boundary

No SubAgent was used. Repository HEAD is `f939e6b7e04869f366f06c7d0d7092443192ff37`. No reset, restore, clean, staging, commit, amend, or unrelated-file modification was performed.

Shopixy and greatone were never written. AhramStore was observed only through `REPEATABLE READ`, read-only, rolled-back snapshots and GET-only authenticated runtime traffic. Non-GET `/api/**` traffic was blocked inside the browser harness. The supplied human account credentials were not needed, not stored, and are not part of evidence; browser authentication used the existing Development-only DB-bound test-token endpoint with an in-memory bearer header.

## 4. Controlled Tenant and Reference Baseline

The controlled tenant is AhramStore, `c9c5c1ae-949e-498e-bc63-07242daeb49d`.

The authoritative pre-change baseline is `C:\Users\fadya\AppData\Local\Temp\opencode\purchase-cycle-ahram-baseline-20260916.json`, captured at `2026-09-16T12:11:23.4347275Z` from database `PosEcommerceDb_Baseline`, transaction isolation `repeatable read`, transaction read-only `on`, tool version `1.0.0`, tenant scope exactly AhramStore.

Its deterministic evidence fingerprint is `3d21aa92f8b4fcecb17814b1cd155f68e7b88518a5658cbda1ce7efdd06f7a9c`. The same fingerprint is returned by the post-migration and post-runtime captures below, proving no business-state drift.

## 5. Settlement Contract and Authority Model

Supplier account credit remains the default purchase-return settlement. Purchase returns support only two persisted snapshots: account credit with zero settlement amounts, or full cash refund with `CashRefundAmount == TotalAmount`.

Card and mixed refunds fail closed because no authoritative inbound supplier-card mapping exists. A cash refund must equal the authoritative server-computed return total and cannot exceed remaining cash paid on the original purchase invoice. Cash settlement produces one balanced journal containing the return reversal plus `Dr Cash / Cr Supplier Payables`. Supplier ledger records `PurchaseReturn` followed by `PaymentReceived`, preserving the supplier balance after cash settlement.

The active idempotency fingerprint includes `RefundToCash` and `RefundAmount`; legacy non-cash fingerprints remain accepted only for non-cash requests. PostgreSQL timestamp precision requires a one-millisecond persisted settlement offset rather than tick precision.

## 6. Production Implementation - Controller

`PointOfSale.API/Controllers/PurchaseReturnsController.cs:354` builds both the current and legacy purchase-return fingerprints. `PurchaseReturnsController.cs:360` and `PurchaseReturnsController.cs:987` accept either fingerprint through `PurchaseReturnFingerprintMatches`, but legacy acceptance is explicitly gated on `!refundToCash`.

`PurchaseReturnsController.cs:588` recomputes the authoritative return total from pending lines. `PurchaseReturnsController.cs:590` rejects a cash request whose supplied refund amount differs from that authoritative total. `PurchaseReturnsController.cs:600` resolves original cash paid from purchase payment records with legacy invoice payment-method fallback. `PurchaseReturnsController.cs:608` rejects cash refunds exceeding remaining cash capacity after deducting prior cash refunds.

`PurchaseReturnsController.cs:769` stamps the immutable snapshot as `RefundMethod.Cash` with full total or `RefundMethod.AccountCredit` with zero. `PurchaseReturnsController.cs:823` applies the deterministic one-millisecond settlement timestamp and `PurchaseReturnsController.cs:827` plus `PurchaseReturnsController.cs:840` record the supplier `PaymentReceived` cash-receipt offset with the same return number.

## 7. Production Implementation - Accounting and Treasury Authority

`PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs:937` validates every purchase-return settlement before posting. `CommerceAccountingService.cs:948` resolves the warehouse cashbox only when a cash refund exists. `CommerceAccountingService.cs:969` posts one journal with `SourceType = PurchaseReturn` and idempotency key `PURCHASE_RETURN:{tenant}:{returnId}`. `CommerceAccountingService.cs:2052` adds the matched `Dr Cash / Cr Supplier Payables` settlement lines.

`PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs:676` routes Treasury cash authority for `PurchaseReturn` to `ResolvePurchaseReturnCashAuthorityAsync`. `FinancialPostingService.cs:751` revalidates return identity, tenant, purchase-return type, cash method, full-total equality, zero card/credit amounts, original invoice existence, original cash paid, and cumulative cash refunds before authorizing Treasury inflow. Forged cash postings without original cash payment return no authority and fail closed.

`PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs:295` and `AccountingOutboxProcessor.cs:2058` retain the synchronous purchase-return enqueue and retry posting contract.

## 8. Database Contract - Migration and Constraint

`PointOfSale.Core/Entities/Return.cs:48` declares nullable `RefundMethod` with `CashRefundAmount`, `CardRefundAmount`, and `CustomerCreditAmount`. The comment explicitly limits purchase returns to supplier account credit or full cash refund.

`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1904` enforces `CK_Returns_SettlementSnapshot_Consistent`: legacy null-method rows must carry zero amounts; sales returns retain cash/card/mixed/credit shapes; purchase returns allow only cash-full-total or account-credit-zero.

Migration `PointOfSale.Infrastructure/Migrations/20260916120000_POS_PurchaseReturnCashSettlement.cs:19` replaces the old constraint with that purchase-return-aware definition. Its `Down` method at line `37` clears purchase-return methods before restoring the legacy constraint, making rollback explicit and safe.

## 9. Frontend Settlement Experience

`PointOfSale.Web/src/pages/PurchaseReturns.jsx:51` owns explicit `refundToCash` state defaulting to account credit. `PurchaseReturns.jsx:256` sends both `refundToCash` and `refundAmount`, with refund amount equal to the live total only for cash. `PurchaseReturns.jsx:605` renders the account-credit choice and `PurchaseReturns.jsx:618` renders the cash choice through radio inputs named `purchaseReturnSettlement`.

English labels expose `Purchase Returns`, `Refund to Cash`, supplier account credit, and cashbox effect copy. Arabic peers expose purchase-return and cash-refund wording. Client prices remain read-only invoice snapshots and are excluded from replay fingerprints by the architecture certification.

## 10. Purchase Evidence

The controlled purchase invoice is `d74094bf-1479-4f1a-b829-25fbee61cf44`, `PUR-20260916-1014`, supplier `0e887580-5737-4801-bae3-dc24aa56759f`, subtotal `800`, tax `80`, total `880`, paid `880`, payment method Cash, warehouse `2bca8d91-2c09-450e-a713-e783b9cf3d7a`.

Its purchase journal is `01a0aa05-a3a4-72ba-8e8e-ebee3d2e0d1d`, `JE-2026-000010`, source type `PurchaseInvoice`, with three ordered lines. Earlier working notes naming a different purchase number or a `1760` total are superseded by this ID-anchored read-only row; the cash movement below explains the current cash position.

## 11. Purchase Return Evidence

The controlled purchase return is `c29ec513-c890-490d-b36c-f14eb71a962e`, `PR-20260916-1012`, linked to original purchase invoice `d74094bf-1479-4f1a-b829-25fbee61cf44` and original invoice number `PUR-20260916-1014`.

It carries subtotal `400`, tax `40`, total `440`, quantity `1`, unit price `400`, cost snapshot `400`, null `RefundMethod`, and zero cash/card/credit amounts. This is therefore the legacy account-credit shape, not a cash settlement. Its return item is `d7cf5522-863e-4188-a5d2-8f6c17fa6878` with total `440`. Earlier working notes using another return number are superseded by this ID-anchored row.

## 12. Return Journal Evidence

The controlled return journal is `01a0aa06-65ba-787d-8032-0709f9065a09`, `JE-2026-000011`, source type `PurchaseReturn`, source ID `c29ec513-c890-490d-b36c-f14eb71a962e`, source reference `PR-20260916-1012`, with three ordered lines.

This preserves the existing account-credit journal shape. The neighboring `JE-2026-000010` belongs to the original purchase invoice, not the return. No duplicate purchase-return journal exists.

## 13. Supplier Ledger Evidence

Supplier transactions total 13 rows. The controlled supplier `0e887580-5737-4801-bae3-dc24aa56759f` owns the purchase credit of `880`, the cash payment debit of `880`, the purchase-return debit of `440` with reference `PR-20260916-1012`, and the related non-cash supplier adjustments.

The return ledger entry is `783da2a3-1231-4557-8994-37622c47fb8b`, debit `440`, balance after `440`, transaction type purchase return. Supplier adjustments `0d37919e-cd7c-40b7-b56a-cdbbd80eac4e` and `be63e7e9-17bc-4f16-988f-352d1362f412` map to `DN-20260916114449` and `CN-20260916114502` with journals `JE-2026-000012` and `JE-2026-000013`, preserving supplier-balance continuity.

## 14. Treasury Evidence

The controlled tenant retains six Treasury accounts, one initial capitalization, one capitalization allocation, zero Treasury transactions in the snapshot window, one supplier-card funding instrument, and one supplier-card funding row.

No Treasury mutation was introduced by migration, tests, runtime health checks, or browser verification. Cash authority remains mapped Cashbox/Bank; clearing and supplier-card funding assets remain non-cash with unchanged report visibility.

## 15. General Ledger and Trial Balance Evidence

Journal totals are balanced: 13 entries, 30 lines, debit `3880`, credit `3880`, variance `0`. Trial Balance is balanced with debit `3880`, credit `3880`, variance `0`. Balance Sheet is balanced with assets `565`, liabilities plus equity and current earnings `565`, variance `0`.

Account `1010` is present with cached balance `10` and ledger balance `10`. This is consistent with the earlier `890` cash position less the controlled `880` cash-paid purchase: no unexplained cash movement exists. Accounts `1020`, `1030`, `1110`, `1120`, `1130`, `1140`, `1310`, `1410`, `2010`, `2105`, `3010`, `4010`, `4020`, `5010`, `6010`, `6020`, and `6110` retain their snapshotted cached/ledger parity.

## 16. Cash Flow Evidence

Cash Flow remains ledger-derived and is covered by the new `PR_CASH_01` reconciliation test for cash settlement. In the controlled snapshot, cash opening/closing parity holds through the same mapped Cashbox/Bank authority used by Treasury.

The current AhramStore cash closing is `10` cached and `10` ledger-derived, variance `0`. Supplier-card closing remains `35`. Net profit is `-440`, operating expenses are `440`, revenue is `0`, and cost of goods sold is `0` in this controlled window. No prefix-based cash inference was reintroduced.

## 17. Outbox and Idempotency Evidence

Accounting outbox totals 11 rows: 5 purchase invoices, 1 purchase return, 1 purchase reversal, 1 supplier payment, 2 party-ledger adjustments, and 1 Treasury initial capitalization. Pending is `0`, failed is `0`, and unlinked is `0`.

The controlled purchase-return outbox row links business entity `c29ec513-c890-490d-b36c-f14eb71a962e` to journal `01a0aa06-65ba-787d-8032-0709f9065a09` with three ordered lines. Exact replay returns the same `ReturnId`; exactly one processed, journal-linked, nonfailed purchase-return outbox row exists.

## 18. Replay and Concurrency Protection

Current fingerprints include supplier, warehouse, return date, original invoice, ordered items, reason, notes, `RefundToCash`, and `RefundAmount`. Legacy fingerprints without settlement fields remain accepted only when the incoming request is non-cash, preserving backward compatibility without allowing a legacy replay to become cash.

Concurrency tests prove duplicate submissions, parallel purchase-return claims, and supplier financial replays do not create duplicate returns, journals, supplier offsets, or outbox rows. The one-millisecond persisted settlement timestamp preserves deterministic supplier ordering under PostgreSQL timestamp precision.

## 19. Negative and Fail-Closed Evidence

Unsupported settlement snapshots are rejected before posting. Cash amount mismatch, cash refunds without original cash payment, cash refunds exceeding remaining original cash capacity, card/mixed purchase-return settlement, and forged Treasury cash inflow all fail closed.

`FinancialPostingServiceTests.cs:583` proves a forged purchase-return cash inflow is rejected with `InvalidJournalException` and produces no effects. `PR_CASH_02` proves a cash refund without original cash payment fails without journal, supplier, Treasury, GL, or Cash Flow effects.

## 20. PostgreSQL Integration Evidence - Cash Settlement

`PR_CASH_01_Cash_Refund_Settles_Supplier_And_Reconciles_Treasury_GL_And_Cash_Flow` proves a valid full cash refund settles the supplier, reconciles Treasury, GL, and Cash Flow, preserves exactly one journal-linked outbox row, and preserves supplier balance through the ordered `PurchaseReturn` plus `PaymentReceived` pair.

`PR_CASH_02_Cash_Refund_Without_Original_Cash_Payment_Fails_Without_Effects` proves the forged-cash path leaves no durable financial effects. Both tests run against PostgreSQL at `192.168.1.5:5432` with the corrected stale projection fixture using published status and authoritative affiliate category/policy seeding.

## 21. Unit, Architecture, and Focused Test Evidence

The final focused gate passed 55/55 across `CommerceAccountingServiceTests`, `FinancialPostingServiceTests`, and `ReturnFinancialAuthorityCertificationTests`, including cash-journal construction, unsupported-settlement rejection, forged Treasury inflow rejection, expanded settlement-snapshot coverage, and frontend authority/replay-fingerprint contracts.

The distributed purchase-return gate passed 13/13 across `PurchaseReturnDistributedReliabilityIntegrationTests`, `PurchaseReturnsConcurrencyIntegrationTests`, and `SupplierFinancialIdempotencyTests`. Toolchain versions are .NET `9.0.308`, Node `v22.20.0`, and npm `10.9.3`.

## 22. Solution Build Evidence

`dotnet build PointOfSale.sln -c Release --no-restore` passed with 0 errors. It reported 425 warnings, all pre-existing xUnit analyzer and nullable-contract style warnings, with no new build error introduced by the purchase-return remediation.

The build result supports certification because the scoped production and test deltas compile cleanly and all required test gates execute from the same Release output.

## 23. Frontend Production Build Evidence

`npm run build` passed with 5217 modules transformed. The only warning is the repository’s existing large-chunk size warning for the approximately 5218 kB main bundle.

The purchase-return settlement UI, English/Arabic labels, request payload fields, and settlement radio behavior are therefore included in a successful production bundle.

## 24. Migration Application Evidence

`dotnet ef database update 20260916120000_POS_PurchaseReturnCashSettlement` completed with `Applying migration 20260916120000_POS_PurchaseReturnCashSettlement` followed by `Done`.

`dotnet ef migrations list` now terminates with `20260916120000_POS_PurchaseReturnCashSettlement`. `dotnet ef migrations has-pending-model-changes` reports `No changes have been made to the model since the last migration`. The model snapshot and database constraint are therefore synchronized.

## 25. Read-Only Reconciliation Evidence - No Drift

Post-migration capture `purchase-cycle-ahram-postmigration-20260916.json` at `2026-09-16T12:56:10.3337034Z` and post-runtime capture `purchase-cycle-ahram-postruntime-20260916.json` at `2026-09-16T13:02:29.5587062Z` use the same database, tenant scope, `REPEATABLE READ`, read-only, rollback, and harness version as the baseline.

Both return fingerprint `3d21aa92f8b4fcecb17814b1cd155f68e7b88518a5658cbda1ce7efdd06f7a9c` and identical counts: 13 journals, 30 lines, 11 outbox rows, 5 purchases, 1 return, 1 return item, and 13 supplier transactions. Migration, tests, runtime health checks, and browser verification caused no AhramStore accounting drift.

## 26. Authenticated Runtime and Health Evidence

The API was already running for final verification. `http://localhost:5010/health/live` returned HTTP 200 and the web root at `http://localhost:5173` returned HTTP 200.

Subsequent browser certification used GET-only authenticated traffic with DB-bound test tokens. No purchase return, supplier payment, transfer, settlement, or other business mutation was submitted during runtime/browser proof. Aggregate external-dependency health was not used as a financial gate.

## 27. Desktop and Mobile Browser Certification

The temporary Playwright harness `.opencode-tmp/purchase-return-browser` used Microsoft Edge Chromium headless, blocked every non-GET/HEAD/OPTIONS `/api/**` request, and certified `/purchase-returns` creation view for AhramStore.

Desktop `1440x1000` and mobile `390x844` both passed all 11 assertions: authenticated route, two settlement choices, account-credit text, cash-refund text, cashbox-effect text, account-credit default, cash-choice selectability, no business mutation, no failed API responses, no request failures, and no unexpected console errors. Evidence is stored under `C:\Users\fadya\AppData\Local\Temp\opencode\purchase-return-browser-evidence` with desktop/mobile screenshots and `browser-evidence.json`.

## 28. Diff Hygiene and Working-Tree Evidence

`git diff --check` reports no whitespace errors; it emits only pre-existing LF-to-CRLF working-copy notices. `git diff --stat` reports 11 modified files with 678 insertions and 28 deletions across API, Core, Infrastructure, snapshot, tests, and web UI.

Including the untracked migration file, the scoped change set is 12 files: 11 modified plus `PointOfSale.Infrastructure/Migrations/20260916120000_POS_PurchaseReturnCashSettlement.cs`. No unrelated production file was edited for this remediation beyond the scoped settlement implementation, constraint, tests, labels already in baseline, and certification report.

## 29. Limitations and Residual Risks

Full frontend `npm test -- --run` was not used as a certification gate because it has unrelated baseline failures and timed out after 120 seconds across payment, storefront, warehouse, footer, UX-state, and Axios-timeout suites. `msedge --version` is absent from `PATH`, but the installed executable at `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe` was used successfully through Playwright channel `msedge`.

The controlled AhramStore baseline already contains a legacy null-method account-credit return; that row remains valid under the new constraint and was not rewritten. Arabic PDF shaping, Excel sheet-name truncation, native date-input chrome, and concurrent worktree changes remain outside this cycle. Future supplier-card inbound authority, if ever introduced, must remain a separate explicit migration and authority review.

## 30. Final Classification and Readiness

**Final classification: PASS.**

Account credit remains the safe default, full cash refunds are now authoritative, exact, cash-capped, deterministically ordered, supplier-offset, cashbox-resolved, balanced, Treasury-authorized, outbox-linked, replay-safe, concurrency-safe, and fail-closed for card/mixed/forged paths. Migration is applied and synchronized, read-only financial evidence is unchanged across baseline, post-migration, and post-runtime captures, focused and distributed tests pass, solution and web builds pass, and authenticated desktop/mobile browser proof passes without mutation.

The implementation is eligible to proceed with no staging or commit performed by this certification pass.

# Treasury T-06D Initial Capitalization Authority Audit - 2026-09-13

| Audit property | Value |
| --- | --- |
| Date | 2026-09-13 |
| Target tenant | `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`) |
| Mode | Discovery and product design only; source inspection, prior browser evidence, and read-only PostgreSQL reconciliation |
| Implementation status | **NOT IMPLEMENTED** |

## 1. Executive Verdict

**VERDICT: THE BLOCK IS INTENTIONAL AND CORRECT, BUT THE PRODUCT LACKS A BOUNDED INITIAL-CAPITALIZATION AUTHORITY.**

The application correctly refuses to create Cashbox or Bank cash from an amount supplied to a generic opening-balance or direct cash-in endpoint. A net increase to an active Cashbox/Bank Treasury asset is centrally rejected unless current same-tenant SQL proves an accepted source document. Transfers and deposits only move existing cash, so they cannot bootstrap a tenant whose authorized Treasury assets are zero.

The missing product capability is not a balance override. It is an immutable, tenant-owned **Treasury Initial Capitalization** source document through which the tenant Owner attests that cash or bank funds existed at accounting cutover. That document should authorize exactly one balanced posting:

| Side | Account | Meaning |
| --- | --- | --- |
| Debit | Active same-tenant Cashbox or Bank Treasury asset | Opening cash/bank position enters the ledger |
| Credit | Seeded same-tenant `3010 Opening Balance Equity` | Imported net opening position |

The first implementation should be Owner-only, one active document per tenant, allocation-based across Cashbox/Bank Treasury accounts, idempotent, immutable after posting, and corrected only by reversal and replacement. It must not enable generic cash-in, alter `CurrentBalance` directly, weaken `PreventNegative`, or treat `1040`, `2030`, `1020`, `1110`, supplier-card assets, provider evidence, or a caller-selected GL account as capital authority.

This authority is appropriate only for cash that predates system cutover. A contribution made after cutover requires a separate ordinary owner-contribution workflow and permanent owner-capital Equity account. An owner loan requires a distinct configured Liability account and source document. Neither should be silently represented by `3010`.

## 2. Current Opening-Balance Architecture

Opening balance is not one uniform domain in the current application:

| Area | Current source record | Ledger treatment | Current status |
| --- | --- | --- | --- |
| Customer | `AccountLedger` opening transaction | Receivable against `3010` | Active, outbox-backed |
| Supplier | `AccountLedger` opening transaction | Payable against `3010` | Active, outbox-backed |
| Warehouse stock | Inventory opening workflow | Inventory authority, separate from Treasury | Active |
| Cashbox | Legacy `Cashbox.OpeningBalanceAmount/Date` metadata | Non-zero value blocked before lookup/posting | Zero metadata only |
| Bank | Legacy `Bank.OpeningBalanceAmount/Date` metadata | Non-zero create blocked; bulk write HTTP `410` | Retired |
| Treasury | Ledger journal plus active Treasury mapping | Generic direct cash-in HTTP `410` and service exception | Retired |

`CommerceAccountingService` validates party opening source rows and posts their two-line journals using tenant-qualified account resolution (`CommerceAccountingService.cs:470-543`, `616-689`). This is the relevant precedent: the business source row exists first, supplies a stable source ID, and is then consumed by the posting authority.

Cashbox and Bank legacy metadata are not financial authority. Treasury balances are derived from `LedgerJournalEntries` and `LedgerLines`; `LedgerAccounts.CurrentBalance` is only a posting-maintained materialization. `ApplicationDbContext.GuardTreasuryCashAuthority` prevents arbitrary ledger-line insertion, account-balance mutation, journal creation, and journal-lineage mutation (`ApplicationDbContext.cs:7028-7113`).

## 3. Why Cashbox Opening Is Blocked

`POST /api/cashbox/{id}/opening-balance` is restricted to Owner/Admin, but it rejects `request.Amount != 0` before loading the Cashbox or opening a transaction. The response is HTTP `400`, code `OPENING_BALANCE_AUTHORITY_REQUIRED`, and the message states that Cashbox opening balances cannot create cash (`CashboxController.cs:1261-1279`).

The remaining body can set zero-valued legacy opening metadata. Its unreachable positive branch resolves a dedicated Cashbox ledger account and calls `CashInAsync` with `1040 Cash Float Reserve` as the offset (`CashboxController.cs:1282-1335`). `CashInAsync` itself always throws because direct cash-in is retired (`TreasuryOperationService.cs:259-273`). These are defense-in-depth barriers, not a partially enabled funding workflow.

The block is necessary because the request contains only an amount, date, and notes. It has no external-capital source document, owner attestation, approval identity, stable caller operation ID, evidence reference, or independently bounded amount. Accepting that request would let a UI amount become cash authority.

## 4. Why Treasury Inflow Is Blocked

`POST /api/treasury/operations/cash-in` returns HTTP `410 Gone`, code `TREASURY_INFLOW_WORKFLOW_REQUIRED`, for Owner/Admin (`TreasuryController.cs:747-761`). The service boundary independently rejects direct cash-in (`TreasuryOperationService.cs:259-273`). Re-enabling either layer would bypass the certified T-02 rule that Treasury inflow must originate from current SQL payment or accounting source truth.

`FinancialPostingService` calculates the net increase across active same-tenant Cashbox/Bank Treasury mappings. When the increase is positive, it accepts only enumerated source types whose amount and tenant are reloaded from SQL, plus bounded reversal (`FinancialPostingService.cs:614-733`). A label such as `InitialCapital`, a route amount, or a `3010` credit is currently insufficient and correctly fails.

Therefore a capitalization implementation needs both elements in one transaction:

- A persisted, current, same-tenant source document that owns the attested amount and lifecycle.
- An explicit `FinancialPostingService` authority branch that reloads that document and cumulatively bounds Treasury increases to its approved total.

## 5. Accounting Models

Three economically different events must not share one ambiguous API:

| Event | Timing | Required posting | Product treatment |
| --- | --- | --- | --- |
| Initial cash at cutover | Funds existed before books begin in this system | Dr Cashbox/Bank; Cr `3010` | T-06D bounded opening capitalization |
| Later owner contribution | New owner funds arrive after cutover | Dr Cashbox/Bank; Cr dedicated Owner Capital Equity | Separate future financing workflow |
| Owner/shareholder loan | Repayable funds arrive | Dr Cashbox/Bank; Cr dedicated Owner Loan Liability | Separate future debt workflow |

`3010` is suitable for imported opening position because its seeded description is "Equity offset account for imported opening balances" (`SystemAccountTemplate.cs:388-398`). It should not become a permanent catch-all for later contributions. If later contributions are required, seed an explicitly named Equity account. If loans are required, seed an explicitly named Liability account and define repayment/outflow authority. `2030 Treasury Suspense` is not either one.

The initial-capitalization document is an accounting assertion by an authorized tenant Owner. The application cannot prove physical notes or an external bank balance by itself. It can make the assertion explicit, attributable, immutable, bounded, and reconcilable, and can require an external reference or cash-count reference as evidence.

A repository-wide domain/source search found no existing `OwnerCapital`, `OpeningCapital`, `CapitalContribution`, `OwnerLoan`, or equivalent source entity/workflow. A new bounded document is therefore required; there is no dormant authority to re-enable.

## 6. Chart of Accounts Evidence

| Code | Seeded name | Type | `PreventNegative` | Treasury mapping | T-06D disposition |
| --- | --- | --- | --- | --- | --- |
| `1010` | Cashbox Main | Asset | true | Cashbox | Eligible debit destination |
| `1030` | Bank Account | Asset | true | Bank | Eligible debit destination |
| `1040` | Cash Float Reserve | Asset | true | None | Ineligible source/destination |
| `2030` | Treasury Suspense | Liability | false | None | Ineligible capitalization offset |
| `3010` | Opening Balance Equity | Equity | false | None | Fixed credit offset for cutover only |
| `1020` | POS Visa Account | Asset | false | None | Protected and ineligible |
| `1110` | Visa Clearing | Asset/Clearing | false | Clearing | Protected and ineligible |

The account templates establish `1010`, `1030`, and `1040` as Assets (`SystemAccountTemplate.cs:90-143`), `2030` as a temporary adjustment Liability (`SystemAccountTemplate.cs:337-347`), and `3010` as the imported-opening Equity offset (`SystemAccountTemplate.cs:388-398`).

`1040` cannot create capital. Debiting `1010` and crediting `1040` is an asset-to-asset float transfer and requires `1040` already to hold value. Allowing it below zero would fabricate an asset credit balance and violate `PreventNegative`. `2030` would fabricate an unexplained liability. `1020` and `1110` represent customer-card/clearing authority, not owner funds.

## 7. Tenant Seeding

`TenantAccountSeeder.SeedAsync` creates system accounts, payment methods, and Treasury mappings per tenant (`TenantAccountSeeder.cs:37-45`). It creates missing templates with `IsSystem=true` and tenant ownership (`TenantAccountSeeder.cs:49-130`), then creates default `1010` Cashbox and `1030` Bank Treasury mappings (`TenantAccountSeeder.cs:235-320`). Tenant registration invokes the full seeder, while startup reconciliation adds missing account templates for active tenants.

The read-only database snapshot found `3010` in every inspected active tenant:

| Tenant | `3010` ledger-derived balance |
| --- | ---: |
| AhramStore | `0.0000` |
| AntonNewStore | `0.0000` |
| AntonStore | `12300.0000` |
| greatone | `-100.0000` |
| highquality | `0.0000` |
| shopixy | `0.0000` |

The journal-usage query found `3010` only under `CustomerOpeningBalance` and `SupplierOpeningBalance`: AntonStore had three customer and four supplier opening journals, and greatone had one supplier opening journal. No inspected tenant had an owner-capitalization source type. This supports reuse of `3010` as the fixed cutover offset but does not establish any existing Treasury capitalization authority.

Seeding account definitions and zero-balance Treasury mappings is correct. Seeding money, a capitalization document, or a synthetic journal is not. T-06D must remain an explicit Owner action and must not be added to tenant startup, registration, Development seeders, or reconciliation jobs.

## 8. Opening vs Ordinary Inflow

Opening capitalization records a position that existed at the system's accounting cutover. Ordinary inflow records a new event after cutover, such as sale collection, customer payment, gateway settlement, or later owner financing. The distinction controls source evidence, date semantics, reporting, correction, and who may attest.

The proposed initial workflow must require an explicit cutover date and an Owner declaration that the allocations existed at that date. It should reject future dates. It should also reject creation once the tenant has any posted Cashbox/Bank Treasury movement, because after that point the opening position can no longer be distinguished safely from an ordinary inflow. Non-Treasury party-opening journals need not block it, but their dates must be consistent with the same cutover policy.

After an initial capitalization is posted, new money must enter only through already certified operating inflows or a separately designed financing source. The direct cash-in endpoint must remain retired.

## 9. Bank Capitalization

The legacy `Bank` table is master data and is not the Bank Treasury authority. `POST /api/banks` rejects a non-zero opening amount and persists `OpeningBalanceAmount/Date` as null (`BanksController.cs:101-151`). `POST /api/banks/opening-balances` returns HTTP `410` (`BanksController.cs:395-402`). The old Bank statement is based on legacy `BankTransactions`, not the certified Treasury ledger.

T-06D should target an active same-tenant `TreasuryAccount` of type `Bank` whose linked ledger account is active, same-tenant, Asset, non-clearing, and not deleted. It must not require or create a legacy `Bank` row. For AhramStore, the seeded `Primary Bank Account` maps to `1030` and is eligible even though the tenant has no legacy Bank master rows.

The document should require a bank statement/deposit reference when a Bank allocation is present. This reference is audit evidence, not independent cash authority and not a provider settlement substitute.

## 10. Cashbox Capitalization

The legacy `Cashbox` row is operational drawer metadata. AhramStore currently has no legacy Cashbox master rows, but it does have the seeded active `Main Cashbox` Treasury mapping to `1010`. A Treasury-first capitalization design can therefore initialize the authoritative Cashbox ledger without manufacturing an unrelated legacy row.

Eligible Cashbox allocations must resolve through active same-tenant `TreasuryAccountType.Cashbox` mappings. For dedicated operational cashboxes, the existing `CashboxLedgerAccountService` pattern may supply their mapped accounts, but the capitalization authority must still consume Treasury identity rather than trust a route account code.

The legacy `/opening-balance/cashbox` form should not be repurposed in place. It currently presents an amount as if that amount were enough to create cash. Replace its navigation with an Owner-only capitalization workspace or make it a zero-opening/read-only operational setup view. Keep the old non-zero endpoint fail-closed.

## 11. Reporting Impact

A correct `Dr 1010/1030, Cr 3010` journal has these effects:

| Report | Expected effect |
| --- | --- |
| General Ledger | One immutable source-linked debit and credit, plus any later reversal lines |
| Treasury account statement | Destination debit increases ledger-derived balance |
| Treasury dashboard/summary | Cash increases from the ledger, not metadata |
| Trial Balance | Debits equal credits |
| Balance Sheet | Assets and Equity increase equally |
| Profit and Loss | No effect; Equity is excluded from revenue/expense |
| Cash Flow | Opening cash if before report period; financing inflow if within report period |

No report should read the capitalization document amount as a balance. The document is source authority and audit lineage; the ledger remains report truth. Reversal must remain visible in history rather than deleting or rewriting the original.

## 12. Cash Flow Semantics

`FinancialReportService.GetCashFlowAsync` calculates opening/closing cash from ledger boundary snapshots and currently treats all `10xx`/`11xx` accounts as cash equivalents. It calculates financing as the net change across all Equity accounts (`FinancialReportService.cs:402-499`, `790-797`).

For an isolated capitalization posting, current behavior is mathematically correct:

- If the journal date is before `FromDate`, the amount is included in opening cash and contributes no current-period financing movement.
- If the journal date is within the selected period, cash increases and the `3010` credit produces an equal positive financing movement.
- Reversal within the period produces the opposite financing movement.

There is a broader pre-existing classification risk: all Equity changes are labeled financing, including customer/supplier opening offsets that may have no cash effect, and prefix-based cash equivalents include clearing-style `11xx` balances. A T-06D implementation must add report tests that combine capitalization with party openings and reversals. Prefer source-aware cash-flow classification for `TreasuryInitialCapitalization` rather than relying indefinitely on aggregate Equity change. Do not special-case the journal out of cash-flow reconciliation.

## 13. Treasury Semantics

Treasury cash authority remains the intersection of:

- An active, non-deleted, same-tenant Cashbox/Bank `TreasuryAccount` identity.
- Its active, same-tenant Asset ledger account.
- A balanced, immutable journal posted through `FinancialPostingService`.
- A current, same-tenant source document accepted and amount-bounded at the central posting boundary.

The proposed source type should be a fixed literal such as `TreasuryInitialCapitalization`. The source ID must be the capitalization document ID, not the tenant ID, user ID, Treasury account ID, or a caller-provided arbitrary GUID with no persisted row. The source reference should be a generated human-readable capitalization number plus any evidence reference.

Capitalization increases aggregate Treasury cash. It is not a Treasury transfer, deposit, settlement, clearing movement, customer payment, sale, or supplier-card funding. Existing transfer endpoints must not be used because their invariant is aggregate-neutral movement of already existing cash.

## 14. Authorization

Recommended v1 authorization is **tenant Owner only** for create, post, and reverse. Capitalization is a legal/economic assertion about owner-supplied opening net assets, not routine Treasury administration. Admin, Manager, Cashier, Warehouse, platform SuperAdmin, background workers, and storefront/customer identities must not be able to originate it.

Read access may be Owner/Admin if normal financial reports already use that boundary, but action controls and API authorization must independently require Owner. The UI route should use the existing role guard and the sidebar entry should declare `requiredRoles: ['Owner']`; menu hiding is not security.

Minimal v1 should use a single Owner action with an explicit declaration and confirmation summary. If the product later requires maker-checker governance, add Draft/Approved states and ensure creator and approver are different identities. Do not simulate approval by allowing Admin to post directly without a persisted approval model.

## 15. Idempotency

The client must send a stable `operationId` for a logical submission. The server should derive both document and journal idempotency from tenant plus operation ID, for example `TREASURY_INITIAL_CAPITALIZATION:{tenantId}:{operationId}`. Random IDs generated only after the request reaches the controller are not replay protection.

Exact retries must return the existing document and journal. Reuse of the same operation ID with a changed date, allocation, total, evidence reference, or declaration must return HTTP `409 CONFLICT` with zero mutation. The database should enforce tenant-qualified uniqueness on the document operation ID, while the existing unique `(TenantId, IdempotencyKey)` journal index provides a second boundary (`JournalEntryConfiguration.cs:18-26`).

Do not persist a successful idempotency artifact separately from a failed posting transaction. Document creation, journal posting, and final document status must commit or roll back together.

## 16. Concurrency

The entire create-and-post operation must run in one relational transaction. It should serialize eligibility for a tenant, recheck that no prior active capitalization or Treasury movement exists, create the document, resolve and lock destination ledger accounts in deterministic ID order, and invoke `FinancialPostingService` inside that transaction.

`FinancialPostingService` already rechecks journal idempotency inside a serializable transaction, tenant-qualifies accounts, orders account loading, and applies `PreventNegative` before commit (`FinancialPostingService.cs:35-124`, `120-233`, `566-610`). The capitalization service still needs a database uniqueness rule or tenant-scoped lock so two different operation IDs cannot both pass the one-time eligibility check concurrently.

Required concurrency result: two simultaneous first-capitalization requests with different keys yield exactly one posted document/journal; the loser receives a stable conflict after reloading committed state. No timeout, duplicate capitalization, partial document, or over-credit to `3010` is acceptable.

## 17. Auditability

The immutable capitalization source document should persist at least:

| Field | Purpose |
| --- | --- |
| `Id`, `TenantId`, generated number | Stable tenant-owned identity |
| `OperationId` | Caller-stable replay identity |
| `CutoverDate` | Accounting date attested by Owner |
| Allocation rows with Treasury and ledger account snapshots | Exact destination identity and amount |
| Total amount | Independent bound reloaded by posting authority |
| Fixed offset account snapshot (`3010`) | Prevent caller-selected equity/liability |
| Evidence/reference and notes | Cash-count or bank-statement traceability |
| Declaration version/text identifier | Meaning of Owner attestation |
| `CreatedByUserId`, `CreatedAtUtc` | Actor and system time |
| Posted and reversal journal IDs | Complete ledger lineage |
| Status/version | Lifecycle and optimistic concurrency |

Audit logs may mirror the action, but they cannot replace the source document or journal. Do not log authentication material, full bank account numbers, or uploaded sensitive evidence. API responses should expose stable codes and safe identifiers, not exception internals.

## 18. Reversal

Posted capitalization must never be edited or deleted. Correction requires Owner-only reversal through `FinancialPostingService.ReverseJournalAsync`, with a mandatory reason, stable reversal operation ID, current same-tenant source/document validation, and explicit linkage to the original journal. The original and reversing entries remain in account statements and reports.

A full reversal posts `Dr 3010, Cr destination Cashbox/Bank`. Existing `PreventNegative` must apply to the Treasury credit. If later operations consumed the capital and the destination lacks funds, reversal must fail with `INSUFFICIENT_FUNDS`; neither another account nor `1040` may be silently substituted.

For minimal v1, reject partial reversal and reject reversing only one allocation from a multi-allocation document. Reverse the complete document atomically, then permit one replacement document under the same opening eligibility lineage. A replacement must reference the reversed document and must not erase the fact that initial capitalization already occurred.

## 19. Multi-Tenant Safety

Every source-document, allocation, Treasury mapping, ledger account, journal, line, idempotency, eligibility, and reversal query must include the trusted tenant context. Route/body tenant IDs must not be accepted. `FinancialPostingService` already loads posting accounts with `TenantId == request.TenantId` (`FinancialPostingService.cs:126-143`).

Current schema controls include unique `(TenantId, AccountCode)`, tenant-qualified Treasury/account alternate keys and foreign keys, unique `(TenantId, AccountId)` Treasury mappings, and unique tenant journal idempotency (`AccountConfiguration.cs:18-24`; `TreasuryAccountConfiguration.cs:18-30`, `61-66`; `JournalEntryConfiguration.cs:18-36`). The new source and allocation schema should follow the same composite co-ownership pattern.

Tests must prove that a tenant cannot capitalize a foreign Treasury ID, foreign `3010`, foreign document, or foreign journal, even when query filters are bypassed. AhramStore capitalization must not change Shopixy, GreatOne, or any platform aggregate used as financial authority.

## 20. Browser/UI Review

Current routes are inconsistent with the backend authority:

| Surface | Evidence | Assessment |
| --- | --- | --- |
| `/opening-balance/cashbox` | Active protected React route; amount/date/notes form posts legacy endpoint (`App.jsx:259-283`; `CashboxOpeningBalance.jsx:37-85`) | Misleading because every non-zero amount is rejected |
| `/opening-balance/banks` | Redirects to `/treasury/account/1030` (`App.jsx:283`) | Safe read-only containment |
| `BankOpeningBalance.jsx` | Still contains retired bulk-write form (`BankOpeningBalance.jsx:64-97`) | Stale and unrouted |
| `/treasury/hub` | Reads ledger balances and dashboard (`TreasuryHub.jsx:94-168`) | Correct read surface |
| `/treasury/account/1010` and `/1030` | Read balance/movements from Treasury APIs (`TreasuryAccountViewer.jsx:62-98`) | Correct ledger statement surfaces |

The sidebar exposes Cashbox Opening Balance without `requiredRoles`, while role filtering only acts when that property is present (`Sidebar.jsx:168-178`, `350-390`). The API currently limits the write to Owner/Admin, but the visible route implies a capability that does not exist.

Existing same-day headed Edge evidence in the T-06B/T-06C reports proves the authenticated AhramStore Owner identity, EGP context, Treasury sources, responsive UI, and database non-mutation. This continuation made no browser mutation. Current unauthenticated HTTP checks returned `200` for API liveness and both SPA route documents; source and read-only SQL establish that AhramStore has no legacy Bank or Cashbox master rows. The proposed UI should provide an Owner-only review screen, allocation rows from backend-returned eligible Treasury identities, required evidence/reference, declaration, confirmation, result/journal link, and read-only reversal history.

## 21. Integration With T-01–T-06

| Prior authority | Required T-06D integration |
| --- | --- |
| T-01 Treasury cash authority | Preserve ledger-only cash truth, mutation scope, immutable lineage, and tenant co-ownership |
| T-02 Treasury inflow authority | Add exactly one SQL-backed, cumulatively bounded opening source; keep generic cash-in retired |
| T-03 Treasury outflow authority | No new ordinary outflow; reversal remains bounded by original journal and available destination funds |
| T-04 settlement containment | Do not use clearing, suspense, provider, deposit, or settlement evidence as capital |
| T-05 account-summary authority | Capitalization appears automatically through ledger-derived Cashbox/Bank summaries; no projection write |
| T-06 supplier-card authority | Creates a legitimate source balance from which later card funding may occur; never funds the card directly |

T-06D does not certify supplier-card funding by itself. After capitalization, T-06 still requires its own source selection, `PreventNegative`, idempotency, concurrency, document, journal, outbox, purchase/payment, reversal, and tenant-isolation invariants. `1020`, `1110`, and the dedicated supplier-card asset remain separate authorities.

The source type allow-list in `FinancialPostingService` is the central integration point. Adding only a controller/service that posts `Dr Cash, Cr 3010` would still fail, and bypassing that validation would regress T-02.

## 22. Minimal Safe Product Design

Recommended v1 product slice:

1. Add tenant-owned `TreasuryInitialCapitalization` and allocation rows with immutable Posted/Reversed lifecycle, Owner identity, stable operation ID, cutover date, reference, declaration version, totals, and journal lineage.
2. Add Owner-only create/read/reverse endpoints under `/api/treasury/initial-capitalizations`; apply `FinancialWrite` rate limiting and stable error contracts.
3. Accept one submission containing one or more positive allocations to active same-tenant Cashbox/Bank Treasury IDs; require unique destinations and require sum of allocations to equal document total.
4. Fix the offset server-side to the tenant's active seeded `3010`; never expose an offset-account picker.
5. Permit the first post only when no effective initial capitalization and no prior posted Cashbox/Bank Treasury movement exist. Permit replacement only after full reversal and with explicit predecessor lineage.
6. Extend central inflow validation to reload the posted/being-posted same-tenant document, verify destination allocations, and cap cumulative net Treasury increase to the document total.
7. Post one balanced journal with `SourceType=TreasuryInitialCapitalization`, source ID=document ID, caller-stable idempotency, Owner user ID, allocation debits, and one `3010` credit.
8. Use the existing posting/reversal authority and transaction. Never write account balances, ledger lines, or legacy opening metadata directly.
9. Replace the misleading Cashbox opening navigation with an Owner-only capitalization workspace; keep Bank opening redirected and retire stale `BankOpeningBalance.jsx` when implementation begins.
10. Add endpoint/PostgreSQL integration, architecture, reporting, browser, role, replay, concurrency, reversal, and cross-tenant tests before enabling the route.

This is intentionally narrower than a generic capital-management module. Later owner contributions, owner loans, distributions, evidence uploads, dual approval, and multiple currencies require separate decisions.

## 23. T-06C Readiness Impact

AhramStore's read-only snapshot on 2026-09-13 found:

| Authority | Cached balance | Ledger balance | State |
| --- | ---: | ---: | --- |
| Main Cashbox / `1010` | `0.0000` | `0` | Active Cashbox Treasury; `PreventNegative=true` |
| Primary Bank / `1030` | `0.0000` | `0` | Active Bank Treasury; `PreventNegative=true` |
| Cash Float Reserve / `1040` | `0.0000` | `0` | Not Treasury; `PreventNegative=true` |
| Opening Balance Equity / `3010` | `0.0000` | `0` | Active seeded Equity; not Treasury |

The snapshot also found no AhramStore legacy Bank/Cashbox master rows. All queries ran in `REPEATABLE READ`, `READ ONLY`, and were rolled back. No business or financial data changed.

T-06C remains **NOT READY** while T-06D is only a design. After an approved implementation, deployment, and an explicitly authorized Owner action, readiness requires evidence that capitalization created exactly one source document and balanced journal, affected only the selected Treasury asset and `3010`, preserved report reconciliation and cross-tenant state, and left enough source cash for the planned `100.00 EGP` supplier-card funding. Only then may the full T-06C Golden Scenario restart from a new documented baseline.

## 24. Risks / Open Decisions

| Decision or risk | Recommendation |
| --- | --- |
| Is AhramStore cash truly pre-cutover? | Obtain Owner/accounting confirmation before any posting; otherwise use a future ordinary contribution workflow |
| Canonical cutover date | Add or explicitly capture it on the document; reject future dates and post-Treasury-activity opening claims |
| One document vs repeated openings | One active initial document per tenant with multiple allocations; reversal and replacement only |
| Post-cutover owner funds | Seed/use a dedicated permanent Equity account in a separate workflow, not `3010` |
| Owner loans | Add a named Liability account and repayment authority; never use `2030` |
| Admin authority | Owner-only v1; add persisted maker-checker approval rather than implicit Admin authority if needed |
| Multiple currencies | Out of scope until ledger/Treasury account currency is authoritative; do not accept a free-form currency |
| Evidence strength | Require reference/declaration in v1; decide attachment retention and privacy separately |
| Cash-flow correctness | Add source-aware tests/classification because current aggregate Equity logic can misclassify non-cash openings |
| Existing tenants with Treasury history | Fail closed; do not offer initial capitalization as a repair or adjustment tool |
| Reversal after funds are spent | Preserve `PreventNegative`; require funds to be restored legitimately before reversal |
| Legacy UI/data | Do not synchronize financial truth back into Bank/Cashbox opening metadata |

The most important unresolved business fact is whether the intended AhramStore amount represents cash that existed at cutover or a new owner contribution occurring now. The journal can be balanced in either case, but the correct Equity account, date, cash-flow classification, and legal assertion differ.

## 25. Final Recommendation

Approve design work for a bounded **Treasury Initial Capitalization** authority, but do not enable or execute it until the AhramStore Owner/accounting classification and cutover date are confirmed.

Implementation acceptance gates are:

- Owner-only, tenant-derived authorization with no platform or Admin shortcut.
- One immutable source document with caller-stable idempotency and one or more active same-tenant Cashbox/Bank allocations.
- Server-fixed `3010` offset for pre-cutover funds only.
- Central SQL source reload and cumulative amount enforcement in `FinancialPostingService`.
- Atomic balanced posting, ledger-only balances, unchanged `PreventNegative`, and reversal-only correction.
- Stable replay/conflict/concurrency/error contracts and complete audit lineage.
- Passing Trial Balance, Balance Sheet, P&L, Cash Flow, statement, role, reversal, and cross-tenant tests.
- Continued retirement of direct cash-in and legacy non-zero Cashbox/Bank opening writes.
- Explicit exclusion of `1040`, `2030`, `1020`, `1110`, supplier-card assets, arbitrary GL accounts, direct SQL, and balance edits.

Until those gates pass, retain `OPENING_BALANCE_AUTHORITY_REQUIRED` and `TREASURY_INFLOW_WORKFLOW_REQUIRED`, keep AhramStore unchanged, and keep T-06C at **FAIL / NOT READY**. The current block is a safety control; the remediation is a new source-document authority, not removal of that control.

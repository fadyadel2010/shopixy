# Treasury T-06D Initial Capitalization Implementation - 2026-09-13

## 1. Executive Verdict

**FAIL / NOT READY FOR AHRAMSTORE MUTATION.** The bounded Treasury Initial Capitalization authority was implemented and its focused verification passed, but the required repository-wide test gate remained red. In accordance with the approved fail-closed design, the migration was not deployed and no AhramStore capitalization was posted.

## 2. Scope Delivered

The implementation adds a tenant-owned source document, immutable allocation snapshots, Owner-only API, central posting validation, atomic ledger/outbox handling, full reversal and replacement lineage, source-aware reporting behavior, and an Owner-only bilingual frontend workspace. It does not add ordinary owner contributions, owner loans, partial reversal, generic Treasury cash-in, direct balance editing, or supplier-card funding.

## 3. Safety Decision

The approved audit requires AhramStore to remain unchanged until all acceptance gates pass. The focused implementation gates passed, but both broad test suites reported failures outside this bounded change and the complete .NET run timed out. Those results were not waived, so deployment and the intended EGP 1,000 rendered-UI action were stopped.

## 4. Target Tenant

The intended business target remains AhramStore, tenant ID `c9c5c1ae-949e-498e-bc63-07242daeb49d`, slug `ahramstore`, currency EGP. No request in this implementation accepts a tenant ID from the browser; all server operations use the trusted tenant context.

## 5. Source Document

`TreasuryInitialCapitalization` is the canonical SQL source authority. It stores the generated document number, stable operation ID, cutover date, total, request hash, declaration identifier, evidence reference, Owner identity, timestamps, fixed opening-equity snapshot, status/version, journal lineage, and optional predecessor/reversal evidence.

## 6. Allocation Authority

`TreasuryInitialCapitalizationAllocation` freezes each selected Treasury account ID, linked ledger account ID, Treasury type/name snapshot, ledger code/name snapshot, and positive amount. Destinations must be unique within a document and are resolved from current active same-tenant SQL truth.

## 7. Lifecycle

The lifecycle is `Draft` to `Posted` to `Reversed`. A Posted document cannot be edited or deleted. A correction requires a complete reversing journal, after which one replacement can reference the latest reversed predecessor while preserving the original cutover date and history.

## 8. Database Constraints

The model enforces tenant-qualified alternate keys and foreign keys, unique tenant operation/document identities, one non-reversed capitalization per tenant, unique predecessor use, unique destination allocation, positive total/allocation checks, a `3010` opening-equity snapshot check, and PostgreSQL `xmin` concurrency.

## 9. Migration State

Migration `20260913182724_T06DInitialCapitalizationAuthority` creates only `TreasuryInitialCapitalizations`, `TreasuryInitialCapitalizationAllocations`, and their constraints/indexes. `dotnet ef migrations list` reported it as the sole pending migration. It was deliberately not applied because the broad verification gate failed.

## 10. Owner Authorization

The controller is protected by `[Authorize(Roles = "Owner")]`, while the service independently reloads an active, non-deleted, same-tenant SQL user whose role is `Owner`. Missing or malformed Owner identity returns stable HTTP 403 `AUTHORIZATION_REQUIRED`. Admin, Manager, Cashier, Warehouse, SuperAdmin, anonymous, background, and storefront identities cannot post or reverse.

## 11. HTTP Contract

The dedicated authority is exposed under `/api/treasury/initial-capitalizations` with endpoints for eligible destinations, document list/detail, create/post, and full reversal. Financial writes use the `FinancialWrite` rate limit. Responses expose immutable source, allocation, equity, journal, status/version, and trace identities.

## 12. Input Validation

Create requires a non-empty operation UUID, non-future non-local cutover date, one or more unique positive allocations, evidence reference, current declaration identifier `TREASURY_CUTOVER_FUNDS_V1`, and explicit acknowledgement. Reversal requires a stable operation UUID, mandatory reason, and explicit full-reversal confirmation.

## 13. Eligibility

The first capitalization is allowed only when the tenant has no active capitalization and no unrelated posted/reversed movement in Cashbox or Bank Treasury accounts. A replacement is allowed only after full reversal, against the latest reversed predecessor, with the same cutover date.

## 14. Idempotency

The browser creates and retains one UUID for retries of the same logical payload. The server hashes all authoritative request fields and enforces unique `(TenantId, OperationId)`. Exact replay returns the existing result; changed payload with the same operation ID returns `IDEMPOTENCY_CONFLICT` without mutation.

## 15. Concurrency

Creation and reversal execute in serializable transactions with a tenant-row lock. Eligibility is rechecked under that lock, database uniqueness protects one active document, and nested EF/PostgreSQL concurrency failures are translated to stable `CONCURRENCY_CONFLICT` responses.

## 16. Posting Authority

`FinancialPostingService` explicitly recognizes source type `TreasuryInitialCapitalization` and reloads the current same-tenant document, allocations, Owner, destination mappings, and opening-equity account. It verifies source reference, cutover date, posting key, exact debit lines, exact `3010` credit, total, lifecycle, predecessor, and absence of unrelated Treasury movement.

## 17. Accounting Entry

One journal debits each selected active Cashbox/Bank Treasury asset and credits the server-resolved active same-tenant Equity account `3010 Opening Balance Equity` for the exact total. The intended AhramStore action would be Dr `1010 Main Cashbox` EGP 1,000 and Cr `3010` EGP 1,000, but that action was not executed.

## 18. Forbidden Accounts

The implementation rejects `1020`, `1110`, `1040`, `2030`, `3010`, clearing accounts, non-Asset destinations, inactive/deleted accounts, foreign-tenant identities, supplier-card assets, and arbitrary caller-selected GL accounts as capitalization destinations. The offset is never browser-selectable.

## 19. Atomic Outbox

The source document, allocations, journal, account materialization, and processed accounting outbox commit in one transaction. Posting and reversal outbox identities derive from the stable operation lineage. No successful idempotency artifact is committed separately from its financial journal.

## 20. Immutability

Entity methods restrict state transitions, and `ApplicationDbContext` rejects allocation updates/deletes, document deletes, and changes outside the bounded lifecycle fields. Existing ledger guards continue to reject direct ledger-line creation and direct `CurrentBalance` modification outside `FinancialPostingService` mutation scope.

## 21. Reversal

Reversal uses `FinancialPostingService.ReverseJournalAsync`, retains the original source and journal, writes a linked reversal journal, and records reason, operation hash, and timestamps. It reverses the full document only. Existing `PreventNegative` behavior can block reversal if later spending leaves insufficient destination funds.

## 22. Tenant Isolation

Service, posting, source, allocation, Treasury, account, journal, user, replay, eligibility, and reversal queries are tenant-qualified. Composite foreign keys prevent an allocation, predecessor, Treasury mapping, or ledger account from crossing tenant ownership even when query filters are bypassed.

## 23. Reporting

Trial Balance, Balance Sheet, Profit and Loss, General Ledger, and Treasury statements continue to derive from ledger entries rather than the capitalization document. Focused reporting tests cover capitalization alongside customer/supplier opening balances and their reversals.

## 24. Cash Flow

Cash-flow classification now excludes both receivable/payable and Equity sides of non-cash customer/supplier opening journals, including reversal lineage. Capitalization remains visible as opening cash when before the period and as financing movement when within the period; it is not removed from reconciliation.

## 25. Frontend Workspace

`TreasuryInitialCapitalization.jsx` presents backend-returned eligible destinations, evidence and cutover fields, one or more allocations, a Dr/Cr preview, two explicit attestations, immutable posting receipt/history, and full-reversal controls. It performs no local balance derivation and does not automatically retry financial writes.

## 26. Route And Navigation

The canonical route `/treasury/initial-capitalization` is wrapped by `PosRoleRouteGuard` with exactly `['Owner']`. Its sidebar item has `requiredRoles: ['Owner']`. Legacy `/opening-balance/cashbox` and `/opening-balance/banks` routes redirect to the canonical workspace; the misleading Cashbox opening component is no longer imported by `App.jsx`.

## 27. Localization And Responsive Behavior

English and Arabic locale trees contain aligned capitalization copy for authority, evidence, allocations, preview, statuses, errors, history, and reversal. The workspace consumes `useRTL`, localized dates/currency, responsive grids/cards, mobile-safe dialogs, accessible labels, and loading/error states. Both locale files parse as valid JSON.

## 28. Focused Verification

The following bounded gates passed:

| Gate | Result |
| --- | --- |
| Solution build | PASS, 0 warnings and 0 errors |
| Capitalization endpoint, reporting, and backend architecture tests | PASS, 36/36 |
| Capitalization API/page/frontend architecture tests | PASS, 8/8 |
| Frontend architecture suite | PASS, 40/40 |
| Frontend production build | PASS |
| Locale JSON parsing | PASS |
| `git diff --check` | PASS, line-ending warnings only |

Standalone ESLint could not run because this repository uses ESLint 9 without an `eslint.config.js`, `eslint.config.mjs`, or `eslint.config.cjs` flat configuration.

## 29. Broad Gate, Runtime, And Mutation Evidence

The full frontend suite reported 1,278 passed and 12 failed tests. Failures were in pre-existing i18n gaps, storefront payment/cart/state behavior, warehouse source certification, and footer authoring. The complete .NET suite timed out after 30 minutes amid numerous unrelated architecture, missing-artifact, SQLite/PostgreSQL-compatibility, and shared integration-database failures; capitalization tests returned HTTP 500 under that global interference but passed 32/32 both before and after in isolation.

The frontend dev server returned HTTP 200 for `/treasury/initial-capitalization`. The API was unavailable during the final runtime probe. The migration remained pending, no authenticated browser capitalization was attempted, no source document/journal/outbox was created for AhramStore, no baseline-to-result reconciliation was required, and no T-06C or supplier-card funding action was started.

## 30. Final Disposition

The implementation is code-complete for the bounded T-06D design and its focused authority tests are green, but release certification is **FAIL** because the required broad verification and runtime/deployment gates did not pass. Keep `OPENING_BALANCE_AUTHORITY_REQUIRED`, `TREASURY_INFLOW_WORKFLOW_REQUIRED`, legacy Bank HTTP 410 behavior, the pending migration, and AhramStore's financial data unchanged. The safe next action is to restore a green repository-wide baseline, rerun all gates, deploy the single pending migration, capture a new read-only AhramStore baseline, and only then perform the one EGP 1,000 rendered-UI capitalization and immediate ledger/report reconciliation.

# POS Existing-Sale Editing Audit

Date: 2026-09-10

## 1. Executive Verdict

PASS for the inspected existing-sale rewrite boundary. Completed sales remain immutable, an admitted draft rewrite now completes atomically, and the resulting inventory, accounting, treasury, outbox, idempotency, and browser-visible lifecycle state converge without duplicate effects.

Repository-wide certification remains subject to the tooling debt in section 26. This verdict covers the inspected POS sales workflow, not unrelated dirty-worktree domains.

## 2. Scope

The audit covered `PUT /api/Sales/{id}`, its invoice lifecycle checks, optimistic concurrency, inventory reversal/reapplication, financial reversal/reposting, accounting and stock outboxes, replay behavior, frontend invoice selection, edit-state hydration, payment resubmission, and desktop/mobile presentation.

## 3. Certified Lifecycle Policy

- Only `Draft` sales invoices may enter the rewrite path.
- A successfully paid rewritten draft transitions to `Completed` before accounting is reposted.
- Completed invoices are immutable and direct the operator to Sales Returns.
- Deferred POS credit mutation remains unsupported and fails closed.

## 4. Runtime Topology

- Live application API: `http://localhost:5010`.
- Isolated diagnostic API used during repair: `http://localhost:5011`.
- Vite POS application: `http://localhost:5173`.
- PostgreSQL configuration source: `PointOfSale.API/appsettings.Development.local.json`.

## 5. Controlled Fixture

- Tenant: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.
- Warehouse: `4a50f767-ce97-478f-af24-66e8e5e14c7f`.
- Variant: `b217add3-06a2-4eb5-950a-2b14a37c3d58`.
- Sale: `bd21beee-452e-4964-921c-7452de619939`.
- Invoice: `INV-20260910-0023`.

The invoice was placed in `Draft` through a temporary local database probe solely to exercise the otherwise protected rewrite path. It was restored to normal lifecycle authority by the successful API rewrite.

## 6. Baseline State

Before the successful rewrite, the controlled invoice was `Draft`, mutation version `0`, quantity `1`, total/paid/cash `11.11`, net stock movement `-1`, with two posted journals, no reversed journals, and one processed accounting outbox row.

## 7. Completed-Invoice Guard

A valid update request against the completed invoice returned `409 SALE_INVOICE_IMMUTABLE` with Sales Returns guidance. No inventory, invoice, ledger, or outbox mutation occurred.

## 8. Draft Rewrite Admission

The final repaired request returned `200` with invoice `INV-20260910-0023`, total `22.22`, paid `22.22`, change `0.00`, and the expected request, correlation, and payment-attempt headers.

## 9. Optimistic Concurrency

The controller compares `ExpectedMutationVersion` with `AccountingMutationVersion` while holding the invoice mutation lock. A controlled stale request using expected version `0` against current version `1` returned `409 SALE_MUTATION_CONFLICT` and exposed `currentMutationVersion: 1` in normalized error data.

## 10. PostgreSQL Physical Table Mapping

Raw financial-integrity locks incorrectly addressed `JournalEntries`, while EF maps the entity to `LedgerJournalEntries`. Sale and purchase financial-integrity services now lock the physical `LedgerJournalEntries` table on PostgreSQL and SQL Server paths.

## 11. PostgreSQL Concurrency Projection

Raw `AccountingOutboxMessages` lock queries now select `xmin` with the mapped columns. This materializes EF's PostgreSQL concurrency token and prevents the rewrite from failing when the outbox row is later updated.

## 12. Inventory Rewrite

The workflow first restores the original item quantity under stock-row locks, removes the old invoice items, validates and applies the new authoritative item set, and appends new movement history. Final movement history contains three rows with net quantity delta `-2`, matching the rewritten invoice quantity.

## 13. Stock Outbox Coalescing

Reversal and reapplication can touch the same `(VariantId, WarehouseId)` in one transaction. Pending changes are now coalesced to the first old quantity and final new quantity before event-version allocation, preventing duplicate `(tenant, variant, warehouse, eventVersion)` rows. The controlled create and rewrite produced published event versions `5` and `6`; none are pending or failed.

## 14. Inventory Truth

The controlled variant's final warehouse stock is `17`, with reserved and damaged quantities both `0`. This matches the fixture's prior stock and the invoice's net quantity `2` consumption.

## 15. Server-Authoritative Pricing

The controller continues to recompose unit price, tax rate, discounts, conversion factor, and totals from admitted SQL product/variant/unit authority. Client totals do not become accounting authority. The final persisted item quantity/base quantity is `2`, and its line total is `22.22`.

## 16. Payment Authority

The rewrite accepts only supported immediate tenders and normalizes paid, cash, and card amounts against the server-calculated total. The controlled rewrite persisted total, paid, and cash as `22.22`, card as `0`, and status as `Completed`.

## 17. Accounting Reversal And Repost

The original posted sale and COGS journals were reversed, then replacement sale and COGS journals were posted after the invoice entered `Completed`. Final state contains four journals: two posted and two reversed.

## 18. Ledger Balance

Final posted journals contain debits `28.2200` and credits `28.2200`. Reversed journals contain debits `14.1100` and credits `14.1100`. Both sets balance exactly.

## 19. Treasury Authority

Treasury replacement-limit calculation now counts only entries whose parent journal remains `Posted`. Reversed historical inflows no longer consume the current authoritative sale amount, while replay and inflation attempts remain bounded by the invoice total.

## 20. Accounting Outbox

The base sale and rewrite reversal rows are both processed, neither is failed, and synchronous posting recorded the matching journal before transaction commit. The background worker remains recovery authority rather than a second normal-path financial writer.

## 21. Exact Replay

Reusing the original `X-Payment-Attempt-Id` with the original payload returned the stored `200` response. The invoice stayed at mutation version `1`; movement, journal, accounting-outbox, and stock-outbox counts did not increase.

## 22. Divergent Replay

Reusing the original payment-attempt key with quantity and payment changes returned `400 INVALID_REQUEST` with `Idempotency key reuse with different payload`. A new key with a byte-equivalent update payload recovered the completed result by the intentionally tested cross-key request-hash recovery path.

## 23. Transaction Rollback

Each pre-repair `500` response rolled back the entire rewrite. Intermediate invoice totals, stock movement, journal, and outbox changes never became durable. The successful request committed those boundaries together.

## 24. Frontend Editing Contract

- Non-draft sales cannot be selected for editing.
- A second full-sale load rechecks lifecycle status before hydrating the cart.
- Draft mutation version, tender, cash amount, representative, item identity, variant, and unit state are retained.
- POS-cart identity remains server-authoritative for new sales.
- Async representative/customer/entity lookups preserve Strict Mode lifecycle and expose partial-load, retry, and end-of-results states.

## 25. Browser And Automated Evidence

- Backend focused proof: `4/4` passed.
- Broader relevant backend classes: `43/43` passed.
- Frontend affected tests: `22/22` passed.
- Frontend production build: passed; only the existing large-chunk warning remains.
- Chromium desktop `1440x1000`: completed invoice Edit disabled, exact Sales Returns guidance, no horizontal overflow.
- Chromium mobile `390x844`: completed invoice remains protected and document-level horizontal overflow is eliminated; the wide invoice table scrolls inside the modal.
- Final browser run: zero runtime exceptions, failed requests, or HTTP error responses.
- Screenshots: `C:/Users/fadya/AppData/Local/Temp/opencode/pos-sales-browser-desktop.png` and `C:/Users/fadya/AppData/Local/Temp/opencode/pos-sales-browser-mobile.png`.

## 26. Quality Gate And Residual Debt

Scoped quality gate: PASS.

- Final invoice: `Completed`, mutation version `1`, total/paid/cash `22.22`.
- Final inventory: net movement `-2`, warehouse stock `17`.
- Final financials: balanced posted and reversed journals.
- Final outboxes: accounting and stock rows processed, no pending or failed rows.
- `git diff --check`: clean.
- ESLint is not runnable in the current checkout because ESLint 9 defaults to flat config while the repository uses `.eslintrc.cjs`, and legacy mode then fails because `@typescript-eslint/eslint-plugin` is not installed. No dependency/config migration was introduced as part of this sales repair.
- A previously observed broader architecture run still has unrelated failures tied to archived migration-file expectations and stale source-shape assertions. Those files were not changed or reverted.

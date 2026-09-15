# Treasury T-06 Post-Migration Certification - 2026-09-13

## 1. Executive Verdict

PASS

Migration `20260912154614_T06SupplierCardFundingAuthority` is deployed exactly once on the controlled Development target. The applied schema matches the reviewed EF migration, focused tests and builds pass, the restarted API serves healthy authenticated read contracts, and the protected financial/business snapshot is byte-for-byte unchanged.

## 2. Target Environment

- Environment: `Development`
- Database: `PosEcommerceDb_Baseline`
- PostgreSQL: `18.4 (Ubuntu 18.4-1.pgdg22.04+1)`
- Schema: `public`
- Tenant: `shopixy`
- Tenant ID: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`
- Configuration source: `PointOfSale.API/appsettings.Development.local.json`
- Credentials are intentionally omitted.

## 3. Pre-Migration Baseline

The pre-migration audit was captured at `2026-09-13T13:14:10Z` under `REPEATABLE READ`, `SET TRANSACTION READ ONLY`, and rollback. The latest applied migration was `20260912120000_T01ShopixyLedgerBalanceCacheRepair`; T-06 was the only pending migration, its tables and columns were absent, and EF reported no model drift.

The comparable protected-business SHA-256 was `3250BC2CE9A9CC47F7ED48C23EB2E2C3806462852699713E4FD25091B8C4793E`. Baseline financial totals were 169 journals, 353 ledger lines, EGP 208,654.72 debits, EGP 208,654.72 credits, and EGP 0.00 difference. Accounting outbox state was 121 processed, 0 pending, and 0 failed.

## 4. Migration Applied

EF Core applied only `20260912154614_T06SupplierCardFundingAuthority` through `dotnet ef database update`. The result was `Applying migration '20260912154614_T06SupplierCardFundingAuthority'. Done.` No manual SQL deployment, migration-history edit, bypass, rollback, or unrelated migration was used.

Post-apply migration history contains exactly one T-06 row and 32 rows total. A second `dotnet ef database update` acquired the normal EF migration lock and returned `No migrations were applied. The database is already up to date.` EF migration listing shows no unexpected pending migration, and the post-apply model check reports no changes since the latest migration.

## 5. Post-Migration Schema

PostgreSQL catalog inspection confirms the reviewed shape: seven additive columns, two new tables, two tenant-scoped alternate keys, three named check constraints, ten foreign keys, and thirteen explicit EF indexes. All expected authority foreign keys use `ON DELETE RESTRICT`.

The additive columns are `LedgerAccounts.IsSupplierCardFunding boolean NOT NULL DEFAULT false`; nullable UUID/integer authority snapshots on `PurchaseInvoicePayments`; and nullable UUID/integer authority snapshots on `SupplierPayments`. The new `SupplierCardFundingInstruments` and `SupplierCardFundings` tables use UUID identities and tenant ownership, positive authority/version/amount checks, composite same-tenant references, dedicated ledger-account uniqueness, and tenant/idempotency uniqueness. The catalog matches the migration with no missing or unexpected T-06 object.

## 6. Business Data Non-Mutation

The migration created schema only. It did not create a funded-card instrument, funding, purchase, supplier payment, journal, ledger line, account, Treasury account, customer, supplier, or inventory transaction. After migration and API restart, `SupplierCardFundingInstruments` has 0 rows, `SupplierCardFundings` has 0 rows, and 0 ledger accounts are designated as supplier-card funding accounts.

No historical transaction was reclassified or backfilled. The only intentional non-schema write used for smoke testing was the built-in Development-only test-auth issuer establishing short-lived SQL-backed reliability-session authority; it did not write a business or financial transaction.

## 7. Protected Data Fingerprint

The protected snapshot covers all ledger accounts excluding only the new T-06 flag, Treasury accounts, journals, ledger lines, accounting outbox messages, purchase invoices, purchase payments excluding only the new nullable snapshots, supplier payments excluding only the new nullable snapshots, customers, suppliers, and inventory stock rows.

- Before migration: `3250BC2CE9A9CC47F7ED48C23EB2E2C3806462852699713E4FD25091B8C4793E`
- Immediately after migration: `3250BC2CE9A9CC47F7ED48C23EB2E2C3806462852699713E4FD25091B8C4793E`
- After restart, authenticated API smoke, tests, and builds: `3250BC2CE9A9CC47F7ED48C23EB2E2C3806462852699713E4FD25091B8C4793E`

The final read-only capture was `2026-09-13T13:34:11.9954338Z` and rolled back.

## 8. 1020 Safety

Shopixy `1020 POS Visa Account` remains active, Asset-classified, not Clearing, not Treasury-mapped, and not supplier-card-designated. Its cached and ledger-derived balances both remain EGP `-3.80` across 8 retained ledger lines. No classification, mapping, balance, journal, ledger line, or historical reference changed.

## 9. 1110 Safety

Shopixy `1110 Visa Clearing` remains active, Asset-classified, Clearing-designated, mapped once to an active Clearing Treasury account, and not supplier-card-designated. Its cached and ledger-derived balances both remain EGP `55.00` across 3 retained ledger lines. No supplier Card payment links to `1110`, and no supplier-card authority was inferred from customer Card clearing.

## 10. Outbox Safety

Accounting outbox state remains 121 total, 121 processed, 0 pending, and 0 failed. The six retained supplier Card-related messages remain four `PosPurchase` and two `PurchaseReversal` messages with exact existing journal references and intentionally absent T-06 authority snapshots. No message was re-enqueued, redispatched, reclassified, or used to create a journal.

## 11. Historical Compatibility

All four retained purchase Card payment rows keep null `FundingInstrumentId`, `FundingLedgerAccountId`, and `FundingInstrumentVersion` snapshots. There are no historical supplier Card rows to backfill. Their existing purchase journals continue to reference historical `1020` evidence and not customer `1110` authority.

Authenticated API detail smoke for retained purchase `94b43c19-4fba-42a5-9489-64a2e925c483` returned HTTP 200, one Card payment, and null instrument/version snapshots. Supplier payment history also returned HTTP 200. The focused legacy replay tests confirm exact-journal replay remains no-op compatible without authority invention.

## 12. API Contract Smoke

Using a short-lived Admin token issued for the real active shopixy tenant, read-only calls returned:

- `GET /api/supplier-card-funding-instruments`: HTTP 200, 0 instruments.
- `GET /api/supplier-card-funding-instruments/funding-sources`: HTTP 200, 2 eligible Cashbox/Bank sources.
- `GET /api/Treasury/summary`: HTTP 200.
- `GET /api/Purchases?page=1&pageSize=100`: HTTP 200, 6 shopixy purchases.
- `GET /api/Suppliers?page=1&pageSize=100`: HTTP 200, 1 shopixy supplier.
- Retained purchase detail and supplier payment-history reads: HTTP 200.

No POST, PUT, funding, purchase, or supplier-payment business endpoint was invoked.

## 13. Authorization

An unauthenticated instrument request was rejected before controller execution with HTTP 400 `TENANT_REQUIRED`, demonstrating fail-closed tenant resolution rather than anonymous data access. A SQL-session-bound Admin token for shopixy was accepted.

Controller metadata restricts instrument reads, funding sources, funding, and statements to `Owner,Admin,Manager`; create and update are further restricted to `Owner,Admin`. `Cashier` and `Warehouse` are absent. No valid lower-role shopixy session was available through the execution channel, so a runtime 403 probe was not fabricated; the role boundary was verified from compiled source metadata and the passing focused authority suite.

## 14. Tenant Isolation

A short-lived token bound to a different real active tenant received HTTP 404 when reading the retained shopixy purchase ID and saw 0 supplier-card instruments. The database schema additionally enforces same-tenant instrument, funding, ledger-account, Treasury-account, purchase-payment, and supplier-payment relationships through composite alternate keys and foreign keys. The disposable migration proof rejected a cross-tenant funding relationship at PostgreSQL.

## 15. Customer Regression

The dedicated customer/supplier regression selection passed 16 of 16 tests, including customer payment persistence, idempotency, ledger authority, enum serialization, opening-balance authority, and statement-of-account behavior. The broader 119-test financial selection also passed customer mixed-payment posting and reporting coverage.

Shopixy remains at 1 active customer with aggregate stored balance EGP `-110.00`. Customer `1110` authority and balances are unchanged by the protected fingerprint.

## 16. Supplier Non-Card Regression

The same 16-test regression selection passed supplier opening-balance, idempotency, party-ledger, and direct-balance-write guards. Existing global supplier-payment rows remain 3 and are unchanged; none was converted into Card authority. Shopixy remains at 1 active supplier with aggregate stored balance EGP `-4.01`.

Cash and Bank Treasury sources remain unchanged for shopixy: `1010` is EGP `73.92`, and `1030` is EGP `10,410.10`, with cached balances equal to ledger-derived balances.

## 17. T-06 Focused Tests

The fresh Release T-06/financial selection passed 119 of 119 tests with 0 failed and 0 skipped in 1 minute 51 seconds. It covered supplier-card funding authority, purchase payment integrity, accounting outbox, commerce accounting, financial reporting, Treasury transfer, settlement authority, migration replay, `PI_CON_01`, and `PR_CON_02`. The current vendor-debt certification selection separately passed 3 of 3.

## 18. Post-Restart Verification

After the API restart, `GET /health/live` returned HTTP 200 with `Healthy`. Authenticated shopixy and cross-tenant read-only contract smoke then completed successfully, and a later health probe again returned HTTP 200.

The POS Vite server at port 5173, storefront at 5174, and CommerceHub at 5175 each returned HTTP 200. The POS deep route `/supplier-card-funding-instruments` returned HTTP 200. No browser-control channel or reusable authenticated browser session was available, so this report does not claim interactive DOM verification. Runtime evidence is limited to HTTP reachability, authenticated API contracts, focused tests, and the successful production build.

## 19. Final Reconciliation

Final global totals remain 169 journals, 353 ledger lines, EGP `208,654.72` debits, EGP `208,654.72` credits, and EGP `0.00` imbalance. Protected counts remain 192 ledger accounts, 36 Treasury accounts, 10 purchase invoices, 9 purchase-payment rows, 3 supplier-payment rows, 33 customers, 9 suppliers, and 52 stock rows.

Shopixy inventory remains 23 active products, 32 active variants, 26 stock rows, quantity `1,641.66`, reserved quantity `1.00`, and damaged quantity `0.00`. API Release build passed with 0 warnings and 0 errors. Vite production build passed with only the existing large-chunk warning. `git diff --check` reported only line-ending notices and no whitespace errors. No funded-card instrument, funding, purchase, supplier payment, or financial journal was created; no historical reclassification was performed; and no commit was created.

## 20. Next Gate

The next gate is `T-06 REAL BUSINESS DATA CERTIFICATION`. It is documented only and was not executed. Creating an instrument, funding it, recording a supplier Card purchase/payment, or posting any related journal requires separate explicit business approval and a new protected baseline.

# Treasury T-06C Remediation 2: Outbox and Cash Flow

Certification date: 2026-09-14

Target tenant: `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`)

Execution mode: source remediation, disposable PostgreSQL regression tests, headed Microsoft Edge, live API, and read-only PostgreSQL reconciliation

Final verdict: **FAIL**

## 1. Executive Verdict

**OVERALL VERDICT: FAIL.** Both source defects identified by the stopped T-06C rerun are remediated for supported behavior. New supplier-card fundings now persist a processed accounting outbox row in the same transaction as the funding document, balanced journal, and ledger lines. The live AhramStore Cash Flow report now derives the existing supplier-card funding cash movement from immutable ledger lines and returns closing cash `900.00`, investing cash flow `-100.00`, financing cash flow `1,000.00`, net cash change `900.00`, and `reconciles=true`.

The source and runtime Cash Flow remediation is **PASS**. The overall certification remains **FAIL** because historical funding `534127ff-18b3-41ce-ac0a-0de146807f8e` was committed before the outbox fix and still has no accounting outbox lineage. No supported reconciliation workflow was found, and this remediation did not fabricate, backfill, replay, or otherwise mutate that historical business record.

## 2. Scope and Safety Constraints

This remediation was limited to atomic funding outbox creation, ledger-derived Cash Flow classification, focused regression coverage, build/static validation, guarded browser reads, and read-only database reconciliation. No second funding, replay, purchase, supplier payment, edit/repost, overspend, reversal, direct SQL business write, cached-balance patch, journal change, ledger-line change, historical outbox insertion, or cross-tenant mutation was performed.

The browser guard admitted `GET`, `HEAD`, and `OPTIONS` requests and the normal authenticated-session refresh only. The certification run recorded one HTTP `POST` to `/api/auth/refresh`; it recorded no business mutation. Database captures used `REPEATABLE READ / READ ONLY / ROLLBACK`.

## 3. Stopped-Run Baseline

The preceding Golden rerun stopped after one real rendered funding action exposed two mandatory discrepancies:

| Item | Stopped state |
| --- | --- |
| Funding | `534127ff-18b3-41ce-ac0a-0de146807f8e`, EGP `100.00` |
| Reference | `T06C-FUND-001-RERUN` |
| Journal | `01a09ce1-3c2a-7153-beb2-b58385b6a22d` / `JE-2026-000002` |
| Posting | Dr `T06C-CARD-ASSET` `100.00`; Cr `1010` `100.00` |
| Balances | Card `100.00`; `1010` Main Cashbox `900.00` |
| Outbox defect | No row linked to the funding or journal |
| Cash Flow defect | Closing `900.00`; net change `1,000.00`; `reconciles=false` |
| AhramStore fingerprint | `394AD2C1711D8A3A882FC1E86DE642CD684C3424B67B239CDFCABDAF8E5CCA0D` |

The prior run stopped before all later Golden scenarios. This remediation preserves that stopped business-data state.

## 4. Funding Outbox Root Cause

`SupplierCardFundingInstrumentsController.Fund` persisted the funding document and called the financial posting service inside a database transaction, but it did not create an `AccountingOutboxMessage`. The journal therefore committed with valid `SupplierCardFunding` source identity while the required accounting event lineage remained absent. The transaction was financially atomic but incomplete with respect to the outbox invariant.

## 5. Atomic Outbox Remediation

`PointOfSale.API/Controllers/SupplierCardFundingInstrumentsController.cs:208-278` now creates the funding document and its accounting outbox message before posting the journal. Both are saved inside the existing funding transaction. After `PostJournalAsync` succeeds, the controller calls `outbox.MarkProcessed(journal.Id)`, saves the processed state, and only then commits.

`CreateFundingOutbox` at `SupplierCardFundingInstrumentsController.cs:436-460` records:

| Field | Value |
| --- | --- |
| Event type | `SupplierCardFunding` |
| Source type | `SupplierCardFunding` |
| Business entity | Funding ID |
| Entity type | `SupplierCardFunding` |
| Idempotency key | `OUTBOX:SUPPLIER_CARD_FUNDING:{tenantId}:{fundingId}` |
| Payload | Frozen funding, instrument, source Treasury, source ledger, card ledger, version, date, amount, reference, and notes |
| Processed linkage | Posted journal ID and processed timestamp |

The change does not introduce a second transaction, asynchronous gap, or separate repair path.

## 6. Atomicity and Idempotency Evidence

Disposable PostgreSQL endpoint tests prove the complete transaction boundary. Zero-balance and one-cent-short requests preserve identical before/after counts and balances, including zero outbox delta. Exact-balance success creates exactly one funding, one balanced journal, two ledger lines, and one processed outbox row linked to the returned funding and journal IDs.

Failed same-key replay leaves no persisted funding or outbox artifact. A committed key with changed payload returns conflict without a second mutation. Concurrent `80.00 + 80.00` requests against `100.00` produce one committed funding/outbox and one safe insufficient-funds result, leaving source balance `20.00`. Foreign-tenant authority is rejected without changing the owner tenant.

## 7. Cash Flow Root Cause

The Cash Flow report included the EGP `1,000.00` opening-equity financing inflow but did not classify the EGP `100.00` transfer from cash into the dedicated supplier-card asset. Closing cash was correctly ledger-derived as `900.00`, while reported net cash change remained `1,000.00`. The missing investing outflow caused `reconciles=false`.

The dedicated card account is not itself a cash equivalent. The report therefore needs the cash-side ledger movement of the funding journal rather than a document amount, cached balance, or card-account balance adjustment.

## 8. Ledger-Derived Cash Flow Remediation

`PointOfSale.Infrastructure/Accounting/FinancialReportService.cs:429-472` now locates in-period `SupplierCardFunding` journals and in-period reversal journals whose original source is `SupplierCardFunding`. It joins their immutable ledger lines to tenant-scoped accounts, retains only cash-equivalent `10*` and `11*` account codes, groups by account, and calculates `DebitAmount - CreditAmount`.

The resulting non-zero movements are classified as investing activities. For the existing funding, the `1010` credit produces `-100.00` with description `Supplier card funding from Cashbox Main`. No supplier-card document amount or mutable account cache is used to construct the movement.

## 9. Reversal Behavior

`FinancialReportServiceTests.GetCashFlowAsync_SupplierCardFundingAndReversal_UseLedgerCashMovement` verifies both directions. With the funding posted, closing cash is `900.00`, investing is `-100.00`, financing is `1,000.00`, net change is `900.00`, and reconciliation passes. After an in-period journal reversal, the original and reversal cash ledger lines net to zero, the investing item disappears, closing/net cash change return to `1,000.00`, and reconciliation still passes.

This prevents reversal-specific document inference and double counting.

## 10. Focused Regression Results

The final focused command was:

```text
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~SupplierCardFundingEndpointIntegrationTests|FullyQualifiedName~SupplierCardFundingAuthorityIntegrationTests|FullyQualifiedName~FinancialReportServiceTests" --logger "console;verbosity=minimal"
```

Result: **PASS**, `28` passed, `0` failed, `0` skipped, duration `1m 10s`.

Coverage includes funding rollback, safe insufficient-funds responses, processed outbox linkage and payload, balanced posting, failed replay, changed-payload conflict, concurrent overspend prevention, tenant isolation, immutable funding authority, statement/replay behavior, Cash Flow classification, and funding reversal netting.

## 11. Build and Static Validation

| Check | Result |
| --- | --- |
| `dotnet build PointOfSale.API/PointOfSale.API.csproj --configuration Release --no-restore` | PASS, `0` warnings, `0` errors |
| `git diff --check` | PASS; no whitespace errors, line-ending notices only |
| API liveness `GET http://localhost:5010/health/live` | PASS, HTTP `200` |
| Authenticated financial and supplier-card GETs | PASS, HTTP `200` |

The aggregate `/health` endpoint returned HTTP `503`, while the dedicated liveness endpoint and all certification endpoints were operational. No aggregate dependency remediation was included in T-06C scope.

The current `EventOutboxConsistencyArchitectureTests` isolated rerun produced `35` passed and `1` failed. The same failure was present in the earlier broader architecture subset, which produced `59` passed and `1` failed. The failing test is `ProductAndStorefrontProjections_MustRejectStaleEvents` at `EventOutboxConsistencyArchitectureTests.cs:744`; it expects the source text `sqlProjection.LastEventVersion = request.EventVersion` in `ProductSqlAuthorityProjectionWriter.cs`. This product/storefront projection assertion is unrelated to the supplier-card outbox and Cash Flow files and was not changed or weakened.

## 12. Live Browser Certification

Headed Microsoft Edge `152` at `1440x1000` authenticated through the normal AhramStore session and verified:

| Identity/check | Observed | Result |
| --- | --- | --- |
| Tenant | `AhramStore` / `c9c5c1ae-949e-498e-bc63-07242daeb49d` | PASS |
| User | `1bb114d3-a9a3-4a9d-ace3-3690361637a8` | PASS |
| Role | `Owner` | PASS |
| Country | `EG` | PASS |
| Instrument | `T06C-CARD-20260913` | PASS |
| Rendered card balance | EGP `100.00` | PASS |
| Business mutation | None | PASS |
| Blocked unexpected mutations | None | PASS |
| Console/page errors | None | PASS |

The `beforeApi` and `afterApi` snapshots in `verify.json` agree. No replay or corrective action was submitted.

## 13. Live Cash Flow Certification

Authenticated `GET /api/financialreports/cash-flow?from=2026-09-01&to=2026-09-14` returned HTTP `200` before and after the rendered-page verification:

| Measure | Corrected live result | Result |
| --- | ---: | --- |
| Opening cash | `0.00` | PASS |
| Operating cash flow | `0.00` | PASS |
| Investing: supplier-card funding from `1010` | `-100.00` | PASS |
| Financing: owner equity movement | `1,000.00` | PASS |
| Closing cash | `900.00` | PASS |
| Net cash change | `900.00` | PASS |
| `reconciles` | `true` | PASS |

The runtime result demonstrates that the deployed source reads the historical journal and ledger correctly without modifying them.

## 14. Final Database State

The AhramStore final capture occurred at `2026-09-13T23:49:23.8824196Z` under `REPEATABLE READ / READ ONLY / ROLLBACK`. Its fingerprint is still `394AD2C1711D8A3A882FC1E86DE642CD684C3424B67B239CDFCABDAF8E5CCA0D`, exactly matching the stopped-run fingerprint.

| Metric | Final |
| --- | ---: |
| Supplier-card instruments / fundings | `1 / 1` |
| Journals / ledger lines | `2 / 4` |
| Trial debits / credits | `1,100.00 / 1,100.00` |
| Purchases / purchase payments | `0 / 0` |
| Supplier payments / transactions | `0 / 0` |
| Supplier balance | `0.00` |
| Stock / reserved / damaged | `100.00 / 0.00 / 0.00` |
| Accounting outbox total / pending / failed | `1 / 0 / 0` |

No hidden AhramStore mutation occurred during remediation certification.

## 15. Journal and Treasury Reconciliation

Historical journal `01a09ce1-3c2a-7153-beb2-b58385b6a22d` remains posted and unchanged with source ID `534127ff-18b3-41ce-ac0a-0de146807f8e` and reference `T06C-FUND-001-RERUN`.

| Line | Account | Debit | Credit | Balance after |
| ---: | --- | ---: | ---: | ---: |
| 1 | `T06C-CARD-ASSET` | `100.00` | `0.00` | `100.00` |
| 2 | `1010` Main Cashbox | `0.00` | `100.00` | `900.00` |

The `1010` cached and ledger-derived balances both equal `900.00`. The card cached and ledger-derived balances both equal `100.00`. Opening Balance Equity `3010` remains EGP `1,000.00` credit, and tenant trial balance remains balanced at `1,100.00 / 1,100.00`.

## 16. Historical Outbox Reconciliation

**FAIL.** The historical accounting outbox remains total `1`, pending `0`, failed `0`, hash `B205FF2FF0B175F214AFC4F73A2555B19FF561AA8366597F20FC0362FED82B0A`. Its only row is the legitimate T-06D `TreasuryInitialCapitalization` event `d08f1dbd-fbd6-46d7-820e-5f749b0a7ce2`, processed against journal `01a09cc3-fe61-749a-b7b1-5746d4e353ba`.

There is still no outbox row for funding `534127ff-18b3-41ce-ac0a-0de146807f8e`, journal `01a09ce1-3c2a-7153-beb2-b58385b6a22d`, reference `T06C-FUND-001-RERUN`, or event/source type `SupplierCardFunding`. The new implementation cannot retroactively make the old transaction atomic. Creating such a row now would fabricate historical atomicity and violate the no-business-write constraint.

## 17. Protected and Cross-Tenant Safety

AhramStore protected accounts remain unchanged:

| Account | Cached | Ledger | Lines | Result |
| --- | ---: | ---: | ---: | --- |
| `1020` POS Visa | `0.00` | `0.00` | `0` | PASS |
| `1110` Visa Clearing | `0.00` | `0.00` | `0` | PASS |

Fresh read-only captures also exactly match the prior cross-tenant fingerprints:

| Tenant | Final fingerprint | Result |
| --- | --- | --- |
| Shopixy | `7ACEF5ED3DA0EBBC8F0EC674C08F743F300AF941157A215CA12CA9932BEC3EFE` | PASS, unchanged |
| GreatOne | `96FFD97DC1027F68BF23ABA6027D5976C316526E0DB490AF81409A1B696536AC` | PASS, unchanged |

Their supplier-card funding counts remain zero. The certification introduced no cross-tenant financial delta.

## 18. Evidence and Residual Gate

Evidence is retained under `C:\Users\fadya\AppData\Local\Temp\opencode\t06c-rerun-evidence\`:

| File | Purpose |
| --- | --- |
| `verify.json` | Guarded browser identity, API before/after snapshots, network log, and corrected Cash Flow |
| `verify-desktop-en.png` | Rendered supplier-card workspace and EGP `100.00` balance |
| `remediation-final-ahramstore.json` | Final AhramStore read-only state and fingerprint |
| `remediation-final-shopixy.json` | Final Shopixy read-only state and fingerprint |
| `remediation-final-greatone.json` | Final GreatOne read-only state and fingerprint |
| `api-remediation.stdout.log` / `api-remediation.stderr.log` | Restart attempt diagnostics retained separately from the user-restarted API |

The remaining T-06C gate is not a source defect in new funding behavior. It is the missing historical outbox lineage. Closure requires an explicitly approved, auditable reconciliation workflow with defined authorization and semantics. None currently exists in the reviewed product surface, so no reconciliation was attempted.

## 19. Final Verdict

**FINAL VERDICT: FAIL.** Atomic funding/outbox behavior is remediated and passes disposable PostgreSQL endpoint tests. The ledger-derived Cash Flow correction is verified by regression tests and the live AhramStore API, which now returns `reconciles=true`. AhramStore, protected accounts, Shopixy, and GreatOne remained unchanged during certification.

The historical real funding remains an outbox orphan, so end-to-end historical lineage cannot be certified. The orphan must remain untouched until a supported reconciliation workflow is authorized. `T-06 COMPLETE` is not declared.

# Treasury T-06D Full-Screen Audit and Browser Certification: AhramStore

Certification date: 2026-09-14  
Target tenant: `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`)  
Execution mode: headed Microsoft Edge, live API, live PostgreSQL, and read-only reconciliation  
Final verdict: **PASS**

## 1. Executive Verdict

**PASS.** The bounded Treasury Initial Capitalization authority was deployed and exercised once through the rendered AhramStore Owner UI. The resulting immutable source document authorized exactly one EGP 1,000 posting: Dr `1010 Cashbox Main` and Cr `3010 Opening Balance Equity`.

Immediate and final read-only PostgreSQL reconciliation found one source document, one allocation, one posted journal, two ledger lines, and one processed nonfailed outbox row. Cached and ledger-derived balances agree, all tested reports reconcile, the protected accounts and unrelated AhramStore business state are unchanged, and immediate cross-tenant controls found no T-06D delta.

## 2. Scope and Decision Rule

This certification covered the T-06D source authority, migration/model state, Owner route, eligible destinations, localized full-screen behavior, one real capitalization, immutable history, General Ledger linkage, financial reports, outbox state, protected accounts, second-attempt containment, reversal safeguards, and tenant isolation.

The decision rule was scoped to T-06D: `PASS` required a single rendered-UI mutation, exact expected source/journal state, balanced reports, no unexpected same-tenant or mutation-window cross-tenant delta, and passing focused implementation gates. This is not a repository-wide production-readiness waiver.

## 3. Safety Constraints

No direct SQL business write, manual journal, account-balance patch, legacy Cashbox/Bank opening endpoint, generic cash-in endpoint, caller-selected offset, or supplier-card funding action was used. SQL evidence ran under `REPEATABLE READ / READ ONLY / ROLLBACK`.

The browser never supplied a tenant ID. The API used trusted tenant context and independently reloaded the active Owner identity. Credentials, cookies, CSRF values, and tokens are omitted from all report evidence.

Only one real T-06D create POST was admitted. All later post-check browser traffic was read-only and guarded by a route interceptor that would abort any non-GET API request.

## 4. Audit Remediation

The full-screen audit found and corrected three bounded defects before certification:

| Finding | Remediation | Result |
| --- | --- | --- |
| Deferred DTO projections were blocked by `PreventEntityExposureFilter` | Materialized destination and history response projections with `.ToList()` | Live GET endpoints return safe DTO collections |
| T-06D reads could participate in automatic GET retry | Added `skipAutomaticRetry` support and opted both T-06D reads out | Synthetic failure produced exactly one request and failed closed |
| Posted workspace lacked current balance, trace, and canonical journal navigation | Added backend `CurrentBalance`, rendered trace/current balance, and General Ledger links | Verified in headed Edge |

The review also added cancellation for stale loads, stable `INSUFFICIENT_FUNDS` localization, reversal-dialog focus/Escape restoration, and removed duplicate Arabic locale keys found during final cleanup.

## 5. Migration and Model State

Migration `20260913182724_T06DInitialCapitalizationAuthority` is applied. It creates the tenant-owned source and allocation tables with tenant-qualified foreign keys, positive-amount checks, one-active authority, operation/document uniqueness, predecessor constraints, fixed `3010` snapshot enforcement, and concurrency support.

`dotnet ef migrations has-pending-model-changes ... --no-build` returned `No changes have been made to the model since the last migration.` The migration list includes `20260913182724_T06DInitialCapitalizationAuthority` without a pending marker.

## 6. Runtime and Route State

| Probe | Result |
| --- | --- |
| `GET http://localhost:5010/health/live` | HTTP `200` |
| `GET http://localhost:5173/treasury/initial-capitalization` | HTTP `200` |
| Authenticated T-06D destination/history reads | HTTP `200` |
| Authenticated Trial Balance, P&L, Balance Sheet, and Cash Flow reads | HTTP `200` |

`/health/ready` returned HTTP `503` because `payout_external_truth_readiness` reported an unrelated external reconciliation mismatch. PostgreSQL, accounting outbox readiness, MassTransit, Redis, MongoDB, Elasticsearch, and RabbitMQ were healthy. This pre-existing platform readiness signal is recorded but is not evidence of a T-06D defect.

## 7. Authenticated Context

The headed browser independently verified the protected route before mutation:

| Property | Value |
| --- | --- |
| Tenant | `AhramStore` |
| Tenant ID | `c9c5c1ae-949e-498e-bc63-07242daeb49d` |
| User ID | `1bb114d3-a9a3-4a9d-ace3-3690361637a8` |
| Role | `Owner` |
| Country | `EG` |
| Currency presentation | `EGP` |

The route and sidebar are Owner-only, and the API controller uses `[Authorize(Roles = "Owner")]`. The service separately requires an active, non-deleted, same-tenant Owner row.

## 8. Eligible Destination Review

The server returned exactly two selectable destinations at preflight:

| Destination | Treasury type | Ledger account | Opening balance | Eligible |
| --- | --- | --- | ---: | --- |
| Main Cashbox | Cashbox | `1010 Cashbox Main` | `0.00` | Yes |
| Primary Bank Account | Bank | `1030 Bank Account` | `0.00` | Yes |

Accounts `1020`, `1110`, `1040`, `2030`, `3010`, clearing accounts, supplier-card assets, inactive/deleted accounts, non-Assets, foreign identities, and arbitrary GL accounts were absent from the rendered selector.

## 9. Immediate Baseline and Eligibility

The immediate baseline was captured at `2026-09-13T21:53:02.8187488Z`:

| State | Baseline |
| --- | ---: |
| T-06D documents / allocations | `0 / 0` |
| Journals / ledger lines | `0 / 0` |
| Accounting outbox rows | `0` |
| Trial debits / credits | `0.00 / 0.00` |
| Main Cashbox `1010` cached / ledger | `0.00 / 0.00` |
| Primary Bank `1030` cached / ledger | `0.00 / 0.00` |

Baseline fingerprint: `855A607F3320D428DA61FEA64B3740820F4779D3ADDF42A9368E927131F58EBB`. The create form and empty history rendered, proving no active prior authority and no prior Treasury movement that would make the tenant ineligible.

## 10. English Desktop Review

At `1440x1000`, the English/LTR workspace rendered the Owner authority banner, accounting rule, cutover evidence, allocation controls, Dr/Cr preview, both attestations, posting control, and immutable history. The document width was `1425`, equal to client width, with no horizontal overflow or inaccessible controls.

The post-success screen rendered the posting receipt, document number, trace ID, EGP 1,000 total, `1010 Main Cashbox`, current ledger balance, Posted status, immutable evidence, `3010` offset, reversal entry point, and General Ledger links.

## 11. Arabic and RTL Review

The same pre-mutation workspace was rendered in Arabic with `lang=ar` and `dir=rtl`. Core authority, evidence, allocation, preview, attestation, error, history, and reversal labels were localized. At `1440x1000`, document and client widths both remained `1425`, so RTL introduced no horizontal overflow.

A synthetic destination failure rendered the Arabic fail-closed service message. Final locale cleanup left one key each for `viewJournal`, `traceId`, `currentBalance`, and T-06D `insufficientFunds`; the production build parsed both locale files successfully.

## 12. Mobile and Responsive Review

The pre-mutation English workspace at `390x844` rendered the destination and posting controls with document/client widths of `375/375`. The posted workspace was checked again at `390x844`; `scrollWidth=375` and `clientWidth=375`.

No horizontal overflow, page exception, failed response, clipped mutation control, or inaccessible reversal trigger was detected. Full-page mobile screenshots were retained before and after posting.

## 13. Validation and Error Handling

The Post button remained disabled when valid date, destination, amount, and evidence were present but either attestation was missing. Duplicate/empty destinations and nonpositive amounts are rejected client-side and server-side; declaration version, evidence, nonfuture cutover date, Owner identity, and fixed offset are server-authoritative.

The synthetic HTTP `503` read test produced one request, no automatic retry burst, localized fail-closed UI, and no mutation. T-06D accounting conflicts map to stable codes, including `INSUFFICIENT_FUNDS`, idempotency conflict, concurrency conflict, and authorization failure.

## 14. Single Rendered-UI Mutation

The rendered Edge form selected Main Cashbox, entered EGP `1,000.00`, supplied evidence reference `T06D-OPENING-20260914`, and checked both required attestations. One T-06D create POST was admitted with operation ID `006e5876-8bf4-4fd5-ab8e-cf5fde998362`.

The UI displayed a successful posting receipt and saved `06-posted-authority-en-desktop.png`. The browser harness later timed out while waiting for an overly strict exact General Ledger heading after the successful commit; it did not retry the create request. The subsequent corrected all-writes-blocked post-check verified the General Ledger separately.

## 15. Source Document Evidence

| Field | Persisted value |
| --- | --- |
| Document ID | `aa6dabe5-5910-449c-a1e1-fe0c17ba1418` |
| Document number | `TIC-20260913-AA6DABE5` |
| Operation ID | `006e5876-8bf4-4fd5-ab8e-cf5fde998362` |
| Status | Posted |
| Total | EGP `1,000.00` |
| Declaration | `TREASURY_CUTOVER_FUNDS_V1` |
| Evidence | `T06D-OPENING-20260914` |
| Created by | `1bb114d3-a9a3-4a9d-ace3-3690361637a8` |
| Posted journal | `01a09cc3-fe61-749a-b7b1-5746d4e353ba` |
| Mutation version | `2` |

The posted UI receipt trace ID is `3260a688-0b50-4885-9da8-b3015209f171`.

## 16. Cutover Date Semantics

The persisted cutover date is `2026-09-13T00:00:00Z`, represented by PostgreSQL as `2026-09-13T03:00:00+03:00`. Posting occurred at approximately `2026-09-13T21:54:45Z`, locally `2026-09-14T00:54:45+03:00`.

The UI and service use the UTC accounting date when determining the latest nonfuture cutover date, so the rendered default was September 13 while the local certification date and evidence label were September 14. No requested invariant required a September 14 journal date, the persisted date was not future under the server rule, and the immutable document was not altered. Product owners should confirm whether future accounting-date UX should use tenant-local rather than UTC date semantics.

## 17. Allocation and Fixed Offset

Allocation ID `3a7534f4-03fb-4736-87bd-6cb1da3fee4c` freezes:

| Authority field | Value |
| --- | --- |
| Treasury account ID | `01a0828d-7502-71c0-b3d5-d0cd09fcbb42` |
| Treasury snapshot | Main Cashbox / Cashbox |
| Ledger account ID | `01a0828d-734b-7385-9659-ef041fa3e836` |
| Ledger snapshot | `1010` / Cashbox Main |
| Amount | EGP `1,000.00` |

The server-fixed offset is ledger account `01a0828d-734b-7c31-a8a5-03138b89890f`, snapshot `3010 Opening Balance Equity`. No offset picker or caller-selected GL identity exists.

## 18. Journal and Ledger Lines

Journal `01a09cc3-fe61-749a-b7b1-5746d4e353ba`, number `JE-2026-000001`, is Posted with source type `TreasuryInitialCapitalization`, source ID equal to the source document, source reference `TIC-20260913-AA6DABE5`, and the caller-stable idempotency lineage.

| Line | Account | Debit | Credit | Balance after |
| ---: | --- | ---: | ---: | ---: |
| 1 | `1010 Cashbox Main` | `1,000.00` | `0.00` | `1,000.00` |
| 2 | `3010 Opening Balance Equity` | `0.00` | `1,000.00` | `1,000.00` |

No reversal linkage, deletion marker, or extra ledger line exists.

## 19. Treasury Balance Reconciliation

| Account | Baseline cached / ledger | Final cached / ledger | Expected delta | Result |
| --- | --- | --- | ---: | --- |
| `1010 Cashbox Main` | `0.00 / 0.00` | `1,000.00 / 1,000.00` | `+1,000.00` | PASS |
| `1030 Bank Account` | `0.00 / 0.00` | `0.00 / 0.00` | `0.00` | PASS |
| `3010 Opening Balance Equity` | `0.00 / 0.00` | `1,000.00 / 1,000.00` credit-normal | `+1,000.00` | PASS |

The rendered posted authority showed `Current ledger balance: EGP 1,000.00`, sourced from the backend destination projection rather than local arithmetic.

## 20. Trial Balance

The live authenticated report for `2026-09-01` through `2026-09-14` returned HTTP `200`:

| Measure | Value |
| --- | ---: |
| Total debits | `1,000.00` |
| Total credits | `1,000.00` |
| Variance | `0.00` |
| Is balanced | `true` |

The report contains only the `1010` debit line and `3010` credit line for AhramStore.

## 21. Balance Sheet

The live Balance Sheet as of `2026-09-14` returned HTTP `200`:

| Measure | Value |
| --- | ---: |
| Total assets | `1,000.00` |
| Total liabilities | `0.00` |
| Total equity | `1,000.00` |
| Liabilities plus equity | `1,000.00` |
| Variance | `0.00` |
| Is balanced | `true` |

The only nonzero lines are `1010` under Assets and `3010` under Equity.

## 22. Profit and Loss

The live P&L for `2026-09-01` through `2026-09-14` returned HTTP `200` with zero revenue, COGS, expenses, gross profit, and net profit. This confirms that the opening Equity credit was not misclassified as revenue and the asset debit did not affect expense.

## 23. Cash Flow

The live Cash Flow for `2026-09-01` through `2026-09-14` returned HTTP `200` and reconciled:

| Measure | Value |
| --- | ---: |
| Opening cash | `0.00` |
| Closing cash | `1,000.00` |
| Operating cash flow | `0.00` |
| Investing cash flow | `0.00` |
| Financing cash flow | `1,000.00` |
| Net cash change | `1,000.00` |
| Reconciles | `true` |

The financing detail is `Net owner equity movement`, account family `3xxx`, EGP `1,000.00`.

## 24. Outbox and Atomicity

Outbox row `d08f1dbd-fbd6-46d7-820e-5f749b0a7ce2` is linked to the source document and posted journal. It has source/event type `TreasuryInitialCapitalization`, the expected allocation/evidence payload, zero attempts, a non-null processed timestamp, `IsFailed=false`, and no last error.

Final state is total `1`, pending `0`, failed `0`. The document, allocation, journal, ledger materialization, and processed outbox committed atomically; no draft or partial artifact exists.

## 25. Immutability and Second-Attempt Containment

The post-success UI renders one active authority and the message that a second capitalization is blocked. The create form, evidence input, and Post button are absent. The all-writes-blocked post-check found exactly one active document and attempted no mutation.

A second real POST was deliberately not sent because the certification constraint permitted exactly one financial POST. Automated endpoint and authority tests cover exact replay, changed-payload conflict, one-active uniqueness, concurrent first requests, and zero-mutation failure behavior. Together, database uniqueness, serializable tenant locking, service eligibility, and the locked UI provide the negative-path evidence without risking another live write.

## 26. Reversal Safety

The posted record exposes only full reversal, not edit, delete, partial reversal, or balance override. The dialog initially focuses the mandatory reason field, keeps `Post full reversal` disabled until both a nonempty reason and explicit confirmation are present, closes on Escape, and restores focus to the trigger.

No reversal was posted. Service and posting tests verify frozen-journal validation, stable reversal identity, original/reversal lineage, replacement predecessor rules, same-tenant authority, and preservation of `PreventNegative`; insufficient available destination cash maps to `INSUFFICIENT_FUNDS` and must roll back.

## 27. Protected and Unrelated AhramStore State

| State | Baseline | Final | Result |
| --- | ---: | ---: | --- |
| `1020` balance / lines | `0.00 / 0` | `0.00 / 0` | Unchanged |
| `1110` balance / lines | `0.00 / 0` | `0.00 / 0` | Unchanged |
| Supplier-card fundings | `0` | `0` | Unchanged |
| Purchase invoices / payments | `0 / 0` | `0 / 0` | Unchanged |
| Supplier payments / transactions | `0 / 0` | `0 / 0` | Unchanged |
| Customer count / balance | `0 / 0.00` | `0 / 0.00` | Unchanged |
| Supplier count / balance | `1 / 0.00` | `1 / 0.00` | Unchanged |
| Variant stock / reserved / damaged | `100.00 / 0.00 / 0.00` | `100.00 / 0.00 / 0.00` | Unchanged |

The protected-account hash stayed `A3D753604F8B6AA52B85A8471D5C0D1FFD8CBF18271B1CA961975D7F77B8E36F`; inventory stayed `BFAC42CC7B6533F88E19C5D4A8E3C02FC15A55F21E8D77E59AB43D79AA3CE2C8`.

## 28. Cross-Tenant Isolation

Immediate controls bracketed the AhramStore mutation:

| Tenant | Before fingerprint | Immediate-after fingerprint | T-06D rows | Result |
| --- | --- | --- | ---: | --- |
| Shopixy | `1DB509B6761EC42EDF87361F5B7FF7B8A955FA29CEBA704045B18EA76C270246` | same | `0` | Exact match |
| GreatOne | `96FFD97DC1027F68BF23ABA6027D5976C316526E0DB490AF81409A1B696536AC` | same | `0` | Exact match |

At the later `2026-09-13T22:02:56Z` read, GreatOne still matched exactly. Shopixy's total fingerprint changed only because retained processed accounting-outbox rows decreased from `35` to `33`; its journals, ledger lines, Trial Balance, Treasury hash, protected accounts, inventory, customers, suppliers, purchases, and all T-06D counts remained unchanged. The repository schedules `AccountingOutboxCleanupJob` hourly with processed-message retention, so this post-window housekeeping is separately attributable and does not contradict the exact immediate mutation-window isolation result.

## 29. Verification and Evidence Catalog

| Gate | Result |
| --- | --- |
| T-06D backend filter | PASS, `19/19` |
| Entity exposure, exception mapping, and financial reports | PASS, `30/30` |
| T-06D frontend API/page/architecture tests | PASS, `8/8` |
| API build | PASS, 0 warnings and 0 errors |
| Frontend production build | PASS, existing large-chunk warning |
| EF model drift | PASS, none |
| `git diff --check` | PASS, line-ending notices only |
| Headed Edge preflight | PASS |
| All-writes-blocked post-check and four live reports | PASS |

Standalone ESLint remains unavailable because ESLint 9 is installed without a flat `eslint.config.js`, `eslint.config.mjs`, or `eslint.config.cjs`. Relevant evidence is under `C:\Users\fadya\AppData\Local\Temp\opencode\t06d-browser-evidence\`, including `preflight.json`, `post-check.json`, screenshots `01` through `06`, `08`, and `09`. Immediate SQL captures are `t06d-full-immediate-before-ahramstore.json`, `t06d-full-immediate-after-ahramstore.json`, and the matching Shopixy/GreatOne controls under `t06c-evidence`.

The first guarded post-mode artifact, `post.json`, records a pre-submit assertion mismatch caused only by a nonbreaking currency space and proves that run did not consume a mutation. The successful guarded run produced the posted screenshot and committed evidence, then timed out after later navigation. Immediate SQL, the rendered receipt, the immutable post-check, and the final matching AhramStore fingerprint independently establish the successful one-time result.

## 30. Final Certification

AhramStore now has one valid active Treasury Initial Capitalization authority for EGP 1,000. It was created by the authenticated Owner through rendered controls, uses only the eligible Main Cashbox destination, fixes the offset to `3010`, posts exactly two balanced lines through `FinancialPostingService`, persists complete source/outbox lineage, preserves protected and unrelated state, renders correctly across English, Arabic, desktop, and mobile, and remains immutable with reversal-only correction.

The final AhramStore fingerprint at `2026-09-13T22:02:56.5145393Z` is `C2BBC3292AC7C7D894111423538487B021D409A9F97349791279574D8AD3D3A3`, exactly matching the immediate post-mutation fingerprint. No later hidden AhramStore mutation occurred.

**FINAL VERDICT: PASS**

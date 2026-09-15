# Treasury T-06C Golden Real Business-Data Certification: AhramStore

Certification date: 2026-09-13  
Target tenant: `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`)  
Execution mode: headed Microsoft Edge, live API, live PostgreSQL, read-only SQL reconciliation  
Final verdict: **FAIL**

## 1. Executive Verdict

**FAIL.** The first mandatory financial scenario, funding the dedicated supplier card with `100.00 EGP`, did not succeed. The rendered UI submitted one real request, and the API returned HTTP `500` with `code=INTERNAL_ERROR` instead of creating the required funding and journal.

The request was atomic: PostgreSQL reconciliation proves that no funding row, journal, ledger line, outbox row, supplier transaction, balance change, or inventory change survived. That rollback is correct, but it does not satisfy the mandatory happy-path invariant.

Execution stopped after this first financial invariant failure. No repair, direct SQL write, source-balance fabrication, retry, or subsequent financial scenario was performed.

## 2. Scope and Method

This certification attempted the requested real-data sequence against the locally running application:

1. Establish authenticated tenant and runtime evidence.
2. Capture Baseline #1 in PostgreSQL.
3. Create one normal zero-balance supplier through the rendered browser UI.
4. Create one dedicated funded-company-card instrument through the rendered browser UI.
5. Capture Baseline #2.
6. Submit one `100.00 EGP` card-funding mutation through the rendered browser UI.
7. Reconcile the mutation immediately through browser-observed API evidence and read-only PostgreSQL queries.
8. Stop on the first financial invariant failure and complete read-only final and cross-tenant checks.

No sub-agent was used. All SQL evidence ran in `REPEATABLE READ / READ ONLY / ROLLBACK`. SQL was never used to create, update, repair, or delete business data.

## 3. Runtime and Authenticated Context

| Check | Evidence | Result |
| --- | --- | --- |
| API liveness | `GET http://localhost:5010/health/live` | HTTP `200` |
| API surface | `GET http://localhost:5010/swagger/index.html` | HTTP `200` |
| Web route | `GET http://localhost:5173/supplier-card-funding` | HTTP `200` |
| Browser | Microsoft Edge `152`, headed | PASS |
| Tenant name | `/api/settings/default-country` returned `AhramStore` | PASS |
| Tenant ID | Session returned `c9c5c1ae-949e-498e-bc63-07242daeb49d` | PASS |
| Authenticated role | `Owner` | PASS |
| Country/currency context | Country `EG`; UI displayed `EGP` | PASS |
| Lifecycle authority | Active, approved, KYC verified, payments not blocked | PASS |

The browser identity was independently checked before mutations. The authenticated user ID was `1bb114d3-a9a3-4a9d-ace3-3690361637a8`. Credentials and session tokens are intentionally omitted from this report.

## 4. Baseline #1

Baseline #1 was captured at `2026-09-13T16:16:06.2341855Z` before T-06C mutations.

| State | Baseline #1 |
| --- | ---: |
| Suppliers | 0 |
| Supplier balance sum | `0.00` |
| Supplier transactions | 0 |
| Purchase invoices | 0 |
| Purchase invoice payments | 0 |
| Supplier payments | 0 |
| Supplier-card instruments | 0 |
| Supplier-card fundings | 0 |
| Journals | 0 |
| Ledger lines | 0 |
| Accounting outbox rows | 0 |
| Products / variants | 1 / 4 |
| Variant stock / reserved / damaged | `100.00 / 0.00 / 0.00` |
| Treasury accounts | 6 |
| Account `1020` balance / lines | `0.00 / 0` |
| Account `1110` balance / lines | `0.00 / 0` |

The T-06C expanded baseline fingerprint was `B45C8C49DF219E49AFEF83FC2EB81672CB45A7C08C4EA265AAD37C0109520990`. The prior T-06B reference fingerprint was `7A6535EC7D409270296098E32FA469341EB1B69B166F1AF6DFB13F8D91602CC1`; the formulas are not byte-comparable because T-06C added treasury and protected-account components.

## 5. Supplier and Instrument Creation

The supplier was created through the rendered `/suppliers` UI:

| Field | Value |
| --- | --- |
| Name | `T06C-SUPPLIER-20260913` |
| ID | `1008e591-9a9f-4ea3-9ac6-2386c0456806` |
| HTTP result | `201 Created` |
| Opening balance | `0.00` |
| Supplier transactions created | 0 |

Immediate reconciliation showed only suppliers changed `0 -> 1`. Financial, treasury, inventory, journal, ledger-line, and outbox state remained unchanged.

The funding instrument was then created through the rendered `/supplier-card-funding` UI:

| Field | Value |
| --- | --- |
| Display name | `T06C-CARD-20260913` |
| Instrument ID | `cee6f007-0685-487e-b2b2-d977faff9d60` |
| Type | `FundedCompanyCard` |
| Authority version | 1 |
| Dedicated ledger account | `T06C-CARD-ASSET` |
| Ledger account ID | `4db74e2c-97e3-4a6a-bd98-d4e4c99ddfff` |
| Ledger account name | `T06C-CARD-20260913 Asset` |
| Funding source | `Main Cashbox` / account `1010` |
| HTTP result | `201 Created` |
| Initial card balance | `0.00` |

PostgreSQL proves the dedicated account is an active Asset with `IsSupplierCardFunding=true`, `PreventNegative=true`, and `CurrentBalance=0.00`. Ledger accounts changed `32 -> 33`; supplier-card-designated accounts changed `0 -> 1`. No financial posting was generated by instrument setup.

## 6. Baseline #2

Baseline #2 was captured at `2026-09-13T16:17:10.4719115Z`, after supplier/instrument setup and before the real funding submission.

| State | Baseline #2 |
| --- | ---: |
| Suppliers / supplier balance | `1 / 0.00` |
| Supplier-card instruments / card balance | `1 / 0.00` |
| Supplier-card fundings | 0 |
| Purchase invoices / payments | `0 / 0` |
| Supplier payments / transactions | `0 / 0` |
| Journals / ledger lines | `0 / 0` |
| Accounting outbox | 0 |
| Variant stock / reserved / damaged | `100.00 / 0.00 / 0.00` |
| `1020` / `1110` | both `0.00`, zero lines |

Fingerprint: `C1239AF22B71A4BBC3DBE073F9F9C4E26644CDEC34641ABA3134121721BB6C3F`.

Two pre-submit locator failures occurred before the harness found the correct rendered controls. Both happened before any funding POST. Read-only repeat captures retained the exact Baseline #2 fingerprint, proving no hidden mutation.

## 7. Funding Happy Path

**FAIL.** The rendered UI submitted exactly one real funding request.

| Request element | Value |
| --- | --- |
| Endpoint | `POST /api/supplier-card-funding-instruments/cee6f007-0685-487e-b2b2-d977faff9d60/fundings` |
| Idempotency key / request ID | `supplier-card-funding-8a57ed47-e942-46f6-8063-822ca46339de` |
| Funding instrument version | 1 |
| Funding source treasury ID | `01a0828d-7502-71c0-b3d5-d0cd09fcbb42` |
| Amount | `100.00 EGP` |
| Date | `2026-09-13` |
| Reference | `T06C-FUND-001` |
| Actual HTTP status | `500` |
| Actual API code | `INTERNAL_ERROR` |
| Trace ID | `supplier-card-funding-8a57ed47-e942-46f6-8063-822ca46339de` |

Mandatory expected state was card `+100.00`, source `-100.00`, exactly one funding row, one posted balanced journal, two ledger lines, and one processed outbox row. Actual state was card `0.00`, source `0.00`, zero funding rows, zero journals, zero ledger lines, and zero outbox rows.

The configured source authority was legitimate, but it could not finance the requested posting:

| Authorized source | Ledger balance | `PreventNegative` |
| --- | ---: | --- |
| Main Cashbox / `1010` | `0.00` | true |
| Primary Bank Account / `1030` | `0.00` | true |

The funding journal credits source account `1010` by `100.00`. `FinancialPostingService.ValidateNoNegativeBalances` therefore projects `0.00 - 100.00 = -100.00` and throws `InsufficientFundsException` (`FinancialPostingService.cs:566-609`). The funding controller catches `InvalidOperationException` but not this `AccountingException` (`SupplierCardFundingInstrumentsController.cs:273-291`). `GlobalExceptionMiddleware` has no accounting-exception mapping and falls through to `500 INTERNAL_ERROR` (`GlobalExceptionMiddleware.cs:70-96`).

This produces two blocking findings:

- The AhramStore clean baseline provides no funded, authorized source from which the mandatory happy path can run without violating the negative-balance guard.
- Expected insufficient funds is exposed as an unexpected server fault instead of a stable validation/business error contract.

The controller transaction correctly rolled back the funding row saved before posting (`SupplierCardFundingInstrumentsController.cs:234-270`).

## 8. Card Purchase

**NOT EXECUTED.** The mandatory funding happy path failed and left card balance `0.00`. Running a card purchase after that failure would not certify the requested funded-card lifecycle and would violate the stop condition.

Expected but unverified: purchase completion, supplier/card authority snapshots, card credit for the card-paid amount, correct inventory increase, supplier balance treatment, one balanced journal, and one processed outbox row.

## 9. Funding Replay

**NOT EXECUTED.** No replay was sent after the financial invariant failure. The caller-owned idempotency key is present in the failed request evidence, but the failed transaction persisted no funding document against which a successful replay could be certified.

## 10. Supplier Payment

**NOT EXECUTED.** No supplier payment mutation was attempted after the stop condition.

## 11. Mixed Purchase

**NOT EXECUTED.** No mixed card/cash purchase mutation was attempted after the stop condition.

## 12. Partial Credit Purchase

**NOT EXECUTED.** No partial-credit purchase mutation was attempted after the stop condition.

## 13. Pure Credit Purchase

**NOT EXECUTED.** No pure-credit purchase mutation was attempted after the stop condition.

## 14. Supplier Payment After Credit

**NOT EXECUTED.** No payable existed because credit purchase scenarios were stopped.

## 15. Edit / Reversal

**NOT EXECUTED.** No purchase existed to edit. Consequently, journal reversal, repost, immutable authority snapshots, stock correction, and supplier-balance correction could not be certified.

## 16. Statement Verification

The final rendered funding workspace showed one active instrument with an `EGP 0.00` balance. Database ledger evidence contains no rows for its dedicated account, so the mathematically valid opening, debit, credit, and closing totals are all `0.00`.

The intended funded/purchase/payment statement sequence was not executed and is therefore not certified.

## 17. Trial Balance

Final AhramStore ledger totals are:

| Measure | Amount |
| --- | ---: |
| Debits | `0.00` |
| Credits | `0.00` |
| Difference | `0.00` |
| Journals | 0 |
| Ledger lines | 0 |

The trial balance remains balanced because no financial posting survived. This proves rollback integrity, not the requested happy path.

## 18. Inventory Evidence

Inventory remained unchanged through supplier setup, instrument setup, the failed funding, and final reconciliation.

| Measure | Baseline #1 | Final | Delta |
| --- | ---: | ---: | ---: |
| Products | 1 | 1 | 0 |
| Variants | 4 | 4 | 0 |
| Variant stock | `100.00` | `100.00` | `0.00` |
| Reserved | `0.00` | `0.00` | `0.00` |
| Damaged | `0.00` | `0.00` | `0.00` |
| Warehouse stock | `0.00` | `0.00` | `0.00` |

Inventory hash remained `BFAC42CC7B6533F88E19C5D4A8E3C02FC15A55F21E8D77E59AB43D79AA3CE2C8`.

## 19. Protected Account Evidence

| Account | Baseline balance / lines | Final balance / lines | Result |
| --- | --- | --- | --- |
| `1020` POS Visa Account | `0.00 / 0` | `0.00 / 0` | Unchanged |
| `1110` Visa Clearing | `0.00 / 0` | `0.00 / 0` | Unchanged |

Protected-account hash remained `A3D753604F8B6AA52B85A8471D5C0D1FFD8CBF18271B1CA961975D7F77B8E36F`. No supplier-card operation used customer-card or clearing authority.

## 20. Outbox and Drain Evidence

| State | Baseline #1 | Baseline #2 | After failure | Final |
| --- | ---: | ---: | ---: | ---: |
| Total outbox rows | 0 | 0 | 0 | 0 |
| Pending | 0 | 0 | 0 | 0 |
| Failed | 0 | 0 | 0 | 0 |

There was nothing to drain because the posting transaction rolled back before commit. The empty hash remained `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`. Processed outbox behavior for a successful T-06C funding was not certified.

## 21. SQL Evidence

All reconciliation was tenant-filtered and executed in `REPEATABLE READ / READ ONLY / ROLLBACK`.

Final capture at `2026-09-13T16:23:38.4728421Z`:

| Entity or amount | Final value |
| --- | ---: |
| Ledger accounts / supplier-card accounts | `33 / 1` |
| Suppliers / supplier balance | `1 / 0.00` |
| Supplier transactions | 0 |
| Instruments / instrument balance | `1 / 0.00` |
| Fundings | 0 |
| Purchases / purchase payments | `0 / 0` |
| Supplier payments | 0 |
| Journals / ledger lines | `0 / 0` |
| Trial debits / credits | `0.00 / 0.00` |
| Outbox total / pending / failed | `0 / 0 / 0` |

The immediate post-failure fingerprint exactly matched Baseline #2: `C1239AF22B71A4BBC3DBE073F9F9C4E26644CDEC34641ABA3134121721BB6C3F`. This is direct evidence that the failed request introduced no persisted delta.

The enhanced final fingerprint is `092722021B2D02D399EC148CE5E17564BC672CA64F7F70FD242135F6DEF72771`; its compatible legacy projection remains `C1239AF22B71A4BBC3DBE073F9F9C4E26644CDEC34641ABA3134121721BB6C3F`.

## 22. API Evidence

Browser-observed responses established:

| Operation | Result |
| --- | --- |
| Login/session | HTTP `200`; AhramStore Owner |
| Supplier create | HTTP `201`; expected supplier ID returned |
| Instrument create | HTTP `201`; expected instrument/account/source returned |
| Funding sources | HTTP `200`; only Main Cashbox and Primary Bank offered |
| Funding submit | HTTP `500`; `INTERNAL_ERROR` |
| Post-failure supplier read | HTTP `200`; balance `0.00` |
| Post-failure instrument read | HTTP `200`; balance `0.00` |
| Post-failure purchase read | HTTP `200`; zero rows |
| Post-failure treasury summary | HTTP `200`; all totals zero |

The response body was `{"code":"INTERNAL_ERROR","message":"An unexpected error occurred.","traceId":"supplier-card-funding-8a57ed47-e942-46f6-8063-822ca46339de","data":null}`.

Authorization cookies, CSRF values, and bearer material are excluded from the report.

## 23. Browser Evidence

The mutations were performed through rendered controls in headed Microsoft Edge, not with direct API mutation calls.

Desktop evidence used viewport `1440x1000` and includes:

- `supplier-before.png`, `supplier-form-ready.png`, `supplier-after.png`
- `instrument-before.png`, `instrument-form-ready.png`, `instrument-after.png`
- `fund-before.png`, `fund-form-ready.png`, `fund-after.png`
- `verify-before.png`, `verify-identity-workspace.png`

The funding form visibly offered the configured Main Cashbox authority and submitted the entered `100.00` amount and `T06C-FUND-001` reference. The post-submit UI retained the zero-balance card and displayed the failure path corresponding to the HTTP 500 response.

Read-only mobile evidence used viewport `390x844`. It loaded `/supplier-card-funding` with `scrollWidth=390`, no page exception, the active instrument, its dedicated ledger account, Main Cashbox authority, and `EGP 0.00` card balance. Evidence files are `mobile-before.png`, `mobile-identity-workspace.png`, and `mobile.json`.

Initial unauthenticated session checks returned expected `401` responses before successful login. No authenticated page exceptions occurred. A CSP console warning about `frame-ancestors` in a meta element is unrelated to the financial failure.

## 24. Final Reconciliation

| Invariant | Expected at completion | Actual final state | Result |
| --- | --- | --- | --- |
| Supplier | One normal supplier | One, balance `0.00` | PASS |
| Dedicated card authority | One instrument/account | One active instrument/account | PASS |
| Card funding | `100.00` available | `0.00` | FAIL |
| Funding source movement | `-100.00` | `0.00` | FAIL |
| Funding document | Exactly one | Zero | FAIL |
| Funding journal | Exactly one posted, balanced | Zero | FAIL |
| Funding ledger lines | Exactly two | Zero | FAIL |
| Funding outbox | Exactly one processed | Zero | FAIL |
| No partial failed state | No partial rows/balances | No partial state | PASS |
| Purchases and payments | Requested full scenario catalog | Zero | NOT EXECUTED |
| Inventory | Scenario deltas reconciled | Unchanged because scenarios stopped | NOT CERTIFIED |
| Trial balance | Balanced | `0.00 = 0.00` | PASS, rollback only |
| `1020` and `1110` | Unchanged | Unchanged | PASS |

The only intentional persisted T-06C state is the supplier, instrument, and dedicated ledger account created before the failed financial mutation.

## 25. Cross-Tenant Isolation

Shopixy and GreatOne were captured before AhramStore mutations and again during final read-only reconciliation.

| Tenant | Before fingerprint | Final compatible fingerprint | Result |
| --- | --- | --- | --- |
| Shopixy | `A12BAB3BEAA7FFB5F8CE2883B70C4C0A9ED090D9590E252483FF252102F14D23` | `A12BAB3BEAA7FFB5F8CE2883B70C4C0A9ED090D9590E252483FF252102F14D23` | Exact match |
| GreatOne | `BF5DF97E3F41E2057164CB0294E63BBBCFE497AE22FF1121421B9973160D7E98` | `BF5DF97E3F41E2057164CB0294E63BBBCFE497AE22FF1121421B9973160D7E98` | Exact match |

No cross-tenant row, balance, journal, inventory, supplier, instrument, funding, or outbox delta was detected.

## 26. Replay / Overspend / Deactivation Negative Evidence

**NOT EXECUTED after stop condition.** Specifically:

- Funding replay with the same key was not sent.
- Replay with a conflicting payload was not sent.
- Card overspend was not attempted because the card never acquired funds.
- Deactivation with a non-zero balance could not be reached.
- Purchase/payment attempts against a deactivated instrument were not sent.

Automated tests cover funded replay using a synthetic `500.00` source opening, but that is not real AhramStore evidence and does not cover the observed zero-balance HTTP contract.

## 27. Journal Reference Catalog

No T-06C journal was committed, so there is no valid journal ID, journal number, source-document ID, or source-reference catalog to report.

| Intended source | Reference | Committed journal | Result |
| --- | --- | --- | --- |
| Supplier card funding | `T06C-FUND-001` | None | FAIL |
| Card purchase | Not created | None | NOT EXECUTED |
| Supplier payment | Not created | None | NOT EXECUTED |
| Mixed/credit purchases | Not created | None | NOT EXECUTED |
| Edit reversal/repost | Not created | None | NOT EXECUTED |

Creating or fabricating journal references would violate certification rules and was not done.

## 28. Final Certification

Verification commands completed after read-only reconciliation:

| Command/check | Result |
| --- | --- |
| `dotnet build .\PointOfSale.sln --no-restore` | PASS, 0 warnings, 0 errors |
| Focused `SupplierCardFundingAuthorityIntegrationTests` | PASS, 2/2 |
| Supplier-card frontend tests | PASS, 18/18 |
| `npm run build` | PASS; existing large-chunk warning |
| Read-only AhramStore final capture | PASS |
| Shopixy/GreatOne exact compatible fingerprint comparison | PASS |
| API/web health checks | PASS, all HTTP 200 |

The passing automated happy-path test seeds its funding source with `500.00` before funding. AhramStore had no such source balance. The suite also lacks an end-to-end assertion that `InsufficientFundsException` from the funding endpoint produces a stable non-500 business response. The real business-data result therefore overrides the synthetic happy-path result for this certification.

Release-blocking remediation is required before rerunning T-06C:

1. Define and execute a legitimate product workflow that capitalizes an authorized Cashbox/Bank source before supplier-card funding, or provide a baseline with sufficient real source funds. Do not bypass `PreventNegative`.
2. Map `InsufficientFundsException`/accounting business exceptions to an explicit stable API status and code instead of `500 INTERNAL_ERROR`.
3. Add endpoint-level zero-balance and insufficient-funds tests that assert atomic rollback and the non-500 error contract.
4. Rerun the full T-06C sequence from a documented valid baseline; do not infer certification for the stopped scenarios from unit/integration tests.

**FINAL VERDICT: FAIL**

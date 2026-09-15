# Treasury T-06C Remediation - 2026-09-13

## 1. Executive Verdict

**OVERALL VERDICT: FAIL.** The observed insufficient-funds defect is remediated and verified: supplier-card funding from a zero or insufficient source now returns HTTP `409`, code `INSUFFICIENT_FUNDS`, a safe message, structured amounts, and the caller trace ID while the transaction rolls back completely. Disposable PostgreSQL endpoint tests also prove exact-balance success, failed replay safety, changed-payload idempotency conflict, concurrent overspend prevention, and tenant isolation.

The full T-06C Golden Scenario was intentionally not rerun. AhramStore still has `0.00 EGP` in both authorized source accounts, and repository review found no enabled official workflow that can legitimately establish an initial positive Cashbox or Bank balance. The error-contract remediation is **PASS**; readiness to execute and pass the complete real-business-data scenario is **FAIL** until an approved capitalization workflow or legitimately funded baseline exists.

## 2. Scope and Safety Constraints

This work was limited to the T-06C failure response, rollback and concurrency regression coverage, frontend handling, read-only workflow review, and non-mutation reconciliation. No sub-agent, commit, migration, direct SQL business write, manual balance update, `PreventNegative` bypass, opening balance, funding, purchase, supplier payment, or full Golden Scenario was executed.

All financial mutations occurred only in disposable test databases. AhramStore SQL evidence used `REPEATABLE READ / READ ONLY / ROLLBACK`. Accounts `1020` and `1110` were not changed.

## 3. Prior Failure and Baseline

The prior Golden certification submitted one rendered funding request for `100.00 EGP` using trace and idempotency key `supplier-card-funding-8a57ed47-e942-46f6-8063-822ca46339de`. The API returned HTTP `500`, code `INTERNAL_ERROR`, although the financial transaction correctly rolled back.

AhramStore retained the intended setup:

| Entity | Identity | State |
| --- | --- | --- |
| Tenant | `c9c5c1ae-949e-498e-bc63-07242daeb49d` | `AhramStore`, active |
| Supplier | `1008e591-9a9f-4ea3-9ac6-2386c0456806` | `T06C-SUPPLIER-20260913`, balance `0.00` |
| Instrument | `cee6f007-0685-487e-b2b2-d977faff9d60` | `T06C-CARD-20260913`, active, balance `0.00` |
| Dedicated account | `4db74e2c-97e3-4a6a-bd98-d4e4c99ddfff` | `T06C-CARD-ASSET`, `PreventNegative=true` |
| Funding source | Main Cashbox / `1010` | ledger and cached balance `0.00` |

## 4. Root Cause

The funding journal correctly attempts Dr dedicated supplier-card Asset and Cr the configured Treasury source. `FinancialPostingService.ValidateNoNegativeBalances` rejected the `1010` credit because it projected `0.00 - 100.00 = -100.00` on a `PreventNegative` account and threw `InsufficientFundsException`.

The controller transaction preserved atomicity, but `GlobalExceptionMiddleware` previously had no mapping for that accounting business exception. It therefore emitted the generic HTTP `500 INTERNAL_ERROR`. A second defect exposed by the new concurrent endpoint case was that competing fundings could reach posting without first serializing on the authorized source ledger row, allowing the loser to time out as HTTP `503` rather than recompute the committed balance.

## 5. Backend Remediation

`PointOfSale.API/Middleware/GlobalExceptionMiddleware.cs:73` now maps `InsufficientFundsException` centrally to HTTP `409 Conflict` and `INSUFFICIENT_FUNDS`. The mapping returns only safe account and amount data and logs at warning level; it does not expose the exception type, stack, or internal message.

`PointOfSale.API/Controllers/SupplierCardFundingInstrumentsController.cs:230` now locks the validated source ledger account inside the existing funding transaction before posting. `LockFundingSourceAsync` at line 363 uses PostgreSQL `FOR UPDATE` and the SQL Server `UPDLOCK, ROWLOCK, HOLDLOCK` equivalent. This keeps lock ownership within the transaction and makes a waiting request reload the winner's balance before applying the negative-balance guard.

No negative-balance rule, funding authority, journal shape, idempotency key, or transaction boundary was weakened.

## 6. Stable API Contract

The remediated insufficient-funds response is:

```json
{
  "code": "INSUFFICIENT_FUNDS",
  "message": "The selected account does not have sufficient available funds.",
  "traceId": "<caller correlation id>",
  "data": {
    "accountCode": "1010",
    "availableBalance": 0.00,
    "requiredAmount": 100.00,
    "shortfall": 100.00
  }
}
```

The status is HTTP `409`. The global middleware regression test verifies the exact code, message, trace ID, structured values, and absence of the exception name. Endpoint tests verify the same contract through real HTTP requests and PostgreSQL posting behavior.

## 7. Frontend Remediation

`PointOfSale.Web/src/services/supplierCardFundingApi.js:61` maps API code `INSUFFICIENT_FUNDS` to `supplierCards.errors.insufficientFunds` instead of relying on the backend message. English and Arabic locale files provide dedicated safe user text.

Frontend regression coverage verifies that the funding form remains open, submit remains available for a corrected action, no automatic retry is sent, no false success is displayed, and the localized insufficient-funds message is shown.

## 8. Endpoint Regression Evidence

`PointOfSale.Tests/Integration/SupplierCardFundingEndpointIntegrationTests.cs` starts a real HTTP application and uses isolated disposable PostgreSQL databases.

| Case | Verified result |
| --- | --- |
| `0.00 / 100.00` | HTTP `409 INSUFFICIENT_FUNDS`; zero persisted delta |
| `99.99 / 100.00` | HTTP `409`; `0.01` shortfall; zero persisted delta |
| `100.00 / 100.00` | HTTP `200`; one funding, one balanced journal, two lines; source closes at zero |
| Failed same-key replay | Same business failure on both requests; no persisted idempotency artifact |
| Committed-key changed payload | HTTP `409 CONFLICT`; no second mutation |
| Concurrent `80.00 + 80.00` from `100.00` | One success; loser gets `INSUFFICIENT_FUNDS` with `20.00` available; no overdraw |
| Foreign tenant | Rejected; owner tenant remains unchanged |

All seven cases passed.

## 9. Atomicity, Idempotency, and Concurrency

Failed funding requests leave funding documents, journals, ledger lines, outbox rows, cached balances, and ledger-derived balances unchanged. Successful exact-balance funding creates one immutable document and one balanced two-line posting, with cached and ledger-derived balances agreeing.

Failed requests do not reserve an idempotency key because the entire transaction rolls back. A committed key replays only its matching request; a changed payload conflicts without mutation. Source-row locking serializes competing fundings so only spend supported by the latest committed balance can post.

## 10. Build and Test Evidence

| Verification | Result |
| --- | --- |
| Endpoint integration suite | PASS, `7/7` |
| T-06 authority plus purchase-payment integrity and endpoint cases | PASS, `19/19` |
| Global exception middleware suite | PASS, `5/5` |
| Supplier-card/Purchase/SOA frontend suite | PASS, `26/26` |
| `dotnet build .\PointOfSale.sln --configuration Release --no-restore` | PASS, `0` errors; `322` existing warnings |
| `npm run build` | PASS; existing large-chunk warning |
| API liveness, Swagger, and supplier-card web route | PASS, HTTP `200` for all three |
| `git diff --check` | PASS; line-ending notices only |

The backend tests were built to an isolated temporary output because the shared Development API process held its normal Debug binaries. The shared process was not stopped or modified.

## 11. AhramStore Non-Mutation Evidence

The pre-remediation capture was taken at `2026-09-13T16:41:05.5933665Z`; the post-remediation capture was taken at `2026-09-13T17:05:46.4538469Z`. Both used `REPEATABLE READ / READ ONLY / ROLLBACK`.

| Evidence | Before | After | Result |
| --- | --- | --- | --- |
| Full fingerprint | `092722021B2D02D399EC148CE5E17564BC672CA64F7F70FD242135F6DEF72771` | same | Exact match |
| Legacy fingerprint | `C1239AF22B71A4BBC3DBE073F9F9C4E26644CDEC34641ABA3134121721BB6C3F` | same | Exact match |
| Ledger-account hash | `9D577FE50A6D525E3BE443A18FADF336688AB7E5D5C3DF856B2461DC15826C33` | same | Exact match |
| Protected-account hash | `A3D753604F8B6AA52B85A8471D5C0D1FFD8CBF18271B1CA961975D7F77B8E36F` | same | Exact match |

Final metrics remain one supplier, one instrument, one dedicated card account, 33 ledger accounts, zero fundings, zero journals, zero ledger lines, zero outbox rows, zero supplier transactions, and stock `100.00`. Treasury, inventory, supplier, instrument, `1020`, and `1110` hashes all match exactly. Evidence files are `remediation-before.json` and `remediation-after.json` under the controlled temporary T-06C evidence directory.

## 12. Cashbox Workflow Review

The visible official route is `/opening-balance/cashbox`, backed by `POST /api/cashbox/{id}/opening-balance` for Owner/Admin. The endpoint returns `OPENING_BALANCE_AUTHORITY_REQUIRED` for any non-zero amount (`CashboxController.cs:1277`). Direct Treasury cash-in returns HTTP `410`, code `TREASURY_INFLOW_WORKFLOW_REQUIRED` (`TreasuryController.cs:750-761`).

Deposit and transfer operations only move value from an already funded account and cannot establish initial capital from a zero-balance Treasury baseline. Therefore the current Cashbox workflow is not suitable for legally funding AhramStore's supplier card.

## 13. Bank Workflow Review

The `/banks` page posts Bank master data to `POST /api/banks`, but `BanksController.CreateBank` only creates a legacy `Bank` row. It does not create or link a Bank `TreasuryAccount` or ledger account, and it rejects a non-zero opening amount. `POST /api/banks/opening-balances` returns HTTP `410 Gone` with the message that legacy writes are disabled (`BanksController.cs:395-402`).

The frontend route `/opening-balance/banks` redirects to `/treasury/account/1030` (`App.jsx:283`), whose `TreasuryAccountViewer` only reads balances and movements. The existing Primary Bank Treasury account `1030` is active but remains at `0.00` with `PreventNegative=true`. Therefore Bank setup is also not a suitable initial-funding workflow.

## 14. Final Gate and Next Run

The release-blocking HTTP `500` defect is fixed in source and covered end to end. The next zero-balance attempt, after deployment/restart, is expected to fail safely with HTTP `409 INSUFFICIENT_FUNDS` and no database delta. This is a valid negative-path result, not a successful funding scenario.

A complete T-06C rerun must wait for one of these approved prerequisites: an official ledger-backed owner-capital/opening-balance workflow that debits `1010` or `1030` against an authorized equity/liability source, or an independently legitimate baseline whose configured Treasury source already has at least `100.00 EGP`. After that prerequisite is approved and executed through product controls, rerun the full Golden Scenario from a fresh documented baseline and reconcile funding, purchase, supplier payment, replay, overspend, reversal, statements, trial balance, inventory, outbox, and cross-tenant isolation.

No direct SQL funding, manual balance edit, `PreventNegative` bypass, or use of `1020`/`1110` is acceptable.

**FINAL VERDICT: FAIL - CONTRACT REMEDIATION PASS; LEGITIMATE SOURCE-CAPITALIZATION WORKFLOW BLOCKED.**

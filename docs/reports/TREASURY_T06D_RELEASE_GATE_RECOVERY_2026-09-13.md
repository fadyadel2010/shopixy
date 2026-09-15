# Treasury T-06D Release Gate Recovery - 2026-09-13

## 1. Executive Verdict

**NO-GO**

T-06D initial-capitalization authority is internally coherent and its focused backend, frontend, accounting, treasury, reporting, endpoint, and regression tests pass. The Hangfire startup-lock defect found during runtime certification has also been fixed and covered by a regression test. Release certification nevertheless remains blocked because repository-wide .NET and frontend gates are red, the broad integration run could not be certified, and the running API reports unhealthy readiness. No repository release-policy waiver was found for those failures.

## 2. Scope and Decision Rule

This recovery assessed the unapplied `20260913182724_T06DInitialCapitalizationAuthority` migration, T-06D backend and frontend behavior, adjacent T-01 through T-06 regressions, broad repository gates, runtime startup, HTTP availability, and AhramStore non-mutation. The decision rule was strict: `GREEN` required successful builds, no EF model drift, passing required broad and focused tests, healthy runtime readiness, and matching before/after business-state fingerprints. A failure without an explicit repository-policy waiver requires `NO-GO`.

## 3. Safety Constraints

The migration was not applied. No capitalization, funding, purchase, supplier-payment, journal, balance-edit, or direct SQL business write was performed. Database evidence was collected through `REPEATABLE READ / READ ONLY / ROLLBACK` transactions. Existing unrelated worktree changes were preserved. User-supplied credentials were not persisted or printed, and login was not performed because it would rotate authentication/session state without changing the already-conclusive release decision.

## 4. Repository and Build State

The repository was already substantially dirty before this recovery and those changes were treated as pre-existing implementation under review. `dotnet build PointOfSale.sln --configuration Release --no-restore` completed with zero errors; a clean compilation surfaced existing warnings. `npm run build` in `PointOfSale.Web` completed successfully with the existing large-chunk warning. Final `git diff --check` found no whitespace errors and reported only existing LF-to-CRLF conversion notices.

## 5. Migration and Model Drift

`dotnet ef migrations list` identified `20260913182724_T06DInitialCapitalizationAuthority` as the only pending migration. `dotnet ef migrations has-pending-model-changes` reported: `No changes have been made to the model since the last migration.` The migration remained unapplied throughout recovery. The migration-filtered test run was red at 25 passed and 9 failed, including missing certification/source artifacts and intermittent test-schema setup behavior; therefore that broad filter is not a release pass.

## 6. Certified AhramStore Baseline

The certified tenant is AhramStore, tenant ID `c9c5c1ae-949e-498e-bc63-07242daeb49d`, slug `ahramstore`, currency EGP. The baseline contained 33 ledger accounts, 6 Treasury accounts, one supplier, one unfunded supplier-card instrument, four product variants, and aggregate stock quantity `100.00`. It contained zero journals, ledger lines, accounting outbox records, supplier-card fundings, supplier payments, supplier transactions, purchase invoices, and purchase-invoice payments.

## 7. Broad .NET Gate

The latest retained complete .NET run was red at 6,749 passed and 587 failed, 7,336 total. Failures included EF provider/model-cache cascades and independent non-T-06D failures. The category recovery runs improved attribution but do not supersede the failed complete gate. Architecture tests remained red at 3,090 passed and 198 failed, primarily because expected certification/source artifacts are absent or stale. Without a documented waiver, the broad .NET gate blocks release.

## 8. Broad Frontend Gate

The broad frontend run was red at 1,274 passed and 16 failed across 163 files, with 153 files passing and 10 failing. Five broad-run timeout failures passed when isolated. Eleven deterministic unrelated failures remained in i18n completeness, payment/cart fixtures, Storefront mocks, Sidebar storage expectations, and footer error classification. The production bundle succeeds, but a successful bundle does not waive the red test gate.

## 9. .NET Category Isolation

Focused category results were: Unit 98/98, Accounting 103/103, Treasury 84/84, and Reporting 30/30. A bounded tenant bundle produced 53 passed and 1 failed due to a stale literal source-shape assertion. A payments namespace run produced 115 passed and 2 failed because Fawry tests expect an `ApplicationCheckpointServiceMissing` fixture/runtime contract that no longer matches. A purchase bundle produced 27 passed and 1 failed due to a concurrency-interceptor pause timeout. These failures are not evidence of a T-06D behavioral defect, but they remain unwaived release-gate failures.

## 10. Integration and Tenant Breadth

The broad integration batch was manually aborted after approximately seven minutes because it was not converging to a certifiable result. Before cancellation it exposed unrelated stock-transfer concurrency/configuration, social-login, purchase-return projection, and legacy `410` endpoint failures. The broad `FullyQualifiedName~Tenant` filter discovered 544 serial tests and was also aborted; bounded tenant classes were used for attribution instead. Neither aborted run can be recorded as passing.

## 11. T-06D Backend Evidence

The focused T-06D endpoint, reporting, and authority bundle passed 36/36. It covered initial-capitalization authority boundaries, reporting behavior, and API integration expectations. The broader API endpoint integration bundle passed 21/21. No focused backend failure established a defect in the T-06D implementation.

## 12. T-06D Frontend Evidence

The focused T-06D frontend API, page, and architecture bundle passed 8/8. The route `/treasury/initial-capitalization` returned HTTP `200` from the running frontend. T-06D frontend evidence is green in isolation, while the unrelated broad frontend gate remains red.

## 13. T-01 Through T-06 Regression Evidence

The T-01 repair-migration retry test passed 1/1. The T-06 funding/payment bundle passed 18/18. The global exception middleware bundle passed 5/5. The T-06 frontend regression bundle passed 26/26. Together with the accounting, Treasury, and reporting category passes, these results found no focused regression attributable to T-06D.

## 14. Failure Attribution

The red broad gates are mostly attributable to pre-existing or unrelated repository conditions: missing architecture-certification artifacts, stale source-shape assertions, fixture/runtime contract drift, concurrency timeouts, EF test-provider/schema interactions, and deterministic frontend fixture/assertion mismatches. That attribution limits the evidence of a T-06D-specific defect but does not convert failed mandatory gates into passes. The only runtime defect discovered directly during this recovery was the Hangfire startup-lock escape described below.

## 15. Hangfire Startup Root Cause

`HangfireInitializationHostedService.StartAsync` handled failed PostgreSQL connectivity by entering a background retry path, but its immediate `RegisterRecurringJobs()` path was unguarded. When Hangfire could reach PostgreSQL but could not acquire the distributed recurring-job lock within its timeout, the exception escaped `StartAsync`. ASP.NET Core treated that hosted-service exception as fatal and terminated the API before it could bind an HTTP port.

## 16. Hangfire Startup Remediation

`StartAsync` now catches an immediate recurring-job registration failure, logs it, and schedules the existing exponential background retry instead of aborting the host. `CanConnectToPostgres()` and `RegisterRecurringJobs()` were made `protected virtual` solely to permit deterministic hosted-service testing. `ImmediateRegistrationFailure_DoesNotAbortHostStartup` simulates the immediate failure and verifies that startup returns while the retry path is scheduled. The Hangfire recurring-job suite passes 4/4 after the change.

## 17. Runtime and HTTP Evidence

The patched Release API binary bound to `http://127.0.0.1:5011` and logged `Application started`; that isolated process was subsequently removed during API restart. The user-run patched API remained available on port `5010`, where the final `GET /health/live` returned `200`; Swagger UI and OpenAPI JSON also returned `200`. Unauthenticated T-06D GET requests returned `400 TENANT_REQUIRED`, proving that the routes exist and fail closed without tenant context. Authenticated GET probes were not needed for the decision and were skipped to avoid rotating persisted refresh/session state.

Full `GET /health` and `GET /health/ready` returned `503`. The health payload reported `payout-external-truth` unhealthy because the provider was unavailable or not configured, `CommerceHubSearchSync` unhealthy because product-search sync was stale, and `database` degraded because the T-06D migration is pending. Hangfire storage itself was reachable. Liveness is green, but readiness is not.

## 18. Final Non-Mutation Reconciliation

The final post-fix snapshot was captured at `2026-09-13T21:02:48.8970914Z` after API startup and restart activity. It exactly matched the certified baseline: full fingerprint `092722021B2D02D399EC148CE5E17564BC672CA64F7F70FD242135F6DEF72771`, legacy fingerprint `C1239AF22B71A4BBC3DBE073F9F9C4E26644CDEC34641ABA3134121721BB6C3F`, and ledger-account hash `9D577FE50A6D525E3BE443A18FADF336688AB7E5D5C3DF856B2461DC15826C33`. All protected zero-count financial metrics and the `100.00` stock quantity were unchanged. Existing CommerceHub startup processing touched non-AhramStore listings, but no AhramStore fingerprint component changed.

## 19. Final Disposition

The Hangfire startup crash is remediated, T-06D focused evidence is green, EF reports no model drift, the migration remains pending, and AhramStore is unchanged. Release remains **NO-GO** because the complete .NET gate is red, Architecture is 198 failures red, the broad frontend gate has 16 failures, broad integration and tenant runs lack certifiable completions, the migration-filtered suite is red, and API readiness is `503`. A future `GREEN` decision requires resolving or explicitly waiving every mandatory broad-gate failure, obtaining successful integration and tenant breadth results, restoring healthy readiness, rerunning the complete release matrix, and reconciling another read-only AhramStore fingerprint.

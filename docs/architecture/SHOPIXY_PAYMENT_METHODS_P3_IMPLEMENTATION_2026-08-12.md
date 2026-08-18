# SHOPIXY Payment Methods P3 Implementation

Date: 2026-08-12  
Project: SHOPIXY / PointOfSale  
Scope: Payment Methods forensic review, remediation, and internal certification

## 1. Executive Summary

The Payment Methods domain was traced from the backend-owned provider catalog through tenant provisioning, encrypted configuration, validation, enable/disable transitions, Mongo projection, storefront availability, checkout revalidation, and Manual Wallet review.

Six real defects were confirmed and remediated:

1. Development Manual Wallet proof upload always selected an unconfigured R2 implementation and failed with `MANUAL_WALLET_UPLOAD_FAILED`.
2. The Manual Wallet configuration response omitted `walletTypes`, while the storefront required that field to make a selection.
3. The payment-method Mongo read model could return an enabled or valid stale document after SQL had disabled or invalidated the method.
4. Manual Wallet proof metadata and validation were hard-coded instead of being projected from the validated `ManualPaymentOptions` contract.
5. PostgreSQL serialization/deadlock conflicts during Manual Wallet approval could be reported as a generic lifecycle failure even though the state transition remained single-winner.
6. The storefront payment-method response did not explicitly prevent client/intermediary caching of tenant payment availability.

The fixes preserve the intended authority chain:

`Supported Provider Catalog -> TenantPaymentMethod -> Configuration Validator -> Enabled/Disabled -> Storefront Availability -> Checkout Validation`

The supported provider set remains exactly `COD`, `STRIPE`, `PAYMOB`, `FAWRY`, and `MANUALWALLET`. No provider execution integration, shipping architecture, payment finality rule, or database schema was redesigned.

All exact in-scope backend tests pass (72/72), Admin Web tests pass (10/10), storefront payment authority tests pass (44/44), both required .NET builds pass, both frontend builds pass, EF reports no pending model changes, and `git diff --check` passes.

## 2. Baseline

The required baseline was captured before P3 edits.

`git status --short` showed these pre-existing Payment-related changes:

- `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`
- `PointOfSale.API/appsettings.Development.json`
- `PointOfSale.Tests/Admin/PaymentMethodsAdminSafetyTests.cs`
- `PointOfSale.Tests/Architecture/ManualPaymentFoundationCertificationTests.cs`
- `PointOfSale.Web/src/pages/ManualPayments.jsx`
- `PointOfSale.Web/src/pages/ManualPayments.test.ts`
- `PointOfSale.Web/src/pages/PaymentMethods.jsx`
- `PointOfSale.Tests/Admin/AdminManualPaymentPendingTests.cs` (untracked)
- `PointOfSale.Tests/Payments/PaymentMethodConfigurationValidatorTests.cs` (untracked)
- `PointOfSale.Web/src/pages/PaymentMethods.test.tsx` (untracked)
- the P1/P2 Payment Methods reports and the schema-drift forensic report (untracked)

The initial tracked `git diff --stat` was 7 files, 494 insertions, and 25 deletions. All pre-existing changes were preserved. No reset, clean, checkout, restore, stash, commit, or push operation was used.

Governing documents reviewed first:

- `SHOPIXY_PAYMENT_METHODS_FORENSIC_RECOVERY_AUDIT_2026-08-12.md`
- `SHOPIXY_PAYMENT_METHODS_P2_IMPLEMENTATION_2026-08-12.md`
- `SHOPIXY_RUNTIME_DATABASE_SCHEMA_DRIFT_FORENSIC_AUDIT_2026-08-12.md`
- existing Payment authority, read-authority, storefront-selection, Manual Payment, and certification artifacts

The specifically named Step 2O1 remediation document was not present. The available schema-drift forensic audit was used instead.

## 3. Payment Provider Catalog

The backend catalog endpoint is `GET /api/admin/payment-methods/catalog`. It owns the supported provider list and returns exactly:

| Code | Default label | Online | Offline | Requires configuration |
|---|---|---:|---:|---:|
| `COD` | Cash on Delivery | No | Yes | No |
| `STRIPE` | Credit/Debit Card | Yes | No | Yes |
| `PAYMOB` | Online Payment | Yes | No | Yes |
| `FAWRY` | Fawry Pay | Yes | No | Yes |
| `MANUALWALLET` | Manual Wallet (Vodafone Cash/Instapay) | No | Yes | Yes |

The catalog also owns descriptions, ordering, icon identifiers, supported status, and configuration requirement metadata. The Admin Web merges this catalog with tenant rows. Its provider-specific switch only defines editable form fields; it cannot add a provider or make an unsupported code authoritative.

No second authoritative provider registry was found in the frontend. Provider execution options exist downstream by design and are not a competing tenant catalog.

## 4. Tenant Provisioning

A tenant with zero `TenantPaymentMethods` rows still receives the full provider catalog and can select Configure. Catalog reads do not create rows.

Verified endpoint behavior:

- `PUT /api/admin/payment-methods/{code}` creates or updates a tenant-owned row.
- `PUT /api/admin/payment-methods/{code}/configure` creates a tenant-owned row when absent, persists configuration encrypted, and does not enable the row.
- `POST /api/admin/payment-methods/{code}/enable` validates configuration first. COD may be created and enabled without configuration; other providers cannot.
- `POST /api/admin/payment-methods/{code}/disable` is idempotent and tenant scoped.
- reset/delete behavior is tenant scoped and does not address another tenant's row.

Tenant identity is derived from `ITenantContext.RequiredTenantId`; it is not accepted from route or body input. Queries always include the current tenant. The unique database constraint on `(TenantId, Code)` is the final duplicate-row guard, and mutations publish through the existing EF/outbox transaction boundary.

## 5. Provider Configuration

The shared `PaymentMethodConfigurationValidator` is the canonical validation contract.

| Provider | Required normalized fields | Alias behavior | Storefront-public configuration |
|---|---|---|---|
| COD | none | none | none |
| Stripe | `publishableKey`, `secretKey`, `webhookSecret` | `apiKey` -> `secretKey` | none |
| Paymob | `apiKey`, `integrationId`, `iframeId`, `hmacSecret` | none | none |
| Fawry | `merchantCode`, `apiKey`, `webhookSecret` | `securityKey` -> `apiKey` | none |
| Manual Wallet | `walletNumber` | none | `walletNumber` only |

The Admin Web exposes the same required fields consumed by runtime integration code. Blank secret fields are treated as “preserve the existing secret,” preventing masked/empty edits from erasing credentials. Configuration is protected with ASP.NET Core Data Protection purpose `PaymentMethodSecrets` before SQL persistence.

Admin read DTOs expose validation state and errors but never return decrypted configuration or plaintext credentials. Only the deliberately public Manual Wallet destination is projected to storefront consumers.

Configure does not imply enable. Reconfiguration that makes an enabled method invalid disables it, and re-enable revalidates the current decrypted configuration.

## 6. ManualPaymentOptions

The development configuration passes `ManualPaymentOptionsValidator` and is coherent with the review workflow:

- provider and feature flags enabled in Sandbox
- proof upload, review, approval, and rejection enabled
- expiration and refund execution deliberately disabled
- customer proof and reference number required
- accepted proof types: JPEG, PNG, and PDF
- maximum upload: 5 MiB
- pending payment TTL: 2 days
- proof access TTL: 7 days
- enabled channel: Vodafone Cash
- runtime bridge, evidence adapter, persistence, provider adapter, and review workflow capabilities certified

The capabilities describe what the implementation supports; enabled channels describe what this environment currently offers. This is not contradictory. No merchant wallet number was invented in application configuration; that value remains tenant-owned encrypted payment-method configuration.

## 7. Manual Wallet Configuration

The in-scope chain is now coherent:

1. Admin configures tenant `walletNumber`.
2. The row remains disabled until explicitly enabled.
3. Enablement requires valid `walletNumber`.
4. Storefront availability requires current SQL enabled/nondeleted/valid state.
5. Manual Wallet configuration returns the public wallet number plus enabled supported wallet types and the validated proof policy.
6. The browser submits the customer-entered source wallet number, proof, and a server-issued upload token.
7. The server derives tenant/session identity, validates the token and file, stores proof privately, and binds it to the checkout session/payment intent.
8. Payment intent enters the existing awaiting-verification/review workflow; only admin approval/rejection executes the existing downstream lifecycle.

The browser is not authoritative for tenant, destination wallet, amount, currency, payment intent, provider credentials, or payment finality.

## 8. Manual Wallet Proof Upload Findings

The observed endpoint was traced independently:

`POST /api/storefront/payments/manual-wallet/upload-proof`

It calls `StorefrontManualWalletController`, validates the upload token/session/file, then performs a server-mediated `IR2StorageService.UploadFileAsync` call. It does not request a presigned attachment lease.

Root cause of the observed 500: development always registered `R2StorageService`, but local R2 account, bucket, access key, and secret were absent. The R2 client therefore failed during the direct proof upload and the controller correctly mapped the exception to `MANUAL_WALLET_UPLOAD_FAILED`.

Remediation: when and only when the host environment is Development and R2 is not configured, dependency injection selects a private filesystem implementation of `IR2StorageService`. Production and any configured development environment continue to use R2. The fallback:

- supports the server-mediated upload/read/metadata/delete/copy operations used by Manual Wallet proof handling
- creates opaque GUID filenames
- confines objects beneath `secure-uploads/development-object-storage`
- rejects traversal and unsupported content types
- computes SHA-256 ETags and enforces conditional reads/copies
- deliberately does not issue presigned URLs

Regression evidence:

- `DevelopmentFileStorageServiceTests` proves private round-trip, deletion, ETag metadata, no presigned lease, and traversal rejection.
- `StorefrontManualWalletStorageTests` proves the upload controller calls storage only after token/session/file checks and returns the expected stored proof outcome.
- The API starts successfully in Development without R2 credentials and resolves the Manual Wallet storefront contract.

## 9. Checkout Attachment Dependency

The observed generic endpoint was traced separately:

`POST /api/storefront/checkout/attachments/upload-lease`

Its call chain is `CheckoutController -> CheckoutAttachmentService -> IR2StorageService.CreateUploadUrlAsync`. It requires a presigned upload facility.

Manual Wallet proof upload does not invoke this controller, service method, route, or lease result. The two requests share the storage abstraction but are otherwise independent. Missing R2 explains why both could fail in the same local environment, but one is not a dependency of the other.

The development storage fallback deliberately leaves presigned leases unavailable, so the generic endpoint remains fail-closed with service unavailable when R2 is absent. Replacing the generic attachment contract with local emulation would be an unrelated checkout-infrastructure change and was not performed.

## 10. Analytics 403 Assessment

`POST /api/storefront/analytics/events` is a telemetry ingestion request. The route is separate from payment-method selection, checkout payment validation, proof storage, and lifecycle execution. Storefront callers treat analytics failure as non-fatal; no payment state transition depends on its result.

The exact environment-specific reason for the observed 403 was not needed to establish domain independence and was not modified. It does not block Payment Methods certification.

## 11. Storefront Availability

The actual path is `GET /api/storefront/payment-methods`.

SQL `TenantPaymentMethods` is now consulted before any Mongo document is admitted. A method is eligible only when it is:

- owned by the resolved tenant
- present in SQL
- nondeleted
- enabled
- valid under the shared configuration validator

Mongo remains a performance/read projection. It is reused only when tenant, payment-method identity, provider code, version, enabled/deleted state, validation state, and public configuration match the SQL-derived authoritative document. A missing, stale, or unavailable projection falls back to SQL.

This closes both failure modes:

- SQL disabled + stale Mongo enabled -> hidden
- SQL invalid/updated + stale Mongo valid/public config -> SQL result used

The endpoint now emits `Cache-Control: no-store, no-cache, max-age=0`, `Pragma: no-cache`, and `Expires: 0`, preventing an otherwise correct disabled state from being obscured by a cached storefront response.

Checkout still revalidates the selected method against SQL under its existing transaction/lock boundary. A browser-provided method code alone is never sufficient.

## 12. State Machine

Verified state model:

`NOT CONFIGURED -> CONFIGURED/DISABLED -> ENABLED + VALID -> STOREFRONT AVAILABLE`

Transitions and failure states:

- Fresh catalog entry has no tenant row and is not checkout-selectable.
- Configure creates/updates a disabled row.
- Enable succeeds for COD or a provider with valid current configuration.
- Invalid required configuration yields `INVALID_PAYMENT_CONFIGURATION` and cannot become available.
- Reconfiguration that invalidates an enabled provider disables it.
- Disable immediately removes SQL eligibility; stale Mongo cannot override it.
- Unsupported codes are rejected at the admin boundary and are not projected.
- Provider runtime availability/execution remains a downstream concern and is not inferred by the browser.

No path was found that allows not-configured, disabled, invalid, deleted, unsupported, or cross-tenant methods to pass checkout validation.

## 13. Security/Tenancy

Security findings and evidence:

- Admin authorization requires Owner, Admin, or Manager; higher-risk sync/reset operations retain their stricter role rules.
- Tenant ID comes from authenticated tenant context, never from a payment-method request payload.
- Every payment-method read/mutation query is tenant scoped.
- The database unique key includes tenant ID.
- Stored provider configuration is Data Protection encrypted.
- Admin DTOs contain state and validation errors, not decrypted secrets.
- Logs use provider code, entity ID, tenant ID, trace ID, and safe validation reasons; no credential values are logged.
- Only `walletNumber`, an intentional customer-facing destination, is included in public configuration.
- Manual proof access is bound to tenant, checkout session, customer/session authority, recovery nonce, and proof token.
- Approval/rejection retains tenant-scoped intent/order lookup and existing payment finality controls.

Behavioral and architecture tests cover tenant-isolated reads and writes, no plaintext response leakage, public-configuration filtering, proof authorization, and single-winner approval.

No confirmed cross-tenant access or secret disclosure remained.

## 14. Database/EF

Payment-related persistence and mappings were inspected, including:

- `TenantPaymentMethods`
- `PaymentIntents`
- `PaymentWebhookLogs`
- `PaymentSettlementBatches`
- `PaymentSettlementOrders`
- payment replay/idempotency records

The confirmed defects were authority, contract, storage composition, and error-classification defects; none required schema change.

Command:

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API/PointOfSale.API.csproj --no-build`

Result: `No changes have been made to the model since the last migration.`

No migration was created.

## 15. Defects Found

### P3-01 — Manual Wallet proof upload had no usable development storage

- Root cause: unconditional R2 storage registration with empty development R2 configuration.
- Exact source: `PointOfSale.API/Program.cs`, `PointOfSale.API/Services/R2StorageService.cs`, and `StorefrontManualWalletController.UploadProof`.
- Why: the proof route is server-mediated but was coupled at composition time to external object storage.
- Impact: deterministic 500 `MANUAL_WALLET_UPLOAD_FAILED` in the observed local environment.

### P3-02 — Manual Wallet wallet-type response contract was incomplete

- Root cause: backend configuration DTO had `WalletTypes`, but the controller did not populate it.
- Exact source: `PointOfSale.API/Controllers/StorefrontManualWalletController.cs` and storefront checkout selection code.
- Why: the P2 display path surfaced Manual Wallet but did not complete its provider-specific selection contract.
- Impact: a customer could see Manual Wallet but have no supported wallet type to submit.

### P3-03 — Mongo projection could override newer SQL availability

- Root cause: repository reads trusted any enabled Mongo document and used SQL only after Mongo failure/miss.
- Exact source: `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs`.
- Why: fallback order treated the derivative read model as availability authority.
- Impact: disabled or invalid methods and stale public wallet configuration could remain visible.

### P3-04 — Proof policy was duplicated and hard-coded

- Root cause: response and upload validation used fixed JPEG/PNG/PDF and 5 MiB values instead of validated tenant runtime options.
- Exact source: `PointOfSale.API/Controllers/StorefrontManualWalletController.cs` and `PointOfSale.API/Services/FileValidationService.cs`.
- Why: configuration existed but was not bridged into the storefront contract.
- Impact: configured policy and actual accepted upload behavior could drift.

### P3-05 — Database concurrency conflicts lost their safe error identity

- Root cause: lifecycle catch handling returned generic `LIFECYCLE_ERROR` for PostgreSQL serialization/deadlock errors; the admin controller also discarded returned failure codes.
- Exact source: `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` and `PointOfSale.API/Controllers/AdminManualPaymentController.cs`.
- Why: EF optimistic conflicts were recognized, but provider-specific database transaction conflicts were not.
- Impact: the state remained single-winner, but clients could not distinguish a safe retry/conflict from an internal failure.

### P3-06 — Storefront availability response was cacheable

- Root cause: the storefront payment-method endpoint lacked explicit no-store headers.
- Exact source: `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs`.
- Why: backend authority checks were correct, but response caching policy was incomplete.
- Impact: a disabled method could remain visible until a client/intermediary cache expired.

No P0 defect was found. These were repaired without changing the provider execution model.

## 16. Fixes Implemented

- Added `DevelopmentFileStorageService` and conditional Development-only selection when R2 is unconfigured.
- Preserved R2 as the production/configured implementation and kept generic presigned leases fail-closed.
- Populated Manual Wallet types from enabled, supported `ManualPaymentOptions.Channels`.
- Projected configured proof MIME types, extensions, and maximum size into the storefront response.
- Converted configured MIME types to the existing file-signature validation enum and applied that policy during upload.
- Reconciled Mongo payment-method documents against current SQL rows, version, validity, state, and public configuration before use.
- Added SQL-first behavior to `GetEnabledPaymentMethods`, `GetById`, and `GetByCode`.
- Added no-store headers to storefront payment-method reads.
- Classified EF and PostgreSQL serialization/deadlock conflicts as `PAYMENT_CONCURRENCY_CONFLICT` and preserved that code at the admin API boundary.
- Updated stale source-shape tests to assert the current application-service/authority chain rather than obsolete implementation text.

## 17. Tests

Exact backend payment-method/manual-wallet suite:

- 72 passed, 0 failed
- Includes admin safety/sync, fresh catalog, configure/enable/disable, validator coverage, tenant authority, storefront availability, stale Mongo rejection, ManualPaymentOptions, proof storage, cleanup, pending admin presentation, architecture contracts, and real PostgreSQL concurrency integration.

Admin Web:

- `PaymentMethods.test.tsx`
- `ManualPayments.test.ts`
- 2 files passed, 10 tests passed

Storefront:

- `commerceAuthority.certification.test.ts`
- 1 file passed, 44 tests passed

A broad name filter containing `Payment` was also evaluated but was not used as a gate because it selects unrelated shipping, inventory, vendor-accounting, and older payment-finality recovery suites. Directly relevant failures exposed by that run were isolated, proven stale or real, corrected, and included in the exact 72-test suite. No in-scope red test remains.

Tests were not weakened to obtain green status. Stale source-string assertions were replaced with assertions of the current behavioral authority and service chain.

## 18. Builds

Required gates:

- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -v:minimal` — PASS, 0 warnings, 0 errors
- `dotnet build PointOfSale.sln --no-restore -v:minimal` — PASS, 0 warnings, 0 errors
- Storefront `npm run build` — PASS
- Admin Web `npm run build` — PASS
- EF pending model check — PASS, no pending changes
- `git diff --check` — PASS

The storefront build emitted existing advisory chunk-size/dynamic-import/browser-data warnings; it produced a successful bundle and no payment build failure.

## 19. Live Verification

The API was started in Development with ephemeral process-only signing keys. No key or provider secret was printed or persisted. A tenant-scoped test-auth token was used for read-only probes against tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.

Observed live results:

- Catalog: `COD, STRIPE, PAYMOB, FAWRY, MANUALWALLET`
- Existing admin rows: 3
- Admin states: `COD=True/valid`, `PAYMOB=True/valid`, `MANUALWALLET=True/valid`
- Storefront status: `AVAILABLE`
- Storefront codes: `COD, PAYMOB, MANUALWALLET`
- Manual Wallet destination present: yes
- Manual Wallet types: `VODAFONECASH`
- Manual Wallet configuration error: none

The API was stopped after the probes and the ephemeral environment variables were removed.

The health aggregate was not green because it includes unrelated/external readiness checks such as provider credentials, R2, DNS, and projection health. Endpoint readiness was therefore established with a successful authenticated API call, not by suppressing those checks.

A real HTTP proof POST was not fabricated because the endpoint correctly requires a genuine active checkout session and its dedicated signed upload token. Creating an order solely to obtain that token would mutate business data. The complete internal path is covered by controller/storage tests, option validation, session-authority certification, and the PostgreSQL approval concurrency integration test. This is a live-verification limitation, not evidence of a remaining defect.

Admin configure/enable mutations and approve/reject mutations were likewise not repeated against existing business data. The tenant already had valid enabled COD and Manual Wallet rows, which allowed read-only verification of the resulting admin/storefront state.

## 20. Remaining External Verification

The following are intentionally not certified as live successes:

- Stripe credential validation, payment, callback/webhook, capture, and refund against Stripe
- Paymob credential validation, payment, callback/HMAC, and refund against Paymob
- Fawry credential validation, payment/reference, webhook, and refund against Fawry
- production R2 upload/download behavior with real bucket credentials
- a browser-created Manual Wallet order using a genuine checkout-session upload token and subsequent human approve/reject action
- the unrelated generic checkout attachment presigned-lease endpoint without configured object storage
- the telemetry-only analytics 403 environment diagnosis

These require real external credentials, a genuine checkout session, or broader non-Payment-Methods infrastructure. No external-provider success is claimed.

## 21. Worktree Safety

All baseline changes were preserved. P3 edits were additive or narrowly applied to Payment Methods and directly dependent Manual Wallet behavior. Shipping Methods and its closed architecture were not changed.

Final safety checks:

- `git status --short` captured
- `git diff --stat` captured
- `git diff --check` exit code 0
- no whitespace errors; Git reported only informational LF-to-CRLF working-copy notices
- no reset, clean, checkout, restore, stash, commit, or push

Because several governing reports and P2 test files were already untracked, tracked `git diff --stat` does not count them. `git status --short` remains the authoritative inventory of tracked and untracked worktree changes.

## 22. Final Certification

**PASS WITH EXTERNAL VERIFICATION BLOCKED**

The Payment Methods domain is internally coherent and production-ready at its defined boundary. All confirmed in-scope defects were remediated, tenant and secret authority remain server-side, enabled/disabled storefront behavior is SQL-authoritative, Manual Wallet development proof storage is functional and fail-closed, exact relevant tests and builds are green, and EF has no pending model changes.

No P0/P1 Payment Methods defect remains. Real external provider/R2 credentials and a genuine customer checkout session are still required for final live external/browser execution verification.

# Shopixy Dynamic Checkout Forensic Architecture Audit

**Audit date:** 2026-08-10  
**Scope:** Dynamic checkout field definitions, storefront rendering and validation, checkout submission, uploads, order snapshots, admin presentation, migrations, and focused tests.  
**Verdict:** **Not production-ready yet.** The core authority and snapshot design is sound and the confirmed local defects found in this pass were fixed. Three attachment-lifecycle gaps remain release blockers because they require an explicit product/storage contract, not an isolated patch.

## 1. Method and Evidence

This audit treated existing implementation notes as unverified claims. The conclusions below came from:

- source inventory across Core, Infrastructure, API, Storefront, POS Admin, migrations, and tests;
- controller-to-service-to-persistence tracing for every Dynamic Checkout endpoint;
- checkout transaction, conditional-rule, idempotency, upload, cleanup, and order-read tracing;
- focused backend and frontend regression execution;
- solution and both frontend production builds;
- EF Core model/migration consistency inspection;
- adversarial review for tenant isolation, stale definitions, races, unbounded input, JSON persistence, object integrity, and authorization.

The repository was already substantially dirty. This audit preserved unrelated domain, SSL, commerce, and user changes.

## 2. Reconstructed Architecture

### 2.1 Persistent model

| Aggregate | Purpose | Important authority/integrity behavior |
| --- | --- | --- |
| `CheckoutFieldDefinition` | Tenant-owned mutable checkout schema | Tenant query filter and explicit tenant predicates; system/custom kind; field type; order; enabled/required flags; JSON configuration/options/rules; audit actor/timestamps |
| `CheckoutAttachment` | Upload lease and staged object lifecycle | Tenant/cart/customer-or-session/field binding; expected and observed metadata; status machine; expiry; nullable order binding |
| `OrderCheckoutFieldSnapshot` | Order-time field definition and value snapshot | Tenant/order binding; copied label/type/config/value; deliberately no FK to the mutable definition |
| `Order` | Checkout result authority | Owns snapshots and attached upload references after the final transaction |

The implementation is additive through these migrations:

1. `20260809185514_SHOPIXY_CheckoutFieldDefinitions`
2. `20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots`
3. `20260809203807_SHOPIXY_CheckoutAttachments`

### 2.2 Admin authoring path

`/storefront/checkout-fields` in the POS admin calls `/api/admin/storefront/checkout-fields`. The controller requires `Owner,Admin`, resolves the authoritative tenant, and delegates to `CheckoutFieldDefinitionService`.

The API supports:

- `GET /api/admin/storefront/checkout-fields`
- `POST /api/admin/storefront/checkout-fields/system-fields/initialize`
- `POST /api/admin/storefront/checkout-fields`
- `PATCH /api/admin/storefront/checkout-fields/{fieldId}`
- `DELETE /api/admin/storefront/checkout-fields/{fieldId}`
- `PUT /api/admin/storefront/checkout-fields/order`

Ten canonical system fields are initialized explicitly. Custom fields support text, textarea, email, phone, number, date, select, radio, checkbox, and file-upload behavior plus conditional field-to-field rules.

### 2.3 Storefront schema and rendering path

The anonymous storefront resolves the tenant through the storefront authority pipeline, then calls `GET /api/storefront/checkout-fields`. Only enabled definitions are returned.

`ShippingStep` renders canonical address inputs for enabled system definitions and renders custom controls from the returned schema. The browser rule engine computes visibility from current values. The checkout store performs immediate client validation, but this is only UX assistance; it is not the acceptance authority.

### 2.4 Attachment path

The storefront uses:

1. `POST /api/storefront/checkout/attachments/upload-lease`
2. direct presigned `PUT` to R2-compatible storage;
3. `POST /api/storefront/checkout/attachments/confirm`;
4. optional `DELETE /api/storefront/checkout/attachments/{attachmentId}`.

The service binds an attachment to tenant, cart, field, and authenticated-customer or guest-session ownership. Confirmation checks configured type/size policy, object metadata, and file signatures for supported formats. Checkout revalidates ownership, status, expiry, policy, and object metadata before binding the attachment to the order.

### 2.5 Submission and transaction path

The checkout request carries canonical system data, a custom scalar-value map, and attachment IDs. The effective flow is:

1. resolve tenant and caller/cart ownership context;
2. load the cart and canonical checkout inputs;
3. admit the current checkout-field schema and perform early validation;
4. include canonical custom values in the idempotency fingerprint;
5. enter the existing checkout execution/transaction pipeline;
6. inside the final serializable order transaction, reload definitions and validate rules/values again;
7. validate each attachment against the current definition and storage object;
8. create the order, bind valid attachments, and write order snapshots;
9. commit once, then continue existing post-commit checkout behavior.

The second validation inside the final transaction is important: an admin edit between page load and checkout cannot silently admit a value under obsolete rules.

### 2.6 Order read and presentation path

`AdminOrderService` includes checkout snapshots in the admin order DTO. The POS order drawer now converts those snapshots into display-safe values. The storefront review step now also shows currently visible custom values and uploaded filenames.

Both presentation paths deliberately omit storage object keys. The underlying admin DTO still exposes raw snapshot JSON; that residual API-contract issue is listed below.

## 3. Architecture Assessment

### Verified strengths

- The server is the final validation authority; browser validation is not trusted.
- Tenant query filters are reinforced by explicit tenant predicates in security-sensitive queries.
- Admin mutation endpoints require `Owner` or `Admin`.
- Anonymous schema reads rely on resolved storefront tenant authority rather than request-body tenant identity.
- Definition lookup failures fail closed instead of accepting unvalidated values.
- Definitions are reloaded and revalidated in the order transaction.
- Conditional-rule evaluation is implemented on both browser and server, with focused parity tests.
- Idempotency includes custom values instead of treating materially different checkouts as identical.
- Order snapshots are not FK-coupled to mutable field definitions.
- Uploads are tied to tenant, cart, owner, session/customer, field, expiry, status, MIME, size, and object signature.
- Cleanup is scheduled and staged uploads have an expiry lifecycle.

### Important divergence from a production-complete design

- A confirmed object can still be overwritten through its unexpired presigned upload URL. The order snapshot does not bind an immutable object version, ETag, or digest.
- Merchants can see upload metadata but there is no authorized order-attachment download flow.
- Upload-lease admission accepts an arbitrary non-empty cart ID without proving that the cart exists and belongs to the caller.
- “Immutable snapshot” is a convention, not enforced by the DbContext or database.
- Raw `ValueJson` and `ConfigurationJson` are exposed to the admin client, coupling consumers to persistence shape and revealing internal object keys.

## 4. Confirmed Defects Fixed

### F-01 Critical: cleanup could delete an object used by a committing checkout

**Failure:** The cleanup job deleted storage first and updated SQL afterward. A checkout could validate/attach the expired row between those operations, leaving an order pointing to a deleted object.

**Fix:** Cleanup now atomically claims eligible relational rows by moving them to `Deleting` with a predicate that still requires no `OrderId`. Only the claimant deletes storage. Final expiry is conditional on the row remaining unbound and in `Deleting`.

**Files:** `CheckoutAttachmentCleanupJob.cs`, `CheckoutAttachmentServiceTests.cs`  
**Regression:** `CleanupJob_ClaimsAttachmentBeforeDeletingStorageObject`

### F-02 High: terminal confirmation failures remained retryable

**Failure:** A mismatched content type or a newly reduced size policy could throw before persisting `Failed`. The row remained in a retryable state.

**Fix:** Content-type mismatch is evaluated in the correct order and configured type/size validation failures persist `Failed` before returning the validation error.

**Files:** `CheckoutAttachmentService.cs`, `CheckoutAttachmentServiceTests.cs`  
**Regression:** `ConfirmUpload_ContentTypeMismatch_IsPersistedAsTerminalFailure`

### F-03 High: NUL characters could break PostgreSQL JSON/text persistence

**Failure:** Definition labels/help/rule values and checkout text admitted `\u0000`. PostgreSQL rejects NUL in text/JSONB, turning a client validation problem into a persistence failure. Conditional comparison values were also unbounded.

**Fix:** Admin text normalization rejects NUL and bounds comparison values to 1,000 characters. Checkout scalar validation rejects NUL before snapshot serialization.

**Files:** `CheckoutFieldDefinitionService.cs`, `CheckoutService.cs`, corresponding service/contract tests.

### F-04 High: server accepted custom text beyond the field-type invariant

**Failure:** With no configured maximum, custom text/email/phone values could use the DTO-wide 5,000-character ceiling even though the authoring contract caps these field types at 1,000.

**Fix:** Server validation now always applies absolute per-type ceilings and clamps configured maxima to those ceilings. Textarea retains the 5,000-character limit.

**Files:** `CheckoutService.cs`, `DynamicAddressContractTests.cs`

### F-05 Medium: browser phone validation disagreed with server authority

**Failure:** The storefront counted total characters for phone length while the server counted digits. It could reject valid formatted numbers or accept punctuation-heavy strings.

**Fix:** Storefront phone validation now verifies allowed characters and counts digits. Checkout phone uses 7-20 digits; manual-wallet phone uses its 8-20 policy. Browser custom-field bounds now also enforce absolute server ceilings.

**Files:** `checkoutStore.ts`, `commerceAuthority.certification.test.ts`

### F-06 Medium: clearing help text was impossible

**Failure:** The admin UI sent `null` when help text was cleared, while the PATCH service treats `null` as “not supplied.” Existing text survived the update.

**Fix:** The UI sends an empty string, which the service intentionally normalizes to stored `null`.

**Files:** `CheckoutFieldsPage.jsx`, admin service and UI tests.

### F-07 Medium: attachment removal leaked an inconsistent 500 contract

**Failure:** Lease and confirmation converted unexpected storage/service failures to the platform 503 error contract; removal did not.

**Fix:** Removal now logs unexpected failures and returns the same traceable 503 response contract.

**Files:** `CheckoutController.cs`, `CheckoutAttachmentControllerContractTests.cs`

### F-08 Medium: collected custom values disappeared from review and order UX

**Failure:** Values and filenames were persisted but omitted from storefront review and the POS order drawer.

**Fix:** Added rule-aware storefront review projection and sanitized admin snapshot presentation. Select/radio values use option labels, checkboxes use readable boolean text, uploads show filenames and metadata, conditionally hidden snapshots stay hidden, and object keys are not rendered.

**Files:** `checkoutFieldRules.ts`, `ReviewStep.tsx`, `orderCheckoutFieldPresentation.js`, `Orders.jsx`, and focused tests.

## 5. Residual Findings

### P0 release blockers

| ID | Finding | Risk | Required direction |
| --- | --- | --- | --- |
| P0-1 | Confirmed uploads remain writable until the presigned PUT expires | An object can change after confirmation or even after order commit, invalidating the file that was inspected | Confirm into immutable, order-scoped storage or bind a version/checksum/ETag and enforce it on every read |
| P0-2 | No authorized merchant download endpoint | File-upload fields are operationally incomplete; direct object access would bypass order/tenant authorization | Add tenant/order/attachment authorized short-lived GET, audit access, and never expose raw object keys |
| P0-3 | Lease/confirm do not prove the referenced cart exists and belongs to the caller | Anonymous callers can mint orphan DB/storage work against arbitrary GUIDs; rate limiting only reduces volume | Reuse canonical cart authority before leasing and confirming; reject stale, foreign, and nonexistent carts |

### P1 hardening

| ID | Finding | Consequence |
| --- | --- | --- |
| P1-1 | Snapshot immutability is not enforced | Future code can mutate historical checkout evidence |
| P1-2 | Definitions have no optimistic concurrency token/revision | Concurrent admins can silently overwrite configuration or ordering |
| P1-3 | Field counts and request collection counts lack explicit tenant/API quotas | Memory, query, and UI cost can grow without a contractual ceiling |
| P1-4 | Database constraints do not fully encode size/status/expiry/schema invariants | Invalid states can be introduced outside current services |
| P1-5 | Definition changes lack an immutable audit-diff trail | Incident reconstruction cannot show the exact before/after policy |
| P1-6 | Cleanup catches top-level failures without surfacing job failure/metrics | Hangfire can report success while expired objects remain |
| P1-7 | Schema responses/submissions have no revision or ETag | Stale-schema rejection is correct but produces avoidable checkout friction |
| P1-8 | Admin order API exposes raw snapshot JSON | Persistence shape becomes a public contract and internal object keys reach the browser |
| P1-9 | Any authenticated admin-order reader receives every custom value | Sensitive fields need explicit role/privacy classification and redaction policy |
| P1-10 | Files are signature-checked but not malware-scanned | A merchant download path would create a malware delivery surface |

### P2 product, UX, and operations gaps

- No end-to-end browser test covers author field -> storefront render -> conditional behavior -> upload -> checkout -> merchant order view.
- No real PostgreSQL/R2 integration test covers final-transaction rollback, cleanup claiming, or post-confirm object mutation.
- Dynamic Checkout has no dedicated metrics for schema fetch failures, validation codes, upload states, orphan volume, cleanup latency, or download access.
- Custom values and attached order files have no data classification, retention, export, or redaction lifecycle.
- Builder/storefront dynamic text is not fully localized despite English/Arabic application support.
- Upload UX has no progress/cancellation and removal suppresses failure after optimistic UI removal.
- Conditional rules only consume other checkout fields; cart, shipping, payment, and geography conditions are absent.
- Billing fields remain outside the dynamic definition model.
- System-field initialization is page-triggered rather than a versioned rollout/backfill process.
- Duplicate display-order values are possible; reads are deterministic but the database does not enforce a unique order.
- The builder can disable every fulfillment identity/address field. Whether this is valid must be decided per shipping/payment policy and then enforced centrally.
- Storefront/admin bundles remain large; build output reports existing chunking warnings.

## 6. API and Data Contract Assessment

The create/update DTO validation plus service normalization is generally defensive. The checkout DTO separates canonical address properties from custom values and attachment IDs, which keeps existing downstream address behavior stable.

Remaining contract work:

1. Return a typed, sanitized order checkout-field DTO instead of raw JSON strings.
2. Add a schema `revision` and require/echo it during checkout so stale configuration has a typed response.
3. Bound custom-value and attachment-ID collection counts at model binding and service boundaries.
4. Publish stable error codes for stale schema, invalid conditional state, attachment version mismatch, scan state, and download authorization.
5. Define archival semantics explicitly: archived definitions stay in historical snapshots and cannot be reactivated accidentally.

## 7. Migration Assessment

- The three Shopixy migrations are additive and ordered correctly: definitions, snapshots, then attachments.
- Generated SQL contains table/index/check-constraint creation for the new aggregates.
- `dotnet ef migrations has-pending-model-changes` reports no model drift.
- The migration chain contains all three Shopixy migrations in the expected order. Applied/pending database state could not be certified because the configured PostgreSQL host (`192.168.1.5:5432`) was unreachable during final verification.
- No destructive backfill is required for existing orders because snapshots are created only for new Dynamic Checkout submissions.

Before production rollout, add a follow-up migration for the P1 database invariants and test both clean install and upgrade from the pre-Shopixy schema against PostgreSQL.

## 8. Verification Record

| Verification | Result |
| --- | --- |
| Dynamic Checkout backend focus (`7` test classes) | **65 passed, 0 failed** |
| Storefront authority/rule focus | **28 passed, 0 failed** during hardening pass |
| Latest storefront rule/review projection suite | **6 passed, 0 failed** |
| POS admin checkout-field and order-presentation focus | **18 passed, 0 failed** |
| `dotnet build PointOfSale.sln --no-restore` | **Passed**, 0 errors; non-incremental compilation emitted existing repository warnings |
| Storefront `npm run build` | **Passed**; existing chunk/dynamic-import warnings |
| POS admin `npm run build` | **Passed** |
| EF pending model check | **No model changes since last migration** |
| EF applied-migration state | **Not verified**: configured PostgreSQL host was unreachable |
| Full unfiltered backend test run | **Inconclusive**: exceeded the 124-second command budget without a final result; focused suite was rerun and passed |

## 9. Prioritized Roadmap

### Phase 0: production gate

1. Design and implement immutable attachment finalization, including checksum/version persistence and adversarial overwrite tests.
2. Add cart-authoritative lease/confirm admission with guest-session and customer ownership integration tests.
3. Add the tenant/order-authorized attachment download API, audit event, sanitized DTO, and malware quarantine/scan state.
4. Run real PostgreSQL and R2-compatible integration tests for upload, confirm, checkout commit/rollback, cleanup race, and download.

### Phase 1: integrity and concurrency

1. Add checkout definition revision/concurrency tokens and stale-schema API semantics.
2. Enforce snapshot append-only behavior in `ApplicationDbContext` and, where practical, PostgreSQL permissions or triggers.
3. Add bounded field/value/attachment quotas and database state constraints.
4. Add immutable definition audit records and operational telemetry/alerts.
5. Replace raw order snapshot JSON with typed, redacted DTOs and explicit sensitive-field authorization.

### Phase 2: completeness

1. Add Playwright coverage across desktop/mobile for the complete author-to-order workflow.
2. Complete English/Arabic localization and accessible validation/error behavior.
3. Add upload progress/cancellation/retry and surfaced removal errors.
4. Define retention/export/redaction policies and automate order-attachment disposal.
5. Decide and implement billing-field, contextual-condition, and minimum-fulfillment-field policy.
6. Introduce versioned system-field initialization/backfill and a deployment/runbook document.

## 10. Release Decision

Dynamic Checkout has a credible transactional core and is materially safer after this audit. It should remain behind a non-production feature flag until P0-1 through P0-3 are implemented and exercised against real PostgreSQL and object storage. Passing unit tests and builds do not compensate for mutable order evidence or the missing authorized attachment lifecycle.

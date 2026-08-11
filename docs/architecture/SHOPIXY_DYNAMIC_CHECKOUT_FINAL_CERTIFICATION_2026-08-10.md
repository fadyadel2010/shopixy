# SHOPIXY DYNAMIC CHECKOUT
# FINAL CERTIFICATION & REMAINING-GAPS AUDIT

## 1. Executive Summary

Final verdict:

BLOCKED
IMPLEMENTATION COMPLETE BUT REQUIRED EXTERNAL VERIFICATION REMAINS

Dynamic Checkout is architecturally complete across Phase 1 through Phase 5 in the current repository implementation. The source implements tenant-owned checkout field definitions, admin CRUD, storefront runtime projection, server-authoritative checkout submission, historical order snapshots, checkout attachment leases/finalization/order binding, merchant download, and equivalent browser/server conditional rule semantics.

One real Dynamic Checkout defect was found during this final pass: finalized but unattached upload objects could become storage orphans after customer removal or checkout abandonment. The root cause was that finalization moved uploads to `Finalized`, while explicit removal and cleanup only disposed `Pending`/`Uploaded` objects. The audit fixed this by allowing unbound finalized attachments to be removed/expired and deleting both staged and final R2 keys. Regression tests were added and focused verification passed.

No remaining P0 implementation defect is known after that fix. The remaining P0 gate is external: live R2 verification remains blocked because no real R2 credentials/configuration were available in the accessible environment. This is not classified as an implementation defect.

## 2. Scope

Audited phases:

- Phase 1: Domain, database, API.
- Phase 2: POS Admin Checkout Form Builder.
- Phase 3: Storefront Dynamic Checkout Runtime.
- Phase 4A: Order Snapshot and custom field persistence.
- Phase 4B: Checkout Attachments, uploads, R2 lifecycle.
- Phase 5: Conditional Checkout Fields and rules engine.

Evidence sources:

- Current repository source under `PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure`, `PointOfSale.Storefront`, `PointOfSale.Web`, and `PointOfSale.Tests`.
- Prior reports:
  - `docs/architecture/SHOPIXY_DYNAMIC_CHECKOUT_FORENSIC_AUDIT_2026-08-10.md`
  - `docs/architecture/SHOPIXY_R2_PRODUCT_MEDIA_FORENSIC_TRACE_2026-08-10.md`
  - `docs/architecture/SHOPIXY_R2_RUNTIME_RECOVERY_P0_VERIFICATION_2026-08-10.md`
- Verification commands listed in Section 22.

Constraints honored:

- No commits, pushes, resets, or unrelated reverts.
- No browser automation.
- No fabricated R2 verification.
- No unrelated feature work.

Worktree baseline:

- `git status --short`: heavily dirty tree with both Dynamic Checkout work and unrelated domain/SSL/custom-domain work. Many Dynamic Checkout files are untracked.
- `git diff --stat`: 84 tracked files changed, 10,398 insertions, 1,085 deletions.
- `git diff --name-only`: tracked changes only; Dynamic Checkout also includes untracked files not represented in that list.
- Dynamic Checkout files include `AdminCheckoutFieldDefinitionsController`, `StorefrontCheckoutFieldsController`, checkout DTOs, `CheckoutFieldDefinitionService`, `StorefrontCheckoutFieldDefinitionService`, `CheckoutService`, `CheckoutAttachmentService`, `CheckoutFieldRuleEngine`, Dynamic Checkout entities/enums/configurations/migrations/tests, Storefront checkout runtime/rules/tests, and Web admin builder/order presentation.
- Clearly unrelated dirty work includes custom domain, SSL/TLS, DNS verification, Caddy, CommerceHub, auth/register, and multiple domain certification documents.

## 3. Phase Status

| Phase | Scope | Implementation | Verification | Status |
|---|---|---|---|---|
| Phase 1 | Domain/API/DB | Implemented: field definitions, enums, EF config, migrations, admin CRUD/init/order. | Static source, EF model check, focused backend tests. | PASS |
| Phase 2 | Admin Builder | Implemented: admin page, IA/nav, create/edit/archive/reorder/config/rules/i18n/error handling. | Static source and Web Vitest builder coverage. | PASS |
| Phase 3 | Storefront Runtime | Implemented: public field API, checkoutStore lifecycle, ShippingStep render/validate/submit integration. | Static source and Storefront Vitest rule/runtime coverage. | PASS |
| Phase 4A | Order Snapshot | Implemented: snapshot entity, EF config, migration, server snapshot creation. | Static source and backend snapshot tests. | PASS WITH P1 HARDENING |
| Phase 4B | Attachments/R2 | Implemented: lease, confirm/finalize, order attach, snapshot metadata, merchant download, cleanup. | Static source, focused tests, build. Live R2 blocked. | BLOCKED / ENVIRONMENT |
| Phase 5 | Conditional Rules | Implemented: browser/server rule engines, admin validation, storefront/server integration. | Static source, backend rule tests, Storefront rule tests. | PASS |

## 4. Final Architecture Map

Dynamic Checkout control flow:

1. POS Admin route `/storefront/checkout-fields`.
2. Admin CRUD API `api/admin/storefront/checkout-fields`.
3. `CheckoutFieldDefinitionService`.
4. `CheckoutFieldDefinition`.
5. `ConfigurationJson` stores field config, upload policy, and conditional rules as jsonb.
6. `StorefrontCheckoutFieldsController`.
7. `StorefrontCheckoutFieldDefinitionService`.
8. `checkoutStore.fetchCheckoutFields`.
9. `ShippingStep` renders system/custom fields in canonical order.
10. Browser `checkoutFieldRules.ts` computes effective visibility/requiredness.
11. Upload fields use lease -> PUT to R2 presigned URL -> confirm/finalize.
12. Checkout submission sends system address fields plus `customFields[]`.
13. `CheckoutService` reloads tenant field definitions and re-evaluates server rule state.
14. Existing checkout authorities revalidate cart, shipping, payment, pricing, coupon/session, and idempotency inside the final transaction.
15. Order creation persists `Order`.
16. `OrderCheckoutFieldSnapshot` persists historical field metadata, values, effective state, config, and rule snapshot.
17. `CheckoutAttachment` rows move to `Attached` for upload fields.
18. Merchant download reads attached final R2 object through admin order authority.

Integration map:

- Shipping methods: loaded and selected through existing storefront shipping method authority. Dynamic fields do not decide shipping availability.
- Payment methods: loaded and selected through existing storefront payment authority. Dynamic fields do not decide payment availability.
- Pricing/cart/coupons: checkout and pricing endpoints still compute totals server-side and revalidate request fingerprints.
- Customer authentication/session: attachment ownership binds tenant, cart, owner type, owner id, session id, and customer id.
- Checkout session/idempotency: checkout request idempotency fingerprint includes normalized custom field values and sorted attachment IDs.
- Order creation: snapshots and attachment binding are created/updated under order transaction authority.

## 5. Phase 1 Findings

PASS.

Implemented evidence:

- `CheckoutFieldDefinition` is tenant-owned and contains `FieldKind`, `FieldType`, `SystemFieldKey`, enabled/required/order fields, schema version, and `ConfigurationJson`.
- Enums `CheckoutFieldKind` and `CheckoutFieldType` cover system/custom and text/email/phone/textarea/select/radio/checkbox/number/date/image/file/multi-image.
- EF configuration maps jsonb config, tenant FK, soft delete/global query filter, check constraints, and indexes.
- Migration `20260809185514_SHOPIXY_CheckoutFieldDefinitions` exists and matches the modeled table.
- `ApplicationDbContext` exposes `CheckoutFieldDefinitions` and applies tenant/not-deleted global filtering.
- Admin endpoints cover GET, POST custom, PATCH, DELETE/archive custom, PUT order, and POST system initialization.
- Admin controller requires Owner/Admin authorization and tenant context.

Integrity evidence:

- System field initialization is idempotent and uses canonical keys.
- Active system field uniqueness is enforced by service validation and database unique index.
- System fields cannot be deleted or have type/key mutated by supported admin APIs.
- Custom fields are always created with `FieldKind.Custom` and `SystemFieldKey = null`; custom fields cannot impersonate system keys.
- Configuration normalization bounds text, options, uploads, and rules.

## 6. Phase 2 Findings

PASS.

Admin builder evidence:

- Route and navigation are present for Storefront checkout fields.
- Backend authorization enforces Owner/Admin; frontend IA exposes the area through the admin storefront module.
- UI supports list rendering, create, update, archive/delete custom fields, enable/disable, required/optional, type editing for custom fields, config editing, select/radio options, ordering, keyboard ordering, dirty-state confirmation, backend error display, and i18n keys.
- Upload configuration supports allowed MIME types, max file size, and max files.
- Rule builder supports source/target/action/operator/mode configuration and delegates final canonical validation to the backend.

Accessibility/static evidence:

- The builder uses labeled controls, button labels/aria labels for ordering actions, dialog primitives, and alert rendering for errors.

Unsupported future functionality:

- Field grouping, multi-page forms, conditional pricing/shipping/payment, templates, product schemas, analytics, external form integrations, and workflow automation are not presented as complete Dynamic Checkout capabilities. They remain Future Enhancement.

## 7. Phase 3 Findings

PASS.

Storefront field API evidence:

- `GET /api/storefront/checkout-fields` is anonymous but resolves tenant through storefront tenant context.
- Service returns only enabled, non-deleted runtime-supported fields for the tenant.
- DTOs expose safe runtime fields: id, kind, type, system key, label/help, required/enabled/order/config, and status metadata.
- No EF entity is returned directly.
- Empty state returns semantic `EMPTY`; unexpected failures return `UNAVAILABLE`/error wrapper.

Storefront runtime evidence:

- `checkoutStore` loads checkout fields with no-store headers, abort/sequence guards, response shape validation, lifecycle status, and pruning of stale custom values.
- Dynamic validation is run in the browser for visible/enabled/effective required fields.
- Request construction sends visible system fields through canonical address/customer notes fields and visible custom fields through `customFields`.
- Hidden and disabled custom fields are not submitted by the browser.
- Unknown/malformed field responses are rejected into safe degraded states.

ShippingStep evidence:

- Dynamic fields render in canonical order.
- Static fallback is preserved when no dynamic field definitions are configured.
- Errors map by system key/custom id.
- Upload controls use lease, direct R2 PUT, confirm, and remove flows.

## 8. Phase 4A Findings

PASS WITH P1 HARDENING.

Snapshot evidence:

- `OrderCheckoutFieldSnapshot` stores tenant id, order id, checkout field definition id, field kind/type, system key, label, help text, display order, required/enabled/effective state, schema version, configuration JSON, and value JSON.
- Migration `20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots` exists.
- EF maps snapshots with tenant/order relationship and composite order FK.
- The snapshot intentionally does not FK back to live field definitions, preserving historical reconstruction after current definitions change/delete.
- `CheckoutService` creates snapshots during order creation from current server-resolved definitions and effective rule state.

Mutability audit:

- No supported API was found that edits or deletes order snapshots after order creation.
- EF itself does not enforce immutability in `SaveChanges`; internal code with DbContext access could mutate snapshots.
- Classification: P1 integrity hardening, not a current API-level defect.

## 9. Phase 4B Findings

PASS STATICALLY; LIVE R2 BLOCKED.

Attachment lifecycle evidence:

- Lease validates tenant, cart, owner, session/customer, field, MIME, size, max files, duplicate pressure, and generates server-owned staging object keys.
- Confirm validates owner/cart/field again, checks R2 metadata/signature, content type, exact size, policy, copies staged object to final key, verifies final metadata, records final ETag/checksum, and moves to `Finalized`.
- Checkout submit validates duplicate IDs, max files, ownership, identity, expiry, finalization, and attaches rows to order.
- Merchant download verifies admin role, order tenant ownership, attachment order binding, `Attached` status, final key, and final ETag before streaming.
- Cleanup now expires unbound `Pending`, `Uploaded`, `Failed`, `Deleting`, and expired `Finalized` attachments.
- Explicit remove now disposes unbound finalized attachments as well as pending/uploaded attachments.

Fix applied during this audit:

- Before this pass, finalized but unattached rows were not removable and cleanup ignored expired `Finalized` rows.
- The fix allows only unbound finalized attachments to be removed/expired and deletes both staged and final object keys.
- Attached order files remain protected.

R2 live status:

- R2 architecture is present.
- Accessible process/user/machine environment and checked config files did not contain real R2 credentials/configuration.
- Therefore R2 LIVE VERIFICATION = BLOCKED / ENVIRONMENT.

## 10. Phase 5 Findings

PASS.

Rule engine evidence:

- Backend `CheckoutFieldRuleEngine` supports modes `ALL` and `ANY`.
- Operators supported: `EQUALS`, `NOT_EQUALS`, `CONTAINS`, `NOT_CONTAINS`, `IS_EMPTY`, `IS_NOT_EMPTY`, `GREATER_THAN`, `GREATER_THAN_OR_EQUAL`, `LESS_THAN`, `LESS_THAN_OR_EQUAL`.
- Actions supported: `SHOW`, `HIDE`, `REQUIRE`, `OPTIONAL`.
- Admin validation rejects missing/disabled/deleted source fields, missing targets, self-reference, invalid operators, invalid comparison values, select/radio values not present in config, invalid upload comparisons, and dependency cycles.
- Browser `checkoutFieldRules.ts` mirrors server semantics.
- Checkout submission re-evaluates rules on the server and does not trust browser visibility/requiredness.

## 11. Rule Semantics Certification

Certified semantics:

For each field:

`effective state = visibility + requiredness + enabledness`

Initial state:

- `enabled = field.IsEnabled`
- `visible = field.IsEnabled`
- `required = field.IsEnabled && field.IsRequired`

Rule precedence:

- SHOW rules: if any SHOW rule targets a field, the field is hidden unless at least one SHOW rule matches.
- HIDE rules: matching HIDE overrides visibility.
- REQUIRE rules: matching REQUIRE makes a visible field required.
- OPTIONAL rules: matching OPTIONAL clears requiredness.
- Invisible fields are not required.
- Disabled fields are not visible and not required.

Mode semantics:

- `ALL`: all conditions in the rule must match.
- `ANY`: at least one condition in the rule must match.

Source value semantics:

- Missing source value is treated as empty/null.
- Hidden source field is evaluated as empty/null.
- Disabled/deleted source fields are rejected during rule validation.
- Upload source supports empty/not-empty based on attachment presence.
- Number comparisons use canonical numeric parsing.
- Date comparisons use canonical date parsing.
- Checkbox comparisons use boolean comparison.
- Select/radio comparisons are constrained to configured option values.
- Text-like fields support equality/contains/empty, not numeric/date comparisons.

Parity:

- Storefront and backend semantics match statically and through focused rule tests.
- No browser/server semantic divergence was found after inspection.

## 12. Checkout Authority Certification

PASS.

Server authority:

- Checkout submission sends `fieldId`, `value`, and/or attachment IDs.
- Server resolves tenant, field definitions, field state, rules, type, configuration, upload policy, and ownership.
- Browser is not authoritative for requiredness, visibility, enabledness, upload validity, tenant, field ownership, or field configuration.

Adversarial cases:

| Case | Server result |
|---|---|
| Hidden custom field submitted | Rejected. |
| Disabled custom field submitted | Rejected. |
| Unknown field ID | Rejected. |
| Cross-tenant field ID | Rejected by tenant-scoped definition load. |
| Deleted field ID | Rejected. |
| Invalid type/value | Rejected. |
| Conditional required field missing | Rejected. |
| Conditional optional field missing | Accepted. |
| Unfinalized upload submitted | Rejected. |
| Expired upload submitted | Rejected. |

## 13. System Field Contract

PASS.

Canonical keys:

- `full_name`
- `phone`
- `email`
- `city`
- `area`
- `street_address`
- `building`
- `apartment`
- `postal_code`
- `order_notes`

Canonical mapping:

| System key | Checkout request/order mapping |
|---|---|
| `full_name` | `ShippingAddress.Name` |
| `phone` | `Phone` |
| `email` | `Email` |
| `city` | `ShippingAddress.City` |
| `area` | `ShippingAddress.State` |
| `street_address` | `ShippingAddress.Street` |
| `building` | `ShippingAddress.Building` |
| `apartment` | `ShippingAddress.Apartment` |
| `postal_code` | `ShippingAddress.ZipCode` |
| `order_notes` | `CustomerNotes` |

`order_notes` duplication:

- `order_notes` is represented as the dynamic system-field snapshot value and as `CustomerNotes` on the order.
- This is intentional and safe: `CustomerNotes` remains the operational order field, while the snapshot preserves the historical checkout form answer.

## 14. Order Historical Reconstruction

PASS STATICALLY.

Hypothetical mixed order persistence:

| Field type | Persisted evidence |
|---|---|
| System field | field id, kind, type, system key, label/help, order, config, effective state, scalar value. |
| Custom text | field id, custom kind/type, label/help, config, rules, scalar string value. |
| Select/radio | selected option value plus snapshot config containing options. |
| Number | canonical numeric string/value JSON plus type/config. |
| Checkbox | boolean value plus type/config. |
| Date | canonical date value plus type/config. |
| Image/file/multi-image | attachment metadata array: attachment id, file name, content type, size, media type, upload/finalization timestamps, final ETag/checksum. |
| Conditional field | value plus effective visibility/requiredness and rule/config snapshot. |

Historical reconstruction after definition changes:

- Current label/type/config/rules changes do not rewrite existing snapshots through supported APIs.
- Snapshots intentionally retain historical metadata and values independent of current definitions.
- Attachment object keys are not stored in snapshot DTO values; merchant download uses order-bound attachment rows.

## 15. Attachment State Machine

Implemented states:

- `Pending`
- `Uploaded`
- `Finalized`
- `Attached`
- `Expired`
- `Failed`
- `Deleting`
- `Deleted`

Requested conceptual mapping:

- `CONFIRMED` maps to implemented `Uploaded`.
- `FINALIZING` is not a persisted state; finalization is guarded through expected-status updates and idempotent final-object checks.
- `FINALIZED` maps to implemented `Finalized`.

Allowed transitions:

| Transition | Status |
|---|---|
| Lease created | `Pending` |
| Confirm metadata accepted before final copy | `Uploaded` |
| Final R2 copy verified | `Finalized` |
| Checkout order binding | `Attached` |
| Explicit remove before order binding | `Deleting` -> `Deleted` |
| Expired orphan cleanup | `Deleting` -> `Expired` + soft deleted |
| Validation/finalization failure | `Failed` |

Illegal transition protections:

- Attached files cannot be removed by storefront delete.
- Pending uploads cannot be attached to an order.
- Expired uploads cannot be attached.
- Duplicate attachment IDs are rejected.
- Cross-owner/session/customer/cart/tenant attachment use is rejected.
- Finalization is idempotent and guarded by expected status.

## 16. Security / Tenant Isolation

PASS STATICALLY.

Read/write path protections:

- Admin checkout field APIs require Owner/Admin and tenant context.
- Storefront field API resolves tenant from storefront request context and returns safe DTOs.
- Checkout submission loads definitions by tenant and not-deleted state, ignoring browser-provided field authority.
- Attachment lease/confirm/remove/attach validate tenant, cart, owner id, owner type, session id, customer id, and field id.
- Merchant download validates admin role, tenant-owned order, order-bound attachment, attached status, and final object integrity.
- R2 object keys are server generated.
- Presigned upload URLs are issued only after cart/field/policy authority checks.
- Final object keys are not exposed in storefront/admin presentation DTO values.

Adversarial classifications:

| Attack | Result |
|---|---|
| Tenant A field in Tenant B checkout | Rejected. |
| Tenant A attachment in Tenant B checkout | Rejected. |
| Tenant A order attachment download by Tenant B admin | Rejected/not found. |
| Customer A attachment in Customer B checkout | Rejected. |
| Guest A session attachment in Guest B session | Rejected. |
| Wrong cart/order/field/attachment | Rejected. |
| Deleted/disabled field | Rejected or omitted according to path. |
| Expired/unfinalized attachment | Rejected. |

## 17. API Contract Matrix

| Method | Route | Authorization | Tenant source | Request DTO | Response DTO / contract | Status |
|---|---|---|---|---|---|---|
| GET | `/api/admin/storefront/checkout-fields` | Owner/Admin | `ITenantContext` | query/default | `CheckoutFieldDefinitionsResponseDto` | PASS |
| POST | `/api/admin/storefront/checkout-fields/system-fields/initialize` | Owner/Admin | `ITenantContext` | none | initialized field list | PASS |
| POST | `/api/admin/storefront/checkout-fields` | Owner/Admin | `ITenantContext` | `CreateCheckoutFieldDefinitionDto` | field DTO | PASS |
| PATCH | `/api/admin/storefront/checkout-fields/{fieldId}` | Owner/Admin | `ITenantContext` | `UpdateCheckoutFieldDefinitionDto` | field DTO | PASS |
| DELETE | `/api/admin/storefront/checkout-fields/{fieldId}` | Owner/Admin | `ITenantContext` | route id | archive/no content | PASS |
| PUT | `/api/admin/storefront/checkout-fields/order` | Owner/Admin | `ITenantContext` | order DTO | reordered field list | PASS |
| GET | `/api/storefront/checkout-fields` | Anonymous | storefront tenant resolver | none | storefront field response with `AVAILABLE`/`EMPTY` | PASS |
| POST | `/api/storefront/checkout/attachments/upload-lease` | Anonymous/customer | storefront tenant + owner/session | lease DTO | presigned upload lease | PASS STATIC |
| POST | `/api/storefront/checkout/attachments/confirm` | Anonymous/customer | storefront tenant + owner/session | confirm DTO | finalized attachment DTO | PASS STATIC |
| DELETE | `/api/storefront/checkout/attachments/{attachmentId}` | Anonymous/customer | storefront tenant + owner/session | route/query ids | delete/no content/error | PASS |
| POST | `/api/storefront/checkout` | Anonymous/customer | storefront tenant + owner/session | `CheckoutRequestDto` | checkout/order response | PASS |
| GET | `/api/admin/orders/{orderId}/checkout-attachments/{attachmentId}/download` | Owner/Admin | `ITenantContext` | route ids | file stream/error | PASS STATIC |

No EF entity leakage was found across Dynamic Checkout API boundaries. No R2 secrets are returned. Storage object keys are not exposed through storefront upload values or admin order presentation.

## 18. Frontend/Backend Contract Parity

PASS.

Parity verified:

- Field kinds and field types match C# DTOs/enums.
- Configuration schema matches options/upload/rules used by backend.
- Conditional rule modes/operators/actions match.
- Upload values use attachment IDs rather than object keys.
- Checkout custom field DTO uses `fieldId`, `value`, and `attachmentIds`.
- Semantic states for checkout fields distinguish available, empty, unavailable, and error.
- Frontend ignores stale/unknown field ids after field refresh.
- Admin order presentation strips storage internals from snapshot display.

No enum, casing, nullability, or scalar type mismatch was found that causes a Dynamic Checkout defect.

## 19. Error Semantics

PASS WITH MINOR P2 UI HARDENING.

Certified semantics:

- Checkout fields: `EMPTY` means no configured runtime fields; `UNAVAILABLE`/`ERROR` means load failure.
- Shipping/payment empty states remain owned by shipping/payment services and are not reclassified by Dynamic Checkout.
- Checkout validation returns field-specific validation errors for required/type/rule/upload failures.
- Upload API separates invalid request, forbidden authority, conflict/invalid state, and storage unavailable cases.
- Merchant download separates not found, not downloadable/integrity conflict, and storage unavailable.

P2 hardening:

- Some frontend paths still use broad generic retry/failure copy for unexpected checkout-field load failures. This is not a business-state defect; it is copy precision hardening.

## 20. Concurrency / Idempotency

PASS STATICALLY.

Evidence:

- System field initialization is idempotent and handles concurrent unique-key insert race.
- Field ordering is tenant-scoped and server-normalized.
- Checkout submission uses idempotency coordination and includes custom field fingerprinting.
- Order creation revalidates pricing/shipping/payment/custom fields inside the final authority path.
- Attachment finalization uses expected-status updates, final object idempotent replay, and final ETag/checksum verification.
- Attachment attach uses expected `Finalized`/`OrderId == null` claim.
- Cleanup claims rows by setting `Deleting` before object deletion.

Remaining hardening:

- Live multi-instance R2 race behavior remains blocked without real R2.

## 21. Test Certification Matrix

| Area | Item | Result | Evidence |
|---|---|---|---|
| Admin | create/update/archive/reorder/initialize/configure upload/configure rules | PASS | `CheckoutFieldDefinitionServiceTests`; Web checkout fields tests. |
| Storefront | load/render/empty/fallback/validation/rules/upload/remove/submission construction | PASS | Storefront checkout/rule tests; static source trace. |
| Backend | validation/tenant/auth/snapshots/attachments/rules/idempotency | PASS | Focused backend tests: 75 passed after fix. |
| Integration | shipping/payment/pricing/cart/order authority | PASS STATIC; broader tests have stale failures | Source trace plus prior focused authority tests; stale certification tests listed below. |
| R2 live | staged upload/finalization/download/cleanup with real credentials | BLOCKED | No real R2 credentials/configuration available. |

Focused verification run during final pass:

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CheckoutAttachmentServiceTests|FullyQualifiedName~CheckoutAttachmentDownloadSecurityArchitectureTests|FullyQualifiedName~CheckoutAttachmentControllerContractTests|FullyQualifiedName~CheckoutFieldDefinitionServiceTests|FullyQualifiedName~OrderCheckoutFieldSnapshotTests|FullyQualifiedName~CheckoutFieldRuleEngineTests|FullyQualifiedName~DynamicAddressContractTests"`: PASS, 75 passed.
- `npm run test -- --run src/commerceAuthority.certification.test.ts src/utils/checkoutFieldRules.test.ts` in Storefront: PASS, 29 passed.
- `npm run test -- --run src/modules/storefront/checkoutFields/checkoutFields.test.tsx src/pages/orderCheckoutFieldPresentation.test.ts` in Web: PASS, 18 passed.

Broader existing failures:

- A broader storefront/payment/shipping authority test filter failed 22 tests before the final fix.
- Classification: C stale certification/source-string assertions and B unrelated existing failures.
- Examples: assertions looking for old source substrings and missing historical certification markers such as `PASS SF-PAY-END`, `PASS SF-PA5.6`, `PASS SF-SHP3`, `PASS SF-FINAL`, and old function/source snippets.
- These were not fixed because they are not real Dynamic Checkout implementation defects.

## 22. Build / Migration Verification

| Check | Result | Evidence |
|---|---|---|
| `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore` | PASS | Build succeeded, 0 warnings, 0 errors. |
| `dotnet build PointOfSale.sln --no-restore` | PASS | Build succeeded, 0 warnings, 0 errors. |
| `npm run build` Storefront | PASS | TypeScript and Vite production build completed. |
| `npm run build` Web | PASS | Vite production build completed. |
| `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext` | PASS | Build succeeded; no pending model changes. |
| Source migrations present: `20260809185514_SHOPIXY_CheckoutFieldDefinitions` | PASS | Migration source exists. |
| Source migrations present: `20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots` | PASS | Migration source exists. |
| Source migrations present: `20260809203807_SHOPIXY_CheckoutAttachments` | PASS | Migration source exists. |
| Source migrations present: `20260810114400_SHOPIXY_CheckoutAttachmentFinalization` | PASS | Migration source exists. |
| Live target database applied migration state | BLOCKED | No target runtime database verification was performed in this final pass. |

Build warnings:

- Storefront Vite reported non-blocking chunk/dynamic import warnings and stale Browserslist data.
- These are not Dynamic Checkout correctness failures.

## 23. Production Readiness Matrix

| Category | Result | Evidence |
|---|---|---|
| Security | PASS STATIC | Server authority, tenant checks, object-key generation, no secret leakage. |
| Tenant isolation | PASS STATIC | Tenant-scoped services, filters, explicit tenant predicates. |
| Data integrity | PASS | DB constraints, server validation, snapshot creation, attachment finalization fix. |
| Historical integrity | PASS WITH P1 HARDENING | Snapshot API immutable by supported paths; no EF immutability guard. |
| Concurrency | PASS STATIC | Expected-state updates and idempotency coordination. |
| Idempotency | PASS STATIC | Checkout fingerprint includes custom fields/attachments. |
| Storage integrity | BLOCKED LIVE | Static lifecycle complete after fix; real R2 not verified. |
| Error semantics | PASS WITH P2 HARDENING | Semantic empty/unavailable separation; minor copy polish remains. |
| Observability | PARTIAL | Logs exist for storage failures; no dedicated Dynamic Checkout metrics/audit dashboard. |
| API stability | PASS STATIC | DTO boundaries present; no EF leakage found. |
| Frontend/backend parity | PASS | TS/C# contracts align. |
| Migration safety | PASS STATIC / BLOCKED LIVE DB | EF no pending model changes; target DB application not verified. |
| Recovery behavior | PASS STATIC / BLOCKED LIVE R2 | Cleanup/finalization recovery logic exists; live R2 race not verified. |

## 24. Remaining Gaps

| ID | Area | Finding | Severity | Type | Evidence | Required Action |
|---|---|---|---|---|---|---|
| DC-BLOCK-001 | R2 | Real R2 staged upload/finalization/download/cleanup/concurrency/idempotency was not live-verified. | P0 | BLOCKED | No real R2 credentials/configuration available in accessible environment; prior R2 report also blocked. | Run disposable live R2 verification with real staging credentials. |
| DC-BLOCK-002 | Database | Target runtime database applied migration state was not live-verified. | P0 for production cutover | BLOCKED | EF model check passed, but no target DB migration list/application check was performed. | Verify production/staging DB has all four Dynamic Checkout migrations applied. |
| DC-P1-001 | Snapshot integrity | `OrderCheckoutFieldSnapshot` has no EF SaveChanges immutability guard. | P1 | Hardening | No supported API mutates snapshots, but internal DbContext code could. | Add guard/tests preventing update/delete after creation except explicitly approved maintenance. |
| DC-P1-002 | Observability | Dynamic Checkout has no dedicated metrics/audit dashboard for field load failures, validation rejects, upload lifecycle failures, cleanup outcomes, or rule conflicts. | P1 | Hardening | Logging exists; no dedicated metric surface found. | Add operational metrics and alerts for checkout field and attachment lifecycle failures. |
| DC-P1-003 | Admin order API privacy | Admin order DTO exposes raw snapshot `ConfigurationJson`/`ValueJson` in addition to safe presentation mapping. | P1 | Hardening | `AdminOrderDetailsDto` carries raw JSON; current snapshot values omit object keys. | Replace raw JSON exposure with typed presentation DTOs or explicitly documented internal-admin contract. |
| DC-P1-004 | Live race recovery | R2 finalization, cleanup race, merchant download integrity, and checkout idempotency are statically guarded but not exercised against real object storage. | P1 | BLOCKED/HARDENING | Static tests use mocks; R2 live blocked. | Add a disposable R2 integration smoke suite once credentials exist. |
| DC-P2-001 | Frontend error copy | Some checkout field load failures use broad generic failure copy instead of more precise semantic wording. | P2 | Quality | Storefront distinguishes states internally, but UI copy can be generic. | Polish copy for unavailable vs validation vs retryable states. |
| DC-P2-002 | Admin rule UX | Admin rule builder relies on backend validation for cycles/conflicts instead of preemptively explaining every invalid graph. | P2 | Quality | Backend rejects invalid graphs; UI handles API errors. | Add client-side previews for cycle/conflict explanations. |
| DC-FUTURE-001 | Form grouping/pages | Field grouping and multi-page checkout forms are not implemented. | FUTURE | Product enhancement | Outside completed phases. | Consider only if product roadmap requires. |
| DC-FUTURE-002 | Conditional commerce effects | Conditional pricing, shipping, and payment rules are not implemented. | FUTURE | Product enhancement | Dynamic Checkout rules affect field visibility/requiredness only. | Treat as separate product phase if needed. |
| DC-FUTURE-003 | Templates/external forms | Reusable templates, product-specific schemas, external form integrations, analytics, advanced drag/drop, and workflow automation are not implemented. | FUTURE | Product enhancement | Outside completed phases. | Defer. |

## 25. Known Unrelated Findings

Preserved from prior forensic work:

- Product media DB deletion does not delete the R2 object.
- Product upload does not prove product existence before upload.
- These are Product Media findings, not Dynamic Checkout P0 blockers.

Other unrelated findings:

- Existing Storefront/shipping/payment certification tests include stale source-string and certification-marker assertions.
- The repository also contains extensive unrelated custom domain/SSL/DNS work in the dirty tree.

## 26. Fixes Applied During Audit

Fix DC-FIX-001: finalized orphan attachment disposal.

Files changed:

- `PointOfSale.API/Services/Checkout/CheckoutAttachmentService.cs`
- `PointOfSale.API/Jobs/CheckoutAttachmentCleanupJob.cs`
- `PointOfSale.Tests/Checkout/CheckoutAttachmentServiceTests.cs`

Root cause:

- Confirming an upload finalizes it immediately.
- The storefront remove endpoint and cleanup job only disposed `Pending`/`Uploaded` objects.
- A customer could remove a confirmed upload or abandon checkout after finalization, leaving an unbound final object and row outside cleanup.

Correction:

- Explicit removal now allows `Finalized` only when `OrderId == null`.
- Cleanup now includes expired unbound `Finalized` rows.
- Disposal deletes both staged and final object keys when both exist.
- Attached order files remain protected.

Regression coverage:

- `RemovePendingAttachment_RemovesUnattachedFinalizedAttachmentAndFinalObject`
- `CleanupJob_ExpiresUnattachedFinalizedAttachmentAndDeletesFinalObject`
- Existing finalized protection coverage was adjusted to prove unexpired finalized uploads are not prematurely deleted.

Verification:

- Focused backend Dynamic Checkout tests passed: 75/75.
- API and solution builds passed.

## 27. Recommended Next Phase

Do not start a new feature phase until the external P0 gates are closed:

1. Disposable live R2 verification.
2. Target database migration-state verification.

After those gates pass, Dynamic Checkout can move out of implementation and into hardening. The highest-value next phase is Dynamic Checkout production hardening, not new form features. The hardening phase should add live R2 smoke automation, snapshot immutability guard, checkout/upload observability, and typed admin order presentation for snapshot values.

Future product phases such as field grouping, multi-page forms, conditional commerce effects, templates, product schemas, external integrations, analytics, and workflow automation can safely be deferred.

## 28. Final Certification

Final status:

BLOCKED
IMPLEMENTATION COMPLETE BUT REQUIRED EXTERNAL VERIFICATION REMAINS

Required next-phase decision answers:

1. Is Dynamic Checkout architecturally complete? Yes. The current architecture covers admin definition, storefront runtime, server authority, snapshots, uploads/R2 lifecycle, merchant download, and conditional rules.
2. Are there any real P0 implementation defects? No known P0 implementation defect remains after the finalized orphan attachment fix.
3. Is the only P0 blocker external R2 runtime verification? R2 live verification is the primary Dynamic Checkout P0 blocker; target database applied-migration verification is also a production cutover blocker.
4. What exact P1 hardening remains? Snapshot immutability guard, Dynamic Checkout observability/alerts, typed admin order snapshot presentation, and live R2 race/recovery smoke automation.
5. What can safely be deferred? Grouping, multi-page forms, conditional pricing/shipping/payment, reusable templates, product-specific schemas, external forms, analytics, advanced drag/drop, and workflow automation.
6. Should we start a new feature phase? No, not before live R2 and target DB migration verification close.
7. If yes, what is the highest-value next phase and why? After blockers close, the highest-value next phase is production hardening because the feature architecture is complete and remaining risk is operational/integrity verification, not missing feature scope.
8. If no, what exact blocker must be closed first? Run real disposable R2 verification with valid credentials, then verify the target database has the four Dynamic Checkout migrations applied.

Certification statement:

Dynamic Checkout is implementation-complete and architecturally complete in the inspected source. It is not fully production-certified only because required external runtime verification, especially real R2, remains unavailable in this environment.

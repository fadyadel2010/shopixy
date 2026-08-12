# SHOPIXY Shipping Methods Step 2N Implementation Report

Date: 2026-08-12  
Repository: `PointOfSale`  
Governing audit: `docs/architecture/SHOPIXY_SHIPPING_FINAL_FORENSIC_AUDIT_2026-08-11.md`  
Certification result: **BLOCKED**

## 1. Executive Summary

Step 2N implemented the production code remediations for F-01 through F-06 and recovered the audit's originally identified critical behavioral tests. The implementation preserves the established server-authoritative chain:

`CarrierCapabilityRegistry -> CarrierIntegrationAuthority -> CarrierConfiguration -> readiness -> ShippingMethod eligibility`

and:

`ShippingMethod.FulfillmentWarehouseId -> WarehouseResolution -> final checkout lock -> inventory reservation -> Order/OrderItem/Shipment warehouse snapshots`.

The focused Step 2N gate is green at 80/80, the Storefront suite is green at 283/283, Shipping Administration is green at 13/13, both frontend builds pass, the solution builds, EF reports no pending model changes, and a real PostgreSQL rollback/legacy-data/reapply migration test passes.

Certification is nevertheless **BLOCKED**. The required broader internal gates are red: the shipping/carrier/shipment name-filtered suite has 34 failures, the full Web suite has 10 failures, both .NET architecture and non-architecture partitions contain numerous unrelated failures, and a monolithic .NET run failed to complete. Under the governing certification rule, an internal red gate cannot be downgraded to a warning.

## 2. Baseline

Before Step 2N edits:

- `git status --short`: 93 entries.
- Tracked modified paths: 56.
- Untracked paths: 37.
- `git diff --stat`: 56 files changed, 4,899 insertions, 1,333 deletions.
- The worktree already contained the Step 2A-2M shipping implementation, warehouse resolution entities/migrations, carrier authority files, audit documents, and many unrelated checkout, CMS, CommerceHub, frontend, and test edits.
- No reset, clean, stash, checkout, revert, commit, or push was performed.

The baseline dirty paths were treated as user-owned. Step 2N edited only in-place areas required for the findings and preserved unrelated hunks.

## 3. F-01 Implementation — Weighted Pricing Convergence

Implemented:

- Added authoritative unit weight to `CartItemSnapshot`.
- Checkout SQL catalog revalidation now refreshes weight from the current variant, falling back to the current product weight.
- Cart preview and checkout pricing both calculate total weight as `sum(max(0, unit weight) * quantity)`.
- Both paths delegate to `IShippingCalculationService`; no new pricing formula was introduced.
- The idempotency snapshot schema advanced to v5 and includes normalized authoritative weight, preventing replay of an old pricing decision after product weight changes.
- Existing free-threshold, non-negative, currency/tenant, and `MidpointRounding.AwayFromZero` semantics remain unchanged.

Covered behavior includes zero weight, weighted cost, free threshold, rounding, cart pricing preview versus checkout pricing, and idempotency hash invalidation on authoritative weight change. Checkout persists `pricing.Shipping` to `Order.ShippingCost` and replay returns the durable order response.

## 4. F-02 Implementation — Webhook Secret Security

Implemented:

- `CarrierConfiguration.WebhookSecret` is encrypted with the existing carrier credential encryption service.
- Canonical save/rotation occurs only through `CarrierIntegrationAuthority`.
- API responses expose only `HasWebhookSecret`; no response DTO has a webhook-secret value.
- A strict startup hosted service encrypts legacy plaintext values in place before webhook traffic is accepted and invalidates prior validation evidence.
- Webhook trust resolves and decrypts the secret inside the authority only after tenant, configuration, and capability checks.
- Missing secrets fail closed for shared-secret or HMAC modes.
- Raw webhook signatures are no longer persisted. A SHA-256 digest is stored because Bosta's shared-secret signature header may itself contain the secret.
- Logs contain carrier/tenant identifiers and counts, never credential or webhook-secret values.

Capability reconciliation:

- Bosta: webhook supported, shared-secret trust.
- Aramex: webhook not advertised because its definition declares no implemented support.
- DHL: webhook not advertised because its definition declares no implemented support.
- Mylerz/FedEx remain unavailable/not-certified in canonical capability metadata and are not promoted as webhook-ready.

Behavioral coverage includes missing, invalid, valid, mixed-case Bearer normalization, duplicate event, rotation, wrong secret, tenant isolation, write-only API serialization, legacy backfill, and secret/signature non-disclosure.

## 5. F-03 Implementation — Validation Version Convergence

Implemented:

- Added `CredentialConfigurationFingerprint` and `LastValidatedConfigurationFingerprint`.
- The SHA-256 fingerprint covers decrypted canonical credential/configuration inputs, endpoint, environment, capability flags, and webhook secret.
- Save atomically clears old validation evidence whenever the current fingerprint changes.
- Validation stamps evidence only against the exact version observed before provider validation.
- A concurrent configuration change during validation fails with `CARRIER_CONFIGURATION_CHANGED_DURING_VALIDATION` and clears trust.
- Runtime readiness requires successful validation and fixed-time equality between persisted current and validated fingerprints.
- Save-without-validate, failed validation, provider timeout, rotation, environment/endpoint/config changes, and provider unavailability fail closed.

## 6. F-04 Implementation — Runtime Service-Level Authority

Implemented one runtime policy in `CarrierIntegrationAuthority.GetServiceLevelReadinessAsync`:

- requires a non-empty service-level code;
- requires support by the current adapter metadata;
- enforces tenant-scoped `CarrierServiceLevelEnablement` when an explicit policy exists;
- treats an explicit policy as an allow-list;
- does not use `CarrierAccount` credentials, health, or connection state as carrier integration authority.

Enforcement points:

1. Shipping-method preview/admission.
2. Warehouse-resolution selection and revalidation.
3. Final checkout shipping-method validation.
4. Shipment creation and carrier execution/retry.

Carrier code and service level are included in warehouse-resolution input evidence, so mutation invalidates an earlier decision.

## 7. F-05 Implementation — Immutable Order Execution Snapshot

Implemented:

- Added `Order.ShippingServiceLevel`.
- Checkout snapshots carrier code, service level, shipping-method identity/name, delivery promise, price, and fulfillment warehouse onto the order.
- Shipment creation no longer loads the mutable current `ShippingMethod` to redefine the purchase.
- Carrier execution uses the order/shipment snapshot while still requiring current carrier credentials, provider readiness, and service-level safety.
- A later shipping-method service-level edit or deactivation does not rewrite or invalidate the purchased service by itself.

Behavioral regression proves an order bought as Bosta Express remains Express after the current method is changed to Standard and deactivated.

## 8. F-06 Implementation — Safe Warehouse Migration

Migration `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`:

- backfills only when a tenant has exactly one active, non-deleted default warehouse;
- joins by tenant and never crosses tenant boundaries;
- preserves valid existing warehouse assignments;
- deactivates unresolved, ambiguous, or inactive-warehouse methods;
- appends an operator-visible remediation marker to the deactivated method description;
- adds `CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse`;
- adds the order service-level snapshot and carrier fingerprint columns.

Application invariants additionally reject:

- activating a method without an active, tenant-owned warehouse;
- cross-tenant/inactive warehouse assignment;
- deactivating or deleting a warehouse still used by an active method unless the methods are deactivated in the same unit of work.

The real PostgreSQL migration test rolls a scratch database back to the pre-Step-2N migration, seeds unambiguous, ambiguous, inactive-warehouse, and already-valid rows, reapplies Step 2N, verifies resulting data, verifies migration history/check constraint, and proves the database rejects reactivation without a warehouse.

## 9. F-07 Test Remediation

The audit failures were reproduced before remediation. Root causes included:

- webhook fixtures asserting Mylerz behavior even though canonical capability marks it unavailable;
- shipment retry fixtures omitting carrier authority and immutable order execution evidence;
- a manual-fulfillment source-token assertion rather than runtime behavior.

Remediation:

- webhook tests now use Bosta with real `CarrierIntegrationAuthority` trust resolution;
- shipment safety tests use immutable Express order evidence and current readiness checks;
- manual fulfillment is verified behaviorally without `CarrierWarehouseAssignment`;
- stale active-method fixtures now include valid tenant-owned fulfillment warehouses;
- the expanded Step 2N behavioral gate passes 80/80.

F-07 is not fully closed because broader required internal suites remain red. See sections 14, 18, and 19.

## 10. UI/API Changes

- Carrier configuration presents mutually coherent states: Not configured, Credentials saved, Connected, Ready for checkout, and Blocked/provider unavailable.
- Webhook secret input is write-only and appears only for canonically supported webhook carriers.
- The UI never hydrates an existing secret.
- Shipping Methods and Carrier Integration remain separate workflows.
- Storefront shipping DTOs carry carrier/service-level display evidence, but selection authority remains the opaque warehouse resolution.
- `POST /api/ShippingMethods/calculate` is explicitly marked no-store and compatibility-quote-only.
- The legacy storefront GET remains display compatibility only; checkout uses preview/select plus warehouse resolution.
- `ShippingQuoteService` remains quote-only and cannot persist checkout/order authority.

## 11. Migration Impact

Schema changes:

- `Orders.ShippingServiceLevel varchar(50) NULL`.
- `CarrierConfigurations.CredentialConfigurationFingerprint varchar(64) NULL`.
- `CarrierConfigurations.LastValidatedConfigurationFingerprint varchar(64) NULL`.
- `CarrierConfigurations.WebhookSecret` widened to 500 characters for ciphertext.
- active-method warehouse check constraint.

Operational impact:

- legacy active methods are never assigned a guessed warehouse;
- ambiguous/invalid rows become visibly inactive;
- legacy order service levels are backfilled only from same-tenant linked methods;
- legacy plaintext webhook secrets are encrypted at startup; failure aborts startup.

## 12. Security Impact

- No plaintext webhook-secret persistence remains after startup remediation.
- No webhook secret is returned in API/UI DTOs.
- Webhook signature material is hashed before persistence.
- Validation success cannot survive credential/configuration rotation.
- Tenant-scoped lookups use explicit tenant predicates with query-filter bypass only where required for authority resolution.
- No raw `X-Tenant-Id` value becomes persistence authority; storefront controllers reconcile the request header with trusted tenant context/claims.

## 13. Authority Verification

Verified conclusions:

- `CarrierConfiguration` plus `CarrierIntegrationAuthority` remain credential/readiness authority.
- `CarrierAccount` is not used for credentials or provider connection readiness. Its identifier is used only to locate existing service-level policy rows and origin-readiness administration records.
- `CarrierWarehouseAssignment` is not checkout warehouse authority.
- Browser-provided warehouse/carrier/service-level/weight/price never overrides final checkout.
- Checkout resolves the warehouse through `WarehouseResolution` and revalidates/locks it before reservation.
- Shipment execution contains no current `ShippingMethods` query; it consumes order/shipment snapshots.
- Compatibility GET/query/quote paths are presentation-only.

## 14. Test Matrix

| Gate | Result | Evidence |
|---|---:|---|
| Step 2N focused/expanded backend | PASS | 80 passed, 0 failed |
| Real PostgreSQL migration scenario | PASS | 1 passed, 0 failed; rollback, legacy seed, reapply |
| Storefront | PASS | 27 files, 283 tests |
| Shipping Administration | PASS | 1 file, 13 tests |
| Shipping/carrier/shipment broad name filter | **FAIL** | 286 passed, 34 failed, 320 total |
| Web full suite | **FAIL** | 757 passed, 10 failed, 767 total |
| .NET architecture partition | **FAIL** | numerous pre-existing source/document/missing-artifact assertions |
| .NET non-architecture/non-integration partition | **FAIL** | numerous pre-existing finance/CMS/SQLite/payment/inventory failures |
| Monolithic .NET suite | **TIMEOUT/HANG** | no completion/TRX after approximately 32 minutes; partitioned runs exposed failures |

The 34 broad shipping-filter failures are predominantly older source-text/document freezes and missing historical artifacts. Several explicitly require shipment execution to reload the current mutable shipping method, which contradicts F-05 and must be replaced with behavioral immutable-snapshot coverage rather than edited to force green.

## 15. Build Matrix

| Build | Result |
|---|---:|
| `dotnet build PointOfSale.sln --no-restore` | PASS; 0 errors, 84 existing warnings |
| `PointOfSale.Storefront npm run build` | PASS |
| `PointOfSale.Web npm run build` | PASS |
| `git diff --check` | PASS; line-ending notices only |

## 16. EF Verification

- Initial pre-migration pending-model check correctly detected changes.
- Migration was scaffolded once and then hand-hardened for safe legacy data handling.
- Final `dotnet ef migrations has-pending-model-changes`: `No changes have been made to the model since the last migration.`
- Real PostgreSQL scratch migration: PASS.
- Representative existing-data migration: PASS.

## 17. Worktree Before/After Evidence

Baseline:

- 93 status entries: 56 tracked modified, 37 untracked.
- Diff stat: 56 files, 4,899 insertions, 1,333 deletions.

Immediately before creating this report:

- 107 status entries: 65 tracked modified, 42 untracked.
- Diff stat: 65 files, 5,572 insertions, 1,417 deletions.

Final state after creating this report:

- 108 status entries: 65 tracked modified, 43 untracked.
- Tracked diff stat remains 65 files, 5,572 insertions, 1,417 deletions; the report is an additional untracked file and therefore is not included in `git diff --stat`.

Step 2N-created files:

- `PointOfSale.API/Services/Shipping/CarrierWebhookSecretBackfillHostedService.cs`
- `PointOfSale.Infrastructure/Migrations/20260811215156_SHOPIXY_ShippingClosureRemediationStep2N.cs`
- `PointOfSale.Infrastructure/Migrations/20260811215156_SHOPIXY_ShippingClosureRemediationStep2N.Designer.cs`
- `PointOfSale.Tests/Integration/ShippingClosureMigrationIntegrationTests.cs`
- `PointOfSale.Tests/Shipping/CarrierIntegrationAuthorityRemediationTests.cs`
- this report

Step 2N-owned edits, some of which were already dirty at baseline and contain preserved prior work:

- carrier configuration API/authority/capability/UI and `Program.cs`;
- checkout DTO, snapshot builder, checkout pricing/order persistence;
- Storefront pricing, shipping-method admission, warehouse resolution, and DTO admission;
- order/carrier configuration entities and EF model/snapshot;
- shipment creation/carrier execution;
- compatibility quote documentation/headers;
- focused shipping, webhook, migration, pricing, idempotency, shipment, warehouse, currency, and stale-fixture tests.

All other baseline changes remain pre-existing/user-owned. No attempt was made to normalize, revert, stage, commit, or push them.

## 18. Remaining Blockers

1. The broad shipping/carrier/shipment filter remains red: 34 failures. These include stale source-token assertions for mutable-method execution, old documentation freezes, and missing historical migration files.
2. The full Web suite remains red: 10 failures in unrelated i18n, cart/auth stability, warehouse source certification, affiliate source assertions, checkout mocks, and footer authoring.
3. The repository-wide .NET suite remains red across CommerceHub finance, CMS, checkout fields, SQLite schema compatibility, payments, analytics, inventory, and architecture freezes.
4. The monolithic .NET invocation does not complete reliably in one host; partitioned invocations complete and prove the gate is red.
5. Because internal gates are red, production/browser/live-provider verification cannot be the only remaining work.

These are real certification blockers. They are not downgraded to warnings and were not hidden by weakening backend invariants.

## 19. Final Certification

**BLOCKED**

The F-01 through F-06 production remediations and the originally identified critical behavioral failures are implemented and locally green. Migration safety, EF convergence, frontend shipping gates, focused backend gates, and the canonical authority chain have positive evidence.

Shipping Methods cannot be certified CLOSED because F-07's required broader internal verification remains red. The next closure action is to replace the remaining stale shipping source/document certifications with behavioral assertions consistent with immutable order shipping snapshots, restore missing historical test artifacts or retire their invalid freezes, and recover the unrelated repository-wide red gates before rerunning the complete suite.

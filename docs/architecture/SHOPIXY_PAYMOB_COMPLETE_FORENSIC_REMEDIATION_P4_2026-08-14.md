# SHOPIXY Paymob Complete Forensic Remediation P4

Date: 2026-08-14  
Evidence boundary: current dirty working tree  
Provider contract: Paymob Intention API and Egypt Unified Checkout  
Overall decision: **SOURCE REMEDIATION VERIFIED; LIVE SANDBOX AND PRODUCTION CERTIFICATION WITHHELD**

## Executive Decision

This pass remediates the material Paymob source findings identified across the POS Storefront and CommerceHub Marketplace lanes without merging their credential authorities.

The POS Storefront lane now resolves only encrypted tenant payment-method configuration, requires the modern Paymob credential shape, creates an Intention, redirects only to the official Unified Checkout host, verifies fixed-field HMAC-SHA512 evidence, and admits lifecycle changes only after tenant, provider, environment, replay, and finality checks.

The CommerceHub lane now resolves only Super Admin provider configuration through its canonical provider adapter. It no longer depends on global Paymob options in `CommerceHubPaymentService`, does not fall back into POS payment intents during callback processing, and protects durable Paymob session and webhook artifacts. CommerceHub refund, capture, and void capability is denied until a modern provider contract for those operations is separately certified.

No live Paymob transaction was attempted. The supplied POS public-key material was inconsistent across the available handoff, no separate CommerceHub credentials were supplied, and public callback/webhook delivery was not available for a safe provider drill. No provider success is inferred from source or test evidence.

## Certification Matrix

| Scope | Decision | Evidence boundary |
| --- | --- | --- |
| POS architecture | Verified | Tenant configuration remains the sole POS credential authority; Runtime and lifecycle boundaries remain explicit. |
| POS modern contract | Verified from source and mocks | Intention creation and official Unified Checkout URL construction are covered by focused tests. |
| POS security | Verified from source and focused tests | HMAC, tenant isolation, environment admission, replay handling, secret redaction, and protected durable artifacts are enforced. |
| POS live Sandbox | Withheld | Credential ambiguity and absent public provider delivery prevented a safe live drill. |
| CommerceHub architecture | Verified for initiation and evidence handling | Super Admin configuration is the lane authority; POS callback fallback and global-option service dependency were removed. |
| CommerceHub refund/capture/void | Not supported | Capability is fail-closed until Paymob's modern operation contract is provider-certified. |
| CommerceHub live Sandbox | Withheld | No separate CommerceHub credentials were supplied. |
| Full repository regression | Not green | The full suite timed out after unrelated failures in the pre-existing dirty working tree. |
| Production deployment | Not certified | Live Sandbox, public ingress, operational latency, and target-environment health evidence remain absent. |

## Lane Authority

### POS Storefront

The POS lane has the following authority chain:

```text
TenantPaymentMethod.ConfigEncrypted
  -> PaymobTenantConfigurationResolver
  -> scoped one-shot credential handoff
  -> PaymobPaymentAdapter
  -> POST https://accept.paymob.com/v1/intention/
  -> https://eg.checkout.paymob.com/?publicKey=...&clientSecret=...
  -> callback/webhook HMAC evidence
  -> Payment Runtime admission
  -> PaymentLifecycleService
```

Required tenant configuration is `publicKey`, `secretKey`, `integrationId`, and `hmacSecret`. Legacy `apiKey` and `iframeId` configuration is migration-required and cannot be enabled as a modern configuration.

PaymentIntent execution additionally requires configured absolute callback and webhook URLs. Operational readiness decrypts and validates an enabled tenant configuration for create, callback, and webhook capabilities rather than treating global defaults as runtime authority.

### CommerceHub Marketplace

The CommerceHub lane has the following authority chain:

```text
Super Admin payment-provider configuration
  -> PaymentProviderAdministrationService
  -> PaymobAdapter
  -> Paymob Intention API
  -> protected CommerceHub checkout session
  -> CommerceHub-only callback/webhook correlation
  -> CommerceHub payment and financial workflow authority
```

`CommerceHubPaymentService` no longer accepts or reads `CommerceHubPaymobOptions`. It receives canonical provider adapters and general CommerceHub payment orchestration options only. This removes the residual global Paymob options dependency from the active service.

Legacy CommerceHub Paymob configuration is marked migration-required and disabled until modern credentials are supplied. CommerceHub callback handling cannot search or mutate POS `PaymentIntent` records.

## Remediation Summary

### Modern Provider Contract

- Replaced the legacy auth/order/payment-key/IFrame create flow with `POST /v1/intention/`.
- Uses `Authorization: Token <secret-key>` for Intention creation.
- Constructs only the official Egypt Unified Checkout URL.
- Validates the checkout host before returning browser handoff material.
- Persists Intention identity as durable evidence instead of storing client-secret metadata.
- Extracts client-secret material only inside the protected runtime handoff path.
- Fences ambiguous or duplicate Intention creation through durable checkpoint state.

### Finality and Money Movement

- Added one `PaymobTransactionFinalityClassifier` for POS and CommerceHub transaction evidence.
- Authorization-only evidence is not treated as collection.
- POS authorization-only evidence maps to `Authorized` and cannot complete collection.
- CommerceHub authorization-only evidence remains pending and records authorization evidence.
- Terminal provider declines map to failure.
- Voided transactions map to cancellation.
- Captured transactions map to successful collection.
- Refund evidence arriving before local capture/payment authority requests retry or reconciliation instead of becoming a false payment failure.
- CommerceHub refund execution returns explicit `not_supported` and advertises no unsupported capability.

### Tenant and Lane Isolation

- Tenant resolution is provider-scoped and requires a unique enabled Paymob match.
- Callback and webhook execution re-resolve current tenant configuration.
- Environment, operation capability, provider code, and tenant identity are checked before execution.
- CommerceHub callback correlation no longer falls back to POS payment intents.
- CommerceHub initiation resolves Super Admin configuration through the canonical adapter.
- The remaining unused global Paymob options dependency was removed from `CommerceHubPaymentService`.

### Durable Artifact Protection

- POS checkout and webhook artifacts use `IPaymobSensitiveArtifactProtector`.
- CommerceHub checkout session response data, payment logs, and unresolved webhook payloads are protected.
- Startup backfill now covers legacy POS and CommerceHub Paymob durable artifacts.
- Backfill fails closed when an artifact cannot be safely protected rather than silently certifying plaintext retention.
- Tracked-source secret-pattern scanning found no Paymob secret-key-shaped value outside tests.

### Administration and Readiness

- POS Admin validates and encrypts modern tenant credentials before save or enablement.
- Super Admin validates modern Paymob key shape and environment-specific secret replacements.
- Stored legacy CommerceHub configuration reports `MigrationRequired` and remains disabled.
- Storefront payment-method visibility depends on executable tenant runtime readiness.
- Health checks validate real enabled tenant configuration for create, callback, and webhook paths.
- Repository defaults remain fail-closed and contain no usable provider credentials.

## Verification Evidence

Validation executed on 2026-08-14 against the current working tree:

| Validation | Result |
| --- | --- |
| Final `FullyQualifiedName~Paymob` backend suite | **172 passed, 0 failed** |
| Earlier focused Paymob/payment backend aggregate | **198 passed, 0 failed** |
| `CommerceHubPaymentServiceTests` after final authority cleanup | **11 passed, 0 failed** |
| `PaymobTransactionFinalityTests` | **8 passed, 0 failed** |
| Storefront payment authority slice | **15 passed, 0 failed** |
| POS Web payment-method slice | **3 passed, 0 failed** |
| Expanded payment architecture/service aggregate | **236 passed, 2 failed**; both failures are stale non-Paymob source/certification assertions described below. |
| `dotnet build PointOfSale.sln --no-restore` | Passed with 0 errors; 305 existing analyzer warnings were emitted on the final rebuild. |
| Storefront production build | Passed |
| POS Web production build | Passed |
| CommerceHub production build | Passed; large-chunk warning remains non-blocking. |
| EF pending-model check | No changes since the last migration. |
| `git diff --check` | No whitespace errors; line-ending conversion warnings only. |
| Tracked Paymob secret-key pattern scan | No matches outside tests. |

No Paymob migration was created because the EF model has no pending changes. An unrelated untracked migration already present in the dirty worktree was not modified or used as Paymob evidence.

## Full-Suite Result

The full command `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-build --no-restore` did not complete within the three-minute validation window. It had already reported multiple failures unrelated to the Paymob remediation, including:

- missing historical certification markers and stale source-text assertions;
- SQLite test setup failing because `btrim` is unavailable;
- checkout concurrency tests missing the newer `X-Pos-Cart-Id` contract;
- inventory reservation seed data no longer matching SQL catalog authority;
- a CMS seeder test expecting rows that were not created;
- unrelated CommerceHub, storefront affiliate, payout, navigation, and promotion authority assertions.

These failures prevent a claim that the entire repository is green. They do not contradict the separately green focused Paymob suite, but they remain a release-level repository risk that must be resolved or baselined by the owners of those systems.

The final expanded payment filter reproduced two of those baseline assertion failures while passing 236 tests:

- `PaymentEndToEndAuthorityCertificationTests.BackendReadCheckoutSessionAndFinancialRuntime_MustOwnPaymentMethodSessionIntentPricingAndSqlPersistence` expects an outdated source fragment.
- `StorefrontPaymentAuthorityCertificationTests.CertificationArtifacts_MustRecordP2DTruth` expects a historical `PASS P2-D` documentation marker.

## Sandbox Decision

### POS Storefront

No live Sandbox request was sent. The available credential handoff contained inconsistent public-key material, and selecting one value without authoritative confirmation could bind a secret key to the wrong public identity. Credentials were not printed or persisted during this pass.

POS live Sandbox certification requires:

1. One authoritative matching Sandbox public key, secret key, integration ID, and HMAC secret supplied through the approved secret path.
2. An enabled encrypted Storefront tenant Paymob configuration.
3. Public HTTPS callback and webhook endpoints registered with Paymob.
4. Green target-environment Paymob readiness health.
5. Provider-delivered create, success, decline, authorization-only, webhook, callback, replay, duplicate, recovery, and refund-evidence drills.

### CommerceHub Marketplace

No separate CommerceHub credentials were supplied. POS credentials must not be reused for this lane. CommerceHub live initiation, callback, webhook, and tenant-isolation evidence therefore remains unavailable.

Refund, capture, and void must remain disabled even after credentials are provisioned until their modern Paymob API contract, authentication, idempotency, and response finality are independently certified.

## Residual Risks

| Risk | Severity | Disposition |
| --- | --- | --- |
| No live POS provider evidence | High, external | Blocks live Sandbox and production certification. |
| No CommerceHub credentials or live evidence | High, external | Blocks CommerceHub live certification. |
| Inconsistent supplied POS public-key material | High, external | Do not attempt live use until corrected by an authority. |
| Full repository suite not green | High | Requires independent baseline and remediation outside this Paymob scope. |
| CommerceHub advanced operations unsupported | Medium | Correctly fail-closed; requires separate provider certification. |
| Legacy artifact backfill not exercised against a production-sized database | Medium, operational | Run with backup, staging rehearsal, monitoring, and rollback planning. |
| CommerceHub dependency audit reports 4 moderate and 3 high findings | Medium | Triage and upgrade separately; production build currently passes. |
| Build emits 305 existing analyzer warnings | Low | No compiler errors; warning debt remains repository-wide. |

## Deployment Gates

Production deployment must remain blocked until all applicable gates are evidenced:

1. Authoritative, lane-specific credentials are provisioned through approved secret storage.
2. Public callback and webhook endpoints are configured and provider-reachable.
3. Target health checks are green with enabled encrypted tenant/provider configuration.
4. Artifact backfill is rehearsed and completed without plaintext residue.
5. Provider-delivered HMAC, replay, duplicate, success, decline, authorization, and refund evidence is captured.
6. Operational latency, alerts, dashboards, and reconciliation paths are measured in the target environment.
7. Repository-wide test failures are either fixed or accepted through a documented release baseline.
8. CommerceHub dependency vulnerabilities are triaged before production release.

## Final Posture

```text
PAYMENT RUNTIME BOUNDARY: PRESERVED
POS PAYMOB MODERN SOURCE IMPLEMENTATION: VERIFIED
POS PAYMOB FOCUSED REGRESSION: GREEN
POS PAYMOB LIVE SANDBOX: NOT CERTIFIED
COMMERCEHUB PAYMOB INITIATION/EVIDENCE SOURCE PATH: VERIFIED
COMMERCEHUB PAYMOB REFUND/CAPTURE/VOID: NOT SUPPORTED
COMMERCEHUB PAYMOB LIVE SANDBOX: NOT CERTIFIED
FULL REPOSITORY REGRESSION: NOT GREEN
PRODUCTION DEPLOYMENT: NOT CERTIFIED
```

The correct next operational step is not additional source optimism. It is authoritative credential correction, lane-specific secret provisioning, public ingress setup, and a measured provider-delivered Sandbox drill with no cross-lane credential reuse.

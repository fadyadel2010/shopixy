# FAWRY-CERTIFICATION-CLOSURE-4 — Webhook Credential Authority

Date: 2026-07-15  
Mission finding: `FAWRY-FINAL-003` only, as defined by the Closure-4 mission  
Scope: Fawry webhook authentication and tenant credential/configuration admission  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Preserved and unchanged

## Finding Identifier Note

The mission identifies webhook credential authority as `FAWRY-FINAL-003`.
`FAWRY_FINAL_RECERTIFICATION.md` labels the same webhook-resolver finding
`FAWRY-FINAL-004`; its `FAWRY-FINAL-003` text concerns the separate outbound
charge mutation checkpoint. This closure follows the mission's explicit title,
objective, named components, fail-closed matrix, and output questions. It changes
only webhook credential authority. It does not review, implement, or close the
charge-checkpoint finding or any later finding.

## Certification Decision

The Board's substantive webhook credential-authority finding was **valid**.

Before this closure, `FawryWebhookController` performed a private
`TenantPaymentMethods` query, decrypted `ConfigEncrypted` itself, and extracted
`webhookSecret`. That controller-local path did not require current active tenant
authority, `IsEnabled`, the soft-delete state, `SupportsOnline`, tenant/application
environment agreement, global provider enablement, callback/webhook execution
flags, callback/webhook certification, configured server-notification capability,
or registered callback/webhook endpoint governance.

The controller was therefore a second credential authority. A disabled,
deleted, inactive-tenant, environment-ineligible, or webhook-ineligible Fawry
configuration could reach protocol verification and possible Runtime/lifecycle
processing.

## Official Documentation Evidence

Official FawryPay documentation was re-read on 2026-07-15:

- [Server To Server Notification V2](https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/server-notification-v2)
- [Create Payment Requests Using FawryPay Reference Number](https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis)

The official Notification V2 contract establishes that:

- FawryPay sends status changes to a merchant callback endpoint configured
  during merchant account setup;
- the merchant supplies notification endpoints for development and production
  environments;
- `messageSignature` is a SHA-256 digest over the documented notification fields
  plus the merchant secure key;
- HTTP 200 acknowledges delivery; a non-200 response causes configured retries.

The reference-charge contract also exposes `orderWebHookUrl` as an optional
server-owned order notification URL and uses the merchant account code and secure
key for authenticated provider requests. These facts require server-side current
merchant/environment/endpoint governance before the secure key is admitted for
notification verification. The webhook payload is evidence; it is not credential,
tenant, environment, or endpoint authority.

## Current Implementation Review

### `FawryTenantConfigurationResolver`

This is now the only Application-layer authority that may read and decrypt the
current tenant Fawry configuration.

The new `WebhookAuthentication` capability and
`ResolveWebhookAuthorityAsync(...)` compose one bounded authority result from:

- current active, non-deleted SQL tenant authority;
- the current tenant-owned `FAWRY` payment-method row;
- enabled, non-deleted, online-capable provider state;
- tenant `IsSandbox` agreement with the admitted application environment;
- current global provider enablement and credential/environment validation gates;
- callback and webhook execution feature flags;
- callback and webhook certification capabilities;
- configured-server-notification capability;
- explicitly configured absolute callback and webhook endpoints;
- decrypted and validated tenant merchant code, API key, and secure key.

The returned `FawryTenantWebhookAuthority` contains the tenant and payment-method
identity, tenant merchant code, tenant secure key, admitted environment, sandbox
state, callback URL, and webhook URL. Global option credentials are not read,
compared, returned, or used as fallback.

### `FawryWebhookRuntimeIntegrationService`

Webhook authentication now begins by calling
`IFawryTenantConfigurationResolver.ResolveWebhookAuthorityAsync(...)`. The
integration verifies that the returned tenant authority matches the request
tenant, then supplies only the resolver-admitted secure key to the existing
webhook evidence adapter.

The public `FawryWebhookRuntimeAdmissionRequest` no longer accepts a secure key.
Callers therefore cannot inject alternate, static, fallback, controller-owned, or
provider-local credentials. Resolver rejection occurs before prior-evidence
loading, duplicate lookup, local payment resolution, evidence-adapter execution,
Runtime execution, replay persistence, or lifecycle request projection.

### `FawryWebhookEvidenceAdapter`

No source change was required. It remains the protocol boundary that parses the
official callback, reconstructs the documented Notification V2 signature input,
computes SHA-256 with the supplied one-request secure key, and compares in fixed
time. It does not query tenant persistence, options, configuration, environment,
or fallback credentials.

### `FawryWebhookController`

The obsolete private credential query/decryption method and Data Protection
dependency were removed. The controller retains only bounded ingress,
Application-integration delegation, transaction/replay/audit coordination, and
entry into the existing lifecycle writer after exact Application/Runtime
admission.

The constructor change is the minimum dependency correction required to remove
controller credential authority. Routes, request/response DTOs, frontend,
business rules, lifecycle semantics, and persistence schema are unchanged.

## Implemented Correction

Production source modified only in the permitted webhook Application boundary:

- `PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryWebhookRuntimeIntegrationService.cs`
- `PointOfSale.API/Controllers/FawryWebhookController.cs` only to remove its
  credential lookup/decryption dependency and secure-key request injection

Focused certification and compatibility tests modified or added:

- `PointOfSale.Tests/Architecture/FawryCertificationClosure4Tests.cs`
- `PointOfSale.Tests/Architecture/FawryContractClosure2CertificationTests.cs`
- `PointOfSale.Tests/Architecture/FawryPaymentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Payments/FawryWebhookReliabilityTests.cs` constructor
  compatibility only

Explicitly unchanged:

- `PointOfSale.Core/Payments/Runtime/**`
- `FawryRuntimeBridge`
- `FawryPaymentAdapter`
- `FawryWebhookEvidenceAdapter` protocol logic
- payment lifecycle services and business rules
- SQL entities, EF mappings, migrations, and database schema
- frontend, DTOs, CommerceHub, and provider execution

No new permanent cross-provider architectural truth was discovered. The existing
Application credential-authority, webhook evidence-adapter, fail-closed,
environment, replay, frozen Runtime, and single-writer rules already govern the
correction. Therefore no `.ai-context` file is modified by this closure.

## Fail-Closed Certification

| Condition | Result | Downstream execution |
|---|---|---|
| Missing trusted tenant | `FAWRY_CONFIGURATION_MISSING` | No evidence adapter, Runtime, replay, or lifecycle execution |
| Deleted/inactive tenant | `FAWRY_CONFIGURATION_MISSING` | No evidence adapter, Runtime, replay, or lifecycle execution |
| Missing tenant Fawry configuration | `FAWRY_CONFIGURATION_MISSING` | No fallback and no downstream execution |
| Deleted tenant configuration | `FAWRY_CONFIGURATION_DELETED` | No downstream execution |
| Disabled tenant provider | `FAWRY_PROVIDER_DISABLED` | No downstream execution |
| Online capability disabled | `FAWRY_CAPABILITY_DISABLED` | No downstream execution |
| Callback/webhook flag, certification, or endpoint configuration disabled | `FAWRY_CAPABILITY_DISABLED` | No downstream execution |
| Sandbox/Production mismatch | `FAWRY_ENVIRONMENT_MISMATCH` | No downstream execution |
| Missing, corrupt, malformed, or invalid tenant credentials | `FAWRY_CREDENTIALS_INVALID` | No downstream execution |
| Notification signature/credential mismatch | `FAWRY_WEBHOOK_SIGNATURE_INVALID` | Rejected evidence; no lifecycle or replay authority |

## Focused Certification Tests

`FawryCertificationClosure4Tests` proves:

- tenant merchant, secure key, environment, and webhook registration resolve as
  one bounded authority;
- valid global credentials cannot replace a missing tenant configuration;
- missing and deleted/inactive tenants fail closed;
- deleted configuration, disabled provider, wrong environment, invalid secure
  key, and disabled webhook capability fail closed;
- resolver rejection occurs before evidence-adapter or Runtime entry and creates
  no replay or webhook-log state;
- a wrong notification signature is rejected without lifecycle or replay
  authority;
- the controller has no configuration, options, persistence, or Data Protection
  credential lookup;
- the webhook admission request cannot carry a caller-selected secure key;
- the evidence adapter remains protocol-only and free of credential-resolution
  dependencies.

## Regression

Executed on 2026-07-15:

| Validation | Result |
|---|---:|
| Closure-4 + Fawry Contract Closure 2 + Fawry application-contract gate | **35 passed, 0 failed, 0 skipped** |
| Test-project/API compilation | **Passed, 0 errors**; 180 repository-existing warnings |
| Frozen Payment Runtime slice | **94 passed, 0 failed, 0 skipped** |
| Complete Fawry aggregate | **130 passed, 7 failed, 0 skipped** |

The complete aggregate retains the same seven previously documented unrelated
failures:

- four `FawryWebhookReliabilityTests` cannot seed the stale local PostgreSQL
  schema because `Orders.BillingAddressAdmissionEvidenceId` is absent;
- three `FawryPaymentAuthorityCertificationTests` retain historical assertions
  for superseded charge/reference source strings and the intentionally absent
  permanent `PASS SF-PAY7` context marker.

The relevant webhook authority assertion in the historical certification test was
updated from the removed controller lookup to the canonical resolver path. The
remaining seven failures are not caused by Closure-4 and do not exercise the new
authority path. Focused webhook credential-authority regression and frozen Runtime
regression are green; an unqualified whole-Fawry aggregate remains withheld for
the documented pre-existing conditions.

## Final Certification Matrix

| Question | Decision |
|---|---|
| Was the Board finding valid? | **Yes.** The controller was a second, incomplete credential authority. |
| Canonical tenant authority certified? | **Yes, for Fawry webhook authentication.** |
| Webhook credential resolution certified? | **Yes.** Merchant and secure key originate only from the current tenant row through `FawryTenantConfigurationResolver`. |
| Environment validation certified? | **Yes.** Tenant and admitted application Sandbox/Production state must agree. |
| Deleted configuration rejected? | **Yes.** Deleted/inactive tenant and deleted tenant payment-method configuration are both rejected. |
| Disabled provider rejected? | **Yes.** Tenant provider state and webhook execution/certification gates fail closed. |
| Fallback removed? | **Yes.** The controller lookup and secure-key request field are removed; no global, static, environment-default, options credential, or provider-local fallback exists. |
| Regression passed? | **Yes for Closure-4, affected application/webhook boundaries, compilation, and frozen Runtime. Whole-Fawry aggregate remains withheld only for seven documented unrelated failures.** |
| Runtime preserved? | **Yes. 94/94 frozen Runtime tests pass; Runtime source is unchanged.** |
| Architecture preserved? | **Yes. Application owns authentication/configuration admission, the webhook evidence adapter owns protocol verification, Runtime owns canonical payment authority, and the lifecycle service remains the single writer.** |
| Recommend Closure-5? | **Yes, only as a separately bounded review of the next Board-assigned finding. The charge-checkpoint finding was not touched by this closure.** |

## Closure Decision

The mission-defined `FAWRY-FINAL-003` webhook credential-authority finding is
**CLOSED at source**. Fawry webhook authentication now has one canonical
Application-layer tenant authority, fails closed before protocol/Runtime/lifecycle
execution when authority is absent or ineligible, and preserves the frozen
Payment architecture.

This decision does not claim Sandbox validation, deployment readiness, live
provider acceptance, whole-provider certification, or closure of any other Board
finding.

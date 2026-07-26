# FEDEX-5 - Operational Readiness Certification

Date: 2026-07-07
Scope: FedEx operational readiness only
Status: COMPLETE

## Executive Summary

FEDEX-5 reviewed only FedEx operational readiness against the frozen Shipping
Platform and the prior FedEx certifications: FEDEX-0, FEDEX-1,
FEDEX-RUNTIME-1, FEDEX-2, FEDEX-3, and FEDEX-4.

No transport execution, OAuth execution, HttpClient, shipment execution,
tracking execution, label execution, pickup execution, runtime redesign,
geography redesign, customs redesign, or Shipping Platform redesign was
implemented.

Final verdict: **NOT READY**.

FedEx has official operational prerequisites and official environments, but the
PointOfSale FedEx rollout is not operationally ready because repository evidence
does not prove sandbox execution, production approval, managed production
credentials, published FedEx carrier mappings, origin/destination/customs
snapshot coverage, monitoring dashboards, alerting, audit evidence, rate-limit
controls, token lifecycle execution, or an approved deployment checklist.

The certified Shipping Platform fully models FedEx operational requirements. No
new Shipping Platform capability is required.

## Official Sources

Official FedEx sources reviewed:

- FedEx API Authorization:
  https://developer.fedex.com/api/en-us/catalog/authorization/v1/docs.html
- FedEx Sandbox Virtualization Guide:
  https://developer.fedex.com/api/en-us/guides/sandboxvirtualization.html
- FedEx Quotas and Rate Limits Guide:
  https://developer.fedex.com/api/en-us/guides/ratelimits.html
- FedEx Integration Best Practices:
  https://developer.fedex.com/api/en-us/guides/best-practices.html
- FedEx Developer Portal Support/FAQs:
  https://developer.fedex.com/api/en-us/support.html

## Operational Checklist

| Gate | Required evidence | FEDEX-5 status |
|---|---|---|
| Sandbox availability | Official test URI and virtualized sandbox support | Verified from official docs |
| Sandbox proof | Credentialed sandbox OAuth and API smoke-test result | Missing |
| Production onboarding | Developer Portal organization, account association, project production movement, terms/validation where applicable | Officially required; local proof missing |
| OAuth credentials | Client ID/API Key and Client Secret/Secret Key, plus child credentials when grant type requires them | Not verified |
| Secret storage | Vault or equivalent managed server-side secret storage | Not verified |
| Environment separation | Sandbox and production base URI separation, no mixed credentials | Config surface exists; operational proof missing |
| Carrier Mapping | Published FedEx mapping release for operation/provider/environment/account/country scope | Missing |
| Warehouse origin | Admitted shipment origin snapshots for rollout warehouses | Not proven |
| Destination snapshot | Admitted destination snapshots for rollout orders | Not proven |
| Customs snapshot | Published/admitted customs evidence and shipment customs snapshots for cross-border | Not proven |
| Address validation | Optional provider evidence only; contingency when unavailable | Boundary certified; operational process missing |
| Monitoring | FedEx request, response, auth, throttle, error, and admission telemetry | Missing |
| Logging | Redacted operational logs with FedEx error code evidence and no secrets | Not proven |
| Audit | Activation, credential rotation, mapping publication, smoke test, rollback audit | Missing |
| Rate limiting | Project rate-limit and OAuth token-threshold controls | Not implemented |
| Token lifecycle | Server-side token cache, expiry handling, 401 regeneration | Not implemented |
| Rollback | Disable FedEx without altering platform domains or data | Procedure required; not proven |

## Sandbox Readiness

FedEx officially provides separate test and production API URIs:

- Sandbox/test: `https://apis-sandbox.fedex.com/`
- Production: `https://apis.fedex.com/`

FedEx also documents sandbox virtualization, predefined responses, JSON API
collections, negative test cases, and virtualized coverage for Ship, Basic
Integrated Visibility, Address Validation, Pickup Request, Service Availability,
Rate, Global Trade, and related APIs.

Operational finding: sandbox availability is verified, but sandbox readiness is
not certified because no credentialed sandbox OAuth token generation, sandbox
request, sandbox response, negative test, provider alert, or operator-signed
smoke-test artifact exists in this repository.

Sandbox production blocker: **sandbox proof absent**.

## Production Readiness

FedEx official support guidance states that FedEx APIs require Developer Portal
project terms, shipping-account End User License Agreement evidence where
applicable, FedEx accounts associated to the organization, and at least one
account added before API projects can be moved to production. Some APIs may also
require basic validation or shipping-label validation.

Operational finding: production onboarding is officially available, but
production readiness is not certified because there is no local evidence of:

- FedEx Developer Portal organization approval.
- FedEx account association and verification.
- Production project movement approval.
- DPLA/EULA acceptance evidence.
- Label validation evidence where required.
- Production FedEx credentials in managed secret storage.
- Production smoke-test approval.

Production activation must fail closed until those artifacts exist.

## OAuth Requirements

FedEx APIs use OAuth 2.0 bearer-token authentication. Official credential
requirements include:

- `grant_type`
- `client_id` / API Key
- `client_secret` / Secret Key
- `child_key` / child credential where the selected grant type requires it
- `child_secret` / child password where the selected grant type requires it

Official grant types reviewed:

- `client_credentials`
- `csp_credentials`
- `client_pc_credentials`

Official token response fields include `access_token`, `token_type`,
`expires_in`, and `scope`. FedEx documents `token_type` as bearer and
`expires_in: 3600`, with access tokens expiring after one hour.

Operational finding: OAuth credential requirements are understood, but OAuth
credentials are not verified and OAuth execution is intentionally not
implemented. Production activation must fail closed when any required credential
or child credential is absent.

## Credential Management

FedEx official best practices require API Key and Secret Key to be treated
securely, not distributed through email or client-side JavaScript, not hardcoded,
and dynamically updateable. FedEx advises recreating the Secret Key immediately
if credentials are compromised.

Operational requirement:

- Store FedEx client ID, client secret, account number, and child credentials
  server-side only.
- Separate sandbox and production credentials.
- Rotate credentials through an audited operator process.
- Never expose credentials or access tokens to the browser.
- Never log client secrets, child secrets, bearer tokens, raw Authorization
  headers, or full provider payloads containing secret material.

Current status: not operationally proven.

## Secrets

Required secret strategy before activation:

- Managed vault or equivalent platform secret store.
- Environment-scoped secret names.
- Least-privilege runtime access.
- Rotation runbook.
- Compromise response runbook.
- Redacted diagnostics.
- Deployment validation that rejects plaintext or missing secrets.

Current status: secrets are unmanaged for FedEx operational rollout. Production
activation must fail closed.

## Carrier Mapping Readiness

FedEx is a registered provider identity, so the certified GEO-9 Carrier
Geographic Resolution boundary applies before any future FedEx provider I/O.

Operational requirement:

- Published FedEx carrier mapping release exists.
- Release is scoped to provider, operation, environment, tenant/account, and
  country coverage.
- Runtime shipment registration snapshots the exact mapping release before
  provider I/O.
- Recovery reuses the shipment mapping snapshot and never remaps to a newer
  release.

Current status: no published FedEx Carrier Mapping release coverage is proven.
Production activation must fail closed when Carrier Mapping is unpublished.

## Geographic Readiness

FEDEX-3 certified that the existing Geographic Platform fully models FedEx
geographic requirements. FedEx Address Validation remains optional provider
evidence and never becomes Geographic Authority.

Operational requirement:

- Active published catalog release for rollout countries.
- Active published FedEx carrier mapping release for rollout scope.
- Destination snapshots admitted before shipment registration.
- Warehouse-origin snapshots admitted before shipment registration.
- Address Validation unavailability contingency exists.

Current status: operational coverage is not proven.

## Warehouse-Origin Readiness

Shipment Origin authority remains Warehouse-owned admitted origin evidence,
snapshotted by Shipment before carrier I/O.

Operational requirement:

- Each rollout warehouse has admitted origin evidence.
- Origin snapshot exists before FedEx registration.
- Origin contact/phone/address fields meet FedEx request-contract validation.
- Recovery uses `OriginSnapshotJson` only.

Current status: rollout warehouse-origin coverage is not proven.

## Customs Readiness

FedEx cross-border shipment execution must consume only carrier-neutral Customs
DTO evidence from an admitted Shipment Customs Snapshot.

Operational requirement:

- Published Customs Evidence exists for cross-border products.
- Shipment Customs Snapshot exists before FedEx provider I/O.
- Commercial invoice/export evidence requirements are satisfied where required.
- Missing customs evidence blocks cross-border activation.

Current status: cross-border Customs rollout coverage is not proven.

## Address Validation Readiness

FedEx Address Validation is suggestion/evidence only. FedEx official best
practices warn not to make shipping dependent on optional Address Validation and
require a contingency when it is unavailable.

Operational requirement:

- Address Validation may be used only as optional evidence.
- Provider-normalized output cannot rewrite canonical address authority.
- Operators have a fallback path when Address Validation is unavailable.
- Address validation errors are logged as provider evidence without exposing PII
  beyond the approved operational log policy.

Current status: boundary is certified; operational process is not proven.

## Monitoring

Required monitoring before activation:

- FedEx OAuth token requests, successes, failures, and 401 regenerations.
- FedEx API request counts by environment, tenant, account, operation, and
  outcome.
- FedEx HTTP status classes, provider error codes, warnings, and alerts.
- Rate-limit and quota events: `429`, quota-exceeded diagnostics, and retry
  scheduling.
- OAuth token-generation threshold events and `403` threshold penalties.
- Carrier response admission failures.
- Unknown enum/status/schema failures.
- Mapping/origin/destination/customs gate failures.
- Sandbox and production smoke-test outcomes.

Current status: no FedEx-specific operational monitoring proof exists.

## Logging

Required logging before activation:

- Structured operational logs with correlation IDs, shipment ID, carrier request
  ID, tenant/account scope, environment, operation, FedEx transaction ID where
  available, provider error code, and admission result.
- Redaction for secrets, bearer tokens, Authorization headers, client secrets,
  child secrets, account credentials, and excessive PII.
- Error-code based handling rather than dynamic message matching.
- Warnings and notes captured for operator review without treating them as
  failures by default.

Current status: not proven for FedEx operational rollout.

## Audit

Required audit before activation:

- Credential creation, rotation, deactivation, and compromise response.
- Environment activation/deactivation.
- Carrier Mapping publication and rollback.
- Geographic publication and rollback.
- Warehouse-origin admission.
- Customs publication/admission.
- Sandbox smoke-test approval.
- Production smoke-test approval.
- Production activation and rollback.
- Operator identity and timestamp for every checklist approval.

Current status: no FedEx operational audit artifact exists.

## Rate Limiting

FedEx officially documents:

- Daily organization and project/capability quotas.
- A project rate limit of 1,400 transactions per 10 seconds.
- OAuth token-generation thresholds applied by public IP address.
- Burst threshold: 3 token-generation hits per second continuously for 5
  seconds.
- Average threshold: 1 token-generation hit per second continuously for 2
  minutes.
- Threshold penalties can return `403 Forbidden` for 10 minutes and can be
  extended by continued violations.

Operational requirement:

- Cache OAuth tokens for their lifetime.
- Do not request a token per carrier operation.
- Bound concurrent FedEx calls below official rate limits.
- Back off and alert on `429`.
- Back off and alert on OAuth threshold `403`.

Current status: not implemented because OAuth and transport execution are
outside this phase.

## Token Lifecycle Requirements

FedEx tokens expire after one hour. FedEx best practices recommend storing
tokens on the web application server only, never exposing them to browsers, and
avoiding multiple token calls by caching the token until a `401` response is
observed, then regenerating.

Operational requirement:

- Server-side token cache keyed by environment/project/account/credential scope.
- Expiration-aware token use.
- 401-triggered regeneration.
- Single-flight token refresh to avoid token storming.
- Token redaction in logs and traces.
- Token invalidation on credential rotation.

Current status: not implemented and therefore not operationally ready.

## Deployment Checklist

Production activation is rejected until every item below is complete:

- [ ] FedEx sandbox project created.
- [ ] FedEx sandbox credentials stored in managed secrets.
- [ ] Sandbox OAuth smoke test passed.
- [ ] Sandbox Ship validation/create negative and positive tests passed.
- [ ] Sandbox Track/Pickup/Service Availability/Address Validation tests passed
      where those features are in rollout scope.
- [ ] FedEx production organization and account association verified.
- [ ] FedEx production project approval complete.
- [ ] DPLA/EULA/validation requirements complete.
- [ ] Production credentials stored in managed secrets.
- [ ] FedEx carrier mapping release published for rollout scope.
- [ ] Geographic catalog release published for rollout scope.
- [ ] Warehouse origin snapshots available for rollout warehouses.
- [ ] Destination snapshots available for rollout orders.
- [ ] Customs snapshots available for cross-border rollout.
- [ ] Monitoring dashboards and alerts deployed.
- [ ] Redacted logging policy verified.
- [ ] Audit checklist signed by operator.
- [ ] Rate-limit and token-lifecycle controls deployed.
- [ ] Rollback procedure tested.
- [ ] Production smoke test approved.

## Blocking Issues

| Blocker | Reason production activation is rejected |
|---|---|
| OAuth credentials absent or unverified | FedEx requires client ID/API Key and client secret/Secret Key, plus child credentials for child grant types |
| Sandbox proof absent | Official sandbox exists, but no credentialed sandbox smoke-test artifact exists |
| Production approval absent | No evidence of FedEx account association, production project movement, terms, or validation approval |
| Carrier Mapping unpublished | FedEx provider mapping release coverage is not proven |
| Origin snapshots unavailable | Rollout warehouse-origin coverage is not proven |
| Destination snapshots unavailable | Rollout destination snapshot coverage is not proven |
| Customs snapshots unavailable for cross-border | Cross-border Customs coverage is not proven |
| Secrets unmanaged | No FedEx managed secret/vault evidence exists |
| Operational checklist incomplete | Monitoring, logging, audit, rate-limit, token-lifecycle, smoke-test, and rollback controls are incomplete |
| Transport/OAuth execution absent | Intentional previous-phase boundary; no FedEx provider I/O can be production active |

## Regression Coverage

Added operational readiness regression coverage in:

- `PointOfSale.Tests/Architecture/FedExOfficialContractCompatibilityTests.cs`

Coverage verifies:

- FEDEX-5 freezes operational gating.
- Credential gating rejects production activation without OAuth credentials.
- Configuration gating requires sandbox/production separation and managed
  secrets.
- Carrier Mapping gating requires published FedEx mapping coverage.
- Sandbox gating requires credentialed sandbox proof.
- Production gating requires production approval and smoke-test evidence.
- No HTTP execution is introduced.
- No OAuth execution is introduced.
- The final verdict remains `NOT READY`.

## Verification

Repository verification:

- FedEx adapter remains fail-closed with `FEDEX_NOT_YET_CERTIFIED`.
- FedEx OAuth client remains fail-closed and performs no token request.
- FedEx runtime files contain no `HttpClient`, `IHttpClientFactory`,
  `SendAsync`, `PostAsync`, or `GetAsync` execution.
- FedEx contract DTOs remain standalone validation/serialization code only.
- No backend workflow, controller, shipment execution, tracking execution,
  label execution, pickup execution, frontend surface, runtime redesign,
  geography redesign, customs redesign, or Shipping Platform redesign was
  introduced.
- No `.ai-context` file was updated because FEDEX-5 found FedEx-specific
  operational findings only and no new permanent cross-provider architectural
  truth.

Operational verification:

- Official sandbox availability: verified.
- Official production onboarding requirements: verified.
- OAuth credential requirements: verified.
- Token expiration policy: verified, one hour / `expires_in: 3600`.
- Token refresh policy: verified, cache server-side and regenerate on 401.
- Environment separation: verified by official separate URI guidance.
- Configuration validation: required, not operationally proven.
- Secret storage strategy: required, not operationally proven.
- Carrier Mapping publication readiness: not ready.
- Warehouse-origin readiness: not ready.
- Shipment snapshot readiness: not ready.
- Customs readiness: not ready for cross-border.
- Address validation readiness: boundary ready, process not proven.
- Operational monitoring requirements: defined, not implemented.
- Logging requirements: defined, not proven.
- Audit requirements: defined, not proven.
- Rollback strategy: required, not proven.
- Operational smoke-test plan: defined, not executed.

## Final Recommendation

Do not activate FedEx production traffic.

Approve FEDEX-5 as an operational-readiness certification with final verdict
`NOT READY`. The next phase should be an explicitly authorized FedEx transport
and response-admission implementation plan only after operational prerequisites
are assigned to owners. Production rollout must remain blocked until sandbox
proof, production approval, managed credentials, mapping publication,
origin/destination/customs coverage, monitoring, logging, audit, rate limiting,
token lifecycle, rollback, and smoke tests are complete.

## Final Verdict

**NOT READY**

FedEx operational deployment is not ready because mandatory operational evidence
is missing: OAuth credentials are absent/unverified, sandbox proof is absent,
production approval is absent, FedEx Carrier Mapping is unpublished,
warehouse-origin/destination/customs snapshot coverage is not proven, secrets
are unmanaged, monitoring/logging/audit/rate-limit/token-lifecycle controls are
not proven, and the operational checklist is incomplete.

## Special Review

Does FedEx operational deployment require any new platform capability not
already represented by the certified Shipping Platform?

No.

The certified Shipping Platform fully models FedEx operational requirements:
carrier credentials, environment separation, shipment origin snapshots,
destination snapshots, published carrier mapping releases, shipment mapping
snapshots, customs snapshots for cross-border, carrier-neutral DTOs, adapter
boundaries, response admission, lifecycle policy, SQL truth, audit, recovery,
monitoring, logging, and operational fail-closed gates are all represented by
existing certified platform concepts.

No new platform domain or Shipping Platform capability is required.

## AI Context Update Decision

No `.ai-context` files were updated.

FEDEX-5 discovered FedEx-specific operational rollout requirements only. It did
not discover new permanent cross-provider architectural truth.

## Explicit Success Answers

- Reviewed ONLY FedEx Operational Readiness? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No production backend code modified; regression tests and this report only.**
- Frontend modified? **No.**
- Sandbox ready? **No. Official sandbox availability is verified, but sandbox proof is absent.**
- Production ready? **No.**
- OAuth credentials verified? **No.**
- Carrier Mapping ready? **No.**
- Operational checklist complete? **No.**
- Production rollout approved? **No.**
- Runtime hardening required? **No runtime hardening in FEDEX-5; future transport/OAuth implementation remains required before activation.**
- New Shipping Platform capability required? **No.**
- Recommend next phase: **FedEx transport and response-admission implementation planning after operational owners accept the FEDEX-5 checklist; do not activate production.**

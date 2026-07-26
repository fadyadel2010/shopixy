# DHL-5 - Operational Readiness Certification

Status: Ready after operational checklist
Date: 2026-07-07
Scope: DHL operational readiness only

## Executive Summary

DHL development is complete for the certified runtime boundary. The implementation preserves DHL as a MyDHL wire adapter behind backend shipment authority, warehouse-origin admission, published carrier geographic mapping, canonical customs evidence, SQL shipment lifecycle, and tenant carrier configuration.

No new runtime production blocker was discovered in this audit. No runtime, backend, frontend, DTO, entity, adapter, Customs, Geography, or Warehouse redesign was performed.

The system is not yet directly certifiable as live production-ready because operational evidence is still missing: credentialed DHL sandbox replay, production account entitlement proof, account-enabled country/service coverage, active published DHL carrier mapping releases, warehouse-origin admission coverage for shipping warehouses, published customs evidence coverage for international products, migration deployment proof, and smoke-test evidence.

Final verdict: **READY AFTER OPERATIONAL CHECKLIST**.

## Operational Checklist

| Area | Certification |
|---|---|
| Sandbox readiness | Structurally ready; not certified without live DHL sandbox credentials and replay evidence |
| Production readiness | Structurally ready; not certified without production account entitlement and rollout evidence |
| Credential model | Ready; tenant carrier config stores encrypted API key/secret, account ID, sandbox flag, endpoint/config metadata |
| Authentication | Ready; DHL adapter uses MyDHL Basic authentication with API key and secret |
| Account configuration | Checklist required; account ID is mandatory, but actual account validity is external evidence |
| Environment switching | Ready; sandbox and production base URLs are selected by `UseSandbox` |
| Warehouse-origin readiness | Runtime ready; rollout requires admitted warehouse-origin evidence for every shipping warehouse |
| Carrier Mapping publication | Runtime ready; rollout requires active published DHL mapping release per environment/account/country |
| Carrier Resolution | Ready; shipment registration resolves published mapping before adapter invocation and snapshots the release |
| Shipment registration | Runtime ready; SQL shipment and idempotency claim are persisted before provider I/O |
| Tracking | Ready; unknown DHL status evidence fails closed |
| Labels | Ready; DHL label document content is admitted as carrier evidence on shipment result |
| Recovery | Runtime shape ready; broad shared shipping recovery regressions remain documented separately |
| Audit | Ready; origin admission and carrier resolution emit geographic audit entries; webhook processing persists logs |
| Observability | Ready at application log/telemetry level; production dashboards/alerts still need rollout proof |
| Deployment readiness | Checklist required; migration/application proof is not present in repository evidence |
| Secrets | Checklist required; encrypted storage is implemented, external secret rotation/expiry proof is operational |
| Retries | Ready with bounded HTTP retry policy for safe requests; shared registration recovery tests still need owner follow-up |
| Timeouts | Ready; named DHL HTTP client is configured with a 15 second timeout |

## Account Readiness

Required DHL account facts before enabling production traffic:

- Valid MyDHL API key.
- Valid MyDHL API secret.
- Valid DHL Express account number / account ID.
- Confirmed sandbox access for `https://express.api.dhl.com/mydhlapi/test`.
- Confirmed production access for `https://express.api.dhl.com/mydhlapi`.
- Account-enabled origin countries, destination countries, products, billing terms, label format, and customs requirements.
- Known account-specific rate limits and service restrictions.

The runtime validates presence of API key, API secret, and account ID before DHL calls. It cannot certify that credentials are valid, unexpired, entitled for a specific country/service, or approved for production. That requires live DHL validation and operator evidence.

Sandbox certified: **No, structurally ready only**.
Production account certified: **No, structurally ready only**.

## Mapping Readiness

DHL carrier mapping is runtime-gated by active published carrier geographic mapping releases. `CarrierShipmentService` resolves published mapping for the selected `UseSandbox` environment and tenant-provider account scope before adapter invocation, snapshots release identity/version, and fails closed when no admissible mapping exists.

GEO-10 remains the controlling operational truth: repository evidence did not prove published DHL mapping coverage. Therefore DHL cannot be activated for production traffic until the operations team publishes DHL mapping releases for every enabled country/service/account scope and verifies release activation.

Carrier Mapping ready: **Runtime ready; operational publication required**.
Published mapping releases: **Not certified from repository evidence**.

## Warehouse Readiness

SHIP-ORIGIN-1 is enforced. Carrier-managed DHL registration requires an order-bound active warehouse, warehouse-owned admitted address evidence, active canonical place evidence, active country profile, and immutable shipment origin snapshot before provider I/O.

Operational rollout must prove every DHL-enabled warehouse has admitted warehouse-origin evidence and complete contact/address data. Existing orders without a warehouse or with incomplete admission intentionally fail closed.

Warehouse-origin ready: **Runtime ready; coverage proof required**.

## Customs Readiness

CUSTOMS-1 through CUSTOMS-4 are enforced as frozen authority. Cross-border DHL registration requires an immutable `ShipmentCustomsSnapshot` produced from approved and published Customs Evidence before the adapter is invoked. DHL serializes carrier-neutral customs DTO evidence into `exportDeclaration`; it does not own HS code, value, origin country, units, commodity, invoice, or customs admission authority.

Operational rollout must prove published customs evidence coverage for all internationally shippable products/variants and country/service combinations. Unsupported optional DHL customs requirements remain limitations until admitted by a future Customs phase.

Customs ready: **Runtime ready; evidence publication coverage required**.

## Deployment Readiness

Deployment checklist before DHL production enablement:

- Apply geographic catalog and carrier mapping migrations.
- Apply warehouse-origin shipment snapshot migration.
- Apply customs evidence, customs workflow, and shipment customs snapshot migrations.
- Confirm DHL `CarrierConfiguration` exists per tenant with `IsEnabled = true` only after evidence is complete.
- Configure encrypted DHL API key, API secret, account ID, sandbox flag, and capability flags.
- Keep DHL webhooks disabled unless an official, account-specific MyDHL webhook contract is certified.
- Publish active DHL carrier mapping release for the target environment/account/country.
- Admit warehouse-origin evidence for every shipping warehouse.
- Approve and publish customs evidence for every international product/variant.
- Run sandbox create, tracking, label, fail-closed, and recovery smoke tests.
- Record production account entitlement and rollout approval.
- Confirm logging, telemetry, alerting, timeout, and retry dashboards.
- Confirm rollback plan: disable DHL carrier config and mapping release without mutating shipment history.

Deployment ready: **No, checklist evidence required**.

## Compatibility Matrix

| Capability | DHL-5 verdict | Notes |
|---|---|---|
| Domestic shipment create | Structurally ready | Requires credentials, origin admission, destination geography, mapping release |
| International shipment create | Structurally ready | Requires all domestic gates plus published customs snapshot |
| Tracking polling | Ready | Unknown statuses fail closed |
| Labels | Ready | DHL label data is stored as response evidence |
| Cancellation | Unsupported | Adapter returns `DHL_SHIPMENT_CANCELLATION_UNSUPPORTED` |
| Webhooks | Not production-certified | Parser fails closed, but official MyDHL webhook contract is not certified |
| COD | Account/service dependent | Must be validated against DHL account/product rules |
| Country support | Account/service dependent | Requires account-enabled coverage and published mapping release |
| Rate quotes | Structurally present | Live sandbox/account validation still required |
| Recovery/retry | Conditionally ready | Shared shipping regression failures remain outside DHL adapter scope |

## Known Limitations

- No credentialed DHL sandbox replay was performed in this audit.
- No DHL production account entitlement evidence was present.
- No repository evidence proves active published DHL mapping releases.
- No repository evidence proves full warehouse-origin admission coverage.
- No repository evidence proves full published customs coverage for all international SKUs.
- DHL cancellation remains unsupported by this MyDHL REST integration.
- DHL webhooks remain not production-certified without an official account-specific contract.
- DHL optional customs/account requirements beyond the canonical modeled evidence are omitted rather than guessed.
- Country restrictions, service availability, COD, billing terms, and rate limits remain DHL account-specific.

## Blocking Issues

No new runtime blocker was found.

Operational blockers before production rollout:

1. Credentialed DHL sandbox certification is missing.
2. DHL production account entitlement certification is missing.
3. Active published DHL carrier mapping release evidence is missing.
4. Warehouse-origin admission coverage evidence is missing.
5. Published customs evidence coverage for international shipments is missing.
6. Migration deployment evidence is missing.
7. Smoke-test and observability evidence is missing.

These are operational rollout blockers, not development blockers.

## Regression Results

Focused DHL operational readiness slice:

Command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~DhlOfficialContractCompatibilityTests|FullyQualifiedName~DhlGeographicCompatibilityTests|FullyQualifiedName~DhlRuntimeAuthorityTests|FullyQualifiedName~ProviderMappingCoverageProductionReadinessTests|FullyQualifiedName~ShipmentOriginAuthorityTests|FullyQualifiedName~Customs" -nologo -v minimal --no-restore
```

Result: **PASS**, 56 passed, 0 failed.

Broad Shipping regression screen:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Shipping -nologo -v minimal --no-build --no-restore
```

Result: **FAILED**, 82 passed, 5 failed.

Regression classification:

| Failing regression | Classification | DHL certification impact |
|---|---|---|
| `ShipmentExternalRegistrationSafetyTests.ExecuteCarrierForExistingShipmentAsync_RetryAfterSuccess_DoesNotCreateDuplicateCarrierShipment` | Out Of Scope | Shared shipping recovery regression/test fixture; not DHL-specific and not a new DHL runtime blocker |
| `ShipmentExternalRegistrationSafetyTests.ExecuteCarrierForExistingShipmentAsync_WhenExternalSuccessWasNotPersisted_ReconcilesByCarrierRequestIdWithoutDuplicateCreate` | Out Of Scope | Shared shipping recovery regression/test fixture now hitting warehouse-origin fail-closed prerequisite; not DHL-specific |
| `ShippingWebhookTrustPipelineTests.ProcessWebhookAsync_ValidSignature_ProcessesWebhook_AndPersistsCompletedLog` | Out Of Scope | Shared webhook trust pipeline; DHL webhooks are not production-certified |
| `ShippingWebhookTrustPipelineTests.ProcessWebhookAsync_UppercaseHexSignature_IsAcceptedCaseInsensitively` | Out Of Scope | Shared webhook trust pipeline; DHL webhooks are not production-certified |
| `CommerceHubFinanceServiceTests.CreateRefundAsync_ShippingOnlyRefund_ShouldNotReverseCommission` | Out Of Scope | CommerceHub finance/accounting authority; unrelated to DHL operational readiness |

Only In Scope failures may affect DHL certification. There were no In Scope DHL failures in the focused readiness slice.

## Verification

Verified from frozen architecture reports and runtime code:

- DHL adapter is registered.
- DHL named HTTP client has bounded timeout/retry policy.
- DHL base URLs separate sandbox and production.
- MyDHL Basic authentication uses API key and API secret.
- API key, API secret, and account ID are required before DHL calls.
- Tenant carrier configuration gates enabled/inactive carrier state.
- Carrier credentials are decrypted in memory from tenant configuration.
- Shipment registration persists SQL shipment and carrier request ID before provider I/O.
- Warehouse-origin admission happens before provider I/O.
- Carrier geographic mapping resolves from active published release before provider I/O.
- Cross-border customs snapshot admission happens before provider I/O.
- DHL adapter serializes request evidence and does not query SQL/geography/customs services.
- Unknown DHL tracking and webhook statuses fail closed.
- Labels and tracking are admitted as provider evidence only.
- Recovery reuses immutable shipment-origin, mapping, and customs snapshots.
- Missing credentials, origin, mapping, customs, inactive carrier, wrong environment mapping, invalid account, and missing configuration fail closed before or during admission.

## Final Verdict

**READY AFTER OPERATIONAL CHECKLIST**

Development Complete: **Yes**. DHL runtime implementation, authority boundaries, customs serialization, geographic consumption, tracking admission, label parsing, timeouts, and focused regression coverage are complete.

Operational Complete: **No**. Live DHL sandbox/account validation, production entitlement proof, mapping publication, warehouse-origin coverage, customs publication coverage, migration deployment evidence, smoke tests, and observability proof are still required.

Production Rollout: **Blocked until the operational checklist is completed**. Once the checklist evidence exists, DHL can be enabled tenant-by-tenant and country-by-country without redesigning runtime.

## Success Answers

- Reviewed ONLY Operational Readiness? **Yes.**
- Runtime files modified? **No.**
- Backend files modified? **No.**
- Frontend files modified? **No.**
- Sandbox certified? **No; structurally ready, live credentialed replay required.**
- Production account certified? **No; account entitlement evidence required.**
- Warehouse-origin ready? **Runtime ready; operational coverage proof required.**
- Carrier Mapping ready? **Runtime ready; published DHL release evidence required.**
- Customs ready? **Runtime ready; published evidence coverage required.**
- Deployment ready? **No; migration/config/secrets/smoke/observability checklist evidence required.**
- Blocking issues? **No new runtime blocker; operational rollout blockers remain.**
- Runtime hardening required? **No.**
- Operationally ready? **Ready after operational checklist.**
- Development complete? **Yes.**
- Recommend next phase: **DHL credentialed sandbox certification and controlled production rollout evidence collection.**

## AI Context Update Decision

The `.ai-context` files were not updated. DHL-5 discovered no new permanent architecture truth; it confirmed operational rollout prerequisites under the already frozen DHL, Customs, Geography, and Shipment Origin architecture.

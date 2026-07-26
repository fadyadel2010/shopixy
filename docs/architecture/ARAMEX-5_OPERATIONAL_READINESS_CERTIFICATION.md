# ARAMEX-5 — Operational Readiness Certification

Audit date: 2026-07-06  
Scope: Aramex operational readiness only  
Final verdict: **NOT READY**

## Executive Summary

The Aramex adapter and shared Shipping runtime are structurally ready for
credentialed operational certification: request admission, official
`ClientInfo` authentication, fixed sandbox/production host selection, shipment
registration, pull tracking, labels, immutable mapping/origin evidence,
recovery, timeout policy, audit, and fail-closed behavior are implemented.

Aramex is **not ready for production activation** because no live Aramex
sandbox or production account evidence, deployed database evidence, admitted
warehouse coverage report, registered provider evidence, or published and
activated Aramex Carrier Mapping release was supplied. Frozen GEO-10 evidence
establishes repository-proven Aramex mapping coverage at 0%.

This audit found and closed one real runtime blocker in carrier administration.
The credential-validation path omitted Aramex account PIN/entity/country
configuration, did not decrypt stored secrets, and treated HTTP success as
credential success even when Aramex returned `HasErrors=true`. Carrier secret
writes also bypassed the existing encryption service. Minimal hardening now
encrypts new secret writes, decrypts only at runtime, carries account-specific
configuration into validation, validates the complete `ClientInfo` before
network I/O, and rejects provider error envelopes.

That hardening makes account certification possible; it does not itself certify
an account or deployment.

## Operational Checklist

| Check | Result | Evidence / required closure |
|---|---|---|
| Adapter registered | Pass | `aramex` is registered behind the shared carrier registry |
| Sandbox host isolation | Pass in code | `UseSandbox` selects the frozen sandbox host; arbitrary `ApiUrl` is not trusted |
| Production host isolation | Pass in code | Production uses the frozen official host |
| Credential shape validation | Pass after hardening | Username, password, account number, PIN, entity, and country fail closed before I/O |
| Credential-at-rest handling | Pass for new writes after hardening | Admin writes encrypt API key/secret; runtime decrypts in memory; legacy plaintext remains readable |
| Live sandbox credentials | **Not certified** | No credentials or successful live contract run supplied |
| Live production credentials | **Not certified** | No controlled production validation supplied |
| Carrier enabled/configured | Not proven | No deployed tenant `CarrierConfigurations` evidence |
| Shipment registration | Pass in code; not live-certified | Resolver and origin admission execute before adapter I/O |
| Tracking | Pass in code; conditional | Pull tracking and identity/status admission exist; account-specific status samples remain required |
| Labels | Pass in code; not live-certified | URL and embedded label forms are admitted from the processed shipment |
| Recovery | Pass in code | Immutable origin and mapping snapshots are reused; no live remapping/re-origin |
| Cancellation | Unsupported | Fails closed; public contract does not expose shipment/AWB cancellation |
| General webhooks | Unsupported | Capability is not advertised |
| Mapping publication | **Blocked** | No repository/deployment evidence of an active Aramex publication |
| Warehouse-origin coverage | **Blocked** | Migration exists; no deployed migration or complete admitted-warehouse report |
| Logging/audit | Pass in code | Registration, origin admission/failure, mapping resolution, and adapter failures are logged/audited |
| Timeout | Pass in code | Named Aramex HTTP client uses 15 seconds |
| Retry | Intentional no blind adapter retry | Public Aramex create idempotency is not guaranteed |
| Deployment evidence | **Absent** | No migration history, configuration, secrets, connectivity, telemetry, or smoke-test artifact |

## Account Readiness

Aramex authentication is carried in the official `ClientInfo` envelope. The
required runtime values are:

- API username (`ApiKey`);
- password (`ApiSecret`);
- account number (`AccountId`);
- `AccountPin`;
- three-character `AccountEntity`;
- ISO alpha-2 `AccountCountryCode`;
- fixed runtime version/source values.

Sandbox and production are isolated by `UseSandbox` and distinct fixed hosts.
Each environment requires vendor-issued credentials and account-specific
product/country enablement. Configuration valid in sandbox must not be presumed
valid in production.

Credential validation is now fail-closed for missing/malformed configuration,
transport failure, non-success HTTP status, and a successful HTTP response with
an Aramex error envelope. No live account was available, so neither credential
expiry behavior nor account enablement is certified.

Account-specific assumptions still requiring evidence are enabled origin and
destination countries, DOM/EXP products, COD entitlement, label modes,
tracking status vocabulary, quotas, and timeout reconciliation procedures.
Pickup runtime, shipment cancellation, general webhooks, and guaranteed
idempotent create retries are unsupported.

## Mapping Readiness

The mapping resolver requires exact provider, Sandbox/Production environment,
tenant-provider-account, tenant, operation, country, freshness, review, release
publication, and latest activation evidence. Missing, stale, inactive,
unpublished, ambiguous, wrong-environment, wrong-account, or wrong-country
evidence fails before carrier I/O.

Frozen GEO-10 truth records:

- no repository-seeded Aramex `CarrierProvider`;
- no repository-published Aramex mapping release;
- no activated Aramex mapping rows;
- repository-proven Aramex coverage of **0%**.

No newer deployment artifact or database report was supplied. Therefore:

- Published Carrier Mapping: **not proven**;
- Active Mapping Release: **not proven**;
- country/account/environment coverage: **0% repository-proven**;
- operational geographic completeness: **not certified**.

## Warehouse Readiness

SHIP-ORIGIN-1 requires an order-bound tenant warehouse, a complete active
admitted `AddressAdmissionEvidence` record, supported country profile,
operational contact name/phone/street, and canonical catalog lineage.
Registration persists an immutable shipment origin snapshot. Recovery reuses
that snapshot and cannot fall back to carrier pickup configuration or current
warehouse data.

Migration `20260706120000_ShipOrigin1CanonicalShipmentOrigin` exists and adds
the origin columns, completeness constraint, and indexes. No deployment
migration history or warehouse coverage query was supplied. Warehouse-origin
readiness is therefore **not certified**.

## Deployment Readiness

Required before activation:

1. Apply and verify the Geographic, GEO-9, and SHIP-ORIGIN-1 migrations.
2. Register the Aramex provider through a governed deployment artifact.
3. Configure the tenant Aramex account with encrypted credentials and complete
   account PIN/entity/country JSON.
4. Keep the carrier disabled until sandbox validation succeeds.
5. Admit every shipping warehouse origin and prove zero missing/expired
   admission rows.
6. Publish and activate complete Aramex mappings separately for sandbox and
   production account scopes.
7. Run a database coverage report proving country/place coverage, freshness,
   review state, publication activation, ambiguity absence, and rollback state.
8. Run credentialed sandbox create, URL label, embedded label (if contracted),
   tracking-history, provider-error, timeout, and recovery/reconciliation
   drills.
9. Validate structured logs/audit events and alerts for credential, mapping,
   origin, registration, unknown tracking status, timeout, and recovery
   failures.
10. Perform a controlled production credential/connectivity smoke test without
    assuming sandbox product or country entitlements.

No feature flag dedicated to Aramex was found. `CarrierConfiguration.IsEnabled`
is the operational activation gate and must remain false until all checklist
evidence is approved.

## Compatibility Matrix

| Surface | Sandbox | Production | Verdict |
|---|---|---|---|
| Host switching | Implemented | Implemented | Code-ready |
| Authentication envelope | Implemented | Implemented | Code-ready; accounts unverified |
| Account validation | Hardened | Hardened | Live evidence absent |
| Create shipment | Contract-tested | Contract-tested | Live account/country certification absent |
| Tracking | Pull-based, fail-closed | Pull-based, fail-closed | Status samples required |
| Labels | URL/file supported | URL/file supported | Live label evidence absent |
| Mapping | Exact sandbox scope required | Exact production scope required | No published releases proven |
| Warehouse origin | Same admission authority | Same admission authority | Deployment coverage absent |
| Recovery | Snapshot-only | Snapshot-only | Structurally ready |
| Cancellation | Unsupported | Unsupported | Correctly fail-closed |
| Webhooks | Unsupported | Unsupported | Must remain disabled |

## Known Limitations

- Aramex publishes no accepted create-idempotency guarantee; blind create retry
  could duplicate AWBs.
- A timeout after provider acceptance requires carrier-request reconciliation
  or manual operational reconciliation.
- Tracking status codes are account/operation dependent; unknown codes are
  quarantined rather than guessed.
- Shipment cancellation and general webhooks are not supported.
- Pickup capability is described by the carrier definition but no shared
  pickup operation exists.
- The public contract does not establish tenant-specific products, quotas,
  retry schedules, or country enablement.
- Existing plaintext carrier secrets remain backward compatible; they should
  be rotated/resaved through the hardened path.

## Blocking Issues

1. No successful credentialed sandbox certification.
2. No certified production Aramex account or controlled production smoke test.
3. No proven registered/active Aramex provider in the target database.
4. No published and activated sandbox or production Aramex mapping release.
5. Repository-proven Aramex mapping coverage remains 0%.
6. No proof that required Geographic/GEO-9/warehouse-origin migrations are
   applied to the target database.
7. No admitted warehouse-origin coverage report.
8. No deployment proof for secrets, carrier configuration, connection,
   logging, telemetry, alerts, and reconciliation runbook.
9. The focused operational regression slice is not fully green because two
   older registration-safety fixtures do not create the now-mandatory admitted
   warehouse origin.

## Regression Results

| Verification | Result |
|---|---|
| `dotnet build PointOfSale.sln --no-restore` | Passed, 0 warnings, 0 errors |
| Focused Aramex contract/credential tests | Passed, 16/16 |
| Combined Aramex/mapping/origin/registration slice | 37 passed, 2 failed |

The two failures are in
`ShipmentExternalRegistrationSafetyTests`. Both reach the newer mandatory
warehouse-origin boundary without complete admitted origin fixtures; one
reports `Shipment registration requires a complete active admitted warehouse
origin snapshot.` This confirms fail-closed runtime behavior but leaves the
combined regression suite red until the fixtures are brought forward.

No live Aramex network call or production database mutation was performed.

## Verification

Static inspection verified:

- fixed environment hosts and named 15-second HTTP client;
- official account fields and request admission;
- encrypted admin credential writes and in-memory decryption;
- provider error-envelope admission;
- registry-only carrier resolution;
- mapping publication/activation and exact scope checks;
- origin admission before adapter I/O;
- immutable mapping and origin recovery snapshots;
- create response identity binding;
- tracking identity/status admission;
- URL and embedded labels;
- unsupported cancellation/webhook behavior;
- audit/logging paths and durable registration failure evidence.

No new architectural truth was discovered. The five canonical `.ai-context`
files were not updated.

## Final Verdict

**NOT READY**

The runtime is structurally capable of entering an operational certification
exercise after the credential blocker hardening, but production readiness
cannot be certified from code alone. Required account, mapping, warehouse,
database, deployment, telemetry, smoke-test, and fully green regression
evidence is absent. Aramex must remain disabled.

## Explicit Success Answers

- Reviewed ONLY Operational Readiness? **Yes.** Runtime was inspected only
  where needed to verify operational behavior and close one real blocker.
- Runtime files modified: **2** — `AdminCarrierConfigController.cs` and
  `AramexAdapter.cs`.
- Backend files modified: **2 runtime files plus 1 backend test file** —
  `AdminCarrierConfigController.cs`, `AramexAdapter.cs`, and
  `AramexOfficialContractCompatibilityTests.cs`.
- Frontend files modified: **None.**
- Sandbox certified? **No.**
- Production account certified? **No.**
- Warehouse-origin ready? **No; structurally implemented, deployment coverage
  unproven.**
- Carrier Mapping ready? **No; no published active release or coverage evidence
  was supplied.**
- Deployment ready? **No.**
- Blocking issues? **Yes; nine are listed above.**
- Runtime hardening required? **Yes; the credential administration/validation
  blocker was minimally hardened.**
- Operationally ready? **No.**
- Recommend next phase: **Operational data rollout and credentialed Aramex
  sandbox certification, followed by controlled production account smoke
  certification; no architecture redesign.**

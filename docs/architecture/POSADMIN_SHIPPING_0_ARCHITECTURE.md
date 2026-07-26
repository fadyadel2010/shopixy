# POSADMIN-SHIPPING-0 - POS Admin Shipping Administration Architecture

Date: 2026-07-07  
Scope: POS Admin Shipping Administration architecture only  
Status: Architecture certified

## Executive Summary

POS Admin Shipping Administration is the tenant-facing administrative control
plane for carrier configuration and operational shipping enablement. It owns
which certified carriers a tenant may use, whether each carrier is enabled,
which credentials and environment are active, which warehouses may ship through
each carrier, which service levels and operational policies are enabled, and
which carrier is the tenant default.

This phase does not implement frontend, API, backend runtime, carrier adapter,
SQL migration, credential storage, provider transport, checkout, shipment,
geography, customs, warehouse, storefront, affiliate, CommerceHub, or Super
Admin behavior.

Shipping Administration is layered on top of the certified Shipping Platform.
No certified platform domain must be redesigned to support this architecture.

## Preparation Review

Reviewed inputs:

- all `.ai-context` files were inspected for shipping, POS Admin, Super Admin,
  geography, customs, carrier, credential, secret, warehouse, and authority
  boundaries;
- `SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE`;
- `GEO-1.5` through `GEO-10` and the governing GEO architecture context;
- `SHIP-ORIGIN-1`;
- `CUSTOMS-1` through `CUSTOMS-4`;
- available Bosta certifications: `BOSTA-1`, `BOSTA-2`, and `BOSTA-2.5`;
- `ARAMEX FINAL`;
- `DHL FINAL`;
- `FEDEX FINAL`.

No repository file matching `*BOSTA*FINAL*` was found under
`docs/architecture`. The available Bosta certification chain was reviewed
instead.

## Authority Matrix

| Concern | POS Admin owns | POS Admin does not own |
|---|---|---|
| Carrier registration | Tenant carrier account/configuration registration for certified providers | Provider adapter certification, provider contracts, runtime shipment registration |
| Carrier enable/disable | Tenant activation and deactivation gates | Shipment lifecycle state or historical shipment truth |
| Carrier credentials | Tenant credential entry, rotation request, active credential selection, revocation workflow | Secret vault internals, decrypted secret exposure, provider credential validity beyond connection-test evidence |
| Sandbox / Production | Tenant environment selection per carrier account | Provider base URL constants, provider contract authority |
| Default carrier | Tenant default carrier policy | Runtime eligibility override when shipment evidence fails closed |
| Domestic policy | Tenant preference and allow/deny policy for domestic carrier selection | Canonical geography, carrier mapping publication, shipment lifecycle |
| International policy | Tenant preference and allow/deny policy for cross-border carrier selection | Customs Evidence, customs snapshots, export compliance authority |
| Warehouse-origin assignment | Mapping tenant warehouses to carrier accounts and service eligibility | Warehouse address lifecycle, admitted origin evidence, origin snapshots |
| Carrier Mapping usage | Selecting carriers that consume published mapping releases; viewing coverage and blockers | Editing canonical geography or carrier mapping releases |
| Operational health | Tenant-facing health status, last test result, blockers, warnings, and rollout readiness evidence | Provider truth, shipment truth, tracking truth |
| Connection testing | Controlled credential/environment reachability tests and structured result recording | Treating test success as shipment success or production entitlement proof |
| Shipping settings | Tenant operational settings for carrier-managed shipping | Platform business rules, order/payment/fulfillment authority |
| Service-level enablement | Tenant service-level allowlist after provider capability is certified | Inventing provider services or bypassing account entitlement |
| Credential rotation | Rotation workflow, cutover selection, audit, rollback to prior active credential where allowed | In-place mutation of historical shipment attempts or secret value exposure |
| Audit | Append-only administrative audit of carrier configuration decisions | Shipment audit, geographic audit, customs audit, provider response truth |
| Secrets ownership | Tenant owns credential intent and authorization; secret store owns secret custody | Plaintext secret storage in UI, logs, DTOs, projections, or audit |

## Ownership Matrix

| Administration object | Owner | Source of truth | Runtime consumer |
|---|---|---|---|
| Tenant carrier account | POS Admin | Tenant-scoped SQL carrier configuration | `CarrierShipmentService` eligibility gate |
| Enabled flag | POS Admin | Carrier configuration | Carrier registration precondition |
| Environment | POS Admin | Carrier configuration | Adapter base URL selection through certified adapter constants |
| Credentials | POS Admin intent, secret store custody | Encrypted/managed secret reference | Adapter authentication after backend retrieval |
| Default carrier | POS Admin | Shipping settings | Runtime selection only when shipment remains eligible |
| Domestic policy | POS Admin | Shipping settings | Carrier selection policy |
| International policy | POS Admin | Shipping settings | Carrier selection policy plus Customs preconditions |
| Warehouse assignment | POS Admin assigns; Warehouse owns warehouse lifecycle | Carrier/warehouse assignment record | Origin admission and shipment registration eligibility |
| Service-level enablement | POS Admin | Carrier service settings | Quote/registration eligibility after provider capability checks |
| Mapping coverage view | Geographic/Carrier Mapping owns data; POS Admin views and consumes | Published mapping release and coverage reports | Shipment mapping resolution |
| Health record | POS Admin | Administrative health/audit record | Operators and rollout gates |
| Audit record | POS Admin for admin actions | Append-only admin audit | Compliance and support |

## POS Admin Owns

POS Admin owns the Shipping Administration control plane:

- carrier account registration for already certified provider identities;
- carrier activation and deactivation;
- credential entry, secret reference selection, rotation, and revocation;
- sandbox or production environment selection;
- default carrier and fallback preferences;
- domestic and international carrier policies;
- warehouse-to-carrier assignment;
- service-level enablement;
- operational settings, readiness checks, and health display;
- connection testing as administrative evidence;
- administrative audit of configuration changes.

## POS Admin Does Not Own

POS Admin does not own:

- Carrier Mapping editing or publication authority in the shipping admin
  workflow;
- Canonical Geography, catalog identity, country profiles, localization,
  aliases, address admission, publication, or rollback;
- Customs Evidence, customs approval, customs publication, or Shipment Customs
  Snapshots;
- Shipment snapshots, shipment lifecycle, carrier request identity, recovery,
  tracking truth, labels, provider response admission, or webhook truth;
- Order state, checkout state, payment state, inventory state, warehouse
  lifecycle, product truth, or financial authority;
- provider wire contracts, provider response truth, provider entitlement truth,
  or platform business rules;
- plaintext secrets, secret vault internals, or decrypted credential exposure.

## Administration Workflow

```text
Register Carrier
  -> Enter Credentials
  -> Validate Connection
  -> Choose Environment
  -> Assign Warehouse
  -> Select Policies
  -> Activate
  -> Runtime Consumption
  -> Monitoring
  -> Credential Rotation
  -> Disable
```

1. Register Carrier: POS Admin creates a tenant carrier account for a certified
   provider code such as Bosta, Aramex, DHL, FedEx foundation, Mylerz where
   supported, or Manual.
2. Enter Credentials: POS Admin submits credential material into the approved
   secret custody boundary. The admin record stores only secret references,
   metadata, masked fingerprints, status, and audit evidence.
3. Validate Connection: backend performs a controlled provider-specific
   connection or structural validation test. A pass is health evidence only; it
   is not proof of shipment success, mapping coverage, warehouse origin
   coverage, customs coverage, or production entitlement.
4. Choose Environment: POS Admin selects Sandbox or Production for the tenant
   carrier account. Certified adapters own fixed base URL constants.
5. Assign Warehouse: POS Admin authorizes which tenant warehouses may use the
   carrier account. Warehouse still owns warehouse lifecycle and admitted
   address evidence.
6. Select Policies: POS Admin chooses domestic/international policies,
   fallback preferences, service levels, COD/insurance/pickup options where
   certified, and default carrier behavior.
7. Activate: POS Admin enables the carrier only when required readiness gates
   pass or accepted blockers are explicitly recorded.
8. Runtime Consumption: runtime reads carrier configuration as an eligibility
   input, then still requires shipment identity, origin snapshot, destination
   snapshot, published mapping snapshot, customs snapshot where required, and
   adapter result admission.
9. Monitoring: POS Admin sees health, last connection test, disabled reasons,
   mapping/origin/customs blockers, credential expiry, and recent administrative
   events.
10. Credential Rotation: POS Admin stages replacement credentials, validates
    them, chooses cutover, disables the prior credential when appropriate, and
    records append-only audit.
11. Disable: POS Admin disables future carrier usage. Existing shipment history
    remains unchanged and recovery continues from persisted shipment evidence.

## Carrier Lifecycle

Carrier lifecycle states for administration:

```text
Draft
  -> CredentialsPending
  -> ConnectionTestPending
  -> ReadyForActivation
  -> Active
  -> Suspended
  -> Disabled
  -> Retired
```

`Active` means POS Admin permits the carrier as a runtime candidate. It never
means runtime may bypass Geography, Warehouse-Origin, Customs, Carrier Mapping,
Shipment Lifecycle, adapter certification, provider response admission, or
fail-closed rules.

## Credential Ownership

POS Admin owns credential administration intent. Secret custody belongs to the
approved backend secret boundary.

Credential rules:

- plaintext credentials must never be returned to frontend, logs, audit,
  projections, health records, exports, or DTOs;
- audit stores actor, carrier, environment, operation, masked fingerprint,
  secret reference, status, correlation ID, and non-PII failure reason;
- rotation creates a new credential version or secret reference rather than
  mutating historical evidence;
- disabling or rotating credentials must not mutate historical shipment
  attempts or persisted carrier request identities;
- connection tests must be isolated from shipment creation unless a later
  runtime phase explicitly certifies a provider-specific validation operation.

## Warehouse Ownership

POS Admin owns carrier-to-warehouse assignment. Warehouse owns the warehouse
lifecycle, operational location, contact data, and admitted address evidence.
Shipment owns the immutable origin snapshot after registration begins.

Carrier assignment cannot make a warehouse shippable when SHIP-ORIGIN-1
preconditions fail. Runtime still requires an active tenant warehouse, admitted
warehouse address evidence, active canonical places, active supported country
profile, and a valid origin snapshot before provider I/O.

## Carrier Mapping Usage

Shipping Administration may display mapping readiness, missing coverage,
published release identity, environment/account scope, and blocking reasons.
It may not edit, publish, roll back, or repair Carrier Mapping inside the
Shipping Administration workflow.

Runtime mapping usage remains:

```text
Published mapping release
  -> CarrierGeographicMappingService
  -> Shipment mapping snapshot
  -> Carrier-neutral request evidence
  -> Adapter serialization
```

If mapping is missing, stale, unpublished, ambiguous, wrong environment, wrong
account, wrong country, or wrong tenant, runtime fails closed before adapter
I/O regardless of POS Admin activation.

## Operational Ownership

POS Admin owns the operational readiness view for tenant shipping:

- enabled/disabled state;
- last connection-test status;
- credential expiry or rotation warnings;
- warehouse-origin coverage blockers;
- mapping coverage blockers;
- customs evidence blockers for international shipping;
- provider operational limitations;
- disabled reasons and operator notes;
- recent administrative audit.

Provider operational truth remains evidence-bound. A green dashboard may
recommend activation only when certified prerequisites are present; it cannot
become Shipment, Geography, Customs, Tracking, or Provider authority.

## Audit

Shipping Administration audit is append-only and records:

- carrier registration;
- credential creation, rotation, revocation, and failed validation;
- environment changes;
- activation and deactivation;
- default carrier changes;
- domestic and international policy changes;
- warehouse assignment changes;
- service-level enablement changes;
- connection-test requests and results;
- operational setting changes;
- administrative override decisions with reason.

Audit must include tenant, actor, role/capability, carrier, environment,
warehouse where applicable, previous state, next state, timestamp, correlation
ID, and non-PII failure reason. It must not include plaintext credentials or
provider response payloads containing secrets or sensitive customer data.

## Security

Shipping Administration is a sensitive POS Admin surface.

Security rules:

- only authorized POS Admin roles may administer tenant shipping;
- all write operations require trusted tenant context;
- all cross-tenant carrier, warehouse, credential, mapping, and audit access is
  forbidden;
- activation must fail closed on missing credentials, inactive provider,
  missing warehouse assignment, missing origin evidence, missing mapping
  coverage, missing customs coverage when applicable, or unsupported provider
  capability;
- connection testing must rate-limit provider calls and redact responses;
- administrative APIs must not expose decrypted secrets;
- frontend state, local storage, browser history, projections, and caches are
  presentation only.

## Secrets

Secrets are custody-controlled backend assets. POS Admin may submit, rotate,
disable, and select secret references but does not own secret storage internals.

Required secret architecture for future implementation:

- encrypted or external secret-manager storage;
- tenant-scoped secret references;
- separate sandbox and production credential records;
- masked fingerprints for display and audit;
- versioned rotation with active/staged/retired states;
- no plaintext in database fields outside the approved secret boundary;
- no plaintext in logs, telemetry, exports, audit, frontend DTOs, or tests;
- operator-visible rotation and expiry status without secret disclosure.

## Architecture Boundaries

### Shipping Platform

POS Admin supplies carrier configuration as runtime input. Shipping Platform
owns shipment identity, carrier request identity, origin/destination/mapping
and customs snapshot consumption, provider response admission, lifecycle
transition validation, SQL persistence, recovery, and shipment audit.

### Geography

Geography owns canonical place identity, address admission, country profiles,
published catalog releases, and published carrier mapping releases. Shipping
Administration may consume readiness status and published evidence only.

### Carrier Mapping

Carrier Mapping is governed by Geographic Administration under POS Admin
authority, but it is not edited inside Shipping Administration. Shipping
Administration uses published release coverage as an activation prerequisite
and runtime dependency.

### Warehouse

Warehouse owns operational warehouse lifecycle and admitted address evidence.
Shipping Administration assigns eligible warehouses to carriers. Shipment owns
origin snapshots after registration begins.

### Shipment

Shipment owns immutable shipment snapshots, lifecycle, external registration
attempts, carrier request identity, tracking admission, labels, recovery, and
shipment audit. Shipping Administration cannot mutate shipment history.

### Checkout

Checkout owns address candidates and checkout admission. Shipping
Administration does not own checkout address entry, pricing validation, cart
state, payment state, or order creation.

### Customs

Customs owns evidence governance, approval, publication, admission, and
Shipment Customs Snapshots. Shipping Administration may block activation or
international policy enablement when customs coverage is missing, but it does
not own customs facts.

### Storefront

Storefront presents checkout and shipment choices as backend-derived state.
Shipping Administration does not create Storefront runtime truth.

### Affiliate

Affiliate owns attribution only. Shipping Administration does not own affiliate
checkout address authority, attribution, commissions, or affiliate order state.

### CommerceHub

CommerceHub owns marketplace/vendor/order channel behavior where certified.
Shipping Administration may affect tenant carrier eligibility for vendor
fulfillment only through backend shipping configuration. It does not own
CommerceHub marketplace governance.

### Super Admin

Super Admin does not own Shipping Administration. Super Admin may own
marketplace-level integrations and CommerceHub marketplace governance only.
Super Admin must not own tenant carrier activation, credentials, environment,
warehouse assignment, carrier service enablement, domestic/international
shipping policies, Carrier Administration, Shipping, Checkout, Storefront,
Affiliate, Geographic Catalog, Carrier Mapping, Country Profiles,
Publication, Rollback, Localization, Address Admission, or Payment.

## Certified Platform Redesign Review

No certified platform redesign is required.

Shipping Administration fits the existing frozen model:

- carrier configuration remains credential, environment, and eligibility
  configuration;
- origin authority remains Warehouse plus admitted Geographic evidence and
  Shipment Origin Snapshot;
- destination authority remains Order/Shipment admitted snapshots;
- carrier mapping remains published mapping release plus Shipment mapping
  snapshot;
- customs remains Customs Evidence plus Shipment Customs Snapshot;
- adapters remain serializers and parsers only;
- runtime remains fail-closed and snapshot-only for recovery.

Therefore this architecture is layered on top of the certified platform.

## Future Runtime Plan

Future runtime work, if authorized separately, should implement only the
backend enforcement of this administration architecture:

- tenant carrier configuration state machine;
- secret reference storage and rotation enforcement;
- environment selection enforcement;
- carrier activation gates;
- warehouse assignment validation;
- domestic/international policy evaluation;
- service-level allowlist enforcement;
- connection-test command handling;
- admin audit persistence;
- runtime read model consumed by `CarrierShipmentService`.

Runtime work must not redesign Shipment, Geography, Customs, Warehouse,
Checkout, Storefront, Affiliate, CommerceHub, or carrier adapters unless a
separate phase explicitly authorizes it.

## Future API Plan

Future API work should expose POS Admin backend endpoints under the existing
POS Admin route family, not Super Admin routes.

API scope should include:

- list certified carriers and current tenant carrier accounts;
- register tenant carrier account;
- stage/update credentials by secret reference;
- run connection test;
- select environment;
- assign warehouses;
- configure policies and service levels;
- activate, suspend, disable, and retire carrier account;
- read operational health and blockers;
- rotate credentials;
- read append-only audit.

The API must return masked secret metadata only.

## Future Frontend Plan

Future frontend work should be a POS Admin operational UI only. It should not
create runtime truth and should not expose plaintext secrets after submission.

Expected views:

- carrier accounts;
- credential and environment setup;
- connection testing;
- warehouse assignment;
- domestic/international policies;
- service-level settings;
- health and readiness blockers;
- credential rotation;
- audit timeline.

No frontend work is authorized by POSADMIN-SHIPPING-0.

## Final Recommendation

Certify POS Admin Shipping Administration as an architecture layer over the
frozen Shipping Platform.

Recommended next phase: POSADMIN-SHIPPING-1 backend administration API and
service design, limited to tenant carrier configuration, credential secret
references, connection-test orchestration, activation gates, warehouse
assignment, service-level settings, health/readiness records, and append-only
audit. Do not implement UI or change runtime shipment behavior in that phase
unless explicitly authorized.

## Explicit Success Answers

- Reviewed ONLY Shipping Administration? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No.**
- Frontend modified? **No.**
- Shipping Administration belongs to POS Admin? **Yes.**
- Super Admin owns Shipping Administration? **No.**
- Any certified platform redesign required? **No.**
- Architecture certified? **Yes.**
- Recommend next phase: **POSADMIN-SHIPPING-1 backend administration API and service design.**

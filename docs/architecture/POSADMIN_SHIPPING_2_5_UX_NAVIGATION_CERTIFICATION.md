# POSADMIN-SHIPPING-2.5 - UX / Navigation Certification

Date: 2026-07-08  
Scope: POS Admin Shipping Administration UX and navigation only  
Status: Certified for future frontend implementation

## Executive Summary

POS Admin Shipping Administration should become a dedicated Shipping
Administration workspace. The existing `Shipping Methods` page should be
preserved as the storefront shipping configuration area, but it should become
one tab inside the broader workspace rather than carrying carrier account
administration, credentials, activation, readiness, health, and audit concerns
inside the current single-page model.

This certification defines information architecture only. No React, JSX,
backend, controller, API, runtime, provider transport, adapter, database,
Shipment, Geography, Customs, Warehouse, Storefront, Affiliate, CommerceHub, or
Super Admin behavior was implemented or modified.

Reviewed preparation inputs:

- all `.ai-context` files;
- `POSADMIN_SHIPPING_0_ARCHITECTURE`;
- `POSADMIN_SHIPPING_1_ADMINISTRATION_RUNTIME`;
- `POSADMIN_SHIPPING_2_ADMINISTRATION_APIS`;
- `SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE`;
- current `PointOfSale.Web/src/pages/ShippingMethods.jsx`.

Certified constraints:

- Shipping Administration is POS Admin only.
- Allowed roles are `Owner`, `Admin`, and `Manager`.
- `Super Admin` must not appear in the workspace, navigation, role copy, or
  permission model.
- Secrets, provider IDs, snapshots, carrier request identity, shipment runtime
  identifiers, mapping internals, customs internals, and decrypted credential
  values must never be displayed.
- All frontend state is presentation only. Backend APIs and certified runtime
  gates remain authority.

## Information Architecture

The current `Shipping Methods` screen should not remain the only shipping admin
surface. It manages storefront shipping methods: names, rates, per-kg pricing,
free-shipping threshold, ETA, active state, sort order, carrier display label,
and tracking URL template. That responsibility is narrower than the certified
Shipping Administration control plane.

Certified future information architecture:

```text
POS Admin
  -> Shipping Administration
       -> Overview
       -> Carrier Accounts
       -> Shipping Methods
       -> Policies
       -> Warehouses
       -> Health
       -> Audit
```

`Service Levels` should not be a top-level tab. It belongs inside Carrier
Accounts because service-level enablement is account/provider capability
configuration. A separate top-level tab would detach service levels from the
carrier account, environment, readiness, provider capability, and activation
gates that govern them.

## Navigation

The POS Admin navigation should expose one primary entry:

```text
Shipping
```

The landing route should open the Shipping Administration workspace. Existing
deep links to the current `Shipping Methods` page should be preserved by
redirecting or selecting the `Shipping Methods` tab, not by removing the
storefront configuration workflow.

Navigation rules:

- show the workspace only to `Owner`, `Admin`, and `Manager`;
- hide the entry for unsupported roles;
- do not show `Super Admin` anywhere;
- tab visibility must derive from backend-authorized POS session authority;
- direct links must re-enter backend authorization and fail closed when the
  current POS session lacks access.

## Workspace Layout

The workspace should use a single page shell:

```text
Header
  Title: Shipping Administration
  Summary: carrier readiness, storefront methods, policies, warehouses, health
  Primary action: Add carrier account
  Secondary action: Run readiness check

Status strip
  Active carrier accounts
  Blocked carrier accounts
  Warehouses ready
  Last health check

Tabs
  Overview | Carrier Accounts | Shipping Methods | Policies | Warehouses | Health | Audit

Main panel
  Tab-specific operational content

Right-side contextual panel, where useful
  Selected carrier/account readiness, blockers, and next action
```

The header must present health and readiness as backend-derived summaries, not
as frontend-calculated truth. The primary action should be disabled or hidden
when the user can view but not administer shipping in a later least-privilege
model. For the certified current role set, `Owner`, `Admin`, and `Manager` are
the only allowed roles.

## Tab Layout

### Overview

Purpose: operational summary and guided next action.

Contents:

- carrier account readiness summary;
- activation blockers by carrier;
- warehouse, policy, credential, connection-test, mapping, origin, customs, and
  provider-capability readiness indicators;
- recent health changes;
- recent audit events;
- recommended next step per carrier account.

Overview belongs because shipping administration has multiple dependencies and
operators need one place to see whether the tenant can ship.

### Carrier Accounts

Purpose: carrier account lifecycle and credentials workflow.

Contents:

- carrier account list grouped by lifecycle state;
- add account flow;
- carrier selection;
- environment selection;
- masked credential status;
- connection-test status;
- warehouse assignment summary;
- service-level enablement;
- activation, suspension, disable, and retirement actions;
- readiness blockers and warnings.

Carrier Accounts is the main administration tab and should own the create and
configure journey.

### Shipping Methods

Purpose: storefront shipping method configuration.

Contents:

- existing shipping method table and create/edit/delete behavior;
- storefront names and localized names;
- base rate, per-kg rate, free-shipping threshold, ETA, display order;
- active/inactive state;
- carrier display metadata where retained by the current feature.

This tab should preserve the current page responsibility. It should not expose
credentials, provider IDs, runtime identifiers, shipment snapshots, carrier
request IDs, mapping snapshots, or customs evidence internals.

### Policies

Purpose: tenant shipping policy settings.

Contents:

- domestic policy;
- international policy;
- default carrier behavior;
- fallback preference;
- COD, insurance, pickup, and related options only where certified;
- customs-readiness blocker for international policy activation.

Policies belongs because operators need a cross-carrier policy view. Carrier
specific service levels remain inside Carrier Accounts.

### Warehouses

Purpose: carrier-to-warehouse eligibility and origin readiness.

Contents:

- warehouse assignment matrix by carrier account;
- warehouse active/inactive presentation;
- origin readiness status;
- admitted origin evidence status;
- missing or blocked origin reasons.

This tab must not edit warehouse lifecycle or address admission. It may only
present assignment and readiness for Shipping Administration.

### Health

Purpose: operational readiness, health state, and blockers.

Contents:

- health state per carrier account;
- latest connection test;
- credential expiry or rotation warning;
- mapping readiness;
- origin readiness;
- customs readiness;
- provider capability readiness;
- disabled, suspended, retired, warning, and blocked explanations.

Health belongs as a top-level tab because it is the main monitoring surface
after activation.

### Audit

Purpose: append-only administration history.

Contents:

- account creation;
- credential-reference changes and rotations;
- environment changes;
- connection tests;
- policy changes;
- warehouse assignment changes;
- service-level changes;
- activation, suspension, disable, retirement;
- health evaluation changes;
- rejected activation or transition attempts.

Audit must show actor, role, carrier account, action, previous state, next
state, timestamp, correlation ID, and non-secret reason. It must not show
plaintext secrets or sensitive provider payloads.

### Tabs Not Approved

`Service Levels` is not approved as a top-level tab. It should be nested under
Carrier Accounts because service levels are carrier-account capability settings
and activation gates, not independent administration objects.

## Carrier Account Experience

Carrier account list rows should present:

- carrier display name and certified carrier code label;
- lifecycle state: Draft, Credentials Pending, Connection Test Pending, Ready
  for Activation, Active, Suspended, Disabled, Retired;
- environment: Sandbox or Production;
- credential status: Missing, Active, Staged, Expiring, Retired, Invalid;
- last connection-test result and timestamp;
- readiness state;
- health state;
- assigned warehouse count;
- enabled service-level count;
- primary next action.

Carrier selection should show only certified supported carriers returned by the
POS Admin Shipping API. It should not allow arbitrary provider entry. Manual
shipping may appear only if certified by the backend carrier list.

Credential presentation rules:

- credential entry may collect secret fields only during submission;
- after submission, display only masked fingerprint, state, and expiry metadata;
- never show plaintext secret values;
- never show secret reference values;
- never show provider IDs or provider account internals.

Activation UX:

- `Activate` is enabled only when backend readiness returns no blockers;
- blocked activation shows exact non-secret blockers;
- activation success moves the account to `Active`;
- `Suspend` preserves account configuration but removes it from active runtime
  eligibility;
- `Disable` blocks future use and should require confirmation;
- `Retire` is available only after disabled state and should be framed as a
  terminal administrative lifecycle action.

## User Journey

Certified user journey:

```text
Create Account
  -> Configure
  -> Credentials
  -> Connection Test
  -> Warehouse
  -> Policies
  -> Activation
  -> Health
  -> Monitoring
```

Step behavior:

1. Create Account: choose a supported certified carrier and create a tenant
   carrier account.
2. Configure: choose environment and account-level settings.
3. Credentials: submit credentials into the approved backend boundary and then
   display only masked metadata.
4. Connection Test: run backend connection validation and show pass/fail
   evidence as readiness, not shipment success.
5. Warehouse: assign eligible warehouses and surface origin readiness.
6. Policies: choose domestic/international policy and fallback/default behavior.
7. Activation: backend readiness gates decide whether activation can proceed.
8. Health: show Healthy, Warning, Blocked, Disabled, or Retired.
9. Monitoring: operators use Health and Audit for ongoing readiness and
   operational traceability.

## Permissions

Workspace access is certified only for:

- `Owner`;
- `Admin`;
- `Manager`.

`Super Admin` is explicitly excluded. The workspace must not use Super Admin
routes, labels, copy, navigation placement, or authority assumptions.

Permission UX:

- unauthorized users should not see the navigation entry;
- direct route access should show an access-denied state after backend
  authorization rejects the request;
- action buttons should not be displayed from frontend role inference alone;
- all mutations must still re-enter backend authorization.

## Health UX

Health states:

| State | Presentation | Meaning |
|---|---|---|
| Healthy | Green status, no blockers | Configuration and readiness gates currently pass |
| Warning | Amber status, action recommended | Usable but needs attention, such as expiring credential |
| Blocked | Red status, activation/runtime eligibility blocked | Required readiness gate is missing or failed |
| Disabled | Gray status | Administratively disabled for future use |
| Retired | Muted terminal status | Account is no longer administratively active |

Readiness failure presentation:

- show the failed gate name;
- show a non-secret reason;
- show the next owner surface when applicable, such as Warehouse, Geographic
  Carrier Mapping, or Customs Administration;
- never claim the frontend can repair authoritative external domains directly;
- never treat connection-test success as proof of shipment success.

Activation blockers should appear inline on the account detail panel and in the
Overview blockers summary.

## Empty States

No carriers:

- message: no carrier accounts configured;
- primary action: add carrier account;
- secondary copy: carrier accounts must be created from certified supported
  carriers.

No credentials:

- message: credentials are required before connection testing and activation;
- action: add credential reference;
- do not display secret examples after submission.

No warehouse:

- message: no warehouse assigned to this carrier account;
- action: assign warehouse;
- include note that warehouse origin readiness is still required.

No policies:

- message: shipping policies are not configured;
- action: configure domestic and international policy.

No health:

- message: no health evaluation has been run;
- action: run readiness check.

No audit:

- message: no administration activity recorded yet;
- action: none unless the current tab has a relevant setup action.

## Error States

Activation blocked:

- show a blocker list grouped by credential, connection, warehouse, mapping,
  origin, customs, provider capability, and service level;
- keep activation disabled until backend readiness passes;
- provide next action links to the relevant tab.

Credential invalid:

- show invalid credential status and latest failed connection-test reason;
- allow credential replacement or rotation;
- never echo submitted secret values.

Connection failed:

- show failed test status, timestamp, environment, and non-secret failure
  category;
- allow retry with rate-limit feedback if the API reports throttling;
- do not mark the account as active.

Warehouse missing:

- show missing assignment as a blocker;
- route to Warehouses tab;
- distinguish "no warehouse assigned" from "assigned warehouse origin not
  ready".

Carrier Mapping unpublished:

- show as a mapping readiness blocker;
- do not expose provider IDs or mapping internals;
- route operators to the appropriate Geographic/Carrier Mapping administration
  area when available.

Origin not admitted:

- show as warehouse-origin readiness blocker;
- do not edit warehouse address admission inside Shipping Administration;
- route to warehouse/geographic admission workflow when available.

Customs readiness missing:

- show only for international policy/readiness;
- do not expose customs evidence internals;
- route to Customs Administration when available.

## Recommendations

The current `ShippingMethods.jsx` should be split by responsibility through
navigation, not by immediately modifying code in this phase.

Recommended disposition:

- preserve the current Shipping Methods responsibility;
- rename the future navigation entry from `Shipping Methods` to `Shipping`;
- place the current page behavior under a `Shipping Methods` tab;
- create a future `Shipping Administration` workspace shell;
- add Carrier Accounts, Overview, Policies, Warehouses, Health, and Audit as
  separate workspace tabs;
- keep Service Levels nested under Carrier Accounts;
- keep carrier credentials, connection tests, activation, suspension, readiness,
  health, and audit out of the current storefront method form.

Reason:

`Shipping Methods` configures customer-facing shipping choices and pricing.
Carrier Accounts configures operational carrier readiness. Combining both in
one single-page form would blur storefront pricing authority, carrier account
lifecycle, credential custody, activation gates, origin readiness, customs
readiness, and health monitoring.

## Future Frontend Plan

Future frontend implementation should be a POS Admin frontend phase only and
must consume the certified `api/admin/shipping` surface.

Implementation order:

1. Add Shipping Administration route and tab shell.
2. Move or mount current Shipping Methods behavior under the `Shipping Methods`
   tab without changing backend semantics.
3. Implement Overview read-only summary.
4. Implement Carrier Accounts list/detail and create flow.
5. Implement credential-reference submission with masked post-submit display.
6. Implement connection-test action and readiness blocker presentation.
7. Implement Warehouses, Policies, Health, and Audit tabs.
8. Add frontend DTO admission for all Shipping Administration read models.
9. Add route/permission tests proving `Owner`, `Admin`, and `Manager` only.
10. Add UX tests proving secrets, provider IDs, snapshots, and runtime internal
    identifiers are never rendered.

No future frontend should claim authority over activation, health, readiness,
mapping, origin, customs, credential validity, shipment lifecycle, provider
response truth, or audit mutation. It may present backend evidence and submit
backend-bound intent only.

## Explicit Success Answers

- Reviewed ONLY UX? **Yes.**
- Frontend implemented? **No.**
- Backend modified? **No.**
- React modified? **No.**
- Current page preserved? **Yes, preserve its storefront Shipping Methods responsibility.**
- Workspace recommended? **Yes, Shipping Administration workspace is recommended.**
- Tab layout approved? **Yes: Overview, Carrier Accounts, Shipping Methods, Policies, Warehouses, Health, Audit.**
- User Journey approved? **Yes: Create Account -> Configure -> Credentials -> Connection Test -> Warehouse -> Policies -> Activation -> Health -> Monitoring.**
- Ready for Frontend implementation? **Yes, as a future separate frontend phase consuming the certified APIs and preserving backend authority.**
- Recommend next phase: **POSADMIN-SHIPPING-3 Frontend Shipping Administration Workspace Implementation.**

# Shopixy M-1A Super Admin Marketplace Authority Certification

Date: 2026-08-31

## Verdict

**Code certification: PASS. Deployment certification: NOT YET CERTIFIED.**

The inspected source, focused behavior tests, authority tests, frontend boundary tests, and backend/frontend builds support the M-1A authority model. Deployment certification remains open because the PostgreSQL ownership-hardening migration is pending in Development, no API process was listening on the configured Development endpoint, and live SQL rows, outbox delivery, projections, and human UI behavior were not observed.

No test, source, or build evidence in this report is promoted to live-runtime or UI evidence.

## Certified Boundary

- PostgreSQL owns Super Admin, tenant, vendor-governance, catalog, variant, listing, and publication authority.
- Super Admin owns marketplace participation governance, verification, approval, and emergency visibility containment.
- Tenant/POS Admin authority owns catalog, variant, inventory, price, and publication intent through supported tenant APIs.
- `CommerceHubListing` owns listing identity, uniquely scoped by tenant and variant.
- MongoDB, Elasticsearch, Redis, events, outbox records, and frontend state are derivatives or delivery evidence only.
- Super Admin governance must not mutate tenant business lifecycle, catalog, inventory, price, order, payment, payout, return, or refund authority.

## Authority Matrix

| Concern | Canonical owner | M-1A result |
| --- | --- | --- |
| Super Admin identity/session | SQL Super Admin authority and token version | PASS by source/tests; live session unverified |
| Vendor identity | `Tenant.Id` and immutable Vendor governance subject | PASS by source/tests |
| Vendor eligibility | Active/non-deleted tenant, active Vendor profile, KYC, marketplace approval | PASS by source/tests |
| Vendor suspension/restore | `UserGovernanceProfile.Status` through governance service | PASS; tenant lifecycle remains unchanged |
| Publication admission | Tenant publication handler plus current vendor eligibility | PASS by source/tests |
| Listing identity | SQL `CommerceHubListing`, tenant plus variant uniqueness | PASS by source/tests |
| Listing suppression/restore | Listing governance methods plus current SQL restore authority | PASS by source/tests |
| Public discovery | SQL visibility authority and SQL-over-projection overlay | PASS by source/tests; live projection unverified |
| Ownership durability | Application save guard plus PostgreSQL trigger | PASS in source/generated SQL; migration pending |
| Frontend governance response | Backend DTO validated as derivative authority evidence | PASS by frontend tests/build |

## Defects Closed

1. Vendor/listing suppression could roll back when a published listing referenced a missing authoritative Product. Suppression now remains fail closed while the unavailable product read-model rebuild is logged and skipped.
2. The marketplace console suspended/restored the core Tenant lifecycle. It now changes Vendor governance through `IUserGovernanceService` and leaves tenant business authority active.
3. Bulk eligible-vendor SQL omitted the `Tenant.IsActive` mirror required by the shared authority snapshot. The predicate is now consistent.
4. Governance search treated missing, unverified, or unapproved Vendor profiles as visible. It now suppresses every ineligible state and reports a missing profile explicitly.
5. Direct Super Admin listing restore checked only partial tenant/governance state. It now requires the shared full marketplace-eligibility snapshot.
6. The recovery migration baseline retained the approval-requires-KYC check but omitted the PostgreSQL immutable-ownership trigger. A new migration restores the update/delete trigger.
7. Exact governance and certification replay appended duplicate domain audit/event work. Exact state-and-reason replay now returns the current authority result without repeating the mutation; approval replay still fails closed when KYC is absent.
8. The frontend governance parser rejected the new `VendorGovernance` response and still accepted the removed `Tenant` response. The contract and regression test now match the backend.

## Verification Matrix

| Evidence class | Command/evidence | Result |
| --- | --- | --- |
| Focused backend | Governance, console, tenant lifecycle, vendor authority | 25/25 passed |
| Broader M-1A backend | Super Admin, eligibility, visibility, discovery, publication, listing ownership, emergency operations | 57/57 passed |
| Frontend authority/session | Vitest governance authority and Super Admin session suites | 8/8 passed |
| Backend build | `dotnet build PointOfSale.sln --no-restore` | PASS, 0 errors; existing warnings remain |
| Frontend build | `npm run build` | PASS; existing large-chunk warning remains |
| Migration discovery | `dotnet ef migrations list` | New M-1A migration discovered and pending |
| Migration SQL | Targeted `dotnet ef migrations script` | PASS; emits trigger/function and history insert in one transaction |
| Development API | Configured `http://localhost:5010`; listener/health probe | UNAVAILABLE |
| Development SQL data | Read-only row/trigger reconciliation | UNVERIFIED; no SQL client and no manual database mutation performed |
| Outbox/projections | Live RabbitMQ/MongoDB/Elasticsearch/Redis reconciliation | UNVERIFIED; no reachable runtime/tooling established |
| Human UI | Browser observation | UNVERIFIED |

## Broader Suite Drift

An initial 63-test authority selection produced four unrelated existing failures after the M-1A emergency-route assertion was corrected:

- three architecture documentation freezes conflict with the currently modified `.ai-context` certification content;
- one final-production architecture assertion expects a shipping-method source marker that is absent from the unchanged shipping service.

These files were not changed or reverted as part of M-1A. The M-1A-specific and directly adjacent subset subsequently passed 57/57.

## Deployment Exit Criteria

M-1A may be promoted from code-certified to deployment-certified only after all of the following are observed in the intended Development deployment:

1. Apply `20260831120000_M1A_UserGovernanceProfileOwnershipAuthority` through the approved migration process.
2. Verify the KYC/approval check constraint and immutable ownership trigger in PostgreSQL.
3. Exercise supported Super Admin and tenant publication APIs with valid Development credentials.
4. Reconcile SQL listing/governance rows with outbox delivery and MongoDB/Elasticsearch/Redis derivatives.
5. Confirm suspended, missing-profile, unverified, unapproved, inactive, and deleted vendors remain absent from discovery.
6. Perform a human desktop/mobile governance-console smoke test without claiming browser automation evidence.

Until those steps are complete, the final operational verdict remains **NOT YET DEPLOYMENT-CERTIFIED**.

# PASS 15.1 Marketplace Governance Console Certification Findings

Date: 2026-06-01

## Certification Result

Marketplace Governance Console is certified for core production governance use.

The pass did not change the frozen governance model:

- Merchant owns publication intent, category selection, attributes, and CommerceHub pricing.
- Marketplace governance owns visibility, suppression, vendor governance, tenant governance, marketplace integrity, and emergency controls.
- Marketplace governance does not approve, reject, create, edit, or mutate merchant CommerceHub price overrides.

## Governance Operations Inventory

- View SQL-authoritative governance dashboard counts.
- Search global marketplace listings across tenants.
- Filter listings by visibility, suppression, orphan state, vendor governance status, tenant status, and override presence.
- Suppress a listing by setting SQL governance restore intent through `CommerceHubListing.HideByGovernance()`.
- Restore a listing only after current SQL tenant, vendor, product, variant, and CommerceHub category authority accepts visibility.
- Suspend a tenant by setting SQL `Tenant.Status = Suspended` and `Tenant.IsActive = false`.
- Restore a tenant by setting SQL `Tenant.Status = Active` and `Tenant.IsActive = true`.
- View existing vendor/customer/admin governance through the Super Admin Users console.
- Suspend, ban, activate, financially lock, and financially unlock governed users through existing SQL `UserGovernanceProfile` workflow.
- View user governance audit timelines and marketplace governance action audit records.

## Findings

1. Listing governance:
   Listing suppression and restoration are SQL-backed visibility controls. Suppression does not mutate merchant category, attributes, publication ownership metadata, or merchant price overrides. Restore validates current SQL tenant, vendor, product, variant, and category authority and fails closed when authority is gone.

2. Vendor governance:
   Existing vendor suspension/ban remains authoritative through `UserGovernanceProfile`. Vendor status hides listings, invalidates governance visibility, and queues CommerceHub search verification. PASS 15.1 exposes that state in listing governance search instead of treating listings as independently visible.

3. Tenant governance:
   Tenant suspension/restoration is now available in the Super Admin marketplace governance console. It mutates SQL tenant lifecycle state and active flag, audits the action, and queues visibility verification.

4. Visibility:
   Visibility is computed from current SQL listing publication, listing suppression intent, vendor governance, tenant governance, product visibility, variant lifecycle, and category liveness. MongoDB, Elasticsearch, Redis, and frontend state are not authority.

5. Dashboard:
   Dashboard counts derive from SQL `CommerceHubListings`, `UserGovernanceProfiles`, `Tenants`, and governance audit logs. They are authoritative snapshots at request time, not projection counts.

6. Search and filters:
   Listing governance search is SQL-backed. Filters for published, unpublished, suppressed, orphaned, vendor suspended, tenant suspended, override present, vendor status, and tenant status are applied server-side.

7. Audit:
   Listing suppression, listing restore, rejected restore, tenant suspend, tenant restore, dashboard view, and search activity log Super Admin audit records with actor, reason, and details. Existing user governance audit timelines remain available for vendor actions.

8. Notifications:
   PASS 15.1 exposes dashboard counts and visible action outcomes. A dedicated immutable operator notification inbox remains future operational maturity work.

9. Frontend/backend alignment:
   `MarketplaceGovernancePage.tsx` uses the new `/api/super-admin/marketplace-governance/*` endpoints directly and exposes only governance-owned controls. It does not expose price approval or merchant price mutation.

10. Performance:
   Search and dashboard are paginated and SQL-projected. Remaining production hardening should add targeted indexes and SLO dashboards if live cardinality outgrows current query plans.

11. Chaos:
   Suppressed listings cannot be restored by merchant bulk operations from PASS 14.3. Super Admin restore now fails closed when current SQL authority is absent. Suspended vendors and tenants are included in SQL visibility state and queued for search verification.

12. Monitoring:
   Console actions are logged and search verification is enqueued. Dedicated governance telemetry counters, alert packs, and SQL-vs-derivative parity dashboards remain operational maturity debt.

## Runtime Hardening Performed

- Added `SuperAdminMarketplaceGovernanceController`.
- Added marketplace governance dashboard and listing search DTOs.
- Added listing suppression and SQL-authority restore endpoints.
- Added tenant suspension and tenant restoration endpoints.
- Added CommerceHub auto-healing verification enqueue for listing and tenant governance actions.
- Added Super Admin frontend marketplace governance page, service, route, and navigation entry.

## Regression Freeze Added

- `MarketplaceGovernanceConsoleCertificationTests` freezes:
  - API route inventory.
  - SQL-backed dashboard/search.
  - listing suppression/restore semantics.
  - tenant governance semantics.
  - frontend/backend alignment.
  - server-side filter semantics.
  - prohibition on marketplace price mutation in governance actions.

## Required Answers

1. Can Super Admin govern marketplace visibility effectively?
   Yes, for listing suppression/restoration and tenant governance, with vendor governance available through the existing Users governance console.

2. Can suppressed listings resurrect?
   Not through reviewed merchant or Super Admin paths. Restore requires current SQL authority and merchant bulk publish remains blocked for suppressed listings.

3. Can suspended vendors resurrect?
   Not through reviewed search/reindex/governance paths. Vendor restore runs through SQL `UserGovernanceProfile` and visibility sync.

4. Can suspended tenants resurrect?
   Not through reviewed marketplace governance console paths. Tenant restoration requires Super Admin action and SQL mutation.

5. Are governance dashboards authoritative?
   Yes, the new marketplace governance dashboard derives from SQL authority.

6. Are governance searches authoritative?
   Yes, listing governance search derives from SQL authority and server-side filters.

7. Are governance audits complete?
   Complete for the new reviewed console actions and existing user governance actions. A richer immutable operator notification inbox remains debt.

8. Is tenant isolation preserved?
   Yes. Super Admin scope is explicit global platform scope, while tenant/listing identities remain SQL-scoped and no raw client tenant authority is accepted.

9. Are any governance controls misleading?
   No current reviewed controls mutate merchant-owned pricing or claim projection authority. Vendor actions remain in the Users governance console; the marketplace page displays vendor state but does not duplicate vendor controls.

10. Is the governance console production-ready?
   Yes for core governance operations. Remaining debt is operational maturity, not authority correctness.

## Remaining Debt

- Dedicated SQL-vs-Mongo-vs-Elasticsearch governance parity dashboard.
- Governance telemetry counters and alert rules.
- Immutable operator notification inbox for governance events.
- Dedicated vendor-governance shortcuts from the marketplace listing details panel.
- Query-plan review and targeted indexes after real production cardinality measurements.

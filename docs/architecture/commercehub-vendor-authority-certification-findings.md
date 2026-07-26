# PASS M1 CommerceHub Vendor Authority Certification Findings

Date: 2026-06-10

## Certified Runtime Boundary

CommerceHub vendor identity is the tenant identity in PostgreSQL. `Tenant.Id` owns vendor identity
and tenant lineage. The tenant's `UserGovernanceProfile` with role `Vendor` owns vendor governance,
verification, approval, suspension, ban, and marketplace eligibility state. SQL user, tenant,
session, refresh-token, and governance rows own authentication and session authority.

Vendor authority establishes who the vendor is and whether that vendor may participate in the
marketplace. It does not establish order, payment, refund, settlement, payout, inventory, listing,
or other commerce truth. DTOs, routes, JWT claims, frontend state, Redis, MongoDB, Elasticsearch,
events, caches, and projections are evidence or derivatives only.

## Findings

### Vendor Identity

- A vendor is identified by the immutable tenant ID; there is no independent vendor identity row
  that can drift from tenant ownership.
- Current SQL tenant existence, active lifecycle, non-deleted state, and matching Vendor governance
  profile are required by the shared vendor authority boundary.
- Governance subject and role are immutable after creation. Application save guards and a
  PostgreSQL trigger reject deletion or ownership reassignment.
- Deleted or inactive tenants fail closed and cannot be restored by stale search, query, or cache
  state.

Conclusion: vendor identity is tenant-owned SQL truth and cannot be supplied by a DTO, route,
claim, projection, or cache.

### Registration

- Registration creates the tenant, owner user, and Vendor governance profile as SQL state.
- New and existing profiles default to unverified and unapproved marketplace state.
- Tenant/user uniqueness constraints and transactional creation remain the duplicate-registration
  fences.
- A repeated or stale onboarding flow cannot become marketplace authority because current
  verification, approval, tenant, and governance state are reloaded at every participation gate.

### Verification

- `IsKycVerified` is durable Vendor governance state and can be changed only through a current
  SuperAdmin-authorized governance route.
- Verification writes run in a serializable transaction and require a reason.
- Revoking verification atomically revokes marketplace approval and hides vendor listings.
- Application save guards prevent ordinary runtime code from directly mutating certification
  fields.

### Governance

- Approval, suspension, ban, activation, deactivation, verification, and marketplace approval are
  current SQL governance decisions.
- Governance mutations serialize, persist before visibility recomposition, invalidate derived
  visibility, and write governance audit evidence.
- Suspended, banned, inactive, unverified, or unapproved vendors fail closed.
- Stale governance actions cannot restore eligibility from an old DTO or projection; restore paths
  reload the current SQL authority snapshot.

### Authentication And Sessions

- Vendor login and request authorization continue to use the certified P1-A SQL user, tenant,
  session, refresh-token, and governance boundaries.
- Suspended or banned vendors cannot use stale access or refresh credentials to regain privileged
  authority.
- Verification or approval revocation may leave an authenticated onboarding session present, but
  every marketplace operation reloads SQL eligibility and is immediately denied.
- Session identity never creates verification, approval, tenant ownership, or commerce authority.

### Ownership

- Vendor ownership is the tenant lineage carried by immutable tenant and governance subject IDs.
- Store, listing, and vendor resource access remains tenant-scoped.
- Governance subject/role reassignment and profile deletion are rejected in both runtime save
  guards and PostgreSQL.
- Profiles, DTOs, routes, JWT claims, caches, and projections cannot replace tenant ownership.

### Marketplace Eligibility

Marketplace eligibility now requires all of the following current SQL facts:

- the tenant exists, is active, has `TenantStatus.Active`, and is not deleted;
- a Vendor governance profile exists and is `Active`;
- KYC verification is current;
- explicit marketplace approval is current.

The same boundary is enforced for listing mutation, listing restoration, order acceptance, public
vendor visibility, CommerceHub query visibility, Elasticsearch SQL overlay, and reindex. Suspended,
rejected, inactive, unverified, or unapproved vendors cannot publish, remain searchable, accept
orders, or be restored by replay.

### Tenant Isolation

- Vendor APIs derive tenant authority from trusted authenticated context.
- Listing, store, query, checkout, governance, and visibility paths retain tenant predicates.
- The shared vendor authority accepts only the candidate tenant/vendor ID and recomposes all
  eligibility facts from SQL.
- Foreign route IDs, DTO IDs, search documents, or cached vendor state cannot grant access to a
  foreign tenant.

### Concurrency And Idempotency

- Verification, approval, and governance mutations use serializable SQL transactions.
- Approval cannot be persisted unless verification is current; a database check constraint
  enforces this invariant.
- Concurrent or stale mutations either serialize to one current result or fail; they cannot
  produce an approved-but-unverified vendor.
- Registration uniqueness and immutable ownership prevent replay from creating duplicate
  authoritative ownership.

### Historical Consistency

- Historical governance audit rows are evidence, not current authority.
- Search, query, and visibility restoration reload current tenant and governance state.
- Deleted, inactive, suspended, unverified, or unapproved vendors remain suppressed during replay
  and rebuild.
- No migration backfills approval from historical listings or projections. Existing vendors are
  fail-closed until explicitly verified and approved.

### Vendor And SuperAdmin Boundary

- Only `RequireSuperAdmin` governance endpoints can mutate verification or marketplace approval.
- Vendor-controlled DTOs and profiles cannot invoke platform governance.
- Approval requires current verification and all changes require a reason and audit context.
- Vendor authority may establish identity and participation eligibility only; it cannot create
  order, payment, refund, settlement, or payout truth.

### Adversarial Review

- Vendor spoofing and tenant guessing fail because current tenant lineage is reloaded from SQL.
- Approval and verification forgery fail because certification fields are SuperAdmin-owned,
  mutation-scoped, audited, and database-constrained.
- Suspension bypass fails at session, listing, order-acceptance, visibility, search, and reindex
  boundaries.
- Ownership corruption fails through immutable governance identity and tenant-scoped resources.
- Projection poisoning fails because Elasticsearch, query projections, and listing restoration
  require current SQL vendor eligibility.
- Replay cannot resurrect a vendor or restore marketplace participation after governance changes.

## Runtime Hardening Performed

- Added one shared SQL vendor authority snapshot and eligible-vendor query.
- Added explicit durable KYC and marketplace approval lineage fields.
- Added SuperAdmin verification and marketplace approval mutation routes.
- Added serializable governance/certification mutations and certification-field save guards.
- Added database approval/KYC and immutable ownership constraints.
- Routed listing, order acceptance, public visibility, query visibility, search overlay, listing
  restoration, and reindex through current SQL marketplace eligibility.
- Preserved authentication as P1-A SQL session authority and commerce truth in its owning domains.

## Exact Runtime Files Modified

- `PointOfSale.Core/Entities/UserGovernanceProfile.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminUsersController.cs`
- `PointOfSale.API/SuperAdmin/DTOs/SuperAdminUserGovernanceDtos.cs`
- `PointOfSale.API/Services/Governance/IUserGovernanceService.cs`
- `PointOfSale.API/Services/Governance/UserGovernanceService.cs`
- `PointOfSale.API/Services/Governance/UserGovernanceService.Evaluation.cs`
- `PointOfSale.API/Services/Governance/UserGovernanceService.Mappings.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPublicVendorVisibilityService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/UserGovernanceMutationScope.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs`
- `PointOfSale.Infrastructure/Migrations/20260610170000_CommerceHubVendorAuthorityHardening.cs`
- `PointOfSale.Infrastructure/Reliability/CommerceHubVendorAuthority.cs`

## Regression Freeze

- `PointOfSale.Tests/Architecture/CommerceHubVendorAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CommerceHubSearchVisibilityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CommerceHubVisibilityLifecycleCertificationTests.cs`
- `PointOfSale.Tests/Architecture/MerchantMarketplaceGovernancePrecedenceCertificationTests.cs`

The focused authority suite passed 11/11 tests. The API project build passed with zero errors.

## Explicit Answers

- Who owns vendor authority? Tenant-owned PostgreSQL `Tenant`, Vendor
  `UserGovernanceProfile`, user/session, and refresh-token rows.
- Can vendor identity be spoofed? No. Candidate IDs are recomposed against current tenant and
  immutable governance ownership.
- Can verification be bypassed? No.
- Can approval be bypassed? No.
- Can suspended vendors continue marketplace operations? No.
- Can sessions survive revocation? Revoked or governance-restricted credentials cannot retain
  authority. An onboarding session may remain after approval revocation, but marketplace work is
  denied immediately from current SQL.
- Can ownership be reassigned? No.
- Can marketplace eligibility diverge from governance truth? No inspected runtime path permits it.
- Can tenant leakage occur? No inspected vendor path permits it after hardening.
- Can replay corrupt vendor authority? No; replay and rebuild remain derivative and fail closed.
- Can vendors influence governance authority? No.
- Were runtime code changes required? Yes.
- Is CommerceHub Vendor Authority production-certifiable? Yes, after applying migration
  `20260610170000_CommerceHubVendorAuthorityHardening` and explicitly verifying and approving each
  marketplace vendor.

## Deployment Conditions And Residual Risk

- The migration deliberately does not infer verification or approval from existing listings,
  orders, frontend state, or projections. Existing vendors are marketplace-ineligible until a
  SuperAdmin records both decisions.
- Production rollout must apply the migration before the new runtime and complete an explicit
  vendor review/approval procedure.
- Production PostgreSQL concurrency, failover, and load evidence remains operational proof outside
  this repository.
- Future delegated approval, support impersonation, vendor transfer, multi-owner vendor identity,
  KYC providers/webhooks, scheduled governance, or bulk approval requires renewed M1 review.

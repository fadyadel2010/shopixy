# GEO-3 - Geographic Catalog Governance Authority

Status: Governance architecture freeze pending approval  
Date: 2026-07-05  
Governing architecture: `ADR-GEO-001_CANONICAL_GEOGRAPHIC_IDENTITY.md`, `GEO-1.5_GEOGRAPHIC_DOMAIN_BOUNDARIES.md`, and `GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md`  
Runtime impact: none  
Backend impact: none  
Frontend impact: none  
Checkout impact: none  
Shipping runtime impact: none  
Carrier adapter impact: none

## Executive Summary

Geographic Catalog Governance is a platform administration authority. It owns
who may stage, validate, review, approve, publish, activate, supersede,
rollback, merge, split, deprecate, localize, alias, synchronize, override, and
audit geographic catalog and carrier geographic mapping releases.

This phase does not implement runtime services, controllers, DTOs, EF entities,
migrations, Checkout behavior, Shipping runtime behavior, Storefront behavior,
or carrier adapter behavior. GEO-3 freezes the governance model that later
implementation must enforce.

The governing rule is fail closed: no catalog release, mapping release, sync
candidate, override, merge, split, deprecation, alias, localization, country
profile, or publication can become runtime authority unless Platform
Geographic Administration stages it, validation passes, a different authorized
approver approves it, and publication activates an immutable release.

## Governance Model

Geographic administration is split into four platform-only workspaces:

1. Geographic Catalog Administration stages canonical places, hierarchy,
   country profiles, names, aliases, merges, splits, deprecations, and catalog
   release drafts.
2. Carrier Mapping Administration stages provider/environment/account-scoped
   mappings, mapping groups, sync candidates, overrides, and mapping release
   drafts.
3. Geographic Review and Approval reviews draft evidence, validation output,
   merge/split/deprecation impact, localization coverage, mapping coverage,
   override scope, and sync diffs.
4. Geographic Publication Operations publishes, activates, supersedes,
   rolls back, archives, and audits immutable releases.

All four workspaces are Platform Admin only. Tenant admins may request support
or scoped mapping override review where product policy permits it, but they do
not mutate the global catalog and they do not publish mapping releases.

## Authority Matrix

| Authority question | Owner | Required control |
|---|---|---|
| Who creates Places? | Platform Geographic Catalog Editors | Draft only; no runtime authority until approved release publication. |
| Who edits Places? | Platform Geographic Catalog Editors | New draft row/version/change entry; optimistic concurrency required. |
| Who retires Places? | Platform Geographic Catalog Editors | Draft retirement plus review, successor policy, and publication. |
| Who merges Places? | Platform Geographic Catalog Editors propose; Geographic Approvers approve | Merge history, successor, impact report, and four-eyes approval required. |
| Who splits Places? | Platform Geographic Catalog Editors propose; Geographic Approvers approve | New target places, reassignment plan, impact report, and four-eyes approval required. |
| Who approves catalog changes? | Platform Geographic Approvers | Approver must differ from creator/editor and must hold approval authority. |
| Who publishes catalog Releases? | Platform Geographic Publication Operators | Publication requires approved validation snapshot and publication lock. |
| Who rolls back catalog Releases? | Platform Geographic Publication Operators | Rollback activates a prior immutable release and audits the reason. |
| Who imports Provider catalogs? | Platform Carrier Sync Operators | Sync run creates evidence and candidates only. |
| Who reviews Sync Candidates? | Platform Carrier Mapping Reviewers | Review records approve, reject, or defer candidates; no auto-publication. |
| Who approves Mapping Releases? | Platform Carrier Mapping Approvers | Four-eyes approval; approver differs from candidate creator/reviewer where policy requires. |
| Who publishes Mapping Releases? | Platform Geographic Publication Operators | Publication requires approved mapping release validation and locking. |
| Who creates Overrides? | Platform Carrier Mapping Editors, or tenant support request converted by Platform Admin | Override is a candidate only until reviewed and included in a mapping release. |
| Who approves Overrides? | Platform Carrier Mapping Approvers | Scope, tenant/account/provider isolation, reason, and expiry/effective dates required. |
| Who owns Country Profiles? | Platform Geographic Catalog Administration | Country requirements are catalog governance data and publish with catalog releases. |
| Who owns Localization? | Platform Geographic Catalog Administration | Names and display labels are catalog data; search consumers are derivative only. |
| Who owns Aliases? | Platform Geographic Catalog Administration | Aliases are candidate search evidence only and never identity authority. |
| Who owns Search labels? | Platform Geographic Catalog Administration | Search indexes/projectors consume published names/aliases and cannot mutate them. |

Forbidden authorities:

- Providers cannot mutate catalog or mapping authority directly.
- Carrier adapters cannot mutate catalog, mappings, sync candidates, or
  overrides.
- Checkout, Storefront, Orders, Shipments, Tracking, Returns, Analytics,
  Search, Redis, MongoDB, Elasticsearch, and browser state cannot mutate
  catalog or mapping authority.

## Publication Workflow

The catalog workflow is:

```text
Catalog Draft
  -> Validation
  -> Review
  -> Approval
  -> Publication
  -> Activation
  -> Supersede
  -> Rollback
  -> Archive
```

Rules:

- Draft isolation: draft rows and candidates are invisible to runtime
  admission, search packages, provider mapping resolution, Checkout,
  Storefront, Orders, Shipments, and carrier adapters.
- Validation: publication validation checks hierarchy completeness,
  parent/type compatibility, country profile requirements, duplicate active
  hierarchy, cycles, localization, aliases, successor links, merge/split
  lineage, and release checksum.
- Review: reviewers inspect validation output, change ledger, source evidence,
  localization impact, alias ambiguity, country isolation, and migration risk.
- Approval: approval is explicit, audited, and separated from draft creation.
- Publication locking: one publication per country/release scope may be active
  at a time; publication holds a lock that prevents concurrent activation or
  rollback races.
- Activation: activation switches the current release pointer only after the
  immutable release and publication audit record are durably written.
- Supersede: a later release supersedes an earlier release without editing it.
- Rollback: rollback activates a prior immutable release through publication
  history and never rewrites business snapshots.
- Archive: archive removes no authority; it only changes operational retention
  and discoverability for old evidence according to policy.

## Approval Workflow

Four-eyes approval is mandatory for production catalog and mapping
publication.

Approval rules:

- The creator/editor/importer cannot be the sole approver.
- Production publication requires at least one approver with Platform
  Geographic Approval authority.
- Mapping release approval must include provider, environment, account scope,
  country scope, freshness, coverage, cardinality, and override review.
- Merge, split, deprecation, rollback, and override approval require a reason.
- Rejection is audited with a reason and returns the draft/candidate to a
  non-published state.
- Approval binds to a validation snapshot and checksum. Changing the draft
  invalidates prior approval.

## Rollback Workflow

Rollback is an activation operation, not an edit.

Workflow:

1. Operator selects a prior immutable catalog or mapping release.
2. System validates the target release is complete, not archived beyond
   recoverability, and compatible with the intended country/provider scope.
3. Approver records rollback reason and operational impact.
4. Publication lock prevents concurrent publish/rollback for the same scope.
5. Current pointer changes to the prior release and emits invalidation.
6. Audit records link the rollback publication to the replaced release.

Rollback does not change orders, shipments, returns, admission evidence,
offline packages already issued, or shipment mapping snapshots. Runtime reads
use the newly activated release for future admission/resolution only.

## Merge Workflow

Place merge is a catalog release change.

Required controls:

- Source and target places must share compatible country and hierarchy policy.
- Self-merge is forbidden.
- Successor place is mandatory unless the merge policy explicitly records a
  retired-without-successor exception.
- Impact report lists affected aliases, names, mappings, saved addresses,
  unresolved legacy candidates, and business snapshots.
- Mapping coverage is revalidated for every affected provider/account scope.
- Approval requires four-eyes review and a mandatory reason.
- Historical snapshots retain the original place IDs and display evidence.

## Deprecation Workflow

Deprecation marks a place as no longer valid for new admission while preserving
history.

Rules:

- Deprecation is draft-only until a catalog release is published.
- Successor policy must be explicit: successor required, no-successor
  justified, or manual-review-only.
- New admissions fail closed or require the approved successor flow.
- Existing snapshots remain renderable and are not rewritten.
- Provider mappings tied to deprecated places must be reviewed before mapping
  release publication.

## Sync Workflow

Provider synchronization is evidence intake, not publication.

```text
Provider API
  -> Sync Run
  -> Candidates
  -> Review
  -> Approval
  -> Release
  -> Publication
```

Rules:

- Provider sync must never publish automatically.
- Sync credentials remain carrier credential authority; raw evidence is
  redacted, hashed, and stored as bounded audit evidence.
- Sync run creates provider nodes and diff candidates only.
- Existing mappings may be auto-carried only when provider stable ID, parent,
  scope, account, environment, and semantics are unchanged.
- Additions, removals, renames, reparenting, splits, merges, ID changes,
  ambiguous names, and stale data require review.
- Approved candidates materialize into a mapping release draft.
- Runtime carrier mapping resolution consumes only published mapping releases.

## Security Model

Security rules:

- Platform Admin only for catalog, mapping, sync, publication, rollback,
  merge, split, deprecation, localization, alias, and country profile mutation.
- Least privilege separates editor, reviewer, approver, sync operator,
  publication operator, and auditor capabilities.
- Approval separation prevents self-approval for production publication.
- Country isolation prevents one country draft/release from mutating another
  country unless an explicit cross-country release scope is approved.
- Provider isolation prevents one provider's sync, candidates, overrides, or
  mappings from affecting another provider.
- Tenant isolation applies only to scoped mapping overrides and account
  releases; tenant scope never owns the global catalog.
- No provider, carrier adapter, Checkout, Storefront, Order, Shipment,
  Tracking, Search, Analytics, projection, cache, or browser client can mutate
  catalog or mapping authority.

## Audit Model

Audit is append-only and mandatory.

Audited events:

- every draft change;
- every validation result;
- every review decision;
- every approval;
- every rejection;
- every publication;
- every activation;
- every supersede;
- every rollback;
- every archive;
- every merge;
- every split;
- every deprecation;
- every alias change;
- every localization change;
- every country profile change;
- every sync run;
- every sync candidate review;
- every override creation, approval, rejection, and release inclusion.

Audit records must include actor, role/capability, timestamp, aggregate type,
aggregate ID, release ID where applicable, country/provider/environment/account
scope, reason, correlation ID, validation checksum, and redacted evidence
reference where applicable.

## Fail-Closed Publication Validation

Publication must fail closed for:

- invalid release status;
- missing approval;
- self-approval where separation is required;
- incomplete hierarchy;
- invalid parent/type transition;
- duplicate active hierarchy;
- cycle in hierarchy;
- broken successor link;
- broken merge/split lineage;
- broken deprecation policy;
- missing preferred localization for required languages;
- broken alias target;
- ambiguous alias marked auto-resolvable;
- missing or invalid country profile;
- broken provider parent mapping;
- missing required provider mapping coverage;
- duplicate provider identity inside a release;
- duplicate canonical/provider mapping where one-to-one is required;
- wrong provider, environment, account, tenant, or country scope;
- stale mapping release under freshness policy;
- unpublished catalog dependency;
- checksum mismatch;
- optimistic concurrency conflict;
- active publication lock conflict.

## Risk Analysis

| Risk | Status | Mitigation |
|---|---|---|
| Self-approved catalog or mapping publication | Mitigated by design | Four-eyes approval and approval separation. |
| Draft leakage into runtime reads | Mitigated by design | Draft isolation and published-release-only runtime resolution. |
| Provider sync auto-publishes bad mappings | Mitigated by design | Sync creates candidates only; review and approval are mandatory. |
| Rollback rewrites history | Mitigated by design | Rollback activates prior immutable releases; snapshots remain unchanged. |
| Tenant override becomes global catalog authority | Mitigated by design | Overrides are scoped mapping candidates only. |
| Country/provider scope drift | Mitigated by design | Country, provider, environment, account, and tenant scope validation. |
| Incomplete implementation later weakens governance | Residual | Architecture regression tests freeze these invariants before runtime work. |

## Final Recommendation

Approve GEO-3 as the Geographic Catalog Governance authority freeze. Proceed
next to **GEO-4 - Geographic Catalog Implementation Plan** only after GEO-1,
GEO-1.5, GEO-2, and GEO-3 are approved.

GEO-4 should design implementation sequencing for admin APIs, authorization
policies, validation services, EF/migrations, publication locks, optimistic
concurrency, audit writes, sync staging, and architecture/runtime tests. It
must still avoid Checkout, Storefront, Shipping runtime, and carrier adapter
changes unless a later approved phase explicitly expands scope.

## Explicit Success Answers

- Reviewed ONLY Geographic Governance: **Yes**.
- Runtime files modified: **No**.
- Backend files modified: **No runtime backend files modified**.
- Frontend files modified: **No**.
- Architecture files added: **Yes**,
  `docs/architecture/GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md`.
- Regression tests added: **Yes, architecture regression only**,
  `PointOfSale.Tests/Architecture/GeographicCatalogGovernanceAuthorityTests.cs`.
- Authority changes discovered: **Yes**. GEO-3 makes explicit that catalog and
  mapping administration require Platform Admin role separation, four-eyes
  approval, publication locking, immutable releases, sync-candidate review, and
  published-release-only runtime consumption.
- Runtime hardening required: **Not in this phase**. Later implementation must
  enforce authorization, publication validation, approval separation,
  optimistic concurrency, publication locks, and audit writes.
- Production-certifiable: **Governance design is certifiable; runtime is not
  production active until implemented and tested**.
- Recommend next phase: **GEO-4 - Geographic Catalog Implementation Plan**.

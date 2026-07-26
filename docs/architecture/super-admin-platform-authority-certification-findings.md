# SUPERADMIN-CON-01 Super-Admin Platform Authority Certification Findings

Date: 2026-05-24

## Scope

This certification reviewed the CommerceHub super-admin and platform-admin boundary across platform-wide tenant management, marketplace governance, global moderation, recovery/rebuild controls, global cache/search operations, dangerous operational primitives, and replay/rebuild diagnostics.

The objective is not to remove global power. Super-admin authority is intentionally global. The certification target is that global authority is explicit, current-SQL backed, replay-safe, auditable, and documented.

## Boundaries Audited

- Super-admin authentication and authorization: `SuperAdminJwt`, `RequireSuperAdmin`, `SuperAdminBaseController`, dedicated token claims, refresh-token rotation, and SQL `SuperAdmins` state.
- Platform actor governance: `UserGovernanceProfiles` for super-admin accounts, super-admin management, governance actions, blacklist operations, review moderation, refunds, payouts, commission rules, promo codes, marketplace CMS/footer/translation, homepage sections, vendor finance, tenant management, maintenance, debug/development controls, and platform finance controllers.
- Global operational controls: tenant creation/status updates, all-tenant WhatsApp transport migration, disabled database reset, development CommerceHub seed path, super-admin marketplace-wide content mutation, tenant-admin global-search rejection, internal all-tenant affiliate repair script scope, and platform/runbook-only global repair.
- Replay/rebuild controls: SQL-first product/search/CommerceHub/homepage/discount rebuild declarations, tenant-scoped admin reindex, global/all-tenant runbook primitives, duplicate replay fences, and rebuild telemetry.
- Dangerous primitives: direct SQL/bulk mutation, full Mongo replacement, raw Elasticsearch writes, Redis pattern invalidation, raw MassTransit publishing, all-tenant repair, support/tenant override, and manual financial repair.
- Observability: super-admin audit queue, request logs, convergence telemetry for authority failures, dangerous primitive guard telemetry, replay/rebuild telemetry, and documented recovery runbooks.

## Dangerous Global Primitives Reviewed

- Global/all-tenant repair remains intentionally flexible but is platform/runbook scope only. Tenant-admin controllers must not widen tenant-scoped repair into all-tenant fanout.
- Global Elasticsearch index deletion/recreation is explicitly blocked from tenant-admin search tooling with `GLOBAL_SEARCH_CONTROL_FORBIDDEN`.
- Internal all-tenant affiliate reindex remains a platform primitive; tenant-admin AF16E uses the tenant-scoped entrypoint.
- Full Mongo replacement, raw Elasticsearch writes, Redis pattern invalidation, and direct SQL/bulk mutation remain governed by source guards, runtime guards, SQL overlays, or documented exceptions.
- Marketplace-wide CMS/footer/translation/homepage moderation is super-admin controlled and SQL-backed; it is not tenant publication authority.
- Manual payment, refund, payout, settlement, ledger, and fulfillment repair remains forbidden outside lifecycle/reconciliation owners.

## Runtime Hardening Added

- Added `SuperAdminPlatformAuthority`, a current-SQL authority boundary for super-admin requests. JWT claims identify a candidate actor only; current SQL `SuperAdmins` plus current `UserGovernanceProfiles` decide whether platform work may proceed.
- Added `SuperAdminPlatformAuthorityMiddleware`, which guards every endpoint carrying the `RequireSuperAdmin` policy, including controllers outside the `/api/super-admin/*` route family.
- Bound super-admin access tokens to `SuperAdmins.TokenVersion` through the `token_version` claim. Logout, refresh-token reuse revocation, and password-change session revocation now fail closed for stale access-token replay.
- `SuperAdminAuthService.RefreshAsync(...)` now re-runs current governance login eligibility before issuing replacement tokens. Governance-restricted refresh attempts clear the stored current refresh authority and fail closed instead of rotating a fresh platform session.
- Added platform-authority convergence telemetry for missing SQL authority, invalid platform claims, governance rejection, and stale super-admin access-token reuse.
- Strengthened tests for current SQL super-admin authority, stale access token rejection, deleted super-admin rejection, governance-restricted super-admin rejection, middleware registration, source markers, and documentation coverage.

## Replay And Rebuild Findings

- Validated: tenant-admin rebuild/reindex remains tenant-scoped and cannot delete/recreate global search indexes.
- Validated: inspected SQL-first rebuild paths redeclare SQL authority or use documented equivalent SQL recomposition before derivative mutation.
- Validated: duplicate replay collapse, dangerous derivative mutation guards, tenant lineage, and operational convergence telemetry remain the governing model for inspected derivative rebuild paths.
- Finding hardened: stale super-admin access-token reuse was the concrete platform-authority gap. A deleted, governance-banned, or token-rotated super-admin access token is now rejected against current SQL state.
- Follow-on finding hardened on 2026-05-26: a still-current refresh credential is no longer enough to mint new platform tokens after governance restriction. Refresh rotation now recomposes current governance state first, and governance restriction revokes stored platform refresh authority.
- Intentional flexibility: global/all-tenant repair remains intentionally flexible when explicitly invoked under platform authority. The certification does not convert runbooks into tenant-admin features.

## Operational Auditability Findings

- Super-admin domain actions already write audit records through `ISuperAdminAuditService` on inspected governance and marketplace management controllers.
- Request-level super-admin logging remains in place for `/api/super-admin/*` routes.
- Platform authority rejection now records low-cardinality convergence telemetry without raw tenant ids or document keys.
- Dangerous primitive acceptance/rejection remains observable where `DerivativeProjectionMutationGuard`, `ProjectionEventFence`, and `SqlTruthRebuildGuard` are used.
- Remaining audit gap: some older platform-policy endpoints outside `/api/super-admin/*`, such as tenant management and disabled maintenance/development controls, rely on structured logs rather than the super-admin audit queue. They are now actor-recomposed by platform authority, but their audit trail is not yet uniformly durable.

## Verified Guarantees

- Super-admin access-token claims are candidate evidence only.
- Current SQL `SuperAdmins` existence and current token version are required for every `RequireSuperAdmin` endpoint.
- Governance-banned, suspended, or financially locked super-admin accounts fail closed for platform operations.
- Governance-banned, suspended, or financially locked super-admin accounts also fail closed for platform refresh rotation; current refresh authority is revoked when that rejection occurs.
- Deleted super-admin rows fail closed even if an unexpired access token remains.
- Super-admin governance actions that move a platform actor out of `Active` revoke the actor's stored refresh authority through the same SQL-backed session-clear path.
- Tenant-admin operational tooling cannot silently escalate to global search index control.
- Dangerous operational primitives remain implementation tools, not authority models.
- Global/all-tenant operation remains intentionally available only under platform authority and documented runbook scope.

## Remaining Platform-Authority Debt

- No enterprise IAM, SSO, ABAC, break-glass ceremony, support impersonation workflow, or durable SIEM-grade audit event store was introduced.
- Some global/platform endpoints outside `/api/super-admin/*` still use local structured logging instead of `ISuperAdminAuditService`; future changes should migrate high-risk platform mutations to durable super-admin audit records.
- Global repair scripts and platform runbooks still need path-specific tests when expanded. This pass certifies the authority boundary and inspected primitives, not every future script.
- All-tenant operations do not yet expose adaptive pacing, tenant hotspot scheduling, or fixed rebuild SLOs beyond existing bounded worker controls.
- Some dangerous primitive paths use equivalent local SQL checks rather than shared helpers. Documentation and tests must continue to state actual controls precisely.

## 2026-05-24 Phase 2 Marketplace Governance Addendum

- Marketplace-wide public visibility governance was certified in `docs/architecture/marketplace-governance-certification-findings.md`.
- Super-admin/global marketplace moderation remains intentionally available only under current SQL platform authority or documented runbook scope.
- Homepage/global discovery curation is now guarded by current SQL listing/category/tenant visibility, and homepage projection replay is fenced before side effects.
- Marketplace product read models now require SQL CommerceHub listing publication authority before product visibility can be restored.
- CommerceHub search new writes use tenant-scoped document identity; legacy listing-only ids are cleanup targets during replay/reindex.

## 2026-05-24 Phase 3 Replay / Rebuild / Recovery Safety Addendum

- Reviewed global rebuild tooling, replay forcing, dead-letter retry, marketplace search recovery, cache/search repair, rebuild interruption, restart, and replay/rebuild overlap semantics for the super-admin recovery layer.
- Finding hardened: tenant admin CommerceHub search recovery previously invoked the global CommerceHub reindex service. The tenant-admin route now calls tenant-scoped CommerceHub search reindex through `ReindexTenantAsync` with trusted tenant context, while global `ReindexAsync` remains a platform/runbook primitive.
- Finding hardened: tenant-admin CommerceHub alias finalization previously remained co-located with tenant-admin search tooling even though alias promotion and migration catch-up are global/platform recovery controls. The tenant-admin finalization route now fails closed with `GLOBAL_SEARCH_CONTROL_FORBIDDEN`; `FinalizeAliasMigrationAsync` and `CatchUpReindexAsync` remain platform/runbook primitives.
- CommerceHub search rebuild declarations now explicitly include `SqlAuthorityOverlayKind.CommerceHubListing`; product/variant/category/stock/media authority is not enough to restore marketplace search visibility.
- Global CommerceHub reindex pacing is now configurable through bounded `CommerceHubReindexOptions` for parallel batch size, status-update batch size, and inter-batch delay. This is operational pacing, not a throughput SLO.
- Dead-letter retry behavior remains bounded: failed marketplace outbox rows must be explicitly reset for retry and then pass the normal claim, ordering, SQL authority, and publish path again.
- Replay amplification containment: duplicate projection completion, homepage replay, product cache replay, and marketplace outbox ordering continue to collapse or defer instead of widening side effects.
- Rebuild interruption and restart semantics: interrupted CommerceHub search reindex can be restarted from SQL authority. Tenant-scoped reindex processes only tenant-owned Mongo seeds and tenant-owned SQL listing seeds; missing/stale SQL authority deletes search visibility.
- Recovery auditability: SQL rebuild declarations, replay fences, dead-letter reset logs, dangerous primitive telemetry, and platform authority telemetry remain the primary diagnostics. Some older platform endpoints still lack durable super-admin audit queue records.
- Remaining recovery-risk debt: no adaptive global scheduler, fixed rebuild SLO, SIEM-grade audit store, universal dead-letter UI, or universal helper adoption for every future feed/export/cache-warmer/global script was introduced.

## 2026-05-24 Phase 4 Auditability And Emergency Governance Addendum

- Reviewed operational auditability, emergency override behavior, support/impersonation boundaries, rollback semantics, dangerous action observability, and platform governance consistency across super-admin and legacy platform-policy endpoints.
- Runtime hardening added: `SuperAdminOperationalAuditMiddleware` now records request-level operational audit for every non-read `RequireSuperAdmin` operation after `SuperAdminPlatformAuthorityMiddleware` recomposes current SQL actor authority.
- This closes the durable-audit gap for legacy platform-policy mutations outside `/api/super-admin/*`, including tenant management, disabled maintenance, development seed, debug, and platform finance mutation surfaces when present. Existing domain audit remains authoritative for richer refund, payout, vendor finance, user governance, accounting, store-config, and marketplace management records.
- Emergency override semantics remain intentionally flexible under platform authority, but overrides must name SQL authority, preserve replay/rebuild convergence, keep dangerous primitive diagnostics visible, and leave request-level audit breadcrumbs.
- No production support impersonation endpoint was found. Support impersonation and tenant override remain dangerous primitives requiring a future reviewed ceremony with SQL actor authority, tenant lineage, reason/reference capture, revocation semantics, durable audit, and replay-safe convergence.
- Rollback governance is SQL-first: rollback means lifecycle/reconciliation repair or derivative rebuild/reindex/delete/tombstone from SQL, never copying old Mongo, Redis, Elasticsearch, queue payload, dashboard, export, or support-tool state back into truth.
- Detailed findings live in `docs/architecture/emergency-governance-certification-findings.md`.
- Remaining auditability debt: no enterprise IAM, support break-glass ceremony, SIEM-grade immutable audit lake, request-body audit capture, or universal domain-specific audit events for every older platform endpoint was introduced.

## 2026-05-24 Phase 5 Operational Chaos Certification Addendum

- Reviewed the super-admin/platform layer under replay storms, rebuild overlap, stale emergency-session reuse, dangerous primitive misuse, marketplace visibility chaos, moderation/review replay, and global recovery pressure.
- Runtime hardening added: marketplace review aggregate replay now applies `CommerceHubSearchSqlAuthorityOverlay` before CommerceHub search mutation and deletes tenant-scoped search visibility when current SQL listing authority is gone.
- Certified convergence: stale platform tokens, revoked/deleted super-admin rows, governance-restricted super-admin profiles, duplicate replay, rebuild overlap, unsafe raw search writes, and unsupported derivative rollback all fail closed, collapse, or redeclare SQL authority instead of becoming operational authority.
- Operational misuse finding: global/all-tenant recovery remains intentionally flexible, but only under current SQL platform authority, request audit, dangerous primitive governance, bounded recovery controls, and SQL-first runbook scope.
- Detailed Phase 5 findings live in `docs/architecture/super-admin-operational-chaos-certification-findings.md`.
- Remaining chaos debt is explicit: no enterprise IAM/SSO/ABAC, SIEM-grade audit lake, universal chaos framework, adaptive global scheduler, fixed rebuild SLO, or universal certification for future feed/export/ranking/cache-warmer/support/global-script paths was introduced.

## 2026-05-24 POS Admin Authentication Boundary Addendum

- POS-ADMIN-CERTIFICATION-01 PASS 1 certifies the adjacent tenant-admin session boundary without merging it into super-admin authority.
- POS access tokens now require current SQL session/actor/tenant authority through `PosSessionAuthority`; super-admin access tokens continue to require current SQL platform authority and token version through `SuperAdminPlatformAuthority`.
- Tenant-admin session hardening does not grant platform authority or widen tenant-scoped recovery into global operations.
- Detailed tenant-admin authentication/session findings live in `docs/architecture/admin-authentication-session-certification-findings.md`.

## 2026-05-26 Refresh Governance Addendum

- Re-certified the super-admin authentication lifecycle together with POS-ADMIN-CERTIFICATION-01 PASS 1 and found a concrete follow-on gap: refresh rotation consulted current token state but not current governance login eligibility.
- `SuperAdminAuthService.RefreshAsync(...)` now calls `EnsureAdminLoginAllowedAsync(...)` before issuing replacement tokens. Governance-restricted refresh attempts fail closed, clear the stored refresh authority, and write bounded audit/log evidence.
- `UserGovernanceService.ApplyActionAsync(...)` now revokes stored platform refresh authority when a super-admin is suspended, banned, or financially locked, so governance changes converge directly into refresh/session invalidation instead of waiting for request-time rejection alone.
- Focused regression coverage now freezes suspended/financially-locked refresh rejection and governance-action refresh revocation in addition to request-time platform authority rejection.

## 2026-05-26 POS-ADMIN-CERTIFICATION-01 PASS 2 Global Taxonomy Addendum

- Reviewed a remaining platform-scope governance leak adjacent to tenant-admin PASS 2: `AdminAffiliateCategoryController` exposed global affiliate taxonomy mutation to tenant `Owner` scope.
- Global affiliate-category create/update/activate/deactivate now require `RequireSuperAdmin` policy authority instead of tenant-admin role authority. Tenant-admin read access remains intentionally available for browse-only workflows.
- This keeps platform-wide affiliate taxonomy governance aligned with the certified model: shared/global mutation is allowed, but only under explicit current-SQL platform authority, request audit, and platform recovery/runbook scope.

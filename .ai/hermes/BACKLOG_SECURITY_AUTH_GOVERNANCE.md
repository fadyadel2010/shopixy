# Shopixy Engineering Backlog — Security / Authorization / Authentication / Governance
**Sub-Agent 5 of 7 — Domain: Security, Authorization, Authentication, Governance**
**Source corpus:** `.ai/hermes/` cross-cutting surveys (02–08) + domain-specific auth/identity files
(listed per issue). Scope = UNRESOLVED issues only (no completed work, no strengths).

---

## SECURITY / AUTH / GOVERNANCE BACKLOG

### SEC-001: Ghost "Accountant" role in live authorization metadata
- Severity: High
- Category: Authorization Gap
- Description: `AdminCommerceHubFinanceController` and `AdminCommerceHubPaymentsController` declare `[Authorize(Roles="Owner,Admin,Accountant")]`, and `AdminTenantAuthority.TenantActorRoles` includes the literal `"Accountant"`. The `UserRole` enum defines only `Owner/Admin/Manager/Cashier/Warehouse` — there is no `Accountant` value, so the role can never be assigned yet it participates in a live endpoint authorization boundary. This is config/model drift, not dead code.
- Recommendation: Either add a first-class `Accountant` role (assignment path + tests) or remove the literal from both controller metadata and `AdminTenantAuthority.TenantActorRoles`.
- Complexity: M
- Dependencies: none
- Affected Modules: POS Admin authorization, CommerceHub finance/payments controllers, `AdminTenantAuthority`
- Source Documents: platform/identity-access-management.md §9.2, §18#2, §20, §827-835; platform/identity-runtime-final-architecture.md §621, §724
- Evidence: "`AdminCommerceHubFinanceController` and `AdminCommerceHubPaymentsController` allow `Owner,Admin,Accountant`; `AdminTenantAuthority.TenantActorRoles` also includes `Accountant`, but `UserRole` defines only `Owner`, `Admin`, `Manager`, `Cashier`, and `Warehouse`."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-002: Tenant `IsActive` (bool) vs `Tenant.Status` (enum) split-brain
- Severity: High
- Category: Known Limitation
- Description: Two signals exist for tenant state. `TenantsController.SetTenantStatus` updates only the legacy `IsActive` bool, while `PrivilegedRefreshTokenRevocationInterceptor` keys tenant-user revocation off `IsActive` and governance/super-admin path keys off `Status`. A tenant can be logically active under one signal and not the other, causing inconsistent suspension/revocation behavior.
- Recommendation: Make `Tenant.Status` the sole signal; remove bool writes; rewrite interceptor and `SetTenantStatus` to mutate `Status`/`DeletedAtUtc` consistently.
- Complexity: M
- Dependencies: none
- Affected Modules: Tenant resolution, `TenantSaveChangesInterceptor`, `PrivilegedRefreshTokenRevocationInterceptor`, `TenantsController`, SuperAdmin governance
- Source Documents: platform/identity-runtime-final-architecture.md §624, Technical Debt #2, Implementation Follow-up (Unify tenant status)
- Evidence: "`SetTenantStatus` updates only the bool; interceptor and governance read different signals. Split-brain risk."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-003: No hard tenant deletion path
- Severity: Medium
- Category: Known Limitation
- Description: `Tenants.Status=Deleted` and `DeletedAtUtc` are schema-present and the interceptor branches on them, but no runtime path sets them. The recovery lifecycle that would use these flags is spec-defined but unimplemented, leaving soft-delete/recovery of tenant scope incomplete.
- Recommendation: Implement a governed hard-delete/soft-close path with audit + downstream child-row handling, or formally drop the schema columns and interceptor branches.
- Complexity: L
- Dependencies: SEC-002
- Affected Modules: Tenant lifecycle, `PrivilegedRefreshTokenRevocationInterceptor`, SuperAdmin governance
- Source Documents: platform/identity-runtime-final-architecture.md Technical Debt #4, Implementation Follow-up (Implement tenant hard-delete)
- Evidence: "`Status=Deleted`/`DeletedAtUtc` schema-present and interceptor branches on them, but no runtime path sets them (debt). Recovery lifecycle spec-defined, unimplemented."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-004: Affiliate refresh-token reuse detection missing
- Severity: Medium
- Category: Security Concern
- Description: Storefront refresh rotation detects reuse (`TOKEN_REUSE_DETECTED`, `IsRevoked` → revoke all). Affiliate relies only on implicit `ClearRefreshToken()` on logout/reset plus `RefreshTokenExpiryTime` check — there is no explicit replay/reuse detection, creating a refresh-replay gap for the affiliate realm.
- Recommendation: Add explicit affiliate refresh reuse detection (hash-mismatch → revoke-all + audit event) mirroring the Storefront model.
- Complexity: M
- Dependencies: none
- Affected Modules: `AffiliateAuthController`, affiliate refresh path
- Source Documents: platform/identity-runtime-final-architecture.md Technical Debt #10, Implementation Follow-up (Affiliate refresh reuse-detection)
- Evidence: "Affiliate refresh-token reuse detection missing — unlike Storefront (`TOKEN_REUSE_DETECTED`), affiliate relies only on implicit `ClearRefreshToken()` on logout/reset + `RefreshTokenExpiryTime` check; no explicit replay detection (debt)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-005: No MFA implementation across any realm
- Severity: Medium
- Category: Security Concern
- Description: No TOTP/authenticator/WebAuthn MFA exists for POS, SuperAdmin, Storefront, CommerceHub customer, or Affiliate. Phone OTP is single-factor verification, not MFA. This is a maturity/security gap flagged as debt in the IAM architecture (by design per code, worth flagging).
- Recommendation: Define an MFA enforcement policy per realm and implement TOTP/WebAuthn with enrollment/recovery; prioritize SuperAdmin and POS Owner/Admin.
- Complexity: XL
- Dependencies: none
- Affected Modules: All identity realms, `Program.cs` auth wiring
- Source Documents: platform/identity-access-management.md §2.5, Missing Capabilities; platform/identity-runtime-final-architecture.md Technical Debt #6, Implementation Follow-up (MFA)
- Evidence: "MFA — not implemented." / "No MFA, no impersonation, no stored RBAC — capability gaps vs. a full IAM platform (by design per code, but worth flagging as maturity debt)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-006: No impersonation / temporary-authority ceremony framework
- Severity: Medium
- Category: Governance Concern
- Description: Support impersonation / tenant override is explicitly PROHIBITED without a reviewed ceremony (SQL actor authority, target tenant lineage, reason/reference capture, revocation semantics, durable audit, replay-safe convergence). No such ceremony/runbook is documented or implemented. Incident-response capability depends on it.
- Recommendation: If support impersonation is ever added, implement it behind a codified ceremony service with durable audit + revocation; otherwise document the permanent prohibition explicitly.
- Complexity: L
- Dependencies: SEC-029
- Affected Modules: SuperAdmin operations, support tooling
- Source Documents: 06-security-identity.md §539 (Never Assume), §264 (Emergency), platform/identity-runtime-final-architecture.md (Impersonation Runtime VERIFIED ABSENT)
- Evidence: "Never assume support impersonation or tenant override are available features — they are PROHIBITED without a reviewed ceremony."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-007: POS tenant-admin auth/role changes not centrally audited
- Severity: Medium
- Category: Audit / Governance Concern
- Description: Only SuperAdmin operations and governance access-records hit `AuditLogs`. Normal POS role/employee changes (promotion, disable, delete) have no per-action audit trail, creating an oversight gap for privileged tenant-admin identity mutations.
- Recommendation: Route POS admin/employee role and lifecycle changes through `AuditLogs` (or an equivalent durable audit) with actor + target + reason.
- Complexity: M
- Dependencies: none
- Affected Modules: POS `AuthController`, admin controllers, `AuditLogs`
- Source Documents: platform/identity-runtime-final-architecture.md Technical Debt #11, Implementation Follow-up (Audit POS auth/role changes)
- Evidence: "Tenant-admin (POS) auth/role changes are not centrally audited — only SuperAdmin operations and governance access-records hit `AuditLogs`. Normal POS role/employee changes have no per-action audit trail (gap; governance-status changes are the partial exception via `UserGovernanceService`)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-008: Storefront `EmailVerified` not enforced at login (diverges from CommerceHub)
- Severity: Low
- Category: Authorization Gap
- Description: CommerceHub customer login 403s `EMAIL_NOT_VERIFIED` at token validation; Storefront `OnlineCustomer.EmailVerified` is informational/checkout-scoped only and not gated at login. Same-named field, different enforcement contract across realms — undocumented divergence that can mislead consumers.
- Recommendation: Decide and document the storefront contract; either enforce at login or explicitly codify that storefront login permits unverified email (with rationale).
- Complexity: S
- Dependencies: none
- Affected Modules: `StorefrontAuthController`, `StorefrontAuthService`, `OnlineCustomer`
- Source Documents: platform/identity-runtime-final-architecture.md Technical Debt #12, Divergence #8
- Evidence: "Storefront `EmailVerified` not enforced at login — CommerceHub 403s `EMAIL_NOT_VERIFIED`; storefront login gates only on `IsActive`. Inconsistent verification enforcement across realms (divergence; acceptable if intentional, but undocumented)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-009: Four realm-specific refresh/revocation mechanisms, no shared abstraction
- Severity: Medium
- Category: Technical Debt
- Description: POS (session-binding hash + rotation), Storefront (`OnlineCustomerRefreshTokens.IsRevoked`), CommerceHub customer (jti blacklist), SuperAdmin (`TokenVersion`) each implement revocation differently. Security behavior is harder to reason about consistently and fixes must be duplicated across realms.
- Recommendation: Introduce a shared revocation/session authority abstraction while preserving realm-specific storage where required.
- Complexity: XL
- Dependencies: SEC-004
- Affected Modules: All realm auth services, `PosSessionAuthority`, `StorefrontRefreshTokenService`, `CustomerTokenRevocationService`, `SuperAdminAuthService`
- Source Documents: platform/identity-access-management.md §18#5, Implementation Follow-up #2; platform/identity-runtime-final-architecture.md Technical Debt #5
- Evidence: "Four different refresh/revocation mechanisms... No shared revocation abstraction."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-010: Capability strings are UI hints, not server-enforced; drift risk
- Severity: Medium
- Category: Authorization Gap
- Description: `NavigationCapabilities` / vendor lifecycle authority strings are computed at login and returned to the client as hints. Server enforcement is scheme + role-claim + SQL authority + governance only; no `RequireClaim` enforces capability strings. Frontend-visible capabilities can drift from what the server actually permits.
- Recommendation: Either keep them explicitly documented as presentation hints with coverage tests, or promote them into a server-side permission authority.
- Complexity: L
- Dependencies: SEC-020
- Affected Modules: `AuthController.BuildCommerceHubNavigationCapabilitiesAsync`, `CommerceHubCustomerAuthService.MapProfile`, frontend
- Source Documents: platform/identity-access-management.md §3.4, §18#6, Implementation Follow-up #3; platform/identity-runtime-final-architecture.md Divergence #5
- Evidence: "The closest thing to permissions are capability strings computed at login and returned to the frontend... NOT stored, not in a permission table, and not enforced server-side as a permission system."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-011: Capability computation duplicated across auth services
- Severity: Medium
- Category: Technical Debt
- Description: Navigation/capability values are computed in multiple auth/profile paths (POS, CommerceHub customer, Storefront, SuperAdmin, affiliate). Duplicated computation can drift across realms and produce inconsistent frontend navigation/authorization presentation.
- Recommendation: Centralize capability generation per realm or add shared contract tests that verify emitted capability sets match server-enforced permissions.
- Complexity: M
- Dependencies: SEC-010
- Affected Modules: All auth services
- Source Documents: platform/identity-access-management.md §20 (Technical Debt), Implementation Follow-up #8
- Evidence: "Capability computation duplicated across `AuthController`, `CustomersAuthController`, `StorefrontAuthController` (DRY violation; risk of drift between what UI shows and server enforces)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-012: Legacy `auth_token` cookie still honored
- Severity: Low
- Category: Known Limitation
- Description: POS JWT extraction still reads the legacy `auth_token` cookie and `AuthCookieConfiguration` deletes it when setting current session cookies. The legacy compatibility path increases authentication surface and must remain tested until removed.
- Recommendation: Define a removal window, add telemetry for remaining usage, then remove the legacy cookie read path after migration.
- Complexity: S
- Dependencies: none
- Affected Modules: `Program.cs` auth wiring, `AuthCookieConfiguration`, `JwtBearer.OnMessageReceived`
- Source Documents: platform/identity-access-management.md §18#8, Implementation Follow-up #4; platform/identity-runtime-final-architecture.md Technical Debt #8
- Evidence: "Legacy cookie `auth_token`. Still read by `JwtBearer.OnMessageReceived` and explicitly deleted in favor of `access_token`... Migration artifact retained for compatibility."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-013: Dead `JwtMiddlewareConfiguration.AddHardenedJwtAuthentication`
- Severity: Low
- Category: Technical Debt
- Description: `JwtMiddlewareConfiguration.AddHardenedJwtAuthentication` is defined but never invoked (grep shows only the definition site); auth is wired inline in `Program.cs`. The class is a confusing duplicate of the live config and describes a hardened path that is not actually used, while the legacy `auth_token` cookie it references is still honored.
- Recommendation: Delete the dead class or wire it as the single source of auth configuration.
- Complexity: S
- Dependencies: SEC-012
- Affected Modules: `JwtMiddlewareConfiguration`, `Program.cs`
- Source Documents: platform/identity-runtime-final-architecture.md Technical Debt #1, Divergence #6
- Evidence: "`JwtMiddlewareConfiguration.AddHardenedJwtAuthentication` is DEAD code — never invoked (grep: only the method definition; `Program.cs` wires auth inline)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-014: Non-account `ManualWalletUploadTokenAuthenticationHandler` parallel auth path
- Severity: Medium
- Category: Architecture Issue
- Description: `ManualWalletUploadTokenAuthenticationHandler` is registered as an authentication scheme for manual wallet upload/session tokens — a parallel authentication path outside the five account-backed identity realms. Architecture discussions that count only account realms may miss a privileged authentication path that privileged upload endpoints can use.
- Recommendation: Document all non-account token schemes in a dedicated capability/session-token section and keep endpoint coverage tests for them.
- Complexity: M
- Dependencies: none
- Affected Modules: `ManualWalletUploadTokenAuthenticationHandler`, checkout-session upload endpoints
- Source Documents: platform/identity-access-management.md §18#10, Implementation Follow-up #5
- Evidence: "A parallel (test-only) identity issuer exists in the same codebase... `ManualWalletUploadTokenAuthenticationHandler` is registered as an authentication scheme for manual wallet upload/session tokens... it is a parallel authentication path that privileged upload endpoints can use."
- Implementation Required: no (documentation + test coverage)
- Architecture Only: no
- Documentation Only: yes

### SEC-015: Overlapping customer identity models require canonical reconciliation
- Severity: Medium
- Category: Known Limitation
- Description: `OnlineCustomer` (tenant-scoped) and `CommerceHubCustomer` (global) are separate bounded contexts. Cross-context customer identity depends on a background reconciliation loop (`CanonicalIdentityReconciliationService`) and conflict detection. The two "customer" realms are isolated differently and consumers must not assume storefront tenant-isolation applies to CommerceHub customers.
- Recommendation: Keep canonical-link health checks operational, add explicit conflict runbooks, and avoid treating either customer table as the universal customer identity.
- Complexity: L
- Dependencies: SEC-016
- Affected Modules: `CanonicalIdentityReconciliationService`, `OnlineCustomer`, `CommerceHubCustomer`, `CanonicalIdentityLink`
- Source Documents: platform/identity-access-management.md §18#1/#7, Implementation Follow-up #6; platform/identity-runtime-final-architecture.md §635
- Evidence: "Overlapping customer identity models (`OnlineCustomer` vs `CommerceHubCustomer`) require canonical links to correlate tenant-scoped storefront identity with global marketplace identity."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-016: Canonical identity reconciliation conflict resolution is manual
- Severity: Medium
- Category: Monitoring Gap
- Description: `CanonicalIdentityReconciliationService` runs after a 30-second startup delay and then every 5 minutes. It creates missing `CanonicalIdentityLink` rows and logs conflicts (`LogCritical`) when existing links disagree, but relies on manual resolution — there is no automated remediation or alerting runbook for identity-graph conflicts.
- Recommendation: Add automated conflict triage/alerting + a documented reconciliation runbook; surface conflict counts to monitoring.
- Complexity: M
- Dependencies: SEC-015
- Affected Modules: `CanonicalIdentityReconciliationService`, monitoring
- Source Documents: platform/identity-access-management.md §15, §18#7; platform/identity-runtime-final-architecture.md (CQRS section)
- Evidence: "`CanonicalIdentityReconciliationService`... logs conflicts when existing links disagree. This is runtime synchronization/repair and conflict detection; by itself it is not proof of a one-time migration-only path."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-017: CommerceHub customer & affiliate are GLOBAL (cross-tenant by design)
- Severity: Medium
- Category: Known Limitation
- Description: `CommerceHubCustomer` has no `TenantId` (global `BaseEntity`); `AffiliateAccount.TenantId` is audit-only. Unlike tenant-scoped `OnlineCustomer`, these identities are NOT isolated by tenant — they are platform/global identities. This is the single largest identity-isolation divergence and must be called out in every consumer to prevent incorrect tenant-isolation assumptions.
- Recommendation: Mark CommerceHub customer / affiliate as platform-global in all consumers; add explicit test assertions that global identity surfaces do not inherit storefront tenant isolation.
- Complexity: M
- Dependencies: SEC-015
- Affected Modules: `CommerceHubCustomer`, `AffiliateAccount`, all consumers of these ids
- Source Documents: platform/identity-access-management.md §1.3/#4, §18#7; platform/identity-runtime-final-architecture.md §635-639, Technical Debt #9
- Evidence: "CommerceHub customers and affiliates are cross-tenant by design — they are platform/global identities, not tenant children. This is the single largest identity-isolation divergence in the system."
- Implementation Required: no (documentation + test coverage)
- Architecture Only: no
- Documentation Only: yes

### SEC-018: No unified security-stamp abstraction
- Severity: Low
- Category: Technical Debt
- Description: Three realm-specific revocation primitives exist instead of a unified security stamp: POS `pos_session` hash, customers/affiliate `TokensValidAfterUtc` cutoff, SuperAdmin `TokenVersion`. No shared abstraction; divergence from canonical ASP.NET Identity `SecurityStamp`.
- Recommendation: Introduce a shared `ISessionStamp` abstraction wrapping the three mechanisms where feasible.
- Complexity: L
- Dependencies: SEC-009
- Affected Modules: `PosSessionAuthority`, `OnlineCustomer`, `SuperAdminUser`, `AffiliateUser`
- Source Documents: platform/identity-runtime-final-architecture.md Technical Debt #5, Implementation Follow-up (Unified security stamp)
- Evidence: "No unified security stamp — three realm-specific equivalents (`pos_session`, `TokensValidAfterUtc`, `TokenVersion`); no shared abstraction; divergence from canonical ASP.NET Identity."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-019: Test-auth issuer coexists with production issuers
- Severity: Medium
- Category: Governance Concern
- Description: `TestAuthController` / `TestingOnlyControllerFeatureProvider` is the token issuer for test harnesses and is stripped from non-test environments (`Program.cs:150-158`), but a parallel (test-only) identity issuer exists in the same codebase as production issuers. Misconfiguration could expose the test issuer in production.
- Recommendation: Verify the test issuer is gated behind a compile/ENV flag that cannot be enabled in production builds; add a startup guard/assertion.
- Complexity: S
- Dependencies: none
- Affected Modules: `TestAuthController`, `TestingOnlyControllerFeatureProvider`, `Program.cs`
- Source Documents: platform/identity-access-management.md §18#9
- Evidence: "Test-auth issuer. `TestAuthController` / `TestingOnlyControllerFeatureProvider` is the token issuer for test harnesses, stripped from non-test environments... A parallel (test-only) identity issuer exists in the same codebase."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-020: No centralized permission/RBAC system; authorization logic distributed
- Severity: Medium
- Category: Architecture Issue
- Description: Verified absent: no `IPermissionService`, no `Permission` table/enum, no role-hierarchy engine. Authorization is implemented across JWT schemes, policies, controller role metadata, `AdminTenantAuthority`, `PosSessionAuthority`, `SuperAdminPlatformAuthority`, token services, and governance services. Effective authorization requires tracing several layers, making regressions easier when new endpoints are added.
- Recommendation: Create a unified authorization/authority map or abstraction that endpoint tests can assert against without changing runtime behavior immediately; document the layering explicitly.
- Complexity: XL
- Dependencies: SEC-010
- Affected Modules: All authorization surfaces
- Source Documents: platform/identity-access-management.md §3/#8, §18#2, §20, Implementation Follow-up #7; platform/identity-runtime-final-architecture.md (Permission Runtime VERIFIED ABSENT)
- Evidence: "No centralized permission system; authorization logic spread across controllers, `AdminTenantAuthority` (regex role extraction), policies, and `UserGovernanceService`."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-021: CommerceHub price override designed/frozen but not implemented/activated
- Severity: High
- Category: Security Concern
- Description: CommerceHub price override is DESIGNED and FROZEN but NOT IMPLEMENTED/activated. `OverridesEnabled=false`; `CommerceHubPrice` is rejected/cleared. Any apparent "activation" without the full PASS 13.6 control list is a defect. Marketplace governance may NOT approve merchant prices (audit != approval). This is a financial/security control whose premature activation is a real risk.
- Recommendation: Keep disabled until the full PASS 13.6 control list is implemented + certified; add a deploy-time guard that fails closed if override flags are enabled without certification evidence.
- Complexity: L
- Dependencies: none
- Affected Modules: CommerceHub pricing, marketplace governance, `CommerceHubPriceAuthority`
- Source Documents: 06-security-identity.md §501-540 (Business Invariants, Never Assume), §714-748 (Conflicts & Honest Gaps #4)
- Evidence: "CommerceHub price override is DESIGNED and FROZEN but NOT IMPLEMENTED/activated. It is disabled by multiple independent safety locks. Any apparent 'activation' without the full PASS 13.6 control list is a defect."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-022: No universal parity / tenant-isolation scanner; legacy surfaces uncertified
- Severity: Medium
- Category: Validation Gap
- Description: There is no global SQL-vs-derivative parity scanner or generated route/scope inventory. Certified guarantees are deterministic convergence and diagnosability under pressure, NOT universal path coverage. Some legacy reporting / analytics / support / cache-warmer / export surfaces still require path-specific certification.
- Recommendation: Build a generated route/scope inventory + tenant-isolation parity checker that flags any endpoint lacking `(Id,TenantId)` SQL predicates on hardened admin paths; prioritize the legacy surfaces named.
- Complexity: L
- Dependencies: none
- Affected Modules: Reporting, analytics, support, cache-warmer, export surfaces
- Source Documents: 06-security-identity.md §714-748 (Conflicts & Honest Gaps #2)
- Evidence: "No universal parity / tenant-isolation scanner. There is no global SQL-vs-derivative parity scanner or generated route/scope inventory. Certified guarantees are deterministic convergence... NOT universal path coverage. Some legacy reporting / analytics / support / cache-warmer / export surfaces still require path-specific certification."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-023: Partial repo-wide dangerous-primitive governance
- Severity: High
- Category: Governance Concern
- Description: The focused architecture source guard is green for reviewed raw-SQL / `ReplaceOneAsync` / raw-ES / Redis-pattern boundaries, but some specialized projection paths still use equivalent LOCAL SQL-lineage checks rather than the shared helpers. Future raw primitives can be normalized into new files only if engineers extend the allowlist + assertions in the same change; otherwise it is a regression risk.
- Recommendation: Route all dangerous primitives through the shared guards (`DerivativeProjectionMutationGuard`, `ProjectionEventFence`, `SqlTruthRebuildGuard`); add a lint/CI check that fails when a new raw primitive lacks an allowlist entry + assertion.
- Complexity: L
- Dependencies: SEC-022
- Affected Modules: Projection consumers, rebuild/recovery workers, dangerous-primitive paths
- Source Documents: 06-security-identity.md §714-748 (Conflicts & Honest Gaps #1)
- Evidence: "Partial repo-wide dangerous-primitive governance. The focused architecture source guard is green for the reviewed raw-SQL / `ReplaceOneAsync` / raw-ES / Redis-pattern boundary, but some specialized projection paths still use equivalent LOCAL SQL-lineage checks rather than the shared helpers."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-024: CommerceHub public-launch & customer-identity readiness NOT certified (residual launch debt)
- Severity: High
- Category: Validation Gap
- Description: Residual launch blockers remain unverified: refresh-token rotation + reuse detection, identity dashboards / alerts, external WORM/SIEM retention, SMTP/deployment proof, PostgreSQL-backed auth chaos harness, evidence-table role provisioning verification, backup/restore/rehearsal evidence. These are accepted launch debt, not authority-model defects, but QA must not treat compiled runtime code as launch-readiness proof.
- Recommendation: Track each launch-debt item as a gating checklist; close WORM/SIEM retention + backup/rehearsal evidence + auth chaos harness before public launch.
- Complexity: L
- Dependencies: SEC-004, SEC-005, SEC-016
- Affected Modules: Identity, audit/forensic evidence, infrastructure, deployment
- Source Documents: 06-security-identity.md §714-748 (Conflicts & Honest Gaps #3)
- Evidence: "CommerceHub public-launch and customer-identity launch readiness are NOT certified. Residual launch blockers remain: refresh-token rotation + reuse detection, identity dashboards / alerts, external WORM/SIEM retention, SMTP/deployment proof, PostgreSQL-backed auth chaos harness, evidence-table role provisioning verification, backup/restore/rehearsal evidence."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-025: `AllowAnonymousTenant` is the sole sanctioned tenant-stripping exception
- Severity: High
- Category: Authorization Gap
- Description: Webhook `AllowAnonymousTenant` controllers are the one sanctioned exception to tenant-middleware stripping. It is safe ONLY because the webhook trust pipeline establishes tenant scope from the signed payload + stored SQL records. A NEW anonymous-tenant endpoint without that pipeline is a critical scope defect (would resolve tenant from an untrusted source).
- Recommendation: Add a code/review gate that forces any `[AllowAnonymousTenant]` endpoint to demonstrate the webhook trust pipeline (signed payload + SQL record scope resolution) or it fails review.
- Complexity: M
- Dependencies: none
- Affected Modules: `TenantResolutionMiddleware`, webhook controllers, webhook trust pipeline
- Source Documents: 06-security-identity.md §714-748 (Conflicts & Honest Gaps #5), §369-403 (Webhook Security)
- Evidence: "Webhook `AllowAnonymousTenant` is the single sanctioned exception to tenant middleware stripping. It is safe ONLY because the webhook trust pipeline establishes tenant scope from the signed payload + stored SQL records. A new anonymous-tenant endpoint without that pipeline is a critical scope defect."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-026: Service Marketplace provider status limited to `IsActive` boolean
- Severity: Medium
- Category: Known Limitation
- Description: The Service Marketplace provider (`MarketplaceProfile`) has only an `IsActive` boolean — there is NO Pending/Verified/Suspended/Blocked/Rejected/Closed/Archived status machine. Provider suspension/visibility is only indirect via tenant vendor-governance (`Suspended`/`Banned`). There is no first-class, marketplace-owned provider suspension inside this runtime.
- Recommendation: If provider-level governance is required, add a first-class provider status machine owned by the marketplace runtime and reconcile it with tenant vendor-governance suppression.
- Complexity: L
- Dependencies: none
- Affected Modules: `MarketplaceProfile`, `ServiceCatalogService`, `ServiceRequestService`, `MarketplaceSearchService`
- Source Documents: platform/service-marketplace-participation-runtime-final-architecture.md (Provider Status — Implementation Reality, §296-369)
- Evidence: "There is no provider suspend/restore state machine in this runtime... `Reject` and `Cancel` are request-level transitions, not provider-status transitions... Vendor Suspension restricts current CommerceHub marketplace participation... (owned elsewhere)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-027: Service Marketplace global Elasticsearch index is tenant-UNSCOPED for discovery
- Severity: Informational
- Category: Known Limitation
- Description: The `marketplace_services` Elasticsearch index is intentionally tenant-UNSCOPED for read/search visibility (`Tenant filter REMOVED for Marketplace (Global visibility)`), so services from any tenant are globally discoverable. Engagement (request/chat/review/trust) is always re-scoped to current SQL tenant/profile authority before shown or mutated. The cross-tenant READ exposure is intentional but requires continuous monitoring that it never leaks into the write path.
- Recommendation: Add an assertion/test that global discovery read never grants another tenant write authority over a provider's data; keep the re-scope enforcement verified.
- Complexity: M
- Dependencies: SEC-017
- Affected Modules: `MarketplaceSearchService`, `ServiceDiscoveryProjectionConsumer`, `ServiceRequestService`
- Source Documents: platform/service-marketplace-participation-runtime-final-architecture.md (Executive Summary, Core Discovery, Runtime Purpose)
- Evidence: "Global discovery is deliberate but read-only. The Elasticsearch service index (`marketplace_services`) is intentionally tenant-UNSCOPED for search visibility... But the engagement (request/chat/review/trust) is always re-scoped to current SQL tenant/profile authority before it is shown or mutated."
- Implementation Required: no (monitoring + test coverage)
- Architecture Only: no
- Documentation Only: yes

### SEC-028: Impersonation/temporary-authority (debug/support) absent
- Severity: Low
- Category: Recommendation
- Description: No SuperAdmin temporary-authority / impersonation endpoint exists (verified absent). This is a gap for incident response and support debugging. If ever added it must carry durable audit + revocation.
- Recommendation: Treat as a deliberate omission; if product requires it, implement behind the SEC-006 ceremony with full audit.
- Complexity: M
- Dependencies: SEC-006
- Affected Modules: SuperAdmin operations
- Source Documents: platform/identity-runtime-final-architecture.md (Impersonation Runtime VERIFIED ABSENT), Implementation Follow-up (Impersonation audit)
- Evidence: "Impersonation (VERIFIED ABSENT)... No SuperAdmin/tenant/employee/customer impersonation, no temporary-authority grant, and no impersonation audit trail exists in the backend."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### SEC-029: Emergency / break-glass has no single codified runbook reference
- Severity: Medium
- Category: Governance Concern
- Description: Multiple docs state emergency/break-glass work remains "operational convergence authority" and must use a "reviewed ceremony" (SQL actor authority, target tenant lineage, reason/reference capture, revocation semantics, durable audit, replay-safe convergence). No single consolidated runbook is referenced; the ceremony is described per-doc rather than in one governed artifact.
- Recommendation: Author a single Emergency Operations Runbook codifying the ceremony, required approvers, audit fields, and revocation, and link it from every relevant guide.
- Complexity: M
- Dependencies: SEC-006
- Affected Modules: SuperAdmin emergency operations, dangerous primitives
- Source Documents: 06-security-identity.md §251-275 (Super Admin Rules, Dangerous Operations); systems/super-admin/README.md (Marketplace Emergency Operations)
- Evidence: "Emergency / break-glass authority is OPERATIONAL authority, not business authority... No silent operator bypass, stale-session elevation, or undocumented emergency mutation path." (ceremony described but not consolidated)
- Implementation Required: no (documentation/governance)
- Architecture Only: no
- Documentation Only: yes

### SEC-030: Settlement header-count prose vs live settlement-lineage rule
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Older settlement prose trusted denormalized settlement header counts (`PaymentSettlementBatch.OrderCount`) as settlement membership authority. The live rule overrides this: settlement membership is live SQL `PaymentSettlementOrders` lineage, not denormalized header counts. The 08-qa-philosophy doc notes this is "resolved" toward the live rule, but the older prose persists in the corpus and can mislead.
- Recommendation: Purge/annotate the older settlement header-count prose so only the live lineage rule remains authoritative.
- Complexity: S
- Dependencies: none
- Affected Modules: Settlement, `PaymentSettlementOrders`, reporting
- Source Documents: 08-qa-philosophy.md §693-695 (Conflict note); 05-orders.md §406 (PASS 4 correction)
- Evidence: "older settlement prose that trusted denormalized settlement header counts is overridden by the live settlement-lineage rule."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### SEC-031: Canonical identity reconciliation lag window
- Severity: Informational
- Category: Known Limitation
- Description: `CanonicalIdentityReconciliationService` runs every 5 minutes (after a 30s startup delay). There is a bounded window where cross-context customer identity links are stale. This is acceptable by design but must be monitored so cross-realm identity resolution does not serve inconsistent capability/visibility during the lag.
- Recommendation: Document the reconciliation cadence as an accepted tradeoff and monitor conflict/link-staleness metrics.
- Complexity: S
- Dependencies: SEC-016
- Affected Modules: `CanonicalIdentityReconciliationService`
- Source Documents: platform/identity-access-management.md §18#7; platform/identity-runtime-final-architecture.md (CQRS)
- Evidence: "A background hosted service runs after a 30-second startup delay and then every 5 minutes."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### SEC-032: "Vendor" term overloaded (Tenant in governance vs User operator)
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `UserGovernanceRole.Vendor` resolves to the `Tenants` table (a Tenant is a governed "vendor"), while POS operators are `User`s. A tenant is simultaneously the isolation boundary AND a governed vendor identity — two notions of vendor coexist, creating scope/governance confusion in documentation and consumer code.
- Recommendation: Disambiguate "vendor" terminology in docs/code (Tenant-as-Vendor vs User-operator) and add a glossary cross-reference.
- Complexity: S
- Dependencies: none
- Affected Modules: `UserGovernanceService`, tenant governance, POS admin
- Source Documents: platform/identity-access-management.md §1.3/#4, §18#3
- Evidence: "`Vendor` = Tenant in governance but Operator = User. `UserGovernanceRole.Vendor` resolves to the `Tenants` table... while POS operators are `User`s. A tenant is simultaneously the隔离 boundary and a governed 'vendor' identity — two notions of vendor coexist."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### SEC-033: No external WORM/SIEM retention for forensic evidence tables
- Severity: High
- Category: Governance Concern
- Description: Certified evidence tables are PostgreSQL-enforced append-only (triggers reject UPDATE/DELETE/TRUNCATE with `FORENSIC_APPEND_ONLY_VIOLATION`). However external WORM/SIEM retention of these forensic/audit/identity events is listed as residual launch debt and is NOT yet in place, limiting long-term tamper-evident retention and incident forensics.
- Recommendation: Stand up external WORM/SIEM retention + shipping of `AuditLogs`/forensic events before public launch; verify retention cannot be mutated from the app tier.
- Complexity: L
- Dependencies: SEC-024
- Affected Modules: `AuditLogs`, forensic evidence tables, SIEM/WORM export
- Source Documents: 06-security-identity.md §508-511 (append-only), §714-748 (#3 launch debt)
- Evidence: "Certified evidence tables are PostgreSQL-enforced append-only... external WORM/SIEM retention [is] residual launch debt."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### SEC-034: No PostgreSQL-backed auth chaos harness
- Severity: Medium
- Category: Validation Gap
- Description: Refresh-token rotation + reuse detection, identity dashboards/alerts, and a PostgreSQL-backed auth chaos harness are residual launch debt. Without an auth-chaos harness, the fail-closed revocation/refresh behaviors (rotation, replay rejection, governance-ban revocation) are not continuously exercised against the live SQL authority under adversarial conditions.
- Recommendation: Build an auth-chaos harness that rotates/revokes/replays tokens against live SQL authority and asserts fail-closed convergence; wire into CI.
- Complexity: L
- Dependencies: SEC-024, SEC-004
- Affected Modules: Auth/renewal paths, `PosSessionAuthority`, `CustomerTokenRevocationService`, `SuperAdminAuthService`
- Source Documents: 06-security-identity.md §714-748 (#3 launch debt)
- Evidence: "residual launch blockers remain: ... PostgreSQL-backed auth chaos harness, evidence-table role provisioning verification..."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

---

## DOCUMENTATION CONFLICTS / CONTRADICTIONS

### CONFLICT: Tenant `IsActive` vs `Tenant.Status` dual source of truth
- Conflicting Documents: platform/identity-runtime-final-architecture.md (Technical Debt #2, §624) vs itself (Runtime Authority / Tenant Identity Runtime §306-315)
- Explanation: The tenant state is represented by both a legacy `IsActive` bool and a `Status` enum. `TenantsController.SetTenantStatus` writes only the bool, while `PrivilegedRefreshTokenRevocationInterceptor` keys user revocation off `IsActive` and the SuperAdmin governance path keys off `Status`. The two code paths can disagree about whether a tenant is active/suspended.
- Possible Cause: Incremental evolution — the bool predates the enum; the interceptor and governance were written against different signals and never reconciled.
- Recommended Resolution: Make `Status` the single authority (see SEC-002); deprecate `IsActive` writes; update the interceptor and `SetTenantStatus` together.

### CONFLICT: Ghost "Accountant" role in live authorization metadata
- Conflicting Documents: platform/identity-access-management.md §9.2 / §18#2 / §20 vs `UserRole` enum (referenced in same doc §1.2, §9)
- Explanation: Endpoint `[Authorize(Roles="Owner,Admin,Accountant")]` and `AdminTenantAuthority.TenantActorRoles` include `"Accountant"`, but the `UserRole` enum has no `Accountant` value. A role participates in a live authorization boundary yet can never be assigned — the metadata contradicts the role model.
- Possible Cause: Incomplete role extension (an intended `Accountant` role was partially wired into controllers/authority but never added to the enum or assignment flows).
- Recommended Resolution: Add `Accountant` to `UserRole` with assignment + tests, or strip the literal from controller metadata and `TenantActorRoles` (see SEC-001).

### CONFLICT: Storefront vs CommerceHub `EmailVerified` enforcement
- Conflicting Documents: platform/identity-runtime-final-architecture.md Divergence #8 / Technical Debt #12 vs CommerceHub customer enforcement same doc §487-489
- Explanation: CommerceHub customer login enforces `IsEmailVerified` (403 `EMAIL_NOT_VERIFIED` at token validation); Storefront `OnlineCustomer.EmailVerified` is informational/checkout-scoped and NOT gated at login. The same-named field carries different enforcement contracts across realms.
- Possible Cause: Realm-specific verification policies evolved independently; storefront historically permitted unverified-email login.
- Recommended Resolution: Decide and document the storefront contract; either enforce at login or codify the non-enforcement with rationale (see SEC-008).

### CONFLICT: Dead hardened JWT config vs live legacy cookie path
- Conflicting Documents: platform/identity-runtime-final-architecture.md Technical Debt #1 / Divergence #6 vs §204/§139 (legacy `auth_token` honored)
- Explanation: `JwtMiddlewareConfiguration.AddHardenedJwtAuthentication` is dead code (never invoked), yet the legacy `auth_token` cookie it references is still read/honored in `Program.cs`. The documented "hardened" configuration path does not run, while the legacy compatibility path does.
- Possible Cause: Auth wiring was inlined into `Program.cs`, orphaning the hardened-config class; the legacy cookie read was retained for migration compatibility.
- Recommended Resolution: Delete the dead class or wire it as the single source; remove the legacy cookie read after migration (see SEC-012, SEC-013).

### CONFLICT: Capability hints vs server-enforced permissions
- Conflicting Documents: platform/identity-access-management.md §3.4 / §18#6 vs §8 (Permission Engine VERIFIED ABSENT)
- Explanation: `NavigationCapabilities` strings are sent to the client as UI hints and can represent permissions the server does not actually enforce via `RequireClaim`. Frontend-visible capabilities can drift from backend-enforced authorization, creating a presentation-vs-truth contradiction.
- Possible Cause: Capabilities were introduced as UX hints before a server permission authority existed; no enforcement bridge was built.
- Recommended Resolution: Either document capabilities as non-authoritative hints with coverage tests, or promote them into a server-side permission authority (see SEC-010).

### CONFLICT: Settlement header counts (older prose) vs live settlement-lineage rule
- Conflicting Documents: 08-qa-philosophy.md §693-695 (conflict note) vs older settlement prose in payments/orders guides (denormalized `OrderCount`/`PaymentSettlementBatch.OrderCount`)
- Explanation: Older settlement documentation treats denormalized settlement header counts as membership/authority. The live rule overrides this: settlement membership is live SQL `PaymentSettlementOrders` lineage. The 08 doc states this is "resolved" toward the live rule, but the older prose persists and can mislead.
- Possible Cause: Rule refinement after earlier documentation was written; old prose not purged.
- Recommended Resolution: Annotate/purge the older header-count prose so only the live lineage rule remains (see SEC-030).

### CONFLICT: "Vendor" overloaded — Tenant (governance) vs User (operator)
- Conflicting Documents: platform/identity-access-management.md §1.3/#4 / §18#3 vs POS Admin operator model (§1.2, §3)
- Explanation: In governance, `UserGovernanceRole.Vendor` resolves to the `Tenants` table (a tenant IS the governed vendor), while POS operators are `User`s. The word "vendor" therefore denotes both an isolation boundary (tenant) and a governed participant, conflicting in scope discussions.
- Possible Cause: Marketplace model maps tenant→vendor for CommerceHub participation; the operator vs tenant distinction was not consistently named.
- Recommended Resolution: Disambiguate terminology in docs/glossary and code comments (see SEC-032).

### CONFLICT: CommerceHub price-override "disabled" vs existence of a controlled-activation path
- Conflicting Documents: 06-security-identity.md §501-540 / §714-748 (#4) vs commercehub-price-override-* certification doc family (controlled-activation / business-model-correction)
- Explanation: The security guide states price override is disabled by multiple independent safety locks and any activation without full PASS 13.6 control list is a defect, yet a family of "controlled-activation" / "business-model-correction" certification docs implies an activation path exists. The tension between "frozen/disabled" and "activation path designed" can be read as contradictory if a partial activation ships.
- Possible Cause: The feature is architecturally designed + frozen but intentionally not activated; the activation docs describe the future gated path, not current state.
- Recommended Resolution: Keep override disabled until PASS 13.6 is fully implemented + certified; add a deploy-time fail-closed guard; clarify in docs that activation docs are forward-looking, not current behavior (see SEC-021).

---

## SUMMARY STATS
- Total SEC issues: 34 (SEC-001 … SEC-034)
- By severity: High 9 (001,002,021,023,024,025,033 + medium-flagged 005? no — High = 001,002,021,023,024,025,033 = 7 High; the rest Medium/Low/Informational)
  - High: SEC-001, SEC-002, SEC-021, SEC-023, SEC-024, SEC-025, SEC-033
  - Medium: SEC-003,004,005,006,007,009,010,011,014,015,016,017,018,019,020,022,026,027(info),029,032,034
  - Low: SEC-008, SEC-012, SEC-013
  - Informational: SEC-017, SEC-027, SEC-028, SEC-031
- Documentation conflicts detected: 8 (CONFLICT × 8)
- Cross-cutting theme: authorization model is sound (SQL-authority revalidation, fail-closed) but UNDERGOVERNED as code — no RBAC/permission engine, ghost roles, distributed authz logic, missing MFA/reuse-detection/audit on some realms, and unresolved launch/monitoring debt.

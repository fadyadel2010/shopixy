# Shopixy AI QA Onboarding — Security, Identity & Authority Model

Official onboarding guide for a Senior QA Engineer protecting Shopixy's security boundaries,
authority model, tenant isolation, and financial correctness.

This document is distilled from the authoritative `.ai-context/*` files and
`docs/architecture/*` certification findings. It contains ONLY durable architectural and
business-security rules. It is intentionally free of temporary implementation details,
test names as code trivia, migration version numbers, and generic OWASP checklist filler.

Where the authoritative sources disagree or leave honest residual gaps, that is called out
explicitly under "Conflicts & Honest Gaps".

---

# Purpose

Give a Senior QA Engineer a single, trustworthy mental model of how Shopixy decides:

- who a caller is (Identity),
- what they are allowed to do (Authentication / Authorization),
- which tenant their work belongs to (Tenant Resolution / Isolation),
- which system state is allowed to be treated as truth (Authority Boundaries),
- what must happen when truth is missing, stale, or uncertain (Fail Closed),
- how money and inventory may move (Business Invariants),
- which operations are dangerous and require explicit platform/runbook scope (Dangerous Operations).

Use this guide to design adversarial tests, review security-sensitive changes, and judge
whether a behavior is a real defect or an intentional accepted tradeoff.

---

# Security Philosophy

1. PostgreSQL is the authoritative internal write model and the system of record. The SQL
   commit boundary is the internal durability boundary.
2. MongoDB, Redis, Elasticsearch, RabbitMQ/MassTransit, outbound provider state, and
   frontend/client state are DERIVATIVE evidence only. They may suggest or cache, but they
   may never decide business, visibility, financial, or authority truth.
3. Authority is RECOMPUTED at request time from current SQL state. It is never trusted from
   a token, cookie, cache, projection, provider callback, or stale session alone.
4. The platform has two authority domains:
   - Platform / global authority — may intentionally cross tenants ONLY under explicit
     `RequireSuperAdmin` policy plus current SQL platform-actor validation.
   - Tenant authority — may NEVER cross tenants.
5. Fail closed, never fail open. When a required fact, authority, version, or policy is
   missing or invalid, block, delete, tombstone, no-op, or route to reconciliation. Silent
   allow is a defect.
6. Default decision rule: if speed conflicts with authority, tenant isolation, ledger
   integrity, event consistency, or reconciliation safety, choose the safer design and
   document the tradeoff.
7. Replay, rebuild, reindex, repair, recovery, auto-healing, and cache-warmers are
   convergence tooling, not authority. They may discover candidates but the final mutation
   must revalidate current SQL truth and converge by apply / delete / tombstone / no-op /
   retry / block / reconciliation-required.
8. Do not introduce shadow or duplicate authority. Extend the existing boundary; do not
   create a second write path for payment, order, fulfillment, inventory, refund, payout,
   tenant-resolution, or accounting behavior.

---

# Identity Model

Four backend-owned identity surfaces exist. PostgreSQL is identity authority for all of them.
JWTs, cookies, refresh credentials, session caches, Redis state, and role/tenant claims are
CANDIDATE EVIDENCE ONLY.

- POS / Admin tenant actors — `Users` plus current hashed POS refresh-session binding.
- Super Admin platform actors — `SuperAdmins` plus `TokenVersion` plus current governance state.
- CommerceHub customers — `CommerceHubCustomers` (global SQL authority), plus verification
  state, governance state, and JWT JTI / durable session cutoff.
- Vendors — a POS tenant actor operating CommerceHub merchant surfaces; scope is derived from
  the current backend POS session, not from frontend state.
- Affiliate portal — a SEPARATE non-POS-admin identity surface; it still warrants its own
  refresh/session certification and must not be assumed to share the POS operational boundary.

Durable identity rules:

- CommerceHub customer registration commits customer identity AND the required governance
  profile in ONE SQL transaction. Concurrent duplicate registration collapses to `EMAIL_EXISTS`.
- Customer access JWTs are HttpOnly-cookie-backed, CSRF-protected, JTI-revocable, and
  subordinate to the durable `TokensValidAfterUtc` cutoff plus current customer governance.
- Suspended or banned customers must not obtain or retain authenticated access.
- Identity lifecycle evidence is append-only SQL evidence (protected by EF guards and, where
  applicable, PostgreSQL trigger enforcement).
- Frontend identity presentation (route guards, cached role, hidden buttons, React/Zustand
  state, browser storage) is presentation only and can never become business authority.

---

# Authentication Rules

POS / Admin:
- Default bearer authentication recomposes current SQL user, role, tenant ownership, current
  session binding, active tenant lineage, and current vendor-governance rejection through
  `PosSessionAuthority` before access is accepted.
- Raw JWT claims alone are not sufficient; the current SQL session/actor state is revalidated
  on every bearer request.

Super Admin:
- Every `RequireSuperAdmin` endpoint passes `SuperAdminPlatformAuthorityMiddleware`, which
  reloads current SQL `SuperAdmins`, current `TokenVersion`, and current governance status
  BEFORE controller execution — including controllers outside `/api/super-admin/*`.

CommerceHub customer:
- Login is the ONLY customer session-creation path. Registration and email verification do
  NOT issue tokens or set auth cookies.
- Unverified customers cannot authenticate (`EMAIL_NOT_VERIFIED`).
- Protected customer routes explicitly require the `CustomerJwt` scheme and `Customer` role.
  Frontend route guards are UX only and are not backend authorization.

Refresh authority (POS):
presented raw refresh credential → hash → matches current `Users.RefreshToken` hash →
non-expired binding → active user → active tenant lineage → current vendor governance.

Refresh authority (Super Admin):
presented raw refresh credential → current `SuperAdmins.RefreshTokenHash` → non-expired binding
→ current `TokenVersion` → current admin governance.

Refresh is no longer stronger than access-token authority for privileged paths. A still-valid
refresh credential is NOT enough to mint a new platform or POS session after a governance
restriction, role change, disable/delete, tenant suspension, or vendor ban.

---

# Authorization Rules

- Authorization is backend-owned and decided at request time. Frontend role, permission,
  feature flag, route guard, hidden control, disabled control, React/Zustand state, DevTools
  edits, cached permission, and decoded JWT are NEVER authorization.
- JWT role and tenant claims are evidence only. Current SQL `Users` / `SuperAdmins`, active
  tenant lineage, and current role decide privileged eligibility.
- `AdminTenantAuthorityMiddleware` revalidates the authenticated tenant actor against current
  SQL user authority, trusted tenant context, active tenant lineage, and current role.
- All platform/global work requires `RequireSuperAdmin`. Tenant-admin routes must NOT branch
  on the raw `SuperAdmin` role string to perform global reindex, repair, cache, search,
  finance, or governance work.
- Marketplace governance owns VISIBILITY only. It may hide, suppress, suspend, or reindex, but
  it may NOT approve, reject, create, or edit merchant prices. Audit must never become approval.
- `SuperAdminOperationalAuditMiddleware` records request-level operational audit for every
  non-read `RequireSuperAdmin` operation AFTER `SuperAdminPlatformAuthorityMiddleware`
  recomposes current SQL actor authority.
- Hidden buttons / removed CSS / disabled controls can be exposed and clicked by a curious
  user, but the request still crosses the backend authority boundary, so they are not a
  security control.

---

# Tenant Resolution

- Inbound `X-Tenant-Id` is STRIPPED by `TenantResolutionMiddleware`. The header is re-added
  only as a compatibility output after trusted resolution. It is never controller-local
  mutation authority.
- Trusted tenant context is the request authority. Tenant-admin code must consume
  `ITenantContext.RequiredTenantId`.
- Controller-local parsing of `X-Tenant-Id` for tenant-admin operational work is an UNSAFE
  scope primitive and a regression-freeze violation.
- On sensitive finance / shipping / category / affiliate / coupon / audit / payout admin
  paths, scope must be bound with an explicit SQL predicate `(Id, TenantId)`, not a
  primary-key-only `FindAsync(...)` lookup or an ambient EF tenant filter alone.
- A mixed endpoint that accepts BOTH an external callback credential AND an authenticated
  tenant actor must bind the authenticated tenant identity to trusted tenant context BEFORE
  it bypasses the external credential check. The external credential remains the authority for
  the callback path; the authenticated actor remains the authority for the admin path.
- Tenant-owned category admin, finance configuration, shipping methods, WhatsApp
  account/template update, and affiliate admin all require explicit trusted tenant binding in
  SQL.
- Global affiliate taxonomy mutation (create/update/activate/deactivate affiliate categories)
  is PLATFORM authority behind `RequireSuperAdmin`. Tenant admins may read the shared taxonomy
  but may not mutate it.

---

# Trust Boundaries

- Frontend / React / React Query cache / LocalStorage / SessionStorage / DevTools / history /
  BFCache / cross-tab BroadcastChannel / SignalR = derivative presentation and revalidation
  signals only. They cannot create, restore, promote, or extend authority.
- Backend authority begins at request authentication/session validation and remains decisive
  through services, business rules, and persistence.
- Provider / external / carrier / webhook callbacks = external evidence only. They never
  overwrite local SQL lifecycle, payment, order, fulfillment, inventory, or accounting truth.
- Derivative stores (Mongo / Redis / Elasticsearch) = evidence only.
- Scope isolation: Customer, Vendor (POS), Super Admin, and Service Marketplace runtimes use
  separate auth stores, cookies, query clients, route guards, terminal storage keys, and DTO
  boundaries. No actor type can be inferred or promoted into another (a customer cannot become
  a vendor; a vendor cannot become a super admin; tenant scope cannot leak across vendors).
- Cross-tab / BroadcastChannel / SignalR events are terminal logout/revalidation signals only
  and are strictly scope-bound; they cannot create or remove authority.

---

# Authority Boundaries

Source-of-truth ownership:
- PostgreSQL is the authoritative write model. Mongo / Redis / ES / WhatsApp / queue payloads
  are derivative.
- The SQL commit is the durability boundary. External providers own only their own side
  effects; they do not replace local SQL, ledger, or lifecycle authority.

Lifecycle authority (must go through the owner, not direct writes):
- `Order.PaymentStatus` → `IPaymentLifecycleService`.
- Order status → order lifecycle service.
- `FulfillmentStatus` → `IOrderFulfillmentService`.
- Direct writes to lifecycle fields violate the boundary unless replaced by a reviewed guard.

Recomposition authorities (use the shared helper, do not re-derive inline):
- `TenantLineageAuthority` — does a tenant-scoped row still belong to the tenant?
- `AdminTenantAuthority` — tenant-bound privileged actor eligibility.
- `SuperAdminPlatformAuthority` — global/platform actor eligibility.
- `ProductVariantLifecycleAuthority` — is a variant publicly/purchasably visible?
- `ProductVariantInventoryLineageAuthority` — can stock be reserved / consumed / propagated?
- `OrderFulfillmentLineageAuthority` — does a shipment still own its order?
- `OrderPaymentLineageAuthority` — may a payment facet mutate customer-visible status?
- `CommerceHubPriceAuthority` — the SOLE CommerceHub item-price resolver; currently
  `ProductVariant.EffectiveOnlinePrice`.
- `CommerceHubSearchSqlAuthorityOverlay` — recompose marketplace listing/public visibility
  from current SQL before serving or indexing.
- `AccountingLedgerAuthority` — may replayed evidence generate a journal?

Marketplace governance authority:
- Marketplace governance is DECISION authority, not visibility authority. Public marketplace
  visibility = current SQL `CommerceHubListings` PLUS current SQL product / variant / category /
  tenant / vendor-governance / pricing / stock / media / review authority.
- Mongo / Redis / ES / homepage JSON / search analytics / recommendations / popularity /
  replay payloads / recovery snapshots are derivative evidence only.

Currency authority:
- POS Admin and storefront currency remain tenant-scoped through `Tenant.Currency`.
- CommerceHub marketplace currency is PLATFORM authority and is EGP only through
  `FinancialCurrencyGuard.PlatformBaseCurrencyCode`. Non-EGP catalog price authority may NOT
  enter CommerceHub publication (publish, republish, listing update, bulk publish, replay,
  recovery, reindex all must reject / converge non-EGP listings to non-visible).

COD / debt authority:
- COD money ownership is split: merchant holds customer cash; platform owns a SQL receivable
  for commission debt.
- COD hard-limit suspension is HARD authority, not dashboard guidance:
  `amountOwed >= 1000 EGP` immediately suspends new CommerceHub order acceptance AND public
  vendor/listing visibility. Reactivation is automatic only when current SQL debt falls below
  the threshold via approved ledger-backed paths.

Replay / rebuild / recovery authority:
- These are convergence tooling. They may discover candidates from Mongo / Redis / ES /
  RabbitMQ / outboxes / provider evidence, but final mutation must revalidate current SQL
  authority, tenant scope, freshness, and idempotency / fence controls. Missing SQL authority
  must delete, tombstone, suppress, invalidate, no-op, or fail closed.

---

# Super Admin Rules

- Super-admin JWT role / auth-type / token-version claims are CANDIDATE EVIDENCE ONLY.
- Current SQL `SuperAdmins` existence, current `TokenVersion`, and current governance status
  are REQUIRED for every `RequireSuperAdmin` endpoint.
- `SuperAdminPlatformAuthorityMiddleware` guards every `RequireSuperAdmin` endpoint, including
  platform-policy controllers outside `/api/super-admin/*`.
- Governance-banned, suspended, or financially-locked super admins fail closed for platform
  operations AND for platform refresh rotation (the stored refresh authority is revoked).
- Deleted super-admin rows fail closed even if an unexpired access token remains.
- Tenant-admin tooling CANNOT silently escalate to global search index control
  (`GLOBAL_SEARCH_CONTROL_FORBIDDEN`).
- Global / all-tenant repair, global cache / search mutation, marketplace-wide moderation,
  tenant override, support impersonation, and emergency primitives are allowed ONLY under
  explicit platform authority or a documented runbook scope — never as a tenant-admin feature.
- Emergency / break-glass authority is OPERATIONAL authority, not business authority. It may
  stop / suspend / freeze / isolate / recover / repair / reindex / replay / shut down /
  diagnose, but it may NOT create visibility, publication, tenant, governance, financial,
  inventory, or SQL authority.
- Emergency rollback must recompose from SQL or route through lifecycle / reconciliation
  owners. It must NOT copy Mongo / Redis / ES / queue payloads / dashboards / exports /
  support-tool state back into truth.
- Emergency actions must be explicit, scoped, auditable, revocable, and runbook-backed. No
  silent operator bypass, stale-session elevation, or undocumented emergency mutation path.

---

# Session Rules

- The certified POS model is ONE current session per `User`. Login, registration issuance,
  refresh, and logout rotate or clear a single hash-only SQL refresh binding.
- New POS sessions store only the current hashed refresh binding (`Users.RefreshToken`); raw
  refresh credentials are never persisted.
- POS access tokens carry a `pos_session` binding claim (non-credential) to the current hashed
  refresh session. Legacy access tokens without `pos_session` fail closed and require re-sign-in.
- Logout must clear the current SQL session authority. If SQL revocation cannot be persisted,
  the endpoint returns `503 SESSION_REVOCATION_FAILED` and does NOT pretend cookie clearing
  completed containment.
- Refresh replay after a successful rotation is rejected (`REFRESH_REPLAY_REJECTED`); old
  access tokens and refresh credentials no longer match SQL authority.
- `Users.RefreshToken` is an EF concurrency token; concurrent refresh/login/logout rotations
  race-safe-reject the stale writer rather than overwriting the binding.
- `PrivilegedRefreshTokenRevocationInterceptor` clears refresh bindings on: POS role change,
  user disable/delete, tenant suspension/deactivation, vendor suspension/ban, and super-admin
  governance restriction. Refresh replay is audited (`REFRESH_TOKEN_REPLAY_DETECTED` /
  `TOKEN_REUSE_DETECTED`).
- CommerceHub customer sessions: logout revokes the current JTI and clears cookies;
  revoke-all, password reset, password change, and email change advance `TokensValidAfterUtc`.
- Super Admin sessions: `tokenVersion` is backend authority; malformed/corrupt tokenVersion
  storage fails closed before refresh.
- Abandoned-cart recovery callback is an APPROVED external machine boundary (tenant-specific
  callback key + idempotency). When the caller is an authenticated tenant admin, its
  `request.TenantId` must equal trusted tenant context or the request fails closed with `403`
  and `admin_tenant_scope_mismatch`. This is a named exception, NOT a pattern to extend.

---

# Claims Rules

- JWT role / tenant / auth-type / session / token-version claims are EVIDENCE ONLY.
- Do not issue POS access tokens without a current SQL session binding claim. Do not validate
  POS tokens using raw JWT claims alone; use `PosSessionAuthority`.
- Do not decode a JWT as authority on the frontend. Role / tenant / user values enter runtime
  only through backend `/auth/session` DTO evidence.
- `token_version` / `TokenVersion` claim must be bound to current SQL `SuperAdmins.TokenVersion`.
- `pos_session` binding claim is a non-credential session pointer.
- Customer access JWTs are HttpOnly-cookie-backed, CSRF-protected, JTI-revocable.
- For webhook ingress (see Webhook Security), `AllowAnonymousTenant` controllers derive tenant
  from the SIGNED payload plus stored SQL payment / shipment records — never from a raw header.

---

# Dangerous Operations

Dangerous operational primitives are low-level tools that can repair OR damage derivative state
faster than normal workflows. They are NOT business authority. They may be used only when a
reviewed authority boundary, runbook, or platform escalation explicitly allows them.

Primitive catalog (dangerous because they can bypass authority):
- Direct SQL / bulk mutation — can bypass lifecycle services, tenant co-ownership, outbox
  staging, version guards.
- Full Mongo document replacement — can overwrite SQL-recomposed fields with stale projection.
- Raw Elasticsearch visibility write — can publish stale or cross-tenant search visibility.
- Redis pattern invalidation / manual key edit — can amplify replay storms or hide stale
  assumptions.
- Raw MassTransit publish / send — can bypass aggregate ordering, tenant validation,
  transaction boundaries.
- Global / all-tenant repair — can turn a tenant incident into platform-wide mutation.
- Tenant override / support impersonation — can bypass trusted tenant and actor authority.
- Manual financial repair — can corrupt payment / refund / settlement / payout / ledger truth.

Approval checklist (all must be true before a dangerous primitive is used):
- The current SQL authority owner is named.
- Tenant scope is explicit and tenant-local unless platform/global scope is approved.
- Current SQL actor authority is checked for request-driven tenant operations.
- Replay collapse / idempotency / freshness / lease / ordering behavior remains intact.
- Fail-closed behavior is documented (delete / tombstone / no-op / retry / block /
  reconciliation-required).
- Operational convergence telemetry or equivalent diagnostics classify the result.
- Rollback is derivative-safe: invalidate / rebuild / reindex / delete / replay from SQL;
  never copy stale derivative state.

Forbidden shortcuts (never do these):
- Do not edit Mongo / Redis / ES to make a screen look correct when SQL authority disagrees.
- Do not use a provider callback payload to force local payment success after SQL reached a
  terminal incompatible state.
- Do not manually insert / update / delete ledger rows to clear accounting queues.
- Do not mark dead letters or outbox rows "processed" to hide a poison authority failure.
- Do not copy tenant-scoped documents / cache keys / search documents across tenants.
- Do not run global index deletion / recreation from tenant-admin tooling.
- Do not disable `ProjectionEventFence`, freshness filters, `SqlTruthRebuildGuard`,
  `TenantLineageAuthority`, `AdminTenantAuthority`, or `DerivativeProjectionMutationGuard`
  during incidents.
- Do not add a second price-approval path; marketplace governance may not approve merchant
  prices.

---

# Webhook Security

- Provider webhook ingress is TENANTLESS at the HTTP host layer when the callback hits the
  shared API hostname. Controllers that derive tenant authority from signed payloads plus
  stored SQL payment / shipment records MUST be marked `[AllowAnonymousTenant]` so the generic
  tenant middleware does not reject the callback before the webhook trust pipeline runs. This
  is the one sanctioned exception to tenant middleware stripping.
- Provider callbacks, redirects, settlement payloads, cached payment state, replayed webhook
  payloads, and stale provider reconciliation data are EXTERNAL EVIDENCE ONLY (delayed provider
  callback authority rule, CHECKOUT-CON-11).
- Before any payment / order / fulfillment / accounting / payout / commission mutation from a
  callback, recompute current SQL payment / order / fulfillment truth under locked or
  serializable SQL boundaries. Stale callbacks must NOT restore collected / settled / completed
  visibility after current SQL says failed / expired / cancelled / partially refunded /
  refunded / deleted.
- Unmatched external-money evidence must be persisted to SQL (e.g. `CommerceHubUnresolvedPaymentWebhook`)
  BEFORE the request is allowed to fail retryably. Distributed lock contention or temporary
  local processing failure is not a reason to rely only on provider redelivery.
- A provider-success webhook that cannot be matched to a paid local payment row must EITHER
  route every financially affected tenant to suspense accounting and place the order on hold,
  OR remain unresolved and globally fail closed for outward money readiness. When later matched,
  suspense clearance and hold release must commit in the SAME local authority transaction.
- Scope-less unresolved captured-payment or provider-refund rows are GLOBALLY BLOCKING external-
  money ambiguity. If SQL holds unresolved external-money truth with no durable tenant scope,
  CommerceHub / affiliate readiness gates fail closed until reconciliation materializes scope or
  clears the ambiguity.
- Webhook processing must be idempotent and replay-safe (duplicate delivery collapses to one
  outcome).
- Carrier / shipment webhooks: SQL shipment / order truth is authoritative over the carrier
  callback. `ShippingWebhookLogs` and processed-event records provide inbound idempotency.
  Unknown carrier/webhook statuses are converted to canonical unknown and fail closed; general
  Aramex webhooks are explicitly unsupported and must stay disabled.
- The webhook trust pipeline is the authority boundary. Generic tenant middleware is stripped
  before it runs; the signed payload + stored SQL record is what establishes scope.

---

# Multi-Tenant Rules

- Tenant isolation is NECESSARY-NOT-SUFFICIENT: a `TenantId` column alone is not enough. Scope
  requires trusted tenant resolution PLUS current SQL active tenant lineage.
- A cross-tenant leak is a CRITICAL defect, treated as a governance/security failure.
- Tenant-admin endpoints must consume `ITenantContext.RequiredTenantId`. Never parse
  `X-Tenant-Id` locally.
- Tenant-scoped derivative writes (Mongo / Search / Cache) must include tenant scope in the
  document id OR the mutation filter (`TenantScopedDerivativeKey` or equivalent). Product-only /
  listing-only / page-only / slug-only ids are insufficient without a tenant predicate plus SQL
  lineage proof.
- Tenant operations must NOT silently fall back to global behavior. Reviewed tenant search
  controls return `GLOBAL_SEARCH_CONTROL_FORBIDDEN` or `GLOBAL_SEARCH_INDEX_UNAVAILABLE` rather
  than performing global index mutation.
- Tenant reindex is explicitly tenant-scoped (trusted tenant context); global reindex is a
  separate super-admin route. Tenant-admin CommerceHub search recovery uses `ReindexTenantAsync`
  and must NOT call global alias promotion / catch-up (`GLOBAL_SEARCH_CONTROL_FORBIDDEN`).
- Hybrid marketplace surfaces may cross tenants for PUBLIC discovery, but writes and recovery
  mutation use SQL listing / product / vendor / category authority AND tenant-scoped document
  identity.
- Replay / rebuild / recovery treat event tenant ids, Mongo docs, Redis keys, ES ids, and repair
  inputs as candidate evidence; current SQL tenant lineage decides apply / delete / tombstone /
  no-op / fail-closed.
- Sensitive admin paths (banks, expense categories, financial accounts, shipping methods,
  categories, coupons, affiliate, audit logs, vendor payout methods, dashboard, financial
  reports) bind tenant scope explicitly in SQL with `(Id, TenantId)` and fail closed to `404`
  for foreign-tenant ids.
- Reporting / dashboard reads consume `ITenantContext.RequiredTenantId` and bind tenant in SQL;
  tenant-keyed cache keys are not enough.

---

# Fail Closed Rules

Silent allow is blocked. The system fails closed for:
- missing required facts,
- invalid authority,
- version mismatch,
- missing mandatory policy / rule,
- replay failure,
- audit failure,
- unknown capability / constraint,
- dependency cycles,
- unresolved conflicts,
- non-deterministic rules,
- side-effect attempts during evaluation.

Behavior when SQL authority is missing / stale / uncertain:
- Stale or missing SQL authority → delete / tombstone / no-op / retry / block / reconciliation-
  required instead of preserving stale derivative visibility.
- Replay / recovery / rebuild with missing SQL authority → converge by delete, tombstone,
  suppress, invalidate, no-op, manual review, or fail closed.
- External side effects that remain uncertain after the local authority commits → keep
  `Processing` / `ReconciliationRequired` / unknown-outcome; never force a false terminal state.
- Readiness gates fail closed when critical accounting / compensation / refund / payout /
  integration backlog is unresolved OR scope-less external-money ambiguity exists.
- Revocation wins IMMEDIATELY at the next authority check: current SQL super-admin authority,
  governance state, token version, tenant actor / session authority, and active tenant lineage
  decide whether authority still exists.
- Payment transition protection: suspense is NOT terminal `FAILED`; an unresolved captured
  payment blocks outward money; per-tenant provider gating where available methods = zero means
  UNAVAILABLE with NO COD fallback.
- Checkout: serializable re-lock transaction; inventory SQL-authoritative with active
  reservations; projection replay fails closed; stale/revoked JWT/claims fail closed.
- Reconciliation states are INTENTIONAL, not bugs. Normal read-model lag and fail-closed
  financial gating under uncertainty are accepted tradeoffs, not defects — unless an
  implementation violates the documented design boundary.

---

# Business Invariants

- Money truth = SQL ledger + append-only financial/accounting ledger. Mongo / Redis / ES /
  provider callbacks / payout-rail responses / dashboard counters = evidence only.
- Payment provider client results and frontend polling are NOT terminal payment authority.
  Backend canonical payment status, lifecycle services, webhook evidence, and reconciliation
  records decide terminal recognition or unresolved / pending state.
- Inward external-cash recognition (e.g. approved vendor repayment proof) is posted into SQL
  ledger truth with duplicate-evidence / serializable / financial-safety checks — it is NOT
  outward money movement and is not blocked behind the outward-transfer readiness gate.
- Outward money movement (refund / payout / gateway refund execution / completed payout
  recognition) runs the final readiness read inside the SAME serializable transaction / claim
  boundary that records the local authority mutation.
- One real-world transfer cannot safely fund duplicate local withdrawal recognition: affiliate
  payout transfer evidence is platform cash-rail proof; payout-allocation idempotency is global
  per payout reference.
- Inventory is SQL-authoritative with active reservations. Available quantity is recomposed
  through current stock-owner lineage, NOT by summing raw undeleted `ProductVariantStocks` rows.
- Settlement / gateway / COD recording rejects non-positive gross amounts and fees outside
  `[0, gross]` before persistence, accounting enqueue, or `Order.PaymentStatus` mutation.
- Settlement accounting recomposes current `PaymentSettlementOrders` lineage; `OrderCount` is
  recorded metadata only; soft-deleted batches are not posting authority.
- COD hard-limit: `amountOwed >= 1000 EGP` suspends new orders and public vendor visibility.
- CommerceHub currency is EGP-only; non-EGP catalog price cannot enter CommerceHub publication.
- CommerceHub price override is DISABLED (`OverridesEnabled = false`; `CommerceHubPrice` rejected
  / cleared / constrained to null; derivative override fields cleared). It must NOT be activated
  without the full PASS 13.6 control list.
- Marketplace governance precedence: current SQL governance wins when merchant intent conflicts
  with governance. Suppress suspended / banned vendors before public visibility on search /
  discovery / detail / homepage / recommendation / autocomplete.
- Order / sub-order lifecycle terminal states (cancelled, delivered, completed, refunded) are
  NOT resurrected by stale projections, replayed events, recovery candidates, or frontend state.
- Certified evidence tables are PostgreSQL-enforced append-only (triggers reject UPDATE / DELETE
  / TRUNCATE with `FORENSIC_APPEND_ONLY_VIOLATION`, including direct SQL that bypasses EF).
- Customer registration is transactional across customer + governance profile; concurrent
  duplicate email collapses to `EMAIL_EXISTS`.

---

# Never Assume

- Never assume a JWT / role / tenant / auth-type claim is authority.
- Never assume a cache, projection, or search document is truth.
- Never assume a frontend route guard, hidden button, or disabled control is authorization.
- Never assume a provider callback / webhook proves payment success.
- Never assume an `X-Tenant-Id` header defines tenant scope.
- Never assume a returned/displayed total equals the authoritative total — the server
  recomputes; client totals are request intent only.
- Never assume read-model lag is a bug — eventual consistency is by design; fail-closed
  financial gating is an accepted tradeoff.
- Never assume an ambient EF tenant filter or a primary-key `FindAsync(...)` lookup is the
  tenant-scope contract on sensitive admin paths.
- Never assume a deleted / inactive / detached / superseded / unpublished variant is still
  purchasable.
- Never assume a banned / suspended vendor is not publicly visible — suppression must be
  re-applied on every public surface.
- Never assume a dev seed / repair helper using a dangerous primitive is safe outside its
  explicit development / tooling boundary.
- Never assume a stale session or refresh remains valid after governance change, tenant
  suspension, role change, or user disable.
- Never assume emergency / break-glass creates business authority.
- Never assume support impersonation or tenant override are available features — they are
  PROHIBITED without a reviewed ceremony (SQL actor authority, target tenant lineage, reason /
  reference capture, revocation semantics, durable audit, replay-safe convergence, tests).
- Never assume a merchant price override is active — it is disabled until explicitly certified.

---

# QA Checklist

Tenant isolation:
- Cross-tenant read AND write are blocked on every admin / finance / shipping / category /
  affiliate / coupon / payout / dashboard path.
- `X-Tenant-Id` header is ignored for scope; `ITenantContext.RequiredTenantId` is the source.
- Tenant-scoped derivative ids / filters are present on public Mongo / Search / Cache writes.
- Cross-tenant replay is rejected by current SQL tenant lineage.
- Tenant-admin search recovery cannot invoke global alias promotion / catch-up.

Super admin:
- Stale token, stale refresh, governance ban, suspension, financial lock, and deletion are all
  rejected (fail closed).
- `SuperAdminPlatformAuthorityMiddleware` runs on every `RequireSuperAdmin` endpoint, including
  those outside `/api/super-admin/*`.
- Global primitives (all-tenant repair, global cache / search mutation) are unreachable from
  tenant-admin scope.

Authentication / session:
- One current POS session per user; refresh rotation clears the old binding.
- Logout clears current SQL session authority; revocation failure returns `503`, not cookie-only.
- Refresh bindings cleared on role change / disable / delete / tenant suspension / vendor ban /
  super-admin governance restriction.
- CommerceHub customer: login is the only session creator; registration and verification issue
  no token; unverified customers cannot log in.
- Customer logout / revoke-all / password reset / password change / email change invalidate
  sessions via `TokensValidAfterUtc`.

Authorization:
- Backend decides; frontend DevTools / role injection / hidden controls do not grant access.
- `AdminTenantAuthorityMiddleware` revalidates the actor against current SQL.
- Marketplace governance cannot approve/edit prices; audit != approval.

Claims:
- No raw JWT claim trust; `pos_session` binding present; `token_version` bound to SQL.
- Webhook `AllowAnonymousTenant` controllers derive tenant from signed payload + stored SQL.

Webhook / callback:
- Replay-safe idempotency; unmatched external-money persisted to SQL before retryable failure.
- Provider-success after terminal SQL failed / expired / refunded is NOT applied as success.
- Suspense routing / hold for unmatched provider-success webhooks.
- Scope-less unresolved external-money blocks outward-money readiness.
- Carrier unknown statuses canonicalized and fail closed.

Fail closed:
- Missing SQL authority → delete / tombstone / no-op, not stale visibility preservation.
- `Processing` / `ReconciliationRequired` preserved under uncertainty.
- Readiness gates fail closed on unresolved critical backlog.

Financial:
- Non-positive gross / fee out-of-range rejected at settlement recording.
- COD hard-limit 1000 EGP enforced at checkout and public visibility.
- CommerceHub currency EGP-only; non-EGP publish rejected.
- Price override disabled; `CommerceHubPrice` null.
- Ledger append-only; no manual ledger row edits.
- One transfer cannot fund duplicate withdrawals (global payout-reference idempotency).

Lifecycle & dangerous primitives:
- Payment / order / fulfillment transitions go through lifecycle services.
- Terminal lifecycle states not resurrected by replay / projection / recovery.
- Dangerous primitives used only under platform/runbook scope with telemetry and
  derivative-safe rollback.

---

# Common Failure Scenarios

(Each maps to an expected FAIL-CLOSED / converged outcome that QA should assert.)

1. Replay storm during tenant migration → collapses through SQL tenant lineage + tenant-scoped
   derivative keys; duplicate projection delivery collapses before side effects.
2. Stale super-admin access-token reuse after logout / password change / deletion / governance
   restriction → rejected against current SQL `SuperAdmins` + `TokenVersion`.
3. Governance-restricted super-admin refresh → fails closed, clears stored refresh authority,
   writes bounded audit.
4. Cached onboarding replay after owner / tenant / governance drift → fails closed; no fresh
   POS session minted.
5. Cross-tenant abandoned-cart recovery callback (authenticated admin submits foreign tenant)
   → `403 admin_tenant_scope_mismatch`; no persistence.
6. Tenant-admin CommerceHub search recovery invoking global alias promotion →
   `GLOBAL_SEARCH_CONTROL_FORBIDDEN`; global stays platform/runbook scope.
7. Provider late success after terminal SQL failed / expired / refunded → not applied as
   success; suspense / manual-review, not silent completion.
8. Unmatched provider-success webhook → suspense accounting + hold, or unresolved + global
   fail closed for outward money.
9. CommerceHub checkout double-submit / stale price / reservation expiry → request-hash idempotency
   + serializable order creation + reservation-before-commit; stale client totals rejected.
10. Variant supersession / warehouse reassignment resurrecting stock → current stock-owner
    lineage recomputed; superseded / detached / inactive-warehouse stock fails closed.
11. COD vendor debt crossing 1000 EGP mid-checkout → checkout rejects suspended vendor; public
    discovery suppresses the vendor.
12. Non-EGP catalog publish attempt → rejected at publication boundary; replay/recovery converge
    to non-visible.
13. Emergency rollback copying derivative state → prohibited; rollback must recompose from SQL.
14. Deleted / inactive tenant lineage on a POS session → session rejected; access token fails
    closed even if cryptographically unexpired.
15. Vendor ban not suppressing public listing → `CommerceHubSearchSqlAuthorityOverlay` and reindex
    suppress the vendor before public return.
16. Banned / suspended vendor still in search / autocomplete / suggestions → item-only
    post-filtering is NOT sufficient; vendor suppression is re-applied before totals/facets/
    suggestions are returned.
17. Frontend role / permission injection via DevTools → changes local presentation only; every
    API call still crosses backend authority.
18. Payment-cancellation outbox race / crash → claim lease + row-versioned collapse; lease clears
    on success / retry / failure; no manual processed-row marking.

---

# High Risk Areas

- Tenant isolation boundaries — cross-tenant leak is a CRITICAL defect.
- Super-admin platform authority + dangerous global primitives (all-tenant repair, global
  cache / search mutation, marketplace-wide moderation).
- Payment lifecycle + webhook / callback authority (external-money ambiguity, suspense routing).
- CommerceHub checkout + financial readiness gates (outward-money blocking, COD debt, EGP
  currency).
- Marketplace governance precedence + suspended / banned vendor suppression across every public
  surface.
- Replay / rebuild / recovery convergence under chaos (replay storms, rebuild overlap, dead-
  letter retry, stale actor sessions, tenant-lineage drift).
- Emergency / break-glass + support impersonation / tenant override (prohibited without ceremony).
- Append-only evidence integrity (forensic tables, ledger, identity lifecycle).
- Refresh / session revocation alignment with privileged authority (role change, disable, tenant
  suspension, vendor ban, governance restriction).
- Affiliate payout scope ambiguity (scope-less captured-payment compensation is globally blocking).
- CommerceHub price override activation (must remain disabled until fully certified).

---

# Regression Scenarios

These regression-freeze test families are the canonical anchors QA should run and extend when
touching the corresponding boundary. They are durable governance contracts, not throwaway tests.

- `TenantIsolationCertificationFreezeTests` — no `X-Tenant-Id` parsing, tenant-scoped derivative
  ids, cross-tenant suppression, explicit SQL tenant predicates on hardened admin paths.
- `CrossSystemAuthorityRegressionGuardTests` — dangerous-primitive governance (direct SQL /
  `ReplaceOneAsync` / raw ES / Redis pattern invalidation) with reviewed-pattern assertions.
- `SuperAdminPlatformAuthorityCertificationTests` — middleware on every `RequireSuperAdmin`
  endpoint, stale token / governance / deletion fail closed, no tenant-admin global escalation.
- `AdminAuthenticationSessionCertificationTests` — `PosSessionAuthority`, one current session,
  refresh rotation / replay, logout SQL revocation, `503` on revocation failure.
- `GlobalScopeTenantBoundaryCertificationTests` — platform vs tenant scope inventory, tenant
  global-search denial, tenant-scoped reindex / recovery / diagnostics.
- `CommerceHubAuthenticationAuthorityCertificationTests`,
  `CommerceHubEmailVerificationCertificationTests`,
  `CommerceHubSessionCreationCertificationTests` — registration/verification issue no token,
  login-only session creation, protected-route enforcement, append-only identity evidence.
- `CommerceHubVendorScopeIsolationAuthorityCertificationTests` — vendor cannot access another
  vendor's catalog / orders / finances / analytics / storefront.
- `CommerceHubSuperAdminSessionAuthorityCertificationTests` — frontend cannot fabricate super-
  admin authority; tokenVersion fail closed; cache / history / cross-tab cannot restore authority.
- `CommerceHubPrivilegedRefreshCertificationTests`,
  `CommerceHubRefreshReplayCertificationTests`,
  `CommerceHubGovernanceRefreshRevocationTests` — role-change / governance / tenant-suspension
  refresh revocation, replay detection + audit.
- Domain certification freezes for CommerceHub: cart, checkout, order/fulfillment lifecycle,
  settlement, COD risk, payout/reconciliation, refund/reversal, currency authority, publication
  currency policy, marketplace governance precedence, variant/inventory, category taxonomy,
  attribute/filter governance, price authority/override, customer identity, product discovery,
  emergency operations, forensic evidence, production readiness.
- `SuperAdminOperationalChaosCertificationTests`,
  `SemanticConvergenceChaosValidationTests` — replay storms, rebuild overlap, review/moderation
  replay, dangerous-primitive misuse, stale emergency-session reuse all converge or fail closed.

When a change touches a boundary, extend the matching freeze test in the SAME change rather than
adding an isolated new test that can drift from the contract.

---

# Conflicts & Honest Gaps

The authoritative sources (`.ai-context/ARCHITECTURE_TRUTH.md`, `CROSS_SYSTEM_AUTHORITY_HARDENING.md`,
`CODEX_RULES.md`, `KNOWN_RISKS.md`, and the `docs/architecture/*` certification findings) are
internally CONSISTENT. No direct architectural contradiction was found. The honest tensions a QA
engineer must keep in view:

1. Partial repo-wide dangerous-primitive governance. The focused architecture source guard is
   green for the reviewed raw-SQL / `ReplaceOneAsync` / raw-ES / Redis-pattern boundary, but some
   specialized projection paths still use equivalent LOCAL SQL-lineage checks rather than the
   shared helpers, and future raw primitives can be normalized into new files if engineers do not
   extend the allowlist + assertions in the same change. Treat "uses an equivalent local check"
   as acceptable only when documented and tested — otherwise it is a regression risk.

2. No universal parity / tenant-isolation scanner. There is no global SQL-vs-derivative parity
   scanner or generated route/scope inventory. Certified guarantees are deterministic convergence
   and diagnosability under pressure, NOT universal path coverage. Some legacy reporting /
   analytics / support / cache-warmer / export surfaces still require path-specific certification.

3. CommerceHub public-launch and customer-identity launch readiness are NOT certified. Residual
   launch blockers remain: refresh-token rotation + reuse detection, identity dashboards / alerts,
   external WORM/SIEM retention, SMTP/deployment proof, PostgreSQL-backed auth chaos harness,
   evidence-table role provisioning verification, backup/restore/rehearsal evidence. These are
   accepted launch debt, not authority-model defects — but QA must not treat compiled runtime code
   as launch-readiness proof.

4. CommerceHub price override is DESIGNED and FROZEN but NOT IMPLEMENTED/activated. It is disabled
   by multiple independent safety locks. Any apparent "activation" without the full PASS 13.6
   control list is a defect.

5. Webhook `AllowAnonymousTenant` is the single sanctioned exception to tenant middleware stripping.
   It is safe ONLY because the webhook trust pipeline establishes tenant scope from the signed
   payload + stored SQL records. A new anonymous-tenant endpoint without that pipeline is a
   critical scope defect.

---

# Source Documents

Authoritative (`.ai-context/`):
- `.ai-context/ARCHITECTURE_TRUTH.md` — source-of-truth ownership, authority model, lifecycle
  authority, fail-closed rules, super-admin / tenant-boundary / marketplace / CommerceHub
  certifications, webhook/callback authority, currency/COD authority.
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md` — cross-system authority hardening, payment
  policy evaluation pipeline, deterministic precedence, conflict resolution.
- `.ai-context/PROJECT_CONTEXT.md` — deployment shape, datastores, lifecycle authority owners,
  reconciliation behavior.
- `.ai-context/KNOWN_RISKS.md` — risk classifications (CONFIRMED / LIKELY / MITIGATED / ACCEPTED),
  residual launch debt, prevention rules.
- `.ai-context/CODEX_RULES.md` — mandatory operational rules (source-of-truth ownership, tenant
  isolation, dangerous primitives, replay/rebuild chaos, super-admin / tenant / auth freezes).

Certification findings (`docs/architecture/`):
- `super-admin-platform-authority-certification-findings.md`
- `global-scope-tenant-boundary-certification-findings.md`
- `tenant-isolation-certification-findings.md`
- `admin-authentication-session-certification-findings.md`
- `commercehub-authentication-authority-certification-findings.md`
- `commercehub-super-admin-session-authority-certification-findings.md`
- `commercehub-authority-scope-isolation-certification-findings.md`
- `commercehub-vendor-scope-isolation-authority-certification-findings.md`
- `commercehub-privileged-refresh-token-hardening-findings.md`
- `affiliate-configuration-authorization-client-trust-boundary-certification-findings.md`
- `dangerous-operational-primitives.md`
- `carrier-integration-authority-certification-findings.md`
- Marketplace / CommerceHub domain certifications: marketplace-governance,
  merchant-marketplace-governance-precedence, commercehub-publication-lifecycle,
  commercehub-currency-authority, commercehub-publication-currency-policy,
  commercehub-price-override-* (persistence / governance / controlled-activation / business-model-
  correction / resolver), commercehub-cart, commercehub-checkout,
  commercehub-order-fulfillment-lifecycle, commercehub-settlement-merchant-balance,
  commercehub-cod-risk-control, commercehub-payout-reconciliation, commercehub-refund-financial-
  reversal, commercehub-product-discovery, commercehub-emergency-operations,
  commercehub-audit-compliance-forensics, marketplace-production-readiness,
  emergency-governance-certification, and the PASS-series convergence / chaos freezes.

When in doubt, `.ai-context/ARCHITECTURE_TRUTH.md` and `CODEX_RULES.md` win over code comments and
over older narrative docs. If two certification findings appear to disagree, re-read the owning
`.ai-context` entry and escalate rather than assuming.

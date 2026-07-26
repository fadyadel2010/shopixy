# PASS F-M0-G CommerceHub Authority Scope Isolation Certification

Status: production-certifiable for inspected frontend runtime paths after domain query-client separation, actor-transition cache purging, storage-scope de-authoritization, exact cross-tab filtering, and CommerceHub vendor authority-loss revalidation.

## Customer/Vendor Isolation Findings

CommerceHub customer authority remains in the dedicated customer application and derives from current backend `/api/customers/auth/me`. POS vendor authority derives from current backend `/api/auth/session`. The runtimes use separate auth stores, cookies, query clients, route guards, terminal storage keys, and DTO boundaries. Neither actor type can be inferred or promoted from the other runtime.

## Vendor/Super Admin Isolation Findings

Vendor and Super Admin routes share a browser SPA shell but no longer share a React Query cache. Super Admin routes execute under `superAdminQueryClient`; POS/vendor routes use the POS query client. Their auth stores, API clients, cookies, route guards, capabilities, storage keys, and authority-loss handling remain distinct. A POS actor transition cannot clear, populate, or restore Super Admin cache authority, and Super Admin logout clears only the Super Admin query domain.

## Tenant Isolation Findings

POS storage scope now derives only from the current backend-hydrated in-memory actor. `localStorage.tenantId` cannot select a tenant cache or storage namespace. POS actor, tenant, or role replacement synchronously clears the POS React Query client and bounded POS runtime cache. Service Marketplace keys remain tenant/user scoped and foreign scopes are removed before presentation.

## CommerceHub/POS Isolation Findings

CommerceHub vendor governance or capability loss no longer converts every protected CommerceHub `403` into POS logout authority. The frontend purges CommerceHub vendor queries, withholds protected presentation, and re-enters `/api/auth/session`. The POS session is cleared only when current backend session authority rejects it. CommerceHub cannot grant POS capabilities, and CommerceHub denial cannot erase an otherwise valid POS actor.

## CommerceHub/Service Marketplace Isolation Findings

Service Marketplace now executes under `marketplaceQueryClient`, separate from POS/CommerceHub vendor and Super Admin caches. Marketplace actor scope remains current backend tenant plus user identity. Route teardown, actor replacement, BroadcastChannel invalidation, and SignalR callbacks are scoped to that identity and cannot write CommerceHub query state.

## Cache Findings

POS, Service Marketplace, Super Admin, and CommerceHub customer query caches are separate runtime domains. POS actor transitions clear POS queries and the custom runtime cache. Marketplace cache keys include tenant/user scope and remove foreign scopes. Super Admin authority loss clears only its dedicated client. Cache remains derivative and cannot create actor, tenant, governance, capability, or visibility truth.

## Realtime Findings

Service Marketplace SignalR and BroadcastChannel paths remain invalidate/refetch only and tenant/user/request scoped. POS cross-tab logout accepts only `pos_vendor_session_logout_at`; foreign runtime storage events and `localStorage.clear()` events do not create or remove POS authority. Customer and Super Admin terminal keys remain separate.

## Actor-Transition Findings

Customer replacement purges customer queries. POS login, logout, user replacement, tenant replacement, and role replacement trigger synchronous POS cache purge. Marketplace scope change removes the old tenant/user scope and fences stale workflows. Super Admin authority revisions fence stale bootstrap/refresh completions and clear the dedicated Super Admin cache.

## Failure Findings

Malformed DTOs, 401, 403, 404, refresh failure, bootstrap failure, and current-authority query failure fail closed. CommerceHub vendor 403 purges and revalidates vendor authority without inventing POS session loss. Actual backend POS session failure clears POS presentation and caches. No failure fallback restores a foreign actor or tenant.

## Adversarial Findings

Cache replay, storage replay, BroadcastChannel replay, SignalR replay, history replay, actor replay, tenant replay, domain replay, navigation replay, route replay, and capability replay were inspected. Distinct query clients, scoped keys, exact terminal events, current backend bootstrap, route guards, capability boundaries, and stale-completion fences prevent foreign authority from becoming current presentation.

## Runtime Hardening Performed

- Added dedicated React Query clients for POS, Service Marketplace, and Super Admin runtime domains.
- Wrapped Service Marketplace and Super Admin route trees in their domain query providers.
- Changed Super Admin authority loss to clear only the Super Admin query client.
- Added synchronous POS React Query and runtime-cache purge on user, tenant, role, login, and logout transitions.
- Changed CommerceHub vendor authority loss to purge vendor queries and revalidate backend POS session authority instead of directly logging out POS.
- Restricted POS cross-tab logout handling to the exact POS terminal key.
- Removed `localStorage.tenantId` as a fallback for POS storage/cache scope.
- Preserved existing customer application isolation and scoped Service Marketplace realtime invalidation.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/queryClient.js`
- `PointOfSale.Web/src/components/RuntimeQueryBoundaries.jsx`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`
- `PointOfSale.Web/src/super-admin/auth/superAdminAuthStore.ts`
- `PointOfSale.Web/src/utils/storageScope.js`

## Regression Freezes Added

- Updated `PointOfSale.Web/src/components/AuthBootstrap.test.tsx`.
- Added `PointOfSale.Web/src/queryClient.test.ts`.
- Updated `CommerceHubFrontendAuthorityCertificationTests`.

## Architecture Updates

Updated all five `.ai-context` authority files with F-M0-G ownership, runtime hardening, risk closure, and future-change freeze rules. Added this certification findings document.

## Explicit Answers

- Who owns scope isolation authority? Backend identity, session, actor, tenant, governance, capability, and visibility authorities own scope truth. Frontend scope isolation owns derivative boundary presentation and purge/revalidation behavior only.
- Can customer authority become vendor authority? No.
- Can vendor authority become Super Admin authority? No.
- Can tenant authority leak? No on inspected runtime paths.
- Can CommerceHub authority leak into Service Marketplace? No on inspected runtime paths.
- Can cache cross scopes? No accepted authority cache crosses the separated runtime clients or tenant/user marketplace keys.
- Can realtime cross scopes? No accepted realtime path can cross current tenant/user/request scope or create authority.
- Can actor transitions preserve authority? No; inspected transitions purge or replace prior scoped presentation and fence stale completions.
- Were runtime changes required? Yes.
- Is CommerceHub Authority Scope Isolation production-certifiable? Yes, for inspected frontend runtime paths.

## Remaining Operational Evidence

Retain deployed-browser chaos coverage for simultaneous POS and Super Admin sessions, customer/vendor multi-tab logout, tenant replacement, delayed storage events, BFCache restoration, offline/reconnect, SignalR reconnect during actor change, and vendor governance loss while CommerceHub and POS screens are open.

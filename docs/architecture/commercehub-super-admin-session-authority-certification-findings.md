# PASS F-M0-C CommerceHub Super Admin Session Authority Certification

CommerceHub Super Admin Session Authority is production-certifiable for inspected CommerceHub administration frontend runtime paths after runtime hardening.

## Admin Identity Findings
- Current Super Admin identity presentation now requires a valid backend `/api/super-admin/auth/session` DTO with Super Admin id, email, exact `SuperAdmin` role, `Bearer` token type, future expirations, and tokenVersion. Cached identity cannot become authority.

## Login Findings
- Login success no longer establishes authenticated UI from the login response. The login flow re-enters backend `/auth/session` before setting session state or navigating to protected administration screens.

## Refresh Findings
- Refresh success is not session authority. Refresh is followed by current backend `/auth/session` hydration before UI presentation is restored.

## TokenVersion Findings
- tokenVersion is backend authority. Malformed, corrupt, or unreadable tokenVersion storage fails closed before refresh instead of refreshing without version evidence.

## Logout Findings
- Logout clears session presentation, tokenVersion/expiration convenience storage, Super Admin caches, and broadcasts terminal cross-tab authority loss.

## Governance Findings
- Governance, finance, operations, payments, payouts, refunds, promo, CMS, homepage, users, and vendor-management screens sit behind Super Admin protected routes and protected API client 401/403 authority-loss handling.

## Cache Findings
- React Query Super Admin cache scopes are purged on authority loss for `super-admin`, payout queue, vendor payment methods, vendor payment tenants, and vendor payments keys. Cache cannot restore session authority.

## Cross-Tab Findings
- Cross-tab storage handling is terminal only through Super Admin logout/authority-loss signals. Cross-tab events cannot create, repair, refresh, or extend authority.

## History Findings
- Deep links, bookmarks, and back/forward navigation re-enter protected route bootstrap and current backend `/auth/session`; history cannot restore authenticated presentation.

## Failure Findings
- Session DTO failures, expired timestamps, protected 401/403 responses, refresh failures, and tokenVersion storage corruption fail closed by clearing presentation and redirecting to login.

## Actor-Transition Findings
- Actor replacement cannot preserve prior cached identity because the auth store is no longer persisted and authority loss purges Super Admin query caches before replacement presentation.

## Adversarial Findings
- JWT replay, refresh replay, TokenVersion replay, cache replay, storage replay, history replay, cross-tab replay, identity replay, governance replay, actor replacement, and malformed DTO injection cannot create frontend Super Admin authority on inspected paths.

## Runtime Hardening Performed
- Added Super Admin session DTO authority validation.
- Removed persisted Super Admin auth-store authority.
- Required login and refresh to re-enter backend `/auth/session` before session presentation.
- Made corrupt/unreadable tokenVersion refresh fail closed.
- Added terminal cross-tab logout broadcast and Super Admin query-cache purge.
- Added focus, visibility, interval, and online revalidation for protected routes.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/super-admin/auth/superAdminAuth.types.ts`
- `PointOfSale.Web/src/super-admin/auth/superAdminAuthStore.ts`
- `PointOfSale.Web/src/super-admin/auth/useSuperAdminSessionBootstrap.ts`
- `PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.tsx`
- `PointOfSale.Web/src/super-admin/auth/SuperAdminLoginPage.tsx`
- `PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/super-admin/auth/superAdminSession.test.ts`
- `PointOfSale.Tests/Architecture/CommerceHubFrontendAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns Super Admin session authority? Backend platform authentication/session, Super Admin identity, TokenVersion, and governance authorities.
- Can frontend fabricate authentication? No.
- Can refresh resurrect revoked admins? No; refresh must be accepted by backend and then `/auth/session` must accept current authority.
- Can stale TokenVersion survive? No; malformed or corrupt tokenVersion evidence fails closed and backend tokenVersion remains authoritative.
- Can governance survive authority loss? No; protected 401/403 and session failure purge session and Super Admin caches.
- Can cache restore authority? No.
- Can history restore authority? No.
- Can actor leakage occur? No inspected runtime path preserves prior actor authority after authority loss or replacement.
- Were runtime changes required? Yes.
- Is CommerceHub Super Admin Session Authority production-certifiable? Yes, for inspected frontend runtime paths, with browser chaos evidence retained as operational follow-up.

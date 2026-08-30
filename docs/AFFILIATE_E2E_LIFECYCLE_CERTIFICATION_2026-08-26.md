# Affiliate E2E Lifecycle Certification — 2026-08-26

## Verdict

**Source and automated certification: CONDITIONAL PASS for the reviewed Affiliate lifecycle boundaries.** The focused identity, verification, frontend, PostgreSQL, and build gates pass. The broad Affiliate backend gate still has 13 failures in unrelated dirty-worktree architecture/documentation, archived-migration, deprecated product-variant endpoint, tenant-fixture, and endpoint-count tests.

**Live end-to-end certification: NOT RUN / NOT CERTIFIED.** The user explicitly requested that browser testing be skipped. In addition, the API could not start because the configured PostgreSQL, MongoDB, Elasticsearch, Redis, and RabbitMQ hosts were unreachable from this development machine. No disposable Affiliate account, order, or commission row was created.

The blocking `/affiliate/login` refresh loop was traced to its authoritative failure boundary, fixed, independently reviewed, extended with additional Codex fixes, and covered by automated tests.

## Required delegation and independent review

- Kilo Delegate was used with the exact `kilo/stealth/ox-alpha` implementer.
- The successful relay evidence is stored at `C:\Users\fadya\AppData\Local\Temp\delegate-relay\PointOfSale-2026-08-26T11-21-07-327Z\result.json`.
- ox-alpha implemented the initial passive-restore versus protected-request auth-failure policy and regression tests.
- Codex inspected the actual diff and then corrected additional defects: replayed protected 401 settlement, missing identity routes/pages, missing development email-link base URL, store-coupled global login, cross-tab false logout, duplicate restore requests, restore error settlement, cached-localStorage auth decisions, safe return URLs, SPA navigation binding, and a category pagination race.

## Live topology evidence

Initial read-only probes found all expected application ports down. The frontend development servers were then started for handoff.

| Surface | Expected URL | Final probe | Status |
|---|---|---:|---|
| POS Admin / Affiliate Hub | `http://localhost:5173` | HTTP 200 | Running |
| Backend | `http://localhost:5010` | Connection refused | Blocked |
| Storefront | `http://localhost:5174` | HTTP 200 | Running |
| CommerceHub | `http://localhost:5175` | HTTP 200 | Running |
| PostgreSQL | configured remote host | TCP unavailable | Blocked |
| MongoDB | configured remote host | TCP unavailable | Blocked |
| Elasticsearch | configured remote host | TCP unavailable | Blocked |
| Redis | configured remote host | TCP unavailable | Blocked |
| RabbitMQ / consumers | configured remote host | TCP unavailable | Blocked |

Docker CLI was not available as a local fallback. No secrets or connection credentials are reproduced in this report.

## Authentication state machine

The browser does not own token authority. Access and refresh tokens are transported in `HttpOnly`, `Secure`, `SameSite=Strict` cookies. JavaScript stores only non-sensitive profile presentation fields. Every restoration reads `GET /api/affiliate/profile`; the dedicated `AffiliateJwt` handler then validates the signed access token and current SQL account, user, email-verification, activation, session-binding, and token-cutoff state.

```text
UNKNOWN
  |
  v
RESTORING -- profile 200 / refreshed replay 200 --> AUTHENTICATED
  |
  +-- terminal 401/403 -------------------------> UNAUTHENTICATED
  |
  `-- transport/5xx/timeout --------------------> ERROR -- retry --> RESTORING
```

Rules now enforced:

- `restoreSession()` is single-flight, so concurrent initializers share one semantic profile request.
- A profile 401 may perform one refresh and one replay.
- A failed refresh or replayed 401 reaches a terminal state; it cannot recursively refresh.
- Passive restore callers own navigation. Login and registration settle unauthenticated without navigating, which removes the loop.
- Protected API failures clear presentation state, publish one cross-tab session event, and navigate once.
- The Affiliate route wrapper binds terminal navigation to React Router, avoiding a hard reload during normal SPA operation.
- Cross-tab events trigger backend revalidation. Individual profile-cache writes are not interpreted as authentication state.
- A network/5xx restore failure produces an explicit retryable `ERROR`; it is not treated as logout.
- Return URLs are restricted to the local `/affiliate/portal` route and its query/fragment. External, protocol-relative, registration, and other paths fall back to `/affiliate/portal`.
- Visiting `/affiliate/login` while authenticated restores once and redirects once to the validated return URL or the portal.
- Logout revokes the refresh session best-effort, clears the cookies server-side when reachable, clears presentation metadata, and returns to login.

### Root cause of the P1 loop

Before the fix, both public login and registration pages invoked `restoreSession()` on mount. For an unauthenticated visitor the sequence was:

```text
/affiliate/login mounts
  -> GET /api/affiliate/profile = 401
  -> POST /api/affiliate/auth/refresh = 401
  -> global response interceptor sets window.location.href=/affiliate/login
  -> document reloads
  -> the same sequence repeats forever
```

The login page was not wrapped by an authenticated route guard. The cycle was caused by a global interceptor treating a caller-owned passive probe as a protected-resource terminal logout. The fix is a per-request failure policy at that interceptor boundary, not a retry guard or auth bypass.

## Authentication API contract

The identity controller is class-level `AllowAnonymous` and each operation is rate-limited. Protected Affiliate APIs use the dedicated `AffiliateOnly` policy; unrelated POS Admin or Super Admin sessions cannot satisfy it.

| Endpoint | Access | Input | Output | Authoritative side effects | Principal errors |
|---|---|---|---|---|---|
| `POST /api/affiliate/auth/register` | Public | email, strong password, display name | unverified Affiliate profile | Creates one global Affiliate account/user and sends verification email; does not establish a session | 400 validation, 409 duplicate |
| `POST /api/affiliate/auth/login` | Public | email, password | Affiliate profile | Creates/rotates refresh authority and sets access/refresh cookies | 400 malformed, 401 invalid credentials/unverified/suspended |
| `POST /api/affiliate/auth/refresh` | Public cookie endpoint | optional refresh DTO or refresh cookie | Affiliate profile | Atomically rotates refresh token/session and cookies | 400 missing, 401 invalid/expired/replayed/ineligible account |
| `POST /api/affiliate/auth/logout` | Public cookie endpoint | refresh cookie when present | success | Revokes presented refresh authority and clears Affiliate cookies | best-effort idempotent response |
| `POST /api/affiliate/auth/verify-email` | Public | verification token | success | Consumes token and marks email verified | 400 missing, 401 invalid/expired |
| `POST /api/affiliate/auth/resend-verification` | Public | email | generic success | Sends only when eligible; enumeration-safe response | 400 malformed |
| `POST /api/affiliate/auth/forgot-password` | Public | email | generic success | Issues reset mail only when eligible; enumeration-safe response | 400 malformed |
| `POST /api/affiliate/auth/reset-password` | Public | token, strong new password | success | Consumes token, changes password, revokes existing sessions | 400 missing/weak, 401 invalid/expired |
| `GET /api/affiliate/profile` | Affiliate-only | access cookie/Bearer fallback | current Affiliate profile | No-cache SQL-authority read | 401 unauthenticated/stale, 403 wrong role |

There is no separate Affiliate `session` endpoint. `GET /api/affiliate/profile` is the current-user/session restoration contract.

Other role boundaries reviewed:

- Affiliate dashboard, member product discovery, orders, earnings, profile, payouts, and promotion links require `AffiliateOnly`.
- Anonymous category discovery and explicitly public-safe product list/detail endpoints use `AllowAnonymous` and omit member-only commission data.
- POS Admin and Super Admin governance endpoints retain their separate policies; no public identity operation requires those stronger roles.

## Registration, login, and invalid-state behavior

Source and automated tests establish the following contracts; they were not exercised against the unavailable live backend:

- Registration does not cache its response as authenticated authority.
- Duplicate email is globally checked and returns a conflict without creating a second account.
- Verification and password-reset links now have actual frontend routes and pages.
- Development email links use `http://localhost:5173` through `AffiliateIdentity:PublicBaseUrl`.
- Successful login performs one semantic login request, establishes cookie authority, caches only presentation metadata, publishes a cross-tab event, and redirects once.
- Invalid credentials share a non-enumerating localized message; malformed input, unverified accounts, and suspended accounts have explicit localized states.
- Repeated submit clicks are single-flight at the page boundary.
- Login is global Affiliate identity and no longer depends on finding or selecting a storefront.

### Email-verification forensic finding

The verification failure was not caused by query-string decoding or token entropy. Anonymous `/api/affiliate/auth/*` requests have no tenant context. The verification query included the required `AffiliateAccount` navigation, whose global tenant filter rejected the account when `CurrentTenantId` was null; EF Core therefore removed the otherwise valid token row from the result.

The production correction and its enforced contracts are:

- Verification bypasses global filters for this global identity lookup, then explicitly rejects deleted tokens, users, and accounts.
- New links use 32 cryptographically random bytes represented as 64 uppercase hexadecimal characters. Only the SHA-256 hash is persisted.
- Previously delivered 64-byte padded Base64 links remain accepted exactly as presented; malformed variants are rejected before lookup.
- Expiry is UTC and exclusive: `ExpiresAtUtc > nowUtc`. PostgreSQL persists the values as `timestamp with time zone`.
- Token claim uses a conditional SQL update inside the same transaction as user verification, account activation, and the audit record.
- Verification and resend use the same user-then-token lock order. Concurrent resends serialize before replacing outstanding links, and concurrent verification claims have one winner.
- Registration commits account, user, and verification-token persistence in one transaction before attempting email delivery.
- Repeated use of an already-consumed token succeeds only when that token's linked user is already verified.
- Development logging records email metadata only and does not emit credential-bearing message bodies or raw links.
- The frontend reads `token` once through `useSearchParams`, forwards the decoded value without a second decode, and emits one semantic request under React StrictMode.

## Product discovery through commission authority

The reviewed lifecycle is deliberately split into authoritative and derived state:

1. Anonymous public discovery uses SQL-admitted public DTOs and does not expose commission fields.
2. Member discovery and referral-link generation require `AffiliateOnly`.
3. Public “promote” actions enter the portal; the portal restores backend authority. A cached account ID is never used as the auth decision.
4. Storefront referral middleware validates the referral code, tenant, link, product/category policy, price, and inventory, then writes protected attribution evidence. Frontend/session storage is only a tenant-scoped hint.
5. Cart lines preserve their own Affiliate admission so mixed carts do not smear attribution across unrelated lines.
6. Checkout revalidates admission against final SQL authority and snapshots the eligible order line, commission policy/rate, and commission amount.
7. Order/payment lifecycle consumers create idempotent earned commission ledger entries; later lifecycle transitions approve, pay, or reverse entries.
8. PostgreSQL ledger rows are monetary authority. MongoDB and Elasticsearch documents are projections and cannot mint commission.

## Production defects fixed in this phase

1. Passive session restore could reload `/affiliate/login` forever.
2. A protected request still returning 401 after refresh replay did not perform terminal cleanup/navigation.
3. Concurrent restore callers could duplicate current-user traffic.
4. Network/5xx restore failures were conflated with unauthenticated state.
5. Profile-cache key churn could falsely log out other tabs.
6. Product/category promotion decisions trusted a localStorage account ID.
7. Terminal navigation was not bound to React Router in production.
8. Return URLs were absent; the new contract is local and allowlisted.
9. Affiliate login incorrectly depended on storefront discovery despite global identity.
10. Verification, forgot-password, and reset-password backend links had no matching frontend routes.
11. Development identity emails had no Affiliate public base URL.
12. Category pagination could be reset to page 1 by a delayed mount-time search effect.
13. Valid Affiliate verification tokens were hidden by a required-navigation tenant filter on anonymous global identity requests.
14. Verification token transport used padded Base64 characters that were unnecessarily fragile in URLs and email HTML.
15. Verification consumption was not atomically arbitrated across concurrent requests.
16. Verification bypass of global filters needed explicit soft-delete authority checks.
17. Resend did not enforce a transactional last-token-wins replacement policy.
18. Registration could persist the account and user separately from verification-token issuance.
19. Development email logging exposed credential-bearing link bodies.

## Verification evidence

| Check | Result |
|---|---|
| Affiliate auth service tests | 15/15 passed (included in focused matrix) |
| Focused Affiliate verification lifecycle and identity certification | **31/31 passed** |
| Real PostgreSQL verification persistence and concurrent claim test (`REQUIRE_REAL_POSTGRES_TESTS=1`) | **1/1 passed** |
| Affiliate Web module tests | **35/35 passed** |
| Affiliate architecture tests | **38/38 passed** |
| Focused Affiliate Web lifecycle matrix | **51/51 passed**, 9 files |
| Focused backend identity/session/checkout/order/commission matrix | **97/97 passed** |
| Storefront full test suite | **344/344 passed**, 34 files |
| POS Web production build | PASS |
| Storefront production build | PASS |
| CommerceHub production build | PASS |
| .NET solution build | PASS, 0 warnings, 0 errors on final incremental gate |
| `git diff --check` | PASS; line-ending notices only |
| Broad backend Affiliate gate | **855/868 passed; 13 unrelated dirty-worktree failures** |

The broad POS Web suite was also run and is not globally green: 20 files / 54 tests failed while 121 files / 1,105 tests passed. The failures are outside this Affiliate lifecycle patch and include existing i18n, payment-copy, checkout-mock, and full-parallel timeout debt. Every Affiliate-focused failure was rerun with a suitable per-test timeout and passed. The latest broad backend Affiliate run executed 868 tests: 855 passed and 13 failed in unrelated dirty-worktree architecture/documentation, archived-migration, deprecated product-variant endpoint, tenant-fixture, and endpoint-count tests. The executable identity and verification matrix is green.

## Manual live re-certification checklist

This is the remaining work the user elected to perform after infrastructure is reachable:

1. Start the API and all dependency consumers; confirm health, queue consumption, and no connection retry storm.
2. Open `/affiliate/login` in a clean profile. Capture one profile probe, at most one refresh attempt, a terminal unauthenticated UI, and no document reload loop.
3. Register a uniquely named disposable Development Affiliate. Confirm one account/user, safe duplicate behavior, verification mail, verification route, and no session before verification.
4. Exercise malformed email/password, wrong password, unverified, suspended, and any supported expired state. Confirm no cookies, redirects, retries, or raw exceptions.
5. Log in. Confirm one login request, cookies, one redirect, one profile restore, and authentication surviving refresh.
6. Open `/affiliate/login` while authenticated. Confirm one redirect to the portal, with no second login or refresh storm.
7. Expire only the access token. Refresh the portal and confirm one refresh rotation plus one profile replay.
8. In a second tab, log in and out. Confirm the first tab revalidates against the backend instead of trusting profile-cache writes.
9. Discover a product, open detail, promote it, generate one link, and open that link on Storefront. Record the referral evidence cookie and tenant-scoped hint.
10. Add eligible and non-eligible items to a mixed cart, checkout, and inspect the authoritative order-line attribution snapshot.
11. Drive the order/payment transition required for earned commission. Confirm exactly one SQL ledger entry and eventual Mongo/Elasticsearch projections; then exercise reversal if supported by the fixture.

Until those steps pass with the backend and consumers available, and the unrelated broad-gate failures are resolved by their owning work, the honest overall status remains **CONDITIONAL PASS: automated/source verification certified, live E2E pending**.

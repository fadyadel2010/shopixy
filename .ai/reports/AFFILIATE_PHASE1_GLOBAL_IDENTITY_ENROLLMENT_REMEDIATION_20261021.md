# Affiliate Phase 1 — Global identity, enrollment and authentication

Date: 2026-10-21. Workspace: `C:\Users\fadya\source\repos\PointOfSale` (primary Windows workspace, branch `main`).

## A. Executive verdict — CONDITIONAL

**Confirmed lifecycle defects were fixed and verified through real headed Edge UI tests against the configured development database.** Global enrollment, email verification/activation, identity/profile readback, login/logout, cookie refresh, recovery, and replay rejection work in the exercised scenarios. Exactly one profile and one owned profile referral code persisted per synthetic enrollee.

**The original AhramStore empty list is legitimate, not a missing global affiliate.** Its existing POS Owner/Admin surface is explicitly a merchant earnings/settlement view. It is not the global enrollment registry. The UI incorrectly claimed affiliates appear upon enrollment; that message is corrected without exposing a global directory to merchant users.

Conditional rather than unrestricted PASS: no existing authorized platform-wide affiliate enrollment management UI/API was found. A global registry/detail and platform lifecycle-management surface would need a separately authorized platform implementation, not removal of the merchant relationship check. Browser tests did not provision separate Cashier/Manager or platform-admin identities; existing authorization contracts/tests were exercised instead. Limitations are itemized below.

Evidence directory: `.ai/evidence/affiliate-phase1-20261021/`.

## B. Global domain model and read-only baseline

### Authority and identity

- PostgreSQL configured `DefaultConnection`: database `PosEcommerceDb_Baseline`, server `192.168.1.5`. Used the application's configured data source, not a recreated database. SQL evidence connections explicitly use read-only transactions, except the tightly guarded task-owned negative-state fixtures described below.
- `AffiliateUsers` is independent credential identity, **not** `Users` (POS identity) or `OnlineCustomers` (storefront customer identity).
- `AffiliateUsers.AffiliateAccountId` references `AffiliateAccounts.Id`; a unique index enforces one credential identity per profile. `AffiliateAccounts.UserId` agrees with that affiliate user for all persisted accounts observed.
- New enrollment writes `Guid.Empty` to the legacy `TenantId` fields. Both pre-existing identities also have this legacy empty scope. This is not AhramStore ownership or a requirement to enroll per merchant.
- `AffiliateAuthController.Register` creates profile, credential identity, and verification token in one transaction. Registration creates no session. Duplicate checks are global and normalized; concurrent unique violations are translated to `409` rather than orphaning another profile.
- `UQ_AffiliateAccounts_ReferralCode_Global` enforces global profile-code uniqueness. Codes are immutable after creation. New codes are uppercase ASCII-alphanumeric, 6–12 characters, with collision checking and a database uniqueness backstop.
- The persisted email index remains legacy `(TenantId, Email)`. Global lookup and the fixed empty scope protect the supported public enrollment path; this phase did not add a migration or claim a new standalone database-wide email constraint.
- Existing `CanonicalIdentityLinks` cross-system bridge contracts were preserved, including the legacy POS-user bridge contract. The actual affiliate login authority remains `AffiliateUser` + `AffiliateAccount`; no synthetic POS user was created.

### Actual states and tokens

- Domain enum: `Pending`, `Active`, `Suspended`, `Closed`. There is no separate `Rejected` enum or independent approval flag in this implementation.
- Registration: `Pending`, `EmailVerified=false`. Verification: verifies the owning credential and automatically activates **only Pending** profiles. No manual approval is required for this normal public flow.
- Login, refresh and `AffiliateJwt` admission require a verified, non-deleted credential and active, non-deleted profile. Pending/suspended/closed accounts are denied. Closed is terminal through the domain API.
- Verification tokens are purpose-specific, SHA-256 hash-only, normally valid 24 hours. Resend replaces outstanding verification links. An already-consumed successful verification link intentionally returns idempotent `200`, but creates no session and no second verification audit.
- Recovery tokens are hash-only, normally valid one hour. Reset now serializes on the identity, conditionally consumes the token, invalidates outstanding siblings, revokes sessions, and commits the audit atomically. Issuance uses the same lock order and invalidates earlier outstanding recovery links.
- Dedicated `AffiliateJwt` scheme/key/issuer/audience; claims identify both affiliate user and account, with session binding and no merchant claim. HttpOnly cookies transport access/refresh tokens. SQL current-session authority is checked on protected requests; logout clears it. Refresh rotation/replay ledger and session cutoff remain authoritative.

### Baseline identities and relationships

| Identity | Affiliate user ID | Global profile ID | Profile code | Baseline state |
|---|---|---|---|---|
| `fadyadel2060@gmail.com` / Memoxp | `01a03e11-e2b3-7875-9b59-6e25f59f03d1` | `01a03e11-e234-7d40-b77e-99f9463c7a40` | `MEMOXP155504` | Verified, Active |
| Existing C57 QA affiliate | `01a05792-05e8-785f-923e-4e3f2c3c0d6b` | `01a05791-fc67-77da-8bd7-2fe2b28ea71b` | `C57AFF618486` | Verified, Active |

Baseline: **2 profiles, 2 affiliate users, 2 promotion links, 10 commission entries, 0 payout requests.** `baseline.sql/json` records the narrow identities, relationships and relevant indexes before source edits.

Initial browser session had no authenticated affiliate (`/api/affiliate/profile` → `401`). Supplied affiliate credentials were used through the actual login form to establish the Memoxp session. Existing POS session was `ahramco.software@gmail.com`, verified Owner, tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d` (AhramStore). The existing storefront session was not repurposed or changed.

### Commerce boundary — discovered, not invented

- `AffiliatePromotionLink` references `AffiliateUserId`, an `AffiliateAccountId` bridge, product and merchant `TenantId`. Its per-product promotion referral code is **distinct from the profile referral code**.
- Memoxp promotion `01a03e6b-3ff1-7c1b-9cf2-a340244c0f25` belongs to its correct user/profile, product `580b30bd-1829-131c-2cc4-5f6cf3994e09`, merchant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` (Shopixy). Its link code is `DMFOQ21IP3`. The product owner matches that merchant.
- Existing commission records for both baseline affiliates are for **Shopixy**, not AhramStore. `StoreTenantId` is the merchant authority on commission records; order and attribution relationships preserve merchant/order IDs. No attribution or financial algorithm was changed.
- MongoDB supplies affiliate dashboard/order/discovery projections; Elasticsearch supports discovery. Neither owns credential identity or determines canonical profile creation.
- Promotion endpoints scope access to the authenticated affiliate. Catalog selection does not grant access to private POS orders, customers, payment proofs or finance.

## C. Admin / Portal consistency and fixes

### Why “No Affiliates Yet” appeared

`GET /api/admin/affiliates` → `AdminAffiliateController.ListAffiliates`:

1. Requires POS `Owner,Admin` and the canonical `affiliates.manage` permission boundary.
2. Uses trusted `ITenantContext`, plus merchant affiliate-readiness prerequisites.
3. Selects global profile IDs from SQL `AffiliateCommissionLedger` with `StoreTenantId == currentTenant` and `EntryType == Earned`.
4. Loads those global profiles and **current-store-only** earnings totals. Detail requires an actual merchant-ledger relationship and otherwise returns `404`.

This is an explicit source/domain contract (AF-20), not an accidental profile `TenantId` filter or wrong identity database. AhramStore's actual API returned `200` with `affiliates:[]`; Memoxp detail returned `404`. SQL proves its relationship is to Shopixy. Enrollment or browsing another store cannot legitimately populate AhramStore's earnings list. No financial transactions were created to force visibility.

Portal `/api/affiliate/profile` returned the exact Memoxp ID/code above; `/api/affiliate/promotion-links` agreed with SQL. After the fix it also returns the owning affiliate user ID, credential email and verification state. Synthetic profile readbacks agree across registration, verified login, refresh, recovery and merchant-context changes.

### Corrected files and rationale

| File | Change |
|---|---|
| `PointOfSale.API/Controllers/AffiliateAuthController.cs` | Server-side email/name format and persistence-length checks; trimmed name and safe code seed; controlled committed-registration/delivery-unavailable `503` and resend handling; private recovery failure response; global/soft-delete-safe reset lookup, atomic conditional consumption and sibling invalidation; nullable optional legacy refresh-body token. |
| `PointOfSale.API/Controllers/AffiliatePortalController.cs` | Profile includes authoritative owning user ID, email and verification state, preventing profile restoration from erasing cached email. |
| `PointOfSale.API/Controllers/AdminAffiliateController.cs` | DEBUG Mongo diagnostic now scopes dashboard/order/earnings projections by trusted merchant, closing a cross-merchant diagnostic read path. Main merchant list/detail/financial scopes remain intact. |
| `PointOfSale.Core/Entities/Affiliate/AffiliateAccount.cs`, `AffiliateUser.cs` | Correct stale tenant/POS-ownership documentation; no schema or domain-state changes. |
| `PointOfSale.Web/src/modules/affiliate/pages/AdminAffiliatesList.jsx` | Explicit merchant-earnings scope, accurate empty-state explanation, and retryable load error instead of falsely showing an empty registry on failed requests. |
| `PointOfSale.Web/src/modules/affiliate/pages/AffiliateRegister.jsx`, `AffiliateLogin.jsx` | Input limits; committed enrollment with failed delivery goes to pending-login/resend with warning instead of encouraging duplicate enrollment. |
| `PointOfSale.Tests/Affiliate/AffiliateEmailVerificationLifecycleTests.cs` | Focused validation, persistence/linkage, delivery failure/duplicate, recovery/replay, deleted/expired token and four-state login regressions; profile-email assertion. Existing logging-sender test now expects its established fail-closed exception while retaining no-secret-log assertions. |
| `PointOfSale.Tests/Affiliate/AdminAffiliateTenantScopeTests.cs` | Foreign-only earnings cannot expose a global enrollee in another merchant list/detail. |
| `PointOfSale.Tests/Infrastructure/ApiRequestNormalizationFilterTests.cs` | Empty cookie-refresh DTO passes the actual shared normalization filter. This reproduces the browser-only regression missed by direct-controller tests. |
| Affiliate page `AdminAffiliatesList.test.tsx`, `AffiliateRegister.test.tsx` and matching `.d.ts` declarations | Empty/error distinction, resend recovery navigation, duplicate handling, merchant-independent payload and field limits; declarations keep the project typecheck clean. |

**Refresh defect reproduction:** authenticated browser sent `{}` to refresh; shared filter returned `400 VALIDATION_ERROR: request.RefreshToken Value is required`. Controller supported cookies but its non-nullable DTO contradicted that contract. After the nullable DTO fix, the same cookie request returned `200`; replay returned `401` and revoked the live session. See `session-ui-results.json` (before) and `session-ui-final.json` (after).

## D. Verification and quality gates

### Real headed browser execution

Reused registered `opencode-ui-test`, headed Microsoft Edge, on the primary workstation. Existing Vite app on `:5173`, current compiled API on `:5010`; artifact hashes in `build-runtime.json`. User stopped the initial API for rebuild; subsequent task-owned API process restart loaded the cookie-refresh fix. No browser request mocks were used for these live scenarios.

Email used the repository's existing loopback SMTP-capture workflow, adapted to `127.0.0.1:2527` with a strict task-recipient allowlist. Overrides are **process-local**; database configuration is unchanged. Raw email links/private browser inputs are under `%LOCALAPPDATA%\Temp\opencode\affiliate-phase1-20261021`, not the report. No real customer email/SMS was sent.

| Scenario | Actual result / evidence |
|---|---|
| Register two synthetic identities in actual UI | Both `200`; required fields and password mismatch blocked; no auth cookies/session from registration. `enrollment-ui-results.json`. |
| Case-normalized duplicate through UI | `409`; no second profile. SQL readback confirms one identity/profile per address. |
| Unverified login / protected profile | `401` / `401` for both QA identities. |
| Verification, activation, repeat link | `200`, Pending → Active; repeat `200` is intentional idempotency, still no pre-login session; SQL shows exactly one `EMAIL_VERIFIED` audit. |
| Invalid and expired verification | Both `401`, controlled response; expired task-owned unverified token remained unconsumed. |
| Login, refresh, profile navigation | `200`; same account/user/email/code; browser refresh retains `affiliate_email`. `verification-ui-results.json`. |
| Two merchant catalog contexts | Supported `storeTenantId` discovery API returned `200` for AhramStore and Shopixy; profile remained identical, including with merchant header hints. Catalog availability/content was not remediated or asserted. |
| Affiliate impersonation | Foreign promotion-link lookup `404`; spoofed account query parameter still returned the authenticated QA profile, never Memoxp. |
| Portal/Admin comparison | AhramStore list `200` empty; foreign-only Memoxp detail `404`; scoped DEBUG diagnostic returns empty merchant arrays. Accurate scope explanation rendered and captured. |
| Logout / protected navigation | Profile `401`, portal redirects to login; final logout clears cached affiliate ID/email. |
| Recovery via actual forgot/reset UI | Forgot `200`, reset `200`, replay `401`; old password `401`, new password `200`, unchanged account/code. Exactly one completion audit. `recovery-ui-results.json`. |
| Verified Pending, Suspended, Closed | UI login and protected profile all `401`. Negative states applied only to the task-owned QA profile, restored to original Active afterward. Domain terminal-state behavior was not bypassed on an existing account or claimed as a supported Closed→Active API transition. |
| Cookie refresh / refresh-token replay | Rotation `200`; previous token `401`; current session then `401`; controlled UI re-login `200`. `session-ui-final.json`, `relogin-after-replay.json`. |
| Expired refresh session | Guarded task-owned expiry fixture; refresh `401`, profile `401`, protected UI redirects to login. `expired-session-ui-results.json`. |
| Active affiliate versus private merchant APIs | Isolated context first proved affiliate profile `200`; no POS cookies. Admin list/detail, orders, customers and manual payments all `401` using a resolvable merchant host. `authorization-active-affiliate.json`. |
| POS cross-tenant spoofing | Foreign tenant-header attempts on affiliate list, orders and customers all `403 TENANT_FORBIDDEN`. |

Screenshots include `existing-portal-profile.png`, `primary-registered-pending.png`, `duplicate-registration.png`, `verification-success.png`, `qa-profile.png`, `merchant-list-scope.png`, `final-admin-scope.png`, `recovery-success.png`, `recovery-replay-rejected.png`, `expired-verification.png`.

### Persisted proof

`final-readback.json`, `authority-final.json`:

- QA primary: user `01a15efa-909e-792c-bc5f-59ad6d752253`, profile `01a15efa-9059-7496-894b-9dfd3ba59433`, code `QAGLOB759667`; verified Active.
- QA unverified: user `01a15efa-a413-789d-a277-1305229cc4b6`, profile `01a15efa-a3d0-7a33-8d04-1a8382fa99dc`, code `QAGLOB648440`; unverified Pending.
- Global duplicate emails: **0**; duplicate profile codes: **0**; orphan affiliate users: **0**; owner mismatches: **0**.
- Exactly one primary verification audit, one password-reset completion audit, one refresh-replay-detected audit at the recorded checkpoint. Final UI logout leaves no QA session authority.

### Automated gates — final results

| Gate | Result |
|---|---|
| Focused backend lifecycle, identity, Admin API/scope, discovery boundary, cross-system identity, state-machine/auth contracts, normalization filter and POS permission tests | **236 passed, 0 failed, 0 skipped**; `affiliate-final-complete.trx`. |
| Affiliate/Auth/Admin frontend service and page tests | **33 passed, 0 failed**; `frontend-complete.json`. |
| Backend compilation | Passed through `dotnet test` build; existing analyzer warnings retained. |
| `PointOfSale.Web` production build | Passed; `frontend-build-final.log` (nonfatal bundle-size warning). |
| `npx tsc --noEmit` | Passed; `typecheck-complete.log`. |
| Focused maintained lint (`npm run lint -- ...`) | Passed; `lint-complete.log` (legacy ESLint configuration deprecation warning only). |
| `git diff --check` | Passed; line-ending advisory warnings only. |

**Failed earlier, resolved:** baseline had 83/84 passing (logging-sender test expected success from a fail-closed sender); an intermediate new assertion inspected a null header collection and was corrected; initial test declaration/typecheck and direct ESLint flat-config invocation were corrected using project conventions. The first private verification script accidentally included an HTML quote in its captured token, correctly producing `401`; the capture parser was corrected, not token validation weakened. Historical intermediate evidence remains retained.

**Blocked / not applicable:** positive platform-wide registry/detail UI verification (no such authorized existing surface discovered); `Rejected` state (not in real model).

**Unexecuted:** full repository test/lint suite, browser login as separately provisioned Cashier/Manager/SuperAdmin, wall-clock access-JWT expiration, concurrent real-PostgreSQL same-token password-reset race, live SMTP-outage UI retry. Recovery conditional-claim/last-token behavior and delivery-failure/orphan prevention have focused automated coverage; cookie expiry/replay has real browser/API coverage. No claim is made that these unexecuted scenarios passed.

## E. Data and workspace preservation

- Retained only two newly enrolled addresses: `affiliate-p1-20261021-primary@example.test` and `affiliate-p1-20261021-unverified@example.test`. No unsupported account deletion or audit cleanup was performed.
- Reversible state/expiry fixtures are guarded by exact task email/account IDs in `qa-fixture.py`. Primary restored to Active; unverified token intentionally expired for the negative test. These fixtures are not production lifecycle features.
- `preservation-before.json` versus `preservation-final.json`: **23/23 baseline rows match exactly**, covering existing profiles, identity credential/linkage fields, promotions, affiliated orders, commission rows and payouts. Expected login-only session/updated-at fields on affiliate credentials were excluded from the comparison. Existing account IDs, referral codes, statuses, promotion associations and financial history were not rewritten.
- Final totals: **4 affiliate profiles/users, 2 promotions, 10 commission entries, 0 payout requests**. No synthetic POS users. **No commissions or payouts were created or changed.**
- No orders, payments, transfers, external provider actions or financial test transactions were created by this task.
- Existing manual-wallet/navigation/accounting changes and historical evidence found in the working tree were preserved. Nothing was staged or committed. No reset/clean/stash/checkout-discard/restore was used. No database reset/replacement, new disposable database environment, source migration, or tester-machine access occurred. Existing automated in-memory/SQLite unit fixtures were used as tests, not as replacements for configured UI data sources.
- Intentional catalog images, missing images, empty categories and fictional marketing counters were not changed or classified as defects.
- Updated API remains running with synthetic-only loopback email overrides; Vite and headed browser remain open. Restarting the API normally outside the task launcher restores its original configured SMTP behavior; no persistent email configuration was edited.

## F. Remaining limitations / follow-up boundary

1. **Global program administration is distinct from merchant settlement administration.** If a platform operator needs to browse/approve/suspend the entire global directory, implement that under established platform authorization with identity-only DTOs and audit requirements. Do not broaden the present merchant endpoint or expose merchant-private financial data through a global profile view.
2. The legacy email index and identity bridge remain as discovered. Supported public enrollment is globally normalized and fixed-scope; alternate/import writers would need their own audit before any compatible global uniqueness migration. No conflicting persisted identities were found.
3. Non-active login currently uses the common legacy denial message “Affiliate account is suspended” for Pending/Closed as well as Suspended; authorization denies correctly. No new status or approval workflow was invented.
4. Status mutation tests used only task-owned safe fixtures because AhramStore correctly has no merchant-ledger relationship authorizing lifecycle actions on those global QA accounts. They do not prove a platform approval UI exists.
5. Remaining test gaps are listed explicitly in D. No Phase 2 attribution, commission calculation, eligibility, catalog or payout implementation work was started.

**Stop condition:** Phase 1 confirmed fixes implemented, current application loaded, headed UI and persisted identity evidence recorded, preservation checked, report produced. Later commerce/financial phases remain untouched.

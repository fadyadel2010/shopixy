# PASS M8 CommerceHub Customer Identity & Session Authority Certification Findings

Scope: PASS M8 CommerceHub Customer Identity & Session Authority

Status: CommerceHub Customer Identity & Session Authority is production-certifiable for inspected runtime paths after request-time email-verification and order-access recovery-token hardening.

## Identity Findings

PostgreSQL `CommerceHubCustomers` owns CommerceHub customer identity. Customer IDs are created by backend SQL registration or order-owned customer lineage and are never accepted from DTOs, cookies, frontend state, projections, or route values as identity authority.

## Registration Findings

Registration normalizes email, enforces global SQL email uniqueness, creates the customer and governance profile in one SQL transaction, issues a hash-only email-verification token, and creates no authenticated session. Concurrent duplicate registration collapses to `EMAIL_EXISTS`.

## Authentication Findings

Login is the only CommerceHub customer JWT issuance path. Login requires current SQL customer existence, non-deletion, password hash authority, verified email, and current customer governance. JWT claims are candidate evidence only; request validation rechecks revocation, durable session cutoff, current SQL email verification, and governance.

## Refresh-Token Findings

CommerceHub customer refresh tokens are not issued, accepted, persisted, returned, or stored in cookies. No refresh-token replay can succeed because no CommerceHub customer refresh-token authority exists. Future refresh-token support requires separate SQL hash-only rotation and reuse-detection certification.

## Password-Reset Findings

Password-reset tokens are SHA-256 hashed at rest, expire, are single-use through conditional SQL consumption, invalidate previous active reset tokens on issuance, and revoke all customer sessions on completion. Reset flows do not create sessions.

## Email-Verification Findings

Email-verification tokens are SHA-256 hashed at rest, expire, are single-use through conditional SQL consumption, and verification does not create a session. Login and request-time access validation now both require current SQL `IsEmailVerified`.

## Ownership Findings

Customer profile, password change, email change, revoke-all, and customer order-account APIs derive caller identity from the validated `CustomerJwt` scheme and then re-read SQL by customer ID. DTO customer IDs and route values do not select ownership.

## Order-Access Findings

Authenticated customer order-account reads require SQL `CommerceHubMarketplaceOrders.CustomerId` ownership. Checkout/recovery order detail reads first load SQL order authority; signed recovery tokens are continuity evidence only and are now server-expiring signed tokens bound to order ID, order number, version, and expiry. Static copied order tokens no longer remain valid forever.

## Tenant-Isolation Findings

CommerceHub customer identity is global, while order access derives from SQL order/customer/vendor rows. Customer account APIs are `[AllowAnonymousTenant]` only because customer identity is not tenant-scoped; marketplace order data remains SQL-owned and vendor/tenant authority stays with M2/M1 boundaries.

## Concurrency Findings

SQL uniqueness prevents duplicate customer identities. Reset and verification token consumption uses conditional SQL updates, so concurrent replay consumes once. Access revocation uses durable `TokensValidAfterUtc`; access-token blacklist lookup fails closed when revocation state cannot be verified.

## Historical-Consistency Findings

Revoked sessions cannot resurrect because access validation checks current SQL cutoff and JTI revocation. Password reset, password change, email change, and revoke-all advance durable session cutoff. Recovery/order-access tokens now expire server-side and cannot be replayed indefinitely from URL or cookie copies.

## Adversarial Findings

JWT tampering fails signature, issuer, audience, lifetime, role, JTI, and SQL-current validation. Cookie tampering fails token validation or signed recovery-token validation. Password-reset and verification replay fail conditional SQL consumption. Customer impersonation and foreign order access fail SQL customer ownership checks. DTO and route tampering do not create identity or order ownership.

## Runtime Hardening Performed

- Customer access-token validation now requires current SQL `CommerceHubCustomers.IsEmailVerified`.
- CommerceHub order-access recovery tokens now include a signed expiry and reject legacy static HMAC tokens.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubOrderAccessService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubCustomerIdentitySessionAuthorityCertificationTests.cs`

## Architecture Updates

Updated all five `.ai-context` authority files with PASS M8 ownership, hardening, risk, and freeze rules.

## Explicit Answers

- Who owns customer identity authority? PostgreSQL `CommerceHubCustomers`, token rows, identity evidence, governance rows, and SQL marketplace order ownership where orders are involved.
- Can identities be spoofed? No on inspected paths.
- Can refresh tokens replay? No; CommerceHub customer refresh tokens do not exist.
- Can revoked sessions survive? No on inspected paths; JTI revocation and durable SQL cutoff are request-time authority.
- Can password-reset tokens replay? No; they are hash-only, expiring, and conditionally consumed once.
- Can email verification be bypassed? No; login and request-time validation require current SQL verification.
- Can customers access foreign orders? No through authenticated customer order APIs; recovery tokens are signed, SQL-order-bound, and expiring continuity evidence.
- Can tenant leakage occur? No inspected M8 path exposes foreign tenant/customer order authority.
- Were runtime code changes required? Yes.
- Is CommerceHub Customer Identity & Session Authority production-certifiable? Yes, for inspected runtime paths.

## Remaining Debt

- CommerceHub customer refresh-token rotation and reuse detection remain intentionally absent.
- No customer-visible active-device inventory or individual remote-session termination exists.
- Production still needs identity/session dashboards, alert packs, SMTP proof, external WORM/SIEM retention, and PostgreSQL-backed chaos evidence.

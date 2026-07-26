# PASS P2-E Storefront Customer Authority Certification Findings

Date: 2026-06-08

## Verdict

Storefront Customer Authority is production-certifiable for inspected runtime paths after hardening.
Current tenant-owned PostgreSQL `OnlineCustomers`, `OnlineCustomerRefreshTokens`, password reset and
email verification token rows, phone-risk SQL rows, SQL orders, and current SQL customer state own
storefront customer identity, lifecycle, session, profile, blocked-phone, and order-ownership
authority. JWTs, cookies, route values, DTOs, MongoDB, Redis, frontend state, and recovery snapshots
are evidence only.

## Runtime Hardening Performed

- Storefront registration now creates customer identity only and does not create access-token or
  refresh-token session authority.
- Storefront login now fails closed when durable refresh-token/session authority cannot be persisted.
- Storefront refresh-token issuance now requires current same-tenant SQL customer existence,
  active state, and non-blocked phone authority.
- Storefront refresh rotation rejects blocked-phone accounts before issuing replacement authority.
- Storefront registration, login, and profile phone updates enforce SQL phone-risk block state.
- Social login token issuance inherits the same SQL customer and blocked-phone session gate through
  `StorefrontRefreshTokenService`.

## Findings By Domain

- Identity: `OnlineCustomers` owns storefront identity. Customer IDs come from SQL-created rows and
  authenticated JWT claims are revalidated against current SQL.
- Registration: Tenant-scoped SQL email uniqueness and blocked-phone checks prevent duplicate or
  blocked registrations. Registration is identity-only.
- Authentication: Login requires current same-tenant SQL customer, active state, local password
  authority, non-blocked phone state, and durable refresh-token persistence before browser session
  authority is returned.
- Session: `OnlineCustomerRefreshTokens` owns durable session authority. Access JWT revocation uses
  JTI blacklist plus current SQL `TokensValidAfterUtc` cutoff and active customer checks.
- Ownership: Profile, session, and order account APIs derive caller ownership from authenticated
  claims plus current SQL tenant/customer predicates.
- Address: There is no separate saved-address authority table in inspected storefront code; checkout
  and order address snapshots are subordinate to checkout/order ownership authority.
- Blocked Phone: SQL phone-risk profiles block registration, login, refresh/session issuance, and
  profile phone mutation.
- Guest Conversion: Guest cart/checkout conversion remains subordinate to checkout authority and
  final order ownership is written through SQL `Orders.OnlineCustomerId`.
- Order Ownership: Account order reads first prove tenant-owned SQL order existence and
  `OnlineCustomerId` ownership, then repair/read Mongo projection as derivative output.
- Promotion Eligibility: Promotion usage remains subordinate to authenticated current customer and
  PASS P2-C SQL promotion authority.
- Checkout Integration: Checkout revalidates active current SQL customer/session state and blocks
  inactive customers before conversion.
- Tenant Isolation: Online customer reads/writes retain tenant predicates; `IgnoreQueryFilters()`
  uses reapply explicit `TenantId` and customer predicates on inspected paths.
- Concurrency: SQL uniqueness, refresh-token rows, token rotation, reset/verification token
  consumption, and checkout/order idempotency prevent replay-driven ownership drift on inspected
  paths.
- Historical Consistency: Online customers are deactivated rather than hard-deleted; order history
  remains SQL-linked and cannot be resurrected by projections.
- Adversarial: JWT forgery fails signature validation; stale JWTs fail blacklist/cutoff/current SQL
  checks; customer/order/address/session ownership is not accepted from DTO or route tampering.

## Explicit Answers

- Who owns storefront customer authority? Current tenant-owned PostgreSQL customer, session,
  phone-risk, and order rows.
- Can customer identity be forged? No, not on inspected paths.
- Can blocked phones bypass controls? No, after runtime hardening.
- Can customers access foreign customer data? No, inspected account APIs require same tenant and
  current authenticated customer ownership.
- Can customers access foreign orders? No, order reads prove SQL `OnlineCustomerId` before serving
  derivative projections.
- Can guest conversion hijack ownership? No, final ownership is SQL checkout/order authority.
- Can JWTs be forged? No, signed JWTs are validated and then rechecked against SQL state.
- Can sessions survive revocation? No, refresh tokens are SQL-revoked and access tokens are checked
  against JTI blacklist and durable cutoff.
- Can disabled customers authenticate? No.
- Can tenant leakage occur? No inspected runtime leakage remains.
- Can replay attacks corrupt customer ownership? No inspected replay path can rewrite SQL customer
  or order ownership.
- Were runtime code changes required? Yes.
- Is Storefront Customer Authority production-certifiable? Yes, for inspected runtime scope.

## Modified Runtime Files

- `PointOfSale.API/Services/Storefront/StorefrontAuthService.cs`
- `PointOfSale.API/Controllers/StorefrontAuthController.cs`
- `PointOfSale.API/Services/Storefront/StorefrontRefreshTokenService.cs`
- `PointOfSale.API/Controllers/StorefrontAccountController.cs`

## Regression Freeze

- `PointOfSale.Tests/Architecture/StorefrontCustomerAuthorityCertificationTests.cs`

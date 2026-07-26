# PASS 16.1 Customer Identity Certification Findings

Date: 2026-06-01

## Verdict

CommerceHub customer identity is **NOT READY** for public production launch.

Core runtime customer journeys are implemented, but production approval still requires deployment evidence, SMTP delivery verification, refresh-token rotation, identity telemetry dashboards and alerts, and PostgreSQL-backed chaos validation.

## Identity Inventory

Implemented runtime capabilities:

- Registration with normalized SQL-owned email uniqueness, governance-profile transaction, verification issuance, and no authenticated session before mailbox verification.
- Cookie-backed JWT login, logout, JTI revocation, durable revoke-all cutoff, and suspended/banned-account rejection.
- Forgot-password and reset-password flows using hashed, expiring, single-use SQL tokens.
- Registration verification issuance, resend verification, and verification consumption using hashed, expiring, single-use SQL tokens.
- Profile read and update.
- Password change with all-session revocation.
- Email change with current-password confirmation, duplicate-race handling, verification reset, new verification delivery, and all-session revocation.
- Guest browsing, anonymous cart use, and guest-cart adoption after login.
- Customer settings UI for profile, verification resend, credential changes, and revoke-all.
- Append-only SQL identity lifecycle evidence with EF mutation guards and PostgreSQL trigger enforcement.

Not implemented:

- CommerceHub refresh-token rotation and reuse detection.
- Social login for CommerceHub customer identity.
- Customer-visible device/session inventory with selective revocation.
- Dedicated identity metrics, dashboards, alerts, and production chaos harness.

## Runtime Hardening Performed

- Removed raw password-reset and verification token leakage from API-shaped auth responses.
- Routed CommerceHub identity mail through the existing `IEmailSender` SMTP/logging boundary.
- Added `CommerceHub:CustomerIdentity:PublicBaseUrl` for reset and verification links.
- Made password-reset and verification consumption atomic with conditional SQL updates.
- Added email-change authority and cookie invalidation after sensitive credential mutations.
- Added migration `20260601213000_CommerceHubCustomerIdentityLifecycle`.
- Added customer-facing verification and account-settings pages.

## Certification Findings

Registration is SQL-authoritative, duplicate registration races collapse to `EMAIL_EXISTS`, and unverified accounts cannot obtain authenticated access.

Login remains cookie-backed and subordinate to JTI revocation, durable session cutoff, and governance status. Password reset, password change, email change, logout, and revoke-all clear browser cookie authority.

Recovery and verification tokens are hashed at rest, expire, are single-use, and collapse concurrent replay through SQL conditional updates. Public forgot-password responses remain enumeration-safe.

Profile ownership derives only from authenticated customer JWT claims. Email changes require the current password and revoke existing sessions.

Guest cart adoption preserves the local guest snapshot until authenticated SQL-backed adoption succeeds.

Identity events are SQL evidence and are guarded append-only in EF plus PostgreSQL. External WORM retention and identity-specific monitoring remain deployment work.

## Required Answers

| Question | Answer |
|---|---|
| Can customers reliably register? | Yes, at the implemented runtime boundary. |
| Can customers reliably log in? | Yes, with access-token expiry requiring re-login. |
| Can customers reliably recover accounts? | Yes, when SMTP delivery and the migration are deployed correctly. |
| Can sessions remain secure and consistent? | Partially. Revocation is enforced, but refresh-token rotation and device-session UX remain missing. |
| Can customers manage profiles safely? | Yes, for implemented profile, password, and email mutations. |
| Is guest identity safe? | Yes, on the inspected guest-cart adoption path. |
| Is frontend/backend identity behavior aligned? | Yes, for implemented routes and settings controls. |
| Are identity incidents auditable? | Partially. SQL evidence exists; dashboards, alerts, and external retention remain missing. |
| Are critical identity risks present? | Yes. Missing refresh-token rotation and unverified deployment controls remain launch blockers. |
| Is CommerceHub customer identity production-ready? | No. |

## Remaining Debt

- Apply the migration and extend production evidence-role provisioning and verification inventory for `CommerceHubCustomerIdentityEvents`.
- Configure and verify SMTP delivery plus `CommerceHub:CustomerIdentity:PublicBaseUrl` in every environment.
- Add refresh-token rotation, reuse detection, and customer-visible device/session management.
- Add identity metrics, dashboards, alerts, and PostgreSQL-backed race tests for reset, verification, email change, revoke-all, and disablement timing.

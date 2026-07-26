# PASS 16.1B-D.1 Privileged Refresh Token Hardening Findings

Date: 2026-06-02

## Findings

| Event | Previous refresh behavior | Hardened behavior |
| --- | --- | --- |
| POS role change | Refresh token could survive and mint a token for the current role. | `PrivilegedRefreshTokenRevocationInterceptor` clears `Users.RefreshToken` and expiry on `User.Role` change. |
| POS user disable/delete | Refresh endpoint rejected disabled users, but stored refresh binding could remain. | Interceptor clears the binding when `User.IsActive` becomes false or `IsDeleted` becomes true. |
| Tenant suspension/deactivation | Access tokens failed current SQL tenant lineage, but tenant user refresh bindings could remain. | Interceptor clears all tenant POS refresh bindings when `Tenant.IsActive` becomes false or `Tenant.Status` becomes `Suspended`/`Deleted`. |
| Vendor suspension/ban | Banned vendor login/refresh failed, suspended vendor login/refresh was weaker than runtime access/listing authority. | `EnsureTenantLoginAllowedAsync` now rejects suspended vendors; governance status changes to `Suspended`/`Banned` clear tenant POS refresh bindings. |
| Super Admin governance restriction | Runtime path already cleared refresh authority in governance service. | Interceptor also clears Super Admin refresh authority on admin governance restriction. |
| Refresh replay | Rejected, but POS replay/not-current evidence was not durable enough. | POS refresh replay writes `REFRESH_TOKEN_REPLAY_DETECTED` to `AuditLogs` with actor/tenant when known, timestamp, IP, and correlation id. |

## Refresh Authority

POS refresh authority is now: presented raw refresh credential -> hash -> current `Users.RefreshToken` hash -> non-expired binding -> active user -> active tenant lineage -> current vendor governance.

Super Admin refresh authority is: presented raw refresh credential -> current `SuperAdmins.RefreshTokenHash` -> non-expired binding -> current `TokenVersion` -> current admin governance.

Refresh authority is no longer stronger than access-token authority for the inspected privileged paths.

## Lifecycle Matrix

| Event | Refresh revoked? |
| --- | --- |
| Logout | Yes. POS clears current `Users.RefreshToken`; Super Admin clears `RefreshTokenHash`. |
| Revoke all | CommerceHub customer cutoff applies to customer access sessions; privileged POS has one current binding and Super Admin has one current hash/version. |
| Password change | Super Admin password update advances token version/clears authority; POS password-change/reset ceremony remains debt. |
| Role change | Yes. POS `User.Role` modification clears refresh binding. |
| User disable | Yes. POS `User.IsActive=false` clears refresh binding. |
| User ban | Yes for Super Admin governance ban; vendor governance ban clears tenant POS refresh bindings. |
| Tenant suspension | Yes. Tenant `Status=Suspended` or `IsActive=false` clears tenant POS refresh bindings. |
| Tenant deactivation | Yes. `IsActive=false` clears tenant POS refresh bindings. |

## Chaos Certification

Actively searched for stale refresh authority, tenant-suspension bypass, role-change bypass, governance bypass, replay reuse, replay without evidence, and refresh resurrection.

No surviving privileged refresh authority remains in the hardened inspected runtime paths. Remaining debt is POS password-reset/change ceremony, per-device privileged-session inventory, and production alert packs over the new audit event.

## Required Answers

Can refresh tokens survive role changes? No, POS role changes now clear refresh bindings.

Can refresh tokens survive tenant suspension? No, tenant suspension/deactivation clears tenant POS refresh bindings.

Can refresh tokens survive governance restrictions? No for inspected privileged vendor/admin restrictions; vendor suspension/ban and admin restriction revoke refresh authority.

Is refresh replay detected? Yes.

Is refresh replay audited? Yes, POS replay writes `REFRESH_TOKEN_REPLAY_DETECTED`; Super Admin reuse writes `TOKEN_REUSE_DETECTED`.

Can stale refresh authority survive? No surviving path was found after hardening.

Is refresh authority now aligned with privileged authority? Yes for inspected POS and Super Admin privileged paths.

Is privileged refresh-token security production-certifiable? Yes for runtime authority, with operational monitoring debt noted.

## Regression Freeze

Added:

- `CommerceHubPrivilegedRefreshCertificationTests`
- `CommerceHubRefreshReplayCertificationTests`
- `CommerceHubGovernanceRefreshRevocationTests`

These freeze role-change revocation, governance revocation, tenant-suspension revocation, replay detection, replay auditing, and refresh-authority validation.

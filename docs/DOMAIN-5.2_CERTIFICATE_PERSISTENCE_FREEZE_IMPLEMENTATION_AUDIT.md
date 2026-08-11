# DOMAIN-5.2 Certificate Persistence Freeze Implementation Audit

## 1. Executive Summary

Certification: PASS

DOMAIN-5.2 implemented the certificate persistence/state foundation required by the DOMAIN-5.1 blueprint. The work is limited to entity state, EF configuration, migration, tenant/domain binding, idempotency persistence, generation/fencing metadata, safe certificate metadata, and regression/architecture tests.

No ACME client, Caddy integration, provisioning execution, renewal execution, revocation execution, SNI probing, TLS edge behavior, scheduler, or frontend SSL action was implemented.

## 2. Pre-Implementation Source Reality

OBSERVED REALITY:

- `SslCertificate` existed as a minimal metadata row with `DomainId`, provider/status, ARN/thumbprint/validity dates, renewal dates, failure reason, timestamps, soft-delete, and history JSON.
- `CertificateStatus` only represented `Pending`, `Active`, `Expired`, `Revoked`, and `Failed`.
- `Domain` already carried `TenantId`, `HostName`, `NormalizedHostName`, lifecycle state, `SslCertificateId`, and PostgreSQL `xmin` versioning.
- Existing tenant isolation is primarily `ITenantOwned` plus `ApplicationDbContext` query filters.
- Existing idempotency is table-scoped and tenant-scoped, not a global generic mechanism.
- Existing concurrency/fencing patterns use PostgreSQL `xmin`, durable lease metadata, lock owner references, and operation-scoped unique indexes.

IMPLEMENTED CHANGE:

- Extended the existing certificate model instead of introducing a competing lifecycle model.
- Kept one canonical business lifecycle state: `CertificateStatus`.
- Added a certificate operation table only for idempotency/operation fencing persistence, not runtime execution.

## 3. DOMAIN-5.1 Requirements Matrix

| Requirement | Result | Evidence |
| --- | --- | --- |
| TenantId/DomainId attribution | Implemented | `SslCertificate` implements `ITenantOwned`; composite FK `(TenantId, DomainId)` to `Domains(TenantId, Id)` |
| Canonical lifecycle states | Implemented | `CertificateStatus` includes Pending, Requested, Authorizing, Issued, Activating, Active, Renewing, Revoked, Expired, Failed |
| Generation model | Implemented | `Generation`, `ReplacementCertificateId`, active and generation uniqueness indexes |
| Idempotency persistence | Implemented | `SslCertificateOperation` plus certificate idempotency indexes |
| Concurrency/fencing | Implemented | `RowVersion`/`xmin`, `FencingToken`, `LockOwnerReference`, `LeaseExpiresAtUtc` |
| Safe provider/edge references | Implemented | Opaque reference columns only |
| Safe certificate metadata | Implemented | Issuer/subject/SAN/thumbprint/fingerprint/serial/signature/validity timestamps |
| Renewal/revocation/failure metadata | Implemented | Retry, renewal, revocation, failure, correlation metadata columns |
| Required indexes/constraints | Implemented | PostgreSQL catalog inspection confirmed expected filtered indexes and check constraints |
| Private-key boundary | Implemented | No private-key/PFX/PEM/ACME secret/challenge credential columns |
| DOMAIN-4 regression safety | Verified | Focused DOMAIN-4/domain tests passed |

## 4. Entity Changes

IMPLEMENTED CHANGE:

- `PointOfSale.Core/Entities/SslCertificate.cs` is now the canonical metadata-only certificate generation entity.
- `PointOfSale.Core/Entities/SslCertificateOperation.cs` was added for durable idempotency, operation status, fencing, and lease metadata.
- `PointOfSale.Core/Entities/Domain.cs` now exposes certificate history through `SslCertificates` and certificate operation history through `SslCertificateOperations`. The legacy in-memory `SslCertificate` pointer is ignored by EF to avoid a competing relationship model.

VALIDATION EVIDENCE:

- Static tests verify required certificate properties, operation properties, and no forbidden secret-bearing properties.

## 5. Enum/State Changes

IMPLEMENTED CHANGE:

- `CertificateStatus` now represents all canonical DOMAIN-5.1 states while preserving existing numeric values for legacy migration safety.
- Added `CertificateOperationPhase`, `CertificateOperationType`, and `CertificateOperationStatus` for operation persistence without duplicating business lifecycle authority.
- `SslCertificate.TransitionTo` rejects terminal revoked/expired reactivation and deterministic failed retry behavior.

VALIDATION EVIDENCE:

- Entity/state tests cover canonical representability, revoked/expired terminal behavior, and retryable/non-retryable failed behavior.

## 6. Database Schema Changes

IMPLEMENTED CHANGE:

- Added explicit certificate `TenantId`, `NormalizedHostName`, generation, operation correlation, provider refs, ACME/edge refs, certificate metadata, activation metadata, renewal metadata, revocation metadata, failure metadata, fencing, lease, and audit linkage columns.
- Added `SslCertificateOperations`.
- Added `AK_Domains_TenantId_Id` so certificates and certificate operations can use composite tenant/domain FKs.

VALIDATION EVIDENCE:

- Applied migration `20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze` to PostgreSQL database `PosEcommerceDb_Baseline`.
- Catalog inspection confirmed the migration history row and expected columns in `SslCertificates` and `SslCertificateOperations`.

## 7. Index/Constraint Changes

IMPLEMENTED CHANGE:

- Added tenant/domain, tenant/hostname/status, renewal due, expiry monitoring, retry due, activation phase, generation uniqueness, active uniqueness, and idempotency indexes.
- Added PostgreSQL filtered unique indexes for active and active/activating generation semantics.
- Added check constraints for canonical status values, positive generation, terminal metadata, failed metadata, activation expected thumbprint, operation idempotency key, and nonnegative fencing.

VALIDATION EVIDENCE:

- PostgreSQL catalog inspection confirmed:
  - `UQ_SslCertificates_TenantId_DomainId_Active`
  - `UQ_SslCertificates_TenantId_DomainId_ActiveActivatingGeneration`
  - `UQ_SslCertificates_TenantId_DomainId_Generation`
  - `UQ_SslCertificates_TenantId_DomainId_IdempotencyKey`
  - `UQ_SslCertificateOperations_Tenant_Domain_Type_IdempotencyKey`
  - `UQ_SslCertificateOperations_Tenant_Domain_Type_InProgress`
  - renewal, expiry, retry, phase, tenant/domain, and fencing indexes

## 8. Tenant Isolation Model

IMPLEMENTED CHANGE:

- Certificates and certificate operations are tenant-owned.
- `ApplicationDbContext` tenant filters now include `SslCertificate` and `SslCertificateOperation`.
- Existing certificate read/delete paths were tightened to tenant/domain/certificate boundaries where they touched active certificate metadata.
- Database FKs bind certificate rows to a domain with the same tenant.

VALIDATION EVIDENCE:

- Configuration tests assert composite FK evidence and tenant filter presence.
- Focused domain service regression tests passed.

## 9. Idempotency Model

IMPLEMENTED CHANGE:

- Added `SslCertificateOperation` as the canonical durable idempotency scope for provisioning, renewal, retry, and revocation submissions.
- Idempotency uniqueness is scoped to `(TenantId, DomainId, OperationType, IdempotencyKey)`.
- In-progress operation uniqueness is scoped to `(TenantId, DomainId, OperationType)` for per-domain operation isolation.
- Certificate rows also carry `OperationId` and optional `IdempotencyKey` for correlation.

VALIDATION EVIDENCE:

- Static tests assert idempotency index names and operation fields.
- PostgreSQL catalog inspection confirmed operation idempotency indexes.

## 10. Generation/Fencing Model

IMPLEMENTED CHANGE:

- Certificate generation is represented as a distinct certificate row with `Generation`.
- Replacement linkage is represented by `ReplacementCertificateId`.
- Current active certificate can coexist with a replacement generation in non-active lifecycle states until future activation proof.
- Fencing/lease primitives are persisted on both certificate rows and certificate operation rows.

VALIDATION EVIDENCE:

- Migration includes generation backfill for existing certificate rows.
- PostgreSQL catalog inspection confirmed generation uniqueness and fencing indexes.

## 11. Secret/Private-Key Boundary Verification

IMPLEMENTED CHANGE:

- No private-key, PEM private material, PFX/PKCS12 material, ACME account secret, DNS credential, challenge credential, Caddy storage content, serialized certificate bundle, or byte-array certificate blob field was introduced.
- `SubjectAlternativeNamesJson` and history JSON remain metadata-only.

VALIDATION EVIDENCE:

- Source scan for forbidden certificate-secret names found only the DOMAIN-5.2 guard test forbidden-word list, one unrelated commerce signing key, and the explicit no-secret comment on `SslCertificate`.
- Live PostgreSQL catalog query over `SslCertificates` and `SslCertificateOperations` returned `SecretColumns: <none>`.

## 12. Migration Audit

IMPLEMENTED CHANGE:

- Created `20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze`.
- Migration is EF-backed with a designer file and updated model snapshot.
- The executable migration is scoped to certificate/domain relationship persistence changes.
- Existing certificate rows are backfilled from domain tenant/hostname data, assigned generation ordering, and terminal metadata defaults where required by constraints.

VALIDATION EVIDENCE:

- `dotnet ef database update 20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext` succeeded.
- PostgreSQL catalog inspection confirmed the migration history row.
- Generated SQL evidence was also retained at `.codex-temp/domain-5-2.sql`.

## 13. Test Evidence

VALIDATION EVIDENCE:

- `dotnet build PointOfSale.sln --no-restore -p:RestoreIgnoreFailedSources=true`
  - Result: passed, 0 warnings, 0 errors.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~Domain52CertificatePersistenceFreezeTests|FullyQualifiedName~DomainPersistenceTests|FullyQualifiedName~DomainReadServiceTests|FullyQualifiedName~DomainWriteServiceCommandTests|FullyQualifiedName~DomainVerificationServiceTests"`
  - Result: passed, 95 passed, 0 failed, 0 skipped.

## 14. Regression Evidence

VALIDATION EVIDENCE:

- Existing DOMAIN-4/domain persistence and domain service tests included in the focused test run passed.
- No DOMAIN-4 tests were weakened.

## 15. Out-of-Scope Confirmation

CONFIRMED NOT IMPLEMENTED:

- ACME client/packages
- Caddy SDK/API/Caddyfile integration
- Certificate provisioning workflow
- Renewal scheduler/execution
- Revocation provider/execution
- HTTP-01/TLS-ALPN implementation
- SNI probing
- TLS edge behavior
- Hangfire SSL jobs
- Frontend provision/renew/revoke buttons

## 16. Known Observations

OBSERVED REALITY:

- PostgreSQL cannot enforce `Domain.SslCertificateId` points only to an `Active` `SslCertificate` using an ordinary check constraint because that is a cross-table status invariant.

IMPLEMENTED CHANGE:

- The schema now provides the tenant/domain/status/generation data required for the future activation transaction to enforce that invariant safely.
- Existing read/delete paths that touch the active certificate pointer were tightened around tenant/domain/certificate boundaries.

REMAINING FUTURE WORK:

- DOMAIN-5.5/DOMAIN-5.8 activation code must enforce the `Domain.SslCertificateId` active-only invariant at the service transaction boundary when it starts writing that pointer.
- This wave intentionally does not implement runtime certificate operations.

## 17. Certification

PASS

DOMAIN-5.2 establishes the canonical certificate persistence freeze required by DOMAIN-5.1: tenant/domain attribution, canonical lifecycle representation, generation semantics, idempotency persistence, fencing/concurrency primitives, safe metadata-only provider/edge references, renewal/revocation/failure metadata, required indexes and constraints, soft-delete-aware history, live PostgreSQL migration verification, and secret-boundary guard tests.

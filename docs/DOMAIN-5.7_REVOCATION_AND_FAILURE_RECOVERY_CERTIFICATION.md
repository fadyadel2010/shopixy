# DOMAIN-5.7 - Certificate Revocation and Failure Recovery Certification

Date: 2026-08-09

Final certification: PASS WITH OBSERVATIONS

## 1. Executive Summary

OBSERVED REALITY: DOMAIN-5.2 already provided tenant/domain/generation certificate metadata, revocation columns, retry metadata, and operation fencing. DOMAIN-5.5 already required Caddy route admission and external SNI proof before activation. DOMAIN-5.6 already provided the Hangfire-only certificate renewal monitor.

IMPLEMENTED CHANGE: Added explicit revocation/recovery orchestration without adding a scheduler, database migration, raw ACME client, or private-key custody. Revocation uses `SslCertificateOperation` with `OperationType.Revocation`; expiry and renewal exhaustion are handled by the existing renewal monitor.

VALIDATION EVIDENCE: `dotnet build PointOfSale.sln` passed. `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~Domain5|FullyQualifiedName~Domain4"` passed 48 tests. `git diff --check` passed with line-ending warnings only.

REMAINING LIMITATION: Real public/staging Caddy/SNI/CA validation was not executed. Full-suite test run has unrelated existing failures outside DOMAIN-4/5.

## 2. Source Reality Audit

Revocation capabilities existed as persistence metadata only: `CertificateOperationType.Revocation`, `CertificateOperationPhase.Revocation`, `SslCertificate.RevokedAtUtc`, `RevocationReason`, actor, and provider reference fields.

Recovery capabilities existed for renewal, activation, Caddy admission, SNI proof, retry backoff, operation uniqueness, leases, and fencing tokens. Missing behavior was explicit revocation orchestration, expired authoritative certificate degradation, renewal exhaustion terminal behavior, revocation events, and recovery telemetry/health classification.

Certificate states: `Pending`, `Active`, `Expired`, `Revoked`, `Failed`, `Requested`, `Authorizing`, `Issued`, `Activating`, `Renewing`.

Operation types: `Provisioning`, `Renewal`, `Retry`, `Revocation`, `Activation`, `MetadataSync`.

## 3. DOMAIN-5.1 Compliance

OBSERVED REALITY: Caddy remains edge/certificate material owner; Shopixy owns policy metadata and lifecycle state.

IMPLEMENTED CHANGE: Revocation provider command is represented as a safe-reference contract. No private key, PEM, PFX, Certes, ACMESharp, or BouncyCastle dependency was introduced.

## 4. DOMAIN-5.2 Compatibility

No migration was required. Existing `SslCertificates` and `SslCertificateOperations` columns represent reason, retry, failure, tenant/domain correlation, generation, idempotency, lease, and fencing metadata.

## 5. DOMAIN-5.3 Compatibility

Added `ICertificateProvider.RevokeCertificateAsync(...)` and safe result/request records. The current Caddy-managed provider foundation returns an explicit out-of-scope provider failure rather than fake success; test fakes exercise successful provider behavior.

## 6. DOMAIN-5.4 Compatibility

Provisioning remains the only issuance entry path. Revocation replacement uses the existing renewal/provisioning/activation chain instead of creating a new issuance engine.

## 7. DOMAIN-5.5 Compatibility

Replacement authority still requires `CertificateActivationService`, Caddy edge admission, external SNI proof, and durable DB commit before the replacement becomes active.

## 8. DOMAIN-5.6 Compatibility

The existing Hangfire recurring job id `certificate-renewal-monitor` remains the scheduler. Candidate detection was extended for expiry recovery and renewal exhaustion avoidance.

## 9. Revocation Ownership

Shopixy records policy, reason, audit, events, and operation state. Provider/edge revocation is issued only through `ICertificateProvider`. No application private material handling was added.

## 10. Revocation Admission

Admission validates tenant id, domain id, certificate id, tenant/domain/certificate correlation, soft-delete state, idempotency key, and canonical reason code. Certificate id alone is not sufficient.

## 11. Revocation State Machine

Revocation claims use `SslCertificateOperation` with `Revocation` type and `Revocation`/`RetryBackoff` phases. Completed operations replay without duplicate provider calls. Retryable provider failures persist `RetryAfterUtc`.

## 12. Compromise Handling

Compromise reasons are explicit: `SECURITY_COMPROMISE` and `CERTIFICATE_COMPROMISED`. A valid authoritative compromised certificate triggers replacement first unless immediate revocation is requested. If replacement does not become authoritative in the DB, provider revocation is skipped and the old valid certificate is preserved with failure metadata.

## 13. Expiry Handling

Expired authoritative certificates are classified as `EXPIRED`, transitioned to `Expired`, and SSL is degraded for non-primary domains by clearing `SslCertificateId` and returning to `Verified`. Primary lifecycle semantics are preserved.

## 14. Renewal Exhaustion

`RENEWAL_EXHAUSTED` is now terminal for a valid active certificate. The scheduler excludes exhausted valid certificates from repeated provider attempts until expiry requires recovery.

## 15. Edge Certificate Mismatch

Existing activation SNI proof rejects SAN, fingerprint, serial, issuer, validity, and expiry mismatches. Explicit edge mismatch recovery code is represented by `EDGE_CERTIFICATE_MISMATCH`; full unrelated-certificate reconciliation remains a future infrastructure validation item.

## 16. SNI Failure Recovery

SNI failure never marks activation or renewal success. Retryability follows the probe result; terminal identity mismatches fail closed.

## 17. Recovery Orchestration

Recovery reuses `CertificateRenewalService`, `CertificateActivationService`, `ICaddyEdgeClient`, and `IEdgeCertificateProbe`.

## 18. Distributed Fencing

Operations use persisted unique idempotency, in-progress uniqueness, `FencingToken`, `LockOwnerReference`, and `LeaseExpiresAtUtc`. Process-local semaphores remain only in earlier provisioning/activation services as contention smoothing, not the durable correctness mechanism.

## 19. Idempotency

Revocation idempotency key scope is tenant + domain + operation type + key. Duplicate revocation requests replay the same operation and do not duplicate provider revocation after success.

## 20. Retry / Backoff

Revocation provider failures use the existing renewal scheduler backoff settings. No `Thread.Sleep`, timer, or second retry framework was introduced.

## 21. Replacement Strategy

Valid current certificates are not revoked until replacement authority moves in the database. The revocation service verifies the durable authority move after renewal before issuing provider revocation.

## 22. Old Certificate Safety

Old valid certificates remain authoritative during recoverable replacement failures. Superseded old certificates may be revoked after replacement proof and durable authority change.

## 23. Primary Domain Safety

Primary lifecycle state is preserved. SSL degradation does not demote `Primary`; non-primary SSL-active domains degrade to `Verified`.

## 24. Tenant Isolation

All revocation and recovery queries correlate `TenantId`, `DomainId`, and `CertificateId`. Wrong-tenant revocation test passes.

## 25. Audit

Added audit actions include `revocation_requested`, `revocation_succeeded`, `revocation_failed`, `certificate_compromised`, `certificate_expired`, `recovery_started`, and `ssl_degraded`. Audit metadata excludes certificate material and secrets.

## 26. Events

Added event types for revocation requested/succeeded/failed, compromised, expired, SSL degraded, and SSL restored. New revocation success event is published after DB save.

## 27. Observability

Added counters for revocation, recovery, expired, compromised, edge mismatch, SSL degraded, and SSL restored outcomes on the existing certificate meter.

## 28. Health

Certificate health now reports unhealthy when authoritative certificates are expired, revoked, compromised, or past `NotAfterUtc`; valid renewal-exhausted certificates degrade health without making the whole API unhealthy.

## 29. Database Safety

No destructive DB operation was performed. No migration was applied. Source-level schema inspection confirms revocation/retry/fencing/index fields in existing EF configuration and snapshot.

## 30. Migration Decision

Migration required: NO.

## 31. Crash Recovery

Recovery uses persisted operations with leases, status, attempt count, and fencing token. A restarted worker reloads the operation and certificate/domain state before continuing.

## 32. DB Failure Recovery

Replacement authority is trusted only after durable DB state moves off the old certificate. If a claimed success does not produce a DB authority move, provider revocation is skipped and the operation fails retryably.

## 33. Failure Matrix

Renewal temporary failure + valid current: keep `SslActive`, retry.
Renewal exhausted + valid current: keep authority, terminal alert/degraded health.
Renewal exhausted + expired: expiry recovery.
Provider timeout: retry if provider marks retryable.
Provider permanent failure + valid current: keep old, fail/alert.
Caddy unavailable + valid current: keep old, retry through activation failure handling.
SNI mismatch + valid old: reject replacement.
Certificate expired: degrade SSL and recover.
Certificate revoked: terminal/idempotent no-op if already revoked.
Certificate compromised: mark reason, require emergency replacement before provider revocation unless immediate revocation is requested.
Replacement activation failure: old valid certificate remains authoritative.
Wrong tenant: reject.
Primary domain recovery: preserve primary state.

## 34. Test Evidence

Added DOMAIN-5.7 tests for duplicate revocation, replacement-before-revocation, false replacement success rejection, wrong-tenant rejection, renewal exhaustion candidate exclusion, and expired recovery candidate inclusion.

## 35. Regression Evidence

`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~Domain5|FullyQualifiedName~Domain4"`: Passed 48, Failed 0.

Full `dotnet test PointOfSale.sln --no-build`: Failed 11 unrelated tests and aborted because of existing non-certificate architecture artifact and POS/CommerceHub integration failures.

## 36. Boundary Scan

Changed-file scan found no raw ACME client, second scheduler, timer, `Thread.Sleep`, PEM/PFX/private-key persistence, or Caddy credential handling. Hits for `private_key`, `pem`, and `pfx` are sanitizer denylist strings in `CertificateProviderError`.

## 37. Secret Leakage Scan

No private key, certificate material, challenge token, Caddy credential, ACME credential, PEM, or PFX field was added.

## 38. Real Infrastructure Validation

NOT EXECUTED. No public staging Caddy/SNI/CA environment was safely available in this workspace. Provider foundation deliberately fails rather than faking real CA revocation.

## 39. Known Observations

The worktree contains many pre-existing unrelated modified/untracked files. Full-suite failures predate or sit outside this DOMAIN-5.7 change surface. Real provider revocation requires a concrete provider adapter behind `ICertificateProvider.RevokeCertificateAsync`.

## 40. Certification

PASS WITH OBSERVATIONS.

The implemented application layer satisfies the critical safety invariant: a valid current certificate remains authoritative during recoverable failures, and revocation of that certificate is deferred until a replacement is durably authoritative. Expired/revoked/compromised certificates are not re-authorized by the certificate entity transition model. Hangfire remains the only certificate scheduler.

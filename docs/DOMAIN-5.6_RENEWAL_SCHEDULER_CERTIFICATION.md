# DOMAIN-5.6 Renewal Scheduler Certification

Audit date: 2026-08-09

Certification: PASS WITH OBSERVATIONS

## 1. Executive Summary

OBSERVED REALITY: DOMAIN-5.2 already provides `SslCertificate`, `SslCertificateOperation`, generation, renewal dates, retry metadata, and indexes. DOMAIN-5.3 provides provider contracts. DOMAIN-5.5 provides Caddy route reconciliation and external SNI probing.

IMPLEMENTED CHANGE: Added one canonical Hangfire renewal monitor, bounded candidate discovery, configuration-driven policy/backoff/provider budgets, renewal reconciliation, replacement-generation activation through `CertificateActivationService`, safe telemetry, aggregate health, audit, events, and focused tests.

VALIDATION EVIDENCE: API build passed. DOMAIN-5.6 focused tests passed 5/5. DOMAIN-4 through DOMAIN-5.6 focused regression passed 115/115 on rerun.

REMAINING FUTURE WORK: Real staging Hangfire execution and public SNI validation require authorized staging infrastructure and a safe existing domain fixture.

## 2. DOMAIN-5.1 Compliance

OBSERVED REALITY: DOMAIN-5.1 makes Caddy the canonical TLS/ACME owner and Shopixy the policy/reconciliation owner.

IMPLEMENTED CHANGE: Renewal never handles private keys or ACME material. It reconciles Caddy-observed edge state, uses provider contracts only, and activates through existing SNI proof.

VALIDATION EVIDENCE: Boundary scan over new renewal files found no private-key/PEM/PFX/challenge-token handling.

REMAINING FUTURE WORK: Production Caddy renewal trigger semantics depend on the real provider adapter maturing beyond the current foundation shell.

## 3. DOMAIN-5.2 Compatibility

OBSERVED REALITY: Persistence already includes `NextRenewalUtc`, `NotAfterUtc`, retry fields, generation, replacement linkage, and `SslCertificateOperation`.

IMPLEMENTED CHANGE: No schema change or migration. Renewal reuses `SslCertificateOperation` with `OperationType.Renewal`.

VALIDATION EVIDENCE: No DOMAIN-5.6 migration was created; focused persistence regression passed.

REMAINING FUTURE WORK: A future explicit `Superseded` status would make retired-generation display clearer, but the current implementation represents retirement with non-active `Renewing` plus `ReplacementCertificateId` to honor the freeze.

## 4. DOMAIN-5.3 Compatibility

OBSERVED REALITY: `ICertificateProvider` is provider-neutral and Caddy-managed ACME remains edge-owned.

IMPLEMENTED CHANGE: Renewal uses `EnsureAccountAsync` and `CreateOrderAsync` only through `ICertificateProvider`.

VALIDATION EVIDENCE: DOMAIN-5.3 regression passed; no ACME library was introduced.

REMAINING FUTURE WORK: Concrete provider execution still needs authorized Caddy/provider integration.

## 5. DOMAIN-5.4 Compatibility

OBSERVED REALITY: Provisioning creates issued metadata and does not activate.

IMPLEMENTED CHANGE: Renewal creates replacement metadata independently for `OperationType.Renewal`, preserving the old active certificate until activation proof.

VALIDATION EVIDENCE: DOMAIN-5.4 regression passed.

REMAINING FUTURE WORK: Manual renewal API is deferred to DOMAIN-5.8 but can call the same service.

## 6. DOMAIN-5.5 Compatibility

OBSERVED REALITY: `CertificateActivationService`, `ICaddyEdgeClient`, and `IEdgeCertificateProbe` own activation and SNI proof.

IMPLEMENTED CHANGE: Renewal calls `CertificateActivationService.ActivateAsync` for replacement activation and does not duplicate Caddy route or SNI engines.

VALIDATION EVIDENCE: DOMAIN-5.5 regression passed; replacement activation test proves old generation retires after proof.

REMAINING FUTURE WORK: Real public SNI validation was not executed in this workspace.

## 7. Hangfire Infrastructure Reality

OBSERVED REALITY: Hangfire uses PostgreSQL storage via `UsePostgreSqlStorage`, `AddHangfireServer`, and centralized `HangfireInitializationHostedService` idempotent `RecurringJob.AddOrUpdate`.

IMPLEMENTED CHANGE: Registered exactly one renewal recurring job in that initializer: `certificate-renewal-monitor`.

VALIDATION EVIDENCE: Source test asserts `RecurringJob.AddOrUpdate<CertificateRenewalSchedulerJob>`.

REMAINING FUTURE WORK: Runtime dashboard/storage inspection requires authorized database access.

## 8. Renewal Ownership Model

OBSERVED REALITY: Caddy owns ACME execution and certificate material; Shopixy owns tenant/domain authorization and lifecycle metadata.

IMPLEMENTED CHANGE: Shopixy scans, claims, reconciles, probes, activates, records audit/events/metrics, and never stores private material.

VALIDATION EVIDENCE: Build, tests, and boundary scan passed.

REMAINING FUTURE WORK: DOMAIN-5.7 will own revocation/expiry recovery.

## 9. Renewal State Machine

OBSERVED REALITY: `CertificateStatus` has no explicit `Superseded` state.

IMPLEMENTED CHANGE: Active old generation remains `Active` during renewal. Replacement moves `Pending -> Requested -> Authorizing -> Issued -> Activating -> Active`; old generation moves to non-active `Renewing` only after replacement activation proof and durable commit path.

VALIDATION EVIDENCE: `Renewal_ReconcilesObservedReplacementActivatesNewGenerationAndRetiresOldAfterProof`.

REMAINING FUTURE WORK: Add explicit retired/superseded state only if a future migration is approved.

## 10. Candidate Discovery

OBSERVED REALITY: Certificate indexes support `NextRenewalUtc`, `NotAfterUtc`, retry due, status, tenant/domain, and active certificate filtering.

IMPLEMENTED CHANGE: Added server-side bounded query for active, renewal-eligible, domain-linked certificates on non-deleted `SslActive`/`Primary` domains for active tenants.

VALIDATION EVIDENCE: Candidate test selects due cert and excludes non-due, archived, and deleted domains.

REMAINING FUTURE WORK: Add live query-plan review against production-like PostgreSQL.

## 11. Renewal Eligibility Policy

OBSERVED REALITY: `NextRenewalUtc` is available and `NotAfterUtc` fallback exists.

IMPLEMENTED CHANGE: `NextRenewalUtc` is primary; missing values are derived from `NotAfterUtc - RenewalThreshold`.

VALIDATION EVIDENCE: Options and candidate tests passed.

REMAINING FUTURE WORK: Add admin-read model surfaces for warning/urgent/critical counts in later UI/API waves.

## 12. Configuration

IMPLEMENTED CHANGE: Added `CertificateRenewalScheduler` options: scan interval, batch size, concurrency, thresholds, max attempts, backoff, max retry delay, provider budget/window, stale timeout, jitter.

VALIDATION EVIDENCE: Options bind/validate on startup and unit validation test passed.

REMAINING FUTURE WORK: Environment-specific overrides should be supplied by ops.

## 13. Distributed Fencing

IMPLEMENTED CHANGE: Renewal uses `SslCertificateOperation` per tenant/domain/type/idempotency plus fencing token, lock owner, lease expiry, and serializable transaction where available.

VALIDATION EVIDENCE: Service source and tests assert operation reuse; database indexes are inherited from DOMAIN-5.2.

REMAINING FUTURE WORK: Multi-process PostgreSQL contention test needs real DB authorization.

## 14. Idempotency

IMPLEMENTED CHANGE: Deterministic key `certificate-renewal:{tenant}:{domain}:{certificate}:g{generation}` makes duplicate scheduler executions converge on the same renewal operation.

VALIDATION EVIDENCE: Retry/replay paths reuse existing operation and replacement generation.

REMAINING FUTURE WORK: DOMAIN-5.8 manual command should use the same service boundary.

## 15. Provider Rate Limits

IMPLEMENTED CHANGE: Configurable per-domain renewal attempt budget within a provider window; rate-limited work persists retryable failure and retry time.

VALIDATION EVIDENCE: Failure preservation test covers provider rate-limit behavior.

REMAINING FUTURE WORK: Broader tenant-wide provider budgets can be added if provider policies require it.

## 16. Retry / Backoff

IMPLEMENTED CHANGE: Bounded exponential backoff with max delay, max attempts, optional jitter, and no sleeps inside Hangfire.

VALIDATION EVIDENCE: Retryable failures persist `RetryAfterUtc`; tests passed.

REMAINING FUTURE WORK: Hangfire delayed retry handoff can be specialized later; current recurring scan picks due retries.

## 17. Caddy Reconciliation

IMPLEMENTED CHANGE: Renewal checks route state via `ICaddyEdgeClient`, probes the public hostname via `IEdgeCertificateProbe`, and accepts Caddy-observed replacement metadata when identity changes or validity extends.

VALIDATION EVIDENCE: Observed-replacement test passes without provider issuance.

REMAINING FUTURE WORK: Live Caddy storage/route inspection needs staging access.

## 18. Replacement Certificate Strategy

IMPLEMENTED CHANGE: Renewal creates a new `SslCertificate` row with next generation and links old `ReplacementCertificateId`.

VALIDATION EVIDENCE: Test verifies generation 2 active and generation 1 linked after proof.

REMAINING FUTURE WORK: None for schema.

## 19. SNI Activation Proof

IMPLEMENTED CHANGE: Activation remains mandatory through `CertificateActivationService`, which uses `IEdgeCertificateProbe`.

VALIDATION EVIDENCE: DOMAIN-5.5 and DOMAIN-5.6 tests passed.

REMAINING FUTURE WORK: Live public SNI proof unavailable locally.

## 20. Old Generation Retirement

IMPLEMENTED CHANGE: `CertificateActivationService` retires an existing active generation inside the activation transaction only after route acceptance and SNI proof.

VALIDATION EVIDENCE: Renewal test verifies old generation remains active on provider failure and retires only on success.

REMAINING FUTURE WORK: Consider explicit retired enum in a future persistence wave.

## 21. Primary Domain Safety

IMPLEMENTED CHANGE: Activation now admits `Primary` lifecycle and preserves `Primary` instead of demoting via `MarkSslActive`.

VALIDATION EVIDENCE: Source/build validation passed.

REMAINING FUTURE WORK: Add a primary-specific renewal test with real primary fixtures.

## 22. Failure Preservation

IMPLEMENTED CHANGE: Provider, edge, SNI, configuration, rate-limit, and activation failures preserve the old active certificate and domain pointer.

VALIDATION EVIDENCE: Failure test verifies old active certificate and `SslCertificateId` are unchanged.

REMAINING FUTURE WORK: DOMAIN-5.7 owns expiry downgrade/recovery.

## 23. Crash Recovery

IMPLEMENTED CHANGE: Stale operations are reclaimed by lease expiry/fencing and existing operations are reconciled before new work.

VALIDATION EVIDENCE: Source-level claim/reload path and idempotency tests passed.

REMAINING FUTURE WORK: Process-crash simulation against PostgreSQL remains future validation.

## 24. Database Failure Recovery

IMPLEMENTED CHANGE: Provider and activation phases are persisted separately; success events are emitted only after activation success state is saved.

VALIDATION EVIDENCE: Tests cover activation success/failure preservation; no destructive DB validation was run.

REMAINING FUTURE WORK: Forced DB commit failure with real PostgreSQL remains future validation.

## 25. Audit / Events

IMPLEMENTED CHANGE: Added safe events for renewal started, replacement issued, renewal failed, renewed, and generation retired. Added audit actions such as `certificate_renewal_started`, `replacement_issued`, `renewal_deferred`, `renewal_failed`, `renewal_reconciled`, and `old_certificate_retired`.

VALIDATION EVIDENCE: Tests assert event publication and no secrets are included in renewal files.

REMAINING FUTURE WORK: Add outbox transaction assertions if needed.

## 26. Observability

IMPLEMENTED CHANGE: Added `CertificateRenewalTelemetry` meter with required metric names and safe dimensions.

VALIDATION EVIDENCE: Registered in OpenTelemetry meter pipeline.

REMAINING FUTURE WORK: Production dashboards/alerts remain ops work.

## 27. Health / Alerting

IMPLEMENTED CHANGE: Added aggregate `certificate_renewal` health check: healthy for bounded backlog, degraded for backlog over batch, unhealthy for urgent expiry backlog.

VALIDATION EVIDENCE: Health check registered in API.

REMAINING FUTURE WORK: Tune thresholds after observing staging traffic.

## 28. Tenant Isolation

IMPLEMENTED CHANGE: Every candidate and renewal operation carries `TenantId`, `DomainId`, and `CertificateId`; lookups use tenant/domain/certificate correlation.

VALIDATION EVIDENCE: Candidate/service tests and DOMAIN-5.2 tenant isolation regression passed.

REMAINING FUTURE WORK: Real multi-tenant DB contention test remains future work.

## 29. Security

IMPLEMENTED CHANGE: No private key, PEM, PFX, challenge, Caddy credential, raw Caddy config, arbitrary upstream, or arbitrary hostname input added to renewal APIs.

VALIDATION EVIDENCE: Boundary scan over new renewal files returned no forbidden secret/scheduler matches.

REMAINING FUTURE WORK: Continue scanning as DOMAIN-5.8/5.9 add API/UI.

## 30. API Compatibility

IMPLEMENTED CHANGE: No public manual renewal API was added. Future API can call `ICertificateRenewalService`.

VALIDATION EVIDENCE: API build passed.

REMAINING FUTURE WORK: DOMAIN-5.8 owns admin command API.

## 31. Database Safety

IMPLEMENTED CHANGE: No migration, no reset, no truncate, no destructive command, no real DB mutation.

VALIDATION EVIDENCE: Validation used source build and in-memory tests only.

REMAINING FUTURE WORK: Existing DB read-only inspection can be performed when connection/use is authorized.

## 32. Real Hangfire Validation

OBSERVED REALITY: Hangfire storage is PostgreSQL and recurring jobs are registered by `HangfireInitializationHostedService`.

VALIDATION EVIDENCE: Source registration and tests confirm one renewal recurring job.

REMAINING FUTURE WORK: Real Hangfire storage/job execution was not run because no authorized staging DB/job fixture was provided.

## 33. Test Evidence

VALIDATION EVIDENCE:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -p:RestoreIgnoreFailedSources=true`: passed.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~Domain56CertificateRenewalSchedulerTests"`: 5 passed.
- Combined regression filter for DOMAIN-5.6/5.5/5.4/5.3/5.2/DOMAIN-4: 115 passed on rerun.

## 34. Regression Evidence

VALIDATION EVIDENCE: DOMAIN-5.5, DOMAIN-5.4, DOMAIN-5.3, DOMAIN-5.2, and DOMAIN-4 focused tests passed in the combined run.

REMAINING FUTURE WORK: Full solution test suite was not run due scope/time.

## 35. Forbidden Boundary Scan

VALIDATION EVIDENCE:

- No renewal `BackgroundService`, `Timer`, Quartz, private-key, PEM/PFX, challenge-token, or dangerous certificate validator was found in new renewal files.
- Broader repo scan shows pre-existing hosted services unrelated to DOMAIN-5.6.

## 36. Known Observations

OBSERVED REALITY: The frozen `CertificateStatus` enum lacks a distinct retired/superseded value.

IMPLEMENTED CHANGE: Old active generation is preserved during renewal and moved out of `Active` after replacement proof using existing status/linkage fields.

REMAINING FUTURE WORK: Consider a minimal migration for a `Superseded` status only if later waves require clearer reporting.

## 37. Certification

PASS WITH OBSERVATIONS

The DOMAIN-5.6 renewal layer is implemented with one Hangfire scheduler, bounded server-side scans, config-driven policy/backoff/rate limits, `SslCertificateOperation` fencing/idempotency, Caddy/provider reconciliation, mandatory SNI activation, old-generation preservation until replacement proof, safe audit/events/telemetry, aggregate health, tenant isolation, and no private-key boundary violation. Observations are limited to absence of authorized real Hangfire/staging Caddy execution and the existing persistence model's lack of an explicit superseded status.

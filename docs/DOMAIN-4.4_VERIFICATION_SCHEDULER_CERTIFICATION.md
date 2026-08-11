# DOMAIN-4.4 Verification Scheduler Certification

## 1. Executive Summary

DOMAIN-4.4 implements the background orchestration wave for storefront custom-domain ownership verification.

The scheduler performs only:

DISCOVER -> SCHEDULE -> INVOKE -> OBSERVE

It discovers bounded batches of eligible `PendingVerification` domains and invokes the already-certified `IDomainVerificationService`. It does not perform DNS lookups, parse records, compare verification values, transition domain lifecycle state, create verification/audit rows, or publish domain verification events.

Final verdict: PASS WITH OBSERVATIONS

## 2. Existing Hangfire Architecture

Existing source uses Hangfire as the background scheduler:

- Hangfire is registered in `PointOfSale.API/Program.cs` with PostgreSQL storage.
- Recurring jobs are registered by `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs`.
- Registration is resilient: startup probes PostgreSQL and retries registration with exponential backoff.
- The dashboard is registered through `UseHangfireDashboard`.
- Existing jobs use `RecurringJob.AddOrUpdate` and Hangfire job attributes such as `DisableConcurrentExecution`.

DOMAIN-4.4 reuses this architecture and does not introduce a second scheduler.

## 3. Scheduler Design

Implemented files:

- `PointOfSale.API/Configuration/DomainVerificationSchedulerOptions.cs`
- `PointOfSale.API/Services/Storefront/DomainVerificationSchedulerCandidateQuery.cs`
- `PointOfSale.API/Services/Storefront/DomainVerificationSchedulerJob.cs`
- `PointOfSale.API/Instrumentation/DomainVerificationSchedulerTelemetry.cs`
- `PointOfSale.Tests/Domain/DomainVerificationSchedulerTests.cs`

The job is registered as recurring Hangfire job `domain-verification-scheduler`.

## 4. Eligibility Contract

The candidate query requires:

- `Domain.LifecycleState == PendingVerification`
- `Domain.IsDeleted == false`
- persisted `TenantId != Guid.Empty`
- joined tenant exists
- `Tenant.IsActive == true`
- `Tenant.Status == Active`
- `Tenant.DeletedAtUtc == null`
- pending-verification entry time remains inside the configured scheduler window
- no verification attempt within the configured poll interval
- fewer than five failed attempts in the last hour

The scheduler projects only `DomainId`, `TenantId`, and the pending-entry timestamp.

## 5. Polling Window

Configuration is strongly typed under:

```json
DnsVerification:Scheduler
```

Defaults:

- `Enabled: true`
- `PollIntervalSeconds: 10`
- `MaxVerificationWindowMinutes: 5`
- `BatchSize: 50`
- `MaxConcurrentVerifications: 10`

Startup validation enforces positive interval/window/batch/concurrency and `MaxVerificationWindow >= PollInterval`.

## 6. Batch Processing

The scheduler queries bounded batches with `Take(options.BatchSize)`.

It does not load all domains into memory. It loops to the next batch only after a bounded batch is processed and stops when no candidates remain or the final batch is smaller than `BatchSize`.

## 7. Concurrency

The job uses:

- Hangfire `[DisableConcurrentExecution(timeoutInSeconds: 300)]` for recurring-job overlap protection.
- bounded per-run parallelism through `Parallel.ForEachAsync`.
- `IDomainVerificationService` per-domain engine protection for final lifecycle/idempotency authority.

No static scheduler dictionaries, local-only locks, or separate distributed-lock system were introduced.

## 8. Horizontal Scaling

The scheduler relies on Hangfire/PostgreSQL recurring-job coordination and the job-level distributed execution lock. Multiple app instances can register/update the same recurring job idempotently, and overlapping executions are prevented through Hangfire.

## 9. Retry Semantics

Scheduler retry is separate from verification retry.

- Hangfire job infrastructure retry: `[AutomaticRetry(Attempts = 1)]`.
- Domain verification attempt semantics: owned by `DomainVerificationService`.
- Candidate retry cadence/rate eligibility: controlled by scheduler query timing and failed-attempt threshold.

One domain invocation failure is logged and isolated from the batch. Candidate discovery/infrastructure failures are rethrown so Hangfire marks the scheduler execution failed.

## 10. Manual Verification Interaction

Manual verification remains authoritative through `IDomainVerificationService`.

If manual verification succeeds before a scheduler run, the domain is no longer `PendingVerification` and is skipped by discovery. If state changes during a race, the engine re-reads by `DomainId` and `TenantId` and enforces lifecycle/idempotency gates before resolver execution.

## 11. Tenant Isolation

The scheduler never uses HTTP context, request host, current user, or inferred tenant. It processes only persisted `(DomainId, TenantId)` pairs and passes both to `IDomainVerificationService.VerifyAsync`.

## 12. Observability

Structured logs include:

- `JobId`
- scheduler correlation id
- candidate correlation id
- `DomainId`
- `TenantId`
- batch size
- candidates found
- processed/success/failure/skipped counts
- duration
- cancellation state

The scheduler does not log verification tokens, expected values, resolved values, DNS record values, or HTTP verification bodies.

## 13. Metrics

Added an OpenTelemetry meter:

`PointOfSale.DomainVerification.Scheduler`

Metrics:

- `domain_verification_scheduler_runs_total`
- `domain_verification_scheduler_candidates_total`
- `domain_verification_scheduler_processed_total`
- `domain_verification_scheduler_success_total`
- `domain_verification_scheduler_failure_total`
- `domain_verification_scheduler_skipped_total`
- `domain_verification_scheduler_duration`
- `domain_verification_scheduler_active_jobs`

The meter is wired into the existing OpenTelemetry/Prometheus pipeline.

## 14. Configuration

Configuration is in `PointOfSale.API/appsettings.json` under existing `DnsVerification`.

No secrets are required. No resolver configuration was changed except adding the scheduler subsection.

## 15. Performance

Performance controls:

- bounded query batch size
- projection-only candidate reads
- no token loading
- no full `Domains` table materialization
- bounded parallel verification calls
- cancellation-aware EF and engine calls
- no `Thread.Sleep`
- no blocking waits

The query uses existing indexes on lifecycle and tenant/lifecycle. No migration was required for scheduler operation.

## 16. Database Safety

No migration was created.

No database reset, fresh database creation, delete, or data mutation was performed.

Validation notes:

- `dotnet ef migrations list --no-build` listed migrations through `20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics`.
- `dotnet ef migrations has-pending-model-changes --no-build` reported pending model changes. This is broader ApplicationDbContext drift and not introduced by the scheduler, which adds no entity/table/index.

## 17. Tests

Added scheduler coverage for:

- PendingVerification discovery
- Draft ignored
- Verified ignored
- Archived ignored
- Deleted ignored
- Disabled/inactive domains ignored
- inactive tenants ignored
- expired scheduler window ignored
- batch size respected
- polling interval respected
- failed-attempt rate limit respected
- multiple batches processed
- `VerifyAsync` called with `DomainId`/`TenantId`
- cancellation token propagated
- one domain failure does not stop the batch
- infrastructure discovery failure is surfaced
- valid/invalid configuration
- source boundary scan

## 18. Boundary Scan

Scheduler files were scanned for forbidden dependencies and logic. No matches were found for direct DNS clients, resolver calls, tokens, expected/resolved values, lifecycle mutation methods, verification events, controller references, SSL logic, or audit creation.

## 19. Runtime Validation

Validation commands run:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -v:minimal`: PASS
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~DomainVerificationSchedulerTests"`: PASS, 7/7
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~DomainVerificationServiceTests|FullyQualifiedName~DomainWriteServiceTests|FullyQualifiedName~DomainPersistenceTests|FullyQualifiedName~DomainReadServiceTests|FullyQualifiedName~DnsResolverTests|FullyQualifiedName~DnsResolverHealthCheckTests|FullyQualifiedName~DomainVerificationSchedulerTests"`: PASS, 153/153
- `dotnet build PointOfSale.sln --no-restore -v:minimal`: PASS
- `git diff --check`: PASS, line-ending warnings only
- scheduler forbidden-boundary `rg` scan: PASS, no matches

Initial full `dotnet build PointOfSale.sln` attempts without `--no-restore` timed out before a verdict; the subsequent no-restore solution build passed.

## 20. Remaining Observations

- Existing EF model drift remains outside DOMAIN-4.4. The scheduler introduced no database schema and does not justify a migration.
- The checked-in appsettings files have an empty `DefaultConnection`, so no mutating existing-development-database probe was run. Validation stayed read-only/safe.
- Hangfire registration uses a six-field seconds cron for sub-minute cadence, matching the constitutional 10-second polling interval and the installed Hangfire 1.8/Cronos line.
- Existing broader test-project warnings remain unrelated to DOMAIN-4.4.

## 21. Final Certification

PASS WITH OBSERVATIONS

DOMAIN-4.4 is implemented as an orchestration-only scheduler. It reuses Hangfire, preserves tenant isolation, invokes `IDomainVerificationService`, avoids verification business logic, remains bounded and cancellation-aware, records structured operational telemetry, and passes the focused and regression verification suite.

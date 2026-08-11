# DOMAIN-4.3 — Domain Verification Engine Implementation

## Architecture

Implemented the business-logic-only Domain Verification Engine behind `IDomainVerificationService`.

Core implementation:

- `PointOfSale.API/Services/Storefront/IDomainVerificationService.cs`
- `PointOfSale.API/Services/Storefront/DomainVerificationService.cs`
- `PointOfSale.Core/Models/VerificationResult.cs`
- `PointOfSale.Core/Models/DomainVerificationFailureReasons.cs`

Persistence/event support:

- `PointOfSale.Core/Entities/DomainVerification.cs`
- `PointOfSale.Infrastructure/Data/Configurations/DomainVerificationConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics.cs`
- `PointOfSale.Core/Events/Domain/DomainEvents.cs`
- `PointOfSale.API/Program.cs`

The engine consumes only `IDnsResolver` for TXT, CNAME, and HTTPS file lookups. It does not reference `DnsClient.NET`, controllers, Hangfire, schedulers, UI, polling, SSL issuance, or certificate provisioning.

## Business Rules

The engine enforces tenant-scoped verification by loading domains with `DomainId` and `TenantId`. Cross-tenant attempts do not call the resolver and do not create verification artifacts for another tenant.

Verification is rejected for:

- Invalid tenant
- Deleted domains
- Archived domains
- Disabled/inactive domains
- Primary domains
- Already verified domains
- Domains outside `PendingVerification`
- Expired verification tokens
- Missing/unsupported verification methods

Supported methods:

- TXT: resolves `_shopixy-verification.{hostname}` and compares the expected token exactly after trimming whitespace and removing surrounding quotes from the resolved TXT value.
- CNAME: resolves `_shopixy-verification.{hostname}` and compares normalized canonical names.
- HTTP File: resolves `https://{hostname}/.well-known/shopixy-domain-verification.txt` through `IDnsResolver.ResolveHttpFileAsync` and compares the trimmed body to the token.

Future enum values throw `NotSupportedException`.

## State Machine

The engine follows constitutional verification states using existing lifecycle/status primitives:

`PendingVerification` → resolver execution → failed verification record or successful verification → `Verified`

Failure states are represented by `VerificationResult.FailureReason` and `DomainVerification.Status = Failed`.

Success is represented by:

- `VerificationResult.Status = Success`
- `DomainVerification.Status = Success`
- `Domain.MarkVerified()`
- `Domain.LifecycleState = Verified`

Illegal lifecycle states are not resolved and return `LifecycleViolation`.

## Events

Internal events only:

- `DomainVerifiedEvent`
- `DomainVerificationFailedEvent`

Events are published through the existing internal `IEventPublisher`. No external messaging, controller hooks, scheduler integration, or background job behavior was added.

## Audit

Every loaded-domain execution creates:

- `DomainVerification`
- `DomainAuditEntry`

Captured diagnostics include:

- `CorrelationId`
- `LatencyMilliseconds`
- `ResolverEndpoint`
- `FailureReason`
- `Attempt`
- `AttemptedAtUtc`
- `Ttl`
- `ResolvedValue`

Unauthorized cross-tenant/not-found requests fail before artifact creation to avoid leaking domain existence or writing tenant-crossing records.

## Performance

The engine performs exactly one resolver call per eligible execution:

- TXT calls `ResolveTxtAsync` once.
- CNAME calls `ResolveCnameAsync` once.
- HTTP File calls `ResolveHttpFileAsync` once.

No duplicate lookups are performed. Cancellation tokens are passed through database and resolver calls.

## Security

Security controls implemented:

- Tenant ownership enforced in the domain query.
- Empty tenant IDs are rejected.
- Verification tokens are compared only inside the engine.
- TXT comparison remains case-sensitive.
- CNAME comparison normalizes DNS names before comparison.
- HTTP verification is HTTPS-only by construction.
- Replay prevention blocks already verified domains before resolver execution.
- Cross-tenant verification attempts do not create audit or verification rows.

## Tests

Added exhaustive engine coverage in:

- `PointOfSale.Tests/Domain/DomainVerificationServiceTests.cs`

Coverage includes:

- TXT success
- TXT missing
- TXT token mismatch
- CNAME success
- HTTP File success
- Lifecycle violations
- Retry eligibility and rate limiting
- Internal events
- Audit creation
- State transitions
- Tenant isolation
- Replay prevention
- Cancellation behavior
- Concurrency/regression guard
- Source regression that prevents direct `DnsClient.NET`, Hangfire, or controller coupling in the engine

Regression fixes were also made to DNS foundation tests to align them with current `DnsClient.NET` APIs and health-check context behavior.

## Certification

Validation executed:

- `dotnet build PointOfSale.sln` — PASS
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~DomainVerificationServiceTests"` — PASS, 19/19
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~DomainWriteServiceTests|FullyQualifiedName~DomainReadServiceTests|FullyQualifiedName~DomainPersistenceTests|FullyQualifiedName~DnsResolverTests|FullyQualifiedName~DnsResolverHealthCheckTests"` — PASS, 114/114
- `git diff --check` — PASS

## Result

PASS WITH OBSERVATIONS

Observations:

- Existing domain creation currently defaults new domains to `Draft`; the verification engine intentionally accepts only `PendingVerification`, matching the constitutional verification gate.
- Cross-tenant/not-found attempts do not create audit records because creating such records would either leak existence or require writing against a tenant the caller does not own.
- The solution continues to emit pre-existing build warnings unrelated to DOMAIN-4.3.

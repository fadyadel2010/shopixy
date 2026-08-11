# DOMAIN-4.3.1 Verification Lifecycle Entry and Engine Certification

## 1. Executive Summary

DOMAIN-4.3.1 closes the lifecycle-entry gap between domain creation and the existing verification engine.

The final certified lifecycle is:

Draft -> PendingVerification -> Verification Engine -> Verified

CreateDomain remains a Draft-creating command. A new explicit command, StartDomainVerification, is the canonical tenant-owned transition from Draft to PendingVerification. The verification engine then consumes only PendingVerification domains and performs the resolver-backed transition to Verified.

Final verdict: PASS WITH OBSERVATIONS

## 2. Existing Lifecycle Reality

Source reality before this wave:

- Domain creation generated a verification token and default verification method, but persisted new domains in Draft.
- DomainVerificationService accepted only PendingVerification domains.
- There was no aggregate method or command path that moved Draft to PendingVerification.
- DOMAIN-3.1-C.1 and existing command tests certified Draft as the CreateDomain initial state.
- DOMAIN-4.1 described the canonical lifecycle but conflicted with the certified command-path reality by implying CreateDomain could immediately enter PendingVerification.

The practical gap was therefore Draft -> PendingVerification, not the verification engine itself.

## 3. Lifecycle Entry Decision

CreateDomain remains Draft.

Rationale:

- DOMAIN-3.1-C and DOMAIN-3.1-C.1 certify CreateDomain as a command that validates and persists a managed domain in Draft.
- The existing UX has a create step followed by a verification step.
- DOMAIN-4.3 already implemented an engine that is intentionally gated on PendingVerification.
- Silently changing CreateDomain to PendingVerification would change certified command semantics and make verification entry implicit.

Canonical lifecycle entry is now StartDomainVerification. It may be triggered by the tenant-owned merchant/admin command path after a domain has been created.

## 4. Implementation Changes

Implemented changes:

- Added Domain.MarkPendingVerification(method, token).
- Added StartDomainVerificationRequest and Version on DomainMutationResponse.
- Added IDomainWriteService.StartDomainVerificationAsync.
- Added AdminDomainsController PUT /api/admin/storefront/domains/{id}/verification/start.
- Wired the domain management frontend create flow to explicitly start verification after creation.
- Replaced GUID token generation with RandomNumberGenerator-backed 256-bit token generation.
- Redacted verification tokens and resolver values from domain/write and verification audit snapshots.
- Moved verification success/failure event publication until after SaveChanges succeeds.
- Added in-process per-domain verification locking plus DbUpdateConcurrencyException handling.
- Repaired the existing DOMAIN-4.3 migration metadata so EF can discover the already-authored diagnostics migration.
- Added lifecycle-entry, token, tenant-isolation, retry, event, audit, resolver-call, and concurrency tests.

No scheduler, Hangfire jobs, SSL provisioning, certificate provisioning, DNS resolver redesign, or background service code was introduced.

## 5. Token Lifecycle

Certified token behavior:

- Tokens are generated with RandomNumberGenerator.GetBytes(32), encoded as lowercase hex, and prefixed with shopixy-domain-verify-.
- CreateDomain creates the token before the domain can enter PendingVerification.
- StartDomainVerification preserves an existing token for harmless retries or method omissions.
- StartDomainVerification regenerates only when the token is missing or the verification method is explicitly changed before entry.
- Token age is evaluated from the PendingVerification entry timestamp, falling back to domain creation time only when no entry timestamp exists.
- PendingVerification requires a non-empty token through the aggregate method.
- DomainVerificationService rejects missing and expired tokens before resolver execution.
- Verified domains cannot re-enter PendingVerification and cannot be successfully verified again.
- Audit snapshots no longer include VerificationToken, DomainVerification.Token, ExpectedValue, or ResolvedValue.

## 6. Verification Engine Certification

DomainVerificationService remains the sole verification engine:

PendingVerification -> IDnsResolver -> VerificationResult -> DomainVerification -> Domain.MarkVerified() -> Verified

Certified properties:

- One resolver call per execution path that reaches DNS/HTTP resolution.
- Tenant lookup is scoped by tenantId and domainId.
- Lifecycle gate rejects anything other than PendingVerification before resolver execution.
- Token validation runs before resolver execution.
- Expired tokens are rejected before resolver execution.
- Failures create retryable failed DomainVerification attempts.
- Success creates one successful DomainVerification attempt and calls Domain.MarkVerified().
- CancellationToken is propagated through repository, resolver, EF, and event paths.
- There is no direct DNS provider, Hangfire, controller, UI, SSL, or certificate provisioning dependency in the engine.

## 7. State Transition Certification

Certified state transitions:

- Draft -> PendingVerification succeeds only through StartDomainVerification.
- PendingVerification -> PendingVerification is rejected.
- Verified -> PendingVerification is rejected.
- Archived -> PendingVerification is rejected.
- Deleted -> PendingVerification is rejected.
- Disabled/inactive -> PendingVerification is rejected.
- Primary domains are rejected from verification lifecycle entry.
- PendingVerification -> Verified succeeds through DomainVerificationService only after resolver success.
- Failed verification leaves the domain PendingVerification and retryable.

Invalid hostname, reserved hostname, duplicate hostname, cross-tenant access, invalid verification method, inactive domain, archived domain, deleted domain, and primary domain are rejected before lifecycle entry.

## 8. Idempotency Certification

Certified repeated execution behavior:

- PendingVerification plus DNS success produces one effective Verified transition.
- Verified plus verification call again is rejected by the lifecycle gate and does not call the resolver.
- PendingVerification plus DNS failure remains retryable and allows a later attempt according to policy.
- Repeated lifecycle-entry calls after PendingVerification are rejected and do not regenerate tokens.
- Harmless StartDomainVerification retries before entry preserve the existing token unless the method is explicitly changed.

## 9. Concurrency Certification

The engine now uses a per-domain SemaphoreSlim gate to serialize concurrent in-process verification calls for the same domain. The second concurrent call re-reads the domain after the first call commits, observes Verified, and exits through the lifecycle gate without a resolver call or second DomainVerifiedEvent.

EF optimistic concurrency remains active through the existing xmin/version model. DbUpdateConcurrencyException is caught in the verification engine and converted into a failed VerificationResult without publishing a success event.

## 10. Audit Certification

Lifecycle entry audit:

- Action: DomainVerificationStarted.
- Includes DomainId and TenantId.
- Includes actor, timestamp, correlation ID, previous state, and new state through the existing DomainAuditEntry structure.
- Does not include verification tokens.

Verification audit:

- Success action: DomainVerified.
- Failure action: DomainVerificationFailed.
- Includes domain, tenant, method, attempt number, status, failure reason, resolver endpoint, latency, TTL, correlation ID, and state snapshots.
- Does not include verification tokens or resolved/expected verification values in audit payloads.

## 11. Event Certification

Event behavior is certified:

- StartDomainVerification does not publish DomainVerifiedEvent.
- Successful verification publishes DomainVerifiedEvent exactly once, after SaveChanges succeeds.
- Failed verification publishes DomainVerificationFailedEvent according to the engine contract.
- Concurrent successful verification requests produce exactly one DomainVerifiedEvent.

No external messaging architecture was introduced.

## 12. Tenant Isolation Certification

Lifecycle entry and verification are tenant-safe:

- StartDomainVerification loads by tenantId plus domainId through the existing domain repository.
- Cross-tenant lifecycle entry is returned as not found.
- Verification loads by domainId and tenantId.
- DomainVerification and DomainAuditEntry rows carry TenantId where supported by the entity.
- Existing database probe validated the lifecycle using a real tenant from the configured database.

## 13. Boundary Certification

Boundary scan results:

- DomainWriteService and DomainVerificationService do not reference DnsClient.NET or ILookupClient.
- DomainWriteService and DomainVerificationService do not reference Hangfire or BackgroundService.
- DomainWriteService and DomainVerificationService do not depend on controllers or UI code.
- Verification engine does not contain SSL or certificate provisioning logic.
- Existing SSL metadata/delete guards remain outside the lifecycle-entry and verification-engine paths.
- DNS lookup logic remains centralized behind IDnsResolver.
- Token comparison and verification result evaluation remain in DomainVerificationService; lifecycle entry only prepares PendingVerification.

## 14. Existing Database Validation

The configured development database was used. No fresh database was created and no new migration was generated.

Runtime finding:

- The existing database was missing the already-authored DOMAIN-4.3 diagnostics migration columns on DomainVerifications.
- The migration file existed, but EF could not discover it because it lacked migration metadata.
- The existing migration metadata was repaired.
- EF normal database update remained blocked by unrelated pending model changes, so the exact existing DOMAIN-4.3 migration delta was applied idempotently through the validation harness and the EF migration history row was registered.

Probe result against the existing database:

- CreateState=Draft.
- StartState=PendingVerification.
- VerificationStatus=Success.
- PersistedStateBeforeCleanup=Verified.
- CleanupState=Deleted.
- ResolverCalls=1.
- VerificationRows=1.
- HasStartAudit=True.
- HasVerifiedAudit=True.
- VerifiedEvents=1.
- FailedEvents=0.

## 15. Test Results

Validation commands run:

- dotnet build PointOfSale.sln: passed, 0 warnings, 0 errors.
- dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~DomainVerificationServiceTests|FullyQualifiedName~DomainWriteServiceTests": passed earlier in the wave, 59 passed.
- dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~DomainVerificationServiceTests|FullyQualifiedName~DomainWriteServiceTests|FullyQualifiedName~DomainPersistenceTests|FullyQualifiedName~DomainReadServiceTests|FullyQualifiedName~DnsResolverTests|FullyQualifiedName~DnsResolverHealthCheckTests": passed, 146 passed.
- git diff --check: passed; only Git LF-to-CRLF warnings were emitted.
- Source boundary scans: passed for the lifecycle-entry and verification-engine paths.
- Existing database lifecycle probe: passed.

The DNS resolver regression slice exposed a brittle exact TTL assertion. The resolver preserves a positive TTL from the DNS record; the test now asserts the stable contract instead of an exact second boundary.

## 16. Remaining Observations

- Earlier full recompilation emitted pre-existing compiler/analyzer warnings outside DOMAIN-4.3.1 scope; the final incremental solution build completed with 0 warnings.
- EF's normal database update command is blocked by unrelated pending model changes in the broader ApplicationDbContext. DOMAIN-4.3.1 did not generate a new migration to resolve unrelated drift.
- The existing database required application of the already-authored DOMAIN-4.3 diagnostics migration delta before the verification engine could persist attempt diagnostics.
- The existing database validation used a deterministic fake IDnsResolver to certify the lifecycle without relying on external DNS.

## 17. Final Certification

PASS WITH OBSERVATIONS

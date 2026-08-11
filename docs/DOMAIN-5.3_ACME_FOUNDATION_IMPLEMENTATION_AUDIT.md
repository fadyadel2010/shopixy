# DOMAIN-5.3 ACME Foundation Implementation Audit

Audit date: 2026-08-09

Certification: PASS WITH OBSERVATIONS

## 1. Executive Summary

OBSERVED REALITY:
- DOMAIN-5.1 canonically chooses Caddy-managed ACME, HTTP-01 primary, TLS-ALPN-01 fallback, DNS-01 out of scope, and no application database private-key custody.
- DOMAIN-5.2 already froze certificate persistence and `SslCertificateOperation` as the durable operation/idempotency boundary.
- No ACME package, Certes, ACMESharp, BouncyCastle, application ACME client, Caddy API client, or certificate provisioning engine existed before this wave.

IMPLEMENTED CHANGE:
- Added provider-neutral certificate provider contracts in Core.
- Added a Caddy-managed ACME foundation shell in Infrastructure using options, `IHttpClientFactory`, cancellation, safe logging scope, and normalized out-of-scope failures.
- Added a test-only fake provider for contract simulation.
- Added DOMAIN-5.3 tests for account/order/authorization/challenge/error/tenant/security/architecture boundaries.

VALIDATION EVIDENCE:
- `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --no-restore -p:RestoreIgnoreFailedSources=true`: passed, 0 warnings, 0 errors.
- `dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --no-dependencies -p:RestoreIgnoreFailedSources=true`: passed, 204 existing test-project warnings, 0 errors.
- Focused regression: 116 passed, 0 failed.

REMAINING FUTURE WORK:
- DOMAIN-5.4+ must implement admission/provisioning orchestration.
- DOMAIN-5.5 must implement the real Caddy edge/admission integration.

## 2. Pre-Implementation Reality

OBSERVED REALITY:
- Existing persistence: `SslCertificate` and `SslCertificateOperation`.
- Existing provider patterns: payment provider adapters and DNS resolver use abstraction plus Infrastructure implementation.
- Existing HTTP pattern: named `AddHttpClient` registrations.
- Existing secret convention: appsettings carry empty placeholders and comments requiring env/user-secrets/managed secret injection.

IMPLEMENTED CHANGE:
- Reused existing `ProviderType`, `CertificateOperationType`, and `SslCertificateOperation` correlation concepts.
- Added no second idempotency table and no new persistence.

VALIDATION EVIDENCE:
- Static scans found no `DOMAIN_5_3` migration.

REMAINING FUTURE WORK:
- Runtime orchestration must persist provider references using DOMAIN-5.2 fields.

## 3. DOMAIN-5.1 Compliance Matrix

| Requirement | Result | Evidence |
| --- | --- | --- |
| Provider abstraction | PASS | `ICertificateProvider` |
| Business layer no ACME package | PASS | no package refs for Certes/ACMESharp/BouncyCastle |
| Account abstraction | PASS | `AcmeAccountReference`, opaque refs only |
| Order abstraction | PASS | `CertificateOrder` |
| Authorization abstraction | PASS | `CertificateAuthorization` |
| Challenge abstraction | PASS | `CertificateChallenge`, `CertificateChallengeSecret` |
| HTTP-01 support | PASS | `CertificateChallengeType.Http01` |
| TLS-ALPN-01 support | PASS | `CertificateChallengeType.TlsAlpn01` |
| DNS-01 rejected | PASS | validation throws `NotSupportedException` |
| Error normalization | PASS | `CertificateProviderErrorCategory` |
| Tenant/operation mandatory | PASS | `CertificateTenantOperationContext.Validate()` |
| No private-key DB persistence | PASS | no migration, no entity changes |
| No Caddy activation | PASS | provider returns `CERTIFICATE_PROVIDER_EXECUTION_OUT_OF_SCOPE` for execution |

## 4. Provider Boundary

OBSERVED REALITY:
- No canonical certificate provider abstraction existed.

IMPLEMENTED CHANGE:
- Added `PointOfSale.Core/Certificates/CertificateProviderContracts.cs`.
- Added `ICertificateProvider` and `ICertificateProviderAccountSelector`.
- Provider methods cover account, order, authorization, challenge, and provider operation status.

VALIDATION EVIDENCE:
- `Domain53AcmeFoundationTests.ProviderBoundary_IsProviderNeutralAndDoesNotExposeAcmeLibraries`.

REMAINING FUTURE WORK:
- DOMAIN-5.4 orchestration should depend on these Core contracts.

## 5. Account Model

OBSERVED REALITY:
- DOMAIN-5.1 says ACME account key material remains outside the app DB.

IMPLEMENTED CHANGE:
- Added `AcmeAccountReference`, `AcmeAccountRequest`, account registration status, environment, contact email, provider account identifier, and external key custody marker.
- No account key, JWK, PEM, or credential field was added.

VALIDATION EVIDENCE:
- `AccountModel_UsesOpaqueReferencesAndExternalKeyCustody`.

REMAINING FUTURE WORK:
- DOMAIN-5.5 must bind account references to actual Caddy-managed account storage.

## 6. Order Model

IMPLEMENTED CHANGE:
- Added `CertificateIssuanceRequest`, `CertificateOrder`, `CertificateProviderOrderResult`, provider operation metadata, requested identifiers, SANs, authorization references, timestamps, and normalized status.

VALIDATION EVIDENCE:
- `OrderContract_ConstructsSafeProviderOrderWithSanAndIdempotencyCorrelation`.

REMAINING FUTURE WORK:
- Actual order creation is deferred.

## 7. Authorization Model

IMPLEMENTED CHANGE:
- Added `CertificateAuthorization` and `CertificateAuthorizationRequest`.
- Available challenge types and challenge references are normalized and provider-neutral.

VALIDATION EVIDENCE:
- `AuthorizationContract_OffersHttp01AndTlsAlpn01AndRejectsDns01`.

REMAINING FUTURE WORK:
- Provider authorization polling belongs to later execution waves.

## 8. Challenge Model

IMPLEMENTED CHANGE:
- Added `CertificateChallenge`, `CertificateChallengeSecret`, and `CertificateChallengePolicy`.
- Challenge secrets are transient contract fields with redacted `ToString()` and `WithoutSecret()`.
- DNS-01 is explicitly rejected.

VALIDATION EVIDENCE:
- `ChallengeContract_RedactsSecretAndKeepsItTransient`.

REMAINING FUTURE WORK:
- Edge challenge serving is deferred to DOMAIN-5.5.

## 9. Error Normalization

IMPLEMENTED CHANGE:
- Added canonical categories: ConfigurationError, AuthenticationError, AuthorizationDenied, RateLimited, BadNonce, InvalidOrder, ChallengeFailed, DomainRejected, CertificateRejected, NetworkFailure, Timeout, ProviderUnavailable, Conflict, InvalidState, UnknownProviderError.
- Added retryability classifier and safe metadata sanitization.

VALIDATION EVIDENCE:
- `ProviderErrorNormalization_CoversEveryCanonicalCategoryWithRetryability`.

REMAINING FUTURE WORK:
- Concrete provider exception mapping belongs with real provider execution.

## 10. Configuration/Environment Model

IMPLEMENTED CHANGE:
- Added `CertificateProviderOptions`, explicit environment, directory URL, timeout, account reference, contact email, issuer, user agent, and retry-after behavior.
- Added options validator and named `HttpClientFactory` registration.

VALIDATION EVIDENCE:
- `InfrastructureBoundary_UsesHttpClientFactoryOptionsAndDoesNotExecuteCaddyIntegration`.

REMAINING FUTURE WORK:
- Production values must be supplied externally; no secrets were added to appsettings.

## 11. Tenant/Operation Correlation

IMPLEMENTED CHANGE:
- `CertificateTenantOperationContext` requires `TenantId`, `DomainId`, `OperationId`, `OperationType`, `IdempotencyKey`, and `Generation`.
- Infrastructure logging scopes include safe correlation dimensions.

VALIDATION EVIDENCE:
- `CertificateRequest_RejectsMissingTenantDomainOperationAndDnsChallengePolicy`.

REMAINING FUTURE WORK:
- DOMAIN-5.4 must create and own `SslCertificateOperation` claims before provider calls.

## 12. Security/Secret Boundary

IMPLEMENTED CHANGE:
- No private-key, JWK, PEM, PFX, DNS credential, provider credential, challenge table, or account secret persistence was added.
- Provider error sanitization removes sensitive metadata keys and values.

VALIDATION EVIDENCE:
- Secret scan found private-key/challenge-secret terms only in guard-test forbidden lists.
- `SecretLeakageScan_FoundationDoesNotPersistPrivateKeyOrChallengeSecrets`.

REMAINING FUTURE WORK:
- Caddy storage permissions and backup/restore are DOMAIN-5.5/5.7 work.

## 13. Package/Dependency Audit

OBSERVED REALITY:
- Existing Infrastructure packages include `DnsClient`, `Microsoft.Extensions.Http.Polly`, and platform dependencies.

IMPLEMENTED CHANGE:
- No packages were added.
- No ACME library was added because DOMAIN-5.1 chooses Caddy-managed ACME and DOMAIN-5.3 does not execute provisioning.

VALIDATION EVIDENCE:
- Source/package scan found no Certes, ACMESharp, or BouncyCastle package references.

REMAINING FUTURE WORK:
- If a later wave needs a package, it must remain isolated to Infrastructure.

## 14. Test Evidence

VALIDATION EVIDENCE:
- DOMAIN-5.3 class: 14 passed.
- Focused combined regression: 116 passed.
- Tests cover provider boundary, account selection, order/SAN handling, authorization/challenge, DNS-01 rejection, secret redaction, error taxonomy, fake provider scenarios, cancellation, architecture boundary, secret scan, and no migration.

## 15. DOMAIN-5.2 Regression Evidence

VALIDATION EVIDENCE:
- Included `Domain52CertificatePersistenceFreezeTests`; passed in the 116-test run.
- No schema/entity migration was introduced by DOMAIN-5.3.

## 16. DOMAIN-4 Regression Evidence

VALIDATION EVIDENCE:
- Included `DomainPersistenceTests`, `DomainReadServiceTests`, `DomainVerificationServiceTests`, and `DomainVerificationSchedulerTests`; passed in the 116-test run.

## 17. Out-of-Scope Verification

VALIDATION EVIDENCE:
- New foundation files do not call Caddy API, generate Caddyfile, reload Caddy, perform SNI checks, mutate `Domain.SslCertificateId`, transition certificates to Active, add scheduler jobs, or expose frontend commands.

## 18. Known Observations

OBSERVED REALITY:
- A running `PointOfSale.API` process locked the normal API output DLL during one test/build attempt.

VALIDATION EVIDENCE:
- The lock was avoided with `--no-build` for tests and project-scoped builds.

REMAINING FUTURE WORK:
- None blocking.

## 19. Migration Decision

IMPLEMENTED CHANGE:
- No migration was created.

VALIDATION EVIDENCE:
- `MigrationDecision_Domain53DoesNotIntroduceNewPersistence`.
- `rg --files PointOfSale.Infrastructure\Migrations | rg "DOMAIN_5_3|Acme|ACME|Provider"` returned no migration files.

## 20. Certification

PASS WITH OBSERVATIONS

Reason:
- The ACME/provider foundation is implemented and verified without provisioning, activation, Caddy deployment, renewal, revocation, private-key persistence, or business-layer ACME dependency.

Observation:
- Real Caddy edge execution remains intentionally out of scope for DOMAIN-5.3.

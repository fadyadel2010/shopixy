# DOMAIN-4.5 Verification Commands API Certification

## 1. Executive Summary

DOMAIN-4.5 implemented the Storefront Domain verification command/API path for Admin operations.

Implemented commands:

- StartDomainVerification
- VerifyNow
- RetryVerification
- RefreshVerificationToken
- ReadVerification remains compatible through the existing verification read endpoint/service.

Final verdict: PASS WITH OBSERVATIONS.

## 2. Existing API Architecture

Live source uses `AdminDomainsController` under `/api/admin/storefront/domains`.

The API remains thin:

- HTTP and tenant validation in controller.
- Command/read orchestration through `IDomainWriteService` and `IDomainReadService`.
- Verification semantics through `IDomainVerificationService`.
- DNS through the existing `IDnsResolver` inside `DomainVerificationService`.

## 3. Command Inventory

Live command contract:

- `PUT /api/admin/storefront/domains/{id}/verification/start`
- `PUT /api/admin/storefront/domains/{id}/verification/verify`
- `PUT /api/admin/storefront/domains/{id}/verification/retry`
- `PUT /api/admin/storefront/domains/{id}/verification/token`
- Existing read path: verification instructions/status through `DomainReadService.GetDomainVerificationAsync`.

## 4. Start Verification

`StartDomainVerification` reuses `DomainWriteService.StartDomainVerificationAsync`.

Confirmed behavior:

- Tenant-scoped domain lookup.
- Draft to PendingVerification transition.
- Existing token generation path.
- Existing audit/event boundary in command service.
- Response maps to `DomainVerificationCommandResponse` plus current verification instructions.

## 5. Verify Now

`VerifyNow` delegates to `DomainWriteService.VerifyDomainNowAsync`, which delegates to `IDomainVerificationService.VerifyAsync`.

The controller does not know TXT, CNAME, or HTTP semantics and does not parse resolver output.

## 6. Retry

`RetryVerification` delegates to the same verification engine.

Attempt policy remains engine-owned. A rate-limited request now returns `RateLimited` before resolver invocation and before appending another verification attempt row.

## 7. Token Refresh Decision

Token refresh is constitutionally permitted before ownership verification succeeds.

Implemented safeguards:

- Forbidden after Verified, SslPending, SslActive, Primary, Disabled, Archived, or Deleted.
- Allowed only in Draft or PendingVerification.
- Cryptographically secure token generation through existing RNG helper.
- Does not reset attempts or lifecycle.
- Existing audit snapshot serialization redacts token values.
- Service-level refresh rate limit: 5 refreshes per domain per tenant per hour.

## 8. Authorization

Endpoints remain protected by the existing Admin controller authorization and tenant validation conventions.

Tenant ownership is enforced by persisted tenant-scoped domain lookups, not by route ID alone.

## 9. IDOR Protection

Cross-tenant access fails closed through tenant-scoped repository/query predicates.

Covered service-level cases:

- Start wrong tenant throws not found.
- Verify/retry wrong tenant throws not found from engine.
- Refresh wrong tenant uses tenant-scoped `FindByIdAsync` and throws not found.

## 10. Rate Limiting

Verify/retry:

- `DomainVerificationService` checks failed attempts in the last hour before DNS/HTTP resolver work.
- `RateLimited` returns deterministic command output and maps to HTTP 429.

Refresh:

- `DomainWriteService` limits token refresh using recent `DomainVerificationTokenRefreshed` audit entries.
- HTTP maps `RateLimitExceeded` to 429.

## 11. Idempotency

Engine-level per-domain semaphore remains the authority for concurrent verification attempts.

Duplicate successful VerifyNow cannot create duplicate successful verification transitions because a second attempt sees the changed lifecycle and fails with `LifecycleViolation`.

Double-submit UI protection was added through explicit mutation state and disabled command buttons.

## 12. HTTP Contract

Mapped responses:

- 200: successful command or accepted mutation result.
- 400: invalid command.
- 401: missing tenant/auth context by existing convention.
- 404: inaccessible or missing domain.
- 409: lifecycle/concurrency conflict.
- 422: expected verification failure.
- 429: rate limited.
- 503: DNS/HTTP infrastructure-style failures.
- 500: unexpected application failure.

## 13. Result DTOs

Added canonical command DTOs in `StorefrontDtos.cs`:

- `VerifyDomainNowRequest`
- `RetryDomainVerificationRequest`
- `RefreshDomainVerificationTokenRequest`
- `DomainVerificationCommandResponse`

The response excludes raw resolver payloads, stack traces, and internal diagnostics.

## 14. Error Mapping

Domain exceptions map through the existing controller error response convention.

Expected verification failures remain distinct from infrastructure failures:

- Domain failures: 409, 422, or 429.
- Resolver/network/timeout failures: 503.
- Unexpected exceptions: 500 with safe `ApiErrorResponse.InternalError`.

## 15. Audit/Event Boundaries

Controller creates no audit rows and publishes no verification events.

Audit/event boundaries remain:

- `DomainWriteService` for command mutations and token refresh.
- `DomainVerificationService` for verification attempts, verified transitions, failed attempts, and verification events.

## 16. Frontend Integration

Updated storefront domain UI:

- `domainManagementService.js` now exposes verify, retry, and refresh-token commands.
- `useDomainMutations.js` exposes explicit mutation states and command functions.
- `DnsVerificationPage` wires Verify Now, Retry Verification, Refresh Token, expected failure, infrastructure error, disabled states, and double-submit protection.
- `DomainDetailPage` wires Start Verification.
- `DomainListPage` adds Start Verification row action.
- `AddDomainWizardPage` prevents duplicate create submission.

No raw fetch bypass was introduced.

## 17. Cache Invalidation

Existing hook refresh behavior is reused after mutations.

Affected domain lists/details are refreshed through the shared hook/service path rather than full application reload or a second cache layer.

## 18. Security

Boundary scan results:

- Controller forbidden logic scan: clean.
- Frontend storage/telemetry token persistence scan: clean.

Verification tokens are only returned through the authorized verification-instructions contract and are not written to browser storage or telemetry by the domain module.

## 19. Database Safety

No database reset, deletion, migration creation, or schema recreation was performed.

Inspection commands:

- `dotnet ef migrations list ... --no-build`: passed and listed the existing domain migrations through `20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics`.
- `dotnet ef migrations has-pending-model-changes ... --no-build`: reported pending model changes.

Because pending model drift already exists in the broader `ApplicationDbContext`, no DOMAIN-4.5 migration was created.

## 20. Tests

Backend:

- `DomainVerificationServiceTests`
- `DomainVerificationSchedulerTests`
- `DomainWriteServiceTests`
- `DomainPersistenceTests`
- `DomainReadServiceTests`
- `DnsResolverTests`
- `DnsResolverHealthCheckTests`

Frontend:

- Storefront domain module tests.
- Storefront information architecture tests.
- Domain management mutation service tests.
- Domain mutation hook tests.

## 21. Integration Validation

Commands run:

- `dotnet build PointOfSale.sln --no-restore -v:minimal`: passed, 0 warnings, 0 errors.
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-build --filter "...domain/dns suites..."`: passed, 158 tests.
- `npm test -- --run --pool=threads src/modules/storefront/domains src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.test.tsx`: passed, 100 tests.
- `npm run build`: passed.
- `git diff --check`: passed with line-ending warnings only.

Observation: no reusable `WebApplicationFactory`/TestServer Admin Domains HTTP integration fixture was present in the test project. DOMAIN-4.5 adds service and frontend mutation coverage, but not a real HTTP integration fixture.

## 22. Boundary Scan

Controller scan used:

`IDnsResolver|DnsClient|ILookupClient|ExpectedValue|ResolvedValue|MarkVerified|MarkPendingVerification|new\s+DomainVerification\b|DomainAuditEntry|DomainVerifiedEvent|DomainVerificationFailedEvent`

Result: no matches.

Frontend persistence scan used:

`localStorage|sessionStorage|analytics|telemetry`

Result: no matches in `PointOfSale.Web/src/modules/storefront/domains`.

## 23. Remaining Observations

1. Existing EF model drift remains outside this DOMAIN-4.5 implementation. It was inspected and left untouched to preserve database safety.
2. Real HTTP API integration testing infrastructure for Admin Domains was not present. The command path is covered through service tests and frontend service/hook tests, with controller boundary verified by source scan.

## 24. Final Certification

DOMAIN-4.5 command/API path is implemented and verified against the live source architecture.

Final verdict: PASS WITH OBSERVATIONS.

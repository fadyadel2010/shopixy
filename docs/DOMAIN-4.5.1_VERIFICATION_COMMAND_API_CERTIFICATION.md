# DOMAIN-4.5.1 Verification Command/API Certification

## 1. Executive Summary

Verdict: PASS WITH OBSERVATIONS.

DOMAIN-4.5 command/API hardening is complete for the live source. The missing real HTTP integration coverage was implemented, token exposure was narrowed, per-domain start/refresh command synchronization was added at the application-service boundary, and backend/frontend verification passed.

## 2. Certification Scope

Certified path: HTTP authentication and authorization, tenant context, `AdminDomainsController`, `DomainWriteService`, `DomainReadService`, `DomainVerificationService`, `IDnsResolver`, lifecycle persistence, audit/event boundaries, response DTOs, frontend mutation service/hook/pages.

## 3. Source Reality

Live routes are `PUT /api/admin/storefront/domains/{id}/verification/start`, `/verify`, `/retry`, `/token`, and `GET /verification`: `PointOfSale.API/Controllers/AdminDomainsController.cs:582`, `:588`, `:662`, `:666`, `:687`, `:691`, `:712`, `:716`.

## 4. HTTP Integration Fixture

Added real loopback ASP.NET integration fixture in `PointOfSale.Tests/Domain/AdminDomainsHttpIntegrationTests.cs`. It drives `HttpClient -> ASP.NET auth/authorization -> AdminDomainsController -> DomainWriteService/DomainReadService -> DomainVerificationService -> deterministic IDnsResolver`.

Evidence tests: `StartVerification_ThroughRealHttp_RequiresAuthenticationAndRole` at line 36, `VerificationCommands_ThroughRealHttp_MutateOnlyAuthorizedTenant` at line 57.

## 5. Authorization

HTTP tests prove unauthenticated start returns 401 and insufficient role returns 403: `AdminDomainsHttpIntegrationTests.cs:36`.

Controller validates tenant context with `ValidateTenant`: `AdminDomainsController.cs:1257`.

## 6. IDOR

Tenant A access to Tenant B read/start/verify/retry/refresh returns 404 and response body does not include Tenant B hostname or token: `EveryVerificationRoute_FailsClosedForCrossTenantAccess`, `AdminDomainsHttpIntegrationTests.cs:107`.

Service-level tenant scoping remains via tenant/domain repository queries and existing 165-test domain regression run.

## 7. Start Verification

Real HTTP start verifies Draft -> PendingVerification, token persistence, audit creation, and authorized instruction response: `AdminDomainsHttpIntegrationTests.cs:57`.

Start concurrency is serialized in `DomainWriteService`: lock field and acquisition at `DomainWriteService.cs:40`, `:451`, release at `:564`.

## 8. Verify Now

Success path verifies exactly one resolver call, one `DomainVerifiedEvent`, one successful verification row, and Verified lifecycle: `AdminDomainsHttpIntegrationTests.cs:57`.

Failure and infrastructure mapping are verified by `VerifyNow_MapsExpectedFailureInfrastructureFailureAndRateLimit`: `AdminDomainsHttpIntegrationTests.cs:142`.

## 9. Retry

Retry uses the same certified engine boundary, not controller-side attempt mutation: controller delegates at `AdminDomainsController.cs:691`, `:702`; write service delegates to `IDomainVerificationService`.

Verified retry returns 409 through real HTTP in `VerificationCommands_ThroughRealHttp_MutateOnlyAuthorizedTenant`.

## 10. Token Refresh

Token refresh is verified through HTTP for uniqueness, no lifecycle reset, no attempt reset, audit creation, and 429 rate limit: `RefreshVerificationToken_ThroughRealHttp_RateLimitsAndDoesNotLeakFromDetailDto`, `AdminDomainsHttpIntegrationTests.cs:198`.

Refresh concurrency is serialized in `DomainWriteService`: `DomainWriteService.cs:631`, release at `:749`.

## 11. Rate Limiting

VerifyNow rate limit returns 429 before resolver work: `AdminDomainsHttpIntegrationTests.cs:142`.

Refresh token rate limit maps `DomainOperationErrorCode.RateLimitExceeded` to 429: `AdminDomainsController.cs:1299`, `:1316`, with HTTP proof at `AdminDomainsHttpIntegrationTests.cs:198`.

## 12. Idempotency

Concurrent VerifyNow produces one success, one conflict, one resolver invocation, one success event, and one success row: `ConcurrentVerifyNow_ProducesOneSuccessOneEventAndOneResolverInvocation`, `AdminDomainsHttpIntegrationTests.cs:234`.

Concurrent Start produces one pending transition and one start audit: `ConcurrentStartRequests_AreSerializedAndCreateOnePendingTransition`, line 262.

## 13. Concurrency

Verify/retry remain engine-owned via `DomainVerificationService`. Start/refresh now use per-domain semaphores in `DomainWriteService` without controller locks: `DomainWriteService.cs:40`, `:1388`, `:1391`.

## 14. Error Contract

Verification result status mapping is explicit: success 200, rate limit 429, lifecycle/expired/unsupported 409, infrastructure 503, expected verification failures 422: `AdminDomainsController.cs:1212`.

Domain operation mapping covers tenant required 401, duplicate/conflict 409, rate limit 429, and validation 400: `AdminDomainsController.cs:1299`.

## 15. Token Security

Fixed generic DTO token surface: removed `VerificationToken` from `DomainDetailDto`. Current token DTO exposure is limited to `DomainVerificationDto`: `StorefrontDtos.cs:797`, `:823`, `:828`.

`DomainReadService` only maps token into verification instructions: `DomainReadService.cs:286`, `:319`, `:324`, `:327`.

Audit snapshots omit verification tokens: `DomainWriteService.cs:1431`, `DomainVerificationService.cs:664`.

Frontend scan found no `localStorage`, `sessionStorage`, or raw `fetch(` in `PointOfSale.Web/src/modules/storefront/domains`.

## 16. Audit/Event Boundaries

Controller boundary scan found no direct DNS, lifecycle mutation, audit creation, or event publication in `AdminDomainsController`.

Verification events are created inside `DomainVerificationService` and published after `SaveChangesAsync`: `DomainVerificationService.cs:214`, `:229`, `:247`, `:270`.

## 17. Tenant Isolation

Read, start, verify, retry, refresh cross-tenant HTTP access fails closed with 404 and no token/hostname leak: `AdminDomainsHttpIntegrationTests.cs:107`.

Domain read/write services continue using tenant ID in domain lookup paths.

## 18. Frontend Mutation Certification

`domainManagementService.js` uses the controller PUT route contract for start/verify/retry/refresh: lines 170, 187, 204, 221.

`useDomainMutations.js` distinguishes 422/429 domain failures from 5xx infrastructure failures: lines 110 and 114.

`DnsVerificationPage.jsx` refreshes reads after mutation success and disables command buttons while mutating: lines 41, 298, 308, 317.

## 19. API Contract Compatibility

Backend and frontend both use PUT for all four verification commands. No `{domainId}`/`{id}` mismatch was found in the domain service.

## 20. Existing Database Validation

Safe EF checks used the existing configured development database only. No database was created, reset, dropped, truncated, or migrated.

`dotnet ef migrations list --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext --no-build` succeeded and listed:

- `20260723214642_Baseline_Recovery3`
- `20260726153652_PosCartRuntime`
- `20260726172030_PlatformCurrencyReferenceSeed`
- `20260729120000_StorefrontProductUnitDbEnforcement`
- `20260729150000_ProductAuthoringXminConcurrency`
- `20260805215917_DOMAIN_2C_0_CreateDomainTables`
- `20260805215918_DOMAIN_2C_DomainPersistence`
- `20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics`

`dotnet ef migrations has-pending-model-changes ... --no-build` returned: “Changes have been made to the model since the last migration. Add a new migration.”

Because model drift exists, the Draft -> PendingVerification -> Verified mutating probe was not run against the existing database. `psql` was unavailable, so read-only row counts could not be collected without adding tooling. No DB data was modified.

## 21. Test Matrix

- `dotnet build PointOfSale.sln --no-restore -v:minimal`: passed, 0 errors.
- New HTTP suite: `dotnet test ... --filter FullyQualifiedName~AdminDomainsHttpIntegrationTests`: passed, 7/7.
- Required backend domain regression filter: passed, 165/165.
- Frontend domain tests: `npm test -- --run src/modules/storefront/domains`: passed, 61/61.
- Frontend build: `npm run build`: passed.
- `git diff --check`: passed with line-ending warnings only.

## 22. Boundary Scan

Controller forbidden scan for DNS, resolver, TXT/CNAME parsing, lifecycle mutation, audit, and event terms returned no matches.

DNS provider scan showed provider calls only in `DnsClientResolver`, health check, DI registration, `IDnsResolver`, and the verification engine’s abstraction calls.

## 23. Issues Found

1. DOMAIN-4.5 lacked real HTTP command/API integration coverage.
2. Generic `DomainDetailDto` still advertised a token-shaped field, even though it was nulled.
3. Start/refresh verification command mutations were not explicitly per-domain serialized at the write-service boundary.

## 24. Issues Fixed

1. Added `AdminDomainsHttpIntegrationTests.cs` with live ASP.NET pipeline coverage.
2. Removed `VerificationToken` from `DomainDetailDto` and from the detail mapper.
3. Added per-domain verification command locks to `DomainWriteService` for StartVerification and RefreshVerificationToken.

## 25. Remaining Observations

Existing database mutating lifecycle probe was intentionally skipped because EF reports pending model changes. This is a non-blocking certification observation because command/API behavior is covered by real HTTP integration tests and no existing data was altered.

The workspace contains many pre-existing modified/untracked storefront/domain files beyond this pass; they were not reverted.

## 26. Final Certification

DOMAIN-4.5.1 is certified PASS WITH OBSERVATIONS for verification commands and APIs. The real HTTP path, authorization, tenant isolation, command delegation, verification engine boundary, resolver abstraction, lifecycle transitions, audit/event boundaries, frontend mutation contract, rate limits, error mapping, token security, and concurrency gates passed with source and test evidence above.

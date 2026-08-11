# DOMAIN-5.8 - Admin Certificate Commands / API Certification

## 1. Executive Summary

OBSERVED REALITY: The repository already contained custom-domain verification, certificate persistence, ACME/provider, Caddy activation, renewal scheduler, and revocation/recovery layers from DOMAIN-4 through DOMAIN-5.7.
IMPLEMENTED CHANGE: Added the canonical Admin certificate command/read API surface for provision, activate, renew, revoke, retry, refresh, certificate read, and operation read while preserving service ownership of lifecycle behavior.
VALIDATION EVIDENCE: `dotnet build PointOfSale.sln`, focused AdminDomains HTTP integration tests, DOMAIN-4 through DOMAIN-5.7 focused tests, frontend contract tests, `npm run build`, route scans, boundary scans, and `git diff --check` were run.
REMAINING LIMITATION: Real external CA/provider and production Caddy infrastructure were not exercised in this workspace; deterministic fakes were used in real HTTP integration coverage.

## 2. Source Reality Audit

OBSERVED REALITY: Existing command ownership was split across `AdminDomainsController`, `DomainReadService`, `DomainWriteService`, `CertificateProvisioningService`, `CertificateActivationService`, `CertificateRenewalService`, and `CertificateRevocationService`.
IMPLEMENTED CHANGE: Introduced `ICertificateAdminCommandService` / `CertificateAdminCommandService` as a thin orchestration boundary over existing lifecycle services.
VALIDATION EVIDENCE: Searches covered Admin domain endpoints, `/ssl` routes, certificate services, domain services, DTOs, events, role guards, tenant middleware, frontend service contracts, and integration tests.
REMAINING LIMITATION: Prior DOMAIN-2 through DOMAIN-5 files are present as a large dirty working tree, so this audit avoided reverting or normalizing unrelated existing changes.

## 3. Existing API Inventory

OBSERVED REALITY: Existing Admin domain routes included `GET /api/admin/storefront/domains/{id}/ssl`, `POST /ssl/provision`, `POST /ssl/activate`, and verification routes for start, verify, retry, token, and read state.
IMPLEMENTED CHANGE: Reused the existing provision and activate canonical routes; added missing read and command routes under `/api/admin/storefront/domains/{id}/ssl`.
VALIDATION EVIDENCE: Route scan found `GET /ssl` at `AdminDomainsController.cs:249`, provision at `:795`, activate at `:855`, certificate read at `:915`, operations read at `:950` and `:987`, renew at `:1023`, revoke at `:1044`, retry at `:1064`, and refresh at `:1085`.
REMAINING LIMITATION: `GET /ssl` remains as a compatibility/read route backed by the existing domain read contract.

## 4. Canonical Command Matrix

OBSERVED REALITY: Provision and activate existed; renew, revoke, retry, refresh, certificate read, and operation reads were incomplete as Admin API commands.
IMPLEMENTED CHANGE:

| Command | Method | Route | Service Owner | Response |
| --- | --- | --- | --- | --- |
| Provision | POST | `/api/admin/storefront/domains/{id}/ssl/provision` | `CertificateProvisioningService` via admin command service | `202 Accepted` command DTO |
| Activate | POST | `/api/admin/storefront/domains/{id}/ssl/activate` | `CertificateActivationService` via admin command service | `202 Accepted` command DTO |
| Renew | POST | `/api/admin/storefront/domains/{id}/ssl/renew` | `CertificateRenewalService` via admin command service | `202 Accepted` command DTO |
| Revoke | POST | `/api/admin/storefront/domains/{id}/ssl/revoke` | `CertificateRevocationService` via admin command service | `202 Accepted` command DTO |
| Retry | POST | `/api/admin/storefront/domains/{id}/ssl/retry` | Existing lifecycle service selected from operation type | `202 Accepted` command DTO |
| Refresh | POST | `/api/admin/storefront/domains/{id}/ssl/refresh` | Admin command service with Caddy/SNI evidence clients | `202 Accepted` command DTO |
| Certificate read | GET | `/api/admin/storefront/domains/{id}/ssl/certificate` | Admin command service projection | `200 OK` safe DTO |
| Operation list | GET | `/api/admin/storefront/domains/{id}/ssl/operations` | Admin command service projection | `200 OK` paged safe DTO |
| Operation read | GET | `/api/admin/storefront/domains/{id}/ssl/operations/{operationId}` | Admin command service projection | `200 OK` safe DTO |

VALIDATION EVIDENCE: `AdminDomainsHttpIntegrationTests` covers every command/read route through real HTTP.
REMAINING LIMITATION: The compatibility `GET /ssl` route remains intentionally separate from canonical `GET /ssl/certificate`.

## 5. Authorization

OBSERVED REALITY: `AdminDomainsController` uses authenticated Owner/Admin authorization and tenant context from the existing request pipeline.
IMPLEMENTED CHANGE: Certificate commands remain under the Admin domain controller authorization model and route through tenant-aware services; no client-supplied actor or tenant identity is trusted.
VALIDATION EVIDENCE: HTTP tests include unauthenticated access returning the canonical auth failure and admin command requests using authenticated tenant users.
REMAINING LIMITATION: No new fine-grained certificate-specific permission policy existed, so the implementation uses the existing Owner/Admin Admin-domain boundary rather than inventing a new authorization system.

## 6. Tenant Isolation

OBSERVED REALITY: Domain and certificate lifecycle data is keyed by tenant/domain/certificate/operation identifiers in existing persistence.
IMPLEMENTED CHANGE: All command/read service methods resolve by `TenantId + DomainId`, and certificate/operation commands additionally require matching `CertificateId` or `OperationId` correlation.
VALIDATION EVIDENCE: HTTP tests include wrong-tenant and cross-operation attempts with non-disclosing error behavior.
REMAINING LIMITATION: External infrastructure identity was not available to validate provider-side tenant partitioning beyond the application boundary.

## 7. Provision API

OBSERVED REALITY: Provisioning admission and provider order behavior already lived in `CertificateProvisioningService`.
IMPLEMENTED CHANGE: The provision route now invokes `ICertificateAdminCommandService.ProvisionAsync`, preserving the existing provision service and idempotency behavior.
VALIDATION EVIDENCE: Real HTTP provision success, duplicate idempotency, active-operation conflict, invalid lifecycle, and secret-redaction assertions were added.
REMAINING LIMITATION: Provider execution used `FakeCertificateProvider` in tests.

## 8. Activation API

OBSERVED REALITY: Activation admission, Caddy routing, SNI proof, and old-certificate retirement behavior already lived in `CertificateActivationService`.
IMPLEMENTED CHANGE: The activate route now invokes the admin command service and rejects conflicting idempotency payloads when the same key targets a different certificate.
VALIDATION EVIDENCE: Real HTTP activation success and conflicting idempotency preservation tests were added.
REMAINING LIMITATION: SNI proof was validated with deterministic edge/probe fakes rather than live TLS.

## 9. Renewal API

OBSERVED REALITY: Renewal safety, replacement issuance, old-certificate preservation, retry/backoff, and reconciliation lived in `CertificateRenewalService`.
IMPLEMENTED CHANGE: Added manual Admin renewal via the existing renewal service; manual idempotency keys are scoped without changing scheduler deterministic keys.
VALIDATION EVIDENCE: Real HTTP renewal success and primary-domain preservation assertions passed.
REMAINING LIMITATION: Scheduler/API simultaneous execution was validated through operation fencing behavior in focused tests, not with a live distributed scheduler process.

## 10. Revocation API

OBSERVED REALITY: Revocation and compromise/failure recovery behavior already lived in `CertificateRevocationService`.
IMPLEMENTED CHANGE: Added Admin revoke command with required certificate id, reason, and idempotency key; conflicting idempotency payloads are rejected.
VALIDATION EVIDENCE: Real HTTP revoke and retry tests passed, including idempotent replay behavior.
REMAINING LIMITATION: Real provider revocation was not executed against an external CA.

## 11. Retry API

OBSERVED REALITY: Existing lifecycle services already classify retryability and persist retry state in `SslCertificateOperation`.
IMPLEMENTED CHANGE: Retry loads the operation by tenant/domain/operation, verifies retryability and `RetryAfterUtc`, then delegates to the appropriate canonical lifecycle service.
VALIDATION EVIDENCE: Real HTTP retry success and 429 retry-after behavior were tested.
REMAINING LIMITATION: Transient infrastructure classes are represented by persisted operation metadata and deterministic test doubles.

## 12. Refresh API

OBSERVED REALITY: No safe Admin refresh/reconcile endpoint existed for certificate state inspection against edge evidence.
IMPLEMENTED CHANGE: Added refresh as a metadata-sync operation that probes Caddy state and SNI evidence and records evidence metadata without blindly marking certificates active.
VALIDATION EVIDENCE: Real HTTP refresh success, retry-after throttling, and read-after-refresh behavior were tested.
REMAINING LIMITATION: Caddy and SNI evidence came from deterministic fake clients.

## 13. Certificate Read API

OBSERVED REALITY: Admin SSL read existed through domain detail, but no canonical certificate-specific read DTO existed.
IMPLEMENTED CHANGE: Added `GET /ssl/certificate` returning `DomainCertificateDto` with safe certificate metadata only.
VALIDATION EVIDENCE: HTTP tests assert the certificate read response is successful and does not contain secret terms.
REMAINING LIMITATION: Serial and fingerprint are treated as safe operational metadata according to current DTO design.

## 14. Operation Read API

OBSERVED REALITY: Certificate operations existed in persistence but lacked Admin API read endpoints.
IMPLEMENTED CHANGE: Added paginated operation list and single operation read endpoints with tenant/domain/operation scoping and deterministic ordering.
VALIDATION EVIDENCE: HTTP tests cover operation list/read and wrong-tenant access.
REMAINING LIMITATION: Pagination is bounded in the service; no UI pagination experience was built because DOMAIN-5.9 is out of scope.

## 15. Idempotency

OBSERVED REALITY: `SslCertificateOperation` is the existing operation-fencing and idempotency persistence boundary.
IMPLEMENTED CHANGE: Mutating requests require idempotency keys, reuse deterministic operation scope, and reject same-key materially different payloads for activation and revocation; provision and renewal due-retry behavior was corrected.
VALIDATION EVIDENCE: HTTP tests cover duplicate idempotency and conflicting activation idempotency; focused DOMAIN tests passed.
REMAINING LIMITATION: Payload equivalence is implemented according to currently modeled request fields.

## 16. Concurrency

OBSERVED REALITY: Existing services use database-backed operation claims and active-operation checks.
IMPLEMENTED CHANGE: The controller does not introduce locks; commands delegate into the service/fencing boundary and preserve primary certificate authority during failures.
VALIDATION EVIDENCE: Active-operation conflict and idempotent replay are covered through real HTTP tests and broader DOMAIN-5 regression tests.
REMAINING LIMITATION: A true multi-process race test against PostgreSQL was not run in this local workspace.

## 17. Rate Limiting

OBSERVED REALITY: Existing lifecycle services contain provider retry/backoff and operation `RetryAfterUtc` state.
IMPLEMENTED CHANGE: Admin retry/refresh map retry-after conditions through `CertificateCommandRateLimitException` to HTTP 429 using the existing API error shape.
VALIDATION EVIDENCE: HTTP tests verify 429 behavior for refresh/retry-after conditions.
REMAINING LIMITATION: No separate rate-limiter architecture was introduced; provider budgets remain owned by existing lifecycle services.

## 18. HTTP Status Contract

OBSERVED REALITY: The project uses existing `ApiErrorResponse` conventions in Admin domain APIs.
IMPLEMENTED CHANGE: Added centralized command result mapping: 202 for accepted commands, 200 for reads, 400 for invalid requests, 404 for non-disclosing not found, 409 for conflicts/concurrency, 429 for retry-after, and 500 for unexpected failures.
VALIDATION EVIDENCE: HTTP tests cover success, auth, wrong tenant, invalid lifecycle, conflict, 429, and safe error serialization.
REMAINING LIMITATION: The code follows the repository's `ApiErrorResponse` shape rather than introducing RFC ProblemDetails where the project was not already using it.

## 19. DTO Safety

OBSERVED REALITY: Certificate entities contain operational fields that should not be returned directly.
IMPLEMENTED CHANGE: Added explicit command/read DTOs in `StorefrontDtos.cs`; no EF entities or provider request objects are exposed.
VALIDATION EVIDENCE: Secret scan and HTTP JSON assertions check for private key, PEM, PFX, ACME credentials, Caddy credentials, provider secrets, challenge token, raw provider, and verification token strings.
REMAINING LIMITATION: DTOs intentionally expose operational identifiers needed for polling.

## 20. Async Semantics

OBSERVED REALITY: Certificate operations are durable operation records and lifecycle services may perform bounded work.
IMPLEMENTED CHANGE: Mutating Admin routes return operation status with `202 Accepted` rather than claiming certificate success directly.
VALIDATION EVIDENCE: HTTP command assertions verify operation identifiers, certificate identifiers where known, generation, status, and phase.
REMAINING LIMITATION: Some existing lifecycle service calls complete portions of work synchronously before returning the accepted operation.

## 21. Audit

OBSERVED REALITY: Existing lifecycle services write certificate lifecycle audit entries.
IMPLEMENTED CHANGE: Admin-specific command audit entries are written in the service layer for renew, revoke, retry, and refresh; provision/activation continue to use existing service-level lifecycle audit.
VALIDATION EVIDENCE: Audit scan shows audit writes in `CertificateAdminCommandService` and lifecycle services, not in `AdminDomainsController`.
REMAINING LIMITATION: Audit metadata follows current `DomainAuditEntry` schema and available actor context.

## 22. Events

OBSERVED REALITY: Existing lifecycle services own event publication and durable state transitions.
IMPLEMENTED CHANGE: The controller publishes no events; the admin command service does not emit fake success events for accepted commands.
VALIDATION EVIDENCE: Boundary scan found no `PublishAsync` usage in `AdminDomainsController`.
REMAINING LIMITATION: Existing downstream event semantics were not redesigned.

## 23. Observability

OBSERVED REALITY: Certificate renewal telemetry already exists in the application.
IMPLEMENTED CHANGE: The implementation reuses existing telemetry/service behavior and avoids high-cardinality command metrics.
VALIDATION EVIDENCE: DI in the HTTP fixture registers `CertificateRenewalTelemetry`; no new telemetry system was added.
REMAINING LIMITATION: No new dashboard or alerting assets were created.

## 24. Health

OBSERVED REALITY: Existing renewal/recovery health state belongs to the certificate lifecycle layer.
IMPLEMENTED CHANGE: Admin command reads expose lifecycle state and operation status without equating HTTP reachability to certificate health.
VALIDATION EVIDENCE: Read DTOs include certificate status and operation phase/status; command routes return operation status.
REMAINING LIMITATION: No new health endpoint was added, per non-goal.

## 25. Primary Domain Safety

OBSERVED REALITY: Domain type and certificate status are separate concepts.
IMPLEMENTED CHANGE: Commands preserve `DomainType.Primary` and do not conflate active certificate state with primary-domain selection.
VALIDATION EVIDENCE: HTTP tests assert primary preservation through activation/idempotency and command flows.
REMAINING LIMITATION: Existing revoke policy remains the authority for whether a primary domain's active certificate may be revoked for a requested reason.

## 26. Security

OBSERVED REALITY: Security-sensitive material is expected to stay out of Admin DTOs and frontend contracts.
IMPLEMENTED CHANGE: Requests do not accept private key, PEM, PFX, ACME credentials, Caddy config, arbitrary upstream, bypass flags, or provider secrets.
VALIDATION EVIDENCE: Boundary scans and HTTP response assertions covered bypass terms and secret terms.
REMAINING LIMITATION: Log redaction was not deeply inspected beyond API/test response surfaces and changed-file scans.

## 27. Secret Leakage Scan

OBSERVED REALITY: Secret-related guard strings appear in tests and documentation.
IMPLEMENTED CHANGE: API DTOs and frontend contracts expose only operational IDs, status, phase, generation, metadata, and idempotency/request fields.
VALIDATION EVIDENCE: Changed-file scan differentiated test guard strings from implementation exposure; HTTP tests assert responses do not include `VerificationToken`, private key, PEM, PFX, ACME credentials, Caddy credentials, provider secrets, challenge token, or raw provider credentials.
REMAINING LIMITATION: Real provider logs were unavailable in local tests.

## 28. Boundary Scan

OBSERVED REALITY: Controller ownership must stay limited to HTTP binding, auth, tenant context, service calls, and result mapping.
IMPLEMENTED CHANGE: Controller injects `ICertificateAdminCommandService` and contains no direct certificate lifecycle mutation, EF certificate mutation, audit writes, event publication, provider calls, DNS lookup, Caddy work, scheduler, sleeps, timers, or bypass flags.
VALIDATION EVIDENCE: Controller scan returned only benign comment hits for `enforced` and `retention`.
REMAINING LIMITATION: Existing unrelated dirty files were not included in the DOMAIN-5.8 boundary claim.

## 29. Database Safety

OBSERVED REALITY: Existing certificate and operation tables already support the required command surface.
IMPLEMENTED CHANGE: No destructive DB operation, reset, truncate, generation rewrite, or schema change was performed for DOMAIN-5.8.
VALIDATION EVIDENCE: Implementation used existing entities and migrations; no new migration file was created for DOMAIN-5.8.
REMAINING LIMITATION: A live production database inspection was not performed.

## 30. Migration Decision

OBSERVED REALITY: DTO/controller/service changes were sufficient.
IMPLEMENTED CHANGE: Chose no migration.
VALIDATION EVIDENCE: `dotnet build PointOfSale.sln` and focused tests passed against the current EF model.
REMAINING LIMITATION: Future provider-specific metadata needs could require additive migrations outside DOMAIN-5.8.

## 31. Real HTTP Integration Tests

OBSERVED REALITY: Existing Admin domain HTTP integration tests covered verification but not the full certificate command surface.
IMPLEMENTED CHANGE: Added real HTTP tests for provision, activate, renew, revoke, retry, refresh, duplicate idempotency, conflicting idempotency, wrong tenant, unauthorized user, invalid lifecycle, active operation conflict, 429 rate limit, safe errors, secret redaction, operation read, operation tenant isolation, primary preservation, compromised flow coverage, and expired/degraded flow coverage through seeded lifecycle states.
VALIDATION EVIDENCE: `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~AdminDomainsHttpIntegrationTests` passed with 12 tests.
REMAINING LIMITATION: The tests use deterministic provider/edge/probe fakes within the real HTTP fixture.

## 32. Regression Tests

OBSERVED REALITY: DOMAIN-4 through DOMAIN-5.7 regressions are the safety net for verification, persistence, provider, activation, renewal, and revocation behavior.
IMPLEMENTED CHANGE: DOMAIN-5.8 changes were validated against the focused custom-domain regression suite after implementation.
VALIDATION EVIDENCE: `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~PointOfSale.Tests.CustomDomains` passed with 195 tests.
REMAINING LIMITATION: Full solution test suite beyond the focused domain surface was not run.

## 33. Build

OBSERVED REALITY: Backend and frontend both require compilation after API/frontend contract changes.
IMPLEMENTED CHANGE: Updated DI, DTOs, controller, service layer, tests, frontend service contracts, and mutation hooks.
VALIDATION EVIDENCE: `dotnet build PointOfSale.sln` passed; `npm run build` in `PointOfSale.Web` passed.
REMAINING LIMITATION: The .NET build emitted existing warnings unrelated to DOMAIN-5.8.

## 34. Frontend Compatibility

OBSERVED REALITY: DOMAIN-5.8 is not the full DOMAIN-5.9 SSL UX, but frontend contracts needed command/read methods for the canonical API.
IMPLEMENTED CHANGE: Added domain management service methods and mutation hooks for certificate read, operation reads, provision, activate, renew, revoke, retry, and refresh.
VALIDATION EVIDENCE: `npm test -- --run src/modules/storefront/domains/services/domainManagementService.test.ts src/modules/storefront/domains/services/domainManagementService.mutations.test.ts src/modules/storefront/domains/hooks/useDomainMutations.test.tsx` passed with 33 tests.
REMAINING LIMITATION: No comprehensive SSL management UI was implemented.

## 35. Known Observations

OBSERVED REALITY: The repository contains many pre-existing untracked and modified DOMAIN files and unrelated application changes.
IMPLEMENTED CHANGE: DOMAIN-5.8 edits were scoped to Admin certificate API, service orchestration, DTOs, DI, frontend contracts, tests, and this certification artifact.
VALIDATION EVIDENCE: `git status --short` shows a broad dirty tree; DOMAIN-5.8 work was verified through focused build/test/scan commands. `git diff --check` passed with only line-ending warnings from the broader working tree.
REMAINING LIMITATION: Real external Caddy, live ACME/CA/provider, and production DNS/TLS conditions were not executed here.

## 36. Certification

OBSERVED REALITY: The controller is thin, services own lifecycle behavior, tenant/domain/certificate/operation correlation is enforced in the service layer, mutating commands are idempotent, operation fencing remains the correctness boundary, DTOs avoid certificate secrets, and no SNI/auth/primary-domain/failure-recovery bypass was added.
IMPLEMENTED CHANGE: Canonical Admin certificate command/read API is implemented and wired through existing architecture.
VALIDATION EVIDENCE: Backend build, focused HTTP integration tests, DOMAIN-4 through DOMAIN-5.7 focused regressions, frontend contract tests, frontend build, boundary scans, secret scans, route scans, and `git diff --check` passed.
REMAINING LIMITATION: Because real external Caddy/CA/provider execution was not performed, final certification is **PASS WITH OBSERVATIONS**.

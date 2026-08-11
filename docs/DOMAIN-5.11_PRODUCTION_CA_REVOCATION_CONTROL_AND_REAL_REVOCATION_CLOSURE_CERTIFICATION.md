# DOMAIN-5.11 - Production CA Revocation Control and Real Revocation Closure Certification

Certification date: 2026-08-10

Final certification: PASS WITH OBSERVATIONS / REAL CA VALIDATION ENVIRONMENT BLOCKED

## 1. Executive Summary

DOMAIN-5.11 closes the source-level production blocker identified in `DOMAIN-FINAL-FULL-ENGINEERING-CERTIFICATION.md`: `ICertificateProvider.RevokeCertificateAsync(...)` is no longer architecturally impossible. The production Caddy-managed provider now executes revocation through an explicitly configured, HTTPS-only, bearer-authenticated revocation control service that owns/controls the Caddy ACME account boundary and returns safe result metadata.

No private key, ACME account key, certificate material, PEM, PFX, JWK, Caddy storage content, or challenge secret was moved into Shopixy. Local edge deactivation remains separate from CA revocation.

Real CA-side proof was not executed because this workspace has no `caddy` executable, no staging Caddy filesystem, no configured revocation control service, no public staging certificate, and no authorized CA test fixture.

## 2. Previous Blocker

Previous blocker: `PointOfSale.Infrastructure/Certificates/CertificateProviderFoundation.cs:429-449` returned `CADDY_CA_REVOCATION_CONTROL_UNAVAILABLE` unconditionally.

Current source:

- `CertificateProviderFoundation.cs:862-922` calls a typed revocation-control client.
- `CertificateProviderFoundation.cs:409-421` still returns `CADDY_CA_REVOCATION_CONTROL_UNAVAILABLE` only when no authorized control surface is configured.
- `CertificateProviderHealthCheck` remains unhealthy if revocation control is absent.

## 3. Control-Surface Decision

Repository/deployment search found no native Caddy revocation API, Caddy module, control service, direct CA API credential path, Docker/Kubernetes revocation service, or provider-specific revocation plane.

Chosen architecture: Option B, authorized certificate/ACME control service.

Shopixy sends a typed revocation command to a configured HTTPS control surface. The control surface is responsible for Caddy ACME account/key custody and CA revocation. Shopixy receives only safe result metadata.

## 4. Architecture

```text
Shopixy Admin API
  -> CertificateRevocationService
  -> ICertificateProvider.RevokeCertificateAsync
  -> HTTPS Revocation Control Service
  -> Caddy/ACME account custody
  -> ACME CA revocation
```

Implemented source:

- `CertificateRevocationControlOptions`: `CertificateProviderFoundation.cs:35`.
- `HttpCertificateRevocationControlClient`: `CertificateProviderFoundation.cs:179`.
- DI registration: `CertificateProviderFoundation.cs:1288-1289`.
- deployment config placeholders: `deploy/staging/pointofsale.api.env.example:37-42`.

## 5. Security Boundary

The control boundary requires HTTPS and bearer auth. HTTP URLs, missing tokens, invalid paths, and invalid control config fail before HTTP execution.

No Shopixy persistence or DTO field was added for ACME keys, certificate keys, PFX, PEM, JWK, challenge secrets, or raw Caddy storage. The bearer token is a service-to-service credential injected by environment/secret manager, not CA private material.

## 6. Provider Implementation

`CertificateProviderFoundation.cs:862-922` validates provider/edge configuration, requires exact certificate identity, calls `ICertificateRevocationControlClient`, and maps `SUCCESS`/`ALREADY_REVOKED` to terminal provider success.

Canonical provider errors are normalized for:

SUCCESS, ALREADY_REVOKED, NOT_FOUND, UNAUTHORIZED, RATE_LIMITED, TEMPORARY_PROVIDER_FAILURE, PERMANENT_PROVIDER_FAILURE, INVALID_CERTIFICATE, CERTIFICATE_NOT_REVOCABLE, CONTROL_SURFACE_UNAVAILABLE, TIMEOUT, and CANCELLED.

## 7. Revocation State Machine

Existing durable flow remains:

`claimed operation -> optional edge deactivation -> provider CA revocation -> certificate Revoked`.

Source evidence:

- Edge deactivation remains separate: `CertificateRevocationService.cs:152-176`.
- Provider revocation happens after edge handling: `CertificateRevocationService.cs:178`.
- Revoked state is persisted only after provider success: `CertificateRevocationService.cs:536-589`.

## 8. Idempotency

Idempotency remains durable in `SslCertificateOperation`.

Evidence:

- Existing request hash and replay logic in `CertificateRevocationService`.
- Provider request includes `tenantId`, `domainId`, `certificateId`, `operationId`, `idempotencyKey`, and generation.
- Regression: focused backend suite passed 213 tests.

## 9. Tenant Isolation

All revocation admission still resolves by `(TenantId, DomainId, CertificateId)`. Wrong-tenant behavior is covered by HTTP/service tests. The control-service payload includes all three identifiers plus generation and operation id.

## 10. Authorization

Admin API authorization remains at the existing Owner/Admin Admin Domain boundary. The controller remains thin and delegates to `ICertificateAdminCommandService` and then lifecycle services.

No force, bypass, direct Caddy, or direct CA call was added to the controller.

## 11. Retry/Backoff

`Retry-After` is mapped from response headers or JSON body and persisted through existing provider failure handling. Rate limit and temporary failures remain retryable; unauthorized/invalid/permanent failures are terminal.

## 12. Edge Deactivation

Route removal remains local edge deactivation only. It is not represented as CA revocation. If route removal fails, revocation fails closed before CA control execution.

## 13. Events/Outbox

Success events are still emitted only from `PersistRevocationSuccessAsync`, after provider success is known and before the same unit of work save. Failure events remain safe and retry-aware.

## 14. Audit

Audit rows remain metadata-only. `revocation_requested`, `revocation_failed`, and `revocation_succeeded` are generated by service-layer code, not by the controller or provider.

## 15. Observability

Provider/control errors expose bounded canonical codes and retryability. Health now checks revocation control readiness instead of reporting healthy merely because issuance/SNI paths exist.

No unbounded domain/certificate/idempotency/provider request labels were added.

## 16. Health

`CertificateProviderHealthCheck` is unhealthy when:

- certificate provider config is invalid;
- revocation control is disabled;
- revocation control config is invalid;
- the control client is not registered;
- the control health endpoint is unreachable/unhealthy.

## 17. Admin API

`POST /api/admin/storefront/domains/{id}/ssl/revoke` still returns accepted operation semantics and never claims immediate certificate success for asynchronous work. Lifecycle behavior remains service-owned.

## 18. Frontend UX

D06 already displays server-authoritative operation state and avoids immediate "certificate revoked successfully" wording on HTTP 202. No frontend secret/idempotency storage was added in DOMAIN-5.11.

## 19. Concurrency

Existing fencing, leases, operation uniqueness, stale recovery, and replacement-before-revocation behavior remain intact. Focused concurrency/idempotency regressions passed in the 213-test backend suite.

## 20. Persistence/Migration

No migration was required. Existing columns already store safe provider revocation reference, revoked timestamp, retry metadata, operation state, and certificate identity metadata.

Evidence:

- `dotnet ef migrations list`: passed.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes.

## 21. Real Control-Surface Validation

Not executed.

Evidence:

- `Get-Command caddy` returned no executable.
- `C:\srv\pointofsale\staging`, staging Caddyfile path, and managed route directory do not exist on this machine.
- deployment example has `CertificateProvider__RevocationControl__Enabled=false` and empty endpoint/token.

## 22. Real CA Validation

Not executed. No staging certificate, public DNS fixture, Caddy runtime, revocation control service, or authorized CA test credential was available. No fake result is claimed as real CA revocation.

## 23. Tests

Passed:

- `dotnet build PointOfSale.sln --no-restore --disable-build-servers -p:UseSharedCompilation=false`: 0 errors.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Domain53AcmeFoundationTests`: 22 passed.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~Domain5|FullyQualifiedName~Domain4|FullyQualifiedName~AdminDomainsHttpIntegrationTests|FullyQualifiedName~CustomDomains"`: 213 passed.
- `npm test -- --run src/modules/storefront/domains`: 78 passed.
- `npm run build` in `PointOfSale.Web`: passed.

## 24. Regression

Regression gates passed for relevant Domain/DNS/SSL backend tests, Admin domain HTTP integration, frontend domain tests, frontend production build, EF migration listing, EF model drift check, and source boundary scans.

The full unfiltered repository test suite was not run because the relevant focused suite and full solution build already covered the modified surface and the repository has a very large unrelated test surface.

## 25. Defects Found

Blocking defect:

- Severity: Critical.
- File/location: `CertificateProviderFoundation.cs:862-922` before DOMAIN-5.11 was absent execution.
- Root cause: stock Caddy/Caddyfile boundary does not expose CA revocation.
- Impact: local `Revoked` could not honestly mean CA revoked.
- Remediation: added authenticated HTTPS revocation-control client and provider integration.
- Evidence: provider tests and focused backend suite pass.

## 26. Defects Fixed

Fixed:

- Source-level real revocation execution path behind `ICertificateProvider`.
- Exact certificate identity requirement for provider revocation.
- HTTPS/authenticated control-surface config validation.
- Canonical CA revocation result mapping.
- Provider readiness accounting for revocation-control availability.

## 27. Remaining Blockers

No source blocker remains for integrating an authorized revocation control service.

Operational blocker remains: no authorized revocation control service is configured or reachable in this environment, so real CA-side revocation is not proven.

## 28. Remaining Observations

- The control service itself is external to this repository and must own/operate Caddy ACME custody.
- Staging must provide a real public hostname and non-production certificate for destructive revocation proof.
- Caddy installation and storage backup/restore remain operational requirements.

## 29. Final Risk

Source risk is medium-low for Shopixy orchestration and high until real staging control-service/CA execution is proven. Production release must not claim PASS until a real staging certificate is revoked through the configured control surface.

## 30. Final Certification

PASS WITH OBSERVATIONS / REAL CA VALIDATION ENVIRONMENT BLOCKED.

The previous source blocker is closed without violating Caddy private-key custody. The only missing proof is external: a deployed authorized revocation control surface and a staging CA certificate to revoke.

This is not a PASS because no actual CA-side revocation was executed in this workspace.

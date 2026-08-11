# DOMAIN-5.5 Caddy Edge Integration and Certificate Activation Certification

Audit date: 2026-08-09

Certification: PASS WITH OBSERVATIONS

## 1. Executive Summary

OBSERVED REALITY: DOMAIN-5.4 persisted certificates as `Issued` and explicitly left Caddy edge activation out of scope.

IMPLEMENTED CHANGE: Added Caddy edge route reconciliation, external HTTPS/SNI certificate probing, activation orchestration, API command, safe events/audit, health signal, staging Caddy import, and focused tests.

VALIDATION EVIDENCE: DOMAIN-5.5 focused tests passed 6/6. Combined DOMAIN-5.5/5.4/5.3/5.2/DOMAIN-4 regression passed 130/130. Infrastructure and API no-dependency builds passed. `git diff --check` exited 0.

REMAINING FUTURE WORK: Real staging edge mutation/SNI proof requires an authorized staging domain and Caddy host access. Renewal, revocation, multi-region, disaster recovery, and scheduler recovery remain later waves.

## 2. Caddy Infrastructure Reality

OBSERVED REALITY: Repository infrastructure contains `deploy/staging/Caddyfile`, `deploy/staging/README.md`, and `deploy/staging/pointofsale.api.env.example`. No Docker, Kubernetes, Helm, NGINX, Traefik, HAProxy, CDN TLS API, or existing Caddy Admin API client was found.

IMPLEMENTED CHANGE: Used the observed Caddyfile topology. Added a managed import to `deploy/staging/Caddyfile` for per-host tenant custom-domain snippets.

VALIDATION EVIDENCE: Source scan found Caddy in staging files only before this wave. Build and tests passed with the Caddyfile-backed implementation.

REMAINING FUTURE WORK: Production Caddy deployment details and exact host paths must be confirmed by ops before enabling `CaddyEdge:Enabled`.

## 3. DOMAIN-5.1 Compliance

OBSERVED REALITY: DOMAIN-5.1 requires Caddy as canonical TLS edge, Caddy-held private material, and public SNI proof before `Active`.

IMPLEMENTED CHANGE: Activation requires route admission plus `IEdgeCertificateProbe` success before `certificate.TransitionTo(CertificateStatus.Active)` and `domain.MarkSslActive()`.

VALIDATION EVIDENCE: `Activation_CaddyAdmissionSuccessButSniFailure_DoesNotMarkActive` proves Caddy success alone does not activate.

REMAINING FUTURE WORK: Renewal/revocation monitoring is not claimed.

## 4. DOMAIN-5.2 Compatibility

OBSERVED REALITY: DOMAIN-5.2 froze `SslCertificate` and `SslCertificateOperation` as the metadata and idempotency model.

IMPLEMENTED CHANGE: Reused `SslCertificateOperation` with `OperationType.Activation`; no new operation table or migration was added.

VALIDATION EVIDENCE: Combined regression included `Domain52CertificatePersistenceFreezeTests`.

REMAINING FUTURE WORK: None for schema in this wave.

## 5. DOMAIN-5.3 Compatibility

OBSERVED REALITY: DOMAIN-5.3 created provider contracts and kept ACME execution edge-owned.

IMPLEMENTED CHANGE: Activation does not introduce Certes/ACMESharp/BouncyCastle or app-owned ACME.

VALIDATION EVIDENCE: Combined regression included `Domain53AcmeFoundationTests`; source scan found no new ACME package usage.

REMAINING FUTURE WORK: Real Caddy ACME execution remains edge-owned.

## 6. DOMAIN-5.4 Compatibility

OBSERVED REALITY: DOMAIN-5.4 exposes `POST /api/admin/storefront/domains/{id}/ssl/provision` and stops at pending activation.

IMPLEMENTED CHANGE: Added `POST /api/admin/storefront/domains/{id}/ssl/activate` as the next command. Provisioning was not changed to mark active.

VALIDATION EVIDENCE: Combined regression included `Domain54CertificateProvisioningAdmissionTests`.

REMAINING FUTURE WORK: Frontend action wiring is deferred.

## 7. Caddy Ownership Model

OBSERVED REALITY: Caddy terminates HTTPS and preserves original `Host`; Shopixy resolves domains and tenants.

IMPLEMENTED CHANGE: Caddy owns TLS serving and route files. Shopixy owns activation admission, metadata, audit, and state transition.

VALIDATION EVIDENCE: No private-key persistence fields or DTOs were added.

REMAINING FUTURE WORK: Caddy storage backup/restore is later DR work.

## 8. Caddy Client Boundary

OBSERVED REALITY: No Caddy client existed.

IMPLEMENTED CHANGE: Added `ICaddyEdgeClient` and `IEdgeCertificateProbe` contracts plus Infrastructure implementations.

VALIDATION EVIDENCE: Business service depends on contracts, not raw Caddy JSON or direct process/config manipulation.

REMAINING FUTURE WORK: If production uses Admin API instead of Caddyfile, implement a new Infrastructure adapter behind the same boundary.

## 9. Route Configuration Model

OBSERVED REALITY: Canonical staging uses Caddyfile, not Admin API.

IMPLEMENTED CHANGE: `CaddyfileEdgeClient` writes one safe managed Caddyfile per hostname and can run configured validate/reload commands.

VALIDATION EVIDENCE: Tests prove route success/failure behavior through the client contract fake; source validates host and route path confinement.

REMAINING FUTURE WORK: Real host command validation needs staging access.

## 10. Trusted Upstream Model

OBSERVED REALITY: Staging uses `API_UPSTREAM` default `127.0.0.1:5010` and storefront static root.

IMPLEMENTED CHANGE: Route target comes only from `CaddyEdge` trusted configuration, never tenant input. Loopback/private upstreams are rejected by default and require explicit `AllowLocalTrustedUpstream=true` for the observed host-service staging model.

VALIDATION EVIDENCE: `CaddyEdgeOptions_RejectsArbitraryUrlUpstream` covers scheme/path/metadata/loopback behavior.

REMAINING FUTURE WORK: Ops must set production upstreams explicitly.

## 11. Security / SSRF Protection

OBSERVED REALITY: No tenant-controlled upstream feature exists.

IMPLEMENTED CHANGE: Activation request accepts only idempotency key and optional certificate id; no arbitrary Caddy config/upstream payload is accepted.

VALIDATION EVIDENCE: DTOs contain no route target/config fields. Upstream validator rejects arbitrary URL/file/metadata forms.

REMAINING FUTURE WORK: More integration tests can cover real environment-denied upstreams.

## 12. Certificate Identity Model

OBSERVED REALITY: `SslCertificate` stores safe metadata: SAN, issuer, thumbprint/fingerprint, serial, validity.

IMPLEMENTED CHANGE: SNI result is compared against expected metadata where available, then persisted as safe observed metadata.

VALIDATION EVIDENCE: Probe fake tests activate only with matching identity and reject failures.

REMAINING FUTURE WORK: Real staging certificate mismatch tests require controlled cert fixtures.

## 13. SNI Verification Model

OBSERVED REALITY: No SNI probe existed.

IMPLEMENTED CHANGE: `ExternalSniCertificateProbe` uses `TcpClient` + `SslStream` to connect to the actual hostname on 443 with SNI and normal certificate validation.

VALIDATION EVIDENCE: Source scan found no `DangerousAcceptAnyServerCertificateValidator`.

REMAINING FUTURE WORK: Live public DNS/SNI validation not executed in this workspace.

## 14. Activation State Machine

OBSERVED REALITY: `Issued` did not imply `Active`.

IMPLEMENTED CHANGE: `Issued -> Activating -> Active` occurs only after edge route and SNI proof. Domain `SslActive` is set through `MarkSslActive()`.

VALIDATION EVIDENCE: Success and failure state tests pass.

REMAINING FUTURE WORK: Expiry downgrade belongs later.

## 15. Idempotency / Concurrency

OBSERVED REALITY: `SslCertificateOperation` unique indexes provide durable operation scope.

IMPLEMENTED CHANGE: Activation uses `OperationType.Activation`, idempotency key, serializable transaction where available, and per-domain operation conflict checks.

VALIDATION EVIDENCE: Repeated activation test creates one activation operation and one route/probe call.

REMAINING FUTURE WORK: Multi-process PostgreSQL contention tests can be added when real DB execution is authorized.

## 16. Recovery Model

OBSERVED REALITY: Caddy and PostgreSQL cannot share one ACID transaction.

IMPLEMENTED CHANGE: Activation phases are explicit. If route succeeds and SNI fails, DB remains non-active and retryable failure metadata is persisted.

VALIDATION EVIDENCE: SNI failure test leaves `Domain.SslCertificateId` null.

REMAINING FUTURE WORK: Later scheduler/reconciler can retry/reconcile stuck operations.

## 17. Audit / Events

OBSERVED REALITY: Domain audit and `IEventPublisher` exist.

IMPLEMENTED CHANGE: Added safe audit actions and events for started, edge-admitted, failed, certificate active, and domain SSL active.

VALIDATION EVIDENCE: Success events are emitted only after durable success persistence; failure test proves no active events are emitted.

REMAINING FUTURE WORK: Outbox-specific transaction assertions can be expanded later.

## 18. Observability

OBSERVED REALITY: Existing logs/health checks use safe operational dimensions.

IMPLEMENTED CHANGE: Activation persists operation phase/status, failure code, retryability, edge identifiers, observed thumbprint, and correlation id. Caddy route reconciliation logs safe IDs.

VALIDATION EVIDENCE: Tests assert operation states and failure codes.

REMAINING FUTURE WORK: Dedicated metrics counters/histograms can be added with the platform telemetry convention.

## 19. Health / Readiness

OBSERVED REALITY: App exposes health checks.

IMPLEMENTED CHANGE: Added `CaddyEdgeHealthCheck`, degraded when disabled and unhealthy if enabled route directory is unreachable.

VALIDATION EVIDENCE: API no-dependency build passed with health registration.

REMAINING FUTURE WORK: Do not make app unavailable for a single tenant activation failure.

## 20. API Boundary

OBSERVED REALITY: `AdminDomainsController` is the domain command boundary.

IMPLEMENTED CHANGE: Added thin `ActivateSsl` endpoint delegating to `ICertificateActivationService`.

VALIDATION EVIDENCE: Controller contains no direct Caddy/probe/EF activation logic.

REMAINING FUTURE WORK: Frontend command wiring later.

## 21. Frontend Boundary

OBSERVED REALITY: Frontend SSL pages exist from prior work.

IMPLEMENTED CHANGE: No frontend UI change was made.

VALIDATION EVIDENCE: No frontend files were touched by DOMAIN-5.5.

REMAINING FUTURE WORK: DOMAIN-5.9 owns merchant-facing activation controls.

## 22. Database Safety

OBSERVED REALITY: The request forbids resets/truncation/new DB.

IMPLEMENTED CHANGE: No migration and no database mutation command were created or run.

VALIDATION EVIDENCE: Validation used source builds and in-memory tests only.

REMAINING FUTURE WORK: Existing DB read-only verification can be performed when connection/access is approved.

## 23. Real Edge Validation

OBSERVED REALITY: No authorized real staging Caddy access or dedicated test domain was available in this workspace.

IMPLEMENTED CHANGE: Real-edge mutation was not faked as certification; it is documented as an environment limitation.

VALIDATION EVIDENCE: Route/probe behavior is covered by contract fakes and source-level staging config.

REMAINING FUTURE WORK: Run with an existing safe staging hostname, public DNS pointed to Caddy, and enabled `CaddyEdge` config.

## 24. Test Evidence

VALIDATION EVIDENCE:

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~Domain55CaddyEdgeActivationTests"`: 6 passed.
- Combined regression filter: 130 passed.

## 25. Regression Evidence

VALIDATION EVIDENCE:

- DOMAIN-5.4 regression: passed in 130-test run.
- DOMAIN-5.3 regression: passed in 130-test run.
- DOMAIN-5.2 regression: passed in 130-test run.
- DOMAIN-4/domain verification/read/write regression: passed in 130-test run.

## 26. Forbidden Boundary Scan

VALIDATION EVIDENCE: Source scan found no insecure TLS bypass, no private key persistence, no raw Caddy payload accepted from frontend/controller, and no Active transition outside activation service. Matches for private-key/challenge text are existing guard comments/tests/contracts.

## 27. Known Observations

OBSERVED REALITY: A running `PointOfSale.API` process locked normal API dependency-copy outputs during one full test build attempt.

VALIDATION EVIDENCE: Project-scoped no-dependency API build, Infrastructure build, test build, and no-build tests passed.

REMAINING FUTURE WORK: Stop the running API process before running a full solution build if a clean copy-to-output pass is required.

## 28. Certification

PASS WITH OBSERVATIONS

The implementation preserves the constitutional invariant:

```text
CADDY CONFIGURED + PUBLIC HTTPS/SNI PROOF + CERTIFICATE IDENTITY MATCH + DURABLE COMMIT = ACTIVE
```

Observation: real public edge validation was not executed because no authorized staging Caddy/test-domain access was available in this workspace.

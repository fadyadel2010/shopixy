# DOMAIN-5.10 - Full Domain & SSL/TLS End-to-End Engineering Certification

Certification date: 2026-08-10  
Repository: `PointOfSale` / Shopixy  
Scope authority: DOMAIN-0 through DOMAIN-5.9  
Final classification: **FAIL**

## 1. Executive Summary

**Classification: FAIL**

The Domain, DNS-verification, certificate-persistence, Caddy-admission, activation, renewal, revocation, admin API, and admin UX implementation was reconstructed from current source and tested after remediation. The locally executable implementation is substantially stronger after this audit: all focused backend and frontend tests pass, both frontends build, the .NET solution builds, the configured PostgreSQL database reports every known migration applied, and EF reports no pending model changes.

The system cannot receive a clean or qualified pass because one blocking production defect remains. `CaddyManagedAcmeCertificateProvider`, the concrete `ICertificateProvider` registered by production DI, returns `CERTIFICATE_PROVIDER_EXECUTION_OUT_OF_SCOPE` for order creation, authorization, challenge, operation-status, and revocation. Consequently, the real production path cannot progress from a verified domain to an issued certificate, and cannot perform real provider revocation. Test fakes prove application orchestration only; they are not production-provider evidence.

This is a source-proven architectural gap, not merely unavailable external infrastructure. Under the acceptance rules, a real architectural defect requires **FAIL**. A new readiness health check now reports this condition as unhealthy when the provider is enabled, so the missing execution path is no longer masked by configuration-only or directory-only health.

Safe defects found during the audit were remediated, including HTTP-verification SSRF/DNS-rebinding exposure, cache TTL violations, premature primary creation/promotion, invalid primary retention after certificate expiry or revocation, non-atomic event/outbox ordering, missing `Retry-After`, unsafe browser retry idempotency, premature success-style UX, misleading add-domain controls, unbounded certificate metric cardinality, and provider readiness reporting.

## 2. Scope

**Classification: PASS**

The audit covered:

- Domain creation, update, verification entry, verification, primary promotion, archive, restore, and soft delete.
- TXT, CNAME, and HTTPS-file ownership verification; caching, timeouts, canonical errors, scheduler, and health.
- `SslCertificate` and `SslCertificateOperation` persistence, generations, operations, leases, fencing, retry metadata, migrations, snapshot, and the live configured database.
- Provider contracts, provisioning admission, Caddy edge route reconciliation, external SNI proof, activation, renewal, replacement, revocation, recovery, audit, events, telemetry, and health.
- Admin controller routes and DTOs, the frontend API boundary, hooks, routes, D02/D03/D05/D06 behavior, Arabic strings, dark/responsive utility classes, accessibility controls, and confirmation dialogs.
- Tenant isolation, IDOR resistance, secret persistence/exposure, SSRF, replay/idempotency, concurrency controls, transaction boundaries, and outbox behavior.
- Current repository state, including its pre-existing large dirty worktree. No reset, checkout, revert, unrelated overwrite, database mutation, or destructive migration was performed.

## 3. Authority Documents

**Classification: PASS WITH OBSERVATIONS**

Located and used as authority where present:

- `docs/architecture/DOMAIN-0_STOREFRONT_CUSTOM_DOMAINS_ARCHITECTURE_AUDIT.md`
- `docs/architecture/DOMAIN-1_STOREFRONT_CUSTOM_DOMAINS_CANONICAL_BLUEPRINT.md`
- `docs/architecture/DOMAIN-1.5_STOREFRONT_CUSTOM_DOMAINS_PERSISTENCE_FREEZE.md`
- `docs/architecture/DOMAIN-1.6_STOREFRONT_CUSTOM_DOMAINS_RUNTIME_CONSTITUTION.md`
- `DOMAIN-2A_HOST_RESOLUTION_FOUNDATION_IMPLEMENTATION.md`
- `DOMAIN-2B_HOST_DOMAIN_RESOLUTION_FOUNDATION_IMPLEMENTATION.md`
- `DOMAIN-2C_CONSTITUTIONAL_ENGINEERING_AUDIT.md`
- `DOMAIN-2C.1_FINAL_EXECUTION_CERTIFICATION.md`
- `docs/architecture/DOMAIN-2D_TENANT_RESOLUTION_INTEGRATION_IMPLEMENTATION.md`
- `docs/architecture/DOMAIN-3.0_STOREFRONT_DOMAIN_MANAGEMENT_ARCHITECTURE_AUDIT.md`
- `docs/architecture/DOMAIN-3.0.1_ARCHITECTURE_VERIFICATION_AND_CLEANUP.md`
- `docs/architecture/DOMAIN-3.0.2_RUNTIME_REALITY_VERIFICATION.md`
- `docs/architecture/DOMAIN-3.1_STOREFRONT_DOMAIN_MANAGEMENT_CANONICAL_BLUEPRINT.md`
- `docs/architecture/DOMAIN-3.1-A.1_UX_CERTIFICATION_AND_PRODUCTION_READINESS.md`
- `docs/Domain/DOMAIN-3.1-B.1_READ_PATH_ENGINEERING_CERTIFICATION.md`
- `DOMAIN-3.1-C_COMMANDS_IMPLEMENTATION.md`
- `DOMAIN-3.1-C.1_COMMAND_PATH_CERTIFICATION.md`
- `docs/architecture/domains/DOMAIN-4.0_DNS_VERIFICATION_ARCHITECTURE_AUDIT.md`
- `docs/architecture/domains/DOMAIN-4.1_DNS_VERIFICATION_CANONICAL_BLUEPRINT.md`
- `DOMAIN-4.2A_DNS_RESOLVER_FOUNDATION_IMPLEMENTATION.md`
- `DOMAIN-4.3_DOMAIN_VERIFICATION_ENGINE_IMPLEMENTATION.md`
- `DOMAIN-4.3.1_VERIFICATION_LIFECYCLE_ENTRY_AND_ENGINE_CERTIFICATION.md`
- `DOMAIN-4.4_VERIFICATION_SCHEDULER_CERTIFICATION.md`
- `DOMAIN-4.5_VERIFICATION_COMMANDS_API_CERTIFICATION.md`
- `DOMAIN-4.5.1_VERIFICATION_COMMAND_API_CERTIFICATION.md`
- `DOMAIN-4.6_FULL_DNS_VERIFICATION_END_TO_END_CERTIFICATION.md`
- `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md`
- `docs/architecture/DOMAIN-5.1_CANONICAL_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_BLUEPRINT.md`
- `DOMAIN-5.2_CERTIFICATE_PERSISTENCE_FREEZE_IMPLEMENTATION_AUDIT.md`
- `DOMAIN-5.3_ACME_FOUNDATION_IMPLEMENTATION_AUDIT.md`
- `DOMAIN-5.4_CERTIFICATE_PROVISIONING_ADMISSION_CERTIFICATION.md`
- `DOMAIN-5.5_CADDY_EDGE_INTEGRATION_AND_CERTIFICATE_ACTIVATION_CERTIFICATION.md`
- `DOMAIN-5.6_RENEWAL_SCHEDULER_CERTIFICATION.md`
- `DOMAIN-5.7_REVOCATION_AND_FAILURE_RECOVERY_CERTIFICATION.md`
- `DOMAIN-5.8_ADMIN_COMMANDS_API_CERTIFICATION.md`
- `DOMAIN-5.9_ADMIN_SSL_MANAGEMENT_UX_CERTIFICATION.md`
- `STOREFRONT_DOMAIN_FULL_ENGINEERING_CERTIFICATION.md`

Standalone files named exactly DOMAIN-2, DOMAIN-3, DOMAIN-3.1-A, DOMAIN-3.1-B, DOMAIN-4, or DOMAIN-4.2 were not present. Their available sub-wave documents and current source were used. Earlier certification claims were treated as hypotheses and were not accepted over contradictory source.

## 4. Repository Reality

**Classification: PASS WITH OBSERVATIONS**

Canonical implementation inventory:

| Area | Current source |
| --- | --- |
| Entities | `PointOfSale.Core/Entities/Domain.cs`, `DomainVerification.cs`, `DomainAuditEntry.cs`, `SslCertificate.cs`, `SslCertificateOperation.cs` |
| Enums and contracts | `PointOfSale.Core/Enums/Domain*.cs`, certificate enums, `PointOfSale.Core/Certificates/*`, `PointOfSale.Core/Interfaces/IDnsResolver.cs` |
| Events | `PointOfSale.Core/Events/Domain/*` |
| EF configuration | `PointOfSale.Infrastructure/Data/Configurations/Domain*.cs`, `SslCertificate*.cs` |
| Persistence and resolution | `DomainRepository.cs`, `PersistenceDomainResolver.cs`, `ApplicationDbContext.cs` |
| Migrations | DOMAIN-2C tables, DOMAIN-4.3 diagnostics, DOMAIN-5.2 certificate persistence, current snapshot |
| DNS | `DnsClientResolver.cs`, `PublicEndpointHttpMessageHandler.cs`, `DnsResolverOptions.cs`, DNS health check |
| Provider/Caddy/SNI | `CertificateProviderFoundation.cs`, `CaddyEdgeIntegration.cs`, `ExternalSniCertificateProbe.cs` |
| Application services | `DomainReadService`, `DomainWriteService`, verification/scheduler, provisioning, activation, renewal, revocation, admin command services |
| API | `AdminDomainsController.cs`, Storefront DTOs, DI and health registration in `Program.cs` |
| Admin frontend | `PointOfSale.Web/src/modules/storefront/domains` and information-architecture routes |
| Runtime storefront | host/tenant resolution middleware and `PointOfSale.Storefront` tenant bootstrap/runtime |
| Tests | `PointOfSale.Tests/Domain`, DNS/host middleware suites, admin-domain Vitest suites, storefront certification suites |

The worktree was already extensively modified and contained many untracked DOMAIN artifacts before this audit. Changes were scoped to relevant files and unrelated state was preserved. The audit-created isolated build directory was removed after validation; other pre-existing `.codex-temp` content was left untouched.

## 5. Domain Lifecycle Verification

**Classification: FAIL**

| Transition | Current verdict | Evidence/result |
| --- | --- | --- |
| Create -> Draft | PASS | Alias/preview creation persists normalized host, audit, outbox event; direct Primary/Redirect/Temporary creation is rejected. |
| Draft -> PendingVerification | PASS | Explicit start command, tenant filter, version checks, refresh limits, token-safe audit. |
| PendingVerification -> Verified | PASS | TXT/CNAME/HTTPS exact checks, attempt persistence, audit/events, concurrent-success regression. |
| Verified -> certificate provisioning | FAIL | Admission is implemented, but production `CreateOrderAsync` always returns execution-out-of-scope. |
| Issued -> Activating | PASS in isolated application tests | Operation claim, lease/fencing, route reconciliation, and audit exist. |
| Activating -> Active/SslActive | NOT VERIFIED externally | Code requires successful route state and valid external SNI identity; fake edge/probe tests pass. Real issuance cannot reach this stage. |
| Active -> renewal generation N+1 | PASS in isolated application tests | Current valid authority is preserved on replacement failure; replacement becomes authoritative only after activation proof. |
| Old generation retirement | PASS WITH OBSERVATIONS | Authority and replacement link move correctly; the old row remains `Renewing` because no explicit `Retired` enum exists. |
| Expiry/revocation -> Verified degradation | PASS | Invalid authoritative SslActive/Primary domains become Alias + Verified and clear `SslCertificateId`. |
| Archive/restore/delete | PASS | Primary protection, retention/state checks, audit/event, and soft-delete behavior are covered. |

The lifecycle as a whole is **FAIL** because the real production execution graph stops at provider order creation.

## 6. Domain Management Verification

**Classification: PASS WITH OBSERVATIONS**

Verified current behavior:

- Create, update, start verification, archive, restore, delete, and primary promotion are service-owned and tenant-scoped.
- Host normalization lowercases, trims a trailing dot, and normalizes underscores to hyphens; reserved host and duplicate checks are enforced.
- Global normalized-host uniqueness and tenant-primary uniqueness are represented in EF/migrations.
- Primary domains cannot be archived/deleted directly.
- Promotion now requires `SslActive`, a linked certificate owned by the same tenant/domain, `CertificateStatus.Active`, and a non-deleted row.
- Direct creation of `Primary` no longer creates an unreachable Draft Primary that verification rejects.
- Promotion uses a serializable transaction and optimistic versions for the target and previous primary.
- Restore and delete retain soft-delete semantics and audit history.

Observation: the older DOMAIN-3.1 blueprint allowed direct Primary selection before DOMAIN-5.1 tightened the invariant. Current code follows the later rule: Primary is granted only after edge-proven SSL.

## 7. DNS Verification Verification

**Classification: PASS WITH OBSERVATIONS**

- Business logic depends on `IDnsResolver`; `DnsClient` remains Infrastructure-only.
- TXT and CNAME responses are normalized into provider-neutral result models and canonical error codes.
- TXT token comparison remains exact and case-sensitive; CNAME comparison normalizes trailing-dot/case behavior.
- HTTPS-file verification requires HTTPS on the default port, rejects credentials and malformed hosts, disables redirects and proxies, limits response bodies to 4096 characters/bytes in the practical verification boundary, and normalizes failures.
- The new socket connector resolves immediately before connection, rejects loopback, RFC1918, CGNAT, link-local, multicast, documentation, benchmark, ULA, and non-global IPv6 addresses, and pins the socket to a validated address. This closes preflight-to-connect DNS rebinding and metadata-endpoint access.
- Positive and negative cache writes are hard-clamped to 300 seconds. Options now reject `MaxCacheTtl` or `NegativeCacheTtl` above 300.
- Timeouts, cancellation, per-instance concurrency limiting, positive/negative caching, and real DNS health checks are present.
- Verification attempts, audit entries, success/failure events, retryability, rate limiting, token refresh, and scheduler batching are covered.

External public DNS propagation and a real merchant HTTPS well-known endpoint were not exercised. That portion is **ENVIRONMENT BLOCKED**.

## 8. Certificate Persistence Verification

**Classification: PASS**

`SslCertificate` stores metadata, provider/edge opaque references, generation, status, validity, retry, replacement, revocation, lease, fencing, audit correlation, and safe history. `SslCertificateOperation` stores the durable operation state, type/phase/status, request hash, idempotency key, attempts, retry window, lease/fencing owner, and safe failure metadata.

Verified controls include:

- Explicit tenant and domain ownership columns.
- Composite tenant/domain relationships and indexes.
- Generation uniqueness.
- Certificate and operation idempotency uniqueness.
- Active/non-terminal/renewal/retry/fencing indexes and check constraints.
- Domain authoritative-certificate relationship.
- No private key, PEM/PFX/PKCS12/JWK, raw provider credential, or raw Caddy storage field.

EF source model, migrations, and snapshot are aligned. No persistence remediation or migration was required by this audit.

## 9. ACME Boundary Verification

**Classification: FAIL**

The abstraction boundary itself is sound:

- `ICertificateProvider` owns account, order, authorization, challenge, status, and revocation operations.
- HTTP-01 and TLS-ALPN-01 exist in provider-neutral contracts; DNS-01 throws `NotSupportedException`.
- Certes, ACMESharp, and BouncyCastle do not leak into Core or API.
- Challenge secrets are transient contract values, redact `ToString()`, and can be removed with `WithoutSecret()`.
- Provider errors sanitize secret-bearing keys/terms.

The concrete production adapter is not an implementation. `CaddyManagedAcmeCertificateProvider` returns `CERTIFICATE_PROVIDER_EXECUTION_OUT_OF_SCOPE` for every operational method except configuration-only account selection. This is deliberate fail-closed behavior, but it means no live ACME/Caddy certificate execution exists. `SupportsLiveExecution` is now explicitly false and the provider health check reports unhealthy when enabled.

No test double was counted as production evidence.

## 10. Provisioning Verification

**Classification: FAIL**

Provisioning admission correctly checks non-empty tenant, tenant/domain correlation, deletion, `VerifiedAtUtc`, `Verified` lifecycle, supported type, normalized/reserved host, provider configuration, active-operation conflicts, generation, idempotency, and durable operation ownership. It creates generation N+1 and persists safe metadata only. Same-key replay and conflicting/concurrent operations are tested.

The orchestration cannot issue a real provider order because the registered provider fails at `CreateOrderAsync`. Recovery logic can persist a safe terminal failure, but successful production provisioning is unreachable.

## 11. Caddy Edge Verification

**Classification: NOT VERIFIED**

Static/source and isolated-test findings:

- `ICaddyEdgeClient` constrains edge actions to typed route requests and queries.
- Managed route fragments are generated by the infrastructure integration; the API does not accept raw Caddy configuration.
- Upstream targets come from trusted configuration, not tenant input.
- Route writes are deterministic/idempotent and invoke validation/reload commands.
- Insecure TLS validation is not enabled.
- Staging remains opt-in and deployment examples default the feature off.
- `CaddyEdgeHealthCheck` checks enablement and managed-route directory availability.

A real Caddy process, config validation/reload, route serving, and failure window were not available. Directory reachability alone is not proof of a serving edge. The separate provider readiness check prevents that limited health signal from implying certificate readiness.

## 12. Activation Verification

**Classification: NOT VERIFIED**

The application activation path enforces:

`Issued -> Activating -> route reconciliation -> external SNI probe -> identity validation -> Active + Domain.MarkSslActive()`.

The SNI probe uses normal certificate validation and checks host/SAN, thumbprint/fingerprint, serial, issuer, validity window, and expiry. SNI failure, certificate mismatch, or route failure persists failure state and never marks the certificate/domain active. Success events are staged in the same transactional outbox save as the active state.

Tests cover route failure, SNI failure, duplicate activation, idempotent replay, and successful metadata activation with controlled fakes. No real public certificate could be issued or served, so real activation remains **ENVIRONMENT BLOCKED** and is also unreachable behind the provider defect.

## 13. Renewal Verification

**Classification: PASS WITH OBSERVATIONS**

- Candidate selection is bounded and uses renewal/expiry indexes.
- One Hangfire recurring job is registered; no second timer/background scheduler was introduced.
- Threshold, retry-after, bounded attempts, exponential backoff, jitter, provider budget, lease/fencing, and stale-operation recovery are represented.
- Renewal creates replacement generation N+1 and calls the activation boundary.
- A valid current certificate remains authoritative when issuance/activation of the replacement fails.
- Authority moves only after replacement activation proof.
- After the current certificate actually expires, an authoritative SslActive or Primary domain degrades to Alias + Verified and clears the link.
- Primary-expiry regression coverage now proves removal of invalid Primary authority.

Observation: successful replacement marks the old certificate non-renewable and links its replacement, but retains `CertificateStatus.Renewing`; an explicit `Retired` state would make history clearer and would require a deliberate schema/enum wave.

Real provider renewal and public edge proof remain unverified and cannot succeed with the current provider adapter.

## 14. Revocation Verification

**Classification: FAIL**

Application policy is present and tested:

- Canonical revocation reason codes and compromise reasons.
- Replacement-before-revocation for a still-valid authority unless immediate revocation is requested.
- Preservation of the valid current certificate when replacement does not become durably authoritative.
- Immediate or already-invalid authoritative revocation degrades SslActive/Primary to Alias + Verified and clears authority.
- Tenant/domain/certificate correlation, operation idempotency, lease/fencing metadata, audit, events, retry metadata, and safe errors.

Real revocation is delegated to `ICertificateProvider.RevokeCertificateAsync`; the registered production provider always returns execution-out-of-scope. Therefore production revocation is not implemented. Any corresponding Caddy storage/route effect is also unproven.

## 15. Failure Recovery Verification

**Classification: PASS WITH OBSERVATIONS**

Verified local invariants:

- Failed replacement does not clear a still-valid authoritative certificate.
- Failed SNI proof does not activate a certificate.
- Expired/revoked authority is not retained as Primary.
- Retryable provider/edge failures retain safe failure codes and retry windows.
- Stale operations have lease/fencing and recovery paths.
- Recovery uses existing renewal/activation orchestration rather than a second scheduler.
- Outbox events are not staged on failed proof/failed transactions.

The provider execution gap prevents real recovery against CA/Caddy failures. Multi-process crash-window recovery was inspected and unit/integration-tested at application level but not chaos-tested against real external infrastructure.

## 16. Admin API Verification

**Classification: PASS**

Verified controller routes:

- `GET /api/admin/storefront/domains/{id}/ssl`
- `GET /api/admin/storefront/domains/{id}/ssl/certificate`
- `GET /api/admin/storefront/domains/{id}/ssl/operations`
- `GET /api/admin/storefront/domains/{id}/ssl/operations/{operationId}`
- `POST /api/admin/storefront/domains/{id}/ssl/provision`
- `POST /api/admin/storefront/domains/{id}/ssl/activate`
- `POST /api/admin/storefront/domains/{id}/ssl/renew`
- `POST /api/admin/storefront/domains/{id}/ssl/revoke`
- `POST /api/admin/storefront/domains/{id}/ssl/retry`
- `POST /api/admin/storefront/domains/{id}/ssl/refresh`

All commands return `202 Accepted` with server operation state when accepted. Validation maps to 400, not found to 404, concurrency/active-operation conflict to 409, rate limits to 429, and authorization is enforced by the controller/policy pipeline. Certificate 429 responses now include a standards-compatible `Retry-After` header plus safe response details. DTOs expose metadata and operation state, not entities or secret material. The controller delegates lifecycle/audit/event ownership to services.

Real HTTP TestServer coverage exercises routing, DI, authorization context, tenant isolation, idempotency conflicts, error status mapping, and response safety.

## 17. Admin UX Verification

**Classification: PASS WITH OBSERVATIONS**

D06 uses real API hooks and displays safe certificate metadata, lifecycle, server-authoritative operations, accepted/failed states, operations history/details, pagination, loading, empty, unauthorized, not-found, rate-limit, and error states. Provision, activate, renew, revoke, retry, and refresh actions use the API service boundary. Activation and revocation use confirmation dialogs; revocation requires a canonical reason.

Remediations:

- A returned failed operation now shows warning/failed wording, not a success-style toast.
- A command key is retained only in memory after an uncertain transport failure and reused for the same action/target. A definitive server response clears it. Nothing is written to `localStorage` or `sessionStorage`.
- D03 no longer offers direct Primary or a redirect type that the backend rejects only after submission.
- D03 shows the single server-managed certificate policy instead of fake selectable providers that were never sent to the API.
- Redirect configuration is described as post-creation instead of exposing a control ignored by creation.
- English and Arabic strings were added for the new managed/redirect wording.

D02 displays SSL summary/primary state and links to D06. Focused tests cover D02/D05/D06, service routes, hooks, command confirmation, idempotency, accepted wording, and safe rendering.

Observation: responsive/dark-mode/accessibility behavior is supported by existing components and utility classes and covered indirectly by render tests; no browser/device matrix or screen-reader session was executed.

## 18. Security Verification

**Classification: PASS WITH OBSERVATIONS**

Secret scan classification:

- `SslCertificate` comments forbid private key/PEM/PFX/PKCS12/JWK/provider credential storage; no such persistence fields exist.
- Certificate challenge token/key authorization exists only in transient provider contracts, is redacted by `ToString()`, is removable with `WithoutSecret()`, and is not persisted.
- Provider error sanitizer terms for `privatekey`, `jwk`, `pem`, `pfx`, `token`, `secret`, and credentials are protective denylist hits.
- Domain ownership-verification tokens are intentionally persisted as ownership proof and returned only by the authorized D05 verification endpoint; detail DTOs, audits, logs, metrics, and certificate DTOs do not expose them.
- D06 stores no certificate command keys or secret material in browser storage.

SSRF and request-boundary findings:

- HTTP verification now blocks non-public network ranges, metadata addresses, localhost/loopback, non-default ports, credentials, redirects, proxies, DNS rebinding, and oversized bodies.
- Caddy upstreams are trusted configuration rather than merchant input.
- No `file://`, arbitrary scheme, raw Caddy configuration, or insecure certificate-validation path is accepted.
- Host normalization and tenant resolution fail closed for unknown/unowned hosts.
- Controller/service queries correlate tenant with domain/certificate/operation IDs to prevent IDOR.

External penetration testing and a real proxy/edge environment were not performed.

## 19. Tenant Isolation Verification

**Classification: PASS**

Domain, verification, certificate, operation, audit, and command queries use explicit tenant correlation, commonly with `IgnoreQueryFilters()` followed by tenant predicates so bypassing a filter cannot create cross-tenant access. Composite relationships and indexes reinforce ownership. Wrong-tenant domain/certificate/operation requests return not found or safe validation errors and do not leak hostname, serial, or certificate metadata. HTTP integration tests exercise cross-tenant probing across all SSL read/command routes.

## 20. Concurrency Verification

**Classification: PASS WITH OBSERVATIONS**

The implementation uses:

- Serializable transactions for domain promotion and operation claiming where applicable.
- Optimistic version checks for domain writes and primary promotion.
- Unique indexes for normalized hosts, one tenant primary, generations, and operation idempotency scope.
- Durable certificate-operation status, attempts, fencing token, lock owner, and lease expiry.
- Stale-worker/lease checks before authoritative transitions.
- Bounded Hangfire candidates and worker concurrency.
- No reliance on browser or in-process locks for server correctness; frontend refs only prevent duplicate user clicks.

Tests cover concurrent StartVerification, VerifyNow, same-key provisioning, duplicate activation, and service fencing/idempotency. Most certificate concurrency tests use EF InMemory/TestServer and do not constitute real multi-node PostgreSQL/Hangfire load proof. A real two-API/two-worker collision campaign is **NOT VERIFIED**.

## 21. Idempotency Verification

**Classification: PASS**

- Provisioning, activation, renewal, revocation, retry, and refresh accept durable idempotency keys.
- Requests store safe request hashes and reject conflicting payload reuse where payload identity matters.
- Same-key replay returns the existing operation and does not repeat proven provider/edge work.
- Database uniqueness scopes keys by tenant/domain/operation type.
- The frontend now reuses the same in-memory key only when transport outcome is uncertain; a server response ends that attempt.
- No command keys are persisted in browser storage.

The production provider cannot demonstrate provider-side idempotency because it never executes; application-side behavior is proven.

## 22. Audit/Event Verification

**Classification: PASS**

Meaningful domain/verification/certificate transitions create safe audit records and integration events with tenant, correlation, operation, and generation metadata where relevant. Secret/token material is excluded from audit snapshots and certificate events.

This audit corrected event ordering so authoritative events are published to MassTransit's EF outbox before the same `SaveChangesAsync` that commits source state. Activation success, verification result, renewal expiry/degradation, renewal success, and revocation success/failure are now atomic with their business mutation. The publisher rejects authoritative publishing when no pending business mutation/transaction exists, preventing post-commit outbox gaps. Failed SNI proof emits failure events only and does not emit activation success.

## 23. Observability Verification

**Classification: PASS WITH OBSERVATIONS**

- DNS health executes a real resolver query and reports latency/failure.
- Certificate renewal health queries the database for due, urgent, exhausted, and unsafe-authority certificates.
- Caddy health reports disabled/missing directory states rather than always healthy.
- The new certificate-provider health check reports disabled as degraded, invalid configuration as unhealthy, and the current non-executing adapter as unhealthy.
- Structured logs carry tenant/domain/operation correlation without certificate secret material.
- Certificate metrics retain bounded provider, phase, outcome, failure-code, and reason dimensions.

Remediation removed domain GUID, certificate GUID, operation GUID, and generation from metric tags; those were unbounded high-cardinality dimensions.

Observation: Caddy health does not independently perform a real SNI transaction; activation is the authority for SNI proof.

## 24. Frontend/Backend Contract Verification

**Classification: PASS**

The backend route/method set matches `domainManagementService.js` for dashboard, list, detail, verification, SSL summary, certificate, operations, redirects, history, activity, all verification commands, all SSL commands, archive, restore, delete, and primary promotion. Hook and page property usage matches current DTO names/status enums and paginated response shape.

The stale service comment claiming D03 was UI-only was corrected. No mock certificate data is used by production pages. Tests mock the service boundary only, which is appropriate frontend isolation and is not runtime data.

## 25. Database/Migration Verification

**Classification: PASS**

Commands and results:

```text
dotnet ef migrations list --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build
```

Connected through Npgsql to the configured `ApplicationDbContext` database and listed migrations from `20260723214642_Baseline_Recovery3` through `20260809203807_SHOPIXY_CheckoutAttachments`, including DOMAIN-2C, DOMAIN-4.3, and DOMAIN-5.2. No migration was marked pending.

```text
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build
No changes have been made to the model since the last migration.
```

`dotnet ef dbcontext info` confirmed Npgsql/PostgreSQL and the configured database. This provides source model vs migration history vs applied-migration state evidence. No data was mutated, no migration was applied, and no destructive schema operation was attempted.

## 26. Runtime Verification

**Classification: FAIL**

Locally proven runtime paths:

- ASP.NET TestServer routing and DI for domain reads/writes, verification commands, SSL reads/commands, authorization context, error mapping, and tenant isolation.
- Real EF model construction and live configured PostgreSQL migration inspection.
- DNS resolver behavior through controlled DNS/HTTP boundaries.
- Admin and storefront production bundle construction.

Not proven:

- Public DNS ownership change.
- A real merchant HTTPS well-known response.
- Real CA/Caddy account/order/challenge/status/revocation.
- Real Caddy validation/reload/serving.
- Public external SNI certificate observation.
- Multi-instance crash/lease/fencing behavior under real PostgreSQL and Hangfire load.

The provider stages are not only unverified: they are source-proven non-executing. Therefore the real Create -> Verify -> Provision -> Issue -> Activate chain is broken and runtime classification is **FAIL**.

## 27. Test Verification

**Classification: PASS WITH OBSERVATIONS**

Final evidence after remediation:

| Command/suite | Result |
| --- | --- |
| `dotnet build PointOfSale.sln --no-restore --disable-build-servers -p:UseSharedCompilation=false` | PASS, 0 errors; full rebuild emitted 289 existing warnings |
| Focused backend `CustomDomains` + `Infrastructure.DnsResolver` | PASS: 246/246 |
| Admin domain frontend `vitest --run src/modules/storefront/domains` | PASS: 8 files, 78/78 |
| Admin frontend `npm run build` | PASS |
| Storefront full Vitest suite | PASS: 27 files, 250/250 |
| Storefront `tsc && vite build` | PASS with bundle/chunk and browserslist warnings |
| EF migrations list | PASS; all known migrations applied |
| EF pending model changes | PASS; none |

The focused backend set includes unit, service, HTTP TestServer, persistence/source-architecture, lifecycle, failure recovery, idempotency, tenant isolation, and concurrency regressions. It uses fake provider/edge/probe implementations for successful external operations; those tests prove orchestration and must not be read as real infrastructure certification.

A full unfiltered .NET test run was not executed because the repository contains a very large unrelated test surface. The solution and complete test project compile, and every relevant Domain/DNS test is green.

## 28. Defects Found

**Classification: FAIL**

| ID | Severity | Classification | Finding |
| --- | --- | --- | --- |
| D510-01 | Blocking | ARCHITECTURAL GAP | Production `ICertificateProvider` cannot execute order, authorization, challenge, status, or revocation. |
| D510-02 | High | REAL BUG | HTTPS ownership verification could connect to loopback/RFC1918/link-local/metadata destinations and was vulnerable to DNS rebinding. |
| D510-03 | Medium | REAL BUG | DNS cache options admitted TTL values above the constitutional 300-second maximum. |
| D510-04 | High | REAL BUG | Direct Draft Primary creation was allowed although verification rejects Primary and Primary requires edge-proven SSL. |
| D510-05 | High | REAL BUG | Primary promotion accepted `Verified` without a linked active certificate. |
| D510-06 | Critical | REAL BUG | Expiry/revocation degradation could leave invalid certificate authority represented as Primary. |
| D510-07 | High | REAL BUG | Several authoritative events were staged after source-state save, breaking atomic outbox requirements. |
| D510-08 | Medium | REAL BUG | Certificate 429 responses omitted the HTTP `Retry-After` header. |
| D510-09 | Medium | REAL BUG | D03 advertised direct Primary/redirect/provider choices that were rejected or ignored by backend contracts. |
| D510-10 | Medium | REAL BUG | D06 generated a new key after uncertain transport failure and displayed success-style action text for already-failed accepted operations. |
| D510-11 | Medium | REAL BUG | Certificate metrics used domain/certificate/operation GUIDs as unbounded metric tags. |
| D510-12 | High | REAL BUG | Readiness had no explicit signal that the configured certificate provider was non-executing. |
| D510-13 | Medium | REAL BUG | Successful HTTPS verification responses had no small response-body cap. |
| D510-14 | Low | DESIGN OBSERVATION | Replaced old certificate generations remain in `Renewing` rather than an explicit Retired state. |
| D510-15 | Medium | VALIDATION GAP | No real CA/Caddy/SNI or multi-node worker environment was available. |

## 29. Defects Fixed

**Classification: PASS**

Fixed D510-02 through D510-13:

- Added a direct, proxy-disabled, DNS-pinned public-address connector for HTTPS verification; expanded blocked special ranges and preserved TLS validation.
- Added a 4096 response cap and canonical `RESPONSE_TOO_LARGE` failure.
- Enforced and hard-clamped 300-second positive/negative cache TTL limits.
- Rejected direct Primary creation and removed misleading D03 options.
- Required SslActive plus a linked tenant/domain-owned Active certificate for primary promotion.
- Demoted invalid SslActive/Primary authority to Alias + Verified and cleared the link after expiry/immediate revocation.
- Moved authoritative event publication into the same pre-save EF outbox transaction.
- Added `Retry-After` to certificate rate-limit responses and an HTTP regression.
- Reused D06 idempotency keys after uncertain transport loss and corrected failed-operation messaging.
- Removed unbounded GUID/generation metric dimensions.
- Added explicit provider readiness health that fails when live execution is absent.
- Added/updated backend and frontend regressions for these invariants.

D510-01 was not safely implementable without selecting and integrating a real ACME/Caddy execution mechanism, defining external key/account custody, and testing against authorized infrastructure. Implementing a fake or unverified success would violate the mission.

## 30. Remaining Observations

**Classification: PASS WITH OBSERVATIONS**

- An explicit `Retired` certificate status would improve generation history but requires an intentional persistence contract change.
- The Caddy health check is a configuration/filesystem check; provider readiness now separately exposes the blocking adapter state.
- The full .NET repository test suite was not run, though the entire solution/test project compiles and all Domain/DNS tests pass.
- The repository emits many unrelated compiler/analyzer warnings on a clean rebuild.
- Both frontends emit non-blocking bundle-size/dependency-age or mixed-import warnings.
- Some older certification text describes now-remediated behavior and should not override this report or current source.

## 31. External Validation Limitations

**Classification: ENVIRONMENT BLOCKED**

The environment did not provide an authorized public domain, public DNS zone, ACME staging account execution, real Caddy edge, public SNI endpoint, or multiple deployed API/Hangfire instances. Therefore the following were not claimed:

- Real TXT/CNAME propagation timing.
- Real HTTPS-file verification against merchant infrastructure.
- Real HTTP-01 or TLS-ALPN-01 execution.
- CA order issuance or revocation.
- Caddy config validation/reload and served-route observation.
- Public SNI identity proof.
- Real crash windows or multi-node lease/fencing contention.

These limitations alone would justify `PASS WITH OBSERVATIONS` if source contained a complete provider. They do not explain away D510-01: the concrete provider source itself is non-executing, which forces `FAIL`.

## 32. Constitutional Compliance Matrix

**Classification: FAIL**

| Law | Source evidence | Current status | Violation/remediation | Regression evidence |
| --- | --- | --- | --- | --- |
| Tenant-owned domain/certificate access | tenant predicates, composite relations, controller service context | PASS | No violation found | cross-tenant HTTP/service tests |
| Globally normalized host uniqueness | normalizer, repository checks, unique migration index | PASS | No violation found | domain persistence/write tests |
| Primary only after edge-proven SSL | DOMAIN-5.1, promotion service | PASS after fix | Direct Primary and Verified promotion blocked | new create/promotion tests |
| Invalid certificate cannot remain Primary | DOMAIN-5.1 degradation rules | PASS after fix | Expiry/revocation demote to Alias + Verified | new renewal/revocation tests |
| DNS business layer is provider-neutral | `IDnsResolver` in Core, implementation in Infrastructure | PASS | No provider leakage | DNS architecture/tests |
| DNS cache TTL <= 300 seconds | resolver options/cache | PASS after fix | Option validation and hard clamp | DNS option tests |
| HTTP verification is SSRF-safe | pinned public connector | PASS after fix | Proxy/redirect/private/special ranges blocked | endpoint guard tests |
| Provider boundary is neutral | `ICertificateProvider` contracts | PASS | No ACME library leakage | DOMAIN-5.3 tests |
| Production provider executes real operations | concrete provider | **FAIL** | All operational methods return execution-out-of-scope | provider test and health test prove failure |
| No certificate secret persistence | entities/config/migrations/DTO scan | PASS | No private material fields | DOMAIN-5.2/security tests |
| Caddy upstream is trusted, typed, and idempotent | Caddy edge integration | PASS in source | No tenant arbitrary upstream/raw config | Caddy tests |
| Activation requires external SNI proof | activation service/probe | PASS in source; NOT VERIFIED externally | No bypass found | route/SNI failure tests |
| Valid current cert survives replacement failure | renewal/revocation services | PASS | Authority preserved until proven replacement | renewal/revocation tests |
| Durable idempotency/fencing/leases | operation entity/config/services | PASS WITH OBSERVATIONS | Multi-node load not executed | persistence/concurrency tests |
| Authoritative events are atomic with state | MassTransit EF outbox publisher | PASS after fix | Events moved before same save | source architecture + focused tests |
| Controller is thin; services own lifecycle/audit/events | controller/services | PASS | No entity-owned controller mutation | HTTP/service tests |
| Frontend uses one API service boundary | domain module service/hooks | PASS | No production mock path | service/hook/page tests |
| Frontend renders server-authoritative state | D05/D06 hooks/pages | PASS after fix | Failed response no longer success-styled | D06 regression tests |
| Metrics use bounded dimensions | certificate telemetry | PASS after fix | GUID/generation tags removed | source regression assertion |
| Dependency health reflects reality | DNS/Caddy/renewal/provider health | PASS after fix | Non-executing provider now unhealthy | provider health test |

The single failed constitutional law is blocking and determines the overall certification.

## 33. Final Risk Assessment

**Classification: FAIL**

| Risk | Level | Consequence | Required closure |
| --- | --- | --- | --- |
| Non-executing production provider | Critical | No real certificate issue/status/revoke path; full custom-domain TLS lifecycle cannot operate | Implement a concrete, fail-safe ACME/Caddy provider with external secret custody and authorized staging proof |
| Real Caddy/SNI untested | High | Static correctness may differ from deployed process/network/storage behavior | Run controlled staging issue/route/SNI/renew/revoke campaign |
| Multi-node concurrency untested | Medium | Lease/fencing assumptions are not load/chaos-proven | Exercise two API instances and two Hangfire workers against PostgreSQL |
| No explicit Retired status | Low | Historical status is less expressive | Consider a future schema/enum wave |
| Existing warnings/bundle size | Low | Maintenance/performance noise outside Domain correctness | Address separately without coupling to this certification |

No known tenant-isolation violation, private-key persistence, SNI bypass, or active invalid-primary bypass remains in the locally audited source. The critical provider risk alone is sufficient to block production certification.

## 34. Final Certification

**Final classification: FAIL**

The implementation is **not certified as full Domain & SSL/TLS end-to-end production-ready**.

Locally verifiable Domain management, DNS verification, persistence, API, UX, security, idempotency, outbox, and failure-preservation gates are green after remediation. External Caddy/CA/SNI validation is unavailable and is explicitly not claimed.

The decisive blocker is current source reality: the production `ICertificateProvider` adapter cannot create orders, retrieve authorization/challenges, poll status, or revoke certificates. This prevents real provisioning, activation reachability, renewal, and revocation. The provider readiness check now exposes this as unhealthy.

Certification may be reconsidered only after:

1. A real provider adapter is implemented without application private-key/credential persistence.
2. HTTP-01 and/or TLS-ALPN-01 execution is demonstrated; DNS-01 remains explicitly out of scope unless separately designed.
3. Provider retry-after, idempotency, recovery, and revocation behavior pass integration tests.
4. Authorized staging proves Create -> Verify -> Provision -> Issue -> Caddy reconcile -> external SNI -> Active.
5. Staging proves renewal replacement, old-authority preservation, retirement, revocation, expiry degradation, and recovery.
6. Multi-instance API/Hangfire contention validates durable leases, fencing, and uniqueness.
7. All local gates recorded in this report remain green.

Until those conditions are met, `PASS`, `PASS WITH OBSERVATIONS`, and production/runtime certification would be inaccurate.

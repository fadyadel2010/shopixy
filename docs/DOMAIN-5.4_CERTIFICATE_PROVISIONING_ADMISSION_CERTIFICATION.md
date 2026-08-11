# DOMAIN-5.4 Certificate Provisioning / Admission Certification

## 1. Executive Summary

OBSERVED REALITY: DOMAIN-5.2 provides `SslCertificate` and `SslCertificateOperation`; DOMAIN-5.3 provides provider-neutral `ICertificateProvider` contracts and a Caddy-managed ACME foundation shell.

IMPLEMENTED CHANGE: Added the DOMAIN-5.4 application orchestration boundary for verified-domain certificate provisioning admission, durable operation claim, provider-neutral issuance request construction, provider result normalization, safe metadata persistence, audit, events, and API entry.

VALIDATION EVIDENCE: API build passed; focused DOMAIN-5.4 tests passed 8/8; combined DOMAIN-5.4/5.3/5.2/DOMAIN-4 regression passed 124/124.

REMAINING FUTURE WORK: DOMAIN-5.5 still owns Caddy edge integration, certificate deployment, SNI proof, edge activation, and `SslActive` transition.

## 2. Pre-Implementation Reality

OBSERVED REALITY: `CertificateOperationType` includes Provisioning, Renewal, Retry, Revocation, Activation, MetadataSync. `CertificateOperationPhase` includes Admission, ProviderRequest, Authorization, MetadataSync, ActivationProbe, RenewalWindow, Revocation, RetryBackoff, EdgeDeactivation. `CertificateStatus` includes Pending, Requested, Authorizing, Issued, Activating, Active, Renewing, Revoked, Expired, Failed.

IMPLEMENTED CHANGE: Reused those values; no duplicate operation table or alternate state machine was added.

VALIDATION EVIDENCE: `Domain54CertificateProvisioningAdmissionTests` asserts operation phase/status and certificate status persistence.

REMAINING FUTURE WORK: Later waves must implement activation and renewal/revocation flows.

## 3. Admission Model

OBSERVED REALITY: `Domain` carries tenant, hostname, type, lifecycle, verification timestamp, soft-delete flag, and SSL certificate pointer.

IMPLEMENTED CHANGE: `CertificateProvisioningService` fails closed for missing tenant/domain, wrong tenant, deleted domain, unverified lifecycle, invalid hostname, reserved host, unsupported type, active certificate conflict, in-progress operation conflict, invalid provider config, invalid generation, and missing idempotency key.

VALIDATION EVIDENCE: Tests cover unverified, deleted, wrong tenant, reserved, unsupported type, and invalid provider config rejection before provider calls.

REMAINING FUTURE WORK: DOMAIN-5.5 edge admission policy remains outside this wave.

## 4. Certificate Generation Model

OBSERVED REALITY: DOMAIN-5.2 unique indexes freeze `(TenantId, DomainId, Generation)`.

IMPLEMENTED CHANGE: New provisioning computes generation as max existing non-deleted generation plus one; existing active/failed generations are never overwritten.

VALIDATION EVIDENCE: Replacement-generation test verifies generation 2 is created while generation 1 is preserved.

REMAINING FUTURE WORK: Activation wave must bind the winning issued generation to the domain only after edge proof.

## 5. Operation/Idempotency Model

OBSERVED REALITY: `SslCertificateOperation` is the durable idempotency and fencing boundary.

IMPLEMENTED CHANGE: The claim correlates TenantId, DomainId, CertificateId, OperationId, OperationType, Generation, and IdempotencyKey; provider request reference is deterministic from OperationId and persisted before provider order creation.

VALIDATION EVIDENCE: Idempotency replay and concurrent same-key tests create one operation, one certificate, and one provider order.

REMAINING FUTURE WORK: Cross-process protection relies on the existing PostgreSQL unique indexes in real DB execution; unit tests also use an in-process lock for deterministic local concurrency.

## 6. Provider Orchestration

OBSERVED REALITY: DOMAIN-5.3 exposes `EnsureAccountAsync`, `CreateOrderAsync`, and `GetOperationStatusAsync`.

IMPLEMENTED CHANGE: Provisioning uses only `ICertificateProvider`, builds `CertificateIssuanceRequest`, and resumes via existing provider operation reference when present.

VALIDATION EVIDENCE: Architecture test asserts provider abstraction usage and absence of ACME library references.

REMAINING FUTURE WORK: The real Caddy-managed provider still returns `CERTIFICATE_PROVIDER_EXECUTION_OUT_OF_SCOPE`; fake provider tests certify orchestration behavior.

## 7. Authorization/Challenge Model

OBSERVED REALITY: DOMAIN-5.3 `CertificateChallengePolicy.Default` is HTTP-01 plus TLS-ALPN-01 and rejects DNS-01.

IMPLEMENTED CHANGE: Provisioning always passes `CertificateChallengePolicy.Default`; DNS-01 is not requested.

VALIDATION EVIDENCE: Boundary test verifies `CertificateChallengePolicy.Default` usage and no `Dns01` reference in the service.

REMAINING FUTURE WORK: Actual challenge serving is edge-owned by DOMAIN-5.5.

## 8. Persistence Model

OBSERVED REALITY: DOMAIN-5.2 already has safe certificate metadata columns, provider refs, operation refs, retry fields, and audit linkage.

IMPLEMENTED CHANGE: Persisted only safe metadata: provider refs, hostname, SAN JSON, issuer, subject, thumbprint/fingerprint, serial number, validity timestamps, status, generation, and operation correlation.

VALIDATION EVIDENCE: Persistence test checks issued metadata and scans serialized certificate persistence for private/challenge terms.

REMAINING FUTURE WORK: No certificate/private-key material is stored; edge storage remains DOMAIN-5.5.

## 9. Failure/Retry Model

OBSERVED REALITY: DOMAIN-5.3 provider errors carry canonical category, code, retryability, and safe message.

IMPLEMENTED CHANGE: Provider failures transition certificate/operation to Failed/RetryBackoff, persist normalized code, safe reason, retryability, and retry timestamp for retryable failures.

VALIDATION EVIDENCE: Rate-limit provider test persists `FAKE_RATE_LIMITED` and `Retryable=true`.

REMAINING FUTURE WORK: Dedicated retry scheduling belongs to later waves.

## 10. Recovery Model

OBSERVED REALITY: Distributed provider/DB execution cannot be one ACID transaction.

IMPLEMENTED CHANGE: The service first persists a durable operation and deterministic provider request reference, then calls the provider; retries inspect existing operation and reuse provider references instead of blindly creating a new order.

VALIDATION EVIDENCE: Idempotency and concurrency tests prove replay does not create duplicate provider orders.

REMAINING FUTURE WORK: Later scheduler/resumption waves can pick up persisted Running/RetryBackoff operations.

## 11. Tenant Isolation

OBSERVED REALITY: Domain/certificate tables are tenant-owned with query filters.

IMPLEMENTED CHANGE: Provisioning loads domains with `IgnoreQueryFilters` only while explicitly filtering by `TenantId` and `DomainId`; wrong-tenant access returns not found and provider is not called.

VALIDATION EVIDENCE: Wrong-tenant admission test rejects before operation/provider work.

REMAINING FUTURE WORK: Background workers must revalidate tenant/domain ownership from persisted IDs.

## 12. Authorization

OBSERVED REALITY: Admin domain controller is `[Authorize(Roles = "Owner,Admin")]`.

IMPLEMENTED CHANGE: Added the provisioning command to the same controller and kept it thin: tenant validation, actor extraction, service invocation, HTTP mapping.

VALIDATION EVIDENCE: Controller boundary test asserts no direct provider dependency in the controller.

REMAINING FUTURE WORK: Fine-grained permission expansion, if any, belongs to future admin-command waves.

## 13. Audit/Event Model

OBSERVED REALITY: `DomainAuditEntry` is append-only and `IEventPublisher` is the event path.

IMPLEMENTED CHANGE: Added safe audit actions and safe certificate events: provisioning started, authorization started, issued, pending activation, failed.

VALIDATION EVIDENCE: Tests assert `CertificateIssuedEvent` and `CertificatePendingActivationEvent` are published and no Active event is emitted.

REMAINING FUTURE WORK: Edge activation events are DOMAIN-5.5.

## 14. Observability

OBSERVED REALITY: Existing logging and provider scopes already include safe provider correlation fields.

IMPLEMENTED CHANGE: Service persists correlation id, operation id, generation, provider refs, phase, status, outcome, and canonical failure codes.

VALIDATION EVIDENCE: Response and persisted operation fields are asserted in tests.

REMAINING FUTURE WORK: Dedicated metrics can be expanded when real edge execution exists.

## 15. Security/Secret Boundary

OBSERVED REALITY: DOMAIN-5.1 forbids private key, PEM, PFX, account key, challenge token, key authorization, and provider credentials in DB/log/audit/event/DTO.

IMPLEMENTED CHANGE: No secret-bearing fields were added; challenge secrets are never requested or persisted by the application service.

VALIDATION EVIDENCE: Secret scan and persistence serialization tests found no private material in provisioning persistence.

REMAINING FUTURE WORK: Caddy storage hardening remains edge work.

## 16. API Boundary

OBSERVED REALITY: Admin domain routes live under `/api/admin/storefront/domains`.

IMPLEMENTED CHANGE: Added `POST /api/admin/storefront/domains/{id}/ssl/provision`.

VALIDATION EVIDENCE: API build passed.

REMAINING FUTURE WORK: Additional renew/retry/revoke/status APIs belong to later waves.

## 17. Frontend Boundary

OBSERVED REALITY: Existing frontend domain pages are present but not part of 5.4 requirements.

IMPLEMENTED CHANGE: No frontend controls were added.

VALIDATION EVIDENCE: No frontend files were modified by this wave.

REMAINING FUTURE WORK: DOMAIN-5.9 owns frontend SSL integration.

## 18. Scheduler Boundary

OBSERVED REALITY: DOMAIN-4 verification scheduler exists.

IMPLEMENTED CHANGE: No certificate scheduler, Hangfire job, renewal job, or polling loop was added.

VALIDATION EVIDENCE: Boundary scan found no scheduler additions in provisioning files.

REMAINING FUTURE WORK: Dedicated scheduler/resumption belongs to later certificate waves.

## 19. DOMAIN-5.1 Compliance

OBSERVED REALITY: Caddy remains canonical TLS edge and activation requires SNI proof.

IMPLEMENTED CHANGE: Issuance success results in `CertificateStatus.Issued`, not Active; domain remains Verified and `Domain.SslCertificateId` is not mutated.

VALIDATION EVIDENCE: Test asserts `DomainLifecycleState.Verified` and null `SslCertificateId` after issued metadata persistence.

REMAINING FUTURE WORK: DOMAIN-5.5 owns Caddy/SNI/Active.

## 20. DOMAIN-5.2 Regression

VALIDATION EVIDENCE: Combined regression included `Domain52CertificatePersistenceFreezeTests`; passed.

## 21. DOMAIN-5.3 Regression

VALIDATION EVIDENCE: Combined regression included `Domain53AcmeFoundationTests`; passed.

## 22. DOMAIN-4 Regression

VALIDATION EVIDENCE: Combined regression included domain persistence, read, write, verification service, and verification scheduler tests; passed.

## 23. Migration Decision

OBSERVED REALITY: DOMAIN-5.2 persistence already had required columns and indexes.

IMPLEMENTED CHANGE: No migration was created.

VALIDATION EVIDENCE: Migration scan found no DOMAIN-5.4 migration.

REMAINING FUTURE WORK: None for schema in this wave.

## 24. Database Safety Evidence

OBSERVED REALITY: The request forbids creating/resetting/truncating/deleting database data.

IMPLEMENTED CHANGE: No DB migration or mutating DB probe was run.

VALIDATION EVIDENCE: Validation used unit/in-memory tests and read-only source/migration scans; existing DB was not mutated.

REMAINING FUTURE WORK: Real DB validation can be read-only when connection details are available; no schema change requires application.

## 25. Test Evidence

VALIDATION EVIDENCE:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -p:RestoreIgnoreFailedSources=true`: passed, 0 errors.
- `dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --no-dependencies -p:RestoreIgnoreFailedSources=true`: passed, 0 errors.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~Domain54CertificateProvisioningAdmissionTests"`: 8 passed, 0 failed.
- Combined regression filter: 124 passed, 0 failed.

## 26. Forbidden Boundary Scan

VALIDATION EVIDENCE: Scans of provisioning/application files found no Certes, ACMESharp, BouncyCastle, Caddyfile, DNS-01 request, private-key persistence, Active transition, SNI verification, renewal scheduler, or revocation workflow. Matches for `SslActive`/`Active` were pre-existing controller comments/error names and the deliberate conflict check preventing duplicate active certificate provisioning.

## 27. Known Observations

OBSERVED REALITY: The real infrastructure provider foundation still returns a normalized out-of-scope failure for execution.

IMPLEMENTED CHANGE: DOMAIN-5.4 orchestration handles that failure safely and proves successful flow through the existing test fake provider.

VALIDATION EVIDENCE: Provider failure and fake issuance tests passed.

REMAINING FUTURE WORK: DOMAIN-5.5 must implement real Caddy-managed provider/edge execution.

## 28. Certification

CERTIFICATION: PASS WITH OBSERVATIONS

Provisioning admission/orchestration is implemented with fail-closed admission, tenant isolation, deterministic generation, durable `SslCertificateOperation` idempotency, provider-neutral request construction, safe result persistence, retryable failure metadata, safe audit/events, API command, and regression coverage. Observation: real Caddy edge execution/activation remains intentionally outside this wave.

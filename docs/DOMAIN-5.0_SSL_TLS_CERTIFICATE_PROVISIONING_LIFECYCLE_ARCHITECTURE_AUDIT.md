# DOMAIN-5.0 SSL/TLS Certificate Provisioning & Lifecycle Architecture Audit

Audit date: 2026-08-09

Verdict: FAIL for DOMAIN-5 implementation readiness.

This is an audit-only artifact. No SSL/TLS implementation, code, migration, or database mutation was performed.

## 1. Executive Summary

SOURCE REALITY WINS.

Observed Shopixy has a certified DNS/custom-domain foundation and a partial certificate metadata model, but it does not have certificate provisioning, ACME orchestration, challenge handling, private-key storage, certificate activation, renewal, revocation, or proxy/CDN distribution implemented.

Observed capabilities:

- Domain verification is implemented end-to-end: Admin UI/API -> DomainWriteService -> DomainVerificationService -> IDnsResolver -> DnsClientResolver -> Verified -> host resolution -> tenant resolution -> storefront. Evidence: `DOMAIN-4.6_FULL_DNS_VERIFICATION_END_TO_END_CERTIFICATION.md:11-17`, `PointOfSale.API/Services/Storefront/DomainVerificationService.cs:178-225`, `PointOfSale.Infrastructure/Services/Dns/DnsClientResolver.cs:15-37`.
- `Domain`, `SslCertificate`, `CertificateStatus`, and `ProviderType` exist as persistence and DTO/read metadata. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:15-104`, `PointOfSale.Core/Enums/CertificateStatus.cs:7-34`, `PointOfSale.Core/Enums/ProviderType.cs:8-25`, `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs:842-858`.
- Host resolution currently serves `Verified`, `SslActive`, and `Primary` domains; it does not require SSL active. Evidence: `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:77-90`, `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:137-142`.

Observed missing capabilities:

- NO OBSERVED ACME IMPLEMENTATION.
- NO OBSERVED CERTIFICATE PROVISIONING SERVICE.
- NO OBSERVED CERTIFICATE PRIVATE-KEY STORAGE ARCHITECTURE.
- NO OBSERVED CERTIFICATE ACTIVATION OR EDGE DEPLOYMENT MECHANISM.
- NO OBSERVED RENEWAL OR REVOCATION ENGINE.
- NO OBSERVED SSL lifecycle background job.
- NO OBSERVED provider-rate-limit or certificate-concurrency guard.

The architecture is sufficiently understood to proceed to DOMAIN-5.1, but DOMAIN-5 implementation must not begin until the canonical SSL/TLS blueprint defines certificate ownership, private-key storage, ACME/provider abstraction, challenge strategy, edge activation, renewal, revocation, observability, and recovery.

## 2. Audit Scope

Audited:

- Prior DOMAIN authority documents: DOMAIN-0, DOMAIN-1, DOMAIN-1.5, DOMAIN-1.6, DOMAIN-2A through DOMAIN-4.6.
- Live backend source for domain entities, EF configuration, migrations, services, controller, DTOs, middleware, Hangfire, DNS resolver, and deployment configuration.
- Live frontend domain management pages, hooks, service clients, and tests.
- Staging reverse proxy and deployment docs.
- General SaaS/Shopify-style behavior from public documentation, separated from Shopixy facts.

Not performed:

- No certificate issuance.
- No ACME account setup.
- No database read/write probe beyond static source inspection.
- No migration generation.
- No source-code modification except this audit artifact.

## 3. Authority Documents

Required authority set was located in the repository:

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

Key authority statements:

- DOMAIN-1 defines SSL lifecycle as Infrastructure/Cert Manager responsibility and not StoreConfig ownership. Evidence: `docs/architecture/DOMAIN-1_STOREFRONT_CUSTOM_DOMAINS_CANONICAL_BLUEPRINT.md:54-69`.
- DOMAIN-1.5 freezes `SslCertificate` as a child certificate-state aggregate and says verification and certificates are independent lifecycle stages. Evidence: `docs/architecture/DOMAIN-1.5_STOREFRONT_CUSTOM_DOMAINS_PERSISTENCE_FREEZE.md:34-61`, `docs/architecture/DOMAIN-1.5_STOREFRONT_CUSTOM_DOMAINS_PERSISTENCE_FREEZE.md:432-436`.
- DOMAIN-1.6 labels the SSL flow as Constitutional Design at the time of that document, not observed implementation. Evidence: `docs/architecture/DOMAIN-1.6_STOREFRONT_CUSTOM_DOMAINS_RUNTIME_CONSTITUTION.md:898-927`.
- DOMAIN-4.1 says SSL starts only after verification and DNS verification is decoupled from SSL lifecycle. Evidence: `docs/architecture/domains/DOMAIN-4.1_DNS_VERIFICATION_CANONICAL_BLUEPRINT.md:52-69`, `docs/architecture/domains/DOMAIN-4.1_DNS_VERIFICATION_CANONICAL_BLUEPRINT.md:1386-1428`.

## 4. Current SSL Reality

OBSERVED REALITY:

- HTTPS redirection is enabled in ASP.NET Core for non-health requests. Evidence: `PointOfSale.API/Program.cs:3379-3381`.
- Caddy is present in staging and has a global ACME email option. Evidence: `deploy/staging/Caddyfile:1-3`.
- Caddy staging serves three static site blocks: API, admin SPA, and wildcard tenant storefront under `*.app-staging.yourdomain.com`. Evidence: `deploy/staging/Caddyfile:22-72`.
- `SslCertificate` metadata exists, but no certificate body, chain, PFX, PEM, CSR, private-key field, or encrypted secret reference exists. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:20-104`.
- Backend exposes read-only SSL metadata at `GET /api/admin/storefront/domains/{id}/ssl`. Evidence: `PointOfSale.API/Controllers/AdminDomainsController.cs:242-281`.

NO OBSERVED:

- ACME client code.
- X509 parsing/generation code.
- certificate request/CSR generation.
- certificate deployment to Caddy/NGINX/Cloudflare.
- certificate material storage.
- certificate activation engine.

## 5. Persistence Reality

OBSERVED REALITY:

`Domain` fields:

- `TenantId`, `HostName`, `NormalizedHostName`, `DomainType`, `LifecycleState`, `SslCertificateId`, verification fields, timestamps including `VerifiedAtUtc` and `SslActivatedAtUtc`, soft delete, `Version`, and `DomainHistoryJson`. Evidence: `PointOfSale.Core/Entities/Domain.cs:27-156`.
- Lifecycle helpers include `MarkVerified`, `MarkSslPending`, `MarkSslActive`, `MarkDisabled`, `MarkArchived`, and `MarkDeleted`. Evidence: `PointOfSale.Core/Entities/Domain.cs:224-283`.

`SslCertificate` fields:

- `Id`, `DomainId`, `Provider`, `Status`, `CertificateArn`, `Thumbprint`, `NotBeforeUtc`, `NotAfterUtc`, `LastRenewalAttemptUtc`, `NextRenewalUtc`, `FailureReason`, `CreatedAt`, `UpdatedAt`, `IsDeleted`, and `SslCertificateHistoryJson`. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:20-104`.

Enums:

- `CertificateStatus`: `Pending`, `Active`, `Expired`, `Revoked`, `Failed`. Evidence: `PointOfSale.Core/Enums/CertificateStatus.cs:7-34`.
- `ProviderType`: `LetsEncrypt`, `CustomUpload`, `SystemDefault`. Evidence: `PointOfSale.Core/Enums/ProviderType.cs:8-25`.

EF configuration:

- `Domains` has `CK_Domains_Primary_Host_Ssl`: `Primary` or `SslActive` requires `SslCertificateId IS NOT NULL`. Evidence: `PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs:43-48`.
- `SslCertificates` indexes exist for `DomainId`, `Status`, `NotAfterUtc`, `NextRenewalUtc`, `IsDeleted`, and filtered unique active certificate by `DomainId`. Evidence: `PointOfSale.Infrastructure/Data/Configurations/SslCertificateConfiguration.cs:101-128`.
- Migration creates `SslCertificates` with metadata columns only. Evidence: `PointOfSale.Infrastructure/Migrations/20260805215917_DOMAIN_2C_0_CreateDomainTables.cs:119-148`.

Gap:

- Persistence is not sufficient for full DOMAIN-5. It lacks issuer, subject, SANs, chain reference, certificate material reference, private-key reference, ACME order/account/challenge identifiers, activation status, deployed-at timestamp, renewal attempt counts, revocation reason/time, provider request ids, lock/fencing fields, and encrypted secret-pointer semantics.

## 6. Certificate Lifecycle Reality

OBSERVED state interaction:

- Verification success calls `domain.MarkVerified()` and publishes `DomainVerifiedEvent`. Evidence: `PointOfSale.API/Services/Storefront/DomainVerificationService.cs:178-225`.
- No observed subscriber/job reacts to `DomainVerifiedEvent` to create `SslCertificate` or transition to `SslPending`.
- `MarkSslPending()` and `MarkSslActive()` exist on `Domain`, but no production service calls them. Evidence: search hits only in entity/tests and no provisioning service; entity methods at `PointOfSale.Core/Entities/Domain.cs:237-251`.
- `DomainWriteService.DeleteDomainAsync` refuses deletion when linked certificate status is `Active`. Evidence: `PointOfSale.API/Services/Storefront/DomainWriteService.cs:1038-1048`.
- `PromotePrimaryDomainAsync` allows promotion from `Verified` or `SslActive` despite comments saying SslActive is required. Evidence: `PointOfSale.API/Services/Storefront/DomainWriteService.cs:1177-1182`.

CONSTITUTIONAL DESIGN:

- DOMAIN-1 target flow includes `Verified -> SslPending -> SslActive`, with failure downgrading to `Verified`. Evidence: `docs/architecture/DOMAIN-1_STOREFRONT_CUSTOM_DOMAINS_CANONICAL_BLUEPRINT.md:202-208`, `docs/architecture/DOMAIN-1_STOREFRONT_CUSTOM_DOMAINS_CANONICAL_BLUEPRINT.md:1075`.
- DOMAIN-1.6 target says SSL activation sets `SslActivatedAtUtc`, links `Domain.SslCertificateId`, and expiry downgrades to `Verified`. Evidence: `docs/architecture/DOMAIN-1.6_STOREFRONT_CUSTOM_DOMAINS_RUNTIME_CONSTITUTION.md:906-927`.

## 7. ACME Reality

NO OBSERVED ACME IMPLEMENTATION.

Evidence:

- No repo package scan found `Certes`, `BouncyCastle`, `ACME`, `X509Certificate`, `X509Certificate2`, `Certbot`, or certificate automation code in first-party source/config.
- Only first-party ACME-adjacent observed configuration is Caddy global email. Evidence: `deploy/staging/Caddyfile:1-3`.
- DOMAIN-4.2A explicitly excluded SSL certificate provisioning. Evidence: `DOMAIN-4.2A_DNS_RESOLVER_FOUNDATION_IMPLEMENTATION.md:98-104`.
- DOMAIN-4.3 says the verification engine does not reference SSL issuance or certificate provisioning. Evidence: `DOMAIN-4.3_DOMAIN_VERIFICATION_ENGINE_IMPLEMENTATION.md:22-24`.

## 8. Challenge Reality

OBSERVED SHOPIXY INFRASTRUCTURE:

- DNS ownership verification supports TXT, CNAME, and HTTPS file verification through `IDnsResolver`. Evidence: `PointOfSale.Infrastructure/Services/Dns/DnsClientResolver.cs:15-37`, `PointOfSale.API/Program.cs:382-437`.
- HTTP file verification uses default SSL validation and does not follow redirects. Evidence: `PointOfSale.API/Program.cs:425-435`.
- Caddy staging is the only observed TLS edge. Evidence: `deploy/staging/README.md:31-39`, `deploy/staging/Caddyfile:14-19`.

Challenge feasibility:

- HTTP-01: Feasible only if the custom domain A/AAAA resolves to the Shopixy edge and port 80 reaches the TLS/proxy layer. Caddy supports HTTP challenge by default when it owns the site block. External Caddy documentation states HTTP challenge requires port 80 to be externally accessible and is enabled by default. Source: [Caddy Automatic HTTPS](https://caddyserver.com/docs/automatic-https).
- TLS-ALPN-01: Feasible only if port 443 reaches the edge and the edge can present challenge certificates by SNI. Caddy supports TLS-ALPN challenge by default. Source: [Caddy Automatic HTTPS](https://caddyserver.com/docs/automatic-https), [RFC 8737](https://www.rfc-editor.org/rfc/rfc8737.html).
- DNS-01: Feasible only with DNS provider credentials or delegated `_acme-challenge` control. Current Shopixy DNS verification can read TXT/CNAME but cannot write DNS records. Caddy documentation says DNS challenge requires DNS provider credentials and is required for wildcard certificates. Source: [Caddy Automatic HTTPS](https://caddyserver.com/docs/automatic-https), [Caddy tls directive](https://caddyserver.com/docs/caddyfile/directives/tls).

Recommendation as CONSTITUTIONAL DESIGN:

- DOMAIN-5.1 must choose whether Shopixy-managed certificates are application-orchestrated ACME certificates distributed to Caddy, or Caddy-owned certificates with Shopixy as policy/data authority.
- HTTP-01/TLS-ALPN-01 are lowest-credential for individual hostnames if all customer hostnames route to Caddy.
- DNS-01 is required for wildcard certificates and for non-public edge reachability, but introduces DNS provider credential risk and tenant/provider complexity.

## 9. TLS Termination Reality

OBSERVED REALITY:

- Staging docs designate Caddy as HTTPS/reverse proxy. Evidence: `deploy/staging/README.md:31-39`.
- Caddy forwards to API upstream and preserves `Host`, `X-Forwarded-Proto`, and `X-Forwarded-For`. Evidence: `deploy/staging/Caddyfile:14-19`.
- ASP.NET Core trusts forwarded host/proto/for after configured known proxies. Evidence: `PointOfSale.API/Program.cs:99-130`.
- Kestrel/application uses HTTPS redirection but no observed custom-domain certificate loading. Evidence: `PointOfSale.API/Program.cs:3379-3381`.

CONSTITUTIONAL TARGET:

- Certificate Owner: Platform/Domain infrastructure, keyed by `TenantId` and `DomainId`.
- Certificate Provisioner: To be defined in DOMAIN-5.1.
- Certificate Storage: To be defined; must not be ordinary raw-key table storage.
- Certificate Distributor: To be defined.
- TLS Terminator: Observed staging is Caddy; production canonical model must explicitly confirm Caddy, Cloudflare, NGINX, Traefik, or another edge.

## 10. Certificate Deployment Reality

NO OBSERVED certificate deployment path.

Missing:

- no Caddy admin API integration.
- no file/volume replacement contract.
- no Kubernetes Secret or Docker secret contract.
- no certificate hot-reload trigger.
- no deployment transaction/fencing.
- no activation confirmation probe.

Operational limitation:

- Current architecture cannot prove zero-downtime activation for issued custom-domain certificates. DOMAIN-5.1 must define whether activation is Caddy automatic HTTPS, Caddy config/API reload, secret rotation, CDN API, or another mechanism.

## 11. Private Key Security

OBSERVED REALITY:

- `SslCertificate` does not store raw private keys. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:20-104`.
- Staging deployment docs require runtime secrets from environment variables, Kubernetes secrets, Azure Key Vault, AWS Secrets Manager, or equivalent. Evidence: `deploy/staging/README.md:22-29`, `deploy/staging/pointofsale.api.env.example:1-4`.
- No certificate private-key-specific storage, encryption, rotation, audit, or least-privilege model exists.

SECURITY FINDING:

- Absence of key storage is safer than unsafe storage, but DOMAIN-5 has no private-key architecture. Raw private keys must never be stored in ordinary application tables, DTOs, logs, JSON history, or audit snapshots.

CONSTITUTIONAL DESIGN:

- Private material must live in a dedicated secret manager, edge-managed certificate store, encrypted object store with KMS envelope encryption, or Caddy-managed storage with explicit filesystem permissions and backup/restore rules.
- Application database may store only opaque key/certificate references and safe metadata.

## 12. Tenant Isolation

OBSERVED REALITY:

- `Domain` carries `TenantId`. Evidence: `PointOfSale.Core/Entities/Domain.cs:29-35`.
- Domain list/detail/SSL reads filter by `TenantId` and `!IsDeleted`. Evidence: `PointOfSale.API/Services/Storefront/DomainReadService.cs:56-67`, `PointOfSale.API/Services/Storefront/DomainReadService.cs:420-428`.
- `ApplicationDbContext` filters `Domain` by tenant except System/Development, and filters `DomainAuditEntry` by tenant. Evidence: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:954-968`.
- `SslCertificate` has only a soft-delete query filter, not a tenant filter, and tenant isolation is reached through `Domain` joins/read paths. Evidence: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:963-964`.

Gap:

- A future direct certificate service/repository must never expose `SslCertificate` by certificate id alone. It must always require `(TenantId, DomainId, CertificateId)` or join through `Domain.TenantId`.
- Private key access must be stricter than metadata access and should be system-only.

## 13. State Machine

OBSERVED state map:

```
Domain:
Draft
  -> PendingVerification     (StartDomainVerification)
PendingVerification
  -> Verified                (DomainVerificationService success)
Verified
  -> Primary                 (PromotePrimaryDomain currently allows Verified)
SslActive
  -> Primary                 (PromotePrimaryDomain)
Primary
  -> SslActive               (Demoted previous primary)
Any non-primary active
  -> Archived/Deleted        (archive/delete subject to guards)

SslCertificate:
Pending | Active | Expired | Revoked | Failed
```

Observed gaps:

- No observed transition creates `SslCertificate`.
- No observed transition changes `CertificateStatus`.
- No observed transition links `Domain.SslCertificateId`.
- No observed transition clears `SslCertificateId`.

CONSTITUTIONAL TARGET state map:

| Target state | Entry condition | Exit condition | Owner | Side effects | Retry/terminal |
| --- | --- | --- | --- | --- | --- |
| Pending | Domain is Verified and provisioning record is created | ACME order/challenge starts or fails | CertificateProvisioningService | Insert metadata row, audit `SslProvisioningStarted` | Retryable |
| Requested | ACME/provider order accepted | Challenge authorization starts | CertificateProvider | Store provider order id/reference | Retryable with provider limits |
| Authorizing | Challenge material active | Provider validates or fails | CertificateProvider/ChallengeService | Publish challenge route/DNS material | Retryable until provider/order expiry |
| Issued | Provider returns certificate chain | Activation starts | CertificateProvisioningService | Store safe metadata and secret refs | Retry activation |
| Active | Edge serving certificate confirmed | Renewal window, revocation, expiry | CertificateActivationService | Set `Domain.SslCertificateId`, `SslActivatedAtUtc`, status Active | Non-terminal |
| Renewing | Active cert within renewal window | Renewed, failed, or cancelled | RenewalScheduler | Lock cert/domain, create replacement order | Retry with backoff |
| Renewed | Replacement issued and activated | Old cert retired | ActivationService | Rotate active certificate atomically | Non-terminal |
| Revoked | System/admin/security revokes | Terminal unless reissued | RevocationService | Revoke provider cert, deactivate edge binding | Terminal for cert row |
| Expired | `NotAfterUtc` passed or probe invalid | Reissue or downgrade | ExpiryMonitor | Mark status Expired, alert | Terminal for cert material |
| Failed | provisioning/renewal/revocation cannot proceed | Manual/system retry | Provisioning/Renewal service | Persist failure code safe for UI | Retryable or terminal by policy |

## 14. Renewal Architecture

OBSERVED REALITY:

- Hangfire is registered with PostgreSQL storage. Evidence: `PointOfSale.API/Program.cs:2925-2943`.
- Recurring jobs are registered by `HangfireInitializationHostedService`. Evidence: `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs:177-180`.
- Domain verification scheduler reuses Hangfire and has `[DisableConcurrentExecution]` and `[AutomaticRetry]`. Evidence: `PointOfSale.API/Services/Storefront/DomainVerificationSchedulerJob.cs:13-15`.
- No SSL renewal job is registered. Evidence: `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs:321-338` registers domain verification only for domain-specific jobs.
- `SslCertificate.NextRenewalUtc` and `LastRenewalAttemptUtc` exist but are not acted upon. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:64-74`.

CONSTITUTIONAL DESIGN:

- Use existing Hangfire. Do not introduce a second scheduler.
- Renewal threshold, retry schedule, max attempts, and backoff must be config-driven and not hardcoded in this audit.
- Renewal must be guarded by per-domain distributed lock/fencing and provider idempotency.

## 15. Expiry Behavior

OBSERVED REALITY:

- `SslCertificate.NotAfterUtc` exists.
- `DomainReadService` computes expiring soon at 30 days for dashboard/UI. Evidence: `PointOfSale.API/Services/Storefront/DomainReadService.cs:213-217`.
- No job marks certificates expired or changes domain state on expiry.

CONSTITUTIONAL DESIGN:

- DOMAIN-1.6 says expired cert downgrades `SslActive -> Verified` and removes SSL while the domain remains verified. Evidence: `docs/architecture/DOMAIN-1.6_STOREFRONT_CUSTOM_DOMAINS_RUNTIME_CONSTITUTION.md:919-927`, `docs/architecture/DOMAIN-1.6_STOREFRONT_CUSTOM_DOMAINS_RUNTIME_CONSTITUTION.md:1115`.
- DOMAIN-5.1 must decide exact behavior for activation failure, deployment failure, invalid served certificate, revoked certificate, and renewal exhaustion.

## 16. Revocation

OBSERVED REALITY:

- `CertificateStatus.Revoked` exists. Evidence: `PointOfSale.Core/Enums/CertificateStatus.cs:25-28`.
- No revoke API, service, provider call, audit event, UI mutation, or edge deactivation path exists.

CONSTITUTIONAL DESIGN:

- Revocation may be required for manual admin action, security incident response, certificate/key compromise, and domain deletion cleanup.
- Ordinary merchants should not retrieve private material or arbitrarily revoke another tenant certificate.
- Revocation must define storefront availability: remain `Verified` without SSL, move to `SslPending`, or disable serving.

## 17. API Reality

OBSERVED API:

- `GET /api/admin/storefront/domains/{id}/ssl` returns read-only `DomainSslDto`. Evidence: `PointOfSale.API/Controllers/AdminDomainsController.cs:242-281`.
- Domain controller endpoint inventory includes read endpoints and verification commands but no SSL write commands. Evidence: `PointOfSale.API/Controllers/AdminDomainsController.cs:31-50`, `PointOfSale.API/Controllers/AdminDomainsController.cs:660-708`.
- `IDomainWriteService` includes verification commands, but no provision/renew/revoke SSL commands. Evidence: `PointOfSale.API/Services/Storefront/IDomainWriteService.cs:51-87`.

Missing:

- `POST provision`.
- `POST renew`.
- `POST revoke`.
- `POST retry`.
- `GET certificate operations/audit`.
- admin-only activation/edge diagnostics.

## 18. Frontend Reality

OBSERVED REALITY:

- `SslManagementPage` is read-backed by `useDomainSsl`. Evidence: `PointOfSale.Web/src/modules/storefront/domains/pages/SslManagementPage.jsx:24-31`.
- It renders status, provider, thumbprint, dates, expiring-soon warning, and a disabled download button. Evidence: `PointOfSale.Web/src/modules/storefront/domains/pages/SslManagementPage.jsx:136-244`.
- `domainManagementService` has only `getDomainSsl` for SSL. Evidence: `PointOfSale.Web/src/modules/storefront/domains/services/domainManagementService.js:75-85`.
- `useDomainMutations` exports no SSL provisioning, renewal, revocation, or retry mutation. Evidence: `PointOfSale.Web/src/modules/storefront/domains/hooks/useDomainMutations.js:249-274`.

UX integrity issues:

- `AddDomainWizardPage` lets users select Let's Encrypt, Custom Upload, or System Default SSL provider, but `createDomain` request does not include SSL provider. Evidence: `PointOfSale.Web/src/modules/storefront/domains/pages/AddDomainWizardPage.jsx:142-173`, `PointOfSale.Web/src/modules/storefront/domains/pages/AddDomainWizardPage.jsx:322-330`.
- `DomainsOverviewPage` has "Renew SSL" with an empty handler. Evidence: `PointOfSale.Web/src/modules/storefront/domains/pages/DomainsOverviewPage.jsx:198-204`.
- `SslManagementPage` displays the route id as hostname instead of actual hostname because `DomainSslDto` has no host field. Evidence: `PointOfSale.Web/src/modules/storefront/domains/pages/SslManagementPage.jsx:109-132`, `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs:842-858`.

## 19. Background Jobs

OBSERVED REALITY:

- Hangfire is the current background job system. Evidence: `PointOfSale.API/Program.cs:2925-2943`.
- Domain verification scheduler is bounded, cancellation-aware, metric-instrumented, and uses concurrency controls. Evidence: `PointOfSale.API/Services/Storefront/DomainVerificationSchedulerJob.cs:13-15`, `PointOfSale.API/Services/Storefront/DomainVerificationSchedulerJob.cs:76-107`, `PointOfSale.API/Instrumentation/DomainVerificationSchedulerTelemetry.cs:24-55`.
- DOMAIN-4.4 certifies no second scheduler was introduced. Evidence: `DOMAIN-4.4_VERIFICATION_SCHEDULER_CERTIFICATION.md:15-25`.

CONSTITUTIONAL DESIGN:

- SSL provisioning/renewal should integrate with Hangfire using the existing pattern.
- Need per-certificate/domain distributed lock beyond process-local dictionaries.

## 20. Observability

OBSERVED REALITY:

- Domain verification scheduler has metrics for runs, candidates, processed, success, failure, skipped, duration, and active jobs. Evidence: `PointOfSale.API/Instrumentation/DomainVerificationSchedulerTelemetry.cs:24-55`.
- DNS resolver logs query latency/status. Evidence: `PointOfSale.Infrastructure/Services/Dns/DnsClientResolver.cs:99-115`.
- No certificate metrics/logging exist.

CONSTITUTIONAL DESIGN:

- Certificate operations need metrics/logs for provision/renew/revoke/activate attempts, successes, failures, expiry risk, provider throttling, and edge deployment status.
- Never log private keys, ACME credentials, challenge secrets, raw cert private material, or full provider secret payloads.

## 21. Disaster Recovery

OBSERVED REALITY:

- There is no certificate backup/restore/reissue runbook.
- Staging docs mention generic secret injection but no certificate/key recovery. Evidence: `deploy/staging/README.md:22-29`.
- If DB restore loses `SslCertificate` metadata, current app has no recovery engine.
- If edge certificate store is lost, current app has no reissue/deploy engine.
- If ACME account state is lost, current app has no account recovery model.

CONSTITUTIONAL REQUIREMENTS:

- Define source of truth for certificate metadata vs certificate material.
- Define whether certificates are recoverable from backup or reissued.
- Define ACME account backup/recreation and rate-limit impact.
- Define restore order: database, secret store, edge store, provider account.

## 22. Provider Rate Limits

OBSERVED REALITY:

- No provider limit model exists.
- No duplicate issuance prevention exists beyond DB unique active certificate row. Evidence: `PointOfSale.Infrastructure/Data/Configurations/SslCertificateConfiguration.cs:122-128`.
- No issuance retry policy exists.

CONSTITUTIONAL DESIGN:

- Provider limits must be centralized in provider abstraction and scheduler policy.
- Duplicate issuance must be prevented with database constraints, advisory locks or equivalent distributed locks, provider idempotency keys, and in-progress state.

## 23. Concurrency

OBSERVED REALITY:

- Verification has per-domain in-process semaphores and service-level lifecycle gates. Evidence: `PointOfSale.API/Services/Storefront/DomainVerificationService.cs:70-71`, `PointOfSale.Tests/Domain/DomainVerificationServiceTests.cs:450-492`.
- Scheduler uses Hangfire `[DisableConcurrentExecution]`. Evidence: `PointOfSale.API/Services/Storefront/DomainVerificationSchedulerJob.cs:13-15`.
- `Domain.Version` uses PostgreSQL xmin rowversion. Evidence: `PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs:132-137`.
- No SSL concurrency guard exists.

CONSTITUTIONAL DESIGN:

- DOMAIN-5 must protect against two provisioning jobs, scheduled renew plus manual renew, revoke plus renew, activation plus replacement, and multi-instance duplicate issuance.
- Process-local semaphores are insufficient for multi-instance SSL issuance.

## 24. Domain/SSL Ownership

OBSERVED/CONSTITUTIONAL BOUNDARY:

- Domain owns identity, hostname, tenant, verification, lifecycle, redirects, and SSL linkage. Evidence: `PointOfSale.Core/Entities/Domain.cs:22-186`.
- `SslCertificate` owns certificate lifecycle metadata. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:15-114`.
- StoreConfig must not own domains/certificates. Evidence: `docs/architecture/DOMAIN-1.5_STOREFRONT_CUSTOM_DOMAINS_PERSISTENCE_FREEZE.md:38-59`, `docs/architecture/DOMAIN-1.5_STOREFRONT_CUSTOM_DOMAINS_PERSISTENCE_FREEZE.md:432-434`.
- Theme must not own domain. Evidence: `docs/architecture/DOMAIN-1_STOREFRONT_CUSTOM_DOMAINS_CANONICAL_BLUEPRINT.md:653-682`.

DOMAIN-5 law:

- StoreConfig, Theme, CMS, Catalog, POS, Checkout, and Builder must not own certificate material or TLS state.

## 25. Host Resolution Coupling

Critical observed answer:

- Host resolution requires `LifecycleState` in `{Verified, SslActive, Primary}`.
- It does not require `SslActive`.

Evidence:

- `PersistenceDomainResolver` comment and gate: `Verified, SslActive, or Primary`. Evidence: `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:44-49`, `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:77-90`, `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:137-142`.
- Program comments say HostResolution performs no DNS/SSL verification. Evidence: `PointOfSale.API/Program.cs:3436-3451`.

Architectural implication:

- Verified domains are servable before SSL activation. DOMAIN-5 must decide whether HTTP storefront service without TLS is acceptable behind the edge, whether edge blocks HTTPS until cert issuance, and whether `Verified` should continue resolving after certificate failure.

## 26. CDN/Proxy Architecture

OBSERVED:

- Caddy exists for staging.
- No observed Cloudflare API/config.
- No observed NGINX/Traefik/HAProxy config.
- No observed Kubernetes ingress/Secret TLS model.
- No observed CDN certificate API integration.

Canonical ownership model is undefined and must be decided in DOMAIN-5.1.

Options:

- Caddy-managed certificates: Caddy owns issuance/storage/renewal; Shopixy owns eligibility/policy and dynamic config/on-demand TLS policy.
- Shopixy-managed certificates: Shopixy ACME service owns issuance/storage/renewal; Caddy is certificate consumer/terminator.
- CDN-managed certificates: CDN owns issuance/edge serving; Shopixy stores provider references and policy.

## 27. Threat Model

| Threat | Classification | Observed control | Gap |
| --- | --- | --- | --- |
| Private key theft | Critical | No raw key storage exists | No key architecture, access control, audit, rotation |
| Certificate substitution | Critical | None observed | Need signed/locked activation, provider validation, edge verification |
| Tenant certificate confusion | Critical | Domain reads filter by tenant | Certificate direct access lacks tenant filter if introduced carelessly |
| Domain ownership bypass | Critical | Verified gate via DNS/HTTP engine | SSL provisioning must require VerifiedAtUtc and DomainId/TenantId |
| ACME account compromise | Critical | No ACME account exists | Need account secret storage/rotation/audit |
| Challenge hijacking | High | DNS verification is read-only | ACME challenge ownership undefined |
| Unauthorized renewal | High | No renew API | Need tenant/admin/system authorization |
| Unauthorized revocation | High | No revoke API | Need platform-admin/security boundary |
| Certificate replay | High | None | Need provider refs, thumbprints, activation fencing |
| Stale certificate activation | High | None | Need issuance generation/fencing |
| Race-condition activation | High | Domain version only | Need distributed cert locks |
| Expired certificate serving | High | Dashboard expiring-soon read | No expiry monitor/enforcement |
| Certificate metadata leakage | Medium | DTO excludes private material | Future DTOs must remain metadata-only |
| Backup leakage | Critical | No private material in DB | Need secret-store backup encryption policy |

## 28. Migration Readiness

Observed schema is not sufficient for full DOMAIN-5.

Likely required schema/design additions, not to be implemented in this audit:

- Certificate material secret reference.
- Provider account/order/challenge identifiers.
- Issuer/subject/SANs/chain metadata or references.
- Activation/deployment status fields.
- Renewal attempt count/backoff fields.
- Revocation timestamp/reason/actor.
- Provider error code and safe diagnostic fields.
- Distributed lock/fencing/idempotency fields.
- Indexes for renewal due, activation pending, failed retry, expiring window.

Existing schema coherence:

- Domain/SslCertificate tables, indexes, and snapshot are covered by tests. Evidence: `PointOfSale.Tests/Domain/DomainPersistenceTests.cs:145-151`, `PointOfSale.Tests/Domain/DomainPersistenceTests.cs:320-336`.
- Existing EF model drift is mentioned in DOMAIN-4.6 and DOMAIN-4.4 as outside those phases. Evidence: `DOMAIN-4.6_FULL_DNS_VERIFICATION_END_TO_END_CERTIFICATION.md:7-7`, `DOMAIN-4.4_VERIFICATION_SCHEDULER_CERTIFICATION.md:180-180`.

## 29. Test Readiness

Observed tests:

- Persistence schema/index coverage for Domain/SslCertificate. Evidence: `PointOfSale.Tests/Domain/DomainPersistenceTests.cs:36-65`, `PointOfSale.Tests/Domain/DomainPersistenceTests.cs:145-151`.
- Domain read SSL metadata tests. Evidence: `PointOfSale.Tests/Domain/DomainReadServiceTests.cs:342-400`.
- Verification success and concurrency tests. Evidence: `PointOfSale.Tests/Domain/DomainVerificationServiceTests.cs:22-70`, `PointOfSale.Tests/Domain/DomainVerificationServiceTests.cs:450-492`.
- Frontend SSL endpoint client test. Evidence: `PointOfSale.Web/src/modules/storefront/domains/services/domainManagementService.test.ts:76-81`.

Missing DOMAIN-5 tests:

- ACME provider contract tests.
- challenge generation/cleanup tests.
- private-key never-in-DTO/log/audit tests.
- provisioning state machine tests.
- activation/deployment tests.
- renewal scheduler tests.
- revocation authorization tests.
- tenant isolation for certificate commands.
- multi-instance/concurrency tests.
- provider rate-limit/backoff tests.
- edge health/probe tests.

## 30. Implementation Waves

Recommended waves:

1. DOMAIN-5.1 - Canonical SSL/TLS Blueprint
   Define certificate owner, provisioner, storage, distributor, TLS terminator, challenge strategy, state machine, security model, and edge integration.

2. DOMAIN-5.2 - Certificate Persistence Freeze
   Freeze schema/entity additions without storing raw private keys in ordinary tables.

3. DOMAIN-5.3 - Certificate Provider / ACME Foundation
   Provider abstraction, ACME account handling, safe challenge contracts, no domain lifecycle mutation yet.

4. DOMAIN-5.4 - Certificate Provisioning Engine
   Verified-domain admission, ACME order/challenge/issue flow, safe metadata, failure handling.

5. DOMAIN-5.5 - Certificate Activation / Edge Integration
   Caddy/CDN/proxy deployment, hot reload, activation probe, rollback.

6. DOMAIN-5.6 - Renewal Scheduler
   Hangfire integration, due scan, retries, provider limit protection, multi-instance lock.

7. DOMAIN-5.7 - Revocation and Failure Recovery
   Manual/system/security revocation, expiry monitor, downgrade behavior, recovery runbooks.

8. DOMAIN-5.8 - SSL Admin Commands and APIs
   provision/renew/revoke/retry/status endpoints with strict tenant/admin boundaries.

9. DOMAIN-5.9 - SSL Frontend Integration
   Replace fake controls with backend-backed actions; metadata-only UI.

10. DOMAIN-5.10 - End-to-End SSL Certification
   Full DNS -> Verified -> issued -> activated -> renewed -> revoked/expired test evidence.

## 31. Architecture Laws

DOMAIN-5 proposed laws:

1. SSL begins only after `Domain.VerifiedAtUtc` exists and domain belongs to the tenant.
2. DNS verification and SSL lifecycle remain decoupled.
3. `Domain` owns hostname and lifecycle; `SslCertificate` owns certificate metadata/lifecycle.
4. Private keys never enter ordinary application tables, DTOs, logs, audit snapshots, or frontend payloads.
5. Certificate commands require `(TenantId, DomainId)` and fail closed on mismatch.
6. TLS terminator ownership must be explicit before activation code exists.
7. Certificate activation must be externally verified at the edge before `SslActive`.
8. Renewal uses Hangfire; no second scheduler.
9. Multi-instance duplicate issuance must be prevented with distributed locking/fencing.
10. Provider rate limits are first-class constraints, not exception afterthoughts.
11. Host resolution does not perform DNS or SSL verification.
12. StoreConfig, Theme, CMS, Catalog, POS, Checkout, and Builder never own certificates.

## 32. Risks

Critical:

- TLS ownership and activation are currently undefined.
- Private-key storage architecture is absent.
- Persistence is insufficient for provider/challenge/activation/renewal/revocation lifecycle.
- Existing frontend contains SSL action illusions (`Renew SSL`, SSL provider selection) without backend authority.

High:

- Host resolution currently serves Verified domains without requiring SslActive.
- Promotion to Primary currently allows `Verified`, while DB constraint requires `SslCertificateId` for Primary; this may fail if no certificate is linked. Evidence: `PointOfSale.API/Services/Storefront/DomainWriteService.cs:1177-1182`, `PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs:43-48`.
- No certificate expiry/revocation monitor exists.
- No ACME/provider rate-limit model exists.

Medium:

- `DomainSslDto.ActivatedAtUtc` is always null when mapping real certs, despite `Domain.SslActivatedAtUtc` existing. Evidence: `PointOfSale.API/Services/Storefront/DomainReadService.cs:662-678`.
- `CertificateArn` is reused by `ParseSans` as if it might contain SAN JSON. Evidence: `PointOfSale.API/Services/Storefront/DomainReadService.cs:681-708`.

## 33. Certification

Final certification: FAIL.

Reasons:

- Certificate activation architecture is undefined.
- TLS terminator ownership is not canonically frozen for production.
- Private-key storage and key-management architecture are absent.
- Existing persistence cannot support full constitutional certificate lifecycle safely.
- ACME/provider/challenge/renewal/revocation functionality is absent.
- Tenant isolation for future certificate commands is not yet designed.

This is not a failure of DOMAIN-4. DOMAIN-4 remains certified for DNS verification. This is a DOMAIN-5 readiness failure: the system is ready for DOMAIN-5.1 architecture design, not for SSL/TLS implementation.

## Appendix A: Shopify / Platform Parity

Observed Shopixy:

- DNS verification and host resolution are implemented.
- SSL metadata exists.
- SSL provisioning/activation/renewal/revocation do not exist.

Constitutional Shopixy Target:

- Automatic platform-managed HTTPS after Verified.
- Certificate lifecycle tied to Domain/SslCertificate, not StoreConfig.
- Renewal, revocation, expiry recovery, and edge activation are platform-owned.
- Custom Upload remains a constitutional option through `ProviderType.CustomUpload`.

General SaaS/Shopify-style behavior:

- Shopify public docs state TLS certificates are provided free for domains added to Shopify and issued automatically after DNS points to Shopify; third-party domains may show SSL/TLS pending while issuance completes. Source: [Shopify secure connections](https://help.shopify.com/en/manual/domains/managing-domains/secure-connections).
- Shopify docs say third-party SSL certificates cannot be used with Shopify stores; Shopixy's constitutional target differs because it includes `CustomUpload`. Source: [Shopify secure connections](https://help.shopify.com/en/manual/domains/managing-domains/secure-connections).
- Shopify troubleshooting docs emphasize DNS correctness, CAA, DNSSEC, and unsupported proxy configurations as common TLS blockers. Source: [Shopify domain troubleshooting](https://help.shopify.com/en/manual/domains/troubleshoot-issues-with-domains?locale=en-US).

Do not treat Shopify internals as Shopixy facts.


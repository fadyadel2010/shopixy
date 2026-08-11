# DOMAIN-5.1 Canonical SSL/TLS Certificate Provisioning Lifecycle Blueprint

Blueprint date: 2026-08-09

Status: CANONICAL DESIGN - IMPLEMENTATION BINDING

This is an architecture-only artifact. No C# source, React/TypeScript/JavaScript source, migrations, database state, packages, ACME accounts, certificates, or infrastructure configuration were created or modified.

SOURCE REALITY WINS. OBSERVED REALITY below is existing implementation. CONSTITUTIONAL DECISION is the DOMAIN-5.1 target. IMPLEMENTATION REQUIREMENT is binding on later DOMAIN-5 waves. OPEN DECISION is used only for unresolved items; this blueprint intentionally resolves all implementation-critical DOMAIN-5.1 decisions.

## 1. Executive Constitution

OBSERVED REALITY:

- DOMAIN-5.0 found SSL/TLS is metadata/read-only only, with no observed ACME, provisioning, activation, renewal, revocation, private-key storage, or deployment path. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:13-31`.
- DOMAIN-4 is certified for DNS/custom-domain verification and must not be duplicated. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:17`, `DOMAIN-4.6_FULL_DNS_VERIFICATION_END_TO_END_CERTIFICATION.md:11-17`.
- `DomainVerificationService` marks a domain verified on successful ownership proof and publishes `DomainVerifiedEvent`; it does not start SSL provisioning. Evidence: `PointOfSale.API/Services/Storefront/DomainVerificationService.cs:178-225`.
- `Domain` already owns hostname identity, tenant ownership, verification timestamps, coarse lifecycle, and SSL linkage. Evidence: `PointOfSale.Core/Entities/Domain.cs:27-156`.
- `SslCertificate` currently owns only certificate lifecycle metadata. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:20-104`.

CONSTITUTIONAL DECISION:

- DOMAIN-4 owns domain ownership verification only.
- DOMAIN-5 owns certificate lifecycle after verification: certificate admission, provisioning workflow, edge admission, activation proof, metadata synchronization, renewal monitoring, revocation orchestration, expiry recovery, observability, and admin/API/UI contracts.
- SSL MUST NOT duplicate DNS verification logic. SSL may read the certified `Domain` state and verification evidence, but it must not reimplement DOMAIN-4 DNS/TXT/CNAME/HTTP ownership checks.
- `Domain` owns identity, hostname, tenant, domain lifecycle, redirects, canonical role, and active certificate linkage.
- `SslCertificate` owns certificate metadata, certificate lifecycle state, provider/edge references, activation/renewal/revocation metadata, safe failure diagnostics, and audit linkage.
- Certificate infrastructure owns ACME account interaction, order/challenge execution, certificate material generation/storage, renewal execution, and provider-specific limits.
- TLS edge owns TLS termination, certificate loading, ACME challenge serving, private-key custody, OCSP/stapling behavior where supported, and traffic forwarding.
- `StoreConfig`, Theme, CMS, Catalog, POS, Checkout, and Builder do not own certificates, private keys, TLS state, ACME accounts, challenge state, or edge activation state.

IMPLEMENTATION REQUIREMENT:

- Every DOMAIN-5 implementation must preserve the certified DOMAIN-4 chain:
  Admin UI -> Verification API -> DomainWriteService -> DomainVerificationService -> IDnsResolver -> DnsClientResolver -> Verified -> Host Resolution -> Tenant Resolution -> Storefront.
- DOMAIN-5 begins only after a domain is verified.

OPEN DECISION:

- None.

## 2. Canonical Certificate Ownership

OBSERVED REALITY:

- Staging has Caddy as HTTPS/reverse proxy and preserves the original `Host`, `X-Forwarded-Proto`, and `X-Forwarded-For`. Evidence: `deploy/staging/README.md:31-39`, `deploy/staging/Caddyfile:14-19`.
- No observed Cloudflare, NGINX, Traefik, HAProxy, Kubernetes ingress TLS secret, or CDN certificate API integration exists. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:465-475`.

CONSTITUTIONAL DECISION:

| Component | Owns | Does Not Own |
| --- | --- | --- |
| Platform/Domain infrastructure | Certificate business ownership by `TenantId` + `DomainId`; admission policy; audit; admin commands | Private key custody; raw certificate material serving |
| `Domain` aggregate | Hostname, tenant, verification, domain lifecycle, active certificate pointer | Provider order/challenge internals; private material |
| `SslCertificate` aggregate | Safe metadata, lifecycle state, provider/edge references, renewal/revocation/deployment metadata | Raw private keys; ACME account secrets; challenge credentials |
| Caddy edge | TLS termination, ACME issuance/renewal execution, challenge serving, certificate/private-key storage, HTTPS forwarding | Tenant authorization; merchant ownership decisions; admin UI state |
| Certificate provider abstraction | Provider/edge-specific operations behind an application contract | Tenant admission; direct controller authorization |
| Hangfire | Scheduling scans, monitor/probe jobs, retry orchestration | Provider business policy; TLS termination |
| Admin APIs/UI | Metadata display and authorized intent submission | Private key exposure; direct edge or provider bypass |
| StoreConfig/Theme/CMS/Catalog/POS/Checkout/Builder | No certificate ownership | All SSL/TLS ownership |

IMPLEMENTATION REQUIREMENT:

- Certificate commands must bind `TenantId`, `DomainId`, and, when present, `CertificateId`.
- No future certificate repository or service may expose `SslCertificate` by certificate id alone.

OPEN DECISION:

- None.

## 3. Canonical TLS Termination

OBSERVED REALITY:

- Caddy is the only observed TLS edge in repository infrastructure. Evidence: `deploy/staging/Caddyfile:1-3`, `deploy/staging/Caddyfile:22-72`.
- ASP.NET Core configures forwarded headers including `XForwardedFor`, `XForwardedProto`, and `XForwardedHost`. Evidence: `PointOfSale.API/Program.cs:103-130`.
- `UseForwardedHeaders()` runs in the pipeline. Evidence: `PointOfSale.API/Program.cs:3361`.
- ASP.NET Core uses HTTPS redirection for non-health requests but has no observed custom-domain certificate loading. Evidence: `PointOfSale.API/Program.cs:3381`, `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:189`.

CONSTITUTIONAL DECISION:

Canonical production model:

```text
Browser
  -> DNS for merchant custom domain
  -> Shopixy Caddy edge
  -> TLS terminates at Caddy
  -> Caddy forwards HTTP to Shopixy API/admin/storefront upstreams
  -> Shopixy resolves tenant from forwarded Host
```

- TLS terminates at Caddy, not in Kestrel/application code.
- Certificates physically live in Caddy-managed certificate storage.
- Caddy loads and serves certificates.
- Caddy performs ACME issuance and renewal.
- Shopixy knows a certificate is active only after an external SNI probe confirms the expected certificate is being served by the edge.
- Activation is not proven by "CA issued certificate" alone.

Rejected alternatives:

- Application/Kestrel TLS termination is rejected because there is no observed application certificate loading path and it would force private keys into the app boundary.
- Cloudflare-managed TLS is rejected for DOMAIN-5 because no Cloudflare config/API integration is observed in the repo.
- NGINX, Traefik, and HAProxy are rejected for DOMAIN-5 because no corresponding Shopixy deployment model exists in source.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.5 must implement against a Caddy edge abstraction. Business/application code must not hardcode Caddy internals outside the edge provider layer.
- Production must use persistent Caddy certificate storage. Multi-edge production must use shared Caddy-compatible certificate storage, not independent local disk per node.

OPEN DECISION:

- None.

## 4. Canonical ACME Ownership

OBSERVED REALITY:

- No first-party ACME implementation exists. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:151-160`.
- The only observed ACME-adjacent configuration is Caddy global email. Evidence: `deploy/staging/Caddyfile:1-3`.
- Caddy official documentation describes automatic HTTPS certificate provisioning/renewal and on-demand TLS for customer-owned domains, with an application permission check to prevent abuse. Sources: [Caddy Automatic HTTPS](https://caddyserver.com/docs/automatic-https), [Caddy On-Demand TLS](https://caddyserver.com/on-demand-tls), [Caddy global options](https://caddyserver.com/docs/caddyfile/options).

CONSTITUTIONAL DECISION:

CANONICAL ACME OWNERSHIP = Edge-managed ACME through Caddy.

- ACME account ownership: Shopixy platform edge operations.
- Account identity: platform ACME identity configured at the Caddy edge, initially represented by `ACME_EMAIL` in observed staging config.
- Account secret storage: Caddy-managed storage only; no application database secret.
- Certificate order ownership: Caddy edge.
- Authorization ownership: Caddy edge, gated by Shopixy admission policy.
- Challenge ownership: Caddy edge.
- Certificate retrieval: Caddy edge.
- Certificate storage: Caddy-managed certificate storage.
- Renewal: Caddy edge executes renewal; Shopixy monitors, probes, records metadata, alerts, and can request retry/recheck through provider abstraction.
- Revocation: Shopixy authorizes revocation; edge/provider layer performs CA/edge revocation without exposing private material to application code.
- Failure recovery: Shopixy records safe failure state, disables edge admission when required, and drives re-probe/retry/reissue workflows.

Boundary:

- Shopixy policy decides whether a hostname may receive a certificate.
- Caddy automation executes ACME and stores private material.
- Shopixy must never make tenant authorization decisions inside Caddy config alone; Caddy must consult a Shopixy admission/permission authority.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.3 must provide a provider abstraction whose canonical implementation is Caddy-managed ACME. Business services depend on the abstraction, not on Caddy SDK/config syntax.
- DOMAIN-5.5 must implement a Caddy on-demand TLS permission/admission endpoint or equivalent Caddy-supported permission mechanism.

OPEN DECISION:

- None.

## 5. Challenge Strategy

OBSERVED REALITY:

- Existing Shopixy DNS verification is read-only: it can read TXT/CNAME/HTTP verification evidence through `IDnsResolver`; it cannot write DNS challenge records. Evidence: `PointOfSale.Infrastructure/Services/Dns/DnsClientResolver.cs:15-37`, `PointOfSale.API/Program.cs:382-437`.
- Caddy is the observed edge. Evidence: `deploy/staging/README.md:31-39`.
- Caddy documentation states DNS challenge requires DNS provider credentials and is used for wildcard certificates. Source: [Caddy tls directive](https://caddyserver.com/docs/caddyfile/directives/tls).

CONSTITUTIONAL DECISION:

| Challenge | Canonical status | Reason |
| --- | --- | --- |
| HTTP-01 | PRIMARY CHALLENGE | Lowest credential footprint for individual merchant hostnames that resolve to Caddy and allow port 80. Caddy edge can serve the challenge without Shopixy writing DNS. |
| TLS-ALPN-01 | FALLBACK CHALLENGE | Suitable when port 443 reaches Caddy and Caddy can answer SNI challenge handshakes. It remains edge-owned. |
| DNS-01 | UNSUPPORTED FOR DOMAIN-5 TENANT CUSTOM DOMAINS | Shopixy has no DNS provider write authority and must not infer DNS write support from read-only verification. DNS-01 also introduces tenant DNS credential risk. |

Wildcard certificates:

- Tenant custom-domain wildcard certificates are outside DOMAIN-5. They require DNS-01 or delegated `_acme-challenge` control, neither of which exists today.
- Existing platform wildcard staging host `*.app-staging.yourdomain.com` is an operations/platform-domain concern, not merchant custom-domain DOMAIN-5 support. Evidence: `deploy/staging/Caddyfile:53-72`.

IMPLEMENTATION REQUIREMENT:

- Certificate admission must require DNS to point the customer hostname to the Caddy edge before activation can pass.
- Caddy/edge provider may attempt HTTP-01 and TLS-ALPN-01 according to its supported automation policy, but DNS-01 must remain disabled for tenant custom domains until a future blueprint explicitly adds DNS write/delegation authority.

OPEN DECISION:

- None.

## 6. Domain Admission Contract

OBSERVED REALITY:

- `Domain` carries `TenantId`, hostname, normalized hostname, lifecycle state, verification method/token, `VerifiedAtUtc`, `SslCertificateId`, and soft-delete state. Evidence: `PointOfSale.Core/Entities/Domain.cs:27-156`.
- `DomainType` includes `Primary`, `Alias`, `Redirect`, `Preview`, `Temporary`, `Development`, and `System`. Evidence: `PointOfSale.Core/Enums/DomainType.cs:7-49`.
- `DomainLifecycleState.Verified` means the domain is eligible for SSL provisioning. Evidence: `PointOfSale.Core/Enums/DomainLifecycleState.cs:24-34`.

CONSTITUTIONAL DECISION:

Canonical provisioning admission predicate:

```text
Allow initial provisioning when:
  tenantId is non-empty
  domain exists by (TenantId, DomainId)
  domain.IsDeleted = false
  domain.VerifiedAtUtc is not null
  domain.LifecycleState = Verified
  domain.DomainType in {Alias, Redirect}
  normalized hostname is non-empty and globally unique
  no non-terminal certificate operation exists for the domain
  no Active certificate already exists for the same hostname/generation
  per-domain distributed lock is acquired
  edge admission policy can allow this normalized hostname
```

Renewal admission predicate:

```text
Allow renewal/metadata sync when:
  domain exists by (TenantId, DomainId)
  domain.IsDeleted = false
  domain.LifecycleState in {SslActive, Primary}
  active certificate belongs to the same DomainId and TenantId
  renewal policy says the certificate is eligible
  per-domain distributed lock is acquired
```

Revocation admission predicate:

```text
Allow revocation when:
  certificate exists by (TenantId, DomainId, CertificateId)
  caller has revocation authority for the reason
  certificate is not already terminal
  per-domain distributed lock is acquired
```

System/Development domains:

- `System` and `Development` domains use `SystemDefault` or environment-owned TLS. They are not merchant ACME provisioning targets in DOMAIN-5.

Primary restriction:

- A domain may become `Primary` only after `SslActive` is proven at the edge.
- The observed `PromotePrimaryDomainAsync` allowance for `Verified` is not canonical and must be corrected in DOMAIN-5.2/5.8 before SSL-backed primary promotion is certified. Evidence: `PointOfSale.API/Services/Storefront/DomainWriteService.cs:1177-1182`, `PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs:43-48`.

IMPLEMENTATION REQUIREMENT:

- The engine MUST NOT provision certificates for arbitrary unverified domains.
- Verification freshness policy, if added, must be configuration-driven and enforced before admission.

OPEN DECISION:

- None.

## 7. Certificate State Machine

OBSERVED REALITY:

- Current `CertificateStatus` has only `Pending`, `Active`, `Expired`, `Revoked`, and `Failed`. Evidence: `PointOfSale.Core/Enums/CertificateStatus.cs:7-34`.
- Current `SslCertificate` persistence lacks order, authorization, challenge, activation, retry, revocation, generation, and fencing fields. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:20-104`.

CONSTITUTIONAL DECISION:

Canonical target certificate states:

| State | Meaning | Entry condition | Exit condition | Owner | Persisted fields | Side effects | Retry | Terminal |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Pending | Shopixy accepted SSL intent for a verified domain | Admission predicate passes | Edge order starts or validation fails | CertificateProvisioningService | TenantId, DomainId, Provider, Status, OperationId, IdempotencyKey | Audit `certificate_provision_started` | Yes | No |
| Requested | Edge/provider accepted certificate management request | Caddy admission enabled and probe/order requested | Authorization begins or provider rejects | Provider abstraction | ProviderRequestRef, EdgeCertificateRef, Generation | Metric/log provider request | Yes | No |
| Authorizing | ACME challenge is in progress at edge | Provider begins challenge | Issued or failed/expired challenge | Caddy edge/provider | ChallengeType, ChallengeRef, Attempt, ExpiresAtUtc | Edge challenge served | Yes until order expiry | No |
| Issued | Provider has issued certificate material at edge | Caddy reports/serves new certificate | Activation probe succeeds/fails | Edge provider | Issuer, Subject, SANs, Thumbprint, NotBeforeUtc, NotAfterUtc | Metadata sync | Activation retry | No |
| Activating | Shopixy is proving edge is serving expected cert | Issued or renewed cert exists at edge | Active or Failed | CertificateActivationService | ActivationStartedAtUtc, ExpectedThumbprint, ProbeAttempt | SNI probe | Yes | No |
| Active | Edge serves expected valid certificate | Activation proof succeeds | Renewal window, revocation, expiry | ActivationService | ActivatedAtUtc, Domain.SslCertificateId, Status Active | Domain `SslActive`; audit/event | Not applicable | No |
| Renewing | Existing active cert is being replaced/refreshed | Renewal threshold reached or manual renewal accepted | Active replacement or renewal failure | RenewalScheduler/provider | RenewalAttempt, NextRetryUtc, ReplacementGeneration | Lock + probe + metadata sync | Yes with backoff | No |
| Revoked | Certificate must no longer be served or trusted | Authorized revocation completes or compromise requires terminalization | Reissue creates new certificate row/generation | RevocationService/edge | RevokedAtUtc, RevokedBy, Reason, ProviderRevocationRef | Edge deactivation; audit/event | No for same row | Yes |
| Expired | Certificate validity has passed or edge serves expired cert | Expiry monitor/probe detects expired cert | Reissue creates new row/generation | ExpiryMonitor | ExpiredAtUtc, DetectedAtUtc | Downgrade/failover; alert | Reissue only | Yes for material |
| Failed | Operation cannot currently proceed | Provider, challenge, activation, storage, probe, or policy failure | Retry accepted or manual resolution | Owning workflow service | FailureCode, FailureReason, RetryAfterUtc, Attempts | Audit safe failure | Policy-driven | Sometimes |

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.2 must extend current persistence because the existing enum/entity cannot represent the canonical state machine.
- Raw private material is never persisted in state history.

OPEN DECISION:

- None.

## 8. Domain to Certificate State Coupling

OBSERVED REALITY:

- `DomainLifecycleState` includes `Verified`, `SslPending`, `SslActive`, and `Primary`. Evidence: `PointOfSale.Core/Enums/DomainLifecycleState.cs:24-46`.
- `Domain.MarkSslPending()` and `Domain.MarkSslActive()` exist. Evidence: `PointOfSale.Core/Entities/Domain.cs:237-251`.
- Host resolution currently excludes `SslPending` and includes `Verified`, `SslActive`, and `Primary`. Evidence: `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:77-90`, `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:137-142`.

CONSTITUTIONAL DECISION:

- Domain lifecycle must remain coarse. Certificate operations are represented primarily by `SslCertificate` state, not by continuously moving `Domain.LifecycleState`.
- Initial provisioning does not require moving `Domain` from `Verified` to `SslPending`. `Verified` remains the safe, servable, ownership-proven state while certificate provisioning is pending or failed.
- `Domain.SslActive` is entered only after activation proof confirms Caddy is serving the expected valid certificate.
- `Primary` requires `SslActive` proof and an active linked certificate.
- `SslPending` is reserved for future explicit maintenance/blocked states and must not be used by automatic provisioning unless the same implementation wave also changes host-resolution semantics.

Coupling rules:

| Certificate event | Domain effect |
| --- | --- |
| Provisioning starts from `Verified` | Domain remains `Verified`; certificate row becomes `Pending` |
| Authorization/issuance in progress | Domain remains `Verified`; operation state lives on certificate/operation |
| Activation succeeds | Domain becomes `SslActive`; `SslCertificateId` links active certificate; `SslActivatedAtUtc` set |
| Activation fails | Domain remains or returns to `Verified`; certificate becomes `Failed`; no active cert link |
| Renewal starts | Domain remains `SslActive` or `Primary`; old cert remains active until replacement activation succeeds |
| Renewal succeeds | Active certificate link atomically moves to replacement; domain remains `SslActive` or `Primary` |
| Renewal exhausted | Domain downgrades from `SslActive` to `Verified`; `Primary` is removed/fails over as below |
| Expired/revoked active cert | Domain downgrades to `Verified`; edge admission is disabled until recovery |
| Primary active cert invalid and no replacement | Domain loses `Primary` lifecycle and becomes `Verified`; canonical storefront must fail over to platform/default primary rather than serving an invalid custom-domain primary |

Primary promotion rule:

- `PromotePrimaryDomain` must require `SslActive` and active certificate proof. The observed allowance for `Verified` is an implementation defect against DOMAIN-5.1.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.2 must freeze schema/state changes that allow safe downgrade/failover and avoid `Primary` with invalid certificate material.
- DOMAIN-5.8 must enforce the primary-promotion rule at API/service level.

OPEN DECISION:

- None.

## 9. Host Resolution Contract

OBSERVED REALITY:

- `PersistenceDomainResolver` returns `Found` for `Verified`, `SslActive`, and `Primary`; it returns `NotFound` for `Draft`, `PendingVerification`, `SslPending`, `Disabled`, `Archived`, and `Deleted`. Evidence: `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:44-49`, `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:77-90`, `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:137-142`.
- Host resolution performs no DNS/SSL verification. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:458-459`.

CONSTITUTIONAL DECISION:

Canonical behavior = Option A: `Verified` remains resolvable without requiring `SslActive`.

Reason:

- TLS availability is enforced at the Caddy edge, not by the application tenant resolver.
- Keeping `Verified` resolvable avoids storefront outages while Caddy-managed ACME is pending, failed, or recovering.
- DOMAIN-1.6 already treats certificate expiry as downgrade to `Verified`, not loss of domain ownership. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:316-319`.

HTTP vs HTTPS policy:

- Caddy owns HTTP-to-HTTPS behavior at the edge.
- Shopixy host resolution does not branch on request scheme for tenant ownership.
- HTTPS success requires edge certificate availability; storefront tenant resolution requires `Verified`, `SslActive`, or `Primary`.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.5 must implement edge admission so Caddy may only obtain/serve certificates for Shopixy-verified, tenant-owned hostnames.
- DOMAIN-5 must not change `PersistenceDomainResolver` to require `SslActive` unless a later constitutional document deliberately changes the storefront availability model.

OPEN DECISION:

- None.

## 10. Certificate Persistence Freeze

OBSERVED REALITY:

- Current `SslCertificate` has metadata only. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:20-104`.
- Current indexes support domain lookup, status, expiry, renewal, soft delete, and unique non-terminal certificate by domain. Evidence: `PointOfSale.Infrastructure/Data/Configurations/SslCertificateConfiguration.cs:101-128`.
- `ApplicationDbContext` filters `SslCertificate` only by soft delete, not tenant. Evidence: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:963-964`.

CONSTITUTIONAL DECISION:

Canonical database storage = SAFE METADATA plus OPAQUE SECRET/EDGE REFERENCES only.

Required certificate metadata:

- Identity: `Id`, `TenantId`, `DomainId`, `NormalizedHostName`.
- Provider: provider type, provider implementation, provider account reference, provider environment.
- Lifecycle: canonical certificate state, operation phase, generation/version, operation id, idempotency key.
- ACME/edge references: order reference, authorization reference, challenge reference, edge certificate reference, Caddy storage key/reference.
- Certificate metadata: issuer, subject, SANs, thumbprint/fingerprint, serial number, signature algorithm where safe, NotBeforeUtc, NotAfterUtc, IssuedAtUtc, ActivatedAtUtc.
- Deployment metadata: edge cluster/id, deployed/observed generation, last edge probe time, last observed thumbprint, activation failure fields.
- Renewal metadata: renewal eligibility, last renewal attempt, next renewal due, attempt count, retry-after, backoff reason, replacement certificate id.
- Revocation metadata: revoked at, revoked by actor type/id, reason, provider revocation reference, edge deactivation timestamp.
- Failure metadata: safe failure code, safe failure reason, provider error class, retryable flag, last failure time, correlation id.
- Concurrency: row version/fencing token, lock owner/lease expiry or equivalent distributed lock reference.
- Audit linkage: created/updated timestamps, created by, last operation audit id, history JSON without secrets.

Required indexes:

- `(TenantId, DomainId, IsDeleted)`.
- `(TenantId, NormalizedHostName, Status)`.
- due renewal by `NextRenewalUtc` and status.
- expiring certificates by `NotAfterUtc` and status.
- failed retry by `RetryAfterUtc`.
- activation pending by status/phase.
- unique active/activating generation per `DomainId`.
- idempotency key unique within tenant/domain/operation.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.2 must freeze schema before ACME/provider code begins.
- The ordinary application database MUST NOT store raw private keys, raw ACME account secrets, challenge provider credentials, PEM/PFX/PKCS12 private material, or full certificate private material.
- DTOs must expose safe metadata only.

OPEN DECISION:

- None.

## 11. Private Key Architecture

OBSERVED REALITY:

- Current `SslCertificate` does not store raw private keys. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:20-104`.
- Staging docs require runtime secrets from environment variables, Kubernetes secrets, Azure Key Vault, AWS Secrets Manager, or equivalent. Evidence: `deploy/staging/README.md:22-29`.
- No certificate-key-specific encryption, RBAC, audit, rotation, or backup model exists. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:216-231`.

CONSTITUTIONAL DECISION:

Canonical private-key storage = Caddy-managed certificate storage on encrypted persistent storage, with production multi-edge using shared encrypted Caddy-compatible storage.

Rules:

- Only the Caddy edge process identity may read private certificate material.
- Shopixy API, frontend, Hangfire workers, domain services, tenant admins, and merchants must never read raw private keys.
- The database may store only opaque Caddy/edge storage references and safe certificate metadata.
- Caddy storage must be encrypted at rest at the volume/object/storage layer.
- Backups of Caddy storage must be encrypted, access-controlled, and audited.
- Restore must preserve ACME account state when possible to reduce reissue/rate-limit pressure.
- Rotation must create a replacement generation and activate only after edge proof.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.5 operations must document filesystem/storage permissions, backup, restore, and access control for Caddy certificate storage.
- No private material may be logged, audited, included in JSON history, serialized in events, or returned to UI/API clients.

OPEN DECISION:

- None.

## 12. Certificate Distribution

OBSERVED REALITY:

- No certificate deployment path exists today: no Caddy API integration, file/volume replacement contract, Kubernetes Secret TLS contract, hot reload trigger, fencing, or activation probe. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:199-214`.

CONSTITUTIONAL DECISION:

Canonical deployment contract:

```text
Shopixy verifies domain ownership
  -> Shopixy enables edge admission for hostname
  -> Caddy obtains/stores certificate through edge-managed ACME
  -> Caddy serves certificate from Caddy storage
  -> Shopixy external SNI probe verifies served certificate
  -> Shopixy stores safe metadata
  -> Domain becomes SslActive
```

- Certificate generation/storage/distribution are edge-owned, not app-owned.
- The application does not copy PEM/PFX files to the edge.
- Atomic replacement is achieved by Caddy certificate storage and activation semantics, plus Shopixy generation/fencing metadata.
- Rollback means continuing to serve the previously active certificate until replacement activation is proven.
- Zero-downtime activation is required: old certificate remains active until the new generation is externally confirmed.
- Stale certificate detection is done by periodic SNI probes comparing expected hostname/SAN/thumbprint/expiry with metadata.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.5 must provide an edge abstraction for admission, activation probing, metadata sync, and deactivation.
- Multi-edge deployments must share certificate storage or otherwise prove all edges converge before `SslActive` is set.

OPEN DECISION:

- None.

## 13. Activation Proof

OBSERVED REALITY:

- `Domain.SslActivatedAtUtc` exists. Evidence: `PointOfSale.Core/Entities/Domain.cs:119-123`.
- Current read mapping does not reliably expose actual domain activation time from `Domain.SslActivatedAtUtc`. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:613-616`.
- No activation probe exists. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:199-214`.

CONSTITUTIONAL DECISION:

Before setting `Domain = SslActive`, Shopixy must externally prove:

- hostname: normalized domain hostname.
- port: 443.
- SNI: normalized hostname.
- issuer: captured and stored as metadata.
- subject/SAN: includes normalized hostname exactly or by accepted wildcard only for platform-owned wildcard, not tenant custom wildcard in DOMAIN-5.
- thumbprint/fingerprint: matches expected observed certificate generation.
- NotBefore/NotAfter: valid at probe time with renewal policy margin.
- chain: validates under system trust.
- protocol: TLS handshake succeeds without bypassing certificate validation.
- edge convergence: all required production edge instances or shared edge endpoint serve the expected generation.

Failure behavior:

- If the edge serves an old, wrong, expired, mismatched, or untrusted certificate, activation fails.
- Domain remains `Verified`; certificate state becomes `Failed` or remains `Activating` with retry according to policy.
- No `SslCertificateId` active link is installed until proof succeeds.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.5 must implement SNI probe and activation fencing.
- Activation probe logs/metrics must never include private material.

OPEN DECISION:

- None.

## 14. ACME Order Lifecycle

OBSERVED REALITY:

- No ACME order/authorization/challenge persistence exists. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:151-160`.

CONSTITUTIONAL DECISION:

With Caddy-managed ACME, ACME order internals are edge-owned, but Shopixy must still persist safe workflow references:

```text
Shopixy operation
  -> edge admission enabled
  -> Caddy ACME account/order
  -> Caddy authorization/challenge
  -> Caddy finalization/certificate storage
  -> Shopixy SNI probe
  -> Shopixy metadata activation
```

Idempotency:

- Provision, renew, retry, and revoke commands require idempotency keys.
- Duplicate commands for the same tenant/domain/operation return the existing operation state instead of creating competing issuance.
- Provider/edge request references must be correlated to operation ids.

Crash recovery:

- If the application crashes after enabling edge admission, the next scheduler/probe reconciles by checking edge-served certificate state.
- If a Hangfire job crashes, the operation remains recoverable by persisted state and retry-after policy.
- If Caddy/provider times out, Shopixy records a retryable provider failure and re-probes before reissuing.
- If challenge/order expires, retry creates a new generation under lock.
- If certificate retrieval is edge-internal but activation probe fails, the operation remains `Activating`/`Failed` without changing domain state.
- If edge deployment fails, old active certificate remains linked until replacement proof succeeds.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.3 provider abstraction must hide provider specifics but expose enough safe state for idempotency, failure classification, and reconciliation.

OPEN DECISION:

- None.

## 15. Renewal Architecture

OBSERVED REALITY:

- Hangfire is registered with PostgreSQL storage. Evidence: `PointOfSale.API/Program.cs:2925-2943`.
- Recurring jobs are registered idempotently by `HangfireInitializationHostedService`. Evidence: `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs:177-180`.
- Domain verification scheduler uses `[DisableConcurrentExecution]` and `[AutomaticRetry]`. Evidence: `PointOfSale.API/Services/Storefront/DomainVerificationSchedulerJob.cs:13-15`.
- No SSL renewal job is registered. Evidence: `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs:326-338`.

CONSTITUTIONAL DECISION:

- Caddy edge executes automatic ACME renewal.
- Shopixy owns renewal policy, monitoring, metadata synchronization, failure detection, alerting, and admin-visible renewal status.
- Hangfire is the only Shopixy scheduler for renewal monitoring/reconciliation. No second scheduler may be introduced.
- Renewal thresholds, scan frequency, max attempts, backoff, and provider/rate-limit windows are configuration-driven.

Canonical flow:

```text
Hangfire CertificateRenewalMonitor
  -> scan Active/Primary certificates by NextRenewalUtc/NotAfterUtc
  -> acquire distributed per-domain lock
  -> ask edge provider for observed/managed certificate state
  -> trigger allowed recheck/renewal if policy permits
  -> SNI probe expected generation
  -> update safe metadata
  -> retire old generation only after replacement proof
```

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.6 must follow existing Hangfire registration patterns.
- Renewal must protect against scheduled renewal plus manual renewal races.
- Old certificate retirement must be delayed until replacement activation is proven.

OPEN DECISION:

- None.

## 16. Revocation Architecture

OBSERVED REALITY:

- `CertificateStatus.Revoked` exists. Evidence: `PointOfSale.Core/Enums/CertificateStatus.cs:25-28`.
- No revoke API, service, provider call, audit event, UI mutation, or edge deactivation exists. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:321-333`.

CONSTITUTIONAL DECISION:

Who may revoke:

- Merchant/tenant admin: may request revocation only for their own non-primary custom-domain certificate when a replacement/recovery path is available or domain is being removed.
- Platform admin: may revoke tenant certificates for operational/security reasons.
- Security automation/system: may revoke on key compromise, certificate substitution, domain ownership loss, provider compromise, or abuse.
- Ordinary users and cross-tenant actors may not revoke.

Reasons:

- `domain_removed`, `tenant_requested`, `security_key_compromise`, `certificate_substitution`, `domain_ownership_lost`, `provider_compromise`, `renewal_recovery`, `platform_admin_action`, `abuse_prevention`.

Effects:

- Provider/CA revocation is performed by the edge/provider layer without exposing private keys to the application.
- Edge admission is disabled for revoked hostnames unless immediate reissue is explicitly authorized.
- Certificate row becomes `Revoked` and is terminal.
- Domain downgrades to `Verified` unless a replacement certificate is activated in the same fenced operation.
- Host resolution may continue for `Verified`, but HTTPS success depends on edge state.
- Primary certificate revocation without replacement removes primary status and falls back to a platform/default primary path.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.7 must implement revocation as fail-closed: if provider/edge revocation cannot be confirmed for a security reason, edge admission must be disabled and the incident remains open.
- A revoked certificate row can never return to `Active`; recovery creates a new certificate generation.

OPEN DECISION:

- None.

## 17. Expiry and Failure Recovery

OBSERVED REALITY:

- `SslCertificate.NotAfterUtc` exists. Evidence: `PointOfSale.Core/Entities/SslCertificate.cs:59-62`.
- No job marks certificates expired or changes domain state on expiry. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:308-319`.

CONSTITUTIONAL DECISION:

| Failure | Domain behavior | Certificate behavior | Recovery |
| --- | --- | --- | --- |
| Certificate expired | `SslActive -> Verified`; `Primary -> Verified` with primary fallback | `Expired` terminal for material | Reissue new generation |
| Certificate revoked | Downgrade to `Verified` unless replacement active | `Revoked` terminal | Reissue after authorization |
| Renewal failed but not exhausted | Domain remains `SslActive`/`Primary` while old cert valid | `Renewing` with retry | Backoff/probe/retry |
| Renewal exhausted | Downgrade to `Verified`; primary removed/fails over | `Failed` or `Expired` | Manual/system recovery |
| Activation failed | Domain remains `Verified` | `Failed`/retryable | Retry activation or reissue |
| Edge probe failed | No activation; existing active cert remains if valid | record `edge_probe_failed` | retry/probe all edges |
| Provider unavailable | No domain promotion | retryable failure | backoff/rate-limit aware retry |
| ACME account unavailable | Edge admission disabled for new issuance | failure/incident | restore Caddy account storage |
| Private key unavailable | Edge cannot serve cert; downgrade after confirmation | failed/expired risk | restore Caddy storage; fallback reissue |
| Certificate store unavailable | Do not mark active | failure/incident | restore storage before reissue |

IMPLEMENTATION REQUIREMENT:

- Failures must never silently leave `SslActive` or `Primary` when the edge is known to serve an invalid certificate.
- Recovery must prefer restore of Caddy certificate/account storage before mass reissue to protect CA rate-limit budget.

OPEN DECISION:

- None.

## 18. Multi-Instance Concurrency

OBSERVED REALITY:

- Current verification uses process-local per-domain semaphores plus Hangfire overlap control. Evidence: `PointOfSale.API/Services/Storefront/DomainVerificationService.cs:70-71`, `PointOfSale.API/Services/Storefront/DomainVerificationSchedulerJob.cs:13-15`.
- `Domain.Version` uses PostgreSQL xmin rowversion. Evidence: `PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs:132-137`.
- No SSL concurrency guard exists. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:422-435`.

CONSTITUTIONAL DECISION:

- SSL operations require distributed concurrency, not process-local locks.
- Canonical lock scope: `(TenantId, DomainId)` plus operation type.
- Canonical protections: database transaction, optimistic row version/fencing token, idempotency key, and a distributed lock/advisory lock or persisted lease.
- Hangfire `[DisableConcurrentExecution]` is useful for scheduler overlap but insufficient for per-domain/manual-vs-scheduled races.

Races covered:

- two provisioning jobs.
- two renewal jobs.
- manual renew plus scheduled renewal.
- revoke plus renew.
- activation plus replacement.
- multi-edge stale activation.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.2 must add persistence/fencing support.
- DOMAIN-5.4 through DOMAIN-5.7 must acquire the same per-domain lock before provider/edge operations.

OPEN DECISION:

- None.

## 19. Tenant Isolation

OBSERVED REALITY:

- `Domain` has `TenantId`. Evidence: `PointOfSale.Core/Entities/Domain.cs:29-35`.
- SSL read joins through tenant-owned domain. Evidence: `PointOfSale.API/Services/Storefront/DomainReadService.cs:420-428`.
- `SslCertificate` currently has no tenant query filter; it is soft-delete filtered only. Evidence: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:963-964`.

CONSTITUTIONAL DECISION:

- Every certificate command and read must bind `TenantId`, `DomainId`, and certificate identity.
- Cross-tenant certificate access returns not found or forbidden according to existing API conventions, but must never reveal certificate existence across tenants.
- Tenant admins may see safe metadata for their own certificates.
- Tenant admins may not read private material, ACME account references that are secret, edge storage paths that leak secret location, or challenge credentials.
- Private key access is system/edge-only and stricter than metadata access.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.2 must add tenant attribution to certificate persistence or enforce tenant joins so strongly that no direct certificate id access path can exist.
- DOMAIN-5.8 APIs must reauthorize every mutation against current tenant/domain ownership.

OPEN DECISION:

- None.

## 20. API Contract

OBSERVED REALITY:

- Current API exposes only `GET /api/admin/storefront/domains/{id}/ssl`. Evidence: `PointOfSale.API/Controllers/AdminDomainsController.cs:242-281`.
- `DomainSslDto` exposes metadata fields only. Evidence: `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs:842-858`.
- There are no provision, renew, retry, revoke, operation-status, or SSL history commands. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:334-350`.

CONSTITUTIONAL DECISION:

Future canonical APIs:

| API | Authorization | Request | Response | Idempotency | Errors |
| --- | --- | --- | --- | --- | --- |
| `GET /domains/{id}/ssl` | tenant domain read | none | safe certificate status, metadata, operation summary | n/a | 404 no cross-tenant leak |
| `POST /domains/{id}/ssl/provision` | tenant admin/domain owner | idempotency key; optional provider policy | accepted operation/status | required | 400 not verified; 409 in-progress; 429 policy |
| `POST /domains/{id}/ssl/renew` | tenant admin or system | idempotency key; reason | operation/status | required | 400 not active; 409 lock; 429 rate limit |
| `POST /domains/{id}/ssl/retry` | tenant admin/system | idempotency key; failed operation id | operation/status | required | 400 not retryable; 409 lock |
| `POST /domains/{id}/ssl/revoke` | platform admin/security; limited tenant admin | idempotency key; reason | operation/status | required | 403 unsupported authority; 409 lock |
| `GET /domains/{id}/ssl/operations/{operationId}` | tenant domain read | none | safe operation state | n/a | 404 no cross-tenant leak |
| `GET /domains/{id}/ssl/history` | tenant domain read | paging | safe audit/history | n/a | 404 no cross-tenant leak |

Error model:

- Safe error codes only: no private key path, ACME secret, raw provider payload, or challenge secret in response.
- Rate limiting must be tenant/domain scoped and provider-limit aware.

IMPLEMENTATION REQUIREMENT:

- SSL APIs must be implemented only after DOMAIN-5.2 through DOMAIN-5.7 establish persistence, provider, activation, renewal, and revocation authority.

OPEN DECISION:

- None.

## 21. Frontend Contract

OBSERVED REALITY:

- `SslManagementPage` reads `useDomainSsl` and displays status/metadata. Evidence: `PointOfSale.Web/src/modules/storefront/domains/pages/SslManagementPage.jsx:24-31`, `PointOfSale.Web/src/modules/storefront/domains/pages/SslManagementPage.jsx:136-244`.
- The page has a disabled download button. Evidence: `PointOfSale.Web/src/modules/storefront/domains/pages/SslManagementPage.jsx:234-243`.
- `DomainsOverviewPage` has a `Renew SSL` action with an empty handler. Evidence: `PointOfSale.Web/src/modules/storefront/domains/pages/DomainsOverviewPage.jsx:198-204`.
- Add-domain UI offers SSL provider choices that are not sent to backend. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:360-364`.

CONSTITUTIONAL DECISION:

Current controls classification:

| UI element | Classification | Canonical action |
| --- | --- | --- |
| SSL status display | READ-ONLY now; REAL after metadata sync | Keep metadata-only |
| Provider display | READ-ONLY | Show canonical provider after backend owns it |
| Thumbprint/dates/SANs | READ-ONLY | Show safe metadata |
| Download certificate | REMOVE or keep DISABLED | No private-key/cert download in merchant UI for Caddy-managed certs |
| Renew SSL overview action | REMOVE until backend exists; FUTURE after DOMAIN-5.8 | Must not be no-op |
| Provision button | FUTURE | Show only when backend command exists and domain is eligible |
| Retry button | FUTURE | Show only for retryable failed states |
| Revoke button | FUTURE restricted | Hide for ordinary users unless authorized |
| Provider selection in add-domain wizard | REMOVE from create flow until backend accepts policy | Must not imply authority that backend ignores |

IMPLEMENTATION REQUIREMENT:

- UI must never imply an SSL operation exists before backend authority exists.
- UI must never display private material, ACME secrets, challenge credentials, Caddy storage keys, or provider secret references.

OPEN DECISION:

- None.

## 22. Observability

OBSERVED REALITY:

- Domain verification scheduler has metrics for scheduler runs and outcomes. Evidence: `PointOfSale.API/Instrumentation/DomainVerificationSchedulerTelemetry.cs:24-55`.
- No certificate metrics/logging exist. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:379-390`.

CONSTITUTIONAL DECISION:

Canonical logs, metrics, traces, and audit events:

- `provision_started`, `provision_succeeded`, `provision_failed`.
- `activation_started`, `activation_succeeded`, `activation_failed`.
- `renewal_started`, `renewal_succeeded`, `renewal_failed`.
- `revocation_started`, `revocation_succeeded`, `revocation_failed`.
- `expiry_warning`, `certificate_expired`.
- `edge_probe_failed`, `edge_certificate_mismatch`, `edge_not_converged`.
- `provider_rate_limited`, `provider_unavailable`.
- `admission_denied`, `duplicate_operation_suppressed`.

Mandatory dimensions:

- tenant id, domain id, certificate id, operation id, provider, edge cluster, state, failure code, retryable flag, correlation id.

Forbidden log/event fields:

- private keys.
- ACME account secrets.
- challenge secrets.
- certificate private material.
- raw provider secret payloads.
- full Caddy storage file contents.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.4 through DOMAIN-5.7 must emit metrics and audit for every lifecycle transition.
- Audit snapshots must be metadata-only.

OPEN DECISION:

- None.

## 23. Security Model

CONSTITUTIONAL DECISION:

| Threat | Classification | Control | Owner | Detection | Recovery |
| --- | --- | --- | --- | --- | --- |
| Private key theft | Critical | Caddy-only private-key access; encrypted storage; no DB key storage | Edge operations | storage access audit; probe mismatch | revoke/reissue; rotate storage credentials |
| ACME account compromise | Critical | Caddy account storage isolated from app; encrypted backup | Edge operations | provider anomalies; issuance audit | restore/rotate ACME account; revoke affected certs |
| Certificate substitution | Critical | activation probe checks SAN/thumbprint/generation | ActivationService | edge mismatch metric | block activation; revoke/reissue |
| Tenant confusion | Critical | all commands bind TenantId/DomainId/CertificateId | API/application | cross-tenant tests/audit | fail closed; incident audit |
| Challenge hijacking | High | Caddy challenge only after Shopixy admission | Edge + admission policy | denied admission logs; failed challenge | disable admission; require reverify |
| Unauthorized renewal | High | tenant/system auth plus distributed lock | API/application | audit anomaly | cancel/revoke replacement |
| Unauthorized revocation | High | restricted authority and reason required | API/security | audit alerts | reissue if improper |
| Stale activation | High | generation/fencing and SNI probe | ActivationService | mismatch probe | keep old cert active |
| Race activation | High | distributed lock and fencing token | Workflow | conflict metrics | retry under current generation |
| Expired certificate serving | High | expiry monitor plus edge probe | RenewalMonitor | expiry warning/expired metric | restore/reissue/downgrade |
| Backup leakage | Critical | encrypted Caddy storage backups; RBAC | Operations | backup access audit | rotate/revoke/reissue |
| Provider impersonation | High | provider abstraction; trust validation; no raw callbacks as authority | Provider layer | provider response validation | block provider; incident runbook |

IMPLEMENTATION REQUIREMENT:

- Private-key exposure is always Critical.
- Certificate activation without edge proof is forbidden.

OPEN DECISION:

- None.

## 24. Disaster Recovery

OBSERVED REALITY:

- There is no certificate backup/restore/reissue runbook. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:392-407`.

CONSTITUTIONAL DECISION:

Canonical DR strategy = backup certificate material and ACME account state through encrypted Caddy storage backups; reissue is fallback.

Restore order:

1. Restore database metadata to a consistent point.
2. Restore Caddy certificate/account storage for the same or newer point.
3. Start Caddy edge and verify certificate inventory.
4. Run Shopixy certificate reconciliation job.
5. Probe active hostnames.
6. Downgrade/reissue only certificates whose material cannot be restored or whose edge proof fails.

Scenarios:

- Database restore with intact Caddy storage: reconcile Caddy-served certificates back into safe metadata.
- Caddy storage loss with intact database: restore encrypted backup first; fallback to rate-limit-aware reissue.
- ACME account loss: restore account storage if possible; if impossible, create new edge account through operations and reissue gradually under provider limits.
- Certificate metadata loss: reconstruct safe metadata from edge probes where possible.
- Certificate material loss: restore Caddy storage; fallback reissue.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.7 must include recovery runbooks and reconciliation jobs.
- Mass reissue must be throttled and provider-limit aware.

OPEN DECISION:

- None.

## 25. Provider Abstraction

OBSERVED REALITY:

- No ACME provider abstraction exists. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:151-160`.

CONSTITUTIONAL DECISION:

Business layer depends on a provider abstraction. No business logic may directly depend on Certes, BouncyCastle, Caddy SDK/config syntax, Cloudflare SDK, or other provider-specific libraries.

Canonical responsibilities:

- `ICertificateProvider`: provider/edge certificate management requests, renewal/recheck request, revocation request, provider status, safe provider failure classification.
- `ICertificateAdmissionPolicy`: determines whether a hostname may receive edge-managed certificate automation.
- `ICertificateMetadataSynchronizer`: maps edge-observed safe metadata into Shopixy persistence.

Provider layer owns:

- ACME details.
- provider API details.
- order/authorization/challenge mechanics.
- certificate retrieval/observation from edge.
- revocation mechanics.
- provider-specific rate limits and retry signals.

Business layer owns:

- domain eligibility.
- tenant security.
- state machine.
- audit.
- workflow orchestration.
- user/system authorization.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.3 must provide provider contract tests before provisioning uses it.

OPEN DECISION:

- None.

## 26. Edge Abstraction

OBSERVED REALITY:

- Staging Caddy exists, but no application edge integration exists. Evidence: `deploy/staging/Caddyfile:14-19`, `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:199-214`.

CONSTITUTIONAL DECISION:

Canonical edge abstractions:

- `IEdgeCertificateAdmission`: enable/disable/check whether an edge may issue/serve for a hostname.
- `IEdgeCertificateProbe`: perform external SNI probes and return safe observed metadata.
- `IEdgeCertificateRevocation`: request edge/provider deactivation/revocation without exposing private material.
- `IEdgeCertificateInventory`: reconcile safe observed certificate inventory for known domains.

Business/application code must not directly depend on Caddy config syntax. Caddy-specific logic lives in the edge provider implementation.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.5 must implement the Caddy edge provider behind these contracts.
- Any future CDN/Cloudflare model requires a new constitutional blueprint or ADR before replacing Caddy.

OPEN DECISION:

- None.

## 27. Events

OBSERVED REALITY:

- Domain events exist for domain lifecycle and verification, including `DomainVerifiedEvent`. Evidence: `PointOfSale.Core/Events/Domain/DomainEvents.cs:101-113`.
- No certificate events exist. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:321-327`.

CONSTITUTIONAL DECISION:

Canonical certificate events:

| Event | Producer | Consumer | Payload safety |
| --- | --- | --- | --- |
| `CertificateProvisioningStarted` | Provisioning service | audit/notifications/jobs | TenantId, DomainId, OperationId only |
| `CertificateRequested` | Provider workflow | audit/metrics | safe provider ref |
| `CertificateIssued` | Provider/metadata sync | activation service | issuer/SAN/thumbprint/validity only |
| `CertificateActivationStarted` | Activation service | metrics/audit | operation/generation |
| `CertificateActivated` | Activation service | domain/read model/UI | safe metadata |
| `CertificateActivationFailed` | Activation service | retry/alert | safe failure code |
| `CertificateRenewalStarted` | Renewal scheduler | metrics/audit | operation id |
| `CertificateRenewed` | Renewal workflow | read model/audit | safe metadata |
| `CertificateRevoked` | Revocation service | audit/alerts | reason/actor, no secrets |
| `CertificateExpired` | Expiry monitor | alert/recovery | safe metadata |

IMPLEMENTATION REQUIREMENT:

- Events must include tenant/domain/certificate identity and idempotency keys.
- No private material in events.

OPEN DECISION:

- None.

## 28. Architecture Boundaries

CONSTITUTIONAL DECISION:

Forbidden dependencies:

- Domain verification MUST NOT depend on SSL provisioning.
- SSL provisioning MAY depend on successful domain verification.
- `StoreConfig` MUST NOT own certificate state.
- Theme MUST NOT own certificate state.
- CMS MUST NOT own certificate state.
- POS MUST NOT own certificate state.
- Catalog/Checkout/Builder MUST NOT own certificate state.
- Certificate provider MUST NOT decide tenant authorization.
- Edge MUST NOT decide Shopixy tenant ownership except by asking Shopixy admission policy.
- Frontend MUST NOT create executable SSL authority without backend confirmation.

IMPLEMENTATION REQUIREMENT:

- Architecture tests in DOMAIN-5.9/5.10 must freeze these forbidden dependencies.

OPEN DECISION:

- None.

## 29. Test Constitution

OBSERVED REALITY:

- Existing tests cover domain persistence/read/verification and some frontend SSL service read behavior. Evidence: `DOMAIN-5.0_SSL_TLS_CERTIFICATE_PROVISIONING_LIFECYCLE_ARCHITECTURE_AUDIT.md:523-545`.

CONSTITUTIONAL DECISION:

Mandatory DOMAIN-5 tests:

| Area | Test type |
| --- | --- |
| Provider abstraction | unit + contract |
| Caddy edge abstraction | contract + integration with test double |
| Challenge strategy | contract + configuration validation |
| Persistence/state machine | unit + database |
| Tenant isolation | API + service + database |
| Authorization | HTTP/API |
| Private-key leakage | architecture/static + DTO/event/audit tests |
| Idempotency | unit + database + HTTP |
| Concurrency | database + multi-worker integration |
| Renewal | scheduler + provider fake + failure paths |
| Revocation | authorization + provider fake + state transitions |
| Expiry | scheduler + edge probe fake |
| Activation proof | edge probe integration |
| Rate limiting | provider-policy tests |
| Multi-instance execution | Hangfire/distributed lock tests |
| Frontend integrity | component/service tests for real vs disabled actions |

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5 cannot be certified without tests proving no private material reaches DB tables, DTOs, logs, audit snapshots, frontend payloads, or events.

OPEN DECISION:

- None.

## 30. Implementation Waves

CONSTITUTIONAL DECISION:

1. DOMAIN-5.2 - Certificate Persistence Freeze
   Freeze schema/entity/state additions, tenant attribution, idempotency, fencing, indexes, metadata-only DTO model, and no-private-key rules.

2. DOMAIN-5.3 - Certificate Provider / ACME Foundation
   Define provider abstraction, Caddy-managed ACME adapter contract, admission policy contract, provider failure taxonomy, and provider-rate-limit model.

3. DOMAIN-5.4 - Certificate Provisioning Engine
   Implement verified-domain admission, operation creation, Caddy admission enablement, provider workflow, idempotency, audit, and failure handling.

4. DOMAIN-5.5 - Certificate Activation / Edge Integration
   Implement Caddy edge provider, on-demand TLS admission/permission, SNI probe, edge convergence, activation fencing, and metadata sync.

5. DOMAIN-5.6 - Renewal Scheduler
   Implement Hangfire renewal/expiry monitor, reconciliation, config-driven thresholds, backoff, provider-limit guard, and replacement activation.

6. DOMAIN-5.7 - Revocation and Failure Recovery
   Implement revocation authority, edge deactivation, CA/provider revocation through edge provider, expiry downgrade, DR/reconciliation runbooks.

7. DOMAIN-5.8 - SSL Admin Commands and APIs
   Implement provision/renew/retry/revoke/status/history APIs with tenant isolation, authorization, idempotency, rate limiting, and safe errors.

8. DOMAIN-5.9 - SSL Frontend Integration
   Replace fake controls with backend-backed actions, remove unsupported provider/download UX, and display metadata/operation state safely.

9. DOMAIN-5.10 - Full SSL End-to-End Certification
   Certify Verified -> provision -> edge issued -> activation proof -> SslActive -> renewal -> revocation/expiry recovery with tests and runtime evidence.

IMPLEMENTATION REQUIREMENT:

- DOMAIN-5.2 may begin from this blueprint.
- Later waves must not reopen Caddy TLS termination, edge-managed ACME, challenge policy, or private-key boundary without a new architecture decision.

OPEN DECISION:

- None.

## 31. Architecture Laws

1. SSL begins only after verified domain ownership.
2. DNS verification and SSL lifecycle remain separate.
3. Caddy is the canonical DOMAIN-5 TLS terminator.
4. Caddy-managed ACME is the canonical certificate issuance path.
5. Shopixy owns certificate policy; Caddy owns private material.
6. Certificates are attributable to `TenantId` and `DomainId`.
7. Certificate commands never look up by certificate id alone.
8. Private keys never enter ordinary application tables.
9. Private keys never enter DTOs, logs, audit snapshots, events, or frontend payloads.
10. HTTP-01 is primary; TLS-ALPN-01 is fallback; DNS-01 is unsupported for tenant custom domains.
11. `Verified` remains host-resolvable; `SslActive` is not required for tenant resolution.
12. `Primary` requires edge-proven `SslActive`.
13. Certificate activation requires external SNI proof.
14. Issuance, renewal, activation, and revocation require idempotency.
15. Process-local locks are insufficient for SSL lifecycle.
16. Distributed per-domain fencing is mandatory.
17. Renewal monitoring uses Hangfire and no second scheduler.
18. Provider rate limits are first-class workflow constraints.
19. Revoked certificate rows never return to Active.
20. Expired/invalid active certificates downgrade domain SSL state.
21. StoreConfig never owns certificate state.
22. Theme/CMS/Catalog/POS/Checkout/Builder never own certificate state.
23. Edge admission must ask Shopixy policy before issuing for a hostname.
24. UI must not display fake SSL authority.
25. Disaster recovery restores Caddy storage first and reissues only as fallback.

## 32. Decision Register

| Decision | Canonical Choice | Rejected Alternatives | Reason | Authority | Wave |
| --- | --- | --- | --- | --- | --- |
| TLS terminator | Caddy edge | Kestrel, Cloudflare, NGINX, Traefik, HAProxy | Caddy is observed; app has no cert loading; others not observed | `deploy/staging/Caddyfile:1-72` | 5.5 |
| ACME owner | Edge-managed Caddy ACME | Shopixy-managed ACME; CDN-managed ACME | Avoids app private-key custody; matches observed edge | DOMAIN-5.0 lines 151-180 | 5.3/5.5 |
| Private key store | Caddy-managed encrypted storage | ordinary DB; app filesystem; env vars | app must not hold keys | DOMAIN-5.0 lines 216-231 | 5.5 |
| Challenge strategy | HTTP-01 primary, TLS-ALPN-01 fallback | DNS-01 for tenant domains | read-only DNS; no DNS credentials | `PointOfSale.API/Program.cs:382-437` | 5.3/5.5 |
| Host resolution | Verified remains resolvable | only SslActive; scheme split | preserves current resolver and failure downgrade | `PersistenceDomainResolver.cs:137-142` | 5.4 |
| Activation | SNI edge proof before SslActive | issued-by-CA alone | edge serving is the real user-facing state | DOMAIN-5.0 lines 199-214 | 5.5 |
| Primary promotion | requires SslActive | Verified promotion | DB requires cert for Primary/SslActive | `DomainWriteService.cs:1177-1182`, `DomainConfiguration.cs:43-48` | 5.8 |
| Renewal scheduler | Hangfire monitor/reconciler | second scheduler | Hangfire is platform scheduler | `Program.cs:2925-2943` | 5.6 |
| Revocation | authorized Shopixy command, edge/provider execution | tenant arbitrary revoke; no revocation | security and lifecycle requirement | DOMAIN-5.0 lines 321-333 | 5.7 |
| DR | encrypted Caddy storage backup, reissue fallback | mass reissue first | rate-limit protection and faster restore | DOMAIN-5.0 lines 392-407 | 5.7 |

No decision is BLOCKING.

## 33. Traceability Matrix

| DOMAIN-5.0 finding | DOMAIN-5.1 decision | Future wave | Certification gate |
| --- | --- | --- | --- |
| No ACME implementation | Caddy-managed ACME through provider abstraction | 5.3/5.5 | provider contract + Caddy edge tests |
| No private-key architecture | Caddy-only encrypted storage; DB safe refs only | 5.2/5.5 | no-private-key leakage tests |
| TLS ownership undefined | Caddy is canonical TLS terminator | 5.5 | edge activation proof |
| No activation path | external SNI probe before `SslActive` | 5.5 | activation integration tests |
| Persistence insufficient | extend certificate metadata/state/fencing | 5.2 | migration/schema certification |
| Host resolution accepts Verified | preserve Verified resolution | 5.4/5.10 | resolver regression tests |
| Primary promotion mismatch | require SslActive for Primary | 5.8 | API/service tests |
| No renewal engine | Hangfire monitor with Caddy renewal reconciliation | 5.6 | scheduler tests |
| No revocation engine | authorized revocation via edge/provider layer | 5.7 | revocation tests |
| UI fake actions | remove/disable until backend authority exists | 5.9 | frontend integrity tests |
| No provider limits | provider-policy/rate-limit model | 5.3/5.6 | rate-limit tests |
| No SSL concurrency guard | distributed per-domain lock/fencing | 5.2/5.4 | multi-instance tests |

## 34. Implementation Readiness Gate

DOMAIN-5.2 may begin when this document is accepted.

Gate status:

- [x] TLS termination is frozen: Caddy edge.
- [x] certificate owner is frozen: Shopixy policy by TenantId/DomainId; Caddy private material.
- [x] ACME ownership is frozen: Caddy-managed ACME.
- [x] challenge strategy is frozen: HTTP-01 primary, TLS-ALPN-01 fallback, DNS-01 unsupported for tenant custom domains.
- [x] private-key storage is frozen: Caddy-managed encrypted storage.
- [x] certificate persistence is frozen at blueprint level: safe metadata plus opaque refs; DOMAIN-5.2 implements schema.
- [x] edge distribution is frozen: Caddy storage/automation, no app file copying.
- [x] activation proof is frozen: external SNI edge probe.
- [x] lifecycle state machine is frozen.
- [x] renewal policy is frozen: Caddy renewal execution plus Hangfire monitor; thresholds config-driven.
- [x] revocation policy is frozen.
- [x] expiry behavior is frozen.
- [x] concurrency strategy is frozen.
- [x] tenant isolation is frozen.
- [x] API contract is frozen.
- [x] frontend contract is frozen.
- [x] observability is frozen.
- [x] disaster recovery is frozen.
- [x] provider abstraction is frozen.
- [x] edge abstraction is frozen.
- [x] test constitution is frozen.

Implementation caveat:

- No SSL/TLS implementation exists yet. Passing this gate authorizes DOMAIN-5.2 architecture implementation work only; it does not certify runtime SSL.

## 35. Final Blueprint Certification

CERTIFICATION: PASS WITH OBSERVATIONS

PASS basis:

- All implementation-critical DOMAIN-5.1 architectural decisions are frozen.
- DOMAIN-5.2 can begin without inventing TLS termination, ACME ownership, challenge strategy, private-key boundary, persistence target, activation proof, renewal model, revocation model, host-resolution coupling, or implementation waves.

Observations:

- Source reality remains non-SSL-capable until DOMAIN-5.2+ implement the blueprint.
- Production Caddy configuration, exact Caddy storage backend, and deployed Caddy version syntax must be verified in DOMAIN-5.5 implementation, but the ownership model is not open: Caddy remains the canonical TLS edge and private-key holder.
- DOMAIN-4 remains certified and must not be redesigned by DOMAIN-5.

CERTIFICATION: PASS WITH OBSERVATIONS

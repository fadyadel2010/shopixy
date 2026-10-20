# Shopixy — Production Readiness Baseline and Execution Roadmap

**Date:** 2026-10-19. **Decision: NOT READY for VPS purchase as a deployment commitment, and NOT READY for public launch.**

This is analysis and planning only. No roadmap implementation, build, test, package installation, EF command, Docker operation, database connection/change, DNS change, restart, or reference-server connection was performed. This report is the only task-authored file. Every implementation and validation action below requires a subsequent explicit authorization.

## 1. Executive decision

Most application-readiness work can and should be completed **before buying a VPS**, using an independently approved Linux VM/CI environment and disposable, freshly initialized services. That environment is neither the normal development infrastructure nor the existing reference/test server.

The recommended provisional deployment is **one Linux host with Docker Engine/Compose, one public Caddy edge, five application image types, singleton data services, and external media plus off-host recovery storage**. Kubernetes is not an application requirement. Removing Kubernetes/operators from the initial profile avoids paying their operational and resource costs on one host; it does not remove the need for health-based traffic admission, isolation, supervision, backups, or operational ownership. This recommendation needs user approval; it is not an already agreed orchestration decision.

Key decisions and boundaries:

1. **Fresh production only:** replay the reviewed migration chain, initialize required reference data, and securely provision the first administrator. Import no development/load-test database, broker messages, projections, Redis keys, sessions, objects, or experimental images.
2. **.NET 10 LTS is the agreed destination.** Plan a coherent SDK/framework/EF/Npgsql update and compatibility validation, not a target-framework-only edit.
3. **Recommend MongoDB 8.0, latest approved patch**, on verified supported CPU/OS hardware. Do not carry forward MongoDB 4.4 or assume the older machine's AVX limitation exists on a new VPS. Published driver compatibility is favorable; Shopixy runtime compatibility is still untested.
4. **Search remains an application blocker.** NEST 7.17.5 plus secured OpenSearch is not a supported compatibility assumption. Select and validate a maintained engine/native-client pair before final image selection.
5. Keep the **existing shared API, embedded workers, and tenant-resolution authority**. Three frontend bundles are real; Affiliate and Super Admin are routes within Web, not new backends.
6. The candidate **8 vCPU / 34 GB / 200 GB** can be a conditional small-launch target, not a capacity promise. The budget below leaves approximately **7 GiB RAM and 55 GiB disk** against conservative decimal units, before provider/filesystem differences are reconciled. Growth, workload, IOPS, and recovery measurements may invalidate it.
7. **Gate A, Gate B, and Gate C are separate and none is passed.** Application readiness does not prove host readiness or public-launch readiness.

Principal remaining blockers are release-source selection; retained quality failures and unexecuted promotion scenarios; current forensic durability/schema gaps; public CommerceHub identity closure; .NET 10 revalidation; absent production packaging/configuration; authenticated integrations; routing/TLS; and measured off-host restore/operations proof.

**Next single implementation task:** **R1 — seal a reviewed, isolated release-candidate source manifest while preserving this entire working tree** (§9). Do not start with the framework upgrade or infrastructure bootstrap.

## 2. Evidence and current baseline

### 2.1 Evidence labels and boundaries

- **SOURCE:** verified by reading current working-tree files in this task; not a runtime pass.
- **VENDOR:** official documentation/package metadata retrieved on 2026-10-19; not a Shopixy compatibility test. Moving documentation and patch releases must be rechecked at version freeze.
- **PRIOR:** evidence from `SHOPIXY_VPS_PRODUCTION_READINESS_AUDIT.md` or earlier reports; not rerun here.
- **PROPOSAL / ESTIMATE:** a recommendation or planning allowance requiring approval and measurement.
- **UNKNOWN:** not verified; the closing check is identified below.

All application paths below are repository-relative. **AUDIT** means `.ai/reports/SHOPIXY_VPS_PRODUCTION_READINESS_AUDIT.md`; **PHASE8** means `.ai/reports/SHOPIXY_STOREFRONT_MANAGEMENT_PHASE8_FINAL_CROSS_DOMAIN_INTEGRATION_CERTIFICATION_20261018.md`.

**Environment separation:** A is existing development source/infrastructure; B is the independent reference/test server; C is future, unpurchased production. B was not contacted. No current DNS, service health, database state, or installed-version claim is inferred from the old snapshot. The infrastructure repository is a reference only; neither repairing it nor certifying B is on this roadmap's critical path.

### 2.2 Release-source state and preservation

**SOURCE/Git:** branch `main`, HEAD `5bd9bfc0e57c23116cd1c4fba456bf0c9f7ccad4`. Entry status remains **4,857 tracked deletions, nine tracked modifications, 22 untracked entries**. The additional entry relative to AUDIT's entry inventory is the completed audit report. The index has no staged diff. HEAD is not the full release source.

The nine modified files are:

- `PointOfSale.API/Controllers/StorefrontChatController.cs`;
- `PointOfSale.Storefront/src/services/api/httpClient.ts`;
- under `PointOfSale.Tests/`: `AbandonedCart/AbandonedCartNativeSchedulerTests.cs`, `Analytics/ProviderDeliveryContractTests.cs`, `Architecture/PosAdministrativeAuthorityCertificationTests.cs`, `Architecture/StorefrontAnalyticsAuthorityCertificationTests.cs`, `CouponProjectionConsumerConsistencyTests.cs`, `Infrastructure/Phase5HttpCacheTests.cs`, `Integration/ProductVariantPriceDistributedReliabilityIntegrationTests.cs`.

Untracked work includes PHASE8, AUDIT, the HTTP privacy regression, configured-scope tests, browser helpers, and quality-closeout runners. Large deletion sets must be reviewed by category; even generated-looking paths are not authorization to discard or stage them wholesale. R1 must decide inclusion/exclusion explicitly, preserving privacy/cache fixes and their evidence unless a reviewed equivalent replaces them.

A content fingerprint was captured before authoring this report across **8,458 existing tracked/nonignored-untracked files and 4,857 missing tracked paths**, approximately 407 MB read. Aggregate SHA-256 excluding the requested output path: `2c44e8b88ee9e4114338c4a225fa1f98b34fc8dadfd1c2340f09133c5ce284af`. Individual contents and sensitive values were not emitted. Final preservation verification is recorded in §10; ignored files are outside this Git-based fingerprint and were not task write targets.

### 2.3 Focused reconciliation with the previous audit

| Finding | Current classification / evidence | Roadmap consequence |
|---|---|---|
| Actual process boundaries | **SOURCE:** `PointOfSale.sln:6–13`; four solution projects, only API is a production server. `PointOfSale.Web/src/App.jsx:149–188,222–263` contains Affiliate/Super Admin routes. | Five natural application image types; no speculative worker, MVC, Affiliate, or Super Admin server. |
| Framework/dependency baseline | **SOURCE:** all four solution `.csproj` files target net9.0; `global.json:3–5` pins 9.0.308. Infrastructure references MongoDB.Driver 3.8.0, NEST 7.17.5, Npgsql EF 9.0.2, MassTransit EF 8.5.7 (`:10–32`). | Coherent .NET 10 plan, not blind package-major upgrades. |
| Production packaging absent | **SOURCE:** no authoritative Dockerfile, `.dockerignore`, Compose definition, or production workload set found in the application tree. Existing `deploy/staging/README.md:22–58` and `Caddyfile:15–78` are host/staging patterns. | Author the selected production topology later; do not claim existing manifests are deployable. |
| Quality closure | **PRIOR, document re-read:** PHASE8 `:7–15,144–201,258` retains 34 failures and three unexecuted distributed promotion scenarios. Source fixtures still exist. | Reproduce safely and resolve individually; these are not 34 proven money/tenant-corruption defects. No blanket green baseline. |
| Test coverage gaps | **SOURCE:** `PointOfSale.Tests/PointOfSale.Tests.csproj:38–48` excludes WhatsApp tests and most Accounting files, with selected Accounting re-inclusions. CommerceHub `package.json:6–10` defines no test command. | Inventory effective compiled/executed coverage; critical exclusions require restored/equivalent tested coverage, not silently inherited green counts. |
| Schema history | **SOURCE:** 53 unique active Migration IDs; latest `20261013210000_CanonicalAddressEvidenceFoundation`. **PRIOR:** A had matching history/canonical guards. | Fresh full-chain replay and model/schema checks still mandatory; A's old state proves nothing about empty C. |
| Forensic hardening | **SOURCE:** active chain lacks creation of the legacy `forensic.reject_append_only_evidence_mutation` / `TRG_ForensicAppendOnly` contract; role script still requires it and legacy `LedgerEntries` (`ProvisionForensicEvidenceRoles.sql:40–43,54–78`). | Reconcile current table inventory and forward migrations before provisioning; do not execute archived scripts unchanged. |
| Audit handoff | **SOURCE:** `AuditService.cs:163–178` falls back after SQL failure; `AuditQueue.cs:15–26` uses an unbounded in-memory channel. | Outage-plus-process-crash evidence loss remains a concrete implementation gap, not fixed by backups. |
| Tenancy | **SOURCE:** `PersistenceDomainResolver.cs:103–119` resolves active SQL slugs; `:124–186` separately handles custom-domain/base-URL authority. | Reuse platform subdomains and original Host, not a tenancy rewrite or extra domain-verification prerequisite for platform slugs. |
| B health, firewall, disk, backups and live drift | **PRIOR only:** AUDIT §§4–5,10–11. | Not revalidated, not production blockers requiring B repair. They explain why B's image/topology/certifications must not be promoted. |
| DNS / development login privileges / development DB size | **PRIOR only:** AUDIT §§9–10. No queries this task. | Obtain current production ownership/network/role evidence later; do not reuse A's runtime identity or size as a capacity forecast. |

Normal projection lag, fail-closed financial readiness, serializable conflict retries, and SQL-first reconciliation are **accepted architectural behavior**, not defects to remove. Authority rules remain `.ai-context/ARCHITECTURE_TRUTH.md:93–105,155–185,419–460` and `docs/operations/operational-recovery-runbooks.md:214–262`. Older `.ai-context` entries claiming missing identity features or completed forensic migrations must be reconciled with current code, not copied as current truth.

## 3. Initial-launch scope: confirmed constraints versus proposed choices

### 3.1 Confirmed by the request

- Fresh database and fresh service state, with reviewed migrations/reference seeds/administrator provisioning.
- .NET 10 direction; no upgrade implemented here.
- Preserve A, B, all existing working changes, and all prior evidence.
- Candidate offer is provisional; architecture must not inherit reference-server assumptions.

### 3.2 Proposed planning envelope — requires approval

Budget all three SPAs, the shared API, and private Baileys so a full-surface launch is not sized as a POS-only application. Public CommerceHub/Affiliate/WhatsApp activation is conditional on their feature gates. Merely declining to publicize a URL does not disable reachable APIs, consumers, or jobs.

| Surface / feature | Proposed initial treatment | Remaining approval / gate |
|---|---|---|
| POS/Admin + tenant Storefront | Core initial launch; platform tenant subdomains only | Tenant onboarding, catalog/publication, pricing, physical inventory, order/payment/fulfillment and security evidence required. |
| CommerceHub | Include bundle and resource allowance; **public activation conditional** | Customer/session gates, EGP financial policy, evidence durability, vendor controls, payouts/refunds and operational acceptance cannot be waived. A POS/Storefront-only first release is a viable separate scope decision. |
| Affiliate + Super Admin | Existing Web `/affiliate/*` and `/super-admin/*`; shared API | Protect platform authority and payout boundaries; no separate deployment. Prefer no extra Affiliate hostname initially. |
| Baileys | Include one private service; real messaging only for approved features/accounts | API startup currently requires URL/key regardless of feature selection (`SensitiveConfigurationValidator.cs:25–35`). Omitting it needs a separately reviewed feature contract, not dummy credentials. |
| Payments/carriers | Enable only explicitly selected and certified lanes | COD/manual is a proposed smaller initial scope, not user-approved policy. Real collection/settlement/evidence still required; manual does not mean bypass finance. Live gateways/carrier automation need external accounts and approval. |
| SMTP and media/evidence storage | Required externally for selected customer/commerce flows | Sending-domain/delivery and signed-object browser access must work; these are not safely replaceable by development fallbacks. |
| Merchant custom domains, AI/OAuth providers, price overrides | Defer unless explicitly selected | Keep unsupported actions unavailable through enforced feature/authorization paths. Existing override activation freeze is preserved. Password/customer identity needed for launched surfaces remains in scope. |
| Apex / platform hosts | Proposed: `app.shopixy.com`, `marketplace.shopixy.com`, `api.shopixy.com`, `*.shopixy.com`; apex content/redirect undecided | Owner approves actual hosts and preserves current apex service until cutover. Reserve `marketplace` before registrations; reserve `affiliate` too if ever selected. Current `StoreSlug.cs:11–23` omits both. |

**Unresolved business inputs:** initial tenants, peak browsing/checkouts, SKU/variant/media volume, background replay/rebuild rates, provider selection, launch date, downtime tolerance, retention/legal obligations, and responsible operator. Gate A test scope and Gate B sizing cannot be signed without explicit values. No tenant-count or throughput guarantee is made here.

## 4. Provisional lean architecture and resource budget

### 4.1 Recommended profile L1 — single-host containers, no Kubernetes initially

```text
Internet HTTPS -> one Caddy edge -> exact-host static Web / Storefront / CommerceHub
                            \-> /api and /hubs -> one shared API + existing workers
                                                   | PostgreSQL primary
                                                   | MongoDB one-member replica set
                                                   | Redis API primary
                                                   | RabbitMQ one broker
                                                   | one supported search node
                                                   | private Baileys -> Redis session primary
External: SMTP, application object storage, independent encrypted backups/evidence retention,
          registry/CI, authoritative DNS/ACME DNS-01, external uptime/notification destination
```

Edge matches `/api` and `/hubs` before static fallback on every frontend host, preserving original Host. It does not turn browser Origin or raw tenant headers into authority. Data/management ports have no public bindings. Container networks separate edge, application, data, and monitoring paths; shared-network membership alone is not a full least-privilege policy. Define actual permitted connectivity, authentication and TLS in R6/R7/R12.

| Component | Classification / selected simplification | Required boundary |
|---|---|---|
| API | **Required**, one container, proposed internal8080 | Hosts REST/auth/webhooks/SignalR, MassTransit, Hangfire and reconciliation. No additional generic worker replica. |
| Web, Storefront, CommerceHub | Web/Storefront **required**; CommerceHub **selected-feature required**; three independently built static images | No Vite development servers. Storefront includes `draft-preview.html`; shared `theme-authority` is in build context. Affiliate/Super Admin reuse Web. |
| Baileys | **Required by present startup contract**; real use feature-selected | Internal3000, matched API keys, one owner per session, encrypted state on separate Redis. |
| PostgreSQL | **Required**, one supported18.x primary, direct client pools initially | No three replicas, CNPG operator, Barman operator or PgBouncer initially. A supported standalone backup tool must replace operator backup functionality, with base backups + WAL/PITR if the approved RPO requires it. |
| MongoDB | **Required** for present full app; recommend8.0 latest approved patch, one authenticated replica-set member | One member enables replica-set semantics but is **not HA**. Fresh bootstrap, indexes, serialization and advertised DNS need proof. Do not import old4.4 files. |
| Redis API | **Required**, one writable primary | Cache shares infrastructure with locks/revocation/backplane; not all keys are evictable. Dedicated production instance, bounded memory, explicit persistence/recovery and fail-closed policy. |
| Redis Baileys | **Required when Baileys is present**, second small writable primary | Separate non-evicting encrypted-session state from API cache churn using existing independent host/port settings. This is two purposes, not replication/HA. |
| RabbitMQ | **Required**, one supported broker and actual MassTransit topology | Durable queues, app vhost/user, permissions, retries/errors, bounded consumers. Omit cluster/topology operators; provision reviewed identities and let existing consumer definitions establish required endpoints. |
| Search | **Required for full launch**, one maintained compatible engine/node | Native authenticated/TLS client decision remains open (§5.3); zero replicas for selected singleton indexes, bounded shards/heap, no search dashboard/operator. Do not disable health or SQL visibility guards to save RAM. |
| Object storage | **Better external**, application R2/S3-compatible service | Browser-reachable HTTPS signed URLs, CORS, least privilege, media/evidence recovery. Omit local MinIO; external backup/evidence sink is independently protected and need not be the same provider/account. |
| Ingress/TLS | **Required**, one Caddy edge, DNS-01 wildcard automation | Omit Traefik/MetalLB/cert-manager in L1. Pin Caddy plus required DNS-provider module and protect its ACME state. App-managed merchant-domain Caddy writes remain deferred, not auto-enabled. |
| Monitoring/recovery | **Required**, bounded Prometheus + alert delivery + small private Grafana/exporters and external uptime | Omit full kube-prometheus stack, Headlamp, Kubernetes dashboard, local log/search observability cluster and in-host registry. CI/builds are off-host. |

Legacy n8n is not an initial-launch dependency: current native recovery consumers/jobs and `PointOfSale.Infrastructure/Migrations/20260930145500_MAIN_RetireN8nRecoveryCallback.cs:10–11` supersede the archived workflow assets. Standalone backup/restore functionality remains mandatory even though its reference operators are omitted.

**Availability decision:** this is a low-complexity, single-failure-domain launch profile, with maintenance windows and host-loss recovery, not host-level high availability. Extra replicas on this VPS would not change that fact. If downtime/RTO expectations require failover, choose independent hosts or managed HA services instead.

### 4.2 L1 planning envelope — all numbers below are ESTIMATES

CPU is a proposed sustained allocation in vCPU equivalents, **not measured use, a guaranteed share, or generated Compose/Kubernetes requests**. Memory is an initial whole-component ceiling/allowance to validate, including workers/caches/native allocations as applicable. Persistent disk is a **planned high-water envelope including growth/indexes/WAL/oplog/retained transport state**, not an initial data-size forecast or automatically enforced quota.

| Component | Planned CPU | RAM GiB | Persistent/high-water GiB | Additional temporary GiB |
|---|---:|---:|---:|---:|
| OS, Docker/runtime, one edge, certificates/config; retained current+rollback images | 0.50 | 2.000 | 25 | 0 |
| Shared API incl. jobs/outboxes/consumers, keys, secure artifacts and bounded durable audit handoff | 1.50 | 4.000 | 3 | 0 |
| Three static servers combined | 0.10 | 0.375 | 0; images above | 0 |
| Baileys process; auth state below | 0.25 | 1.000 | 0 | 0 |
| PostgreSQL primary, data/indexes/growth and bounded local WAL | 0.90 | 4.000 | 24 | 0 |
| MongoDB member, data/indexes/growth/oplog | 0.50 | 3.000 | 16 | 0 |
| Redis API, AOF/RDB allowance | 0.15 | 1.000 | 4 | 0 |
| Redis sessions, AOF/RDB allowance | 0.10 | 0.500 | 2 | 0 |
| RabbitMQ broker and bounded disk queues | 0.30 | 1.500 | 8 | 0 |
| One search node, native memory + heap + cache | 1.00 | 5.000 | 24 | 0 |
| Prometheus, alerts and exporters; bounded retention | 0.20 | 1.000 | 6 | 0 |
| Private Grafana state | 0.05 | 0.250 | 1 | 0 |
| Bounded/rotated host and container logs | included above | included above | 4 | 0 |
| Serialized backup/maintenance/compression/rewrite workspace | 0.30 | 1.000 | 0 | 14 shared |
| **Total planned consumption / ceilings** | **5.85** | **24.625** | **117** | **14** |

Arithmetic and admission rules:

- Conservatively, **34 GB =31.66 GiB**, **200 GB =186.26 GiB**. Planned RAM headroom is **about7.04 GiB**; planned disk use including maintenance is **131 GiB**, leaving **55.26 GiB /29.7%** before filesystem/provider differences. CPU reserve is **2.15 vCPU**; bursts/IOPS must still be measured.
- The OS/runtime25GiB disk row comprises approximately10GiB OS +14GiB image layers/rollback pulls +1GiB edge/config state. Logs and maintenance are separate, not omitted or double-counted. No production-host build cache or local full backup retention is assumed.
- Require **at least25% planned free disk**, including the largest approved maintenance operation, and never plan below20%. With a131GiB envelope the actual usable filesystem must be at least174.7GiB; use **176GiB minimum** at admission and prefer more. Reserved blocks/partitions reduce available space; measure the real filesystem before accepting C.
- The14GiB scratch envelope requires streamed external backups, bounded local WAL, serialized jobs, and bounded AOF/index rebuild operations. A full local24GiB dump, dual full search indexes beyond the24GiB search envelope, or a restore requiring larger scratch **invalidates this budget**. Rebudget or move the operation/service off-host; do not consume the free-space reserve silently.
- Explicitly bound PostgreSQL connection pools/work memory, Mongo WiredTiger cache, search heap/shards, Redis maxmemory below process allowance, Rabbit queues/disk alarms, Hangfire workers, consumer concurrency, logs and metrics retention. Mongo must be able to burst/use multiple cores; its0.5 planning share is not a proposed single-core CPU quota.
- Configure real volume ownership, capacity alarms and expansion/replacement procedures. Named volumes/local-path requested sizes are not filesystem quota enforcement. No broad automatic image/PVC/volume pruning is permitted.
- Prefer one-at-a-time API replacement with a maintenance window until overlap safety and spare rollout memory are proved. Running two fully budgeted APIs during rollout would consume the reserve. Supervisor restart policy and edge readiness admission must be tested; a container marked healthy/unhealthy is not itself traffic control.

**Measurement basis, not a forecast:** AUDIT `:105–109,140–144,475–499` recorded B's recently restarted low-load API at440MiB, search around4.7GiB per pod and aggregate node memory around21GiB. These **PRIOR point samples** informed caution, not the above caps. Modern Mongo/search versions, .NET10, simultaneous workers, data growth and backup load can materially change usage. The old default source9.3CPU/34.25GiB/510GiB stack is rejected; no fresh sizing claim is based on B.

### 4.3 Alternatives if L1 does not fit or does not match operating preferences

| Alternative | Benefit / planning change | Conditions |
|---|---|---|
| L1 with managed/external search | Removes approximately1 planned CPU,5GiB RAM and24GiB local storage, before client/network overhead | Price/latency/egress/private access, authenticated native-client contract and recovery/reindex must be approved. Does not solve application defects. |
| Managed PostgreSQL and/or MongoDB | Reduces local data/I/O/backup operations; can buy real service-level resilience | Vendor support, extensions/roles, transaction semantics, TLS, network dependency, PITR/exports and costs need validation. Fresh production still required. |
| Lean single-node K3s instead of Compose | Valid if operator/team requires Kubernetes | **Alternative, not included in L1.** Estimate at least an additional0.5CPU/1.5GiB RAM/4GiB disk for control plane/CNI/controllers, plus every chosen operator/backup component. Result is about26.125GiB before extra operators, leaving only5.54GiB on decimal34GB: below this plan's6GiB minimum margin. Externalize search or enlarge target and remeasure; no automatic full operator fleet. |
| Larger machine, especially disk, or independent service hosts | More operational/growth margin; independent hosts can address failure domains | Obtain provider-specific CPU/IOPS/support/network evidence. More RAM alone does not solve disk growth or recovery. |
| Explicit POS/Storefront-only release | Reduces exposed feature certification burden | Must actually enforce the scope in UI/API/jobs and preserve shared safety contracts. Mongo/Redis/Rabbit/search/Baileys are not automatically removable. |

Do not purchase based only on the provisional totals. Confirm usable RAM/disk, sustained CPU/steal limits, AVX exposure, disk durability/IOPS, bandwidth/backup egress, supported OS/kernel, console/recovery and provider firewall first.

## 5. Version and integration decisions

### 5.1 .NET 10 and toolchain upgrade path

**VENDOR:** Microsoft's support policy, updated2026-10-13, lists **.NET10 LTS through2028-11-14**, latest runtime patch10.0.13, and **.NET9 through2026-11-10**. A runtime patch number is not an SDK version. Pin a supported stable10.0 SDK and matching supported runtime/base-image patch at implementation time; do not copy a documentation example SDK number blindly.

**SOURCE baseline:** API `PointOfSale.API.csproj:11–33` includes Hangfire1.8.23/PostgreSql1.20.12, MassTransit8.5.7, ASP.NET9 packages, Stripe52.1.0, Swashbuckle6.5.0 and OpenTelemetry1.15.x including a beta Prometheus exporter. Infrastructure `PointOfSale.Infrastructure.csproj:10–34` includes EF9/Npgsql9, MongoDB.Driver3.8.0, StackExchange.Redis2.8.24, NEST7.17.5 and compression libraries. Tests use EF InMemory/SQLite9, Roslyn4.11 and xUnit2 (`PointOfSale.Tests.csproj:10–19`).

Required small upgrade steps in R3:

1. Align the four solution TFMs, SDK, Microsoft ASP.NET/Extensions/EF runtime/design/test-provider packages and EF tooling; inventory diagnostic/helper projects too. Choose Npgsql EF10 and its compatible Npgsql dependency coherently. Do not leave mixed EF majors.
2. **VENDOR package metadata:** MassTransit.EntityFrameworkCore8.5.7 has a native `net10.0` dependency group requiring EF Relational10.0.0 and MemoryCache10.0.0. A forced upgrade to another MassTransit major is not established as necessary. Confirm the full resolved graph and tested outbox/inbox behavior; package metadata is not execution proof.
3. Hangfire.PostgreSql1.20.12 declares a minimum Npgsql6.0.11 dependency in its .NETStandard2.0 group. This does not prove compatibility with the Npgsql version ultimately resolved alongside EF10. Verify schema initialization, distributed/advisory locks, job scheduling/restart and connection budgets. Omit transaction PgBouncer initially to remove an avoidable session-lock uncertainty.
4. Review .NET10 `BackgroundService.ExecuteAsync` scheduling, configuration null/array behavior, JSON serialization, proxy/cookie/auth behavior, tracing/logging, shutdown, OpenAPI and native Linux dependencies. Startup readiness must await essential initialization explicitly rather than rely on old synchronous worker ordering.
5. Review Npgsql10 array `Contains` translation/index plans and generated-column behavior, EF10 update/query API changes, model snapshot differences and SQLite timestamp behavior in tests. Do not apply SQL Server-specific EF release-note changes to PostgreSQL by analogy. Do not introduce new JSON mappings or inferred schema rewrites as incidental upgrade work.
6. Revalidate tenant interceptors, xmin/optimistic concurrency, serializable retry, uniqueness, money precision, outbox/inbox schemas, seeds, auth/CSRF, Data Protection key continuity, metrics and graceful termination. Trimming/NativeAOT are not initial-launch goals.

**Node recommendation:** pin supported **Node24 LTS**, current approved patch, for frontend builds and provisional Baileys runtime. Official schedule gives Node20 EOL2026-04-30 and Node24 EOL2028-04-30. Node26 is not yet LTS on this report date. Package engine acceptance alone is not Baileys certification.

**SOURCE lockfiles:** Web/Storefront Vite5.4.21; CommerceHub Vite8.2.1 and plugin-react6.0.5 require `^20.19.0` or `>=22.12.0`; Web jsdom29.0.2 requires newer Node patch levels. Baileys lock resolves **7.0.0-rc14**, not merely package.json's `^7.0.0-rc.9`, with redis4.7.1 and uuid13.0.2. Node24 satisfies the inspected numeric engine ranges, but ESM/CommonJS/native dependencies, QR/session lifecycle and RC support/API stability need tests. Keep builds reproducible with all four lockfiles; do not deploy the old generic Node20 minimum from project prose.

### 5.2 MongoDB: fresh supported version, not an in-place4.4 migration

**Recommendation:** MongoDB **8.0 latest approved patch** is the initial compatibility target, subject to edition/license, OS/CPU, security and runtime validation. It is a mature maintained branch with a longer horizon than7.0. MongoDB9.0 is newer and supported, but is not selected solely for being newest.

Verified vendor evidence:

- MongoDB lifecycle table lists4.4 EOL **2024-02-29**,6.0 EOL2025-07-31,7.0 EOL2027-08-31,8.0 EOL **2029-10-31**,9.0 EOL2031-10-31. These are published lifecycle schedules, not a purchased Community support SLA or proof that an old Percona image has equivalent coverage.
- The official C# compatibility table explicitly marks **driver3.8–3.9 fully compatible with server8.0** and **driver3.5.2–3.12 compatible with .NET10**. Driver3.8–3.9 is only partially compatible with server9.0. Thus neither a Mongo major-client migration nor retaining4.4 is justified merely by the .NET10 move.
- Vendor production notes specify x86_64 minimum microarchitectures (Intel Haswell-or-later Core / Tiger-Lake-or-later Celeron/Pentium; AMD Bulldozer-or-later), with AVX required from the5.0 generation; ARM64 has different minimum architecture requirements. **AVX2 is not asserted here as a universal minimum.** Obtain exact requirements for the selected binary and provider's guest CPU; containerization cannot manufacture absent CPU instructions.
- Mongo8 tuning differs from4.4-era recipes: vendor notes recommend THP enabled for8.0, bounded WiredTiger cache under container limits, and version/OS/kernel-specific platform checks. Do not copy old host tuning blindly.

**Before purchase:** get provider confirmation of architecture, CPU model and exposed virtualization flags, not just a vCPU count. **On a disposable production-like host and later C:** verify guest CPU/OS/kernel and boot the exact pinned image without instruction faults; verify auth/TLS, member DNS, restart, indexes, aggregation/update/freshness behavior and backup restore.

Fresh C avoids a4.4→5→6→7→8 in-place data upgrade: initialize new supported volumes/FCV directly and create projections from fresh SQL. Do not attach or restore old4.4 data files. Any later migration of existing production Mongo state would need its own sequential vendor-supported upgrade/FCV/tool/backup plan; that is **not** this initial launch. `MongoDB/Services/MongoDbContext.cs:35–40` constructs the client from a connection string; feature support still requires exercising actual repositories/serializers/unique and TTL indexes. The targeted Infrastructure search found no explicit transaction/change-stream calls, but that is not a whole-repository guarantee that a standalone server is sufficient; a one-member replica set is the conservative proposed shape.

### 5.3 Search: resolve engine/client before claiming readiness

**SOURCE:** `ElasticsearchServiceCollectionExtensions.cs:48–82` builds `Nest.ElasticClient`, enables API-versioning headers, and does not configure explicit authentication/CA options. `SystemDependencyHealthChecks.cs:214–253` treats disabled search as Unhealthy. Search is used beyond one Storefront box: product, CMS/category, marketplace, service and affiliate indexing/reindex paths depend on its contracts.

**VENDOR:** OpenSearch explicitly states Elasticsearch clients are not fully compatible with OpenSearch2.0+ and recommends its native clients. Elastic's lifecycle table lists Elasticsearch7.17 support ending **2026-01-15**; choosing an old7.x server just to preserve NEST is not an acceptable new production baseline. API compatibility headers do not settle authentication, TLS, product checks or query semantics.

**Provisional preference:** one maintained **OpenSearch3.x + official OpenSearch.Client/OpenSearch.Net** pair, if the bounded R6c spike demonstrates the current mappings/analyzers/queries can be retained through reviewed adapter changes. **Alternative:** maintained Elasticsearch9.x + official matching Elastic .NET client, especially if supported vendor hosting is preferred. The exact server/client patches, .NET10 support, licensing and migration scope remain **UNRESOLVED — Gate A blocker**; neither pair has been tested here. Do not run both engines in the initial profile.

R6c must cover every existing search interface, multilingual analyzers, serializers/mappings, aliases/versioned indexes, bulk writes, counts/facets/completion, tenant-scoped identifiers, SQL overlays/tombstones, retry/error parsing, authenticated health, CA/SNI validation and reindex/recovery. Use least-privilege runtime/index-initializer roles. No product-header spoofing or certificate-validation bypass. A green Ping alone is insufficient. Preserve existing SQL authority services; a client replacement is not permission to redesign pricing or visibility.

### 5.4 Redis, RabbitMQ, PostgreSQL and remaining version choices

| Integration | Proposed decision / current evidence | Required closing validation |
|---|---|---|
| PostgreSQL | Supported18.x current approved patch; vendor lifecycle through2030-11-14. Fresh production, direct connections, standalone primary. | Full reviewed EF10 chain replay, privileges, Npgsql/Hangfire semantics, bounded connections/WAL, encrypted off-host base/WAL recovery. No inherited4000-connection reference tuning. |
| Redis server | Prefer a currently maintained stable Redis8.x release after edition/license/security review; exact minor/patch and applicable **Open Source** maintenance policy remain unresolved. | Verify against StackExchange.Redis2.8.24 and Node redis4.7.1, scripts/locks/JTI/rate limits and TLS. The fetched Redis Software lifecycle page is **not** evidence for Open Source EOL. Valkey is an alternative only after explicit compatibility/support proof. |
| Redis routing/durability | Two singleton writable primaries; no Sentinel/operator/mixed master-replica service. `Baileys/src/services/SessionManager.ts:90–111` accepts only host/port/password/db, not TLS/Sentinel options. | Add/review required TLS/CA options if selected; no public Redis. Both instances non-evicting for control/session keys, explicit TTLs/maxmemory, AOF/fsync/RDB policy and rewrite headroom; test memory-full failure and restart/restore, not just PING. Persist Node secret/salt/key history with encrypted sessions. API cache memory cannot evict revocations or locks silently. |
| RabbitMQ | Prefer maintained4.3.x current patch; vendor page shows4.3.6 and community coverage through2027-01-31. Recheck near launch and own the next upgrade. Do not inherit4.0.5 or presume commercial LTS is free. | One broker with durable queues initially; confirm actual queue type and MassTransit support, app vhost/user/permissions, TLS/CA, publisher/consumer topology, delivery/retry/error queues, SQL outbox drain and restart. Fresh topology may use durable classic queues; quorum queues on one member are not HA and need explicit selection. |
| Rabbit client/health | `Program.cs:2187–2199,2242–2249,2306–2307` wires EF outbox and consumers, not three generic queues. Bus lacks a shared explicit TLS options contract; separate health factory at `SystemDependencyHealthChecks.cs:291–338` lacks TLS and reports failures Degraded. | Unify bus/health configuration and production admission semantics. Verify required bus/worker failures block relevant traffic/actions even if default health HTTP handling accepts Degraded. Do not remove consumers/Hangfire to fit RAM. |
| Object storage | External R2/S3 HTTPS for app; separately protected backup/immutable evidence destination. | Real signed browser upload/download, CORS, CA, permissions and recovery. Do not assume R2 automatically supplies the required WORM/Object-Lock semantics; verify selected provider/retention feature and administrative deletion resistance. |
| Edge/runtime/monitoring | Supported pinned Docker/Compose, Caddy+DNS module, static-server images, Prometheus/alerting/Grafana and backup-tool versions; exact versions remain to be selected. | Vendor support/security/image provenance, DNS-provider plugin, Compose semantics and restore-tool compatibility verified before Gate B. Ubuntu24.04 LTS is a provisional OS choice, not a verified provider image. |

All external data-service connections require authenticated certificate-validated TLS. For same-host private transports, document the threat model and prefer TLS; any private plaintext exception needs explicit security approval, not accidental reliance on missing client options. API→Baileys still requires its private API key even on an isolated network.

## 6. Dependency-ordered implementation roadmap

**Nothing in this section was executed.** Each subphase is a separately reviewable change/evidence unit; split further rather than batch unrelated remediations. Preserve the original worktree throughout. All test/provisioning activity mentioned here is future work on approved isolated targets, never A's shared services or B.

Common evidence for every phase: exact source commit/content manifest, tool/package/image/config revisions, test selection plus failures/skips/exclusions, redacted logs, acceptance decision and preservation check. Do not expose secrets in evidence. Changes touching authority must update the owning architecture documents/tests and `.ai-context` in the future implementation task, not in this report-only task.

**Execution dependency map:** R1 → R2 → R3; then R4b's schema/application work + R4c → R5 → R6a. R6b/R6c can start after R3 and finish against the selected initialized fixtures. R4a's full distributed acceptance follows R6a/R6b/R6c, although assertion classification and component repairs can start earlier. All applicable R4–R6 results → R7 → R8 → R9; R10 operational implementation follows its listed prerequisites. R11 signs Gate A only after all these application contracts pass; R12 signs Gate B; separately authorized R13/R14 lead to Gate C. Final R11 regression reruns cover every shared integration change, including earlier R4b work. There is no requirement to buy a VPS to unblock an isolated compatibility test.

### R1 — Seal release source and launch-scope decisions

- **Objective/scope:** produce a reproducible candidate without changing business behavior or discarding any local work. **Pre-purchase; no external service needed.** Depends on user review of §3's proposed scope and permission for an isolated release workspace.
- **Paths:** `PointOfSale.sln`, the nine files in §2.2, untracked privacy/configured-scope/browser/quality-closeout work, `.gitignore`, AUDIT, PHASE8; review deleted paths by category.
- **Intended changes:** explicit include/exclude manifest with hashes for modifications/untracked/deletions; archive/preserve the starting state through an approved mechanism; materialize only approved release files in a separate clean release checkout/worktree. Keep ignored secrets/dumps/evidence out. Do not use reset/clean/stash to manufacture cleanliness. A selected base commit alone is insufficient.
- **Acceptance/evidence:** owner approves every source delta/category and actual launch surfaces; independent reconstruction gives the same candidate tree hash; product privacy/cache fixes and intended regressions are included or replaced deliberately; original status/content/index unchanged. **Rollback:** abandon only the task-owned release area; never revert A. **No builds/tests/package changes are needed in this first task.**

### R2 — Create a safe test baseline and classify retained failures

- **Objective:** make later red/green evidence reproducible and incapable of touching shared services. **Pre-purchase; approved Linux VM/CI plus disposable services required.** Depends R1; no production credentials.
- **Paths:** `PointOfSale.Tests/PointOfSale.Tests.csproj:38–48`, `PointOfSale.Tests/Integration/ProductVariantPriceDistributedReliabilityIntegrationTests.cs`, `PointOfSale.Tests/AbandonedCart/`, `PointOfSale.Tests/Admin/Phase8IntegrationConfiguredTests.cs`, `tests/quality-closeout/`, frontend test/config/lock files, PHASE8 `:144–201,248–258`.
- **Intended changes:** task-scoped fixtures with allowlisted disposable hosts/DB names, explicit opt-in, denied A/B addresses and production credentials, bounded cleanup ownership, stable solution-root discovery and isolated artifacts. Inventory compiled test exclusions, CommerceHub test coverage and previously blocked exact scenarios.
- **Acceptance:** reproduce and classify all34 retained assertions (documentation/shape versus behavior) and PRICE_CON08/09/10 without treating provisioning refusal or a silent return as PASS. Capture precise failures, coverage gaps and safe reproduction commands. Existing green tests stay green; document expected red tests for R4. **Preservation/rollback:** cleanup only disposable manifested state; no historical report rewrite, fake PASS markers, weakened assertions or shared-output use.

### R3 — Upgrade the isolated candidate to .NET 10

- **Objective:** supported backend runtime with coherent dependencies, no incidental feature/schema redesign. **Pre-purchase; CI/isolated services.** Depends R1/R2 and §5.1 compatibility decisions.
- **Paths:** `global.json`, four solution `.csproj` files, diagnostic/helper `.csproj` files selected for CI, `PointOfSale.API/Program.cs`, `PointOfSale.Infrastructure/Data/ApplicationDbContext*.cs`, owning auth/interceptor/outbox/job tests.
- **Intended changes:** pinned SDK/TFMs/Microsoft10/EF10/Npgsql10, minimal proven third-party adjustments, tool/dependency graph and support inventory; preserve approved runtime package versions where compatible. Separate compiler-only fixes from behavioral changes. Inspect model differences; do not automatically regenerate a baseline migration.
- **Acceptance:** clean restore/build/publish and required tests on Linux; no unresolved dependency downgrades; auth/proxy/JSON/worker startup/shutdown/Data Protection/Hangfire/MassTransit regression evidence. R2's existing reds remain separately identified, not relabeled newly green. Review any actual schema difference as a separate forward change. **Rollback:** prior source/image candidate only; no shared DB was upgraded and no automatic migration Down.

### R4a — Close promotion/configuration quality contracts

- **Objective:** resolve the retained34-case reconciliation and three distributed promotion scenarios on the supported candidate. **Pre-purchase; disposable PostgreSQL/Mongo/Redis/Rabbit/search as required.** Component work depends R2/R3; full distributed acceptance also depends on the compatible R6 service contracts.
- **Paths:** `PointOfSale.Tests/Integration/ProductVariantPriceDistributedReliabilityIntegrationTests.cs`, `PointOfSale.Tests/Architecture/*Promotion*`, `*Coupon*`, `StorefrontConfigurationAuthorityCertificationTests.cs`; owning promotion/configuration consumers, SQL writers and Storefront pricing/configuration clients.
- **Intended changes:** one behavior/contract per change: first equivalent behavioral proof, then correct stale source/document expectations where justified; repair actual replay/repair/cache defects inside existing authority owners. Maintain SQL mutation versions, If-Match, event fences and tenant cache identity.
- **Acceptance:** exact PRICE_CON08/09/10 activation/expiration/duplicate-delivery races pass; SQL→projection/cache/search repair equivalence and cross-tenant/price/tax negatives pass; all34 rows have current, reviewed closure and real replacement assertions where necessary. **Rollback:** revert only the isolated change; retain old failure evidence. No deletion of inconvenient tests or weakening financial/tenant gates.

### R4b — Close selected-surface identity and financial readiness

- **Objective:** remove remaining blocking application defects for the approved public surfaces, not invent new IAM/business features. **Pre-purchase; isolated state and controlled providers.** Depends R3 and R2's coverage inventory; may proceed independently of R4a.
- **Paths:** `PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs`, `CustomerTokenRevocationService.cs`, `Controllers/CustomersAuthController.cs`, POS/SuperAdmin auth boundaries, `PointOfSale.Tests/Architecture/*Authentication*`, `*Payout*`, `*Financial*`, `PointOfSale.Tests/Accounting/`, `docs/architecture/commercehub-authentication-authority-certification-findings.md:9,69–75`.
- **Intended changes:** verify and close the documented CommerceHub refresh rotation/reuse-detection requirement if CommerceHub launches; do not reimplement already present verification/reset/profile/cutoff flows. Restore/equivalently cover critical excluded accounting/WhatsApp contracts. Complete bounded PostgreSQL concurrency tests for checkout/refund/payout/settlement/external-evidence reuse and mixed-currency rejection.
- **Acceptance:** two-tenant actor/session/CSRF negatives; current SQL authorization after logout/role/tenant changes; idempotent exactly-once financial recognition under races, balanced ledger, immutable cost snapshots, EGP CommerceHub authority and preserved uncertain-outcome blocking. No synthetic balance/evidence to satisfy readiness. **Rollback:** source plus schema-compatibility review; no rewriting historical A financial state. External SMTP/live-provider proof remains R13/R14.

### R4c — Implement durable audit handoff and current evidence protections

- **Objective:** close an existing public-marketplace launch blocker without introducing a competing business authority. **Pre-purchase; disposable PostgreSQL + restartable Linux storage; external retention credentials needed only for integration proof.** Depends R3/current table inventory.
- **Paths:** `PointOfSale.API/Services/Audit/{AuditService,AuditQueue,AuditLogBackgroundService}.cs`, `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`, `PointOfSale.Infrastructure/Scripts/{ProvisionForensicEvidenceRoles,VerifyForensicEvidenceDatabaseHardening}.sql`, active migrations and `PointOfSale.Tests/Architecture/{EvidenceDatabaseHardening,MarketplaceProductionReadiness,MarketplaceForensicEvidence}CertificationTests.cs`.
- **Intended changes:** review bounded, encrypted, persisted outage handoff/replay in the existing audit boundary; capacity/failure semantics must not silently drop evidence. Reconcile evidence families against current SQL tables and add reviewed forward trigger/ACL changes, not archived migrations. Separate runtime/evidence owner/migrator authority and external export/retention contracts.
- **Acceptance:** SQL outage + process kill/restart preserves acknowledged audit evidence, retry is idempotent/tenant-scoped, full-spool failures are visible/fail safely, secrets are redacted; runtime cannot UPDATE/DELETE/TRUNCATE protected current evidence. Review conflicts with legitimate lifecycle/retention operations. External immutability is not proven by export creation. **Rollback:** preserve spool/keys/evidence; use forward corrections where protections are irreversible, never delete the evidence volume to roll back.

### R5 — Prove repeatable fresh PostgreSQL initialization and least privilege

- **Objective:** reviewed production initialization, not a development restore. **Pre-purchase; disposable approved PostgreSQL18 target.** Depends R3/R4c and any approved schema changes from R4b.
- **Paths:** `PointOfSale.Infrastructure/Migrations/`, `Data/ApplicationDbContextFactory.cs:16–43`, `Migrations/20260408103000_AddPurchaseInvoiceSequence.cs:29–36`, `20260723214642_Baseline_Recovery3*`, `20260726172030_PlatformCurrencyReferenceSeed*`, `Data/Seeding/`, `PointOfSale.API/SuperAdmin/Services/SuperAdminRootSeeder.cs:26–86`, tenant/CMS/COA seed owners; `PointOfSale.Tests/Security/TenantRegistrationFreshDatabaseTests.cs`.
- **Intended changes:** release-specific migration artifact/runner with explicit target and no development fallback; migration lock, owner/migrator/runtime/backup/report identities; deterministic required seeds and secure one-time root provisioning; pre-provision Hangfire/index DDL or bounded documented initialization privileges, then revoke elevation. Define a reference-data allowlist, not an opaque seed dump.
- **Acceptance:** replay the **entire** selected chain from empty twice on separate fresh targets; verify history/model/schema/indexes/constraints/canonical guards/current forensic objects, required currencies/taxonomy/themes/COA and first-admin login; rerun initialization is safe and does not reset credentials or create sample business rows. Missing/invalid root setup fails the initialization gate, not just a log. Current fresh-registration tests use `EnsureCreatedAsync` (`:32–35`) and do **not** replace real PostgreSQL migration replay. **Rollback:** no manual history stamps, arbitrary skips, EnsureCreated production bootstrap, archived baselines or default migration Down; retain failed target/evidence as approved, correct forward and retry on another isolated empty target.

### R6a — Certify MongoDB 8.0 and projection bootstrap

- **Objective:** supported CPU/version, secure clean boot and current projection compatibility. **Pre-purchase Linux VM/CI; provider CPU confirmation before purchase; actual guest check again on C.** Depends R3/R5's synthetic authority fixtures.
- **Paths:** `PointOfSale.Infrastructure/MongoDB/{Services,Configuration}/`, `MongoDbServiceCollectionExtensions.cs`, index initializers, projection consumers, Mongo freshness/tenant/rebuild tests; future selected service definition.
- **Intended changes:** pinned image/FCV, private authenticated one-member replica set, discoverable member DNS, TLS/CA, least-privilege users, repeatable empty-volume initialization and indexes; bound WiredTiger/oplog/resources. No4.4 import or unauthenticated public boot window.
- **Acceptance:** exact image starts on verified CPU/kernel; auth/CA negatives, indexes/serialization/GUID/date/decimal/filter/update/aggregation, duplicate/stale projection delivery, tenant uniqueness, SQL-first rebuild, restart and consistent backup/restore tests. Document non-rebuildable analytics/receipts separately. **Rollback:** never point back to old4.4 volumes or downgrade new data files; preserve source DBs and use validated fresh fixtures/forward corrections.

### R6b — Certify Redis, RabbitMQ and embedded-worker contracts

- **Objective:** real writable/authenticated endpoints and reliable bounded worker lifecycle. **Pre-purchase isolated Linux services.** Depends R3 and selected server versions; R4 regressions exercise integration behavior.
- **Paths:** `PointOfSale.API/Program.cs:1917–2309,3131–3148`, `HealthChecks/SystemDependencyHealthChecks.cs`, `PointOfSale.Infrastructure/Redis/`, consumer definitions/outbox jobs, `PointOfSale.WhatsApp.Baileys/src/{config,services}/`, `src/index.ts:174–189`, Hangfire initialization.
- **Intended changes:** independent production Redis primaries/prefixes and durable Node key contract, shared explicit transport auth/TLS configuration, Rabbit vhost/permissions and full consumer topology, bounded queue/worker/connections/pacing and graceful drain. Select queue type deliberately; do not copy reference topology-operator objects or add duplicate CronJobs.
- **Acceptance:** read/write/lock/revocation/session encryption tests, restart and memory-full behavior, broker auth/CA failure, actual endpoint/subscription inventory, retries/errors/outbox recovery, lease expiry and duplicate restart convergence; API and Baileys stop/drain within a measured grace period without losing acknowledged state. Current Baileys signal handlers do not alone prove HTTP in-flight drain (`index.ts:153–188`). **Rollback:** preserve queue/session/key volumes, no purge or manual processed-row marking. Account for sensitive transport paths not covered by the SQL bus outbox (`Program.cs:2268–2274`); not every broker message is automatically reconstructible.

### R6c — Select and implement the search engine/native-client contract

- **Objective:** resolve §5.3 with the smallest supported adapter migration. **Pre-purchase; isolated search node; managed-service credentials only if that option is selected.** Depends R3; budget/server version locks before R8/R12.
- **Paths:** `PointOfSale.Infrastructure/Elasticsearch/{Configuration,Documents,Services}/`, `ElasticsearchServiceCollectionExtensions.cs`, `Jobs/CommerceHubReindexService.cs`, `Jobs/ElasticsearchDriftReconciliationJob.cs`, search consumers/health checks and related architecture/runtime tests.
- **Intended changes:** first a bounded compatibility spike comparing the preferred maintained OpenSearch/native pair with the Elastic alternative; then commit only the chosen adapter/auth/CA/index setup. Preserve all existing authority overlays, tenant IDs, analyzers and business interfaces. Record package/license/maintenance support.
- **Acceptance:** the full §5.3 matrix passes with valid auth/CA, including cold indexes, non-English queries, SQL visibility deletion, recovery and index-budget behavior; no unauthenticated/TLS-bypass fallback. Health is not only Ping. **Rollback:** isolated disposable indexes or versioned aliases with verified compatibility; indexes are derivative, never restored over SQL truth. Do not introduce two production engines as a temporary workaround.

### R7 — Establish complete production configuration and runtime admission

- **Objective:** valid fail-closed startup, secrets, probes, shutdown and feature contracts. **Pre-purchase; synthetic configuration/credentials, isolated services.** Depends R3/R5/R6; external identities/URLs may be validated structurally before they exist.
- **Paths:** `PointOfSale.API/appsettings{,.Production}.json`, `Configuration/SensitiveConfigurationValidator.cs`, `Program.cs:65–93,119–148,1358–1377,3567–3588,3830–3848`, `deploy/staging/pointofsale.api.env.example`, health/security config tests; Node config; frontend env readers/build configs.
- **Intended changes:** canonical secret-free config schema/examples covering URLs/templates, five distinct JWT keys and valid issuer/audience/TTL, SMTP/R2/provider flags, independent service identities, mounts, persistent Data Protection application/key history, Node encryption key/salt, budgets and pacing. Remove dev URL/array inheritance from the effective production profile; reject experimental/test-host bypass flags in Production, including `REQUIRE_REAL_POSTGRES_TESTS=1`, which removes hosted services at `Program.cs:3591–3597`. Implement required-configuration/feature validation and startup prerequisite visibility without weakening financial readiness.
- **Acceptance:** positive production-shaped config and missing/placeholder/reused-secret/wrong-URL/CA negatives; no production need for user-secrets/ignored local files. SuperAdmin blank issuer/zero TTL (`appsettings.Production.json:23–28`) and blank POS URL (`:6–7`) are resolved. `/health/live` stays dependency-free; required readiness/bus/initializer failures block traffic, not trigger dependency liveness restart storms. Edge actively removes unhealthy API; Compose health status alone does not do so. Grace, non-root permissions, DNS/TLS, secret rotation/restart and key-restoration behavior tested. **Preservation:** no real production secrets in Git/images/Vite/logs; mount-file secrets require an explicit consumer bridge where current code expects env, not fictional `__FILE` support. Readiness may legitimately remain non-Healthy for explicitly deferred features, but required failures must never be masked.

### R8 — Build reproducible production images and CI artifacts

- **Objective:** production artifacts for actual processes, independent of dirty A or B's local image. **Pre-purchase; approved CI/registry credentials for publication.** Depends selected source/toolchains/R6c/R7; package definitions can be drafted earlier but final acceptance uses the supported candidate.
- **Paths:** four solution projects, `theme-authority/shopixy-theme-authority.json`, all four Node package/lock files, frontend Vite configs and Storefront `draft-preview.html`; future Dockerfiles/ignore/CI/static-server definitions (new paths require later authorization).
- **Intended changes:** five multi-stage application images (API, Web, Storefront, CommerceHub, Baileys), plus pinned edge/DNS-module build; minimal non-root runtimes, secret-excluding build context, lockfiles, immutable version/digest, SBOM/vulnerability/dependency/license review, isolated test stages and registry provenance. Static builds need no Vite/Node runtime server. Do not root-`COPY . .` this working tree.
- **Acceptance:** independent clean Linux CI reconstructs candidate and builds/publishes/pulls identical identified release artifacts; required tests and static/deep-link/asset/CSP checks pass; no local JSON/secrets/dumps/evidence/diagnostic controllers or stale test helpers in images. Validate Node24/Baileys RC compatibility. Production builds include shared theme authority and both Storefront entrypoints (`vite.config.ts:87–95,121–126`). No unresolved exploitable release-blocking finding. **Rollback:** retain prior immutable digest/config revision, not mutable latest tags; no source or data-volume deletion.

### R9 — Validate tenant hosts, cookies, security headers and HTTPS-shaped routing

- **Objective:** retain current host-based tenancy through the selected edge. **Pre-purchase on approved test domains/private trusted CA; real public issuance needs DNS credentials later.** Depends R7/R8 and approved hostname scope.
- **Paths:** `PointOfSale.Core/Tenancy/StoreSlug.cs`, `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:103–186`, `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`, `Security/AuthCookieConfiguration.cs:99–124`, `Program.cs:119–148,3120–3127,3649`, `PointOfSale.Storefront/src/bootstrap/{tenantResolver,bootstrap}.ts`, `src/services/api/httpClient.ts`, static/edge configs.
- **Intended changes:** exact platform hosts before tenant wildcard, same-origin `/api` and `/hubs`, preserved Host, narrowly trusted proxy/hop/scheme, unknown-host rejection and selected slug reservations. Host-only auth/CSRF cookies, separate surface keys and intentional CORS. Keep merchant-domain frontend/edge lifecycle off until separately certified; Caddy presence alone does not fix apex bootstrap rejection (`tenantResolver.ts:72–101`).
- **Acceptance:** two independent synthetic tenants equivalent to Ahram/Fady, without copying their business data: config/catalog/cart/auth/cache isolation, forged Host/forwarded/tenant headers, unknown/inactive/deleted/ambiguous hosts and cross-tenant JWT denial; SPA deep links/preview framing/WebSocket/auth/reset links; callback/global API hosts admitted only through existing trust boundaries. Build-time Vite URL/CSP values are correct; no `.shopixy.com` blanket cookie sharing. **Rollback:** versioned edge/config, reserved-name conflict preflight, no merchant renames and no live DNS cutover yet. The production CSP must allow the authorized draft-preview framing while denying other embedding; blindly copying staging X-Frame-Options DENY everywhere can break preview.

### R10 — Implement minimum monitoring, off-host recovery and rehearsal

- **Objective:** recover state and page a responsible operator; no full observability platform expansion. **Mostly pre-purchase on isolated Linux; external backup/retention/notification credentials required for end-to-end proof.** Depends R4c/R5/R6/R7/R8; actual-C proof is repeated in R14.
- **Paths:** `docs/operations/operational-recovery-runbooks.md:214–262`, existing API health/metrics/telemetry and forensic export boundaries, Node `/metrics`, future selected backup/monitoring/runtime definitions. Reference repo backup scripts are examples only, not selected execution paths.
- **Intended changes:** bounded Prometheus retention plus tested alert delivery; off-host PostgreSQL base/WAL or approved equivalent, media/evidence protection, Node/Redis state and key backups, Mongo family coverage, Rabbit definitions/message-recovery procedure, edge/config/registry/host rebuild recipe. Independent encryption keys, restricted read/delete roles and immutable/versioned retention where required. Backups retry after boot readiness and fail loudly on failed transfers/freshness.
- **Acceptance:** SQL outage+crash and restore tests from R4c; restore onto a different isolated empty host/target using only external artifacts and recoverable keys, prove SQL history/roles/invariants, decrypt providers/session essentials, access media and rebuild projections safely. Alerts reach operator for required readiness/financial backlog, disk/inodes, memory, backup freshness/failure, cert expiry, audit-spool/export failures and endpoint silence. No raw live Rabbit directory tar or unchecked curl exit is accepted as restore proof. **Rollback/preservation:** never restore over A/B/production, never blanket-flush API Redis auth/control keys, never delete failed financial work. Freeze RPO/RTO/retention/owner before acceptance; §7 defines proposed targets only.

### R11 — Sign Gate A and freeze the application release

- **Objective:** decide application readiness, not claim launch. **Pre-purchase; all R1–R9 mandatory selected-scope evidence plus R10 software/recovery-harness contracts.** Real production credentials/DNS/provider capacity are not prerequisites for a synthetic application pass.
- **Paths:** selected source/lockfiles/CI/config schema, relevant regression tests and future release evidence manifest; this roadmap's Gate A checklist.
- **Intended changes:** final release-specific rerun after all shared-boundary changes; document every coverage exclusion and accepted nonblocking limitation, no unresolved applicable blocking defect. Freeze schema artifact, candidate tree, images/toolchain and tested config contract together. Reopen upstream evidence if later changes alter it.
- **Acceptance:** §8 Gate A passes with current exact-candidate evidence, required frontend/backend/worker/security/financial/concurrency tests, production image/config proof and fresh SQL replay. External feature sandboxes are explicitly distinguished from mocks. **Rollback:** leave Gate A failed when evidence is absent; do not pin green counts from PHASE8. No VPS purchase/provision is implied by this phase alone.

### R12 — Approve host contract and Gate B before purchase/provisioning

- **Objective:** approve the actual resource/network/recovery plan and supported stack. **Pre-purchase planning and provider/DNS/service-account access; target hardware checks repeated after purchase.** Depends approved L1/alternative and Gate A before execution commitment; external plan work may proceed earlier.
- **Paths:** future selected Compose/host/firewall/storage/secrets/backup/edge definitions, production environment contract and this report §§3–5,7–8. No use of reference `bootstrap-host.sh`/`bootstrap.sh` unchanged.
- **Intended changes:** provider-specific quote/units/CPU flags/IOPS/durability/OS/kernel/IP/firewall/console/egress verification, exact support/license/image freeze, private networking/TLS flow map, no public data/admin ports, scoped SSH, secret delivery/rotation, real capacity/maintenance bounds, operators/runbooks and off-host service ownership. Compose secrets are not an automatically encrypted secret manager; root/Docker access remains privileged.
- **Acceptance:** Gate B checklist approved with ≥6GiB RAM reserve and ≥25% planned free disk including maintenance, load test plan, external services/DNS prerequisites available, no unsupported version/client ambiguity. Host firewall and Docker forwarding paths planned/tested on a disposable equivalent, not UFW output alone. **Rollback:** choose another offer/profile if conditions fail; do not lower margins to make the quote fit. Gate B is approval to proceed later, not authorization from this report.

### R13 — Provision privately, initialize fresh C and verify real edge/integrations

- **Objective:** production-shaped host and clean deploy with no public business traffic yet. **Post-purchase; new real host, production secrets, registry, DNS and selected provider access required.** Depends separately authorized provisioning after Gates A/B.
- **Paths:** approved deployment/host definitions, R5 migration/seeding artifact, R7 config revision, R8 image digests, R9 edge/DNS plan; operator-controlled external settings.
- **Intended changes:** provider firewall/console/SSH before software; verify actual CPU/kernel/disk/resources, install only selected services, generate independent identities/keys/state, run reviewed initialization once, remove initialization elevation/bootstrap credentials as appropriate, deploy privately and issue trusted exact/apex/wildcard TLS with DNS-01. Keep operational paths private.
- **Acceptance:** external IPv4/IPv6 exposure tests, correct private service auth/TLS, actual source/image/config/schema identity, essential jobs/consumers/init complete, administrator/onboarding functional, correct Host/HTTPS/cookies and real domain ownership/cert renewal; approved SMTP/object/provider sandbox or canary proof. Stateful E2E uses a separate disposable prelaunch deployment with the same release/security contracts. Complete R14's stateful capacity work there before final production initialization. Run prelaunch and production stacks **sequentially**, not as two full stacks on this budget. At the final cutover checkpoint, initialize new production targets fresh and verify target-specific differences/read-only smoke; retain only authorized reference/admin/real onboarding records, never QA/load-test imports. **Rollback:** revert edge/image/config only when schema-compatible; preserve new state/keys, stop admission on failure, no automatic reset/restore or DNS replacement of the existing apex service.

### R14 — Measure capacity, rehearse restoration/rollback and sign Gate C

- **Objective:** approve public launch of the exact deployed release. **Post-purchase / real production-like host and external services; real side effects only under explicit canary authorization.** Depends R13; reopens Gate A/B for material drift.
- **Paths:** approved end-to-end/tenant/financial/provider suites, runtime health/metrics and deployment/backup/incident runbooks; release evidence manifest.
- **Intended changes:** bounded representative soak/peak on the disposable prelaunch targets with API workers, indexing/rebuild and streamed backup overlap; off-host restore onto a separate isolated target; failed-deploy/previous-image recovery, key/session recovery and incident paging drill. Then complete R13's final fresh-production initialization/checks, take its initial off-host backup, and approve a limited legitimate canary/reversible traffic ramp. No duplicate full staging stack or synthetic business-data import into final production is assumed. Restore/retest the final reference-only backup on a separate target without creating QA orders in production.
- **Acceptance:** exact §8 Gate C checks; agreed latency/error/backlog/connection/CPU/RAM/disk/IOPS targets and free-space margin maintained, recovery meets approved RPO/RTO, valid renewing TLS, all mandatory feature/security/tenant/financial tests and actionable alerts pass. Verify broker-required failure is not hidden behind Degraded HTTP200. **Rollback:** record previous compatible digest/config and admission stop/DNS plan; no unsupported DB downgrade, queue purge, history editing or readiness override. If no prior release exists, rollback is fail-safe traffic closure plus supported forward correction/approved recovery, not a fictional earlier production version.

## 7. Recovery, operational targets and evidence requirements

**PROPOSAL for owner review, not achieved SLOs:** business SQL RPO ≤15minutes using verified external WAL/PITR and RTO ≤4hours including redeploy/reconciliation; media/evidence loss tolerance and WhatsApp session RPO must be approved separately. Daily dumps alone cannot demonstrate a15minute RPO. Immutable financial/audit retention follows approved legal/business policy, not a default cache TTL.

| State | Must preserve / recover | Acceptance evidence |
|---|---|---|
| PostgreSQL | Business authority, auth/session cutoffs, ledger, outboxes, schema/history/roles and necessary globals | External base+WAL catalog/checksums/freshness, isolated point-in-time restore, balanced ledger/current tenant scope and replay/reconciliation. Runtime cannot bypass evidence ACLs. |
| Data Protection / production encryption | Key ring/history/application name, Node encryption secret and salt history, secret recovery access | Restored ciphertext decrypts; no values in evidence; authorized key recovery independent of the failed VPS. Key rotation is planned, not casual regeneration. |
| Application objects/evidence | Non-SQL-rebuildable media, proof/attachments and immutable exports | Independent protected/versioned retention, tested restore/read and administrative-delete protection for selected immutability mechanism. A primary external bucket alone is not a complete backup policy. |
| Redis | WhatsApp auth state and security/control continuity; caches only through reviewed rehydrate paths | Persist/restart/rewrite/backup restoration; explicit fail-closed session revocation policy if security keys are missing. Do not restore stale lock ownership as valid active authority. API and session stores are isolated. |
| MongoDB | Classify each collection: rebuildable projections versus analytics/receipts requiring preservation | Consistent supported backup where needed and SQL-first family-specific rebuild tests; no blanket assumption all Mongo data is disposable. |
| RabbitMQ | Definitions, app permissions, durable pending messages and non-outbox paths | Tested supported cold/quiesced backup or explicit replay/reconciliation recovery, no raw live-volume-copy proof. Uncertain external side effects remain uncertain, not reissued blindly. |
| Host/edge/deployment/metrics | Pinned deployment/registry access, edge cert state, configs, alert rules and selected Grafana state | New-host reconstruction without B or A, private console/SSH recovery, external notification/uptime checks continue when the VPS is down. Metrics history may have a reduced retention/RPO by explicit policy. |

Minimum measured workload definition before Gate B: catalog and index size, active tenant distribution, concurrent browsing/cart/checkout, payment/callback rates, WhatsApp sessions, scheduled job cadence, consumer/outbox throughput and backlog recovery, PostgreSQL pool count, WAL/object growth, largest backup/index maintenance operation and restore bandwidth. Set numerical latency/error/backlog targets before testing; do not reverse-fit them after a result.

Operational thresholds proposed for refinement: disk warning before25% free, critical before20% free, with forecasted days-to-full and inode checks; memory/CPU/IO latency and queue growth alerts; backup freshness tied to RPO; TLS-expiry/renewal failure, audit handoff capacity and financial readiness alerts. Crossing thresholds invokes reviewed admission/backpressure/incident controls, **not deletion of authoritative evidence or failed financial rows**.

## 8. Explicit readiness gates and pre-/post-purchase classification

| Gate | Required evidence / work | Status now |
|---|---|---|
| **A — Application ready before VPS purchase** | R1 source reconstruction and chosen feature scope; .NET10 + compatible dependency graph; required backend/three-SPA/Node tests with critical compile exclusions resolved/equivalently covered; all applicable34 retained findings + three exact distributed scenarios closed; no unresolved tenant/security/financial/identity/audit implementation blocker; fresh PostgreSQL replay/least-privilege/init tests; authenticated supported service contracts incl. search; reproducible secret-free images and validated production configuration/probes/shutdown/routing; recovery software/harness tested on approved isolated services. | **NOT PASSED.** Source/docs inspection is not build/test/compatibility proof. External production accounts/DNS and provider-specific capacity are not certified by Gate A. |
| **B — Safe to purchase/provision/configure the selected host** | Approved actual offer/usable capacity/CPU features/OS/kernel/supported versions; approved L1 or rebudgeted alternative with margins; security/network/SSH/console plan; no public data/admin exposure; secret/rotation/registry strategy; domain control and ACME provider/module prerequisites; volume/growth/maintenance plan; monitoring/on-call and independent encrypted off-host backup/retention/RPO/RTO plan with required service access. Gate A is prerequisite to a deployment purchase commitment in this roadmap. | **NOT PASSED.** Offer, scope, search/client freeze, Redis support choice, credentials, sizing inputs and operator acceptance remain open. Hardware/ingress implementation evidence must be rechecked on C. |
| **C — Approved for public launch** | Exact deployed image/config/schema/source identity; secure actual host and restricted ports; genuinely fresh final production initialization and verified administrator access; real tenant Host/cookie/cache/HTTPS/TLS/renewal and forged-context negatives; deployed E2E/worker/outbox/financial/security/provider acceptance for selected features; functioning fresh off-host copies + isolated tested restore and recoverable keys/media/sessions; delivered alerts/external uptime; measured representative capacity and maintenance reserve; actual rollback/traffic-stop rehearsal and accountable sign-off. | **NOT PASSED.** No production host or deployed acceptance evidence exists. Gate A or B cannot substitute for C. |

**Pre-purchase possible:** R1–R11 application work, synthetic integration and browser tests, image/CI creation, disposable Linux migration/restart/restore rehearsals, and R12 provider/security planning. These need authorized compute/tooling and sometimes trial/production-like external accounts, but not the purchase of the final VPS. The existing A/B machines are not implicitly authorized test targets.

**Needs external access, potentially before purchase:** registry publication; DNS ownership/provider API and a permitted test domain; SMTP domain/sandbox; S3/R2/immutable backup destination; notification destination; approved payment/carrier/OAuth/WhatsApp account/sandbox. Missing access blocks only the relevant proof, never permits a mock to be labeled live-provider certification.

**Needs real host / post-purchase:** actual guest CPU/kernel/IOPS and provider perimeter, independent external IPv4/IPv6 reachability, final secret delivery/volume ownership, final fresh C initialization, real production DNS/TLS/renewal/cutover, production-shaped soak/backup load, final restore/rollback/operator drill and approved canary. That is R13/R14, not a side effect of completing this report.

**NO-GO:** any unresolved blocking application defect; an unsupported/incompatible selected runtime/client/server; required disabled/unhealthy dependencies; unexplained missing tests; unsafe public data/admin access; untrusted Host/proxy/cookies; unknown fresh schema/roles/seeds/root identity; lost/ephemeral keys; unproved off-host restoration; insufficient margins; missing provider/domain ownership; or absent operator/rollback evidence. Do not bypass financial readiness, remove tenant isolation, seed fake external-money evidence, clear queues/outboxes or invent PASS markers to meet a gate.

## 9. Exact next single implementation task

**Task R1: “Seal an isolated Shopixy release-candidate source and scope manifest without changing runtime behavior.”**

1. With explicit user approval, capture recoverable source-state evidence and content hashes for existing modified/untracked/deleted paths; preserve all work, including ignored material, without copying secret values into reports.
2. Review the nine tracked modifications, nonignored untracked helpers/tests/reports, and4,857 deletions by category; record exact include/exclude decisions and approve the initial feature/hostname scope. No bulk staging of generated/sensitive artifacts.
3. Materialize a clean, task-owned release candidate outside this dirty working tree from only the approved source; retain immutable base/delta/tree identity and verify independent reconstruction.
4. Prove original files/status/index are unchanged. Produce the candidate manifest and bounded handoff to R2.

**Out of that task's scope:** .NET/package changes, fixes, builds/tests, migrations, service setup, VPS purchase, B access or deployment. R2 is the next separately authorized task after R1 acceptance. This sequencing protects the user's uncommitted privacy/cache fixes and prevents upgrade/test results from being attributed to the wrong source.

## 10. Checks performed, preservation and uncertainty ledger

Performed only read-only Git/source/project/document inspection, in-memory JSON/lockfile/migration inventory, file hashing, and public vendor documentation/NuGet `.nuspec` HTTP reads. No infrastructure repository clone/fetch or server access was necessary. No credentials were requested, read from user-secrets for execution, printed or copied. The earlier audit was read first; `.ai-context` authority/risk guidance and relevant deployment/forensic/recovery/test documents were reconciled with current source. Existing context documents were not updated because only this output file is permitted.

Not executed: any application/backend/frontend/Node test or build; restore/install; EF model/CLI/runtime migration; Docker/Compose/Kubernetes action; any database/service connectivity probe; DNS lookup/cutover; current B diagnostics; host/provider benchmarking; secret/vulnerability clearance; real SMTP/payment/shipping/WhatsApp/OAuth action; backup or restore. Every acceptance statement in §6–8 is a future requirement, not a claimed pass.

**Final preservation result: MATCH.** Excluding only this exact report path, the original content/missing-path aggregate remains `2c44e8b88ee9e4114338c4a225fa1f98b34fc8dadfd1c2340f09133c5ce284af`, across the same8,458 existing files,4,857 missing tracked paths and407,368,238 bytes. Filtered porcelain status SHA-256 also matches entry (`b175ccdf7ad6a5d3651c37d25f1b2d0b7301b1c417abe4aed7e62f10f0feded0`); staged diff is still empty and HEAD unchanged. Final status is4,857 deletions /nine modifications /23 untracked entries: **the sole additional entry is this report**. The first final-hash attempt timed out; the completed retry is the verification evidence. Ignored-file byte identity is not claimed by a Git-only fingerprint; this task issued no writes to ignored files. Subsequent proofreading edits were confined to this excluded report.

Unresolved decisions with closing evidence:

- **Scope/hosts/load/RPO/RTO/operator:** owner approval in R1/R10/R12, including CommerceHub launch and selected providers; not inferred from repository presence.
- **Offer:** written provider CPU/AVX/virtualization/units/durability/IOPS/network/console/OS contract in R12; actual guest/host evidence in R13/R14.
- **Search:** native engine/client .NET10 compatibility, authentication/CA and every current feature in R6c; exact maintained patches/license frozen before Gate A/B.
- **Redis/other tool versions:** applicable edition lifecycle, licenses, client TLS and selected patches in R6/R8/R12; Redis Software documentation is not an Open Source support guarantee.
- **Schema/identity/forensics/quality:** current-candidate replay, privileges, root/seed verification, durable audit crash proof, identity gates and exact missing test closure in R2–R5. Prior A database and report counts cannot close them.
- **External operations:** real credentials, ownership, delivery, certificate renewal, immutable retention, restore and provider canary evidence in R10/R13/R14, with strict side-effect approval.

### Vendor references retrieved for this plan

1. [.NET lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [.NET9→10 migration](https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100?view=aspnetcore-10.0), [.NET10 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0), [EF10 changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes), [Npgsql EF10](https://www.npgsql.org/efcore/release-notes/10.0.html).
2. [MongoDB lifecycle](https://www.mongodb.com/legal/support-policy/lifecycles), [MongoDB8 production/CPU notes](https://www.mongodb.com/docs/v8.0/administration/production-notes/), [official driver compatibility tables — .NET/C# sections](https://www.mongodb.com/docs/drivers/compatibility/). The old C#-specific compatibility URL returned403; the consolidated official table was successfully retrieved. Marketing/sample FAQ text does not override the dated lifecycle table.
3. [MassTransit EF8.5.7 dependency metadata](https://api.nuget.org/v3-flatcontainer/masstransit.entityframeworkcore/8.5.7/masstransit.entityframeworkcore.nuspec), [MongoDB.Driver3.8.0 metadata](https://api.nuget.org/v3-flatcontainer/mongodb.driver/3.8.0/mongodb.driver.nuspec), [Hangfire.PostgreSql1.20.12 metadata](https://api.nuget.org/v3-flatcontainer/hangfire.postgresql/1.20.12/hangfire.postgresql.nuspec). Metadata was read, not installed/restored.
4. [OpenSearch client compatibility warning](https://docs.opensearch.org/latest/clients/), [native .NET clients](https://docs.opensearch.org/latest/clients/dot-net/), [Elastic lifecycle](https://www.elastic.co/support/eol). An old Elastic7.17 .NET compatibility URL returned404; no success claim relies on it.
5. [Node release schedule](https://github.com/nodejs/Release/blob/main/schedule.json), [PostgreSQL lifecycle](https://www.postgresql.org/support/versioning/), [RabbitMQ release/support table](https://www.rabbitmq.com/release-information). [Redis Software lifecycle](https://redis.io/docs/latest/operate/rs/installing-upgrading/product-lifecycle/) was accessible but is not the applicable proof for an Open Source selection.

**Stop:** baseline and execution roadmap only. Review this report and explicitly authorize R1 before any remediation, service testing, provisioning or deployment.

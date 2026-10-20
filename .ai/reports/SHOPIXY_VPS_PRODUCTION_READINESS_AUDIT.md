# Shopixy VPS Production Readiness Audit

**Audit date:** 2026-10-19. **Overall verdict: NOT READY.**

**Scope:** analysis only; application working tree, infrastructure repository, authorized read-only SSH inspection of `192.168.1.50`, development database metadata, and DNS. No application, infrastructure, database, firewall, DNS, registry, or deployment changes were made. This is the only audit-authored workspace file. Proposed changes and future acceptance tests below are **not executed or authorized by this report**.

## 1. Executive summary

Shopixy has an established multi-tenant application and extensive reliability controls, but does **not** have a reproducible, production-approved Kubernetes deployment. Do not run the existing bootstrap unchanged on a public VPS.

### Principal launch blockers

1. **The default infrastructure does not fit the candidate VPS.** The principal checked-in workloads alone request approximately **9.3 CPU cores / 34.25 GiB RAM / 510 GiB PVC capacity**, before operators, the API, WhatsApp, static servers, and OS/K3s overhead. The candidate is only **8 vCPU / 34 GB RAM / 200 GB NVMe**. Actual server B resource requests are larger still. A deliberately reduced single-host topology is technically plausible, but is a different, unvalidated configuration.
2. **Public-edge security defaults are unsuitable.** Host bootstrap disables active firewalls; infrastructure bootstrap subsequently resets UFW and opens Kubernetes administration and all principal data-service ports to any IPv4/IPv6 source. K3s kubeconfig is installed mode `644`; this is also the actual server B permission. LAN MetalLB addresses cannot be copied to a public VPS.
3. **Application packaging and production workload definitions are missing from the application source.** No authoritative Dockerfiles, image pipeline, production Kubernetes resources, production ingress, or registry/pull-secret contract were found. A separate experimental API on server B is not a production release artifact.
4. **Production configuration, database privileges, migration/initialization procedure, and durable key storage are not certified.** Development PostgreSQL has all 53 current migration IDs, but its configured login is a superuser. The forensic schema/roles/triggers described by older reports are absent from that database and from the active migration chain's creation statements.
5. **Production DNS/TLS and original-storefront-host forwarding remain to be configured.** Existing tenant-subdomain resolution should be reused, not rebuilt. Apex DNS currently resolves elsewhere; no positive A answer was obtained for tested production subdomains or a wildcard sample. There is no Shopixy public certificate or ACME DNS-01 issuer on B.
6. **Operational recovery is not launch-ready.** B has a failed external-backup service, root disk 88% used, image-GC failures, OpenSearch yellow health, and RabbitMQ topology resources with `Ready=False`. In-cluster PostgreSQL backups currently complete, but same-host MinIO does not protect against VPS loss. No restore was executed in this audit.
7. **Current quality closure is conditional.** The latest Phase 8 report explicitly retains 34 failed backend assertions and three unexecuted distributed promotion scenarios. Its local synthetic commerce proof is not production or external-provider certification.

**Capacity conclusion:** reject the **unchanged repository topology** for the candidate. Consider the candidate only after approving a lean single-host profile, external durable object storage/off-host backups, measured application load targets, and resource/disk budgets. **Replicas on one physical VPS do not provide host-level high availability.**

**First safe action:** review and approve the P0 decisions in §13, especially launch scope, lean-versus-full topology, DNS ownership/cutover, and data initialization versus migration. Do not purchase/provision or run bootstrap on the assumption that B's installed stacks prove readiness.

### Evidence terminology

- **FACT:** directly inspected current file/configuration, current diagnostic, or metadata query.
- **HISTORICAL:** an earlier report's claim, not rerun here.
- **INFERENCE:** consequence of inspected implementation; not a completed runtime test.
- **ESTIMATE:** planning allowance, not measured production utilization.
- **UNKNOWN:** not verified; requires the specified future check.

Paths prefixed **APP/** are relative to `C:\Users\fadya\source\repos\PointOfSale`. Paths prefixed **INFRA/** refer to `fadyadel2010/Kubernetes_Vps_Deploy` at commit `4fd4677dc7506b6d65d5146ca723617b59c05e44`, unless explicitly identified as B's modified checkout. Remote source links can be constructed as `https://github.com/fadyadel2010/Kubernetes_Vps_Deploy/blob/4fd4677dc7506b6d65d5146ca723617b59c05e44/<path>#L<line>`.

## 2. Environment map: keep A, B, and C separate

| Environment | Verified identity and role | Actual application relationship | Reuse boundary |
|---|---|---|---|
| **A — current development** | Application source on this Windows audit workstation; user identifies its infrastructure as the Ubuntu UI development machine. Effective Development files/user-secrets select PostgreSQL/Redis/RabbitMQ/search at `192.168.1.5`, Mongo at `192.168.109.128`; see §6. | **Not configured to use B's Kubernetes stacks.** At this audit snapshot TCP listeners on local 5010/5173/5174/5175/3000 were absent; no API process was observed. Earlier reports describe those applications running. This audit did not restart them. | Source, contracts, migration chain, tenant resolution, and operational code are reusable. Development addresses, secrets, local URLs, data, permissions, and certificates are not production configuration. |
| **B — infrastructure/reference server** | SSH `shopixy@192.168.1.50`; `shopixyserver`; Ubuntu 22.04.5; 24 logical CPUs; 94 GiB physical RAM reported; one K3s node. | Infrastructure exists. **Exception discovered:** `shopixy-loadtest/shopixy-api-loadtest` is a separate Development/experimental API using a local image and Secret-backed configuration. It is unready. It does not establish that A's application uses B, or that the current main source is deployed. No production POS/Storefront/CommerceHub/Baileys deployment was found. | Useful reference/validation environment only. Preserve its local changes, workloads, and test data; never treat it as C or silently redirect A to it. |
| **C — future production** | Not provisioned. Candidate specifications are user-supplied, not purchased or independently verified. No public IP, provider networking/storage contract, Ubuntu version, or production kubeconfig exists. | All production application workloads and their production integration remain to be packaged/configured/deployed under a later task. | Reuse reviewed source and corrected/pinned infrastructure definitions, not a blind copy of B's runtime or development databases. |

**Evidence reconciliation:** the user's separation of the normal application from B is confirmed by A's configuration. The absolute statement that B contains only disconnected infrastructure needs the above **separate load-test API** qualification. That workload was discovered, not assumed to be the normal application.

## 3. Repositories, source of truth, and application inventory

### 3.1 Application repository state

- **FACT:** origin `https://github.com/fadyadel2010/shopixy.git`; branch `main`; HEAD `5bd9bfc0e57c23116cd1c4fba456bf0c9f7ccad4`; last commit `2026-10-18T02:47:13+03:00`, subject `review all pos admin storefront screens`.
- Entry `git status --porcelain=v1`: **4,857 deleted tracked files, nine modified tracked files, 21 untracked entries**. The large deletion set is generated/temporary artifact material; it was neither repaired nor discarded. This is not a clean release checkout.
- Modified product files: `PointOfSale.API/Controllers/StorefrontChatController.cs` and `PointOfSale.Storefront/src/services/api/httpClient.ts`.
- Modified tests: `AbandonedCart/AbandonedCartNativeSchedulerTests.cs`, `Analytics/ProviderDeliveryContractTests.cs`, `Architecture/PosAdministrativeAuthorityCertificationTests.cs`, `Architecture/StorefrontAnalyticsAuthorityCertificationTests.cs`, `CouponProjectionConsumerConsistencyTests.cs`, `Infrastructure/Phase5HttpCacheTests.cs`, `Integration/ProductVariantPriceDistributedReliabilityIntegrationTests.cs` under `PointOfSale.Tests/`.
- Existing untracked work includes the Phase 8 report, HTTP privacy test, configured-scope tests, browser scripts and quality-closeout helpers. All preserved. Release selection must deliberately incorporate or exclude this work through the user's review; HEAD alone does not describe the audited working tree.
- Instructions inspected: `APP/AGENTS.md`, `.ai-context/CODEX_RULES.md`, `PROJECT_CONTEXT.md`, `ARCHITECTURE_TRUTH.md`, `KNOWN_RISKS.md`. No nested `AGENTS.md` was found. The explicit task restriction prevented updating those existing context files.

### 3.2 Infrastructure repository state

- No matching checkout was found in the bounded Windows locations searched: sibling repositories and two directory levels under `source`, `Documents`, `Desktop`, and `Downloads`. This is not a claim about every disk/path.
- Anonymous GitHub API access returned 404 and `gh api` was unauthenticated. **Read-only Git access succeeded** through existing Git credential authorization. Repository tree/files were read over authenticated HTTPS with authorization held in memory, never printed.
- Remote HEAD/main and B's checkout HEAD agree: `4fd4677dc7506b6d65d5146ca723617b59c05e44`.
- **Authoritative existing checkout found on B:** `/home/shopixy/Kubernetes_Vps_Deploy`, branch main. Dirty files: `minio/bootstrap.sh`, `minio/minio-deployment.yaml`, `postgresql-backup/logs/backup.log`.
- The inspected B diff adds the missing `shopixy-postgres` bucket and endpoint/bucket checks, and pins MinIO to `quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z`. Remote source still uses `minio/minio:latest` and omits that bucket. These local fixes are **not in remote HEAD**. Do not reset or overwrite them.
- Live CNPG/OpenSearch/RabbitMQ resources also differ materially from checked-in resource requests/limits (§11); Git equality does not imply live-manifest equality.

### 3.3 Authoritative implementation and documentation hierarchy

1. Current working source/configuration plus inspected live resource state, with exact environment labels.
2. `APP/PointOfSale.sln:6–13`, project files and `Program.cs`: actual process boundaries.
3. `.ai-context` architecture: PostgreSQL write authority; derivative Mongo/search/cache; event/outbox convergence; financial fail-closed behavior. Normal eventual consistency is an accepted design, not itself a defect.
4. `APP/deploy/staging/{README.md,Caddyfile,pointofsale.api.env.example}`: reusable **staging host/process** patterns, not production Kubernetes deployment resources.
5. `APP/docs/operations/operational-recovery-runbooks.md`, especially lines 214–262: restart, SQL-first recovery and DR limitations.
6. Historical schema/recovery and certification reports, reconciled against current code rather than treated as approvals.

Important discrepancies:

| Earlier claim/document | Current evidence and correct interpretation |
|---|---|
| `.ai-context/PROJECT_CONTEXT.md:109–110` mentions a minimal MVC shell and archived standalone Admin. | Current solution contains API/Core/Infrastructure/Tests; no current deployable MVC shell or standalone Admin project exists in the inspected root. Do not invent deployments for them. |
| Project context describes StoreConfig reads as projection-oriented. | `StorefrontStoreConfigController.cs:74–149` reads current SQL StoreConfig joined to active tenant authority. Mongo/Redis remain derivative; do not restore their config over SQL. |
| Older domain descriptions imply only registered verified Domains resolve tenants. | `PersistenceDomainResolver.cs:103–119` first resolves platform-template slugs from active SQL tenants. Platform subdomains **do not require separate custom-domain ownership verification**. |
| July recovery reports describe one baseline migration. | Current source contains **53 unique Migration attributes**, and A's database has exactly those 53 IDs. July reports are snapshots, not the current migration inventory. |
| October 14 missing-schema/readiness concern. | A's read-only metadata query confirms canonical column and three guards, 53/53 IDs; do not re-report that old missing-column issue as still present in A. C/B schema state is separate. |
| `.ai-context/KNOWN_RISKS.md` contains old unresolved ledger-cache and identity statements alongside later mitigation. | `20260912120000_T01ShopixyLedgerBalanceCacheRepair.cs:20–54` exists and is applied in A's history. Identity recovery implementation exists. No fresh business-ledger reconciliation was performed; do not assert old amounts remain wrong or all finance is now correct. |
| Forensic-hardening reports reference `20260601190000_EvidenceDatabaseHardening`. | That migration is archived, not in the active chain. Active migrations contain no `forensic.reject_append_only_evidence_mutation`/`TRG_ForensicAppendOnly` creation; A has no forensic schema/function/owner role and zero such triggers. Existing provisioning scripts depend on these missing objects and legacy table inventory. |
| `INFRA/redis/docs/REDIS_PRODUCTION_GUIDE.md:6,633–652`: production-ready/HA complete. | Redis validation explicitly skips or tolerates broken Sentinel; live pods are Ready but failover was not tested. Generic Redis service selects all replication pods, not only master. This is not an application-ready HA endpoint. |
| RabbitMQ certification and OpenSearch new-VPS guide show PASS/CERTIFIED/GREEN. | Current Rabbit topology resources have `Ready=False`; current search health is yellow. Historical certifications do not override live evidence. |
| `APP/README.md` still references n8n assets. | Current native Hangfire dispatch exists; active migration `20260930145500_MAIN_RetireN8nRecoveryCallback` and current consumers retire that dependency. Do not add n8n merely because archived workflow assets remain. |
| Phase 8 integration report. | Its own verdict is CONDITIONAL, with 34 failures and three blocked scenarios; not an unrestricted backend or production pass. The two product modifications listed above are still uncommitted. |

## 4. Existing Kubernetes server: health, capacity, and resources

**FACT snapshot:** 2026-10-19 approximately **11:00–11:02 UTC** over authorized SSH. SSH key/batch authentication initially failed; masked local prompts then enabled password authentication. Passwords remained in memory, were not embedded in commands/files, and were not emitted. Known-host validation remained enabled. One password-fed sudo invocation read UFW status; no firewall change was made.

### 4.1 Host and cluster

| Check | Result |
|---|---|
| Host/OS/kernel | `shopixyserver`; Ubuntu **22.04.5 LTS**, kernel **5.15.0-198-generic** |
| CPU/RAM | **24** logical CPUs; `free -h`: **94 GiB total**, 19 GiB used, 73 GiB available; no swap |
| Uptime/load | Approximately **28 minutes**; load average **0.75 / 0.68 / 1.28**. Recent recovery/reboot context limits interpretation of a single idle sample. |
| Root disk | `/dev/sda2`: **548G total / 454G used / 67G available / 88% used** |
| K3s/container runtime | Client/server **v1.36.2+k3s1**; `containerd://2.3.2-k3s2` |
| Nodes | One Ready control-plane node, internal IP `192.168.1.50`, no external node IP; no host-level redundancy |
| Node conditions | Ready=True; MemoryPressure/DiskPressure/PIDPressure=False at the sampled time. This does not negate disk-GC warnings. |
| Node metrics | **1,559m CPU / 21,653 MiB memory** (6% / 22%) |
| Pod metrics | 45 measured pods, sum **1,066m / 18,874 MiB**; not simultaneous accounting with the node sample and not a peak/load result |
| StorageClass | `local-path`, provisioner `rancher.io/local-path`, default; **Delete**, **WaitForFirstConsumer**, **allowVolumeExpansion=false** |
| PV/PVC | 25 observed claims, all Bound; 17 infrastructure claims total **510 GiB**, eight load-test-related claims total **56 GiB**; total nominal **566 GiB**. All on the one node/local-path. |
| Filesystem statistics limitation | Kubelet volume statistics for local-path PVCs repeatedly report the **whole backing filesystem's** usage/capacity. They are not per-PVC actual bytes; summing them would grossly overstate usage. Per-directory/database sizes on B remain unknown. |
| Host exposure | `ss -lnt` includes wildcard SSH 22, RPC 111, K3s 6443, node-exporter 9100 and kubelet 10250. Service/NodePort forwarding is not fully represented by this socket list. |

**Warnings:** repeated `ImageGCFailed` / `FreeDiskSpaceFailed` for `node/shopixyserver`; startup probe warnings around the recent restart; repeated Rabbit topology `FailedDeclare`; 145 sampled unready warnings on the load-test API. No unrelated processes, images, pods, or completed Jobs were removed.

### 4.2 Helm releases

Plain `helm list -A` initially failed by trying localhost:8080. The read-only retry with **`helm --kubeconfig /etc/rancher/k3s/k3s.yaml list -A` succeeded**:

| Release / namespace | Chart | Reported application version |
|---|---|---|
| cert-manager / cert-manager | cert-manager-v1.18.2 | v1.18.2 |
| cnpg / cnpg-system | cloudnative-pg-0.29.0 | 1.30.0 |
| opensearch-exporter / opensearch | prometheus-elasticsearch-exporter-7.4.0 | v1.11.0 |
| opensearch-operator / opensearch-system | opensearch-operator-3.0.2 | **3.0.0-alpha** |
| prometheus / prometheus | kube-prometheus-stack-87.15.2 | v0.92.1 |
| redis-operator / redis-operator | redis-operator-0.24.0 | 0.24.0 |
| traefik / kube-system | traefik-40.1.3+up40.1.0 | v3.7.1 in Helm metadata; actual pod image **3.7.4** |
| traefik-crd / kube-system | traefik-crd-40.1.3+up40.1.0 | v3.7.1 |

Other stacks are raw/operator-managed resources, not absent merely because they have no Helm release.

### 4.3 Workload state and observed memory

| Stack | Current observed state | Pod-memory sample, including relevant sidecars/exporters where applicable |
|---|---|---:|
| cert-manager | Three deployments 1/1 | 77 MiB |
| CNPG/operator/plugin | Operator and Barman ready; database 3/3 healthy, primary **shopixy-postgres-2**; pooler two ready | Database/poolers 1,333 MiB; operator/plugin 78 MiB |
| MongoDB | StatefulSet 3/3; exporter ready; TCP probes only do not certify replica/auth/application schema | 532 MiB |
| Redis | Three replication pods and three Sentinel pods each 2/2; designated master redis-0 | 74 MiB; operator 19 MiB |
| RabbitMQ | Three broker pods ready; RabbitmqCluster AllReplicasReady/ReconcileSuccess=True; **Vhost/User/Permission/Exchange/Queue Ready=False** | Brokers 416 MiB; operators 49 MiB |
| OpenSearch | Three data/master/ingest pods ready; **yellow / RUNNING**, version 2.8.0; dashboard/exporter ready | **14,665 MiB** in namespace; approximately 4.7 GiB per search pod |
| MinIO | One ready deployment; pinned image differs from remote source; no probes/resources | 179 MiB |
| Prometheus | Operator, metrics, exporter, Prometheus ready | 634 MiB |
| Grafana | One ready deployment in **monitoring**, not grafana namespace | 130 MiB |
| API experiment | `shopixy-loadtest/shopixy-api-loadtest`: **0/1 ready**, image `shopixy-api-loadtest:2026-10-16-m466-recompose-bypass-1` | 440 MiB |

The experimental API is `ASPNETCORE_ENVIRONMENT=Development`, `IfNotPresent`, uses `shopixy-api-loadtest-secrets`, has multiple diagnostics/experiment environment keys, and mounts data-protection, secure-upload and PointOfSale PVCs. Only its explicit `RabbitMQ__Host=rabbitmq.rabbitmq.svc.cluster.local` proves that dependency's cluster target. Other endpoints are behind its Secret and were **not decoded**; do not infer all Healthy dependencies are B's services.

Read-only requests to existing NodePort `192.168.1.50:30510`: `/health/live` **200 Healthy**, `/health/ready` **503 Unhealthy**. Failing entry: `payout_external_truth_readiness`; disabled certificate provider/Caddy are Degraded. PostgreSQL, Redis, Mongo, Elasticsearch and Rabbit checks were Healthy **for that older experiment's effective configuration**. No data repair or gate bypass was attempted.

### 4.4 Actual edge and certificate state

- `kube-system/traefik` LoadBalancer address **192.168.1.190**, ports 80/443 plus **5432/6379/5672/15672/9200/9000/27017**, each with allocated NodePorts.
- Ingress hosts: `minio.local`, `grafana.local`, `prometheus.local`, **HTTP only**. No Shopixy application Ingress or IngressRoute was found.
- IngressRouteTCP objects: Mongo, OpenSearch, PostgreSQL, RabbitMQ, Redis. MinIO TCP resource exists in source but was absent from live list.
- Five Ready cert-manager Certificates cover CNPG/Barman and internal RabbitMQ/webhook identities. Only ClusterIssuer is `rabbitmq-selfsigned`; namespaced issuers are also internal/self-signed. No `shopixy.com`/wildcard certificate or public ACME issuer was found.
- Internal certificate expiries observed in December 2026; automatic renewals/notifications were not tested.
- Actual UFW is active, default deny incoming/allow outgoing/deny routed, but permits **22,80,443,6443,5432,27017,6379,5672,15672,9200,9000 from Anywhere, IPv4 and IPv6**. Internet reachability of this private LAN host and provider/router filtering are unknown.

## 5. Infrastructure inventory and bootstrap audit

Statuses below distinguish implementation readiness for **C** from B's sampled workload health. No stack is certified merely by installation.

| Stack / current source | B observation | C classification and missing acceptance evidence |
|---|---|---|
| Host/K3s/Helm/kubectl — `bootstrap-host.sh` | Installed; Ready node | **Ready with configuration changes.** Supported/pinned versions, secure kubeconfig, provider firewall, SSH recovery and fail-safe sequencing required. |
| local-path storage — host bootstrap 486–507 | Default/Delete/RWO; all claims Bound | **Ready with configuration changes.** Single-host durability only; no online expansion in current SC; quotas/real free-space monitoring, retention and restore required. No external storage or second node is supplied by these scripts. |
| cert-manager — `cert-manager/bootstrap.sh:28–67` | Three deployments and internal certs ready | **Missing required resources** for public TLS: ACME production issuer, DNS-01 provider integration, restricted DNS credentials, apex+wildcard certificate and renewal/expiry checks. Namespace existence is not installation proof. |
| MetalLB — `metallb/ipaddresspool.yaml:8–9` | L2 load balancer at .190 | **Blocked by topology decision.** Pool `192.168.1.190–192.168.1.210` is LAN-specific. VPS must have provider-supported routable/floating IP/L2 or a different single-node exposure method. MetalLB is **not inherently required** on a one-public-IP VPS. |
| Traefik — `traefik/base/helmchartconfig.yaml`, `tcp/` | Ready, broad data TCP exposure | **Ready with configuration changes.** Production HTTP routing/TLS, HTTPS redirect, trusted proxy/header behavior, exact/wildcard priorities, private-only admin and no public database entrypoints. |
| UFW — `firewall/bootstrap.sh:30–63` | Active with broad allows | **Not production-ready as implemented.** Reset/destructive policy replacement, all-source data/admin rules, and nonstandard SSH risk. Evaluate Kubernetes packet/NAT paths as well as UFW status. |
| MinIO — `minio/bootstrap.sh`, deployment/PVC | Ready; **modified checkout** fixes bucket/image | **Ready with configuration changes / dependency gap.** Remote source omits CNPG's `shopixy-postgres` bucket; latest image, no resources/probes; local disk and same-host backups. May be deferred if CNPG backups and application storage use approved external S3-compatible services. |
| PostgreSQL/CNPG/Barman/PgBouncer — `postgresql/` | 3/3 healthy, pooler active, current internal backups complete | **Ready with configuration changes**, not Shopixy schema-ready proof. App DB is `shopixy` in bootstrap, versus A's `PosEcommerceDb_Baseline`. Production roles, migrations, direct/session/transaction-pool compatibility, storage, external backup and recovery acceptance outstanding. |
| Mongo native — `mongo-native/` | 3/3 TCP-ready, 4.4.29 | **Missing reliable clean-cluster bootstrap path / configuration changes.** Auth/keyfile manifest is applied before multi-pod replica/user initialization; it assumes preexisting authenticated state or a bootstrap state not selected by the master script. Current authenticated CRUD/replica health and upgrade support not certified here. |
| Redis — `redis/redis-replication.yaml`, `redis-sentinel.yaml` | Pods Ready; generic service points at master+replicas | **Blocked for production client routing.** Select a verified writable endpoint or real Sentinel-aware client/failover contract; set resources/maxmemory/eviction/persistence and reconcile validation exceptions. No Redis Cluster sharding is implemented. |
| RabbitMQ — operator/cluster/topology | Brokers ready, topology **not ready** | **Unhealthy/blocked by topology reconciliation** for intended app user/vhost. Configure vhost `shopixy`, permissions and MassTransit-created topology. TLS and CA/client configuration must match actual API wiring; static three queues are not the complete consumer topology. |
| OpenSearch — `opensearch/cluster/opensearch-cluster.yaml` | yellow, three nodes; operator image alpha | **Blocked pending compatibility and health diagnosis.** Main app uses NEST 7.17.5/Elasticsearch contracts, not OpenSearch client/auth settings. TLS/auth/CA, product compatibility, indexes/analyzers, resource/disk pressure and snapshot implementation need proof. |
| Prometheus — `prometheus/custom-values.yaml:1–28` | Ready; infrastructure monitors | **Ready with configuration changes.** 15-day retention/20Gi needs size cap; Alertmanager disabled; app/Baileys scraping and notifications not supplied. Target health not inferred from ServiceMonitor existence. |
| Grafana — `grafana/` | Ready in monitoring, internal HTTP | **Ready with configuration changes.** Provisioned infra dashboards/Telegram routing exist; credential rotation, external TLS/private access, notification drill, app/business alerts, backup and namespace corrections required. |
| NetworkPolicies — `network-policies/` | Policies exist; no minio policy in live list | **Incomplete security implementation.** Most permit any namespace on data ports; default-deny is ingress-only. Labels/ports and operator paths must be checked. No production application/Baileys namespace policy is supplied. |
| Backups — `master-backup.sh`, stack backup/restore scripts | CNPG internal backups succeed; external master service failed | **Unhealthy/incomplete coverage.** Master invokes only PostgreSQL+Mongo, not Redis/Rabbit/object storage/keys. Off-host transfer, schedules, freshness/restore proof and alerting remain gates. |
| Headlamp/dashboard — `headlamp/`, `kubernetes-dashboard/` | Not installed in observed namespaces | **Not required for launch.** Manifest cluster-admin bindings and HTTP ingresses add risk; leave undeployed unless separately secured/approved. |

### 5.1 Actual master order and dependency defects

`INFRA/bootstrap.sh:63–113` executes:

`cert-manager → MetalLB → Traefik → firewall → MinIO → PostgreSQL → Prometheus → Mongo → RabbitMQ → Redis → OpenSearch → Grafana → NetworkPolicies`.

**FACT/INFERENCE issues:**

- Traefik applies all `tcp/` resources at lines 64–67 **before their backend namespaces exist on a genuinely new cluster**. Those cross-namespace object creations can fail at PostgreSQL/Mongo/etc. Namespace creation is not performed beforehand by this master.
- PostgreSQL bootstrap applies its **ServiceMonitor in namespace prometheus** at lines 451–453 **before Prometheus installs that namespace and monitoring CRDs**. On a clean host this is a concrete missing dependency; B's existing CRDs hide it.
- Some operator rollouts are awaited, but namespace/Deployment existence checks are sometimes used as install/reuse gates. They do not establish CRD/webhook readiness, compatible chart version, or healthy operands. Rabbit webhook validation checks resource existence; the comment claiming a multi-file `kubectl apply` is atomic is incorrect—partial application remains possible.
- `minio/bootstrap.sh:195–198` creates `postgres-backups`, `opensearch-snapshots`, `mongodb-backups`; `postgresql/.env:19–20` requires **s3://shopixy-postgres**. B's dirty checkout fixes exactly this mismatch. Reuse requires deliberate reconciliation, not resetting B.
- `mongo-native/scripts/bootstrap.sh:218–232,242–255,379–395,565–625` applies the authenticated three-pod StatefulSet first, then attempts remote multi-member replica/user initialization. Additional bootstrap/pre-auth manifests exist but are not selected by the master path. A fresh-volume rehearsal must demonstrate correct auth sequencing; do not disable auth on a public host as an audit workaround.
- NetworkPolicies are applied last, default-deny first and allow policies afterwards (`network-policies/bootstrap.sh:76–95`); interrupted/partial execution can strand earlier services. Conversely, services are less isolated until this stage. Production policy/labels must be planned before exposure.

### 5.2 Safety, repeatability and version control

- **Firewall-disable window:** `bootstrap-host.sh:190–224,696–703` disables UFW/firewalld before package/network installations. There is no EXIT trap restoring prior firewall state. Package download, Git access, MetalLB, or Traefik failure can leave the public host unfiltered indefinitely. Infrastructure firewall runs only after three stacks. A provider firewall established **before** any bootstrap is mandatory; future implementation should eliminate or explicitly contain this window. Nothing was executed here.
- `firewall/bootstrap.sh:12–28` requires the `ufw` executable before the later conditional installation, so a host without UFW can fail before reaching its install block. It then `ufw --force reset`s existing rules and assumes SSH TCP22. Nonstandard/restricted SSH needs explicit preservation and provider-console recovery.
- `bootstrap-host.sh:396–399` uses a changing K3s stable channel and **`--write-kubeconfig-mode 644`**. Actual B file is `644 root root`. No contents were read. Treat local access to cluster-admin material as a security boundary.
- Remote install scripts/latest binary downloads, unpinned Helm charts and mutable images prevent deterministic reproduction. Host bootstrap registers `opensearch` as the chart repository while OpenSearch bootstrap expects the same alias to be the **operator** repository; the existence check at `opensearch/bootstrap.sh:79–85` can preserve the wrong URL on a clean host.
- `bootstrap-host.sh:405,492,530` uses `((tries++))` under `set -e`; the first zero-valued post-increment can return status 1 and trigger premature exit if the wait loop is actually needed. Syntax passing does not detect this runtime failure. Its minimum RAM/disk failures print `err` but do not necessarily abort at lines 166–178.
- Host bootstrap writes sysctl/fstab, disables swap, autoremoves packages, copies kubeconfig, changes executable bits, and fetches repository refs. These are real changes; its blanket idempotent/safe-to-rerun comment is not an approval for an existing host.
- Stack reruns can Helm-upgrade operators, overwrite generated manifests/Secrets, patch/restart the Rabbit topology operator, replace dashboards/configmaps, reapply storage settings and perform backup/SQL writes. **Not universally non-destructive/idempotent.** CNPG explicitly creates/inserts `bootstrap_wal_test`, checkpoints and switches WAL (`postgresql/bootstrap.sh:706–725`).
- OpenSearch bootstrap skips an existing Cluster rather than reconciling changed node settings (`154–167`). Grafana regenerates `.generated-alerting` and deletes/recreates dashboard ConfigMaps; it does not certify immutable rollout behavior. PostgreSQL uses a fixed `first-real-backup` name, so a prior completed object is not a new backup-freshness proof.
- Do not execute `cleanup.sh`, restore scripts, or broad directory applies containing archived/pre-auth/alternative resources. Single-host singleton choices also require updating hard-coded three-pod waits, counts, backup-secondary discovery, quorum and replica settings—not just editing a replica count.

### 5.3 Validation is not production acceptance

- `INFRA/validate.sh:44` changes script permissions; nested MinIO validation runs **`mc alias set`**, which changes its client configuration. Therefore the master validation is not strictly read-only and was not run.
- `validate.sh:162–174` inventories nonexistent `traefik` and `grafana` namespaces instead of their actual `kube-system` and `monitoring` homes. Its textual bad-status scan does not validate every Running pod's readiness and explicitly excludes Redis Sentinel problems at 189–194.
- `redis/scripts/validate.sh:120–180` skips CrashLoopBackOff Sentinel and tolerates invalid master/quorum evidence. Ready pods on today's B do not remove this validation defect or establish failover.
- Infrastructure validation does not validate current Shopixy release/configuration, EF model/history/roles, every MassTransit consumer, HTTP tenant isolation, external payments/shipping, secret isolation, off-host restore, or application-domain TLS.
- All **44 shell scripts passed `bash -n`**, and **209 non-Helm-template YAML files parsed**, but this is grammar-only. The empty `network-policies/base/namespaces.yaml` parses as zero documents. No Kubernetes API-schema validation or Helm rendering was claimed.

## 6. Local dependencies versus Kubernetes services

### 6.1 Actual Development configuration

Configuration precedence is source-verified in `APP/PointOfSale.API/Program.cs:65–79`: base JSON → environment JSON → optional environment local JSON → **Development user-secrets** → environment variables. Effective values below were reconstructed using those sources; sensitive values were not printed. They describe configuration, **not a running process's captured environment**.

| Dependency | A configured target / safe observation | B infrastructure service / differences | C requirement |
|---|---|---|---|
| PostgreSQL | `192.168.1.5:5432`, `PosEcommerceDb_Baseline`; PostgreSQL18.4 confirmed; TCP and read-only SQL succeeded | `shopixy-postgres-rw.postgresql.svc.cluster.local:5432`; transaction pooler `shopixy-pgbouncer.postgresql.svc.cluster.local:5432`; bootstrap DB/owner `shopixy`; 3 instances | Fresh production DB identity and least-privilege roles; reviewed migration chain; pool/session-lock compatibility and verified TLS policy. Do not point production to A. |
| Mongo | `192.168.109.128:27017`, `ecommerce_read_db`, `products`; bounded TCP check timed out | `mongo.mongo.svc.cluster.local:27017`, headless per-pod DNS; 3-member rs0; keyfile/auth enabled; live `mongo-primary` service also exists | Verify database/user/authSource, replica-set seed list and discoverable internal member DNS. A single externally exposed cluster VIP is not automatically a usable replica-set connection for an external application. |
| Redis | `192.168.1.5:6379`; TCP succeeded; instance prefix `pos` | `redis-master.redis.svc.cluster.local:6379` currently selects master only; **`redis.redis.svc.cluster.local` selects all three replication pods**; Sentinel26379 | Writable primary routing/failover contract; auth/TLS policy supported by both API and Node service; independent production prefix; resource, maxmemory and persistence policy. |
| RabbitMQ | `192.168.1.5`, vhost `/`; TCP5672 succeeded; Windows-specific projection endpoint prefix in local JSON | `rabbitmq.rabbitmq.svc.cluster.local`; 5672/5671; management15672/15671; intended vhost **shopixy** and user permissions currently not reconciled | Provision non-guest app user/vhost/permissions; reconcile bus/health TLS settings; validate subscriptions, durable queues, error queues and SQL outbox delivery. |
| Search | `http://192.168.1.5:9200`, Elasticsearch config, products index; TCP succeeded | `https://shopixy-search.opensearch.svc.cluster.local:9200`, generated internal TLS/auth, OpenSearch2.8 | Decide compatible engine/client/security contract; current main has no explicit BasicAuth/CA fields in ElasticsearchSettings/client builder. Do not copy HTTP endpoint or bypass certificate verification. |
| Object storage | R2 account/access/bucket/service settings present in Development user-secrets; actual values redacted | MinIO backup buckets on same-node PVC; no evidence normal application consumes it | Choose external R2/S3 or certify MinIO HTTPS, browser-reachable presigned URLs, CORS, credentials, media/evidence buckets and independent backup. |
| WhatsApp | API expects `http://localhost:3000`; TCP3000 refused | No Baileys workload found | Separate private Node deployment/service3000; API-key agreement and encrypted-session Redis durability. `localhost` inside API pod is not the Node service. |
| Email/AI | Local secret sources enable SMTP/AI; default source disables them | Not infrastructure stacks supplied by this repository | External SMTP/delivery domain and AI provider approvals/secrets/budgets; explicitly disable unused features rather than copying dev credentials. |
| API/frontend listeners | TCP5010/5173/5174/5175 all refused on this workstation at sample | Only separate experimental API on5010/NodePort30510 | Deploy real API and three static bundles. No currently running local UI/API verification was possible without forbidden startup. |

`APP/PointOfSale.API/appsettings.Development.local.json:2–5,30–39,60–101,118–141,170–176` and the existing user-secrets key inventory establish A. `appsettings.json:47–74,321–363,380–403,431–443` and Production JSON retain development URL/default inheritance unless overridden.

**Connectivity conclusion:** A has not been redirected to B. No packet-level proof of A-to-B application traffic was collected, and none is inferred. Existing B services can be inspected safely, but authenticating every service as the future production app and validating Pod-to-service policies would require an approved future test deployment/configuration. No temporary pod, port-forward, open port, or connection-string change was created.

## 7. Application, container and Kubernetes readiness matrix

### 7.1 Actual deployable process boundaries

| Component | Path / technology / build command proposed for later execution | Runtime / ports / dependencies | Current deployment evidence and missing acceptance |
|---|---|---|---|
| Main API | `PointOfSale.API/PointOfSale.API.csproj`; ASP.NET Core **net9.0**. From repository root: `dotnet publish PointOfSale.API/PointOfSale.API.csproj -c Release -o <isolated-publish-directory>` | `dotnet PointOfSale.API.dll`; development5010; container must explicitly bind `ASPNETCORE_URLS=http://+:<chosen-port>` (5010 or8080, consistently). PostgreSQL, Mongo, Redis, Rabbit, search, object storage, Baileys, external adapters | No authoritative Dockerfile/build context/image pipeline/production Deployment/Service/ConfigMap/Secret contract. A configured but not listening; B has **different experimental image only**. Acceptance: current approved source digest, non-root runtime, correct content root/native dependencies, probes, budgets, secrets, key/PVC storage, ready bus/jobs, rollback. |
| Core library | `PointOfSale.Core/PointOfSale.Core.csproj`; net9; built transitively by publish | No port/process. Embeds `../theme-authority/shopixy-theme-authority.json` | **Not an independent deployment.** Container build context must include shared theme-authority. |
| Infrastructure library | `PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj`; net9/EF/Npgsql/MassTransit/Mongo/NEST | No independent server; adapters/consumers/workers hosted by API | **Not a separate microservice by default.** Include migrations and approved schema artifact in release procedure; don't deploy a second speculative backend. |
| POS/Admin SPA | `PointOfSale.Web`; React18/Vite5; `npm ci`, then `npm run build` → dist | Static HTTP server (chosen80/8080); API `/api`, hubs `/hubs`; no Node/Vite runtime required after build | No Dockerfile/nginx/static production configuration. `vite.config.ts:30,59–60` reads sibling theme-authority. Serve SPA fallback/deep links, immutable hashed assets, uncached HTML, correct CSP; same-origin backend routing required. |
| Storefront SPA | `PointOfSale.Storefront`; React18/TS/Vite5; `npm ci`, `npm run build` (`tsc && vite build`) | Static server, multi-host bundle; API origin default same-origin; **two entry HTML files**, index and `draft-preview.html` | No production container/resources. Include theme-authority context and preview asset; wildcard routing must preserve tenant Host. Prove Ahram/Fady isolation and publication/config/auth/cache behavior. |
| CommerceHub SPA | `PointOfSale.CommerceHub`; React18/TS/**Vite8.2.1**, plugin-react6; `npm ci`, `npm run build` (`tsc && vite build`) | Static server; `VITE_API_BASE_URL` defaults empty; credentialed API requests | No production container/resources or hosted proof. Select Node build version satisfying actual lockfile/Vite engine requirements, not generic Node20 minimum. Verify customer identity, SMTP, marketplace payment and EGP authority gates. |
| Affiliate portal | **Inside POS/Admin bundle**: `PointOfSale.Web/src/App.jsx:149–161,222–234`, `/affiliate/*` | Same API process, affiliate JWT/cookies/identity links | **No independent Affiliate project/process found.** Use `app.shopixy.com/affiliate`, or serve the same Web bundle at `affiliate.shopixy.com` retaining `/affiliate` path/redirect. Root-host branding/route behavior requires a decision/test, not a new backend. |
| Platform/Super Admin | **Inside Web bundle**, `App.jsx:166–188,210,238–263`, `/super-admin/*` | Same API `/api/super-admin`; dedicated signing key/root identity | No independent SPA/backend required. Optional admin hostname can serve same bundle, but routing does not replace authorization; restrict admin ingress/access and validate SQL operator authority. |
| WhatsApp/Baileys | `PointOfSale.WhatsApp.Baileys`; Node/Express/TS/Baileys7 RC; `npm ci`, `npm run build` | `npm start` = `node dist/index.js`; **3000**, `PORT`; Redis, WhatsApp external network, API caller | No Dockerfile/resources; no B deployment. `/live`, `/ready`, `/health`, `/metrics` implemented. Authenticated API internal-only. Persist encryption key and session state; retain one instance initially until ownership/failover tested. Validate RC/library/Node compatibility. |
| Optional marketing/apex page | No independently hosted root-domain landing application identified in this workspace | Decision required: redirect apex, serve selected existing bundle/path, or separately supply a landing site | Do not call apex deployment complete without selecting actual content/service. |
| Diagnostic/test tools | `tests/diagnostics/StoreConfigTrace`, `tests/quality-closeout/RuntimeIdentity`, test/browser/load helpers | Not launch services | Leave outside production images/endpoints. Development/test-only controllers are removed by `Program.cs:163–177` only when the environment is correctly Production. |

### 7.2 Work hosted inside the API

`APP/PointOfSale.API/Program.cs:599–605,708–721,802,868–870,928–929,1176–1177,1851–1873,2187–2199,2326–2336,2471,3137–3148,3600–3623,3821–3822` establishes:

- REST APIs for POS/admin, Storefront, CommerceHub, Affiliate and Super Admin; payment/carrier webhooks; OAuth/identity and public feeds.
- MassTransit Rabbit consumers for product/category/CMS/config/order/affiliate/marketplace/WhatsApp and other projection/update flows. SQL EF bus outbox/inbox plus generic, accounting, stock-sync, marketplace, payment-cancellation, provider-delivery and compensation dispatchers.
- Hosted reconciliation/recovery: unresolved Storefront/CommerceHub payments, payouts/refunds, identity, Storefront/CommerceHub order projections, aggregates, shipping authority/webhook-secret backfill, audit retry, cache warmup, search/Mongo index initialization and promotion expiration.
- SignalR **`/hubs/chat`** with optional shared Redis backplane; WebSocket ingress and correct cookie/auth/timeout behavior required.
- Hangfire server/storage in PostgreSQL. `Jobs/HangfireInitializationHostedService.cs:197–388` schedules shipping tracking, proof/evidence/attachment cleanup, accounting cleanup, inventory reservations, WhatsApp heartbeat/retry/session cleanup, abandoned carts/checkouts/native dispatch, processed trust cleanup, marketplace/cart/idempotency cleanup, trust/search/finance/inventory reconciliation, disk monitor, affiliate ranking, stale return escalation, DNS verification and certificate provisioning/renewal.

There is **no independent worker executable** in the inspected solution. Do not duplicate these as Kubernetes CronJobs or spin up a second API pretending to be a worker without a reviewed responsibility/lease design. Hung API startup, rollout overlap, CPU starvation, or bad config can affect both requests and these jobs.

### 7.3 Packaging and rollout contract still required

For **all five natural image types** (API, Web, Storefront, CommerceHub, Baileys):

1. Reviewed production Dockerfile/build contexts and `.dockerignore` must exclude ignored local JSON, user secrets, test artifacts, DB dumps, node_modules and development uploads. A root-context `COPY . .` is unsafe with this workspace's sensitive/generated material.
2. Use lockfiles, pinned supported base images/digests, reproducible multi-stage builds and isolated CI; publish immutable commit/version tags plus digest to an approved registry. No registry/account/push permissions/pipeline was verified.
3. Define imagePullPolicy and optional imagePullSecrets. B's unqualified local `IfNotPresent` image is not available automatically on C, and must not be used as the release template without review.
4. Define Deployment/Service selectors, container/listen/target ports, non-root/security contexts, dropped capabilities, writable tmp/key/upload mounts, requests/limits, startup/readiness/liveness, termination grace, revision retention and rollout strategy. Use **`/health/live` for liveness**, not dependency-heavy `/health`; readiness is `/health/ready`. Baileys equivalents `/live` and `/ready`; static server GET check.
5. Configure deployment secrets/config separately from immutable images. A pod normally needs restart to consume changed env/Secret values; no hot-update mechanism was established. Vite values are **build-time**, even if the client reads `import.meta.env` per request.
6. Sequence schema/security/bootstrap prerequisites → dependencies → private app startup → readiness/bus/jobs → limited traffic. Startup can seed taxonomy/root/CMS/COA/indexes and Hangfire schemas; it is not a read-only deployment validation action.
7. Persist/share Data Protection keys (`Program.cs:1358–1377`) and approved local secure artifacts. On a single-node RWO key/upload volume, choose a rollout compatible with mount/access and process overlap. Do not declare multi-replica API safe merely because SQL leases exist.
8. Roll back to a recorded prior **image digest plus configuration revision** only when schema backward compatibility is proven. Several migration Downs are intentionally unsupported. A DB downgrade, Helm rollback or deleting a PVC is not a safe default application rollback.

## 8. Production configuration and secrets

Values below are **names/contracts only**. No secret values, credential-bearing URLs, Secret data, private keys or kubeconfig contents are included.

### 8.1 Configuration inventory

| Settings / consumers | Current source and production readiness |
|---|---|
| `ConnectionStrings__DefaultConnection` — EF, authority stores, outboxes, Hangfire | Base/Production blank; A local JSON/user-secrets populated. Required at startup. Fresh production database, independent least-privilege runtime/migration/backup identities, pool budgets and TLS required. Factory working-directory assumption at `Infrastructure/Data/ApplicationDbContextFactory.cs:21–37` needs explicit migration-runner context. |
| `MongoDB__ConnectionString`, `__DatabaseName`, `__ProductsCollectionName` | Required by `SensitiveConfigurationValidator.cs:53–55`; Production inherits base collection/database names. Configure authSource/replicaSet/seeds/TLS and production identity; no Secret-backed C resources exist. |
| `Redis__ConnectionString`, `__Enabled`, `__InstanceName`, timeouts/cache TTLs | API cache, locks, JWT revocation, authority cache and SignalR use it. Required for readiness when enabled; disabling is not a launch workaround. Use writable primary/Sentinel-compatible settings and environment isolation. Node service has **different** Redis keys below. |
| `RabbitMQ__Host`, `__VirtualHost`, `__Username`, `__Password` | Required. `Program.cs:2242–2249` does not bind a separate TLS/CA/port options object. Health check independently parses host:port (`SystemDependencyHealthChecks.cs:265–338`) and lacks TLS setup. Resolve both paths consistently; vhost `/` versus `shopixy` is a real environment difference. |
| `Elasticsearch__Url`, `__Enabled`, product/CommerceHub/affiliate index names, shards/replicas | Main client uses NEST7.17.5 and `.EnableApiVersioningHeader()` (`Infrastructure/Elasticsearch/ElasticsearchServiceCollectionExtensions.cs:60–82`). Settings have no explicit username/password/CA properties. OpenSearch secured endpoint compatibility remains a P0 integration test/decision, not assumed failure or success from B's older API. |
| `JwtSettings__Secret`, `SuperAdminJwt__Secret`, `StorefrontJwt__Secret`, `CustomerJwt__Secret`, `AffiliateJwt__Secret`; issuer/audience/expiry fields | Five distinct keys of at least32 characters required by validator25–46. **SuperAdminJwt issuer/audience and expiries are blank/zero in Production JSON23–28**; configure valid values, not merely the secret. Current local user-secrets are not provisioned into Production. |
| `CommerceHub__OrderAccessSigningKey`, `AbandonedCart__TokenSecret` | Required ≥32 characters; preserve/rotate deliberately so existing order/recovery tokens behave as planned. Native abandoned-cart scheduling is current; stale N8n key presence in user-secrets does not make n8n a runtime prerequisite. |
| `SUPERADMIN_EMAIL`, `SUPERADMIN_PASSWORD` | First-root bootstrap required if none exists (`SuperAdminRootSeeder.cs:12–14,58–86`). Seeder is asynchronous and logs failures while API continues; HTTP liveness does not prove an operator can sign in. Provision once securely, test and remove/rotate bootstrap access as appropriate. |
| `DataProtection__KeysPath`, `DataProtection__ApplicationName` | File-system persistence created at startup. Default per-user LocalApplicationData is unsafe as ephemeral container storage. Keys protect stored OAuth/provider secrets and application tokens; backup and restore with protected data, restrict permissions, never regenerate casually. |
| `Storefront__UrlTemplate`, `Storefront__BaseUrl`, `StorefrontEmailVerification__StorefrontUrlTemplate`, `AbandonedCart__StorefrontUrlTemplate` | Base values are localhost/development and are **not overridden by Production JSON**. Set consistent production template `https://{slug}.shopixy.com` if chosen; tenant canonical base URLs/links also need reviewed data configuration. |
| `Platform__Domain`, `Platform__PosUrl`, `PosUserManagement__FrontendBaseUrl`, `EmailVerification__BaseUrl`, `AffiliateIdentity__PublicBaseUrl`, `CommerceHub__CustomerIdentity__PublicBaseUrl` | Base includes `myplatform.com` and localhost; Production POS-user URL is empty and fails its startup URL validator (`Program.cs:391–404`). Configure approved host/path targets for email/reset/affiliate links; do not assume candidate hostname roots match current SPA routes. |
| `Cors__AllowedOrigins__<index>`, `AllowedHosts`, `ForwardedHeaders__KnownProxies__<index>` | Base CORS lists localhost only, credentials allowed, no wildcard-subdomain predicate (`Program.cs:3118–3127`). Production AllowedHosts remains `*`. Configure exact permitted origins or use current same-origin proxy design; verify ASP.NET configuration-array merging does not retain unintended dev entries. Trusted proxy defaults are loopback only, ForwardLimit1 (`119–148`). |
| `StorefrontCookie` settings; shared auth/CSRF cookies | HttpOnly/Secure and host-only cookie design exists; Storefront configurable Domain should normally remain unset for tenant isolation. Do not blanket-share `.shopixy.com`. Separate API-origin cookies and JS-readable CSRF bootstrap need explicit browser proof (§9). |
| `VITE_API_URL`, `VITE_ADMIN_ORIGIN`, `VITE_DEV_API_TARGET`, storefront dev-override/tenant flags | Storefront build-time settings; production tenant fallback from query/env is disabled. `VITE_ADMIN_ORIGIN` controls preview CSP. Staging example omits it. Setting container env after static build does not rewrite JS/meta CSP. |
| `VITE_API_BASE_URL`, `VITE_MARKETPLACE_PUBLIC_URL` | CommerceHub uses first; Web marketplace CMS links use second. Web main client is hardcoded same-origin `/api`, not a generic VITE_API_URL consumer (`Web/src/services/api.js:34–42`). Declared `VITE_API_TOKEN` in CommerceHub typings is **not an active client auth mechanism**; never inject a private token into Vite. |
| `BaileysService__BaseUrl`, `__ApiKey`, timeouts/health options | Validator unconditionally requires URL/key≥32 even if a product surface is deferred. Choose real private service or a separately reviewed feature-scope change; no such change is authorized here. |
| Node `NODE_ENV`, `PORT`, `REDIS_HOST`, `REDIS_PORT`, `REDIS_PASSWORD`, `REDIS_DB`, `BAILEYS_API_KEY`, `AUTH_STATE_ENCRYPTION_SECRET`, `AUTH_SALT_PREFIX`, session/pool/log settings | `Baileys/src/config/index.ts:9–82`; NODE_ENV must be production; key/secret minimum32. SessionManager Redis socket only binds host/port/password/db; no current TLS/Sentinel option contract. Both clients must support the chosen topology. Redis contains encrypted WhatsApp auth sessions: not all its contents are disposable cache. |
| R2 aliases `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET`, `R2_SERVICE_URL`, `R2_PUBLIC_URL` or `R2:*` | Bound `Program.cs:274–282`. Optional-at-startup validation does not make uploads optional for commerce. Production always selects real R2StorageService; development-only file fallback is unavailable (`553–565`). S3 uses ForcePathStyle; presigned URLs force HTTPS (`R2StorageService.cs:149–175,191–207,434–466`). A cluster-only HTTP MinIO URL is not a usable browser upload/download endpoint. |
| Payments: `Storefront:Payments:Paymob:*`, `CommerceHub:Payments:{Paymob,Stripe,ManualPayment}:*`, `Fawry` foundation, tenant payment configuration | Main Storefront Paymob configuration is separate from platform CommerceHub payment settings. Production flags mostly disable/sandbox defaults. Supply approved live credentials, callbacks/webhooks, HMAC/signing secrets, merchant/channel configuration and enable only certified execution lanes. Tenant credentials may be stored encrypted in SQL; preserving Data Protection keys is essential. |
| Shipping/carrier connections, callback secrets, canonical/parcel/origin warehouse evidence, `Shipping__WebhookClaims__Enabled` | Tenant/platform administration and SQL configuration, not simply a single appsettings secret. Current source supports carrier adapters including Bosta, Aramex, DHL, FedEx and Mylerz boundaries. Real provider accounts, callback URL contracts, credential protection, webhook claims schema and external test authority are required per selected carrier. |
| SMTP `Smtp__Enabled/Host/Port/Username/Password/EnableSsl/FromEmail/FromName` | Disabled by base, local source enabled. CommerceHub/customer/affiliate/password recovery and registration cannot be launch-approved without functional mail delivery, verified sending domain and correct public links. No mail was sent. |
| OAuth provider credentials/callback origins; `CommerceAI:*` | Admin-managed tenant auth provider settings and external AI adapter configuration. Provider console registrations/callback validation/budgets are external dependencies; do not treat local mocked tests as live OAuth/inference approval. Disable unused AI execution explicitly. |
| `Logging:*`, OTLP options, `PROMETHEUS_API_KEY`, operational thresholds/outbox/job pacing | Console JSON/OpenTelemetry/health/metrics implemented. Configure retention/redaction, trusted scrape path, collectors if used, dashboard/alert routing and tested paging. A metrics endpoint guarded by private IP must not become publicly readable merely because proxy trust is wrong. |
| `CertificateProvider:*`, `CaddyEdge:*`, DNS/scheduler settings | Base disables provider/Caddy but enables scheduler registrations. Caddy integration writes routes and runs configured validate/reload commands; Traefik does not consume them. Choose certificate/edge ownership; don't enable stale Windows commands/paths in an API pod. |
| Backup object-store credentials, rclone remotes, timer path/user | Infrastructure-specific secrets; production external destination, encryption, least privilege and restore operator access are not provided by copying repo artifacts. `shopixy-backup.service:7–9` hardcodes user/path. |

### 8.2 Committed material and secret handling

**FACT:** active application JSON uses blank secret placeholders; ignored local JSON and the existing user-secrets file contain populated credentials. They were not copied. No whole-repository secret-scan clearance is claimed: historical/generated artifacts and DB dumps exist and were deliberately not printed.

**FACT:** INFRA tracks populated Secret payload fields in:

- `grafana/grafana-secret.yaml` — admin and Telegram fields;
- `minio/minio-secret.yaml` — root credentials;
- `mongo-native/monitoring/mongodb-exporter-secret.yaml` — credential-bearing URI;
- `postgresql/minio-backup-secret.yaml` — object-store keys;
- `postgresql/secret.yaml` — database credentials;
- `rabbitmq/generated/secret-shopixy-app.yaml` — application credentials.

Additionally the tree includes `postgresql/.env`, `mongo-native/.env`, `redis/.env`, `rabbitmq/bootstrap.env`, `mongo-native/secrets/mongo-keyfile`, SQL/Mongo backup archives, and generated/alternate resources. Secret-template placeholders are not equivalent to the populated concrete manifests. **Do not reuse any of these credential values in C.** Rotation/revocation and removal/history/access remediation need a separately authorized task; do not silently rewrite Git history during audit.

No Kubernetes Secret values or kubeconfig contents were retrieved for publication. Credential names/references are retained where needed. Kubernetes Secrets still require RBAC, at-rest encryption decision, delivery/rotation procedure and access logging; base64 is not encryption.

## 9. Production domain, HTTPS, and tenant routing

### 9.1 Existing behavior to preserve

**User-established local behavior:** `http://ahramstore.localhost:5174/` and `http://fadystore.localhost:5174/` resolve their storefronts. This was not rerun because the local applications were not listening. Current source explains the behavior:

1. `Storefront/src/bootstrap/tenantResolver.ts:72–101,156–184` extracts a hostname slug; production disables the Development query/env fallback.
2. `bootstrap/bootstrap.ts:123–166` initializes candidate tenant/session, loads StoreConfig and replaces the candidate with backend-issued canonical tenant ID. Browser candidate/header is not SQL authority.
3. `Storefront/vite.config.ts:99–102` uses `/api` proxy with **changeOrigin:false**, preserving tenant Host; production equivalent must do the same.
4. `API/Program.cs:119–148,3649,3710–3751` processes trusted forwarded headers before host/domain/tenant resolution. HostNormalizer validates IDN/ports/trailing dots (`Middleware/HostNormalizer.cs:27–138`).
5. `PersistenceDomainResolver.cs:103–119` matches configured platform URL template and active/nondeleted SQL tenant slug first; `124–186` handles verified/SSL-active/primary registered custom Domains and an unambiguous canonical-base-URL fallback. It does **not** force a platform tenant slug to obtain independent custom-domain ownership verification.
6. `TenantResolutionMiddleware.cs:83–187` strips raw tenant input, validates claims/host mismatch and rejects spoofing; active tenant authority joins prevent inactive/deleted tenants being admitted. Custom/base URL matches require unambiguous authority; SQL exceptions do not grant a tenant.
7. Public StoreConfig is SQL-authoritative (`StorefrontStoreConfigController.cs:74–149`). Other read models/caches are tenant-scoped. Redis domain keys include normalized host and a shared invalidation generation (`RedisDomainResolutionCache.cs:24–34,70–94`); browser cache identity includes tenant/language/affiliate/preview as appropriate (`runtimeCachePolicy.ts:26–48`). Do not share production cache prefixes with A or the load-test namespace.

**Reusability verdict:** the platform-subdomain design exists. Production work is template/DNS/edge/proxy/security/configuration and validation—not rebuilding multi-tenancy.

### 9.2 Candidate host mapping, not a finalized architecture

| Host candidate | Existing compatible surface | Required decision/configuration |
|---|---|---|
| `shopixy.com` | No specific apex landing service identified | Choose landing site or redirect; wildcard record/cert alone does not cover the apex. Preserve current domain service until an approved cutover. |
| `app.shopixy.com` | Web bundle: POS/Admin, `/affiliate`, `/super-admin` | Same-origin `/api` and `/hubs` routes to API; SPA deep-link fallback. |
| `api.shopixy.com` | Shared API/global/webhook clients | Useful dedicated API endpoint, but not a substitute for original storefront-host context. Restrict operational paths. |
| `marketplace.shopixy.com` | CommerceHub bundle | Same-origin `/api` proxy avoids current credential/CSRF cross-origin complications. Reserve hostname before tenant registration. |
| `affiliate.shopixy.com` | Same Web image, existing `/affiliate/*` routes | Choose whether root redirects to `/affiliate`; configure identity/public URLs accordingly. No separate backend is required. Reserve name. |
| `*.shopixy.com` | One Storefront image across all tenant hosts | Route single-label tenant hosts, with exact platform hosts taking precedence; preserve original host on API calls. |
| Optional admin host | Same Web image `/super-admin/*` | Separate access restriction and auth proof; don't expose cluster dashboards to implement product administration. |

**Slug reservation gap:** `Core/Tenancy/StoreSlug.cs:11–23` blocks app/api/admin/etc., but **does not reserve `marketplace` or `affiliate`**. Registration normalizes and handles `IX_Tenants_Slug` conflicts (`TenantRegistrationService.cs:110,154,569–574`); A's read-only metadata confirms the unique slug index. Before selecting additional platform hosts, audit existing slugs and reserve all chosen names through a reviewed change—do not rename merchants automatically.

### 9.3 DNS/TLS facts and remaining work

- **FACT, workstation resolver:** apex A for `shopixy.com` returned **23.227.38.32**. This is not B's private address or a verified future VPS IP. Its current service/ownership/cutover requirements were not investigated by making application requests.
- Queries for `app.shopixy.com`, `api.shopixy.com`, `marketplace.shopixy.com`, and `shopixy-audit-readonly.shopixy.com` produced **no usable positive A result** in this audit. This is not proof about every authoritative DNS view, AAAA record, wildcard type, or provider propagation state.
- Future DNS: A/AAAA for apex and chosen exact hosts plus wildcard for tenants to the actual approved public edge IP. Do not publish AAAA without tested IPv6; inspect CAA, CDN/proxy mode and authoritative NS ownership. Wildcard DNS does not route the apex, and wildcard TLS `*.shopixy.com` covers only one label, not apex or nested subdomains.
- Certificate must include at least apex **and** wildcard where used; choose cert-manager ACME **DNS-01** with least-privilege provider automation. HTTP-01 cannot issue wildcard certificates. No DNS provider/API credential or production ClusterIssuer was verified.
- B's cert-manager installation and self-signed Rabbit certificates are **not** Shopixy Internet HTTPS. Verify certificate SAN/chain/SNI, HTTPS redirect, renewal, expiry paging and secret references at the future edge.
- Traefik exact-host routes must outrank wildcard tenant catch-all; reject unknown/unassigned hosts safely. Match actual Traefik version syntax, entrypoints/services/namespace and production ports. No such production routes currently exist in the audited repos.

### 9.4 Forwarded context, CORS, cookies, custom domains

**Preferred minimal deployment adaptation:** retain the already-working **same-origin frontend `/api` proxy pattern** on each Storefront host, forwarding the original Host to the shared API. API can still also be published at `api.shopixy.com`. This preserves the current design without a new tenancy protocol.

If the storefront instead calls `https://api.shopixy.com` directly, the browser sends **Host=api.shopixy.com**. The API will not infer a tenant from Origin/Referer or raw X-Tenant-Id; bootstrap omits the tenant candidate header before canonical StoreConfig (`httpClient.ts:195–224`). **There is no automatically trustworthy original-storefront-host context in that request.** A reviewed edge-controlled context arrangement would be needed; never trust a browser-supplied X-Forwarded-Host or tenant ID as a shortcut.

Trusted-proxy defaults only cover loopback; Traefik pod/node addresses will differ. Preserve Host where possible, configure narrowly trusted proxies/hops/scheme, and strip/rewrite untrusted forwarded headers at the public edge. Failure affects secure cookies, redirects, tenant host resolution, rate-limit IPs and metrics protection. Do not solve this with a trust-all proxy allowlist.

Credentialed cross-origin clients also need exact CORS origins and CSRF access: `AuthCookieConfiguration.cs:99–124` issues a host-only JS-readable CSRF cookie; the browser at a different frontend origin cannot simply read the API host's cookie. Do not broaden cookie Domain to all tenants without an explicit threat/compatibility review. Test login/refresh/logout/reset/OAuth redirects, CSRF rejection, and cookies independently for POS, tenant A/B, marketplace, affiliate and Super Admin.

**Custom-domain limitation:** backend custom-domain verification/certificate lifecycle exists, but current normal frontend bootstrap rejects a two-label merchant apex such as `merchant.com` before fetching backend StoreConfig (`tenantResolver.ts:72–101`, `bootstrap.ts:125–133`). Reserved `www` hosts also do not produce a candidate. Some three-label custom hosts can proceed to backend canonicalization, but no general apex custom-domain browser readiness is established. **Can defer only if merchant-owned domains are explicitly excluded from initial launch.**

Further, app-managed certificate activation is implemented through **Caddy**, writing managed files and executing configured validate/reload commands (`Infrastructure/Certificates/CaddyEdgeIntegration.cs`, `CertificateProviderFoundation.cs`); infrastructure edge is **Traefik**. A Traefik wildcard certificate for platform subdomains does not implement merchant-domain activation/revocation or synchronize SQL certificate state. Decide ownership and keep unsupported custom-domain UI/actions unavailable until certified. Do not run two competing ACME/edge controllers blindly.

## 10. Databases, persistence, migrations, backups and recovery

### 10.1 Current schema evidence and startup prerequisites

**Executed on A only:** psycopg connection used existing configured credentials in memory, connection option `default_transaction_read_only=on`, explicit **`BEGIN READ ONLY`**, statement/lock timeouts, then rollback. No rows, schemas, history, migrations or sequences were changed.

| A read-only metadata result | Meaning |
|---|---|
| `192.168.1.5:5432 / PosEcommerceDb_Baseline`, PostgreSQL18.4 | Confirms development database, not B/C |
| **53 applied IDs / 53 unique source IDs**, no unknown history or missing source ID; latest `20261013210000_CanonicalAddressEvidenceFoundation` | Matches current source inventory; **not** a new EF pending-model comparison, full schema equivalence audit, or greenfield migration replay |
| `AddressAdmissionEvidence.CanonicalAddressEvidenceJson`: text, nullable; three enabled canonical guards | Earlier specific missing-column blocker is closed for A |
| Unique `IX_Tenants_Slug` present | Database slug uniqueness exists; additional platform-reserved names still need review |
| Configured role flags: superuser/createdb/createrole/bypassrls **all true** | Development runtime identity must not be used for production |
| forensic schema/function/owner role absent; matching forensic append-only triggers **0** | Older role/trigger certifications do not describe this current development database |
| Database size **126,957,247 bytes** | Small development authority database; not a production-growth estimate or total B storage usage |

**B:** CNPG health, storage, tuning and backup resources were inspected, but its database/schema/history/privileges were **not certified**. The Secret-backed load-test database target was not decoded; neither Healthy dependency probes nor B's bootstrap database name establishes current main schema compatibility. **C has no database yet.**

Before production startup:

1. Choose **empty production initialization** versus an explicitly reviewed data migration. Do not restore A's development/test orders, credentials, providers, tenants or synthetic financial state as production by default.
2. Review the current discoverable migration chain, including the early guarded purchase sequence migration, canonical Recovery3 baseline, later data repairs/seeds/constraints and intentionally unsupported Down paths. The guarded pre-baseline sequence checks for absent PurchaseInvoices (`20260408103000_AddPurchaseInvoiceSequence.cs:29–36`); do not arbitrarily skip it or manually stamp history.
3. Generate/review a release-specific migration artifact later in isolated tooling; prove full replay against a disposable **approved** fresh PostgreSQL18 target before C. This audit did not generate/build/apply a migration or baseline.
4. Compare EF model/history and actual schema on the chosen target; verify shipping webhook claims/startup validator, canonical address/parcel/origin evidence, uniqueness, tenant indexes and financial invariant objects. `Program.cs:3627–3639` deliberately rejects enabled shipping claims against an unprepared database.
5. Separate migration authority from runtime/backup/reporting identities. Review app startup DDL assumptions (Hangfire initialization, Mongo/search indexes) against least privilege rather than granting PostgreSQL superuser.
6. Reconcile forensic SQL with current baseline/model/table inventory. Existing `ProvisionForensicEvidenceRoles.sql:40–43,54–78` expects absent forensic objects and includes legacy `LedgerEntries`; the current baseline has no such forensic creation. **Do not simply execute archived migration/provisioning scripts against C.** `VerifyForensicEvidenceDatabaseHardening.sql:117–135` issues UPDATE/DELETE probes even with WHERE FALSE; not an audit-safe read-only script.
7. Validate essential reference seeds (currencies, taxonomy, themes/CMS/COA), root operator bootstrap, tenant registration/store config and projection/index initialization. Current application startup can write/seed; it was not invoked here.

### 10.2 Persistence and recovery matrix

| Component | Authority / topology / storage | Recovery coverage and gaps |
|---|---|---|
| PostgreSQL | Business/financial/auth/outbox authority. Three CNPG instances ×20Gi local-path; preferred anti-affinity permits one host. Two transaction PgBouncers. B live max_connections4000/shared_buffers4GB and larger worker settings differ from source. | B ContinuousArchiving=True and current Oct19 ScheduledBackup completed; historical failed backup remains. Barman target local MinIO. External pg_dump workflow is separate and currently failed. Need external PITR/full-backup policy, roles/globals/config/key coverage, encryption, immutable/versioned retention, restore rehearsal and SQL/reconciliation checks. |
| Mongo | Mostly derivative read models/event receipts/analytics; three20Gi replica-set members on same host; auth/keyfile required | Backup script selects SECONDARY, uses mongodump/copy/archive; no executed consistency/restore proof. A primary-only launch profile requires changing that assumption. Rebuild verified derivative families from SQL; don't assume all analytics/evidence can be reconstructed or discard them indiscriminately. |
| Redis | Cache/locks/revocation/backplane plus **encrypted Baileys session credentials and continuity state**; three5Gi replication volumes, three Sentinel pods | Source resources/maxmemory/persistence behavior not bounded/proven. Redis backup script exists but master timer does not invoke it; remote upload failure can be warning-only. Cache rebuild does not recover WhatsApp session keys/auth continuity. Back up encryption key separately. |
| RabbitMQ | Transport, not business write authority; three50Gi broker volumes | SQL outboxes enable replay/convergence, but broker queues/durable pending messages remain operationally valuable. Backup exports definitions and live tars of `/var/lib/rabbitmq`; live raw file copy is not a demonstrated consistent broker restore. Not invoked by master. Prove durable queue type/replication/permissions and replay behavior. |
| Search | Derivative indexes; three50Gi OpenSearch pools on one disk; heap4g live,2g source | Reindex from SQL is primary recovery path. Snapshot registration script/CronJob exist but main bootstrap doesn't invoke/apply them; B has **no CronJobs**. S3 repository plugin/keystore/credentials are not established by active cluster manifest. Snapshot cron curl lacks HTTP-failure and snapshot SUCCESS assertion; exit0 need not mean a good snapshot. |
| MinIO/object store | MinIO50Gi local-path backups; application media/proof/attachments may instead be external R2 and are not SQL-rebuildable | Same-node MinIO is **not off-VPS backup**. Object versioning/lifecycle/retention/encryption/external replication are unverified. Application media, payment/refund evidence and attachment backup must be included independently of database dumps. |
| Data Protection and Node encryption | Cryptographic state needed to decrypt SQL provider secrets/tokens and Redis auth state | No complete external backup/synchronization procedure found. Ephemeral pod loss must not destroy keys. Preserve key history and application name; secure restore test required. |
| Grafana/Prometheus/K3s | Grafana5Gi SQLite/config; Prometheus20Gi TSDB; single-node control-plane state | Provisioning source is useful, but UI state/dashboard changes and cluster/server recovery require a procedure. No complete tested K3s datastore/token/config/PKI and registry recovery coverage was established. Protect necessary cluster recovery material without publishing it. |

PVC Bound status proves provisioning, **not** application data integrity, host failure survival, backups or restore. Local-path Delete can remove data when a claim is deleted; per-PVC requested capacity is not an enforced filesystem quota in the observed shared backing layout. No reboot/pod recreation/failover/restore was initiated.

### 10.3 Backup implementation findings

- `INFRA/master-backup.sh:69–81` only runs PostgreSQL then Mongo. Its installed timer is daily02:00UTC/Persistent=true, but installer is inside the backup script, not master infrastructure bootstrap.
- **B service failed**, exit1 at **2026-10-19 10:33:11 UTC**. Its journal shows Kubernetes API connection refused during boot catch-up. Unit only declares After=network-online.target; no verified K3s-ready retry barrier. The failure occurs before Mongo invocation. The timer being installed does not prove external backups are current.
- CNPG's Oct19 in-cluster backup later completed. **Do not conflate this with the failed off-host master backup**, or claim all backups failed.
- Master expects remote `PostgressBackup:` and purges PostgreSQL/Mongo paths older15d. Mongo script defaults instead to `MongoBackup:shopixy-backups/mongo` (`backup.sh:47–50,225–235`), subject to `.env` overrides. Actual remote mapping/retention consistency was not verified; master cleanup might not address the uploaded path.
- PostgreSQL local retention7d versus master15d and CNPG30d are separate policies. MinIO capacity and staged archive storage must accommodate actual dump/WAL growth; policy names alone do not reserve space.
- Mongo dump lacks an explicit consistent point-in-time cross-collection/oplog capture in the inspected command (`146–153`). A secondary dump while writes continue is not a cross-store consistent backup claim. Its EXIT trap runs cleanup before reading the original status, risking misleading error diagnostics.
- Redis backup remote-transfer errors may still end in a success summary (`redis/scripts/backup.sh:291–323`). RabbitMQ's live file archive also needs consistency/restore validation. No actual off-host archive listing, last-success object checksum or restore was verified.
- Backup scripts create archives, run BGSAVE, create temporary pod files, perform remote uploads/deletion and retention cleanup. They were **inspected only**, never executed.
- Required business decisions: RPO/RTO, retention/legal/evidence policy, external object-store/account ownership, encryption and immutable/versioned off-host copy, alert owner, isolated restore environment and restoration order. SQL-first rebuild/reconciliation rules in `APP/docs/operations/operational-recovery-runbooks.md:239–262` remain applicable; derivative snapshots must not become financial authority.

## 11. Security, operations and candidate VPS sizing

### 11.1 Mandatory security and operational controls

- Provider firewall **before host bootstrap**: HTTPS80/443 as intended, management SSH only from approved sources/VPN/recovery path; Kubernetes6443/kubelet/node-exporter and all data/admin ports private. Verify IPv4, IPv6, NodePorts and NAT paths from an external vantage point. B's LAN reachability is not an Internet security test.
- Secure kubeconfig and cluster RBAC; no default-service-account broad access or cluster-admin dashboard bindings for app workloads. Decide secret encryption at rest, restricted delivery and rotation; audit object-store and SQL roles.
- Replace any broad network allowance with reviewed namespace/pod selectors and necessary ports, including DNS, operators/webhooks, CNPG replication/control, Mongo replica traffic, Redis replication/Sentinel, Rabbit clustering/TLS and search transport. Source Rabbit policy lacks5671; Traefik ingress policy lacks27017; verify actual behavior before applying strict policies. No app/Baileys policy exists.
- Harden images/containers, pin versions/digests, scan release dependency/container vulnerabilities in CI later, secure writable storage and logs. No vulnerability scan or clean SBOM was generated here. Percona4.4.29/OpenSearch2.8/Rabbit4.0.5/alpha operator and Baileys RC require supported-version/vendor review, not blind inheritance from old certifications.
- **.NET9 support cliff:** API/Core/Infrastructure target net9; `global.json` pins9.0.308. Microsoft's support page, updated2026-10-13, lists9.0.21 as latest patch and **2026-11-10 end of support**. Choose a supported launch horizon and upgrade plan before packaging; do not call .NET9 already unsupported on this audit date. Reference: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core .
- Restrict `/hangfire`, detailed `/health`, `/metrics`, dashboards and operational APIs. Swagger is Development-only; B experiment is Development and must not be exposed as production. Health responses can reveal internal dependency identities/debug data, so expose minimal public liveness only if needed.
- Verify webhook signatures/HMAC/replay/rate limits and trusted tenantless provider callback admission through existing controllers. No real charge/refund/carrier booking/message was triggered. Provider callback health is not certified by an HTTP port being open.
- Durable audit fallback remains a concrete current gap: SQL-first audit writes fall back to **in-memory unbounded Channel** (`AuditService.cs:163–178`, `AuditQueue.cs:15–26`), retried by background service. A process/host crash during SQL outage can lose it; infrastructure backup cannot cure in-flight volatile evidence. External immutable retention/operational evidence launch gates in existing Marketplace certification remain unresolved.
- Infrastructure Grafana rules exist for PostgreSQL/Redis/Rabbit/OpenSearch, with Telegram routing. Alertmanager is disabled. No executed notification test, application/Baileys scrape target, API readiness/payout gate alert, backup-failure/freshness alert, object-store capacity or production certificate-expiry drill was demonstrated. Add and test the minimum release alerts; do not equate a provisioned dashboard with paging.
- Define responsible operator, external uptime check, disk alert thresholds, incident access/recovery and approved stop/rollback decisions. Single VPS outage simultaneously affects cluster control plane, authority DB, messages, cache, search and local backups; replicas do not change this shared failure domain.

### 11.2 Evidence-based resource comparison

**Checked-in principal stacks, not chart defaults or app budgets:**

| Stack | Requested CPU | Requested RAM | CPU/RAM limits | Nominal PVC |
|---|---:|---:|---|---:|
| CNPG3 × source cluster.yaml | 1.5 cores | 3 GiB | 6 cores /12GiB aggregate | 60GiB |
| Mongo3 × statefulset | 3 cores | 6 GiB | 12 cores /24GiB | 60GiB |
| Rabbit3 × cluster | 1.5 cores | 12 GiB | 6 cores /12GiB | 150GiB |
| OpenSearch3 × active source pool | 3 cores | 12 GiB | 6 cores /12GiB | 150GiB |
| Redis3 + Sentinel3 | **unset** | **unset** | **unset** | 15GiB |
| MinIO1 | **unset** | **unset** | **unset** | 50GiB |
| Prometheus1 | 0.2 cores | 1 GiB | 1 core /2GiB | 20GiB |
| Grafana1 | 0.1 cores | 0.25 GiB | 0.5 core /0.5GiB | 5GiB |
| **Subtotal** | **9.3 cores** | **34.25GiB** | Incomplete limits | **510GiB** |

Excluded: operators, PgBouncer, dashboards/exporters, K3s/OS, API, all static servers, Baileys, temporary backup files, logs, image layers, registry cache and free-space margin. **This subtotal already exceeds the candidate.** 34GB may mean only31.7GiB; 200GB approximately186.3GiB before filesystem/OS. Confirm provider units.

**Live B**, from active infrastructure regular-container resources excluding completed installer/debug/security jobs and all load-test containers: declared requests approximately **21.2 CPU /51.05GiB**, configured limits approximately **70.25CPU /125.37GiB**, with **29 containers lacking requests and30 lacking limits**. Init-sidecar and pod-overhead semantics mean this is not an exact Kubernetes scheduling total; unbounded components make it an incomplete maximum. B's API additionally requests1CPU/1GiB and limits14CPU/8GiB—those limits are inappropriate to copy to an8-vCPU machine.

Key drift: live PostgreSQL instances request4CPU/4GiB, limit10CPU/16GiB **each**, versus source0.5CPU/1GiB and2CPU/4GiB; live search requests1CPU/6GiB, limits4CPU/8GiB and4g heap **each**, versus source1CPU/4GiB,2CPU/4GiB and2g heap; live Rabbit limits8GiB instead of source4GiB. These represent tuned reference/test state, not a safe C capacity profile.

Measured B usage is much smaller than requests for several databases but search uses ~14.3GiB across three pods. This is a **recently restarted, low-load point sample**, with an unready experimental API; it cannot establish a production peak or safe tenant/order/connection capacity. No throughput extrapolation is made from old load-test Job names.

### 11.3 Technically plausible initial-launch option — estimate, not approval

A lean configuration could retain one API, three static bundles, one Baileys service, one PostgreSQL primary, one Mongo member, one Redis primary, one Rabbit broker, **one compatible search node**, and reduced monitoring. Use external durable application object storage and independent external backups; consider direct external CNPG object storage to avoid same-host MinIO.

| Budget item | ESTIMATED initial RAM allowance |
|---|---:|
| OS/K3s/controllers/edge | 2–3GiB |
| PostgreSQL | 2–4GiB |
| Mongo | 1.5–3GiB |
| Redis incl. session/lock/cache headroom | 0.5–1.5GiB |
| Rabbit | 1–2GiB |
| One compatible search node | 4–6GiB |
| Monitoring/Grafana | 1–2GiB |
| API + Baileys + static servers | 2–4GiB |
| **Combined estimate** | **14–25.5GiB**, before additional safety margin |

These are proposed planning ranges, **not measured requirements or ready-made Helm values**. Require at least ~6GiB operational/peak margin, bounded memory, aggregate CPU requests below capacity with OS/maintenance reserve, and a representative monitored peak/load/backup test before accepting C. Search, SQL connection concurrency, background reconciliation/rebuilds, backup compression and local shared-disk I/O are likely dominant pressure sources. Heavy AI calls are external but require bounded concurrency/cost.

Illustrative lean storage envelope with **external media/backups**: PG20 + Mongo20 + Redis5 + Rabbit10 + search30 + Prometheus10 + Grafana5 + app/key/temp PVC5 = **105GiB nominal**; allow another20–35GiB for OS/images/logs and10–20GiB backup/maintenance scratch. On200GB this leaves only about26–51GiB, depending on actual disk units/usage—tight at the upper end. Same-host MinIO20–50GiB, retained archives or unexpected WAL/search growth can consume that margin. Real growth/rate/retention sizes remain unknown. Keep at least20–25% filesystem free and implement real monitoring; PVC sizes alone cannot enforce it on local-path.

Trade-offs/decisions:

- Singletons remove wasted same-host replication but accept service downtime during restarts and lose replica recovery options; modify scripts/probes/quorum/backup source assumptions accordingly, under a later task.
- External object storage/offsite backups reduce local disk failure exposure; managed PostgreSQL/search can reduce resource/operational burden but add cost/network/provider dependencies.
- Defer search only with an explicitly approved reduced product scope and source/runtime readiness behavior review; current readiness treats disabled search/Redis as Unhealthy. It is **not** a drop-in resource-saving switch for full Storefront/CommerceHub/Affiliate launch.
- AI, Headlamp, Kubernetes dashboard, dedicated search dashboard and legacy n8n can be deferred where unused. SMTP, application object storage, schema readiness, the required Baileys startup contract and core data/transport dependencies cannot simply be omitted.
- If the desired topology is the existing replicated full stack, choose materially larger RAM/CPU/disk or separate services/hosts. Increasing RAM alone does not resolve the510GiB source storage footprint or host-level availability.

## 12. Safe checks executed and results

### 12.1 Command ledger

Commands were executed from Windows workspace unless marked **B/SSH**. Inline diagnostic programs were piped to Python/Node and retained data in memory; no helper/credential/evidence file was created by the auditor. The harness automatically retains tool output; the self-contained findings are in this report.

| Executed check / exact command family | Result / boundary |
|---|---|
| `git status --short --branch`; `git status --porcelain=v1`; `git branch --show-current`; `git rev-parse HEAD`; `git log -1 --format="%cI %s"`; `git remote -v`; targeted `git diff --stat`, `git diff --name-only`, `git ls-files` | Repository identity/dirty inventory above; no Git mutation. Early broad status/stat output was noisy/truncated, then narrowed. |
| `git ls-remote https://github.com/fadyadel2010/Kubernetes_Vps_Deploy.git HEAD refs/heads/main refs/heads/master` | Remote main/HEAD hash confirmed; no clone/fetch/reset into workspace. |
| Anonymous `GET https://api.github.com/repos/fadyadel2010/Kubernetes_Vps_Deploy/git/trees/main?recursive=1`; `gh api ...` | Initial access failures (404/unauthenticated), **not** final repo unavailability. |
| Existing `git credential fill` captured in memory; authenticated GitHub `GET /repos/.../git/trees/<sha>?recursive=1` and `GET /repos/.../contents/<path>?ref=<sha>` | Successful read-only tree/script/manifest/doc inspection; authorization never emitted or persisted. Binary backup archives not downloaded/decoded. |
| Dedicated read/glob/grep, bounded directory searches, `Get-Command ssh,dotnet,node,docker,kubectl,helm,bash,gh,python` | Source/tool availability; Docker/kubectl/Helm unavailable on Windows PATH; remote tools available. Failed guessed paths were corrected using source searches, not treated as missing components. |
| `ssh -o BatchMode=yes -o ConnectTimeout=12 shopixy@192.168.1.50 "..."` | Initial auth rejected. No remote commands in that attempt passed. Follow-up Paramiko used masked interactive credentials, known-host rejection policy. |
| **B:** `date -u +%FT%TZ`, `hostname`, `uptime`, `cat /etc/os-release`, `uname -r`, `free -h`, `nproc`, `df -h -x tmpfs -x devtmpfs`, `ss -lnt` | Succeeded; host metrics/exposure above. |
| **B:** `kubectl version -o json`; `kubectl get nodes -o wide`; `kubectl get namespaces`; `kubectl get storageclass`; `kubectl get pv`; `kubectl get pvc -A`; `kubectl top nodes`; `kubectl top pods -A` | Succeeded, metrics available. Read-only node/CR/pod JSON projections used for selected resources/security/probes without printing env values. |
| **B:** `helm list -A`, then `helm --kubeconfig /etc/rancher/k3s/k3s.yaml list -A` | First failed localhost8080; explicit-config retry succeeded. Kubeconfig contents never printed. |
| **B:** `kubectl get pods -A -o wide`; `kubectl get deployments,statefulsets,daemonsets -A`; `kubectl get services -A`; `kubectl get ingress -A`; `kubectl get ingressroute -A`; `kubectl get ingressroutetcp -A`; `kubectl get networkpolicies -A` | Succeeded; IngressRoute resource list empty, not a missing CRD/install request. |
| **B:** `kubectl get certificates -A`; `kubectl get clusterissuers`; `kubectl get issuers -A`; JSON projections of cert SAN/issuer/expiry | Internal certs only; no public Shopixy cert/issuer. |
| **B:** `kubectl get clusters.postgresql.cnpg.io -A`; `kubectl get poolers.postgresql.cnpg.io -A`; `kubectl get backups.postgresql.cnpg.io -A`; `kubectl get scheduledbackups.postgresql.cnpg.io -A`; `kubectl get objectstores.barmancloud.cnpg.io -A` | Current cluster/pooler/internal backup status inspected; no backup triggered. |
| **B:** `kubectl get rabbitmqclusters.rabbitmq.com -A`; `kubectl get redisreplications.redis.redis.opstreelabs.in -A`; `kubectl get redissentinels.redis.redis.opstreelabs.in -A`; `kubectl get opensearchclusters.opensearch.org -A` | CR status inspected; does not prove application credentials/failover/index compatibility. |
| **B:** `kubectl get cronjobs,jobs -A`; `kubectl get cronjobs -A -o json` | Existing jobs inventoried; **no CronJobs**. Old load-test failures/completions not rerun or counted as application tests. |
| **B:** `kubectl get events -A --field-selector type=Warning -o json` | Selected reason/object/count/time only emitted; disk-GC/topology/API warnings above. |
| **B:** `kubectl get services -n redis -o json`; `kubectl get endpointslices -n redis -o json`; `kubectl get vhosts,users,permissions,exchanges,queues -n rabbitmq -o json`; `kubectl get servicemonitors,podmonitors -A -o json` | Confirmed Redis mixed-role selection, Rabbit topology false readiness, infrastructure monitor inventory; no temporary workload or mutation. |
| **B:** `kubectl get --raw /api/v1/nodes/shopixyserver/proxy/stats/summary` | Read-only existing kubelet statistics; local-path whole-filesystem reporting limitation recorded. |
| **B:** `kubectl get deployment shopixy-api-loadtest -n shopixy-loadtest -o json` | Printed only image/ports/mounts, safe environment fields and Secret references; **Secret data not decoded**. |
| **B:** `curl --max-time 20 -sS -w '\nHTTP %{http_code}\n' http://192.168.1.50:30510/health/live` and same `/health/ready` | 200 Healthy /503 Unhealthy, selected status fields only. These are old experimental API checks, not current-main tests. |
| **B:** `sudo -n ufw status verbose`, then `sudo -S -p '' ufw status verbose` with password supplied over stdin in memory | First needs auth; authorized retry succeeded. No secret on command line/output, no rule change. |
| **B:** `stat -c '%a %U %G %n' /etc/rancher/k3s/k3s.yaml` | Mode644 metadata only; no key/config contents. |
| **B:** `systemctl show shopixy-backup.service -p ActiveState -p Result -p ExecMainStatus -p ExecMainStartTimestamp -p ExecMainExitTimestamp`; `systemctl list-timers shopixy-backup.timer --no-pager`; `journalctl -u shopixy-backup.service -n 22 --no-pager --output=cat` | Failed external-backup unit and boot-time API-refused cause inspected; not restarted. |
| **B:** `git -C /home/shopixy/Kubernetes_Vps_Deploy status --short --branch`; `git ... rev-parse HEAD`; `git ... diff -- minio/bootstrap.sh minio/minio-deployment.yaml`; bounded `find /home/shopixy -maxdepth 3 ...` | Existing checkout/MinIO differences found and preserved; test artifact directories discovered, not repurposed. |
| Remote source text piped to `C:/Program Files/Git/bin/bash.exe -n` | **44/44 shell grammar checks passed**; scripts not sourced/executed. |
| Remote YAML `yaml.safe_load_all(text)` in memory, excluding vendored Helm Go-template directory | **209/209 parsed**; total253 script/YAML files checked. Syntax only, no schema/operator/chart render validation. |
| Local Python `json.loads`/XML `ElementTree.parse` | JSON/package/lock/launch configs parsed; all four current project XML files parsed. Strict JSON initially rejects Web tsconfig because it is JSONC, **not a product defect**. |
| Node TypeScript `readConfigFile(..., ts.sys.readFile)` on three frontend tsconfigs | **3/3 JSONC parsing passed**, no compilation/output generated. An observer Python escape SyntaxWarning had no check failure and is not a product warning. |
| Python bounded `socket.create_connection(endpoint,timeout=3)` | A PostgreSQL/Redis/Rabbit/search transport connections succeeded; A Mongo timed out; local five application listeners refused. No auth/write/service mutation. |
| Python psycopg: existing A connection, `default_transaction_read_only=on`, explicit `BEGIN READ ONLY`; SELECT database/server/history/catalog/role metadata; rollback | Schema/history/privileges results §10. No EF command, model update, business DML, baseline or repair. |
| `Resolve-DnsName <host> -Type A -DnsOnly` for apex, app, api, marketplace, wildcard sample | Apex23.227.38.32; no usable positive answer for other tested names. NS query returned records but the selected output did not establish DNS provider ownership/credentials. |
| `git -c core.safecrlf=false diff --check -- PointOfSale.API PointOfSale.Core PointOfSale.Infrastructure PointOfSale.Storefront/src PointOfSale.Web/src PointOfSale.CommerceHub/src PointOfSale.WhatsApp.Baileys/src` | Exit0 for inspected preexisting product diffs; not a build/test certification. |

### 12.2 Deliberately not executed

No `dotnet build/publish`, npm build/install/test, EF CLI migration/model build, integration suite, Docker build/push, Helm render/install/upgrade, `kubectl apply/delete/patch/edit/scale/rollout-restart`, bootstrap/cleanup/backup/restore/validation scripts, firewall/DNS changes, account provisioning, real provider actions, application startup/restart, or workload/failover/reboot test. Builds would write artifacts; many tests provision/reset shared databases. User allowed only this report file creation.

No safe check is represented as a full production build, functional test, restore rehearsal or passing deployment. Historical Phase8/October14 results remain **HISTORICAL** except the narrowly reverified A database metadata.

## 13. Prioritized remediation checklist with dependencies and acceptance criteria

All entries are proposed subsequent work. **Block** means blocks the stated initial-launch scope; **Conditional** requires explicit scope exclusion/decision; **Defer** does not remove any security requirement for components actually exposed. Environment A is preserved unless separately authorized.

### P0 — blockers and architecture decisions

| ID / severity / environment / launch gate | Verified evidence and impact | Required action, order and explicit acceptance |
|---|---|---|
| **P0-01 Critical — C — Block** topology/capacity | §11 source9.3CPU/34.25GiB/510GiB; B larger live requests | Before provisioning, approve lean profile versus larger/separate services. Include all operators/apps/OS/keys/scratch and units. **Accept:** signed resource/storage budget fits with ≥20–25% disk free and explicit RAM/CPU margin; coherent singleton/quorum/backup changes reviewed; representative safe load/backup measurements planned and later passed. |
| **P0-02 Critical — A/B/C — Block** release/experimental separation | Dirty APP; B Development experimental image with unreviewed diagnostics; no production packaging | Review the nine existing modifications and untracked work; explicitly select what belongs in the release. Do not promote B's image/Secret. **Accept:** reproducible release source/config digests and traceable test results, no experimental authority bypass enabled; A remains unchanged. Precedes P3. |
| **P0-03 High — C — Block** launch scope/hosting | Web contains Affiliate and Super Admin; three SPAs, shared API; no apex app | Approve exact surfaces, apex content/redirect, optional dedicated Affiliate path, payments/carriers, custom-domain deferral and AI scope. **Accept:** every public host/path has one actual bundle/backend target and defined required integrations. Precedes domain/container decisions. |
| **P0-04 Critical — C — Block** search/client contract | NEST7.17.5 builder lacks explicit auth/CA options; infrastructure secured OpenSearch2.8 | Choose a supported compatible engine/client/auth/TLS contract; preserve existing domain search authority. **Accept:** approved current-main client can authenticate with CA validation and exercise create-index/analyzers/index/search/reindex/health on isolated approved target; no certificate-verification bypass; supported-version review. Precedes infrastructure image selection. |
| **P0-05 Critical — A/B/C — Block** database launch basis | A53/53 IDs but superuser and missing forensic objects; C absent | Choose fresh initialization versus approved data migration; review full chain/model, roles, seeds, forensic current inventory. **Accept:** disposable approved greenfield replay/model/schema checks, no manual history stamps; least-privilege runtime tested; target inventory matches release; rollback/data preservation plan. No historical development ledger repair by assumption. |
| **P0-06 High — B/C — Block for using B as validation reference** health/drift | B disk88%, searchyellow, Rabbit topologyFalse, failed external backup, loadtestready503; remote/local/live drift | Separately authorize diagnosis, not automatic cleanup. Preserve B changes and data. **Accept:** root causes recorded; reference health/backup/reconciliation acceptable for selected tests; source/runtime differences reconciled or explicit; current-main isolated validation cannot contaminate A or existing load tests. C may use another approved validation environment instead. |
| **P0-07 High — C — Block** support lifecycle | net9 and near2026-11-10 EOS; old data/operator images; Vite8 Node requirements | Select supported runtime/patch/base-image/toolchain horizon and upgrade/revalidation owner. **Accept:** current vendor support/security inventory for all release versions; build/runtime compatibility demonstrated before launch. No upgrade performed here. |
| **P0-08 High — A/C — Block for unqualified full launch** unresolved quality gates | Phase8 report§1,§3:34 failures/three unexecuted distributed promotion scenarios | Reconcile stale assertions versus actual unresolved replay/repair contracts using safe isolated environments. **Accept:** every retained failed/blocked scenario has explicit resolution and current evidence; no blanket pass or weakened tenant/financial gates; provider mocks distinguished. Depends approved release/test environment. |

### P1 — host bootstrap

| ID / severity / environment / launch gate | Evidence / impact | Required action and acceptance |
|---|---|---|
| **P1-01 Critical — C — Block before bootstrap** firewall/SSH recovery | Host190–224 disables active firewall; master firewall occurs later; no restoration trap | Establish provider firewall and console/recovery access first; review removal/containment of disable window and nonstandard SSH handling. **Accept:** approved sources retain SSH/recovery; only intended ingress survives IPv4/IPv6; interrupted bootstrap cannot expose administration/data ports. Requires P0 provider/network decision. |
| **P1-02 Critical — B/C — Block C** kubeconfig/host privilege | Host398; actual B kubeconfig644 | Use restricted admin material and least-privilege operator access; choose supported pinned K3s/chart versions and install provenance. **Accept:** kubeconfig/root credentials inaccessible to unprivileged users; no secrets in logs/Git; remote API source restricted. B remediation needs separate approval. |
| **P1-03 High — C — Block before host script** deterministic preflight | Host405/492/530 post-increment under errexit; min checks nonfatal; UFW dependency before install | Review corrected waits/fatal checks/dependencies and safe host-only rerun policy. Confirm genuinely new supported Ubuntu, architecture, free disk/inodes, DNS/NTP, provider network routes, OS update/reboot policy. **Accept:** clean-host preflight/rehearsal records and bounded failure exits; version/checksum inputs pinned; no existing host/PVC touched. |

### P2 — infrastructure bootstrap

| ID / severity / environment / launch gate | Evidence / impact | Required action and acceptance |
|---|---|---|
| **P2-01 Critical — C — Block** dependency order | Traefik TCP namespaces precede creation; PostgreSQL ServiceMonitor precedes Prometheus CRDs; operator repo alias collision | Approve dependency graph: namespaces/CRDs/operators+webhooks before custom resources; monitoring CRDs before monitors; backend namespaces/services before dependent routes. **Accept:** clean approved rehearsal reaches all intended resources Ready without preexisting B artifacts; errors cannot be mistaken for successful complete bootstrap. |
| **P2-02 Critical — C — Block** public exposure | Firewall33–63 and Traefik values46–102 expose data/admin ports | Remove/private-scope unnecessary public TCP entrypoints and NodePorts under approved profile; retain cluster discovery internally. **Accept:** external scans from approved test origin can reach only agreed public80/443 and restricted SSH; no data/Kubernetes admin exposure including IPv6/NAT. Requires P1-01. |
| **P2-03 Critical — B/C — Block** writable Redis/HA semantics | Live redis service/EndpointSlices include all replication pods; source TCP route points there | Choose primary-only service or fully tested Sentinel-aware API+Node integration; set auth, persistence and memory/eviction budgets. **Accept:** all required write/lock/revocation/session operations target master; approved failover test preserves expected behavior; no crashloop exceptions hidden by validation. If singleton accepted, document downtime and recovery rather than claim HA. |
| **P2-04 High — C — Block** Mongo first boot | Native bootstrap applies --auth/keyfile before multi-member initialization | Reconcile bootstrap/pre-auth/reuse paths and supported version plan without publicly exposing unauthenticated Mongo. **Accept:** empty-volume isolated rehearsal forms intended replica topology, creates least-privilege user, rejects anonymous access, authenticates current app and provisions indexes; restart persistence checked later. |
| **P2-05 High — B/C — Block** Rabbit vhost/topology | B application topologyReadyFalse, repeated FailedDeclare; A `/` versus intended shopixy; main TLS binding gap | Diagnose operator reconciliation and provision correct app permissions; align API bus and dependency-check transport. **Accept:** user/vhost/permissions/required topology Ready; intended release consumes/publishes from SQL outboxes with retries/error visibility and no guest/admin app credentials. |
| **P2-06 High — B/C — Block** storage/MinIO divergence | Remote omits CNPG bucket; B local patch fixes it; local-path Delete/noexpand and mutable source image | Reuse local patch only through review; decide external S3 versus MinIO, define capacity/reclaim/growth and image pinning. **Accept:** needed buckets/CA/endpoints/auth and storage mount ownership verified; backup target exists; deletion/expansion procedures documented; no blind recursive apply. |
| **P2-07 High — C — Block** policy correctness | namespaceSelector:{} broad ingress, wrong default-deny filename namespace, missing TLS/internal ports, no apps policy | Review selectors/namespace names and allow graph with DNS/operators/data/monitoring egress/ingress. **Accept:** permitted app↔dependency flows work and unauthorized namespace↔data flows fail under actual CNI; no temporary bypass left. Requires real workload labels and approved future tests. |
| **P2-08 High — B/C — Block for unattended operation** real readiness/monitoring | validate.sh namespace/skip gaps; Alertmanageroff; no app ServiceMonitor; searchyellow | Make validation explicit about readiness and required failures; configure app/Baileys scrape, disk/backups/cert/outbox/financial readiness paging. **Accept:** selected monitors report real targets UP; actionable synthetic alert reaches operator; backup/cert/readiness failures page; no unconditional PASS/skipped health masquerades. |

### P3 — application packaging

| ID / severity / environment / launch gate | Evidence / impact | Required action and acceptance |
|---|---|---|
| **P3-01 Critical — C — Block** production image definitions | No source Dockerfiles/container/K8s assets | Add reviewed API/threeSPA/Baileys containers in a later implementation task, root/shared context and secret-excluding ignores, supported pinned toolchains. **Accept:** clean isolated CI builds approved release, tests safe selections, scans and publishes immutable digests; private registry access works on clean node; no dev JSON/dumps/test data in layers. |
| **P3-02 Critical — C — Block** runtime workload contract | No prod Deployments/Services/config refs/probes/budgets | Define workload ports, namespace/labels, non-root/security, probes, resources, env/Secrets/key mounts, graceful stop, revisions and rollout. **Accept:** empty-node pulls/startup succeed, current release ready without Development flags; liveness doesn't restart on dependency outage; failed readiness blocks traffic; restart/overlap preserves keys and authority. Depends P0-01/05/P2. |
| **P3-03 High — C — Block** static serving/config | Vite dev proxy is not a built-image server; preview HTML/CSP/build-time env | Supply static server SPA/deep-link/CSP/cache rules and original-host `/api`/WebSockets routing; build correct public URLs/admin preview origin. **Accept:** no Vite dev server in production, nolocalhost in release flows; all three deep links and preview/asset loads work; config/image rollback independent and versioned. |
| **P3-04 High — C — Block** rollout/rollback recovery | API embeds workers/seeds, RWO keys, no release rollback assets | Define previous-image/config restore and schema compatibility gate, one-at-a-time safe rollout, rejected-startup recovery. **Accept:** approved validation environment demonstrates failed-start rejection and return to prior compatible digest without migration down/PVC delete or lost keys/messages. |

### P4 — production domain and TLS

| ID / severity / environment / launch gate | Evidence / impact | Required action and acceptance |
|---|---|---|
| **P4-01 Critical — C/external DNS — Block** cutover | Apex resolves23.227.38.32; tested hosts/wildcard no positive A; C IP absent | Confirm domain/DNS account ownership/current service and approved cutover/rollback; publish exact+wildcard/A/AAAA/CAA only later. **Accept:** authoritative and external resolvers reach actual approved edge, apex preserved/redirected as agreed, no stale IPv6 route; rollback retained. |
| **P4-02 Critical — C/external DNS — Block** public certificates | B internal issuers/certs only; no wildcard DNS-01 | Configure chosen ACME controller, scoped DNS credential and apex+wildcard SANs; routing precedence/HTTPS/security headers. **Accept:** valid trusted cert for each host, unknown host safely rejected, renewal/expiry monitoring and tested DNS-01 issuance; no staging certificate or competing controller. |
| **P4-03 Critical — C — Block** tenancy/proxy/cookie integrity | Main rejects raw tenant/Origin authority; loopback-only trusted proxies; CSRF host-only | Preserve original Host using same-origin proxy or separately reviewed trusted-context design; narrowly configure proxies/CORS/cookies. **Accept:** Ahram/Fady config+cart+auth+read-model isolation over HTTPS, spoofed headers/cross-tenant JWT fail, unknown/inactive/deleted/ambiguous host fails closed, callback/global hosts work, browser auth/refresh/CSRF proven. |
| **P4-04 High — C — Block for chosen names** reservations | StoreSlug list lacks marketplace/affiliate | Preflight existing slug conflicts, reserve selected platform hostnames via reviewed change before registrations/cutover. **Accept:** concurrent registrations cannot claim reserved/duplicate case-normalized slug; no merchant silently reassigned. |
| **P4-05 High — C — Conditional** merchant-owned domains | Apex bootstrap candidate failure and Caddy/Traefik lifecycle mismatch | Explicitly defer custom domains or complete approved frontend/edge/certificate lifecycle integration without a tenancy rewrite. **Accept if enabled:** verified merchant apex/www/subdomain resolves correctly, ownership and active tenant enforced, cert issue/renew/revoke/redirect works; deleted mapping stops serving; no platform subdomain verification requirement introduced. |

### P5 — data, secrets and security

| ID / severity / environment / launch gate | Evidence / impact | Required action and acceptance |
|---|---|---|
| **P5-01 Critical — A/B/C — Block C** credential isolation | Tracked infra Secret/env/key artifacts; A populated local files; current runtime role superuser | Rotate/revoke exposed/shared credentials in an authorized task; create independent production Secrets and least-privilege SQL/data users; exclude secrets from image/build context/logs. **Accept:** no production secret derives from Git/dev values, access tests reject forbidden operations, secret/RBAC/at-rest/rotation ownership documented; A/B preserved until authorized changes. |
| **P5-02 Critical — C — Block** durable state/keys/offsite recovery | Master lacks Redis/Rabbit/objects/keys; local MinIO shares host; B masterfailed | Implement complete independently stored encrypted backup policy, freshness/failed-transfer alert, boot-readiness retry and consistent retention paths. **Accept:** recent external copies verified by checksum/catalog; isolated restoration of SQL, crypto keys and media/session essentials meets approvedRPO/RTO; tenant/ledger/outbox/rebuild checks pass; no restore over shared DB. |
| **P5-03 High — C — Block for full financial/marketplace launch** forensic durability | A lacks forensic objects; provisioning script stale; audit fallback unbounded memory | Reconcile current migration/table/role inventory and durability/retention design; protect SQL evidence without applying obsolete scripts. **Accept:** current-table ACL/trigger and runtime identity checks proven; outage-surviving audit path/retention meets agreed launch obligations; external immutable evidence delivery verified. Existing source certification is not sufficient. |
| **P5-04 High — C/external — Block selected features** live integration configuration | Base SMTPdisabled/localhostidentity; payment sandbox flags; R2 requirement; Baileys absent | Configure real public mail/reset/OAuth URLs, external object access/CORS, private Baileys and chosen payment/shipping credentials/callbacks; explicitly disable unlaunched features. **Accept:** approved provider sandbox/live-canary checks and signature/replay/error cases, correct tenants and evidence, no unaudited charge/refund/booking; operational owner can rotate/recover keys. |

### P6 — go-live validation

| ID / severity / environment / launch gate | Evidence / impact | Required action and acceptance |
|---|---|---|
| **P6-01 Critical — approved validation/C — Block** end-to-end release acceptance | No current-main production build/container/browser/provider/restore acceptance executed here | Run the sequenced matrix in §14 only after explicit authorization, with named test tenants/data/provider boundaries. **Accept:** all mandatory checks pass for exact image/config/schema digests, accepted degraded features documented, no unresolved critical financial/tenant/security/backup gate, operational sign-off and rollback ready. |
| **P6-02 High — C — Block** measured capacity/operations | Idle B sample is not C load/capacity proof | Validate representative requests+workers+backup/rebuild concurrency within approved budget, then limited canary. **Accept:** agreed latency/error/backlog/CPU/memory/disk targets and free-space margin under conservative peak; alert delivery works; resource saturation/failure response documented. No tenant/user-count guarantee without this evidence. |

## 14. Proposed deployment sequence and go/no-go criteria

**This sequence is a future plan, not an instruction to execute now.**

1. **Review P0:** approve surfaces/hosts/data basis/version/support/lean topology and external services. Separate production from A and B experiments. Resolve current quality/forensic/schema contract gaps and choose a safe isolated validation target.
2. **Review source/bootstrap changes** in a separate task; pin versions, fix dependency/order/security/bucket/repo-alias/wait/idempotency issues, produce secret-free environment-specific inputs. Retain B's local patches and document live tuning differences.
3. **Obtain external prerequisites:** domain/DNS control, publicIP/provider firewall/console, registry, SMTP/sending DNS, S3/R2/backups, selected payment/shipping/WhatsApp/OAuth credentials and provider callback approvals, operator/RPO/RTO.
4. **Approve and provision new C only afterwards:** supported Ubuntu and provider network, recoverySSH and firewall before bootstrap; prove NTP/DNS/capacity/versions and secure admin access. Record genuine clean-host state.
5. **Infrastructure in dependency order:** namespaces/CRDs/operators/readiness, supported edge exposure/storage, object-store targets/buckets, databases/broker/cache/search, monitoring+network isolation. Do not publish app traffic yet. Validate real auth/routing/persistence, not script banners.
6. **Prepare state:** create least-privilege production identities and fresh schema from reviewed release migration artifact; seed required reference/admin data through supported mechanisms; configure encrypted production secrets and durable keys. If migrating existing data, use separately approved preservation/cutover/reconciliation procedure.
7. **Package/publish current release** in isolated CI and deploy private workloads with pinned digests/config, readiness, budgets and safe rollout/recovery. Prove current-main module/config identity; no Development/testauth/experiment bypasses. Validate registry pulls on a clean node.
8. **Domain/TLS staging:** issue production-approved certs through tested DNS-01; route exact app/global hosts and wildcard tenants with originalHost preserved; prepare DNS cutover/rollback. Test via approved edge routing without changing A's connections.
9. **Run the minimum acceptance matrix below**, then limited canary traffic. Validate external health/alerts and backups during operation. Widen only with an explicit go decision.

### Minimum future safe test sequence

These functional and failure/restart/restore tests **will write state** or change test infrastructure; they were not run in this analysis-only audit. They require a separately approved environment and exact cleanup/preservation boundaries.

| Order | Test / expected acceptance |
|---:|---|
| 1 | Verify release/config/schema identity, all required dependencies authenticated, API startup/liveness/readiness; optional-disabled checks must have explicit accepted semantics. Required unhealthy checks prevent go-live. Ensure Hangfire recurring jobs, bus consumers and outboxes are active. |
| 2 | DNS/TLS exact/apex/wildcard/SNI/HTTPS redirect; invalid host and forged forwarded context fail safely; public data/admin ports unreachable; operational endpoints restricted. |
| 3 | POS/Admin password login/session refresh/logout/CSRF/permission paths; root Super Admin and tenant actor authority; Affiliate routes/identity mail and recovery. Correct host-only cookies and no cross-surface session confusion. |
| 4 | **Ahram and Fady production-shaped tenant hosts:** StoreConfig/canonical tenant/currency, published theme/CMS/navigation/catalog/media; separate browser sessions/carts/customer auth; no SQL/Mongo/Redis/browser-cache leakage. Unknown/inactive/deleted/ambiguous tenant and raw tenant-header negatives. |
| 5 | Catalog/pricing/inventory changes → SQL outbox → Rabbit consumers → Mongo/search/cache convergence; duplicate/stale replay and safe repair/rebuild; verify current blocked promotion scenarios in approved isolation. No manual derivative overwrites. |
| 6 | Storefront order lifecycle: quote/cart/coupon/tax/shipping/warehouse/physical stock reservation/checkout idempotency → fulfillment/cancellation/returns. Confirm SQL ledger, inventory and projection consistency, not just UI totals. |
| 7 | Chosen payment lanes: COD/manual evidence review/settlement; approved provider payment/webhook/replay/timeout/refund cases. Ambiguous external outcome stays blocked/reconciliation-required; no duplicate capture/refund/ledger. Unselected providers remain disabled. |
| 8 | CommerceHub customer registration/verification/login/reset/email links, discovery/detail/cart, checkout/split order, EGP policy, vendor governance/COD debt, refunds/payout readiness and evidence. Unhealthy payout external truth cannot be bypassed for a green dashboard. |
| 9 | Affiliate referral link → tenant storefront attribution → cart/order line commission candidate → approved lifecycle/reporting; tenant/global ownership and payout evidence boundaries. |
| 10 | Selected carriers: canonical address/warehouse/parcel prerequisites, signed callbacks, tracking, cancellation/recovery, COD collection/remittance evidence. Approved sandbox/canary only; no real booking/label/cash assertion from mocks. |
| 11 | Baileys private API/key, Redis session encryption, QR/session/message/retry flow under explicit authorization; SignalR authenticated WebSockets; SMTP delivery; object upload/download signed URLs/CORS; OAuth and AI only if selected. |
| 12 | Metrics/target health, app/backlog/financial readiness/cert/disk/backup alerts delivered to operator; representative monitored load with workers and backup; no runaway unbounded queues/logs/disk. |
| 13 | Approved restart/failed rollout/previous-image recovery and isolated off-host restore rehearsal. Persist keys/media/session essentials, verify SQL authority and controlled derivative/outbox reconvergence, recordRPO/RTO. No destructive tests against A/B shared data. |

### Explicit go/no-go

**GO only when:** P0 decisions and all applicable blocking checklist items are closed with current evidence; exact release images/config/schema and external dependencies approved; all required services/consumers/jobs genuinely ready; tenant/security/financial tests pass; external backup+restore and paging work; capacity budgets/load/free-space margins pass; domain cutover and rollback approved; responsible operator signs off.

**NO-GO if any:** unchanged full stack on candidate; unresolved public administration/data exposure; missing image/prod config/least-privilege/keys; unreviewed schema/baseline/history manipulation; unknown off-host restore; untrusted Host/cookie/tenant isolation; required Rabbit/search integration unproved; unresolved critical financial readiness; failed current required tests; unsupported launch runtime without approved supported path; or unresolved domain ownership/cutover. Do not override readiness, empty queues, alter ledger state, disable fences or discard user changes to manufacture acceptance.

## 15. Unknowns, external dependencies and user decisions

1. **Launch topology and target load:** initial tenants/orders/concurrency/catalog/media size, heavy jobs, RPO/RTO and availability expectations. Candidate CPU type/shared limits, diskunits/IOPS, backup bandwidth, IPv4/IPv6/floating-IP/L2 support and actual Ubuntu version remain unverified.
2. **Provider/DNS ownership:** current apex service, DNS provider/API access, CAA/CDN behavior, authoritative wildcard/AAAA configuration, cutover downtime and rollback. No DNS or certificate provisioning was changed.
3. **Initial data:** empty production versus explicit reviewed migration; merchant/public records to retain; live provider credentials and synthetic QA data exclusions. B database schema/role/financial/data ownership remains unverified; A model-wide schema drift/financial correctness was not fully re-audited.
4. **Image/registry/CI:** registry choice, private pulls, production image provenance, release build/vulnerability results, artifact retention and rollback ownership. B's load-test image/source equivalence is unknown and must not be assumed.
5. **Search and supported versions:** compatible product/auth/CA contract with current main; resource need under representative workload; cause of B yellow health and exact shard/disk contribution; alpha operator acceptability/vendor support.
6. **Database/client connection semantics:** CNPG/PgBouncer session/advisory lock/Hangfire compatibility, actual Mongo auth/replica health, Redis Sentinel failover/persistence/maxmemory, RabbitMQ topology failure cause and complete current-main consumer durability contract. These need bounded approved verification, not bootstrap changes during audit.
7. **Backup/restore:** actual current off-host archive inventory, remote encryption/immutability/retention, last usable Mongo/Redis/Rabbit/media/key backups, B per-directory disk consumers, K3s datastore/PKI recovery and tested targetRPO/RTO. The external timer currently fails; internal CNPG backup success does not close this.
8. **Secrets:** production independently generated credentials, external secret-manager/delivery choice, Kubernetes at-rest encryption and rotation, response to committed secret/key/backup artifacts. Values deliberately withheld throughout.
9. **Domains/features:** final POS/Affiliate/admin hosts and rootpaths, apex service, platform-reserved slugs, whether merchant-owned custom domains ship initially and Caddy-versus-Traefik certificate ownership.
10. **External integrations:** live merchant/provider accounts and enabled scope; SMTP/sending-domain SPF/DKIM/DMARC/delivery, R2/S3/CORS/backups, payment callback URLs/live keys, carrier contracts, WhatsApp authorization/session durability, OAuth consoles, AI budget/credentials and real provider acceptance. No live side effect performed.
11. **Operations/security sign-off:** provider perimeter, safe SSH/recovery, RBAC/SQL identity, dashboards/paging, immutable audit requirements and volatile audit fallback resolution, release support horizon, incident operator and capacity/backup alert thresholds.
12. **Quality evidence:** resolve Phase8's 34 retained failures and three blocked distributed promotion tests; older source certification cannot replace production environment evidence. Current builds/tests were intentionally not rerun under the report-only file-creation constraint.

**Stop condition met:** source and reference-server audit completed; findings and proposed sequence recorded. **No remediation, provisioning or deployment begins until this report is reviewed and a new explicit task is authorized.**

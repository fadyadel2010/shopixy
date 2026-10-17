# Shopixy — Phase 7 Bosta geography, dependency reliability and lint

**2026-10-16 — OPEN / NOT CLOSED.** This is the requested closure ledger, not a declaration that the acceptance gates passed. The bootstrap API build is installed and verified after user restart, and real reference drafts have been persisted/read back. Full canonical-review/mapping/controlled-shipment acceptance and maintained lint **0 errors / 0 warnings** remain outstanding. No live shipment, cancellation, pickup, dispatch, financial operation or message is authorized by this report.

## Later October 16 Easy Orders / documented-default continuation

The current implementation/results superseding older observations below are in `.ai/reports/SHOPIXY_EASY_ORDERS_SHIPPING_REFERENCE_AND_IMPLEMENTATION_20261016.md`:

- Read-only authenticated Easy Orders reference investigation completed without save/activation or credential export. Demonstrated Shopixy defects fixed: unconditional saved Bosta pickup ID (now verified unique-default support with frozen concrete origin), country-scope loss on rate edits, missing warehouse readback, and insufficiently visible separation of authentication/activation/booking blockers.
- New API installed after user stop/restart; all three runtime assemblies match attested artifacts, configuration unchanged, PID **2524** / start **2026-10-16T12:42:42.0082547Z**. Real Admin GET verifies provider default without saving/activating; only the pickup-proof blocker disappears. Disabled configuration and unpublished geography remain. Earlier historical statements that a manually saved businessLocationId is always necessary are **superseded**, not silently rewritten as provider requirements.
- **453/453** carrier/bootstrap/dependency regressions, **61/61** method/DTO/controller persistence-contract tests, **108/108** Mylerz containment regressions, full **1,856/1,856 Admin** and **528/528 Storefront**, both typechecks and production builds passed. Source-attested full frontend run was unchanged. Test-only fixes/failures are separately retained.
- Fresh architecture invocation: **26 passed / same 3 historical-artifact failures**. Full maintained lint: **7 errors / 119 warnings**, zero introduced findings against the original 193/186. Neither gate is closed.
- Source research/review manifest is now prepared without invented canonical UUIDs/approval: see `.ai/reports/SHOPIXY_CANONICAL_EGYPT_SOURCE_REVIEW_20261016.md`. One country ISO correspondence, 18 name-only candidates, 599 unresolved references, three unserviceable references; zero confirmed city/district mappings and zero publications. No independent reviewer is designated.
- Protected-order read-only repeat **1/1 passed**; all compared fields unchanged. Preservation: **6,677 original files, zero missing, index unchanged**. Three loaded dependency samples again Healthy for R2/Redis but overall **Degraded** due to unchanged TLS/Caddy health entries; original incident causes remain open.

The details below remain invocation-specific historical evidence, not the latest source state. Controlled new default/explicit create-cancel orchestration passes with process-local persistence; full relational/actual-source-reviewed publication acceptance remains unfinished. No live Bosta shipment effect was performed.

Evidence: `.ai/evidence/phase7-20261015/` (the work began on October 15; the report uses the local execution date). The authoritative inherited record remains `.ai/reports/SHOPIXY_BOSTA_R2_LINT_QUALITY_CLOSURE_20261015.md`, including its cumulative Bosta continuation. Prior results are not relabeled as fresh executions.

## 1. Preservation and safety

- Before editing, retained all **6,677** discovered source/config/test files in the selected project scope with SHA256 hashes, plus all-untracked Git status and the index. `baseline-source/`, `baseline-hashes.json`, `git-before.txt`, `index-before.txt` are retained. No staging, commit, reset, stash, clean, migration, schema recreation or database reset was performed.
- The recorded intermediate preservation check found **zero missing original files**, an unchanged index, and no changes to appsettings, launch settings, ESLint rules, package manifests, TypeScript configuration or `AGENTS.md`. A final recheck is still required after the remaining edits.
- Credentials were not copied into this report, source or test fixtures. Synthetic tests use explicit non-live credentials. No browser storage/authentication-state export was performed.
- The protected order `ac89be7f-5e8c-4834-bcec-a1d14d5af2e6` / `ORD-C9C5-2026-01053` remains protected from mutation and fulfillment conversion. Its inherited Manual unpaid EGP 40 COD contract is not a Bosta test candidate. The October 16 read-only observation passed 1/1; `protected-order-comparison.json` found every compared order/warehouse/reservation/shipment field unchanged from the preserved Phase 6 projection. This is not a claim about every database table.
- Historical cancellation attribution and TLS/Caddy configuration were not repaired or rewritten. After the tool-server interruption, the user-launched API was observed as PID **20452**, started **2026-10-16T11:01:34.9052664Z**, using the normal Debug executable on port 5010. Disk/source-equivalence attestation is pending; successful new endpoint behavior alone is not a complete loaded-build attestation.

## 2. Bosta: implemented boundaries and limits of proof

### Genuine external reference observation

Authenticated, GET-only Bosta discovery returned **28 cities**, **592 Cairo districts**, and an account-owned pickup. Sanitized evidence is `bosta-reference-live.json`. Genuine IDs observed:

| Identity | Provider ID |
|---|---|
| Pickup | `PZ_CVuhF4R` |
| Egypt | `60e4482c7cb7d4bc4849c4d5` |
| Cairo | `FceDyHXwpSYYF9zGW` |
| Zahraa Nasr City district | `VPRW3NEl5WQ` |
| Zone observed in provider reference | `8wlSvImUB7P` |

These are provider IDs, **not fabricated Shopixy canonical place IDs**, warehouse-address admission, active booking entitlement, or permission to create a production parcel. Provider reference discovery remains GET-only.

### Source implementation

- Reuses the existing geographic releases, sync runs/candidates, mapping publications and audits. The new account-scoped controller binds tenant, carrier, environment and current saved credential reference; sync source version records `bosta-v1:{accountId:N}:{credentialReferenceId:N}`. It retains observed reference/serviceability evidence and requires explicit canonical choices.
- Draft review/publication enforces checksum fencing, independent review, current account/reference freshness, canonical hierarchy and compatible published catalog. Validated drafts cannot be extended through the ordinary mapping-add route. Generic candidate routes cannot forge or rewrite Bosta's authenticated discovery evidence.
- Generic mapping/sync/override administration is tenant-scoped; tenant audit reads do not disclose foreign carrier audits. Shipping-management permission is additionally required. These latest security changes still need the final build/regression pass.
- Mapping supersession uses newer activation evidence at read time. Historical publication rows remain append-only; they are not rewritten to mark supersession. Publication/rollback and stale-authority regression coverage has been added.
- `BostaPickupOrigin` separates external account-owned pickup authority from inventory warehouse identity. Save/validation/create verify the selected pickup through authenticated GET. The durable request includes inventory warehouse ID and provider pickup identity without inventing warehouse address evidence. Provider pickup changes between intent preparation and create are rejected.
- Checkout and registration require published destination mappings. Existing durable create claims, request/account binding, ambiguity handling and original-identity cancellation remain in place. Cancellation is confirmed only after an independent provider state read matches the exact persisted delivery/tracking identity and terminal state.
- The Admin component provides explicit reference loading, district sync, canonical selection, draft readback and independent-review resume. It does not implicitly activate an account or book a shipment. Unit tests cover scope invalidation, foreign responses, explicit review and exact checksum submission.

### What has and has not been proved

`BostaGeographyWorkflowTests` and `BostaSyntheticWorkflowTests` use the genuine captured IDs but **replay reference responses** and use EF's in-memory provider, synthetic canonical places/accounts and controlled shipment effects. They exercise persisted orchestration/readback and exact-identity cancellation in that test store. They do **not** constitute fresh authenticated reference discovery, PostgreSQL locking/trigger certification, actual Admin-to-database acceptance, or the required complete controlled end-to-end proof. No production shipment effect was sent. No relational fixture that creates/migrates/resets a database was run.

The newly authenticated headed browser renders the new Bosta geography panel against the restarted API. On October 16, real authenticated reference GET returned **200**, **28 cities / 592 districts**, with the exact genuine destination marked drop-off available. Real UI pickup discovery returned **200** with the expected pickup. No pickup selection was saved, carrier activated or shipment booked. `loaded-admin-reference-20261016.json` retains sanitized observations.

**Actual bootstrap blockers:** the loaded API returns no published canonical catalog ID and zero canonical places. The explicit Cairo sync action performed real provider discovery but returned **409 `UnknownCarrierProvider`**, before adding a sync run/candidates/audit, because an active Bosta geography-provider record was not found. No provider metadata, canonical places or mappings were fabricated to bypass these prerequisites. Actual persistence/publication and the complete safe orchestration acceptance therefore remain blocked, separately from unfinished lint work. Connection success must not override disabled configuration, geography, warehouse, entitlement or parcel-evidence blockers.

### Governed bootstrap extension explicitly authorized by the user

The user selected **Add Governed Bootstrap**, rather than leaving implementation blocked. Source now includes:

- Explicit Bosta geographic-provider registration plus authenticated draft sync, under the existing serialized geographic transaction. Only missing geographic metadata is registered; deliberately disabled metadata is not silently enabled. No account activation, configuration change, canonical import, publication or shipment effect is hidden in this action. Persisted reference readback remains account/credential/environment/tenant-bound, with genuine provider IDs, parents, endpoint provenance, serviceability and source hash retained.
- Source-attributed canonical JSON import preview and atomic draft staging using existing places, names, country profiles, hierarchy edges and release manifests. Stable canonical IDs are supplied by the reviewed import, never inferred from carrier labels. Duplicate identities, existing identities/countries needing reconciliation, missing/cross-country parents, unsupported hierarchy/levels, missing localizations and invalid postal policies block staging. A changed preview cannot be silently saved.
- Actual Admin import/review/readback UI with source identity, canonical IDs, names, parent/country relationships, policy details and pagination. Independent official-source review is required before validation, and separate explicit approval/checksum is required for publication. The imported source URI/hash cannot be substituted during review. Existing catalog and mapping publication remain separate from carrier activation and shipment execution.
- Mapping choices now reject ambiguity, incompatible kind/hierarchy and unserviceable/unproven references before approving any candidate. Existing four-eyes publication, revision/freshness and authoritative admission gates remain in place.

`docs/architecture/BOSTA_GOVERNED_GEOGRAPHY_BOOTSTRAP.md` documents the workflow and typed import contract. No authoritative canonical dataset or independent human source-review attestation has been invented. The workflow is implemented; completing the actual operational publication requires a real reviewed source and an independent authorized reviewer, not synthetic test data.

`bootstrap-admin-render-20261016.json` verifies that the actual Admin renders the new panel and withholds staging/publication without reviewed inputs. This is render-only evidence, not completed persistence/publication acceptance. `bootstrap-backend-tests1` passed **68/68** and `bootstrap-ui-tests2` passed **15/15** (including the corrected stable translation mock for deletion-modal tests). Broader reruns are recorded separately below.

The user stopped the API for installation. `bootstrap-debug-equivalence` passed and attested unchanged inputs. Its pre-installation comparison correctly found the running API assembly was older, while Core/Infrastructure matched. Local configuration files absent from the original project inventory were unchanged between runtime observations; their absence must not be called a detected configuration edit. `bootstrap-artifact-install.json` records installation of exactly nine verified artifacts with all original binaries backed up and configuration unchanged. The user restarted the API as PID **5100** at **2026-10-16T11:46:32.0358584Z**. Before/after loaded observations match all three verified API/Core/Infrastructure assemblies, unchanged configuration, and disk writes before process start. The comparison is source/disk/process equivalence plus actual endpoint behavior, not a direct managed-memory MVID read.

**Real draft acceptance now completed:** through the authenticated Admin, explicit bootstrap approval returned **200** and persisted run `e9d38578-2e31-458f-9df6-8ba600917f42`, **621 candidates** (one country, 28 cities, 592 districts), genuine parent/provider identities and source checksum. Three unserviceable candidates were rejected. An independent HTTP read and then a fresh-page Admin resume returned the same persisted run/hash; the UI rendered 593 district options including the empty choice, with the two unserviceable districts disabled (the third excluded reference is a city). The complete compared account projection remained unchanged: **ConnectionTestPending / NotReady, enabled=false, canExecute=false**, with disabled configuration, missing pickup selection and unpublished mappings still reported. A negative empty canonical import preview returned **400**, with zero catalog rows afterward. No canonical source, mapping, activation or shipment effect was invented. Evidence: `bosta-governed-bootstrap-live.json` and the sanitized full `bosta-bootstrap-persisted-reference.json`.

Post-bootstrap protected-order observation again passed **1/1**; `protected-order-post-bootstrap-comparison.json` found all compared fields unchanged. This does not certify every business table. Full canonical official-source review, publication by an independent operator and controlled create/cancel acceptance remain separate, unfinished gates.

The supplied Storefront credentials were also used for sign-in and a read-only empty-cart check (`storefront-browser-20261016.json`); no checkout/order was submitted. Anonymous startup 401/MISSING_TOKEN events and bootstrap/i18n warnings are qualified, not described as a clean-console certification. Generated login snapshot data was sanitized; a scan of this invocation's retained browser text artifacts found none of the supplied credentials. No browser storage state was exported.

## 3. R2 and Redis: separated observations, no invented root cause

- AWS SDK handler caching and observed warm connection reuse disprove the earlier inference that every recreated SDK client necessarily opens cold TCP/TLS. No singleton-client change is justified by that inference.
- R2's source probe keeps the **5,000 ms overall budget** and minimal `ListObjectsV2(MaxKeys=1)` read. It permits at most two **2,000 ms** attempts with a **100 ms** scheduling delay, limited to transient timeout/network/provider-failure categories. Authentication/permission, DNS, TLS and malformed-response failures are not treated as successful probes. SDK retries remain zero. Caller cancellation propagates. First-failure evidence remains visible even when the second attempt succeeds.
- Transport event tracing records measured DNS/TCP/TLS/connection-acquisition/response-header intervals where observed. Missing phases and unseen warm connections are marked **unobserved**, not assigned invented durations or called definitely cold. Body/parsing/completion is explicitly a combined interval, not a parser-only measurement. Latest source separates measured retry scheduling from operation/transport duration and retains per-attempt provider status/request IDs; it awaits final verification.
- The configured observer performed **24** low-rate minimal R2 reads: all Healthy, approximately **146.7–501.4 ms**. It recorded no object names or contents and made no R2 writes. Warm samples mostly waited approximately 160–213 ms for response headers. This separate process is **not API activation**, and successful samples do not explain or erase the retained prior five-second R2 failure.
- Redis's **100 ms slow-PING readiness threshold is unchanged**. A separate earlier API sample reproduced a readiness 503 with a successful **288 ms PING while R2 was Healthy**. That is a Redis latency failure, not an R2 failure.
- The later 24-sample configured observer saw Redis PING approximately **2.1–95.1 ms**, with queue/thread-pool/GC observations retained in `dependency-traced-probes.json`. Those successful observations were not simultaneous with the failed PING and do not establish its root cause.
- The Redis connection holder now recovers initialization failures with single-inflight connection attempts, cooldown, bounded waits, cancellation handling and late-connection disposal/retention. This repairs initialization recovery; it does **not** establish that unexplained runtime PING spikes are fixed.

Original R2 timeout attribution and intermittent Redis latency remain **OPEN**. No arbitrary readiness threshold/overall-timeout increase, false-green override or conflation of dependency failures is claimed.

Before bootstrap installation, `health-loaded-before-bootstrap.json` recorded three live/ready pairs. HTTP was 200, but readiness payload status remained **Degraded**; R2 and Redis entries were Healthy in those samples. These few observations are not root-cause closure or an all-healthy readiness certification.

After the verified user restart, three additional pairs (`health-loaded-after-bootstrap.json`) again returned HTTP 200 with **Degraded** readiness due to `certificate_provider` / `caddy_edge`. R2 was Healthy (approximately **178–652 ms**) and Redis was Healthy (**7.8–15.9 ms PING**). TLS/Caddy remains deliberately outside this repair scope; these successful dependency samples do not explain either prior incident.

## 4. Lint: reduced, not clean

Original full maintained lint: **193 errors / 186 warnings** (`lint-before.json`). `lint-current3` reached **33 errors / 123 warnings**, exit **1**. `lint-comparison-current3.json` compares file/rule/severity/message occurrence counts, ignoring line movement, and records **zero introduced findings** for that invocation. The bootstrap rerun is recorded separately when complete; numeric 0/0 closure remains outstanding.

Work includes reviewed unused-import/binding removal, replacing explicit `any` with DTO/prop/error-boundary types, stable dependency handling, preserving unsaved opening-balance input across presentation changes, and extracting non-component helpers into separate modules for Fast Refresh. Tests/callers and declarations follow the moved helper boundaries. No rule/scope change, new exclusion, suppression or blanket autofix was used.

Remaining work includes restricted transport/component/API imports and substantial legacy hook/Fast Refresh findings. The contradictory Axios boundary must be resolved through a coherent architecture, not an alternate import spelling or exemption. Numeric lint closure has **not** been achieved.

## 5. Invocation ledger — do not add overlapping selections

Every invocation retains independent stdout, stderr and exit metadata. TRX/JSON files are separate for reruns.

| Invocation | Result / qualification |
|---|---|
| `mapping-reliability-tests2` | 53/53 passed at that source snapshot; not a final rerun |
| `admin-regressions1` | 1,786 passed / 3 failed of 1,789; failed cases subsequently passed in `admin-failure-detail` |
| `ui-geography-tests` | Failed under concurrent heavy workload; original failure/time-limit evidence retained |
| `ui-geography-tests2` | Focused UI set passed with one worker and unchanged test time limits |
| `admin-typecheck1` | Failed; three type-boundary issues corrected |
| `admin-typecheck2` | Passed at that source snapshot; later edits require rechecking |
| `pickup-fixture-build` | External shell timeout; no completed exit result, not counted as a passed build |
| `synthetic-workflow-build` | One test compilation error; retained and corrected |
| `synthetic-workflow-build2`, `3`, `4` | Build invocations passed; later production/test source changed |
| `backend-regressions1` | 383 passed / 7 failed of 390 |
| `backend-regressions2` | 388 passed / 2 failed of 390 |
| `backend-regressions3` | 390/390 passed; later security/publication changes were not in this snapshot |
| `backend-regressions4` | 395 passed / 1 failed of 396; new test attempted forbidden mutation of append-only publication evidence; corrected using release state instead |
| `append-only-publication-build` | Passed; later health timing/permission changes require another build |
| `backend-geography-regressions5` | 425 passed / 3 failed of 428; expanded historical-artifact checks described below |
| `admin-regressions2` | Interrupted by tool-server restart; no completed exit/result, not counted as passed |
| `current-release-build` | Passed; 3,597 captured C#/project inputs unchanged across build; nine API runtime artifacts hashed |
| `backend-current-expanded6` | 425 passed / 3 failed of 428; same three historical-artifact failures, including all latest health/permission changes |
| `admin-regressions3` | Worker heap exhaustion; exact verification process tree stopped, exit 1; no test result totals certified |
| `admin-diagnostic4` | Shell time limit after worker heap exhaustion; not passed |
| `admin-diagnostic5` | Progress reporter isolated deletion-modal translation-mock identity loop; heap exhaustion and exact runner termination retained as exit 1 |
| `protected-order-readonly` | 1/1 passed; read-only repeatable-read transaction, no SaveChanges/provider call |
| `admin-typecheck3`, `admin-typecheck4` | Passed; second includes the governed bootstrap UI/tests |
| `governed-bootstrap-build`, `governed-bootstrap-build2` | Passed, each source-attested; latest captured 3,601 inputs with nine runtime artifacts hashed |
| `bootstrap-backend-tests1` | 68/68 passed; synthetic governance/workflow tests, not official-source or relational acceptance |
| `bootstrap-ui-tests1` | 13 passed / 2 failed; new test exact-text queries corrected by separating identity/parent display elements |
| `bootstrap-ui-tests2` | 15/15 passed; explicit import/review/publication, bootstrap consent, scope invalidation and deletion-modal safety |
| `backend-bootstrap-expanded7` | 483 passed / 3 failed of 486; includes new import/bootstrap, canonical initial publication and controlled synthetic workflow suites; same historical-artifact failures |
| `bootstrap-debug-equivalence` | Passed and source-attested; private Debug build used for verified artifact installation |
| `admin-regressions6` | **1,798/1,798 passed**; full one-worker rerun, no larger heap or test timeout configured; later formatting-prop/serviceability refinements need their own verification |
| `storefront-regressions1` | **528/528 passed** |
| `admin-build-bootstrap`, `storefront-build1` | Passed, isolated output directories; regular dist directories not replaced |
| `storefront-typecheck1` | Passed |
| `lint-bootstrap4` | 33 errors / 123 warnings; zero introduced findings in occurrence-count comparison |
| `admin-typecheck5` | Passed after presentation-only component formatting inputs were moved to page-owned props |
| `marketplace-presentation-tests1` | Passed; marketplace and frontend architecture suite after formatting-prop extraction |
| `protected-order-post-bootstrap` | 1/1 passed; compared projection unchanged after actual geographic draft bootstrap |
| `preservation-bootstrap` | 6,677 original files checked, zero missing, 153 changed, index unchanged |

The broadened geography architecture run found three failures in unmodified historical-artifact tests: `Geo5Migration_MustBeAdditiveAndGeographicOnly` expects missing `PointOfSale.Infrastructure/Migrations/20260705170009_InitialGeographicRuntimeFoundation.cs`; `Geo3ArtifactsAndContext_MustRecordGovernanceFreeze` and `Geo4ArtifactsAndContext_MustRecordRuntimeAuthorityFreeze` expect historical markers in `.ai-context` documents. This phase did not recreate a migration, alter those tests, insert historical freeze claims or weaken their assertions to make the run green. The expanded run remains failed. Its successful runtime cases include the append-only publication/rollback workflow; they do not erase the three failures.

## 6. Remaining acceptance gates

1. Continue lint remediation under unchanged rules/scope, then rerun current-source Admin regressions/typecheck/build and maintained lint. Lint must actually reach 0/0 for full closure; the already-passed full run must not be relabeled as covering later edits.
2. Preserve the three expanded historical-artifact failures separately; do not recreate historical migrations or invent freeze documentation to erase them.
3. Complete actual canonical-source import/review/publication and account-bound mapping acceptance with a genuine authoritative source and independent authorized reviewer. The implementation no longer depends on invented catalog data or manually inserted provider rows.
4. Complete the controlled create/cancel proof without production orders/parcels, real carrier mutations or a database migration/reset. Real reference discovery/draft persistence now has actual Admin/PostgreSQL evidence; in-memory shipment replay alone still does not close the remaining gate.
5. Keep all live Bosta effects disabled and preserve the protected order. Repeat preservation/loaded-build checks if backend source changes again.
6. Retain truthful dependency diagnostics; do not label either original incident resolved without supporting failure-phase evidence. TLS/Caddy is not being reopened to force readiness green.

**Disposition: work continues; no full closure, no live Bosta go-ahead, no final lint certification.**

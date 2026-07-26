# Executive Summary

This is the final independent execution-readiness review for the Shopixy Persistence Recovery Program.

Final certification: **GO WITH CONDITIONS**.

The Recovery Program's core architectural direction is sound: the runtime EF model should be treated as the persistence authority, the broken historical chain should be retired from the active build path, and a new differ-backed canonical baseline should be generated from the runtime model only after the old chain is archived.

Execution must **not** begin from the repository state observed in this review. The current tree fails multiple execution preconditions in RECOVERY-3, and several source-verified facts are more nuanced than the recovery documents state. These do not invalidate the baseline strategy, but they do block immediate execution.

Critical stop conditions before S-01:

- The working tree is not clean. `PointOfSale.Infrastructure` and `PointOfSale.Core` currently have 272 git status entries, including persistence model, configuration, migration, and snapshot changes.
- `docs/audits/` and `docs/recovery/` are untracked. The recovery documents themselves are not yet committed source truth.
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` is dirty and has been partially advanced with later model content, so it is not accurately described as simply frozen at `20260510130500`.
- `tools/Recovery3.DifferHarness/` does not exist, while RECOVERY-3 P-10 requires it before execution.
- `docs/recovery/RECOVERY-3_EXECUTION_LOG.md` does not exist, while RECOVERY-3 P-12 requires it before execution.
- The plan's guardrail wording conflicts with the expected baseline artifact set: it says every `*.cs` in `Migrations/` must have a sibling Designer, but the required `ApplicationDbContextModelSnapshot.cs` intentionally has no Designer.
- The plan assumes Unix-style shell tooling (`ls | grep | wc`, `find`, `sha256sum`, `bash`, `dropdb`) while the live execution environment is Windows PowerShell. The runbook is not yet zero-ambiguity for this environment.

If those conditions are closed and re-verified from a clean recovery branch, implementation may begin.

# Fresh Source Verification

Review date: 2026-07-23.

Primary source: live source tree under `C:\Users\fadya\source\repos\PointOfSale`.

No code, migrations, snapshots, SQL, or database state were modified by this review. Only this report file was created.

Verified runtime model topology:

| Fact | Source-verified result |
|---|---:|
| EF context | `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` |
| Design-time factory | `PointOfSale.Infrastructure/Data/ApplicationDbContextFactory.cs` |
| Runtime provider | Npgsql via `PointOfSale.API/Program.cs` and design-time factory |
| DbSet declarations | 242 |
| Configuration files under `Data/Configurations` | 123 |
| `IEntityTypeConfiguration<>` declarations | 156 |
| Current snapshot file | `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` |
| Snapshot line count | 18,859 |
| Snapshot `ProductVersion` | 9.0.1 |
| Snapshot entity blocks | 357 |
| Active migration class files by filename | 149 |
| Active Designer files | 49 |
| Active snapshot files | 1 |
| Active `.sql` files under active migrations | 97 |
| Legacy `Data/Migrations` class files | 40 |
| Legacy `Data/Migrations` Designer files | 38 |
| Legacy `Data/Migrations` `.sql` files | 12 |
| Repo-wide `.sql` files | 396 |
| History CSV rows | 128 |
| Last applied history row | `20260511150000_S80_AddTenantsCurrencyDefaultValue` |
| .NET SDK pin | `global.json` requires 9.0.308 |
| Actual `dotnet --version` | 9.0.308 |
| `dotnet-ef` local tool | 9.0.1, rollForward false |
| EF Design package | 9.0.1 |
| Npgsql EF provider | 9.0.2 |

Migration topology by filename:

| Comparison | Result |
|---|---:|
| Active filename IDs | 149 |
| Applied history IDs | 128 |
| Filename intersection | 69 |
| Pending by filename | 80 |
| Orphan history by filename | 59 |
| Pending filename IDs below last applied | 34 |
| First pending by filename | `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` |

Migration topology by EF-style source attributes:

| Comparison | Result |
|---|---:|
| Source `[Migration]` IDs across active + legacy folders | 155 |
| Applied history IDs | 128 |
| Attribute-ID intersection | 103 |
| Pending discoverable-by-attribute IDs | 52 |
| Orphan history by attribute IDs | 25 |
| Pending attribute IDs below last applied | 29 |
| First pending attribute ID | `20260419113000_AddOrderRequestIdempotencySnapshotHash` |

Important nuance: the recovery documents primarily count migration filenames. EF Core discovery is attribute-based, and many migration class files are not equivalent to discoverable EF migrations. Active class files include a mix of:

- Generated migrations where `[Migration]` and `[DbContext]` live in the Designer partial.
- Hand-authored classes with explicit `[Migration]` and sometimes explicit `[DbContext]`.
- Hand-authored classes with neither generated Designer metadata nor explicit discoverability metadata.

The legacy `PointOfSale.Infrastructure/Data/Migrations` folder is not excluded by the Infrastructure project file. Because it is inside the SDK project tree and its Designer files contain `[DbContext(typeof(ApplicationDbContext))]` plus `[Migration(...)]`, it should be treated as compiled/discoverable until moved or explicitly excluded. RECOVERY-3 does move it, which is correct, but the pre-execution facts should state that it is not merely inert by virtue of folder name.

Snapshot verification:

- The snapshot contains later artifacts such as `ProviderDeliveryOutboxMessage`, `ProviderDeliveryReplayAudit`, `StripeWebhookReplayRecord`, and `PaymobWebhookReplayRecord`.
- The snapshot has no hits for the Carrier account runtime entities.
- The snapshot's `AffiliatePayoutRequest` block has no `PaidBy` property.
- `AffiliatePayoutRequest.PaidBy` exists in `PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs`.
- The only persistence artifact found for `PaidBy` is `PointOfSale.Infrastructure/Migrations/20260626153000_AffiliatePayoutPaidByAuditIdentity.sql`.

Carrier runtime verification:

- `CarrierAccount`, `CarrierCredentialReference`, `CarrierWarehouseAssignment`, `CarrierAccountPolicy`, `CarrierServiceLevelEnablement`, `CarrierConnectionTestRecord`, and `CarrierAdministrationAudit` exist under `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`.
- `ApplicationDbContext` exposes DbSets for the Carrier account runtime set.
- Snapshot and migration-chain hits for those Carrier account runtime entities were not found.

Build and CI verification:

- `PointOfSale.sln` includes `PointOfSale`, `PointOfSale.Core`, `PointOfSale.Infrastructure`, `PointOfSale.API`, and `PointOfSale.Tests`.
- No `.github` workflow directory was found.
- No `azure-pipelines`, `gitlab-ci`, `appveyor`, `jenkins`, or comparable pipeline file was found by filename search.
- Existing deployment/migration automation is mostly ad hoc scripts and old setup scripts, including `setup-database.ps1` and `setup-database.bat`, which still call `dotnet ef migrations add InitialCreate`.
- I did not run `dotnet build`, `dotnet ef`, or database commands because this was a no-mutation readiness review and those commands can write build artifacts or touch external state.

# Recovery Program Review

The completed recovery work correctly identifies the central architecture failure: persistence has multiple drifted materialization artifacts, while the only complete and living model is the runtime EF model.

The program is directionally correct on these points:

- Runtime model as authority is the only defensible hierarchy.
- Snapshot, Designers, migrations, SQL, live schema dumps, and history CSV are derived or operational evidence, not architecture.
- Continuing to repair the existing chain in place is not a bounded or reliable strategy.
- A new canonical baseline generated from a clean migrations assembly is the most recoverable path.
- Existing history must be preserved for forensics but retired from the active materialization path.
- Existing environment re-stamping belongs after greenfield materialization proof, not before.

However, the recovery documents overstate or simplify several source facts:

- The snapshot is not purely frozen at the last Designer. It is dirty and contains later model content.
- "100 post-freeze hand-authored migrations" is not chronologically accurate. There are 100 Designer-less active class files, but 50 are at or before `20260510130500`; 50 are after it.
- "149 migration classes" is a filename count, not an EF-discoverable migration count.
- The legacy `Data/Migrations` folder is not currently inactive from a compile/discovery standpoint.
- CI guardrail claims are aspirational; no CI pipeline file exists in this checkout.

These issues do not overturn the hybrid baseline decision. They do require RECOVERY-1/2/3 to be treated as governance documents with corrections required before execution evidence is signed.

# Constitution Review

RECOVERY-1 is fundamentally sound as a constitution.

Verified strengths:

- Authority hierarchy is correct: runtime EF model must govern.
- Artifact roles are mostly correct: snapshot is generated cache; migrations are materialization history; raw SQL is operational; DB dumps and `__EFMigrationsHistory` are evidence, not intent.
- Invariants are enforceable in principle.
- Success criteria are mechanical and tool-verifiable.
- Exit gates correctly require source evidence, role assignment, invariants, and no side effects.

Required corrections:

- RECOVERY-1 says `PointOfSale.Infrastructure/Migrations/` has 190 `.sql` files. Current active folder has 97 `.sql`; repo-wide SQL is 396. The number must be scoped.
- RECOVERY-1 says there are 50 later classes with no Designer. Current active folder has 100 Designer-less class files total, with 50 after the last Designer.
- The legacy `Data/Migrations` role should explicitly state "currently compiled until archived/excluded" rather than implying it is inert today.
- Artifact role #5 should distinguish filename class inventory from EF-discoverable migration inventory.

Conclusion: RECOVERY-1 is complete enough as an authority constitution, but its evidence table needs source-corrected amendments before execution logs cite it as exact fact.

# ADR Review

RECOVERY-2 selects Candidate C: consolidated canonical baseline plus frozen archive plus guardrails.

That decision remains the best strategy.

Rejected alternatives remain weaker:

- Completing the existing chain is not superior. It leaves interleaved, partly manual, partly undiscoverable history in the canonical path and depends on a compromised differ baseline.
- Snapshot refresh plus selective chain repair is not superior. It requires hand-repairing many migrations and keeps the fragile history in the path.
- Pure greenfield squash without explicit archive governance is slightly weaker than Candidate C because it loses forensic discipline.

Source-verified objections against RECOVERY-2:

- The statement that the snapshot is "still frozen at the identical point" is inaccurate in the live tree; the snapshot has uncommitted later content.
- The statement "100 post-freeze" should be "100 Designer-less active class files; 50 after the last Designer."
- The ADR does not separate EF-discoverable migrations from migration-looking class files.
- It assumes guardrails and CI can be installed, but no existing CI file was found.

Do these objections invalidate Candidate C? No.

They actually strengthen the case for Candidate C. A snapshot that has been irregularly or manually advanced is less trustworthy, not more. A compiled legacy folder with migration metadata is more reason to retire all historical migration sources from the active assembly before generating the baseline. The best strategy remains: clean branch, archive every old migration source, verify `migrations list` empty, then emit the baseline from runtime model.

# Execution Plan Review

RECOVERY-3 is structurally close to executable but not safe to start now.

Verified strengths:

- Execution order is mostly correct: freeze point, toolchain proof, build/design-time load, archive manifest, archive move, empty migrations assembly proof, guardrails, baseline generation, differ-zero proof, scratch DB materialization, script generation, smoke test, guardrail proof, certification PR.
- Rollback model is appropriate for repository-only changes plus disposable scratch database.
- Scratch-only database validation is correct.
- Existing environment re-stamping is correctly deferred to RECOVERY-4.
- The archive plan preserves history rather than deleting it.
- The plan correctly requires `dotnet ef migrations list --no-connect` to prove the active migrations assembly is empty after archiving.

Blocking issues:

- Current repository state fails P-02. The worktree is not clean, and persistence-critical files are modified/untracked.
- P-10 is not satisfied. No differ harness exists under `tools/Recovery3.DifferHarness/`.
- P-12 is not satisfied. No execution log exists.
- P-03, P-07, P-08, and P-11 are not externally evidenced in the repository.
- The plan is written with Unix command syntax, but the observed shell is Windows PowerShell.
- Guardrail specification must exclude `ApplicationDbContextModelSnapshot.cs`; otherwise it will fail against the intended three-file baseline folder.
- The plan should use EF-discoverability checks in addition to filename counts. Required checks: `dotnet ef migrations list --no-connect` before and after archive, and source scans for `[Migration]`/`[DbContext]` metadata outside the new baseline folder.
- The plan says to wire guardrails into CI, but no CI pipeline file exists. It must either create one as part of RECOVERY-3 or explicitly gate on the team's actual external CI system.
- `setup-database.ps1` and `setup-database.bat` still teach the old workflow (`dotnet ef migrations add InitialCreate`). They are not in the RECOVERY-3 archive plan and would remain as misleading deployment scripts unless corrected or quarantined.

Conclusion: the execution plan can succeed after corrections, but the current repository is not execution-ready.

# Red Team Findings

Strongest argument against the proposed strategy:

The program is trying to solve governance failure with a one-time baseline. If the team can accumulate uncommitted runtime model changes, dirty snapshots, untracked recovery documents, manual SQL scripts, Designer-less migration classes, and no CI pipeline, then cutting a baseline may only create a new false sense of safety. The same operating model could recreate drift immediately after RECOVERY-3.

That argument is serious but not fatal.

The correct response is not to abandon the baseline strategy. The correct response is to make the entry gates non-negotiable and enforce recurrence prevention before the baseline merges:

- Clean branch from an agreed base SHA.
- Full source inventory and archive manifest.
- Empty migrations assembly proof after archive.
- Baseline generated only by EF tooling.
- Differ-zero proof by `dotnet ef` and a custom differ harness.
- CI or equivalent merge-blocking guardrails before merge.
- Explicit quarantine or rewrite of old setup scripts that teach manual migration generation.

If the gates are enforced, the red-team objection is answered. If they are waived, the program should be stopped.

# Risk Register

| ID | Severity | Risk | Evidence | Required treatment |
|---|---|---|---|---|
| R-01 | Critical | Baseline could be cut from an uncommitted persistence state | 272 status entries under Core/Infrastructure; dirty snapshot | Clean branch, commit/park changes, re-run verification |
| R-02 | Critical | RECOVERY documents are untracked evidence | `docs/audits/` and `docs/recovery/` untracked | Commit/ratify docs before execution |
| R-03 | Critical | Required differ harness absent | `tools/Recovery3.DifferHarness/` missing | Implement and build harness before S-01 or downgrade P-10 out of preconditions |
| R-04 | Critical | Guardrail spec contradicts baseline artifact set | Snapshot `.cs` has no Designer by design | Exclude `ApplicationDbContextModelSnapshot.cs` and require exactly one snapshot |
| R-05 | High | Plan commands are not portable to actual shell | Current shell is PowerShell; plan uses Unix commands | Provide PowerShell equivalents or require Git Bash/WSL explicitly |
| R-06 | High | Filename counts do not equal EF discoverability | 149 filename IDs vs 155 attribute IDs across active+legacy | Add EF-discoverability inventory to gates |
| R-07 | High | Legacy migrations are compiled/discoverable until moved | `Data/Migrations` inside project; 38 Designer metadata files | Preserve S-05 move; add pre/post `migrations list` proof |
| R-08 | High | No CI file found for guardrail wiring | No `.github` or pipeline files found | Define/create the actual merge-blocking CI path |
| R-09 | Medium | Snapshot is irregularly advanced, not simply frozen | Snapshot contains later entities but misses Carrier/PaidBy | Correct docs; never trust current snapshot as baseline |
| R-10 | Medium | Old setup scripts preserve bad workflow | `setup-database.ps1/.bat` call `migrations add InitialCreate` | Archive, rewrite, or mark obsolete during recovery |
| R-11 | Medium | Build/design-time state unproved in this review | Build not run to avoid mutation | P-05/P-06 must run on clean branch |
| R-12 | Low | SQL corpus counts are inconsistently scoped | Active 97, legacy 12, repo-wide 396 | Use scoped counts in all logs |

# Blockers

Critical blockers that stop immediate execution:

1. Dirty persistence worktree.
   - `git status --porcelain -- PointOfSale.Infrastructure PointOfSale.Core` returned 272 entries.
   - A baseline generated from this state would not have a stable, reviewable source base.

2. Recovery evidence not committed.
   - `docs/audits/` and `docs/recovery/` are untracked.
   - Future reviewers cannot treat untracked files as a ratified final approval chain.

3. Dirty, irregular snapshot.
   - `ApplicationDbContextModelSnapshot.cs` is modified.
   - It contains later entities but still misses known runtime model members.
   - Current snapshot cannot be used as an execution fact except as evidence of drift.

4. Missing differ harness.
   - RECOVERY-3 P-10 requires `tools/Recovery3.DifferHarness/`.
   - It does not exist.

5. Missing execution log.
   - RECOVERY-3 P-12 requires `docs/recovery/RECOVERY-3_EXECUTION_LOG.md`.
   - It does not exist.

6. Guardrail contradiction.
   - RECOVERY-3 expects the new active migrations folder to contain baseline `.cs`, baseline `.Designer.cs`, and `ApplicationDbContextModelSnapshot.cs`.
   - The guardrail wording says every `*.cs` must have a sibling Designer, which would incorrectly fail the snapshot unless amended.

High blockers or required corrections:

- Convert runbook commands to the actual execution shell or state a required shell environment.
- Add EF-discoverability inventory checks, not only filename counts.
- Define the CI or merge-blocking system that will run the guardrails.
- Quarantine or update old setup scripts that still instruct `dotnet ef migrations add InitialCreate`.
- Correct RECOVERY-1/2/3 wording around snapshot freeze, post-freeze counts, SQL counts, and legacy folder activity.

Medium and low blockers:

- Re-run build and design-time model load on a clean branch.
- Verify scratch PostgreSQL version and isolation.
- Verify no real environment connection strings are reachable in the execution shell.
- Create repository bundle and archive copies before S-01.

# Final Recommendation

Recommendation: **GO WITH CONDITIONS**.

The Recovery Program may begin implementation only after every Critical blocker above is closed and re-verified from a clean dedicated branch. The final pre-execution checklist must include:

1. Commit or park all unrelated work; start from `recovery/3-greenfield-baseline` at an agreed base SHA.
2. Commit/ratify `docs/audits/` and `docs/recovery/`.
3. Amend RECOVERY-1/2/3 factual inaccuracies found in this review.
4. Add or adjust RECOVERY-3 guardrail spec so it excludes the model snapshot and validates EF discoverability.
5. Provide PowerShell-native commands or require a specific Unix shell.
6. Create and build `tools/Recovery3.DifferHarness/`.
7. Create `docs/recovery/RECOVERY-3_EXECUTION_LOG.md`.
8. Prove build, design-time context load, scratch PostgreSQL availability, and no real-environment connectivity.
9. Define and wire the actual CI/merge-blocking guardrail path.
10. Re-run this readiness gate's source counts immediately before S-01.

No Critical blocker may be waived.

# Final Certification

**GO WITH CONDITIONS**

The hybrid canonical baseline strategy is architecturally approved. Immediate execution is not approved from the current repository state. Execution may begin only after the listed Critical blockers are closed and re-verified.

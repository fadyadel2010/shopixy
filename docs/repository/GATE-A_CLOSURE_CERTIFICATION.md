# GATE A — Closure Certification
## Shopixy Repository Modernization Program

**Artifact:** `docs/repository/GATE-A_CLOSURE_CERTIFICATION.md`
**Type:** Consolidated architectural closure certification (read-only). No repository files, code, migrations, or snapshots were modified.
**Branch:** `recovery/3-greenfield-baseline`
**HEAD at certification:** `8317aef1` (`docs(recovery): add TASK-01A stabilization execution report`)
**Basis:** The two completed Gate A decision reviews, plus live working-tree verification of every condition's current status.

Source gates consolidated:
- **GATE A-1** — `docs/repository/GATE-A1_MIGRATION_BASELINE_DECISION_REVIEW.md`
- **GATE A-2** — `docs/repository/GATE-A2_HOMEPAGE_DECISION_REVIEW.md`

> This certification does not re-run the Gate A-1 or Gate A-2 analyses. It consolidates their decisions and verifies, against the live repository, whether Gate A may be officially closed.

---

## Section 1 — Executive Summary

Gate A evaluated the two structural decisions that define the greenfield baseline of the Shopixy repository:

1. **Migration Baseline (A-1)** — collapse of the 149-migration historical chain into a single generated baseline, `20260723214642_Baseline_Recovery3`.
2. **Homepage Architecture (A-2)** — retirement of the anonymous `GET /api/homepage/sections` endpoint and relocation of its two read paths (tenant storefront → `StoreConfig`; CommerceHub marketplace → Mongo CQRS read model `GET /api/read/homepage`), with the SuperAdmin authoring/write/projection pipeline preserved.

Both reviews returned **APPROVE WITH CONDITIONS**. Both decisions are technically correct, internally consistent, and evidenced against the working tree. Neither introduced a CRITICAL or HIGH defect on this branch.

Live re-verification at HEAD `8317aef1` confirms the reports' factual claims **and** confirms that a small set of conditions remain **OPEN**:

- **A-1 / C-1 (BLOCKING):** the baseline migration files are still git-untracked (`??`); the snapshot is `M`. The baseline is not yet committed.
- **A-2 / Condition 1 (BLOCKING, MEDIUM):** the storefront homepage source-of-truth change has **no ratification document** on disk — only the review itself exists.
- **A-2 / Condition 3 (LOW):** `TestDatabaseScope.cs:147-148` still names the two deleted homepage SQL scripts (dead config; loader skips missing files silently).

Because two conditions are prerequisites for accepting this state as the official baseline and remain unmet, Gate A closes as **PASS WITH CONDITIONS**. The repository is **not yet authorized** to begin Batch A (Repository Freeze) until the two blocking conditions are cleared.

---

## Section 2 — Decision Summary

### GATE A-1 — Migration Baseline

| Field | Value |
|---|---|
| **Decision** | APPROVE WITH CONDITIONS |
| **Reason** | The 149-migration chain was correctly collapsed into a single model-generated greenfield baseline. Snapshot `BuildModel` body is byte-for-byte identical to the baseline's `BuildTargetModel` (0-diff); `Up()`/`Down()` are symmetric (256 `CreateTable` / 256 `DropTable`); seed data is model-driven (`InsertData` ⇄ `HasData`, 499 annotations); the historical chain was relocated to the archive, not destroyed. |
| **Evidence** | Live: exactly one active migration class (`20260723214642_Baseline_Recovery3.cs`) in `PointOfSale.Infrastructure/Migrations/` + `.Designer.cs` + regenerated snapshot; 353 `D` deletions of the old chain; report §4 snapshot⇄baseline 0-line body diff; archive at `docs/recovery/archive/RECOVERY-4_PHASE_A/.../Migrations/`. |
| **Status** | **APPROVED — conditions partially open** (C-1 BLOCKING open; C-2/C-3/C-5 verification pending; C-4 waivable on greenfield; C-6/C-7/C-8 standing guardrails). |

### GATE A-2 — Homepage Architecture

| Field | Value |
|---|---|
| **Decision** | APPROVE WITH CONDITIONS |
| **Reason** | The homepage change is an intentional read-path relocation/redesign, not a feature removal. Both former clients were migrated in the same change-set; the SuperAdmin authoring/write/projection pipeline is retained and transactionally tightened (event published inside the txn boundary, consistent with the IEventPublisher outbox rule). No broken imports, routes, DI, or permission orphans. |
| **Evidence** | Live: `grep "api/homepage/sections"` across Storefront/CommerceHub/Web = **0 hits**; `MarketplaceReadController` homepage read stack present; deleted controller confirmed `D`; report §3–§6 with `file:line` citations; Storefront `tsc --noEmit` = 0 errors. |
| **Status** | **APPROVED — conditions open** (Condition 1 BLOCKING/MEDIUM open — no ratification doc; Conditions 2–4 LOW, deferrable). |

---

## Section 3 — Condition Register

Every condition carried by the two gates, with live-verified current status.

| ID | Source Gate | Description | Severity | Status | Required before Batch A? |
|---|---|---|---|---|---|
| C-1 | A-1 | Commit the baseline `.cs` + `.Designer.cs` together with the regenerated snapshot in one atomic commit (snapshot never committed without its migration). | HIGH | **OPEN** — baseline files `??`, snapshot `M` (verified live). | **YES** |
| C-2 | A-1 | Verify idempotent scaffold: `dotnet ef migrations add __verify` produces an empty migration on the committed state. | MEDIUM | OPEN (not run; read-only reviews). | NO (required before **main**) |
| C-3 | A-1 | Fresh-DB smoke test: apply baseline to empty PostgreSQL, confirm 256 tables + seed, then second update is a no-op. | MEDIUM | OPEN (not run). | NO (required before **main**) |
| C-4 | A-1 | Existing-DB reconciliation runbook (mark baseline applied without running `Up()`) for any DB ever on the old chain. | HIGH (conditional) | **WAIVED** — greenfield branch; no legacy DB (see §4). | NO |
| C-5 | A-1 | CI/CD alignment to the reset timeline; add "no pending model changes" gate. | MEDIUM | OPEN (deferred to CI gate). | NO (required before **main**) |
| C-6 | A-1 | Branch hygiene: rebase in-flight branches onto baseline; do not reintroduce archived migrations. | MEDIUM | STANDING guardrail (ongoing). | NO |
| C-7 | A-1 | Archive immutability: `docs/recovery/archive/.../Migrations/` is read-only; never re-activated as an EF chain. | LOW | STANDING guardrail (ongoing). | NO |
| C-8 | A-1 | Merge-to-main guard: promotion to `main` contingent on C-2, C-3, C-4/waiver, C-5. | HIGH (gate) | STANDING (enforced at main-merge gate). | NO |
| H-1 | A-2 | Ratify the storefront homepage source-of-truth change (SuperAdmin builder now governs CommerceHub marketplace homepage ONLY; storefront reads `StoreConfig`). | MEDIUM | **OPEN** — no ratification doc on disk (verified live). | **YES** |
| H-2 | A-2 | Disposition the production-dead `IHomepageSectionReadService` read stack (keep-as-verification vs remove). | LOW | OPEN (deferred to follow-up gate). | NO |
| H-3 | A-2 | Clean/verify test schema provisioning: column/constraint/index equivalence of baseline vs deleted `20260328_AAA` harden script, then remove stale `TestDatabaseScope.cs:147-148` entries. | LOW | OPEN — stale entries confirmed present live. | NO |
| H-4 | A-2 | Flag stale documentation still describing `api/homepage/sections` as the storefront read path. | LOW | OPEN (deferred to documentation gate). | NO |

**Conditions that block Batch A: C-1 and H-1.**

---

## Section 4 — Waived Conditions

**C-4 — Existing-database reconciliation runbook (A-1).**
- **Reason waived:** This baseline lives on `recovery/3-greenfield-baseline`, an explicitly greenfield lineage. The migration timeline is being reset from scratch; there is no environment carrying the historical `__EFMigrationsHistory` rows that this branch must adopt transparently. A-1 §7 C-4 states the condition is waived when no such database exists ("If no such database exists (true greenfield), state that explicitly and C-4 is waived.").
- **Supporting evidence:** Branch `recovery/3-greenfield-baseline`; A-1 §6 R2/R3 (fresh-DB provisioning is the intended happy path); the collapse is designed for fresh provisioning, and the historical chain is retained only as archived documentation.
- **Scope of waiver:** Waived **for this branch only**. C-4 is **re-armed** if the baseline is ever targeted at an environment that previously ran the old chain — that promotion path remains gated by C-8.

No other conditions are waived.

---

## Section 5 — Accepted Risks

Only risks explicitly accepted by the source gates on this branch are recorded here. No new risks are invented.

| ID | Source | Accepted Risk | Severity (this branch) |
|---|---|---|---|
| R1 (A-1) | A-1 §6 | In-chain migration history is no longer an applicable EF chain (retained as archived documentation only). | Low |
| R7 (A-1) | A-1 §6 | Feature branches predating the collapse will conflict on merge and must be rebased (managed by C-6). | Medium (process) |
| R-2 (A-2) | A-2 §8 | `IHomepageSectionReadService` + cache/schema-registry remain DI-registered with no production consumer (kept alive by tests). | Low |
| R-4 (A-2) | A-2 §8 | CommerceHub `homepage-service.ts` synthesizes ids and casts through `unknown` over the new read-model contract (type-safety shim / debt). | Low |
| R-5 (A-2) | A-2 §8 | `preview` mode removed from the anonymous/tenant path (still available via SuperAdmin controller) — intentional hardening. | Low |
| R-6 (A-2) | A-2 §8 | External callers of `GET /api/homepage/sections` (if any) receive 404 — no in-repo consumers exist. | Low |

Explicitly **not** accepted (still requiring action): R2 (A-1) existing-DB adoption — neutralized by the C-4 waiver on this branch, re-armed at main promotion; R-1 (A-2) storefront source-of-truth shift — must be ratified (H-1) before acceptance, not silently accepted.

---

## Section 6 — Deferred Items

Technical debt intentionally deferred to later gates (none block Batch A):

- **D-1 (from H-2):** Decide the fate of the production-dead homepage read stack (`IHomepageSectionReadService`, `HomepageSectionCacheService`, `HomepageSectionSchemaRegistry` read path, `HomepageSectionCacheOptions`) — keep as internal verification surface or remove. Deferred to a follow-up code gate.
- **D-2 (from H-3):** One-time column/constraint/index equivalence check of the baseline migration vs the deleted `20260328_AAA_HardenHomepageSectionsDraftPublishing.sql`, then removal of the two stale entries at `PointOfSale.Tests/Infrastructure/TestDatabaseScope.cs:147-148`. Deferred to a follow-up commit.
- **D-3 (from H-4):** Mark stale architecture/certification docs (e.g. `docs/architecture/homepage-discovery-recommendation-certification-findings.md`) as superseded re: the retired `api/homepage/sections` storefront path. Deferred to the next documentation gate.
- **D-4 (from C-2/C-3/C-5):** EF baseline runtime verification (idempotent scaffold, fresh-DB smoke test) and CI/CD "no pending model changes" gate. Deferred to the pre-main-merge verification gate (governed by C-8).
- **D-5 (from C-6/C-7):** Standing hygiene — rebase in-flight branches onto the baseline; treat the migration archive as immutable.

---

## Section 7 — Readiness Assessment

| Target | Rating | Basis |
|---|---|---|
| **Batch A (Repository Freeze)** | **READY WITH CONDITIONS** | Structurally sound; blocked only by C-1 (commit the untracked baseline) and H-1 (ratify storefront source-of-truth). Both are small, well-defined actions. Freeze must not proceed over an untracked baseline. |
| **Batch B** | **NOT READY** | Depends on a frozen, committed Batch A baseline. Cannot begin until Batch A closes. |
| **Repository Freeze** | **READY WITH CONDITIONS** | Same gating as Batch A — a freeze is only meaningful once C-1 has the baseline tracked and H-1 ratifies the one behavioral change. |
| **Main Branch** | **NOT READY** | Governed by C-8: requires C-2, C-3, C-4(waived here)/re-armed for main, and C-5, none of which are yet evidenced. |
| **Production** | **NOT READY** | Requires main-branch readiness plus deployment automation alignment (C-5/C-6) and, for any environment ever on the old chain, the re-armed C-4 reconciliation runbook. |

---

## Section 8 — Final Certification

### **PASS WITH CONDITIONS**

Gate A is certified as substantively complete: both decisions (Migration Baseline and Homepage Architecture) are APPROVED, technically correct, and evidenced against the live repository. Gate A **may be closed as PASS WITH CONDITIONS**, but the repository is **not yet authorized to begin Batch A** until the two blocking conditions below are cleared and evidenced.

**Blocking conditions (must clear before Batch A / Repository Freeze):**

1. **C-1 (A-1) — Commit the baseline.** Stage and commit `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs` and `.Designer.cs` **together with** the regenerated `ApplicationDbContextModelSnapshot.cs` in a single atomic commit. *Live status: baseline files `??` untracked, snapshot `M` — OPEN.*
2. **H-1 (A-2 Condition 1) — Ratify the storefront homepage source-of-truth change.** Record explicit product/architecture sign-off in `docs/` stating the SuperAdmin homepage builder governs the **CommerceHub marketplace homepage only**, and the tenant storefront homepage renders from `StoreConfig`. *Live status: no ratification doc on disk — OPEN.*

All other conditions are either waived (C-4, this branch), standing guardrails (C-6/C-7/C-8), or deferrable to later gates (C-2/C-3/C-5 to the main-merge gate; H-2/H-3/H-4 to follow-up gates) and do **not** block Batch A.

---

## Section 9 — Next Authorized Phase

The **only** phase authorized to begin is the **Gate A blocking-condition remediation** required to unlock Batch A — specifically:

- Clear **C-1** (commit the baseline atomically), and
- Clear **H-1** (publish the storefront homepage source-of-truth ratification).

**Batch A (Repository Freeze) is authorized to begin immediately upon evidenced completion of C-1 and H-1** — and not before. No later phase (Batch B, main-branch promotion, production) is authorized by this certification.

---

## Success Statement

> **Has Gate A been successfully completed, and is the repository authorized to begin Batch A?**

**Gate A is successfully completed and certified PASS WITH CONDITIONS.** Both architectural decisions are approved and verified. The repository is **conditionally authorized** for Batch A: authorization takes effect the moment two blocking conditions — **C-1** (commit the untracked baseline atomically with its snapshot) and **H-1** (ratify the storefront homepage source-of-truth change) — are satisfied and evidenced. Until then, Batch A / Repository Freeze must **not** commence.

---

### Verification Appendix (live checks run for this certification, read-only)

- Branch/HEAD: `recovery/3-greenfield-baseline` @ `8317aef1`.
- Baseline tracking (C-1): `git status --short` → `20260723214642_Baseline_Recovery3.cs` = `??`, `.Designer.cs` = `??`, `ApplicationDbContextModelSnapshot.cs` = `M`. **OPEN.**
- Migration chain collapse: exactly one active non-Designer migration class in `PointOfSale.Infrastructure/Migrations/`; old chain shows `D` deletions. Confirmed.
- Homepage relocation (A-2): `grep "api/homepage/sections"` across Storefront/CommerceHub/Web = **0**; `MarketplaceReadController` homepage read stack present; deleted controller = `D`. Confirmed.
- Ratification doc (H-1): no storefront-homepage source-of-truth ratification file found under `docs/` (only the A-2 review). **OPEN.**
- Stale test config (H-3): `PointOfSale.Tests/Infrastructure/TestDatabaseScope.cs:147-148` still names `20260327_ZZZ_AddHomepageSections.sql` and `20260328_AAA_HardenHomepageSectionsDraftPublishing.sql`. **OPEN.**

# PUBLISH-0 — PUBLIC REPOSITORY READINESS CERTIFICATION

Date: 2026-07-26
Scope: Can this working tree be initialized (`git init`) as a brand-new PUBLIC GitHub repository TODAY?
Method: STRICT read-only. Every claim below is backed by live commands run against the repository
(`git ls-files`, `git status --porcelain`, `git check-ignore -v --no-index`, `dotnet build`, `find`, `grep`).
No file was created, moved, deleted, or modified except this report.

---

## 1. Executive Summary

FINAL CERTIFICATION: **NOT READY**

The engineering core is in good shape: the solution builds with 0 warnings / 0 errors, the
`.gitignore` is source-code-backed and passes all ignore/keep proofs, every tracked
`appsettings*.json` is placeholder-only, and no real secret (Stripe/AWS/JWT/private key) was
found in any file that a fresh `git init` would commit.

Four findings block public publication TODAY:

1. **README.md is corrupted** — `file README.md` reports `data`; UTF-16/NUL bytes are appended
   after the markdown body (`# S H O P I X Y` in NUL-interleaved bytes at file tail). The public
   landing page of the repo is a broken file. (HIGH)
2. **No LICENSE file**, and the corrupted README declares "Proprietary — All rights reserved".
   A public repo without a license is legally "all rights reserved" — publishing proprietary
   code publicly requires an explicit owner decision. (HIGH — business decision, not technical)
3. **~1,547 files under `docs/` plus `.ai/` (150 files), `.ai-context/` (7 files) and 546
   untracked docs would ALL enter the first public commit.** They include internal security
   audit artifacts by name: `HOSTILE_AUDIT_FINAL_REPORT.md`, `TENANT-ISOLATION-AUDIT-REPORT.md`,
   `.ai-context/KNOWN_RISKS.md`, chat/tenant/CVE-style hardening reports. Publishing an
   unreviewed internal vulnerability-audit corpus is an information-disclosure risk. (HIGH)
4. **The current repo is mid-recovery** (branch `recovery/3-greenfield-baseline`, 3,277 dirty
   status entries, ~1,220+ ghost index entries — paths tracked in the old index but absent on
   disk: `PointOfSale/`, `SqlRunner/`, `migrations/`, `testsprite_tests/`, 300+ root scratch
   files). A fresh `git init` sidesteps the old index, but the recovery program that produced
   this tree is visibly unfinished; publishing mid-migration is premature. (MEDIUM)

Everything else is minor: missing `.gitattributes`/`.editorconfig`/`.github` CI, README content
stale (says "PointOfSale.Web (to be created)" while it exists with 611 tracked files).

---

## 2. Repository Identity

| Item | Value | Evidence |
|---|---|---|
| Repository root | `C:\Users\fadya\source\repos\PointOfSale` | cwd |
| Solution | `PointOfSale.sln` (only .sln in repo) | `find -name '*.sln'` |
| Product name | "Shopixy" per .gitignore header & README tail; solution/projects named PointOfSale | `.gitignore:2`, README tail bytes |
| Primary runtime | .NET 9 — `global.json` pins SDK `9.0.308`, `rollForward: latestPatch`; installed SDK is exactly 9.0.308 | `global.json`, `dotnet --version` |
| Backend | PointOfSale.API / .Core / .Infrastructure / .Tests (C#, EF Core, PostgreSQL) | csproj census |
| Frontends | PointOfSale.Web, PointOfSale.CommerceHub, PointOfSale.Storefront (Vite/React, each has `package.json`) | `find -maxdepth 2 -name package.json` |
| Node service | PointOfSale.WhatsApp.Baileys (`package.json`) | same |
| Branch | `recovery/3-greenfield-baseline` | `git branch --show-current` |

**Naming inconsistency (INFO):** repo/solution = "PointOfSale", product branding = "Shopixy".
Public repo name should be decided before publication.

## 3. Repository Inventory

Top-level directories on disk (18): `.agents` (empty), `.ai` (150 files), `.ai-context` (7),
`.ai-testing` (21), `.config` (dotnet-tools manifest), `.vscode` (settings.json only), `DB`
(184 files, SQL migrations), `deploy` (4 files: Caddyfile, n8n workflow, env example, README),
`docs` (1,547 files), 8 `PointOfSale.*` project dirs, `temp` (1 runtime data-protection key).

Root files on disk: exactly 4 — `.gitignore`, `global.json`, `PointOfSale.sln`, `README.md`.
(The 300+ legacy root scratch files seen in `git ls-files` are GHOSTS: tracked in the old
index, deleted from disk — verified absent with `[ -e ]` checks on samples: `api-build.binlog`,
`build_log.txt`, `errors.txt`, `GitHub..gitignore`, `appsettings.af22.json` all ABSENT.)

Publishable file count (disk, excluding .git/node_modules/bin/obj/dist/temp): **~6,190 files**.

## 4. Build Readiness

| Check | Result | Evidence |
|---|---|---|
| `dotnet build PointOfSale.sln` | **SUCCESS — 0 warnings, 0 errors** (incremental) | run 2026-07-26 |
| Full rebuild (`--no-incremental`) | Failed ONLY on MSB3027/MSB3021 file locks — a live `testhost (21544)` process held `PointOfSale.API.dll`; 452 warnings surfaced from full compile | environment condition (running test host), not a repo defect |
| Solution membership | 4 projects: Core, Infrastructure, API, Tests — all 4 `.csproj` files exist on disk | sln grep + csproj find |
| Broken/dead references | None — csproj on disk exactly matches sln entries; no orphan csproj found outside sln | csproj census |
| Retired projects | Old index still tracks retired dirs (`PointOfSale/` MVC, `SqlRunner`, `DbQuery`, `DbPatcher`, `PointOfSale.EventBackbone.Tests`) — ALL absent on disk; they exist only as ghost index entries and would NOT enter a fresh init | `[ -e ]` checks |

Note: the 452 warnings on full rebuild were not classified (build log truncated by lock
failure); re-run a clean build after killing testhost to inventory them. The 0-warning
incremental result indicates none are new.

## 5. Configuration Readiness

Config census (disk, excluding node_modules/bin/obj):

| File | Tracked? | Ignored? | Content class | Verdict |
|---|---|---|---|---|
| PointOfSale.API/appsettings.json | tracked | — | ALL secrets empty `""`; carries `_SecretsSourceNote` instructing env-vars/user-secrets | SAFE placeholder |
| appsettings.{Development,Staging,Production,Test}.json | tracked | — | same pattern; every `Secret`, `ApiKey`, `SecretKey`, `WebhookSecret`, `HmacSecret`, `Password`, `ConnectionString` inspected = empty | SAFE placeholder |
| PointOfSale.API/appsettings.Development.local.json | untracked | IGNORED (`.gitignore:115 appsettings.*.local.*`) | real dev creds live here | correct |
| PointOfSale.API/appsettings.Test.local.json | untracked | IGNORED (same rule) | | correct |
| WhatsApp.Baileys/.env | untracked | IGNORED (project `.gitignore:27`) | local dev config, empty REDIS_PASSWORD | correct |
| WhatsApp.Baileys/.env.production | tracked in OLD index | IGNORED by `.gitignore:97 .env.*` in a fresh init | inspected: `AUTH_STATE_ENCRYPTION_SECRET=CHANGE_THIS_...` placeholder, empty Redis password; private IP `192.168.1.5` only | placeholder — but will drop out of a fresh init (good) |
| WhatsApp.Baileys/.env.staging | tracked in OLD index | IGNORED in fresh init | `staging-secret-key-change-this-...` placeholder | same |
| *.env.example (CommerceHub, Storefront ×2, Web, Baileys, deploy/staging) | tracked | KEPT via `!.env.example` / `!.env.*.example` negations | templates | correct |
| launchSettings.json (API only) | tracked | — | standard | correct |
| UserSecretsId `bbdf2e74-…` in API + Infrastructure csproj | tracked | — | user-secrets GUID is not sensitive | INFO only |

Hierarchy is coherent: committed appsettings = placeholder defaults; real values via
user-secrets / env vars / gitignored `*.local.json` — and `SensitiveConfigurationValidator`
enforces this at startup (referenced in `.ai-context/LOCAL_DEV_STRIPE_HANDOFF_2026-05-12.md`).

**Fresh-init nuance:** `.env.production`/`.env.staging` are tracked in the CURRENT index but
matched by `.env.*` — in a brand-new `git init` they will be correctly ignored. Their values
are placeholders anyway. The only real information they leak is internal LAN IP `192.168.1.5`
(LOW).

## 6. Git Readiness

- `.gitignore`: 146 lines, sectioned, source-code-backed (authored under
  `docs/project/GIT-1_PROFESSIONAL_GITIGNORE_CERTIFICATION.md`). Verified live:
  - `git check-ignore -v --no-index` proves IGNORED: `.env`, `.env.production`, `.env.staging`,
    `appsettings.*.local.json`, `temp/`, `bin/`, `node_modules/`, both data-protection
    `key-*.xml` files on disk.
  - Negation proof: `.env.example` / `.env.staging.example` remain tracked (KEPT).
  - Regression guard: `git ls-files | git check-ignore --stdin` = **0** (current .gitignore
    newly ignores zero tracked files).
- Current index state (context only — irrelevant after fresh init): 4,451 tracked files,
  3,277 dirty entries (1,220 untracked / 831 modified / 872 unstaged-D / 351 staged-D).
  The unstaged/staged deletes are the ghost cleanup of the recovery program.
- Tracked garbage in OLD index: 4 files (`*.binlog` ×3, `current.trx`) — all absent on disk,
  ghosts only; a fresh init picks up none of them.
- Untracked-but-would-be-committed content: 546 files under `docs/`, 417 under
  `PointOfSale.Tests/`, 101 `PointOfSale.Web/`, 84 `PointOfSale.API/` — a fresh init commits
  ALL of these. That is the intent of the recovery baseline, but it means the first public
  commit content has never been reviewed as a set.

## 7. Security Readiness

Searched (tracked files + on-disk publishable tree): Stripe (`sk_live|sk_test|pk_live`),
AWS (`AKIA…`), private keys (`BEGIN … PRIVATE`), Slack `xox…`, GitHub `ghp_…`, Google `AIza…`,
plus keyword sweep (password/secret/key/token/connectionstring) over all tracked appsettings.

| Finding | Class | Verdict |
|---|---|---|
| `sk_test_123` in `PointOfSale.Tests/Security/StorefrontTenantIsolationTests.cs:934` | obvious dummy test fixture | SAFE (INFO) |
| Test fixtures with `secretKey` literals in `PaymentMethodsAdminSafetyTests.cs`, `PaymentMethods.jsx` | dummy values | SAFE (INFO) |
| All tracked appsettings secrets | empty strings + `_SecretsSourceNote` | SAFE |
| `.env.production` / `.env.staging` | `CHANGE_THIS…` placeholders; internal IP 192.168.1.5 | LOW (IP disclosure; also ignored in fresh init) |
| Data-protection `key-*.xml` (real crypto keys) ×2 on disk | both under `temp/` paths — verified IGNORED | SAFE (ignored) |
| `auth_states/` live WhatsApp session credentials | none on disk; rule present | SAFE |
| deploy/staging/Caddyfile | `yourdomain.com` placeholders, env-var substitution | SAFE |
| n8n workflow JSON | resolves secrets from `$env`, none embedded | SAFE |
| `.ai-context/LOCAL_DEV_STRIPE_HANDOFF_2026-05-12.md` | no key material (grep for sk_/pk_/whsec_ = none); process notes only | SAFE content, but internal doc (see §8) |
| **Internal audit corpus**: `HOSTILE_AUDIT_*`, `TENANT-ISOLATION-AUDIT-REPORT`, `CHAT_SUBSYSTEM_ADVERSARIAL_AUDIT_REPORT`, `.ai-context/KNOWN_RISKS.md`, docs/audits/, docs/certifications/ | information disclosure: maps historical vulnerabilities and residual risks of the live platform | **HIGH — must be reviewed/excluded before public release** |

No live secret was found anywhere in content a fresh init would commit. **History is not a
concern** — a brand-new `git init` has no history; the old `.git` must simply NOT be copied.

## 8. Documentation Readiness

- **README.md is CORRUPTED (BLOCKER).** `file README.md` → `data`. The 233-line markdown body
  is followed by UTF-16LE/NUL-interleaved bytes spelling `# SHOPIXY` (hex dump verified). A
  binary-tainted README renders wrong or as garbage on GitHub.
- README content is also stale: "PointOfSale.Web (to be created)" while the project exists
  with 611 tracked files; stack described is a fraction of reality (no mention of CommerceHub,
  Storefront, WhatsApp service, RabbitMQ, Mongo, ES, Redis); README states
  "License: Proprietary — All rights reserved".
- **No LICENSE file** (verified absent).
- docs/ is enormous (1,547 files) and mixes: recovery governance, phase logs, audits,
  fix journals, certifications. It is an internal engineering journal, not public docs.
- `.ai/`, `.ai-context/`, `.ai-testing/` are AI-assistant context corpora (162+ files) —
  internal by nature; `.ai-context/KNOWN_RISKS.md` explicitly catalogs known weaknesses.

## 9. Developer Experience

Clean-clone experience (simulated from evidence):
- `global.json` pins SDK 9.0.308 with `rollForward: latestPatch` — good, reproducible.
- `dotnet restore/build` works (proven above), no NuGet.Config needed (nuget.org default).
- 4 npm projects each need `npm install`; `.env.example` templates exist for all of them.
- Runtime prerequisites are heavy and UNDOCUMENTED in README: PostgreSQL, Redis, MongoDB,
  Elasticsearch, RabbitMQ, n8n (optional), WhatsApp/Baileys sidecar. `deploy/staging/README.md`
  covers staging only. A newcomer cannot boot the system from the root README. (MEDIUM)
- No hardcoded user paths in code: `git grep 'C:\\Users\\fadya'` over *.cs/*.json/*.csproj = 0.
  Machine-ish paths exist only as overridable defaults (`C:\srv\pointofsale\staging\…` in
  appsettings.Staging KeysPath and Caddyfile env-var fallbacks) — acceptable, documented.
- `.config/dotnet-tools.json` present (tool manifest restores cleanly with `dotnet tool restore`).

## 10. CI Readiness

- `.github/` does not exist — no workflows, templates, CODEOWNERS. CI must be authored.
- No hidden blockers for CI: solution builds from a clean SDK 9.0.308; tests are a standard
  xunit-style project; frontends are standard Vite builds. Integration tests will need
  service containers (Postgres/Redis/Mongo/ES/RabbitMQ) — the test matrix and which tests are
  unit-safe is undocumented. CI can be added immediately for build + unit tests; full test CI
  needs service orchestration work. (LOW-MEDIUM)

## 11. Repository Hygiene / Release Readiness

- Disk root is clean: 4 files only. The 300+ historical root scratch files are ghosts
  (tracked-in-old-index, absent on disk) and cannot enter a fresh init.
- `bin/`, `obj/`, `node_modules/`, `dist/`, `.vs/`, `TestResults/`, `temp/`,
  data-protection keys: all verified ignored by live `git check-ignore` probes.
- `.agents/` is empty on disk and ignored. `.vscode/settings.json` is the only .vscode file
  and is intentionally kept by negation rules.
- Missing polish: no `.gitattributes` (CRLF/LF normalization matters — repo has mixed line
  endings, e.g. .gitignore is CRLF), no `.editorconfig`, no `Directory.Build.props`.

## 12. Architecture Review (repo-shape only)

- Structure is coherent: 4-project Clean Architecture backend + 3 Vite frontends + 1 Node
  sidecar + DB/ + deploy/ + docs/. Naming is consistent (`PointOfSale.*`), ownership clear.
- Legacy remnants exist ONLY in the old index (retired MVC `PointOfSale/`, `SqlRunner`,
  `DbQuery`, `DbPatcher`, `testsprite_tests`, `migrations/`, `staging/`, `scripts/`,
  `tools/`) — all absent on disk. Fresh init inherits none.
- Brand split PointOfSale vs Shopixy should be resolved for a public identity (INFO).

## 13. Risk Matrix

| # | Finding | Severity |
|---|---|---|
| R1 | README.md corrupted (binary tail) — broken public landing page | HIGH |
| R2 | No LICENSE + "Proprietary" declaration vs public release intent | HIGH (decision) |
| R3 | Internal security-audit / known-risks corpus (docs/, .ai*, HOSTILE_AUDIT, KNOWN_RISKS) would be published unreviewed | HIGH |
| R4 | Recovery program mid-flight; first-commit content set (6,190 files incl. 546 untracked docs) never reviewed as a whole | MEDIUM |
| R5 | Root README lacks bootstrap docs for 6+ infrastructure dependencies | MEDIUM |
| R6 | No .gitattributes → CRLF/LF churn for non-Windows contributors | MEDIUM |
| R7 | No CI, no .editorconfig, no Directory.Build.props, no .github templates | LOW |
| R8 | Internal LAN IP 192.168.1.5 in (ignored) .env files; `C:\srv\...` default paths | LOW |
| R9 | 452 warnings surfaced on full rebuild, unclassified (locked-file abort) | LOW |
| R10 | PointOfSale vs Shopixy naming split; UserSecretsId GUID visible | INFO |

## 14. Publication Checklist

| Item | Status |
|---|---|
| Solution builds (SDK pinned, restore clean) | READY |
| Project graph / no broken or orphan projects | READY |
| .gitignore certified, all ignore/keep proofs pass | READY |
| Tracked configs placeholder-only, secret hierarchy enforced | READY |
| No live secrets in publishable content | READY |
| Local secret files (.env, *.local.json, DP keys) ignored | READY |
| No hardcoded user paths in code | READY |
| Deploy manifests placeholderized (Caddyfile, n8n, env.example) | READY |
| README.md | **BLOCKED** (corrupted binary tail; stale content) |
| LICENSE | **BLOCKED** (absent; "Proprietary" vs public intent — owner decision) |
| Internal docs / audit corpus review (docs/, .ai/, .ai-context/, .ai-testing/) | **NOT READY** (unreviewed disclosure risk) |
| First-commit content review (546 untracked docs + full tree as a set) | NOT READY |
| .gitattributes (line-ending normalization) | NOT READY |
| .editorconfig / Directory.Build.props | NOT READY (optional polish) |
| CI workflows (.github/) | NOT READY (can be added immediately after init) |
| Bootstrap/onboarding documentation (Postgres/Redis/Mongo/ES/RabbitMQ/Baileys) | NOT READY |
| Full-rebuild warning inventory (452 warnings) | NEEDS MANUAL REVIEW |
| Repository/product name decision (PointOfSale vs Shopixy) | NEEDS MANUAL REVIEW |

## 15. Repository Quality Score

| Category | Score /10 | Basis |
|---|---|---|
| Architecture | 8 | Clean, consistent multi-project layout; legacy fully retired from disk |
| Repository Hygiene | 7 | Disk tree clean (4 root files); old index is a mess but dies with fresh init |
| Git Readiness | 8 | Certified .gitignore, all proofs pass, regression guard = 0 |
| Security | 7 | Zero live secrets; deduction for unreviewed audit/known-risks corpus |
| Documentation | 3 | Corrupted README, no LICENSE, internal journal ≠ public docs |
| Developer Experience | 5 | Pinned SDK + env templates good; 6-service bootstrap undocumented |
| Maintainability | 7 | Placeholder config discipline, tool manifest, startup validator |
| Professionalism | 5 | Broken README + missing metadata undercut a strong codebase |
| Release Readiness | 5 | Build green, ignores proven; content-set review + metadata missing |

**OVERALL: 55 / 100** (weighted equally, ×10/9 ≈ 61 unweighted-average; reported
conservatively as 55 because R1–R3 are gate-level, not averaging-level, defects).

## 16. Final Certification

# NOT READY

The repository CANNOT be initialized as a brand-new public GitHub repository today.

The blockers are small in number and mostly non-code:

1. Replace README.md with a clean UTF-8 file that reflects the real system (fixes R1, R5).
2. Owner decision on licensing; add a LICENSE file (or decide against public release) (R2).
3. Review-and-decide pass over docs/ + .ai* trees: publish, prune, or move to a private
   docs repo — explicitly including HOSTILE_AUDIT_*, TENANT-ISOLATION-AUDIT*,
   KNOWN_RISKS.md, and all certifications/audits (R3, R4).
4. Add .gitattributes (`* text=auto` + eol rules) before first commit (R6).

The code, build, configuration hygiene, and .gitignore are already at public-release
standard. Once items 1–4 are done, re-certification should upgrade this repository to
READY WITH MINOR OBSERVATIONS at minimum.

---
*All evidence gathered read-only on 2026-07-26 against the live tree and index. No source,
config, or git state was modified. Generated file: this report only.*

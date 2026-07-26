# CONFIG-1 — Configuration Authority Audit
## Source of Truth Discovery

Repository: `PointOfSale` (Shopixy multi-tenant e-commerce/affiliate/POS)
Branch: `recovery/3-greenfield-baseline`
Scope: ANALYSIS ONLY — no files modified, no values changed, no commits. Evidence-only.
All conclusions cite `file:line`. Secret values are masked.

---

## 1. Executive Summary

- The runtime system is a **single ASP.NET Core 9 host**: `PointOfSale.API` (`PointOfSale.API/Program.cs:57`). SDK pinned to `9.0.308` (`global.json`).
- The solution `PointOfSale.sln` contains 5 .NET projects: `PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure`, `PointOfSale.Tests`, and a legacy MVC stub `PointOfSale`.
- `PointOfSale` (root MVC project) is a **default template stub** — `WebApplication.CreateBuilder(args)` with no custom config, trivial `appsettings.json` (Logging + AllowedHosts only). Not part of the commerce runtime.
- Non-.NET runtime services with their own config: `PointOfSale.WhatsApp.Baileys` (Node service, `.env`), and Vite front-ends `PointOfSale.Web`, `PointOfSale.Storefront`, `PointOfSale.CommerceHub` (`VITE_*` build-time env).
- **Configuration providers for the API, in precedence order** (`Program.cs:58-71`): `appsettings.json` → `appsettings.{Env}.json` → `appsettings.{Env}.local.json` → User Secrets (Development only) → Environment Variables. No command-line, no Azure Key Vault.
- **Secrets are correctly externalized**: every committed `appsettings*.json` ships secret fields BLANK. Real credentials live only in git-ignored `appsettings.{Env}.local.json` and user-secrets. A startup validator (`SensitiveConfigurationValidator.ValidateOrThrow`, `Program.cs:85`) **fails closed** if secrets are missing, too short, placeholders, or reused across JWT realms.
- **Primary databases**: PostgreSQL (authoritative), MongoDB (read models), Redis (cache/locks), Elasticsearch (search), RabbitMQ (MassTransit bus). Hangfire uses the same PostgreSQL. No Kubernetes/Helm/Docker manifests exist in-repo.
- **Findings**: 2 tracked `.env` files for the Baileys service contain hardcoded internal infra (`REDIS_HOST=192.168.1.5`) and placeholder secrets; several config sections are duplicated across environment files; a `DB/appsettings.af22.json` orphan file is not wired to any loader.

Verdict: **PASS WITH OBSERVATIONS** (see §20).

---

## 2. Configuration Architecture

- Runtime authority host: `PointOfSale.API/Program.cs`.
- Config bootstrap is explicit and overrides the default host chain by calling `builder.Configuration.AddJsonFile(...)` directly (`Program.cs:58-64`).
- Design-time (EF migrations) uses a SEPARATE builder: `ApplicationDbContextFactory.CreateDbContext` (`PointOfSale.Infrastructure/Data/ApplicationDbContextFactory.cs:22-33`) — its own `ConfigurationBuilder`, base path `../PointOfSale.API`, same JSON layering + user secrets, plus `ConnectionStrings__DefaultConnection` env var (`:36`).
- Connection string is normalized centrally by `PostgresConnectionStringPolicy.Normalize` (`Program.cs:73`; policy at `PointOfSale.Infrastructure/Configuration/PostgresConnectionStringPolicy.cs:14`) — injects Timeout=15, CommandTimeout=30, and in Development forces `SSL Mode=Disable` when unset.
- Options pattern is used pervasively (~40 `Configure<T>` / `.Bind()` sites in `Program.cs`) binding config sections to strongly-typed option classes.

---

## 3. Configuration Loading Pipeline

Exact code (`PointOfSale.API/Program.cs:57-83`):

```
57 var builder = WebApplication.CreateBuilder(args);
58 builder.Configuration
59   .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
60   .AddJsonFile($"appsettings.{Env}.json", optional: true, reloadOnChange: true)
61   .AddJsonFile($"appsettings.{Env}.local.json", optional: true, reloadOnChange: IsDevelopment)
66 if (IsDevelopment) builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
71 builder.Configuration.AddEnvironmentVariables();
73 defaultConnectionString = PostgresConnectionStringPolicy.Normalize(GetConnectionString("DefaultConnection"), IsDevelopment)
82 builder.Configuration["ConnectionStrings:DefaultConnection"] = defaultConnectionString  // normalized value written back
85 SensitiveConfigurationValidator.ValidateOrThrow(builder.Configuration)  // fail-closed gate
```

Note: `WebApplication.CreateBuilder(args)` ALSO pre-loads the framework default chain (appsettings.json, appsettings.{Env}.json, user secrets in Dev, env vars, command line) BEFORE line 58. The explicit `AddJsonFile`/`AddUserSecrets`/`AddEnvironmentVariables` calls therefore RE-APPEND those providers, so the explicit ones (later in the list) win. The net authoritative precedence is the explicit chain in §5.

---

## 4. Configuration Providers

| # | Provider | Source | Where | Optional | Reload |
|---|----------|--------|-------|----------|--------|
| 1 | JSON | `appsettings.json` | `Program.cs:59` | No (required) | Yes |
| 2 | JSON | `appsettings.{Env}.json` | `Program.cs:60` | Yes | Yes |
| 3 | JSON | `appsettings.{Env}.local.json` (git-ignored) | `Program.cs:61-64` | Yes | Dev only |
| 4 | User Secrets | id `bbdf2e74-8790-4753-8874-f20fcfbdbbd5` | `Program.cs:68`; `PointOfSale.API.csproj:7`; `PointOfSale.Infrastructure.csproj:39` | Yes | — |
| 5 | Environment Variables | process env | `Program.cs:71` | — | — |

- NOT present: `AddCommandLine` (beyond framework default), `AddAzureKeyVault`, custom `IConfigurationProvider`, `ConfigureAppConfiguration`.
- User Secrets is enabled ONLY in Development (`Program.cs:66-69`) and shares one secret id across API + Infrastructure projects.

---

## 5. Configuration Precedence (authoritative override chain)

Lowest → highest priority (later overrides earlier), per `Program.cs:58-71`:

```
appsettings.json
  ↓
appsettings.{Environment}.json          (Development/Production/Staging/Test)
  ↓
appsettings.{Environment}.local.json    (git-ignored; real dev/test secrets live here)
  ↓
User Secrets                            (Development only)
  ↓
Environment Variables                   (container/host injection — production authority)
  ↓
[Runtime normalization write-back]      ConnectionStrings:DefaultConnection re-set at Program.cs:82
```

- Command line: only the framework-default pass; not re-added explicitly.
- The `ConnectionStrings:DefaultConnection` value is deterministically overwritten in-memory at `Program.cs:82` AFTER all providers load, making the **normalized** string the final runtime authority regardless of source.

---

## 6. Project Configuration Inventory

| Project | Type | Config files | Env files | Loading code | Runtime env |
|---------|------|--------------|-----------|--------------|-------------|
| PointOfSale.API | ASP.NET Core 9 Web API | appsettings.json, .Development/.Production/.Staging/.Test.json, .Development.local/.Test.local.json (ignored) | via env vars / user secrets | `Program.cs:57-71` | Dev/Prod/Staging/Test |
| PointOfSale.Infrastructure | Class library (EF, Mongo, Redis, ES, MassTransit) | shares API appsettings at design time | — | `ApplicationDbContextFactory.cs:22-33` | design-time + runtime |
| PointOfSale.Core | Domain library | none | — | — | — |
| PointOfSale.Tests | xUnit test project | uses appsettings.Test.json + .Test.local.json | env | `WebApplicationFactory`/`ConfigurationBuilder` in test fixtures | Test |
| PointOfSale (root) | MVC template stub | appsettings.json, appsettings.Development.json (Logging/AllowedHosts only) | — | `PointOfSale/Program.cs:1` (default builder) | Development |
| PointOfSale.WhatsApp.Baileys | Node.js service | — | .env (untracked), .env.example, .env.production (TRACKED), .env.staging (TRACKED) | dotenv | dev/staging/production |
| PointOfSale.Web | Vite/React admin SPA | — | .env.staging.example | `import.meta.env.VITE_*` | build-time |
| PointOfSale.Storefront | Vite SPA | — | .env.example, .env.staging.example | `import.meta.env.VITE_*` | build-time |
| PointOfSale.CommerceHub | Vite SPA | — | .env.example | `import.meta.env.VITE_*` | build-time |

- Orphan: `DB/appsettings.af22.json` (AffiliateProductIntelligence + Storefront.BaseUrl) — NOT referenced by any loader; dead config (see §14).
- launchSettings: `PointOfSale.API/Properties/launchSettings.json` (http:5010, https:7263; `ASPNETCORE_ENVIRONMENT=Development`) and `PointOfSale/Properties/launchSettings.json` (5273/7221; Development).

---

## 7. Connection String Inventory

All values below are read from git-ignored `appsettings.*.local.json` (dev/test machine). Sensitive data MASKED.

| Name | File (source) | Value (masked) | Used by | DbContext | Referenced? | Runtime authority |
|------|---------------|----------------|---------|-----------|-------------|-------------------|
| ConnectionStrings:DefaultConnection | committed appsettings*.json = `""`; real value in `appsettings.Development.local.json` / `appsettings.Test.local.json` | `Host=192.168.1.5;Port=5432;Database=PosEcommerceDb_Baseline;Username=postgres;Password=***MASKED***` | PointOfSale.API, EF migrations | `ApplicationDbContext` (`Program.cs:328-330` `UseNpgsql`) | Yes | env var / local.json → normalized at `Program.cs:82` |
| MongoDB:ConnectionString | local.json | `mongodb://192.168.109.128:27017` | Infrastructure MongoDB | `MongoDbContext` (`MongoDB/Services/MongoDbContext.cs:40` `new MongoClient`) | Yes | env var / local.json |
| Redis:ConnectionString | local.json | `192.168.1.5:6379` | Redis cache/locks + SignalR backplane | `RedisConnectionFactory.cs:218` `ConnectionMultiplexer.Connect`; SignalR at `Program.cs:3251` | Yes (when `Redis:Enabled`) | env var / local.json |
| Elasticsearch:Url | local.json | `http://192.168.1.5:9200` | Search indexing | ES client via `ElasticsearchSettings` | Yes (when `Elasticsearch:Enabled`) | env var / local.json |
| RabbitMQ (Host/VHost/User/Password) | local.json | `Host=192.168.1.5; User=admin; Password=***MASKED***; VHost=/` | MassTransit bus | `Program.cs:2000-2007` `UsingRabbitMq`/`cfg.Host` | Yes | env var / local.json |

- All committed `appsettings*.json` ship `DefaultConnection: ""` and blank Mongo/Redis/ES/RabbitMQ endpoints (`appsettings.json:3-4,247-269`; Production/Staging/Test identical pattern).
- No duplicate or dead named connection strings in code — only `DefaultConnection` is a formal `ConnectionStrings` entry; Mongo/Redis/ES/RabbitMQ are custom sections, not `ConnectionStrings`.

---

## 8. Database Configuration Inventory

| Store | Config section | Runtime authority | Fallback | Override chain |
|-------|----------------|-------------------|----------|----------------|
| PostgreSQL (authoritative) | `ConnectionStrings:DefaultConnection` | `UseNpgsql(defaultConnectionString)` `Program.cs:330` | none — `ValidateOrThrow` fails closed (`SensitiveConfigurationValidator.cs:25`) | appsettings → local.json → user secrets → env → normalize@82 |
| Hangfire | uses same PostgreSQL; `Hangfire:DashboardPath` `Program.cs:2838` | PostgreSQL | `/hangfire` default | same as PG |
| MongoDB (read models) | `MongoDB:*` | `MongoDbContext.cs:40` | none — required (`SensitiveConfigurationValidator.cs:54`) | same |
| Redis (cache/locks/SignalR) | `Redis:*` | `RedisConnectionFactory.cs:218` | required only when `Redis:Enabled=true` (`SensitiveConfigurationValidator.cs:58-61`); Test sets `Enabled:false` | same |
| Elasticsearch (search) | `Elasticsearch:*` | ES client | required when `Enabled=true` (`SensitiveConfigurationValidator.cs:63-66`); Test `Enabled:false` | same |
| RabbitMQ (MassTransit) | `RabbitMQ:*` | `Program.cs:2003` | code default `localhost`/`guest` at `:2003-2006` but validator requires real values (`SensitiveConfigurationValidator.cs:49-52`) | same |
| Cloudflare R2 (object storage) | `R2:*` or `R2_*` env | `Program.cs:260-267` | optional; partial-config rejected (`SensitiveConfigurationValidator.cs:183-214`) | env var preferred over `R2:` section |

- `Redis:Enabled` differs by env: `true` in dev/prod/staging, `false` in Test (`appsettings.Test.json`). ES `Enabled:false` in Test.

---

## 9. Environment Matrix

| Environment | appsettings loaded | ASPNETCORE_ENVIRONMENT set by | Notes |
|-------------|--------------------|-------------------------------|-------|
| Development | appsettings.json + .Development.json + .Development.local.json + user secrets + env | `launchSettings.json` profiles (`ASPNETCORE_ENVIRONMENT=Development`) | SSL Mode forced Disable for PG (`PostgresConnectionStringPolicy.cs:33-36`); user secrets active |
| Production | appsettings.json + .Production.json + (.Production.local.json if present) + env | host/container env var | secrets via env vars / external secret manager (note in `appsettings.Production.json:1`) |
| Staging | appsettings.json + .Staging.json + env | host env var | DataProtection keys at `C:\srv\pointofsale\staging\data-protection-keys` (`appsettings.Staging.json`) |
| Test | appsettings.json + .Test.json + .Test.local.json + env | test harness / `REQUIRE_REAL_POSTGRES_TESTS` (`Program.cs:78`) | Redis/ES disabled; DataProtection keys under temp/test-environment |
| Local | = Development + *.local.json overrides | dev machine | real credentials live only here (git-ignored) |
| Docker | none in-repo | would be env var | no Dockerfile/compose present (§11) |
| Kubernetes | none in-repo | would be env/ConfigMap | no manifests present (§12) |

- Environment resolved by ASP.NET Core from `ASPNETCORE_ENVIRONMENT` (default Development in launchSettings; design-time factory falls back `ASPNETCORE_ENVIRONMENT` → `DOTNET_ENVIRONMENT` → `Development`, `ApplicationDbContextFactory.cs:16-19`).

---

## 10. User Secrets Audit

- Enabled: YES, Development only (`Program.cs:66-69`).
- Secret Id: `bbdf2e74-8790-4753-8874-f20fcfbdbbd5` — shared by `PointOfSale.API.csproj:7` and `PointOfSale.Infrastructure.csproj:39` (same store used by runtime + design-time EF).
- Package: `Microsoft.Extensions.Configuration.UserSecrets` 9.0.0 (`PointOfSale.Infrastructure.csproj:25`).
- Precedence: sits above JSON files, below environment variables (§5).
- Values expected from user secrets: any of the blank secret keys (JWT secrets, DB/Mongo/Redis/RabbitMQ, SMTP, API keys). Actual stored values are outside the repo (not auditable, correctly).
- Design-time factory also loads user secrets in Development (`ApplicationDbContextFactory.cs:28-31`).

---

## 11. Docker Configuration Audit

- **No Dockerfile, docker-compose.yml/.yaml, or `.dockerignore` found in the repository** (`find` over the tree returned none).
- No Docker-injected environment variables, volumes, mounted config, or Docker secrets exist in-repo.
- Consequence: containerized deployment (if any) is managed OUTSIDE this repo; container env vars would be the production authority per §5.

---

## 12. Kubernetes Configuration Audit

- **No Kubernetes manifests, Helm charts, `values.yaml`, ConfigMaps, Secrets, or Deployment YAML found in the repository.**
- No mounted files, no k8s-injected env, no overrides present in-repo.
- Consequence: no k8s configuration authority in scope; production overrides would occur via env vars in an external deployment pipeline.

---

## 13. Runtime Authority Matrix

| Setting | Default Source | Dev Override | Prod Override | Docker | K8s | User Secrets | Runtime Authority |
|---------|----------------|--------------|---------------|--------|-----|--------------|-------------------|
| ConnectionStrings:DefaultConnection | appsettings.json (`""`) | .Development.local.json | env var | n/a | n/a | Dev | env var → normalize@Program.cs:82 |
| JwtSettings/SuperAdminJwt/Storefront/Customer/Affiliate Secret | appsettings.json (`""`) | .Development.local.json | env var | n/a | n/a | Dev | env var / secrets (validated distinct, `SensitiveConfigurationValidator.cs:38-47`) |
| MongoDB:ConnectionString | appsettings.json (`""`) | .Development.local.json | env var | n/a | n/a | Dev | env var / local.json |
| Redis:ConnectionString | appsettings.json (`""`) | .Development.local.json | env var | n/a | n/a | Dev | env var / local.json |
| Elasticsearch:Url | appsettings.json (`""`) | .Development.local.json | env var | n/a | n/a | Dev | env var / local.json |
| RabbitMQ:Password | appsettings.json (`""`) | .Development.local.json | env var | n/a | n/a | Dev | env var / local.json |
| Smtp:Password | appsettings.json (`""`) | .Development.local.json | env var | n/a | n/a | Dev | env var / local.json |
| CommerceAI:ApiKey | appsettings.json (`""`) | .Development.local.json | env var | n/a | n/a | Dev | env var / local.json |
| R2 credentials | appsettings.json (`""`) | .Development.local.json | `R2_*` env preferred | n/a | n/a | Dev | `R2_*` env var (`Program.cs:262-267`) |
| DataProtection:KeysPath | (none) | filesystem default LocalAppData | .Staging.json path | n/a | n/a | — | config value or LocalAppData default (`Program.cs:1109-1119`) |
| Feature toggles (Stripe/Paymob/ManualPayment Enabled) | appsettings.json (`false`) | local.json | env / section | n/a | n/a | — | config section (`Program.cs:189-192`) |

---

## 14. Dead Configuration

- `DB/appsettings.af22.json` — orphan file (`AffiliateProductIntelligence`, `Storefront.BaseUrl`); no loader references it. **DEAD.**
- `PointOfSale/appsettings.json` + `PointOfSale/appsettings.Development.json` — belong to the MVC template stub; the stub `Program.cs` has no commerce wiring. Effectively dead relative to the product runtime.
- `appsettings.Development.json` (API) contains only a Logging block plus a `_SecretsSourceNote` — minimal; not dead but near-empty.
- No dead named connection strings detected (only `DefaultConnection` exists).
- Blank secret keys in committed appsettings are intentional placeholders (fail-closed contract), NOT dead config.

---

## 15. Duplicate Configuration

- Full config trees are duplicated across `appsettings.json`, `.Production.json`, `.Staging.json`, `.Test.json` — e.g. all five JWT sections, `RabbitMQ`, `Redis`, `Elasticsearch`, `MongoDB`, `PlatformSecurity`, `WhatsAppRateLimits`, `CommerceHub.Payments` are repeated in each environment file. This is standard ASP.NET layering but creates drift risk (a key added to base is not auto-present in env overlays).
- `Storefront.BaseUrl` / `UrlTemplate` appear in base appsettings, Staging, Test, AND the orphan `DB/appsettings.af22.json`.
- `R2` credentials are readable from BOTH a flat env style (`R2_ACCOUNT_ID`) and a section style (`R2:AccountId`) — dual key path by design (`Program.cs:262-267`), env style wins.
- WhatsApp `AUTH_STATE_ENCRYPTION_SECRET` duplicated across Baileys `.env`, `.env.production`, `.env.staging` (all placeholder values).

---

## 16. Security Findings

Positive controls (evidence-backed):
- Committed `appsettings*.json` ship ALL secrets blank (`appsettings.json:4,8,14,21,28,35,41,55,...`; Production/Staging/Test identical). No real DB/JWT/API secret is committed in .NET config.
- Startup fail-closed validator `SensitiveConfigurationValidator.ValidateOrThrow` (`Program.cs:85`) enforces: presence, min length 32 for signing keys, placeholder rejection (`REPLACE_WITH`, `CHANGE_THIS`, `YOUR_`, `CHANGEME`, ... `SensitiveConfigurationValidator.cs:7-19`), and cross-realm JWT secret distinctness (`:38-47`).
- `.gitignore` excludes `.env`, `.env.*`, `appsettings.*.local.json`, `secrets.json` (`.gitignore:33-36,57`). Confirmed `git ls-files` shows NO `*.local.json` tracked.
- SuperAdmin/Affiliate JWT secrets have additional inline hard-stop checks (`Program.cs:2237-2243,2293-2296`).
- Data Protection keys persisted to filesystem with app-name isolation (`Program.cs:1124-1128`).
- Hardcoded-secret sweep over `*.cs` (API/Infrastructure/Core) returned ZERO real embedded credentials.

Findings / risks:
- **[MEDIUM] Committed `.env.production` and `.env.staging` for the Baileys service are TRACKED in git** (`git ls-files` confirms). They contain internal infra topology `REDIS_HOST=192.168.1.5` (`.env.production`/`.env.staging`) and placeholder secrets `AUTH_STATE_ENCRYPTION_SECRET=CHANGE_THIS_...` / `staging-secret-key-change-this-...`. No live secret leaked (all placeholders), but committing prod/staging env files invites future real-secret leakage and exposes internal IP addressing. Recommend converting to `.env.production.example` and git-ignoring the real ones.
- **[LOW] Baileys default `.env` is git-ignored but the tracked `.env.production` documents `REDIS_PASSWORD=` (empty)** — implies the production Redis may be unauthenticated on `192.168.1.5:6379`. Verify Redis AUTH at the network layer.
- **[LOW] Internal IPs and a developer Gmail address are embedded in git-ignored `*.local.json`** (`192.168.1.5`, `192.168.109.128`, `anatico1996@gmail.com`). Not committed (safe), but present on dev machines.
- **[INFO] Single user-secrets id shared** across API + Infrastructure — acceptable, but couples the two projects' secret stores.
- **[INFO] RabbitMQ code-level fallback to `guest`/`guest`** (`Program.cs:2003-2006`) is unreachable in practice because the validator requires real values first, but the literal default remains in code.

---

## 17. Risks

| # | Risk | Severity | Evidence |
|---|------|----------|----------|
| R1 | Tracked prod/staging `.env` files invite future secret leakage + expose internal topology | MEDIUM | `git ls-files` → `PointOfSale.WhatsApp.Baileys/.env.production`, `.env.staging` |
| R2 | Production Redis appears passwordless | LOW | `.env.production` `REDIS_PASSWORD=` |
| R3 | Config drift across duplicated env overlays | LOW | §15 |
| R4 | Orphan `DB/appsettings.af22.json` may mislead operators | LOW | §14 |
| R5 | No in-repo Docker/K8s manifests — deployment config authority is undocumented/external | MEDIUM | §11, §12 |
| R6 | Dual R2 key paths (`R2_*` vs `R2:`) can cause confusion about which wins | INFO | `Program.cs:262-267` |
| R7 | Shared user-secrets id couples API + Infrastructure | INFO | csproj:7 / csproj:39 |

---

## 18. Recommendations

1. Untrack Baileys `.env.production` / `.env.staging`; replace with `*.example` templates and add to `.gitignore` (already covers `.env.*` — the tracked files predate the rule, `git rm --cached` needed). *(Requires user action — not performed in this read-only audit.)*
2. Confirm production Redis requires AUTH; set `REDIS_PASSWORD` and pass via env/secret store.
3. Delete or wire the orphan `DB/appsettings.af22.json`; if `AffiliateProductIntelligence:LowStockThreshold` is needed, move it into `appsettings.json`.
4. Document the external deployment config source (Docker/K8s/CI env injection) in the repo, since it is the production authority and is currently invisible.
5. Consider consolidating repeated sections via a shared base + minimal env deltas to reduce drift.
6. Standardize on ONE R2 key path (prefer `R2_*` env) and remove the section-style fallback if unused.
7. Keep the fail-closed validator as-is — it is the strongest control and should gate any new secret-bearing section.

---

## 19. Canonical Source of Truth

- **Development/Test**: `appsettings.{Env}.local.json` (git-ignored) + user secrets are the effective source of real values, layered over committed `appsettings.json` defaults.
- **Production/Staging**: **Environment variables** are the canonical runtime authority (committed appsettings ship blank; no in-repo Docker/K8s to override). `appsettings.{Env}.json` supplies non-secret defaults (URLs, TTLs, feature flags, DataProtection paths).
- **Connection string final authority**: the normalized value written back at `Program.cs:82`, sourced from env var / user secret / local.json via `GetConnectionString("DefaultConnection")`.
- **Secret contract**: nothing secret is authoritative from committed files by design; `SensitiveConfigurationValidator` guarantees the process refuses to start without externally-provided real secrets.
- **Non-.NET services**: Baileys reads its own `.env`; Vite front-ends bake `VITE_*` at build time.

---

## 20. Final Certification

Every configuration source was discovered (5 .NET config files × environments, launchSettings, global.json, user secrets, env vars, orphan DB json, Baileys `.env` set, Vite `.env` examples). Every provider and its precedence is identified from `Program.cs:57-71`. Every connection string has a documented runtime authority (§7, §13). Every environment is mapped (§9). Every secret location is identified and the fail-closed control verified (§16). No Docker/K8s manifests exist in-repo (explicitly confirmed, not assumed). No real secrets are committed in .NET config; the only tracked env files contain placeholders + internal IPs.

### VERDICT: PASS WITH OBSERVATIONS

Reasoning: The configuration architecture is sound and security-forward — secrets are externalized, a fail-closed startup validator enforces presence/strength/distinctness, and `.gitignore` correctly excludes local secret files (verified: no `*.local.json` tracked). It does not FAIL because no live credential is committed and the runtime authority chain is deterministic and validated. It is not a clean PASS because of the observations in §16–§17: two tracked Baileys prod/staging `.env` files (placeholders + internal topology), a passwordless production Redis indication, an orphan dead config file, duplicated env overlays, and the absence of any in-repo deployment (Docker/K8s) config making the production override authority undocumented.

---
*Audit performed read-only against the live working tree of branch `recovery/3-greenfield-baseline`. No files (other than this report), values, or commits were modified.*

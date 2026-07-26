# RETIRE-1 — LEGACY HOST RETIREMENT CERTIFICATION
## Project: `PointOfSale` (the legacy MVC host, NOT `PointOfSale.API`)

**Audit type:** Source-code-backed. No assumptions. No documentation-based reasoning.
**Scope:** Current working tree only (Git history not consulted, per spec).
**Mode:** STRICT / EVIDENCE-ONLY — nothing deleted, renamed, moved, or edited.
**Repository root:** `C:\Users\fadya\source\repos\PointOfSale`
**Target project file:** `PointOfSale\PointOfSale.csproj`

---

## 1. Executive Summary

`PointOfSale` is the **default, unmodified ASP.NET Core MVC template scaffold** created when
the solution was first generated. It shares the *solution name* with the platform but is **not**
the runtime host. The real application host is **`PointOfSale.API`** (882 `.cs` files, 185
controllers). The legacy `PointOfSale` project contains exactly **3 source `.cs` files**
(`Program.cs`, `HomeController.cs`, `ErrorViewModel.cs`), all pristine template content.

Key findings, all source-backed:

- `PointOfSale.csproj` declares **zero `<ProjectReference>`** entries — it depends on nothing in the solution.
- **No project in the repository references `PointOfSale.csproj`.** The only files mentioning it are
  its own build artifacts (`obj/*.json`) and the solution file.
- **No source code** references its namespaces (`PointOfSale.Controllers`, `PointOfSale.Models`)
  from anywhere outside the legacy folder.
- It is **not** wired into any CI, Dockerfile, deploy script, MSBuild import, `Directory.Build.*`, or `global.json`.
- It carries **no configuration, secrets, connection strings, or `UserSecretsId`** — its
  `appsettings.json` is the empty template default.
- It contributes **nothing unique**; everything it contains is template boilerplate duplicated by the .NET SDK templates.

**Final certification: SAFE TO RETIRE** (see §15). The only build-graph touch point is
its membership in `PointOfSale.sln`, which must be removed as part of retirement.

---

## 2. Project Inventory

Source: `PointOfSale\PointOfSale.csproj` (full file, 20 lines).

| Property | Value | Evidence |
|---|---|---|
| SDK | `Microsoft.NET.Sdk.Web` | csproj line 1 |
| TargetFramework | `net9.0` | csproj line 4 |
| OutputType | (implicit) `Exe` — Web SDK default | csproj (no `<OutputType>` override) |
| Nullable | `enable` | csproj line 5 |
| ImplicitUsings | `enable` | csproj line 6 |
| UserSecretsId | **none** | csproj has no `<UserSecretsId>` |
| ProjectReferences | **NONE** | csproj has no `<ProjectReference>` |
| PackageReferences | `Microsoft.EntityFrameworkCore.Design` 9.0.1, `Microsoft.EntityFrameworkCore.Tools` 9.0.1 (both `PrivateAssets=all`) | csproj lines 10–17 |
| Content / Embedded / StaticAssets | none declared explicitly (default wwwroot static files only) | csproj has no `<Content>`/`<EmbeddedResource>`/`<None>` items |
| CopyToOutput entries | none | csproj |
| Special MSBuild targets | none | csproj |

**Source files (excluding `bin/`, `obj/`):**

```
PointOfSale/Program.cs                 (29 lines — default WebApplication + AddControllersWithViews)
PointOfSale/Controllers/HomeController.cs  (Index / Privacy / Error — template default)
PointOfSale/Models/ErrorViewModel.cs   (template default)
PointOfSale/Views/**                    (Home/Index, Home/Privacy, Shared/_Layout, Error, etc. — template)
PointOfSale/wwwroot/**                   (bootstrap, jquery, jquery-validation — template libs)
PointOfSale/appsettings.json            (Logging + AllowedHosts only)
PointOfSale/appsettings.Development.json (Logging only)
PointOfSale/Properties/launchSettings.json (http/https profiles, commandName=Project)
```

`Program.cs` in full (line-verified) is the stock template:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();
```

No DbContext, no DI registrations beyond MVC, no auth wiring, no data access — despite the two EF
`Design`/`Tools` packages (which are tooling-only, `PrivateAssets=all`, and unused here).

---

## 3. Solution Membership

Source: `PointOfSale.sln` (93 lines).

`PointOfSale` **is** a member of the solution:

- Declaration: line 6 — `Project(...) = "PointOfSale", "PointOfSale\PointOfSale.csproj", "{6930C4B6-7FAF-4B90-85FD-E2581C589193}"`
- Project GUID: `{6930C4B6-7FAF-4B90-85FD-E2581C589193}`
- Solution folder: none (top-level)
- Configuration matrix: lines 26–37 — full Debug/Release × Any CPU/x64/x86, all with `.Build.0` enabled (it **is** built by a solution build).

All five solution members:

| Project | GUID | csproj |
|---|---|---|
| PointOfSale (legacy) | `6930C4B6-…-E2581C589193` | `PointOfSale\PointOfSale.csproj` |
| PointOfSale.Core | `97E8E5AD-…-72380B9C31BD` | `PointOfSale.Core\…` |
| PointOfSale.Infrastructure | `184AEE44-…-B9F1255C9447` | `PointOfSale.Infrastructure\…` |
| PointOfSale.API | `8AFC7706-…-5E31E8D40851` | `PointOfSale.API\…` |
| PointOfSale.Tests | `9C2C0F7A-…-ACEA475170BB` | `PointOfSale.Tests\…` |

**Implication:** `dotnet build PointOfSale.sln` currently compiles the legacy project. Retirement
requires removing its 3 declaration lines (6–7) and its config block (lines 26–37) from the `.sln`.
Because it has no dependents, removing it does not break any other project's build.

---

## 4. Dependency Analysis

**Outbound (does PointOfSale depend on others?):** NONE. `PointOfSale.csproj` has no `<ProjectReference>`.

**Inbound (does anything depend on PointOfSale?):** NONE.

Evidence — every `<ProjectReference>` in the solution:

| Project | References |
|---|---|
| PointOfSale (legacy) | — (none) |
| PointOfSale.Core | — (none) |
| PointOfSale.Infrastructure | `PointOfSale.Core` |
| PointOfSale.API | `PointOfSale.Core`, `PointOfSale.Infrastructure` |
| PointOfSale.Tests | `PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure` |

Repo-wide search for `PointOfSale.csproj` (all `.sln/.csproj/.props/.targets/.yml/.yaml/.json/.sh/.ps1`):
only matches were:
- `./PointOfSale/obj/PointOfSale.csproj.nuget.dgspec.json` (its own build artifact)
- `./PointOfSale/obj/project.assets.json` (its own build artifact)
- `./PointOfSale.sln` (solution membership, §3)

No `Directory.Build.props/targets` exist (only `global.json`, which pins the SDK and does not
reference any project). No MSBuild imports reference it.

---

## 5. Runtime Analysis

The legacy project is **never** used as a runtime host by any other component:

- No code references its namespaces (`PointOfSale.Controllers`, `PointOfSale.Models`) outside the
  legacy folder. Grep across all `.cs` (excluding bin/obj, excluding the legacy folder itself) = **0 matches**.
- No `AddApplicationPart(...)` / `new ApplicationPart(...)` anywhere in the repo → its controllers/views
  cannot be dynamically loaded into another host. **0 matches.**
- Its assembly name is `PointOfSale` (default); the API's is `PointOfSale.API`. No collision, no shared output path.
- Its only two packages are EF `Design`/`Tools` with `PrivateAssets=all` — design-time tooling that
  produces no runtime coupling.

The real runtime host is `PointOfSale.API` (see §6/§9).

---

## 6. Startup Analysis

- `PointOfSale/Properties/launchSettings.json` defines `http`/`https` profiles with
  `"commandName": "Project"` — i.e. it *can* be F5-launched in Visual Studio **standalone**, but no
  other project, task, or script invokes it.
- No `dotnet run --project PointOfSale` (bare) appears in any script, CI, or Dockerfile (grep = 0).
- It is a self-contained entry point (`Main` via top-level `Program.cs`) that nothing else calls.
- The platform's actual entry point is `PointOfSale.API/Program.cs` (imports `MassTransit`, `Hangfire`,
  JWT bearer auth, EF Core, OpenTelemetry, SuperAdmin infrastructure, etc. — a full production host).

Removing the legacy project removes one *unused, standalone* VS launch target and nothing else.

---

## 7. Build Analysis (what PointOfSale contains)

Category-by-category, verified against source:

| Category | Present in legacy PointOfSale? | Evidence |
|---|---|---|
| Business logic | No | only template Home controller |
| Infrastructure | No | no data/services code |
| Controllers | Template only (`HomeController`) | `Controllers/HomeController.cs` |
| Views | Template only | `Views/Home/*`, `Views/Shared/*` |
| Models | Template only (`ErrorViewModel`) | `Models/ErrorViewModel.cs` |
| Services | No | none |
| Middleware | No | stock pipeline in `Program.cs` |
| Authentication | No | no auth registration |
| Authorization | `app.UseAuthorization()` with no policies | `Program.cs:19` (stock) |
| Background services | No | none |
| SignalR | No | none |
| Swagger | No | none |
| Anything unique | **No** | see §8/§10 |

File-count contrast (source `.cs`, excluding bin/obj):
`PointOfSale = 3`, `PointOfSale.API = 882`, `PointOfSale.Core = 516`,
`PointOfSale.Infrastructure = 574`, `PointOfSale.Tests = 839`.
API controllers = 185; API `.cshtml` views = 0 (API is not a views host).

---

## 8. Code Ownership / Exclusive Assets

`PointOfSale` owns **nothing exclusive** to the platform. Everything under it is standard
`Microsoft.NET.Sdk.Web` MVC template output:

- `Program.cs` — stock template (byte-for-byte the default scaffold shape).
- `HomeController.cs` — stock `Index/Privacy/Error`.
- `ErrorViewModel.cs` — stock.
- `Views/**`, `wwwroot/lib/**` (bootstrap, jquery, jquery-validation) — stock template assets.
- `appsettings*.json` — empty template defaults (Logging + AllowedHosts).

No file here is imported, linked (`<Compile Include>` from elsewhere), or referenced by any other
project. `PointOfSale.Tests` compiles only from `PointOfSale.API/.Core/.Infrastructure`
(see `PointOfSale.Tests.csproj` lines 33–35), never from the legacy project.

**Exclusive files that would be lost on deletion:** none of value — all are regenerable template scaffold.

---

## 9. Configuration Analysis

The legacy project holds **no runtime-critical configuration**:

- `appsettings.json` — only `Logging` + `"AllowedHosts": "*"`. No connection strings.
- `appsettings.Development.json` — only `Logging`.
- **No `UserSecretsId`** in `PointOfSale.csproj` (contrast: `PointOfSale.API.csproj:7` and
  `PointOfSale.Infrastructure.csproj:39` both declare `UserSecretsId = bbdf2e74-8790-4753-8874-f20fcfbdbbd5`).
  The shared user-secrets store belongs to API + Infrastructure, **not** to the legacy project —
  removing the legacy project does not touch it.
- No certificates, no environment-variable wiring beyond the template `ASPNETCORE_ENVIRONMENT=Development`
  in `launchSettings.json` (VS-only, standalone).
- No secrets or connection strings of any kind.

---

## 10. Impact of Removal

| Concern | Affected by removing PointOfSale? | Why |
|---|---|---|
| `dotnet build` (solution) | No, once removed from `.sln` | nothing references it; other builds are independent |
| `dotnet test` | No | Tests reference API/Core/Infrastructure only (`PointOfSale.Tests.csproj:33–35`) |
| EF migrations | No | migrations live in Infrastructure/API (own `UserSecretsId`, EF packages); legacy has no DbContext |
| User Secrets | No | legacy has no `UserSecretsId`; shared store owned by API + Infrastructure |
| Configuration loading | No | its appsettings are template defaults used by nothing else |
| Publish | No | the published artifact is `PointOfSale.API`; legacy is never published by any script |
| CI | No | no CI/script references the legacy project or its assembly |
| Debugging | Only removes one unused standalone VS launch target | no other profile/task depends on it |
| Deployment | No | no Dockerfile/deploy script references it |

The **one and only** build-graph coupling is `.sln` membership (§3). Everything else is inert.

---

## 11. Risk Assessment

| # | Finding | Classification |
|---|---|---|
| 1 | No inbound project references | **SAFE** |
| 2 | No source references to its namespaces outside the folder | **SAFE** |
| 3 | No ApplicationPart / reflection / Assembly.Load loading it | **SAFE** |
| 4 | No CI / Docker / script / MSBuild import references | **SAFE** |
| 5 | No unique config, secrets, or connection strings | **SAFE** |
| 6 | Contains only regenerable template scaffold | **SAFE** |
| 7 | Listed in `PointOfSale.sln` with build enabled (must be de-listed) | **LOW** (mechanical `.sln` edit; no dependents) |
| 8 | Has a standalone VS launch profile (removing it drops one unused F5 target) | **LOW** (cosmetic; not part of platform runtime) |

No MEDIUM / HIGH / CRITICAL findings.

---

## 12. Retirement Readiness

**READY.** The project is a dead template scaffold with a single mechanical coupling (`.sln`
membership). All 15 audit steps confirm no runtime, build, test, config, or deployment dependency.

---

## 13. Exact Retirement Procedure (for execution later — NOT performed in this audit)

1. Remove `PointOfSale` from the solution:
   `dotnet sln PointOfSale.sln remove PointOfSale/PointOfSale.csproj`
   (or delete `.sln` lines 6–7 and the `{6930C4B6-…}` config block, lines 26–37).
2. Delete the directory `PointOfSale/` (the legacy host folder — do **not** confuse with
   `PointOfSale.API/.Core/.Infrastructure/.Tests`).
3. Verify build: `dotnet build PointOfSale.sln -c Debug` → expect success.
4. Verify tests: `dotnet test PointOfSale.sln` → expect prior pass/fail baseline unchanged.
5. Verify publish: `dotnet publish PointOfSale.API/PointOfSale.API.csproj -c Release` → expect success.
6. Verify no broken references: `dotnet build PointOfSale.sln` produces no `CS0234`/missing-project errors.

---

## 14. Validation Checklist

- [ ] `dotnet sln list` no longer shows `PointOfSale\PointOfSale.csproj`.
- [ ] `PointOfSale/` directory removed.
- [ ] `dotnet build PointOfSale.sln` succeeds.
- [ ] `dotnet test PointOfSale.sln` matches pre-removal baseline.
- [ ] `dotnet publish PointOfSale.API` succeeds and output is unchanged.
- [ ] Repo-wide search for `PointOfSale\PointOfSale.csproj` returns 0 matches (after `obj/` cleanup).
- [ ] Application starts via `PointOfSale.API` exactly as before.

---

## 15. Final Certification

### ✅ SAFE TO RETIRE

`PointOfSale` (the legacy MVC template host) can be permanently removed from the repository **today**
without affecting runtime, build, tests, or deployment. The sole required action beyond deleting the
folder is removing its membership from `PointOfSale.sln` (§13, steps 1–2); it has **no dependents,
no unique assets, and no runtime role**. The real platform host is `PointOfSale.API`.

**Answer to the primary question — "Can PointOfSale be permanently removed from the repository today?":**
**Yes.** Every conclusion above is backed by the actual source files cited, with no modifications made.

---

*Generated under STRICT/EVIDENCE-ONLY mode. No files were deleted, renamed, moved, or edited. No commit made.*

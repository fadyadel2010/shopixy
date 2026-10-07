# MAIN WhatsApp Settings Dell Baileys Playwright Final Certification - 2026-10-07

## 1. Verdict

**Baileys-only transport authority, legacy Playwright transport retirement, tenant configuration normalization, Redis-aware health/readiness, Dell deployment, Admin WhatsApp Settings behavior, desktop/mobile presentation, and focused automated coverage: PASS.**

**Overall production certification: CONDITIONAL PASS.**

**Live WhatsApp account linking and external message delivery: NOT CERTIFIED.**

The application and Dell runtime are certified to use Baileys as the sole WhatsApp transport. No authorized isolated WhatsApp account or recipient was available for this run, so no QR was scanned and no OTP, order, recovery, or other outbound message was sent. Unconditional delivery certification requires a separate controlled test with an explicitly authorized account and recipient.

## 2. Root Cause And Resolution

The failing Dell QR path was routed by the legacy `Tenant.UseBaileys=false` feature gate to `PlaywrightWhatsAppEngine`. That engine attempted to start a bundled Playwright browser whose expected Chromium revision was absent. The resulting Chromium launch error was a legacy-routing defect, not a Baileys defect.

The resolution removes the dual-transport architecture rather than installing another application-owned browser:

- Baileys is the only registered and injected WhatsApp transport.
- Admin account, QR, status, heartbeat, and message dispatch paths use keyed transport `baileys`.
- Requests to disable Baileys are rejected with `BAILEYS_TRANSPORT_REQUIRED`.
- The legacy Playwright engine, transport, web-engine contract, session-manager contract, implementation, package reference, and obsolete tests were removed.
- Architecture tests reject future runtime Playwright/Chromium reintroduction.
- `Tenant.UseBaileys` and its EF model default are `true`.
- Migration `20261007154500_MAIN_BaileysOnlyTransport` normalizes existing false values to true and changes the PostgreSQL default to true.

## 3. Certified Scope

This certification covered:

- Sole-transport DI and dispatch authority.
- Admin WhatsApp account, QR, status, transport-information, and heartbeat routing.
- Tenant configuration enforcement and persisted default normalization.
- Baileys Redis startup behavior and truthful health/readiness probes.
- Session lifecycle cleanup and duplicate self-healing interval prevention.
- Removal of application runtime Playwright files and Chromium ownership.
- Admin WhatsApp Settings in Arabic/RTL on desktop and mobile.
- AhramStore public storefront smoke verification.
- Dell build, migration, restart, process, and health evidence.

No account/session data was altered. The only persisted change was the deliberate Baileys-only tenant configuration migration.

## 4. Transport Authority

Post-restart authenticated evidence from `GET /api/admin/whatsapp/transport-info`:

```json
{
  "tenantId": "c9c5c1ae-949e-498e-bc63-07242daeb49d",
  "transport": "Baileys",
  "useBaileys": true,
  "message": "Using Baileys (authoritative WhatsApp transport)"
}
```

`GET /api/admin/whatsapp/accounts` returned HTTP `200` with `[]` for that authenticated tenant. The page therefore rendered the correct empty-account state without attempting QR generation or modifying a real account.

The pre-existing disabled/disconnected account for tenant `aa9fcb66-50d9-4003-ae0c-3fda0c200366` was not linked, enabled, disconnected, or otherwise changed during this work.

## 5. Migration State

Applied on the Dell Development database:

- `20261007154500_MAIN_BaileysOnlyTransport`

EF reported the migration without a `(Pending)` suffix after application. The migration performs:

```sql
UPDATE "Tenants"
SET "UseBaileys" = TRUE
WHERE "UseBaileys" = FALSE;
```

It then changes the `UseBaileys` column default to `TRUE`. This prevents old tenant rows or newly inserted rows from selecting a retired transport.

## 6. Baileys Reliability Changes

Redis startup and probes now fail truthfully:

- Redis connection timeout is five seconds.
- Offline command queuing is disabled.
- Redis client errors have an explicit listener.
- `GET /health` returns `503` when Redis is unavailable.
- `GET /ready` requires a ready Redis client and a successful `PING` within two seconds.

Session IDs use `crypto.randomUUID()` rather than the ESM-only `uuid` runtime package.

`SelfHealingService.start()` clears an existing interval before installing a replacement. This makes reinitialization idempotent and prevents duplicate healing loops or leaked process handles.

## 7. Automated Evidence

| Gate | Result |
|---|---|
| Focused .NET WhatsApp selection | PASS: 28 passed, 0 failed, 0 skipped |
| Baileys Jest suite, exact `npm test -- --runInBand` | PASS: 10 suites, 135 tests |
| Baileys TypeScript build | PASS |
| Dell targeted API build | PASS: 0 warnings, 0 errors on final incremental verification |
| Dell Baileys TypeScript build | PASS |
| POS Admin focused WhatsApp tests | PASS: 2 files, 14 tests |
| `git diff --check` | PASS; line-ending notices only |

The Baileys suite initially exposed stale mocks and property assumptions. The final harness now models Redis `on`/`del`, Pino child loggers, Baileys ESM default exports, current browser descriptors, session cleanup between property runs, sticky pool mappings, reconnect behavior, and QR timeout semantics.

The exact Jest command exits normally. A singleton self-healing interval leak discovered by the test process was corrected in production lifecycle code rather than hidden with `--forceExit`.

## 8. Dell Build And Deployment

| Attribute | Value |
|---|---|
| Host | `192.168.1.5` |
| Repository | `/home/fady/ShopixyQA/PointOfSale` |
| Branch baseline | `main` at `878605b2` |
| API command | `dotnet run --project PointOfSale.API/PointOfSale.API.csproj --no-build --launch-profile http` |
| Baileys command | `node node_modules/.bin/ts-node src/index.ts` |
| API port | `5010` |
| Baileys port | `3000` |

Only the API and Baileys processes were restarted. Unrelated services and user-owned browser processes were not terminated.

The Dell files `PointOfSale.API/appsettings.QA.json` and `PointOfSale.API/secure-uploads/development-object-storage/tenants/aa9fcb66-50d9-4003-ae0c-3fda0c200366/` remained present and unmodified as untracked content.

## 9. Runtime Evidence

Final post-restart snapshot:

| Check | Result |
|---|---|
| API `GET /health/live` | HTTP `200` |
| Baileys `GET /health` | HTTP `200`; `status=healthy`, `redis=ready` |
| Baileys `GET /ready` | HTTP `200`; `ready=true` |
| Active Baileys sessions | `0` |
| Stuck sessions | `0` |
| Reconnect storms | `0` |
| Poisoned sessions | `0` |
| Application Playwright artifacts | `0` under API and Infrastructure trees |
| Application Chromium processes | None |
| Isolated browser-test sessions after verification | None |

The Baileys health response also reported `version=PHASE_3_RELIABLE`, ten configured pool members, and zero active sockets.

## 10. Runtime Playwright Retirement

The rebuilt runtime has no `Microsoft.Playwright` package reference, runtime engine, transport, session manager, `.playwright` directory, `playwright.ps1`, or `Microsoft.Playwright.dll` under the API and Infrastructure trees.

Historical Markdown documents still describe the retired migration path, and Baileys' npm lockfile contains the transitive package `electron-to-chromium`, which is browser-version metadata used by JavaScript tooling. Neither is an executable application transport or Chromium runtime.

Browser certification used separately installed global `@playwright/cli@0.1.22` with an isolated in-memory `opencode-ui-test` session. That tooling is outside the application dependency graph. The session was closed after verification, and `playwright-cli list` returned no browsers.

## 11. Browser Evidence

Headed Dell verification covered:

- Admin: `http://localhost:5173/ecommerce/whatsapp`
- Storefront: `http://ahramstore.localhost:5174/`
- API: `http://localhost:5010/`
- Baileys: `http://localhost:3000/`

Admin observations:

- Existing authenticated session survived the API restart and refresh flow.
- WhatsApp Settings loaded with zero console errors after the final restart.
- Accounts API returned HTTP `200` and an empty list.
- Transport information returned HTTP `200`, `transport=Baileys`, and `useBaileys=true`.
- The Arabic page rendered its WhatsApp Settings heading and empty-account guidance correctly.
- At `390 x 844`, the page used `lang=ar`, `dir=rtl`, and had no horizontal overflow.

Storefront observations:

- `ahramstore.localhost` resolved to AhramStore.
- Desktop and mobile layouts had no horizontal overflow.
- Observed storefront console failures were limited to expected unauthenticated auth probes; no WhatsApp transport error was present.

## 12. Safety And Data Handling

- No WhatsApp QR was scanned.
- No account was linked, enabled, disabled, or disconnected.
- No outbound WhatsApp message was sent.
- No OTP was requested.
- No order, cart, payment, shipment, or customer record was created or changed.
- Existing Redis auth state was not deleted or rewritten.
- Credentials are not included in this report.

## 13. Residual Conditions

1. **External delivery remains uncertified.** A controlled QR link and outbound send with an explicitly authorized isolated account and recipient are still required before claiming end-to-end WhatsApp delivery.
2. **The certified Admin tenant currently has no configured account.** Settings and transport authority are healthy, but that tenant cannot deliver messages until an authorized account is linked.
3. **Historical documentation retains legacy terminology.** These references are useful migration evidence but should not be interpreted as active runtime capability.
4. **The deployment is an uncommitted worktree overlay.** Local and Dell source changes were intentionally not committed because no commit was requested.

## 14. Principal Files

Backend authority and persistence:

- `PointOfSale.API/Controllers/Admin/WhatsAppAdminController.cs`
- `PointOfSale.API/Controllers/TenantsController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Core/Entities/Tenant.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/20261007154500_MAIN_BaileysOnlyTransport.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.Infrastructure/WhatsApp/Interfaces/IWhatsAppTransport.cs`
- `PointOfSale.Infrastructure/WhatsApp/Jobs/WhatsAppSessionHeartbeatJob.cs`
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppMessageDispatcher.cs`

Baileys runtime:

- `PointOfSale.WhatsApp.Baileys/src/index.ts`
- `PointOfSale.WhatsApp.Baileys/src/services/SessionManager.ts`
- `PointOfSale.WhatsApp.Baileys/src/services/SelfHealingService.ts`

Certification tests:

- `PointOfSale.Tests/Admin/WhatsAppAdminScopeTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontWhatsAppAuthorityCertificationTests.cs`
- `PointOfSale.WhatsApp.Baileys/src/routes/__tests__/messages.test.ts`
- `PointOfSale.WhatsApp.Baileys/src/routes/__tests__/sessions.test.ts`
- `PointOfSale.WhatsApp.Baileys/src/services/__tests__/*.test.ts`

## 15. Final Decision

The Dell application is certified to run WhatsApp Settings and backend dispatch authority through Baileys only. The legacy application-owned Playwright/Chromium transport is removed, persisted tenant selection is normalized, health/readiness are truthful, the final runtime is healthy, and headed Admin evidence is clean.

The release may proceed for Baileys-only Settings and transport architecture. Live external delivery must remain labeled **not certified** until a separately authorized end-to-end account-linking and message-delivery exercise is completed.

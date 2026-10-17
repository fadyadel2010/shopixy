# Easy Orders shipping reference and Shopixy implementation — 2026-10-16

**IMPLEMENTED AND VERIFIED WITH OPEN OPERATIONAL GATES — not production booking certification.** No Easy Orders settings save/activation, live order/shipment/cancellation, or unapproved geography publication was performed. The API is installed and source/disk/process equivalence was verified after user restart.

## Reference observations (actual authenticated browser)

Reused the existing `app.easy-orders.net/#/shipping` session in the registered `opencode-ui-test` browser; no credentials requested or exported. Added a page-scoped read-only network guard before inspecting dialogs/reloading. Browser observations use Playwright, not REA Evidence: REA tools/CLI are unavailable in this session; no installation or global configuration change was needed.

- Shared shipping settings show integration enablement, shipping country, fixed price versus per-city pricing, default shipping fee, and a separate settings-save button. City table has city name, shipping fee, city type, visibility/state and edit columns; 28 saved rows were visible. The current fixed-price radio was selected. Country choices are discoverable without selecting or changing the stored country.
- Bosta dialog has provider activation, `metadata.apiKey`, and `metadata.canOpenPackage`. No pickup selector was visible in this unconfigured dialog. This is not evidence that all account-specific Bosta pickup requirements disappear.
- Mylerz dialog has activation, `metadata.username`, `metadata.password`, `metadata.serviceType`, `metadata.country`, and `metadata.wharehouseName` (reference's spelling). Do not apply these fields to Bosta or treat the reference as authoritative Mylerz API documentation.
- Existing settings were reloaded. No save, activation or validation-error submission was performed. Save request contract and post-save persistence are intentionally **unobserved**, not passed. The visible save buttons establish an interaction pattern, not a tested mutation contract.
- Sanitized GET contracts: `/api/v1/tenant-shipping-settings` **200**, keys `id, tenantId, showZones, shippingCostType, shippingCost, enableShippingIntegration, countryId, zonesSource`; `/api/v1/shipping-methods` **200**, 53 entries, keys `id, name, key, logo, country`; `/api/v1/tenant-shipping-method-settings` **200**, empty list; `/api/v1/countries` **200**, keys `id, enName, arName, code`; `/api/v1/tenant-cities-settings` **200**, keys `id, tenantId, cityId, cityName, isHidden, isAdded, shippingCost`. `/api/v1/plugins/shipping` returned **400**, a `message` field; no body text or cause inferred.

No tokens, cookies, API keys, private input values, customer data, raw network bodies, HAR or reference screenshots are retained. Supplied Bosta key is not copied into artifacts; the existing encrypted saved-account discovery path is sufficient.

## Gap matrix / implementation decisions

| Area | Reference pattern | Shopixy evidence / action |
|---|---|---|
| Shared pricing | Fixed/per-city merchant fees separate from credentials | Existing `ShippingMethod` supports base/per-kg/free threshold and country scope. Do not call these remote Bosta quotes. No supported per-city rate domain was found; do not invent one by replacing geography IDs with labels. |
| Country/readback | Shared country selection | Fixed: readback had dropped warehouse identity and edits omitted existing `AvailableCountries`, allowing scope erasure. Existing-contract authoring/readback now preserves both. |
| Credentials | Per-carrier fields, independent activation | Shopixy already uses capability descriptors, encrypted save, saved-credential authentication and explicit lifecycle actions. Preserve these distinctions; do not auto-activate after authentication. |
| Bosta pickup | Unconfigured dialog does not require a manual pickup field | Fixed the unconditional saved-ID blocker: authenticated unique-default discovery now supplies frozen concrete origin authority, with exact change detection retained. |
| Reference geography | Country/city selectors | Shopixy has real provider discovery and persisted draft resume (621 genuine candidates), governed canonical import/review, revision/freshness and four-eyes publication. Do not publish the unapproved source research. |
| Parcel / destination | Not observable without orders | Keep canonical destination admission, actual goods declaration/measurements and provider-contract validation; never derive goods value from COD. |
| Creation / cancellation | Not exercised (read-only reference) | Keep durable single-create claim/no blind repost; cancellation must match retained delivery/tracking and independently confirm terminal state. Verify through controlled transport only. |
| Readiness | Provider switch is distinct from shared setting | Keep authentication, account activation, origin evidence, geography, entitlement and booking eligibility separate. Reference simplicity is not authorization to remove real safeguards. |

## Implementation and verification

### Bosta contract and implementation

The retained official OpenAPI (`.ai/evidence/phase6-20261015/bosta-api.yaml`, `BusinessadddeliveryRequest`, lines 1130–1138 and 1271–1274) requires `type`/`cod`, not `businessLocationId`; type **10** is Deliver. Its account/pickup schemas expose `defaultPickupLocation` / `isDefault`, with authenticated `GET /pickup-locations`. The older `BOSTA-2_REQUEST_CONTRACT_COMPATIBILITY.md` says the default applies when omitted. The current YAML's field description does **not** specify an atomic omission/fallback algorithm. We therefore do not depend on an unverified omission experiment: resolve and transmit the concrete verified default ID instead.

Fresh saved-account GET on October 16 returned **200/no-store**, one genuine marked EG default, with exact provider city/district IDs and no street/contact fields (`easyorders-bosta-pickup-readonly.json`). This proves discovery/ownership, not account booking entitlement. The retained October 15 profile had `isActive=false`, mandatory item value, sufficient-credit flag, and automatic/recurring pickups disabled; those retained account-specific flags must not be replaced with an assumption that authentication means activation or daily collection. No live create trial is authorized to resolve them.

Implemented:

- `BostaPickupSelection.cs`: one shared rule for explicit owned pickup or unique provider-marked default. Missing/multiple defaults, incomplete EG geography and malformed discovery fail closed. Invalid/unowned explicit IDs **never** fall back to default; a lone unmarked location is not presumed default.
- `CarrierShipmentService.BostaPickup.cs` / `BostaAdapter.cs`: resolve and freeze the actual location in the existing tenant/configuration/environment/warehouse-bound intent before the durable create claim. Leave saved credentials untouched. Repeat authenticated discovery before POST, compare the exact frozen identity/geography and existing ten-minute freshness window, and transmit the concrete ID. A default change causes zero delivery POSTs; no blind retry or rewritten intent is allowed. Existing historical request formats are not rewritten.
- `BostaPickupVerificationCache.cs`, `CarrierIntegrationAuthority.cs`, `AdminBostaSetupController.cs`, API registration: default-mode readiness uses bounded server-owned proof from authenticated discovery, bound to tenant/configuration/account/credential version/environment/material. No external HTTP is added inside readiness or claim transactions. Missing/expired/evicted/restarted proof blocks readiness with `BOSTA_PICKUP_VERIFICATION_REQUIRED`, not a false requirement to manually type an ID. Failed discovery revokes the matching cached proof. This is transient readiness evidence, not durable shipment authority or activation; each create still independently verifies its frozen origin.
- Bosta Admin now explains default versus explicit override, labels actual provider defaults, reports verification failures, and displays proof without silently selecting/saving anything. Updated English/Arabic text removes the incorrect warehouse-address equivalence and false daily-pickup implication.
- `CarrierAccountCard.jsx` prominently separates authentication from account state, runtime eligibility and blockers. Existing explicit lifecycle actions remain; no activation is implicit.

Shared-method fixes in `ShippingMethodsTab.jsx`, `shippingMethodCountryScope.js` and `ShippingMethodsController.cs` now preserve warehouse identity and country-scope JSON through rate edits, support explicit all/selected countries, validate against the existing Core ISO allowlist, and reject malformed/empty/duplicate scopes instead of broadening them to all countries. A parity test keeps browser country codes aligned with Core. These merchant fees remain base/per-kg/free-threshold prices, not carrier quote certification. No unsupported per-city pricing table or global integration flag was invented.

Controlled tests cover default uniqueness, complete discovery, invalid override behavior, cache scope/rotation/expiry/failure, prepared-origin requirement, exact serialized origin/unique reference/goods value, default/geography change, stale intent, denial, and synthetic create/cancel with independent exact-identity confirmation. Results are recorded below.

Earlier Phase 7 results remain separately attributed in `.ai/reports/SHOPIXY_BOSTA_GEOGRAPHY_R2_LINT_FINAL_CLOSURE_20261016.md`; they are not relabeled as verification of later edits. The previous full Admin **1,798/1,798** and Storefront **528/528** runs and builds predate these changes. The known three missing historical migration/freeze-artifact failures remain open; no historical artifacts are fabricated to erase them.

### Current invocation results

| Invocation | Result |
|---|---|
| `easyorders-account-ui-tests1` | Passed; separate booking-eligibility presentation and existing credential forms |
| `easyorders-backend-build1` / `build2` | First passed; second failed because the new controlled adapter fixture omitted required neutral request fields; preserved, corrected with explicitly empty unused origin fields |
| `easyorders-backend-build3` | Passed; 3,606 unchanged captured inputs and nine artifacts attested |
| `easyorders-backend-build4` / `build5` | Passed; later changes are test-only assertions/persistence coverage, not runtime source |
| `easyorders-backend-regressions1` | 451 passed / 2 failed: new test's verification query accidentally used the unconfigured context tenant filter; fixed by explicit tenant-bound, filter-independent observation |
| `easyorders-backend-regressions2` | **453/453 passed**, zero skipped: carrier setup/readiness, controlled default/explicit create/cancel, ambiguous-create boundaries, mapping/import/publication guards, R2/Redis unit regressions |
| `easyorders-country-backend-tests1` | **26/26 passed**; country DTO validation (separate filter; not falsely counted in the 453 run) |
| `easyorders-method-persistence-tests1` | **61/61 passed**, including country DTOs, controller create/update/readback, rejected updates retaining country/warehouse/rates, tenant isolation, shipping-method synchronization and storefront contract/currency cases |
| `easyorders-mylerz-regressions1` | **108/108 passed**, existing official-contract, public client, money and runtime containment boundaries; not positive Egypt booking certification |
| `easyorders-geographic-architecture1` | **26 passed / 3 failed of 29**; same missing historical initial geographic migration and GEO-3/GEO-4 freeze markers, retained unchanged rather than reconstructed |
| `easyorders-admin-typecheck1` / `typecheck2` | First failed in an unfinished earlier public-transport mock signature; corrected without casts/weaker assertions. Second passed |
| `easyorders-bosta-ui-tests2` / `easyorders-ui-tests3` | First 193 passed / 1 failed because a new test observed the loading status before completion; exact completed-status query fixed. Rerun **199/199 passed** |
| `easyorders-admin-full1` | **1,856/1,856 passed**, full suite, one worker; all 1,689 captured frontend inputs unchanged |
| `easyorders-storefront-tests1` | **528/528 passed** |
| `easyorders-admin-build1`, `easyorders-storefront-build1`, `easyorders-storefront-typecheck1` | Passed; isolated production outputs, regular dist directories not overwritten |
| `easyorders-debug-build1` | Passed/source-attested; nine artifacts installed after user stop, old binaries backed up, configurations unchanged |
| Loaded API equivalence | Passed for API/Core/Infrastructure, same before/after PID **2524**, start **2026-10-16T12:42:42.0082547Z**, writes before process start, configuration unchanged |
| Actual Admin country read/negative validation | Passed: scope and warehouse match API readback; empty selected-country list rejected; zero attempted writes and all compared method readback fields unchanged. First browser attempt timed out on incorrect label capitalization; corrected retry retained as `easyorders-admin-country-browser2.json` |
| Actual Admin Bosta default | **200/no-store**; `ProviderDefault`, genuine `PZ_CVuhF4R`, actual provider EG/city/district IDs; verified message visible; dropdown stays unselected; zero shipping writes. Readiness refresh removes **only** pickup-verification blocker; disabled configuration and unpublished geography remain. Compared account configuration unchanged |
| Actual Admin saved mapping draft | **200**, same run/hash and 621 candidates; save remains disabled because there is no published canonical catalog; no publication attempted |
| `protected-order-post-easyorders` | **1/1 passed**; all compared protected-order/warehouse/reservation/shipment fields unchanged |
| `easyorders-lint1` | **7 errors / 119 warnings**, unchanged maintained scope/rules; zero introduced findings compared with original 193/186. **0/0 is not achieved** |

Invocation logs, JSON/TRX, runtime attestations and sanitized browser evidence are in `.ai/evidence/phase7-20261015/`. Controlled controller and shipment orchestration persistence tests use process-local EF fixtures; they do **not** establish PostgreSQL locking/trigger/concurrency acceptance. Actual PostgreSQL evidence here is existing-data readback plus the earlier genuine reference-draft persistence, not a synthetic relational shipment create/cancel or a merchant settings mutation. No schema creation/reset/migration was used to manufacture a pass.

Fresh loaded health samples (`health-loaded-easyorders.json`) returned HTTP 200, with R2 Healthy (**164–640 ms**) and Redis Healthy (**6.1–28.0 ms PING**), but overall readiness remained **Degraded** (`certificate_provider`, `caddy_edge`). Those out-of-scope TLS/Caddy conditions were not changed. Original R2 timeout attribution and intermittent Redis slow-PING root cause remain unresolved; successful samples do not prove incident repair.

### Canonical source review handoff

The previously requested source research is retained in `.ai/reports/SHOPIXY_CANONICAL_EGYPT_SOURCE_REVIEW_20261016.md`, with a source-identity-preserving review manifest and provenance/checksums. CAPMAS-origin OCHA/HDX is a candidate, not approval: 27 ADM1 / 365 ADM2 / 5,716 partial ADM3, stale-update warning, licensing/metadata discrepancy, missing ADM3 English names and unresolved locality semantics. Comparison finds one country ISO identity, 18 **name-only** review candidates, 599 unresolved references and three unserviceable references. **Zero** city/district identities are confirmed; no canonical UUIDs, source review, mapping approval or publication were fabricated.

### Operational limitations of default readiness

The default proof is deliberately process-local and expires after ten minutes, matching the existing pickup-intent freshness window. Server restart/eviction or a different application instance requires fresh authenticated discovery. It is not a persisted account-activation declaration. This conservative operational restriction is exposed rather than extending freshness arbitrarily or making provider HTTP part of a database claim transaction. Actual shipment intent remains durable and immutable.

## Remaining operational conditions

- **Bosta:** Shopixy configuration is disabled and canonical geography/mappings are unpublished. No independent source reviewer has been designated. The retained provider profile also had `isActive=false`; obtain current account-issued activation/service eligibility confirmation rather than assuming that a working key/default pickup certifies booking. Actual authorized parcel/goods and canonical destination evidence are still required for any future real order. No production create trial was performed.
- **Mylerz:** do not copy Easy Orders service/warehouse fields into an executable Egypt contract. Shopixy correctly retains `MYLERZ_OFFICIAL_CONTRACT_UNAVAILABLE` containment: the account-issued Egypt endpoint/environment/token response, merchant warehouse/service entitlement, identity correlation, safe deduplication/recovery and exact cancellation confirmation remain unproven. See `docs/architecture/MYLERZ-2_COMPREHENSIVE_CONTRACT_REMEDIATION.md`. Username/password configuration is not production service activation.
- **Verification limits:** controlled shipment/controller tests are not relational trigger/locking certification; no merchant database was recreated or reset. The three historical geography-artifact gates and maintained lint **7/119** remain open, as do the original dependency incident causes.

Production booking readiness is **not established**. Existing source/business data and all prior failures remain preserved; `preservation-easyorders-final.json` checks 6,677 original files, **zero missing**, 195 intentionally changed from the original task inventory, and **index unchanged**. Nothing was staged or committed.

The Easy Orders account was left unchanged and authenticated; no browser storage/session export was made. The limited generated-browser-artifact credential-pattern check found no credential-value patterns in the four text artifacts in its current-task time window (`easyorders-artifact-hygiene.json`); this is not a universal secret-detector claim.

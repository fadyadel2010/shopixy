# Shipping carriers — final closure certification

Date: 2026-10-11. Scope: current working tree, not deployed runtime.

## Verdict

**CONDITIONAL — software integration corrections verified; not a fleet-wide production certification.** The offline regression selection is not fully green: 18 historical architecture/source/document assertions remain failing. Live carrier account/route qualification, relational durability/concurrency, and authenticated storefront checkout E2E are not certified here. Mylerz remains unavailable and FedEx remains NotCertified; neither is activated.

This phase performed no commit, staging, migration, backup, business-database creation, merchant-data mutation, real carrier shipment/quote/tracking/cancellation call, real payment, treasury or accounting operation. The agent did not restart services or activate the new backend assemblies. The user independently restarted the API; unrelated background/startup activity is outside this evidence boundary. Normal POS login and read-only cookie-session validation were the only non-fixture authentication interactions.

## Current-source review

Reviewed the latest forensic shipping, FedEx remediation (including canonical parcel/postal/warehouse addenda), DHL remediation/parcel/activation, cancellation, Bosta observed-collection, scheduled tracking tenant context, tracking persistence concurrency and Manual COD certification reports, plus the earlier Mylerz/Aramex material identified during inventory. Historical conclusions were checked against current source, not assumed to be present defects.

Traced shared capability/configuration and connection/readiness admission, shipping method/pricing and purchased execution snapshots, storefront preview/selection/warehouse authority, shipment registration and ambiguous recovery, tracking scheduler and persistence, webhook trust/tenant/identity admission, parcel evidence, label projection, cancellation outcomes, and separate delivery/collection/remittance presentation.

| Carrier | Current integration posture retained | Fresh verification boundary |
|---|---|---|
| Manual | ProductionReady catalog; local fulfillment; existing Manual COD/remittance workflow retained | InMemory creation/COD snapshot and shared projections; no financial workflow execution |
| Bosta | Partial; official operations and narrowly admitted observed collection evidence | Mock HTTP contract/parser/trust tests and shared/UI regressions; no live account or money certification |
| Aramex | Partial; official request/response/identity/time admission; cancellation fail-closed | Mock contract tests; no real account/route certification |
| DHL Express | Partial; measured parcel and canonical origin/destination evidence; local-only verification is not remote account certification | Mock contract/registration tests; actual details/parcel UI rendering; remote-account/customs gates remain |
| FedEx | NotCertified; empty admitted service catalog despite selected executable mocked operations and canonical evidence wiring | Mock transport/endpoint/response/evidence tests; parcel UI now reachable; no activation/catalog expansion |
| Mylerz | Unavailable; empty service catalog; explicit no-transport contract/account containment | Containment/contract regressions; UI refuses unsupported activation/service submission; missing Egypt merchant contract remains a blocker |

## Confirmed defects corrected

| Defect | Smallest correction and evidence |
|---|---|
| Admin details omitted carrier code, purchased service, AWB/labels, COD, registration diagnostics and lifecycle fields required by the UI | Expanded `AdminShipmentDetailsDto` and `AdminShipmentService.MapToDetailsDto` from stored shipment evidence. Six-carrier InMemory read tests preserve tenant isolation. No raw credentials/request payloads added. |
| Summary read flat recipient/creation properties instead of the actual DTO | Uses nested `recipient` and `createdAtUtc`; timeline uses real timestamps rather than marking every earlier checkpoint observed. |
| Standard shipment creation omitted expected COD snapshot | `ShipmentService` copies expected COD flag/amount using the existing canonical order predicate. Three InMemory production-path tests cover COD alias/prepaid; no collection or financial posting is asserted. Existing shipments were not backfilled. |
| List filters sent API-ignored carrier/date parameter names; COD/search were ignored and carrier matching was substring-based | Hook sends `carrier/fromDate/toDate`; DTO/service admit `isCod/search`; exact normalized carrier filtering. Six-carrier tests cover dates, order search, false COD, partial-name rejection and foreign tenant details. |
| Pending external registration, Manual/FedEx filters and essential shipment text were absent/unlocalized | Added status/options and EN/AR shipment evidence, timeline, action boundary, filter and warehouse vocabulary. Preserved cancellation execution authority. |
| Saved inline labels had no presentation; COD wording could imply merchant settlement | Safe stored-document formats/HTTP(S) links only; no generated-label/retrieval operation. UI explicitly separates expected COD, customer collection and merchant remittance; removed guessed badge currency and the misleading blanket “Prepaid” claim. |
| Warehouse canonical address admission had backend endpoints but no POS entry point | Permission-gated warehouse modal uses published place lookup, cascading identities, saved street confirmation, actual postal input and reason. Validates lookup/result identities and fingerprint; preserves failed input and shows confirmed evidence. Backend admission/tenant/actor checks remain authoritative. |
| Missing capability/connection fields were interpreted optimistically; HTTP 200 failed connection record was called success | Missing/unknown capability defaults unavailable, missing mode/support defaults unsupported, unavailable/NotCertified activation disabled. Connection action requires `status === Succeeded`; failed record preserves error and refreshes backend state. |
| Empty carrier service catalog invented standard; editing could silently substitute another service | No invented service fallback; unavailable catalog cannot submit; stored unsupported service is preserved visibly until an explicit supported choice. |
| Tracking ignored admitted history, returned success on rejected progression and could apply COD in that rejected branch | Catch-up traverses only chronological, UTC, nonconflicting observed edges admitted by the unchanged strict lifecycle policy. Each intermediate edge/event/fulfillment synchronization is saved within the same owned relational transaction; provider HTTP remains outside and is called once. Missing/contradictory history returns `TRACKING_PROGRESSION_UNRESOLVED`, retains diagnostic metadata and cannot apply collection. Actual intermediate timestamps are retained. |
| Scheduler skipped Delivered, preventing Delivered→Returned observations, while selecting Failed | Scheduler terminal selection now matches sync: Failed/Cancelled/Returned excluded; Delivered remains eligible for return evidence. Existing tenant binding, batch scopes and lock order preserved. |
| Storefront inferred AVAILABLE from a method list when response status was missing/unknown | Shipping preview admission now requires an explicit admitted availability status; no warehouse/continuation authority survives malformed or failed selection. Focused real-store regressions verify request bodies and fail-closed outcomes. |
| Geographic architecture assertion referenced a retired production migration | Test checks that retired Geo9 migration is absent from production, reads its historical archive, and verifies authoritative `Baseline_Recovery3` contains the shipment evidence columns. No migrations restored/changed/executed. |

Primary files: `PointOfSale.API/{DTOs/Admin/AdminShipmentDtos.cs,Services/Admin/AdminShipmentService.cs,Services/Shipping/ShipmentService.cs}`, `PointOfSale.Infrastructure/Shipping/{CarrierShipmentService.cs,CarrierTrackingProgression.cs,BackgroundJobs/TrackingSyncJob.cs}`, `PointOfSale.Web/src/modules/{shipping,shipping-administration}`, `PointOfSale.Web/src/pages/Warehouses.jsx`, and `PointOfSale.Storefront/src/store/checkoutStore.ts`. Added regression and browser files are listed in the evidence section.

## Fresh build and test results

Backend built from current source with `dotnet build PointOfSale.Tests/PointOfSale.Tests.csproj -c ShippingClosure --no-restore`: **success, 0 errors, 352 warnings**. Separate configuration leaves running Debug assemblies untouched. Both frontend production builds succeeded into isolated temporary output; storefront includes TypeScript checking. Existing large-chunk build warnings remain.

| Selection | Discovered | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|---:|
| Backend, 58 inspected offline classes | 737 | 737 | 719 | 18 | 0 |
| POS shipping + shipping-administration Vitest | 56 | 56 | 56 | 0 | 0 |
| Storefront checkout state + ShippingStep localization Vitest | 19 | 19 | 19 | 0 | 0 |

Backend discovery used `--list-tests` on the fresh ShippingClosure assembly and the same saved filter as execution. Of 737 results, 221 are Architecture-namespace cases and 516 are outside that namespace; all 18 failures are architecture assertions. All **19 new backend regression cases**, **10 new POS closure cases**, and **3 new storefront cases** passed. Counts above are final selection counts, not totals inflated by repeated troubleshooting runs.

Relational database-creating suites were **excluded, not reported as skipped passes**: tracking persistence/tenant context, cancellation, DHL parcel, Bosta observed COD, Manual COD settlement and carrier remittance; migration integration was also excluded. Their eight source suites contain 106 Fact/Theory declarations; expanded execution counts were not discovered or claimed. They require prohibited relational setup or migration execution. InMemory/mock tests do not prove SQL locks, rollback/outbox atomicity, permanent identity constraints or immutable SQL triggers.

### Remaining failed architecture assertions

These tests remain failing, unchanged except for the explicitly authorized archived-migration assertion described above. Twelve failing source literals were already absent in the saved pre-edit source. Six failures require historical PASS strings in untouched `.ai-context/PROJECT_CONTEXT.md`. No historical report was rewritten to manufacture PASS.

| Test class | Failing method(s) |
|---|---|
| `ShipmentIdentityAuthorityCertificationTests` | `CertificationArtifacts_MustRecordM7AShipmentIdentityTruth` |
| `ShipmentFinalAuthorityCertificationTests` | `FinalCertificationRecord_MustFreezeNoFrontendShipmentAuthorityEmergence` |
| `ShipmentActionAuthorityCertificationTests` | `ShipmentMutations_MustRemainBackendRoleAndTenantBound`; `ShipmentCreationAndRecovery_MustBindEveryMutationToCurrentTenantAuthority` |
| `ShippingMethodSelectionAuthorityCertificationTests` | `SplitOrdersAndShipments_MustUseBackendResolvedOrderMethodLineage` |
| `ShippingMethodOwnershipAuthorityCertificationTests` | `ShipmentCreation_MustConsumeTheOrderBoundMethodAndRejectSubstitution` |
| `ShippingMethodIdentityAuthorityCertificationTests` | `ShippingMethodIdentity_MustBeServerGeneratedTenantBoundAndNotClientRestorable` |
| `ShippingMethodScopeIsolationAuthorityCertificationTests` | `StorefrontCache_MustCarryAndValidateCurrentTenantScope`; `ShipmentAndCarrierRegistration_MustUseOrderBoundTenantMethodScope` |
| `ShippingMethodFinalAuthorityCertificationTests` | `FinalRuntimeAuthority_MustRemainBackendOwnedAcrossShippingMethodGovernance`; `SplitOrderShipmentCarrierProjectionCacheAndReplay_MustNotRestoreAuthority`; `FinalCertificationRecord_MustFreezeShippingMethodAuthorityProductionCertification` |
| `AdminShippingMethodReadAuthorityCertificationTests` | `ShippingMethodReadRuntime_MustRemainBackendOwnedAndTenantScoped`; `ShippingMethodReadAuthorityDocumentation_MustBeFrozen` |
| `ShippingSessionAuthorityCertificationTests` | `StorefrontShippingSessionFrontend_MustPersistCandidatesOnlyAndAdmitBackendDtos`; `BackendShippingSessionAndQuoteApis_MustBeTenantCartCustomerAndNoStoreBound`; `CertificationArtifacts_MustRecordSfShp2Truth` |
| `StorefrontShippingAuthorityCertificationTests` | `CertificationArtifacts_MustRecordP2FTruth` |

Current authority is not reintroduced merely to satisfy retired source spelling: standard creation consumes tenant-bound purchased order snapshots rather than reloading mutable methods; controller permission policies are installed by `PosPermissionApplicationModelConvention` and `PosEndpointPermissionCatalog` (`Shipment`/`AdminShipments` read ShippingView, mutation ShippingFulfill), rather than the old controller role attribute. Checkout selection is async preview/select authority, not the asserted historical synchronous setter. Source/cache/no-store assertions need an independently scoped architecture-test reconciliation; this phase does not certify them all obsolete or erase their failures.

## Browser verification

Reused registered headed Edge session `opencode-ui-test`. POS login succeeded normally after an earlier HTTP 500 while the API was unavailable; route guards and real cookie-session validation were retained. All business API reads/writes were intercepted, including unmatched requests. Unmatched mutations were rejected. Vite source transforms were cache-busted locally to ensure edited modules, not stale Windows watcher cache, were exercised.

POS: **33 scenario cases / 48 measured layouts passed; 266 business requests intercepted; 0 real provider calls or business mutations**. Authenticated real shipment details for all six carriers; stored AWB/label, nested recipient, expected COD and unresolved registration; withheld shipment actions; DHL/FedEx parcel form applicability. Real administration overview/accounts/readiness/health/audit, unsupported Mylerz/FedEx activation, empty catalog submission refusal, warehouse admission, supported Manual method submission, list filter query, HTTP 200 connection failure, read failure/retry and 400 input preservation/confirmed admission were exercised with fixtures. EN/LTR and AR/RTL at desktop 1440×1000 and mobile 390×844. Page-level overflow was checked; horizontal tables/tab strips remain contained scrolling regions. The three mutation endpoint types were synthetic connection-test, shipping-method creation and warehouse admission, fulfilled locally. No actual label link was opened/downloaded; stored-label href/format/rendering was checked.

Storefront: **isolated actual `ShippingStep` component**, not an authenticated AhramStore checkout route. Controlled presentation state covers AVAILABLE, EMPTY, NOT_APPLICABLE, VALIDATION_REQUIRED, UNAVAILABLE, ERROR, pending and stale selection in EN/AR at both viewports. Real `isStepValid` continuation gating was used; no auth/tenant resolution was fabricated. **28 scenario cases / 24 measured layouts passed; 0 component network requests**. The fixture does not prove customer authentication, full checkout/session/pricing E2E, order submission, or payment execution. Those limitations are intentional containment, not a claimed E2E pass.

## Evidence and preservation

Evidence directory: `C:\Users\fadya\AppData\Local\Temp\opencode\shipping-final\`.

- `backend-build.log`, `backend-selected-files.txt`, `backend-filter.txt`, `backend-discovered.log`, `backend-tests.log`, `test-results/shipping-closure.trx`.
- `web-tests.json`, `storefront-tests.json`, `web-build.log`, `storefront-build.log`, isolated `web-dist/` and `storefront-dist/`.
- `admin-browser.log`, `admin-browser-result.json`, `storefront-browser.log`, `storefront-browser-result.json`, EN/AR desktop/mobile screenshots.
- `baseline-status.txt`, `baseline-index.diff`, `baseline-hashes.json`, `source-baseline/`, `task-only.diff`, `preservation-changes.json`, `preservation-final.json`, `verification-summary.json`, `baseline-failure-classification.json`, `excluded-relational-suites.json`.
- Reproducible fixture scripts: `tests/browser/shipping-final-closure.js` and `tests/browser/storefront-shipping-closure.js`. Require existing local dev servers; POS script requires normal prior login. Storefront script is explicitly a component fixture.
- Focused regressions: `PointOfSale.Tests/Shipping/{CarrierTrackingProgressionTests.cs,ShipmentCreationProjectionTests.cs,ShipmentExternalRegistrationSafetyTests.cs}`, `PointOfSale.Web/src/modules/shipping/shippingClosure.test.tsx`, and additions to `PointOfSale.Storefront/src/store/checkoutStore.validation.test.ts`.

Pre-existing modified/untracked sources were copied and hashed before edits. Final SHA-256 comparison: **7,771 protected pre-existing files; 23 explicitly task-modified; 7,748 unrelated files byte-identical; 0 unexpected changes or deletions**. Task-only comparisons preserve their existing hunks; existing cancellation/financial/evidence sources were not edited. Task-only whitespace check: 0 issues. Cached Git diff remains empty (0 bytes); nothing staged or committed. The temporary source/business browser routes were removed and both isolated component tabs closed; the reused session remains at about:blank.

## Unresolved limitations / stopping boundary

1. No live carrier certification, request execution, pickup/service availability, new label generation/download, or real merchant account validation. Mylerz contract/account, FedEx production certification, DHL remote-account and international customs evidence gates remain.
2. Tracking catch-up requires complete admitted history. An adapter without sufficient intermediate observations remains unresolved, rather than inventing events or weakening canonical transitions. New relational catch-up atomicity/concurrency must be verified in an authorized relational test phase before activation.
3. Legacy generic webhook `Processing` duplicate acknowledgement/recovery remains independently unresolved; the narrowly atomic Bosta official observed-money path was not generalized or weakened.
4. Delivery and customer collection remain distinct from merchant remittance. Existing Manual remittance is retained; no new Bosta/Aramex/DHL/FedEx/Mylerz financial feature or activation was introduced.
5. Eighteen architecture assertions prevent a clean full-selection regression PASS. No fresh SQL database/migration tests or authenticated storefront checkout E2E were executed.
6. Backend changes are source/build verified only, not activated against the running merchant database. No legacy shipment backfill or account/configuration changes were performed.

Stop after this phase. Production fleet readiness remains blocked on the carrier, relational and operational gates above; this report is not deployment or financial-operation authorization.

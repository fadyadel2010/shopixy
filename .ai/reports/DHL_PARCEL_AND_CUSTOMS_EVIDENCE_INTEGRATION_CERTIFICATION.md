# DHL Parcel and Customs Evidence Integration Certification

## 1. Verdict and execution boundary

**PASS — bounded, supported single-parcel measurement integration in production source, verified through actual SQL orchestration and deterministic DHL HTTP mocks, and through the real admin UI with intercepted API responses. International creation and remote DHL account activation remain blocked. This is not deployment or remote account certification.**

- Executed on 2026-10-11 in `C:\Users\fadya\source\repos\PointOfSale`, without subagents.
- Branch `main`, HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`; HEAD unchanged. Index empty at inspection and closeout.
- The user explicitly confirmed that DHL implementation is authoritative and the appended cancellation runtime closeout must not be executed. MAIN was not restarted, migrated, or activated. The existing cancellation report was not edited.
- No real shipment, cancellation, pickup, collection, settlement, remittance, refund, or other MAIN business-data mutation was performed. No DHL sandbox/production endpoint was contacted. Public developer documentation was read only.
- POS authentication and ordinary reads used the already-running local application. Shipment/evidence/quote UI verification used synthetic intercepted traffic. Credentials are not included in this report or the browser script.
- **306 distinct backend cases passed in the two disjoint final runs**, with zero failures/skips; **33 frontend tests passed**. Earlier failures and recovery runs are disclosed below. Repeated executions are not counted as additional cases.

## 2. Source findings before implementation

| Source | Actual meaning and defect |
|---|---|
| `PointOfSale.Core/Entities/Product.cs`, shipping fields | `Weight`, `Length`, `Width`, `Height`, `WeightUnit`, `DimensionUnit` are catalog product fields. Although dimension comments say “Package”, there is no shipment/piece ownership, packed contents, operator measurement, or validated packing algorithm establishing a finished order parcel. |
| `PointOfSale.Core/Entities/ProductVariant.cs` | Optional product-variant weight falls back to product weight. It is not packed gross weight. |
| `PointOfSale.Core/Entities/Shipment.cs` | Legacy nullable `WeightKg`, `PackageCount`, `PackageDescription` exist, but no parcel dimensions or measurement provenance exists. Legacy weight alone is insufficient evidence. |
| `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs` | Both create-request builders used nominal `1.0m` weight and omitted dimensions. International customs snapshot acquisition could also substitute `1.0m` gross weight. |
| `PointOfSale.Infrastructure/Shipping/ShippingQuoteService.cs` | Legacy compatibility quote builder used caller weight/count, carrier-configuration pickup location and Cairo/EG fallback, and omitted dimensions. This is not canonical warehouse-origin or packed-parcel evidence. |
| `PointOfSale.API/Controllers/StorefrontShippingQuotesController.cs` | Anonymous compatibility DTO defaulted absent weight to 1 kg. It was not an operator measurement workflow. |
| Existing admin shipment-details page | No measurement entry existed. |
| Existing registration durability | Shipment request identity, attempt count, state and timestamps existed; origin/geography/customs had their own snapshots. **No complete carrier create-request JSON/fingerprint existed on Shipment** before this change. The cancellation operation fingerprint is a separate authority. |

No packing algorithm was found in the traced production path. No catalog dimensions were added together, copied into a parcel, or used to initialize the measurement form. No historical legacy `WeightKg` was automatically recertified as measured weight. Existing MAIN parcel records were not queried to invent that meaning.

## 3. Complete caller traces

### Canonical storefront selection and order/shipment intent

1. `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx` consumes `src/store/checkoutStore.ts`.
2. `checkoutStore.ts` uses `/api/storefront/shipping-methods`, `/preview`, `/select`, and canonical checkout/pricing/session requests. It does not supply measured parcel dimensions to DHL or call the compatibility `/shipping/quotes` route.
3. `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs` → `Services/Storefront/ShippingMethodService.cs` → shipping calculation/readiness and canonical configured warehouse admission. Catalog/cart weight for merchant shipping calculation does not become measured parcel weight.
4. `PointOfSale.API/Controllers/CheckoutController.cs` → `Services/Checkout/CheckoutService.cs`. `ValidateShippingMethodAsync` reloads SQL shipping-method authority and checks service and `CheckoutAdmission` readiness; transaction-scoped warehouse resolution is validated. Order snapshots retain `ShippingMethodId`, name, `ShippingServiceLevel`, fulfillment carrier/mode, warehouse, address admission and order-item quantities.
5. `PointOfSale.API/Consumers/OrderShipmentOrchestrationConsumer.cs` handles order confirmation/payment/accounting events and invokes `Services/Shipping/ShipmentService.cs`, after existing payment/accounting gates.
6. `ShipmentService.CreateShipmentAsync` creates the canonical shipment intent and atomically publishes `ShipmentExternalRegistrationRequestedEvent` through the existing outbox. Carrier intents start `PendingExternalRegistration`.
7. `PointOfSale.Infrastructure/Consumers/ShipmentExternalRegistrationRequestedConsumer.cs` invokes `ICarrierShipmentService.ExecuteCarrierForExistingShipmentAsync` after commit. `OrderShipmentOrchestrationConsumer` can also execute an existing pending intent. `PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs` resolves each candidate tenant and recovers pending registrations through the same service.

DHL remains unavailable to new canonical checkout admission under the existing remote-account readiness block. Historical/synthetic DHL intents can collect operator evidence without removing this block. Saving measurement does not execute a provider POST. Existing pending-registration recovery can consume the saved evidence after a future separately authorized account certification; it still rechecks readiness and all ordinary authorities.

### New authenticated measurement and quote path

```text
/admin/shipping/shipments/:id
  ShipmentDetailsPage.jsx
    ParcelEvidenceForm.jsx
      GET /api/admin/shipments/:id/parcel-evidence
      PUT /api/admin/shipments/:id/parcel-evidence
        AdminShipmentsController.SaveParcelEvidence
          authenticated principal actor + ITenantContext.RequiredTenantId
          ShipmentParcelEvidenceService.SaveAsync
            same-tenant shipment/order/items; shipment SQL lock; cancellation barrier
            ParcelMeasurements.Admit
            immutable Shipment.ParcelEvidenceJson committed
      POST /api/admin/shipments/:id/parcel-quote
        AdminShipmentsController.QuoteParcel
          ICarrierShipmentService.QuoteExistingShipmentAsync
            restored parcel + admitted canonical origin/destination + canonical credentials
            ShippingQuoteRequest -> DhlAdapter.GetQuoteAsync -> normalized ShippingQuoteResult
```

New actions remain under the existing `AdminShipments` permission catalog: GET requires `shipping.view`; PUT/POST require `shipping.fulfill`. They also explicitly require authentication. Mutation actor comes from the authenticated NameIdentifier, not a body field. Input has no tenant, order, carrier account, environment, currency, ledger, insurance, charge, or customs-value selector.

### Registration after measurement

```text
existing outbox/consumer/recovery caller
  ExecuteCarrierForExistingShipmentAsync
    existing service/payment/readiness/cancellation checks
    ParcelMeasurements.Restore against same-tenant order contents
    existing origin/geography/customs resolution
    BuildDhlRequest with actual centimetres / packed gross kilograms / parcel reference
    BuildDhlIntent
    ClaimShipmentWithEvidenceAsync
      SQL shipment lock + fresh context/account/evidence qualification
      conditional single-winner attempt claim
      request JSON + SHA-256 committed in the same transaction
    DhlAdapter.CreateShipmentAsync outside SQL transaction
    existing normalized response admission and successful registration persistence
```

The legacy `CreateShipmentWithCarrierAsync` DHL branch now persists a pending intent and delegates to this same worker path. Its nominal-weight builder is no longer a DHL execution path. Non-DHL legacy behavior is preserved.

The anonymous compatibility `ShippingQuoteService` explicitly excludes DHL because it cannot prove a measured parcel or canonical origin. Other providers retain their compatibility contracts. Authenticated shipment quotes are the supported DHL estimate path; checkout customer-charge authority is not replaced by provider estimates.

Final review also found that domestic canonical `express` maps to DHL product N for creation but the quote filter compared it with the returned neutral `domestic` name, yielding an empty successful estimate. DHL quote filtering now selects the same admitted route product code as creation (N/P/H), rejects unsupported requested service mappings before HTTP, and reports `DHL_RATE_UNAVAILABLE` when the requested product has no estimate. The PostgreSQL end-to-end test requires a nonempty 123.45 EGP quote, not just `Success=true`; four fresh route/service regression cases verify this defect correction.

## 4. Authoritative parcel facts after implementation

`Shipment.ParcelEvidenceJson` holds a versioned `ParcelEvidence` containing:

- Same-tenant Shipment/Order identities, authenticated operator ID and UTC recording timestamp.
- Original measured length, width, height, dimension unit, actual packed gross weight and weight unit.
- Explicit package count 1 and operator parcel reference. This reference is a customer piece reference, not a DHL-issued tracking identity.
- Deterministically converted centimetres/kilograms.
- All canonical order item IDs, product IDs, optional variant IDs and quantities, ordered by item ID. All order contents belong to the one admitted parcel; split packing is not admitted.

The original measurement and conversion result are retained together. Restoration reruns conversion and compares ownership, version, converted values and the full contents assignment. Legacy `Shipment.WeightKg` is deliberately not overwritten: its `numeric(10,3)` precision is narrower than the official package schema, and its historical provenance is different.

### Validation

- Raw measurements must be positive decimal values. ASP.NET decimal JSON binding rejects non-finite/overflow numeric input; the service validates values independently of frontend checks.
- Supported dimension units: `cm`, `mm`, `in`; decimal conversion factors 1, 0.1, 2.54.
- Supported weight units: `kg`, `g`, `lb`, `oz`; decimal conversion factors 1, 0.001, 0.45359237, 0.028349523125.
- Unrounded converted measurements must be within official metric limits. Converted output rounds to 3 decimals using `MidpointRounding.AwayFromZero`. Out-of-range raw conversions cannot be rounded into admission.
- MyDHL 3.3.4 Package schema limits: dimensions **0.001..9,999,999**, weight **0.001..999,999,999,999**, each a multiple of 0.001. The adapter now independently validates these bounds/precision for both quote/create.
- These are schema bounds, not claims that an account/product accepts every parcel within them. Account and route limits remain uncertified and gated.
- Only one package in both input and canonical Shipment is supported. No multi-package capability is inferred from DTO cardinality.
- Parcel reference: 1..35 ASCII letters/digits/hyphens. Order contents must exist and have positive quantities.
- A cancelled order, cancelled/coordinated shipment, registered shipment, prior registration attempt or frozen request cannot accept new measurement input.
- Measurement is write-once. Equal resubmission before registration returns the same original evidence/timestamp; different resubmission conflicts. SQL also rejects changes/clearing of an existing parcel snapshot.
- No product-weight summation, volumetric algorithm, shipping charge, rate, or payment total supplies measurements.

Official public source read on this date: <https://developer.dhl.com/sites/default/files/2026-10/dpdhl-express-api-3.3.4.yaml>, Package schema and export line-item schema. No provider account operation was used to obtain these constraints.

## 5. Customs provenance, semantics and explicit containment

Reviewed:

- `PointOfSale.Core/Entities/Customs/CustomsEntities.cs`
- `PointOfSale.Core/Interfaces/Customs/CustomsContracts.cs`
- `PointOfSale.Core/DTOs/Customs/CarrierNeutralCustomsDto.cs`
- `PointOfSale.Infrastructure/Customs/CustomsEvidenceAdmissionService.cs`
- `PointOfSale.Infrastructure/Customs/CustomsAdministrationWorkflowService.cs`
- `CarrierShipmentService.ResolveOrRestoreShipmentCustomsAsync`
- `DhlAdapter.TryBuildExportDeclaration`

Existing customs workflow supports draft/review/approval/publication and version/checksum records. Snapshot acquisition selects current **Published** product/variant evidence with matching same-tenant approval and publication checksums. That service/model foundation is not proof that the current shipping UI or order records already establish every DHL customs fact.

| Customs fact | Provenance / remaining qualification |
|---|---|
| Line identity and quantity | Canonical OrderItem/Product/Variant and order quantity, frozen into customs snapshot. DHL admits bounded integer quantity and PCS-compatible units only. |
| Description, HS code, origin/manufacture country | Published customs evidence version, not guessed from shipper address or catalog name. Manufacturer is nullable in the existing neutral model; DHL rejects missing manufacturer, HS code or description. |
| Net weight | Existing snapshot computes evidence net weight × order quantity; line net weight is a total line weight. DHL converts it to metric and does not multiply it again or invent line gross weight. |
| Shipment gross weight | DHL snapshot acquisition now uses restored measured packed gross weight. Missing DHL parcel evidence, including international recovery, cannot create a nominal 1 kg customs snapshot. Other-provider existing semantics are preserved. |
| Declared value | Existing evidence has `DeclaredValue`, `Currency`, `ValueSource`, source JSON, but lacks an admitted per-unit/per-line value-basis contract. Snapshot copies DeclaredValue without quantity extension. A free-form ValueSource is not sufficient proof of invoice meaning. |
| Currency | Existing published evidence currency is retained. DHL rejects missing/invalid or inconsistent line currencies; no automatic conversion or UI currency override is introduced. |
| Export reason, purpose, invoice and incoterms | Neutral DTO does not establish complete route-specific invoice number/date, commercial/personal purpose, export-reason type, country-specific licenses, or admitted incoterm. Current adapter's DAP constant is not evidence of an order/agreed incoterm. |
| Route | Canonical shipment origin and destination snapshots. Adapter retains route matching and now requires customs gross weight to match actual package weight and aggregate line net weight not to exceed it. |

**International new creation remains blocked.** Missing published evidence retains existing rejection. Even a successful snapshot result cannot trigger a new international DHL POST: orchestration returns `DHL_CUSTOMS_VALUE_SEMANTICS_UNPROVEN` before attempt claim. The UI displays this limitation for known international routes, and unresolved route applicability when canonical origin/destination is not yet known. No disconnected customs form or unqualified invoice fields were added.

The official export line `price` is a **unit/article price**, while line weight is explicitly a **total**. The adapter's isolated serialization now calculates shipment declaration as `sum(unit price × quantity)` rather than summing unextended unit values. The regression fixture proves 125.50 × 2 = 251.00 USD; line price remains 125.50 and total line net weight remains 0.5 kg. This does **not** recertify ambiguous existing publications as unit-price evidence: canonical international creation is blocked precisely because that value basis is unproven. Order item UnitPrice/Total, tax/discount, shipping charges and customer/order payment totals are not substituted for customs values.

Official exportReasonType supports, among others, permanent, temporary, return, commercial_purpose_or_sale, sample and gift. Which reason, invoice requirements, classifications, incoterm and extra documents are mandatory for an actual P/H route/account still needs authoritative operator/document evidence and account/route qualification. No mapping was invented. Dangerous goods, insurance, COD and unsupported services remain rejected.

## 6. Durable identity, recovery and concurrency

- New `CarrierRequestSnapshotJson` records version, tenant/shipment/order, canonical configuration ID, account ID, sandbox flag, configured API URL, exact parcel JSON, relevant origin/destination/recipient/order/context/contents and the actual neutral create request. Secrets are not serialized.
- `CarrierRequestFingerprint` is SHA-256 over the exact persisted request representation. Text storage preserves the serialized bytes rather than allowing JSONB rewriting to change fingerprint input.
- DHL request construction happens before claim. Snapshot/fingerprint and the single-winner attempt count commit together, under the existing shipment row-lock and cancellation barrier, before provider HTTP. Snapshot failure rolls back the attempt claim.
- Inside the claim transaction, fresh same-tenant shipment/order/items and canonical credential authority are checked again. Changed evidence, context, configuration/account/environment, cancelled order or lost financial authority prevents a new POST.
- Replays with a snapshot validate version/ownership, SHA-256, parcel representation, context, account/environment/configuration/API URL and durable request ID. Changed destination, account or environment requires reconciliation. Existing order historical guards reject currency mutation outright.
- PostgreSQL trigger additionally freezes previously populated parcel JSON and request JSON/fingerprint. There is no backfill of fictitious measurements into old attempts.
- A prior ambiguous attempt enters existing lookup/reconciliation, not create. DHL lookup remains unimplemented/fail-closed. Timeout, invalid correlation or missing mandatory label stays `ReconciliationRequired`, attempt count 1, no duplicate POST.
- The original `ClaimShipmentForExternalCallAsync` private signature is retained as a wrapper around the evidence-aware implementation. Existing registration/cancellation SQL coordination tests retain their actual execution boundary. No cancellation regression assertions were edited.
- Existing lifecycle/response admission remains authoritative: only admitted correlation, shipment identity, single piece and mandatory PDF label can produce successful DHL registration. No pickup or POST retry was added; Message-Reference/customer reference is not an idempotency guarantee.
- Quote evidence is persisted/revalidated as the measured parcel plus canonical route context; no immutable create-operation identity is reused for quote estimates.

## 7. Frontend and browser evidence

Actual screen: `PointOfSale.Web/src/modules/shipping/pages/ShipmentDetailsPage.jsx`, with the integrated `components/ParcelEvidenceForm.jsx` shown for DHL.

- EN/AR labels, dimension/weight units, explicit packed-vs-catalog distinction, original measured input, one-parcel contents assignment and parcel reference.
- Missing/negative values disable save; backend validation remains required. Localized server validation explanations include schema ranges, immutable/locked evidence and account readiness.
- Input retained across recoverable 400/409 errors; equal resubmission is unchanged. Successful save disables editing, and reload restores the exact original units/values.
- Backend-authorized availability controls entry; accounts, tenant, currency, ledger and server-owned money are absent from the input.
- Quotes explicitly presented as estimates. Unresolved registration state/error remains visible, not promoted to successful shipment creation.

Browser procedure: `playwright-cli list` first returned no registered browsers. One isolated headed Edge `opencode-ui-test` was opened with the prescribed command; subsequent `list` confirmed ownership. The user requested help inspection; `playwright-cli --help` and `--help open` were inspected. The session was reused throughout, without duplicate opens or persistent/default-profile options.

Executable verification script: `tests/browser/dhl-parcel-evidence.js`, run using:

```powershell
playwright-cli -s=opencode-ui-test run-code --filename="tests/browser/dhl-parcel-evidence.js"
```

Latest browser result: **PASS**, eight scenarios: missing evidence; explicit unit entry; synthetic 400 with preserved values; immutable save; readiness block; estimate display; Arabic RTL; immutable reload. Two PUTs and two quote POSTs were intercepted. Both PUT bodies were exactly the same: 231 × 145 × 89 mm, 1750 g, BOX-1, one parcel. Synthetic responses carry `X-Shopixy-Test-Traffic: intercepted-synthetic-dhl-parcel`; IDs and displayed shipment name identify intercepted evidence. All other business-mutating API traffic is blocked by the test route. Actual provider calls: **0**.

Screenshots read/reviewed under `.artifacts/dhl-parcel/`:

- `browser-validation-en.png`
- `browser-estimate-en.png`
- `browser-ar-rtl.png`

One initial browser-script execution failed because URL was unavailable in the CLI execution scope; path parsing was corrected. That failed attempt is not positive verification. Final script execution completed with its explicit PASS result. Existing surrounding shipment shell still contains English legacy text in Arabic mode; the new measurement section is localized and explicitly RTL.

## 8. Migration and MAIN boundary

Additive migration:

- `PointOfSale.Infrastructure/Migrations/20261011121839_ShipmentParcelRequestEvidence.cs`
- matching Designer and `ApplicationDbContextModelSnapshot.cs`

Adds three nullable Shipment columns: parcel evidence text, carrier request text, fingerprint varchar(64), plus the PostgreSQL immutability function/trigger. Existing origin/geography/customs snapshots alone cannot preserve actual parcel dimensions and the complete account-bound request, so an additive schema change is required. No old measurements are backfilled.

Migration generation and `dotnet ef migrations has-pending-model-changes --configuration DhlParcel` used the design-time factory, Test environment and a dummy design-only connection; no database update command was run. Pending-model result: **No changes have been made to the model since the last migration.** Custom DhlParcel build configuration avoids replacing running MAIN binaries.

`TestDatabaseScope` created unique isolated test databases on the configured PostgreSQL test host. It applies the existing full EF migration chain with its established supplemental SQL/normalization procedure. The new migration's applied identity was asserted in a fresh regression; immutable parcel and fingerprint mutation were rejected by actual PostgreSQL. No migration was applied to MAIN and the new schema is **not activated there**. No claim is made that the running MAIN API exposes these newly built endpoints.

## 9. Fresh build, discovery and exact outcomes

Final backend build from current source:

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" --artifacts-path ".artifacts/dhl-parcel" --no-restore --no-incremental -v minimal "-clp:ErrorsOnly;Summary"
```

**Succeeded: 460 warnings, 0 errors, elapsed 00:00:25.97.** Core, Infrastructure, API and Tests were rebuilt into isolated artifacts. Test DLL: `.artifacts/dhl-parcel/bin/PointOfSale.Tests/debug/PointOfSale.Tests.dll`. No historical binaries supplied fresh-source proof.

Fresh `dotnet test ... --no-build --no-restore --list-tests --filter ...` discovery was performed initially and again from the final binary, using the union of the exact class filters below. It listed concrete theory cases. Frontend `npx vitest list` separately listed all 33 selected frontend cases.

Final backend execution uses common flags:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --artifacts-path ".artifacts/dhl-parcel" --no-build --no-restore --filter $filter --logger "console;verbosity=normal" --logger "trx;LogFileName=<name>.trx" --results-directory ".artifacts/dhl-parcel/results"
```

Actual filters:

```text
parcel-dhl-final:
FullyQualifiedName~DhlParcelEvidencePostgresTests|FullyQualifiedName~ParcelMeasurementTests|FullyQualifiedName~DhlOfficialContractCompatibilityTests|FullyQualifiedName~DhlRuntimeAuthorityTests|FullyQualifiedName~DhlGeographicCompatibilityTests|FullyQualifiedName~CarrierIntegrationAuthorityRemediationTests|FullyQualifiedName~ShipmentExternalRegistrationSafetyTests|FullyQualifiedName~ShipmentExternalRegistrationRequestedConsumerTests|FullyQualifiedName~CarrierIntegrationStateMachineConvergenceTests|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests|FullyQualifiedName~ShippingMethodsCarrierAuthorityTests|FullyQualifiedName~T10SettlementContainmentTests

containment-providers-final:
FullyQualifiedName~ShipmentCancellationPostgresTests|FullyQualifiedName~AramexOfficialContractCompatibilityTests|FullyQualifiedName~MylerzOfficialContractCompatibilityTests|FullyQualifiedName~FedExResponseAdmissionTests|FullyQualifiedName~PosPermissionAuthorizationTests|FullyQualifiedName~BostaEndpointSurfaceTests

parcel-concurrency-recheck:
FullyQualifiedName~DhlParcelEvidencePostgresTests.Concurrent
```

Inspected final TRX counters (not inferred from source test counts):

| TRX | Total/executed | Passed | Failed | Summary |
|---|---:|---:|---:|---|
| `parcel-dhl-final.trx` | 173 | 173 | 0 | Completed |
| `containment-providers-final.trx` | 133 | 133 | 0 | Completed |

Both final TRX files have failed/error/timeout/aborted/notExecuted counters 0. Console execution times: 2.9720 minutes and 6.4586 minutes respectively. These final results are from the same final current-source build.

An earlier completed 169-case run had 167 passed / 2 failed / 0 skipped: `ConcurrentMeasurementWriters_OnlyOneImmutableParcelWins` (fixture ResetAsync read timeout) and `ConcurrentRegistration_OneFrozenIntentAndOneHttpPost` (connection timeout). Neither reached its body. Once the configured isolated PostgreSQL host became reachable, **both passed** in `parcel-concurrency-recheck.trx` (2 executed/passed, all other failure counters 0, Completed, 1.0931 minutes). Both also passed in the full final 173-case run. No infrastructure/deployment service was restarted to recover it. The final-named TRX was overwritten by the final green execution; the earlier failed counters are retained here, not counted as skipped or additional cases.

Distinct latest-case outcomes, without adding repeated executions:

| Class | Cases | Latest outcome |
|---|---:|---|
| ParcelMeasurementTests | 21 | 21 passed |
| DhlParcelEvidencePostgresTests | 16 | 16 passed in full final run |
| DhlOfficialContractCompatibilityTests | 82 | 82 passed |
| DhlRuntimeAuthorityTests | 5 | 5 passed |
| DhlGeographicCompatibilityTests | 3 | 3 passed |
| CarrierIntegrationAuthorityRemediationTests | 9 | 9 passed |
| ShipmentExternalRegistrationSafetyTests | 5 | 5 passed |
| ShipmentExternalRegistrationRequestedConsumerTests | 1 | 1 passed |
| CarrierIntegrationStateMachineConvergenceTests | 8 | 8 passed |
| CarrierIntegrationAuthorityCertificationTests | 10 | 10 passed |
| ShippingMethodsCarrierAuthorityTests | 5 | 5 passed |
| T10SettlementContainmentTests | 8 | 8 passed |
| ShipmentCancellationPostgresTests | 46 | 46 passed |
| AramexOfficialContractCompatibilityTests | 16 | 16 passed |
| MylerzOfficialContractCompatibilityTests | 7 | 7 passed |
| FedExResponseAdmissionTests | 14 | 14 passed |
| PosPermissionAuthorizationTests | 40 | 40 passed |
| BostaEndpointSurfaceTests | 10 | 10 passed |
| **Disjoint final total** | **306** | **306 passed, 0 failed, 0 skipped** |

Fresh tests cover valid persisted measurement reuse, positive/range/unit/decimal constraints, missing and non-finite measurements, catalog-vs-packed weight, single/multiple-package rejection, same-tenant access, immutable mutation/replay, customs gross/net/currency/value consistency, unresolved customs semantics, account/environment/destination changes, timeout without duplicate POST, required label/correlation and SQL concurrency. Existing provider, permission, cancellation, generic Clearing and financial containment assertions remain meaningful.

Intermediate failures are not hidden or added to final case totals: one new test constructor signature compile error was corrected; incomplete synthetic geographic/financial fixtures were corrected without weakening product guards; a currency mutation test was corrected to assert the existing immutable-order rejection; the claim-signature compatibility issue was fixed in production without editing cancellation tests. Final quote review added four route-product cases; an intermediate 173-case run passed 172 with one literal architecture failure because its `_product` source check matched the newly introduced error-code string, not a product authority dependency. The error code now uses the consistent rate namespace `DHL_RATE_UNAVAILABLE`; architecture assertions remain unchanged. The full final run passed all 173. Some earlier complete runs passed 37/37 and 132/132; other combined runs exceeded terminal deadlines and are not claimed as completed verification. They do not expand the final distinct count.

Final frontend commands:

```powershell
npx vitest run src/modules/shipping/parcelEvidence.test.tsx src/modules/shipping/cancellationSafety.test.tsx src/modules/shipping/shipmentActionAuthority.test.ts
npm run build
```

**3 files / 33 tests passed**, 0 failed; Vitest 4.1.4, duration 8.69s. New parcel tests: 8; existing cancellation/authority: 25. Production build succeeded, 5247 modules, 24.02s; existing large-chunk warning remains. No storefront source changed and no storefront business POST was used for verification.

## 10. Financial, lifecycle and preservation evidence

- Initial status/branch/HEAD, tracked diff statistics and complete untracked listing were inspected. Initial tracked diff already comprised **639 changed files, 3186 insertions and 53150 deletions**, including deleted tracked temporary artifacts and extensive cancellation, financial, tracking, migration and frontend work.
- Intentional source changes are enumerated below. Existing modified/untracked files were read as current authority and retained; no broad revert, reset, clean, stash, staging, commit or existing artifact deletion was performed by this task.
- Existing cancellation outcome/authority changes, Bosta collection/remittance/Manual COD work, tracking changes, three previously corrected cancellation certification assertions and reports are not attributed to this task and were not edited.
- Additional parcel/request properties and mapping preserve all preceding Shipment and model changes. Existing cancellation migrations and other untracked artifacts remain present. Built outputs and browser evidence are local development artifacts.
- Actual cancellation PostgreSQL tests verify registration/cancellation coordination in both lock-wait directions, only admitted confirmation finalization, tenant/actor checks and generic Clearing transfer rejection. The new PostgreSQL pipeline tests also assert no payment intent/journal creation.
- Manual COD implementation, refund lineage, remittance controls and financial posting source were not changed. DHL COD/collection/automatic remittance/pickup are not enabled. Existing permission/containment tests were rerun, not used to claim new positive financial certification.
- `git diff --check` passed; LF/CRLF notices from existing working-copy files are not whitespace failures. HEAD remains unchanged and no files were staged.

## 11. File-by-file intentional inventory

| File | Change |
|---|---|
| `PointOfSale.API/Controllers/AdminShipmentsController.cs` | Authenticated parcel read/save and canonical quote endpoints; existing cancellation behavior preserved. |
| `PointOfSale.API/Program.cs` | Register ShipmentParcelEvidenceService. |
| `PointOfSale.Core/Entities/Shipment.cs` | Three additive nullable evidence/request fields; legacy weight unchanged. |
| `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` | Text/fingerprint mappings. |
| `PointOfSale.Infrastructure/Shipping/ParcelMeasurement.cs` | New input/evidence/content records, decimal conversion, ownership/content restoration and request application. |
| `PointOfSale.Infrastructure/Shipping/ShipmentParcelEvidenceService.cs` | New tenant-isolated authenticated-actor evidence workflow, locking and immutability. |
| `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.ParcelEvidence.cs` | New canonical shipment quote, measured DHL request construction and durable context/fingerprint helpers. |
| `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs` | Integrate measurements and atomic request snapshot into existing registration/replay claim; DHL legacy delegation; measured customs gross; international containment and honest ambiguous state. |
| `PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs` | Add optional neutral PieceReference; no other-provider mandatory contract changes. |
| `PointOfSale.Infrastructure/Shipping/ShippingQuoteService.cs` | Withhold DHL on unqualified anonymous compatibility path. |
| `PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs` | Official measurement range/precision checks, admitted route-product quote filtering, actual piece customer reference, quantity-extended declaration, gross/net consistency. Existing label/correlation/service rejection retained. |
| `PointOfSale.Infrastructure/Migrations/20261011121839_ShipmentParcelRequestEvidence.cs` | Additive columns and immutable-evidence SQL trigger. |
| `PointOfSale.Infrastructure/Migrations/20261011121839_ShipmentParcelRequestEvidence.Designer.cs` | Generated migration model. |
| `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | Add evidence columns while retaining preceding model work. |
| `PointOfSale.Tests/Shipping/ParcelMeasurementTests.cs` | New 21-case decimal/unit/content/input regression suite. |
| `PointOfSale.Tests/Shipping/DhlParcelEvidencePostgresTests.cs` | New 16-case isolated SQL and real-adapter/mock HTTP pipeline suite. |
| `PointOfSale.Tests/Infrastructure/DhlOfficialContractCompatibilityTests.cs` | Seven fresh negative cases, four fresh route-product quote cases, and corrected unit-price × quantity assertion. |
| `PointOfSale.Web/src/modules/shipping/components/ParcelEvidenceForm.jsx` | Actual measurement/save/estimate UI. |
| `PointOfSale.Web/src/modules/shipping/pages/ShipmentDetailsPage.jsx` | Integrate form on actual DHL details screen. |
| `PointOfSale.Web/src/i18n/locales/en.json` | New localized measurement/validation/containment labels; existing translations preserved. |
| `PointOfSale.Web/src/i18n/locales/ar.json` | Arabic equivalents, with RTL section support. |
| `PointOfSale.Web/src/modules/shipping/parcelEvidence.test.tsx` | Eight EN/AR recovery, authority, immutable entry and estimate tests. |
| `tests/browser/dhl-parcel-evidence.js` | Intercepted real-application browser regression script; no secrets. |
| `.ai/reports/DHL_PARCEL_AND_CUSTOMS_EVIDENCE_INTEGRATION_CERTIFICATION.md` | This certification. |

## 12. Remaining activation and support requirements

1. Additive schema and rebuilt API are not activated in MAIN. Deployment/migration requires a future authorized phase; this phase deliberately leaves runtime intact.
2. `DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED` remains enforced by the existing integration authority. Real credentials/account ownership, account environment/endpoint, product entitlement, lane availability, physical limits, response correlation, label contents and billing currency/fees remain externally unverified.
3. International execution requires an admitted per-unit customs value basis, source currency, total-line net-weight meaning, complete manufacturer/origin evidence, purpose/invoice/incoterm and route/account-specific mandatory attributes. The current canonical path blocks international create even when a published snapshot exists.
4. DHL remains the same bounded P/H/N mapping and one-package/one-piece/PDF-label admission. Extra services, dangerous goods, insurance, COD, pickup, shipment cancellation, webhook admission, document formats and broader checkpoint mappings are not activated or inferred from the schema.
5. Operator parcel evidence is write-once in this initial workflow. Incorrect saved measurements require an explicit future controlled correction/versioning design; silently replacing facts under the same operation is prohibited.
6. No provider exactly-once/idempotent POST guarantee or automatic lookup was invented. Ambiguous external creation remains operator reconciliation work.

**Deliverable:** `C:\Users\fadya\source\repos\PointOfSale\.ai\reports\DHL_PARCEL_AND_CUSTOMS_EVIDENCE_INTEGRATION_CERTIFICATION.md`.

# Shopixy — End-to-End Shipping Carriers Forensic Audit

## 1. Executive verdict

**NOT END-TO-END OPERATIONALLY CERTIFIED. NO EXTERNAL CARRIER CURRENTLY PROVIDES ADMITTED, INDEPENDENTLY VERIFIED COD MONEY OR MERCHANT REMITTANCE AUTHORITY. CARRIER REMITTANCE REMAINS DISABLED.**

Shopixy registers six providers: Manual, Bosta, Aramex, Mylerz, DHL Express, and FedEx. Registration is a catalog fact. Aramex and DHL have executable logistics clients with selected mock-verified operations; Bosta has executable HTTP operations but material contradictions with current official creation, tracking, and webhook contracts. Mylerz is deliberately blocked. FedEx has real OAuth/transport and selected adapter operations but creation remains blocked by readiness and incompatible shared response admission. Manual has local fulfillment functionality and a separate previously certified Manual Fulfillment COD settlement operation.

The strongest newly re-verified operational findings are:

1. **Bosta creation:** always enables open-package delivery without required delivery images; official response state uses `{code,value}`, which the adapter cannot read. Tracking has the same state mismatch. Official webhooks cannot pass the current parser, and its separate `COD_COLLECTED` branch is unreachable.
2. **DHL tracking:** official `status` is a string; scans use `date`, `time`, `typeCode`, and an array of `serviceArea`. Shopixy expects a status object, `timestamp`, `statusCode`, and one service-area object. Its tests use the application's assumed shape. Its rate mapper also uses `currencyType` (BILLC/PULCL/BASEC) as a monetary currency instead of `priceCurrency`.
3. **Scheduled tracking:** the job creates fresh scopes without establishing tenant context. Mandatory EF tenant filters prevent tenant discovery and shipment reads. Even after that is corrected, missed intermediate statuses, terminal failed states, and exclusion of delivered shipments prevent reliable catch-up.
4. **Admin cancellation:** an accessible admin service locally marks carrier-managed shipments Cancelled without invoking the carrier cancellation authority. Its UI says it will notify the carrier. This can report a cancellation that never occurred externally.
5. **Financial boundary:** normalized carrier responses expose a collection boolean/time, not actual collected amount/currency/provenance. Expected `Shipment.CodAmount` is not observed money. No carrier adapter retrieves a final merchant receipt with permanent identity, complete membership, gross/fees/net/currency/date/destination, or reversals.

**The proposed Manual Carrier Remittance policy is safely implementable in principle, with a dedicated reviewed authority and atomic operation.** Authorized operator attestation of *actual receipt* can be the accepted business evidence; an electronic carrier statement need not be the sole evidence. This is a new business authority, not proof that an existing carrier API remits money, and not permission to reuse the Manual Fulfillment executor or a generic Clearing transfer. Collection must already be independently qualified and Posted. The proposed workflow is currently missing.

### Audit identity and limits

- Date: 2026-10-09; MAIN/PRIMARY Windows working copy only: `C:\Users\fadya\source\repos\PointOfSale`.
- HEAD: `256339621c0ea49f3602097cdacdf89ffe71754f`. Findings concern the current working tree including existing uncommitted Manual/prerequisite/Treasury work.
- Read completely: `.ai/reports/CARRIER_COD_REMITTANCE_FORENSIC_AUDIT.md` (471 lines), `COD_SETTLEMENT_PREREQUISITES_CERTIFICATION.md` (337), and `MANUAL_COD_SETTLEMENT_FINAL_CERTIFICATION.md` (407). Their claims were rechecked against current source; their financial test counts are historical, not fresh audit results.
- No subagents, application edits, migrations, flag changes, provider login, live carrier operation, collection/settlement/refund execution, synthetic merchant fixture, or direct merchant database mutation. Public documentation and WSDL **metadata GETs** were retrieved; no carrier operation was invoked.
- Fresh verification: source/contract inspection, isolated HTTP mocks and non-financial InMemory tests, source architecture/containment tests, mocked frontend tests, localhost health GET, authenticated AhramStore read-only API/UI checks. POS login was explicitly authorized; supplied credentials are omitted from this report. Storefront login was unnecessary.
- No fresh backend build: tests used existing `ManualCodVerification` binaries. Discovery and execution are confirmed for those binaries; current-source architecture tests read the working tree. Binary freshness against every current source file is not independently certified. No positive financial E2E or live external operation is certified here.
- Repository paths below are exact; symbols and current line ranges identify evidence. Provider URLs are public documentation, not evidence of credentialed execution.

## 2. Complete discovered inventory

### 2.1 Registered providers and actual AhramStore methods

`PointOfSale.Core/Constants/CarrierCodes.cs:16–54` lists exactly `manual`, `bosta`, `aramex`, `mylerz`, `dhl`, `fedex`. `PointOfSale.API/Program.cs:983–1004` registers `ManualCarrierDefinition`, `BostaAdapter`, `AramexAdapter`, `MylerzAdapter`, `DhlAdapter`, `FedExAdapter`, plus `IFedExOAuthClient` and `IFedExTransportClient`. `Infrastructure/Shipping/ShippingCarrierRegistry.cs` discovers these DI adapters. No additional production carrier adapter was found. Test Fake/Recording/Deterministic adapters are not providers.

Additional **shipping methods**, rather than carriers, exist as tenant-configurable `ShippingMethod` rows. `API/Services/Platform/TenantRegistrationService.cs:384–440` seeds Standard Shipping, Express Shipping, and Free Shipping with null carrier code (normalized Manual). They have different merchant pricing, not three external integrations. No separate executable local-pickup/collection-point provider was established.

Fresh AhramStore authenticated GETs, all 200:

| Read | Observed result |
|---|---|
| `/api/admin/shipping/supported-carriers` | All six providers; Manual ProductionReady, Bosta/Aramex/DHL Partial, FedEx NotCertified, Mylerz Unavailable |
| `/api/admin/shipping/carrier-accounts` | Empty |
| `/api/admin/carriers` | Six catalog/configuration projections, all `isEnabled=false`; this projection alone is not proof of six persisted configurations |
| `/api/ShippingMethods` | Three active Manual methods below |
| `/api/admin/shipments?page=1` | One Manual Pending, non-COD, uncollected shipment; no external shipment |

| AhramStore persisted method projection | Arabic name | Carrier/service | Merchant pricing |
|---|---|---|---|
| Standard Shipping | شحن عادي | manual / standard | Base10, perKg0 |
| Express Shipping | شحن سريع | manual / standard | Base25, perKg2 |
| Free Shipping | شحن مجاني | manual / standard | Base0, perKg0, threshold100 |

These are this tenant's observed methods, not an enumeration of every tenant's database rows. The complete repository registration/catalog inventory is six providers; merchants may name additional rows without adding adapters.

### 2.2 Definitions, authority services, persistence and derivative stores

| Surface | Exact source and role |
|---|---|
| Catalog definitions | `Infrastructure/Shipping/Adapters/{Bosta,Aramex,Mylerz,Dhl,FedEx}CarrierDefinition.cs`; `CarrierDefinition` base is in `BostaCarrierDefinition.cs`. Capability descriptions are not executors. `ManualCarrierDefinition.cs` itself implements the local no-op adapter. Some definition pickup claims exceed implemented operations. |
| Runtime capability/readiness | `Infrastructure/Shipping/CarrierCapabilityRegistry.cs`, `BuildCapability:83–183`; `CarrierIntegrationAuthority.cs`, `ResolveCredentialsAsync`, `BuildReadinessAsync`, `ResolveWebhookTrustAsync`; `CarrierCredentialEncryptionService.cs`. |
| Carrier operations | `Infrastructure/Shipping/CarrierShipmentService.cs`: `CreateShipmentForOrderAsync`, `ExecuteCarrierForExistingShipmentAsync`, `CancelShipmentAsync`, `SyncTrackingAsync`, `ProcessWebhookInternalAsync`, `GetLabelAsync`. |
| Local shipment authority | `API/Services/Shipping/ShipmentService.cs`, `CreateShipmentAsync`, `UpdateStatusAsync`, `ReconcileShipmentAuthorityAsync`; `API/Services/Admin/AdminShipmentService.cs`. |
| Method admission/pricing | `API/Services/Storefront/ShippingMethodService.cs`, `ApplyCalculatedPrices`, warehouse/stock/tenant currency/readiness qualification; `WarehouseResolutionService.cs`; `API/Services/Checkout/CheckoutService.cs`, order-bound method/fulfillment snapshots and final checkout revalidation; `API/Services/CommerceHub/CommerceHubShippingService.cs`, vendor-scoped method selection. |
| Legacy provider quotes | `Infrastructure/Shipping/ShippingQuoteService.cs`: compatibility display quotes, fixed rates/markup/free threshold; not canonical checkout admission. |
| Administration facade | `API/Services/Admin/ShippingAdministrationApiService.cs`; `Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`; actor/audit, credential references, environment, policies, service levels and warehouse assignments. Facade readiness comes from canonical integration authority. |
| Persisted logistics | `Core/Entities/ShippingMethod.cs`, `Shipment.cs`, `CarrierConfiguration.cs`, `ShippingWebhookLog.cs`; `Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`: `CarrierAccount`, `CarrierCredentialReference`, `CarrierWarehouseAssignment`, `CarrierAccountPolicy`, `CarrierServiceLevelEnablement`, `CarrierConnectionTestRecord`, `CarrierAdministrationAudit`. |
| Address/customs snapshots | Shipment origin/destination address-admission/catalog/place snapshots; carrier geographic mapping release/account/environment snapshot; shipment-owned customs evidence. `CarrierShipmentService.ResolveOrRestoreShipmentOriginAsync:3813…`, geographic/customs resolution before adapter invocation. |
| Financial persistence | `PaymentSettlementBatch`, `PaymentSettlementOrder`, `OrderRefund`/lines, order payment and posting facts; accounting outbox, immutable journal and LedgerLines; MassTransit EF event/inbox/outbox machinery. No dedicated carrier-receipt/economic evidence entity was found. |
| Derivative stores | `Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs`; `Infrastructure/MongoDB/Documents/ShippingMethodDocument.cs`, `ShippingMethodReadRepository.cs`; `Infrastructure/Redis/Services/ShippingMethodCacheService.cs`; `OrderProjectionConsumer.cs` and Affiliate projections. Tenant/version-qualified presentation, not financial authority. |

`Shipment` persists expected COD, status/timestamps, order/provider/tracking/external request IDs, registration attempts/errors, labels, raw create/tracking responses, origin/destination and geographic evidence. It has no immutable financial account/environment binding or actual-collected-money fields. Geographic account scope is not a money-receipt account identity.

### 2.3 API surface

| Controller | Registered route and operations |
|---|---|
| `ShippingMethodsController.cs` | `/api/ShippingMethods`: GET list/ID; POST create; PUT ID; DELETE ID; POST `calculate`, `sync`. |
| `AdminCarrierConfigController.cs` | `/api/admin/carriers`: GET `supported`, list/code; PUT code; POST code/`toggle`, code/`validate`; DELETE code. Validation can contact a provider; not invoked. |
| `AdminShippingAdministrationController.cs` | `/api/admin/shipping`: GET supported-carriers, carrier-accounts/list/ID/health, audit; POST account create, connection-tests, activate/suspend/disable/retire, health/evaluate; PUT account, credential-reference, environment, warehouse-assignments/ID, policies/domestic/international, service-levels/code. Only GETs used. |
| `ShipmentController.cs` | `/api/shipments`: POST intent; GET ID; PUT ID/status. Tenant context is server-resolved; generic status mutation rejects carrier-managed shipments. |
| `AdminShipmentsController.cs` | `/api/admin/shipments`: GET list/details; PUT ID/status and ID/tracking; POST ID/cancel. Cancellation defect is separate from the protected generic status writer. |
| `ShippingWebhooksController.cs` | `/api/webhooks/shipping/{carrierCode}` plus fixed bosta/aramex/mylerz/dhl POSTs; GET health. Anonymous ingress uses request/trust pipeline. Only Bosta capability allows an external webhook; routes alone do not enable the others. |
| `StorefrontShippingMethodsController.cs` | `/api/storefront/shipping-methods`: GET; POST preview/select. Backend cart/customer/session/warehouse/method admission. |
| `StorefrontShippingQuotesController.cs` | `/api/storefront/shipping/quotes` POST; anonymous tenant-resolved legacy quote display. Not called: it could contact providers. |
| `StorefrontShipmentController.cs` | `/api/storefront/orders/{orderId}/shipment` GET, StorefrontJwt, tenant/customer ownership; guest tracking is separate session/token-bound read in `StorefrontOrdersReadController.cs`. |
| `PaymentLifecycleController.cs` | Manual collection, dedicated Manual settlement GET/POST/GET; legacy carrier COD/gateway settlement routes always409. No Manual Carrier Remittance route. |

Searches of API controllers/callers found **no public carrier tracking-sync or label-retrieval endpoint** invoking `ICarrierShipmentService.SyncTrackingAsync`/`GetLabelAsync`; sync is a background service operation, labels are stored-response reads in source. No pickup scheduling/cancellation or return-booking HTTP workflow exists. No remittance webhook/consumer/executor ingress exists. Fulfillment action APIs are distinct operational authorities, not arbitrary carrier status setters.

### 2.4 Events, consumers and background jobs

- `Core/Events/ShipmentEvents.cs`: shipment creation/external registration requested, status changed, ReadyForPickup, PickedUp, InTransit, OutForDelivery, Delivered, Failed, Cancelled, Returned, COD collected; `ShippingMethodEvents.cs`: method create/update/delete.
- `API/Consumers/OrderShipmentOrchestrationConsumer.cs`: order-confirmation/payment-accounting-driven durable shipment orchestration after committed order authority.
- `Infrastructure/Consumers/ShipmentExternalRegistrationRequestedConsumer.cs:31–107`: executes existing SQL intent post-commit; selected retryable codes throw; successful/failed/reconciled telemetry. Does not solve provider idempotency.
- `ShipmentCodCollectedConsumer.cs:27–71`: passes only tenant/order/shipment locators to canonical collection admission; definition has concurrency8 and 1/5/15-second retries.
- `OrderDeliveredEventConsumer.cs` contains `FulfillmentDeliveredEventConsumer`: delivery-only observation, explicitly no COD collection.
- `OrderStatusSyncConsumer.cs` and `API/Services/OrderStatusSyncService.cs`: SQL shipment lineage bridge; Returned does not itself refund. Shipment Created/Shipped/Delivered/Cancelled notification consumers provide customer notifications through `ShipmentNotificationConsumerBase.cs`; these are not finance evidence.
- `OrderProjectionConsumer.cs`, `ShippingMethodProjectionConsumer.cs`, Affiliate lifecycle/settlement consumers and registered marketplace delivery bridges derive downstream visibility/domain decisions, not carrier remittance proof.
- `Infrastructure/Shipping/BackgroundJobs/TrackingSyncJob.cs`: Hangfire `shipping-tracking-sync`, every30 minutes (`API/Jobs/HangfireInitializationHostedService.cs:197–200`), 15-minute age, 50/tenant, five concurrent scopes; tenant-scope defect below.
- `API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs`: every30 seconds, registration age1 minute, batches25, up to4 passes; missing intent/pending registration/SQL authority repair. Does not reclaim abandoned webhook Processing logs or settle carrier money.
- Accounting outbox processor/recovery posts from current SQL source; MassTransit transactional outbox emits committed events. COD foundation catalog/bridge/orchestrator classes under `API/Services/Payments/Cod/` construct reference-domain outcomes but provide no external receipt verifier or carrier SQL settlement executor.

## 3. Provider-by-provider operation matrix

Legend: **V** = implemented and positively verified within stated local/mock/read-only scope; **P** = implemented, partially verified; **B** = source present but unreachable/blocked; **S** = stub/no-op/placeholder; **M** = missing; **C** = contradicts fresh official contract; **U** = external behavior/credentials/environment unknown or not exercised. V never means a live external or financial E2E success. Details/source/test references in sections4–9 qualify every cell.

| Operation | Manual | Bosta | Aramex | Mylerz | DHL Express | FedEx |
|---|---|---|---|---|---|---|
| 1 Credentials/tenant/account/environment | V local; no external account | P/U tenant config; cities test not account binding | P/U ClientInfo/account; no live validation | B/U unconditional gate | P/U Basic/account; validation local-only | P OAuth mocks; B create readiness; U entitlement |
| 2 Availability/pricing | P merchant method rules; V live catalog reads | S fixed30/35 and50/55 EGP; U actual lane | P remote rate; U lane/product/COD entitlement | B dormant quote | C rate currency; P endpoint; U actual product | P typed availability mocks; M quote execution; B canonical readiness |
| 3 Create/idempotency | P local SQL intent; S adapter | C required images/state; P HTTP route; M recovery lookup | P mock create; M provider dedupe/lookup | B dormant create | P mock create/customs; M dedupe/lookup | P direct mock create; B shared admission/readiness; unsafe POST retry |
| 4 Matching | P tenant/order snapshots | P SQL; M response-to-request tracking/account binding | P keyed tracking/optional create echoes | B unverified assumptions | P AWB match; U account provenance | P internal response consistency; M requested-number match |
| 5 Label/AWB | M external label | M AWB generation client; tracking ID is not PDF | P creation label; M PrintLabel executor/public read | B dormant data | P base64 creation label; M operational retrieval UI | P direct label admission; B shared MIME/status; S standalone |
| 6 Tracking progression | P local actions; S external tracking | C official state/history shape | P admitted mock shape; U full vocabulary; shared job blocker | B | C official shape/vocabulary | P direct transport mocks; shared scope/progression defects |
| 7 Cancellation | P local; S adapter | P DELETE route; admin bypass/ambiguity risk | B explicit unsupported shipment cancellation | B | B explicit unsupported shipment cancellation | S fail-closed; official operation not implemented |
| 8 Pickup schedule/cancel | M external operation | M despite official endpoints | M despite official CreatePickup/CancelPickup | M/B hypothetical | M; create explicitly `isRequested=false` | S schedule; M cancellation; DTOs only |
| 9 Delivery confirmation | P canonical fulfillment; no fresh delivery mutation | C official webhook/track; legacy mock vocabulary only | P delivery status; M proof-of-delivery execution | B | C tracking; M POD operation | P normalized scans; M SPOD execution |
| 10 Returns/failed delivery | P local return/failure; refund separate | C type-specific states unparsed; M CRP/exchange booking | P normalized status; M return booking | B | C scan meanings; M return booking | P limited admitted statuses; M return creation |
| 11 COD mapping/collection | P manual admission; prior certification only | P outbound expected cod; C webhook; M admitted collection money | P CODS/USD request; B true collection flag | B | M COD request/collection money | M COD service/money/collection |
| 12 Webhook auth/replay/recovery | M/no ingress permitted | P shared-secret pipeline; C parser; M freshness/lease recovery | B capability false | B capability false/parser gate | B capability false; assumed parser dormant | B capability false/null parser |
| 13 Fulfillment/inventory/payment events | P common SQL lifecycle; separate collection | P common chain; blocked official inputs | P common chain; no collection authority from current adapter | B external inputs | P common chain; blocked official tracking | P common chain for valid existing shipment; creation B |
| 14 Timeouts/retries/jobs/crash | P local replay/outbox | P finite HTTP; B effective scheduler; M automatic ambiguous recovery | P finite HTTP; B scheduler; M ambiguous recovery | B HTTP | P finite HTTP; B scheduler; M ambiguous recovery | P OAuth/retry mocks; unsafe create retry; B scheduler |
| 15 Admin/readiness/errors/visibility | V method reads; P authority UI | P config blockers; S cities identity; missing detail errors | P config; U remote trust; facade drift | V blocked catalog locally; B operations | P misleading local validation; missing detail errors | V NotCertified catalog; B activation usefulness |
| 16 EN/AR/responsive UX | P:12 sampled admin layouts; partial Arabic | P shared screens, no live provider rows | P shared screens, no live provider rows | P catalog/blocked UI only | P shared screens, no live labels | P catalog/blocked UI only |

**No external provider operation is V for live execution.** Actual AhramStore external configurations are disabled. The executable partial operations and the contract contradictions remain findings even though that tenant currently cannot invoke them through creation admission.

## 4. Fresh official external-contract evidence

All URLs below were retrieved/attempted on2026-10-09 without carrier credentials. Public documentation is distinct from a live merchant contract. No documentation examined establishes provider-side exactly-once shipment creation or financial finality unless explicitly described below. Unknowns stay unknown.

### 4.1 Bosta

| Official URL/artifact | Established behavior | Current source comparison |
|---|---|---|
| https://docs.bosta.co/api/api.yaml — OpenAPI3.0.1, info.version2.0.0; no release date | Authorization API key/Bearer; create `POST /deliveries?apiVersion=1`, business GET tracking and DELETE terminate; districtId OR districtName+cityId; `_id`, trackingNumber, `state:{code,value}` in published create/tracking examples | `BostaAdapter.CreateShipmentAsync:90–279`, tracking322–430, cancel281–320 use official routes and address IDs. `BostaDeliveryData.GetStateName:788–807` accepts string or object.name only. Published code/value state returns empty and fails admission; `_id` is metadata rather than preferred external reference. No businessReference/request-number/account response check. |
| https://docs.bosta.co/docs/how-to/create-your-first-delivery | Type10 delivery,15 cash collection,30 exchange,25 CRP; open-package=true requires deliveryImages with1–10 URLs; missing images error2150; COD limit30000EGP. Prepaid escrow is part of COD, not extra money | `CreateShipmentAsync:116–147` always Type10, expected cod (or0), Parcel/SMALL, `AllowToOpenPackage=true`; `BostaCreateDeliveryRequest` has no images, goodsInfo, uniqueBusinessReference, escrow or return booking. Required-image contradiction is direct. Type10/prepaid/zero-COD market semantics require reviewed handling; no blanket live acceptance assumed. |
| https://docs.bosta.co/docs/how-to/get-delivery-status-via-webhook | Lowercase `_id`, trackingNumber, numeric state/timeStamp/cod, type, optional isConfirmedDelivery; state45 delivered; cod explicitly customer-collected amount only in delivered state; dashboard Authorization/custom header; webhook occurs on status changes, not initial creation | `ParseWebhook:526–570` requires exact uppercase EventType/TrackingNumber and string event. Rejects official payload. COD_COLLECTED is not admitted by `TryMapBostaWebhookEvent:595–611`, so true collection impossible. Event time is server-now; documented amount/type/POD/reference ignored. Documentation type table calls trackingNumber string but examples use number; future parsing must explicitly handle this discrepancy, not silently misidentify events. |
| https://docs.bosta.co/docs/how-to/create-your-first-pickup | POST/pickups; date/location/contact/parcels; cutoffs/holiday/date rules; no two requests for same date/location | Missing executor. Definition's assumption of automatic daily pickups is not proof of booking. |
| https://docs.bosta.co/docs/how-to/print-awbs/ | POST/deliveries/mass-awb, trackingNumbers or IDs, A4/A6, ar/en; <=50 returns base64PDF, larger requests email export, max1000; finished/CRP/cash-collection not printable | Missing client and operational retrieval. Bosta creation does not populate a label. An AWB/reference string is not a generated PDF. |
| YAML `/businesses/{id}/transactions`, operationId Businesstransactions | Bearer GET returns `data.unpaidCodAmount`, collected but **not yet transferred** aggregate | Not implemented. No completed payout identity, member list, fees/net/currency/receipt date/destination/finality. PaymentInfo/BankInfo are configuration, not receipt evidence. |

Production base `https://app.bosta.co/api/v2` agrees with official how-to pages. YAML server is HTTP, but Shopixy correctly uses HTTPS. Hardcoded staging `https://staging-api.bosta.co` remains externally unverified. `ValidateCredentialsAsync` GET/cities reports generic Bosta Account on any2xx; it does not prove merchant/account or finance entitlement. `GetQuoteAsync:40–88` always returns synthetic standard30/35EGP and express50/55EGP; names/days are local assumptions, same_day catalog entry has no quote, and creation does not select the quoted service.

Official state meanings must be type-aware: code41 for SEND is out-for-delivery, not necessarily initial pickup;47 Exception can be retryable NDR,46 returned-to-business differs by order type,48 Terminated and49 Cancelled differ. Current uppercase aliases do not prove full numeric vocabulary support. No published retry interval/freshness/unique state-change ID guarantee was established on these pages. `_id` identifies the delivery, not a unique transition; it must not become a permanent event ID that collapses all later statuses.

### 4.2 Aramex

Freshly accessible official service metadata (ShippingAPI.V2 path; namespace ShippingAPI/v1, service1.0; no publication date):

- https://ws.aramex.net/ShippingAPI.V2/Shipping/Service_1_0.svc?singleWsdl
- https://ws.aramex.net/ShippingAPI.V2/Tracking/Service_1_0.svc?singleWsdl
- https://ws.aramex.net/ShippingAPI.V2/RateCalculator/Service_1_0.svc?singleWsdl
- https://ws.aramex.net/ShippingAPI.V2/Location/Service_1_0.svc?singleWsdl

| Operation/contract | Source comparison and limit |
|---|---|
| ClientInfo | WSDL requires UserName,Password,Version,AccountNumber,AccountPin,AccountEntity,AccountCountryCode. `AramexAdapter.BuildClientInfo:859–875` supplies these, v1.0, Source24. Account-specific entitlement remains U. |
| CreateShipments | Official Shipment has references, Shipper/Consignee, ShippingDateTime, DueDate, Details, ForeignHAWB; Money has Value/CurrencyCode. ProcessedShipment has ID/references, **per-shipment HasErrors/Notifications**, ShipmentLabel. `CreateShipmentAsync:188–447` sends Details/CODS/CashOnDeliveryAmount, one shipment, validates optional echoed references, parses nested label. Root errors checked; response is reused `AramexShipment` model and per-shipment HasErrors/Notifications are not qualified. No create idempotency/recovery lookup. |
| CalculateRate | WSDL request includes addresses/ShipmentDetails, optional preferred currency; response TotalAmount is Money. `GetQuoteAsync:56–186` parses amount and ISO currency, but COD quote sends PaymentType=C without CODS/amount; create sends PaymentType=P plus CODS. Fee equivalence is not verified. Domestic standard/express both mapOND (`MapServiceLevel:915–930`); local estimated days do not establish distinct purchased services. |
| TrackShipments | WSDL keyed TrackingResults and TrackingResult WaybillNumber,UpdateCode,UpdateDateTime,UpdateTimeZone. `GetTrackingAsync:466–601` requires exact outer key and admitted timestamps/statuses; inner WaybillNumber and timezone are not independently admitted. Unknown history code rejects the whole response. WSDL string fields do not define SH code semantics. |
| PrintLabel/CreatePickup/CancelPickup | Published Shipping WSDL operations present; not executed by Shopixy. No CancelShipment operation in that published operation list; adapter correctly fails closed for shipment cancellation. |
| Delivery/returns/COD | WSDL HasShipmentBeenDelivered provides boolean delivery, not money; not executed. Current COD substring checks follow a whitelist containing no COD codes (`542–587`, `747–781`, `801–842`); true collection branch is unreachable. `COLLECTED` maps parcel pickup, not cash. Webhook capability false. |

Official manuals/developer-center attempted, all403 in this audit:

- https://www.aramex.com/ag/en/developers-solution-center/aramex-apis
- https://www.aramex.com/docs/default-source/resourses/resourcesdata/shipping-services-api-manual.pdf
- https://www.aramex.com/content/uploads/109/232/42194/shipping-services-api-manual.pdf
- https://www.aramex.com/content/uploads/109/232/42254/shipments-tracking-api-manual.pdf

Consequently prior internal documents are not fresh official evidence for USD-only COD, all validation lengths, product codes, complete status vocabularies, JSON WCF date conventions, webhook guarantees or sandbox semantics. Current source **does** reject non-USD COD (`ValidateCreateShipmentRequest:1084–1087`), including Egyptian EGP orders; WSDL Money.CurrencyCode is unconstrained string, so the global USD-only business claim is **unverified**, not inferred false from XSD alone. Production and sandbox constants use `ws.aramex.net` / `ws.sbx.aramex.net`; no credentialed sandbox verification performed. No admitted remittance/statement/bank receipt contract was established.

### 4.3 DHL Express / MyDHL

Fresh official references:

- https://developer.dhl.com/api-reference/dhl-express-mydhl-api?lang=en&language_content_entity=en
- https://developer.dhl.com/sites/default/files/2026-10/dpdhl-express-api-3.3.3.yaml — OpenAPI3.0.0, version3.3.3, release2026-10-04.

Internal DHL report/source comments cite3.3.1 (2026-06-28); these are superseded for current-contract comparison. Official documentation establishes preemptive Basic Auth, account-based rates, production `/mydhlapi`, test `/mydhlapi/test`, test500 invocations/day. Quotes are indicative, not actual deductions. Test tracking supports listed fixed waybills; newly created test shipments are not necessarily trackable.

| Operation | Fresh official comparison |
|---|---|
| Rate POST/rates | Correct route and Basic/account request in `DhlAdapter.GetQuoteAsync:54–190`. Official products.totalPrice entries carry currencyType=BILLC/PULCL/BASEC and **priceCurrency=GBP/USD/etc**. `DhlPrice:1401–1407` omits priceCurrency and mapper150 returns CurrencyType as money. Missing price falls back0, missing currencyUSD. Contract contradiction; real quotes cannot be trusted as currently mapped. Request uses one package with fabricated30x20x10 dimensions and estimated days, not complete actual package details. |
| Create POST/shipments | Request/response surface broadly matches shipping contract; `CreateShipmentAsync:193–469` validates one package/positive weight, requires admitted customs for international goods, requests encoded PDF label. Official supports COD services, but this DTO has no COD value-added service mapping. Pickup.isRequested=false. No provider dedupe/lookup. Creation/mock success does not certify delivery, money or actual fee. |
| Track GET/tracking | Official `supermodelIoLogisticsExpressTrackingResponse`: status STRING Success; events date/time/typeCode, serviceArea ARRAY. `DhlShipmentTracking:1500–1512`/`DhlTrackingEvent:1476–1488` instead expect status object, timestamp/statusCode, one serviceArea. Valid documented response can throw deserialization and return TRACKING_ERROR; absent status object does not cure ignored event fields. |
| Scan vocabulary | Official sample AF=arrived sort facility, PL=processed, RR=customs clearance updated, CR=clearance complete, AR=arrived delivery facility, WC=with courier, OK=delivered. `TryMapDhlStatus:809–848` maps AF/PL ReadyForPickup, RR Returned, CR OutForDelivery, AR PickedUp. These are direct contradictions even after shape fix. RR must not trigger a physical return/refund inference. |
| Cancel/pickup | Official DELETE/pickups/{dispatchConfirmationNumber}, POST/pickups, PATCH pickup; no shipment-delete operation established. Adapter explicit shipment cancellation failure is correct; pickup operations missing. |
| Labels/POD/returns | Create documents parsed; official separate image/POD/shipment services are not implemented operationally. Local GetLabelAsync reads stored fields only. No return booking. Label-bearing create may succeed without a label: shared admission treats labels optional. |
| COD/finance | Official portal release2.0.0 mentions support for COD shipments; this corrects any broad claim that DHL Express cannot provide COD. Shopixy maps none of it, parses no customer-collected money, and has no merchant remittance evidence. Other DHL divisions' products are not this integration. |

`ValidateCredentialsAsync:622–643` returns success based only on three strings. Canonical capability accurately labels LocalOnly, but readiness can subsequently consider its successful validation; this is not remotely proven authentication, account ownership or ship entitlement. DHL webhook capability is false; dormant assumed HMAC/parser does not establish an official enabled webhook integration.

### 4.4 Mylerz

https://www.mylerz.com/ was retrieved: logistics/fulfillment/merchant-dashboard marketing and merchant link https://mylerz.net/login. No versioned public API schema, auth, hosts, sandbox, webhook/retry, collected-money or remittance receipt contract was established. No merchant portal login attempted. This is a bounded public-document discovery gap, not a claim private vendor documentation cannot exist.

`MylerzAdapter.OfficialPublicContractUnavailable:713 => true` gates quote62, create183, cancellation357, tracking417, validation511, service-levels564 and webhook603. Dormant Bearer/custom-header/HMAC code, prospective DTOs and `CodCollected` fields cannot execute and are not external evidence. Hosts `api.mylerz.com/v2` and `sandbox-api.mylerz.com/v2` remain unverified assumptions. Obtain vendor-issued versioned package before any operational implementation.

### 4.5 FedEx

Fresh official v1 operation documentation (no page publication date established):

| Exact URL | Established contract / current implementation |
|---|---|
| https://developer.fedex.com/api/en-us/catalog/authorization/v1/docs.html | OAuth2 Bearer, form client_credentials/csp_credentials/client_pc_credentials, client ID/secret and optional child credentials, normally3600-second token. `FedExOptions.cs` contains real `FedExOAuthClient` (not a separate file), cache/locks/skew/refresh; tested with mocks. Adapter chooses client_credentials. Credentials enable project APIs, not arbitrary finance authority. |
| https://developer.fedex.com/api/en-us/catalog/ship/v1/docs.html | Account, shipper/recipient/service/package/payment/pickup type/weights/label details; returns tracking+labels; cancel, return shipments and tags described. `FedExAdapter.CreateShipmentCoreAsync:289–352` invokes real transport; BuildCreateShipmentRequest402–441 omits COD and customs/special-service mapping, uses one line item/USE_SCHEDULED_PICKUP. Direct result InitialStatus=Pending and label format=application/pdf clash with shared valid statuses/formats. |
| https://developer.fedex.com/api/en-us/catalog/track/v1/docs.html | Up to30 tracking numbers, detailed scans/status/return info; SPOD is distinct, signature requires matching shipper account; tracking availability may lag24h. `GetTrackingCoreAsync:354–400` admits internally consistent identifiers, but never binds response to requested tracking number. No SPOD executor or collected-money output. |
| https://developer.fedex.com/api/en-us/catalog/service-availability/v1/docs.html | Services/transit and optional special-service capability depend on lane/account/date/commodities; address validity is not blanket delivery availability | Typed adapter overload138–209 calls `/availability/v1/transittimes`; actual mock verification. Generic overload fails closed; no normal API/checkout caller established for typed endpoint. |
| https://developer.fedex.com/api/en-us/catalog/address-validation/v1/docs.html | Suggested normalization/classification, not deliverability; country coverage limited; up to100 addresses | Typed adapter overload221–287 executes `/address/v1/addresses/resolve`, admitted provider evidence; no proof of Egyptian coverage or merchant shipment readiness. |
| https://developer.fedex.com/api/en-us/catalog/rate/v1/docs.html | Estimates with account/list/preferred currency and surcharges; rate does not itself validate service availability and may differ from invoice | No executable quote adapter; DTO/capability catalog is not rating execution. |
| https://developer.fedex.com/api/en-us/catalog/pickup/v1/docs.html | Availability/cutoff/access time, confirmation/location/date, create/cancel, Express/Ground rules; pickup != shipment cancellation | SchedulePickupAsync fails closed; cancellation missing. Options/constants/DTO validation and generic transport support do not establish application pickup operation. |
| https://developer.fedex.com/api/en-us/guides/ratelimits.html | Organization/project/capability quotas; project1400/10s example limit, Track default100K/day;429 may mean retry10s or midnightGMT; OAuth IP thresholds can yield403/10min | `FedExTransport.SendJsonAsync:67–182` immediately retries408/429/5xx/network/timeouts; no Retry-After or quota-aware delay. Finite is not rate-limit-compliant. |
| https://developer.fedex.com/api/en-us/guides/sandboxvirtualization.html | Predefined virtual replies may not vary with inputs; shipping/track/rate/availability/pickup/address are virtualized; token still required | A sandbox success cannot prove real lane/address/membership, actual collection or remittance. No sandbox operation executed. |

Official Ship documentation describes COD/ECOD services, including contract-dependent electronic deposits and non-electronic checks in specific markets. These service descriptions **do not publish an authenticated per-remittance read identity/finality/member/G/F/N receipt interface used by Shopixy**, nor establish entitlement in Egypt. No claim that FedEx lacks every remittance product is made.

`CarrierCapabilityRegistry` marks NotCertified and has no FedEx credential descriptor/service levels. Creation through canonical readiness is blocked. Real direct adapter transport code contradicts old comments that all HTTP operations are unimplemented. Independently, `CarrierShipmentService.IsValidInitialCarrierStatus:3436–3441` excludes Pending and `IsValidOptionalLabelFormat:3406–3415` excludes MIME application/pdf, so direct create success cannot be admitted into the shared registration pipeline unchanged. `GetShipmentByCarrierRequestIdAsync` returns null. Quote/cancel/credential validation/standalone-label/pickup remain fail-closed stubs; webhooks null/capabilityfalse.

### 4.6 Manual

No external provider contract applies. Local `ManualCarrierDefinition.CreateShipmentAsync`, cancellation and validation are intentional no-ops; quote/tracking fail NOT_SUPPORTED and webhook null. Merchant method pricing/local fulfillment authority and dedicated Manual COD accounting are actual application operations, not electronic carrier confirmations. No external return/pickup/AWB authority is implied.

## 5. Exact operational lifecycle, matching and recovery

### 5.1 Intent → external create → local admission

Canonical storefront method selection is tenant SQL method → `FulfillmentWarehouseId` → opaque `WarehouseResolution`; server cart/subtotal/weight/stock/currency are authority. `ShippingMethodService.ApplyCalculatedPrices:561–571` uses merchant BaseRate/PerKgRate/free threshold. Carrier quote display is a separate path; `ShippingQuoteService.BuildFixedRateQuotes:305–329` omits currency and inherits USD, and quotes from different currencies are sorted numerically together. Neither quoted charge nor merchant charge proves a carrier's actual fee deduction.

`ShipmentService.CreateShipmentAsync` qualifies tenant/order/method, immutable fulfillment ownership, current warehouse and order operational financial readiness. Carrier intent is PendingExternalRegistration; events are staged before commit. `ExecuteCarrierForExistingShipmentAsync` reloads SQL authority, restores origin/customs/geographic snapshots, obtains same-tenant/carrier credentials, persists a request ID/attempt, uses conditional SQL claim, calls provider outside an EF transaction, and re-locks/requalifies order/shipment before persistence. `EnsureNoActiveLocalTransactionForExternalCarrierCall:4370–4377` protects the external boundary.

`BuildCarrierRequestId:2630` is `ship-{tenant}-{shipment}`. Real Bosta/Aramex/DHL do not send this as a documented provider idempotency key; Bosta businessReference, Aramex Reference2, DHL customer references and FedEx customer transaction ID are correlation, not exactly-once guarantees. Every real adapter recovery lookup defaults/returns null. A prior ambiguous create attempt leads to `EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED` and suppresses blind recreation (`ReconcilePendingCarrierRequestAsync:2787–2852`). This is sound containment but incomplete recovery: a provider may have accepted a shipment whose response was lost/rejected. Operators need a verified reference lookup/reconciliation workflow.

Shared creation admission validates identifiers/URLs/base64/format/metadata/initial status (`ValidateCarrierCreateResultForPersistence`, `IsValidInitialCarrierStatus`). It does not establish actual merchant account identity, make optional labels mandatory, or add provider idempotency. URLs allow HTTP/HTTPS without a provider-host allowlist; current UI opens them, no server label-fetch SSRF was demonstrated.

### 5.2 Tracking and stale/out-of-order progression

`CarrierShipmentService.SyncTrackingAsync:1150–1399` resolves tenant shipment, current config, authenticated tracking and Serializable mutation; reloads shipment, blocks invalid transition, stores raw metadata/last sync, synchronizes canonical fulfillment, publishes events before commit. Its normalized result has no response-tracking/account identity field, so adapters must do that binding themselves. Aramex checks the outer key; DHL checks requested AWB; Bosta and FedEx omit request/response binding.

**Scheduling is presently blocked by scope composition.** `TrackingSyncJob.SyncAllAsync:65–76` queries tenant-filtered Shipments in a fresh scope without setting tenant or bypassing filters for qualified discovery. `SyncTenantAsync:99–113` creates another unbound scope; `SyncShipmentAsync:179–185` another. `ApplicationDbContext.CurrentTenantId:29`, Shipment filter741–742, and `TenantContext.TenantId` defaultnull show these reads cannot find non-null tenant rows. Merely passing tenantId as a method argument does not set context. The registration cron test does not test this runtime behavior. Fix requires explicitly qualified global discovery and bound per-tenant scopes, never weakening global tenant filters.

Further defects after that blocker:

- Shared `ShipmentLifecycleTransitionPolicy.cs:19–37` requires intermediate transitions. ReadyForPickup→InTransit/Delivered and PickedUp→OutForDelivery/Delivered are rejected. Tracking/webhook service applies **only current/latest** status, not ordered admitted history. An interval or missed webhook can strand a valid progressed shipment while updating LastCarrierSyncAtUtc and reporting success/no change. Fulfillment synchronization can step intermediate states, but the shipment gate rejects first.
- Direct sync stops on Failed/Cancelled/Returned; policy never exits Failed. Provider retryable exception/re-delivery is not equivalent to irreversible failure. Bosta47 NDR especially requires separate attempt/exception semantics.
- Job excludes Delivered/Cancelled/Returned but not Failed. Direct sync skips Failed without updating LastCarrierSyncAtUtc; old failed rows can repeatedly occupy the50 oldest slots. Delivered→Returned is supported by direct policy/service, but scheduled sync never observes it.
- Neither shared webhook nor tracking collection admission compares an authoritative event sequence or freshness timestamp; stale status is blocked but collection can be latched even in the rejected-progression branch. Provider collection timestamp can default to server-now. Future money admission must reject stale/inconsistent collection independently of physical progression.
- Hosted authority recovery excludes delivered Pending COD in its repair candidates (`ShipmentAuthorityRecoveryHostedService:286–296`); it is not a customer-collection reconciliation worker. Its `result.Success || result.Reconciled` counter can count a blocked reconciliation as recovered (`249–252`), overstating success.

### 5.3 Cancellation, returns and label visibility

`CarrierShipmentService.CancelShipmentAsync:994–1129` permits only pre-pickup transitions, resolves configuration and invokes provider when tracking exists, otherwise marks local cancellation. Unsupported/failing cancellation stores a ReconciliationRequired error. External existence is judged only by trackingNumber, so an external-reference/AWB-only or ambiguous in-flight create can be cancelled locally without external proof. This writer lacks post-call SQL re-lock/revalidation and emits no status/fulfillment bridge itself.

**Separate accessible admin route is defective:** `AdminShipmentsController.CancelShipment` → `AdminShipmentService.CancelShipmentAsync:525–624` accepts a tenant shipment, checks local transition, writes Cancelled/events and commits, **without any IsCarrierManaged guard or carrier call**. Transaction uses default isolation; ordinary EF SELECT is not an explicit row lock despite comments. Generic status/tracking writers correctly reject carrier ownership, but that does not protect this cancellation action. Duplicate cancellation throws immutable rather than returning original success. UI `ShipmentActions.jsx:135–136` claims carrier notification/order update. `AdminOrderService.CancelOrderAsync` has a separate authoritative cancellation/compensation path; this route is not a substitute for that lifecycle.

Physical failed delivery maps to `MarkDeliveryFailedAsync(canRetry:false)`; pre-delivery return-to-origin becomes FailedDelivery, delivered return uses `MarkReturnedAsync` (`CarrierShipmentService:4754–4867`). Fulfillment return explicitly rejects refund arguments (`OrderFulfillmentService.MarkReturnedAsync:417–425`); neither return nor delivery should become a finance journal automatically. No external return shipment booking exists. Restoration/refund remains separate canonical payment/inventory authority.

`CarrierShipmentService.GetLabelAsync:3712–3744` is stored-data retrieval, not provider generation/refetch. Aramex/DHL/FedEx direct creation can yield labels, but operational API/UI exposure is incomplete. `AdminShipmentDetailsDto` and `AdminShipmentService.MapToDetailsDto:893–931` omit carrierCode, AWB, labels, package details, COD amount/flag/time, failed/returned/pickup timestamps, LastCarrierSync and registration errors. `ShipmentDetailsPage.jsx` expects those flat fields and also `createdAt` rather than returned `createdAtUtc`. Therefore its COD/label sections cannot render even for relevant rows on this endpoint; they are present but data-blocked. Fresh Manual detail GET confirmed absent fields; no carrier row was fabricated to demonstrate it.

## 6. COD customer collection is not merchant receipt

### 6.1 Five separate economic facts

| State | Meaning | Current authority |
|---|---|---|
| A Customer paid carrier | Customer discharged an amount to carrier | Must be actual observed collection, not expected COD/delivery |
| B Carrier acknowledged collection | Trusted carrier confirms actual collection amount/currency/time | No external adapter currently admits complete evidence; official Bosta cod could help after parser/contract fixes |
| C Carrier owes merchant | Qualified collection creates receivable/clearing, subject to proven fees/adjustments | Existing canonical collection journal Dr1130/Cr4020 recognizes expected whole order after SQL admission; current transport money gap prevents blanket provider certification |
| D Carrier initiated/scheduled payout | Transfer is planned/dispatched, possibly pending/failed | No implemented feed; does not debit Cashbox/Bank |
| E Merchant received money | Physical cash in merchant custody or credited identified merchant bank | Requires accepted actual-receipt authority: future authenticated final provider/bank evidence OR proposed authorized operator attestation; no current carrier workflow |

### 6.2 Actual admitted chain

```text
Tenant-qualified config + admitted authenticated tracking
  OR webhook target resolution + configured trust + adapter parsing
→ CarrierShipmentService sets Shipment.CodCollected / CodCollectedAtUtc
→ ShipmentCodCollectedEvent (expected amount/currency also included)
→ ShipmentCodCollectedConsumer passes tenant/order/shipment locators ONLY
→ PaymentLifecycleService.RecordCarrierCodCollectionAsync
→ ValidateCodCollectionAdmissionAsync, BEFORE terminal replay/repair
→ canonical Pending→Collected, stock commit and accounting outbox
→ current SQL recovery / CommerceAccountingService.PostCodDeliveredAsync
→ Posted Order_COD Dr1130 / Cr4020
→ STOP: no enabled carrier receipt/remittance executor
```

`ValidateCodCollectionAdmissionAsync:349–399` requires carrier fulfillment and snapshot, same-tenant/order nondeleted carrier shipment, matching carrier, COD flag, persisted collected/time, expected CodAmount==order.Total and matching currency; Delivered for first recognition. It overwrites requested money from order SQL and reconstructs actor/reference from shipment. Manual operator collection requires Manual fulfillment/no carrier snapshot. Consumer amount/currency/actor are not ledger authority. These prerequisite controls remain intact.

However, SQL consistency of **expected** amount cannot prove actual customer paid that amount. `IShippingCarrierAdapter.cs` tracking329–351 and webhook399–414 expose only collection boolean/time/raw text. `Shipment` has expected CodAmount/currency, no observed money/source/account/environment/contract version. `PublishShipmentCodCollectedEventAsync:4622–4640` sends the expected values. Financial admission does not independently parse a raw provider receipt. A future parser that simply turns delivered into CodCollected would post full order.Total even if collected short, wrong currency, prepaid elsewhere or refunded. Fix the money evidence boundary alongside parser admission.

`FulfillmentDeliveredEventConsumer` correctly does not collect. Inventory reservations commit at canonical payment success (`PaymentLifecycleService:2600–2669`), with COGS outbox staging; cancellation releases through canonical payment/order cancellation. Physical carrier return alone does not restore committed stock or recognize refund. `LinkReturnStockAndCogsReversalForRefundAsync:5221–5384` caps returned/refunded shares and restores reservations/COGS in the refund transaction; historical pre-COGS behavior differs. No such mutation was executed here.

### 6.3 Carrier money evidence matrix

| Provider | Customer collection A/B | Actual amount/currency/time | Merchant receipt E / ID/finality | Exact remittance membership / G,F,N,date / receiving account / reversals/disputes |
|---|---|---|---|---|
| Bosta | **Not admitted**: official webhook rejected; tracking no true collection | Official delivered webhook documents cod and timeStamp, but no currency in body; current parser discards all; account/market currency must be explicitly bound | None implemented; unpaidCodAmount explicitly not transferred | All absent from implemented remittance contract; delivery `_id`/businessReference are logistics IDs only |
| Aramex | True COD branch unreachable; webhooks disabled | Expected outbound Money only; observed collected amount/currency/time absent | None | All absent; SH003/delivery/parcel COLLECTED is not payout evidence |
| DHL Express | No mapped COD collection | None; declared customs value is not COD | None | All absent; AWB/dispatch-confirmation is not settlement identity |
| Mylerz | Blocked | Dormant guesses, not executable evidence | None | Unknown external contract; none implemented |
| FedEx | No mapped COD special service or collected truth | None | No receipt feed; published ECOD business description not per-receipt API authority | None implemented; transactionId is API correlation, not permanent payout ID |
| Manual Fulfillment | Distinct merchant operator collection authority | SQL whole order amount/currency; historical certification, no external feed | Existing dedicated Main Cashbox settlement, separate from carrier receipt | Whole order/zero-fee/1010 contract only; proposed carrier variant absent |

No carrier currently proves actual deducted shipping fees, bank/account receipt identity, finality, complete allocation or remittance reversals/disputes. This is a contract/implementation gap, not proof that every provider lacks private finance products.

## 7. Manual Carrier Remittance feasibility — design only

### 7.1 Business-policy verdict

**Conditionally feasible and accounting-sound.** The user's policy changes acceptable receipt evidence: authenticated merchant operator explicitly asserts actual receipt. Independent electronic carrier statement is not mandatory. The system can accurately record **operator-attested receipt**, without falsely calling it provider-verified. Fraud/error controls must reflect that authority. Delivery and carrier collection remain separate prerequisites and never create cashbox balance automatically.

The earlier carrier audit's electronic-final-receipt recommendation is appropriate for **automated provider execution**. It does not prohibit a separately approved operator-receipt workflow. The certified Manual Fulfillment executor remains restricted; the proposed carrier operation must not loosen it implicitly.

### 7.2 Smallest dedicated future contract

1. Separate Manual Carrier Remittance operation/type/source, permissions and bilingual review screen. Resolve current tenant and active authorized financial operator server-side (initial Owner/Admin policy consistent with existing financial commands); authenticate/revalidate actor on every execution and replay access. Record original actor permanently.
2. Selection consists of bounded duplicate-free **whole collected carrier COD orders**, qualified inside Serializable transaction: tenant/order/shipment/provider ownership, immutable carrier/account/environment collection evidence, Delivered/Collected/paid truth, exact Posted Dr1130/Cr4020 collection journal and authoritative whole totals. No generic balance-based allocation.
3. One receiving channel per operation. Physical merchant cash→active tenant Main Cashbox1010; actual merchant bank credit→active tenant Bank1030. Resolve mappings/account IDs server-side; caller selects approved channel/account candidate, never arbitrary ledger destination. Actual posted receiving debit is permanent refund destination.
4. Operator explicitly attests *money actually received*, real receipt date/time (separate from recorded-at UTC), carrier/account, reference and amount/currency. Store reconciliation notes and supporting evidence references where supplied. Do not label scheduled, dispatched, unbanked check or wallet credit as bank receipt. Attachment is support, not a computed authenticity flag; physical cash receipt is a valid channel.
5. Gross is whole-order SQL sum, net is actual receipt, fees must be explicit supported/proven deductions, algebra N=G−F. Reject over/short receipts, currency/precision/tenant mismatch, unknown/duplicate member, prior allocation including deleted rows, refunds/residual balances, holds/disputes, and incomplete collection authority. Cash initial scope can require zero fees; cash net-of-fees needs reviewed Dr1010=N/Dr6020=F/Cr1130=G extension.
6. Permanent GUID operation ID and canonical immutable fingerprint covering order set/provider/account/channel/actual destination/G/F/N/currency/receipt date/reference/evidence. Same-ID same facts returns original posted result; changed facts conflicts; new-ID overlapping order conflicts through shared permanent tenant/order allocation index. Human reference alone is not reliable dedupe: provider/account/channel/receipt-reference collision detection and review needed for two operators allocating one receipt to disjoint orders.
7. Atomically commit batch/members, receipt-attestation/evidence/audit, Posted journal/balances, Settled states, accounting receipt and event outbox. Return success only with actual durable Posted journal. Rollback and clean-context conflict/retry; lost response retrieves original identity. Do not use dormant post-commit best-effort carrier coordinator.
8. Extend posting-engine **source-specific authority**, refund lineage/source readers and recovery explicitly. Use common locking/posting/outbox infrastructure, not the certified Manual Fulfillment executor. Retain old409/410 routes and generic Clearing-transfer restrictions.
9. Explicit operational reconciliation: received but unmatched money, collected but unremitted orders, contradictory collection/receipt, fee discrepancy, duplicate receipt, hold/dispute/reversal. Persist decisions without prematurely allocating/posting. Require a separate immutable correction/reversal domain; never edit original batch/membership/journal or reopen permanent identity.

### 7.3 Risks and controls

| Risk | Necessary control |
|---|---|
| Fictitious receipt or wrong channel | Explicit receipt attestation/review, server role/actor, real channel/account and date; optional evidence/notes retention; configurable approval/dual-control policy if the business wants it, rather than pretending an attachment authenticates money |
| Double booking receipt under new IDs | Permanent operation/member indexes plus receipt-level reference/fingerprint collision and reconciliation checks, shared across manual and future automated workflows |
| Collection shortfall/prepaid COD | Observed amount/currency/provenance must be admitted before full clearing recognition; do not use remittance confirmation to silently manufacture collection |
| Fee manipulation | Server whole totals and exact decimal currency; categorized proven deductions, net receipt validation; no guessed quote-as-fee or unrelated liability offset |
| Refund raced with settlement | Sorted row locks, prior refunds/allocation check inside transaction, account xmin conflict rollback/retry; exact Posted journal requirement |
| Bank vs physical cash confusion | Display separate channel and journal preview; no default inferred from carrier/Delivered; retain actual debit AccountId for refunds |
| Evidence corrections/deletion | Append-only evidence/decision lineage and immutable result; separate audited correction domain and retention/access policy |

**Architecture suitability:** existing whole-order allocation, immutable posted receiving debit, lifecycle interceptor ownership, posting authority, Serializable scopes, order locks, xmin, event/accounting outboxes and permanent SQL indexes are reusable. Existing batch fields hold gross/fees/currency/settled-date/provider/reference and BaseEntity actor/time, but do not fully store receipt channel attestation, carrier account/environment/provenance, independent net, evidence/notes and complete conflict fingerprint. A bounded reviewed persistence extension is needed; no current field or dormant foundation flag proves the proposed workflow exists.

## 8. Accounting, refunds and ledger consistency

### 8.1 Current and proposed journals

```text
Qualified customer collection of G:
    Dr 1130 COD Clearing       G
    Cr 4020 E-commerce Sales   G
    Source Order_COD; COD_DELIVERED:{tenant}:{order}

Actual physical merchant receipt (initial zero-fee scope):
    Dr 1010 Main Cashbox       G
    Cr 1130 COD Clearing       G

Actual bank receipt with supported proven fees:
    Dr 1030 Bank               N
    Dr 6020 Shipping Fees      F   (only when positive)
    Cr 1130 COD Clearing       G
    N = G − F
```

`CommerceAccountingService.PostCodDeliveredAsync:1396–1430` implements Dr1130/Cr4020 using SQL order.Total. Collection outbox/recovery can leave Collected while accounting is pending; settlement must inspect Posted journal, not payment badge.

`PostCodSettlementAsync:1957–2004` retains Bank/Fees/Clearing composer for historical OFFLINE_COLLECTION batches. It takes money arguments; it does not authenticate merchant receipt. `PaymentLifecycleService.RecordOfflineSettlementAsync:401–635` retains whole-total/currency/batch/outbox checks but commits Settled at595 before best-effort accounting615–634. It is not the current certified atomic settlement path and not suitable to enable unchanged.

Dedicated `PaymentLifecycleService.ManualCodSettlement.cs:49–125` remains implemented only for Manual Fulfillment: SQL active same-tenant Owner/Admin actor, fixed cashbox, zero fees, exact Posted collection, no refunds/prior allocations,1–100 whole orders, atomic journal/balance/state/outbox/receipt commit. `ManualCodSettlementAuthority.ValidateOrdersAsync:60–62` rejects carrier orders; `BuildPostingAsync:86–118` independently revalidates authority. `FinancialPostingService.ValidateManualCodSettlementInflowAsync:806…` checks source shape. This source is preserved, not newly financially recertified.

### 8.2 Safety/compatibility table

| Requirement | Current proof and carrier extension limit |
|---|---|
| Exact whole orders | `PaymentSettlementOrder` stores tenant/batch/order/total/currency. Manual independently validates exact two-line collection journal and totals. Carrier variant needs same qualification plus carrier observed collection and receipt attestation. |
| Permanent identity/membership | `ApplicationDbContext:898–943`: unique unfiltered tenant/type/SettlementId and tenant/order; shared across kinds. Migration `20261008223819_ManualCodPermanentSettlementIdentity` removes filters. Applied schema status is historical certification only, not a fresh DB query. Provider/account/environment external ID not part of batch uniqueness; deterministically scope canonical identity and immutable receipt fingerprint. |
| Immutability | `GuardTreasuryCashAuthority:7156…` guards header/member/ledger lines, authorized journal/balance writer and journal economic identity; BaseEntity facts persisted. FKs are ID-based; explicit tenant relationship checks remain essential. No privileged/raw-SQL immutability proof claimed. |
| Concurrent/idempotent posting | Sorted PostgreSQL order FOR UPDATE, Serializable lifecycle, permanent unique keys, account xmin, existing journal tenant/idempotency key. Posting joins current transaction. Accounts are ordinary ordered reads with optimistic xmin, not explicit account FOR UPDATE. New carrier operation must define conflict/rollback/re-read behavior. |
| Atomicity | Certified Manual source commits all effects together; dormant offline coordinator does not. Carrier API fetch/evidence acquisition should occur before financial transaction, then immutable accepted evidence/SQL requalified inside it. |
| Currency/precision | Journal lines/account balances are scalar, no posting currency dimension. Settlement numeric(18,2). Enforce active tenant ledger currency==every order/shipment/member/evidence currency, supported precision/representability. No implicit FX or display currency authority. |
| Refund destination | `SettlementRefundLineage.ResolveDestinationAccountAsync:17–110` independently reloads current tenant order/batch/members, exact one Posted journal, balanced positive lines, gross clearing credit, one mapped active non-clearing Asset receiving debit equalG−F, other debits Expense. Returns actual receiving AccountId. Existing type contracts OFFLINE_COLLECTION/CODSettlement, GATEWAY/GatewaySettlement, MANUAL_COD_COLLECTION/ManualCodSettlement. New source requires explicit extension. |
| Refund enforcement | Lifecycle `ResolveRefundAccountingContextAsync:4981–5045`, composer `ResolveEcommerceRefundCreditAccountIdAsync:1808–1835`, posting `ResolveOrderRefundCashAuthorityAsync:1120…` independently qualify actual lineage. Missing/reversed/foreign/ambiguous/inactive history fails, no Bank/Cashbox fallback. |
| Recovery | `AccountingOutboxProcessor.ProcessCodSettlementAsync:3523–3562` reloads source through `AccountingLedgerAuthority`, not event money; verifies SQL historical header, not external remittance. Manual `RecoverReceiptAsync` acknowledges existing Posted result only. New receipt needs recoverable lease/result lookup; no duplicate inflow on retries. |

### 8.3 Unsupported/residual cases

- **Pre-settlement partial refund:** Dr sales/Cr actual COD clearing reduces residual. Whole-original-order membership cannot express that residual safely; current Manual rejects any persisted refund. Proposed first-scope carrier workflow must do the same, never credit original G after reduced clearing or silently drop refunded members.
- **Post-settlement partial refunds:** Dr4020/Cr actual posted cash/bank destination, with canonical cumulative order cap and idempotent refund identity. Fees can make full refund exceed original net receipt; account funding/fee recovery is a separate policy, not invented carrier credit.
- **Zero net F=G:** dormant composer calls Debit(0), prohibited by `PostingLine.Debit`; refund resolver rejects F>=G and cannot identify positive receiving debit. Hold/review; no whole-order settlement. F>G/negative/mismatched N rejected.
- **Split receipt/destination or partial settlement:** permanent tenant/order allocation and single receiving debit cannot express it. Unsupported initial scope; no arbitrary refund account or new parallel allocation table.
- **Disputed/held/reversed remittance:** generic journal reversal exists, but no certified carrier allocation/payment-state/reversal lifecycle. Preserve originals, append linked decision, no automatic reallocation or reopening of identities.
- **Cancellation/return after collection:** physical status does not reverse receivable/payment. Canonical refund/cancellation/accounting recovery handles actual money; unresolved carrier liability must remain visible.

## 9. Tenant security, trust, idempotency and observability

### 9.1 Proven protections and exact gaps

- Provider configs loaded explicitly by tenant+normalized carrier+nondeleted (`CarrierIntegrationAuthority.LoadConfigurationAsync:693–703`). Admin account/warehouse operations qualify same tenant; storefront shipment reads verify JWT tenant/customer/order, with404 for foreign ownership (`StorefrontShipmentController:66–88`). Shipping permissions are server catalog `shipping.manage`, `shipping.view`, `shipping.fulfill` (`PosEndpointPermissionCatalog:167–171`); UI grants do not authorize requests.
- Creation readiness checks enabled config, required credentials and matching validation fingerprint; purpose exceptions intentionally allow Tracking/Cancellation even while create readiness is blocked (`988–991`). Therefore disabled catalog does not prove every existing-shipment read/cancel cannot use retained credentials. All external AhramStore rows currently absent/disabled as observed.
- **Binding limit:** outbound credentials use current tenant/carrier configuration, not permanently snapshotted shipment credential/account/environment. Admin account is a local tenant object, not provider-verified legal merchant binding. Bosta cities validation has no merchant identity, DHL no HTTP validation, Aramex generic account label, FedEx token cache keys account/environment/clientId but not tenant, secret version, child key or grant. Credential/account/environment changes can invalidate old shipment matching; receipt workflow must bind immutable provenance.
- Encryption uses DataProtection purpose Shipping.CarrierCredentials.Secrets/ENC prefix; decrypt failures empty, legacy plaintext accepted (`CarrierCredentialEncryptionService:90–111`). New writes/facade config use encryption; no database secret inventory or legacy plaintext prevalence inspected.
- Anonymous webhook pipeline validates content type/body limit/rate limit; resolves SQL target before tenant context assignment (`ShippingWebhookRequestPipeline.cs`, resolver245–441). Claimed tenant/shipment is a constraint; unique persisted carrier/tracking is matching authority. Unknown/ambiguous reject. Generic route reads X-Webhook-Signature; fixed Bosta reads Authorization, so its dashboard custom header must match configured route.
- `ResolveWebhookTrustAsync:640–691` requires enabled config and capability. Bosta default shared_secret; Aramex/DHL/Mylerz/FedEx disabled. Central policy also accepts configured HMAC/IP modes even if provider descriptor only lists shared_secret (`993–1046`); that does not prove vendor HMAC support. HMAC binds bytes; fixed-time token comparison authenticates configured secret; IP trust is not independently signed money evidence. No signed freshness/nonce/window or provider signing-key chain established.
- ForwardedHeaders in Program119–147 clears defaults then trusts loopbacks and configured proxies, ForwardLimit1; IP trust depends on deployed proxy configuration, not arbitrary caller X-Forwarded-For alone. Deployment/source-IP provider guarantee not externally verified.
- Replay logs: permanent tenant/carrier/eventId and payloadHash indexes (`ApplicationDbContext:1775–1801`); same eventID different bytes→WEBHOOK_TAMPERED, Completed/Skipped/Processing→duplicate, Failed can retry (`PrepareShippingWebhookLogAsync:1901–2036`). Raw-byte hash is not canonical economic identity. Recursive generic id extraction may pick an unrelated object ID (`ResolveWebhookEventId:2405–2418`); Bosta `_id` deliberately not admitted as transition ID. No semantic event/revision contract or crash lease.
- **Crash window:** Processing log SaveChanges1683 precedes mutation transaction1709. Crash before final processing leaves Processing; repeated delivery is acknowledged duplicate forever in that method. No scheduled stale-Processing reclaim found. Acknowledgement is not successful state application.
- Shipment unique nondeleted tenant/order, tenant/carrier/externalReference and tenant/carrier/requestID indexes (`3437–3458`). Carrier+tracking index is **nonunique**. Tracking ambiguity rejects, but multiple tracking identifiers and soft-deleted/reused external IDs remain an operational reconciliation risk; no financial permanence can be inferred from shipping indexes.
- Logging redaction is partial: webhook log stores redacted summary/IP, hashed signature, truncated UA. But Bosta174–178, Aramex100–103/314–318 and DHL119–122/366–370 log raw response Body, and Shipment stores raw provider create/tracking JSON. Provider errors can echo PII and secrets. No observed secret disclosure claimed; source exposes retention/redaction gap. Financial consumer logging includes event money for diagnosis but does not use it for posting.
- Source-specific financial permission/SQL actor controls exist for Manual only. Operational admin actor has claims-based string fallback `unknown-pos-admin`; not a suitable future financial receipt actor. Admin cancellation logs caller reason and does not attribute the same durable GUID/receipt lineage as Manual settlement.

### 9.2 Outages/rate limits and unresolved exceptions

Named external clients have15-second timeout (`Program:1272–1323`); GET/HEAD/OPTIONS get two transient/429 retries250/500ms; mutation retries only with Idempotency-Key header. Real Bosta/Aramex/DHL create lacks that header; POST tracking/rate calls also do not receive generic retries. These are bounded, not documented provider rate-limit enforcement. FedEx adds own immediate retries to shipment POST and read POST, which can duplicate external creation after ambiguous timeout. Request correlation is not dedupe. No per-account circuit breaker/quota-aware scheduling or provider Retry-After admission was established.

Creation error/attempt/reconciliation fields, webhook processing logs, reliability telemetry, carrier administration audit/readiness and accounting-outbox health exist. **Operational visibility is incomplete:** detail DTO omits most exception fields; no abandoned-webhook work queue, semantic collection-money discrepancy report or carrier-remittance reconciliation UI exists. Shipping webhook `HealthCheck:286–295` always returns healthy plus registered codes; it does not test parser compatibility, provider connectivity, tenant account binding or active scheduler. Aggregate payout reconciliation is a different domain, not inbound carrier receipt.

## 10. Test discovery and exact fresh execution

### 10.1 Backend compilation/discovery

`PointOfSale.Tests.csproj`: net9.0, xUnit2.9.2, runner2.8.2, TestSDK17.12.0; default SDK Compile includes Infrastructure/Architecture/Shipping/Security/Integration/Payments/Admin/Storefront files. `Compile Remove Accounting/*.cs` and WhatsApp/*.cs exclude most accounting tests. Explicit Accounting re-includes are AffiliateAccountingBridge, AffiliateReversalAccountingConsumer, AccountingOutboxProcessor, CommerceAccountingService, FinancialReportService and two T08 COGS suites.

Executed provider files are actually discovered in `bin/ManualCodVerification/net9.0/PointOfSale.Tests.dll`, not inferred from filenames. `--list-tests` inspected the shipping/provider/containment slice before execution. Separate discovery for `FinancialPostingServiceTests|TreasuryTransferServiceTests|AccountingHardeningTests` returned **No test matches**; those Accounting files are excluded. In particular old TreasuryTransfer settlement tests cannot certify generic Clearing transfer authority.

| Test source (under PointOfSale.Tests) | Discovery/current audit use and limits |
|---|---|
| `Infrastructure/BostaEndpointSurfaceTests.cs` | Discovered/executed; mocked HTTP routes/address validation/unknown-status rejection. Does not test current official code/value or numeric webhook, required images, collection money or financial effects. |
| `Infrastructure/AramexOfficialContractCompatibilityTests.cs` | Discovered/executed; request/label/product/identity/invalid-COD/cancel fail-closed mocks. Passing USD-only fixture does not supply fresh official market rule. |
| `Infrastructure/DhlOfficialContractCompatibilityTests.cs` | Discovered/executed; create/customs/label/unknown status/cancel/local validation. Tracking_DeliveredCodeMapsToDelivered uses invented status-object/timestamp/statusCode fixture, not fresh MyDHL payload. |
| `Infrastructure/MylerzOfficialContractCompatibilityTests.cs` | Discovered/executed; gate/no-outbound verification only. |
| `Infrastructure/FedExTransportRuntimeTests.cs`, `FedExResponseAdmissionTests.cs`, `FedExEndpointRuntimeIntegrationTests.cs` | Discovered/executed; mock OAuth/HTTP/retry/admission/typed endpoints/create-label. Direct create test expects Pending and MIME label; it does not prove shared registration succeeds. |
| `Architecture/FedExOfficialContractCompatibilityTests.cs`, `CarrierIntegrationAuthorityCertificationTests.cs`, `ShipmentLifecycleAuthorityCertificationTests.cs` | Discovered/executed; source-string/DTO/OAuth mocks/policy checks. Assertions about declarations and artifacts are not provider operation certification. |
| `Security/ShippingWebhookTrustPipelineTests.cs`, `ShippingWebhookTenantResolutionTests.cs` | Discovered/executed; isolated InMemory, fake parser/no-op events, non-COD shipment. Trust helper named ComputeSignature returns shared secret, **not HMAC**. Verifies token/target/duplicate/non-financial failed/return paths, not official Bosta parser or relational crash/concurrency. |
| `Payments/T10SettlementContainmentTests.cs`, `Architecture/TreasurySettlementAuthorityCertificationTests.cs` | Discovered/executed; mock controller rejection/source boundary; no positive settlement. |
| `Shipping/ShipmentExternalRegistrationSafetyTests.cs`, `ShipmentExternalRegistrationRequestedConsumerTests.cs`, `CarrierIntegrationAuthorityRemediationTests.cs`; Admin/Storefront shipping suites | Included/default compiled; shipping slice discovery shows these suites; not executed here. Existing fixtures cannot prove vendor dedupe or all shared jobs run correctly. |
| `Infrastructure/ShipmentCodCollectedConsumerTests.cs`; `Integration/CodPaymentLifecycleTests.cs`, `ManualCodSettlementTests.cs`, `CodSettlementPrerequisiteLineageTests.cs`, `EcommerceRefundLifecycleTests.cs` | Included/default compiled; positive financial suites deliberately not executed. Prior report certification remains historical; source and containment reviewed only. |
| TrackingSync runtime | Found cron registration test and architecture references, **no dedicated runtime tenant-scope/catch-up test**. No scheduler execution was triggered. |

Exact executed primary backend command:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c ManualCodVerification --no-build --no-restore --filter "FullyQualifiedName~BostaEndpointSurfaceTests|FullyQualifiedName~AramexOfficialContractCompatibilityTests|FullyQualifiedName~DhlOfficialContractCompatibilityTests|FullyQualifiedName~MylerzOfficialContractCompatibilityTests|FullyQualifiedName~FedExTransportRuntimeTests|FullyQualifiedName~FedExResponseAdmissionTests|FullyQualifiedName~FedExEndpointRuntimeIntegrationTests|FullyQualifiedName~FedExOfficialContractCompatibilityTests|FullyQualifiedName~ShipmentLifecycleAuthorityCertificationTests|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests|FullyQualifiedName~T10SettlementContainmentTests|FullyQualifiedName~TreasurySettlementAuthorityCertificationTests" --logger "trx;LogFileName=shipping-carriers-e2e-audit.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
```

**162 executed:161 passed,1 failed,0 skipped**, reported duration2s. Failure:

`PointOfSale.Tests.Architecture.ShipmentLifecycleAuthorityCertificationTests.CertificationArtifacts_MustRecordShipmentLifecycleAuthorityTruth`, source line183, required substring **PASS SF-SHP5** missing from one required `.ai-context` artifact. Failure output does not identify which loop member; it was not guessed or repaired. This is a documentary assertion failure, not a live provider/finance failure. TRX counters independently read. No green full-slice claim.

Additional safe security command, same project/config/no-build/no-restore/logger/results directory:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c ManualCodVerification --no-build --no-restore --filter "FullyQualifiedName~ShippingWebhookTenantResolutionTests|FullyQualifiedName~ShippingWebhookTrustPipelineTests" --logger "trx;LogFileName=shipping-carriers-security-audit.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
```

**8 executed/8 passed/0 failed/0 skipped**, duration9s. Combined backend: **170 executed/169 passed/1 failed/0 skipped**; disjoint slices. Test stores are InMemory or mocked provider clients; no merchant financial fixtures.

### 10.2 Frontend discovery/results

Web `vitest.config.ts:5–9` uses jsdom and includes src TS/TSX spec/test files; selected .test.tsx files are included. UI APIs are mocked, including Manual settlement POST; no backend financial call.

```powershell
# workdir PointOfSale.Web
npx vitest run src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx src/pages/treasury/TreasuryManualCodSettlement.test.tsx src/pages/treasury/TreasuryGeneralLedger.test.tsx
```

**3 files passed/24 tests passed/0 failed**, total44.84s. Workspace13, preserved Manual5 and General Ledger6. Tests prove rendered backend catalog/readiness/error/role/intent behavior with mocks, not provider operations or actual cash receipt.

Storefront `package.json` has test=vitest; `vite.config.ts` supplies `@` alias, default Vitest discovery includes `src/components/checkout/ShippingStep.localization.test.ts`:

```powershell
# workdir PointOfSale.Storefront
npx vitest run src/components/checkout/ShippingStep.localization.test.ts
```

Runner reported **1 file/2 tests passed**,15.85s, but the shell invocation failed to exit and was terminated at60s. Record as **passing test assertions with timed-out process**, not clean command success. No rerun or source/config edit. Existing Vite esbuild/oxc deprecation warnings printed. This suite reads source/i18n, no cart/provider/financial call. Web config does not automatically include Storefront tests; it was invoked in its own project.

### 10.3 Read-only runtime and UX

- MAIN port5010 process PID11340, verified executable `C:\Users\fadya\source\repos\PointOfSale\PointOfSale.API\bin\Debug\net9.0\PointOfSale.API.exe`. No restart/stop.
- Health GET at `2026-10-09T11:02:48.3230103Z`, correlation `3076f0e0-3fb1-4dff-8742-2993954c8998`: aggregate Unhealthy; COD foundation Healthy **as disabled**: enabledfalse, environmentDisabled, runtime/evidence/lifecycle/settlement execution allfalse. Payout external truth latestCritical/one mismatch; not carrier remittance.
- POS authorized Owner session: live catalog/method/account/shipment reads in section2; no provider validation, toggle, activation, tracking, cancel or money mutation clicked.
- Registered isolated headed Edge `opencode-ui-test`. Initial open timed out60s, list showed absent; second documented isolated open succeeded; list then showed open/in-memory/headed. CLI help confirms syntax and no startup-timeout option. Reused session thereafter, no unrelated Edge process terminated. Browser visibility was not treated as registration proof.
- `/admin/shipping`, `/ecommerce/shipping-methods` (wrapper sets methods tab), existing Manual detail `/admin/shipping/shipments/1b66785b-2762-405e-9a40-0f195a689429` inspected EN/LTR and AR/RTL at1440 and390 width: **12 sampled layout states**, document/body width<=viewport throughout (mobile375 due scrollbar). No page-level horizontal overflow; no populated carrier/label/COD state or complete accessibility certification.
- Administration/localized method names render Arabic, but detail headings **Shipment Timeline, Carrier Information, Actions** remained English after switching RTL. Hardcoded error/COD/action copy persists in `ShipmentDetailsPage.jsx`/`ShipmentActions.jsx`. Responsive utility classes do not prove every long identifier/modal/table state works.
- `Web/src/modules/storefront/shipping/{CheckoutShippingStep,OrderSuccessShipping}.jsx`, storefront ShippingStep.tsx, shipping quote/timeline/status components, and shipping hooks provide backend-bound shipping presentation. No storefront checkout/order submission or customer login performed; actual storefront responsive delivery/COD states remain source/localization-test partial evidence.

## 11. Prioritized findings

Severity reflects operational/financial consequence. **Code/contract-proven** means source plus fresh contract or direct local reads; **gap** means absent/unverified external behavior. Latent finance enablement hazards are explicitly contained, not claimed active exploits.

### Critical — contained financial enablement hazards

| ID | Finding/type | Evidence/impact |
|---|---|---|
| C1 | No automated carrier remittance authority — external contract and implementation gap | Every provider lacks admitted completed receipt ID/finality/member/G/F/N/currency/date/destination/reversal evidence. Enabling automatic Bank/Cashbox movement would manufacture financial authority. Old409 route and disabled foundation contain it. Operator-attested dedicated future workflow is a separately acceptable authority, not an automated API exception. |
| C2 | Dormant coordinator not atomic Posted settlement — code-proven latent defect | `RecordOfflineSettlementAsync` commits Settled before best-effort journal. SQL batch caps authenticate neither receipt nor full journal shape. Do not enable or repurpose unchanged; no current positive carrier route was found. |

**No currently enabled carrier-remittance financial exploit was demonstrated.** Critical means mandatory blocker before enabling that authority.

### High

| ID | Finding/type | Precise evidence |
|---|---|---|
| H1 | Bosta official webhook/COD rejected — code/contract | ParseWebhook uppercase/string fields; official lowercase/numeric; COD_COLLECTED absent from whitelist. Delivery and customer collection cannot arrive officially. |
| H2 | Bosta creation/tracking contract contradictions — code/contract | Always open-package=true without required images2150; code/value state unreadable for create and GET; no actual-money/currency mapping. |
| H3 | DHL tracking schema and scan meanings incorrect — code/contract | status string vs object; date/time/typeCode/serviceArea array vs assumed fields; RR customs→Returned, CR clearance→OutForDelivery, AF/PL→ReadyForPickup. Tests' assumed payload masks it. |
| H4 | Scheduled tracking tenant scopes unbound — code | TrackingSyncJob fresh scopes + mandatory Shipment tenant filter; passing GUID argument is insufficient. No runtime test found. |
| H5 | Missed-status/failed/delivered catch-up unsafe — code/contract gap | Strict shared latest-only transition, permanent Failed, job Delivered exclusion; real progressed/returned/retryable-NDR shipments can stay stale. |
| H6 | Admin cancel falsely finalizes carrier shipment — code | AdminShipmentService.CancelShipmentAsync no carrier call/guard; default transaction/no explicit row lock. UI claims notification. Additional carrier writer tracking-only external-existence and no post-call revalidation risks ambiguous create/race. |
| H7 | Collection lacks actual money/provenance — code | Normalized boolean/time + expected CodAmount/order.Total; cannot detect short/wrong-currency/prepaid collection. New parser alone is insufficient financial fix. |
| H8 | Bosta/FedEx requested tracking/account binding absent — code | Bosta.GetTracking returns response status without comparing identifier; FedEx admission checks response identifiers internally but adapter does not bind request. Tenant config is not immutable provider-account provenance. |
| H9 | Webhook Processing crash acknowledged as duplicate — code | Save log1683 before transaction1709; Processing duplicate1968–1977; no lease/recovery. Can permanently lose a trusted transition. |
| H10 | FedEx create retry and shared admission incompatibility — code/gap | Generic transport retries non-idempotent POST after ambiguity without dedupe; adapter Pending/application/pdf rejected by shared admission, readiness NotCertified. Current creation blocked, selected direct transport is not E2E integration. |
| H11 | Aramex COD collection unreachable/local currency blocked — code/gap | No admitted code containsCOD; USD-only current validation rejectsEGP; fresh official country/financial/status contract unavailable. CODS request is not observed payment. |
| H12 | DHL rate monetary currency incorrect — code/contract | Uses currencyType qualifier as currency; missing amount/currency falls back0/USD. Actual fee/reconciliation cannot trust it. |

### Medium

| ID | Finding/type | Evidence/impact |
|---|---|---|
| M1 | Misleading price/service availability | Bosta synthetic rates/services; Aramex domestic express/standard map sameOND and COD quote/create differ; legacy fixed quote defaultsUSD and cross-currency sort. Not canonical checkout finance. |
| M2 | Admin detail DTO hides operational authority | Missing COD/label/AWB/registration errors and timestamps expected by frontend; live read confirms contract mismatch. No base64 label download route/UI. |
| M3 | Account/credential readiness overclaims | Cities/local-only/general account labels not vendor legal binding; account health gate accepts any historic successful test, not current reference/environment; facade canonical fingerprints mitigate execution but administrative evidence drifts. |
| M4 | Mylerz unknown contract / pickup/return/POD gaps | Appropriate fail-closed Mylerz; catalog claims/DTOs do not implement missing booking/PrintLabel/SPOD/return methods. Logistics improvements need separate contract evidence. |
| M5 | Logs/raw response retention | Provider error Body logging, shipment raw JSON vs redacted webhook logs. PII/secret echo exposure potential; no actual leak shown. |
| M6 | Replay identity/freshness/trust scope | Byte hashes, recursive generic id, no signed age/nonce; shared token/IP not payload signature; provider-account/global identifier reuse unresolved. |
| M7 | Retry/rate-limit/recovery visibility | No provider quota/Retry-After policy; immediate FedEx429 retries; failed job-slot starvation, blocked recovery counted as recovered; no abandoned/finance-discrepancy work queue. |
| M8 | Unsupported financial cases | Zero-net, residual/partial/split allocation, disputes/reversal/fee recovery lack domain support; no current positive carrier path. Preserve fail-closed rules. |
| M9 | Positive tests do not match contracts/discovery gaps | Bosta/DHL fixtures omit current official shapes; most Accounting tests excluded; no fresh financial E2E. Source marker tests do not certify external operation. |

### Low

| ID | Finding/type | Evidence |
|---|---|---|
| L1 | Partial Arabic detail/action UX | Live Arabic/RTL retains English headings, hardcoded errors/COD/modal copy. Sampled responsive shell passed, populated states unknown. |
| L2 | Stale comments/documentation and marker failure | Bosta/Program production-ready comments, FedEx no-HTTP comments contradicted by current code; DHL3.3.1 internal version stale; one PASS SF-SHP5 artifact assertion fails. |
| L3 | Shipping health is catalog liveness | GET/webhooks/shipping/health alwayshealthy, not integration/parser/job/finance readiness. |
| L4 | Test process completion issue | Storefront localization2 assertions passed but invocation timed out; CLI first browser registration also timed out. Harness/process exit needs bounded follow-up, no product defect inferred. |

## 12. Remediation plan and smallest safe sequence

This is a recommendation for a separately authorized phase, not implementation approval. Independent shipping defects do not require a remittance API to be fixed.

### A. Provider contract/parser and COD collection

1. Freeze fresh official Bosta create/tracking/webhook fixtures (numeric code/value/type/time/POD/ref, declared string vs example numeric tracking), required image/open-package policy, supported lane/COD currency/limits, identifier preservation and matching. Correct parser and current create contract before readiness can claim useful execution.
2. Freeze DHL3.3.3 tracking/rate fixtures and reference vocabularies; correct status/scan schemas, timestamp/timezone handling, serviceArea and priceCurrency. Never map customs events as returns. Correct estimate/availability labels and reject missing monetary fields rather than0/USD.
3. Add carrier-neutral immutable **observed customer collection** authority: actual amount/currency/time, provider/account/environment/source/version/hash, exact order/shipment/ref match, expected-vs-observed reconciliation. Admit whole COD collection only when explicitly correct; under/over/prepaid/unsupported/disputed observations go to review, not full sales/clearing posting.
4. Obtain official country/account-specific Aramex status/COD/JSON/sandbox rules and Mylerz vendor package. Keep unsupported collection blocked; do not invent COD status strings or reactivate dormant URLs.

### B. Operational fixes independent of remittance

1. Fix tracking-job tenant discovery/bound scopes, with meaningful non-financial mock/in-isolated-store tests proving two tenants and no cross-tenant access. Add history/current catch-up through approved canonical steps and provider-specific retryable exception semantics. Define post-delivery return/collection observation and Failed starvation policy.
2. Route admin carrier cancellation through dedicated pre-pickup provider authority or fail closed. Coordinate authoritative order cancellation separately; preserve external intent ambiguity, re-lock/revalidate after provider call, publish local events atomically; distinguish request/confirmed/reconciliation states.
3. Fix Bosta/FedEx request/response identifier/account binding, FedEx shared initial status/label-format integration and unsafe creation retries. Keep NotCertified until actual registration admission and sanctioned test evidence pass.
4. Provide recoverable webhook Processing leases/age/attempt/error ownership and durable result lookup; semantic provider event identities, explicit stale/out-of-order rules. Do not use shipment `_id` as universal transition ID.
5. Add operational label retrieval/download for existing admitted creation labels, including encoded PDF, then documented Bosta mass-AWB/Aramex PrintLabel in bounded scope. Add pickup scheduling/cancellation only under documented confirmation identities/cutoffs; return booking/POD separate reviewed operations.
6. Replace synthetic carrier-pricing claims and currency defaults; distinguish merchant delivery charge, provider estimate, actual billed fee and supported lane/service. Align readiness with effective operation support.

### C. Manual Carrier Remittance business workflow

After qualified collection and Posted clearing evidence are available, implement the distinct operator-receipt contract from section7: explicit cash/bank channel, whole orders, actual receipt/net/proven fees/currency/reference/date/evidence notes, authenticated actor, immutable idempotent receipt identity, shared permanent allocation, dedicated posting authority, atomic commit, actual-debit refund lineage and recovery. First scope: one tenant currency, one provider/account, one destination, positive net, bounded whole orders, no prior refunds/holds/splits/reversals. Cash zero-fee first; bank supported-fee second if desired. Preserve certified Manual Fulfillment source; add new source readers explicitly.

No electronic carrier statement is required as the **only** evidence, and no provider remittance API is a dependency for that business workflow. Missing actual customer-collection evidence remains a prerequisite; operator receipt must not retroactively fake it.

### D. Future automated carrier remittance

Conditional on a verified vendor receipt package: stable tenant/provider/account/environment ID, exact complete membership/pagination, observed gross/deductions/net/currency/date/account, final merchant receipt semantics, pending/failed/reversed/disputed corrections, authenticated source and sanctioned test environment. Prefer read-only receipt fetch before finance transaction. Bosta is a sensible documentation-discovery target, **not ready merely because unpaidCodAmount exists**. FedEx ECOD or DHL COD product descriptions likewise do not supply application receipt authority. Shared manual/automated allocation and receipt-duplicate protection must prevent double recognition.

### E. Reporting, observability and recovery

Expose registration intent/request/attempt/provider IDs, ambiguity/cancel state, webhook Processing age/replay/conflict, tracking lag, short/mismatched collection, Posted collection journal and clearing residual, carrier collected/unremitted aging, operator receipt provenance/channel/reference/journal, unmatched bank/cash receipts, fee/member/currency discrepancies and dispute/reversal exceptions. Repair admin detail DTOs/download actions, bilingual strings and populated mobile layouts; redact provider bodies/protect evidence retention. Separate parser/job/account readiness from alwayshealthy catalog endpoint.

### Acceptance gate for a later phase

- Exact official fixtures, positive/negative parser/identity/money tests; no code-only capability upgrade.
- Non-financial jobs/cancellation/label/pickup/recovery tests use isolated contexts/mocks and prove actual canonical admission, not just DTO roundtrip.
- Separately authorized isolated PostgreSQL financial tests for receipt idempotency, overlapping/disjoint concurrent operations/refunds, atomic failure rollback, lost-response recovery, G/F/N entries, actual receiving-account refunds, currency/precision and shared permanent allocation. No shared merchant fixture.
- Actual external sanctioned logistics testing is separate from financial receipt testing; sandbox virtualized replies never prove merchant money. Future financial E2E certification requires explicit scope and evidence, not this report's mocks.

## 13. Containment, preservation and exact files changed

**Carrier Remittance remains disabled** by fresh health runtime flags, current source `PaymentLifecycleController.RecordCodSettlement:253–267` unconditional409/no call to dormant coordinator, no receipt ingress, passing T10 containment/Treasury source authority checks, and unchanged Manual source rejecting carrier membership. Retired admin410/generic clearing transfer restrictions were source/test checked; no live financial POST was used. Foundation health alone is not proof of every internal call, hence caller/source inspection also performed.

Before report creation, existing tracked working diff fingerprint (`git diff --binary | git hash-object --stdin`) was:

`dc3db7ebe83c3f46c6d19ebcaec14dfb94730783`

Preservation baseline for important existing untracked user files (`git hash-object`):

| File | Baseline hash |
|---|---|
| `Infrastructure/Payments/PaymentLifecycleService.ManualCodSettlement.cs` | `7b72ac580f30224df1e0a0276554c4a4f08cb64e` |
| `Infrastructure/Accounting/ManualCodSettlementAuthority.cs` | `3cebf62c2cabba0849793a0b138e2a31767bf601` |
| `Infrastructure/Accounting/SettlementRefundLineage.cs` | `f2482c510b76022e4ed6ede3da26bf42194a2c52` |
| `Core/Interfaces/Payments/ManualCodSettlement.cs` | `70f9c189cd579053236615e80b4dac44e7259c12` |
| `Tests/Integration/ManualCodSettlementTests.cs` | `44649d4a3c765ad95b8f4d51555bdfe782085d29` |
| `Infrastructure/Migrations/20261008223819_ManualCodPermanentSettlementIdentity.cs` | `b7b3469ef455a3ded367567f3a4dd5d4be5f2352` |
| `Web/src/pages/treasury/TreasuryManualCodSettlement.jsx` | `45de756de165a067a16e4c9554983d2bb72049e3` |
| `Web/src/services/manualCodSettlementApi.js` | `c530cead64685e56b1202ef79c9ffcece704a9a8` |

Table paths abbreviate the `PointOfSale.` project prefix only. Completion checks reproduced the identical tracked diff fingerprint and all eight listed hashes; final `git status --short` retained the pre-existing modified/untracked user files. These checks do not claim every ignored/untracked runtime cache was hashed. `git diff --check` and explicit untracked-report `git diff --no-index --check -- /dev/null ".ai/reports/SHIPPING_CARRIERS_END_TO_END_FORENSIC_AUDIT.md"` reported no whitespace errors (existing LF/CRLF notices only). All intentional repository edits in this phase are the report-only patch. Test TRX and browser snapshots/console artifacts reside under the preapproved temporary directory, not versioned application files; provider-document captures are tool-managed. Ordinary test-runner ignored caches are not application edits.

**Exact repository file created/changed by this audit:**

1. `.ai/reports/SHIPPING_CARRIERS_END_TO_END_FORENSIC_AUDIT.md` — this report.

Existing user application/test/UI/configuration/migration/report work is preserved. No settlement, collection, remittance, journal, refund or provider record was created by this audit. No supplied credentials are recorded here.

**STOP: audit/report only. Implementation, migrations, enabling carrier operations or any financial workflow require separate approval.**

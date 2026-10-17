# Shopixy — Carrier COD Remittance Forensic Audit & Implementation Readiness

## 1. Executive verdict

**AUDIT COMPLETE — NO CURRENT CARRIER CAN AUTHORIZE POSITIVE COD REMITTANCE SETTLEMENT.**

No implemented carrier integration retrieves or verifies a merchant remittance with permanent external settlement identity, exact shipment/order membership, gross, actual deducted fees, net, authoritative currency, receipt/finality status, and remittance date. Shipping registration, tracking, delivery, and customer collection are separate facts. Existing settlement journals and SQL recovery machinery do not supply the missing external evidence.

**No provider qualifies as READY FOR SANDBOX IMPLEMENTATION or ADAPTER FEASIBLE, CREDENTIALS REQUIRED for remittance.** Credentials alone would not resolve the contract gaps. Bosta, Aramex, and DHL have real but partial shipping clients; Mylerz is deliberately blocked; FedEx has real transport and selected operations but incomplete shipping/COD admission. None implements remittance.

Manual COD Settlement remains the certified, separate merchant-cashbox workflow. Its application source, financial boundaries, fixed destination, allocation protection, and actual-journal refund lineage are preserved. Carrier settlement remains disabled.

### Audit identity and evidence limits

- Working copy: `C:\Users\fadya\source\repos\PointOfSale`, MAIN/PRIMARY Windows only; branch `main`.
- HEAD: `256339621c0ea49f3602097cdacdf89ffe71754f`. Findings concern the **current working tree**, including pre-existing uncommitted prerequisite/Manual COD changes, not HEAD alone.
- Date: 2026-10-09 Windows local. Fresh MAIN health returned UTC `2026-10-08T23:19:02.7579537Z`; timestamps are reported as observed.
- All three required reports were read completely: `COD_SETTLEMENT_PRE_IMPLEMENTATION_FORENSIC_AUDIT.md`, `COD_SETTLEMENT_PREREQUISITES_CERTIFICATION.md`, and `MANUAL_COD_SETTLEMENT_FINAL_CERTIFICATION.md` in `.ai/reports/`.
- Earlier findings C1/C2 are superseded by the prerequisite implementation. Manual COD is implemented, superseding earlier statements that it was unavailable. The older `.ai/reports/COD_MANUAL_AND_CARRIER_SETTLEMENT_IMPLEMENTATION_CERTIFICATION.md` was also inspected: it explicitly records a historical stop, not positive carrier certification.
- No subagents, commit, LOAD-TEST/Dell access, production application edits, migration execution, financial POST, carrier API execution, provider login, new adapter, or financial-data fixture was performed.
- Fresh verification is source inspection, existing non-financial/mock backend tests, existing frontend tests, and a MAIN health GET. No current direct merchant-database row/index audit or positive financial E2E is claimed. Historical Manual certification results remain historical evidence, not this audit's new test counts.

References below are repository-relative files and exact methods, with current line ranges where useful. Documentation and constants are distinguished from executable behavior and credentialed provider proof.

## 2. Actual provider inventory and implementation status

### Registration is not operational authority

`PointOfSale.Core/Constants/CarrierCodes.cs:16–54` lists `manual`, `bosta`, `aramex`, `mylerz`, `dhl`, and `fedex`. `PointOfSale.API/Program.cs:983–1004` registers exactly these six adapters and FedEx OAuth/transport. `PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs`, constructor and `GetAdapter`, discovers DI registrations. Its `IsCarrierSupported` means registered, not financially certified.

The other `*CarrierDefinition.cs` files under `Infrastructure/Shipping/Adapters/` provide capability/catalog descriptions; Bosta/Aramex/Mylerz/DHL/FedEx definitions are not additional API clients. Manual's definition is itself a no-op adapter. Searches of adapter implementations, registration, carrier constants, and carrier-specific references found no other production shipping provider implementation. Test `Fake`/`Deterministic`/`RecordingCarrierAdapter` classes are shipping test doubles, not remittance providers.

`CarrierCapabilityRegistry.BuildCapability:83–183` classifies shipping as Manual ProductionReady, Bosta/Aramex/DHL Partial, Mylerz Unavailable, FedEx NotCertified. Source comments in `Program.cs` and adapter headers saying “production ready” or “all operations fail closed” are not authoritative: current methods and readiness gates determine the actual result.

### Implemented operations

All adapter paths in this table are under `PointOfSale.Infrastructure/Shipping/Adapters/`.

| Provider | Actual client and operations | Important exclusions / status |
|---|---|---|
| Bosta | `BostaAdapter.CreateShipmentAsync:90–279`: POST `/deliveries?apiVersion=1`; `CancelShipmentAsync:281–320`: DELETE `/deliveries/business/{escapedTracking}/terminate`; `GetTrackingAsync:322–430`: GET `/deliveries/business/{escapedTracking}`; `ValidateCredentialsAsync:432–493`: GET `/cities`. `GetSupportedServiceLevels` supplies local service metadata. | `GetQuoteAsync:40–88` returns **synthetic fixed EGP prices**, not authenticated carrier pricing. No dedicated label retrieval, pickup, remittance, payout, statement, or settlement operation. Creation copies label-independent shipment references, not a receipt. Credential validation against cities does not establish a finance entitlement or merchant payout account. |
| Aramex | `AramexAdapter.GetQuoteAsync:56–186`: POST `/RateCalculator/Service_1_0.svc/json/CalculateRate`; `CreateShipmentAsync:188–447`: POST `/Shipping/Service_1_0.svc/json/CreateShipments`, including label output; `GetTrackingAsync:466–601`: POST `/Tracking/Service_1_0.svc/json/TrackShipments`; `ValidateCredentialsAsync:603–668`: POST `/Location/Service_1_0.svc/json/FetchCountries`. | `CancelShipmentAsync:449–464` rejects with `ARAMEX_SHIPMENT_CANCELLATION_UNSUPPORTED`; pickup cancellation must not be confused with shipment cancellation. No remittance/statement API. `ValidateCreateShipmentRequest:933–1095` requires CODS and a positive COD amount, and currently admits COD money only in **USD** (`1084–1087`); this is an implementation constraint, not proof that Egyptian remittances use USD. |
| DHL Express / MyDHL | `DhlAdapter.GetQuoteAsync:54…`: POST `/rates`; `CreateShipmentAsync:193–469`: POST `/shipments`, returns tracking/AWB and encoded documents; `GetTrackingAsync:489–620`: GET `/tracking?shipmentTrackingNumber=…`. `AddAuthHeaders:862–869` is used for outbound HTTP. | `CancelShipmentAsync:471–487` returns `DHL_SHIPMENT_CANCELLATION_UNSUPPORTED`. `ValidateCredentialsAsync:622–643` checks only local presence and returns success **without HTTP**. `CreateShipmentAsync` does not map `IsCashOnDelivery`/`CodAmount` into a COD service; no collection/remittance financial DTO is parsed. No standalone pickup, bank-receipt, statement, or remittance execution. |
| Mylerz | `MylerzAdapter` is registered and contains prospective quote/create/cancel/tracking/account models and code. | All public operations gate on `OfficialPublicContractUnavailable() => true` (`713`): quote `62`, create `183`, cancel `357`, tracking `417`, credential validation `511`, service levels `564`, webhook `603`. They return `MYLERZ_OFFICIAL_CONTRACT_UNAVAILABLE`, no service levels, or null **before provider execution**. The hosts, Bearer/custom headers, payloads, HMAC logic, and collection fields below those guards are unverified dormant assumptions. |
| FedEx | `FedExAdapter.CreateShipmentCoreAsync:289–352` and `GetTrackingCoreAsync:354–400` call real `IFedExTransportClient`; typed `ValidateAddressAsync:221–287` and typed `GetServiceAvailabilityAsync:138–209` also execute. `FedExOptions:15–53` defines `/oauth/token`, `/ship/v1/shipments`, `/track/v1/trackingnumbers`, `/address/v1/addresses/resolve`, and `/availability/v1/transittimes`. `FedExResponseAdmission` validates response contracts. | Quote, cancellation, credential validation, standalone label, and pickup adapter operations fail closed; `ParseWebhook:109–115` returns null; supported service levels are empty. `CreateShipmentCoreAsync` returns initial Pending, and `BuildCreateShipmentRequest:402–441` does not map COD special-service money. DTOs/constants for other endpoints are not executed adapter operations. Shipping capability remains NotCertified and creation admission is blocked by readiness; direct adapter transport code nevertheless exists. No remittance operation. |
| Manual | `ManualCarrierDefinition.CreateShipmentAsync`, `CancelShipmentAsync`, `ValidateCredentialsAsync` are local no-ops; quote/tracking are unsupported and `ParseWebhook` is null. | No external authentication, sandbox, collection feed, or carrier financial authority. The certified dedicated **Manual COD Settlement** is real application accounting and must not be reclassified as carrier remittance or a fixture provider. |

The shared `IShippingCarrierAdapter.cs:18–99` has quote, create, create-recovery lookup, cancel, tracking, credential validation, service levels, and webhook parsing only. It has **no remittance contract**. `ShipmentTrackingResult:329–351` and `WebhookParseResult:399–414` expose status, collection boolean/time, and raw data; neither exposes actual collected money/currency, provider settlement ID, payout membership, gross/fees/net, or merchant receipt date.

## 3. Exact source files and external evidence contracts

### Bosta — strongest discovery candidate, still a contract gap

**Implemented shipment identity:** `BostaAdapter.CreateShipmentAsync` sends `businessReference = InternalShipmentId` (`146`), returns delivery ID/tracking/AWB (`205–257`), and stores `_id` in metadata. These can help match a shipment, provided tenant and carrier-account identity are qualified. They are not payout IDs.

**Current collection parser defect:** `ParseWebhook:526–570` requires uppercase `EventType` and `TrackingNumber`. It calls `TryMapBostaWebhookEvent` and returns null for unknown events (`548–551`) **before** testing `EventType == COD_COLLECTED` (`554`). `TryMapBostaWebhookEvent:595–611` has no `COD_COLLECTED` case. Thus no admitted event in this parser sets `CodCollected=true`. `GetTrackingAsync` also never sets collection truth. Delivery remains possible under its admitted string vocabulary, but current provider-fed customer collection is not established by these methods.

Fresh public official documentation fetched in this audit:

1. `https://docs.bosta.co/docs/how-to/get-delivery-status-via-webhook` — “How to use” and “Bosta States”: lowercase `_id`, `trackingNumber`, numeric `state`, `cod`, numeric `timeStamp`, and `businessReference`; delivered state 45. It expressly defines `cod` as **“The amount collected from your customer”**, only in Delivered state. This is not payment to the merchant. It supports custom webhook headers / an authorization key, not a documented mandatory provider HMAC signature.
2. `https://docs.bosta.co/api/api.yaml` — OpenAPI 3.0.1, `info.version=2.0.0`. `components.securitySchemes` defines `ApiKey` in Authorization and Bearer. `paths./businesses/{id}/transactions.get` (`operationId=Businesstransactions`) is Bearer-authenticated and returns only `data.unpaidCodAmount`, explicitly **collected but not yet transferred**. It is not implemented in Shopixy. The schema gives no per-remittance ID, completed-bank-transfer status, matched order list, gross/fees/net, currency, or remittance timestamp for this response.
3. The same YAML's `BusinessPaymentInfo` and `PaymentInfo` describe transfer frequency, schedule, method, limits, and payment configuration identity. `BankInfo` is beneficiary/account configuration. Wallet/Finance access-group descriptions mention transaction histories and payout methods, but **permission descriptions are not a published payout-evidence contract**.

The YAML was inspected via its complete captured tool output and targeted searches/reads, including the transaction operation. Its aggregate must not be differenced into invented payout evidence: collections, deductions, adjustments, and refunds can all change a balance. The HTML API reference (`https://docs.bosta.co/api/`) itself returned a loading shell; the published YAML supplied the actual examined contract.

**Fresh document/source mismatch:** the public numeric/lowercase webhook cannot pass the current uppercase/string-event parser. This audit does not patch it. Generic webhook event-ID extraction also accepts a property named `id`, but does not currently extract Bosta's `_id`. That `_id` is a shipment ID (not a unique state-change ID), so any future parser adaptation must establish event identity rather than adding `_id` as a permanent event ID and treating repeated shipment identity as a conflicting event. No new live webhook or exploit was sent.

`docs/architecture/BOSTA-1_ENDPOINT_RUNTIME_COMPATIBILITY.md`, sections Endpoint Inventory / Sandbox, corroborates implemented production routes while expressly excluding webhook-body and live-credential certification. Its code staging host is not independently certified by that report.

### Aramex — shipping contract does not establish COD financial semantics

`BuildClientInfo:859–876` supplies user/password/account/PIN/entity/country/version in JSON; creation sends `Reference1=OrderNumber`, `Reference2=InternalShipmentId`, and returns `ID` / `ForeignHAWB` (`226–227`, `348–424`). Tracking requires exactly one response keyed to the requested tracking number (`512–524`).

`GetTrackingAsync:542–553` rejects any event whose code is not admitted by `TryMapAramexStatus:801–842`. Later it searches for a code containing `COD` to set collection (`575–587`). **None of the admitted codes contains COD.** Likewise `ParseWebhook:747–753` rejects unknown codes before its COD substring check (`766–768`). Therefore the current code cannot establish a true collection flag through those branches. `COLLECTED` maps to package pickup, not customer money. Do not reinterpret SH003/Delivered as a bank receipt or silently synthesize collection.

Public-contract references already in the repository: `docs/architecture/ARAMEX-2_REQUEST_CONTRACT_COMPATIBILITY.md`, official ShippingAPI.V2 service / ClientInfo / COD request validation, and `docs/architecture/PCC-1_OFFICIAL_SHIPPING_PROVIDER_DOCUMENTATION_AUDIT.md`, Aramex section. They establish shipping/rating/tracking/location authorities, not a carrier COD payout statement or settlement-ID contract. Current no-webhook capability prevents the legacy parser from being a live trusted ingress. A private local-market COD finance contract could exist, but none is established by the inspected sources.

### DHL Express — logistics evidence, no implemented COD money evidence

`CreateShipmentAsync` sends customer order reference (`309–312`); `GetTrackingAsync:540–552` checks the response shipment number against the requested tracking number. Tracking outputs delivery timestamps/status, not collection or merchant receipt. `ParseWebhook:782–791` explicitly sets `CodCollected=false`, and capability gating disables DHL webhooks despite a parser/HMAC helper remaining in source.

`docs/architecture/DHL-1_OFFICIAL_CONTRACT_AND_RUNTIME_COMPATIBILITY.md` references MyDHL v3.3.1 at `https://developer.dhl.com/api-reference/dhl-express-mydhl-api?lang=en&language_content_entity=en`. These shipping references do not establish a COD remittance source. Source searches confirm no COD-service amount mapping in `DhlAdapter`; the name DHL must not imply other DHL divisions' finance products are integrated.

### Mylerz — blocked assumptions, not evidence

`docs/architecture/MYLERZ-1_OFFICIAL_CONTRACT_AND_RUNTIME_COMPATIBILITY.md:22–87` records absent vendor-issued public technical authority for hosts, auth, shipment/tracking/webhooks/sandbox. Current fail-closed code confirms that boundary. Dormant response fields `CodCollected` / `CodCollectedAt` and dormant webhook parsing cannot prove even executable collection, much less merchant remittance. Obtain a versioned vendor package; do not resurrect guessed URLs or HMAC semantics.

### FedEx — actual transport, incomplete operational/COD integration

`FedExOptions.cs`, `FedExOAuthClient.GetAccessTokenCoreAsync` / `AcquireTokenAsync` / `RefreshAuthorizationHeaderAfterUnauthorizedAsync`, and `FedExTransportClient.SendJsonAsync` implement real OAuth, cached Bearer headers, finite retries, and authenticated HTTPS requests. `FedExResponseAdmission.AdmitTracking:269–440` checks internal consistency of two response tracking identifiers and known statuses; it does **not bind them to the requested number** in `FedExAdapter.GetTrackingCoreAsync`, and returns no collection money/flag. A future matching boundary must independently bind request to response and tenant/account.

Provider response `transactionId` is request/response correlation in these transport/admission DTOs, **not a financial settlement ID**. `BuildPackageLineItem:667–685` supplies shipment/customer references. `GetShipmentByCarrierRequestIdAsync:61–67` returns null. `docs/architecture/FEDEX-5_OPERATIONAL_READINESS_CERTIFICATION.md` documents shipping sandbox/onboarding dependencies historically; its “OAuth not implemented” statements predate current transport code and are not used as current implementation truth. No inspected FedEx contract supplies COD remittance finality or order allocation.

### Required remittance fields — provider-by-provider conclusion

| Provider | Provider settlement/batch ID | Actual remittance gross/fees/net/currency/date | Exact payout membership / merchant receiving account | Present authority |
|---|---|---|---|---|
| Bosta | None implemented or established by examined financial response | None; `unpaidCodAmount` is an untransferred aggregate; quoted shipping prices are not actual deductions | No remittance member list or receipt; shipment references exist | Logistics API and documented customer collection only |
| Aramex | None | None | Shipment ID/ForeignHAWB/reference fields exist; no payout linkage | Logistics API; collection branch currently unreachable |
| DHL Express | None | None | AWB/tracking/customer reference only | Delivery/tracking; no mapped COD collection |
| Mylerz | None | None | Prospective fields below an unconditional gate | No executable external authority |
| FedEx | None; `transactionId` is not settlement identity | None | Tracking/customer references only | Selected logistics transport; no COD authority |
| Manual | No external carrier identity | Server-owned cashbox gross, zero fees, tenant currency, operation date in dedicated Manual workflow | Certified whole-order membership and posted Cashbox debit | Merchant cash operation, not carrier remittance |

No external provider request was made to substantiate account-specific permissions, merchant identity, bank destination, finance reports, or sandbox finality. No claim is made that private provider finance products cannot exist.

## 4. Authentication, replay protection, tenant isolation, retries, and sandbox

### Outbound credentials and environment

`CarrierIntegrationAuthority.LoadConfigurationAsync:693–703` loads tenant/carrier/nondeleted configuration; `ResolveCredentialsAsync:248–301` and `BuildReadinessAsync:705–805` apply purpose-specific admission and configuration fingerprints. `BuildCredentials:829–861` decrypts API key/secret/configuration and supplies AccountId/UseSandbox. Tracking and Cancellation are exceptions to the executable-readiness requirement (`PurposeRequiresExecutableReadiness:988–991`); they must not be described as uniformly enforcing creation readiness.

`CarrierCredentialEncryptionService.Encrypt` / `Decrypt` use ASP.NET Data Protection purpose `Shipping.CarrierCredentials.Secrets`, `ENC:` prefix; failed decryption returns empty and legacy plaintext is accepted. This is storage protection, not authenticity of provider financial facts. No client certificate, mTLS, asymmetric provider-signature, or signed-statement verifier is implemented in these inspected carrier paths; normal HTTPS uses platform TLS validation.

| Provider | Implemented outbound auth | Webhook trust that can actually be admitted | Sandbox/test evidence |
|---|---|---|---|
| Bosta | Direct `Authorization: ApiKey` in all four outbound methods | Capability permits shared-secret webhook; Bosta-specific controller reads Authorization. Central policy can be configured to HMAC/shared-secret/IP modes; only documented custom-header semantics are established by fresh provider docs. | `UseSandbox` selects hard-coded `https://staging-api.bosta.co`; production `https://app.bosta.co/api/v2`. No verified remittance sandbox contract/access; existing endpoint report calls sandbox uncertifiable. |
| Aramex | `ClientInfo` username/password/account/PIN/entity/country in HTTPS request body | **Disabled by capability**. `ReceiveAramexWebhook` and legacy parser existing in source do not change this. | Hard-coded `https://ws.sbx.aramex.net/ShippingAPI.V2` vs production `https://ws.aramex.net/ShippingAPI.V2`; shipping sandbox path exists, COD remittance test path unknown. |
| DHL Express | HTTP Basic API key/secret; shipper AccountId in requests | **Disabled by capability**; dormant parser HMAC is not a verified live DHL webhook contract | `https://express.api.dhl.com/mydhlapi/test` vs production `/mydhlapi`; logistics test support is not remittance simulation. Local-only validation does not prove access. |
| Mylerz | No executed outbound auth; dormant Bearer/custom headers unverified | Disabled; parser returns null | Code constants for `api.mylerz.com/v2` / `sandbox-api.mylerz.com/v2` are blocked, not usable verified environments. |
| FedEx | OAuth client_credentials (adapter), Bearer; OAuth contract supports other credential grants | Disabled; generic route cannot overcome null parser / capability | `https://apis-sandbox.fedex.com/` vs production `https://apis.fedex.com/`; real logistics transport exists. No remittance entitlement/test contract. |
| Manual | None | Not allowed | Local no-op; no carrier sandbox |

Bosta/Aramex/DHL ignore `CarrierCredentials.ApiUrl` in favor of adapter constants (`GetBaseUrl` or inline UseSandbox selection); configuration presence is not proof of a working finance endpoint. FedEx uses its options, not a remittance URL.

### Webhook authentication and matching

1. `PointOfSale.API/Controllers/ShippingWebhooksController.cs`, `ReceiveWebhook`, carrier-specific `Receive*Webhook`, `HandleWebhookAsync:147–281`: generic POST `/api/webhooks/shipping/{carrierCode}`, fixed Bosta/Aramex/Mylerz/DHL variants. Anonymous HTTP ingress is protected by content-type validation, size limit, rate limiting, request pipeline, and service trust checks—not POS authentication. Generic reads `X-Webhook-Signature`; fixed routes read Authorization / X-Aramex-Signature / X-Mylerz-Signature / X-DHL-Signature. No remittance route/consumer is present.
2. `API/Services/Shipping/ShippingWebhookRequestPipeline.cs`, `ShippingWebhookTenantResolver.ResolveAsync:245–319`, `ResolveByShipmentIdAsync:321–374`, `ResolveByTrackingNumberAsync:376–441`: parse for locators, then resolve persisted nondeleted carrier/shipment/tracking. Claimed tenant is a constraint, not authority. Unknown/ambiguous matches reject; request context takes tenant from SQL.
3. `CarrierShipmentService.ProcessWebhookInternalAsync:1482–1639` parses untrusted data for matching, resolves a unique same-tenant carrier/tracking shipment and order, obtains configured trust, and validates trust **before mutation/log admission**. Adapter is subsequently called with null signature/secret because central validation owns trust.
4. `CarrierIntegrationAuthority.ResolveWebhookTrustAsync:640–691` requires enabled config, SupportsWebhooks in both config and capability, and required secret. `ResolveWebhookTrustPolicy:993–1046` supports configured HMAC/shared-secret/IP modes; fallbacks for disabled providers are unreachable while capability forbids webhooks. Bosta is the only enabled external webhook capability here.
5. `CarrierShipmentService.ValidateWebhookTrust:2086–2108`, `ValidateHmacWebhookSignature`, `ValidateSharedSecretWebhook`, `ValidateIpAllowlistWebhook`: HMAC-SHA256 over UTF-8 payload, normalized hex/base64 signature and fixed-time equality; fixed-time shared-secret equality (Bearer normalization); exact normalized IP allowlist. Shared secret is **not a payload-bound provider signature**; IP trust is not independently signed financial evidence. `SignatureVerified=true` in the log means configured trust succeeded, including token/IP, not necessarily HMAC.

No provider signing-key certificate chain, signed event timestamp/freshness window, nonce, or bank-statement authenticity verification is established by these mechanisms. Central trust-mode availability is not proof that a particular provider implements that signing protocol.

### Replay, retries, and recovery boundaries

- `CarrierShipmentService.PrepareShippingWebhookLogAsync:1901–2036` hashes raw payload, scopes event/hash lookup by tenant+carrier, rejects same event ID/different hash with `WEBHOOK_TAMPERED`, collapses Completed/Skipped/Processing duplicates, and retries failed records. `ResolveWebhookEventId:2405–2419` takes eventId/event_id/id/webhookId/updateId or `payload:{SHA256}`. Byte-level hash deduplication does not establish economic remittance identity or semantic equality across differently encoded payloads.
- `ApplicationDbContext.cs:1775–1801` defines unfiltered unique `(TenantId,CarrierCode,EventId)` and `(TenantId,CarrierCode,PayloadHash)` indexes. `ShippingWebhookLog.cs` persists redacted payload summary, hashed signature, verification method, target IDs, processing status/time, and redacted source info. These are **shipping audit/replay records**, not immutable economic remittance records.
- Webhook Processing log is saved before `BeginCarrierMutationTransactionAsync` (`1683`, `1709`). A process crash in that gap can leave Processing; subsequent retries are collapsed as duplicates (`1968–1977`). No processing-age takeover is present in that method. The new evidence architecture must have an explicit lease/recovery policy instead of copying this pattern. This is a source-level failure window, not a demonstrated live loss.
- Shipment mutation uses a Serializable transaction when owning a relational scope (`BeginCarrierMutationTransactionAsync:4357–4367`), reloads shipment/order, checks Manual ownership, applies status progression, and publishes events before save/commit. It does not authenticate payout or atomically post a settlement journal.
- `Program.cs.CreateExternalHttpRetryPolicy:1272–1299` / `ConfigureExternalHttpClient:1301–1323`: named clients have 15-second timeouts; GET/HEAD/OPTIONS get two transient/429 retries (250/500ms); non-idempotent calls retry only if Idempotency-Key is present. Bosta/Aramex/DHL shipment creation sends no such header. A durable local CarrierRequestId does not mean the provider honors an idempotency key.
- `IShippingCarrierAdapter.GetShipmentByCarrierRequestIdAsync` defaults to null; FedEx explicitly returns null. `CarrierShipmentService.ReconcilePendingCarrierRequestAsync:2787…` tries lookup and fails closed when unavailable rather than blindly creating again. This protects registration, not remittance allocation.
- FedEx additionally has account-number/client-ID/environment-keyed token caching and per-key semaphore, 60-second expiry skew, default one OAuth retry, and default one transport retry (`FedExOptions.cs`, `FedExOAuthClient.CreateCacheKey:419–423`). Tenant ID and secret/credential version are not part of this token-cache key; it is not a new remittance tenant/account-isolation guarantee. `FedExTransportClient.SendJsonAsync:67–182` retries timeout/network/408/429/5xx and refreshes once after 401; this layer also handles shipment POST and does not prove provider financial idempotency. Transport `x-customer-transaction-id` is correlation only. A remittance read adapter would need contract-specific safe retries and bounded pagination.
- `Shipping/BackgroundJobs/TrackingSyncJob.cs`: every 30 minutes; 15-minute minimum sync age, 50 shipments/tenant, concurrency five with separate scopes. It excludes Delivered/Cancelled/Returned (`43–48`, `105–113`), although direct `SyncTrackingAsync` permits Delivered to observe later returns/collection. Therefore scheduled tracking is **not a guaranteed post-delivery collection/remittance catch-up mechanism**.
- `ShipmentCodCollectedConsumerDefinition:76–94` has concurrency eight and 1/5/15-second retries. Longer out-of-order delivery requires explicit recovery/redelivery; finite retries do not prove eventual settlement readiness.

## 5. Complete evidence lifecycle and collection versus remittance authority

### Shared actual collection lifecycle

```text
Tenant-owned carrier credentials → HTTPS tracking response
  OR shipping webhook → SQL target resolution → configured trust validation
→ adapter ShipmentTrackingResult / WebhookParseResult (collection boolean/time only)
→ CarrierShipmentService shipment mutation + ShippingWebhookLog where applicable
→ persisted Shipment.CodCollected / CodCollectedAtUtc
→ ShipmentCodCollectedEvent (tenant/order/shipment locators)
→ ShipmentCodCollectedConsumer.Consume
→ PaymentLifecycleService.RecordCarrierCodCollectionAsync
→ ValidateCodCollectionAdmissionAsync (re-read SQL shipment + order)
→ canonical Pending → Collected, accounting outbox
→ Posted Order_COD journal Dr1130 / Cr4020
→ eligible to be considered for a future remittance only after independent receipt proof
→ STOP: no provider remittance evidence / adapter / enabled execution ingress
```

Exact authorities:

- `CarrierShipmentService.SyncTrackingAsync:1150–1394` and `ProcessWebhookInternalAsync:1746–1854` set collection once for COD shipments, persist raw tracking response/time, enforce status progression, synchronize fulfillment, and publish collection event. A missing provider collection timestamp falls back to server time. That timestamp is an observation/default, not provider money-transfer time.
- `PublishShipmentCodCollectedEventAsync:4622…` publishes shipment/tenant/order, expected COD amount/currency, and a shipment+collection-time idempotency string. The consumer (`Infrastructure/Consumers/ShipmentCodCollectedConsumer.cs:27–72`) forwards **locators only**; untrusted event money/actor never selects ledger facts.
- `PaymentLifecycleService.RecordCarrierCodCollectionAsync:328–347`, `ValidateCodCollectionAdmissionAsync:349–399`, called before successful replay (`2349`), requires Carrier ownership, SQL shipment ID+tenant+order, same carrier, IsCashOnDelivery, persisted collection flag/time, expected `CodAmount == order.Total`, matching currency, and Delivered truth for initial recognition. It reconstructs tracking/actor from shipment and overwrites request amount/currency from order. Operator collection (`MarkOfflineCollectionDeliveredAsync`) instead requires Manual ownership and no carrier snapshot.
- `Shipment.CodAmount` is **expected amount to collect**, populated from order total on shipment creation (`CarrierShipmentService` order-to-shipment/request construction, e.g. `362`, `479`, `919`). The current normalized provider response cannot attest the actual collected amount/currency. SQL qualification was prerequisite-certified; it does not cure unreachable provider parsers or turn expected money into independently verified actual receipts.
- `FulfillmentDeliveredEventConsumer.Consume`, in `Infrastructure/Consumers/OrderDeliveredEventConsumer.cs:57–65`, explicitly waits for collection evidence. Delivered does not collect.
- `CommerceAccountingService.PostCodDeliveredAsync:1396–1430` posts order.Total with key `COD_DELIVERED:{tenant}:{order}`. Lifecycle collection accounting staging/recovery (`PaymentLifecycleService.EnqueueSuccessOutboxAsync:6264…` → `IAccountingOutboxService.EnqueueCodDeliveredAsync`, SQL-authoritative committed posting methods) can leave Collected before Posted; settlement must require the exact Posted journal, not a payment label alone.

### Provider-specific stop points

| Provider source → normalized evidence → persistence / eligibility | Where the real chain stops |
|---|---|
| Bosta authenticated tracking → admitted delivery/status → shipment/fulfillment; token-authenticated legacy webhook → admitted mapped status → log/shipment | Tracking never supplies collection; `COD_COLLECTED` webhook is rejected; official documented webhook format is incompatible. Even an adapted delivered `cod` would prove customer collection only. No merchant receipt continuation. |
| Aramex authenticated keyed tracking → admitted events → shipment/fulfillment | COD substring branch is unreachable; webhook capability is disabled. No authenticated receipt/statement continuation. |
| DHL Basic-auth tracking → matching AWB + delivery → shipment/fulfillment | No mapped collection money/flag; disabled webhook. No merchant receipt continuation. |
| Mylerz source request / webhook | Stops at unconditional contract-unavailable gate; no admitted external evidence. |
| FedEx OAuth → HTTP → strict logistics admission → normalized tracking → tenant shipment | No COD mapping/collection truth, response/request identity binding gap, creation readiness NotCertified. No remittance continuation. |
| Existing legitimately qualified carrier SQL collection history, regardless of provider | Can produce certified customer collection recognition through canonical lifecycle; cannot move Clearing into Bank without new independent provider remittance authority. |

**Economic boundary:** carrier collection ends with evidence that customer money is held/owed by the carrier and Dr1130/Cr4020 recognition. Merchant remittance begins only when a verified provider contract and authenticated source establish an actual final payment to the merchant's identified receiving bank account, with the exact orders and deductions. A scheduled payout, wallet credit, unpaid COD decrease, transfer initiation, delivered flag, operator confirmation, tracking ID, or caller-authored BankReference does not establish that final receipt. If “paid” means merely instructed/dispatched, independently authenticated receiving-bank confirmation must complete the proof before Dr1030.

## 6. Current accounting and persistence model — compatibility assessment

### Journals and distinct execution contracts

Expected and currently implemented collection composer:

```text
Dr 1130 COD Clearing        G
Cr 4020 E-commerce Sales    G
SourceType = Order_COD; SourceId = order.Id
```

Expected remittance and retained historical composer (`CommerceAccountingService.PostCodSettlementAsync:1957–2004`):

```text
Dr 1030 Bank                G − F
Dr 6020 Shipping Fees       F, when positive
Cr 1130 COD Clearing        G
SourceType = CODSettlement; SourceId = batch.Id
IdempotencyKey = COD_SETTLEMENT:{tenant}:{settlementId}
```

That composer accepts money parameters and does not verify provider receipt. `FinancialPostingService.ValidateTreasuryInflowAuthorityAsync:616–753` validates CODSettlement against an existing nondeleted same-tenant OFFLINE_COLLECTION batch/source/date/count and caps cash increase; it does not inspect external remittance evidence. These are retained historical SQL accounting authorities, not a live carrier settlement capability.

The separate, enabled Manual contract remains `MANUAL_COD_COLLECTION` / `ManualCodSettlement` / `COD_MANUAL_SETTLEMENT:{tenant}:{GUID}`, zero fees, Dr actual Main Cashbox1010 / Cr1130. `ManualCodSettlementAuthority.BuildPostingAsync:86–118` and `FinancialPostingService.ValidateManualCodSettlementInflowAsync:806–815` independently verify its exact SQL authority. Carrier orders cannot qualify for it.

### Persisted identity, allocations, currency, and immutable lineage

- `Core/Entities/PaymentSettlementBatch.cs` stores tenant/type/settlement ID, optional Provider/PayoutBatchId/BankReference, GrossAmount, Fees, Currency, OrderCount, SettledAtUtc and BaseEntity audit fields. **No external authentication record, source environment/account contract, verified net, remittance evidence ID, or independent receipt status is persisted.** Net can be derived; authenticated net cannot currently be reconciled.
- `PaymentSettlementOrder.cs` stores exact tenant/batch/order, optional intent, whole order total/currency; shared allocation across all settlement kinds.
- `ApplicationDbContext.cs:898–943` has permanent unique `(TenantId,SettlementType,SettlementId)` and `(TenantId,OrderId)`, numeric(18,2) gross/fees/order amounts, tenant filters, and ID-based FKs. The batch/member/order triple index is still filtered, but the stronger permanent tenant/order index remains. `20261008223819_ManualCodPermanentSettlementIdentity.Up` removes the two identity/allocation soft-delete filters. Its applied MAIN status (46 migrations) was certified previously; this audit reads source and does not rerun migration/schema queries.
- Permanent batch identity **does not include Provider, carrier account, or environment**. A remittance adapter must scope its external identity permanently and map it deterministically to a canonical settlement identity; caller text `SettlementId`/`ShippingCompany` is insufficient. Do not create a parallel order-allocation table or weaken the shared permanent allocation index.
- `ApplicationDbContext.GuardTreasuryCashAuthority:7156…` guards economic batch/member/ledger-line modification/deletion, authorized journal creation, account-balance writer, and posted journal financial identity. These are application/EF guards, not proof of protection against privileged SQL deletion or every cascade outside EF.
- `Core/Models/Accounting/PostingRequest.cs` has no currency dimension on journal requests/lines; account balances are scalar. A new boundary must enforce **active tenant ledger currency == evidence currency == every order/shipment/member currency**, supported currency precision, and numeric(18,2) representability. Same-currency orders alone are insufficient.

### Requirements matrix

| Requirement | Current proof / reusable mechanism | Carrier readiness / required addition |
|---|---|---|
| Exact order membership | Durable PaymentSettlementOrders, whole totals, permanent tenant/order uniqueness; Manual exact Posted collection-line validation (`ManualCodSettlementAuthority.ValidateOrdersAsync:44–84`) | Reuse allocation. Obtain complete provider membership, qualify tenant+carrier-account+shipment/order identity, and reject unmatched/duplicate/partial batches. No current provider proof. |
| One authoritative currency | Manual resolves active tenant Currency and reference precision (`ResolveAccountsAsync:18–42`) | Dormant offline coordinator only normalizes supplied currency and checks orders. New carrier boundary must enforce tenant/account currency and precision without FX guesses. |
| Gross/fees/net reconciliation | Composer supports 0 ≤ F < G algebra; batch has G/F; dormant `RecordOfflineSettlementAsync:401–635` checks gross against summed totals and fee bounds | Independently observed net and actual deduction meaning absent. Reject taxes/debt offsets/holdbacks/unrelated shipping invoice deductions unless the provider contract proves they fit the approved F policy. |
| Permanent external identity | Permanent batch/allocation/journal indexes and immutable economic facts | Add verified tenant+provider+merchant account+environment external identity/fingerprint binding; finance identity is not webhook event ID or shipment ID. |
| Duplicate/replay rejection | Journal key, shared SQL constraints, Manual retry reads exact original result | Dormant `SettlementBatchMatches:1782–1806` compares type/ID/provider/payout/G/F/currency/order set but not BankReference/remittance date/auth/finality. No canonical remittance fingerprint or freshness admission. |
| Safe concurrency / atomic posting | Lifecycle sorted PostgreSQL order FOR UPDATE (`LoadOrdersForSettlementMutationAsync:3251–3272`), Serializable scope/savepoints; FinancialPostingService joins current transaction (`83–98`); account xmin | Manual makes batch/member/journal/balances/state/receipt/events one commit (`PaymentLifecycleService.ManualCodSettlement.cs:49–125`). Dormant carrier coordinator commits Settled before best-effort journal (`595–634`). It is not the certified atomic Manual workflow. Reuse infrastructure with a distinct carrier authority and race/conflict policy. |
| Crash recovery | Existing accounting outbox and SQL replay; Manual `RecoverReceiptAsync:133–147` acknowledges exact posted history only | `AccountingOutboxProcessor.ProcessCodSettlementAsync:3523–3562` reloads SQL via `AccountingLedgerAuthority.LoadCurrentSettlementAsync` / `EvaluateSettlementPosting:238–323`; checks header/count/fee bounds, not provider authenticity. Future evidence capture needs recoverable leases and atomic application, no “Settled/Posted” before commit. |
| Actor/audit lineage | Manual authenticated GUID, SQL active Owner/Admin, batch/member CreatedBy, journal UserId, accounting receipt/journal | Carrier collection reconstructs actor as carrier code. Dormant `CreateSettlementBatch`/COD journal composer leave actor unset; webhook log is not remittance operator/service audit. New ingress must bind service identity, initiating operator when applicable, integration/account/credential version, contract/verifier, evidence and journal. |
| Refund destination | Actual immutable receiving debit resolved, not hard-coded kind/account | Compatible for positive-net single destination; any new settlement type/source must be recognized explicitly and preserve historical contracts. No fallback to Bank or Cashbox. |
| Zero-net / partial / split / reversal / dispute | Fail-closed refund lineage; whole-order allocation | Unsupported as automatic first-scope execution. Details below. |

`FinancialPostingService.PostJournalInternalAsync:124–136` sorts account reads; despite its “LOCK AND LOAD” comment, this query is not explicit account FOR UPDATE. `Data/Configurations/AccountConfiguration.cs:95–100` uses PostgreSQL xmin optimistic concurrency. Correct race behavior requires clean-context re-read/retry and permanent indexes, not overstated pessimistic account locking.

### Actual posted receiving-account refund lineage

`SettlementRefundLineage.ResolveDestinationAccountAsync:17–110` independently loads current tenant order/batch/members/journal/lines/accounts/mappings; validates currency, gross/member count/amounts, matching type/reference/date, exactly one nondeleted Posted journal, balanced positive lines, one clearing credit, and **exactly one receiving debit == G−F** on an active same-tenant mapped non-clearing Cashbox/Bank Asset. Other debit legs must be Expense. It returns that debit's AccountId.

Callers: `PaymentLifecycleService.ResolveRefundAccountingContextAsync:4981–5045`, `CommerceAccountingService.ResolveEcommerceRefundCreditAccountIdAsync:1808–1835`, and `FinancialPostingService` settlement refund outflow validation (`1135`). Retain all three independent checks. Historical OFFLINE_COLLECTION/CODSettlement and GATEWAY/GatewaySettlement still resolve actual Bank lines; Manual resolves its actual posted destination. If a new carrier kind is introduced, resolver/recovery/source validation need a bounded explicit contract extension in a separately authorized phase.

### Unsupported economic cases — explicit policy

| Case | Current limitation | Smallest safe future treatment |
|---|---|---|
| F=0, G>0 | Two positive Bank/Clearing legs are representable | Admit only when authenticated evidence agrees and other gates pass. |
| F=G (zero net) | Dormant composer calls `PostingLine.Debit(0)`, which throws (`PostingRequest.cs:84–87`); refund resolver rejects F≥G and has no receiving debit | Persist/retain verified evidence as Unsupported/Review, **no allocation, Settled transition, or journal**. Separate decision needed for fee-only journal and future refund funding. Do not claim current support. |
| F>G / negative G or F / net mismatch | Outside accounting/evidence bounds | Reject/quarantine without money movement. |
| Partial amount for an order / residual after refund | Whole-order allocation cannot express residuals; refunds reduce recognized clearing | Do not settle original gross after a refund. Reject/quarantine whole batch; no silent exclusion of a refunded member. |
| Split receipt / multiple bank destinations / multiple payouts per order | One receiving line and permanent whole-order membership cannot resolve proportional refund destinations | Unsupported; do not pick an arbitrary account, sum multiple receipts into a made-up finality, or mark whole order Settled. |
| Initiated/scheduled/pending/failed transfer | No proof of actual merchant money | Evidence may be held Pending/Rejected; accounting stays Collected, no Bank inflow recognition. |
| Reversed remittance | Generic journal reversal exists, but no certified carrier external-reversal/allocation/payment-state lifecycle | Quarantine and surface original linkage; no automated reversal/reallocation. Preserve original economic rows/IDs. Require separate reversal domain approval. |
| Disputed/held remittance or unresolved provider adjustment | Authenticated does not mean unconditional final receipt | Explicit hold/review; no automatic execution until contract finality and discrepancy are resolved. |

A full refund after fees may exceed the original net bank receipt; the existing refundable-order cap and receiving-account checks remain authoritative. Carrier fee recovery, additional bank funding, and refunding provider deductions must not be invented by the adapter. First-scope tests must preserve current rejection/account-balance policies.

## 7. Provider-by-provider remittance readiness

Classification is for **Carrier COD Remittance**, not shipping in general.

| Provider | Classification | Evidence-backed reason |
|---|---|---|
| Bosta | **CONTRACT GAP** | Real shipment client and documented customer collection; published aggregate explicitly concerns **untransferred** money. No established completed-payment ID/member/G/F/N/currency/date/destination contract or verified sandbox finance path. Current collection parser mismatch is an additional blocker. |
| Aramex | **CONTRACT GAP** | Real shipping/rating/tracking client and logistics sandbox; no established local COD remittance contract/statement; current COD flag logic unreachable and USD-only create constraint. Credentials alone cannot fix meaning or exact membership. |
| DHL Express | **CONTRACT GAP** | Real MyDHL logistics client/test URL; no mapped COD collection service or remittance contract for this product/account/market. Other DHL offerings are not this integration. |
| Mylerz | **NOT IMPLEMENTED** | Registered incomplete code, unconditional contract-unavailable gates, no verified runnable shipping/remittance adapter. Underlying documentation gap must be resolved first. |
| FedEx | **NOT IMPLEMENTED** | Selected actual shipping transport exists, but Carrier COD collection/remittance operations are not implemented and shipping admission remains NotCertified. No remittance contract. This classification does not deny implemented OAuth/create/track/address/availability code. |
| Manual catalog entry | **NOT IMPLEMENTED** as an external carrier remittance provider | No external carrier source by design. Certified Manual COD Settlement remains **implemented and intact**, under a different authority. |

No provider's row is upgraded on the basis of its catalog SupportsCod flag, test URL, interface, source comment, old architecture certification, or a successful logistics credential test.

## 8. Missing credentials, documentation, business evidence, and environment dependencies

### Common provider evidence package required

Before a real remittance adapter can be authorized, obtain from the chosen carrier:

1. Versioned official API/statement/report contract identifying the product, merchant legal account, country, and payout destination. A screenshot or arbitrary uploaded CSV without verifiable origin is insufficient.
2. Exact read endpoint/export retrieval or verified webhook contract; authentication entitlements, signing rules if applicable, credential rotation/account binding, and documented response/error schema.
3. Definition of each transfer status: specifically whether a status proves completed merchant receipt, only carrier dispatch, or only a wallet credit. If bank receipt is not guaranteed, obtain independent authenticated bank confirmation and matching contract.
4. Permanent unique settlement/transfer/batch ID, account/environment scope, corrections/reversals and identity-reuse rules; complete per-shipment membership (including all pages), stable shipment references and ambiguity rules.
5. Gross collected and remitted, each fee/deduction and tax/adjustment meaning, actual net transferred, explicit currency/precision, dates/timezone, receiving bank account identity, and reconciliation rules.
6. Retry/rate-limit/pagination/finality/event-sequence guarantees, duplicate/timeout behavior, delayed settlements, and dispute/reversal semantics.
7. Vendor-confirmed test/sandbox facility or sanctioned isolated finance test account, deterministic positive/negative receipt behavior, finance-entitled read credentials, and sample authenticated completed/pending/reversed statements linked to identifiable test orders.
8. Merchant business reconciliation sample: one provider completed remittance, its exact orders and fee breakdown, and matching receiving-bank credit. This confirms the economic interpretation before automatic posting.

### Provider-specific blockers

- **Bosta:** finance/wallet-entitled merchant account access; actual per-payment statement/transaction feed (not `unpaidCodAmount`); provider confirmation of final receipt semantics; official staging URL/version/finance test behavior. Fixing collection parser against documented state/cod is a future bounded prerequisite, not remittance proof.
- **Aramex:** country/account-specific COD collection and finance agreement, remittance statement schema and endpoint, definitive collection event/code, sanctioned finance sandbox; validate actual supported settlement currency against USD-only current COD request rule.
- **DHL:** identify Express/MyDHL COD service availability for merchant/lane, required service mapping, actual collection/remittance product and finance interface; MyDHL shipping credentials/local validation do not grant finance access.
- **Mylerz:** vendor-issued complete shipping and finance contract with confirmed hosts/auth/sandbox before any prospective code can be used.
- **FedEx:** approved merchant project/account credentials and operation/COD service entitlement, shipping readiness/mapping coverage, actual collection/remittance product contract and finance sandbox. Logistics OAuth does not prove finance permission.

No live carrier finance credential availability was audited or tested; credentials are **not verified available**, rather than asserted absent from every secret store. No secrets were printed or copied. This distinction does not change the contract-gap verdict.

Application dependencies for a future isolated relational implementation include MAIN source, PostgreSQL with current permanent identity migration, active tenant currency/reference precision and mapped Bank1030/Clearing1130/Fees6020, transactional event/accounting infrastructure, and tenant-owned carrier integration/account authority. MAIN's current aggregate health remains Unhealthy because `payout_external_truth_readiness` has Critical reconciliation / one mismatch; it is not evidence of a COD defect or usable carrier payout feed.

## 9. Recommended first provider and smallest safe scope

**No real-provider end-to-end implementation is currently recommended for authorization.** The missing receipt contract is a hard prerequisite; do not fabricate an adapter around caller amounts, bank references, collected flags, or balance deltas.

**First documentation/account-access target: Bosta**, because a real local shipping client, shipment business reference, documented customer-collected amount, and a published business finance aggregate provide a concrete discovery starting point. This is a recommendation to obtain and prove a contract, **not READY/ADAPTER FEASIBLE classification**. The aggregate cannot serve as the receipt.

Conditional smallest end-to-end workflow **only after** those blockers close and a separate implementation decision:

1. One verified Bosta merchant account and vendor-confirmed isolated finance test environment; one active tenant ledger currency (EGP only if the provider contract explicitly supports it), one mapped Bank1030 destination.
2. Server-side authenticated **read-only fetch of one externally identified completed remittance** and all its membership pages. Avoid adding a new finance webhook initially if a provider receipt read exists. No browser-authored money, membership substitution, “verified” checkbox, or bank-reference authority.
3. Whole delivered/collected Carrier COD orders for that exact provider/account, exact Posted Dr1130/Cr4020 lineage, no refunds/prior allocations; complete batch within a bounded limit (initially at most100). Reject rather than truncate a provider batch.
4. G>0, 0≤F<G, authenticated N=G−F, one currency/precision, supported fee meaning, identified completed bank receipt. Hold pending/disputed/partial/split/zero-net/reversed evidence without posting.
5. Shared canonical lifecycle and allocation authority; one Serializable commit of evidence application linkage, batch/members, balanced Bank/Fees/Clearing journal, balances, processed receipt, Settled state, actor/service lineage, and event outbox. External fetch occurs before the financial transaction.
6. Read durable posted result on same external identity; refund resolves actual posted Bank debit. Retain Manual boundaries, retired admin settlement, generic Clearing-transfer rejection, and old caller-authored COD/gateway route containment.

If Bosta cannot supply complete final receipt evidence, stop provider implementation and reassess another provider against the same evidence gate. Synthetic fixtures can validate application architecture but cannot upgrade any real provider's readiness.

## 10. Proposed adapter and canonical evidence model — design only

No types, tables, routes, or adapters described here were created.

### Adapter boundary

Propose a separate `ICarrierCodRemittanceEvidenceProvider`-equivalent contract, rather than overloading `IShippingCarrierAdapter.ParseWebhook` or assigning financial meaning to tracking:

- Accept a server-derived tenant/integration/account/environment context plus provider evidence locator; derive credentials internally from tenant-owned authority.
- Retrieve/verify only a frozen provider evidence contract; never expose raw HTTP/provider credentials to accounting or the browser.
- Return an authenticated **receipt observation**, not an instruction to settle. Missing/invalid/ambiguous/unsupported evidence is an explicit outcome.
- Normalize provider membership references and amounts; canonical authority independently re-matches SQL orders/shipments, confirms complete membership/finality/currency/fees, and controls financial execution.

### Canonical immutable facts and provenance

| Group | Proposed required facts |
|---|---|
| Permanent identity | Evidence ID; tenant; normalized provider; stable provider merchant/account ID; integration ID; environment/evidence origin; external remittance/settlement ID and batch/transfer references. Credential rotation must not reopen the same external identity. |
| Contract and trust | Official contract version/artifact digest; adapter/verifier version; authenticated request/source locator; transport status/verification method; signing key or credential-version identifier (never secret); provider business account binding; observation time. Verification is computed server-side, never caller-settable. |
| Economic receipt | Provider receipt/finality status and its defined meaning; gross G; actual fee F and categorized deduction breakdown; independently returned net N; explicit currency/precision; provider remitted/received timestamp plus normalized timezone; verified receiving bank/account reference. If required, authenticated bank confirmation reference. |
| Exact membership | Complete expected member count / pagination-complete marker; unique provider shipment/delivery/AWB/business references; member collected/remitted gross and currency; resolved tenant order+shipment IDs and existing collection journal IDs; no unmatched or silently dropped rows. |
| Integrity / retention | Raw-response digest and canonical economic fingerprint (stable serialization of account/environment/identity/finality/G/F/N/currency/date/destination/exact sorted membership); protected immutable evidence artifact reference with retention/redaction; no raw tokens/customer contacts in ordinary audit logs. A digest protects integrity, not source authenticity. |
| Application lineage | Evidence decision and reason; approving/initiating operator or service identity; canonical batch ID; immutable posted journal ID/result, actual receiving LedgerLines.AccountId; receipt/outbox/correlation linkage. |

Separate immutable economic observations from mutable delivery/processing leases and append-only decision history. Suggested decisions: PendingFinality, AwaitingCollection/PostedCollection, VerifiedEligible, RejectedAuthentication, IdentityConflict, Unsupported/Review, and AppliedPosted. “VerifiedEligible” never means merchant cash is posted. Only an atomically linked Posted result may report completed settlement.

### Identity, execution, and recovery rules

- Permanently deduplicate by tenant+provider+merchant account+environment+external remittance ID; economic identity excludes changing tokens/credential-version IDs. Scope environments so sandbox evidence cannot become live authority. Bind provider account to tenant; do not allow one external merchant account to mint the same receipt independently for multiple tenants without an explicit business allocation contract.
- Same identity/same canonical economic fingerprint returns the original evidence/result. Same identity/different date, destination, membership, amounts, currency, or finality is a conflict/quarantine, not an update to applied truth. Pending→completed provider observations must follow an explicit version/status contract; do not overwrite an immutable pending payload with a different hash and call it replay.
- Preserve permanent tenant/order allocation across Manual/historical/new carrier kinds. Namespace/mapping of canonical SettlementId must avoid provider/account collisions and fit current length limits; any necessary evidence persistence extension requires separate authorization and schema review.
- Financial transaction re-reads evidence and SQL in a fresh context, locks sorted orders and evidence application identity, verifies exact collection lines and unallocated/unrefunded amounts, maps active same-tenant Bank/Clearing/Expense accounts, and posts through existing financial authority. Do not introduce an interceptor bypass outside PaymentLifecycleService.
- Prefer the certified Manual atomic application pattern. Explicitly classify unique/serialization/deadlock/xmin races; rollback leaves no batch/member/journal/state/balance changes, and same-ID retry re-reads committed authority.
- Evidence fetch timeout/retry is not financial failure or success. Persist recoverable observation/processing states with bounded leases; application crash before commit leaves no financial effect; crash after commit/lost response returns original posted journal. Recovery consumes durable verified SQL/evidence linkage, never an event's freely authored G/F/N.
- Historical COD/Gateway/Manual source contracts retain their meaning and refund resolver behavior. A new carrier source must independently validate complete journal shape, not rely solely on net-cash-increase caps or reuse Manual inflow authority.

## 11. Deterministic test-only fixture and required acceptance criteria

### Production-inaccessible fixture proposal

Place a deterministic `FixtureCarrierCodRemittanceEvidenceProvider` **only in `PointOfSale.Tests`**, explicitly registered by test composition; no production assembly reference, Program registration, route, configuration toggle, default-provider fallback, or dynamic assembly discovery. Use a reserved code such as `test-only-cod-remittance`, never `bosta`, `aramex`, `mylerz`, `dhl`, or `fedex`.

Fixture origin remains `TestFixture` permanently. A test-only authority can exercise the same canonical verifier/matcher/lifecycle/posting pipeline against managed isolated PostgreSQL stores. Production eligibility must reject TestFixture/Sandbox origins even if payload claims live provider/tenant/signature. Environment names, an allowlist, or `verified=true` alone must not unlock it. Tests must prove absence from production DI/API and rejection of direct synthetic evidence injection into live authority. This fixture is **not a simulated Bosta contract**.

Determinism: fixed clock, fixed order/shipment/evidence IDs, frozen fixture contract, test-only signing key and HMAC bytes, explicit bad-auth variants, exact decimal money, scripted HTTP/timeouts/retries, and synchronization barriers for races. No internet, real carrier credentials, shared merchant database, or background timing assumption. Use separate contexts and fresh SQL/report reads; do not certify concurrency or balance effects with EF InMemory alone. Fixture evidence generation never bypasses real lifecycle/journal authority.

### Required test matrix for a later implementation

| Scenario | Required assertion / exact financial outcome |
|---|---|
| Valid whole-order evidence | Two collected orders100+50 EGP: exactly two collection journals Dr1130/Cr4020 totaling150; verified receipt G150/F5/N145 creates one batch, two members, one Posted settlement Dr1030=145/Dr6020=5/Cr1130=150. Fresh Bank145, Clearing0, fees expense5, sales credit150; Cashbox unchanged; exact two orders Collected→Settled; original collection journal links preserved. |
| Zero fees | G150/F0/N150: exactly two positive settlement lines DrBank150/CrClearing150, no zero-valued fee leg. |
| Invalid/missing auth/signature | Wrong key, tenant/account credential, altered bytes, missing header, bad provider account binding: reject before eligible evidence/application; no batch/member/journal/state/balance changes. Error/audit decision is not accepted receipt. |
| Duplicate external ID — same facts | Re-fetch/retry/reordered membership returns same evidence/batch/journal/result; original actor retained; no additional cash or fee effect. |
| Duplicate external ID — changed facts | Change one member, G/F/N, currency, receipt date, destination, account, or finality under same scoped identity: conflict/quarantine; immutable original remains, no money movement. Distinct provider/account IDs cannot collide through an unscoped SettlementId. |
| Replayed evidence | Same/different event IDs and equivalent payload encoding cannot create a second receipt application. Signed stale/replayed events follow frozen freshness/status rules; no second journal or fee. |
| Unknown or foreign-tenant order references | Include unknown, ambiguous tracking, same tracking in two tenants, a foreign real order/shipment, wrong provider/account, or mismatched response/request ID: reject **entire** batch; no dropped member or partial application. |
| Uncollected / Pending accounting / delivery-only | Delivered only, uncollected shipment/order, false SQL collection provenance, missing/incorrect/reversed/unposted Dr1130/Cr4020 journal: hold/reject, order not Settled and Bank unchanged. |
| Already settled | Order allocated to prior Manual/historical/carrier batch, including soft-deleted membership: new external identity rejected; original allocation/journal preserved. Same valid identity retry remains idempotent. |
| Gross/fees/net mismatch | G differs from exact members/order totals, N≠G−F, negative fee, F>G, unsupported deductions, precision overflow, incomplete pagination/count/duplicate member: no execution. No rounding tolerance invented for unsupported precision. |
| Currency mismatch | Evidence/order/shipment/member/active tenant currency mismatch or unsupported reference precision: reject; scalar balances unchanged. Changing display currency cannot override ledger authority. |
| Concurrent attempts | Separate contexts/barrier: same receipt identity, distinct IDs with overlapping orders, disjoint orders sharing accounts, and refund racing settlement. Exactly one winning allocation for overlap, no lost balance increments for disjoint success, balanced immutable journals, loser deterministic conflict/re-read; retry converges without duplicate fees. |
| Delayed/out-of-order events | Receipt arrives before shipment delivery/collection/collection journal; retain pending verified observation without Bank movement. Later independent valid prerequisites apply at most once. Scheduled/initiated status never posts; status regression/duplicate cannot undo final applied truth. |
| Provider retry and timeout | Script429/5xx/network timeout/401/credential rotation and multi-page interruption; finite safe read retries, no application of partial page set, no fabricated success. Same locator resumes without duplicate application. |
| Posting failure and recovery | Inject failure after evidence claim/batch creation, during real posting, after journal before event publication, and before commit. Fresh SQL shows full financial rollback, eligible orders still Collected, same-ID retry succeeds once. Crash/timeout after actual commit/lost response returns exact original Posted journal. Recover abandoned processing lease; no permanent duplicate collapse with missing result. |
| Zero net | G=F>0/N0 records Unsupported/Review decision only; no batch/member/journal/Settled or Bank change. Explicitly assert no `PostingLine.Debit(0)` success path. |
| Partial / split / refund residual | Partial member money, multiple destinations/receipts per order, pre-settlement refund: unsupported whole batch with no clearing reduction. No arbitrary refund-account selection or whole-order Settled flag. |
| Reversal / dispute | Pending/held/disputed/reversed provider observations cannot authorize new settlement. Reversal after posting links original evidence for review without editing original batch/members/journal or reopening identity/allocation. Separate approved reversal workflow required. |
| Refund destination lineage | After real receipt G150/F5/N145, refund25: Dr4020=25/Cr **actual posted Bank debit account**=25, Bank120, Clearing0, Cashbox unchanged. Same refund retry one row/journal. Preserve actual Cashbox refund for Manual and historical Bank destinations. Missing/ambiguous/reversed/foreign/inactive lineage fails closed. |
| Permissions / audit / fixture exclusion | Owner/Admin operator policies or explicit service authority; deny Manager/Cashier/Warehouse and forged caller actor/tenant/amount/verification fields; successful batch/member/journal/receipt linked to original actor/service/evidence. Production composition cannot resolve fixture; synthetic evidence cannot be promoted to live origin/provider. |

For **every** positive test: fresh journal Dr=Cr exactly, total posting amount agrees with canonical G, precise account deltas, exact membership/status transitions, same-tenant source/actor/journal/refund linkage, official Treasury/GL agreement, and no unrelated account movement. For **every** rejection: compare fresh batch/member/outbox/journal/line counts, order payment/financial state and all affected balances with the fixture baseline. Quarantined evidence/decision persistence is allowed; financial changes are not. Test discovery must be confirmed: the test project excludes most `Accounting/*.cs` except explicitly included suites (`PointOfSale.Tests.csproj:38–48`).

These are **required future tests, not delivered passing carrier-settlement tests**. Do not infer real-provider readiness from fixture success.

## 12. Remaining security/accounting risks and fresh verification

### Principal unresolved risks

1. No authenticated final merchant receipt identity/member/money contract for any provider. A hash/token/delivery flag is not that contract.
2. Provider-fed collection currently cannot be established through Bosta/Aramex COD branches; DHL/FedEx do not map COD money; Mylerz is blocked. Existing certified SQL collection admission does not certify every provider transport/parser.
3. Expected Shipment.CodAmount is not observed customer-collected money; present normalization cannot detect short collection. This matters before settling whole order totals.
4. Shared-secret/IP trust, no signed freshness window, shipping event-ID ambiguity, Processing-log crash gap, and Delivered exclusion from scheduled tracking cannot be assumed adequate for financial evidence/recovery.
5. Dormant carrier coordinator is not atomically Posted at its Settled commit; generic posting caps/SQL batch authority do not authenticate external funds. Preserve containment.
6. External identity lacks provider/account/environment scope and immutable economic fingerprint in the existing batch contract; private feed corrections/partial pagination could misallocate funds if ignored.
7. Scalar ledger currency and two-decimal settlement schema require strict tenant currency/precision; no implicit conversion.
8. Zero net, partial, split destination, disputed/reversed receipts, fee recovery, and residual refunds lack certified automatic lifecycles. Do not bypass shared membership/refund authority to accommodate them.
9. ID-based FKs/application guards require explicit tenant checks at all relationships; privileged SQL bypass protection is outside this audit's proof.
10. MAIN aggregate payout reconciliation health is not green. This audit does not repair or repurpose CommerceHub/vendor payouts as inbound carrier remittance evidence.

### Fresh tests executed in this audit

Backend command (existing compiled `ManualCodVerification` artifacts; no rebuild):

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c ManualCodVerification --no-build --no-restore --filter "FullyQualifiedName~T10SettlementContainmentTests|FullyQualifiedName~TreasurySettlementAuthorityCertificationTests|FullyQualifiedName~ShipmentCodCollectedConsumerTests|FullyQualifiedName~BostaEndpointSurfaceTests|FullyQualifiedName~MylerzOfficialContractCompatibilityTests" --logger "trx;LogFileName=carrier-cod-remittance-audit.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
```

**34 passed / 0 failed / 0 skipped**, 889ms reported test duration; TRX counters inspected. Breakdown: T10 containment8, Treasury source authority7, carrier collection consumer2, Bosta endpoint/status contract10, Mylerz fail-closed7. These are mocks/local source-contract tests, no positive financial operation/provider call. Source-reading architecture tests examine current working files. No fresh backend build or full-suite certification is claimed.

Frontend command, working directory `PointOfSale.Web`:

```powershell
npx vitest run src/pages/treasury/TreasuryManualCodSettlement.test.tsx src/pages/treasury/TreasuryGeneralLedger.test.tsx
```

**2 files / 11 tests passed / 0 failed**, 21.72s total. Existing Manual request/confirmation/retry/reload and General Ledger UI regressions pass; no backend settlement or browser financial POST was issued. Positive Manual backend/accounting/refund certification remains the prior report, supported here by unchanged source and retained boundaries; it was not rerun as a money-creating fixture during this audit.

### Fresh MAIN runtime observation

- Port5010 listener: PID15248, executable/command `C:\Users\fadya\source\repos\PointOfSale\PointOfSale.API\bin\Debug\net9.0\PointOfSale.API.exe`; confirmed with `Get-NetTCPConnection` / `Get-CimInstance`. No process was stopped/restarted.
- GET `http://localhost:5010/health`: status Unhealthy, trace/correlation `ec85f9a4-2767-42e4-be44-cd8e8edc37af`, UTC `2026-10-08T23:19:02.7579537Z`.
- `cod_reference_workflow_foundation`: Healthy **as disabled**; enabled=false, environment=Disabled, runtimeExecutionEnabled=false, evidenceAdmissionEnabled=false, lifecycleMutationEnabled=false, settlementExecutionEnabled=false.
- Accounting outbox Healthy, CriticalPendingCount=0, RepeatedFailureCount=0, CriticalLagCount=0; shipment-cod-collected and fulfillment-delivered consumers Ready.
- PostgreSQL reachable; reported database `PosEcommerceDb_Baseline`. `payout_external_truth_readiness` Unhealthy, latest reconciliation Critical / one mismatch. Health visibility is not a carrier receipt record or current financial balance certification.

### Carrier containment and Manual preservation proof

`PaymentLifecycleController.RecordCodSettlement:253–267` still returns409 `SETTLEMENT_EVIDENCE_REQUIRED`, with no call to `RecordOfflineSettlementAsync`; gateway sibling remains contained. T10 current source assertions and compiled controller/mock tests pass, including eight simultaneous requests plus replay. `AdminAccountingController` retains410 `SETTLEMENT_AUTHORITY_REQUIRED`; generic Treasury Clearing transfers remain restricted by retained source architecture checks. No live financial POST was needed.

Manual routes (`PaymentLifecycleController:210–239`), strict minimal JSON request (`348–349`), active tenant/actor/whole-order eligibility, fixed Main Cashbox authority, atomic commit, permanent allocation, actual-journal refund destination, and dedicated UI remain present. `ManualCodSettlementAuthority.ValidateOrdersAsync` requires Manual fulfillment/no carrier ownership (`60–62`); carrier cannot enter that executor.

Preservation fingerprints captured before report creation and rechecked at completion:

| Existing untracked Manual/prerequisite artifact | Git blob hash |
|---|---|
| `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.ManualCodSettlement.cs` | `7b72ac580f30224df1e0a0276554c4a4f08cb64e` |
| `PointOfSale.Infrastructure/Accounting/ManualCodSettlementAuthority.cs` | `3cebf62c2cabba0849793a0b138e2a31767bf601` |
| `PointOfSale.Infrastructure/Accounting/SettlementRefundLineage.cs` | `f2482c510b76022e4ed6ede3da26bf42194a2c52` |
| `PointOfSale.Core/Interfaces/Payments/ManualCodSettlement.cs` | `70f9c189cd579053236615e80b4dac44e7259c12` |
| `PointOfSale.Tests/Integration/ManualCodSettlementTests.cs` | `44649d4a3c765ad95b8f4d51555bdfe782085d29` |
| `PointOfSale.Infrastructure/Migrations/20261008223819_ManualCodPermanentSettlementIdentity.cs` | `b7b3469ef455a3ded367567f3a4dd5d4be5f2352` |
| `PointOfSale.Web/src/pages/treasury/TreasuryManualCodSettlement.jsx` | `45de756de165a067a16e4c9554983d2bb72049e3` |
| `PointOfSale.Web/src/services/manualCodSettlementApi.js` | `c530cead64685e56b1202ef79c9ffcece704a9a8` |

The same PowerShell pipeline `git diff --binary | git hash-object --stdin` for the complete existing tracked working diff is unchanged: `dc3db7ebe83c3f46c6d19ebcaec14dfb94730783`. This fingerprints the rendered diff, not a claim that every ignored/untracked file is hashed. Final status inspection and report-only patch confirm preservation of existing application/configuration/UI/test changes. No feature flag was changed.

`git diff --check` and `git diff --no-index --check -- /dev/null ".ai/reports/CARRIER_COD_REMITTANCE_FORENSIC_AUDIT.md"` completed without whitespace errors; Git emitted existing LF/CRLF normalization notices. The report is untracked, so the second check explicitly includes its new contents.

## 13. Exact files changed and stop decision

**Only repository artifact added by this audit:**

1. `.ai/reports/CARRIER_COD_REMITTANCE_FORENSIC_AUDIT.md` — this report.

No existing repository file was edited. Pre-existing modified/untracked Manual COD, prerequisites, Treasury UI/tests, reports, migrations, and local evidence remain user work. No application, schema, configuration, adapter, endpoint, UI, journal, settlement, or remittance record was created/enabled by this phase.

The test runner's local TRX is outside the repository at `C:\Users\fadya\AppData\Local\Temp\opencode\carrier-cod-remittance-audit.trx`; public-document tool output is harness-managed evidence, not a repository artifact. No additional audit code/probe/test provider was added.

**STOP:** retain certified Manual COD Settlement and disabled carrier remittance. The next decision must first establish a provider's authenticated, complete, final remittance contract and sanctioned test path, then separately authorize the bounded adapter/evidence/atomic accounting implementation and its acceptance tests. This report authorizes no implementation.

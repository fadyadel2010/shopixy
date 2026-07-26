# ARAMEX-2 — Request Contract Compatibility

Audit date: 2026-07-06  
Scope: outbound Aramex shipment-create request generation only

## Executive Summary

The outbound create-shipment request was audited against the official Aramex
Shipping Services API manual and the published `ShippingAPI.V2` service.
Tracking, cancellation, lifecycle, recovery, Checkout, Geographic design, POS
Admin, CommerceHub, Affiliate, and frontend runtime were not reviewed.

The pre-audit create path was not request-contract compatible. It could send
missing mandatory email/description values, invalid lengths, invalid country
and currency codes, non-positive or out-of-range weights and pieces, partial
dimensions, invalid service/product combinations, COD without a positive
amount, COD in a currency rejected by the published manual, and international
dutiable parcels without customs value. It also incorrectly changed freight
`PaymentType` to consignee-collect when COD service was selected, fabricated a
three-day `DueDate`, silently invented `"Package"` as goods description, and
serialized shipment dates as ISO text rather than the JSON/WCF date form used
by the official REST binding.

Minimal request-boundary hardening now validates the canonical input before
HTTP, adds only officially documented optional DTO members, normalizes
country/currency evidence, uses the documented product pairings, keeps
transportation payment prepaid (`P`) while representing COD only as `CODS`,
requires conditional customs/COD data, and omits optional values rather than
inventing them.

The hardened request generator is production-compatible with the reviewed
public contract for admitted requests. Production activation remains
conditional on tenant-account sandbox certification and GEO-10 mapping-data
readiness.

Official authorities:

- [Aramex Shipping Services API manual](https://www.aramex.com/docs/default-source/resourses/resourcesdata/shipping-services-api-manual.pdf)
- [Published ShippingAPI.V2 service](https://ws.aramex.net/ShippingAPI.V2/Shipping/Service_1_0.svc)
- [Aramex Developer Solutions Center](https://www.aramex.com/ag/en/developers-solution-center/aramex-apis)

## Official Contract Matrix

| Area | Official contract | Runtime verdict |
|---|---|---|
| Envelope | `ClientInfo`, optional `Transaction`, `Shipments`, optional `LabelInfo` | Compatible; no invented envelope fields |
| ClientInfo | all fields mandatory; username/password/account number/PIN max 50, entity exactly 3, country ISO2, version length 4, source integer | Validated before I/O |
| Shipment | `Shipper`, `Consignee`, `ShippingDateTime`, `Details` mandatory | Always emitted after admission |
| Address | `Line1` mandatory, 4–50; `Line2/3` optional max 50; city/postcode/state conditional; country mandatory ISO2 | Validated; international admission requires state and postcode evidence |
| Contact | person/company max 50; phone/cell max 30; email max 50; shipment contacts mandatory | Validated for shipper and consignee |
| Pieces | integer 1–100 | Validated |
| Weight | value greater than 0 and no more than 100, maximum three decimals; `KG` supported | Validated and serialized as decimal + `KG` |
| Dimensions | optional as a group; length/width/height/unit all present, positive, max 100; `CM` supported | Group validation and exact serialization added |
| Product | `DOM` + `OND`; international parcel `EXP` + `PPX`/`EPX` | Exact allowed mapping; unknown service rejected |
| Payment | `P`, `C`, or `3`; conditional payment options/parties follow payer | Runtime emits `P`; COD no longer corrupts freight payer |
| Services | documented comma-separated codes, max 25; `CODS` is COD | Only `CODS` emitted when COD is requested |
| Description | mandatory, max 100 | Required; unofficial `"Package"` fallback removed |
| Goods origin | mandatory ISO2 | Normalized and validated |
| Customs value | conditional for dutiable product | Required for admitted international parcel products |
| COD amount | conditional when COD service is present; official manual requires USD | Positive amount and USD required |
| Insurance | optional money | Added, positive value and ISO currency guarded |
| Label | report `9201`; type `URL` or `RPT` | `9201` + `URL`; compatible |
| References | shipment references optional, max 50 | Reference1/2 required locally and max 50 |
| Dates | shipment date mandatory; due date optional | WCF JSON date emitted; invented due date removed |

## Outbound DTO Matrix

| Runtime DTO | Serialized member | Status |
|---|---|---|
| `AramexCreateShipmentRequest` | `ClientInfo`, `LabelInfo`, `Shipments` | Exact documented names |
| `AramexClientInfo` | official eight authentication members | Exact documented names/types |
| `AramexShipment` | references, shipper, consignee, dates/comments, `Details` | Exact documented names |
| `AramexParty` | references, account, `PartyAddress`, `Contact` | Exact documented names |
| `AramexAddress` | `Line1/2/3`, `City`, `StateOrProvinceCode`, `PostCode`, `CountryCode` | Exact documented names |
| `AramexContact` | documented contact members | Exact names; only populated subset emitted |
| `AramexShipmentDetails` | dimensions, pieces, weight, products, payment, services, description, origin, customs/COD/insurance | Exact documented names |
| `AramexDimensions` | `Length`, `Width`, `Height`, `Unit` | Added from official contract |
| `AramexWeight` | `Value`, `Unit` | Compatible |
| `AramexMoney` | `Value`, `CurrencyCode` | Compatible |
| `AramexLabelInfo` | `ReportID`, `ReportType` | Compatible |

No `Building`, `Company`, `ContactName`, or generic `DeclaredValue` wire fields
were invented. Building/street evidence belongs in official address lines;
company/contact name belong in official `Contact`; canonical `DeclaredValue`
maps to official `CustomsValueAmount`.

## Mandatory Field Matrix

| Mandatory field | Admission |
|---|---|
| Client credentials/account/entity/country | non-empty, official lengths, ISO2 |
| Shipper and consignee | always created |
| Address line 1 | 4–50 characters |
| City | required by the canonical input and max 50 |
| Country | recognized ISO2 and normalized uppercase |
| Person/company | non-empty and max 50 |
| Phone/cell | non-empty, valid characters/digit evidence, max 30 |
| Email | syntactically valid and max 50 |
| Shipping date | generated at request admission |
| Details | always created |
| Pieces | 1–100 |
| Actual weight | positive, max 100, scale max 3 |
| Product group/type | allowlisted pairing |
| Payment type | fixed documented `P` for this shipper-paid integration |
| Description | required, max 100 |
| Goods origin | ISO2 |

## Conditional Field Matrix

| Condition | Enforcement |
|---|---|
| Any dimension supplied | all three dimensions emitted with `CM`; partial group rejected |
| International parcel (`PPX`/`EPX`) | positive customs/declared value required |
| International address | state/province and postcode evidence required for both parties |
| CODS requested | positive COD amount and USD required |
| CODS not requested | supplied COD amount rejected |
| Insurance supplied | positive amount with admitted ISO currency |
| Optional comment absent | omitted, not serialized as invented empty text |
| Due date absent | omitted; no unofficial three-day default |
| Third-party freight payment | unsupported and cannot be generated |
| Payment type `C` | unsupported and cannot be generated without its conditional payment option |
| Special services other than CODS | unsupported and cannot be generated |

## Serialization Matrix

| Concern | Runtime |
|---|---|
| Property casing | explicit `JsonPropertyName` freezes official names |
| Null optionals | omitted via `WhenWritingNull` |
| Boolean | standard JSON boolean; no create-shipment boolean member emitted |
| Decimal | JSON numeric decimal, culture independent |
| Currency | uppercase ISO 4217 alpha-3 |
| Country | uppercase ISO 3166-1 alpha-2 |
| Shipment date | `/Date(<Unix milliseconds>)/` WCF JSON form |
| Weight unit | `KG` |
| Dimension unit | `CM` |
| Label | integer `9201`, string `URL` |
| References | strings no longer than 50 |
| Services | exact `CODS`; absent otherwise |

## Breaking Issues

Found and closed:

1. no fail-closed request admission;
2. missing mandatory consignee email could be omitted;
3. missing description was replaced with an unofficial default;
4. COD incorrectly changed transportation `PaymentType` to `C`;
5. COD amount could be zero or absent;
6. COD used arbitrary tenant currency despite the published USD rule;
7. international dutiable parcel products omitted customs value;
8. dimensions/customs/insurance had no official DTO representation;
9. references, addresses, contacts, pieces, weights, countries, currencies,
   products, and service levels were not contract-bounded;
10. partial dimensions could not be represented safely;
11. due date was invented as current time plus three days;
12. JSON shipment date formatting did not match the REST/WCF binding.

## Runtime Hardening

Modified runtime:

- `PointOfSale.Infrastructure/Shipping/Adapters/AramexAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs`

Hardening is limited to request DTO capability, adapter-local validation,
serialization correction, and request admission. No controller, Checkout,
Geographic, Shipment lifecycle, tracking, cancellation, recovery, database,
migration, frontend, POS Admin, CommerceHub, or Affiliate runtime was changed.

Invalid requests return `ARAMEX_REQUEST_CONTRACT_INVALID` before construction
of provider HTTP traffic.

## Regression Tests

`PointOfSale.Tests/Infrastructure/AramexOfficialContractCompatibilityTests.cs`
freezes:

- official create endpoint;
- nested processed-shipment label behavior retained from ARAMEX-1;
- exact domestic and international product pairings;
- mandatory contact company;
- prepaid freight payment independent of COD;
- WCF JSON date and omitted unofficial due date;
- dimensions, customs, insurance, COD, money, and label serialization;
- missing email, invalid weight, partial dimensions, missing international
  customs value, non-USD COD, and unknown service all fail before network I/O;
- ARAMEX-1 cancellation/capability corrections remain frozen.

## Verification

| Command | Result |
|---|---|
| Infrastructure build | Passed; existing warnings only |
| Focused ARAMEX-2 tests | Passed: 10/10 |
| Solution build | Passed; existing warnings only |
| Focused carrier/Aramex/GEO-9/GEO-10 slice | Passed: 36/36 |
| Relevant Architecture Shipping + Geographic slice | Passed: 108/108 |
| Broad name-matched Shipping + Geographic slice | 130/133 passed; 3 unrelated pre-existing failures in webhook-trust and CommerceHub finance tests |

No live Aramex call was made. External mutation was neither necessary nor
appropriate for a contract audit.

The three broad-slice failures do not execute Aramex request generation: two
are `ShippingWebhookTrustPipelineTests` (webhook processing is explicitly out
of scope) and one is
`CommerceHubFinanceServiceTests.CreateRefundAsync_ShippingOnlyRefund_ShouldNotReverseCommission`
(fails in the journal-entry financial-authority guard). The focused and
architecture-relevant slices are green.

## Risks

1. Aramex's public manual is current on the portal but describes a legacy
   service and does not publish a structured version history.
2. Product availability, country-specific postcode/state validation, COD
   enablement, customs policy, special-service combinations, and monetary
   limits remain account/country dependent. The adapter intentionally rejects
   evidence it cannot prove.
3. Existing production builders do not yet supply international customs,
   state, and postcode evidence. Those requests now fail closed rather than
   emit an invalid shipment.
4. The published COD money rule requires USD. Accounts contracted for a
   different official COD currency need a vendor-issued account-specific
   contract before relaxing this guard.
5. GEO-10 still reports zero repository-proven published provider mapping
   coverage. Request compatibility does not remove that activation blocker.

## Final Recommendation

Accept the request-boundary hardening. The outbound serializer is compatible
for requests it admits, and invalid requests cannot reach Aramex.

Do not treat this as unconditional carrier activation approval. Proceed to
account-specific Aramex sandbox certification using vendor-issued current
WSDL/sample payloads, enabled product/service matrices, country rules, COD
currency terms, and successful/failing golden requests. Complete GEO-10
provider registration and mapping publication before production rollout.

## Explicit Success Answers

- Reviewed ONLY outbound request contract? **Yes.**
- Runtime files modified: **2** — `AramexAdapter.cs` and
  `IShippingCarrierAdapter.cs`.
- Backend files modified: **2 runtime files plus 1 backend test file**.
- Frontend files modified: **None**.
- Official request contract verified? **Yes, against the public official
  manual and published service; account-private rules remain a certification
  condition.**
- Mandatory fields complete? **Yes for admitted requests.**
- Conditional rules verified? **Yes for the implemented product, COD,
  dimensions, customs, insurance, address, and payment surfaces.**
- Serialization verified? **Yes.**
- Breaking issues found? **Yes — 12 request-generation/admission issues.**
- Runtime hardening required? **Yes, completed minimally.**
- Production compatible? **Yes for admitted request serialization;
  production activation remains conditional on account sandbox certification
  and GEO-10 mapping readiness.**
- Recommend next phase: **Aramex account-specific sandbox/WSDL golden-payload
  certification plus governed GEO-10 mapping-data rollout.**

No new cross-system architecture truth was discovered. The five architecture
`.ai-context` files were not updated.

# PCC-1 — Official Shipping Provider Documentation Audit

Audit date: 5 July 2026  
Certification type: documentation-first, official-provider-source research  
Status: **COMPLETE**

## Executive Summary

This report establishes the official documentation baseline for the four external shipping providers frozen by PCC-0: Bosta, Aramex, Mylerz, and DHL Express.

No runtime, source code, tests, requests, responses, DTOs, implementation webhooks, or provider compatibility were inspected or compared. Statements in this report describe only publicly accessible official provider documentation as of the audit date.

The documentation authorities have materially different maturity:

- **DHL Express MyDHL API** has a current numbered release, dated release notes, an OAS3 definition, explicit test and production endpoints, and broad shipping operations.
- **Bosta** publishes current interactive REST documentation for `/api/v2`, API-key guidance, shipment, pickup, label, return-order, and webhook material, but no formal version policy, changelog, deprecation schedule, retry contract, idempotency contract, or public quota specification was located.
- **Aramex** publishes official manuals, WSDL packages, and samples for its Shipping, Tracking, Rate Calculator, and Location Services. The public authority remains `service_1_0`-style SOAP/WCF contracts and does not expose a reliable dated release, deprecation, or migration register.
- **Mylerz** confirms integrations on its official corporate site, but no public official developer portal, API contract, version register, authentication specification, environment list, OpenAPI definition, or lifecycle documentation was located. Vendor-issued onboarding documentation is required.

Documentation risk is **MEDIUM** for DHL, **MEDIUM** for Bosta, and **HIGH** for Aramex and Mylerz. These are documentation-governance risk ratings, not runtime compatibility verdicts.

## Reviewed Scope

Included providers:

- Bosta
- Aramex
- Mylerz
- DHL Express

Excluded completely:

- Amazon
- Noon
- Stripe
- Paymob
- Fawry
- Twilio
- Manual Payment
- Vodafone Cash

Reviewed documentation dimensions:

- Official documentation and developer portals
- Official/current versions and version policy
- Authentication, headers, content types, and environment URLs
- OpenAPI, Swagger, WSDL, samples, plugins, and official SDK availability
- Shipment, tracking, cancellation, label, pickup, return, and proof-of-delivery documentation
- Webhooks, callbacks, retry, idempotency, rate limits, pagination, and status lists
- Release notes, breaking changes, migrations, deprecations, and changelogs

The audit deliberately does not compare any provider contract with the PointOfSale implementation.

## Official Provider Inventory

| Provider | Official Product | Public Technical Authority | Documentation Maturity |
|---|---|---|---|
| Bosta | Bosta Business / Custom APIs | Bosta Documentation and interactive API Reference | Current but lifecycle governance is incomplete |
| Aramex | Shipping Services, Shipment Tracking, Rate Calculator, Location Services | Aramex Developer Solutions Center, manuals, WSDL packages, samples | Public but legacy and weakly version-governed |
| Mylerz | Mylerz shipping integration | Corporate website only; technical contract not public | Insufficient for independent certification |
| DHL | DHL Express — MyDHL API | DHL API Developer Portal, OAS3 reference, reference-data guide, release notes | Strong and actively maintained |

## Documentation Sources

Only official provider-controlled sources were treated as authority.

### Bosta

- [Bosta Documentation](https://docs.bosta.co/)
- [Bosta API Reference](https://docs.bosta.co/api/)
- [Get Your API Key](https://docs.bosta.co/docs/how-to/get-your-api-key/)
- [Create Your First Delivery](https://docs.bosta.co/docs/how-to/create-your-first-delivery)
- [Create Your First Pickup](https://docs.bosta.co/docs/how-to/create-your-first-pickup)
- [Get Shipment Status via Webhook](https://docs.bosta.co/docs/how-to/get-delivery-status-via-webhook/)
- [Print Air Waybill](https://docs.bosta.co/docs/how-to/print-awbs/)
- [Plugins and SDKs](https://docs.bosta.co/docs/category/plugins-and-sdks/)

### Aramex

- [Aramex Developer Solutions Center](https://www.aramex.com/eg/en/developers-solution-center/aramex-apis)
- [Official Shipment Tracking API manual](https://www.aramex.com/content/uploads/109/232/42254/shipments-tracking-api-manual.pdf)
- [Official Shipping Services API manual](https://www.aramex.com/content/uploads/109/232/42194/shipping-services-api-manual.pdf)
- Official WSDL and sample-code downloads linked by the Developer Solutions Center

### Mylerz

- [Mylerz official website](https://www.mylerz.com/)

No publicly accessible official Mylerz technical authority was located.

### DHL

- [DHL Express — MyDHL API](https://developer.dhl.com/api-reference/dhl-express-mydhl-api)
- [DHL API Developer Portal](https://developer.dhl.com/)
- [DHL API Status](https://status.api.dhl.com/)

## Version Matrix

| Provider | Official API Version | Current Stable Version | Version Policy | Release Authority |
|---|---|---|---|---|
| Bosta | REST path `/api/v2`; delivery creation documentation also uses `apiVersion=1` | `/api/v2` with operation-level `apiVersion=1` where documented | No public formal compatibility or support-window policy located | Live documentation only; no structured changelog located |
| Aramex | Public SOAP/WCF contracts use `service_1_0.svc`-style services; portal groups Shipping, Tracking, Rate, and Location APIs | `1.0` service contracts are the latest publicly identifiable authority; no global semantic release is published | No public version-support or retirement policy located | Manuals and WSDL downloads; no dated release register located |
| Mylerz | Not publicly documented | Unknown | Not publicly documented | Vendor/onboarding authority required |
| DHL | MyDHL API | **3.3.1**, released 28 June 2026 | Numbered product releases with dated per-release notes; no separate public long-term-support schedule identified | Product page, downloadable OAS3, reference-data package, and release notes |

“Current stable” means the latest version identifiable from official public documentation. It does not imply implementation compatibility.

## Authentication Summary

| Provider | Authentication Method | Authentication Flow | Required Headers | Supported Content Types |
|---|---|---|---|---|
| Bosta | API key | Generate a scoped key in the Bosta dashboard, copy it once, send it directly in `Authorization` | `Authorization: <API_KEY>`; `Content-Type: application/json` for JSON requests | JSON; label operations return or deliver PDF artifacts as documented |
| Aramex | Account and user credentials embedded in SOAP request `ClientInfo` | Obtain Aramex account/user credentials; include account number, entity, country code, PIN, username, password, and version as applicable in each request | SOAP binding headers and action determined by the official WSDL | SOAP/XML; official services also expose WCF JSON bindings in some packages, but WSDL/XML remains the machine-readable public authority |
| Mylerz | Unknown | Not publicly documented | Unknown | Unknown |
| DHL | HTTP Basic authentication using issued MyDHL API ID credentials | Hold a valid DHL Express account, register in the developer portal, request access, receive credentials, then use Basic authentication | `Authorization: Basic <credentials>`; operation-specific accept/content headers in OAS3 | HTTPS REST/JSON for the reviewed MyDHL API product; labels and documents may be returned as encoded document content |

All four providers require a later security review. For Mylerz this is mandatory because no public authentication authority exists.

## Supported Operations

Legend:

- **Documented** — a public official operation or workflow is documented.
- **Indirect** — supported as part of another workflow, not as a distinct API.
- **Not located** — no public official operation was found.
- **Unknown** — technical documentation is not publicly available.

| Operation | Bosta | Aramex | Mylerz | DHL MyDHL API |
|---|---|---|---|---|
| Shipment creation | Documented: deliveries, including bulk creation | Documented: Shipping Services | Unknown | Documented: shipment creation |
| Tracking | Documented in API reference and status/webhook material | Documented: Shipment Tracking API | Unknown | Documented |
| Shipment cancellation | Documented in interactive API reference | No distinct public shipment-cancellation operation located | Unknown | Documented shipment deletion/cancellation subject to shipment state |
| Labels / AWB | Documented: mass AWB, A4/A6; PDF behavior documented | Documented through Shipping Services label output | Unknown | Documented as shipment documents/transport labels |
| Pickup | Documented: create business pickup; recurring options documented | Documented in Shipping Services/manual artifacts | Unknown | Documented: request, update, and cancel pickup |
| Returns | Documented as order types/workflows including CRP, exchange, and RTO; no separate general returns API family located | No distinct public return API located | Unknown | Supported through shipment/return-label capabilities; no separate universal “returns API” family identified |
| Proof of delivery | Webhook includes `isConfirmedDelivery`; no dedicated POD retrieval API located | No dedicated public POD retrieval API located | Unknown | Documented electronic proof-of-delivery endpoint |
| Rate quote | API reference includes pricing/term operations; no public global quota policy | Documented: Rate Calculator API | Unknown | Documented: one-piece and multi-piece rates and landed cost |
| Official status list | Documented Bosta state and exception-code lists | Tracking manual contains update/status definitions | Unknown | Tracking checkpoint/status definitions are supplied by the API/reference data; no single universal cross-DHL status list |

### Pagination Model

| Provider | Official Pagination Baseline |
|---|---|
| Bosta | Endpoint-specific pagination may appear in the interactive reference; no single public global pagination contract was located |
| Aramex | No general pagination model was located in the reviewed public SOAP authorities; shipment lists are operation-specific |
| Mylerz | Unknown |
| DHL | No global pagination model applies to the core shipment operations reviewed; any collection pagination is endpoint-specific in OAS3 |

### Webhooks, Callbacks, Retry, Idempotency, and Rate Limits

| Provider | Webhooks / Callbacks | Retry Documentation | Idempotency Documentation | Rate Limits |
|---|---|---|---|---|
| Bosta | Shipment-status webhook documented; HTTP `POST` on state changes; account-level or per-delivery URL; optional custom headers | No public retry schedule, delivery guarantee, or dead-letter policy located | No public idempotency-key or replay-window contract located | No public authoritative quota or standard rate-limit-header contract located |
| Aramex | No general public shipment webhook/callback product located; tracking is request/poll based | No formal public retry policy located | No formal public idempotency contract located | No public quota or standard rate-limit-header contract located |
| Mylerz | Unknown | Unknown | Unknown | Unknown |
| DHL | No universal MyDHL shipment webhook/callback contract was located; core MyDHL tracking is pull based | No single global retry policy located; behavior remains operation/error specific | No global idempotency-key contract identified | Test environment: 500 calls/day. Production limits are access/account specific in the reviewed product authority |

## Provider Findings

=================================================

### Provider: Bosta

**Official Product:** Bosta Business / Custom APIs  
**Official Documentation:** [Bosta Documentation](https://docs.bosta.co/) and [API Reference](https://docs.bosta.co/api/)  
**Official Developer Portal:** Bosta documentation and business dashboard  
**Official API Version:** `/api/v2`; delivery creation uses `?apiVersion=1` in current how-to material  
**Current Stable Version:** `/api/v2` with operation-level `apiVersion=1` where specified  
**Authentication:** Scoped API key sent directly in `Authorization`; JSON requests use `Content-Type: application/json`  
**Sandbox URL:** No public official sandbox base URL was located  
**Production URL:** Current official examples use `https://app.bosta.co/api/v2`  
**Official SDK:** No official language SDK located; official Shopify and WooCommerce integration guidance is published  
**OpenAPI:** Interactive API reference exists; no official downloadable OpenAPI/Swagger artifact was located  
**Supported Operations:** Deliveries, bulk deliveries, tracking/status, cancellation in the API reference, pickup creation, AWB generation, return-related order types, and status webhooks  
**Official Status List:** Published numeric state codes include pickup, route, warehouse, transit, delivered, returned, exception, cancellation, termination, loss, damage, investigation, awaiting action, archived, and on-hold states; separate exception/NDR codes are published  
**Webhook Documentation:** Yes; state-change `POST`, per-account or per-delivery URL, optional custom headers, numeric state payload  
**Callback Documentation:** No separate callback contract located  
**Retry / Idempotency / Rate Limits:** No authoritative public contracts located  
**Breaking Changes:** No formal breaking-change register exists. Documentation currently establishes `/api/v2`, requires `apiVersion=1` for the shown delivery flow, and exposes a broader numeric status vocabulary than older minimal integrations may assume. Because no dated changelog is published, the date and compatibility classification of status additions cannot be certified.  
**Deprecated APIs:** None publicly declared in a structured deprecation register  
**Migration Notes:** No official migration guide located; consumers moving from older undocumented contracts must use the current `/api/v2` reference and operation-specific query version  
**Known Limitations:** No public sandbox authority, formal version policy, changelog, deprecation schedule, retry guarantee, idempotency guarantee, rate-limit policy, downloadable OpenAPI, or official server SDK catalog  
**Risk Level:** **MEDIUM**

=================================================

### Provider: Aramex

**Official Product:** Aramex Shipping Services, Shipment Tracking, Rate Calculator, and Location Services APIs  
**Official Documentation:** [Aramex Developer Solutions Center](https://www.aramex.com/eg/en/developers-solution-center/aramex-apis), official manuals, WSDL packages, and samples  
**Official Developer Portal:** Aramex Developer Solutions Center  
**Official API Version:** Public contracts use `service_1_0.svc`-style SOAP/WCF services  
**Current Stable Version:** Publicly identifiable `1.0` service contracts; Aramex does not publish a global current semantic version  
**Authentication:** Credentials embedded in the request `ClientInfo`, including account number, account entity, account country code, account PIN, username, password, and version fields as applicable  
**Sandbox URL:** `https://ws.sbx.aramex.net/` service family; exact service paths are defined by each official WSDL  
**Production URL:** `https://ws.aramex.net/` service family; exact service paths are defined by each official WSDL  
**Official SDK:** No maintained official language SDK catalog located; official sample code is downloadable  
**OpenAPI:** No REST OpenAPI/Swagger authority located; WSDL/XSD packages are the official machine-readable contracts  
**Supported Operations:** Shipment creation and label generation, shipment tracking, rate calculation, location services, and pickup-related Shipping Services operations  
**Official Status List:** Tracking status/update definitions are documented in the official tracking manual; no separately versioned public canonical status registry was located  
**Webhook Documentation:** No general public shipment webhook product located  
**Callback Documentation:** No general public callback contract located  
**Retry / Idempotency / Rate Limits / Pagination:** No formal public contracts located  
**Breaking Changes:** No dated public breaking-change record was located. The enduring public authority remains a legacy `service_1_0` SOAP/WCF contract, so undocumented server-side evolution cannot be independently reconstructed from official release history.  
**Deprecated APIs:** No public structured deprecation list or retirement timetable located  
**Migration Notes:** No public migration guide located  
**Known Limitations:** Old manuals, no transparent product release timeline, credentials carried inside request bodies, no global version policy, no public webhook contract, no OpenAPI, and no formal retry/idempotency/quota documentation  
**Risk Level:** **HIGH**

=================================================

### Provider: Mylerz

**Official Product:** Mylerz shipping integration  
**Official Documentation:** [Mylerz official website](https://www.mylerz.com/)  
**Official Developer Portal:** No publicly accessible official developer portal located  
**Official API Version:** Unknown  
**Current Stable Version:** Unknown  
**Authentication:** Unknown  
**Sandbox URL:** Unknown  
**Production URL:** Unknown  
**Official SDK:** None publicly located  
**OpenAPI:** None publicly located  
**Supported Operations:** The official corporate site confirms integration capability in general, but no public technical operation contract can certify shipment, tracking, cancellation, label, pickup, return, or POD APIs  
**Official Status List:** Not publicly documented  
**Webhook / Callback Documentation:** Not publicly documented  
**Retry / Idempotency / Rate Limits / Pagination:** Not publicly documented  
**Breaking Changes:** Indeterminable without vendor-issued versioned documentation  
**Deprecated APIs:** Indeterminable  
**Migration Notes:** Obtain the current official onboarding/partner API package, its issue date, version, environment list, change notices, and vendor support statement before PCC-2  
**Known Limitations:** The entire technical authority appears restricted to customers, partners, or onboarding channels. Search results, source comments, third-party articles, and guessed hostnames are not accepted as official authority.  
**Risk Level:** **HIGH**

=================================================

### Provider: DHL

**Official Product:** DHL Express — MyDHL API  
**Official Documentation:** [DHL Express — MyDHL API](https://developer.dhl.com/api-reference/dhl-express-mydhl-api)  
**Official Developer Portal:** [DHL API Developer Portal](https://developer.dhl.com/)  
**Official API Version:** MyDHL API  
**Current Stable Version:** **3.3.1**, released 28 June 2026  
**Authentication:** HTTP Basic using MyDHL API ID credentials issued after portal access approval; a valid DHL Express account is required  
**Sandbox URL:** `https://express.api.dhl.com/mydhlapi/test`  
**Production URL:** `https://express.api.dhl.com/mydhlapi`  
**Official SDK:** No official maintained language SDK catalog located for MyDHL; official OAS3, reference-data guides, and downloadable artifacts are provided  
**OpenAPI:** **Yes — official downloadable OAS3 YAML**  
**Supported Operations:** Rating, products, landed cost, shipment creation, shipment documents/labels, shipment cancellation where eligible, add-piece, pickup create/update/cancel, tracking, address capability, identifiers, image upload/retrieval, return-label workflows, and electronic proof of delivery  
**Official Status List:** Product/reference-data authorities document tracking checkpoints and codes; DHL divisions do not share one universal status list  
**Webhook Documentation:** No universal MyDHL shipment webhook contract located  
**Callback Documentation:** No universal MyDHL callback contract located  
**Retry / Idempotency:** No global public client idempotency-key or retry-window contract identified for the reviewed product  
**Rate Limits:** Test environment limited to 500 calls/day; production entitlement is account/access specific  
**Breaking Changes:** Current release 3.3.1 adds `SRV` and `MF` manufacturer product ID reference types. Release 3.3.0 changed shipment manifest mapping, tightened export-line description validation to at least one character, changed pickup cutoff validation, expanded customer-reference behavior, and added label-free fallback behavior. Earlier official release notes also record replacement/removal changes, including removal of registration-number codes in 3.1.2, replacement of `PPY` by `PCH`, replacement of legacy `GMTCutoffTime` by `pickupCutoffSameDayOutboundProcessing`, and the historic removal of the shipment request `function` field.  
**Deprecated APIs:** No current whole-product retirement notice was located. Release notes do identify removed/replaced fields and enum values that act as deprecation/migration signals.  
**Migration Notes:** Consumers below 3.3.1 should review every intervening release note and refresh against the current OAS3/reference data. Particular attention is required for stricter validation, removed/replaced codes, pickup cutoff behavior, and response-field replacements.  
**Known Limitations:** Access requires an approved DHL Express account; production entitlements and limits are account specific; operations and statuses are specific to DHL Express/MyDHL and must not be generalized to other DHL divisions  
**Risk Level:** **MEDIUM**

=================================================

## Breaking Changes

This section records only changes discoverable from official documentation. It does not assess whether the application is affected.

| Provider | Documentation Change Baseline |
|---|---|
| Bosta | Current baseline is `/api/v2`, with `apiVersion=1` on documented delivery creation. The published webhook/status vocabulary includes codes through 105 and optional operational fields. No changelog exists to date or classify additions. |
| Aramex | No public change register permits reliable breaking-change discovery. The official public contract remains `service_1_0`-style WSDL/manual authority. |
| Mylerz | Indeterminable without the official partner/onboarding documentation package. |
| DHL | 3.3.1 adds new reference type codes. 3.3.0 tightens validations and pickup-cutoff handling. Prior official notes record removed/replaced fields and codes, including 3.1.2 registration-number removals, `PPY` → `PCH`, `GMTCutoffTime` replacement, and removal of the shipment request `function` field. |

No official documentation reviewed announced a new universal authentication method, new universal webhook protocol, or whole-product retirement for these four products.

## Deprecated APIs

| Provider | Official Deprecation Baseline |
|---|---|
| Bosta | No structured deprecation notices or retirement schedule located |
| Aramex | No structured deprecation notices or retirement schedule located |
| Mylerz | Unknown |
| DHL | No current whole-MyDHL retirement notice located; per-release removed/replaced fields and enum values are documented |

Absence of a public notice is not evidence that a private/account-specific contract has no deprecations.

## Migration Notes

### Bosta

No official migration guide exists. A migration baseline must use the live `/api/v2` reference, retain the documented `apiVersion=1` query requirement where applicable, accept the current status vocabulary, and validate operation-specific documentation at migration time.

### Aramex

No official migration guide or newer global version target was located. The currently published WSDLs and manuals are the migration authority. Any vendor-issued private replacement package must be obtained directly from Aramex and version-frozen before use.

### Mylerz

Migration cannot be assessed. Obtain:

- Current official API package and version
- Issue/revision date
- Sandbox and production environments
- Authentication and credential-rotation rules
- OpenAPI/Swagger or equivalent schema
- Status list
- Webhook/callback contract
- Retry, idempotency, quota, and pagination policies
- Release notes, breaking changes, and deprecation notices

### DHL

For movement to 3.3.1, review every official release between the source version and 3.3.1, update to the current OAS3 and reference-data package, and evaluate all validation tightening, removed/replaced codes, pickup cutoff changes, and field replacements. DHL's release notes, rather than an independent migration guide, are the authoritative migration record.

## OpenAPI Availability

| Provider | Official OpenAPI / Swagger | Other Machine-Readable Authority |
|---|---|---|
| Bosta | No downloadable official artifact located | Interactive API reference |
| Aramex | No | Official WSDL/XSD packages |
| Mylerz | Not publicly available | None publicly available |
| DHL | **Yes — OAS3 YAML** | Reference-data PDF/ZIP and product downloads |

## Risk Assessment

Risk measures the quality, currency, completeness, and lifecycle governance of official documentation. It does not measure implementation compatibility.

| Provider | Risk | Rationale |
|---|---|---|
| Bosta | **MEDIUM** | Current usable docs and workflows exist, but no formal version policy, changelog, deprecation schedule, public sandbox authority, retry/idempotency guarantees, or quota contract was located |
| Aramex | **HIGH** | Official public artifacts exist but are legacy, SOAP/WSDL based, and lack a transparent release, deprecation, migration, retry, idempotency, and quota record |
| Mylerz | **HIGH** | No public technical authority exists; version, environments, authentication, schemas, operations, and lifecycle are unverified |
| DHL | **MEDIUM** | Strong versioned OAS3 and release notes substantially reduce uncertainty, but account-specific access/limits and documented validation/removal changes require disciplined version review |

## Compatibility Matrix

| Provider | Documentation Available | Current Official Version | Breaking Changes | Deprecated APIs | Migration Needed | Official OpenAPI | Security Review Needed | Compatibility Review Needed |
|---|---|---|---|---|---|---|---|---|
| Bosta | Yes | `/api/v2`; operation-level `apiVersion=1` | Change history unavailable; current status/version baseline identified | None publicly listed | Unknown until PCC-2 baseline comparison | No downloadable artifact located | Yes | Yes |
| Aramex | Partial but official | `service_1_0`-style contracts | Not transparently published | None publicly listed | Unknown; current WSDL freeze required | No; WSDL/XSD available | Yes | Yes |
| Mylerz | No public technical docs | Unknown | Indeterminable | Indeterminable | **Official vendor package required** | No public artifact | Yes | Yes |
| DHL | Yes | **3.3.1** | Yes; additions, tightened validation, and prior removed/replaced fields/codes | Per-field/code replacements; no whole-product retirement found | Conditional on source version; release-by-release review required | **Yes, OAS3** | Yes | Yes |

## Final Verdict

**PCC-1 documentation baseline: ESTABLISHED WITH DOCUMENTED AUTHORITY GAPS**

Bosta, Aramex, Mylerz, and DHL Express are now frozen as the only providers in this certification. DHL has a complete enough public authority for a precise PCC-2 surface audit. Bosta has a usable official surface but weak public lifecycle governance. Aramex requires WSDL/manual-based comparison with explicit uncertainty around release history. Mylerz cannot receive a public-documentation compatibility verdict until the current vendor-issued technical package is obtained.

No application compatibility conclusion is made here. No request, response, DTO, webhook implementation, runtime, source, or test comparison was performed.

The audit discovered provider-documentation facts, not new PointOfSale runtime architecture truth. Therefore the five `.ai-context` architecture files were intentionally left unchanged.

Next Recommended Certification:

PCC-2 — Shipping Endpoint & API Surface Compatibility Audit

Status:

READY

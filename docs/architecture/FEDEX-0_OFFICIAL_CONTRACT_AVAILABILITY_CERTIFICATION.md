# FEDEX-0 - Official Contract Availability Certification

Date: 2026-07-07
Scope: FedEx official developer contract availability only
Status: COMPLETE

## Executive Summary

FedEx provides an official Developer Portal and official API documentation
sufficient to begin engineering certification.

This certification reviewed only official FedEx developer resources. It did not
review PointOfSale runtime code, adapter code, Geography, Customs, Shipment
Origin, carrier mappings, warehouse evidence, checkout behavior, request
serialization, response admission, or operational rollout.

Final verdict: **READY WITH MINOR LIMITATIONS**.

The FedEx official contract is broad and current enough for FEDEX-1 through
FEDEX.FINAL planning. The main limitations are documentation-delivery
limitations, not contract absence: no public official OpenAPI/Swagger artifact
or official SDK package was found during this pass, and some downloadable JSON
API collections / sandbox examples are exposed through the Developer Portal
experience rather than as stable repository artifacts.

## Official Developer Resources

Official sources reviewed:

- FedEx Developer Portal: https://developer.fedex.com/api/en-us/home.html
- FedEx API Catalog: https://developer.fedex.com/api/en-us/catalog.html
- API Authorization: https://developer.fedex.com/api/en-us/catalog/authorization/v1/docs.html
- Ship API: https://developer.fedex.com/api/en-us/catalog/ship.html
- Ship API Documentation: https://developer.fedex.com/api/en-us/catalog/ship/v1/docs.html
- Basic Integrated Visibility / Track: https://developer.fedex.com/api/en-us/catalog/track.html
- Track Documentation: https://developer.fedex.com/api/en-us/catalog/track/v1/docs.html
- Rates and Transit Times API: https://developer.fedex.com/api/en-us/catalog/rate.html
- Rates Documentation: https://developer.fedex.com/api/en-us/catalog/rate/v1/docs.html
- Pickup Request API: https://developer.fedex.com/api/en-us/catalog/pickup.html
- Pickup Documentation: https://developer.fedex.com/api/en-us/catalog/pickup/v1/docs.html
- Service Availability API: https://developer.fedex.com/api/en-us/catalog/service-availability.html
- Service Availability Documentation: https://developer.fedex.com/api/en-us/catalog/service-availability/v1/docs.html
- Address Validation API: https://developer.fedex.com/api/en-us/catalog/address-validation.html
- Address Validation Documentation: https://developer.fedex.com/api/en-us/catalog/address-validation/v1/docs.html
- Advanced Integrated Visibility Webhook: https://developer.fedex.com/api/en-us/catalog/shipment-visibility-webhook.html
- Quotas and Rate Limits Guide: https://developer.fedex.com/api/en-us/guides/ratelimits.html
- Sandbox Virtualization Guide: https://developer.fedex.com/api/en-us/guides/sandboxvirtualization.html
- Integration Best Practices: https://developer.fedex.com/api/en-us/guides/best-practices.html
- FedEx Terms of Use: https://www.fedex.com/en-us/terms-of-use.html

## Authentication Documentation

Available.

FedEx documents OAuth 2.0 bearer-token authentication through the API
Authorization documentation. The official contract identifies Client ID/API Key,
Client Secret/Secret Key, token generation through `POST /oauth/token`, token
type, one-hour expiration, and additional child credential modes for internal,
compatible, proprietary parent-child, and integrator customers.

Engineering readiness: sufficient for FEDEX-1 authentication/runtime
compatibility analysis.

## Shipping Documentation

Available.

FedEx documents Ship API capability for domestic, international, and return
shipments. The documentation covers create shipment, validate shipment, async
shipment retrieval, cancel shipment, create tag, cancel tag, required shipment
inputs, service type, packaging type, shipper/recipient information, payer
information, package weight, label specification, customs and international
shipping guidance, and change logs.

Engineering readiness: sufficient for FEDEX-1 and FEDEX-2 request-contract
compatibility work.

## Tracking Documentation

Available.

FedEx documents Basic Integrated Visibility, formerly Track API. It covers
tracking by tracking number, door tag, FedEx Office order number, reference,
Transportation Control Number, proof-of-delivery behaviors, notification
options, business rules, and usage limit evidence.

Engineering readiness: sufficient for FEDEX-1 tracking contract analysis and
later operational status-mapping certification.

## Label Documentation

Available through Ship API.

FedEx documents label creation as part of Ship API and states that shipment
creation returns tracking number and label information. It documents label
specification inputs and label output behavior, including PDF and PNG label
handling and important label printing constraints.

Engineering readiness: sufficient for FEDEX-1 label contract discovery and
FEDEX-2 request/response compatibility planning.

## Pickup Documentation

Available.

FedEx documents the Pickup Request API for pickup availability, create pickup,
and cancel pickup. The documentation includes required pickup inputs, FedEx
Express and Ground business rules, cancellation limitations, availability
inputs, confirmation numbers, carrier requirements, scheduled date, location,
and pickup timing constraints.

Engineering readiness: sufficient to treat pickup as an officially supported
future capability, subject to separate runtime implementation and account
entitlement certification.

## Sandbox Support

Available.

FedEx documents separate test and production API URIs:

- Test: `https://apis-sandbox.fedex.com/`
- Production: `https://apis.fedex.com/`

FedEx also documents sandbox virtualization, including predefined virtualized
responses, JSON API collections, negative test cases, and virtualized coverage
for Rate, Basic Integrated Visibility, Address Validation, Postal Code
Validation, Pickup Request, Service Availability, Global Trade, Ground End of
Day Close, Open Ship, Ship, and FedEx Location Search.

Engineering readiness: sufficient for FEDEX-5 operational-readiness planning,
with the caveat that virtualized sandbox responses are not proof of live account
entitlement or production behavior.

## Versioning

Available.

FedEx API product pages expose version and last-updated metadata. The Ship API,
Track/Basic Integrated Visibility, Rate, Pickup, Service Availability, Address
Validation, and Advanced Integrated Visibility pages include version markers,
last-updated values, and change logs. Ship API and Rates documentation include
2025 and 2026 change-log entries.

Engineering readiness: sufficient to begin version pinning and change-log
review in FEDEX-1.

## SDK Availability

No official SDK package was confirmed in this pass.

FedEx publishes API documentation, samples, interactive request/response areas,
and JSON API collections. No official FedEx-maintained SDK package or official
FedEx GitHub repository was found from official FedEx sources during this
availability certification.

Engineering impact: minor limitation. SDK absence does not block certification
because the official HTTP contract is available, but FEDEX-1 should avoid
assuming an SDK authority.

## OpenAPI Availability

No public official OpenAPI or Swagger artifact was confirmed in this pass.

FedEx documentation exposes structured API pages, request/response samples, JSON
API collection sections, and interactive portal testing surfaces, but the
reviewed official pages did not publish a stable downloadable OpenAPI/Swagger
specification comparable to a direct `api.yaml` contract.

Engineering impact: minor limitation. FEDEX-1 and FEDEX-2 can proceed from
official documentation and JSON collections, but request/response schema
certification may require portal export artifacts or manual field-level
freezing from official docs.

## Developer Portal

Available.

The FedEx Developer Portal supports sign-up/login, organizations, shipping
accounts, API projects, webhook projects, API catalog exploration, developer
guides, sandbox testing, production movement, and certification flow.

Engineering readiness: sufficient.

## Documentation Quality Assessment

Contract quality classification: **Good**.

Reasons:

- Official Developer Portal exists.
- Official authentication documentation exists.
- Official API documentation exists for authorization, ship, tracking, rates,
  pickup, service availability, address validation, sandbox, rate limits,
  webhooks/eventing, and best practices.
- Official sandbox and production URI guidance exists.
- Official rate-limit and quota rules exist.
- Official error model guidance exists through HTTP status codes, error payloads,
  warnings, notes, and coding guidance to depend on error codes rather than
  dynamic messages.
- Official change logs and version metadata exist on API product pages.
- Official Terms of Use exists.
- JSON API collection sections exist for key APIs.

Not classified as Enterprise Grade in this narrow certification because:

- No public official OpenAPI/Swagger artifact was confirmed.
- No official SDK package was confirmed.
- Some portal collection/testing artifacts appear tied to the Developer Portal
  experience and may require account/login context.
- Advanced Integrated Visibility webhooks are subscription-only and currently
  limited to U.S.-based customers/accounts.

## Engineering Readiness

The official FedEx documentation is sufficient to begin:

| Future phase | Readiness |
|---|---|
| FEDEX-1 Official Contract & Runtime Compatibility | Ready |
| FEDEX-2 Request Contract Compatibility | Ready |
| FEDEX-3 Geographic Compatibility | Ready to audit against official FedEx address/service requirements |
| FEDEX-4 Runtime Authority | Ready after FEDEX-1/2 field-level contract freeze |
| FEDEX-5 Operational Readiness | Ready to plan; credentialed sandbox/live proof remains future work |
| FEDEX.FINAL Development Certification Freeze | Ready as a future phase after focused compatibility, runtime, and regression gates |

## Risks

| Risk | Classification | Impact |
|---|---|---|
| No public OpenAPI/Swagger artifact confirmed | Minor limitation | Field-level schema freeze may require portal collections/manual doc freezing |
| No official SDK confirmed | Minor limitation | Runtime should implement direct HTTP contract rather than SDK assumptions |
| Virtualized sandbox responses are predefined | Operational limitation | Sandbox pass cannot prove live entitlement or production behavior |
| Webhooks are premium/subscription and U.S.-limited | Capability limitation | Webhook support must be separately certified and may not be globally available |
| FedEx API change logs continue to evolve | Normal provider-contract risk | FEDEX phases must pin reviewed dates/versions and fail closed on unknown enums/statuses |
| Terms and service conditions govern use | Legal/operational dependency | Production activation must respect FedEx account agreements and service guide constraints |

## Final Recommendation

Proceed to **FEDEX-1 - Official Contract & Runtime Compatibility**.

FEDEX-1 should freeze the exact official FedEx endpoints, base URLs, auth flow,
HTTP methods, request/response fields, supported capabilities, unsupported
capabilities, error model, retry/idempotency posture, and version/change-log
baseline before any runtime implementation or modification.

## Final Verdict

**READY WITH MINOR LIMITATIONS**

FedEx has an official developer portal, official API documentation, official
authentication guidance, official sandbox/production guidance, official rate
limit guidance, official error guidance, official API change logs, official
Terms of Use, JSON API collection support, and official documentation for the
requested core shipping capabilities.

The minor limitations are the absence of a confirmed public official
OpenAPI/Swagger artifact and the absence of a confirmed official SDK. Those do
not block engineering certification, but they require FEDEX-1 and FEDEX-2 to
freeze request/response contracts directly from official FedEx documentation and
portal-provided JSON collections.

## Explicit Success Answers

- Reviewed ONLY FedEx documentation? **Yes.**
- Official developer portal available? **Yes.**
- Official API documentation available? **Yes.**
- OpenAPI / Swagger available? **Not confirmed from official public FedEx sources.**
- Sandbox available? **Yes.**
- SDKs available? **Not confirmed from official FedEx sources.**
- Documentation quality? **Good.**
- Engineering certification ready? **Yes, with minor limitations.**
- Recommend next phase: **FEDEX-1 - Official Contract & Runtime Compatibility.**

## AI Context Update Decision

No `.ai-context` files were updated. This certification discovered
provider-specific FedEx documentation availability findings only and did not
discover new permanent cross-provider architectural truth.

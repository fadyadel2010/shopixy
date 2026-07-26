\# PCC-1 — Official Provider Documentation Authority Audit



Audit date: 5 July 2026  

Scope: official vendor documentation only. No code, runtime, or test comparison was performed.



Important: “Risk” below measures documentation maturity, API churn, and migration exposure. It does not assert whether the current application is compatible; that determination belongs to PCC-2 through PCC-8.



\---



\## Shipping



\### Bosta



\*\*Official docs:\*\* \[Bosta Developer Documentation](https://docs.bosta.co/)  

\*\*Official API/current stable version:\*\* REST API path `/api/v2`; delivery creation additionally uses `apiVersion=1`. Bosta does not publish a separate semantic release number.



\*\*Authentication and headers\*\*



\- API key in `Authorization`.

\- `Content-Type: application/json`.

\- Keys support read-only, read/write, and full-access scopes.

\- Bosta recommends least privilege. \[API key documentation](https://docs.bosta.co/docs/how-to/get-your-api-key/)



\*\*Rate limits:\*\* No public authoritative quota or rate-limit response-header specification located.



\*\*Webhooks/callbacks\*\*



\- Status webhook is an HTTP `POST`.

\- Triggered on status change, not shipment creation.

\- Configured account-wide through the dashboard or per delivery using `webhookUrl`.

\- Optional custom authentication header.

\- Payload includes numeric `state`, shipment type, timestamp, attempts, exception codes, COD, and proof-of-delivery indicators.

\- Current documented states include codes 10–105. \[Webhook and status documentation](https://docs.bosta.co/docs/how-to/get-delivery-status-via-webhook/)



\*\*Retry and idempotency:\*\* No public retry schedule, delivery guarantee, event identifier, or API idempotency contract located.



\*\*Changelog/deprecations/migrations:\*\* No authoritative structured API changelog, deprecation schedule, or migration guide located.



\*\*Official SDK/OpenAPI:\*\* Interactive API reference is provided, but no public official SDK or downloadable OpenAPI/Swagger authority was identified.



\*\*Breaking-change baseline\*\*



\- Current authority uses `/api/v2`.

\- Webhook state is numeric and its enum has expanded through newer operational states such as investigation, archived, and on-hold.

\- Consumers must tolerate optional webhook fields and additional status codes.

\- No published version policy or compatibility guarantee exists.



\*\*Risk: MEDIUM-HIGH\*\* — Current documentation is available, but lifecycle, retry, rate-limit, idempotency, and schema-evolution policies are missing.



\---



\### Aramex



\*\*Official docs:\*\* \[Aramex Developer Solutions Center](https://www.aramex.com/be/en/developers-solution-center/aramex-apis)  

\*\*Current stable API:\*\* SOAP services, commonly exposed as `service\_1\_0.svc`; Aramex does not publish a newer global stable API version. \[Official Shipping Services manual](https://www.aramex.com/content/uploads/109/232/42194/shipping-services-api-manual.pdf)



\*\*Authentication\*\*



Credentials are carried in the SOAP request client information:



\- Account number

\- Account entity

\- Account country code

\- Account PIN

\- Username

\- Password/version fields as applicable



\*\*Required protocol/headers\*\*



\- HTTPS SOAP/XML.

\- SOAP content type and SOAP action depend on the generated WSDL binding.

\- The official center provides WSDL downloads for shipping, tracking, rates, and locations.



\*\*Rate limits:\*\* No public quota or rate-limit headers documented.



\*\*Webhooks/callbacks:\*\* No general shipment webhook or callback API is documented in the public official center. Tracking is request/poll based.



\*\*Retry/idempotency:\*\* No formal retry or idempotency contract located.



\*\*Changelog/deprecations/migrations:\*\* No dated public changelog, deprecation table, breaking-change register, or migration guide located.



\*\*SDK/OpenAPI\*\*



\- Official sample code is downloadable.

\- WSDL/XSD definitions are the machine-readable authority.

\- No official REST OpenAPI/Swagger authority identified.



\*\*Breaking-change baseline\*\*



\- SOAP `service\_1\_0` remains the published contract.

\- The documentation portal does not expose a reliable release timeline.

\- Authentication and payloads are embedded in XML rather than standardized authorization headers.

\- Certificate/TLS assumptions in older manuals require separate security validation in PCC-6.



\*\*Risk: HIGH\*\* — The public contract is old, SOAP-based, and lacks transparent lifecycle and deprecation governance.



\---



\### Mylerz



\*\*Official public authority:\*\* \[Mylerz official site](https://www.mylerz.com/)



No public official developer portal, API reference, API version, authentication specification, headers, rate limits, webhook contract, callback contract, retry policy, idempotency policy, changelog, deprecation notice, migration guide, SDK, OpenAPI definition, or release notes were located.



The official site states that integrations are supported, but it does not publish the integration contract. API documentation therefore appears to be account-, onboarding-, or partner-restricted.



\*\*Breaking-change baseline:\*\* Indeterminable from public official documentation.



\*\*Risk: HIGH\*\* — Compatibility cannot be governed against a publicly versioned authority. The onboarding documentation supplied directly by Mylerz must become the controlling PCC artifact.



\---



\### DHL



\*\*Official docs:\*\* \[DHL API Developer Portal](https://developer.dhl.com/)  

DHL is an API portfolio, not one API; versions and limits are product-specific.



\*\*Current stable examples\*\*



\- Shipment Tracking Unified: current endpoint `https://api-eu.dhl.com/track/shipments`; subscription-key contract. \[Tracking API](https://developer.dhl.com/tracking)

\- Location Finder Unified: `/location-finder/v1`.

\- DHL Freight booking, price quote, labels and timetable: `/v1` endpoint families.

\- MyDHL Express SOAP Developer Guide reached version 2.41 in March 2026, with 2.40 and 2.34 previously published. \[MyDHL SOAP v2.41](https://developer.dhl.com/sites/default/files/2026-03/DHL%20EXPRESS%20-%20MyDHL%20API%20-%20SOAP%20Developer%20Guide%20-%20v2.41.pdf)



\*\*Authentication\*\*



Varies by product:



\- Unified APIs commonly require `DHL-API-Key`.

\- Freight APIs use API key/secret to obtain a 30-minute OAuth 2.0 bearer token.

\- Some Supply Chain APIs use HTTP Basic authentication.



\*\*Required headers:\*\* Product-specific; usually `DHL-API-Key` or `Authorization: Bearer …`, plus documented JSON/XML content negotiation.



\*\*Rate limits\*\*



\- Product-specific.

\- Tracking initial tier: 250 calls/day with a low-frequency throttle; production upgrades are requested through the portal.

\- Freight APIs commonly document 250 calls/day.

\- Exceeding quotas returns HTTP 429.

\- DHL explicitly directs consumers to each API’s rate-limit section. \[Rate-limit guidance](https://developer.dhl.com/getting-started/get-a-higher-rate-limit)



\*\*Webhooks/callbacks:\*\* Available only for particular products, including Unified Tracking Push. No universal DHL webhook format exists.



\*\*Retry/idempotency:\*\* No global policy. Several products recommend exponential backoff for 429 responses; idempotency must be assessed per API operation.



\*\*Changelog/OpenAPI/releases\*\*



\- Individual API pages contain downloadable OpenAPI YAML, Postman collections, manuals, release versions, notifications, and dated notes.

\- DHL confirms that most APIs provide OpenAPI specifications. \[Integration testing guidance](https://developer.dhl.com/getting-started/test-your-integration)



\*\*Important breaking notice\*\*



Shipment Tracking Unified decommissioned the `post\_de` backend on 25 February 2026 and now routes to `svb` or `parcel-de`. DHL warns that changed data-retention behavior may cause 404 responses for shipments previously available. \[Official Tracking notice](https://developer.dhl.com/tracking)



\*\*Risk: MEDIUM-HIGH\*\* — Strong documentation exists, but compatibility depends on identifying the exact DHL division and API product. The 2026 tracking backend transition is runtime-audit relevant.



\---



\## Payments



\### Stripe



\*\*Official docs:\*\* \[Stripe APIs](https://docs.stripe.com/apis)  

\*\*Current documented stable version:\*\* `2026-02-25.clover`. Stripe pins account and webhook behavior to an API version. \[Versioning](https://docs.stripe.com/api/versioning)



\*\*Version policy\*\*



\- Monthly releases inside a named release are backward-compatible.

\- Approximately twice yearly, a new named release begins with breaking changes.

\- Clover began with breaking version `2025-09-30.clover`; later Clover releases are additive. \[Clover migration record](https://docs.stripe.com/changelog/clover)



\*\*Authentication and headers\*\*



\- Secret/restricted API key using HTTP Basic authentication or `Authorization: Bearer`.

\- `Stripe-Version` pins request behavior.

\- `Idempotency-Key` is supported for mutations.

\- Connect additionally uses account/context headers where applicable. \[API keys](https://docs.stripe.com/keys)



\*\*Rate limits\*\*



\- Live global default: 100 operations/second.

\- Sandbox: 25 operations/second.

\- Default endpoint limit: 25 operations/second, with product-specific overrides.

\- HTTP 429 plus `Stripe-Rate-Limited-Reason`. \[Rate limits](https://docs.stripe.com/rate-limits)



\*\*Webhooks/callbacks\*\*



\- Signed using each endpoint’s webhook secret.

\- Webhook API version is independently configurable.

\- Live delivery retries continue for up to three days with exponential backoff; sandbox retries three times over several hours. \[Webhook delivery](https://docs.stripe.com/webhooks)

\- Webhook migration requires matching SDK/API versions where statically typed event models are used. \[Webhook versioning](https://docs.stripe.com/webhooks/versioning)



\*\*Idempotency\*\*



\- API v1: `POST`, normally retained at least 24 hours.

\- API v2: `POST` and `DELETE`, scoped replay window up to 30 days.

\- Reusing a key with different parameters produces an idempotency error. \[API v2 behavior](https://docs.stripe.com/api-v2-overview)



\*\*Latest major breaking baseline\*\*



`2025-09-30.clover` includes changes such as:



\- Flexible billing mode becoming the default for new subscriptions.

\- Removed or restructured Checkout and discount fields.

\- Changed decline/error codes.

\- Added or changed enum values and Connect verification behavior.



\*\*SDK/OpenAPI\*\*



\- Official SDKs for .NET, Java, Node, PHP, Python, Ruby, Go, iOS, Android, and React Native.

\- Official unified v1/v2 OpenAPI JSON/YAML is published in Stripe’s repository. \[SDK inventory](https://docs.stripe.com/sdks), \[OpenAPI announcement](https://docs.stripe.com/changelog/clover/2026-01-28/openapi-with-v2-apis)



\*\*Risk: MEDIUM\*\* — Excellent governance and version pinning, but explicit major-version and webhook-version checks are mandatory.



\---



\### Paymob



\*\*Official docs:\*\* \[Paymob Documentation](https://developers.paymob.com/)  

\*\*Current stable API:\*\* Current documentation centers on Payment Intention APIs and unified/embedded checkout. No global semantic API version is published.



\*\*Authentication\*\*



Depending on product:



\- API secret key/public key/integration identifiers for current checkout flows.

\- Some resources use a generated bearer auth token, documented as valid for one hour.

\- HMAC-SHA512 is used to authenticate callbacks.



\*\*Required headers\*\*



\- `Authorization: Bearer <token>` for bearer-protected resources.

\- `Content-Type: application/json`.

\- Product-specific keys or credentials where documented.



\*\*Rate limits:\*\* No public global rate-limit policy or standard response-header contract located.



\*\*Webhooks/callbacks\*\*



\- Transaction Processed Callback: backend source of truth.

\- Transaction Response Callback: customer redirect/UX.

\- Callbacks occur for successful or declined transactions.

\- HMAC is supplied as a query parameter and computed from a precisely ordered field sequence.

\- Card-token callbacks have a separate field sequence. \[Callback overview](https://developers.paymob.com/paymob-docs/developers/webhook-callbacks-and-hmac), \[HMAC specification](https://developers.paymob.com/paymob-docs/developers/webhook-callbacks-and-hmac/hmac/hmac-for-card-tokens)



\*\*Retry/idempotency:\*\* No public general retry schedule or idempotency-key contract located.



\*\*Changelog/deprecations/migrations:\*\* No structured public API changelog, deprecation timetable, or version migration register located.



\*\*SDK/OpenAPI\*\*



\- Official mobile SDK documentation is provided.

\- The current portal presents an interactive API reference.

\- No authoritative downloadable OpenAPI or server-side SDK release catalog was identified.



\*\*Breaking-change baseline\*\*



The documented preferred flow now starts with Payment Intention creation and a client secret, followed by hosted, embedded, or mobile checkout. Legacy multi-step authentication/order/payment-key flows therefore require explicit PCC-2 comparison.



\*\*Risk: MEDIUM-HIGH\*\* — Current documentation is materially newer, but lifecycle, rate-limit, retry, and idempotency governance remain incomplete.



\---



\### Fawry



\*\*Official docs:\*\* \[FawryPay Developer Guide](https://developer.fawrystaging.com/docs-home)  

\*\*Current stable API:\*\* Core server APIs are mostly unversioned by URL; payment status and server notifications explicitly provide V2 contracts.



\*\*Authentication\*\*



\- Merchant code plus a per-operation SHA-256 signature generated from an exact ordered concatenation of request fields and the merchant secure key.

\- Pay-by-link APIs additionally expose login/refresh-token flows.

\- Authentication is operation-specific rather than one universal authorization header.



\*\*Required headers\*\*



\- Usually `Content-Type: application/json`.

\- Merchant identity and signature are normally request fields.

\- Exact signature field ordering and decimal formatting are contract-critical.



\*\*Rate limits/retry/idempotency:\*\* No public authoritative limits, retry schedule, idempotency key, or duplicate-request guarantee located.



\*\*Webhooks/callbacks\*\*



\- Server Notification V2 sends HTTP `POST` whenever transaction status changes.

\- Callback URL is configured during merchant onboarding, not per order.

\- Statuses include `NEW`, `PAID`, `CANCELED`, `REFUNDED`, `EXPIRED`, `PARTIAL\_REFUNDED`, and `FAILED`.

\- Callback authenticity uses `messageSignature` with SHA-256 over ordered values plus the secure key. \[Server Notification V2](https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/server-notification-v2)

\- Status V2 supports pull-based reconciliation. \[Payment Status V2](https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/get-payment-status-v2)



\*\*Changelog/deprecations/migrations\*\*



\- A migration guide exists for legacy checkout/plugins.

\- No comprehensive dated API changelog, deprecation timetable, or release-note stream was located.



\*\*SDK/OpenAPI\*\*



\- Official Android, iOS, Flutter, and React Native repositories are linked from the portal.

\- An official Postman collection is linked.

\- No public OpenAPI/Swagger authority was identified.



\*\*Breaking-change baseline\*\*



\- Notification V2 and Status V2 are the current explicit versioned contracts.

\- Field names, ordered signature inputs, decimal formatting, and status spelling are compatibility-sensitive.

\- Legacy checkout/plugin migration is documented.



\*\*Risk: MEDIUM-HIGH\*\* — Good operation-level documentation, but weak global versioning and lifecycle governance.



\---



\## Commerce



\### Amazon SP-API



\*\*Official docs:\*\* \[Amazon SP-API Documentation](https://developer-docs.amazon.com/sp-api/docs/welcome)  

\*\*Current stable API:\*\* No single global version. Each API family has its own dated version, such as Catalog Items `2022-04-01`, Data Kiosk `2023-11-15`, App Integrations `2024-04-01`, and Notifications `v1`.



\*\*Authentication and required headers\*\*



\- Login with Amazon OAuth 2.0 authorization.

\- Refresh token exchanged for a one-hour LWA access token.

\- Restricted operations require a Restricted Data Token.

\- Requests include `x-amz-access-token`, `host`, and `user-agent`; content headers apply where a body is present. \[Connection authority](https://developer-docs.amazon.com/sp-api/docs/connecting-to-the-selling-partner-api), \[Public authorization](https://developer-docs.amazon.com/sp-api/docs/authorize-public-applications)



\*\*Rate limits\*\*



\- Operation-specific token-bucket limits.

\- Standard and dynamic usage plans.

\- `x-amzn-RateLimit-Limit` may be returned but is not guaranteed.

\- HTTP 429 is retryable and requires backoff. \[Usage plans](https://developer-docs.amazon.com/sp-api/docs/usage-plans-and-rate-limits)



\*\*Notifications\*\*



\- Notifications API v1 supports SQS and Amazon EventBridge destinations.

\- Notification payload versions are independently selectable.

\- Amazon recommends a backup retrieval mechanism because delivery can be delayed or interrupted. \[Notifications API](https://developer-docs.amazon.com/sp-api/docs/notifications-api)



\*\*Retry/idempotency\*\*



\- 429 and transient server errors require backoff.

\- No universal application-level idempotency-key contract exists; behavior is operation-specific.



\*\*Changelog/deprecations/migrations\*\*



Amazon publishes all three:



\- \[Announcements/changelog](https://developer-docs.amazon.com/sp-api/changelog)

\- \[Deprecation schedule](https://developer-docs.amazon.com/sp-api/docs/sp-api-deprecations)

\- Per-API release notes and migration guides.



\*\*Current high-impact removals/deprecations\*\*



\- Catalog Items v0 operations have been removed.

\- Legacy XML/flat-file listing feed types were removed in 2025; migration target is Listings Items API or `JSON\_LISTINGS\_FEED`.

\- `listCatalogCategories` v0 reached removal in March 2026.

\- Legacy Data Kiosk datasets were removed in February 2026.

\- Finances v0 operations are scheduled for removal on 28 August 2026.

\- Certain settlement report types are scheduled for removal on 11 November 2026.



\*\*SDK/OpenAPI\*\*



\- Official Swagger/OpenAPI models, JSON schemas, samples, and generated SDK guidance are published through Amazon’s official GitHub resources linked from the documentation.



\*\*Risk: HIGH\*\* — Strong documentation, but extensive per-family versioning, frequent removals, dynamic quotas, payload versions, and active 2026 migrations create significant audit exposure.



\---



\### Noon



\*\*Official public authority:\*\* \[Noon Seller Help Center](https://helpcenter.noon.partners/)  

\*\*API baseline:\*\* Noon confirms a production API platform for catalogs, orders, fulfillment, inventory, returns, and event notifications. Detailed FBPI API documentation is supplied during staging/onboarding rather than comprehensively exposed in the public help center. \[FBPI FAQ](https://helpcenter.noon.partners/en/category/fulfilled-by-partner-fbp/fulfilled-by-partner-integration-faqs)



\*\*Version/authentication/headers/rate limits:\*\* Not authoritatively specified in the accessible public documentation.



\*\*Webhooks/callbacks\*\*



\- FBPI supports direct API integration and a webhook option.

\- Warehouses must be configured as integration warehouses.

\- Noon shares orders with the integrator, expects acknowledgement and shipment creation, and supports status, inventory, return, and RTO flows.

\- Direct webhook setup requires coordination with the account manager. \[FBPI integration guide](https://helpcenter.noon.partners/en/category/fulfilled-by-partner-fbp/fulfilled-by-partner-integration-a-comprehensive-guide)



\*\*Retry/idempotency/changelog/deprecations/migrations:\*\* No public official contracts located.



\*\*SDK/OpenAPI:\*\* No publicly accessible official SDK, OpenAPI, Swagger, or release catalog identified.



\*\*Breaking-change baseline:\*\* Cannot be completed without the version of the Noon API package issued during onboarding.



\*\*Risk: HIGH\*\* — The governing technical authority is account-restricted, so the local copy and its issue date must be obtained before subsequent PCC phases.



\---



\## Messaging



\### Twilio



\*\*Official docs:\*\* \[Twilio Documentation](https://www.twilio.com/docs)  

\*\*Current stable API:\*\* Product-specific versions. Programmable Messaging and core Accounts resources continue to use the stable `2010-04-01` URI version; newer products use their own `v1`, `v2`, or dated contracts.



\*\*Authentication and headers\*\*



\- HTTP Basic authentication.

\- Recommended production credentials are API key SID/secret; account SID/auth token remains supported.

\- Form-encoded request bodies are common for core messaging APIs. \[API request documentation](https://www.twilio.com/docs/usage/requests-to-twilio)



\*\*Rate limits:\*\* Product-, account-, sender-, destination-, and regulatory-route-specific. Twilio does not publish one global REST quota.



\*\*Webhooks/callbacks\*\*



\- Incoming message webhooks support HTTP `POST` or `GET`.

\- Outbound delivery uses status callbacks.

\- Incoming messaging webhooks ordinarily require TwiML; informational callbacks require HTTP 200. \[Messaging webhooks](https://www.twilio.com/docs/usage/webhooks/messaging-webhooks)

\- Requests are signed with `X-Twilio-Signature` using the exact URL, parameters, and account auth token.

\- Twilio warns that new webhook parameters may be added without advance notice. \[Webhook security](https://www.twilio.com/docs/usage/webhooks/webhooks-security)



\*\*Retry and idempotency\*\*



Connection overrides allow:



\- Retry count 0–5; default 1.

\- Retry policies for 4xx, 5xx, connect timeout, and read timeout.

\- `I-Twilio-Idempotency-Token` distinguishes retry attempts.

\- Voice callbacks retain a hard 15-second total limit. \[Webhook retry controls](https://www.twilio.com/docs/usage/webhooks/webhooks-connection-overrides)



REST mutation idempotency is resource-specific; no universal client `Idempotency-Key` contract exists.



\*\*Changelog/deprecations/migrations:\*\* Twilio publishes a global changelog and product migration/deprecation notices, but changes must be evaluated by product.



\*\*SDK/OpenAPI\*\*



\- Official server-side and mobile helper libraries.

\- Official OpenAPI definitions are maintained in \[twilio/twilio-oai](https://github.com/twilio/twilio-oai).



\*\*Breaking-change baseline\*\*



\- Core messaging URI version remains stable.

\- Webhook schemas are intentionally additive.

\- Delivery status names and parameters vary by channel.

\- Security validation must use the exact externally visible URL and tolerate unknown parameters.

\- Callback retry behavior is configurable and can create duplicates.



\*\*Risk: MEDIUM\*\* — Stable core version and mature documentation, but channel-specific statuses, configurable retries, and additive webhook fields require careful compatibility handling.



\---



\# Final Compatibility Matrix



| Provider | Compatibility risk | Public docs current | Breaking changes identified | Migration required by baseline | Security review | Runtime audit |

|---|---:|---:|---:|---:|---:|---:|

| Bosta | Medium-High | Yes | Expanded webhook/status contract; no lifecycle record | Unknown | Yes | Yes |

| Aramex | High | Partially | No transparent change history | Unknown | Yes | Yes |

| Mylerz | High | No public API authority | Indeterminable | Authority package required | Yes | Yes |

| DHL | Medium-High | Yes, per product | 2026 tracking backend decommission and retention behavior | Possibly | Yes | Yes |

| Stripe | Medium | Yes | Clover major release changes; v1/v2 differences | Only if upgrading pinned version | Yes | Yes |

| Paymob | Medium-High | Yes | Current flow centers on Payment Intentions | Likely for legacy integrations | Yes | Yes |

| Fawry | Medium-High | Yes | Notification/Status V2 and legacy checkout migration | Possibly | Yes | Yes |

| Amazon SP-API | High | Yes | Multiple removed APIs, feeds, datasets, and 2026 removals | Likely if legacy resources exist | Yes | Yes |

| Noon | High | Public overview only | Indeterminable without onboarding package | Authority package required | Yes | Yes |

| Twilio | Medium | Yes | Product-specific/additive webhook changes | Usually product-specific | Yes | Yes |



\## Baseline conclusions



1\. \*\*Highest lifecycle risk:\*\* Amazon SP-API, Aramex, Mylerz, and Noon.

2\. \*\*Known dated breaking event:\*\* DHL Unified Tracking’s February 2026 backend transition.

3\. \*\*Best version governance:\*\* Stripe and Amazon, although Amazon has much greater migration surface.

4\. \*\*Missing public authority packages:\*\* Mylerz and Noon.

5\. \*\*Missing formal rate-limit/retry/idempotency policies:\*\* Bosta, Aramex, Mylerz, Paymob, Fawry, and Noon.

6\. \*\*Webhook security contracts requiring PCC-4/PCC-6 attention:\*\* Bosta custom headers, Stripe signatures, Paymob HMAC-SHA512, Fawry SHA-256 message signatures, and Twilio `X-Twilio-Signature`.

7\. No implementation compatibility conclusion is made in PCC-1; this report establishes the official documentation baseline only.


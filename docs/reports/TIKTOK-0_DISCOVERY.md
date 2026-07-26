# TIKTOK-0 Discovery

Status: complete  
Verification date: 2026-07-22  
Primary sources:
- TikTok Events API web event endpoint and parameters: https://ads.tiktok.com/gateway/docs/index?doc_id=1727541103358977&language=ENGLISH
- TikTok Events API setup and access token generation: https://ads.tiktok.com/gateway/docs/index?doc_id=1739584855420929&language=ENGLISH
- TikTok Events API test events: https://ads.tiktok.com/gateway/docs/index?doc_id=1724255493685249&language=ENGLISH
- TikTok Events API 2.0 navigation, authentication, responses/errors, supported events: https://ads.tiktok.com/gateway/docs/index?doc_id=1751443956638721&language=ENGLISH
- TikTok matching-data guidance: https://ads.tiktok.com/help/article/how-to-set-up-matching-events-with-events-api

## Authentication

TikTok Events API web reporting uses server-to-server HTTP POST calls to TikTok Business API. Authentication is performed with an `Access-Token` request header. The request content type is `application/json`.

Access tokens can be generated from TikTok Events Manager for a Pixel, or by using the Business API authorization flow with the Measurement permission needed to report pixel events.

## Credentials

Required tenant credentials:
- `PixelId`: TikTok Pixel Code / Event Source ID.
- `AccessToken`: TikTok Events API access token.

Shopixy settings:
- `Enabled`
- `PixelId`
- `AccessToken`
- `TestMode`
- `Endpoint`
- `Timeout`

Security handling:
- `AccessToken` is encrypted by the existing analytics settings service.
- The access token is never serialized into provider payload JSON.
- The access token is applied at send time by `TikTokHttpRequestCustomizer`.

## Event API

Implemented endpoint:

```text
POST https://business-api.tiktok.com/open_api/v1.3/event/track/
```

Implemented payload family:

```json
{
  "event_source": "web",
  "event_source_id": "PIXEL_CODE",
  "data": [
    {
      "event": "Purchase",
      "event_time": 1784723696,
      "event_id": "event-dedupe-id",
      "user": {},
      "page": {},
      "properties": {}
    }
  ]
}
```

## Supported Events

The TikTok provider maps the canonical storefront events required for Phase B:
- `PageView`
- `ViewContent`
- `Search`
- `AddToCart`
- `InitiateCheckout`
- `Purchase`

TikTok documentation also lists broader standard web events. Unsupported canonical events are skipped by the dispatcher without blocking analytics ingestion.

## Required Fields

For Shopixy TikTok delivery:
- `event_source`: fixed to `web`.
- `event_source_id`: tenant TikTok Pixel ID.
- `data[].event`: TikTok event name.
- `data[].event_time`: UTC Unix seconds from `AnalyticsEvent.OccurredAtUtc`.
- `data[].event_id`: canonical `AnalyticsEvent.EventId`.
- `data[].page.url`: canonical `AnalyticsContext.PageUrl` when present.
- `Access-Token`: applied as HTTP header by the request customizer.
- `Content-Type`: `application/json`.

For value-bearing events:
- `properties.currency`: required from canonical analytics context.
- `properties.value`: canonical or pixel-override-resolved value.

## Optional Fields

Mapped when available:
- `data[].user.external_id`: SHA-256 hash of authenticated customer ID or guest session ID.
- `data[].user.ip`: server-captured/anonymized IP address.
- `data[].user.user_agent`: server-captured user agent.
- `data[].page.referrer`: canonical referrer.
- `properties.contents[]`: product/order-item content metadata.
- `properties.content_type`
- `properties.description`
- `properties.query`
- `properties.order_id`

## Deduplication

TikTok supports event deduplication through `event_id`. Shopixy uses canonical `AnalyticsEvent.EventId` as `data[].event_id` and uses the existing provider delivery `DeliveryKey`:

```text
TikTok:{tenantId}:{eventId}:{requestType}
```

This gives both provider-side deduplication and local outbox duplicate protection.

## Retry Semantics

TikTok does not require provider-specific retry infrastructure in Shopixy. Retries use the existing `ProviderDeliveryRetryEngine`:
- Retryable: network errors, timeout/no status, `408`, `409`, `425`, `429`, and `5xx`.
- Terminal credential failures: `401`, `403`.
- Terminal payload failures: other `4xx`.
- `Retry-After` is honored by the generic sender when the provider returns it.
- Exponential backoff and jitter are provider-neutral.

## Error Codes

Shopixy classifies TikTok responses through generic HTTP classes:
- `2xx`: delivered.
- `401`/`403`: `credential_rejected`.
- `429`: `rate_limited`.
- `408`/`5xx`: `provider_transient`.
- Other `4xx`: `payload_rejected`.

TikTok Business API documentation exposes response/error-code references under Events API 2.0 and the Business API appendix. The integration intentionally does not branch on TikTok-specific error codes in runtime infrastructure.

## Rate Limits

TikTok documentation directs developers to their current developer app QPS limit in the Business API app console. TikTok FAQ guidance recommends event batches under 100 events and under 10 MB.

Shopixy sends one canonical analytics event per provider delivery outbox message, so TikTok payloads stay well below the documented batch recommendation.

## Security

- No TikTok script or pixel SDK is added to the storefront.
- TikTok receives only server-approved canonical analytics data.
- `AccessToken` is encrypted at rest and only materialized at send time.
- Advanced matching external ID is SHA-256 hashed.
- Provider delivery logs mask secrets through existing delivery masking.
- Payloads do not include access tokens.

## Testing

TikTok supports Events Manager test-event validation. For Shopixy Phase B, tests are local contract and runtime infrastructure tests:
- Golden payload contract tests for six required events.
- Customizer test for endpoint, headers, access token header, and content type.
- Existing retry, replay, dead-letter, circuit-breaker, metrics, and tracing tests prove delivery behavior remains provider-neutral.

## Payload Examples

Page view:

```json
{
  "event_source": "web",
  "event_source_id": "PIXEL_CODE",
  "data": [
    {
      "event": "PageView",
      "event_time": 1784723696,
      "event_id": "11111111-1111-1111-1111-111111111111",
      "page": {
        "url": "https://store.example.com/products/red-shirt"
      },
      "properties": {
        "content_type": "page",
        "description": "red-shirt"
      }
    }
  ]
}
```
```

Purchase:

```json
{
  "event_source": "web",
  "event_source_id": "PIXEL_CODE",
  "data": [
    {
      "event": "Purchase",
      "event_time": 1784723696,
      "event_id": "11111111-1111-1111-1111-111111111111",
      "user": {
        "external_id": "sha256-canonical-actor-id",
        "ip": "203.0.113.0",
        "user_agent": "UnitTest/1.0"
      },
      "page": {
        "url": "https://store.example.com/thank-you",
        "referrer": "https://store.example.com"
      },
      "properties": {
        "contents": [
          {
            "content_id": "SKU-RED",
            "content_name": "Red Shirt",
            "content_type": "product",
            "quantity": 2,
            "price": 10.00
          }
        ],
        "content_type": "product",
        "currency": "USD",
        "value": 20.00,
        "order_id": "33333333-3333-3333-3333-333333333333"
      }
    }
  ]
}

# TIKTOK-0.5 Runtime Mapping

Status: complete

## Canonical To TikTok

| Canonical `AnalyticsEventType` | TikTok event | Mapping status |
| --- | --- | --- |
| `PageView` | `PageView` | implemented |
| `ProductView` | `ViewContent` | implemented |
| `SearchPerformed` | `Search` | implemented |
| `AddToCart` | `AddToCart` | implemented |
| `BeginCheckout` | `InitiateCheckout` | implemented |
| `PurchaseCompleted` | `Purchase` | implemented |

Unsupported canonical event types return `null` from the TikTok mapper and are skipped by the dispatcher.

## Root Payload

| TikTok field | Source |
| --- | --- |
| `event_source` | constant `web` |
| `event_source_id` | `TenantAnalyticsSettings.TikTok.PixelId` |
| `data[]` | one TikTok event per canonical analytics event |

## Event Fields

| TikTok field | Source |
| --- | --- |
| `event` | provider event name from mapping table |
| `event_time` | `AnalyticsEvent.OccurredAtUtc` as Unix seconds |
| `event_id` | `AnalyticsEvent.EventId` |
| `page.url` | `AnalyticsContext.PageUrl` |
| `page.referrer` | `AnalyticsContext.Referrer` |
| `user.external_id` | SHA-256 of `Actor.CustomerId` or `Actor.SessionId` |
| `user.ip` | `AnalyticsContext.IpAddress` |
| `user.user_agent` | `AnalyticsContext.UserAgent` |

## Properties Mapping

| Canonical event | TikTok properties |
| --- | --- |
| `PageView` | `content_type=page`, `description` from URL path |
| `ProductView` | `contents[]`, `content_type=product`, `description`, `currency`, `value` |
| `SearchPerformed` | `query`, `content_type=product` |
| `AddToCart` | `contents[]`, `content_type=product`, `description`, `currency`, `value` |
| `BeginCheckout` | `currency`, `value` |
| `PurchaseCompleted` | `contents[]`, `content_type=product`, `currency`, resolved `value`, `order_id` |

Purchase line items are resolved from SQL `OrderItems` when `Payload.OrderId` is present. Purchase `value` uses the existing `IPixelValueResolver` with `PixelProvider.TikTok`.

## Delivery Mapping

| Delivery field | TikTok value |
| --- | --- |
| `Provider` | `AnalyticsProvider.TikTok` |
| `RequestType` | TikTok event name |
| `DeliveryKey` | `TikTok:{tenantId}:{eventId}:{requestType}` |
| `EndpointUrl` | `TenantAnalyticsSettings.TikTok.Endpoint` or default TikTok Events API endpoint |
| `HttpMethod` | `POST` |
| `ContentType` | `application/json` |
| `TimeoutSeconds` | `TenantAnalyticsSettings.TikTok.Timeout`, clamped to 1-120 |
| Headers | added by `TikTokHttpRequestCustomizer` at send time |

## Non-Goals

- No Tracking Runtime changes.
- No TikTok-specific sender logic.
- No TikTok-specific retry, replay, dead-letter, circuit-breaker, metrics, or tracing logic.
- No storefront TikTok script.

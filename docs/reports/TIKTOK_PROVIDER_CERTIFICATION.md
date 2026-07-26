# TikTok Provider Certification

Status: PASS  
Certification date: 2026-07-22

## Architecture

PASS. TikTok is implemented as:
- `AnalyticsProvider.TikTok`
- `TenantAnalyticsSettings.TikTok`
- `TikTokAnalyticsDispatcher`
- `TikTokHttpRequestCustomizer`

The dispatcher maps canonical `AnalyticsEvent` records to TikTok request payloads and enqueues `ProviderDeliveryRequest` objects. It does not send HTTP.

## Payload

PASS. Golden payload contracts exist for:
- `PageView`
- `ViewContent`
- `Search`
- `AddToCart`
- `InitiateCheckout`
- `Purchase`

Payloads use snake_case TikTok field names and do not include the access token.

## Runtime

PASS. Runtime delivery uses:
- Existing generic provider outbox.
- Existing generic HTTP sender.
- Existing provider request customizer extension point.

No Tracking Runtime modifications were made.

## Retry

PASS. TikTok uses `ProviderDeliveryRetryEngine` unchanged for retry classification, retry-after handling, exponential backoff, and terminal failure classification.

## Replay

PASS. TikTok dead-letter replay uses `ProviderDeliveryOutboxService.Replay*` unchanged. Replay is provider-neutral and keyed by `AnalyticsProvider.TikTok` where a provider filter is supplied.

## Security

PASS. TikTok access tokens are encrypted in tenant analytics settings and are resolved only by `TikTokHttpRequestCustomizer` at send time. Payload JSON and endpoint URLs do not contain the token.

## Telemetry

PASS. TikTok uses the existing provider delivery activity source and meter:
- `ProviderDeliveryTelemetry.ActivitySourceName`
- `ProviderDeliveryMetrics.MeterName`

No TikTok-specific telemetry path was added.

## Health

PASS. TikTok messages use the same pending, delivered, failed, dead-lettered, and replay states as all provider delivery messages. Existing admin provider-delivery views can filter by provider.

## Performance

PASS. Payload generation is scoped to one canonical event. Purchase item lookup is read-only and only performed for purchase events. Delivery batching, parallelism, timeout, and backoff remain controlled by generic provider delivery infrastructure.

## Production Readiness

PASS. TikTok is ready as a first-class provider behind tenant configuration. A tenant must provide a valid Pixel ID, access token, endpoint, and timeout before dispatch is enabled.

## Explicit Runtime Verification

PASS. TikTok was implemented entirely through the existing Provider Delivery Infrastructure without modifying the Tracking Runtime.

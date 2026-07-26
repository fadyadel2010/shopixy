# PASS P2-H Storefront Analytics Authority Certification Findings

Date: 2026-06-09

## Verdict

PASS P2-H is production-certifiable for the inspected runtime. Analytics owns calculations,
aggregations, projections, dashboards, and presentation. It does not own orders, payments,
revenue, customers, promotions, or shipping. Storefront purchase cohorts and net revenue now
derive from tenant-scoped posted PostgreSQL storefront revenue journals and ecommerce order
identity. Redis and MongoDB remain rebuildable behavioral/projection stores.

## Findings

- Revenue findings: dashboard, attribution, and ROI revenue previously trusted `Order.Total`,
  Mongo attribution snapshots, or cached composites. They now use posted `EcommerceSales` ledger
  credits minus debits as-of the requested range end. Refunds reduce revenue and projections
  cannot inflate or suppress it.
- Order findings: completed purchase counts now require a posted storefront revenue journal and
  matching same-tenant ecommerce order. Created, unpaid, failed, or cancelled-without-recognition
  orders do not count. Later soft deletion cannot erase posted financial history.
- Conversion findings: behavioral funnel steps remain telemetry, but `PurchaseCompleted` and
  conversion numerators use SQL/ledger authority. Client purchase events remain rejected.
- Customer findings: analytics customer/session IDs are evidence only. Journey reads first prove
  current same-tenant financially recognized order ownership; analytics cannot create customer
  authority.
- Promotion findings: promotion/campaign dimensions are attribution evidence only. Revenue
  credited to a campaign is recomputed from authoritative order/ledger truth; expired or spoofed
  promotion metadata cannot create revenue.
- Traffic findings: visits, sessions, page views, referrers, and UTM values are behavioral
  telemetry, not commerce truth. URL secrets remain stripped while bounded allowlisted UTM fields
  are retained. Traffic remains inherently spoofable and must not be interpreted as financial
  authority.
- Dashboard findings: storefront, realtime, funnel, and live-monitoring purchase KPIs use the
  authority reader. The composite storefront dashboard cache was removed. KPI cards are
  presentation and cannot become authority.
- Export findings: no storefront analytics CSV/Excel export runtime exists. Financial exports are
  outside P2-H and remain governed by their certified SQL financial domains. Any future analytics
  export must call the same authority reader and trusted tenant context.
- Caching findings: Redis supplies hot behavioral telemetry only. Purchase counts, conversion
  completion, revenue, AOV, attribution revenue, and ROI no longer read Redis as authority.
  Mongo stores append-only events and attribution dimensions, not authoritative money.
- Update findings: analytics writes append telemetry or derivative attribution dimensions only.
  They cannot update orders, payments, ledger entries, customers, promotions, or shipping truth.
- Tenant-isolation findings: authority reads explicitly bind tenant ID; attribution Mongo filters,
  live monitoring, journeys, campaign costs, and controllers use trusted tenant context. Foreign
  order IDs cannot contribute revenue to another tenant.
- Concurrency findings: Mongo unique `(TenantId, EventId)` insertion is now the durable replay
  fence before Redis counters, dispatch, or attribution. Duplicate deliveries skip all side
  effects. Scoped dispatch/attribution work is awaited rather than detached.
- Historical-consistency findings: purchase cohorts come from original posted storefront revenue
  journals in the requested window. Net revenue applies posted credits/debits before the range end,
  so reruns converge and later refunds affect only applicable as-of ranges.
- Adversarial findings: DTO money is discarded; client purchase completion is rejected; route and
  tenant values cannot replace trusted context; custom ranges are capped at 366 days and
  pagination at 200; campaign cost currency must match current tenant currency; replay cannot
  duplicate projection side effects; cached or Mongo values cannot alter financial KPIs.

## Runtime Hardening Performed

- Added one SQL/ledger-backed storefront analytics authority reader.
- Routed dashboard, realtime, funnel/alert, live purchase, attribution, and ROI financial metrics
  through authoritative commerce/accounting reads.
- Removed the storefront dashboard composite cache.
- Replaced Redis purchase counters with authoritative purchase counts.
- Recomputed attribution/ROI money from ledger truth instead of immutable Mongo values.
- Made Mongo durable insertion the replay fence before projection mutations.
- Preserved bounded UTM evidence separately from sanitized URLs.
- Required current payment/tenant authority for server purchase telemetry and journey rendering.
- Enforced tenant currency for campaign cost/ROI and bounded historical queries.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Analytics/StorefrontAnalyticsAuthorityReader.cs`
- `PointOfSale.API/Services/Analytics/StorefrontAnalyticsDashboardService.cs`
- `PointOfSale.API/Services/Analytics/AnalyticsRealtimeService.cs`
- `PointOfSale.Infrastructure/Analytics/Alerts/AnalyticsMetricReader.cs`
- `PointOfSale.API/Services/Analytics/StorefrontLiveMonitoringService.cs`
- `PointOfSale.API/Services/Analytics/AnalyticsSessionJourneyService.cs`
- `PointOfSale.Infrastructure/Attribution/OrderAttributionStore.cs`
- `PointOfSale.Infrastructure/Attribution/AttributionContextResolver.cs`
- `PointOfSale.Infrastructure/CampaignCost/CampaignRoiService.cs`
- `PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/AuthoritativePurchaseAnalyticsConsumer.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/IAnalyticsColdStoreService.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/AnalyticsColdStoreService.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/AnalyticsEventDocument.cs`
- `PointOfSale.Core/Analytics/AnalyticsContext.cs`
- `PointOfSale.API/Services/Analytics/AnalyticsEventValidator.cs`
- `PointOfSale.API/Controllers/AdminAnalyticsAttributionController.cs`
- `PointOfSale.API/Controllers/AdminCampaignCostController.cs`
- `PointOfSale.API/Program.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontAnalyticsAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Analytics/StorefrontAnalyticsHardeningTests.cs`

## Explicit Answers

- Who owns analytics authority? Analytics owns derivation and presentation; PostgreSQL commerce
  domains and the posted ledger own source truth.
- Can revenue analytics diverge from commerce truth? Not on inspected paths; it is recomputed from
  posted storefront revenue journals and refunds.
- Can KPI cards become authority? No.
- Can cached analytics become authority? No.
- Can exports bypass authority? No storefront analytics export exists; future exports must use the
  same authority reader.
- Can analytics inflate revenue? No.
- Can analytics suppress revenue? No inspected projection/cache path can; posted ledger history is
  read directly.
- Can tenant leakage occur? No inspected path permits it.
- Can replay corrupt analytics? Duplicate event replay cannot repeat counters, dispatch, or
  attribution side effects.
- Were runtime code changes required? Yes.
- Is Storefront Analytics Authority production-certifiable? Yes, for the inspected runtime.

## Residual Risk

Traffic telemetry can be automated or blocked by clients and therefore remains approximate.
Production Mongo/Redis/PostgreSQL failover, high-volume aggregation, and replay chaos evidence is
operational proof not captured in this repository. Future analytics exports, materialized SQL
views, external warehouse ingestion, cross-currency stores, identity stitching, bot scoring, or
long-retention rollups require renewed P2-H review and freeze updates.

# MAIN Orders Domain Isolation And Localization Final Certification - 2026-09-28

## 1. Verdict

**Order-domain isolation implementation and automated regression certification: PASS.**

**Authenticated browser journey certification: PASS.**

Storefront/POS, CommerceHub, Affiliate, and marketing-attribution order surfaces now use explicit admission boundaries. Storefront Admin orders admit canonical Ecommerce orders only, CommerceHub vendor orders use dedicated list/detail endpoints, Affiliate projections require matching canonical Ecommerce order and SQL attribution evidence, and attribution count/paging applies canonical purchase membership and requested filters before page slicing.

English and Arabic resources cover the changed Storefront Orders, CommerceHub vendor orders, and attribution-period presentation. The relevant pages use locale-aware date/currency formatting and logical RTL-safe alignment classes.

This verdict is scoped to the implementation, builds, focused automated tests, static review, live route probes, and the authenticated single-tenant browser journey documented below. It is not a global production-readiness claim.

## 2. Domain Boundaries

### Storefront Orders

- `GET /api/admin/orders` forces `OrderSource.Ecommerce`.
- A non-Ecommerce `source` query is rejected with `ORDER_SCOPE_INVALID`.
- Service list queries default to Ecommerce and exclude CommerceHub sub-orders.
- The shared list service rejects internal scopes other than Ecommerce or Marketplace; `OrderSource.POS` cannot fall through to Marketplace results.
- Storefront details, ownership checks, status mutation reads, and locked mutation reads require canonical Ecommerce source.
- POS orders remain excluded from the Storefront list/detail path.
- The Admin client rejects any Storefront response containing `isMarketplaceOrder=true` or a source other than `Ecommerce`.

### CommerceHub Vendor Orders

- `GET /api/commercehub/vendor-orders` is the dedicated vendor list endpoint.
- `GET /api/commercehub/vendor-orders/{subOrderId}` is the dedicated vendor detail endpoint.
- List authority is constrained to `CommerceHubSubOrders.StoreId == current tenant` and Marketplace source.
- Detail authority requires the requested sub-order to belong to the current tenant and its parent order to remain active.
- The frontend no longer requests Marketplace rows through `/api/admin/orders`.
- Frontend admission rejects non-Marketplace rows, malformed lineage, foreign tenants, malformed return grants, and malformed action authority.

### Affiliate Orders

Admin and portal Affiliate lists now admit MongoDB projection rows only after resolving canonical SQL order IDs that satisfy all of the following:

- The order is not deleted.
- The order source is Ecommerce.
- A non-deleted SQL `AffiliateAttribution` exists for the same tenant and order.
- Affiliate account IDs match.
- Referral codes match.
- Promotion-link IDs match.
- An optional settlement-status filter is intersected with the canonical ID set before Mongo count and paging.

### Marketing Attribution

- Storefront purchase membership comes from `IStorefrontAnalyticsAuthorityReader` and canonical posted Ecommerce accounting/order evidence.
- Canonical purchase IDs are included in the MongoDB query before count, skip, and take.
- Model, source, and campaign filters are applied before count and paging.
- Source and campaign matching is exact and case-insensitive through escaped anchored regular expressions.
- `TotalCount` now represents the complete filtered canonical result set rather than only the returned page.
- Revenue remains sourced from the SQL/accounting authority snapshot rather than MongoDB projection values.

## 3. Localization And RTL

The following changed surfaces were localized in English and Arabic:

- Storefront order list, filters, empty/error states, and pagination.
- Storefront order detail lifecycle, fulfillment evidence, payment commands, shipment evidence, administrative commands, customer/source evidence, marketplace lineage, Affiliate attribution, totals, items, refunds, physical returns, receipt-only controls, payments, addresses, notes, and checkout attachments.
- CommerceHub vendor list/detail, readiness, backend-authorized action labels, return logistics, refund/return state, receipt controls, totals, lifecycle history, navigation, empty/error/loading states, and pagination.
- Marketing-attribution period labels, attribution-model labels, result count, date formatting, and table alignment.

Locale resource parity checks reported:

| Namespace | English keys with Arabic values |
|---|---:|
| `orders.management` | 169 |
| `commerceHub.vendorOrders` | 130 |
| `attribution.periods` | 6 |

Directional CSS in the audited pages uses logical forms such as `start`, `end`, `ms`, `ps`, and `border-s`; no physical left/right alignment class remained in the changed Orders, CommerceHub Orders, or Attribution Orders pages.

## 4. Regression Coverage

Backend coverage verifies:

- Default Storefront lists exclude CommerceHub and POS rows.
- Explicit Marketplace service queries return only CommerceHub vendor sub-orders.
- Storefront details reject CommerceHub sub-order IDs.
- Dedicated CommerceHub detail reads preserve parent/sub-order lineage.
- Tenant boundaries hold for Storefront, Affiliate, and CommerceHub records.
- PostgreSQL list execution preserves count and row isolation without set-operation failures.
- Static architecture checks require separate Storefront/CommerceHub contracts.
- Static architecture checks require canonical Affiliate admission before projection count/paging.
- Static architecture checks require attribution canonical membership and filters before count/paging.

Frontend coverage verifies:

- Storefront lifecycle controls execute only backend-advertised actions.
- Fulfillment, payment, cancellation, shipment, return, and refund controls remain distinct.
- Storefront response admission rejects cross-domain rows.
- CommerceHub list/detail services use dedicated endpoints.
- CommerceHub authority rejects malformed and cross-scope evidence.
- Storefront marketplace lifecycle controls are withheld and redirect to the dedicated CommerceHub surface.
- Localization-aware source-contract assertions replace stale English-literal checks.

## 5. Automated Verification

| Gate | Result |
|---|---|
| Final isolated API build | PASS: 168 repository compiler warnings, 0 errors |
| Focused backend tests | PASS: 9 passed, 0 failed, 0 skipped |
| Focused Admin tests | PASS: 4 files, 40 passed |
| Admin production Vite build | PASS |
| Authenticated headed-Chromium journey | PASS |
| Arabic RTL mobile checks at `390x844` | PASS |
| Locale JSON parsing | PASS |
| Scoped locale-key parity | PASS |
| Scoped `git diff --check` | PASS; line-ending notices only |

Focused backend filter:

```text
FullyQualifiedName~OrderDomainIsolationCertificationTests
|FullyQualifiedName~AdminOrderCrossChannelConvergenceTests
|FullyQualifiedName~AdminOrderListDeterministicTests
|FullyQualifiedName~AdminOrderListPostgresTests
```

The final focused test rerun compiled into an isolated output beneath the solution root so the user-started API could remain running. The backend test build emitted the repository's existing compiler and xUnit-analyzer warnings, but the selected tests completed successfully. A full isolated API rebuild emitted 168 repository compiler warnings and zero errors. The production Admin build retained the existing large-chunk warning.

## 6. Live Runtime Evidence

The API was restarted by the user and was listening on `http://localhost:5010` for live verification.

Swagger exposed separate route families:

```text
/api/admin/orders
/api/admin/orders/{orderId}
/api/admin/orders/{orderId}/cancel
/api/admin/orders/{orderId}/checkout-attachments/{attachmentId}/download
/api/admin/orders/{orderId}/status
/api/commercehub/vendor-orders
/api/commercehub/vendor-orders/{subOrderId}
```

Unauthenticated/no-tenant probes to both list routes returned controlled HTTP `400` responses before any order data was returned. This confirms route registration and fail-closed tenant admission.

### Authenticated Browser Journey

A temporary external Microsoft Playwright 1.57 harness launched installed Chromium in headed mode. Authentication was completed through the visible login page using a dedicated test account; the harness did not receive credentials, persist browser storage, or write cookies/tokens to disk. The journey was read-only and produced the following evidence:

- `http://ahramstore.localhost:5174/` loaded successfully and rendered a titled tenant storefront document in Chromium.
- Storefront Orders searched for `ORD-CH-0024-A` through `GET /api/admin/orders`; the HTTP `200` response contained neither the CommerceHub order ID nor order number.
- Direct Storefront navigation to `/ecommerce/orders/e7f16817-e361-436e-a7b9-c9a91c633ab1` called the Storefront detail endpoint and was rejected with HTTP `404`.
- CommerceHub vendor Orders searched through `GET /api/commercehub/vendor-orders`; the HTTP `200` response contained the expected vendor-owned sub-order ID and number.
- Direct CommerceHub navigation to `/commercehub/orders/e7f16817-e361-436e-a7b9-c9a91c633ab1` returned HTTP `200` from the dedicated detail endpoint and visibly rendered `ORD-CH-0024-A`.
- Attribution Orders returned HTTP `200` from `/api/admin/analytics/attribution/orders` and contained neither the CommerceHub-only order ID nor order number.
- The visible English Storefront, CommerceHub, and Attribution order headings rendered before locale switching.
- The UI language switcher changed the document to `lang="ar"` and `dir="rtl"`.
- Storefront Orders, CommerceHub Orders, and Attribution Orders visibly rendered their Arabic headings at a `390x844` viewport with no document-level horizontal overflow.

The harness observed six browser console/page-error events across the complete development journey, including the intentionally exercised Storefront `404`. Their messages were not persisted. No asserted route, response, localization, direction, visibility, or layout check failed.

`GET /health` returned HTTP `503`. The failing entry was unrelated to this change:

- `payout_external_truth_readiness`: Unhealthy because the latest reconciliation status was Critical with one mismatch.

Relevant dependencies and domains were healthy:

- PostgreSQL
- MongoDB
- Redis connectivity
- Elasticsearch
- RabbitMQ/MassTransit
- Cloudflare R2
- CommerceHub platform dependency schema/index checks
- CommerceHub search synchronization
- Canonical identity authority
- CommerceHub external-outcome recovery

The health response also reported degraded but non-blocking projection/latency/environment entries. No health remediation was attempted because those concerns are outside this order-isolation scope.

## 7. Data Evidence

A read-only PostgreSQL audit established that the originally observed order was a CommerceHub-only sub-order:

- Sub-order: `e7f16817-e361-436e-a7b9-c9a91c633ab1` / `ORD-CH-0024-A`
- Parent marketplace order: `4ef2b262-556f-4ed3-9262-7f7e5fe8b527` / `ORD-CH-0024`
- Vendor tenant: `c43aebf2-4cc4-406a-a2db-4df06d40bce5`
- Matching canonical `Orders` row: none

The former leakage therefore came from unioning `CommerceHubSubOrders` into generic Admin order results, not from a duplicate canonical Ecommerce order. The audit transaction was rolled back and made no data changes.

## 8. Security And Authority

- Tenant identity remains server-authoritative.
- Storefront and CommerceHub responses retain frontend defense-in-depth admission checks.
- CommerceHub vendor detail ownership is checked against `StoreId`.
- Affiliate Mongo projections cannot independently establish canonical order membership.
- Marketing-attribution Mongo projections cannot independently establish purchase or revenue authority.
- Lifecycle mutations remain backend-advertised and backend-revalidated.
- No credentials, cookies, bearer tokens, or secrets were written to repository files or this report.
- No production or development order mutation was performed during certification.
- Unrelated modified and untracked worktree content was preserved.

## 9. Files In Scope

Principal implementation and verification files include:

- `PointOfSale.API/Controllers/AdminOrdersController.cs`
- `PointOfSale.API/Controllers/CommerceHubOrdersController.cs`
- `PointOfSale.API/Controllers/AdminAffiliateController.cs`
- `PointOfSale.API/Controllers/AffiliatePortalController.cs`
- `PointOfSale.API/Controllers/AdminAnalyticsAttributionController.cs`
- `PointOfSale.API/Services/Admin/AdminOrderService.cs`
- `PointOfSale.API/Services/Admin/IAdminOrderService.cs`
- `PointOfSale.Infrastructure/Attribution/IOrderAttributionStore.cs`
- `PointOfSale.Infrastructure/Attribution/OrderAttributionStore.cs`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/services/adminOrdersApi.js`
- `PointOfSale.Web/src/services/commerceHubVendorOrdersApi.js`
- `PointOfSale.Web/src/pages/Orders.jsx`
- `PointOfSale.Web/src/pages/CommerceHubOrders.jsx`
- `PointOfSale.Web/src/modules/analytics/attribution/pages/AttributionOrdersPage.jsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`
- `PointOfSale.Tests/Admin/AdminOrderCrossChannelConvergenceTests.cs`
- `PointOfSale.Tests/Admin/AdminOrderListDeterministicTests.cs`
- `PointOfSale.Tests/Admin/AdminOrderListPostgresTests.cs`
- `PointOfSale.Tests/Architecture/OrderDomainIsolationCertificationTests.cs`
- `PointOfSale.Web/src/pages/Orders.test.ts`
- `PointOfSale.Web/src/pages/CommerceHubOrders.test.ts`
- `PointOfSale.Web/src/services/commerceHubVendorOrdersApi.test.ts`

## 10. Cleanup

- The external read-only audit harness at `C:\Users\fadya\AppData\Local\Temp\opencode\shopixy-order-audit` was removed.
- The external browser-certification harness at `C:\Users\fadya\AppData\Local\Temp\opencode\order-browser-cert` was removed after execution.
- Isolated API/test build outputs used for final verification were removed after execution.
- No test orders, attribution rows, MongoDB documents, or lifecycle mutations were created.
- The user-started API was left running for the user to stop when convenient.
- No unrelated worktree file was reset, cleaned, or reverted.

## 11. Residual Risk And Limitations

No blocking defect was found in the scoped implementation or automated tests.

The following validation limitations remain:

- The authenticated browser journey used one dedicated test account and one vendor tenant; separate Owner/Admin/Manager, insufficient-role, and foreign-tenant live matrices were not executed.
- Browser console/page-error events were counted but not persisted or individually classified; the journey intentionally generated one Storefront detail `404` and all scoped assertions passed.
- Mobile and Arabic RTL rendering was checked in headed Chromium through visible-content, document-direction, and overflow assertions; no screenshot baseline or cross-browser matrix was run.
- Overall API health remains HTTP `503` because of an unrelated payout external-truth reconciliation mismatch.

These limitations do not invalidate the passing scoped browser, implementation, build, and test evidence, but they prevent treating this report as a global role-matrix, cross-browser, or production-readiness certification.

## 12. Final Decision

**PASS for scoped order-domain isolation, canonical attribution admission, localization implementation, builds, focused automated regression tests, and the authenticated single-tenant browser journey.**

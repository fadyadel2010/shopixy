# Shopixy M-1B Publication-to-Marketplace Discovery Certification

Date: 2026-08-31

## Verdict

**Code certification: PASS. Live runtime certification: PASS. Human UI certification: PARTIAL. Overall M-1B certification: NOT YET COMPLETE.**

The inspected source, focused behavior and authority tests, production builds, supported-API lifecycle exercises, and cross-system reconciliation support the M-1B publication-to-discovery authority model. Human observation confirmed corrected public desktop/mobile discovery and merchant-maintained variant quantities. Overall certification remains open for a post-stock-policy public visual recheck and the merchant/governance authority screens. Browser automation was intentionally not used.

No build, source, test, HTTP response, or datastore inspection is promoted to human UI evidence in this report.

## Certified Boundary

- PostgreSQL owns tenant, vendor-governance, product, variant, inventory, category, listing, publication-intent, price-override, and suppression authority.
- Merchant publication and unpublication occur through supported tenant APIs.
- Super Admin suppression and restoration contain public visibility without taking ownership of merchant publication intent.
- Public browse and search apply current SQL admission, tenant, exclusion, category, and text predicates before deterministic pagination.
- MongoDB and Elasticsearch are derivative discovery/detail projections and cannot admit a listing that current SQL authority rejects.
- RabbitMQ, outbox rows, Redis invalidation, and consumer fences are delivery and convergence evidence, not business authority.
- Detail projection lag may temporarily fail closed after publication; SQL-first browse remains authoritative and the detail projection must converge.

## Authority Matrix

| Concern | Canonical owner | M-1B result |
| --- | --- | --- |
| Listing identity | SQL `CommerceHubListing`, unique tenant and variant lineage | PASS |
| Merchant publication intent | Tenant publication command and SQL listing state | PASS |
| Current category | SQL listing category plus live category lineage | PASS |
| Product/variant text and SKU | Current SQL product and variant authority | PASS |
| Tenant and exclusion filters | Base SQL listing predicate | PASS |
| Vendor eligibility | Active tenant and active, KYC-verified, marketplace-approved Vendor profile | PASS |
| Public pagination | SQL admission followed by stable ordering and paging | PASS |
| Super Admin containment | SQL listing suppression with restore-publication intent | PASS |
| MongoDB detail projection | Derivative, generation-fenced | PASS by live convergence |
| Elasticsearch search projection | Derivative, generation-fenced | PASS by live convergence |
| Redis invalidation | Derivative cache lineage | PASS by live reconciliation |
| Human UI | Public CommerceHub and merchant inventory observed; post-policy stock labels and governance screens remain | PARTIAL |

## Defects Closed

1. Public category filtering was not part of the base SQL listing predicate. It now uses the listing's current authoritative category before candidate paging.
2. Public SQL-first search candidates omitted the current variant SKU. Candidate authority now includes `SKU` and `AttributesJson` alongside product name and description.
3. Exact punctuation-bearing terms such as `NOKIA-N73-BLACK-32` were normalized asymmetrically. Both candidate values and terms are now normalized before matching.
4. Requested-tenant and excluded-tenant constraints were applied outside the base listing predicate. They now participate in SQL admission before ordering and pagination.
5. Cross-tenant regression fixtures were accidentally constrained by EF query filters. Test-only authority lookups now use `IgnoreQueryFilters()` to construct the intended scenarios without weakening production filters.
6. Genuine UI smoke found that the CommerceHub client-supplied `lang=en` parameter was parsed as a dynamic product attribute. Because `lang` is not a live category attribute, public search correctly failed closed but rendered an empty catalog. The parser now reserves `lang` as request metadata while preserving real `attr.*` filters, with a focused regression.
7. Product detail recomputed availability only as `TotalStock > 0`, which discarded SQL-resolved untracked/backorder policy. Detail variants now carry SQL-resolved availability, projected variants retain quantity-derived availability, and available untracked zero-quantity products use the truthful `Available` label rather than `In stock (0)`.
8. Product authoring forcibly persisted `TrackStock=false` for every variant product, ignoring the supported API request and making maintained variant inventory unenforceable. Variant products now preserve the requested product stock policy, with an integration regression.

## Verification Matrix

| Evidence class | Command/evidence | Result |
| --- | --- | --- |
| Focused discovery/detail behavior | Query-service, detail mapping, and language-query parser regressions | 16/16 passed |
| Product authoring stock policy | Product-authoring producer completeness and variant stock-policy regression | 9/9 passed |
| Combined post-fix backend selection | Discovery, detail, parser, and product-authoring stock policy | 25/25 passed |
| Broader M-1B backend | Discovery, parser, publication intent/lifecycle, governance, and emergency visibility selection | 44/44 passed |
| Frontend authority | POS vendor route/session and marketplace-governance authority selection | 15/15 passed |
| Backend production build | `dotnet build PointOfSale.sln -c Release --no-restore` with isolated output | PASS, 0 errors; existing warnings remain |
| CommerceHub production build | `npm run build` in `PointOfSale.CommerceHub` | PASS; large-chunk warning remains |
| POS web production build | `npm run build` in `PointOfSale.Web` | PASS; large-chunk warning remains |
| Marketplace health | `GET /health/marketplace` | Healthy; no pending PostgreSQL migrations or Mongo/Elasticsearch drift |
| Public browse/search | Unfiltered, category, exact SKU, tenant, unknown-filter, and pagination probes | PASS |
| Access isolation | POS-authenticated foreign tenant request | `403` |
| Merchant lifecycle | Supported unpublish, replay, republish, and replay APIs | PASS |
| Governance lifecycle | Supported suppress, replay, restore, and replay APIs | PASS |
| Projection convergence | Read-only SQL, MongoDB, Elasticsearch, Redis, RabbitMQ, outbox, and fence inspection | PASS at product projection version 22 |
| Human UI | Genuine public desktop/mobile and merchant inventory observation | PARTIAL; final stock/governance recheck pending |

## Live Certification Lineage

- Tenant/Vendor: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`
- Product: `580b30bd-1829-131c-2cc4-5f6cf3994e09` (`Nokia N73`)
- Variant: `e5857268-db63-4806-9af8-f5706bbda5cf`
- SKU: `NOKIA-N73-BLACK-32`
- Listing: `884794fc-550b-45e1-9890-3fd309e008ba`
- Zero-stock variant: `df0b7567-1470-4f42-b14a-8143201f115b` (`NOKIA-N73-BLACK-16`)
- Zero-stock listing: `f5909ab4-1194-48b6-8aca-9bbc9ffd516d`
- Category: `10000000-0000-0000-0000-000000000012` (`Mobile Phones`)

Final SQL authority for the primary certified listing contained exactly one active lineage row. It was published, not deleted, unsuppressed, and had `RestorePublicationOnActivate=false`, EGP 400 effective price, stock 3, product projection mutation version 22, active category lineage, and an active/KYC-verified/marketplace-approved vendor. The sibling zero-stock listing remained published but is unavailable by stock policy.

## Runtime Results

- Unfiltered public browse returned two admitted listings.
- Current category browse/search returned two admitted listings.
- Exact SKU browse/search returned exactly the certified listing.
- After the parser repair and API restart, the exact CommerceHub browser-proxy request including `lang=en` returned one `Nokia N73`; unfiltered and category requests returned two.
- A valid requested tenant succeeded; unknown tenant, category, and SKU filters returned empty `200` responses.
- Page size one produced stable, non-overlapping pages.
- Product detail returned `200` after derivative convergence.
- Merchant unpublish immediately removed SQL admission, browse returned zero, detail failed closed with `404`, and exact replay reported no change.
- Merchant republish restored category, price, and attributes. SQL-first SKU browse recovered immediately, exact replay reported no change, and detail converged to `200`.
- Super Admin suppression removed public browse/detail while preserving restore-publication intent. Exact replay reported no change.
- Super Admin restoration returned the listing to published and unsuppressed state. Exact replay reported no change.
- Human public UI observation after the `lang` repair confirmed exact search, unfiltered browse, detail navigation, and narrow/mobile rendering. Merchant screenshots confirmed four maintained Nokia variants with quantities `5`, `0`, `0`, and `3`.
- The first supported settings-only product update exposed the variant-authoring override: the request returned `200` and advanced the row version but retained `TrackStock=false`. Variant identities and quantities were unchanged.
- After repairing product authoring and restarting only the API, a version-guarded, idempotent supported `PUT /api/products/{id}` persisted `TrackStock=true` with `AllowBackorder=false`; publication and all four variant identities/quantities remained unchanged.
- Exact zero-stock SKU search returned listing `f5909ab4-1194-48b6-8aca-9bbc9ffd516d` with `InStock=false`; detail returned stock `0` and the default variant unavailable. The quantity-3 sibling search/detail returned `InStock=true` and stock `3`.
- MongoDB and Elasticsearch converged to product projection event version 22. Both contained the zero-stock listing as unavailable and the quantity-3 listing as available.
- The matching Redis lineage key was absent and the RabbitMQ projection queue was drained.
- No direct SQL, MongoDB, Elasticsearch, Redis, RabbitMQ, checkout, payment, order, or historical-order mutation was used.

## Residual Risks

- Public desktop/mobile discovery was observed after the `lang` repair. A final visual refresh is still required to confirm `Out of stock` for the zero-stock listing and `In stock` for the quantity-3 sibling after enabling stock tracking.
- Merchant publication and Super Admin governance screens still require human observation. POS variant-inventory screenshots confirm maintained quantities but do not certify publication/governance presentation.
- Baseline aggregate `/health` and `/health/ready` probes were Degraded because of broader application dependencies. The scoped `/health/marketplace` probe is Healthy and reports zero marketplace drift.
- Direct access to the configured MongoDB port was unavailable from the shell. MongoDB was nevertheless verified through scoped health and the configured read-only driver inspector.
- Production bundles retain existing large-chunk warnings. They do not block this authority certification but remain a performance concern.
- A short detail `404` immediately after publication is expected while the derivative projection converges. Continued or unbounded lag would be operationally unacceptable and should remain monitored.

## Human UI Exit Criteria

1. On desktop and mobile, refresh `http://localhost:5175/products?search=NOKIA-N73-BLACK-16` and confirm exactly one `Nokia N73` result labeled `Out of stock`, with no stale or duplicate card.
2. Open that product card and confirm detail renders stock `0` as unavailable. Repeat for `NOKIA-N73-BLACK-32` and confirm stock `3` remains available at EGP 400.
3. Use the public category control for `Mobile Phones` and confirm the URL-backed filter remains selected after refresh and the two Nokia listings have consistent stock labels.
4. Sign in to POS web on port 5173, open `/admin/commercehub`, and confirm the certified variant is published with the expected category, price, and listing state. Do not mutate it during observation.
5. Sign in as Super Admin, open `/super-admin/marketplace/governance`, search for `NOKIA-N73-BLACK-32`, and confirm the listing is `Published`, vendor/tenant authority is active, and suppression is absent. Do not invoke lifecycle actions during observation.
6. Confirm no broken layout, clipped controls, inaccessible modal/drawer, stale state, or console-visible fatal error at desktop and mobile widths.

After those observations are recorded, M-1B may be promoted from code- and runtime-certified to fully certified without another authority mutation, provided scoped marketplace health remains Healthy.

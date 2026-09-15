# M3B Projection Execution Report — Shopixy Load Harness

> Date (UTC): 2026-09-15T18:10:04.365097+00:00. Scope: M3-B1 + M3-B2 only. No k6 load, no orders/sales/payments, no providers.

## 1. Environment

- API: `http://localhost:5010` (`Development`, Windows local); infra Ubuntu `192.168.1.5` (Postgres/Mongo/Redis/RabbitMQ/ES).
- `TEST_ENVIRONMENT=local`; `TEST_DB_PROOF` matched approved host+db (gate-verified); `LOADTEST_SEED_PASSWORD` present in process memory only (freshly rotated, never logged/stored).
- k6 NOT run. No stress/soak/spike.

## 2. Database target

- Host `192.168.1.5`, database `PosEcommerceDb_Baseline` (approved local baseline). Mongo `ecommerce_read_db`; ES `http://192.168.1.5:9200`.

## 3. Tenants processed (sequential, no parallelization)

- tenant-a: `8242b275-ee28-5e42-b37c-62eb48e2c1b6` (loadtest-tenant-a)
- tenant-b: `d74f075b-e0f5-57a2-80cd-cd3ab6ed616e` (loadtest-tenant-b)
- tenant-c: `6b9c21af-8eba-5b8a-87d7-da4a40263717` (loadtest-tenant-c)

## 4. StoreConfig creation method

- Confirmed path per `M3B_STORECONFIG_PATH_AUDIT.md`: `GET /api/admin/store-config` → `PUT /api/admin/store-config/status` (`storefrontStatus=live` + RowVersion), Owner JWT, per-tenant. Registration NOT used; no SQL edits.
- Note: the rows were created by the first Bearer-authenticated execution pass via the same `PUT …/status` create-or-upsert (RowVersion `0` → persisted row + `StoreConfigCreatedEvent`); this final pass re-verified idempotently (`alreadyLive`, no write needed).
- tenant-a: read={'http': 200, 'rowVersion': '3775055', 'status': 'live', 'tenantMatch': True} publication={'http': None, 'created': False, 'alreadyLive': True, 'status': 'live'} verified={'http': 200, 'status': 'live', 'tenantMatch': True, 'rowVersion': '3775055'}
- tenant-b: read={'http': 200, 'rowVersion': '3776552', 'status': 'live', 'tenantMatch': True} publication={'http': None, 'created': False, 'alreadyLive': True, 'status': 'live'} verified={'http': 200, 'status': 'live', 'tenantMatch': True, 'rowVersion': '3776552'}
- tenant-c: read={'http': 200, 'rowVersion': '3778038', 'status': 'live', 'tenantMatch': True} publication={'http': None, 'created': False, 'alreadyLive': True, 'status': 'live'} verified={'http': 200, 'status': 'live', 'tenantMatch': True, 'rowVersion': '3778038'}

### 4b. Unit backfill (M2-deferred prerequisite, organic path)

- `POST /api/units/seed-defaults` (+ `POST /api/units` fallback) then `POST /api/products/{id}/units` per simple product lacking units (base Piece, default, conversion 1, product price/barcode; Idempotency-Key + If-Match; authoring orchestrator events). No stock impact; variant-container skipped (variant admission).
- tenant-a: seedDefaults={'http': 200} piece=True attached=0 present=17 other=[{'slug': 'lt-a-product-002', 'productId': 'b44bc350-3a28-528a-8702-30fe0f9c182f', 'action': 'price-corrected'}, {'slug': 'lt-a-product-003', 'productId': '29da9226-2f7f-5765-b01c-bbdca5dcb6b1', 'action': 'price-corrected'}]
- tenant-b: seedDefaults={'http': 200} piece=True attached=0 present=17 other=[{'slug': 'lt-b-product-002', 'productId': 'ac15c5b7-cc6a-5ad4-96bb-13344d1b99f1', 'action': 'price-corrected'}, {'slug': 'lt-b-product-003', 'productId': 'f037e7f6-bbca-5948-acbe-eba9b408b548', 'action': 'price-corrected'}]
- tenant-c: seedDefaults={'http': 200} piece=True attached=0 present=18 other=[{'slug': 'lt-c-product-002', 'productId': '70dce816-a111-5295-ae15-c532496e40d1', 'action': 'price-corrected'}]

### 4c. SQL catalog end-state (read-only guard)

- tenant-a: {'total': 20, 'publishedVisible': 20, 'defaultUnits': 19}
- tenant-b: {'total': 20, 'publishedVisible': 20, 'defaultUnits': 19}
- tenant-c: {'total': 20, 'publishedVisible': 20, 'defaultUnits': 19}

## 5. Publication results

- tenant-a: login={'http': 200, 'ok': True, 'tenantBound': True, 'role': 'Owner', 'sessionCookie': '***REDACTED***'}
- tenant-b: login={'http': 200, 'ok': True, 'tenantBound': True, 'role': 'Owner', 'sessionCookie': '***REDACTED***'}
- tenant-c: login={'http': 200, 'ok': True, 'tenantBound': True, 'role': 'Owner', 'sessionCookie': '***REDACTED***'}

## 6. Mongo sync results

- tenant-a: categories={'http': 200, 'count': 4, 'staleDeleted': 0} products={'http': 200, 'totalProcessed': 20, 'successCount': 20, 'skippedCount': 0, 'errorCount': 0, 'errors': []}
- tenant-b: categories={'http': 200, 'count': 4, 'staleDeleted': 0} products={'http': 200, 'totalProcessed': 20, 'successCount': 20, 'skippedCount': 0, 'errorCount': 0, 'errors': []}
- tenant-c: categories={'http': 200, 'count': 4, 'staleDeleted': 0} products={'http': 200, 'totalProcessed': 20, 'successCount': 20, 'skippedCount': 0, 'errorCount': 0, 'errors': []}

## 7. ES sync results

- tenant-a: full={'http': 200, 'elapsedMs': 1536} waitS=60
- tenant-b: full={'http': 200, 'elapsedMs': 1402} waitS=60
- tenant-c: full={'http': 200, 'elapsedMs': 1337} waitS=60

## 8. Organic A-path spot-check results (2 ordinary products/tenant)

- tenant-a lt-a-product-002: {'productId': 'b44bc350-3a28-528a-8702-30fe0f9c182f', 'slug': 'lt-a-product-002', 'stateBefore': {'publishStatus': 'Draft', 'isOnlineVisible': False}, 'restorePublish': {'http': 200, 'item': {'status': 'Processed', 'code': None, 'previousVersion': '3785491', 'newVersion': '3785890'}}, 'restoreLagS': 5.2, 'rowVersionBefore': '3785890', 'unpublishHttp': 200, 'unpublishAtUtc': '2026-09-15T18:06:08.305769+00:00', 'unpublishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3785890', 'newVersion': '3785922'}, 'unpublishProjectionLagS': 5.0, 'publishHttp': 200, 'publishAtUtc': '2026-09-15T18:06:13.645260+00:00', 'publishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3785922', 'newVersion': '3786039'}, 'publishProjectionLagS': 5.0}
- tenant-a lt-a-product-003: {'productId': '29da9226-2f7f-5765-b01c-bbdca5dcb6b1', 'slug': 'lt-a-product-003', 'stateBefore': {'publishStatus': 'Draft', 'isOnlineVisible': False}, 'restorePublish': {'http': 200, 'item': {'status': 'Processed', 'code': None, 'previousVersion': '3785504', 'newVersion': '3786064'}}, 'restoreLagS': 5.0, 'rowVersionBefore': '3786064', 'unpublishHttp': 200, 'unpublishAtUtc': '2026-09-15T18:06:24.800285+00:00', 'unpublishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3786064', 'newVersion': '3786156'}, 'unpublishProjectionLagS': 5.0, 'publishHttp': 200, 'publishAtUtc': '2026-09-15T18:06:30.233454+00:00', 'publishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3786156', 'newVersion': '3786181'}, 'publishProjectionLagS': 5.0}
- tenant-b lt-b-product-002: {'productId': 'ac15c5b7-cc6a-5ad4-96bb-13344d1b99f1', 'slug': 'lt-b-product-002', 'stateBefore': {'publishStatus': 'Draft', 'isOnlineVisible': False}, 'restorePublish': {'http': 200, 'item': {'status': 'Processed', 'code': None, 'previousVersion': '3786217', 'newVersion': '3786533'}}, 'restoreLagS': 5.0, 'rowVersionBefore': '3786533', 'unpublishHttp': 200, 'unpublishAtUtc': '2026-09-15T18:07:47.495078+00:00', 'unpublishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3786533', 'newVersion': '3786577'}, 'unpublishProjectionLagS': 5.0, 'publishHttp': 200, 'publishAtUtc': '2026-09-15T18:07:52.955319+00:00', 'publishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3786577', 'newVersion': '3786606'}, 'publishProjectionLagS': 5.0}
- tenant-b lt-b-product-003: {'productId': 'f037e7f6-bbca-5948-acbe-eba9b408b548', 'slug': 'lt-b-product-003', 'stateBefore': {'publishStatus': 'Draft', 'isOnlineVisible': False}, 'restorePublish': {'http': 200, 'item': {'status': 'Processed', 'code': None, 'previousVersion': '3786230', 'newVersion': '3786667'}}, 'restoreLagS': 5.0, 'rowVersionBefore': '3786667', 'unpublishHttp': 200, 'unpublishAtUtc': '2026-09-15T18:08:03.832944+00:00', 'unpublishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3786667', 'newVersion': '3786692'}, 'unpublishProjectionLagS': 5.0, 'publishHttp': 200, 'publishAtUtc': '2026-09-15T18:08:09.335392+00:00', 'publishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3786692', 'newVersion': '3786722'}, 'publishProjectionLagS': 5.0}
- tenant-c lt-c-product-002: {'productId': '70dce816-a111-5295-ae15-c532496e40d1', 'slug': 'lt-c-product-002', 'stateBefore': {'publishStatus': 'Draft', 'isOnlineVisible': False}, 'restorePublish': {'http': 200, 'item': {'status': 'Processed', 'code': None, 'previousVersion': '3786864', 'newVersion': '3787146'}}, 'restoreLagS': 5.0, 'rowVersionBefore': '3787146', 'unpublishHttp': 200, 'unpublishAtUtc': '2026-09-15T18:09:26.848474+00:00', 'unpublishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3787146', 'newVersion': '3787192'}, 'unpublishProjectionLagS': 5.0, 'publishHttp': 200, 'publishAtUtc': '2026-09-15T18:09:32.630953+00:00', 'publishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3787192', 'newVersion': '3787235'}, 'publishProjectionLagS': 5.0}
- tenant-c lt-c-product-003: {'productId': '6c1d33d6-7f01-5ac9-9bb6-bec9c97e93a2', 'slug': 'lt-c-product-003', 'stateBefore': {'publishStatus': 'Published', 'isOnlineVisible': True}, 'rowVersionBefore': '3785328', 'unpublishHttp': 200, 'unpublishAtUtc': '2026-09-15T18:09:37.956239+00:00', 'unpublishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3785328', 'newVersion': '3787265'}, 'unpublishProjectionLagS': 5.0, 'publishHttp': 200, 'publishAtUtc': '2026-09-15T18:09:43.361486+00:00', 'publishItem': {'status': 'Processed', 'code': None, 'previousVersion': '3787265', 'newVersion': '3787310'}, 'publishProjectionLagS': 5.0}

## 9. Projection lag observed

- ES assertion window: 60s per tenant (audit guidance).
- Organic spot-check lags recorded per item in §8 (Mongo poll, 5s interval).

## 10. Redis observations (passive; no direct mutation)

- No manual clear, no `clear-cache` endpoint, no direct Redis writes. Projection writer invalidation relied upon (product sync clears tenant product cache per API contract).

## 11. Mongo verification

- tenant-a: {'products': 20, 'wantProducts': 20, 'categories': 4, 'wantCategories': 4, 'tenantMatch': True, 'sentinelPresent': True, 'sentinelSku': 'LT-PROBE-A', 'sentinelSlug': 'lt-probe-tenant-a', 'sentinelPrice': '111.11', 'foreignProbeHits': 0, 'foreignTenantLeak': 0}
- tenant-b: {'products': 20, 'wantProducts': 20, 'categories': 4, 'wantCategories': 4, 'tenantMatch': True, 'sentinelPresent': True, 'sentinelSku': 'LT-PROBE-B', 'sentinelSlug': 'lt-probe-tenant-b', 'sentinelPrice': '222.22', 'foreignProbeHits': 0, 'foreignTenantLeak': 0}
- tenant-c: {'products': 20, 'wantProducts': 20, 'categories': 4, 'wantCategories': 4, 'tenantMatch': True, 'sentinelPresent': True, 'sentinelSku': 'LT-PROBE-C', 'sentinelSlug': 'lt-probe-tenant-c', 'sentinelPrice': '333.33', 'foreignProbeHits': 0, 'foreignTenantLeak': 0}

## 12. ES verification

- tenant-a: {'sentinelDocHttp': 200, 'tenantField': 'tenantId', 'sentinelTenantMatch': True, 'lookupMs': 15.3, 'productsCount': 20, 'categoriesCount': 4, 'searchBySlug': 1, 'searchBySku': 1, 'foreignProbeBHits': 0, 'foreignProbeCHits': 0}
- tenant-b: {'sentinelDocHttp': 200, 'tenantField': 'tenantId', 'sentinelTenantMatch': True, 'lookupMs': 18.0, 'productsCount': 20, 'categoriesCount': 4, 'searchBySlug': 1, 'searchBySku': 1, 'foreignProbeAHits': 0, 'foreignProbeCHits': 0}
- tenant-c: {'sentinelDocHttp': 200, 'tenantField': 'tenantId', 'sentinelTenantMatch': True, 'lookupMs': 22.8, 'productsCount': 20, 'categoriesCount': 4, 'searchBySlug': 1, 'searchBySku': 1, 'foreignProbeAHits': 0, 'foreignProbeBHits': 0}

## 13. Storefront verification (anonymous, Host header)

- tenant-a: {'products': {'http': 200, 'hash': '42814217da80b742'}, 'categories': {'http': 200, 'hash': '45191aae3f6f2e3a'}, 'byId': {'http': 200, 'hash': 'ff13b096e27bb337'}, 'search': {'http': 200, 'hash': 'ed4f0ec387de9189', 'foreignLeak': False, 'foreignLeakIds': []}, 'suggest': {'http': 200, 'hash': '02c5b915e6910a1d', 'foreignLeak': False, 'foreignLeakIds': []}, 'resolveSlug': {'http': 200, 'hash': '9355e7e3efe4ba95'}}
- tenant-b: {'products': {'http': 200, 'hash': '8076d79f7ace1e9c'}, 'categories': {'http': 200, 'hash': 'ab0fccbd351607ec'}, 'byId': {'http': 200, 'hash': 'c74acbc5028e4fe3'}, 'search': {'http': 200, 'hash': '73197b5d0c3b0650', 'foreignLeak': False, 'foreignLeakIds': []}, 'suggest': {'http': 200, 'hash': '0f2055206251a4cf', 'foreignLeak': False, 'foreignLeakIds': []}, 'resolveSlug': {'http': 200, 'hash': 'cb710fcc8e77be74'}}
- tenant-c: {'products': {'http': 200, 'hash': '0f6b1a470eefdabe'}, 'categories': {'http': 200, 'hash': '58e438d56bce79f2'}, 'byId': {'http': 200, 'hash': '10b801ab262084df'}, 'search': {'http': 200, 'hash': '6df4f6f4a759f88e', 'foreignLeak': False, 'foreignLeakIds': []}, 'suggest': {'http': 200, 'hash': '4b139789d0a7a070', 'foreignLeak': False, 'foreignLeakIds': []}, 'resolveSlug': {'http': 200, 'hash': '9d629853d8bbe4bd'}}

## 14. I-1 isolation result (no foreign products/probes)

- a->b: {'src': 'a', 'dst': 'b', 'byIdHttp': 404, 'byIdLeak': False, 'searchHttp': 200, 'searchLeak': False, 'searchLeakIds': []}
- b->c: {'src': 'b', 'dst': 'c', 'byIdHttp': 404, 'byIdLeak': False, 'searchHttp': 200, 'searchLeak': False, 'searchLeakIds': []}
- c->a: {'src': 'c', 'dst': 'a', 'byIdHttp': 404, 'byIdLeak': False, 'searchHttp': 200, 'searchLeak': False, 'searchLeakIds': []}

## 15. I-2 isolation result (store resolves to own tenant)

- Covered by resolve-slug + by-id + search assertions in §13 (own tenant data, no foreign probes) and §14 cross-tenant probes.

## 16. Stock budget verification

- before=121 rows after=121 rows identical=True: 121 rows identical (quantity+reserved)

## 17. Request/correlation evidence

- 149 gated HTTP calls recorded (method/path/status/ latency/body-hash/request-id/correlation-id; secrets redacted).
  - GET /api/admin/store-config ×6
  - GET /api/products/05a18eef-1a22-57cd-8e60-968aef8ec4bb/units ×1
  - GET /api/products/07b223c2-8f79-5903-9d47-44ac04736965/units ×1
  - GET /api/products/09c201ec-a3e8-58f8-81ec-7f259cf755a6/units ×1
  - GET /api/products/1b30adde-77fe-57d6-9c2f-e2c50d306620/units ×1
  - GET /api/products/1fa43705-6af5-50f5-811e-4b7831e8af09/units ×1
  - GET /api/products/29da9226-2f7f-5765-b01c-bbdca5dcb6b1 ×4
  - GET /api/products/29da9226-2f7f-5765-b01c-bbdca5dcb6b1/units ×1
  - GET /api/products/2a8547fd-3d75-5405-a18f-7fd82eda7d49/units ×1
  - GET /api/products/353058f3-f76a-5193-9e4e-5bfdc0f6d268/units ×1
  - GET /api/products/37347c8f-56cc-50e6-a79e-9543b224235e/units ×1
  - GET /api/products/3d941248-0757-5630-b703-4d31cb41fd8f/units ×1
  - GET /api/products/405b1e70-6192-503f-9cbd-ca80d14ca163/units ×1
  - GET /api/products/40daf850-eec8-5685-b285-52c042832a05/units ×1
  - GET /api/products/43e9007e-f1c4-5de6-8341-455b7af5ed01/units ×1
  - GET /api/products/43f18759-7b7e-5d59-b44a-1ba374643a72/units ×1
  - GET /api/products/56b18ab0-dbeb-5609-b1ab-c661696bdb0f/units ×1
  - GET /api/products/5908c8ab-73f0-552f-a658-7a60e4080dcc/units ×1
  - GET /api/products/5ae19982-a800-5b40-9da4-a8b8214f182d/units ×1
  - GET /api/products/5de99157-ab1b-5369-9fda-d6115e533e64/units ×1
  - GET /api/products/5e39369a-7fbd-50c5-ac91-ea0eac752462/units ×1
  - GET /api/products/64bf74ec-287f-5a29-a7b9-2c62c01c3a96/units ×1
  - GET /api/products/6a18efce-0121-52fd-a631-f545b4ea2bb1/units ×1
  - GET /api/products/6ba716bc-bc54-52ae-8cbc-f305fb37d3c1/units ×1
  - GET /api/products/6c1d33d6-7f01-5ac9-9bb6-bec9c97e93a2 ×2
  - GET /api/products/6c1d33d6-7f01-5ac9-9bb6-bec9c97e93a2/units ×1
  - GET /api/products/70605470-88d3-550a-9b93-11289ca42dcb/units ×1
  - GET /api/products/70dce816-a111-5295-ae15-c532496e40d1 ×4
  - GET /api/products/70dce816-a111-5295-ae15-c532496e40d1/units ×1
  - GET /api/products/71f296b9-52e1-5b98-8616-f9deb07353f4/units ×1
  - GET /api/products/7309b1b2-6fee-51ed-9032-831864511ab0/units ×1
  - GET /api/products/75438cef-65ba-53b9-858e-7fc1d2022dd4/units ×1
  - GET /api/products/7683be19-cdb4-5be1-8759-f9f689435bd4/units ×1
  - GET /api/products/7f40cf11-4fb0-5025-99b3-8cd90bb20e72/units ×1
  - GET /api/products/8297bf62-b591-5a39-9e22-bc6f097be919/units ×1
  - GET /api/products/87ca196a-144d-598c-b0df-f4a83f01cced/units ×1
  - GET /api/products/8c92e2aa-cc95-562a-82cc-5421cb3e1aac/units ×1
  - GET /api/products/92cb4746-8798-5ce6-8d7f-0d7b4a2a8082/units ×1
  - GET /api/products/93c7034e-d46d-5317-a38d-b955c8ba891e/units ×1
  - GET /api/products/9a294ebb-acdf-5748-a5df-f29061677f44/units ×1
  - GET /api/products/9cc6041f-bd9d-5b24-ad38-0700ae2a8454/units ×1
  - GET /api/products/9d992f72-aa8e-57d2-9a7c-f1f26063a208/units ×1
  - GET /api/products/a224fa70-eb2b-5293-9cd1-06a55b35d6a5/units ×1
  - GET /api/products/a694d285-a8cf-58f9-9420-2444d82635dd/units ×1
  - GET /api/products/a6b0668b-823f-56b8-81c6-cee6f5b8b5fb/units ×1
  - GET /api/products/ac15c5b7-cc6a-5ad4-96bb-13344d1b99f1 ×4
  - GET /api/products/ac15c5b7-cc6a-5ad4-96bb-13344d1b99f1/units ×1
  - GET /api/products/adc0e08b-64e5-59d6-9153-06456aeb5c5c/units ×1
  - GET /api/products/b3cc5750-97b3-5408-aabb-36e86ba01ad3/units ×1
  - GET /api/products/b44bc350-3a28-528a-8702-30fe0f9c182f ×4
  - GET /api/products/b44bc350-3a28-528a-8702-30fe0f9c182f/units ×1
  - GET /api/products/b4d59ad2-5082-54dd-afd6-f627094189e7/units ×1
  - GET /api/products/b677f7e1-60bf-5d9f-b85a-505a94ac912d/units ×1
  - GET /api/products/be9a9d6c-a10f-5486-86cf-d23b03e37ef1/units ×1
  - GET /api/products/cdf712a5-e2ae-56c7-bead-a2cc85f84570/units ×1
  - GET /api/products/ce936cfc-9ebf-5a5e-bd65-abd9f570bb2a/units ×1
  - GET /api/products/d277a0f2-766c-5d2c-9c30-c4f4ab28266c/units ×1
  - GET /api/products/d3d0f57f-202b-54a7-9d26-b62cede71182/units ×1
  - GET /api/products/d6b442ac-b34a-54e8-9876-dde7af653b4e/units ×1
  - GET /api/products/e934feb7-0114-5bef-b12b-4e13e0272acb/units ×1
  - GET /api/products/f037e7f6-bbca-5948-acbe-eba9b408b548 ×4
  - GET /api/products/f037e7f6-bbca-5948-acbe-eba9b408b548/units ×1
  - GET /api/products/fb07a6e2-e1a9-516b-970b-ba2cd81b3eef/units ×1
  - GET /api/products/fcd29f5d-1be7-5b58-bad1-8c8b86a905d3/units ×1
  - GET /api/storefront/categories ×3
  - GET /api/storefront/products ×3
  - GET /api/storefront/products/by-id/a6b0668b-823f-56b8-81c6-cee6f5b8b5fb ×2
  - GET /api/storefront/products/by-id/adc0e08b-64e5-59d6-9153-06456aeb5c5c ×2
  - GET /api/storefront/products/by-id/cdf712a5-e2ae-56c7-bead-a2cc85f84570 ×2
  - GET /api/storefront/resolve-slug/lt-probe-tenant-a ×1
  - GET /api/storefront/resolve-slug/lt-probe-tenant-b ×1
  - GET /api/storefront/resolve-slug/lt-probe-tenant-c ×1
  - GET /api/storefront/search/suggest?q=lt-probe ×3
  - GET /api/storefront/search?q=LT-PROBE-A ×2
  - GET /api/storefront/search?q=LT-PROBE-B ×2
  - GET /api/storefront/search?q=LT-PROBE-C ×2
  - GET /api/units ×3
  - POST /api/admin/sync/categories ×3
  - POST /api/admin/sync/product-variants ×3
  - POST /api/admin/sync/search/full ×3
  - POST /api/auth/login ×3
  - POST /api/products/bulk/publish ×11
  - POST /api/products/bulk/unpublish ×6
  - POST /api/units/seed-defaults ×3
  - PUT /api/products/29da9226-2f7f-5765-b01c-bbdca5dcb6b1/units/ea616887-6c32-4bb6-a342-763aafb451f8 ×1
  - PUT /api/products/70dce816-a111-5295-ae15-c532496e40d1/units/45f95486-7281-4e37-979f-a3af8485fc47 ×1
  - PUT /api/products/ac15c5b7-cc6a-5ad4-96bb-13344d1b99f1/units/37e37e66-bfee-4ad2-8e15-1e1eea53f6c6 ×1
  - PUT /api/products/b44bc350-3a28-528a-8702-30fe0f9c182f/units/ea616887-6c32-4bb6-a342-763aafb451f8 ×1
  - PUT /api/products/f037e7f6-bbca-5948-acbe-eba9b408b548/units/37e37e66-bfee-4ad2-8e15-1e1eea53f6c6 ×1

## 18. Safety proof

- `orchestrate.m3b_preflight()` passed (env=local, approved DB/host, seed password present, no prod markers, providers off, TestAuth forbidden, denylist enforced). No bypass flag exists.
- Owner credential rotation: loadtest-owner-a, loadtest-owner-b, loadtest-owner-c — PasswordHash field only, deterministic-Id scoped, user-authorized; no other rows.
- Tenant Owner JWT via real login; per-tenant JWTs; sequential tenants.
- Routes invoked are allowlist-only; transports recorded in §17.

## 19. Failures/blockers

- None.

## 20. Final verdict

**PASS**

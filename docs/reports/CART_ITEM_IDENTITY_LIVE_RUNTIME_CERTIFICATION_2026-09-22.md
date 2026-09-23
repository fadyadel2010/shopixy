# Cart Item Identity Live Runtime Certification - 2026-09-22

## 1. Environment Identity

This verification was confined to the MAIN/PRIMARY Shopixy development copy at
`C:\Users\fadya\source\repos\PointOfSale`, connected to the DELL development
services. No Kubernetes, load-test server, M4.5, k6, load manifest, threshold,
VUs, seed, or workload was accessed or changed.

The running API process was
`PointOfSale.API\bin\Debug\net9.0\PointOfSale.API.exe` (PID 14804), launched by
`dotnet run`. Its binary timestamp was later than the remediated `CartService.cs`
timestamp, so the running process contained the current cart implementation.

Runtime dependency identity:

| Service | Endpoint | Runtime evidence |
|---|---|---|
| PostgreSQL | `192.168.1.5:5432`, database `PosEcommerceDb_Baseline` | API had established TCP connections; `/health` reported `Healthy` and reachable |
| Redis | `192.168.1.5:6379` | TCP reachable and direct read-only RESP `PING` returned `PONG`; API health reported it disabled by configuration |
| MongoDB | `192.168.109.128:27017`, database `ecommerce_read_db` | API had established TCP connections; `/health` reported `Healthy`, `ok=1` |

Health evidence:

- `GET http://localhost:5010/health/live` returned HTTP `200`, status `Healthy`.
- `GET http://localhost:5010/health` returned HTTP `503`, status `Unhealthy`.
- The full health response identified `redis` as `Unhealthy` with description
  `Redis is disabled by configuration` and `redis_latency` as `Degraded` for the
  same reason.
- PostgreSQL and MongoDB were healthy. The full readiness endpoint also reported
  unrelated payout reconciliation and CommerceHub search-sync failures.

No dependency was restarted. No application or configuration value was changed
to make the manual verification pass.

## 2. Backend URL

- Direct API: `http://localhost:5010`
- Storefront-proxied API used for the cart trace:
  `http://shopixy.localhost:5174/api/storefront`

## 3. Frontend URL

- MAIN Shopixy storefront: `http://shopixy.localhost:5174`
- Vite listener: `localhost:5174`
- Vite proxy target: `http://localhost:5010`

Both the frontend document and public product APIs returned HTTP `200`.

## 4. Tenant/User

Read-only PostgreSQL authority identified the target tenant and an existing safe
development user:

| Field | Value |
|---|---|
| Tenant name/slug | `shopixy` |
| TenantId | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Tenant active | `true` |
| Existing test UserId | `00000000-0000-0000-0000-000000000901` |
| Existing test username | `test-reliability-agent-01` |
| Existing test role/state | `Admin`, active |

The provided POS owner credential authenticated successfully with HTTP `200`, but
its server-issued tenant was `c9c5c1ae-949e-498e-bc63-07242daeb49d`, not Shopixy.
Supplying that tenant ID on the Shopixy host returned HTTP `403` with
`TENANT_FORBIDDEN`, so that identity was not used to access the target tenant.
Credentials and issued session tokens are intentionally omitted. No credential
was available for the existing Shopixy test user.

The cart lifecycle used the normal guest storefront owner contract to avoid an
unrelated customer's cart:

| Field | Value |
|---|---|
| Owner type | `session` |
| Lifecycle owner/session ID | `dce4108f-82b8-4a76-9365-e8435374ea71` |
| Frontend browser session ID | `5401716b-a8e0-458c-b90e-0fcc1ddd9913` |
| Existing CartId | None could be read or established because cart GET/POST returned `503` |

## 5. Simple Product Identity

The normal tenant-scoped product detail API returned HTTP `200` for this existing
simple product:

| Field | Value |
|---|---|
| Product name | `sss` |
| ProductId | `f3deaf29-b096-5fb8-7ef6-9738f50b79e6` |
| Slug | `hp-pavilion-15-laptop` |
| Tenant | `shopixy` / `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| HasVariants | `false` |
| Product availability | `isAvailable=true`, `canPurchase=true`, `canAddToCart=true` |
| Current price | `100 EGP` |
| Available stock | `10`; maximum quantity `10` |
| Active/default variant | `isActive=true`, `isDefault=true` |

The detail response is produced after the storefront SQL-authority overlay; no
client-supplied price, stock, or identity was treated as authority.

## 6. Canonical Variant Identity

The authoritative detail response returned:

`defaultVariantId = a00531ac-6c64-4f38-8bc6-eb44ebf8fc24`

The sole/default variant had the same ID and SKU `swswsws`. This establishes the
canonical simple-product identity independently of cart persistence.

## 7. POST Evidence

Request:

```http
POST http://shopixy.localhost:5174/api/storefront/cart/items
X-Tenant-Id: shopixy
X-Session-Id: dce4108f-82b8-4a76-9365-e8435374ea71
Content-Type: application/json

{"productId":"f3deaf29-b096-5fb8-7ef6-9738f50b79e6","quantity":1}
```

Result:

| Field | Value |
|---|---|
| HTTP status | `503` |
| Error code | `SERVICE_UNAVAILABLE` |
| ProductId | Submitted as `f3deaf29-b096-5fb8-7ef6-9738f50b79e6` |
| VariantId | Correctly omitted, but no cart response was produced |
| CartId | Not established |
| Quantity | Submitted as `1`; not persisted |
| Tenant | `shopixy` / `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Trace ID | `00-6b7c46b16974e28b49016979c08fc0c9-6f51b6ba61a47d05-01` |

The live assertion that the response contains a non-null canonical `VariantId`
is blocked because the running API refuses cart access while Redis is disabled.

## 8. GET Evidence

`GET http://shopixy.localhost:5174/api/storefront/cart` returned HTTP `503`, code
`SERVICE_UNAVAILABLE`, trace ID
`00-2180b23350cd4ccd641d4bfedca48b6d-dd512c02a31414de-01`.

No `CartId`, product line, `VariantId`, or quantity was returned. Identity and
CartId stability therefore could not be asserted live.

## 9. PUT Evidence

Request:

```http
PUT http://shopixy.localhost:5174/api/storefront/cart/items/variant/a00531ac-6c64-4f38-8bc6-eb44ebf8fc24
Content-Type: application/json

{"quantity":2}
```

Result: HTTP `503`, code `SERVICE_UNAVAILABLE`, trace ID
`00-adb8be19118dc9e9b2cb49fbb7840bb2-913bc039eb0a6299-01`.

The route used the exact server-authoritative canonical variant ID, but no cart
was available to update. ProductId, CartId, and returned quantity were absent.

## 10. GET After Update

The second `GET /api/storefront/cart` returned HTTP `503`, code
`SERVICE_UNAVAILABLE`, trace ID
`00-7a405aab3997d23b9ab912de4d7592e0-7fa2f515652b4dfe-01`.

No persisted update could be observed.

## 11. DELETE Evidence

`DELETE /api/storefront/cart/items/variant/a00531ac-6c64-4f38-8bc6-eb44ebf8fc24`
returned HTTP `503`, code `SERVICE_UNAVAILABLE`, trace ID
`00-e90af9f012c093fb288d41b3907a5ee1-092f5e3957d98b20-01`.

The request used the canonical variant ID. No CartId or response cart was
available.

## 12. Final GET

The final `GET /api/storefront/cart` returned HTTP `503`, code
`SERVICE_UNAVAILABLE`, trace ID
`00-a981018c1f01123a5656a097865d4f3a-ec3e46297619550c-01`.

The item-absence and post-delete CartId assertions could not be evaluated.

## 13. Correct Variant Validation

A simple-product add with both ProductId and the exact canonical VariantId was
submitted. It returned HTTP `503 SERVICE_UNAVAILABLE` before a cart response was
created. Repeated-add merge behavior and subsequent GET/PUT/DELETE behavior were
not live-verifiable.

The focused identity suite covers this contract and passed as reported in
section 21.

## 14. Wrong Variant Rejection

The current-tenant simple ProductId was submitted with an unrelated, valid
current-tenant variant ID
`dea366bd-f8b8-4e11-9af2-f931031bc704`. The request returned HTTP `503
SERVICE_UNAVAILABLE`, not the normal mismatch error, because cart persistence is
disabled in the running environment.

Read-only Redis inspection confirmed that no cart key was created for the
isolated verification owner. The focused identity suite covers random/unrelated
variant rejection and passed.

## 15. Cross-Tenant Rejection

A safe existing foreign fixture was identified through read-only PostgreSQL
authority:

| Field | Value |
|---|---|
| Foreign TenantId | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` |
| Foreign ProductId | `644425a7-b660-9a94-cb66-429e5dbdfb52` |
| Foreign VariantId | `6281ec68-43a1-472b-bf51-8cd4786918bc` |

The Shopixy simple ProductId was submitted with that foreign VariantId. The
request returned HTTP `503 SERVICE_UNAVAILABLE`, trace ID
`00-1f9d1713f9e3665f3e22d885bcafba0b-b4bc61465f503589-01`. Cart mutation locking
requires Redis before line admission, so the request did not reach the expected
association rejection. The correct Shopixy Redis key remained absent and no
foreign line was persisted or exposed. Live cross-tenant rejection is therefore
not certified; the focused cross-tenant mismatch test passed.

## 16. Variant Product Control

An existing selectable-variant product was identified through the normal product
detail API:

| Field | Value |
|---|---|
| Product | `Samsung Galaxy S26 Ultra` |
| ProductId | `606a46bc-24b5-76af-834d-34933906d5f2` |
| HasVariants | `true` |
| Selected VariantId | `dea366bd-f8b8-4e11-9af2-f931031bc704` |
| Variant state | active, default, purchasable |
| Available stock | `10`; maximum quantity `10` |
| Current effective price | `693 EGP` |

The valid variant-product POST returned HTTP `503 SERVICE_UNAVAILABLE`. The
POST/GET/PUT/GET/DELETE/GET control lifecycle could not proceed. Existing
variant behavior remains covered by the passing focused identity suite.

## 17. Legacy Null-ID Migration

Not performed. There was no safe existing legacy cart fixture, and the running
API could not read carts while Redis was disabled. Redis was not manually changed.
The focused identity suite covers GET-driven null-ID upgrade, stable CartId, and
no duplicate line.

## 18. Redis Evidence

Read-only Redis observation used the DELL endpoint directly:

| Check | Result |
|---|---|
| `PING` | `PONG` |
| Verification key | `pos:tenant:83b39fe2c80d45a08c0d5ce6c1f33ae5:cart:session:dce4108f-82b8-4a76-9365-e8435374ea71` |
| `EXISTS` | `0` |
| `GET` | null |

This proves the Redis server is reachable and that rejected/blocked attempts did
not persist a partial cart. It cannot prove a new simple line has non-null
`VariantId`, because the running API's `Redis:Enabled=false` gate prevents the
write and read path.

## 19. Frontend Evidence

A real headless Microsoft Edge session loaded
`http://shopixy.localhost:5174/products/hp-pavilion-15-laptop`. The page rendered
the authoritative simple product as in stock with an enabled `Add to Cart`
button.

Clicking that actual button issued:

```text
POST /api/storefront/cart/items?lang=en -> 503
```

The browser captured response code `SERVICE_UNAVAILABLE` and displayed `Service
temporarily unavailable`. The frontend-generated owner was
`storefront_session_id:shopixy = 5401716b-a8e0-458c-b90e-0fcc1ddd9913`.

Because add did not produce an item, the cart page could not expose increase,
decrease, or remove controls. No `CART_ITEM_NOT_FOUND` was observed, but absence of
that signature is not a certification: the workflow stopped earlier at Redis
availability.

## 20. Stock/Purchasability

The normal product API identified an existing unavailable simple-product case:

| Field | Value |
|---|---|
| Product | `realme GT Neo 3` |
| ProductId | `cb81e893-e41f-fc67-dd39-d7c9f4afaa33` |
| Canonical VariantId | `7a620725-bc66-4ba1-ac3b-e33867edc0b5` |
| Stock | `0` |
| Product authority | `canPurchase=false`, `OUT_OF_STOCK` |

Its add request returned HTTP `503 SERVICE_UNAVAILABLE` because the cart service
could not access its persistence authority. Runtime stock rejection was not
reached. No stock, active/deleted flag, ownership, or tenant data was modified.
Focused contract tests cover inactive, deleted, unpriced, unpurchasable, and
insufficient-stock cases.

## 21. Regression Tests

All requested focused regressions passed without modifying tests:

| Command scope | Result |
|---|---|
| `CartItemIdentityContractTests` | 10 passed, 0 failed, 0 skipped |
| `cartStore.persistence.test.ts` | 6 passed, 0 failed |
| `CheckoutCartIdempotencySnapshotBuilderTests` and `CheckoutControllerIdempotencyHeaderTests` | 8 passed, 0 failed, 0 skipped |
| `cartResponse.test.ts` and `checkoutStore.validation.test.ts` | 15 passed, 0 failed |

Vitest emitted only existing Vite plugin deprecation/configuration warnings.

## 22. Failure Classification

Primary classification: **environment configuration**.

Evidence chain:

1. The API and frontend listeners are running and product reads succeed.
2. PostgreSQL and MongoDB are healthy.
3. The Redis endpoint is reachable and returns `PONG`.
4. The active MAIN configuration sets `Redis:Enabled=false`.
5. `/health` reports Redis disabled by configuration.
6. Every cart method returns the cart controller's `503 SERVICE_UNAVAILABLE`
   contract before a cart can be established.
7. Focused backend and frontend identity/persistence regressions are green.

This is not classified as an application-contract regression. It is also not an
authentication, authorization, tenant-isolation, PostgreSQL, MongoDB, frontend
routing, network, Kubernetes, or load-test failure. A live contract pass remains
unproven until the MAIN runtime is intentionally operated with its required Redis
cart persistence enabled.

## 23. Commands Executed

Sensitive login material and issued tokens are omitted. Representative commands:

```powershell
git status --short --branch
Get-NetTCPConnection -State Listen
Get-CimInstance Win32_Process

curl.exe -i http://localhost:5010/health/live
curl.exe -i http://localhost:5010/health
curl.exe -i http://shopixy.localhost:5174/api/storefront/products/discover?page=1&pageSize=5
curl.exe -i http://shopixy.localhost:5174/api/storefront/products/hp-pavilion-15-laptop
curl.exe -i http://shopixy.localhost:5174/api/storefront/products/samsung-galaxy-s26-ultra
curl.exe -i http://shopixy.localhost:5174/api/storefront/products/realme-gt-neo-3

curl.exe -X POST http://shopixy.localhost:5174/api/storefront/cart/items ...
curl.exe -X GET http://shopixy.localhost:5174/api/storefront/cart ...
curl.exe -X PUT http://shopixy.localhost:5174/api/storefront/cart/items/variant/{VariantId} ...
curl.exe -X DELETE http://shopixy.localhost:5174/api/storefront/cart/items/variant/{VariantId} ...
```

Read-only Redis RESP commands `PING`, `EXISTS`, and `GET` were sent through a
short-lived TCP client. Microsoft Edge was launched headlessly against the MAIN
frontend and controlled through its local DevTools protocol for the real button
click. A short-lived .NET/Npgsql helper outside the workspace executed read-only
tenant, user, and foreign-variant `SELECT` queries against the documented MAIN
PostgreSQL endpoint.

Regression commands:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --filter "FullyQualifiedName~CartItemIdentityContractTests" --no-restore --logger "console;verbosity=minimal"

dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --filter "FullyQualifiedName~CheckoutCartIdempotencySnapshotBuilderTests|FullyQualifiedName~CheckoutControllerIdempotencyHeaderTests" --no-restore --logger "console;verbosity=minimal"

npx vitest run src/store/cartStore.persistence.test.ts --pool=threads --maxWorkers=1

npx vitest run src/services/api/cartResponse.test.ts src/store/checkoutStore.validation.test.ts --pool=threads --maxWorkers=1
```

## 24. Exact Runtime Results

Requested primary sequence on the unchanged MAIN runtime:

| Step | Method and URL | Status | ProductId | VariantId | CartId | Tenant | Relevant response |
|---|---|---:|---|---|---|---|---|
| Add | `POST /api/storefront/cart/items` | `503` | `f3deaf29-b096-5fb8-7ef6-9738f50b79e6` | omitted as required | unavailable | `shopixy` | `SERVICE_UNAVAILABLE` |
| Get | `GET /api/storefront/cart` | `503` | unavailable | unavailable | unavailable | `shopixy` | `SERVICE_UNAVAILABLE` |
| Update | `PUT /api/storefront/cart/items/variant/a00531ac-6c64-4f38-8bc6-eb44ebf8fc24` | `503` | unavailable | canonical route ID | unavailable | `shopixy` | `SERVICE_UNAVAILABLE` |
| Get after update | `GET /api/storefront/cart` | `503` | unavailable | unavailable | unavailable | `shopixy` | `SERVICE_UNAVAILABLE` |
| Delete | `DELETE /api/storefront/cart/items/variant/a00531ac-6c64-4f38-8bc6-eb44ebf8fc24` | `503` | unavailable | canonical route ID | unavailable | `shopixy` | `SERVICE_UNAVAILABLE` |
| Final get | `GET /api/storefront/cart` | `503` | unavailable | unavailable | unavailable | `shopixy` | `SERVICE_UNAVAILABLE` |

The original failure signature `POST 200 -> GET 200 -> PUT 404 -> DELETE 404`
was not reproduced. The required remediated success signature also could not be
reached. The observed signature was:

`POST 503 -> GET 503 -> PUT 503 -> GET 503 -> DELETE 503 -> GET 503`.

## 25. Remaining Limitations

- Enable Redis intentionally for the MAIN DELL runtime through the environment's
  approved configuration process, then rerun the exact lifecycle without code or
  test changes.
- Capture the resulting Redis line and prove its `variantId` is the canonical
  non-null value.
- Rerun correct/wrong/cross-tenant variant submissions after persistence is
  available.
- Complete selectable-variant control, legacy migration if a safe fixture exists,
  stock rejection, and frontend increase/decrease/remove.
- A target-tenant storefront/POS credential was not available; the isolated
  normal guest owner path was used instead. The supplied authenticated POS user
  belonged to another tenant and was not reused across the boundary.
- No safe live legacy-null cart was available.

## 26. Final Certification

**CONDITIONAL PASS**

The remediated canonical identity contract is supported by current authoritative
product data and 39 passing focused backend/frontend regressions. No remaining
application-contract defect was reproduced. Full live certification is
conditionally withheld because the actual unchanged MAIN runtime has Redis
explicitly disabled, causing all cart methods and the real frontend add action to
return `503 SERVICE_UNAVAILABLE` before CartId or line identity can exist.

## 27. Post-Restart Redis Preflight

A second MAIN-only certification attempt was started after the API was reported
as restarted with Redis enabled. The listener had changed from the earlier PID
`14804` to a new `PointOfSale.API.exe` process, PID `18968`, launched by
`dotnet run` and listening on `http://localhost:5010` in Development.

The required preflight produced:

| Check | HTTP/status | Relevant evidence |
|---|---|---|
| `GET /health/live` | `200 Healthy` | New process is live |
| `GET /health` | `503 Unhealthy` | Aggregate remains unhealthy for multiple entries |
| `redis` | `Unhealthy` | `Redis is disabled by configuration.`, `enabled=false` |
| `redis_latency` | `Degraded` | `Redis is disabled by configuration.`, `Enabled=false` |
| `postgresql` | `Healthy` | `192.168.1.5:5432`, database `PosEcommerceDb_Baseline` |
| `mongodb` | `Healthy` | Database `ecommerce_read_db`, `ok=1` |

The post-restart effective configuration investigation found:

```text
Development user-secret Redis:Enabled = false
Development user-secret Redis:ConnectionString = 192.168.1.5:6379
appsettings.Development.local.json Redis:Enabled = false
appsettings.Development.local.json Redis:ConnectionString = 192.168.1.5:6379
running-process Redis__* environment entries = none
```

Development user-secrets are appended after the local JSON provider, so the
actual effective source remains the Development user-secret with
`Redis:Enabled=false`. The endpoint is still `192.168.1.5:6379`. The new process
has two established connections to that endpoint, but, as documented in the
Redis root-cause report, SignalR connects whenever the connection string is
nonblank and does not prove that the cart Redis factory is enabled.

Per the requested stop condition, no cart API mutation, frontend cart action,
fixture mutation, regression test, Kubernetes command, load test, M4.5 command,
or k6 command was run in this second attempt. No application code, configuration,
test, or load-test asset was changed.

## 28. Post-Restart Attempt Status

**STOPPED - EFFECTIVE REDIS CONFIGURATION IS STILL DISABLED**

This is still an environment-configuration gate, not evidence of a remaining
application defect. The earlier **CONDITIONAL PASS** remains the latest cart
identity verdict; no new lifecycle verdict can be issued from this stopped
attempt.

To resume the requested certification, the effective Development user-secret
must contain `Redis:Enabled=true` and the MAIN API must then be restarted. The
gitignored Development local JSON should also be aligned to `true` so removal of
the user-secret cannot silently restore the stale disabled value. After that
restart, the individual `redis` health entry must report `enabled=true` before
the live cart lifecycle proceeds.

## 29. Third Attempt: Effective Redis Remediation

The effective Windows .NET user-secrets store was inspected again. It still held
`Redis:Enabled=false`; the gitignored Development local JSON fallback also held
`false`. Both configuration values were aligned to `true`, without changing
application code:

```text
Development user-secret Redis:Enabled = true
Development user-secret Redis:ConnectionString = 192.168.1.5:6379
appsettings.Development.local.json Redis:Enabled = true
appsettings.Development.local.json Redis:ConnectionString = 192.168.1.5:6379
```

Only the MAIN API was restarted. The certified process was
`PointOfSale.API.exe`, PID `11276`, launched by `dotnet run` in Development at
`2026-09-22T16:18:46.8214990+03:00`. Its process environment contained
`ASPNETCORE_ENVIRONMENT=Development`,
`ASPNETCORE_URLS=http://localhost:5010`, and no `Redis__*` override.

This certification attempt changed no `CartService`, controller, frontend, test,
Kubernetes, load-test, M4.5, k6, or `tests/load/*` source.

## 30. Healthy Redis Runtime Preflight

The required post-restart health preflight passed the dependency gate:

| Check | HTTP/status | Relevant evidence |
|---|---|---|
| `GET http://localhost:5010/health/live` | `200 Healthy` | MAIN process live |
| `GET http://localhost:5010/health` | `503 Unhealthy` | Aggregate affected by unrelated readiness entries |
| `redis` | `Healthy` | `Redis is reachable.`, `enabled=true`, `connected=true` |
| `redis` final latency | `13.4631 ms` | Individual dependency health remained healthy |
| `redis_latency` | `Healthy` | Final post-certification latency `37 ms`; initial/intermediate observations were connected but Degraded at `58/50 ms` |
| `postgresql` | `Healthy` | `192.168.1.5:5432`, database `PosEcommerceDb_Baseline` |
| `mongodb` | `Healthy` | Database `ecommerce_read_db`, `ok=1` |

The aggregate `503` did not come from Redis, PostgreSQL, or MongoDB and did not
block cart correctness verification. Redis was enabled, connected, and serving
cart persistence at the required endpoint `192.168.1.5:6379`.

## 31. Current Authoritative Fixtures

The normal Shopixy product-detail contract was read again immediately before the
live lifecycle.

Simple product:

| Field | Value |
|---|---|
| Tenant | `shopixy` / `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Product | `sss` |
| ProductId | `f3deaf29-b096-5fb8-7ef6-9738f50b79e6` |
| HasVariants | `false` |
| Canonical/default VariantId | `a00531ac-6c64-4f38-8bc6-eb44ebf8fc24` |
| Availability | `isAvailable=true`, `canPurchase=true`, `canAddToCart=true` |
| Product state | Publicly visible through the SQL-authority overlay; not deleted |
| Variant state | `isActive=true`, `isDefault=true` |
| Stock | `10`, maximum quantity `10` |
| Price | `100 EGP` |

Selectable-variant control:

| Field | Value |
|---|---|
| Product | `Samsung Galaxy S26 Ultra` |
| ProductId | `606a46bc-24b5-76af-834d-34933906d5f2` |
| HasVariants | `true` |
| Selected non-default VariantId | `4af7fe7b-3162-492e-969e-8eb725dc94ba` |
| SKU | `SAMSUNG-GALAXY-S26-ULTRA-BLACK-512` |
| Attributes | `Capacity=512`, `Color=black` |
| Availability | active, purchasable, stock `10` |

Negative fixtures remained the same authoritative fixtures recorded in the
earlier run: current-tenant unrelated VariantId
`dea366bd-f8b8-4e11-9af2-f931031bc704`, foreign-tenant VariantId
`6281ec68-43a1-472b-bf51-8cd4786918bc`, and zero-stock simple ProductId
`cb81e893-e41f-fc67-dd39-d7c9f4afaa33` with canonical VariantId
`7a620725-bc66-4ba1-ac3b-e33867edc0b5`.

## 32. Complete Simple-Product Lifecycle

The primary lifecycle used a fresh guest owner so it could not touch another
customer or session cart:

| Field | Value |
|---|---|
| Tenant | `shopixy` / `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Session/owner ID | `a1f5a3ae-82bb-4bd8-8020-e429803d2c6a` |
| CartId | `7cdc7d4a-52f5-4b02-bad2-083f7c85d9a4` |
| ProductId | `f3deaf29-b096-5fb8-7ef6-9738f50b79e6` |
| Canonical VariantId | `a00531ac-6c64-4f38-8bc6-eb44ebf8fc24` |

Exact result:

| Step | Request | HTTP | CartId | ProductId | VariantId | Quantity/items |
|---|---|---:|---|---|---|---|
| Add | `POST /api/storefront/cart/items`, ProductId only | `200` | `7cdc7d4a-52f5-4b02-bad2-083f7c85d9a4` | `f3deaf29-b096-5fb8-7ef6-9738f50b79e6` | `a00531ac-6c64-4f38-8bc6-eb44ebf8fc24` | `1` |
| Read | `GET /api/storefront/cart` | `200` | same | same | same non-null ID | `1` |
| Update | `PUT /api/storefront/cart/items/variant/a00531ac-6c64-4f38-8bc6-eb44ebf8fc24` | `200` | same | same | same | `2` |
| Read after update | `GET /api/storefront/cart` | `200` | same | same | same | `2` |
| Remove | `DELETE /api/storefront/cart/items/variant/a00531ac-6c64-4f38-8bc6-eb44ebf8fc24` | `200` | same | absent | absent | `0` items |
| Final read | `GET /api/storefront/cart` | `200` | same | absent | absent | `0` items |

The POST omitted `VariantId` exactly as required. The server resolved and
persisted the canonical non-null ID. ProductId, VariantId, tenant, owner, and
CartId remained identical through POST, GET, PUT, and the read after PUT. The
final GET proved the line was absent after DELETE.

The corrected real-runtime signature is:

`POST 200 -> GET 200 -> PUT 200 -> GET 200 -> DELETE 200 -> GET 200`.

The original failure signature
`POST 200 -> GET 200 -> PUT 404 -> DELETE 404` was not reproduced.

## 33. Supplied-ID And Security Controls

### A. Correct Supplied Canonical VariantId

Session `c3744f2f-68f9-4075-84b6-75697d83c5e5` submitted the simple ProductId
with canonical VariantId `a00531ac-6c64-4f38-8bc6-eb44ebf8fc24`.

| Check | Result |
|---|---|
| POST | `200` |
| CartId | `cfaf04d8-869c-46c5-a5a8-d632c02e9778` |
| Returned ProductId/VariantId | Exact submitted canonical pair |
| GET | `200`, same CartId and identity, quantity `1` |
| Cleanup DELETE | `200` |

### B. Wrong Unrelated VariantId

Session `a09b00d8-492d-4b3e-854a-f477d1a1bb59` submitted the simple ProductId
with unrelated current-tenant VariantId
`dea366bd-f8b8-4e11-9af2-f931031bc704`.

POST returned `404 VARIANT_NOT_FOUND`, trace ID
`00-72bfd27bcf02f420a905ce3875b71551-1c5b942ba804e604-01`. A subsequent GET
returned `200` with zero items. The unrelated variant was not associated with or
exposed through the simple product cart.

### C. Cross-Tenant VariantId

Session `1f9994e1-4164-4bcb-b2ec-dc0e3e8c9e12` submitted the Shopixy simple
ProductId with VariantId `6281ec68-43a1-472b-bf51-8cd4786918bc`, owned by foreign
TenantId `c43aebf2-4cc4-406a-a2db-4df06d40bce5`.

POST returned `404 VARIANT_NOT_FOUND`, trace ID
`00-d02fe5cf330a9d7d1b02350bf251a743-40b3d2a43872b55a-01`. A subsequent GET
returned `200` with zero items and the Shopixy tenant ID. No foreign identity or
line crossed the tenant boundary.

## 34. Selectable-Variant Control

The selectable-variant lifecycle used session
`fd48607b-5a74-41f2-a333-84838958c698`, CartId
`a671a223-3b2d-4fee-90ba-7f155654a289`, ProductId
`606a46bc-24b5-76af-834d-34933906d5f2`, and selected non-default VariantId
`4af7fe7b-3162-492e-969e-8eb725dc94ba`.

| Step | HTTP | Identity and quantity result |
|---|---:|---|
| POST selected variant | `200` | Exact ProductId/VariantId, quantity `1`, SKU and `512 / black` attributes |
| GET | `200` | Same CartId, identity, attributes, and quantity `1` |
| PUT by selected VariantId | `200` | Same identity, quantity `3` |
| GET after update | `200` | Same CartId and identity, quantity `3` |
| DELETE by selected VariantId | `200` | Zero items |
| Final GET | `200` | Same CartId, zero items |

This controls against a change that might fix only synthetic simple-product
identity while breaking normal selectable-variant behavior.

## 35. Stock Negative And Legacy Migration

Session `77465fbb-f4d9-4510-a02a-8718560fa0ed` submitted the existing zero-stock
simple ProductId `cb81e893-e41f-fc67-dd39-d7c9f4afaa33` without a VariantId.
POST returned `409 PRODUCT_OUT_OF_STOCK`, trace ID
`00-3e9e4c41ac593becaf5dc126e7f8a0b0-c41bb3f0b0472797-01`. A subsequent GET
returned `200` with zero items. No unavailable line was admitted.

Legacy null-ID migration was not executed. No safe pre-existing legacy guest
fixture was available, and manufacturing one by rewriting live Redis would not
be an application-contract-only test. Existing focused coverage still proves
GET-driven null-ID upgrade, stable CartId, and no duplicate line. This optional
scenario is not needed for the requested PASS gate, which requires the complete
simple lifecycle, security negatives, and selectable-variant control.

## 36. Direct Redis Persistence Evidence

A final isolated simple-product POST used session
`f5384198-217e-450f-9ba9-7b86bc6a0314` and returned HTTP `200`, CartId
`3185d6b0-18a6-46d0-bad1-d89994c4152f`.

Read-only RESP `GET` against `192.168.1.5:6379` inspected this exact key:

```text
pos:tenant:83b39fe2c80d45a08c0d5ce6c1f33ae5:cart:session:f5384198-217e-450f-9ba9-7b86bc6a0314
```

The persisted JSON contained:

| Field | Value |
|---|---|
| CartId | `3185d6b0-18a6-46d0-bad1-d89994c4152f` |
| TenantId | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| OwnerId | `f5384198-217e-450f-9ba9-7b86bc6a0314` |
| ProductId | `f3deaf29-b096-5fb8-7ef6-9738f50b79e6` |
| VariantId | `a00531ac-6c64-4f38-8bc6-eb44ebf8fc24` |
| Quantity | `1` |
| Available stock | `10` |

The API response and Redis JSON had the same CartId, ProductId, non-null
canonical VariantId, owner, tenant, and quantity. The isolated item was then
removed through the normal canonical-variant DELETE endpoint, which returned
`200` and item count `0`.

## 37. MAIN Frontend Certification

A real isolated headless Microsoft Edge session used
`http://shopixy.localhost:5174`. Local and session storage were cleared before
the run. The frontend generated guest session
`3ace755c-8502-4f3c-b094-84f1a9bcf23f`; the backend returned CartId
`5874dcae-da30-45f3-8de5-f8ce450202ee`.

| UI action | Network result | Visible result |
|---|---|---|
| Click product `Add to Cart` | `POST /api/storefront/cart/items?lang=en` -> `200`; payload contained ProductId and quantity only | Cart badge `1`; `sss added to cart!` |
| Open cart | `GET /api/storefront/cart?lang=en` -> `200` | `sss`, SKU `swswsws`, quantity `1`, `EGP 100.00` displayed |
| Click `Increase quantity` | `PUT /api/storefront/cart/items/variant/a00531ac-6c64-4f38-8bc6-eb44ebf8fc24?lang=en` -> `200` | Badge and row quantity `2`; total `EGP 200.00`; `Quantity updated` |
| Click `Remove from cart` | `DELETE` on the same canonical-variant route -> `200` | Product disappeared; `Your cart is empty`; `Item removed from cart` |

The GET response and mutation responses preserved tenant
`83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`, CartId
`5874dcae-da30-45f3-8de5-f8ce450202ee`, ProductId
`f3deaf29-b096-5fb8-7ef6-9738f50b79e6`, and VariantId
`a00531ac-6c64-4f38-8bc6-eb44ebf8fc24`. The PUT returned quantity `2`; DELETE
returned zero items.

## 38. Focused Regression Rerun

All existing focused tests were rerun unchanged after the live lifecycle:

| Scope | Result |
|---|---|
| `CartItemIdentityContractTests` | 10 passed, 0 failed, 0 skipped |
| `cartStore.persistence.test.ts` | 6 passed, 0 failed |
| `CheckoutCartIdempotencySnapshotBuilderTests` and `CheckoutControllerIdempotencyHeaderTests` | 8 passed, 0 failed, 0 skipped |
| `cartResponse.test.ts` and `checkoutStore.validation.test.ts` | 15 passed, 0 failed |

Total: **39 passed, 0 failed, 0 skipped**. Vitest emitted only the existing Vite
plugin deprecation/configuration warnings.

## 39. Superseding Final Certification

**PASS**

This verdict supersedes the earlier **CONDITIONAL PASS** and the stopped second
preflight while preserving both as historical evidence.

The complete MAIN DELL live simple-product lifecycle passed with stable CartId,
ProductId, tenant, owner, and non-null canonical VariantId. Correct supplied
identity passed. Unrelated and cross-tenant identities failed closed. The
selectable-variant control passed. The stock negative failed closed. Direct
Redis inspection proved canonical identity was persisted. The real MAIN frontend
completed add, display, quantity update, and removal through the canonical
variant routes. All 39 focused regressions passed unchanged.

No reproducible application defect remains in the requested cart-item identity
contract. No `CartService` change is warranted.

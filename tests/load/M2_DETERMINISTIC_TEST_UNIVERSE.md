# M2 Deterministic Test Universe — Shopixy Load Harness

> `NON-PRODUCTION TEST DATA`. Test-data infrastructure only.
> **NO PRODUCTION DATA. NO REAL PAYMENT. NO EXTERNAL PROVIDERS.**

## 1. Exact dataset

Three primary tenants (`tenant-a/b/c`), additive-only seeding into the
approved local test database. Existing data (e.g. `AhramStore`) is preserved
and untouched; it was **not** adopted as Tenant-A.

| Per tenant | Count | Total (×3) |
|---|---|---|
| Staff (owner/admin/cashier) | 3 | 9 |
| Customers (5 + 1 sentinel) | 6 | 18 |
| Categories (3 + 1 sentinel) | 4 | 12 |
| Products (19 + 1 sentinel probe) | 20 | 60 |
| Variants (19×1 default + 1 container×3) | 22 | 66 |
| Warehouses | 2 | 6 |
| Variant-stock rows | 23 | 69 |
| Probe products | 1 | 3 |

Product kinds per tenant: `sentinel` (001), `normal` (002–014, 020),
`low-stock` (015, qty 3), `contended` (016, qty 20),
`backorder` (017, `AllowBackorder`, qty 0), `untracked` (018,
`TrackStock=false`, no stock rows), `variant-container` (019: default variant
w1 500 + w2 100, two zero-stock variants). Plus one extra w2 row (250) on a
normal product for multi-warehouse coverage. Sentinel orders are
**intentionally not created** (would trigger order workflows); `probes.json`
records `sentinelOrderCreated: false`.

## 2. Naming conventions (all `loadtest-`/`TEST-PROBE-` namespaced)

- Tenants: slug `loadtest-tenant-{a,b,c}`, name `Loadtest Tenant X (TEST DATA)`
- Staff: `loadtest-{owner,admin,cashier}-{a,b,c}` (+ `@loadtest.local` email)
- Customers: `loadtest-customer-{k}-{001..005}`, sentinel `test-probe-customer-{k}`
- Categories: `LOADTEST-CAT-{K}-{001..003}`, sentinel `TEST-PROBE-CAT-{K}`
- Products: slug `lt-{k}-product-{001..020}`, SKU `LT{K}-P{001..020}`, barcode
  `LTBC{K}{000001..}`; sentinel `TEST-PROBE-TENANT-{K}` / `lt-probe-tenant-{k}` /
  `LT-PROBE-{K}` / price fingerprint 111.11 / 222.22 / 333.33
- Variants: `{SKU}-{D,V2,V3}`; warehouses: code `LT-{K}-W{1,2}` (w1 default)

## 3. Deterministic identity strategy

UUIDv5 (`seedNamespace b9c4d3a1-…`, `seedVersion m2.1`) over canonical names:
`tenant/{slug}`, `user/{slug}/{username}`, `customer/{slug}/{key}`,
`category/{slug}/{name}`, `product/{slug}/{pslug}`,
`variant/{slug}/{pslug}/{vkey}`, `warehouse/{slug}/{w}`,
`stock/{slug}/{variantId}/{warehouseId}`, `variantattr/…`. Same inputs →
same IDs across reruns and machines. Passwords use per-user random salts
(non-deterministic by design) in ASP.NET Core Identity V3 format
(PBKDF2-HMAC-SHA256, 16-byte salt, 32-byte subkey, 61-byte `0x01` layout),
which `VerifyHashedPassword` accepts — proven by the owner-login check.
No random GUIDs for domain entities.

## 4. Safety gate

`seed.py` fails closed unless: `TEST_ENVIRONMENT ∈ {local, test, ci,
staging-test}`; `TEST_DB_PROOF` contains the **actual** connected host and
database; actual DB name carries no production marker
(`prod/production/live/customer/shopixy.com`); connection resolves from the
gitignored local settings (env `local`) or `LOADTEST_CONNECTION_STRING`
(other envs). `--seed` additionally requires `LOADTEST_SEED_PASSWORD`
(≥12 chars, upper/lower/digit; never logged or stored). No
`--force-production` bypass exists. Uniqueness pre-checks
(global `Users` username/email, per-tenant customer email, SKU/barcode)
refuse with a conflict report instead of overwriting; the seeder never
deletes.

## 5. Database scope

Approved target for this phase: host `192.168.1.5`, database
`PosEcommerceDb_Baseline` (local Ubuntu infra, dev baseline). Tables written:
`Tenants, Users, OnlineCustomers, Categories, Products, ProductVariants,
ProductVariantAttributes, Warehouses, ProductVariantStocks` — test-namespaced
rows only. The single login-proof call also touches our own test user row
(`LastLoginAt`/refresh token). Nothing else is written; no schemas,
migrations, or business logic changed.

## 6. Rerun/idempotency semantics

Insert-if-missing by deterministic Id inside one transaction per `--seed`
(rollback on any failure, non-zero exit, exact stage reported).
Existing deterministic rows are **preserved**; loadtest-owned credential rows
with a malformed hash are **repaired** (hash field only); unrelated and
business data are never deleted, never overwritten. Proof: run1
`created=243`, run2 `created=0` (+27 hash repairs), run3
`created=0 preserved=243`.

## 7. Stock budgets (per tenant, recorded in manifest)

plentiful `10000.00` (w1) / `250.00` (w2 sample), contended `20.00`,
low-stock `3.00`, zero `0.00` (explicit rows), backorder `0.00` with
`AllowBackorder=true`, untracked: no rows, sentinel `100.00`. All
`ReservedQuantity=0`, `DamagedQuantity=0`. Later checkout tests must treat
these as expendable caps, never exceed them, and expect 409s on the
contended SKU by design.

## 8. Probe design

Per tenant: `TEST-PROBE-TENANT-{K}` product (GUID + SKU + slug + barcode +
price fingerprint + sentinel category + sentinel customer). Any response,
projection, event, or log containing another tenant's probe identifier is a
future P0 isolation failure. Multi-identifier detection (not names alone):
GUID, SKU, slug, barcode, price.

## 9. Verification queries/checks (read-only, post-transaction)

Per tenant: row counts (3/6/4/20/22/2/23), tenant active
(`IsActive ∧ Status=Active ∧ DeletedAtUtc IS NULL`), sentinel SKU/slug/
barcode/price equality, sentinel variant barcode, budget equality per
(variant, warehouse) with `Reserved=0`, and zero foreign-tenant references
(staff/customer/category/product/variant/warehouse `TenantId`, variant→product
and stock→variant/warehouse tenant agreement). Any failure → no `SEED PASS`.
Plus one `POST /api/auth/login` as tenant-a owner (no TestAuth): 200 +
`access_token` cookie + body `tenantId` binding.

## 10. Cleanup policy

No automatic cleanup exists by design. Test rows are namespaced
(`loadtest-%`, `LT%`, `TEST-PROBE-%`, `@loadtest.local`) for explicit,
reviewed manual removal only. Never delete business data.

## 11. k6 consumption (later, not this phase)

k6 scenarios read `tests/load/data/manifests/latest.json` (tenant IDs/slugs,
user/customer IDs, product/variant/warehouse IDs, budgets, probes) for
deterministic VU→tenant mapping, contended-SKU schedules within budget, and
I-1…I-9 probe assertions. Passwords stay in the environment, never in files.
Seeded SQL rows are **not** Mongo/ES-projected (creation events never fired):
catalog visibility via search/projections requires a projection rebuild or
event-driven seeding — an explicit M3 prerequisite, not assumed.

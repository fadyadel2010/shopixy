# M2 Seed Report — Deterministic Test Universe (2026-09-15 UTC)

> TEST-DATA INFRASTRUCTURE ONLY. No k6 load, no checkout, no providers.
> Final verdict: **PASS** (with limitations, see §11).

## 1. Environment

Windows local API `http://localhost:5010` (`Development`); infra on Ubuntu
`192.168.1.5` (Postgres/Mongo/Redis/RabbitMQ/ES per M1.2 health).
`TEST_ENVIRONMENT=local`. k6 was **not** run in this phase (per scope).

## 2. Database target

Host `192.168.1.5`, database `PosEcommerceDb_Baseline` — local dev baseline,
explicitly approved for M2. Proof used:
`TEST_DB_PROOF=PosEcommerceDb_Baseline@192.168.1.5-local-baseline` (contains
actual host + db name; gate-verified). No production host or DB involved.
AhramStore and all pre-existing business data preserved untouched.

## 3. Safety gate results — PASS

| Check | Result |
|---|---|
| `--verify` with no env | `VERIFY REFUSE` exit 1 |
| `--seed` with `TEST_ENVIRONMENT=production` | `SEED REFUSE` exit 1 |
| `--seed` with missing env | `SEED REFUSE` exit 1 |
| `--verify` with proof of a different DB/host | `VERIFY REFUSE` (actual host/db echoed, secrets never printed) exit 1 |
| `--seed` with full safe env + `LOADTEST_SEED_PASSWORD` | gate passed → seeded |

No bypass flag exists; no refusal was downgraded. Connection for `local`
resolved from gitignored `appsettings.Development.local.json` (never logged).

## 4. Files created/modified (all under `tests/load/`)

- Created: `python/seed.py` (PLAN/VERIFY/SEED, gate, Identity-V3 hashing,
  manifests), `data/universe.json`, `data/probes.json`,
  `data/manifests/run-*.json` (3 runs) + `latest.json`,
  `M2_DETERMINISTIC_TEST_UNIVERSE.md`, this report.
- `python/seed_plan.py` left untouched (plan-only contract preserved).
- No application/business/schema/migration files changed.

## 5. Exact seed command

```powershell
$env:TEST_ENVIRONMENT='local'
$env:TEST_DB_PROOF='PosEcommerceDb_Baseline@192.168.1.5-local-baseline'
$env:LOADTEST_SEED_PASSWORD='<strong-test-secret-12+-chars upper/lower/digit>'
python tests/load/python/seed.py --seed   # --plan / --verify for other modes
```

Password lives only in the process environment; never in files, logs, or manifests
(secret-scan of `tests/load/data/` clean).

## 6. First seed result

`created=243 preserved=0`, exit 3 (`SEED COMPLETE WITH FAILURES`): all counts
and ownership checks passed, but three defects surfaced — (a) stock-budget
check compared numerics as strings (`'0.00' != '0'`), (b) sentinel check
wrongly expected `NULL` barcode on a simple (non-container) probe, both
harness-check bugs, data correct; (c) my first Identity-V3 layout carried an
extra subkey-length field (65 vs 61 bytes) so logins 401'd. Transaction
itself was sound; no partial data, no business impact.

## 7. Second seed / idempotency result

After fixing (a)–(c) plus an idempotent hash-repair path for loadtest-owned
credential rows: `created=0 preserved=216` (+27 silent hash repairs — the
print has since been extended to show `repaired=N`), all 36 verification
lines OK except the login-proof shape (looked for a body token; this API
issues HttpOnly session cookies). Third run after correcting the proof:
`created=0 preserved=243 repaired=0`, `SEED PASS`, exit 0.
Idempotency proven across three runs; no destructive reset ever performed.

## 8. Entity counts — PASS

Per tenant: staff 3/3, customers 6/6, categories 4/4, products 20/20,
variants 22/22, warehouses 2/2, stocks 23/23. Totals: 9 / 18 / 12 / 60 /
66 / 6 / 69 + 3 probe products. Tenant active 3/3.

## 9. Ownership verification — PASS

`foreignTenantViolations=0` on all tenants (staff/customer/category/product/
variant/warehouse `TenantId`, variant→product and stock→variant/warehouse
agreement). Sentinel fingerprints (SKU/slug/barcode/price + variant barcode)
3/3. Stock budgets per (variant, warehouse) with `Reserved=0` 3/3.
Owner login proof (tenant-a, single `POST /api/auth/login`, no TestAuth):
HTTP 200 + `access_token` cookie + body `tenantId` bound to
`loadtest-tenant-a` — also proves the Python-generated Identity-V3 hashes
verify server-side, which future k6 auth pools depend on.

## 10. Conflicts encountered

None from existing data: deterministic `loadtest-`/`LT-`/`TEST-PROBE-`
namespaces did not collide with business rows (global `Users`
username/email uniques respected by construction). AhramStore left intact;
no reconciliation was needed. Pre-seed `--verify` correctly reported
everything missing (exit 2) with zero violations.

## 11. Limitations

1. Seeded SQL rows are **not** Mongo/ES-projected (no creation events fire
   on direct inserts): storefront search/discovery reads will not see the
   universe until a projection rebuild or event-driven seeding (M3
   prerequisite). Admin/POS SQL-backed reads work now.
2. No sentinel orders (deliberate — avoids order workflows); order-path
   probes arrive with transactional scenarios.
3. `ProductUnits` rows not seeded (checkout prices from variants; units
   deferred to M3 if needed). Per-tenant accounting seeding
   (`ITenantAccountSeeder`) not invoked — verify before finance-adjacent tests.
4. Login proof writes to our own test row (`LastLoginAt`/refresh token) —
   the only write outside the seed transaction, permitted as the verified
   validation step.
5. Results are local-validation only; not capacity, not isolation
   certification (isolation certified only by later multi-tenant concurrent
   scenarios with these sentinels).

## 12. Recommendation for M3

Consume `manifests/latest.json` in k6: deterministic VU→tenant pools with
real logins (password from env), contended-SKU schedule capped by manifest
budgets (expect 409s by design), I-1…I-9 sentinel assertions, per-request
correlation IDs; add projection-rebuild/event-seeding step first, plus
post-run outbox/stock spot-checks. Keep Windows-local as validation-only;
capacity stages stay gated on K8s/perf env with exporters.

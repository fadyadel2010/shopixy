"""M2 deterministic test-universe seeder — PLAN / VERIFY / SEED.

TEST-DATA INFRASTRUCTURE ONLY. No load tests, no checkout, no providers.

Modes:
  python seed.py --plan    Build the deterministic universe, print the plan,
                           write tests/load/data/universe.json + probes.json.
                           No database contact at all.
  python seed.py --verify  Read-only checks against the target DB: report
                           which deterministic entities exist / are missing /
                           violate ownership. No writes.
  python seed.py --seed    Full safety gate + transactional seed + post-seed
                           verification + manifest. The ONLY writing mode.

Safety gate (any mode that touches the DB; --seed additionally requires the
seed password):
  TEST_ENVIRONMENT in {local, test, ci, staging-test} (reject production,
  prod, unknown, missing), TEST_DB_PROOF must contain the ACTUAL connected
  host and database name, and the actual database name must not match
  production markers. No --force-production bypass exists by design.

Secrets: LOADTEST_SEED_PASSWORD supplies staff/customer passwords (never
logged, never written to any file). Connection: TEST_ENVIRONMENT=local reads
the gitignored PointOfSale.API/appsettings.Development.local.json; any other
environment requires LOADTEST_CONNECTION_STRING. Secrets are never printed.

Passwords are hashed in the ASP.NET Core Identity V3 format
(PBKDF2-HMAC-SHA256, 128-bit salt, 256-bit subkey) so VerifyHashedPassword
accepts them; correctness is proven by a single owner login during --seed.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import struct
import sys
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from uuid import UUID, uuid5

try:
    import psycopg
except ImportError:  # pragma: no cover
    psycopg = None  # type: ignore

# ----------------------------------------------------------------------------
# Constants (stable: same inputs -> same outputs)
# ----------------------------------------------------------------------------
SEED_VERSION = "m2.1"
SEED_NAMESPACE = UUID("b9c4d3a1-7e22-4f5a-9c1d-2a6f8e0b4d11")
MARKER = "NON-PRODUCTION TEST DATA"
ALLOWED_ENVIRONMENTS = ("local", "test", "ci", "staging-test")
PROD_MARKERS = ("prod", "production", "live", "customer", "shopixy.com")

TENANT_KEYS = ("a", "b", "c")
PRICE_FINGERPRINT = {"a": "111.11", "b": "222.22", "c": "333.33"}
STAFF_ROLES = (("owner", 0), ("admin", 1), ("cashier", 3))
CUSTOMER_COUNT = 5  # + 1 sentinel customer per tenant
CATEGORY_COUNT = 3  # + 1 sentinel category per tenant
PRODUCT_COUNT = 20

REPO_ROOT = Path(__file__).resolve().parents[3]
DATA_DIR = REPO_ROOT / "tests" / "load" / "data"
MANIFEST_DIR = DATA_DIR / "manifests"


def det(*parts: str) -> str:
    """Deterministic UUIDv5 from canonical '/'-joined name."""
    return str(uuid5(SEED_NAMESPACE, "/".join(parts)))


def identity_v3_hash(password: str, iterations: int = 100_000) -> str:
    """ASP.NET Core Identity V3 password hash.

    Layout (all uint32 big-endian): 0x01 | prf=1 (HMAC-SHA256) |
    iterCount | saltLen | salt(16) | subkey(32, PBKDF2-HMAC-SHA256).
    Total 61 bytes. VerifyHashedPassword reads the embedded iteration
    count, so any reasonable count verifies; rehash-on-login may trigger
    (harmless).
    """
    salt = os.urandom(16)
    subkey = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, iterations, 32)
    blob = b"\x01" + struct.pack(">III", 1, iterations, len(salt)) + salt + subkey
    return base64.b64encode(blob).decode("ascii")


def is_valid_v3_hash(stored: str | None) -> bool:
    """True when `stored` decodes to the 61-byte Identity V3 layout."""
    if not stored:
        return False
    try:
        raw = base64.b64decode(stored, validate=True)
    except Exception:
        return False
    return len(raw) == 61 and raw[0] == 0x01


# ----------------------------------------------------------------------------
# Universe definition (pure data, no I/O)
# ----------------------------------------------------------------------------
def tenant_def(key: str) -> dict:
    slug = f"loadtest-tenant-{key}"
    return {
        "key": key,
        "tenantId": det("tenant", slug),
        "slug": slug,
        "name": f"Loadtest Tenant {key.upper()} (TEST DATA)",
        "currency": "EGP",
    }


def staff_def(t: dict, role: str) -> dict:
    username = f"loadtest-{role}-{t['key']}"
    return {
        "userKey": username,
        "userId": det("user", t["slug"], username),
        "username": username,
        "email": f"{username}@loadtest.local",
        "firstName": "Loadtest",
        "lastName": f"{role.capitalize()} {t['key'].upper()}",
    }


def customer_def(t: dict, i: int, sentinel: bool = False) -> dict:
    if sentinel:
        ckey = f"test-probe-customer-{t['key']}"
    else:
        ckey = f"loadtest-customer-{t['key']}-{i:03d}"
    return {
        "customerKey": ckey,
        "customerId": det("customer", t["slug"], ckey),
        "email": f"{ckey}@loadtest.local",
        "displayName": f"Loadtest Customer {t['key'].upper()}-{i:03d}" if not sentinel else f"TEST PROBE CUSTOMER {t['key'].upper()}",
        "sentinel": sentinel,
    }


def category_def(t: dict, i: int, sentinel: bool = False) -> dict:
    if sentinel:
        name = f"TEST-PROBE-CAT-{t['key'].upper()}"
    else:
        name = f"LOADTEST-CAT-{t['key'].upper()}-{i:03d}"
    return {
        "name": name,
        "categoryId": det("category", t["slug"], name),
        "sentinel": sentinel,
    }


def product_kind(index: int) -> str:
    if index == 1:
        return "sentinel"
    if index == 15:
        return "low-stock"
    if index == 16:
        return "contended"
    if index == 17:
        return "backorder"
    if index == 18:
        return "untracked"
    if index == 19:
        return "variant-container"
    return "normal"


def product_def(t: dict, index: int, categories: list[dict]) -> dict:
    k = t["key"].upper()
    kind = product_kind(index)
    if kind == "sentinel":
        name = f"TEST-PROBE-TENANT-{k}"
        slug = f"lt-probe-tenant-{t['key']}"
        sku = f"LT-PROBE-{k}"
        barcode = f"LTBCPROBE{t['key'].upper()}001"
        price = PRICE_FINGERPRINT[t["key"]]
        cat = next(c for c in categories if c["sentinel"])
    else:
        name = f"Loadtest Product {k}-{index:03d} (TEST DATA)"
        slug = f"lt-{t['key']}-product-{index:03d}"
        sku = f"LT{k}-P{index:03d}"
        barcode = f"LTBC{k}{index:06d}"
        price = f"{100 + index}.00"
        normals = [c for c in categories if not c["sentinel"]]
        cat = normals[(index - 2) % len(normals)]
    return {
        "index": index,
        "kind": kind,
        "productKey": slug,
        "productId": det("product", t["slug"], slug),
        "name": name,
        "slug": slug,
        "sku": sku,
        "barcode": barcode,
        "price": price,
        "categoryId": cat["categoryId"],
        "categoryName": cat["name"],
        "trackStock": kind != "untracked",
        "allowBackorder": kind == "backorder",
        "hasVariants": kind == "variant-container",
    }


def variant_def(t: dict, p: dict, vkey: str, default: bool, sort: int) -> dict:
    sku = f"{p['sku']}-{vkey}"
    return {
        "variantKey": vkey,
        "variantId": det("variant", t["slug"], p["slug"], vkey),
        "sku": sku,
        "barcode": f"{p['barcode']}-{vkey}",
        "price": p["price"],
        "isDefault": default,
        "sortOrder": sort,
    }


def build_universe() -> dict:
    tenants = []
    for key in TENANT_KEYS:
        t = tenant_def(key)
        staff = [staff_def(t, role) for role, _ in STAFF_ROLES]
        customers = [customer_def(t, i) for i in range(1, CUSTOMER_COUNT + 1)]
        sentinel_customer = customer_def(t, 0, sentinel=True)
        categories = [category_def(t, i) for i in range(1, CATEGORY_COUNT + 1)]
        sentinel_category = category_def(t, 0, sentinel=True)
        all_cats = categories + [sentinel_category]
        products = []
        for index in range(1, PRODUCT_COUNT + 1):
            p = product_def(t, index, all_cats)
            if p["kind"] == "variant-container":
                p["variants"] = [
                    variant_def(t, p, "D", True, 0),
                    variant_def(t, p, "V2", False, 1),
                    variant_def(t, p, "V3", False, 2),
                ]
            else:
                p["variants"] = [variant_def(t, p, "D", True, 0)]
            products.append(p)
        warehouses = [
            {"warehouseKey": f"warehouse-{key}-1", "warehouseId": det("warehouse", t["slug"], "w1"),
             "name": f"Loadtest Warehouse {key.upper()}-1 (TEST DATA)", "code": f"LT-{key.upper()}-W1", "isDefault": True},
            {"warehouseKey": f"warehouse-{key}-2", "warehouseId": det("warehouse", t["slug"], "w2"),
             "name": f"Loadtest Warehouse {key.upper()}-2 (TEST DATA)", "code": f"LT-{key.upper()}-W2", "isDefault": False},
        ]
        budgets = stock_plan(t, products, warehouses)
        tenants.append({
            "marker": MARKER,
            "key": key, **t, "staff": staff,
            "customers": customers, "sentinelCustomer": sentinel_customer,
            "categories": categories, "sentinelCategory": sentinel_category,
            "products": products, "warehouses": warehouses,
            "stockBudgets": budgets,
        })
    return {"marker": MARKER, "seedVersion": SEED_VERSION,
            "seedNamespace": str(SEED_NAMESPACE), "tenants": tenants}


def stock_plan(t: dict, products: list[dict], warehouses: list[dict]) -> list[dict]:
    """Deterministic (variant, warehouse, quantity) rows. No zero-qty rows for
    untracked products; zero-stock cases get explicit 0 rows."""
    w1, w2 = warehouses[0]["warehouseId"], warehouses[1]["warehouseId"]
    rows = []
    for p in products:
        for v in p["variants"]:
            vid = v["variantId"]
            if p["kind"] == "sentinel":
                rows.append({"variantId": vid, "warehouseId": w1, "quantity": "100.00", "purpose": "sentinel"})
            elif p["kind"] == "low-stock" and v["isDefault"]:
                rows.append({"variantId": vid, "warehouseId": w1, "quantity": "3.00", "purpose": "low-stock"})
            elif p["kind"] == "contended" and v["isDefault"]:
                rows.append({"variantId": vid, "warehouseId": w1, "quantity": "20.00", "purpose": "contended"})
            elif p["kind"] == "backorder" and v["isDefault"]:
                rows.append({"variantId": vid, "warehouseId": w1, "quantity": "0.00", "purpose": "backorder-zero"})
            elif p["kind"] == "untracked":
                continue  # no stock rows: TrackStock=false
            elif p["kind"] == "variant-container":
                if v["variantKey"] == "D":
                    rows.append({"variantId": vid, "warehouseId": w1, "quantity": "500.00", "purpose": "container-default"})
                    rows.append({"variantId": vid, "warehouseId": w2, "quantity": "100.00", "purpose": "container-default-w2"})
                else:
                    rows.append({"variantId": vid, "warehouseId": w1, "quantity": "0.00", "purpose": "container-zero-variant"})
            elif v["isDefault"]:
                rows.append({"variantId": vid, "warehouseId": w1, "quantity": "10000.00", "purpose": "plentiful"})
    # A second-warehouse row for one normal product (multi-warehouse coverage).
    normal = next(p for p in products if p["kind"] == "normal")
    rows.append({"variantId": normal["variants"][0]["variantId"], "warehouseId": w2,
                 "quantity": "250.00", "purpose": "plentiful-w2"})
    return rows


def build_probes(universe: dict) -> dict:
    probes = []
    for t in universe["tenants"]:
        sentinel_product = next(p for p in t["products"] if p["kind"] == "sentinel")
        probes.append({
            "marker": MARKER,
            "tenantKey": f"tenant-{t['key']}",
            "tenantId": t["tenantId"],
            "slug": t["slug"],
            "probeProductId": sentinel_product["productId"],
            "probeName": sentinel_product["name"],
            "probeSlug": sentinel_product["slug"],
            "probeSku": sentinel_product["sku"],
            "probeBarcode": sentinel_product["barcode"],
            "probePrice": sentinel_product["price"],
            "probeCategoryId": t["sentinelCategory"]["categoryId"],
            "probeCategoryName": t["sentinelCategory"]["name"],
            "probeCustomerId": t["sentinelCustomer"]["customerId"],
            "probeCustomerEmail": t["sentinelCustomer"]["email"],
            "sentinelOrderCreated": False,
        })
    return {"marker": MARKER, "seedVersion": SEED_VERSION, "probes": probes}


# ----------------------------------------------------------------------------
# Safety gate + connection (no secrets in output)
# ----------------------------------------------------------------------------
class SafetyRefusal(Exception):
    pass


def safety_gate(require_password: bool = False) -> dict:
    env_name = (os.environ.get("TEST_ENVIRONMENT") or "").strip().lower()
    if env_name not in ALLOWED_ENVIRONMENTS:
        raise SafetyRefusal(
            f"TEST_ENVIRONMENT={env_name!r} not in allowlist {list(ALLOWED_ENVIRONMENTS)}")
    proof = (os.environ.get("TEST_DB_PROOF") or "").strip()
    if not proof:
        raise SafetyRefusal("missing TEST_DB_PROOF (explicit test-database target proof required)")

    conninfo = resolve_connection(env_name)  # never logged
    host = (conninfo["host"] or "").lower()
    dbname = conninfo["dbname"] or ""
    low = dbname.lower()
    if any(m in low for m in PROD_MARKERS):
        raise SafetyRefusal(f"refusing target database {dbname!r}: production marker")
    if any(m in host for m in ("shopixy.com",) if m):
        raise SafetyRefusal(f"refusing target host {host!r}: production marker")
    if dbname not in proof or host not in proof.lower():
        raise SafetyRefusal(
            f"TEST_DB_PROOF does not contain actual target host={host!r} db={dbname!r}")
    password = None
    if require_password:
        password = os.environ.get("LOADTEST_SEED_PASSWORD") or ""
        if len(password) < 12 or not any(c.isupper() for c in password) \
                or not any(c.islower() for c in password) \
                or not any(c.isdigit() for c in password):
            raise SafetyRefusal(
                "LOADTEST_SEED_PASSWORD missing or too weak "
                "(>=12 chars with upper/lower/digit required; value never logged)")
    return {"env": env_name, "host": host, "dbname": dbname,
            "conninfo": conninfo, "seedPassword": password}


def resolve_connection(env_name: str) -> dict:
    if env_name == "local":
        settings_path = REPO_ROOT / "PointOfSale.API" / "appsettings.Development.local.json"
        if not settings_path.exists():
            raise SafetyRefusal(
                "local settings file missing; cannot resolve test connection")
        settings = json.loads(settings_path.read_text(encoding="utf-8"))
        try:
            cs = settings["ConnectionStrings"]["DefaultConnection"]
        except KeyError:
            raise SafetyRefusal("DefaultConnection missing from local settings")
        kv = dict(p.split("=", 1) for p in cs.split(";") if "=" in p)
        return {"host": kv.get("Host"), "port": kv.get("Port", "5432"),
                "dbname": kv.get("Database"), "user": kv.get("Username"),
                "password": kv.get("Password"), "connect_timeout": 15}
    raw = os.environ.get("LOADTEST_CONNECTION_STRING") or ""
    if not raw:
        raise SafetyRefusal(
            f"LOADTEST_CONNECTION_STRING required for TEST_ENVIRONMENT={env_name}")
    kv = dict(p.split("=", 1) for p in raw.split(";") if "=" in p)
    return {"host": kv.get("Host") or kv.get("host"),
            "port": kv.get("Port", kv.get("port", "5432")),
            "dbname": kv.get("Database") or kv.get("dbname"),
            "user": kv.get("Username") or kv.get("user"),
            "password": kv.get("Password") or kv.get("password"),
            "connect_timeout": 15}


def connect(conninfo: dict):
    if psycopg is None:
        raise SafetyRefusal("psycopg is not installed (pip install psycopg)")
    return psycopg.connect(host=conninfo["host"], port=conninfo["port"],
                           dbname=conninfo["dbname"], user=conninfo["user"],
                           password=conninfo["password"],
                           connect_timeout=conninfo.get("connect_timeout", 15))


# ----------------------------------------------------------------------------
# Seeding (idempotent: insert-if-missing by deterministic Id; never delete)
# ----------------------------------------------------------------------------


def seed_tenant(cur, t: dict) -> str:
    cur.execute('SELECT "Id" FROM "Tenants" WHERE "Id"=%s', (t["tenantId"],))
    if cur.fetchone():
        return "preserved"
    cur.execute(
        'INSERT INTO "Tenants" ("Id","Name","IsActive","CreatedAtUtc","Slug",'
        '"Status","Currency","BootstrapVersion","UseBaileys") '
        "VALUES (%s,%s,TRUE,now(),%s,1,%s,1,FALSE)",
        (t["tenantId"], t["name"], t["slug"], t["currency"]))
    return "created"


def seed_staff(cur, t: dict, s: dict, role_int: int, pw_hash: str) -> str:
    cur.execute('SELECT "PasswordHash" FROM "Users" WHERE "Id"=%s', (s["userId"],))
    row = cur.fetchone()
    if row:
        # Idempotent repair: rows with our deterministic Id/username are
        # definitionally loadtest-owned; refresh only a malformed hash.
        if not is_valid_v3_hash(row[0]):
            cur.execute('UPDATE "Users" SET "PasswordHash"=%s WHERE "Id"=%s',
                        (pw_hash, s["userId"]))
            return "repaired"
        return "preserved"
    cur.execute('SELECT "Id" FROM "Users" WHERE "Username"=%s OR "Email"=%s',
                (s["username"], s["email"]))
    clash = cur.fetchone()
    if clash:
        raise SafetyRefusal(
            f"unique conflict on staff {s['username']}: non-deterministic row blocks seed")
    cur.execute(
        'INSERT INTO "Users" ("Id","Username","Email","PasswordHash","FirstName",'
        '"LastName","Role","IsActive","TenantId","EmailVerificationStatus",'
        '"CreatedAt","IsDeleted") VALUES (%s,%s,%s,%s,%s,%s,%s,TRUE,%s,1,now(),FALSE)',
        (s["userId"], s["username"], s["email"], pw_hash, s["firstName"],
         s["lastName"], role_int, t["tenantId"]))
    return "created"


def seed_customer(cur, t: dict, c: dict, pw_hash: str) -> str:
    cur.execute('SELECT "PasswordHash" FROM "OnlineCustomers" WHERE "Id"=%s',
                (c["customerId"],))
    row = cur.fetchone()
    if row:
        if not is_valid_v3_hash(row[0]):
            cur.execute('UPDATE "OnlineCustomers" SET "PasswordHash"=%s WHERE "Id"=%s',
                        (pw_hash, c["customerId"]))
            return "repaired"
        return "preserved"
    cur.execute('SELECT "Id" FROM "OnlineCustomers" WHERE "TenantId"=%s AND "Email"=%s',
                (t["tenantId"], c["email"]))
    if cur.fetchone():
        raise SafetyRefusal(
            f"unique conflict on customer {c['email']} in tenant {t['slug']}")
    cur.execute(
        'INSERT INTO "OnlineCustomers" ("Id","TenantId","Email","NormalizedEmail",'
        '"PasswordHash","Provider","DisplayName","IsActive","EmailVerified",'
        '"EmailVerifiedAtUtc","PhoneVerified","CreatedAtUtc") '
        "VALUES (%s,%s,%s,%s,%s,'Local',%s,TRUE,TRUE,now(),FALSE,now())",
        (c["customerId"], t["tenantId"], c["email"], c["email"].lower(),
         pw_hash, c["displayName"]))
    return "created"


def seed_category(cur, t: dict, c: dict) -> str:
    cur.execute('SELECT "Id" FROM "Categories" WHERE "Id"=%s', (c["categoryId"],))
    if cur.fetchone():
        return "preserved"
    cur.execute(
        'INSERT INTO "Categories" ("Id","TenantId","Name","CreatedAt","IsDeleted") '
        "VALUES (%s,%s,%s,now(),FALSE)",
        (c["categoryId"], t["tenantId"], c["name"]))
    return "created"


def seed_product(cur, t: dict, p: dict) -> str:
    cur.execute('SELECT "Id" FROM "Products" WHERE "Id"=%s', (p["productId"],))
    if cur.fetchone():
        return "preserved"
    for col, val in (("SKU", p["sku"]), ("Barcode", p["barcode"])):
        cur.execute(
            f'SELECT "Id" FROM "Products" WHERE "TenantId"=%s AND "{col}"=%s '
            'AND "IsDeleted"=FALSE', (t["tenantId"], val))
        if cur.fetchone():
            raise SafetyRefusal(
                f"unique conflict on product {col}={val} in tenant {t['slug']}")
    barcode = None if p["hasVariants"] else p["barcode"]
    online = None if p["hasVariants"] else p["price"]
    cur.execute(
        'INSERT INTO "Products" ("Id","TenantId","Name","Barcode","SKU","CategoryId",'
        '"PurchasePrice","SalePrice","OnlinePrice","PriceIncludesTax","TaxRate",'
        '"Unit","MinimumStockLevel","IsActive","TrackStock","HasVariants","Slug",'
        '"Weight","Length","Width","Height","WeightUnit","DimensionUnit",'
        '"PublishStatus","IsOnlineVisible","IsSearchable","IsFeatured",'
        '"LowStockThreshold","AllowBackorder","ProjectionMutationVersion",'
        '"AiGenerated","CreatedAt","IsDeleted") '
        "VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,FALSE,14,'Piece',0,TRUE,%s,%s,%s,"
        "0,0,0,0,'kg','cm',1,TRUE,TRUE,FALSE,10,%s,0,FALSE,now(),FALSE)",
        (p["productId"], t["tenantId"], p["name"], barcode, p["sku"],
         p["categoryId"], p["price"], p["price"], online, p["trackStock"],
         p["hasVariants"], p["slug"], p["allowBackorder"]))
    return "created"


def seed_variant(cur, t: dict, p: dict, v: dict) -> str:
    cur.execute('SELECT "Id" FROM "ProductVariants" WHERE "Id"=%s', (v["variantId"],))
    if cur.fetchone():
        return "preserved"
    for col in ("SKU", "Barcode"):
        cur.execute(
            f'SELECT "Id" FROM "ProductVariants" WHERE "TenantId"=%s AND "{col}"=%s '
            'AND "IsDeleted"=FALSE', (t["tenantId"], v[col.lower()]))
        if cur.fetchone():
            raise SafetyRefusal(
                f"unique conflict on variant {col}={v[col.lower()]} in tenant {t['slug']}")
    cur.execute(
        'INSERT INTO "ProductVariants" ("Id","TenantId","ProductId","SKU","Barcode",'
        '"Price","OnlinePrice","CostPrice","IsDefault","IsActive",'
        '"SemanticLifecycleState","SortOrder","ProjectionMutationVersion",'
        '"CreatedAt","IsDeleted") VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,TRUE,0,%s,0,now(),FALSE)',
        (v["variantId"], t["tenantId"], p["productId"], v["sku"], v["barcode"],
         v["price"], v["price"], v["price"], v["isDefault"], v["sortOrder"]))
    cur.execute(
        'INSERT INTO "ProductVariantAttributes" ("Id","VariantId","Name","Value",'
        '"SortOrder","CreatedAt","IsDeleted") VALUES (%s,%s,%s,%s,%s,now(),FALSE)',
        (det("variantattr", t["slug"], p["slug"], v["variantKey"]),
         v["variantId"], "Size", "Standard", v["sortOrder"]))
    return "created"


def seed_warehouse(cur, t: dict, w: dict) -> str:
    cur.execute('SELECT "Id" FROM "Warehouses" WHERE "Id"=%s', (w["warehouseId"],))
    if cur.fetchone():
        return "preserved"
    cur.execute(
        'INSERT INTO "Warehouses" ("Id","TenantId","Name","Code","IsActive",'
        '"IsDefault","CreatedAt","IsDeleted") VALUES (%s,%s,%s,%s,TRUE,%s,now(),FALSE)',
        (w["warehouseId"], t["tenantId"], w["name"], w["code"], w["isDefault"]))
    return "created"


def seed_stock(cur, t: dict, wmap: dict, row: dict) -> str:
    cur.execute(
        'SELECT "Id" FROM "ProductVariantStocks" WHERE "TenantId"=%s AND "VariantId"=%s '
        'AND "WarehouseId"=%s', (t["tenantId"], row["variantId"], row["warehouseId"]))
    if cur.fetchone():
        return "preserved"
    cur.execute(
        'INSERT INTO "ProductVariantStocks" ("Id","TenantId","VariantId","WarehouseId",'
        '"Quantity","ReservedQuantity","DamagedQuantity","ProjectionMutationVersion",'
        '"CreatedAt","IsDeleted") VALUES (%s,%s,%s,%s,%s,0,0,0,now(),FALSE)',
        (det("stock", t["slug"], row["variantId"], row["warehouseId"]),
         t["tenantId"], row["variantId"], row["warehouseId"], row["quantity"]))
    return "created"


# ----------------------------------------------------------------------------
# Verification (read-only)
# ----------------------------------------------------------------------------
def verify_universe(cur, universe: dict) -> dict:
    report: dict = {"tenants": [], "violations": []}
    for t in universe["tenants"]:
        tid = t["tenantId"]
        entry: dict = {"slug": t["slug"], "tenantId": tid, "checks": {}}
        cur.execute('SELECT "IsActive","Status","DeletedAtUtc" FROM "Tenants" WHERE "Id"=%s', (tid,))
        row = cur.fetchone()
        entry["checks"]["tenantActive"] = bool(row and row[0] and row[1] == 1 and row[2] is None)

        entry["checks"]["staff"] = _count(cur, "Users", tid)
        entry["checks"]["customers"] = _count(cur, "OnlineCustomers", tid)
        entry["checks"]["categories"] = _count(cur, "Categories", tid)
        entry["checks"]["products"] = _count(cur, "Products", tid)
        entry["checks"]["variants"] = _count(cur, "ProductVariants", tid)
        entry["checks"]["warehouses"] = _count(cur, "Warehouses", tid)
        entry["checks"]["stocks"] = _count(cur, "ProductVariantStocks", tid)

        # Ownership: every seeded entity's TenantId must equal the tenant.
        own_checks = [
            ("Users", "userId", [s["userId"] for s in t["staff"]]),
            ("OnlineCustomers", "customerId",
             [c["customerId"] for c in t["customers"]] + [t["sentinelCustomer"]["customerId"]]),
            ("Categories", "categoryId",
             [c["categoryId"] for c in t["categories"]] + [t["sentinelCategory"]["categoryId"]]),
            ("Products", "productId", [p["productId"] for p in t["products"]]),
            ("ProductVariants", "variantId",
             [v["variantId"] for p in t["products"] for v in p["variants"]]),
            ("Warehouses", "warehouseId", [w["warehouseId"] for w in t["warehouses"]]),
        ]
        violations = 0
        for table, _, ids in own_checks:
            if not ids:
                continue
            cur.execute(
                f'SELECT COUNT(*) FROM "{table}" WHERE "Id" = ANY(%s) AND "TenantId" <> %s',
                (ids, tid))
            bad = int(cur.fetchone()[0])
            violations += bad
        # Variant->product tenant agreement + stock->variant/warehouse agreement.
        cur.execute(
            'SELECT COUNT(*) FROM "ProductVariants" v JOIN "Products" p ON p."Id"=v."ProductId" '
            'WHERE v."TenantId"=%s AND p."TenantId"<>%s', (tid, tid))
        violations += int(cur.fetchone()[0])
        cur.execute(
            'SELECT COUNT(*) FROM "ProductVariantStocks" s '
            'JOIN "ProductVariants" v ON v."Id"=s."VariantId" '
            'JOIN "Warehouses" w ON w."Id"=s."WarehouseId" '
            'WHERE s."TenantId"=%s AND (v."TenantId"<>%s OR w."TenantId"<>%s)',
            (tid, tid, tid))
        violations += int(cur.fetchone()[0])
        entry["checks"]["foreignTenantViolations"] = violations
        report["violations"].append(violations)

        # Sentinel fingerprints.
        sp = next(p for p in t["products"] if p["kind"] == "sentinel")
        cur.execute('SELECT "SKU","Slug","Barcode","SalePrice" FROM "Products" WHERE "Id"=%s',
                    (sp["productId"],))
        srow = cur.fetchone()
        # Variant-container products must not carry a barcode (domain rule);
        # simple sentinels carry the barcode fingerprint on the product row.
        want_barcode = None if sp["hasVariants"] else sp["barcode"]
        entry["checks"]["sentinelFingerprint"] = bool(
            srow and srow[0] == sp["sku"] and srow[1] == sp["slug"]
            and srow[2] == want_barcode and str(srow[3]) == sp["price"])
        cur.execute('SELECT "Barcode" FROM "ProductVariants" WHERE "Id"=%s',
                    (sp["variants"][0]["variantId"],))
        vrow = cur.fetchone()
        entry["checks"]["sentinelVariantBarcode"] = bool(
            vrow and vrow[0] == sp["barcode"] + "-D")

        # Stock budgets.
        budget_ok = True
        for row in t["stockBudgets"]:
            cur.execute(
                'SELECT "Quantity","ReservedQuantity" FROM "ProductVariantStocks" '
                'WHERE "TenantId"=%s AND "VariantId"=%s AND "WarehouseId"=%s',
                (tid, row["variantId"], row["warehouseId"]))
            srow = cur.fetchone()
            # Numeric comparison (numeric scale varies: '0' vs '0.00').
            if not srow or float(srow[0]) != float(row["quantity"]) or float(srow[1]) != 0:
                budget_ok = False
                break
        entry["checks"]["stockBudgets"] = budget_ok
        report["tenants"].append(entry)
    return report


def _count(cur, table: str, tid: str) -> int:
    # OnlineCustomers has no IsDeleted column (lifecycle is IsActive).
    extra = "" if table == "OnlineCustomers" else ' AND "IsDeleted"=FALSE'
    cur.execute(f'SELECT COUNT(*) FROM "{table}" WHERE "TenantId"=%s{extra}', (tid,))
    return int(cur.fetchone()[0])


EXPECTED = {"staff": 3, "customers": 6, "categories": 4, "products": 20,
            "warehouses": 2}


def verification_verdict(report: dict, universe: dict) -> tuple[bool, list[str]]:
    lines = []
    ok = True
    for entry, t in zip(report["tenants"], universe["tenants"]):
        c = entry["checks"]
        exp_variants = sum(len(p["variants"]) for p in t["products"])
        exp_stocks = len(t["stockBudgets"])
        wants = {"staff": EXPECTED["staff"], "customers": EXPECTED["customers"],
                 "categories": EXPECTED["categories"], "products": EXPECTED["products"],
                 "variants": exp_variants, "warehouses": EXPECTED["warehouses"],
                 "stocks": exp_stocks}
        for k, want in wants.items():
            got = c.get(k, -1)
            mark = "OK " if got == want else "MISS"
            if got != want:
                ok = False
            lines.append(f"  [{mark}] {t['slug']}.{k}: {got}/{want}")
        for k in ("tenantActive", "sentinelFingerprint", "sentinelVariantBarcode", "stockBudgets"):
            good = bool(c.get(k))
            if not good:
                ok = False
            lines.append(f"  [{'OK ' if good else 'FAIL'}] {t['slug']}.{k}")
        if c.get("foreignTenantViolations", -1) != 0:
            ok = False
        lines.append(f"  [{'OK ' if c.get('foreignTenantViolations') == 0 else 'FAIL'}] "
                     f"{t['slug']}.foreignTenantViolations={c.get('foreignTenantViolations')}")
    return ok, lines


# ----------------------------------------------------------------------------
# Manifest
# ----------------------------------------------------------------------------
def write_manifest(universe: dict, gate: dict, stats: dict, stamp: str) -> Path:
    MANIFEST_DIR.mkdir(parents=True, exist_ok=True)
    manifest = {
        "marker": MARKER,
        "seedVersion": SEED_VERSION,
        "seedNamespace": str(SEED_NAMESPACE),
        "timestampUtc": stamp,
        "environment": gate["env"],
        "database": {"host": gate["host"], "database": gate["dbname"]},
        "counts": stats,
        "tenants": [
            {
                "key": t["key"], "tenantId": t["tenantId"], "slug": t["slug"],
                "staff": [{"userKey": s["userKey"], "userId": s["userId"]} for s in t["staff"]],
                "customers": [{"customerKey": c["customerKey"], "customerId": c["customerId"]}
                              for c in t["customers"]] + [{
                                  "customerKey": t["sentinelCustomer"]["customerKey"],
                                  "customerId": t["sentinelCustomer"]["customerId"]}],
                "categories": [{"name": c["name"], "categoryId": c["categoryId"]}
                               for c in t["categories"]] + [{
                                   "name": t["sentinelCategory"]["name"],
                                   "categoryId": t["sentinelCategory"]["categoryId"]}],
                "products": [{"slug": p["slug"], "productId": p["productId"],
                              "kind": p["kind"], "sku": p["sku"]}
                             for p in t["products"]],
                "variants": [{"variantId": v["variantId"], "sku": v["sku"]}
                             for p in t["products"] for v in p["variants"]],
                "warehouses": [{"code": w["code"], "warehouseId": w["warehouseId"]}
                               for w in t["warehouses"]],
                "stockBudgets": t["stockBudgets"],
            }
            for t in universe["tenants"]
        ],
        "probes": build_probes(universe)["probes"],
    }
    run_path = MANIFEST_DIR / f"run-{stamp.replace(':', '').replace('-', '')}.json"
    run_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    latest = MANIFEST_DIR / "latest.json"
    latest.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return run_path


# ----------------------------------------------------------------------------
# Modes
# ----------------------------------------------------------------------------
def cmd_plan() -> int:
    universe = build_universe()
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    (DATA_DIR / "universe.json").write_text(
        json.dumps(_public_universe(universe), indent=2), encoding="utf-8")
    (DATA_DIR / "probes.json").write_text(
        json.dumps(build_probes(universe), indent=2), encoding="utf-8")
    n_prod = sum(len(t["products"]) for t in universe["tenants"])
    n_var = sum(len(p["variants"]) for t in universe["tenants"] for p in t["products"])
    n_stock = sum(len(t["stockBudgets"]) for t in universe["tenants"])
    print(f"PLAN OK [{MARKER}] seedVersion={SEED_VERSION}")
    print(f"Tenants: {len(universe['tenants'])}/3 | Staff: 9 | Customers: 18 "
          f"| Categories: 12 | Products: {n_prod} | Variants: {n_var} "
          f"| Warehouses: 6 | Stock rows: {n_stock} | Probe products: 3")
    print(f"wrote {DATA_DIR / 'universe.json'} and {DATA_DIR / 'probes.json'} (identities only, no secrets)")
    return 0


def _public_universe(universe: dict) -> dict:
    return universe  # identities only by construction; password hashes never enter it


def cmd_verify() -> int:
    try:
        gate = safety_gate(require_password=False)
    except SafetyRefusal as exc:
        print(f"VERIFY REFUSE: {exc}")
        return 1
    universe = build_universe()
    con = connect(gate["conninfo"])
    try:
        with con.cursor() as cur:
            report = verify_universe(cur, universe)
        ok, lines = verification_verdict(report, universe)
    finally:
        con.close()
    print(f"VERIFY vs {gate['host']}/{gate['dbname']} [{MARKER}]")
    print("\n".join(lines))
    print("VERIFY PASS" if ok else "VERIFY INCOMPLETE (missing entities or violations above)")
    return 0 if ok else 2


def cmd_seed() -> int:
    try:
        gate = safety_gate(require_password=True)
    except SafetyRefusal as exc:
        print(f"SEED REFUSE: {exc}")
        return 1
    universe = build_universe()
    pw_hash = identity_v3_hash(gate["seedPassword"])
    stats = {"created": 0, "preserved": 0, "repaired": 0}
    stamp = datetime.now(timezone.utc).isoformat()
    con = connect(gate["conninfo"])
    try:
        with con.transaction():
            with con.cursor() as cur:
                for t in universe["tenants"]:
                    tally(cur, stats, seed_tenant(cur, t))
                    for (role, num), s in zip(STAFF_ROLES, t["staff"]):
                        tally(cur, stats, seed_staff(cur, t, s, num, pw_hash))
                    for i, c in enumerate(t["customers"], start=1):
                        tally(cur, stats, seed_customer(cur, t, c, pw_hash))
                    tally(cur, stats, seed_customer(cur, t, t["sentinelCustomer"], pw_hash))
                    for c in t["categories"]:
                        tally(cur, stats, seed_category(cur, t, c))
                    tally(cur, stats, seed_category(cur, t, t["sentinelCategory"]))
                    for p in t["products"]:
                        tally(cur, stats, seed_product(cur, t, p))
                        for v in p["variants"]:
                            tally(cur, stats, seed_variant(cur, t, p, v))
                    for w in t["warehouses"]:
                        tally(cur, stats, seed_warehouse(cur, t, w))
                    for row in t["stockBudgets"]:
                        tally(cur, stats, seed_stock(cur, t, {}, row))
        with con.cursor() as cur:
            report = verify_universe(cur, universe)
        ok, lines = verification_verdict(report, universe)
    except SafetyRefusal as exc:
        print(f"SEED REFUSE (rolled back): {exc}")
        return 1
    except Exception as exc:  # noqa: BLE001 - report exact stage, fail non-zero
        print(f"SEED FAIL (rolled back): {type(exc).__name__}: {exc}")
        return 1
    finally:
        con.close()
    print(f"SEED vs {gate['host']}/{gate['dbname']} [{MARKER}] "
          f"created={stats['created']} preserved={stats['preserved']} "
          f"repaired={stats['repaired']}")
    print("\n".join(lines))
    run_path = write_manifest(universe, gate, stats, stamp)
    print(f"manifest: {run_path} (+ latest.json)")
    login_ok = owner_login_proof(gate, universe)
    print("owner login proof (tenant-a, single call, no TestAuth):",
          "OK" if login_ok else "FAIL")
    final = ok and login_ok
    print("SEED PASS" if final else "SEED COMPLETE WITH FAILURES (see above)")
    return 0 if final else 3


def tally(cur, stats: dict, result: str) -> None:
    stats[result] += 1


def owner_login_proof(gate: dict, universe: dict) -> bool:
    """Single verified validation: POST /api/auth/login as tenant-a owner.

    Writes only to our own test row (LastLoginAt/refresh token). Proves the
    Python-generated Identity V3 hash verifies server-side, which is what
    future k6 auth pools depend on. No TestAuth involved.
    """
    t = universe["tenants"][0]
    body = json.dumps({"Username": t["staff"][0]["username"],
                       "Password": gate["seedPassword"]}).encode()
    req = urllib.request.Request(
        "http://localhost:5010/api/auth/login", data=body,
        headers={"Content-Type": "application/json",
                 "X-Request-Id": "m2-login-proof", "X-Correlation-Id": "m2-login-proof"},
        method="POST")
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read().decode()
            cookies = resp.getheader("Set-Cookie") or ""
    except Exception as exc:  # noqa: BLE001
        print(f"  login error: {type(exc).__name__}")
        return False
    # This API issues tokens via HttpOnly session cookies (AuthResponse body
    # carries the user DTO, not the token). Proof = 200 + session cookie +
    # body bound to our tenant.
    try:
        payload = json.loads(raw)
        bound_tenant = ((payload.get("user") or {}).get("tenantId") or "").lower()
    except Exception:
        bound_tenant = ""
    ok = resp.status == 200 and "access_token" in cookies \
        and bound_tenant == t["tenantId"].lower()
    if not ok:
        print(f"  login unexpected: status={resp.status} "
              f"cookie={bool(cookies)} tenant={bound_tenant or '?'}")
    return ok


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="M2 deterministic test-universe seeder.")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--plan", action="store_true", help="Build plan + data files, no DB contact.")
    group.add_argument("--verify", action="store_true", help="Read-only verification vs target DB.")
    group.add_argument("--seed", action="store_true", help="Safety gate + seed + verify + manifest.")
    args = parser.parse_args(argv)
    if args.verify:
        return cmd_verify()
    if args.seed:
        return cmd_seed()
    return cmd_plan()  # default is non-destructive plan


if __name__ == "__main__":
    sys.exit(main())

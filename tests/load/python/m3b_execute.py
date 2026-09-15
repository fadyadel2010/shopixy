"""M3-B projection execution — deterministic load-test universe preparation.

Scope: StoreConfig create/upsert + publication, Mongo syncs, ES full sync,
organic A-path spot check (2 products/tenant), read-only verification,
stock-budget integrity, evidence report, manifest freeze.

HARD RULES (fail closed, no bypass):
  * Every HTTP call is checked against orchestrate.m3b_route_allowed first.
  * m3b_preflight() must pass before ANY network traffic.
  * Tenant Owner JWT via real POST /api/auth/login. TestAuth forbidden.
  * No k6, no orders/sales/payments, no providers, no webhooks, no global
    reindex, no direct Mongo/ES/SQL writes (except the user-authorized
    owner-credential rotation, PasswordHash field only, 3 loadtest rows).
  * Secrets (passwords, tokens, cookies) are NEVER logged or written to files.
    Bodies are recorded as SHA-256 hashes + redacted summaries only.
  * Tenants run SEQUENTIALLY (A, B, C). No parallelization.

Usage (PowerShell, from repo root):
  $env:TEST_ENVIRONMENT='local'
  $env:TEST_DB_PROOF='PosEcommerceDb_Baseline@192.168.1.5-local-baseline'
  python tests/load/python/m3b_execute.py
The owner credential rotation generates a fresh strong password in process
memory (per explicit user authorization) and repairs ONLY the three
loadtest-owner rows via the seeder's field-only repair pattern.
"""
from __future__ import annotations

import hashlib
import json
import os
import secrets
import string
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

import requests

REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(Path(__file__).resolve().parent))
DATA_DIR = REPO_ROOT / "tests" / "load" / "data"
MANIFEST_DIR = DATA_DIR / "manifests"

import orchestrate  # noqa: E402
import seed as seeder  # noqa: E402

BASE_URL = os.environ.get("BASE_URL", "http://localhost:5010").rstrip("/")
TENANT_KEYS = ("a", "b", "c")
ES_URL = "http://192.168.1.5:9200"
PROJECTION_WAIT_S = 60
SPOTCHECK_TIMEOUT_S = 60

SENSITIVE_KEYS = ("password", "passwd", "token", "cookie", "set-cookie",
                  "authorization", "accesstoken", "refreshtoken")


def utcnow() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


def redact(obj):
    """Deep-redact sensitive keys for evidence; never leak secrets."""
    if isinstance(obj, dict):
        out = {}
        for k, v in obj.items():
            if any(s in str(k).lower().replace("_", "").replace("-", "") for s in
                   ("password", "token", "cookie", "authorization", "secret")):
                out[k] = "***REDACTED***"
            else:
                out[k] = redact(v)
        return out
    if isinstance(obj, list):
        return [redact(v) for v in obj]
    if isinstance(obj, str) and len(obj) > 400:
        return obj[:400] + f"...[truncated {len(obj)} chars]"
    return obj


class Evidence:
    def __init__(self):
        self.calls: list[dict] = []

    def record(self, tenant, method, path, status, latency_ms, body_text,
               req_id, corr_id, note="", extra=None):
        entry = {
            "tsUtc": utcnow(),
            "tenant": tenant,
            "method": method,
            "path": path,
            "status": status,
            "latencyMs": round(latency_ms, 1),
            "bodyHash": sha256_text(body_text or ""),
            "bodyLen": len(body_text or ""),
            "requestId": req_id,
            "correlationId": corr_id,
            "note": note,
        }
        if extra:
            entry.update(redact(extra))
        self.calls.append(entry)
        return entry


EV = Evidence()
FAILURES: list[str] = []

# In-memory tenant Owner JWTs (from access_token cookie after real login).
# Memory only: never logged (redacted), never written to files. Required
# because Python does not return Secure cookies over plain HTTP whereas the
# API's cookie-first/Bearer-fallback auth accepts the Authorization header
# (the documented harness approach per LOAD_TEST_DISCOVERY §4.3).
BEARER: dict[str, str] = {}


def fail(msg: str):
    FAILURES.append(msg)
    print(f"M3B FAIL: {msg}", file=sys.stderr)


def api_call(session: requests.Session, tenant_key: str, method: str, path: str,
             note="", host_header=None, tenant_id=None, json_body=None,
             extra_headers=None, timeout=60):
    """Single gated HTTP call. Refuses denylisted/off-allowlist routes."""
    if not orchestrate.m3b_route_allowed(method, path):
        fail(f"route refused by denylist/allowlist: {method} {path}")
        raise RuntimeError(f"refused route {method} {path}")
    req_id = f"m3b-{tenant_key}-{uuid.uuid4().hex[:8]}"
    # Product-mutation endpoints require Idempotency-Key == X-Request-Id
    # when both are present (PRODUCT_IDEMPOTENCY_HEADER_MISMATCH). The
    # idempotency key therefore doubles as the correlation ID.
    extra_idem = (extra_headers or {}).get("Idempotency-Key")
    if extra_idem:
        req_id = extra_idem
    headers = {"X-Request-Id": req_id, "X-Correlation-Id": req_id,
               "Accept": "application/json"}
    if tenant_key in BEARER and path != "/api/auth/login":
        headers["Authorization"] = "Bearer " + BEARER[tenant_key]
    if tenant_id:
        headers["X-Tenant-Id"] = tenant_id
    if host_header:
        headers["Host"] = host_header
    if extra_headers:
        headers.update(extra_headers)
    url = BASE_URL + path
    t0 = time.perf_counter()
    try:
        resp = session.request(method, url, headers=headers,
                               json=json_body, timeout=timeout)
        latency = (time.perf_counter() - t0) * 1000
        body = resp.text or ""
        EV.record(tenant_key, method, path, resp.status_code, latency, body,
                  req_id, req_id, note,
                  extra={"respHeaders": redact(dict(resp.headers))})
        return resp
    except Exception as exc:  # noqa: BLE001
        latency = (time.perf_counter() - t0) * 1000
        EV.record(tenant_key, method, path, -1, latency, str(exc),
                  req_id, req_id, note + " [TRANSPORT ERROR]")
        raise


# ----------------------------------------------------------------------------
# Step 0: user-authorized owner credential rotation (in-memory only)
# ----------------------------------------------------------------------------
def generate_strong_password(length=24) -> str:
    alphabet = string.ascii_letters + string.digits
    while True:
        pw = "".join(secrets.choice(alphabet) for _ in range(length))
        if (any(c.isupper() for c in pw) and any(c.islower() for c in pw)
                and any(c.isdigit() for c in pw)):
            return pw


def rotate_owner_credentials(universe: dict) -> dict:
    """Reset PasswordHash for the 3 loadtest owners ONLY.

    Field-only update scoped by deterministic Id + username + tenant match,
    mirroring seed.seed_staff's repair path. No other rows touched.
    Password lives in process memory only; never printed or written.
    """
    new_pw = generate_strong_password()
    os.environ["LOADTEST_SEED_PASSWORD"] = new_pw
    try:
        gate = seeder.safety_gate(require_password=True)
    except seeder.SafetyRefusal as exc:
        fail(f"credential rotation safety gate refused: {exc}")
        raise RuntimeError("rotation refused") from exc
    new_hash = seeder.identity_v3_hash(new_pw)
    con = seeder.connect(gate["conninfo"])
    rotated = []
    try:
        with con.transaction():
            with con.cursor() as cur:
                for t in universe["tenants"]:
                    if t["key"] not in ("a", "b", "c"):
                        continue
                    owner = t["staff"][0]
                    expect_user = f"loadtest-owner-{t['key']}"
                    cur.execute(
                        'SELECT "Id","Username","Email","TenantId","Role" '
                        'FROM "Users" WHERE "Id"=%s', (owner["userId"],))
                    row = cur.fetchone()
                    if not row:
                        fail(f"owner row missing for {expect_user}; refusing")
                        raise RuntimeError("owner row missing")
                    _id, username, _email, tenant_id, _role = row
                    if (username != expect_user
                            or str(tenant_id).lower() != t["tenantId"].lower()):
                        fail(f"owner row identity mismatch for {expect_user}; refusing")
                        raise RuntimeError("owner identity mismatch")
                    cur.execute(
                        'UPDATE "Users" SET "PasswordHash"=%s WHERE "Id"=%s',
                        (new_hash, owner["userId"]))
                    rotated.append(expect_user)
        print(f"credential rotation: updated PasswordHash for {len(rotated)} "
              f"loadtest-owned users (usernames: {', '.join(rotated)}); "
              "no other rows touched; password in memory only")
    finally:
        con.close()
    return {"gate": gate, "rotated": rotated}


# ----------------------------------------------------------------------------
# Read-only helpers (SQL / Mongo / ES)
# ----------------------------------------------------------------------------
def sql_stock_snapshot(conninfo: dict) -> dict:
    """Read-only stock snapshot keyed (variantId, warehouseId)."""
    import psycopg
    snap = {}
    con = psycopg.connect(host=conninfo["host"], port=conninfo["port"],
                          dbname=conninfo["dbname"], user=conninfo["user"],
                          password=conninfo["password"], connect_timeout=15)
    try:
        with con.cursor() as cur:
            cur.execute(
                'SELECT "TenantId","VariantId","WarehouseId","Quantity",'
                '"ReservedQuantity" FROM "ProductVariantStocks" '
                'WHERE "IsDeleted"=FALSE')
            for tid, vid, wid, qty, res in cur.fetchall():
                snap[(str(vid), str(wid))] = {
                    "tenantId": str(tid), "quantity": str(qty),
                    "reserved": str(res)}
    finally:
        con.close()
    return snap


def mongo_client():
    from pymongo import MongoClient
    settings = json.loads(
        (REPO_ROOT / "PointOfSale.API"
         / "appsettings.Development.local.json").read_text(encoding="utf-8"))
    return MongoClient(settings["MongoDB"]["ConnectionString"],
                       serverSelectionTimeoutMS=15000)


def pick_key(doc: dict, *names: str):
    lowered = {k.lower(): k for k in doc.keys()}
    for n in names:
        if n.lower() in lowered:
            return doc[lowered[n.lower()]]
    return None


PROBE_SKIP_KEYS = {"q", "query", "searchterm", "keyword", "term", "text",
                   "searchtext"}


def result_blob(text: str) -> str:
    """Response text minus query-echo fields, for isolation assertions.

    Prevents false P0s where the API echoes the searched probe string.
    """
    try:
        obj = json.loads(text)
    except Exception:  # noqa: BLE001
        return text or ""
    strings: list[str] = []

    def walk(o):
        if isinstance(o, dict):
            for k, v in o.items():
                if str(k).lower() in PROBE_SKIP_KEYS:
                    continue
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
        elif isinstance(o, str):
            strings.append(o)

    walk(obj)
    return " ".join(strings)


def es_get(path: str, body=None, timeout=30):
    """Read-only ES access (GET/POST _search/_count/_doc only)."""
    assert body is None or isinstance(body, dict)
    url = ES_URL + path
    t0 = time.perf_counter()
    if body is None:
        r = requests.get(url, timeout=timeout)
    else:
        r = requests.post(url, json=body, timeout=timeout)
    return r, (time.perf_counter() - t0) * 1000


def main() -> int:
    # In-process safe defaults (never override an explicit user value).
    os.environ.setdefault("TEST_ENVIRONMENT", "local")
    os.environ.setdefault("BASE_URL", "http://localhost:5010")
    os.environ.setdefault("TEST_DB_PROOF",
                           "PosEcommerceDb_Baseline@192.168.1.5-local-baseline")

    universe = seeder.build_universe()
    tenants = {t["key"]: t for t in universe["tenants"]}

    # Step 0 — credential rotation (user-authorized, loadtest rows only).
    # Runs BEFORE preflight because preflight itself requires the seed
    # password to be present; rotation's own safety_gate enforces the same
    # approved-DB/proof rules (fail closed, no weakening).
    rot = rotate_owner_credentials(universe)
    gate = rot["gate"]
    seed_pw = os.environ["LOADTEST_SEED_PASSWORD"]

    if orchestrate.m3b_preflight() != 0:
        print("M3B ABORT: preflight refused", file=sys.stderr)
        return 1

    baseline_stocks = sql_stock_snapshot(gate["conninfo"])
    print(f"baseline stock snapshot: {len(baseline_stocks)} rows (read-only)")

    results: dict = {"tenants": {}}
    sessions: dict = {}

    for key in TENANT_KEYS:
        t = tenants[key]
        tid = t["tenantId"]
        slug = t["slug"]
        host = f"{slug}.localhost:5174"
        owner = t["staff"][0]
        print(f"--- tenant {key} ({slug}) ---")
        sess = requests.Session()
        sessions[key] = sess
        tr: dict = {"tenantId": tid, "slug": slug}

        # -- Part C: login owner -------------------------------------------
        resp = api_call(sess, key, "POST", "/api/auth/login",
                        note="owner login (no TestAuth)",
                        json_body={"Username": owner["userKey"],
                                   "Password": seed_pw})
        login_ok = False
        bound_tenant = ""
        role = ""
        has_cookie = False
        if resp.status_code == 200:
            try:
                payload = resp.json()
                user = payload.get("user") or {}
                bound_tenant = str(user.get("tenantId") or "").lower()
                role = str(user.get("role") or user.get("Role") or "")
            except Exception:  # noqa: BLE001
                pass
            has_cookie = "access_token" in (resp.headers.get("Set-Cookie") or "")
            token = sess.cookies.get("access_token", "")
            if has_cookie and token:
                BEARER[key] = token  # memory only; redacted in evidence
            login_ok = has_cookie and bool(token) \
                and bound_tenant == tid.lower()
        tr["login"] = {"http": resp.status_code, "ok": login_ok,
                       "tenantBound": bound_tenant == tid.lower(),
                       "role": role, "sessionCookie": has_cookie}
        print(f"  login: http={resp.status_code} bound={bound_tenant == tid.lower()} "
              f"role={role} cookie={has_cookie}")
        if not login_ok:
            fail(f"tenant-{key}: owner login proof failed")
            results["tenants"][key] = tr
            continue

        # -- Part D: StoreConfig read + create/publish ----------------------
        resp = api_call(sess, key, "GET", "/api/admin/store-config",
                        note="read store config (discover RowVersion)",
                        tenant_id=tid)
        cfg = resp.json() if resp.status_code == 200 else {}
        rv = str(cfg.get("rowVersion") or "0")
        status = str(cfg.get("storefrontStatus") or "")
        cfg_tenant = str(cfg.get("tenantId") or "").lower()
        tr["storeConfigRead"] = {"http": resp.status_code, "rowVersion": rv,
                                 "status": status,
                                 "tenantMatch": cfg_tenant == tid.lower()}
        created = False
        if resp.status_code == 200 and rv != "0" and status.lower() == "live" \
                and cfg_tenant == tid.lower():
            print(f"  storeconfig: row exists + live (rv={rv}); no write needed")
            tr["publication"] = {"http": None, "created": False,
                                 "alreadyLive": True, "status": status}
        elif resp.status_code == 200:
            resp2 = api_call(
                sess, key, "PUT", "/api/admin/store-config/status",
                note="create-or-publish storefrontStatus=live",
                tenant_id=tid,
                json_body={"storefrontStatus": "live", "rowVersion": rv})
            created = rv == "0"
            ok = resp2.status_code == 200
            try:
                pub = resp2.json() if ok else {}
                new_status = str(pub.get("storefrontStatus") or "")
                new_rv = str(pub.get("rowVersion") or "")
            except Exception:  # noqa: BLE001
                new_status, new_rv = "", ""
            tr["publication"] = {"http": resp2.status_code, "created": created,
                                 "alreadyLive": False, "status": new_status,
                                 "newRowVersion": new_rv}
            print(f"  publish: http={resp2.status_code} created={created} "
                  f"status={new_status} rv={new_rv}")
            if not ok or new_status.lower() != "live":
                fail(f"tenant-{key}: publication failed "
                     f"(http={resp2.status_code} status={new_status!r})")
        else:
            fail(f"tenant-{key}: GET store-config http={resp.status_code}")
        # re-read to verify
        resp3 = api_call(sess, key, "GET", "/api/admin/store-config",
                         note="verify publication state", tenant_id=tid)
        cfg3 = resp3.json() if resp3.status_code == 200 else {}
        tr["publicationVerified"] = {
            "http": resp3.status_code,
            "status": str(cfg3.get("storefrontStatus") or ""),
            "tenantMatch": str(cfg3.get("tenantId") or "").lower() == tid.lower(),
            "rowVersion": str(cfg3.get("rowVersion") or "")}
        print(f"  verify: status={tr['publicationVerified']['status']} "
              f"rv={tr['publicationVerified']['rowVersion']}")

        # -- Part D0: organic unit backfill (M2 deferred units; projection
        # admission requires DefaultUnit on simple products for both Mongo
        # and ES overlays). Production path: seed-defaults -> {id}/units.
        tr["unitBackfill"] = backfill_units(sess, key, tid, t)

        # -- Part E: Mongo syncs -------------------------------------------
        resp = api_call(sess, key, "POST", "/api/admin/sync/categories",
                        note="mongo categories rebuild", tenant_id=tid,
                        timeout=180)
        try:
            cat_body = resp.json() if resp.status_code == 200 else {}
        except Exception:  # noqa: BLE001
            cat_body = {}
        tr["mongoCategories"] = {"http": resp.status_code,
                                 "count": cat_body.get("count"),
                                 "staleDeleted": cat_body.get("staleDeleted")}
        print(f"  sync categories: http={resp.status_code} "
              f"count={cat_body.get('count')}")

        resp = api_call(sess, key, "POST", "/api/admin/sync/product-variants",
                        note="mongo products rebuild", tenant_id=tid,
                        timeout=300)
        try:
            pv_body = resp.json() if resp.status_code == 200 else {}
        except Exception:  # noqa: BLE001
            pv_body = {}
        tr["mongoProducts"] = {
            "http": resp.status_code,
            "totalProcessed": pv_body.get("totalProcessed"),
            "successCount": pv_body.get("successCount"),
            "skippedCount": pv_body.get("skippedCount"),
            "errorCount": pv_body.get("errorCount"),
            "errors": redact(pv_body.get("errors") or [])}
        print(f"  sync product-variants: http={resp.status_code} "
              f"total={pv_body.get('totalProcessed')} "
              f"ok={pv_body.get('successCount')} "
              f"skip={pv_body.get('skippedCount')} "
              f"err={pv_body.get('errorCount')}")
        total = pv_body.get("totalProcessed") or 0
        synced = (pv_body.get("successCount") or 0) \
            + (pv_body.get("skippedCount") or 0)
        if resp.status_code != 200 or (pv_body.get("errorCount") or 0) != 0 \
                or total != 20 or synced != 20:
            fail(f"tenant-{key}: mongo product sync failed "
                 f"(http={resp.status_code} total={total} synced={synced} "
                 f"err={pv_body.get('errorCount')})")

        # -- Part F: ES full sync ------------------------------------------
        resp = api_call(sess, key, "POST", "/api/admin/sync/search/full",
                        note="elasticsearch tenant full sync", tenant_id=tid,
                        timeout=300)
        try:
            es_body = resp.json() if resp.status_code == 200 else {}
        except Exception:  # noqa: BLE001
            es_body = {}
        tr["esFullSync"] = {"http": resp.status_code,
                            "elapsedMs": es_body.get("elapsedMs")}
        print(f"  es full sync: http={resp.status_code} "
              f"elapsedMs={es_body.get('elapsedMs')}")
        if resp.status_code != 200:
            fail(f"tenant-{key}: ES full sync failed")

        print(f"  waiting {PROJECTION_WAIT_S}s projection lag window...")
        time.sleep(PROJECTION_WAIT_S)
        tr["projectionWaitS"] = PROJECTION_WAIT_S

        # -- Part G: organic A-path spot check (2 ordinary products) -------
        # Restores deterministic published state first (an earlier aborted
        # run left these unpublished), then runs unpublish->publish with
        # genuine domain events through outbox->bus->projections.
        spot = []
        normals = [p for p in t["products"] if p["kind"] == "normal"][:2]
        for p in normals:
            item = {"productId": p["productId"], "slug": p["slug"]}
            cur = read_product(sess, key, tid, p["productId"])
            if cur is None:
                fail(f"tenant-{key}: spot product read failed {p['slug']}")
                item["error"] = "read failed"
                spot.append(item)
                continue
            item["stateBefore"] = {
                "publishStatus": cur.get("publishStatus"),
                "isOnlineVisible": cur.get("isOnlineVisible")}
            if not (str(cur.get("publishStatus")).lower() == "published"
                    and cur.get("isOnlineVisible") is True):
                st, res = bulk_lifecycle(
                    sess, key, tid, "publish", p["productId"],
                    str(cur.get("rowVersion") or ""))
                item["restorePublish"] = {"http": st, "item": res}
                lag0 = poll_mongo_visibility(key, tid, p["productId"],
                                             expect_visible=True) \
                    if res.get("status") == "Processed" else None
                item["restoreLagS"] = lag0
                cur = read_product(sess, key, tid, p["productId"]) or cur
            item["rowVersionBefore"] = str(cur.get("rowVersion") or "")
            t_unpub = utcnow()
            st, res = bulk_lifecycle(
                sess, key, tid, "unpublish", p["productId"],
                item["rowVersionBefore"])
            item["unpublishHttp"] = st
            item["unpublishAtUtc"] = t_unpub
            item["unpublishItem"] = res
            if st == 200 and res.get("status") in ("Processed", "Unchanged"):
                lag1 = poll_mongo_visibility(key, tid, p["productId"],
                                             expect_visible=False)
            else:
                lag1 = None
                fail(f"tenant-{key}: spot unpublish not applied "
                     f"for {p['slug']} (http={st} item={res})")
            item["unpublishProjectionLagS"] = lag1
            cur2 = read_product(sess, key, tid, p["productId"]) or {}
            t_pub = utcnow()
            st, res = bulk_lifecycle(
                sess, key, tid, "publish", p["productId"],
                str(cur2.get("rowVersion") or res.get("newVersion") or ""))
            item["publishHttp"] = st
            item["publishAtUtc"] = t_pub
            item["publishItem"] = res
            if st == 200 and res.get("status") in ("Processed", "Unchanged"):
                lag2 = poll_mongo_visibility(key, tid, p["productId"],
                                             expect_visible=True)
            else:
                lag2 = None
                fail(f"tenant-{key}: spot publish not applied "
                     f"for {p['slug']} (http={st} item={res})")
            item["publishProjectionLagS"] = lag2
            if lag1 is None or lag2 is None:
                fail(f"tenant-{key}: spot-check projection did not converge "
                     f"for {p['slug']}")
            spot.append(item)
            print(f"  spot {p['slug']}: unpub={item['unpublishHttp']} "
                  f"lag={lag1}s pub={item['publishHttp']} lag={lag2}s")
        tr["spotCheck"] = spot
        results["tenants"][key] = tr

    # -- Parts H/I/J + report + manifest ------------------------------------
    verification = run_verification(tenants, sessions)
    verification["sqlCatalog"] = sql_catalog_state(gate["conninfo"], tenants)
    after_stocks = sql_stock_snapshot(gate["conninfo"])
    stock_ok, stock_notes = compare_stocks(baseline_stocks, after_stocks)
    write_report(tenants, results, verification, baseline_stocks, after_stocks,
                 stock_ok, stock_notes, rot["rotated"])
    maybe_freeze_manifest(verification, stock_ok)
    print("M3B evidence calls:", len(EV.calls))
    if FAILURES:
        print(f"M3B COMPLETE WITH FAILURES ({len(FAILURES)}):", file=sys.stderr)
        for f in FAILURES:
            print(f"  - {f}", file=sys.stderr)
        return 3
    print("M3B EXECUTION PASS")
    return 0


def read_product(sess: requests.Session, tenant_key: str, tenant_id: str,
                 product_id: str):
    """GET /api/products/{id} -> product dict or None. Gated + recorded."""
    try:
        r = api_call(sess, tenant_key, "GET", f"/api/products/{product_id}",
                     note="read product (RowVersion/state)", tenant_id=tenant_id)
    except Exception:  # noqa: BLE001
        return None
    if r.status_code != 200:
        return None
    try:
        return r.json()
    except Exception:  # noqa: BLE001
        return None


def bulk_lifecycle(sess: requests.Session, tenant_key: str, tenant_id: str,
                   op: str, product_id: str, expected_version: str):
    """POST /api/products/bulk/{publish|unpublish} for one product.

    Returns (http_status, per-item dict with status/code/newVersion).
    """
    try:
        r = api_call(
            sess, tenant_key, "POST", f"/api/products/bulk/{op}",
            note=f"bulk {op} (organic event path)", tenant_id=tenant_id,
            json_body={"items": [{"productId": product_id,
                                  "expectedVersion": expected_version}]},
            extra_headers={"Idempotency-Key":
                           f"m3b-{tenant_key}-{op}-{product_id[:8]}-"
                           f"{uuid.uuid4().hex[:6]}"})
    except Exception:  # noqa: BLE001
        return -1, {"status": "TransportError"}
    if r.status_code != 200:
        return r.status_code, {"status": f"http-{r.status_code}"}
    try:
        body = r.json()
        results = body.get("results") or []
        first = results[0] if results else {}
        return r.status_code, {
            "status": str(first.get("status") or ""),
            "code": first.get("code"),
            "previousVersion": first.get("previousVersion"),
            "newVersion": first.get("newVersion"),
        }
    except Exception:  # noqa: BLE001
        return r.status_code, {"status": "unparseable"}


def backfill_units(sess: requests.Session, tenant_key: str, tenant_id: str,
                   t: dict) -> dict:
    """Organic unit backfill for simple products lacking ProductUnits.

    M2 deferred ProductUnits; both Mongo and ES SQL-authority overlays
    require a default unit for admission. Production path per product:
    GET {id}/units -> POST {id}/units (base Piece, default, via authoring
    orchestrator with genuine domain events). No stock/budget impact.
    """
    out: dict = {"seedDefaults": None, "pieceUnitId": None, "products": []}
    try:
        r = api_call(sess, tenant_key, "POST", "/api/units/seed-defaults",
                     note="ensure tenant Piece unit", tenant_id=tenant_id)
        out["seedDefaults"] = {"http": r.status_code}
    except Exception as exc:  # noqa: BLE001
        out["seedDefaults"] = {"error": str(exc)[:120]}
    try:
        r = api_call(sess, tenant_key, "GET", "/api/units",
                     note="list tenant units", tenant_id=tenant_id)
        units = r.json() if r.status_code == 200 else []
    except Exception:  # noqa: BLE001
        units = []
    piece = next((u for u in units
                  if str(u.get("name") or "").lower() == "piece"), None)
    if piece is None:
        try:
            r = api_call(sess, tenant_key, "POST", "/api/units",
                         note="create tenant Piece unit", tenant_id=tenant_id,
                         json_body={"name": "Piece", "symbol": "pc",
                                    "description": "Individual piece or item",
                                    "isActive": True})
            piece = r.json() if r.status_code in (200, 201) else None
            out["pieceCreated"] = r.status_code
        except Exception as exc:  # noqa: BLE001
            out["pieceCreated"] = str(exc)[:120]
    piece_id = str((piece or {}).get("id") or "")
    out["pieceUnitId"] = bool(piece_id)
    if not piece_id:
        fail(f"tenant-{tenant_key}: no Piece unit available; unit backfill "
             "BLOCKED")
        return out
    # Deterministic SQL-authority prices (the API returns salePrice 0 for
    # unitless products; the canonical simple price lives in ProductUnits).
    price_of = {p["productId"]: float(p["price"]) for p in t["products"]}
    for p in t["products"]:
        if p.get("hasVariants", p.get("kind") == "variant-container"):
            continue
        entry = {"slug": p["slug"], "productId": p["productId"]}
        try:
            r = api_call(sess, tenant_key, "GET",
                         f"/api/products/{p['productId']}/units",
                         note="check product units", tenant_id=tenant_id)
            existing = r.json() if r.status_code == 200 else []
        except Exception:  # noqa: BLE001
            existing = []
        price = price_of.get(p["productId"], 0.0)
        default = next((u for u in existing if u.get("isDefault")), None)
        if default and float(default.get("sellingPrice") or 0) > 0:
            entry["action"] = "already-present"
            out["products"].append(entry)
            continue
        cur = read_product(sess, tenant_key, tenant_id, p["productId"])
        if cur is None:
            entry["action"] = "read-failed"
            fail(f"tenant-{tenant_key}: unit backfill read failed "
                 f"{p['slug']}")
            out["products"].append(entry)
            continue
        body = {"unitId": piece_id, "conversionFactor": 1,
                "purchasePrice": price, "sellingPrice": price,
                "barcode": cur.get("barcode"),
                "isDefault": True, "isBaseUnit": True}
        idem = (f"m3b-{tenant_key}-"
                f"{'unitfix' if default else 'unit'}-{p['productId'][:8]}")
        try:
            if default:
                # Price-0 default left by the first pass: correct via PUT.
                # Route key is the Units-table UnitId (not the ProductUnit
                # row id).
                r = api_call(
                    sess, tenant_key, "PUT",
                    f"/api/products/{p['productId']}/units/"
                    f"{default.get('unitId')}",
                    note=f"correct default unit price on {p['slug']}",
                    tenant_id=tenant_id, json_body=body,
                    extra_headers={
                        "Idempotency-Key": idem,
                        "If-Match": str(cur.get("rowVersion") or "0")})
                ok = r.status_code in (200, 201)
                entry["action"] = "price-corrected" if ok \
                    else f"http-{r.status_code}"
            else:
                r = api_call(
                    sess, tenant_key, "POST",
                    f"/api/products/{p['productId']}/units",
                    note=f"attach base Piece unit to {p['slug']}",
                    tenant_id=tenant_id, json_body=body,
                    extra_headers={
                        "Idempotency-Key": idem,
                        "If-Match": str(cur.get("rowVersion") or "0")})
                ok = r.status_code in (200, 201)
                entry["action"] = "attached" if ok \
                    else f"http-{r.status_code}"
            if not ok:
                fail(f"tenant-{tenant_key}: unit attach failed "
                     f"{p['slug']} http={r.status_code} "
                     f"body={(r.text or '')[:200]}")
        except Exception as exc:  # noqa: BLE001
            entry["action"] = f"error {exc}"[:120]
            fail(f"tenant-{tenant_key}: unit attach error {p['slug']}: {exc}")
        out["products"].append(entry)
    attached = sum(1 for e in out["products"]
                   if e["action"] in ("attached", "price-corrected"))
    present = sum(1 for e in out["products"]
                  if e["action"] == "already-present")
    print(f"  unit backfill: attached={attached} present={present} "
          f"of={len(out['products'])}")
    return out


def poll_mongo_visibility(tenant_key: str, tenant_id: str, product_id: str,
                          expect_visible: bool):
    """Poll Mongo products doc until visibility matches expectation.

    Read-only. Returns lag seconds or None on timeout.
    """
    deadline = time.time() + SPOTCHECK_TIMEOUT_S
    try:
        client = mongo_client()
    except Exception as exc:  # noqa: BLE001
        fail(f"tenant-{tenant_key}: mongo unavailable for spot poll: {exc}")
        return None
    try:
        coll = client["ecommerce_read_db"]["products"]
        while time.time() < deadline:
            doc = coll.find_one({"TenantId": tenant_id,
                                 "ProductId": product_id})
            if doc is None:
                # try camelCase fallback once
                doc = coll.find_one({"tenantId": tenant_id,
                                     "productId": product_id})
            visible = bool(doc) and bool(pick_key(doc, "IsOnlineVisible"))
            if visible == expect_visible:
                started = deadline - SPOTCHECK_TIMEOUT_S
                return round(time.time() - started, 1)
            time.sleep(5)
    finally:
        client.close()
    return None


def run_verification(tenants: dict, sessions: dict) -> dict:
    """Part H: read-only Mongo / ES / storefront / cross-tenant verification."""
    out: dict = {"tenants": {}, "crossTenant": []}
    probes = {k: next(p for p in tenants[k]["products"]
                      if p["kind"] == "sentinel") for k in TENANT_KEYS}
    try:
        client = mongo_client()
        mongo_ok = True
    except Exception as exc:  # noqa: BLE001
        fail(f"mongo verification unavailable: {exc}")
        client, mongo_ok = None, False
    try:
        for key in TENANT_KEYS:
            t = tenants[key]
            tid = t["tenantId"]
            slug = t["slug"]
            host = f"{slug}.localhost:5174"
            anon = requests.Session()
            v: dict = {}
            # Mongo (read-only)
            if mongo_ok:
                coll = client["ecommerce_read_db"]["products"]
                catcoll = client["ecommerce_read_db"]["categories"]
                docs = list(coll.find({"TenantId": tid}))
                if not docs:
                    docs = list(coll.find({"tenantId": tid}))
                catdocs = list(catcoll.find({"TenantId": tid}))
                if not catdocs:
                    catdocs = list(catcoll.find({"tenantId": tid}))
                sentinel = next(
                    (d for d in docs
                     if str(pick_key(d, "ProductId")).lower()
                     == probes[key]["productId"].lower()), None)
                foreign_probe_hits = 0
                for fk in TENANT_KEYS:
                    if fk == key:
                        continue
                    fp = next(p for p in tenants[fk]["products"]
                              if p["kind"] == "sentinel")
                    for d in docs:
                        blob = json.dumps(redact(
                            {kk: vv for kk, vv in d.items()
                             if kk != "_id"}), default=str)
                        if fp["sku"] in blob or fp["slug"] in blob:
                            foreign_probe_hits += 1
                            break
                foreign_leak = coll.count_documents(
                    {"ProductId": probes[key]["productId"],
                     "TenantId": {"$ne": tid}})
                price = str(pick_key(sentinel, "Price", "SalePrice")
                            ) if sentinel else None
                v["mongo"] = {
                    "products": len(docs), "wantProducts": 20,
                    "categories": len(catdocs), "wantCategories": 4,
                    "tenantMatch": all(
                        str(pick_key(d, "TenantId")).lower() == tid.lower()
                        for d in docs),
                    "sentinelPresent": sentinel is not None,
                    "sentinelSku": pick_key(sentinel, "SKU", "Sku")
                    if sentinel else None,
                    "sentinelSlug": pick_key(sentinel, "Slug", "Slug")
                    if sentinel else None,
                    "sentinelPrice": price,
                    "foreignProbeHits": foreign_probe_hits,
                    "foreignTenantLeak": foreign_leak,
                }
            # ES (read-only HTTP)
            v["es"] = es_verify(key, tid, probes)
            # Storefront (anonymous, Host header)
            sf: dict = {}
            for name, m, p in [
                    ("products", "GET", "/api/storefront/products"),
                    ("categories", "GET", "/api/storefront/categories"),
                    ("byId", "GET", "/api/storefront/products/by-id/"
                     + probes[key]["productId"]),
                    ("search", "GET", "/api/storefront/search?q="
                     + probes[key]["sku"]),
                    ("suggest", "GET", "/api/storefront/search/suggest?q="
                     + probes[key]["slug"][:8]),
                    ("resolveSlug", "GET", "/api/storefront/resolve-slug/"
                     + probes[key]["slug"])]:
                try:
                    r = api_call(anon, key, m, p,
                                 note=f"storefront {name} (anonymous)",
                                 host_header=host)
                    sf[name] = {"http": r.status_code,
                                "hash": sha256_text(r.text or "")}
                    if name in ("search", "suggest"):
                        # foreign probe identifiers must be absent from
                        # RESULT payload (query echo excluded to avoid
                        # false positives)
                        blob = result_blob(r.text or "")
                        leak_ids = []
                        for fk in TENANT_KEYS:
                            if fk == key:
                                continue
                            fp = next(pp for pp in tenants[fk]["products"]
                                      if pp["kind"] == "sentinel")
                            for ident in (fp["productId"], fp["sku"],
                                          fp["slug"], fp["barcode"],
                                          fp["name"]):
                                if ident and ident in blob:
                                    leak_ids.append(f"{fk}:{ident}")
                        sf[name]["foreignLeak"] = bool(leak_ids)
                        sf[name]["foreignLeakIds"] = leak_ids[:5]
                        if leak_ids:
                            fail(f"tenant-{key}: storefront {name} leaks "
                                 f"foreign probe {leak_ids[:3]}")
                    if name == "byId" and r.status_code != 200:
                        fail(f"tenant-{key}: storefront by-id sentinel "
                             f"http={r.status_code}")
                    if name in ("products", "categories") \
                            and r.status_code != 200:
                        fail(f"tenant-{key}: storefront {name} "
                             f"http={r.status_code}")
                except Exception as exc:  # noqa: BLE001
                    sf[name] = {"http": -1, "error": str(exc)[:120]}
                    fail(f"tenant-{key}: storefront {name} error: {exc}")
            v["storefront"] = sf
            out["tenants"][key] = v
        # Cross-tenant probes: A->B, B->C, C->A (read-only)
        pairs = [("a", "b"), ("b", "c"), ("c", "a")]
        for src, dst in pairs:
            host = f"{tenants[src]['slug']}.localhost:5174"
            anon = requests.Session()
            dst_probe = next(p for p in tenants[dst]["products"]
                             if p["kind"] == "sentinel")
            entry = {"src": src, "dst": dst}
            try:
                r = api_call(anon, src, "GET",
                             "/api/storefront/products/by-id/"
                             + dst_probe["productId"],
                             note=f"cross-tenant {src}->{dst} by-id",
                             host_header=host)
                entry["byIdHttp"] = r.status_code
                entry["byIdLeak"] = r.status_code == 200
                if r.status_code == 200:
                    fail(f"cross-tenant {src}->{dst}: foreign sentinel "
                         "visible via by-id")
            except Exception as exc:  # noqa: BLE001
                entry["byIdError"] = str(exc)[:120]
            try:
                r = api_call(anon, src, "GET",
                             "/api/storefront/search?q=" + dst_probe["sku"],
                             note=f"cross-tenant {src}->{dst} search",
                             host_header=host)
                blob = result_blob(r.text or "")
                leak_ids = [ident for ident in
                            (dst_probe["productId"], dst_probe["sku"],
                             dst_probe["slug"], dst_probe["barcode"],
                             dst_probe["name"])
                            if ident and ident in blob]
                entry["searchHttp"] = r.status_code
                entry["searchLeak"] = bool(leak_ids)
                entry["searchLeakIds"] = leak_ids[:5]
                if leak_ids:
                    fail(f"cross-tenant {src}->{dst}: foreign sentinel "
                         f"visible via search {leak_ids[:3]}")
            except Exception as exc:  # noqa: BLE001
                entry["searchError"] = str(exc)[:120]
            out["crossTenant"].append(entry)
    finally:
        if client is not None:
            client.close()
    return out


def es_verify(key: str, tenant_id: str, probes: dict) -> dict:
    """Read-only ES checks: counts, sentinel by slug/SKU, isolation probes."""
    out: dict = {}
    sentinel = probes[key]
    doc_id = f"{tenant_id}_{sentinel['productId']}"
    try:
        r, ms = es_get(f"/products/_doc/{doc_id}")
        out["sentinelDocHttp"] = r.status_code
        src = (r.json().get("_source") or {}) if r.status_code == 200 else {}
        tkey = next((k for k in src if k.lower() == "tenantid"), "tenantId")
        out["tenantField"] = tkey
        out["sentinelTenantMatch"] = str(src.get(tkey, "")).lower() \
            == tenant_id.lower() if src else False
        out["lookupMs"] = round(ms, 1)
        if r.status_code != 200 or not out["sentinelTenantMatch"]:
            fail(f"tenant-{key}: ES sentinel doc missing/mismatched "
                 f"(http={r.status_code})")
    except Exception as exc:  # noqa: BLE001
        out["sentinelDocError"] = str(exc)[:150]
        fail(f"tenant-{key}: ES sentinel lookup failed: {exc}")
        return out
    # counts per tenant (field name detected from the live doc)
    for idx, want in (("products", 20), ("categories", 4)):
        try:
            r, _ = es_get(f"/{idx}/_count",
                          {"query": {"term": {tkey: tenant_id}}})
            cnt = (r.json().get("count") if r.status_code == 200 else None)
            out[f"{idx}Count"] = cnt
            if cnt != want:
                fail(f"tenant-{key}: ES {idx} count={cnt} want={want}")
        except Exception as exc:  # noqa: BLE001
            out[f"{idx}Error"] = str(exc)[:150]
            fail(f"tenant-{key}: ES {idx} count failed: {exc}")
    # sentinel searchable by slug + SKU (tenant-scoped query)
    skey = next((k for k in src if k.lower() == "slug"), "slug")
    ukey = next((k for k in src if k.lower() in ("sku", "skus")), "sku")
    for field, val in ((skey, sentinel["slug"]), (ukey, sentinel["sku"])):
        try:
            q = {"query": {"bool": {"must": [
                {"term": {tkey: tenant_id}},
                {"match": {field: val}}]}}, "size": 5}
            r, _ = es_get("/products/_search", q)
            hits = (r.json().get("hits", {}).get("total", {}) or {})
            n = hits.get("value", -1) if isinstance(hits, dict) else -1
            out[f"searchBy{field.capitalize()}"] = n
            if r.status_code != 200 or n < 1:
                fail(f"tenant-{key}: ES search by {field} hits={n}")
        except Exception as exc:  # noqa: BLE001
            out[f"searchBy{field.capitalize()}Error"] = str(exc)[:150]
    # isolation: foreign probes must return 0 hits under this tenant
    try:
        mj = json.loads((MANIFEST_DIR / "latest.json").read_text(
            encoding="utf-8"))
    except Exception:  # noqa: BLE001
        mj = {"probes": []}
    for fk in TENANT_KEYS:
        if fk == key:
            continue
        other = next((p for p in mj.get("probes", [])
                      if p.get("tenantKey") == f"tenant-{fk}"), None)
        if not other:
            continue
        try:
            q = {"query": {"bool": {"must": [
                {"term": {tkey: tenant_id}},
                {"match": {"sku": other["probeSku"]}}]}}, "size": 5}
            r, _ = es_get("/products/_search", q)
            hits = (r.json().get("hits", {}).get("hits", [])
                    if r.status_code == 200 else [1])
            out[f"foreignProbe{fk.upper()}Hits"] = len(hits)
            if len(hits) != 0:
                fail(f"tenant-{key}: ES leaks tenant-{fk} probe "
                     f"({len(hits)} hits)")
        except Exception as exc:  # noqa: BLE001
            out[f"foreignProbe{fk.upper()}Error"] = str(exc)[:150]
    return out


def sql_catalog_state(conninfo: dict, tenants: dict) -> dict:
    """Read-only deterministic-state guard: all 20 products per tenant must
    end Published + online-visible + active (spot-check restores state)."""
    import psycopg
    out: dict = {}
    con = psycopg.connect(host=conninfo["host"], port=conninfo["port"],
                          dbname=conninfo["dbname"], user=conninfo["user"],
                          password=conninfo["password"], connect_timeout=15)
    try:
        with con.cursor() as cur:
            for key in TENANT_KEYS:
                tid = tenants[key]["tenantId"]
                cur.execute(
                    'SELECT COUNT(*) FROM "Products" WHERE "TenantId"=%s '
                    'AND "IsDeleted"=FALSE', (tid,))
                total = int(cur.fetchone()[0])
                cur.execute(
                    'SELECT COUNT(*) FROM "Products" WHERE "TenantId"=%s '
                    'AND "IsDeleted"=FALSE AND "PublishStatus"=1 '
                    'AND "IsOnlineVisible"=TRUE AND "IsActive"=TRUE', (tid,))
                pub = int(cur.fetchone()[0])
                cur.execute(
                    'SELECT COUNT(*) FROM "ProductUnits" pu JOIN "Products" p '
                    'ON p."Id"=pu."ProductId" WHERE p."TenantId"=%s '
                    'AND pu."IsDeleted"=FALSE AND pu."IsDefault"=TRUE', (tid,))
                with_units = int(cur.fetchone()[0])
                entry = {"total": total, "publishedVisible": pub,
                         "defaultUnits": with_units}
                out[key] = entry
                if total != 20 or pub != 20:
                    fail(f"tenant-{key}: SQL catalog not fully published "
                         f"(total={total} publishedVisible={pub})")
    finally:
        con.close()
    return out


def compare_stocks(before: dict, after: dict):
    notes = []
    if set(before) != set(after):
        missing = set(before) - set(after)
        extra = set(after) - set(before)
        notes.append(f"row-set changed: missing={len(missing)} extra={len(extra)}")
        return False, notes
    changed = [(k, before[k], after[k]) for k in before
               if before[k] != after[k]]
    if changed:
        notes.append(f"{len(changed)} rows changed: "
                     + "; ".join(f"{k[0][:8]}.. {v0}->{v1}"
                                 for k, v0, v1 in changed[:5]))
        return False, notes
    notes.append(f"{len(before)} rows identical (quantity+reserved)")
    return True, notes


def write_report(tenants, results, verification, before, after,
                 stock_ok, stock_notes, rotated):
    lines = []
    A = lines.append
    A("# M3B Projection Execution Report — Shopixy Load Harness")
    A("")
    A(f"> Date (UTC): {utcnow()}. Scope: M3-B1 + M3-B2 only. No k6 load, no "
      "orders/sales/payments, no providers.")
    A("")
    A("## 1. Environment")
    A("")
    A(f"- API: `{BASE_URL}` (`Development`, Windows local); infra Ubuntu "
      "`192.168.1.5` (Postgres/Mongo/Redis/RabbitMQ/ES).")
    A(f"- `TEST_ENVIRONMENT=local`; `TEST_DB_PROOF` matched approved "
      "host+db (gate-verified); `LOADTEST_SEED_PASSWORD` present in process "
      "memory only (freshly rotated, never logged/stored).")
    A("- k6 NOT run. No stress/soak/spike.")
    A("")
    A("## 2. Database target")
    A("")
    A("- Host `192.168.1.5`, database `PosEcommerceDb_Baseline` (approved "
      "local baseline). Mongo `ecommerce_read_db`; ES `http://192.168.1.5:9200`.")
    A("")
    A("## 3. Tenants processed (sequential, no parallelization)")
    A("")
    for k in TENANT_KEYS:
        A(f"- tenant-{k}: `{tenants[k]['tenantId']}` ({tenants[k]['slug']})")
    A("")
    A("## 4. StoreConfig creation method")
    A("")
    A("- Confirmed path per `M3B_STORECONFIG_PATH_AUDIT.md`: "
      "`GET /api/admin/store-config` → `PUT /api/admin/store-config/status` "
      "(`storefrontStatus=live` + RowVersion), Owner JWT, per-tenant. "
      "Registration NOT used; no SQL edits.")
    for k in TENANT_KEYS:
        tr = results["tenants"].get(k, {})
        A(f"- tenant-{k}: read={tr.get('storeConfigRead')} "
          f"publication={redact(tr.get('publication'))} "
          f"verified={redact(tr.get('publicationVerified'))}")
    A("")
    A("### 4b. Unit backfill (M2-deferred prerequisite, organic path)")
    A("")
    A("- `POST /api/units/seed-defaults` (+ `POST /api/units` fallback) then "
      "`POST /api/products/{id}/units` per simple product lacking units "
      "(base Piece, default, conversion 1, product price/barcode; "
      "Idempotency-Key + If-Match; authoring orchestrator events). No stock "
      "impact; variant-container skipped (variant admission).")
    for k in TENANT_KEYS:
        ub = results["tenants"].get(k, {}).get("unitBackfill", {})
        prods = ub.get("products", [])
        attached = sum(1 for e in prods if e.get("action") == "attached")
        present = sum(1 for e in prods if e.get("action") == "already-present")
        others = [e for e in prods if e.get("action")
                  not in ("attached", "already-present")]
        A(f"- tenant-{k}: seedDefaults={ub.get('seedDefaults')} "
          f"piece={ub.get('pieceUnitId')} attached={attached} "
          f"present={present} other={redact(others)}")
    A("")
    A("### 4c. SQL catalog end-state (read-only guard)")
    A("")
    for k in TENANT_KEYS:
        A(f"- tenant-{k}: "
          f"{redact((verification.get('sqlCatalog') or {}).get(k))}")
    A("")
    A("## 5. Publication results")
    A("")
    for k in TENANT_KEYS:
        tr = results["tenants"].get(k, {})
        A(f"- tenant-{k}: login={redact(tr.get('login'))}")
    A("")
    A("## 6. Mongo sync results")
    A("")
    for k in TENANT_KEYS:
        tr = results["tenants"].get(k, {})
        A(f"- tenant-{k}: categories={tr.get('mongoCategories')} "
          f"products={tr.get('mongoProducts')}")
    A("")
    A("## 7. ES sync results")
    A("")
    for k in TENANT_KEYS:
        tr = results["tenants"].get(k, {})
        A(f"- tenant-{k}: full={tr.get('esFullSync')} "
          f"waitS={tr.get('projectionWaitS')}")
    A("")
    A("## 8. Organic A-path spot-check results (2 ordinary products/tenant)")
    A("")
    for k in TENANT_KEYS:
        for item in results["tenants"].get(k, {}).get("spotCheck", []):
            A(f"- tenant-{k} {item.get('slug')}: {redact(item)}")
    A("")
    A("## 9. Projection lag observed")
    A("")
    A(f"- ES assertion window: {PROJECTION_WAIT_S}s per tenant (audit guidance).")
    A("- Organic spot-check lags recorded per item in §8 "
      "(Mongo poll, 5s interval).")
    A("")
    A("## 10. Redis observations (passive; no direct mutation)")
    A("")
    A("- No manual clear, no `clear-cache` endpoint, no direct Redis writes. "
      "Projection writer invalidation relied upon (product sync clears "
      "tenant product cache per API contract).")
    A("")
    A("## 11. Mongo verification")
    A("")
    for k in TENANT_KEYS:
        A(f"- tenant-{k}: {redact(verification['tenants'].get(k, {}).get('mongo'))}")
    A("")
    A("## 12. ES verification")
    A("")
    for k in TENANT_KEYS:
        A(f"- tenant-{k}: {redact(verification['tenants'].get(k, {}).get('es'))}")
    A("")
    A("## 13. Storefront verification (anonymous, Host header)")
    A("")
    for k in TENANT_KEYS:
        A(f"- tenant-{k}: {redact(verification['tenants'].get(k, {}).get('storefront'))}")
    A("")
    A("## 14. I-1 isolation result (no foreign products/probes)")
    A("")
    for e in verification.get("crossTenant", []):
        A(f"- {e.get('src')}->{e.get('dst')}: {redact(e)}")
    A("")
    A("## 15. I-2 isolation result (store resolves to own tenant)")
    A("")
    A("- Covered by resolve-slug + by-id + search assertions in §13 (own "
      "tenant data, no foreign probes) and §14 cross-tenant probes.")
    A("")
    A("## 16. Stock budget verification")
    A("")
    A(f"- before={len(before)} rows after={len(after)} rows "
      f"identical={stock_ok}: {'; '.join(stock_notes)}")
    if not stock_ok:
        A("- VERDICT IMPACT: BLOCKED (budgets changed outside projection metadata).")
    A("")
    A("## 17. Request/correlation evidence")
    A("")
    A(f"- {len(EV.calls)} gated HTTP calls recorded (method/path/status/ "
      "latency/body-hash/request-id/correlation-id; secrets redacted).")
    methods = {}
    for c in EV.calls:
        methods[(c["method"], c["path"])] = methods.get((c["method"], c["path"]), 0) + 1
    for (m, p), n in sorted(methods.items()):
        A(f"  - {m} {p} ×{n}")
    A("")
    A("## 18. Safety proof")
    A("")
    A("- `orchestrate.m3b_preflight()` passed (env=local, approved DB/host, "
      "seed password present, no prod markers, providers off, TestAuth "
      "forbidden, denylist enforced). No bypass flag exists.")
    A(f"- Owner credential rotation: {', '.join(rotated)} — PasswordHash "
      "field only, deterministic-Id scoped, user-authorized; no other rows.")
    A("- Tenant Owner JWT via real login; per-tenant JWTs; sequential tenants.")
    A("- Routes invoked are allowlist-only; transports recorded in §17.")
    A("")
    A("## 19. Failures/blockers")
    A("")
    if FAILURES:
        for f in FAILURES:
            A(f"- {f}")
    else:
        A("- None.")
    A("")
    A("## 20. Final verdict")
    A("")
    verdict = Jh_verdict(verification, stock_ok)
    A(f"**{verdict}**")
    A("")
    path = REPO_ROOT / "tests" / "load" / "M3B_PROJECTION_EXECUTION_REPORT.md"
    path.write_text("\n".join(lines), encoding="utf-8")
    print(f"report: {path} verdict={verdict}")
    return verdict


def Jh_verdict(verification: dict, stock_ok: bool) -> str:
    if FAILURES or not stock_ok:
        weak = any("BLOCKED" in f for f in FAILURES)
        return "BLOCKED" if (weak or not stock_ok) else "FAIL"
    for k in TENANT_KEYS:
        v = verification["tenants"].get(k, {})
        m = v.get("mongo") or {}
        e = v.get("es") or {}
        s = (verification.get("sqlCatalog") or {}).get(k, {})
        if m.get("products") != 20 or m.get("categories") != 4:
            return "FAIL"
        if not m.get("sentinelPresent") or m.get("foreignProbeHits") != 0:
            return "FAIL"
        if not m.get("tenantMatch"):
            return "FAIL"
        if e.get("productsCount") != 20 or e.get("categoriesCount") != 4:
            return "FAIL"
        if s.get("total") != 20 or s.get("publishedVisible") != 20:
            return "FAIL"
    for c in verification.get("crossTenant", []):
        if c.get("byIdLeak") or c.get("searchLeak"):
            return "FAIL"
    return "PASS"


def maybe_freeze_manifest(verification: dict, stock_ok: bool):
    verdict = Jh_verdict(verification, stock_ok)
    latest = MANIFEST_DIR / "latest.json"
    manifest = json.loads(latest.read_text(encoding="utf-8"))
    if verdict == "PASS":
        manifest["projectionReady"] = True
        manifest["publicationReady"] = True
        manifest["verifiedAtUtc"] = utcnow()
        manifest["mongoVerified"] = True
        manifest["elasticsearchVerified"] = True
        manifest["storefrontVerified"] = True
        manifest["isolationProbesVerified"] = True
        manifest["stockBudgetVerified"] = True
        latest.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
        stamp = manifest["verifiedAtUtc"].replace(":", "").replace("-", "")
        (MANIFEST_DIR / f"m3b-projection-{stamp}.json").write_text(
            json.dumps(manifest, indent=2), encoding="utf-8")
        print("manifest FROZEN: projectionReady=true publicationReady=true")
    else:
        print(f"manifest NOT frozen (verdict={verdict})")


if __name__ == "__main__":
    try:
        sys.exit(main())
    except RuntimeError as exc:
        print(f"M3B ABORT: {exc}", file=sys.stderr)
        sys.exit(2)

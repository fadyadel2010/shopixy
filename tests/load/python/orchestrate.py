"""M1.1 preflight + M3-B execution safety gate. Fails closed.

M1.1 mode (default): READ-ONLY smoke gate. Refuses to run unless the target
is explicitly proven non-production. Performs NO network writes.

M3-B mode (--m3b-preflight): execution gate for the deterministic projection
phase (StoreConfig create/upsert + publication + tenant Mongo/ES syncs +
organic spot check + read-only verification). Still fails closed, still
forbids production, providers, webhooks, orders/payments, TestAuth, global
reindex and all destructive routes. There is intentionally NO bypass flag.

Exit 0 = allowed; nonzero = REFUSE TO RUN.
"""
from __future__ import annotations

import argparse
import os
import sys
from urllib.parse import urlparse

REQUIRED_ENV = ("BASE_URL", "TEST_ENVIRONMENT", "TEST_TENANT", "TEST_SLUG", "TEST_PRODUCT_ID")
ALLOWED_ENVIRONMENTS = {"local", "test", "ci", "staging-test"}

# Substrings that mark a target as production (fail closed on any match).
PROD_HOST_MARKERS = ("prod", "production", "live", "shopixy.com", "azurewebsites.net")

# Route fragments that must never appear in an M1.1 smoke allowlist/run.
UNSAFE_ROUTE_FRAGMENTS = (
    "/webhooks/", "/payments/webhook", "/paymob/webhook", "/fawry",
    "/maintenance/", "/reset-database",
    "/admin/search/", "/reindex", "/admin/af16e-reindex",
    "/upload", "/debug/", "/outbox/",
    "/dev/commercehub", "/dev/",
    "/test-auth",
    "/checkout",  # POST checkout + pricing are writes/preview-compute: out of M1.1 smoke
    "/pos-carts", "/api/sales", "/api/orders", "/fulfillment",
    "/lifecycle", "/settlements", "/refund",
)

# Provider execution flags that must be absent or explicitly false.
PROVIDER_EXEC_FLAGS = (
    "PAYMOB_EXECUTION_ENABLED",
    "STRIPE_EXECUTION_ENABLED",
    "FAWRY_EXECUTION_ENABLED",
    "WHATSAPP_EXECUTION_ENABLED",
    "EMAIL_EXECUTION_ENABLED",
    "SHIPPING_EXECUTION_ENABLED",
)


def _fail(reason: str) -> int:
    print(f"PREFLIGHT REFUSE: {reason}", file=sys.stderr)
    return 1


# ----------------------------------------------------------------------------
# M3-B execution gate (additive; M1.1 behavior above is unchanged).
# ----------------------------------------------------------------------------
# M3-B is the ONLY phase allowed to go beyond read-only smoke, and only via
# the tenant-scoped allowlist below. Everything else stays denied.
M3B_ALLOWED_ENVIRONMENTS = {"local", "test"}

# Approved local test database for this phase (explicit, fail closed).
M3B_APPROVED_DB = "posecommercedb_baseline"
M3B_APPROVED_HOST = "192.168.1.5"

# Tenant-scoped allowlist: (method, path-prefix) pairs. Any request whose
# (method, path) is not covered here must be refused by the executor.
M3B_ALLOWLIST = (
    ("POST", "/api/auth/login"),
    ("GET", "/api/auth/session"),
    ("GET", "/api/admin/store-config"),
    ("PUT", "/api/admin/store-config/status"),
    ("PUT", "/api/admin/store-config"),
    ("POST", "/api/admin/sync/categories"),
    ("POST", "/api/admin/sync/product-variants"),
    ("GET", "/api/admin/sync/categories/debug"),
    ("GET", "/api/admin/sync/products/debug"),
    ("POST", "/api/admin/sync/search/full"),
    ("POST", "/api/admin/sync/search/products"),
    ("POST", "/api/admin/sync/search/categories"),
    ("POST", "/api/admin/sync/search/pages"),
    ("POST", "/api/products/bulk/publish"),
    ("POST", "/api/products/bulk/unpublish"),
    # POST /api/products/{id}/units only (organic unit backfill; the
    # executor never calls product-create or bulk-delete through this prefix;
    # every call is recorded in evidence).
    ("POST", "/api/products/"),
    # PUT /api/products/{id}/units/{unitId} only (correct price-0 default
    # units left by the first backfill pass; same enforcement as above).
    ("PUT", "/api/products/"),
    ("POST", "/api/units/seed-defaults"),
    ("POST", "/api/units"),
    ("GET", "/api/units"),
    ("GET", "/api/products/"),
    ("GET", "/api/products"),
    ("GET", "/api/storefront/"),
)

# Fragments that must NEVER be invoked during M3-B. Checked against every
# planned route AND against the environment (no opt-out flag exists).
M3B_DENYLIST_FRAGMENTS = (
    "test-auth",
    "reindex/global",
    "marketplace/admin/reindex",
    "af16e-reindex",
    "promote",
    "alias",
    "recreate-index",
    "delete-index",
    "/index",
    "maintenance/",
    "reset-database",
    "debug/",
    "outbox/",
    "__",
    "webhook",
    "paymob",
    "fawry",
    "stripe",
    "whatsapp",
    "email/send",
    "sms",
    "shipment/create",
    "shipping/purchase",
    "carrier/register",
    "/checkout",
    "checkout-session",
    "pos-carts",
    "/api/sales",
    "/api/orders",
    "fulfillment",
    "lifecycle",
    "settlements",
    "refund",
    "cod/confirm",
    "payment/intent",
    "payments/intents",
    "clear-cache",
    "redis/flush",
)

# Env names that would indicate a TestAuth / bypass attempt. Any of these
# being set (or any CLI bypass flag) refuses the run.
M3B_FORBIDDEN_ENV = (
    "USE_TEST_AUTH",
    "TEST_AUTH_TOKEN",
    "M3B_SKIP_SAFETY",
    "LOADTEST_SKIP_SAFETY",
    "ALLOW_PROD",
    "FORCE_PROD",
    "FORCE_PRODUCTION",
    "SKIP_DB_PROOF",
)

M3B_FORBIDDEN_ARGV = (
    "--force",
    "--force-production",
    "--allow-prod",
    "--skip-safety",
    "--no-safety",
    "--bypass",
)


def _m3b_fail(reason: str) -> int:
    print(f"M3B PREFLIGHT REFUSE: {reason}", file=sys.stderr)
    return 1


def m3b_route_allowed(method: str, path: str) -> bool:
    """True only when (method, path) hits the allowlist and no denylist fragment."""
    low_path = (path or "").lower()
    for frag in M3B_DENYLIST_FRAGMENTS:
        if frag.lower() in low_path:
            return False
    low_method = (method or "").upper()
    for allowed_method, prefix in M3B_ALLOWLIST:
        if low_method == allowed_method and low_path.startswith(prefix.lower()):
            return True
    return False


def m3b_preflight() -> int:
    """Safety gate for M3-B execution. Fail closed; no bypass flag exists."""
    get = os.environ.get

    for token in M3B_FORBIDDEN_ARGV:
        if token in sys.argv:
            return _m3b_fail(f"bypass flag forbidden: {token} (no bypass exists)")

    for name in M3B_FORBIDDEN_ENV:
        if (get(name) or "").strip():
            return _m3b_fail(f"forbidden env {name} is set (TestAuth/bypass forbidden)")

    test_env = (get("TEST_ENVIRONMENT") or "").strip().lower()
    if test_env not in M3B_ALLOWED_ENVIRONMENTS:
        return _m3b_fail(
            f"TEST_ENVIRONMENT={test_env!r} not in M3-B set {sorted(M3B_ALLOWED_ENVIRONMENTS)}"
        )

    base_url = (get("BASE_URL") or get("LOADTEST_BASE_URL") or "http://localhost:5010").strip()
    try:
        host = (urlparse(base_url).hostname or "").lower()
    except Exception:
        return _m3b_fail(f"BASE_URL unparseable: {base_url!r}")
    if not host:
        return _m3b_fail(f"BASE_URL has no hostname: {base_url!r}")
    if any(m in host for m in PROD_HOST_MARKERS):
        return _m3b_fail(f"production hostname protection: {host!r}")
    if host not in ("localhost", "127.0.0.1", "::1") and not host.endswith(".localhost"):
        return _m3b_fail(f"M3-B runs local/test only; refusing host {host!r}")

    db_proof = (get("TEST_DB_PROOF") or "").strip()
    if not db_proof:
        return _m3b_fail("missing TEST_DB_PROOF (explicit test-database target proof required)")
    low_proof = db_proof.lower()
    if any(m in low_proof for m in PROD_HOST_MARKERS):
        return _m3b_fail("TEST_DB_PROOF looks production (no production marker allowed)")
    if M3B_APPROVED_DB not in low_proof or M3B_APPROVED_HOST not in low_proof:
        return _m3b_fail(
            "TEST_DB_PROOF must match the actual approved DB/host "
            f"({M3B_APPROVED_DB}@{M3B_APPROVED_HOST})"
        )

    seed_pw = get("LOADTEST_SEED_PASSWORD") or ""
    if len(seed_pw) < 12 or not any(c.isupper() for c in seed_pw) \
            or not any(c.islower() for c in seed_pw) \
            or not any(c.isdigit() for c in seed_pw):
        return _m3b_fail(
            "LOADTEST_SEED_PASSWORD missing or too weak "
            "(>=12 chars with upper/lower/digit; tenant JWT login requires it)")

    for flag in PROVIDER_EXEC_FLAGS:
        val = (get(flag) or "").strip().lower()
        if val in ("1", "true", "yes", "enabled", "on"):
            return _m3b_fail(f"provider execution flag set: {flag}={val!r} (no providers in M3-B)")

    # M3-B authenticates via tenant Owner JWT from real login; TestAuth forbidden.
    use_test_auth = (get("M3B_USE_TEST_AUTH") or get("USE_TESTAUTH") or "").strip().lower()
    if use_test_auth in ("1", "true", "yes"):
        return _m3b_fail("TestAuth forbidden in M3-B (use tenant Owner JWT via /api/auth/login)")

    for p in ("/api/auth/login", "/api/admin/store-config", "/api/admin/store-config/status",
              "/api/admin/sync/categories", "/api/admin/sync/product-variants",
              "/api/admin/sync/search/full"):
        if not m3b_route_allowed("POST" if "login" in p or "sync" in p else "GET" if p == "/api/admin/store-config" else "PUT", p):
            return _m3b_fail(f"internal allowlist misconfiguration for {p}")

    print("M3B PREFLIGHT OK "
          f"(env={test_env}, host={host}, db_proof_approved=True, seed_password_present=True, "
          "testauth=forbidden, denylist_enforced=True)")
    return 0


def main(argv: list[str] | None = None) -> int:
    if argv is None:
        argv = sys.argv[1:]
    if "--m3b-preflight" in argv:
        return m3b_preflight()

    get = os.environ.get

    for name in REQUIRED_ENV:
        if not (get(name) or "").strip():
            return _fail(f"missing required env {name}")

    test_env = (get("TEST_ENVIRONMENT") or "").strip().lower()
    if test_env not in ALLOWED_ENVIRONMENTS:
        return _fail(
            f"TEST_ENVIRONMENT={test_env!r} not in allowed set {sorted(ALLOWED_ENVIRONMENTS)}"
        )

    base_url = (get("BASE_URL") or "").strip()
    try:
        host = (urlparse(base_url).hostname or "").lower()
    except Exception:
        return _fail(f"BASE_URL unparseable: {base_url!r}")
    if not host:
        return _fail(f"BASE_URL has no hostname: {base_url!r}")
    if host in ("localhost", "127.0.0.1", "::1") or host.endswith(".localhost"):
        pass  # local dev target explicitly allowed with TEST_ENVIRONMENT set
    elif any(m in host for m in PROD_HOST_MARKERS):
        return _fail(f"production hostname protection: {host!r}")
    if test_env in ("local", "test", "ci", "staging-test") and any(
        m in host for m in PROD_HOST_MARKERS
    ):
        return _fail(f"production hostname protection: {host!r}")

    # Denylist self-check: M1.1 smoke URL list must not contain unsafe routes.
    smoke_paths = [
        "/health/ready",
        "/api/storefront/resolve-slug/",
        "/api/storefront/store-config",
        "/api/storefront/categories",
        "/api/storefront/products",
        "/api/storefront/products/by-id/",
        "/api/storefront/search",
        "/api/storefront/search/suggest",
    ]
    for p in smoke_paths:
        low = p.lower()
        if any(u in low for u in UNSAFE_ROUTE_FRAGMENTS if u not in ("/checkout",)):
            return _fail(f"smoke path violates denylist: {p}")
    # Explicit guard: smoke must be GET-only (no checkout/cart writes in M1.1).
    if get("LOAD_SMOKE_WRITES", "").strip().lower() in ("1", "true", "yes"):
        return _fail("M1.1 supports READ-ONLY smoke only (LOAD_SMOKE_WRITES must be unset)")

    for flag in PROVIDER_EXEC_FLAGS:
        val = (get(flag) or "").strip().lower()
        if val in ("1", "true", "yes", "enabled", "on"):
            return _fail(f"provider execution flag set: {flag}={val!r} (no real provider actions in M1.1)")

    db_proof = (get("TEST_DB_PROOF") or "").strip()
    if not db_proof:
        return _fail("missing TEST_DB_PROOF (explicit test-database target proof required)")
    if any(m in db_proof.lower() for m in PROD_HOST_MARKERS):
        return _fail(f"TEST_DB_PROOF looks production: {db_proof!r}")

    print("PREFLIGHT OK: read-only smoke allowed "
          f"(env={test_env}, host={host}, db_proof_present=True)")
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Shopixy load-test safety gates.")
    parser.add_argument("--m3b-preflight", action="store_true",
                        help="M3-B execution safety gate (no bypass flag exists).")
    args, _unknown = parser.parse_known_args()
    # NOTE: argparse presence check is cosmetic; main() re-checks sys.argv so
    # the gate cannot be silently skipped by unknown-arg handling.
    sys.exit(main())

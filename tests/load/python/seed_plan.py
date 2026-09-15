"""M1.1 seed planner — PLAN ONLY.

Generates a deterministic proposed test universe (tenants, slugs, staff,
customers, products, probe entities, warehouses, stock budget placeholders).
Writes NO database state, performs NO network I/O, contains NO credentials.

Deterministic: same inputs -> same outputs (UUIDv5 from fixed namespace).
Outputs (optional): tests/load/data/universe.json + probes.json, clearly
marked NON-PRODUCTION TEST DATA. Rerunnable via --out-dir.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import uuid
from pathlib import Path

NAMESPACE = uuid.UUID("00000000-0000-4000-8000-000000000001")
MARKER = "NON-PRODUCTION TEST DATA — Shopixy load harness M1.1 plan only"

TENANT_KEYS = ("tenant-a", "tenant-b", "tenant-c")
SLUGS = {"tenant-a": "store-a", "tenant-b": "store-b", "tenant-c": "store-c"}
PRICES = {"tenant-a": "111.11", "tenant-b": "222.22", "tenant-c": "333.33"}


def det_guid(*parts: str) -> str:
    return str(uuid.uuid5(NAMESPACE, "|".join(parts)))


def build_universe() -> dict:
    tenants = []
    for key in TENANT_KEYS:
        slug = SLUGS[key]
        tenant_id = det_guid("tenant", key)
        suffix = hashlib.sha1(key.encode()).hexdigest()[:4]
        tenants.append(
            {
                "tenantKey": key,
                "tenantId": tenant_id,
                "storeId": tenant_id,  # Tenant == Store convention
                "slug": slug,
                "staff": [
                    {"userKey": f"user-{key[-1]}-00{i}", "role": role, "userId": det_guid("user", key, str(i))}
                    for i, role in enumerate(("Owner", "Admin", "Cashier"), start=1)
                ],
                "customers": [
                    {
                        "customerKey": f"customer-{key[-1]}-00{i}",
                        "customerId": det_guid("customer", key, str(i)),
                    }
                    for i in range(1, 6)
                ],
                "categories": [
                    {"slug": f"cat-{key[-1]}-{i}", "categoryId": det_guid("category", key, str(i))}
                    for i in range(1, 5)
                ],
                "products": [
                    {
                        "productKey": f"product-{key[-1]}-{i:03d}",
                        "productId": det_guid("product", key, str(i)),
                        "slug": f"product-{key[-1]}-{i:03d}",
                        "sku": f"SKU-{key[-1].upper()}-{i:03d}",
                        "variants": [
                            det_guid("variant", key, str(i), "v1"),
                            det_guid("variant", key, str(i), "v2"),
                        ],
                    }
                    for i in range(1, 21)
                ],
                "probeProduct": {
                    "name": f"TEST-PROBE-{key.upper().replace('-', '_')}",
                    "slug": f"test-probe-{key}-{suffix}",
                    "sku": f"SKU-PROBE-{key[-1].upper()}-{suffix}",
                    "productId": det_guid("probe-product", key),
                    "priceFingerprint": PRICES[key],
                },
                "warehouses": [
                    {
                        "warehouseKey": f"warehouse-{key[-1]}-{i}",
                        "warehouseId": det_guid("warehouse", key, str(i)),
                        "stockBudget": {"plentiful": 10000, "contended": 20, "zero": 0}[
                            ("plentiful", "contended")[i - 1]
                        ],
                    }
                    for i in (1, 2)
                ],
            }
        )
    return {"marker": MARKER, "tenants": tenants}


def build_probes(universe: dict) -> dict:
    return {
        "marker": MARKER,
        "probes": [
            {
                "tenantKey": t["tenantKey"],
                "probeName": t["probeProduct"]["name"],
                "probeSlug": t["probeProduct"]["slug"],
                "probeSku": t["probeProduct"]["sku"],
                "probeProductId": t["probeProduct"]["productId"],
            }
            for t in universe["tenants"]
        ],
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="M1.1 deterministic seed planner (no writes).")
    parser.add_argument("--out-dir", default="", help="Optional dir for universe.json + probes.json.")
    parser.add_argument("--stdout", action="store_true", help="Print universe JSON to stdout.")
    args = parser.parse_args(argv)

    universe = build_universe()
    probes = build_probes(universe)

    if args.out_dir:
        out = Path(args.out_dir)
        out.mkdir(parents=True, exist_ok=True)
        (out / "universe.json").write_text(json.dumps(universe, indent=2), encoding="utf-8")
        (out / "probes.json").write_text(json.dumps(probes, indent=2), encoding="utf-8")
        print(f"wrote {out / 'universe.json'} and {out / 'probes.json'} [{MARKER}]")
    if args.stdout or not args.out_dir:
        print(json.dumps(universe, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())

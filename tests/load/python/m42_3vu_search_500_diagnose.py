from __future__ import annotations

import base64
import hashlib
import json
import os
from pathlib import Path
import re
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone


ROOT = Path(__file__).resolve().parents[3]
RESULTS = ROOT / "tests/load/results"
OUT = RESULTS / "m42-3vu-search-500-diagnostic.json"
CONFIG = ROOT / "PointOfSale.API/appsettings.Development.local.json"
R3_LOG = RESULTS / "m42-newenv-r3-3-a.log"
R3_ANALYSIS = RESULTS / "m42-newenv-r3-3-a-analysis.json"
R3_SUMMARY = RESULTS / "m42-m42-newenv-r3-3-a-summary.json"
M41_LOGS = [RESULTS / "m41-newenv-a.log", RESULTS / "m41-newenv-b.log"]

TENANT_C = "6b9c21af-8eba-5b8a-87d7-da4a40263717"
HOST_C = "loadtest-tenant-c.localhost:5174"
QUERY = "LT-PROBE-C"
FAILED_REQUEST_ID = "47e0d8a2-2b4b-4da2-a472-20d5cb9b15a4"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def millis(start: float) -> float:
    return round((time.perf_counter() - start) * 1000.0, 3)


def fnv1a_body_hash(body: str | bytes | None) -> str:
    if body is None:
        return "empty"
    if isinstance(body, bytes):
        text = body.decode("utf-8", errors="replace")
    else:
        text = body
    value = 0x811C9DC5
    for ch in text:
        value ^= ord(ch)
        value = (value * 0x01000193) & 0xFFFFFFFF
    return f"{value:08x}:{len(text)}"


def extract_json_message(record: dict, prefix: str) -> dict | None:
    msg = record.get("msg", "")
    marker = prefix + " "
    if not msg.startswith(marker):
        return None
    return json.loads(msg[len(marker):])


def parse_log_line(line: str) -> dict | None:
    try:
        return json.loads(line)
    except json.JSONDecodeError:
        return None


def recover_r3_context() -> dict:
    records = []
    contexts = {}
    previous_steps = []
    following_steps = []
    failure = None
    violation = None
    seen_failure = False
    with R3_LOG.open(encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, start=1):
            record = parse_log_line(line)
            if not record:
                continue
            ctx = extract_json_message(record, "M42-CTX")
            if ctx:
                contexts[(str(ctx.get("vuId")), int(ctx.get("iteration", -1)))] = ctx
            step = extract_json_message(record, "M42-STEP")
            if step:
                if step.get("requestId") == FAILED_REQUEST_ID:
                    failure = {
                        "line": line_number,
                        "record": record,
                        "step": step,
                        "ctx": contexts.get((str(step.get("vuId")), int(step.get("iteration", -1))))
                    }
                    seen_failure = True
                elif not seen_failure:
                    previous_steps.append({"line": line_number, "step": step})
                    previous_steps = previous_steps[-5:]
                else:
                    following_steps.append({"line": line_number, "step": step})
                    following_steps = following_steps[:5]
            if FAILED_REQUEST_ID in record.get("msg", "") and record.get("level") == "error":
                match = re.search(r"M4-VIOLATION (\{.*\})", record.get("msg", ""))
                if match:
                    violation = {"line": line_number, "record": record, "violation": json.loads(match.group(1))}
            records.append(record)
    analysis = json.loads(R3_ANALYSIS.read_text(encoding="utf-8"))
    summary = json.loads(R3_SUMMARY.read_text(encoding="utf-8"))
    return {
        "failure": failure,
        "violation": violation,
        "previous_steps": previous_steps,
        "following_steps": following_steps,
        "analysis_metrics": {
            "statuses": analysis.get("statuses"),
            "checks": analysis.get("checks"),
            "counters": analysis.get("counters"),
            "thresholds": analysis.get("thresholds"),
            "transport_status_zero": analysis.get("transport_status_zero"),
        },
        "summary_identity": {
            "runId": summary.get("runId"),
            "stageVus": summary.get("stageVus"),
            "manifest": summary.get("manifest"),
        },
        "response_body_captured": False,
        "response_body_evidence": "k6 workload logs recorded only responseHash; body text is not present in R3 artifacts.",
    }


def parse_m41() -> dict:
    exact = []
    for path in M41_LOGS:
        if not path.exists():
            continue
        raw = path.read_bytes()
        candidates = [raw.decode("utf-8", errors="replace"), raw.decode("utf-16-le", errors="replace")]
        text = max(candidates, key=lambda value: value.count(f"/api/storefront/search?q={QUERY}"))
        pattern = re.compile(
            r'path\\":\\"/api/storefront/search\?q=' + re.escape(QUERY) +
            r'\\",\s*\\"status\\":(?P<status>\d+),\s*\\"latencyMs\\":\s*(?P<latency>\d+)',
            re.DOTALL,
        )
        for match in pattern.finditer(text):
            exact.append({
                "file": str(path.relative_to(ROOT)),
                "step": {
                    "path": f"/api/storefront/search?q={QUERY}",
                    "status": int(match.group("status")),
                    "latencyMs": int(match.group("latency")),
                }
            })
    statuses = {}
    for item in exact:
        status = str(item["step"].get("status"))
        statuses[status] = statuses.get(status, 0) + 1
    latencies = [item["step"].get("latencyMs") for item in exact if isinstance(item["step"].get("latencyMs"), (int, float))]
    return {
        "exact_query_count": len(exact),
        "status_counts": statuses,
        "latency_ms": {
            "min": min(latencies) if latencies else None,
            "max": max(latencies) if latencies else None,
            "mean": sum(latencies) / len(latencies) if latencies else None,
        },
        "decode_note": "M4.1 logs are UTF-16 console captures; parser tolerates line wrapping inside escaped JSON.",
        "examples": exact[:10],
    }


def json_body_status(value):
    return value.get("status") if isinstance(value, dict) else None


def opener_no_proxy():
    return urllib.request.build_opener(urllib.request.ProxyHandler({}))


def manual_attempts() -> list[dict]:
    attempts = []
    url = "http://localhost:5010/api/storefront/search?" + urllib.parse.urlencode({"q": QUERY})
    opener = opener_no_proxy()
    for index in range(1, 6):
        request_id = f"diag-m42-search500-{index}-{int(time.time() * 1000)}"
        headers = {
            "Host": HOST_C,
            "X-Tenant-Id": TENANT_C,
            "X-Session-Id": f"sess-loadtest-tenant-c-vu-3-iter-diag-{index}",
            "X-Request-Id": request_id,
            "X-Correlation-Id": request_id,
            "Accept": "application/json",
        }
        req = urllib.request.Request(url, headers=headers, method="GET")
        started = utc_now()
        start = time.perf_counter()
        body = b""
        status = None
        response_headers = {}
        error = None
        try:
            with opener.open(req, timeout=30) as response:
                status = response.status
                body = response.read()
                response_headers = dict(response.headers.items())
        except urllib.error.HTTPError as exc:
            status = exc.code
            body = exc.read()
            response_headers = dict(exc.headers.items())
        except Exception as exc:
            error = str(exc)
        attempts.append({
            "attempt": index,
            "timestamp_utc": started,
            "status": status,
            "latency_ms": millis(start),
            "request_id": request_id,
            "headers_sent": headers,
            "response_hash": fnv1a_body_hash(body),
            "response_body": body.decode("utf-8", errors="replace"),
            "response_headers": response_headers,
            "error": error,
        })
        time.sleep(0.25)
    return attempts


def config() -> dict:
    return json.loads(CONFIG.read_text(encoding="utf-8"))


def os_request(cfg: dict, path: str, method: str = "GET", body: dict | None = None) -> dict:
    es = cfg["Elasticsearch"]
    url = es["Url"].rstrip("/") + path
    headers = {"Accept": "application/json"}
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    if es.get("Username") or es.get("Password"):
        token = base64.b64encode(f"{es.get('Username', '')}:{es.get('Password', '')}".encode()).decode()
        headers["Authorization"] = "Basic " + token
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    context = ssl._create_unverified_context()
    start = time.perf_counter()
    result = {"path": path, "method": method, "timestamp_utc": utc_now(), "success": False}
    try:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))
        with opener.open(req, timeout=30) as response:
            raw = response.read()
            result.update({"success": 200 <= response.status < 300, "status": response.status, "latency_ms": millis(start), "body": json.loads(raw.decode("utf-8")) if raw else None})
    except urllib.error.HTTPError as exc:
        raw = exc.read()
        result.update({"status": exc.code, "latency_ms": millis(start), "body": raw.decode("utf-8", errors="replace")})
    except Exception as exc:
        result.update({"latency_ms": millis(start), "error": str(exc)})
    return result


def opensearch_diagnostics(cfg: dict) -> dict:
    products_index = cfg["Elasticsearch"].get("ProductsIndexName", "products_loadtest")
    unified_query = {
        "from": 0,
        "size": 20,
        "query": {
            "function_score": {
                "query": {
                    "bool": {
                        "must": [
                            {"multi_match": {"query": QUERY, "fields": ["name^4", "nameAr^2", "searchText^2", "description", "slug"], "type": "best_fields", "fuzziness": "AUTO"}}
                        ],
                        "filter": [
                            {"term": {"tenantId": TENANT_C}},
                            {"term": {"isActive": True}},
                            {"term": {"isOnlineVisible": True}}
                        ]
                    }
                },
                "functions": [
                    {"weight": 5.0, "filter": {"term": {"hasActivePromotion": True}}},
                    {"weight": 10.0, "filter": {"bool": {"must": [{"term": {"hasActivePromotion": True}}, {"range": {"promotionDiscountValue": {"gte": 20}}}]}}},
                    {"field_value_factor": {"field": "searchBoostOverride", "factor": 1.0, "modifier": "none", "missing": 0}}
                ],
                "score_mode": "sum",
                "boost_mode": "sum"
            }
        },
        "track_total_hits": True,
        "sort": ["_score"]
    }
    sku_query = {
        "size": 10,
        "query": {
            "bool": {
                "filter": [
                    {"term": {"tenantId": TENANT_C}},
                    {"term": {"sku": QUERY}}
                ]
            }
        }
    }
    return {
        "endpoint": {"url": cfg["Elasticsearch"]["Url"], "products_index": products_index, "auth_present": bool(cfg["Elasticsearch"].get("Username") or cfg["Elasticsearch"].get("Password"))},
        "cluster_health": os_request(cfg, "/_cluster/health"),
        "products_count": os_request(cfg, f"/{products_index}/_count"),
        "products_index_health": os_request(cfg, f"/_cluster/health/{products_index}"),
        "exact_sku_search": os_request(cfg, f"/{products_index}/_search", method="POST", body=sku_query),
        "app_like_unified_search_products_index": os_request(cfg, f"/{products_index}/_search", method="POST", body=unified_query),
    }


def health_context() -> dict:
    paths = [
        RESULTS / "m42-newenv-r3-initial-health.json",
        RESULTS / "m42-newenv-r3-3-a-pre-health.json",
        RESULTS / "m42-newenv-r3-3-a-post-health.json",
        RESULTS / "m42-newenv-r3-initial-redis-functional.json",
        RESULTS / "m42-newenv-r3-3-a-pre-redis-functional.json",
        RESULTS / "m42-newenv-r3-3-a-post-redis-functional.json",
    ]
    out = {}
    for path in paths:
        if path.exists():
            data = json.loads(path.read_text(encoding="utf-8"))
            out[path.name] = data
    return out


def main() -> int:
    if OUT.exists():
        raise RuntimeError(f"Refusing to overwrite {OUT}")
    cfg = config()
    data = {
        "created_utc": utc_now(),
        "scope": "NO_K6_NO_CAPACITY_LOAD_READ_ONLY_DIAGNOSTIC",
        "failed_request_id": FAILED_REQUEST_ID,
        "r3_context": recover_r3_context(),
        "manual_reproduction": manual_attempts(),
        "opensearch": opensearch_diagnostics(cfg),
        "m41_comparison": parse_m41(),
        "health_context_files": health_context(),
        "notes": [
            "Manual reproduction uses GET only against the same API route with Host, X-Tenant-Id, X-Session-Id, X-Request-Id, and X-Correlation-Id headers.",
            "OpenSearch diagnostics are read-only cluster/index/search/count requests.",
            "No k6 process is launched by this helper."
        ]
    }
    with OUT.open("x", encoding="utf-8") as stream:
        json.dump(data, stream, indent=2)
    print(json.dumps({
        "out": str(OUT.relative_to(ROOT)),
        "manual_statuses": [attempt["status"] for attempt in data["manual_reproduction"]],
        "opensearch_cluster": json_body_status(data["opensearch"]["cluster_health"].get("body")),
        "m41_exact_query_count": data["m41_comparison"]["exact_query_count"],
        "m41_status_counts": data["m41_comparison"]["status_counts"],
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

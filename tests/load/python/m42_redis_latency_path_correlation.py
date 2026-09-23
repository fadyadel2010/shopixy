from __future__ import annotations

import importlib.util
import json
import socket
import statistics
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
RESULTS = ROOT / "tests/load/results"
OUT = RESULTS / "m42-redis-latency-path-correlation.json"
HELPER = ROOT / "tests/load/python/m42_redis_health_investigate.py"

spec = importlib.util.spec_from_file_location("redis_diag", HELPER)
diag = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(diag)


def percentile_nearest(values: list[float], pct: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    index = max(0, min(len(ordered) - 1, int(len(ordered) * pct + 0.999999) - 1))
    return ordered[index]


def stats(values: list[float]) -> dict:
    values = [float(v) for v in values if isinstance(v, (int, float))]
    if not values:
        return {"count": 0}
    return {
        "count": len(values),
        "median": statistics.median(values),
        "p95_nearest_rank": percentile_nearest(values, 0.95),
        "max": max(values),
        "min": min(values),
    }


def tcp_probe(config: dict, index: int) -> dict:
    record = {"index": index, "timestamp_utc": diag.utc_now(), "success": False}
    sock = None
    start = time.perf_counter()
    try:
        sock = socket.create_connection(
            (config["host"], config["port"]),
            timeout=(config.get("connect_timeout_ms") or 5000) / 1000.0,
        )
        record["success"] = True
    except Exception as exc:
        record["error"] = str(exc)
    finally:
        record["tcp_connect_ms"] = diag.millis(start)
        if sock is not None:
            sock.close()
    return record


def routing_probe(config: dict, index: int) -> dict:
    record = {"index": index, "timestamp_utc": diag.utc_now(), "success": False}
    sock = None
    try:
        sock, timings = diag.open_redis(config)
        record["connection_timings_ms"] = timings
        start = time.perf_counter()
        role = diag.redis_command(sock, "ROLE")
        record["role_latency_ms"] = diag.millis(start)
        record["raw_role"] = role
        record["role"] = role[0] if isinstance(role, list) and role else None
        start = time.perf_counter()
        info = diag.redis_command(sock, "INFO", "replication")
        record["info_latency_ms"] = diag.millis(start)
        fields = {}
        for line in str(info).splitlines():
            if ":" in line and not line.startswith("#"):
                key, value = line.split(":", 1)
                fields[key] = value
        record["info_role"] = fields.get("role")
        record["master_replid"] = fields.get("master_replid")
        record["master_repl_offset"] = fields.get("master_repl_offset")
        record["connected_slaves"] = fields.get("connected_slaves")
        record["success"] = record["role"] == "master" and record["info_role"] == "master"
    except Exception as exc:
        record["error"] = str(exc)
    finally:
        if sock is not None:
            sock.close()
    return record


def flatten(sample: dict) -> dict:
    health = sample["api_health"]
    states = health.get("states") or {}
    redis_latency = states.get("redis_latency") or {}
    direct = sample["direct_redis"]
    timings = direct.get("timings_ms") or {}
    direct_command_sum = sum(
        float(timings.get(name, 0.0))
        for name in ("ping_ms", "set_ms", "get1_ms", "incr_ms", "get2_ms", "del_ms")
    )
    api_latency = redis_latency.get("reported_latency_ms")
    tcp_ms = sample["tcp_connect"].get("tcp_connect_ms")
    return {
        "index": sample["index"],
        "timestamp_utc": sample["timestamp_utc"],
        "api_http_status": health.get("http_status"),
        "api_health_duration_ms": health.get("elapsed_ms"),
        "api_redis_state": (states.get("redis") or {}).get("status"),
        "api_redis_latency_state": redis_latency.get("status"),
        "api_redis_latency_ms": api_latency,
        "postgresql": (states.get("postgresql") or {}).get("status"),
        "mongodb": (states.get("mongodb") or {}).get("status"),
        "elasticsearch": (states.get("elasticsearch") or {}).get("status"),
        "rabbitmq": (states.get("rabbitmq") or {}).get("status"),
        "masstransit_bus": (states.get("masstransit-bus") or {}).get("status"),
        "tcp_connect_success": sample["tcp_connect"].get("success"),
        "tcp_connect_ms": tcp_ms,
        "direct_success": direct.get("success"),
        "direct_total_ms": direct.get("total_cycle_ms"),
        "direct_tcp_connect_ms": timings.get("tcp_connect_ms"),
        "direct_auth_ms": timings.get("auth_ms"),
        "direct_ping_ms": timings.get("ping_ms"),
        "direct_set_ms": timings.get("set_ms"),
        "direct_get1_ms": timings.get("get1_ms"),
        "direct_incr_ms": timings.get("incr_ms"),
        "direct_get2_ms": timings.get("get2_ms"),
        "direct_del_ms": timings.get("del_ms"),
        "direct_command_sum_ms": round(direct_command_sum, 3),
        "delta_api_minus_direct_command_sum_ms": round(float(api_latency) - direct_command_sum, 3) if isinstance(api_latency, (int, float)) else None,
        "delta_api_minus_tcp_connect_ms": round(float(api_latency) - float(tcp_ms), 3) if isinstance(api_latency, (int, float)) and isinstance(tcp_ms, (int, float)) else None,
    }


def main() -> int:
    if OUT.exists():
        raise RuntimeError(f"Refusing to overwrite {OUT}")
    config = diag.redis_config()
    data = {
        "started_utc": diag.utc_now(),
        "health_implementation": {
            "source": "PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs:1249-1342",
            "operation": "StringSetAsync with 10s TTL, StringGetAsync, KeyDeleteAsync measured by application-side Stopwatch",
            "healthy_below_ms": 50,
            "degraded_at_or_above_ms": 50,
            "unhealthy_at_or_above_ms": 200,
        },
        "redis_endpoint": diag.redacted_config(config),
        "cadence_seconds": 5,
        "planned_samples": 36,
        "process_start": diag.proc_snapshot(),
        "samples": [],
    }
    for i in range(36):
        sample_start = time.perf_counter()
        sample = {"index": i + 1, "timestamp_utc": diag.utc_now()}
        sample["api_health"] = diag.health_sample(i + 1)
        sample["tcp_connect"] = tcp_probe(config, i + 1)
        sample["direct_redis"] = diag.direct_cycle(config, i + 1)
        sample["finished_utc"] = diag.utc_now()
        sample["sample_elapsed_ms"] = diag.millis(sample_start)
        sample["aligned"] = flatten(sample)
        data["samples"].append(sample)
        if i != 35:
            time.sleep(max(0.0, 5.0 - (time.perf_counter() - sample_start)))

    data["process_after_samples"] = diag.proc_snapshot()
    data["routing_probes"] = [routing_probe(config, i + 1) for i in range(5)]
    data["process_end"] = diag.proc_snapshot()

    aligned = [sample["aligned"] for sample in data["samples"]]
    api_lat = [row["api_redis_latency_ms"] for row in aligned if isinstance(row["api_redis_latency_ms"], (int, float))]
    tcp_lat = [row["tcp_connect_ms"] for row in aligned if isinstance(row["tcp_connect_ms"], (int, float))]
    direct_total = [row["direct_total_ms"] for row in aligned if isinstance(row["direct_total_ms"], (int, float))]
    direct_ping = [row["direct_ping_ms"] for row in aligned if isinstance(row["direct_ping_ms"], (int, float))]
    deltas_cmd = [row["delta_api_minus_direct_command_sum_ms"] for row in aligned if isinstance(row["delta_api_minus_direct_command_sum_ms"], (int, float))]
    deltas_tcp = [row["delta_api_minus_tcp_connect_ms"] for row in aligned if isinstance(row["delta_api_minus_tcp_connect_ms"], (int, float))]
    direct_component_stats = {}
    for field in ("direct_set_ms", "direct_get1_ms", "direct_incr_ms", "direct_get2_ms", "direct_del_ms", "direct_command_sum_ms"):
        direct_component_stats[field] = stats([row[field] for row in aligned if isinstance(row[field], (int, float))])
    data["summary"] = {
        "sample_count": len(aligned),
        "api_redis_latency_ms": stats(api_lat),
        "tcp_connect_ms": stats(tcp_lat),
        "direct_total_cycle_ms": stats(direct_total),
        "direct_ping_ms": stats(direct_ping),
        "direct_components_ms": direct_component_stats,
        "api_redis_latency_ge_50_count": sum(1 for value in api_lat if value >= 50),
        "direct_total_ge_50_count": sum(1 for value in direct_total if value >= 50),
        "tcp_connect_ge_50_count": sum(1 for value in tcp_lat if value >= 50),
        "delta_api_minus_direct_command_sum_ms": stats(deltas_cmd),
        "delta_api_minus_tcp_connect_ms": stats(deltas_tcp),
        "api_redis_latency_states": {state: [row["api_redis_latency_state"] for row in aligned].count(state) for state in sorted(set(row["api_redis_latency_state"] for row in aligned), key=str)},
        "other_dependency_states": {
            name: {state: [row[name] for row in aligned].count(state) for state in sorted(set(row[name] for row in aligned), key=str)}
            for name in ("postgresql", "mongodb", "elasticsearch", "rabbitmq", "masstransit_bus")
        },
        "routing_roles": [probe.get("role") for probe in data["routing_probes"]],
        "routing_info_roles": [probe.get("info_role") for probe in data["routing_probes"]],
        "routing_master_replids": sorted({probe.get("master_replid") for probe in data["routing_probes"] if probe.get("master_replid")}),
        "routing_all_success": all(probe.get("success") for probe in data["routing_probes"]),
    }
    data["finished_utc"] = diag.utc_now()
    with OUT.open("x", encoding="utf-8") as stream:
        json.dump(data, stream, indent=2)
    print(json.dumps(data["summary"], indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

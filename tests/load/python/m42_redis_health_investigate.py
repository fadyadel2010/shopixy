from __future__ import annotations

import json
import os
from pathlib import Path
import socket
import statistics
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

import psutil


ROOT = Path(__file__).resolve().parents[3]
RESULTS = ROOT / "tests/load/results"
OUT = RESULTS / "m42-redis-health-stability-samples.json"
HEALTH_URL = "http://localhost:5010/health"
CONFIG = ROOT / "PointOfSale.API/appsettings.Development.local.json"
HEALTH_DEPS = [
    "postgresql",
    "mongodb",
    "redis",
    "redis_latency",
    "elasticsearch",
    "rabbitmq",
    "masstransit-bus",
]


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def millis(start: float) -> float:
    return round((time.perf_counter() - start) * 1000.0, 3)


def parse_redis_connection_string(value: str) -> dict:
    parts = [p for p in value.split(",") if p]
    host, port = parts[0].rsplit(":", 1)
    result = {"host": host, "port": int(port), "password": None, "db": 0}
    for part in parts[1:]:
        if "=" not in part:
            continue
        key, raw = part.split("=", 1)
        key = key.strip().lower()
        if key == "password":
            result["password"] = raw
        elif key == "defaultdatabase":
            result["db"] = int(raw)
    return result


def redis_config() -> dict:
    config = json.loads(CONFIG.read_text(encoding="utf-8"))["Redis"]
    parsed = parse_redis_connection_string(config["ConnectionString"])
    return {
        **parsed,
        "instance_name": config.get("InstanceName"),
        "connect_timeout_ms": config.get("ConnectTimeoutMs"),
        "sync_timeout_ms": config.get("SyncTimeoutMs"),
        "enabled": config.get("Enabled"),
    }


def redacted_config(config: dict) -> dict:
    return {
        "host": config["host"],
        "port": config["port"],
        "db": config["db"],
        "password_present": bool(config.get("password")),
        "instance_name": config.get("instance_name"),
        "connect_timeout_ms": config.get("connect_timeout_ms"),
        "sync_timeout_ms": config.get("sync_timeout_ms"),
        "enabled": config.get("enabled"),
    }


def encode_command(*parts: object) -> bytes:
    out = [f"*{len(parts)}\r\n".encode("ascii")]
    for part in parts:
        data = str(part).encode("utf-8")
        out.append(f"${len(data)}\r\n".encode("ascii"))
        out.append(data)
        out.append(b"\r\n")
    return b"".join(out)


def read_line(sock: socket.socket) -> bytes:
    chunks = []
    while True:
        ch = sock.recv(1)
        if not ch:
            raise ConnectionError("Redis socket closed")
        chunks.append(ch)
        if len(chunks) >= 2 and chunks[-2:] == [b"\r", b"\n"]:
            return b"".join(chunks[:-2])


def read_resp(sock: socket.socket):
    prefix = sock.recv(1)
    if not prefix:
        raise ConnectionError("Redis socket closed")
    if prefix in (b"+", b"-", b":"):
        line = read_line(sock).decode("utf-8", errors="replace")
        if prefix == b"-":
            raise RuntimeError("Redis error: " + line)
        if prefix == b":":
            return int(line)
        return line
    if prefix == b"$":
        length = int(read_line(sock))
        if length == -1:
            return None
        data = b""
        while len(data) < length:
            data += sock.recv(length - len(data))
        trailer = sock.recv(2)
        if trailer != b"\r\n":
            raise RuntimeError("Invalid Redis bulk trailer")
        return data.decode("utf-8", errors="replace")
    if prefix == b"*":
        count = int(read_line(sock))
        if count == -1:
            return None
        return [read_resp(sock) for _ in range(count)]
    raise RuntimeError("Unexpected Redis response prefix: " + repr(prefix))


def redis_command(sock: socket.socket, *parts: object):
    sock.sendall(encode_command(*parts))
    return read_resp(sock)


def open_redis(config: dict) -> tuple[socket.socket, dict]:
    timings = {}
    start = time.perf_counter()
    sock = socket.create_connection(
        (config["host"], config["port"]),
        timeout=(config.get("connect_timeout_ms") or 5000) / 1000.0,
    )
    sock.settimeout((config.get("sync_timeout_ms") or 1000) / 1000.0)
    timings["tcp_connect_ms"] = millis(start)
    if config.get("password"):
        start = time.perf_counter()
        redis_command(sock, "AUTH", config["password"])
        timings["auth_ms"] = millis(start)
    if config.get("db", 0) != 0:
        start = time.perf_counter()
        redis_command(sock, "SELECT", config["db"])
        timings["select_ms"] = millis(start)
    else:
        timings["select_ms"] = 0.0
    return sock, timings


def direct_cycle(config: dict, index: int) -> dict:
    key = f"diagnostic:m42:redis-health:{os.getpid()}:{int(time.time() * 1000)}:{index}"
    record = {"index": index, "timestamp_utc": utc_now(), "key_prefix": "diagnostic:m42:redis-health", "success": False}
    sock = None
    total = time.perf_counter()
    try:
        sock, timings = open_redis(config)
        start = time.perf_counter()
        pong = redis_command(sock, "PING")
        timings["ping_ms"] = millis(start)
        start = time.perf_counter()
        redis_command(sock, "SET", key, "0", "EX", "60")
        timings["set_ms"] = millis(start)
        start = time.perf_counter()
        first_get = redis_command(sock, "GET", key)
        timings["get1_ms"] = millis(start)
        start = time.perf_counter()
        incr = redis_command(sock, "INCR", key)
        timings["incr_ms"] = millis(start)
        start = time.perf_counter()
        second_get = redis_command(sock, "GET", key)
        timings["get2_ms"] = millis(start)
        start = time.perf_counter()
        deleted = redis_command(sock, "DEL", key)
        timings["del_ms"] = millis(start)
        record.update({
            "success": pong == "PONG" and first_get == "0" and incr == 1 and second_get == "1" and deleted == 1,
            "ping_response": pong,
            "set_value": "0",
            "get1_value": first_get,
            "incr_value": incr,
            "get2_value": second_get,
            "deleted": deleted,
            "timings_ms": timings,
        })
    except Exception as exc:
        record["error"] = str(exc)
    finally:
        if sock is not None:
            try:
                sock.close()
            except Exception:
                pass
        record["total_cycle_ms"] = millis(total)
    return record


def role_probe(config: dict, index: int) -> dict:
    record = {"index": index, "timestamp_utc": utc_now(), "success": False}
    sock = None
    try:
        sock, timings = open_redis(config)
        start = time.perf_counter()
        role = redis_command(sock, "ROLE")
        timings["role_ms"] = millis(start)
        record["timings_ms"] = timings
        record["raw_role"] = role
        record["role"] = role[0] if isinstance(role, list) and role else None
        record["master_replid"] = role[1] if isinstance(role, list) and len(role) > 1 else None
        record["master_offset"] = role[2] if isinstance(role, list) and len(role) > 2 else None
        record["success"] = record["role"] == "master"
    except Exception as exc:
        record["error"] = str(exc)
    finally:
        if sock is not None:
            try:
                sock.close()
            except Exception:
                pass
    return record


def health_sample(index: int) -> dict:
    evidence = {"index": index, "timestamp_utc": utc_now(), "url": HEALTH_URL}
    start = time.perf_counter()
    try:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        try:
            response = opener.open(HEALTH_URL, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            evidence["http_status"] = response.status
            body = json.load(response)
        evidence["elapsed_ms"] = millis(start)
        evidence["aggregate_status"] = body.get("status")
        entries = body.get("entries") or {}
        states = {}
        for dep in HEALTH_DEPS:
            item = entries.get(dep) or {}
            state = {"status": item.get("status"), "duration_ms": item.get("duration")}
            data = item.get("data") or {}
            if dep == "redis_latency":
                state["reported_latency_ms"] = data.get("LatencyMs")
                state["description"] = item.get("description")
            elif dep == "redis":
                state["reported_latency_ms"] = data.get("latencyMs")
                state["description"] = item.get("description")
            states[dep] = state
        evidence["states"] = states
    except Exception as exc:
        evidence["elapsed_ms"] = millis(start)
        evidence["error"] = str(exc)
    return evidence


def proc_snapshot() -> dict:
    rows = []
    for proc in psutil.process_iter(["pid", "ppid", "name", "create_time"]):
        try:
            name = proc.info.get("name") or ""
            cmd = " ".join(proc.cmdline())
            lower = (name + " " + cmd).lower()
            if any(token in lower for token in ("pointofsale.api", "k6", "dotnet test", "dotnet build", "msbuild", "vstest", "testhost", "wrk", "vegeta", "autocannon", "locust")):
                cpu = proc.cpu_times()
                rows.append({
                    "pid": proc.pid,
                    "ppid": proc.ppid(),
                    "name": name,
                    "command": cmd,
                    "created": proc.create_time(),
                    "cpu_seconds": round(cpu.user + cpu.system, 6),
                })
        except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
            continue
    listeners = []
    for conn in psutil.net_connections(kind="tcp"):
        if conn.status == psutil.CONN_LISTEN and conn.laddr and conn.laddr.port == 5010:
            listeners.append({"pid": conn.pid, "address": conn.laddr.ip, "port": conn.laddr.port})
    return {"timestamp_utc": utc_now(), "processes": rows, "listeners_5010": listeners}


def summarize_latencies(values: list[float]) -> dict:
    sorted_values = sorted(values)
    if not sorted_values:
        return {}
    p95_index = max(0, min(len(sorted_values) - 1, int(len(sorted_values) * 0.95 + 0.999999) - 1))
    return {
        "count": len(sorted_values),
        "min": sorted_values[0],
        "median": statistics.median(sorted_values),
        "p95_nearest_rank": sorted_values[p95_index],
        "max": sorted_values[-1],
    }


def max_run(values: list[str], wanted: str) -> int:
    best = current = 0
    for value in values:
        if value == wanted:
            current += 1
            best = max(best, current)
        else:
            current = 0
    return best


def main() -> int:
    if OUT.exists():
        raise RuntimeError(f"Refusing to overwrite {OUT}")
    config = redis_config()
    data = {
        "started_utc": utc_now(),
        "redis_config": redacted_config(config),
        "health_url": HEALTH_URL,
        "process_start": proc_snapshot(),
        "health_samples": [],
        "direct_redis_cycles": [],
        "role_probes": [],
    }

    for i in range(24):
        data["health_samples"].append(health_sample(i + 1))
        if i != 23:
            time.sleep(5)

    data["process_after_health"] = proc_snapshot()

    for i in range(20):
        data["direct_redis_cycles"].append(direct_cycle(config, i + 1))
        time.sleep(0.25)

    for i in range(5):
        data["role_probes"].append(role_probe(config, i + 1))
        time.sleep(0.25)

    data["process_end"] = proc_snapshot()

    states = [((sample.get("states") or {}).get("redis_latency") or {}).get("status") for sample in data["health_samples"]]
    latencies = [((sample.get("states") or {}).get("redis_latency") or {}).get("reported_latency_ms") for sample in data["health_samples"]]
    latencies = [float(v) for v in latencies if isinstance(v, (int, float))]
    totals = [float(c["total_cycle_ms"]) for c in data["direct_redis_cycles"] if c.get("success")]
    data["summary"] = {
        "health_state_counts": {state or "missing": states.count(state) for state in sorted(set(states), key=lambda x: str(x))},
        "redis_latency_ms": summarize_latencies(latencies),
        "direct_total_cycle_ms": summarize_latencies(totals),
        "max_consecutive_redis_latency_healthy": max_run(states, "Healthy"),
        "max_consecutive_redis_latency_degraded": max_run(states, "Degraded"),
        "role_probe_roles": [probe.get("role") for probe in data["role_probes"]],
        "role_probe_master_replids": sorted({probe.get("master_replid") for probe in data["role_probes"] if probe.get("master_replid")}),
        "direct_success_count": sum(1 for c in data["direct_redis_cycles"] if c.get("success")),
    }
    data["finished_utc"] = utc_now()

    with OUT.open("x", encoding="utf-8") as stream:
        json.dump(data, stream, indent=2)
    print(json.dumps(data["summary"], indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

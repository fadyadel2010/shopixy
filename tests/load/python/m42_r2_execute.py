from __future__ import annotations

import collections
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

import psutil

ROOT = Path(__file__).resolve().parents[3]
RESULTS = ROOT / "tests/load/results"
PREFIX = "m42-newenv-r2"
K6 = Path(r"C:\Program Files\k6\k6.exe")
FLAGS = "projectionReady publicationReady mongoVerified elasticsearchVerified storefrontVerified isolationProbesVerified stockBudgetVerified".split()
DEPENDENCIES = "postgresql mongodb redis redis_latency elasticsearch rabbitmq masstransit-bus".split()
COUNTERS = "m42_failed_requests m42_i1_violations m42_i2_violations m42_negative_probe_failures".split()
STAGES = (3, 5, 10, 25, 50, 100)
STATE = {"namespace": PREFIX, "attempts": [], "accepted": [], "status": "PREFLIGHT", "planned": [f"{PREFIX}-{v}-{r}" for v in STAGES for r in "ab"]}


def now():
    return datetime.now(timezone.utc).isoformat()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def save(path, value, exclusive=False):
    with path.open("x" if exclusive else "w", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2)
        stream.flush()
        os.fsync(stream.fileno())


def checkpoint():
    save(RESULTS / f"{PREFIX}-stages.json", STATE)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def frozen():
    paths = list((ROOT / "tests/load/k6").rglob("*"))
    paths += [ROOT / "tests/load/data/manifests/latest.json"]
    return {str(p.relative_to(ROOT)): digest(p) for p in paths if p.is_file()}


def integrity():
    require(frozen() == STATE["frozen"], "Frozen workload/manifest mutation")
    require(not git("status", "--porcelain", "--", "tests/load/k6/"), "k6 tree not clean")
    for path, value in STATE["historical"].items():
        require(digest(ROOT / path) == value, "Historical artifact changed: " + path)


def redact(command):
    return re.sub(r"(?i)((?:password|secret|token|api[-_]?key)\s*[=:]\s*)[^\s;]+", r"\1<redacted>", command)


def snapshot():
    result = []
    for p in psutil.process_iter():
        try:
            name = p.name()
            command = None
            try:
                command = " ".join(p.cmdline())
            except psutil.AccessDenied:
                require(not re.search(r"(?i)dotnet|msbuild|k6|node|python|java|testhost|vstest", name), "Cannot inspect candidate PID " + str(p.pid))
            cpu = p.cpu_times()
            result.append({"pid": p.pid, "ppid": p.ppid(), "created": p.create_time(), "name": name, "command": redact(command) if command else command, "cpu": cpu.user + cpu.system})
        except (psutil.NoSuchProcess, psutil.ZombieProcess):
            continue
    require(result, "Empty process snapshot")
    return result


def identity(p):
    return p["pid"], p["created"]


def is_msbuild(p):
    return "msbuild" in ((p["command"] or "") + p["name"]).lower()


def competitors(processes, baseline, expected=None):
    old = {identity(p): p for p in baseline}
    hits = []
    for p in processes:
        if expected is not None and identity(p) == expected:
            continue
        name = p["name"].lower()
        cmd = (p["command"] or "").lower()
        if p["pid"] == os.getpid():
            continue
        reason = None
        if name in {"k6.exe", "wrk.exe", "wrk2.exe", "hey.exe", "ab.exe", "oha.exe", "vegeta.exe", "locust.exe", "jmeter.exe", "bombardier.exe", "autocannon.exe", "testhost.exe", "vstest.console.exe"}:
            reason = "competing test/load executable"
        elif name in {"dotnet.exe", "msbuild.exe", "node.exe", "python.exe", "java.exe", "cmd.exe", "powershell.exe", "pwsh.exe"}:
            if re.search(r"\bdotnet(?:\.exe)?[\"']?\s+(?:test|build|msbuild(?!\.dll\b))\b|\b(?:benchmarkdotnet|jmeter|gatling|locust|autocannon|artillery)\b|\bnpm\s+(?:run\s+)?(?:build|test|bench|load)\b", cmd):
                reason = "competing test/build/benchmark command"
        if is_msbuild(p):
            previous = old.get(identity(p))
            if previous is None:
                reason = "new MSBuild process"
            elif cmd != (previous["command"] or "").lower() or p["cpu"] - previous["cpu"] > 0.1:
                reason = "existing MSBuild changed command or became active"
        if reason:
            hits.append({**p, "reason": reason})
    return hits


def listener(processes):
    owners = {c.pid for c in psutil.net_connections(kind="tcp") if c.status == psutil.CONN_LISTEN and c.laddr.port == 5010}
    require(len(owners) == 1 and None not in owners, "5010 must have exactly one known listener owner")
    api = [p for p in processes if p["pid"] in owners]
    require(len(api) == 1 and api[0]["name"].lower() == "pointofsale.api.exe", "Unexpected API listener process")
    require(api[0]["command"] and str(ROOT / "PointOfSale.API").lower() in api[0]["command"].lower(), "API executable outside intended workspace")
    require(len([p for p in processes if p["name"].lower() == "pointofsale.api.exe"]) == 1, "Multiple API instances")
    if "api" in STATE:
        require(identity(api[0]) == tuple(STATE["api"]), "API process identity changed")
    return api[0]


def validate_health(body):
    require(isinstance(body, dict) and isinstance(body.get("entries"), dict), "Health JSON entries missing")
    entries = body["entries"]
    states = {name: entries.get(name, {}).get("status") for name in DEPENDENCIES}
    require(all(value == "Healthy" for value in states.values()), "Relevant health gate failed: " + json.dumps(states))
    pg = entries["postgresql"].get("data", {})
    require(pg.get("host") == "192.168.1.190" and pg.get("database") == "PosEcommerceDb_LoadTest", "Live PostgreSQL target mismatch")
    require(entries["mongodb"].get("data", {}).get("database") == "ecommerce_read_db_loadtest", "Live Mongo target mismatch")
    require(entries["elasticsearch"].get("data", {}).get("url") == "https://192.168.1.190:9200", "Live search target mismatch")
    require(entries["rabbitmq"].get("data", {}).get("host") == "192.168.1.190", "Live RabbitMQ target mismatch")
    endpoints = entries["masstransit-bus"].get("data", {}).get("Endpoints")
    require(isinstance(endpoints, dict) and endpoints and all(e.get("status") == "Healthy" for e in endpoints.values()), "MassTransit endpoint health missing/unhealthy")
    return states


def health(label):
    evidence = {"started": now(), "url": "http://localhost:5010/health", "passed": False}
    try:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        try:
            response = opener.open(evidence["url"], timeout=30)
        except urllib.error.HTTPError as error:
            require(error.code == 503, "Unexpected health HTTP status " + str(error.code))
            response = error
        with response:
            evidence["http_status"] = response.status
            evidence["body"] = json.load(response)
        evidence["states"] = validate_health(evidence["body"])
        processes = snapshot()
        evidence["listener"] = listener(processes)
        evidence["states"]["API listener"] = "Healthy"
        evidence["passed"] = True
        return evidence
    except Exception as error:
        evidence["error"] = str(error)
        raise
    finally:
        evidence["finished"] = now()
        save(RESULTS / f"{label}-health.json", evidence, exclusive=True)


def analyze(rid, vus):
    summary = json.loads((RESULTS / f"m42-{rid}-summary.json").read_text(encoding="utf-8"))
    metrics = summary["metrics"]
    events = collections.defaultdict(list)
    warnings = []
    with (RESULTS / f"{rid}.log").open(encoding="utf-8") as stream:
        for line in stream:
            if not line.strip():
                continue
            record = json.loads(line)
            msg = record.get("msg", "")
            if record.get("level") in {"warning", "error"}:
                warnings.append(record)
            for marker in ("M42-VU-START", "M42-VU-END", "M42-STEP", "M42-DONE", "M42-CTX", "M42-MANIFEST"):
                if msg.startswith(marker + " "):
                    events[marker].append(json.loads(msg[len(marker) + 1:]))
    out = (RESULTS / f"{rid}.stdout.log").read_text(encoding="utf-8")
    interruption = re.findall(r"(\d+) complete and (\d+) interrupted iterations", out)
    steps = events["M42-STEP"]
    starts, ends = events["M42-VU-START"], events["M42-VU-END"]
    counts = {name: metrics[name]["values"]["count"] for name in COUNTERS}
    thresholds = {name: metric["thresholds"] for name, metric in metrics.items() if "thresholds" in metric}
    endpoints = {}
    for endpoint in sorted({s["endpoint"] for s in steps}):
        values = [s["latencyMs"] for s in steps if s["endpoint"] == endpoint]
        endpoints[endpoint] = {"count": len(values), "mean_ms": sum(values) / len(values), "max_ms": max(values)}
    result = {"run_id": rid, "vus": vus, "iterations_planned": vus * 3, "iterations": metrics["iterations"]["values"]["count"], "requests": metrics["http_reqs"]["values"]["count"], "checks": metrics["checks"]["values"], "counters": counts, "thresholds": thresholds, "statuses": dict(collections.Counter(str(s["status"]) for s in steps)), "negative_probes": len([s for s in steps if s["endpoint"] == "negative-by-id-foreign"]), "transport_status_zero": len([s for s in steps if s["status"] == 0]), "warnings_errors": warnings, "interruption_evidence": interruption, "vus_max": metrics["vus_max"]["values"]["max"], "vu_start_count": len(starts), "vu_end_count": len(ends), "overlap": bool(starts and ends and max(s["wallMs"] for s in starts) < min(e["wallMs"] for e in ends)), "request_rate": metrics["http_reqs"]["values"]["rate"], "latency": metrics["m42_http_latency_ms"]["values"], "journey": metrics["m42_journey_duration_ms"]["values"], "iteration_duration": metrics["iteration_duration"]["values"], "endpoint_observations": endpoints, "builtin_http_req_failed": metrics["http_req_failed"]["values"]}
    errors = []
    def gate(condition, message):
        if not condition:
            errors.append(message)
    gate(summary["runId"] == rid and summary["stageVus"] == vus, "Summary identity mismatch")
    gate(result["iterations"] == vus * 3 and result["requests"] == vus * 30, "Iteration/request count mismatch")
    gate(result["checks"]["passes"] == vus * 72 and result["checks"]["fails"] == 0, "Checks failed/missing")
    gate(all(v == 0 for v in counts.values()), "Custom counter failure")
    gate(set(thresholds) == {"checks", *COUNTERS} and all(t.get("ok") is True for value in thresholds.values() for t in value.values()), "Threshold failure/missing")
    gate(bool(interruption) and interruption[-1] == (str(vus * 3), "0"), "Interruption evidence failed/missing")
    expected_vus = {str(v) for v in range(1, vus + 1)}
    gate(len(starts) == len(ends) == vus and {s["vuId"] for s in starts} == {s["vuId"] for s in ends} == expected_vus and result["overlap"] and result["vus_max"] == vus, "Concurrency gate failed")
    gate(len(steps) == vus * 30 and len(events["M42-DONE"]) == vus * 3, "Incomplete log census/journeys")
    gate(len({s["requestId"] for s in steps}) == vus * 30, "Duplicate/missing request IDs")
    gate(all(s["method"] == "GET" and s["i1"] == "pass" for s in steps), "Method/isolation failure")
    neg = [s for s in steps if s["endpoint"] == "negative-by-id-foreign"]
    gate(len(neg) == vus * 3 and all(s["status"] == 404 and s["negativeProbe"] == s["negFingerprint"] == "pass" for s in neg), "Negative probe failure")
    gate(all(200 <= s["status"] < 400 for s in steps if s["endpoint"] != "negative-by-id-foreign"), "Positive status failure")
    gate(not warnings, "k6 warning/error observed")
    for vu in expected_vus:
        for iteration in range(3):
            own = [s for s in steps if s["vuId"] == vu and s["iteration"] == iteration]
            gate(len(own) == 10 and len({s["endpoint"] for s in own}) == 10, "Journey step census mismatch")
    result["gate_errors"] = errors
    save(RESULTS / f"{rid}-analysis.json", result, exclusive=True)
    return result


def stop_process(process):
    if process.poll() is None:
        process.send_signal(signal.CTRL_BREAK_EVENT)
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=10)


def cleanup(hits):
    for hit in hits:
        try:
            p = psutil.Process(hit["pid"])
            require(p.create_time() == hit["created"], "Contaminant PID reused; refusing cleanup")
            require(hit["pid"] not in {os.getpid(), STATE["api"][0]}, "Protected process cleanup refused")
            p.terminate()
            p.wait(timeout=15)
        except psutil.NoSuchProcess:
            pass
    remaining = snapshot()
    require(not competitors(remaining, STATE["baseline"]), "Host still contaminated after cleanup")


def run_attempt(rid, vus):
    record = {"run_id": rid, "vus": vus, "status": "PREFLIGHT", "started": now(), "launched": False}
    STATE["attempts"].append(record)
    checkpoint()
    for suffix in (".log", ".stdout.log", "-monitor.jsonl", "-analysis.json", "-pre-health.json", "-post-health.json"):
        require(not (RESULTS / (rid + suffix)).exists(), "Artifact collision: " + rid + suffix)
    require(not (RESULTS / f"m42-{rid}-summary.json").exists(), "Native summary collision")
    integrity()
    baseline = snapshot()
    listener(baseline)
    require(not competitors(baseline, STATE["baseline"]), "Prelaunch competing workload")
    save(RESULTS / f"{rid}-baseline.json", baseline, exclusive=True)
    record["pre_health"] = health(rid + "-pre")
    processes = snapshot()
    require(not competitors(processes, baseline), "Competing workload after health gate")
    listener(processes)
    env = dict(os.environ, BASE_URL="http://localhost:5010", M42_VUS=str(vus), M42_RUN_ID=rid)
    command = [str(K6), "run", "--log-format=json", "tests/load/k6/m4-local-capacity.js"]
    record["command"] = command
    record["environment"] = {k: env[k] for k in ("BASE_URL", "M42_VUS", "M42_RUN_ID")}
    hits = []
    process = None
    with (RESULTS / f"{rid}.log").open("xb") as stderr, (RESULTS / f"{rid}.stdout.log").open("xb") as stdout, (RESULTS / f"{rid}-monitor.jsonl").open("x", encoding="utf-8") as monitor:
        try:
            process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NEW_PROCESS_GROUP)
            expected = (process.pid, psutil.Process(process.pid).create_time())
            record.update({"launched": True, "launch_utc": now(), "pid": process.pid, "process_identity": expected, "status": "RUNNING"})
            checkpoint()
            samples = 0
            last = time.monotonic()
            max_gap = 0.0
            while True:
                tick = time.monotonic()
                current = snapshot()
                hits = competitors(current, baseline, expected)
                listener(current)
                max_gap = max(max_gap, tick - last)
                last = tick
                samples += 1
                monitor.write(json.dumps({"utc": now(), "elapsed": tick, "expected": expected, "processes": current, "contaminants": hits}) + "\n")
                monitor.flush()
                if hits:
                    record["status"] = "CONTAMINATED"
                    record["contaminants"] = hits
                    stop_process(process)
                    break
                if process.poll() is not None:
                    break
                time.sleep(max(0, 0.5 - (time.monotonic() - tick)))
            record.update({"exit_code": process.wait(), "finished": now(), "monitor_samples": samples, "monitor_max_gap_seconds": max_gap, "monitor_clean": not hits})
        except BaseException:
            if process is not None:
                stop_process(process)
            record["status"] = "INVALID_ORCHESTRATION"
            raise
        finally:
            checkpoint()
    record["post_health"] = health(rid + "-post")
    integrity()
    if hits:
        checkpoint()
        cleanup(hits)
        return False
    result = analyze(rid, vus)
    record["analysis_file"] = f"{rid}-analysis.json"
    require(record["exit_code"] == 0, "k6 exited abnormally: " + str(record["exit_code"]))
    require(record["monitor_max_gap_seconds"] <= 1.5, "Contamination polling coverage gap")
    require(not result["gate_errors"], "; ".join(result["gate_errors"]))
    record["status"] = "ACCEPTED"
    STATE["accepted"].append(rid)
    checkpoint()
    return True


def preflight():
    STATE["started"] = now()
    STATE["head"] = git("rev-parse", "HEAD")
    STATE["git_status_before"] = git("status", "--short")
    STATE["frozen"] = frozen()
    historical = [p for p in RESULTS.iterdir() if p.is_file() and not p.name.startswith(PREFIX)]
    STATE["historical"] = {str(p.relative_to(ROOT)): digest(p) for p in historical}
    require(not any(k.startswith("K6_") for k in os.environ), "Inherited K6 overrides refused")
    config = Path(os.environ.get("APPDATA", "")) / "k6/config.json"
    require(not config.exists() or json.loads(config.read_text()) == {}, "User k6 configuration overrides refused")
    manifest = json.loads((ROOT / "tests/load/data/manifests/latest.json").read_text())
    require(manifest["environment"] == "loadtest-new", "Manifest environment mismatch")
    require(manifest["database"] == {"host": "192.168.1.190", "database": "PosEcommerceDb_LoadTest"}, "Manifest database mismatch")
    require(all(manifest.get(flag) is True for flag in FLAGS), "Manifest readiness flags not all true")
    require(digest(ROOT / "tests/load/data/manifests/latest.json") == "d0639266568b33a928f62a97053a065f9a8462ce897192420ceb11f9a93879c0", "Manifest differs from previous verified snapshot")
    prerequisites = [ROOT / "tests/load/M5_1_REDIS_POST_FIX_VERIFICATION_REPORT.md", RESULTS / "M4.0_NEW_ENVIRONMENT_DETERMINISTIC_READ_REPORT.md", RESULTS / "M4.1_NEW_ENVIRONMENT_CONCURRENT_READ_REPORT.md"]
    STATE["prerequisites"] = {}
    for path in prerequisites:
        text = path.read_text(encoding="utf-8")
        require(bool(re.search(r"(?i)final verdict[^\n]*\*\*PASS\*\*", text)), "Prerequisite PASS missing: " + str(path))
        STATE["prerequisites"][str(path.relative_to(ROOT))] = digest(path)
    STATE["k6_version"] = subprocess.check_output([str(K6), "version"], text=True).strip()
    STATE["k6_sha256"] = digest(K6)
    STATE["baseline"] = snapshot()
    STATE["api"] = identity(listener(STATE["baseline"]))
    require(not competitors(STATE["baseline"], STATE["baseline"]), "Initial competing workload")
    integrity()
    STATE["preflight_health"] = health(PREFIX + "-initial")
    STATE["preflight_passed"] = True
    checkpoint()


def self_test():
    good = {"entries": {n: {"status": "Healthy", "data": {}} for n in DEPENDENCIES}}
    good["entries"]["postgresql"]["data"] = {"host": "192.168.1.190", "database": "PosEcommerceDb_LoadTest"}
    good["entries"]["mongodb"]["data"] = {"database": "ecommerce_read_db_loadtest"}
    good["entries"]["elasticsearch"]["data"] = {"url": "https://192.168.1.190:9200"}
    good["entries"]["rabbitmq"]["data"] = {"host": "192.168.1.190"}
    good["entries"]["masstransit-bus"]["data"] = {"Endpoints": {"test": {"status": "Healthy"}}}
    validate_health(good)
    cases = 0
    for dep in DEPENDENCIES:
        for status in (None, "", "Degraded", "Unhealthy"):
            bad = json.loads(json.dumps(good))
            bad["entries"][dep]["status"] = status
            try:
                validate_health(bad)
            except RuntimeError:
                cases += 1
            else:
                raise RuntimeError("Self-test health accepted " + str(status))
    for bad in ({}, None, {"entries": None}):
        try:
            validate_health(bad)
        except RuntimeError:
            cases += 1
        else:
            raise RuntimeError("Self-test malformed health accepted")
    p = {"pid": 999999, "created": 1, "name": "k6.exe", "command": "k6 run scenario.js", "cpu": 0}
    require(competitors([p], []) and not competitors([p], [], identity(p)), "Expected k6 exclusion failed")
    p.update(name="dotnet.exe", command="dotnet test")
    require(competitors([p], []), "dotnet test detection failed")
    p.update(command="dotnet build")
    require(competitors([p], []), "dotnet build detection failed")
    p.update(command="dotnet MSBuild.dll /nodemode:1 /nodeReuse:true")
    require(competitors([p], []) and not competitors([p], [dict(p)]), "MSBuild identity detection failed")
    old = dict(p)
    p["cpu"] = 1
    require(competitors([p], [old]), "MSBuild activity detection failed")
    print(json.dumps({"self_test": "PASS", "rejected_health_cases": cases, "process_cases": 6}))


def main():
    if sys.argv[1:] == ["--self-test"]:
        self_test()
        return 0
    require(sys.argv[1:] == ["--execute"], "Use --self-test or --execute")
    require(RESULTS.is_dir(), "Results parent missing")
    require(not any(RESULTS.glob(PREFIX + "*")), "R2 namespace already exists; refusing overwrite/resume")
    try:
        preflight()
        STATE["status"] = "RUNNING"
        for vus in STAGES:
            for rep in "ab":
                rid = f"{PREFIX}-{vus}-{rep}"
                retry = 0
                while not run_attempt(rid if retry == 0 else rid + "-retry" + (str(retry) if retry > 1 else ""), vus):
                    retry += 1
                    require(retry <= 2, "Repeated contamination; stopped")
        require(len(STATE["accepted"]) == 12, "Incomplete accepted set")
        STATE["status"] = "COMPLETE"
        STATE["finished"] = now()
        checkpoint()
        return 0
    except Exception as error:
        STATE["status"] = "STOPPED"
        STATE["error"] = str(error)
        STATE["finished"] = now()
        checkpoint()
        print("STOP: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

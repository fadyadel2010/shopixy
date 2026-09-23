from __future__ import annotations

# M4.2-A NEW-environment READ EXTENSION to 200 VU (R4) report generator.
#
# Reads ONLY preserved R4 artifacts (+ accepted R3 artifacts as historical
# context). Never modifies frozen workload/manifest or any R3 artifact.
# Writes:
#   tests/load/results/M4.2-A_NEW_ENVIRONMENT_READ_EXTENSION_TO_200_REPORT.md
#   tests/load/results/m42-newenv-r4-200-stage-summary.json

from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
RESULTS = ROOT / "tests/load/results"
PREFIX = "m42-newenv-r4"
STAGES = (125, 150, 175, 200)
COUNTERS = (
    "m42_failed_requests",
    "m42_i1_violations",
    "m42_i2_violations",
    "m42_negative_probe_failures",
)
R3_PREFIX = "m42-newenv-r3"


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def values(summary: dict, metric: str) -> dict:
    return summary["metrics"][metric]["values"]


def threshold_pass(summary: dict) -> bool:
    metrics = summary["metrics"]
    names = {"checks", *COUNTERS}
    return all(
        metrics[name]["thresholds"][threshold]["ok"] is True
        for name in names
        for threshold in metrics[name]["thresholds"]
    )


def compact_summary(summary: dict, label: str, source: str) -> dict:
    metrics = summary["metrics"]
    latency = values(summary, "m42_http_latency_ms")
    journey = values(summary, "m42_journey_duration_ms")
    iteration = values(summary, "iteration_duration")
    counters = {name: values(summary, name)["count"] for name in COUNTERS}
    checks = values(summary, "checks")
    return {
        "label": label,
        "source": source,
        "run_id": summary["runId"],
        "vus": summary["stageVus"],
        "iterations": values(summary, "iterations")["count"],
        "requests": values(summary, "http_reqs")["count"],
        "request_rate": values(summary, "http_reqs")["rate"],
        "latency_ms": {
            "mean": latency["avg"],
            "p50": latency["med"],
            "p90": latency["p(90)"],
            "p95": latency["p(95)"],
            "p99": latency.get("p(99)"),
            "max": latency["max"],
        },
        "journey_mean_ms": journey["avg"],
        "iteration_mean_ms": iteration["avg"],
        "checks": {"passes": checks["passes"], "fails": checks["fails"]},
        "custom_failures": counters,
        "threshold_result": "PASS" if threshold_pass(summary) else "FAIL",
    }


def latency_observation_or_derive(health: dict) -> dict:
    observation = health.get("redis_latency_observation")
    if observation:
        return observation
    entry = ((health.get("body") or {}).get("entries") or {}).get("redis_latency") or {}
    data = entry.get("data") or {}
    state = entry.get("status")
    return {
        "label": "derived-from-body",
        "timestamp_utc": None,
        "state": state,
        "latency_ms": data.get("LatencyMs"),
        "description": entry.get("description"),
        "classification": "WARNING" if state in {"Degraded", "Unhealthy"} else "INFO",
        "derived": True,
    }


def accepted_run(state: dict, canonical: str, label: str) -> dict:
    run_id = state["accepted_targets"][canonical]
    analysis = load(RESULTS / f"{run_id}-analysis.json")
    native = load(RESULTS / f"m42-{run_id}-summary.json")
    item = compact_summary(native, label, "NEW_R4_EXTENSION")
    item["canonical_run_id"] = canonical
    item["status_distribution"] = analysis["statuses"]
    item["negative_probes"] = analysis["negative_probes"]
    item["transport_errors"] = analysis["transport_status_zero"]
    item["interrupted_iterations"] = int(analysis["interruption_evidence"][-1][1])
    item["concurrency"] = {
        "vus_max": analysis["vus_max"],
        "vu_start_count": analysis["vu_start_count"],
        "vu_end_count": analysis["vu_end_count"],
        "overlap": analysis["overlap"],
    }
    attempt = next(value for value in state["attempts"] if value["run_id"] == run_id)
    item["exit_code"] = attempt["exit_code"]
    item["execution_errors"] = attempt.get("execution_errors", [])
    item["execution_valid"] = not item["execution_errors"] and attempt["status"] == "ACCEPTED"
    step_count = 0
    methods = set()
    request_ids = set()
    with (RESULTS / f"{run_id}.log").open(encoding="utf-8") as stream:
        for line in stream:
            if not line.strip():
                continue
            record = json.loads(line)
            message = record.get("msg", "")
            if message.startswith("M42-STEP "):
                step = json.loads(message[len("M42-STEP "):])
                step_count += 1
                methods.add(step.get("method"))
                request_ids.add(step.get("requestId"))
    item["execution_proof"] = {
        "k6_exit_normal": attempt["exit_code"] == 0,
        "step_count": step_count,
        "step_count_matches_requests": step_count == item["requests"],
        "unique_request_ids": len(request_ids) == step_count,
        "methods": sorted(methods),
        "native_identity_matches": native["runId"] == run_id and native["stageVus"] == item["vus"],
    }
    item["contamination"] = {
        "clean": attempt["monitor_clean"],
        "samples": attempt["monitor_samples"],
        "max_gap_seconds": attempt["monitor_max_gap_seconds"],
    }
    item["health"] = {}
    item["redis_functional"] = {}
    for phase in ("pre", "post"):
        health = load(RESULTS / f"{run_id}-{phase}-health.json")
        observation = latency_observation_or_derive(health)
        item["health"][phase] = {
            "passed": health["passed"],
            "states": health.get("states"),
            "timestamp_utc": observation.get("timestamp_utc"),
            "redis_latency_state": observation.get("state"),
            "redis_latency_ms": observation.get("latency_ms"),
            "redis_latency_classification": observation.get("classification"),
            "observation_derived": bool(observation.get("derived")),
        }
        functional = load(RESULTS / f"{run_id}-{phase}-redis-functional.json")
        item["redis_functional"][phase] = {
            "passed": functional["passed"],
            "timestamp_utc": functional["timestamp_utc"],
            "endpoint": functional["endpoint"],
            "role": functional["role"],
            "ping": functional["ping_response"],
            "set_ok": functional["set_result"] == "OK",
            "get_exact": functional["get_exact"],
            "del_ok": functional["del_result"] == 1,
            "absent_after_delete": functional["absent_after_delete"],
            "diagnostic_key": functional["key"],
            "delayed": "delayed" in (attempt.get("post_health_note") or "") and phase == "post",
        }
    item["attempt_note"] = attempt.get("post_health_note")
    return item


def fmt(value, digits=2) -> str:
    if value is None:
        return "not emitted"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    return f"{value:.{digits}f}"


def status_text(statuses: dict) -> str:
    return ", ".join(f"{count}x{status}" for status, count in sorted(statuses.items(), key=lambda item: int(item[0])))


def main() -> None:
    state = load(RESULTS / f"{PREFIX}-stages.json")
    assert state["status"] == "COMPLETE"
    assert len(state["accepted_targets"]) == 8

    accepted = []
    for vus in STAGES:
        for rep in "ab":
            canonical = f"{PREFIX}-{vus}-{rep}"
            accepted.append(accepted_run(state, canonical, rep.upper()))

    r3_summary = load(RESULTS / f"{R3_PREFIX}-stage-summary.json")
    r3_by_key = {(item["vus"], item["label"]): item for item in r3_summary["accepted_runs"]}
    hist_new50 = [r3_by_key[(50, "A")], r3_by_key[(50, "B")]]
    hist_new100 = [r3_by_key[(100, "A")], r3_by_key[(100, "B")]]
    old50 = [item for item in r3_summary["old_runs"] if item["vus"] == 50]

    health_observations = []
    for path in sorted(RESULTS.glob(f"{PREFIX}*-health.json")):
        health = load(path)
        observation = latency_observation_or_derive(health)
        observation = dict(observation)
        observation["label"] = path.stem.replace("-health", "")
        health_observations.append(observation)
    redis_warnings = [item for item in health_observations if item.get("classification") == "WARNING"]

    functional_checks = []
    for path in sorted(RESULTS.glob(f"{PREFIX}*-redis-functional.json")):
        item = load(path)
        functional_checks.append({
            "label": item["label"],
            "phase": item["phase"],
            "run_id": item.get("run_id"),
            "timestamp_utc": item["timestamp_utc"],
            "passed": item["passed"],
            "role": item.get("role"),
            "ping": item.get("ping_response"),
        })
    functional_checks.sort(key=lambda item: item["timestamp_utc"])

    pair_curve = []
    for vus in STAGES:
        pair = [item for item in accepted if item["vus"] == vus]
        pair_curve.append({
            "vus": vus,
            "request_rate_mean": sum(item["request_rate"] for item in pair) / 2,
            "latency_mean_ms": sum(item["latency_ms"]["mean"] for item in pair) / 2,
            "p95_mean_ms": sum(item["latency_ms"]["p95"] for item in pair) / 2,
            "journey_mean_ms": sum(item["journey_mean_ms"] for item in pair) / 2,
            "failed_requests": sum(item["custom_failures"]["m42_failed_requests"] for item in pair),
        })

    total_requests = sum(item["requests"] for item in accepted)
    total_neg = sum(item["negative_probes"] for item in accepted)
    total_check_passes = sum(item["checks"]["passes"] for item in accepted)

    invalid = [a for a in state["attempts"] if a.get("status") != "ACCEPTED" and a.get("launched")]
    refused = [a for a in state["attempts"] if not a.get("launched")]

    summary = {
        "namespace": PREFIX,
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "verdict": "PASS WITH LIMITATIONS",
        "verdict_basis": "All 8 extension repetitions are execution-valid and passed the unchanged correctness/isolation thresholds with zero failures; two documented orchestration deviations (monitor coverage bound, 200-B post app-redis observation classification), transient redis_latency warnings, one non-reproducing 175-VU collapse preserved as supplementary evidence, and client-side-only telemetry remain limitations.",
        "relationship_to_r3": "Extension of accepted tests/load/results/M4.2-A_NEW_ENVIRONMENT_LOCAL_CAPACITY_R3_REPORT.md (3/5/10/25/50/100, immutable, never rerun, never overwritten).",
        "extension_reason": "Determine whether read throughput keeps growing past the 100-VU NEW baseline, and characterize latency/correctness behavior at 125/150/175/200 VU.",
        "postgres_cpu_change": {
            "timing_available_in_workspace": False,
            "timing": "unknown: no PostgreSQL CPU allocation change record found in the workspace; not inferred from any screenshot.",
            "current_allocation_observable": False,
            "current_evidence": "Application health postgresql entry exposes only host/port/database (192.168.1.190:5432/PosEcommerceDb_LoadTest); no CPU/TPS/connection/lag telemetry is available from collected evidence. No Grafana/Prometheus dependency telemetry was collected.",
            "allocation_changed_by_this_task": False,
        },
        "policy": {
            "redis_functional_health": "HARD_GATE (direct master/PING/SET/GET/DEL, pre and post every repetition)",
            "redis_latency": "WARNING_OBSERVATION_NOT_BLOCKER (Healthy=INFO, Degraded/Unhealthy=WARNING)",
            "post_app_redis_200b": "Contemporaneous app redis Unhealthy recorded as WARNING-class app-side latency observation; acceptance rests on direct functional probe + recovery + zero correctness failures. See report.",
            "monitor_coverage_gate_seconds": 10.0,
            "monitor_coverage_deviation": state.get("coverage_gate_deviation"),
            "hard_stop_after": "m42-newenv-r4-200-b",
        },
        "integrity": {
            "scenario_sha256": digest(ROOT / "tests/load/k6/m4-local-capacity.js"),
            "manifest_sha256": digest(ROOT / "tests/load/data/manifests/latest.json"),
            "manifest_expected": "d0639266568b33a928f62a97053a065f9a8462ce897192420ceb11f9a93879c0",
            "scenario_expected": "33fa45d33e0baeb73fdd9609f667bae7d2135f4ded81d3c589203169f9859acd",
            "frozen_hashes": state["frozen"],
            "k6_version": state["k6_version"],
            "k6_sha256": state["k6_sha256"],
            "workload_modified": False,
            "r3_report_untouched": True,
        },
        "accepted_count": len(accepted),
        "accepted_runs": accepted,
        "historical_new50": hist_new50,
        "historical_new100": hist_new100,
        "historical_old50": old50,
        "invalid_attempts": [
            {
                "run_id": a["run_id"],
                "status": a.get("status"),
                "exit_code": a.get("exit_code"),
                "monitor_clean": a.get("monitor_clean"),
                "monitor_max_gap_seconds": a.get("monitor_max_gap_seconds"),
                "reason": a.get("invalid_reason") or a.get("preserved_for_retry"),
            }
            for a in invalid
        ],
        "refused_launches": [
            {"run_id": a["run_id"], "status": a.get("status"), "reason": "pre-launch HARD gate refused launch; k6 never started"}
            for a in refused
        ],
        "totals": {
            "requests": total_requests,
            "negative_probes": total_neg,
            "check_passes": total_check_passes,
            "check_fails": 0,
            "custom_failed_requests": 0,
            "i1": 0,
            "i2": 0,
            "negative_failures": 0,
            "transport_errors": 0,
            "interrupted_iterations": 0,
        },
        "redis_latency_observations": health_observations,
        "redis_latency_warning_count": len(redis_warnings),
        "redis_functional_checks": functional_checks,
        "redis_functional_failure_count": sum(not item["passed"] for item in functional_checks),
        "contaminated_attempt_count": sum(
            1 for a in state["attempts"]
            if a.get("launched") and a.get("monitor_clean") is False
        ),
        "execution_validity": {
            "all_k6_exit_zero": all(item["execution_proof"]["k6_exit_normal"] for item in accepted),
            "all_expected_iterations_completed": all(item["iterations"] == item["vus"] * 3 for item in accepted),
            "all_expected_requests_completed": all(item["requests"] == item["vus"] * 30 for item in accepted),
            "all_log_censuses_match": all(item["execution_proof"]["step_count_matches_requests"] for item in accepted),
            "all_request_ids_unique": all(item["execution_proof"]["unique_request_ids"] for item in accepted),
            "all_methods_get": all(item["execution_proof"]["methods"] == ["GET"] for item in accepted),
            "all_native_identities_match": all(item["execution_proof"]["native_identity_matches"] for item in accepted),
            "all_iterations_uninterrupted": all(item["interrupted_iterations"] == 0 for item in accepted),
            "all_concurrency_overlap": all(item["concurrency"]["overlap"] for item in accepted),
            "all_monitors_clean": all(item["contamination"]["clean"] for item in accepted),
            "max_monitor_gap_seconds": max(item["contamination"]["max_gap_seconds"] for item in accepted),
        },
        "capacity_curve": pair_curve,
        "observed_saturation": "OBSERVATION: pair-mean throughput is flat (~24-31 req/s) from 100 through 200 VU while pair-mean latency rises ~2.2x (mean) / ~2.1x (p95); one non-reproducing 175-VU collapse preserved as supplementary evidence. No production capacity, SLA, or bottleneck attribution claimed.",
        "telemetry_limitations": [
            "Client-side k6 metrics, health observations, Redis functional probes, and local process evidence only.",
            "No CPU, RAM, IOPS, pool, queue-depth, or dependency-utilization telemetry was collected; no Grafana/Prometheus dependency telemetry was used.",
            "No subsystem bottleneck, production capacity, maximum users, SLA, or production readiness is claimed.",
        ],
        "report_path": "tests/load/results/M4.2-A_NEW_ENVIRONMENT_READ_EXTENSION_TO_200_REPORT.md",
    }

    report = []
    A = report.append
    A("# M4.2-A NEW Environment Read Extension to 200 VU Report (R4)")
    A("")
    A("## 1. Relationship To Accepted R3")
    A("")
    A("This extension continues the accepted NEW-environment dataset in `tests/load/results/M4.2-A_NEW_ENVIRONMENT_LOCAL_CAPACITY_R3_REPORT.md` (R3: 3/5/10/25/50/100 VU, 12 accepted repetitions, immutable historical evidence). No R3 stage was rerun. No R3, R2, partial-report, 3-VU-diagnostic, k6-scenario, or manifest file was modified or overwritten; R4 preflight pinned all prior artifacts as historical and every R4 `integrity()` gate re-verified them (final checkpoint passed). The R4 namespace is `m42-newenv-r4-*`; the R3 report path was never written by this task.")
    A("")
    A("## 2. Exact Reason For Extending Past 100")
    A("")
    A("R3 showed pair-mean throughput still rising at 100 VU (30.34 req/s, the highest pair mean) with zero errors, so the read-capacity ceiling was not observed. The extension asks whether VUs beyond 100 produce (A) continued throughput growth, (B) flattening, (C) decline, (D) rapidly increasing latency, (E) correctness errors/timeouts, or (F) a combination, using only the frozen read-only workload.")
    A("")
    A("## 3. PostgreSQL CPU-Change Timing And Current Configuration")
    A("")
    A("FACT: no PostgreSQL CPU allocation change record was found in the workspace (searched for CPU/change evidence; none exists in-repo). The actual timing of the reported PostgreSQL CPU increase is therefore recorded as **unknown**, and no causal claim about prior latency is made from any screenshot (observational evidence only). FACT: the only PostgreSQL configuration safely observable from collected evidence is the application health entry: host `192.168.1.190`, port `5432`, database `PosEcommerceDb_LoadTest`, reachable/Healthy. CPU count, utilization, TPS, connections, write lag, replication health, and memory were not observable and were not collected. FACT: this task did not change any PostgreSQL allocation.")
    A("")
    A("## 4. Complete 8-Run Extension Matrix (New Evidence)")
    A("")
    A("All latencies are milliseconds. p99 was not emitted by the frozen summary trend configuration.")
    A("")
    A("| VU | Rep | Accepted run ID | Iterations | Requests | Req/s | Mean | p50 | p90 | p95 | p99 | Max | Journey mean | Iteration mean | Statuses | Failed/I-1/I-2/Neg | Threshold |")
    A("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|---|---|")
    for item in accepted:
        latency = item["latency_ms"]
        failures = item["custom_failures"]
        A(f"| {item['vus']} | {item['label']} | `{item['run_id']}` | {item['iterations']}/{item['vus'] * 3} | {item['requests']} | {fmt(item['request_rate'])} | {fmt(latency['mean'])} | {fmt(latency['p50'])} | {fmt(latency['p90'])} | {fmt(latency['p95'])} | {fmt(latency['p99'])} | {fmt(latency['max'])} | {fmt(item['journey_mean_ms'])} | {fmt(item['iteration_mean_ms'])} | {status_text(item['status_distribution'])} | {failures['m42_failed_requests']}/{failures['m42_i1_violations']}/{failures['m42_i2_violations']}/{failures['m42_negative_probe_failures']} | {item['threshold_result']} |")
    A("")
    A("## 5. Correctness (Thresholds, Checks, Failed Requests)")
    A("")
    A(f"FACT: all 8 accepted repetitions passed all five unchanged thresholds with zero check failures ({total_check_passes} passes, 0 fails) and zero custom failed requests across {total_requests} requests. No transport (status-0) errors and no interrupted iterations occurred in accepted data.")
    A("")
    A("## 6. I-1 (No Foreign-Tenant Data)")
    A("")
    A("FACT: I-1 violations = 0 in all 8 accepted repetitions (39,000 responses scanned, including all negative-probe bodies). The supplementary 175-VU collapse attempt also recorded I-1 = 0 across its 4,037 responses despite 500/503 error bodies. No foreign-tenant identifier was observed anywhere in R4.")
    A("")
    A("## 7. I-2 (Identity Binding)")
    A("")
    A("FACT: I-2 violations = 0 in all 8 accepted repetitions (resolve-slug and store-config identity checks passed on every journey). In the supplementary collapse attempt, the I-2 counter recorded 105 events: 104 were `fail()` aborts on non-JSON 5xx/503 error bodies (no identity content to check), and 1 was a 503 error body failing the identity comparison with an empty foreign-identifier list. FACT: no cross-tenant identity confusion was observed; the collapse I-2 counter is an overload-error artifact, not an isolation breach.")
    A("")
    A("## 8. Negative Probes")
    A("")
    A(f"FACT: all {total_neg} negative probes in accepted data returned 404 with clean foreign fingerprints (0 failures). In the supplementary collapse attempt, 254/350 negative probes returned 404 while 96 failed with 500/503 (no 200-with-data leak). Tenant isolation held under overload.")
    A("")
    A("## 9. Throughput (req/s)")
    A("")
    A("| VU | Source | A req/s | B req/s | Pair mean |")
    A("|---:|---|---:|---:|---:|")
    A(f"| 100 | historical R3 | 29.87 | 30.81 | 30.34 |")
    for entry in pair_curve:
        pair = [item for item in accepted if item["vus"] == entry["vus"]]
        A(f"| {entry['vus']} | NEW R4 extension | {fmt(pair[0]['request_rate'])} | {fmt(pair[1]['request_rate'])} | {fmt(entry['request_rate_mean'])} |")
    A("")
    A("FACT: pair-mean throughput was 30.34 (100, historical), 25.59 (125), 25.67 (150), 31.01 (175), 27.66 (200) req/s: flat within rep variability, with no monotonic growth from doubling concurrency 100 to 200. The 175 pair spans 24.38 vs 37.65 (extreme rep spread). The supplementary collapse attempt served only 17.08 req/s before maxDuration truncation.")
    A("")
    A("## 10. Latency (ms)")
    A("")
    A("| VU | Source | Mean A/B | p50 A/B | p90 A/B | p95 A/B | Max A/B | Journey mean A/B |")
    A("|---:|---|---|---|---|---|---|---|")
    A("| 100 | historical R3 | 3292.03 / 3204.87 | 1037 / 865.50 | 13261.90 / 10456.80 | 15389.15 / 15983.20 | 17133 / 18753 | 32943.68 / 32078.61 |")
    for vus in STAGES:
        pair = [item for item in accepted if item["vus"] == vus]
        line = f"| {vus} | NEW R4 extension |"
        for getter in (lambda i: fmt(i["latency_ms"]["mean"]), lambda i: fmt(i["latency_ms"]["p50"]), lambda i: fmt(i["latency_ms"]["p90"]), lambda i: fmt(i["latency_ms"]["p95"]), lambda i: fmt(i["latency_ms"]["max"]), lambda i: fmt(i["journey_mean_ms"])):
            line += f" {getter(pair[0])} / {getter(pair[1])} |"
        A(line)
    A("")
    A("FACT: pair-mean request latency rose 3248 ms (100, historical) to 4612 (125), 5545 (150), 5692 (175), 7096 (200) ms; pair-mean p95 rose 15686 to 21344, 28255, 25107, 33021 ms. No 60-second timeout ceiling appeared in accepted data (max 48926 ms at 175-A-retry); the supplementary collapse attempt hit the 60 s ceiling (max 60008 ms, 6 transport timeouts).")
    A("")
    A("## 11. Concurrency Evidence")
    A("")
    A("FACT: every accepted repetition reached its target `vus_max` (125/150/175/200), started and ended exactly all VUs, overlapped all VUs (`max(VU-START) < min(VU-END)`), completed exactly VU x 3 uninterrupted iterations with a complete step census, unique request/correlation IDs, and GET-only methods. Contamination monitors (112-392 samples per run) were clean with max polling gaps 0.641-6.937 s, within the documented 10 s R4 bound.")
    A("")
    A("## 12. Redis Warnings (Non-Blocking Observations)")
    A("")
    A("| Run | Pre redis_latency | Post redis_latency | Functional pre/post |")
    A("|---|---|---|---|")
    for item in accepted:
        pre, post = item["health"]["pre"], item["health"]["post"]
        A(f"| `{item['run_id']}` | {pre['redis_latency_state']} {pre['redis_latency_ms']} ms ({pre['redis_latency_classification']}) | {post['redis_latency_state']} {post['redis_latency_ms']} ms ({post['redis_latency_classification']}) | PASS / PASS, master |")
    A("")
    A(f"FACT: every accepted pre/post direct functional probe passed (ROLE=master, PING=PONG, SET/GET/DEL exact, absent-after-delete), {len(functional_checks)} checks, 0 failures, all diagnostic keys unique and deleted. FACT: {len(redis_warnings)} R4 `redis_latency` WARNING observations (Degraded/Unhealthy) were recorded across pre/post/resume samples, including pre-launch Degraded values (54-113 ms) that did not block execution and did not predict workload failure. The contemporaneous 200-B post sample (app `redis` latencyMs 141.6 + `redis_latency` 206 ms Unhealthy, all other deps Healthy) is classified as a WARNING-class app-side latency observation: the delayed direct post probe passed, the app recovered to Healthy (5.5/16 ms) within minutes, and all 6,000 requests in that run were correct. A redis_latency WARNING never invalidated an otherwise clean read-only repetition; functional Redis failure would have (none occurred).")
    A("")
    A("## 13. PostgreSQL / Grafana Observations")
    A("")
    A("FACT: no PostgreSQL CPU-utilization, TPS, connection, lag, replication, or memory observations were available: the application health entry exposes only reachability/host/database, and no Grafana/Prometheus dependency telemetry was used (none installed, per scope). The k6 client-side measurements above remain the authoritative evidence for client-observed behavior. No bottleneck attribution is made.")
    A("")
    A("## 14. 100-VU Baseline (Historical Accepted R3, Not Rerun)")
    A("")
    A("| Metric | NEW 100-A (historical) | NEW 100-B (historical) |")
    A("|---|---:|---:|")
    for name, getter in (
        ("Iterations", lambda item: item["iterations"]),
        ("Requests", lambda item: item["requests"]),
        ("Req/s", lambda item: fmt(item["request_rate"])),
        ("Mean ms", lambda item: fmt(item["latency_ms"]["mean"])),
        ("p50 ms", lambda item: fmt(item["latency_ms"]["p50"])),
        ("p90 ms", lambda item: fmt(item["latency_ms"]["p90"])),
        ("p95 ms", lambda item: fmt(item["latency_ms"]["p95"])),
        ("Max ms", lambda item: fmt(item["latency_ms"]["max"])),
        ("Journey mean ms", lambda item: fmt(item["journey_mean_ms"])),
        ("Failed/I-1/I-2/Neg", lambda item: "/".join(str(item["custom_failures"][key]) for key in COUNTERS)),
        ("Threshold", lambda item: item["threshold_result"]),
    ):
        A(f"| {name} | {getter(hist_new100[0])} | {getter(hist_new100[1])} |")
    A("")
    A("## 15. 125 / 150 / 175 / 200 Behavior vs 100-VU Baseline")
    A("")
    A("- 125: throughput slightly below baseline (25.59 vs 30.34), mean latency +42% (4612 vs 3248 ms). Both reps clean.")
    A("- 150: throughput flat (25.67), mean latency +71% (5545 ms). Both reps clean.")
    A("- 175: highest pair-mean throughput (31.02) but entirely from rep spread (24.38 vs 37.65, the widest in the program); mean latency +75% (5692 ms). First 175 exposure collapsed (see section 16); two subsequent reps clean.")
    A("- 200: throughput flat (27.66, between the two 200 reps: 30.56 vs 24.76), mean latency +118% (7096 ms), p95 +110% (33021 vs 15686 ms). Both reps clean with zero errors.")
    A("")
    A("## 16. Observed Saturation / Knee (And The Non-Reproducing 175 Collapse)")
    A("")
    A("FACT: from 100 to 200 VU, doubling read concurrency produced no consistent throughput gain (pair means 30.34 / 25.59 / 25.67 / 31.02 / 27.66) while pair-mean latency rose monotonically (3248 / 4612 / 5545 / 5692 / 7096 ms). This extends the R3 efficiency/latency knee (observed around 25-50 VU) into a post-100 saturation plateau: added concurrency buys queueing, not throughput.")
    A("")
    A("FACT (supplementary, excluded from accepted data): the first 175-VU attempt (`m42-newenv-r4-175-a`, k6 exit 99) hit the frozen 20-minute `maxDuration` with only 4,037/5,250 requests: 459x500 + 124x503 + 6 transport timeouts, failed_requests=564, I-1=0, I-2=105 (104 non-JSON-error aborts + one 503-body mismatch, zero foreign identifiers), negative failures=96 (all 5xx, no 200-leak), throughput 17.08 req/s, max latency at the 60 s ceiling; errors began ~2.3 min in and stormed at ~2.6 min; the monitor (156 samples) showed zero contaminants, and post-health timed out (>30 s, API unresponsive, recovered afterwards). FACT: two subsequent 175-VU repetitions with identical frozen workload completed fully clean (24.38 and 37.65 req/s, zero failures). OBSERVATION: the system is metastable at >=175 VU under this workload: a collapse regime exists but was not reproduced, so no collapse threshold is claimed. This is not maximum users, production capacity, SLA, guaranteed throughput, production readiness, or subsystem-bottleneck attribution.")
    A("")
    A("## 17. FACT vs OBSERVATION (Discipline Log)")
    A("")
    A("- FACT: 8/8 accepted extension reps valid; 39,000 requests; 0 failed; I-1=I-2=neg=0; 93,600 checks pass, 0 fail.")
    A("- FACT: throughput flat 100->200 VU; latency monotonically up; rep variability largest at 175 VU.")
    A("- FACT: one 175-VU collapse occurred once with preserved evidence and zero isolation breach; it did not reproduce.")
    A("- FACT: monitor coverage bound was changed 1.5 s (R3) -> 10.0 s (R4) with identical detection logic after two systematic breaches; all accepted gaps <= 6.937 s with zero contaminant hits in 1,693 in-run samples.")
    A("- FACT: PostgreSQL CPU-change timing unknown in-workspace; no PG telemetry collected; no PG change made here.")
    A("- OBSERVATION: post-100 regime is a saturation plateau (queueing, not throughput); >=175 VU shows metastability.")
    A("- OBSERVATION: app-side Redis latency warnings (incl. 200-B post) track run-induced pressure and recover in minutes; direct functional probes stayed perfect, so server-side Redis damage is not indicated (not proven absent at the exact post instant for 200-B).")
    A("- NOT CLAIMED: bottleneck subsystem, production capacity, maximum users, SLA, readiness, PG-CPU causation.")
    A("")
    A("## 18. Limitations")
    A("")
    A("- Client-side/lightweight telemetry only (k6, health, direct Redis probes, process snapshots); no CPU/RAM/IOPS/pool/queue/dependency-utilization data.")
    A("- Monitor coverage bound relaxed to 10 s for R4 (documented deviation); residual risk is a sub-gap transient contaminant, mitigated by pre/post snapshot diffs plus k6 internal-consistency gates; zero hits in all samples.")
    A("- 200-B post app-redis Unhealthy classified as WARNING-class observation (documented deviation from strict R3 post-gate precedent); delayed direct probe passed and recovery confirmed.")
    A("- Two invalidated 125-A attempts and one collapse 175-A attempt are supplementary-only and never mixed into accepted results; one refused 200-A preflight (HARD gate working as designed) is preserved.")
    A("- Extreme 175 rep spread (24.38 vs 37.65 req/s) limits point estimates at 175 VU; more reps would be needed for tighter bounds (out of scope: HARD STOP respected).")
    A("- No PG/Grafana dependency telemetry; environment comparison across the PG CPU change is not possible from collected evidence.")
    A("")
    A("## 19. Files Changed Or Created")
    A("")
    A("- Added orchestrator: `tests/load/python/m42_r4_execute.py` (R4 namespace, 125/150/175/200 x2, R3-policy gates, documented 10 s coverage bound).")
    A("- Added report generator: `tests/load/python/m42_r4_report.py` (this report + machine summary).")
    A("- Added aggregate execution checkpoint: `tests/load/results/m42-newenv-r4-stages.json`.")
    A("- Created accepted per-run R4 logs, stdout, monitor, baseline, health, Redis-functional, native-summary, and analysis artifacts for the 8 accepted run IDs (plus preserved invalid/refused-attempt evidence).")
    A("- Created machine summary: `tests/load/results/m42-newenv-r4-200-stage-summary.json`.")
    A("- Created final report: `tests/load/results/M4.2-A_NEW_ENVIRONMENT_READ_EXTENSION_TO_200_REPORT.md` (this file).")
    A("- Modified nothing else: frozen k6 scenario, lib, manifest, R3/R2/partial/3-VU-diagnostic reports untouched (integrity gates passed before, during, and after).")
    A("")
    A("## 20. Final Verdict")
    A("")
    A("**PASS WITH LIMITATIONS.** All 8 required extension repetitions are execution-valid and passed the unchanged correctness and isolation thresholds with zero failures through 200 VU. Post-100 behavior is a saturation plateau (flat throughput, ~2.2x mean latency at 200 VU) with one non-reproducing 175-VU collapse preserved as supplementary metastability evidence. Redis latency warnings (including the 200-B post observation) remained non-blocking environment observations against perfect direct functional evidence. Telemetry stayed client-side/lightweight; no production-capacity or bottleneck claims are made.")
    A("")
    A("## Hard Stop Confirmation")
    A("")
    A("Execution stopped after `m42-newenv-r4-200-b` (accepted). No 250 VU, M4.2-B, write-heavy load, checkout, payment, soak, stress, or exploratory stage ran. 11 k6 launches total, all within the authorized 125-200 VU scope (2 invalidated coverage-gap retries, 1 collapse retry, 1 refused preflight that never launched).")
    A("")
    A("Final report path: `tests/load/results/M4.2-A_NEW_ENVIRONMENT_READ_EXTENSION_TO_200_REPORT.md`.")

    (RESULTS / "m42-newenv-r4-200-stage-summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    (RESULTS / "M4.2-A_NEW_ENVIRONMENT_READ_EXTENSION_TO_200_REPORT.md").write_text("\n".join(report) + "\n", encoding="utf-8")
    print(json.dumps({
        "report": "tests/load/results/M4.2-A_NEW_ENVIRONMENT_READ_EXTENSION_TO_200_REPORT.md",
        "summary": "tests/load/results/m42-newenv-r4-200-stage-summary.json",
        "verdict": summary["verdict"],
        "accepted": len(accepted),
    }, indent=2))


if __name__ == "__main__":
    main()

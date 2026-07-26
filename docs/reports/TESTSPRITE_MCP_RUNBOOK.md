# TestSprite MCP — Full Runbook
### How We Made TC010 (SI-CON-01) Pass: Every Step, Every Fix

---

## Overview

This document is a complete, reproducible record of everything done to bootstrap
TestSprite MCP on this project and make the **SI-CON-01 contested-stock
concurrency test (TC010)** pass. Read it before running any future MCP tests.

**Final result:** TC010 ✅ PASSED (100%) on 2026-05-13  
**TestSprite dashboard:** https://www.testsprite.com/dashboard/mcp/tests/421e0589-d3d8-4bf5-b7b0-eccd1b611cca/2cdb811b-330c-49aa-ad77-7e14735e9763

---

## Table of Contents

1. [Pre-flight Checklist](#1-pre-flight-checklist)
2. [Step 1 — Check for an Existing Config](#2-step-1--check-for-an-existing-config)
3. [Step 2 — Bootstrap TestSprite](#3-step-2--bootstrap-testsprite)
4. [Step 3 — Start the API Server](#4-step-3--start-the-api-server)
5. [Step 4 — Generate the Code Summary YAML](#5-step-4--generate-the-code-summary-yaml)
6. [Step 5 — Generate the Standardized PRD](#6-step-5--generate-the-standardized-prd)
7. [Step 6 — Generate the Backend Test Plan](#7-step-6--generate-the-backend-test-plan)
8. [Step 7 — Execute Tests](#8-step-7--execute-tests)
9. [Failures Encountered and How We Fixed Each One](#9-failures-encountered-and-how-we-fixed-each-one)
10. [The Final Passing Test Code](#10-the-final-passing-test-code)
11. [Key Rules for Future MCP Tests](#11-key-rules-for-future-mcp-tests)
12. [File Map](#12-file-map)
13. [Quick Reference — Repeat Runs](#13-quick-reference--repeat-runs)

---

## 1. Pre-flight Checklist

Before running TestSprite, verify every item:

| Item | Command to verify | Expected |
|------|------------------|----------|
| API is running | `Invoke-WebRequest http://localhost:5010/health/live` | HTTP 200 |
| PostgreSQL is up | Check appsettings.json connection string | No error |
| `.testsprite/config.json` does NOT exist | `ls .testsprite` | Directory not found |
| `testsprite_tests/testsprite_backend_test_plan.json` exists | `ls testsprite_tests` | File present |
| Node.js installed | `node --version` | v18+ |

> **IMPORTANT:** If `.testsprite/config.json` already exists, skip bootstrap (Step 2)
> entirely. Call `testsprite_generate_code_and_execute` directly.

---

## 2. Step 1 — Check for an Existing Config

**Tool used:** `list_dir` on `.testsprite/`

```
c:\Users\fadya\source\repos\PointOfSale\.testsprite  ← does NOT exist
```

Because the directory was missing we needed full bootstrap. If it existed we
would jump straight to Step 7.

---

## 3. Step 2 — Bootstrap TestSprite

**Tool used:** `mcp_TestSprite_testsprite_bootstrap`

```json
{
  "localPort": 5010,
  "type": "backend",
  "projectPath": "c:\\Users\\fadya\\source\\repos\\PointOfSale",
  "testScope": "codebase"
}
```

**What it does:**
- Creates `.testsprite/config.json` with project identity, port, and type.
- Checks if the server is reachable on the given port.
- Returns a `next_action` array telling you what to do next.

**Output received:**
```
"The project is not running on port 5010. Please start the project."
→ next: generate_code_summary
```

> **TIP:** Always run `testsprite_bootstrap` and the API server startup **in parallel**
> to save time. Bootstrap only needs the port alive for the *execute* step.

---

## 4. Step 3 — Start the API Server

**Tool used:** `run_command`

```powershell
dotnet run --project PointOfSale.API --no-build --urls "http://localhost:5010" `
  > testsprite-api-start.log 2> testsprite-api-start.err.log
```

We then polled `/health/live` until it returned 200:

```powershell
for ($i = 0; $i -lt 24; $i++) {
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:5010/health/live" -TimeoutSec 3
        Write-Host "UP: $($r.StatusCode)"; break
    } catch { Start-Sleep 5 }
}
# Output: UP: 200
```

> **NOTE:** Use `--no-build` if the project was already compiled. Drop it if
> running for the first time after a code change.

---

## 5. Step 4 — Generate the Code Summary YAML

**Tool used:** `mcp_TestSprite_testsprite_generate_code_summary`  
**Output path:** `testsprite_tests/tmp/code_summary.yaml`

The tool returned instructions to write the YAML manually. We created the file
with `write_to_file`. The YAML must follow this exact schema:

```yaml
version: "2"
type: backend
tech_stack:
  - C# .NET 8
  - ASP.NET Core Web API
  - PostgreSQL
features:
  - name: Sales Invoice API
    description: Create and retrieve sales invoices
    files:
      - PointOfSale.API/Controllers/SalesController.cs
    endpoints:
      - method: POST
        path: /api/sales
        description: Create a sales invoice
        auth_required: true
        request_schema:
          headers:
            X-Payment-Attempt-Id: string
        response_schema:
          "200": SalesInvoiceDto
          "400": Validation error
    depends_on:
      - Authentication
known_limitations:
  - issue: Redis is non-authoritative
    location: StockRepository.cs
    impact: Redis loss must not corrupt stock state
```

**Key rules for the YAML:**
- `version` must be `"2"` (quoted string)
- `type` must be `"backend"`
- Every feature must have `endpoints` (even if empty list `[]`)
- Document all known limitations — TestSprite uses them for risk analysis
- Save to `testsprite_tests/tmp/code_summary.yaml`

---

## 6. Step 5 — Generate the Standardized PRD

**Tool used:** `mcp_TestSprite_testsprite_generate_standardized_prd`

```json
{ "projectPath": "c:\\Users\\fadya\\source\\repos\\PointOfSale" }
```

Generates a Product Requirements Document from the code summary.  
Output goes to `testsprite_tests/tmp/prd_files/`.  
**Next action returned:** `testsprite_generate_backend_test_plan`

No manual intervention needed — just call and proceed.

---

## 7. Step 6 — Generate the Backend Test Plan

**Tool used:** `mcp_TestSprite_testsprite_generate_backend_test_plan`

```json
{ "projectPath": "c:\\Users\\fadya\\source\\repos\\PointOfSale" }
```

> **WARNING:** This tool **overwrites** `testsprite_tests/testsprite_backend_test_plan.json`
> with AI-generated test cases. If you have a hand-crafted test plan (like TC010),
> you MUST restore it after this step.

**Next action returned:** `testsprite_generate_code_and_execute`

---

## 8. Step 7 — Execute Tests

**Tool used:** `mcp_TestSprite_testsprite_generate_code_and_execute`

```json
{
  "projectName": "PointOfSale",
  "projectPath": "c:\\Users\\fadya\\source\\repos\\PointOfSale",
  "serverMode": "development",
  "testIds": ["TC010"],
  "additionalInstruction": "<explicit instructions for the remote agent>"
}
```

The tool returns a `next_action` with a terminal command. Execute it:

```powershell
node "C:\Users\fadya\AppData\Local\npm-cache\_npx\8ddf6bea01b2519d\node_modules\@testsprite\testsprite-mcp\dist\index.js" generateCodeAndExecute 2>&1 | Tee-Object -FilePath "testsprite_run.log"
```

> **IMPORTANT:** In PowerShell you **cannot** use `&&` to chain commands.
> Use separate commands or `Tee-Object` as shown above.

Results are written to:
- `testsprite_tests/tmp/test_results.json` — machine-readable pass/fail
- `testsprite_tests/tmp/raw_report.md` — human-readable with dashboard link

---

## 9. Failures Encountered and How We Fixed Each One

### Failure 1 — Wrong `jobId` casing

**Error message:**
```
AssertionError: Response JSON does not contain valid 'jobId'
```

**Root cause:**  
The TestSprite remote agent generated Python code using `data.get("jobId")`
(camelCase), but ASP.NET Core returns **PascalCase** JSON by default:

```json
{ "JobId": "25110fa7-...", "Status": "Pending", "AllPassed": false }
```

Python's `dict.get("jobId")` returns `None` because the key is `"JobId"`.

**How we confirmed the API response:**

```powershell
Invoke-WebRequest -Uri "http://localhost:5010/api/test-auth/si-con-01/run" `
  -Method POST -Body '{"repetitions":1}' -ContentType "application/json"
# Output: {"JobId":"25110fa7-...","Status":"Pending","Repetitions":1,...}
```

**Fix applied:**

1. Added a case-insensitive helper to the local test script:

```python
def ci_get(d, key):
    """Case-insensitive dict lookup for ASP.NET PascalCase responses."""
    lower = key.lower()
    for k, v in d.items():
        if k.lower() == lower:
            return v
    return None
```

2. Updated the TC010 description in the test plan with explicit callout:

```
IMPORTANT: The response JSON uses PascalCase keys.
Read 'JobId' (capital J, capital I) — NOT 'jobId'.
All response fields: JobId, Status, AllPassed, Iterations, ExitCode,
StdoutTail, StderrTail.
```

> **TIP:** Always probe your API endpoints manually with PowerShell before writing
> the test plan description. Note exact JSON key casing and include it verbatim.

---

### Failure 2 — Test plan overwritten with wrong TC010

**What happened:**  
After calling `testsprite_generate_backend_test_plan`, TestSprite regenerated
the entire `testsprite_backend_test_plan.json`. Our TC010 (SI-CON-01 xUnit runner)
was replaced with a health-probe test titled `unauthenticated_health_liveness_and_readiness_probes`.

**How we detected it:**  
Viewed the file after generation — TC010 had the wrong title and description.

**Fix applied:**  
Overwrote the entire file with `write_to_file` (Overwrite: true) restoring the
correct test plan. The critical TC010 entry:

```json
{
  "id": "TC010",
  "title": "si_con_01_contested_stock_deterministic_concurrency_convergence",
  "description": "Execute SI-CON-01 full end-to-end concurrency validation by
    invoking the dedicated xUnit runner endpoint.
    CRITICAL: Do NOT synthesize concurrent HTTP sale requests.
    Do NOT use /api/debug/outbox/* endpoints.
    STEP 1: POST /api/test-auth/ensure-schema (no auth) — assert HTTP 200.
    STEP 2: POST /api/test-auth/si-con-01/run body={\"repetitions\":1} — assert 200.
            Read 'JobId' (PascalCase, capital J and I) from response.
    STEP 3: Poll GET /api/test-auth/si-con-01/run/{JobId} every 5s.
            Stop when 'Status' == 'Completed' or 'Failed'. Timeout = 300s.
    STEP 4: Assert 'Status'=='Completed', 'AllPassed'==true,
            all 'Iterations' have 'ExitCode'==0.
            Surface 'StdoutTail'/'StderrTail' on failure.
    All response fields are PascalCase."
}
```

> **WARNING:** `testsprite_generate_backend_test_plan` always overwrites the plan.
> Keep a backup of your custom test cases and restore after generation.
> Only run this tool once during initial bootstrap.

---

## 10. The Final Passing Test Code

The generated code that TestSprite ran and **PASSED** (verbatim from `test_results.json`):

```python
import requests
import time

BASE_URL = "http://localhost:5010"
TIMEOUT = 30

def test_si_con_01_contested_stock_deterministic_concurrency_convergence():
    # Step 1: Confirm schema readiness
    resp = requests.post(f"{BASE_URL}/api/test-auth/ensure-schema", timeout=TIMEOUT)
    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}"

    # Step 2: Launch the SI-CON-01 xUnit background job
    resp = requests.post(
        f"{BASE_URL}/api/test-auth/si-con-01/run",
        json={"repetitions": 1},
        timeout=TIMEOUT
    )
    assert resp.status_code == 200
    data = resp.json()
    job_id = data.get("JobId")           # PascalCase — capital J and I
    assert job_id, "Missing JobId in response"

    # Step 3: Poll until Completed or Failed (300s timeout)
    poll_url = f"{BASE_URL}/api/test-auth/si-con-01/run/{job_id}"
    deadline = time.time() + 300
    final_data = {}
    status = None
    while time.time() < deadline:
        pr = requests.get(poll_url, timeout=TIMEOUT)
        assert pr.status_code == 200
        final_data = pr.json()
        status = final_data.get("Status")   # PascalCase
        if status in ("Completed", "Failed"):
            break
        time.sleep(5)
    else:
        assert False, f"Timeout waiting for job {job_id}"

    # Step 4: Assert convergence correctness
    assert status == "Completed"
    assert final_data.get("AllPassed") is True      # PascalCase
    iterations = final_data.get("Iterations") or [] # PascalCase
    assert len(iterations) > 0

    for iteration in iterations:
        exit_code = iteration.get("ExitCode")       # PascalCase
        if exit_code != 0:
            assert False, (
                f"ExitCode={exit_code}\n"
                f"StdoutTail: {iteration.get('StdoutTail')}\n"
                f"StderrTail: {iteration.get('StderrTail')}"
            )

test_si_con_01_contested_stock_deterministic_concurrency_convergence()
```

---

## 11. Key Rules for Future MCP Tests

### Rule 1 — Never use `&&` in PowerShell

```powershell
# WRONG — parser error in PowerShell
cd project && node script.js

# CORRECT — separate commands
node "C:\path\to\script.js" 2>&1 | Tee-Object -FilePath "output.log"
```

### Rule 2 — ASP.NET returns PascalCase JSON

Always probe endpoints manually first:

```powershell
Invoke-WebRequest -Uri "http://localhost:5010/api/endpoint" `
  -Method POST -Body '{}' -ContentType "application/json" `
  | Select-Object -ExpandProperty Content
```

Include exact key names in the test plan `description` field.

### Rule 3 — Protect your custom test plan

`testsprite_generate_backend_test_plan` overwrites the JSON every run.

```
SAFE WORKFLOW:
1. Bootstrap once  → creates .testsprite/config.json
2. Write code_summary.yaml manually
3. Generate PRD
4. Generate test plan → IMMEDIATELY restore your test plan backup
5. Execute tests
6. For all future runs: go straight to step 5 only
```

### Rule 4 — Bootstrap flow (one-time only)

```
testsprite_bootstrap
    ↓
testsprite_generate_code_summary  +  write code_summary.yaml
    ↓
testsprite_generate_standardized_prd
    ↓
testsprite_generate_backend_test_plan  →  restore custom test plan!
    ↓
testsprite_generate_code_and_execute   ← repeat this step for every run
```

### Rule 5 — Write highly explicit test plan descriptions

The remote TestSprite agent reads only your `description` to generate code.
Be extremely prescriptive and include constraints:

```json
{
  "description": "EXACT FLOW:
    STEP 1: POST /api/endpoint (no auth, no body) — assert 200.
    STEP 2: Read 'FieldName' (PascalCase, capital F) from response JSON.
    STEP 3: Poll every 5s, timeout 300s. Read 'Status' (PascalCase).
    STEP 4: Assert specific field values.
    DO NOT call any other endpoints.
    DO NOT approximate with a different approach.
    All response fields are PascalCase: FieldA, FieldB, FieldC."
}
```

### Rule 6 — Use `testIds` to run specific tests

```json
{ "testIds": ["TC010"] }
```

Saves time and avoids noise from other tests during focused debugging.

### Rule 7 — Check `test_results.json` after every run

```json
{
  "testStatus": "PASSED",   // or "FAILED"
  "testError": "",          // full Python traceback if failed
  "code": "..."             // exact Python code the remote agent ran
}
```

The `code` field is invaluable — it shows you exactly what the remote agent
generated and which line failed.

---

## 12. File Map

```
PointOfSale/
├── .testsprite/
│   └── config.json                          ← Created by bootstrap (do not edit)
│
└── testsprite_tests/
    ├── testsprite_backend_test_plan.json    ← YOUR test definitions (protect this!)
    ├── TC010_si_con_01_contested_stock_deterministic_concurrency_convergence.py
    │                                        ← Local reference copy of TC010 script
    ├── standard_prd.json                    ← Generated PRD (auto-managed)
    └── tmp/
        ├── code_summary.yaml               ← Required by TestSprite (write manually)
        ├── config.json                     ← TestSprite run config (auto-managed)
        ├── test_results.json               ← Results of last run (ground truth)
        ├── raw_report.md                   ← Human-readable report + dashboard link
        └── prd_files/                      ← Generated PRD fragments (auto-managed)
```

---

## 13. Quick Reference — Repeat Runs

After the initial bootstrap, this is all you need for every run:

```
1. Ensure API is running on port 5010
   → dotnet run --project PointOfSale.API --no-build --urls "http://localhost:5010"

2. Call testsprite_generate_code_and_execute:
   {
     "projectName": "PointOfSale",
     "projectPath": "c:\\Users\\fadya\\source\\repos\\PointOfSale",
     "serverMode": "development",
     "testIds": ["TC010"],
     "additionalInstruction": "<guidance for remote agent>"
   }

3. Run the node command returned by the tool in PowerShell (no &&):
   node "C:\Users\fadya\AppData\Local\npm-cache\_npx\...\index.js" generateCodeAndExecute

4. Check results:
   → testsprite_tests/tmp/test_results.json   (testStatus: PASSED / FAILED)
   → testsprite_tests/tmp/raw_report.md       (dashboard link)
```

---

## 14. SI-CON-02 Update - Replay During Active Transaction

**Final result:** TC011 PASSED on 2026-05-13

**Dashboard:**
https://www.testsprite.com/dashboard/mcp/tests/067bdbb2-9bdc-47bf-856b-d5fe5d7e5a25/f4073a82-2ff1-42b7-8d80-c98731237f6f

### Critical Lesson

Do **not** tell TestSprite to run `dotnet test` directly from remote Python.
The remote runtime does not have the Windows repository path
`c:\Users\fadya\source\repos\PointOfSale`, so direct subprocess execution fails
with:

```text
FileNotFoundError: No such file or directory: 'c:\\Users\\fadya\\source\\repos\\PointOfSale'
```

The correct model is:

1. Keep the local API running on `http://localhost:5010`.
2. TestSprite calls the dedicated local runner endpoint.
3. The local API process runs the authoritative focused xUnit test.
4. TestSprite polls the runner endpoint and asserts PascalCase response fields.

### TC011 Required Flow

Use this exact TestSprite flow for SI-CON-02:

```text
STEP 1: POST /api/test-auth/ensure-schema and assert HTTP 200.
STEP 2: POST /api/test-auth/si-con-02/run with {"repetitions":1}; read JobId exactly.
STEP 3: Poll GET /api/test-auth/si-con-02/run/{JobId} every 5 seconds until Status is Completed or Failed.
STEP 4: Assert Status == Completed, AllPassed == true, Iterations has at least one item, and every ExitCode == 0.
```

Do **not** call `/api/sales`, `/api/sales/*`, or `/api/debug/outbox/*`.
Do **not** synthesize replay/concurrency timing with HTTP calls. The xUnit test
owns the deterministic transaction overlap.

### Correct TC011 Additional Instruction

Use wording like this when calling `testsprite_generate_code_and_execute`:

```text
Execute ONLY TC011. This is an authoritative deterministic integration scenario.
TestSprite MUST NOT call /api/sales and MUST NOT synthesize replay HTTP flows.
TestSprite must call only the dedicated local runner endpoints:
POST /api/test-auth/ensure-schema,
POST /api/test-auth/si-con-02/run with {"repetitions":1},
then poll GET /api/test-auth/si-con-02/run/{JobId}.
Read PascalCase fields exactly: JobId, Status, AllPassed, Iterations,
ExitCode, StdoutTail, StderrTail.
Do NOT run dotnet test inside remote Python; the local runner endpoint executes
the focused xUnit test on the Windows machine.
Pass only when Status is Completed, AllPassed is true, Iterations has at least
one item, and every ExitCode is 0.
On failure print StdoutTail and StderrTail.
```

### Start API and Run TestSprite Together

Keep the API alive during TestSprite's tunnel probe:

```powershell
$ErrorActionPreference = 'Stop'
$apiOut = Join-Path (Get-Location) 'testsprite-api-start.log'
$apiErr = Join-Path (Get-Location) 'testsprite-api-start.err.log'
if (Test-Path -LiteralPath $apiOut) { Remove-Item -LiteralPath $apiOut -Force }
if (Test-Path -LiteralPath $apiErr) { Remove-Item -LiteralPath $apiErr -Force }

$env:REQUIRE_REAL_POSTGRES_TESTS = '1'
$api = Start-Process -FilePath 'dotnet' `
  -ArgumentList @('run','--project','PointOfSale.API','--no-build','--urls','http://localhost:5010') `
  -WorkingDirectory (Get-Location) `
  -RedirectStandardOutput $apiOut `
  -RedirectStandardError $apiErr `
  -WindowStyle Hidden `
  -PassThru

try {
  $ready = $false
  for ($i = 0; $i -lt 36; $i++) {
    if ($api.HasExited) { throw "API exited before readiness. ExitCode=$($api.ExitCode)" }
    try {
      $r = Invoke-WebRequest -Uri 'http://localhost:5010/health/live' -UseBasicParsing -TimeoutSec 3
      if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch {
      Start-Sleep -Seconds 5
    }
  }
  if (-not $ready) { throw 'API did not become ready on /health/live within 180 seconds.' }

  node C:\Users\fadya\AppData\Local\npm-cache\_npx\8ddf6bea01b2519d\node_modules\@testsprite\testsprite-mcp\dist\index.js generateCodeAndExecute
} finally {
  if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force }
}
```

### Runner Endpoint Requirement

The local runner must pass this environment variable into the child `dotnet test`
process:

```csharp
startInfo.Environment["REQUIRE_REAL_POSTGRES_TESTS"] = "1";
```

### Expected Generated Code Shape

The generated code must contain:

```python
requests.post("http://localhost:5010/api/test-auth/ensure-schema")
requests.post("http://localhost:5010/api/test-auth/si-con-02/run", json={"repetitions": 1})
requests.get(f"http://localhost:5010/api/test-auth/si-con-02/run/{job_id}")
```

and must **not** contain:

```python
subprocess.run(...)
requests.post("http://localhost:5010/api/sales", ...)
```

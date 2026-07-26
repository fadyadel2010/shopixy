# Fawry Final Updated Re-Certification

**Audit date:** 2026-07-15  
**Audit type:** Independent current-source re-certification after
`FAWRY_CERTIFICATION_CLOSURE_1` through `FAWRY_CERTIFICATION_CLOSURE_5`  
**Scope:** Audit only. No source, Runtime, architecture, configuration, migration,
or `.ai-context` change was authorized or made.

## Executive Summary

The five closure missions were re-evaluated against the current source rather
than accepted as historical assertions. Their substantive corrections remain
present:

- the active PayAtFawry request now carries the official required application
  fields and a deterministic numeric merchant reference;
- the charge response is signature-verified and exactly correlated before its
  reference is admitted;
- webhook credential authority is tenant-owned and capability-gated;
- a durable `RegistrationStarted` checkpoint commits before the provider
  mutation boundary and prevents blind charge recreation; and
- durable webhook artifacts are protected and replay authority is
  application-owned.

The frozen provider-neutral Runtime remains unchanged in responsibility and
contains no Fawry, provider transport, credential, SQL, EF Core, or application
orchestration concern. Application code remains the orchestration owner, Runtime
remains the payment decision authority, and `PaymentLifecycleService` remains
the single durable lifecycle writer.

Two previously unclosed High findings remain provable in current active source:

1. the application-to-adapter credential handoff is a reusable credential-bearing
   request object rather than a one-shot consume-and-clear handoff; and
2. non-success Fawry HTTP response values can survive ineffective key-name
   replacement and enter canonical provider-error and rejected-evidence metadata.

These are current-source architecture/security defects, not deployment,
sandbox, documentation, historical, or local-environment evidence gaps. They
block Reference Provider and software certification. No already certified
closure is reopened.

## Audit Basis

### Governance and certification record reviewed

The audit read all seven current `.ai-context` files and applied their authority,
evidence, and historical-finding rules. It also read:

- `PAYMENT_REFERENCE_FREEZE.md`
- `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`
- `STRIPE_FINAL_RECERTIFICATION.md`
- `PAYMOB_FINAL_RECERTIFICATION.md`
- `FAWRY_FINAL_RECERTIFICATION.md`
- `FAWRY_CERTIFICATION_CLOSURE_1.md`
- `FAWRY_CERTIFICATION_CLOSURE_2.md`
- `FAWRY_CERTIFICATION_CLOSURE_3.md`
- `FAWRY_CERTIFICATION_CLOSURE_4.md`
- `FAWRY_CERTIFICATION_CLOSURE_5.md`
- every Fawry discovery, foundation, adapter, bridge, webhook, convergence,
  contract-closure, production-readiness, sandbox-validation, certification,
  and certification-findings document under `docs/architecture`.

The current official contract was cross-checked against Fawry's published
[Create Payment Reference Number API](https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis),
[Server Notification V2](https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/server-notification-v2),
and [Payment Status V2](https://developer.fawrystaging.com/docs/sdks/payment-notifications/get-payment-status-v2)
documentation. The request field set, request signature, response signature,
notification signature, and provider reference/status shapes used by the active
implementation are consistent with those published contracts.

### Current-source evidence reviewed

The review traced the active path end to end:

`PaymentService` -> `FawryRuntimeApplicationService` -> `FawryRuntimeBridge` ->
`FawryPaymentAdapter` -> provider evidence -> `PaymentRuntimeOrchestrator` ->
application projection/checkpoint, plus `FawryWebhookController` -> tenant
webhook authority -> `FawryWebhookEvidenceAdapter` -> Runtime ->
`PaymentLifecycleService` -> replay/log/outbox SQL commit.

The review also inspected tenant configuration, Data Protection artifact
handling and backfill, health/telemetry registration, retry configuration,
storefront handoff admission/presentation, entity configuration, migration
source, model snapshot, DI composition, and all Fawry certification and
reliability tests.

## Closed Findings

| Historical or closure finding | Current determination | Current-source evidence |
|---|---|---|
| Raw webhook payload and raw message-signature persistence | **Resolved — remains closed** | `FawryWebhookController.cs:382-383` protects the payload and stores only a signature digest; the purpose-isolated protector and backfill remain registered. |
| Incomplete official active charge request | **Resolved — remains closed** | `PaymentService.cs:2566-2585` supplies customer data and language. `FawryPaymentAdapter.cs:111-160` requires and maps mobile, email, description, language, `PayAtFawry`, amount, charge items, expiry, and signature. `FawryChargeRequest` at lines 669-723 models the official wire fields. |
| Non-numeric/unstable merchant reference | **Resolved — remains closed** | `FawryMerchantReference.Build` derives the numeric reference used as both official `merchantRefNum` and the adapter idempotency projection (`FawryPaymentAdapter.cs:121-165`). |
| Charge response accepted without message integrity or exact correlation | **Resolved — remains closed** | `FawryPaymentAdapter.cs:54-77` refuses evidence before `FawryChargeResponseIntegrityValidator`. Lines 411-533 require the documented signed fields, verify SHA-256 in fixed time, and correlate merchant reference, amounts, method, optional currency/account/customer identity. |
| Webhook caller could supply reusable credentials or bypass tenant capability authority | **Resolved — remains closed** | `FawryWebhookRuntimeIntegrationService.cs:54-142` parses the notification, resolves application-owned webhook authority, and supplies only the resolved secure key to the adapter. `FawryTenantConfigurationResolver` verifies active tenant/payment-method/environment/feature/callback/certification authority before decrypting tenant credentials. |
| Provider charge mutation lacked a durable started checkpoint | **Resolved — remains closed** | `PaymentService.cs:2264-2335` commits `RegistrationStarted` before provider I/O. `FawryProviderCheckpointStore.cs:146-203` permits accepted-reference persistence only from that state; `RegistrationStarted` resumes fail closed to provider inquiry/webhook reconciliation rather than charge recreation. |
| Accepted provider reference could be projected separately from its creation event | **Resolved — remains closed** | `PaymentService.cs:2415-2443` applies the accepted checkpoint and writes the `PaymentIntentCreated` outbox event in the same application SQL transaction before commit. |
| Simulated/local-only Fawry reference generation | **Resolved — remains closed** | The active application path reaches the HTTPS charge adapter through the sole Fawry application service and bridge; no competing active creation path was found. |
| Custom raw-payload HMAC or obsolete notification contract | **Resolved — remains closed** | `FawryWebhookEvidenceAdapter.cs:299-350` verifies the official ordered Notification V2 SHA-256 `messageSignature` with fixed-time comparison. |
| Missing durable webhook replay authority | **Resolved — remains closed** | `FawryWebhookRuntimeIntegrationService.cs:192-237,423-475` loads and appends prior evidence. `ApplicationDbContext.cs:785-803` and migration `20260715120000_FawryContractClosure2WebhookReplay` define tenant/provider/event uniqueness. |
| Runtime provider coupling, persistence, transport, or provider-owned stage sequencing | **False positive / not present — remains closed** | A current Runtime scan found no Fawry, Stripe, Paymob, HTTP client, EF Core, DB context, SQL, API key, secure key, or credential reference. Runtime's orchestrator remains the only canonical stage sequencer. |
| Storefront payment finality or provider-reference authority | **False positive / not present — remains closed** | `paymentSessionAuthority.ts` admits Fawry only as a pending backend handoff with a complete backend reference; `CheckoutSuccess.tsx` only presents that reference. No client-side paid/final transition was found. |
| Duplicate lifecycle writer or direct controller mutation | **False positive / not present — remains closed** | `FawryWebhookController.cs:199-238` calls `PaymentLifecycleService` and persists replay evidence only after successful lifecycle processing, inside the same serializable application transaction. |

## Remaining Findings

Only the following findings block this certification decision.

### 1. FAWRY-FINAL-005 — Credential handoff is reusable rather than one-shot

- **Determination:** Still Valid
- **Severity:** High
- **Affected certification:** Architecture, Security, Implementation, Software,
  Reference Provider
- **Current evidence:**
  - `FawryRuntimeApplicationService.cs:41-66` resolves tenant credentials, creates
    `FawryPaymentCredentials`, embeds them in the execution request, and invokes
    the bridge.
  - `FawryRuntimeBridge.cs:62-110` exposes `Credentials` as a public init-only
    property on `FawryExecutionRequest` and retains the credential record for the
    lifetime of that reusable request.
  - `FawryRuntimeBridge.cs:31-40` reads the retained credentials on every
    `ExecuteAsync` invocation.
  - There is no consume operation, one-use token, consumed state, overlap guard,
    clear-after-read behavior, or failure-path clearing. Calling the bridge
    again with the same request reuses the same merchant/API/secure-key values.
- **Why this blocks Reference Provider certification:**
  `PAYMENT_REFERENCE_FREEZE` makes single-use, operation-scoped,
  overlap-safe, consume-before-I/O, clear-after-consumption credential handoff a
  mandatory Reference Provider invariant. Correct tenant credential resolution
  does not satisfy that separate boundary rule. The active bridge therefore
  cannot be the reusable template for future providers.
- **Correction boundary:** Application/bridge/adapter credential handoff only;
  no Runtime change.

### 2. FAWRY-FINAL-006 — Provider error response values can cross the adapter boundary

- **Determination:** Still Valid
- **Severity:** High
- **Affected certification:** Security, Provider Adapter, Application Contract,
  Artifact Protection, Software, Reference Provider
- **Current evidence:**
  - `FawryPaymentAdapter.cs:104-107` reads up to 512 characters of any non-success
    HTTP response body.
  - `FawryPaymentAdapter.cs:233-256` puts the result in canonical error metadata
    as `provider_diagnostic`.
  - `FawryPaymentAdapter.cs:327-337` replaces only the text of the names
    `signature`, `secureKey`, and `apiKey`; it neither parses the payload nor
    removes the corresponding values. For example, a body containing
    `{"signature":"secret"}` becomes `{"redacted":"secret"}`.
  - `FawryRuntimeBridge.cs:315-347` copies all canonical error metadata into
    rejected `ProviderEvidence`, allowing the surviving value to cross the
    provider adapter boundary and enter application/Runtime evidence objects.
  - `FawryProviderAdapterCertificationTests.cs:116-130` supplies the value
    `secret` but asserts only that the strings `secure-key` and `signature` are
    absent; it does not assert that the sensitive value is absent.
- **Why this blocks Reference Provider certification:**
  `PAYMENT_REFERENCE_FREEZE` requires bounded, allow-listed, redacted canonical
  diagnostics and forbids credentials, reusable artifacts, raw signatures, and
  customer-sensitive provider payloads from crossing the adapter boundary.
  Arbitrary provider body values violate that security contract even when the
  key name is changed.
- **Correction boundary:** Active Fawry adapter error translation and its tests;
  no Runtime change.

## Reclassified Findings

These items remain relevant but are not additional blockers in this decision.

| Finding | Determination | Classification | Evidence and certification effect |
|---|---|---|---|
| Authenticated `FAILED`, `EXPIRED`, `CANCELED`, and `CANCELLED` notifications are identified as terminal but skipped | **Still Valid** | **Medium / Operational recovery** | `FawryWebhookRuntimeIntegrationService.cs:398-419` admits only positive/refund statuses to Runtime and allows lifecycle persistence only for paid/captured. The controller returns processed/skipped. This is fail-closed—no false paid state or second writer occurs—but negative terminal convergence depends on inquiry/reconciliation. It does not add a money-authority blocker to the two High findings above. |
| Fawry operational metrics are declared but not used by executing paths | **Still Valid** | **Operational / Deployment** | `FawryOperationalTelemetry` is registered and exported, while current tests explicitly confirm it is absent from active payment/webhook paths. This is production observability evidence, not a Runtime or provider-authority defect. |
| `FawryOptions.RetryCount` is not applied to the active named client | **Operational** | **Operational / Documentation** | Blind HTTP retry of the non-idempotent charge mutation would be unsafe after `RegistrationStarted`; production retry policy must remain inquiry/reconciliation-aware. The unused option and runbook semantics require deployment governance, not a Runtime change. |
| EF model snapshot does not contain `FawryWebhookReplayRecords` | **Still Valid** | **Deployment / Migration (Medium)** | The migration is source-valid and `dotnet ef migrations list --no-connect` lists `20260715120000_FawryContractClosure2WebhookReplay`, but `dotnet ef migrations has-pending-model-changes` reports model changes since the last migration and the snapshot has no Fawry replay entity. This prevents overall migration certification and must be resolved before a governed release, but does not negate the current migration's explicit table/index SQL. |
| Four Fawry reliability tests fail against the local PostgreSQL database | **Local Environment** | **Local Environment / Deployment** | Each fails before Fawry execution because local `Orders` lacks `BillingAddressAdmissionEvidenceId`. This proves the local schema is behind current source; it is not evidence of a Fawry replay/lifecycle regression. |
| Three legacy Fawry authority-certification assertions fail | **Historical Assertions** | **Historical Assertions / Documentation** | The assertions search old source strings and `PASS SF-PAY7`; they do not execute the current closed application path. Current closure/architecture tests independently pass. They should not reopen certified request, webhook, or checkpoint closures. |
| Live sandbox credentials, callback delivery, provider reference, replay, and recovery proof are absent | **External** | **External / Sandbox Evidence** | No provider-side live sandbox execution was performed or evidenced in this audit. This remains a deployment gate, not a software-authority finding. |
| CommerceHub refunds/capture/void/inquiry activation | **Out of Scope** | **Out of Scope** | No active CommerceHub Fawry path was found. Translation-only advanced-operation code is not application-owned active execution and is not part of the certified reference-creation/webhook slice. |
| General repository/browser-data and bundle-size warnings | **Out of Scope** | **Documentation / Operational** | The storefront production build succeeds; these warnings do not alter Fawry payment authority. |

## Regression Summary

### Verification executed against the current tree

| Verification | Result | Interpretation |
|---|---:|---|
| Complete `FullyQualifiedName~Fawry` test aggregate | **135 passed / 7 failed / 142 total** | Four failures are local-schema drift; three are historical string/document assertions. No closure behavioral test failed. |
| Current Fawry architecture/closure slice excluding the historical authority class | **134 passed / 0 failed** | Current request, response-integrity, webhook authority, checkpoint, artifact, convergence, operations-source, and sandbox-source contracts remain green. |
| `FullyQualifiedName~PaymentRuntime` | **94 passed / 0 failed** | Provider-neutral Runtime behavior and determinism remain green. |
| API/Core/Infrastructure/Test compilation during test execution | **Succeeded** | Current .NET source compiles before test execution. |
| Storefront `npm run build` | **Succeeded** | TypeScript and Vite production build completed; warnings were non-Fawry operational/bundling warnings. |
| EF migration list without database connection | **Fawry replay migration listed** | Migration source is discoverable. |
| EF pending-model check | **Failed: pending model changes** | Confirms model-snapshot/deployment drift; it does not prove a Runtime or active Fawry code regression. |

### Regression determinations

- **Runtime regression:** none found.
- **Provider protocol leakage into Runtime:** none found.
- **Lifecycle-writer regression:** none found.
- **Replay regression:** none found in current source or applicable green tests.
- **Duplicate-prevention regression:** none found; SQL log and replay uniqueness
  remain tenant scoped and transactionally enforced.
- **Checkpoint regression:** none found; the started/accepted/completed state
  machine remains fail-closed.
- **Artifact-protection regression:** none found.
- **Closed official request/response/webhook contract regression:** none found.
- **Security regression:** no newly introduced regression was identified, but
  security certification remains unmet because the two High findings were never
  closed and remain active.

## Architecture Summary

| Required confirmation | Current determination | Evidence basis |
|---|---|---|
| Runtime frozen | **Yes** | No Runtime source or responsibility change was required by any Fawry closure. |
| Provider-neutral Runtime preserved | **Yes** | Source scan is clean; 94/94 Runtime tests pass. |
| Application owns orchestration | **Yes** | Creation, credential resolution, checkpointing, webhook authority, lifecycle call, replay persistence, and recovery policy remain in application/infrastructure code. |
| Runtime owns payment authority | **Yes** | Provider evidence passes through `PaymentRuntimeOrchestrator`; application persistence is gated by exact Runtime outcomes. |
| Lifecycle remains single writer | **Yes** | Fawry webhook persistence delegates to `PaymentLifecycleService`; controller and adapters do not mutate payment finality directly. |
| No provider protocol leakage | **Yes** | Fawry wire fields/signatures remain in provider adapters/application boundaries. |
| No credential leakage | **No** | No credentials enter Runtime or durable artifacts on the success path, but the reusable handoff and arbitrary provider-error value propagation violate the frozen credential/error boundaries. |
| No replay or duplicate regression | **Yes, source-level** | Replay prior evidence, log idempotency, and tenant/provider/event uniqueness remain in place. Target-database application is deployment evidence still pending. |
| No checkpoint regression | **Yes** | Durable started checkpoint commits before provider mutation and ambiguous outcomes fail closed. |
| No security regression | **No overall certification** | Closed artifact, response-integrity, and webhook-authority controls remain intact; two previously unclosed High security findings remain. |

No new permanent cross-provider architectural truth was discovered. Therefore no
`.ai-context` update is warranted.

## Deployment Summary

The current source contains production-oriented health checks, validated
tenant configuration, environment-specific HTTPS endpoints, protected webhook
artifacts, replay persistence, and checkpoint recovery. Those controls are not
enough for deployment approval because:

1. the two High current-source blockers must be corrected and independently
   re-certified;
2. the EF snapshot/pending-model drift must be reconciled and the actual target
   migration state verified;
3. live executing-path metrics and inquiry-aware retry/recovery procedures need
   operational proof; and
4. official sandbox charge, callback, duplicate/replay, ambiguous-response, and
   recovery evidence remains external and pending.

Deployment and live-sandbox evidence are therefore not the only work remaining.

## Certification Decision

| Certification question | Decision | Basis |
|---|---|---|
| Architecture certified? | **No** | The mandatory one-shot credential handoff invariant is not implemented. |
| Implementation certified? | **No** | Two active High boundary defects remain. |
| Security certified? | **No** | Reusable credential handling and provider-error value propagation remain. |
| Operations certified? | **No** | Executing-path observability, target deployment, and live recovery evidence remain pending. |
| Migration certified? | **No overall** | The Fawry replay migration is discoverable and source-valid, but EF model drift and target application remain unresolved. |
| Regression certified? | **No overall** | Applicable current slices are green, but the complete aggregate is not green and two unclosed High findings lack closure proof. |
| Reference Provider certified? | **No** | The two numbered High findings violate mandatory Reference Provider boundaries. |
| Provider-neutral Runtime preserved? | **Yes** | Runtime is provider neutral and 94/94 tests pass. |
| Architecture frozen? | **Payment architecture and Runtime: yes. Fawry implementation: no.** | Corrections belong only in application/adapter boundaries. |
| Software certified? | **No** | The two High findings are software defects, not deployment-only evidence gaps. |
| Deployment pending only? | **No** | Source correction remains necessary. |
| Live Sandbox pending only? | **No** | Source correction, migration governance, and then sandbox proof remain. |
| Ready for future providers? | **Yes against the frozen Stripe/Paymob reference architecture; no using Fawry as the template.** | Runtime neutrality is preserved, but Fawry is not yet a certified Reference Provider. |

## Final Decision

**WITHHOLD CERTIFICATION**

The only blocking findings are the two numbered High findings in **Remaining
Findings**. Closed findings are not reopened, and operational, external,
out-of-scope, documentation, historical-assertion, local-environment,
deployment, migration-evidence, and sandbox-evidence items are not used as
additional software-certification blockers.

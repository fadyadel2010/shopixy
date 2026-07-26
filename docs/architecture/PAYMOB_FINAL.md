# PAYMOB-FINAL End-to-End Production Certification

Date: 2026-07-14  
Audit posture: independent certification review of the current working tree  
Reference provider: Stripe  
Payment Runtime posture: permanently frozen  
Final decision: **CERTIFICATION WITHHELD**

## Executive Summary

Paymob is **not Software Certified**, **not Security Certified**, **not Operations Certified**, and **not eligible to become the Paymob provider template** in the current working tree.

The provider-neutral Payment Runtime remains clean, provider-neutral, SQL-free, and frozen. Stripe remains Source/Software Certified as the reference provider. The Paymob replay migration is discoverable and generates the expected table and indexes. The solution compiles, the Storefront production build succeeds, and most focused Paymob tests pass.

Those positive facts do not close the certification. Independent source review found one Critical and five High implementation/security findings:

1. The POS/Storefront Paymob webhook path invokes the Runtime but does not require the required Runtime outcome before executing `ProcessPaymobTransactionAsync` and the SQL lifecycle writer.
2. The create-payment adapter accepts a Paymob order and then creates the payment key without a durable application checkpoint for the accepted Paymob order ID. A failure in the second step loses the recovery handle and can reopen provider execution.
3. Webhook/callback credential resolution can select soft-deleted tenant Paymob configuration and does not enforce the global environment/capability gates used by the reference provider.
4. Tenant Paymob credentials are resolved only as a validation exercise; the adapter executes with global `PaymobOptions`. Distinct tenant merchant credentials therefore cannot govern their own provider calls.
5. Active CommerceHub Paymob payment, webhook, recovery, refund, and inquiry flows execute direct provider HTTP outside the certified Paymob adapter/Runtime pipeline.

Security certification is additionally blocked because Paymob persists raw webhook payloads and raw HMAC signatures in `PaymentWebhookLogs`, and persists payment keys in plaintext JSON routing metadata. Stripe, the reference provider, redacts webhook payloads, hashes signatures, and encrypts its comparable client-secret artifact.

Operational certification is withheld because Paymob metrics are declared and exported but never recorded by production Paymob code, and `RetryCount` is exposed as governed configuration without governing the `PaymobOfficialApi` client. Regression certification is also withheld: the combined Paymob/Payment Runtime filter produced 189 passes and one failure.

Live operational certification is separately withheld. This workspace contains no official Paymob Sandbox credentials, enabled Sandbox tenant, public callback/webhook delivery evidence, live payment/callback/webhook evidence, advanced-operation evidence, attack-drill evidence, or measured latency/throughput.

This is not a deployment-only hold. Source implementation closure is required. No Runtime change is required or authorized.

## Audit Basis

The audit reviewed every file under `.ai-context`, all Paymob architecture records in `docs/architecture`, the mandated Payment Runtime and Stripe final certifications, the root Paymob integration records, current implementation, DI/health/telemetry registration, controllers, provider adapters, application services, webhook admission, lifecycle handoff, replay persistence, EF migration metadata/model snapshot, focused tests, and Storefront build output.

Controlling evidence included:

- `PAYMOB_1_FOUNDATION_CERTIFICATION.md`
- `PAYMOB_2_PROVIDER_ADAPTER.md`
- `PAYMOB_3_RUNTIME_BRIDGE.md`
- `PAYMOB_4_WEBHOOK_EVIDENCE_ADAPTER.md`
- `PAYMOB_5_ADVANCED_OPERATIONS.md`
- `PAYMOB_CONTRACT_CLOSURE_1.md`
- `PAYMOB_CONTRACT_CLOSURE_2.md`
- `PAYMOB_CONVERGENCE_1.md`
- `PAYMOB_PRODUCTION_1_OPERATIONAL_READINESS.md`
- `PAYMOB_PRODUCTION_2_SANDBOX_VALIDATION.md`
- `PAYMOB_SECURITY_HOTFIX_1_LOG_REDACTION.md`
- `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`
- `STRIPE_FINAL_RECERTIFICATION.md`
- `PAYMENT_0_5B_PAYMOB_DISCOVERY.md`
- `paymob-payment-authority-certification-findings.md`

Earlier phase certifications are historical evidence, not controlling proof. This report is the controlling Paymob certification decision for the audited working tree.

## Architecture Review

### Preserved architecture

The canonical Runtime under `PointOfSale.Core/Payments/Runtime` contains no Paymob, Stripe, provider HTTP, EF Core, SQL, controller, credential, or frontend logic. The canonical sequence remains:

```text
ProviderEvidence
  -> Admission
  -> Verification
  -> Decision
  -> State Transition
  -> Transaction
  -> Settlement
  -> RuntimeResult
```

`PaymentRuntimeOrchestrator` remains the only Runtime sequencer. No Runtime modification is required by any finding in this report.

The intended POS/Storefront create pipeline is visible:

```text
Storefront controller
  -> PaymobPaymentService
  -> PaymobRuntimeApplicationService
  -> PaymobRuntimeBridge
  -> PaymobPaymentAdapter
  -> ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> application mapping
```

The intended webhook pipeline is also visible, including application-owned replay persistence and the single SQL lifecycle writer. The defect is not the absence of these layers; it is failure to enforce their output before mutation.

### Architecture defects

The active Paymob surface is not one converged architecture:

```text
POS / Storefront
  -> certified Paymob application/adapter/Runtime path

CommerceHub
  -> CommerceHubPaymentService / CommerceHubRefundGatewayService
  -> unnamed HttpClient
  -> direct Paymob endpoints
  -> CommerceHub-local webhook/lifecycle handling
```

`PAYMOB_CONVERGENCE_1.md` explicitly excluded CommerceHub. PAYMOB-FINAL does not: its mission is the entire Paymob provider implementation and asks whether the implementation is ready for CommerceHub reuse. The excluded path is active source, selected by production provider switches, and includes direct auth, order, payment-key, refund, inquiry, recovery, and webhook execution. It therefore prevents whole-provider architecture certification.

Paymob also imports provider-neutral-looking error and bridge types from `PointOfSale.API.Services.Payments.Stripe` in the adapter, advanced adapter, Runtime bridge, Runtime application service, and webhook adapter. `PaymentRuntimeOrchestrator` is registered by `AddStripeFoundation`, not the Paymob registration. The current application compiles because Stripe is present, but Paymob is not an independently composable provider package and is not a sound template boundary.

**Architecture decision: Not certified.**  
**Provider-neutral Runtime decision: Preserved and frozen.**

## Implementation Review

### Create-payment flow

The current adapter sequence in `PaymobPaymentAdapter.cs` is:

```text
AuthenticateAsync
  -> RegisterOrderAsync
  -> CreatePaymentKeyAsync
  -> return ProviderEvidence
```

The accepted Paymob order ID exists after `RegisterOrderAsync` at lines 68-73, but no application persistence callback or recovery record occurs before `CreatePaymentKeyAsync` at lines 76-79. `PaymobPaymentService` persists the provider order ID only after the complete Runtime application call returns, at lines 549-559 and 683-693.

If Paymob accepts the order and payment-key creation times out, fails, or returns an unusable response, `PaymobPaymentAdapterResult.Failed` carries only a canonical error. The accepted provider order ID is lost to the application. `MarkPersistedPaymobIntentFailedAsync` then marks the local claim failed. A subsequent retry can reopen provider order execution. A deterministic `merchant_order_id` reduces risk but is not evidence that Paymob will retrieve and return the previously accepted order rather than reject or duplicate the request.

This directly contradicts the permanent architecture truth in `.ai-context/ARCHITECTURE_TRUTH.md`: once Paymob accepts an order ID, payment-key failures must recover that accepted order and must not create a second order.

### Webhook outcome admission

`PaymobPaymentService.ProcessWebhookAsync` calls `EvaluateAndExecuteAsync` at lines 943-951. It handles replay/duplicate, invalid callback, and non-terminal/ignored classifications, then begins persistence at line 1002 and calls `ProcessPaymobTransactionAsync` at line 1104.

There is no Paymob equivalent of the Stripe reference guard at `StripePaymentService.cs:1054-1068`:

```text
if application admission required Runtime
and required Runtime outcome was not produced
  -> throw
  -> no lifecycle mutation
```

Consequently, a successful or refund callback classified for Runtime can still reach `HandlePaymentResultAsync` or `RecordRefundAsync` when the Runtime adapter/result rejected it. This is a Runtime bypass at the application interpretation boundary even though the Runtime was technically invoked.

### Adapter and controller boundaries

- POS/Storefront controllers delegate to `IPaymobPaymentService`; no controller-to-Paymob-adapter direct call was found.
- `PaymobPaymentService` no longer performs direct Paymob HTTP for create-payment execution. The prior direct POS/Storefront path is closed.
- `PaymobAdvancedOperationsAdapter` has no production caller. Refund, partial refund, void, capture, and inquiry are translation components only, not certified active command flows.
- CommerceHub has active direct Paymob HTTP execution at `CommerceHubPaymentService.cs:321`, `2490-2559`, `4503-4698` and `CommerceHubRefundGatewayService.cs:208-261`, `381-448`, `608-618`.

**Implementation decision: Not certified.**

## Security Review

### Credential governance

The create application service resolves encrypted tenant configuration but discards the returned credentials at `PaymobRuntimeApplicationService.cs:55`. The adapter builds requests from injected global `PaymobOptions` at `PaymobPaymentAdapter.cs:41-57`. Tenant configuration is accepted only when every credential exactly equals the global values at `PaymobRuntimeApplicationService.cs:221-224`.

This fails the documented multi-tenant claim that each tenant owns its Paymob merchant credentials. It is fail-closed against a mismatched tenant, but it is not tenant-scoped provider execution and cannot support distinct merchant accounts.

Webhook/callback resolution is weaker. `GetPaymobConfigurationUnfilteredAsync` bypasses query filters and checks `TenantId`, code, enablement, and ciphertext presence, but omits `!pm.IsDeleted` at `PaymobPaymentService.cs:1878-1885`. It also does not enforce global provider enablement, Sandbox/Production alignment, or the callback/webhook capability flags.

The feature flags themselves are not usable operational gates: `PaymobCredentialValidator` rejects any enabled payment, callback, webhook, or refund execution flag at `CommerceHubPaymobOptions.cs:168-173`. Production webhook execution does not consult these flags.

### Sensitive audit persistence

The create-path structured log hotfix is valid: current Paymob success logs omit `PaymentKey`, `IframeUrl`, API keys, HMAC secrets, access tokens, and Authorization values.

That does not close durable logging/audit storage. Paymob writes raw provider payloads and raw HMAC signatures into `PaymentWebhookLogs` at `PaymobPaymentService.cs:2252-2270` and `2309-2327`. Paymob payloads include provider/customer transaction fields including `source_data.pan`. The Stripe reference uses `WebhookDataRedactor.RedactPayload` and a SHA-256 signature digest at `StripePaymentService.cs:2231-2232` and `2292-2293`.

Paymob payment keys are also persisted as plaintext JSON in `PaymentIntent.ProviderMetadata` through `PaymentIntentRoutingMetadataSerializer`. The serializer provides no Data Protection boundary. The artifact is time-limited but sensitive; omission from text logs does not make plaintext durable storage equivalent to Stripe's encrypted client-secret handling.

### HMAC and ingress

Positive controls:

- bounded webhook request bodies and rate limiting are present;
- tenant resolution is server-authoritative and advisory tenant headers are mismatch-checked;
- HMAC-SHA512 comparison uses fixed-time byte comparison;
- callback redirect does not write payment success;
- invalid signatures fail closed;
- replay and webhook-log unique indexes exist.

Timestamp tolerance exists in the evidence adapter but the production Paymob webhook call does not supply a tolerance. This is not the primary replay fence, but live security validation must prove the chosen delayed-delivery/replay policy.

**Security decision: Not certified.**

## Operational Review

### Health and registration

The following are registered and compile:

- `paymob_foundation` health check;
- `paymob_operational_readiness` health check;
- Runtime bridge, application service, webhook integration, evidence adapter, and provider adapter;
- replay-store and webhook-audit-store probes;
- OpenTelemetry meter `PointOfSale.Payments.Paymob`.

The operational readiness health check fails closed for unavailable database/tables, missing enabled encrypted tenant configuration, invalid foundation options, and missing service registrations.

### Telemetry and retry gaps

`PaymobOperationalTelemetry` declares thirteen counters and the meter is exported, but repository search finds no production injection or call to its `Record*` methods. The counters therefore cannot substantiate provider failure, webhook, replay, duplicate, Runtime admission/rejection, refund, void, capture, recovery, or retry operations.

`PaymobOptions.RetryCount` is validated and displayed by health, but is not consumed by the `PaymobOfficialApi` client. The adapter uses the named client `PaymobOfficialApi`; the separately configured `Paymob` client receives the shared retry policy but is not used by the certified adapter. Operational documentation therefore overstates governed retry behavior.

Runbooks and alert matrices exist as documentation. No deployed dashboard, alert route, firing test, on-call acknowledgement, retention job, or live drill evidence exists in this workspace.

**Operations decision: Not certified.** Source operational artifacts exist, but source observability and configuration governance are incomplete; live operations are also unproven.

## Migration Review

### Replay migration

The migration `20260713120000_PaymobContractClosure2WebhookReplay` is discoverable by EF Core. The model snapshot contains `PaymobWebhookReplayRecord`. Generated idempotent SQL contains:

- `CREATE TABLE "PaymobWebhookReplayRecords"`;
- `IX_PaymobWebhookReplayRecords_Tenant_EvidenceHash`;
- unique `IX_PaymobWebhookReplayRecords_Tenant_Provider_Event`;
- `IX_PaymobWebhookReplayRecords_Tenant_ProviderReference`;
- the migration-history insert.

This Paymob replay migration artifact is source-certified. Applying it and verifying it in every target database remain deployment work.

EF Core separately reports: `Changes have been made to the model since the last migration.` The audit did not create a migration because this is certification-only. The pending drift is repository-wide and is not proven to originate in Paymob, but it prevents an unqualified whole-model migration declaration.

### Provider protocol migration

`PAYMENT_0_5B_PAYMOB_DISCOVERY.md` records that current official Paymob direction uses the Intention API with Unified Checkout/Pixel, mode-specific Secret/Public keys, callback URLs, and regional governance. The implemented adapter still uses the legacy auth/order/payment-key/IFrame flow and Egypt base URL. This may remain usable for an existing enabled merchant, but it is not suitable as a new-merchant or multi-region Paymob template without an explicit supported-legacy decision and live merchant evidence.

**Migration decision: Not certified overall.** The replay migration artifact is certified; whole-model and provider-protocol migration readiness are not.

## Regression Review

Independent commands executed on 2026-07-14:

| Evidence | Result |
|---|---:|
| `dotnet build PointOfSale.Tests --no-restore` | Passed; 0 warnings, 0 errors |
| Paymob or Payment Runtime focused filter | **189 passed, 1 failed** |
| Payment Runtime-only filter | 94 passed, 0 failed |
| Paymob filter excluding `PaymobPaymentAuthorityCertificationTests` | 93 passed, 0 failed |
| `PaymobPaymentAuthorityCertificationTests` | 3 passed, 1 failed |
| Stripe or Payment Runtime reference filter | 206 passed, 0 failed |
| `PointOfSale.Storefront` production build | Passed; non-blocking Vite bundle/import warnings |
| EF migration discovery | Paymob replay migration listed |
| EF idempotent SQL inspection | Paymob table/index/history statements present |
| EF pending-model check | Failed: model changes exist after last migration |

The failing Paymob test is `CertificationArtifacts_MustRecordSfPay5Truth`; it expects `PASS SF-PAY5` in permanent `.ai-context` artifacts. The architecture findings in this audit are violations of existing cross-provider truths, not a new permanent truth, so `.ai-context` is not modified merely to make the guard pass.

The focused suite is valuable but insufficient by itself. Much of the Paymob certification suite is source-string/shape coverage; it does not simulate Runtime rejection followed by lifecycle handling, provider-order acceptance followed by payment-key failure, soft-deleted webhook credential selection, distinct tenant credentials, or live provider behavior.

**Regression decision: Not certified.**

## Production Readiness

Certification layers are deliberately separate:

| Layer | Decision | Reason |
|---|---|---|
| Source Certification | **Withheld** | Open Critical/High implementation and security findings |
| Operational Readiness | **Withheld** | Metrics are not emitted; retry/capability governance is incomplete |
| Deployment Readiness | **Withheld** | Source blockers, pending model drift, migrations/secrets/endpoints not verified in a target environment |
| Live Operational Evidence | **Not certified** | No official live/Sandbox execution evidence |

### Live evidence matrix

| Required evidence | Official evidence found? | Decision |
|---|---:|---|
| Sandbox credentials | No | External dependency pending |
| Sandbox tenant | No | External/deployment dependency pending |
| Public callback endpoint reachability | No | Not certified |
| Public webhook endpoint reachability | No | Not certified |
| Live payment | No | Not certified |
| Live callback | No | Not certified |
| Live webhook | No | Not certified |
| Refund | No live evidence | Adapter/source evidence only |
| Void | No live evidence | Dormant adapter only |
| Capture | No live evidence | Dormant adapter only |
| Replay attack | No live/concurrent drill evidence | Source fences only |
| Duplicate attack | No live/concurrent drill evidence | Source fences only |
| Latency | No measurements | Targets only |
| Throughput | No measurements | Not certified |

Live operation must not be inferred from compilation, unit tests, migration generation, health registration, or documented targets.

## Outstanding Findings

### PAYMOB-FINAL-001 — Runtime rejection does not block lifecycle mutation

- **Classification:** Critical
- **Root cause:** `PaymobPaymentService` treats application admission classification and successful Runtime execution as separate facts but gates only on replay/invalid/non-terminal classification. It never enforces the required Runtime result before calling the lifecycle path.
- **Responsible layer:** Paymob application webhook contract/outcome-admission layer.
- **Impact:** Successful/refund provider evidence can reach SQL payment/refund mutation after the frozen Runtime rejected or failed the required execution.
- **Evidence:** `PaymobPaymentService.cs:943-1002`, `1104`; reference guard `StripePaymentService.cs:1054-1068`.
- **Recommended closure phase:** `PAYMOB-CONTRACT-CLOSURE-3 Runtime Outcome Enforcement`.
- **Certification effect:** Blocks Architecture, Implementation, Security, Software, and Paymob template certification.

### PAYMOB-FINAL-002 — Accepted Paymob order is not durably checkpointed before payment-key execution

- **Classification:** High
- **Root cause:** Adapter composition hides the intermediate accepted order ID until auth, order, payment key, evidence, and Runtime work finish.
- **Responsible layer:** Provider adapter/application persistence recovery handoff.
- **Impact:** Timeout/failure after provider order acceptance can lose the recovery handle and reopen provider execution, risking duplicate/stranded provider sessions.
- **Evidence:** `PaymobPaymentAdapter.cs:59-87`; `PaymobPaymentService.cs:519-559`, `683-693`; `.ai-context/ARCHITECTURE_TRUTH.md:258-259`.
- **Recommended closure phase:** `PAYMOB-RECOVERY-1 Provider Session Checkpoint and Inquiry`.
- **Certification effect:** Blocks Implementation, Recovery, Replay/Idempotency, and Software certification.

### PAYMOB-FINAL-003 — Deleted and environment-ungoverned webhook credentials remain admissible

- **Classification:** High
- **Root cause:** Unfiltered tenant configuration lookup omits the soft-delete predicate and does not use a shared capability/environment resolver.
- **Responsible layer:** Application credential authority.
- **Impact:** A deleted tenant method can remain HMAC authority; global provider disablement and Sandbox/Production/capability gates do not reliably stop webhook/callback execution.
- **Evidence:** `PaymobPaymentService.cs:1874-1904`; contrast `StripeTenantConfigurationResolver.cs:57-85`, `99-126`, `164-184`.
- **Recommended closure phase:** `PAYMOB-SECURITY-CLOSURE-1 Tenant Credential and Capability Authority`.
- **Certification effect:** Blocks Security, Operations, and Software certification.

### PAYMOB-FINAL-004 — Provider execution is global-credential-bound, not tenant-credential-bound

- **Classification:** High
- **Root cause:** Tenant credentials are decrypted and validated, then discarded; the adapter consumes global `PaymobOptions`. Exact equality is used instead of a scoped runtime credential accessor.
- **Responsible layer:** Runtime application/adapter credential handoff.
- **Impact:** Distinct tenant merchant accounts cannot execute through their own credentials; the documented multi-tenant capability is false and CommerceHub reuse cannot safely scale.
- **Evidence:** `PaymobRuntimeApplicationService.cs:55`, `112-170`, `206-225`; `PaymobPaymentAdapter.cs:41-57`.
- **Recommended closure phase:** `PAYMOB-SECURITY-CLOSURE-1 Tenant Credential and Capability Authority`.
- **Certification effect:** Blocks Implementation, Security, CommerceHub reuse, and template certification.

### PAYMOB-FINAL-005 — Active CommerceHub direct Paymob execution bypasses the certified provider pipeline

- **Classification:** High
- **Root cause:** Earlier convergence deliberately excluded CommerceHub and left provider-specific execution in CommerceHub services.
- **Responsible layer:** CommerceHub payment/refund application integration.
- **Impact:** Duplicate auth/order/payment-key/refund/inquiry/webhook implementations, unnamed HttpClients, separate credential behavior, separate replay semantics, and no canonical Runtime outcome authority for the entire provider surface.
- **Evidence:** `CommerceHubPaymentService.cs:321`, `620`, `2490-2559`, `4503-4698`; `CommerceHubRefundGatewayService.cs:208-261`, `381-448`, `608-618`.
- **Recommended closure phase:** `PAYMOB-COMMERCEHUB-CONVERGENCE-1`.
- **Certification effect:** Blocks Architecture, Implementation, CommerceHub reuse, and Paymob template certification.

### PAYMOB-FINAL-006 — Sensitive webhook/payment artifacts are durably stored without reference-provider redaction/encryption

- **Classification:** High
- **Root cause:** Paymob audit persistence predates the Stripe redaction/digest closure; Paymob payment-key routing metadata has no protector.
- **Responsible layer:** Application audit and payment-session persistence.
- **Impact:** Raw provider/customer transaction payloads, raw HMAC signatures, and plaintext payment keys have a broader durable exposure surface than the Stripe reference contract.
- **Evidence:** `PaymobPaymentService.cs:2252-2270`, `2309-2327`; `PaymentIntentRoutingMetadata.cs:24-48`; contrast `StripePaymentService.cs:2231-2232`, `2292-2293` and encrypted Stripe client-secret storage.
- **Recommended closure phase:** `PAYMOB-SECURITY-HOTFIX-2 Durable Audit and Payment Artifact Protection`.
- **Certification effect:** Blocks Security and Software certification.

### PAYMOB-FINAL-007 — Operational metrics are registered but inert

- **Classification:** Medium
- **Root cause:** The meter/counters were implemented and exported without instrumenting production Paymob flows.
- **Responsible layer:** Paymob operational instrumentation.
- **Impact:** Dashboards and alerts cannot observe the documented create/provider/webhook/replay/duplicate/Runtime/refund/void/capture/recovery/retry signals.
- **Evidence:** only `Program.cs:2059`, `2099` and `PaymobOperationalTelemetry.cs` reference the type; no production `Record*` call exists.
- **Recommended closure phase:** `PAYMOB-PRODUCTION-1B Telemetry Activation`.
- **Certification effect:** Blocks Operations certification.

### PAYMOB-FINAL-008 — Retry configuration does not govern the certified adapter client

- **Classification:** Medium
- **Root cause:** `RetryCount` is validated/reported, while `PaymobOfficialApi` receives timeout configuration only. The separately retried `Paymob` named client is not used by the adapter.
- **Responsible layer:** Foundation HTTP dependency governance.
- **Impact:** Health/runbook claims do not match actual retry execution; timeout/recovery behavior is not governed by the advertised option.
- **Evidence:** `CommerceHubPaymobOptions.cs:19`, `108-111`; `PaymobFoundationServiceCollectionExtensions.cs:24-30`; `PaymobPaymentAdapter.cs:131`; `Program.cs:1059`.
- **Recommended closure phase:** `PAYMOB-PRODUCTION-1B Telemetry and HTTP Governance`.
- **Certification effect:** Blocks unqualified Operations certification.

### PAYMOB-FINAL-009 — Paymob provider source depends on Stripe provider-owned types and registration

- **Classification:** Medium
- **Root cause:** Canonical provider error and bridge trace/value types were left in the Stripe namespace and reused directly by Paymob.
- **Responsible layer:** Cross-provider application contracts/DI composition.
- **Impact:** Paymob cannot be independently composed or used as the clean template for another provider; removing Stripe breaks Paymob compilation/DI assumptions.
- **Evidence:** Stripe namespace imports in five Paymob implementation files; `StripeFoundationServiceCollectionExtensions.cs:43` registers `PaymentRuntimeOrchestrator`.
- **Recommended closure phase:** `PAYMENT-PROVIDER-CONTRACT-CLOSURE-1 Neutral Application Contracts` with Runtime unchanged.
- **Certification effect:** Blocks Paymob template and independent provider architecture certification.

### PAYMOB-FINAL-010 — Latest official Paymob checkout/credential migration is incomplete

- **Classification:** Medium
- **Root cause:** Implementation phases certified the existing legacy Accept/IFrame protocol without closing the already-documented Intention/Unified Checkout migration plan.
- **Responsible layer:** Paymob provider protocol/configuration.
- **Impact:** New-merchant onboarding, mode-specific Secret/Public key governance, regional base URLs, Unified Checkout, and Pixel are not template-ready.
- **Evidence:** `PAYMENT_0_5B_PAYMOB_DISCOVERY.md`; current legacy endpoint constants in `PaymobPaymentAdapter.cs` and CommerceHub services.
- **Recommended closure phase:** `PAYMOB-MIGRATION-1 Intention and Unified Checkout Decision/Adapter`.
- **Certification effect:** Blocks Migration and Paymob template certification; does not require a Runtime change.

### PAYMOB-FINAL-011 — Focused regression suite is not green

- **Classification:** Medium
- **Root cause:** Permanent certification artifacts and the `PaymobPaymentAuthorityCertificationTests` expectation are inconsistent.
- **Responsible layer:** Certification regression/context governance.
- **Impact:** Regression certification cannot be granted and phase documents cannot be treated as self-consistent proof.
- **Evidence:** 189/190 combined Paymob/Runtime tests; failing `CertificationArtifacts_MustRecordSfPay5Truth`.
- **Recommended closure phase:** same implementation closure followed by `PAYMOB-FINAL-RECERTIFICATION`; update permanent context only if the closure discovers a genuinely new permanent truth.
- **Certification effect:** Blocks Regression certification.

### PAYMOB-FINAL-012 — EF model has changes after the last migration

- **Classification:** Low
- **Root cause:** Repository-wide current model and snapshot are not fully converged.
- **Responsible layer:** Repository migration governance; Paymob ownership not proven.
- **Impact:** Whole-model migration readiness cannot be stated without qualification.
- **Evidence:** `dotnet ef migrations has-pending-model-changes` failed; Paymob replay migration discovery and SQL generation separately passed.
- **Recommended closure phase:** repository migration reconciliation, then target-database verification.
- **Certification effect:** Does not invalidate the Paymob replay migration artifact; blocks unqualified whole-model deployment readiness.

### PAYMOB-FINAL-013 — Advanced operations are translation-only

- **Classification:** Dormant
- **Root cause:** No POS/Storefront or CommerceHub command entry point consumes `IPaymobAdvancedOperationsAdapter`.
- **Responsible layer:** Future application command integration.
- **Impact:** Refund/partial refund/void/capture/inquiry adapter mappings must not be represented as active production operations.
- **Evidence:** production references are limited to interface, implementation, and DI registration.
- **Recommended closure phase:** only when product command entry points are authorized; require Runtime/outcome/single-writer/replay review.
- **Certification effect:** No active competing writer; live/active advanced operations not certified.

### PAYMOB-FINAL-014 — Live operational evidence is absent

- **Classification:** External
- **Root cause:** No official credentials or deployed public Paymob environment was supplied to this workspace.
- **Responsible layer:** Deployment/operations and Paymob merchant account.
- **Impact:** No live Sandbox or production assertion is possible.
- **Recommended closure phase:** `PAYMOB-PRODUCTION-2B Live Sandbox Evidence`, after all source blockers close.
- **Certification effect:** Independently blocks Live Operational Certification.

### PAYMOB-FINAL-015 — Suspected POS/Storefront direct HTTP and structured secret logging

- **Classification:** False Positive
- **Explanation:** Current `PaymobPaymentService` delegates create execution through `IPaymobRuntimeApplicationService`; no direct Paymob HTTP remains in that service. The identified `PaymentKey`/`IframeUrl` structured log was removed by `PAYMOB_SECURITY_HOTFIX_1`. No API key, HMAC secret, access token, or Authorization value was found in Paymob structured log templates.
- **Boundary:** This does not clear CommerceHub direct HTTP or Paymob durable raw audit/plaintext artifact findings.

### PAYMOB-FINAL-016 — Frozen Runtime and Stripe reference evidence remain green

- **Classification:** Informational
- **Explanation:** Runtime source scan found no provider/persistence coupling; 94/94 Runtime-only tests passed. Stripe/Runtime reference filter passed 206/206. No Paymob finding requires or authorizes a Runtime change.

## Risk Classification Summary

| Classification | Count | Certification posture |
|---|---:|---|
| Critical | 1 | Open; certification prohibited |
| High | 5 | Open; certification prohibited |
| Medium | 5 | Open; requires closure or explicit scoped disposition |
| Low | 1 | Open; qualified migration/deployment risk |
| Informational | 1 | Positive evidence |
| External | 1 | Live evidence unavailable |
| Dormant | 1 | Not an active production capability |
| False Positive | 1 | Closed by direct inspection |

## Certification Matrix

| Certification area | Decision | Basis |
|---|---|---|
| Architecture | **Withheld** | Runtime-outcome bypass, CommerceHub duplicate architecture, Stripe-owned Paymob contracts |
| Implementation | **Withheld** | Critical webhook gate and provider-session checkpoint defects |
| Security | **Withheld** | Deleted credential authority, global credential execution, raw audit/plaintext artifact storage |
| Operations | **Withheld** | Inert metrics, retry/governance mismatch, no live operational proof |
| Migration | **Withheld overall** | Replay migration passes; pending model drift and Paymob protocol migration remain |
| Regression | **Withheld** | 189/190 combined focused tests |
| Reference Provider | **Certified** | Stripe final record plus 206/206 current Stripe/Runtime tests |
| Provider-neutral Runtime | **Certified/preserved** | No provider/SQL/runtime coupling found; 94/94 Runtime tests |
| Paymob architecture freeze | **Not authorized** | Open Critical/High source defects |
| Payment Runtime freeze | **Retained** | No change required or permitted |
| Source/Software | **Withheld** | Open Critical/High findings |
| Deployment | **Not ready** | Source defects plus target-environment prerequisites |
| Live operation | **Not certified** | No official live/Sandbox evidence |
| CommerceHub reuse | **Not ready** | CommerceHub remains a direct, separate Paymob implementation |
| Paymob template | **Not ready** | Credential, contract, protocol, telemetry, and convergence gaps |

## Reference Provider Decision

**Stripe remains Reference Provider Certified** for the bounded surface defined by `STRIPE_FINAL_RECERTIFICATION.md`.

Current independent evidence is 206 passed, 0 failed for the Stripe-or-Payment-Runtime filter. The reusable reference behavior is still:

```text
trusted tenant/application authority
  -> capability-specific credential resolution
  -> provider adapter
  -> canonical action/evidence
  -> frozen Payment Runtime
  -> exact Runtime outcome enforcement
  -> one SQL lifecycle writer
  -> durable replay/audit/recovery
```

Paymob does not yet satisfy that template. This decision does not revoke Stripe because the Paymob defects are downstream deviations from the reference, not defects in the frozen Runtime or certified Stripe bounded context.

## Architecture Freeze Decision

```text
PAYMENT RUNTIME: FROZEN — RETAINED
PROVIDER-NEUTRAL RUNTIME: PRESERVED
STRIPE REFERENCE ARCHITECTURE: RETAINED
PAYMOB ARCHITECTURE: NOT FROZEN
PAYMOB SOFTWARE: NOT CERTIFIED
```

No Runtime, bridge, adapter, controller, persistence, frontend, contract, or business-rule change is authorized by this report. Closure work must occur in separately reviewed Paymob application/security/CommerceHub/operations phases. Any proposal to modify the Runtime must be rejected unless a separate Runtime architecture review proves that a provider-neutral invariant itself is wrong; no such evidence was found here.

No new permanent cross-provider architecture truth was discovered. The findings are violations or incomplete applications of existing truths: provider evidence is subordinate to Runtime and SQL authority, provider execution must be isolated, tenant credentials must be server-authoritative, external acceptance gaps require durable recovery, and live evidence cannot be inferred from source. `.ai-context` is therefore not modified.

## Success Answers

| Question | Answer |
|---|---|
| Architecture certified? | **No.** |
| Implementation certified? | **No.** |
| Security certified? | **No.** |
| Operations certified? | **No.** |
| Migration certified? | **No overall.** Paymob replay migration artifact only: yes. |
| Regression certified? | **No.** 189/190 combined focused tests passed. |
| Reference Provider certified? | **Yes. Stripe remains certified.** |
| Provider-neutral Runtime preserved? | **Yes.** |
| Architecture frozen? | **Payment Runtime: yes. Paymob architecture: no.** |
| Software certified? | **No.** |
| Operational deployment pending only? | **No. Source closure is required first.** |
| Ready for CommerceHub reuse? | **No.** |
| Ready to become the Paymob template? | **No.** |
| Ready to begin the next provider? | **Not from Paymob and not as Paymob closure. Stripe remains the only certified template.** |
| Recommend certification closure? | **No. Close the Critical/High findings, rerun regression and migration checks, then perform live Sandbox evidence collection.** |

**FINAL DECISION: PAYMOB CERTIFICATION WITHHELD. THE PAYMENT RUNTIME REMAINS FROZEN AND PROVIDER-NEUTRAL. STRIPE REMAINS THE CERTIFIED REFERENCE PROVIDER. PAYMOB REQUIRES SOURCE IMPLEMENTATION, SECURITY, COMMERCEHUB CONVERGENCE, OPERATIONAL, REGRESSION, AND LIVE-EVIDENCE CLOSURE BEFORE RECERTIFICATION.**

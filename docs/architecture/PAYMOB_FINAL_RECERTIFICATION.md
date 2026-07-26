# PAYMOB-FINAL Re-Certification

Date: 2026-07-14  
Audit posture: independent re-certification of the current working tree after `PAYMOB_CERTIFICATION_CLOSURE_1` through `PAYMOB_CERTIFICATION_CLOSURE_4`  
Reference provider: Stripe  
Payment Runtime posture: permanently frozen  
Controlling decision: **PAYMOB SOFTWARE CERTIFICATION REMAINS WITHHELD**

## Executive Summary

The four Certification Closures are real and effective for their stated scopes. Direct source and regression verification closes `PAYMOB-FINAL-001`, `PAYMOB-FINAL-002`, `PAYMOB-FINAL-003`, and `PAYMOB-FINAL-004`:

- required Paymob Runtime rejection/failure now stops before the webhook SQL transaction and lifecycle writer;
- provider-order registration, accepted order identity, and accepted payment-key material are durably checkpointed and recovered without reopening provider-order creation;
- Storefront/POS create, retry, recovery, webhook, and callback admission now require a current enabled, non-deleted, environment-compatible tenant Paymob configuration and the relevant operation capability;
- the payment adapter consumes the exact validated tenant credential set through a scoped, one-shot handoff and no longer reads global credential options.

Those closures eliminate the original one Critical and three of the original five High findings. They do not eliminate the other two High findings or the original Medium and Low findings. The current open software-risk count is therefore:

| Severity | Original | Closed | Still open |
|---|---:|---:|---:|
| Critical | 1 | 1 | 0 |
| High | 5 | 3 | 2 |
| Medium | 5 | 0 | 5 |
| Low | 1 | 0 | 1 |

Whole-provider certification remains blocked because active CommerceHub Paymob creation, webhook, recovery, refund, and inquiry still use a separate direct-HTTP/global-options implementation outside the certified adapter/Runtime path. Security certification also remains blocked because Storefront/POS persists raw webhook payloads, raw HMAC signatures, and plaintext payment keys in durable SQL fields. These are not deployment-only concerns.

Operations certification remains withheld because the Paymob meter is registered but production code never records its counters, and the validated/reported `RetryCount` does not govern the named `PaymobOfficialApi` client used by the adapters. Paymob also still depends on Stripe-owned application contract types and on `AddStripeFoundation` for `PaymentRuntimeOrchestrator` registration.

The Paymob replay migration remains source-valid and discoverable. EF generates the expected table and unique replay index, but the repository model still has pending changes after the last migration. The implemented provider protocol also remains the legacy Egypt Accept order/payment-key/IFrame flow while current official Paymob documentation directs API integrations through the Intention API and Unified Checkout or Pixel.

Regression is not fully certified. The current combined Paymob/Payment Runtime slice produced **218 passes and 1 failure**. The only failure is the pre-existing `CertificationArtifacts_MustRecordSfPay5Truth` assertion. Excluding that four-test artifact class, the applicable Paymob/Runtime regression is **215/215**. Stripe/Payment Runtime remains green at **206/206**, the API build passes with zero warnings and errors, and the Storefront production build passes.

Live Operational Certification is separately withheld. No official Sandbox credentials, enabled Sandbox tenant, public callback/webhook reachability, successful or declined payment, refund/capture/void execution, replay/duplicate attack drill, or measured operational latency is evidenced in this workspace.

Final posture:

```text
PAYMENT RUNTIME: CERTIFIED AND FROZEN
STRIPE REFERENCE PROVIDER: CERTIFIED
PAYMOB CLOSURES 1-4: VERIFIED CLOSED
PAYMOB ARCHITECTURE: NOT CERTIFIED OR FROZEN
PAYMOB SOFTWARE: NOT CERTIFIED
PAYMOB OPERATIONS: NOT CERTIFIED
PAYMOB DEPLOYMENT: NOT READY
PAYMOB LIVE OPERATION: NOT CERTIFIED
```

## Audit Basis

The audit reviewed all seven files under `.ai-context`, all Paymob architecture records under `docs/architecture`, the required `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md` and `STRIPE_FINAL_RECERTIFICATION.md`, current Paymob and CommerceHub source, controllers, DI, health, telemetry, persistence metadata, EF migration/source snapshot, tests, and current official Paymob API direction.

The mandatory Paymob record included:

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
- `PAYMOB_FINAL.md`
- `PAYMOB_CERTIFICATION_CLOSURE_1.md`
- `PAYMOB_CERTIFICATION_CLOSURE_2.md`
- `PAYMOB_CERTIFICATION_CLOSURE_3.md`
- `PAYMOB_CERTIFICATION_CLOSURE_4.md`
- `PAYMENT_0_5B_PAYMOB_DISCOVERY.md`
- `paymob-payment-authority-certification-findings.md`

Earlier certifications and closure reports were treated as claims requiring current source verification, not as self-proving authority.

## Independent Validation

Executed on 2026-07-14 against the current working tree:

| Validation | Result |
|---|---:|
| `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore` | Passed; 0 warnings, 0 errors |
| Paymob or Payment Runtime aggregate | **218 passed, 1 failed** |
| Applicable Paymob/Runtime slice excluding `PaymobPaymentAuthorityCertificationTests` | **215 passed, 0 failed** |
| Stripe or Payment Runtime reference slice | **206 passed, 0 failed** |
| `npm.cmd run build` in `PointOfSale.Storefront` | Passed; non-blocking Vite/bundle warnings |
| EF migration discovery with `--no-connect` | `20260713120000_PaymobContractClosure2WebhookReplay` listed |
| EF idempotent SQL generation | Paymob replay table, indexes, unique replay fence, and history row generated |
| EF pending-model check | Failed: model changes exist after the last migration |

The single aggregate test failure is:

```text
PaymobPaymentAuthorityCertificationTests.CertificationArtifacts_MustRecordSfPay5Truth
Expected: PASS SF-PAY5 in each permanent .ai-context file
Observed: marker absent
```

No `.ai-context` file is modified by this report because no new permanent cross-provider architecture truth was discovered.

## Original Findings Matrix

| ID | Original class | Current status | Current class | Evidence-based decision |
|---|---|---|---|---|
| PAYMOB-FINAL-001 | Critical | **Closed** | Informational closure | `PaymobRuntimeOutcomeGuard.EnsureRequiredOutcome` is called before `BeginTransactionAsync` and before `ProcessPaymobTransactionAsync` in `PaymobPaymentService.cs:855-872,974`. Closure tests are included in the green applicable slice. |
| PAYMOB-FINAL-002 | High | **Closed** | Informational closure | `PaymobPaymentAdapter.cs:91-134` writes registration-started, accepted-order, and accepted-key checkpoints in order. `PaymobProviderCheckpointStore.cs:70-75,92-197` blocks ambiguous recreation and persists reusable identities before subsequent provider/Runtime work. |
| PAYMOB-FINAL-003 | High | **Closed** | Informational closure | `PaymobTenantConfigurationResolver.cs:63-98` requires capability admission and an enabled, non-deleted tenant row. Lines 101-155 enforce provider, environment, and operation gates. Webhook and callback use this resolver at `PaymobPaymentService.cs:722-724,1504-1506`. |
| PAYMOB-FINAL-004 | High | **Closed** | Informational closure | `PaymobRuntimeApplicationService.cs:42-62` resolves tenant credentials, starts the one-shot handoff, and then invokes the bridge. `PaymobPaymentAdapter.cs:59-60` consumes it. Payment and advanced adapters contain no `PaymobOptions`, `IOptions`, `IConfiguration`, tenant persistence, or credential resolver lookup. |
| PAYMOB-FINAL-005 | High | **Still Open** | High | Active CommerceHub paths still inject `IHttpClientFactory` and global `CommerceHubPaymobOptions`, call Paymob auth/order/payment-key/refund/inquiry endpoints directly, and process a separate webhook/lifecycle flow. Evidence includes `CommerceHubPaymentService.cs:77-104,321,620-874,2490-2559,2816-2854,4503-4698` and `CommerceHubRefundGatewayService.cs:42-56,202-270,373-465,608-628`. |
| PAYMOB-FINAL-006 | High | **Still Open** | High | `PaymentIntentRoutingMetadata.cs:11,50-65` serializes `PaymentKey` directly into plaintext provider metadata. `PaymobPaymentService.cs:2091-2110,2148-2167` stores raw payload and raw signature values. The log-template hotfix does not protect durable SQL artifacts. |
| PAYMOB-FINAL-007 | Medium | **Still Open** | Medium | `PaymobOperationalTelemetry` declares `Record*` methods at lines 59-95 and is registered/exported by `Program.cs:2060,2100`; repository-wide production search finds no Paymob `Record*` caller. The metrics remain inert. |
| PAYMOB-FINAL-008 | Medium | **Still Open** | Medium | `RetryCount` remains validated and reported, but `PaymobFoundationServiceCollectionExtensions.cs:24-29` configures only base URI and timeout for `PaymobOfficialApi`. The adapter uses that client at `PaymobPaymentAdapter.cs:239-241`. The separately governed `Paymob` client is not the adapter client. |
| PAYMOB-FINAL-009 | Medium | **Still Open** | Medium | Five Paymob implementation files still import `PointOfSale.API.Services.Payments.Stripe`; `PaymobRuntimeBridge.cs:273` constructs a Stripe-owned execution type. `PaymentRuntimeOrchestrator` is still registered by `StripeFoundationServiceCollectionExtensions.cs:43`, while `Program.cs:187-189` must add Stripe foundation after Paymob. |
| PAYMOB-FINAL-010 | Medium | **Still Open** | Medium | Current adapter endpoints remain `auth/tokens`, `ecommerce/orders`, `acceptance/payment_keys`, and IFrame handoff (`PaymobPaymentAdapter.cs:249-273,893`). Current official Paymob API guidance begins with Intention Creation and offers Unified Checkout/Pixel. No migration or explicit supported-legacy freeze has closed this template gap. |
| PAYMOB-FINAL-011 | Medium | **Still Open** | Medium | The complete current Paymob/Runtime filter is not green: 218/219. `PaymobPaymentAuthorityCertificationTests.cs:161-179` requires `PASS SF-PAY5` in five permanent context files, contrary to the current absence of that marker. The applicable implementation slice is green but Regression Certification is not. |
| PAYMOB-FINAL-012 | Low | **Still Open** | Low | EF discovers and scripts the Paymob replay migration, but `dotnet ef migrations has-pending-model-changes` still reports model changes after the last migration. Paymob ownership of the broader drift remains unproven. |
| PAYMOB-FINAL-013 | Dormant | **Still Open** | Dormant | `IPaymobAdvancedOperationsAdapter` has no production caller beyond implementation and DI registration (`PaymobProviderAdapterServiceCollectionExtensions.cs:16`). Translation exists; active refund/partial-refund/void/capture/inquiry command certification does not. |
| PAYMOB-FINAL-014 | External | **Still Open** | External | No real credentials, tenant, public ingress, provider-delivered events, advanced-operation drill, attack drill, or latency evidence is available. This independently blocks Live Operational Certification. |
| PAYMOB-FINAL-015 | False Positive | **False Positive** | False Positive | POS/Storefront create execution still delegates through `IPaymobRuntimeApplicationService`; controllers and Checkout do not call adapters or provider HTTP. The structured `PaymentKey`/`IframeUrl` log placeholders remain removed. This does not clear CommerceHub direct HTTP or durable artifact storage. |
| PAYMOB-FINAL-016 | Informational | **Closed** | Informational | Runtime scans remain free of provider, HTTP, EF, SQL, credential, and frontend dependencies. Payment Runtime tests remain included in the green reference slice; Stripe/Runtime passes 206/206. |

No original finding is classified as Superseded, Partially Closed, or Out of Scope. The four implemented closures close their exact findings; they do not narrow the whole-provider scope established by `PAYMOB_FINAL.md`.

## Closure Verification

### Closure 1 - Runtime Outcome Enforcement

Verified flow:

```text
EvaluateAndExecuteAsync
  -> replay / duplicate handling
  -> invalid / non-terminal / ignored handling
  -> PaymobRuntimeOutcomeGuard.EnsureRequiredOutcome
  -> begin serializable SQL transaction
  -> webhook log preparation
  -> ProcessPaymobTransactionAsync
  -> PaymentLifecycleService
```

The guard predicate is narrow and correct: application admission required Runtime, but the adapter/Runtime did not produce an admitted result. It throws `PAYMOB_RUNTIME_OUTCOME_REJECTED`. Because it executes before the SQL transaction is opened, the original Runtime bypass is eliminated for the audited Storefront/POS webhook path.

Decision: **PAYMOB-FINAL-001 Closed.**

### Closure 2 - Durable Provider Checkpoint

Verified flow:

```text
load durable PaymentIntent checkpoint
  -> accepted key exists: rebuild evidence, no provider call
  -> accepted order exists: authenticate and create key for that order
  -> registration-started without accepted order: fail closed for reconciliation
  -> otherwise persist registration-started
       -> register provider order
       -> persist accepted order
       -> create payment key
       -> persist key and iframe
       -> enter Runtime
```

The adapter calls `BeginOrderRegistrationAsync` before order registration, `PersistAcceptedOrderAsync` before payment-key creation, and `PersistPaymentKeyAsync` before evidence enters Runtime. An indeterminate order-registration state cannot reopen order registration automatically.

Decision: **PAYMOB-FINAL-002 Closed.**

### Closure 3 - Tenant Credential Authority

The shared resolver performs a fresh no-tracking SQL read using `IgnoreQueryFilters()` paired with explicit tenant, provider code, enabled, non-deleted, and ciphertext predicates. It rejects provider disablement, disabled credential/environment validation, unsupported operation capability, Sandbox/Production mismatch, invalid tenant configuration, and missing credential fields. Storefront/POS create, webhook, and callback paths use it.

This closure intentionally does not govern the separate CommerceHub direct implementation.

Decision: **PAYMOB-FINAL-003 Closed for its stated Storefront/POS scope.**

### Closure 4 - Tenant-Scoped Adapter Credential Handoff

The application resolver result is carried through a scoped handoff. The handoff rejects overlap, requires tenant identity in canonical action metadata, consumes once, clears before provider I/O, and cannot fall back to global configuration. Both payment and dormant advanced-operation adapters require it.

This closure intentionally does not converge CommerceHub and does not activate advanced operations.

Decision: **PAYMOB-FINAL-004 Closed for its stated adapter/application scope.**

## Current Findings Matrix

| ID | Class | Affected certification | Current evidence |
|---|---|---|---|
| PAYMOB-FINAL-005 | High | Architecture, Implementation, Security, Reference Provider, Software | Active duplicate CommerceHub direct provider stack and global credential authority |
| PAYMOB-FINAL-006 | High | Security, Software, Deployment | Raw payload/signature and plaintext payment-key persistence |
| PAYMOB-FINAL-007 | Medium | Operations | Exported Paymob metrics have no production recording calls |
| PAYMOB-FINAL-008 | Medium | Operations | Advertised retry count does not govern the adapter client |
| PAYMOB-FINAL-009 | Medium | Architecture, Reference Provider | Paymob depends on Stripe-owned types and Stripe DI registration |
| PAYMOB-FINAL-010 | Medium | Migration, Reference Provider | Legacy Egypt Accept/IFrame protocol remains the only implemented provider protocol |
| PAYMOB-FINAL-011 | Medium | Regression | Full Paymob/Runtime aggregate remains 218/219 |
| PAYMOB-FINAL-012 | Low | Migration, Deployment | Repository model has pending changes after last migration |
| PAYMOB-FINAL-013 | Dormant | Active-operation scope only | Advanced adapter remains translation-only and uncalled |
| PAYMOB-FINAL-014 | External | Live Operational Certification | No live/Sandbox evidence set |

## Architecture Review

### Preserved

- `PointOfSale.Core/Payments/Runtime` contains no Paymob, Stripe, provider transport, credential, EF Core, SQL, controller, or frontend logic.
- `PaymentRuntimeOrchestrator` remains the only Runtime stage sequencer.
- Paymob Runtime Bridge and webhook evidence adapter depend on canonical Runtime objects and do not persist SQL or mutate lifecycle state.
- Storefront/POS controllers delegate to `IPaymobPaymentService`; no controller-to-adapter or controller-to-provider-HTTP path was found.
- Storefront/POS create, retry, rehydration, and background recovery converge through `PaymobRuntimeApplicationService` and `PaymobRuntimeBridge`.
- `PaymentLifecycleService` remains the application lifecycle writer after the required Runtime outcome.

### Not preserved across the complete provider

CommerceHub remains a second active Paymob architecture:

```text
CommerceHubPaymentService / CommerceHubRefundGatewayService
  -> global CommerceHubPaymobOptions
  -> unnamed HttpClient
  -> direct Paymob auth/order/payment-key/refund/inquiry
  -> CommerceHub-local webhook and lifecycle interpretation
```

This is a runtime bypass at the whole-provider architecture boundary even though the Storefront/POS path is converged. It duplicates provider creation, recovery, HMAC/webhook, refund, inquiry, credential, and error semantics outside the certified adapter/Runtime pipeline.

Paymob is also not independently composable. Canonical provider error and execution contract types remain in the Stripe provider namespace, and Paymob relies on Stripe foundation registration for `PaymentRuntimeOrchestrator`.

Architecture decision: **Withheld.**  
Provider-neutral Runtime decision: **Preserved and frozen.**

## Implementation Review

### Certified closure behavior

- Required Runtime outcome enforcement precedes lifecycle mutation.
- Accepted provider identities and payment keys are durably checkpointed.
- Storefront/POS retries and recovery reuse the same local/provider authority.
- Current tenant configuration and exact tenant credentials govern Storefront/POS adapter calls.
- Adapter credential lookup/fallback is absent.
- No SQL or application business lifecycle logic was found inside payment, webhook, or advanced-operation adapters.
- No provider-specific logic or SQL was found inside Runtime.

### Remaining implementation blockers

- CommerceHub directly implements the same Paymob provider protocol and operations.
- Paymob payment keys remain plaintext routing metadata.
- Advanced operations have no active provider-neutral application command, Runtime-outcome, replay, and single-writer path.
- Paymob cannot compile/compose as a clean provider template without Stripe application types and registration.

Implementation decision: **Withheld for the complete Paymob provider.**

## Security Review

### Verified controls

- Storefront/POS tenant configuration is encrypted at rest and selected by trusted tenant identity.
- Disabled, deleted, missing, malformed, capability-disabled, and environment-mismatched tenant configuration fails closed.
- Adapter credentials are scoped, one-shot, tenant-bound, and cleared before provider I/O.
- Webhook and callback HMAC use HMAC-SHA512 with fixed-time comparison.
- Request bodies are bounded and ingress is rate limited.
- Callback redirect is pending-only and cannot write payment success.
- Durable replay and webhook-log uniqueness fences exist.
- Structured create-path logs do not emit payment key, iframe URL, API key, HMAC secret, auth token, or Authorization values.

### Open security blockers

Storefront/POS durable webhook audit storage writes provider payload and HMAC signature verbatim. Paymob callback data can contain customer/provider transaction fields such as source data. Storing the raw signature also retains reusable authentication evidence instead of a digest.

`PaymentIntentRoutingMetadataSerializer` stores `PaymentKey` as plaintext JSON. The checkpoint closure increases the importance of this field by making it durable before Runtime interpretation, but it does not add encryption or token protection. This remains materially weaker than the Stripe reference provider's protected client-secret and redacted/digested webhook evidence.

CommerceHub continues to use global Paymob credentials and direct HTTP paths that bypass the Storefront/POS resolver, capability, environment, and handoff model.

Security decision: **Withheld.**

## Operations Review

### Present source artifacts

- `paymob_foundation` and `paymob_operational_readiness` health checks;
- configuration, environment, tenant, replay-table, webhook-log, and DI probes;
- OpenTelemetry meter registration;
- documented counters, alert matrix, runbooks, drills, rollout, and rollback checklists.

### Open operational gaps

The Paymob metrics are declarations only. Production Paymob services do not inject `PaymobOperationalTelemetry` or call any `Record*` method. Therefore provider failures, webhook receipts, replay/duplicate rejection, Runtime admission/rejection, refunds, voids, captures, recovery, and retry cannot drive the documented dashboards or alerts.

`PaymobOptions.RetryCount` is validated and exposed through health, but the `PaymobOfficialApi` client used by both adapters receives only base address and timeout. The separate `Paymob` client configured by shared external HTTP policy is not used by the certified adapter.

There is no target-environment proof of green health, deployed metrics, alert firing, public ingress, retention, on-call routing, or measured latency.

Operations decision: **Withheld.** Source operational scaffolding exists; operational behavior and live readiness are not certified.

## Migration Review

### Replay migration

Source evidence is valid:

- EF lists `20260713120000_PaymobContractClosure2WebhookReplay`.
- The model snapshot contains `PaymobWebhookReplayRecords` and the three required indexes.
- Generated idempotent SQL contains the replay table.
- Generated SQL contains `IX_PaymobWebhookReplayRecords_Tenant_EvidenceHash`.
- Generated SQL contains unique `IX_PaymobWebhookReplayRecords_Tenant_Provider_Event` filtered on non-deleted records.
- Generated SQL contains `IX_PaymobWebhookReplayRecords_Tenant_ProviderReference` and the migration-history insert.

The replay migration artifact is source-certified. Application and target-database verification remain deployment work.

### Repository model state

EF reports:

```text
Changes have been made to the model since the last migration.
```

This is repository-wide drift and is not proven to be Paymob-owned, but unqualified whole-model deployment readiness cannot be certified.

### Provider protocol

Current official Paymob documentation states that [API payment flows begin with the Intention Creation API](https://developers.paymob.com/paymob-docs/integration-paths/apis) and launch either [Unified Checkout or Pixel](https://developers.paymob.com/paymob-docs/developers/checkout-experiences). The current implementation remains the legacy Egypt Accept auth/order/payment-key/IFrame flow and models neither API Secret/Public keys nor regional endpoint governance. This remains architecture-compatible for a specifically supported existing IFrame merchant, but it is not a certified new-merchant, multi-region, or next-provider template.

Migration decision: **Withheld overall.** Replay migration artifact only: **certified at source level**.

## Regression Review

The closure behavior is regression-green, but the complete certification surface is not:

| Surface | Result | Decision |
|---|---:|---|
| Paymob or Payment Runtime aggregate | 218/219 | Not green |
| Applicable Paymob/Runtime excluding artifact-only class | 215/215 | Closure implementation green |
| Stripe or Payment Runtime | 206/206 | Reference provider green |
| API build | Passed, 0 warnings/errors | Green |
| Storefront production build | Passed | Green with non-blocking warnings |

The remaining failure is a certification-context assertion, not a demonstrated payment-flow regression. It still prevents an unqualified Regression Certification because the governing test suite and permanent-context update rule disagree. This report does not manufacture a permanent architecture marker merely to satisfy the test.

Regression decision: **Withheld.**

## Production Readiness

The certification layers remain separate:

| Layer | Decision | Reason |
|---|---|---|
| Software Certification | **Withheld** | Two High source findings plus architecture/security/operations gaps remain |
| Operational Readiness | **Withheld** | Inert metrics, retry-governance mismatch, no deployed validation |
| Deployment Readiness | **Not ready** | Source blockers, pending model drift, target migrations/secrets/endpoints unverified |
| Live Operational Certification | **Not certified** | Required provider/deployment evidence is absent |

### Live validation matrix

| Required evidence | Evidence available? | Decision |
|---|---:|---|
| Sandbox credentials | No | External dependency pending |
| Sandbox tenant | No | Deployment dependency pending |
| Public callback | No | Not certified |
| Public webhook | No | Not certified |
| Successful payment | No | Not certified |
| Declined payment | No | Not certified |
| Refund | No live evidence | Not certified |
| Capture | No live evidence; adapter dormant | Not certified |
| Void | No live evidence; adapter dormant | Not certified |
| Replay attack | No live/concurrent drill evidence | Not certified |
| Duplicate attack | No live/concurrent drill evidence | Not certified |
| Operational latency | No measured create/webhook/callback/recovery latency | Not certified |

Source compilation, source HMAC/replay controls, migration SQL, unit tests, and documented performance targets are not substitutes for live provider evidence.

## Outstanding Findings

### PAYMOB-FINAL-005 - Complete-provider convergence

- Classification: **High**
- Status: **Still Open**
- Impact: duplicate active provider creation, webhook, recovery, refund, inquiry, credential, and lifecycle behavior prevents whole-provider authority and reference-template certification.
- Closure required: a separately reviewed CommerceHub convergence phase; no Runtime change is required.

### PAYMOB-FINAL-006 - Durable sensitive artifact protection

- Classification: **High**
- Status: **Still Open**
- Impact: raw provider/customer payloads, raw HMAC signatures, and plaintext payment keys have broader durable exposure than the reference provider.
- Closure required: durable audit redaction/digest and payment-artifact protection with compatibility/recovery proof.

### PAYMOB-FINAL-007 / 008 - Telemetry and HTTP governance

- Classification: **Medium**
- Status: **Still Open**
- Impact: documented operational signals and retry behavior do not describe the executing adapter.
- Closure required: production-path instrumentation and explicit adapter HTTP retry/idempotency governance.

### PAYMOB-FINAL-009 - Neutral provider application contracts

- Classification: **Medium**
- Status: **Still Open**
- Impact: Paymob cannot serve as an independently composable reference provider while it imports Stripe-owned contract types and registration.
- Closure required: neutral application contracts/composition ownership without modifying Runtime semantics.

### PAYMOB-FINAL-010 - Paymob protocol migration decision

- Classification: **Medium**
- Status: **Still Open**
- Impact: legacy IFrame implementation is not a certified general Paymob template for new merchants or regions.
- Closure required: explicit supported-legacy freeze with merchant evidence, or separately authorized Intention/Unified Checkout migration.

### PAYMOB-FINAL-011 / 012 - Regression and model convergence

- Classification: **Medium / Low**
- Status: **Still Open**
- Impact: full regression remains red and whole-model deployment state is not converged.
- Closure required: reconcile the stale certification assertion without inventing permanent truth; reconcile repository migration drift and verify target database state.

### PAYMOB-FINAL-013 / 014 - Dormant operations and live proof

- Classification: **Dormant / External**
- Status: **Still Open**
- Impact: advanced operations are not active certified commands, and Live Operational Certification has no evidence base.
- Closure required: only activate advanced operations through a separately certified application/Runtime/single-writer path; collect live Sandbox evidence after software blockers close.

## Risk Classification

| Classification | Open | Closed / retained | Certification effect |
|---|---:|---:|---|
| Critical | 0 | 1 | Original Critical is closed |
| High | 2 | 3 | Blocks Architecture, Implementation, Security, Reference Provider, and Software certification |
| Medium | 5 | 0 | Blocks unqualified Operations, Migration, Regression, and template certification |
| Low | 1 | 0 | Blocks unqualified whole-model deployment readiness |
| Informational | 0 | 1 | Runtime/reference evidence retained |
| External | 1 | 0 | Independently blocks Live Operational Certification |
| Dormant | 1 | 0 | Not an active production capability |
| False Positive | 0 | 1 | No certification effect within its corrected boundary |
| Out of Scope | 0 | 0 | None; whole-provider scope includes CommerceHub |

No new Critical or High finding was discovered. The remaining High findings are the same unclosed `PAYMOB_FINAL` findings, not rediscoveries of closed issues.

## Reference Provider Decision

**Stripe remains Reference Provider Certified. Paymob is not Reference Provider Certified.**

Current Stripe/Payment Runtime regression is 206/206. The reusable reference shape remains:

```text
trusted tenant/application authority
  -> capability-specific credential resolution
  -> provider adapter
  -> canonical action/evidence
  -> frozen Payment Runtime
  -> exact required Runtime outcome enforcement
  -> one SQL lifecycle writer
  -> durable replay, recovery, minimized audit evidence, and operations
```

Storefront/POS Paymob now satisfies more of that shape after Closures 1-4. The complete provider does not because CommerceHub remains outside it, sensitive durable artifacts remain weaker than the reference, telemetry/retry governance is incomplete, and Paymob contracts still depend on Stripe-owned types.

## Architecture Freeze Decision

```text
PAYMENT RUNTIME: FROZEN - RETAINED
PROVIDER-NEUTRAL RUNTIME: PRESERVED
STRIPE REFERENCE ARCHITECTURE: FROZEN - RETAINED
PAYMOB ARCHITECTURE: NOT FROZEN
PAYMOB SOFTWARE: NOT CERTIFIED
```

No Runtime change is required or authorized. The next closures belong to Paymob application, CommerceHub convergence, security persistence, operations, neutral provider contracts, regression, and migration governance.

## Success Answers

| Question | Answer |
|---|---|
| Architecture certified? | **No.** Complete-provider convergence and neutral contract ownership remain open. |
| Implementation certified? | **No.** Closures 1-4 are verified, but active CommerceHub direct execution remains. |
| Security certified? | **No.** Durable raw webhook/signature and plaintext payment-key storage remain open. |
| Operations certified? | **No.** Metrics are inert and adapter retry governance does not match configuration. |
| Migration certified? | **No overall.** Paymob replay migration artifact only: yes. Protocol and whole-model readiness: no. |
| Regression certified? | **No.** Full aggregate is 218/219; applicable implementation slice is 215/215. |
| Reference Provider certified? | **Stripe: yes. Paymob: no.** |
| Provider-neutral Runtime preserved? | **Yes.** |
| Architecture frozen? | **Payment Runtime and Stripe: yes. Paymob: no.** |
| Software certified? | **No.** |
| Operational deployment pending only? | **No.** Source architecture, security, and operations closure is still required. |
| Ready to begin the next provider? | **No, not using Paymob as the reference or as evidence of provider completion. Stripe remains the only certified template.** |
| Recommend another closure? | **Yes.** Close the two remaining High findings first, then the operational/contract/migration/regression findings, and rerun final re-certification before live Sandbox validation. |

## Final Decision

**PAYMOB CERTIFICATION REMAINS WITHHELD. CERTIFICATION CLOSURES 1-4 ARE VERIFIED AND HAVE ELIMINATED THE ORIGINAL CRITICAL FINDING AND THREE HIGH FINDINGS. TWO HIGH, FIVE MEDIUM, AND ONE LOW SOFTWARE/DEPLOYMENT FINDINGS REMAIN. STRIPE REMAINS THE CERTIFIED REFERENCE PROVIDER. THE PAYMENT RUNTIME REMAINS PROVIDER-NEUTRAL AND FROZEN. PAYMOB LIVE OPERATION IS NOT CERTIFIED.**

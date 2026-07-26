# FAWRY-3 Runtime Bridge Certification

Date: 2026-07-14  
Scope: Fawry Runtime Bridge only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and permanently frozen

## Decision

FAWRY-3 implements only the Fawry Runtime Bridge.

The bridge connects the translation-only `IFawryPaymentAdapter` from FAWRY-2 to the frozen `PaymentRuntimeOrchestrator`. It owns orchestration only. It does not own business rules, lifecycle authority, persistence, SQL, credential lookup, provider configuration, callbacks, webhooks, advanced operations, controllers, frontend behavior, or provider protocol changes.

## Execution Diagram

```text
PaymentAction + explicit one-operation FawryPaymentCredentials
  -> IFawryRuntimeBridge.ExecuteAsync
  -> IFawryPaymentAdapter.InitializePaymentAsync
  -> ProviderEvidence or CanonicalProviderError
  -> rejected ProviderEvidence when adapter returns CanonicalProviderError
  -> PaymentRuntimeOrchestrator.Execute
  -> RuntimeResult
  -> FawryExecutionResult
```

The bridge invokes the Runtime orchestrator exactly once for each admitted bridge request. It does not call individual Runtime stages.

## Bridge Responsibility

The bridge owns:

- invoking `IFawryPaymentAdapter` for the supplied canonical `PaymentAction`;
- passing only explicit one-operation credentials supplied by the caller;
- preserving action, session, attempt, correlation, and provider evidence lineage;
- translating adapter failure into rejected canonical `ProviderEvidence`;
- invoking `PaymentRuntimeOrchestrator.Execute` with the supplied prior Runtime history;
- projecting `ProviderEvidence`, `RuntimeResult`, provider error, trace, and metadata into `FawryExecutionResult`.

The bridge does not own:

- tenant lookup, credential lookup, configuration lookup, or fallback credentials;
- provider request construction or response parsing beyond adapter result handling;
- Runtime stage logic, decisions, transitions, transactions, or settlements;
- SQL, EF Core, durable replay, checkpoints, lifecycle writes, or outbox work;
- outcome admission policy or public DTO mapping;
- callback, webhook, refund, capture, void, inquiry, or reconciliation operations.

## Failure Flow

```text
Provider rejection / HTTP failure / transport failure / malformed provider response
  -> IFawryPaymentAdapter
  -> CanonicalProviderError
  -> FawryExecutionFactory.ProviderErrorEvidence
  -> ProviderEvidence.Status = Rejected
  -> PaymentRuntimeOrchestrator.Execute
  -> RuntimeResult failure
  -> FawryExecutionResult.ExecutionStatus = ProviderFailed
```

Provider rejection never bypasses Runtime. The rejected evidence is still submitted to the frozen Runtime, which fails closed under canonical admission rules.

## ProviderEvidence Flow

Successful adapter execution returns canonical `ProviderEvidence` created by the FAWRY-2 adapter. FAWRY-3 preserves that evidence unchanged and supplies it to `PaymentRuntimeOrchestrator.Execute` with the caller-provided context, action, session, and prior Runtime artifacts.

Provider evidence remains candidate evidence only. It is not local lifecycle finality and the bridge does not mutate SQL.

## RuntimeResult Flow

`PaymentRuntimeOrchestrator` is the only Runtime stage sequencer. Its `RuntimeResult` is passed through to `FawryExecutionResult` without reinterpretation or fabricated success.

Projection rules:

| Runtime / adapter condition | Fawry execution status |
|---|---|
| Adapter returned `CanonicalProviderError` | `ProviderFailed` |
| Adapter succeeded and Runtime succeeded | `RuntimeSucceeded` |
| Adapter succeeded and Runtime failed | `RuntimeFailed` |

The application layer remains responsible for exact required Runtime outcome admission before any lifecycle mutation in later phases.

## Architecture Preservation

| Boundary | FAWRY-3 decision |
|---|---|
| Payment Runtime | Unchanged. |
| Runtime stages | Not called directly. |
| Provider Adapter | Preserved as translation/protocol only. |
| Application Services | Not modified. |
| Controllers / frontend | Not modified. |
| Callback / webhook adapters | Not implemented. |
| Advanced operations | Not implemented. |
| Credential lookup / configuration lookup | Not implemented. Credentials are explicit bridge request input. |
| SQL / persistence / lifecycle writer | Not implemented. |
| CommerceHub | Not modified. |

Runtime independence guards verify `PointOfSale.Core/Payments/Runtime` contains no Fawry names, Fawry endpoints, or Fawry bridge references.

## Certification Evidence

Focused tests in `PointOfSale.Tests/Architecture/FawryRuntimeBridgeCertificationTests.cs` cover:

| Evidence | Coverage |
|---|---|
| Execution delegation | `PaymentAction` and credentials pass to `IFawryPaymentAdapter`; adapter evidence enters Runtime. |
| ProviderEvidence flow | Adapter evidence is preserved and submitted to `PaymentRuntimeOrchestrator`. |
| Rejected evidence flow | Adapter error becomes `EvidenceStatus.Rejected` provider evidence and still enters Runtime. |
| CanonicalProviderError flow | Provider error is retained in `FawryExecutionResult.ProviderError` and projected into evidence metadata. |
| RuntimeResult propagation | Runtime success and failure are returned unchanged in the execution result. |
| ExecutionResult mapping | Provider failure, Runtime success, and Runtime failure map to distinct Fawry execution statuses. |
| Architecture isolation | No SQL, lifecycle service, configuration lookup, logging, webhook/callback, advanced operations, Runtime stage calls, Stripe dependency, or Paymob dependency. |
| Runtime independence | Runtime source remains free of Fawry provider-specific terms. |

## Certification Decision

| Question | Decision |
|---|---|
| Runtime Bridge implemented? | Yes. |
| Execution delegation certified? | Yes, source and focused tests. |
| ProviderEvidence flow certified? | Yes. |
| Rejected evidence handling certified? | Yes. |
| CanonicalProviderError flow certified? | Yes. |
| RuntimeResult propagation certified? | Yes. |
| ExecutionResult mapping certified? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Runtime Bridge certified? | Yes for bridge-only orchestration after focused tests pass. |
| Recommend next phase? | Yes. Proceed to FAWRY-4 callback/webhook evidence adapter certification without Runtime changes. |

No `.ai-context` file was modified because no new permanent cross-provider architectural truth was discovered.

## Explicit Exclusions

FAWRY-3 does not certify or implement:

- callback adapter;
- webhook adapter;
- advanced operations;
- application service activation;
- controller or frontend integration;
- persistence, SQL, replay store, checkpoints, outbox, or lifecycle writes;
- credential lookup, configuration lookup, or credential fallback;
- business rules or exact application outcome admission;
- provider protocol changes;
- live Sandbox validation or deployment readiness.

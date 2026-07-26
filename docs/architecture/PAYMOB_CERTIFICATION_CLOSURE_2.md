# PAYMOB-CERTIFICATION-CLOSURE-2 Durable Provider Checkpoint

Date: 2026-07-14  
Scope: `PAYMOB-FINAL-002` only  
Payment Runtime posture: frozen and unchanged  
Decision: **HIGH FINDING CLOSED**

## Finding

`PAYMOB-FINAL-002` found that the Storefront/POS adapter accepted a Paymob provider order and then created the payment key before the application durably retained the accepted provider order ID. A failure, process crash, or node restart between those operations could lose the reusable provider identity and let recovery create another provider order.

This closure does not address any other PAYMOB-FINAL finding.

## Audit Result

The active create-payment paths were traced as follows:

| Entry path | Create execution | Recovery/retry execution | Checkpoint decision |
| --- | --- | --- | --- |
| Storefront create | `PaymobPaymentService` -> Runtime Application -> Runtime Bridge -> adapter | Same path | Hardened by this closure |
| Checkout create | `CheckoutService` -> `IPaymobPaymentService` | Same path | Hardened by this closure |
| Existing incomplete intent | Same durable `PaymentIntent` -> Runtime Application -> adapter | Same path | Replacement rehydration removed; checkpoint reused |
| Retry endpoint | `IPaymobPaymentService.CreatePaymentIntentAsync` | Same path | Accepted order/payment key reused |
| Background recovery | `CheckoutPaymentIntentRecoveryService` -> `IPaymobPaymentService` | Same path | Accepted order/payment key reused |
| Complete intent reuse | SQL read | No provider execution | Existing behavior preserved |
| CommerceHub Paymob create | CommerceHub-local provider path | CommerceHub-local recovery | Existing pre-provider re-init fence and accepted-order recovery confirmed; unchanged because CommerceHub convergence is a separate PAYMOB-FINAL finding |

No second POS/Storefront Paymob order/payment-key HTTP path remains in `PaymobPaymentService`.

## Root Cause

The adapter previously executed this opaque sequence:

```text
Authenticate
  -> Register Paymob order
  -> Create payment key
  -> return provider evidence
  -> application persists provider fields
```

The application could not persist the intermediate accepted order ID. Incomplete-intent rehydration also created a new local intent and a fresh provider session, and failure handling could mark an accepted-provider session as locally failed.

## Checkpoint Model

The existing SQL `PaymentIntent` remains the durable application authority. No new table, migration, controller, DTO, frontend contract, business rule, or Runtime object was introduced.

Checkpoint states are stored in the existing routing metadata and provider fields:

```text
local PaymentIntent committed
  -> order_registration_started
  -> ProviderIntentId + provider_order_accepted
  -> PaymentKey + RedirectUrl + payment_key_accepted_runtime_pending
  -> required Runtime result
  -> normal application persistence/event completion
```

Rules:

1. `order_registration_started` is committed before the provider-order request.
2. The accepted Paymob order ID is committed before payment-key creation.
3. The accepted payment key and iframe URL are committed before provider evidence enters Runtime interpretation.
4. Final application persistence removes the pending checkpoint marker only after the required Runtime outcome is produced.
5. Provider failures after any checkpoint keep the local intent pending and recoverable; they do not mark the accepted provider authority failed.
6. An indeterminate registration-started state with no accepted order ID blocks automatic order recreation with `PAYMOB_PROVIDER_ORDER_OUTCOME_UNKNOWN`. It must reconcile through provider/webhook/operator evidence rather than risk a duplicate order.

## Recovery Flow

```text
Retry / restart / background recovery
  -> load durable PaymentIntent checkpoint
  -> payment key checkpoint exists?
       yes: make no provider call; rebuild provider evidence from checkpoint
       no:
         accepted provider order exists?
           yes: authenticate and create key for that exact order
           no:
             registration-started fence exists?
               yes: stop for reconciliation; never register another order
               no: begin the single permitted provider-order registration
```

This makes recovery deterministic from SQL state. A process cannot use age, a fresh GUID, or a replacement local intent to reopen provider-order creation.

## Architecture Preservation

- Payment Runtime source and contracts are unchanged.
- Runtime Bridge source and contracts are unchanged.
- The provider adapter still owns Paymob protocol translation and returns only canonical provider evidence/error artifacts.
- The new checkpoint store is a provider-specific application persistence handoff; it does not enter Runtime.
- Webhook Adapter, controllers, public DTOs, frontend, routes, and business rules are unchanged.
- `PaymentLifecycleService` remains the single payment lifecycle writer.
- Existing `PaymentIntent` columns and routing metadata are reused; no schema migration is required.
- CommerceHub is audited but not redesigned or converged in this closure.

## Evidence

Implementation evidence:

- `PaymobProviderCheckpointStore` owns write-ahead, accepted-order, and accepted-payment-key persistence.
- `PaymobPaymentAdapter` calls the checkpoint store before every subsequent durable provider operation and rehydrates accepted artifacts without duplicate provider calls.
- `PaymobRuntimeApplicationService` supplies the durable local payment-intent identity through application metadata.
- `PaymobPaymentService` resumes the existing intent and no longer creates `paymob-rehydrate` replacement sessions or writes `REHYDRATION_SUPERSEDED` during creation recovery.
- `PaymentIntentRoutingMetadataSerializer` carries backward-compatible nullable checkpoint fields.
- DI registers the checkpoint store in the existing Paymob provider-adapter composition root.

Focused certification coverage in `PaymobCertificationClosure2Tests` proves:

- accepted provider order is committed before payment-key execution;
- retry after payment-key failure skips provider-order registration;
- accepted-order recovery creates the key for the original order;
- restart recovery reuses a durable payment key with zero provider calls;
- registration ambiguity blocks duplicate provider-order creation;
- provider Runtime remains Paymob-checkpoint free;
- replacement rehydration remains absent.

Verification results:

| Command | Result |
| --- | --- |
| `dotnet test ... --filter FullyQualifiedName~PaymobCertificationClosure2Tests` | **6 passed, 0 failed** |
| Paymob/Payment Runtime focused regression excluding the known `PASS SF-PAY5` artifact guard | **200 passed, 0 failed** |
| Unmodified PAYMOB-FINAL combined filter | **200 passed, 1 failed**; the only failure is the pre-existing `CertificationArtifacts_MustRecordSfPay5Truth` permanent-context guard recorded by PAYMOB-FINAL |
| `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore` | **Succeeded; 0 warnings, 0 errors** |

The pre-existing `PASS SF-PAY5` guard is not changed in this closure because it is unrelated to `PAYMOB-FINAL-002`, and the mission explicitly prohibits closing another finding or modifying permanent context merely to turn that guard green.

## Certification Decision

`PAYMOB-FINAL-002` is closed for the active POS/Storefront Paymob creation, retry, rehydration, and background recovery surface.

The closure guarantees that an accepted provider order is durably retained before payment-key creation and that an accepted payment key is durably retained before Runtime interpretation. Recovery either reuses those artifacts or stops behind a durable reconciliation fence. It never solves uncertainty by creating a replacement provider order.

## Success Answers

| Question | Answer |
| --- | --- |
| High finding closed? | **Yes — PAYMOB-FINAL-002 only.** |
| Durable checkpoint implemented? | **Yes.** Write-ahead, accepted-order, and accepted-payment-key checkpoints are SQL-durable. |
| Recovery deterministic? | **Yes.** Recovery branches only from durable `PaymentIntent` state. |
| Duplicate provider creation prevented? | **Yes.** Accepted artifacts are reused; indeterminate registration is fail-closed. |
| Runtime preserved? | **Yes.** Runtime source, objects, sequencing, and contracts are unchanged. |
| Architecture preserved? | **Yes.** Existing application/bridge/adapter/lifecycle boundaries remain intact. |
| Regression passed? | **Yes for this closure: 6/6 focused and 200/200 applicable Paymob/Runtime tests.** The unmodified aggregate command retains the one documented unrelated `PASS SF-PAY5` artifact failure. |
| Recommend Closure-3? | **Yes, after this focused regression remains green. Closure-3 must address only the next PAYMOB-FINAL finding.** |

No new cross-provider architecture truth was introduced. The existing permanent rule that reusable provider identity must be checkpointed before subsequent provider work is now enforced on the Storefront/POS Paymob path. The existing consolidated risk entry in `.ai-context/KNOWN_RISKS.md` was updated from CommerceHub-only scope to both hardened Paymob surfaces.

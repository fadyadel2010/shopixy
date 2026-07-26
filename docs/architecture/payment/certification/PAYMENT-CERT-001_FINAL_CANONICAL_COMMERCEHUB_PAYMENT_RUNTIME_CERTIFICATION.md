# PAYMENT-CERT-001 - Final Canonical CommerceHub Payment Runtime Certification

Date: 2026-07-20

## Executive Summary

Certification decision: FAIL.

The canonical CommerceHub payment path has materially converged: `CommerceHubPaymentsController` delegates to `ICommerceHubPaymentOrchestrationService`, orchestration delegates to `ICommerceHubPaymentService`, the canonical service uses `IPaymentProviderAdapter` for Stripe and Paymob session/webhook work, and the success completion transaction performs inventory finalization, order paid transition, vendor earnings, settlement/accounting outbox, event outbox, commit, and notification request publication.

The runtime is not fully provider-independent across the complete solution. Three certification-blocking findings remain:

1. `CommerceHubPaymentService` still contains Paymob callback protocol and HMAC verification logic (`HMACSHA512`) instead of delegating that callback parsing/verification to `IPaymentProviderAdapter`.
2. `CheckoutService` still uses `IPaymentGateway`; its Stripe and Paymob gateway implementations call `StripePaymentService` and `PaymobPaymentService`, which are provider-specific runtime services with order/payment mutation authority.
3. `CheckoutPaymentIntentRecoveryService` directly calls `IStripePaymentService.CreatePaymentIntentAsync` and `IPaymobPaymentService.CreatePaymentIntentAsync`, bypassing the canonical CommerceHub orchestration/runtime graph.

The targeted certification test run also failed on the Paymob protocol isolation invariant:

`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~CommerceHubPaymentOrchestrationArchitectureTests|FullyQualifiedName~PaymobProviderAdapterCanonicalIntegrationTests|FullyQualifiedName~CommerceHubPaymentServiceTests|FullyQualifiedName~CommerceHubFinancialLedgerServiceTests|FullyQualifiedName~CommerceHubPaymentOrchestrationServiceTests" --no-restore`

Result: 47 passed, 1 failed. Failure: `PaymobProviderAdapterCanonicalIntegrationTests.Architecture_MustKeepPaymobProtocolInsideAdapterBoundary` found `HMACSHA512` in `CommerceHubPaymentService`.

## Certification Scope

Reviewed implementation areas:

- `CommerceHubPaymentsController`
- `StripeWebhooksController`
- `PaymobWebhooksController`
- `StorefrontStripePaymentsController`
- `StorefrontPaymobPaymentsController`
- `CommerceHubPaymentOrchestrationService`
- `CommerceHubPaymentService`
- `IPaymentRuntime` and `PaymentRuntimeOrchestrator`
- `IPaymentProviderAdapter`, `StripeAdapter`, `PaymobAdapter`
- `CommerceHubPaymentLifecycleAuthority`
- `CommerceHubFinancialWorkflowService`
- `CommerceHubFinancialLedgerService`
- `InventoryConsistencyService`
- Accounting outbox and notification/event publishing
- Dependency injection, telemetry, logging, recovery, architecture tests, and unit tests

## Runtime Conformance Matrix

| Requirement | Status | Evidence |
| --- | --- | --- |
| One runtime authority | FAIL | Canonical path exists, but `CheckoutService` still calls `IPaymentGateway` at `PointOfSale.API/Services/Checkout/CheckoutService.cs:229`, `1533`, `2779`, `2795`; gateway implementations call provider services. |
| One orchestration authority | PASS for canonical controller, FAIL solution-wide | `CommerceHubPaymentsController` injects orchestration at `PointOfSale.API/Controllers/CommerceHubPaymentsController.cs:24-30`, but checkout/recovery paths bypass orchestration. |
| Controllers converge | PASS with observations | Provider-named controllers inject and call `ICommerceHubPaymentOrchestrationService`: Stripe webhook `StripeWebhooksController.cs:43-54`, `129-134`; Paymob webhook `PaymobWebhooksController.cs:41-52`, `144-148`; storefront Stripe `StorefrontStripePaymentsController.cs:34-45`, `128-136`; storefront Paymob `StorefrontPaymobPaymentsController.cs:35-45`, `250-259`. |
| Runtime provider-neutral | FAIL | `CommerceHubPaymentService.ValidateCallbackAsync` is Paymob-specific and verifies HMAC using `HMACSHA512` at `CommerceHubPaymentService.cs:732-811`, `867-900`. |
| Providers protocol-only | FAIL solution-wide | `StripeAdapter` and `PaymobAdapter` are protocol-only, but legacy `StripePaymentService` and `PaymobPaymentService` still own provider protocol plus order/payment mutation. |
| No provider SDK inside runtime | PASS for canonical service, FAIL solution-wide | Stripe SDK construction is in `StripeAdapter.cs:180-188`, but `StripePaymentService` still constructs/parses Stripe webhooks with `EventUtility.ConstructEvent` at `StripePaymentService.cs:1006-1014`. |
| Completion pipeline canonical | PASS for canonical path | Success path in `CommerceHubPaymentService.cs:1955-2192` orders evidence, inventory, paid state, financial workflow, outbox, save, commit. |
| Transaction boundaries correct | PASS for canonical completion, FAIL solution-wide | Canonical completion uses serializable transaction at `CommerceHubPaymentService.cs:1692`, saves and commits at `2191-2192`. Legacy provider services still own independent payment transactions. |
| Recovery deterministic | PASS for canonical recovery, FAIL solution-wide | Canonical recovery blocks unresolved/provider-session gaps, but `CheckoutPaymentIntentRecoveryService.cs:282-301` directly invokes provider payment services. |
| Idempotency preserved | PASS for canonical completion, FAIL solution-wide | Canonical duplicate guards exist at `CommerceHubPaymentService.cs:1766-1802`, compensation idempotency at `1473-1476`; legacy runtimes remain independently idempotent, not unified under one authority. |
| Dependency graph canonical | FAIL | Expected graph is bypassed by `IPaymentGateway` registrations in `Program.cs:665-669` and provider service registrations in `Program.cs:622`, `664`. |
| Tests support implementation | FAIL | Targeted architecture test failed; existing tests also permit legacy provider services and gateway registrations. |

## Controller Audit

Canonical controller:

- `CommerceHubPaymentsController` delegates payment initiation and webhooks to `ICommerceHubPaymentOrchestrationService` (`CommerceHubPaymentsController.cs:24-30`, `89-100`, `190-195`).
- It performs HTTP request concerns: auth token/cookie resolution, rate limit, body read, provider routing, signature-header extraction, and error mapping.
- Exception: it is still provider-aware for request routing and signature header selection (`ResolveProvider`, `ResolveSignature`), but it does not verify signatures or parse provider payloads.

Provider-named controllers:

- `StripeWebhooksController` delegates to orchestration after request preparation (`StripeWebhooksController.cs:91-112`, `129-134`).
- `PaymobWebhooksController` delegates webhook and callback processing to orchestration (`PaymobWebhooksController.cs:144-148`, `233-236`).
- `StorefrontStripePaymentsController` delegates initiation/status to orchestration (`StorefrontStripePaymentsController.cs:128-136`, `268-276`).
- `StorefrontPaymobPaymentsController` delegates initiation/status to orchestration (`StorefrontPaymobPaymentsController.cs:102-108`, `250-259`).

Documented exceptions:

- Provider-specific controllers still exist as route compatibility shims. They do not appear to call provider SDKs or services directly.
- `PaymobWebhooksController` collects callback query parameters before delegation (`PaymobWebhooksController.cs:224-236`). The protocol-level verification then incorrectly occurs in `CommerceHubPaymentService`, not in the adapter.

## Runtime Boundary Audit

Canonical CommerceHub service evidence:

- `CommerceHubPaymentService` receives `IEnumerable<IPaymentProviderAdapter>` and stores adapters by normalized provider code (`CommerceHubPaymentService.cs:104`, `118-133`).
- Session creation delegates to adapters through `GetProviderAdapter(...).CreateSessionAsync` for Paymob and Stripe (`CommerceHubPaymentService.cs:2998-3004`, `3034-3040`).
- Webhook parsing delegates to adapters through `ParseWebhookAsync` (`CommerceHubPaymentService.cs:931-935`, `1148-1152`).

Failure evidence:

- Paymob callback parsing and HMAC verification remain inside `CommerceHubPaymentService` (`CommerceHubPaymentService.cs:732-811`, `867-900`).
- The failed architecture test confirms the runtime-boundary violation by finding `HMACSHA512` in the canonical service.

## Provider Boundary Audit

Canonical adapter evidence:

- `StripeAdapter : IPaymentProviderAdapter` creates Stripe PaymentIntents, verifies Stripe webhook signatures, and performs status lookup (`StripeAdapter.cs:15-18`, `48-90`, `120-145`, `147-188`).
- `PaymobAdapter : IPaymentProviderAdapter` creates hosted checkout/session artifacts, exposes capability metadata, and translates provider failures (`PaymobAdapter.cs:12-24`, `36-58`, `62-170`).
- Paymob provider adapter registration is scoped behind `IPaymentProviderAdapter` (`PaymobProviderAdapterServiceCollectionExtensions.cs:24`).
- Stripe provider adapter registration is scoped behind `IPaymentProviderAdapter` (`StripeFoundationServiceCollectionExtensions.cs:47`).

Failure evidence:

- `StripePaymentService` still owns database mutation, lifecycle service calls, provider webhook parsing, and Stripe signature verification (`StripePaymentService.cs:48-86`, `116-170`, `1006-1014`, `1416-1420`).
- `PaymobPaymentService` still owns database mutation, lifecycle service calls, provider payload parsing, and Paymob HMAC verification (`PaymobPaymentService.cs:56-86`, `105-152`, `719-780`, `1928-1970`).
- These services are registered in DI (`Program.cs:622`, `664`) and reachable through `IPaymentGateway` and recovery service paths.

## Completion Pipeline Verification

Canonical success ordering in `CommerceHubPaymentService`:

1. Verified provider notification enters canonical application through adapter parsing (`CommerceHubPaymentService.cs:931-935`, `1148-1152`).
2. Serializable completion transaction begins (`CommerceHubPaymentService.cs:1692`).
3. Payment/order are loaded and validated (`CommerceHubPaymentService.cs:1695-1743`).
4. Duplicate/processed guard exits without reapplying (`CommerceHubPaymentService.cs:1766-1802`).
5. Completion evidence is recorded (`CommerceHubPaymentService.cs:2018-2034`).
6. Inventory reservations are finalized (`CommerceHubPaymentService.cs:2036-2043`).
7. Marketplace and sub-orders are marked paid by `CommerceHubPaymentLifecycleAuthority` (`CommerceHubPaymentService.cs:2045-2051`).
8. Vendor earnings, settlement, and ledger/accounting are applied by `CommerceHubFinancialWorkflowService` (`CommerceHubPaymentService.cs:2053-2059`; `CommerceHubFinancialWorkflowService.cs:55-124`).
9. Completion outbox and notification request events are written before save (`CommerceHubPaymentService.cs:2126-2135`, `2256-2376`).
10. Save and commit occur after all mutations/outbox writes (`CommerceHubPaymentService.cs:2191-2192`).

Result: canonical completion pipeline passes. Whole-solution certification still fails because alternate provider runtimes remain reachable.

## Runtime Independence Verification

Confirmed canonical evidence:

- Canonical runtime depends on `IPaymentProviderAdapter` and not directly on Stripe/Paymob SDK or HTTP for session/webhook parsing.
- `PaymentRuntimeOrchestrator` is provider-neutral; it models admission, verification, decision, transition, transaction, and settlement stages without provider names.

Blocking occurrences:

- Provider runtime services still registered: `StripePaymentService` (`Program.cs:622`) and `PaymobPaymentService` (`Program.cs:664`).
- Legacy provider gateway path still registered: `IPaymentGateway` and `IProviderPaymentGateway` registrations at `Program.cs:665-669`.
- `CheckoutService` still invokes `_paymentGateway.CreateSessionAsync` for online payment creation (`CheckoutService.cs:1533-1548`, `2779-2806`).
- `CheckoutPaymentIntentRecoveryService` bypasses CommerceHub orchestration and calls provider services directly (`CheckoutPaymentIntentRecoveryService.cs:282-301`).
- `CommerceHubPaymentService` contains Paymob callback HMAC protocol (`CommerceHubPaymentService.cs:867-900`).

## Transaction Audit

Canonical path:

- Payment completion transaction is owned by `CommerceHubPaymentService` and uses serializable isolation (`CommerceHubPaymentService.cs:1692`).
- Inventory finalization joins the same EF context/transaction before commit (`CommerceHubPaymentService.cs:2036-2043`).
- Financial workflow and ledger writes occur before commit (`CommerceHubPaymentService.cs:2053-2059`; `CommerceHubFinancialWorkflowService.cs:101-124`).
- `CommerceHubFinancialLedgerService` enqueues `AccountingOutboxMessage` with the financial transaction in the same context (`CommerceHubFinancialLedgerService.cs:724-755`).
- Canonical event/notification outbox writes occur before `SaveChanges` and commit (`CommerceHubPaymentService.cs:2104`, `2256-2376`, `2191-2192`).

Failure:

- Whole-solution transaction authority is not singular because `StripePaymentService` and `PaymobPaymentService` still open and manage their own payment transactions (`StripePaymentService.cs:133-135`; `PaymobPaymentService.cs:120-122`) and are reachable from checkout/recovery.

## Idempotency Audit

Canonical protections:

- Processed-payment guard prevents duplicate completion (`CommerceHubPaymentService.cs:1766-1802`).
- Refund replay checks payment logs and compensation outbox idempotency (`CommerceHubPaymentService.cs:1457-1476`).
- Compensation outbox creation uses deterministic idempotency keys (`CommerceHubPaymentService.cs:4747-4766`, `4814-4833`).
- Financial ledger duplicate handling is based on idempotency keys and transaction fingerprints (`CommerceHubFinancialLedgerService.cs:778-846`).

Certification result:

- Canonical path has idempotency protections.
- Solution-wide idempotency is not certifiable because duplicate online payment creation/recovery can still enter through noncanonical `IPaymentGateway` and provider-service paths.

## Recovery Audit

Canonical recovery evidence:

- `CommerceHubPaymentRecoveryAuthorityService` blocks payment retry when unresolved captured payment evidence, provider-session persistence gaps, or reservation authority loss exists (`CommerceHubPaymentRecoveryAuthorityService.cs:55-118`, `160-230`).
- `CommerceHubPaymentService` records provider-session outcome-unknown and persistence-gap states rather than creating another external session (`CommerceHubPaymentService.cs:408-463`, `517-617`).
- Unresolved captured provider evidence is persisted with accounting outbox handling (`CommerceHubPaymentService.cs:3782-4028`).
- Compensation outbox worker processes refund-required/failed states and records accounting with idempotent ledger keys (`CommerceHubPaymentCompensationOutboxWorker.cs:14-18`, `191-245`, `946-1000`, `1148-1151`).

Failure:

- `CheckoutPaymentIntentRecoveryService` still calls legacy Stripe/Paymob services directly (`CheckoutPaymentIntentRecoveryService.cs:282-301`), so recovery ownership is not exclusively CommerceHub canonical.

## Dependency Audit

Expected graph:

`Controller -> CommerceHubPaymentOrchestrationService -> IPaymentRuntime -> CommerceHubPaymentService -> IPaymentProviderAdapter -> Provider Adapter -> Provider`

Observed canonical graph:

- Controller to orchestration: present.
- Orchestration to `ICommerceHubPaymentService`: present (`CommerceHubPaymentOrchestrationService.cs:49-56`, `93-100`, `163-168`).
- `IPaymentRuntime` is registered to `PaymentRuntimeOrchestrator` (`PaymentRuntimeServiceCollectionExtensions.cs:12-14`).
- `CommerceHubPaymentService` to `IPaymentProviderAdapter`: present.

Observed bypasses:

- `CheckoutService -> IPaymentGateway -> StripePaymentGateway/PaymobPaymentGateway -> StripePaymentService/PaymobPaymentService`.
- `CheckoutPaymentIntentRecoveryService -> IStripePaymentService/IPaymobPaymentService`.
- Provider services are registered and reachable through DI.

Dependency audit result: FAIL.

## Configuration Audit

Pass observations:

- CommerceHub Stripe options are strongly typed in `CommerceHubPaymentsOptions` (`CommerceHubPaymentsOptions.cs`).
- Paymob options include enabled state, API key, integration ID, iframe ID, HMAC secret, base URL, environment, sandbox mode, timeout, retry count, feature flags, callback URLs, and capability model (`CommerceHubPaymobOptions.cs`).
- Paymob validator checks base URL, timeout, retry count, sandbox/production consistency, required credentials when enabled, callback URL configuration, and capability gates.
- Appsettings contain empty/default placeholders rather than hardcoded live provider secrets.

Observation:

- `CommerceHubPaymentsOptions.Stripe` does not show the same explicit sandbox/production validator shape as Paymob in the inspected file. This is not the certification blocker.

## Telemetry Audit

Pass observations:

- `CommerceHubPaymentOrchestrationTelemetry` has metrics for admission/execution, provider selection, webhook received, completion started/succeeded/failed, order marked paid, inventory finalized, vendor earnings, settlement, and outbox publication.
- Provider latency exists in Paymob adapter telemetry (`PaymobAdapter.cs:158-160`) and Stripe operational telemetry is registered.
- Telemetry meters are registered in `Program.cs:2084-2089`, `2121-2133`.

Gaps:

- Canonical telemetry does not expose a clearly named accounting-specific or recovery-specific metric in `CommerceHubPaymentOrchestrationTelemetry`; accounting outbox delivery metrics exist in `CommerceReliabilityTelemetry` via `AccountingOutboxProcessor`.
- This is an operations/testing observation, not the primary certification blocker.

## Logging Audit

Pass observations:

- Provider logs are isolated in provider adapters/services (`StripeAdapter`, `PaymobAdapter`, legacy provider services).
- Canonical business logs are emitted from `CommerceHubPaymentOrchestrationService` and `CommerceHubPaymentService`.
- Financial audit is centralized through `CommerceHubFinancialWorkflowService` and `ICommerceHubFinancialAuditService` calls (`CommerceHubFinancialWorkflowService.cs:108-123`).

Observation:

- Legacy provider services still log business/lifecycle paths, so audit/log ownership is not as clean solution-wide as the canonical path.

## Test Assessment

Relevant coverage exists:

- `CommerceHubPaymentOrchestrationArchitectureTests` verifies `CommerceHubPaymentsController` and orchestration service dependencies.
- `PaymobProviderAdapterCanonicalIntegrationTests` verifies Paymob adapter session creation, webhook HMAC verification, provider failure translation, and provider-boundary isolation.
- `StripeConvergence1CertificationTests` and `StripeContractClosure2CertificationTests` cover Stripe runtime/admission/replay behavior.
- `CommerceHubPaymentServiceTests`, `CommerceHubPaymentOrchestrationServiceTests`, `CommerceHubFinancialLedgerServiceTests`, and related CommerceHub finance tests cover important unit-level behavior.

Insufficient for certification:

- Tests do not enforce removal of `IPaymentGateway` as an online payment authority.
- Tests do not enforce removal/unreachability of `StripePaymentService` and `PaymobPaymentService`.
- Existing tests explicitly allow legacy provider service files as approved SDK/protocol locations.
- The targeted certification test run failed on Paymob protocol isolation in `CommerceHubPaymentService`.

## Architectural Evidence

Runtime authority:

- Present: `CommerceHubPaymentsController -> ICommerceHubPaymentOrchestrationService -> ICommerceHubPaymentService -> IPaymentProviderAdapter`.
- Not singular: `CheckoutService -> IPaymentGateway -> provider gateway -> provider payment service`.

Business authority:

- Canonical completion business authority is `CommerceHubPaymentService`, `CommerceHubPaymentLifecycleAuthority`, `CommerceHubFinancialWorkflowService`, `CommerceHubFinancialLedgerService`, and `InventoryConsistencyService`.
- Evidence: `CommerceHubPaymentService.cs:1955-2192`, `CommerceHubFinancialWorkflowService.cs:55-124`, `InventoryConsistencyService.cs:58-108`.

Provider authority:

- Canonical provider authority is `IPaymentProviderAdapter` implemented by `StripeAdapter` and `PaymobAdapter`.
- Failure: provider services still contain provider protocol and lifecycle mutation.

Transaction ownership:

- Canonical completion transaction is `CommerceHubPaymentService` serializable transaction.
- Ledger/accounting outbox and event outbox are written before canonical commit.
- Failure: legacy provider services retain separate transaction ownership.

Dependency ownership:

- Canonical DI exists in `Program.cs:459-464`.
- Provider adapter DI exists in `StripeFoundationServiceCollectionExtensions.cs:47` and `PaymobProviderAdapterServiceCollectionExtensions.cs:24`.
- Failure: legacy `IPaymentGateway`, provider gateways, and provider payment services remain registered (`Program.cs:622`, `664-669`).

Recovery ownership:

- Canonical recovery authority exists in `CommerceHubPaymentRecoveryAuthorityService`.
- Failure: `CheckoutPaymentIntentRecoveryService` bypasses canonical CommerceHub orchestration.

## Remaining Findings

### Architecture

- Blocking: `IPaymentGateway` remains an active online payment session authority outside canonical CommerceHub orchestration.
- Blocking: `StripePaymentService` and `PaymobPaymentService` remain registered and reachable provider-specific runtime services.
- Blocking: recovery service directly calls provider-specific payment services.

### Runtime

- Blocking: `CommerceHubPaymentService` owns Paymob callback HMAC verification and protocol field ordering.
- Blocking: legacy provider services still own provider webhook parsing/signature verification plus payment lifecycle mutation.

### Testing

- Blocking for certification confidence: relevant targeted architecture test fails.
- Gap: architecture tests do not reject `IPaymentGateway` bypass or registered legacy provider runtimes.

### Documentation

- Observation: prior certification artifacts and tests describe intermediate convergence phases and may conflict with final canonical runtime expectations.
- Documentation drift alone is not used as a failure reason.

### Operations

- Observation: canonical telemetry is strong for execution/completion/outbox but accounting/recovery metrics are split across other telemetry classes rather than a single payment runtime surface.

## Certification Decision

FAIL

## Certification Rationale

The canonical CommerceHub payment path is substantially implemented and has a deterministic completion transaction, provider-adapter boundary for session/webhook processing, inventory finalization, financial workflow, ledger/accounting outbox, event outbox, and notification request publication.

However, PAYMENT-CERT-001 requires the complete solution to have exactly one provider-independent online payment runtime. The current implementation still has active provider-specific runtime authorities and an active gateway bypass. `CommerceHubPaymentService` also still contains Paymob protocol/HMAC logic. These are Architecture and Runtime findings, so they prevent certification.

# PAYMENT-REFERENCE-FREEZE - Reference Payment Architecture

Date: 2026-07-14  
Scope: Permanent cross-provider Payment Architecture authority  
Status: **CERTIFIED AND ARCHITECTURE FROZEN**  
Runtime posture: **PAYMENT RUNTIME CERTIFIED, PROVIDER-NEUTRAL, AND PERMANENTLY FROZEN**

## Executive Summary

This document is the permanent architectural authority for every payment
provider and payment workflow in PointOfSale. It governs Stripe, Paymob, Fawry,
Manual Payment, Cash On Delivery, Marketplace payment providers, and all future
providers.

The architecture is frozen in one direction:

```text
Provider / Workflow
  -> Application-owned authority and orchestration
  -> Provider-specific translation
  -> Canonical ProviderEvidence
  -> Frozen Payment Runtime
  -> Exact Runtime outcome admission
  -> Single SQL lifecycle writer
  -> Durable SQL truth, outbox, audit, recovery, and reconciliation
```

Providers adapt to the Runtime. The Runtime never adapts to a provider.

The canonical Payment Runtime, its object model, stage order, authority split,
determinism, replay semantics, state transitions, transaction mapping,
settlement mapping, and failure behavior are frozen. No provider may add a
provider status, credential, SDK type, wire contract, transport rule,
persistence concern, or business rule to the Runtime.

Application services own end-to-end orchestration. Adapters translate and
execute provider protocol only. Webhook evidence adapters authenticate and
translate inbound evidence only. `PaymentRuntimeOrchestrator` owns only the
ordered execution of canonical Runtime stages. `PaymentLifecycleService` is the
single durable payment and order-payment lifecycle writer. PostgreSQL remains
the internal business authority; provider responses and callbacks are evidence,
not local lifecycle finality.

Final reference-provider status:

| Provider | Reference status | Certified boundary | Explicit exclusion |
|---|---|---|---|
| Stripe | **REFERENCE PROVIDER - CERTIFIED** | POS Admin + Storefront | Marketplace / CommerceHub |
| Paymob | **REFERENCE PROVIDER - CERTIFIED** | POS Admin + Storefront | Marketplace / CommerceHub |

Reference Provider certification in this freeze certifies the reusable
architecture shape within the named boundary. It does not silently certify a
target deployment, live credentials, provider account entitlement, public
ingress, live transaction evidence, or an excluded Marketplace path.

## Authority And Precedence

This document is the controlling cross-provider architecture authority.

The following records remain authoritative within their narrower scopes:

1. `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md` defines the frozen canonical Runtime.
2. `STRIPE_FINAL_RECERTIFICATION.md` defines Stripe software and reference
   certification for POS Admin + Storefront.
3. `PAYMOB_FINAL_RECERTIFICATION.md` and Paymob Certification Closures define
   the historical Paymob findings, closure evidence, and non-architecture
   certification limits.
4. Provider phase documents define provider-specific contracts and evidence.
5. `.ai-context` defines repository-wide SQL authority, tenant isolation,
   replay, reconciliation, and source-of-truth rules.

If a provider document conflicts with this cross-provider freeze, the provider
must change. This document does not authorize a Runtime change.

The Paymob status is intentionally bounded. The earlier whole-provider Paymob
re-certification included active CommerceHub paths and therefore withheld an
unqualified whole-provider decision. This freeze does not reverse that
whole-provider or live-operation decision. It records POS Admin + Storefront as
the certified Paymob reference architecture after the bounded convergence and
Certification Closures, including Closure 5 sensitive-artifact protection.
Marketplace / CommerceHub remains explicitly excluded and uncertified by this
reference status.

## Certification Vocabulary

Certification labels are not interchangeable:

| Label | Meaning |
|---|---|
| Layer Certified | One named layer satisfies its contract; it does not certify the provider end to end. |
| Architecture Certified | Authority, dependency direction, boundaries, and fail-closed behavior conform to this freeze. |
| Reference Provider Certified | The named bounded provider surface is an approved reusable example of this architecture. |
| Software Certified | The in-scope implementation has no blocking source defect under its final provider board. |
| Source Operational Readiness Certified | Health, telemetry, runbooks, migrations, and recovery controls exist and are source-verified. |
| Deployment Ready | A specific environment has migrations, secrets, endpoints, health, alerts, and rollback verified. |
| Sandbox Validated | Official provider credentials and provider-delivered outcomes have passed the required scenario matrix. |
| Live Operationally Certified | A named deployment has live evidence, monitoring, drills, latency evidence, and operational approval. |

No lower label implies a higher one. In particular, Reference Provider
certification does not imply Live Operational Certification.

## Frozen Architecture

### Canonical Command Flow

```text
Trusted API / Checkout / Admin / Recovery ingress
  -> Runtime Application Service
       -> trusted tenant and actor authority
       -> current tenant provider configuration
       -> capability and environment admission
       -> policy / business authorization when applicable
       -> local PaymentSession / PaymentIntent authority
       -> replay and idempotency check
       -> durable pre-provider checkpoint
       -> one-shot tenant credential handoff
       -> Runtime Bridge
            -> Provider Adapter
                 -> provider protocol mutation or inquiry
                 -> ProviderEvidence or CanonicalProviderError
            -> PaymentRuntimeOrchestrator
                 -> Admission
                 -> Verification
                 -> Decision
                 -> State Transition
                 -> Transaction
                 -> Settlement
                 -> RuntimeResult
       -> exact Runtime outcome admission
       -> PaymentLifecycleService
       -> PostgreSQL + transactional outbox / audit
```

The Runtime Application Service owns the use case. The Runtime Bridge owns the
provider-to-canonical execution bridge. The Runtime Orchestrator owns canonical
stage sequencing only. These are distinct authorities.

### Canonical Webhook Flow

```text
Bounded raw request ingress
  -> server-authoritative tenant resolution
  -> current tenant configuration and webhook capability admission
  -> bounded body and rate controls
  -> official signature / HMAC verification
  -> provider event classification
  -> durable replay lookup / reservation
  -> application Outcome Admission
  -> Webhook Evidence Adapter
       -> canonical ProviderEvidence or CanonicalProviderError
  -> PaymentRuntimeOrchestrator
  -> exact required Runtime outcome validation
  -> PaymentLifecycleService
  -> webhook audit + replay completion + SQL/outbox commit
```

Unknown, malformed, unauthenticated, unsupported, disputed, stale, duplicate,
or non-executable events do not acquire lifecycle authority. An authenticated
provider-negative outcome is still not a positive financial decision. The
application may route admitted negative evidence to the single lifecycle writer
under certified lifecycle rules; it may never reinterpret it as success.

### Canonical Recovery Flow

```text
Current locked SQL authority
  -> durable checkpoint and prior evidence
  -> current tenant configuration
  -> same Runtime Application Service
  -> same credential handoff
  -> same Provider Adapter / Runtime Bridge
  -> same Runtime outcome admission
  -> same single lifecycle writer
```

Recovery never creates a privileged alternate provider path. It resumes or
reconciles the same local and provider identities. If provider acceptance is
possible but cannot be proved, recovery fails closed to a reconciliation state;
it does not create a second external session.

## Frozen Layer Contracts

### Payment Runtime

The Payment Runtime is the provider-neutral, deterministic, in-memory canonical
execution model certified by `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`.

It owns:

- canonical Runtime objects and construction invariants;
- evidence admission and verification;
- canonical decision production;
- legal `PaymentSession` state transitions;
- canonical `PaymentTransaction` artifact production;
- canonical `PaymentSettlement` intent production;
- deterministic Runtime results, trace, identities, and failure artifacts;
- prior-history replay rejection within supplied immutable Runtime inputs.

It does not own:

- providers, SDKs, HTTP, credentials, tenant configuration, or feature flags;
- SQL, EF Core, persistence, replay-record loading, outbox, or transactions;
- APIs, controllers, frontend handoff, or DTO construction;
- provider selection, eligibility, risk, fraud, compliance, or business policy;
- ledger posting, treasury, bank execution, or external settlement;
- durable order or payment lifecycle persistence.

The Runtime is pure for identical normalized immutable input. Runtime stages do
not use the wall clock or random identity generation. Duplicate or stale prior
history cannot create a second financial artifact.

### Runtime Orchestrator

`PaymentRuntimeOrchestrator` is the only canonical Runtime stage sequencer.

It owns:

- canonical stage order;
- one invocation of each required stage;
- context, session, action, attempt, and immediate-parent lineage checks;
- stop-on-failure behavior;
- preservation of successful prior-stage artifacts;
- complete `RuntimeResult` assembly.

It must not own provider calls, credentials, persistence, policy, DTO mapping,
webhook ingress, recovery storage, lifecycle persistence, or business rules.
Runtime stages do not call each other, and bridges do not call individual
Runtime engines.

### Runtime Bridge

Each provider or workflow has a provider-specific Runtime Bridge at the
application boundary.

The bridge owns:

- invocation of the certified provider adapter for the admitted operation;
- conversion of adapter output into canonical action/evidence input;
- invocation of `PaymentRuntimeOrchestrator` exactly once per admitted attempt;
- correlation and lineage preservation across adapter and Runtime results;
- returning a provider-neutral application result for interpretation.

The bridge must not create a decision, transition, transaction, settlement, or
fabricated successful Runtime result. It must not call Runtime stages directly,
write SQL, resolve credentials, own replay storage, mutate lifecycle state, or
hide provider lifecycle rules.

### Provider Adapter

Each provider or workflow has a provider adapter. It is a protocol and
translation boundary, not a business or lifecycle authority.

The adapter may:

- serialize official provider requests;
- execute provider transport under an admitted application command;
- parse official provider responses;
- normalize provider facts into canonical `ProviderEvidence`;
- expose provider capabilities and constraints;
- translate provider failures into `CanonicalProviderError`;
- perform provider-specific primitive verification or inquiry when directed.

The adapter must not:

- resolve, select, decrypt, infer, cache, refresh, or fall back to credentials;
- query tenant configuration or business persistence;
- write SQL or manage application transactions;
- decide eligibility, routing, risk, fraud, retry, refund, capture, void,
  settlement, or manual review policy;
- own payment or order lifecycle progression;
- create Runtime decisions, transitions, transactions, settlements, or results;
- treat provider acceptance, redirect, SDK success, or browser state as finality;
- bypass the Runtime Application Service, Runtime Bridge, or Runtime evidence
  pipeline.

Provider-specific concepts terminate inside the adapter. Only canonical
evidence, canonical error, capability/constraint facts, and approved handoff
artifacts may leave it.

### Webhook Evidence Adapter

Each asynchronous provider has a webhook/callback evidence adapter. Manual and
COD workflows require an equivalent receipt, review, delivery, or collection
evidence adapter even when no HTTP webhook exists.

The adapter owns:

- provider-specific signature, message-signature, or HMAC primitive handling;
- parsing and classification of the provider event;
- mapping authenticated supported facts to canonical `ProviderEvidence`;
- canonical failure translation.

The adapter admits evidence only. It must not own durable replay, prior-evidence
loading, outcome admission policy, SQL, lifecycle mutation, refund execution,
settlement, accounting, notification, or frontend behavior. It must never make
an unknown event executable by guessing a canonical status.

### Advanced Operations Adapter

Capture, refund, partial refund, void/cancel, retrieve/inquiry, and comparable
operations use a provider-specific Advanced Operations Adapter.

The adapter is certified first as translation-only. Translation-only
certification does not activate an operation.

An advanced operation becomes active only through a separately certified
application command that proves:

- current tenant and actor authority;
- provider capability and policy permission;
- current SQL payment/refund authority;
- operation idempotency and durable pre-provider checkpoint;
- one-shot tenant credential handoff;
- canonical evidence or canonical error output;
- required Runtime execution and exact outcome admission where the operation
  can affect payment lifecycle or financial artifacts;
- mutation only through the single lifecycle writer;
- recovery, replay, reconciliation, audit, and operational readiness.

No advanced adapter may become an alternate lifecycle writer.

### Runtime Application Service

Each provider has one Runtime Application Service per bounded payment use-case
family. This is the end-to-end orchestration authority.

It owns:

- trusted tenant, actor, order, and payment authority acquisition;
- tenant provider configuration and capability resolution;
- business-policy invocation where applicable;
- local authority creation/reuse before external mutation;
- idempotency, replay lookup, durable checkpoints, and recovery selection;
- credential handoff creation and lifetime;
- Runtime Bridge invocation;
- exact Runtime outcome validation;
- public DTO and error mapping;
- entry into the single lifecycle writer and application transaction boundary;
- audit, telemetry, reconciliation routing, and fail-closed response behavior.

Controllers, checkout services, retry jobs, and recovery workers must delegate
to this boundary or to a provider-neutral service that delegates to it. They
must not call adapters or provider HTTP directly.

### Tenant Configuration Authority

Tenant configuration is application authority, never adapter authority.

Every operation performs a current trusted tenant-scoped resolution before
Runtime, provider, signature/HMAC, retry, or recovery execution. Resolution must
validate at least:

- trusted tenant identity and current tenant activity;
- enabled, non-deleted provider configuration;
- provider code and operation capability;
- Sandbox/Production environment compatibility;
- required credential shape and provider account identity;
- global provider enablement and feature gates;
- timeout, retry, API/protocol version, and endpoint governance as applicable.

Missing, deleted, disabled, malformed, cross-tenant, environment-mismatched, or
capability-ineligible configuration fails closed. Global credentials may not be
used as a silent tenant fallback.

### Tenant Credential Handoff

After successful tenant configuration resolution, the application supplies the
exact validated credential set to the adapter through a bounded one-operation
handoff.

The handoff must be:

- scoped to one trusted tenant and one admitted operation;
- single-use and overlap-safe;
- consumed before provider I/O;
- cleared after consumption or failure;
- unavailable to Runtime and durable evidence;
- non-cacheable and non-reusable by later operations.

The adapter validates handoff correlation but never independently obtains
credentials. Credential rotation creates a new authoritative resolution; it
does not mutate prior evidence or Runtime artifacts.

### Replay Protection

Replay protection exists at two boundaries:

1. Application replay authority durably records and loads provider event,
   operation, attempt, and idempotency identities.
2. Runtime prior-history verification deterministically rejects duplicate or
   out-of-order canonical evidence and downstream artifacts supplied to it.

Required properties:

- stable tenant + provider + provider-event or provider-operation identity;
- database uniqueness for the durable replay fence where persistence exists;
- deterministic canonical evidence identity and hash;
- duplicate, concurrent, delayed, and out-of-order collapse;
- explicit processing/retry/reconciliation state after ambiguous failure;
- replay completion recorded only under the application transaction rule;
- no duplicate lifecycle transition, transaction, settlement, refund, capture,
  void, provider session, or external operation.

Redis, memory locks, request deduplication, and provider idempotency keys may
assist but are not substitutes for durable application replay authority.

### Checkpoint Recovery

A durable local authority row, idempotency identity, routing identity, and
minimum reconciliation metadata must exist before crossing an external provider
mutation boundary.

For a multi-step provider protocol:

1. Persist the operation-started checkpoint before the provider mutation.
2. Persist every accepted reusable provider identity immediately after it is
   received and before the next provider mutation.
3. Persist protected reusable handoff material before returning it or entering
   later processing that depends on it.
4. Resume from the latest proven checkpoint without recreating prior accepted
   provider authority.
5. If acceptance is indeterminate, block automatic recreation and route to
   inquiry, webhook reconciliation, or manual review.

Recovery must use current SQL authority and the original provider identity. It
must not reconstruct primary payment truth from browser state, cache, projection
documents, stale queue payloads, or an old callback.

### Outcome Admission

Outcome Admission belongs to the Application layer. It decides whether a
provider result or inbound event is eligible to enter executable Runtime and
lifecycle processing.

Admission must reject or quarantine, as appropriate:

- unknown or unsupported event/operation types;
- malformed or unauthenticated evidence;
- wrong tenant, provider, payment, amount, currency, environment, or account;
- non-terminal evidence presented as terminal;
- terminal negative evidence presented as financial success;
- duplicate, stale, replayed, disputed, or out-of-order evidence;
- missing required Runtime artifacts or unexpected Runtime results;
- ambiguous external outcomes that require reconciliation.

Application code must validate the exact Runtime outcome required for the use
case. A non-null or superficially successful `RuntimeResult` is insufficient.

### Single Writer

`PaymentLifecycleService` is the single durable application writer for payment
and order-payment lifecycle state.

All accepted create, success, failure, cancellation, refund, capture, void,
settlement, expiration, callback, webhook, recovery, and reconciliation paths
that affect lifecycle must converge on the owning lifecycle service. Provider
adapters, Runtime bridges, webhook adapters, controllers, jobs, projections,
and frontend code are never lifecycle writers.

The Runtime owns canonical in-memory state transition and financial artifact
production. The lifecycle service owns durable SQL mutation. These authorities
are complementary and must not be collapsed or duplicated.

### Provider Evidence

Provider evidence is an immutable candidate fact, not final local truth.

Evidence must carry deterministic identity, provider reference, correlation,
tenant/payment/action lineage, type, outcome, amount/currency where relevant,
time, payload reference or protected audit reference, and integrity evidence as
required by the canonical contract.

Provider success does not override terminal SQL truth. Late or contradictory
external-money evidence is preserved and routed to unresolved/suspense/manual
reconciliation according to certified policy. It must not fabricate local
success, discard external-money evidence, or reopen a duplicate capture lane.

### Canonical Provider Error

Every provider boundary translates provider SDK, HTTP, transport, protocol,
authentication, validation, throttling, rejection, timeout, and unknown failures
into a provider-neutral `CanonicalProviderError` before the error leaves the
adapter boundary.

The canonical error must preserve safe classification, retryability or outcome
ambiguity, provider code/status, request correlation, and bounded redacted
diagnostics. It must not expose credentials, authorization values, reusable
handoff artifacts, raw signatures, customer-sensitive payloads, SDK exceptions,
or provider wire objects.

Canonical error contracts must live in a provider-neutral application contract
boundary. A provider-owned namespace or provider registration must not become a
dependency of another provider's permanent architecture.

### Operational Readiness

Every provider must have source-verifiable operational controls before final
software certification and environment-verifiable controls before deployment.

Minimum source controls:

- startup configuration validation and fail-closed feature gates;
- foundation and operational-readiness health checks that probe real
  dependencies rather than DI registration alone;
- bounded governed HTTP clients with explicit timeout, retry, idempotency, and
  ambiguous-outcome behavior;
- production-path metrics for requests, failures, latency, webhook receipt,
  signature rejection, replay, Runtime admission/rejection, lifecycle outcomes,
  recovery, reconciliation, and advanced operations;
- low-cardinality, non-secret telemetry and correlation-aware logs;
- alert matrix, ownership, escalation, runbooks, rotation procedures, replay
  recovery, reconciliation, outage, rollback, and data-retention guidance;
- migration discovery, generated SQL review, uniqueness fences, and current
  model-state review;
- encrypted or purpose-protected reusable payment artifacts;
- encrypted/minimized webhook payload evidence and digested raw signatures;
- no secrets or reusable provider artifacts in logs, metrics, public errors,
  health output, events, cache snapshots, or durable projections.

Minimum environment controls include applied migrations, tenant credentials,
official provider endpoint registration, signed delivery, health, alerts,
dashboards, secret rotation, failure drills, replay/duplicate drills,
two-tenant isolation, measured latency, rollback, and recovery evidence.

## Authority Matrix

| Concern | Exclusive authority | Provider role |
|---|---|---|
| Tenant identity | Trusted application tenant resolution | None |
| Provider configuration | Tenant Configuration Authority | Supplies externally provisioned account facts only |
| Credential selection | Runtime Application Service | None |
| Credential use | One-shot application handoff | Adapter consumes once |
| Business eligibility and routing | Certified application/policy authority | Capability and constraint facts only |
| Provider protocol | Provider Adapter | Serialize, call, parse, translate |
| Evidence admission | Application Outcome Admission + Runtime Admission | Supplies candidate evidence |
| Canonical stage sequencing | `PaymentRuntimeOrchestrator` | None |
| Canonical session transition | `StateTransitionRuntime` | Evidence only |
| Canonical transaction artifact | `PaymentTransactionRuntime` | Evidence only |
| Canonical settlement intent | `PaymentSettlementRuntime` | Evidence only |
| Durable payment lifecycle | `PaymentLifecycleService` + PostgreSQL | No mutation authority |
| Replay persistence | Application replay store + SQL uniqueness | Stable provider identity input |
| Checkpoint and recovery | Application service + SQL | Inquiry and original provider identity |
| Accounting / ledger | Accounting authority | Settlement/reconciliation evidence only |
| Treasury / bank movement | Treasury/bank authority | External settlement evidence only |
| Frontend state | No business authority | Displays authorized handoff/status only |

## Mandatory Architecture Rules

The governance axioms are:

```text
RUNTIME IS FROZEN
PROVIDERS ADAPT TO RUNTIME
NEVER RUNTIME TO PROVIDER
NO PROVIDER-SPECIFIC RUNTIME LOGIC
NO SQL INSIDE RUNTIME
NO PROVIDER LIFECYCLE AUTHORITY
SINGLE LIFECYCLE WRITER
APPLICATION OWNS ORCHESTRATION
ADAPTERS TRANSLATE ONLY
WEBHOOK ADAPTERS ADMIT EVIDENCE ONLY
TENANT CREDENTIALS ARE AUTHORITATIVE
PROVIDER ADAPTERS NEVER RESOLVE CREDENTIALS
DURABLE CHECKPOINTS BEFORE PROVIDER MUTATION
FAIL CLOSED
PROVIDER NEUTRALITY IS MANDATORY
```

The following rules are permanent and mandatory:

1. Payment Runtime is frozen.
2. Providers and workflows adapt to Runtime; Runtime never adapts to them.
3. Provider neutrality is mandatory.
4. No provider-specific Runtime logic, type, status, branch, SDK, credential,
   endpoint, transport, or wire contract is permitted.
5. No SQL, EF Core, persistence, replay loading, tenant lookup, or network I/O is
   permitted inside Runtime.
6. `PaymentRuntimeOrchestrator` is the only canonical Runtime stage sequencer.
7. Application owns end-to-end orchestration.
8. Runtime Bridge invokes the provider adapter and Runtime orchestrator; it does
   not create Runtime authority.
9. Provider adapters translate and execute protocol only.
10. Webhook and equivalent workflow adapters authenticate and admit evidence
    candidates only.
11. Advanced adapters remain translation-only until an application command is
    independently certified.
12. Adapters never own provider or payment lifecycle authority.
13. Adapters never resolve credentials.
14. Current tenant credentials and capability configuration are authoritative.
15. Credential handoff is tenant-bound, operation-bound, single-use, and
    unavailable to Runtime.
16. Global credentials never silently replace missing tenant credentials.
17. Durable local authority and a re-init fence exist before provider mutation.
18. Accepted reusable provider identity is checkpointed before the next
    provider mutation.
19. Ambiguous external acceptance fails closed to recovery or reconciliation;
    it never triggers blind recreation.
20. Provider evidence is never final payment status by itself.
21. Provider errors cross the adapter boundary only as canonical redacted
    errors.
22. Outcome Admission belongs to Application.
23. Application validates the exact required Runtime outcome before lifecycle
    mutation.
24. `PaymentLifecycleService` is the single durable lifecycle writer.
25. PostgreSQL payment/refund/settlement truth dominates callbacks, queues,
    cache, projections, dashboards, exports, and frontend state.
26. Replay and duplicate protection must be durable, deterministic, and
    tenant-scoped.
27. Recovery reuses original local and provider identities and the same
    certified path.
28. Unknown, malformed, unsupported, cross-tenant, or inconsistent evidence
    fails closed.
29. Late provider success after terminal SQL truth is preserved for
    reconciliation, not promoted to local success.
30. Policies and business rules do not live in Runtime or adapters.
31. Runtime executes only admitted, policy-permitted operations where a policy
    gate applies.
32. Provider capability is not business eligibility.
33. Secrets and reusable provider artifacts are never logged or persisted in
    plaintext.
34. Controllers, checkout services, jobs, and frontend code never call provider
    SDK/HTTP or adapters as an alternate execution path.
35. No provider may bypass any frozen layer because its protocol is synchronous,
    manual, cash-based, redirect-based, webhook-free, or marketplace-specific.
36. Any Runtime change requires separate architecture review and complete
    Runtime recertification before provider work continues.

## Certified Reference Providers

### Stripe

Status: **REFERENCE PROVIDER - CERTIFIED**  
Boundary: **POS Admin + Storefront**  
Excluded: **Marketplace / CommerceHub**

Stripe is the primary SDK/PaymentIntent-shaped reference. Its certified pattern
demonstrates tenant configuration, provider adapter isolation, Runtime Bridge,
webhook evidence admission, durable replay, protected client-secret handling,
exact Runtime outcome validation, single-writer lifecycle convergence, and
source-level operational readiness.

Disabled Checkout Sessions, dormant advanced-operation commands, and direct
Marketplace/CommerceHub paths are not part of the reference template.

### Paymob

Status: **REFERENCE PROVIDER - CERTIFIED**  
Boundary: **POS Admin + Storefront**  
Excluded: **Marketplace / CommerceHub**

Paymob is the primary multi-step redirect/HMAC-shaped reference. Its bounded
reference pattern demonstrates provider order/payment-key checkpoint recovery,
Runtime Application Service convergence, tenant credential authority, one-shot
adapter credential handoff, webhook evidence admission, replay closure,
sensitive artifact protection, exact Runtime outcome enforcement, and
single-writer lifecycle convergence.

This certification does not certify Paymob Marketplace/CommerceHub, an active
advanced-operation command surface, a target deployment, or live Paymob
operation. Provider-specific remaining software and operational findings retain
the disposition of the latest provider re-certification until separately
closed. They cannot be used to weaken this architecture or to claim an excluded
surface is certified.

## Future Provider Requirements

Every future provider or workflow must implement the same architecture. A
provider may use different protocol mechanics, but it may not omit an authority
layer.

| Future provider/workflow | Required adaptation |
|---|---|
| Fawry | Reference-code creation, signed callback/status inquiry, expiration, refund, and reconciliation map to provider adapter, canonical evidence, Runtime, checkpoints, replay, and one writer. |
| Manual Payment / Manual Wallet | Receipt submission, operator review, rejection, approval, expiration, and reconciliation use a workflow adapter and canonical evidence. Operator action is evidence/policy input, not direct SQL status mutation. |
| Cash On Delivery | Delivery, failed-delivery, collection, remittance, discrepancy, and settlement evidence use the same Runtime and lifecycle writer while fulfillment and cash/treasury authorities remain separate. |
| Marketplace Providers | Must implement the complete frozen stack inside an independently certified Marketplace bounded context, including multi-merchant identity, split-order/payment lineage, settlement, refund, payout, tenant/vendor scope, and reconciliation. Current Stripe and Paymob Marketplace paths are not certified by this freeze. |
| Wallets / BNPL / Bank Transfer / Future Providers | Capabilities, constraints, asynchronous evidence, delayed finality, recovery, settlement, and provider-specific protocol stay outside Runtime and enter through the same layers. |

For a provider without an HTTP webhook, the Webhook Evidence Adapter requirement
is satisfied by an explicitly certified equivalent inbound evidence boundary,
such as receipt review, driver collection confirmation, bank statement import,
or provider status inquiry. It is never satisfied by deleting evidence
admission or allowing direct lifecycle writes.

For a provider without external credentials, Tenant Configuration Authority and
Credential Handoff remain explicit: configuration/capability authority is still
required, and certification records the credential handoff as not applicable
only after proving no secret or external account authority exists. A no-op
implementation may not conceal a bypass.

## Certification Pipeline

Every provider must pass the gates in order. A later gate may expose a defect in
an earlier gate and return the provider to closure work.

### Gate 1 - Foundation

Required evidence:

- official supported SDK/protocol and version decision;
- immutable provider configuration model;
- environment separation and feature/capability flags;
- tenant credential governance and secure secret handling;
- governed DI/lifetimes and HTTP clients;
- configuration health and provider-neutral Runtime isolation.

Exit: Foundation Layer Certified. No payment execution is implied.

### Gate 2 - Provider Adapter

Required evidence:

- canonical request/action mapping;
- official response/evidence mapping;
- deterministic provider idempotency identity;
- capability and constraint mapping;
- canonical provider error translation;
- proof of no SQL, lifecycle ownership, Runtime modification, credential
  resolution, webhook ownership, or controller/frontend coupling.

Exit: Provider Adapter Certified - Translation/Protocol Only.

### Gate 3 - Runtime Bridge

Required evidence:

- adapter-to-evidence-to-orchestrator execution order;
- one orchestrator invocation;
- complete lineage and correlation preservation;
- exact failure propagation;
- proof the bridge does not call Runtime stages or create Runtime artifacts;
- proof of no SQL, lifecycle mutation, credential resolution, or hidden policy.

Exit: Runtime Bridge Certified.

### Gate 4 - Webhook Evidence Adapter

Required evidence:

- official signature/HMAC verification;
- bounded raw ingress and event classification;
- supported-event-to-evidence mapping;
- unknown and invalid event fail-closed behavior;
- canonical error translation;
- proof of evidence-only responsibility and no SQL/lifecycle mutation.

For non-webhook workflows, certify the equivalent inbound evidence adapter.

Exit: Webhook/Evidence Adapter Certified - Evidence Only.

### Gate 5 - Advanced Operations

Required evidence for each operation:

- capability matrix and unsupported-operation behavior;
- request, response, evidence, idempotency, and error mappings;
- no lifecycle, persistence, Runtime, policy, or credential authority inside the
  adapter;
- explicit declaration of translation-only versus active-command status.

Exit: Advanced Operations Adapter Certified - Translation Only. Activation
requires later application-contract and convergence certification.

### Gate 6 - Application Contract

Required evidence:

- one Runtime Application Service;
- trusted tenant configuration and one-shot credential handoff;
- public DTO/handoff compatibility;
- exact Runtime result interpretation;
- canonical public error mapping;
- durable local authority and checkpoint order;
- no controller, checkout, retry, or recovery bypass.

Exit: Application Contract Certified for each named operation and surface.

### Gate 7 - Webhook Closure

Required evidence:

- application-owned Outcome Admission;
- durable replay lookup, uniqueness, and completion;
- prior-evidence loading outside Runtime;
- required Runtime outcome enforcement before mutation;
- one lifecycle writer;
- audit minimization/protection and retry-safe HTTP behavior;
- duplicate, concurrent, delayed, unknown, negative, refund, and failure paths.

Exit: Application Webhook Contract Certified.

### Gate 8 - Convergence

Required evidence:

- complete entry-point execution map;
- legacy and direct-provider-call audit;
- one create, retry, recovery, webhook, and reconciliation path;
- no controller/service/job/provider HTTP bypass;
- one Runtime authority and one durable lifecycle writer;
- explicit bounded-context exclusions.

Exit: Convergence Certified for the precisely named bounded context.

### Gate 9 - Operational Readiness

Required evidence:

- dependency-backed health and startup validation;
- executing-path telemetry, metrics, alerts, and dashboards;
- governed HTTP retry/timeout/idempotency behavior;
- runbooks and failure/rotation/replay/reconciliation drills;
- migration and data-protection readiness;
- secret/artifact/log redaction;
- rollout, rollback, ownership, escalation, and retention controls.

Exit: Source Operational Readiness Certified. Environment validation remains a
separate decision.

### Gate 10 - Sandbox Validation

Required evidence in an official provider environment:

- valid isolated Sandbox tenant credentials;
- public signed callback/webhook reachability;
- success, decline/failure, cancellation/expiration, refund, and each active
  advanced-operation scenario;
- duplicate, replay, concurrency, delay, outage, timeout, ambiguity, recovery,
  and secret-rotation drills;
- two-tenant isolation;
- health, alert firing, backlog/replay, and measured latency evidence.

Exit: Sandbox Validated for the named deployment and operation matrix. If
external evidence is unavailable, this gate is Pending and Live Operational
Certification is forbidden; architecture/source labels remain independently
reported.

### Gate 11 - Final Certification

Required evidence:

- unified architecture, implementation, security, operations, migration,
  regression, extensibility, and production review;
- severity-classified findings with no open blocking software finding for the
  requested label;
- exact certified surface and exclusions;
- explicit Software, Reference Provider, Deployment, and Live Operation
  decisions rather than one ambiguous verdict.

Exit: Final Certification decision.

### Gate 12 - Certification Closures

When Final Certification identifies blockers, each closure must:

- name exactly one finding or a tightly bounded root-cause group;
- preserve the frozen Runtime;
- document root cause, correction, recovery, compatibility, security, and
  regression evidence;
- avoid claiming unrelated findings are closed;
- retain operational and excluded-surface limits.

Exit: Named finding closed only.

### Gate 13 - Final Re-Certification

After closures, an independent board must re-read the complete current provider
record and current source, verify closure behavior, rerun the full applicable
regression and migration checks, recount remaining findings, and issue the new
controlling provider decision.

Re-certification must not inherit a historical pass without current evidence.
It must distinguish bounded architecture/reference status from whole-provider,
deployment, and live-operation status.

## Certification Rules

1. Every certification names its exact provider, operations, applications, and
   bounded context.
2. Exclusions are explicit and never implied to pass.
3. A layer certification never certifies a later layer.
4. Translation-only advanced operations are not active operations.
5. Source operational readiness is not Sandbox or live validation.
6. Missing credentials or public ingress may leave environment validation
   pending, but source defects remain source defects.
7. A broad provider name filter is not sufficient regression evidence when it
   includes unrelated failures; applicable and aggregate results must both be
   reported honestly.
8. Architecture guards prove boundaries but do not replace behavioral,
   migration, recovery, security, or provider-environment evidence.
9. A closure changes only the disposition it names until final re-certification
   reviews the whole provider.
10. Provider protocol age, regional support, account entitlement, and official
    API direction must be stated; an existing supported legacy protocol does not
    automatically become the future-provider template.
11. Provider-specific types and DI ownership may not become shared canonical
    contracts merely because another provider currently imports them.
12. Certification evidence must prove Fail Closed behavior under missing,
    malformed, ambiguous, duplicate, stale, cross-tenant, and out-of-order input.
13. Certification must verify durable checkpoint order around every external
    mutation capable of creating reusable provider authority.
14. Certification must verify the single writer for every lifecycle-affecting
    entry point, including recovery and reconciliation.
15. Certification must preserve provider evidence needed to reconcile external
    money while protecting secrets and personal data.

## Architecture Governance

### Frozen Changes Requiring Runtime Re-Certification

Any proposed change to the following requires a separate Runtime architecture
review and complete Runtime recertification before merge or provider adoption:

- canonical Runtime objects or identifiers;
- stage order or stage authority;
- admission, verification, decision, transition, transaction, or settlement
  semantics;
- state transition table;
- determinism, hashing, timestamps, or identity derivation;
- replay and prior-history behavior;
- transaction or settlement mapping;
- failure-stop or artifact-preservation behavior;
- Runtime dependencies or provider neutrality.

A provider need is not justification for such a change. The first response must
be to adapt the provider at the application/adapter boundary.

### Provider Changes Requiring Provider Re-Certification

Provider re-certification is required for changes to:

- provider protocol, SDK, API version, endpoint, or region;
- credential model, tenant resolution, environment rules, or key rotation;
- request/evidence mapping, supported event types, or canonical error mapping;
- capability/constraint matrix or active advanced operations;
- application orchestration, checkpoint order, replay store, recovery, outcome
  admission, or single-writer routing;
- sensitive artifact persistence, webhook audit, telemetry, health, migration,
  runbooks, or public contract;
- bounded-context scope, especially Marketplace/CommerceHub adoption.

### Governance Enforcement

Future payment pull requests and architecture reviews must cite this document.
They must state:

- provider and bounded context;
- affected frozen layer;
- whether Runtime is unchanged;
- authority and dependency direction;
- tenant credential and handoff path;
- checkpoint/replay/recovery behavior;
- exact Runtime outcome admission;
- single-writer proof;
- security and operational evidence;
- certification gate reached and gates still pending.

An exception cannot be approved informally. Any claimed exception requires a
new architecture decision that explicitly supersedes the affected rule and, if
Runtime is affected, complete Runtime recertification. Until then, the system
fails closed and the provider remains uncertified.

## Final Freeze Declaration

```text
PAYMENT RUNTIME: CERTIFIED AND FROZEN
RUNTIME ORCHESTRATION: FROZEN
PROVIDER-NEUTRAL CONTRACTS: FROZEN
APPLICATION ORCHESTRATION OWNERSHIP: FROZEN
TENANT CONFIGURATION AND CREDENTIAL AUTHORITY: FROZEN
PROVIDER EVIDENCE MODEL: FROZEN
OUTCOME ADMISSION: APPLICATION OWNED
PAYMENT LIFECYCLE: SINGLE SQL WRITER
STRIPE REFERENCE PROVIDER: CERTIFIED - POS ADMIN + STOREFRONT
PAYMOB REFERENCE PROVIDER: CERTIFIED - POS ADMIN + STOREFRONT
MARKETPLACE / COMMERCEHUB PAYMENT PROVIDERS: EXCLUDED AND NOT CERTIFIED HERE
FUTURE PROVIDERS: MUST ADAPT TO THIS ARCHITECTURE
```

## Final Certification Decision

| Question | Decision |
|---|---|
| Frozen Runtime? | **Yes. Permanently frozen; changes require full Runtime recertification.** |
| Frozen Architecture? | **Yes.** |
| Provider-neutral? | **Yes. Mandatory.** |
| Application owns orchestration? | **Yes.** |
| Adapters translate only? | **Yes.** |
| Webhook adapters admit evidence only? | **Yes.** |
| Tenant credentials authoritative? | **Yes.** |
| Adapters resolve credentials? | **No. Forbidden.** |
| Durable checkpoints required before provider mutation? | **Yes.** |
| Replay and recovery frozen? | **Yes.** |
| Outcome Admission application-owned? | **Yes.** |
| Single durable lifecycle writer? | **Yes, `PaymentLifecycleService`.** |
| Stripe reference certified? | **Yes, POS Admin + Storefront.** |
| Paymob reference certified? | **Yes, POS Admin + Storefront.** |
| Marketplace / CommerceHub certified by this freeze? | **No. Explicitly excluded.** |
| Fawry ready to adapt? | **Yes, through the complete certification pipeline.** |
| Manual Payment / Wallet ready to adapt? | **Yes, through equivalent workflow/evidence layers.** |
| Cash On Delivery ready to adapt? | **Yes, while fulfillment and cash/treasury authority remain separate.** |
| Future providers may bypass a layer? | **No.** |
| Implementation authorized by this document? | **No. This is Architecture Governance certification only.** |

No Runtime, provider implementation, test, migration, or `.ai-context` file is
modified or authorized by this certification. The freeze consolidates existing
provider-neutral Runtime, application orchestration, tenant authority,
provider-evidence, replay, checkpoint, single-writer, fail-closed, and
reconciliation truths into one permanent cross-provider authority.

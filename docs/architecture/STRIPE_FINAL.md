# STRIPE-FINAL Independent Principal Architecture Re-Certification

Date: 2026-07-13

Scope: POS Admin and Storefront Stripe bounded context. CommerceHub and Marketplace payments are excluded.

Final decision: **WITHHOLD CERTIFICATION**

Payment Runtime decision: **CERTIFIED, PROVIDER-NEUTRAL, AND FROZEN**

## Executive Summary

The current code preserves the intended provider-neutral architecture. Stripe SDK types stay outside the frozen Payment Runtime, Stripe evidence enters the canonical orchestrator, financially admitted success and refund webhooks must complete the Runtime before SQL lifecycle mutation, and `PaymentLifecycleService` remains the sole durable payment/order lifecycle writer. The create path also enforces the exact non-financial Runtime result that represents a client-confirmation handoff rather than treating Runtime output as observational.

That architecture is not yet packaged as an implementation-complete permanent reference provider. This independent audit found concrete source defects that are unrelated to missing live Stripe evidence:

1. `20260712160000_StripeContractClosure2WebhookReplay` is not discoverable by EF Core. It has no generated designer metadata and no explicit `Migration`/`DbContext` attributes, is absent from `ApplicationDbContextModelSnapshot`, and does not appear in `dotnet ef migrations list`. A normal deployment therefore cannot create the replay table through the repository migration pipeline.
2. `RedirectUrlRecoveryHostedService` attempts to deserialize `TenantPaymentMethod.ConfigEncrypted` directly as JSON even though the column contains Data Protection ciphertext and has no EF value converter. The documented missing-client-secret recovery path stops before calling the correctly decrypting Runtime application service.
3. Global Stripe enablement, capability flags, sandbox/production mode, timeout, retry, and telemetry settings do not govern the tenant-scoped live PaymentIntent client. The live client creates a default `PaymentIntentService` and supplies only the tenant API key. `PaymentIntentsEnabled`, `WebhooksEnabled`, and the other capability flags have no production consumers.
4. Tenant Stripe configuration validation checks presence but not key prefix or `TenantPaymentMethod.IsSandbox` consistency. The live client does not use the resolved `IsSandbox` value. The webhook configuration lookup also bypasses query filters without excluding `IsDeleted`, so deleted configuration can remain signature authority if it is still enabled.
5. Paid `checkout.session.completed` events are admitted into Runtime, but the bounded-context lifecycle switch has no handler for them. The current application does not create Stripe Checkout Sessions and the flag is disabled, so this is not a live PaymentIntent defect, but the event must not remain inside the certified reference surface as if it were complete.

The focused Stripe and Runtime suites are green, and the Storefront production build succeeds. Those suites do not exercise EF migration discovery, encrypted background recovery, effective feature gating, or tenant key-mode validation. The evidence is sufficient to withhold final/reference certification, not sufficient to certify the current implementation as permanent.

Live Stripe test-mode and production validation remains a deployment-time prerequisite. It is **not** the reason certification is withheld.

## Architecture Review

### Dependency graph

The implemented core path is directionally correct:

```text
Storefront/POS application
  -> StripePaymentService
  -> StripeRuntimeApplicationService
  -> StripeRuntimeBridge
  -> StripePaymentIntentAdapter
  -> official Stripe SDK
  -> canonical ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> RuntimeResult
  -> application persistence
```

The financial webhook path is also ordered correctly:

```text
Webhook request pipeline
  -> server-authoritative tenant resolution
  -> official Stripe signature verification
  -> application outcome admission and replay check
  -> StripeWebhookEvidenceAdapter
  -> PaymentRuntimeOrchestrator
  -> successful canonical financial RuntimeResult
  -> PaymentLifecycleService
  -> PostgreSQL commit, outbox, webhook log, replay record
```

Negative terminal events are deliberately not admitted as positive financial Runtime evidence. They remain authenticated provider evidence interpreted by the single SQL lifecycle writer. This is consistent with the frozen Runtime, whose verification engine proves evidence integrity rather than provider-negative business meaning.

### Provider neutrality and frozen Runtime

Certified:

- No Stripe SDK package or Stripe type exists in `PointOfSale.Core.Payments.Runtime`.
- Runtime contains no Stripe status, event, credential, client-secret, transport, persistence, API, or frontend branch.
- `PaymentRuntimeOrchestrator` owns the only canonical stage order.
- `StateTransitionRuntime`, `PaymentTransactionRuntime`, and `PaymentSettlementRuntime` retain their frozen authorities.
- Runtime is immutable, deterministic for identical inputs, fail-closed, and storage-free.
- Stripe adapters create evidence, not decisions, transactions, settlements, or SQL lifecycle state.

No Runtime redesign or Runtime modification is authorized by this audit.

### Authority boundaries and single writer

Certified for the active PaymentIntent and financial webhook flow:

- PostgreSQL remains internal payment truth.
- `PaymentLifecycleService` remains the sole durable order-payment lifecycle writer.
- Runtime produces canonical in-memory artifacts and does not compete as a SQL writer.
- Stripe provider output remains external evidence.
- Create persistence occurs only after the application validates the exact expected client-confirmation Runtime shape.
- Financial webhook mutation occurs only when an event classified for Runtime admission produces `RuntimeOverallStatus.Succeeded`.
- Duplicate webhook execution is fenced again by the webhook-log database uniqueness boundary inside the serializable lifecycle transaction.

### Architecture conclusion

**Architecture certified for dependency direction, provider neutrality, canonical execution order, and single-writer authority.**

The complete Stripe provider is not certified because deployable replay persistence, configuration governance, recovery, and the declared webhook surface do not yet match that architecture.

## Implementation Review

### Dependency injection and lifetime

- Runtime orchestrator and the stateless webhook adapter are safe singleton services.
- EF-backed application services, tenant credentials, PaymentIntent adapter, Runtime bridge, and webhook integration are scoped.
- Controllers do not directly use Stripe SDK services.
- `StripePaymentIntentSdkClient` remains unused production source.
- Advanced operations are singleton/global-client services with no POS Admin or Storefront production caller. They are excluded from the reference-provider surface.

The mutable scoped `StripeRuntimeCredentialAccessor` assumes one Stripe operation at a time per scope. Current entry points follow that assumption, but the invariant is not enforced by the type or a concurrency test. It should not be copied into a provider template without an explicit serial-use boundary or immutable operation context.

### Async, cancellation, and resource lifetime

- Reviewed EF and Stripe calls propagate cancellation tokens.
- Transactions are asynchronously disposed and explicitly committed or rolled back.
- No sync-over-async was found in the bounded context.
- Webhook failures return non-2xx results so Stripe can retry.
- The configured named `HttpClient` does not govern the tenant-scoped live client, so the documented timeout/network lifecycle is not an effective production guarantee.

### Exception handling and immutability

- Stripe exceptions are translated at adapter/application boundaries.
- Signature failures fail before financial mutation.
- Runtime exceptions become explicit failed `RuntimeResult` artifacts.
- Runtime models and bridge metadata defensively copy collections.
- Provider-created/local-incomplete recovery uses deterministic idempotency and provider-identity mismatch checks.

### Feature flags and configuration

Not certified:

- `StripeOptions.FeatureFlags.PaymentIntentsEnabled`, `WebhooksEnabled`, `CaptureEnabled`, `RefundsEnabled`, `CheckoutSessionsEnabled`, and `CustomerPortalEnabled` are declarations only.
- Global `Enabled`/environment flags influence startup validation and health but do not gate tenant-scoped create or webhook execution.
- Tenant configuration is the effective execution authority, but it receives weaker validation than the global configuration.
- Global health can be red while tenant-scoped Stripe execution remains reachable.

### Implementation conclusion

**Implementation certification withheld.** The migration-discovery and encrypted-recovery defects are implementation blockers. Configuration and network controls also need one effective authority path before this can become the permanent template.

## Security Review

Certified controls:

- Tenant credentials are stored encrypted and decrypted server-side.
- Client secrets are encrypted at rest and are not persisted in Zustand/local storage.
- Storefront access is bound to tenant and checkout/customer authorization.
- Webhook request bodies are bounded and rate-limited.
- Stripe signature validation uses official `EventUtility.ConstructEvent` over the raw body.
- Webhook signature material is persisted only as a SHA-256 digest.
- Persisted webhook payloads are bounded and redacted.
- Provider event identity and local webhook logs provide deterministic idempotency fences.
- No Stripe secret or client-secret value was found in reviewed structured log templates or health data.

Certification blockers:

- Durable replay cannot be assumed on a normally migrated deployment because the replay migration is undiscoverable.
- Actual tenant keys are not checked for test/live prefix consistency with `TenantPaymentMethod.IsSandbox`.
- A soft-deleted but enabled Stripe method can still supply webhook verification authority through `StripePaymentService.GetStripeConfigurationAsync`.
- The unauthenticated webhook path performs bounded database tenant resolution before signature verification. This is structurally necessary for tenant-scoped webhook secrets, but it requires deployed rate/load evidence and must remain fail-closed.
- A retention period for minimized webhook audit and replay rows is not implemented or configured.

**Security certification withheld** until deployable replay and effective tenant credential/configuration authority are corrected. No credential disclosure was found.

## Operational Review

### Health and readiness

Strengths:

- `stripe_foundation` validates official SDK presence and global configuration without making provider calls.
- `stripe_operational_readiness` probes database connectivity, replay and webhook tables, tenant configuration presence, SDK version, and critical DI registrations.
- Readiness fails if the replay migration is absent, which is correct behavior.

Gaps:

- The live route does not honor the same global enablement and capability settings that health reports.
- The health check proves an enabled tenant configuration exists but does not decrypt it or validate its key mode.
- Route reachability, actual Stripe API access, and signed provider delivery remain deployment checks.

### Metrics, alerts, and runbooks

- Required counters and the `PointOfSale.Payments.Stripe` meter are registered and exported.
- `stripe_runtime_decision_total` is labeled with application lifecycle strings rather than the actual Runtime decision/status.
- `tenant_bucket` is a stable eight-character tenant identifier prefix, not a fixed-cardinality bucket.
- No create, webhook, replay, provider-call, or Runtime latency histogram implements the documented performance targets.
- Alert matrices and runbooks exist as documentation, but deployed alert rules and firing evidence are absent.
- The documented redirect/client-secret recovery drill is not executable because of the ciphertext-as-JSON defect.

**Operations certification withheld.** Source runbooks and counters are useful but do not compensate for broken recovery, mismatched health/execution controls, and absent deployed operational validation.

## Migration Review

### Completed convergence

- POS Admin and Storefront create/recovery callers converge through `IStripePaymentService` and `IStripeRuntimeApplicationService`.
- Direct PaymentIntent SDK calls were removed from controllers, checkout services, and redirect recovery.
- Financial webhook persistence converges through `PaymentLifecycleService`.
- Client-secret persistence uses `ClientSecretEncrypted`.
- Charge refund tenant resolution prefers nested PaymentIntent identity and falls back to Charge identity.
- Replay lookup is bounded to indexed provider-event identity rather than loading tenant history.

### Blocking migration defect

`PointOfSale.Infrastructure/Migrations/20260712160000_StripeContractClosure2WebhookReplay.cs` is a partial `Migration` class without the metadata normally supplied by a `.Designer.cs` file and without explicit attributes. `ApplicationDbContextModelSnapshot` also lacks the replay entity.

Executable verification:

```text
dotnet ef migrations list --project PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj \
  --startup-project PointOfSale.API/PointOfSale.API.csproj \
  --context ApplicationDbContext --no-build --no-connect
```

The command succeeded but did not list `20260712160000_StripeContractClosure2WebhookReplay`.

Consequences on a fresh or normally upgraded database:

- `StripeWebhookReplayRecords` is not created by EF migrations.
- operational readiness is unhealthy;
- admitted webhook replay lookup fails;
- the claimed durable Runtime replay boundary is unavailable.

**Migration certification withheld.** This is a source/deployment packaging defect, not missing live Stripe evidence.

## Extensibility Review

The frozen Runtime remains suitable for every requested provider class. The current Stripe implementation is not yet the canonical provider template.

| Provider/workflow | Frozen Runtime suitability | Stripe-template decision |
|---|---|---|
| Paymob | High. Redirect/callback evidence maps naturally to canonical actions and evidence. | Do not copy the current configuration, migration, mutable credential handoff, or recovery implementation. |
| Fawry | High. Reference payment and signed callback/status evidence fit the Runtime. | Use the authority direction, but require deployable replay and explicit credential/HMAC mode validation. |
| Manual Payment | High for receipt/review evidence and canonical decisions. | Share contracts and lifecycle authority; do not copy Stripe SDK/client-confirmation mechanics. |
| Cash On Delivery | High for delivery/collection evidence and COD settlement intent. | Share the Runtime and SQL single writer; fulfillment/collection authority must remain distinct from Stripe webhooks. |
| Future Marketplace integration | Potentially suitable, but explicitly outside this audit. | No reference decision is made for split funds, connected accounts, allocations, transfers, or CommerceHub direct Stripe paths. |

Reusable truths already certified:

- provider facts translate to canonical evidence;
- Runtime remains provider-neutral;
- Runtime output gates continuation where financial execution is claimed;
- SQL lifecycle service is the only internal status writer;
- idempotency and replay are durable application concerns;
- provider ambiguity fails closed to retry, recovery, quarantine, or reconciliation.

Not reusable as a permanent template:

- an undiscoverable provider replay migration;
- split global/tenant configuration governance;
- capability flags that do not gate execution;
- mutable scoped credential/artifact exchange;
- provider recovery that reads encrypted storage directly;
- unsupported webhook events admitted without a lifecycle owner;
- global-credential advanced-operation registration in a tenant-scoped provider model.

## Production Review

### Known risks and blockers

Source blockers:

1. Deployable durable replay storage is missing from EF migration discovery.
2. Background client-secret recovery cannot read encrypted tenant configuration.
3. Global enablement/capability/environment/network settings do not control the live tenant path.
4. Tenant key mode and deleted configuration authority are not fully validated.
5. The declared paid Checkout Session webhook path has no SQL lifecycle handler.

Deployment-time prerequisites, not architecture blockers:

- provision real tenant-scoped Stripe test and live credentials;
- register and verify the signed webhook endpoint in Stripe Workbench;
- apply and verify the corrected replay migration;
- execute success, decline, cancel, refund, duplicate, concurrent-delivery, outage, and recovery drills;
- execute two-tenant isolation and key/webhook-secret rotation drills;
- prove alert firing, runbook recovery, backlog behavior, and measured latency;
- review restricted-key permissions and Stripe API-version compatibility.

### Go / No-Go

- Software architecture direction: **GO**.
- Frozen Runtime use: **GO**.
- Stripe implementation freeze/reference-provider designation: **NO-GO**.
- Production traffic approval: **NO-GO**.

The production no-go is based on current source defects plus remaining deployment validation. It is not based solely on the absence of live sandbox evidence.

## Legacy Audit

Legacy audit completed: **Yes**.

Remaining legacy, transitional, or excluded surfaces:

1. `StripePaymentService` remains a large compatibility/application service that owns configuration, initial signature parsing, webhook logs, event dispatch, recovery coordination, lifecycle mapping, and response construction.
2. Webhook signatures are parsed once for application routing and verified again by the canonical evidence adapter.
3. `StripePaymentIntentSdkClient` is unused production code.
4. `StripeAdvancedOperationsAdapter` is registered but has no POS Admin or Storefront command caller and uses the global client model.
5. `PaymentIntent.RedirectUrl` still contains a backward-compatible legacy client-secret fallback.
6. `checkout.session.completed` is mapped and admitted despite no bounded-context Checkout Session producer or lifecycle handler.
7. Earlier Stripe certification documents record historical phase outcomes and must not override current code or this decision.
8. CommerceHub direct Stripe payment/refund code is explicitly outside this audit and is not used as a reason to withhold this bounded-context decision.

## Risk Register

| ID | Severity | Current evidence | Effect | Required disposition |
|---|---:|---|---|---|
| STR-FINAL-001 | High | Replay migration absent from EF migration list and model snapshot | Fresh/upgraded deployments lack durable replay storage | Generate or explicitly annotate a discoverable migration, update snapshot, and test migration discovery/application |
| STR-FINAL-002 | High | Recovery worker deserializes `ConfigEncrypted` directly | Missing-client-secret background recovery always fails for real encrypted configuration | Remove duplicate pre-read or decrypt through the same application credential boundary; add executable recovery test |
| STR-FINAL-003 | High | Live create/webhook paths do not consume global/capability flags | Kill switches and environment governance do not control traffic | Establish one effective feature/config authority and test disabled/enabled modes |
| STR-FINAL-004 | High | Tenant key prefixes and `IsSandbox` are not correlated; deleted config can verify webhooks | Mode drift or withdrawn configuration can remain operational authority | Validate tenant key/secret mode and exclude deleted rows at every credential lookup |
| STR-FINAL-005 | Medium | Tenant client bypasses named configured `HttpClient` | Configured timeout/retry/telemetry is not guaranteed on live calls | Inject a tenant-capable client using the governed transport or prove equivalent settings |
| STR-FINAL-006 | Medium | Paid Checkout Session admitted but unhandled by SQL lifecycle switch | Event can be marked processed without local lifecycle mutation if capability is introduced | Remove from admitted surface or implement and certify the lifecycle path before enabling |
| STR-FINAL-007 | Medium | Runtime metric contains application outcomes; tenant tag is not a bounded bucket; no latency histograms | Misleading or high-cardinality operations data | Emit truthful Runtime/application metrics with bounded tags and latency/backlog measures |
| STR-FINAL-008 | Medium | Mutable scoped credential/artifact accessor | Concurrent operations in one scope can cross-contaminate artifacts | Enforce serial scope use or replace with immutable per-operation context before template reuse |
| STR-FINAL-009 | Medium | No retention enforcement for minimized audit/replay rows | Operational data growth and undefined retention | Define and implement retention consistent with reconciliation/legal requirements |
| STR-FINAL-010 | External prerequisite | No live signed Stripe drill or measured performance evidence | Deployment behavior is unproven | Complete the live validation matrix after source blockers close; not an architecture blocker |

## Certification Evidence

Executed during this independent audit:

| Evidence | Result |
|---|---:|
| `dotnet test ... --filter "FullyQualifiedName~Stripe"` | **102 passed, 0 failed, 0 skipped** |
| `dotnet test ... --filter "FullyQualifiedName~PaymentRuntime"` | **93 passed, 0 failed, 0 skipped** |
| `dotnet ef migrations list ... --no-connect` | Command passed; replay migration **not listed** |
| `npm.cmd run build` in `PointOfSale.Storefront` | **Passed**; warnings only |
| Complete unfiltered .NET test suite | Attempted with `--no-build`; no final result before the 10-minute timeout |

The focused tests prove component mapping, Runtime isolation, authority gating, replay query shape, redaction, refund identity, and source-level readiness structure. They do not prove:

- EF discovery/application of the replay migration;
- background recovery using encrypted tenant configuration;
- effective feature-flag or global-disable behavior;
- tenant test/live credential-mode enforcement;
- live Stripe API/webhook behavior;
- deployed alerts, retention, or performance.

Certification evidence is sufficient for the Runtime certification and for this withholding decision. It is not sufficient for permanent reference-provider or production certification.

## Reference Provider Decision

**Stripe is not certified as the permanent reference provider in the current codebase.**

The dependency direction and authority model are suitable as the target pattern, but a permanent reference must also demonstrate deployable replay storage, one effective configuration/feature authority, executable recovery, and a truthful supported-event surface. Copying the current implementation would replicate provider-local defects into Paymob and Fawry.

Ready to begin Paymob:

- Against the frozen provider-neutral Runtime contracts: **Yes, design work can proceed.**
- By cloning Stripe as the certified permanent provider template: **No.** Close and recertify STR-FINAL-001 through STR-FINAL-004 first.

## Freeze Decision

### Payment Runtime

**FROZEN: YES.**

No provider leakage, authority overlap, state-machine defect, or architectural drift was found that justifies a Runtime change. Future changes to Runtime objects, pipeline order, state transitions, determinism, replay semantics, transaction mapping, settlement mapping, or failure behavior still require separate recertification.

### Stripe architecture direction

**FROZEN: YES** for the rule:

```text
STRIPE ADAPTS TO THE PAYMENT RUNTIME
THE PAYMENT RUNTIME NEVER ADAPTS TO STRIPE
POSTGRESQL LIFECYCLE PERSISTENCE HAS ONE WRITER
```

### Stripe implementation

**FROZEN AS REFERENCE: NO.** Provider-local implementation and operational changes remain required.

## Final Certification Decision

**FINAL DECISION: WITHHOLD CERTIFICATION.**

| Success question | Answer |
|---|---|
| Architecture certified? | **Yes** for provider-neutral dependency direction, canonical Runtime execution, and SQL single-writer ownership. |
| Implementation certified? | **No.** Replay migration, recovery, and configuration-control defects remain. |
| Security certified? | **No.** Durable replay deployment and tenant credential/deleted-config authority are incomplete. |
| Operations certified? | **No.** Recovery, effective gating, telemetry, retention, and live operational validation remain incomplete. |
| Migration certified? | **No.** The replay migration is not discoverable by EF Core. |
| Legacy audit completed? | **Yes.** |
| Reference provider certified? | **No.** |
| Provider-neutral Runtime preserved? | **Yes.** |
| Architecture frozen? | **Yes for the Payment Runtime and provider-to-Runtime direction; no for the Stripe implementation.** |
| Production-ready? | **No.** |
| Ready to begin Paymob? | **Runtime-targeted design: yes. Stripe-template implementation: no.** |
| Final recommendation? | Correct the four High source defects, add executable migration/recovery/gating tests, rerun focused and regression suites, then perform live Stripe validation as a deployment prerequisite and repeat STRIPE-FINAL. |

## Context Update Decision

No new permanent cross-provider architecture truth was discovered. The findings enforce existing truths: provider evidence is subordinate to canonical execution and SQL lifecycle authority, replay must be durable, configuration authority must fail closed, and Runtime remains provider-neutral. Therefore `.ai-context` was not modified.

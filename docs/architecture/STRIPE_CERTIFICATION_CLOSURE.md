# Stripe Certification Closure

Date: 2026-07-13  
Scope: POS Admin + Storefront Stripe bounded context  
Authority reviewed: `STRIPE_FINAL.md`  
Runtime status: frozen and unchanged

## Executive Summary

The implementation root causes behind both Critical findings and the actionable High findings have been corrected outside the frozen Payment Runtime. Stripe creation now enforces the one valid Runtime result shape for a client-confirmation handoff. Financial webhook evidence must complete the canonical Runtime before SQL lifecycle mutation. Provider-negative terminal events are explicitly excluded from financial Runtime admission and remain subordinate evidence interpreted by the single SQL lifecycle writer.

Refund tenant resolution, replay lookup complexity, webhook audit-data minimization, readiness dependency checks, and executable certification evidence were also corrected. The focused Stripe suite is green at **102 passed, 0 failed**. The frozen Runtime suite remains green at **93 passed, 0 failed**.

One board High item, STR-F-007, is not an implementation defect that can be closed in a source-only task. It is an external evidence requirement. Live Stripe test-mode, migration, recovery, alert, and performance evidence is still required before production certification. Therefore this closure is ready for a new architecture board run, but it does not itself certify production.

No Runtime, Runtime contract, adapter contract, policy engine, or application-layer architecture was redesigned.

## Finding Classification

| ID | Board severity | Closure classification | Disposition |
|---|---:|---|---|
| STR-F-001 | Critical | Critical | **Implemented and closed.** Required Runtime outcomes are now enforced before create continuation or financial webhook lifecycle mutation. |
| STR-F-002 | Critical | Critical | **Implemented and closed.** Financial success/refund evidence has an explicit canonical Runtime path; provider-negative terminal evidence is not falsely admitted as a financial Runtime decision. |
| STR-F-003 | High | High | **Implemented and closed.** Charge ingress prefers nested `payment_intent` identity and falls back to the charge/provider-payment identity. |
| STR-F-004 | High | High | **Implemented and closed.** Replay admission performs an indexed identity existence check and no longer materializes tenant history. |
| STR-F-005 | High | High | **Implemented and closed for the reported sensitive-data defect.** Persisted payloads are redacted/bounded and signatures are one-way digests. A retention period for minimized audit rows remains an operational policy recommendation. |
| STR-F-006 | High | High | **Implemented and closed.** Readiness now exercises database connectivity, required tables, effective tenant configuration, DI boundaries, and the certified SDK version. |
| STR-F-007 | High | Needs Clarification | **Not source-closable.** The finding is a genuine external certification-evidence gap, not an implementation root cause. Live environment evidence remains mandatory. |
| STR-F-008 | High | High | **Implemented and closed.** Stale certification assertions and provider-incompatible test harnesses were repaired; the focused slice is green. |
| STR-F-009 | Medium | Medium | Deferred by mission scope. Named-client timeout conformance remains for a later closure. |
| STR-F-010 | Medium | Medium | Deferred by mission scope. Runtime/application metric semantics and cardinality remain an operations-certification item. |
| STR-F-011 | Medium | Medium | Deferred by mission scope. Scoped mutable accessor concurrency requires an explicit executable invariant. |
| STR-F-012 | Medium | Medium | Deferred by mission scope. Advanced operations remain outside the certified provider-template surface. |
| STR-F-013 | Medium | Medium | Deferred by mission scope. Pre-signature tenant lookup load remains bounded by monitoring/rate-control work. |
| STR-F-014 | Medium | Medium | Deferred and outside this bounded context. CommerceHub Stripe convergence remains separately tracked. |

No board finding was dismissed wholesale as a False Positive. STR-F-002 required semantic clarification, and several failures contributing to STR-F-008 were false product-defect signals caused by test-provider incompatibilities. The underlying certification-evidence finding was nevertheless genuine.

## Root Cause Groups

### RCG-A — Runtime outcome interpretation and admission mismatch

Findings: STR-F-001, STR-F-002.

The application treated Runtime execution as observational. Creation also assumed that only overall Runtime success was valid, although the frozen Runtime deliberately rejects transaction production for the non-financial `Pending -> AwaitingCustomer` handoff. Webhook translation admitted provider-negative events into a verification path whose authentic evidence necessarily produces `Approved`, then independently selected a negative SQL lifecycle outcome.

Closure:

- Creation accepts only the exact, frozen non-financial result shape: admitted, verified, `Approved`, transition applied to `AwaitingCustomer`, then transaction-stage `TRANSACTION_FAILED` with no financial artifacts.
- Any other create Runtime result fails closed with `STRIPE_RUNTIME_OUTCOME_REJECTED`.
- Successful PaymentIntent/Checkout evidence enters Runtime from `Approved`, permitting `Approved -> Completed` and canonical Sale transaction/settlement production.
- Refund evidence enters Runtime from `RefundPending`, permitting `RefundPending -> Refunded` and canonical Refund transaction/settlement production.
- Failed/cancelled provider events remain terminal application evidence but are explicitly not admitted as canonical financial Runtime decisions.
- A webhook marked for Runtime execution cannot reach the SQL lifecycle writer unless Runtime overall status is `Succeeded`.

### RCG-B — Stripe object identity precedence

Finding: STR-F-003.

Charge objects were treated as though `object.id` were a PaymentIntent identifier. Standard refund events therefore attempted `ch_*` lookup through the `ProviderIntentId` field.

Closure: nested `payment_intent` (`pi_*`) is resolved first; the charge object ID (`ch_*`) is then used as a `ProviderPaymentId` fallback. PaymentIntent-shaped events continue to resolve their own object ID.

### RCG-C — Tenant-history materialization used as replay evidence

Finding: STR-F-004.

The replay loader materialized and sorted every Stripe replay row for a tenant even though the durable event identity is tenant + provider + provider event ID.

Closure: the loader performs a bounded indexed `AnyAsync` check for that identity and supplies no unrelated historical actions to Runtime ordering logic. Durable uniqueness remains the concurrency fence.

### RCG-D — Audit persistence trusted provider input

Finding: STR-F-005.

Verified raw JSON and the complete signature header were copied into durable audit rows.

Closure: the raw body remains available only in memory for signature validation and translation. Durable rows receive the shared bounded/redacted representation. Signature material is stored only as a SHA-256 digest. Success, retry, and failure persistence paths use the same transformation.

### RCG-E — Readiness tested registrations instead of dependencies

Finding: STR-F-006.

The health check could report green when services were registered even if the database, required tables, tenant configuration, or certified SDK were unusable.

Closure: readiness now calls `CanConnectAsync`, queries the replay and webhook-log tables, proves an enabled non-deleted tenant Stripe method with encrypted configuration exists, verifies required services, and validates the certified Stripe SDK version. Exceptions expose only safe exception type data.

### RCG-F — Certification tests encoded historical text and incompatible providers

Finding: STR-F-008.

Certification guards depended on historical `.ai-context` marker text and stale source shapes. SQLite attempted to create PostgreSQL-only constraints/`xmin` columns, the in-memory provider escalated its expected transaction warning, an invalid Runtime mock returned null, and a mocked outbox did not stage the row now required by the atomic lifecycle assertion.

Closure: guards now assert current architecture boundaries and certification state. Test-only database models adapt PostgreSQL-specific metadata for SQLite, the expected in-memory transaction limitation is explicit, Runtime failure is represented by a real exception outcome, and the outbox double stages a durable row inside the test transaction.

### RCG-G — External production evidence gap

Finding: STR-F-007.

Live credentials, Stripe delivery behavior, deployed migrations, real alerts, and measured latency are external state. No source change can manufacture valid evidence. This group remains open for the production certification exercise.

## Implemented Fixes

### Runtime authority enforcement

- Added exact create-handoff Runtime outcome validation.
- Made unexpected create Runtime shapes fail closed before provider artifacts are accepted by the application.
- Required successful canonical Runtime completion for financially admitted webhooks before lifecycle mutation.
- Preserved the SQL `PaymentLifecycleService` as the sole durable status/inventory/accounting writer.

### Truthful webhook outcome admission

- Restricted financial Runtime admission to successful PaymentIntent, paid Checkout Session, and refund evidence.
- Kept failed/cancelled events in the application lifecycle path with the explicit reason `TERMINAL_NEGATIVE_APPLICATION_LIFECYCLE`.
- Supplied canonical starting states for success and refund transitions without changing Runtime rules.

### Refund identity resolution

- Added realistic Charge refund resolution through nested PaymentIntent identity.
- Added provider-payment fallback through the Charge ID.

### Bounded replay loading

- Replaced tenant-wide load/order/materialization with an indexed duplicate-identity query.
- Preserved the database unique key as the durable race/concurrency boundary.

### Webhook data protection

- Applied bounded redaction before every Stripe webhook audit write.
- Replaced stored signature headers with irreversible digests.
- Kept raw material transient for validation only.

### Dependency-backed readiness

- Added database connectivity and required-table probes.
- Added effective tenant Stripe enablement/configuration proof.
- Added exact certified SDK compatibility and retained required DI boundary checks.

### Executable certification

- Added ten focused closure regressions covering Runtime outcome enforcement, negative admission, financial success/refund execution, bounded replay, Charge refund identity, redaction, and readiness behavior/source boundaries.
- Repaired stale architecture assertions and provider-specific test harness behavior.

## Rejected Findings

No finding was rejected as an opinion or cosmetic concern.

The following interpretations were rejected while preserving the underlying finding:

- **STR-F-002:** SQL mapping of provider-negative evidence is not itself an authority violation. The defect was admitting that evidence into a canonical financial Runtime path that could only decide `Approved`, then ignoring the mismatch. Explicit non-admission plus the existing single SQL lifecycle writer is the correct closure under the frozen Runtime.
- **STR-F-008 test failures:** SQLite PostgreSQL syntax failures, the in-memory transaction warning, and a null loose mock were not evidence of Stripe product defects. They were valid evidence that certification was not reproducible, so the finding remained High until the harness was corrected.

## Deferred Findings

STR-F-009 through STR-F-014 remain deferred because the mission authorizes implementation only for Critical and High root causes. They were not silently closed or reclassified as harmless.

STR-F-007 remains open pending external execution. At minimum the next production evidence package must include:

- real tenant-scoped Stripe test credentials and signature verification;
- success, failure, cancel, refund, duplicate, and concurrent delivery;
- provider-created/local-failed recovery and secret rotation;
- deployed migration/table verification;
- alert firing and recovery runbook execution;
- measured webhook latency, replay growth, and load behavior.

## Regression Evidence

Executed on 2026-07-13:

| Command/filter | Result |
|---|---:|
| `FullyQualifiedName~Stripe` | **102 passed, 0 failed, 0 skipped** |
| `FullyQualifiedName~PaymentRuntime` | **93 passed, 0 failed, 0 skipped** |
| New `StripeCertificationClosureTests` | **10 passed, 0 failed** |
| Previously failing tenant-isolation and Stripe lifecycle harness cases | **4 passed, 0 failed** |

The projects compiled successfully as part of the test build. No frozen Runtime source was modified.

Regression coverage now proves:

- the precise non-financial create result accepted by the application;
- rejection of any unexpected create Runtime result;
- non-admission of provider-negative terminal evidence;
- successful full Runtime transaction/settlement for payment success and refund;
- failure-closed webhook behavior when required Runtime execution does not succeed;
- replay query behavior independent of tenant history size;
- realistic Charge refund tenant resolution;
- durable payload redaction and signature hashing;
- dependency-backed readiness checks;
- frozen Runtime behavior remains unchanged.

## Remaining Risks

| Risk | Status | Required disposition |
|---|---|---|
| Live Stripe behavior and deployed operational evidence | Open; production blocker | Close STR-F-007 in the target environment. |
| Retention duration for minimized webhook audit rows | Open operational policy | Set and enforce a retention schedule consistent with reconciliation/legal needs. Raw payload/signature retention is already eliminated. |
| Named HTTP timeout conformance | Deferred Medium | Resolve STR-F-009 before operations certification. |
| Metric meaning/cardinality | Deferred Medium | Resolve STR-F-010 before operations certification. |
| Scoped accessor concurrency | Deferred Medium | Resolve STR-F-011 before copying the pattern into concurrent scoped workflows. |
| Advanced operations | Outside certified surface | Keep STR-F-012 excluded from the provider template. |
| Pre-signature lookup load | Deferred Medium | Monitor, rate-limit, and load-test under STR-F-013. |
| CommerceHub direct Stripe paths | Adjacent scope | Track separately under STR-F-014. |

The readiness check proves database/table/configuration dependencies from inside the application. It does not prove public route reachability through an ingress proxy or successful communication with Stripe; those belong to STR-F-007 live evidence.

## Certification Recommendation

### Closure answers

| Question | Answer |
|---|---|
| Critical findings closed? | **Yes.** STR-F-001 and STR-F-002 implementation root causes are closed. |
| High findings closed? | **All source-closable High defects are closed.** STR-F-003 through STR-F-006 and STR-F-008 are closed. STR-F-007 remains an external evidence blocker. |
| False positives documented? | **Yes.** No board item was discarded; false product signals inside STR-F-008 and the STR-F-002 semantic clarification are documented. |
| Root causes eliminated? | **Yes for implemented RCG-A through RCG-F.** RCG-G requires live external execution. |
| Regression passed? | **Yes.** Stripe 102/102 and frozen Runtime 93/93. |
| Ready to rerun `STRIPE_FINAL`? | **Yes for architecture/source recertification.** Production certification must still withhold approval until STR-F-007 evidence exists. |

### Freeze and reference-provider recommendation

- Payment Runtime: **remain frozen**.
- Provider-neutral Runtime: **preserved**.
- Stripe implementation: **eligible for final source architecture recertification**.
- Stripe as permanent reference provider: **do not freeze until the board confirms these closures and STR-F-007 is evidenced**.
- Paymob start from the Stripe template: **wait for that recertification decision**.
- Production-ready: **not yet certified**, solely because required live evidence and deferred operations conditions remain.

## Context Update Decision

No new permanent architecture truth was discovered. The fixes enforce already documented truths: frozen Runtime semantics, provider-neutral evidence processing, and the SQL lifecycle single-writer boundary. Therefore no `.ai-context` file was modified by this closure.

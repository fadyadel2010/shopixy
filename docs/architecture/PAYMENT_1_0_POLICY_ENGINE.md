# PAYMENT-1.0 Payment Policy Engine Architecture Certification

Date: 2026-07-10
Scope: Provider-neutral enterprise Payment Policy Engine architecture
Status: CERTIFIED ARCHITECTURE

Runtime modified: No.

Implementation modified: No.

`.ai-context` updated: Yes. PAYMENT-1.0 introduces a permanent Payment Policy ownership boundary: Policy decides business eligibility, routing, risk, compliance, manual-review, refund, capture, void, retry, recovery, and settlement admissibility; Runtime executes; Adapters translate and expose capabilities.

## Executive Summary

PAYMENT-1.0 defines the permanent Payment Policy Engine for all payment workflows.

The Payment Policy Engine is the provider-neutral authority for payment business decisions. It determines whether a payment method, provider, action, lifecycle operation, refund, capture, void, retry, recovery, settlement, manual review, COD, wallet, QR, reference payment, marketplace, affiliate, tenant, country, region, currency, amount, time, risk, fraud, customer, product, or order condition is allowed, denied, quarantined, redirected, retried, or sent to manual review.

The Policy Engine does not execute provider calls, mutate payment state directly, create payment transactions, call provider APIs, or serialize provider wire contracts. Those responsibilities remain with the certified Runtime and Adapter boundaries:

```text
Policy Engine decides.
Payment Runtime executes.
Provider Adapter translates.
```

Certification decision: **PAYMENT-1.0 is certified as the enterprise Payment Policy Engine architecture.**

## Policy Architecture

The Policy Engine is a deterministic decision boundary between payment context and payment execution.

It consumes current platform authority, provider-neutral capabilities, constraints, risk evidence, merchant/tenant/customer/order context, historical decisions, and operation intent. It returns a policy decision with explicit reason codes, audit entries, version evidence, and permitted execution path.

Canonical flow:

```text
Payment Intent / Operation Intent
  -> Policy Context Assembly
  -> Eligibility Policy
  -> Routing Policy
  -> Risk / Fraud Policy
  -> Compliance Policy
  -> Operation Policy
  -> Manual Review / Recovery Policy
  -> Execution Decision
  -> Payment Runtime
  -> Provider Adapter
```

The Policy Engine is provider-neutral. It may know abstract method families, provider capability facts, tenant entitlements, country/currency support, risk scores, and constraints. It must not know Stripe, Paymob, Fawry, Manual, COD, or future provider implementation details.

## Responsibilities

Only Policy decides:

| Decision | Policy Responsibility |
|---|---|
| Payment Method Eligibility | Whether a method can be offered or used for the current tenant, merchant, customer, order, amount, country, region, currency, risk, product, and time context. |
| Provider Eligibility | Whether a provider is eligible after entitlement, capabilities, constraints, geography, currency, amount, risk, availability, and operational restrictions. |
| Routing Eligibility | Which eligible provider or provider class may be attempted, preferred, blocked, or used as fallback. |
| Refund Eligibility | Whether a payment is refundable, by whom, through which operation, and under what amount/currency/reason constraints. |
| Partial Refund Eligibility | Whether partial refund is allowed and what remaining refundable balance, count, window, and minimum/maximum amount rules apply. |
| Capture Eligibility | Whether an authorized payment may be captured now, partially captured, blocked, or sent to review. |
| Void Eligibility | Whether a pending, unpaid, authorized, or uncaptured obligation can be voided or cancelled instead of refunded. |
| Retry Eligibility | Whether a failed, pending, ambiguous, expired, or superseded attempt may be retried safely. |
| Recovery Eligibility | Whether recovery may query provider/manual/COD status, rebind evidence, retry execution, quarantine, or require reconciliation. |
| Settlement Eligibility | Whether confirmed, captured, collected, partially refunded, or adjusted value may be settled or requires reconciliation. |
| Manual Approval Requirement | Whether a payment, refund, settlement, COD collection, suspicious evidence, or high-risk operation requires human review. |
| COD Eligibility | Whether COD can be offered or collected for the order, customer, product, geography, shipping method, merchant, and risk context. |
| Wallet Eligibility | Whether wallet actions are allowed for the current method/provider/account/country/currency/customer/device context. |
| QR Eligibility | Whether QR action issuance is allowed and under what expiry, amount, and evidence requirements. |
| Reference Payment Eligibility | Whether reference-code payment may be issued, renewed, expired, retried, or blocked. |
| Merchant Restrictions | Merchant-level allow/deny rules, entitlements, risk limits, settlement posture, and operational blocks. |
| Country / Region Restrictions | Geographic allow/deny, sanctions, local payment rules, provider availability, and regional risk restrictions. |
| Currency Restrictions | Currency support, tenant base-currency rules, provider currency support, settlement currency policy, and mismatch handling. |
| Amount Restrictions | Minimum/maximum amounts, thresholds, cumulative limits, velocity limits, capture/refund balances, and high-value review gates. |
| Time Restrictions | Expiry windows, business-hour rules, blackout windows, refund/capture/void windows, and settlement timing rules. |
| Risk / Fraud Restrictions | Risk score thresholds, velocity, suspicious evidence, customer behavior, provider mismatch, device signals, and quarantine gates. |
| Customer Restrictions | Customer eligibility, authentication posture, blocklists, prior disputes, COD refusal history, and saved-method restrictions. |
| Product Restrictions | Product/category restrictions, hazardous/regulated product constraints, digital/physical goods rules, and COD exclusions. |
| Order Restrictions | Order state, payable snapshot, shipping state, fulfillment dependency, split-order/marketplace rules, and immutable amount/currency constraints. |
| Tenant Restrictions | Tenant entitlements, provider enablement, feature flags, compliance posture, operating countries, and financial readiness. |

Policy is the only owner of these business decisions. Runtime may request a decision and enforce the returned result. Adapters may expose capability facts and evidence. Neither Runtime nor Adapters may invent, skip, or override policy.

## Runtime Boundary

Runtime never decides:

- Business rules.
- Merchant policy.
- Refund policy.
- Provider selection policy.
- Country rules.
- Region rules.
- Currency rules.
- Amount rules.
- Time rules.
- Risk rules.
- Fraud rules.
- Customer rules.
- Product rules.
- Order rules.
- Compliance rules.

Runtime responsibilities remain:

- Create or replay PaymentSession through certified runtime admission.
- Assemble or request PolicyContext.
- Call Policy before issuing actions or executing operations that require business permission.
- Enforce allowed/denied/manual-review/quarantine/retry/recovery decisions.
- Execute state-machine transitions after verified evidence and admitted policy.
- Create PaymentActions, PaymentDecisions, PaymentTransactions, Refunds, Captures, Voids, Settlements, Recoveries, AuditRecords, and Events only through certified runtime rules.
- Preserve idempotency, replay collapse, terminal-state dominance, and SQL authority.

Runtime executes only. It must not create policy, infer policy, persist hidden business rules, select providers by business preference, bypass manual review, or silently convert provider capability into eligibility.

## Adapter Boundary

Adapters never decide:

- Eligibility.
- Refund.
- Partial refund.
- Capture.
- Void.
- Retry.
- Settlement.
- Business rules.
- Routing preference.
- Risk disposition.
- Compliance disposition.
- Manual approval requirement.

Adapters may:

- Expose provider capabilities and constraints.
- Create provider sessions or handoff artifacts under runtime command.
- Translate provider/customer/operator/fulfillment facts into ProviderEvidence candidates.
- Verify provider primitives such as signatures, HMACs, message signatures, provider references, receipt metadata, and provider account facts.
- Execute provider operations after Runtime receives an allowed policy decision.
- Query provider/manual/COD status for runtime-controlled recovery.

Adapters must never treat provider support as business eligibility. A provider may support refunds, capture, QR, wallet, reference payment, void, or settlement while Policy still denies use for this tenant, merchant, customer, order, country, currency, amount, risk score, time, product, or evidence posture.

## Policy Inputs

Policy input is a provider-neutral `PolicyContext` assembled from current authority and evidence.

Required input families:

| Input Family | Examples |
|---|---|
| Tenant | Tenant id, tenant status, entitlements, enabled methods, enabled provider classes, operating countries, compliance posture, settlement readiness. |
| Merchant | Merchant id, store/vendor context, merchant category, risk tier, restrictions, payout/settlement status, operational holds. |
| Customer | Customer id, authentication posture, account age, blocklist flags, dispute history, COD refusal history, saved-method posture, segment. |
| Order | Order id, order state, payable snapshot, order total, currency, line categories, product restrictions, split-order/marketplace evidence, refund/capture balance. |
| Currency | Payable currency, provider-supported currencies, tenant base currency, settlement currency, conversion policy if certified. |
| Country / Region | Billing, shipping, fulfillment, warehouse, marketplace, customer, merchant, provider, and compliance geography. |
| Warehouse | Warehouse id, readiness, assignment evidence, fulfillment constraints, COD/shipping method compatibility. |
| Shipping Method | Shipping method id, carrier-backed status, COD support, delivery/collection evidence requirements, shipment state. |
| Payment Method | Method family, requested method, method enablement, method constraints, method risk posture. |
| Provider | Provider class or candidate id, capability facts, constraints, availability, environment, account entitlement, provider health. |
| Capabilities | Redirect, embedded checkout, SDK, wallet, QR, reference, receipt upload, manual review, capture, refund, partial refund, void, settlement, status inquiry, recovery. |
| Constraints | Requires delivery, webhook, callback, status inquiry, provider session, receipt, HMAC, carrier evidence, manual approval, settlement report, expiry, or provider account. |
| Risk Score | Fraud score, velocity, device/session signals, customer/order/merchant risk, mismatch evidence, suspicious proof, provider ambiguity. |
| Order Total | Amount, tax, shipping, discount, COD fee, split totals, refund balance, capture balance, settlement balance. |
| Settlement Status | Unsettled, pending, settled, partially settled, over/short, reconciliation required, treasury/accounting readiness. |
| Evidence | Provider evidence, receipt evidence, carrier/driver collection evidence, reviewer evidence, refund/settlement evidence, verification results. |
| Decision History | Prior decisions, denied reasons, retries, quarantines, manual reviews, overrides where certified, recovery attempts, terminal outcomes. |

Inputs must be snapshots or current authority references. Policy must not read provider APIs directly, trust frontend state, or derive authority from cache/projection/browser state.

## Policy Outputs

Policy returns a deterministic `PolicyResult`.

Supported outputs:

| Output | Meaning |
|---|---|
| Allowed | The requested method, provider, action, operation, or transition is permitted under the supplied context. |
| Denied | The request is refused. Runtime must not execute it. |
| Manual Review | Runtime must create or route to a review action before execution or final decision. |
| Retry | Runtime may retry according to returned attempt, provider, expiry, and idempotency constraints. |
| Quarantine | Runtime must hold the operation because evidence or context is plausible but ambiguous or mismatched. |
| Redirect Provider | Runtime may use a different eligible provider class or provider candidate. |
| Alternative Method | Runtime may present or require a different payment method. |
| Alternative Provider | Runtime may route to a fallback provider after eligibility evaluation. |
| Recovery | Runtime may enter controlled recovery, status inquiry, evidence rebinding, reconciliation, or operator flow. |
| Reason Codes | Machine-stable reasons for allow/deny/review/quarantine/retry/recovery decisions. |
| Audit Entries | Policy version, rule ids, input hash, context summary, decision path, actor/system, and timestamp. |

Policy outputs must be explicit. Silent fallback is forbidden. If the engine cannot determine eligibility, it fails closed to Denied, Manual Review, Quarantine, or Recovery depending on the certified domain rule.

## Policy Domains

The Policy Engine is separated into policy domains so future rules can be added without changing Runtime.

| Domain | Responsibility |
|---|---|
| Eligibility Policy | Method, provider, tenant, merchant, customer, order, product, country, region, currency, amount, time, wallet, QR, COD, reference, and action eligibility. |
| Routing Policy | Provider ranking, fallback, alternative provider/method selection, health-aware routing, and retry routing. |
| Refund Policy | Refund and partial refund eligibility, amount windows, reason restrictions, provider/method entitlement, and review requirements. |
| Capture Policy | Authorization capture eligibility, partial capture rules, capture windows, and provider/method limits. |
| Void Policy | Pending/action-required/authorized void eligibility, cancellation windows, and void-vs-refund routing. |
| Settlement Policy | Settlement eligibility, settlement evidence requirements, COD/manual/provider settlement rules, discrepancy handling, and reconciliation gates. |
| Recovery Policy | Retry vs recovery vs quarantine vs reconciliation decisions for ambiguous provider/manual/COD/settlement states. |
| Fraud Policy | Fraud/risk score thresholds, velocity controls, suspicious evidence handling, customer/device/order risk, and quarantine. |
| Compliance Policy | Country, region, currency, product/category, merchant, sanctions, KYC/KYB, tax, retention, and regulatory restrictions. |
| Manual Review Policy | Review requirement, reviewer role, four-eyes rules where needed, override eligibility where certified, and review audit. |
| COD Policy | COD availability, customer history, geography, shipping compatibility, amount limits, collection evidence, and settlement/remittance conditions. |
| Wallet Policy | Wallet availability, account/country/currency/method support, customer eligibility, and evidence requirements. |
| QR Policy | QR availability, expiry, amount, provider/method support, fraud gates, and status evidence. |
| Reference Payment Policy | Reference-code issuance, renewal, expiry, retry, amount/currency, customer instructions, and status inquiry requirements. |
| Marketplace Policy | Split-order, vendor, merchant, commission, payout, settlement, refund, and provider-routing policy for marketplace payments. |
| Affiliate Policy | Affiliate-attributed order payment restrictions, commission-impacting refund/cancellation policy, and settlement interaction rules. |

## Policy Chain

Default policy pipeline:

```text
Context Admission
  -> Eligibility
  -> Routing
  -> Operation Policy
  -> Risk / Fraud
  -> Compliance
  -> Manual Review
  -> Recovery / Settlement if applicable
  -> Execution Decision
  -> Runtime
```

Pipeline rules:

1. Context admission verifies required input completeness and authority source.
2. Eligibility removes disallowed methods, providers, actions, or operations.
3. Routing chooses among eligible providers only.
4. Operation policy evaluates refund, capture, void, retry, recovery, settlement, COD, wallet, QR, reference, or manual action rules.
5. Risk and fraud can deny, quarantine, require manual review, lower limits, or block retry.
6. Compliance can deny or require review regardless of routing preference.
7. Manual review policy decides whether automation is allowed or review is mandatory.
8. Recovery and settlement policies apply only to relevant operation intents.
9. Execution Decision is the only output Runtime may execute.

No later policy stage may resurrect an entity denied by a mandatory earlier compliance, tenant, currency, amount, or authority-source rule unless an explicit certified override policy exists.

## Policy Model

### Policy

A named, versioned decision unit in a policy domain. A Policy declares its scope, required inputs, evaluated rules, possible outputs, reason-code namespace, fail-closed behavior, and audit contract.

### PolicyResult

The immutable output of evaluating a Policy or Policy Chain:

- Decision: Allowed, Denied, ManualReview, Retry, Quarantine, RedirectProvider, AlternativeMethod, AlternativeProvider, Recovery.
- Reason codes.
- Selected method/provider/action where applicable.
- Required constraints.
- Manual review requirement.
- Recovery or retry instructions.
- Audit summary.
- Policy version and rule ids.

### PolicyContext

The provider-neutral input snapshot containing tenant, merchant, customer, order, currency, country, region, warehouse, shipping method, payment method, provider, capabilities, constraints, risk score, order total, settlement status, evidence, and decision history.

### PolicyRule

An atomic deterministic predicate or calculation. A rule may check facts, compare limits, evaluate windows, inspect risk, require evidence, or produce a reason. A rule must not execute provider operations, mutate state, or read non-authoritative projection/browser/cache state.

### PolicyEvaluation

The complete evaluation trace for one context and intent: policy versions, rule order, matched rules, skipped rules, input hash, rule results, final decision, and audit metadata.

### PolicyDecision

The business decision returned to Runtime. It is distinct from `PaymentDecision` in PAYMENT-0.8. PolicyDecision authorizes or blocks execution; PaymentDecision records the runtime lifecycle outcome after verified evidence and state-machine admission.

### PolicyAudit

Append-only decision evidence: context hash, policy version, rule ids, reason codes, actor/system, timestamp, prior decision reference, input completeness, and output.

### PolicyVersion

Immutable version identity for policies and rule sets. New rules or modified thresholds create new versions. Runtime and audit records must be able to replay the exact version used for a decision.

## Architecture Rules

1. Policies never execute payments.
2. Policies never call provider APIs.
3. Policies never serialize provider wire contracts.
4. Policies never create PaymentTransactions.
5. Policies never mutate PaymentSession state directly.
6. Policies never bypass PaymentVerification or PaymentDecision.
7. Runtime never creates policy.
8. Runtime never hides business rules in state-machine code.
9. Runtime executes only policy-approved actions and operations.
10. Adapters expose capabilities and constraints only.
11. Adapters never decide eligibility, routing, refund, capture, void, retry, settlement, risk, fraud, compliance, or manual review.
12. Adapters never bypass policy.
13. Provider support is not business eligibility.
14. Policy must remain provider-neutral.
15. Policy may consume provider capability facts but not provider implementation details.
16. Policy decisions are deterministic for the same context, policy version, and rule set.
17. Policies are auditable.
18. Policies are versioned.
19. Policies are replay-safe.
20. Policy input must come from current authority, immutable snapshots, or admitted evidence.
21. Frontend, browser storage, redirects, SDK results, cache, Redis, Mongo, Elasticsearch, provider payload replay, and projections are not policy authority.
22. Missing required policy context fails closed.
23. Silent fallback is forbidden.
24. Manual review is a policy outcome, not an adapter or frontend invention.
25. Future providers must enter through capabilities, constraints, evidence mapping, verification, and policy evaluation without Runtime redesign.

## Extensibility Review

PAYMENT-1.0 supports future rules without Runtime changes:

| Future Need | Policy Extension | Runtime Change Required |
|---|---|---:|
| New Country | Add country/region/currency/compliance rules and provider capability data. | No |
| New Merchant | Add merchant entitlement, category, risk, settlement, or restriction rules. | No |
| New Provider | Add provider capability/constraint facts, routing rules, and evidence policy gates. | No |
| New Wallet | Add wallet eligibility, customer/device/currency/country, evidence, and risk rules. | No |
| New BNPL | Add authorization/capture/refund/settlement, customer risk, product/category, and amount-window rules. | No |
| New Marketplace Rule | Add vendor, split-order, commission, settlement, refund, and routing rules. | No |
| New Affiliate Rule | Add affiliate attribution, commission-impacting refund/cancellation, and settlement interaction rules. | No |
| New Fraud Rule | Add risk score, velocity, evidence mismatch, customer/device, and quarantine rules. | No |
| New Compliance Rule | Add sanctions, KYC/KYB, retention, currency, product, geography, or regulated-goods rules. | No |

Runtime may need new operation contracts only when the platform introduces an entirely new execution capability. Ordinary policy growth must not require state-machine specialization or provider-specific runtime branches.

## Certification Decision

Policy Engine designed: **Yes**.

Responsibilities defined: **Yes**.

Runtime boundary preserved: **Yes**. Runtime executes only and remains the certified state-machine/evidence/transaction authority.

Adapter boundary preserved: **Yes**. Adapters translate, expose capabilities/constraints, and execute provider operations under Runtime command only.

Policy domains completed: **Yes**.

Policy chain completed: **Yes**.

Provider-neutral: **Yes**.

Future-proof: **Yes**. New rules, countries, merchants, providers, wallets, BNPL, marketplace rules, affiliate rules, fraud rules, and compliance rules enter through policy versions and capability/constraint facts without Runtime redesign.

Production-certifiable: **Yes, as architecture.** Runtime implementation, policy storage, governance UI, rule authoring, operational tests, and provider-specific policy regression suites remain later phases.

Recommended next phase: **PAYMENT-1.1 Policy Contract, Rule Catalog, and Governance Model**, followed by policy runtime implementation planning and provider-specific policy regression certification.

## Success Answers

Policy Engine designed? **Yes**.

Responsibilities defined? **Yes**.

Runtime boundary preserved? **Yes**.

Adapter boundary preserved? **Yes**.

Policy domains completed? **Yes**.

Policy chain completed? **Yes**.

Provider-neutral? **Yes**.

Future-proof? **Yes**.

Production-certifiable? **Yes, as certified architecture.**

Recommended next phase? **PAYMENT-1.1 Policy Contract, Rule Catalog, and Governance Model.**

# PAYMENT-0.9 Provider Adapter Architecture Certification

Scope: Provider adapter architecture, canonical runtime contract conformance, capability matrix, constraint matrix, and runtime neutrality certification.

This document reviews the certified providers only:

- Stripe
- Paymob
- Fawry
- Manual Payment
- Cash On Delivery

This phase does not re-review provider APIs, implementation details, runtime code, frontend code, database schema, migrations, controllers, or provider SDK behavior. It treats `PAYMENT_0_PLATFORM_ARCHITECTURE_CERTIFICATION`, `PAYMENT_0_7_CANONICAL_PAYMENT_DOMAIN`, `PAYMENT_0_8_CANONICAL_RUNTIME_MODEL`, and `PAYMENT_0_5A` through `PAYMENT_0_5E` as certified source material.

## Executive Summary

All reviewed providers can execute through the canonical Payment Runtime without runtime specialization.

The provider adapter architecture is certified because each provider can be represented as a bounded adapter that creates provider sessions or handoff candidates, exposes capability and constraint facts, translates provider/customer/operator/fulfillment work into `PaymentAction`, normalizes external or manual facts into `ProviderEvidence`, supplies verification primitives, supports recovery/status inquiry where applicable, and submits settlement/refund evidence without owning canonical payment state.

No provider requires provider-specific runtime state, provider-specific lifecycle branches, or provider-specific transaction writers. Provider statuses, callbacks, redirects, SDK results, reference numbers, receipt uploads, driver evidence, carrier evidence, settlement reports, and refund evidence remain evidence candidates until admitted by the runtime. `PaymentDecision` remains the only source of canonical state transition authority. `PaymentTransaction` remains immutable runtime output and is never adapter-owned.

Certification decision: **PAYMENT-0.9 is certified as provider-adapter architecture complete.**

## Provider Adapter Model

Every certified provider can implement the canonical adapter contracts:

| Provider | IPaymentProvider | IPaymentEvidenceAdmission | IPaymentVerification | IPaymentRecovery | IPaymentSettlement | Certification |
|---|---:|---:|---:|---:|---:|---|
| Stripe | Yes | Yes | Yes | Yes | Yes | Certified |
| Paymob | Yes | Yes | Yes | Yes | Yes | Certified |
| Fawry | Yes | Yes | Yes | Yes | Yes | Certified |
| Manual Payment | Yes | Yes | Yes | Yes | Yes | Certified |
| COD | Yes | Yes | Yes | Yes | Yes | Certified |

Adapter contract meaning:

- `IPaymentProvider`: creates provider/session/action candidates and exposes provider capabilities and constraints.
- `IPaymentEvidenceAdmission`: normalizes inbound provider, customer, operator, fulfillment, refund, or settlement facts into `ProviderEvidence` candidates.
- `IPaymentVerification`: verifies provider primitives such as signatures, HMACs, message signatures, amount/currency, tenant binding, provider references, receipt metadata, reviewer identity, carrier/driver identity, and replay/idempotency facts.
- `IPaymentRecovery`: re-enters SQL payment authority, runs status inquiry or manual/COD reconciliation, preserves retry fences, and quarantines ambiguity.
- `IPaymentSettlement`: submits provider/manual/COD settlement evidence for runtime admission.

## Canonical Object Mapping

| Canonical Object | Stripe | Paymob | Fawry | Manual Payment | COD | Runtime Change Required |
|---|---|---|---|---|---|---|
| PaymentSession | PaymentIntent/session authority | Paymob order/payment-key/intention session authority | Reference/hosted/order session authority | Manual proof session | COD collection session | No |
| PaymentAttempt | Stripe attempt/confirmation try | Paymob auth/order/payment-key try | Fawry reference/order attempt | Receipt/review attempt | Delivery collection attempt | No |
| ProviderSession | PaymentIntent/client secret/Checkout session | Payment key/iframe/intention/client secret | Reference number/hosted link/wallet QR | Receipt submission/review packet | Shipment/collection packet | No |
| PaymentAction | SDK, embedded, redirect, status refresh | Redirect/iframe, embedded Pixel, wallet approval, status refresh | Reference payment, redirect, QR, wallet approval, status inquiry | Receipt upload, manual review | Receive shipment, delivery collection, manual review | No |
| ProviderEvidence | Webhook, SDK result, status inquiry, refund/settlement reports | Webhook, callback, HMAC payload, transaction inquiry, refund/settlement reports | Notification, callback, inquiry, reference payment, refund evidence | Receipt, reviewer decision, manual settlement/refund evidence | Carrier evidence, driver evidence, cash remittance, settlement report | No |
| PaymentVerification | Signature, event id, amount/currency, metadata | HMAC, provider reference, amount/currency, tenant binding | Message signature/HMAC where available, reference binding, amount/currency | Receipt integrity, reviewer authorization, amount/currency | Carrier/driver/collection verification, amount/currency | No |
| PaymentDecision | Authorize, confirm, capture, cancel, expire, reject, refund, partial refund, void, settlement, recovery, quarantine | Confirm, capture, collect where method applies, cancel, expire, reject, refund, partial refund, void, settlement, recovery, quarantine | Authorize, confirm, collect, cancel, expire, reject, refund, partial refund, void, settlement, recovery, quarantine | Confirm, collect, reject, refund, partial refund, settlement, recovery, quarantine | Collect, reject, refund/reversal, settlement, recovery, quarantine | No |
| PaymentTransaction | Authorization/capture/refund/void/settlement transaction | Confirm/capture/refund/void/settlement transaction | Reference collection/refund/void/settlement transaction | Collection/refund/settlement transaction | Collection/refund/settlement transaction | No |
| PaymentSettlement | Provider settlement/payout evidence | Provider settlement/payout evidence | Provider settlement/report evidence | Manual deposit/reconciliation evidence | COD remittance/reconciliation evidence | No |

Conclusion: all provider facts map into canonical runtime objects. No provider requires a runtime-specific object.

## Action Mapping

| Action | Canonical PaymentAction | Providers |
|---|---|---|
| Redirect | `Redirect` | Stripe, Paymob, Fawry |
| Embedded Checkout | `EmbeddedCheckout` | Stripe, Paymob |
| SDK | `SdkConfirmation` or `EmbeddedCheckout` | Stripe |
| Reference Payment | `ReferencePayment` | Fawry |
| Receipt Upload | `ReceiptUpload` | Manual Payment |
| Receive Shipment | `ReceiveShipment` | COD |
| QR | `QrPayment` | Fawry; Paymob only if account/API support is later certified |
| Wallet Approval | `WalletApproval` | Paymob and Fawry where method/account enabled |
| Manual Review | `ManualReview` | Stripe risk review, Manual Payment, COD, refund/settlement exceptions |
| Status Refresh | `StatusRefresh` | Stripe, Paymob, Fawry, Manual Payment, COD |

Action mapping is complete. Provider-specific action names do not enter the runtime state machine.

## Evidence Mapping

| Evidence Source | Canonical ProviderEvidence | Providers |
|---|---|---|
| Webhook | Webhook evidence | Stripe, Paymob, Fawry |
| Callback | Callback evidence | Paymob, Fawry, Stripe return evidence as non-final presentation/evidence |
| Notification | Provider notification evidence | Fawry, Paymob |
| Receipt | Receipt evidence | Manual Payment |
| Carrier Evidence | Fulfillment collection evidence | COD |
| Driver Evidence | Driver collection evidence | COD |
| Settlement Report | Settlement evidence | Stripe, Paymob, Fawry, Manual Payment, COD |
| Refund Evidence | Refund evidence | Stripe, Paymob, Fawry, Manual Payment, COD |
| Status Inquiry | Status inquiry evidence | Stripe, Paymob, Fawry, Manual Payment, COD |

Evidence mapping is complete. Every source remains non-authoritative until verified and admitted.

## Decision Mapping

| Canonical Decision | Stripe | Paymob | Fawry | Manual Payment | COD |
|---|---:|---:|---:|---:|---:|
| Authorize | Yes | Yes | Yes | N/A | N/A |
| Confirm | Yes | Yes | Yes | Yes | N/A |
| Capture | Yes | Yes | Yes | N/A | N/A |
| Collect | N/A | Method-dependent | Yes | Yes | Yes |
| Cancel | Yes | Yes | Yes | Yes | Yes |
| Expire | Yes | Yes | Yes | Yes | Yes |
| Reject | Yes | Yes | Yes | Yes | Yes |
| Refund | Yes | Yes | Yes | Yes | Yes |
| Partial Refund | Yes | Yes | Yes | Yes | Yes |
| Void | Yes | Yes | Yes | N/A | N/A |
| Settlement | Yes | Yes | Yes | Yes | Yes |
| Recovery | Yes | Yes | Yes | Yes | Yes |
| Quarantine | Yes | Yes | Yes | Yes | Yes |

`N/A` means the provider workflow does not naturally expose that decision, not that the runtime requires specialization. Unsupported decisions are capability-gated by policy.

## State Mapping

Providers map into canonical states only:

| Canonical State | Provider Mapping Examples |
|---|---|
| Pending | Created session, reference issued, receipt awaiting upload, COD awaiting shipment |
| ActionRequired | Redirect needed, SDK confirmation needed, reference payment pending, receipt upload needed, delivery collection needed |
| AwaitingConfirmation | Webhook/callback/inquiry pending, manual review pending, COD collection pending verification |
| Authorized | Stripe/Paymob/Fawry authorization evidence admitted |
| Confirmed | Provider success or manual approval admitted |
| Captured | Capture evidence admitted |
| Collected | Manual/COD/reference collection admitted |
| Settled | Provider/manual/COD settlement evidence admitted |
| PartiallyRefunded | Partial refund/reversal admitted |
| Refunded | Full refund/reversal admitted |
| Cancelled | Runtime cancellation admitted |
| Expired | Runtime/provider/manual expiry admitted |
| Failed | Verified failure admitted |
| Rejected | Manual/provider/runtime rejection admitted |
| Quarantined | Plausible but mismatched or ambiguous evidence |
| ReconciliationRequired | External/payment/settlement ambiguity requires reconciliation |

Provider-specific states must not leak into runtime. They are mapped evidence only.

## Capability Matrix

Legend: Yes = provider/runtime architecture supports the capability; Partial = supported with provider/account/method/runtime execution limits documented in discovery; No = not certified for that provider; N/A = not applicable to provider workflow.

| Provider | Redirect | Embedded Checkout | SDK | Wallet | QR | Reference Payment | Manual Review | Receipt Upload | Delivery Collection | Capture | Refund | Partial Refund | Void | Settlement | Async Confirmation | Status Inquiry | Recovery | Replay Protection | Idempotency | Multi-Currency | Metadata | Audit | Evidence | Retry | Quarantine | Reconciliation |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Stripe | Partial | Yes | Yes | Method-dependent | No | No | Partial | No | No | Partial | Partial | Partial | Partial | Yes | Partial | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| Paymob | Yes | Partial | No | Partial | No | No | Partial | No | No | Partial | Partial | Partial | Partial | Yes | Partial | Yes | Yes | Yes | Partial | Partial | Partial | Yes | Yes | Yes | Yes | Yes |
| Fawry | Partial | No | No | Partial | Partial | Yes | Partial | No | No | Partial | Partial | Partial | Partial | Yes | Yes | Yes | Yes | Yes | Partial | Partial | Yes | Yes | Yes | Yes | Yes | Yes |
| Manual | No | No | No | No | No | No | Yes | Yes | No | N/A | Partial | Partial | N/A | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Partial | Partial | Yes | Partial | Yes | Yes |
| COD | No | No | No | No | No | No | Yes | No | Yes | N/A | Partial | Partial | N/A | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Partial | Yes | Yes | Yes | Yes |

Notes:

- Stripe hosted redirect and embedded checkout are provider-supported; current implementation centers on Payment Element/client secret and return evidence.
- Paymob Unified Checkout and Pixel are provider-supported, while the current certified runtime uses legacy iframe/payment-key flow. That remains adapter-compatible.
- Fawry reference payment is the primary certified action. QR/wallet/redirect are provider capabilities that remain future adapter modes.
- Manual Payment and COD rely on human/fulfillment evidence, not provider SDKs.
- Capture, refund, partial refund, void, and settlement are runtime workflows capability-gated by provider/method/account support.

## Constraint Matrix

Legend: Yes = required by the provider/workflow; Conditional = required only for some methods or operational modes; No = not required.

| Provider | Requires Redirect | Requires Delivery | Requires Human Review | Requires Reference Number | Requires Webhook | Requires Callback | Requires Status Inquiry | Requires Settlement Report | Requires Receipt | Requires HMAC | Requires Message Signature | Requires Manual Approval | Requires Provider Session | Requires Carrier Evidence |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Stripe | Conditional | No | Conditional | No | Yes | Conditional | Conditional | Conditional | No | No | Yes | No | Yes | No |
| Paymob | Conditional | No | Conditional | No | Yes | Yes | Conditional | Conditional | No | Yes | No | No | Yes | No |
| Fawry | Conditional | No | Conditional | Yes | Conditional | Conditional | Yes | Conditional | No | Conditional | Conditional | No | Yes | No |
| Manual | No | No | Yes | No | No | No | Conditional | Yes | Yes | No | No | Yes | Conditional | No |
| COD | No | Yes | Conditional | No | No | Conditional | Yes | Yes | No | No | No | Conditional | Conditional | Yes |

Constraint conclusion: constraints are provider metadata and policy inputs, not runtime branches. The runtime evaluates constraints through capability/decision policy before issuing actions or admitting decisions.

## Adapter Responsibilities

Adapters may:

- Translate provider/domain-specific raw facts into canonical candidates.
- Normalize callbacks, webhooks, notifications, status inquiries, receipts, carrier/driver evidence, settlement reports, and refund evidence.
- Verify primitives such as HMAC, message signature, event identity, provider reference, receipt metadata, provider account, amount, currency, and tenant binding.
- Create provider sessions or handoff artifacts under runtime direction.
- Collect evidence from provider/customer/operator/fulfillment sources.
- Query provider/manual/COD status for recovery and reconciliation.
- Expose capabilities and constraints.

Adapters must never:

- Own payment state.
- Own lifecycle progression.
- Mutate runtime state directly.
- Create `PaymentTransaction` records.
- Skip verification.
- Bypass evidence admission.
- Treat redirects, SDK results, browser state, cache, provider payload, shipping status, or accounting state as payment finality.

## Extensibility Review

The architecture can onboard future providers by implementing adapter contracts only:

| Future Provider Class | Adapter Work Required | Runtime Redesign Required |
|---|---|---:|
| PayPal | Provider session, redirect/embedded action, webhook/signature evidence, refund/settlement mapping | No |
| Amazon Pay | Provider session, wallet approval action, signed notification evidence, refund/settlement mapping | No |
| Apple Pay | SDK/wallet action, token/provider evidence, authorization/capture mapping | No |
| Google Pay | SDK/wallet action, token/provider evidence, authorization/capture mapping | No |
| ValU | BNPL authorization/capture/settlement evidence, refund/reversal mapping | No |
| Tamara | BNPL provider session, async confirmation, capture/refund/settlement evidence | No |
| Tabby | BNPL provider session, async confirmation, capture/refund/settlement evidence | No |
| Bank APIs | Redirect/reference/authorization action, signed bank evidence, settlement/reconciliation mapping | No |
| Crypto | Wallet/QR action, chain/provider evidence, confirmation-depth verification, settlement/reconciliation mapping | No |

Future providers may require new adapter code, capability flags, tenant configuration, provider credential governance, tests, and operational runbooks. They do not require a new payment runtime model.

## Architecture Certification

Certified:

- Provider adapters can execute through canonical runtime contracts.
- All provider actions map into `PaymentAction`.
- All provider/manual/fulfillment evidence maps into `ProviderEvidence`.
- All final outcomes map through `PaymentVerification` and `PaymentDecision`.
- All financial recognition maps into immutable `PaymentTransaction`.
- Settlement remains separate from confirmation, capture, and collection.
- Refund, partial refund, void, recovery, quarantine, and reconciliation remain runtime workflows.
- Provider states are mapped evidence only and are forbidden as runtime states.
- Capability and constraint differences are policy inputs, not runtime specialization.

Runtime neutrality is preserved.

## Production Readiness

Production architecture is complete for provider adapter certification.

Operational and implementation readiness remain separate future phases:

- Provider-specific adapter migration and execution phases for Stripe, Paymob, Fawry, Manual Payment, and COD.
- Provider refund/capture/void/settlement execution adapters where product scope requires outbound execution.
- Provider credential/environment governance and tenant entitlement modeling.
- Webhook/callback/status replay drills for each provider.
- Settlement and reconciliation operational runbooks.
- Provider-specific regression suites proving capability/constraint gates.

## Success Answers

Reviewed all providers? **Yes. Stripe, Paymob, Fawry, Manual Payment, and COD were reviewed as certified providers.**

Capability Matrix completed? **Yes.**

Constraint Matrix completed? **Yes.**

Canonical mapping completed? **Yes.**

Runtime neutrality preserved? **Yes. No provider requires runtime specialization.**

Adapter responsibilities certified? **Yes. Adapter may translate, normalize, verify primitives, create provider sessions, collect evidence, query status, and expose capabilities/constraints; adapter must never own state, lifecycle, transactions, or evidence admission bypass.**

Future provider onboarding certified? **Yes. Future providers can be onboarded through adapter contracts, capabilities, constraints, evidence mappings, and verification rules only.**

Production architecture complete? **Yes for PAYMENT-0.9 provider adapter architecture certification. Implementation and operational rollout remain later phases.**

Recommended next phase? **PAYMENT-1.0 Provider Adapter Contract and Capability Registry Design, followed by provider-specific adapter implementation/migration phases for Stripe, Paymob, Fawry, Manual Payment, COD, refunds, settlement, and recovery.**

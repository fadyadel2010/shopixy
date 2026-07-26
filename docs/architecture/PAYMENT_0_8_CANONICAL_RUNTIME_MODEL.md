# PAYMENT-0.8 Canonical Runtime Model

Date: 2026-07-10
Scope: Provider-neutral enterprise Payment Runtime model and state machine
Status: CERTIFIED ARCHITECTURE

Runtime modified: No.

Implementation modified: No.

`.ai-context` updated: No. PAYMENT-0.8 does not discover a new authority boundary beyond PAYMENT-0.7; it converts the certified Payment Domain into the canonical runtime model.

## Executive Summary

PAYMENT-0.8 defines the permanent provider-neutral Payment Runtime that every provider adapter, manual workflow, cash workflow, settlement workflow, refund workflow, mobile client, public API, and future marketplace extension must follow.

The runtime is not Stripe, Paymob, Fawry, Manual Payment, Vodafone Cash, COD, wallet, QR, bank transfer, or BNPL specific. Providers and workflows are runtime participants that create handoff data, request actions, submit evidence, execute supported operations, or report settlement. They never own platform payment state.

The canonical runtime sequence is:

```text
PaymentSession
  -> PaymentAttempt
  -> ProviderSession
  -> PaymentAction
  -> ProviderEvidence
  -> PaymentVerification
  -> PaymentDecision
  -> PaymentTransaction
  -> PaymentSettlement / Refund / Capture / Void / Recovery
  -> AuditRecord / PaymentEvent / Projection
```

Certification decision: PAYMENT-0.8 is certified as the canonical runtime model and state machine for future Payment implementation phases.

## Runtime Objects

### PaymentSession

| Dimension | Definition |
|---|---|
| Purpose | Durable platform payment obligation for an order, invoice, marketplace payable, subscription invoice, COD collection, or other payable reference. |
| Owner | Payment Runtime backed by SQL payment authority. Checkout, Order, Provider, Shipping, Administration, Treasury, Accounting, and frontend clients are consumers or evidence producers only. |
| Lifetime | Created or replayed idempotently after payable admission; remains addressable through terminal state, refund, settlement, recovery, audit, and retention windows. |
| Mutability | Mutable only through authorized Payment Runtime decisions and state-machine transitions. Identity, tenant, payable reference, original amount, original currency, and initial order binding are immutable after admission. |
| Relationships | Owns many PaymentAttempts, PaymentActions, ProviderEvidence records, PaymentVerifications, PaymentDecisions, PaymentTransactions, AuditRecords, and zero or more Settlements, Refunds, Captures, Voids, and Recoveries. |
| Responsibilities | Preserve tenant scope, payable identity, amount/currency authority, provider/method selection, session expiration, active attempt rules, canonical state, idempotency keys, recovery posture, and lifecycle audit. |

### PaymentAttempt

| Dimension | Definition |
|---|---|
| Purpose | Retryable initiation, confirmation, capture, refund, settlement, status inquiry, manual review, or recovery activity under a PaymentSession. |
| Owner | Payment Runtime. Provider adapters may execute attempt commands but do not own attempt authority. |
| Lifetime | Starts when the runtime authorizes an action crossing a provider/manual/operational boundary; ends as succeeded, failed, expired, cancelled, blocked, or superseded. |
| Mutability | Append-oriented. Attempt status and provider execution metadata may be updated by runtime-controlled admission; provider identity once accepted is immutable except for additive evidence. |
| Relationships | Belongs to one PaymentSession; may create one or more ProviderSessions, PaymentActions, ProviderEvidence records, Verifications, Decisions, Transactions, and AuditRecords. |
| Responsibilities | Fence retries, prevent duplicate external sessions, bind provider execution to idempotency, preserve provider acceptance ambiguity, and support safe recovery. |

### ProviderSession

| Dimension | Definition |
|---|---|
| Purpose | Provider-facing reference, token, redirect URL, client secret, payment key, QR payload, reference number, wallet request, manual proof session, COD obligation reference, capture id, refund id, or settlement batch identifier. |
| Owner | Payment Runtime as admitted evidence; provider owns the external artifact but not platform finality. |
| Lifetime | Begins after provider/workflow initiation evidence is admitted; expires according to runtime policy, provider policy, or action policy; retained for audit/reconciliation. |
| Mutability | Provider identity fields are immutable after admission. Expiration, recoverability, status-inquiry metadata, and redacted provider metadata may be appended. |
| Relationships | Belongs to one PaymentAttempt and one PaymentSession; can require PaymentActions and can be referenced by ProviderEvidence, Verification, Decision, Transaction, Settlement, Refund, Capture, or Void. |
| Responsibilities | Provide stable routing for callbacks/webhooks/status inquiries, enforce tenant/provider/method binding, support deduplication, and prevent mixed-provider evidence. |

### PaymentAction

| Dimension | Definition |
|---|---|
| Purpose | Runtime-issued requirement for a customer, operator, provider, fulfillment actor, driver, carrier, or system to perform an action before a decision can be made. |
| Owner | Payment Runtime. UI and provider adapters render or execute actions only after runtime issuance. |
| Lifetime | Created when the session needs external activity; active until completed, expired, cancelled, superseded, or invalidated by terminal state. |
| Mutability | Action state may change through runtime transitions; action instructions and secure handoff data are immutable per issuance. New attempts create new actions instead of rewriting prior action history. |
| Relationships | Belongs to PaymentSession and usually PaymentAttempt/ProviderSession; produces ProviderEvidence or triggers Recovery/Status Refresh. |
| Responsibilities | Represent redirect, embedded checkout, SDK confirmation, receipt upload, reference payment, QR scan, wallet approval, receive-shipment-and-pay, manual review, and status refresh without provider-specific state. |

### ProviderEvidence

| Dimension | Definition |
|---|---|
| Purpose | Any fact submitted to Payment before final admission: webhook, callback, status inquiry, frontend result, receipt, carrier evidence, driver evidence, settlement report, refund evidence, or manual review. |
| Owner | Payment Runtime after admission into the evidence log. Source systems own raw facts before submission. |
| Lifetime | Created at receipt/admission; retained according to audit, financial, dispute, security, and provider retention policy. |
| Mutability | Immutable after admission except for verification linkage, redaction markers, quarantine classification, and retention metadata. Raw payloads are stored by reference or hash where appropriate. |
| Relationships | Belongs to PaymentSession and optionally PaymentAttempt/ProviderSession/Refund/Capture/Void/Settlement; feeds PaymentVerification. |
| Responsibilities | Preserve source, tenant binding, provider reference, amount/currency, event type, timestamps, actor/source identity, idempotency key, payload hash, integrity hash, ordering key, and audit trail. |

### PaymentVerification

| Dimension | Definition |
|---|---|
| Purpose | Runtime admission result that authenticates, normalizes, deduplicates, orders, tenant-checks, amount/currency-checks, provider-checks, and replay-checks ProviderEvidence. |
| Owner | Payment Runtime verification boundary. Provider adapters may supply verification primitives but not final admission. |
| Lifetime | Created per evidence admission or recovery inquiry; retained with evidence and decisions. |
| Mutability | Immutable once completed. Failed verifications can be superseded only by new evidence or authorized recovery, not edited into success. |
| Relationships | Consumes ProviderEvidence; produces zero or one PaymentDecision; references PaymentSession, Attempt, ProviderSession, and AuditRecord. |
| Responsibilities | Validate signatures, HMAC, message signatures, amount, currency, tenant, provider identity, duplicate keys, ordering, replay windows, capability/constraint fit, and recovery eligibility. |

### PaymentDecision

| Dimension | Definition |
|---|---|
| Purpose | Lifecycle-owned determination after verification or policy evaluation. |
| Owner | Payment Runtime lifecycle authority. |
| Lifetime | Created for every material state transition, rejection, quarantine, retry block, terminal collapse, refund/capture/void decision, or settlement admission. |
| Mutability | Immutable decision record. A later decision may supersede an earlier non-terminal decision through an allowed transition. |
| Relationships | Consumes PaymentVerification, policy context, or recovery context; creates PaymentTransaction, Refund, Capture, Void, Settlement, Recovery, AuditRecord, and events. |
| Responsibilities | Confirm, authorize, capture, reject, cancel, expire, refund, partial refund, void, collect, quarantine, fail, settle, or leave pending with explicit decision authority and audit reason. |

### PaymentTransaction

| Dimension | Definition |
|---|---|
| Purpose | Admitted financial movement or financial lifecycle outcome recognized by Payment. |
| Owner | Payment Runtime for payment recognition; Accounting/Treasury consume admitted outcomes for ledger and cash management authority. |
| Lifetime | Created after an admitted decision; retained permanently or according to financial retention policy. |
| Mutability | Immutable. Corrections use reversal, adjustment, reconciliation, refund, or settlement transactions. |
| Relationships | Belongs to PaymentSession and Decision; may link to ProviderEvidence, Refund, Capture, Void, Settlement, Accounting entry, Treasury movement, and AuditRecord. |
| Responsibilities | Represent authorization, capture, sale, collection, refund, void, settlement, failure, reversal, adjustment, and reconciliation-facing facts with amount/currency and source evidence. |

### PaymentSettlement

| Dimension | Definition |
|---|---|
| Purpose | Payment-owned record that collected/captured value was settled, deposited, cleared, reconciled, paid out, or otherwise matched to treasury/accounting evidence. |
| Owner | Payment Runtime owns settlement admission for payment state; Treasury and Accounting own cash/ledger execution records. |
| Lifetime | Begins when settlement evidence is admitted; retained for reconciliation and audit. |
| Mutability | Immutable once recorded; corrections are settlement adjustments or reversals. |
| Relationships | Belongs to PaymentSession and usually one or more Transactions; may consume provider settlement report, manual deposit, COD remittance, treasury evidence, accounting evidence, and reconciliation results. |
| Responsibilities | Support provider settlement, manual settlement, COD settlement, treasury integration, accounting integration, settlement discrepancy handling, and reconciliation linkage. |

### Refund

| Dimension | Definition |
|---|---|
| Purpose | Full or partial reversal of confirmed, collected, captured, or settled payment value. |
| Owner | Payment/Refund lifecycle authority. Provider adapters, Admin, Treasury, and Accounting execute or evidence refund operations under runtime control. |
| Lifetime | Starts with refund intent/admission; progresses through requested, processing, completed, failed, rejected, cancelled, or reconciliation-required; retained permanently for financial history. |
| Mutability | Refund identity, amount, currency, target session, and reason snapshot are immutable. Status changes occur only through decisions and evidence. |
| Relationships | Belongs to PaymentSession; creates PaymentAttempt, ProviderEvidence, Verification, Decision, Transaction, Settlement adjustment, and AuditRecord. |
| Responsibilities | Enforce refundable balance, partial refund rules, provider capability, amount/currency validation, duplicate execution prevention, evidence capture, and lifecycle fanout. |

### Capture

| Dimension | Definition |
|---|---|
| Purpose | Completion of a prior authorization into captured value. |
| Owner | Payment Runtime. |
| Lifetime | Starts from capture intent or provider evidence; ends captured, failed, cancelled, expired, or reconciliation-required. |
| Mutability | Capture amount/currency/authorization binding are immutable. Outcome is append-only through evidence and decisions. |
| Relationships | Belongs to an Authorized PaymentSession and related ProviderSession; creates PaymentTransaction and may feed Settlement. |
| Responsibilities | Enforce prior authorization, capturable balance, provider capability, duplicate protection, amount/currency validation, and audit. |

### Void

| Dimension | Definition |
|---|---|
| Purpose | Cancellation of an unpaid, pending, reference, authorized, or uncaptured obligation where provider/method policy allows voiding. |
| Owner | Payment Runtime. |
| Lifetime | Created from cancellation/void intent; terminal as voided/cancelled, failed, expired, or reconciliation-required. |
| Mutability | Immutable identity and target binding; outcome appended through evidence and decisions. |
| Relationships | Belongs to PaymentSession and may reference Attempt, ProviderSession, Authorization, or PaymentAction. |
| Responsibilities | Prevent void after capture/collection/settlement unless modeled as reversal/refund, preserve provider evidence, release pending obligations, and emit audit. |

### Recovery

| Dimension | Definition |
|---|---|
| Purpose | Controlled re-entry path for ambiguous, stale, failed, duplicate, partially persisted, missing projection, webhook-late, provider-status-unknown, or settlement-mismatch conditions. |
| Owner | Payment Runtime recovery authority. |
| Lifetime | Begins when the runtime detects ambiguity or an operator/system requests recovery; ends resolved, terminally failed, quarantined, or requires manual reconciliation. |
| Mutability | Recovery record is append-only; resolved state is produced by decisions, not by editing history. |
| Relationships | References PaymentSession, Attempt, ProviderSession, Evidence, Verification, Decision, Transaction, Settlement, Refund, and AuditRecord. |
| Responsibilities | Re-enter SQL authority, verify current provider status, collapse duplicates, respect terminal states, preserve retry fences, quarantine ambiguity, and never trust cache/projection/frontend/provider payload alone. |

### AuditRecord

| Dimension | Definition |
|---|---|
| Purpose | Durable trace of runtime actions, evidence, verification, decisions, retries, actor actions, provider references, state transitions, settlement, refund, recovery, and failures. |
| Owner | Payment Runtime audit boundary. |
| Lifetime | Created for every material runtime event; retained according to regulatory, dispute, security, and financial policy. |
| Mutability | Append-only. Redaction may hide sensitive payload fragments without rewriting material facts. |
| Relationships | Can attach to every runtime object and event. |
| Responsibilities | Provide forensic replay, certification proof, compliance evidence, dispute support, retention control, and projection rebuild source references. |

## Object Relationships

Canonical cardinality:

```text
PaymentSession 1
  -> * PaymentAttempt
  -> * PaymentAction
  -> * ProviderEvidence
  -> * PaymentVerification
  -> * PaymentDecision
  -> * PaymentTransaction
  -> * AuditRecord
  -> * Recovery

PaymentAttempt 1
  -> * ProviderSession
  -> * PaymentAction
  -> * ProviderEvidence
  -> * PaymentVerification
  -> * PaymentDecision

ProviderSession 1
  -> * PaymentAction
  -> * ProviderEvidence
  -> * PaymentVerification

ProviderEvidence 1
  -> 1 PaymentVerification
  -> 0..1 PaymentDecision
  -> 0..* PaymentTransaction

PaymentDecision 1
  -> 0..* PaymentTransaction
  -> 0..* Refund
  -> 0..* Capture
  -> 0..* Void
  -> 0..* PaymentSettlement
  -> 1..* AuditRecord
```

Ownership review:

- PaymentSession owns runtime identity and canonical state.
- PaymentAttempt owns retry/execution identity under the session.
- ProviderSession is evidence owned by Payment, even when the external provider owns its own remote session.
- ProviderEvidence is not finality; it requires verification.
- PaymentVerification is not state change by itself; it authorizes a decision candidate.
- PaymentDecision owns lifecycle mutation authority.
- PaymentTransaction is immutable financial recognition after a decision.
- PaymentSettlement is payment-state settlement admission, while Treasury/Accounting own their own cash and ledger records.
- Refund, Capture, Void, and Recovery re-enter Payment authority and never mutate state outside the state machine.

## State Machine

### Canonical States

The runtime uses one compact provider-neutral state machine:

| State | Meaning | Terminal |
|---|---|---|
| Draft | Runtime has accepted a payable candidate but has not completed session admission. | No |
| Pending | Session exists; no final outcome has been admitted. | No |
| ActionRequired | Customer, operator, provider, fulfillment, driver, carrier, or system action is required. | No |
| AwaitingConfirmation | Action/evidence occurred or is expected; final confirmation has not been admitted. | No |
| Authorized | Funds or provider approval are authorized but not captured/collected. | No |
| Confirmed | Provider/workflow confirmation was admitted for non-cash/non-COD confirmation flows. | No |
| Captured | Authorized funds were captured. | No |
| Collected | Cash/manual/COD-like value collection was admitted. | No |
| Settled | Captured/confirmed/collected value was reconciled or settled. | Yes for positive payment value, except refund or adjustment can follow. |
| PartiallyRefunded | Partial refund was admitted. | No |
| Refunded | Full refund was admitted. | Yes |
| Cancelled | Pending, action-required, awaiting-confirmation, or authorized obligation was cancelled or voided. | Yes |
| Expired | Session/action/provider window elapsed without valid completion. | Yes |
| Failed | Terminal payment failure was admitted. | Yes |
| Rejected | Human/provider/runtime review rejected evidence before confirmation/collection. | Yes |
| Quarantined | Runtime found amount/currency/scope/provider/ordering ambiguity requiring controlled review. | No |
| ReconciliationRequired | External or settlement ambiguity requires recovery, operator review, or accounting/treasury reconciliation before finality. | No |

Provider-specific statuses are mapped to these states only as evidence. No provider may add runtime states.

### Allowed Transitions

| From | To | Required Authority |
|---|---|---|
| Draft | Pending | PaymentSession admission succeeds. |
| Draft | Failed | Admission fails in a terminal, non-retryable way. |
| Draft | Quarantined | Admission detects inconsistent payable, tenant, amount, or currency evidence. |
| Pending | ActionRequired | Provider/session/manual/COD action is issued. |
| Pending | AwaitingConfirmation | Runtime awaits webhook, callback, manual review, status inquiry, carrier evidence, or provider completion. |
| Pending | Authorized | Verified authorization evidence admitted. |
| Pending | Confirmed | Verified confirmation/sale evidence admitted. |
| Pending | Collected | Verified collection evidence admitted. |
| Pending | Expired | Runtime expiration policy fires. |
| Pending | Cancelled | Authorized cancellation/void decision. |
| Pending | Failed | Verified terminal failure decision. |
| Pending | Rejected | Manual/provider/runtime rejection decision. |
| Pending | Quarantined | Verification mismatch or provider/session ambiguity. |
| ActionRequired | AwaitingConfirmation | Action completed or submitted; backend awaits final evidence. |
| ActionRequired | Pending | Action superseded by retry or refreshed handoff without finality. |
| ActionRequired | Expired | Action/session expiration. |
| ActionRequired | Cancelled | Authorized cancellation/void. |
| ActionRequired | Failed | Terminal action failure. |
| ActionRequired | Rejected | Manual/operator/provider rejection. |
| AwaitingConfirmation | Confirmed | Verified confirmation evidence admitted. |
| AwaitingConfirmation | Authorized | Verified authorization evidence admitted. |
| AwaitingConfirmation | Collected | Verified collection evidence admitted. |
| AwaitingConfirmation | Pending | Retry/recovery returns to pending before a new action. |
| AwaitingConfirmation | Expired | Expiration admitted. |
| AwaitingConfirmation | Failed | Verified terminal failure. |
| AwaitingConfirmation | Rejected | Review rejection. |
| AwaitingConfirmation | Quarantined | Verification mismatch or ambiguous provider result. |
| Authorized | Captured | Capture decision admitted. |
| Authorized | Cancelled | Void/cancel decision admitted before capture. |
| Authorized | Expired | Authorization expired. |
| Authorized | Failed | Authorization/capture failure requiring terminal failure. |
| Authorized | Quarantined | Authorization/capture ambiguity. |
| Confirmed | Settled | Settlement admitted. |
| Confirmed | PartiallyRefunded | Partial refund admitted. |
| Confirmed | Refunded | Full refund admitted. |
| Confirmed | ReconciliationRequired | External/settlement ambiguity discovered. |
| Captured | Settled | Settlement admitted. |
| Captured | PartiallyRefunded | Partial refund admitted. |
| Captured | Refunded | Full refund admitted. |
| Captured | ReconciliationRequired | Capture/settlement ambiguity discovered. |
| Collected | Settled | Cash/manual/COD settlement admitted. |
| Collected | PartiallyRefunded | Partial refund/reversal admitted. |
| Collected | Refunded | Full refund/reversal admitted. |
| Collected | ReconciliationRequired | Cash custody, COD, manual, or settlement ambiguity discovered. |
| Settled | PartiallyRefunded | Partial refund after settlement. |
| Settled | Refunded | Full refund after settlement. |
| Settled | ReconciliationRequired | Settlement discrepancy requiring adjustment. |
| PartiallyRefunded | Refunded | Additional refund reaches full refunded amount. |
| PartiallyRefunded | Settled | Remaining non-refunded balance settlement/reconciliation admitted. |
| PartiallyRefunded | ReconciliationRequired | Refund/settlement ambiguity. |
| Quarantined | Pending | Controlled recovery proves no finality and permits retry. |
| Quarantined | AwaitingConfirmation | Recovery proves evidence is still pending finality. |
| Quarantined | Confirmed | Authorized review/recovery verifies successful payment. |
| Quarantined | Collected | Authorized review/recovery verifies collection. |
| Quarantined | Failed | Authorized review/recovery rejects or fails evidence. |
| Quarantined | Rejected | Human/provider/runtime review rejects evidence. |
| Quarantined | ReconciliationRequired | Ambiguity cannot be resolved inside payment verification. |
| ReconciliationRequired | Confirmed | Reconciliation proves confirmed payment. |
| ReconciliationRequired | Captured | Reconciliation proves captured payment. |
| ReconciliationRequired | Collected | Reconciliation proves collected value. |
| ReconciliationRequired | Settled | Reconciliation proves settlement. |
| ReconciliationRequired | PartiallyRefunded | Reconciliation admits partial refund. |
| ReconciliationRequired | Refunded | Reconciliation admits full refund. |
| ReconciliationRequired | Failed | Reconciliation proves terminal failure/non-payment. |

### Forbidden Transitions

These transitions are forbidden unless expressed as a new Refund, Adjustment, Reversal, Recovery, or Reconciliation decision:

- Failed, Rejected, Cancelled, Expired, or Refunded -> Confirmed, Captured, Collected, Authorized, or Settled.
- Settled -> Pending, ActionRequired, AwaitingConfirmation, Authorized, Confirmed, Captured, or Collected.
- Captured, Confirmed, Collected, or Settled -> Cancelled or Expired.
- Authorized -> Confirmed without capture/sale/collection evidence.
- Pending or ActionRequired -> Settled without Confirmed, Captured, or Collected value.
- Any state -> provider-specific state.
- Any state -> payment success from frontend SDK result, redirect result, browser state, cache, projection, or raw provider payload alone.
- Any state -> payment finality from delivery evidence alone unless the evidence is explicitly admitted as collection evidence by Payment.
- Any state -> amount/currency-changing state without immutable adjustment/reversal/refund transaction.

### Retry Transitions

Retry is allowed only while the session is non-terminal or explicitly recoverable:

- Pending -> PaymentAttempt -> ActionRequired.
- ActionRequired -> superseded PaymentAction under the same or new PaymentAttempt.
- AwaitingConfirmation -> Pending only after runtime proves the prior attempt did not produce external finality or is safely fenced.
- Expired -> Pending only by creating a new PaymentSession or formally reopening through a certified policy; the expired session itself remains terminal.
- Failed/Rejected/Cancelled -> new PaymentSession only, unless a certified recovery decision proves the prior terminal decision was a controlled quarantine/reconciliation classification rather than final payment outcome.

Retry must preserve provider-session re-init fences. If the runtime crossed an external provider boundary and cannot prove whether provider authority exists, retry is blocked and the session enters Quarantined or ReconciliationRequired.

### Recovery Transitions

Recovery always re-enters SQL Payment authority and may perform status inquiry, webhook replay admission, duplicate collapse, settlement report matching, manual review, refund evidence matching, or operator reconciliation.

Allowed recovery outcomes:

- Non-terminal ambiguity -> Pending, ActionRequired, AwaitingConfirmation, Quarantined, or ReconciliationRequired.
- Verified success -> Confirmed, Captured, Collected, or Settled according to evidence.
- Verified failure -> Failed, Rejected, Cancelled, or Expired.
- Refund/settlement ambiguity -> PartiallyRefunded, Refunded, Settled, or ReconciliationRequired.

Recovery cannot use frontend state, route state, redirect parameters, browser storage, Redis, Mongo projections, search indexes, provider payload replay, order status alone, or accounting records alone as Payment authority.

### Terminal States

Terminal for the payment obligation:

- Cancelled
- Expired
- Failed
- Rejected
- Refunded

Terminal for positive payment value but still open to refund/adjustment/reconciliation:

- Settled

Non-terminal:

- Draft
- Pending
- ActionRequired
- AwaitingConfirmation
- Authorized
- Confirmed
- Captured
- Collected
- PartiallyRefunded
- Quarantined
- ReconciliationRequired

### Failure Handling

Failures must be classified:

- Verification failure: evidence rejected, no lifecycle success.
- Provider failure: provider reports decline, cancellation, expired, failed, or not found.
- Runtime failure: local persistence, idempotency, or provider-session materialization uncertainty.
- Amount/currency failure: mismatch fails closed to Quarantined or Failed.
- Tenant/scope failure: rejected or quarantined; never admitted.
- Duplicate failure: duplicate collapses idempotently to current authority.
- Ordering failure: stale evidence is ignored, quarantined, or retained as audit only.

### Refund Handling

Refund is not a direct rewrite of payment state. It creates Refund, Evidence, Verification, Decision, Transaction, Settlement/Accounting/Treasury integration, and Audit records.

Refund rules:

- Refund requires Confirmed, Captured, Collected, Settled, or PartiallyRefunded state.
- Refund amount cannot exceed refundable balance.
- Partial refund produces PartiallyRefunded.
- Full refund produces Refunded.
- Provider/manual/COD refund evidence must be verified.
- Failed refund does not undo the original payment.
- Refund after settlement may require settlement adjustment or reconciliation.

### Settlement Transitions

Settlement is a distinct runtime admission:

- Confirmed -> Settled for sale-style providers after provider settlement/reconciliation evidence.
- Captured -> Settled after capture clearing evidence.
- Collected -> Settled after cash/manual/COD deposit/remittance evidence.
- PartiallyRefunded -> Settled for remaining settled balance where applicable.
- Any settled discrepancy -> ReconciliationRequired or settlement adjustment.

Settlement does not create original payment finality. It reconciles value already confirmed, captured, or collected.

## Action Model

PaymentAction is the provider-neutral representation of required work before a decision.

| Action Type | Runtime Meaning | Completion Evidence |
|---|---|---|
| Redirect | Customer must visit backend-issued provider URL or hosted checkout. | Webhook, callback, status inquiry, or provider event. |
| EmbeddedCheckout | Customer must complete embedded iframe, payment element, pixel, or checkout component. | Webhook, callback, frontend result as evidence, status inquiry. |
| SDKConfirmation | Client SDK may confirm or start authentication, but finality remains backend-owned. | Provider webhook/status inquiry plus SDK result as low-trust evidence. |
| ReceiptUpload | Customer uploads proof for manual wallet, bank transfer, cash deposit, or offline payment. | Receipt evidence plus reviewer decision. |
| ReferencePayment | Customer pays using reference code/number. | Provider notification/status inquiry or manual settlement evidence. |
| QR | Customer scans or displays QR for wallet/bank/app payment. | Provider/wallet evidence or status inquiry. |
| WalletApproval | Customer approves wallet/request-to-pay/mobile authorization. | Provider/wallet confirmation evidence. |
| ReceiveShipment | Customer receives shipment and pays cash/COD. | Carrier/driver collection evidence admitted by Payment. |
| ManualReview | Authorized operator reviews proof, suspicious result, COD collection, refund, or settlement. | Reviewer evidence and lifecycle decision. |
| StatusRefresh | Runtime pulls provider/manual/settlement/refund status. | Status inquiry evidence and verification. |

Lifecycle:

- Issued by PaymentDecision or session initiation.
- Active until completed, expired, cancelled, superseded, or terminally invalidated.
- Expiration is explicit and bound to action/session/provider policy.
- Retry creates a new action or supersedes the old action through an audit-visible decision.
- Ownership remains Payment Runtime; UI renders only admitted actions.

## Evidence Model

ProviderEvidence supports:

- Webhook
- Callback
- Status Inquiry
- Frontend Result
- Receipt
- Carrier Evidence
- Driver Evidence
- Settlement Report
- Refund Evidence
- Manual Review

Admission requirements:

- Evidence must bind to tenant, PaymentSession, provider/method, and payable reference when available.
- Evidence must carry source, event type, received timestamp, occurred timestamp where known, idempotency key, payload hash, provider reference, amount/currency where relevant, and actor/source identity where relevant.
- Evidence with missing required scope is rejected or quarantined.
- Evidence from unknown provider sessions is unresolved evidence until recovery can bind or reject it.

Verification requirements:

- Signature, HMAC, or message-signature validation where provider supports it.
- Amount and currency validation against PaymentSession authority.
- Tenant resolution from trusted stored runtime state, not from raw inbound claims alone.
- Provider identity and provider-session validation.
- Replay protection using event ids, provider references, message hashes, action ids, webhook log ids, or equivalent keys.
- Duplicate detection and idempotent collapse.
- Ordering checks against state machine and provider event sequence where available.

Ordering:

- Terminal decisions dominate stale non-terminal evidence.
- Later evidence cannot rewrite immutable transactions.
- Earlier evidence received late is retained and either ignored, deduplicated, or quarantined.
- Provider event time is evidence; runtime received time and admitted decision time are authoritative for platform ordering.

Deduplication and idempotency:

- Duplicates collapse to the current PaymentSession state.
- Duplicate webhooks/callbacks/status pulls/manual reviews/uploads cannot create duplicate PaymentTransactions.
- Idempotency keys are scoped to tenant, session, attempt, provider, action, and operation.

Audit and retention:

- Raw sensitive payloads may be stored by secure reference or redacted hash.
- Evidence hash and verification result are retained.
- Rejected evidence remains auditable.
- Retention must satisfy payment, dispute, refund, settlement, tax, accounting, security, and provider obligations.

## Verification Model

PaymentVerification is mandatory before any evidence-driven lifecycle mutation.

Supported verification checks:

- Signature validation.
- HMAC validation.
- Message signature validation.
- Amount validation.
- Currency validation.
- Tenant validation.
- Replay protection.
- Duplicate detection.
- Ordering validation.
- Provider validation.
- Provider capability and constraint validation.
- PaymentSession state validation.
- Attempt/action expiration validation.
- Recovery eligibility validation.

Verification outcomes:

| Outcome | Meaning |
|---|---|
| Accepted | Evidence may produce a PaymentDecision. |
| Rejected | Evidence failed decisively and cannot mutate state. |
| Duplicate | Evidence was already admitted or collapsed. |
| IgnoredStale | Evidence is valid but too old to affect current state. |
| Quarantined | Evidence is plausible but mismatched or ambiguous. |
| ReconciliationRequired | Evidence crosses payment/settlement/provider uncertainty requiring controlled reconciliation. |
| RecoveryRequired | More provider/status/manual lookup is required before decision. |

Recovery:

- Recovery may create new evidence through status inquiry or operator review.
- Recovery does not edit prior verification.
- Recovery must explain why the new evidence supersedes or resolves ambiguity.

## Decision Model

PaymentDecision supports:

- Confirm
- Authorize
- Capture
- Reject
- Cancel
- Expire
- Refund
- Partial Refund
- Void
- Collect
- Quarantine
- Fail
- Settle
- Require Reconciliation
- Require Action
- Retry / Supersede Action

Decision authority:

- Automated decisions are made by Payment Runtime after verification and policy evaluation.
- Manual decisions are backend-bound reviewer intents admitted by Payment Runtime.
- Provider decisions are provider facts until verified and admitted.
- Checkout, Order, Shipping, Warehouse, frontend clients, projections, provider redirects, and caches cannot make PaymentDecisions.

Decision history:

- Every decision is immutable.
- Every state transition references a decision.
- Supersession is additive and audit-visible.
- Terminal-state replay collapses to current terminal state.

Decision audit:

- Decision must record actor/system, source evidence, verification result, prior state, next state, policy/capability checks, amount/currency effect, idempotency key, and reason code.

## Transaction Model

PaymentTransaction supports:

- Authorization
- Capture
- Sale
- Collection
- Refund
- Void
- Settlement
- Failure
- Adjustment
- Reversal
- ReconciliationMarker

Creation rules:

- Created only by admitted PaymentDecision.
- Must reference PaymentSession and amount/currency where financial.
- Must reference source evidence/verification unless policy-generated, such as expiration.
- Must not be created directly by provider adapter, frontend, Order, Shipping, Treasury, Accounting, or projection.
- Must not mutate prior transactions.

Ownership:

- Payment owns transaction recognition.
- Treasury owns cash movement records.
- Accounting owns ledger postings.
- Reconciliation owns matching/reporting according to certified boundaries.

Immutability:

- Posted transactions are immutable.
- Corrections use refund, void, reversal, adjustment, or reconciliation marker.
- Provider transaction ids are evidence references, not replacement identity.

## Settlement Model

Settlement supports:

- Provider Settlement
- Manual Settlement
- COD Settlement
- Treasury Integration
- Accounting Integration
- Reconciliation

Provider settlement:

- Consumes provider settlement report, transfer report, payout batch, clearing status, or status inquiry.
- Validates tenant, provider, session, provider transaction, amount, currency, fees where modeled, settlement date, and duplicate keys.

Manual settlement:

- Consumes bank deposit, cash deposit, wallet transfer, accounting evidence, or reviewer evidence.
- Requires authorized review and amount/currency validation.

COD settlement:

- Consumes carrier/driver collection evidence, cash handoff, remittance, deposit, shortage/overage, and reconciliation evidence.
- Delivery alone does not settle COD.

Treasury integration:

- Payment emits admitted settlement outcomes.
- Treasury records cash account movement, custody, deposit, transfer, and treasury reporting.
- Treasury cannot create Payment finality without Payment settlement admission.

Accounting integration:

- Payment emits immutable payment/refund/settlement outcomes.
- Accounting posts ledger entries and reconciliation entries.
- Accounting cannot rewrite PaymentSession state.

Reconciliation:

- Matches PaymentTransactions, ProviderEvidence, Settlement records, Treasury records, Accounting records, and provider reports.
- Mismatches produce ReconciliationRequired, Adjustment, or Recovery.

## Contracts

Contracts are provider-neutral responsibilities, not implementations.

### IPaymentRuntime

- Admit or replay PaymentSession.
- Enforce state machine transitions.
- Issue PaymentActions.
- Start and fence PaymentAttempts.
- Admit ProviderEvidence.
- Run verification and decision workflows.
- Create PaymentTransactions.
- Coordinate refund, capture, void, settlement, and recovery.
- Emit audit and events from SQL authority.

### IPaymentProvider

- Expose provider capabilities and constraints.
- Initiate provider sessions under runtime command.
- Return provider handoff evidence.
- Execute supported capture, void, refund, status inquiry, or settlement lookup commands.
- Normalize provider raw facts into ProviderEvidence candidates.
- Never mutate PaymentSession state directly.

### IPaymentVerification

- Authenticate evidence.
- Validate tenant, amount, currency, provider identity, provider session, ordering, replay, duplicate, and capability/constraint fit.
- Return immutable verification outcomes.

### IPaymentEvidenceAdmission

- Accept raw webhook/callback/status/manual/receipt/carrier/driver/settlement/refund evidence.
- Bind or quarantine evidence.
- Persist evidence hashes, payload references, idempotency keys, timestamps, and audit records.
- Route evidence to verification.

### IPaymentSettlement

- Admit provider/manual/COD settlement evidence.
- Validate settled amount/currency and references.
- Link settlement to PaymentTransactions.
- Emit payment settlement outcome for Treasury, Accounting, and Reconciliation.

### IPaymentRecovery

- Detect ambiguous or incomplete runtime state.
- Re-enter SQL PaymentSession authority.
- Perform status inquiry or evidence rebinding.
- Respect terminal states and retry fences.
- Quarantine or reconcile unresolved external authority.

### IPaymentDecisionPolicy

- Evaluate capability, constraints, current state, evidence, verification, tenant policy, risk, and financial rules.
- Produce decision candidates with reason codes.
- Refuse forbidden transitions.

### IPaymentActionCoordinator

- Create, expire, cancel, supersede, and complete PaymentActions.
- Ensure frontend/mobile/public clients receive only admitted action data.
- Prevent stale action reuse after terminal or superseded state.

### IPaymentTransactionWriter

- Create immutable PaymentTransactions from admitted decisions.
- Enforce amount/currency/reference rules.
- Provide transaction evidence for settlement, refund, accounting, treasury, and projection consumers.

### IPaymentAudit

- Append runtime audit records.
- Link sessions, attempts, actions, evidence, verifications, decisions, transactions, settlements, refunds, captures, voids, and recoveries.
- Preserve forensic replay without making audit the mutable state authority.

### IPaymentProjectionPublisher

- Publish SQL-derived events and read-model updates after committed decisions.
- Ensure projections are derivative and rebuildable.
- Prevent projection state from becoming Payment authority.

## Extensibility Review

The runtime supports future extensions without redesign because all external behavior enters through capabilities, constraints, actions, evidence, verification, decisions, and transactions.

| Future Need | Runtime Fit |
|---|---|
| Future Providers | New IPaymentProvider adapter, capabilities, constraints, evidence mappings, and verification rules. |
| Future Wallets | Wallet approval/QR/reference actions plus provider evidence and status inquiry. |
| Future BNPL | Authorization/capture/settlement/refund states plus provider-specific evidence mapped to canonical states. |
| Future QR | QR PaymentAction and wallet/provider Evidence. |
| Future Bank Transfer | ReceiptUpload or ReferencePayment action, manual/provider evidence, settlement admission. |
| Future Offline Payments | Receipt, manual review, cash/deposit evidence, collection and settlement transactions. |
| Future Marketplace | PaymentSession payable references can bind order groups, suborders, merchant splits, commission candidates, settlement/payout consumers, and reconciliation without changing payment finality rules. |
| Future Mobile Apps | Mobile clients consume PaymentActions and submit evidence candidates; no client finality. |
| Future Public APIs | Public APIs can create payable candidates, retrieve actions, submit evidence, and poll status under runtime contracts without provider-specific state ownership. |

## Architecture Decisions

1. PaymentSession is the canonical aggregate name; current `PaymentIntent` implementations are compatibility names.
2. ProviderSession is evidence, not platform authority.
3. PaymentAction is the only runtime abstraction for customer/operator/provider/fulfillment work.
4. ProviderEvidence must always pass PaymentVerification before lifecycle mutation.
5. PaymentDecision is the only source of canonical state transitions.
6. PaymentTransaction is immutable financial recognition.
7. Settlement is separate from confirmation, capture, and collection.
8. Refund, Capture, Void, and Recovery are runtime workflows under the same state machine.
9. Provider-specific statuses are forbidden as runtime states.
10. Frontend, mobile, public API, cache, projection, redirect, SDK result, receipt, delivery event, and provider raw payload are never Payment finality by themselves.
11. Recovery must preserve external-provider ambiguity rather than retrying blindly.
12. `.ai-context` does not need an update because no new authority truth was discovered beyond PAYMENT-0.7.

## Certification Decision

Runtime Model completed: Yes.

Objects defined: Yes.

Relationships defined: Yes.

State Machine completed: Yes.

Evidence Model completed: Yes.

Verification Model completed: Yes.

Decision Model completed: Yes.

Transaction Model completed: Yes.

Settlement Model completed: Yes.

Contracts completed: Yes.

Provider-neutral: Yes.

Future-proof: Yes.

Runtime-ready: Yes, as canonical architecture for implementation phases. No runtime code was changed.

Recommended next phase: PAYMENT-0.9 Provider Adapter Contract Certification, followed by provider-specific runtime migration phases for Stripe, Paymob, Fawry, Manual/Vodafone Cash, COD, Refund, Settlement, and Recovery adapters.

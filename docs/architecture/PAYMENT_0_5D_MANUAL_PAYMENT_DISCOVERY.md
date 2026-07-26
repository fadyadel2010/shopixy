# PAYMENT-0.5D Manual Payment Workflow Discovery

Date: 2026-07-10
Scope: Manual Payment, Manual Wallet, Vodafone Cash, Bank Transfer, Cash Deposit, receipt/proof upload, manual verification, approval, rejection, expiration, cancellation, refund, audit, notifications, administration, and storefront UX
Status: DISCOVERY COMPLETE - NO RUNTIME CHANGE

## Executive Summary

Manual Payment was reviewed against the certified PAYMENT-0 provider-neutral architecture, the prior PAYMENT-0.5A Stripe, PAYMENT-0.5B Paymob, and PAYMENT-0.5C Fawry discoveries, and the current PointOfSale implementation.

Manual Payment is not an API provider. In the current project it is implemented primarily as `ManualWallet`, with Vodafone Cash, Instapay, and Orange Cash exposed as wallet-type variants inside a backend-owned payment workflow. The implementation is architecture-compatible with PAYMENT-0 because payment authority remains in backend SQL `PaymentIntent`, `Order`, `IPaymentLifecycleService`, tenant-scoped payment-method configuration, private proof storage, and admin approval/rejection endpoints. Storefront owns only method selection, sender-wallet candidate entry, proof-upload intent, retry, refresh, navigation, and admitted DTO presentation.

Certification decision: **FIRST-CLASS PROVIDER-COMPATIBLE WITH WORKFLOW GAPS**.

No runtime, backend, frontend, API, migration, refactor, test, or configuration change was applied.

Primary findings:

- Current Manual Payment is production-certifiable for the existing Manual Wallet/Vodafone Cash approval and rejection workflow.
- Manual Payment can operate as a first-class provider inside PAYMENT-0 if it is modeled as a backend-owned workflow provider rather than an external API adapter.
- The current implementation supports customer proof upload, tenant-scoped private evidence storage, backend proof admission, pending review, admin proof preview, approval, rejection, ledger-first collection, rejection reason, notification events, concurrency protection, and cleanup.
- The implementation is not a complete enterprise manual-payment workflow yet. It lacks first-class reviewer assignment, reviewer notes/history, explicit transfer/reference metadata, duplicate receipt detection, evidence hashing, immutable evidence versions, search/filter administration, explicit manual expiration endpoint, explicit manual cancellation endpoint, and manual refund execution workflow.
- Bank Transfer and Cash Deposit are not first-class manual payment subtypes in the inspected Storefront Manual Wallet implementation. They appear elsewhere as payout/refund/accounting concepts, but not as complete customer manual-payment methods with transfer instructions and evidence capture.
- Fraud controls are mostly authority-boundary controls, not fraud-detection controls. The system prevents frontend finality and storage tampering, but does not identify duplicate receipts, reused references, amount mismatches from OCR/provider data, or multi-order proof replay.
- Runtime update is not required to preserve PAYMENT-0 compatibility, but a future Manual Payment Workflow Expansion phase is recommended before treating Manual Payment as a full enterprise operations module.

## Current Implementation

Inspected implementation surfaces:

- `PointOfSale.API/Controllers/StorefrontManualWalletController.cs`
- `PointOfSale.API/Controllers/AdminManualPaymentController.cs`
- `PointOfSale.API/Services/Payments/Adapters/ManualWalletPaymentAdapter.cs`
- `PointOfSale.API/Services/Payments/ManualWalletStateMachine.cs`
- `PointOfSale.API/Services/ManualWalletProofStorage.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.API/Jobs/ManualWalletProofCleanupJob.cs`
- `PointOfSale.Core/Entities/PaymentIntent.cs`
- `PointOfSale.Core/Enums/PaymentIntentStatus.cs`
- `PointOfSale.API/DTOs/Admin/AdminManualPaymentDtos.cs`
- `PointOfSale.API/Events/ManualPaymentEvents.cs`
- `PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Web/src/pages/ManualPayments.jsx`
- `PointOfSale.Tests/Architecture/ManualPaymentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/VodafoneCashPaymentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ManualWalletPaymentArchitectureTests.cs`
- `PointOfSale.Tests/Integration/ManualWalletPaymentConcurrencyIntegrationTests.cs`

Current implemented flow:

```text
Storefront selects MANUALWALLET
  -> backend exposes tenant manual wallet configuration and upload rules
  -> customer selects wallet type and enters sender wallet number
  -> customer uploads receipt/proof through backend-issued manual-wallet upload token
  -> backend validates file and stores tenant-scoped private object key
  -> checkout submission sends wallet type, sender wallet candidate, and proof key candidate
  -> backend revalidates session, tenant, customer/cart/order, expected pricing, payment method, and proof-session binding
  -> SQL PaymentIntent is created with backend amount/currency/reference/provider and proof key
  -> ManualWalletPaymentAdapter transitions the intent to AwaitingVerification
  -> Storefront receives only pending manual-wallet session evidence
  -> POS Admin lists pending manual-wallet payments
  -> POS Admin previews proof through a signed short-lived private-storage redirect
  -> Owner/Admin approves or rejects
  -> IPaymentLifecycleService owns final payment/order mutation and events
```

Durable fields currently modeled on `PaymentIntent`:

- `Provider = PaymentProvider.ManualWallet`
- `PaymentReference`
- `Amount`
- `Currency`
- `Status`
- `WalletType`
- `WalletPhoneNumber`
- `ProofImageUrl`
- `VerifiedAtUtc`
- `VerifiedByAdmin`
- `RejectionReason`
- `ProofImageExpiresAtUtc`
- `ProofImageDeleted`
- `ProviderMetadata`
- `IdempotencyKey`
- `RowVersion`

## Workflow Analysis

| Workflow Step | Current Support | Notes |
|---|---:|---|
| Create Manual Session | Supported | Checkout creates SQL `PaymentIntent`; upload is bound to checkout session/recovery nonce. |
| Display Instructions | Supported | Backend builds wallet instructions from tenant payment-method config and amount/currency. |
| Display Transfer Number | Supported | Merchant wallet number is backend-derived and displayed to Storefront. |
| Reference Number | Partial | Backend `PaymentReference` exists; customer transfer reference is not separately modeled. |
| Amount | Supported | Backend uses current order total, not frontend amount. |
| Currency | Supported | Backend validates currency consistency. |
| Expiration | Partial | Proof cleanup and intent expiration states exist; no first-class manual session expiry administration surfaced. |
| Receipt Upload | Supported | Private upload API with token, file validation, tenant-scoped object key, replacement, and clearing. |
| Customer Notes | Partial | Checkout has order notes, but no dedicated manual-payment customer notes/reviewer notes model. |
| Pending Review | Supported | `AwaitingVerification` and admin pending list exist. |
| Reviewer Assignment | Not implemented | No assignment, queue ownership, or four-eyes review workflow found. |
| Approve | Supported | Owner/Admin route, state machine, lifecycle service, ledger-first accounting/outbox. |
| Reject | Supported | Owner/Admin route, required reason, lifecycle service, rejection event. |
| Expire | Partial | Status supports `Expired`; cleanup supports proof expiration; no explicit manual review expiry workflow found. |
| Cancel | Partial | State machine permits cancellation from pending/awaiting verification; no dedicated Manual Payment cancel endpoint found. |
| Refund | Partial | PAYMENT-0 lifecycle and refund authority exist generally; no Manual Payment-specific refund execution/review path found. |
| Audit | Partial | Events, order status events, correlation/idempotency, proof cleanup logs, and lifecycle fields exist; no complete manual payment audit history table found. |

## Customer Experience

Supported:

- Manual Wallet appears in checkout as a payment method lane.
- Customer can choose Vodafone Cash, Instapay, or Orange Cash.
- Customer sees merchant wallet number and transfer instructions.
- Customer enters sender wallet number.
- Customer uploads image/PDF proof with size/type checks.
- Customer can replace or clear proof before checkout submission.
- Storefront requires manual-wallet response evidence before presenting pending state.
- Recovery clears manual wallet type, proof key, and upload token, requiring re-entry.

Gaps:

- No explicit post-order customer progress page with a manual-payment timeline was confirmed.
- No customer-facing rejected/expired retry workflow with replacement proof after order creation was confirmed.
- No dedicated customer cancellation flow for pending manual review was confirmed.
- No structured transfer reference, transfer time, sender name, receiver number confirmation, or transfer amount input was confirmed.

## Administration Review

Supported:

- Pending manual payments list.
- Pagination.
- Order link.
- Customer name, amount, currency, wallet type, sender wallet number, proof presence, created date.
- Proof preview for images and PDFs through backend proof-image endpoint.
- Approval and rejection actions.
- Rejection reason validation.
- Owner/Admin-only financial write authority.
- Manager can view pending/proof, but cannot approve/reject.
- Idempotency headers on admin frontend actions.
- Optimistic concurrency protection at the backend.

Gaps:

- No search/filtering by order number, customer, amount, date range, wallet type, status, reviewer, or reference.
- No reviewer assignment, queue ownership, or SLA view.
- No reviewer notes separate from rejection reason.
- No immutable audit history view per payment.
- No manual retry/reopen/replacement-proof administration workflow.
- No dedicated refund operation for manually approved payments.
- No duplicate proof/reference warning UI.
- No amount/currency mismatch review UI beyond backend amount/currency authority.

## Payment Evidence Review

| Evidence Type | Current Support | Notes |
|---|---:|---|
| Receipt Images | Supported | Image upload and admin preview. |
| PDF Receipt | Supported | Storefront and admin preview support PDF. |
| Bank Receipt | Not first-class | Could be uploaded as proof, but no bank-transfer metadata model exists. |
| Wallet Receipt | Supported as file evidence | Structured receipt data is not extracted. |
| Transaction Reference | Partial | Backend payment reference exists; customer transfer reference is not separately captured. |
| Transfer Number | Supported for sender wallet number | Stored as `WalletPhoneNumber`; validation is broad. |
| Sender Number | Supported for wallet flows | Same field as sender/transfer number. |
| Receiver Number | Supported as merchant wallet config | Not copied as a dedicated immutable field except via provider metadata/instructions. |
| Transfer Time | Not implemented | No structured transfer timestamp field. |
| Transfer Amount | Backend-supported, customer evidence absent | Order amount is authoritative; customer-entered transfer amount not modeled. |
| Metadata | Partial | Provider metadata stores merchant wallet/instructions; no rich evidence metadata. |
| Evidence Integrity | Partial | Tenant-scoped private key, no external URLs, file validation, storage cleanup. No content hash/dedup/versioning found for manual wallet proof. |

## Architecture Compatibility

Manual Payment is compatible with PAYMENT-0 under these rules:

- Treat Manual Payment as a backend-owned workflow provider.
- Do not treat it as an external API provider.
- Keep the durable session/attempt as SQL `PaymentIntent`.
- Keep customer proof and transfer data as candidate evidence until backend admission.
- Keep approval/rejection under `IPaymentLifecycleService`.
- Keep Storefront and Admin UI as presentation/backend-bound intent only.
- Keep accounting, ledger, settlement, refund, notifications, and order status changes behind lifecycle authority.

Compatibility strengths:

- Provider-neutral `IPaymentAdapter` includes `ManualWalletPaymentAdapter`.
- SQL `PaymentIntent` stores manual evidence and status.
- `ManualWalletStateMachine` guards manual-wallet status transitions.
- `IPaymentLifecycleService` is the only approval/rejection mutation path.
- Admin controller no longer uses obsolete direct `PaymentService` manual mutation methods.
- `PaymentStatusSaveChangesInterceptor` preserves lifecycle ownership.
- `RowVersion` and integration tests cover concurrent admin approval.
- Storefront does not expose approve/reject endpoints or accepted-payment authority.

Compatibility limits:

- The canonical Manual Payment workflow is broader than the current Manual Wallet implementation.
- Manual Bank Transfer and Cash Deposit are not first-class storefront/admin manual-payment variants.
- Manual refund, expiration, cancellation, and retry are not first-class workflow operations.
- Evidence is stored as a single proof pointer rather than an immutable evidence aggregate.

## Capability Matrix

| Capability | Current Project | Recommendation |
|---|---:|---|
| Supports Manual Approval | Yes | Keep lifecycle-owned. |
| Supports Manual Rejection | Yes | Keep required reason and lifecycle-owned. |
| Supports Receipt Upload | Yes | Add immutable evidence metadata/hash/versioning later. |
| Supports Reviewer Assignment | No | Add workflow queue model in future phase. |
| Supports Expiration | Partial | Add explicit manual expiration policy and job/endpoint. |
| Supports Cancellation | Partial | Add customer/admin cancellation workflow if product needs it. |
| Supports Refund | Partial | Add Manual Payment refund execution and evidence workflow. |
| Supports Audit | Partial | Add append-only manual-payment audit/history. |
| Supports Notifications | Partial | Events exist; operational notification coverage should be certified separately. |
| Supports Evidence | Yes | Current file evidence is accepted; structured evidence is incomplete. |
| Supports Attachments | Single proof | Add multi-attachment evidence if needed. |
| Supports Customer Notes | Partial | Order notes exist; dedicated manual-payment notes absent. |
| Supports Retry | Partial | Checkout recovery/re-entry exists; post-order replacement proof/retry absent. |
| Supports Vodafone Cash | Yes | As Manual Wallet wallet type. |
| Supports Manual Wallet | Yes | Current implementation center. |
| Supports Bank Transfer | Not first-class | Add as manual subtype with bank account instructions and receipt metadata. |
| Supports Cash Deposit | Not first-class | Add as manual subtype with branch/deposit slip metadata. |
| Supports Duplicate Detection | No | Add proof hash/reference duplicate controls. |
| Supports Amount/Currency Review | Backend amount/currency only | Add evidence-level mismatch review if extracting receipt metadata. |

## Provider Constraints

Manual Payment has provider-specific constraints even though it is not an API provider:

- Human approval is required before money is recognized.
- Reviewer availability and business hours affect time-to-approval.
- Maximum waiting period should be explicit.
- Proof validity rules must be documented per subtype.
- Image/PDF requirements are enforced for upload, but evidence content requirements are not structured.
- Duplicate detection is required for enterprise fraud resistance.
- Manual fraud risk is high without independent reference/source verification.
- Sender number, receiver number, amount, currency, transfer time, and transaction reference should become structured evidence where possible.
- Bank Transfer and Cash Deposit need distinct instructions and metadata from mobile-wallet flows.
- Reviewer permissions should remain stricter than view permissions.

## Canonical Runtime Mapping

| Canonical Runtime Concept | Manual Payment Equivalent | Current Project Mapping | Gap / Recommendation |
|---|---|---|---|
| Payment Session | Manual payment request | SQL `PaymentIntent` | Compatible. |
| Provider Session Id | Manual request id | `PaymentIntent.Id` / `PaymentReference` | Compatible; consider explicit manual request number. |
| Provider | Manual workflow provider | `PaymentProvider.ManualWallet` | Add broader `ManualPayment` subtype model if Bank Transfer/Cash Deposit become first-class. |
| Payment Method | Manual Wallet / Vodafone Cash / Bank Transfer / Cash Deposit | `MANUALWALLET` plus `WalletType` | Wallet variants supported; bank/cash variants absent. |
| Customer Action | Upload receipt/proof after transfer | Storefront proof upload | Compatible. |
| Provider Confirmation | Reviewer approval | Admin confirm route + `WalletApproval` lifecycle source | Compatible. |
| Provider Rejection | Reviewer rejection | Admin reject route + `WalletRejection` lifecycle source | Compatible. |
| Async Event | Reviewer decision | Lifecycle event/outbox | Compatible. |
| Settlement | Manual settlement / clearing account | Wallet approval posts wallet-approved accounting | Partial; settlement/reconciliation detail should be expanded. |
| Refund | Manual refund | General refund lifecycle exists | Manual-specific execution/evidence workflow missing. |
| Expiration | Manual request timeout | Status exists; proof cleanup exists | First-class expiry policy/transition incomplete. |
| Cancellation | Customer/admin cancel before approval | Status/state machine partial | Dedicated endpoint/workflow absent. |
| Evidence | Receipt/proof file and sender number | `ProofImageUrl`, `WalletPhoneNumber` | Add structured evidence, hash, duplicate detection, immutable versions. |
| Audit | Manual payment history | Events/logs/lifecycle fields | Add append-only admin review history. |

## Current Gaps

1. Manual Payment is narrower than requested scope.
   - Current runtime is Manual Wallet, mainly Vodafone Cash/Instapay/Orange Cash.
   - Bank Transfer and Cash Deposit are not first-class storefront/admin manual-payment methods.

2. Evidence model is too thin for enterprise manual payment.
   - Single `ProofImageUrl` and sender wallet number are not enough for strong review.
   - Missing structured transaction reference, transfer amount, transfer time, sender/receiver details, and receipt metadata.

3. Evidence integrity is incomplete.
   - Private tenant-scoped storage and file validation are strong.
   - No content hash, duplicate detection, immutable evidence versioning, or multi-order reuse detection was found.

4. Administration is operationally useful but not workflow-complete.
   - Pending list, proof preview, approve/reject exist.
   - Search, filtering, assignment, reviewer notes, audit history, SLA, retry, replacement proof, and refund actions are absent.

5. Manual lifecycle is not fully surfaced.
   - `Pending`, `AwaitingVerification`, `Paid`, `Rejected`, `Cancelled`, and `Expired` exist at status level.
   - Waiting for customer, waiting for review, refunded, replacement proof, customer cancellation, and manual expiration are not first-class workflow states/screens.

6. Fraud review is mostly manual and unassisted.
   - Reviewers can inspect proof, amount, currency, sender number, and order.
   - No automated duplicate receipt/reference/sender/order pattern detection was found.

7. Reviewer governance is incomplete.
   - Approval/rejection require Owner/Admin, which is good.
   - No assignment, separation of duties, two-person approval, or reviewer workload controls were found.

8. Manual refund is not provider-specific.
   - Payment/refund architecture exists generally.
   - A manual refund workflow with payout method, refund proof, reviewer evidence, and accounting reconciliation was not found.

## Security Review

Strong alignments:

- Storefront cannot approve or reject manual payments.
- Uploads require a backend-issued manual-wallet upload token.
- Upload token is bound to checkout session and recovery nonce.
- Uploads are validated by content type/profile and size.
- Proof keys must be tenant-scoped private object keys.
- External absolute proof URLs are rejected.
- Admin proof access is tenant-scoped and uses short-lived signed download URLs.
- Approval/rejection requires Owner/Admin.
- Manager can view but not financially approve/reject.
- Approval/rejection route through `IPaymentLifecycleService`.
- Payment status mutation is protected by authorized lifecycle context.
- Concurrent approval is guarded by optimistic concurrency.
- Obsolete direct manual-payment mutation methods are disabled.

Security gaps/recommendations:

- Add content hash and duplicate detection for uploaded proof files.
- Add immutable proof evidence records rather than relying on a mutable `ProofImageUrl` pointer.
- Add reviewer audit history with actor, timestamp, action, previous/new state, notes, and evidence version.
- Add structured evidence validation for amount, currency, transfer reference, sender, receiver, and transfer time where possible.
- Add explicit customer replacement-proof workflow after rejection without letting frontend alter accepted historical evidence.
- Add proof retention and legal/audit retention policy by status.
- Add suspicious/quarantine workflow for duplicate or mismatched evidence.

## Fraud Review

Confirmed fraud risks:

- Duplicate receipt can be reused across multiple orders because no proof hash/dedup control was found.
- Same transaction reference can be reused because no structured transaction reference field exists.
- Same sender number may submit multiple proofs/orders without risk scoring.
- Receipt image can show a different amount/currency, but the system does not extract and compare proof content.
- Customer can upload proof before actual transfer; human review is the only confirmation.
- Reviewer may approve based on incomplete evidence because the admin surface lacks required structured evidence checklist.
- Manual approval is an insider-risk path; no four-eyes or assignment separation was found.

Mitigated by current architecture:

- Frontend cannot mark the order paid.
- Manual approval requires backend admin authorization.
- Lifecycle/ledger authority prevents money recognition without the approved path.
- Tenant-scoped private storage limits proof tampering and cross-tenant leakage.

## Migration Opportunities

Recommended future phases:

1. **PAYMENT-0.6 Manual Payment Workflow Model**
   - Define Manual Payment as a provider-neutral workflow provider with subtypes: Manual Wallet, Vodafone Cash, Bank Transfer, Cash Deposit.
   - Add canonical manual payment request/evidence/review concepts without changing external API-provider contracts.

2. **PAYMENT-0.7 Manual Evidence Integrity**
   - Add immutable proof/evidence records, content hash, metadata, duplicate detection, evidence versioning, and retention policy.

3. **PAYMENT-0.8 Manual Administration Workflow**
   - Add search/filtering, assignment, reviewer notes, audit history, SLA, status buckets, replacement-proof review, and suspicious queue.

4. **PAYMENT-0.9 Manual Lifecycle Expansion**
   - Add explicit waiting-for-customer, waiting-for-review, expired, cancelled, retry/replacement proof, and manual refund workflows.

5. **PAYMENT-1.0 Manual Refund and Reconciliation**
   - Add manual refund execution with method, payout/evidence, approval, accounting, customer notification, and reconciliation.

6. **PAYMENT-1.1 Manual Fraud Controls**
   - Add duplicate receipt/reference detection, reused sender/receiver analysis, mismatch checklist, reviewer separation, and risk flags.

## Runtime Impact

Immediate runtime update required: **No**.

Manual Payment remains compatible with PAYMENT-0 today for the current Manual Wallet/Vodafone Cash workflow. Runtime updates are recommended only for expanding Manual Payment into a complete enterprise manual-payment provider with Bank Transfer, Cash Deposit, richer evidence, administration, fraud controls, expiration/cancellation, and manual refund execution.

## Compatibility Scores

Scores are intentionally separate; no overall score is calculated.

| Score Area | Score | Rationale |
|---|---:|---|
| Architecture Compatibility | 88/100 | Backend authority, SQL `PaymentIntent`, lifecycle approval/rejection, tenant-scoped evidence, provider-neutral adapter shape, and Storefront presentation-only boundaries are strong. Points lost because the provider is named `ManualWallet` rather than a broader Manual Payment workflow with first-class subtypes. |
| Workflow Completeness | 55/100 | Create, instructions, upload, pending review, approve, reject, and proof preview exist. Assignment, structured evidence, expiration, cancellation, retry/replacement proof, bank transfer, cash deposit, and manual refund workflows are incomplete. |
| Security Alignment | 76/100 | Upload-token binding, file validation, private tenant-scoped storage, short-lived proof access, role gates, lifecycle-only mutation, and concurrency are strong. Missing immutable evidence, duplicate detection, audit history, and structured mismatch controls reduce alignment. |
| Administration Readiness | 48/100 | Pending list, proof preview, approval, rejection, and pagination exist. Search/filtering, assignment, reviewer notes, audit history, refund, retry, SLA, and fraud queue are missing. |
| Future Runtime Readiness | 72/100 | PAYMENT-0 can absorb Manual Payment cleanly as a workflow provider. Existing lifecycle and evidence boundaries are good foundations, but durable evidence and subtype modeling need planned expansion. |

## Recommendations

1. Do not change runtime in PAYMENT-0.5D.
2. Certify the existing implementation as first-class provider-compatible for Manual Wallet/Vodafone Cash-style flows only.
3. Do not claim Bank Transfer or Cash Deposit are complete manual-payment providers until subtype-specific instructions, evidence fields, admin review, and reconciliation are implemented.
4. Add an explicit Manual Payment Workflow architecture phase before runtime expansion.
5. Prioritize immutable evidence records, proof hash, duplicate detection, and review audit before expanding volume.
6. Add reviewer assignment, notes, search/filtering, and audit history before treating administration as enterprise-complete.
7. Add explicit expiration, cancellation, replacement-proof retry, and manual refund workflows as lifecycle extensions.
8. Preserve the current rule that Storefront cannot mark manual payment accepted, paid, verified, or approved.
9. Preserve lifecycle/ledger-first approval as non-negotiable.

## Certification Decision

**PAYMENT-0.5D Manual Payment Discovery Decision: FIRST-CLASS PROVIDER-COMPATIBLE WITH WORKFLOW GAPS.**

Manual Payment can operate as a first-class provider inside the provider-neutral Payment Platform if it remains a backend-owned workflow provider and not an API provider. The current implementation is compatible for Manual Wallet/Vodafone Cash-style proof-and-review payments. It is not yet a complete enterprise Manual Payment workflow for Bank Transfer, Cash Deposit, rich evidence, assignment, fraud detection, expiration/cancellation, or manual refund execution.

## Success Answers

| Question | Answer |
|---|---|
| Reviewed current implementation? | Yes. Manual Wallet controllers, services, adapter, state machine, proof storage, checkout/payment/lifecycle paths, Storefront UI, Admin UI, cleanup job, entities, DTOs, events, and regression tests were reviewed. |
| Workflow architecture compatible? | Yes. Compatible for backend-owned Manual Wallet/Vodafone Cash workflow. |
| Security issues found? | Yes. No immediate critical authority break, but evidence integrity, duplicate detection, immutable audit, and structured fraud controls are incomplete. |
| Fraud risks identified? | Yes. Duplicate receipts, reference reuse, replay across orders, amount/currency mismatch, manual reviewer error, and insider approval risks were identified. |
| Administration complete? | No. Pending list, proof preview, approve, and reject exist; assignment, notes, audit history, search/filtering, retry, and refund are incomplete. |
| Migration recommended? | Yes. Migrate from narrow Manual Wallet to a broader Manual Payment workflow provider model with subtypes and evidence integrity. |
| Runtime update required? | No immediate runtime update required for current compatibility. Runtime expansion is recommended for enterprise completeness. |
| Canonical Runtime Mapping completed? | Yes. Manual Payment concepts were mapped to Payment Session, provider session id, customer action, reviewer decision, async event, settlement, refund, expiration, cancellation, evidence, and audit concepts. |
| Immediate action required? | No immediate code action required unless Bank Transfer/Cash Deposit/full manual workflow is being advertised as complete today. |
| Recommended next phase? | PAYMENT-0.6 Manual Payment Workflow Model and Evidence Integrity Design. |

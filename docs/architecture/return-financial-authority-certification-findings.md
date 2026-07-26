# PASS M5-F Return Financial Authority Certification Findings

Date: 2026-06-22

Status: Return Financial Authority is production-certifiable for the inspected frontend runtime after POS return financial payload hardening.

## Financial Artifacts

Actual implemented return-domain financial artifacts inspected:

- CommerceHub/Super Admin refund-return artifacts: `amount`, `currency`, `quantity`, `unitPriceSnapshot`, `order.totalAmount`, `order.shippingTotal`, `item.unitPrice`, `item.totalPrice`, `item.platformCommissionAmount`, `item.vendorEarningAmount`, `financialPreview.commissionReversalAmount`, `financialPreview.vendorEarningChange`, `financialPreview.paymentRefundedAmountBefore`, `financialPreview.paymentRefundedAmountAfter`, `financialPreview.vendorNetEarningsBefore`, `financialPreview.vendorNetEarningsAfter`, `financialPreview.platformCashDelta`, `financialPreview.platformPayableDelta`, `financialPreview.vendorDebtDelta`, `financialPreview.commissionReceivableDelta`, `financialPreview.vendorPayableBefore`, `financialPreview.vendorPayableAfter`, `financialPreview.vendorDebtBefore`, `financialPreview.vendorDebtAfter`, `execution.financialRefundId`, `execution.commissionReversalAmount`, and `execution.vendorEarningReversalAmount`.
- CommerceHub return artifacts: `returnId`, `refundId`, `orderItemId`, `customerId`, `vendorId`, `refundExecutionTrigger`, and `isExecutionTriggerReached`. The inspected CommerceHub return DTO carries no independent return money field.
- POS sales return artifacts: invoice `unitPrice`, `taxRate`, discounts, tax, subtotal, total, `RefundMethod`, `CashAmount`, `CardAmount`, `RefundAmount`, and `CustomerCreditAmount`. Runtime now submits refund destination only; backend derives amounts from locked original invoice authority.
- POS purchase return artifacts: invoice `unitPrice`, `taxRate`, `TotalAmount`, `RefundAmount`, supplier ledger debit, product cost/value adjustment, and stock movement price. Runtime now submits identity/quantity intent only; backend derives all financial values from locked original purchase invoice and inventory authority.

## Financial Findings

- Runtime gap found and fixed: POS return create payloads submitted frontend money fields. Sales returns sent line `unitPrice`, `taxRate`, `cashAmount`, and `cardAmount`; purchase returns sent line `unitPrice`, `taxRate`, `refundToCash`, and `refundAmount`.
- Runtime hardening removed those fields from frontend payloads, removed editable POS refund amount controls, derived sales cash/card payout amounts from backend-calculated return totals, and excluded ignored client money from return idempotency fingerprints.
- Super Admin return financial presentation remains refund-bound evidence validated by `requireRefundFinancialPresentation` and current list/detail selection.

## Domain Findings

- Customer findings: Customer refund/order return-linked surfaces cannot create, recalculate, or override return money. Customer refund creation remains amount-free intent against current backend order-item authority; stale money is withheld by current order/refund validation.
- Vendor findings: Vendor refund impacts are read-only current-vendor financial evidence. Vendor surfaces cannot create balances, modify balances, restore balances, or preserve stale return money after current authority loss.
- Admin findings: Monitoring, selected rows, details, execution dialogs, and return dialogs remain current-list/current-detail-bound. Monitoring cannot become financial authority; dialogs close/withhold during refresh, failure, or mismatch.
- Refund consistency findings: Return financial presentation is bound to backend refund amount/currency/execution evidence. Return money cannot exceed refund authority in the inspected frontend because frontend return paths do not compute CommerceHub return money.
- Order consistency findings: Order totals are display evidence only. POS return totals are recomputed by backend from locked original invoices; frontend order totals cannot create or override return money.
- Payment consistency findings: Payment financials and callbacks cannot create return money. Payment status/provider evidence remains refund/payment evidence only.
- Inventory consistency findings: Frontend cannot compute authoritative inventory reversal value. POS purchase return inventory/cost impact is server-derived from locked purchase invoice and product/stock authority.
- Settlement consistency findings: Frontend cannot compute settlement, vendor earning, commission, or ledger value. Super Admin deltas are validated backend evidence only.
- Bootstrap findings: Initial load, refresh, reconnect, focus, visibility, history, and BFCache may refetch or withhold only. They cannot preserve return financial authority.
- Cache findings: React Query, local component state, dialogs, selected rows, route state, history state, localStorage, and sessionStorage cannot restore money. Return create payloads no longer carry frontend money.
- Failure findings: 401, 403, 404, 409, 422, malformed DTOs, failed reads, and failed mutations cannot create replacement money. Malformed financial DTOs fail closed before presentation.
- Adversarial findings: Financial replay, storage replay, history replay, route replay, selection replay, mutation replay, actor replay, and tenant replay cannot fabricate return money or bypass backend financial authority on inspected paths.

## Runtime Hardening Performed

- Removed `unitPrice` and `taxRate` from POS sales return frontend create payloads.
- Removed `cashAmount`, `cardAmount`, mixed refund allocation controls, and editable immediate refund amount state from POS sales return frontend create flow.
- Changed POS sales return backend payout resolution so cash/card payout amount is derived from the backend-calculated return total when the frontend supplies only destination intent.
- Removed `unitPrice`, `taxRate`, `refundToCash`, and `refundAmount` from POS purchase return frontend create payloads.
- Made POS purchase return unit price and tax rate read-only presentation.
- Removed ignored client money from sales and purchase return idempotency fingerprints.

## Files Modified

- `PointOfSale.Web/src/pages/SalesReturns.jsx`
- `PointOfSale.Web/src/pages/PurchaseReturns.jsx`
- `PointOfSale.API/Controllers/SalesReturnsController.cs`
- `PointOfSale.API/Controllers/PurchaseReturnsController.cs`
- `PointOfSale.Tests/Architecture/ReturnFinancialAuthorityCertificationTests.cs`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/return-financial-authority-certification-findings.md`

## Regression Freezes Added

- `ReturnFinancialAuthorityCertificationTests`

## Architecture Updates

- Added M5-F entries to all five authority context files.
- Added this findings artifact.

## Explicit Certification Answers

- Who owns return financial authority? Backend.
- Can frontend fabricate money? No.
- Can customer create money? No.
- Can vendor create balances? No.
- Can monitoring become financial authority? No.
- Can return money exceed refund authority? No inspected frontend path can make it do so.
- Can order totals create return money? No; order totals are evidence, and POS return money is backend-derived.
- Can payment money create return money? No.
- Can frontend compute inventory value? No authoritative inventory value; backend owns inventory reversal value.
- Can frontend compute settlement value? No.
- Can stale money survive refresh? No inspected authority path permits stale money to remain authoritative.
- Can cache restore money? No.
- Can replay restore money? No.
- Can tenant leakage occur? No inspected frontend path permits return financial tenant leakage; backend endpoint authorization remains decisive.
- Were runtime changes required? Yes.
- Is Return Financial Authority production-certifiable? Yes, for the inspected frontend runtime paths.

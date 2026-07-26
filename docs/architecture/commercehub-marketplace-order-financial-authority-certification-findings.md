# PASS M2-I Marketplace Order Financial Authority Certification

Marketplace Order Financial Authority is production-certifiable for the inspected CommerceHub checkout, cart, customer orders/details/recovery/refunds, POS vendor financial/earnings/settlement views, Super Admin order/finance monitoring, split-order, bootstrap, refresh, cache, failure, and adversarial frontend runtime paths.

## Financial findings

Order totals, subtotal, shipping, split totals, payment handoff amounts, and refund amounts are accepted only from coherent current backend DTOs. The frontend validates evidence and formats it; it does not calculate, repair, or persist financial truth.

## Customer findings

Customer list, detail, recovery, checkout-result, payment-retry, and refund DTOs fail closed for malformed or contradictory money. Payment retries refetch the order and bind the provider handoff amount/currency to that current snapshot.

## Vendor findings

Vendor finance, earnings, payout, statement, commission, settlement, and payment presentation remains current-session and current-vendor scoped through the existing financial participation authority boundary. No client-side commission, payout, settlement, or earnings calculation was found.

## Admin findings

Super Admin order detail now rejects contradictory parent/split totals and aggregate commission/vendor-earning evidence. Visibility, filters, and selected rows remain presentation only.

## Split findings

Parent totals must match backend-issued sub-order totals; each sub-order subtotal plus shipping must match its total, and item totals must match its merchandise portion. The frontend never derives a replacement split total.

## Refund findings

Customer refund list/create DTOs require a non-negative backend amount, currency, unit-price snapshot, and exact backend item quantity evidence. Malformed refund money is withheld.

## Bootstrap and cache findings

Cart pricing no longer retains previous query data. Cart and checkout withhold financial presentation during pricing refresh/failure; customer orders already withhold during current refresh/failure, and customer refunds now do the same. Session/actor/tenant authority-loss paths retain their existing scoped cache purge and backend revalidation behavior.

## Failure and adversarial findings

401/403, malformed DTOs, contradictory totals, stale cache/replay, history/URL state, mutation responses, local/session storage, React state, contexts, cross-tab signals, and actor/tenant replacement cannot manufacture financial authority. They can only trigger invalidation, revalidation, or withheld presentation. Backend execution remains decisive.

## Runtime hardening performed

- Added the shared CommerceHub financial DTO authority boundary for order, split, cart-pricing, payment-handoff, and refund evidence.
- Removed local cart-summary fallback financial presentation and prior-pricing query retention.
- Bound payment initiation/recovery handoffs to the just-refetched backend order amount and currency.
- Added Super Admin order financial coherence checks.

## Exact runtime files modified

- `PointOfSale.CommerceHub/src/features/orderFinancialAuthority.ts`
- `PointOfSale.CommerceHub/src/services/cart-service.ts`
- `PointOfSale.CommerceHub/src/cart/useCommerceHubCartPricing.ts`
- `PointOfSale.CommerceHub/src/cart/CartSummary.tsx`
- `PointOfSale.CommerceHub/src/checkout/CheckoutSummary.tsx`
- `PointOfSale.CommerceHub/src/pages/CartPage.tsx`
- `PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx`
- `PointOfSale.CommerceHub/src/services/checkout-service.ts`
- `PointOfSale.CommerceHub/src/features/checkout/orderCreationAuthority.ts`
- `PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts`
- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`
- `PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx`
- `PointOfSale.CommerceHub/src/services/customer-refunds-service.ts`
- `PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx`
- `PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.ts`

## Regression freezes added

- `PointOfSale.Tests/Architecture/CommerceHubMarketplaceOrderAuthorityCertificationTests.cs`
- `PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.test.ts`

## Architecture updates

Updated the five authority context records with the PASS M2-I truth, risk, hardening, and freeze rule.

## Certification answers

- Who owns order financial authority? Current backend Order, Split Order, Payment, Settlement, Commission, Vendor Earnings, Tax, Shipping, Refund, Return, and Governance authorities.
- Can frontend fabricate totals? No.
- Can vendor earnings be recomputed? No.
- Can commissions be fabricated? No.
- Can settlement amounts be fabricated? No.
- Can split financials drift? No; contradictory backend evidence is rejected.
- Can refunds fabricate financial truth? No.
- Can stale financials survive refresh? No; financial presentation is withheld on the hardened refresh paths.
- Can cache restore financial authority? No.
- Can tenant leakage occur? No inspected frontend financial path can bypass current backend scope validation and scoped purge.
- Were runtime changes required? Yes.
- Is Marketplace Order Financial Authority production-certifiable? Yes.

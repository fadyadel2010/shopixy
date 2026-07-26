# PASS C3 CommerceHub Order & Checkout Currency Authority

Date: 2026-06-23

## Verdict

CommerceHub Order and Checkout Currency Authority is production-certifiable for the inspected cart, persisted-cart, checkout, order creation, split-order, order projection, recovery, cache, formatter, and adversarial runtime paths. CommerceHub owns the only accepted order currency: `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`).

## Runtime Findings

- Cart: browser and persisted cart candidates are EGP-normalized for display, and cart API responses now fail closed when a line carries a non-EGP currency. Server sanitation reloads listing, product, variant, stock, and EGP admission authority; request currency is not accepted.
- Checkout: pricing, shipping, discount, tax/total snapshots, payment session, and checkout response use the server EGP constant. Frontend pricing and checkout validators reject anything other than EGP.
- Orders and sub-orders: checkout stamps the marketplace order and every split sub-order with the one resolved EGP value. Customer order list/detail reads withhold parent or child rows that violate EGP; public order projection reads re-check both SQL authority and the repaired projection before returning it.
- Projections and recovery: projection replay/recovery is not accepted as currency authority. A non-EGP order projection is withheld with a service-unavailable response; cache/history/BFCache candidates only become visible after fresh backend evidence passes the same checks.
- Formatters: `formatCurrency` supports EGP only. A non-EGP input returns the blocked label rather than formatting USD/EUR/etc.
- Failures/adversarial inputs: forged/missing/malformed cart, checkout, payment, and order currencies are rejected or withheld. Customer order reads fail closed rather than converting or relabeling mixed-currency evidence.

## Runtime Hardening Performed

- Added an EGP-only frontend currency validator for cart state, cart pricing, order totals, split orders, checkout results, payment sessions, payments, and refunds.
- Applied the cart-state validator to get/save/clear cart API responses and the pricing validator to pricing responses.
- Forced cart aggregation and cart display group formatting to the CommerceHub EGP constant, so persisted browser data cannot display USD before a refresh.
- Added SQL/read-model fences that exclude parent or sub-order currency violations from customer lists/details and public order recovery projections.
- Added PostgreSQL `NOT VALID` EGP check constraints for marketplace orders and sub-orders. They enforce every new/changed row without destroying legacy forensic evidence; invalid historical rows remain withheld until explicitly repaired.

## Regression Freeze

`CommerceHubOrderCurrencyAuthorityCertificationTests` freezes the EGP order-creation path, customer/projection read fences, database constraints, and frontend cart/checkout/order validation boundaries.

## Explicit Answers

| Question | Answer |
|---|---|
| Who owns CommerceHub Order Currency Truth? | CommerceHub platform runtime via `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`). |
| Can cart display USD? | No. Cart display is EGP-normalized and formatter rejects USD. |
| Can checkout display USD? | No. Checkout/pricing validators and formatter reject it. |
| Can order totals display USD? | No. Order evidence must be EGP; invalid projections are withheld. |
| Can tenant or storefront currency affect checkout currency? | No. They cannot select, override, or format CommerceHub checkout currency. |
| Can non-EGP products enter cart? | No authoritative cart line; backend admission/sanitation rejects them. |
| Can non-EGP orders be created? | No. Creation stamps EGP and database constraints reject new non-EGP order/sub-order rows. |
| Can mixed-currency orders or sub-orders exist? | Not as accepted authority. Creation uses one EGP value; mixed legacy evidence is withheld. |
| Can projections contain non-EGP currency? | They may exist only as stale/legacy data, never as served CommerceHub truth. |
| Can cache restore non-EGP order state? | No. Cache/recovery candidates are revalidated and withheld when non-EGP. |
| Can forged currency become order truth? | No. Currency is not trusted from request/DTO/cache evidence. |
| Were runtime changes required? | Yes: frontend response validation/display normalization, order read/projection fences, and database constraints. |
| Is this production-certifiable? | Yes, for the inspected CommerceHub order and checkout currency runtime paths. |

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/features/orderFinancialAuthority.ts`
- `PointOfSale.CommerceHub/src/services/cart-service.ts`
- `PointOfSale.CommerceHub/src/features/checkout/orderCreationAuthority.ts`
- `PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts`
- `PointOfSale.CommerceHub/src/types/cart.ts`
- `PointOfSale.CommerceHub/src/cart/StoreGroup.tsx`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCustomerOrderService.cs`
- `PointOfSale.API/Controllers/CommerceHubOrdersController.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubMarketplaceOrderConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubSubOrderConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260623120000_CommerceHubOrderCurrencyAuthorityHardening.cs`

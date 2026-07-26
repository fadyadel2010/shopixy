# PASS 16.5F-A CommerceHub Currency Authority Certification

Date: 2026-06-02

## Verdict

CommerceHub currency authority is **EGP only** after this pass.

Before hardening, CommerceHub cart, checkout, Mongo projection, Elasticsearch SQL overlay, vendor repayment dashboard, vendor financial-control fallback, and payout queue fallback could derive displayed or persisted CommerceHub currency from `Tenant.Currency`. That meant a tenant using USD/EUR/SAR/AED/GBP for POS or storefront operations could leak non-EGP currency into CommerceHub customer and finance surfaces.

## Currency Authority Inventory

| Domain | Authority owner | Source of truth | Derivative consumers |
|---|---|---|---|
| POS | Tenant/admin | `Tenant.Currency`, tenant-scoped settings/tax | POS UI, tenant accounting displays |
| Storefront | Tenant/admin | `Tenant.Currency`, storefront config projection | Storefront product/cart/checkout displays |
| CommerceHub listing | Platform marketplace | `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`) | Mongo/Elasticsearch product documents, query DTOs |
| CommerceHub pricing | Platform marketplace | EGP amount resolved by `ICommerceHubPriceAuthority` | Cart, checkout, search, detail, projections |
| CommerceHub cart | Platform marketplace | Backend sanitation/pricing in `CommerceHubCartService` | Browser cart DTOs and persisted cart JSON |
| CommerceHub checkout/orders | Platform marketplace | `CommerceHubCheckoutService` order creation | Marketplace order, sub-order, payment session |
| CommerceHub settlements | Platform ledger | EGP financial transactions/settlement rows | Merchant finance controls, dashboards |
| CommerceHub payouts | Platform ledger | EGP ledger-backed payable/payout rows | Payout queue, Super Admin execution |
| CommerceHub refunds | Platform ledger | Order/refund currency from EGP marketplace order | Refund execution, reversal, ledgers |
| CommerceHub ledger | Platform ledger | `CommerceHubFinancialTransaction.Currency` with EGP guardrails | Reconciliation, dashboards |

## Findings

- Publication/listing records do not carry a separate currency field. Listing price override remains amount-only and subordinate to CommerceHub EGP authority.
- Cart pricing previously required all participating tenants to share one `Tenant.Currency`; this blocked mixed-currency POS tenants and could stamp cart DTOs with non-EGP currency. It now emits EGP.
- Checkout previously stamped marketplace orders and sub-orders from tenant currency consensus. It now stamps EGP and no longer requires vendor currency agreement.
- Search projection and SQL overlay previously used tenant/storefront currency, including a silent EGP fallback for blank tenant currency. They now converge to platform EGP.
- Tenant-currency replay previously rewrote CommerceHub product documents with current tenant currency. It now rewrites only to CommerceHub EGP.
- Vendor repayments and financial controls now default to and require EGP instead of tenant currency. Payout queue fallback no longer uses `Tenant.Currency`.

## Required Answers

- CommerceHub authority currency: **EGP**.
- Can non-EGP products be published? Products from non-EGP POS/storefront tenants can be published, but CommerceHub publication currency is EGP.
- Can non-EGP prices enter listings? No separate currency can enter listing authority; active runtime pricing is EGP amount authority.
- Can overrides bypass currency authority? No. Overrides are amount-only and rendered/resolved under EGP.
- Can replay/recovery bypass currency authority? Hardened projection replay and SQL overlay converge to EGP.
- Can cart contain non-EGP authority? No after hardening.
- Can checkout contain non-EGP authority? No after hardening.
- Can ledger contain non-EGP authority? Certified paths are EGP-guarded; historical corrupted ledger rows remain quarantined by mismatch checks.
- Can refunds contain non-EGP authority? Refunds inherit EGP marketplace order currency.
- Is CommerceHub currency-safe? Yes for inspected runtime paths after this pass; launch blockers from prior production-readiness passes still apply.

## Regression Freeze Added

- `CommerceHubCurrencyAuthorityCertificationTests`
- `CommerceHubPublicationCurrencyCertificationTests`
- `CommerceHubPriceOverrideCurrencyCertificationTests`

## Remaining Debt

- Database-level check constraints for every CommerceHub financial currency column should be added in a rollout-safe migration.
- A production parity scanner should compare CommerceHub SQL/Mongo/Elasticsearch currency fields and alert on non-EGP.
- PostgreSQL-backed chaos tests should exercise mixed tenant currencies through publish, cart, checkout, refund, payout, replay, recovery, and reindex.

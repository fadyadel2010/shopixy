# Currency Runtime — FINAL CANONICAL ARCHITECTURE

Status: IMPLEMENTATION-BACKED CANONICAL SPECIFICATION
Authority: Implementation only (`.cs` / `.ts` sources). No markdown, comments, UI labels, or docs were trusted as truth. Every claim cites `file:line`.
Reverse-engineered: 2026-07-18
Recertified: 2026-07-22
Scope: Currency as a cross-domain runtime concern across Merchant / CommerceHub / Money / Pricing / Tax / Receipt / Accounting / Payment / Storefront / POS / Reporting.

---

## 0. Executive Summary

Currency in Shopixy is NOT a simple settings toggle. It is a **two-authority financial runtime**:

- **Merchant Currency Runtime** — owned by the tenant. Source of truth is `Tenant.Currency` (PostgreSQL). Supported ISO-4217 codes are permitted subject to SQL reference data; default `EGP`. It propagates to POS, Storefront, Cart, Checkout, Orders, Receipts, Refunds, Affiliate, WhatsApp, Shipping, and Reporting via SQL-backed reads plus the `TenantCurrencyChangedEvent` outbox + MongoDB/Redis projections.
- **CommerceHub / Platform Currency Runtime** — owned by the platform marketplace and is **hard-pinned to EGP**. It is enforced by `FinancialCurrencyGuard.PlatformBaseCurrencyCode = "EGP"`, never reads `Tenant.Currency`, and is guarded by `RequireEgpCurrency` on every ledger/settlement/payout/refund path and by a hard reject on cart/checkout for non-EGP vendors.

The single canonical answer to "how does one merchant-selected currency propagate everywhere while CommerceHub stays permanently EGP" is: **the merchant currency is the SQL authority for the tenant's own surfaces, and is EXPLICITLY overwritten to EGP at every CommerceHub boundary (`CommerceHubProjectionConsumer.ResolveCurrency()`, `CommerceHubCartService`/`CommerceHubCheckoutService` vendor-currency reject, `CommerceHubPriceAuthority` amount-only pricing, `CommerceHubSearchSqlAuthorityOverlay` eligibility gate, and `RequireEgpCurrency` in all finance entities).** The two runtimes never share a source of truth — they are intentionally forked.

---

## 1. Architecture Vision

A single tenant may operate in a supported ISO-4217 currency for its own POS/Storefront business. The platform marketplace (CommerceHub) is a single-currency (EGP) sub-economy: non-EGP merchants are rejected at publication/update, suppressed from search, rejected from cart/checkout, and cannot create CommerceHub financial rows. Money is modeled as a proper value object (`Money` + `Currency`) in the payment runtime, while the legacy order/pricing domain still uses primitive `decimal Amount` + `string Currency` columns that are re-validated at write time through `FinancialCurrencyGuard`.

## 2. Runtime Purpose

Enforce and propagate two independent currency authorities (tenant vs platform-EGP) without cross-contamination, failing closed on missing/invalid/mismatched currency, and preserving historical order currency immutably.

## 3. Runtime Scope

IN scope: currency code storage, validation, normalization, propagation (SQL→outbox→Mongo/Redis→storefront/cart/orders), CommerceHub EGP pinning, order-currency stamping, receipt/invoice/ledger currency, payment provider currency authority, reporting currency selection.

OUT of scope: exchange-rate conversion (NO conversion runtime exists — cross-currency is rejected, not converted), FX pricing, multi-currency wallets.

## 4. Runtime Owns

- Authoritative tenant currency: `Tenant.Currency` (PointOfSale.Core/Entities/Tenant.cs:62).
- Platform base currency constant: `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (PointOfSale.Core/Financial/FinancialCurrencyGuard.cs:10).
- ISO-4217 validation: `Iso4217CurrencyProvider` (PointOfSale.Core/Financial/Iso4217CurrencyProvider.cs:7).
- Currency normalization/Guard: `FinancialCurrencyGuard` (PointOfSale.Core/Financial/FinancialCurrencyGuard.cs:8) + `CurrencyNormalizationHelper` (PointOfSale.Infrastructure/Utilities/CurrencyNormalizationHelper.cs:9).
- Merchant→storefront/mongo/redis propagation: `TenantCurrencyChangedEvent` + consumers.
- CommerceHub EGP pinning: `CommerceHubProjectionConsumer.ResolveCurrency()` (CommerceHubProjectionConsumer.cs:2932), cart/checkout rejects, `RequireEgpCurrency` in finance entities.
- Order-currency stamping: `CheckoutService.EnsureCheckoutCurrencyAsync` (CheckoutService.cs:8554).

## 5. Runtime Does NOT Own

- Exchange rates / conversion (no converter exists anywhere; `FinancialCurrencyGuard.RequireMatch` rejects, never converts).
- Provider-side currency decision for online gates (provider currency is authoritative for reconciliation, see §11, but the platform does not choose it).
- Frontend currency labels as authority (they are presentation-only; missing currency fails closed — FrontendCurrencyAuthorityCertificationTests).

---

## 6. Architecture Principles

1. **SQL is the only source of truth for tenant currency.** Mongo/Redis are projections ONLY; consumers re-read `Tenant.Currency` from SQL before mutating projections (TenantCurrencyProjectionConsumer.cs:89, :173).
2. **Fail closed on missing/invalid/mismatched currency.** Silent fallbacks are explicitly forbidden and `[Obsolete(error:true)]` (CurrencyNormalizationHelper.cs:27-30).
3. **CommerceHub is permanently EGP.** `ResolveCurrency()` returns the constant unconditionally (CommerceHubProjectionConsumer.cs:2932-2933); non-EGP vendor carts/checkouts are rejected (CommerceHubCartService.cs:505, CommerceHubCheckoutService.cs:462).
4. **Historical order currency is immutable.** Orders stamp `Currency = validatedOrderCurrency` at creation (CheckoutService.cs:5414, :5533, :6714) and downstream consumers copy `order.Currency` — they never mutate it.
5. **Money never loses its currency code.** The payment-runtime `Money` value object couples `Amount` + `Currency` (PaymentRuntimeValueObjects.cs:31-47).
6. **Idempotent, order-agnostic, replay-safe propagation** via `ProjectionEventFence` + freshness watermarks per consumer (TenantCurrencyProjectionConsumer.cs:69, :217).

---

## 7. Merchant Currency Runtime

### Storage
- `Tenant.Currency` (string, ISO-4217) on the tenant row; default `"EGP"` (Tenant.cs:62).
- Also mirrored into `SystemSettings` key `Tax.CurrencyCode` + `Tax.CurrencySymbol` for display/legacy (SettingsController.cs:181-182). This is a *derived* tenant setting, not the authority.
- Settings read uses `Tenant.Currency` as required fallback authority after ISO validation; missing/invalid tenant currency fails closed, while invalid derived settings fall back only to validated `Tenant.Currency` (SettingsController.cs:100-126).

### Mutation
- Only via `SettingsController.UpdateSettings` (Owner/Admin). It writes `Tax.CurrencyCode`, then **also** writes `Tenant.Currency` and enqueues `TenantCurrencyChangedEvent` to the outbox atomically (SettingsController.cs:190-256). The event is enqueued BEFORE `SaveChangesAsync` for atomicity (SettingsController.cs:217-241).

### Validation
- `ResolveRequestedSettingsCurrencyCode` runs `FinancialCurrencyGuard.RequireCurrency` (ISO-validated) + `CurrencyAliases` symbol→code mapping (SettingsController.cs:429-441). `$`→USD, `€`→EUR, `£`→GBP, `E£`/`LE`/`ج.م`→EGP (SettingsController.cs:24-37).

### Persistence & Propagation
- SQL commit → outbox row → MassTransit `TenantCurrencyChangedEvent` → consumers update MongoDB `storefront_configs.currency`, invalidate Redis, and re-read SQL authority (TenantCurrencyProjectionConsumer.cs:48-185).

### Tenant Isolation
- Strictly tenant-scoped. `ITenantContext.RequiredTenantId` is the boundary; `TenantCurrencyChangedEvent` carries `TenantId` and consumers reject empty GUIDs (TenantCurrencyProjectionConsumer.cs:52-59). No cross-tenant currency bleed exists in this path.

### Answers
- Can every merchant choose any currency? **Yes, within supported ISO/reference data.** The controller validates via `Iso4217CurrencyProvider`, and persistence is constrained by the SQL currency reference table.
- How stored? `Tenant.Currency` SQL column (authority) + `Tax.CurrencyCode` setting (display).
- How enforced? ISO validation on write; propagation via outbox; consumers re-read SQL.

---

## 8. CommerceHub Currency Runtime

### Authority
- **EGP only.** Constant `FinancialCurrencyGuard.PlatformBaseCurrencyCode = "EGP"` (FinancialCurrencyGuard.cs:10).

### Can CommerceHub change currency?
- **No.** `CommerceHubProjectionConsumer.ResolveCurrency()` returns the constant unconditionally (CommerceHubProjectionConsumer.cs:2932-2933). It ignores `Tenant.Currency` entirely.

### Is it hardcoded?
- Yes, intentionally: the platform marketplace is a single-currency EGP sub-economy. Non-EGP merchants are rejected from publication/update, suppressed from listing/search, rejected from cart/checkout, and blocked from ledger/settlement/payout/refund rows.

### Validation / gating
- Publication/update eligibility gate: CommerceHub command/controller paths reject non-EGP tenants before publication or listing updates.
- Vendor visibility gate: `CommerceHubSearchSqlAuthorityOverlay` returns null (no listing) unless `tenant.Currency == EGP` (CommerceHubSearchSqlAuthorityOverlay.cs:120-123).
- Cart: every participating tenant must be EGP or checkout is rejected `STORE_CURRENCY_INVALID` (CommerceHubCartService.cs:503-512; also CommerceHubCheckoutService.cs:462).
- Pricing: `CommerceHubPriceAuthority` resolves amount-only; currency is never carried from variant — EGP is the implicit authority (CommerceHubPriceAuthority.cs:88-124).
- Finance: every ledger/settlement/payout/refund entity forces EGP via `RequireEgpCurrency` (CommerceHubFinancialTransaction.cs:84, CommerceHubVendorSettlement.cs:66, CommerceHubVendorPaymentRequest.cs:141, SuperAdminRefundsService.cs:124, SuperAdminPayoutsService.cs:739, CommerceHubDailyFinancialAggregateService.cs:478, CommerceHubFinanceReadService.cs:154).

### Does Marketplace ignore merchant currency?
- **Yes, by design.** See divergence §27.

---

## 9. Money Runtime

### Implementation
- `Money` is a proper value object: `decimal Amount` + `Currency Currency` (PaymentRuntimeValueObjects.cs:31-47).
- `Currency` is a record with a 3-letter A-Z uppercase ISO-style code, validated in the constructor (PaymentRuntimeValueObjects.cs:5-29).
- `Money` rejects negative amounts and null currency (PaymentRuntimeValueObjects.cs:37-46).

### Legacy domain
- The order/pricing domain still uses primitive `decimal Amount` + `string Currency` columns (e.g. `Order.Currency`), re-validated through `FinancialCurrencyGuard` at write time (CheckoutService.cs:5414, :8570). So Money is a proper domain model **in the payment runtime only**; the broader order domain is primitive-decimal + string currency that is guarded but not a value object.

### Precision / symbols / formatting
- Formatting is presentation-only: `formatPrice` requires explicit currency, no fallback (currency.ts:69-91); RTL-safe symbol map (currency.ts:18-50). Backend symbol map in SettingsController.cs:39-45.

---

## 10. Pricing Runtime

- Product variant prices are primitive `decimal Price` / `decimal? OnlinePrice` with **no own currency field** — they inherit the tenant currency (CommerceHubPriceAuthority.cs:83-86). CommerceHub override is amount-only (CommerceHubPriceAuthority.cs:108-123).
- Order totals (`OrderTotalsDto`) and discounts/coupons/shipping/fees/taxes are stamped with the **validated tenant currency** at checkout (CheckoutService.cs:5414-5422, 5533). Historical orders never change currency.
- Coupons/Promotions/SmartOffers resolve tenant currency via `ResolveTenantCurrencyAsync` and validate against it (CouponsController.cs:846, PromotionsController.cs:78, AdminSmartOffersController.cs:500).

**Conclusion:** prices inherit currency from the tenant (or EGP at the CommerceHub boundary); they do not store their own currency code.

---

## 11. Payment Integration

Providers: Stripe, Paymob, Fawry, Manual Payment, Cash On Delivery (COD).

- **Order currency is chosen by the tenant/checkout**, validated against `Tenant.Currency` via `EnsureCheckoutCurrencyAsync` → `RequireMatch` (CheckoutService.cs:8554-8575).
- **After order creation, historical `Order.Currency` is the authority for payment creation/capture.** Payment runtime validates `order.Currency` and matches provider/payment currency to `order.Currency`; it does not revalidate historical orders against current `Tenant.Currency` after a merchant currency change.
- **Provider currency is authoritative for reconciliation evidence**, NOT for order creation. Provider evidence must carry currency and match `Order.Currency`; missing provider currency fails closed rather than defaulting to EGP.
- **Can providers reject currencies?** Yes — `RequireMatch(order, provider)` would surface mismatch at capture; CommerceHub captures are always EGP because its orders are EGP.
- **COD / Manual:** currency = order currency (no provider FX gate); COD admission must receive explicit currency and fails closed when missing. Refunds for CommerceHub are forced EGP (RefundFinancialDecisionEngine.cs:43, SuperAdminRefundsService.cs:124).
- Evidence: orders copy `order.Currency` into payment intents (OrdersController.cs:454, 632; StorefrontOrdersReadController.cs:762, 803).

---

## 12. Receipt Runtime

- Storefront/WhatsApp receipts use the **order currency** (`order.Currency`) and preserve historical currency immutably (WhatsAppDispatchAuthority.cs:133).
- Legacy POS receipt DTOs do not carry an explicit currency field and can format historical amounts with current tenant settings. This is implementation debt and not a separate currency authority.

---

## 13. Accounting Runtime

- CommerceHub accounting is **always EGP** — every finance entity calls `RequireEgpCurrency` (CommerceHubFinancialTransaction.cs:84; CommerceHubVendorSettlement.cs:66; CommerceHubVendorPaymentRequest.cs:141; SuperAdminPayoutsService.cs:739, 1012, 1055; CommerceHubDailyFinancialAggregateService.cs:478; CommerceHubFinanceReadService.cs:154; SuperAdminRefundsService.cs:124).
- Merchant ecommerce/refund/affiliate accounting uses **order currency** where order-backed financial rows carry currency (OrdersController.cs:340, 473; ProjectionFailureCompensation.cs:384, 433; AffiliateService.cs:1561, 1601).
- Legacy POS `SalesInvoice`/`PurchaseInvoice` and core accounting `LedgerEntry` rows are amount-only and do not carry first-class currency columns. They inherit tenant/order context operationally but do not fully satisfy first-class historical currency preservation. This is implementation debt, not a new authority.
- **Currency preservation:** CommerceHub rows are quarantined by mismatch checks if non-EGP (commercehub-currency-authority-certification-findings.md:44); legacy currencyless POS accounting remains an observation.

---

## 14. Storefront Runtime Integration

- Public Storefront config/cart reads current SQL tenant/config authority for currency; MongoDB `storefront_configs.currency` remains the derived projection/cache read model of `Tenant.Currency` for storefront projection paths (StorefrontConfigDocument.cs:69; StorefrontConfigProjectionConsumer.cs:201 `ResolveTenantCurrency(tenant)`).
- Frontend `formatPrice`/store config require explicit currency; missing currency fails closed, never defaults to EGP/USD (currency.ts:69-91; FrontendCurrencyAuthorityCertification findings:12-16).
- Product pages/cart/checkout display the storefront-config currency, refreshed from backend before order creation (frontend-currency-authority-certification-findings.md:8).

---

## 15. POS Runtime Integration

- POS sales/returns/invoices/receipts/cash-drawer read currency from the **tenant currency** (Tenant.cs:62) via store config / tenant context. POS is a tenant-owned surface; it uses `Tenant.Currency`, never CommerceHub EGP.
- Receipt printing uses order currency (§12). POS does not talk to CommerceHub currency at all.

---

## 16. Reporting Runtime Integration

- **Merchant/tenant reporting** uses the tenant currency (`ResolveTenantCurrencyAsync` in campaign/analytics controllers; AdminCampaignCostController.cs:123-139; AnalyticsTenantCurrencyResolver).
- **CommerceHub / SuperAdmin financial reporting** is EGP — `DefaultCurrency = PlatformBaseCurrencyCode` across SuperAdminFinancialDashboardService (SuperAdminFinancialDashboardService.cs:3106+), SuperAdminOrdersService.cs:717 `= "EGP"`, SuperAdminVendorFinanceService.cs:1474 `= DefaultCurrency`.
- **Historical order reporting** preserves the order's original currency via `order.Currency` (OrdersController.cs:454, 632, 2307, 2778).
- **Divergence:** a merchant reporting in USD and a CommerceHub payout report in EGP are two different authorities (§27).

---

## 17. Currency Propagation Runtime

```
Merchant selects currency (SettingsController.UpdateSettings)
        ↓  validates ISO, writes Tenant.Currency + Tax.CurrencyCode
SQL Tenant.Currency  (AUTHORITY)
        ↓  atomic outbox enqueue TenantCurrencyChangedEvent (SettingsController.cs:217-241)
OutboxMessage (PostgreSQL)  → MassTransit publish
        ↓
TenantCurrencyProjectionConsumer  → re-reads SQL Tenant.Currency and ISO-validates it,
   writes storefront_configs.currency (Mongo), invalidates Redis
   (TenantCurrencyProjectionConsumer.cs:48-185)
        ↓
StorefrontConfigProjectionConsumer → Mongo storefront_configs on tenant events
   (StorefrontConfigProjectionConsumer.cs:201)
        ↓
Redis store-config cache invalidated (TenantCurrencyProjectionConsumer.cs:187-215)
        ↓
Storefront UI / Cart / Checkout read explicit storefront currency from SQL-backed config/projection (currency.ts)
        ↓
CheckoutService.EnsureCheckoutCurrencyAsync → RequireMatch(snapshot, Tenant.Currency)
   → Order.Currency = validatedOrderCurrency (CheckoutService.cs:5414, 5533)
        ↓
Orders / Receipts / Invoices / Ledger copy order.Currency (immutable)
        ↓
Reports: tenant reports use order/tenant currency; CommerceHub reports use EGP

=== CommerceHub BOUNDARY (fork) ===
TenantCurrencyChangedEvent consumed by CommerceHubProjectionConsumer
   → ResolveCurrency() returns EGP, overwrites ALL product docs' Currency=EGP
   (CommerceHubProjectionConsumer.cs:873-1689, 2932)
CommerceHub cart/checkout reject non-EGP vendors (CommerceHubCartService.cs:505,
   CommerceHubCheckoutService.cs:462)
CommerceHub finance entities RequireEgpCurrency (every ledger/settlement/payout)
```

### Per-stage authority / sync / failure
| Stage | Authority | Synchronization | Failure behaviour |
|---|---|---|---|
| Settings write | Tenant (Owner/Admin) | SQL + outbox | ISO invalid → 500 (SettingsController.cs:270) |
| SQL Tenant.Currency | Tenant | n/a | n/a |
| Outbox | Eventual, atomic w/ commit | S40 ordering | throws → retry |
| Mongo storefront_configs | Tenant (via consumer) | re-read SQL | throws → MassTransit retry (TenantCurrencyProjectionConsumer.cs:164-170) |
| Redis cache | derived | invalidated | non-fatal warn (TenantCurrencyProjectionConsumer.cs:208-214) |
| Storefront/Cart | SQL-backed storefront config / projection | SQL + projection | missing → fail closed (currency.ts/backend guards) |
| Checkout/Order | Tenant (RequireMatch) | validated | CURRENCY_MISMATCH/INVALID/NOT_SET → CheckoutException (CheckoutService.cs:8586-8624) |
| CommerceHub product docs | Platform EGP | overwritten | non-EGP vendors rejected at publication/update and pre-checkout |
| CommerceHub finance | Platform EGP | RequireEgpCurrency | throws on non-EGP |
| Reports | tenant or EGP per surface | n/a | EGP surfaces reject non-EGP |

---

## 18. Currency Validation Runtime

- ISO-4217 set: `Iso4217CurrencyProvider.ActiveCurrencyCodes` (Iso4217CurrencyProvider.cs:13-67). Case-insensitive contains check.
- `FinancialCurrencyGuard`:
  - `RequireCurrency` — non-empty + ISO valid (FinancialCurrencyGuard.cs:12-30).
  - `RequireExpectedCurrency` / `RequireEgpCurrency` — must equal expected/EGP (FinancialCurrencyGuard.cs:32-69).
  - `RequireMatch` — two currencies must equal (FinancialCurrencyGuard.cs:85-106).
  - `RequireConsistentCurrency` — tenant + order + payment consistency (FinancialCurrencyGuard.cs:108-131).
  - `NormalizeIfPresent` — optional (FinancialCurrencyGuard.cs:71-83).
- Exceptions: `FinancialCurrencyMissingException`, `FinancialCurrencyInvalidException`, `FinancialCurrencyMismatchException` (FinancialCurrencyGuard.cs:134-192).
- **Silent default is forbidden:** `CurrencyNormalizationHelper.NormalizeCurrencyOrDefault` is `[Obsolete(error:true)]` and always throws (CurrencyNormalizationHelper.cs:27-30).

---

## 19. Currency Formatting Runtime

- Backend: `SettingsController.ResolveDisplayCurrencySymbol` maps code→symbol, falls back to code (SettingsController.cs:443-454).
- Frontend: `currency.ts` `formatPrice` requires explicit currency, no EGP/USD fallback, throws on missing (currency.ts:69-91). Symbol map includes EGP as `"EGP "` + regional currencies (currency.ts:18-50).
- RTL: handled in storefront i18n; currency code is language-agnostic.

---

## 20. Localization Runtime

- Currency code is locale-independent; symbol/separators are presentation-only.
- Thousands/decimal separators come from `CultureInfo` in `ParseStoredDecimal`/`ParseStoredBoolean` (SettingsController.cs:461-483). No hard-coded culture in currency authority.
- Frontend formatting maps known currencies to symbol-before/after; default prefix (currency.ts:82-90).
- RTL/Arabic: tenant `Country` drives localization; currency remains code-based (SettingsController.cs:322-336 country validation ISO-3166).

---

## 21. CQRS

- **Write model (SQL authority):** `Tenant.Currency` + `SystemSettings`. Mutations only via `SettingsController` + `TenantCurrencyChangedEvent` outbox.
- **Read models (projections):**
  - SQL-backed public storefront config/cart reads — current tenant storefront authority.
  - MongoDB `storefront_configs.currency` — tenant storefront read model (StorefrontConfigDocument.cs:69).
  - MongoDB `commercehub_products.Currency` — ALWAYS EGP (CommerceHubProjectionConsumer.cs:2932).
  - Elasticsearch CommerceHub docs — `Currency = EGP` (CommerceHubSearchSqlAuthorityOverlay.cs:204).
  - Redis store-config cache — derived snapshot, invalidated on change (TenantCurrencyProjectionConsumer.cs:187-215).
- **Synchronization:** outbox → MassTransit → consumers with `ProjectionEventFence` idempotency + freshness watermarks (TenantCurrencyProjectionConsumer.cs:69, :217).
- **Consistency:** eventual; consumers re-read SQL authority so delayed/replayed events converge to current truth, never stale payload (TenantCurrencyProjectionConsumer.cs:89-103).

---

## 22. Projection Usage

| Projection | Source | Target | Currency |
|---|---|---|---|
| TenantCurrencyProjectionConsumer | TenantCurrencyChangedEvent | Mongo storefront_configs | re-read SQL tenant currency with ISO validation |
| StorefrontConfigProjectionConsumer | tenant events | Mongo storefront_configs | tenant currency (StorefrontConfigProjectionConsumer.cs:201) |
| CommerceHubProjectionConsumer | TenantCurrencyChangedEvent + listing events | Mongo commercehub_products + ES | EGP (overwrites) |
| AffiliateProduct*ProjectionConsumer | TenantCurrencyChangedEvent | Mongo affiliate products | tenant currency (AffiliateProductPolicyProjectionConsumer.cs:616) |
| CommerceHubSearchSqlAuthorityOverlay | SQL | ES overlay | EGP (CommerceHubSearchSqlAuthorityOverlay.cs:204) |

---

## 23. Read Models

- SQL-backed public storefront config/cart reads — current tenant currency.
- `StorefrontConfigDocument.Currency` (storefront projection read) — tenant currency.
- `CommerceHubProductDocument.Currency` — EGP.
- `OutboxMessage` / `MarketplaceEventOutboxMessage` — propagation log; marketplace events validated for currency match (`MarketplaceEventOutboxService.ValidateCurrencyAsync`, :39, :57-118).

## 24. Write Models

- `Tenant` (authority), `SystemSettings` (display), `Order.Currency` (stamped), CommerceHub finance entities (EGP).

---

## 25. Runtime Authority Matrix

| Concept | Business Owner | Mutation Authority | Read Authority | Projection Authority | Runtime Truth |
|---|---|---|---|---|---|
| Merchant Currency | Tenant (Owner/Admin) | SettingsController + outbox | Tenant.Currency | Mongo storefront_configs | Tenant.Currency (SQL) |
| CommerceHub Currency | Platform Marketplace | none (constant) | ResolveCurrency() | Mongo commercehub_products=EGP | EGP constant |
| Money | Payment runtime | constructor | value object | n/a | Money.Amount+Code |
| Order Currency | Tenant (at checkout) | CheckoutService (RequireMatch) | Order.Currency | n/a | Order.Currency (immutable) |
| Invoice Currency | Tenant/CommerceHub per surface | copy order / RequireEgpCurrency where modeled | order/finance | n/a | order currency or EGP where currency column exists; legacy POS invoices are currencyless debt |
| Receipt Currency | Tenant (order) | copy order.Currency | order | n/a | order.Currency |
| Storefront Currency | Tenant | SQL Tenant.Currency + projection | SQL-backed config / storefront_configs | Mongo | Tenant.Currency with projection read model |
| Payment Currency | Provider (reconciliation) / Order (creation) | order + provider capture | order/payment | n/a | order currency; provider evidence must match order currency at capture |
| Reporting Currency | Tenant (merchant) / Platform (CommerceHub) | per surface | per surface | n/a | tenant or EGP |
| Tax Currency | Tenant | Settings Tax.CurrencyCode | settings | n/a | tenant currency |

---

## 26. Runtime Authority Divergence

1. **Merchant Currency ≠ CommerceHub Currency.** `Tenant.Currency` (any ISO) vs `EGP` constant (Tenant.cs:62 vs FinancialCurrencyGuard.cs:10). Intentional fork at `CommerceHubProjectionConsumer.ResolveCurrency()` (CommerceHubProjectionConsumer.cs:2932).
2. **Storefront Currency ≠ CommerceHub Currency.** Storefront = tenant currency; CommerceHub product docs = EGP.
3. **Historical Order Currency ≠ CommerceHub Reporting Currency.** A USD merchant's order stays USD; CommerceHub payout report is EGP (OrdersController.cs:454 vs SuperAdminFinancialDashboardService.cs:3106).
4. **Payment Currency (provider) ≠ Accounting Currency (tenant).** Provider currency authoritative at capture (IPaymentLifecycleService.cs:89-100); merchant ledger uses order currency.
5. **Configuration Currency (`Tax.CurrencyCode`) vs Business Currency (`Tenant.Currency`).** Settings writes BOTH, but `Tenant.Currency` is the re-read authority for projections (SettingsController.cs:101-111, 173-209).
6. **Affiliate product currency = tenant currency, but affiliate FINANCIAL surfaces must not collapse/relabel** (FrontendCurrencyAuthorityCertification findings:16). Affiliate discovery uses `tenant.Currency` (AffiliateProductDiscoveryService.cs:412; PublicAffiliateProductsController.cs:383) — diverges from CommerceHub EGP.

---

## 27. Cross-Domain Authority Graph

- **POS ↔ Orders:** POS reads tenant currency; orders stamped tenant currency (immutable).
- **Storefront ↔ Checkout ↔ Orders:** storefront-config currency → snapshot → `EnsureCheckoutCurrencyAsync` RequireMatch → Order.Currency.
- **Orders ↔ Payments:** order currency; provider currency authoritative at capture.
- **Orders ↔ Accounting:** copy order.Currency where modeled (merchant ecommerce/refund/affiliate) or EGP (CommerceHub); legacy POS invoice/ledger rows are currencyless debt.
- **CommerceHub ↔ Merchant:** forked — CommerceHub never reads Tenant.Currency for currency (only for eligibility gate CommerceHubSearchSqlAuthorityOverlay.cs:120).
- **CommerceHub ↔ Affiliate:** affiliate uses tenant currency; CommerceHub uses EGP — divergence at publication.
- **SuperAdmin ↔ CommerceHub finance:** EGP only.
- **Inventory:** currency-agnostic (quantities), but inventory value reports use tenant currency.
- **Search (ES):** CommerceHub docs forced EGP (CommerceHubSearchSqlAuthorityOverlay.cs:204); eligibility gated on tenant EGP (CommerceHubSearchSqlAuthorityOverlay.cs:120).

---

## 28. Business Invariants (implementation-backed)

1. **Merchant owns merchant currency.** `Tenant.Currency` mutated only by Owner/Admin via SettingsController (SettingsController.cs:156, 209).
2. **CommerceHub is permanently EGP.** `ResolveCurrency()` constant; RequireEgpCurrency on all finance (CommerceHubProjectionConsumer.cs:2932; CommerceHubFinancialTransaction.cs:84).
3. **Historical orders never change currency.** `Order.Currency` stamped once at creation; downstream copies, never mutates (CheckoutService.cs:5533; OrdersController.cs:454).
4. **Receipts preserve order currency where modeled.** Storefront/WhatsApp copy `order.Currency`; legacy POS receipt DTOs are currencyless debt.
5. **Payments validate currency.** `EnsureCheckoutCurrencyAsync` RequireMatch at checkout; payment creation/capture validates immutable `Order.Currency` and provider evidence must match it.
6. **Money never loses currency code.** `Money` couples Amount+Code; null rejected (PaymentRuntimeValueObjects.cs:31-46).
7. **Silent currency fallback forbidden.** `[Obsolete(error:true)]` (CurrencyNormalizationHelper.cs:27-30).
8. **Non-EGP vendors cannot enter CommerceHub publication/cart/checkout.** Reject publication/update and `STORE_CURRENCY_INVALID` in cart/checkout (CommerceHubCartService.cs:505; CommerceHubCheckoutService.cs:462).
9. **Mongo/Redis are projections only; SQL is authority.** Consumers re-read Tenant.Currency (TenantCurrencyProjectionConsumer.cs:89, 173).
10. **Marketplace events validated for currency match before enqueue.** `MarketplaceEventOutboxService.ValidateCurrencyAsync` (MarketplaceEventOutboxService.cs:39, 57-118).

---

## 29. Architectural Decisions (WHY)

- **Why merchant owns currency:** SaaS multi-tenant — each store operates in its local economy; tenant is the natural owner (Tenant.cs:62).
- **Why CommerceHub ignores merchant currency:** CommerceHub is a single settlement/payout sub-economy in EGP; mixing currencies would break ledger/settlement/reconciliation. Pinned via constant + RequireEgpCurrency so it cannot drift (FinancialCurrencyGuard.cs:10; CommerceHubProjectionConsumer.cs:2932).
- **Why orders preserve currency:** financial/legal immutability — a sold order's amount+currency must be stable for receipts, refunds, tax, accounting (CheckoutService.cs:5533).
- **Why reports use specific currency per surface:** merchant reports are operational (tenant currency); CommerceHub/SuperAdmin finance is the platform ledger (EGP). Divergence is intentional, not a bug.
- **Why Money is a value object in payment runtime:** payment reconciliation needs an inseparable amount+currency pair with validation (PaymentRuntimeValueObjects.cs:31-47). The broader order domain stays primitive for migration compatibility but is guarded.
- **Why fail-closed on missing currency:** prior production incidents from silent EGP/USD fallbacks; now missing currency blocks display/throws (currency.ts:69-91; frontend cert findings).

---

## 30. Technical Debt (implementation-backed)

1. **Hardcoded currency defaults in DTOs/events** — some read DTOs/events still default currency (`"EGP"` or legacy `"USD"`) and can mask missing-currency bugs on tenant surfaces.
2. **Legacy primitive decimal + string currency** in order/pricing domain instead of `Money` value object (Order.Currency string; CheckoutService.cs:5533). Inconsistent with payment runtime.
3. **Legacy POS invoice/ledger/receipt currency gaps** — `SalesInvoice`, `PurchaseInvoice`, invoice payment rows, core `LedgerEntry`, and POS receipt DTOs are amount-only or can format with current settings. They do not create a new authority, but they are not first-class historical currency preservation.
4. **No FX conversion runtime.** Cross-currency is rejected, never converted. If a non-EGP merchant's CommerceHub payout is needed, there is no conversion path — only EGP pinning (CommerceHubVendorPaymentRequest.cs:141).
5. **Duplicate formatting logic.** Backend `ResolveDisplayCurrencySymbol` (SettingsController.cs:443) and frontend `currency.ts` symbol maps can drift; EGP symbol differs (`E£` backend vs `EGP ` frontend).
6. **ES/SQL overlay hardcodes EGP** (CommerceHubSearchSqlAuthorityOverlay.cs:204) — correct but undocumented as a second EGP authority site; risk if constant renamed.
7. **CartEvents.Currency default `"USD"`** (CartEvents.cs:58) — a non-EGP default on an event that later hits RequireMatch; relies on snapshot override.
8. **CommerceHub DB-level EGP constraints are partially implemented** for marketplace orders/suborders, payments/payouts, financial transactions, settlements, payment requests, refunds/returns, and vendor earnings; app-level `RequireEgpCurrency` remains the primary runtime guard.
9. **No PostgreSQL-backed mixed-currency chaos proof** in production parity.

---

## 31. Implementation Follow-up Items

| Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|
| DB check constraints for CommerceHub currency | App-level `RequireEgpCurrency` plus implemented EGP CHECK constraints on key CommerceHub finance/order tables | Defense in depth | Continue expanding parity/constraint coverage where new EGP columns are added | Medium | In progress |
| Unify Money value object across order domain | Primitive decimal+string guarded | Consistency | Refactor Order/Invoice to `Money` | Medium | Open |
| Add first-class currency to legacy POS invoice/ledger/receipt rows | Amount-only legacy rows/DTOs | Historical currency preservation | Migration + DTO/API/frontend receipt formatting update | High | Open |
| Remove hardcoded DTO `Currency` defaults | `"EGP"`/`"USD"` defaults | Mask missing currency | Require explicit currency (mirror frontend fail-closed) | Medium | Open |
| Production parity currency scanner | Manual cert | Catch drift | Scheduled SQL/Mongo/ES comparison alerting non-EGP | High | Open |
| Mixed-currency chaos tests | Unit cert only | Launch readiness | PostgreSQL chaos: publish/cart/checkout/refund/payout/replay | High | Open |
| FX conversion runtime | None (reject) | Future multi-currency payout | Design conversion service if required | Low | Open |

---

## 32. Merchant Currency Lifecycle (REQUIRED)

```
1. Merchant selects currency (Owner/Admin)  — SettingsController.UpdateSettings
   Authority: Tenant. Validation: ISO-4217 via RequireCurrency (SettingsController.cs:429-441).
2. Persistence  — writes Tax.CurrencyCode + Tenant.Currency, enqueues TenantCurrencyChangedEvent
   to OutboxMessage BEFORE SaveChanges (SettingsController.cs:181-241). Atomic commit (256).
3. Propagation  — MassTransit publishes event →
   TenantCurrencyProjectionConsumer re-reads and ISO-validates SQL Tenant.Currency, writes Mongo
   storefront_configs.currency, invalidates Redis (TenantCurrencyProjectionConsumer.cs:48-215).
4. Storefront  — reads SQL-backed storefront config/current tenant authority and projection read models; formatPrice requires explicit currency (currency.ts:69).
5. Cart/Checkout — snapshot.Currency → EnsureCheckoutCurrencyAsync RequireMatch(tenant)
   (CheckoutService.cs:5414, 8554). Mismatch → CheckoutException.
6. Orders — Order.Currency = validatedOrderCurrency (immutable) (CheckoutService.cs:5533).
7. Receipt — copies order.Currency (SettingsController.cs:137; WhatsAppDispatchAuthority.cs:133).
8. Reports — tenant reports use order/tenant currency; CommerceHub reports use EGP.

Failure behaviour: ISO invalid → 500; projection throw → MassTransit retry (5x/2s);
Redis invalidation non-fatal. Replay: consumers re-read SQL → converge to current truth.
Consistency: eventual; freshness watermarks prevent stale overwrite
(TenantCurrencyProjectionConsumer.cs:217-235).
```

---

## 33. Order Money Lifecycle (REQUIRED)

```
Product Variant Price (decimal, no currency) + Tenant.Currency
   ↓ (CommerceHub: EGP override via CommerceHubPriceAuthority; else tenant currency)
Cart Snapshot.Currency = storefront currency
   ↓
Checkout EnsureCheckoutCurrencyAsync → RequireMatch(snapshot, Tenant.Currency)
   ↓ (CommerceHub checkout: vendor must be EGP or rejected)
Order.Currency = validatedOrderCurrency  [PRESERVED, immutable]
   ↓
Payment — created with immutable order currency; provider capture currency must match order currency for reconciliation
   ↓
Invoice — copies order.Currency (merchant) / RequireEgpCurrency (CommerceHub)
   ↓
Ledger — merchant ledger uses order currency; CommerceHub ledger EGP (RequireEgpCurrency)
   ↓
Reporting — order currency preserved; CommerceHub/SuperAdmin finance EGP

Where preserved: Order.Currency stamped once, copied downstream, never mutated.
Where it cannot change: historical orders, receipts, refunds inherit order/EGP currency.
Where it changes: ONLY at the CommerceHub boundary, where tenant currency is overwritten to EGP.
```

---

## 34. Currency Decision Matrix (REQUIRED)

| Decision | Business Rule | Runtime Owner | Authority | Validation Layer | Failure Behaviour | Evidence |
|---|---|---|---|---|---|---|
| Can merchant change currency? | Yes, supported ISO/reference currency | Tenant (Owner/Admin) | Tenant.Currency | RequireCurrency + SQL reference | 500 on invalid/persistence reject | SettingsController.cs:429-441 |
| Can CommerceHub change currency? | No | Platform | EGP constant | ResolveCurrency() | n/a (constant) | CommerceHubProjectionConsumer.cs:2932 |
| Can old orders change currency? | No | Order | Order.Currency | immutable stamp | never mutated | CheckoutService.cs:5533 |
| Can reports mix currencies? | Per surface only | Tenant/Platform | tenant or EGP | per-surface | EGP surfaces reject non-EGP | SuperAdminFinancialDashboardService.cs:3106 |
| Can payments override currency? | No FX override; provider evidence must match order | Payment runtime | Order.Currency + provider evidence | RequireMatch | mismatch/missing provider currency at capture | PaymentLifecycleService currency guard |
| Can receipts change currency? | No | Order | order.Currency | copy | n/a | WhatsAppDispatchAuthority.cs:133 |
| Can storefront ignore merchant currency? | No | Storefront SQL/projection | Tenant.Currency / storefront_configs | fail closed | throws on missing | currency.ts:69-91 |
| Can taxes use another currency? | No (tenant only) | Tenant | Tax.CurrencyCode | RequireCurrency | 500 on invalid | SettingsController.cs:181, 423 |
| Can non-EGP vendor enter CommerceHub? | No | CommerceHub | EGP | publication/cart/checkout reject | publication/update reject or STORE_CURRENCY_INVALID | CommerceHubCartService.cs:505 |
| Can marketplace event carry wrong currency? | No | Marketplace outbox | tenant currency | ValidateCurrencyAsync | reject enqueue | MarketplaceEventOutboxService.cs:39,57 |

---

## 35. Architectural Synthesis — What Currency Actually Is

**Currency in Shopixy is a DUAL-AUTHORITY FINANCIAL RUNTIME, not a simple setting and not a single Money runtime.**

It is definitively:
- **A Merchant Currency Runtime** — tenant-owned, SQL-authoritative, ISO-validated, propagated to storefront/cart/orders/receipts/reports via SQL-backed reads plus outbox+Mongo+Redis projection. Evidence: Tenant.cs:62, SettingsController.cs:209-241, TenantCurrencyProjectionConsumer.cs.
- **A Platform (CommerceHub) Currency Runtime pinned to EGP** — constant-enforced, RequireEgpCurrency-guarded, intentionally forked from tenant currency. Evidence: FinancialCurrencyGuard.cs:10, CommerceHubProjectionConsumer.cs:2932, CommerceHubFinancialTransaction.cs:84.

Why it is NOT merely "Configuration Authority": configuration is only the write entry point; the runtime spans propagation, validation, projection, and immutable order stamping.
Why it is NOT merely "Money Runtime": `Money` exists only in the payment runtime (PaymentRuntimeValueObjects.cs:31); the order/pricing domain is primitive-guarded, not value-object.
Why it is NOT merely "Pricing Runtime": pricing inherits currency; it does not own it.
Why it is NOT "Value Object Runtime": only one sub-domain models currency as a value object.
Why it is NOT "Monetary Policy Runtime": there is no FX/conversion/issuance policy — only validation and pinning.

**Conclusion: Currency = a two-authority financial propagation + validation runtime (Merchant authority + Platform-EGP authority) with an immutable order-currency snapshot and a proper Money value object in the payment sub-domain.**

---

## 36. MOST IMPORTANT QUESTION — Canonical Answer

**"How does one merchant-selected currency propagate through the entire platform while CommerceHub remains permanently EGP?"**

Implementation-backed canonical explanation:

1. The merchant currency lives ONLY in `Tenant.Currency` (PostgreSQL) — the single authority for the tenant's own surfaces (Tenant.cs:62).
2. When changed, `SettingsController.UpdateSettings` writes `Tenant.Currency` and atomically enqueues `TenantCurrencyChangedEvent` to the outbox (SettingsController.cs:209-241).
3. That event fans out to **tenant-facing projections only**: `TenantCurrencyProjectionConsumer` re-reads and ISO-validates SQL `Tenant.Currency`, then writes MongoDB `storefront_configs.currency` + invalidates Redis (TenantCurrencyProjectionConsumer.cs:48-185). Storefront, Cart, Checkout, Orders, Receipts, Affiliate, WhatsApp, Shipping, and merchant Reporting all read this tenant currency through SQL-backed config and projection paths.
4. At the **CommerceHub boundary the fork happens**: `CommerceHubProjectionConsumer` ALSO consumes `TenantCurrencyChangedEvent` but its `ResolveCurrency()` returns the constant `EGP` and overwrites every CommerceHub product document's currency to EGP (CommerceHubProjectionConsumer.cs:873-1689, 2932-2933). It never reads `Tenant.Currency` as financial authority.
5. CommerceHub additionally rejects non-EGP vendors at publication/update and before cart/checkout (`CommerceHubCartService.cs:505`, `CommerceHubCheckoutService.cs:462`), gates search eligibility on tenant EGP (`CommerceHubSearchSqlAuthorityOverlay.cs:120`), forces EGP in all finance entities via `RequireEgpCurrency` (CommerceHubFinancialTransaction.cs:84, CommerceHubVendorSettlement.cs:66, CommerceHubVendorPaymentRequest.cs:141), and pins ES docs to EGP (`CommerceHubSearchSqlAuthorityOverlay.cs:204`).
6. Orders created on the tenant side stamp `Order.Currency = validatedOrderCurrency` (tenant currency) immutably (CheckoutService.cs:5533) and carry it into receipts/ledger/merchant reports. CommerceHub orders are EGP because the boundary already pinned them.

**Therefore the merchant currency propagates everywhere the tenant owns, and is EXPLICITLY CONVERTED/REJECTED TO EGP at every CommerceHub touchpoint.** The two authorities never share a source of truth; they are deliberately forked, with SQL as tenant authority and the EGP constant as platform authority. This is why a USD merchant can run its POS/Storefront in USD while its CommerceHub marketplace presence is always EGP.

---

## 37. Certification

- Source of truth: implementation (`.cs`/`.ts`) only. No docs/comments/UI trusted.
- Key certifications corroborated by existing PASS docs: `commercehub-currency-authority-certification-findings.md` (EGP-only), `frontend-currency-authority-certification-findings.md` (fail-closed), and the settlement/payout/vendor-earnings currency certification findings (all EGP-guarded).
- Certification observations as of 2026-07-22: canonical fail-closed fixes applied for settings tenant authority fallback, tenant-currency projection ISO validation, payment lifecycle historical order-currency validation, Fawry missing provider currency, and COD missing admission currency. Remaining observations: legacy invoice/POS/accounting surfaces still lack first-class currency columns in places; some DTO/event defaults remain; production parity scanner and mixed-currency chaos proof remain open.
- Verdict: **Merchant Currency Runtime + Platform-EGP Runtime**, implementation-backed and canonical as of 2026-07-22, certified with observations.

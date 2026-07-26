# CERT-1 / PHASE B / B1 - Payment Method Selection Certification

Architecture Certification generated from source-code inspection only. Markdown documentation was not used as authority.

## Executive Summary

Payment Method Selection is owned by the tenant-scoped `TenantPaymentMethod` SQL write model and the POS Admin payment-method controller. Storefront React components may display and submit a customer selection, but the browser selection is only an admission candidate.

Payment methods may be changed by authenticated POS Admin users through `PaymentMethodsAdminController`, role-gated to `Owner`, `Admin`, and `Manager`; the recovery sync endpoint is further limited to `Owner` and `Admin`. Admin writes persist to PostgreSQL, publish payment-method events, and project storefront-readable records to MongoDB.

Storefront listing is validated by `StorefrontPaymentMethodsController` and `PaymentMethodReadRepository`: only tenant-scoped, enabled, non-deleted, supported, configuration-valid methods are exposed. If MongoDB projection data is stale, missing, or unavailable, the read repository falls back to SQL.

Final admission happens in `CheckoutService.ValidatePaymentMethodAsync`, which reloads the selected method from SQL `TenantPaymentMethods`, rejects missing, disabled, deleted, unsupported, or invalidly configured methods, maps an immutable payment-method snapshot, recomputes pricing, and enforces amount limits before order creation.

Final Certification: **PASS**

## Source Evidence

- Storefront selection UI: `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- Storefront state: `PointOfSale.Storefront/src/store/checkoutStore.ts`
- Checkout page refresh triggers: `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- Checkout recovery page: `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- Storefront payment-method read API: `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs`
- POS Admin mutation API: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`
- Checkout DTOs: `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- Checkout service: `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- SQL payment-method entity/configuration: `PointOfSale.Core/Entities/TenantPaymentMethod.cs`, `PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs`
- Configuration validator: `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`
- Mongo read model/repository/projection: `PointOfSale.Infrastructure/MongoDB/Documents/PaymentMethodDocument.cs`, `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs`, `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs`

## Storefront Flow

Payment methods are loaded on checkout mount by `CheckoutPage`, which calls `fetchPaymentMethods()` together with cart and shipping refresh. The checkout store calls `GET /api/storefront/payment-methods` with no-store headers and admits the response through `admitStorefrontPaymentMethods`.

The storefront admission function rejects malformed payloads, invalid UUIDs, invalid method codes, missing display names, unsupported online/offline flags, negative fees, invalid fee types, negative limits, invalid min/max ordering, duplicate IDs, and duplicate codes before placing methods into Zustand state.

Unavailable methods disappear when they are disabled or invalidly configured because `StorefrontPaymentMethodsController` maps only enabled projection/SQL records that are configuration-valid and supported. The current API maps returned methods with `IsAvailable = true`; the UI still has defensive disabled rendering for `isAvailable = false`.

Amount-limit unavailability is calculated in the UI from the backend pricing preview total when available, otherwise cart subtotal. The UI disables a method when the total is below `minOrderAmount` or above `maxOrderAmount`, and prevents continuation.

Stale selections are handled when a refreshed method list no longer contains the stored code in its available method codes. The store marks `paymentSelectionStatus = stale`, keeps the old code for user visibility, emits `CheckoutSelectionInvalidated` telemetry with reason `missing_from_latest_config`, excludes stale payment codes from pricing preview, and blocks checkout submission with `STALE_PAYMENT_SELECTION`.

Payment selection survives browser refresh through Zustand `persist` under `checkout-state`, storing `paymentMethod` and `selectedPaymentMethodCode` in local storage. Sensitive address/manual wallet session fields are persisted separately under `checkout-sensitive-session` in session storage and cleared after checkout/reset. Checkout recovery restores `paymentMethodCode` from recovered sessions, but clears manual-wallet proof fields and routes manual-wallet recovery back through payment re-entry.

## Checkout Authority

Browser selection is not authoritative. It is a customer-choice candidate submitted as `CheckoutRequestDto.PaymentMethod`.

Final payment-method authority exists in `CheckoutService.ValidatePaymentMethodAsync`. The service normalizes accepted aliases for COD and card/Stripe, loads `TenantPaymentMethods` from SQL for the tenant, ignores deleted rows, rejects missing rows, rejects `IsEnabled = false`, decrypts and validates configuration, and returns an authoritative snapshot object.

SQL revalidation occurs in both preview and final checkout. `PreviewPricingAsync` validates the payment method before calculating pricing and amount limits. `ProcessCheckoutAsync` validates it again before final pricing, amount-limit validation, inventory reservation, and order creation.

Client tampering is prevented by server-side validation. A forged payment code must exist in tenant SQL authority, be enabled, not deleted, and pass `PaymentMethodConfigurationValidator`. Forged fees, limits, display names, support flags, or availability from the browser are ignored because checkout maps those fields from SQL, not the request.

## Validation Rules

- Supported methods: `COD`, `STRIPE`, `PAYMOB`, `FAWRY`, and `MANUALWALLET` are supported by storefront exposure and configuration validation.
- Alias normalization: checkout maps `COD`, `CASH`, and `CASH_ON_DELIVERY` to `COD`; `STRIPE`, `CARD`, and `CREDIT_CARD` to `STRIPE`; `PAYMOB` to `PAYMOB`; other submitted codes are uppercased and then must pass SQL/configuration authority.
- Disabled methods: SQL `IsEnabled = false` is rejected in final checkout and hidden from storefront listing.
- Missing methods: absence from tenant SQL raises checkout payment-method rejection.
- Deleted methods: SQL reads require `!IsDeleted`; projection tombstones set `IsEnabled = false`, `IsConfigurationValid = false`, and `IsDeleted = true`.
- Configuration validity: `COD` needs no config; `STRIPE` requires `publishableKey`, `secretKey`, `webhookSecret`; `PAYMOB` requires `apiKey`, `integrationId`, `iframeId`, `hmacSecret`; `FAWRY` requires `merchantCode`, `apiKey`, `webhookSecret`; `MANUALWALLET` requires `walletNumber`.
- Amount limits: server validates `MinOrderAmount` and `MaxOrderAmount` against the authoritative recalculated total in preview and final checkout.
- Duplicate methods: SQL enforces a unique `(TenantId, Code)` index; frontend admission rejects duplicate response IDs or codes.
- Selection changes: selecting a method updates `selectedPaymentMethodCode`, `paymentMethod`, status, manual-wallet fields, and autosaves the checkout session.
- Refresh behavior: checkout mount refetches methods; persisted codes are retained only as valid or stale; stale codes are not sent into pricing preview and cannot submit.
- Projection freshness: projection consumers re-read SQL, require exact SQL event version, fence duplicate events, and only apply Mongo writes newer by event timestamp/version. Read repository falls back to SQL when projection records lack configuration-valid evidence or Mongo fails.

## State Management

React state is held in Zustand `checkoutStore`. Payment method fields are `paymentMethods`, `selectedPaymentMethodCode`, `paymentMethod`, `paymentSelectionStatus`, and `stalePaymentSelection`.

Persistence stores only `shippingMethodId`, `paymentMethod`, `selectedPaymentMethodCode`, and `step` under local storage key `checkout-state`. Sensitive checkout data is stored separately in session storage and cleared on checkout completion and reset.

Reset clears autosave timers, aborts pending checkout/pricing requests, clears sensitive checkout session storage, and restores the initial checkout state.

Recovery rehydrates recovered payment codes into state, but manual-wallet evidence is intentionally cleared and must be re-entered.

## DTO Certification

`CheckoutRequestDto.PaymentMethod` is required, non-empty, and length-limited. It is not treated as final authority.

Manual-wallet selection adds DTO validation for wallet type, phone, and proof file key. Supported wallet types are `VodafoneCash`, `Instapay`, and `OrangeCash`; values are normalized before downstream use.

`CheckoutExpectedPricingDto` requires non-negative pricing components and verifies discount and total arithmetic, including `PaymentFee`. Final checkout still recalculates and compares current backend pricing, so DTO totals are evidence, not authority.

Order response DTO contains backend-owned payment identity fields such as `PaymentIntentId`, `PaymentStatus`, provider handoff artifacts, manual-wallet fields, and totals. The frontend validates response shape before rendering provider handoff evidence, but durable payment authority remains backend-owned.

## Runtime Boundary

Payment Method Selection ends at customer choice admission: display eligible methods, accept one selected code as candidate, refresh price with that candidate, and validate/admit the candidate against SQL.

Payment Method Selection does not own checkout execution, payment runtime, provider calls, provider secrets, orders, inventory, settlement, commission, marketplace, or CommerceHub. Those concerns occur after selection admission or in separate services/controllers.

The inspected checkout flow may create payment intent/session evidence after order creation for payment-capable methods. That is outside the Payment Method Selection boundary and is not certified here as provider runtime.

## Business Rules

- A tenant has isolated payment-method configurations.
- Storefront is read-only for payment methods.
- Storefront payment-method responses contain no encrypted configuration secrets.
- Only enabled, supported, configuration-valid payment methods can be displayed to customers.
- Browser-selected payment method is a candidate only.
- Checkout must re-read payment-method authority from SQL.
- Invalid, disabled, deleted, unsupported, or missing payment methods fail checkout.
- Provider methods requiring configuration cannot be enabled or admitted without valid required fields.
- Manual wallet requires customer wallet type, phone number, proof key, and tenant wallet configuration.
- Payment fees and amount limits are backend-derived from the SQL payment-method snapshot.
- Amount limits are checked against authoritative recalculated totals.
- Stale persisted selections are blocked until the customer selects a currently available method.
- Projection reads must be tenant-scoped and recover by SQL fallback when stale or unavailable.

## Critical Invariants

- SQL `TenantPaymentMethods` is final payment-method selection authority.
- Mongo payment-method documents are read models only.
- Storefront selection is not trusted for fee, limit, support, enabled, or configuration truth.
- Disabled methods cannot be admitted even if submitted directly to checkout.
- Configuration-invalid methods cannot be exposed or admitted.
- Payment-method code uniqueness is tenant-scoped.
- Final checkout pricing must include the backend-calculated payment fee.
- Stale selection state prevents review/submission.
- Manual-wallet sensitive proof state is not restored as durable client authority.
- Payment Method Selection does not execute or settle payments.

## Architecture Boundaries

Payment Method Selection owns:

- Customer payment-method choice.
- Storefront display of selectable methods.
- Candidate selection state.
- Stale-selection handling.
- Admission validation against tenant SQL authority.
- Payment-method snapshot contribution to pricing/order creation.

Payment Method Selection does not own:

- Checkout execution.
- Payment runtime.
- Provider calls.
- Provider secrets.
- Orders.
- Inventory.
- Settlement.
- Commission.
- CommerceHub.

## Architecture Risks

No architectural violations were identified.

## Certification Checklist

| Area | Result |
|---|---|
| Selection | PASS |
| Validation | PASS |
| State Management | PASS |
| DTO Safety | PASS |
| Authority | PASS |
| Runtime Boundary | PASS |
| Business Rules | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS |

## Final Certification

PASS

# CERT-1 PHASE A A2 - Provider Configuration Certification

Certification Date: 2026-07-20  
Certification Type: Architecture Certification  
Scope: POS Admin Provider Configuration for standalone POS Storefront payments  
Source of Truth: Source code implementation only

## Executive Summary

Provider Configuration owns tenant-scoped storefront payment method configuration. It owns payment method identity, enabled state, display metadata, fee and order-amount limits, sandbox flag, encrypted provider configuration, validation status, and safe public projection metadata.

Provider Configuration configures five implemented payment methods: `COD`, `STRIPE`, `PAYMOB`, `FAWRY`, and `MANUALWALLET`. These are the only payment method codes admitted by the POS Admin controller and shared validator.

Provider Configuration does not own checkout creation, order pricing, payment execution, provider adapter calls, webhook processing, refunds, financial posting, inventory, settlement, payout, or CommerceHub marketplace administration. Checkout and runtime services consume provider configuration; they do not mutate it.

Certification result: PASS.

## Implementation Evidence

Primary implementation evidence:

- Controller: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`
- Storefront read controller: `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs`
- Entity: `PointOfSale.Core/Entities/TenantPaymentMethod.cs`
- EF configuration: `PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs`
- Validator: `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`
- Events: `PointOfSale.Core/Events/PaymentMethodEvents.cs`
- Projection consumer: `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs`
- Read model and repository: `PointOfSale.Infrastructure/MongoDB/Documents/PaymentMethodDocument.cs`, `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs`
- Checkout/runtime consumers: `PointOfSale.API/Services/Checkout/CheckoutService.cs`, provider tenant resolvers and runtime application services under `PointOfSale.API/Services/Payments`
- Admin UI: `PointOfSale.Web/src/pages/PaymentMethods.jsx`
- Storefront UI/store: `PointOfSale.Storefront/src/store/checkoutStore.ts`, `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`, `PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx`
- Tests: `PointOfSale.Tests/Admin/PaymentMethodsAdminSafetyTests.cs`, `PointOfSale.Tests/Admin/PaymentMethodsAdminSyncReliabilityTests.cs`, `PointOfSale.Web/src/tests/storefrontUxStateStability.test.ts`, `PointOfSale.Tests/Architecture/VodafoneCashPaymentAuthorityCertificationTests.cs`

## Provider Model

### Cash on Delivery (`COD`)

- Configuration model: tenant payment method row with no provider credential requirement. `RequiresConfiguration` returns false for `COD`.
- Credential model: none implemented.
- Sandbox support: `TenantPaymentMethod.IsSandbox` exists but `COD` has no gateway credential or runtime environment dependency.
- Production support: implemented as an offline method; no provider endpoint.
- Dependencies: checkout and payment service treat COD as non-gateway/offline.
- Validation: supported code; no required configuration.
- Runtime mapping: checkout maps `COD`, `CASH`, and `CASH_ON_DELIVERY` to `COD`; payment service maps cash payment method to `PaymentProvider.Cod`.
- Evidence: `PaymentMethodsAdminController.cs:532`, `PaymentMethodConfigurationValidator.cs:23`, `CheckoutService.cs:4955`, `PaymentService.cs:1869`.

### Stripe (`STRIPE`)

- Configuration model: tenant-scoped method with encrypted JSON configuration.
- Credential model: `publishableKey`, `secretKey`, `webhookSecret`.
- Sandbox support: stored by `IsSandbox`; runtime validates `pk_test_` and `sk_test_`/`rk_test_` prefixes when sandbox is active.
- Production support: runtime validates `pk_live_` and `sk_live_`/`rk_live_` prefixes when production is active.
- Dependencies: `StripeTenantConfigurationResolver`, `StripeRuntimeApplicationService`, tenant-scoped Stripe client.
- Validation: required `publishableKey`, `secretKey`, `webhookSecret`; `apiKey` is normalized as `secretKey` for Stripe compatibility.
- Runtime mapping: checkout maps `STRIPE` to `PaymentProvider.Stripe`; runtime returns Stripe `clientSecret` and publishable key to the browser after order/payment-intent creation.
- Evidence: `PaymentMethodConfigurationValidator.cs:23-26`, `PaymentMethodConfigurationValidator.cs:84-88`, `StripeTenantConfigurationResolver.cs:68-96`, `StripeTenantConfigurationResolver.cs:159-184`, `StripeRuntimeApplicationService.cs:41-51`, `StripeRuntimeApplicationService.cs:282-287`, `StorefrontStripePaymentsController.cs:127-141`.

### Paymob (`PAYMOB`)

- Configuration model: tenant-scoped method with encrypted JSON configuration.
- Credential model: `apiKey`, `integrationId`, `iframeId`, `hmacSecret`.
- Sandbox support: stored by `IsSandbox`; runtime requires active options to be sandbox-compatible.
- Production support: runtime requires active options to be production-compatible.
- Dependencies: `PaymobTenantConfigurationResolver`, `PaymobRuntimeApplicationService`, Paymob runtime bridge/adapter.
- Validation: required `apiKey`, `integrationId`, `iframeId`, `hmacSecret`; runtime additionally requires numeric `integrationId` and `iframeId`.
- Runtime mapping: checkout maps `PAYMOB` to `PaymentProvider.Paymob`; storefront Paymob endpoint returns generated iframe URL and payment key, not stored credentials.
- Evidence: `PaymentMethodConfigurationValidator.cs:29-33`, `PaymobTenantConfigurationResolver.cs:69-98`, `PaymobTenantConfigurationResolver.cs:188-209`, `PaymobRuntimeApplicationService.cs:36-43`, `PaymobRuntimeApplicationService.cs:280-287`, `StorefrontPaymobPaymentsController.cs:250-264`.

### Fawry (`FAWRY`)

- Configuration model: tenant-scoped method with encrypted JSON configuration.
- Credential model: `merchantCode`, `apiKey`, `webhookSecret`. Runtime resolver maps `webhookSecret` into the Fawry secure key field.
- Sandbox support: stored by `IsSandbox`; runtime enforces sandbox option and capability compatibility.
- Production support: runtime enforces production option and capability compatibility.
- Dependencies: `FawryTenantConfigurationResolver`, `FawryRuntimeApplicationService`, `FawryRuntimeBridge`, `FawryPaymentAdapter`.
- Validation: required `merchantCode`, `apiKey`, `webhookSecret`; `securityKey` is normalized as `apiKey` for compatibility.
- Runtime mapping: checkout maps `FAWRY` to `PaymentProvider.Fawry`; payment service invokes Fawry runtime application service.
- Evidence: `PaymentMethodConfigurationValidator.cs:36-39`, `PaymentMethodConfigurationValidator.cs:91-95`, `FawryTenantConfigurationResolver.cs:76-118`, `FawryTenantConfigurationResolver.cs:220-241`, `FawryRuntimeApplicationService.cs:39-63`, `PaymentService.cs:2593-2612`.

### Manual Wallet (`MANUALWALLET`)

- Configuration model: tenant-scoped method with encrypted JSON configuration and a storefront-safe public projection of `walletNumber`.
- Credential model: required `walletNumber`; UI also accepts `walletName`, but validator does not require it and public projection exposes only `walletNumber`.
- Sandbox support: stored by `IsSandbox`; manual payment runtime requires tenant environment to match configured runtime options.
- Production support: runtime requires production option and capability compatibility.
- Dependencies: `ManualPaymentTenantConfigurationResolver`, manual payment runtime/evidence/review services, manual wallet storefront config/proof endpoints.
- Validation: required `walletNumber`; checkout requires configured wallet details before completing manual-wallet checkout.
- Runtime mapping: checkout maps `MANUALWALLET` to `PaymentProvider.ManualWallet`; storefront displays merchant wallet number and requires wallet type, customer wallet phone, and uploaded proof.
- Evidence: `PaymentMethodConfigurationValidator.cs:42-43`, `PaymentMethodConfigurationValidator.cs:142-151`, `ManualPaymentRuntimeApplicationService.cs:89-160`, `ManualPaymentRuntimeApplicationService.cs:275-282`, `StorefrontManualWalletController.cs:57-94`, `CheckoutService.cs:3196-3230`, `ManualWalletFields.tsx:21-80`.

## Configuration Ownership

Provider configuration is owned by the tenant's POS Admin context.

- Mutation authority is `PaymentMethodsAdminController`, routed at `api/admin/payment-methods` and protected by `[Authorize(Roles = "Owner,Admin,Manager")]`.
- Sync/recovery authority is narrower: `SyncPaymentMethods` is additionally protected by `[Authorize(Roles = "Owner,Admin")]`.
- Storefront reads are anonymous but read-only, tenant-scoped, and expose only enabled, valid, supported methods.
- Checkout, payment runtime services, provider resolvers, and storefront payment controllers consume configuration but do not own mutation.
- Super Admin provider administration is outside this POS Admin Provider Configuration module and is not certified as an owner for this standalone storefront scope.

Evidence: `PaymentMethodsAdminController.cs:30-31`, `PaymentMethodsAdminController.cs:454-456`, `StorefrontPaymentMethodsController.cs:27-28`, `StorefrontPaymentMethodsController.cs:76-102`, `CheckoutService.cs:4758-4807`.

## Configuration Storage

The write source of truth is PostgreSQL `TenantPaymentMethods`.

- Entity fields include `TenantId`, `Code`, `DisplayName`, `IsEnabled`, `SupportsOnline`, `SupportsOffline`, `IsSandbox`, `ConfigEncrypted`, order limits, fee fields, and version.
- EF enforces a unique tenant/code index and tenant-enabled lookup index.
- `ConfigEncrypted` is a text column containing Data Protection protected serialized JSON.
- Data Protection uses purpose `PaymentMethodSecrets` consistently in admin, projection, repository fallback, checkout, and provider resolvers.
- Data Protection keys are persisted to the filesystem and scoped to application name `PointOfSale` unless configured otherwise.
- MongoDB `payment_methods` is a read projection. It stores tenant/payment-method identifiers, display metadata, availability, configuration-valid flag, `HasConfiguration`, and safe `PublicConfiguration`; it does not store encrypted secrets.
- The Mongo read repository falls back to SQL if projection data is missing, stale, or Mongo is unavailable.
- No explicit in-memory provider-configuration cache was identified in the audited implementation.

Evidence: `TenantPaymentMethod.cs:28-144`, `TenantPaymentMethodConfiguration.cs:16-69`, `PaymentMethodsAdminController.cs:41-53`, `PaymentMethodsAdminController.cs:873`, `Program.cs:1106-1126`, `PaymentMethodDocument.cs:31-168`, `PaymentMethodReadRepository.cs:46-97`, `PaymentMethodProjectionConsumer.cs:237-266`.

## Provider Credentials

| Provider | Implemented Credential | Storage | Browser Exposure | Runtime Use |
|---|---|---|---|---|
| Stripe | `publishableKey` | Encrypted JSON in `ConfigEncrypted` | Returned only by Stripe payment-intent response for Stripe.js | Runtime credential resolver and Stripe client |
| Stripe | `secretKey` | Encrypted JSON | Not returned by admin/storefront config APIs | Tenant-scoped Stripe client API key |
| Stripe | `webhookSecret` | Encrypted JSON | Not returned | Stripe webhook/runtime credential authority |
| Paymob | `apiKey` | Encrypted JSON | Not returned | Paymob credential handoff |
| Paymob | `integrationId` | Encrypted JSON | Not returned directly | Paymob intent/iframe generation |
| Paymob | `iframeId` | Encrypted JSON | Not returned directly | Paymob iframe URL generation |
| Paymob | `hmacSecret` | Encrypted JSON | Not returned | Paymob callback/webhook validation |
| Fawry | `merchantCode` | Encrypted JSON | Not returned as configuration | Fawry runtime credentials |
| Fawry | `apiKey` | Encrypted JSON | Not returned | Fawry runtime credentials |
| Fawry | `webhookSecret` | Encrypted JSON | Not returned | Used as Fawry secure key |
| Manual Wallet | `walletNumber` | Encrypted JSON plus public projection | Returned intentionally as merchant payment destination | Manual wallet display and checkout response |

Admin DTOs return validity and errors, not credential values. Empty payloads do not wipe stored credentials. Decryption failures are treated as invalid and require explicit reset before replacement. The only implemented browser exposure of a stored provider value is the safe/manual `walletNumber` public configuration and Stripe publishable key during runtime payment initiation.

Evidence: `PaymentMethodConfigurationValidator.cs:23-43`, `PaymentMethodConfigurationValidator.cs:138-151`, `PaymentMethodsAdminController.cs:613-636`, `PaymentMethodsAdminController.cs:807-873`, `StripeRuntimeApplicationService.cs:103-104`, `StorefrontManualWalletController.cs:80-94`.

## Validation Rules

Implemented validation rules:

- Only `COD`, `STRIPE`, `PAYMOB`, `FAWRY`, and `MANUALWALLET` are valid admin codes.
- Unsupported codes fail validation.
- Provider configuration required fields are enforced per provider.
- `STRIPE.apiKey` aliases to `secretKey`; `FAWRY.securityKey` aliases to `apiKey`.
- `configure` rejects empty configuration payloads.
- `upsert` ignores empty configuration payloads and preserves existing secrets.
- Reset is supported only for configurable methods.
- Reset requires `confirmReset=true`.
- Reset cannot be combined with new provider values.
- Reset clears `ConfigEncrypted` and disables the method.
- If stored configuration cannot be decrypted, mutation fails with reset-required semantics.
- Enabling a configurable method requires currently valid configuration.
- Storefront suppresses invalid payment method projections.
- Checkout revalidates method existence, enabled state, configuration validity, and min/max order amount limits before order submission.
- Runtime credential resolvers enforce provider capability flags and environment compatibility.

Evidence: `PaymentMethodsAdminController.cs:532`, `PaymentMethodConfigurationValidator.cs:23-47`, `PaymentMethodConfigurationValidator.cs:84-120`, `PaymentMethodsAdminController.cs:735-873`, `PaymentMethodsAdminController.cs:885-945`, `StorefrontPaymentMethodsController.cs:80-102`, `CheckoutService.cs:4758-4807`, `CheckoutService.cs:5021-5043`.

## Environment Management

Environment is persisted per payment method as `TenantPaymentMethod.IsSandbox`.

Admin UI exposes a sandbox toggle in provider configuration. Backend persists `IsSandbox` on upsert/configure. Runtime resolvers compare tenant `IsSandbox` with active provider options and reject environment mismatches.

Stripe validates sandbox/production prefixes for publishable and secret keys. Paymob validates sandbox/production capabilities and option state. Fawry and Manual Payment validate tenant sandbox state against active runtime environment options.

Evidence: `TenantPaymentMethod.cs:89`, `PaymentMethodsAdminController.cs:155`, `PaymentMethodsAdminController.cs:258-262`, `PaymentMethods.jsx:702-713`, `StripeTenantConfigurationResolver.cs:159-184`, `PaymobTenantConfigurationResolver.cs:188-209`, `FawryTenantConfigurationResolver.cs:220-228`, `ManualPaymentRuntimeApplicationService.cs:275-282`.

## Runtime Integration Boundary

The implemented runtime boundary is:

Provider Configuration -> Storefront payment-method read API -> Checkout validation/order creation -> Payment runtime application service -> Provider adapter.

Provider Configuration does not execute provider payments directly. It persists and validates settings. Checkout and provider runtime services consume settings and execute runtime behavior.

Checkout validates payment method authority from `TenantPaymentMethods`, maps method codes to payment providers, stores payment method snapshots on the order, and calls payment service/runtime execution only after order/checkout admission. Provider runtime services resolve encrypted tenant credentials immediately before provider execution.

Evidence: `StorefrontPaymentMethodsController.cs:76-102`, `CheckoutService.cs:1151-1224`, `CheckoutService.cs:5584-5587`, `CheckoutService.cs:6634-6742`, `PaymentService.cs:156-196`, `StripeRuntimeApplicationService.cs:41-51`, `PaymobRuntimeApplicationService.cs:36-43`, `FawryRuntimeApplicationService.cs:39-63`.

## Security Certification

Implemented controls:

- Admin mutation endpoint requires POS roles `Owner`, `Admin`, or `Manager`.
- Sync endpoint requires `Owner` or `Admin`.
- Tenant ownership is enforced by `TenantId` query filters and explicit tenant predicates.
- `ConfigEncrypted` is protected with ASP.NET Core Data Protection purpose `PaymentMethodSecrets`.
- Admin responses return configuration status/errors, not credential values.
- Storefront payment-method list returns no secrets and suppresses invalid configurations.
- Mongo projection stores only public-safe configuration, currently `MANUALWALLET.walletNumber`.
- Decryption failures do not leak stored values and cause invalid/reset-required handling.
- Reset requires explicit confirmation and disables the method.
- Manual wallet proof upload requires a dedicated upload token, validates checkout session identity, validates file content, and stores tenant-scoped object keys.
- Checkout local storage partialization excludes manual-wallet upload token/proof and clears sensitive checkout session storage after submission.

Evidence: `PaymentMethodsAdminController.cs:30-31`, `PaymentMethodsAdminController.cs:613-636`, `PaymentMethodsAdminController.cs:748-787`, `PaymentMethodsAdminController.cs:839-873`, `StorefrontPaymentMethodsController.cs:57-102`, `PaymentMethodDocument.cs:130-142`, `StorefrontManualWalletController.cs:101-236`, `checkoutStore.ts:1989-1993`, `checkoutStore.ts:1747-1761`.

## Frontend Certification

The POS Admin UI implements:

- Admin route to `ecommerce/payment-methods`.
- Load from `/admin/payment-methods`.
- Fallback display definitions for the five implemented method codes.
- Edit modal for display names, descriptions, sort order, fee, fee type, min/max order amount.
- Configure modal for provider fields.
- Sandbox mode selector.
- Sensitive fields rendered as password inputs.
- Save flow to `/admin/payment-methods/{code}/configure`.
- Enable/disable flow to `/admin/payment-methods/{code}/enable` and `/disable`.
- Reset flow requiring explicit confirmation before submit.
- Client-side prevention of empty configuration save and reset-plus-values conflict.
- Error display with validation details from API.

The Storefront UI implements:

- Fetch from `/api/storefront/payment-methods`.
- Admission of server payment-method response shape.
- Payment method selection from server-provided methods.
- Min/max amount UI disablement.
- Manual Wallet fields only when `MANUALWALLET` is selected.
- Manual wallet config fetch, wallet destination display, wallet type/phone/proof validation, upload/clear proof flows, and upload-token use.

Evidence: `App.jsx:300`, `PaymentMethods.jsx:33-81`, `PaymentMethods.jsx:202-286`, `PaymentMethods.jsx:322-360`, `PaymentMethods.jsx:683-752`, `checkoutStore.ts:57`, `checkoutStore.ts:664`, `checkoutStore.ts:1250-1265`, `PaymentStep.tsx:240-254`, `ManualWalletFields.tsx:21-80`, `ManualWalletFields.tsx:129-145`.

## Business Rules

Implementation-backed business rules:

- Provider configuration belongs to a tenant/store.
- One payment method code is unique per tenant.
- POS Admin owns mutation.
- Storefront reads enabled, valid, supported methods only.
- Provider credentials are encrypted at rest.
- Admin configuration APIs do not return credential values.
- Empty configuration payloads do not erase secrets.
- Configuration reset is explicit and disables the payment method.
- Configurable methods cannot be enabled until valid.
- COD requires no gateway configuration.
- Stripe, Paymob, Fawry, and Manual Wallet require provider configuration.
- Manual Wallet exposes merchant wallet number as a public payment destination.
- Checkout validates payment method availability and configuration before order creation.
- Checkout stores payment method code/name snapshot on the order.
- Orders start unpaid; COD is not auto-marked paid by provider configuration.
- Runtime services resolve credentials at execution time.

Evidence: `TenantPaymentMethodConfiguration.cs:21-31`, `PaymentMethodsAdminController.cs:358-404`, `PaymentMethodsAdminController.cs:735-873`, `PaymentMethodConfigurationValidator.cs:117-120`, `StorefrontPaymentMethodsController.cs:100-102`, `CheckoutService.cs:4758-4807`, `CheckoutService.cs:5584-5587`.

## Critical Invariants

- Provider configuration is tenant-owned.
- Provider configuration mutation is POS Admin authority.
- Storefront payment methods are read-only projections.
- Secrets never return from admin configuration APIs.
- Invalid configurations are not exposed by the storefront payment-method list.
- Checkout does not bypass provider configuration validation.
- Provider runtime does not own configuration mutation.
- Configuration does not execute payments.
- Reset clears provider configuration and disables the method.
- Manual Wallet public wallet number is the only provider configuration value intentionally projected for customer display.

Evidence: `PaymentMethodsAdminController.cs:30-31`, `TenantPaymentMethod.cs:28-104`, `StorefrontPaymentMethodsController.cs:76-102`, `PaymentMethodProjectionConsumer.cs:237-266`, `CheckoutService.cs:4758-4807`, `StripeTenantConfigurationResolver.cs:68-96`, `PaymobTenantConfigurationResolver.cs:69-98`, `FawryTenantConfigurationResolver.cs:76-118`.

## Architecture Boundaries

Provider Configuration owns:

- Provider Settings
- Credentials
- Sandbox Configuration
- Provider Metadata
- Validation
- Configuration Status
- Storefront-safe public provider metadata

Provider Configuration does not own:

- Checkout
- Payment Runtime
- Payment Execution
- Orders
- Inventory
- Refunds
- Webhooks
- Financial Posting
- CommerceHub Marketplace
- Marketplace Settlement
- Marketplace Commission
- Vendor Earnings

Evidence: mutation logic resides in `PaymentMethodsAdminController.cs`; checkout/payment execution resides separately in `CheckoutService.cs`, `PaymentService.cs`, provider runtime application services, and provider adapters.

## Architecture Risks

No architectural violations were identified.

## Certification Checklist

| Area | Result | Evidence |
|---|---|---|
| Ownership | PASS | POS Admin controller authorization and tenant context |
| Configuration Model | PASS | `TenantPaymentMethod` and EF configuration |
| Credential Management | PASS | Data Protection encrypted JSON; no admin secret return |
| Validation | PASS | Shared provider validator and admin mutation guards |
| Environment | PASS | `IsSandbox` persistence and runtime environment checks |
| Security | PASS | Role checks, tenant isolation, secret suppression, reset controls |
| Runtime Boundary | PASS | Checkout/runtime consume configuration; admin config does not execute payments |
| Frontend | PASS | Admin config UI and storefront checkout selection implemented |
| Business Rules | PASS | Rules enforced in controller, validator, checkout, and tests |
| Critical Invariants | PASS | Tenant-owned, read-only storefront projection, checkout revalidation |
| Architecture Boundaries | PASS | Configuration separated from checkout/payment execution |
| Overall Certification | PASS | No implementation-backed architectural violations identified |

## Test Evidence

Implemented tests verify:

- Empty upsert preserves existing encrypted secrets.
- Empty configure payload is rejected and preserves secrets.
- Reset requires explicit confirmation and clears config only when confirmed.
- Invalid provider configuration prevents enabling.
- Valid configuration permits enabling.
- Sync endpoint does not report success before commit.
- Storefront checkout preserves stale payment selections as stale rather than silently changing flow.
- Manual Wallet authority boundaries are asserted across frontend/backend/runtime evidence.

Evidence: `PaymentMethodsAdminSafetyTests.cs:22`, `PaymentMethodsAdminSafetyTests.cs:85`, `PaymentMethodsAdminSafetyTests.cs:133`, `PaymentMethodsAdminSafetyTests.cs:230`, `PaymentMethodsAdminSafetyTests.cs:272`, `PaymentMethodsAdminSyncReliabilityTests.cs:22`, `PaymentMethodsAdminSyncReliabilityTests.cs:51`, `storefrontUxStateStability.test.ts:266`, `VodafoneCashPaymentAuthorityCertificationTests.cs:6`, `VodafoneCashPaymentAuthorityCertificationTests.cs:53`.

## Final Certification

PASS

Justification: the implementation establishes tenant-owned POS Admin provider configuration with encrypted credential storage, provider-specific validation, environment checks, read-only storefront projection, checkout revalidation, and separate runtime execution boundaries. No architectural violations were identified in the audited source code.

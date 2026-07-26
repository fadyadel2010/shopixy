# CERT-1 A1 Store Payment Settings Certification

Certification: CERT-1  
Phase: A - Administration  
Area: A1 - Store Payment Settings  
Date: 2026-07-20  
Scope: POS Admin Store Payment Settings and POS Storefront checkout consumption only  
Decision: PASS

## Executive Summary

Store Payment Settings are the tenant-scoped administration boundary for Storefront payment-method configuration. The module owns the configured payment-method catalog for a store: method code, display labels, descriptions, enabled state, online/offline capability flags, sandbox flag, icon, minimum and maximum order amounts, fee, fee type, provider configuration storage, configuration validity, projection events, and Storefront-safe read projection.

Store Payment Settings control which implemented payment methods can be exposed to Storefront checkout and which provider configuration is available to payment runtime. The authoritative write model is SQL `TenantPaymentMethods`; Storefront reads use Mongo projection with SQL fallback and final checkout revalidates against SQL.

Store Payment Settings do not execute payment processing. They do not create payment intents, call providers, perform checkout, execute refunds, process webhooks, post financial ledger entries, own orders, own inventory, or own shipping.

## Evidence Reviewed

- `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`
- `PointOfSale.Core/Entities/TenantPaymentMethod.cs`
- `PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs`
- `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`
- `PointOfSale.Web/src/pages/PaymentMethods.jsx`
- `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/PaymentMethodDocument.cs`
- `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Pricing/StorefrontPricingService.cs`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- Relevant tests under `PointOfSale.Tests/Admin`, `PointOfSale.Tests/Storefront`, and `PointOfSale.Tests/Architecture`.

## Module Ownership

Store Payment Settings are tenant-owned. `TenantPaymentMethod` implements `ITenantOwned`, has a required `TenantId`, and EF enforces a unique `(TenantId, Code)` index.

POS Admin owns configuration through `PaymentMethodsAdminController` at `api/admin/payment-methods`. The controller resolves tenant scope from `ITenantContext.RequiredTenantId` and queries/mutates `TenantPaymentMethods` with explicit tenant predicates.

The Storefront may only read enabled, valid, supported methods through `GET /api/storefront/payment-methods`. The Storefront stores selected payment-method codes as candidates only.

Checkout consumes Store Payment Settings by revalidating the selected method against SQL `TenantPaymentMethods` before pricing preview/final order creation and by snapshotting admitted method code/name/fee on the order.

Within this standalone POS Storefront scope, unauthenticated customers, browser state, React/Zustand state, Mongo projection alone, Redis/cache state, and unsupported modules have no authority to modify Store Payment Settings.

## Aggregate Ownership

Implemented aggregates and records:

| Aggregate / Record | Implemented owner | Notes |
|---|---|---|
| Store / Tenant | SQL tenant context | Payment methods are scoped by `TenantId`. |
| Payment Configuration | SQL `TenantPaymentMethods` | Authoritative write model. |
| Payment Method | `TenantPaymentMethod` | One method code per tenant. |
| Provider Configuration | `ConfigEncrypted` | Encrypted JSON provider configuration. |
| Credential Reference | `ConfigEncrypted` plus config validity flags | Secrets are stored encrypted; DTOs return validity, not plaintext. |
| Environment | `IsSandbox` | Admin-configurable sandbox flag. |
| Display Rule | display name, Arabic display name, description, icon | Used by admin and Storefront presentation. |
| Visibility Rule | `IsEnabled`, `IsConfigurationValid`, supported code suppression | Storefront only receives enabled, valid, supported methods. |
| Sort Order | `SortOrder` | Admin and Storefront reads order by sort order. |
| Fee Configuration | `Fee`, `FeeType` | Backend pricing calculates payment fee. |
| Projection | `PaymentMethodDocument` | Derivative Mongo read model with SQL fallback. |

## Configuration Model

Implemented configurable properties:

| Property | Source of truth | Certification |
|---|---|---|
| `Code` | SQL `TenantPaymentMethods.Code` | Restricted to `COD`, `STRIPE`, `PAYMOB`, `FAWRY`, `MANUALWALLET` in admin controller. |
| `DisplayName`, `DisplayNameAr` | SQL | Editable through admin upsert. |
| `Description`, `DescriptionAr` | SQL | Editable through admin upsert. |
| `IsEnabled` | SQL | Changed by enable/disable endpoints; Storefront visibility depends on it. |
| `SortOrder` | SQL | Editable through admin upsert; used for ordering. |
| `SupportsOnline`, `SupportsOffline` | Backend-derived from code | Online: Stripe, Paymob, Fawry. Offline: COD, Manual Wallet. |
| `IsSandbox` | SQL | Editable through admin upsert/configure. |
| `Icon` | SQL | Editable through admin upsert or defaulted by code. |
| `MinOrderAmount`, `MaxOrderAmount` | SQL | Exposed to Storefront and enforced in checkout against authoritative totals. |
| `Fee`, `FeeType` | SQL | Exposed to Storefront; backend pricing computes final fee. |
| Provider config | encrypted SQL `ConfigEncrypted` | Updated via configure/upsert; never returned in DTOs. |
| Reset / Confirm Reset | configure DTO | Clears config and disables method only with explicit confirmation. |

Not implemented as Store Payment Settings properties: currency restrictions, country/region restrictions, customer/product restrictions, provider capability registry, or independent availability schedules. Currency validation exists in checkout/payment runtime, not as payment-method settings.

## Supported Payment Methods

| Business name | Internal identifier | Configuration options | Environment | Checkout availability | Dependencies |
|---|---|---|---|---|---|
| Cash on Delivery | `COD` | Display fields, sort order, icon, min/max amount, fee, fee type, enabled flag | `IsSandbox` field exists but COD requires no gateway config | Visible when enabled and valid; checkout keeps order unpaid | Fulfillment/payment lifecycle handles collection; no gateway config. |
| Stripe Card Payment | `STRIPE` | `publishableKey`, `secretKey`, `webhookSecret`, plus common display/fee/limit fields | `IsSandbox` | Visible only when enabled and config-valid | Stripe runtime creates provider handoff; finality remains backend lifecycle-owned. |
| Paymob Online Payment | `PAYMOB` | `apiKey`, `integrationId`, `iframeId`, `hmacSecret`, plus common fields | `IsSandbox` | Visible only when enabled and config-valid | Paymob runtime creates iframe/payment-key handoff; finality remains backend lifecycle-owned. |
| Fawry Pay | `FAWRY` | `merchantCode`, `apiKey`, `webhookSecret`, plus common fields | `IsSandbox` | Visible only when enabled and config-valid | Fawry payment runtime/handoff is backend-owned. |
| Manual Wallet | `MANUALWALLET` | `walletNumber` required; `walletName` UI field is accepted as extra config but only `walletNumber` is public config | `IsSandbox` | Visible only when enabled and config-valid | Manual proof upload, admin review, and lifecycle approval/rejection are outside settings. |

Vodafone Cash, Instapay, and Orange Cash are implemented as wallet-type candidates inside the `MANUALWALLET` payment lane, not separate payment-method records.

## Provider Mapping

| Payment method | Runtime mapping certified from code |
|---|---|
| `COD` | Offline COD method; no provider configuration required; checkout does not create reusable online provider artifacts for COD. |
| `STRIPE` | Maps to `PaymentProvider.Stripe` for payment-intent/session runtime. |
| `PAYMOB` | Maps to `PaymentProvider.Paymob` for payment session/runtime. |
| `FAWRY` | Maps to `PaymentProvider.Fawry` for payment intent/session runtime. |
| `MANUALWALLET` | Maps to `PaymentProvider.ManualWallet`; wallet proof/review workflow is backend-owned. |

## Checkout Integration

Implemented flow:

```text
POS Admin Store Payment Settings
  -> SQL TenantPaymentMethods
  -> payment-method events
  -> Mongo PaymentMethodDocument projection or SQL fallback
  -> GET /api/storefront/payment-methods
  -> Storefront DTO admission
  -> selected payment-method code candidate
  -> pricing preview / final checkout request
  -> CheckoutService SQL revalidation
  -> backend pricing fee calculation and order snapshot
  -> Payment runtime handoff when applicable
  -> Provider/runtime boundary
```

Checkout consumes configuration in two stages. First, Storefront displays only backend-returned payment method DTOs after client DTO admission. Second, checkout treats the selected code as a candidate and calls `ValidatePaymentMethodAsync`, which reloads current SQL `TenantPaymentMethods`, rejects missing/disabled/invalid configuration, validates amount limits after authoritative pricing, and snapshots `PaymentMethodCode`, `PaymentMethodName`, and `PaymentFee` on the order.

## Runtime Boundary

Store Payment Settings do not:

- Execute payments.
- Create payment intents.
- Call Stripe, Paymob, Fawry, or wallet/provider APIs.
- Perform checkout.
- Execute refunds.
- Execute webhooks.
- Recognize collected money.

Store Payment Settings configure and expose eligible payment methods only. Payment execution begins later in checkout/payment services after backend admission.

## Security

Implemented controls:

| Control | Implementation-backed certification |
|---|---|
| Tenant isolation | Admin and read paths bind tenant id explicitly; EF enforces unique tenant/code. |
| Encrypted secret storage | Provider config is protected with ASP.NET Data Protection purpose `PaymentMethodSecrets`. |
| Plaintext secret suppression | Admin response DTO and Storefront DTO do not include `ConfigEncrypted` or decrypted secrets. |
| Empty payload protection | Empty configure payloads do not wipe stored secrets. |
| Explicit reset | Reset requires `confirmReset=true`, rejects mixed reset/new config, clears config, and disables method. |
| Configuration validity | Required provider fields are validated before enablement and before Storefront exposure/checkout admission. |
| No-store reads | Admin and Storefront payment-method reads set no-store headers; Storefront client sends no-store headers. |
| Projection hardening | Projection reloads SQL authority, validates event version, fences duplicate/stale events, and tombstones deleted rows. |
| Fail-closed Storefront DTO admission | Client rejects malformed method lists, duplicate ids/codes, invalid fee/type/order/limits, and missing identity/display/availability evidence. |

## Permissions

Implemented permission model:

| Action | Implemented authority |
|---|---|
| View admin payment methods | `PaymentMethodsAdminController` class role gate: `Owner,Admin,Manager`; frontend route also requires `commercehub.payments.view`. |
| Edit display/fee/limit/sort/sandbox/common properties | `PUT /api/admin/payment-methods/{code}` under `Owner,Admin,Manager`. |
| Configure provider credentials | `PUT /api/admin/payment-methods/{code}/configure` under `Owner,Admin,Manager`. |
| Rotate credentials | Same configure endpoint; non-empty incoming fields merge into encrypted config. No separate rotate permission is implemented. |
| Reset credentials | Same configure endpoint with `reset` and `confirmReset`; no separate reset permission is implemented. |
| Enable method | `POST /api/admin/payment-methods/{code}/enable` under `Owner,Admin,Manager`, with config validity gate for configured methods. |
| Disable method | `POST /api/admin/payment-methods/{code}/disable` under `Owner,Admin,Manager`. |
| Projection sync | `POST /api/admin/payment-methods/sync` under `Owner,Admin`. |
| Storefront read | Anonymous route, tenant-context required, read-only, enabled/config-valid/supported methods only. |

## Tenant Isolation

PASS. Store Payment Settings are tenant-scoped.

- SQL write/read paths use `TenantId == tenantId`.
- The admin controller derives tenant from trusted tenant context.
- EF enforces one method code per tenant through `(TenantId, Code)` uniqueness.
- Mongo documents include `TenantId` and repository filters by tenant.
- SQL fallback also filters by tenant and `IsDeleted`.
- Checkout SQL revalidation filters by tenant and code.
- Storefront can only read current tenant configuration through resolved tenant context and cannot modify settings.

No implementation-backed path was identified where Store A can modify Store B payment settings or where Storefront checkout can consume another tenant's method configuration.

## Validation Rules

Implemented validation:

- Unsupported method codes are rejected in admin mutation paths.
- Storefront read suppresses unsupported codes.
- Enabled methods requiring configuration cannot be enabled unless configuration validates.
- Checkout rejects missing payment method authority.
- Checkout rejects disabled methods.
- Checkout rejects invalid provider configuration.
- Checkout rejects min/max amount limit violations against authoritative final totals.
- Storefront DTO admission rejects malformed lists/items, invalid UUIDs, invalid codes, missing display names, missing booleans, non-online/non-offline methods, negative fees, invalid fee types, negative amount limits, inverted min/max limits, duplicate ids, and duplicate codes.
- Reset requires explicit confirmation and rejects reset mixed with new provider values.
- Empty configuration payloads do not overwrite existing credentials.
- Decryption failure blocks safe overwrite and requires explicit reset.

Provider-specific required fields:

| Code | Required configuration |
|---|---|
| `COD` | None |
| `STRIPE` | `publishableKey`, `secretKey`, `webhookSecret` |
| `PAYMOB` | `apiKey`, `integrationId`, `iframeId`, `hmacSecret` |
| `FAWRY` | `merchantCode`, `apiKey`, `webhookSecret` |
| `MANUALWALLET` | `walletNumber` |

## UI Certification

PASS for implemented POS Admin Payment Methods UI.

`PaymentMethods.jsx` implements:

- Payment Methods page at `/ecommerce/payment-methods`.
- Backend load from `/admin/payment-methods`.
- Loading spinner, empty state, success banner, and validation/error detail banner.
- Table columns for name, type, fee, configuration status, enabled status, and actions.
- Enable/disable action.
- Edit modal for display name, Arabic display name, description, Arabic description, sort order, fee, fee type, minimum order amount, and maximum order amount.
- Configure modal for sandbox mode and provider fields.
- Password inputs for sensitive Stripe/Paymob/Fawry secrets.
- Secret non-return warning: stored provider secrets are never returned to the browser.
- Explicit reset section with confirmation checkbox and client-side reset conflict checks.
- Configuration status badge derived from backend validity.

The UI includes fallback default method definitions when the admin API fails, but those fallback rows are disabled presentation-only data and do not establish backend authority.

## Business Rules

- Payment settings belong to a tenant/store.
- Method code is unique per tenant.
- Only implemented method codes may be configured.
- COD requires no provider configuration.
- Stripe, Paymob, Fawry, and Manual Wallet require valid configuration before enablement and Storefront exposure.
- Storefront payment-method reads are read-only.
- Disabled methods do not appear in Storefront reads.
- Invalidly configured enabled methods are suppressed from Storefront reads.
- Checkout cannot rely on Storefront selection alone; it revalidates against current SQL.
- Payment fee is calculated by backend pricing, not the browser.
- Order stores a snapshot of admitted payment method code, display name, and fee.
- Configuration reset disables the method.
- Payment settings do not execute payment processing.

## Critical Invariants

- Store owns its payment settings through tenant-scoped SQL rows.
- Storefront never becomes payment-method configuration authority.
- Checkout never bypasses current SQL payment-method validation.
- Secrets never leave protected storage through payment-method DTOs.
- Mongo is a derivative read model, not source of truth.
- Disabled or invalid methods must not be offered as valid checkout methods.
- Runtime payment execution never owns Store Payment Settings.
- Store Payment Settings never execute payments, refunds, webhooks, or provider calls.
- Display labels and icons do not change provider/runtime ownership.

## Administrative Runtime

Implemented administration runtime:

```text
POS Admin Payment Methods UI
  -> /api/admin/payment-methods
  -> trusted tenant context and role authorization
  -> SQL TenantPaymentMethods
  -> encrypted ConfigEncrypted for provider fields
  -> payment-method event publication
  -> projection consumer reloads SQL authority
  -> Mongo PaymentMethodDocument for Storefront reads
  -> Storefront / checkout SQL revalidation
  -> Payment runtime only after checkout admission
```

Configuration SHALL NOT execute payment processing. Configuration SHALL NOT execute checkout. Configuration SHALL NOT invoke provider APIs.

## Architecture Boundaries

Store Payment Settings own:

- Payment method configuration.
- Enabled/disabled visibility.
- Provider selection by method code.
- Display names, descriptions, icons.
- Sort order.
- Fee configuration.
- Amount limits.
- Sandbox flag.
- Encrypted credential storage and validity status.
- Storefront-safe method projection.

Store Payment Settings do not own:

- Checkout orchestration.
- Payment runtime execution.
- Provider adapters.
- Payment success/failure.
- Payment intents as runtime sessions.
- Refunds.
- Webhooks.
- Financial posting.
- Orders.
- Inventory.
- Shipping/fulfillment.
- General ledger.
- CommerceHub Marketplace.

## Architecture Risks

No architectural violations were identified in the Store Payment Settings boundary.

Implementation-backed observations outside the settings boundary:

- `PaymentMethodsAdminController` allows `Manager` for edit/configure/enable/disable/reset. This is the implemented role model, not a certification failure.
- No separate rotate/reset/environment permissions are implemented beyond the role-gated configure/upsert endpoints.
- Currency and regional/customer/product restrictions are not Store Payment Settings properties in the implemented model.
- Provider operational completeness differs by provider runtime and is outside this A1 settings certification.

## Certification Checklist

| Item | Result |
|---|---|
| Ownership | PASS |
| Tenant Isolation | PASS |
| Configuration Model | PASS |
| Provider Mapping | PASS |
| Checkout Integration | PASS |
| Runtime Boundary | PASS |
| Security | PASS |
| Permissions | PASS |
| Validation | PASS |
| UI | PASS |
| Business Rules | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS |

## Final Certification

PASS.

Store Payment Settings are certified as a tenant-owned POS Admin configuration module for POS Storefront payment-method availability and provider configuration. The implementation preserves the boundary between configuration, Storefront read presentation, checkout admission, and payment runtime execution.

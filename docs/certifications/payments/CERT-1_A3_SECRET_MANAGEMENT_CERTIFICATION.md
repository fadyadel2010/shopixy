# CERT-1 PHASE A - A3 Secret Management Certification

Certification date: 2026-07-20  
Certification scope: POS Admin Secret Management for tenant payment methods  
Certification authority: Source code inspection only  
Certification result: PASS

## Executive Summary

POS Admin Secret Management owns the tenant payment-method credential lifecycle for `TenantPaymentMethods`. The implemented authority is `PaymentMethodsAdminController`, which receives provider configuration from POS Admin, validates it, encrypts it with ASP.NET Core Data Protection purpose `PaymentMethodSecrets`, persists it in PostgreSQL as `TenantPaymentMethod.ConfigEncrypted`, evaluates it for provider readiness, clears it through explicit reset, and emits projection-safe payment method events.

It protects payment provider credentials and configured payment identifiers for STRIPE, PAYMOB, FAWRY, and MANUALWALLET. It also protects the full provider configuration document because implementation stores provider configuration as one encrypted JSON payload, even when some entries are public identifiers.

It does not own checkout execution, order lifecycle, inventory, settlement, commission, CommerceHub, Marketplace, provider adapter internals, or payment execution decisions. Runtime services consume decrypted credentials only after resolving the current tenant payment-method row. Checkout and storefront responses receive only explicitly exposed public or runtime-generated artifacts, not backend provider credentials.

Evidence:
- POS Admin controller uses `DataProtectionPurpose = "PaymentMethodSecrets"` and creates the protector at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:41` and `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:53`.
- Tenant scoping is sourced from `ITenantContext.RequiredTenantId` at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:59`.
- Encrypted source of truth is `TenantPaymentMethod.ConfigEncrypted` at `PointOfSale.Core/Entities/TenantPaymentMethod.cs:104`.
- EF maps `ConfigEncrypted` to PostgreSQL `text` at `PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs:59`.
- Data Protection keys are persisted to the file system and application-scoped in startup at `PointOfSale.API/Program.cs:1121`.

## Secret Inventory

| Provider | Implemented value | Classification | Purpose | Owner | Storage | Consumer | Exposure | Lifecycle |
|---|---|---:|---|---|---|---|---|---|
| STRIPE | `publishableKey` | Public client key | Stripe.js initialization | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | Stripe runtime, checkout response | Returned as runtime/customer handoff via `StripePublishableKey`; not returned by admin DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| STRIPE | `secretKey` | Backend secret | Stripe API authentication | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | `StripeTenantConfigurationResolver`, Stripe client | Never returned to browser/admin DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| STRIPE | `webhookSecret` | Backend secret | Stripe webhook verification authority | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | Stripe runtime/webhook services through resolver/status paths | Never returned to browser/admin DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| PAYMOB | `apiKey` | Backend secret | Paymob API authentication | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | `PaymobTenantConfigurationResolver` | Never returned to browser/admin DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| PAYMOB | `integrationId` | Provider identifier | Paymob payment key/session configuration | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | `PaymobTenantConfigurationResolver` | Not returned by admin/storefront DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| PAYMOB | `iframeId` | Provider identifier | Build Paymob iframe URL | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | `PaymobTenantConfigurationResolver` | Not returned directly; runtime-generated iframe URL may be returned | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| PAYMOB | `hmacSecret` | Backend secret | Paymob HMAC validation | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | `PaymobTenantConfigurationResolver` and HMAC verification paths | Never returned to browser/admin DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| FAWRY | `merchantCode` | Provider identifier | Fawry merchant identity | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | `FawryTenantConfigurationResolver` | Not returned by admin/storefront DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| FAWRY | `apiKey` | Backend secret | Fawry API authentication | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | `FawryTenantConfigurationResolver` | Never returned to browser/admin DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| FAWRY | `webhookSecret` | Backend secret | Fawry secure key/webhook signature authority | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted` | `FawryTenantConfigurationResolver` as `SecureKey` | Never returned to browser/admin DTO | Submitted, validated, encrypted, loaded, decrypted, consumed, reset |
| MANUALWALLET | `walletNumber` | Customer-visible payment destination | Customer manual transfer target | POS Admin Secret Management | Encrypted JSON in `ConfigEncrypted`; projected as public config | Checkout/manual wallet storefront config | Intentionally returned as `WalletNumber` and `MerchantWalletNumber` | Submitted, validated, encrypted, projected, returned, reset |
| MANUALWALLET | `walletName` | Optional non-secret config | Admin-entered label from UI | POS Admin Secret Management | Encrypted JSON if submitted | No certified runtime consumer found | Not projected by `BuildPublicConfiguration` | Submitted if non-empty, encrypted, reset |

Evidence:
- Required STRIPE fields are implemented at `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:24`.
- Required PAYMOB fields are implemented at `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:30`.
- Required FAWRY fields are implemented at `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:37`.
- Required MANUALWALLET `walletNumber` is implemented at `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:43`.
- Manual wallet public projection is limited to `walletNumber` at `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:138` and `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:151`.
- POS Admin UI defines these provider input fields at `PointOfSale.Web/src/pages/PaymentMethods.jsx:364` through `PointOfSale.Web/src/pages/PaymentMethods.jsx:383`, including sensitive flags for backend secrets.

## Secret Lifecycle

Creation and submission:
- Admin create/update endpoints accept `UpsertPaymentMethodDto.Configuration` for provider-specific values at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:1042`.
- `UpsertPaymentMethod` and `UpdateConfiguration` both call `ApplyConfigurationMutation` at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:162` and `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:267`.

Validation:
- Incoming values are trimmed and empty values are dropped by `NormalizeConfiguration` at `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:60`.
- Existing and incoming configuration are merged by `MergeConfiguration` at `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:101`.
- Provider-specific required fields are validated in `PaymentMethodConfigurationValidator.Validate`.

Encryption and persistence:
- Valid merged configuration is serialized and protected before assignment to `method.ConfigEncrypted` at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:873`.
- The encrypted payload is stored as `TenantPaymentMethod.ConfigEncrypted`, with tenant-specific uniqueness on `(TenantId, Code)` at `PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs:21`.

Loading and decryption:
- POS Admin validity evaluation decrypts stored configuration through `TryReadStoredConfiguration` at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:885` and `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:900`.
- Projection and SQL fallback decrypt only to compute validity and public projection at `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs:311` and `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs:273`.

Runtime consumption:
- Stripe resolves current tenant credentials from enabled, non-deleted `TenantPaymentMethods` with non-empty `ConfigEncrypted` at `PointOfSale.API/Services/Payments/Stripe/StripeTenantConfigurationResolver.cs:68` and decrypts at `PointOfSale.API/Services/Payments/Stripe/StripeTenantConfigurationResolver.cs:136`.
- Paymob resolves current tenant credentials from enabled, non-deleted `TenantPaymentMethods` with non-empty `ConfigEncrypted` at `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs:69` and decrypts at `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs:162`.
- Fawry resolves current tenant credentials from `TenantPaymentMethods`, rejects deleted/disabled rows, and decrypts at `PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs:76` and `PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs:241`.

Update:
- Partial update is implemented by decrypting existing configuration, normalizing incoming values, merging, validating, and re-encrypting at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:839` through `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:873`.
- Empty configuration on the general upsert path is ignored to preserve existing secrets at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:831`.
- Empty configuration on the configuration-only endpoint is rejected at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:813`.

Reset and deletion:
- Reset requires `Reset = true` and `ConfirmReset = true`. Missing confirmation is rejected at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:762`.
- Reset cannot be combined with incoming provider values at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:767`.
- Confirmed reset sets `ConfigEncrypted = null` and disables the payment method at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:783` through `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:786`.
- No POS Admin hard-delete endpoint for provider secrets was identified. Implemented recovery is explicit reset and reconfiguration.

Recovery:
- If stored configuration cannot be decrypted, replacement is blocked and the admin receives `CONFIGURATION_RESET_REQUIRED` at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:839`.
- Projection and read fallback fail closed by marking configuration invalid when decryption fails at `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs:326` and `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs:288`.

## Secret Storage

The source of truth is PostgreSQL `TenantPaymentMethods.ConfigEncrypted`. It stores an encrypted JSON dictionary of normalized provider configuration. The entity field is nullable, and reset clears it. EF maps it to a `text` column.

MongoDB is not the source of truth and does not store `ConfigEncrypted`. It stores display metadata, validity flags, and `PublicConfiguration`. The only public configuration currently built by the validator is manual wallet `walletNumber`.

Environment variables/configuration are used for runtime feature flags and global provider operational options, but tenant payment provider secrets certified here are resolved from `TenantPaymentMethods.ConfigEncrypted`, not global fallback credentials.

Evidence:
- Source field: `PointOfSale.Core/Entities/TenantPaymentMethod.cs:104`.
- EF text mapping: `PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs:59`.
- Mongo document has `IsConfigurationValid`, `HasConfiguration`, and `PublicConfiguration`, not `ConfigEncrypted`, at `PointOfSale.Infrastructure/MongoDB/Documents/PaymentMethodDocument.cs:130`, `PointOfSale.Infrastructure/MongoDB/Documents/PaymentMethodDocument.cs:136`, and `PointOfSale.Infrastructure/MongoDB/Documents/PaymentMethodDocument.cs:142`.
- Projection maps only public configuration and validity at `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs:262`.

## Encryption Certification

Encryption technology: ASP.NET Core Data Protection.

Purpose strings:
- Tenant payment-method provider configuration: `PaymentMethodSecrets`.
- Stripe payment-intent client secret storage: `PaymentIntent.ClientSecret`.
- Paymob protected payment session: `PointOfSale.Paymob.PaymentSession.v1`.
- Paymob protected webhook payload: `PointOfSale.Paymob.WebhookPayload.v1`.

Key management:
- Startup persists Data Protection keys to a configured path or a local application data fallback and sets an application name. This is implemented at `PointOfSale.API/Program.cs:1106` through `PointOfSale.API/Program.cs:1125`.

Protection scope:
- Provider configuration is protected before database persistence.
- Runtime decrypts only inside backend services that need credentials.
- Projection decrypts only server-side to compute validity and public configuration.

Plaintext storage:
- No plaintext provider credentials are assigned to persistent POS Admin payment-method storage. The persistence write assigns only `_dataProtector.Protect(...)` to `ConfigEncrypted`.
- Plaintext exists transiently in request DTOs, normalized dictionaries, decrypted runtime credential records, and provider call arguments during execution.

Evidence:
- Admin encryption at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:873`.
- Admin decryption at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:900`.
- Runtime provider decryption at `PointOfSale.API/Services/Payments/Stripe/StripeTenantConfigurationResolver.cs:136`, `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs:162`, and `PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs:241`.
- Stripe client secret protection at `PointOfSale.API/Services/Payments/PaymentIntentClientSecretProtector.cs:35` and unprotection at `PointOfSale.API/Services/Payments/PaymentIntentClientSecretProtector.cs:45`.
- Paymob protected payment session at `PointOfSale.API/Services/Payments/Paymob/PaymobSensitiveArtifactProtector.cs:59`.

## Browser Exposure Certification

Admin browser:
- Admin read DTOs return metadata, `RequiresConfiguration`, `IsConfigurationValid`, and `ConfigurationErrors`; they do not include `Configuration`, `ConfigEncrypted`, or decrypted secret values.
- Admin configuration modal starts with an empty `configuration` object; existing secret values are not prefilled.
- Sensitive provider fields render as password inputs.

Storefront browser:
- `StorefrontPaymentMethodsController` filters to enabled, supported, valid payment methods and maps to `StorefrontPaymentMethodDto`, which contains display metadata, fees, limits, and availability only.
- Manual wallet config intentionally returns `WalletNumber` because customers need it to transfer payment.
- Checkout DTO intentionally exposes runtime/client handoff artifacts: Stripe client secret, Stripe publishable key, Paymob iframe URL/payment key, Fawry reference code, and manual wallet number. These are not backend provider credentials stored in `ConfigEncrypted`.

Evidence:
- Admin DTO omits configuration fields in `PaymentMethodResponseDto` at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:1017`.
- Admin UI empty config state at `PointOfSale.Web/src/pages/PaymentMethods.jsx:101` and modal reset at `PointOfSale.Web/src/pages/PaymentMethods.jsx:315`.
- Password input selection at `PointOfSale.Web/src/pages/PaymentMethods.jsx:725`.
- Storefront filters invalid configuration at `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs:81` and returns only valid methods at `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs:100`.
- Storefront DTO begins at `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs:168`.
- Manual wallet returns `WalletNumber` at `PointOfSale.API/Controllers/StorefrontManualWalletController.cs:92` and DTO field at `PointOfSale.API/Controllers/StorefrontManualWalletController.cs:439`.
- Checkout handoff fields are implemented at `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs:601` through `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs:640`.

## DTO Certification

PASS. POS Admin response DTOs do not expose encrypted payloads or decrypted provider secrets. `UpsertPaymentMethodDto` is the only POS Admin DTO that receives provider configuration. `PaymentMethodResponseDto` returns validity metadata, errors, timestamps, flags, and payment method display fields.

The broader checkout DTO intentionally includes runtime-generated payment artifacts and public payment destinations. This is outside POS Admin secret storage ownership, but it is relevant to browser exposure and does not return backend provider credentials such as Stripe `secretKey`, Stripe `webhookSecret`, Paymob `apiKey`, Paymob `hmacSecret`, or Fawry `apiKey`/`webhookSecret`.

Evidence:
- `PaymentMethodResponseDto`: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:1017`.
- `UpsertPaymentMethodDto.Configuration`: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:1057`.
- Checkout runtime handoff fields: `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs:601`.

## Logging Certification

PASS. In the audited POS Admin secret-management path, logs include code, payment method id, tenant id, actor, operation, and trace id. They do not log normalized configuration dictionaries, encrypted payloads, or decrypted secret values.

Decryption failures are logged with exception metadata and payment method identifiers. Error responses return generic validation messages or reset instructions, not secret values. Validation messages name missing fields but do not echo submitted secret values.

Evidence:
- Reset log includes identifiers only at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:790`.
- Configuration update log includes code/id/operation/trace only at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:876`.
- Decryption failure log includes code/id only at `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:909`.
- Validation errors are fixed messages in `PaymentMethodConfigurationValidator.Require` at `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:126`.

## Runtime Consumption Certification

Certified runtime path:

Configuration -> Resolver -> Decrypt -> Provider Runtime -> Provider API

Stripe:
- Resolver loads enabled, non-deleted tenant STRIPE row with non-empty `ConfigEncrypted`.
- Resolver decrypts and validates provider/environment shape.
- Runtime service sets scoped credentials and consumes provider artifacts.
- Tenant-scoped Stripe client uses `credentials.SecretKey` for provider API calls.

Paymob:
- Resolver loads enabled, non-deleted tenant PAYMOB row with non-empty `ConfigEncrypted`.
- Resolver decrypts and validates API key, HMAC secret, integration id, iframe id, sandbox/environment.
- Runtime begins a single-use credential handoff.
- Handoff clears active credentials before provider I/O and validates tenant identity.

Fawry:
- Resolver loads tenant FAWRY row, rejects deleted/disabled/unsupported rows, validates environment, decrypts config, and checks credential shape.
- Runtime creates one-operation `FawryPaymentCredentials` from resolved tenant credentials.

Runtime caching:
- No durable credential cache was identified in the certified paths.
- Stripe uses a scoped accessor with `SetCredentials`; Paymob uses a scoped single-use handoff; Fawry passes credentials into one runtime request.
- Browser never receives Stripe `secretKey`, Stripe `webhookSecret`, Paymob `apiKey`, Paymob `hmacSecret`, or Fawry `apiKey`/`SecureKey`.

Evidence:
- Stripe resolver and runtime: `PointOfSale.API/Services/Payments/Stripe/StripeTenantConfigurationResolver.cs:57`, `PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs:51`, `PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs:362`.
- Stripe artifact consumption: `PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs:68` and `PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs:326`.
- Paymob resolver and handoff: `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs:58`, `PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs:46`, `PointOfSale.API/Services/Payments/Paymob/PaymobAdapterCredentialHandoff.cs:40`.
- Paymob handoff single-use clear before provider I/O: `PointOfSale.API/Services/Payments/Paymob/PaymobAdapterCredentialHandoff.cs:40`.
- Fawry resolver/runtime: `PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs:58`, `PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs:41`.

## Secret Reset Certification

PASS. Reset is explicit and destructive only after confirmation.

Implemented reset behavior:
- Reset unsupported for non-configurable payment methods.
- Reset requires `confirmReset`.
- Reset cannot include new provider values.
- Confirmed reset clears `ConfigEncrypted`.
- Confirmed reset disables the payment method immediately.
- Reset publishes an update event and a disabled event when applicable.
- Empty configuration does not wipe secrets.
- Decryption failure blocks overwrite and requires explicit reset.

Evidence:
- Reset support check: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:744`.
- Confirmation required: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:762`.
- Conflict detection: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:767`.
- Clear and disable: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:783` through `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:786`.
- Empty payload handling: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:813` and `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:831`.
- Decryption failure reset requirement: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:839`.
- Tests for empty payload, reset confirmation, and enable validation: `PointOfSale.Tests/Admin/PaymentMethodsAdminSafetyTests.cs:22`, `PointOfSale.Tests/Admin/PaymentMethodsAdminSafetyTests.cs:85`, `PointOfSale.Tests/Admin/PaymentMethodsAdminSafetyTests.cs:133`, and `PointOfSale.Tests/Admin/PaymentMethodsAdminSafetyTests.cs:230`.

## Validation Rules

Implemented validation:
- Supported POS Admin payment method codes: COD, STRIPE, PAYMOB, FAWRY, MANUALWALLET.
- STRIPE requires `publishableKey`, `secretKey`, `webhookSecret`.
- PAYMOB requires `apiKey`, `integrationId`, `iframeId`, `hmacSecret`.
- FAWRY requires `merchantCode`, `apiKey`, `webhookSecret`.
- MANUALWALLET requires `walletNumber`.
- COD requires no provider configuration.
- Empty keys or values are ignored during normalization.
- STRIPE alias `apiKey` is normalized to `secretKey` when `secretKey` is absent.
- FAWRY alias `securityKey` is normalized to `apiKey` when `apiKey` is absent.
- Enable is blocked when a configurable provider has invalid configuration.
- Decryption failure makes configuration invalid and blocks replacement until explicit reset.

Evidence:
- Code validation: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:532`.
- Required fields: `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:24`, `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:30`, `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:37`, `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:43`.
- Normalization and aliases: `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:60` and `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs:85`.
- Enable-time validation: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:358`.

## Frontend Certification

PASS. POS Admin frontend secret handling uses controlled React component state, password inputs for sensitive provider fields, empty initial configuration state, client-side empty-payload/reset conflict checks, and explicit reset confirmation. No `localStorage` or `sessionStorage` use was identified in the POS Admin `PaymentMethods.jsx` implementation.

The UI does not receive existing secret values from the admin API. It states that blank fields preserve stored values, then submits only non-empty fields through `sanitizeConfiguration`.

Evidence:
- Empty config state: `PointOfSale.Web/src/pages/PaymentMethods.jsx:101`.
- Sanitization: `PointOfSale.Web/src/pages/PaymentMethods.jsx:114`.
- Reset conflict and confirmation checks: `PointOfSale.Web/src/pages/PaymentMethods.jsx:254` through `PointOfSale.Web/src/pages/PaymentMethods.jsx:278`.
- Password inputs for sensitive fields: `PointOfSale.Web/src/pages/PaymentMethods.jsx:725`.
- Reset confirmation checkbox: `PointOfSale.Web/src/pages/PaymentMethods.jsx:761`.

## Security Controls

Implemented controls:
- ASP.NET Core Data Protection encryption for provider config.
- File-system persisted Data Protection key ring with application name.
- Tenant isolation through `TenantId` filtering and tenant-owned entity.
- Unique `(TenantId, Code)` constraint.
- No-store cache headers on POS Admin payment method reads.
- DTO sanitization: admin response does not return configuration or encrypted payload.
- Projection sanitization: Mongo stores validity and limited `PublicConfiguration`, not `ConfigEncrypted`.
- Storefront suppression of invalid payment methods.
- Reset confirmation.
- Reset disables payment method.
- Empty payload preservation.
- Decryption failure fail-closed behavior.
- Runtime tenant-scoped resolver per provider.
- Paymob single-use credential handoff.
- Provider environment/sandbox validation in Stripe, Paymob, and Fawry resolvers.
- Tests covering preservation/reset/enable safety.

Evidence:
- No-store headers: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs:966`.
- Tenant uniqueness: `PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs:21`.
- Projection sanitization: `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs:262`.
- Storefront invalid suppression: `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs:81`.
- Runtime resolver validations: `PointOfSale.API/Services/Payments/Stripe/StripeTenantConfigurationResolver.cs:159`, `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs:188`, `PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs:267`.

## Critical Invariants

Implementation-backed invariants:
- Tenant payment provider configuration is stored only as `ConfigEncrypted`.
- Provider credentials are protected with purpose `PaymentMethodSecrets`.
- Admin read DTOs do not expose stored configuration.
- Mongo payment method projection does not store `ConfigEncrypted`.
- Storefront payment method list exposes only valid enabled methods.
- Manual wallet number is intentionally public and is the only validator-built public configuration value.
- Empty config submissions do not clear existing secrets.
- Reset clears provider configuration and disables the payment method.
- Runtime decrypts from current tenant payment-method configuration when credentials are needed.
- Browser does not receive backend provider credentials.
- Checkout receives runtime/client artifacts only.
- Paymob runtime credential handoff is single-use.

## Architecture Boundaries

Secret Management owns:
- Provider configuration receipt from POS Admin.
- Provider-specific validation.
- Encryption and protected storage.
- Configuration validity evaluation.
- Reset and clear behavior.
- Projection-safe public configuration generation.
- Tenant-scoped credential loading for provider runtime resolvers.

Secret Management does not own:
- Checkout workflow or order creation.
- Payment execution outcome.
- Provider adapters beyond credential consumption boundaries.
- Orders, inventory, settlement, commission, financial posting.
- CommerceHub and Marketplace payment authority.
- Affiliate payout wallet handling.
- Storefront OAuth provider secret management.

Boundary evidence:
- Payment methods are managed through `PaymentMethodsAdminController`.
- Checkout DTOs and runtime services consume resulting artifacts but do not own POS Admin provider configuration.
- Runtime application services receive credentials from tenant configuration resolvers, rather than reading arbitrary configuration directly.

## Security Risks

No security architecture violations were identified.

## Certification Checklist

| Area | Result |
|---|---|
| Secret Inventory | PASS |
| Secret Lifecycle | PASS |
| Storage | PASS |
| Encryption | PASS |
| Browser Exposure | PASS |
| DTO Safety | PASS |
| Logging Safety | PASS |
| Runtime Consumption | PASS |
| Reset Flow | PASS |
| Validation | PASS |
| Frontend | PASS |
| Security Controls | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS |

## Final Certification

PASS

Justification: The implementation stores POS Admin payment provider configuration only as Data Protection protected JSON in tenant-scoped PostgreSQL `TenantPaymentMethods.ConfigEncrypted`; admin DTOs do not return stored secrets; MongoDB projections omit encrypted and plaintext provider credentials; reset is explicit and disables affected methods; runtime providers resolve and decrypt tenant credentials server-side only when needed; and browser exposure is limited to intentionally public manual wallet details and runtime/client payment handoff artifacts.

## Evidence Register

- `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`: POS Admin controller, tenant scoping, no-store headers, configuration mutation, encryption, decryption, reset, DTOs.
- `PointOfSale.Core/Entities/TenantPaymentMethod.cs`: tenant-owned payment method entity and `ConfigEncrypted`.
- `PointOfSale.Infrastructure/Data/Configurations/TenantPaymentMethodConfiguration.cs`: database table, tenant/code uniqueness, encrypted config column mapping.
- `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`: provider-specific validation, normalization, merge rules, public configuration rules.
- `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs`: projection from SQL authority to Mongo without secret storage.
- `PointOfSale.Infrastructure/MongoDB/Documents/PaymentMethodDocument.cs`: Mongo payment method document fields.
- `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs`: storefront read fallback, tenant filtering, validity recomputation.
- `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs`: storefront filtering and DTO mapping.
- `PointOfSale.API/Controllers/StorefrontManualWalletController.cs`: intentional wallet number exposure.
- `PointOfSale.API/Services/Payments/Stripe/StripeTenantConfigurationResolver.cs`: Stripe credential resolution/decryption/validation.
- `PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs`: Stripe runtime credential use and client artifact return.
- `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs`: Paymob credential resolution/decryption/validation.
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdapterCredentialHandoff.cs`: single-use Paymob credential handoff.
- `PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs`: Fawry credential resolution/decryption/validation.
- `PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs`: Fawry one-operation runtime credential use.
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`: checkout runtime/browser handoff fields.
- `PointOfSale.API/Services/Payments/PaymentIntentClientSecretProtector.cs`: Stripe client secret storage protector.
- `PointOfSale.API/Services/Payments/Paymob/PaymobSensitiveArtifactProtector.cs`: Paymob payment session and webhook payload protection.
- `PointOfSale.Web/src/pages/PaymentMethods.jsx`: POS Admin React secret input, save, reset, and validation flow.
- `PointOfSale.Tests/Admin/PaymentMethodsAdminSafetyTests.cs`: tests for empty payload preservation, reset confirmation, reset clearing, and enable-time configuration validation.

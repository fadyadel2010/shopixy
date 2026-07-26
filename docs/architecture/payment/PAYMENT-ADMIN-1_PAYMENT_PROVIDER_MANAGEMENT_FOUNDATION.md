# PAYMENT-ADMIN-1 Payment Provider Management Foundation

## Executive Summary

PAYMENT-ADMIN-1 introduces a provider-independent CommerceHub payment provider administration foundation. It creates the canonical administration route namespace, DTO envelope, domain contracts, Super Admin permission/navigation hook, reusable frontend shell, and audit-backed controller surface for provider configuration management.

This phase does not implement Paymob configuration fields, external credential validation, test connection, provider health checks, secret rotation, or payment execution.

## Administrative Architecture

The administrative hierarchy is:

CommerceHub -> Administration -> Payment Providers -> Provider Configuration.

Provider Configuration is generic. Provider-specific details are isolated behind the `Configuration` dictionary envelope and are not modeled as root properties.

## Navigation

The Super Admin shell now exposes a `Payment Providers` navigation item under the CommerceHub administration surface at:

`/super-admin/commercehub/payment-providers`

The item is gated by `super-admin.payment-providers.view`.

## Routing

Canonical administration API routes are:

`GET /api/admin/payment-providers`

`GET /api/admin/payment-providers/{providerCode}`

`PUT /api/admin/payment-providers/{providerCode}`

No provider-specific settings routes were introduced.

## DTO Design

Canonical administration DTOs were added:

`PaymentProviderConfigurationDto`

`PaymentProviderSummaryDto`

`PaymentProviderCapabilityDto`

`PaymentProviderConfigurationRequest`

`PaymentProviderConfigurationResponse`

`PaymentProviderListResponse`

DTOs contain provider code, display name, description, enabled state, environment, capabilities, status, metadata, and generic configuration. Secrets are not returned.

## Domain Model

The canonical domain contracts live under `PointOfSale.Core/Payments/Administration`:

`PaymentProviderConfiguration`

`PaymentProviderCapability`

`PaymentProviderRegistration`

`PaymentProviderPermissions`

The root model remains provider-independent. Provider-specific configuration is encapsulated in the generic configuration dictionary and can be interpreted by future provider implementations.

## API Design

`AdminPaymentProvidersController` handles enumeration, provider selection, and saving generic configuration envelopes. The controller derives from `SuperAdminBaseController`, so only Super Admin sessions can access it.

The API does not call payment providers, validate external credentials, test connectivity, or execute payment operations.

## Permission Model

Canonical payment provider permissions are defined as:

`PaymentProviders.View`

`PaymentProviders.Edit`

`PaymentProviders.Configure`

`PaymentProviders.TestConnection`

`PaymentProviders.RotateSecrets`

The current navigation gate is `super-admin.payment-providers.view`; the canonical permissions are returned by the administration API for future UI/action gating.

## Frontend Architecture

The Super Admin frontend adds `PaymentProvidersPage` and a provider administration state reducer covering loading, saving, selected provider, provider list, configuration, and validation errors.

The frontend reads providers from `/api/admin/payment-providers`. It does not hardcode Paymob UI assumptions.

## UI Components

Reusable components added:

`PaymentProviderLayout`

`PaymentProviderList`

`PaymentProviderCard`

`PaymentProviderDetails`

`ConfigurationSection`

`SaveBar`

`ProviderBadge`

These components render provider summaries, details, capability lists, placeholder configuration, and a reusable save surface.

## Backend Validation

Validation covers:

Unknown provider.

Missing provider.

Duplicate provider registration.

Provider code mismatch.

Secret-like keys in configuration or metadata envelopes.

Permission failures are handled by the inherited Super Admin authorization policy.

## Testing

Foundation tests cover:

Navigation and route wiring.

Authorization through Super Admin base controller and capability gating.

Provider enumeration.

Provider selection.

DTO serialization.

API routing.

Permission constants.

Provider-independent frontend rendering.

Validation for unknown, missing, duplicate, and secret-bearing provider data.

No provider communication tests were added.

## Files Added

`PointOfSale.Core/Payments/Administration/PaymentProviderPermissions.cs`

`PointOfSale.Core/Payments/Administration/PaymentProviderAdministrationModels.cs`

`PointOfSale.API/DTOs/Admin/PaymentProviderAdministrationDtos.cs`

`PointOfSale.API/Services/Admin/IPaymentProviderAdministrationService.cs`

`PointOfSale.API/Services/Admin/PaymentProviderAdministrationException.cs`

`PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs`

`PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs`

`PointOfSale.Web/src/super-admin/payment-providers/*`

`PointOfSale.Tests/Architecture/PaymentProviderAdministrationFoundationTests.cs`

`docs/architecture/payment/PAYMENT-ADMIN-1_PAYMENT_PROVIDER_MANAGEMENT_FOUNDATION.md`

## Files Modified

`PointOfSale.API/Program.cs`

`PointOfSale.API/SuperAdmin/Controllers/SuperAdminAuthController.cs`

`PointOfSale.Web/src/App.jsx`

`PointOfSale.Web/src/super-admin/auth/superAdminAuth.types.ts`

`PointOfSale.Web/src/super-admin/auth/superAdminSession.test.ts`

`PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts`

`PointOfSale.Web/src/super-admin/layout/SuperAdminLayout.tsx`

## Compatibility

Existing payment runtime, checkout, webhook, Paymob, Stripe, Fawry, COD, and Manual Wallet execution paths are unchanged. This foundation introduces an administration plane only.

The provider list is backend-driven. PAYMENT-ADMIN-1 registers Paymob as the first administration-visible provider without adding provider-specific configuration fields.

## Readiness for PAYMENT-ADMIN-2

PAYMENT-ADMIN-2 can add Paymob-specific configuration by plugging a provider-specific payload into the generic configuration envelope, adding provider-specific validation behind the administration service boundary, and extending the placeholder frontend configuration section with a provider-specific form while preserving the canonical routes, DTO root shape, permissions, and reusable layout.

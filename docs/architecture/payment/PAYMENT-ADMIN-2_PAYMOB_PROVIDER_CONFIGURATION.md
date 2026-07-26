# PAYMENT-ADMIN-2 Paymob Provider Configuration

## Executive Summary

PAYMENT-ADMIN-2 implements the first provider-specific configuration experience on top of the generic Payment Provider Management foundation. Paymob can now be configured from the Super Admin Payment Providers page through the canonical provider administration API.

This phase is configuration-only. It does not call Paymob, test credentials, report provider health, rotate stored credentials, create payment sessions, or alter checkout/payment runtime behavior.

## Configuration Architecture

The generic provider architecture remains intact:

`GET /api/admin/payment-providers/{providerCode}`

`PUT /api/admin/payment-providers/{providerCode}`

Paymob-specific settings are stored inside the generic `Configuration` envelope. Provider-specific fields were not promoted to root DTO properties. Secret replacements travel through a generic replacement envelope and are scrubbed from responses.

When the API runs with an `ApplicationDbContext`, the administration service persists the Paymob administration payload in `SystemSettings` under the `CommerceHubPaymentProviders` category. The local parameterless service path remains available for source-level certification tests.

## General Section

The Paymob form includes:

`Enabled`

`Display Name`

`Environment`

`Sandbox`

`Production`

`Currency`

`Description`

Frontend and backend validation reject missing display name, missing description, invalid currency, and unknown environments.

## Credentials

The form supports replacement-only credentials:

`API Key`

`HMAC Secret`

Stored credential values are never returned. The API returns only configured flags and the mask `**************`. The frontend renders password inputs with `autoComplete="new-password"` and does not hydrate stored values into component state.

## Integration

The integration section includes:

`Integration ID`

`iFrame ID`

`Merchant Identifier`

`Provider Account Name`

Integration ID and iFrame ID are required positive integers. Merchant Identifier and Provider Account Name are accepted as Paymob account metadata for future use.

## Runtime Settings

The runtime section captures future-compatible defaults:

`Timeout`

`Retry Count`

`Request Timeout`

`Maximum Retry Attempts`

Negative values are rejected. These values are configuration-plane data only in PAYMENT-ADMIN-2 and are not wired into runtime services.

## Callback Information

The form displays read-only callback routes:

`POST /api/commercehub/payments/paymob/webhook`

`GET /api/commercehub/payments/paymob/callback`

The administration page never asks the user to edit callback URLs manually.

## Validation

Frontend and backend validation cover:

Missing API Key.

Missing HMAC Secret.

Invalid Integration ID.

Invalid iFrame ID.

Negative Timeout.

Negative Retry Count.

Invalid Currency.

Unknown Environment.

The backend also rejects secret-like keys inside the generic configuration and metadata envelopes. Secrets are accepted only through the replacement envelope.

## Save Workflow

The save workflow is:

Validate.

Persist configuration.

Write audit record.

Return updated provider configuration.

The save path does not contact Paymob.

## Audit Model

The controller writes `PAYMENT_PROVIDER_CONFIGURATION_SAVED` through `ISuperAdminAuditService`.

The audit record includes:

Who modified the configuration.

When the change occurred.

Provider code.

Changed fields.

Environment.

The audit record never includes API Key values, HMAC Secret values, or submitted replacement payloads. Credential changes are represented through redacted changed-field markers.

## API Design

Only canonical routes are used:

`GET /api/admin/payment-providers/{providerCode}`

`PUT /api/admin/payment-providers/{providerCode}`

No `/paymob/save`, `/paymob/update`, or other provider-specific administration routes were introduced.

## Frontend Design

The page continues to use `PaymentProviderLayout`.

`ConfigurationSection` now dispatches Paymob to `PaymobConfigurationForm`.

The form includes:

General.

Credentials.

Integration.

Runtime.

Callback Information.

Advanced.

Validation Summary.

The save bar supports loading state, dirty state, saving state, success message, cancel changes, and unsaved change detection.

## Security

Secrets are never returned by the API.

Secrets are never logged by the controller audit payload.

Secrets are not stored in Redux or application-wide client state. Replacement values are kept in a local ref until save and cleared after save, provider switching, or cancel.

Saved values are represented with `**************` masks and configured flags only.

## Testing

Tests cover:

Load configuration.

Save configuration.

Validation failures.

Secret masking.

Secret replacement.

Read-only callback URLs.

Audit logging.

Permission enforcement through the existing Super Admin base controller and route gate.

Frontend rendering.

DTO serialization.

Environment switching.

No external Paymob tests were added.

## Files Added

`PointOfSale.Web/src/super-admin/payment-providers/components/PaymobConfigurationForm.tsx`

`PointOfSale.Tests/Architecture/PaymentAdmin2PaymobProviderConfigurationTests.cs`

`docs/architecture/payment/PAYMENT-ADMIN-2_PAYMOB_PROVIDER_CONFIGURATION.md`

## Files Modified

`PointOfSale.API/DTOs/Admin/PaymentProviderAdministrationDtos.cs`

`PointOfSale.API/Services/Admin/IPaymentProviderAdministrationService.cs`

`PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs`

`PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs`

`PointOfSale.API/Program.cs`

`PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx`

`PointOfSale.Web/src/super-admin/payment-providers/paymentProviderTypes.ts`

`PointOfSale.Web/src/super-admin/payment-providers/usePaymentProviderAdministrationState.ts`

`PointOfSale.Web/src/super-admin/payment-providers/components/ConfigurationSection.tsx`

`PointOfSale.Web/src/super-admin/payment-providers/components/SaveBar.tsx`

`PointOfSale.Tests/Architecture/PaymentProviderAdministrationFoundationTests.cs`

## Compatibility

Payment runtime is unchanged. Checkout initiation, provider adapters, Paymob payment service, webhook execution, refund execution, and tenant payment method runtime resolution are not modified by this phase.

PAYMENT-ADMIN-2 uses the administration plane only and keeps provider-specific data behind the generic configuration envelope.

## Readiness for PAYMENT-ADMIN-3

PAYMENT-ADMIN-3 can build on this by adding operational actions such as connection checks, provider health, or credential lifecycle workflows behind explicit generic capabilities and permissions.

Those future actions should continue to use canonical administration routes or generic provider action endpoints rather than provider-specific shortcuts.

# PAYMENT-ADMIN-3 Provider Connectivity And Health

## Executive Summary

PAYMENT-ADMIN-3 adds operational diagnostics for payment providers. Super Admins can run a canonical Test Connection action against Paymob without creating payments, checkout sessions, refunds, inventory reservations, events, or runtime payment mutations.

## Operational Architecture

The implementation separates configuration management, provider action orchestration, and provider-specific connectivity checks:

- `PaymentProviderAdministrationService` remains the owner of stored provider configuration and protected credentials.
- `PaymentProviderActionService` owns canonical provider actions.
- `IPaymentProviderConnectivityTester` provides provider-specific diagnostics behind a provider-independent contract.
- `PaymobConnectivityTester` is the first implementation.

## Provider Action Model

Provider actions live under the canonical namespace:

`POST /api/admin/payment-providers/{providerCode}/actions/test-connection`

Future actions such as Refresh Health, Validate Credentials, and Re-synchronize Provider Metadata can extend the same `/actions/{action}` model.

## Test Connection Flow

The Test Connection flow is:

1. Load stored provider configuration.
2. Validate provider existence, enabled state, required configuration, and credentials.
3. Authenticate with Paymob using the stored API key.
4. Verify the authentication response without creating an order, payment key, checkout session, refund, or provider transaction.
5. Return a canonical action result.
6. Persist only redacted operational result metadata.

## Health Model

Provider health uses canonical statuses:

- `Healthy`
- `Warning`
- `Error`
- `Unknown`

The health card is derived from the latest recorded diagnostic result and includes last successful check, last failed check, duration, provider version, and connection quality.

## API Design

The endpoint is provider-independent:

`POST /api/admin/payment-providers/{providerCode}/actions/test-connection`

No `/paymob/test` or `/paymob/health` routes are introduced.

## Generic Response Contracts

`PaymentProviderActionResultDto` returns:

- `success`
- `status`
- `provider`
- `environment`
- `message`
- `checkedAtUtc`
- `latencyMs`
- `warnings`
- `errors`
- `errorCategory`
- `providerVersion`
- `capabilities`

`PaymentProviderHealthDto` returns:

- `status`
- `environment`
- `lastSuccessfulCheckUtc`
- `lastFailedCheckUtc`
- `lastCheckDurationMs`
- `providerVersion`
- `connectionQuality`
- `operationalMessages`
- `latestResult`
- `previousResult`

## Frontend Design

The Payment Providers page now composes reusable operational components:

- `ProviderHealthCard`
- `ConnectionResultPanel`
- `TestConnectionButton`
- `HealthBadge`
- `OperationalMessages`

The visible flow is configuration, health, operational messages, and Test Connection. Existing configuration behavior from PAYMENT-ADMIN-2 is unchanged.

## Security

The diagnostic path never logs or returns API keys, HMAC secrets, authentication tokens, raw provider payloads, or provider responses that may contain secrets. Stored credentials are decrypted only inside the backend service boundary for the diagnostic call.

## Audit

Every Test Connection attempt is audited with:

- Actor id and email
- Provider
- Environment
- Result status
- Error category
- Duration
- Checked timestamp

The audit entry records `SecretsRedacted = true` and `ProviderPayloadPersisted = false`.

## Testing

Tests cover:

- Successful Test Connection
- Authentication failure
- Missing configuration
- Missing credentials
- Provider timeout
- Unknown provider
- Permission and Super Admin endpoint enforcement
- Health calculation
- Canonical response serialization
- Frontend rendering
- Audit logging

No tests execute payments, create sessions, perform checkout, or trigger refunds.

## Files Added

- `PointOfSale.API/Services/Admin/PaymentProviderActionService.cs`
- `PointOfSale.Web/src/super-admin/payment-providers/components/ProviderHealthCard.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/ConnectionResultPanel.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/TestConnectionButton.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/HealthBadge.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/OperationalMessages.tsx`
- `PointOfSale.Tests/Architecture/PaymentAdmin3ProviderConnectivityAndHealthTests.cs`
- `docs/architecture/payment/PAYMENT-ADMIN-3_PROVIDER_CONNECTIVITY_AND_HEALTH.md`

## Files Modified

- `PointOfSale.API/DTOs/Admin/PaymentProviderAdministrationDtos.cs`
- `PointOfSale.API/Services/Admin/IPaymentProviderAdministrationService.cs`
- `PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs`
- `PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts`
- `PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/paymentProviderTypes.ts`
- `PointOfSale.Web/src/super-admin/payment-providers/paymentProvidersService.ts`
- `PointOfSale.Web/src/super-admin/payment-providers/usePaymentProviderAdministrationState.ts`
- `PointOfSale.Tests/Architecture/PaymentProviderAdministrationFoundationTests.cs`

## Compatibility

PAYMENT-ADMIN-2 configuration contracts remain compatible. New health fields are additive, and provider actions use a canonical namespace that supports additional providers and actions later.

## Readiness for PAYMENT-ADMIN-4

The provider action model is ready for future operational actions such as health refresh, credential validation, metadata synchronization, and expanded provider diagnostics without changing checkout runtime ownership.

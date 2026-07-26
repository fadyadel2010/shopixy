# PAYMENT-ADMIN-4 Security And Secret Rotation

## Executive Summary

PAYMENT-ADMIN-4 makes payment provider secrets first-class Super Admin administration concepts. The phase adds provider-independent secret lifecycle metadata, explicit rotation APIs, masked version history, secure fingerprints, and audit records for Paymob as the first provider implementation.

This phase is secret management only. It does not execute payment transactions, change checkout behavior, modify provider adapters, restart runtime state, or perform payment execution tests.

## Secret Lifecycle

The lifecycle is explicit:

Current Secret -> Rotate Secret -> Pending Activation -> Activated -> Retired

The backend creates a pending version during rotation, immediately activates it after encrypted persistence succeeds, and retires the previous active version for the same provider secret type.

## Secret Versioning

Each secret version has canonical metadata:

- Secret Id
- Provider Code
- Secret Type
- Version
- Created
- Created By
- Activated
- Retired
- Status
- Fingerprint
- Last Rotated
- Last Used

Version metadata is stored with provider administration metadata. Secret values remain in the existing protected secret store and are never returned by lifecycle APIs.

## Rotation Workflow

Rotation performs:

1. Validate provider and secret type.
2. Validate administrator confirmation.
3. Validate replacement and confirmation fields.
4. Detect duplicate replacement against the current active backend secret.
5. Generate a SHA-256 fingerprint.
6. Protect the replacement through the provider administration secret persistence path.
7. Create the new secret version.
8. Mark previous active version retired.
9. Audit redacted metadata.
10. Return masked metadata only.

## Metadata

The UI displays version, status, created time, last rotated time, future last-used field, created by, and fingerprint. It does not display plaintext, encrypted secret values, or provider payloads.

## Fingerprints

Fingerprints are generated from SHA-256(secret) and displayed as a short comparison token such as `AB39-91AF-....`. The fingerprint exists for audit, comparison, and verification only. It is not used to reconstruct or expose the secret value.

## API Design

Endpoints:

- `GET /api/admin/payment-providers/{providerCode}/secrets`
- `POST /api/admin/payment-providers/{providerCode}/secrets/rotate`

Request:

- Secret Type
- New Secret
- Confirm New Secret
- Reason
- Administrator Confirmation

Responses return only lifecycle metadata and permissions.

## Frontend Design

Reusable components:

- `SecretCard`
- `SecretVersionList`
- `RotateSecretDialog`
- `SecretStatusBadge`
- `SecretMetadataPanel`

The Payment Providers page loads metadata through React Query and stores rotation inputs only in local dialog state until submission. No secret is written to browser storage, Redux, Zustand, telemetry, or audit.

## Backend Design

`IPaymentProviderSecretService` is the provider-independent lifecycle boundary. `PaymentProviderSecretService` implements Paymob secret type support for API Key and HMAC Secret while consuming the existing provider administration persistence path for encrypted storage.

The runtime-facing operational configuration remains unchanged and continues to load current active protected credentials naturally.

## Security

Secrets:

- Never leave backend responses in plaintext.
- Are protected before persistence by the existing provider administration secret protector.
- Are never logged by lifecycle APIs.
- Are never written to audit as plaintext or ciphertext.
- Are never placed in browser storage.
- Are never placed in global frontend state.

## Audit

Audit records include:

- Who
- When
- Provider
- Secret Type
- Version
- Fingerprint
- Reason

Audit records explicitly mark secrets as redacted and avoid plaintext and encrypted values.

## Migration

Existing configured Paymob secrets are lazily represented as version 1 active metadata. This preserves runtime behavior and avoids forcing a runtime restart. The next rotation persists full lifecycle history and retires the migrated active version.

## Testing

Coverage includes:

- Secret rotation.
- Version creation.
- Previous version retirement.
- Metadata generation.
- Fingerprint generation.
- Permission constants.
- Duplicate detection.
- Encryption-preserving persistence path.
- Redacted audit design.
- Frontend rendering.
- Runtime compatibility source assertions.

No payment execution tests are added.

## Files Added

- `PointOfSale.API/Services/Admin/IPaymentProviderSecretService.cs`
- `PointOfSale.API/Services/Admin/PaymentProviderSecretService.cs`
- `PointOfSale.Web/src/super-admin/payment-providers/components/SecretCard.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/SecretVersionList.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/RotateSecretDialog.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/SecretStatusBadge.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/SecretMetadataPanel.tsx`
- `PointOfSale.Tests/Architecture/PaymentAdmin4SecurityAndSecretRotationTests.cs`
- `docs/architecture/payment/PAYMENT-ADMIN-4_SECURITY_AND_SECRET_ROTATION.md`

## Files Modified

- `PointOfSale.Core/Payments/Administration/PaymentProviderPermissions.cs`
- `PointOfSale.API/DTOs/Admin/PaymentProviderAdministrationDtos.cs`
- `PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs`
- `PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/paymentProviderTypes.ts`
- `PointOfSale.Web/src/super-admin/payment-providers/paymentProvidersService.ts`

## Compatibility

Runtime compatibility is preserved. Rotation does not restart the application, reset runtime state, trigger checkout, or execute payment operations. Existing provider configuration remains compatible, and existing secrets migrate to active version 1 metadata without changing current payment behavior.

## Readiness for PAYMENT-ADMIN-5

PAYMENT-ADMIN-5 can build on the generic secret lifecycle service to add additional secret types, archived status, managed last-used signals, provider-specific format plugins, and deeper approval workflows without changing provider adapters or checkout execution paths.

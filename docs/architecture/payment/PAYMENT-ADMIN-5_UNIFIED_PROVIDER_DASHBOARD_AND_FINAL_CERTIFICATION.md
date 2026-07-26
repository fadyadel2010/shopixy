# PAYMENT-ADMIN-5 Unified Provider Dashboard and Final Certification

## Executive Summary

PAYMENT-ADMIN-5 completes the CommerceHub Payment Provider Administration platform with a unified Super Admin dashboard for provider configuration state, operational health, canonical capabilities, secret lifecycle metadata, recent administrative activity, provider metadata, and administrative permissions.

The dashboard is aggregation-only. It does not execute checkout, create payment sessions, mutate provider configuration, rotate secrets, test connections, publish events, or call provider adapters.

## Administration Architecture

Payment provider administration remains provider-independent through the existing `/api/admin/payment-providers` route family. The new dashboard endpoint is a read-only sibling:

- `GET /api/admin/payment-providers/dashboard`
- Existing configuration, test connection, and secret rotation routes remain unchanged.
- Existing provider adapters and checkout runtime services remain outside the dashboard dependency path.

## Dashboard Architecture

`PaymentProviderDashboardService` composes recent audit metadata and delegates provider-state aggregation to `IPaymentProviderAdministrationService.GetDashboard`.

The dashboard aggregates:

- Configuration metadata
- Operational health
- Capabilities
- Secret lifecycle metadata
- Recent administrative activity
- Provider metadata
- Administrative permissions

## Capability Model

Capabilities are backend-owned and canonical. Current capability examples include Payments, Refunds, Capture, Void, Recurring, 3DS, Tokenization, Webhooks, Payouts, Settlement, Sandbox, and Production.

Unsupported capabilities are displayed as metadata and do not imply runtime availability.

## Dashboard API

`GET /api/admin/payment-providers/dashboard` returns `PaymentProviderDashboardDto`.

The endpoint:

- Reads administrative configuration state.
- Reads health metadata previously produced by administration actions.
- Reads secret lifecycle metadata only.
- Reads redacted audit timeline metadata.
- Audits dashboard views with `PAYMENT_PROVIDER_DASHBOARD_VIEWED`.

## DTO Design

`PaymentProviderDashboardDto` contains provider-neutral root fields:

- `Providers`
- `Summary`
- `Capabilities`
- `Health`
- `Secrets`
- `Permissions`
- `RecentActivity`
- `Warnings`

No provider-specific root fields are introduced.

## Frontend Design

The frontend adds:

- `ProviderDashboard`
- `ProviderSummaryCard`
- `CapabilityGrid`
- `AdministrationTimeline`
- `ProviderStatusOverview`
- `DashboardFilters`

It reuses existing provider administration components including `ProviderHealthCard`, `SecretCard`, `ConfigurationSection`, and `ProviderBadge`. Filtering, searching, sorting, provider selection, expand/collapse, loading, and manual refresh are handled client-side over the dashboard DTO.

## Backend Design

Backend additions:

- `IPaymentProviderDashboardService`
- `PaymentProviderDashboardService`
- Dashboard DTOs in `PaymentProviderAdministrationDtos.cs`
- `IPaymentProviderAdministrationService.GetDashboard`
- `PaymentProviderAdministrationService.GetDashboard`

Provider state loading is batched through `LoadProviderStates()` when a database context is available.

## Security

The dashboard never returns:

- Plaintext secrets
- Encrypted secrets
- Tokens
- Provider payloads
- Raw audit payloads

Secret lifecycle output is limited to metadata such as type, version, status, fingerprint, actor, and timestamps.

## Performance

The dashboard batches provider setting reads by key, avoids per-section provider queries, and reuses existing administration metadata.

Recent activity is capped to 25 audit records and projected to safe display DTOs.

## Testing

Certification coverage includes:

- Dashboard aggregation
- Provider summaries
- Health rendering contract
- Secret metadata rendering contract
- Capability rendering contract
- Permission enforcement
- Filtering
- Searching
- Refresh without connection testing
- DTO serialization
- Provider independence
- Runtime isolation
- Performance guard

## Certification

PAYMENT-ADMIN-5 certifies:

- Administration is provider-independent.
- Runtime is isolated.
- Checkout is isolated.
- Secret lifecycle is isolated.
- Provider actions are canonical.
- Generic DTOs are preserved.
- Generic routes are preserved.
- Security boundaries are preserved.
- Audit boundaries are preserved.
- Frontend remains reusable.
- Future providers require no architectural redesign.

## Files Added

- `PointOfSale.API/Services/Admin/IPaymentProviderDashboardService.cs`
- `PointOfSale.API/Services/Admin/PaymentProviderDashboardService.cs`
- `PointOfSale.Web/src/super-admin/payment-providers/providerDashboardFilters.ts`
- `PointOfSale.Web/src/super-admin/payment-providers/components/ProviderDashboard.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/ProviderSummaryCard.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/CapabilityGrid.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/AdministrationTimeline.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/ProviderStatusOverview.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/DashboardFilters.tsx`
- `PointOfSale.Tests/Architecture/PaymentAdmin5UnifiedProviderDashboardCertificationTests.cs`
- `docs/architecture/payment/PAYMENT-ADMIN-5_UNIFIED_PROVIDER_DASHBOARD_AND_FINAL_CERTIFICATION.md`

## Files Modified

- `PointOfSale.API/DTOs/Admin/PaymentProviderAdministrationDtos.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.API/Services/Admin/IPaymentProviderAdministrationService.cs`
- `PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs`
- `PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs`
- `PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/paymentProviderTypes.ts`
- `PointOfSale.Web/src/super-admin/payment-providers/paymentProvidersService.ts`
- `PointOfSale.Web/src/super-admin/payment-providers/components/SecretCard.tsx`
- `PointOfSale.Tests/Architecture/PaymentAdmin3ProviderConnectivityAndHealthTests.cs`

## Final Architecture Decision

Approved. CommerceHub payment provider administration now has a unified provider-independent dashboard that aggregates existing administrative state without changing runtime behavior, provider adapters, checkout behavior, payment execution, or secret handling boundaries.

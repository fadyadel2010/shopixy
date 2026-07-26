# PAYMENT-IMPLEMENTATION-2: Paymob Provider Adapter And Canonical Integration

## Executive Summary
PAYMENT-IMPLEMENTATION-2 integrates Paymob as the first canonical payment provider behind the CommerceHub payment provider adapter boundary. CommerceHub remains the owner of payment lifecycle, state transitions, idempotency, order completion, accounting events, and vendor earnings. Paymob is isolated to authentication, official API communication, request/response mapping, hosted checkout creation, callback parsing, webhook signature verification, status lookup, capability metadata, provider retry translation, and canonical provider error translation.

## Objective
Implement a canonical Paymob provider adapter and connect `CommerceHubPaymentService` to Paymob through the provider abstraction without moving CommerceHub business rules into provider code.

## Motivation
CommerceHub payment orchestration previously had a canonical boundary, but CommerceHub payment execution still contained Paymob protocol details for authentication, order registration, payment-key creation, hosted checkout URL creation, webhook parsing, and HMAC verification. This created provider coupling inside the business runtime and made future providers harder to add consistently.

## Architectural Context
The canonical runtime is:

`Checkout Controller -> CommerceHubPaymentOrchestrationService -> CommerceHubPaymentService -> IPaymentProviderAdapter -> PaymobAdapter -> Paymob APIs`

CommerceHub owns business authority. The adapter owns provider protocol authority.

## Existing Architecture
Before this implementation, `CommerceHubPaymentService` directly used Paymob-specific constants, HMAC construction, HTTP request construction, Paymob DTOs, and hosted checkout URL construction. Paymob foundation and lower-level adapter components already existed, but CommerceHub payment initiation/webhook paths were not fully using a canonical provider adapter boundary.

## New Architecture
`IPaymentProviderAdapter` is the canonical provider adapter interface for CommerceHub provider integration. `PaymobAdapter` implements it and is registered through DI. `CommerceHubPaymentService` now resolves the Paymob adapter by provider code and calls:

- `CreateSessionAsync` for payment intention/order and hosted checkout session creation.
- `ParseWebhookAsync` for signature verification and callback/webhook parsing before CommerceHub applies business effects.
- `GetStatusAsync` for provider status lookup capability.

## Design Decisions
- Added `IPaymentProviderAdapter` under provider contracts instead of coupling CommerceHub to `IPaymobPaymentAdapter`.
- Kept CommerceHub payment persistence, duplicate handling, locks, payment records, order state changes, accounting outbox creation, and refund compensation inside `CommerceHubPaymentService`.
- Moved Paymob session creation and webhook signature/parsing into `PaymobAdapter`.
- Preserved the existing provider-session outcome-unknown behavior by translating adapter outcome-unknown failures into `CommerceHubProviderSessionOutcomeUnknownException` with the accepted Paymob order id.
- Supported both `Secret` and existing `HmacSecret` configuration names for webhook signing compatibility.
- Added configurable `BaseUrl` to Paymob options and used it for official HTTP client base address and hosted iframe URL construction.

## Provider Responsibilities
`PaymobAdapter` owns:

- Paymob authentication through `auth/tokens`.
- Paymob order/intention registration through the official API client.
- Paymob payment-key/session creation.
- Hosted checkout iframe URL construction.
- Paymob transaction status lookup.
- Webhook/callback parsing.
- Official Paymob HMAC verification before CommerceHub business processing.
- Provider capability metadata.
- Provider error translation into `CanonicalProviderError`.
- Provider outcome-unknown translation for recovery-safe retries.

## Runtime Responsibilities
CommerceHub runtime remains authoritative for:

- Checkout execution and payment initiation admission.
- Payment row creation and session persistence.
- Payment lifecycle transitions.
- Idempotency and duplicate notification behavior.
- Order completion and retry/hold decisions.
- Accounting, suspense routing, compensation, and vendor earnings.
- Domain event and outbox publication.

## Files Added
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs`
- `PointOfSale.Tests/Architecture/PaymobProviderAdapterCanonicalIntegrationTests.cs`
- `docs/architecture/payment/PAYMENT-IMPLEMENTATION-2_PAYMOB_PROVIDER_ADAPTER_AND_CANONICAL_INTEGRATION.md`

## Files Modified
- `PointOfSale.API/Configuration/CommerceHubPaymobOptions.cs`
- `PointOfSale.API/Instrumentation/PaymobOperationalTelemetry.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobFoundationService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobFoundationServiceCollectionExtensions.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs`
- `PointOfSale.API/Services/Payments/ProviderContracts/CanonicalPaymentGatewayContracts.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`
- `PointOfSale.Tests/Security/StorefrontTenantIsolationTests.cs`

## Dependency Changes
- Added canonical `IPaymentProviderAdapter` contract and Paymob implementation registration.
- `CommerceHubPaymentService` now depends on `IEnumerable<IPaymentProviderAdapter>` instead of direct Paymob options and `IHttpClientFactory` for Paymob execution.
- `PaymobAdapter` depends on Paymob options, Paymob official API client, capability provider, telemetry, and structured logging.

## API Impact
No public DTO contracts were changed. No checkout UI contracts were changed. Existing CommerceHub payment initiation and webhook endpoints remain the same.

## Database Impact
No database schema changes were made. Payment idempotency and duplicate webhook effects continue to rely on existing CommerceHub persistence and unique constraints.

## Configuration Changes
Paymob strongly typed options now support:

- `ApiKey`
- `Secret`
- `HmacSecret`
- `IntegrationId`
- `IframeId`
- `BaseUrl`
- `Environment`
- `SandboxMode`

`Secret` is accepted as an alias for webhook signing secret while `HmacSecret` remains supported for existing deployments.

## Telemetry Added
Provider telemetry remains Paymob-specific and does not duplicate orchestration telemetry. Added provider request latency metric:

- `paymob_provider_request_latency_ms`

Existing Paymob telemetry counters are used for provider attempts, successes, and failures. Structured logs include events such as `PaymobAuthenticationStarted`, `PaymobAuthenticationSucceeded`, `PaymentIntentionCreated`, `HostedCheckoutCreated`, `WebhookValidated`, and `WebhookRejected`.

## Logging Changes
Paymob provider logs are structured and scoped to provider infrastructure events. CommerceHub payment lifecycle/audit logs remain in CommerceHub payment runtime.

## Testing
Added focused tests for:

- Paymob adapter payment/session request mapping.
- Hosted checkout response mapping.
- Webhook parsing and official signature verification.
- Invalid signature rejection.
- Provider error translation to canonical errors.
- Strongly typed configuration validation for secret/base URL.
- Architecture guardrails preventing Paymob protocol in CommerceHub service and preventing adapter dependency on CommerceHub order/accounting/vendor earning types.

Updated existing CommerceHub payment service tests to verify provider adapter isolation instead of direct `IHttpClientFactory` behavior.

## Verification Results
- `dotnet build PointOfSale.sln`: Passed with existing warnings.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter PaymobProviderAdapterCanonicalIntegrationTests`: Passed, 6/6.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter CommerceHubPaymentServiceTests`: Passed, 8/8.

## Compatibility
Existing payment DTOs, database schema, checkout UI, payment lifecycle, accounting runtime, settlement runtime, payout runtime, and CommerceHub business rules were preserved. Existing `HmacSecret` configuration remains compatible while `Secret` is now also supported.

## Remaining Gaps
- Stripe still has direct integration points in `CommerceHubPaymentService`; this phase only canonicalized Paymob per scope.
- Full end-to-end Paymob sandbox execution requires live Paymob credentials and environment configuration.
- Refund execution still uses existing refund gateway paths and was not reworked in this phase.

## Next Phase (PAYMENT-IMPLEMENTATION-3)
PAYMENT-IMPLEMENTATION-3 should continue provider-boundary convergence by applying the same canonical adapter pattern to remaining online provider execution paths, expanding live sandbox certification, and adding operational runbooks for provider outage/retry/reconciliation scenarios.

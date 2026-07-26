# STRIPE-1 Foundation Certification

Date: 2026-07-11

Scope: Official Stripe SDK, immutable configuration, credential governance, environment separation, dependency injection, configuration-only health, feature flags, and architecture isolation.

Status: CERTIFIED - FOUNDATION ONLY

## Executive Summary

STRIPE-1 installs the official Stripe foundation layer without adding PaymentIntent creation, Checkout Sessions, webhook processing, capture, refund, Customer Portal, payment execution, provider adapter behavior, or Payment Runtime activation.

The frozen Payment Runtime remains provider-neutral and independent. Stripe is registered as future adapter infrastructure only; the Runtime does not reference Stripe, Stripe SDK types, provider enums, or provider objects.

Certification decision: PRODUCTION FOUNDATION CERTIFIED.

## SDK Review

- Official SDK: `Stripe.net`.
- Certified package version: `52.1.0`.
- Current official Stripe API version recorded for this foundation: `2026-06-24.dahlia`.
- Project target: `.NET 9`.
- Stripe SDK support policy confirms .NET Core 9 is supported at this date, with future support-drop guidance governed by Stripe's SDK policy.
- The foundation registers the official `StripeClient` directly. No custom Stripe SDK wrapper was introduced.
- HTTP lifecycle is owned by `IHttpClientFactory` through the named client `StripeOfficialSdk`.
- Timeout and retry count are configuration-bound.
- SDK telemetry is configuration-bound.

No Stripe API call is made by the foundation health path or registration tests.

## Configuration

`StripeOptions` is bound from:

```text
CommerceHub:Payments:Stripe
```

Certified fields:

- `Enabled`
- `PublishableKey`
- `SecretKey`
- `WebhookSecret`
- `ApiVersion`
- `Environment`
- `Timeout`
- `RetryCount`
- `TelemetryEnabled`
- `SandboxMode`
- `FeatureFlags`

`StripeOptions` and `StripeFeatureFlags` are immutable sealed records with init-only properties. Default `appsettings.json` keeps Stripe disabled and all future execution flags off.

## Credential Governance

Credential validation is startup-bound through `IValidateOptions<StripeOptions>` and `.ValidateOnStart()`.

Validation covers:

- Missing credentials when Stripe is enabled.
- Too-short placeholder-shaped credentials.
- Publishable key prefix.
- Secret or restricted key prefix.
- Webhook secret prefix.
- Sandbox/live key separation.
- SandboxMode and Environment consistency.
- Timeout and retry bounds.
- API version presence.

Secrets are not emitted by `StripeHealthStatus`, `StripeConfigurationStatus`, or `StripeFoundationHealthCheck` data. Health data reports only configuration shape and SDK availability.

Credential rotation readiness is supported by configuration binding and startup validation. Operators can rotate keys by changing external configuration or secret manager values and restarting/reloading the host according to deployment policy.

## Environment Model

Supported environments:

- `Sandbox`
- `Production`

Environment support is configuration-only. No runtime switching logic, payment execution, provider calls, or adapter branching was introduced.

Environment separation rules:

- Sandbox requires `SandboxMode = true`, `pk_test_`, and `sk_test_` or `rk_test_`.
- Production requires `SandboxMode = false`, `pk_live_`, and `sk_live_` or `rk_live_`.
- `WebhookSecret` must use `whsec_`.

## Dependency Injection

Registered services:

- `IOptions<StripeOptions>`
- `IValidateOptions<StripeOptions>`
- named `HttpClient` `StripeOfficialSdk`
- official `StripeClient`
- `IStripeFoundationHealthContributor`
- `StripeFoundationHealthCheck`

Existing payment execution services are not rewired in STRIPE-1. Existing Stripe PaymentIntent/webhook runtime remains outside this foundation certification.

## Health

Created models:

- `StripeHealthStatus`
- `StripeConfigurationStatus`
- `CredentialValidationResult`
- `SDKAvailability`

The health model is configuration-only. It performs no Stripe API request and does not validate credentials by contacting Stripe.

`StripeFoundationHealthCheck` is registered under:

```text
stripe_foundation
```

## Feature Flags

Supported flags:

- Stripe enabled: `Enabled` and `FeatureFlags.Enabled`
- Stripe sandbox enabled: `FeatureFlags.SandboxEnabled`
- Stripe production enabled: `FeatureFlags.ProductionEnabled`
- Future capability flags:
  - `PaymentIntentsEnabled`
  - `CheckoutSessionsEnabled`
  - `WebhooksEnabled`
  - `CaptureEnabled`
  - `RefundsEnabled`
  - `CustomerPortalEnabled`

All future capability flags are inert. They do not execute payment behavior.

## Architecture Boundaries

Preserved boundaries:

- Stripe knows future adapter/runtime needs.
- Runtime never references Stripe.
- Runtime contains no provider objects.
- Runtime contains no Stripe enums.
- Runtime contains no Stripe SDK dependency.
- `PointOfSale.Core` has no `Stripe.net` package reference.
- `PointOfSale.Core.Payments.Runtime` has no `using Stripe`, `Stripe.`, `PaymentIntentService`, or `CheckoutSession` references.

STRIPE-1 does not implement:

- PaymentIntent.
- Checkout Session.
- Webhook processing.
- Capture.
- Refund.
- Customer Portal.
- Payment execution.
- Provider runtime behavior.
- Provider adapter.

## Test Certification

Certification coverage:

- Configuration binding.
- Credential validation.
- Environment separation.
- DI registration.
- Official SDK availability.
- Health model serialization without secrets.
- Configuration-only health check.
- Runtime independence from Stripe SDK.
- Foundation source guard against payment execution objects.

Command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~StripeFoundationCertificationTests -nologo -v minimal
```

Observed result:

```text
Passed: 8, Failed: 0, Skipped: 0
```

Compilation command:

```text
dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal
```

Observed result:

```text
Build succeeded.
```

Warnings are pre-existing repository warnings outside the STRIPE-1 foundation scope.

## Certification Decision

| Question | Answer |
|---|---|
| Official SDK Certified? | Yes. `Stripe.net` is pinned to `52.1.0` and official `StripeClient` is registered. |
| Configuration Certified? | Yes. Immutable `StripeOptions` covers required foundation fields. |
| Credential Governance Certified? | Yes. Startup validation enforces credential presence, shape, and environment separation when enabled. |
| Environment Separation Certified? | Yes. Sandbox and Production are configuration-only and fail closed on key-mode mismatch. |
| Dependency Injection Certified? | Yes. SDK, options, validation, HTTP lifecycle, and health contributors are registered. |
| Health Model Certified? | Yes. Health is configuration-only and performs no API calls. |
| Architecture Boundaries Preserved? | Yes. Stripe does not enter the frozen runtime. |
| Runtime Independence Preserved? | Yes. Runtime has no Stripe SDK or provider dependency. |
| Production Foundation Certified? | Yes, for foundation only. |
| Recommend next phase? | Yes. Begin STRIPE-2 provider adapter design against the frozen Payment Runtime, still without enabling live payment execution until separately certified. |

## Final Verdict

STRIPE-1 is certified as a production-ready Stripe foundation layer. It prepares the platform for future Stripe adapter implementation while preserving the frozen provider-neutral Payment Runtime.

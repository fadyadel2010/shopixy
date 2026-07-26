# PAYMOB-1 Foundation Certification

Date: 2026-07-13

Scope: POS Admin and Storefront Paymob foundation only

Excluded: payment execution, callbacks, webhooks, refunds, provider adapter, runtime bridge, persistence, controllers, frontend, and Payment Runtime changes

## Executive Summary

Paymob foundation is implemented as a provider-specific configuration and operational-readiness layer that adapts to the certified Stripe reference architecture without copying Stripe wire mechanics.

The Payment Runtime remains frozen and unmodified. Paymob foundation does not execute payments, process callbacks, create provider evidence, mutate SQL payment lifecycle state, or register any provider adapter.

## Foundation Architecture

The foundation owns:

- immutable Paymob configuration;
- environment and sandbox/production validation;
- credential shape validation;
- integration and iframe identifier validation;
- callback configuration validation;
- feature flag validation;
- capability declaration;
- official Paymob HTTPS API dependency registration;
- configuration-only health/readiness contribution.

It deliberately does not own provider execution or lifecycle authority.

## Configuration Model

Canonical configuration lives under `CommerceHub:Payments:Paymob` and binds to `PaymobOptions`.

`CommerceHubPaymobOptions` remains as a compatibility subclass for existing application code. The canonical foundation type is `PaymobOptions`.

Key fields:

- `ApiKey`
- `IntegrationId`
- `IframeId`
- `HmacSecret`
- `Environment`
- `SandboxMode`
- `Timeout`
- `RetryCount`
- `FeatureFlags`
- `Callback`
- `Capabilities`

## Credential Governance

Credentials are required only when Paymob is enabled. Startup validation fails closed for missing API key, missing HMAC secret, invalid or missing integration identifiers, too-short secrets, and disabled environment feature flags.

Health output reports credential presence only. It does not serialize API keys or HMAC secrets.

## Environment Separation

Sandbox requires `SandboxMode = true` and the sandbox feature flag. Production requires `SandboxMode = false` and the production feature flag.

This mirrors the Stripe reference provider’s environment authority pattern while avoiding Stripe-specific key-prefix rules that Paymob does not expose as an official capability in this codebase.

## Dependency Injection

Registered foundation services:

- `PaymobOptions`
- `PaymobOptionsValidator`
- named `HttpClient` `PaymobOfficialApi`
- `IPaymobFoundationHealthContributor`
- `PaymobFoundationHealthCheck`

No payment service, callback handler, webhook handler, runtime bridge, lifecycle writer, persistence component, controller, or frontend component is registered by the foundation extension.

## Health Model

`PaymobFoundationHealthContributor` reports:

- foundation health;
- credential health;
- environment health;
- configuration health;
- HTTP dependency health;
- operational readiness contribution.

The health check is configuration-only and does not call Paymob.

## Security Review

Certified controls:

- secrets are not serialized in foundation health;
- no API key or HMAC secret is logged by foundation code;
- credentials remain server-side configuration;
- tenant execution remains outside foundation and must stay server-authoritative;
- unofficial wrappers are not introduced;
- official Paymob HTTPS APIs are accessed through .NET `HttpClient`.

## Architecture Rules

1. Paymob foundation must not modify `PointOfSale.Core.Payments.Runtime`.
2. Paymob foundation must not create provider evidence.
3. Paymob foundation must not mutate payment lifecycle state.
4. Paymob foundation must not register provider adapters or runtime bridges.
5. Paymob foundation must not implement Accept Payment, iframe execution, card, wallet, Apple Pay, Google Pay, webhook, callback, refund, capture, or void behavior.
6. Paymob must adapt to the frozen Runtime in later phases.
7. Stripe remains the permanent reference provider for architecture shape.

## Certification Decision

Paymob foundation is certified for source-level configuration, credential governance, environment separation, dependency injection, health, startup validation, security boundary preservation, and runtime independence.

Operational production validation and payment execution certification are not part of this phase.

## Success Answers

| Question | Answer |
|---|---|
| Official SDK certified? | Yes for official Paymob HTTPS APIs via `HttpClient`; no unofficial wrapper introduced. |
| Configuration certified? | Yes. |
| Credential governance certified? | Yes. |
| Environment separation certified? | Yes. |
| Dependency Injection certified? | Yes. |
| Health model certified? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Foundation certified? | Yes. |
| Recommend next phase? | Yes: Paymob provider adapter/runtime integration design, still adapting to the frozen Runtime and Stripe reference architecture. |

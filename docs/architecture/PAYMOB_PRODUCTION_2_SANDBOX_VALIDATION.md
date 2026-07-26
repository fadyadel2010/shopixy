# PAYMOB-PRODUCTION-2 Sandbox Validation

Date: 2026-07-14

Scope: POS Admin and Storefront Paymob payment pipeline validation against the certified Payment Runtime, Paymob adapters, application contracts, and PAYMOB-PRODUCTION-1 operational readiness.

Status: **SANDBOX EXECUTION NOT CERTIFIED IN THIS WORKSPACE; SOURCE-LEVEL PRODUCTION VALIDATION COMPLETE**

## Executive Summary

This pass validates Paymob deployment readiness and the production sandbox validation boundaries that can be proven from the repository without official Paymob Sandbox credentials, a deployed public callback URL, a deployed public webhook URL, tenant Sandbox configuration, and live Paymob callback/webhook delivery.

The frozen Payment Runtime, Runtime Bridge, Provider Adapter, Webhook Adapter, application contracts, business rules, controllers, frontend, persistence, and SQL were not modified. This report separates source certification and operational readiness from live Sandbox execution evidence.

Live Paymob Sandbox execution is **not certified** by this report because this workspace exposes no Paymob Sandbox credentials, repository defaults keep Paymob disabled with empty credentials, no enabled tenant Paymob configuration is available, and no public Paymob callback/webhook delivery channel is available to execute the required end-to-end drills.

## Certification Separation

| Layer | Decision | Evidence boundary |
| --- | --- | --- |
| Source Certification | Source-level Paymob deployment readiness is validated except for the documented payment-artifact log redaction finding. | Repository inspection, focused architecture tests, Paymob source certifications, and frozen Runtime certifications. |
| Operational Readiness | Source-level operational readiness remains certified by PAYMOB-PRODUCTION-1. | Health checks, telemetry, metrics, alerts, runbooks, and failure-drill documentation. |
| Deployment Readiness | Deployment readiness is partially validated from source gates only. | Configuration, tenant, credential, health, callback, webhook, replay, audit, and tenant-isolation gates are present but not green in a target Sandbox deployment. |
| Live Sandbox Evidence | Not certified. | Official Sandbox credentials, public callback/webhook reachability, Paymob event delivery, live transactions, and latency measurements are unavailable in this workspace. |

## Environment Validation

| Item | Evidence | Decision |
| --- | --- | --- |
| Sandbox credentials | Repository defaults have `Paymob.Enabled=false`, empty `ApiKey`, empty `IntegrationId`, empty `IframeId`, and empty `HmacSecret`. No Paymob environment variable names were present in this process. | Not present in workspace; must be supplied by deployment secret manager or local user secrets before live drill. |
| Sandbox callback URL | `PaymobOptionsValidator` requires an absolute server-owned callback URL when Paymob is enabled. | Source-certified; public reachability not live-verified. |
| Sandbox webhook URL | `PaymobOptionsValidator` requires an absolute server-owned webhook URL when Paymob is enabled. | Source-certified; public Paymob delivery not live-verified. |
| Tenant enablement | `PaymobOperationalReadinessHealthCheck` requires an enabled, encrypted tenant `PAYMOB` method. | Source-certified; no live tenant configuration observed. |
| Feature flags | Sandbox execution requires Paymob enabled, `SandboxMode=true`, and the sandbox feature flag. Execution flags remain fail-closed under current foundation validation. | Source-certified; deployment configuration pending. |
| Health status | `paymob_foundation` and `paymob_operational_readiness` are registered. Operational readiness checks provider, environment, credentials, callback/webhook URLs, replay store, webhook audit store, tenant configuration, and dependency registration. | Source-certified; target environment health not measured. |
| Operational readiness | PAYMOB-PRODUCTION-1 certifies source-level readiness for health, telemetry, metrics, alerts, runbooks, and failure drills. | Source-certified; live operational sign-off pending. |

## Validation Matrix

| Scenario | Validation evidence | Result |
| --- | --- | --- |
| Environment configuration | `PaymobCredentialValidator` fails closed for missing credentials, Sandbox/Production mode mismatch, disabled sandbox or production flags, missing HMAC secret, and missing callback/webhook configuration. | Source-certified; live Sandbox configuration not provisioned. |
| Tenant configuration | Operational readiness requires enabled encrypted tenant `PAYMOB` configuration and execution services resolve tenant-scoped configuration. | Source-certified; live tenant Sandbox method not observed. |
| Credential validation | API key, integration id, iframe id, HMAC secret, numeric merchant identifiers, and absolute callback/webhook URLs are required before enablement can pass. | Source-certified; official credentials not validated against Paymob. |
| Create payment | `PaymobPaymentService.CreatePaymentIntentAsync` validates tenant, order, customer, currency, idempotency, and routes through `PaymobRuntimeApplicationService` before creating Paymob redirect/iframe material. | Source-certified; live payment-key/iframe creation not executed. |
| Redirect / Iframe generation | Paymob create flow persists provider order/payment key and redirect/iframe routing metadata when provider creation succeeds. | Source-certified; live Paymob iframe URL not generated. |
| Successful payment | Verified terminal success is admitted through `PaymobWebhookRuntimeIntegrationService` before `PaymentLifecycleService.HandlePaymentResultAsync` records success. | Source-certified; live Sandbox success not executed. |
| Declined payment | Terminal negative provider evidence is classified for application lifecycle handling and recorded as failed without treating provider-negative evidence as a positive Runtime financial decision. | Source-certified; live declined payment not executed. |
| Cancelled payment | Paymob `is_voided` is treated as terminal negative application lifecycle evidence. | Source-certified; live cancelled callback/webhook not executed. |
| Webhook delivery | `PaymobWebhooksController` delegates to `IPaymobPaymentService.ProcessWebhookAsync`; service verifies HMAC, performs Runtime admission/replay checks, logs webhook state, then persists lifecycle outcome. | Source-certified; live Paymob delivery not observed. |
| Callback delivery | `ValidateCallbackAsync` resolves local payment intent candidates and validates Paymob callback HMAC before returning callback status. | Source-certified; live browser callback not observed. |
| HMAC verification | `PaymobWebhookHmacVerifier` computes HMAC-SHA512 over canonical Paymob payload and compares with fixed-time hex comparison; callback HMAC validation is also enforced. | Source-certified; live signed payload not verified. |
| Replay rejection | `PaymobWebhookPriorEvidenceLoader` loads durable prior evidence and rejects provider-event replay before lifecycle mutation. | Source-certified. |
| Duplicate rejection | Processed `PaymentWebhookLogs` and duplicate hooks reject duplicate webhook/callback effects before lifecycle mutation. | Source-certified. |
| Background recovery | Create-path recovery handles incomplete Paymob payment-intent material and deterministic fallback resolves missing provider intent from webhook merchant order id. | Source-certified; live recovery drill not executed. |
| Retry | Create idempotency keys are normalized; webhook duplicate/replay is collapsed; provider failures remain explicit without false success. | Source-certified; live retry drill not executed. |
| Refund | Provider refund webhooks reconcile through `PaymentLifecycleService.RecordRefundAsync`; advanced adapter supports official Paymob refund translation. | Source-certified; live refund not executed. |
| Partial refund | Refund delta is computed from cumulative provider refund amount and existing local refunds; advanced adapter supports partial refund translation. | Source-certified; live partial refund not executed. |
| Void | Advanced adapter supports official Paymob void translation; webhook `is_voided` is terminal negative evidence. | Source-certified; live void not executed. |
| Capture | Advanced adapter supports official Paymob capture translation and maps captured state in evidence metadata. | Source-certified; live capture not executed. |
| Transaction inquiry | Advanced adapter supports official Paymob transaction inquiry translation. | Source-certified; live inquiry not executed. |
| Recovery | Runtime admission, durable replay records, webhook logs, deterministic fallback lookup, and idempotency preserve recovery without inventing success. | Source-certified; live recovery latency not measured. |
| Tenant isolation | Tenant id scopes configuration resolution, payment intent lookup, webhook replay records, webhook logs, order/customer validation, and metrics tenant buckets. | Source-certified; two-merchant Sandbox drill not executed. |

## Observed Results

Validated in this workspace:

- Required certification documents were reviewed: `PAYMENT_RUNTIME_FINAL_CERTIFICATION`, `STRIPE_FINAL_RECERTIFICATION`, `PAYMOB_PRODUCTION_1_OPERATIONAL_READINESS`, and `STRIPE_PRODUCTION_2_SANDBOX_VALIDATION`.
- All `.ai-context` files were reviewed for governing architecture context: `ARCHITECTURE_TRUTH.md`, `CODEX_RULES.md`, `CROSS_SYSTEM_AUTHORITY_HARDENING.md`, `KNOWN_RISKS.md`, `LESSON_UPDATE_RULES.md`, `LOCAL_DEV_STRIPE_HANDOFF_2026-05-12.md`, and `PROJECT_CONTEXT.md`.
- The Payment Runtime remains frozen and provider-neutral.
- Paymob source boundaries preserve Runtime admission before lifecycle persistence for successful and refund outcomes.
- Paymob HMAC, replay, duplicate, tenant-configuration, health, telemetry, and operational-readiness controls are present at source level.
- No Paymob Sandbox credential variables were visible in this process.

Not observed in this workspace:

- Official Paymob Sandbox credential authentication.
- Live payment-key creation or iframe URL generation.
- Live successful, declined, cancelled, refund, partial refund, void, capture, or inquiry execution.
- Live public callback or webhook reachability.
- Live Paymob HMAC delivery from Paymob infrastructure.
- Live latency measurements.

## Operational Findings

| Finding | Disposition |
| --- | --- |
| Repository defaults are disabled and empty for Paymob credentials. | Correct fail-closed posture for source control. |
| Operational readiness requires database connectivity, replay store, webhook audit store, tenant configuration, and service registrations. | Correct deployment-readiness gate; must be green in Sandbox. |
| Paymob execution remains tenant-scoped and does not add provider state to Runtime. | Certified source behavior. |
| Paymob create-path payment-artifact logging | Closed by PAYMOB-SECURITY-HOTFIX-1. Paymob production logs no longer emit structured `PaymentKey` or `IframeUrl` values. |
| Live Sandbox evidence is absent. | Blocks Production Sandbox certification. |

## Security Validation

| Control | Decision |
| --- | --- |
| Credential isolation | Tenant Paymob configuration is encrypted and decrypted inside application services; credentials do not enter Runtime. Source-certified. |
| Tenant isolation | Tenant id scopes configuration, orders, payment intents, replay records, webhook logs, and telemetry buckets. Source-certified; live two-tenant drill pending. |
| Environment isolation | Sandbox/Production environment and `SandboxMode` must agree; sandbox and production flags gate execution. Source-certified. |
| HMAC verification | Webhook and callback HMAC verification fail closed. Source-certified; live signed delivery pending. |
| Replay protection | Durable Paymob replay records reject provider-event replay before lifecycle mutation. Source-certified. |
| Duplicate protection | Webhook logs and duplicate admission hooks collapse already processed provider events. Source-certified. |
| Telemetry redaction | `PointOfSale.Payments.Paymob` metrics use `tenant_bucket` and operational labels, not raw tenant ids or secrets. Source-certified. |
| Secret and artifact logging | API keys and HMAC secrets were not found in reviewed structured log templates. PAYMOB-SECURITY-HOTFIX-1 closed the create-path payment-artifact log finding by omitting `PaymentKey` and `IframeUrl` from Paymob production log templates and logger arguments. |

Validated Paymob metric names:

- `paymob_payment_create_attempt_total`
- `paymob_payment_create_success_total`
- `paymob_provider_failure_total`
- `paymob_webhook_received_total`
- `paymob_replay_rejected_total`
- `paymob_duplicate_rejected_total`
- `paymob_runtime_admitted_total`
- `paymob_runtime_rejected_total`
- `paymob_refund_received_total`
- `paymob_void_received_total`
- `paymob_capture_received_total`
- `paymob_background_recovery_total`
- `paymob_retry_total`

## Performance Notes

No real Sandbox timing was captured in this workspace. These are the acceptance targets for the required live drill:

| Measurement | Target |
| --- | --- |
| Create latency | p95 <= 2500 ms excluding user browser/card interaction. |
| Webhook latency | p95 <= 1000 ms for admitted terminal events excluding provider delivery delay. |
| Callback latency | p95 <= 1000 ms excluding browser network variability. |
| Recovery latency | p95 <= 3000 ms for incomplete intent recovery excluding Paymob outage. |
| Retry latency | p95 <= 3000 ms for retryable create/recovery operation excluding Paymob outage. |
| Replay or duplicate detection latency | p95 <= 250 ms with indexed replay/webhook-log lookup. |
| Runtime latency | p95 <= 50 ms for in-memory Runtime execution. |

These targets are documented only. No Runtime or provider optimization is authorized by this phase.

## Outstanding Deployment Dependencies

Before Production Sandbox certification can be granted, operators must provide and verify:

1. Official Paymob Sandbox API key, integration id, iframe id, and HMAC secret through the approved secret path.
2. Enabled encrypted tenant `PAYMOB` payment method in the target Sandbox database.
3. Public HTTPS callback URL registered with Paymob and reachable from Paymob infrastructure.
4. Public HTTPS webhook URL registered with Paymob and reachable from Paymob infrastructure.
5. Green `paymob_foundation` and `paymob_operational_readiness` health in the target deployment.
6. Applied database migrations including Paymob replay and webhook audit storage.
7. Live create, redirect/iframe, success, decline, cancel, webhook, callback, replay, duplicate, retry, recovery, refund, partial refund, void, capture, inquiry, and tenant-isolation drills where Paymob Sandbox supports the operation.
8. Measured create, webhook, callback, recovery, retry, replay, duplicate, and Runtime latency in the target deployment.
9. Operator confirmation that telemetry, alert routing, dashboards, and runbooks are active.

## Stripe Delta

Only provider protocol differences are documented. The operational validation methodology remains identical to STRIPE-PRODUCTION-2.

| Area | Stripe | Paymob |
| --- | --- | --- |
| Provider protocol | PaymentIntent, client secret, card confirmation, and Stripe Workbench webhook flow. | API key authentication, Paymob order/payment key, redirect/iframe flow, callback, and webhook flow. |
| Signature model | Official Stripe SDK signature verification over raw payload and `whsec_` secret. | Paymob HMAC-SHA512 verification over canonical callback/webhook payload and HMAC secret. |
| Credential shape | Publishable key, secret/restricted key, webhook secret, API version. | API key, numeric integration id, numeric iframe id, HMAC secret. |
| Browser return | Stripe client confirmation and webhook completion. | Paymob redirect/iframe plus callback and webhook evidence. |
| Advanced operations | Stripe refund, capture, cancel, retrieve adapter translations. | Paymob refund, partial refund, void, capture, and transaction inquiry adapter translations. |
| Metrics meter | `PointOfSale.Payments.Stripe`. | `PointOfSale.Payments.Paymob`. |

The architecture remains identical: provider-specific protocol handling terminates inside the provider boundary, provider facts are adapted to canonical Runtime evidence, and SQL lifecycle mutation remains application-owned.

## Certification Decision

Paymob source-level production validation and deployment readiness are certified for POS Admin and Storefront Paymob operations. The documented create-path payment-artifact log redaction finding is closed by PAYMOB-SECURITY-HOTFIX-1.

Paymob live Production Sandbox execution is **not certified** in this workspace. Official Paymob Sandbox credentials, public callback/webhook reachability, tenant enablement, live Paymob delivery, and measured latency are unavailable here. Live success must not be inferred from source code.

Paymob source-level security certification is no longer blocked by the payment-artifact log redaction finding. Live Sandbox security certification still requires official Paymob Sandbox credentials, public callback/webhook reachability, live signed delivery, tenant isolation drills, and deployment evidence.

No new permanent cross-provider architecture truth was discovered. `.ai-context` is not modified.

## Success Answers

| Question | Answer |
| --- | --- |
| Environment validated? | Partially. Source configuration and fail-closed readiness validated; real Sandbox secrets, tenant configuration, and public callback/webhook endpoints were not present. |
| Create Payment certified? | Source-certified for the Paymob create pipeline; live Sandbox payment creation not certified. |
| Payment Key certified? | Source-certified for adapter/application payment-key handling; live Paymob payment-key generation not certified. |
| Iframe generation certified? | Source-certified for iframe URL construction and routing metadata; live Paymob iframe generation not certified. |
| Successful payment certified? | Source-certified for POS Admin/Storefront pipeline; live Sandbox successful payment not certified. |
| Declined payment certified? | Source-certified for terminal negative lifecycle path; live declined payment not certified. |
| Webhook certified? | Source-certified for HMAC, admission, replay, duplicate, Runtime, and lifecycle ordering; live Paymob delivery not certified. |
| Callback certified? | Source-certified for callback HMAC validation and local intent resolution; live browser callback not certified. |
| HMAC certified? | Source-certified for callback and webhook HMAC validation; live Paymob-signed delivery not certified. |
| Replay certified? | Source-certified with durable replay store and Runtime duplicate/replay rejection. |
| Duplicate certified? | Source-certified with webhook log duplicate collapse and duplicate rejection hooks. |
| Recovery certified? | Source-certified for incomplete intent recovery and deterministic webhook fallback; live recovery drill not certified. |
| Retry certified? | Source-certified for idempotency and explicit retry/failure behavior; live retry drill not certified. |
| Refund certified? | Source-certified for webhook-driven refund reconciliation and adapter translation; live refund not certified. |
| Void certified? | Source-certified for adapter translation and voided webhook evidence; live void not certified. |
| Capture certified? | Source-certified for adapter translation and capture metadata; live capture not certified. |
| Tenant isolation certified? | Source-certified; real two-tenant Paymob Sandbox drill not executed. |
| Security certified? | Source-certified after PAYMOB-SECURITY-HOTFIX-1. Live security certification remains deployment-dependent. |
| Performance acceptable? | Not measured; acceptance targets documented. |
| Production Sandbox certified? | **No. Live Paymob Sandbox execution was not completed in this workspace.** |
| Recommend next phase? | Yes. Provision official Paymob Sandbox credentials and public callback/webhook endpoints, enable a Sandbox tenant, run the live drill matrix, then proceed only after external evidence is captured. |

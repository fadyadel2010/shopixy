# PAYMOB Certification Closure 5 - Sensitive Artifact Protection

Date: 2026-07-14  
Scope: `PAYMOB-FINAL-006` only  
Payment Runtime posture: frozen and unchanged  
Decision: **HIGH FINDING CLOSED**

## Finding

`PAYMOB-FINAL-006 - Sensitive provider artifacts are persisted in plaintext` was High because reusable Paymob payment keys were stored in `PaymentIntent.ProviderMetadata`, token-bearing iframe URLs were stored in `PaymentIntent.RedirectUrl`, and Paymob webhook bodies and raw HMAC signatures were stored directly in `PaymentWebhookLog`.

Durable checkout replay and payment-created event projections could also copy the iframe URL. Anyone with read access to those stores could obtain reusable payment material or raw provider security evidence.

This closure addresses only `PAYMOB-FINAL-006`. It does not close, modify, or reinterpret any other Paymob finding.

## Root Cause

The application treated all provider output as ordinary recovery or audit metadata. It did not distinguish canonical identifiers from reusable credentials, token-bearing URLs, raw signatures, and payloads that can contain provider authorization or customer material.

Recovery depended on the payment key plus iframe URL, and unresolved-webhook reconciliation depended on the webhook body. Those dependencies made simple deletion unsafe, but they did not require plaintext persistence. Raw HMAC values were not required for replay, verification, recovery, or reconciliation after admission.

## Artifact Classification

| Artifact | Durable location before closure | Recovery | Audit / reconciliation / debugging | Protection decision |
|---|---|---:|---:|---|
| Payment key | `PaymentIntent.ProviderMetadata` | Required for accepted-key checkpoint and checkout replay | Not required in raw form | Encrypt with the iframe URL in a purpose-isolated session envelope |
| Iframe URL | `PaymentIntent.RedirectUrl`, event and replay snapshots | Required to resume the customer redirect; sensitive because it embeds `payment_token` | Not required in raw durable projections | Encrypt in the session envelope; clear `RedirectUrl`; omit from durable event/replay projections |
| Webhook payload | `PaymentWebhookLog.RawPayload` | Required for unresolved webhook recovery | Required for audit, reconciliation, and debugging | Encrypt with a separate Data Protection purpose; decrypt only inside recovery |
| Callback payload | Request query parameters | Not required after callback validation | Canonical order/transaction outcomes are sufficient | Never durably persist; discard after in-memory validation |
| HMAC signature | `PaymentWebhookLog.Signature` | Not required | Digest is sufficient for correlation and audit evidence | Store lowercase SHA-256 digest with `sha256:` prefix; never store raw HMAC |
| Provider auth token | Adapter request memory | Not required after provider call | Not required | Never persist; discard after request execution |
| Authorization header | Adapter HTTP message | Not required | Not required | Never persist; remains transient only |
| API key / HMAC secret / authentication material | Existing encrypted tenant payment configuration, then request memory | Required to admit later operations through the existing credential authority | Raw value not required in payment evidence | Existing encrypted configuration remains authoritative; no copy is written to payment artifacts |
| Provider response metadata | Runtime/adapter memory and selected payment routing fields | Provider order identity is required | Canonical IDs/status are required | Persist only canonical provider order ID, merchant order ID, state, and timestamp; session secrets are encrypted separately |
| Routing metadata | `PaymentIntent.ProviderMetadata` | Tenant, order, storefront base URL, provider order ID, and checkpoint are required | Required for reconciliation | Keep canonical non-secret projection in plaintext; store only an opaque protected session alongside it |
| Replay / duplicate evidence | SQL payment intent, webhook event ID, Redis idempotency value, checkout idempotency snapshot | Required | Required | Keep canonical IDs and hashes; reconstruct the live iframe response from the encrypted session; durable Paymob response fields remain null |

## Protection Strategy

`PaymobSensitiveArtifactProtector` uses ASP.NET Core Data Protection with separate versioned purposes for payment sessions and webhook payloads:

- `paymob-session:v1:` protects the payment key and iframe URL as one atomic recovery envelope;
- `paymob-webhook:v1:` protects the complete webhook body needed for audit and suspense recovery;
- `sha256:` identifies the one-way digest stored instead of the raw HMAC signature.

Purpose separation prevents a webhook envelope from being accepted as a payment session. Envelope prefixes allow deterministic classification without attempting plaintext fallback. Invalid, incomplete, corrupt, or wrongly purposed envelopes throw and fail closed.

New routing serialization cannot write a plaintext `PaymentKey`. `PaymentIntent.RedirectUrl` is cleared for Paymob after session protection. Paymob payment-created events and checkout idempotency snapshots persist no iframe URL or payment key. The live DTO remains unchanged and receives decrypted session material only when serving the authorized request.

The one-shot `PaymobSensitiveArtifactBackfillHostedService` upgrades existing rows before normal hosted operation:

1. Legacy structured payment keys and iframe URLs become one encrypted session envelope.
2. Token-bearing `RedirectUrl` is cleared.
3. Legacy webhook bodies are encrypted.
4. Legacy raw HMAC signatures are replaced with digests.
5. An unclassifiable or incomplete legacy session aborts startup with the exact payment intent ID. It is not discarded, guessed, or retried through a new provider session.

No schema, entity, table, index, Runtime contract, or provider protocol was redesigned.

## Compatibility Analysis

Replay protection still uses canonical provider event IDs and the existing webhook idempotency store. Neither depends on the raw HMAC. Duplicate detection still resolves the same `PaymentWebhookLog` and payment intent identities.

Webhook audit retains the complete original payload as encrypted evidence and records whether signature verification succeeded. The signature digest supports evidence correlation without retaining reusable raw material.

Provider reconciliation retains provider transaction/order IDs, merchant order ID, checkpoint state, tenant/order routing, processing status, timestamps, and encrypted webhook evidence. No canonical reconciliation identifier was removed or transformed.

Provider authorization values, authentication tokens, callback parameters, and Runtime provider evidence are transient. They are not copied into durable payment, event, replay, or webhook records.

## Recovery Preservation

Checkpoint and checkout recovery decrypt the protected payment session only after the existing payment intent, tenant, order, and checkpoint checks succeed. Accepted payment-key recovery still returns the original payment key and iframe URL without another provider call.

Unresolved webhook recovery decrypts `RawPayload` only inside `StorefrontUnresolvedPaymentReconciliationJob`, then runs the existing parser, tenant resolver, duplicate handling, and payment reconciliation. Legacy suspense rows are protected before continued processing.

If the Data Protection key ring is missing, an envelope is corrupt, or legacy payment material is incomplete, recovery fails closed. The exact preservation constraint is that payment-key recovery requires both the original key and its matching iframe URL, while webhook recovery requires the original body. Therefore these artifacts are encrypted rather than digested or discarded. Key restoration or manual reconciliation is required; Runtime and replay are not weakened.

## Architecture Preservation

Unchanged by this closure:

- `PointOfSale.Core/Payments/Runtime/**`;
- `PaymobRuntimeBridge`;
- `PaymobPaymentAdapter` request mapping and provider protocol;
- `PaymobWebhookEvidenceAdapter` and webhook admission protocol;
- payment lifecycle and business transition rules;
- controllers, routes, DTO contracts, and frontend;
- CommerceHub;
- database entities, EF mappings, migrations, tables, and indexes.

Changes are confined to the existing Paymob persistence/application boundary, checkout recovery/idempotency persistence compatibility, unresolved webhook recovery, DI registration, tests, and certification documentation.

## Evidence

Implementation evidence:

- `PointOfSale.API/Services/Payments/Paymob/PaymobSensitiveArtifactProtector.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobSensitiveArtifactBackfillHostedService.cs`
- `PointOfSale.API/Services/Payments/PaymentIntentRoutingMetadata.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobProviderCheckpointStore.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Payments/StorefrontUnresolvedPaymentReconciliationJob.cs`
- `PointOfSale.Tests/Architecture/PaymobCertificationClosure5Tests.cs`

Focused tests prove encryption round trips, plaintext exclusion, HMAC digestion, malformed-envelope fail-closed behavior, legacy row backfill, accepted-key checkpoint recovery, webhook recovery compatibility, durable event/replay omission, and frozen architecture guards.

Validation executed on 2026-07-14:

| Validation | Result |
|---|---:|
| API build | **Passed; 0 errors**; existing repository warnings remain |
| Test project build | **Passed; 0 errors**; existing repository warnings remain |
| Closure 5 tests | **8 passed, 0 failed** |
| Closure 5 + checkpoint + callback focused tests | **16 passed, 0 failed** |
| Applicable Paymob + frozen Payment Runtime regression excluding the unrelated artifact assertion | **226 passed, 0 failed** |
| All Paymob-named tests | **131 passed, 1 unrelated failure** |

The sole Paymob aggregate failure is `PaymobPaymentAuthorityCertificationTests.CertificationArtifacts_MustRecordSfPay5Truth`, which expects the unrelated historical marker `PASS SF-PAY5` in `PAYMENT_POLICY_ENFORCEMENT`. This closure does not alter another finding's certification artifact.

## Certification Decision

`PAYMOB-FINAL-006` is closed for the audited durable Paymob artifact paths. New and legacy reusable payment sessions are encrypted, raw webhook evidence is encrypted, raw HMAC signatures are absent, and transient provider authentication material is not persisted. Replay, duplicate detection, checkpoint recovery, webhook audit, and reconciliation retain their existing authority and behavior.

This decision does not grant overall Paymob Software, Security, Operations, Migration, Deployment, CommerceHub, template, or Live Operational certification. Every other PAYMOB-FINAL finding retains its prior disposition.

## Success Answers

| Question | Answer |
|---|---|
| High finding closed? | **Yes - `PAYMOB-FINAL-006` only.** |
| Sensitive artifacts protected? | **Yes. Durable reusable or raw security material is encrypted, digested, omitted, or kept transient.** |
| PaymentKey protected? | **Yes. It is stored only inside the versioned encrypted payment-session envelope.** |
| Raw HMAC removed? | **Yes. Durable HMAC evidence is a SHA-256 digest only.** |
| Replay preserved? | **Yes. Canonical event IDs, hashes, and idempotency evidence are unchanged.** |
| Recovery preserved? | **Yes. Checkpoint and checkout recovery decrypt the session in memory; unresolved webhook recovery decrypts the payload in memory.** |
| Audit preserved? | **Yes. Full webhook evidence remains encrypted and signature-verification status plus digest remain queryable.** |
| Runtime preserved? | **Yes. The frozen Payment Runtime and Runtime Bridge are unchanged.** |
| Architecture preserved? | **Yes. No storage redesign, protocol redesign, controller/frontend change, or CommerceHub change was made.** |
| Regression passed? | **Yes for Closure 5 and applicable focused coverage. The Paymob aggregate retains one documented unrelated certification-text failure.** |
| Recommend Closure-6? | **Yes, only for its separately scoped finding after this closure is accepted.** |

**FINAL DECISION: PAYMOB-FINAL-006 CLOSED. DURABLE PAYMOB SECURITY ARTIFACTS ARE PROTECTED, AND RECOVERY, REPLAY, AUDIT, RUNTIME, AND ARCHITECTURE AUTHORITY ARE PRESERVED.**

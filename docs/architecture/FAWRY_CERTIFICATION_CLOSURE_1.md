# FAWRY Certification Closure 1 - Sensitive Artifact Protection

Date: 2026-07-15  
Scope: durable Fawry webhook artifact protection only  
Payment Runtime posture: frozen and unchanged  
Decision: **HIGH FINDING CLOSED**

## Root Cause

`FAWRY_PRODUCTION_2_SANDBOX_VALIDATION` identified one High security exception: `FawryWebhookController` wrote the complete raw webhook JSON and raw Fawry message signature into `PaymentWebhookLogs`.

The payload is required as durable audit evidence, but plaintext persistence was not required. The raw message signature is not required after signature admission; a cryptographic digest is sufficient for audit correlation. The previous persistence path did not distinguish those security properties.

This closure addresses only that durable artifact-protection finding. It does not change or reinterpret any other Fawry certification, deployment, Sandbox, telemetry, or operations decision.

## Fix

`FawrySensitiveArtifactProtector` applies the certified Paymob strategy with a Fawry-specific Data Protection purpose and envelope:

- `fawry-webhook:v1:` contains the encrypted webhook evidence;
- every `messageSignature` property is removed recursively before encryption so a reversible raw signature is not retained inside the payload envelope;
- `sha256:` identifies the lowercase SHA-256 digest stored in `PaymentWebhookLog.Signature`;
- invalid, corrupt, wrongly purposed, plaintext, or signature-bearing recovered envelopes fail closed.

The controller still receives and verifies the original payload in memory. Only the persistence values are redirected to the protected payload and signature digest. Routes, request contracts, response contracts, admission, ordering, lifecycle behavior, and provider protocol are unchanged.

`FawrySensitiveArtifactBackfillHostedService` protects legacy Fawry webhook rows before normal hosted operation. It encrypts signature-scrubbed legacy JSON, replaces a legacy raw signature with its digest, validates existing protected envelopes, and preserves all canonical row identity, status, linkage, timing, error, and verification fields. Empty or unclassifiable legacy evidence aborts startup with the exact webhook-log ID. Evidence is not guessed, discarded, or silently skipped.

## Protected Artifacts

| Artifact | Durable decision |
| --- | --- |
| Webhook JSON | Remove embedded raw `messageSignature`, then encrypt the remaining complete JSON evidence in the Fawry webhook envelope. |
| Message signature | Store only a SHA-256 digest; never durably store the raw value. |
| Merchant secure key / webhook secret / API key | Remain only in the existing encrypted tenant payment-method configuration and bounded request memory; no new durable copy is created. |
| Provider/customer fields and tokens inside webhook evidence | Protected by the encrypted payload envelope and exposed only by explicit in-memory unprotection. |
| Checkout/session material | No sensitive reusable checkout session material was identified in the certified Fawry reference-code flow. Recovery uses canonical Fawry reference codes and payment-intent identity, so this closure creates no new session envelope or application-service dependency. |
| Replay snapshots | `FawryWebhookReplayRecords` contain canonical IDs, evidence hashes, references, correlation, classification, and timestamps only; no raw payload, signature, key, token, or session artifact is copied. |

## Replay Preservation

Replay and duplicate authority remain unchanged:

- tenant plus provider event ID remains the durable `PaymentWebhookLogs` duplicate fence;
- `FawryWebhookReplayRecords` retain the same canonical prior-evidence fields;
- processing state, signature-verification state, provider event type, intent/order linkage, and timestamps are unchanged;
- payload protection is not used to derive replay identity;
- no Runtime prior-history contract or replay architecture changed.

## Recovery Preservation

Fawry payment recovery remains based on current SQL `PaymentIntent` authority, the original provider/reference identity, and the existing application/Runtime/lifecycle path. It does not reconstruct a payment session from `PaymentWebhookLog.RawPayload`.

Durable webhook audit evidence remains recoverable by unprotecting the payload envelope in memory. The removed raw signature is not needed after the persisted `SignatureVerified` admission result; its digest remains available for correlation. If the Data Protection key ring is missing, an envelope is corrupt, or legacy JSON cannot be safely classified, startup or unprotection fails closed. Key restoration or manual reconciliation is required; the implementation does not fall back to plaintext or discard legacy evidence.

## Security Review

- Plaintext Fawry webhook JSON is absent from every new controller persistence path.
- Raw message signatures are absent from both the signature column and encrypted payload plaintext.
- Data Protection purpose isolation prevents a non-Fawry or differently purposed envelope from being accepted.
- Protection and backfill exceptions identify only the durable row/artifact class; payload, signature, secret, token, and session values are not included.
- Existing Fawry telemetry and controller logs continue to use canonical identifiers and reason/status values; this closure adds no sensitive logging.
- Data Protection key-ring availability is an operational prerequisite for audit recovery, matching the certified Paymob strategy.

## Architecture Preservation

Unchanged by this closure:

- `PointOfSale.Core/Payments/Runtime/**`;
- `FawryRuntimeBridge`;
- `FawryRuntimeApplicationService`;
- `FawryPaymentAdapter` and provider protocol;
- `FawryWebhookEvidenceAdapter` and signature/admission protocol;
- business rules and payment lifecycle semantics;
- controller routes, request/response contracts, and frontend;
- CommerceHub;
- database entities, EF mappings, migrations, tables, indexes, and replay architecture.

Changes are limited to a Fawry-specific persistence protector, startup backfill, dependency registration, minimal controller persistence redirection, focused tests, and this certification record. `PAYMENT_REFERENCE_FREEZE` remains the governing authority.

## Regression Summary

Validation executed on 2026-07-15:

| Validation | Result |
| --- | ---: |
| Closure 1 tests | **5 passed, 0 failed** |
| Closure 1 plus prior Sandbox-validation certification tests | **11 passed, 0 failed** |
| Applicable Fawry regression excluding two pre-existing failing classes | **90 passed, 0 failed** |
| Frozen Payment Runtime regression | **94 passed, 0 failed** |
| API build | **Passed; 0 warnings, 0 errors** |
| Test project build | **Passed; 0 warnings, 0 errors** |

The complete Fawry-named aggregate produced **91 passed and 7 unrelated existing failures**. Four `FawryWebhookReliabilityTests` fail during test setup because the local PostgreSQL test schema lacks `Orders.BillingAddressAdmissionEvidenceId`; the controller and closure assertions are not reached. Three `FawryPaymentAuthorityCertificationTests` are stale source/certification-text assertions expecting earlier implementation markers (`ReferenceCode = referenceCode`, `GetString(root, "fawryRefNumber")`, and `PASS SF-PAY7`). These failures are outside durable artifact protection and were not altered to manufacture a green aggregate.

## Certification Decision

The High finding from `FAWRY_PRODUCTION_2_SANDBOX_VALIDATION` is closed at source level. Fawry durable webhook payload evidence is protected, raw signature persistence is removed, legacy records are upgraded fail closed, and replay, duplicate detection, recovery, audit, ordering, Runtime authority, and the single lifecycle writer remain unchanged.

This closure does not certify a deployment, Data Protection key-ring operation in a named environment, public ingress, live Fawry Sandbox evidence, telemetry execution, CommerceHub, or active advanced-operation commands. Those retain their prior decisions.

## Success Answers

| Question | Answer |
| --- | --- |
| High finding closed? | **Yes - the durable Fawry webhook artifact finding only.** |
| Sensitive artifacts protected? | **Yes. Durable webhook/customer material is encrypted; raw security material is digested, omitted, or remains transient.** |
| Webhook payload protected? | **Yes. Signature-scrubbed JSON evidence is stored only in the versioned encrypted envelope.** |
| Raw signature removed? | **Yes. Only a SHA-256 digest remains durably; the raw field is removed before payload encryption.** |
| Replay preserved? | **Yes. Canonical replay identities and prior-evidence records are unchanged.** |
| Recovery preserved? | **Yes. Reference/intent recovery is unchanged and protected audit evidence remains recoverable in memory.** |
| Audit preserved? | **Yes. Payload evidence remains encrypted, while verification state and the signature digest remain queryable.** |
| Runtime preserved? | **Yes. The frozen Payment Runtime is unchanged.** |
| Architecture preserved? | **Yes. No lifecycle, protocol, replay, controller-contract, frontend, CommerceHub, or storage redesign was introduced.** |
| Regression passed? | **Yes for Closure 1, the applicable Fawry slice, and the frozen Runtime. Seven unrelated existing aggregate failures are documented separately.** |
| Recommend FAWRY-FINAL? | **Yes for source final re-certification. Deployment, telemetry execution, and live Sandbox evidence remain independently pending.** |

**FINAL DECISION: THE FAWRY DURABLE WEBHOOK ARTIFACT HIGH FINDING IS CLOSED AT SOURCE LEVEL.**

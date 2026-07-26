# FAWRY Certification Closure 7 - Provider Error Redaction

Date: 2026-07-15
Scope: `FAWRY-FINAL-006` only
Payment Runtime posture: frozen and unchanged
Decision: **HIGH FINDING CLOSED**

## Root Cause

`FAWRY-FINAL-006` was valid.

The active Fawry create-reference adapter read up to 512 characters of a
non-success HTTP response body and passed that body into canonical provider-error
metadata as `provider_diagnostic`. The previous helper replaced selected field
names such as `signature`, `secureKey`, and `apiKey`, but it did not remove the
corresponding values. A response like `{"signature":"secret"}` could therefore
become `{"redacted":"secret"}` and cross the Provider Adapter boundary through
`CanonicalProviderError` and rejected `ProviderEvidence`.

This violated `PAYMENT_REFERENCE_FREEZE`, which requires provider errors to
cross adapter boundaries only as canonical redacted errors and forbids raw
provider response bodies, signatures, secure keys, API keys, authorization
values, tokens, session material, sensitive provider metadata, and internal
provider diagnostics.

## Current Implementation Review

The reviewed active path remains:

```text
FawryRuntimeApplicationService
  -> FawryRuntimeBridge
  -> FawryPaymentAdapter
  -> CanonicalProviderError or ProviderEvidence
  -> PaymentRuntimeOrchestrator
```

Official Fawry reference-number documentation requires a `signature` generated
from the request fields plus `secureKey`, and documents provider status/error
codes such as `9901` and `9946`. Fawry notification/status documentation also
defines signed provider identifiers and `messageSignature`. Those contracts make
provider bodies unsafe as generic diagnostics unless parsed through an
allow-list. This closure does not need raw HTTP response bodies to preserve the
permitted canonical output.

Official references reviewed:

- https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis
- https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/server-notification-v2
- https://developer.fawrystaging.com/docs/sdks/payment-notifications/get-payment-status-v2

## Implemented Correction

The correction is bounded to the active Fawry payment adapter HTTP error
translation:

- `FawryPaymentAdapter` no longer reads the non-success HTTP response body.
- `FawryProviderErrorTranslator.FromHttp` now accepts only the HTTP status code.
- `provider_diagnostic` is a fixed sanitized message:
  `Fawry returned a non-success HTTP status. Provider response body was discarded.`
- `http_status`, provider name, canonical error code, canonical category, and
  canonical message are preserved.
- The old raw-body redaction helper was removed from `FawryPaymentAdapter`.

Unchanged:

- Payment Runtime
- Runtime Bridge
- lifecycle service
- controllers
- frontend
- persistence and migrations
- checkpoint logic
- credential authority
- business rules
- webhook processing
- advanced-operation activation status

## Redaction Policy

Permitted across the active adapter boundary:

- `CanonicalProviderError`
- provider error code derived from HTTP status, such as `FAWRY_HTTP_401`
- canonical category, such as `Authentication` or `Validation`
- correlation identifiers already carried by the canonical action/evidence path
- HTTP status as non-sensitive transport classification
- fixed sanitized diagnostic message

Forbidden across the active adapter boundary:

- raw provider response body
- merchant secure key
- provider signatures
- API keys
- tokens
- authorization values
- session material
- secret identifiers
- sensitive provider metadata
- internal provider diagnostics

If a non-success HTTP response cannot be safely translated, the adapter now
fails closed to the canonical HTTP provider failure and discards provider
content.

## Regression

Focused certification tests:

| Test slice | Result |
|---|---:|
| `FawryProviderAdapterCertificationTests` + `FawryCertificationClosure7Tests` | **9 passed / 0 failed** |

Command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~FawryProviderAdapterCertificationTests|FullyQualifiedName~FawryCertificationClosure7Tests" -nologo -v minimal --no-restore
```

The test run also compiled `PointOfSale.Core`, `PointOfSale.Infrastructure`,
`PointOfSale.API`, and `PointOfSale.Tests`. Existing repository warnings remain
unrelated to this closure.

Focused coverage proves:

- raw HTTP provider response values are absent from `CanonicalProviderError`;
- signatures, secure keys, API keys, tokens, authorization values, and provider
  diagnostics are absent;
- provider HTTP error code and canonical category are preserved;
- rejected provider evidence preserves canonical error and correlation metadata
  without raw provider values;
- the active adapter source no longer contains the raw-body read/redaction path.

## Certification Decision

| Question | Decision |
|---|---|
| Was the Board finding valid? | **Yes.** |
| Sensitive provider values blocked? | **Yes.** |
| Raw response removed? | **Yes. Non-success HTTP response bodies are discarded.** |
| `CanonicalProviderError` preserved? | **Yes.** |
| Provider code preserved? | **Yes. HTTP-derived codes such as `FAWRY_HTTP_401` remain.** |
| Canonical category preserved? | **Yes.** |
| Correlation identifiers preserved? | **Yes, through rejected canonical evidence.** |
| Logging redacted? | **Yes for this adapter boundary; `FawryPaymentAdapter` has no logger and emits no raw provider body.** |
| Telemetry redacted? | **Yes for this adapter boundary; no telemetry path receives the raw body.** |
| Runtime preserved? | **Yes.** |
| Architecture preserved? | **Yes.** |
| Regression passed? | **Yes for the focused Closure 7 and affected adapter slice.** |

## Closure Recommendation

`FAWRY-FINAL-006` is closed.

Recommend **FINAL RECERTIFICATION** for the current Fawry software record after
the certification board rereads current source and the complete Fawry closure
set. This closure does not certify deployment, sandbox execution, live
operation, migration state, CommerceHub, or any finding other than
`FAWRY-FINAL-006`.

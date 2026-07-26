# FAWRY-4 Webhook Evidence Adapter Certification

Date: 2026-07-15  
Scope: Fawry callback/webhook evidence adapter only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and permanently frozen

## Decision

FAWRY-4 implements only the Fawry Webhook / Callback Evidence Adapter.

The adapter authenticates official Fawry Server Notification V2 callback/webhook payloads, validates their structure, maps supported provider status events to canonical `ProviderEvidence`, translates invalid input and provider-negative facts to `CanonicalProviderError`, and exposes replay/duplicate detection hooks.

It does not integrate with Runtime, execute lifecycle mutation, write SQL, own replay persistence, own duplicate persistence, resolve credentials, use configuration, register controllers, or implement business rules.

## Callback Flow

```text
Official Fawry merchant callback POST body
  -> FawryWebhookEvidenceAdapter
  -> JSON payload parsing
  -> official messageSignature SHA-256 verification
  -> optional paymentTime tolerance check when paymentTime exists
  -> application-supplied replay hook
  -> application-supplied duplicate hook
  -> supported orderStatus classification
  -> canonical ProviderEvidence or rejected ProviderEvidence
```

## Webhook Flow

```text
Official Fawry per-order webhook/notification body
  -> same FawryWebhookEvidenceAdapter boundary
  -> same official signature verification
  -> same evidence translation
```

`FawryWebhookEvidenceKind` records whether the ingress was treated as merchant `Callback` or per-order `Webhook`. The signature and evidence model is the same because the official payload contract is the same Server Notification V2 body.

## Signature Model

Official Fawry Server Notification V2 signs:

```text
SHA-256(
  fawryRefNumber
  + merchantRefNumber
  + paymentAmount formatted 0.00
  + orderAmount formatted 0.00
  + orderStatus
  + paymentMethod
  + paymentReferenceNumber when present
  + secureKey)
```

The adapter recomputes this value and compares it with `messageSignature` using fixed-time byte comparison. The secure key is an explicit method input in the request object. The adapter performs no credential lookup and uses no `IConfiguration` or `IOptions`.

## Replay Strategy

The adapter exposes `FawryWebhookDetectionHook` for replay detection.

The hook receives safe canonical identifiers only:

- provider;
- event type;
- request id;
- provider reference;
- evidence reference;
- payload hash;
- occurred timestamp when present.

The adapter owns no replay store. Durable replay lookup, reservation, uniqueness, completion, and transaction boundaries remain application authority in later phases.

## Duplicate Strategy

The adapter exposes the same hook shape for duplicate detection.

Duplicate candidates return rejected `ProviderEvidence` and a `CanonicalProviderError`. The adapter does not persist duplicate state and does not collapse lifecycle outcomes.

## Evidence Mapping

Supported Fawry `orderStatus` values:

| Fawry status | Canonical metadata status |
|---|---|
| `NEW` | `AwaitingCustomer` |
| `PAID` | `Completed` |
| `CAPTURED` | `Completed` |
| `AUTHORIZED` | `Authorized` |
| `CANCELED`, `CANCELLED` | `Cancelled` |
| `REFUNDED` | `Refunded` |
| `EXPIRED` | `Expired` |
| `PARTIAL_REFUNDED`, `PARTIALLY_REFUNDED` | `PartiallyRefunded` |
| `FAILED` | `Failed` plus `CanonicalProviderError.ProviderRejected` |

Canonical `ProviderEvidence` fields:

| Provider fact | Canonical mapping |
|---|---|
| `requestId` | event id metadata and evidence reference suffix |
| `fawryRefNumber` | provider reference `fawry:order:{fawryRefNumber}` |
| `merchantRefNumber` | merchant reference metadata |
| `paymentReferenceNumber` / `paymentRefrenceNumber` | payment reference metadata and signature input |
| `orderStatus` | event type `ORDER_{status}`, order status metadata, canonical status metadata |
| `paymentMethod` | payment method metadata |
| `paymentAmount`, `orderAmount` | amount metadata formatted to two decimals |
| `paymentTime` when present | evidence timestamp and optional tolerance input |
| raw payload | SHA-256 payload hash only |

Raw payloads, raw signatures, secure keys, and authentication material are not copied into evidence metadata.

## Failure Flow

```text
missing secret / malformed payload / invalid signature / stale timestamp
  -> CanonicalProviderError
  -> rejected ProviderEvidence
```

```text
unsupported orderStatus
  -> CanonicalProviderError.Validation
  -> rejected ProviderEvidence
```

```text
replay or duplicate hook rejection
  -> CanonicalProviderError.Validation
  -> rejected ProviderEvidence
```

```text
official FAILED status
  -> recorded ProviderEvidence
  -> CanonicalProviderError.ProviderRejected attached to result and metadata
```

Rejected evidence is a candidate artifact for later application handling only. The adapter never submits evidence to Runtime and never determines final payment state.

## Architecture Preservation

Preserved boundaries:

| Boundary | FAWRY-4 decision |
|---|---|
| Payment Runtime | Unchanged. |
| Runtime Bridge | Unchanged. |
| Runtime execution | Not invoked. |
| Application services | Not modified. |
| Controllers / frontend | Not modified. |
| Credential lookup / configuration lookup | Not implemented. Secure key is explicit input. |
| SQL / persistence / replay store | Not implemented. |
| Lifecycle writer | Not referenced or invoked. |
| Business rules | Not implemented. |
| CommerceHub | Not modified. |

Runtime independence guards verify `PointOfSale.Core/Payments/Runtime` remains free of Fawry provider-specific terms.

## Certification Evidence

Focused tests in `PointOfSale.Tests/Architecture/FawryWebhookEvidenceAdapterCertificationTests.cs` cover:

| Evidence | Coverage |
|---|---|
| Official signature verification | SHA-256 Fawry `messageSignature` construction and invalid signature rejection. |
| Callback verification | Callback ingress maps to canonical evidence. |
| Webhook verification | Webhook ingress maps to canonical evidence. |
| Malformed payload rejection | Invalid JSON returns rejected evidence and canonical validation error. |
| Unsupported event rejection | Unknown `orderStatus` fails closed without guessed canonical status. |
| Timestamp validation | `paymentTime` tolerance check when official timestamp is present. |
| Replay hook | Application-supplied hook can reject without adapter persistence. |
| Duplicate hook | Application-supplied hook can reject without adapter persistence. |
| ProviderEvidence mapping | Stable evidence id/hash, references, metadata, and redaction. |
| CanonicalProviderError mapping | Invalid input and provider `FAILED` facts map to canonical errors. |
| Architecture isolation | No Runtime dependency, SQL, persistence, configuration, logging, controller, Stripe, or Paymob coupling. |

## Certification Decision

| Question | Decision |
|---|---|
| Webhook adapter implemented? | Yes. |
| Official signature verification certified? | Yes, source and focused tests. |
| Callback verification certified? | Yes. |
| Webhook verification certified? | Yes. |
| ProviderEvidence mapping certified? | Yes, evidence-only. |
| Replay hook certified? | Yes, hook-only with no store. |
| Duplicate hook certified? | Yes, hook-only with no store. |
| CanonicalProviderError certified? | Yes. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Webhook adapter certified? | Yes for evidence-adapter scope after focused tests pass. |
| Recommend next phase? | Yes. Proceed to FAWRY-5 advanced operations translation-only certification, or separately certify application webhook contract/replay persistence when activation is required. |

No `.ai-context` file was modified because no new permanent cross-provider architectural truth was discovered.

## Explicit Exclusions

FAWRY-4 does not certify or implement:

- Runtime integration;
- application webhook admission;
- replay persistence;
- duplicate persistence;
- checkpoint persistence;
- SQL or EF Core artifacts;
- lifecycle mutation;
- controllers or public HTTP ingress;
- frontend behavior;
- credential lookup or tenant configuration resolution;
- business rules;
- live Sandbox validation or deployment readiness.

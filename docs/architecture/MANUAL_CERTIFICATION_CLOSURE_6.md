# Manual Certification Closure 6

## Executive Summary

Manual Payment Closure 6 addresses only MANUAL-FINAL-006.

The correction establishes `IManualPaymentDurableEvidenceAuthority` as the
canonical Manual Payment durable evidence authority. Manual Payment recovery,
replay, audit, operator review, and operational continuity now validate the
durable receipt evidence envelope before treating a Manual Wallet intent as
executable.

## Board Finding

MANUAL-FINAL-006 was valid.

The Manual Payment evidence adapter produced canonical evidence identity,
hash, status, timestamp, instruction version, proof metadata, and admission
facts, but the durable payment intent did not preserve one authoritative
evidence envelope for later recovery, replay, audit, and review decisions.

## Root Cause

Canonical receipt evidence existed only inside the application execution path.
Later code could reason from selected durable fields such as proof key,
provider metadata, payment reference, and status without one authority defining
which evidence facts were sufficient and conflict-free.

## Implemented Correction

`ManualPaymentDurableEvidenceAuthority` now:

- writes a minimized durable receipt evidence envelope to the existing durable
  `PaymentIntent.ProviderMetadata` field after canonical evidence admission;
- defines the required receipt evidence facts;
- validates evidence completeness and consistency for replay, recovery, audit,
  operational continuity, and operator review;
- rejects missing, malformed, incomplete, conflicting, or unsupported evidence;
- preserves the frozen Runtime, Runtime Bridge, Provider Adapter, and Evidence
  Adapter protocol.

No migration, SQL redesign, Runtime change, adapter change, telemetry change,
retention redesign, or recovery redesign was introduced.

## Durable Evidence Authority

The certified flow is:

```text
Manual Payment receipt evidence
  -> ManualPaymentEvidenceApplicationService
  -> canonical ProviderEvidence
  -> ManualPaymentDurableEvidenceAuthority
  -> durable PaymentIntent.ProviderMetadata envelope
  -> replay / recovery / review validation
  -> existing lifecycle or recovery authority
```

The durable envelope includes tenant, order, payment intent, payment reference,
amount, currency, channel, evidence id, evidence hash, evidence reference,
payload reference, evidence type, evidence status, submitted time, instruction
version, proof identifier, proof source, proof metadata version, and proof
metadata.

## Evidence Completeness

The authority fails closed when:

- durable evidence is missing;
- durable evidence JSON is malformed;
- evidence type is not `Receipt`;
- evidence status is not `PendingReview`;
- evidence id or hash is missing or invalid;
- tenant, order, payment intent, payment reference, amount, or currency
  conflicts with current SQL authority;
- proof identifier conflicts with the durable proof key;
- instruction version, proof source, proof metadata version, submitted time, or
  required proof metadata is missing;
- the requested evidence purpose is unsupported.

## Regression

Focused certification coverage was added in
`ManualPaymentCertificationClosure6Tests`.

Coverage includes:

- required durable evidence accepted;
- missing durable evidence rejected;
- conflicting durable evidence rejected;
- unsupported evidence type rejected;
- replay validation through the durable evidence authority;
- recovery validation through the durable evidence authority;
- operator review validation through the durable evidence authority;
- architecture guards proving no Runtime, Runtime Bridge, Provider Adapter,
  Evidence Adapter, or controller-owned evidence logic was introduced.

Focused regression executed:

```text
dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore -nologo -v minimal -clp:ErrorsOnly
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~ManualPaymentCertificationClosure6Tests|FullyQualifiedName~ManualPaymentCertificationClosure4Tests" -nologo -v minimal
```

Observed result:

- test project build: succeeded, 0 errors;
- focused Closure 6 + Closure 4 tests: 10 passed, 0 failed, 0 skipped.

## Certification Decision

MANUAL-FINAL-006 is closed.

Runtime remains provider-neutral and frozen. Runtime Bridge, Provider Adapter,
Evidence Adapter protocol, Recovery Authority from Closure 4, retention
authority from Closure 5, telemetry, migration, sandbox, production, webhook,
callback, lifecycle redesign, SQL redesign, MANUAL-FINAL-007, and
MANUAL-FINAL-009 remain unchanged and uncertified by this closure.

# MANUAL-2 Provider Adapter

Date: 2026-07-15

Scope: Manual Payment provider adapter translation only.

Status: CERTIFIED - PROVIDER ADAPTER ONLY

## Provider Model

Manual Payment remains a first-class reference provider with provider code
`manual_payment`. MANUAL-2 implements only the canonical provider adapter
translation boundary.

The adapter owns:

- canonical Manual Payment request validation and normalization;
- Manual provider request construction;
- pending Manual provider evidence construction;
- canonical `ProviderEvidence` translation;
- unsupported-capability rejection evidence;
- provider-neutral `CanonicalProviderError` translation for malformed adapter input.

The adapter does not own proof upload, approval, review, rejection workflow,
admin behavior, persistence, Runtime bridge execution, Runtime orchestration,
controller behavior, lifecycle mutation, webhook, callback, recovery, replay, or
checkpoint behavior.

## Canonical Request

The canonical Manual Payment request is `ManualPaymentCanonicalRequest`.

Certified fields:

- trusted tenant id;
- payment intent id;
- canonical action id and correlation id;
- amount and currency;
- customer reference/contact facts;
- reference-number policy;
- proof-required policy;
- requested Manual Payment channel;
- supported channel configuration supplied by the application boundary;
- instruction configuration and instruction version;
- expiration policy;
- evidence timestamp.

The adapter does not resolve tenant configuration. It only consumes the already
admitted configuration facts supplied in the request.

## Canonical Evidence

Manual provider evidence is `ManualPaymentProviderEvidence` and translates to
canonical `ProviderEvidence`.

Certified evidence fields:

- pending Manual Payment provider status;
- instruction identifier;
- reference required flag;
- proof required flag;
- expiration timestamp;
- channel;
- instruction version;
- evidence timestamp;
- provider status;
- canonical status.

Pending Manual Payment maps to canonical `ProviderEvidence` with:

- source `manual_payment`;
- evidence type `Receipt`;
- evidence status `Recorded`;
- canonical status metadata `AwaitingCustomer`.

Rejected adapter evidence maps to canonical `ProviderEvidence` with:

- evidence status `Rejected`;
- canonical status metadata `Rejected`;
- provider error code and reason metadata.

## Translation

Certified translation path:

```text
ManualPaymentCanonicalRequest
  -> ManualPaymentProviderRequest
  -> ManualPaymentProviderEvidence
  -> canonical ProviderEvidence
```

Provider failures translate to `CanonicalProviderError` before leaving the
adapter boundary. Unsupported capabilities translate to rejected canonical
`ProviderEvidence` and do not execute a workflow.

## Capability Matrix

| Capability | MANUAL-2 status |
|---|---|
| Request mapping | Certified |
| Instruction mapping | Certified |
| Channel mapping | Certified |
| Expiration mapping | Certified |
| Pending provider evidence mapping | Certified |
| Rejected provider evidence mapping | Certified |
| Canonical provider error mapping | Certified |
| Runtime bridge | Not implemented |
| Proof upload | Not implemented |
| Approval workflow | Not implemented |
| Review workflow | Not implemented |
| Persistence | Not implemented |
| Controller changes | Not implemented |
| SQL changes | Not implemented |

## Regression

Fail-closed behavior is certified for:

- unknown channel;
- missing instructions;
- missing reference policy;
- missing or invalid expiration policy;
- unsupported capability;
- malformed canonical lineage.

Architecture guards prove the adapter source has no HTTP client, configuration
resolver, options binding, EF Core, SQL execution, lifecycle service, Runtime
orchestrator, controller, proof upload, approval, or review responsibility.

## Certification

Focused certification tests were added in:

```text
PointOfSale.Tests/Architecture/ManualPaymentProviderAdapterCertificationTests.cs
```

Certification command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~ManualPaymentProviderAdapterCertificationTests -nologo -v minimal --no-restore
```

## Decision

| Question | Answer |
|---|---|
| Provider adapter implemented? | Yes, translation only. |
| Canonical request mapping certified? | Yes. |
| Provider evidence mapping certified? | Yes. |
| Instruction mapping certified? | Yes. |
| Channel mapping certified? | Yes. |
| CanonicalProviderError certified? | Yes. |
| Architecture preserved? | Yes. |
| Runtime preserved? | Yes. No Runtime change. |
| Recommend MANUAL-3? | Yes. Proceed to Runtime Bridge certification only if it remains adapter-to-evidence-to-Runtime orchestration without persistence or lifecycle authority. |

No permanent cross-provider architecture truth was discovered. No `.ai-context`
update is required.

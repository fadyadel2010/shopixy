# FAWRY Certification Closure 6 - One-Shot Credential Handoff

Date: 2026-07-15  
Scope: `FAWRY-FINAL-005` only  
Payment Runtime posture: frozen and unchanged  
Decision: **HIGH FINDING CLOSED**

## Root Cause

`FAWRY-FINAL-005` was valid.

The previous Fawry create path resolved current tenant credentials in
`FawryRuntimeApplicationService`, copied them into `FawryPaymentCredentials`,
and embedded that credential record directly in `FawryExecutionRequest`.
`FawryRuntimeBridge.ExecuteAsync` then read `request.Credentials` on every
invocation and passed the same record to `IFawryPaymentAdapter`.

That made the request object reusable. A second bridge invocation, including a
concurrent invocation of the same request, could reuse the same merchant code,
API key, and secure key. There was no consume operation, no consumed state, no
clear-after-consume behavior, no tenant/action correlation check, and no stale
request rejection.

This violated `PAYMENT_REFERENCE_FREEZE`, which requires tenant credential
handoff to be operation-bound, single-use, overlap-safe, consumed before
provider I/O, cleared after consumption or failure, unavailable to Runtime, and
non-reusable by later operations.

## Current Implementation Review

The reviewed execution path remains:

```text
FawryRuntimeApplicationService
  -> current tenant credential resolution
  -> FawryExecutionFactory.CreateRequest
  -> FawryRuntimeBridge
  -> IFawryPaymentAdapter.InitializePaymentAsync
  -> PaymentRuntimeOrchestrator
```

Official Fawry reference-number payment documentation requires provider
credential material at the provider call boundary: `merchantCode` is required,
and `signature` is the SHA-256 digest of the documented request fields plus the
merchant secure key. Therefore credential authority must remain in the
application handoff and must not move into Runtime or durable evidence.

## Implemented Correction

The correction is bounded to application credential handoff and bridge
credential admission:

- `FawryExecutionRequest` no longer exposes reusable
  `FawryPaymentCredentials`.
- `FawryExecutionRequest` now carries `FawryCredentialHandoff`.
- `FawryExecutionFactory.CreateRequest` creates the handoff after the
  application has built the admitted action and session.
- `FawryRuntimeBridge` consumes the handoff once immediately before adapter
  entry.
- `FawryCredentialHandoff` validates tenant identity, action identity, expiry,
  and credential presence.
- Consumption uses `Interlocked.CompareExchange` and clears the credential
  reference with `Interlocked.Exchange(ref _credentials, null)`.
- Missing, reused, concurrent, stale, operation-mismatched, or tenant-mismatched
  handoff states become canonical configuration failures and do not call the
  provider adapter.

Unchanged:

- Payment Runtime
- `PaymentRuntimeOrchestrator`
- provider adapter protocol
- webhook protocol
- lifecycle service
- controllers
- frontend
- persistence and migrations
- business rules

## Credential Lifecycle

```text
Fresh tenant resolver output
  -> operation-local FawryPaymentCredentials
  -> FawryCredentialHandoff.Create(tenant, action, credentials, issuedAt)
  -> FawryExecutionRequest.CredentialHandoff
  -> bridge Consume(action, executionTimestamp)
       -> atomic first-consumer admission
       -> clear stored credential reference
       -> tenant/action/stale/missing validation
       -> adapter receives credentials only for this provider call
  -> no credential remains available from the handoff
```

Fail-closed behavior:

| Case | Result |
|---|---|
| Single consume | Adapter receives credentials once. |
| Second consume | Rejected with `FAWRY_CREDENTIAL_HANDOFF_REUSED`; no adapter call. |
| Concurrent consume | One execution consumes; overlapping execution is rejected; no second adapter call. |
| Stale request | Rejected with `FAWRY_CREDENTIAL_HANDOFF_STALE`; no adapter call. |
| Tenant mismatch | Rejected with `FAWRY_CREDENTIAL_HANDOFF_TENANT_MISMATCH`; no adapter call. |
| Missing credentials | Rejected with `FAWRY_CREDENTIAL_HANDOFF_MISSING`; no adapter call. |

Credentials are not supplied to Runtime, not persisted, not projected, and not
available after handoff consumption.

## Regression

Focused certification tests:

| Test slice | Result |
|---|---:|
| `FawryRuntimeBridgeCertificationTests` + `FawryCertificationClosure6Tests` | **13 passed / 0 failed** |

Command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~FawryRuntimeBridgeCertificationTests|FullyQualifiedName~FawryCertificationClosure6Tests" -nologo -v minimal --no-restore
```

The test run also compiled `PointOfSale.Core`, `PointOfSale.Infrastructure`,
`PointOfSale.API`, and `PointOfSale.Tests`. Existing repository warnings remain
unrelated to this closure.

## Certification Decision

| Question | Decision |
|---|---|
| Was the Board finding valid? | **Yes.** |
| One-shot credential handoff certified? | **Yes, for `FAWRY-FINAL-005`.** |
| Reusable request removed? | **Yes. `FawryExecutionRequest` no longer exposes reusable credentials.** |
| Consume-and-clear certified? | **Yes. The handoff atomically consumes and clears credentials before adapter I/O.** |
| Concurrent reuse prevented? | **Yes. Atomic consumption permits only one execution.** |
| Stale reuse prevented? | **Yes. Expired handoff state is rejected before provider execution.** |
| Missing credential rejected? | **Yes.** |
| Tenant mismatch rejected? | **Yes.** |
| Provider execution blocked on failed handoff? | **Yes.** |
| Runtime preserved? | **Yes.** |
| Architecture preserved? | **Yes.** |
| Regression passed? | **Yes for the focused Closure 6 and affected bridge slice.** |

## Closure Recommendation

`FAWRY-FINAL-005` is closed. Proceed to Closure 7 only for the next separately
scoped Fawry finding. This closure does not close `FAWRY-FINAL-006` or any
operational, migration, sandbox, deployment, webhook, lifecycle, controller,
frontend, persistence, or business-rule finding.

# FAWRY-CONTRACT-CLOSURE-1 Application Contract Certification

Date: 2026-07-15  
Scope: Fawry POS Admin + Storefront application execution boundary only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime remains certified, provider-neutral, and permanently frozen

## Decision

FAWRY-CONTRACT-CLOSURE-1 implements the application-owned execution boundary through `IFawryRuntimeApplicationService` and `FawryRuntimeApplicationService`.

The boundary validates its application request, resolves current tenant credentials through the canonical encrypted `TenantPaymentMethods` authority, admits the payment-execution capability and environment, creates canonical Runtime session/action input, delegates exactly once to `IFawryRuntimeBridge`, validates the exact Runtime outcome required for PayAtFawry reference preparation, maps failures, and projects the provider-neutral execution into application artifacts.

No Runtime, Runtime Bridge, provider adapter, webhook adapter, advanced-operations adapter, controller, public DTO, frontend, persistence, SQL schema, CommerceHub, lifecycle service, or business-rule implementation was modified by this closure.

## Application Execution Flow

```text
Storefront / POS Admin application caller
  -> IFawryRuntimeApplicationService.CreatePaymentIntentAsync
       -> application request shape validation
       -> IFawryTenantConfigurationResolver
            -> current tenant-scoped TenantPaymentMethods authority
            -> enabled + non-deleted + online-capable admission
            -> Sandbox/Production compatibility
            -> global execution feature/capability governance
            -> tenant credential decryption and canonical shape validation
            -> no global credential fallback
       -> canonical PaymentSession + PaymentAction construction
       -> one-operation FawryPaymentCredentials construction
       -> IFawryRuntimeBridge.ExecuteAsync
            -> existing IFawryPaymentAdapter
            -> existing PaymentRuntimeOrchestrator
            -> existing FawryExecutionResult
       -> exact reference-preparation Runtime outcome validation
       -> canonical application failure mapping or execution projection
       -> ReferenceCode + ExpiresAtUtc + PaymentUrl + ProviderStatus
```

The expected reference-preparation outcome is intentionally not a financial Runtime success. The provider reference is admitted and verified, the canonical decision is approved, the session transitions to `AwaitingCustomer`, and the transaction stage rejects creation of a financial artifact. This exact fail-closed handoff shape is required; a merely non-null or superficially successful Runtime result is rejected.

## Responsibility Matrix

| Component | Owns | Does not own |
|---|---|---|
| `FawryRuntimeApplicationService` | Request validation, canonical session/action construction, tenant-resolution orchestration, bridge delegation, exact Runtime outcome admission, application error mapping, result projection | Provider protocol, Runtime decisions, lifecycle mutation, persistence, SQL, replay, checkpoints, controllers, frontend, business rules |
| `FawryTenantConfigurationResolver` | Current tenant configuration read, enable/delete/capability/environment gates, credential decryption and shape validation | Provider calls, adapter behavior, Runtime execution, global credential fallback, lifecycle mutation |
| `IFawryRuntimeBridge` | Existing adapter-to-Runtime delegation | Tenant/configuration lookup, public projection, persistence, lifecycle authority |
| `IFawryPaymentAdapter` | Existing Fawry HTTPS protocol and canonical evidence translation | Tenant resolution, application admission, Runtime decisions, persistence |
| Payment Runtime | Existing canonical evidence, decision, transition, transaction, and settlement authority | Fawry configuration, credentials, protocol, DTOs, SQL, lifecycle persistence |

## Tenant Governance

The canonical application configuration authority is the current encrypted `TenantPaymentMethods` row selected by trusted tenant identity and provider code `FAWRY`.

Resolution fails closed for:

| Condition | Application failure code |
|---|---|
| Missing tenant configuration | `FAWRY_CONFIGURATION_MISSING` |
| Deleted configuration | `FAWRY_CONFIGURATION_DELETED` |
| Disabled provider | `FAWRY_PROVIDER_DISABLED` |
| Tenant online capability disabled | `FAWRY_CAPABILITY_DISABLED` |
| Global payment execution/certification capability disabled | `FAWRY_CAPABILITY_DISABLED` |
| Environment validation disabled or Sandbox/Production mismatch | `FAWRY_ENVIRONMENT_MISMATCH` |
| Missing, undecryptable, malformed, or invalid credential shape | `FAWRY_CREDENTIALS_INVALID` |

Global Fawry option credentials are not read by the resolver, compared with tenant credentials, returned, or used as fallback. Global options govern only provider enablement, environment, and certified feature/capability admission.

The validated tenant credential set is copied into a new `FawryPaymentCredentials` object for the single bridge request. Credentials remain outside Runtime artifacts and application result projection.

## Capability Validation

Payment execution requires all of the following:

- Fawry runtime/provider enablement;
- credential validation enabled;
- environment validation enabled;
- official HTTPS API capability;
- SHA-256 signature capability;
- tenant-scoped credential capability;
- matching Sandbox/Production environment capability;
- payment execution feature enabled;
- payment execution certified;
- provider adapter certified;
- tenant payment method enabled, non-deleted, and `SupportsOnline`.

This closure activates only the application contract for payment/reference initialization. It does not activate webhook handling or any advanced operation.

## Failure Mapping

Provider-specific exceptions do not leave the adapter. `CanonicalProviderError` is mapped at the application boundary:

| Canonical provider error | Application failure | Retryable |
|---|---|---:|
| Authentication | `FAWRY_PROVIDER_AUTHENTICATION_FAILED` | No |
| Authorization | `FAWRY_PROVIDER_AUTHORIZATION_FAILED` | No |
| Rate limited | `FAWRY_PROVIDER_RATE_LIMITED` | Yes |
| Network / provider unavailable | `FAWRY_PROVIDER_UNAVAILABLE` | Yes |
| Validation | `FAWRY_PROVIDER_VALIDATION_FAILED` | No |
| Not supported | `FAWRY_PROVIDER_OPERATION_UNSUPPORTED` | No |
| Other provider rejection/state | `FAWRY_PROVIDER_REJECTED` | No |
| Unexpected Runtime outcome | `FAWRY_RUNTIME_OUTCOME_REJECTED` | No |
| Missing/malformed projection artifact | `FAWRY_EXECUTION_PROJECTION_INVALID` | No |

The application failure exposes the stable application code, failure category, retryability, and redacted provider code. Tenant credentials and signatures are not projected.

## Execution Delegation And Projection

The application service creates deterministic session, attempt, and action identities from tenant, order, local payment-intent, and idempotency authority. It submits `PaymentActionType.ReferencePayment` with provider-neutral metadata to the existing bridge.

Successful reference preparation projects only:

- Fawry reference code;
- expiration timestamp when returned;
- provider payment URL when returned;
- provider status;
- canonical `ProviderEvidence`;
- unchanged `RuntimeResult`;
- unchanged `FawryExecutionResult`.

Existing public controllers and DTOs were not changed. The projected names `ReferenceCode` and `ExpiresAtUtc` preserve the existing Storefront handoff vocabulary without coupling the application boundary to controller DTO types.

## Architecture Preservation

| Boundary | Decision |
|---|---|
| Payment Runtime | Unchanged. |
| Runtime Bridge | Unchanged. |
| Provider Adapter | Unchanged. |
| Webhook Evidence Adapter | Unchanged and not invoked. |
| Advanced Operations Adapter | Unchanged and not activated. |
| Controllers | Unchanged. |
| DTOs | Unchanged. |
| Frontend | Unchanged. |
| Persistence / SQL | No entity, DbContext model, query mutation, migration, or SQL change. The resolver performs only the authorized current configuration read. |
| Business rules | Unchanged. |
| Lifecycle mutation | Not implemented. |
| CommerceHub | Unchanged and excluded. |
| `.ai-context` | Unchanged; no new permanent cross-provider architectural truth was discovered. |

No Architecture Exception was required.

## Certification Evidence

Focused tests in `PointOfSale.Tests/Architecture/FawryApplicationContractCertificationTests.cs` cover:

| Evidence | Coverage |
|---|---|
| Application boundary | Resolver-to-bridge execution and exact result projection |
| Tenant resolution | Current tenant row and encrypted credential resolution |
| Fail Closed governance | Missing, disabled, deleted, capability-disabled, environment-mismatched, and invalid credential cases |
| No fallback | Tenant credentials remain authoritative even when global credentials differ |
| Capability validation | Execution stops before bridge delegation |
| Failure mapping | Canonical provider error to stable application failure |
| Execution delegation | One adapter/bridge invocation with exact tenant credentials |
| Runtime outcome admission | Exact `AwaitingCustomer` reference-preparation outcome |
| Backward compatibility | Existing handoff vocabulary retained; controller/DTO independence |
| Architecture isolation | No Runtime/provider/bridge/lifecycle/persistence/CommerceHub modification or dependency inversion |

Verification executed for this closure:

| Command | Result |
|---|---:|
| `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~FawryApplicationContractCertificationTests" -nologo -v minimal --no-restore` | 16 passed, 0 failed, 0 skipped |
| Combined FAWRY-1 through FAWRY-5, application-contract, and frozen Runtime final-certification slice | 77 passed, 0 failed, 0 skipped |
| `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal --no-restore` | Passed, 0 errors; existing repository warnings remain |

The broader legacy `FawryPaymentAuthorityCertificationTests` slice is not green: 2 passed and 2 failed. One stale source assertion expects the pre-FAWRY-2 `BuildDeterministicReferenceCode(intent)` implementation detail, while the certified FAWRY-2 adapter now uses `FawryMerchantReference.Build`. The other requires a `PASS SF-PAY7` marker in permanent `.ai-context` files even though the governing update rule and this mission prohibit manufacturing a new permanent truth. Neither failure is introduced or hidden by this application closure, but an unqualified repository-wide Fawry regression decision remains withheld until those historical assertions are reconciled in their owning scope.

## Certification Decision

| Question | Decision |
|---|---|
| Application Contract implemented? | **Yes.** |
| DTO compatibility preserved? | **Yes.** Existing DTOs are unchanged and projection preserves `ReferenceCode` / `ExpiresAtUtc` vocabulary. |
| Tenant Credential Resolution certified? | **Yes, for the current tenant-scoped payment-execution boundary.** |
| Capability validation certified? | **Yes.** |
| Failure Mapping certified? | **Yes.** |
| Backward Compatibility preserved? | **Yes for the in-scope DTO, controller, Storefront handoff, bridge, and adapter contracts. Unqualified legacy certification-test compatibility remains withheld because two pre-existing historical assertions are stale.** |
| Runtime Independence preserved? | **Yes.** |
| Architecture boundaries preserved? | **Yes.** |
| Application Contract certified? | **Yes, for Fawry POS Admin + Storefront reference-payment initialization only.** |
| Recommend next phase? | **Yes. Proceed to a separately scoped convergence/activation phase that routes existing Storefront/POS execution through this boundary, while preserving the frozen Runtime and single lifecycle writer.** |

## Explicit Exclusions

This closure does not implement webhook persistence, replay storage, durable checkpoints, SQL mutation, lifecycle mutation, controller changes, DTO changes, frontend changes, business-rule changes, CommerceHub convergence, advanced-operation activation, Sandbox validation, deployment readiness, or live operational certification.

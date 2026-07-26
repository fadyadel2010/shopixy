# MANUAL-1 Foundation Certification

Date: 2026-07-15

Scope: Manual Payment discovery and foundation only.

Status: CERTIFIED - FOUNDATION ONLY

## Executive Summary

MANUAL-1 defines Manual Payment as a first-class backend-owned payment provider
workflow. It is not a shortcut, cash exception, admin bypass, or direct lifecycle
writer.

The implemented foundation adds immutable `ManualPaymentOptions`, feature flags,
environment separation, capability declarations, instruction configuration,
accepted proof type and upload-size policy, supported-channel validation,
expiration policy validation, DI registration, and configuration-only health.

No Runtime behavior, Runtime bridge, evidence adapter, approval workflow, review
workflow, admin action, controller, SQL, persistence, webhook, callback,
recovery, replay, checkpoint, or lifecycle mutation was added by this phase.

Certification decision: **MANUAL PAYMENT FOUNDATION CERTIFIED**.

## Workflow Discovery

Manual Payment is modeled as a workflow provider whose protocol is human and
documentary rather than an external payment API.

Canonical channels discovered for the provider model:

- Bank Transfer
- Vodafone Cash
- Instapay
- Wallet Transfer
- Cash Deposit
- Custom Instructions
- Offline Invoice

Canonical proof and instruction concepts discovered:

- reference number requirement
- customer-facing payment instructions
- receipt or transfer proof upload
- accepted proof MIME types
- maximum proof upload size
- pending-payment expiration policy
- private proof access lifetime

Concept placement:

| Concept | Authority |
|---|---|
| Supported channels | Provider foundation configuration |
| Customer instructions | Provider foundation configuration |
| Accepted proof types | Provider foundation configuration |
| Maximum upload size | Provider foundation configuration |
| Expiration policy | Provider foundation configuration |
| Tenant and payment authority | Application layer, later phase |
| Proof receipt and evidence admission | Evidence adapter, later phase |
| Review, approval, rejection | Application workflow, later phase |
| Runtime execution | Frozen provider-neutral Runtime, later phase |
| Durable payment lifecycle | `PaymentLifecycleService`, later phase |

## Provider Model

Manual Payment is a first-class Reference Provider candidate with provider code
`manual_payment`.

It has no external SDK and no external network dependency at foundation time.
That absence does not remove any architecture layer. Later phases must still
adapt Manual Payment through the same provider/application/evidence/Runtime and
single-writer architecture used by Stripe, Paymob, and Fawry.

Foundation capability is declarative only. The capability model confirms support
for tenant-scoped configuration, sandbox and production environment separation,
configured instructions, receipt proof upload, supported channels, reference
numbers, accepted proof types, and expiration policy. It deliberately does not
certify provider adapter, Runtime bridge, evidence adapter, review, approval,
persistence, or lifecycle execution.

## Architecture

MANUAL-1 preserves `PAYMENT_REFERENCE_FREEZE.md`:

```text
Manual Payment Provider / Workflow
  -> Application-owned authority and orchestration, later phase
  -> Manual Payment translation/evidence boundary, later phase
  -> Canonical ProviderEvidence, later phase
  -> Frozen Payment Runtime, later phase
  -> Exact Runtime outcome admission, later phase
  -> PaymentLifecycleService, later phase
```

Foundation owns only configuration, validation, registration, and health.

Foundation does not own:

- payment execution
- proof upload execution
- review, approval, or rejection
- Runtime bridge invocation
- provider evidence construction
- SQL or EF Core
- controller routes
- admin actions
- webhook or callback handling
- recovery, replay, or checkpoint behavior
- lifecycle mutation

## Configuration

`ManualPaymentOptions` binds from:

```text
CommerceHub:Payments:ManualPayment
```

Certified fields:

- `Enabled`
- `Environment`
- `SandboxMode`
- `FeatureFlags`
- `Capabilities`
- `Instructions`
- `Proof`
- `Expiration`
- `Channels`

Fail-closed validation:

| Condition | Decision |
|---|---|
| Missing enabled-channel configuration when enabled | Reject |
| Disabled provider with foundation feature flag enabled | Reject |
| Unsupported payment channel | Reject |
| Missing customer instructions when enabled | Reject |
| Missing expiration policy when enabled | Reject |
| Missing accepted proof types | Reject |
| Unsupported proof content type | Reject |
| Maximum upload size <= 0 or > 25 MiB | Reject |
| Sandbox environment without `SandboxMode = true` | Reject |
| Production environment with `SandboxMode = true` | Reject |
| Enabled sandbox without sandbox feature flag | Reject |
| Enabled production without production feature flag | Reject |
| Review, approval, rejection, expiration, or refund execution flags without later certification | Reject |

Default `appsettings.json` keeps Manual Payment foundation disabled and inert.

## Capabilities

Certified foundation capabilities:

- tenant-scoped configuration
- sandbox environment
- production environment
- instruction configuration
- receipt proof upload declaration
- Bank Transfer channel declaration
- Vodafone Cash channel declaration
- Instapay channel declaration
- Wallet Transfer channel declaration
- Cash Deposit channel declaration
- Custom Instructions channel declaration
- Offline Invoice channel declaration
- reference number declaration
- expiration policy declaration
- accepted proof type declaration

Not certified in MANUAL-1:

- provider adapter
- Runtime bridge
- evidence adapter
- review workflow
- approval workflow
- rejection workflow
- persistence
- SQL lifecycle integration
- admin workflow
- refund execution
- expiration execution
- proof upload execution

## Health

`ManualPaymentFoundationHealthCheck` is registered as:

```text
manual_payment_foundation
```

Health is configuration-only. It reports safe shape data only: enablement,
environment, sandbox mode, instruction/expiration configured flags, enabled
channel count, accepted proof type count, maximum upload size, provider code,
provider model, external-network requirement, and Runtime execution status.

It does not expose customer instructions, account numbers, wallet numbers,
reference values, proof file names, proof content, credentials, SQL data, or
Runtime artifacts.

## Test Certification

Focused certification tests were created in:

```text
PointOfSale.Tests/Architecture/ManualPaymentFoundationCertificationTests.cs
```

Coverage includes:

- configuration binding
- immutable/sealed foundation model shape
- missing configuration fail-closed behavior
- disabled-provider fail-closed behavior
- environment separation
- unsupported channel rejection
- invalid proof configuration rejection
- capability declaration without runtime/workflow certification
- DI registration
- duplicate DI registration rejection
- configuration-only health check
- health serialization without instructions
- Runtime independence guards
- source guards against Runtime, approval, persistence, and controller behavior

Certification command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~ManualPaymentFoundationCertificationTests -nologo -v minimal --no-restore
```

## Certification Decision

| Question | Answer |
|---|---|
| Manual Payment model defined? | Yes. Backend-owned first-class workflow provider. |
| Foundation implemented? | Yes. |
| Foundation certified? | Yes, foundation only. |
| Configuration certified? | Yes. |
| Capability model certified? | Yes, declarative foundation capabilities only. |
| Environment separation certified? | Yes. |
| Health model certified? | Yes, configuration-only. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Runtime implemented or changed? | No. |
| Payment execution implemented? | No. |
| Approval/review/admin workflow implemented? | No. |
| SQL/persistence/controller changes introduced? | No. |
| Recommend MANUAL-2? | Yes. Begin provider adapter/evidence-boundary design only, still without approval workflow activation until separately certified. |

## Final Verdict

MANUAL-1 is certified as a Manual Payment foundation layer. Manual Payment is now
defined as a full provider workflow that must obey the same frozen architecture
as Stripe, Paymob, and Fawry. MANUAL-1 authorizes only the next certification
phase; it does not authorize runtime execution, approval workflow, persistence,
or live payment processing.

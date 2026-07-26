# Manual Certification Closure 9

## Executive Summary

Manual Payment Closure 9 addresses only MANUAL-FINAL-009.

The correction establishes Migration and Deployment Governance as the canonical
Manual Payment source/deployment convergence authority. Deployment readiness is
now an explicit governed classification and is separated from source,
environment, migration, and production readiness.

## Board Finding

MANUAL-FINAL-009 was valid.

Manual Payment migration evidence did not fully prove that the EF model,
snapshot, migration chain, standalone SQL artifacts, and deployment evidence
were converged. Source implementation was complete after Closures 1-7, but
deployment evidence remained incomplete.

## Root Cause

The repository mixed three evidence classes:

- discoverable EF migrations and model snapshot state;
- standalone SQL operational artifacts; and
- deployment/operator verification tasks.

Those classes were not governed by one classification authority, so source
certification, migration readiness, deployment readiness, environment
readiness, and production readiness could be read as one ambiguous decision.

## Implemented Correction

Migration governance is now explicit:

- `20260419190000_AddCheckoutSessionManualWalletRecovery` is a discoverable
  context-specific EF migration and remains additive.
- `20260420103000_NormalizeManualWalletProofReferences` is a discoverable
  context-specific EF migration and its SQL companion now matches the EF
  migration's row-tenant/path-tenant proof-reference validation.
- `20260419222000_AddPaymentIntentActiveProviderIdempotency.sql` is classified
  as operational evidence only. It now fails closed on duplicate active
  `PaymentIntents`, does not delete business rows, and does not stamp
  `__EFMigrationsHistory` outside the discoverable EF chain.
- The active-intent uniqueness index remains canonical in
  `20260420223926_S12A_AuthorizationWebhookTrust` and the current EF snapshot.
- Focused source tests freeze the migration chain, SQL compatibility,
  snapshot convergence evidence, and readiness classification vocabulary.

No Runtime, Runtime Bridge, Provider Adapter, Evidence Adapter, lifecycle,
recovery, retention, replay, telemetry, or business-rule behavior was changed.

## Canonical Authority

`MANUAL_CERTIFICATION_CLOSURE_9.md` is the canonical Manual Payment Migration
and Deployment Governance Authority.

It governs only source/deployment convergence evidence and readiness
classification. It does not activate Manual Payment, apply migrations to a
target database, enable tenant capabilities, validate object storage, install
alerts, run drills, approve rollback, or certify live operation.

## Verification Review

Reviewed source evidence:

- EF model and `ApplicationDbContextModelSnapshot`;
- discoverable migration classes for Manual Wallet recovery and proof
  normalization;
- S12A active-intent uniqueness migration and current snapshot index;
- standalone SQL companion scripts;
- pending-migration and model-drift command requirements;
- deployment checklist and operational evidence requirements;
- source/deployment convergence classifications.

Required operator verification remains:

```text
dotnet ef migrations list --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext --no-connect
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext
dotnet ef migrations script --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext --idempotent
```

The source classification is closed only when these commands are captured for
the release candidate. Applying the chain and verifying it against each target
database is deployment evidence, not source evidence.

## Readiness Classification

The following categories are separate and may not be merged:

| Category | Closure 9 decision | Boundary |
|---|---|---|
| Source Certification | Closed for MANUAL-FINAL-009 source governance | Discoverable migration chain, source SQL companion safety, snapshot/index evidence, and focused tests are governed |
| Deployment Readiness | Pending | Requires captured release-candidate migration list, pending-model check, idempotent script review, backup/rollback plan, and deployment checklist signoff |
| Environment Readiness | Not established | Requires a named environment with applied migrations, Manual Payment configuration, storage dependency, operators, alerts, and rollback evidence |
| Migration Readiness | Source-ready / target-pending | Source chain is governed; each target database still needs clean/upgrade application and post-apply verification |
| Production Readiness | Not ready | Requires source certification plus deployment, environment, operational evidence, drills, latency, rollback, and approval |

## Deployment Evidence Required

Deployment owners must capture:

- EF migration list and exact last migration;
- clean pending-model check or explicit repository-wide drift disposition;
- idempotent migration script artifact reviewed for the target release;
- preflight duplicate-active-intent report;
- proof-reference normalization report proving row tenant equals path tenant;
- backup/restore rehearsal and rollback decision;
- post-apply `__EFMigrationsHistory`, index, column, and sample data checks;
- Manual Payment configuration/capability state for each tenant;
- private storage, alerts, dashboards, and operator access evidence.

Missing deployment evidence blocks Deployment Readiness without reopening the
source correction.

## Regression

Focused certification coverage was added in
`ManualPaymentCertificationClosure9Tests`.

Coverage includes:

- discoverable Manual Wallet EF migration classes;
- proof-normalization SQL parity with EF tenant/path validation;
- active-intent operational SQL fail-closed behavior and absence of business
  row deletion/history stamping;
- model snapshot evidence for Manual Wallet columns and active-intent index;
- exact readiness category separation;
- architecture guards proving Runtime, Runtime Bridge, Provider Adapter,
  Evidence Adapter, and business-rule behavior were not modified by this
  closure.

## Certification Decision

MANUAL-FINAL-009 is closed as a bounded source migration and deployment
governance finding.

Deployment Readiness remains pending. Environment Readiness is not established.
Migration Readiness is source-ready and target-pending. Production Readiness is
not ready. No sandbox, target deployment, production, live operation, Runtime,
adapter, lifecycle, business-rule, recovery, replay, retention, or telemetry
certification is implied.

# Payment Runtime 1 Foundation

Status: foundation only

## Namespaces

- `PointOfSale.Core.Payments.Runtime.Contracts`: canonical runtime and policy contracts.
- `PointOfSale.Core.Payments.Runtime.Domain`: immutable identifiers and provider-neutral enums.
- `PointOfSale.Core.Payments.Runtime.Results`: provider-neutral result models.
- `PointOfSale.Core.Payments.Runtime.Configuration`: disabled feature flag defaults.
- `PointOfSale.Infrastructure.Payments.Runtime`: inert dependency registration.

## Responsibilities

The foundation defines contracts and value objects only. It does not implement business logic, providers, policies, state transitions, persistence, settlement processing, evidence verification, or lifecycle mutation.

## Dependency Graph

```text
Runtime
  -> Policy
  -> Adapter
  -> Provider
```

Policy decides. Runtime executes. Adapters translate. Providers remain outside the runtime foundation.

## Extension Points

- Add runtime behavior behind `IPaymentRuntime`.
- Add policy behavior behind `IPaymentPolicyEngine`.
- Add future adapters behind `IPaymentProvider`.
- Keep feature flags disabled until a certified implementation phase enables shadow or dual-run execution.

## Contribution Rules

- Do not add provider-specific names, namespaces, states, fields, or transaction writers to runtime contracts.
- Do not reference frontend projects from runtime code.
- Do not add persistence or migrations in the foundation phase.
- Do not implement lifecycle decisions or policy rules in runtime placeholders.
- Keep all new payment execution behavior behind disabled feature flags and certified architecture tests.

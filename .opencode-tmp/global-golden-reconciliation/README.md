# Global Golden Treasury Reconciliation — read-only evidence harness

Deterministic PostgreSQL evidence capture in support of
`docs/reports/GLOBAL_GOLDEN_TREASURY_RECONCILIATION_ARCHITECTURE_2026-09-15.md`.
Harness support only. This tool is not the certifier and performs no business
mutation.

## Guarantees

- `REPEATABLE READ` + `SET TRANSACTION READ ONLY`, verified via
  `current_setting('transaction_read_only')`, always rolled back.
- Every executed statement passes an `IsReadOnlySql` gate (`SELECT`/`WITH`
  only, single statement, no mutating keywords).
- Tenant scope defaults to exactly AhramStore, shopixy, greatone; explicit
  `--tenant` subsets are accepted only from that allow-list.
- Explicit tenant predicates on every business query; no EF filters involved.
- Secrets/PII excluded (payloads, tokens, emails, phones, addresses, party
  personal names, arbitrary free text, and external payment references).
  Tenant names and internal document/source numbers are kept.
- Canonical JSON with capture metadata separated from evidence; SHA-256 over
  deterministic evidence only.

## Commands

```powershell
dotnet build .opencode-tmp/global-golden-reconciliation/GlobalGoldenTreasuryReconciliation.csproj -c Release
dotnet run --project .opencode-tmp/global-golden-reconciliation/GlobalGoldenTreasuryReconciliation.csproj -c Release --no-build -- --self-test
dotnet run --project .opencode-tmp/global-golden-reconciliation/GlobalGoldenTreasuryReconciliation.csproj -c Release --no-build -- --output .opencode-tmp/global-golden-reconciliation/verification-evidence.json --overwrite
dotnet run --project .opencode-tmp/global-golden-reconciliation/GlobalGoldenTreasuryReconciliation.csproj -c Release --no-build -- --output <path> [--overwrite] [--tenant GUID]... [--source-id GUID]...
```

Connection: `ConnectionStrings__DefaultConnection`, falling back to
`PointOfSale.API/appsettings.Development.local.json`. Never printed.

Stdout is a single terse JSON summary:
`{"path":..., "fingerprint":..., "tenantCount":..., "counts":{...}}`.

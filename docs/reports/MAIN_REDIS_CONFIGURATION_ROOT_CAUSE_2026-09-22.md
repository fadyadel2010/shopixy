# MAIN Redis Configuration Root Cause - 2026-09-22

## 1. Scope And Boundary

This investigation was read-only and confined to the MAIN/PRIMARY Shopixy API
running on the DELL-connected development copy at
`C:\Users\fadya\source\repos\PointOfSale`.

No application logic, cart logic, configuration, dependency, Redis data, test,
Kubernetes resource, load-test copy, M4.5 asset, k6 asset, or `tests/load/*` file
was changed or executed. The only repository change is this report.

## 2. Executive Result

The running MAIN API gets its effective Redis settings from the Development
user-secrets store associated with UserSecretsId
`bbdf2e74-8790-4753-8874-f20fcfbdbbd5`:

```text
Redis:Enabled = false
Redis:ConnectionString = 192.168.1.5:6379
```

`appsettings.Development.local.json` contains the same endpoint and the same
`Enabled=false` value, but user-secrets are added later and therefore are the
effective provider. There is no running-process Redis environment variable and
no command-line Redis override.

The setting was originally an intentional degraded-mode workaround for a July
2026 Redis startup incident. It is now stale configuration drift: the endpoint
answers Redis `PING` with `PONG`, the startup connection path is timeout-bounded,
and current storefront carts require Redis persistence and locks.

Enabling Redis requires configuration changes and an API restart only. No code
change or schema migration is required.

## 3. Running Process Identity

| Field | Evidence |
|---|---|
| API process | `PointOfSale.API.exe`, PID `14804` |
| Parent | `dotnet.exe`, PID `4120` |
| Parent command line | `dotnet run` |
| API command line | `PointOfSale.API\bin\Debug\net9.0\PointOfSale.API.exe` |
| Environment | `ASPNETCORE_ENVIRONMENT=Development` |
| URL environment | `ASPNETCORE_URLS=http://localhost:5010` |
| Redis process environment entries | None |

The process environment was read from the running Windows process environment
block, not inferred from the current shell. The parent PowerShell and `dotnet`
processes also had Development selected and no Redis override.

## 4. Configuration Precedence

`WebApplication.CreateBuilder(args)` installs the normal ASP.NET Core providers.
The API then explicitly appends these providers in `Program.cs:64-78`:

1. `appsettings.json`
2. `appsettings.Development.json`
3. `appsettings.Development.local.json`
4. Development user-secrets
5. Environment variables

Later providers win. The framework command-line provider exists from
`CreateBuilder(args)`, but this process has no application configuration
arguments, and the API's explicitly appended providers occur after it.

The effective Redis chain is:

| Provider | `Redis:Enabled` | `Redis:ConnectionString` | Effective? |
|---|---:|---|---|
| `appsettings.json` | `true` | empty | Overridden |
| `appsettings.Development.json` | not present | not present | No Redis override |
| `appsettings.Development.local.json` | `false` | `192.168.1.5:6379` | Overridden by identical user-secrets values |
| Development user-secrets | `false` | `192.168.1.5:6379` | **Current effective source** |
| Running-process environment | no `Redis__*` variables | no `Redis__*` variables | No override |
| Command line | no Redis arguments | no Redis arguments | No override |

The user-secrets file exists at the standard per-user path and was last modified
on `2026-09-19T00:31:56+03:00`. Its non-Redis contents were not captured. Both
the API and Infrastructure projects declare the same UserSecretsId
(`PointOfSale.API.csproj:7` and
`PointOfSale.Infrastructure.csproj:38-42`).

## 5. Appsettings Matrix

| Environment | Source | Redis setting |
|---|---|---|
| Base | `appsettings.json:345-354` | Enabled, blank endpoint, instance `pos`, connect timeout 5000 ms, sync timeout 1000 ms |
| Development | `appsettings.Development.json` | No Redis section; inherits base before local/secrets |
| Development local | `appsettings.Development.local.json:84-92` | Endpoint `192.168.1.5:6379`, instance `pos`, **disabled** |
| Test | `appsettings.Test.json:149-158` | Disabled, blank endpoint, instance `pos-test` |
| Test local | `appsettings.Test.local.json:83-91` | Endpoint `192.168.1.5:6379`, instance `pos`, enabled |
| Staging | `appsettings.Staging.json:147-156` | Enabled, blank endpoint, instance `pos-staging` |
| Production | `appsettings.Production.json:72-80` | Enabled, blank endpoint, instance `pos` |

The local files are intentionally machine-local and ignored by Git through
`.gitignore:103-108`. The repository README documents local JSON, user-secrets,
and environment variables as the supported configuration layers
(`README.md:258-283`).

## 6. Launch Settings And Environment

Both launch profiles set only `ASPNETCORE_ENVIRONMENT=Development`; neither has a
Redis setting (`Properties/launchSettings.json:4-20`). The HTTP profile defines
`http://localhost:5010`.

The live process confirms Development and the expected URL. It contains no
`Redis__Enabled`, `Redis__ConnectionString`, or other `Redis__*` entry. The
persistent user and machine Windows environment scopes also contain no Redis
override. Therefore no environment variable supersedes user-secrets.

## 7. Exact Redis Endpoint

When enabled, the Redis cache/factory path will use:

```text
192.168.1.5:6379
```

Effective supporting settings are:

| Setting | Value | Source |
|---|---:|---|
| `Redis:ConnectionString` | `192.168.1.5:6379` | user-secrets |
| `Redis:Enabled` | `false` | user-secrets |
| `Redis:InstanceName` | `pos` | Development local/base |
| `Redis:ConnectTimeoutMs` | `5000` | Development local/base |
| `Redis:SyncTimeoutMs` | `1000` | Development local/base |

The connection string contains no password, TLS option, or database selector, so
StackExchange.Redis uses its normal default database selection. Direct read-only
runtime verification already proved `PING -> PONG`.

The running API also currently has two established TCP connections from
`192.168.1.4` to `192.168.1.5:6379`. This is consistent with the SignalR
registration described below.

## 8. Binding And Registration

`Program.cs:1837-1846` calls `AddRedisCache(builder.Configuration)`
unconditionally and registers cache warmup.

`RedisServiceCollectionExtensions.cs:33-54` does the following at startup:

- Binds the `Redis` section into `RedisSettings`.
- Uses StackExchange Redis for `IDistributedCache` only when `Enabled=true` and
  the connection string is nonblank.
- Uses process-local distributed memory cache otherwise.

It then registers the singleton `RedisConnectionFactory`, direct
`IConnectionMultiplexer`, distributed lock service, JSON cache, invalidators,
and funnel services (`RedisServiceCollectionExtensions.cs:56-84`).

`RedisConnectionFactory.cs:108-124` is the controlling feature switch. When
`Enabled=false`, it logs that Redis caching is disabled and returns no database.
When enabled, it parses the configured endpoint, applies the 5000/1000 ms
timeouts, sets three connect retries and `AbortOnConnectFail=false`, and connects
through a dedicated timeout guard (`RedisConnectionFactory.cs:126-185`).

The current code bounds connection startup with a dedicated connect thread and
watchdog. Its effective connection budget is at least 10 seconds
(`RedisConnectionFactory.cs:189-255`). A failed connection therefore degrades to
null instead of reproducing the historical unbounded startup hang.

## 9. Health Behavior

`RedisDependencyHealthCheck` uses `RedisConnectionFactory.IsEnabled`. When false,
it returns `Unhealthy("Redis is disabled by configuration.")`
(`SystemDependencyHealthChecks.cs:81-105`). This exactly matches the live MAIN
health response, proving the bound `RedisSettings.Enabled` value is false.

The separate latency check returns Degraded while disabled. When enabled, it
performs a short-lived write/read/delete health probe and applies 50 ms degraded
and 200 ms unhealthy thresholds
(`ProductionHealthChecks.cs:1245-1341`).

`SensitiveConfigurationValidator.cs:58-61` requires a nonblank connection string
when Redis is enabled. The current user-secret already satisfies that condition.

## 10. Important SignalR Exception

`Redis:Enabled` is not a complete global off-switch.

`Program.cs:3557-3579` enables the SignalR Redis backplane whenever
`Redis:ConnectionString` is nonblank; it does not inspect `Redis:Enabled`. The
backplane uses the same endpoint and a fixed `PointOfSale` channel prefix.

Therefore the running MAIN process can hold Redis connections for SignalR while
the cache/cart `RedisConnectionFactory` reports disabled. This explains why the
API has established Redis sockets despite cart persistence returning 503. It also
means enabling the switch does not introduce Redis as a wholly new dependency;
it activates the currently disabled cache, lock, and persistence paths on an
endpoint already configured for this process.

## 11. Why Redis Was Disabled

The original action was intentional. The July 24 startup investigation records:

- `192.168.1.5:6379` accepted TCP but did not answer Redis protocol commands.
- `ConnectionMultiplexer.Connect()` blocked API startup.
- `appsettings.Development.local.json` was deliberately changed to
  `Redis:Enabled=false` as a degraded-mode workaround.
- Connection construction was subsequently hardened with a dedicated bounded
  watchdog.

Evidence: `docs/reports/REDIS_STARTUP_HANGS_DEBUG_REPORT.md:18-36,63-101` and
`docs/runtime/STARTUP-1_FORENSIC_EXECUTION_AUDIT.md:192-225`.

Later startup verification proved both enabled and disabled modes start, and the
enabled mode connects successfully
(`docs/runtime/STARTUP-5_IMPLEMENTATION_AND_VERIFICATION.md:203-259`).

## 12. Intent Versus Drift

**Original status:** intentionally disabled as an emergency local-development
workaround.

**Current status:** configuration drift.

Reasons:

1. Redis now answers a real protocol `PING` with `PONG`.
2. The running API already has established connections to that endpoint through
   the independently enabled SignalR backplane.
3. The historical unbounded startup path has been replaced by a bounded watchdog.
4. Base, Staging, Production, and Test-local configurations enable Redis.
5. The repository configuration audit describes Development Redis as enabled and
   Test as the intentionally disabled environment.
6. Storefront carts use Redis as persistence, not as an optional acceleration
   cache; disabled MAIN Redis makes the live cart contract unavailable.

The false setting itself was deliberate, but its continued presence in both
Development local JSON and user-secrets no longer matches the current service
health or MAIN runtime requirements.

## 13. Code Change Requirement

**No code change is required.**

No change is required to `CartService`, controllers, Redis factory, DI
registration, health checks, persistence models, SQL schema, MongoDB, or the
frontend. The required implementation is already registered and covered by the
existing startup hardening.

An API restart is required after changing configuration. `AddRedisCache` chooses
Redis versus distributed memory during service registration, and the singleton
factory/options snapshot is built at startup. Development JSON reload cannot
rebuild those registrations in an already-running process.

## 14. Safety Assessment

Enabling Redis is safe for the MAIN DELL development runtime with the controls
below.

Positive evidence:

- The endpoint is reachable and returns `PONG`.
- The API already uses the endpoint for its SignalR backplane.
- Connection startup is bounded and failure-tolerant.
- Cart persistence keys include instance, tenant, owner type, and owner ID:
  `pos:tenant:{tenantId}:cart:{ownerType}:{ownerId}`
  (`CacheKeyBuilder.cs:153-180`, `CartService.cs:2691-2696`).
- New cart data has a 24-hour TTL.
- Redis remains non-authoritative for PostgreSQL/Mongo product truth.

Operational caveats:

- `appsettings.Test.local.json` targets the same endpoint and uses the same `pos`
  instance name. Do not run a real-Redis Test host concurrently with MAIN unless
  the Test namespace/database is first isolated.
- Cart and checkout lock keys are tenant-scoped but do not include
  `Redis:InstanceName` (`CartService.cs:2974-2975` and
  `CheckoutExecutionLockManager.cs:210-214`). Concurrent MAIN/Test activity for
  the same tenant can therefore contend even if cache prefixes later differ.
- SignalR uses the fixed `PointOfSale` channel prefix independently of
  `InstanceName`.
- Enabling affects more than carts: cache services, distributed locks,
  invalidation, rate-limiting/deduplication consumers, warmup, and health probes
  will begin using Redis.
- Existing unexpired `pos:*` keys will be reused. That is normal persistence
  behavior, but verification should use a fresh isolated storefront session.

No load-test or Kubernetes workload should share this verification window.

## 15. Exact Safe Remediation

Do not change application code. Do not change the endpoint.

When approval is given to enable Redis:

1. Ensure no Test API using `appsettings.Test.local.json` is running against the
   same DELL Redis namespace.
2. Change the effective user-secret, which is mandatory because it currently has
   higher precedence than local JSON:

   ```powershell
   dotnet user-secrets set "Redis:Enabled" "true" --project "PointOfSale.API/PointOfSale.API.csproj"
   ```

3. Align the gitignored
   `PointOfSale.API/appsettings.Development.local.json` value to
   `"Enabled": true` so the fallback machine-local source no longer carries the
   stale workaround. Keep `ConnectionString` as `192.168.1.5:6379` and
   `InstanceName` as `pos`.
4. Restart only the MAIN API through its normal Development launch profile. A
   live reload is insufficient.
5. Verify the individual `redis` and `redis_latency` entries in `/health` report
   enabled/connected and acceptable latency. Aggregate `/health` may still be 503
   because unrelated readiness checks were already unhealthy.
6. Run an isolated simple-product cart lifecycle and inspect its tenant/session
   Redis key before proceeding to wider verification.

Minimal effective change: step 2 plus restart. Step 3 is included in the safe
remediation to remove duplicate contradictory authority and prevent Redis from
silently reverting to disabled if the user-secret is later removed.

No remediation was applied during this investigation.

## 16. Final Answers

| Question | Answer |
|---|---|
| Current config source | Development user-secrets, overriding an identical `false` in `appsettings.Development.local.json` |
| Current Redis endpoint | `192.168.1.5:6379` |
| Why disabled | Historical emergency workaround for a protocol-dead Redis/startup hang; now stale configuration drift |
| Exact safe remediation | Set effective user-secret `Redis:Enabled=true`, align Development local JSON, restart MAIN API, verify individual Redis health, then run an isolated cart lifecycle |
| Code change required | **No** |
| Safe for MAIN DELL | **Yes, conditionally**, with no concurrent real-Redis Test host sharing the `pos` namespace and with post-restart health verification |

## 17. Final Determination

The cart `503 SERVICE_UNAVAILABLE` condition is caused by configuration, not cart
business logic and not Redis network availability. The current effective setting
is the Development user-secret `Redis:Enabled=false`. The application is already
implemented to connect to `192.168.1.5:6379`; enabling it is a configuration-only,
restart-required remediation.

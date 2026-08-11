# SHOPIXY Hangfire Cron Startup Fix - 2026-08-10

## 1. Executive Summary

The Shopixy API startup failure was caused by the certificate renewal recurring job registration in `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs`.

The failing job was `certificate-renewal-monitor`. Its configured interval was `CertificateRenewalScheduler:ScanIntervalMinutes = 60`. The previous registration logic transformed that value with `Hangfire.Cron.MinuteInterval(60)`, producing the invalid CRON expression:

```text
*/60 * * * *
```

Hangfire 1.8.23 internalizes Cronos 0.11.1 and validates recurring jobs with `Cronos.CronExpression.Parse(...)`. The actual parser rejects `*/60` in the minutes field with:

```text
Minutes: Value must be a number between 1 and 59 (all inclusive).
```

The fix preserves the intended hourly renewal scan by rendering the 60-minute interval as:

```text
0 */1 * * *
```

Startup was verified after the fix: PostgreSQL was reachable, Hangfire registered all recurring jobs, Kestrel started on `http://127.0.0.1:5019`, and no Cronos/CRON startup exception occurred.

## 2. Exact Failure

Observed failure:

```text
System.ArgumentException: CRON expression is invalid.
Cronos.CronFormatException: The given cron expression has an invalid format.
Minutes: Value must be a number between 1 and 59 (all inclusive).
```

Failure site:

```text
PointOfSale.API.Jobs.HangfireInitializationHostedService.RegisterRecurringJobs()
line 361
```

Line 361 is the `RecurringJob.AddOrUpdate<CertificateRenewalSchedulerJob>(...)` call.

## 3. Affected Recurring Job

| Field | Value |
|---|---|
| Job ID | `certificate-renewal-monitor` |
| Job type | `PointOfSale.API.Services.Storefront.CertificateRenewalSchedulerJob` |
| Registration method | `RecurringJob.AddOrUpdate<CertificateRenewalSchedulerJob>(...)` |
| Source method | `HangfireInitializationHostedService.RegisterRecurringJobs()` |
| Config key | `CertificateRenewalScheduler:ScanIntervalMinutes` |
| Actual configured value | `60` |
| Previous generated expression | `*/60 * * * *` |
| Fixed generated expression | `0 */1 * * *` |

## 4. Actual Invalid Cron Expression

The invalid expression was not guessed. It was proven by running Hangfire 1.8.23's helper and internal Cronos parser from the installed package:

```text
Cron.MinuteInterval(60)=*/60 * * * *
Cron.HourInterval(1)=0 */1 * * *
```

Parser evidence:

```text
Cronos.CronExpression Parse(System.String)
Cronos.CronExpression Parse(System.String, Cronos.CronFormat)
```

The full inventory validation showed only this expression invalid:

```text
certificate-renewal-monitor | */60 * * * * | INVALID | Cronos.CronFormatException:
The given cron expression has an invalid format. Minutes: Value must be a number between 1 and 59 (all inclusive).
```

## 5. Configuration Source

Trace:

```text
appsettings.json
  "CertificateRenewalScheduler": {
    "Enabled": true,
    "ScanIntervalMinutes": 60
  }

HangfireInitializationHostedService.RegisterRecurringJobs()
  _configuration.GetSection(CertificateRenewalSchedulerOptions.SectionName)
  .Get<CertificateRenewalSchedulerOptions>()

CertificateRenewalSchedulerOptions.SectionName
  "CertificateRenewalScheduler"

certificateRenewalSchedulerOptions.ScanIntervalMinutes
  60

previous BuildCertificateRenewalSchedulerCron(60)
  Cron.MinuteInterval(60)
  "*/60 * * * *"
```

Checked overrides:

| Source | Result |
|---|---|
| `appsettings.json` | `CertificateRenewalScheduler:ScanIntervalMinutes = 60` |
| `appsettings.Development.json` | no scheduler override |
| `appsettings.Test.json` | no scheduler override |
| `appsettings.Production.json` | no scheduler override |
| `appsettings.Staging.json` | no scheduler override |
| `appsettings.Development.local.json` | no scheduler override found |
| `appsettings.Test.local.json` | no scheduler override found |
| `launchSettings.json` | only `ASPNETCORE_ENVIRONMENT=Development` |
| environment variables | no scheduler override found |
| user-secrets filtered for scheduler keys | no scheduler override found |

## 6. Root Cause

Classification: **D. Bad Cron construction logic**.

The configuration value `60` was valid as a business interval: scan once per hour. The defect was using `Cron.MinuteInterval(scanIntervalMinutes)` for all positive minute values. Hangfire's helper does not guard against `60`; it returns `*/60 * * * *`, which is invalid for Cronos' minutes field.

Zero was not the root cause. Existing option validation already rejected zero for `CertificateRenewalScheduler:ScanIntervalMinutes`, and no evidence was found that zero means disabled. The scheduler already has a separate `Enabled` boolean for disabled semantics.

## 7. Git/Regression Evidence

`git blame` shows the affected domain/certificate scheduler registrations and cron builder methods are uncommitted working-tree changes:

```text
00000000 (Not Committed Yet 2026-08-10 17:12:40 +0300)
```

The base repository history for `HangfireInitializationHostedService.cs` only showed prior commits:

```text
090906e NewFramework for Storefront Products
3fb3d5c Initial repository
```

Conclusion: this startup failure was introduced by current uncommitted domain/certificate scheduler work, not by an older committed production behavior.

## 8. Fix Applied

Changed `BuildCertificateRenewalSchedulerCron(int scanIntervalMinutes)` so supported intervals are rendered into valid Hangfire/Cronos expressions:

| Interval | Fixed expression |
|---:|---|
| `1` minute | `*/1 * * * *` |
| `5` minutes | `*/5 * * * *` |
| `60` minutes | `0 */1 * * *` |
| `1440` minutes | `0 0 * * *` |

Unsupported values now fail before `RecurringJob.AddOrUpdate(...)` with an actionable configuration error naming:

- job ID
- configuration key
- invalid value
- expected range

The domain/provisioning seconds helper was also made job/key aware so provisioning errors no longer incorrectly mention `DnsVerification:Scheduler:PollIntervalSeconds`.

## 9. Defensive Validation

Added parser-backed validation using the actual Hangfire assembly:

```text
typeof(Hangfire.Cron).Assembly.GetType("Cronos.CronExpression")
```

Five-field expressions are parsed with standard Cronos parsing. Six-field expressions are parsed with `Cronos.CronFormat.IncludeSeconds` via reflection because Hangfire internalizes Cronos types.

`CertificateRenewalSchedulerOptions.Validate(...)` now rejects:

- `0`
- negative values
- unsupported non-representable values such as `90`
- excessive values such as `1500`

Valid renewal intervals are:

```text
1, 2, 3, 4, 5, 6, 10, 12, 15, 20, or 30 minutes;
or 60, 120, 180, 240, 360, 480, 720, or 1440 minutes.
```

## 10. Tests

Added/updated focused tests:

| Test area | Coverage |
|---|---|
| Previous failure | `Cron.MinuteInterval(60)` produces `*/60 * * * *` and is rejected by Hangfire/Cronos |
| Valid cron | `Cron.Hourly()` is accepted by the parser-backed validator |
| Fixed default | `BuildCertificateRenewalSchedulerCron(60)` returns `0 */1 * * *` |
| Invalid values | `0`, `-1`, `90`, and `1500` rejected deterministically |
| Valid minimum | `1` minute accepted |
| Valid maximum | `1440` minutes accepted |
| Inventory | every recurring job registered in `HangfireInitializationHostedService` validates successfully |

Focused test command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~HangfireRecurringJobCronTests|FullyQualifiedName~Domain56CertificateRenewalSchedulerTests"
```

Result:

```text
Passed: 16, Failed: 0, Skipped: 0
```

## 11. Startup Verification

Startup command:

```text
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:5019'
dotnet run --no-build --project PointOfSale.API\PointOfSale.API.csproj --no-launch-profile
```

Filtered startup evidence:

```text
PostgreSQL is available. Registering Hangfire recurring jobs immediately.
All Hangfire recurring jobs registered successfully.
Now listening on: http://127.0.0.1:5019
Application started. Press Ctrl+C to shut down.
Domain verification scheduler started ...
```

No filtered log line matched:

```text
CRON expression is invalid
CronFormatException
invalid CRON
```

The process was terminated by the verification timeout after successful startup. This is expected for a running web API.

Accounting Outbox Worker behavior was not changed. The originally observed `Accounting Outbox Worker stopped` log is consistent with host shutdown after the startup exception; this fix did not modify that worker.

## 12. Build Verification

Required API build:

```text
dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore
```

Result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

Solution build:

```text
dotnet build PointOfSale.sln --no-restore
```

Result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

## 13. Other Recurring Jobs Audit

| Job ID | Method | Cron Source | Actual Expression | Valid? |
|---|---|---|---|---|
| `shipping-tracking-sync` | `ITrackingSyncJob.SyncAllAsync` | `TrackingSyncJobRegistration.CronExpression` | `*/30 * * * *` | Yes |
| `manual-wallet-proof-cleanup` | `ManualWalletProofCleanupJob.RunAsync` | `Cron.HourInterval(6)` | `0 */6 * * *` | Yes |
| `accounting-outbox-cleanup` | `AccountingOutboxCleanupJob.RunAsync` | `Cron.Hourly` | `0 * * * *` | Yes |
| `inventory-reservation-cleanup` | `InventoryReservationCleanupJob.ExecuteAsync` | `Cron.Minutely` | `* * * * *` | Yes |
| `refund-evidence-cleanup` | `RefundEvidenceCleanupJob.RunAsync` | `Cron.Hourly` | `0 * * * *` | Yes |
| `checkout-attachment-cleanup` | `CheckoutAttachmentCleanupJob.RunAsync` | `Cron.Hourly` | `0 * * * *` | Yes |
| `whatsapp-session-heartbeat` | `WhatsAppSessionHeartbeatJob.ExecuteAsync` | `Cron.MinuteInterval(5)` | `*/5 * * * *` | Yes |
| `whatsapp-message-retry` | `WhatsAppMessageRetryJob.ExecuteAsync` | `Cron.MinuteInterval(5)` | `*/5 * * * *` | Yes |
| `whatsapp-session-cleanup` | `WhatsAppSessionCleanupJob.ExecuteAsync` | `Cron.Daily(3)` | `0 3 * * *` | Yes |
| `abandoned-cart-scanner` | `AbandonedCartScannerJob.ExecuteAsync` | `Cron.MinuteInterval(5)` | `*/5 * * * *` | Yes |
| `abandoned-cart-cleanup` | `AbandonedCartCleanupJob.ExecuteAsync` | `Cron.Daily(3)` | `0 3 * * *` | Yes |
| `abandoned-checkout-scanner` | `AbandonedCheckoutScannerJob.ExecuteAsync` | `Cron.MinuteInterval(5)` | `*/5 * * * *` | Yes |
| `processed-trust-events-cleanup` | `ProcessedTrustEventsCleanupJob.ExecuteAsync` | `Cron.Daily(3)` | `0 3 * * *` | Yes |
| `commercehub-cart-cleanup` | `CommerceHubCartCleanupJob.ExecuteAsync` | `Cron.HourInterval(6)` | `0 */6 * * *` | Yes |
| `order-request-idempotency-cleanup` | `OrderRequestIdempotencyCleanupJob.ExecuteAsync` | `Cron.Daily(2)` | `0 2 * * *` | Yes |
| `marketplace-trust-reconciliation` | `TrustDriftReconciliationJob.ExecuteAsync` | `Cron.Hourly` | `0 * * * *` | Yes |
| `marketplace-es-drift-reconciliation` | `ElasticsearchDriftReconciliationJob.ExecuteAsync` | `Cron.Daily(4)` | `0 4 * * *` | Yes |
| `commercehub-search-reconciliation` | `CommerceHubReindexService.ExecuteAsync` | `Cron.Hourly` | `0 * * * *` | Yes |
| `commercehub-finance-reconciliation` | `CommerceHubFinanceReconciliationJob.ExecuteAsync` | `Cron.Hourly` | `0 * * * *` | Yes |
| `commercehub-inventory-reconciliation` | `CommerceHubInventoryReconciliationJob.ExecuteAsync` | `Cron.Hourly` | `0 * * * *` | Yes |
| `marketplace-disk-monitor` | `DiskUsageMonitorJob.Execute` | `Cron.Hourly` | `0 * * * *` | Yes |
| `affiliate-ranking-recalculation` | `AffiliateRankingRecalculationJob.Execute` | `Cron.Daily` | `0 0 * * *` | Yes |
| `commercehub-stale-return-escalation` | `StaleReturnEscalationJob.ExecuteAsync` | `Cron.HourInterval(6)` | `0 */6 * * *` | Yes |
| `domain-verification-scheduler` | `DomainVerificationSchedulerJob.ExecuteAsync` | `DnsVerification:Scheduler:PollIntervalSeconds = 10` | `*/10 * * * * *` | Yes, parsed with `IncludeSeconds` |
| `certificate-provisioning-scheduler` | `CertificateProvisioningSchedulerJob.ExecuteAsync` | `CertificateProvisioningScheduler:ScanIntervalSeconds = 60` | `*/1 * * * *` | Yes |
| `certificate-renewal-monitor` | `CertificateRenewalSchedulerJob.ExecuteAsync` | `CertificateRenewalScheduler:ScanIntervalMinutes = 60` | `0 */1 * * *` | Yes |

Audit conclusion:

| Question | Answer |
|---|---|
| One invalid job? | Yes, before the fix: `certificate-renewal-monitor` |
| Multiple invalid jobs? | No |
| Shared configuration bug? | No |
| Malformed default/fallback? | No; the default interval was valid, the cron construction was wrong |
| Environment-specific configuration problem? | No scheduler override was found |

## 14. Unrelated Existing Issues

The repository was heavily dirty before this work. Existing unrelated modifications and untracked files were preserved.

During runtime startup, an unrelated recurring job logged that the `ProcessedTrustEvents` table did not exist and skipped cleanup. That did not stop startup and was not caused by this fix.

The first attempted startup harness using `Start-Process` failed before launching the API because the PowerShell environment has duplicate `Path/PATH` keys. The fallback foreground `dotnet run` startup verification succeeded.

## 15. Files Changed

Files changed by this fix:

```text
PointOfSale.API/Jobs/HangfireInitializationHostedService.cs
PointOfSale.API/Configuration/CertificateRenewalSchedulerOptions.cs
PointOfSale.Tests/Domain/Domain56CertificateRenewalSchedulerTests.cs
PointOfSale.Tests/Jobs/HangfireRecurringJobCronTests.cs
docs/architecture/SHOPIXY_HANGFIRE_CRON_STARTUP_FIX_2026-08-10.md
```

No commit was created. No push was performed. No reset, clean, checkout, or revert was performed.

## 16. Final Certification

PASS — STARTUP FAILURE FIXED AND VERIFIED

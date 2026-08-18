# SHOPIXY Checkout Attachment Pending Cleanup C1.2 Implementation

Date: 2026-08-12

## 1. Current Symptom

After the C1/C1.1 development upload path reached `CheckoutAttachmentService`, dynamic checkout Image/File uploads could receive:

`CHECKOUT_ATTACHMENT_PENDING_LIMIT_EXCEEDED`

The failing request was:

`POST /api/storefront/checkout/attachments/upload-lease?lang=en`

The endpoint was no longer failing at R2 availability or CSRF. It was reaching business validation and being blocked by the pending-upload guard.

## 2. Pending Limit Source

The guard is in `PointOfSale.API/Services/Checkout/CheckoutAttachmentService.cs`, method `CreateUploadLeaseAsync`.

The effective limit is:

`Math.Max(policy.MaxFiles, policy.MaxFiles * PendingUploadLimitMultiplier)`

with `PendingUploadLimitMultiplier = 3`.

For Image/File fields, `MaxFiles = 1`, so the hard limit is 3 active uploads for the same scope.

The active-count query now scopes by:

- tenant
- owner id
- owner type
- cart
- checkout field definition
- not deleted
- not attached to an order
- not expired
- status Pending, Uploaded, or Finalized

Pending rows only count while they are still inside the upload lease window.

## 3. Attachment State Machine

Actual statuses are:

- `Pending`
- `Uploaded`
- `Attached`
- `Expired`
- `Deleted`
- `Failed`
- `Deleting`
- `Finalized`

The normal dynamic upload path is:

`CreateUploadLeaseAsync -> Pending -> development/R2 binary PUT -> ConfirmUploadAsync -> Uploaded -> Finalized -> checkout/order attach -> Attached`

Failure branches:

- no binary upload: Pending ages past lease window and is cleanup-eligible
- failed binary upload: development token becomes failed/used and the Pending row stops counting after lease expiry
- failed confirm validation: attachment becomes Failed
- failed final copy: attachment becomes Failed with recoverable final object key evidence where available
- checkout/order attach: finalized attachment becomes Attached and is protected from cleanup

## 4. Cleanup Job

`CheckoutAttachmentCleanupJob` is registered by `HangfireInitializationHostedService` as recurring job `checkout-attachment-cleanup` with `Cron.Hourly`.

The job selects unattached, non-deleted rows:

- `Deleting`
- Pending rows with expired `ExpiresAtUtc` or stale lease-created time
- Uploaded/Finalized/Failed rows whose `ExpiresAtUtc` has passed

It claims each row as `Deleting`, deletes staged and final storage objects idempotently, then marks the row `Expired` and `IsDeleted = true`.

Order-attached attachments remain excluded by `OrderId == null`.

## 5. Lease vs Attachment Expiration

Before this fix:

- upload lease expiry: 15 minutes
- Pending attachment expiry: 24 hours

That meant a failed browser binary upload left an unusable Pending row that still counted toward the upload limit for 24 hours.

After this fix:

- new Pending rows use the upload lease expiry for `ExpiresAtUtc`
- stale pre-existing Pending rows also stop counting after the lease window based on `CreatedAt`
- successful finalization extends `ExpiresAtUtc` to the existing 24-hour attachment retention window

## 6. Runtime Data Evidence

A read-only EF audit against the local development database found exactly three Pending, non-deleted, unattached checkout attachments in the same scope:

- Tenant: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`
- Cart: `2e0f51ed-92b6-4f2b-a2ca-432ade99c6ff`
- Field: `cc1bf2b3-96cb-407b-8166-9288860fa611`
- OwnerType: `customer`
- OwnerId: `316a3a06b2ab46a08eb43778873136a7`

Created timestamps were on 2026-08-12 around 20:04-20:07 UTC. Their stored expiration timestamps were 2026-08-13 around 20:04-20:07 UTC, proving the C1/C1.1 failed attempts had 24-hour Pending retention.

## 7. Root Cause

Root cause: C1 created generic checkout attachment Pending rows with the long attachment retention TTL even though the binary upload lease expires after 15 minutes.

Classification:

- G: browser retry behavior could create multiple active Pending records
- H: failed binary upload attempts did not stop consuming Pending slots promptly
- J: production-relevant lifecycle defect in the generic attachment lifecycle

The pending limit itself is correct and was preserved.

## 8. Remediation

Implemented the smallest lifecycle fix:

- Pending attachments now expire at the same time as the upload lease.
- Confirmed/finalized attachments extend to the existing 24-hour unattached attachment retention window.
- Pending count excludes stale Pending rows whose lease window has elapsed.
- Cleanup targets the same stale Pending rows counted by the guard.
- Pending count now includes `OwnerType` to match checkout cart authority scope.

No limit increase was made.

## 9. Security/Tenancy

Tenant, cart, field, owner id, and owner type are all part of the active pending scope.

Cleanup remains server-side and does not trust browser-supplied tenant ids. It never deletes order-attached attachments because the query requires `OrderId == null`.

No Production R2 behavior was weakened. The storage abstraction remains unchanged for R2.

## 10. Storage Cleanup

Cleanup deletes:

- staged object key
- final object key when present and distinct

Deletion is idempotent for development local storage and handles R2 missing-object responses safely.

Finalized order attachments are protected by `OrderId == null`.

## 11. Retry/Idempotency

Active Pending uploads still count toward the limit. Stale Pending leases no longer block retry after the lease window.

Repeated confirmation of an already Finalized attachment remains idempotent. Lease replay prevention remains in `DevelopmentFileStorageService`.

## 12. Tests

Added/updated tests covering:

- active Pending uploads still enforce the limit
- expired Pending uploads do not block a new lease
- stale Pending rows from the old 24-hour behavior do not block a new lease
- Pending count separates owner type
- finalization extends retention beyond upload lease expiry
- cleanup expires stale Pending rows
- cleanup deletes staged development upload objects
- cleanup never deletes finalized order attachments
- Image/File/MultiImage development upload and confirm

Regression commands:

- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CheckoutAttachmentServiceTests"`: passed, 44 tests
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CheckoutAttachmentControllerContractTests|FullyQualifiedName~CookieAuthSecurityTests|FullyQualifiedName~ManualWalletProofCleanupJobTests|FullyQualifiedName~CheckoutAttachmentDownloadSecurityArchitectureTests|FullyQualifiedName~DynamicCheckoutObservabilityTests"`: passed, 22 tests
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ManualWalletProof"`: passed, 4 tests

An attempted broader checkout-session country normalization run failed from unrelated missing tenant-currency fixture setup.

## 13. Builds

- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore`: passed
- `dotnet build PointOfSale.sln --no-restore`: passed
- `npm run build`: passed

Storefront build emitted existing Vite chunking and browserslist warnings.

## 14. EF/Migration

Command:

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj`

Result:

`No changes have been made to the model since the last migration.`

No migration was created.

## 15. Browser Verification

Not completed in this run. The local code and database evidence prove the server-side Pending-limit blocker is remediated, but live browser verification remains external.

## 16. Remaining Limitations

Existing stale rows are not deleted immediately at startup. They stop blocking after the lease window and are removed by the normal cleanup job. Manual execution of `CheckoutAttachmentCleanupJob` can also process them through the application cleanup path.

## 17. Worktree Safety

Initial worktree was already dirty with existing C1/C1.1 and unrelated Payment/Manual Wallet changes. No reset, clean, restore, stash, commit, or push was performed.

## 18. Final Certification

PASS WITH EXTERNAL VERIFICATION BLOCKED

Development pending lifecycle is corrected, cleanup matches the authoritative active-count semantics, expired/stale uploads no longer block new upload leases, the pending limit remains effective, Production R2 behavior is preserved, backend and frontend builds pass, EF has no pending model changes, and only live browser/R2 verification remains external.

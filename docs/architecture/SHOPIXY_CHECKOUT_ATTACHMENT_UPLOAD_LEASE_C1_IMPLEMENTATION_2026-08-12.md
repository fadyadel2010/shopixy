# SHOPIXY Checkout Attachment Upload Lease C1 Implementation - 2026-08-12

## 1. Root Cause

Dynamic checkout Image/File/MultiImage fields use the generic checkout attachment path:

`ShippingStep.tsx -> POST /api/storefront/checkout/attachments/upload-lease -> CheckoutController -> CheckoutAttachmentService -> IR2StorageService.CreateUploadUrlAsync`.

In local Development without configured R2, `Program.cs` selected `DevelopmentFileStorageService` for `IR2StorageService`, but that service deliberately threw from `CreateUploadUrlAsync`. `CheckoutController.CreateAttachmentUploadLease` caught that storage exception and returned `503 SERVICE_UNAVAILABLE` with `ApiErrorResponse.ServiceUnavailable(traceId)`.

This was not the Manual Wallet proof upload path. Manual Wallet continues to use `/api/storefront/payments/manual-wallet/upload-proof` and was not merged with checkout attachments.

## 2. Current Upload Contract

Storefront request body:

- `cartId`
- `fieldId`
- `fileName`
- `contentType`
- `fileSizeBytes`

Storefront headers come from the normal checkout HTTP client context, including tenant/session/customer authority headers where applicable. The direct binary upload uses `fetch(lease.data.uploadUrl)` with:

- method from `lease.data.uploadMethod`, normally `PUT`
- `Content-Type` equal to `lease.data.contentType`
- file body

Lease response DTO:

- `attachmentId`
- `uploadUrl`
- `uploadMethod`
- `contentType`
- `expiresAtUtc`
- `attachmentExpiresAtUtc`

After binary upload succeeds, Storefront calls `POST /api/storefront/checkout/attachments/confirm` with `cartId`, `fieldId`, and `attachmentId`. Final checkout later submits the finalized attachment IDs inside the dynamic checkout field value.

## 3. Development Storage Strategy

Implemented Option A: extend the existing Development-only `DevelopmentFileStorageService` behind `IR2StorageService`.

The Development implementation now returns an opaque application upload URL:

`/api/storefront/checkout/attachments/development-upload?token=...`

The token is not a filesystem path and does not contain tenant/session/object metadata. The token maps to a private lease file under `secure-uploads/development-object-storage/_leases`.

Production behavior remains unchanged:

- Development fallback is selected only when `builder.Environment.IsDevelopment()` and R2 is not configured.
- Configured R2 still selects `R2StorageService`.
- Production never selects the Development fallback.

## 4. Security / Authority

Preserved authority checks:

- Tenant is resolved server-side by checkout controller/header validation.
- Cart/session/customer ownership is validated by `CheckoutAttachmentService.ValidateCartAuthorityAsync`.
- Field identity is resolved server-side from `CheckoutFieldDefinitions`.
- Upload field type must be Image, File, or MultiImage.
- MIME type and max size are validated against dynamic field configuration before lease creation.
- Confirm resolves an owned attachment by tenant, cart, field, owner, session/customer.
- Finalization cannot bind an unrelated object because it uses the server-generated object key stored on the attachment row.

Development upload URL hardening:

- Opaque random token with secret hash in private lease metadata.
- Token is scoped to one server-generated object key and content type.
- Expiration is enforced before accepting bytes.
- Successful upload consumes the lease; replay is rejected.
- Upload without a valid lease is rejected.
- Path normalization rejects traversal and invalid path segments.
- Files are stored under private `secure-uploads/development-object-storage`, not `wwwroot`.
- Browser never receives filesystem paths.

## 5. Dynamic Field Support

The implementation consumes the existing `CheckoutAttachmentService.BuildPolicy` rules:

- Image defaults/configured MIME types.
- File defaults/configured MIME types.
- MultiImage configured `maxFiles`.
- Configured `maxFileSize`.

No hard-coded storefront MIME policy was introduced. Development upload storage validates signatures against the approved content type, and finalization revalidates metadata, size, content type, and signature through the existing checkout attachment service.

## 6. Lifecycle / Finalization

Existing lifecycle remains:

1. Lease creates a Pending `CheckoutAttachment`.
2. Binary upload writes the staged object.
3. Confirm reads metadata and content prefix.
4. Confirm copies staged object to deterministic final object key.
5. Attachment becomes Finalized.
6. Checkout attaches finalized attachments to the order.
7. Cleanup deletes expired Pending/Uploaded/Finalized/Failed unattached objects.

Abandoned or expired checkout attachments continue through `CheckoutAttachmentCleanupJob`. Failed finalization persists terminal failure state where existing logic already did so.

## 7. Implementation Changes

Changed:

- `PointOfSale.API/Services/DevelopmentFileStorageService.cs`
  - Implements Development upload leases.
  - Adds private token lease metadata.
  - Adds single-use binary upload handling.
  - Adds content-type/signature validation.
  - Fixes local `GetObjectBytesAsync` to return an object prefix like R2.

- `PointOfSale.API/Controllers/CheckoutController.cs`
  - Adds Development-only `PUT attachments/development-upload`.
  - Returns 404 outside Development or when configured R2 is active.
  - Maps invalid/expired/replayed lease failures to explicit API errors.

- Tests updated/added in:
  - `PointOfSale.Tests/Storefront/DevelopmentFileStorageServiceTests.cs`
  - `PointOfSale.Tests/Checkout/CheckoutAttachmentServiceTests.cs`
  - `PointOfSale.Tests/Checkout/CheckoutAttachmentControllerContractTests.cs`

Manual Wallet code was not changed.

## 8. Tests

Passed:

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CheckoutAttachmentServiceTests|FullyQualifiedName~DevelopmentFileStorageServiceTests|FullyQualifiedName~CheckoutAttachmentControllerContractTests"`
  - 42 passed.

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~StorefrontManualWalletStorageTests|FullyQualifiedName~ManualWalletProofCleanupJobTests"`
  - 6 passed.

- `npm run test -- --run src/commerceAuthority.certification.test.ts src/utils/checkoutFieldRules.test.ts`
  - 2 files passed, 54 tests passed.

Coverage includes Development lease success without R2, path traversal rejection, upload without lease rejection, lease expiration, replay prevention, successful upload + finalization, Image/File/MultiImage support, dynamic MIME/max-size authority through service tests, production/R2 registration pinning, and Manual Wallet regression checks.

## 9. Builds

Passed:

- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore`
- `dotnet build PointOfSale.sln --no-restore`
- `npm run build`

Storefront build completed with existing Vite warnings for chunk size/dynamic imports and outdated Browserslist data.

## 10. EF / Migration

Passed:

- `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj`

Result: `No changes have been made to the model since the last migration.`

No migration was created.

## 11. Browser Verification

Not completed in this pass. The local browser/data environment was not exercised end to end.

Automated service-level verification proves the Development dynamic attachment sequence now completes:

`lease -> development PUT upload -> confirm -> finalized attachment`.

External live browser verification remains to confirm UI behavior against a seeded local storefront checkout field and cart.

## 12. Remaining Limitations

- Development upload URLs are process-local application URLs, not R2-compatible external presigned URLs.
- Lease token metadata is Development-only private filesystem metadata.
- Full browser verification and real R2 verification remain external checks.
- Existing finalization behavior still uses prefix validation in `CheckoutAttachmentService`, consistent with the previous R2 path.

## 13. Worktree Safety

Initial state had pre-existing Payment Methods / Manual Wallet changes and untracked Development storage artifacts. Those were preserved.

No `git reset`, `git clean`, `git checkout`, `git restore`, `git stash`, commit, or push was performed.

## 14. Final Certification

PASS WITH EXTERNAL VERIFICATION BLOCKED

Development dynamic checkout attachment upload now works through the generic checkout attachment infrastructure without configured R2, production R2 selection remains preserved, Manual Wallet remains separate, tests/builds pass, and only live browser/real external R2 verification remains.

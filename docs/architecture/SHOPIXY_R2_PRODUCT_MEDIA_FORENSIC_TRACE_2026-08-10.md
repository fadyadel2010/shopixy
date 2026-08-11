# SHOPIXY R2 PRODUCT MEDIA FORENSIC TRACE

## 1. Executive Summary

Status: present in source but configuration unavailable in this verification environment.

The current POS Admin product media flow is R2-backed for uploaded image/video files. The active UI calls `POST /api/upload`, which uploads server-side through `IR2StorageService`/`R2StorageService`, then registers the returned media URL through `POST /api/admin/products/{productId}/media`.

The canonical R2 schema is `R2_ACCOUNT_ID`, `R2_SERVICE_URL`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET`, and `R2_PUBLIC_URL`, with nested equivalents `R2:AccountId`, `R2:ServiceUrl`, `R2:AccessKeyId`, `R2:SecretAccessKey`, `R2:Bucket`, and `R2:PublicUrl`.

No usable R2 values were found in the local process/user/machine environment, API appsettings files, API user-secrets, launch profiles, or staging env example. Staging documentation says R2 secrets are injected at runtime and not stored in `appsettings.*.json`.

## 2. Product Media Upload Architecture

Flow:

`PointOfSale.Web/src/components/ProductForm.jsx`
-> `ProductMediaManager`
-> `api.post('/upload', multipart/form-data)`
-> `UploadController.Upload`
-> `FileValidationService.ValidateFileAsync(..., FileValidationProfiles.ProductMedia)`
-> `IR2StorageService.UploadFileAsync`
-> `R2StorageService.UploadFileAsync`
-> AWS S3 SDK `PutObjectAsync`
-> `R2Options`
-> Cloudflare R2/S3-compatible endpoint
-> returned public URL
-> `api.post('/admin/products/{productId}/media')`
-> `ProductMediaController.AddMedia`
-> `ProductMediaService.AddMediaAsync`
-> `ProductMedia` database row and `ProductMediaChangedEvent`.

The older `ImageUpload.jsx` component posts to `/products/upload-images`, but no matching current API route was found. The registered `IImageService` implementation is local filesystem-backed and returns `/uploads/images/...`; it is not the active `ProductForm` media manager path.

## 3. Canonical R2 Storage Implementation

Interface: `PointOfSale.API/Services/R2StorageService.cs`, `IR2StorageService`.

Implementation: `R2StorageService`, using `AmazonS3Client`, `BasicAWSCredentials`, `AmazonS3Config.ServiceURL`, `ForcePathStyle = true`, and `AuthenticationRegion = "auto"`.

Registration: `PointOfSale.API/Program.cs` registers `IR2StorageService` as scoped to `R2StorageService`.

Configuration class: `PointOfSale.API/Configuration/R2Options.cs`.

Configuration binding: `PointOfSale.API/Program.cs` manually binds flat `R2_*` keys first, falling back to nested `R2:*` keys.

Configured check: `R2Options.IsConfigured` requires access key id, secret access key, bucket, and a resolved service URL. Resolved service URL can come from explicit `ServiceUrl`, `AccountId`, or a Cloudflare R2 public URL host.

## 4. Configuration Sources

| Setting | Canonical Name | Source | Present? | Used By |
|---|---|---|---|---|
| Account id | `R2_ACCOUNT_ID` or `R2:AccountId` | Current process/user/machine env | ABSENT | `R2Options`, `R2StorageService` |
| Service URL | `R2_SERVICE_URL` or `R2:ServiceUrl` | Current process/user/machine env | ABSENT | `R2Options`, `R2StorageService` |
| Access key id | `R2_ACCESS_KEY_ID` or `R2:AccessKeyId` | Current process/user/machine env | ABSENT | `R2Options`, `R2StorageService` |
| Secret access key | `R2_SECRET_ACCESS_KEY` or `R2:SecretAccessKey` | Current process/user/machine env | ABSENT | `R2Options`, `R2StorageService` |
| Bucket | `R2_BUCKET` or `R2:Bucket` | Current process/user/machine env | ABSENT | `R2Options`, `R2StorageService` |
| Public URL | `R2_PUBLIC_URL` or `R2:PublicUrl` | Current process/user/machine env | ABSENT | public URL construction |
| Account id | `R2:AccountId` | `PointOfSale.API/appsettings.json`, `appsettings.Test.json`, `appsettings.Staging.json` | ABSENT_OR_EMPTY | `R2Options` |
| Service URL | `R2:ServiceUrl` | same appsettings files | ABSENT_OR_EMPTY | `R2Options` |
| Access key id | `R2:AccessKeyId` | same appsettings files | ABSENT_OR_EMPTY | `R2Options` |
| Secret access key | `R2:SecretAccessKey` | same appsettings files | ABSENT_OR_EMPTY | `R2Options` |
| Bucket | `R2:Bucket` | same appsettings files | ABSENT_OR_EMPTY | `R2Options` |
| Public URL | `R2:PublicUrl` | same appsettings files | ABSENT_OR_EMPTY | `R2Options` |
| All R2 settings | flat `R2_*` | `deploy/staging/pointofsale.api.env.example` | ABSENT_OR_EMPTY example keys | deployment secret injection contract |
| All R2 settings | flat or nested | .NET user-secrets id `bbdf2e74-8790-4753-8874-f20fcfbdbbd5` | ABSENT | Development API runtime |

No credential values were printed or recorded.

## 5. Environment Analysis

Repository root: `C:/Users/fadya/source/repos/PointOfSale`.

Baseline dirty worktree: many pre-existing modified/untracked files. Relevant pre-existing modified files include `PointOfSale.API/Services/R2StorageService.cs`, `PointOfSale.API/Program.cs`, `PointOfSale.API/appsettings.json`, checkout services/controllers/DTOs, and storefront checkout files.

Configuration loading order in `Program.cs`:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `appsettings.{Environment}.local.json`
4. user-secrets only when `builder.Environment.IsDevelopment()`
5. environment variables

Launch profiles only set `ASPNETCORE_ENVIRONMENT=Development`; they do not set R2 keys.

Development: local API user-secrets are accessible for the API project and contain non-R2 keys, but all R2 flat and nested names are ABSENT.

Test: `appsettings.Test.json` has an R2 section, but its values are empty. User-secrets are not added unless the environment is Development.

Staging/Production: staging docs state R2 secrets must be injected by runtime environment variables, Kubernetes secrets, Azure Key Vault, AWS Secrets Manager, or equivalent. The staging env example lists the variables but leaves them empty. No Kubernetes or Helm manifest with concrete R2 values was found.

Why previous P0 verification did not see R2: the verification environment checked `R2_*` variables in a shell/runtime where those variables are absent. Source also accepts nested `R2:*`, but those are absent in appsettings and user-secrets here. Product media may have worked historically in a different deployed or manually launched runtime with injected R2 values; that credential-bearing runtime source is not present in this workspace/session.

## 6. Product Image Storage

Bucket: `R2Options.Bucket`.

Provider: Cloudflare R2 through AWS S3 SDK.

Upload endpoint: `POST /api/upload`.

Upload method: `R2StorageService.UploadFileAsync`.

Product object-key pattern:

`tenants/{tenantId:D}/products/{productId}/{generatedGuid}.{extension}`

For variant uploads:

`tenants/{tenantId:D}/variants/{variantId}/{generatedGuid}.{extension}`

For general uploads:

`tenants/{tenantId:D}/general/{optionalEntityId}/{generatedGuid}.{extension}`

Who generates key: server-side `UploadController.BuildObjectPath` plus server-side GUID filename in `R2StorageService.UploadFileAsync`.

Browser control: browser supplies file, `entityType`, and `entityId`; server validates `entityType`, sanitizes `entityId`, and generates the final filename. `UploadController` does not prove the product id exists before uploading. Product media registration later proves the product exists and belongs to the tenant.

Path traversal prevention: path segments and entity id remove invalid filename chars, `/`, `\`, and `..`.

Original filename: not used for the R2 object key.

Determinism/immutability: object keys are random GUID-based; uploaded object is not immutable by application contract, but product uploads use server-side `PutObjectAsync`, not a reusable browser presigned PUT.

Delete: `ProductMediaService.RemoveMediaAsync` removes the `ProductMedia` database row and publishes projection changes. It does not call `IR2StorageService.DeleteObjectAsync`, so product media deletion does not delete R2 objects.

Read/download: storefront/admin read paths consume stored public URLs from product media projection. No product-media signed download path was found.

Metadata: product media database row stores media type, URL, provider, position, and created timestamp. It does not store bucket, object key, ETag, checksum, or content length.

## 7. Checkout Attachment Storage

Current Phase 4B path:

`ShippingStep.tsx`
-> `POST /api/storefront/checkout/attachments/upload-lease`
-> `CheckoutController.CreateAttachmentUploadLease`
-> `CheckoutAttachmentService.CreateUploadLeaseAsync`
-> `IR2StorageService.CreateUploadUrlAsync`
-> direct browser `PUT` to presigned R2 URL
-> `POST /api/storefront/checkout/attachments/confirm`
-> `GetObjectMetadataAsync`
-> `GetObjectBytesAsync` with ETag
-> file signature validation
-> `CopyObjectAsync` from staged key to final key
-> `CheckoutAttachment` row finalized
-> order submission attaches finalized rows.

Staged object-key pattern:

`tenants/{tenantId:N}/checkout/{cartId:N}/fields/{fieldId:N}/attachments/{yyyy/MM/dd}/{generatedGuid}.{extension}`

Final object-key pattern:

`tenants/{tenantId:N}/checkout-final/{cartId:N}/fields/{fieldId:N}/attachments/{yyyy/MM/dd}/{attachmentId}.{extension}`

Checkout attachments reuse the same `IR2StorageService`, `R2StorageService`, `R2Options`, bucket, service URL, and credential source as product media. They use different purpose-specific prefixes.

## 8. Compatibility Assessment

Can the existing Product Media R2 configuration be reused to perform real Phase 4B verification?

NO.

Reason: the code paths are compatible and share the same storage abstraction/configuration, but the actual R2 credential-bearing configuration is not available in this verification environment. A real Phase 4B verification needs the same runtime-injected R2 values used by the working API environment.

Specific answers:

1. `CheckoutAttachmentService` uses `IR2StorageService`: YES.
2. `IR2StorageService` resolves the same R2 configuration used by product media: YES, both use `R2Options`.
3. Phase 4B `CopyObjectAsync` uses the real R2 provider when configured: YES, `R2StorageService.CopyObjectAsync` calls S3-compatible copy in the configured bucket.
4. Missing configuration is an environment/config-source issue in this verification session: YES.
5. Phase 4B is not wired to a different configuration path: it uses the same `IR2StorageService`.

## 9. Runtime Verification

Runtime R2 connectivity was not tested.

Reason: no usable R2 configuration was visible in current env variables, API appsettings, API user-secrets, launch settings, or staging env example. Running the built-in R2 health check would report configuration failure rather than Cloudflare connectivity.

Existing read-only capability: `R2DependencyHealthCheck` performs `ListObjectsV2` with `MaxKeys = 1` when configuration is present.

No upload, delete, copy, presigned URL generation, database mutation, or production object access was performed.

## 10. Root Cause of Previous R2 Verification Block

Result: D. Different environment/runtime secret injection configuration.

Evidence:

- Product upload source uses R2 through `UploadController` and `R2StorageService`.
- `Program.cs` binds both flat `R2_*` and nested `R2:*` names.
- Local process/user/machine environment: all flat `R2_*` names ABSENT.
- API appsettings R2 sections: present but empty where present.
- API/Infrastructure shared user-secrets id: R2 flat and nested names ABSENT.
- Launch profiles: only `ASPNETCORE_ENVIRONMENT=Development`.
- Staging docs: explicitly require runtime secret injection and prohibit storing credentials in `appsettings.*.json`.

Therefore the previous P0 verification did not see R2 because it was not running in the credential-bearing API runtime/environment. The repository contains the canonical schema and R2 implementation, but not the actual runtime values.

## 11. Security Findings

Secret handling: no secret values are checked into inspected appsettings or staging env example. Runtime secret injection is the documented staging approach.

Tenant isolation: product upload object paths include tenant id. Checkout attachments include tenant id, cart id, field id, owner id/type, session/customer identity, expiry, and tenant-scoped persistence predicates.

Object-key generation: product and checkout final object filenames are server-generated. Original product filenames are not used for object keys. Checkout original filenames are sanitized and stored as metadata/presentation, not as object keys.

Public URL exposure: product media stores public URLs in `ProductMedia.Url`; product image reads use these URLs directly. Checkout attachments do not expose object keys to storefront review and use admin-authorized download through `DownloadObjectAsync`.

Signed URL usage: checkout upload leases return presigned PUT URLs to the browser. Product media uploads are server-side multipart uploads and do not expose upload signed URLs.

Client-controlled keys: browser does not control final filename. Product upload browser supplies `entityId`; server sanitizes it, but `UploadController` does not prove product existence before upload, so orphan objects are possible if a caller submits an arbitrary id.

Product media deletion: DB row deletion does not delete the R2 object, leaving possible orphaned product media objects.

## 12. Phase 4B Recommendation

Minimum next step: run Phase 4B verification in the same credential-bearing API runtime environment used for product media, or inject the canonical R2 variables into the verification runtime using the documented deployment secret mechanism.

Do not add new configuration names. Use the existing `R2_*` or `R2:*` schema already bound by `Program.cs`.

## 13. Files Inspected

- `PointOfSale.API/Program.cs`
- `PointOfSale.API/Configuration/R2Options.cs`
- `PointOfSale.API/Configuration/SensitiveConfigurationValidator.cs`
- `PointOfSale.API/Services/R2StorageService.cs`
- `PointOfSale.API/Controllers/UploadController.cs`
- `PointOfSale.API/Controllers/ProductMediaController.cs`
- `PointOfSale.API/Services/ProductMediaService.cs`
- `PointOfSale.API/Services/ImageService.cs`
- `PointOfSale.API/Controllers/ProductsController.cs`
- `PointOfSale.API/Services/FileValidationService.cs`
- `PointOfSale.Web/src/components/ProductForm.jsx`
- `PointOfSale.Web/src/components/ProductMediaManager.jsx`
- `PointOfSale.Web/src/components/ImageUpload.jsx`
- `PointOfSale.Core/Entities/ProductMedia.cs`
- `PointOfSale.Infrastructure/Data/Configurations/ProductMediaConfiguration.cs`
- `PointOfSale.API/Services/Checkout/CheckoutAttachmentService.cs`
- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx`
- `PointOfSale.Core/Entities/CheckoutAttachment.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CheckoutAttachmentConfiguration.cs`
- `PointOfSale.API/Services/Admin/AdminOrderService.cs`
- `PointOfSale.API/HealthChecks/SystemDependencyHealthChecks.cs`
- `PointOfSale.API/PointOfSale.API.csproj`
- `PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj`
- `PointOfSale.API/Properties/launchSettings.json`
- `PointOfSale.API/appsettings.json`
- `PointOfSale.API/appsettings.Development.json`
- `PointOfSale.API/appsettings.Test.json`
- `PointOfSale.API/appsettings.Staging.json`
- `PointOfSale.API/appsettings.Production.json`
- `deploy/staging/README.md`
- `deploy/staging/pointofsale.api.env.example`
- `docs/fixes/COMMERCEHUB_R2_DIRECT_UPLOAD_CORS.md`
- `docs/architecture/SHOPIXY_DYNAMIC_CHECKOUT_FORENSIC_AUDIT_2026-08-10.md`

## 14. Files Changed

None, outside this requested forensic report file.

Created report:

`docs/architecture/SHOPIXY_R2_PRODUCT_MEDIA_FORENSIC_TRACE_2026-08-10.md`

No source code, configuration, user-secrets, database records, migrations, R2 buckets, or object storage were modified.

## 15. Final Certification

BLOCKED - Existing R2 configuration could not be identified

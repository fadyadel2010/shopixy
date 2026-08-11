# Shopixy Dynamic Checkout Upload Format UX Hardening - 2026-08-10

## 1. Executive Summary

The POS Admin Checkout Fields Builder no longer asks merchants to type raw MIME strings for upload-capable custom fields. Image, File, and Multiple Images fields now use accessible, merchant-facing checkbox selectors backed by the existing `allowedMimeTypes` configuration contract.

No backend storage, R2 lifecycle, attachment persistence, upload lease, confirmation, finalization, cleanup, or snapshot semantics were changed.

## 2. Existing Upload Configuration Contract

Checkout upload configuration is stored on `CheckoutFieldDefinition.ConfigurationJson` using schema version 1.

Observed representation:

```json
{
  "schemaVersion": 1,
  "allowedMimeTypes": ["image/jpeg"],
  "maxFileSize": 5242880,
  "maxFiles": 1
}
```

`allowedMimeTypes` is an array of MIME strings. `maxFileSize` is bytes. `maxFiles` is an integer. Single Image and File fields use `maxFiles: 1`; Multiple Images can configure `maxFiles`.

## 3. Forensic Findings

Admin UI previously converted `allowedMimeTypes` into `allowedMimeTypesText` and rendered a raw text input labeled "Allowed MIME Types".

Backend Admin validation currently accepts:

- Image and Multiple Images: `image/jpeg`, `image/png`, `image/webp`
- File: `image/jpeg`, `image/png`, `image/webp`, `application/pdf`, `application/msword`

The runtime attachment service builds the authoritative upload policy from the same persisted configuration. It validates lease MIME, max size, max files, object metadata, content type, extension, and file signatures through `FileValidationService`.

Storefront reads `configuration.allowedMimeTypes` and uses it only for the browser file input `accept` hint. Server-side validation remains authoritative.

## 4. Root Cause / UX Problem

The merchant-facing builder exposed backend MIME syntax directly. A normal merchant had to know values such as `image/jpeg`, even though the system already had a constrained backend allowlist.

## 5. New Merchant-Facing UX

Image and Multiple Images now show:

- Allowed image formats
- JPG / JPEG
- PNG
- WEBP

File now shows:

- Allowed file types
- JPG / JPEG
- PNG
- WEBP
- PDF
- Word

Excel, CSV, ZIP, and DOCX are not exposed because the current checkout-field Admin/runtime policy does not fully admit them for checkout uploads.

## 6. Canonical Format Mapping

The Admin UI uses `PointOfSale.Web/src/modules/storefront/checkoutFields/uploadFormatOptions.js` as the single UI-level mapping source:

- `jpg` -> `image/jpeg`
- `png` -> `image/png`
- `webp` -> `image/webp`
- `pdf` -> `application/pdf`
- `word_doc` -> `application/msword`

Labels are resolved through i18n keys, not hardcoded in JSX.

## 7. Backward Compatibility Strategy

Existing persisted MIME arrays are normalized and read back into checkbox state. Examples:

- `["image/jpeg"]` selects JPG / JPEG.
- `["image/jpeg", "image/png"]` selects JPG / JPEG and PNG.

Unknown legacy MIME values are displayed in a legacy configured formats notice and preserved in the Admin payload unless the merchant explicitly removes them.

## 8. Validation Strategy

Frontend validation now blocks:

- Empty supported image format selection for Image and Multiple Images.
- Empty supported file type selection for File.
- Duplicate MIME values.
- Invalid max file size.
- Invalid Multiple Images max file count.

The frontend no longer provides arbitrary MIME entry. Backend validation still remains authoritative.

## 9. Security Preservation

No security boundary moved to the frontend. The backend still controls:

- MIME allowlisting.
- File signature/content validation.
- File extension validation.
- Max file size.
- Max file count.
- Tenant isolation.
- Customer/session/cart ownership.
- Upload lease validation.
- Upload confirmation and finalization.
- R2 object key generation.
- Orphan cleanup.
- Order checkout field snapshots.

## 10. Files Changed

- `PointOfSale.Web/src/modules/storefront/checkoutFields/uploadFormatOptions.js`
- `PointOfSale.Web/src/modules/storefront/checkoutFields/pages/CheckoutFieldsPage.jsx`
- `PointOfSale.Web/src/modules/storefront/checkoutFields/checkoutFields.test.tsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`
- `docs/architecture/SHOPIXY_DYNAMIC_CHECKOUT_UPLOAD_FORMAT_UX_HARDENING_2026-08-10.md`

## 11. Tests Added/Updated

Updated POS Admin checkout field tests cover:

- Image defaults.
- JPG/JPEG, PNG, and WEBP mapping.
- Image deselection persistence.
- Empty image selection rejection.
- Multiple Images format behavior with max file size and max files preservation.
- File presets and MIME mapping for PDF and Word.
- Empty File selection rejection.
- No arbitrary MIME input.
- Existing MIME configuration readback.
- Unknown legacy MIME preservation.
- Upload configuration round trip with conditional rules and limits.

## 12. Build Verification

Passed:

- `npm test -- checkoutFields.test.tsx --run` in `PointOfSale.Web` - 24 tests passed.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CheckoutFieldDefinitionServiceTests|FullyQualifiedName~CheckoutAttachmentServiceTests|FullyQualifiedName~OrderCheckoutFieldSnapshotTests|FullyQualifiedName~AdminOrderCheckoutFieldPresentationTests|FullyQualifiedName~CheckoutAttachmentControllerContractTests|FullyQualifiedName~CheckoutAttachmentDownloadSecurityArchitectureTests"` - 74 tests passed.
- `npm run build` in `PointOfSale.Web`.
- `npm run build` in `PointOfSale.Storefront`.
- `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore`.
- `dotnet build PointOfSale.sln --no-restore`.

## 13. EF/Migration Verification

Passed:

```powershell
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext
```

Result: no pending model changes. No migration was created.

## 14. Unrelated Existing Issues

The worktree was already heavily dirty before this task, including untracked checkout-field backend/Admin files and unrelated domain/storefront changes. Those were not reset or reverted.

Storefront build completed with pre-existing Vite warnings about stale Browserslist data, dynamic/static import chunking, and chunk size.

## 15. Final Status

The upload format configuration UX is hardened in POS Admin while preserving the existing backend contract and runtime upload security model.

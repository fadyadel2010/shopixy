# MAIN Storefront Checkout Fields File Upload R2 Final Certification

## Decision

**CONDITIONAL PASS** for the application implementation and authenticated attachment lifecycle.

The secure checkout attachment flow is implemented and verified through lease admission, real R2 object upload, confirmation, immutable order persistence, redacted Admin presentation, authenticated image preview, and authenticated download. Backend, POS Admin, Storefront, build, responsive, RTL, and reload-recovery checks pass.

Deployment acceptance remains **BLOCKED** for an unassisted browser workflow because the R2 bucket does not return an `Access-Control-Allow-Origin` response for `http://ahramstore.localhost:5174`. The browser correctly rejects the presigned PUT during preflight. A Playwright-only network bridge was used to exercise the downstream confirmation, order, preview, and download stages; this does not certify the current R2 CORS configuration.

This is a feature certification, not a global production-readiness assessment.

## Scope And Safety

- Repository: MAIN Windows checkout on branch `main`.
- Runtime: Development API on `5010`, POS Admin on `5173`, and Storefront on `5174`.
- Existing R2 storage and checkout attachment abstractions remain authoritative; no duplicate storage path was added.
- R2 credentials, bucket details, object keys, ETags, checksums, cookies, and authentication tokens are not recorded here.
- No database reset, broad cleanup, or unrelated worktree modification was performed.
- Extensive concurrent worktree changes were preserved.

## Implemented Controls

### Upload Admission

- Original filename extensions must match the server-admitted canonical file type.
- JPEG admits `.jpg` and `.jpeg`; PNG, WEBP, PDF, DOC, and DOCX admit only their corresponding extensions.
- A mismatch fails with `CHECKOUT_ATTACHMENT_EXTENSION_MISMATCH` before a lease is issued.
- Existing MIME, size, field policy, tenant, cart, and lifecycle validation remains in force.

### Storefront Upload UX

- Presigned object upload uses `XMLHttpRequest` so progress can be reported.
- Upload status is exposed through an accessible `<progress>` element and live status text.
- Storage upload failures produce localized English/Arabic feedback and retain the failed file for explicit retry.
- Confirmed attachment values continue through the canonical checkout field value and order submission flow.
- Sensitive checkout data is stored only in tenant-scoped `sessionStorage`.

### Authenticated Admin Read

- Download remains under `GET /api/admin/orders/{orderId}/checkout-attachments/{attachmentId}/download`.
- The endpoint explicitly admits only `Owner` and `Admin`; the controller-level `Manager` role is narrowed out.
- Order ownership, tenant, order, attachment, and attached-state predicates are checked before object access.
- The service compares downloaded object length and normalized content type with authoritative attachment metadata.
- Integrity mismatches dispose the object stream and return a controlled conflict response.
- Responses disable caching and MIME sniffing. Source and architecture tests also require a restrictive attachment CSP.
- Admin order JSON omits the final object key, ETag, and checksum.

### Secure Preview

- POS Admin requests attachment bytes through the authenticated API and renders a temporary `blob:` URL.
- Only `image/jpeg`, `image/png`, and `image/webp` with image media classification are previewed.
- SVG, PDF, and other files remain download-only.
- Object URLs are revoked on cleanup.
- Loading, failure, retry, download-in-progress, and download-failure states are represented.

## Reload Persistence Defect And Correction

The first browser run found that a reload cleared address and confirmed attachment presentation even though checkout autosave had reported success.

Root cause: `checkoutStore` was imported before tenant bootstrap. Zustand attempted tenant-scoped hydration while no tenant key was available and never retried after the backend supplied canonical tenant authority.

Correction:

- Automatic pre-bootstrap Zustand hydration is disabled.
- Checkout storage hydration now runs explicitly after canonical tenant resolution and before the application becomes ready.
- The sensitive session snapshot is read before persisted-state migration and reapplied afterward, preventing hydration side effects from erasing it.
- A regression test covers tenant-scoped address and confirmed attachment recovery.

Post-fix headed-browser evidence used synthetic session-only values, performed a full page reload, and confirmed recovery of the customer name, address line, cart binding, and confirmed attachment value. The synthetic checkout storage entry was removed immediately afterward.

## Browser Certification

A disposable optional image checkout field and a valid 96 by 96 PNG were used.

| Check | Result |
|---|---|
| Checkout field rendered in Storefront | Pass |
| Native browser R2 preflight | Blocked by missing R2 CORS origin |
| User-visible native failure and retry | Pass |
| Playwright-bridged real PNG PUT | Pass, 759 bytes |
| Attachment confirmation | Pass |
| Order creation and immutable snapshot | Pass |
| POS Admin authenticated preview | Pass |
| Preview source | `blob:` URL, not a public R2 URL |
| Preview dimensions | 96 by 96 natural dimensions |
| Authenticated download | HTTP 200, `image/png`, expected filename |
| Live cache-control | `private, no-store, max-age=0` |
| Live MIME-sniffing protection | `X-Content-Type-Options: nosniff` |
| Admin JSON storage metadata redaction | Pass |
| Mobile viewport at 390 by 844 | Pass, no horizontal overflow |
| RTL Arabic presentation | Pass |
| Preview certification console errors | 0 |
| Post-fix full reload recovery | Pass |

The disposable trace order was `ORD-C9C5-2026-01037`. The Admin cancellation workflow moved it to the on-hold state rather than a terminal cancelled state. No unsupported database or lifecycle override was used to force further cleanup.

## R2 CORS Blocker

The browser preflight to the presigned R2 upload URL does not admit the Storefront origin:

```text
Origin: http://ahramstore.localhost:5174
Required method: PUT
Required request header: Content-Type
Observed result: preflight response lacks Access-Control-Allow-Origin
```

The browser reports that the preflight response does not pass access control. The Storefront then displays the controlled storage-upload failure and retry action.

Required environment remediation:

- Add the exact intended Storefront origins to the bucket CORS policy.
- Permit `PUT` for presigned checkout uploads.
- Permit the `Content-Type` request header.
- Keep the origin list explicit; do not use a wildcard merely to make certification pass.
- Re-run the same browser workflow without Playwright routing.

No Cloudflare account or bucket configuration was changed during this work. Private-bucket configuration was not independently proven from Cloudflare account authority and is not claimed.

## Persistence And Redaction Evidence

- The order was created through the Storefront checkout path after attachment confirmation.
- POS Admin subsequently read the immutable checkout snapshot and rendered the attachment metadata.
- The snapshot retained safe presentation data: attachment identifier, filename, canonical content type, size, media type, and lifecycle timestamps.
- Admin JSON contained no object key, final ETag, or final checksum.
- Object bytes were available only through the authenticated tenant- and order-scoped Admin endpoint.
- Download object length and content type are checked against the authoritative database association before streaming.

## Verification Results

| Verification | Result |
|---|---|
| Focused backend checkout attachment suite | 56 passed, 0 failed, 0 skipped |
| POS Admin preview and presentation suite | 8 passed, 0 failed |
| Full Storefront Vitest suite | 386 passed across 43 files |
| Storefront focused reload test | 12 passed in its file |
| Storefront production build | Passed; existing Browserslist/import/chunk warnings remain |
| `PointOfSale.Web` production build | Passed in this certification cycle |
| `git diff --check` | Passed; line-ending warnings remain informational |

The focused backend run used isolated artifacts because another process held normal build outputs:

```text
POINT_OF_SALE_SOLUTION_ROOT=C:\Users\fadya\source\repos\PointOfSale
--artifacts-path C:\Users\fadya\AppData\Local\Temp\opencode\pos-checkout-fields-artifacts
```

## Source Changes

- `PointOfSale.API/Services/Checkout/CheckoutAttachmentService.cs`
  - Adds authoritative original extension validation.
- `PointOfSale.API/Services/Admin/AdminOrderService.cs`
  - Validates object size/content type and redacts private metadata from snapshot presentation.
- `PointOfSale.API/Services/Admin/IAdminOrderService.cs`
  - Carries the attachment download integrity contract.
- `PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs`
  - Removes final ETag and checksum from Admin attachment output.
- `PointOfSale.API/Controllers/AdminOrdersController.cs`
  - Narrows authorization and hardens download response headers.
- `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx`
  - Adds progress, retry, accessibility, and localized upload states.
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
  - Defers tenant-scoped hydration until canonical tenant authority exists.
- `PointOfSale.Storefront/src/bootstrap/bootstrap.ts`
  - Executes checkout hydration after canonical tenant resolution.
- `PointOfSale.Storefront/src/store/checkoutStore.validation.test.ts`
  - Covers address and confirmed attachment reload recovery.
- `PointOfSale.Web/src/pages/OrderCheckoutAttachmentPreview.jsx`
  - Adds authenticated allow-listed blob preview and fallback behavior.
- `PointOfSale.Web/src/services/adminOrdersApi.js`
  - Adds reusable authenticated blob retrieval and safe download cleanup.
- `PointOfSale.Web/src/pages/orderCheckoutFieldPresentation.js`
  - Retains safe media classification and legacy image inference.
- English and Arabic locale files
  - Add Storefront upload and Admin preview text.
- Checkout backend and POS Admin frontend tests
  - Cover spoofing, integrity, disposal, redaction, headers, preview restrictions, retry, and URL cleanup.

Some listed files contain unrelated concurrent modifications. This certification describes only the checkout attachment changes.

## Cleanup

- The disposable checkout field was archived.
- Cash on delivery was restored to disabled.
- Synthetic reload-certification session data was removed.
- The generated `.playwright-cli/placeholder.png` artifact was removed.
- No R2 credentials, signed upload URL, object key, or private metadata were persisted in the report.
- The test order remains on hold because the supported cancellation action produced that lifecycle state.

## Residual Risks

- Native Storefront upload cannot pass until bucket CORS admits the Storefront origin and PUT headers.
- Bucket privacy was not independently inspected in Cloudflare account configuration.
- Live insufficient-role and cross-tenant download attempts were not performed; authorization and ownership behavior are covered by source architecture and service tests.
- Global API readiness remains unhealthy because of unrelated `payout_external_truth_readiness`; PostgreSQL and R2 readiness checks were healthy during certification.

## Final Result

**CONDITIONAL PASS: the secure application lifecycle, persistence, redaction, authenticated preview/download, responsive/RTL behavior, and reload recovery are verified. Final deployment acceptance requires an exact-origin R2 CORS correction followed by an unbridged browser rerun.**

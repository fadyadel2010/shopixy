# SHOPIXY C5.5 Manual Payment Admin Review and Order Lifecycle Integrity Implementation

Date: 2026-08-13
Project: SHOPIXY / PointOfSale
Status: Implementation complete, final certification blocked by live external verification limits.

## 1. Executive Summary

C5.5 remediated two admin workflow gaps without redesigning the proven Manual Wallet checkout path.

- Manual Payment proof viewing remains tenant/order/payment authorized and now streams through the protected admin API instead of redirecting the browser to storage.
- Admin Orders no longer presents lifecycle-controlled states as ordinary direct status candidates.
- Admin Orders now reads backend-issued fulfillment action authority from the canonical fulfillment endpoint and exposes only evidence-light actions from the drawer.
- Backend `INVALID_ORDER_STATE` protection remains intact.
- Regression coverage was added for proof authorization and lifecycle UI/source contracts.

Final certification is `BLOCKED` because live in-app browser verification could not establish an authenticated Admin session, Chrome connector access was unavailable, and the exact non-isolated solution build was blocked by a running `PointOfSale.API (20184)` process locking build output.

## 2. Current Verified State

User-provided live evidence established this working path before C5.5:

Manual Wallet -> Storefront Checkout -> Order Creation -> Pending Manual Payment -> Admin Approval -> Payment Collected -> Order Confirmed.

This implementation did not redesign that path. The approval endpoint still delegates to `_manualPaymentReviewApplicationService.ApproveAsync`, and order/payment lifecycle mutation remains backend-owned.

## 3. Proof Viewer Forensics

Path traced:

- Admin UI: `PointOfSale.Web/src/pages/ManualPayments.jsx`
- List endpoint: `GET /api/admin/payments/manual-wallet/pending`
- Proof endpoint: `GET /api/admin/payments/manual-wallet/proof-image/{paymentIntentId}`
- Controller: `AdminManualPaymentController.GetProofImage`
- Storage reference: `PaymentIntent.ProofImageUrl`
- Storage key parser: `ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey`
- Storage abstraction: `IR2StorageService`

Observed symptom from supplied screenshot: clicking `View` opened the proof modal but displayed a failure message.

Source-level first failing point: the proof endpoint had an access-contract mismatch. The Admin UI fetches the protected proof endpoint as a blob, while the controller still attempted to create a storage download URL and redirect. That made the browser/storage access path depend on signed URL behavior instead of returning the already-authorized protected content.

Classification: G/K - secure URL/access contract mismatch plus frontend blob rendering contract mismatch. Live network capture was not possible in this environment.

## 4. Proof Authority

Proof retrieval is now bound before storage I/O to:

- current tenant from `ITenantContext`
- `PaymentIntent.Id`
- `PaymentIntent.Provider == ManualWallet`
- `PaymentIntent.TenantId == current tenant`
- associated `Order`
- `Order.Id == PaymentIntent.OrderId`
- `Order.TenantId == current tenant`
- non-deleted order
- tenant-scoped storage object key

The browser cannot override tenant, order, attachment, or object key. It supplies only the payment intent id; the server resolves and validates everything else.

## 5. Storage Contract

Canonical storage remains `IR2StorageService`.

- Development uses `DevelopmentFileStorageService`.
- Production uses `R2StorageService`.
- Manual Wallet proof keys are normalized by `ManualWalletProofStorage`.
- No public proof files or storage credentials were introduced.

The proof endpoint now calls `DownloadObjectAsync` and returns `File(download.Content, download.ContentType)` after authorization. R2 behavior remains behind the same abstraction.

## 6. Manual Payment API

Endpoints inspected:

- `GET /api/admin/payments/manual-wallet/pending`
- `GET /api/admin/payments/manual-wallet/proof-image/{paymentIntentId}`
- `POST /api/admin/payments/manual-wallet/{paymentIntentId}/confirm`
- `POST /api/admin/payments/manual-wallet/{paymentIntentId}/reject`

The pending DTO already supplies order, amount, customer, wallet display information, proof reference intent, payment state, and created date needed by the review UI. Secrets are not returned.

## 7. Approval Workflow

Approval path remains:

Pending manual payment -> `_manualPaymentReviewApplicationService.ApproveAsync` -> payment lifecycle collected -> order confirmed.

Reject path remains:

Pending manual payment -> `_manualPaymentReviewApplicationService.RejectAsync` -> payment review rejection semantics.

C5.5 preserved this authority. It also returns the lifecycle failure code when approval fails instead of collapsing every failure to `LIFECYCLE_ERROR`.

## 8. Order Lifecycle State Machine

Direct generic status mutation remains intentionally limited. Lifecycle-controlled states are:

- `Confirmed`
- `Processing`
- `Shipped`
- `Delivered`
- `Completed`
- `Cancelled`
- `Refunded`

Generic direct status mutation is now represented in the UI only for direct-safe candidates such as `OnHold` and same-status display. Fulfillment, shipment, delivery, completion, cancellation, and payment-driven status changes remain dedicated lifecycle actions.

## 9. Direct Status Endpoint

Endpoint inspected:

- `PUT /api/admin/orders/{orderId}/status`

Protection source:

- `AdminOrderService.UpdateOrderStatusAsync`
- `EnsureAdminStatusOverrideDoesNotAdvanceOperationalLifecycle`
- `InvalidAdminOrderStateException`
- error code `INVALID_ORDER_STATE`

The guard producing the known message is intentional and remains unchanged:

`Direct status update to {status} is prohibited. Use dedicated lifecycle endpoints to ensure cross-system integrity.`

## 10. Dedicated Lifecycle Endpoints

Canonical fulfillment endpoints discovered:

- `GET /api/orders/{orderId}/fulfillment/actions`
- `POST /api/orders/{orderId}/fulfillment/confirm`
- `POST /api/orders/{orderId}/fulfillment/pack`
- `POST /api/orders/{orderId}/fulfillment/out-for-delivery`
- `POST /api/orders/{orderId}/fulfillment/delivered`
- `POST /api/orders/{orderId}/fulfillment/complete`
- `POST /api/orders/{orderId}/fulfillment/returned`
- `POST /api/orders/{orderId}/fulfillment/failed-delivery`
- `POST /api/orders/{orderId}/fulfillment/retry-delivery`
- `POST /api/orders/{orderId}/fulfillment/cancel`

Admin cancellation also exists at:

- `POST /api/admin/orders/{orderId}/cancel`

C5.5 did not invent endpoint names.

## 11. Admin UI Contract

Implemented contract:

- Generic status dropdown uses `getDirectOrderStatusMutationOptions`.
- Lifecycle-controlled statuses are excluded from generic candidate submission.
- UI explains that shipment, delivery, completion, cancellation, and payment-driven states are dedicated lifecycle actions.
- Fulfillment lifecycle panel loads backend-issued action authority with `getAdminOrderFulfillmentActions`.
- Authority is validated with `requireShipmentActionAuthority`.
- Evidence-light actions are submitted with `runAdminOrderFulfillmentAction`.
- Actions requiring additional evidence, such as delivery failure/return evidence, are disabled in this drawer.

## 12. Tenant/Security

Manual proof retrieval now refuses storage access if the payment/order tenant binding is invalid.

Fulfillment endpoints now check `IsCurrentTenantOrderAsync` before exposing actions or mutating fulfillment state. Cross-tenant order ids return `ORDER_NOT_FOUND`.

No tenant predicates were removed.

## 13. Concurrency

Manual approval/rejection concurrency remains owned by the existing manual payment review application service, payment lifecycle service, and existing concurrency tests. C5.5 did not add a second approval path or double-collection path.

Fulfillment concurrency remains owned by `OrderFulfillmentService` with serializable transitions and lifecycle policies.

## 14. Previous Fix Regression Review

Reviewed authorities:

- wallet number/types/options: Manual Wallet admin/storefront payment method configuration
- proof upload/finalization: `StorefrontManualWalletController`, `CheckoutService`, `ManualWalletProofStorage`
- development storage and R2 abstraction: `DevelopmentFileStorageService`, `R2StorageService`, `IR2StorageService`
- CSRF and checkout binding: existing checkout/session/attachment services
- cleanup: `ManualWalletProofCleanupJob`, `CheckoutAttachmentCleanupJob`
- proof claim/order creation: `CheckoutService`
- approval/rejection: `AdminManualPaymentController`, manual payment review application service
- COD separation: preserved by provider checks and existing payment flow tests

C5.5 did not duplicate these authorities.

## 15. Root Causes

Defect A - Proof viewer:

- Symptom: proof modal failed to render stored proof.
- First failing point: protected proof endpoint returned a storage redirect instead of protected proof bytes matching the UI blob contract.
- Root cause: API/browser proof access contract drift.
- File/symbol: `AdminManualPaymentController.GetProofImage`.
- Impact: valid pending payments could still be approved, but proof inspection could fail.

Defect B - Order lifecycle UX:

- Symptom: Admin Orders allowed users to select `Shipped` or `Completed` in generic status UI, then backend returned 409 `INVALID_ORDER_STATE`.
- First failing point: frontend direct status candidate list included lifecycle-controlled states.
- Root cause: Admin UI did not mirror backend lifecycle authority.
- File/symbol: `Orders.jsx` generic status control.
- Impact: predictable domain-protected 409 shown as ordinary workflow failure.

## 16. Fixes Implemented

Backend:

- Bound proof image retrieval to tenant-owned manual wallet payment and associated order before storage I/O.
- Streamed proof bytes through the protected admin endpoint using `IR2StorageService.DownloadObjectAsync`.
- Preserved legacy proof fallback through the same storage abstraction.
- Added fulfillment tenant guard in `OrderFulfillmentController`.
- Added `FulfillmentStatus` to `AdminOrderDetailsDto` and standard order details mapping.

Frontend:

- Added direct-safe status candidate filtering in Admin Orders.
- Added fulfillment actions API calls.
- Added backend-issued fulfillment action authority validation.
- Added lifecycle action panel in the order details drawer.
- Disabled lifecycle actions that require extra evidence from this generic drawer.

Documentation/context:

- Added missing SF-ADM3.4 registration markers required by existing architecture certification tests.

## 17. Regression Tests

Added or extended:

- `AdminManualPaymentPendingTests.GetProofImage_WithPaymentOrderTenantMismatch_DoesNotTouchStorage`
- `AdminManualPaymentPendingTests.GetProofImage_WithWrongTenantScopedProofKey_DoesNotTouchStorage`
- updated authorized proof streaming test
- `Orders.test.ts` lifecycle UI contract tests
- `AdminOrderLifecycleAuthorityCertificationTests`
- `AdminOrderFulfillmentShipmentAuthorityCertificationTests`

Coverage maps to proof authorization, tenant isolation, storage non-access on invalid authority, generic UI exclusion of lifecycle states, canonical fulfillment endpoint wiring, and disabled evidence-required actions.

## 18. Live Browser Verification

Attempted:

- In-app browser at `http://localhost:5173/ecommerce/manual-payments`
- Chrome connector

Result:

- In-app browser remained at session restoration without an authenticated Admin context.
- Chrome connector returned unavailable.
- Live proof/network capture and live lifecycle action verification could not be completed from this Codex environment.

## 19. Build/EF

Passed:

- `npm test -- --run src/pages/ManualPayments.test.ts src/pages/Orders.test.ts` - 12 tests passed.
- `npm run build` in `PointOfSale.Web` - passed.
- `npm run build` in `PointOfSale.Storefront` - passed with existing Vite/Browserslist/chunk warnings.
- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -p:UseAppHost=false -p:OutputPath=C:\tmp\pos-c55-api-build\ -clp:ErrorsOnly` - passed.
- targeted backend tests with repo-local isolated output - 11 passed.
- `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build` - no model changes.
- `git diff --check` - no whitespace errors; existing LF/CRLF warnings only.

Blocked:

- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore` and `dotnet build PointOfSale.sln --no-restore` were blocked by `PointOfSale.API (20184)` locking build output.
- `dotnet ef migrations has-pending-model-changes` without `--no-build` was blocked by the same build lock.

## 20. Remaining Limitations

- Final live browser evidence is still required in a signed-in Admin session.
- Exact non-isolated solution build requires stopping or moving the running local API process that locks `bin\Debug\net9.0`.
- Actions needing delivery/return/failure evidence are intentionally disabled in the generic order drawer until a UI collects the required evidence payloads.

## 21. Worktree Safety

Before modification:

- `git status --short` showed a large dirty worktree from prior C1-C5.4/shipping/payment work.
- `git diff --stat` showed 136 files changed, 4477 insertions, 1315 deletions.

During C5.5:

- No reset, clean, restore, checkout, stash, commit, or push was performed.
- Existing dirty work was preserved.

After modification:

- `git status --short` remains broadly dirty with preserved prior C1-C5.4 work plus C5.5 changes.
- `git diff --stat` reports 147 tracked files changed, 4830 insertions, 1327 deletions.
- `.codex-temp/` is present from the repo-local isolated test output and was not deleted in order to avoid cleanup/destructive worktree actions in a dirty tree.

## 22. Final Certification

`BLOCKED`

Reason: implementation and targeted regression coverage are complete, but the requested live browser workflow could not be established and the exact non-isolated solution build remains blocked by a running local API process. Backend protections remain intact; no proof files were made public; storage credentials were not exposed; tenant/order authorization was strengthened rather than weakened.

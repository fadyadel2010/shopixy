# SHOPIXY Shipping Methods Step 2H Implementation

Date: 2026-08-11

Certification: PASS WITH EXTERNAL VERIFICATION BLOCKED

## 1. Scope

Implemented the narrow Storefront lifecycle fix for Step 2H:

```text
cart hydration
  -> valid cartId
  -> CheckoutSession create/resume for that cart
  -> shipping preview with checkoutSessionId only
  -> shipping selection with shippingMethodId
  -> WarehouseResolution
  -> final checkout
```

No warehouse-selection redesign, carrier redesign, backend preview contract change, payment architecture change, or migration was introduced.

## 2. Audit Reference

Canonical audit:

`docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2H_FORENSIC_AUDIT_2026-08-11.md`

Audit verdict was `BLOCKED` because Storefront could call `fetchShippingMethods()` before cart hydration allowed checkout session creation.

## 3. Root Cause Confirmed

Confirmed source sequence before the fix:

```text
CheckoutPage mount
  -> fetchCart()
  -> fetchShippingMethods()
  -> ensureCheckoutSessionInitialized()
  -> useCartStore.getState().cart is missing
  -> null session
  -> local Storefront error
```

The observed text, `Checkout session is required before shipping methods can be previewed.`, is local Storefront text generated before backend preview is reached.

## 4. Exact Files Changed

Step 2H files changed:

| File | Purpose |
| --- | --- |
| `PointOfSale.Storefront/src/store/checkoutStore.ts` | Added cart-aware session association and not-ready handling for shipping preview. |
| `PointOfSale.Storefront/src/pages/CheckoutPage.tsx` | Gated shipping preview behind checkout cart refresh completion and non-loading cart id. |
| `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx` | Guarded mount-triggered shipping fetch behind initialized, non-loading cart readiness. |
| `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx` | Clears `checkoutSessionCartId` during recovery rehydration. |
| `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts` | Added Step 2H Storefront lifecycle regression tests. |
| `PointOfSale.Tests/Storefront/StorefrontShippingMethodsControllerContractTests.cs` | Added backend preview missing-session contract regression test. |
| `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2H_IMPLEMENTATION_2026-08-11.md` | This implementation report. |

The repository had many pre-existing dirty tracked and untracked files before this implementation. They were preserved.

## 5. Lifecycle Changes

`CheckoutPage` no longer calls `fetchShippingMethods()` in the same mount effect as `fetchCart()`.

New behavior:

```text
CheckoutPage mount
  -> await fetchCart() completion marker
  -> cart effect observes cartId and not loading
  -> fetchShippingMethods()
  -> ensure session for current cart
  -> preview
```

`fetchShippingMethods()` now treats missing cart id as initialization not-ready state:

```text
no cartId
  -> abort stale shipping request
  -> set shipping status IDLE
  -> no preview request
  -> no terminal session-required error
```

Real session/backend failures still surface through the shipping error path.

## 6. Session/Cart Consistency Behavior

Added in-memory `checkoutSessionCartId` to `checkoutStore`.

Invariant:

```text
checkoutSessionId is reusable only when checkoutSessionCartId === current cartId
```

Behavior:

| Scenario | Result |
| --- | --- |
| Current cart matches `checkoutSessionCartId` | Existing session may be reused. |
| Current cart differs from `checkoutSessionCartId` | Stale session authority is cleared and a new session is created/resumed for the current cart. |
| Concurrent callers for same cart | Share `checkoutSessionInitPromise`; one session request. |
| Concurrent/in-flight cart changes | Old in-flight session init is aborted and not reused for the new cart. |

## 7. ShippingStep Behavior

`ShippingStep` still can request shipping methods, but only after cart readiness:

```text
cartId && isCartInitialized && !isCartLoading && shippingMethods.length === 0
```

This prevents early component mount from independently producing the local session-required preview failure.

## 8. Manual Fulfillment Verification

Manual Fulfillment production logic was not changed.

Verified by source/static checks:

| Requirement | Status |
| --- | --- |
| `carrierCode = manual` remains first-class | Preserved. |
| No `CarrierAccount` required for manual preview/select | Preserved. |
| No `CarrierWarehouseAssignment` required for manual preview/select | Preserved. |
| Active configured `FulfillmentWarehouseId` still required | Preserved. |
| Warehouse/inventory eligibility still required | Preserved. |

## 9. Backend Contract Preserved

Preview payload remains exactly:

```json
{
  "checkoutSessionId": "GUID"
}
```

Static check confirmed no `warehouseId`, `cartId`, or `tenantId` was added to the Storefront preview payload.

Selection payload remains:

```json
{
  "checkoutSessionId": "GUID",
  "shippingMethodId": "GUID"
}
```

No backend production contract was changed.

## 10. Security/Authority Verification

Verified Step 2H did not introduce:

| Forbidden regression | Result |
| --- | --- |
| `warehouseId` in preview payload | Not introduced. |
| Browser-selected warehouse authority | Not introduced. |
| Raw `X-Tenant-Id` result authority | Not introduced. |
| Backend session bypass | Not introduced. |
| Client-side tenant authority | Not introduced. |
| Manual carrier-account dependency | Not introduced. |
| Duplicate session creation for same cart | Covered by regression test. |
| Stale session reuse across cart ids | Covered by regression test. |

## 11. Tests Added

Storefront tests added in `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts`:

| Test | Coverage |
| --- | --- |
| No cart during shipping preview initialization | No preview request and no terminal session-required error. |
| Delayed cart hydration | Session creation occurs after cart exists and before preview. |
| Preview receives valid session | Preview payload is `{ checkoutSessionId }` only. |
| Cart ID swap | Old session is not reused for new cart. |
| Concurrent session initialization | One checkout-session request for same cart. |
| ShippingStep early mount guard | ShippingStep source requires initialized/non-loading cart. |
| Real backend session failure | Real session failure is surfaced, not converted to silent loading. |
| Happy path | Session -> preview -> select -> WarehouseResolution is asserted in delayed hydration test. |

Backend test added in `PointOfSale.Tests/Storefront/StorefrontShippingMethodsControllerContractTests.cs`:

| Test | Coverage |
| --- | --- |
| Preview missing `checkoutSessionId` | Returns HTTP 400, code `CHECKOUT_SESSION_REQUIRED`, message `Checkout session is required.`, and does not call warehouse preview loading. |

## 12. Tests Executed

| Command | Result |
| --- | --- |
| `npm test -- --run src/commerceAuthority.certification.test.ts` in `PointOfSale.Storefront` | PASS, 35 tests. |
| `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter StorefrontShippingMethodsControllerContractTests` | First attempt timed out after dependency build at 120s; rerun with 300s PASS, 1 test. |
| `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ShopixyWarehouseResolutionAuthorityCertificationTests|FullyQualifiedName~ShopixyShippingMethodFulfillmentWarehouseAuthorityTests|FullyQualifiedName~StorefrontShippingMethodAuthorityCertificationTests|FullyQualifiedName~WarehouseResolutionServiceTests|FullyQualifiedName~StorefrontShippingMethodsControllerContractTests"` | 23 passed, 2 failed due unrelated/stale architecture expectations. See known unrelated failures. |
| `npm test -- --run` in `PointOfSale.Storefront` | PASS, 274 tests. |
| `npm test -- --run src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx` in `PointOfSale.Web` | PASS, 13 tests. |

## 13. Build Results

| Command | Result |
| --- | --- |
| `npm run build` in `PointOfSale.Storefront` | PASS. Vite emitted existing dynamic import/chunk size/Browserslist warnings. |
| `npm run build` in `PointOfSale.Web` | PASS. |
| `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore` | PASS, 0 warnings, 0 errors. |
| `dotnet build PointOfSale.sln --no-restore` | PASS, 0 warnings, 0 errors. |

## 14. EF Migration Result

Exact command requested from repo root:

`dotnet ef migrations has-pending-model-changes`

Result: failed because no project was found at the solution root.

Rerun with explicit project/startup project:

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API/PointOfSale.API.csproj`

Result: PASS. EF reported: `No changes have been made to the model since the last migration.`

No migration is required for Step 2H.

## 15. Worktree Safety

Pre-change status and diff-stat were captured.

The worktree was already heavily dirty before Step 2H, including many tracked shipping/checkout changes and untracked Step 2A-2G artifacts. No reset, clean, checkout, stash, revert, commit, or push was performed.

Step 2H intentionally modified only the files listed in section 4. Existing unrelated dirty files were preserved.

## 16. Known Unrelated Failures

The relevant backend architecture test run reported two failures in `StorefrontShippingMethodAuthorityCertificationTests`:

| Failure | Why considered unrelated/stale |
| --- | --- |
| `StorefrontShippingMethodFrontend_MustSubmitCandidatesOnly` | The assertion expects exact source text `setShippingMethod: (id) =>`, while current Step 2G/2H checkout store uses async method selection to call `/shipping-methods/select` and receive `WarehouseResolution`. |
| `CertificationArtifacts_MustRecordSfPa53Truth` | Existing certification artifact text does not contain expected `PASS SF-PA5.3`; unrelated to Step 2H lifecycle code. |

These tests were not modified to hide the failures.

## 17. Final Certification

Certification: PASS WITH EXTERNAL VERIFICATION BLOCKED

Reasoning:

| Criterion | Result |
| --- | --- |
| Storefront lifecycle no longer intentionally previews before cart/session context | PASS. |
| Session id is cart-aware and not reused across cart id changes | PASS. |
| Preview contract remains `{ checkoutSessionId }` only | PASS. |
| Backend contract preserved | PASS. |
| Manual Fulfillment authority preserved | PASS by source/static verification. |
| Storefront tests/build pass | PASS. |
| Backend API/solution builds pass | PASS. |
| EF migration check | PASS with explicit project/startup project. |
| Live browser/API/PostgreSQL verification | Not performed in this environment. |

Step 2H implementation is ready for external/live verification.

# SHOPIXY Storefront Checkout C4 Forensic Review

Date: 2026-08-13  
Scope: Storefront Checkout from cart hydration through committed order and success state  
Disposition: Remediated and internally coherent for the final live COD/Manual Wallet Place Order verification

## 1. Scope and baseline preservation

The review started from a dirty worktree containing the prior C1, C1.1, C1.2, C2, C3, Shipping, and Payment work. The initial baseline was captured before C4 edits:

- `git status --short`: prior modified and untracked checkout, attachment, shipping, payment, tests, reports, and generated development-upload files were present.
- `git diff --stat`: 32 tracked files, 1,638 insertions, 176 deletions.
- No reset, clean, restore, checkout, stash, commit, or push was performed.
- Existing generated/untracked reports and `.codex-temp` content were preserved.

This report describes the cumulative checkout state, but only defects proven during C4 were changed. Closed Shipping and Payment architectures were not redesigned.

## 2. Lifecycle proof

The effective Storefront sequence is now:

1. Storefront configuration and identity bootstrap resolve the tenant on the server.
2. `CheckoutPage` requests and awaits a fresh authoritative cart.
3. Checkout state binds persisted sensitive data to that cart ID and removes attachments from any previous cart.
4. A checkout session is created or resumed for the authoritative cart and owner/session identity.
5. Dynamic checkout definitions load independently; an authoritative empty definition set remains empty and does not reactivate legacy address requirements.
6. Shipping preview waits for both the current cart and checkout session.
7. Payment availability loads from the Storefront availability endpoint and admits only configured, enabled, executable methods.
8. Server pricing preview runs for the current cart/session/selections. Same-state requests coalesce; changed-state requests cancel and supersede earlier responses.
9. Customer field and attachment input is validated. Pending uploads globally block step progression and final submission.
10. Shipping selection returns the server-owned warehouse resolution and version. The browser cannot submit a warehouse ID.
11. Final submission refreshes the cart, shipping authority, payment availability, and pricing before issuing an idempotent checkout request.
12. The server revalidates tenant, owner, cart, session/version, field visibility, attachments, shipping resolution, payment method, pricing, promotion, and inventory.
13. One serializable transaction reserves inventory, inserts the Order and items, claims attachments after the Order exists, writes snapshots, creates the required payment state, writes outbox records, and commits.
14. The stored idempotency result is returned and the Storefront records the active order context before entering success state.

Cancellation is silent for intentionally aborted cart, session, field, shipping, payment, selection, attachment, and pricing work. Sequence fencing prevents late responses from replacing newer state.

## 3. Authority and contract matrix

| Frontend operation | Backend endpoint | Authoritative input/DTO behavior | Response consumed by Storefront |
| --- | --- | --- | --- |
| Cart hydration/mutation | `GET/POST/PATCH/DELETE /api/storefront/cart...` | Tenant and owner/session headers are resolved and validated server-side; product identity, variant, price, stock, discounts, and totals are server-derived | Normalized `Cart` at the API boundary; summary renders its snapshot fields and totals |
| Checkout session | `POST/PATCH /api/storefront/checkout-session...` | Cart ID is checked against tenant and authenticated customer or guest session authority; optimistic version protects stale writes | Session ID/version, cart binding, recovery and upload authority |
| Dynamic fields | `GET /api/storefront/checkout-fields` | Server publishes enabled definitions and rule metadata | Typed controls, labels, help, constraints, and current visibility state |
| Attachment lease/confirm/remove | `/api/storefront/checkout/attachments/...` | Lease token binds tenant, owner/session, cart, field, MIME, size, count, expiry, and one-time confirmation | Finalized pending attachment IDs only; no browser file path is trusted |
| Shipping preview/select | `/api/storefront/shipping-methods/preview` and `/select` | Checkout session, live cart, geography, and method eligibility are revalidated; warehouse and price are resolved server-side | Eligible methods plus warehouse resolution ID/version/expiry |
| Payment methods | `GET /api/storefront/payment-methods` | Tenant document and global provider runtime configuration must both be valid | `AVAILABLE`, `EMPTY`, `UNAVAILABLE`, or `ERROR`; only available methods may be selected |
| Pricing preview | `POST /api/storefront/checkout/pricing` | Live cart, promotion, shipping, tax, payment fee, discounts, currency, and rounding are recomputed server-side | Admitted authoritative subtotal through total; client expected pricing is only a stale-state guard |
| Final checkout | `POST /api/storefront/checkout` | Idempotency key plus server-owned tenant, owner, cart/session, shipping resolution, payment configuration, pricing, and inventory checks | Deterministic order/payment response or classified safe error with trace ID |

No reviewed path accepts browser authority for tenant, customer, cart ownership, product price, subtotal, shipping price, warehouse, inventory, payment amount/finality/provider state, order identity, or order financial state.

## 4. Dynamic field and attachment proof

The existing rule engine remains the single Storefront evaluator. Precedence is:

`Disabled -> Hidden -> Conditional rule -> Visible -> Validation`

The server independently re-evaluates submitted fields. Disabled or currently hidden fields are rejected as submitted business data rather than trusted. Required validation only applies to effective visible fields. Text, textarea, number, date, select, radio, checkbox, image, file, and multi-image definitions retain typed validation, labels, Arabic/English presentation, and help/error metadata.

C1/C1.1/C1.2 behavior remains intact:

- Upload leases are scoped and expiring, and the development binary endpoint requires lease-token authority across the CSRF boundary.
- MIME type, byte size, field type, maximum file count, tenant, owner/session, and cart are revalidated.
- Confirmation is single-use; duplicate, expired, wrong-field, wrong-cart, wrong-session, and cross-tenant operations fail closed.
- Refresh retains only cart-bound finalized pending attachment references.
- Cart mutation or cart-ID swap aborts active transfers and removes stale attachment authority from checkout state.
- Abandoned pending objects are cleanup-eligible; Order-attached objects are protected.
- C3 ordering is preserved: attachment claims occur only after the Order row has been inserted.

## 5. Proven C4 defects and remediation

### Storefront request and state defects

1. **Concurrent cart hydration did not return the real in-flight request.** A second caller could proceed before hydration completed. `cartStore` now coalesces callers onto one promise and fences stale results.
2. **Checkout mount work could run on persisted/stale cart state.** Session creation, shipping, and pricing now wait for fresh cart hydration. Analytics no longer controls checkout initialization.
3. **Payment EMPTY/error could refetch indefinitely.** Automatic payment loading now starts only from `IDLE`; retry remains explicit. Requests coalesce, abort, and admit only the latest response.
4. **Shipping selection responses could arrive out of order.** Selection now clears previous warehouse authority before the request, uses abort/sequence fencing, exposes loading, and ignores late results.
5. **Step validity did not require current warehouse authority.** Progression and submit now require a resolved, unexpired, cart-bound checkout-session resolution and no selection/upload in flight.
6. **Uploads were not globally represented in checkout validity.** Active uploads now block navigation and submission; unmount/cart change aborts them.
7. **An empty dynamic-field configuration reactivated legacy fields.** Authoritative `EMPTY` remains empty. Hidden system fields are neither validated nor submitted.
8. **Persisted sensitive field values were not cart-bound.** Attachment values now carry `checkoutDataCartId`; cart changes prune them while retaining safe customer text/address input.
9. **Stale-session recovery could loop.** Preview, selection, and final submission perform bounded session recovery and require new authority before retry.
10. **Pricing and shipping issued avoidable duplicate calls.** Same-state work coalesces; changed inputs cancel the prior call; aborts are silent.
11. **Final payment selection was not refreshed immediately before checkout.** Submit now reloads availability and requires `AVAILABLE` plus an admitted selected method.
12. **Several technical failures had poor customer recovery guidance.** The Storefront maps stale pricing, shipping changes, conflicts, promotion failures, and network uncertainty to actionable retry/review messages while preserving checkout input.

### Backend authority, lifecycle, and observability defects

13. **Configured Paymob could be displayed when its execution runtime was disabled or incomplete.** Storefront availability now intersects the tenant read model with global orchestration readiness. Stripe and Paymob require enabled, configured runtime; Paymob configuration also checks credentials, callback, and adapter capability. Runtime now exposes COD and Manual Wallet only.
14. **Late successful payment replay could bypass the explicit quarantine path.** Quarantine terminal-state handling now precedes the general stale-success rejection and preserves the original reservation failure code/reason. Replay is deterministic.
15. **PostgreSQL order lock queries omitted the mapped `xmin` concurrency token.** Both raw order-lock projections now use `SELECT xmin, *`, preventing EF projection failure during real PostgreSQL payment/inventory lifecycle work.
16. **COD lifecycle mutation attempted to resolve a PaymentIntent that intentionally does not exist.** Offline collection is now recognized as order-only and skips PaymentIntent resolution.
17. **Known checkout errors could collapse into a generic failure and lacked complete diagnostic scope.** The controller now scopes `operation`, tenant ID, cart ID, checkout-session ID, and trace ID; maps approved 400/409/503 contracts; and reserves `500 UNEXPECTED_CHECKOUT_FAILURE` for unexpected faults. Root exceptions remain in server logs while secrets, tokens, uploaded bytes, and raw sensitive data remain excluded.

## 6. Session, concurrency, and transaction findings

### Session scenarios

- **Fresh checkout:** one cart-bound session is created after hydration.
- **Refresh:** active compatible session is resumed; sensitive state remains cart-bound.
- **Cart mutation:** shipping authority, pricing, and dependent availability are invalidated/refreshed.
- **Cart-ID swap:** old session, shipping, pricing, and attachments are removed or aborted.
- **Expiration:** stale session authority is rejected and bounded recovery creates/resumes valid authority.
- **Concurrent tab:** optimistic session versioning rejects stale writes; final idempotency and cart/order uniqueness collapse duplicate submission.
- **Retry:** the stable checkout ID/idempotency key returns the stored deterministic result; a genuinely changed request receives a conflict.

### Final transaction order

The final service performs validation before entering its serializable write transaction. Inside the transaction it:

1. acquires cart/order/idempotency authority and resolves inventory rows,
2. creates inventory reservations under concurrency control,
3. inserts the Order and OrderItems and flushes the Order identity,
4. validates and claims finalized checkout attachments,
5. writes checkout-field, attachment, shipping, and financial snapshots,
6. creates payment state where the method requires a PaymentIntent,
7. writes durable outbox/idempotency records,
8. commits once and records/replays the deterministic checkout result.

Failures before commit roll back order, attachment claims, payment state, inventory reservations, and outbox writes together. The active-order/cart uniqueness guard and durable idempotency claim prevent two successful Orders, two active PaymentIntents, or duplicate active inventory reservation side effects.

## 7. Pricing, shipping, payment, and inventory conclusions

- Order Summary uses the normalized cart response once at the API boundary. It does not reload catalog data or recalculate product/financial authority.
- Product snapshot name, image, variant, SKU, quantity, unit price, line total, discounts, totals, and currency come from the cart contract.
- Shipping selection submits `shippingMethodId`; warehouse resolution ID/version comes from the server. Browser `warehouseId` is never authoritative.
- Preview and final checkout use the same pricing service and rounding/currency rules. `ExpectedPricing` only detects stale UI state.
- Order totals and required PaymentIntent amounts are created from final server pricing.
- COD is order-only; Manual Wallet uses its defined pending/manual-review lifecycle. Stripe, Paymob, and Fawry were inspected and regression-tested without live provider calls.
- Inventory reservation uses the fulfillment warehouse resolution, product/variant, and authoritative cart quantity. Row/database concurrency and transaction rollback prevent double reservation and partial checkout artifacts.

## 8. Error and UX conclusions

- `400`: malformed requests, invalid fields, invalid shipping/payment selection, and stale expected data where defined.
- `401/403`: missing or invalid authenticated/guest ownership authority.
- `404`: unavailable scoped resources where non-disclosure semantics require it.
- `409`: idempotency mismatch, stale version, concurrent checkout, or authoritative state conflict.
- `422`: retained only where the existing business-validation contract already uses it.
- `503`: required dependent capability unavailable, including promotion calculation where policy fails closed.
- `500`: unexpected server failure only, with a safe public code and root exception retained in scoped server logs.

Buttons and step navigation are disabled during selection, upload, pricing refresh, and final submission. Double-clicks reuse the in-flight submission. Inline field/upload/shipping/payment errors expose a recovery action. Existing translated labels, RTL/LTR layout behavior, semantic inputs, and keyboard labels remain in place; C4 did not redesign checkout presentation.

## 9. Verification evidence

### Storefront

- Full Vitest run: **299/299 passed across 30 files**.
- Production build: **passed** (`tsc` and Vite).
- Build warnings are non-blocking: a roughly 1.27 MB chunk, mixed static/dynamic imports, stale Browserslist data, and deprecated Vite/esbuild settings.
- ESLint could not be run because the repository uses ESLint 9 without an `eslint.config.*` file. TypeScript compilation and the full Storefront test suite passed.

### Backend and database

- Checkout/session bounded suite: **146/146 passed**.
- Payment/inventory lifecycle suite: **52/52 passed**.
- Real PostgreSQL COD lifecycle: **13/13 passed**.
- Inventory lifecycle: **18/18 passed**.
- Payment/Manual Wallet/availability batch: **53/53 passed**.
- Checkout field definitions: **20/20 passed in a fresh process**.
- Checkout architecture certifications: **93/93 passed**.
- Fawry authority certifications: **4/4 passed**.
- Solution build: **passed with 0 warnings and 0 errors**.
- EF pending-model check: **no pending model changes**.
- EF migration list is current through `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N` with no pending marker.

A broad historical cross-system wildcard run produced 224 passes and 46 failures, primarily old marketplace/payout source-text certifications that reference removed migrations or prior implementation shapes. Direct checkout-related stale Fawry assertions found by that run were corrected and pass in a fresh process. The remaining failures are test-infrastructure debt outside Storefront Checkout and are not hidden by this report.

## 10. Runtime readiness

The rebuilt services are left running for final verification:

- Storefront built artifact/proxy: `http://127.0.0.1:5174/`
- The local proxy supplies the verified `shopixy.localhost:5174` forwarded host to the API, so tenant resolution works from the loopback URL without a hosts-file change.
- API: `http://127.0.0.1:5010/`

Runtime probes:

- Storefront `/checkout`: `200`.
- Compiled Storefront JavaScript asset: `200`.
- API `/health/live`: `200 Healthy`.
- Storefront `/api/storefront/store-config` through the direct loopback URL: `200`, tenant `shopixy`, currency `EGP`, status `live`.
- Guest `GET /api/storefront/cart` through the direct loopback URL with a fresh session ID: `200`.
- Storefront `/api/storefront/payment-methods` through the direct loopback URL: `AVAILABLE`, exactly `COD` and `MANUALWALLET`; Paymob is correctly suppressed because its execution runtime is not ready.
- `/health/ready`: `Unhealthy` only because optional/deployment capabilities are not configured in this development runtime: R2, Paymob, Stripe, Fawry, Manual Payment operational readiness, certificate provider, and Caddy edge.

The readiness result does not invalidate the Development attachment path or COD checkout proof. It does mean this workstation is not a production-ready deployment for those optional providers. The final live Place Order should use COD first, then Manual Wallet if its operational wallet configuration is intentionally completed. No live order was placed during C4.

## 11. Final disposition

The Storefront Checkout lifecycle is internally coherent at its defined boundaries. All C4 defects proven in cart/session sequencing, stale state, request races, dynamic fields, attachments, Storefront payment availability, PostgreSQL lifecycle locking, COD lifecycle handling, error classification, and observability were remediated and regression-covered.

The subsystem is ready for the final controlled Live Verification / COD Place Order test against the running development stack. Production deployment readiness remains contingent on configuring the explicitly unhealthy optional providers and edge/storage capabilities.

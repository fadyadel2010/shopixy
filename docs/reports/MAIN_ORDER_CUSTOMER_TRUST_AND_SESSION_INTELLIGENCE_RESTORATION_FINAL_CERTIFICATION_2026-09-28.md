# MAIN Order Customer Trust And Session Intelligence Restoration Final Certification - 2026-09-28

## 1. Verdict

**PASS for the scoped customer-trust and order-scoped session-intelligence restoration.**

The canonical Storefront Admin order detail now displays tenant-scoped customer trust evidence and an order-bound analytics journey without accepting a client session identifier or exposing the raw session ID. Phone block, unblock, and spam-report commands derive the target phone from the tenant-owned Ecommerce order. English and Arabic/RTL responsive presentation, available and unavailable journey states, optional-service failure isolation, builds, focused tests, and live read-only browser behavior passed.

This is a scoped certification, not a global production-readiness statement. No delivery-success percentage, customer rating, or multi-admin warning threshold was added because the repository contained no authoritative persisted model or implemented rule for those semantics.

## 2. Restored Authority

### Customer Trust

`GET /api/admin/orders/{orderId}/customer-trust` now resolves an active canonical Ecommerce order for the current tenant and returns implementation-backed evidence only:

- Normalized order phone availability.
- Previous Ecommerce order count for the phone within the current tenant.
- Cancelled previous order count within the current tenant.
- Tenant spam-report count.
- Global spam-report count.
- Existing blocked-phone evidence.
- Whether the current tenant already reported this order as spam.
- Existing IP and phone velocity evidence.
- Risk level and evidence signals.

The restored risk signals are:

- `SEVERE_GLOBAL_SPAM`
- `GLOBAL_SPAM`
- `TENANT_SPAM`
- `HIGH_CANCELLATION_RATE`
- `FIRST_ORDER`
- `IP_VELOCITY`
- `PHONE_VELOCITY`

An order without any phone evidence returns `UNAVAILABLE`; unsupported delivery or rating conclusions are not synthesized.

Phone identity uses the same non-null precedence as canonical order details: POS customer phone, then Storefront account phone, then the immutable guest/order phone snapshot. Live certification found and remediated a defect where a Storefront account with a null phone prevented fallback to the captured guest snapshot. The corrected behavior is covered by an automated regression test and a live rerun of the affected order.

### Phone Commands

The canonical order-scoped commands are:

```text
POST /api/admin/orders/{orderId}/customer-phone/block
POST /api/admin/orders/{orderId}/customer-phone/unblock
POST /api/admin/orders/{orderId}/customer-phone/report-spam
```

The backend derives the phone from the tenant-owned order. The Admin client never submits a phone number for these commands. The legacy spam request retains `PhoneNumber` for request compatibility only; the value is ignored and `OrderId` remains authoritative.

Spam reporting remains idempotent through the existing unique tenant/order report constraint. Cross-tenant order mutation is rejected by service authority tests.

### Session Intelligence

`GET /api/admin/orders/{orderId}/session-intelligence` derives the effective session internally from the posted Ecommerce order or its last-touch attribution. The client sends only the order ID.

The response contains:

- Order ID.
- Source and medium.
- Device.
- Visited pages.
- Duration.
- First visit, checkout visit, and purchase timestamps.
- Logical session count and event count.

The raw session ID is excluded from the Admin DTO and JSON serialization. Session mismatch and missing-purchase logs contain tenant and order IDs but not the session value. A missing eligible journey returns `204 No Content` and does not make the canonical order unreadable.

## 3. Canonical Customer Identity

Admin order detail mapping now includes Storefront account identity in addition to POS customer and guest identity:

- `POS_CUSTOMER`
- `STOREFRONT_ACCOUNT`
- `GUEST`

Name, email, and phone presentation follows the canonical order relationship and snapshot fallbacks. The identity label is localized in the order drawer.

## 4. Admin Experience

The canonical `PointOfSale.Web/src/pages/Orders.jsx` drawer now contains independent cards for:

- Customer trust evidence.
- Risk status and localized risk signals.
- Previous and cancelled order counts.
- Tenant and global spam evidence.
- Block/unblock and spam-report controls when a phone is available.
- Session source, medium, device, duration, event timing, session count, and visited pages.
- Loading, error, Retry, no-phone, and no-journey states.

Trust and session queries are independent from the core order-detail query. A simulated session endpoint `503` rendered an isolated error and Retry action while the order and customer-trust cards remained readable.

## 5. Automated Verification

| Gate | Result |
|---|---|
| Release solution build | PASS: 338 existing repository warnings, 0 errors |
| Focused backend tests | PASS: 9 passed, 0 failed, 0 skipped |
| Focused Admin tests | PASS: 2 files, 37 passed |
| Admin production build | PASS: 5,235 modules transformed |
| Locale JSON parsing | PASS |
| Scoped `git diff --check` | PASS; line-ending notices only |
| ESLint | UNAVAILABLE: ESLint 9 found no `eslint.config.js`, `.mjs`, or `.cjs` |

Focused backend command:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~PhoneRiskProfileServiceTests|FullyQualifiedName~AdminOrderSessionIntelligenceTests" --no-restore
```

The nine tests cover tenant-isolated phone status and history, server-derived spam identity, foreign-tenant mutation rejection, guest-snapshot fallback when an account phone is null, and suppression of raw session IDs from Admin serialization.

Focused Admin command:

```text
npm test -- --run src/services/adminOrdersApi.test.ts src/pages/Orders.test.ts
```

The 37 tests cover fixed order-scoped endpoints, `204` journey handling, response order matching, absence of client session IDs, absence of client-submitted phone numbers, and source-level UI authority/privacy contracts.

The Admin production build completed with the existing oversized-chunk warning. No lint configuration was introduced because repository-wide tooling migration is outside this restoration.

## 6. Live Runtime Evidence

### Environment

- API: `http://localhost:5010`, `/health/live` HTTP `200`.
- Admin: `http://localhost:5173`.
- Browser: headed Microsoft Edge through `playwright-cli`.
- Authenticated authority: existing Owner account in tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.
- Available canonical Storefront orders: 12.

Credentials, cookies, tokens, and CSRF values are not recorded in this report or repository. The temporary persistent browser profile was deleted after certification.

### Live Endpoint Coverage

The new trust endpoint returned HTTP `200` for all 12 existing canonical Storefront orders inspected. The session endpoint returned both supported outcomes across that set:

- Five orders returned HTTP `200` with an order-bound journey.
- Seven orders returned HTTP `204 No Content`.

A nonexistent order probe returned `404 ORDER_NOT_FOUND` from customer trust and no evidence from session intelligence. No cross-order data was returned.

The live session response for an available journey contained only the documented presentation fields. Its JSON property set did not contain `sessionId`.

### Available Journey

Order `ORD-83B3-2026-01033` was exercised read-only:

- Core detail HTTP `200`.
- Customer trust HTTP `200`.
- Session intelligence HTTP `200`.
- Fulfillment and payment authority reads HTTP `200`.
- Customer trust rendered Safe status, order counts, spam counts, the first-order signal, and available phone controls.
- Session Details rendered source/medium, device, duration, timestamps, session count, and the visited order page.
- No raw session identifier appeared in the response or rendered drawer.
- Final English desktop pass at `1440x1000` had no document or dialog horizontal overflow and no browser-console error.

### Unavailable Journey

Order `ORD-83B3-2026-01036` returned HTTP `204` from session intelligence while its core detail, trust, fulfillment, and payment reads remained HTTP `200`. The drawer rendered the localized no-journey state rather than failing or hiding order details.

### Phone Fallback Remediation

Before remediation, order `ORD-83B3-2026-01034` displayed a captured phone in canonical order detail but trust returned `hasPhone=false`. The cause was relationship-based selection that stopped at a Storefront account whose phone field was null instead of falling back to the order phone snapshot.

After the fix and API rebuild/restart:

- Customer trust returned HTTP `200`.
- `hasPhone` was `true`.
- The phone was normalized by the existing server normalizer.
- Safe trust evidence rendered.
- Block and Report Spam controls rendered.
- Session intelligence remained HTTP `200` and readable.

No phone action was submitted against shared data.

### Failure Isolation

A browser-local route fault returned HTTP `503` only for the session-intelligence request of `ORD-83B3-2026-01034`. The HTTP client performed its configured retries. The final drawer retained:

- Canonical order detail.
- Customer identity.
- Customer trust evidence.
- Totals, items, addresses, and notes.
- An isolated session error with Retry.

The route fault was removed before the final live pass; the endpoint then returned HTTP `200` again. This fault injection did not reach or mutate backend data.

### Arabic And Mobile

At `390x844`, the document reported `lang="ar"` and `dir="rtl"`. The Arabic drawer rendered localized customer trust, session details, phone actions, no-journey text, risk status, and journey fields. Neither the document nor the dialog had horizontal overflow.

## 7. Security And Privacy

- All reads require authenticated Owner, Admin, or Manager authority and trusted tenant context.
- Order lookup requires the current tenant, Ecommerce source, active row, and exact order ID.
- Trust history counts remain tenant-scoped.
- Phone mutations derive identity from server-side order evidence.
- The Admin client validates that trust and journey response `orderId` matches the requested route.
- Session resolution remains server-side.
- Raw session IDs are omitted from the Admin DTO, JSON, UI, and mismatch/not-found logs.
- Optional analytics failure does not weaken canonical SQL order readability.
- No database migration was introduced.
- No shared order, phone profile, spam report, or customer record was mutated during browser certification.

## 8. Files In Scope

Principal files include:

- `PointOfSale.API/Controllers/AdminOrdersController.cs`
- `PointOfSale.API/Controllers/AdminPhonesController.cs`
- `PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs`
- `PointOfSale.API/DTOs/Admin/AdminPhoneDtos.cs`
- `PointOfSale.API/Services/Admin/AdminOrderService.cs`
- `PointOfSale.API/Services/PhoneRisk/IPhoneRiskProfileService.cs`
- `PointOfSale.API/Services/PhoneRisk/PhoneRiskProfileService.cs`
- `PointOfSale.API/Services/Analytics/IAnalyticsSessionJourneyService.cs`
- `PointOfSale.API/Services/Analytics/AnalyticsSessionJourneyService.cs`
- `PointOfSale.Tests/PhoneRisk/PhoneRiskProfileServiceTests.cs`
- `PointOfSale.Tests/Analytics/AdminOrderSessionIntelligenceTests.cs`
- `PointOfSale.Web/src/services/adminOrdersApi.js`
- `PointOfSale.Web/src/services/adminOrdersApi.test.ts`
- `PointOfSale.Web/src/pages/Orders.jsx`
- `PointOfSale.Web/src/pages/Orders.test.ts`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

## 9. Limitations

- Block, unblock, and spam-report buttons were not submitted in the live shared tenant because they mutate persisted business data. Their server-derived identity, tenant rejection, idempotency, and client payload contracts are covered by focused automated tests.
- Browser certification used one Owner authority and Microsoft Edge; a complete Owner/Admin/Manager and cross-browser matrix was not run.
- Only evidence already implemented in the system was restored. Delivery-success percentages, customer ratings, and multi-admin warning thresholds remain intentionally absent pending an authoritative model and policy.
- ESLint remains unavailable until the repository adopts an ESLint 9 flat configuration.
- Existing solution analyzer/compiler warnings and the Admin oversized-bundle warning remain outside this scoped restoration.

## 10. Cleanup

- The browser-only fault route was removed before the final live pass.
- An unused temporary mock script was deleted without execution.
- The headed browser session was closed and its persistent profile deleted.
- No credentials or session material were persisted in repository files.
- No business-data cleanup was required because certification was read-only.
- The user-managed API was left running on port `5010`.
- Unrelated modified and untracked worktree content was preserved.

## 11. Final Decision

**PASS for scoped canonical customer-trust restoration, server-authoritative phone actions, order-scoped privacy-preserving session intelligence, localized responsive Admin presentation, focused regression coverage, and live read-only browser certification.**

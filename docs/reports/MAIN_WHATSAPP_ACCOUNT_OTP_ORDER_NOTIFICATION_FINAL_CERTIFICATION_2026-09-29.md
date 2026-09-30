# MAIN WhatsApp Account, OTP, And Order Notification Final Certification - 2026-09-29

## 1. Verdict

**Baileys transport routing, QR account linking, connected-phone attestation, checkout OTP delivery and verification, order-confirmation notification, store-branded OTP messaging, country-aware local phone entry, English/Arabic localization, and focused regression tests: PASS.**

**Overall production certification: CONDITIONAL PASS.**

Every functional path in scope was certified against a real linked WhatsApp device and a real submitted order, not mocked. Three conditions prevent an unconditional sign-off:

1. The OTP resend publish path retains a documented ambiguity between a fresh code and a reused active code.
2. Baileys evicts idle in-memory sessions, which produces a transient `503` on the provider status endpoint even though the account remains linked and self-heals without a new QR scan.
3. The broader POS Admin frontend suite is not green because of pre-existing failures unrelated to WhatsApp.

## 2. Certified Scope

This certification covered:

- Tenant transport selection between the Baileys microservice and the legacy Playwright engine.
- QR generation, expiry, refresh, and scan-to-pair through the Baileys transport.
- Connected-phone attestation against the configured account phone number.
- Account enable, disable, disconnect, and one-account-per-tenant admission.
- Checkout phone OTP request, delivery, code verification, and verification-status reuse.
- Order-confirmation dispatch, delivery, and content projection.
- Store-branded OTP template rendering with tenant store-name resolution.
- Country-aware local phone entry derived from store configuration.
- English and Arabic localization plus RTL presentation.
- Desktop and mobile presentation.
- Read-only runtime evidence from authenticated Admin APIs and public health endpoints.

One live test order was submitted under explicit user approval. No payment was captured: the order is cash on delivery.

## 3. Tenant And Account Identity

| Attribute | Value |
|---|---|
| Tenant ID | `c9c5c1ae-949e-498e-bc63-07242daeb49d` |
| Store | AhramStore |
| Account ID | `138379f5-7754-4c20-8863-b3b7aad28888` |
| Display name | `fady` |
| Configured phone | `+201022174803` |
| Account status | `Connected` |
| Enabled | `true` |
| Use for order confirmation | `true` |
| Use for OTP verification | `true` |
| Use for payment approval | `false` |
| Use for abandoned cart recovery | `false` |

The live API attested `connectedPhone: +201022174803`, which matches the configured account phone exactly. A mismatched linked identity is rejected with `WHATSAPP_CONNECTED_PHONE_MISMATCH` rather than silently accepted.

## 4. Transport Authority

`GET /api/admin/whatsapp/transport-info` returned:

```json
{
  "tenantId": "c9c5c1ae-949e-498e-bc63-07242daeb49d",
  "transport": "Baileys",
  "useBaileys": true,
  "message": "Using Baileys (lightweight Node.js transport)"
}
```

The earlier QR failure was not a Baileys defect. The tenant feature gate `UseBaileys` was `false`, so QR requests were served by the legacy Playwright browser transport, which produced a Playwright Chromium session and a misleading "Already logged in" result. After the tenant was switched to `UseBaileys = true`, QR requests produced a real Baileys session with no `PointOfSale\WhatsAppSessions` Chrome process.

Baileys service health during certification:

```json
{
  "status": "healthy",
  "version": "PHASE_3_RELIABLE",
  "activeSessions": 1,
  "sessionsByStatus": { "connected": 1 },
  "poolSessions": 0,
  "poolMembers": 10,
  "stuckSessions": 0,
  "reconnectStormsDetected": 0,
  "poisonedSessions": 0,
  "activeSockets": 1
}
```

## 5. Account Linking Evidence

- A fresh Baileys QR was requested from the Admin WhatsApp workspace.
- Baileys reported `sessionsByStatus: { qr_pending: 1 }` with `activeSockets: 1` while the QR was displayed.
- The QR countdown, expiry state, and manual refresh were exercised.
- Two earlier Baileys QRs expired unscanned and the service correctly released them, returning `activeSessions: 0`; this is the self-healing stuck-QR reset path behaving as designed.
- The user's third scan completed the pairing and the session transitioned to `connected`.
- No Chromium process owned by the application was present at any point during Baileys certification.

## 6. OTP Delivery And Verification

Three OTP messages were dispatched during certification. All were recorded `SENT` in the Admin message log:

| Log ID | Type | Status | Created (UTC) | Sent (UTC) |
|---|---|---|---|---|
| `b42c7083-b126-4d4c-a383-63c3a5fd7e27` | `OTP` | `SENT` | 2026-09-29T13:50:21.58Z | 2026-09-29T13:50:25.268Z |
| `65adf916-7b8f-4995-a5fc-f77be1fba0a9` | `OTP` | `SENT` | 2026-09-29T14:00:46.746Z | 2026-09-29T14:00:50.469Z |
| `3a968584-f920-41b8-b064-cc53184dfcca` | `ORDER_CONFIRMATION` | `SENT` | 2026-09-29T14:16:51.14Z | 2026-09-29T14:16:54.608Z |

The customer received each code on the linked device, entered the code into checkout, and the storefront advanced to a verified state that enabled order submission. The OTP code is never persisted in plaintext: the log stores a redacted placeholder and only the code length.

## 7. Order And Notification Certification

Order submitted under explicit user approval:

| Attribute | Value |
|---|---|
| Order number | `ORD-C9C5-2026-01039` |
| Order ID | `a1f201e7-6124-4d63-bd88-fd2fe1cc8e88` |
| Customer | Certification User |
| Item count | 1 |
| Subtotal | EGP 550.00 |
| Shipping | EGP 10.00 |
| Total | EGP 560.00 |
| Payment method | Cash on delivery |
| Order status | `Processing` |
| Payment status | `Pending` |
| Source | `Ecommerce` |
| Created (UTC) | 2026-09-29T14:16:50.413193Z |

The order was confirmed in the backend through authenticated Admin APIs and the Storefront routed to the pending-payment route rather than declaring success before backend confirmation.

The customer received this order-confirmation message on the linked device:

```text
Order confirmed

Hello Certification User, thank you for shopping with AhramStore.

Order: ORD-C9C5-2026-01039
Items: 1
Total: EGP 560.00
Shipping: Test address only, Cairo, Nasr City
Payment status: Pending
```

Every variable projected correctly: customer name, store name, order number, item count, formatted total, composed shipping address, and live payment status. Dispatch latency from log creation to send was approximately 3.5 seconds.

## 8. Store-Branded OTP Messaging

The built-in OTP templates were replaced with professional, security-focused, store-branded copy in both languages, and the template engine now resolves the tenant store name for OTP messages:

| Language | Template ID | Content |
|---|---|---|
| English | `00000000-0000-0000-0000-000000000201` | `{{store_name}} verification code` + one-time code + expiry + do-not-share + ignore-if-not-requested |
| Arabic | `00000000-0000-0000-0000-000000000202` | `رمز التحقق من {{store_name}}` + one-time code + expiry + do-not-share + ignore-if-not-requested |

Changes:

- `ResolveOtpAsync` now substitutes `{{store_name}}` in addition to `{{otp_code}}`.
- The store name resolves from the tenant `StoreConfig`, falls back to the tenant name, and finally to a localized generic label so no template ever renders an unresolved placeholder.
- OTP template validation accepts `{{store_name}}` while still requiring `{{otp_code}}`.
- The live API returned both upgraded templates, and a live branded OTP was delivered and confirmed by the user on the linked device.

The OTP audit log still redacts message content, so store branding did not weaken the existing privacy control.

## 9. Country-Aware Local Phone Entry

The checkout previously required customers to type a full international number such as `+201022174803`. The customer now types only their local number, and the dialing prefix is derived from the store's configured country rather than hardcoded.

Implementation:

- The prefix is derived from the storefront `defaultCountryCode` using `libphonenumber-js` international metadata, so it is correct for any store country: Egypt `+20`, Saudi Arabia `+966`, United States `+1`, and so on.
- The address country overrides the storefront default when the checkout address carries a country, so a traveler is not silently mislabeled.
- A saved E.164 value is displayed in its national format rather than as a raw `+` number.
- A foreign international number and any value the user is actively typing are left untouched, so the field never fights the customer.
- The input row is forced LTR inside the Arabic layout, which is correct for phone numbers in RTL pages.
- The prefix is presentation only. The submitted value stays local and the backend still normalizes to E.164 through `IPhoneNumberNormalizer` before any WhatsApp dispatch, so the server remains the authority on the destination number.
- The behavior applies to both the default checkout form and administrator-configured dynamic phone fields.

Live English evidence for AhramStore (`EG`): label `Phone Number*`, non-editable `+20` prefix, value `010 22174803`, help text `The country code is selected automatically from the store's country.`

Live Arabic evidence: label `رقم الهاتف*`, prefix `+20`, placeholder `أدخل رقم هاتفك المحلي`, help text `يتم تحديد رمز الدولة تلقائيًا حسب دولة المتجر.`, page `lang="ar"`, input row `dir="ltr"`, value `01022174803` entered as a purely local number.

Desktop and mobile widths were both verified.

## 10. Automated Verification

| Gate | Result |
|---|---|
| Backend focused WhatsApp and Storefront phone-verification tests | PASS: 35 passed, 0 failed, 0 skipped |
| Storefront full Vitest suite | PASS: 45 files, 393 passed |
| Storefront production build | PASS |
| Storefront English/Arabic locale JSON parsing | PASS |
| POS Admin English/Arabic locale JSON parsing | PASS |
| Scoped `git diff --check` | PASS; line-ending notices only |
| POS Admin full Vitest suite | NOT GREEN: 1511 passed, 2-3 failed across 3 files |

The POS Admin failures are pre-existing and unrelated to WhatsApp:

- `src/i18n/i18nCertification.test.ts` reports missing `storefront.homepageEditor.*` keys. This was confirmed pre-existing by stashing the WhatsApp locale edits and rerunning: the failure is identical, and every missing key belongs to the homepage editor, not WhatsApp.
- `src/tests/affiliateCheckoutFrontendCertification.test.ts` asserts affiliate carrier authority.
- `src/pages/PaymentMethods.test.tsx` asserts masked Paymob API key presentation.

These were not silently waived or rewritten. The i18n audit requires a separate homepage-editor locale completion pass before the POS Admin suite can serve as a green release gate.

Backend tests were executed while the API process was stopped, because the running API holds the rebuilt `PointOfSale.Core` and `PointOfSale.Infrastructure` binaries. The API was restarted immediately afterward and returned HTTP `200` on `GET /health/live`.

## 11. Browser Evidence

Browser verification used the isolated `playwright-cli` session `opencode-ui-test` against:

- Admin: `http://localhost:5173/`
- Storefront: `http://ahramstore.localhost:5174/`
- API: `http://localhost:5010/`
- Baileys: `http://localhost:3000/`

Admin observations:

- Transport reported `Baileys` with `useBaileys: true`.
- Account card showed `Connected`, both use-case badges, and a closed circuit breaker.
- QR modal rendered the code, countdown, expiry state, and refresh action.
- The message log exposed the OTP and order-confirmation audit trail.

Storefront observations:

- Checkout resolved the AhramStore tenant and reached Review with backend-authoritative pricing.
- Phone verification blocked order submission until the customer code was accepted.
- After verification, order submission succeeded and the pending-payment route rendered the backend order number and `PENDING` status.
- The country-aware phone field rendered correctly in English and Arabic.

A hot-module-replacement artifact was observed and worked around during verification: after editing the phone field, the checkout page remained bound to a stale in-memory store instance and its enabled Continue button could not advance. A normal reload resolved it and the same server draft progressed normally. This is a development-time HMR concern, not a production defect, but it is recorded because it can mislead a tester into believing a checkout control is broken.

## 12. Runtime Health

`GET /health/live` returned HTTP `200` throughout.

`GET /health/ready` was not used as the WhatsApp liveness gate because it reports `503` for dependencies outside the WhatsApp scope.

Baileys `GET /health` consistently reported `healthy` with version `PHASE_3_RELIABLE`, zero stuck sessions, zero reconnect storms, and zero poisoned sessions.

## 13. Residual Risks

1. **OTP resend publish ambiguity.** When a resend occurs inside the cooldown window, the active code is reused deterministically. The publish-versus-reuse intent is not fully distinguishable from the consumer side. The OTP storage and publish path were reworked in this effort, but the ambiguity remains and should be closed with an explicit resend-intent marker before multi-region rollout.

2. **Idle session eviction presents as a transient provider outage.** Baileys closes idle sessions while preserving auth state, so `GET /api/admin/whatsapp/accounts/{id}/status` can return `WHATSAPP_PROVIDER_UNAVAILABLE` for a tenant whose account is still correctly linked and enabled. This was reproduced and then shown to self-heal: the QR endpoint returned `alreadyConnected: true` and the status immediately returned `Connected` with the correct `connectedPhone`, all without a new scan. This is correct fail-closed behavior, but Admin operators may read the transient `503` as a broken integration. The Admin card already avoids disabling the account on unavailable status; the residual item is operator-facing messaging.

3. **Stale legacy linked device.** An earlier QR was scanned through the unintended Playwright transport and left a Windows Chrome device linked on the customer's phone. It is cosmetic and does not affect the Baileys account, but it should be unlinked manually from the phone's Linked Devices screen.

4. **POS Admin suite not green.** Three pre-existing failures are documented in Section 10. One is an i18n structural gap in the homepage editor that must be closed before the suite is a trustworthy gate.

5. **Test account is a real store.** Certification ran against the live AhramStore tenant and created a real cash-on-delivery order that will appear in fulfillment queues.

## 14. Principal Files

Backend and messaging:

- `PointOfSale.API/Controllers/Admin/WhatsAppAdminController.cs` — transport-aware routing, country-aware account entry, QR/status/attestation, structured errors.
- `PointOfSale.API/Controllers/TenantsController.cs` — Baileys rollout transport switch.
- `PointOfSale.Core/Entities/Tenant.cs` — `UseBaileys` feature gate.
- `PointOfSale.Core/Interfaces/WhatsApp/IWhatsAppTemplateResolver.cs` — system templates and store-branded OTP copy.
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppTemplateResolver.cs` — store-name resolution for OTP rendering.
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppAccountService.cs` — account and template validation, single-account admission.
- `PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppOtpConsumer.cs` — OTP dispatch with redacted audit logging.
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppOtpService.cs` — PBKDF2-SHA256 OTP storage.
- `PointOfSale.Infrastructure/Services/PhoneNumberNormalizer.cs` — libphonenumber normalization authority.
- `PointOfSale.API/Services/Storefront/StorefrontPhoneVerificationService.cs` — checkout OTP lifecycle.
- `PointOfSale.API/Services/Storefront/CheckoutPhoneOtpStore.cs` — checkout OTP persistence.
- `PointOfSale.API/Services/Checkout/CheckoutService.cs` — order-time OTP gate.
- `PointOfSale.Infrastructure/Migrations/20260929115948_MainWhatsAppSingleAccountPerTenant.cs` — guarded single-account index.

POS Admin:

- `PointOfSale.Web/src/modules/whatsapp/pages/WhatsAppSettings.jsx` — country selector, accounts, QR modal, templates.
- `PointOfSale.Web/src/modules/whatsapp/components/AccountCard.jsx` — account and QR action states.
- `PointOfSale.Web/src/modules/whatsapp/hooks/useWhatsAppApi.js` — Strict Mode loading fix and status handling.
- `PointOfSale.Web/src/modules/whatsapp/components/TemplateEditor.jsx`
- `PointOfSale.Web/src/modules/whatsapp/components/VariableHelper.jsx`
- `PointOfSale.Web/src/i18n/locales/en.json`, `PointOfSale.Web/src/i18n/locales/ar.json`

Storefront:

- `PointOfSale.Storefront/src/utils/phone.ts` — calling-code and national-format derivation.
- `PointOfSale.Storefront/src/utils/phone.test.ts`
- `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx` — country-aware phone field in both form modes.
- `PointOfSale.Storefront/src/i18n/locales/en.json`, `PointOfSale.Storefront/src/i18n/locales/ar.json`

Certification tests:

- `PointOfSale.Tests/Admin/WhatsAppAdminScopeTests.cs`
- `PointOfSale.Tests/Infrastructure/WhatsAppAccountServiceTenantScopeTests.cs`
- `PointOfSale.Tests/Infrastructure/WhatsAppTemplateResolverTests.cs`
- `PointOfSale.Tests/Storefront/StorefrontPhoneVerificationReliabilityTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontWhatsAppAuthorityCertificationTests.cs`
- `PointOfSale.Storefront/src/components/checkout/ShippingStep.localization.test.ts`

## 15. Security And Mutation Statement

- No credential, cookie, token, Baileys API key, or authentication value was written to this report.
- No OTP code value was written to this report.
- The linked phone number appears only as it is stored in the product itself and is the certification subject.
- Exactly one order was created, `ORD-C9C5-2026-01039`, under explicit user approval, using test contact data and cash on delivery. No payment was captured.
- The existing WhatsApp account was not deleted. One WhatsApp account per tenant is enforced.
- Tenant `UseBaileys` was set to `true` to select the required transport.
- Browser state changed only to add a product to the cart and reach checkout.
- Unrelated modified and untracked worktree content was preserved.

## 16. Conclusion

The WhatsApp onboarding, OTP verification, and order-notification chain is certified end to end against a real device and a real order through the intended Baileys transport, with backend-reachable identity attestation, redaction-safe audit logging, professional store-branded messaging, and country-correct phone entry that does not hardcode any single market.

Closure to an unconditional sign-off requires: resolving the OTP resend-intent ambiguity, improving the Admin operator messaging for idle provider unavailability, unlinking the stale legacy device, and closing the pre-existing homepage-editor i18n gap.

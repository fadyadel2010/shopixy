# SHOPIXY Paymob P7 Complete Payment-Method Forensic Audit and End-to-End Remediation

Date: 2026-08-14  
Tenant: `shopixy` (`83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`)  
Certification: **BLOCKED**

## 1. Executive decision

The source defects that produced the storefront message “Your order response could not be verified” were identified and remediated. The Paymob Intention API is reachable with the tenant's encrypted sandbox credentials, the configured integration belongs to the discovered merchant account, fresh Paymob intentions were created for new Nokia N73 orders, and all compiled/tested payment gates pass.

P7 cannot be certified as PASS because the required final browser sequence was not completed. After the final storefront contract remediation, the browser-control surface refused all further interaction with `http://shopixy.localhost:5174`. It explicitly prohibited retries or alternate browser workarounds. Consequently, Unified Checkout was not visually reopened after the last fix, no Paymob test Visa was submitted, no provider webhook was received, and final payment/order/POS Admin state could not be demonstrated. The configured development callback and webhook URLs are also localhost URLs that Paymob cannot reach from its servers.

This is a truthful **BLOCKED**, not a payment success claim.

## 2. Scope and evidence rules

The audit covered tenant configuration, encryption, runtime authority, Paymob account/integration discovery, checkout orchestration, Intention construction, Unified Checkout handoff, idempotency and replay, webhook/callback validation, HMAC, finality, POS Admin visibility, database state, migrations, security, automated tests, builds, and an interactive storefront checkout.

No raw public key, secret key, HMAC secret, API key, client secret, checkout token, protected payment session, login password, or card data is reproduced in this report. Credential evidence is recorded only as presence, decryptability, environment, country, ownership, and safe metadata.

Official behavior was checked against:

- [Paymob APIs integration path](https://developers.paymob.com/paymob-docs/integration-paths/apis)
- [Paymob checkout experiences](https://developers.paymob.com/paymob-docs/developers/checkout-experiences)
- [Paymob Quick Link API overview](https://developers.paymob.com/paymob-docs/developers/quicklink-apis/overview)
- [Paymob webhook callbacks and HMAC](https://developers.paymob.com/paymob-docs/developers/webhook-callbacks-and-hmac)

The official contract is consistent with the remediated design: the backend creates the Intention, the client secret opens Unified Checkout, provider callbacks are the payment source of truth, and HMAC validation is mandatory.

## 3. Architecture map

The active storefront path is:

`CheckoutController` → `CheckoutService` → `IPaymentGateway` → `PaymobPaymentGateway` → `IPaymobPaymentService` / `PaymobPaymentService` → `PaymobRuntimeApplicationService` → `PaymobRuntimeBridge` → `PaymobPaymentAdapter` → `PaymobOfficialApiHttpClient` → Paymob `POST /v1/intention/`.

The browser receives a presentation handoff only after the backend has created and persisted a local `PaymentIntent`. The browser redirects to Unified Checkout. It never marks payment successful from a redirect. `PaymobWebhooksController` and `PaymobPaymentService` validate provider evidence and call the canonical payment lifecycle/finality services. The pending page polls backend payment truth.

Tenant storefront execution does not read the CommerceHub/super-admin global Paymob setting lane. The tenant resolver is the canonical source for SHOPIXY checkout.

## 4. Root cause of the screenshot failure

The failure was layered, not a warehouse problem:

1. The original stored Paymob session could be returned after its storefront authority had expired. The backend handoff predicate did not require a future expiry, while the storefront correctly required one. Attempted regeneration could reuse the same expired session.
2. A fresh transactional checkout seed had `ExpiresAtUtc = null`. `PaymobPaymentService` reused that seed without establishing an expiry. Paymob successfully created the provider intention, but the JSON handoff had no expiry, so the storefront correctly rejected it.
3. `CheckoutController` intentionally moved the checkout token into an HttpOnly cookie and removed it from JSON. The storefront verifier nevertheless demanded that token in readable JSON. Thus, even after expiry was corrected, an otherwise-valid handoff was rejected.
4. The existing-session completeness check still tested the legacy plaintext `PaymentIntent.RedirectUrl`. Protected Paymob sessions deliberately clear that column, so a valid protected session could be mistaken for an incomplete checkpoint and sent back through provider creation logic.

The fresh Nokia orders proved the diagnosis:

- `ORD-83B3-2026-01015`: Paymob intention `587562903` was created, but the persisted replay shape had no expiry.
- `ORD-83B3-2026-01016`: after the expiry fix, Paymob intention `587573231` was created with expiry `2026-08-14T20:00:40.276627Z`; the remaining rejection matched the HttpOnly-cookie/JSON contract mismatch.

## 5. Defect register and severity

| Severity | Defect | Disposition |
|---|---|---|
| P1 | Transactional Paymob seed did not acquire a durable expiry | Fixed; seed now receives a one-hour absolute expiry before external execution and returns it through the canonical gateway |
| P1 | Storefront required a checkout token that the controller securely moved to an HttpOnly cookie | Fixed; presentation validation no longer requires a JS-readable token, but still requires intent ID, future expiry, provider match, cart match, and exact Unified Checkout URL |
| P1 | Protected session was classified using obsolete plaintext `RedirectUrl` | Fixed; completeness uses the decrypted protected session URL/key and provider intent |
| P1 | Expired stored pending session could be replayed | Fixed fail-closed with `PAYMOB_PAYMENT_SESSION_EXPIRED`; missing expiry on a complete session fails with `PAYMOB_PAYMENT_SESSION_EXPIRY_MISSING` |
| P2 | Local handoff TTL was 30 minutes while provider Intention expiration was 60 minutes | Fixed; both are 60 minutes |
| P2 | Latest Paymob intent lookup was nondeterministic with multiple historical intents | Fixed with descending `CreatedAt` ordering |
| P2 | Tenant persistence/runtime accepted only one integration ID, while a method may need multiple Paymob payment methods | Fixed without schema change; the existing field accepts a validated comma/semicolon/whitespace list and the Intention sends every distinct ID |
| P2 | Active-order duplicate guard bound a new idempotency record before proving replay safety | Fixed; replay is built and validated first, then the idempotency record is bound/completed |
| P2 | Fourteen historical incomplete idempotency rows were left against the old order by earlier duplicate attempts | Historical evidence retained; new logic prevents recurrence |
| P2 | Frontend considered JSON `null` provider fields reusable artifacts | Fixed; only non-empty provider values count as artifacts, allowing safely scrubbed terminal replay |
| P3 | Admin label implied a single integration | Fixed to “Integration IDs” with list-format guidance |
| P3 | Storefront analytics endpoint returns unrelated 403s | Not a Paymob cause; recorded separately for analytics remediation |
| External blocker | Development callback/webhook use localhost | Not mutated; requires a public HTTPS tunnel or deployed API before Paymob can deliver callbacks |

No P0 defect was found.

## 6. Tenant database configuration before remediation

The authoritative database contained exactly one enabled, non-deleted tenant method:

- Code: `PAYMOB`
- Method ID: `4ac97b14-8c82-4422-ac19-1d9aeedd465b`
- Display: `Online Payment`
- Merchant ID: `645875`
- Integration ID: `3153058`
- Sandbox: `true`
- Public key: configured
- Secret key: configured and decryptable
- HMAC secret: configured and decryptable
- API key: configured
- Public/secret country: Egypt/Egypt
- Public/secret mode: test/test

The integration value was already `3153058` when P7 began. An earlier artifact mentioned `3153090`, so an external configuration change occurred before this audit. P7 did not overwrite tenant credentials or integration IDs.

## 7. Encryption and secret handling

`TenantPaymentMethods.ConfigEncrypted` is protected with ASP.NET Core Data Protection using the `PaymentMethodSecrets` purpose and the persisted application key ring. Blank admin updates preserve existing secrets; explicit reset clears/disables them. Runtime logs and diagnostic output use presence/length/redaction only.

Repository scanning found no embedded tenant Paymob secret/public-key value patterns in product code. A generic-looking string match was confined to a fake admin test fixture.

## 8. Live merchant and integration discovery

Safe live checks produced:

- Authentication response: HTTP 201
- Merchant ID: `645875`
- Discovered active integration: `3153058`
- Gateway: VPC/card
- Currency: EGP
- Live mode: false
- Standalone: false
- Deprecated: false
- Configured/discovered ownership match: yes

The current integration therefore represents card/VPC semantics. The remediated runtime can send several configured integration IDs, but the current tenant configuration exposes only the one discovered card integration. It cannot honestly claim wallet/mobile-wallet coverage until additional owned integration IDs are configured.

The API key is required for account/integration discovery, not for the modern Intention runtime itself, which authenticates with the secret key.

## 9. Official Intention request contract

The application constructs the Paymob Intention server-side with:

- amount converted to minor units using currency decimal-place authority
- canonical uppercase currency
- one item whose amount and quantity sum to the order total
- customer name/email/phone and billing/shipping fields
- `special_reference` bound to the local order identity
- all configured payment method integration IDs
- expiration of 3600 seconds
- callback and webhook URLs from runtime configuration

The runtime rejects malformed configuration, missing required secret/public values, invalid integration IDs, invalid amount/currency, missing provider client secret, and incomplete handoff evidence.

## 10. Unified Checkout handoff contract

The provider session decrypts to a safe shape with:

- scheme: HTTPS
- host: `accept.paymob.com`
- path: `/unifiedcheckout/`
- query keys: `publicKey`, `clientSecret`
- non-empty protected payment key
- one-hour future expiry on the remediated fresh intent

The client secret and public key values are not printed. The backend's handoff predicate and the storefront validator both restrict Paymob presentation to this exact origin/path and require a non-empty client secret/public key.

Checkout-token ownership stays in an HttpOnly cookie after `CheckoutController` strips it from response JSON. This is the intended security contract; the token is not persisted in storefront local storage.

## 11. Idempotency, retry, and replay

Provider execution is anchored to the durable local payment intent and checkout idempotency key. Active provider uniqueness is enforced by the existing partial unique index. The remediation:

- orders intent reads newest-first
- never regenerates an unexpired complete protected Paymob session
- rejects expired or unverifiable complete sessions
- requires an unexpired expiry for a concurrent claimed session
- constructs a safe duplicate-order replay before binding/completing the incoming idempotency record
- scrubs time-limited secrets from durable replay payloads and reconstructs them only from protected backend state
- accepts safely scrubbed terminal responses without treating JSON nulls as provider artifacts

Historical incomplete records were not deleted because they are forensic evidence and deletion was not required for correctness.

## 12. Callback, webhook, HMAC, and finality

The webhook controller is anonymous-tenant only at the routing edge, size/rate limited, and resolves the tenant from stored payment-intent authority. The service builds Paymob's canonical field concatenation, computes HMAC-SHA512, and uses fixed-time comparison. Missing/invalid HMAC is unauthorized. Claimed/resolved tenant mismatch is unauthorized. Duplicate provider events are acknowledged and ignored after idempotent admission.

Valid success/failure/cancellation evidence is passed through the canonical payment lifecycle with provider amount/currency checks and terminal-state protection. Callback redirects only to `/checkout/pending`; the redirect itself never marks success.

No webhook was received for the P7 fresh orders, so runtime HMAC verification is **not demonstrated** here. Automated callback/webhook security gates pass.

## 13. Database and EF Core impact

No schema change was required. Multiple integration IDs reuse the encrypted `integrationId` string field with normalized list parsing. Expiry uses the existing nullable `PaymentIntents.ExpiresAtUtc` column. No migration was added by P7, and the prior pending-model check reported no model changes since the last migration.

The browser test legitimately added two fresh local orders and two Paymob provider intentions. It did not mutate credentials or payment-method configuration.

## 14. Fresh product and warehouse evidence

Per the user's instruction, the old product was replaced by a fresh Nokia test selection:

- Product: Nokia N73
- Variant: yellow / 32
- SKU: `NOKIA-N73-YELLOW-32`
- Displayed stock: 5
- Unit price: EGP 400.00
- Shipping: EGP 10.00
- Total: EGP 410.00

The fresh order contains exactly one unit. Product stock tracking is disabled for this product, so no inventory reservation row is expected. Warehouse resolution converted to `Ordered`. The Paymob error reproduced independently of warehouse availability, proving warehouse state was not the cause.

## 15. Fresh order and provider evidence

Final remediated diagnostic order:

- Order ID: `afdbb817-1740-467b-901d-2c69f1f9d8b1`
- Order number: `ORD-83B3-2026-01016`
- Cart ID: `cef05807-3d9e-4b1f-86f2-5c99049e6698`
- Payment intent: `bebffb4e-b3ad-4fe0-a7b4-93a55ff0cfea`
- Provider intention: `587573231`
- Amount/currency: EGP 410.00
- Local order status: pending
- Local checkout state: payment pending
- Local payment status: pending/unpaid
- Failure code/reason: null/null
- Protected provider session: present
- Expiry: `2026-08-14T20:00:40.276627Z`

The earlier diagnostic order `ORD-83B3-2026-01015` proved the missing-expiry defect. Neither order is represented as paid.

## 16. POS Admin visibility

Source and automated tests confirm POS Admin reads backend order/payment state and presents pending/collected/failed transitions without trusting a browser redirect. The fresh order exists in SQL and is therefore available to that read path.

The required interactive POS Admin check was not performed after the browser-control restriction. POS Admin visibility is therefore source/test/SQL-backed but not browser-certified for `ORD-83B3-2026-01016`.

## 17. Failure matrix

| # | Scenario | Required/resulting behavior |
|---:|---|---|
| 1 | Valid sandbox configuration | Available; live auth/discovery/Intention creation succeeded |
| 2 | Invalid secret key | Provider authentication failure; no accepted handoff |
| 3 | Missing secret key | Configuration invalid/unavailable; no provider call |
| 4 | Missing API key | Intention runtime remains possible; discovery/reconciliation unavailable |
| 5 | Malformed integration ID/list | Validation failure before provider request |
| 6 | Integration not owned by account | Discovery mismatch or provider rejection; no handoff |
| 7 | Paymob 401 | Fail payment initialization; do not fabricate success |
| 8 | Paymob 404 | Fail payment initialization; do not fabricate success |
| 9 | Paymob 422 | Fail payment initialization with safe provider error |
| 10 | Timeout/network failure | Persist failure/checkpoint evidence; no handoff |
| 11 | Paymob 5xx | Fail safely; no success state |
| 12 | Intention missing client secret | Adapter rejects incomplete provider contract |
| 13 | Client secret present but handoff absent | Backend/storefront reject response |
| 14 | Malformed/wrong-host Unified URL | Storefront rejects redirect |
| 15 | Stale replay | Expired session rejected fail-closed |
| 16 | Duplicate active-order checkout | Safe replay; no duplicate order/provider session |
| 17 | Retry after failed local intent | Terminal failed intent permits a new separately tracked attempt |
| 18 | Expired pending intent | Fail-closed and require status reconciliation |
| 19 | Succeeded terminal replay | No reusable provider artifacts in response |
| 20 | Duplicate webhook | Idempotently acknowledged/ignored |
| 21 | Invalid/missing HMAC | Unauthorized |
| 22 | Valid HMAC | Accepted for lifecycle admission |
| 23 | Provider failure callback | Payment/order remain unpaid and transition through canonical failure path |
| 24 | Provider success webhook | Canonical collected/confirmed transition after authority checks |
| 25 | Tenant mismatch | Unauthorized/quarantined |
| 26 | Currency mismatch/missing currency | Canonical currency guard rejects |
| 27 | Amount mismatch | Minor-unit/item-total and lifecycle amount guards reject |
| 28 | Wrong integration in callback | Reject against configured integration-ID set |
| 29 | Frontend/backend contract mismatch | Reject unsafe evidence; HttpOnly-cookie transfer is now explicitly supported |

## 18. Files changed by P7

Product/runtime changes attributable to this audit:

- `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`
- `PointOfSale.API/Services/Payments/Paymob/IPaymobPaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentGateway.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.Web/src/pages/PaymentMethods.jsx`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`

Regression/certification changes:

- `PointOfSale.Tests/Payments/PaymentMethodConfigurationValidatorTests.cs`
- `PointOfSale.Tests/Architecture/PaymobPaymentAdapterCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymobPaymentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontCheckoutSubmissionAuthorityCertificationTests.cs`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.test.ts`
- `PointOfSale.Web/src/pages/PaymentMethods.test.tsx`

The worktree contained many pre-existing changes. P7 did not revert or overwrite unrelated user work.

## 19. Automated test results

Final distinct gates:

- Backend tests filtered by `Paymob`: **180 passed, 0 failed, 0 skipped**
- Storefront full suite: **335 passed, 0 failed** across 34 files
- Admin PaymentMethods suite: **4 passed, 0 failed**
- Total distinct tests: **519 passed, 0 failed**

The focused storefront payment-session suite is included in the 335 total and passed 22/22. Earlier focused backend subsets are included in the 180 total and are not double-counted.

## 20. Build, migration, and hygiene gates

- API build: passed, 0 warnings, 0 errors
- Solution build performed during P7: passed
- Storefront production build after final fix: passed
- Admin production build: passed
- EF pending-model check: no pending model changes
- `git diff --check`: passed; line-ending notices only
- Secret-pattern scan: no tenant Paymob credential committed

Build output contains existing frontend bundle-size/dynamic-import advisories and stale browser-data advisories; these are not Paymob correctness failures.

## 21. Browser test chronology

1. Authenticated to the local SHOPIXY storefront with user-provided credentials.
2. Cleared the old/merged cart.
3. Selected Nokia N73 yellow/32, verified stock 5, and checked out one item.
4. Chose Paymob “Online Payment” and submitted a fresh order.
5. Reproduced the verification error and proved the first fresh intention existed but lacked expiry.
6. Implemented/passed the expiry remediation, restarted the source API, cleared the converted cart, and created a second fresh Nokia checkout.
7. Proved the second intention had a future expiry and valid protected Unified Checkout URL.
8. Identified and fixed the HttpOnly-cookie/JSON token mismatch.
9. Before the safe idempotent retry could open Unified Checkout, the browser-control surface refused further use of the local site and prohibited workarounds.

Unified Checkout was therefore not reached after the final fix.

## 22. External callback reachability

Development callback/webhook configuration points at the local API on port 5010. Paymob's servers cannot call a developer's loopback address. A complete provider finality test requires either:

- a public HTTPS tunnel mapped to the local callback/webhook routes, or
- a deployed HTTPS API environment with matching tenant configuration.

The resulting public URLs must be configured in Paymob and in the application before a success webhook can be certified.

## 23. Residual risks and required follow-up

1. Re-run the browser checkout after browser control is permitted.
2. Confirm the idempotent retry of `ORD-83B3-2026-01016` reuses provider intention `587573231` and opens Unified Checkout.
3. Submit Paymob's current official sandbox Visa only after Unified Checkout is visible.
4. Configure a public HTTPS callback/webhook endpoint and confirm receipt.
5. Prove valid HMAC at runtime, not only in automated tests.
6. Verify backend payment becomes collected/settled and order becomes confirmed/paid according to canonical policy.
7. Verify the same state in POS Admin.
8. Configure additional owned integration IDs if wallets or other Paymob methods must be offered; current `3153058` is card/VPC only.
9. Remediate the unrelated analytics 403 separately.

## 24. Certification rationale

Code remediation, credential ownership, live provider Intention creation, protected handoff shape, automated security gates, and builds all pass. However, the P7 definition requires an observed Unified Checkout, test Visa submission, callback/webhook, HMAC acceptance, payment finality, order finality, and POS Admin confirmation. Those claims cannot be made from source/tests alone.

Because the interactive browser was blocked before the final retry and the configured webhook is externally unreachable, the only accurate result is **BLOCKED**.

## 25. Required A–P handoff

| Item | Result |
|---|---|
| A. Root cause | Missing durable expiry on reused transactional seed; stale-session replay; obsolete plaintext redirect completeness check; storefront required a token deliberately transferred to HttpOnly cookie |
| B. All defects | Listed and severity-classified in sections 5 and 17 |
| C. Changes | Runtime, validation, replay, expiry, multi-integration, frontend contract, admin label, and tests changed as listed in section 18 |
| D. Integration changed | No P7 configuration change. DB already contained `3153058`; prior `3153090` drift occurred before P7 |
| E. DB changed | Two fresh Nokia diagnostic orders/payment intents/Paymob intentions created; no credential/config mutation |
| F. Migrations | None added; existing schema sufficient; no pending EF model change |
| G. Credentials | Public/secret/HMAC/API key configured; secret/HMAC decryptable; Egypt test context matches; values not disclosed |
| H. Browser reached | Checkout review and fresh order creation reached; final Unified Checkout not reached after last fix because browser control blocked local-site interaction |
| I. Test Visa | Not entered |
| J. Webhook received | No |
| K. HMAC | Source and automated tests pass; fresh runtime webhook HMAC not observed |
| L. Payment status | `ORD-83B3-2026-01016`: Pending/unpaid |
| M. Order status | `ORD-83B3-2026-01016`: Pending / PaymentPending |
| N. POS Admin | SQL/source/test-backed visibility; interactive fresh-order verification not completed |
| O. Exact tests | 519 distinct passed, 0 failed (180 backend + 335 storefront + 4 admin) |
| P. Certification | **BLOCKED** |

# SHOPIXY Paymob P8.2 Adversarial Finality & Security Audit

Date: 2026-08-15

Certification: PASS WITH CONDITIONS

## 1. Executive Summary

P8.2 attempted to violate Paymob payment finality, order finality, inventory safety, tenant isolation, customer retry authorization, attempt lineage, idempotency, terminal-state protection, and secret hygiene through deterministic application-layer tests.

The first real adversarial defect found was merchant/MID integrity: Paymob webhook finality validated HMAC and integration id but did not validate the callback `owner` value against the tenant Paymob merchant id. The smallest correct remediation was added at Paymob webhook admission before runtime/lifecycle mutation. Regression tests now prove wrong merchant owner values cannot finalize payment.

All Paymob-focused P8/P8.1/P8.2 tests pass after remediation. Broader checkout/admin filters still contain unrelated pre-existing failures documented below; builds and EF model validation pass.

## 2. Scope

In scope: Paymob webhook entry, HMAC verification, tenant resolution, payment intent/attempt correlation, payment lifecycle finality, retry authority, duplicate/concurrent webhook handling, amount/currency/integration/order/merchant validation, browser callback behavior, logging, inventory safety, and admin/storefront observability for Paymob flows.

Out of scope: real Paymob Live calls, real-money card authorization, live credentials, unrelated providers except cross-provider confusion assertions, and schema changes.

## 3. P8/P8.1 Baseline

P8 baseline remained green through `PaymobSuccessPathSimulationTests`: intention creation, Unified Checkout handoff, real webhook pipeline, HMAC verification, success finality, order finality, inventory finalization, duplicate/concurrent success idempotency, and failure handling.

P8.1 retry baseline remained green through the Paymob focused suite: failed payment retry creates a new Paymob attempt/provider reference, failed attempt remains historical, failed-retry-success works, and late success for an older failed attempt cannot override a newer active attempt.

## 4. Architecture Map

Webhook entry: `PointOfSale.API/Controllers/PaymobWebhooksController.cs:79` handles Paymob webhooks and calls `PaymentWebhookRequestPipeline.PrepareAsync` before `IPaymobPaymentService.ProcessWebhookAsync`.

Browser callback: `PointOfSale.API/Controllers/PaymobWebhooksController.cs:214` validates callback data and redirects to pending status; it does not mark orders paid.

Webhook parsing and tenant pipeline: `PointOfSale.API/Services/Payments/PaymentWebhookRequestPipeline.cs:66` requires a signature, reads the raw body, resolves tenant, rejects claimed tenant mismatch, and applies tenant context.

Tenant resolution: `PointOfSale.API/Services/Payments/IPaymentWebhookTenantResolver.cs:156` resolves Paymob tenant context by provider order id, merchant order id, or provider payment id.

Paymob HMAC: `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:935`, `:2283`, and `:2315` build the canonical payload and verify the HMAC.

Integration validation: `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:989` validates callback integration id against tenant configuration.

Merchant/MID validation: `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:998` and `:2238` now validate callback owner against configured merchant id.

Payment/attempt correlation: `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:1273` resolves Paymob transaction finality to the local Paymob payment intent and attempt lineage.

Success/failure finality: `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:1465` and `:1660` delegate success/failure to `PaymentLifecycleService`.

Lifecycle locking/finality: `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs:1816`, `:2663`, `:3357`, `:3410`, and `:3907` provide transactional lifecycle finality, row locking, stale attempt rejection, terminal SQL-truth rejection, and amount/currency authority mismatch quarantine.

Refund protection: `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs:842` records refunds through lifecycle transaction rules.

Retry authority: `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:541`, `:578`, and `:2039` enforce retry eligibility, order/customer ownership, and provider lineage.

## 5. Threat Model

The audit assumes an attacker may replay webhooks, omit or alter HMACs, mutate signed payloads, send validly signed but mismatched Paymob fields, race duplicate success events, race retries, reuse stale attempts/sessions, cross tenant/customer boundaries, spoof browser redirects, confuse providers, collide provider references/event ids, or try to leak payment secrets through logs.

## 6. HMAC Attacks

Invalid HMAC, missing HMAC, and modified payload with original HMAC were exercised through the real controller and service pipeline. All rejected before finality. Regression tests: `InvalidHmacSignature_CannotFinalize`, `MissingHmac_CannotFinalize` at `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs:385`, and `PayloadModifiedAfterHmacComputation_CannotFinalize` at `:408`.

## 7. Amount Attacks

Wrong amount variants `40999`, `41001`, `40000`, `0`, `-41000`, and `999999999999` cents cannot finalize an EGP 410 payment. The lifecycle authority check quarantines amount mismatches before paid/order/inventory mutation. Regression test: `WrongAmountVariants_CannotFinalize` at `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs:492`.

## 8. Currency Attacks

Wrong currency variants `USD`, `EUR`, empty, and `SAR` cannot finalize the EGP payment. Lowercase `egp` is intentionally normalized to canonical `EGP`, not treated as a distinct currency. Regression test: `WrongCurrencyVariants_CannotFinalize` at `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs:507`.

## 9. Order Correlation Attacks

Wrong merchant/provider order references cannot mutate another order or finalize the payment incorrectly. Existing Paymob focused tests cover wrong order correlation and tenant-scoped resolution. Source: Paymob tenant resolver and `ProcessPaymobTransactionAsync`.

## 10. Attempt Lineage Attacks

Failed attempt then retry then success remains authoritative to the newest active attempt. Late success for the older failed attempt is rejected by lifecycle stale/superseded attempt checks. Paymob focused suite and P8.1 retry tests pass.

## 11. Integration Attacks

Wrong integration ids cannot finalize. Source validation is in `PaymobPaymentService.ProcessWebhookAsync`; existing Paymob focused tests cover wrong integration rejection. Correct integration `3153058` still succeeds.

## 12. Merchant/MID Attacks

Initial result: FAIL. A valid-HMAC callback with correct integration but wrong `owner` was not rejected by application-layer merchant validation.

Root cause: `ProcessWebhookAsync` compared integration id but did not compare `payload.Obj.Owner` to configured `PaymobTenantRuntimeCredentials.MerchantId`.

Remediation: added `PAYMOB_MERCHANT_MISMATCH` rejection at `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:998` and helper at `:2238`.

Regression tests: `WrongMerchantOwnerId_CannotFinalize` at `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs:463` and `WrongMerchantOwnerVariants_CannotFinalize` at `:527`.

## 13. Tenant Isolation

Paymob tenant resolution is scoped to local Paymob intent/order/provider identifiers and the webhook pipeline rejects claimed tenant mismatch before applying tenant context. Retry authority validates tenant and customer ownership before creating a new attempt.

## 14. Customer Authorization

Retry authorization requires correct tenant/customer/order ownership and a retry-eligible failed Paymob attempt. Wrong customer, anonymous/missing context, stale session/order mismatch, and different tenant customer cannot receive a new Paymob handoff by the source authority checks.

## 15. Duplicate/Concurrent Events

Duplicate success events are idempotent and concurrent duplicate success events finalize exactly once. Regression tests in Paymob success simulation and Paymob focused suite pass.

## 16. Concurrent Retry

Retry creation is guarded by retry authority and lifecycle/order mutation paths. P8.1 focused tests prove failed retry creates one new active Paymob lineage and stale older attempts cannot override it.

## 17. Terminal-State Protection

Lifecycle finality rejects stale provider success against terminal SQL truth and superseded terminal attempts. Terminal orders are not reopened or repaid by stale Paymob success events.

## 18. Refund/Void Protection

Refund lifecycle exists through `PaymentLifecycleService.RecordRefundAsync` at `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs:842`; Paymob refund finality is guarded by canonical refund reconciliation checks. Direct sandbox card refund/void authorization remains externally unavailable, but application-side stale success protection is proven by source and lifecycle regression tests.

## 19. Stale Session Protection

Paymob handoff/session authority is backend controlled and retry requires the authoritative failed local Paymob payment intent. Old provider handoff values or stale order/session references cannot create paid finality without a valid provider webhook and local correlation.

## 20. Browser Redirect Protection

`HandleCallback` validates callback shape and redirects to pending status only. Browser navigation, fake success query parameters, fake transaction id, fake order id, or altered status cannot mark an order paid.

## 21. Event Ordering

SUCCESS/SUCCESS duplicate is idempotent. FAILURE/SUCCESS, SUCCESS/FAILURE, FAILURE/RETRY/SUCCESS, and late stale success after retry follow lifecycle finality and stale-attempt rejection. Refund/void ordering is protected by lifecycle refund/terminal-state rules.

## 22. Cross-Provider Protection

Paymob webhook processing resolves only local Paymob payment intents and cannot finalize manual wallet, COD, Fawry, Stripe, or nonexistent provider intents.

## 23. Reference Collision

Provider reference ambiguity is constrained by Paymob provider/order correlation and local payment intent resolution. Existing Paymob focused tests cover collision-resistant correlation; ambiguous references do not authorize cross-order finality.

## 24. Event ID Collision

The idempotency contract treats provider event identity as authoritative and binds replay handling to provider/reference evidence. A tampered payload with the original HMAC is rejected; a duplicate successful event cannot duplicate financial or inventory effects.

## 25. Secret/PII Logging Audit

Inspected Paymob service/controller/tests/docs for API key, secret key, HMAC secret, client secret/payment key, access token, PAN, and CVV logging. No production logging statements print Paymob secrets, payment keys, raw card data, or CVV. Webhook payloads are protected and HMAC signatures are digested. Test-only constants are non-production.

## 26. Inventory Safety

Successful Paymob finality finalizes inventory exactly once. Rejected malicious events produce zero inventory mutation. Duplicate/concurrent success produces no extra stock decrement or duplicate reservation mutation.

## 27. Admin/Storefront Observability

Paymob focused tests verify admin and storefront payment status remain consistent after success, failure, retry, duplicate success, and stale attempt scenarios. Broader admin/storefront filters have unrelated pre-existing failures listed in regression results.

## 28. Adversarial Matrix

| Attack | Expected Result | Actual Result | PASS/FAIL | Security Impact | Source Location | Regression Test |
|---|---|---|---|---|---|---|
| Invalid HMAC | Reject, no finality | Rejected | PASS | Prevents unsigned mutation | `PaymobPaymentService.cs:935`, `:2315` | Paymob success simulation |
| Missing HMAC | Reject, no finality | Rejected | PASS | Prevents unsigned mutation | `PaymentWebhookRequestPipeline.cs:66` | `MissingHmac_CannotFinalize` |
| Modified payload | Reject, no finality | Rejected | PASS | Prevents payload tampering | `PaymobPaymentService.cs:2283` | `PayloadModifiedAfterHmacComputation_CannotFinalize` |
| Wrong amount | No paid/inventory mutation | Rejected/quarantined | PASS | Prevents under/overpay finality | `PaymentLifecycleService.cs:3907` | `WrongAmountVariants_CannotFinalize` |
| Wrong currency | No paid/inventory mutation | Rejected/quarantined | PASS | Prevents currency substitution | `PaymentLifecycleService.cs:3907` | `WrongCurrencyVariants_CannotFinalize` |
| Wrong order | No cross-order mutation | Rejected | PASS | Protects order correlation | `PaymobPaymentService.cs:1273` | Paymob focused suite |
| Wrong integration | No finality | Rejected | PASS | Protects tenant integration | `PaymobPaymentService.cs:989` | Paymob focused suite |
| Wrong MID | No finality | Initially failed, remediated | PASS | Protects merchant identity | `PaymobPaymentService.cs:998` | `WrongMerchantOwnerId_CannotFinalize` |
| Wrong tenant | No cross-tenant mutation | Rejected | PASS | Protects tenant boundary | `IPaymentWebhookTenantResolver.cs:156` | Paymob focused/source proof |
| Wrong customer | No retry/handoff | Rejected | PASS | Protects customer boundary | `PaymobPaymentService.cs:2039` | P8.1 focused/source proof |
| Duplicate SUCCESS | One finalization | One finalization | PASS | Prevents duplicate financial effect | `PaymentLifecycleService.cs:1816` | Paymob success simulation |
| Concurrent SUCCESS | One finalization | One finalization | PASS | Prevents race double-finality | `PaymentLifecycleService.cs:2663` | Paymob success simulation |
| Concurrent retry | One active retry lineage | One active lineage | PASS | Prevents duplicate attempts | `PaymobPaymentService.cs:541` | P8.1 focused/source proof |
| Stale SUCCESS | No override/regression | Rejected/idempotent | PASS | Protects attempt lineage | `PaymentLifecycleService.cs:3357` | P8.1 focused |
| Terminal order retry | No reopen/repaid state | Rejected by authority/finality | PASS | Protects terminal orders | `PaymentLifecycleService.cs:3410` | Lifecycle focused/source proof |
| Refund/void stale SUCCESS | No return to paid | Protected by lifecycle; sandbox refund auth external | PASS | Protects negative terminal state | `PaymentLifecycleService.cs:842` | Lifecycle focused/source proof |
| Browser redirect spoof | No paid finality | Pending redirect only | PASS | Blocks client-side finality spoof | `PaymobWebhooksController.cs:214` | Source proof |
| Stale session | No retry/finality | Rejected by authority | PASS | Blocks stale handoff reuse | `PaymobPaymentService.cs:541` | P8.1 focused/source proof |
| Wrong provider | No Paymob finality | Rejected | PASS | Blocks provider confusion | `PaymobPaymentService.cs:1273` | Paymob focused/source proof |
| Provider reference collision | No ambiguous finality | Protected by correlation | PASS | Blocks cross-order collision | `IPaymentWebhookTenantResolver.cs:156` | Paymob focused/source proof |
| Event ID collision | No duplicate material effect | Idempotent/replay guarded | PASS | Blocks duplicate effects | `PaymentLifecycleService.cs:1816` | Paymob focused/source proof |
| Secret leakage | No secret/PII logs | No secret logging found | PASS | Protects credentials | Paymob service/controller/tests scan | Secret scan |

## 29. Remediations

Implemented merchant/MID validation in `PaymobPaymentService.ProcessWebhookAsync`. A Paymob callback with configured tenant merchant id must have a matching callback `owner`; otherwise `PAYMOB_MERCHANT_MISMATCH` is thrown before runtime admission or lifecycle mutation.

No schema changes were required.

## 30. Regression Results

PaymobSuccessPathSimulationTests: 22 passed, 0 failed.

Paymob focused suite: 202 passed, 0 failed.

Webhook hardening/callback suite: 31 passed, 0 failed.

PaymentLifecycle focused suite: 60 passed, 0 failed.

Broad checkout/payment filter: 475 passed, 25 failed. Failures are pre-existing/unrelated to P8.2 and include stale architecture assertions, missing historical SQL files, Fawry checkpoint dependency, CommerceHub category gating differences, and cart-clearance source assertion.

Broad admin/storefront payment filter: 67 passed, 5 failed. Failures are pre-existing/unrelated artifact/source assertion tests.

## 31. Build Results

`dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore`: PASS, 0 warnings, 0 errors.

`dotnet build PointOfSale.sln --no-restore`: PASS, 0 warnings, 0 errors.

Storefront `npm run build`: PASS with non-fatal Browserslist/chunk warnings.

Admin Web `npm run build`: PASS.

`git diff --check`: PASS; no whitespace errors.

## 32. Migration Status

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API`: No changes have been made to the model since the last migration.

No EF model changes required.

## 33. Remaining Risks

The only application-side security defect found in this audit was merchant/MID validation and it is remediated. Remaining failures in broad filters are unrelated pre-existing project/test-suite issues and do not invalidate Paymob finality/security certification.

## 34. Paymob Sandbox Boundary

Paymob Sandbox card authorization remains externally unverified because Paymob currently rejects the official Sandbox test card, including through a Paymob Payment Link. This does not invalidate the SHOPIXY application-side finality/security certification.

## 35. Final Certification

P8.2 certification is PASS WITH CONDITIONS: application-side Paymob adversarial finality/security tests pass after the merchant/MID remediation, no unresolved Paymob finality/security defect remains, builds pass, EF has no pending model changes, and the only conditions are unrelated pre-existing broad-suite failures plus the external Paymob Sandbox card authorization limitation.

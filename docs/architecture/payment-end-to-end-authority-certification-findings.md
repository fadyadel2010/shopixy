# Complete Payment End-to-End Authority Certification (PASS SF-PAY-END)

Date: 2026-07-03

## Scope

Reviewed only the complete PointOfSale.Storefront payment runtime, directly consumed PointOfSale.Web payment configuration/read surfaces, and backend APIs/services directly involved in Payment Method Read, Checkout Session, Payment Session, Manual Payment, Vodafone Cash, Stripe, Paymob, Fawry, Payment Lifecycle, Checkout, Payment Recovery, Payment Audit, and Payment Projection.

Excluded refunds, chargebacks, settlement, accounting, affiliate payouts, and marketplace payouts except where names appear in lifecycle files outside this certification scope.

Prior SF-PAY1 through SF-PAY7 certifications were treated as architectural truth.

## Certification

The complete Payment Domain remains backend-owned and production-certifiable with no runtime behavior change required.

Payment begins when Storefront submits a backend-bound checkout/payment-method candidate for Manual Payment, Vodafone Cash, Stripe, Paymob, or Fawry. Authority begins only after backend tenant/session/customer/cart/order/payment-method admission. Verification begins only at backend Stripe signed webhook, Paymob HMAC callback/webhook, Fawry HMAC webhook, or backend admin manual-wallet verification boundaries. Financial authority begins in backend pricing, expected-pricing comparison, SQL order snapshot, SQL payment intent amount/currency, and lifecycle amount/currency validation. SQL persistence begins in SQL `Orders`, SQL `PaymentIntents`, SQL webhook logs, checkout/session rows, outbox, audit, and projection source rows. Audit begins in backend lifecycle/admin/webhook/outbox events. Projection begins only when SQL-derived consumers reload or validate SQL truth. Presentation begins only after accepted backend DTO emission and Storefront DTO admission.

Frontend owns only presentation, selection, redirect, SDK/iframe/wallet display, loading, retry, refresh, navigation, candidate submission, and rendering.

Backend owns Payment Identity, PaymentIntent, Payment Method authority, Checkout Token, Provider Sessions, Stripe Session, Paymob Session, Fawry Session, Manual Wallet, Vodafone Wallet, Merchant Wallet, Merchant Reference, Reference Code, Payment Keys, Client Secret, Webhook, Callback, Verification, Lifecycle, Payment Status, Order Status, Financial Authority, Audit, Recovery, Projection Source, and SQL Truth.

## Runtime Findings

- Payment Method Read remains backend-owned through no-store backend reads, tenant-scoped SQL `TenantPaymentMethods`, configuration validation, supported-code filtering, SQL fallback, and Storefront DTO admission before Zustand.
- Payment Session and Checkout Session remain backend-owned through checkout admission, row locks, expected-pricing comparison, backend-generated tokens, SQL `Orders`, SQL `PaymentIntents`, provider handoff persistence, and Storefront `requireStorefrontPaymentSessionAuthority`.
- Manual Payment and Vodafone Cash remain inside backend-owned ManualWallet authority. Storefront can submit wallet/reference/proof candidates, but proof upload is token-bound and verification is admin/backend/lifecycle only.
- Stripe remains backend-owned through SQL PaymentIntent creation, encrypted client-secret persistence, signed webhook validation, SQL webhook logs, lifecycle-owned status transitions, and canonical backend status polling.
- Paymob remains backend-owned through backend payment-key/session creation, stored routing metadata, callback/webhook HMAC validation, SQL webhook logs, lifecycle-owned status transitions, and canonical backend status polling.
- Fawry remains backend-owned through SQL PaymentIntent creation, backend-generated checkout token, deterministic backend reference-code generation, stored reference-code tenant resolution, tenant-specific webhook HMAC validation, SQL webhook logs, lifecycle-owned status transitions, and canonical backend status polling.
- Financial authority cannot be altered by a provider because backend order totals, expected-pricing stale detection, payment-intent amount/currency, provider amount/currency validation, and amount-mismatch quarantine remain decisive.
- Status transitions are owned by `IPaymentLifecycleService` and guarded by `PaymentStatusSaveChangesInterceptor`; duplicate webhooks/callbacks collapse through SQL webhook logs, idempotency keys, terminal-state collapse, and lifecycle idempotency.
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projections are presentation, cache, invalidation, or rebuild surfaces only. They cannot become Payment Authority.
- Recovery, refresh, reconnect, restart, duplicate checkout, duplicate PaymentIntent, duplicate upload, duplicate verification, duplicate webhook, duplicate callback, duplicate redirect, Redis flush, Mongo rebuild, and projection rebuild all re-enter backend SQL/payment authority or fail closed.
- Adjacent Orders, Coupons, Promotions, Smart Offers, Affiliate, Marketplace, Inventory, Checkout, Builder, CMS, and Analytics boundaries do not overwrite Payment Authority and Payment does not overwrite their authority. Payment lifecycle emits events/outbox/projections from SQL truth only.

## Runtime Hardening

No runtime behavior changes were required.

Certification-only changes:

- Added `PointOfSale.Tests/Architecture/PaymentEndToEndAuthorityCertificationTests.cs`.
- Added `docs/architecture/payment-end-to-end-authority-certification-findings.md`.
- Updated the five `.ai-context` authority files with PASS SF-PAY-END truth, risk, hardening, and freeze entries.

## Final Payment Pipeline

Manual Payment, Vodafone Cash, Stripe, Paymob, and Fawry all operate inside one backend authority model:

Storefront payment-method candidate -> backend tenant/session/customer/cart/order/payment-method admission -> backend expected-pricing and SQL order authority -> backend SQL `PaymentIntent` authority -> backend checkout token or proof/session artifact authority -> provider-specific backend session/reference/key/client-secret creation -> backend webhook/callback/admin verification -> `IPaymentLifecycleService` status transition -> SQL `Orders`/`PaymentIntents`/webhook logs/outbox/audit truth -> SQL-derived projection -> Storefront presentation only.

## Provider Matrix

| Provider | PaymentIntent | Checkout Token | Provider Session | Verification | Lifecycle | Audit | Recovery | SQL Truth |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Manual Payment | Backend SQL `PaymentIntents` with ManualWallet provider and pending/awaiting-verification status | Backend upload/checkout proof token where issued | Backend ManualWallet proof-session and tenant merchant-wallet evidence | Backend admin approval/rejection only | `IPaymentLifecycleService` wallet approval/rejection | Admin/lifecycle/outbox/audit rows | Backend checkout/payment-intent/proof-session re-entry | SQL `Orders`, `PaymentIntents`, checkout proof rows, outbox/audit |
| Vodafone Cash | Backend SQL `PaymentIntents` inside ManualWallet lane with `VodafoneCash` wallet type | Backend upload/checkout proof token where issued | Backend ManualWallet proof-session and Vodafone wallet evidence | Backend admin approval/rejection only | `IPaymentLifecycleService` wallet approval/rejection | Admin/lifecycle/outbox/audit rows | Backend checkout/payment-intent/proof-session re-entry | SQL `Orders`, `PaymentIntents`, checkout proof rows, outbox/audit |
| Stripe | Backend SQL `PaymentIntents` linked to Stripe provider evidence | Backend `CheckoutSessionTokenService` | Backend Stripe PaymentIntent/client-secret creation and encrypted SQL persistence | Backend signed Stripe webhook only | `IPaymentLifecycleService` Stripe webhook transitions | SQL webhook logs, lifecycle/outbox/audit rows | Backend checkout/payment-intent/session re-entry and status polling | SQL `Orders`, `PaymentIntents`, `PaymentWebhookLogs`, outbox/audit |
| Paymob | Backend SQL `PaymentIntents` linked to Paymob routing evidence | Backend `CheckoutSessionTokenService` | Backend Paymob order/session/payment-key creation | Backend HMAC callback/webhook validation; callback remains pending-only presentation | `IPaymentLifecycleService` Paymob webhook transitions | SQL webhook logs, lifecycle/outbox/audit rows | Backend checkout/payment-intent/session re-entry and status polling | SQL `Orders`, `PaymentIntents`, `PaymentWebhookLogs`, outbox/audit |
| Fawry | Backend SQL `PaymentIntents` linked to backend reference-code evidence | Backend `CheckoutSessionTokenService` | Backend Fawry initiation with deterministic reference code, provider intent, amount/currency, and expiration | Backend tenant-resolved Fawry HMAC webhook only | `IPaymentLifecycleService` Fawry webhook transitions | SQL webhook logs, lifecycle/outbox/audit rows | Backend checkout/payment-intent/session re-entry and status polling | SQL `Orders`, `PaymentIntents`, `PaymentWebhookLogs`, outbox/audit |

## Authority Map

- Payment Identity: backend SQL `PaymentIntents` / backend DTO admission.
- PaymentIntent: backend SQL `PaymentIntents`.
- Payment Method: backend SQL `TenantPaymentMethods`, backend read/checkout admission.
- Provider Session: backend payment services.
- Stripe Session: backend Stripe service and SQL routing/client-secret evidence.
- Paymob Session: backend Paymob service and SQL routing/payment-key evidence.
- Fawry Session: backend Fawry adapter/service and SQL reference/provider evidence.
- Manual Wallet: backend ManualWallet configuration, proof-session validation, admin verification.
- Vodafone Wallet: backend ManualWallet lane with `VodafoneCash` wallet type.
- Checkout Token: backend `CheckoutSessionTokenService`.
- Merchant Wallet: backend ManualWallet tenant configuration.
- Merchant Reference: backend payment service/provider adapter metadata.
- Client Secret: backend Stripe service, encrypted SQL persistence, transient admitted DTO.
- Payment Key: backend Paymob service, stored routing metadata, transient admitted DTO.
- Reference Code: backend Fawry adapter deterministic reference generation, stored SQL evidence, transient admitted DTO.
- Webhook: backend controllers/services with signature/HMAC validation.
- Callback: backend Paymob callback validation and pending-only redirect.
- Verification: backend webhook/admin lifecycle services.
- Amount/Currency/Tax/Shipping/Grand Total: backend pricing/checkout/order/payment authority.
- Payment Status: backend `IPaymentLifecycleService` and SQL.
- Order Status: backend checkout/payment lifecycle and SQL.
- Reservation: backend checkout/inventory authority and lifecycle release/confirmation.
- Financial Authority: backend pricing, expected-pricing stale detection, SQL snapshots, lifecycle validation.
- Audit: backend audit/outbox/webhook/lifecycle events.
- Recovery: backend checkout/session/payment-intent recovery.
- Projection: SQL-derived projection consumers/read models only.
- SQL Truth: PostgreSQL `Orders`, `PaymentIntents`, `TenantPaymentMethods`, webhook logs, checkout/session, outbox, and audit rows.

## Explicit Answers

- Can frontend fabricate Payment? No.
- Can frontend fabricate Financial Success? No.
- Can replay fabricate Payment? No.
- Can duplicate webhook succeed twice? No.
- Can duplicate callback succeed twice? No.
- Can duplicate redirect succeed twice? No.
- Can duplicate upload succeed twice? No.
- Can duplicate checkout succeed twice? No.
- Can mixed provider DTOs succeed? No.
- Can React become Authority? No.
- Can React Query become Authority? No.
- Can Zustand become Authority? No.
- Can LocalStorage become Authority? No.
- Can Redis become Authority? No.
- Can Mongo Projection become Authority? No.
- Can Recovery diverge from SQL? No.
- Can another domain overwrite Payment Authority? No.
- Were runtime changes required? No.
- Is the COMPLETE Payment Domain production-certifiable? Yes.
- Final production provider list: Manual Payment, Vodafone Cash, Stripe, Paymob, Fawry.

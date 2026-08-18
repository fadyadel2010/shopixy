# SHOPIXY Payment Methods Forensic Recovery Audit

Date: 2026-08-12
Project: SHOPIXY / PointOfSale
Scope: audit-only forensic diagnosis after Shipping Methods closure

## 1. Executive Summary

Two live symptoms were traced end-to-end without modifying source, tests, configuration, schema, migrations, or data.

Symptom A, Admin Payment Methods showing "No payment methods available", is caused by the backend returning a successful empty list for the current runtime tenant. The frontend calls `GET /api/admin/payment-methods`; the API maps to `PaymentMethodsAdminController.GetPaymentMethods`; that endpoint queries `TenantPaymentMethods` for the resolved tenant and returns only existing tenant rows. It does not synthesize static provider registry defaults on read. An authenticated runtime probe returned `200 []` for tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.

Symptom B, Manual Payments showing "Manual Payment configuration is invalid." while the browser reports `GET /api/admin/payments/manual-wallet/pending?page=1&pageSize=25` as `404`, is not a missing route. The exact route exists and is mapped by `AdminManualPaymentController`. An authenticated runtime probe reached that controller and returned `404` with code `MANUAL_PAYMENT_CONFIGURATION_INVALID`. The 404 is produced by the backend manual-payment admission/configuration gate, not by route resolution.

Final certification: DIAGNOSIS CONFIRMED — SAFE TO REMEDIATE

## 2. Current Payment Architecture

Current payment architecture is a combination model:

- Admin payment-method authority: SQL `TenantPaymentMethods` rows, tenant-scoped by `TenantId`.
- Storefront selector read model: Mongo `payment_methods` documents with SQL fallback through `PaymentMethodReadRepository`.
- Checkout authority: SQL shipping method, SQL tenant payment method, server recalculated cart/pricing, and persisted `Order` / `PaymentIntent`.
- Provider execution: online CommerceHub orchestration for Stripe and Paymob; legacy/payment service runtime paths for Fawry and ManualWallet; COD is non-gateway/offline.
- Webhook/callback finality: provider-specific controllers and runtime services for Stripe, Paymob, and Fawry. COD webhooks are explicitly blocked.

Primary entities and contracts inspected:

- `TenantPaymentMethod`
- `PaymentIntent`
- `PaymentWebhookLog`
- `PaymentCancellationOutboxMessage`
- `PaymentSettlementBatch`
- `PaymentSettlementOrder`
- `PaymentMethodDocument`
- `PaymentMethodConfigurationValidator`
- `PaymentIntentStatus`
- `PaymentStatus`
- `PaymentProvider`
- `CheckoutState`
- payment lifecycle/runtime contracts under `PointOfSale.Core/Payments/Runtime`

Registered/intended provider codes found in current code:

- `COD`
- `STRIPE`
- `PAYMOB`
- `FAWRY`
- `MANUALWALLET`

No evidence was found that Vodafone Cash is a separate provider code. It is treated as a wallet type/channel inside the generic manual wallet/manual payment model.

## 3. Payment Methods Admin UI

Admin route:

- UI route: `PointOfSale.Web/src/App.jsx` maps `ecommerce/payment-methods` to `PaymentMethods`.
- Page: `PointOfSale.Web/src/pages/PaymentMethods.jsx`.
- API client: `PointOfSale.Web/src/services/api.js`.
- Base URL: `/api`, proxied by Vite to `http://localhost:5010`.

Load path:

- Frontend call: `api.get('/admin/payment-methods')`
- Actual HTTP request: `GET /api/admin/payment-methods`
- Query parameters: none.
- Tenant context: resolved by backend tenant middleware/auth context; frontend does not add a payment-specific tenant header.
- Response handling: `setMethods(mergeMethods(response.data || []))`.
- Empty state: when `methods.length === 0`, the page renders `No payment methods available`.
- Filtering: `mergeMethods` only maps/sorts received methods. It does not filter out disabled/invalid providers and does not synthesize defaults.
- Error behavior: on catch, the page sets `methods` to `[]` and renders an error. Runtime proof for this symptom is not an error path; the backend returned `200 []`.

Conclusion: the empty page is caused by API returning `[]`, not frontend filtering.

## 4. Payment Method Backend Discovery

Admin controller:

- File: `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`
- Route: `[Route("api/admin/payment-methods")]`
- Authorization: `[Authorize(Roles = "Owner,Admin,Manager")]`
- GET: `[HttpGet] GetPaymentMethods`

GET behavior:

- Reads `tenantId = _tenantContext.RequiredTenantId`.
- Queries `_dbContext.TenantPaymentMethods.Where(m => m.TenantId == tenantId)`.
- Orders by `SortOrder`, then `Code`.
- Maps rows to DTOs.
- Returns `Ok(response)`.

Important behavior:

- GET does not create missing provider rows.
- Mutation paths can create rows:
  - `PUT /api/admin/payment-methods/{code}`
  - `PUT /api/admin/payment-methods/{code}/configure`
  - `POST /api/admin/payment-methods/{code}/enable`
  - `POST /api/admin/payment-methods/{code}/disable`
- Supported admin codes are validated as `COD`, `STRIPE`, `PAYMOB`, `FAWRY`, `MANUALWALLET`.

Therefore Payment Methods are not a pure static registry. They are tenant-specific SQL configuration rows with code-level defaults used during mutation, plus projected read models for storefront.

## 5. Runtime Database Identity

Runtime database context was inspected read-only through EF tooling and authenticated GET probes.

Runtime host/port:

- API: `http://localhost:5010`
- Web dev proxy: `http://localhost:5173` -> `http://localhost:5010`

Runtime EF context:

- DbContext: `PointOfSale.Infrastructure.Data.ApplicationDbContext`
- Provider: `Npgsql.EntityFrameworkCore.PostgreSQL`
- Database: `PosEcommerceDb_Baseline`
- Data source: `tcp://192.168.1.5:5432`
- Connection-string source: configuration/user-secrets resolved by API startup. Secret values were not printed in this report.
- Environment: Development runtime, evidenced by availability of testing-only auth endpoint and launch settings.

Authenticated runtime tenant used for probes:

- Tenant ID: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`
- Role: `Admin`
- Token was obtained from development-only `POST /api/test-auth/token`; no data was mutated.

Runtime observed state:

- `GET /api/admin/payment-methods` returned count `0`.
- `GET /api/admin/payments/manual-wallet/pending?page=1&pageSize=25` returned status `404` with code `MANUAL_PAYMENT_CONFIGURATION_INVALID`.

## 6. Payment Migration State

Active EF migrations contain payment schema beginning in the baseline migration and are fully applied according to `dotnet ef migrations list`.

Active migration list observed:

- `20260723214642_Baseline_Recovery3`
- `20260726153652_PosCartRuntime`
- `20260726172030_PlatformCurrencyReferenceSeed`
- `20260729120000_StorefrontProductUnitDbEnforcement`
- `20260729150000_ProductAuthoringXminConcurrency`
- `20260805215917_DOMAIN_2C_0_CreateDomainTables`
- `20260805215918_DOMAIN_2C_DomainPersistence`
- `20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics`
- `20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze`
- `20260809185514_SHOPIXY_CheckoutFieldDefinitions`
- `20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots`
- `20260809203807_SHOPIXY_CheckoutAttachments`
- `20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement`
- `20260810114400_SHOPIXY_CheckoutAttachmentFinalization`
- `20260810181420_DOMAIN_Final_ForensicHardening`
- `20260811150644_SHOPIXY_WarehouseResolutionAuthority`
- `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority`
- `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`

Payment-related tables/indexes found in active migration snapshots include:

- `TenantPaymentMethods`
- `PaymentIntents`
- `PaymentWebhookLogs`
- `PaymentCancellationOutboxMessages`
- `PaymentSettlementBatches`
- `PaymentSettlementOrders`
- `StripeWebhookReplayRecords`
- `PaymobWebhookReplayRecords`
- `FawryWebhookReplayRecords`

Source latest payment migration:

- `20260723214642_Baseline_Recovery3` is the active migration that introduced/contains the recovered payment schema.

Runtime latest migration:

- `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`

Conclusion:

- No active pending migrations were reported.
- The live admin empty state is not explained by active migration drift.
- The current evidence points to missing tenant payment-method rows/configuration, not missing payment schema.

## 7. Manual Payment 404 Forensics

Frontend:

- File: `PointOfSale.Web/src/pages/ManualPayments.jsx`
- List request: `api.get('/admin/payments/manual-wallet/pending', { params: { page, pageSize }})`
- Actual request: `GET /api/admin/payments/manual-wallet/pending?page=1&pageSize=25`
- Proof image: `GET /api/admin/payments/manual-wallet/proof-image/{paymentIntentId}`
- Approve: `POST /api/admin/payments/manual-wallet/{paymentIntentId}/confirm`
- Reject: `POST /api/admin/payments/manual-wallet/{paymentIntentId}/reject`

Backend:

- File: `PointOfSale.API/Controllers/AdminManualPaymentController.cs`
- Route: `[Route("api/admin/payments/manual-wallet")]`
- Pending endpoint: `[HttpGet("pending")]`
- Proof endpoint: `[HttpGet("proof-image/{paymentIntentId:guid}")]`
- Confirm endpoint: `[HttpPost("{paymentIntentId:guid}/confirm")]`
- Reject endpoint: `[HttpPost("{paymentIntentId:guid}/reject")]`

Runtime proof:

- Authenticated request reached backend.
- Response was not a framework route 404.
- Response body was:
  - code: `MANUAL_PAYMENT_CONFIGURATION_INVALID`
  - message: `Manual Payment configuration is invalid.`

Exact reason:

- `AdminManualPaymentController.GetPendingPayments` calls manual-payment capability admission before querying pending `PaymentIntents`.
- `ManualPaymentTenantConfigurationResolver.AdmitAsync` calls `EnsureProviderConfiguration` before loading tenant `TenantPaymentMethods`.
- Current runtime manual-payment options do not satisfy `ManualPaymentOptions` validation/readiness.
- Controller maps manual-payment configuration failures to HTTP 404.

Answers:

- Exact endpoint exists: yes.
- Controller exists: yes.
- Route prefix identical: yes.
- HTTP verb correct: yes.
- Endpoint mapped: yes, via `MapControllers`.
- Authorization route mismatch: no evidence.
- Frontend outdated path: no.
- Endpoint renamed/retired: no.
- Current replacement endpoint: none required; this endpoint is current.

## 8. Manual Payment Configuration

Manual payment is configured in two layers:

- Global/runtime options: `CommerceHub:Payments:ManualPayment`, represented by `ManualPaymentOptions`.
- Tenant payment method: `TenantPaymentMethods` row with code `MANUALWALLET` and encrypted configuration validated by `PaymentMethodConfigurationValidator`.

Global `ManualPaymentOptions` validation requires, when enabled:

- provider enabled through `Enabled` or feature flags,
- at least one enabled channel,
- configured instructions/default instructions,
- customer proof requirement,
- configured positive expiration TTLs,
- capability certifications and feature flags for review/proof/approval workflows,
- runtime bridge and evidence capability where relevant.

Default `appsettings.json` has manual payment disabled/incomplete:

- `Enabled=false`
- `FeatureFlags.Enabled=false`
- review/proof/approval execution disabled
- instructions not configured
- expiration not configured
- channels empty

Tenant `MANUALWALLET` validation requires:

- `walletNumber`

Why UI says "Manual Payment configuration is invalid":

- Proven source is backend validation, not frontend precondition.
- The list endpoint returns backend code `MANUAL_PAYMENT_CONFIGURATION_INVALID`.
- The frontend displays the backend message.
- The "No pending manual payments found" text is secondary empty-table rendering because the payments array is empty after the failed load.

## 9. Provider State Machines

Common admin/backend model:

- Not configured: no `TenantPaymentMethods` row or missing required encrypted config.
- Configured: tenant row exists and `PaymentMethodConfigurationValidator` validates required fields.
- Enabled: `IsEnabled=true`.
- Available for checkout: enabled and configuration-valid, then visible through storefront read model/SQL fallback and accepted by checkout SQL validation.

Provider-specific notes:

- COD: requires no credentials; can be enabled without config; available only when tenant row exists, enabled, and not deleted.
- Stripe: requires `publishableKey`, `secretKey`, `webhookSecret`; checkout/webhook readiness also depends on runtime provider options/capabilities.
- Paymob: requires `apiKey`, `integrationId`, `iframeId`, `hmacSecret`; callbacks/webhooks require HMAC and capability flags.
- Fawry: requires `merchantCode`, `apiKey`/`securityKey`, `webhookSecret`; readiness depends on callback/webhook and SHA-256 signature support.
- ManualWallet: tenant config requires `walletNumber`; admin pending/review workflow additionally requires global manual-payment runtime options/capabilities.

Agreement gaps:

- A provider can exist as a configured SQL row but be unavailable for checkout if disabled, invalid, not projected, or rejected by runtime provider options.
- A provider can be configured in global options but invisible to admin/storefront if tenant `TenantPaymentMethods` rows are missing.
- Current admin GET shows only rows; it does not show the intended registry in `NOT CONFIGURED` state.

## 10. Stripe

Evidence:

- Admin code: `STRIPE`.
- Tenant config fields: `publishableKey`, `secretKey`, `webhookSecret`.
- Admin validator aliases `apiKey` to `secretKey`.
- Storefront direct route: `POST /api/storefront/payments/stripe/create-intent`.
- Status route: `GET /api/storefront/payments/stripe/status/{orderId}`.
- Config route: `GET /api/storefront/payments/stripe/configuration`.
- Webhook route: `POST /api/webhooks/stripe`.
- Webhook signature header: `Stripe-Signature`.
- Adapter creates Stripe PaymentIntents and uses idempotent provider request options.
- Webhook code treats provider webhook as source of truth for online payment status.

Credential handling:

- Tenant admin credentials are stored encrypted in `TenantPaymentMethod.ConfigEncrypted`.
- Stripe client secret is stored encrypted in `PaymentIntent.ClientSecretEncrypted`.
- Public response may expose publishable key and client secret as payment-session artifacts; secret API keys/webhook secrets are not exposed by admin DTOs.

No live external Stripe verification was performed, so live Stripe capability is not certified by this audit.

## 11. Paymob

Evidence:

- Admin code: `PAYMOB`.
- Tenant config fields: `apiKey`, `integrationId`, `iframeId`, `hmacSecret`.
- Storefront route: `POST /api/storefront/payments/paymob/intent/{orderId}`.
- Status routes: `GET /api/storefront/payments/paymob/status/{orderId}` and `GET /api/storefront/payments/paymob/status`.
- Webhook route: `POST /api/webhooks/paymob`.
- Callback route exists in `PaymobWebhooksController`.
- Webhook HMAC is read from `Hmac`/header or callback payload paths and verified with HMAC SHA-512 logic.
- Paymob runtime has replay records and webhook logs for idempotency.

Credential handling:

- Tenant credentials are loaded from encrypted `TenantPaymentMethod.ConfigEncrypted`.
- Sensitive Paymob session/webhook artifacts have protector/backfill services.
- DTOs inspected do not expose admin secret fields in read responses.

No external Paymob validation was performed.

## 12. Fawry

Evidence:

- Admin code: `FAWRY`.
- Tenant config fields: `merchantCode`, `apiKey`/`securityKey`, `webhookSecret`.
- Payment gateway path exists through `FawryPaymentGateway` and `PaymentService.CreatePaymentIntentAsync`.
- Runtime application service creates Fawry payment-intent application requests.
- Fawry webhook runtime parses callbacks, resolves tenant authority, verifies evidence, loads local `PaymentIntent`, and uses replay records.
- Signature/security code uses SHA-256-style hashing/signatures and protects sensitive webhook artifacts.

Route note:

- No `StorefrontFawryPaymentsController.cs` file was present under `PointOfSale.API/Controllers`.
- Fawry checkout is routed through the generic checkout/payment service path rather than a dedicated storefront Fawry controller in the inspected tree.

No external Fawry validation was performed.

## 13. Vodafone Cash / Manual Payment

Current implementation treats Vodafone Cash as a manual wallet channel/type, not an independent provider.

Evidence:

- Payment provider enum includes `ManualWallet`.
- Payment method code is `MANUALWALLET`.
- Checkout request carries `WalletType`, `WalletPhoneNumber`, and proof file key.
- Manual wallet instructions include a `VODAFONECASH` branch that says to transfer to a merchant wallet number and upload proof.
- Merchant wallet number is derived from tenant config, never trusted from browser request.
- Pending/admin approval workflow is under `/api/admin/payments/manual-wallet`.

Workflow:

- Customer selects `MANUALWALLET`.
- Checkout validates tenant method and proof reference.
- PaymentService creates a reviewable `PaymentIntent` with status `AwaitingVerification`.
- Admin pending list reads pending/awaiting-verification manual wallet intents.
- Admin approve/reject transitions payment/order state through manual-payment review application services.

The current runtime 404 blocks the admin pending-payment list before it can query any pending rows.

## 14. Cash on Delivery

COD is a payment method/special offline checkout mode, not an external gateway.

Evidence:

- Admin code: `COD`.
- `PaymentMethodConfigurationValidator` marks COD as not requiring configuration.
- Checkout treats non-gateway methods generically; `PaymentService` treats provider `Cod` specially.
- COD intent status is `Skipped`.
- COD order can be confirmed without gateway credentials.
- COD webhooks are explicitly rejected as a security violation.
- `OrderOperationalFinancialAuthority` allows outbound progress for COD without recognized online payment, while non-COD positive-total orders require paid/collected/settled state and accounting readiness.

COD still requires tenant `TenantPaymentMethods` row enabled for storefront checkout selection. It does not require external credentials.

## 15. Storefront Payment Flow

Storefront flow:

- Storefront fetches `GET /api/storefront/payment-methods`.
- Checkout state fetches shipping and payment methods.
- Customer selects shipping method and payment method.
- Checkout submits `POST /api/storefront/checkout`.
- Server validates headers, cart ownership, idempotency, cart snapshot, shipping method, payment method, currency, totals, inventory, and then creates order/payment intent.
- Online providers return payment-session artifacts.
- Webhooks/callbacks finalize provider state.
- Manual wallet awaits admin review.
- COD is offline and confirmed without provider webhook.

Authority boundaries:

- Payment method authority: SQL `TenantPaymentMethods` during checkout.
- Amount authority: server recalculated order total.
- Currency authority: tenant/storefront/cart/order consistency and gateway validation.
- Tenant authority: middleware/context and tenant-scoped SQL queries.
- Order authority: persisted `Order`.
- Payment finality authority: payment lifecycle service/webhook/review/COD authorities, not browser claims.
- Idempotency: checkout idempotency coordinator, order row locks, active payment intent unique index, webhook replay/idempotency records.

Browser is not authoritative for amount, currency, payment finality, provider state, tenant, or credentials in the inspected checkout path.

## 16. Shipping + Payment Interaction

Checkout validates shipping and payment together before creating the order/payment session.

Evidence:

- `ValidateShippingMethodAsync` loads SQL shipping authority by `ShippingMethodId`, `TenantId`, and `!IsDeleted`.
- It verifies active state and carrier/service readiness.
- `ValidatePaymentMethodAsync` loads SQL payment authority by code, `TenantId`, and `!IsDeleted`.
- It verifies enabled state and valid configuration.
- `CalculateCheckoutAsync` receives recalculated cart snapshot, authoritative shipping method, and authoritative payment method.
- `EnsureExpectedPricingMatches` compares expected vs actual `subtotal`, `discount`, `coupon_discount`, `promotion_discount`, `cart_promotion_discount`, `shipping`, `tax`, `payment_fee`, `manual_discount`, and `total`.
- Payment intent amount is created from `order.Total`; shipping amount is stored on the order totals.

Conclusion:

- The inspected checkout path preserves server-authoritative shipping/payment total calculation.
- Payment initialization does not accept browser-supplied shipping cost as authoritative.

## 17. Payment Finality

Payment intent statuses:

- `Pending`
- `Authorized`
- `Paid`
- `Failed`
- `Cancelled`
- `Expired`
- `Skipped`
- `AwaitingVerification`
- `Rejected`
- `AmountMismatch`
- `Suspicious`

Order payment statuses:

- `Pending`
- `Authorized`
- `Collected` / obsolete `Paid`
- obsolete `PartiallyPaid`
- `Failed`
- `Refunded`
- `PartiallyRefunded`
- `AwaitingVerification`
- `Rejected`
- `Settled`
- `CashOnDelivery`
- `Expired`
- `AmountMismatch`

Finality controls found:

- Online gateway finality is driven by provider-specific webhooks/callbacks.
- Generic webhook signature verification fails closed and points to provider-specific endpoints.
- COD webhooks are blocked.
- Manual wallet finality is driven by admin approve/reject workflow.
- PaymentService rejects duplicate active intents and non-pending order/payment states.
- Fulfillment progression uses `OrderOperationalFinancialAuthority` to block non-COD outbound progress before recognized payment and accounting readiness.

No confirmed browser-driven finality bypass was found in inspected paths.

## 18. Manual Payment Pending Workflow

Intended workflow:

- Customer selects `MANUALWALLET`.
- Checkout validates tenant payment method row and proof file key.
- `PaymentService.CreatePaymentIntentAsync` creates a reviewable manual wallet `PaymentIntent`.
- Status becomes `AwaitingVerification`.
- Optional events request admin/WhatsApp notification.
- Admin opens Manual Payments.
- Frontend calls `GET /api/admin/payments/manual-wallet/pending`.
- Backend admits `PendingReviewRead` capability.
- Backend queries tenant-scoped pending/reviewable manual wallet `PaymentIntents`.
- Admin approves or rejects through idempotent POST endpoints.
- Payment/order lifecycle services update final state.

Current blockage:

- The pending list fails at manual-payment configuration admission.
- Because the endpoint returns 404 before querying `PaymentIntents`, the admin pending workflow is currently blocked regardless of whether pending rows exist.

## 19. API Contract Matrix

| Frontend Endpoint | Backend Endpoint | Status | Notes |
| --- | --- | --- | --- |
| `GET /api/admin/payment-methods` | `GET /api/admin/payment-methods` | Match | Runtime returned `200 []`; cause of empty admin page. |
| `PUT /api/admin/payment-methods/{code}` | `PUT /api/admin/payment-methods/{code}` | Match | Updates display/settings row. |
| `PUT /api/admin/payment-methods/{code}/configure` | `PUT /api/admin/payment-methods/{code}/configure` | Match | Saves encrypted tenant config; disables on reset. |
| `POST /api/admin/payment-methods/{code}/enable` | `POST /api/admin/payment-methods/{code}/enable` | Match | Creates missing row, validates config if required. |
| `POST /api/admin/payment-methods/{code}/disable` | `POST /api/admin/payment-methods/{code}/disable` | Match | Disables tenant row. |
| `GET /api/admin/payments/manual-wallet/pending?page=1&pageSize=25` | `GET /api/admin/payments/manual-wallet/pending` | Match | Runtime reached backend; returned config-invalid 404. |
| `GET /api/admin/payments/manual-wallet/proof-image/{paymentIntentId}` | `GET /api/admin/payments/manual-wallet/proof-image/{paymentIntentId:guid}` | Match | Tenant-scoped proof access after admission. |
| `POST /api/admin/payments/manual-wallet/{paymentIntentId}/confirm` | Same | Match | Owner/Admin only; financial rate limit. |
| `POST /api/admin/payments/manual-wallet/{paymentIntentId}/reject` | Same | Match | Owner/Admin only; financial rate limit. |
| `GET /api/storefront/payment-methods` | `GET /api/storefront/payment-methods` | Match | Storefront selector, suppresses invalid methods. |
| `POST /api/storefront/checkout` | `POST /api/storefront/checkout` | Match | Server authoritative order/payment session creation. |
| `POST /api/storefront/payments/stripe/create-intent` | Same | Match | Dedicated Stripe intent route. |
| `GET /api/storefront/payments/stripe/status/{orderId}` | Same | Match | Dedicated Stripe status route. |
| `POST /api/storefront/payments/paymob/intent/{orderId}` | Same | Match | Dedicated Paymob intent route. |
| `GET /api/storefront/payments/paymob/status/{orderId}` | Same | Match | Dedicated Paymob status route. |
| `POST /api/payments/intents/{orderId}` | Same | Match | Generic payment intent route; manual wallet rejected outside checkout. |
| `POST /api/payments/cod/confirm/{orderId}` | Same | Match | COD confirmation route. |
| `GET /api/payments/orders/{orderId}/status` | Same | Match | Canonical payment status route. |
| `POST /api/webhooks/stripe` | Same | Match | Stripe webhook with signature verification. |
| `POST /api/webhooks/paymob` | Same | Match | Paymob webhook with HMAC verification. |
| Fawry dedicated storefront controller | Not found | Missing dedicated controller | Fawry uses generic/payment-service path in inspected code. |
| CommerceHub `POST /api/commercehub/payments/initiate` | Same | Match | CommerceHub payment initiation for marketplace flow. |

No stale frontend path was found for the reported manual-wallet pending request.

## 20. Tenant Isolation

Tenant isolation evidence:

- Admin payment methods query `TenantPaymentMethods` by resolved tenant.
- Manual pending query filters `PaymentIntents` by `TenantId` and `Provider == ManualWallet`.
- Proof-image endpoint loads tenant-scoped payment intent and rejects unsafe storage references.
- Checkout loads shipping and payment authorities by `TenantId`.
- Payment intent creation locks/loads orders by tenant and validates order customer tenancy.
- Webhook processing requires resolved tenant and matches payment intent by tenant.
- Payment method configuration uses tenant-owned `TenantPaymentMethod`.
- Unique tenant/code index exists for `TenantPaymentMethods`.
- Payment intent active unique index prevents duplicate active provider intents per order/provider.

No confirmed cross-tenant authority bypass was found in the inspected payment paths.

Residual risk:

- Webhook tenant resolution remains high-risk by nature; this audit did not perform external webhook replay attacks. Existing code has provider-specific signature and replay protections.

## 21. Security

Security controls found:

- Payment method secrets stored in `TenantPaymentMethod.ConfigEncrypted` using the `PaymentMethodSecrets` data-protection purpose.
- Admin read DTOs expose public configuration/status fields and not decrypted secret values.
- Stripe client secret stored encrypted on `PaymentIntent.ClientSecretEncrypted`.
- Paymob and Fawry sensitive webhook/session artifacts have protector/backfill services.
- Stripe webhook requires `Stripe-Signature`.
- Paymob webhook/callback verifies HMAC.
- Fawry webhook/runtime verifies signed evidence and protects raw artifacts/signatures.
- Generic webhook signature verification fails closed and instructs provider-specific routes.
- COD webhook attempts are rejected as security violations.
- Manual wallet merchant wallet number is derived from tenant config, not browser input.
- Manual admin approve/reject routes require admin roles and idempotency headers/rate limiting.

No secrets are included in this report.

## 22. Test Coverage

Payment-related tests found:

- Unit/service tests: `PaymentServiceCreatePaymentIntentGuardTests`, `PaymentsControllerTests`, `PaymentRequestAuthorizationServiceTests`, `PaymobCallbackValidationTests`, `FawryWebhookReliabilityTests`, `CanonicalPaymentStatusResolverTests`.
- Integration tests: `ManualWalletPaymentConcurrencyIntegrationTests`, `CodPaymentLifecycleTests`, `CodWebhookBlockingTests`, `PurchasePaymentIntegrityIntegrationTests`, `CheckoutSessionConsistencyIntegrationTests`.
- Admin/storefront tests: `PaymentMethodsAdminSafetyTests`, `PaymentMethodsAdminSyncReliabilityTests`, `StorefrontPaymentMethodAvailabilityTests`, `StorefrontManualWalletStorageTests`, `ManualWalletProofCleanupJobTests`.
- Architecture/certification tests: extensive suites for Stripe, Paymob, Fawry, ManualPayment, COD, PaymentRuntime, PaymentFinalAuthority, PaymentFinancialAuthority, PaymentScopeIsolationAuthority, StorefrontPaymentAuthority, StorefrontCheckoutPricingTotalsAuthority, and shipping/payment authority.
- Frontend tests: storefront payment flow and checkout completion tests, affiliate checkout frontend certification, UX stability tests.

Coverage appears broad for authority, finality, idempotency, configuration, and provider readiness. This audit did not run the test suite because the mission was forensic diagnosis, not code validation after changes.

Gap relevant to current symptoms:

- Tests do not appear to enforce that Admin Payment Methods GET must display the intended provider registry in `NOT CONFIGURED` state when tenant rows are absent.
- Tests did not prevent manual-payment configuration failures from surfacing as a route-looking 404 in the admin UI.

## 23. Symptom Classification

Symptom A:

- Classification: TENANT DATA ISSUE
- Proof: authenticated runtime `GET /api/admin/payment-methods` returned `200 []`; backend GET only returns `TenantPaymentMethods` rows for the resolved tenant; migrations are applied; frontend does not filter away returned rows.

Symptom B:

- Classification: CONFIGURATION
- Proof: authenticated runtime `GET /api/admin/payments/manual-wallet/pending?page=1&pageSize=25` reached `AdminManualPaymentController` and returned backend code `MANUAL_PAYMENT_CONFIGURATION_INVALID`; the endpoint exists and is mapped; current default/runtime manual-payment options are disabled/incomplete for pending-review admission.

## 24. P0/P1/P2/P3 Findings

P0:

- None confirmed in inspected paths.

P1:

- Admin Payment Methods page is unusable for the current tenant because no `TenantPaymentMethods` rows exist and GET does not synthesize intended provider entries.
- Manual Payment pending workflow is blocked because manual-payment runtime configuration is invalid/incomplete; pending list returns 404 before querying pending rows.

P2:

- Manual Payments UI can show "No pending manual payments found" alongside a configuration failure, which can be misread as an empty queue.
- Backend uses HTTP 404 for manual-payment configuration invalid, creating route-missing confusion.
- `TenantPaymentMethod` comments mention COD/Stripe/Paymob while current code also supports Fawry and ManualWallet.
- Admin registry/default behavior is mutation-only, making a missing tenant bootstrap look like no providers exist.

P3:

- Documentation/help text likely needs alignment after remediation, but documentation was not changed in this audit except this report.

## 25. Exact Root Causes

Root cause for Symptom A:

- The current runtime tenant has no payment-method rows returned by `TenantPaymentMethods`.
- The Admin Payment Methods GET endpoint returns only persisted tenant rows.
- Therefore frontend receives an empty array and renders `No payment methods available`.

Root cause for Symptom B:

- The Manual Payments pending endpoint exists and is reached.
- Before pending-row lookup, backend manual-payment admission validates global/runtime `ManualPaymentOptions`.
- Runtime manual-payment configuration is invalid/incomplete for `PendingReviewRead`.
- The controller maps that configuration failure to HTTP 404 with code `MANUAL_PAYMENT_CONFIGURATION_INVALID`.

## 26. Minimal Remediation Plan

Smallest safe remediation path:

1. Preserve current architecture: SQL tenant payment methods, storefront read projection/fallback, checkout SQL authority, provider runtime gates.
2. Restore tenant payment-method configuration rows for intended providers (`COD`, `STRIPE`, `PAYMOB`, `FAWRY`, `MANUALWALLET`) through an idempotent tenant-scoped bootstrap or existing admin mutation paths. Avoid inventing providers.
3. Configure only providers intended to be available. Leave credentialed providers disabled until required encrypted fields are supplied and validated.
4. For ManualWallet, configure both layers:
   - tenant `MANUALWALLET` row with valid wallet number,
   - global/runtime `CommerceHub:Payments:ManualPayment` options and capability flags/channels/instructions/expiration needed for review workflow.
5. Consider changing config-invalid manual-payment response semantics to `409` or `422` in a later remediation so 404 means missing route/resource, not invalid configuration.
6. Consider making Admin Payment Methods GET synthesize registry entries in `NOT CONFIGURED` state, or add an explicit tenant bootstrap step during tenant creation/recovery.

## 27. Migration Impact

No schema migration is required by the proven root causes.

The active runtime database reports all active migrations applied through `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`. Payment schema exists in active migration snapshots.

Potential remediation is data/configuration/bootstrap, not schema:

- If a migration is chosen to seed rows, it must be idempotent, tenant-scoped, and must not insert live credentials.
- A safer path is a controlled tenant bootstrap/repair operation using existing domain rules.
- Manual-payment global runtime options are configuration, not schema.

## 28. Verification Requirements

Before certifying remediation:

- `GET /api/admin/payment-methods` for tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` returns intended provider rows.
- Admin page no longer renders `No payment methods available`.
- Disabled/unconfigured providers are shown as disabled/not configured, not exposed to checkout.
- `GET /api/storefront/payment-methods` shows only enabled and configuration-valid methods.
- `POST /api/storefront/checkout` rejects disabled/missing/invalid payment methods.
- Manual payment options pass `ManualPaymentOptionsValidator`.
- `GET /api/admin/payments/manual-wallet/pending?page=1&pageSize=25` returns `200` with an empty list or real pending items, not config-invalid 404.
- Manual wallet approve/reject endpoints remain tenant-scoped and idempotent.
- Shipping/payment total verification still rejects client pricing mismatches including shipping.
- Provider webhooks still require signatures and replay protection.

## 29. Final Certification

DIAGNOSIS CONFIRMED — SAFE TO REMEDIATE

This certification is limited to diagnosis. No fixes, migrations, data seeding, endpoint changes, frontend changes, or configuration changes were performed.

## 30. Worktree Safety

Before audit:

- `git status --short`: `?? docs/architecture/SHOPIXY_RUNTIME_DATABASE_SCHEMA_DRIFT_FORENSIC_AUDIT_2026-08-12.md`
- `git diff --stat`: no tracked diff output.
- The dirty file was pre-existing and unrelated to this audit.

After report creation:

- `git status --short`:
  - `?? docs/architecture/SHOPIXY_PAYMENT_METHODS_FORENSIC_RECOVERY_AUDIT_2026-08-12.md`
  - `?? docs/architecture/SHOPIXY_RUNTIME_DATABASE_SCHEMA_DRIFT_FORENSIC_AUDIT_2026-08-12.md`
- `git diff --stat`: no tracked diff output.

Only allowed intended mutation:

- `docs/architecture/SHOPIXY_PAYMENT_METHODS_FORENSIC_RECOVERY_AUDIT_2026-08-12.md`

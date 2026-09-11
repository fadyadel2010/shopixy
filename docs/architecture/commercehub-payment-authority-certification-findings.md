# PASS M3 CommerceHub Payment Authority Certification Findings

Date: 2026-06-10

Status: CommerceHub Payment Authority is production-certifiable for the inspected runtime paths. No runtime code hardening was required; existing SQL, provider-evidence, lifecycle, idempotency, refund, payout, and reconciliation controls already satisfy the M3 authority model.

## Gate 3R payment configuration authority addendum

Date: 2026-09-06

Status: source-level Gate 3R authority hardening and regressions pass. The bounded existing-order
attempt exposed a frontend focus-revalidation lifecycle defect before payment initiation; that defect
is fixed and verified. No genuine Paymob success, provider ownership, or financial finality is claimed.

Gate 3R corrected a cross-domain configuration dependency discovered after M3. CommerceHub cart,
checkout, configuration status, and payment initiation previously treated vendor-owned Storefront
`TenantPaymentMethods` rows as CommerceHub payment-provider authority. Those rows remain authoritative
only for each vendor's Storefront. CommerceHub derives online-provider availability from platform
`IPaymentProviderAdapter` registrations and executable capabilities. COD remains CommerceHub platform
policy and does not require a vendor Storefront COD row.

Online-provider admission requires payment-session creation and webhook-verification capabilities;
Paymob also requires callback parsing. Stripe adapter capabilities reflect current platform Stripe
payment and webhook configuration. An unavailable provider is rejected with
`PAYMENT_METHOD_NOT_AVAILABLE` during checkout before reservation validation, distributed locks,
payment-row creation, or inventory mutation. Payment initiation independently rejects a provider
that is no longer executable with `PAYMENT_PROVIDER_NOT_CONFIGURED` before adapter execution.

Paymob platform configuration now requires API key, secret key, HMAC secret, integration id, positive
merchant identifier, public key, and HTTPS webhook/browser callback URLs before it is executable.
The Super Admin connection test authenticates the API key and requires the authenticated Paymob
profile merchant to equal the configured CommerceHub merchant. A successful authentication remains
`Warning`, not `Healthy`, because secret-key and integration ownership are not proven until Paymob
accepts an Intention request.

Signed Paymob callbacks are also checked against both configured integration and merchant ownership.
A merchant mismatch fails closed with `PAYMOB_MERCHANT_MISMATCH`. The Super Admin form validates API
key and merchant identifier and identifies its configuration as CommerceHub admission authority.

Regression and build evidence:

- Focused configuration, connectivity, adapter, authority, checkout, and payment-service suite:
  60 passed, 0 failed, 0 skipped.
- Broader `FullyQualifiedName~Paymob|FullyQualifiedName~PaymentAdmin|FullyQualifiedName~CommerceHubPayment`
  suite: 280 passed, 0 failed, 0 skipped.
- API Release build: 0 warnings and 0 errors.
- POS Web and CommerceHub production builds passed. Existing bundle-size and stale Browserslist data
  warnings remain non-blocking build debt.
- `git diff --check` reported no errors for the Gate 3R files.

Runtime environment evidence:

- POS Web `http://localhost:5173` and CommerceHub `http://localhost:5175`: HTTP 200.
- `/health/live`: HTTP 200 Healthy.
- `/health/marketplace`: HTTP 200 Healthy; Marketplace and CommerceHub platform dependencies were
  healthy with no missing PostgreSQL schema, MongoDB index, or CommerceHub search-index requirements.
- `/health/ready` and `/health`: HTTP 503. The shared Elasticsearch cluster was red and the separate
  CommerceHub search-sync ratio check was unhealthy; these remain environment readiness blockers.
- The running API process started before this hardening was built. These health results prove the
  surrounding environment only and are not deployment evidence for the new source.

### Gate 3R runtime provenance recheck

Rechecked at `2026-09-06T20:03:36Z` through `2026-09-06T20:04:32Z`. The mandatory current-runtime
prerequisite is **NOT MET**, so certification stopped before public HTTPS callback inspection,
Paymob dashboard inspection or mutation, Super Admin configuration, connection testing, and checkout.

- API PID `17204` is listening on local port `5010` from
  `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`. It started at
  `2026-09-06T18:27:34.5207872Z`.
- The loaded Debug API DLL was last written at `2026-09-06T18:23:23.6601501Z` and identifies commit
  `2a7a8477ebb4978085a99574226b5696fd5b33d1`. Its SHA-256 is
  `55ABD6BAEE5E1C1EC2BB01AB01E8F5591C8A79D3F05A0AECE0C4975DC9DBE3CA`.
- Current hardened source postdates that DLL. In particular,
  `PaymentProviderAdministrationService.cs` and `PaymobAdapter.cs` were last written at
  `2026-09-06T19:16:20Z`, and `PaymentProviderActionService.cs` was last written at
  `2026-09-06T19:19:46Z`. These files contain substantive uncommitted Gate 3R changes.
- POS Web on `http://localhost:5173/super-admin/commercehub/payment-providers` returned HTTP `200`.
  Its Vite process PID is `12736`, started at `2026-09-06T16:57:24.5196570Z`.
- CommerceHub on `http://localhost:5175/` returned HTTP `200`. Its Vite process PID is `5960`,
  started at `2026-09-06T16:57:37.0575927Z`.
- `GET /health/live` returned HTTP `200` with status `Healthy`.
- `GET /health/marketplace` returned HTTP `200` with status `Healthy`.
- `GET /health/ready` returned HTTP `503`.
- `GET /health` returned HTTP `503` with status `Unhealthy`. Shared Elasticsearch was RED and the
  CommerceHub search-sync ratio was unhealthy; these results are separate from payment-authority
  source correctness.

Classification: **EVIDENCE LIMITATION**. Runtime deployment evidence for the latest hardened source
is unavailable. This is not a claim of a source-level financial defect, and no runtime certification
is inferred from prior tests or builds. Integration `#3153089` was not inspected or modified, and
Integration `#3183622` was not configured or treated as certified during this stopped run. Gate 3R
remains **NOT YET COMPLETE**.

### Gate 3R restarted-runtime and public-callback recheck

The operator restarted the API, resolving the preceding runtime-provenance limitation. The mandatory
current-runtime prerequisite is now **PASS**:

- API PID `16672` is listening on local port `5010` from
  `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`. It started at
  `2026-09-06T20:06:38.5380857Z`.
- The loaded Debug API DLL was rebuilt at `2026-09-06T20:06:37.0859934Z`, after all identified Gate
  3R API source changes. Its SHA-256 is
  `6B403C0CA8EB381156B3CA24524E2635DC9AB9B95028DB6E11175EA24D688495`.
- POS Web `http://localhost:5173/super-admin/commercehub/payment-providers` returned HTTP `200`.
- CommerceHub `http://localhost:5175/` returned HTTP `200`.
- At `2026-09-06T20:07:58Z` through `2026-09-06T20:07:59Z`, `/health/live` and
  `/health/marketplace` returned HTTP `200 Healthy`; `/health/ready` and `/health` returned HTTP
  `503 Unhealthy`. Aggregate readiness remains separately affected by shared Elasticsearch and
  CommerceHub search-sync conditions; these results do not negate scoped payment runtime provenance.

The next mandatory prerequisite, a real externally reachable HTTPS endpoint forwarding to this exact
API runtime, is **NOT MET**:

- No process is listening on local HTTPS port `443`.
- No active Caddy, ngrok, cloudflared, Traefik, FRP, SSH reverse-tunnel, or localtunnel process was
  found.
- The only local edge listener is IIS/HTTP.sys on plain HTTP port `80`; it is not HTTPS and does not
  forward the required Shopixy callback routes to PID `16672`.
- `deploy/staging/Caddyfile` contains only placeholder `yourdomain.com` hosts, and Caddy is neither
  installed nor running.
- A localtunnel CLI is installed but no tunnel is active. Tool availability is not evidence of an
  externally reachable callback endpoint.
- Development Paymob callback settings remain localhost HTTP legacy routes, not the required public
  Shopixy endpoints.

Classification: **EXTERNAL PROVIDER / ENVIRONMENT LIMITATION** and, under the final defect taxonomy,
**ENVIRONMENTAL RESIDUAL**. Certification stopped before inspecting or changing Paymob Integration
`#3183622`, before Super Admin provider configuration, and before checkout. Integration `#3153089`
was not inspected or modified. No dashboard callback, customer charge, provider success, settlement,
payout, refund finality, or external transfer is claimed. Gate 3R remains **NOT YET COMPLETE**.

### Gate 3R live public-tunnel and payment-initiation continuation

Continued on `2026-09-06` after the operator supplied a temporary Development-only LocalTunnel public
base. This continuation supersedes only the earlier public-ingress absence; all other source and
authority conclusions remain in force.

Runtime and ingress evidence:

- API PID `16672` remained the listener on `localhost:5010`, using
  `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`. Its process start remained
  `2026-09-06T20:06:38.5380857Z`, and the loaded API DLL build timestamp remained
  `2026-09-06T20:06:37.0859934Z`.
- Local `GET http://localhost:5010/health/live` and public
  `GET https://dirty-spies-build.loca.lt/health/live` both returned HTTP `200 Healthy` from the
  current runtime. Active LocalTunnel processes explicitly forwarded local port `5010`.
- The configured CommerceHub Paymob endpoints were
  `POST https://dirty-spies-build.loca.lt/api/payments/paymob/webhook` and
  `GET https://dirty-spies-build.loca.lt/api/commercehub/payments/paymob/callback`.
- The temporary tunnel removed the public-HTTPS ingress prerequisite for this run. It is not a stable
  deployment endpoint and must not be treated as production infrastructure.

Authenticated Super Admin configuration evidence:

- The existing isolated Super Admin login was used at
  `http://localhost:5173/super-admin/commercehub/payment-providers`.
- Paymob was enabled in Sandbox through the rendered UI using CommerceHub Integration `#3183622`,
  merchant identifier `645875`, and the two public Shopixy callback URLs above. Integration
  `#3153089` was not modified or repurposed.
- The UI save returned `Payment provider configuration saved.` and status `Configured`.
- A full browser refresh read the saved configuration back as enabled, with Integration `#3183622`,
  merchant `645875`, and both callback URLs matching. The rendered validation summary contained zero
  errors. Secret values were not emitted into source, documentation, command output, or evidence.
- The authenticated `Test Connection` action completed in `279 ms` with status `Warning` and provider
  version `Paymob Accept API`: API-key authentication and merchant identity verification passed.
  The warning correctly stated that secret-key and integration ownership require Paymob to accept an
  Intention request.

CommerceHub frontend and live initiation evidence:

- Authenticated CommerceHub checkout rendered both Cash on Delivery and the platform-authorized
  `Pay with Card / Wallet` Paymob method. This proves current platform admission, not provider
  collection.
- The real frontend created disposable order `ORD-CH-0022`, marketplace order id
  `36e4b3ec-6c6d-41a2-98ed-c98e9df60703`, for one item from one store totaling EGP `750`. The order
  remained Payment Pending and exposed `Continue to payment`; no direct order/payment API or SQL
  mutation was used.
- The real frontend payment-initiation request returned HTTP `409 Conflict`. The correlated API log at
  `2026-09-06T21:18:52.906Z` recorded provider `PAYMOB` entering the canonical `IPaymentRuntime`
  boundary. At `2026-09-06T21:18:53.138Z`, `CommerceHubFinancialReadinessGate` rejected
  `CommerceHubPayment.Initiate` because latest reconciliation report
  `7936fcb8-3c85-4b62-abab-1a5f01c98089`, although `Healthy` at `2026-09-06T21:00:01Z`, did not prove
  coverage for vendor `c43aebf2-4cc4-406a-a2db-4df06d40bce5`.
- The exact reason code was `COMMERCEHUB_FINANCIAL_RECONCILIATION_VENDOR_SCOPE_REQUIRED`; the request
  completed HTTP `409` in approximately `281 ms`. The CommerceHub frontend surfaced the matching
  fail-closed message: `Outward money movement is blocked until financial reconciliation proves this
  vendor was checked.`
- Source ordering confirms the readiness gate runs at
  `CommerceHubPaymentService.cs:324-327`, before `SaveChangesAsync` at line 329 and before provider
  session creation at lines 338-346. Therefore this rejected request did not call Paymob Intention
  creation and cannot prove Integration `#3183622` ownership, provider acceptance, transaction
  creation, customer charge, webhook delivery, browser callback delivery, or provider finality.

Classification: **FINANCIAL READINESS / VENDOR RECONCILIATION SCOPE BLOCKER**. The control behaved
correctly and was not bypassed. Live Gate 3R execution stopped at the first authoritative failure as
required.

Supported recovery-path evidence:

- There is no Super Admin mutation endpoint or button that runs financial reconciliation. The
  authenticated finance endpoint at `GET /api/super-admin/finance/reconciliation` only computes and
  returns a read snapshot and records the view in the Super Admin audit log.
- The sole normal full-report writer is the Hangfire recurring job
  `commercehub-finance-reconciliation`, registered as `Cron.Hourly` (`0 * * * *`). It invokes
  `CommerceHubFinanceReconciliationJob.ExecuteAsync`, which invokes
  `IFinancialReconciliationService.RunAsync`, persists the immutable report and finance audit, and
  then performs its independently guarded unresolved-payment recovery sweep.
- At the source-inspection time `2026-09-06T21:22:01Z`, the next normal run boundary was
  `2026-09-06T22:00:00Z`. An unauthenticated read probe to the local Hangfire dashboard returned HTTP
  `401`; no dashboard authorization was bypassed and no direct job-trigger request was sent.
- `FinancialReconciliationService.LoadContextAsync` includes every non-deleted sub-order created at
  or before the report snapshot boundary. `ResolveCheckedVendorIds` adds each such sub-order's
  `StoreId`, so the already persisted pending sub-order for `ORD-CH-0022` should place vendor
  `c43aebf2-4cc4-406a-a2db-4df06d40bce5` in the next report's `checkedVendorIds`.
- This does not manufacture a clean result for an unpaid order: ledger-presence validations are
  conditional on financial recognition, and a sub-order is financially recognized only after its
  payment status is Collected, Settled, Refunded, or Partially Refunded. Independent order-math,
  promo, refund, orphan, provider-lifecycle, aggregate, and other reconciliation checks still run and
  can legitimately make the new report Warning or Critical.
- The readiness gate accepts vendor-scoped reports only when they are no more than two hours old and
  do not contain a Critical mismatch affecting that vendor. The scheduled report outcome must be
  observed rather than assumed.

Do not retry `ORD-CH-0022` until the scheduled reconciliation writer produces a current report whose
`checkedVendorIds` includes vendor `c43aebf2-4cc4-406a-a2db-4df06d40bce5` and whose status is accepted
by the readiness gate. After that prerequisite is observed, resume through the existing CommerceHub
frontend `Continue to payment` action so the same order can request its first genuine Paymob
Intention. Gate 3R remains **NOT YET COMPLETE**; Gate 4 was not started.

### Gate 3R scheduled-reconciliation and replacement-tunnel continuation

Continued on `2026-09-07` after the earlier temporary LocalTunnel endpoint was closed. The current
Development-only public base for this continuation was `https://wicked-months-sin.loca.lt`, forwarding
to the API on local port `5010`. The earlier `dirty-spies-build.loca.lt` URLs were not reused or
treated as current.

Current runtime and ingress evidence:

- Port `5010` belonged to API PID `3500`, executable
  `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`, started at
  `2026-09-07T10:59:26.4261150Z`. Active LocalTunnel processes explicitly targeted port `5010`.
- Public `GET https://wicked-months-sin.loca.lt/health/live` repeatedly returned HTTP `200 Healthy`,
  proving the replacement tunnel reached the current runtime.
- Public `GET https://wicked-months-sin.loca.lt/health/marketplace` reached the same runtime but
  repeatedly returned HTTP `503 Unhealthy`: the Marketplace check itself was Healthy, PostgreSQL
  schema and CommerceHub Elasticsearch alias checks passed, but the CommerceHub MongoDB probe timed
  out. Public `/health/ready` was also HTTP `503` because MongoDB was unavailable and the shared
  Elasticsearch cluster was red. These dependency failures remained explicit and were not hidden.
- The readiness response independently exposed latest financial reconciliation timestamp
  `2026-09-07T11:00:16.0022330Z`, status `Healthy`, and mismatch count `0`.

Scheduled reconciliation verification:

- A repeatable-read, read-only SQL transaction tied `ORD-CH-0022` to marketplace order
  `36e4b3ec-6c6d-41a2-98ed-c98e9df60703` and vendor
  `c43aebf2-4cc4-406a-a2db-4df06d40bce5`.
- Latest immutable report `56513b25-54dc-49d1-8014-61d34c2dd94f` was `Healthy`, had zero mismatches,
  included three vendors in `checkedVendorIds`, included the order's vendor in that checked set, and
  did not include it in `affectedVendorIds`.
- At verification, the report was within the readiness gate's two-hour maximum age. Applying the
  exact vendor-scope, freshness, and Critical-affected-vendor rules yielded accepted payment
  financial readiness. No report was inserted, edited, or manually triggered.

Current Paymob configuration read-back:

- The operator updated the two callback fields through the real authenticated Super Admin UI. A
  rendered UI read-back at
  `http://localhost:5173/super-admin/commercehub/payment-providers` confirmed Paymob enabled in
  Sandbox, Integration ID `3183622`, merchant identifier `645875`, and no unsaved changes.
- Current webhook URL:
  `https://wicked-months-sin.loca.lt/api/payments/paymob/webhook`.
- Current browser callback URL:
  `https://wicked-months-sin.loca.lt/api/commercehub/payments/paymob/callback`.
- The UI showed no operational messages. Provider configuration/health remained `Warning`, with
  provider version `Paymob Accept API`, because a genuine accepted Intention is still required to
  prove secret-key and integration ownership. Browser-rendered secret values were not collected or
  documented; the temporary clipboard used for page-text verification was cleared.
- Storefront Integration `#3153089` was not inspected, modified, or repurposed.

Existing-order resume result:

- No new order or checkout was created. The authenticated customer session was used only with
  existing `ORD-CH-0022`.
- The dedicated `/orders/{id}/recover` frontend route timed out because it requires MongoDB order
  projection repair/read while current MongoDB health was unavailable. No payment initiation was
  sent from that failed read view.
- After the operator started the Ubuntu VM hosting MongoDB, public `/health/live` and
  `/health/marketplace` both returned HTTP `200 Healthy`; the latter confirmed the CommerceHub Mongo
  collection exists with no missing indexes. Reloading the existing order after dependency recovery
  still rendered the same reservation-authority block. MongoDB recovery resolved the projection
  availability issue but did not and must not recreate expired SQL reservation authority.
- The authenticated SQL-backed `/orders/{id}` customer page successfully rendered `ORD-CH-0022`,
  payment method Paymob, total EGP `750`, and `PAYMENT UNDER REVIEW`. It did not render a
  `Continue to payment` button. Its exact authority message was: `Payment retry is blocked because
  the marketplace order no longer has active SQL reservation authority. Restart checkout so
  inventory and payment authority are recreated from current SQL truth.`
- Source authority at `CommerceHubPaymentRecoveryAuthorityService` requires every expected online
  order item to retain matching active, unexpired SQL `InventoryReservations` coverage before retry
  can be offered. Read-only SQL found one historical reservation for `ORD-CH-0022` but zero active,
  unexpired reservations.
- Read-only SQL also found zero `CommerceHubPayments`, zero `CommerceHubPaymentAttempts`, and zero
  Paymob intent, transaction, or checkout-URL evidence for the order. Therefore this continuation did
  not call Paymob, create a provider session, charge the customer, or exercise either current callback
  endpoint.

Classification: **INVENTORY RESERVATION AUTHORITY LOSS / TERMINAL EXISTING-ORDER BLOCKER**. Financial
reconciliation readiness now passes, but `ORD-CH-0022` cannot legitimately initiate payment. The
supported UI explicitly requires restarting checkout, which would create replacement order authority;
that action was prohibited for this run. The reservation gate was not bypassed, no reservation was
extended or recreated directly, and no payment mutation was attempted. Gate 3R remains **NOT YET
COMPLETE** and stopped at this authoritative blocker. Gate 4 was not started.

### Gate 3R guarded reservation reacquisition and one-shot continuation

Continued on `2026-09-08` without creating a replacement order. This continuation corrects the prior
classification of reservation expiry as necessarily terminal. A pending online order with no payment
or provider evidence can safely reacquire reservation authority from current SQL inventory truth, but
only while holding the same order-scoped payment lock and only after all original order, catalog,
variant, quantity, stock, lifecycle, tenant, and financial-readiness authority has been revalidated.

Source hardening:

- `InventoryConsistencyService.ReacquireMarketplaceOrderReservationsForPaymentAsync` locks the
  reservation history and marketplace-order authority, validates the original order item identity and
  quantity against current catalog and variant lineage, and uses
  `Quantity - ReservedQuantity - DamagedQuantity` as available stock.
- Reacquisition rejects partial reservation authority, changed or deleted catalog authority,
  insufficient stock, any payment or provider evidence, and soft-deleted historical payment or
  webhook evidence. A completed reacquisition is replay-safe.
- `CommerceHubPaymentRecoveryAuthorityService` now exposes
  `RequiresReservationReacquisition` only for pending online orders that have provable current
  reacquisition authority and no payment, unresolved-provider, or soft-deleted evidence.
- `CommerceHubPaymentService.InitiateAsync` reloads lifecycle authority under the payment lock,
  revalidates access, currency, provider, and unresolved-payment authority, runs financial readiness
  before zero-evidence reservation mutation, reacquires when required, runs readiness again, and
  rechecks current order authority immediately before provider-session work.
- The customer order query always refetches current backend authority when mounted or focused, and
  `Continue to payment` explicitly refetches before initiation. Generic non-retryable presentation no
  longer labels an unclassified failure as `Payment Under Review`.

Regression and build evidence:

- Focused updated authority suite: 81 passed, 0 failed, 0 skipped.
- Real PostgreSQL concurrent reacquisition test: passed; two concurrent callers produced exactly one
  active reservation for quantity `2`, leaving available quantity `3`.
- CommerceHub suite: 318 passed, 0 failed, 0 skipped.
- Paymob suite: 213 passed, 0 failed, 0 skipped.
- CommerceHub payment suite: 54 passed, 0 failed, 0 skipped.
- CommerceHub financial suite: 51 passed, 0 failed, 0 skipped.
- API Release build completed with zero warnings and zero errors. CommerceHub and POS Web production
  builds passed. `git diff --check` found no whitespace errors; only working-tree LF-to-CRLF notices
  were emitted.

Pre-attempt runtime authority evidence:

- The rebuilt Release API listened on `localhost:5010`; CommerceHub listened on `localhost:5175`.
  Local and public `/health/live` and `/health/marketplace` returned HTTP `200` through
  `https://wicked-months-sin.loca.lt`. The marketplace report was Healthy with zero drift and no
  pending migrations.
- Immutable reconciliation report `ec77cec5-d914-43d6-9b7f-d297d374113e`, timestamp
  `2026-09-08T11:00:00.574334Z`, was Healthy with zero mismatches, covered all three checked vendors,
  included vendor `c43aebf2-4cc4-406a-a2db-4df06d40bce5`, and did not mark that vendor affected. It
  was within the readiness gate's two-hour limit.
- Read-only SQL found checkout idempotency authority for key
  `b497e2a3-7d63-4107-880b-0cd1d78426f8` and request hash
  `0A0ED33CE2F466508822A62BF9FE32DA85B92EE8DE063B112762AE11FD303622`.
- Read-only SQL found zero payments, payment attempts, provider identities, provider transactions,
  provider URLs, payment logs, and unresolved webhooks for `ORD-CH-0022`, including soft-deleted
  evidence. The exact recovery service returned `CanRetryPayment=true`,
  `RequiresReservationReacquisition=true`, and `HasReservationAuthorityLoss=true` with no warning.
- The authenticated customer page rendered existing order `ORD-CH-0022`, Paymob, EGP `750`, and one
  enabled `Continue to payment` action. No replacement order was created.

One-shot execution and post-attempt evidence:

- The single authorized physical click was consumed between
  `2026-09-08T12:57:38.7121051Z` and `2026-09-08T12:57:38.7321676Z` at screen coordinates
  `x=1566`, `y=338`. It must not be repeated during this certification run.
- Foreground activation completed `GET /api/customers/auth/me` with HTTP `200` in `75.3681 ms`,
  correlation id `6d7c0a40-846d-4233-904c-81f58948ce2e`. It then completed
  `GET /api/customers/orders/36e4b3ec-6c6d-41a2-98ed-c98e9df60703` with HTTP `200` in
  `190.0668 ms`, correlation id `624cc0ee-081a-45af-992d-d23cda100a11`.
- The durable API log contains no corresponding
  `POST /api/commercehub/payments/initiate`, `CommerceHubPayment.Initiate` boundary entry, payment
  handler exception, reservation reacquisition, or Paymob adapter call. The page did not navigate to
  Paymob. The consumed click therefore did not reach payment initiation.
- The first lifecycle cause was the two payment pages rendering their initial skeleton whenever
  `orderQuery.isFetching` became true. Window focus starts a background order refetch, so the rendered
  payment action was unmounted even though current order data remained available.
- The second lifecycle cause was `CustomerSessionBootstrap` using route-withholding revalidation for
  focus, visibility, and online events. That called `beginRouteAuthorityValidation()`, set
  `hasHydrated=false`, and caused `RequireCustomerAuth` to replace the entire protected route while
  the background session check was pending. A pre-fix DOM probe observed the payment button transition
  from one instance to zero and back to one across this focus boundary.
- `CustomerOrderDetailsPage` and `OrderPaymentRecoveryPage` now reserve skeleton presentation for
  `orderQuery.isLoading`; background `isFetching` preserves the mounted action. Both actions still
  `await orderQuery.refetch()` and validate current retry authority before
  `checkoutService.initiatePayment(...)`.
- `CustomerSessionBootstrap` now performs focus, visibility, online, interval, and initial checks as
  background `revalidateSession()` calls without withholding route presentation. `popstate` and
  persisted `pageshow` navigation retain `revalidateSession(true)` and the hard route-authority gate.
- The post-fix OS terminal-to-Edge foreground DOM probe collected `349` samples over four seconds with
  `mutationCount=0`, `minimumCount=1`, `maximumCount=1`, and `zeroSampleCount=0`. The durable API log
  simultaneously confirms successful session checks and a successful current-order GET at
  `2026-09-08T13:19:16.784Z`, correlation id `d49cda77-1060-4dda-9e6a-3a5a4db40f16`, without any
  payment POST. This verifies that authority revalidation remains active without unmounting the
  payment action.
- Focused frontend-authority regression coverage passed `23/23`. It locks initial-loading-only
  skeletons, pre-initiation current-order refetch ordering, non-withholding background session checks,
  and route-withholding history navigation. The final CommerceHub production build also passed; only
  existing Browserslist and chunk-size warnings remained.
- A post-attempt repeatable-read, read-only SQL check observed immutable reconciliation report
  `eee7d309-5778-415e-8fca-bd33e737029a`, timestamp
  `2026-09-08T13:00:07.810471Z`, as `Healthy` with zero mismatches, accepted vendor coverage, and no
  affected order vendor. It found `paymentCount=0`, `paymentAttemptCount=0`,
  `providerEvidenceCount=0`, `paymentLogCount=0`, `unresolvedPaymentCount=0`, and
  `activeUnexpiredReservationCount=0`. Recovery authority remained `CanRetryPayment=true`,
  `HasReservationAuthorityLoss=true`, and `RequiresReservationReacquisition=true`.
- The attempt therefore created no SQL payment authority, reacquired no stock, established no Paymob
  Intention or transaction identity, delivered no callback, and cannot be treated as provider
  acceptance, customer charge, or financial finality.

Classification: **FRONTEND LIFECYCLE DEFECT FIXED; PROVIDER EXECUTION EVIDENCE NOT OBTAINED**. The
guarded existing-order recovery path and mounted-action correction are source-, regression-, build-,
and runtime-verified, and the one permitted attempt failed closed without inventory, payment, or
provider mutation. Because no genuine Paymob Intention was accepted, Integration `#3183622`
ownership remains unproven and Gate 3R remains **NOT YET COMPLETE**. Integration `#3153089` was not
inspected, modified, or repurposed. Gate 4 was not started.

### Gate 3R instrumented POST, country correction, and Paymob outcome-unknown continuation

The earlier no-POST boundary is now closed. Development-only, non-secret browser trace stages were
added from both customer payment handlers through current-order refetch, retry-authority validation,
`checkoutService.initiatePayment`, the shared HTTP request/response interceptors, and the final handler
outcome. Focused frontend authority coverage remained green (`23/23`) and the CommerceHub production
build passed.

The first post-fix physical frontend click reached
`POST /api/commercehub/payments/initiate?lang=en` and returned HTTP `400` with
`MISSING_STORE_COUNTRY`. A repeatable-read, read-only SQL snapshot tied the failure to vendor
`c43aebf2-4cc4-406a-a2db-4df06d40bce5` having `Tenants.Country = NULL`. The attempt created local
failed payment `9f9a4ff6-d489-47c9-9dc6-1152428dd23f`, failed attempt
`5485c547-fa0b-45e5-86cf-414e53e6009f`, and `initiate.paymob.failed`; its provider-intent values were
internal `PROVIDER_SESSION_REINIT_FENCE:` sentinels, not Paymob identity. It created no checkout URL,
transaction id, provider event id, unresolved webhook, or financial finality.

The vendor operator then configured country `EG` through the authenticated vendor Settings frontend.
Read-only SQL verified `Country=EG`, `Currency=EGP`, active tenant status, and update timestamp
`2026-09-08T15:01:14.823317Z`. The same snapshot verified exact platform Paymob configuration:
Sandbox, Integration `3183622`, merchant `645875`, canonical public webhook and browser-callback URLs
under `https://bright-items-hope.loca.lt`, and protected credential names only. Local and public
`/health/live` and `/health/marketplace` returned HTTP `200`; no credential values were exposed.

Exactly one explicitly authorized retry then reached the same frontend-originated POST. It returned
HTTP `503` with `PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED`, trace id
`00-567cac820721c61f462ee37b0e016709-b700b871b5189763-01`, and detail stating that Paymob intention
creation had an unknown outcome. The immediate repeatable-read snapshot at
`2026-09-08T15:13:16.521009Z` found:

- pending local payment `e548556d-b49c-45f5-8f29-9c1bee2ec2f6`, EGP `750`, Paymob;
- pending attempt `15f60bb8-a1d4-490f-8404-76f941540410`;
- `initiate.paymob.outcome_unknown` with no provider event id;
- only `PROVIDER_SESSION_REINIT_FENCE:` values, no genuine Paymob intention id, transaction id, or
  checkout URL;
- zero unresolved webhook rows;
- active reservation `bcf697b9-b910-462c-a96a-d11634737c39`, whose provider-reconciliation grace
  protects it even after its nominal `2026-09-08T15:25:26.489072Z` expiry;
- immutable reconciliation report `31ac2f3d-e60d-4719-b827-22965817603a`, timestamp
  `2026-09-08T15:00:03.628301Z`, `Healthy`, zero mismatches, vendor checked and unaffected.

Source diagnosis found a new liveness defect in modern Paymob intention creation: every
`PaymobOfficialApiException`, including definitive HTTP 4xx rejections, was classified as an
indeterminate provider outcome. That incorrectly strands known rejections behind a duplicate-
prevention fence. The adapter now treats definitive client-error responses as canonical known
rejections while retaining outcome-unknown protection for HTTP `408`, `429`, 5xx, transport failure,
timeout, and invalid successful response bodies. Regression coverage passed `19/19`, including known
4xx and indeterminate status families.

The operator then searched the API debug view for trace
`00-567cac820721c61f462ee37b0e016709` and found no retained result. In the authenticated Paymob
dashboard for MID `645875`, Test mode, the operator inspected both Orders and Transactions and saw
only historical August 2026 records; there was no current EGP `750` order or transaction matching
the pending provider reference `e548556db49c45f58f299c1bee2ec2f6`. This is authentic operator
dashboard evidence that no provider session is visible, but absence in the UI is not treated as a
fabricated callback, provider success, or automatic SQL authority.

A supported Super Admin reconciliation action was therefore added to the existing Payment Providers
frontend. It accepts only the exact local payment id and provider reference, the fixed evidence code
`PAYMOB_DASHBOARD_NO_ORDER`, a bounded operator note, and explicit administrator confirmation. The
server acquires the same provider/order payment lock as initiation and, under a serializable payment
boundary, fails closed unless the payment is pending Paymob outcome-unknown, has only internal
reinitialization sentinels, has no checkout token/URL, transaction id, provider event id, unresolved
webhook, or paid sibling payment, and the marketplace order remains pending payment. A successful
resolution marks the payment and latest attempt `Failed` and retains
`initiate.paymob.outcome_reconciled_not_created`; it does not mark the order paid or manufacture any
provider identity. Reservation recovery was also narrowed so only terminal failed payments with no
external identity or provider event cease to block guarded reacquisition; deleted, pending, paid,
non-terminal, or genuine-provider-evidence payments still block.

Focused reconciliation, recovery-authority, and inventory tests passed `39/39`; the Super Admin
component tests passed `2/2`. API Release built with `0` errors and `91` existing warnings, and the POS
Web production build passed. The live API has not yet been restarted on these fixes, and no
reconciliation action or additional customer payment retry has been performed yet.

The final pre-runtime provenance check found API PID `14032` still executing the older Debug binary
started at `2026-09-08T17:51:44.9087326+03:00`; its Debug DLL timestamp predates the reconciliation
implementation. Local `/health/live` remained HTTP `200`. Local `/health/marketplace` returned HTTP
`503` solely because the MongoDB subcheck timed out while the Marketplace entry itself reported
Healthy with zero drift; this is recorded as environmental health, not payment failure. Both public
`bright-items-hope.loca.lt` health paths returned `503 Tunnel Unavailable`. Execution is therefore
stopped until the operator restarts the API from current source and supplies a live replacement
tunnel URL. No frontend reconciliation or payment retry is authorized before those checks pass.

The first operator restart attempt at `2026-09-08T16:36:37.327Z` exposed a separate startup defect:
`PaymobSensitiveArtifactBackfillHostedService` attempted to replace a legacy plaintext
`CommerceHubPaymentLog.RawResponse` with its protected representation, while the CommerceHub
immutability guard correctly prohibited every post-creation RawResponse change. Startup failed before
the host or message bus became operational; this was not a provider request and caused no payment,
order, inventory, or financial mutation.

The security backfill now enters a dedicated, depth-counted sensitive-artifact backfill scope. Only
within that scope may the persistence guard permit `CommerceHubPaymentLog.RawResponse` to change;
the normal payment boundary alone still cannot rewrite the field, and all other immutable log fields
remain protected. Regression coverage now proves both successful legacy CommerceHub log protection
and rejection of an arbitrary RawResponse rewrite outside the security scope. The combined Paymob
backfill, provider-session reconciliation, recovery-authority, and inventory slice passed `48/48`;
the current Debug API build completed with `0` errors. A second operator start is required before any
runtime reconciliation is attempted.

The operator completed that restart and supplied replacement tunnel
`https://wicked-pants-lick.loca.lt`. Runtime provenance verified API PID `20632`, process start
`2026-09-08T19:46:31.0170985+03:00`, and Debug API DLL timestamp
`2026-09-08T16:44:00.3464515Z`; the process started after the compiled artifact. Both local and public
`/health/live` returned HTTP `200`. The aggregate `/health/marketplace` returned HTTP `503` only
because its MongoDB health check timed out; its embedded Marketplace result remained `Healthy`, with
zero drift and healthy PostgreSQL/Elasticsearch dependencies. This is an environmental aggregate
health limitation, not payment-authority evidence.

The operator saved the replacement callbacks through the actual Super Admin frontend. A
repeatable-read, read-only SQL snapshot at `2026-09-08T16:56:24.383253Z` verified Sandbox Paymob
Integration `3183622`, merchant `645875`, webhook
`https://wicked-pants-lick.loca.lt/api/payments/paymob/webhook`, browser callback
`https://wicked-pants-lick.loca.lt/api/commercehub/payments/paymob/callback`, and protected secret
names only. It also verified tenant country `EG`, currency `EGP`, current `Healthy` reconciliation
`9662df6c-3c93-4303-94ce-63203103bfce` with zero mismatches and the vendor checked/unaffected, zero
unresolved webhooks, and both reservations fully released. The ambiguous payment and attempt remain
pending with only internal reinitialization sentinels and no transaction, checkout URL, provider
event, or other provider identity. The startup backfill left both existing Paymob CommerceHub payment
logs protected (`ProtectedCount=2`, `UnprotectedCount=0`); no payload was decrypted or printed.

One final at-rest edge was then closed before allowing the frontend reconciliation mutation. The new
administrative reconciliation event now protects its serialized evidence through
`IPaymobSensitiveArtifactProtector` before the immutable log is inserted, rather than relying on a
future startup backfill to convert it. Its focused reconciliation, recovery-authority, and inventory
slice passes `39/39` in Release while the running Debug executable remains locked by PID `20632`.
Therefore one final operator restart from current source is required before clicking the
reconciliation action. No reconciliation or additional payment initiation has occurred.

The operator completed that final restart. Runtime verification found API PID `14080`, started
`2026-09-08T19:58:09.4242320+03:00`, loading Debug DLL timestamp
`2026-09-08T16:54:04.4162915Z`, which is newer than the final reconciliation-service source update.
Local `/health/live` returned HTTP `200`, and the replacement public tunnel
`https://wicked-pants-lick.loca.lt/health/live` independently returned HTTP `200`. A final
repeatable-read snapshot at `2026-09-08T16:59:59.016848Z` reconfirmed the exact callbacks,
`ProtectedCount=2`, `UnprotectedCount=0`, zero unresolved webhooks, fully released reservations, and
the untouched pending target payment/attempt with no provider transaction, checkout URL, provider
event, or external intention identity. The bounded Super Admin reconciliation action is now
runtime-authorized exactly once; customer payment retry remains prohibited until its post-action SQL
state is verified.

The operator then executed that reconciliation exactly once through the real Super Admin frontend;
the UI returned `Provider-session outcome was reconciled as not created. The customer may retry
through the CommerceHub frontend.` The immediate read-only snapshot at
`2026-09-08T17:03:09.962008Z` verified payment
`e548556d-b49c-45f5-8f29-9c1bee2ec2f6` and attempt
`15f60bb8-a1d4-490f-8404-76f941540410` are `Failed` with the canonical reconciled-not-created reason,
while order `ORD-CH-0022` remains pending payment. Immutable event
`bb713bcc-428f-464c-a249-75c6f915647d` is
`initiate.paymob.outcome_reconciled_not_created`, has no provider event id, and raised the protected
Paymob CommerceHub log count to three with zero unprotected records. Unresolved webhooks remain zero;
no provider transaction, checkout URL, payment token, paid timestamp, or external intention identity
exists. The current financial reconciliation report remains `Healthy`, zero mismatches, with the
vendor checked and unaffected. Super Admin audit row `1202` independently records the successful
`PAYMENT_PROVIDER_SESSION_RECONCILED_NOT_CREATED` action against the exact payment id and provider
reference from source `SuperAdminAPI`.

Post-action path inspection exposed a final handoff mismatch before any customer retry: recovery
authority correctly permits guarded reservation reacquisition after evidence-free terminal failed
payments, but `CommerceHubPaymentService.InitiateAsync` selected reacquisition only when the order had
zero historical payment rows. Because this order now has two evidence-free failed payments and fully
released reservations, the running code would call the active-reservation assertion and fail before
the provider. Initiation now selects the same guarded reacquisition path when every historical payment
lacks reservation-reacquisition-blocking evidence; any pending/non-terminal payment, checkout/token,
external provider identity, transaction, provider event, or unresolved webhook still fails closed in
the inventory service. A dedicated failed-payment handoff regression passes, and the combined payment
service, reconciliation, recovery, inventory, and architecture slice passes `61/61`. The Debug API
must be restarted once more to load this final initiation-branch correction. No post-reconciliation
customer payment attempt has been made.

The operator completed the restart. Runtime provenance verified API PID `23412`, started
`2026-09-08T20:13:20.3669738+03:00`, using Debug DLL timestamp
`2026-09-08T17:13:18.6046971Z`, newer than the corrected payment-service source. Local and public
`/health/live` both returned HTTP `200`. A repeatable-read snapshot at
`2026-09-08T17:14:02.034440Z` reconfirmed the two terminal failed payments, reconciled encrypted log,
successful Super Admin audit, zero unresolved webhooks, released reservations, pending order, current
Healthy zero-mismatch reconciliation, and exact Integration `3183622`/MID `645875` callbacks on the
replacement tunnel. Exactly one customer-frontend `Continue to payment` action is therefore
authorized; execution must stop again at the first returned provider or API result for immediate
evidence capture.

The authorized customer retry then reached Paymob and received a definite HTTP `404` configuration
rejection under trace `00-63f019c01d591da2305564332ea7eaf3-6739325ad1cfd1b9-01`: `Integration
ID/Name does not exist in our system`. The immediate read-only snapshot at
`2026-09-08T17:17:35.98559Z` verified third payment
`bce7c34e-e674-4003-a91f-fa231b0a9b52` and attempt
`3f8e82ec-6e7e-4082-8b7b-300e06f8ce45` were terminal `Failed`, retained only internal
reinitialization fences, and had no provider transaction, checkout URL, payment token, provider event,
or external intention identity. Order `ORD-CH-0022` remained pending, zero unresolved webhooks existed,
its reservation was safely reacquired, and fresh financial reconciliation remained `Healthy` with zero
mismatches. This is definite evidence that Integration `3183622` is not available to the configured
CommerceHub Intention API context; it is not an unknown provider outcome and requires no session
reconciliation.

The authenticated Paymob dashboard subsequently showed both `3183622` and `3153089` as `Shopify`
integrations. The operator then saved `3153089` as the CommerceHub integration and retried, receiving
`PAYMENT_ATTEMPTS_EXCEEDED` under trace
`00-cf8ed47c8aad3e5b73ad65cf26082dc9-e0450683098daa11-01`. A read-only snapshot at
`2026-09-08T17:21:25.099029Z` proved the fourth request was blocked before mutation or provider access:
the same three payment/attempt rows remained, the active reservation remained, and unresolved webhook
count remained zero. Configuration history independently showed `3153089` was saved at
`2026-09-08T17:19:54.148889Z` with the exact public tunnel webhook/callback URLs.

The operator's Integration `3153089` details screenshot then established that its Webhook URL and
Redirect URL both remain `http://shopixy.localhost:5174/checkout/success`, its payment method is `VPC`,
and its type is `Shopify`. It therefore belongs to the POS tenant Storefront path and is not valid
CommerceHub platform authority. No further retry is authorized with `3153089`. CommerceHub needs a
dedicated Paymob integration compatible with the direct Intention API and the platform's public
webhook/callback URLs.

The operator then supplied the authenticated detail view for Integration `3153090`. It is EGP,
channel `online_new`, payment method `UIG`, type `Non-Shopify`, and belongs to the same MID `645875`.
Its saved Paymob dashboard Webhook and Redirect URLs both use Paymob's own
`https://accept.paymobsolutions.com/api/acceptance/post_pay` endpoint. The operator's old-dashboard
method table provides the decisive semantic label: `3153090` is **Mobile Wallet**, not Online Card.
It is therefore not the CommerceHub card candidate despite its Non-Shopify type. Integration
`3153089` is Online Card but remains Shopify-owned and changing its URLs would both repurpose the POS
tenant's working Storefront integration and leave its Shopify integration class unchanged. The safe
requirement is a separate Test-mode **Online Card, Non-Shopify/API** integration under MID `645875`.
`PaymobAdapter.CreateModernSessionAsync` will supply that integration id and the public CommerceHub
`notification_url` and `redirection_url` in every Intention request. No existing integration is now
authorized for another CommerceHub attempt.

The operator subsequently confirmed that this Sandbox account cannot create another integration and
explicitly authorized repurposing Integration `3153089` for CommerceHub while dropping the POS tenant
Storefront Paymob path from this certification. This is a conscious ownership reassignment, not proof
that Paymob's direct Intention API accepts a Shopify-class integration. The integration's dashboard
Webhook and Redirect URLs and the Super Admin CommerceHub configuration must both be changed to the
same fresh public tunnel endpoints only after the corrected API runtime is loaded. Exactly one
controlled CommerceHub attempt may then determine whether Paymob accepts `3153089`; another definite
integration rejection must stop execution without further retries.

The corrected API was subsequently restarted as PID `11020` at
`2026-09-08T20:47:58.011335+03:00`, and the operator configured Integration `3153089` with tunnel
`https://good-cases-pay.loca.lt`. A controlled attempt at `2026-09-08T17:50:33Z` received the same
definite Paymob integration-not-found rejection. Payment
`8ca7e763-69aa-4c5a-881a-fe6feff47ba2` and attempt
`4533b9e6-e20c-495d-b373-6ce42fa0928a` are terminal `Failed`, retain only internal fences, and have
no provider event, external intention, checkout, token, or transaction identity. The order remains
pending, zero unresolved webhooks exist, and a new uncommitted reservation was safely reacquired.
Startup backfill and insert-time protection are both proven: all five Paymob CommerceHub logs are now
protected and zero remain plaintext.

A local read-only, prefix-only credential audit decrypted no value to output and confirmed the saved
CommerceHub Secret Key and Public Key are both Sandbox keys matching the configured Sandbox
environment, with Integration `3153089` and MID `645875`. Environment mismatch therefore does not
explain the repeated rejection. The same audit found the active `shopixy` tenant Paymob configuration
actually uses Integration `3153058`, not `3153089`, with Sandbox credentials and no legacy iframe ID.
The authenticated Paymob integration table previously identified `3153058` as `VPC`, EGP,
`Non-Shopify`. This is materially stronger evidence for the existing modern Online Card integration
than the operator's browser-return observation for `3153089`. Because the operator has explicitly
removed POS Storefront Paymob from scope, `3153058` may be evaluated as the next bounded CommerceHub
candidate only after its Paymob detail page is visually confirmed and both callback ownership and
Super Admin configuration are deliberately reassigned. `3153089` is now definitively rejected for
the current CommerceHub Intention path and must not be retried.

Two source defects exposed during this sequence are now closed. Paymob CommerceHub lifecycle logs
protect their payload at insertion (including success, known failure, unknown outcome, persistence-gap,
refund, and canonical-completion paths), and the protector is now a required service dependency. The
single plaintext failure log already written remains eligible for the existing startup backfill.
Retry-budget accounting now limits evidence-bearing external provider sessions, while definite local
or configuration failures that created no external provider identity remain recoverable after the
configuration is corrected. Three genuine evidence-bearing failures still enforce the ceiling. The
combined payment service, reconciliation, recovery, inventory, and architecture slice passes `62/62`
in Release. These changes have not yet been loaded into the running Debug API; restart and post-start
SQL verification are still required, and no customer retry may occur before the dedicated platform
integration is selected.

A final pre-restart repeatable-read snapshot at `2026-09-08T17:37:48.193007Z` reconfirmed the exact
three evidence-free failed payments and attempts, zero external provider identities/events,
pending/pending order state, zero unresolved webhooks, active uncommitted reservation, current
`3153089` warning configuration, and `ProtectedCount=3` / `UnprotectedCount=1`. Local health remained
HTTP `200` on ports 5010 and 5175, but the `wicked-pants-lick.loca.lt` tunnel was no longer reachable.
The running API still uses the older Debug DLL from `2026-09-08T17:13:18.6046971Z`; the corrected
source is newer. A clean isolated Debug build completed with zero errors (existing warnings only).

Classification: **FRONTEND POST PATH PROVEN; DEFINITE PROVIDER CONFIGURATION REJECTION**. Integration
`#3183622` was rejected by Paymob, while Integration `#3153089` is proven Storefront/Shopify-owned and
must not be used for CommerceHub. All downstream collection, fulfillment, settlement, payout, and
post-payout refund stages remain unproven. Gate 3R remains **NOT YET COMPLETE** and Gate 4 has not
started.

Earlier read-only evidence for `ORD-CH-0017` confirmed one live pending EGP 2,150 marketplace order,
two pending suborders totaling EGP 650 and EGP 1,500, three failed Stripe attempts with durable
reinitialization fences, and no provider transaction id. Both vendors had Storefront Stripe enabled,
while `CommerceHubPaymentProviders` platform settings had no rows. No payment retry, duplicate order,
refund, payout, provider success, or financial SQL mutation was performed.

An unauthenticated initiation probe against that order returned HTTP 401 with
`ORDER_ACCESS_DENIED`. A subsequent read-only transaction reconfirmed the unchanged order and payment
rows and found zero related vendor earnings, refunds, payouts, financial audit rows, and financial
transactions.

Storefront Paymob Integration `#3153089` remains Storefront-only evidence. It was temporarily entered
as CommerceHub configuration by the operator, but the attempt ceiling prevented any new payment row or
provider call after that change. It must be replaced before runtime continuation. Integration
`#3183622` under MID `645875` is now definitively incompatible with the configured CommerceHub
Intention API context because Paymob rejected it as nonexistent; its dashboard type is also `Shopify`.

The general Stripe and Paymob operational health checks still include Storefront/tenant readiness and
must not be interpreted as CommerceHub provider availability. CommerceHub uses platform adapter
capability admission; the Super Admin provider connection action tests the platform-owned account.

During the earlier baseline represented by `ORD-CH-0017`, no authenticated Super Admin configuration
or customer checkout was performed because browser automation and direct mutation APIs were excluded.
That historical limitation was superseded by the later public-tunnel and `ORD-CH-0022` continuations
above. Those continuations still did not obtain genuine Paymob webhook delivery, collection finality,
refund, settlement, or payout evidence.

No operator-supplied payment credential was copied into source, documentation, command output, or
runtime logs during this certification.

The operator later pasted Paymob credential material directly into the conversation while asking to
verify the active configuration. No credential value was repeated, copied into a command, written to
the workspace, or added to this report. The exposed API key, Secret Key, and HMAC secret must be
treated as compromised and rotated in Paymob before further provider execution. Exact replacement
values must be entered only through the authenticated Super Admin secret-replacement fields. The
read-only local audit is limited to non-secret classifications and confirms only that the currently
stored keys have Sandbox prefixes, not that they equal the subsequently pasted values.

### Storefront Integration 3153089 versus CommerceHub comparison

This comparison uses operator-supplied authenticated dashboard screenshots and read-only local
evidence. It does not claim that the operator-observed Storefront behavior proved provider finality.

1. **The observed localhost behavior proves browser return, not server callback delivery.** The
   reported Integration `#3153089` processed/response URLs both targeted
   `http://shopixy.localhost:5174/checkout/success`. A customer's browser running on the development
   machine can follow that URL because `localhost` resolves on that same machine. A Paymob server
   sending a Transaction Processed notification cannot reach that local listener because its own
   `localhost` is not the developer's computer. The Storefront route is also a legacy frontend route
   that immediately rewrites `/checkout/success` to `/checkout/pending`; it is not a webhook handler
   (`PointOfSale.Storefront/src/app/router.tsx:83-87`).

2. **Storefront browser evidence is deliberately non-final.** The Paymob form treats `success` and
   `error_occured` query parameters only as evidence that the browser returned, removes those values,
   and enters pending (`PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx:23-31`,
   `122-134`). The pending page polls the canonical backend status every three seconds for at most 20
   attempts (`PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx:22-23`, `83-169`, `208-210`).
   Only canonical `PAID` can route to success
   (`PointOfSale.Storefront/src/features/checkout/paymentFlow.ts:86-90`, `132-140`).

3. **Storefront finality remains webhook/lifecycle owned.** The current tenant flow sends separate
   `notification_url` and `redirection_url` values in its Paymob intention
   (`PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs:433-454`). The notification
   endpoint is `POST /api/webhooks/paymob`; it verifies HMAC, tenant configuration, integration,
   merchant, amount, currency, replay, and lifecycle evidence before mutation
   (`PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:995-1177`). Its browser callback
   validates HMAC and stored routing context but only returns navigation data
   (`PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:1843-2012`). Historical P9
   inspection found zero Paymob webhook logs and no provider transaction id, so the available evidence
   does not prove Integration `#3153089` ever delivered genuine server-to-server finality.

4. **CommerceHub uses separate platform payment authority.** CommerceHub does not consume a vendor's
   Storefront `TenantPaymentMethods` row or inherit Integration `#3153089`. Its modern intention
   explicitly sends the platform-configured `webhookUrl` and `browserCallbackUrl`, along with the
   configured CommerceHub integration id
   (`PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs:467-529`). The expected endpoints are
   `POST /api/payments/paymob/webhook` and
   `GET /api/commercehub/payments/paymob/callback`
   (`PointOfSale.API/Controllers/CommerceHubPaymentsController.cs:136-153`). Integration `#3183622`
   remains only a candidate until its merchant/integration ownership and accepted Intention are
   verified through the platform configuration flow.

5. **CommerceHub browser callback cannot collect money locally.** `ValidateCallbackAsync` delegates
   HMAC parsing to `PaymobAdapter`, resolves an existing CommerceHub payment, and returns identifiers;
   it performs no payment mutation
   (`PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:716-771`). The controller then
   redirects to `orders/{orderId}?payment=pending`
   (`PointOfSale.API/Controllers/CommerceHubPaymentsController.cs:150-194`). A callback query that says
   success therefore cannot mark a CommerceHub payment or order paid.

6. **Only admitted webhook evidence reaches CommerceHub collection mutation.** The webhook path
   verifies HMAC through `PaymobAdapter.ParseWebhookAsync`, checks configured integration and merchant
   ownership, resolves provider/payment identity, and validates amount/currency before calling
   `ApplyWebhookOutcomeAsync`
   (`PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs:218-277`, `595-625`;
   `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:818-1061`). That method acquires
   the payment lock, starts a serializable transaction, reloads SQL authority, applies duplicate and
   order-state guards, and only then marks the payment and marketplace order collected and invokes the
   financial workflow (`CommerceHubPaymentService.cs:1568-2009`).

7. **HTTPS has two related but distinct purposes.** Public reachability is indispensable for the
   server-to-server webhook: Paymob must be able to connect to the API from its own network. The browser
   callback only needs to be reachable by the customer's browser, which explains why a local browser
   could follow the Storefront localhost URL during development. CommerceHub nevertheless requires
   both URLs to be absolute HTTPS before enabling Paymob
   (`PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs:775-898`). Requiring HTTPS
   for the browser callback protects signed callback data and gives deployed customers a valid public
   return destination; it is not evidence that the browser callback owns financial finality.

8. **Neither ordinary status path substitutes provider inquiry for a missing webhook.** Storefront's
   canonical status endpoint reads local lifecycle state; its Paymob-specific status call supplies
   local retry authority (`PointOfSale.API/Controllers/PaymentsController.cs:302-353`;
   `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:960-987`). CommerceHub's
   `GetPaymentStatusAsync` likewise reads `CommerceHubPayments` SQL state only
   (`PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs:641-696`). `PaymobAdapter`
   implements transaction retrieval, but current platform-backed capabilities advertise status lookup
   as false and no CommerceHub invocation of adapter `GetStatusAsync` was found
   (`PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs:45-58`, `351-386`). A public signed
   webhook, or a separately designed reconciliation workflow that admits inquiry evidence through the
   same authority controls, is therefore required for genuine provider finality.

Source-level operational gap: unlike Storefront, CommerceHub currently has no callback-specific
pending poll. The callback redirects to the customer order-details page, whose order query now
refetches on mount and window focus but has no `refetchInterval`
(`PointOfSale.CommerceHub/src/features/customer/queries.ts`). If the initial browser-return refetch
wins a race with the webhook and the page remains focused, it remains safely pending but may not
display the later collected state until another focus event, explicit refetch, or manual refresh. This
is a customer-state convergence gap, not a path to false payment success, and it should be remediated
before live CommerceHub Paymob runtime certification.

Direct conclusions:

- Integration `#3153089` could appear to work locally because the development browser could return to
  its own localhost Storefront. That observation does not establish that Paymob's server reached a
  webhook or that backend payment finality completed.
- There is no available evidence that `#3153089` delivered a genuine server-to-server callback; the
  historical SQL evidence says the opposite for the inspected local attempts.
- CommerceHub must not reuse Storefront payment authority. Configure and verify its own platform
  integration only after a public HTTPS webhook and browser callback base is available.
- The minimum external prerequisite for genuine CommerceHub Paymob finality is a stable public HTTPS
  route to the exact hardened API runtime for `POST /api/payments/paymob/webhook`. A public HTTPS
  browser callback is also mandatory under current configuration policy and for a usable customer
  return, but it remains navigation-only.
- No tunnel, Paymob integration, platform provider configuration, checkout, payment row, order, or
  financial state was created or changed during this comparison.
- Focused `CommerceHubPaymentServiceTests`, `CommerceHubPaymentAuthorityCertificationTests`, and
  `PaymentRemediation002RuntimeAuthorityClosureTests` verification passed: 22 passed, 0 failed, 0
  skipped. Existing analyzer warnings remain unrelated test-project debt.

Gate 3R runtime files modified:

- `PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs`
- `PointOfSale.API/Services/Admin/PaymentProviderActionService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentMethodAuthority.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs`
- `PointOfSale.CommerceHub/src/features/customer/queries.ts`
- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`
- `PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx`
- `PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx`
- `PointOfSale.Web/src/super-admin/payment-providers/components/PaymobConfigurationForm.tsx`

Gate 3R regression files modified:

- `PointOfSale.Tests/Architecture/PaymentAdmin2PaymobProviderConfigurationTests.cs`
- `PointOfSale.Tests/Architecture/PaymentAdmin3ProviderConnectivityAndHealthTests.cs`
- `PointOfSale.Tests/Architecture/PaymobProviderAdapterCanonicalIntegrationTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- `PointOfSale.Tests/Architecture/CommerceHubFrontendAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CommerceHubCheckoutReservationCertificationTests.cs`
- `PointOfSale.Tests/Architecture/FrontendFinancialAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/MarketplaceSplitOrderAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymentActionAuthorityCertificationTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentMethodAuthorityTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentRecoveryAuthorityServiceTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubPaymentServiceTests.cs`
- `PointOfSale.Tests/CommerceHub/InventoryConsistencyServiceTests.cs`
- `PointOfSale.Tests/Integration/InventoryReservationConcurrencyIntegrationTests.cs`

## Payment identity findings

CommerceHub payment identity is owned by PostgreSQL `CommerceHubPayments`, `CommerceHubPaymentItems`, `CommerceHubPaymentAttempts`, and `CommerceHubPaymentLogs`, tied to the SQL `CommerceHubMarketplaceOrder`. Provider identity is linked through unique SQL provider reference, provider intent, and provider transaction boundaries. Paid records require processed state, paid timestamp, and provider transaction evidence, and only one live paid payment may exist per marketplace order.

Payment identity cannot be spoofed from DTOs, routes, frontend state, projections, caches, or stale events. Initiation reloads the SQL marketplace order, validates customer/order-access authority, checks current payment-method and reservation authority, acquires a payment lock, and creates or reuses SQL payment rows only inside the CommerceHub payment mutation boundary.

## Collection findings

Collection truth derives from signed provider evidence plus stored SQL payment/order truth. Stripe webhooks use `EventUtility.ConstructEvent`; Paymob webhooks use HMAC verification. Provider amount and currency are compared to the SQL payment record before collection is recognized. Provider identity is checked against other payment rows before mutation.

Payment success runs under the payment boundary, payment-lifecycle boundary, order lifecycle boundary, row reloads, distributed payment lock, and serializable SQL transaction. Duplicate provider success for an already paid order is not recognized again; it is routed to compensation/refund handling. Unmatched captured money is persisted as unresolved payment evidence for suspense/reconciliation rather than discarded or silently accepted.

## Vendor earnings findings

Vendor earnings are generated only by `CommerceHubFinancialWorkflowService` after online payment success or COD delivery collection. Earnings derive from immutable SQL sub-order financial snapshots and the collected payment id for online orders. Live original earnings are database-unique per sub-order through the filtered `CommerceHubVendorEarnings.SubOrderId` index where `OriginalEarningId IS NULL AND IsDeleted = false`.

Earnings cannot inflate through replay because existing non-pending earnings must match the locked financial snapshot and cannot be reassigned to another payment id. Earnings cannot suppress refunds incorrectly because refunded or partially refunded payment states cannot create or unlock vendor earnings.

## Marketplace fee findings

Marketplace fee truth derives from SQL sub-order commission snapshots: platform commission, promo discount, final commission, and vendor earning fields. `CommerceHubFinancialWorkflowService` validates commission snapshot JSON against persisted fields and verifies arithmetic before ledger or earning creation.

Marketplace fees cannot diverge from collected value on inspected paths because provider collection must match payment amount/currency, order/sub-order financial snapshots are checkout-created SQL truth, and ledger posting consumes those snapshots rather than projection or frontend totals.

## Escrow findings

CommerceHub Payment Authority may create collected value truth but does not create settlement truth. Online captured funds become platform-held SQL/ledger facts; vendor earnings remain pending/available according to delivery, payout delay, ledger confirmation, payout reservation, and payout status. Payout services, not payment collection, own outward release.

Escrow/holding state cannot release early on inspected paths because payout creation requires available earnings, settlement delay, ledger confirmation, currency consistency, payout readiness, and payout reservation. Payment collection does not bypass payout, settlement, treasury, or readiness gates.

## Refund interaction findings

Refund authority remains separate. Provider refund webhooks update provisional refund evidence only after payment/currency matching, duplicate refund detection, remaining-refundable bounds, and immutable payment-item checks. Refund services own completed refund lifecycle, reversal amounts, payout-overlap handling, and reconciliation-required states.

Refunds cannot corrupt payment authority on inspected paths because over-refunds fail closed, duplicate provider refund events collapse, missing immutable payment-item authority is flagged for manual review, and vendor earning reversals are modeled as refund-owned adjustments rather than rewriting payment identity or completed payout history.

## Reconciliation findings

Reconciliation is derivative. It may discover, persist, classify, route to suspense, or report unresolved external-money evidence, but it cannot create collected value without re-entering the SQL payment authority boundary. Unresolved captured payment and refund evidence remains explicit and readiness-blocking until reconciled.

Financial reconciliation validates payments, refunds, vendor earnings, ledger rows, payouts, and aggregate financial consistency from SQL. It can prove internal SQL correctness, not external gateway truth without provider evidence.

## Currency findings

CommerceHub payment currency is SQL payment/order authority and platform-base marketplace currency. Provider callbacks must match the persisted payment currency; mismatches reject, quarantine, flag manual review, or route unresolved evidence instead of relabeling money. Vendor earnings, fees, refunds, ledger rows, and payout eligibility remain currency-scoped.

No inspected path lets provider payloads, tenant display currency, frontend state, projections, dashboards, or stale snapshots corrupt CommerceHub payment currency.

## Tenant-isolation findings

Payment initiation requires authenticated customer ownership or signed order-access evidence. Webhook ingress is tenantless at HTTP host level but resolves authority from signed provider evidence plus stored SQL payment/order rows. Vendor earnings and payout/read surfaces filter by trusted tenant/store id.

No inspected path allows foreign payments, earnings, allocations, or refunds to be viewed or mutated through route ids, DTO tenant ids, projections, or cache state.

## Concurrency findings

Payment initiation uses per-order/provider locks, unresolved payment checks, active reservation checks, provider-session persistence fences, provider identity uniqueness, and SQL uniqueness. Webhook processing uses per-payment locks and serializable SQL transactions. Paid payment uniqueness, provider reference uniqueness, provider transaction uniqueness, webhook duplicate checks, and compensation routing prevent duplicate collection from creating duplicate value.

Concurrent callbacks, payment retries, and reconciliation attempts converge to collected, ignored, compensation-required, retry, or reconciliation-required states rather than duplicate earnings or duplicate fees.

## Historical-consistency findings

Payment records, payment items, attempts, logs, sub-order financial snapshots, vendor earnings, refund evidence, and ledger transactions are SQL history. EF guards block direct mutation of payment, refund, earning, and financial lifecycle fields outside their boundaries. Reruns use existing SQL rows and idempotency/fingerprint evidence rather than rebuilding financial history from projections.

Stale snapshots cannot resurrect payment authority because payment recognition reloads current SQL order/payment/reservation state at mutation time.

## Adversarial findings

Payment spoofing is blocked by signed provider evidence and SQL amount/currency/provider identity matching. Duplicate collection is blocked or routed to compensation. Vendor earning inflation is blocked by SQL snapshot validation, live original earning uniqueness, payment-id locking, and payout ledger confirmation. Vendor earning suppression through refund replay is blocked by refund-owned bounded adjustments and payment states that cannot unlock earnings after refund.

Fee manipulation is blocked by immutable SQL commission snapshots and arithmetic validation. Escrow bypass is blocked by payout readiness, ledger confirmation, and payout reservations. Refund corruption is blocked by cumulative amount bounds, duplicate refund detection, and reconciliation-required states. Currency manipulation is blocked by SQL/provider currency comparison. Tenant guessing, DTO tampering, route tampering, projection poisoning, and stale event replay are derivative-only and fail closed on inspected paths.

## Runtime hardening performed

No runtime code changes were required for PASS M3. The inspected runtime already contained the required controls.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/CommerceHubPaymentAuthorityCertificationTests.cs`

## Architecture updates

Updated this findings file and all five `.ai-context` authority files with PASS M3 payment authority truth and freeze rules.

## Explicit answers

- Who owns CommerceHub payment authority? PostgreSQL `CommerceHubPayments`, `CommerceHubPaymentItems`, `CommerceHubPaymentAttempts`, provider logs, unresolved webhook evidence, marketplace order payment fields under `CommerceHubPaymentLifecycleAuthority`, and financial workflow/ledger rows under their certified boundaries.
- Can payments be replayed? Provider and initiation replay cannot duplicate collected value on inspected paths; replay collapses to existing SQL state, no-op, compensation, retry, or reconciliation-required handling.
- Can vendor earnings diverge from payment truth? No on inspected paths; online earnings are payment-id-linked and SQL snapshot-derived, COD earnings require delivery/COD collection authority, and live original earnings are unique per sub-order.
- Can marketplace fees diverge from collected value? No on inspected paths; fees derive from SQL financial snapshots validated before ledger/earning creation and collection must match SQL payment amount/currency.
- Can escrow state drift? No inspected drift path was found; held/pending/releasable value remains SQL/ledger/payout-readiness derived and payment collection cannot create settlement truth.
- Can refunds corrupt payment authority? No on inspected paths; refund lifecycle is separate, bounded, idempotent, and reconciliation-aware.
- Can reconciliation create value? No. Reconciliation is derivative and must re-enter payment/refund authority before mutation.
- Can tenant leakage occur? No inspected path exposed or mutated foreign payment, earning, refund, or allocation authority.
- Can currency corruption occur? No inspected path allowed provider/frontend/projection currency to override SQL payment currency.
- Were runtime code changes required? No.
- Is CommerceHub Payment Authority production-certifiable? Yes, for the inspected runtime authority paths, with broader launch monitoring, alerting, external retention, and PostgreSQL-backed chaos evidence remaining operational maturity debt.

## Gate 3R Paymob declined-callback rollback correction (2026-09-08)

The first genuine modern Paymob session was accepted only after CommerceHub was configured with the
existing VPC/Online Card/Non-Shopify Integration `3153058`. Paymob created provider order
`605400359` for marketplace payment `d2cc699c-db89-4937-8d68-4e80b79db117` and rendered its hosted
EGP 750 card checkout. This proves real provider-session creation but does not prove collection.

The genuine sandbox card attempt returned a terminal failed transaction with transaction identity
`531263455`, `success=false`, `pending=false`, the expected EGP 750 amount, Integration `3153058`,
profile/MID `645875`, and the provider message `Invalid credentials.` The first browser return ended
in `INTERNAL_ERROR`. A repeatable-read SQL snapshot taken afterward proved the failure had not been
applied: the payment and its attempt were still Pending, the marketplace order payment status was
still Pending, and reservation `6d5ed57a-47c9-454c-b187-9bc89980f0e0` remained active.

The exact callback defect had two parts:

1. Modern Paymob returns the configured MID as `profile_id` while `owner` is a different Paymob
   account-owner identity. `PaymobAdapter.EnsureConfiguredIntegration` incorrectly compared the
   configured MID to `owner`, so this authentic callback threw `PAYMOB_MERCHANT_MISMATCH` and escaped
   as an internal error.
2. Even a verified terminal browser failure was treated only as a routing signal. It did not enter
   the existing idempotent canonical failure workflow, so immediate stock rollback depended entirely
   on a separate server webhook.

The correction now parses `profile_id`, validates the configured MID against it with `owner` retained
only as a legacy fallback, preserves query-form merchant reference and provider failure details, and
converts adapter validation exceptions into a safe invalid-callback result instead of an unhandled
500. A HMAC-verified terminal browser failure now enters the same payment-locked, serializable,
replay-safe failure workflow as the webhook. That workflow marks the payment and attempt Failed,
records provider transaction/failure evidence in a protected payment log, resets online order payment
state for retry, and calls `ReleaseMarketplaceOrderReservationsAsync`. Captured success remains
webhook-authoritative; the browser callback does not recognize collected money.

Focused regression evidence after the correction: `39 passed`, `0 failed`, `0 skipped` across
`CommerceHubPaymentServiceTests` and `PaymobProviderAdapterCanonicalIntegrationTests`. The tests cover
modern `profile_id`/`owner` separation, query-form failure detail preservation, immediate decline
rollback, protected evidence, no success-side financial effects, and callback replay idempotency.

After the corrected API restart, the operator refreshed the already-authentic failed browser callback
once. It redirected to the customer order route with `payment=failed`. A repeatable-read SQL snapshot
at `2026-09-08T18:25:53.143792Z` confirmed the rollback: payment
`d2cc699c-db89-4937-8d68-4e80b79db117` and its sole attempt are Failed and processed, provider order
`605400359` and provider transaction `531263455` are retained, the failure reason is
`Invalid credentials.`, the marketplace order is OnHold with payment Pending for a fresh checkout,
and reservation `6d5ed57a-47c9-454c-b187-9bc89980f0e0` is fully Released (`1.00/1.00`). The CALLBACK
payment log is present and the Paymob protection audit reports `8 protected / 0 unprotected` payloads.
A separate read-only negative financial check returned zero vendor earnings, zero CommerceHub
financial transactions, zero financial audit entries, and zero accounting outbox rows for this
declined payment/order scope. No callback was fabricated or invoked directly by diagnostic tooling.

## Gate 3R canonical Development success lifecycle certification (2026-09-09)

This continuation certifies the maximum lifecycle scope that can be proven without forging provider
evidence, bypassing authenticated frontend authority, fabricating an external transfer, or mutating
financial data directly. Repository HEAD was
`2a7a8477ebb4978085a99574226b5696fd5b33d1` during verification.

### Development collection seam, isolation, and runtime corrections

- `POST /api/dev/commercehub/payments/{paymentId}/simulate-success` remains on a
  `[DevelopmentOnlyController]`, is hidden from API exploration, requires the Super Admin policy, and
  independently returns Not Found outside Development. Its UI and HTTP client are also guarded by
  `import.meta.env.DEV`.
- The controller requires explicit administrator confirmation and records a Super Admin audit action.
  The response and audit evidence state `IsDevelopmentSimulation=true` and
  `ExternalMoneyMovementConfirmed=false`.
- `ExecuteDevelopmentSuccessAsync` accepts only platform-owned online provider payment rows, derives a
  deterministic `DEV-SIM:ONLINE:` reference from the payment and idempotency key, and enters the same
  private `ApplyWebhookOutcomeAsync` collection workflow used after admitted provider evidence. It
  does not invoke the public webhook parser or manufacture callback/HMAC evidence.
- Development success is limited to an unprocessed Pending payment or an exact replay of the same
  simulation. A processed or otherwise non-Pending payment is rejected with
  `DEVELOPMENT_PAYMENT_SIMULATION_STATE_INVALID` before payment-attempt resolution or financial
  mutation.
- The runtime attempt against `ORD-CH-0023` exposed that the canonical financial mutation scope ended
  before `ApplyWebhookOutcomeAsync` persisted financial snapshot changes. The persistence ownership
  guard correctly rejected the save and rolled the transaction back. The financial-workflow scope
  now remains active through the canonical success save and commit, and a relational regression
  exercises the real persistence guard.
- The retry against that stale order then encountered expired reservation authority. Development
  simulation now converts that condition to `DEVELOPMENT_PAYMENT_SIMULATION_RESERVATION_INVALID`
  without creating captured-money compensation. A compensation row created by the pre-correction
  runtime was safely closed by the worker as
  `DEVELOPMENT_PAYMENT_SIMULATION_NOT_COLLECTED`; it did not resolve or call a refund gateway.
- Earnings linked to a `DEV-SIM:ONLINE:` payment are rejected by canonical payout creation with
  `PAYOUT_DEVELOPMENT_SIMULATION_NOT_TRANSFERABLE`. A simulated collection therefore cannot be
  converted into a claimed external vendor transfer.

### Successful canonical collection and fulfillment proof

- A fresh order was created through the authenticated customer frontend and collected through the
  authenticated Super Admin Development panel:
  - marketplace order `ORD-CH-0024`, id `4ef2b262-556f-4ed3-9262-7f7e5fe8b527`;
  - vendor suborder `ORD-CH-0024-A`;
  - payment `f008bd46-0425-4ffc-af1a-246bedb1fa6d`, EGP `2,200`;
  - idempotency key `gate3r-f008bd46-success-v1`;
  - transaction evidence
    `DEV-SIM:ONLINE:f008bd4604254ffcaf1a246bedb1fa6d:33666dffbfc0be3de578f834`;
  - immutable financial snapshot: EGP `220` commission and EGP `1,980` vendor earning.
- Super Admin payment details independently reported the payment `Paid`, paid at
  `2026-09-09T12:38:28Z`, and Event Processing `Processed`. Customer and vendor projections reported
  the order payment Collected. The `DEV-SIM:ONLINE:` identity explicitly proves this was not a
  genuine Paymob collection and moved no external money.
- The vendor frontend advanced the authorized suborder transitions Confirmed -> Processing -> Shipped
  -> Delivered. Payment remained Collected and no further fulfillment action was exposed.
- Delivery entered the seven-day settlement/refund hold. Vendor read authority reported EGP `0`
  eligible, EGP `1,980` current payable, EGP `1,980` holdback, EGP `0` paid out, and EGP `0` pending
  payout operations. The exact statement retained one EGP `1,980` credit for
  `ORD-CH-0024-A`, reference `e7f16817-e361-436e-a7b9-c9a91c633ab1`, with opening balance EGP `0`
  and closing balance EGP `1,980`.
- Both `PayoutDelayDays` and `RefundWindowDays` are `7`. Super Admin payout readiness also retained
  the active-refund blocker and zero eligible batch. No payout was created or completed.

### Before-payout refund and return proof

- Before delivery, customer authority correctly reported maximum refundable quantity `0`. After
  delivery it reported `1`, and the customer submitted exactly one EGP `2,200` request through the
  authenticated frontend: refund `d9ac7cf0-3822-437c-9e66-e8c258c8b633`, reason Other, with one PNG
  evidence item and a masked Vodafone Cash payout snapshot.
- The request POST committed, but its post-success order refresh exposed a Development storage defect:
  `RefundEvidenceService` requested a presigned download URL while
  `DevelopmentFileStorageService` only supported local private object bytes. The customer GET failed
  with trace `5dd286a9-48a6-4f99-ae79-dcfd3580ba49`. Development storage now issues opaque expiring
  download leases and redeems them through a Development-only byte-stream endpoint. It does not expose
  filesystem paths. After restart, the customer order and evidence view loaded and proved the
  original request existed, so it was not resubmitted.
- Super Admin preview classified the request as `OnlineBeforePayout`: projected customer payment
  EGP `2,200`, vendor payable reversal EGP `1,980`, commission reversal EGP `220`, platform cash delta
  `-2,200`, platform payable delta `-1,980`, commission receivable delta `-220`, and vendor debt delta
  `0`. Refunded amount remained EGP `0`; EGP `2,200` was only the projected post-execution value.
- Super Admin approved the request once and created the vendor-owned return workflow. The vendor
  frontend then performed only currently authorized transitions with tracking
  `GATE3R-RETURN-D9AC7CF0`: Pending -> PickupScheduled -> PickedUp -> InTransit -> DeliveredToVendor
  -> Confirmed. The `OnPickup` trigger changed the refund from WaitingForReturn to AwaitingPayout.
- Vendor receipt was actually recorded as resellable quantity `0` and damaged quantity `1`. This is a
  valid receipt totaling the approved quantity and is recorded here as submitted; it was not rewritten
  or replayed.
- Final read authority agreed across actors: customer and vendor showed AwaitingPayout, vendor return
  Confirmed, execution NotStarted, Super Admin payout attempts `0`, paid timestamp unavailable, and
  the only remaining action `Send money and confirm`. Vendor refund impact remained EGP `0`, the
  statement retained only its original EGP `1,980` credit, and payout payable/holdback remained
  EGP `1,980` with zero pending payout operations. These observations prove workflow readiness and
  absence of financial reversal, not refund execution.
- The first vendor Finance readback incorrectly classified the online order's EGP `220` platform
  commission as outstanding COD debt. `CommerceHubVendorDebtService` used platform commission rows
  as a legacy fallback whenever a vendor ledger row shared the order/refund reference, but did not
  validate the persisted payment-method snapshot. The fallback now resolves order references through
  `CommerceHubSubOrders` and refund references through `CommerceHubRefunds` plus their suborders, and
  admits only `CommerceHubPaymentMethod.CashOnDelivery`. Direct vendor commission debt and completed
  post-payout refund receivables remain authoritative and unchanged.
- After rebuilding and restarting, authenticated vendor Finance read authority reported current
  balance EGP `1,980`, total earnings EGP `1,980`, total commissions EGP `220`, refund impact EGP `0`,
  and current debt/debt outstanding EGP `0`. The statement still contained exactly one EGP `1,980`
  order credit. This proves the correction changed debt classification only; it did not rewrite the
  immutable commission, earning, refund, payout, or statement ledger evidence.

### External-money boundary

- Canonical payout creation revalidates selected earnings and their delayed availability. Super Admin
  payout orchestration requires the canonical external handoff followed by immutable transfer, bank,
  or provider evidence and sets `ExternalTransferConfirmedExecuted=true` only when recognizing an
  already-executed transfer. No Development payout simulator exists.
- Refund decisions remain economically distinct. `OnlineBeforePayout` reduces platform cash and
  vendor payable; `OnlineAfterPayout` reduces platform cash and creates vendor debt because the vendor
  cash has already left the platform. The available Development refund seam is specifically the COD
  debt-reduction scenario and does not simulate an online post-payout external refund.
- Paymob CommerceHub refunds remain disabled as `not_supported` pending provider certification. The
  operator therefore did not click `Send money and confirm`, enter a transaction reference, claim a
  customer transfer, or invoke the COD-only Development refund seam for this online order.
- Each accepted CommerceHub financial transaction is deduplicated before insertion and creates an
  `AccountingOutboxMessage` in the same transaction with source type
  `CommerceHubFinancialTransaction` and a transaction-specific idempotency key.

### Verification evidence

- Initial focused payment/finance/Gate 3R slice: `60 passed`, `0 failed`, `0 skipped`.
- Expanded lifecycle slice after the payment-boundary and compensation corrections:
  `148 passed`, `0 failed`, `0 skipped`. The final focused worker/payment slice after eligibility
  hardening passed `26/26`.
- Final combined payment, compensation, finance, payout, customer refund, Super Admin refund,
  financial calculator, refund execution, return, evidence, Development storage, and Gate 3R slice:
  `105 passed`, `0 failed`, `0 skipped`.
- Focused vendor-debt and finance regression slice after COD fallback classification hardening:
  `84 passed`, `0 failed`, `0 skipped`. Dedicated regressions prove that online commission remains
  platform revenue without vendor debt, legacy COD commission still creates debt, and online
  post-payout refunds create only their vendor receivable amount rather than adding online commission.
- Development storage and refund evidence tests specifically passed `12/12`, including opaque
  download round-trip and invalid/expired token rejection.
- POS Web Development simulation component suite: `4 passed`, `0 failed` using one Vitest thread.
- The Debug API build completed with zero errors. Existing unrelated analyzer warnings appeared while
  the test project compiled.

### Runtime provenance and health

- Port `5010` is served by PID `5936`,
  `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`, started at
  `2026-09-09T16:58:07+03:00`. Its loaded Debug DLL timestamp is
  `2026-09-09T13:51:09Z` and SHA-256 is
  `B7399C0597B463F21A6D452E47E6F6DB97ACC10F085F5D7CFDF234C69A9368E6`.
- An unauthenticated POST to the exact Development simulation route returned HTTP `401` with
  `WWW-Authenticate: Bearer`, proving route presence and authentication fencing before the human
  Super Admin action.
- `/health/live` returned HTTP `200` Healthy and `/health/marketplace` returned HTTP `200` Healthy
  with zero marketplace drift and healthy platform dependencies after the final runtime sequence.
  `/health/ready` returned HTTP `200` Degraded only because certificate-provider and Caddy execution
  are disabled. Accounting outbox, payout external truth, and external outcome recovery were Healthy;
  unresolved refund, captured-webhook, and refund-webhook counts were all zero.

### Certification boundary

The runtime sequence used authenticated customer, vendor, and Super Admin frontends, one authorized
human action at a time. It performed no direct SQL mutation, forged callback/HMAC, browser automation,
direct service invocation, fabricated payout reference, Storefront payment authority, external payout,
or external refund confirmation.

The resulting classification is:

- **PASS, CANONICAL DEVELOPMENT RUNTIME:** Development-only collection admission, payment finality,
  fulfillment, settlement hold, vendor earning/ledger visibility, customer refund admission, Super
  Admin review, vendor return logistics, and refund execution readiness.
- **PASS, FAIL-CLOSED SAFETY:** invalid simulation state, missing reservation, simulated compensation,
  simulated earning payout, private Development evidence download, and external-transfer boundaries.
- **CAPABILITY NOT AVAILABLE:** External payout and post-payout runtime refund certification for a
  simulated collection. Payout recognition requires genuine already-executed transfer evidence, and
  simulated earnings fail closed before payout creation.
- **EXTERNAL PROVIDER LIMITATION:** Genuine successful Paymob collection remains unverified. The
  successful collection is explicitly Development-simulated, and Paymob CommerceHub refund execution
  remains disabled pending provider certification.
- **RESIDUAL DATA/OPERATIONS WARNING:** The last payout-authority read reported
  `LedgerPayableDrift` and a missing default payout method. The incorrect EGP `220` debt classification
  is resolved: authenticated vendor Finance now reports EGP `0` debt while preserving EGP `220`
  commission revenue and the EGP `1,980` statement balance. Any remaining payout drift requires
  separate reconciliation and is not treated as a completed Gate 3R payout or refund effect.

Gate 3R is therefore certified through the maximum honest non-external runtime boundary. It is not a
certification of genuine Paymob collection, customer refund money movement, external vendor payout,
or any post-payout refund path.

## ORD-CH-0024 before-payout refund execution addendum

Date: 2026-09-09

Status: **ACCOUNTING RECOVERED; NOT CERTIFIED AS A DEVELOPMENT NO-EXTERNAL-MONEY SUCCESS PATH.** The
completed refund has correct `OnlineBeforePayout` operational and GL effects, and the previously
blocked vendor-refund mirror is now processed without a duplicate journal. Its immutable
execution/audit evidence still claims an ordinary manual external transfer rather than a Development
simulation, and provider refund confirmation remains absent. This addendum supersedes the earlier
pending-accounting disposition, but does not rewrite or upgrade the external-money evidence.

### Read-only persisted evidence

- A repeatable-read PostgreSQL transaction was explicitly set `READ ONLY`; it performed no inserts,
  updates, deletes, callbacks, job triggers, or service mutations.
- Refund request `d9ac7cf0-3822-437c-9e66-e8c258c8b633` completed once for quantity `1` and EGP
  `2,200`. Financial refund `ea49ca3e-3549-4426-8d0b-3dca4bd18e23` is full and completed, with EGP
  `1,980` vendor earning reversal, EGP `220` commission reversal, EGP `0` promo reversal, and
  `IsPostPayoutAdjustment=false`.
- Earning `4688f93b-ce92-4e07-96a3-ed4acb994c45` is reversed for EGP `1,980`, has no payout id,
  EGP `0` paid out, and EGP `0` reserved. No payout exists for the vendor.
- The seven operational transactions preserve the expected before-payout economics: original EGP
  `2,200` platform payment, EGP `1,980` vendor proceeds, and EGP `220` commission; refund EGP `2,200`
  platform cash debit, EGP `1,980` platform payable reduction, EGP `1,980` vendor statement debit,
  and EGP `220` commission reversal. This explains vendor balance EGP `0`, historical earnings EGP
  `1,980`, net commissions EGP `0`, refund impact EGP `1,980`, and debt EGP `0`.
- The successful execution row is classified as `ManualExternalReference`, not
  `DevelopmentSimulation`. Both `REFUND_PAYOUT_INITIATED` and `REFUND_PAYOUT_CONFIRMED` audits persist
  `ExecutionPath=OnlineBeforePayout`, `IsDevelopmentSimulation=false`, and
  `ExternalMoneyMovementConfirmed=true`. `RefundExecutionService` derives that assertion from
  `ConfirmMoneySent && !IsDevelopmentSimulation`; ordinary manual completion stores gateway status
  `manual-confirmed`.
- These records do not prove that a provider or wallet actually moved money. They prove that the
  application was told external money had been sent. Because the operator reports no such movement,
  the persisted claim and the physical-world observation conflict. Certification cannot choose the
  observation over immutable execution evidence or relabel the row as simulated.

### Reconciliation and accounting disposition

- Normal hourly reconciliation report `95c3bb0a-bbba-4635-875b-05421ee8cc95`, timestamp
  `2026-09-09T18:00:15.224856Z`, is `Critical` with exactly one finding. It checked `23` suborders,
  `5` completed refunds, and all `3` vendors. Daily aggregate validation covered `412` stored rows
  with zero invalid and zero missing rows.
- `EXTERNAL_REFUND_CONFIRMATION_MISSING` is valid: the payment provider is external, while the local
  refund is completed without a provider-confirmed refund reference/status. `manual-confirmed` is
  manual transfer evidence, not Paymob refund evidence. No genuine Paymob refund success is claimed.
- `EXTERNAL_PAYMENT_ORDER_STATE_MISMATCH` is absent from the current report, confirming the corrected
  predicate in the restarted runtime. Read-only evidence confirms the payment remains historically
  `Paid`, while marketplace order `ORD-CH-0024` and its suborder are both `Status=Refunded` and
  `PaymentStatus=Refunded`. The corrected predicate accepts `Collected`, `Settled`, `Refunded`, and
  `PartiallyRefunded` as financially recognized.
- The operator used the authenticated, capability-protected Development UI once to requeue only
  accounting outbox `01a08680-3cff-7ef1-a6ff-58fa723f30f1`. The normal worker processed it at
  `2026-09-09T17:13:29.257573Z`. Its final state is attempt count `8`, `IsFailed=false`, no claim,
  no error, no next retry, and `JournalId=00000000-0000-0000-0000-000000000000`.
- The message references vendor refund payable transaction
  `fa6453fa-86ff-463e-bc14-15c751188801`, EGP `1,980`. It is covered by canonical platform payable
  adjustment `a48d90ea-7214-4d75-b672-b4e887544f94` for the same refund, amount, currency, and
  payable bucket. The mirror created no journal and no `CommerceAccountingAudit` row.
- Every one of the four refund financial fingerprints occurs exactly once. The three posting rows
  retain one journal each, all individually balanced: commission reversal `JE-2026-000004` debits
  `4020` and credits clearing `2030` for EGP `220`; payable reduction `JE-2026-000005` debits `2040`
  and credits `2030` for EGP `1,980`; customer cash refund `JE-2026-000006` debits `2030` and credits
  bank `1030` for EGP `2,200`. The clearing account nets to zero, producing the canonical net result
  DR `2040` EGP `1,980`, DR `4020` EGP `220`, and CR `1030` EGP `2,200`.
- Account `2040` has EGP `1,980` lifetime credits, EGP `1,980` lifetime debits, and current balance
  EGP `0`. The vendor operational payable likewise has EGP `1,980` credits, EGP `1,980` debits, and
  current balance EGP `0`. Earning `4688f93b-ce92-4e07-96a3-ed4acb994c45` remains `Reversed`, with
  no payout, no paid amount, and no payout reservation.
- The earlier `journalCount` change from `59` to `60` was not a journal count: the Development
  test-auth endpoint returned `processedCount` under that name. The endpoint now queries actual
  `LedgerJournalEntries` with query filters ignored. It reports `62` total journals, and the newest
  actual journal remains `JE-2026-000006` at `14:09:25Z`, more than three hours before mirror recovery.

### Source corrections

- `CommerceHubFinanceReconciliationReadService` now applies legacy platform-commission receivable
  fallback only when an order/refund reference resolves to a persisted COD suborder. Linked online
  commission remains platform revenue and no longer becomes hidden vendor debt. Direct vendor debt
  rows and post-payout refund receivables remain authoritative.
- The external-payment reconciliation check now recognizes refunded and partially refunded order
  payment states as historically collected rather than reporting a false lifecycle mismatch.
- Vendor Finance `Refund Impact` copy now describes vendor proceeds reversed by completed refunds,
  in English and Arabic, instead of incorrectly limiting the metric to after-payout adjustments.
- `AccountingOutboxProcessor` now recognizes only the exact covered vendor refund payable row as a
  non-posting operational mirror. It fails closed when the canonical platform payable adjustment is
  absent, and successful empty-journal outcomes create neither a journal nor a commerce-accounting
  audit row.
- `AccountingOutboxMessage.MarkProcessed` clears retry and permanent-failure state. Financial
  readiness delegates covered mirrors to the canonical adjustment's posted journal while rejecting
  orphan or uncovered mirrors.
- The bounded Development requeue UI validates the exact response and now surfaces the same sanitized
  `WasRequeued`, message-id, and timestamp text in drawer state, persistent page feedback, and an
  immediate alert so background list revalidation cannot temporarily hide the result.

### Verification and runtime boundary

- Focused Release accounting-processor, finance-readiness, reconciliation, and refund-execution suite:
  `88 passed`, `0 failed`, `0 skipped`.
- POS Web refund-authority suite: `17 passed`, `0 failed`. POS Web and CommerceHub production builds
  passed; their existing oversized-chunk warnings and CommerceHub's stale Browserslist warning remain
  non-blocking build debt.
- The complete POS Web i18n certification file passed `2/4`. Its two failures remain unrelated missing
  product-pricing and services-marketplace keys in concurrent worktree areas; the focused bilingual
  Refund Impact assertion remains passing and those unrelated files were not changed for this recovery.
- API Release and Debug builds passed with existing unrelated analyzer warnings and no errors.
- Scoped `git diff --check` reported no whitespace errors; Git emitted only line-ending conversion
  warnings.
- Immediate post-click verification used a PostgreSQL repeatable-read transaction explicitly set
  `READ ONLY`; it performed no insert, update, delete, callback, job trigger, worker call, or service
  mutation.
- Restarted Debug API PID `13828` began at `2026-09-09T17:49:56Z`. Its loaded DLL was written at
  `2026-09-09T17:49:55Z` and has SHA-256
  `C33D367D906ECB4C285B835C3E8A33375AA14BECFC3C234AA79CEAC935D68C59`; it includes the mirror and
  diagnostic corrections. `/health/live` and `/health/marketplace` return HTTP `200 Healthy`.
  Accounting outbox readiness is `Healthy` with zero pending, repeated-failure, critical-lag, and
  warning-lag counts. `/health/ready` remains HTTP `503 Unhealthy` solely within this scope because
  payout external-truth readiness reads the current `Critical` report and its one external-refund
  mismatch; completed-payout and payout-reconciliation blocker counts are all zero.

### Final classification

- **PASS, OPERATIONAL BEFORE-PAYOUT ACCOUNTING:** no payout or vendor debt was created; payable and
  commission operational balances reversed by EGP `1,980` and EGP `220` respectively.
- **PASS, GL COMPLETENESS AND RECOVERY:** the canonical refund journals are balanced, the operational
  mirror is processed without a duplicate journal/audit, account `2040` is zero, and accounting
  readiness is Healthy.
- **FAIL, DEVELOPMENT SIMULATION EVIDENCE:** persisted execution explicitly says non-simulation and
  external money movement confirmed.
- **FAIL, PROVIDER REFUND FINALITY:** no confirmed Paymob refund reference exists.
- **FAIL-CLOSED, OVERALL READINESS:** payout external-truth readiness remains unhealthy while the
  immutable external-refund mismatch is unresolved; settlement, payout, and Gate 4 remain blocked.
- **NO GATE 4 / NO DATA REWRITE:** no payout, replacement order, provider callback, direct SQL
  mutation, compensating journal, or fabricated external reference was created.

`ORD-CH-0024` therefore proves the operational and Accounting `OnlineBeforePayout` reversal
semantics, including supported outbox recovery, but it does not qualify as the requested Development
success-path/no-external-money refund certification.

## Gate 3R post-payout online money lifecycle pre-remediation finding (2026-09-09)

Status: **HISTORICAL FINDING; SUPERSEDED BY THE 2026-09-10 FINAL DISPOSITION BELOW.** At the time of
this inspection, source economics and recovery passed, the production payout handoff failed ADR-001,
and the fresh runtime lifecycle was incomplete. This continuation inspected the canonical collection,
settlement, payout, online-after-payout refund, and vendor-recovery authorities. It did not create a
fresh order or perform a financial mutation.

### Authority and capability boundary

- Delivery makes an earning `Available`; it does not make it immediately payout-eligible. Eligibility
  separately enforces `AvailableAtUtc`, `PayoutDelayDays`, and expiry of the customer refund window.
  Current `PayoutDelayDays` and `RefundWindowDays` are both `7`.
- Canonical payout creation rejects earnings collected through Development online simulation with
  `PAYOUT_DEVELOPMENT_SIMULATION_NOT_TRANSFERABLE`. No Development payout simulator exists.
- Canonical payout completion requires committed handoff plus genuine immutable external transfer,
  provider, or bank evidence. `ExternalMoneyMovementConfirmed` cannot be asserted merely to advance
  certification.
- The active Super Admin production UI nevertheless directs the operator to execute the external
  transfer first and then use `Record completed payout`. That single request calls `CreatePayoutAsync`,
  `MarkProcessingAsync`, and `MarkCompletedAsync` in immediate sequence. The persisted `Pending` and
  `Processing` states therefore begin after the physical transfer instead of forming the durable
  pre-transfer handoff required by ADR-001.
- The vendor-scoped processing, completion, and failure HTTP mutations are deliberately disabled, and
  no provider or bank payout executor is bound to the handoff token/idempotency key. There is no
  production HTTP or worker path that commits `Processing`, allows the transfer to occur, and later
  reconciles success or proven non-execution.
- Development refund authority exposes only canonical `OnlineBeforePayout` and COD debt-reduction
  simulation paths. It deliberately rejects the online simulation if the financial decision is
  `OnlineAfterPayout`; no Development online-after-payout refund simulator exists.
- Advancing timestamps, bypassing the holdback/readiness gates, directly invoking controllers or
  services, fabricating a payout/refund reference, or rewriting SQL would invalidate the evidence.
  None of those actions was taken. Existing orders `ORD-CH-0024` and `ORD-CH-0022` were not mutated.

### Lifecycle evidence matrix

| Lifecycle stage | Source/regression evidence | Fresh runtime evidence | Certification |
| --- | --- | --- | --- |
| Online collection | Canonical provider and Development-simulation collection paths remain covered | Not executed in this continuation | Unverified for a fresh order |
| Delivery and settlement availability | Delivery creates an available earning; payout read authority applies both seven-day constraints | Not executed in this continuation | Source/test pass only |
| Payout creation and completion | Service primitives enforce states and evidence, but the active Super Admin path persists the handoff only after the operator has transferred funds | Capability not available without external money or a forbidden bypass | Production architecture incomplete; not certified |
| Online refund after payout | Decision engine preserves completed payout history and creates vendor debt for paid vendor proceeds | No safe Development execution path | Not certified |
| Vendor recovery | Debt-control services support later reduction through authorized recovery/payment activity | No fresh debt existed to recover | Not certified |
| Replay/idempotency | Focused tests cover single execution/event publication and Development refund replay | No runtime payout/refund replay occurred | Source/test pass only |

No fresh payment id, earning id, payout id, refund id, event id, accounting journal, or recovery row
exists for this continuation. The absence of those identifiers is intentional and must not be filled
with evidence from another order or with synthetic values.

### Pre-remediation architecture-versus-Development decision

The required A/B/C decision is **A: production architecture is incomplete**.

- **A selected:** ADR-001 requires `Pending -> Processing -> committed external transfer handoff`
  before external money movement can reach terminal recognition. The active production UI explicitly
  says the transfer has already executed before its only action, and
  `SuperAdminPayoutOrchestrationService.ExecuteAsync` creates, processes, and completes the payout
  only after that action. Persisting the required state names after the transfer does not create a
  recoverable pre-transfer boundary.
- **B rejected:** Development does lack payout and `OnlineAfterPayout` refund simulation seams, but
  adding those seams would only test service primitives and would not correct the production ordering
  defect.
- **C rejected:** no real provider or bank sandbox payout adapter consumes the POS handoff identity.
  External reference and bank transaction fields are operator-supplied evidence used to recognize an
  already completed transfer, not an executable sandbox integration.

Production completion requires either a two-step authorized operator workflow or an asynchronous
provider/bank executor. The first committed action must reserve the earnings and persist the payout,
transfer intent, handoff token, and `Processing` state before money is sent. A separate replay-safe
action must then record provider/bank success, proven non-execution, or outcome-unknown reconciliation.
Only after that production boundary exists would a safe Development simulator be the remaining
certification gap.

### Expected canonical money and accounting effects

The following table records the behavior represented by the source model and regressions. It is not a
claim that these movements occurred during this continuation.

| Stage | Canonical economic effect |
| --- | --- |
| Online collection | Platform cash/collection increases by gross customer payment; vendor payable/earning and platform commission are recognized from the immutable order snapshot. |
| Delivery/settlement | Earning availability changes; no second customer collection or external vendor transfer occurs. Holdback remains until payout and refund-window eligibility pass. |
| Completed payout | Vendor proceeds leave platform cash only after external evidence; vendor payable is consumed once and completed payout history remains immutable. |
| Full online refund after payout | Platform refunds the full customer amount, reverses commission, preserves the completed payout, and creates vendor receivable/debt equal to vendor proceeds already paid. |
| Vendor recovery | Authorized recovery reduces that vendor receivable/debt through later cash recovery or canonical netting; it must not relabel the original refund as a debt reduction. |

### Event-contract correction

Inspection found one source-level evidence defect: a positive `OnlineAfterPayout` vendor-debt delta was
published as `VendorDebtReductionAmount = Abs(VendorDebtDelta)`. The SQL financial decision and ledger
effect already represented a debt increase correctly, and the order projection consumer reloads
canonical SQL, so no persisted finance was rewritten.

`CommerceHubRefundFinanciallyExecutedEvent` now carries the signed `VendorDebtDelta` and separate
non-negative `VendorDebtIncreaseAmount` and `VendorDebtReductionAmount` fields. Publication maps a
positive delta only to increase and a negative delta only to reduction. The added post-payout
regression proves `OnlineAfterPayout` publishes vendor proceeds as debt increase with zero reduction;
the COD regression continues to prove negative commission debt delta as reduction with zero increase.

### Payout accounting mirror correction

Inspection found a separate production accounting defect in the completed-payout ledger contract.
`RecordPayoutAsync` intentionally emits three operational rows for one amount: a platform payable
debit, a platform settlement/cash debit, and a vendor payable debit. The platform and vendor payable
rows both mapped independently to GL account `2040`, so normal outbox processing would debit the
vendor liability twice even though the vendor row is only the tenant statement mirror of the
canonical platform liability movement.

The vendor payout row remains in the immutable operational ledger because payout balances and vendor
statements consume it. `AccountingOutboxProcessor` now treats only the exact vendor
`Payout/Debit/Payable` row as `OPERATIONAL-MIRROR`, and only when a platform
`Payout/Debit/Payable` row exists with the same payout reference, amount, and currency. An orphan or
mismatched mirror stays retryable and does not call the GL posting service. Financial readiness now
delegates that mirror's coverage only to a posted journal for the matching canonical platform payout
row; an absent or unposted canonical row continues to block outward money movement.

This correction yields one liability journal and one cash journal for a completed payout while
retaining all three operational rows. No historical financial data was rewritten, and the current
runtime contains no completed payout requiring repair.

### Verification and restarted runtime

- Broader focused Release refund, payout, ledger, accounting, and recovery suite after the event
  correction: `98 passed`, `0 failed`, `0 skipped`.
- Final narrower rerun of `RefundExecutionServiceTests`, `CommerceHubFinanceServiceTests`, and
  `CommerceHubFinancialLedgerServiceTests`: `74 passed`, `0 failed`, `0 skipped`.
- Expanded Release payout-accounting and finance-readiness slice after the payout mirror correction:
  `102 passed`, `0 failed`, `0 skipped`. Added regressions prove covered mirrors produce no duplicate
  journal/audit, orphan mirrors remain retryable, posted canonical payout coverage passes readiness,
  and missing or unposted canonical coverage fails closed.
- Final Release accounting-processor, finance-readiness, Super Admin orchestration, and payout
  architecture slice after recording decision A: `74 passed`, `0 failed`, `0 skipped`. The exact five
  new payout-mirror cases then passed independently with `5 passed`, `0 failed`, `0 skipped`.
- Final API Release build: `0 warnings`, `0 errors`. Final Debug build: `0 warnings`, `0 errors`.
- Relevant `git diff --check` reported no whitespace errors; Git emitted only line-ending conversion
  warnings.
- Current rebuilt Debug API PID `9016` began at `2026-09-09T20:01:34Z` and serves port `5010` from
  `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`. Its DLL timestamp is
  `2026-09-09T18:39:53Z` and SHA-256 is
  `D5EFA1DCEE189EC2AE0476EABAD04EA7567C002E4994BC9A20ED51889DBC189C`.
- Post-restart `/health/live` and `/health/marketplace` returned HTTP `200`; `/health/ready` and
  `/health` returned HTTP `503`.
- At `2026-09-09T20:04:13Z`, accounting outbox readiness was Healthy with zero critical pending rows,
  and external-outcome recovery was Healthy. Payout external-truth readiness was Unhealthy because
  the latest hourly reconciliation was `Critical` with one mismatch. Completed payouts without
  evidence, completed payouts, stale processing payouts, and reconciliation-required payouts were all
  zero.
- Aggregate health separately retained degraded projection drift (`4`), one stale reservation holding
  `1.00` unit, and unhealthy CommerceHub search sync (`0.4`). These are environment residuals, not
  evidence that the blocked post-payout lifecycle ran.

### Pre-remediation classification

- **PASS, SOURCE ECONOMIC MODEL:** online-after-payout refund reduces platform cash, reverses
  commission, creates vendor debt equal to paid vendor proceeds, and preserves payout history.
- **PASS, EVENT SEMANTICS:** debt increase and debt reduction are now separately and correctly
  represented, with focused regression coverage.
- **PASS, FAIL-CLOSED AUTHORITY:** simulated online earnings cannot be paid out, payout completion
  cannot omit external evidence, and the Development online-refund seam cannot cross into the
  post-payout scenario.
- **PASS, PAYOUT GL CORRECTION:** one completed payout retains three operational rows but posts the
  payable movement once; vendor payout mirrors fail closed unless the matching canonical platform
  payout liability exists and is posted.
- **FAIL, PRODUCTION PAYOUT HANDOFF:** the active operator path recognizes an already executed transfer
  and only then persists `Pending`, `Processing`, and the handoff before immediate completion. This is
  not the durable pre-transfer boundary mandated by ADR-001.
- **DECISION A, PRODUCTION ARCHITECTURE INCOMPLETE:** the missing production execution/reconciliation
  boundary is the primary blocker. This A/B/C label is separate from ADR-001's name
  `Architecture B` for the desired lifecycle.
- **CAPABILITY NOT AVAILABLE:** safe Development payout simulation and Development online-after-payout
  refund execution.
- **CURRENT NON-BLOCKING CONDITION:** a fresh earning must naturally pass both seven-day eligibility
  constraints before payout; bypassing them is forbidden.
- **HISTORICAL RESIDUAL:** the retained `ORD-CH-0024` external-refund confirmation mismatch keeps
  payout external-truth readiness unhealthy.
- **EXTERNAL PROVIDER LIMITATION:** genuine successful Paymob collection and refund remain unverified.

This historical **A: production architecture incomplete** classification is superseded by the
2026-09-10 remediation and final disposition below. Its external-provider and fresh-runtime evidence
limitations remain valid.

## Gate 3R final disposition (2026-09-10)

Status: **GATE 3R - CONDITIONALLY CERTIFIED.** The sole confirmed internal production defect, the
post-transfer one-shot Super Admin payout handoff, is remediated. No remaining internal financial
defect was confirmed in the inspected collection, settlement, payout, post-payout refund, vendor
recovery, accounting, replay, or tenant-isolation boundaries. Certification remains conditional
because no genuine Paymob success, bank payout, customer refund transfer, or fresh end-to-end
post-payout runtime sequence was executed.

### Production payout handoff remediation

- The former `POST /api/super-admin/payouts/{vendorId}/execute` endpoint is removed. It no longer
  creates `Pending`, marks `Processing`, and marks `Completed` after an operator reports that money
  was already sent.
- `POST /api/super-admin/payouts/{vendorId}/prepare` now creates or resumes the canonical payout and
  commits the earning reservation, immutable payout destination, external transfer idempotency key,
  handoff token, handoff timestamp, and `Processing` state before the UI authorizes external transfer.
- `POST /api/super-admin/payouts/{vendorId}/{payoutId}/complete` is a separate reconciliation action.
  It requires explicit executed-transfer confirmation, the committed handoff token, and immutable
  bank or provider evidence before canonical completion, ledger recording, and state verification.
- A retry with the same tenant-scoped idempotency key resumes the existing `Pending` payout and does
  not create another payout or reserve another set of earnings. Payload amount and payout-method
  mismatches fail closed.
- The queue exposes only tenant-bound active `Pending`, `Processing`, or `ReconciliationRequired`
  payouts. Backend `CanPreparePayout` and `CanCompletePayout` remain action authority; the frontend
  validates but does not infer permission from displayed balances.
- The UI now instructs the operator to commit the payout handoff before sending money. Only a later
  action with executed-transfer confirmation and immutable evidence can record completion.

### Final evidence matrix

| Boundary | Evidence | Classification |
| --- | --- | --- |
| Collection ownership and provider finality | SQL payment authority, signed provider evidence, idempotent payment transitions, and prior focused regressions remain intact | **PASS** |
| Delivery, settlement, and payout eligibility | Availability, payout delay, refund window, risk flags, and EGP-only authority remain backend-owned | **PASS** |
| Durable pre-transfer payout handoff | Separate prepare endpoint commits canonical `Processing`, transfer intent, handoff token, destination snapshot, and earning reservations before transfer | **PASS - REMEDIATED** |
| Payout completion | Separate completion endpoint requires the matching handoff token, explicit execution confirmation, and immutable bank/provider evidence | **PASS** |
| Payout replay and interrupted preparation | Tenant-scoped idempotency lookup resumes an existing `Pending` payout; canonical creation and processing remain idempotent and conflict-checked | **PASS** |
| Queue recovery and action authority | Pending and committed payouts survive refresh and are surfaced with mutually exclusive backend prepare/complete authority | **PASS** |
| Tenant isolation | Payout method, payout identity, earning selection, active queue state, processing, and completion are all constrained by `vendorId`/`TenantId` | **PASS** |
| Completed-payout accounting | Canonical platform payable and cash rows post once; the vendor payable row is a validated operational mirror and fails closed without posted canonical coverage | **PASS** |
| Online-after-payout refund economics | Completed payout history remains immutable; full customer refund reverses commission and creates vendor debt equal to vendor proceeds already paid | **PASS - SOURCE/REGRESSION** |
| Vendor recovery | Authorized payment or canonical netting reduces the later vendor receivable/debt without relabeling the original refund | **PASS - SOURCE/REGRESSION** |
| `ORD-CH-0024` internal accounting | Payment EGP `2,200`, commission EGP `220`, earning EGP `1,980`, payout EGP `0`, refund EGP `2,200`, earning reversal EGP `1,980`, commission reversal EGP `220`, vendor debt EGP `0`; no history was mutated | **PASS** |
| Fresh post-payout runtime lifecycle | No order, payment, payout, refund, journal, or recovery row was created during this final review | **EVIDENCE LIMITATION** |
| Development payout and online-after-payout refund simulation | No safe simulator exists, and simulated collection earnings remain non-transferable | **EVIDENCE LIMITATION / CAPABILITY NOT AVAILABLE** |
| Genuine Paymob success and refund finality | No provider-confirmed successful collection or refund was available | **EXTERNAL LIMITATION** |
| Genuine bank/provider vendor payout | No payout-provider adapter or real executed transfer was available | **EXTERNAL LIMITATION** |
| Unresolved internal production financial defects in inspected scope | None confirmed after payout handoff and payout GL mirror remediation | **PASS - NO OPEN DEFECT** |

### Final verification

- API Release build passed after the remediation. Existing unrelated compiler/analyzer warnings remain.
- POS Web production build passed. The existing oversized-bundle warning remains.
- Focused Release payout orchestration, queue, accounting, finance-readiness, and architecture slice:
  `101 passed`, `0 failed`, `0 skipped`.
- Focused POS Web payout financial-authority suite: `8 passed`, `0 failed` using a single threads
  worker. This replaced the earlier forks-worker startup timeout with an explicit supported worker
  configuration; the tests themselves are passing.
- Scoped `git diff --check` reported no whitespace errors; Git emitted line-ending conversion warnings
  only.
- No runtime scenario or financial mutation was performed. `ORD-CH-0024` and all existing payout,
  refund, accounting, and recovery history were left unchanged.

### Final classification

- **PASS:** internal collection, settlement, payout eligibility, pre-transfer handoff, completion
  evidence, replay, tenant isolation, post-payout economics, vendor recovery, and accounting contracts.
- **DEFECT:** none remaining in the inspected internal financial boundary.
- **EVIDENCE LIMITATION:** no fresh post-payout runtime sequence and no Development payout or
  online-after-payout refund simulation capability.
- **EXTERNAL LIMITATION:** genuine Paymob success/refund and genuine bank/provider payout remain
  unverified and unavailable in this environment.

Gate 3R is therefore **CONDITIONALLY CERTIFIED**. The condition is external and evidentiary, not an
open internal financial architecture defect. This disposition does not certify actual provider or bank
money movement and does not authorize Gate 4 by itself.

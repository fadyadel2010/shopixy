# MAIN backend/frontend integration final audit - 2026-09-23

## Verdict

**PASS for the bounded M4.5 backend/frontend integration remediation, with release conditions.** The audited Storefront host now bootstraps from trusted host authority, adopts the backend-issued canonical tenant UUID, preserves that tenant/session identity through catalog, product detail, cart mutation, and cart reload, and correctly handles an unauthenticated refresh attempt. Authenticated POS Admin login, session recovery, product access, and Storefront settings access also completed against the live MAIN runtime.

This is **not an unconditional production-release certificate**. Aggregate health and readiness remain HTTP 503, the live configuration advertises guest checkout while the cart UI forces ordinary guests to sign in, and nine pre-existing POS frontend contract tests remain red. Those conditions are recorded without being relabeled as M4.5 remediation failures.

## Evidence boundary

- Repository: `PointOfSale`.
- Branch and audited base: `main` at `24e1f3a` plus the current dirty worktree.
- Audit date: 2026-09-23.
- Runtime endpoints: API `5010`, POS Admin `5173`, Storefront `5174`.
- Browser tenant host: `shopixy.localhost:5174`.
- Canonical tenant ID: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.
- Storefront: `shopixy`, status `live`.
- The worktree contained extensive pre-existing modified, deleted, and untracked work. It was preserved; no reset, stash, clean, revert, or unrelated cleanup was performed.
- No load test, Kubernetes, k6, workload, VU, threshold, or infrastructure-sizing file was modified for this audit.
- Browser evidence used a disposable .NET Playwright runner outside the repository. No maintained browser-E2E suite exists in the repository.
- Supplied login credentials were used only for the runtime POS journey and are intentionally excluded from this report.

## Findings

### F-1 - Fixed: storefront bootstrap retained a non-canonical slug

Severity: critical integration blocker.

Initial browser evidence showed:

- `GET /api/storefront/store-config` returned HTTP 200 when no tenant header was sent.
- Every subsequent browser request sent `X-Tenant-Id: shopixy`.
- The backend trusted host resolved the canonical UUID and rejected the conflicting slug header with HTTP 403 `TENANT_FORBIDDEN`.
- Catalog, navigation, categories, footer, offers, and auth-provider calls therefore failed after a successful bootstrap.

Root cause:

- `resolveTenant()` correctly treated the subdomain as an initial candidate.
- The normal bootstrap path never replaced that candidate with `StoreConfig.tenantId`.
- Only the theme-preview path promoted backend-issued authority.

Remediation:

- StoreConfig and theme-preview bootstrap requests omit `X-Tenant-Id`, allowing trusted host authority to resolve the tenant.
- Every accepted StoreConfig response must include a UUID tenant ID.
- Bootstrap replaces the host candidate with that canonical ID and initializes the canonical tenant-scoped session before ordinary API traffic starts.
- Subsequent requests carry the UUID and fail closed if StoreConfig does not provide canonical tenant evidence.

Final browser evidence showed the canonical UUID on navigation, catalog discovery, categories, footer, offers, product detail, related products, cart mutation, and cart reload. Those application calls returned HTTP 200.

### F-2 - Fixed: cookie refresh was rejected by model validation

Severity: high auth/session contract defect.

The Storefront client uses HttpOnly refresh cookies and posts an empty JSON object to `/api/storefront/auth/refresh`. The controller documented an optional body and already preferred the cookie, but `RefreshTokenRequestDto.RefreshToken` was non-nullable. ASP.NET automatic validation returned HTTP 400 `VALIDATION_ERROR` before the controller could inspect the cookie or return its intended unauthenticated response.

The DTO property is now nullable. With no cookie, the live endpoint returns HTTP 401 `MISSING_TOKEN`; with a valid cookie, the request can reach the existing rotation path. A source certification test freezes the optional-body contract.

### F-3 - Open: guest-checkout configuration is not honored by the cart UI

Severity: high product/configuration integration defect; classified as pre-existing and outside the narrow remediation edits.

Live StoreConfig returns `behavior.enableGuestCheckout: true`. The Cart Summary does not read that setting. It permits checkout only for an authenticated customer or affiliate mode and otherwise redirects to `/login?return=%2Fcheckout`.

Runtime proof:

- A guest added a real product successfully.
- `GET /api/storefront/cart` returned HTTP 200 and the item persisted under the same canonical tenant/session identity.
- Clicking the enabled checkout control navigated to `/login?return=%2Fcheckout` instead of `/checkout`.

Required follow-up: decide whether `enableGuestCheckout` is authoritative. If it is, wire it into the cart admission UI and retain backend checkout admission as final authority. If guest checkout is intentionally unsupported, remove or redefine the published setting so Admin and Storefront cannot disagree.

### F-4 - Open: POS login labels are not programmatically associated

Severity: low accessibility and testability defect; pre-existing.

The login page visibly renders `Username or email` and `Password`, but its `<label>` elements do not use `htmlFor` and the inputs do not have corresponding IDs. Accessible label lookup failed even though the form was visible. Authentication succeeded when the disposable runner selected the inputs by their `autocomplete` attributes.

Required follow-up: associate each label and input with stable `id`/`htmlFor` pairs.

### F-5 - Open: broader test and operational baselines are not fully green

Severity: release condition, not a regression introduced by this integration remediation.

- The full POS frontend run passed 1427 of 1436 tests. Nine failures reflect existing contract drift around checkout-token storage, payment lifecycle copy, cart preservation, an incomplete checkout API mock, warehouse/sidebar persistence, and footer-feedback conflict ordering.
- The repository-wide backend run exceeded five minutes and exposed existing environment/baseline failures, including a missing frozen report marker, missing `SHOPIXY_ELASTICSEARCH_URL`, SQLite/PostgreSQL migration incompatibilities, and legacy fixture failures.
- Aggregate `/health` and `/health/ready` return HTTP 503.

## Remediation summary

### StoreConfig and publication authority

- `StorefrontStoreConfigController` returns bootstrap configuration for active tenants even when the Storefront is in maintenance or disabled state, allowing the frontend to render the authoritative status screen.
- The controller still rejects inactive, deleted, or non-active-status tenants.
- `StorefrontPublicationGuardMiddleware` defers exact and trailing-slash StoreConfig paths to the controller while retaining publication blocking for other public Storefront reads.

### Tenant and session authority

- StoreConfig bootstrap requests do not send an untrusted subdomain candidate as `X-Tenant-Id`.
- Normal and preview bootstrap paths both require and adopt backend-issued UUID tenant authority.
- Session storage is re-evaluated under the canonical UUID, preventing a slug-scoped guest identity from leaking into canonical requests.

### Auth refresh contract

- The refresh request body is truly optional for cookie-based sessions.
- Anonymous browser startup now produces expected HTTP 401 auth evidence rather than an invalid-payload HTTP 400.

### POS test fixtures

- Product, Storefront Settings, and Product Workspace tests now provide the permission/settings contexts introduced by the current UI authority model.
- Product Storefront links use Settings-provided Storefront URL authority and fail closed when it is unavailable.

## Contract audit

| Boundary | Result | Evidence |
| --- | --- | --- |
| Trusted host to tenant resolution | PASS | `shopixy.localhost` resolves StoreConfig without a candidate tenant header. |
| StoreConfig to canonical browser identity | PASS after fix | Browser adopts `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`. |
| Canonical identity to catalog reads | PASS | Discovery, categories, navigation, footer, and offers return 200. |
| Catalog list to product detail | PASS | Real product detail and related discovery return 200. |
| Product detail to guest cart mutation | PASS | `POST /api/storefront/cart/items` returns 200. |
| Cart mutation to cart reload | PASS | `GET /api/storefront/cart` returns 200 and the product remains visible. |
| Storefront unauthenticated session probe | PASS | `/auth/me` and missing-token `/auth/refresh` return expected 401 responses. |
| Guest cart to configured guest checkout | FAIL, pre-existing | UI redirects to login while StoreConfig enables guest checkout. |
| Authenticated checkout/order/payment browser completion | NOT CERTIFIED | No Storefront customer credentials or safe order mutation fixture were used. Source/focused tests passed. |
| POS login and session establishment | PASS | Login and session endpoints return 200. |
| POS permission-protected products route | PASS | `/products` remains authorized; categories and products return 200. |
| POS permission-protected Storefront settings route | PASS | `/storefront/status` remains authorized; Admin StoreConfig returns 200. |
| Aggregate operational readiness | FAIL | `/health` and `/health/ready` return 503. |

## Browser certification

### Storefront guest journey

The disposable Chromium journey performed:

1. Open `http://shopixy.localhost:5174`.
2. Bootstrap StoreConfig without `X-Tenant-Id`.
3. Verify ordinary requests use the canonical UUID and a canonical tenant-scoped session ID.
4. Load homepage discovery, navigation, categories, footer, offers, and auth providers.
5. Open product `b55ddc92-14b2-0160-78e2-a9ebfe867f1b`.
6. Load product detail and related products.
7. Add the product to the guest cart.
8. Open `/cart` and verify the item persists.
9. Select checkout and observe the login redirect described in F-3.

The final journey ran against the Release API process and produced no browser page errors, no failed API transport requests, and no non-canonical Storefront request. Expected anonymous auth probes returned 401. Application reads and the cart mutation returned 200.

### POS Admin journey

The disposable Chromium journey performed:

1. Open `http://localhost:5173` and observe expected anonymous session 401 responses.
2. Submit the supplied POS Admin credentials.
3. Verify login and session endpoints return 200.
4. Load the dashboard and its protected data calls.
5. Navigate to `/products`; verify the route is not permission-redirected and products/categories return 200.
6. Navigate to `/storefront/status`; verify the route is not permission-redirected and Admin StoreConfig returns 200.

The final authenticated journey ran against the Release API process and ended at `/storefront/status`, with no page errors or failed API transport requests.

## Verification

### Current-source tests and builds

- StoreConfig/publication backend focus: 4 passed, 0 failed.
- Tenant/domain/product/cache backend focus: 32 passed, 0 failed.
- Cart/auth/payment/POS backend focus: 104 passed, 0 failed.
- Cookie-refresh optional-body certification: 1 passed, 0 failed.
- Storefront authority certification after canonical promotion: 45 passed, 0 failed.
- Full Storefront suite: 36 files, 355 passed, 0 failed.
- Focused remediation-owned POS fixtures: 3 files, 140 passed, 0 failed.
- Full POS suite: 1427 passed, 9 failed from the pre-existing drift summarized in F-5.
- Release solution build: succeeded with 0 warnings and 0 errors.
- Storefront production build: succeeded.
- POS Admin production build: succeeded.
- CommerceHub production build: succeeded.
- Scoped `git diff --check`: no whitespace errors; only Git LF-to-CRLF working-copy notices.

One grouped `StorefrontCustomerAuthorityCertificationTests` run reported 4 passing tests and one unrelated pre-existing documentation-freeze failure. The newly added refresh-body test passes when selected directly.

### Runtime process

- Final behaviorally certified process: PID `9396`.
- Binary: `PointOfSale.API/bin/Release/net9.0/PointOfSale.API.exe`.
- Launch mode: `dotnet run --project PointOfSale.API -c Release --no-build`.
- API liveness: HTTP 200.
- Aggregate health: HTTP 503.
- Aggregate readiness: HTTP 503.
- Release source/build parity: Release solution build passed after all source edits.
- Final compact browser result: canonical Storefront identity true, non-canonical identity false, StoreConfig/discovery/detail/cart mutation/cart reload true, expected missing-token 401 true, POS login/session/products/Admin StoreConfig true.

## Operational health

The final health snapshots remained HTTP 503. Relevant entries included:

| Check | State | Evidence |
| --- | --- | --- |
| PostgreSQL | Healthy | Reachable on the configured baseline database. |
| MongoDB | Healthy | Read-model database reachable. |
| Elasticsearch | Healthy | Cluster green. |
| RabbitMQ / MassTransit | Healthy | Bus and endpoints ready. |
| Redis connectivity | Unhealthy | Reachable but slow, approximately 156-170 ms in the sampled checks. |
| Redis latency | Unhealthy | Sampled at 266 ms. |
| Projection drift | Degraded | 23-document drift, MongoDB ahead. |
| Payout external truth readiness | Unhealthy | Latest reconciliation status critical with one mismatch. |
| Stock reservation | Degraded | One stale reservation holding 1.00 unit. |
| CommerceHub search sync | Unhealthy | Sync ratio 0.4. |
| Certificate provider | Degraded | Provider execution disabled. |
| Caddy edge | Degraded | Edge integration disabled. |

These conditions remain independent release blockers. Successful browser journeys do not override aggregate readiness.

## Release conditions

1. Resolve or explicitly waive the guest-checkout configuration/UI contradiction in F-3.
2. Resolve aggregate readiness failures relevant to the target deployment, especially Redis latency, payout external truth, and CommerceHub search sync.
3. Reconcile the nine POS frontend contract tests or formally classify and update their obsolete expectations.
4. Run an authenticated Storefront checkout/order/payment journey in an isolated mutation-safe environment before claiming full purchase-flow certification.

## Final conclusion

The M4.5 remediation no longer breaks the backend/frontend Storefront identity boundary. Trusted host bootstrap, canonical tenant promotion, session scoping, catalog reads, product detail, guest cart mutation/reload, anonymous refresh semantics, POS authentication, and representative POS permission guards are integrated and working against the freshly built Release API in the live MAIN development runtime. The remaining failures are explicit: guest-checkout configuration drift, incomplete end-to-end purchase mutation evidence, non-green aggregate readiness, and broader pre-existing test debt.

# MAIN First-Login Payment/Shipping Sidebar Navigation — Final Certification

**Date:** 2026-10-07  
**Runtime:** MAIN Windows; POS Admin `http://localhost:5173/`; real Development API/database  
**Browser:** headed Microsoft Edge, registered session `opencode-ui-test`  
**Feature verdict: PASS**

A brand-new disposable tenant exposed its authorized **Payment Methods** and **Shipping Methods** links during the first authenticated session, explicit login, and immediately after successful storefront activation. Neither page had been opened. The links were actually rendered, scrollable, unobscured, and reachable as pointer targets. Authorization was preserved. This is a certification of this navigation fix, not an assertion that the entire repository test suite is green.

## 1. Exact root cause

The defect was **navigation expansion initialization and persistence**, not missing Owner permissions or capability discovery.

Before the fix:

1. `Sidebar.jsx::readExpandedMenus` returned `{}` when no expansion cache existed.
2. `expandedMenus[groupKey]` was consequently `undefined` for the fresh Storefront, Operations, Payments, and Shipping branches.
3. The submenu renderer interpreted that presentation state as collapsed: `max-height: 0px; opacity: 0`.
4. Both authorized links were already members of the filtered navigation tree and already existed in the DOM.
5. The route-location effect called `expandActiveNodes(filteredMenuItems)`. Opening Shipping Methods expanded its ancestor chain and set `operations-shipping=true`; opening Payment Methods set `operations-payments=true`.
6. Both version-1 localStorage keys persisted those booleans globally, without tenant/user scope. Reload restored them, creating the appearance that visiting each page had discovered its availability.

**Machine comparison of the four pre-fix captures asserts complete session DTO equality and navigation membership equality. Only expansion changed.**

| Transition | Exact expansion delta | Session/permissions/capabilities | Navigation membership |
|---|---|---|---|
| A → B, Shipping opened | `operations-shipping`: absent → `true` | Unchanged | Unchanged |
| B → C, Payment opened | `operations-payments`: absent → `true` | Unchanged | Unchanged |
| C → D, hard reload | None | Unchanged | Unchanged |

Additional defects established by the source audit:

- Payments and Shipping inherited generic `storefront.orders.view` in the sidebar instead of their route-management permissions. Structural parents also acquired a generic Design permission, incorrectly blocking permission-authorized Operations users such as Manager.
- Expanded nested menus had fixed 800px/1200px caps. The complete Operations tree measured **1316px** in the corrected desktop runtime. Keeping the old caps would clip later entries when the hierarchy was fully expanded.

These additional defects were corrected at the navigation projection/rendering boundary; no backend grant, session policy, or route guard was relaxed.

## 2. Disposable reproduction and first-login lifecycle

| Fixture | Tenant ID | Owner user ID |
|---|---|---|
| Pre-fix reproduction, `sidebar-pre-20261007` | `c5372303-d2eb-4c77-a543-4b84b8e76d3c` | `8f3794ce-b9be-4ad8-8830-e34b1b5db91c` |
| Post-fix acceptance, `sidebar-post-20261007` | `df829b1f-0903-443f-b618-047f5f68cb0a` | `e5655270-ce0d-4eb4-a90b-b97321e9f91a` |

Both were registered through the visible registration form. Registration establishes an immediate authenticated session; the post-fix fixture also completed explicit logout/password-login before activation.

Observed initial authority:

- `hasHydrated=true`, `isAuthenticated=true` after the authoritative `/api/auth/session` DTO was accepted.
- Role: `Owner`.
- Effective permissions included `storefront.orders.view`, `storefront.design.manage`, `storefront.payments.manage`, `shipping.manage`, and the other Owner grants.
- Canonical tenant ID matched the session user and lifecycle `vendorId` throughout A–D.
- New storefront status: Disabled. After verification and the normal live-status save: Live.
- CommerceHub lifecycle remained Pending, `tenantActive=true`, `marketplaceApproved=false`, `kycVerified=false`.
- Its explicit navigation capabilities remained `commercehub.finance.view`, `commercehub.payments.view`, and `commercehub.payments.submit`.
- Marketplace operating blocks remained true. Activating the owner's own storefront did not grant marketplace operating capabilities.
- Registration seeded three shipping methods with the default Main Warehouse. Payment configuration was initially empty/not configured; that is valid administration data, not a disabled administration module.

For A, the Storefront and Operations branches were opened through their normal controls. Payments and Shipping remained collapsed. The pre-fix screenshots/state captures therefore distinguish a missing link from a link hidden by its containing accordion.

### Runtime blockers encountered and resolved

**Registration initially returned HTTP 500.** The user supplied the corresponding API exception:

```text
23502: null value in column "UseBaileys" of relation "Tenants"
violates not-null constraint
```

The existing worktree model declared `UseBaileys` with a generated database default, but MAIN's database lacked that default. EF migration inspection showed exactly one pending migration: `20261007154500_MAIN_BaileysOnlyTransport`.

With the user's explicit approval, the existing migration was applied:

```powershell
dotnet ef database update 20261007154500_MAIN_BaileysOnlyTransport --project "PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj" --startup-project "PointOfSale.API/PointOfSale.API.csproj" --no-build -- --environment Development
```

EF reported `Applying migration ... Done.` Registration then returned HTTP 201. This existing migration sets the missing database default and normalizes existing false transport flags to true. It was not authored or modified by this sidebar task.

**Publishing before email verification returned HTTP 400 `EMAIL_NOT_VERIFIED`.** The protection worked correctly. A temporary, strictly scoped fixture inserted a known hashed verification token for each of these two disposable owners; the browser completed the normal verification ceremony/API and then the normal live-status save. No production verification check was changed or bypassed. Three additional verified role-test users were seeded only in the post-fix disposable tenant: Admin, Manager, and Cashier. All then authenticated through the real login/session endpoints.

No shared products, orders, payment configuration, shipping configuration, or external carrier/WhatsApp accounts were edited. The approved transport migration is the sole shared-database change. Disposable tenant/user fixtures remain identifiable by the IDs and audit names above.

## 3. Exact visibility trace for both entries

| Required audit field | Payment Methods | Shipping Methods |
|---|---|---|
| Source definition | `OPERATIONS_NAVIGATION`, `operations-payments`, `/ecommerce/payment-methods` | `OPERATIONS_NAVIGATION`, `operations-shipping`, `/ecommerce/shipping-methods` |
| Source file | `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js` | Same |
| Route registry | `App.jsx`, eager `PaymentMethods` import and protected route | `App.jsx`, eager `ShippingMethods` import and protected route |
| Existing route requirement | `storefront.payments.manage` | `shipping.manage` |
| Previous sidebar requirement | Generic inherited `storefront.orders.view`, plus generic structural ancestor filtering | Same generic requirement; `requiredRoles` metadata was not evaluated by this renderer |
| Corrected sidebar requirement | Canonical `storefront.payments.manage`, inherited from the Payments node | Canonical `shipping.manage`, inherited from the Shipping node |
| Membership predicate | `filterMenuNode` → `hasPosPermission(user, item.permission)` | Same |
| Capability predicate | None on this node or its Storefront ancestors | None on this node or its Storefront ancestors |
| Tenant/feature requirement | Authenticated canonical tenant and permission authority; no storefront-enabled/readiness flag controls this configuration link | Same |
| Data source | Static IA tree + `useAuthStore.user.effectivePermissions`; expansion is local React state and localStorage presentation data | Same |
| Old first-load presentation | No cached boolean → collapsed parent(s) | Same |
| Old later trigger | Route location/active screen → `expandActiveNodes` → `operations-payments=true` | Route location/active screen → `expandActiveNodes` → `operations-shipping=true` |
| Reload behavior | Old unscoped expansion cache restored | Same |
| Corrected initialization | Model-derived initial Operations subtree expansion; validated same-actor version-2 preferences may override it | Same |

Relevant source chain:

```text
TenantRegistrationService: Owner + atomic tenant bootstrap
AuthController: server-derived EffectivePermissions and navigation/lifecycle DTO
AuthBootstrap: /auth/session → requirePosVendorSessionUserAuthority
authStore: validated canonical user; authority is not persisted in localStorage
ProtectedRoute: hydration/authentication boundary
Sidebar: static IA localization/projection → permission/capability filtering
sidebarExpansion: model defaults + actor-scoped presentation preferences
Sidebar location effect: reveal active ancestor chain
Sidebar renderer: content-sized accordion, no fixed height cap
```

The route pages update their own configuration/readiness state. `PaymentMethods.jsx` loads its catalog/configuration; the shipping workspace hooks load carriers, warehouses, methods, and audit. Neither registers these sidebar entries or supplies their navigation authority. `posRuntimeCache`/React Query invalidation on actor transitions remains in the existing auth bootstrap. There is no Redux navigation store and no route-lazy module registration responsible for the behavior.

## 4. Initial bootstrap versus direct-navigation network evidence

The pre-fix registration/bootstrap CLI request listing recorded:

- `POST /api/platform/register` → 201 after the migration.
- `GET /api/auth/session` → 200, with Owner permissions already complete.
- `GET /api/settings`, `/api/auth/verification-status`, `/api/marketplace/incoming-requests` → 200.
- Dashboard reads: `/api/dashboard/stats`, `sales-trend`, `top-products`, `recent-sales`, `low-stock`, `category-sales`, and `payment-methods` → 200.
- `/api/admin/store-config` → 200.

**No navigation-discovery request was missing.** The absent payment catalog/shipping readiness calls were page-data calls, appropriately made when their respective pages mount. They are not prerequisites for sidebar membership or expansion.

Exact live direct-navigation request sets, recorded again after the navigation fix:

| Route | Page-specific requests, all HTTP 200 for Owner |
|---|---|
| Shipping Methods | `/api/admin/shipping/supported-carriers`; `/api/admin/shipping/carrier-accounts`; `/api/warehouses`; `/api/admin/shipping/audit?limit=100`; `/api/shippingmethods?activeOnly=false` |
| Payment Methods | `/api/admin/payment-methods/catalog`; `/api/admin/payment-methods` |

Common `/api/auth/session`, settings, notification, and verification-status reads also occur on full-document navigation. That common session bootstrap happens on other pages too. It did not change the baseline A–D session DTO.

Post-fix activation evidence records the status mutation and normal bootstrap/config reads but **no payment catalog or shipping workspace requests**, while both sidebar links pass the hit-target/ancestor checks. Thus page-data fetches are demonstrably unnecessary to initialize the links.

Development StrictMode caused duplicate initial GETs. They are recorded as duplicates, not mistaken for a second capability-discovery step.

**Capture limitation:** Early documents loaded more static modules than the browser's default resource-timing buffer. The baseline A–D JSON has complete authority/navigation/geometry evidence but empty timing arrays; the initial bootstrap request listing was captured with `playwright-cli requests`. For subsequent documents the audit increased the instrumentation-only timing buffer to 10,000 and saved exact API URLs/statuses in checkpoint JSON. The post-fix direct-navigation request sets above are live measured results, not fabricated pre-fix timing logs.

## 5. Capability, permission, and availability conclusions

- No capability changed from unknown/false to enabled between baseline A–D.
- No authority-store mutation or stale capability cache explains the Owner symptom.
- No canonical tenant identity mismatch occurred in the captured first-login flow.
- Sidebar authority waits for the accepted session through the existing protected layout.
- Unknown/loading configuration/readiness is not evidence that a management workspace is disabled. Disabled storefront availability and unconfigured payment methods still permit authorized management of those settings.
- An explicit empty CommerceHub capability array is tested with a valid Pending lifecycle and does not suppress the owner's own POS configuration routes.
- A malformed/missing mandatory session capability array remains an **invalid authority payload**, not a guessed “disabled feature”; the existing session admission fails closed.
- Unavailable/blocked marketplace functionality still uses its existing explicit capabilities and lifecycle blocks. This patch does not invent any feature status or collapse a capability/readiness state into an expansion boolean.

## 6. Small architectural fix

1. **Navigation model defaults:** The Storefront root declares initial expansion, and Operations declares `initialExpansion: 'subtree'`. A generic model traversal derives initial expansion for its groups. No payment/shipping path exception exists in the sidebar.
2. **Scoped hydration:** `sidebarExpansion.js` initializes defaults and accepts only version-2 presentation snapshots matching `tenantId:userId`. Legacy unscoped, malformed, null, empty, and foreign-actor snapshots cannot carry route-visit history into a new actor's initial navigation.
3. **Actor transitions:** The sidebar presentation subtree is keyed by canonical actor scope, resetting in-memory expansion synchronously when actor/tenant changes. The persisted envelope contains expansion preferences, never authority grants.
4. **Permission projection:** Structural parents are projected from their authorized children rather than acquiring a generic Design permission. Payments and Shipping declare their existing canonical route-management requirements in the IA model. Other affected Operations nodes also declare their existing Orders, Promotions, Analytics, Auth Provider, and Commerce AI route permissions so opening structural access cannot expose unrelated unauthorized management links. This preserves Manager's Shipping/analytics access while hiding payment management, auth providers, Commerce AI, pixel configuration, and analytics configuration.
5. **Unclipped layout:** Grid-based content-sized accordion rows replace fixed submenu height caps. Collapsed groups are inert. Explicit same-actor collapse choices persist across remount/logout-login; the fix is initial discoverability, not permanently forced visibility.

There are no new timers, reloads, redirects to these workspaces, public routes, duplicated capability logic, backend permission grants, or permission-check removals.

## 7. Files changed by this task

| File | Purpose |
|---|---|
| `PointOfSale.Web/src/components/Sidebar.jsx` | Model/scoped expansion integration, permission projection, unclipped/inert submenus |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js` | Initial navigation metadata and canonical Operations permission requirements |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/sidebarExpansion.js` | Generic expansion-default/cache admission layer |
| `PointOfSale.Web/src/components/Sidebar.firstLogin.test.tsx` | First session, navigation hydration, cache/scope isolation, authority and language regressions |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.test.tsx` | Version-2 persistence assertion |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/wave1StorefrontInformationArchitecture.test.tsx` | Valid Shipping permission fixture and initial-expanded toggle semantics |
| `PointOfSale.Tests/Security/PosPermissionAuthorizationTests.cs` | Backend payment/shipping role and endpoint-policy matrix |
| `PointOfSale.Tests/Security/TenantRegistrationFreshDatabaseTests.cs` | New tenant Owner/payment/shipping grant assertions |
| `tests/browser/sidebarFirstLogin.cli.js` | Repeatable real-sidebar visibility, permission, ancestor geometry, and pointer-hit acceptance checker |
| This report | Evidence, scope, failures, and final verdict |

The pre-existing WhatsApp/Baileys modifications, deletions, migration, and report were preserved. No commit was made. Temporary fixture/tool source and generated build artifacts are removed after evidence capture; browser artifacts remain under `.playwright-cli/`.

## 8. Automated regression results

| Gate | Result |
|---|---|
| Focused frontend navigation/auth/IA selection | **144 passed**, 7 files, 0 failed |
| Supplemental payment page, permission-route guard, warehouse frontend selection | **17 passed**, 3 files, 0 failed |
| Relevant backend registration/session/permission/tenant selection | **69 passed**, 0 failed, 0 skipped |
| Broader backend authority architecture selection | **14 passed, 3 failed**; classified in §11 |
| Production `npm run build` | **PASS**, 5237 modules; existing large-chunk warning |
| Baseline evidence comparison | **PASS**: full session DTO and navigation membership unchanged A–D |
| Real sidebar checker | **PASS** at 18 saved checkpoints, including new-session/activation before route visits and final Manager projection review |
| `git diff --check` | **PASS**; Windows line-ending notices |

Focused commands executed from `PointOfSale.Web`:

```powershell
npm test -- --run src/components/Sidebar.firstLogin.test.tsx src/modules/storefront/informationArchitecture src/components/Sidebar.users.test.tsx src/components/AuthBootstrap.test.tsx src/auth/posVendorSessionAuthority.test.ts
npm test -- --run src/pages/PaymentMethods.test.tsx src/auth/posPermissions.test.ts src/store/authStore.test.ts src/components/PosPermissionRouteGuard.test.tsx src/tests/warehouseFrontendCertification.test.ts
npm run build
```

The supplemental command matches three existing test files; the requested `posPermissions.test.ts` and `authStore.test.ts` patterns do not identify existing files. No nonexistent suite is counted as passing. Permission/session coverage is supplied by the focused bootstrap/session suites and the new regression suite.

Backend commands executed from repository root:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --filter "FullyQualifiedName~TenantRegistrationFreshDatabaseTests|FullyQualifiedName~PosSession|FullyQualifiedName~PosPermissionAuthorizationTests|FullyQualifiedName~TenantAuthorityMemoTests" --no-restore --verbosity quiet
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --no-build --filter "FullyQualifiedName~PosPermissionAuthorityCertificationTests|FullyQualifiedName~AdminPaymentMethodReadAuthorityCertificationTests|FullyQualifiedName~AdminShippingMethodReadAuthorityCertificationTests|FullyQualifiedName~AdminTenantOperationalScopeTests|FullyQualifiedName~AdminAuthenticationSessionCertificationTests" --verbosity minimal
```

The new regression tests verify delayed initial session hydration without fetching either page, legacy/corrupt/empty cache admission, explicit choice restoration, actor and tenant transitions, permission revocation, Owner/Admin/Manager/Cashier/Warehouse route denial, valid empty marketplace capabilities, malformed authority rejection, and Arabic/English direction-independent discoverability.

## 9. Headed browser A-to-Z acceptance matrix

All work reused the registered headed `opencode-ui-test` session, with short finite-timeout commands. `playwright-cli --help` and `--help open` were inspected after the user's launch concern; this run's opening command returned successfully and `list` confirmed ownership. No unrelated Edge process was terminated.

| Required journey | Measured result |
|---|---|
| A. Brand-new tenant: register → first session → login → activate | **PASS**. Both links before either route was visited |
| B. Existing tenant login | **PASS**. Reused the previously created/activated pre-fix disposable tenant; shared business credentials were not used |
| C. Owner | **PASS**. Both links and both pages |
| D. Admin | **PASS**. Both links on Dashboard and both direct pages |
| D. Manager | **PASS**. Shipping link/page available; payment-management link absent; direct payment route returns to denied landing |
| E. Unauthorized Cashier | **PASS**. Both links absent; both direct routes denied; payment catalog and shipping methods APIs return 403 |
| F. Logout/login | **PASS**. Owner re-login and role/tenant transitions restore correctly scoped navigation |
| G. Hard reload | **PASS** before and after direct-route checks |
| H. Arabic/RTL + English/LTR | **PASS**. Same canonical link authority and reachable target checks |
| I. Desktop 1440×900 | **PASS** |
| J. Mobile 390×844 | **PASS** in Arabic and English with the actual drawer open |
| K. Direct navigation | **PASS**. Pages open when authorized; they were tested only after first-login/activation acceptance was established |

The checker scrolls the actual sidebar as needed and verifies `document.elementFromPoint` hits each link, plus ancestor opacity/inert/clip boundaries. It does not equate DOM membership or direct URL accessibility with visible navigation certification.

### Evidence index

Baseline state captures:

- [A: activated, both link groups collapsed](../../.playwright-cli/sidebar-pre-A-activated.json)
- [B: Shipping visited](../../.playwright-cli/sidebar-pre-B-shipping.json)
- [C: Payment visited](../../.playwright-cli/sidebar-pre-C-payment.json)
- [D: hard reload](../../.playwright-cli/sidebar-pre-D-reload.json)
- [Machine-computed state deltas and checkpoint summary](../../.playwright-cli/sidebar-audit-summary.json)

Primary acceptance:

- [First registered session checks](../../.playwright-cli/sidebar-post-first-session-checks.json)
- [Explicit first password-login checks](../../.playwright-cli/sidebar-post-first-login-checks.json)
- [Immediately after successful activation, before either route](../../.playwright-cli/sidebar-post-activated-checks.json)
- [Activation screenshot showing both menu entries and Live status](../../.playwright-cli/sidebar-post-activated-payment.png)
- [Reload before either route](../../.playwright-cli/sidebar-post-reload-before-routes-checks.json)

Languages/viewports:

- [Arabic desktop checks](../../.playwright-cli/sidebar-post-arabic-desktop-checks.json)
- [Arabic mobile screenshot](../../.playwright-cli/sidebar-post-arabic-mobile-shipping.png)
- [Arabic mobile checks](../../.playwright-cli/sidebar-post-arabic-mobile-checks.json)
- [English mobile screenshot](../../.playwright-cli/sidebar-post-english-mobile-shipping.png)
- [English mobile checks](../../.playwright-cli/sidebar-post-english-mobile-checks.json)

Roles, direct navigation, and scope:

- [Admin Dashboard](../../.playwright-cli/sidebar-role-admin-dashboard-checks.json)
- [Manager Dashboard](../../.playwright-cli/sidebar-role-manager-dashboard-checks.json)
- [Final Manager permission-projection review](../../.playwright-cli/sidebar-role-manager-final-checks.json)
- [Cashier Dashboard](../../.playwright-cli/sidebar-role-cashier-dashboard-checks.json)
- [Cashier denied-route screenshot](../../.playwright-cli/sidebar-cashier-denial.png)
- [Direct Shipping network/authority](../../.playwright-cli/sidebar-post-direct-shipping-checks.json)
- [Direct Payment network/authority](../../.playwright-cli/sidebar-post-direct-payment-checks.json)
- [Reload after route visits](../../.playwright-cli/sidebar-post-reload-after-routes-checks.json)
- [Existing disposable tenant login](../../.playwright-cli/sidebar-existing-tenant-login-checks.json)
- [Cross-tenant return to post-fix Owner](../../.playwright-cli/sidebar-post-logout-login-cross-tenant-return-checks.json)
- [Final Owner Dashboard](../../.playwright-cli/sidebar-final-owner-dashboard-checks.json)

Repeat an acceptance checkpoint in the already authenticated headed session:

```powershell
playwright-cli --raw -s=opencode-ui-test eval "window.__sidebarAuditStage='review-checkpoint'"
playwright-cli --raw -s=opencode-ui-test run-code --filename=tests/browser/sidebarFirstLogin.cli.js
```

The session remains registered/headed on the post-fix Owner Dashboard for visible review and reuse.

## 10. Console/page/network classification

| Observation | Classification |
|---|---|
| Initial registration HTTP 500 | Genuine environment/schema mismatch; diagnosed from API exception and resolved with the explicitly approved existing migration |
| Activation HTTP 400 `EMAIL_NOT_VERIFIED` | Correct trust-boundary denial; normal verification completed before activation |
| `/auth/session` and `/auth/refresh` 401 while anonymous/after logout | Expected cookie-session bootstrap rejection; the saved login-transition checkpoints contain only these auth error kinds |
| Cashier payment catalog and shipping-method reads 403 | Expected server-side authorization-denial probes |
| Successful activation, languages/mobile, and Owner direct pages | No console errors or failing API requests in those checkpoints |
| React Router v7 future-flag warnings | Two existing development warnings per document; unrelated to navigation hydration |
| Storefront Settings displays some untranslated `storefront.*` strings | Existing localization gaps outside the two audited menu labels; visible in activation screenshots |
| Production large-chunk warning and .NET analyzer warnings | Existing build/test hygiene warnings, not build failures |
| One intercepted click while the activation confirmation dialog was still open | Test interaction corrected by dismissing the dialog; no application exception |
| CLI quoting/selector/initial evidence-writer errors | Harness mistakes corrected with file-based run-code/valid selectors; not application page errors |
| Two frontend test timeouts and a browser-command timeout during concurrent final production build | Resource-contention run. The identical focused test command subsequently passed all 144 tests with unchanged test timeouts; the registered browser was reused and final Manager/Owner checks passed. No application delay or timeout override was introduced |

The CLI reported no page exceptions during the successful acceptance documents. Console logs and snapshots remain in `.playwright-cli/`. Denial and anonymous-bootstrap failures are not relabeled as successful network requests.

## 11. Unrelated pre-existing failures

The broader backend selection produced **three failures**, not waived or hidden:

1. `PosPermissionAuthorityCertificationTests.RepresentativeAdministrativeEndpoints_MustDeclareBackendPermissionPolicies`: expects a class-level role attribute on `AdminCommerceHubController`; reflection finds no matching role attribute (`Sequence contains no matching element`, assertion line 36/helper line 69).
2. `AdminShippingMethodReadAuthorityCertificationTests.ShippingMethodReadRuntime_MustRemainBackendOwnedAndTenantScoped`: asserts the exact source substring `const admittedResponse = admitStorefrontShippingMethods(response.data);` in the public storefront checkout store; it is absent (line 79).
3. `AdminShippingMethodReadAuthorityCertificationTests.ShippingMethodReadAuthorityDocumentation_MustBeFrozen`: expects `PASS SF-SHIP.READ Shipping Method Read Authority Certification` in `.ai-context/PROJECT_CONTEXT.md`; it is absent (line 100).

The failing test sources and implicated CommerceHub controller, public storefront checkout store, and project-context document have no worktree diff. They were not changed by this task. The selected payment-read/authentication/tenant checks otherwise passed. This report does not claim a full-suite pass or resolve those unrelated source/documentation contracts.

## 12. Final verdict

**PASS — MAIN first-login/new-tenant sidebar navigation acceptance is satisfied.**

Payment Methods and Shipping Methods are discoverable immediately from the correctly initialized, permission-filtered navigation model after new-tenant login/activation. Direct navigation is unnecessary. Reload, logout/login, actor/tenant changes, English/Arabic, desktop/mobile, and live role-denial checks preserve the correct result. Backend authorization and protected routes remain enforced.

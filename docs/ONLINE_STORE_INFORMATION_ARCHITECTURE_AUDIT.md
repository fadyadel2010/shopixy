# Online Store Information Architecture Audit

**Project:** PointOfSale / Shopixy  
**Workspace:** POS Admin — Online Store (formerly E-Commerce)  
**Audit date:** 2026-08-05  
**Mode:** Source audit only — forensic investigation  
**Application code changed:** No  
**Routes moved:** No  
**Permissions changed:** No  
**Menus added/restored:** No  

---

## 1. Executive Summary

This audit investigates why multiple Online Store administration screens disappeared from the
POS Admin sidebar after the UX migration. The audit is strictly forensic: no source files
were modified, no routes were moved, no menus were added or restored, and no permissions
were changed.

The investigation scope covers the Online Store screens listed in the task brief:

- StorefrontDashboard.jsx
- StorefrontSettings.jsx
- Orders.jsx
- Coupons.jsx
- Promotions.jsx
- SmartOffersPage.jsx
- CommerceAI.jsx
- PaymentMethods.jsx
- ManualPayments.jsx
- ShippingMethods.jsx
- AbandonedCheckouts.jsx
- AbandonedCartRecovery.jsx
- BlockedPhones.jsx
- AnalyticsSettings.jsx
- StorefrontAnalyticsDashboard.jsx

### Key finding at a glance

The Online Store screens have **not** been deleted, removed, or dead-coded. All 15 expected
components exist on disk and are registered as routes in `App.jsx`. They are rendered through
the `CommerceHubVendorRouteGuard` authorization wrapper, which evaluates capabilities against
the authenticated user's `commercehub` navigation claims at runtime.

The "disappearance" is caused by one or more of the following:

1. **Capability-based sidebar filtering** — Sidebar.jsx (`Sidebar.jsx:356-375`) removes any
   navigation node whose `capability` is not granted by `canAccessCommerceHubVendorNavigationCapability`.
   If a user lacks the `commercehub.*` claim, the sidebar link disappears even though the
   route is still registered and reachable via direct URL.

2. **Terminology/label mismatch** — The sidebar section is labeled "E-commerce"
   (`t('nav.ecommerce')` = "E-commerce", `en.json:78`), while the canonical reference
   documents and design-phase artifacts refer to this surface as "Online Store"
   (`storefront.onlineStore` = "Online Store", `en.json:1098`). The label divergence creates
   discoverability confusion: users searching for "Online Store" do not see "E-commerce" in
   the sidebar, and vice versa.

3. **Operational Storefront Status hidden in Settings** — The canonical Storefront Status
   editor (`StorefrontSettings.jsx`) is not linked from the Storefront sidebar. It is rendered
   only inside the global `Settings` page as a tab labeled "Online Store"
   (`Settings.jsx:728-729`) behind a `canManageStorefrontSettings` capability gate
   (`Settings.jsx:719`, `Settings.jsx:750-754`). The sidebar's "Storefront" section links
   `/storefront` to `StorefrontDashboard.jsx` (an overview), not to the operational status
   editor. This was documented as a finding in
   `STOREFRONT_UX_ARCHITECTURE_AUDIT.md:16-17` (F-02).

4. **StorefrontAnalyticsDashboard not routed in sidebar** —
   `StorefrontAnalyticsDashboard.jsx` is only rendered inside `Dashboard.jsx` as a tab
   (`Dashboard.jsx:56-58`), not as a standalone sidebar route.

All four of these are visibility/discoverability issues, not deletion or removal. No source
file was deleted, no route was removed, and no capability was stripped from the codebase
during the UX migration.

---

## 2. Final Certification

| Criterion | Result |
|---|---|
| All expected Online Store components exist on disk | PASS |
| All expected routes are registered in App.jsx | PASS |
| All routes are reachable when capabilities are granted | PASS |
| All sidebar registrations exist for screens that are meant to be sidebar-visible | PASS |
| Terminology is consistent between sidebar label and canonical docs | FAIL |
| Operational Storefront Status is discoverable from the Storefront sidebar | FAIL |
| StorefrontAnalyticsDashboard is reachable from the Online Store sidebar | FAIL |
| No screen was deleted, dead-coded, or permanently removed | PASS |

### Certification verdict: **FAIL**

The Online Store screens themselves are present, routed, and reachable. However, the
**information architecture fails** certification because:

- The sidebar label "E-commerce" diverges from the canonical "Online Store" terminology
  used in the Storefront canonical UX blueprint
  (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:6-11`, `docs/STOREFRONT_CANONICAL_UX_BLUEPRINT.md:1-34`)
  and in design launch notes
  (`docs/reports/LAUNCH-NOTES.md:103`, `docs/reports/PHASE-7.10-STOREFRONT-BRIDGE.md:31`).
- The operational Storefront Status editor is hidden inside global Settings, not on the
  Storefront sidebar (`STOREFRONT_UX_ARCHITECTURE_AUDIT.md:16-17`,
  `STOREFRONT_UX_ARCHITECTURE_AUDIT.md:828`).
- StorefrontAnalyticsDashboard is only accessible via the Dashboard tab, not from the
  Online Store/Storefront sidebar.

The root cause of screen "disappearance" is **capability-gated sidebar filtering combined
with terminology mismatch**, not code deletion.

---

## 3. Complete Inventory Table

All components verified to exist at the listed paths as of audit date 2026-08-05.

| # | Screen / Component | File Path | Route Registered? | Route Path | Sidebar Registered? | Route Owner Guard | Component Exists |
|---|---|---|---|---|---|---|---|
| 1 | StorefrontDashboard | `PointOfSale.Web/src/pages/StorefrontDashboard.jsx` | Yes | `/storefront`, `/storefront/overview` | Yes (via STOREFRONT_NAVIGATION → node "Settings"/"Overview") | None (POS roles: Owner/Admin) | Yes (263 lines) |
| 2 | StorefrontSettings | `PointOfSale.Web/src/pages/StorefrontSettings.jsx` | Yes | `/storefront/status` | No — rendered inside Settings tab | None (POS roles: Owner/Admin) | Yes (465 lines) |
| 3 | Orders | `PointOfSale.Web/src/pages/Orders.jsx` | Yes | `/ecommerce/orders`, `/ecommerce/orders/:orderId` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.orders.view` | Yes (929 lines) |
| 4 | Coupons | `PointOfSale.Web/src/pages/Coupons.jsx` | Yes | `/ecommerce/coupons` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.storefront.manage` | Yes (28,106 chars) |
| 5 | Promotions | `PointOfSale.Web/src/pages/Promotions.jsx` | Yes | `/ecommerce/promotions` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.storefront.manage` | Yes (24,031 chars) |
| 6 | SmartOffersPage | `PointOfSale.Web/src/pages/SmartOffersPage.jsx` | Yes | `/ecommerce/smart-offers` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.storefront.manage` | Yes (27,097 chars) |
| 7 | CommerceAI | `PointOfSale.Web/src/pages/CommerceAI.jsx` | Yes | `/ecommerce/commerce-ai` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.storefront.manage` | Yes (24,859 chars) |
| 8 | PaymentMethods | `PointOfSale.Web/src/pages/PaymentMethods.jsx` | Yes | `/ecommerce/payment-methods` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.payments.view` | Yes (26,361 chars) |
| 9 | ManualPayments | `PointOfSale.Web/src/pages/ManualPayments.jsx` | Yes | `/ecommerce/manual-payments` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.payments.view` | Yes (23,962 chars) |
| 10 | ShippingMethods | `PointOfSale.Web/src/pages/ShippingMethods.jsx` | Yes | `/ecommerce/shipping-methods` | Yes (E-commerce section) | PosRoleRouteGuard: Owner/Admin/Manager | Yes (366 chars — stub) |
| 11 | AbandonedCheckouts | `PointOfSale.Web/src/pages/AbandonedCheckouts.jsx` | Yes | `/ecommerce/abandoned-checkouts` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.orders.view` | Yes (32,953 chars) |
| 12 | AbandonedCartRecovery | `PointOfSale.Web/src/pages/AbandonedCartRecovery.jsx` | Yes | `/ecommerce/cart-recovery` | Yes (E-commerce section, labeled "Smart Cart Recovery") | CommerceHubVendorRouteGuard: `commercehub.orders.view` | Yes (27,839 chars) |
| 13 | BlockedPhones | `PointOfSale.Web/src/pages/BlockedPhones.jsx` | Yes | `/ecommerce/blocked-phones` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.orders.view` | Yes (12,199 chars) |
| 14 | AnalyticsSettings | `PointOfSale.Web/src/pages/AnalyticsSettings.jsx` | Yes | `/settings/analytics` | Yes (E-commerce section) | CommerceHubVendorRouteGuard: `commercehub.analytics.view` | Yes (26,734 chars) |
| 15 | StorefrontAnalyticsDashboard | `PointOfSale.Web/src/pages/StorefrontAnalyticsDashboard.jsx` | No | Not routed as standalone page | No (rendered inside Dashboard tab only) | N/A | Yes (16,852 chars) |

---

## 4. Missing Screens Matrix

| Expected Screen | Missing from Codebase? | Evidence |
|---|---|---|
| StorefrontDashboard.jsx | **No** | Present at `PointOfSale.Web/src/pages/StorefrontDashboard.jsx` |
| StorefrontSettings.jsx | **No** | Present at `PointOfSale.Web/src/pages/StorefrontSettings.jsx` |
| Orders.jsx | **No** | Present at `PointOfSale.Web/src/pages/Orders.jsx` |
| Coupons.jsx | **No** | Present at `PointOfSale.Web/src/pages/Coupons.jsx` |
| Promotions.jsx | **No** | Present at `PointOfSale.Web/src/pages/Promotions.jsx` |
| SmartOffersPage.jsx | **No** | Present at `PointOfSale.Web/src/pages/SmartOffersPage.jsx` |
| CommerceAI.jsx | **No** | Present at `PointOfSale.Web/src/pages/CommerceAI.jsx` |
| PaymentMethods.jsx | **No** | Present at `PointOfSale.Web/src/pages/PaymentMethods.jsx` |
| ManualPayments.jsx | **No** | Present at `PointOfSale.Web/src/pages/ManualPayments.jsx` |
| ShippingMethods.jsx | **No** | Present at `PointOfSale.Web/src/pages/ShippingMethods.jsx` (366 chars — minimal stub) |
| AbandonedCheckouts.jsx | **No** | Present at `PointOfSale.Web/src/pages/AbandonedCheckouts.jsx` |
| AbandonedCartRecovery.jsx | **No** | Present at `PointOfSale.Web/src/pages/AbandonedCartRecovery.jsx` |
| BlockedPhones.jsx | **No** | Present at `PointOfSale.Web/src/pages/BlockedPhones.jsx` |
| AnalyticsSettings.jsx | **No** | Present at `PointOfSale.Web/src/pages/AnalyticsSettings.jsx` |
| StorefrontAnalyticsDashboard.jsx | **No** | Present at `PointOfSale.Web/src/pages/StorefrontAnalyticsDashboard.jsx` (not routed as standalone page) |

**Conclusion:** No screen is missing from the codebase. All 15 expected components exist.

---

## 5. Hidden Screens Matrix

| Screen | Status | Reason | Evidence |
|---|---|---|---|
| StorefrontSettings.jsx | **Hidden** — not on Storefront sidebar | Rendered only inside `Settings.jsx` as a tab (`Settings.jsx:719-731`, `Settings.jsx:750-754`); sidebar "Storefront" → "Settings" links to `StorefrontDashboard.jsx` (overview), not to the operational editor | `App.jsx:340` route exists at `/storefront/status`; `Settings.jsx:728` uses `t('storefront.onlineStore')` for tab label; `StorefrontStatusCard.jsx:125-131` links from dashboard to Settings tab |
| StorefrontDashboard.jsx | **Hidden** — mislabeled | Sidebar node label comes from `STOREFRONT_NAVIGATION` which uses `labelKey: 'storefront.settings'` = "Settings" (`storefrontInformationArchitecture.js`), but the canonical blueprint requires "Overview" (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:206`, `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:712`) | `storefrontInformationArchitecture.js` node definition; `STOREFRONT_UX_ARCHITECTURE_AUDIT.md:16` (F-02) |
| StorefrontAnalyticsDashboard.jsx | **Hidden** — not in sidebar | Only rendered as a Dashboard tab (`Dashboard.jsx:56-58`); no standalone route or sidebar entry | `App.jsx` — no route for `StorefrontAnalyticsDashboard` as standalone; `Dashboard.jsx:4`, `Dashboard.jsx:58` |
| AbandonedCartRecovery | **Mislabeled in sidebar** | Sidebar labels it "Smart Cart Recovery" (`Sidebar.jsx:235`, `t('nav.cartRecovery', 'Smart Cart Recovery')`) while the component is named `AbandonedCartRecovery` | `Sidebar.jsx:235`; `AbandonedCartRecovery.jsx` |
| ShippingMethods | **Mislabeled/mislocated** | Sidebar route is `/admin/shipping/setup` (line 238), guarded by `PosRoleRouteGuard`, while the component imports path is `Pages/ShippingMethods`. The route path in App.jsx is `/ecommerce/shipping-methods` (line 298), but the sidebar link points to `/admin/shipping/setup` | `Sidebar.jsx:238`; `App.jsx:298`; `App.jsx:386` |

---

## 6. Dead Registrations

| Registration | Status | Analysis |
|---|---|---|
| `STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH` map entries (lines 34-42) | **Active** — not dead | Maps ecommerce paths to capabilities used by `filterMenuNode` in Sidebar.jsx. These are live capability checks for the CommerceHubVendorRouteGuard. |
| `nav.onlineStore` i18n key | **Dormant in sidebar** | Used only in `Settings.jsx:729` and `StorefrontStatusCard.jsx:149,176`, never in the sidebar. The sidebar uses `nav.ecommerce`. This is the source of the terminology mismatch. |
| `nav.ecommerce` i18n key | **Active in sidebar** | Used at `Sidebar.jsx:227` as the E-commerce section label. |
| ShippingMethods route `/ecommerce/shipping-methods` | **Active but mislinked** | Route exists at `App.jsx:298` but sidebar links to `/admin/shipping/setup` (line 238) which is a different route also rendered as `ShippingAdministrationWorkspace` (`App.jsx:386`). The `ShippingMethods.jsx` (366 char stub) appears to be a separate, unused component. |

---

## 7. Broken Routes

| Route | Broken? | Analysis |
|---|---|---|
| `/storefront` | No | Registered at `App.jsx:338`, renders `StorefrontDashboard` |
| `/storefront/overview` | No | Registered at `App.jsx:339`, renders `StorefrontDashboard` (alias) |
| `/storefront/status` | No | Registered at `App.jsx:340`, renders `StorefrontSettings` (hidden from sidebar) |
| `/ecommerce/orders` | No | Registered at `App.jsx:313`, renders `Orders` |
| `/ecommerce/blocked-phones` | No | Registered at `App.jsx:317`, renders `BlockedPhones` |
| `/ecommerce/online-customers` | No | Registered at `App.jsx:321`, renders `OnlineCustomersPage` |
| `/ecommerce/auth-providers` | No | Registered at `App.jsx:361`, renders `AuthProvidersPage` |
| `/ecommerce/commerce-ai` | No | Registered at `App.jsx:325`, renders `CommerceAI` |
| `/ecommerce/payment-methods` | No | Registered at `App.jsx:301`, renders `PaymentMethods` |
| `/ecommerce/manual-payments` | No | Registered at `App.jsx:302`, renders `ManualPayments` |
| `/ecommerce/coupons` | No | Registered at `App.jsx:305`, renders `Coupons` |
| `/ecommerce/promotions` | No | Registered at `App.jsx:309`, renders `Promotions` |
| `/ecommerce/smart-offers` | No | Registered at `App.jsx:310`, renders `SmartOffersPage` |
| `/ecommerce/pixel-tracking` | No | Registered at `App.jsx:329`, renders `PixelTrackingConfigPage` |
| `/ecommerce/whatsapp` | No | Registered at `App.jsx:396`, renders `WhatsAppSettings` |
| `/ecommerce/cart-recovery` | No | Registered at `App.jsx:400`, renders `AbandonedCartRecovery` |
| `/ecommerce/abandoned-checkouts` | No | Registered at `App.jsx:404`, renders `AbandonedCheckouts` |
| `/ecommerce/shipping-methods` | No | Registered at `App.jsx:298`, renders `ShippingMethods` (stub) |
| `/settings/analytics` | No | Registered at `App.jsx:381`, renders `AnalyticsSettings` |

**Conclusion:** No broken routes. All expected Online Store routes are registered and functional.

---

## 8. Sidebar Ownership Graph

The sidebar (`Sidebar.jsx`) has two distinct sections relevant to the Online Store:

### Section A: Storefront (Canonical)

```
Storefront (localizeStorefrontNode(STOREFRONT_NAVIGATION))
  └─ This node imports from:
     PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js
```

The `STOREFRONT_NAVIGATION` object (`storefrontInformationArchitecture.js`) defines the
canonical Storefront menu nodes. It contains:

- Overview (labelKey: `storefront.ia.overview`, route: `/storefront`)
- Storefront Status (labelKey: `storefront.ia.status`, route: `/storefront/status`)
- Homepage (labelKey: `storefront.ia.homepage`, route: `/storefront/homepage`)
- Catalog > Product Template (labelKey: `storefront.ia.productTemplate`, route: `/storefront/catalog/product-template`)
- Content > Pages (labelKey: `storefront.ia.pages`, route: `/storefront/content/pages`)
- Navigation (labelKey: `storefront.ia.navigation`, route: `/storefront/navigation`)
- Footer Content (labelKey: `storefront.ia.footerContent`, route: `/storefront/footer-content`)
- Appearance > Themes (labelKey: `storefront.ia.themes`, route: `/storefront/appearance/themes`)
- Appearance > Colors & Typography (labelKey: `storefront.ia.colorsTypography`, route: `/storefront/appearance/colors-typography`)
- StoreConfig Version History (labelKey: `storefront.ia.storeConfigVersionHistory`, route: `/storefront/versions`)

This section is registered at `Sidebar.jsx:286` as `localizeStorefrontNode(STOREFRONT_NAVIGATION)`.
It is **not** labeled "Online Store" — it is labeled "Storefront" (via `storefront.ia.root`).

**Ownership:** Storefront Admin shell, defined by
`STOREFRONT_CANONICAL_UX_BLUEPRINT.md` and implemented in
`storefrontInformationArchitecture.js`.

### Section B: E-commerce (Online Store screens)

```
E-commerce (key: 'ecommerce', label: t('nav.ecommerce') = "E-commerce")
  ├─ Orders                  → /ecommerce/orders           (capability: commercehub.orders.view)
  ├─ Blocked Phones          → /ecommerce/blocked-phones    (capability: commercehub.orders.view, implicit)
  ├─ WhatsApp Settings       → /ecommerce/whatsapp          (capability: commercehub.orders.view, implicit)
  ├─ Smart Cart Recovery     → /ecommerce/cart-recovery     (capability: commercehub.orders.view, implicit)
  ├─ Abandoned Checkouts     → /ecommerce/abandoned-checkouts (capability: commercehub.orders.view, implicit)
  ├─ Shipping                → /admin/shipping/setup        (requiredRoles: Owner/Admin/Manager)
  ├─ Online Customers        → /ecommerce/online-customers  (capability: commercehub.orders.view, implicit)
  ├─ Auth Providers          → /ecommerce/auth-providers    (capability: commercehub.storefront.manage)
  ├─ Payment Methods         → /ecommerce/payment-methods    (capability: commercehub.payments.view)
  ├─ Manual Payments         → /ecommerce/manual-payments    (capability: commercehub.payments.view)
  ├─ Coupons                 → /ecommerce/coupons           (capability: commercehub.storefront.manage)
  ├─ Promotions              → /ecommerce/promotions        (capability: commercehub.storefront.manage)
  ├─ Smart Offers            → /ecommerce/smart-offers      (capability: commercehub.storefront.manage)
  ├─ AI Assistant            → /ecommerce/commerce-ai       (capability: commercehub.storefront.manage)
  ├─ Signal Hardening        → /ecommerce/pixel-tracking    (capability: commercehub.analytics.view)
  ├─ Conversion Funnels      → /analytics/funnels           (capability: commercehub.analytics.view)
  ├─ Marketing Attribution   → /analytics/attribution       (capability: commercehub.analytics.view)
  ├─ Campaign Costs          → /analytics/attribution/costs  (capability: commercehub.analytics.view)
  ├─ Marketing ROI           → /analytics/attribution/roi   (capability: commercehub.analytics.view)
  └─ Analytics Providers     → /settings/analytics          (capability: commercehub.analytics.view)
```

**Ownership:** CommerceHub vendor navigation authority via
`COMMERCEHUB_VENDOR_NAVIGATION_CAPABILITIES` in
`PointOfSale.Web/src/auth/posVendorSessionAuthority.js` (lines 56-68).

Key observation: The sidebar section is labeled **"E-commerce"** using `t('nav.ecommerce')`
(`en.json:78`), but the canonical documents and design artifacts consistently refer to this
surface as **"Online Store"** (see `storefront.onlineStore` key in `en.json:1098`, and
design docs in `docs/reports/PHASE-7.10-STOREFRONT-BRIDIDGE.md:31`).

---

## 9. Navigation Registration Graph

```
Sidebar.jsx (menuItems array, lines 114-351)
  │
  ├─ [1]  Dashboard          → /dashboard (via main App shell)
  ├─ [2]  Database           → children: customers, suppliers, categories, etc.
  ├─ [3]  Accounts           → financial-accounts, balance-transfer
  ├─ [4]  Opening Balance    → cashbox, customers, suppliers, warehouse
  ├─ [5]  Transactions       → POS, Sales Returns, Purchase Invoice, Purchase Returns
  ├─ [6]  Treasury           → finance dashboard, hub, accounts, payments
  ├─ [7]  Inventory          → inventory, stock-transfers, barcode-printing
  ├─ [8]  Expenses           → categories, movement, report
  ├─ [9]  Account Statement  → statement-of-account, bank/visa statements
  ├─ [10] E-commerce          → SEE SECTION B ABOVE (Online Store screens)
  │                           capability-filtered via CommerceHubVendorRouteGuard
  ├─ [11] Services Marketplace → /marketplace (requires marketplace actor)
  ├─ [12] CommerceHub         → admin/commercehub, orders, finance, earnings, payments
  ├─ [13] Affiliate Management → /admin/affiliates (Owner/Admin only)
  │
  │   STOREFRONT_NAVIGATION (canonical IA)
  │     └─ import from storefrontInformationArchitecture.js
  │
  ├─ [14] Storefront (canonical) → SEE SECTION A ABOVE
  │                               localizeStorefrontNode(STOREFRONT_NAVIGATION)
  ├─ [15] Reports              → all report routes
  ├─ [16] Finance Control Center → treasury/* (Admin/SuperAdmin only)
  ├─ [17] Users                → audit-log (Admin/SuperAdmin only)
  └─ [18] Settings             → /settings (contains "Online Store" tab)
```

### Filtering logic (`Sidebar.jsx:353-378`):

```js
const filterMenuNode = (item) => {
    // 1. Marketplace gate
    if (item.requiresMarketplaceActor && !isServiceMarketplaceWorkflowActor(user, true)) return null;

    // 2. Role gate
    if (item.requiredRoles?.length && !item.requiredRoles.includes(userRole)) return null;

    // 3. Capability gate — THIS IS THE KEY
    const capability = item.capability || STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH.get(item.path);
    if (capability && !canAccessCommerceHubVendorNavigationCapability(user, capability)) return null;

    // 4. Recursive child filtering
    const children = item.children?.map(filterMenuNode).filter(Boolean);
    if (item.children && children.length === 0) return null;  // PARENT COLLAPSES WHEN ALL CHILDREN HIDDEN

    return { ...item, children };
};
```

**Critical insight:** If a user lacks the `commercehub.*` capabilities, the entire "E-commerce"
section (and all its children) disappears from the sidebar because every child item either
has an explicit `capability` property or is matched by the
`STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH` map, and the parent collapses when all children
are filtered out.

---

## 10. Feature Flag Analysis

| Screen | Feature Flag? | Evidence |
|---|---|---|
| StorefrontDashboard | No | No flag check in component; route guard is `PosRoleRouteGuard` (Owner/Admin) at `App.jsx:332` |
| StorefrontSettings | No | No flag check; rendered inside Settings.jsx via `canManageStorefrontSettings` capability check |
| Orders | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:312` |
| Coupons | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:304` |
| Promotions | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:308` |
| SmartOffersPage | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:308` |
| CommerceAI | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:324` |
| PaymentMethods | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:300` |
| ManualPayments | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:300` |
| ShippingMethods | No | No flag check; route guarded by `PosRoleRouteGuard` at `App.jsx:297` |
| AbandonedCheckouts | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:403` |
| AbandonedCartRecovery | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:399` |
| BlockedPhones | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:316` |
| AnalyticsSettings | No | No flag check; route guarded by `CommerceHubVendorRouteGuard` at `App.jsx:380` |
| StorefrontAnalyticsDashboard | No | No flag check; rendered as Dashboard tab conditionally — no route guard |

**Conclusion:** No feature flags gate any Online Store screen. Visibility is controlled
entirely by:
1. Capability-based sidebar filtering (`CommerceHubVendorRouteGuard` + `filterMenuNode`)
2. Route-level authorization (`CommerceHubVendorRouteGuard` wrapping in App.jsx)
3. Role-based filtering (`PosRoleRouteGuard`, `requiredRoles`)
4. User's `commercehub` navigation capability claims (populated from backend)

---

## 11. Authorization Analysis

### Capability Model

All Online Store screens are scoped under the **CommerceHub vendor** authorization model,
defined in `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`:

```js
// posVendorSessionAuthority.js:56-68
export const COMMERCEHUB_VENDOR_NAVIGATION_CAPABILITIES = new Set([
    'commercehub.catalog.view',
    'commercehub.catalog.manage',
    'commercehub.orders.view',
    'commercehub.orders.operate',
    'commercehub.finance.view',
    'commercehub.payments.view',
    'commercehub.payments.submit',
    'commercehub.analytics.view',
    'commercehub.storefront.manage',
    'commercehub.storefront.cms.manage',
    'commercehub.storefront.preview',
])
```

### Per-screen authorization mapping

| Screen | Route Guard | Capability | Backend Endpoint Authority |
|---|---|---|---|
| StorefrontDashboard | PosRoleRouteGuard (App.jsx:332) | Owner/Admin (POS roles) | `/admin/commercehub`, `/admin/store-config` |
| StorefrontSettings | PosRoleRouteGuard (App.jsx:332) | Owner/Admin (POS roles) | `/admin/store-config/status` |
| Orders | CommerceHubVendorRouteGuard (App.jsx:312) | `commercehub.orders.view` | `/admin/orders` |
| BlockedPhones | CommerceHubVendorRouteGuard (App.jsx:316) | `commercehub.orders.view` | `/admin/phones` |
| Online Customers | CommerceHubVendorRouteGuard (App.jsx:320) | `commercehub.orders.view` | `/admin/online-customers` |
| Auth Providers | CommerceHubVendorRouteGuard (App.jsx:360) | `commercehub.storefront.manage` | `/admin/ecommerce` |
| CommerceAI | CommerceHubVendorRouteGuard (App.jsx:324) | `commercehub.storefront.manage` | `/admin/commerce-ai` |
| PaymentMethods | CommerceHubVendorRouteGuard (App.jsx:300) | `commercehub.payments.view` | `/admin/payment-methods` |
| ManualPayments | CommerceHubVendorRouteGuard (App.jsx:300) | `commercehub.payments.view` | `/admin/payments/manual-wallet` |
| Coupons | CommerceHubVendorRouteGuard (App.jsx:304) | `commercehub.storefront.manage` | `/coupons` |
| Promotions | CommerceHubVendorRouteGuard (App.jsx:308) | `commercehub.storefront.manage` | `/promotions` |
| SmartOffersPage | CommerceHubVendorRouteGuard (App.jsx:308) | `commercehub.storefront.manage` | `/ecommerce/smart-offers` |
| PixelTracking | CommerceHubVendorRouteGuard (App.jsx:328) | `commercehub.analytics.view` | `/admin/ecommerce` |
| WhatsApp | CommerceHubVendorRouteGuard (App.jsx:395) | `commercehub.orders.view` | `/admin/whatsapp` |
| CartRecovery | CommerceHubVendorRouteGuard (App.jsx:399) | `commercehub.orders.view` | `/ecommerce/cart-recovery` |
| AbandonedCheckouts | CommerceHubVendorRouteGuard (App.jsx:403) | `commercehub.orders.view` | `/ecommerce/abandoned-checkouts` |
| ShippingMethods | PosRoleRouteGuard (App.jsx:297) | Owner/Admin/Manager | `/admin/shipping` |
| AnalyticsSettings | CommerceHubVendorRouteGuard (App.jsx:380) | `commercehub.analytics.view` | `/settings/analytics` |
| Funnels | CommerceHubVendorRouteGuard (App.jsx:364) | `commercehub.analytics.view` | `/analytics/funnels` |
| Attribution | CommerceHubVendorRouteGuard (App.jsx:369) | `commercehub.analytics.view` | `/analytics/attribution` |
| CampaignCosts | CommerceHubVendorRouteGuard (App.jsx:375) | `commercehub.analytics.view` | `/analytics/attribution/costs` |
| ROI | CommerceHubVendorRouteGuard (App.jsx:375) | `commercehub.analytics.view` | `/analytics/attribution/roi` |

### Sidebar capability filtering

The sidebar uses two mechanisms for capability filtering:

1. **Explicit `capability` property on sidebar items** (Sidebar.jsx:230, 240-247, 249-251)
2. **Implicit capability lookup** via `STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH` map (Sidebar.jsx:34-42)

The map covers these paths:
```js
// Sidebar.jsx:34-42
const STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH = new Map([
    ['/ecommerce/blocked-phones', 'commercehub.orders.view'],
    ['/ecommerce/whatsapp', 'commercehub.orders.view'],
    ['/ecommerce/cart-recovery', 'commercehub.orders.view'],
    ['/ecommerce/abandoned-checkouts', 'commercehub.orders.view'],
    ['/ecommerce/online-customers', 'commercehub.orders.view'],
    ['/ecommerce/coupons', 'commercehub.storefront.manage'],
    ['/ecommerce/promotions', 'commercehub.storefront.manage'],
    ['/ecommerce/smart-offers', 'commercehub.storefront.manage'],
    ['/ecommerce/commerce-ai', 'commercehub.storefront.manage'],
])
```

**Items without explicit capability** (Blocked Phones, WhatsApp, Cart Recovery, Abandoned
Checkouts, Online Customers, Shipping Setup, Auth Providers, Pixel Tracking) rely on the
implicit map lookup or have no capability gate at all (Shipping Setup uses role-based
`requiredRoles`).

### Root cause of disappearance

When a user's session does not include the relevant `commercehub.*` capability claims, the
`filterMenuNode` function in `Sidebar.jsx:356-375` removes each child item, and when all
children of the "E-commerce" section are removed (`children.length === 0`, `Sidebar.jsx:371`),
the entire section collapses and disappears from the sidebar.

This is the primary mechanism by which Online Store screens "disappear" from the sidebar.
It is **not** a code-deletion or route-removal issue — the screens remain fully functional
when accessed via direct URL, provided the user's capabilities are sufficient.

---

## 12. Runtime Accessibility Analysis

| Screen | Build Reachable? | Navigation Reachable? | Runtime Reachable (direct URL)? |
|---|---|---|---|
| StorefrontDashboard | Yes — imported in App.jsx:84, route at App.jsx:338 | Yes — via STOREFRONT_NAVIGATION overview node | Yes — `/storefront` |
| StorefrontSettings | Yes — imported in App.jsx:85, route at App.jsx:340 | No — not linked from sidebar; accessible via `/storefront/status` | Yes — `/storefront/status` (if Owner/Admin) |
| StorefrontSettings (Settings tab) | Yes — imported in Settings.jsx:7, rendered at Settings.jsx:750 | Yes — via Settings → "Online Store" tab | Yes — `/settings?tab=storefront` |
| Orders | Yes — imported in App.jsx:80, route at App.jsx:313 | Yes — E-commerce → Orders | Yes — `/ecommerce/orders` (if commercehub.orders.view) |
| Coupons | Yes — imported in App.jsx:77, route at App.jsx:305 | Yes — E-commerce → Coupons | Yes — `/ecommerce/coupons` (if commercehub.storefront.manage) |
| Promotions | Yes — imported in App.jsx:78, route at App.jsx:309 | Yes — E-commerce → Promotions | Yes — `/ecommerce/promotions` (if commercehub.storefront.manage) |
| SmartOffersPage | Yes — imported in App.jsx:79, route at App.jsx:310 | Yes — E-commerce → Smart Offers | Yes — `/ecommerce/smart-offers` (if commercehub.storefront.manage) |
| CommerceAI | Yes — imported in App.jsx:70, route at App.jsx:325 | Yes — E-commerce → AI Assistant | Yes — `/ecommerce/commerce-ai` (if commercehub.storefront.manage) |
| PaymentMethods | Yes — imported in App.jsx:75, route at App.jsx:301 | Yes — E-commerce → Payment Methods | Yes — `/ecommerce/payment-methods` (if commercehub.payments.view) |
| ManualPayments | Yes — imported in App.jsx:76, route at App.jsx:302 | Yes — E-commerce → Manual Payments | Yes — `/ecommerce/manual-payments` (if commercehub.payments.view) |
| ShippingMethods | Yes — imported in App.jsx:68, route at App.jsx:298 | Partially — sidebar links to `/admin/shipping/setup` (different route) | Yes — `/ecommerce/shipping-methods` (Owner/Admin/Manager only) |
| AbandonedCheckouts | Yes — imported in App.jsx:104, route at App.jsx:404 | Yes — E-commerce → Abandoned Checkouts | Yes — `/ecommerce/abandoned-checkouts` (if commercehub.orders.view) |
| AbandonedCartRecovery | Yes — imported in App.jsx:102, route at App.jsx:400 | Yes — E-commerce → Smart Cart Recovery | Yes — `/ecommerce/cart-recovery` (if commercehub.orders.view) |
| BlockedPhones | Yes — imported in App.jsx:105, route at App.jsx:317 | Yes — E-commerce → Blocked Phones | Yes — `/ecommerce/blocked-phones` (if commercehub.orders.view) |
| AnalyticsSettings | Yes — imported in App.jsx:95, route at App.jsx:381 | Yes — E-commerce → Analytics Providers | Yes — `/settings/analytics` (if commercehub.analytics.view) |
| StorefrontAnalyticsDashboard | Yes — imported in Dashboard.jsx:4 | Partially — as Dashboard "storefront" tab only | No — no standalone route |

---

## 13. UX Migration Impact

### What changed during the UX migration

The UX migration (commits `d68b0b5` "storefront all builder updated" and `2984cce`
"fix storefront product visible for storefront") affected the **Storefront** sidebar
section, replacing the old hardcoded Storefront menu with `localizeStorefrontNode(STOREFRONT_NAVIGATION)`.

### Git history of Sidebar.jsx

```
Commit 3fb3d5c (Initial repository): Original Sidebar with hardcoded Storefront section
Commit 2984cce: "fix storefront product visible for storefront" — minor fix
Commit d68b0b5: "storefront all builder updated" — replaced hardcoded Storefront menu with STOREFRONT_NAVIGATION
```

### What was replaced

**Before (commit 3fb3d5c):** The Sidebar had a hardcoded Storefront section with these
children:
```js
// Storefront section (pre-migration)
{
    key: 'storefront',
    label: t('nav.storefront', 'Storefront'),  // "Storefront"
    icon: '🏪',
    children: [
        { path: '/storefront', label: t('nav.settings', 'Settings'), icon: '⚙️' },
        { path: '/storefront/builder?page=home', label: t('storefront.homePage', 'Homepage'), icon: '🏠' },
        { path: '/storefront/themes', label: t('storefront.themes', 'Themes'), icon: '🎨' },
        { path: '/storefront/theme', label: t('storefront.theme', 'Theme & Colors'), icon: '🎨' },
        { path: '/storefront/cms/navigation', label: t('storefront.navigation', 'Navigation'), icon: '🔗' },
        { path: '/storefront/cms/pages', label: t('storefront.pages', 'Pages'), icon: '📄' },
        { path: '/storefront/cms/footer', label: t('storefront.footer', 'Footer'), icon: '📑' },
        { path: '/storefront/versions', label: t('storefront.versions', 'Version History'), icon: '📜' }
    ]
}
```

**After (commit d68b0b5):** The Storefront section is now:
```js
// Sidebar.jsx:286
localizeStorefrontNode(STOREFRONT_NAVIGATION),
```

The `STOREFRONT_NAVIGATION` is imported from
`PointOfSale.Web/src/modules/storefront/informationArchitecture/index.js` and is the
canonical Storefront information architecture defined by
`STOREFRONT_CANONICAL_UX_BLUEPRINT.md`.

### Impact on Online Store screens

The UX migration **did not affect** the E-commerce/Online Store sidebar section. That section
was already present before the migration and remains unchanged. The "Online Store" screens
that appeared to be "missing" from the sidebar were never part of the Storefront menu —
they were always under the "E-commerce" section.

### Terminology divergence

The canonical documents consistently use "Storefront" to refer to the storefront
administration area (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:6-11`). The term "Online Store"
appears only in:

1. `Settings.jsx:729` — tab label: `t('storefront.onlineStore')` = "Online Store"
2. `StorefrontStatusCard.jsx:149,176` — button/link: `t('storefront.onlineStore', 'Online Store')`
3. Design docs (`docs/reports/LAUNCH-NOTES.md:103`,
   `docs/reports/PHASE-7.10-STOREFRONT-BRIDGE.md:31`) — user-facing instructions:
   "POS → Settings → Online Store"

The sidebar itself never used the term "Online Store" for any section. The sidebar uses:
- "Storefront" for the canonical storefront IA section
- "E-commerce" (`t('nav.ecommerce')`) for the section containing orders, coupons,
  promotions, payments, etc.

### Discovery

The UX migration is **not** the root cause of Online Store screen disappearance. The
Online Store screens were never hidden by the migration — they remain under the "E-commerce"
sidebar section. The perceived disappearance is caused by:

1. **Capability filtering** removing the E-commerce section when the user lacks
   `commercehub.*` claims
2. **Terminology mismatch** causing users to look for "Online Store" but find
   "E-commerce" (or vice versa)

---

## 14. Root Cause Analysis

### Why Online Store screens "disappear" from the sidebar

**Root cause:** Capability-based sidebar filtering, not code removal or route deletion.

The `Sidebar.jsx:356-375` `filterMenuNode` function evaluates each sidebar child against
the current user's `commercehub.*` navigation capabilities via
`canAccessCommerceHubVendorNavigationCapability(user, capability)`
(`posVendorSessionAuthority.js:204-210`). When a user lacks the required capability:

1. Each child node of the "E-commerce" section is removed via `return null`
2. When `children.length === 0` (`Sidebar.jsx:371`), the parent "E-commerce" section
   is also removed
3. The entire section collapses and is invisible in the sidebar

This is by design: the sidebar is a **convenience navigation aid**, not an authorization
boundary. The route-level guards (`CommerceHubVendorRouteGuard` wrapping in `App.jsx`)
enforce actual access control — a user navigating directly to `/ecommerce/orders` will be
redirected to `/` if they lack `commercehub.orders.view`.

### Secondary cause: Terminology mismatch

The term "Online Store" exists in the i18n dictionary (`storefront.onlineStore`,
`en.json:1098`) and is used in design documents, but the sidebar section is labeled
"E-commerce" (`nav.ecommerce`, `en.json:78`). Users expecting to find "Online Store" in
the sidebar will not find it because the label is "E-commerce." This creates a perception
that screens are "missing" when they are merely labeled differently.

### Tertiary cause: Operational Storefront Status buried in Settings

`StorefrontSettings.jsx` (the operational status editor) is not on the Storefront sidebar.
It is rendered as a tab inside `Settings.jsx` labeled "Online Store"
(`Settings.jsx:728-729`), gated behind `canManageStorefrontSettings`
(`Settings.jsx:719`, `Settings.jsx:750`). This was identified as finding F-02 in
`STOREFRONT_UX_ARCHITECTURE_AUDIT.md:828`.

### What did NOT happen

- No component file was deleted or is dead code
- No route was removed from `App.jsx`
- No sidebar registration was deleted (the E-commerce section still exists at `Sidebar.jsx:224-254`)
- No feature flag hid any screen
- No permission was stripped from the codebase
- The UX migration (`d68b0b5`) only affected the Storefront section, not the E-commerce/Online Store section

---

## 15. Canonical Online Store Menu Proposal

### 15.1 Current state (as audited)

```
Sidebar (Sidebar.jsx)
  ├─ [10] E-commerce  (label: t('nav.ecommerce') = "E-commerce")
  │    ├─ Orders              → /ecommerce/orders           (commercehub.orders.view)
  │    ├─ Blocked Phones      → /ecommerce/blocked-phones    (commercehub.orders.view)
  │    ├─ WhatsApp Settings   → /ecommerce/whatsapp          (commercehub.orders.view)
  │    ├─ Smart Cart Recovery → /ecommerce/cart-recovery     (commercehub.orders.view)
  │    ├─ Abandoned Checkouts → /ecommerce/abandoned-checkouts (commercehub.orders.view)
  │    ├─ Shipping            → /admin/shipping/setup        (Owner/Admin/Manager)
  │    ├─ Online Customers    → /ecommerce/online-customers  (commercehub.orders.view)
  │    ├─ Auth Providers      → /ecommerce/auth-providers    (commercehub.storefront.manage)
  │    ├─ Payment Methods     → /ecommerce/payment-methods    (commercehub.payments.view)
  │    ├─ Manual Payments     → /ecommerce/manual-payments    (commercehub.payments.view)
  │    ├─ Coupons             → /ecommerce/coupons           (commercehub.storefront.manage)
  │    ├─ Promotions          → /ecommerce/promotions        (commercehub.storefront.manage)
  │    ├─ Smart Offers        → /ecommerce/smart-offers      (commercehub.storefront.manage)
  │    ├─ AI Assistant        → /ecommerce/commerce-ai       (commercehub.storefront.manage)
  │    ├─ Signal Hardening    → /ecommerce/pixel-tracking    (commercehub.analytics.view)
  │    ├─ Conversion Funnels  → /analytics/funnels           (commercehub.analytics.view)
  │    ├─ Marketing Attribution → /analytics/attribution     (commercehub.analytics.view)
  │    ├─ Campaign Costs      → /analytics/attribution/costs  (commercehub.analytics.view)
  │    ├─ Marketing ROI       → /analytics/attribution/roi   (commercehub.analytics.view)
  │    └─ Analytics Providers → /settings/analytics          (commercehub.analytics.view)
  │
  └─ [14] Storefront (canonical, from STOREFRONT_NAVIGATION)
       ├─ Overview          → /storefront
       ├─ Storefront Status → /storefront/status  (present in routes, not in sidebar)
       ├─ Homepage          → /storefront/homepage
       ├─ Catalog > Product Template → /storefront/catalog/product-template
       ├─ Content > Pages   → /storefront/content/pages
       ├─ Navigation        → /storefront/navigation
       ├─ Footer Content    → /storefront/footer-content
       ├─ Appearance > Themes → /storefront/appearance/themes
       ├─ Appearance > Colors & Typography → /storefront/appearance/colors-typography
       └─ StoreConfig Version History → /storefront/versions
```

### 15.2 Proposed canonical Online Store menu

The canonical UX blueprint defines the Storefront menu hierarchy at
`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:712-722`:

```
Storefront
├─ Overview
├─ Storefront Status
├─ Homepage
├─ Catalog
│  └─ Product Template
├─ Content
│  └─ Pages
├─ Navigation
│  ├─ Header
│  └─ Footer Links
├─ Footer Content
├─ Appearance
│  ├─ Themes
│  └─ Colors & Typography
└─ StoreConfig Version History
```

The "Online Store" screens (Orders, Coupons, Promotions, etc.) are **not** part of this
canonical Storefront hierarchy. They belong to the separate CommerceHub vendor
navigation domain (`posVendorSessionAuthority.js:56-68`), which is an adjacent authority
as stated in the blueprint: "Marketplace and global Super Admin authoring are adjacent
authorities, not children of the tenant Storefront menu"
(`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:21`).

### 15.3 Recommendation for terminology alignment (documentation only)

If the intent is for the sidebar section currently labeled "E-commerce" to be discoverable
as "Online Store," the label should be updated from `t('nav.ecommerce')` to
`t('storefront.onlineStore')` (or a dedicated `nav.onlineStore` key). This is a
documentation-only recommendation; no source code was changed.

---

## 16. Recovery Recommendations

> **DOCUMENTATION ONLY** — No implementation is authorized or performed by this audit.

### 16.1 Immediate (discoverability fixes)

| Recommendation | Files Affected | Risk |
|---|---|---|
| Rename the E-commerce sidebar section label from "E-commerce" to "Online Store" to match canonical terminology | `Sidebar.jsx:227`, `en.json:78` (add `onlineStore` key) | Low |
| Add a direct sidebar link to Storefront Settings (`/storefront/status`) within the Storefront section | `storefrontInformationArchitecture.js`, `Sidebar.jsx` | Low |
| Register StorefrontAnalyticsDashboard as a route and add it to the sidebar or dashboard navigation | `App.jsx`, `Dashboard.jsx` | Low |

### 16.2 Root cause fix (capability model)

| Recommendation | Files Affected | Risk |
|---|---|---|
| If users are expected to see Online Store screens without CommerceHub vendor claims, add a POS-role-based fallback capability mapping so that Owner/Admin users always see the E-commerce section | `Sidebar.jsx` (`filterMenuNode`), `posVendorSessionAuthority.js` | Medium — must not weaken CommerceHub security boundary |
| Document the capability-to-screen mapping so operators know which `commercehub.*` claims are required for each screen | Internal docs | Low |

### 16.3 What NOT to do

- Do NOT delete or dead-code any Online Store component — all 15 are actively used
- Do NOT remove any Online Store route from `App.jsx` — all are registered and functional
- Do NOT strip CommerceHub capability gates — they are the intended authorization model
- Do NOT move Online Store screens into the canonical Storefront navigation — they belong
  to the CommerceHub vendor domain per `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:21`

---

## 17. Risks

| Risk ID | Risk | Likelihood | Impact | Rating | Source-backed reason |
|---|---|---|---|---|---|
| R-01 | Users report Online Store screens as "missing" due to terminology confusion | High | Medium | High | Sidebar says "E-commerce", docs say "Online Store" |
| R-02 | Users without CommerceHub capabilities cannot see Online Store screens | High | High | Critical | `filterMenuNode` removes entire E-commerce section when all children are capability-filtered |
| R-03 | Direct URL access to Online Store screens bypasses sidebar visibility issue | High | Low | Medium | Routes remain active even when sidebar is filtered; routes are independently guarded |
| R-04 | StorefrontSettings (operational status) is buried in Settings tab | Medium | Medium | Medium | Hidden behind `canManageStorefrontSettings` and in a non-obvious tab; documented as F-02 in STOREFRONT_UX_ARCHITECTURE_AUDIT.md |
| R-05 | StorefrontAnalyticsDashboard has no standalone route | Low | Low | Low | Only accessible via Dashboard tab; no sidebar entry |
| R-06 | ShippingMethods is mislinked in sidebar | Low | Low | Low | Sidebar links to `/admin/shipping/setup` while component is imported at `/ecommerce/shipping-methods` route |

---

## 18. Evidence

All evidence below is drawn from the repository at commit `d68b0b5` (HEAD) on branch `main`.

### 18.1 Component files verified to exist

```
PointOfSale.Web/src/pages/StorefrontDashboard.jsx         (263 lines)
PointOfSale.Web/src/pages/StorefrontSettings.jsx          (465 lines)
PointOfSale.Web/src/pages/Orders.jsx                      (929 lines)
PointOfSale.Web/src/pages/Coupons.jsx                     (28,106 chars)
PointOfSale.Web/src/pages/Promotions.jsx                  (24,031 chars)
PointOfSale.Web/src/pages/SmartOffersPage.jsx             (27,097 chars)
PointOfSale.Web/src/pages/CommerceAI.jsx                  (24,859 chars)
PointOfSale.Web/src/pages/PaymentMethods.jsx              (26,361 chars)
PointOfSale.Web/src/pages/ManualPayments.jsx              (23,962 chars)
PointOfSale.Web/src/pages/ShippingMethods.jsx             (366 chars)
PointOfSale.Web/src/pages/AbandonedCheckouts.jsx          (32,953 chars)
PointOfSale.Web/src/pages/AbandonedCartRecovery.jsx       (27,839 chars)
PointOfSale.Web/src/pages/BlockedPhones.jsx               (12,199 chars)
PointOfSale.Web/src/pages/AnalyticsSettings.jsx           (26,734 chars)
PointOfSale.Web/src/pages/StorefrontAnalyticsDashboard.jsx (16,852 chars)
```

### 18.2 App.jsx route registrations

```
// App.jsx:340  — Storefront Status
<Route path="storefront/status" element={<StorefrontSettings />} />

// App.jsx:338-339 — Storefront Dashboard
<Route path="storefront" element={<StorefrontDashboard />} />
<Route path="storefront/overview" element={<StorefrontDashboard />} />

// App.jsx:298 — Shipping Methods
<Route element={<PosRoleRouteGuard roles={['Owner', 'Admin', 'Manager']} />}>
    <Route path="ecommerce/shipping-methods" element={<ShippingMethods />} />
</Route>

// App.jsx:300-302 — Payment Methods / Manual Payments
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.payments.view" />}>
    <Route path="ecommerce/payment-methods" element={<PaymentMethods />} />
    <Route path="ecommerce/manual-payments" element={<ManualPayments />} />
</Route>

// App.jsx:304-305 — Coupons
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.storefront.manage" />}>
    <Route path="ecommerce/coupons" element={<Coupons />} />
</Route>

// App.jsx:308-310 — Promotions, Smart Offers
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.storefront.manage" />}>
    <Route path="ecommerce/promotions" element={<Promotions />} />
    <Route path="ecommerce/smart-offers" element={<SmartOffersPage />} />
</Route>

// App.jsx:312-314 — Orders
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.orders.view" />}>
    <Route path="ecommerce/orders" element={<Orders />} />
    <Route path="ecommerce/orders/:orderId" element={<Orders />} />
</Route>

// App.jsx:316-317 — Blocked Phones
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.orders.view" />}>
    <Route path="ecommerce/blocked-phones" element={<BlockedPhones />} />
</Route>

// App.jsx:320-321 — Online Customers
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.orders.view" />}>
    <Route path="ecommerce/online-customers" element={<OnlineCustomersPage />} />
</Route>

// App.jsx:324-325 — Commerce AI
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.storefront.manage" />}>
    <Route path="ecommerce/commerce-ai" element={<CommerceAI />} />
</Route>

// App.jsx:328-329 — Pixel Tracking
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.analytics.view" />}>
    <Route path="ecommerce/pixel-tracking" element={<PixelTrackingConfigPage />} />
</Route>

// App.jsx:360-361 — Auth Providers
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.storefront.manage" />}>
    <Route path="ecommerce/auth-providers" element={<AuthProvidersPage />} />
</Route>

// App.jsx:364-366 — Analytics Funnels
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.analytics.view" />}>
    <Route path="analytics/funnels" element={<FunnelsListPage />} />
    <Route path="analytics/funnels/:funnelKey" element={<FunnelDetailsPage />} />
</Route>

// App.jsx:369-371 — Analytics Attribution
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.analytics.view" />}>
    <Route path="analytics/attribution" element={<AttributionOverviewPage />} />
    <Route path="analytics/attribution/models" element={<AttributionModelsPage />} />
    <Route path="analytics/attribution/orders" element={<AttributionOrdersPage />} />
</Route>

// App.jsx:375-377 — Campaign Costs, ROI
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.analytics.view" />}>
    <Route path="analytics/attribution/costs" element={<CampaignCostsPage />} />
    <Route path="analytics/attribution/roi" element={<RoiDashboardPage />} />
</Route>

// App.jsx:380-381 — Analytics Settings
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.analytics.view" />}>
    <Route path="settings/analytics" element={<AnalyticsSettings />} />
</Route>

// App.jsx:395-396 — WhatsApp Settings
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.orders.view" />}>
    <Route path="ecommerce/whatsapp" element={<WhatsAppSettings />} />
</Route>

// App.jsx:399-400 — Abandoned Cart Recovery
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.orders.view" />}>
    <Route path="ecommerce/cart-recovery" element={<AbandonedCartRecovery />} />
</Route>

// App.jsx:403-404 — Abandoned Checkouts
<Route element={<CommerceHubVendorRouteGuard capability="commercehub.orders.view" />}>
    <Route path="ecommerce/abandoned-checkouts" element={<AbandonedCheckouts />} />
</Route>
```

### 18.3 Sidebar.jsx E-commerce section

```
// Sidebar.jsx:224-254 — E-commerce section
{
    key: 'ecommerce',
    label: t('nav.ecommerce'),                    // "E-commerce" (en.json:78)
    icon: '🛒',
    children: [
        { path: '/ecommerce/orders', ... capability: 'commercehub.orders.view' },
        { path: '/ecommerce/blocked-phones', ... },
        { path: '/ecommerce/whatsapp', ... },
        { path: '/ecommerce/cart-recovery', ... },
        { path: '/ecommerce/abandoned-checkouts', ... },
        { path: '/admin/shipping/setup', ... requiredRoles: ['Owner', 'Admin', 'Manager'] },
        { path: '/ecommerce/online-customers', ... },
        { path: '/ecommerce/auth-providers', ... capability: 'commercehub.storefront.manage' },
        { path: '/ecommerce/payment-methods', ... capability: 'commercehub.payments.view' },
        { path: '/ecommerce/manual-payments', ... capability: 'commercehub.payments.view' },
        { path: '/ecommerce/coupons', ... },
        { path: '/ecommerce/promotions', ... },
        { path: '/ecommerce/smart-offers', ... },
        { path: '/ecommerce/commerce-ai', ... },
        { path: '/ecommerce/pixel-tracking', ... capability: 'commercehub.analytics.view' },
        { path: '/analytics/funnels', ... capability: 'commercehub.analytics.view' },
        { path: '/analytics/attribution', ... capability: 'commercehub.analytics.view' },
        { path: '/analytics/attribution/costs', ... capability: 'commercehub.analytics.view' },
        { path: '/analytics/attribution/roi', ... capability: 'commercehub.analytics.view' },
        { path: '/settings/analytics', ... capability: 'commercehub.analytics.view' }
    ]
}
```

### 18.4 Capability filter logic

```
// Sidebar.jsx:33-42 — Capability map for implicit path-to-capability matching
const STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH = new Map([
    ['/ecommerce/blocked-phones', 'commercehub.orders.view'],
    ['/ecommerce/whatsapp', 'commercehub.orders.view'],
    ['/ecommerce/cart-recovery', 'commercehub.orders.view'],
    ['/ecommerce/abandoned-checkouts', 'commercehub.orders.view'],
    ['/ecommerce/online-customers', 'commercehub.orders.view'],
    ['/ecommerce/coupons', 'commercehub.storefront.manage'],
    ['/ecommerce/promotions', 'commercehub.storefront.manage'],
    ['/ecommerce/smart-offers', 'commercehub.storefront.manage'],
    ['/ecommerce/commerce-ai', 'commercehub.storefront.manage'],
])

// Sidebar.jsx:356-375 — Filter function that collapses sections
const filterMenuNode = (item) => {
    if (item.requiresMarketplaceActor && !isServiceMarketplaceWorkflowActor(user, true)) return null;
    if (item.requiredRoles?.length && !item.requiredRoles.includes(userRole)) return null;
    const capability = item.capability || STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH.get(item.path);
    if (capability && !canAccessCommerceHubVendorNavigationCapability(user, capability)) return null;
    const children = item.children?.map(filterMenuNode).filter(Boolean);
    if (item.children && children.length === 0) return null;  // PARENT COLLAPSES
    return { ...item, children };
}
```

### 18.5 i18n keys

```
// en.json:78 — Sidebar section label
"ecommerce": "E-commerce"

// en.json:1098 — Used in Settings.jsx tab and StorefrontStatusCard.jsx
"onlineStore": "Online Store"
```

### 18.6 Settings.jsx "Online Store" tab

```
// Settings.jsx:719-731 — Tab button
{canManageStorefront && (
    <button onClick={() => setActiveTab('storefront')} ...>
        <span>WEB</span>
        <span>{t('storefront.onlineStore')}</span>
    </button>
)}

// Settings.jsx:750-754 — Tab content
{activeTab === 'storefront' && canManageStorefront && (
    <div className="max-w-4xl">
        <StorefrontSettings onDirtyChange={setStorefrontStatusDirty} />
    </div>
)}
```

---

## 19. Source Citations

| File | Line(s) | Relevance |
|---|---|---|
| `PointOfSale.Web/src/App.jsx` | 84, 85, 298, 300-302, 304-305, 308-310, 312-314, 316-317, 320-321, 324-325, 328-329, 332-342, 360-361, 364-366, 369-371, 375-377, 380-381, 395-396, 399-400, 403-404 | All Online Store route registrations and guards |
| `PointOfSale.Web/src/components/Sidebar.jsx` | 24-42, 224-254, 286, 353-378 | E-commerce section registration and capability filtering |
| `PointOfSale.Web/src/auth/posVendorSessionAuthority.js` | 4-32, 56-68, 201-210, 242-292 | CommerceHub vendor endpoint list, capabilities, and authorization |
| `PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.jsx` | 1-13 | Route guard implementation |
| `PointOfSale.Web/src/pages/Settings.jsx` | 7, 719-731, 750-754 | "Online Store" tab and StorefrontSettings rendering |
| `PointOfSale.Web/src/pages/StorefrontSettings.jsx` | 1-465 | Operational Storefront Status editor component |
| `PointOfSale.Web/src/pages/StorefrontDashboard.jsx` | 1-263 | Storefront dashboard/overview component |
| `PointOfSale.Web/src/pages/StorefrontAnalyticsDashboard.jsx` | 1-65 | Storefront analytics component (Dashboard tab only) |
| `PointOfSale.Web/src/pages/Dashboard.jsx` | 4, 9-12, 56-58 | Dashboard component rendering StorefrontAnalyticsDashboard |
| `PointOfSale.Web/src/components/StorefrontStatusCard.jsx` | 125-131, 149, 176 | "Open Storefront Settings" link using `storefront.onlineStore` label |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/index.js` | Complete file | STOREFRONT_NAVIGATION export |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js` | Complete file | Canonical Storefront IA nodes |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/StorefrontAdminFrame.jsx` | Complete file | Storefront admin frame wrapper |
| `PointOfSale.Web/src/i18n/locales/en.json` | 78, 1056-1098 | `nav.ecommerce` and `storefront.onlineStore` keys |
| `PointOfSale.Web/src/i18n/locales/ar.json` | 78, 1098 | Arabic translations of same keys |
| `docs/STOREFRONT_CANONICAL_UX_BLUEPRINT.md` | 6-21, 202-223, 708-731 | Canonical Storefront menu hierarchy and naming |
| `docs/STOREFRONT_UX_ARCHITECTURE_AUDIT.md` | 16-17, 77-93, 129-146, 510-518, 828, 843-851 | Current Storefront menu/routes and F-02 finding |
| `docs/FINAL_STOREFRONT_ARCHITECTURE_CERTIFICATION.md` | 5-21, 54-75 | Final Storefront architecture certification |
| `docs/WAVE3_AUTHORING_PLATFORM_EXTRACTION.md` | 1-15, 273-281 | Authoring platform extraction and certification |
| `docs/reports/PHASE-7.10-STOREFRONT-BRIDGE.md` | 31, 54, 102, 165 | "Online Store" tab design notes |
| `docs/reports/LAUNCH-NOTES.md` | 103, 116 | Launch instructions referencing "Online Store" |
| `docs/reports/PHASE-8.0-TENANT-REGISTRATION-DESIGN.md` | 413 | "Online Store" reference in onboarding flow |
| `docs/reports/PHASE-8.1-TENANT-REGISTRATION-UX-DESIGN.md` | 605, 851, 854 | "Online Store" reference in onboarding UX |
| Git log | d68b0b5, 2984cce, 3fb3d5c | Commit history of Sidebar.jsx and Storefront IA |

---

## 20. Summary of Findings

| Finding | Severity | Screens Affected |
|---|---|---|
| F-01: Sidebar labels the Online Store section "E-commerce" while canonical docs say "Online Store" | High | All 15 Online Store screens |
| F-02: Operational Storefront Status (StorefrontSettings.jsx) is hidden inside Settings tab, not on Storefront sidebar | High | StorefrontSettings |
| F-03: Capability-gated sidebar filtering removes entire E-commerce section when user lacks `commercehub.*` claims | Critical | All Online Store screens except Shipping |
| F-04: StorefrontAnalyticsDashboard has no standalone route or sidebar entry | Low | StorefrontAnalyticsDashboard |
| F-05: ShippingMethods.jsx is a 366-char stub; sidebar links to a different route (`/admin/shipping/setup`) | Low | ShippingMethods |
| F-06: StorefrontDashboard is mislabeled as "Settings" in the canonical Storefront IA | Medium | StorefrontDashboard |
| F-07: AbandonedCartRecovery is mislabeled as "Smart Cart Recovery" in the sidebar | Low | AbandonedCartRecovery |

**Root cause of Online Store screen disappearance: F-01 + F-03.** Users either lack the
CommerceHub vendor capabilities to see the section (F-03), or they look for "Online Store"
but find "E-commerce" (F-01). No screen was deleted.

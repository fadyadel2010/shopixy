# Online Store Workspace Certification

**Project:** PointOfSale / Shopixy  
**Workspace:** POS Admin — Online Store (sidebar section labeled "E-commerce")  
**Certifying body:** Shopixy Architecture Program  
**Audit date:** 2026-08-05  
**Mode:** Source audit only — forensic investigation (implementation-free)  
**Application source changed:** No  
**Routes moved:** No  
**Permissions changed:** No  
**Menus added/restored:** No  

---

## 1. Executive Summary

This certification audits the current POS Admin **Online Store** workspace (the sidebar section currently labeled "E-commerce") against the canonical workspace architecture principles defined in the Shopixy architecture program. The audit is strictly forensic: no source files were modified, no routes were moved, no menus were added or restored, and no permissions were changed.

The Online Store workspace is a **CommerceHub vendor capability-gated navigation domain**. It is not part of the canonical Storefront administration hierarchy (which is rooted at `/storefront` and owned by the `STOREFRONT_NAVIGATION` information architecture). The Online Store screens live under CommerceHub vendor authorization (`CommerceHubVendorRouteGuard` + `canAccessCommerceHubVendorNavigationCapability`) and are surfaced in the POS Admin sidebar under the key `ecommerce`, labeled `t('nav.ecommerce')` = "E-commerce."

The certification reveals that the Online Store workspace has **no coherent single product identity in the UI surface**. The sidebar section is labeled "E-commerce" while canonical documents and design artifacts refer to the same surface as "Online Store." This terminology divergence, combined with capability-based sidebar filtering that can collapse the entire section when the user lacks `commercehub.*` claims, produces the perception that Online Store screens "disappear." Additionally, a cross-product leak exists: the `/ecommerce/orders` route is registered in both the Online Store (E-commerce) sidebar section and the CommerceHub admin sidebar section.

The workspace's screens are all present, routed, and reachable when capabilities are granted, but the information architecture fails certification due to identity ambiguity, cross-product leakage, and hidden/unreachable screens.

---

## 2. Certification Verdict

**FAIL**

### Verdict Rationale

The Online Store workspace fails certification because:

1. **Product Identity ambiguity:** The sidebar section is labeled "E-commerce" (`nav.ecommerce`, `en.json:78`) while canonical design documents and the i18n dictionary use "Online Store" (`storefront.onlineStore`, `en.json:1098`). This is an active identity conflict, not a minor labeling inconsistency.

2. **Cross-product boundary violation:** The `/ecommerce/orders` route is registered as a sidebar node in BOTH the Online Store ("E-commerce") section (`Sidebar.jsx:230`) AND the CommerceHub Admin section (`Sidebar.jsx:270`). This is a direct boundary breach — the same screen is claimed by two product workspaces.

3. **Capability filtering collapses the entire section:** The `filterMenuNode` function (`Sidebar.jsx:356-375`) removes any sidebar child whose capability is not granted. When all children of the "E-commerce" section are filtered out (line 371: `children.length === 0` → `return null`), the entire Online Store section disappears with no fallback or placeholder.

4. **Hidden screens:** `StorefrontSettings.jsx` (operational Storefront Status editor) is hidden inside the global `Settings.jsx` tab (`Settings.jsx:719-731, 750-754`), not directly reachable from the Online Store sidebar. `StorefrontAnalyticsDashboard.jsx` is only rendered inside `Dashboard.jsx` as a tab (`Dashboard.jsx:4, 58`), with no standalone route or sidebar entry.

5. **Mislinked screen:** The `ShippingMethods.jsx` stub (5 lines, 237 chars — `PointOfSale.Web/src/pages/ShippingMethods.jsx:1-5`) renders `ShippingAdministrationWorkspace`, but the sidebar link points to `/admin/shipping/setup` (`Sidebar.jsx:238`), which is a different route rendering the same workspace component (`App.jsx:385-386`). The `/ecommerce/shipping-methods` route (`App.jsx:298`) is registered but has no sidebar entry.

### Per-criterion verdict

| Criterion | Result |
|---|---|
| 1. Product Identity | FAIL — "E-commerce" vs "Online Store" conflict |
| 2. Workspace Ownership | FAIL — `/ecommerce/orders` shared between E-commerce and CommerceHub |
| 3. Information Architecture | FAIL — hidden screens, no canonical grouping |
| 4. Navigation | FAIL — duplicate entry points, mislinked routes, orphan tabs |
| 5. Screen Inventory | PASS — all 16 components exist on disk |
| 6. Workspace Boundaries | FAIL — cross-product leak of Orders screen |
| 7. Workspace Consistency | FAIL — mixed "E-commerce"/"Online Store" terminology, scope inconsistency |
| 8. Discoverability | FAIL — 3 screens hidden or undiscoverable without direct URL |
| 9. Shopify Parity | FAIL — no coherent workspace organization to compare |
| 10. Technical Ownership | PASS — routing, sidebar, IA are all in source-backed files |

---

## 3. Current Workspace Map

### 3.1 Sidebar structure (as audited)

Source: `PointOfSale.Web/src/components/Sidebar.jsx:114-351`

```
POS Admin Sidebar (menuItems array)
├─ [1]  Dashboard              → / (Dashboard.jsx)
├─ [2]  Database               → customers, suppliers, categories, units, warehouses, banks, products
├─ [3]  Accounts               → financial-accounts, balance-transfer
├─ [4]  Opening Balance         → cashbox, customers, suppliers, warehouse
├─ [5]  Transactions            → pos, sales-returns, purchase-invoice, purchase-returns
├─ [6]  Treasury                → treasury, payments, adjustments, outstanding-balances
├─ [7]  Inventory               → inventory, stock-transfers, barcode-printing
├─ [8]  Expenses                → expense-categories, expenses, reports/expenses
├─ [9]  Account Statement        → statement-of-account, reports/bank-statement, reports/visa-statement
├─ [10] E-commerce              → 17 children (Online Store screens) — capability-gated
├─ [11] Services Marketplace     → /marketplace (requiresMarketplaceActor)
├─ [12] CommerceHub              → 5 children (admin/commercehub, /ecommerce/orders, finance, earnings, payments)
├─ [13] Affiliate Management    → /admin/affiliates (Owner/Admin only)
├─ [14] Storefront (canonical)  → localizeStorefrontNode(STOREFRONT_NAVIGATION) — 11 nodes
│   ├─ Overview
│   ├─ Storefront Status
│   ├─ Homepage
│   ├─ Catalog > Product Template
│   ├─ Content > Pages
│   ├─ Navigation > Header / Footer Links
│   ├─ Footer Content
│   ├─ Appearance > Themes / Colors & Typography
│   └─ StoreConfig Version History
├─ [15] Reports
├─ [16] Finance Control Center  → treasury/* (role-gated)
├─ [17] Users                  → /settings/audit-log (Admin/SuperAdmin only)
└─ [18] Settings               → /settings (contains "Online Store" tab)
```

### 3.2 E-commerce section detail (the "Online Store" workspace)

Source: `Sidebar.jsx:224-254`

```
E-commerce (key: 'ecommerce', label: t('nav.ecommerce') = "E-commerce", icon: '🛒')
├─ Orders                  → /ecommerce/orders           (capability: commercehub.orders.view)
├─ Blocked Phones          → /ecommerce/blocked-phones   (implicit: commercehub.orders.view)
├─ WhatsApp Settings       → /ecommerce/whatsapp         (implicit: commercehub.orders.view)
├─ Smart Cart Recovery     → /ecommerce/cart-recovery    (implicit: commercehub.orders.view)
├─ Abandoned Checkouts     → /ecommerce/abandoned-checkouts (implicit: commercehub.orders.view)
├─ Shipping                → /admin/shipping/setup       (requiredRoles: Owner/Admin/Manager)
├─ Online Customers        → /ecommerce/online-customers  (implicit: commercehub.orders.view)
├─ Auth Providers          → /ecommerce/auth-providers    (capability: commercehub.storefront.manage)
├─ Payment Methods         → /ecommerce/payment-methods    (capability: commercehub.payments.view)
├─ Manual Payments         → /ecommerce/manual-payments    (capability: commercehub.payments.view)
├─ Coupons                 → /ecommerce/coupons           (implicit: commercehub.storefront.manage)
├─ Promotions              → /ecommerce/promotions        (implicit: commercehub.storefront.manage)
├─ Smart Offers            → /ecommerce/smart-offers       (implicit: commercehub.storefront.manage)
├─ AI Assistant            → /ecommerce/commerce-ai        (implicit: commercehub.storefront.manage)
├─ Signal Hardening        → /ecommerce/pixel-tracking    (capability: commercehub.analytics.view)
├─ Conversion Funnels      → /analytics/funnels            (capability: commercehub.analytics.view)
├─ Marketing Attribution   → /analytics/attribution        (capability: commercehub.analytics.view)
├─ Campaign Costs          → /analytics/attribution/costs  (capability: commercehub.analytics.view)
├─ Marketing ROI           → /analytics/attribution/roi   (capability: commercehub.analytics.view)
└─ Analytics Providers     → /settings/analytics           (capability: commercehub.analytics.view)
```

### 3.3 CommerceHub admin section

Source: `Sidebar.jsx:263-275`

```
CommerceHub (key: 'commercehub', label: t('nav.commerceHub', 'CommerceHub'), icon: '🏪')
├─ Publishing Settings      → /admin/commercehub            (capability: commercehub.catalog.manage)
├─ Orders                   → /ecommerce/orders             (capability: commercehub.orders.view)  ← DUPLICATE
├─ Finance Dashboard        → /commercehub/finance          (capability: commercehub.finance.view)
├─ Vendor Earnings          → /commercehub/earnings         (capability: commercehub.finance.view)
└─ Debt Repayment           → /commercehub/payments         (capability: commercehub.payments.view)
```

---

## 4. Canonical Workspace Map

### 4.1 Canonical Online Store hierarchy (per STOREFRONT_CANONICAL_UX_BLUEPRINT.md:181-198)

The canonical blueprint defines the **Storefront** hierarchy (not "Online Store"). The blueprint explicitly states that Marketplace and global Super Admin authoring are "adjacent authorities, not children of the tenant Storefront menu" (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:21`).

The canonical Storefront sidebar:

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

### 4.2 Canonical Online Store hierarchy (proposed)

The Online Store is a distinct CommerceHub vendor capability surface. Its canonical form should be:

```
Online Store
├─ Orders
├─ Customers
│  ├─ Online Customers
│  └─ Blocked Phones
├─ Checkout
│  ├─ Abandoned Checkouts
│  └─ Smart Cart Recovery
│  └─ WhatsApp Settings
├─ Payments
│  ├─ Payment Methods
│  └─ Manual Payments
├─ Catalog
│  ├─ Coupons
│  ├─ Promotions
│  └─ Smart Offers
├─ Shipping
├─ Marketing
│  ├─ Signal Hardening (Pixel Tracking)
│  ├─ Conversion Funnels
│  ├─ Marketing Attribution
│  │  ├─ Campaign Costs
│  │  └─ Marketing ROI
│  └─ Analytics Providers
└─ AI Assistant
```

### 4.3 Gap: no canonical Online Store hierarchy exists

There is **no canonical blueprint document** that defines the Online Store workspace hierarchy. The only canonical document (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md`) covers the Storefront workspace, not the Online Store. The Online Store screens are treated as an adjacent CommerceHub vendor domain with no formal IA definition.

---

## 5. Screen Classification Matrix

Source: `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:117-133`, verified against `App.jsx` and component files.

| # | Screen | File Path | Line Count | Primary Domain | Route Path | Sidebar? | Visibility |
|---|---|---|---|---|---|---|---|
| 1 | Orders | `src/pages/Orders.jsx` | 929 | Orders | `/ecommerce/orders`, `/ecommerce/orders/:orderId` | Yes (E-commerce + CommerceHub) | Hidden if no `commercehub.orders.view` |
| 2 | Coupons | `src/pages/Coupons.jsx` | 663 | Catalog | `/ecommerce/coupons` | Yes | Hidden if no `commercehub.storefront.manage` |
| 3 | Promotions | `src/pages/Promotions.jsx` | 920 | Marketing | `/ecommerce/promotions` | Yes | Hidden if no `commercehub.storefront.manage` |
| 4 | Smart Offers | `src/pages/SmartOffersPage.jsx` | 481 | Catalog / AI | `/ecommerce/smart-offers` | Yes | Hidden if no `commercehub.storefront.manage` |
| 5 | Commerce AI | `src/pages/CommerceAI.jsx` | 408 | AI | `/ecommerce/commerce-ai` | Yes | Hidden if no `commercehub.storefront.manage` |
| 6 | Payment Methods | `src/pages/PaymentMethods.jsx` | 691 | Payments | `/ecommerce/payment-methods` | Yes | Hidden if no `commercehub.payments.view` |
| 7 | Manual Payments | `src/pages/ManualPayments.jsx` | 624 | Payments | `/ecommerce/manual-payments` | Yes | Hidden if no `commercehub.payments.view` |
| 8 | Shipping Methods | `src/pages/ShippingMethods.jsx` | 5 | Shipping | `/ecommerce/shipping-methods` | NO — sidebar links to `/admin/shipping/setup` | Mislinked |
| 9 | Abandoned Checkouts | `src/pages/AbandonedCheckouts.jsx` | 521 | Orders | `/ecommerce/abandoned-checkouts` | Yes | Hidden if no `commercehub.orders.view` |
| 10 | Abandoned Cart Recovery | `src/pages/AbandonedCartRecovery.jsx` | 418 | Orders | `/ecommerce/cart-recovery` | Yes | Hidden if no `commercehub.orders.view` |
| 11 | Blocked Phones | `src/pages/BlockedPhones.jsx` | 216 | Customers | `/ecommerce/blocked-phones` | Yes | Hidden if no `commercehub.orders.view` |
| 12 | Online Customers | `modules/onlineCustomers` | — | Customers | `/ecommerce/online-customers` | Yes | Hidden if no `commercehub.orders.view` |
| 13 | Pixel Tracking | `src/modules/storefront/config/PixelTrackingConfigPage` | — | Marketing | `/ecommerce/pixel-tracking` | Yes | Hidden if no `commercehub.analytics.view` |
| 14 | WhatsApp Settings | `src/modules/whatsapp` | — | Orders | `/ecommerce/whatsapp` | Yes | Hidden if no `commercehub.orders.view` |
| 15 | Conversion Funnels | `src/modules/analytics` | — | Marketing | `/analytics/funnels` | Yes | Hidden if no `commercehub.analytics.view` |
| 16 | Marketing Attribution | `src/modules/analytics` | — | Marketing | `/analytics/attribution` | Yes | Hidden if no `commercehub.analytics.view` |
| 17 | Campaign Costs | `src/modules/analytics` | — | Marketing | `/analytics/attribution/costs` | Yes | Hidden if no `commercehub.analytics.view` |
| 18 | Marketing ROI | `src/modules/analytics` | — | Marketing | `/analytics/attribution/roi` | Yes | Hidden if no `commercehub.analytics.view` |
| 19 | Analytics Providers | `src/pages/AnalyticsSettings.jsx` | 1050 | Infrastructure | `/settings/analytics` | Yes | Hidden if no `commercehub.analytics.view` |
| 20 | StorefrontAnalyticsDashboard | `src/pages/StorefrontAnalyticsDashboard.jsx` | 313 | Analytics | No standalone route | NO — only in Dashboard tab | Hidden — undiscoverable |
| 21 | StorefrontSettings | `src/pages/StorefrontSettings.jsx` | 465 | Infrastructure | `/storefront/status` | NO — hidden in Settings tab | Hidden — buried in global Settings |

Classification of Online Store screens by domain:

| Domain | Screens |
|---|---|
| Overview | — (no overview screen; Dashboard.jsx is POS Admin overview, not Online Store) |
| Content | — (no content screens) |
| Catalog | Coupons, Promotions, Smart Offers |
| Orders | Orders, Abandoned Checkouts, Abandoned Cart Recovery, Blocked Phones, Online Customers, WhatsApp Settings |
| Marketing | Pixel Tracking, Conversion Funnels, Marketing Attribution, Campaign Costs, Marketing ROI |
| Customers | Online Customers (could also classify under Orders) |
| Payments | Payment Methods, Manual Payments |
| Shipping | Shipping Methods (stub) |
| Analytics | Analytics Providers (Settings route) |
| AI | Commerce AI |
| Infrastructure | Analytics Settings (Settings route), StorefrontSettings (Status editor) |

| Not Online Store | Count |
|---|---|
| Storefront screens (Overview, Status, Homepage, Product Template, Pages, Navigation, Footer Content, Themes, Colors & Typography, Version History) | 10 |
| Dashboard.jsx (POS overview with storefront tab) | 1 |
| StorefrontAnalyticsDashboard.jsx | 1 |
| StorefrontStatusCard.jsx | 1 |
| POS Admin screens (Database, Accounts, Treasury, Inventory, etc.) | ~25 |

---

## 6. Workspace Ownership Matrix

Source: `Sidebar.jsx:224-275`, `posVendorSessionAuthority.js:56-68`

| Screen/Route | Online Store (E-commerce) Sidebar | CommerceHub Admin Sidebar | Storefront Sidebar | POS Admin (other) | Global Settings |
|---|---|---|---|---|---|
| /ecommerce/orders | YES (`Sidebar.jsx:230`) | YES (`Sidebar.jsx:270`) | No | No | No |
| /ecommerce/blocked-phones | YES | No | No | No | No |
| /ecommerce/whatsapp | YES | No | No | No | No |
| /ecommerce/cart-recovery | YES | No | No | No | No |
| /ecommerce/abandoned-checkouts | YES | No | No | No | No |
| /admin/shipping/setup | YES (links here) | No | No | No | No |
| /ecommerce/shipping-methods | NO (route only) | No | No | No | No |
| /ecommerce/online-customers | YES | No | No | No | No |
| /ecommerce/auth-providers | YES | No | No | No | No |
| /ecommerce/payment-methods | YES | No | No | No | No |
| /ecommerce/manual-payments | YES | No | No | No | No |
| /ecommerce/coupons | YES | No | No | No | No |
| /ecommerce/promotions | YES | No | No | No | No |
| /ecommerce/smart-offers | YES | No | No | No | No |
| /ecommerce/commerce-ai | YES | No | No | No | No |
| /ecommerce/pixel-tracking | YES | No | No | No | No |
| /analytics/funnels | YES | No | No | No | No |
| /analytics/attribution | YES | No | No | No | No |
| /analytics/attribution/costs | YES | No | No | No | No |
| /analytics/attribution/roi | YES | No | No | No | No |
| /settings/analytics | YES | No | No | No | YES (Settings) |
| /storefront/status | NO | No | YES (canonical) | No | YES (via Settings tab) |
| Dashboard (storefront tab) | NO | No | NO (POS Admin only) | YES | No |

**Critical finding:** `/ecommerce/orders` is owned by two sidebars simultaneously.

---

## 7. Workspace Boundary Analysis

### 7.1 Declared workspaces

Source: `Sidebar.jsx:114-351`

| Workspace | Sidebar Section Key | Route Prefix | Authorization | Declared Owner |
|---|---|---|---|---|
| POS Admin Core | dashboard, database, accounts, opening_balance, transactions, treasury, inventory, expenses, account_statement | `/customers`, `/products`, `/pos`, `/treasury`, `/inventory`, etc. | `PosRoleRouteGuard` / `ProtectedRoute` | POS Admin |
| Online Store | ecommerce | `/ecommerce/*`, `/analytics/*`, `/settings/analytics` | `CommerceHubVendorRouteGuard` (capability-gated) | CommerceHub vendor |
| Services Marketplace | marketplace | `/marketplace` | `requiresMarketplaceActor` | Marketplace workflow |
| CommerceHub Admin | commercehub | `/admin/commercehub`, `/commercehub/*`, `/ecommerce/orders` | `CommerceHubVendorRouteGuard` | CommerceHub admin |
| Affiliate Management | admin-affiliates | `/admin/affiliates` | `PosRoleRouteGuard` (Owner/Admin) | POS Admin |
| Storefront | storefront | `/storefront/*` | `PosRoleRouteGuard` (Owner/Admin) | Storefront Admin |
| Reports | reports | `/reports/*`, `/sales-history` | `ProtectedRoute` | POS Admin |
| Finance Control Center | finance_control | `/treasury/*` | Role-gated (Admin/SuperAdmin/Owner/Accountant) | POS Admin |
| Users | users | `/settings/audit-log` | Role-gated (Admin/SuperAdmin) | POS Admin |
| Settings | settings | `/settings` | `ProtectedRoute` | POS Admin |

### 7.2 Boundary violations identified

| Violation | Detail | Source |
|---|---|---|
| **V-01: Orders dual ownership** | `/ecommerce/orders` appears in both the "E-commerce" section (`Sidebar.jsx:230`) and the "CommerceHub" section (`Sidebar.jsx:270`). Both use the same capability gate (`commercehub.orders.view`). This is a direct cross-product leak — the CommerceHub workspace owns the same screen as the Online Store workspace. | `Sidebar.jsx:230,270` |
| **V-02: Analytics routes under Online Store** | Five analytics routes (`/analytics/funnels`, `/analytics/attribution`, `/analytics/attribution/costs`, `/analytics/attribution/roi`, `/settings/analytics`) are registered under the "E-commerce" section in the sidebar (`Sidebar.jsx:248-252`) with `commercehub.analytics.view` capability. These are analytics screens, not e-commerce/Online Store screens, but they are physically grouped under the Online Store navigation tree. | `Sidebar.jsx:248-252` |
| **V-03: Settings routes under Online Store** | `/settings/analytics` is registered as a child of the "E-commerce" section (`Sidebar.jsx:252`) despite being a global Settings route. The `/settings` route itself is a separate top-level sidebar section (`Sidebar.jsx:345-350`), creating ambiguity about whether Analytics Settings belongs to Online Store or global Settings. | `Sidebar.jsx:252, 345-350` |
| **V-04: Shipping mislocation** | The sidebar "Shipping" node (`Sidebar.jsx:238`) points to `/admin/shipping/setup`, which is outside the `/ecommerce` route prefix. The `ShippingMethods.jsx` component at `/ecommerce/shipping-methods` (`App.jsx:298`) is the canonical Online Store shipping route but is not linked from the sidebar. The sidebar link and the route point to different components/URLs for the same conceptual screen. | `Sidebar.jsx:238`, `App.jsx:298,385-386` |
| **V-05: Storefront Settings leaked into Settings** | The operational Storefront Status editor (`StorefrontSettings.jsx`) is registered at `/storefront/status` (canonical, `App.jsx:340`) but is rendered as a tab inside the global `Settings.jsx` (`Settings.jsx:719-731, 750-754`). Users must navigate to Settings to access it, making it a hidden screen from the Online Store perspective. | `App.jsx:340`, `Settings.jsx:719-754` |
| **V-06: StorefrontAnalyticsDashboard orphaned** | `StorefrontAnalyticsDashboard.jsx` is rendered only as the "storefront" tab inside `Dashboard.jsx` (`Dashboard.jsx:4-5, 56-58`). No standalone route exists in `App.jsx`, no sidebar entry exists. | `Dashboard.jsx:4-5, 56-58` |
| **V-07: CommerceHub admin duplicates Online Store order capability** | The CommerceHub admin section (`Sidebar.jsx:263-275`) includes an "Orders" entry (`Sidebar.jsx:270`) that routes to the same `/ecommerce/orders` as the Online Store section, gated by the same `commercehub.orders.view` capability. | `Sidebar.jsx:263-275` |

### 7.3 Workspace independence verification

| Question | Answer | Evidence |
|---|---|---|
| Does the Online Store have its own shell? | No | No `StorefrontAdminFrame` or equivalent wrapper; Online Store screens render directly via `Layout` in `App.jsx:235` |
| Does the Online Store have its own breadcrumbs? | No | Breadcrumbs are only defined for Storefront screens in `StorefrontBreadcrumbs.jsx` (`resolveStorefrontScreen` only handles `/storefront/*` paths) |
| Does the Online Store have its own header? | No | Uses the global `Layout` component (`App.jsx:235, 136`) |
| Does the Online Store have its own sidebar section? | Yes (partially) | The "E-commerce" section (`Sidebar.jsx:224-254`) but shares the global sidebar shell |
| Does the Online Store have its own authorization model? | Yes | `CommerceHubVendorRouteGuard` + `posVendorSessionAuthority.js` capabilities |
| Does another workspace own Online Store screens? | Yes | CommerceHub admin owns `/ecommerce/orders` (`Sidebar.jsx:270`) |

---

## 8. Information Architecture Analysis

### 8.1 Hierarchy

The Online Store workspace has **no formal hierarchical structure**. The "E-commerce" sidebar section (`Sidebar.jsx:224-254`) is a flat list of 17 children with no parent grouping (no collapsible sub-groups beyond the top-level expandable section). Contrast with the Canonical Storefront section (`STOREFRONT_NAVIGATION`) which has a properly nested hierarchy: Catalog > Product Template, Content > Pages, Navigation > Header/Footer Links, Appearance > Themes/Colors & Typography.

The 17 Online Store children are an undifferentiated flat list mixing:
- Orders management (Orders, Blocked Phones, Abandoned Checkouts, Smart Cart Recovery, Online Customers, WhatsApp Settings)
- Payment management (Payment Methods, Manual Payments)
- Catalog/promotion (Coupons, Promotions, Smart Offers, Commerce AI)
- Analytics (Signal Hardening, Conversion Funnels, Marketing Attribution, Campaign Costs, Marketing ROI, Analytics Providers)
- Shipping (mislinked)

### 8.2 Grouping

**No logical grouping exists.** All 17 items are flat children of the "E-commerce" section. There are no sub-groups for Orders, Payments, Catalog, Marketing, or Analytics. The canonical Storefront section properly groups items (Catalog, Content, Navigation, Appearance) but the Online Store does not follow this pattern.

### 8.3 Naming

**Terminology is inconsistent across multiple dimensions:**

| Inconsistency | Sidebar label | Canonical term | Source |
|---|---|---|---|
| Section identity | "E-commerce" | "Online Store" | `Sidebar.jsx:227` vs `en.json:1098` |
| Cart recovery | "Smart Cart Recovery" | "Abandoned Cart Recovery" | `Sidebar.jsx:235` vs `AbandonedCartRecovery.jsx` |
| Promotions | "Offers" | "Promotions" | `Sidebar.jsx:244` (`t('promotions.title', 'Offers')` → `en.json:1390` "Promotions (Offers)") |
| AI Assistant | "AI Assistant" | "Commerce AI" | `Sidebar.jsx:246` vs `CommerceAI.jsx` |
| Shipping | "Shipping" | "Shipping Methods" | `Sidebar.jsx:238` vs `ShippingMethods.jsx` |
| Pixel tracking | "Signal Hardening" | "Pixel Tracking" | `Sidebar.jsx:247` |
| Analytics routes | Under E-commerce | Separate Analytics domain | `Sidebar.jsx:248-252` |

The i18n key `storefront.onlineStore` = "Online Store" (`en.json:1098`) is used only in `Settings.jsx:729` and `StorefrontStatusCard.jsx:149,176` — never in the sidebar. The sidebar uses `nav.ecommerce` = "E-commerce" (`en.json:78`). This is the primary terminology divergence.

### 8.4 Ordering

The Online Store children follow a rough workflow order (Orders → Customers → Payments → Catalog → Analytics) but lack a coherent information architecture. The canonical Storefront ordering follows a task-based progression (Overview → Status → Homepage → Catalog → Content → Navigation → Footer → Appearance → History) that is logical and predictable.

### 8.5 Discoverability

See Section 12 for the detailed Hidden Screen Matrix. Three of 21 screens are not discoverable through the sidebar.

### 8.6 Menu consistency

The Online Store section is the only major workspace that is a flat list without hierarchical sub-groups. All other workspaces (Storefront, Database, Treasury, Reports, Finance Control Center, CommerceHub) use either single-link items or hierarchical children. The Online Store's flat structure is inconsistent with the rest of the sidebar.

### 8.7 Sidebar consistency

The "E-commerce" section uses the same `renderNestedNode` function as all other sections (`Sidebar.jsx:420-463`), so the rendering mechanism is consistent. However, the content structure is inconsistent — it is the only major section without sub-grouping.

### 8.8 Header consistency

The Online Store has no dedicated header — it uses the global `Layout` component (`App.jsx:136,235`). The Storefront workspace, by contrast, has the `StorefrontAdminFrame` (`App.jsx:337`) which wraps routes with `StorefrontBreadcrumbs` and the `AuthoringShell` (`StorefrontAdminFrame.jsx`).

### 8.9 Breadcrumb consistency

Breadcrumbs are only implemented for Storefront screens via `StorefrontBreadcrumbs.jsx` (`resolveStorefrontScreen` only resolves `/storefront/*` paths). Online Store screens have no breadcrumbs — they use the global `Layout` breadcrumbs (`Layout.jsx`).

### 8.10 Workspace boundaries

The Online Store boundary is **not cleanly delimited**. Five analytics routes use the `/analytics/*` prefix (`Sidebar.jsx:248-251`), one uses `/settings/*` (`Sidebar.jsx:252`), one uses `/admin/*` (`Sidebar.jsx:238`), and the rest use `/ecommerce/*`. The canonical Storefront workspace is cleanly delimited at `/storefront/*`.

---

## 9. Navigation Analysis

### 9.1 Sidebar

Source: `Sidebar.jsx:224-275`

The Online Store (E-commerce) section has 17 children, all flat. The CommerceHub section has 5 children. **Orders appears in both sections** (`Sidebar.jsx:230` and `Sidebar.jsx:270`).

### 9.2 Secondary navigation

No secondary navigation exists for the Online Store. The Storefront workspace uses tabs within `Dashboard.jsx` (`Dashboard.jsx:9-12`) for POS vs Storefront analytics, and `Settings.jsx` uses tab navigation (`Settings.jsx:719-754`).

### 9.3 Tabs

Tabs in the Online Store context:
- `Dashboard.jsx:56-58` — POS vs Storefront analytics tab (POS Admin, not Online Store)
- `Settings.jsx:719-754` — Settings tabs including "Online Store" tab for StorefrontSettings
- No tabs exist within any Online Store route

### 9.4 Cross-links

| Source | Target | Type |
|---|---|---|
| `StorefrontDashboard.jsx` (Overview) | `/storefront/homepage`, `/storefront/content/pages`, `/storefront/navigation?view=header`, `/storefront/footer-content`, `/storefront/appearance/themes`, `/storefront/storeconfig-version-history` | Internal deep links (Overview → owning screens) |
| `StorefrontStatusCard.jsx:125-131` | `/storefront/status` | Status card → operational settings |
| `StorefrontStatusCard.jsx:125-131` | Opens live Storefront URL | External |
| `Settings.jsx:719-754` | Renders `StorefrontSettings` as tab | Cross-product link (Settings → Online Store) |
| `Dashboard.jsx:4-5, 56-58` | Renders `StorefrontAnalyticsDashboard` (via tab) | Cross-product link (Dashboard → Storefront) |

### 9.5 Entry points

| Entry point | Route | Source |
|---|---|---|
| Sidebar → E-commerce → Orders | `/ecommerce/orders` | `Sidebar.jsx:230` |
| Sidebar → CommerceHub → Orders | `/ecommerce/orders` | `Sidebar.jsx:270` (duplicate) |
| Sidebar → E-commerce → Shipping | `/admin/shipping/setup` | `Sidebar.jsx:238` (mislinked) |
| Settings tab → StorefrontSettings | `/settings?tab=storefront` | `Settings.jsx:719-731, 750-754` |
| Dashboard tab → StorefrontAnalyticsDashboard | No URL — tab-based | `Dashboard.jsx:11, 56-58` |
| Direct URL → any Online Store route | `/ecommerce/*` | `App.jsx:296-446` |

### 9.6 Hidden entry points

- `/settings?tab=storefront` — StorefrontSettings rendered as Settings tab (`Settings.jsx:750-754`)
- `/storefront/status` — direct URL to StorefrontSettings (`App.jsx:340`)
- `/ecommerce/shipping-methods` — direct URL to ShippingMethods stub (`App.jsx:298`)
- Dashboard "storefront" tab — StorefrontAnalyticsDashboard (`Dashboard.jsx:11, 56-58`)

### 9.7 Duplicate entry points

| Route | Entry 1 | Entry 2 | Type |
|---|---|---|---|
| `/ecommerce/orders` | Sidebar "E-commerce" → Orders (`Sidebar.jsx:230`) | Sidebar "CommerceHub" → Orders (`Sidebar.jsx:270`) | Same route, same component, same capability |

### 9.8 Orphan pages

| Screen | Orphaned? | Reason |
|---|---|---|
| StorefrontAnalyticsDashboard | Yes | No standalone route; only rendered inside Dashboard tab (`Dashboard.jsx:58`) |
| ShippingMethods (`/ecommerce/shipping-methods`) | Partially | Route exists (`App.jsx:298`) but sidebar links to `/admin/shipping/setup` (`Sidebar.jsx:238`) |

### 9.9 Broken journeys

| Journey | Broken? | Detail |
|---|---|---|
| E-commerce → Shipping | Partially | Sidebar "Shipping" links to `/admin/shipping/setup` which is the `ShippingAdministrationWorkspace` (`App.jsx:385-386`), not the `ShippingMethods` component. The `/ecommerce/shipping-methods` route (`App.jsx:298`) renders the `ShippingMethods` stub which also renders `ShippingAdministrationWorkspace` but with `initialTab="shipping-methods"` — two routes, same component, different initial tab |
| E-commerce → Storefront Settings | Broken | No direct link from sidebar; StorefrontSettings must be reached via `/storefront/status` (not in Online Store section) or `/settings?tab=storefront` (global Settings) |
| Navigation editor exit | Documented | `NavigationEditor.jsx:331-340` exits to Pages list instead of Storefront overview (`STOREFRONT_UX_ARCHITECTURE_AUDIT.md:289`) |

### 9.10 Deep links

Deep links verified:
- `/storefront/navigation?view=header` and `/storefront/navigation?view=footer` — canonical deep links (`storefrontInformationArchitecture.js:68, 75`)
- `/settings?tab=storefront` — Settings deep link to StorefrontSettings (`Settings.jsx:729, 750`)

---

## 10. Discoverability Analysis

### 10.1 Clicks to reach every screen (from sidebar expansion)

| Screen | Clicks from sidebar | Via sidebar? | Via direct URL? |
|---|---|---|---|
| Orders | 2 (expand E-commerce → click Orders) | Yes | Yes |
| Coupons | 2 | Yes | Yes |
| Promotions | 2 | Yes | Yes |
| Smart Offers | 2 | Yes | Yes |
| Commerce AI | 2 | Yes | Yes |
| Payment Methods | 2 | Yes | Yes |
| Manual Payments | 2 | Yes | Yes |
| Shipping Setup | 2 | Yes (but links to `/admin/shipping/setup`, not `/ecommerce/shipping-methods`) | Yes (both routes) |
| Abandoned Checkouts | 2 | Yes | Yes |
| Abandoned Cart Recovery | 2 | Yes | Yes |
| Blocked Phones | 2 | Yes | Yes |
| Online Customers | 2 | Yes | Yes |
| WhatsApp Settings | 2 | Yes | Yes |
| Auth Providers | 2 | Yes | Yes |
| Pixel Tracking | 2 | Yes | Yes |
| Conversion Funnels | 2 | Yes | Yes |
| Marketing Attribution | 2 | Yes | Yes |
| Campaign Costs | 3 (expand → Attribution → Campaign Costs) | Yes | Yes |
| Marketing ROI | 3 | Yes | Yes |
| Analytics Providers | 2 | Yes | Yes |
| StorefrontAnalyticsDashboard | 3 (Dashboard → storefront tab) | No | No (no route) |
| StorefrontSettings | 3 (Settings → Online Store tab, or `/storefront/status` via StatusCard) | No | Yes (`/storefront/status` or `/settings?tab=storefront`) |

### 10.2 Hidden screens

Three screens are not discoverable through the Online Store sidebar:
1. **StorefrontSettings** — accessible only via `/settings?tab=storefront` or `/storefront/status` or StorefrontStatusCard link
2. **StorefrontAnalyticsDashboard** — accessible only via Dashboard "storefront" tab; no URL route
3. **ShippingMethods** (`/ecommerce/shipping-methods` route) — sidebar links to `/admin/shipping/setup` instead

### 10.3 Screens requiring knowing URLs

| Screen | URL | Barrier |
|---|---|---|
| StorefrontSettings | `/storefront/status` | Not in Online Store sidebar; canonical Storefront route |
| StorefrontSettings | `/settings?tab=storefront` | Not a standalone route; buried in Settings tab |
| ShippingMethods | `/ecommerce/shipping-methods` | Sidebar links to `/admin/shipping/setup` instead |
| StorefrontAnalyticsDashboard | (none) | No route; only accessible via Dashboard tab |
| StorefrontAnalyticsDashboard | (none) | No URL possible — rendered inline as tab child |

### 10.4 Screens existing but practically undiscoverable

| Screen | Component | Barrier |
|---|---|---|
| StorefrontAnalyticsDashboard | `StorefrontAnalyticsDashboard.jsx` (313 lines) | Only rendered inside `Dashboard.jsx:58` as a tab; no route in `App.jsx`; no sidebar entry; no deep-link URL |

---

## 11. Duplicate Responsibility Matrix

Source: `Sidebar.jsx:224-275`

| Duplicate Responsibility | Owner A | Owner B | Runtime Consequence | Severity |
|---|---|---|---|---|
| **Orders screen** | Online Store (E-commerce) section | CommerceHub Admin section | Same route `/ecommerce/orders`, same component `Orders.jsx`, same capability `commercehub.orders.view` | High — two product workspaces claim the same screen |
| **Orders capability** | Online Store Orders (`Sidebar.jsx:230`) | CommerceHub Orders (`Sidebar.jsx:270`) | Identical capability gate `commercehub.orders.view` — no separation of concern | High |
| **Storefront Status** | Storefront sidebar (`/storefront/status`) | Global Settings tab (`/settings?tab=storefront`) | Same component `StorefrontSettings.jsx` rendered in two different contexts | Medium — operational settings scattered |
| **Analytics** | Online Store (E-commerce) section with `commercehub.analytics.view` | (No separate Analytics workspace — grouped under E-commerce) | Analytics screens masquerade as Online Store screens | Medium — analytics is not commerce |
| **Shipping** | Online Store sidebar (links to `/admin/shipping/setup`) | Admin/Shipping Administration workspace (`/admin/shipping/setup`, `/admin/shipping`) | Sidebar "Shipping" node uses `requiredRoles` (POS role gate) not CommerceHub capability; points to admin shipping workspace, not the `/ecommerce/shipping-methods` route | Medium — inconsistent authorization model |

---

## 12. Hidden Screen Matrix

| Screen | Status | Reason | Evidence |
|---|---|---|---|
| StorefrontSettings.jsx | **Hidden** — not on Online Store sidebar | Rendered only inside `Settings.jsx` as a tab (`Settings.jsx:719-731, 750-754`); sidebar "E-commerce" section has no link to `/storefront/status`; the canonical Storefront sidebar does include it (`storefrontInformationArchitecture.js:18-22`) but uses `PosRoleRouteGuard`, not CommerceHub capability | `App.jsx:340` routes `/storefront/status` to `StorefrontSettings`; `Settings.jsx:729` tab label uses `t('storefront.onlineStore')`; `StorefrontStatusCard.jsx:125-131, 149, 176` links to it |
| StorefrontAnalyticsDashboard.jsx | **Hidden** — no standalone route | Only rendered inside `Dashboard.jsx` as the "storefront" tab (`Dashboard.jsx:4-5, 56-58`); no route registered in `App.jsx`; no sidebar entry | `Dashboard.jsx:4` import, `Dashboard.jsx:11` tab definition, `Dashboard.jsx:58` conditional render |
| ShippingMethods.jsx | **Mislinked** — sidebar points to wrong route | Sidebar "Shipping" (`Sidebar.jsx:238`) links to `/admin/shipping/setup` which renders `ShippingAdministrationWorkspace` (`App.jsx:385-386`). The `/ecommerce/shipping-methods` route (`App.jsx:298`) renders `ShippingMethods.jsx` (5-line stub that wraps the same workspace with `initialTab="shipping-methods"`) but has no sidebar entry | `Sidebar.jsx:238`; `App.jsx:298` (stub route); `App.jsx:385-386` (sidebar route); `ShippingMethods.jsx:1-5` (366-char stub) |

---

## 13. Cross Product Leakage

### 13.1 POS Admin → Online Store leakage

**The "Online Store" terminology leaks from POS Admin into global Settings:**

The `storefront.onlineStore` i18n key ("Online Store") is used in `Settings.jsx:729` as a tab label and in `StorefrontStatusCard.jsx:149,176` as a display label. This creates the impression that "Online Store" is a POS Admin concept, when it is actually a CommerceHub/Storefront concept that has no dedicated sidebar presence in the Online Store workspace.

### 13.2 Online Store → CommerceHub leakage

**Orders screen is shared:**

The `/ecommerce/orders` route is registered in `App.jsx:313-314` under `CommerceHubVendorRouteGuard` with capability `commercehub.orders.view`. This same route is linked from both the "E-commerce" sidebar section (`Sidebar.jsx:230`) and the "CommerceHub" sidebar section (`Sidebar.jsx:270`). The CommerceHub admin section is described in the sidebar comment as "Global Marketplace Publishing" (`Sidebar.jsx:263`), yet it claims an Online Store screen.

### 13.3 Online Store → Storefront leakage

**Storefront screens embedded in Online Store context:**

`StorefrontAnalyticsDashboard.jsx` is rendered inside `Dashboard.jsx` (`Dashboard.jsx:58`) which is the POS Admin dashboard, not the Online Store workspace. Similarly, `StorefrontSettings.jsx` is rendered inside `Settings.jsx` (`Settings.jsx:750-754`), which is a global POS Admin screen. Neither screen has a dedicated entry in the Online Store (E-commerce) sidebar.

### 13.4 Storefront → Online Store leakage

**Analytics routes under E-commerce:**

Five analytics routes (`/analytics/funnels`, `/analytics/attribution`, `/analytics/attribution/costs`, `/analytics/attribution/roi`) and one settings route (`/settings/analytics`) are registered as children of the "E-commerce" sidebar section (`Sidebar.jsx:248-252`). Analytics is a cross-cutting concern, not a core Online Store/Catalog/Orders function. These routes use `commercehub.analytics.view` capability, tying them to the CommerceHub vendor model, but their URL prefixes (`/analytics/*`, `/settings/*`) break the `/ecommerce/*` convention.

### 13.5 Summary of leakage points

| From Workspace | To Workspace | Screen/Route | Evidence |
|---|---|---|---|
| CommerceHub Admin | Online Store | `/ecommerce/orders` | `Sidebar.jsx:270` (CommerceHub section) claims same route as `Sidebar.jsx:230` (E-commerce section) |
| POS Admin (Settings) | Online Store | StorefrontSettings (via tab) | `Settings.jsx:719-731, 750-754` |
| POS Admin (Dashboard) | Storefront/Online Store | StorefrontAnalyticsDashboard (via tab) | `Dashboard.jsx:4-5, 56-58` |
| Online Store | CommerceHub Admin | `/ecommerce/orders` (same route) | `App.jsx:313-314` — single route, shared component |
| Online Store | Global Settings | `/settings/analytics` | `Sidebar.jsx:252` (under E-commerce) and `Sidebar.jsx:345-350` (under Settings) |

---

## 14. Workspace Risks

| Risk ID | Risk | Likelihood | Impact | Rating | Source-backed reason |
|---|---|---|---|---|---|
| R-01 | Users cannot find Online Store screens because the sidebar says "E-commerce" not "Online Store" | High | Medium | High | Sidebar label `t('nav.ecommerce')` = "E-commerce" (`Sidebar.jsx:227`, `en.json:78`); canonical docs use "Online Store" (`en.json:1098`, `Settings.jsx:729`) |
| R-02 | The entire Online Store section disappears when the user lacks any `commercehub.*` capability claims | High | High | Critical | `filterMenuNode` in `Sidebar.jsx:356-375` removes children whose capability check fails; parent collapses when `children.length === 0` (`Sidebar.jsx:370-372`); all 17 children have capabilities or implicit path-map matches |
| R-03 | Users confuse CommerceHub Orders and Online Store Orders (same screen, two sidebar entries) | Medium | Medium | Medium | `/ecommerce/orders` appears in both E-commerce (`Sidebar.jsx:230`) and CommerceHub (`Sidebar.jsx:270`) sections |
| R-04 | Shipping screen is mislinked — sidebar goes to `/admin/shipping/setup`, route `/ecommerce/shipping-methods` has a stub component | Medium | Low | Medium | `Sidebar.jsx:238` vs `App.jsx:298`; `ShippingMethods.jsx` is a 5-line stub (`ShippingMethods.jsx:1-5`) |
| R-05 | StorefrontSettings (operational status) is not discoverable from Online Store sidebar | Medium | Medium | Medium | Hidden in `Settings.jsx` tab (`Settings.jsx:719-754`); no Online Store sidebar link to `/storefront/status` |
| R-06 | StorefrontAnalyticsDashboard has no route or sidebar entry | Low | Low | Low | Only rendered in `Dashboard.jsx:58` as a tab; no route in `App.jsx` |
| R-07 | Analytics routes break the `/ecommerce/*` convention (use `/analytics/*` and `/settings/*`) | Medium | Low | Medium | `Sidebar.jsx:248-252`; routes at `App.jsx:364-377, 380-381` |
| R-08 | The "E-commerce" section is the only major sidebar section without hierarchical sub-grouping | Medium | Low | Medium | `Sidebar.jsx:224-254` — all 17 children are flat; no "Orders", "Payments", "Catalog", "Marketing" sub-groups |
| R-09 | The CommerceHub admin section is not clearly delimited from the Online Store | Medium | Medium | Medium | Comment says "Global Marketplace Publishing" (`Sidebar.jsx:263`) but includes `/ecommerce/orders` (`Sidebar.jsx:270`) — a POS Online Store screen |

---

## 15. Migration Readiness

### 15.1 Current state assessment

The Online Store workspace is **not ready** for a canonical migration. The following prerequisites must be met before any migration wave begins:

1. **Identity resolution:** The "E-commerce" vs "Online Store" terminology conflict must be resolved. A single product identity must be chosen and consistently applied across sidebar, routes, and documentation.

2. **Boundary separation:** The `/ecommerce/orders` route must be removed from the CommerceHub admin sidebar section (`Sidebar.jsx:270`) or from the Online Store section (`Sidebar.jsx:230`), not both. The CommerceHub admin should have its own Orders entry point if it needs one.

3. **Screen recovery:** `StorefrontSettings` must be linked from the Online Store sidebar (or the canonical Storefront sidebar already includes it via `storefrontInformationArchitecture.js:18-22`). `StorefrontAnalyticsDashboard` must be registered as a standalone route with a sidebar entry.

4. **Shipping reconciliation:** The `/ecommerce/shipping-methods` route and the `/admin/shipping/setup` route must be reconciled. The sidebar must link to the correct canonical route.

5. **Hierarchical grouping:** The flat 17-child E-commerce section must be organized into logical sub-groups (Orders, Payments, Catalog, Marketing, Analytics, Shipping).

### 15.2 Blocking issues

| Issue | Blocker for migration? | Resolution required |
|---|---|---|
| Terminology "E-commerce" vs "Online Store" | Yes | M0 |
| Orders dual sidebar registration | Yes | M0 |
| StorefrontSettings hidden in Settings tab | Yes | M0 |
| StorefrontAnalyticsDashboard no route | Yes | M0 |
| ShippingMethods mislinked | Yes | M0 |
| No hierarchical grouping | Yes | M1 |

### 15.3 No-code-impact observations

All 21 Online Store screens exist as component files and are registered as routes in `App.jsx`. No component file was deleted, no route was removed, and no capability gate was stripped. The issues are exclusively information-architecture, navigation, and discoverability problems.

---

## 16. Recommended Migration Waves

### M0 — Critical discovery and boundary fixes (blocking)

**Goal:** Establish a single identifiable, coherent Online Store workspace with no boundary violations.

| Task | Source target | Description |
|---|---|---|
| M0-1 | Resolve identity | Choose "Online Store" as the canonical product identity. Rename the sidebar section label from `t('nav.ecommerce')` = "E-commerce" to `t('storefront.onlineStore')` = "Online Store" or a new `nav.onlineStore` key (`Sidebar.jsx:227`). |
| M0-2 | Remove Orders duplication | Remove `/ecommerce/orders` from the CommerceHub admin sidebar section (`Sidebar.jsx:270`). The CommerceHub section should have its own Orders entry or point to a CommerceHub-scoped Orders view. |
| M0-3 | Surface StorefrontSettings | Add `/storefront/status` to the Online Store (or Storefront) sidebar with a proper "Storefront Status" label. Remove the hidden Settings tab dependency for discovery (`storefrontInformationArchitecture.js:18-22` already defines this node). |
| M0-4 | Route StorefrontAnalyticsDashboard | Register a standalone route for `StorefrontAnalyticsDashboard.jsx` in `App.jsx` and add it to the sidebar or Dashboard navigation (`Dashboard.jsx:4-5`). |
| M0-5 | Fix Shipping link | Make the sidebar "Shipping" node link to the correct canonical route. Either `/ecommerce/shipping-methods` or `/admin/shipping/setup` — pick one and make the route match the sidebar. |
| M0-6 | Add missing route guard | `/storefront/status` uses `PosRoleRouteGuard` (`App.jsx:332`) not CommerceHub capability. Verify this is the intended authorization model for Online Store operational status. |

### M1 — Information architecture restructuring

**Goal:** Organize the Online Store workspace into a coherent hierarchical structure.

| Task | Source target | Description |
|---|---|---|
| M1-1 | Group Online Store children | Reorganize the flat 17-child E-commerce section into sub-groups: Orders, Customers, Payments, Catalog, Marketing, Analytics, Shipping, AI (`Sidebar.jsx:224-254`). |
| M1-2 | Separate Analytics | Move analytics routes (`/analytics/*` and `/settings/analytics`) out of the Online Store section into a dedicated Analytics group or section (`Sidebar.jsx:248-252`). |
| M1-3 | Reconcile route prefixes | Standardize all Online Store routes under a single prefix (`/onlinestore/*` or `/ecommerce/*`) rather than mixing `/ecommerce/*`, `/analytics/*`, and `/settings/*`. |
| M1-4 | Align labels | Fix all label mismatches: "Smart Cart Recovery" → "Abandoned Cart Recovery", "Offers" → "Promotions", "Signal Hardening" → "Pixel Tracking" (`Sidebar.jsx:235, 244, 247`). |

### M2 — Workspace shell alignment

**Goal:** Give the Online Store workspace its own shell, breadcrumbs, and header.

| Task | Source target | Description |
|---|---|---|
| M2-1 | Create OnlineStoreAdminFrame | Add an `OnlineStoreAdminFrame` analogous to `StorefrontAdminFrame.jsx:16-43`, wrapping Online Store routes with breadcrumbs and `AuthoringShell` (`App.jsx:235`); |
| M2-2 | Implement Online Store breadcrumbs | Extend `StorefrontBreadcrumbs.jsx` or create an `OnlineStoreBreadcrumbs` component to handle `/ecommerce/*` and `/analytics/*` paths. |
| M2-3 | Add workspace header | Create an Online Store workspace header analogous to the Storefront admin frame header. |

### M3 — Authorization and capability alignment

**Goal:** Align the authorization model with the workspace boundaries.

| Task | Source target | Description |
|---|---|---|
| M3-1 | Separate CommerceHub and Online Store auth | Ensure CommerceHub admin screens use `commercehub.catalog.manage` / `commercehub.finance.view` etc., while Online Store screens use a POS-tier capability set that doesn't collapse the entire section. |
| M3-2 | Add POS role fallback | If POS Owner/Admin users should always see the Online Store section, add a role-based fallback in `filterMenuNode` (`Sidebar.jsx:356-375`) so the section is visible even without `commercehub.*` claims. |
| M3-3 | Verify Shipping authorization | The sidebar "Shipping" node uses `requiredRoles` (`Sidebar.jsx:238`), not CommerceHub capabilities. This is inconsistent with the rest of the section. |

### M4 — Polish and parity

**Goal:** Final alignment with Shopify parity and quality gates.

| Task | Source target | Description |
|---|---|---|
| M4-1 | Shopify parity review | Compare the final hierarchical structure with Shopify's Online Store admin organization. |
| M4-2 | End-to-end journey testing | Verify click paths from sidebar entry to every screen reach their target without confusion. |
| M4-3 | Documentation update | Update `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md` and design docs to reflect the canonical "Online Store" terminology. |

---

## 17. Evidence

All evidence below is drawn from the repository at the current HEAD. No application source was modified during this audit.

### 17.1 Component files verified to exist

```
PointOfSale.Web/src/pages/Orders.jsx                        (929 lines)
PointOfSale.Web/src/pages/Coupons.jsx                       (663 lines)
PointOfSale.Web/src/pages/Promotions.jsx                    (920 lines)
PointOfSale.Web/src/pages/SmartOffersPage.jsx               (481 lines)
PointOfSale.Web/src/pages/CommerceAI.jsx                    (408 lines)
PointOfSale.Web/src/pages/PaymentMethods.jsx               (691 lines)
PointOfSale.Web/src/pages/ManualPayments.jsx               (624 lines)
PointOfSale.Web/src/pages/ShippingMethods.jsx                  (5 lines, 237 chars — stub)
PointOfSale.Web/src/pages/AbandonedCheckouts.jsx             (521 lines)
PointOfSale.Web/src/pages/AbandonedCartRecovery.jsx          (418 lines)
PointOfSale.Web/src/pages/BlockedPhones.jsx                  (216 lines)
PointOfSale.Web/src/pages/AnalyticsSettings.jsx             (1050 lines)
PointOfSale.Web/src/pages/StorefrontAnalyticsDashboard.jsx  (313 lines)
PointOfSale.Web/src/pages/StorefrontSettings.jsx            (465 lines)
PointOfSale.Web/src/pages/StorefrontDashboard.jsx           (263 lines)
PointOfSale.Web/src/components/StorefrontStatusCard.jsx     (216 lines)
PointOfSale.Web/src/components/Sidebar.jsx                  (570 lines)
PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.jsx (13 lines)
PointOfSale.Web/src/components/PosRoleRouteGuard.jsx         (13 lines)
PointOfSale.Web/src/auth/posVendorSessionAuthority.js      (351 lines)
PointOfSale.Web/src/auth/storefrontAdminAuthority.js        (5 lines)
PointOfSale.Web/src/modules/storefront/informationArchitecture/index.js          (9 lines)
PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js (280 lines)
PointOfSale.Web/src/modules/storefront/informationArchitecture/StorefrontAdminFrame.jsx (45 lines)
PointOfSale.Web/src/modules/storefront/informationArchitecture/StorefrontBreadcrumbs.jsx (87 lines)
```

### 17.2 App.jsx route registrations (verified)

```
// App.jsx:297-298 — Shipping Methods (POS role-gated, stub route)
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

// App.jsx:319-321 — Online Customers
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

// App.jsx:332-357 — Storefront administration (PosRoleRouteGuard, Owner/Admin)
<Route element={<PosRoleRouteGuard roles={['Owner', 'Admin']} />}>
    <Route element={<StorefrontAdminFrame />}>
        ...Routes for /storefront, /storefront/status, /storefront/themes, etc.
    </Route>
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

// App.jsx:384-387 — Shipping Administration
<Route element={<PosRoleRouteGuard roles={['Owner', 'Admin', 'Manager']} />}>
    <Route path="admin/shipping" element={<ShippingAdministrationWorkspace />} />
    <Route path="admin/shipping/setup" element={<ShippingAdministrationWorkspace />} />
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

### 17.3 Sidebar.jsx capability map and filtering logic

```
// Sidebar.jsx:33-42 — Implicit capability lookup map
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

// Sidebar.jsx:356-375 — Filter function (capability-based sidebar collapse)
const filterMenuNode = (item) => {
    if (item.requiresMarketplaceActor && !isServiceMarketplaceWorkflowActor(user, true)) return null
    if (item.requiredRoles?.length && !item.requiredRoles.includes(userRole)) return null
    const capability = item.capability || STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH.get(item.path)
    if (capability && !canAccessCommerceHubVendorNavigationCapability(user, capability)) return null
    const children = item.children?.map(filterMenuNode).filter(Boolean)
    if (item.children && children.length === 0) return null  // PARENT COLLAPSES
    return { ...item, children }
}
```

### 17.4 i18n key verification

```
// en.json:78 — Sidebar section label
"ecommerce": "E-commerce"

// en.json:1098 — Used in Settings.jsx tab and StorefrontStatusCard.jsx
"onlineStore": "Online Store"

// ar.json:1098 — Arabic translation
"onlineStore": "المتجر الإلكتروني"
```

The sidebar never uses `storefront.onlineStore` — it uses `nav.ecommerce` ("E-commerce"). The `storefront.onlineStore` key is used only for the Settings tab label (`Settings.jsx:729`) and the StorefrontStatusCard display label (`StorefrontStatusCard.jsx:149,176`).

### 17.5 Storefront settings tab in Settings.jsx

```
// Settings.jsx:36 — capability check
const canManageStorefront = canManageStorefrontSettings(user)

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

### 17.6 CommerceHub admin section (Sidebar.jsx:263-275)

```
{
    key: 'commercehub',
    label: t('nav.commerceHub', 'CommerceHub'),
    icon: '🏪',
    children: [
        { path: '/admin/commercehub', label: t('nav.commerceHubSettings', 'Publishing Settings'), icon: '⚙️', capability: 'commercehub.catalog.manage' },
        { path: '/ecommerce/orders', label: t('orders.title', 'Orders'), icon: '📦', capability: 'commercehub.orders.view' },  // ← DUPLICATE of Sidebar.jsx:230
        { path: '/commercehub/finance', label: t('nav.commerceHubFinance', 'Finance Dashboard'), icon: '💼', capability: 'commercehub.finance.view' },
        { path: '/commercehub/earnings', label: t('nav.commerceHubEarnings', 'Vendor Earnings'), icon: '📈', capability: 'commercehub.finance.view' },
        { path: '/commercehub/payments', label: t('nav.commerceHubDebtRepayment', 'Debt Repayment'), icon: '💸', capability: 'commercehub.payments.view' },
    ],
}
```

Note: `t('nav.commerceHub', 'CommerceHub')` — the `nav.commerceHub` key does NOT exist in `en.json`, so it falls back to the hardcoded string "CommerceHub". Same for `nav.commerceHubSettings`, `nav.commerceHubFinance`, `nav.commerceHubEarnings`, `nav.commerceHubDebtRepayment`, `nav.marketplace`, `nav.affiliateManagement`, `nav.financeControlCenter`, `nav.mainNavigation`.

### 17.7 Dashboard.jsx tab rendering

```
// Dashboard.jsx:9-12 — Tab definitions
const DASHBOARD_TABS = [
    { id: 'pos', labelKey: 'dashboard.tabs.pos' },
    { id: 'storefront', labelKey: 'dashboard.tabs.storefront' },
]

// Dashboard.jsx:4 — Import
import StorefrontAnalyticsDashboard from './StorefrontAnalyticsDashboard'

// Dashboard.jsx:56-58 — Tab conditional render
{activeTab === 'pos'
    ? <PosDashboardContent />
    : <StorefrontAnalyticsDashboard />}
```

### 17.8 StorefrontSettings.jsx

```
// StorefrontSettings.jsx:1-4 — Imports from authoring platform
import ShippingMethods from '../modules/shipping-administration/ShippingAdministrationWorkspace'
// (No — this is ShippingMethods.jsx:1)

// StorefrontSettings.jsx:35 — Component declaration
export default function StorefrontSettings({ onDirtyChange }) {

// StorefrontSettings.jsx:43 — Authorization
const canManageStorefront = canManageStorefrontSettings(user)

// StorefrontSettings.jsx:227 — Permission gate
if (!canManageStorefront) { ... }
```

The component header comment at `StorefrontSettings.jsx:20-30` confirms it is the "Storefront Status Component" controlling "Storefront status (live/maintenance/disabled)" and "Maintenance message."

### 17.9 ShippingMethods.jsx (stub)

```
// ShippingMethods.jsx:1-5 (237 bytes total)
import ShippingAdministrationWorkspace from '../modules/shipping-administration/ShippingAdministrationWorkspace'

export default function ShippingMethods() {
    return <ShippingAdministrationWorkspace initialTab="shipping-methods" />
}
```

### 17.10 posVendorSessionAuthority.js capabilities

```
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

### 17.11 storefrontInformationArchitecture.js (canonical Storefront IA)

The canonical `STOREFRONT_NAVIGATION` defines 11 nodes (`storefrontInformationArchitecture.js:3-119`) including `Storefront Status` at `/storefront/status` (`storefrontInformationArchitecture.js:18-22`). This node is in the canonical Storefront sidebar (`localizeStorefrontNode(STOREFRONT_NAVIGATION)` in `Sidebar.jsx:286`) but has no counterpart in the Online Store (E-commerce) section.

### 17.12 Route compatibility map

```
// storefrontInformationArchitecture.js:121-158 — STOREFRONT_ROUTE_COMPATIBILITY
{ screenId: 'status', canonical: '/storefront/status', legacy: ['/settings?tab=storefront'] }
```

This confirms that `/settings?tab=storefront` is a legacy alias for the canonical `/storefront/status` route.

---

## 18. Source Citations

| File | Lines | Relevance |
|---|---|---|
| `PointOfSale.Web/src/App.jsx` | 1-459 | All Online Store route registrations and guards; ShippingAdministrationWorkspace routes; StorefrontAdminFrame wrapping |
| `PointOfSale.Web/src/components/Sidebar.jsx` | 24-42, 224-254, 263-275, 286, 353-378 | E-commerce section registration, capability filtering, CommerceHub section, Storefront section |
| `PointOfSale.Web/src/auth/posVendorSessionAuthority.js` | 4-32, 56-68, 201-210, 242-292 | CommerceHub vendor endpoint list, capabilities, and authorization (`canAccessCommerceHubVendorNavigationCapability`) |
| `PointOfSale.Web/src/auth/storefrontAdminAuthority.js` | 1-5 | `canManageStorefrontSettings` role check (Owner/Admin) |
| `PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.jsx` | 1-13 | Route guard implementation for CommerceHub capability checks |
| `PointOfSale.Web/src/components/PosRoleRouteGuard.jsx` | 1-13 | POS role-based route guard |
| `PointOfSale.Web/src/pages/Settings.jsx` | 1-764 | "Online Store" tab (line 719-754) rendering StorefrontSettings; `canManageStorefrontSettings` check (line 36) |
| `PointOfSale.Web/src/pages/StorefrontSettings.jsx` | 1-465 | Operational Storefront Status editor; uses authoring platform components |
| `PointOfSale.Web/src/pages/StorefrontDashboard.jsx` | 1-263 | Storefront overview/dashboard with deep links to owning screens |
| `PointOfSale.Web/src/pages/StorefrontAnalyticsDashboard.jsx` | 1-313 | Storefront analytics component (Dashboard tab only, no standalone route) |
| `PointOfSale.Web/src/components/StorefrontStatusCard.jsx` | 1-216 | "Online Store" label usage (lines 149, 176); links to `/storefront/status` (lines 125-131) |
| `PointOfSale.Web/src/pages/Dashboard.jsx` | 1-61 | Tab-based rendering of StorefrontAnalyticsDashboard (lines 4, 11, 56-58) |
| `PointOfSale.Web/src/pages/ShippingMethods.jsx` | 1-5 | Stub component (237 chars) wrapping ShippingAdministrationWorkspace |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/index.js` | 1-9 | Exports STOREFRONT_NAVIGATION and resolveStorefrontScreen |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js` | 1-280 | Canonical Storefront IA nodes, route compatibility map, screen resolver, breadcrumb definitions |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/StorefrontAdminFrame.jsx` | 1-45 | Storefront authoring shell wrapper (AuthoringPlatformProvider + AuthoringShell + breadcrumbs) |
| `PointOfSale.Web/src/modules/storefront/informationArchitecture/StorefrontBreadcrumbs.jsx` | 1-87 | Storefront-only breadcrumb resolver (`resolveStorefrontScreen` handles `/storefront/*` only) |
| `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx` | 1-374 | Full shipping administration workspace (tabs: overview, carrier-accounts, shipping-methods, policies, warehouses, health, audit) |
| `PointOfSale.Web/src/i18n/locales/en.json` | 78, 1098 | `"ecommerce": "E-commerce"` and `"onlineStore": "Online Store"` |
| `PointOfSale.Web/src/i18n/locales/ar.json` | 78, 1098 | Arabic translations of same keys |
| `docs/STOREFRONT_CANONICAL_UX_BLUEPRINT.md` | 6-21, 181-198, 712-722 | Canonical Storefront menu hierarchy and naming; Online Store is "adjacent authority" |
| `docs/ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md` | 1-1009 | Primary reference for this certification (Online Store IA audit) |
| `docs/STOREFRONT_UX_ARCHITECTURE_AUDIT.md` | 16-17, 77-93, 129-146, 288 | Current Storefront menu/routes; F-02 finding (hidden Storefront Settings) |
| `docs/FINAL_STOREFRONT_ARCHITECTURE_CERTIFICATION.md` | 5-21, 54-75 | Final Storefront architecture certification; boundary ownership |
| `docs/WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md` | 1-247 | Authoring experience alignment; action ordering contract |
| `docs/WAVE3_AUTHORING_PLATFORM_EXTRACTION.md` | 1-281 | Authoring platform extraction and certification |
| `git log` | d68b0b5, 2984cce, 3fb3d5c | Commit history of Sidebar.jsx and Storefront IA |

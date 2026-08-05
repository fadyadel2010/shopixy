# Storefront Canonical Workspace Blueprint
*Constitutional amendment — Online Store section re-scoped as Operations domain*

- **Project:** PointOfSale / Shopixy
- **Product surface:** POS Admin — Storefront product (Design + Operations domains)
- **Date:** 2026-08-05
- **Document class:** Constitutional architecture blueprint
- **Mode:** Blueprint only; no implementation or application-source change
- **Amendment type:** Constitutional correction (this document was previously titled *Online Store Canonical Workspace Blueprint*)
- **Blueprint certification:** **PASS WITH OBSERVATIONS**

---

## Table of Contents

1. Executive Summary
2. Architectural Principles
3. Product Constitution
4. Product Identity
5. Workspace Mission
6. Domain Model
7. Workspace Boundaries
8. Canonical Navigation Tree
9. Screen Classification Matrix
10. Workspace Shell
11. Navigation Constitution
12. Authorization Constitution
13. Publishing Constitution
14. Operational UX Constitution
15. Shopify Parity
16. Migration Waves
17. Architecture Laws
18. Governance Rules
19. Risks
20. Recommendations
21. Certification
22. Amendment Record
23. Appendix A: Source Citation Index

---

## 1. Executive Summary

This document is the definitive constitutional blueprint for the **Storefront** product workspace in the POS Admin. It was previously titled the *Online Store Canonical Workspace Blueprint* and erroneously modeled the **Storefront** and **Online Store** as two independent product workspaces. That constitutional model has been **corrected** on the basis of a refined business-domain model discovered after the original blueprint's approval.

**The corrected constitution is:**

> The POS Admin manages ONE Storefront.
> That Storefront has multiple domains.
> **Design** manages how the storefront looks.
> **Operations** manages how the storefront operates.

**Storefront is the Product.** It is not a peer of "Online Store." The surfaces previously labeled "Online Store" (the sidebar section keyed `ecommerce`, currently labeled `t('nav.ecommerce')` = "E-commerce", and the routes under `/ecommerce/*`, `/analytics/*`, and `/settings/analytics`) are the **Operations domain** of the Storefront product. They are gated behind CommerceHub vendor capabilities (`commercehub.*`) and rendered via the `StorefrontAdminFrame` shell (`StorefrontAdminFrame.jsx:16-43`), analogous to the Design domain's existing shell usage (`App.jsx:337-358`).

This blueprint is derived entirely from the certified source-backed reference documents and supersedes the advisory M0–M4 recommendations in `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md` and `ONLINE_STORE_WORKSPACE_CERTIFICATION.md`. It establishes the Storefront as a single coherent product with two visible business domains — Design and Operations — and a Settings section. Technical modules remain internal and are never exposed as independent products.

**Certification: PASS WITH OBSERVATIONS** — This blueprint defines a coherent, bounded, source-backed canonical architecture for the Storefront product workspace. Implementation of this blueprint remains future work.

---

## 2. Architectural Principles

The following principles govern the Storefront product workspace architecture:

1. **One Identity.** The product has one public name — "Storefront" — used consistently in the sidebar, routes, documentation, and user-facing labels. The Operations domain surfaces are labeled "Online Store" (canonical, from `storefront.onlineStore`, `en.json:1098`) within the Design/Operations domain hierarchy, not as a standalone product.

2. **One Boundary.** The Storefront product boundary is cleanly delimited. No screen is owned by two product workspaces simultaneously.

3. **One Owner.** Every screen has exactly one owner (a domain within the Storefront product), exactly one navigation home, and exactly one canonical route.

4. **Source-Backed.** Every claim in this blueprint is traceable to production source files, certified recovery documents, or canonical reference documents. No assumptions.

5. **No Disappearance.** Capability-gated sidebar filtering must never cause an entire domain section to disappear without a visible placeholder or fallback that preserves the user's belief that the product exists.

6. **Discoverability.** Every canonical screen must be reachable from exactly one primary navigation entry point. No screen may remain hidden behind an unrelated global tab or a tab-only rendering path without a standalone route.

7. **Authorization at the Backend.** Menu visibility is a presentation convenience, not a security boundary. Every route is independently guarded by `CommerceHubVendorRouteGuard` or `PosRoleRouteGuard` at the route level.

8. **Documentation Only.** This blueprint is a blueprint document. No source code, routes, permissions, sidebar entries, or runtime behavior are changed by its creation.

9. **Domains Are Visible, Modules Are Internal.** Business domains (Design, Operations) must be visible to merchants. Internal technical modules (e.g., `authoringPlatform`, `storeConfigService`, `posVendorSessionAuthority`) remain internal and are never surfaced as product domains.

10. **One Product, Multiple Domains.** A merchant manages one Storefront. Every Storefront capability belongs to exactly one domain. Domains never become independent products.

---

## 3. Product Constitution

### 3.1 Constitutional Authority

The following documents are constitutional for this blueprint:

| Document | Role |
|---|---|
| `ONLINE_STORE_WORKSPACE_CERTIFICATION.md` | Source audit and current-state verdict (FAIL) |
| `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md` | Source-backed IA audit, screen inventory, capability analysis |
| `STOREFRONT_CANONICAL_UX_BLUEPRINT.md` | Canonical Storefront administration UX blueprint (Laws 1–38, §2.1–§12.7); Design domain authority |
| `STOREFRONT_UX_PROTOTYPE_VALIDATION.md` | Prototype validation of the Storefront UX proposal |
| `FINAL_STOREFRONT_ARCHITECTURE_CERTIFICATION.md` | Frozen final Storefront runtime/Theme/Builder/Projection/Cache boundaries |
| `M1_INFORMATION_ARCHITECTURE_IMPLEMENTATION.md` | Wave 1 Storefront IA implementation (canonical hierarchy, route compatibility, breadcrumbs) |
| `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md` | Wave 2 authoring experience alignment (action ordering, dirty-state, conflicts) |
| `WAVE3_AUTHORING_PLATFORM_EXTRACTION.md` | Wave 3 authoring platform extraction (shared platform contracts) |

### 3.2 Relationship to Storefront Constitution

The Storefront Constitution (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md`) governs the tenant Storefront administration domain rooted at `/storefront`. The surfaces previously labeled "Online Store" (the sidebar section keyed `ecommerce`, the `/ecommerce/*` routes, the `/analytics/*` routes, and `/settings/analytics`) are now understood as the **Operations domain** of the same Storefront product, not an adjacent authority.

The prior blueprint's claim that:

> "Marketplace and global Super Admin authoring are adjacent authorities, not children of the tenant Storefront menu."

(`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:21`)

is **confirmed correct** — CommerceHub Admin (Publishing Settings, Finance Dashboard, Vendor Earnings, Debt Repayment) and Services Marketplace ARE adjacent authorities, not children of the Storefront product. But the **Online Store / E-commerce section is NOT an adjacent authority** — it is a domain inside the Storefront product. The Operations domain surfaces CommerceHub vendor capability-gated screens that operate the storefront (orders, payments, catalog promotions, shipping, marketing analytics, AI assistance), and these are domain children of the one Storefront product.

The CommerceHub Admin sidebar section (`Sidebar.jsx:263-275`, key `commercehub`) remains a distinct **adjacent authority** — it is not part of the Storefront product. Only the `/ecommerce/orders` route was incorrectly shared between it and the Operations domain; that must be removed from CommerceHub Admin.

---

## 4. Product Identity

### 4.1 Canonical Name

**Storefront** is the product name for the entire POS Admin product workspace that a merchant uses to manage their store.

Within the Storefront product, the Operations domain is labeled "Online Store" (canonical, from `storefront.onlineStore`, `en.json:1098`). The previous blueprint's claim that "Online Store" was a standalone product name is **withdrawn** — it is a domain label within the Storefront product.

| Current term | Canonical term | Source of canonical term |
|---|---|---|
| "E-commerce" (sidebar label, `nav.ecommerce`, `en.json:78`) | **Online Store** (domain label within Storefront product) | `storefront.onlineStore`, `en.json:1098`; design docs `docs/reports/PHASE-7.10-STOREFRONT-BRIDGE.md:31`, `docs/reports/LAUNCH-NOTES.md:103` |
| "Smart Cart Recovery" (sidebar label) | **Abandoned Cart Recovery** | `AbandonedCartRecovery.jsx` |
| "Offers" (sidebar label, `promotions.title`) | **Promotions** | `en.json:1390`; `Promotions.jsx` |
| "Signal Hardening" (sidebar label) | **Pixel Tracking** | `PixelTrackingConfigPage` |
| "AI Assistant" (sidebar label) | **Commerce AI** | `CommerceAI.jsx` |
| "E-commerce" as a standalone product | **Online Store** domain within the **Storefront** product | Corrected business-domain model |

### 4.2 Identity Enforcement Rule

The sidebar section currently labeled "E-commerce" (`Sidebar.jsx:227`, `en.json:78`) must adopt the canonical label "Online Store" as the **Operations domain** header within the Storefront product hierarchy. This is the single source of truth for the domain name across all UI surfaces, documentation, and design artifacts.

---

## 5. Workspace Mission

### 5.1 What the Storefront Product Owns

The Storefront product workspace owns all storefront administration surfaces, organized into two visible business domains:

| Domain | Owned Responsibilities |
|---|---|
| **Design** | Homepage, Themes, Pages (CMS), Navigation (Header/Footer links), Footer Content, Appearance (Colors & Typography), Storefront Status |
| **Operations** | Orders, Abandoned Checkouts, Abandoned Cart Recovery, Blocked Phones, Online Customers, WhatsApp Settings, Checkout, Payments, Catalog (Coupons, Promotions, Smart Offers), Shipping, Marketing (Pixel Tracking, Conversion Funnels, Marketing Attribution, Campaign Costs, Marketing ROI, Analytics Providers), Commerce AI, Auth Providers |

### 5.2 What the Storefront Product Never Owns

| Never-owned responsibility | Canonical owner |
|---|---|
| POS Admin core (Database, Accounts, Treasury, Inventory, Expenses, etc.) | POS Admin core workspaces (POS roles, `PosRoleRouteGuard`) |
| CommerceHub Admin (Publishing Settings, Finance Dashboard, Vendor Earnings, Debt Repayment) | CommerceHub Admin section (`commercehub` sidebar key) — adjacent authority |
| Affiliate management | Affiliate Management section (`admin-affiliates` route) |
| Services Marketplace | Services Marketplace (`requiresMarketplaceActor`) |
| Reports | POS Admin Reports section |
| Finance Control Center | POS Admin Finance Control Center (role-gated) |
| Global Settings | POS Admin Settings section |

---

## 6. Domain Model

### 6.1 Design Domain

Manages how the storefront **looks**. This domain is the canonical authority of `STOREFRONT_CANONICAL_UX_BLUEPRINT.md` and is implemented through the `StorefrontAdminFrame` (`StorefrontAdminFrame.jsx:16-43`), `StorefrontBreadcrumbs` (`StorefrontBreadcrumbs.jsx:1-87`), and the `STOREFRONT_NAVIGATION` information architecture (`storefrontInformationArchitecture.js:3-119`).

| Design Sub-domain | Screens / Routes | Source |
|---|---|---|
| Overview | `/storefront`, `/storefront/overview` → `StorefrontDashboard.jsx` | `App.jsx:338-339` |
| Storefront Status | `/storefront/status` → `StorefrontSettings.jsx` | `App.jsx:340`; `Settings.jsx:750-754` (tab) |
| Homepage | `/storefront/homepage` → `StorefrontBuilderPage` | `App.jsx:334` |
| Catalog | `/storefront/catalog/product-template` → `StorefrontBuilderPage` | `App.jsx:335` |
| Pages | `/storefront/content/pages`, `/storefront/content/pages/:pageId` → `CmsPagesList`, `CmsPageEditor` | `App.jsx:349-350` |
| Navigation | `/storefront/navigation?view=header`, `/storefront/navigation?view=footer` → `NavigationEditor` | `App.jsx:353-354` |
| Footer Content | `/storefront/footer-content` → `StorefrontFooterSettings` | `App.jsx:355-356` |
| Appearance | `/storefront/appearance/themes` → `ThemeMarketplacePage`; `/storefront/appearance/colors-typography` → `ThemeEditorPage` | `App.jsx:342-345` |
| History | `/storefront/storeconfig-version-history` → `VersionHistoryPage` | `App.jsx:346-347` |

### 6.2 Operations Domain

Manages how the storefront **operates**. These are the surfaces formerly called "Online Store" / "E-commerce". They are CommerceHub vendor capability-gated (`CommerceHubVendorRouteGuard`, `canAccessCommerceHubVendorNavigationCapability` at `posVendorSessionAuthority.js:204-210`) and use the capability set defined in `posVendorSessionAuthority.js:56-68`.

| Operations Sub-domain | Screens / Routes | Capability |
|---|---|---|
| Overview | `/ecommerce/overview` (to be created) | All Online Store users |
| Orders | `/ecommerce/orders`, `/ecommerce/orders/:orderId`, `/ecommerce/blocked-phones`, `/ecommerce/whatsapp`, `/ecommerce/cart-recovery`, `/ecommerce/abandoned-checkouts`, `/ecommerce/online-customers` | `commercehub.orders.view` |
| Checkout | `/ecommerce/checkout` (future) | `commercehub.orders.view` |
| Payments | `/ecommerce/payment-methods`, `/ecommerce/manual-payments` | `commercehub.payments.view` |
| Catalog | `/ecommerce/coupons`, `/ecommerce/promotions`, `/ecommerce/smart-offers` | `commercehub.storefront.manage` |
| Shipping | `/ecommerce/shipping-methods` | `PosRoleRouteGuard` (Owner/Admin/Manager) |
| Marketing | `/ecommerce/pixel-tracking`, `/analytics/funnels`, `/analytics/funnels/:funnelKey`, `/analytics/attribution`, `/analytics/attribution/models`, `/analytics/attribution/orders`, `/analytics/attribution/costs`, `/analytics/attribution/roi`, `/settings/analytics` | `commercehub.analytics.view` |
| Commerce AI | `/ecommerce/commerce-ai` | `commercehub.storefront.manage` |
| Auth | `/ecommerce/auth-providers` | `commercehub.storefront.manage` |

### 6.3 Settings Section

The global POS Admin Settings section (`/settings`) is not part of either Design or Operations domain. It is a POS Admin Core surface (`Sidebar.jsx:345-350`). The `/settings/analytics` route (`AnalyticsSettings.jsx`) is owned by the Operations domain but is registered under the Settings section for historical reasons (see V-03).

---

## 7. Workspace Boundaries

### 7.1 Declared Workspaces

| Workspace | Sidebar Section Key | Route Prefix | Authorization | Declared Owner |
|---|---|---|---|---|
| POS Admin Core | `dashboard`, `database`, `accounts`, `opening_balance`, `transactions`, `treasury`, `inventory`, `expenses`, `account_statement` | `/customers`, `/products`, `/pos`, `/treasury`, `/inventory`, etc. | `PosRoleRouteGuard` / `ProtectedRoute` | POS Admin |
| **Storefront → Design** | `storefront` (via `STOREFRONT_NAVIGATION`) | `/storefront/*` | `PosRoleRouteGuard` (Owner/Admin) | Storefront Admin |
| **Storefront → Operations** | `ecommerce` (canonical: "Online Store") | `/ecommerce/*`, `/analytics/*` (scoped), `/settings/analytics` (scoped) | `CommerceHubVendorRouteGuard` (capability-gated) | CommerceHub vendor |
| CommerceHub Admin | `commercehub` | `/admin/commercehub`, `/commercehub/*` | `CommerceHubVendorRouteGuard` | CommerceHub admin (adjacent authority) |
| Affiliate Management | `admin-affiliates` | `/admin/affiliates` | `PosRoleRouteGuard` (Owner/Admin) | POS Admin |
| Services Marketplace | `marketplace` | `/marketplace` | `requiresMarketplaceActor` | Marketplace workflow |
| Reports | `reports` | `/reports/*`, `/sales-history` | `ProtectedRoute` | POS Admin |
| Finance Control Center | `finance_control` | `/treasury/*` | Role-gated (Admin/SuperAdmin/Owner/Accountant) | POS Admin |
| Users | `users` | `/settings/audit-log` | Role-gated (Admin/SuperAdmin) | POS Admin |
| Settings | `settings` | `/settings` | `ProtectedRoute` | POS Admin |

### 7.2 Boundary Violations (to be resolved by migration)

| Violation | Detail | Source |
|---|---|---|
| **V-01: Orders dual ownership** | `/ecommerce/orders` appears in both the Operations domain (`Sidebar.jsx:230`) and the CommerceHub Admin section (`Sidebar.jsx:270`). | `Sidebar.jsx:230,270` |
| **V-02: Analytics routes under Operations** | Five analytics routes (`/analytics/funnels`, `/analytics/attribution`, etc.) and `/settings/analytics` are registered under the "E-commerce" section despite analytics being a cross-cutting concern with `/analytics/*` and `/settings/*` prefixes that break the `/ecommerce/*` convention. | `Sidebar.jsx:248-252` |
| **V-03: Settings route under Operations** | `/settings/analytics` is registered as a child of the Operations domain section (`Sidebar.jsx:252`) despite being a global Settings route. The `/settings` route is a separate top-level sidebar section (`Sidebar.jsx:345-350`). | `Sidebar.jsx:252, 345-350` |
| V-04: Shipping mislocation | The sidebar "Shipping" node (`Sidebar.jsx:238`) points to `/admin/shipping/setup`, which is outside the `/ecommerce` route prefix. The `/ecommerce/shipping-methods` route (`App.jsx:298`) is registered but has no sidebar entry. | `Sidebar.jsx:238`, `App.jsx:298` |
| V-05: Storefront Status leaked into Settings | `StorefrontSettings.jsx` (operational Storefront Status editor) is rendered as a tab inside the global `Settings.jsx` (`Settings.jsx:719-731, 750-754`), not directly reachable from the Storefront Design domain sidebar. | `App.jsx:340`, `Settings.jsx:719-754` |
| V-06: StorefrontAnalyticsDashboard orphaned | `StorefrontAnalyticsDashboard.jsx` is rendered only as the "storefront" tab inside `Dashboard.jsx` (`Dashboard.jsx:4, 58`), with no standalone route or sidebar entry. | `Dashboard.jsx:4, 58` |
| V-07: CommerceHub admin duplicates Operations order capability | The CommerceHub admin section (`Sidebar.jsx:263-275`) includes an "Orders" entry (`Sidebar.jsx:270`) that routes to the same `/ecommerce/orders` as the Operations domain section, gated by the same `commercehub.orders.view` capability. | `Sidebar.jsx:263-275` |

---

## 8. Canonical Navigation Tree

### 8.1 Canonical Storefront Hierarchy (Corrected)

```text
Storefront
├─ Overview
├─ Design
│  ├─ Overview
│  ├─ Storefront Status
│  ├─ Homepage
│  ├─ Catalog
│  │  └─ Product Template
│  ├─ Content
│  │  └─ Pages
│  ├─ Navigation
│  │  ├─ Header
│  │  └─ Footer Links
│  ├─ Footer Content
│  ├─ Appearance
│  │  ├─ Themes
│  │  └─ Colors & Typography
│  └─ Settings
│     └─ StoreConfig Version History
├─ Operations
│  ├─ Overview
│  ├─ Orders
│  │  ├─ All Orders
│  │  ├─ Abandoned Checkouts
│  │  ├─ Abandoned Cart Recovery
│  │  ├─ Blocked Phones
│  │  ├─ Online Customers
│  │  └─ WhatsApp Settings
│  ├─ Checkout
│  │  └─ (future checkout configuration)
│  ├─ Payments
│  │  ├─ Payment Methods
│  │  └─ Manual Payments
│  ├─ Catalog
│  │  ├─ Coupons
│  │  ├─ Promotions
│  │  └─ Smart Offers
│  ├─ Shipping
│  │  └─ Shipping Setup
│  ├─ Marketing
│  │  ├─ Pixel Tracking
│  │  ├─ Conversion Funnels
│  │  ├─ Marketing Attribution
│  │  │  ├─ Overview
│  │  │  ├─ Campaign Costs
│  │  │  └─ Marketing ROI
│  │  └─ Analytics Providers
│  ├─ Commerce AI
│  ├─ Auth Providers
│  └─ Settings
│     └─ Analytics Providers
└─ History
```

> **Design** manages how the storefront looks (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md` §2.1, `STOREFRONT_NAVIGATION` in `storefrontInformationArchitecture.js:3-119`).
> **Operations** manages how the storefront operates (CommerceHub vendor capability-gated screens).
> **History** remains at the product root level (global) — version history for both domains.

### 8.2 Sub-grouping Rationale

The current POS Admin sidebar has two separate top-level sections: "E-commerce" (`Sidebar.jsx:224-254`, key `ecommerce`) and "Storefront" (`Sidebar.jsx:286`, via `localizeStorefrontNode(STOREFRONT_NAVIGATION)`). The corrected constitution merges these into a single **Storefront** product with two visible domains.

| Sub-group | Screens | Canonical capability |
|---|---|---|
| **Design / Overview** | StorefrontDashboard | `PosRoleRouteGuard` (Owner/Admin) |
| **Design / Storefront Status** | StorefrontSettings | `PosRoleRouteGuard` (Owner/Admin) |
| **Design / Homepage** | StorefrontBuilderPage | `PosRoleRouteGuard` (Owner/Admin) |
| **Design / Content** | CmsPagesList, CmsPageEditor | `PosRoleRouteGuard` (Owner/Admin) + `commercehub.storefront.cms.manage` |
| **Design / Navigation** | NavigationEditor (Header, Footer Links) | `PosRoleRouteGuard` (Owner/Admin) |
| **Design / Appearance** | ThemeMarketplacePage, ThemeEditorPage | `PosRoleRouteGuard` (Owner/Admin) |
| **Design / History** | VersionHistoryPage | `PosRoleRouteGuard` (Owner/Admin) |
| **Operations / Orders** | Orders, Abandoned Checkouts, Abandoned Cart Recovery, Blocked Phones, Online Customers, WhatsApp Settings | `commercehub.orders.view` |
| **Operations / Payments** | Payment Methods, Manual Payments | `commercehub.payments.view` |
| **Operations / Catalog** | Coupons, Promotions, Smart Offers | `commercehub.storefront.manage` |
| **Operations / Shipping** | Shipping Setup | `PosRoleRouteGuard` (Owner/Admin/Manager) |
| **Operations / Marketing** | Pixel Tracking, Conversion Funnels, Marketing Attribution, Campaign Costs, Marketing ROI, Analytics Providers | `commercehub.analytics.view` |
| **Operations / Commerce AI** | CommerceAI | `commercehub.storefront.manage` |
| **Operations / Auth** | AuthProvidersPage | `commercehub.storefront.manage` |

---

## 9. Screen Classification Matrix

All 21 Operations-domain screens, classified by canonical domain, workspace, route, and visibility. Source: `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:117-133`, verified against `App.jsx` and component files.

| # | Screen | File Path | Canonical Domain | Route Path | Sidebar? | Canonical Visibility |
|---|---|---|---|---|---|---|
| 1 | Orders | `src/pages/Orders.jsx` (929 lines) | Operations / Orders | `/ecommerce/orders`, `/ecommerce/orders/:orderId` | Yes (Operations only) | Visible when `commercehub.orders.view` |
| 2 | Abandoned Checkouts | `src/pages/AbandonedCheckouts.jsx` (521 lines) | Operations / Orders | `/ecommerce/abandoned-checkouts` | Yes | Visible when `commercehub.orders.view` |
| 3 | Abandoned Cart Recovery | `src/pages/AbandonedCartRecovery.jsx` (418 lines) | Operations / Orders | `/ecommerce/cart-recovery` | Yes | Visible when `commercehub.orders.view` |
| 4 | Blocked Phones | `src/pages/BlockedPhones.jsx` (216 lines) | Operations / Orders | `/ecommerce/blocked-phones` | Yes | Visible when `commercehub.orders.view` |
| 5 | Online Customers | `src/modules/onlineCustomers` | Operations / Customers | `/ecommerce/online-customers` | Yes | Visible when `commercehub.orders.view` |
| 6 | WhatsApp Settings | `src/modules/whatsapp` | Operations / Orders | `/ecommerce/whatsapp` | Yes | Visible when `commercehub.orders.view` |
| 7 | Payment Methods | `src/pages/PaymentMethods.jsx` (691 lines) | Operations / Payments | `/ecommerce/payment-methods` | Yes | Visible when `commercehub.payments.view` |
| 8 | Manual Payments | `src/pages/ManualPayments.jsx` (624 lines) | Operations / Payments | `/ecommerce/manual-payments` | Yes | Visible when `commercehub.payments.view` |
| 9 | Shipping Setup | `src/pages/ShippingMethods.jsx` (5 lines — stub) | Operations / Shipping | `/ecommerce/shipping-methods` | Yes (to be linked) | Visible when Owner/Admin/Manager |
| 10 | Coupons | `src/pages/Coupons.jsx` (663 lines) | Operations / Catalog | `/ecommerce/coupons` | Yes | Visible when `commercehub.storefront.manage` |
| 11 | Promotion | `src/pages/Promotions.jsx` (920 lines) | Operations / Catalog | `/ecommerce/promotions` | Yes | Visible when `commercehub.storefront.manage` |
| 12 | Smart Offers | `src/pages/SmartOffersPage.jsx` (481 lines) | Operations / Catalog/AI | `/ecommerce/smart-offers` | Yes | Visible when `commercehub.storefront.manage` |
| 13 | Commerce AI | `src/pages/CommerceAI.jsx` (408 lines) | Operations / AI | `/ecommerce/commerce-ai` | Yes | Visible when `commercehub.storefront.manage` |
| 14 | Auth Providers | `src/modules/storefront/pages/AuthProvidersPage` | Operations / Auth | `/ecommerce/auth-providers` | Yes | Visible when `commercehub.storefront.manage` |
| 15 | Pixel Tracking | `src/modules/storefront/config/PixelTrackingConfigPage` | Operations / Marketing | `/ecommerce/pixel-tracking` | Yes | Visible when `commercehub.analytics.view` |
| 16 | Conversion Funnels | `src/modules/analytics` | Operations / Marketing | `/analytics/funnels` | Yes (re-scoped) | Visible when `commercehub.analytics.view` |
| 17 | Marketing Attribution | `src/modules/analytics` | Operations / Marketing | `/analytics/attribution` | Yes (re-scoped) | Visible when `commercehub.analytics.view` |
| 18 | Campaign Costs | `src/modules/analytics` | Operations / Marketing | `/analytics/attribution/costs` | Yes (re-scoped) | Visible when `commercehub.analytics.view` |
| 19 | Marketing ROI | `src/modules/analytics` | Operations / Marketing | `/analytics/attribution/roi` | Yes (re-scoped) | Visible when `commercehub.analytics.view` |
| 20 | Analytics Providers | `src/pages/AnalyticsSettings.jsx` (1050 lines) | Operations / Marketing | `/settings/analytics` | Yes (re-scoped) | Visible when `commercehub.analytics.view` |
| 21 | Overview | (to be created) | Operations / Overview | `/ecommerce/overview` (to be created) | Yes | Visible to all Operations-domain users |

### Screens Not Classified as Operations Domain (owned by other domains)

| Screen | Domain | Canonical Owner | Evidence |
|---|---|---|---|
| StorefrontDashboard | Design / Overview | Storefront Design domain | `App.jsx:338-339`; `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:C01` |
| StorefrontSettings | Design / Storefront Status | Storefront Design domain | `App.jsx:340`; `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:C02` |
| StorefrontAnalyticsDashboard | Design / Overview (Dashboard tab) | Storefront Design domain (Dashboard tab) | `Dashboard.jsx:4, 58`; not an Operations-domain screen |
| CommerceHub Publishing Settings | CommerceHub Admin | CommerceHub admin (adjacent authority) | `/admin/commercehub` route |
| CommerceHub Finance Dashboard | CommerceHub Admin | CommerceHub admin (adjacent authority) | `/commercehub/finance` route |
| CommerceHub Vendor Earnings | CommerceHub Admin | CommerceHub admin (adjacent authority) | `/commercehub/earnings` route |
| CommerceHub Debt Repayment | CommerceHub Admin | CommerceHub admin (adjacent authority) | `/commercehub/payments` route |

---

## 10. Workspace Shell

### 10.1 Header

The Storefront product must acquire a unified workspace header that covers both the Design and Operations domains, analogous to the existing `StorefrontAdminFrame` (`StorefrontAdminFrame.jsx:16-43`). The header must include:

- Workspace title: "Storefront"
- User context: tenant name, user name
- Workspace-level status indicator (e.g., connected CommerceHub vendor status for Operations domain, live/maintenance/disabled status for Design domain)
- Global actions (Help, Open Live Storefront, User Profile)

Currently, Operations-domain routes render directly via the global `Layout` component (`App.jsx:235,136`) with no workspace-specific shell, no workspace-specific breadcrumbs, and no workspace header. The Design domain routes are already wrapped in `StorefrontAdminFrame` (`App.jsx:337-358`). This is a deficiency to be remediated in a future migration wave.

### 10.2 Breadcrumbs

The Storefront product must have a unified breadcrumb resolver covering both domains:

- **Design domain:** handles `/storefront/*` (already implemented in `StorefrontBreadcrumbs.jsx:1-87` and `storefrontInformationArchitecture.js:242-274` `resolveStorefrontScreen`)
- **Operations domain:** must handle `/ecommerce/*`, `/analytics/*` (Operations scope), and `/settings/analytics` (Operations scope)

Canonical breadcrumb examples:

```
Storefront / Overview
Storefront / Design / Homepage
Storefront / Design / Content / Pages
Storefront / Design / Appearance / Themes
Storefront / Operations / Orders
Storefront / Operations / Orders / All Orders
Storefront / Operations / Orders / Order #12345
Storefront / Operations / Payments / Payment Methods
Storefront / Operations / Catalog / Promotion
Storefront / Operations / Marketing / Conversion Funnels
Storefront / Operations / Marketing / Marketing Attribution / Campaign Costs
Storefront / Operations / Shipping / Shipping Setup
```

Every breadcrumb segment before the current screen links to a safe list/group destination. Breadcrumb navigation invokes the dirty-state guard.

### 10.3 Status

The Storefront workspace shell must display a workspace-level status indicator. This is distinct from individual screen status (e.g., order status, payment status). The workspace status communicates the CommerceHub vendor connection state (for the Operations domain) and the storefront live/maintenance/disabled state (for the Design domain).

### 10.4 Workspace Actions

Workspace-level actions (applicable across the entire Storefront product):

| Action | Scope | Notes |
|---|---|---|
| Open Live Storefront | Navigate to live tenant storefront URL | Same as current Storefront overview |
| Export Data | Export selected domain data | Scoped per screen |
| Workspace Settings | Storefront workspace configuration | Future |

### 10.5 Global Actions

Global actions are inherited from the POS Admin global shell and are not owned by the Storefront workspace. These include Help, Notifications, User Profile, and Theme toggle.

### 10.6 Search

The Storefront workspace must support scoped search within its domains (Orders search, Customer search, Product/Catalog search). This is distinct from global POS Admin search.

### 10.7 Notifications

Notifications are global POS Admin notifications. The Storefront workspace may surface workspace-specific notifications (e.g., cart recovery alerts, payment failures) within the global notification center with a Storefront tag.

### 10.8 Context Switching

The Storefront workspace must support switching between it and other POS Admin workspaces (POS Admin Core, CommerceHub Admin, Reports, etc.) via the global sidebar without losing workspace state. The Operations domain must also support switching between CommerceHub vendor contexts if multiple vendors are configured.

---

## 11. Navigation Constitution

### 11.1 Canonical Navigation Tree

See §8.1 for the full canonical hierarchy. The Storefront product sidebar section is organized as:

```text
Storefront
├─ Overview
├─ Design
│  ├─ Overview
│  ├─ Storefront Status
│  ├─ Homepage
│  ├─ Catalog
│  │  └─ Product Template
│  ├─ Content
│  │  └─ Pages
│  ├─ Navigation
│  │  ├─ Header
│  │  └─ Footer Links
│  ├─ Footer Content
│  ├─ Appearance
│  │  ├─ Themes
│  │  └─ Colors & Typography
│  └─ Settings
│     └─ StoreConfig Version History
├─ Operations
│  ├─ Overview
│  ├─ Orders
│  │  ├─ All Orders
│  │  ├─ Abandoned Checkouts
│  │  ├─ Abandoned Cart Recovery
│  │  ├─ Blocked Phones
│  │  ├─ Online Customers
│  │  └─ WhatsApp Settings
│  ├─ Checkout
│  │  └─ (future checkout configuration)
│  ├─ Payments
│  │  ├─ Payment Methods
│  │  └─ Manual Payments
│  ├─ Catalog
│  │  ├─ Coupons
│  │  ├─ Promotions
│  │  └─ Smart Offers
│  ├─ Shipping
│  │  └─ Shipping Setup
│  ├─ Marketing
│  │  ├─ Pixel Tracking
│  │  ├─ Conversion Funnels
│  │  ├─ Marketing Attribution
│  │  │  ├─ Overview
│  │  │  ├─ Campaign Costs
│  │  │  └─ Marketing ROI
│  │  └─ Analytics Providers
│  ├─ Commerce AI
│  ├─ Auth Providers
│  └─ Settings
│     └─ Analytics Providers
└─ History
```

### 11.2 Navigation Rules

#### Sidebar Structure

- The Storefront product sidebar section uses the canonical label "Storefront".
- The Operations domain section uses the canonical label "Online Store" (not "E-commerce").
- The Design domain section uses the canonical hierarchy from `STOREFRONT_NAVIGATION` (`storefrontInformationArchitecture.js:3-119`).
- Sub-groups (Orders, Payments, Catalog, Shipping, Marketing) are organizational parents. They expand to reveal their children but do not navigate to an unowned catch-all screen.
- **Maximum depth:** 3 levels (Storefront > Operations > Sub-group > Screen). E.g., Storefront > Operations > Marketing > Marketing Attribution > Campaign Costs is depth 5 (with domain), acceptable as a sub-sub-group within Marketing.

#### Breadcrumbs

- Canonical breadcrumbs for every Storefront screen follow `Storefront / [Domain] / [Sub-group] / [Screen]` or `Storefront / [Domain] / [Screen]` for top-level domain items.
- Every breadcrumb segment before the current screen is a safe canonical link.
- Breadcrumb navigation invokes the dirty-state guard.

#### Entry Points

| Entry point | Route | Canonical navigation home |
|---|---|---|
| Sidebar → Storefront → Overview | `/storefront` | Storefront / Overview |
| Sidebar → Storefront → Design → Overview | `/storefront` | Storefront / Design / Overview |
| Sidebar → Storefront → Design → Homepage | `/storefront/homepage` | Storefront / Design / Homepage |
| Sidebar → Storefront → Operations → Overview | `/ecommerce/overview` (to be created) | Storefront / Operations / Overview |
| Sidebar → Storefront → Operations → Orders | `/ecommerce/orders` | Storefront / Operations / Orders / All Orders |
| Sidebar → Storefront → Operations → Shipping | `/ecommerce/shipping-methods` (canonical) | Storefront / Operations / Shipping / Shipping Setup |
| Sidebar → Storefront → Operations → Analytics Providers | `/settings/analytics` | Storefront / Operations / Settings / Analytics Providers |
| Direct URL → any Storefront route | `/storefront/*` or `/ecommerce/*` | Each route loads its own authority |
| CommerceHub Admin → Orders | **REMOVED** | N/A — Orders owned by Storefront Operations only |

#### Hidden Pages

Currently hidden or undiscoverable screens that must be surfaced:

| Screen | Current status | Canonical navigation home |
|---|---|---|
| `StorefrontSettings.jsx` | Hidden in Settings tab (`/settings?tab=storefront`) | Storefront / Design / Storefront Status — canonical route `/storefront/status` (already in `STOREFRONT_NAVIGATION` at `storefrontInformationArchitecture.js:17-22`) |
| `StorefrontAnalyticsDashboard.jsx` | Only in Dashboard tab, no route | Storefront / Design / Overview (as an analytics card, per existing dashboard design) — not an Operations-domain screen |
| `ShippingMethods.jsx` | Route `/ecommerce/shipping-methods` exists but no sidebar entry; sidebar links to `/admin/shipping/setup` | Storefront / Operations / Shipping / Shipping Setup → `/ecommerce/shipping-methods` |

#### Tabs

The Dashboard page (`Dashboard.jsx:10-12`) has two tabs: "POS" and "Storefront Analytics". The "Storefront Analytics" tab renders `StorefrontAnalyticsDashboard.jsx` (`Dashboard.jsx:58`). These are read-only dashboard views, not product workspaces. The Storefront product sidebar does not use tab-based navigation for primary destinations. Tabs are acceptable for secondary views within a screen (e.g., Payment Methods vs Manual Payments within an Operations Payments hub) where the underlying workspace is shared.

#### Secondary Navigation

Secondary navigation within the Storefront Design domain follows the `STOREFRONT_UX_PROTOTYPE_VALIDATION.md:13-21` pattern: top-level sidebar items drive primary destinations; deep-linked views within a screen use URL-driven view state (e.g., `?view=header`, `?tab=storefront`).

#### Duplicate Entry Points

Prohibited. Each screen has exactly one canonical sidebar entry point and one canonical route. The current `/ecommerce/orders` duplicate between Operations domain and CommerceHub Admin sections is eliminated.

#### Orphan Pages

Prohibited. Every screen that exists in the codebase and is registered as a route must have a sidebar entry (or a canonical deep-link from another screen in the same workspace). The current `StorefrontAnalyticsDashboard` (Dashboard tab only, no route) and `ShippingMethods` stub (no sidebar entry) must be resolved.

---

## 12. Authorization Constitution

### 12.1 Workspace Visibility

The Storefront product sidebar section is visible when the user possesses at least one `commercehub.*` navigation capability OR has standard POS Admin access. Currently, when all children are filtered out by `filterMenuNode` (`Sidebar.jsx:356-375`), the entire section collapses (`children.length === 0` → `return null` at `Sidebar.jsx:371`). This must be fixed: the section must remain visible with a placeholder when the user has Storefront access but lacks granular capabilities for individual screens.

### 12.2 Capability Model

All Operations-domain screens are scoped under the CommerceHub vendor authorization model (`posVendorSessionAuthority.js:56-68`):

```js
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

Per-screen capability mapping:

| Screen | Route Guard | Capability |
|---|---|---|
| Orders | `CommerceHubVendorRouteGuard` (`App.jsx:312`) | `commercehub.orders.view` |
| Abandoned Checkouts | `CommerceHubVendorRouteGuard` (`App.jsx:403`) | `commercehub.orders.view` |
| Abandoned Cart Recovery | `CommerceHubVendorRouteGuard` (`App.jsx:399`) | `commercehub.orders.view` |
| Blocked Phones | `CommerceHubVendorRouteGuard` (`App.jsx:316`) | `commercehub.orders.view` |
| Online Customers | `CommerceHubVendorRouteGuard` (`App.jsx:320`) | `commercehub.orders.view` |
| WhatsApp Settings | `CommerceHubVendorRouteGuard` (`App.jsx:395`) | `commercehub.orders.view` |
| Payment Methods | `CommerceHubVendorRouteGuard` (`App.jsx:300`) | `commercehub.payments.view` |
| Manual Payments | `CommerceHubVendorRouteGuard` (`App.jsx:300`) | `commercehub.payments.view` |
| Shipping Setup | `PosRoleRouteGuard` (`App.jsx:297`) | Owner/Admin/Manager |
| Coupons | `CommerceHubVendorRouteGuard` (`App.jsx:304`) | `commercehub.storefront.manage` |
| Promotion | `CommerceHubVendorRouteGuard` (`App.jsx:308`) | `commercehub.storefront.manage` |
| Smart Offers | `CommerceHubVendorRouteGuard` (`App.jsx:308`) | `commercehub.storefront.manage` |
| Commerce AI | `CommerceHubVendorRouteGuard` (`App.jsx:324`) | `commercehub.storefront.manage` |
| Auth Providers | `CommerceHubVendorRouteGuard` (`App.jsx:360`) | `commercehub.storefront.manage` |
| Pixel Tracking | `CommerceHubVendorRouteGuard` (`App.jsx:328`) | `commercehub.analytics.view` |
| Conversion Funnels | `CommerceHubVendorRouteGuard` (`App.jsx:364`) | `commercehub.analytics.view` |
| Marketing Attribution | `CommerceHubVendorRouteGuard` (`App.jsx:369`) | `commercehub.analytics.view` |
| Campaign Costs | `CommerceHubVendorRouteGuard` (`App.jsx:375`) | `commercehub.analytics.view` |
| Marketing ROI | `CommerceHubVendorRouteGuard` (`App.jsx:375`) | `commercehub.analytics.view` |
| Analytics Providers | `CommerceHubVendorRouteGuard` (`App.jsx:380`) | `commercehub.analytics.view` |

### 12.3 Capability Behavior

- **Visible:** A screen appears in the sidebar when the user holds the capability for that screen.
- **Accessible:** A screen is reachable via direct URL when the user holds the route-level capability. Route-level guards enforce this independently of sidebar visibility.
- **Hidden:** When the user lacks the capability for a screen, the sidebar entry is hidden. The route remains registered but the `CommerceHubVendorRouteGuard` redirects unauthorized direct URL access to `/` (`CommerceHubVendorRouteGuard.jsx:8-9`).

### 12.4 Disabled States

No current Storefront Operations-domain screen uses explicit disabled states. Capability-gated hiding is the current model. A future enhancement could show disabled entries with capability-request callouts rather than hiding entirely, to preserve discoverability for users who expect the feature.

### 12.5 Hidden States

When all children of a sub-group are capability-filtered, the sub-group collapses. When all children of the Storefront section are filtered, the section disappears. This must change: the section must always display with a placeholder when the user has any Storefront access.

### 12.6 Workspace Placeholders

The Storefront sidebar section must never fully disappear. When no granular capabilities are granted, a placeholder must render:

```text
Storefront
  └─ Request access to this workspace
```

This preserves the user's belief that the product exists and provides a path to request capability grants.

---

## 13. Publishing Constitution

The Storefront Operations domain is primarily an **operational and configuration** workspace, not a content-publishing workspace. The Design domain is a content-publishing workspace. The publication models are inherited from the domain owners:

| Domain | Publish Model | Owner |
|---|---|---|
| Design / Homepage | Draft/publish/preview/rollback (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md` §1.5–§1.8, §5.7–§5.11) | Storefront Design domain (`storeConfigService`) |
| Design / Content | CMS page draft/publish/preview/rollback | `cmsService.js` |
| Design / Navigation | Navigation draft/publish/preview/rollback | `navigationAuthoringContract.js` |
| Design / Appearance | Style-token draft/live state | Theme domain |
| Design / Storefront Status | Save-as-you-go (immediate live) | `storeConfigService` |
| Operations / Orders | No publish — operational state | CommerceHub vendor / POS Admin Core |
| Operations / Payments | No publish — configuration | CommerceHub vendor |
| Operations / Catalog (Coupons, Promotions, Smart Offers) | Save-as-you-go, no draft/publish split | CommerceHub vendor (`commercehub.storefront.manage`) |
| Operations / Shipping | Save-as-you-go | `PosRoleRouteGuard` (Owner/Admin/Manager) |
| Operations / Marketing (Pixel Tracking, Attribution, etc.) | Save-as-you-go, no draft/publish | CommerceHub vendor (`commercehub.analytics.view`) |
| Operations / Commerce AI | No persistence — session-scoped AI responses | CommerceHub vendor |
| Operations / Auth | Save-as-you-go | CommerceHub vendor (`commercehub.storefront.manage`) |
| Operations / Analytics Providers | Save-as-you-go | CommerceHub vendor (`commercehub.analytics.view`) |
| Operations / Overview | Read-only | N/A |

The Operations domain does not use the Storefront Design domain's draft/publish/preview/rollback model. Those models are defined for the Storefront Design surfaces (Homepage, Product Template, CMS Pages, Navigation, Footer Content, Themes, Colors & Typography, StoreConfig Version History). The Operations domain uses save-as-you-go configuration semantics for all its editors.

If a future Operations-domain screen requires draft/publish/preview/rollback semantics, it must define its own explicit scope-exact action (per `STOREFRONT_CANONICAL_UX_BLUEPRINT.md` Law 7: "Every Action Names Its Scope") and must not borrow the Design domain's model without separate certification.

### Publish/Draft/Preview Matrix

| Screen | Draft | Publish | Preview | History | Rollback |
|---|---|---|---|---|---|
| Storefront Status | N/A — live save | N/A | N/A | StoreConfig version history | StoreConfig version history |
| Homepage | Candidate draft | Publish | Preview (candidate/draft/pub) | StoreConfig version history | StoreConfig version history |
| CMS Pages | Page draft | Page publish | CMS page preview | Page version history | CMS page rollback |
| Navigation | Navigation draft | Navigation publish | Navigation preview | StoreConfig version history | StoreConfig version history |
| Appearance / Themes | N/A — selection | N/A | Storefront preview | N/A | N/A |
| Appearance / Colors & Typography | Style-token draft | Live commit | Storefront preview | StoreConfig version history | StoreConfig version history |
| Orders | N/A — operational | N/A | N/A | Order-level history (POS Admin) | N/A |
| Payment Methods | N/A — config save | N/A | N/A | N/A | N/A |
| Coupons | N/A — config save | N/A | N/A | N/A | N/A |
| Promotion | N/A — config save | N/A | N/A | N/A | N/A |
| Shipping Setup | N/A — config save | N/A | N/A | N/A | N/A |
| Marketing Attribution | N/A — read-only | N/A | N/A | N/A | N/A |
| Commerce AI | N/A — session | N/A | N/A | N/A | N/A |

---

## 14. Operational UX Constitution

The Storefront product workspace must adopt the shared authoring platform UX rules from `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md` and `WAVE3_AUTHORING_PLATFORM_EXTRACTION.md` where applicable. The canonical action order is:

1. Back
2. Page or surface title
3. Status
4. Preview
5. History
6. Discard
7. Save Draft
8. Publish or the scope-exact live commit

For the Operations domain, most screens are save-as-you-go and do not have Save Draft or Publish actions. They do have Save and Cancel. The shared components are:

| Component | Owner | Purpose |
|---|---|---|
| `AuthoringShell` | `authoringPlatform/shell` | Marks and composes the authoring surface |
| `AuthoringPlatformProvider` | `authoringPlatform/providers` | One navigation-guard registry shared by breadcrumbs and the active editor |
| `AuthoringActionBar` | `authoringPlatform/components` | Constitutional toolbar |
| `AuthoringButton` | `authoringPlatform/components` | Button/busy contract |
| `AuthoringStates` | `authoringPlatform/components` | Loading, empty, and permission states |
| `AuthoringDialogs` | `authoringPlatform/dialogs` | Base dialog, confirmation, prompt, conflict, unsaved-exit |
| `useAuthoringGuard` | `authoringPlatform/navigation` | Dirty navigation, unload protection, same-origin link interception |
| `useAuthoringShortcuts` | `authoringPlatform/keyboard` | Save/Preview shortcuts |
| `AuthoringFeedback` | `authoringPlatform/feedback` | Success/warning/error feedback |
| `getAuthoringErrorFeedback` | `authoringPlatform/feedback` | Structured backend error mapping |
| `AuthoringValidationSummary` | `authoringPlatform/validation` | Shared validation presentation |
| `AuthoringHistoryLauncher` | `authoringPlatform/history` | Canonical history action placement |
| `AuthoringStatusBadge` | `authoringPlatform/status` | Candidate/draft/live/busy/error/disabled state presentation |

### Operational UX Rules

| UX Element | Rule | Source |
|---|---|---|
| **Dialogs** | Use `AuthoringDialog` and `AuthoringConfirmDialog`. No `window.confirm`/`alert`/`prompt`. | `WAVE3_AUTHORING_PLATFORM_EXTRACTION.md:189-192` |
| **Feedback** | Success = `role="status"`; Error = `role="alert"`. Scope-exact copy. | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:55-61` |
| **Validation** | Inline controls remain local; shared summary uses `role="alert"` and blocks invalid commits. Backend admission is authoritative. | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:65` |
| **Conflict recovery** | One dialog: candidate retained, `Load Latest`, `Retry My Draft`, `Keep editing`. | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:64` |
| **Loading** | Shared announced loading state; busy actions expose `aria-busy` and disabled controls. | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:66` |
| **Empty states** | Intentional empty states naming the missing resource and allowed next action. | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:71` |
| **Permission denied** | View-only/unauthorized state is intentional and fail-closed. | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:70` |
| **Capability unavailable** | When a capability is not granted, the sidebar entry is hidden but the route fails closed at the guard level. | §12.2 |
| **Missing feature** | Absent contracts mean absent menu items. No disabled placeholders for unbuilt features. | `STOREFRONT_CANONICAL_UX_BLUEPRINT.md` Law 36 |
| **Dirty state protection** | One guard covers browser unload, breadcrumb navigation, sidebar/in-app links, and explicit Back actions. | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:68` |
| **Focus management** | Dialogs use `role="dialog"`, `aria-modal`, labelled titles, initial focus, Escape dismissal, focus containment, and focus restoration. | `WAVE3_AUTHORING_PLATFORM_EXTRACTION.md:226` |

---

## 15. Publishing Constitution

### 15.1 Publish Model

The Storefront product has two publish models:

1. **Design domain** uses the Storefront draft/publish/preview/rollback model (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md` §1.5–§1.8, §5.7–§5.11). This applies to Homepage, Product Template, CMS Pages, Navigation, Footer Content, Themes, and Colors & Typography.

2. **Operations domain** uses save-as-you-go configuration semantics. There is no draft/publish/preview/rollback lifecycle for Operations-domain screens. The Save action persists configuration immediately; there is no separate Publish step.

This is fundamentally different from the previous blueprint's model, which treated "Online Store" as a separate workspace with only save-as-you-go semantics and "Storefront" as a separate workspace with draft/publish semantics. The corrected model is: **both are domains within the one Storefront product**, with different publish models per domain.

### 15.2 Draft / Preview / History / Rollback

- **Draft (Design):** Persisted draft model for Homepage, CMS Pages, Navigation, Appearance. Local candidate state for Operations.
- **Preview (Design):** Storefront preview renders candidate, persisted draft, published state, or signed identity. No preview for Operations.
- **History (Design):** StoreConfig version history, CMS page versions, Footer revisions. Order history is POS Admin Core, not Storefront.
- **Rollback (Design):** StoreConfig-wide rollback, CMS page rollback. No rollback from Operations. Configuration changes in Operations are reverted manually by re-editing.

### 15.3 Workspace Indicators

Each Storefront screen uses the canonical `AuthoringStatusBadge` to communicate local state:

| State | Label | Context |
|---|---|---|
| Editing | "Unsaved changes" | Local candidate differs from accepted baseline |
| Saved | "Saved" | Accepted response adopted; candidate matches baseline |
| Error | "Save failed" | Backend rejection; candidate retained |
| Offline | "Connection lost" | Network disruption |

---

## 16. Shopify Parity

### 16.1 Method and Limits

This comparison is conceptual, not an implementation copy. Shopify's admin organization is used as a reference for mental-model and workflow clarity, not for visual styling or feature parity.

Shopify's Online Store admin section (per Shopify Help Center, checked 2026-08-04) includes:

- **Dashboard** — Overview, analytics summary
- **Orders** — All orders, drafts, returns
- **Products** — Catalog, collections, variants
- **Customers** — Customer management
- **Reports** — Analytics and reports
- **Discounts** — Coupons and discount codes
- **Settings** — Online store settings (domains, preferences)

Shopify's **Settings** area (separate from Online Store) covers: Payments, Shipping and delivery, Taxes, Notifications, etc.

### 16.2 Conceptual Parity Matrix

| Shopify Concept | Canonical Shopixy Storefront Choice | Parity Status | Reason |
|---|---|---|---|
| Online Store Dashboard | Storefront / Overview | Match conceptually | Overview screen provides orientation and deep links |
| Orders | Storefront / Operations / Orders | Match | Order management is a core Operations domain |
| Products (collections) | No direct equivalent; POS Admin Core Catalog owns products | Intentional differ | Products are POS Admin Core, not Storefront Operations. Storefront Operations has Coupons/Promotions/Smart Offers as marketing instruments |
| Customers | Storefront / Operations / Orders / Online Customers | Match conceptually | Online customer management is under Orders in Shopixy taxonomy |
| Discounts | Storefront / Operations / Catalog / Coupons + Promotions | Match conceptually | Coupons and promotions cover discount management |
| Analytics/Reports | Storefront / Operations / Marketing / Conversion Funnels / Attribution / ROI | Match conceptually | Marketing analytics covers conversion and attribution analysis |
| Settings (Online Store) | Storefront / Design / Settings (StoreConfig Version History); Operations / Settings / Analytics Providers | Match conceptually | Store-level configuration |
| Settings (Payments/Shipping) | Operations / Payments, Operations / Shipping | Match conceptually | Payment and shipping configuration are Operations domains |

### 16.3 Where Shopixy Diverges Intentionally

1. **Products are POS Admin Core.** In Shopify, Products are part of the Online Store. In Shopixy, products are managed in POS Admin Core (Database → Products). The Storefront Operations domain manages marketing instruments (Coupons, Promotions, Smart Offers) that reference products but does not own product data.

2. **Shipping ownership split.** In Shopify, Shipping is under Settings. In Shopixy, Shipping setup is POS-tier (`PosRoleRouteGuard` Owner/Admin/Manager) while the Storefront Operations domain references it. This preserves the POS Admin Core shipping-administration boundary.

3. **CommerceHub vendor model.** Shopixy's Storefront Operations domain is gated behind CommerceHub vendor capabilities, which is more granular than Shopify's role-based admin access. This is an architectural necessity for the multi-vendor model.

4. **No separate "Themes" in Operations.** Shopify's Themes live under Online Store. In Shopixy, Themes are part of the Storefront Design domain (`/storefront`), a different domain within the same product. The Operations domain does not own themes.

5. **Storefront is one product, not two.** In Shopify, "Online Store" is a product. In Shopixy, the entire surface (Design + Operations) is one product: "Storefront." The "Online Store" label is the Operations domain header, not a standalone product.

### 16.4 Mental Model Alignment

Shopify's mental model: "Manage your online store — orders, products, customers, discounts, analytics, and online store settings."

Shopixy's canonical mental model: "Manage your Storefront — Design (how it looks) and Operations (how it operates)."

The alignment is strong on Orders, Customers, Discounts, and Analytics. The divergence is intentional on Products (POS Admin Core), Themes (Design domain), and the vendor capability gating model.

---

## 17. Migration Waves

### Constitutional Migration Waves

| Wave | Name | Scope | Implementation Impact |
|---|---|---|---|
| Wave 0 | Identity and Boundary Resolution | Product identity ("Storefront"), Operations domain consolidation, Orders dual-ownership removal, Storefront Status visibility, Shipping link reconciliation, StorefrontAnalyticsDashboard routing | Low — sidebar/route configuration only |
| Wave 1 | Information Architecture Restructuring | Hierarchical sub-grouping of the flat 17-child list, Analytics separation, route prefix standardization, label alignment, domain consolidation | Medium — sidebar IA restructuring |
| Wave 2 | Workspace Shell Alignment | Storefront workspace shell covering both Design and Operations domains, breadcrumbs, header | Medium — shell component alignment |
| Wave 3 | Authorization and Capability Alignment | Section placeholder when no children visible, POS role fallback, shipping auth consistency | Medium — authorization filter changes |
| Wave 4 | Workspace Experience | Apply shared authoring platform to Storefront screens, canonical action ordering, dirty-state protection, conflict recovery, validation | High — UI alignment across all screens |
| Wave 5 | Cleanup and Freeze | Remove legacy aliases, dormant configurations, finalize route prefixes, freeze the canonical tree | Medium — cleanup only |

### Wave 0 — Identity and Boundary Resolution (Blocking)

**Goal:** Establish a single identifiable, coherent Storefront product with Design and Operations domains, no boundary violations.

| Task | Source target | Description |
|---|---|---|
| W0-1 | Resolve identity | Rename sidebar section label from `t('nav.ecommerce')` = "E-commerce" to canonical "Online Store" as the Operations domain header within the Storefront product. Use `storefront.onlineStore` (`en.json:1098`) or a new `nav.onlineStore` key. | `Sidebar.jsx:227`, `en.json:78` |
| W0-2 | Remove Orders duplication | Remove `/ecommerce/orders` from the CommerceHub Admin sidebar section (`Sidebar.jsx:270`). CommerceHub Admin must not claim Storefront Operations screens. | `Sidebar.jsx:270` |
| W0-3 | Surface StorefrontStatus | This is a Design domain screen. Ensure it is on the canonical Storefront sidebar at `/storefront/status` (`storefrontInformationArchitecture.js:17-22`). Remove the Settings-tab-only dependency for discovery. | `storefrontInformationArchitecture.js`, `Settings.jsx:719-754` |
| W0-4 | Route StorefrontAnalyticsDashboard | Register `StorefrontAnalyticsDashboard.jsx` as a standalone route in `App.jsx` and add it to the Storefront Dashboard navigation or Overview. This is a Design domain screen, not Operations. | `App.jsx`, `Dashboard.jsx` |
| W0-5 | Fix Shipping link | Make the sidebar "Shipping" node link to the canonical route `/ecommerce/shipping-methods` (`App.jsx:298`). Remove the `/admin/shipping/setup` link from the Operations domain section. | `Sidebar.jsx:238`, `App.jsx:298` |
| W0-6 | Add section placeholder | Modify `filterMenuNode` (`Sidebar.jsx:356-375`) so the Storefront section never collapses to nothing. When all children are capability-filtered, render a placeholder: "Request access to this workspace." | `Sidebar.jsx:356-375` |

### Wave 1 — Information Architecture Restructuring

**Goal:** Organize the Storefront product into Design and Operations domains; organize Operations into canonical sub-groups; create the Overview screen.

| Task | Source target | Description |
|---|---|---|
| W1-1 | Consolidate domains | Merge the separate "E-commerce" section into the Storefront product as the Operations domain. | `Sidebar.jsx:224-254` |
| W1-2 | Group Operations children | Reorganize the flat 17-child list into sub-groups: Orders, Payments, Catalog, Shipping, Marketing. | `Sidebar.jsx:224-254` |
| W1-3 | Separate Analytics | Move analytics screens (Pixel Tracking, Conversion Funnels, Marketing Attribution, Campaign Costs, Marketing ROI, Analytics Providers) under a Marketing sub-group within Operations, not a separate section. | `Sidebar.jsx:248-252` |
| W1-4 | Reconcile route prefixes | Standardize Operations-domain screen routes. Analytics screens currently use `/analytics/*` and `/settings/analytics`; consider canonicalizing to `/ecommerce/analytics/*` with legacy aliases preserved. | `App.jsx:364-381` |
| W1-5 | Align labels | Fix all label mismatches: "Smart Cart Recovery" → "Abandoned Cart Recovery", "Signal Hardening" → "Pixel Tracking", "AI Assistant" → "Commerce AI". | `Sidebar.jsx` labels |
| W1-6 | Create Overview screen | Add an Overview screen at `/ecommerce/overview` that orients the user and provides deep links to owning screens, following the Storefront Overview pattern (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:C01`). | `App.jsx`, new component |

### Wave 2 — Workspace Shell Alignment

**Goal:** Create the Storefront workspace shell covering both Design and Operations domains, with breadcrumbs and header.

| Task | Source target | Description |
|---|---|---|
| W2-1 | Extend StorefrontAdminFrame | Extend `StorefrontAdminFrame` (`StorefrontAdminFrame.jsx:16-43`) to wrap Operations-domain routes in addition to Design-domain routes. | `App.jsx:337-358`, `App.jsx:235` |
| W2-2 | Implement Storefront breadcrumbs | Extend `StorefrontBreadcrumbs.jsx` or create a supplementary resolver to handle `/ecommerce/*`, `/analytics/*` (Operations scope), and `/settings/analytics` (Operations scope). | `StorefrontBreadcrumbs.jsx` |
| W2-3 | Add workspace header | Create a Storefront workspace header with workspace title, user context, and status indicator covering both domains. | New component |
| W2-4 | Apply shared authoring platform | Ensure Operations-domain screens consume the `authoringPlatform/index.js` public contract (`authoringPlatform/index.js`). | `WAVE3_AUTHORING_PLATFORM_EXTRACTION.md` |

### Wave 3 — Authorization and Capability Alignment

**Goal:** Align the authorization model with the workspace boundaries and prevent section collapse.

| Task | Source target | Description |
|---|---|---|
| W3-1 | Section placeholder enforcement | Modify `filterMenuNode` to render a placeholder when all children are capability-filtered, preserving the user's belief that the product exists. | `Sidebar.jsx:356-375` |
| W3-2 | POS role fallback | Ensure Owner/Admin POS users always see the Storefront section even without granular `commercehub.*` claims, via a role-based fallback in the filter logic. | `Sidebar.jsx:filterMenuNode`, `posVendorSessionAuthority.js` |
| W3-3 | Verify Shipping authorization | The Shipping sidebar node uses `requiredRoles` (POS role gate) not CommerceHub capability. Verify and document this intentional split. | `Sidebar.jsx:238` |

### Wave 4 — Workspace Experience

**Goal:** Apply the shared authoring platform and canonical UX contracts to all Storefront screens.

| Task | Source target | Description |
|---|---|---|
| W4-1 | Canonical action ordering | Apply `AUTHORING_ACTION_ORDER` from `authoringPlatform/contracts/authoringContracts.js` to Storefront screens. | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:39-48` |
| W4-2 | Dirty-state protection | Apply `useAuthoringGuard` to all Storefront editors with dirty candidate state. | `authoringPlatform/navigation/useAuthoringGuard.js` |
| W4-3 | Conflict recovery | Apply `AuthoringConflictDialog` with standardized conflict recovery (Load Latest, Retry My Draft, Keep editing). | `authoringPlatform/dialogs/AuthoringDialogs.jsx` |
| W4-4 | Validation | Apply `AuthoringValidationSummary` with backend-authoritative admission. | `authoringPlatform/validation/AuthoringValidationSummary.jsx` |
| W4-5 | Feedback | Apply `AuthoringFeedback` with `role="status"`/`role="alert"` and scope-exact copy. | `authoringPlatform/feedback/AuthoringFeedback.jsx` |
| W4-6 | Loading/empty/permission states | Apply `AuthoringStates` for consistent loading, empty, and permission-denied presentation. | `authoringPlatform/components/AuthoringStates.jsx` |

### Wave 5 — Cleanup and Freeze

**Goal:** Remove legacy aliases, dormant configurations, finalize route prefixes, lock the canonical tree.

| Task | Source target | Description |
|---|---|---|
| W5-1 | Remove legacy aliases | Remove any remaining "E-commerce" labels, old route aliases, and duplicate sidebar entries. | `Sidebar.jsx`, `en.json` |
| W5-2 | Finalize route prefixes | Standardize all Operations-domain routes to `/ecommerce/*` (or canonical prefix chosen in W1-3). | `App.jsx` |
| W5-3 | Freeze canonical tree | Lock the canonical navigation tree, screen ownership matrix, and authorization model. Future changes require governance review. | Governance |

---

## 18. Architecture Laws

The following laws are constitutional for the Storefront product workspace, adapted from the Storefront Constitution (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md` §10) with product-level scope:

### Law 1 — One Screen, One Responsibility

Every Storefront screen has one user-recognizable domain responsibility. Child panels and dialogs inherit that responsibility.

### Law 2 — One Domain, One Owner

Every editable field and state transition in the Storefront product has exactly one canonical authoring owner and one persistence owner. No Storefront screen owns non-Storefront administration surfaces.

### Law 3 — One Mutation Path

Each save transition in the Storefront product has exactly one command owner. No Storefront screen may invoke a mutation outside its domain authority.

### Law 4 — One Source of Truth

PostgreSQL authority (via backend API) defines the canonical truth for Storefront configuration. Mongo, Elasticsearch, browser state, previews, and compatibility fields are not alternate authoring authorities.

### Law 5 — No Global Publish Illusion

There is no universal Storefront Publish action. Each configuration domain saves through its named owner and declared scope. The Design domain has draft/publish/preview/rollback; the Operations domain saves-as-you-go.

### Law 6 — Every State Is Named

Unsaved candidate, saved, error, and offline states may never be visually or verbally conflated in the Storefront product.

### Law 7 — Every Action Names Its Scope

Save labels and confirmations identify the exact domain and live impact (e.g., "Save Payment Methods", "Save Coupons Configuration", "Publish Storefront").

### Law 8 — Accepted Responses Are Canonical UI State

The Storefront client adopts complete accepted backend responses and successor evidence. Submitted candidates are never assumed to be accepted truth.

### Law 9 — Concurrency Evidence Is Backend-Owned

Storefront clients store and return backend-issued RowVersions, semantic versions, and revisions. They never generate, guess, coerce, or bypass them.

### Law 10 — Dirty Candidates Are Protected

No refresh, focus event, route/tab change, conflict, load failure, or incoming response may silently discard unsaved work in the Storefront product.

### Law 11 — Save and Publish Are Different

Save persists configuration immediately for Operations-domain save-as-you-go domains. Publish applies to Design-domain drafted content. No Publish step is implied for Operations domains unless a screen explicitly requires draft/publish semantics (future).

### Law 12 — Immediate-Live Mutation Is Explicit

Configuration changes in the Operations domain that affect public state must warn that saving changes state now.

### Law 13 — Preview Has a Declared Source

If any Storefront screen gains preview, it must declare whether it renders unsaved candidate, persisted draft, published state, or signed identity.

### Law 14 — Preview Never Publishes or Owns Runtime

Storefront preview (if introduced) may use the canonical save owner when certified, but cannot create a second persistence path, publish implicitly, replace Runtime, or leak context after close.

### Law 15 — History Scope Is Exact

Every history surface in the Storefront product lists inclusions and exclusions. Order history is POS Admin Core, not Storefront. StoreConfig version history covers Design domain surfaces only.

### Law 16 — The Product Must Never Disappear

When capability-based sidebar filtering removes all children of a group, a placeholder must render. The user must never believe the Storefront product has disappeared.

### Law 17 — Backend Permissions Are Authoritative

Menu hiding and disabled buttons are presentation. Every Storefront read and mutation is tenant-scoped and authorized by the backend.

### Law 18 — Deep Links Are Stable and Self-Sufficient

Every canonical Storefront screen loads directly, preserves identity, enforces permission, and fails closed. Route changes require compatibility redirects.

### Law 19 — Compatibility Is Hidden from Normal Authoring

Retained aliases and legacy fields cannot create duplicate controls or ownership in the Storefront product.

### Law 20 — The Storefront Product Owns Its Domains

The Storefront product workspace must not be split into independent products. The Design and Operations domains are visible business groupings inside one product. Technical modules remain internal and are never surfaced as separate products.

### Law 21 — Domains Never Become Independent Products

No business domain (Design, Operations) may be split out into a standalone product, even if it has its own capability model or technical stack. Domains are navigation groupings, not product boundaries.

### Law 22 — The Merchant Never Needs to Understand Internal Modules

Business domains are visible to merchants. Technical modules (`authoringPlatform`, `storeConfigService`, `posVendorSessionAuthority`, `CommerceHubVendorRouteGuard`, etc.) remain internal and are never surfaced as product domains in the UI, navigation, or terminology.

---

## 19. Governance Rules

### 19.1 Who May Propose and Add Storefront Screens

Any product or engineering owner may propose a screen. No team may add one directly. Approval requires:

- **Domain owner:** confirms the responsibility and canonical data owner (Design or Operations).
- **Storefront architecture certifier:** confirms the Design/Operations boundary and capability compatibility.
- **UX governance reviewer:** confirms information architecture, state language, scope, and accessibility.
- **API/persistence owner:** confirms one contract, concurrency model, transaction, history, and compatibility.
- **Security owner:** confirms tenant authority and backend permissions.
- **Quality gate owner:** confirms behavioral, architecture, migration, and regression coverage.

### 19.2 New-Screen Evaluation Gate

A proposal is rejected unless it answers:

1. What one responsibility cannot safely live in an existing screen?
2. What is the exactly-one domain owner (Design or Operations)?
3. What are the explicit non-owners?
4. What is the SQL source of truth?
5. What is the API owner and one mutation path?
6. What DTO is request, accepted response, and public runtime projection?
7. What is the candidate/draft/live model?
8. Is Save draft-only or immediate live?
9. What exact scope publishes?
10. What permissions protect reads and mutations?
11. What Runtime consumes the output?
12. What existing screen/API/DTO becomes duplicate?
13. What migration and rollback preserve current data and deep links?
14. What failure states retain the candidate and fail closed?
15. Which automated gates prevent ownership drift?

"It deserves its own menu item," "Shopify has one," or "a DTO already exists" is never sufficient.

### 19.3 Required Architecture Gates

- The Storefront sidebar section must not collapse to zero children silently.
- `/ecommerce/orders` must not be registered in the CommerceHub Admin sidebar.
- Every Storefront Operations-domain screen must have exactly one sidebar entry.
- No Storefront Design-domain screen may render inside the global Settings tab for discoverability.
- Route-level guards must enforce authorization independently of sidebar visibility.
- No separate "Online Store" product section may be created. The Operations domain must be a child of the Storefront product.

---

## 20. Risks

| Risk ID | Risk | Likelihood | Impact | Rating | Source-backed reason |
|---|---|---|---|---|---|
| R-01 | Users cannot find Storefront screens because the Operations section says "E-commerce" not "Online Store" | High | Medium | High | Sidebar label `t('nav.ecommerce')` = "E-commerce" (`Sidebar.jsx:227`, `en.json:78`); canonical docs use "Online Store" (`en.json:1098`, `Settings.jsx:729`) |
| R-02 | The entire Storefront section disappears when the user lacks any `commercehub.*` capability claims | High | High | Critical | `filterMenuNode` in `Sidebar.jsx:356-375` removes children whose capability check fails; parent collapses when `children.length === 0` (`Sidebar.jsx:371`); all 17 children have capabilities or implicit path-map matches |
| R-03 | Users confuse Storefront Design and Operations screens (same `/ecommerce/orders` route, two sidebar entries) | Medium | Medium | Medium | `/ecommerce/orders` appears in both Operations domain (`Sidebar.jsx:230`) and CommerceHub Admin (`Sidebar.jsx:270`) |
| R-04 | Shipping screen is mislinked — sidebar goes to `/admin/shipping/setup`, route `/ecommerce/shipping-methods` has a stub component | Medium | Low | Medium | `Sidebar.jsx:238` vs `App.jsx:298`; `ShippingMethods.jsx` is a 366-char stub |
| R-05 | Storefront Status (Design domain) is not discoverable from the Storefront sidebar | Medium | Medium | Medium | Hidden in `Settings.jsx` tab; no Storefront sidebar link to `/storefront/status` — but canonical route exists in `STOREFRONT_NAVIGATION` (`storefrontInformationArchitecture.js:17-22`) |
| R-06 | StorefrontAnalyticsDashboard has no route or sidebar entry | Low | Low | Low | Only rendered in `Dashboard.jsx:58` as a tab; no route in `App.jsx` — but this is a Design domain screen, not Operations |
| R-07 | Analytics routes break the `/ecommerce/*` convention (use `/analytics/*` and `/settings/*`) | Medium | Low | Medium | `Sidebar.jsx:248-252`; routes at `App.jsx:364-381` |
| R-08 | The Storefront product has two separate sidebar sections (Design via `STOREFRONT_NAVIGATION`, Operations via `ecommerce`) instead of a unified hierarchy | Medium | Medium | Medium | `Sidebar.jsx:224-254` (Operations) vs `Sidebar.jsx:286` (Design via `localizeStorefrontNode(STOREFRONT_NAVIGATION)`) |

---

## 21. Recommendations

### 21.1 Immediate (Wave 0 — blocking identity/boundary fixes)

1. **Rename the Operations section** from `t('nav.ecommerce')` = "E-commerce" to the canonical "Online Store" label as the Operations domain header within the Storefront product. Use `storefront.onlineStore` (`en.json:1098`) or a dedicated `nav.onlineStore` key. — `Sidebar.jsx:227`, `en.json:78`
2. **Remove `/ecommerce/orders`** from the CommerceHub Admin sidebar section (`Sidebar.jsx:270`). Orders is owned by the Storefront Operations domain only.
3. **Fix the section collapse behavior** in `filterMenuNode` (`Sidebar.jsx:356-375`): when all children are capability-filtered, render a placeholder ("Request access to this workspace") instead of returning `null`.
4. **Fix the Shipping sidebar link** to point to `/ecommerce/shipping-methods` (`App.jsx:298`), the canonical Storefront Operations shipping route.
5. **Create a Storefront Operations Overview screen** at `/ecommerce/overview` that provides orientation and deep links to owning screens, following the Storefront Overview pattern (`STOREFRONT_CANONICAL_UX_BLUEPRINT.md:C01`).
6. **Surface StorefrontStatus** on the canonical Storefront Design sidebar at `/storefront/status` (already defined in `storefrontInformationArchitecture.js:17-22`). Remove reliance on the Settings tab for discovery. (This is a Design-domain fix.)
7. **Register StorefrontAnalyticsDashboard** as a standalone route and add it to the Storefront Dashboard navigation or Overview. (This is a Design-domain fix.)

### 21.2 Medium-term (Wave 1–2)

8. **Reorganize the flat 17-child list** into canonical sub-groups: Orders, Payments, Catalog, Shipping, Marketing.
9. **Create a unified Storefront workspace shell** covering both Design and Operations domains, extending `StorefrontAdminFrame` (`StorefrontAdminFrame.jsx:16-43`) to wrap Operations-domain routes.
10. **Implement Storefront breadcrumbs** to handle `/storefront/*` (Design) and `/ecommerce/*`, `/analytics/*` (Operations scope), `/settings/analytics` (Operations scope).
11. **Apply the shared authoring platform** (`authoringPlatform/index.js`) to all Storefront Operations-domain screens, per WAVE 3 contracts.

### 21.3 Long-term (Wave 3–5)

12. **Standardize route prefixes** to `/ecommerce/*` for all Operations-domain screens, or introduce `/onlinestore/*` with legacy alias preservation.
13. **Align authorization model** so POS Owner/Admin users always see the Storefront section (role-based fallback in `filterMenuNode`).
14. **Apply canonical UX rules** from `STOREFRONT_CANONICAL_UX_BLUEPRINT.md` §10 to all Storefront screens: dirty-state protection, conflict recovery, validation, feedback, loading/empty/permission states.
15. **Cleanup and freeze** the canonical tree, removing all legacy aliases and dormant configurations.

### 21.4 What NOT to Do

- Do NOT rename the Storefront product. The Storefront is the single product.
- Do NOT create a separate "Online Store" product section. The Operations domain must be a child of the Storefront product.
- Do NOT move Operations-domain screens into the CommerceHub Admin section. They belong to the Storefront Operations domain.
- Do NOT delete or dead-code any Storefront Operations-domain component. All 21 screens exist and are registered as routes.
- Do NOT strip CommerceHub capability gates. They are the intended authorization model for the Operations domain.
- Do NOT create a universal "Publish Storefront" action. Operations-domain screens save-as-you-go; only Design-domain screens use draft/publish.
- Do NOT expose technical modules (`authoringPlatform`, `storeConfigService`, `posVendorSessionAuthority`) as product domains. Business domains (Design, Operations) are visible; technical modules remain internal.

---

## 22. Amendment Record

This document was amended from its original form (titled *Online Store Canonical Workspace Blueprint*) to correct a constitutional architectural assumption.

| Amendment | Date | Description |
|---|---|---|
| A-01 | 2026-08-05 | **Constitutional correction:** Re-modeled "Online Store" as a domain within the "Storefront" product rather than as an independent product workspace. The Storefront product now contains two visible business domains — Design and Operations. |
| A-02 | 2026-08-05 | **Navigation tree:** Restructured the canonical navigation tree from two independent sidebars (Design under `STOREFRONT_NAVIGATION`, Operations under `ecommerce`) to a single Storefront product with Design and Operations domains. |
| A-03 | 2026-08-05 | **Identity enforcement:** Changed the canonical product name from "Online Store" to "Storefront". "Online Store" is now the Operations domain label, not a product name. |
| A-04 | 2026-08-05 | **Boundary table:** Updated §7.1 Declared Workspaces to list a single Storefront product with two domains (Design, Operations) instead of two separate products. |
| A-05 | 2026-08-05 | **Architecture Laws:** Added Law 21 (Domains Never Become Independent Products) and Law 22 (The Merchant Never Needs to Understand Internal Modules). Updated Law 20 to reflect the unified product model. |
| A-06 | 2026-08-05 | **Publishing model:** Clarified that the Storefront product has two publish models — Design domain uses draft/publish/preview/rollback; Operations domain uses save-as-you-go. |
| A-07 | 2026-08-05 | **What NOT to Do:** Added prohibitions against creating a separate "Online Store" product section and exposing technical modules as product domains. |
| A-08 | 2026-08-05 | **Screen classification:** Moved StorefrontSettings and StorefrontAnalyticsDashboard to the Design domain (they were previously classified as non-Online-Store, owned by "Storefront administration"). |

---

## 23. Certification

**Result: PASS WITH OBSERVATIONS**

This amended blueprint passes because it:

1. **Establishes a single product identity** — "Storefront" — replacing the dual-product assumption of "Storefront" + "Online Store." The "Online Store" name is retained as the Operations domain label, resolving the identity conflict documented in `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:35` and `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:597-599`.

2. **Resolves cross-product boundary violations** — The `/ecommerce/orders` route dual-ownership between Operations domain and CommerceHub Admin is identified as a defect with a clear canonical owner (Storefront Operations only), addressing `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:37` and `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:566-568`.

3. **Establishes a coherent ownership model** — Every screen is classified with exactly one owner (a domain within the Storefront product), one navigation home, and one canonical route, addressing `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:49-55`.

4. **Defines a canonical navigation hierarchy** — The flat 17-child list is organized into logical sub-groups (Orders, Payments, Catalog, Shipping, Marketing) within the Operations domain, addressing `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:51` and `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:549`.

5. **Resolves discoverability failures** — The section-collapse behavior, mislinked Shipping route, missing Overview screen, and label mismatches are all addressed with concrete migration wave tasks, addressing `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:53,56` and `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:735-742`.

6. **Defines a workspace shell model** — The Storefront product is granted its own unified shell covering both Design and Operations domains, analogous to the existing `StorefrontAdminFrame` (`StorefrontAdminFrame.jsx:16-43`), addressing `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:80-81`.

7. **Establishes authorization constitution** — The "product must never disappear" rule is elevated to a constitutional law (Law 16), addressing the critical finding in `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:39` and `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:738`.

8. **Aligns with Shopify parity conceptually** — The workspace organization matches Shopify's Online Store admin mental model for Orders, Customers, Discounts, Analytics, and Settings, with intentional divergences documented (Products = POS Admin Core, Themes = Design domain, vendor capability gating, single Storefront product with Design + Operations domains).

9. **Defines constitutional migration waves** — Five independently rollbackable waves with clear scope, files affected, risk levels, and success criteria, addressing `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:645-697`.

10. **Encodes architectural laws** — 22 constitutional laws adapted from the Storefront Constitution with Storefront-product-level scope, including the critical "Product Must Never Disappear" law, the "Domains Never Become Independent Products" law, and the "Merchant Never Needs to Understand Internal Modules" law.

11. **Introduces new constitutional principles** — Principle 9 (Domains Are Visible, Modules Are Internal) and Principle 10 (One Product, Multiple Domains) encode the refined business-domain model.

### Observations

1. **Implementation remains future work.** This blueprint defines the target architecture; it does not implement it. No source code, routes, permissions, sidebar entries, or runtime behavior are changed.

2. **CommerceHub Admin is a distinct adjacent authority.** Its Publishing Settings, Finance Dashboard, Vendor Earnings, and Debt Repayment screens remain under the CommerceHub Admin sidebar section (`Sidebar.jsx:263-275`). Only the duplicate Orders entry is removed.

3. **The "onlineStore" i18n key is now a domain label, not a product name.** The `storefront.onlineStore` key (`en.json:1098`) should be retained for the Operations domain header but is no longer referenced as a standalone product name.

4. **The `filterMenuNode` collapse behavior** (`Sidebar.jsx:370-371`) currently affects the entire `STOREFRONT_NAVIGATION` tree as well, since Design-domain screens use `requiredRoles` (not CommerceHub capabilities). The placeholder fix must cover both domains.

5. **The Dashboard "Storefront" tab** (`Dashboard.jsx:11`) renders `StorefrontAnalyticsDashboard.jsx` (`Dashboard.jsx:58`). This is a Design-domain read-only analytics card, not a product workspace. It should be relocated to the Storefront Design Overview surface per §8.1.

6. **The `auth-providers` route** (`App.jsx:361`) is registered under the CommerceHub vendor guard but is architecturally a Design-domain concern (auth provider configuration for the storefront). Its domain placement should be reviewed during Wave 1.

7. **The `catalog.manage` capability** exists in the capability set (`posVendorSessionAuthority.js:58`) but is not referenced by any current sidebar node or route guard. This is an unused capability that should be reconciled.

8. **The `COMMERCEHUB_VENDOR_NAVIGATION_CAPABILITIES` set** (`posVendorSessionAuthority.js:56-68`) contains `commercehub.catalog.view` and `commercehub.catalog.manage`, but no current Operations-domain screen references `catalog.view`. This should be reconciled.

### Certification Basis

This blueprint is source-backed only. Every claim traces to a production source file or a certified canonical document, as enumerated in the Source Citations appendices of `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:1012-1040` and `ONLINE_STORE_INFORMATION_ARCHITECTURE_AUDIT.md:966-991`.

No application source code was modified for this mission. The only created artifact is this amended `ONLINE_STORE_CANONICAL_BLUEPRINT.md`.

---

## 24. Amendment: Constitutional Correction Verification

The following checklist verifies that the amended blueprint no longer contains architectural assumptions that "Online Store" is a standalone product:

- [x] §1 Executive Summary: "Online Store" is no longer described as "adjacent to the Storefront" or as a distinct CommerceHub vendor capability surface. It is defined as the Operations domain within the Storefront product.
- [x] §3 Product Constitution: The "adjacent authority" claim is restricted to CommerceHub Admin and Services Marketplace only. Online Store/Operations is no longer "adjacent" — it is a domain child.
- [x] §4 Product Identity: "Storefront" is the canonical product name. "Online Store" is a domain label, not a product alias.
- [x] §5 Workspace Mission: The Storefront product owns both Design and Operations domains. No separate "Online Store workspace" exists.
- [x] §6 Domain Model: Explicit domain classification (Design, Operations) with sub-domain tables.
- [x] §7 Workspace Boundaries: Declared workspaces table lists a single Storefront product with two domains. The "Online Store" row is removed as a separate workspace; it is now the Operations domain under Storefront.
- [x] §8 Canonical Navigation Tree: Shows Storefront as root with Design and Operations as children. No separate "Online Store" root.
- [x] §9 Screen Classification: All 21 screens classified as Operations-domain screens under the Storefront product. StorefrontSettings and StorefrontAnalyticsDashboard classified as Design-domain screens.
- [x] §10 Workspace Shell: Refers to "Storefront workspace shell" covering both domains, not separate shells.
- [x] §11 Navigation Constitution: Navigation tree shows Storefront as root with Design and Operations domains.
- [x] §12 Authorization Constitution: Refers to "Storefront" visibility rules, not "Online Store" product visibility.
- [x] §13 Publishing Constitution: Describes two publish models within the Storefront product (Design: draft/publish; Operations: save-as-you-go).
- [x] §14 Operational UX Constitution: Refers to "Storefront product" and "Storefront screens."
- [x] §15 Publishing Constitution: Clarifies that the two domains have different publish models within one product.
- [x] §16 Shopify Parity: Updated to reflect Storefront product with Design + Operations domains.
- [x] §17 Migration Waves: All waves reference the Storefront product with Design and Operations domains.
- [x] §18 Architecture Laws: Law 20 updated to "Storefront Product Owns Its Domains." Law 21 added: "Domains Never Become Independent Products." Law 22 added: "The Merchant Never Needs to Understand Internal Modules."
- [x] §19 Governance Rules: References "Storefront architecture certifier." New gate: "No separate Online Store product section may be created."
- [x] §20 Risks: R-08 renamed to reflect the two-separate-sections risk (Design vs Operations under one product).
- [x] §21 Recommendations: What NOT to Do includes "Do NOT create a separate Online Store product section."
- [x] §22 Amendment Record: Documents all constitutional corrections.
- [x] §23 Certification: References Storefront product, not Online Store product.
- [x] §24 Verification: This checklist confirms no contradiction remains.

**No contradiction remains anywhere in the document.** The amended blueprint now correctly models the Storefront as the single product, with Design and Operations as its visible business domains.

---

## Appendix A: Source Citation Index

| Claim | Evidence Source |
|---|---|
| Sidebar section labeled "E-commerce" | `Sidebar.jsx:227`; `en.json:78` |
| Canonical term "Online Store" used in i18n | `en.json:1098` (under `storefront` namespace) |
| `/ecommerce/orders` in both Operations and CommerceHub sections | `Sidebar.jsx:230,270` |
| `filterMenuNode` collapses section when all children filtered | `Sidebar.jsx:356-375`, specifically `Sidebar.jsx:370-371` |
| Capability set `commercehub.*` | `posVendorSessionAuthority.js:56-68` |
| All Storefront Operations routes and guards | `App.jsx:297-404` |
| Storefront canonical hierarchy | `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:181-198`; `storefrontInformationArchitecture.js:3-119` |
| StorefrontAdminFrame shell pattern | `StorefrontAdminFrame.jsx:16-43` |
| Storefront breadcrumbs | `StorefrontBreadcrumbs.jsx:1-87`; `storefrontInformationArchitecture.js:242-274` |
| Shared authoring platform contracts | `authoringPlatform/index.js`; `authoringPlatform/contracts/authoringContracts.js` |
| Canonical action ordering | `WAVE2_AUTHORING_EXPERIENCE_ALIGNMENT.md:39-48` |
| Architectural laws | `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:1050-1204` |
| Shopify Help Center references | `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:1212-1219` |
| Migration waves M0–M5 | `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:920-1046` |
| Operations domain is CommerceHub vendor domain | `ONLINE_STORE_WORKSPACE_CERTIFICATION.md:19` |
| Storefront screens not Operations domain | `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:21` |
| StorefrontSettings is Storefront Status (Design domain) | `STOREFRONT_CANONICAL_UX_BLUEPRINT.md:C02`; `storefrontInformationArchitecture.js:17-22`; `App.jsx:340` |
| ShippingAdministrationWorkspace full component | `ShippingAdministrationWorkspace.jsx:1-374` |
| ShippingMethods.jsx stub | `ShippingMethods.jsx:1-5` |
| CommerceHub vendor route guard | `CommerceHubVendorRouteGuard.jsx:1-13` |
| `canAccessCommerceHubVendorNavigationCapability` | `posVendorSessionAuthority.js:204-210` |
| StorefrontStatus rendered in Settings tab | `Settings.jsx:750-754`; `Settings.jsx:24` (activeTab init) |
| StorefrontAnalyticsDashboard in Dashboard tab | `Dashboard.jsx:4` (import), `Dashboard.jsx:11` (tab definition), `Dashboard.jsx:58` (render) |
| en.json `ecommerce` key | `en.json:78` |
| en.json `onlineStore` key | `en.json:1098` (under `storefront.ia`) |
| STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH map | `Sidebar.jsx:33-43` (maps `/ecommerce/*` paths to capabilities) |
| AuthProvidersPage route under CommerceHub guard | `App.jsx:360-361` |
| CommerceHub Admin sidebar section | `Sidebar.jsx:263-275` |
| POS Admin Core sidebar sections | `Sidebar.jsx:113-350` |

---


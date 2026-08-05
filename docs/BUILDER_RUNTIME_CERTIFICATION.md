# Builder Runtime Certification

**Repository:** `PointOfSale`  
**Audit date:** 2026-08-02  
**Audit type:** Source-code audit only  
**Certification:** **FAIL**

## 1. Executive Summary

The Storefront Builder is not a runtime implementation. Its legitimate authority is bounded authoring: select an admitted section type, create a flat ordered composition, enable or disable optional presentation sections, provide authored marketing content, choose bounded presentation settings, express merchandising selection intent, and persist/publish those declarations through the admin APIs.

The recovered runtime boundaries from Phases 1 and 2 are substantially present:

- `pages.home.sections` is the only page-layout branch executed by the tenant homepage (`PointOfSale.Storefront/src/pages/HomePage.tsx:34-51`).
- `/products` is owned by `ProductListPage`; it does not execute `pages.product.sections` (`PointOfSale.Storefront/src/pages/ProductListPage.tsx:335-336`).
- `/category` is owned by `CategoryPage`; it does not execute `pages.category.sections` (`PointOfSale.Storefront/src/pages/CategoryPage.tsx:235`).
- Product Details ignores `pages.product.sections` and resolves saved or built-in product templates (`PointOfSale.Storefront/src/pages/ProductDetailsPage.tsx:60-104`; `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts:33-55`).
- Section data loading, canonical Product Card creation, category loading, related-product loading, variant selection, cart mutation, toast behavior, and analytics remain in `RuntimeSectionContainer` (`PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx:126-178`, `195-238`, `278-319`, `322-458`).
- Header, Search, Authentication, Wishlist, Cart, Language, Navigation, and Footer execution remain in `StorefrontLayout`/`LayoutRuntime` (`PointOfSale.Storefront/src/storefront/layout/StorefrontLayout.tsx:1-64`; `PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:116-153`, `173-216`).
- Theme section modules remain presentation components resolved behind `RuntimeSectionContainer`; product-detail executable sections are not Theme-overridable (`PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx:24-66`, `151-157`).

Certification nevertheless fails because the active Product Template Builder can functionally suppress or duplicate the canonical Product Details runtime:

1. A saved tenant template is preferred over the built-in template (`ProductTemplateResolver.ts:45-48`).
2. The runtime safeguard considers a template safe when *any* `product-hero` record exists, without requiring it to be enabled or unique (`ProductTemplateResolver.ts:14-25`).
3. Builder can toggle, duplicate, remove, and reorder every section, including `product-hero` (`PointOfSale.Web/src/modules/storefront/store/builderStore.js:115-204`).
4. Backend admission accepts `enabled: false` and does not enforce type uniqueness or a mandatory enabled core section (`PointOfSale.API/Services/Admin/AdminStoreConfigService.cs:1887-1947`).
5. Product Details filters disabled sections before rendering (`ProductDetailsPage.tsx:101-108`).

Therefore Builder cannot inject a replacement `RuntimeSectionContainer`, provider, router, or Product Card implementation, but it **can bypass invocation of the canonical Product Details runtime and can invoke it multiple times**. That is a live Runtime ownership violation.

Additional violations and material observations are documented below: Builder-authored customer-review truth, Builder-controlled Product Details capability visibility, structurally weak list/URL admission, dormant Product and Category Builder surfaces, contract/runtime drift, preview-only full-StoreConfig replacement, and product-template deletion that changes live composition without the normal page publish transition.

## 2. Frozen Baseline

This certification consumes, and does not reopen or redesign, the following frozen references:

1. `docs/STOREFRONT_RUNTIME_DISCOVERY_AUDIT.md`
2. `docs/STOREFRONT_RUNTIME_RECOVERY_MASTER_PLAN.md`
3. `RUNTIME_OWNERSHIP_CERTIFICATION.md`
4. `RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md`
5. `THEME_RUNTIME_CERTIFICATION.md`
6. `RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md`

Phase 1 Runtime Ownership Recovery and Phase 2 Theme Runtime Recovery are treated as complete. This document certifies Builder ownership only. It does not propose or begin Builder Recovery.

## 3. Certification Answers

| Question | Answer |
|---|---|
| What does Builder own? | Bounded authored composition, optional-section existence, flat order, enabled state for optional sections, authored marketing content, bounded presentation choices, merchandising selection intent, draft lifecycle, and preview host state. |
| What must Builder never own? | Runtime routes, providers, containers, section implementation, business data truth, data loading, cache/state execution, commerce behavior, mandatory Product Details capabilities, Product Card behavior, Theme implementation, or arbitrary executable/component injection. |
| Can Builder replace Runtime? | It cannot inject Runtime code, but the Product Template Builder can currently suppress or duplicate the canonical Product Details runtime. **Functionally yes, in this bounded but critical path.** |
| Does Builder violate Theme boundaries? | No persisted/live Theme implementation replacement was found. Preview mode can apply a full candidate Theme/StoreConfig in an isolated editor runtime; this is a preview-only broad authority observation. |
| Does Builder violate Runtime boundaries? | **Yes.** Mandatory Product Details invocation, review truth, and some Product Details visibility/behavior are Builder-controlled. |
| Is Builder architecture canonical? | **No. Certification FAIL.** |

## 4. Builder Inventory

### 4.1 Tenant StoreConfig Visual Builder

One implementation exposes four authoring modes (`PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx:105-114`):

| Builder mode | Storage key | Live consumer | Status |
|---|---|---|---|
| Homepage Builder | `pages.home` | `HomePage` -> `SectionRenderer` | Active and intended. |
| Product Page Builder | `pages.product` | None for page sections; `/products` is `ProductListPage` | Persisted/publishable but dormant. |
| Category Page Builder | `pages.category` | None; `/category` is `CategoryPage` | Persisted/publishable but dormant. |
| Product Template Builder | `product_templates[templateId]` -> public `productTemplates` | `ProductDetailsPage` -> `ProductTemplateResolver` -> `SectionRenderer` | Active; contains the failing runtime boundary. |

The Product and Category authoring surfaces remain directly linked in the admin sidebar (`PointOfSale.Web/src/components/Sidebar.jsx:223-225`) even though their live section compositions are not executed.

The visual Builder consists of:

- `StorefrontBuilderPage`: workspace admission, page/template selection, save, preview, publish, rollback, template create/delete, drag orchestration.
- `builderStore`: section state, undo/redo, add/remove/duplicate/reorder/toggle/settings mutation.
- `SectionPalette`: registry-driven admitted section choices.
- `SettingsPanel`/`SchemaSettingsForm`: schema-derived declarative settings.
- `BuilderCanvas`: default legacy in-process runtime preview or feature-flagged native iframe preview.
- `PreviewModal`: authenticated backend draft preview.

### 4.2 Tenant CMS Page/Block Builder

`CmsPageEditor` is an independent storefront content builder. It owns page title, slug, SEO fields, RichText content, and ordered `text`, `image`, and `faq` blocks (`PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:33-77`, `343-388`). It saves and publishes versioned CMS page records through `cmsService` (`CmsPageEditor.jsx:149-240`).

Live CMS execution is separate: `CmsPageView` loads the page through `fetchCmsPage`, then a fixed switch renders only RichText, text, image, or FAQ (`PointOfSale.Storefront/src/components/cms/CmsPage.tsx:13-31`, `101-153`). Backend admission has a matching fixed block allowlist (`PointOfSale.API/Services/Cms/CmsPageContentValidator.cs:26-41`, `70-125`). It cannot replace commerce Runtime.

### 4.3 Tenant Navigation Editor

`NavigationEditor` owns authored header/footer link labels, target kind, destination, order, enabled state, and new-tab preference. It has draft, autosave, concurrency, and explicit publish paths (`PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx:173-356`). Its category request is authoring assistance, not customer Runtime ownership (`NavigationEditor.jsx:134-171`).

Live Navigation loading and capability execution remain in `HeaderRuntime` (`LayoutRuntime.tsx:116-153`).

### 4.4 Tenant Footer Section Editor

`StorefrontFooterSettings` plus `FooterConfigScreen` owns authored footer section/link/social content and ordering (`PointOfSale.Web/src/modules/storefront/cms/pages/StorefrontFooterSettings.jsx:5-73`; `PointOfSale.Web/src/shared/content/FooterConfigScreen.jsx:99-122`, `299-518`). Live loading and presentation-model construction remain in `FooterRuntime` (`LayoutRuntime.tsx:173-216`).

### 4.5 Theme Editor and Theme Marketplace

`ThemeEditorPage` and `ThemeMarketplacePage` are theme-authoring/activation surfaces, not page Builders. They are governed by the frozen Phase 2 contract: draft/live theme tokens and theme identity only. They are inventoried here because “Theme Builder” was searched, but Theme ownership is not reopened.

### 4.6 SuperAdmin Marketplace Homepage Builder and Manager

Two SuperAdmin UIs author the same separate `HomepageSection` domain:

- `HomepageBuilderPage`: visual drag, schema fields, global/tenant scope, React Query draft/published reads, save, reorder, publish, starter template, and real Storefront renderer preview (`PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.tsx:345-468`, `730-895`; `HomepageBuilderPreview.tsx:1-167`).
- `HomepageManagerPage`: record-oriented CRUD/reorder/publish over the same four schemas (`PointOfSale.Web/src/super-admin/homepage/HomepageManagerPage.tsx:535-619`, `724-855`).

The backend schema registry permits only `hero`, `products`, `banner`, and `categories` (`PointOfSale.API/Services/Homepage/HomepageSectionSchemaRegistry.cs:35-43`, `53-122`). These records feed the Marketplace read model, not `PointOfSale.Storefront/src/pages/HomePage.tsx`, which reads tenant StoreConfig. They are outside the tenant Storefront Builder runtime contract.

### 4.7 SuperAdmin Marketplace CMS Page Editor

`MarketplaceCmsPageEditor` is a separate marketplace-domain RichText/block editor with localized content, translation, save, preview, and publish (`PointOfSale.Web/src/super-admin/marketplace-cms/MarketplaceCmsPageEditor.jsx:40-260`). It does not feed tenant Storefront routes.

### 4.8 Builders Not Found

No tenant Storefront implementation was found for a nested Layout Builder, Slot Builder, Widget Builder, Plugin Builder, Component Builder, Registry Builder, Dynamic Builder, standalone Banner Builder, Brands Builder, Blog Builder, Search Box Builder, Filter Panel Builder, Custom HTML Builder, or arbitrary code/component builder.

No visual Landing Page Builder was found. Affiliate product authoring can select a backend `templateType`, while the live Affiliate Landing runtime resolves one of three statically registered presentation templates. That is a template selector/runtime registry, not a page Builder.

`VariantOptionsBuilder` belongs to Product Authoring and is excluded by the mission’s frozen Product Authoring boundary.

## 5. StoreConfig Builder Execution Graph

```text
URL page/template selection
  -> StorefrontBuilderPage
  -> authenticated admin StoreConfig + public live StoreConfig load
  -> backend identity/tenant/row-version admission
  -> accepted pages[page].sections or productTemplates[templateId].sections
  -> page availability filter
  -> builderStore hydration + undo/redo state
  -> SectionPalette (sectionRegistry + canonical section contract)
  -> SchemaSettingsForm / drag/toggle/duplicate/remove/reorder
  -> strict client contract validation
  -> PUT /api/admin/store-config/pages/{pageKey}
  -> PostgreSQL StoreConfig.CustomizationJson selected branch .draft
  -> POST /pages/{pageKey}/publish
  -> admitted draft deep-copied to selected branch .live
  -> public GET /api/storefront/store-config
  -> StorefrontDraftConfigMapper.MapLive
  -> Storefront bootstrap -> storeConfigStore
  -> active consumer:
       pages.home.sections -> HomePage
       productTemplates[id] -> ProductTemplateResolver -> ProductDetailsPage
  -> SectionRenderer
  -> ThemeSectionResolver (presentation component only)
  -> RuntimeSectionContainer
  -> Runtime hooks/stores/presentation models
  -> storefront APIs / catalog/search/category/cart data authorities
```

### 5.1 Configuration and Loading

The workspace loads admin draft config and public runtime config in parallel, admits backend identity/tenant evidence, and refuses editable rendering until an accepted RowVersion is present (`StorefrontBuilderPage.jsx:214-282`; `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts:15-42`).

### 5.2 Serialization and Storage

The client serializes a flat array of `{id,type,order,enabled,settings}`. A page template additionally serializes `templateId` and `name` (`StorefrontBuilderPage.jsx:326-347`).

The backend stores page drafts under `pages[pageId].draft` and product templates under `product_templates[templateId].draft` (`AdminStoreConfigService.cs:266-329`). Draft save calls `SaveChangesAsync` without publishing StoreConfig events (`AdminStoreConfigService.cs:1300-1320`).

At publish, the backend re-admits the entire composition and deep-copies it to `live` (`AdminStoreConfigService.cs:335-419`). Public Storefront config uses `MapLive`, never draft (`StorefrontStoreConfigController.cs:61-67`, `127-139`; `StorefrontDraftConfigMapper.cs:32-47`, `443-465`).

### 5.3 Hydration

Public/admin mapping exposes only page keys `home`, `product`, and `category`, plus the separate product template dictionary (`StorefrontDraftConfigMapper.cs:339-403`). Sections are re-normalized against the shared contract and invalid sections are omitted (`StorefrontDraftConfigMapper.cs:473-514`).

### 5.4 Rendering and Section Execution

`SectionRenderer` normalizes sections, skips disabled entries, resolves an active Theme presentation module or a default component, and always delegates through `RuntimeSectionContainer` (`SectionRenderer.tsx:88-157`, `162-198`). Product-detail executable types use the default component rather than a Theme override (`SectionRenderer.tsx:24-66`).

`RuntimeSectionContainer` owns all current data-bound execution:

- Product Grid/Featured Products -> `useHomepageSectionProducts`, canonical `ProductCard`.
- Category Grid/Subcategory Grid -> `useCategories`.
- Related Products -> `useProducts`, current-product exclusion, canonical `ProductCard`.
- Product Hero -> `useVariantSelector`, cart store, analytics, toast, pricing/availability presentation model.
- Static sections -> presentation component only; no Builder-provided functions or component modules.

### 5.5 Preview Paths

There are three preview forms:

1. **Default legacy canvas:** feature flag defaults off, so Builder hosts the actual `SectionRenderer` inside the POS Admin bundle through `BuilderThemeProvider` (`BuilderCanvas.jsx:168-247`; `themeNativeCanvasFlag.js:18-31`). This executes canonical Runtime hooks in preview; Builder does not implement them.
2. **Authenticated PreviewModal:** saves draft, fetches backend draft preview, injects it temporarily into the Storefront config store, renders `SectionRenderer`, and restores the prior config on cleanup (`PreviewModal.jsx:25-56`, `278-299`).
3. **Feature-flagged native iframe:** sends a complete candidate StoreConfig through `config.apply` to an editor-mode Storefront iframe (`ThemeNativeCanvas.jsx:48-75`, `181-256`). The child accepts an object with a Theme object, replaces the Storefront config store, and calls `applyTheme` (`PointOfSale.Storefront/src/editor-mode/editorMode.ts:64-75`). This is origin/tenant-gated and preview-only, but it is a broad runtime/theme replacement seam.

The native candidate builder writes `pages[currentPage].sections`. For `product-template`, that produces `pages["product-template"]` rather than `productTemplates[templateId]`; the iframe URL also remains the Storefront root. Therefore native Product/Category/Product Template preview does not reconstruct the corresponding live execution path. The path is currently flag-off by default.

## 6. Canonical Builder Contract

### 6.1 Allowed Responsibilities

Builder is allowed to own only the following:

1. **Bounded composition:** select a canonical admitted section type for an admitted page context.
2. **Optional section existence:** add, remove, duplicate, enable, or disable optional presentation sections.
3. **Flat visual order:** order sections in one flat list.
4. **Authored presentation content:** headings, subtitles, marketing copy, sanitized rich text, media references, CTA labels/destinations, badges, testimonial content, and decorative colors/alignment.
5. **Bounded presentation choices:** columns, rows, grid/carousel choice, visual skin identifier, media thumbnail count, optional static-media controls, and similar contract-enumerated choices.
6. **Merchandising intent:** choose `all`, `random`, or `curated`; supply product/category IDs; supply a bounded display count. Runtime remains responsible for admitting IDs, loading truth, availability, and presentation models.
7. **Supplemental Product Template composition:** position optional Product Details presentation sections around a mandatory Runtime-owned core.
8. **Draft authoring lifecycle:** save draft, request backend preview, publish, view version history, and rollback through authenticated authoring APIs with backend concurrency evidence.
9. **Preview hosting:** host the canonical runtime for visual inspection, provided preview cannot become the source of live Runtime or Theme authority.
10. **Separate content builders:** CMS page block order/content, Navigation link content/order, and Footer content/order. Their live loading and execution remain Runtime-owned.

The current backend explicitly rejects nested layout-authority keys such as containers, grids, columns structures, responsive rules, breakpoints, and visibility rules (`AdminStoreConfigService.cs:40-57`, `1974-1981`). Therefore Builder does **not** currently own arbitrary container hierarchy, slots, nested layout, or responsive execution. It owns only contract fields such as `columns`, `rows`, alignment, height, and display style.

### 6.2 Forbidden Responsibilities

Builder must never own:

- Catalog, Search, Filters, Pagination, Sorting, Product, Category, Navigation, Footer, Authentication, Wishlist, Cart, Projection, or business Runtime implementation.
- Runtime routes, route replacement, page component replacement, `LayoutRuntime`, `RuntimeSectionContainer`, Search providers, Theme providers, or other Runtime providers.
- API/data execution in customer runtime: `fetch`, Axios, React Query, MongoDB, Elasticsearch, cache policy, projection reads, or runtime-store mutation.
- Section execution logic, data hooks, cache keys, loading/error states, runtime presentation-model construction, or callbacks for business actions.
- Product Card implementation, price/rating/cart visibility authority, click behavior, availability, or analytics.
- Product Details invariants: the existence, uniqueness, and enabled state of the mandatory product purchase runtime; variant resolution; availability; stock; price; add-to-cart; media truth; or product-authored specification truth.
- Customer-review/recommendation truth or any other business-data truth represented as authored content.
- Theme identity, Theme contract mutation, Theme module selection outside active Theme resolution, Theme token mutation through page composition, or Theme-owned visual implementation.
- StoreConfig mutation outside the single admitted page/template draft branch being authored.
- Arbitrary HTML without sanitization, JavaScript, functions, component classes, dynamic import paths, provider injection, plugin loading, reflection, or executable registry entries.

Admin authoring API calls are not forbidden: they are the required persistence boundary. What is forbidden is Builder-owned customer-runtime/API behavior or executable settings that bypass Runtime.

## 7. Section Ownership

Legend: **B** = Builder declaration/content, **R** = Runtime authority/execution, **T** = Theme/default presentation implementation, **—** = none. “B→R violation” means current Builder authority conflicts with the canonical owner.

| Canonical section | Existence / placement | Layout / visual selection | Behavior | Data truth | Loading / execution | Certification finding |
|---|---|---|---|---|---|---|
| `hero-banner` | B | B selects; T renders | R executes navigation | B marketing content | R/browser | Allowed; CTA URL admission is structurally weak. |
| `product-grid` | B | B selects; T renders | R | B merchandising intent; R product truth | R | Allowed boundary; `layout` and card-visibility settings are not all honored. |
| `featured-products` | B | B selects; T renders | R | B intent; R truth | R | Allowed boundary; Runtime reads `limit` although contract exposes `maxItems`. |
| `category-grid` | B | B selects; T renders | R | B intent; R category truth | R | Allowed boundary. |
| `subcategory-grid` | B | B selects; T renders | R | R | R | Contract exposes source/IDs/count/columns, but Runtime currently takes the first six subcategories. |
| `text-block` | B | B selects; T renders | — | B authored content | R sanitizes/renders | Allowed; rich HTML is sanitized. |
| `image-block` | B | B selects; T renders | — | B authored media reference | R/browser | Allowed. |
| `video-block` | B | B selects; T renders | B declares playback preferences; R/browser executes | B authored media reference | R/browser | Allowed only as bounded non-business presentation behavior; no executable code is accepted. |
| `newsletter-signup` | B | B selects; T renders | No subscription runtime exists; form only prevents submit | B marketing content | None | Observation: it is a visual form, not Newsletter Runtime. |
| `testimonials` | B | B selects; T renders | — | B authored marketing testimonials | R renders | Allowed as marketing content; nested list objects have weak admission. |
| `product-hero` | **B→R violation** | B skin/optional display; T/default renders | R owns variants/cart/stock; B still controls description/gallery/SKU visibility | R product/price/availability | R | **FAIL:** Builder can disable/duplicate the mandatory runtime; critical toggles are hardcoded true, but core invocation is not protected. |
| `product-gallery` | B optional section | B thumbnails/count/skin; T/default renders | R component behavior, B visibility preferences | R product media | R | Observation/violation boundary: Builder controls Product Details media capability visibility. |
| `product-specs` | B optional section | B title/use-description/skin | R parses Product description | R product truth; Builder `items` is ignored | R | Contract drift; no current Builder-authored spec truth reaches default runtime. |
| `product-video` | B optional section | B title/autoplay/controls | B declares Product Details playback behavior; R/browser executes | R product media; contract `videoUrl` is ignored | R/browser | Product Details behavior boundary observation. |
| `customer-reviews` | B | B skin/title | T/default renders | **B→R violation** | No Review Runtime load | **FAIL:** arbitrary Builder-authored review objects are presented as customer reviews. |
| `related-products` | B optional section | B title/limit/skin | R query/filter/card behavior | R | R | Allowed supplemental composition; `showPrice`/`showRating` are ignored by canonical Product Card path. |

### 7.1 Requested Sections Not Present in the Main Registry

| Requested concept | Source result |
|---|---|
| Banner | A default `banner.tsx` and legacy alias exist, but there is no canonical `banner` contract, so strict normalization cannot admit it. SuperAdmin Marketplace Homepage has a separate `banner` type. |
| Brands | No main StoreConfig Builder section. |
| FAQ | No main StoreConfig Builder section; fixed `faq` exists only in CMS page blocks. |
| Blog | No main Builder section. |
| Custom HTML | No main Builder section. `text-block` and CMS RichText sanitize HTML before rendering. |
| Carousel | No standalone section; bounded product `layout`/`displayStyle` settings expose carousel presentation. |
| Recommendations | `related-products`; Runtime loads products. |
| Search Box / Filter Panel | Not Builder sections. Search and Filters remain Runtime-owned. |
| Navigation / Footer | Not main Builder sections. Separate content editors author data; `LayoutRuntime` loads and executes it. |

## 8. Registry Analysis

| Registry / resolution seam | Owner and purpose | Can Builder replace Runtime? |
|---|---|---|
| Admin `sectionRegistry` | Mutable in-memory palette of the 16 canonical types; public `register`, lookup, category grouping (`sectionRegistry.js:32-224`). Defaults/schema are overwritten from the shared contract. | No live replacement. No external registrations were found, backend rejects unknown types, and live rendering does not consume this registry. It remains an admin extensibility risk. |
| Canonical section contract | `theme-authority/shopixy-theme-authority.json`, mirrored by frontend runtime and `StorefrontSectionContracts`. Owns types, keys, primitive types, ranges, and enums. | Prevents unknown type/key injection, but list contents and URL semantics are not deeply admitted. |
| Runtime legacy `SectionRegistry` alias | Static aliases in `homepageSections.ts:20-43`, followed by canonical type normalization and strict validation. | No. It only supplies resolution candidates. The legacy `banner` alias cannot pass canonical contract validation. |
| `ThemeSectionResolver` | Dynamic Vite glob for default and active-Theme section modules (`ThemeSectionResolver.ts:8-17`). | Builder selects only a canonical section type. Theme can replace presentation for general sections, but Runtime wrapper remains; product-detail executable sections are default-only. |
| `ProductTemplateRegistry` | Static built-in product template compositions (`ProductTemplateRegistry.ts:20-435`). | No component/runtime injection. Saved tenant compositions override built-in composition, which creates the mandatory-core violation described above. |
| `ThemeRegistry` / admin Theme marketplace registry | Immutable manifest-derived Theme availability and metadata. | No Builder mutation/replacement. |
| Affiliate Landing `TemplateRegistry` | Exported mutable map of presentation components; statically registers Classic, Storytelling, and MinimalFastCheckout (`TemplateRegistry.ts:81-124`; `landing/index.ts:17-29`). | No Builder connection was found. It is a separate Runtime presentation extension seam; behavior is supplied by parent callbacks. |
| CMS block resolution | Fixed frontend switch plus backend `text`/`image`/`faq` allowlist. | No arbitrary component injection. |
| SuperAdmin Homepage schema registry | Backend registry for four Marketplace homepage record types. | No tenant Storefront Builder connection. |
| Widget, plugin, slot, component, or page registry | None found for the main Builder. | No. |

## 9. Dependency Graph

```text
Tenant StoreConfig Builder
  ↓ sectionRegistry + shared section contract
  ↓ flat section/template presentation declaration
  ↓ public live StoreConfig bootstrap
  ↓ HomePage or ProductTemplateResolver/ProductDetailsPage
  ↓ SectionRenderer
  ↓ ThemeSectionResolver (presentation only)
  ↓ RuntimeSectionContainer
  ↓ Runtime hooks/stores/presentation models
  ↓ storefront APIs and authoritative data

Tenant CMS Page Builder
  ↓ fixed RichText/block allowlist
  ↓ versioned CMS page storage
  ↓ CmsPageView/CmsPageRenderer
  ↓ fetchCmsPage / CMS Runtime
  ↓ CMS SQL/read cache

Navigation/Footer Editors
  ↓ admitted navigation/footer DTOs
  ↓ Navigation/Footer stores
  ↓ LayoutRuntime presentation models and capability slots
  ↓ public navigation/footer APIs
  ↓ authoritative SQL/read models

SuperAdmin Marketplace Homepage Builder (separate domain)
  ↓ HomepageSectionSchemaRegistry
  ↓ HomepageSection draft/published records
  ↓ Marketplace projection/read model
  ↓ Marketplace runtime, not tenant Storefront HomePage
```

Ownership violations occur before Runtime invocation in the active Product Template branch and at the review-data boundary. No violation was found below `RuntimeSectionContainer` for Catalog, Search, Product Card construction, variant selection, cart, or caching.

## 10. Builder Ownership Matrix

Each row ends in exactly one canonical owner. Builder/Runtime/Theme columns describe the observed role.

| Responsibility | Builder | Runtime | Theme | Owner |
|---|---|---|---|---|
| Homepage optional-section existence/order/enabled | Authors | Consumes | Renders | Builder |
| Flat section composition | Authors | Admits/executes | Renders | Builder |
| Nested containers, slots, grids, responsive rules | Rejected by backend | Owns layout structure | Renders | Runtime |
| Authored marketing text/media/CTA | Authors | Sanitizes/executes link | Renders | Builder |
| Merchandising source/curated IDs/count intent | Authors | Admits and resolves | Presents | Builder |
| Catalog product truth | None | Loads/owns | Presents model | Runtime |
| Category truth | None | Loads/owns | Presents model | Runtime |
| Search/filter/sort/pagination | None | Loads/owns | Presents model | Runtime |
| Runtime section component invocation | Chooses admitted type | Resolves/wraps/executes | Supplies presentation component | Runtime |
| Product Card implementation and behavior | Contract contains ignored visibility fields | Creates canonical `ProductCard` | Skins within frozen contract | Runtime |
| Product Details core existence/uniqueness/enabled state | **Currently controls** | Guard is incomplete | No authority | Runtime |
| Variant selection | No effective control | Owns | Presents model | Runtime |
| Add to cart / stock / availability | No effective control; contract toggles ignored | Owns | Presents model | Runtime |
| Product description/gallery/SKU visibility | Currently controls some toggles | Executes | Presents | Runtime |
| Product media truth | No load authority | Owns Product DTO/media | Presents | Runtime |
| Related-product query and cards | Title/limit only | Owns | Presents | Runtime |
| Customer-review truth | **Currently authors arbitrary list** | No loader | Presents | Runtime |
| Runtime APIs / Axios / fetch / React Query | Admin authoring calls only | Owns customer execution | None | Runtime |
| Runtime stores and caching | Builder has isolated editor state only | Owns | None | Runtime |
| Router/page selection | URL selects authoring target only | Owns live routes | None | Runtime |
| Header/Footer capability slots | None | Owns | Renders supplied model/slots | Runtime |
| Navigation/Footer authored content | Separate content editors author | Loads/normalizes | Presents | Builder |
| Authentication/Wishlist/Cart/Search capability visibility | None | Owns | Places required slots | Runtime |
| Theme identity and tokens | Main Builder copies for preview only | Applies live admitted config | Theme editor/authority owns | Theme |
| Theme module implementation and visual CSS | Chooses bounded visual settings | Enforces wrapper | Owns | Theme |
| Product-to-template assignment | No ownership | Consumes Product DTO field | None | Product Authoring |
| Draft/live branch transition | Requests | Backend admits/publishes | None | Runtime |
| Product-template deletion transition | Requests immediate delete | Persists immediate live removal | None | Runtime |

## 11. Runtime Override Matrix

| Override target | Builder path found | Live effect | Finding |
|---|---|---|---|
| Homepage page body | `pages.home.sections` | Replaces fallback hero with admitted section composition | Intended Builder ownership. |
| Catalog `/products` | `pages.product.sections` authoring exists | Not consumed | No Runtime replacement; dormant Builder surface. |
| Category `/category` | `pages.category.sections` authoring exists | Not consumed | No Runtime replacement; dormant Builder surface. |
| Product Details via `pages.product` | Persisted branch exists | Resolver ignores it | Recovered boundary holds. |
| Product Details via saved product template | Saved template preferred | Drives PDP section list | Intended supplemental composition, but can suppress/duplicate core Runtime: violation. |
| Router | No route/component field in contract | None | Boundary holds. |
| `RuntimeSectionContainer` | No component/function/module setting | Always wraps live section | Boundary holds. |
| Presentation models | No Builder implementation | Constructed in Runtime | Boundary holds. |
| Product Card | Contract has visibility flags, but Runtime injects canonical `ProductCard` | Builder flags do not replace card behavior | Boundary holds with contract drift. |
| Search/Filters/Pagination/Sorting | No admitted section/provider implementation | Runtime-only | Boundary holds. |
| Header/Footer/Navigation capability runtime | Main Builder has no section for these | Runtime-only; separate content editors provide data | Boundary holds. |
| Runtime providers | No persisted provider declaration | Runtime-only | Boundary holds. |
| Theme contracts/modules | No live page-Builder mutation path | Theme resolver/authority controls | Boundary holds in live runtime. |
| Full StoreConfig/Theme in editor iframe | `config.apply` candidate document | Replaces editor-mode config and applies Theme | Preview-only broad override observation. |
| CMS page runtime | Fixed RichText/block content | Fixed CMS renderer only | No commerce Runtime replacement. |
| Affiliate landing runtime | No Builder; backend template selector | Static registered presentation template | Separate runtime seam, no Builder override. |

## 12. Hidden Runtime Owners and Escape Hatches

| Hidden owner/seam | Source | Assessment |
|---|---|---|
| `RuntimeSectionContainer` | `storefront/runtime/containers/RuntimeSectionContainer.tsx` | Expected hidden Runtime owner behind data-bound sections. |
| Theme dynamic imports | `ThemeSectionResolver.ts:8-13` | Theme presentation replacement only; Runtime wrapper preserved. |
| Editor Bridge `config.apply` | `ThemeNativeCanvas.jsx:181-256`; `editorMode.ts:64-75` | Full preview StoreConfig/Theme replacement; origin/tenant gated, not persisted. |
| Native-canvas feature flag | `themeNativeCanvasFlag.js:18-31` | `localStorage` can switch preview implementations; default legacy. No live customer effect. |
| `BuilderThemeProvider` | `BuilderThemeProvider.jsx:12-44` | Injects full config and tenant into shared Storefront stores for in-process preview and does not restore prior config on unmount. Admin-preview state contamination risk only. |
| `PreviewThemeProvider` | `PreviewModal.jsx:25-56` | Injects draft preview, but restores prior config. |
| Admin `sectionRegistry.register` | `sectionRegistry.js:188-203` | Imperative admin extension seam; no external registrations found and cannot pass unknown live contract admission. |
| Affiliate Landing template registration | `pages/landing/TemplateRegistry.ts:81-124` | Mutable presentation registry, but no Builder connection and behavior is callback-owned. |
| Product template resolver cache | `ProductTemplateResolver.ts:4-12`, `33-69` | Runtime-owned cache keyed by config timestamp/template. No Builder cache implementation. |
| Runtime providers | `StorefrontLayout.tsx`; `app/App.tsx` | Builder preview may host them; live ownership remains Runtime. |
| Feature flags in section composition | None found | Builder has no section-level feature-flag execution authority. |
| Reflection/plugin loading | No main Builder path found | No live Builder replacement path. |

## 13. Violations and Observations

### V1 — Critical — Product Template Builder can suppress or duplicate Product Details Runtime

**Classification:** Runtime ownership violation; certification blocker.

The mandatory guard checks only for the presence of a `product-hero` type. Builder can persist it disabled, and Product Details filters it out. Builder can also duplicate it, producing multiple variant/cart/analytics runtime instances. Backend admits both states. This violates the rule that mandatory Product Details capability existence and execution are Runtime-owned.

### V2 — High — Builder owns customer-review presentation truth

**Classification:** Business-data ownership violation; certification blocker.

The canonical contract exposes `customer-reviews.reviews` as an authored list (`theme-authority/shopixy-theme-authority.json:794-809`). The default section renders names, avatars, star ratings, and text directly from that list without a Review Runtime/data loader (`PointOfSale.Storefront/src/components/sections/defaults/customer-reviews.tsx:12-72`). `customer-reviews` is not restricted to Product Template context because it is absent from the product-context set (`pageSectionAvailability.js:1-23`).

### V3 — High — Contract admission is strict by key but weak by meaning

**Classification:** Admission/runtime risk.

Frontend and backend require all known fields and reject unknown keys, but `list` validates only “is array”; nested object shape is not validated (`PointOfSale.Core/Constants/StorefrontSectionContracts.cs:75-141`, `154-160`). URL/image/video fields fall through to string validation; no safe URL scheme or length admission is applied (`StorefrontSectionContracts.cs:161-181`). The main backend normalization calls this primitive validator directly (`AdminStoreConfigService.cs:1966-1995`); older URL/text normalization helpers are not on that active path. Builder content therefore can carry arbitrary nested review/testimonial objects and unconstrained CTA/media strings into live presentation.

### V4 — High — Builder still controls Product Details capability visibility and playback preferences

**Classification:** Runtime boundary violation/observation.

Runtime correctly hardcodes variant selector, Add to Cart, and stock status visible (`RuntimeSectionContainer.tsx:322-329`), but Builder settings still control product description, the product-hero media gallery, SKU visibility, separate gallery thumbnails/count, and product-video autoplay/controls (`RuntimeSectionContainer.tsx:323-330`; `defaults/product-gallery.tsx:15-38`; `defaults/product-video.tsx:11-60`). Under the certified contract, Product Details capability visibility and behavior belong to Runtime; Builder may select presentation skin and supplemental placement only.

### V5 — Medium — Product and Category Builder modes are publishable but have no live consumer

**Classification:** Contract mismatch; no current Runtime override.

The admin exposes and persists both modes (`StorefrontBuilderPage.jsx:107-108`; `Sidebar.jsx:224-225`), the backend maps them (`StorefrontDraftConfigMapper.cs:351-353`), and existing Builder certification tests explicitly keep reusable sections available on Product Page. Live Product and Category pages do not execute them. The Builder preview can therefore display and publish compositions customers never see.

### V6 — Medium — Canonical section contract and Runtime consumption drift

**Classification:** Ownership ambiguity/preview correctness risk.

Observed drift includes:

- `product-grid.layout` is forced to `grid` in Runtime.
- `featured-products.maxItems` is exposed, but Runtime reads `sectionProps.limit`.
- Product-grid/featured/related `showPrice`, `showRating`, and `showAddToCart` are passed as settings but canonical Product Card construction ignores them.
- `subcategory-grid` source, IDs, max count, and columns do not control Runtime selection; Runtime uses the first six subcategories.
- `product-specs.items` is admitted but ignored by the default Product Specs component.
- `product-video.videoUrl` is admitted but Runtime uses Product media.

These do not grant Builder more Runtime authority; they make declared Builder authority inaccurate.

### V7 — Medium — Native preview can replace the full editor Runtime config and Theme

**Classification:** Preview-only hidden override.

`ThemeNativeCanvas` sends a full normalized StoreConfig. The child validates only that the candidate and `theme` are objects, then replaces config state and applies Theme. The current host changes only one page branch, but the protocol/applier is broader than Builder’s page ownership. No persistence or normal customer activation path was found.

### V8 — Medium — Product template deletion bypasses the page draft/publish transition

**Classification:** Lifecycle inconsistency.

Delete removes the entire `product_templates[templateId]` entry, including live content, and persists an `Updated` StoreConfig action immediately (`AdminStoreConfigService.cs:464-503`). This is not the normal draft-then-publish page transition. The resulting live PDP falls back through `ProductTemplateResolver`.

### V9 — Medium — Feature-flagged native preview does not target non-home live routes correctly

**Classification:** Preview accuracy observation.

The iframe URL does not vary by Builder page, and the candidate writes every target under `pages[currentPage]`. Product Template is actually stored under `productTemplates`, while Product and Category live pages ignore their section branches. This path is off by default, but when enabled it cannot certify parity for those modes.

### V10 — Low — In-process preview provider does not restore shared Storefront config

**Classification:** Admin preview state observation.

`BuilderThemeProvider` writes normalized StoreConfig and tenant into shared singleton stores and has no cleanup restoration (`BuilderThemeProvider.jsx:12-44`). `PreviewModal` does restore its previous config. No customer runtime effect was found.

## 14. Risk Assessment

| Risk | Severity | Reach | Source certainty |
|---|---|---|---|
| Disabled core `product-hero` suppresses purchase runtime | Critical | Any product assigned to the affected saved template; potentially the default template | Direct active source path |
| Duplicate `product-hero` duplicates cart/variant/analytics runtime | Critical | Same as above | Direct active source path |
| Builder-authored customer reviews presented as customer truth | High | Any page/template admitting the section | Direct active source path |
| Product Details gallery/SKU/playback capability controlled by Builder | High | Product templates containing those sections/settings | Direct active source path |
| Weak nested list and URL semantic admission | High | All Builder list/URL settings | Direct validator source |
| Product/Category Builder drafts have no live consumer | Medium | Admin authors; no customer composition effect | Direct route/consumer comparison |
| Contract fields ignored or misnamed by Runtime | Medium | Affected sections | Direct contract/runtime comparison |
| Full StoreConfig/Theme replacement in editor mode | Medium | Feature-flagged preview iframe only | Direct preview source path |
| Product-template deletion changes live composition immediately | Medium | Products using deleted template | Direct persistence source path |
| Native non-home preview mismatch | Medium | Feature-flagged preview only | Direct URL/candidate/consumer comparison |
| Shared preview-store contamination | Low | POS Admin preview bundle only | Direct provider source |

## 15. Certification

### Result: FAIL

Builder architecture is **not canonical** under the frozen Runtime and Theme ownership baseline.

The following boundaries pass:

- Catalog, Category, Search, Filters, Pagination, Sorting, Navigation Runtime, Footer Runtime, Authentication, Wishlist, Cart stores, Product Card construction, Runtime providers, and Theme live implementation are not replaceable through main Builder contracts.
- Unknown section types and unknown top-level setting keys fail admission.
- Live public StoreConfig reads the live branch; admin preview reads the draft branch.
- `RuntimeSectionContainer` remains the data/behavior owner for all data-bound sections that reach it.
- Theme section overrides remain presentation-only and product-detail executable sections use default Runtime presentation components.
- No arbitrary component, dynamic import path, plugin, provider, fetch callback, Axios callback, React Query callback, Mongo query, Elasticsearch query, or executable JavaScript field exists in the Builder contract.

The following blockers fail certification:

1. Product Template Builder can suppress or duplicate the mandatory Product Details runtime.
2. Builder contract/runtime presents Builder-authored customer reviews as customer-review truth.
3. Builder controls Product Details visibility/playback responsibilities that belong to Runtime.
4. Contract admission does not deeply constrain list content or URL meaning at the live boundary.

This document is the canonical Builder Contract and audit baseline for a later Builder Recovery phase. No recovery work is included or started here.

## 16. Audit Integrity Statement

- Audit was source-only.
- No application source code, tests, runtime configuration, schema, or frozen Phase 1/2 artifact was modified.
- The only created artifact is this certification document.
- Audit stops at certification.

# Homepage × Theme × Appearance coherence — final certification

Date: 2026-09-24 (browser execution on 2026-09-25). Scope: authenticated POS Admin tenant StoreConfig builder at `/storefront/homepage`, `/storefront/appearance/themes`, `/storefront/appearance/colors-typography`, and the tenant's public storefront. **The similarly named super-admin Marketplace HomepageSections builder is a separate product and does not own this storefront.**

## 1. Design-system authority map

| Visible element | Source of authority → Admin control → storefront consumer | Lifecycle and result |
| --- | --- | --- |
| Active theme, hero/header/footer/category/product-grid skins, product cards, badges, button shapes, navigation presentation | `StoreConfig.CustomizationJson.storefrontTheme` selects the official theme in `theme-authority/shopixy-theme-authority.json`; Themes activation; `ThemeResolver`, `ThemeLayoutResolver`, `ThemeSectionResolver`, `ThemeComponents` and theme-specific renderers | Theme preview is read-only; activation changes the live skin without replacing page sections. Tested Modern → Minimal → Modern. |
| Global primary/secondary/background/text/muted colors, font family/base-size, border radius | StoreConfig theme preset plus live/draft overrides; Colors & Typography editor; `themeToCssVariables` / `applyTheme` and component token classes | Candidate preview → explicit draft save → Appearance-only publish. Verified public `--color-primary` changed and restored. |
| Shadows, hover treatments, image treatment, section headers/eyebrows, default spacing, typography scale, container width | Theme-specific components/Tailwind/CSS, e.g. `modern/sections/*`, `ThemeComponents.tsx`; theme selection changes them; not separately mutable global tokens | Intentional theme design foundation rather than duplicate Homepage controls. `max-w-7xl` and other dimensions are component constants, not falsely represented as configurable global widths. |
| Header/nav/footer content and links, store identity | StoreConfig header settings and independently authored CMS navigation/footer plus store name; `StorefrontLayout` and themed header/footer | Not owned by Homepage. Theme changes presentation; publishing Homepage does not publish navigation/footer. |
| Homepage composition: hero, featured/product grids, category/subcategory grids, newsletter, editorial/image/video/testimonial sections | `CustomizationJson.pages.home.draft/live.sections`; page builder/canvas/settings; public `HomePage` → `SectionRenderer` | Section order, enabled flag, data, IDs and settings follow explicit draft save/publish/version lifecycle. |
| Hero text/CTA/media/height/alignment/colors/overlay | Canonical `hero-banner` section contract and Homepage editor → themed hero renderer | Section-specific settings remain in Homepage. Modern's authored `highlights` values/captions are optional, max three, and only Modern renders its strip. |
| Former “24h / 3x / 1” and image's “Featured collection / Ready to shop” | Previously literal English text in `modern/sections/hero-banner.tsx`, unrelated to Theme settings, StoreConfig, or product data | Removed misleading fixed marketing content. Modern highlights now render only authored Homepage data. Legacy heroes missing the new field materialize to an empty strip. |
| Featured product identity, badge and layout | Homepage `source`, ordered `productIds`, `maxItems`, `displayStyle`, badge fields; catalog Product ID; public `useHomepageSectionProducts`/runtime presentation | Products are authoritative tenant catalog records. Missing products are shown as unavailable in the editor and omitted safely by storefront retrieval. |
| Categories and ordering | Homepage `categoryIds`, `source`, `maxItems`, `columns`; category API and runtime tree | Distinct curated/discovery behavior; not theme-owned. |
| Fallback empty-store hero and guest/customer CTA | Public runtime fallback and auth components | Deliberate runtime defaults when no sections exist; not separate Homepage draft content. |

## 2. Theme inventory

Canonical authority publishes **10 official active themes**: Modern (default), Minimal, Fashion, Pearl, Phoenix Sports, Hype Electronics, Alizon, Sharp Jewelry, Cosmetics and Seven Womenwear. Electronics is experimental/disabled. Each active manifest exposes header/footer, hero, product grid, category grid, product-card and purchase-profile capabilities with a design foundation (colors, font, radius, button style, layout defaults); implementation-specific header, card, shadows and spacing remain with theme components. The marketplace offers signed candidate preview and explicit live activation. No theme selector or duplicated global card/shadow/spacing fields were added to Homepage.

## 3. Homepage inventory and repairs

`theme-authority/shopixy-theme-authority.json` is the shared section contract for POS Admin, backend validation and storefront. Homepage offers hero/banner, products, featured products, category/subcategory grid, newsletter, text/image/video and testimonial sections. Section order/visibility, titles/media/CTA, product/category sources, limits, layout and section-specific styling are composed in the page draft. The Modern-only highlight strip is now authored through an optional three-item `highlights` list (value + caption) in the hero contract. Empty list renders no strip. Backend normalization, draft mapper and frontend validation admit pre-existing hero drafts without `highlights`; canonical saves add the default. The hero renderer honors its authored foreground color rather than overriding it with a slate text utility.

## 4. Appearance inventory

Appearance controls theme preset, five global colors, font family and border radius. Local candidate preview does not publish; Save Draft changes only Appearance draft; Publish Colors & Typography promotes it to live. Runtime applies the global CSS variables; Theme still determines component structure. No Appearance operation during certification replaced Homepage sections.

## 5. Missing and overlapping controls found

- The featured shelf exposed a GUID textarea; replaced with catalog-backed search/selection.
- The curated storefront shelf fetched the ordered IDs but did not enforce `maxItems` and its cache key did not include the limit. The public loader now slices to the configured count and keys the result by count. A browser publication of two IDs with `maxItems=1` rendered only the first product; original `maxItems=8`/all-products state was restored afterward.
- Real product GUIDs such as `cc20abdf-0a4f-0278-3c59-c4f10d17bc96` failed a JS UUID-version regex even though .NET accepts them. Corrected Admin validation and all three public homepage validation/filter points. This was reproduced by a real failed browser Save Draft before repair.
- Modern displayed three fabricated, untranslated stat values and two image overlay slogans. These did not come from configuration or a product feed; removed and replaced with explicit optional authored Modern highlight content. Similar unowned editorial taglines in Minimal/Fashion/Pearl/Sharp hero skins and Modern/Fashion/Electronics product-grid skins were removed without changing their layouts or user-authored content.
- Homepage status/actions/field labels/options, publish confirmation, discard/conflict dialogs and feedback were English under Arabic UI. Relevant labels now resolve through the existing `react-i18next` locale dictionaries; identifiers and catalog data remain untranslated by the builder.
- The Admin preview was schematic and omitted selected-product names and highlights; it now renders the authored highlights and tenant catalog identities in selected order. **Its visual geometry remains an editorial approximation; the public site is the pixel-level rendering authority.**

## 6. Product Selector contract

`CuratedProductSelector.jsx` searches `GET /api/products/lookup?search=…&isActive=true` (name, barcode or SKU; parent Product IDs; simple and variant products), resolves saved IDs with `GET /api/products/lookup/selected?ids=…`, preserves ordered `productIds` in the section, supports reorder/removal and the 50-ID contract limit, and explains the separate `maxItems` display limit. The list is only exposed when `source=curated`. Missing/deleted/inactive records are shown with an explicit removal action, never silently rewritten. Lookup runs under authenticated tenant-scoped Product query filters; the new batch endpoint also limits requests to 50. StoreConfig draft save and publish recheck curated IDs against **active products owned by that tenant**. Foreign IDs cannot become live even if a browser request is forged. The public shelf still uses the existing storefront catalog consumer; no parallel product catalog was introduced.

## 7. Arabic localization

The POS Admin is user-language-selected; English and Arabic live in `src/i18n/locales/{en,ar}.json`. Verified in Chromium that Arabic Homepage shows localized section names, actions/status, editor field names and option values, selector search/count/reorder controls. Source content (`Welcome to Shopixy`, product names/SKUs) deliberately stays as authored/catalog data and does not become a translated UI label. Section preview labels and the common status badge now use i18n. Some shared builder/error/template surfaces retain legacy English strings; see limitations.

## 8. Theme/Homepage relationship and lifecycle

Theme activation changes `storefrontTheme`, not page content. Appearance updates overrides, not `pages.home`; Homepage PUT writes `pages.home.draft.sections`, not theme tokens. Shared StoreConfig RowVersion must match on each mutation. Homepage preview reads the authenticated draft endpoint; publication validates then promotes the normalized draft and appends version history. Theme activation is live; Appearance has candidate/draft/live stages. StoreConfig History rollback is **whole-live-configuration rollback** and creates a new version; it is not a Homepage-only undo. Certification restored original Homepage live by publishing its original section composition after restoring its draft; Theme and Appearance were restored separately.

## 9. Backend/frontend contract

Admin sends a page-scoped `{sections:[…]}` PUT with `If-Match`; backend validates canonical section keys/types, numeric/list limits and current tenant curated products, then returns an accepted StoreConfig DTO and RowVersion. `POST .../pages/home/publish` revalidates and promotes only that page. Authenticated lookup uses the existing `/products/lookup` plus the new scoped selected-IDs projection; ID is the backend identity, not a SKU or variant ID. Public `GET /api/storefront/store-config` loads live composition and global theme; runtime validates/materializes the shared section contract and fetches curated catalog records by IDs. Existing tenant query filters, concurrency preconditions, session/CSRF controls, publication events and public cache path are preserved.

## 10. Authenticated browser and storefront evidence

Real Chromium/Playwright drove POS Admin at `localhost:5173`, API at `localhost:5010`, and public tenant `ahramstore.localhost:5174`. The tested tenant started with **four sections, Modern theme, `#2563eb` primary, empty featured selection and empty hero highlights**.

1. Opened Homepage; edited Modern hero highlight `42 / Browser certification`; opened Featured Products; searched `iPhone` and `slug`, selected a variant-capable and a simple product, reordered them; Save Draft returned 200 with two ordered IDs. Reload preserved selection and row version admission. Preview showed the highlight and both product names. Publish rendered the same highlight and products on the public storefront; the public live configuration contained the authored highlight and IDs. Returning via Themes/Appearance to Homepage retained section content.
2. Activated Minimal in the Themes UI; public header/hero changed to Minimal while Homepage sections stayed byte-for-byte equivalent. Edited Appearance primary color to `#a83232`, locally previewed, saved draft (live primary unchanged), published, and inspected public `--color-primary:#a83232`. Homepage remained intact. Restored original Appearance draft/live and reactivated Modern.
3. Restored original Homepage draft, published original Homepage live, and reopened public storefront. Final browser pass found four original sections, `productIds:[]`, `highlights:[]`, Modern, original `#2563eb` Appearance draft/live, no probe copy on the public page. An unsaved title edit survived a browser tab switch/focus and was discarded without a server RowVersion mutation or “Restoring session” display. Arabic section/editor labels were checked after changing UI language.
4. Separately authored two curated product IDs with `maxItems=1`, published and waited for the public shelf to resolve. It rendered the first product only. Draft and live were restored via explicit Save/Publish; final read-only Chromium check confirmed the original four sections, zero curated IDs/highlights, original Theme and Appearance, and no hard-coded Modern shelf tagline.

Browser probing also found the too-strict UUID regex. After repair, the same real products passed Save Draft and rendered publicly. Test automation and credentials were not checked into the repo.

## 11. Regression and builds

- POS Admin targeted Vitest: **172/172** (15 files): Homepage builder/selector, theme registry, Appearance-adjacent authoring, session/focus/auth replay, product catalog/media/variants, navigation, StoreConfig and information architecture.
- Storefront targeted Vitest: **72/72** (11 files): runtime contract, official themes, cards, catalog ownership, product detail, templates.
- Backend Release xUnit: **46/46** StoreConfig draft/publish/rollback/concurrency, theme runtime/preview, curated-tenant rejection and legacy hero compatibility.
- API Release build: success, 0 errors. POS/Admin production build: success. Storefront TypeScript + production build: success. `git diff --check`: success (Git printed line-ending conversion notices only).

## 12. Restored state and limitations

- Verified current tenant draft and public live have original four sections/order/content, no selected product IDs, no highlight test text, Modern active, original Appearance primary `#2563eb` in draft/live. No test product was created. No active test draft remains.
- Save/publish/restore operations legitimately incremented RowVersion and added **append-only historical publication versions**. They are not active public versions and were not deleted or rewritten; erasing audit history would violate the StoreConfig version contract.
- Admin canvas/preview shows a schematic, authenticated content/layout approximation rather than the public theme's exact markup, product images and card grid. Visual certification therefore used the actual public browser after publishing, not the Admin sketch alone. Modern hero highlights are intentionally theme-specific and described as such; switching to another theme preserves their data but that theme does not show Modern's strip.
- Some legacy backend-supplied error messages, theme/Appearance authoring copy and theme-specific storefront fallback copy remain outside the localized Homepage controls. No claim of whole-application Arabic translation or pixel-identical candidate preview is made.

## FINAL VERDICT

**Certified for the tenant's repaired Homepage authoring, curated product identities, Modern highlight ownership, Arabic editing controls, independent Theme/Appearance/Homepage publication and public runtime correspondence.** The original current live/draft content was restored and the focused regressions and three production builds passed. The schematic Admin preview and some backend/theme-specific English fallback strings are the explicit remaining limits to a literal pixel-perfect/whole-application localization certification.

# Phoenix Sports — Storefront Theme Design Report

**Source:** https://phoenix-sports.myeasyorders.com/
**Generated:** 2026-07-28
**Target platform:** PointOfSale.Storefront theme system (ThemeConfig → cssVariables → applyTheme)
**Reference model:** mirrors the structure of `docs/pearl-watches-theme-design-report.md` so the tokens drop straight into a new `<id>/themeConfig.ts`.

---

## 1. Executive Summary

Phoenix Sports is a **minimalist, premium activewear** storefront built on the **EasyOrders SaaS** (Next.js + Tailwind + Swiper). The entire visual identity is driven by CSS variables, which makes it a clean template to port into a Shopixy storefront theme as a token-based skin.

**Brand identity at a glance:**
- Vibe: calm, uncluttered, "quiet luxury" athleisure. Product photography does the talking; UI chrome is invisible.
- Palette: warm off-white canvas, solid black UI/footer, **red sale badge + red sale price** as the only accent.
- Type: **Jost** (Google Fonts, 400–800) for every UI/body/price/name element. Hero display title uses a serif fallback (`ui-serif, Georgia, Cambria…`) at 60px — note this is NOT Jost, it's an inherited serif; treat hero display as "serif, 500 weight" in the token layer.
- Shape language: **sharp corners everywhere** (border-radius 0 on cards, badge is the only rounded element at 14px). Buttons are thin outlined pills (radius 20px) or square 12px add-to-cart.
- Motion: subtle scroll-reveal (fade-up / zoom-in / blur-reveal keyframes) + Swiper carousels + a draggable before/after comparison slider.

**Effort to replicate in Shopixy:** ~90% achievable with ThemeConfig tokens alone (colors, fonts, radii, card variant, header variant). The before/after slider and Swiper carousels need one new section component each (the hero-banner section already exists and can host a Swiper; the before/after needs a bespoke section). Everything else is token-driven.

---

## 2. Design Token System (the heart of the theme)

The site themes itself almost entirely through CSS custom properties. The brand override stylesheet (`files.easy-orders.net/…style.css`) defines these with safe fallbacks, e.g. `var(--body-bg, #ece9e2)`. The relevant tokens and the **recommended Shopixy `ThemeConfig` mapping** are below.

### 2.1 Color tokens (measured)

| Token | Value (measured) | Where it's used | Shopixy ThemeConfig key |
|---|---|---|---|
| `--body-bg` | `#ece9e2` (warm off-white) | Page canvas, mobile menu bg | `colors.background` |
| `--body-text` | `#131316` | All body text, prices, nav, product names | `colors.text` |
| `--hd-bg` | `#FFF` | Header bar background | `colors.background` (header uses white card) |
| `--hd-text` | `#000` | Header logo, nav, icons, cart count | `colors.text` |
| `--ft-bg` | `#202021` (rgb 32,32,33) | Footer background | `colors.muted` → footer override |
| `--ft-text` | `#FFF` | Footer text | footer text override |
| `--color-primary` | `0, 0, 0` (black) | Primary action color (black buttons) | `colors.primary` |
| `--color-a11y` | `255, 255, 255` | Inverse text on dark | `colors.secondary` (inverse) |
| `--ann-bg` | `#212121` | Announcement bar | header/announcement override |
| `--ann-text` | `#fff` | Announcement text | announcement text override |
| `--product-sale-price-text` | `#cd1515` (red) | Sale price text | `colors.primary` (sale) |
| Sale badge bg | `rgb(201, 74, 74)` (`#C94A4A`) | `-28%` discount pill | accent override |

**Key insight:** the "accent" is two different reds depending on context — sale price is `#cd1515`, the discount badge is `#C94A4A`. In Shopixy, map sale price to `colors.primary` (red) and keep the badge as a derived red.

### 2.2 Recommended `ThemeConfig` block

```ts
// themes/<id>/themeConfig.ts
import type { ThemeConfig } from '@/types';

export const phoenixThemeConfig: ThemeConfig = {
    colors: {
        // Sale price + discount badge accent (design: #cd1515 / #C94A4A)
        primary: '#cd1515',
        // Footer + inverse-surface text (design: #202021 bg, #fff text)
        secondary: '#202021',
        // Warm off-white page canvas (design: #ece9e2)
        background: '#ece9e2',
        // Body / nav / price text (design: #131316)
        text: '#131316',
        // Section headings inherit text color; footer uses secondary
        muted: '#202021',
    },
    font: {
        // Jost is EVERYTHING on this site (UI, nav, names, prices).
        family: "'Jost', sans-serif",
        familyAr: "'Cairo', 'Tajawal', 'Noto Sans Arabic'",
        baseSize: '15px',
    },
    ui: {
        // Sharp editorial corners on cards (radius 0); buttons/pills are separate.
        borderRadius: '0px',
        buttonStyle: 'outline',
    },
    layout: {
        productCardVariant: 'featured', // image-first, color swatches, quick-add
        headerVariant: 'floating',      // pill header centered on canvas (see §3.1)
    },
};
```

Fonts: extend the single Google Fonts `<link>` in `PointOfSale.Storefront/index.html` with `family=Jost:wght@400;500;600;700;800` (already present on the source). Do NOT add a second font mechanism.

---

## 3. Layout & Component Design (measured specs)

### 3.1 Header — floating pill nav
- `.ab-header`: `position: relative; max-width: calc(100% - 40px); margin: 19px auto; border-radius: 12px; box-shadow: 0 1px 4px rgba(0,0,0,.08); background: var(--hd-bg,#fff)`. Wrapped in a `position:fixed; top:0; pointer-events:none` container so it floats over the hero.
- Inner grid: `grid-template-columns: 1fr max-content 1fr; height: 48px` → logo left, nav center, actions right.
- **Logo:** text "Sports Club" italic 34px (`ab-logo-text`), OR an `<img>` max-height 32px. The Phoenix site uses a wordmark.
- **Nav links** (`.ab-nav a`): measured `font-size 12px; font-weight 500; text-transform: uppercase; letter-spacing 0.96px; color #000`. Hover = underline grows from left (`::after` width 0→100%).
- **Nav items:** MEN · WOMEN · SHOES · SHIPPING POLICY · RETURN AND EXCHANGE POLICY. (SHOES is a category, the two policies are pages.)
- **Right actions:** search icon (mask SVG), cart icon with count badge (`background: var(--hd-text); color: var(--hd-bg); border-radius: 8px; font-size 9px`). Icons are 24×24 CSS-mask buttons.
- Mobile: hamburger → slide-in `.ab-mobile-menu` (width 92%, translateX drawer) with accordion sub-nav.

### 3.2 Announcement bar
- `.ab-announce`: `background: var(--ann-bg,#212121); color: #fff; height: 31px; font-size: 11px; text-align: center`. Supports three modes: simple text, marquee (CSS keyframe `ab-marquee`), or slider. Sits above/inside the floating header wrapper.

### 3.3 Hero — full-bleed Swiper carousel
- Full-width lifestyle carousel (`.swiper`). Slides = model-in-activewear photography.
- Overlaid copy: hero **title** `font: ui-serif/Georgia…; font-size 60px; font-weight 500; color rgb(250,247,242)` (warm off-white on dark image). Sub-text smaller. CTA pill buttons.
- Slide dots nav bottom-right; "Pause slideshow" control.
- **Port to Shopixy:** use existing `hero-banner` section; swap to a Swiper-based multi-slide variant if not already. Keep title serif/60px, overlay text warm-white.

### 3.4 "Shop by collection" — 3 category tiles
- 3 large image tiles (MEN / WOMEN / SHOES) with label overlay. `.ab-categories .ab-cat-card { aspect-ratio: 4/4; max-width: 360px }` on desktop, `55vw` on mobile. → maps to `category-grid` section.

### 3.5 Product grids — "Best Sellers" / "MEN COLLECTIONS" / "WOMEN COLLECTIONS"
- Container `.ab-pgrid-card`: `display:flex` (vertical card). Card image box: `background:#fff; border-radius:0; aspect-ratio: 4/5` (measured image 487×650 ≈ 4:5).
- **Two card flavours observed:**
  1. *Featured/Best-Seller card* — large image, name below, price row, **color swatches** (`.ab-featured-swatches`, small circles), tiny circular quick-add icon button on the right.
  2. *List card* (MEN/WOMEN collections) — image, "Add to cart" square button overlay, name, price, swatches.
- **Product name** (`.ab-pgrid-name`): `font-family: Jost; font-size: 16px; font-weight: 500; text-transform: uppercase`.
- **Price row:** original price struck-through `color: rgb(from var(--body-text) r g b/.5)` (50% opacity), sale price `color: var(--product-sale-price-text,#cd1515); font-size: 18px; line-height:1`.
- **Discount badge** (`.ab-pgrid-badge.ab-sale-badge`): `background: rgb(201,74,74); color:#fff; font-size:11px; font-weight:600; letter-spacing:0.66px; border-radius:14px; padding:3px 8px`. Top-left of card.
- **Color swatches:** small circular color chips (Red/Black/Green/Grey…). Rendered as `<img>` or CSS circles.
- **Quick-add:** `.ab-icon-btn.cart-btn` — 24×24 transparent circle button, mask SVG cart icon.
- **Section heading** (h2): `font-family: Jost; font-size: 48px; font-weight: 500; letter-spacing: 0.64px`. Eyebrow labels (e.g. "STYLING ROOM") smaller uppercase.
- **"VIEW ALL" button** (`.ab-plist-viewall-btn`): `background: transparent; border: 1px solid rgba(33,42,47,.35); color: #212a2f; border-radius: 20px; padding: 0 20px; font: Jost 600 11px; letter-spacing: 1.1px; text-transform: uppercase`.
- Grid responsive: the source hides its 5-col grid and uses 2-up on mobile; for Shopixy use `grid-cols-2 sm:grid-cols-3 md:grid-cols-4`.

### 3.6 "The spring edit" — editorial banner
- Full-width lifestyle image, eyebrow "NEW SUMMER COLLECTION", h1 "The spring edit", sub-line "Silhouettes, textures, and tones curated for everyday luxury.", CTAs "SHOP COLLECTION" / "LOOKBOOK". A `hero-banner`-style section variant.

### 3.7 "Shop the look" — styling room
- Section header: eyebrow "STYLING ROOM", h2 "Shop the look", sub "One outfit, every piece linked—build yours in a single pass."
- One large hero outfit image ("City nights") + linked product cards beside it (Rise Above Seamless Shirt, Duramo Sl2 W shoes, Downshifter 14 shoes, Axis Edgeline Top) each with before/after price. → a curated "shop the look" section (bespoke or reuse product-grid with a lead image).

### 3.8 "See the Transformation" — before/after slider
- `.ab-comp-range` (`<input type=range>`) driving a draggable clip between two images labeled **Before** (left) / **After** (right). Circular handle center.
- **Port to Shopixy:** this is the one genuinely custom section. Build a `before-after` section component (image-comparison slider) — not token-driven. Keep it optional/capability-gated.

### 3.9 SHOES band
- Full-width SHOES image tile + "VIEW ALL" → category-grid / promo-banner variant.

### 3.10 Footer — solid black
- `.ab-footer`: `background: rgb(32,32,33) (#202021); color: #fff`. No padding on the root; inner columns.
- Columns: newsletter signup (light input + circular arrow submit), SHOP / PAGES / CONTACT link groups (~18 links), social icons, payment-method icons row at the very bottom.
- Newsletter button: small circular arrow (minimal).
- **Port:** map footer bg to `colors.secondary` (#202021) and footer text to white. The existing `footer.tsx` layout component can take these tokens.

---

## 4. Button & Control Specs

| Control | Spec (measured) | Token/style |
|---|---|---|
| VIEW ALL | transparent, `1px solid rgba(33,42,47,.35)`, radius 20px, Jost 600 11px, ls 1.1px, uppercase | `ui.buttonStyle: 'outline'`, pill |
| Add to cart (list card) | white bg, `1px solid #212a2f`, radius 12px, uppercase 12px, ls .6px | solid-outline square |
| Buy button (PDP) | `bg: var(--buy-btn-border,#212a2f)`, white text, radius 12px, uppercase | primary square |
| Quick-add icon | 24×24 transparent circle, mask SVG cart | icon button |
| Cart count chip | `bg var(--hd-text); color var(--hd-bg); radius 8px; 9px` | badge |
| Dropdown panel | white, radius 10px, shadow `0 4px 16px rgba(0,0,0,.1)` | menu |
| Quantity stepper | `1px solid #d9d9d9; height 36px; radius 8px` | input group |

---

## 5. Spacing & Motion

- **Whitespace:** generous, consistent. Sections use `py-12 md:py-16`-class rhythm; container `mx-auto max-w-7xl px-4`.
- **Corner language:** cards radius 0, badge radius 14px, pills 20px, square buttons 12px. Mixed but intentional — cards are sharp, controls are rounded.
- **Animations (keyframes in brand CSS):** `ab-fadeUp`, `ab-zoomIn`, `ab-blurReveal`, `ab-slideCascade`, `ab-fadeRight`, `ab-flipUp`, plus a marquee. Scroll-reveal via `[data-section-anim]` + `.ab-anim-visible` with stagger delays (0.05s…0.85s). Respects `prefers-reduced-motion`.
- **Carousels:** Swiper (`--swiper-navigation-size: 44px; --swiper-theme-color: #007aff`). Prev/next arrows + dots.

---

## 6. Section Inventory → Shopixy Section Mapping

| Source section | Shopixy section type | Notes |
|---|---|---|
| Floating pill header | `layout.header` | variant `floating` |
| Announcement bar | `layout.announcement` | marquee mode |
| Hero Swiper | `section.hero-banner` | multi-slide variant |
| Shop by collection (3 tiles) | `section.category-grid` | 3 cells |
| Best Sellers grid | `section.product-grid` | featured variant + quick-add |
| The spring edit banner | `section.hero-banner` | variant 2 |
| MEN/WOMEN collections | `section.product-grid` | list-card variant |
| Shop the look | `section.product-grid` (curated) or new | lead image + linked products |
| Before/After slider | NEW `section.before-after` | custom component |
| SHOES band | `section.category-grid` / promo | full-width tile |
| Black footer | `layout.footer` | token-driven |

---

## 7. Fonts & Assets

- **Fonts:** Jost (400/500/600/700/800) via Google Fonts — already in the source's `<link>`. Add to `PointOfSale.Storefront/index.html`.
- **Icons:** CSS-mask SVGs served from `files.easy-orders.net` (search, cart, lang, user, social, trash). For Shopixy, reuse the platform's existing icon set or inline equivalent masks.
- **Images:** all remote CDN (easy-orders.net / cdn.easyorders.shop). In Shopixy these come from the tenant's product/category media.

---

## 8. Build / Port Plan (recommended phases)

1. **T1.0 — Scaffold + tokens:** copy `fashion` → `<id>` (byte-identical per platform pitfalls), add `ThemeConfig` (§2.2), register in `StorefrontThemes.cs`, `ThemeRegistry.ts`, `themeMarketplaceRegistry.js`. Add Jost to `index.html`.
2. **T1.1 — Layout tokens:** add `<id>/layout/...Tokens.ts` (container, section padding, nav-link class, eyebrow) per `pearlTokens.ts` model.
3. **T1.2 — Visual pass:** apply header `floating` pill, black footer, sharp cards, red sale price/badge, outlined pill buttons, Jost everywhere.
4. **T1.3 — New sections:** `before-after` comparison slider; Swiper multi-slide hero; "shop the look" curated grid. Gate behind capabilities.

> Platform caveat (from shopixy-storefront-theme skill): `pearl/themeConfig.ts` is currently **dormant** (not imported by the runtime) — tokens only take effect once wired into `cssVariables.themeToCssVariables` + `applyTheme`. The same applies to any new theme: ThemeConfig alone is the scaffold; the visual phase must connect it.

---

## 9. Evidence Appendix (real measurements)

All values below were read live from the rendered page via `getComputedStyle`:

- `body` font-family: `-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto…` (overridden to Jost by brand CSS on elements)
- Heading font (h2): Jost, 48px, weight 500, ls 0.64px
- Hero h1: serif (ui-serif, Georgia…), 60px, weight 500, color rgb(250,247,242)
- Nav link: 12px, weight 500, uppercase, ls 0.96px, #000
- Product name: Jost, 16px, weight 500, uppercase
- Sale price: #cd1515, 18px, line-height 1
- Discount badge: bg rgb(201,74,74), #fff, 11px, weight 600, ls 0.66px, radius 14px, padding 3px 8px
- Card image box: bg #fff, radius 0, aspect 4/5
- VIEW ALL btn: transparent, 1px solid rgba(33,42,47,.35), radius 20px, Jost 600 11px, ls 1.1px
- Header: white, radius 12px, shadow 0 1px 4px rgba(0,0,0,.08), margin 19px auto, max-width calc(100%-40px)
- Footer: bg rgb(32,32,33), color #fff
- Brand CSS vars: `--hd-bg #FFF`, `--hd-text #000`, `--ft-bg #202021`, `--ft-text #FFF`, `--body-bg #ece9e2`, `--body-text #131316`, `--product-sale-price-text #cd1515`, `--color-primary 0,0,0`, `--ann-bg #212121`
- Stylesheets: Jost Google Fonts link ×2, `_next/static/css/...css`, `files.easy-orders.net/…style.css` (brand override, 132 KB)

---

*End of report. Token blocks in §2.2 and the section map in §6 are structured to drop directly into a new Shopixy storefront theme folder.*

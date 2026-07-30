# ALIZON Storefront Theme — Full Design Report

Source: https://prime-general.myeasyorders.com/
Site name: ALIZON (title tag + wordmark)
Engine identified from CSS class prefixes: `ab-*` (ab-header, ab-plist-*, ab-pgrid-*, ab-cart-count, ab-featured-*, ab-compare-float__btn, ab-footer-*), `mcp-multi-cat__tab`, `fsc-flash-sale__code-btn`, `newsletter-luxe__submit`, `hr-reviews__*` → the site runs on the **EasyOrders / easy-orders.net** SaaS storefront engine. The report below is a faithful, measurable teardown you can use to rebuild the same look as a **new "alizon" theme** inside `PointOfSale.Storefront`.

All colors, fonts, and sizes below were extracted live from the rendered page (computed styles via browser console), not guessed.

---

## 1. Overall Vibe

A **general-merchandise superstore** with a **dark header / white page** layout. The header is a solid near-black bar (`#131921`) with white text and orange/red accents. The page body is clean white with generous vertical spacing. Products are organized into horizontal category shelves (Mobiles & Accessories, Kitchen, Furniture, Bags, Watches, Fashion) each with a section heading, a "View All" link, and a horizontal product carousel/grid.

Key visual traits:
- **Dark header** (`#131921`) with white nav text, orange "Offers Channel" button (`#FF0000`)
- **White page** (`#FFFFFF`) body with light gray section backgrounds
- **Red sale badges** (`#FF0000`) on all discounted products
- **Red discount price** (`#FF0000`) on sale items, with old price struck-through in dark gray (`#121212`)
- **Black product names** (`#131316`), clean sans-serif typography
- **Flash sale** section with countdown timer and promo code button (`#FFD814` yellow button on dark)
- **Black footer** (`#121212`) with white text and social icons
- **Horizontal product carousels** with navigation arrows (white circular buttons)
- **Sticky compare button** floating on the right side of the viewport

---

## 2. Exact Color Palette

All values extracted from computed styles.

| Token | Value | Where used |
|---|---|---|
| Header background | `#131921` (rgb 19,25,33) | Top header bar, nav links, footer top |
| Page background | `#FFFFFF` (white) | Body, all product sections |
| Cart badge / red accent | `#FF0000` (rgb 255,0,0) | Cart count badge, "Offers Channel" button, sale % badges |
| Sale price text | `#FF0000` (rgb 255,0,0) | Discounted product prices |
| Old/strikethrough price | `#121212` (rgb 18,18,18) | Original price with line-through |
| Product name / body text | `#131316` (rgb 19,19,22) | Product titles, body copy |
| Section heading | `#121212` (rgb 18,18,18) | H2 category titles |
| Footer background | `#121212` (rgb 18,18,18) | Footer |
| Footer text | `#FFFFFF` (white) | Footer links, social |
| Flash sale button | `#FFD814` (rgb 255,216,20) | Promo code button "Use Code: GET25" |
| Flash sale button text | `#000000` (black) | On yellow button |
| Carousel nav arrows | `#F9F9F9` (rgb 249,249,249) | White circular prev/next buttons |
| Carousel nav border | `rgba(33,42,47,0.35)` | Border on carousel arrow buttons |
| Newsletter submit button | `#121212` (rgb 18,18,18) | Black "Sign Up" button |
| Compare float button | `#121212` (rgb 18,18,18) | Floating compare button |
| Color swatch (black) | `#000000` (rgb 0,0,0) | Product variant swatch |
| Color swatch (brown) | `#8B4513` (rgb 139,69,19) | Product variant swatch |
| Search box text | `color(srgb 1 1 1 / 0.8)` | Search input over dark header |

---

## 3. Typography

Two families loaded from Google Fonts:

- **Jost** (weights 400, 500, 600, 700, 800) — primary UI / body / product names / prices / nav / buttons.
- **Newsreader** (weights 200, 400) — serif display font (loaded but not visibly used in the main page layout; available for hero headings).
- **Cairo** (weights 400, 500, 700, 900) — Arabic font support.

The body font-family falls back to the system stack (`-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen, Ubuntu, Cantarell, "Fira Sans", "Droid Sans", "Helvetica Neue", sans-serif`).

Measured specs:

| Element | Font | Size | Weight | Color | Notes |
|---|---|---|---|---|---|
| H2 section title | System stack | 25px | 600 | `#121212` | Centered, letter-spacing 0.5px |
| H3 (product name on card) | Jost | 16px | 800 | `#131316` | Bold, not uppercase |
| Product name (list) | Jost | 16px | 400 | `#131316` | Normal case |
| Sale price | Jost | 15px | 600 | `#FF0000` | Red, bold |
| Old price | Jost | 15px | 400 | `#121212` | Line-through |
| Non-sale price | Jost | 15px | 600 | `#131316` | Bold, no strike |
| Sale badge (%) | Jost | 11px | 400 | `#FFFFFF` | On `#FF0000` red, no border radius |
| Nav links | Jost | 16px | 400 | `#FFFFFF` | On dark header |
| "Offers Channel" button | Jost | 16px | 400 | `#FFFFFF` | On `#FF0000` red |
| Flash sale heading | Jost | 16px | 400 | `#131316` | "Flash Sale now on!" |
| Flash sale countdown | Jost | 16px | 400 | `#131316` | Numbers only |
| Promo code button | Jost | 15px | 400 | `#000000` | On `#FFD814` yellow, `border-radius: 999px` |
| Newsletter submit | Jost | 15.2px | 400 | `#FFFFFF` | On `#121212` black, `border-radius: 999px` |
| Footer eyebrow | Jost | 12px | 500 | `#FFFFFF` | Uppercase, wide tracking |
| Footer heading | Jost | 16px | 800 | `#FFFFFF` | Bold |
| Footer links | Jost | 16px | 400 | `#FFFFFF` | Normal |

---

## 4. Page Structure (full section order)

1. **Top utility strip** — social icons (Facebook, Instagram, X, LinkedIn, TikTok, YouTube, Snapchat, WhatsApp) + "Welcome to Our Store" + "Free Shipping Above 10.000 L.E" + policy links (Shipping Policy, Terms of Use, Return and Exchange Policy).
2. **Main header** — dark `#131921` bar: "ALIZON" wordmark (left), search box (center), Cart button (right).
3. **Primary navigation** — inline nav links: Mobiles & Accessories, Perfumes, Watches, Fashion, Bags, Furniture, More. Plus "Offers Channel" button (red).
4. **Product compare float** — sticky circular button on the right edge: "Compare (0)".
5. **Category tabs** — horizontal scrollable tabs: Best Sellers, New Arrivals, Mobiles & Accessories, Kitchen, Furniture, Fashion, Baby & Toys, Perfumes, Watches, Bags.
6. **Hero banner** — full-width promotional image.
7. **Mobiles & Accessories shelf** — H2 title "Mobiles & Accessories" + "View All" + horizontal product grid (6 cards with red discount badges).
8. **Kitchen shelf** — H2 "Kitchen" + "View All" + product grid (10 cards, various discounts).
9. **Furniture shelf** — H2 "Furniture" + "View All" + product grid (8 cards).
10. **Bags shelf** — H2 "Bags" + "View All" + product grid (10 cards).
11. **Flash Sale section** — dark background: "Flash Sale now on!" heading, countdown timer (20:11:07:53), "Save on modern table office, best sellers + more", yellow "Use Code: GET25" button.
12. **Watches shelf** — H2 "Watches" + "View All" + product grid (10 cards, no discounts).
13. **Fashion shelf** — H2 "Fashion" + "View All" + product grid (8 cards with small discounts).
14. **Footer** — black `#121212`: columns for Shop links, Pages, Contact, Social icons, Payment methods.

---

## 5. Product Card Spec (critical detail)

Two card variants observed:

### List-style card (ab-plist-card) — used in Mobiles & Accessories, Fashion shelves:
- **Image**: top of card, landscape orientation, white background.
- **Discount badge**: red `#FF0000`, white 11px Jost, `padding 1px 5px`, **no border radius** (sharp), sits top-left over the image.
- **Product name**: Jost 16px, 400 weight, `#131316`, normal case.
- **Price row**: sale price in red `#FF0000` (15px, 600 weight) + old price struck-through in `#121212` (15px, 400 weight).
- **Color swatch**: tiny dots under the name — black `#000000` and brown `#8B4513` — when variants exist.
- **No** add-to-cart button visible on the card face.
- **No** wishlist heart, **no** quick-view.

### Grid-style card (ab-pgrid-card) — used in Kitchen, Furniture, Bags shelves:
- **Image**: top of card, white background.
- **Discount badge**: red `#FF0000`, white 11px Jost, sharp (no radius), top-left.
- **Product name**: Jost 16px, 400 weight, `#131316`.
- **Price row**: same as list — red sale price + struck-through old price.
- **No** color swatches, **no** add-to-cart on card.

### Featured card (ab-featured-badge) — used in Fashion shelf:
- Similar to grid but with a red `ab-featured-badge--sale` badge.
- Same pricing pattern: red sale price + struck-through old price.

---

## 6. Button System

| Button | Style |
|---|---|
| "Offers Channel" | Solid red `#FF0000` bg, white text, no border radius |
| Carousel prev/next | Circular (50% radius), `#F9F9F9` bg, `#121212` text, `1px solid rgba(33,42,47,0.35)` border |
| Promo code "Use Code: GET25" | Solid yellow `#FFD814` bg, black text, `border-radius: 999px` (pill) |
| Newsletter "Sign Up" | Solid black `#121212` bg, white text, `border-radius: 999px` (pill) |
| Footer subscribe | White bg, `#232F3B` (rgb 35,47,62) text, `border-radius: 999px` (pill), `0px` border |
| Compare float | Black `#121212` bg, white text, `8px` border radius (top corners only) |
| Footer accordion triggers | Transparent bg, white text, no border radius |

---

## 7. How This Maps to Your Storefront Theme System

Your `PointOfSale.Storefront` already has a clean multi-theme architecture:

- **Theme authority**: `theme-authority/shopixy-theme-authority.json` — the canonical source of truth for theme definitions. New themes are added here.
- **Theme registry**: `src/storefront/themes/ThemeRegistry.ts` — reads from `themeAuthority.themes`, filters for `officialStatus === 'official' && activationStatus === 'active' && runtimeAvailability`, and exposes `AVAILABLE_THEMES`.
- **Section resolver**: `src/storefront/themes/ThemeSectionResolver.ts` — uses `import.meta.glob('./*/sections/*.tsx')`, so **any folder you add under `themes/<id>/sections/` is auto-registered**.
- **Theme components**: `src/components/theme-components/ThemeComponents.tsx` — the `ThemeComponentTheme` type and all styled components (ThemeSurface, ThemeSectionHeader, ThemeButton, ThemeBadge, ThemeProductTitle, ThemePrice, etc.) use a `Record<ThemeComponentTheme, ...>` pattern. **Adding a new theme requires adding entries to every Record in this file.**
- **Commerce browsing**: `src/storefront/commerce/commerceTheme.ts` + `CommerceBrowsingExperience.tsx` — maps `themeId` to `ThemeComponentTheme` via `commerceBrowsingTheme` in the authority JSON. Commerce page styles, grid styles, and merchandising blocks are per-theme Records.
- **Per-theme sections**: each theme has `layout/header.tsx`, `layout/footer.tsx`, `sections/hero-banner.tsx`, `sections/category-grid.tsx`, `sections/product-grid.tsx`.
- **Theme config → CSS vars**: `src/theme/cssVariables.ts` maps `ThemeConfig` to CSS custom properties applied on `:root` by `applyTheme.ts`.
- **Theme selection**: `config.storefrontTheme` (string id) → resolved by `ThemeResolver.ts` → consumed by `ThemeContext`.

The closest existing theme to ALIZON is **hype** (dark header, orange/red accents, white page, product shelves). However, ALIZON uses a **red** (`#FF0000`) accent instead of hype's orange (`#F06736`), and has a **yellow flash sale button** and **black footer** (hype uses black footer too, but with orange text).

---

## 8. Implementation Guide — Create an "alizon" Theme

### Step 1 — Add to theme authority JSON

In `theme-authority/shopixy-theme-authority.json`, add a new theme entry:

```json
{
  "id": "alizon",
  "displayName": "Alizon",
  "version": "1.0.0",
  "officialStatus": "official",
  "activationStatus": "active",
  "backendAvailability": true,
  "marketplaceVisibility": "visible",
  "runtimeAvailability": true,
  "homepageAvailability": true,
  "componentTheme": "alizon",
  "commerceBrowsingTheme": "alizon",
  "supportedSections": ["hero-banner", "product-grid", "category-grid"],
  "supportedLayouts": ["header", "footer"],
  "supportedBuilderFeatures": ["homepage", "catalog", "product-template"],
  "supportedProductCards": ["alizon"],
  "supportedProductTemplates": ["alizon"],
  "supportedPurchaseProfiles": ["alizon"],
  "capabilities": ["layout.header", "layout.footer", "section.hero-banner", "section.product-grid", "section.category-grid"],
  "marketplace": {
    "previewImage": "/theme-previews/alizon.png",
    "description": "A general-merchandise superstore theme with dark header, red accents, and product shelves.",
    "accent": "#FF0000"
  },
  "defaultThemeConfig": {
    "colors": {
      "primary": "#FF0000",
      "secondary": "#121212",
      "background": "#FFFFFF",
      "text": "#131316",
      "muted": "#6D6D74"
    },
    "font": {
      "family": "'Jost', sans-serif",
      "familyAr": "'Cairo', 'Tajawal', 'Noto Sans Arabic'",
      "baseSize": "16px"
    },
    "ui": {
      "borderRadius": "0px",
      "buttonStyle": "solid"
    },
    "layout": {
      "productCardVariant": "compact",
      "headerVariant": "standard"
    }
  }
}
```

### Step 2 — Add `alizon` to the `ThemeComponentTheme` type

In `src/components/theme-components/themeComponentTypes.ts`, add `'alizon'` to the union:

```ts
export type ThemeComponentTheme =
    | 'modern'
    | 'minimal'
    | 'fashion'
    | 'pearl'
    | 'phoenix'
    | 'hype'
    | 'electronics'
    | 'alizon'
    | 'default';
```

### Step 3 — Add `alizon` entries to all `Record<ThemeComponentTheme, ...>` maps

In `src/components/theme-components/ThemeComponents.tsx`, add `'alizon'` entries to every Record:

**surfaceStyles** — product card surface:
```ts
alizon: 'overflow-hidden border border-muted/20 bg-background shadow-sm transition-all duration-fast hover:border-primary/50 hover:shadow-md',
```

**sectionHeaderStyles** — section title + "View All":
```ts
alizon: {
    wrapper: 'mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between',
    eyebrow: 'text-[11px] font-semibold uppercase tracking-[0.16em] text-primary',
    title: 'text-2xl font-semibold leading-tight text-text sm:text-3xl',
    subtitle: 'mt-2 max-w-xl text-sm leading-6 text-muted',
    action: 'inline-flex items-center gap-1 text-sm font-semibold uppercase tracking-[0.14em] text-text transition-colors hover:text-primary',
},
```

**categoryArt** — placeholder gradients for category cards (when no image):
```ts
alizon: [
    'bg-[linear-gradient(135deg,#131921_0%,#1E293B_50%,#FF0000_50%,#FFFFFF_100%)]',
    'bg-[linear-gradient(135deg,#0F1729_0%,#1E293B_48%,#FFD814_48%,#FFFFFF_100%)]',
    'bg-[linear-gradient(135deg,#1E293B_0%,#334159_52%,#FF0000_52%,#F1F5F9_100%)]',
    'bg-[linear-gradient(135deg,#0F1729_0%,#1E293B_50%,#FF0000_50%,#FFFFFF_100%)]',
],
```

**buttonStyles** — primary CTA button:
```ts
alizon: 'rounded-none bg-primary px-6 py-3 text-sm font-semibold text-white transition-colors duration-fast hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50 focus-visible:ring-offset-2',
```

**ThemeBadge** — sale/discount badge:
```ts
alizon: {
    primary: 'bg-primary text-white',
    accent: 'bg-primary text-white',
    muted: 'border border-muted/20 bg-background text-muted',
},
```
And the badge class:
```ts
alizon: 'px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.12em]',
```

**ThemeLabel** — product meta label:
```ts
alizon: 'text-[11px] font-semibold uppercase tracking-[0.14em] text-muted',
```

**ThemeProductTitle** — product name on card:
```ts
alizon: 'line-clamp-2 text-sm font-normal leading-snug text-text sm:text-base',
```

**ThemeProductMeta** — product meta (e.g. "In Stock"):
```ts
alizon: 'text-[11px] font-semibold uppercase tracking-[0.14em] text-muted',
```

**ThemePrice** — price display:
```ts
// wrapper
alizon: 'flex items-baseline gap-2 tabular-nums',
// current (sale) price
alizon: 'text-lg font-bold text-primary tabular-nums',
// muted (regular) price
alizon: 'text-sm font-semibold text-muted tabular-nums',
// compare-at (old) price
alizon: 'text-sm text-muted line-through',
```

**ThemeImageFrame** — product image container:
```ts
alizon: 'relative aspect-[4/3] overflow-hidden bg-muted/5',
```

**ThemeLoadingSkeleton** — loading placeholder:
```ts
alizon: 'animate-pulse border border-muted/20 bg-[linear-gradient(90deg,rgba(255,0,0,0.06)_1px,transparent_1px),linear-gradient(180deg,rgba(255,0,0,0.06)_1px,transparent_1px)] bg-[size:20px_20px]',
```

**ThemeEmptyState** — empty state:
```ts
alizon: {
    wrapper: 'rounded border border-muted/20 bg-background px-6 py-10 text-center text-muted shadow-sm',
    title: 'text-lg font-semibold text-text',
    message: 'mx-auto mt-2 max-w-md text-sm leading-6',
},
```

**ThemeCategoryCard** — category card (add a new `if (theme === 'alizon')` block before the default):
```tsx
if (theme === 'alizon') {
    const artClass = categoryArt.alizon[index % categoryArt.alizon.length];
    return (
        <AffiliateLink href={href} className="group block overflow-hidden border border-muted/20 bg-background shadow-sm transition-all duration-fast hover:-translate-y-1 hover:border-primary/50 hover:shadow-md">
            <article className="relative aspect-[4/3] min-h-[12rem] overflow-hidden">
                <div className={cx('absolute inset-0 transition-transform duration-normal group-hover:scale-[1.03]', artClass)} />
                <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(19,25,33,0.72)_0%,rgba(19,25,33,0.18)_100%)]" />
                <div className="absolute inset-x-4 bottom-4">
                    <h3 className="overflow-hidden text-lg font-semibold leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">{title}</h3>
                </div>
            </article>
        </AffiliateLink>
    );
}
```

### Step 4 — Add commerce browsing styles

In `src/storefront/commerce/commerceTheme.ts`, the `normalizeCommerceTheme` function already maps via the authority JSON's `commerceBrowsingTheme` field, so adding `"commerceBrowsingTheme": "alizon"` in the authority JSON is sufficient.

In `src/storefront/commerce/CommerceBrowsingExperience.tsx`, add `alizon` entries to every `Record<ThemeComponentTheme, ...>`:

**commercePageStyles**:
```ts
alizon: {
    page: 'bg-[linear-gradient(180deg,rgba(255,0,0,0.04)_0%,transparent_18rem)]',
    container: 'container mx-auto px-4 py-6 md:py-10',
    breadcrumb: 'mb-5 text-sm',
    header: 'mb-7 rounded border border-muted/20 bg-background p-5 shadow-sm md:p-6',
    eyebrow: 'text-xs font-bold uppercase tracking-[0.14em] text-primary',
    title: 'text-2xl font-bold leading-tight text-text md:text-4xl',
    subtitle: 'mt-3 max-w-2xl text-sm leading-6 text-muted',
    controls: 'rounded border border-muted/20 bg-background p-3 shadow-sm',
    controlPanel: 'flex items-center gap-2 flex-wrap',
    categoryNav: 'mb-5 rounded border border-muted/20 bg-background p-2',
    main: 'flex gap-4',
    sidebar: 'hidden lg:block',
    resultPanel: 'min-w-0 flex-1',
    activeFilters: 'mb-5 rounded border border-primary/20 bg-primary/5 p-3',
    pagination: 'mt-8 border-t border-muted/25 pt-6',
},
```

**commerceGridStyles**:
```ts
alizon: 'grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4',
```

**getCommerceGridItemClass** — no special case needed; returns `''` by default.

**CommerceMerchandisingBlock** — add an `if (theme === 'alizon')` block:
```tsx
if (theme === 'alizon' || theme === 'electronics' || theme === 'hype') {
    return (
        <section className="mb-5 grid gap-3 rounded-md border border-muted/25 bg-background p-3 sm:grid-cols-3" aria-label="Decision support summary">
            <Metric label="Available" value={String(inStockCount)} />
            <Metric label="Brands" value={String(brandCount)} />
            <Metric label="Offers" value={String(promotedCount)} />
        </section>
    );
}
```

**searchPageStyles** — add `alizon` entry matching the hype/electronics pattern:
```ts
alizon: {
    page: 'flex-1 container mx-auto px-4 py-6',
    header: 'mx-auto mb-6 max-w-4xl rounded-md border border-muted/25 bg-background p-4 shadow-sm',
    eyebrow: 'mb-2 text-[11px] font-bold uppercase tracking-[0.14em] text-primary',
    title: 'mb-4 text-2xl font-black text-text md:text-3xl',
    resultWrap: 'mx-auto max-w-5xl',
    tabs: 'mb-5 flex gap-1 overflow-x-auto pb-2',
    activeTab: 'bg-primary text-white',
    inactiveTab: 'border border-muted/25 text-muted hover:border-primary/40 hover:text-text',
    meta: 'mb-3 text-xs font-semibold text-muted',
    list: 'overflow-hidden rounded-md border border-muted/25 bg-background divide-y divide-muted/10',
    pagerButton: 'border border-muted/25 text-text hover:border-primary/50',
    disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
},
```

### Step 5 — Scaffold the theme folder

```bash
cd PointOfSale.Storefront/src/storefront/themes
cp -r hype alizon
```

This gives you `alizon/layout/{header,footer}.tsx` and `alizon/sections/{hero-banner,category-grid,product-grid}.tsx`. The resolver picks them up automatically.

### Step 6 — Recolor the sections

- **`alizon/layout/header.tsx`**: Change the header background from `#F06736` (orange) to `#131921` (dark blue-black). Change nav link color to `#FFFFFF`. Change "Offers Channel" button from orange to `#FF0000`. Change the cart icon background to `#131921`.
- **`alizon/layout/footer.tsx`**: Change footer background from `#000000` to `#121212`. Change text from `#F06736` (orange) to `#FFFFFF`.
- **`alizon/sections/hero-banner.tsx`**: Replace the dark `#141218` hero with a white background. Use a lighter hero style matching the ALIZON aesthetic.
- **`alizon/sections/product-grid.tsx`**: The product cards will automatically use the `alizon` theme component styles (red badges, red prices, black names). Adjust the section wrapper to use `bg-white` instead of `bg-[#141218]`.
- **`alizon/sections/category-grid.tsx`**: Use the `ThemeCategoryCard` with `themeId="alizon"` — the new category card block handles the rendering.

### Step 7 — Add theme tokens file

Create `alizon/layout/alizonTokens.ts` (copy from `hypeTokens.ts` and adjust):

```ts
export const ALIZON_CONTAINER = 'mx-auto max-w-7xl px-4';
export const ALIZON_SECTION_PADDING = 'py-12 md:py-16';
export const ALIZON_FONT_DISPLAY = "font-['Newsreader',serif]";
export const ALIZON_FONT_BODY = "font-['Jost',sans-serif]";
export const ALIZON_NAV_LINK = `${ALIZON_FONT_BODY} text-[16px] font-medium`;
export const ALIZON_EYEBROW = `${ALIZON_FONT_BODY} text-[12px] font-semibold uppercase tracking-[0.32em]`;
export const alizonNavLinkClass = (isActive: boolean): string => {
    const base = `${ALIZON_NAV_LINK} transition-colors`;
    return isActive ? `${base} text-[#FFFFFF]` : `${base} text-[#FFFFFF]/70 hover:text-[#FFFFFF]`;
};
export const ALIZON_BRAND_BG = 'bg-[#131921]';
export const ALIZON_SALE_BG = 'bg-[#FF0000]';
export const ALIZON_NEW_BG = 'bg-[#FFD814]';
export const ALIZON_HERO_BG = 'bg-[#FFFFFF]';
export const ALIZON_FOOTER_BG = 'bg-[#121212]';
export const ALIZON_HEADING = 'text-[#121212]';
```

### Step 8 — Verify

```bash
cd PointOfSale.Storefront
npm run dev
# set VITE_ENABLE_STOREFRONT_THEME_DEV_OVERRIDE=true and localStorage 'storefront_theme'='alizon'
```

Confirm: dark `#131921` header with white text, red `#FF0000` "Offers Channel" button, white page body, red sale badges, red sale prices with struck-through old prices, black product names, black `#121212` footer with white text.

---

## 9. Quick Reference — "Alizon" Design Tokens (copy/paste)

```
BG_PAGE        #FFFFFF
HEADER_BG      #131921   (dark blue-black)
FOOTER_BG      #121212   (black)
RED_ACCENT     #FF0000   (offers button, sale badges, sale price)
SALE_PRICE     #FF0000
OLD_PRICE      #121212   (line-through)
PRODUCT_NAME   #131316
SECTION_H2     #121212
FLASH_BTN      #FFD814   (yellow promo button)
FLASH_BTN_TXT  #000000
FOOTER_TEXT    #FFFFFF
CAROUSEL_BG    #F9F9F9
CAROUSEL_BORDER rgba(33,42,47,0.35)

FONTS          Jost (UI/body) + Newsreader (serif display) + Cairo (Arabic)
SECTION_H2     25px / 600 / system stack / #121212 / centered
PRODUCT_NAME   16px / 400 / Jost / #131316 / normal case
SALE_PRICE     15px / 600 / Jost / #FF0000
OLD_PRICE      15px / 400 / Jost / #121212 / line-through
BADGE          11px / Jost / #FFF on #FF0000 / radius 0 (sharp)
NAV            16px / Jost / #FFFFFF on #131921
FLASH_BTN      15px / Jost / #000000 on #FFD814 / radius 999px (pill)
NEWSLETTER     15px / Jost / #FFFFFF on #121212 / radius 999px (pill)
RADIUS         sharp (0) corners for cards/buttons; pill for CTA buttons
CURRENCY       EGP, thousands comma-separated
```

---

## 10. Gaps / Things to Decide

- The source is a **demo SaaS template** (EasyOrders). Some blocks (flash sale with countdown timer, sticky compare button, color swatches, social media strip in the header) are not in your core `THEME_CAPABILITIES` and will need custom homepage sections or header modifications.
- **Flash sale section**: Not in the core `capabilities` list. Add as a custom homepage section (`sections/flash-sale.tsx`) with a countdown timer component.
- **Sticky compare button**: Not in the core system. Add as a floating UI element in the header or a layout component.
- **Color swatches**: The product card system doesn't currently render color swatches. Add to the product card component if needed.
- **Social media strip**: The top utility strip with social icons is not in the current header. Add as an announcement bar or header extension.
- **Mobile breakpoint spec**: Recommend a 4-col → 2-col → 1-col responsive grid for product rows, matching the existing `hype` theme's responsive classes.
- **Arabic (RTL) support**: Keep `familyAr` so the palette carries into RTL mode. The dark header with white text works well in both LTR and RTL.

# Sharp Jewelry Storefront Theme — Full Design Report

Source: https://sharp-jewelry.myeasyorders.com/
Engine identified from CSS class prefixes: `ps-*` (ps-header, ps-pgrid-*, ps-plist-*, ps-announce-*, ps-footer, ps-cat-tile, ps-desktop-nav-link, ps-sale-badge, ps-plist-price-current, ps-plist-price-old, ps-plist-badge, ps-plist-arrow, ps-compare__heading, ps-nav-top-item, ps-mobile-link, ps-icon-btn, ps-menu-btn, ps-hd-icon-masked, ps-footer-heading, ps-pgrid-title), `newsletter-immersive` → the site runs on the **EasyOrders / easy-orders.net** SaaS storefront engine. The report below is a faithful, measurable teardown you can use to rebuild the same look as a **new "sharp" theme** inside `PointOfSale.Storefront`.

All colors, fonts, and sizes below were extracted live from the rendered page via `getComputedStyle` (computed styles), not guessed. CSS custom properties were read from `:root`.

---

## 1. Overall Vibe

Luxury jewelry boutique ("AURORA" brand). **Light/white page** with a **deep emerald green (#013423) header + announcement bar + footer**. Accent color is a **warm red (#FF5A5A)** used for carousel arrows and hover states. Typography is clean and modern: **Instrument Sans** as the primary UI/body font, **Playfair Display** as a serif display font for the newsletter heading. **Almarai** is loaded for Arabic (RTL) support. Product photography is clean white-background studio shots. The layout is dense with many product carousels per category (Rings, Necklaces, Earrings, Bracelets), each with a carousel navigation. Discount badges use the format "Save -X%" (note the minus sign). Prices are in EGP.

---

## 2. Exact Color Palette

All values extracted from `:root` CSS custom properties and computed styles.

| Token | Hex | CSS Var | Where Used |
|---|---|---|---|
| Page background | `#FFFFFF` | `--ps-page-bg` | Body + all sections |
| Header / announcement / footer bg | `#013423` (rgb 1,52,35) | `--ft-bg`, `--ann-bg` | Header, announcement bar, footer |
| Header text | `#FFFFFF` | `--ft-text`, `--ann-text` | Header nav, announcement |
| Accent (red) | `#FF5A5A` (rgb 255,90,90) | `--ps-header-nav-accent` | Carousel arrows, hover accents |
| Red badge (sale) | `#E32C2B` (rgb 227,44,43) | `--ps-sale-strong` | Discount badges |
| Sale color | `#F2627B` | `--ps-sale` | Sale-related elements |
| Product name text | `#013423` (rgb 1,52,35) | `--product-name-text` | Product card titles |
| Price text (current) | `rgba(3,3,2,0.76)` | `--ps-card-price-muted` | Current/sale price |
| Price text (old/strikethrough) | `#013423` (rgb 1,52,35) | `--ps-price-strike` | Old price (line-through) |
| Section heading | `#013423` (rgb 1,52,35) | `--hd-text` | H2 section titles |
| Body text | `#131316` (rgb 19,19,22) | `--ps-card-ink`, body color | General text |
| Footer text | `#FFFFFF` | `--ft-text` | Footer links |
| Border (light) | `#D1D1D1` | `--ps-border-light` | Section dividers |
| Border (e0) | `#E0E0E0` | `--ps-border-e0` | Input borders |
| Border (e3) | `#E3E3E3` | `--ps-border-e3` | Section borders |
| Border (section) | `#E5E5E5` | `--ps-border-section` | Section separators |
| Border (lavender) | `#E5E5EF` | `--ps-border-lavender` | Subtle borders |
| Border (faint) | `#EDEDED` | `--ps-border-faint` | Divider lines |
| Border (muted) | `#D2D2D2` | `--ps-border-muted` | General borders |
| Gray 350 | `#C8C8C8` | `--ps-gray-350` | Disabled states |
| Gray 400 | `#A8A8A8` | `--ps-gray-400` | Placeholder text |
| Gray 500 | `#878787` | `--ps-gray-500` | Secondary text |
| Gray 600 | `#676665` | `--ps-gray-600` | Muted text |
| Gray warm | `#9A9590` | `--ps-gray-warm` | Warm gray accents |
| Charcoal | `#0D0D12` | `--ps-charcoal` | Dark text |
| Black | `#000000` | `--ps-black` | Pure black |
| Surface 2 | `color-mix(in srgb, #101010 3%, #ffffff)` | `--ps-surface-2` | Subtle surface |
| Surface muted | `color-mix(in srgb, #101010 8%, #ffffff)` | `--ps-surface-muted` | Muted surface |
| Surface subtle | `color-mix(in srgb, #101010 4%, #ffffff)` | `--ps-surface-subtle` | Subtle surface |
| Surface checkout | `color-mix(in srgb, #101010 5%, #ffffff)` | `--ps-surface-checkout` | Checkout surface |
| Card ink | `#1D242D` | `--ps-card-ink` | Card text |
| Ink muted | `#303030` | `--ps-ink-muted` | Muted ink |
| Ink secondary muted | `color-mix(in srgb, #101010 65%, transparent)` | `--ps-ink-secondary-muted` | Secondary muted |
| Thanks hero ink | `#111111` | `--ps-thanks-hero-ink` | Thank you page |
| Thanks hero ink muted | `color-mix(in srgb, #111 50%, transparent)` | `--ps-thanks-hero-ink-muted` | Thank you page muted |

### Background colors observed on the page
- `#FFFFFF` (white) — page, product grids, category tiles
- `#013423` (emerald) — header, announcement, footer
- `#E3E3E3` — category tile placeholder bg
- `#23001B` — color swatch on product cards (inferred from badge scan)
- Various `rgba(0,0,0,0.x)` overlays for image contrast

### Text colors observed
- `#013423` (emerald) — headings, product names, old prices
- `#131316` (near-black) — body text
- `#161616` (near-black) — secondary text
- `#181814` (near-black) — tertiary text
- `#252525` (near-black) — quaternary text
- `#FFFFFF` (white) — header/footer text
- `#FF5A5A` (red) — accent text
- `rgba(3,3,2,0.76)` — muted price text

---

## 3. Typography

Multiple fonts are loaded from Google Fonts (the site supports multiple storefronts, so several families are preloaded):

**Primary font (this store):**
- **Instrument Sans** (weights 400, 500, 600, 700) — UI/body/names/prices/headers

**Secondary/display fonts (loaded but check usage):**
- **Playfair Display** (weights 400, 500, 600, 700) — serif display (used for NEWSLETTER heading)
- **Manrope** (weights 400–700) — alternative body
- **Montserrat** (weights 400–700) — alternative body
- **Nunito** (weights 400–700) — alternative body
- **Jost** (weights 400–800) — alternative body (used by other EasyOrders stores)
- **Almarai** (weights 400, 700) — Arabic/RTL support

**Google Fonts URL:**
```
https://fonts.googleapis.com/css2?family=Instrument+Sans:wght@400;500;600;700&family=Almarai:wght@400;500;600;700&family=Manrope:wght@400;500;600;700&family=Montserrat:wght@400;500;600;700&family=Nunito:wght@400;500;600;700&family=Playfair+Display:wght@400;500;600;700&display=swap
```

### Measured typography specs

| Element | Font | Size | Weight | Color | Notes |
|---|---|---|---|---|---|
| Body | Instrument Sans | 16px | 400 | #131316 | Base body font |
| Section H2 (Rings, Necklaces, etc.) | Instrument Sans | 24px | 400 | #013423 | Centered for "Shop By Collection", left/start for category rows |
| Section H2 (CHOOSE YOUR COLOUR) | Instrument Sans | 34px | 600 | #12100E | Centered, letter-spacing 2.72px |
| Newsletter H2 | Manrope | 44px | 700 | #FFFFFF | Centered, letter-spacing 2.64px, on emerald bg |
| Footer H4 | Instrument Sans | 16px | 400 | #FFFFFF | Footer column headings |
| Nav links (desktop) | Instrument Sans | 14px | 400 | #FFFFFF | Over emerald header |
| Nav links (mobile) | Instrument Sans | 14px | 400 | #013423 | Over white bg |
| Category tile labels | Instrument Sans | 16px | 400 | #131316 | On #E3E3E3 bg |
| Product name | Instrument Sans | 16px | 400 | #013423 | Left-aligned |
| Current price | Instrument Sans | 12px | 500 | rgba(3,3,2,0.76) | `ps-plist-price-current` |
| Old price (strikethrough) | Instrument Sans | 12px | 400 | #013423 | `ps-plist-price-old`, line-through |
| Discount badge | Instrument Sans | 12px | 700 | #FFFFFF | On #E32C2B bg, padding 6px 10px, no border radius |
| Carousel arrow buttons | Instrument Sans | 16px | 400 | #FF5A5A | Circular, 32×32, border 1px solid #FF5A5A |
| Announcement bar | Instrument Sans | 14px | 400 | #FFFFFF | On #013423 bg |
| "SHOP NOW" buttons | Instrument Sans | 16px | 400 | #013423 | On #E3E3E3 bg, in category hero regions |
| "SUBSCRIBE" button | Instrument Sans | 16px | 400 | #013423 | In newsletter form |
| Form input | Instrument Sans | 16px | 400 | #013423 | Border #D1D1D1 |

---

## 4. Page Structure (full section order)

Extracted from the accessibility snapshot (98 elements total):

1. **Announcement bar** — emerald (#013423) strip with "Welcome to Our Store" text + Free Shipping message. Carousel nav (Previous/Next buttons).
2. **Header** — emerald (#013423) bg, height 58px. Left: nav links (Necklaces, Bracelets, Rings, Earrings, Shipping Policy, Return And Exchange Policy). Center: "AURORA" brand. Right: Search link, Cart button.
3. **Rings hero region** — dark section with "Rings" heading + "SHOP NOW" button.
4. **"Shop By Collection"** — centered H2 (24px, #013423).
5. **Category tiles row** — NECKLACES / BRACELETS / RINGS / EARRINGS as full-width image tiles (#E3E3E3 bg), each linking to category.
6. **Rings product carousel** — H2 "Rings" (24px, #013423) + carousel nav (Prev/Next) + 8 product cards in a horizontal carousel.
7. **Necklaces hero region** — "Necklaces" + "SHOP NOW" button.
8. **Necklaces product carousel** — H2 "Necklaces" + "View All" link + 7 product cards in carousel.
9. **"CHOOSE YOUR COLOUR"** — centered section: "LE DALIA" label + H2 "CHOOSE YOUR COLOUR" (34px, 600, letter-spacing 2.72px) + "Drag to compare photos" button.
10. **Earrings product carousel** — H2 "Earrings" + carousel nav + 7 product cards.
11. **Bracelets hero region** — "Bracelets" + "SHOP NOW" button.
12. **Bracelets product carousel** — H2 "Bracelets" + "View All" link + 9 product cards.
13. **Newsletter section** — emerald (#013423) bg. H2 "NEWSLETTER" (44px, 700, Manrope, letter-spacing 2.64px) + email input + "SUBSCRIBE" button.
14. **Footer** — emerald (#013423) bg, white text. Columns: SHOP (Necklaces, Bracelets, Rings, Earrings), PAGES (Shipping Policy, Return and Exchange Policy, Terms of Use, Privacy Policy), CONTACT US (Phone, Email, Address), SUBSCRIBE TO OUR NEWSLETTER (email input + button). Social icons (Facebook, Instagram, X, LinkedIn, TikTok, YouTube, Snapchat, WhatsApp). Payment methods image. "EASY ORDERS" credit link.

---

## 5. Product Card Spec (critical detail)

Extracted from computed styles on product card elements:

- **Container**: White bg (`rgb(255,255,255)`), flex column, left-aligned text, 16px base font.
- **Image**: Studio product photos on white bg. Images measured at 475×475px (aspect 1:1) for individual product shots. Aspect ratio is square (1:1).
- **Discount badge**: Red `#E32C2B` (rgb 227,44,43), white text, Instrument Sans 12px/700, padding `6px 10px`, **no border radius** (sharp), width ~88-94px, height ~28px. Class: `ps-plist-badge ps-sale-badge`. Text format: "Save -X%" (note the minus sign, e.g. "Save -5%", "Save -18%").
- **Product name**: Instrument Sans, 16px, 400, color `#013423` (rgb 1,52,35). Left-aligned. Full product name including material (e.g. "Pear Halo Diamond & Red Stone Ring In 18K Gold").
- **Price**: Instrument Sans, 12px, 500, color `rgba(3,3,2,0.76)` for current price (`ps-plist-price-current`). Old/strikethrough price: 12px, 400, color `#013423`, line-through (`ps-plist-price-old`). Currency: EGP (e.g. "77750 EGP").
- **Layout**: Name and prices are in separate spans. The card is a link (`<a>`) wrapping image + text.
- **No** add-to-cart button visible on the card face. No wishlist heart. No quick-view. No color swatches observed.
- **Carousel**: Products are in horizontal carousels with Prev/Next arrow buttons (32×32, circular, red #FF5A5A border + text).

---

## 6. Button System

| Button | Style |
|---|---|
| Carousel Prev/Next arrows | Circular 32×32, border 1px solid #FF5A5A, text #FF5A5A, padding 6px, border-radius 31px (circular). Class: `ps-plist-arrow ps-plist-arrow-prev/next` |
| "SHOP NOW" (category hero) | On #E3E3E3 bg, text #013423, 16px, 400. No border, no padding. |
| "SUBSCRIBE" (newsletter) | Text #013423, 16px, 400, padding 6px. On form input area. |
| "Drag to compare photos" | Text button, 16px, 400, color #013423 (inferred from body text). |
| Announcement carousel nav (Prev/Next) | 40×40, transparent bg, white text, 12px, 500, no border radius. Class: `ps-announce-mc-nav` |

---

## 7. How This Maps to Your Storefront Theme System

Your `PointOfSale.Storefront` has a clean multi-theme architecture:

- **Theme authority (source of truth)**: `theme-authority/shopixy-theme-authority.json` — the canonical JSON manifest. Each theme entry needs `id`, `displayName`, `officialStatus: "official"`, `activationStatus: "active"`, `runtimeAvailability: true`, `componentTheme`, `commerceBrowsingTheme`, and `defaultThemeConfig`.
- **ThemeComponentTheme union**: `src/components/theme-components/themeComponentTypes.ts` — add `'sharp'` to the union type.
- **ThemeComponents Records**: `src/components/theme-components/ThemeComponents.tsx` — every `Record<ThemeComponentTheme, ...>` map needs a `'sharp'` entry (surfaceStyles, sectionHeaderStyles, categoryArt, buttonStyles, ThemeBadge toneStyles + themeClass, ThemeLabel, ThemeProductTitle, ThemeProductMeta, ThemePrice, ThemeImageFrame, ThemeLoadingSkeleton, ThemeEmptyState). ThemeCategoryCard needs an `if (theme === 'sharp')` block.
- **Commerce browsing styles**: `src/storefront/commerce/CommerceBrowsingExperience.tsx` — add `'sharp'` entries to `commercePageStyles`, `commerceGridStyles`, `searchPageStyles` Records, and `getCommerceGridItemClass`.
- **Section resolver (auto-glob)**: `src/storefront/themes/ThemeSectionResolver.ts` — uses `import.meta.glob('./*/sections/*.tsx')` (lazy). Adding a folder `themes/sharp/sections/*.tsx` auto-registers; no manual wiring needed.
- **Theme config → CSS vars**: `src/theme/cssVariables.ts` + `applyTheme.ts` — `ThemeConfig` → CSS custom properties on `:root`.
- **Existing theme folders**: `themes/{modern,minimal,fashion,pearl,phoenix,hype,electronics,alizon}/{layout/*.tsx, sections/*.tsx}`. Copy `fashion` (editorial) or `pearl` (luxury) as the closest base.

**Three registration points required** — missing any one silently falls back to `modern`:
1. `theme-authority/shopixy-theme-authority.json` — add theme entry
2. `themeComponentTypes.ts` — add `'sharp'` to union
3. `ThemeComponents.tsx` — add `'sharp'` to every `Record<ThemeComponentTheme, ...>`
4. `CommerceBrowsingExperience.tsx` — add `'sharp'` to commerce Records
5. `themes/sharp/` folder — copy from `fashion` or `pearl`

---

## 8. Implementation Guide — Create a "Sharp" Theme

### Step 1 — Register in theme authority

In `theme-authority/shopixy-theme-authority.json`, add a new theme entry (after the `alizon` entry, before `electronics`):

```json
{
  "id": "sharp",
  "displayName": "Sharp Jewelry",
  "version": "1.0.0",
  "officialStatus": "official",
  "activationStatus": "active",
  "backendAvailability": true,
  "marketplaceVisibility": "visible",
  "runtimeAvailability": true,
  "homepageAvailability": true,
  "componentTheme": "sharp",
  "commerceBrowsingTheme": "sharp",
  "supportedSections": ["hero-banner", "product-grid", "category-grid"],
  "supportedLayouts": ["header", "footer"],
  "supportedBuilderFeatures": ["homepage", "catalog", "product-template"],
  "supportedProductCards": ["sharp"],
  "supportedProductTemplates": ["sharp"],
  "supportedPurchaseProfiles": ["sharp"],
  "capabilities": ["layout.header", "layout.footer", "section.hero-banner", "section.product-grid", "section.category-grid"],
  "marketplace": {
    "previewImage": "/theme-previews/sharp.png",
    "description": "A luxury jewelry storefront theme with emerald green header, white canvas, and carousel product browsing.",
    "accent": "#FF5A5A"
  },
  "defaultThemeConfig": {
    "colors": {
      "primary": "#FF5A5A",
      "secondary": "#013423",
      "background": "#FFFFFF",
      "text": "#131316",
      "muted": "#013423"
    },
    "font": {
      "family": "'Instrument Sans', sans-serif",
      "familyAr": "'Almarai', 'Tajawal', 'Noto Sans Arabic'",
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

### Step 2 — Add to ThemeComponentTheme union

In `src/components/theme-components/themeComponentTypes.ts`, add `'sharp'` to the union:

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
    | 'sharp'
    | 'default';
```

### Step 3 — Add to ThemeComponents Records

In `src/components/theme-components/ThemeComponents.tsx`, add `'sharp'` entries to every `Record<ThemeComponentTheme, ...>`:

- `surfaceStyles` — white bg, no border, no shadow (clean jewelry card)
- `sectionHeaderStyles` — emerald (#013423) title, centered for collection, left for rows
- `categoryArt` — 4 gradient placeholders in emerald/white tones
- `buttonStyles` — red accent (#FF5A5A) primary
- `ThemeBadge` toneStyles + themeClass — red badge (#E32C2B) for sale
- `ThemeLabel` styles — emerald text
- `ThemeProductTitle` styles — Instrument Sans 16px, emerald color
- `ThemeProductMeta` styles — Instrument Sans 12px, muted
- `ThemePrice` wrapperStyles + currentStyles + mutedStyles + compareStyles
- `ThemeImageFrame` styles — square aspect (1:1)
- `ThemeLoadingSkeleton` styles — clean pulse
- `ThemeEmptyState` styles — clean
- `ThemeCategoryCard` — add `if (theme === 'sharp')` block

### Step 4 — Add to CommerceBrowsingExperience Records

In `src/storefront/commerce/CommerceBrowsingExperience.tsx`, add `'sharp'` entries to:
- `commercePageStyles` Record — emerald gradient page bg
- `commerceGridStyles` Record — square grid
- `getCommerceGridItemClass` — add conditional if needed
- `CommerceMerchandisingBlock` — add `if (theme === 'sharp')` block
- `searchPageStyles` Record

### Step 5 — Scaffold theme folder

```bash
cd PointOfSale.Storefront/src/storefront/themes
cp -r pearl sharp
# gives sharp/layout/{header,footer,AnnouncementMarquee,pearlTokens}.tsx
# gives sharp/sections/{hero-banner,category-grid,product-grid}.tsx
```

Rename `pearlTokens.ts` → `sharpTokens.ts` and update references.

### Step 6 — ThemeConfig

Already set in the authority JSON `defaultThemeConfig` above. The `themeConfig.ts` file in the theme folder should export the matching config:

```ts
import type { ThemeConfig } from '@/types';

export const sharpThemeConfig: ThemeConfig = {
    colors: {
        primary: '#FF5A5A',
        secondary: '#013423',
        background: '#FFFFFF',
        text: '#131316',
        muted: '#013423',
    },
    font: {
        family: "'Instrument Sans', sans-serif",
        familyAr: "'Almarai', 'Tajawal', 'Noto Sans Arabic'",
        baseSize: '16px',
    },
    ui: {
        borderRadius: '0px',
        buttonStyle: 'solid',
    },
    layout: {
        productCardVariant: 'compact',
        headerVariant: 'standard',
    },
};

export default sharpThemeConfig;
```

### Step 7 — Load fonts

In `sharp/layout/header.tsx` (or `index.html`), add the Google Fonts link:

```html
<link href="https://fonts.googleapis.com/css2?family=Instrument+Sans:wght@400;500;600;700&family=Almarai:wght@400;500;600;700&family=Playfair+Display:wght@400;500;600;700&display=swap" rel="stylesheet">
```

Note: Playfair Display is used for the NEWSLETTER heading (44px, 700, Manrope — but the CSS var `--ps-ff-display` is set to Playfair Display). The body font is Instrument Sans. Load both.

### Step 8 — Recolor checklist

- [ ] `hero-banner.tsx`: White bg, emerald (#013423) header, centered "Shop By Collection" H2, category tiles row, red (#FF5A5A) carousel arrows.
- [ ] `product-grid.tsx`: Square (1:1) product cards, white bg, name 16px #013423, price 12px, red (#E32C2B) sharp discount badge "Save -X%", red (#FF5A5A) circular carousel arrows.
- [ ] `category-grid.tsx`: Full-width image tiles (#E3E3E3 placeholder), "SHOP NOW" buttons.
- [ ] `header.tsx`: Emerald (#013423) bg, white text, centered "AURORA" brand, Instrument Sans 14px nav links.
- [ ] `footer.tsx`: Emerald (#013423) bg, white text, columns Shop/Pages/Contact/Newsletter/Social.
- [ ] `AnnouncementMarquee.tsx`: Emerald (#013423) bg, white text, carousel nav.

### Step 9 — Verify

```bash
cd PointOfSale.Storefront
npm run dev
# set VITE_ENABLE_STOREFRONT_THEME_DEV_OVERRIDE=true
# localStorage 'storefront_theme'='sharp'
```

Confirm: white page, emerald header with centered "AURORA" brand, white announcement bar, "Shop By Collection" centered H2, category tiles row, product carousels with red circular arrows, red sharp discount badges, emerald footer with white text.

---

## 9. Quick Reference — "Sharp" Design Tokens (copy/paste)

```
BG_PAGE        #FFFFFF
HEADER_FOOTER  #013423   (emerald green)
ANNOUNCEMENT   #013423   (emerald green)
ACCENT_RED     #FF5A5A   (carousel arrows, hover)
RED_BADGE      #E32C2B   (discount "Save -X%" badges)
SALE_COLOR     #F2627B
PRODUCT_NAME   #013423   (emerald)
PRICE_CURRENT  rgba(3,3,2,0.76)
PRICE_OLD      #013423   (line-through)
SECTION_H2     #013423   (emerald)
BODY_TEXT      #131316
FOOTER_TEXT    #FFFFFF
BORDER_LIGHT   #D1D1D1
BORDER_E0      #E0E0E0
BORDER_E3      #E3E3E3
BORDER_SECTION #E5E5E5
BORDER_FAINT   #EDEDED
BORDER_MUTED   #D2D2D2

FONTS          Instrument Sans (UI/body) + Playfair Display (display) + Almarai (Arabic)
BODY           16px / 400 / Instrument Sans / #131316
SECTION_H2     24px / 400 / Instrument Sans / #013423 / centered (collection) or left (rows)
SECTION_H2_ALT 34px / 600 / Instrument Sans / #12100E / centered / ls 2.72px (CHOOSE YOUR COLOUR)
NEWSLETTER_H2  44px / 700 / Manrope / #FFFFFF / ls 2.64px
PRODUCT_NAME   16px / 400 / Instrument Sans / #013423
PRICE_CURRENT  12px / 500 / Instrument Sans / rgba(3,3,2,0.76)
PRICE_OLD      12px / 400 / Instrument Sans / #013423 / line-through
NAV_LINKS      14px / 400 / Instrument Sans / #FFFFFF (desktop) or #013423 (mobile)
BADGE          12px / 700 / Instrument Sans / #FFF on #E32C2B / radius 0 / padding 6px 10px
CAROUSEL_ARROW 16px / 400 / Instrument Sans / #FF5A5A / 32x32 circular / border 1px #FF5A5A
CURRENCY       EGP, space-separated (e.g. "77750 EGP")
CARD_ASPECT    1:1 (square product images)
RADIUS         sharp (0) corners
```

---

## 10. Gaps / Things to Decide

- The source is a **demo SaaS template** (EasyOrders). The "CHOOSE YOUR COLOUR" section with "Drag to compare photos" is a custom feature not in your core `THEME_CAPABILITIES` and will need a custom homepage section.
- The site uses **horizontal product carousels** per category (Rings, Necklaces, Earrings, Bracelets), each with its own hero "SHOP NOW" button. Your existing `product-grid` section supports `layout: "carousel"` — verify this maps correctly.
- **Discount badge format** is "Save -X%" (with a minus sign, e.g. "Save -5%"). This is unusual — most sites use "Save X%". Decide whether to replicate the minus sign or normalize to "Save X%".
- **No mobile breakpoint spec** was extracted here. Recommend matching the existing `fashion` theme's responsive classes (4-col → 2-col → 1-col grid).
- **Arabic (RTL) support**: Almarai is loaded for Arabic. Keep `familyAr: "'Almarai', 'Tajawal', 'Noto Sans Arabic'"` so the emerald/white palette carries into RTL mode.
- **Playfair Display** is loaded but only observed in use for the NEWSLETTER heading (though the CSS var `--ps-ff-display` is set to it). The newsletter H2 actually renders in Manrope. Decide whether to use Playfair Display for display headings or Manrope.
- **Color swatches**: No color swatches were observed on product cards in this store. The existing `pearl` theme has swatch support — decide whether to keep or remove for `sharp`.
- **Payment methods**: The footer has an "Accepted payment methods" image. Your existing footer has a placeholder payment area — add real payment method logos.

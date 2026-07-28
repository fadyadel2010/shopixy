# Hype Electronics Storefront Theme — Design Report (BUILDER-ARCH-1 reference)

> Source analysed live: `https://hype-electronics.myeasyorders.com/` (mobile/electronics storefront, "easyorders" / "myeasyorders" platform).
> All values below were extracted from computed styles + the live stylesheet (`files.easy-orders.net/...style.css`), not guessed.
> Purpose: this is the canonical design spec to implement a new Shopixy storefront theme (proposed id `hype`) mirroring the reference site.
> Analysed against the live Shopixy theme contract: `PointOfSale.Storefront/src/types/storeConfig.ts` (`ThemeConfig`), `ThemeRegistry.ts`, and the existing `pearl` reference theme.

---

## 1. At-a-glance identity

| Property | Value | Notes |
|---|---|---|
| Platform family | EasyOrders mobile store | Not Shopify; pure product-grid + carousel storefront |
| Overall vibe | Bright white, sharp editorial, orange accent, dark luxury hero/footer | "Premium tech" feel |
| Fonts | **Jost** (UI/body/nav/price) + **Newsreader** (serif display, hero/headings) | Both via Google Fonts |
| Base font size | 16px / line-height 24px | |
| Dominant color | Near-black text `#131316` on white `#FFFFFF` | |
| Accent (brand/nav/CTA) | Orange `#F06736` (`--hd-text`) | |
| Sale badge | Orange-red `#F54E05` (`--product-sale-badge-bg`), white text, rounded pill | |
| Custom/"New" badge | Cyan `#27CDE4` (`--product-custom-badge-bg`), white text, rounded pill | |
| Section heading | Dark slate `#212A2F` (the `#212a2f` token appears 236× in the CSS — the de-facto brand ink) | |
| Muted text | Gray `#6D6D74` | |
| Hero bg | Dark `#141218` | full-bleed, blurred product image |
| Footer bg | Pure black `#000000` | white text |
| Corner radius | **Mixed**: product cards `8px`, category cards `0px`, buttons pill (`rounded-full`), inputs rounded | deliberate editorial mix |
| Carousels | Swiper (prev/next 40×40 circular buttons, `--swiper-theme-color: #007aff` default) | product sections are sliding rails |

---

## 2. Design tokens (exact)

### 2.1 Color
| Token | Hex | RGB | Where used |
|---|---|---|---|
| Brand / nav / header text | `#F06736` | 240,103,54 | logo, nav links, active states |
| Sale badge bg | `#F54E05` | 245,78,5 | `-8%` / `-16%` discount pills |
| Custom badge bg ("New") | `#27CDE4` | 39,205,228 | cyan "New" pill |
| Page background | `#FFFFFF` | 255,255,255 | body canvas |
| Body / price text | `#131316` | 19,19,22 | paragraphs, current price |
| Section heading | `#212A2F` | 33,42,47 | H2 titles |
| Muted / secondary text | `#6D6D74` | 109,109,116 | subtitles, captions |
| Hero background | `#141218` | 20,18,24 | main hero band |
| Footer background | `#000000` | 0,0,0 | footer |
| Card surface (light) | `#FCFCFC` / `#FAFAFA` / `#F5F4F2` | — | card/image backdrops |
| Hairline border | `rgba(0,0,0,.06–.15)` / `#E5E7EB` | — | dividers, card edges |
| Highlight (legacy) | `#EE8D63` | — | `--kalles-highlight`, used sparingly |

### 2.2 Typography
| Element | Family | Size | Weight | Tracking | Case |
|---|---|---|---|---|---|
| H1 hero | Newsreader (serif) | 60px | 300 | normal | sentence |
| H2 section title | Jost | 48px | 500 | 0.64px | normal (centered) |
| Eyebrow ("NEW ARRIVALS") | Jost | 12px | 600 | 4.2px | UPPER |
| Nav link | Jost | 16px | 500 | normal | normal |
| Body | Jost | 16px | 400 | — | — |
| Price (current) | Jost | 16px | 400 | — | — |
| Badge | Jost | 12px | 500 | — | UPPER/short |
| Logo wordmark | Jost | ~16–32px | 400 | wide | UPPER |

### 2.3 Spacing / layout
- Container: `max-w-7xl` (1280px) with `px-4`/`px-6` gutters; section bands are full-bleed.
- Header height: 78px desktop / 64px mobile; logo width 100px (`--logo-width-desktop/mobile`).
- Hero padding: `48px 24px`; hero min-height large (centered).
- Product section rhythm: `py-12 md:py-16` equivalent.
- Product card: ~349px wide in carousel, `border-radius:8px`, padding ~10.4px.
- Category card: ~344px, `border-radius:0`, image + "Shop" micro-label + uppercase name.
- Buttons: hero CTA = white pill (`rounded-full`); swiper nav = 40×40 circular; "LOAD MORE" = text link.
- Shadows: cards `rgba(0,0,0,.08)`, hover `.12`, sticky header `0 18px 40px rgba(0,0,0,.22)`.

---

## 3. Page structure (top → bottom, as rendered)

1. **Header (white)** — logo "Mobile Store" (orange, left), inline horizontal nav (center/right), Search + Wishlist(count) + Cart(count) icons (right). Sticky.
   - Nav items: Flagship Phones, Power Banks, Inventions, Smart Watches, Airpods, Chargers & Cable (carries a "New" cyan badge), Shipping Policy, Refund & Exchange Policy.
2. **Announcement marquee** — "LIMITED EDITION COLLECTION" repeated, bullet (`•`) separated, on a light/transparent strip, dark text.
3. **Hero** — dark `#141218` full-bleed band, blurred product image, centered white text: eyebrow "NEW ARRIVALS", H1 "The Best Collection" (Newsreader 60px/300), subtitle "Silhouettes, textures, and tones curated for everyday luxury.", white pill CTA "SHOP COLLECTION".
4. **Shop By Category** — 6 cards (Flagship Phones, Airpods, Power Banks, Smart Watches, Inventions, Chargers & Cable). Each: image, "Shop" label, UPPERCASE name. Square corners.
5. **Product rails (per category)** — Flagship Phones, Airpods, Power Banks, Chargers & Cable. Each a **Swiper carousel** with H2 title + Prev/Next. Cards show: discount pill (`-8%`), hover action icons (wishlist / quick view / add to cart), name, old price (strikethrough) + current price, color-swatch dots.
6. **Comparison slider** — "IPHONE 17 PRO MAX" section with a before/after image slider (circular drag handle). Special interactive block.
7. **Shop the look** — promo band "City nights" (image + "SHOP FULL LOOK" CTA) beside a featured product list (iPhone 17 Pro Max, Samsung S26 Ultra, Oppo Reno15, Honor X9d with prices).
8. **Smart Watches (featured)** — large horizontal product cards (image left, details right), "Sale" badge.
9. **What customers are saying** — testimonials band.
10. **Newsletter** — "Subscribe To Our Newsletter" email input + Subscribe button (also a second standalone newsletter block lower).
11. **Footer (black)** — columns: **Shop** (Flagship Phones, Power Banks, Smart Watches, Inventions, Airpods, Chargers & Cable), **Pages** (Shipping Policy, Terms & Conditions, Refund & Exchange Policy, Privacy Policy), **Contact** (hype-electronics@gmail.com, 01234567890), **Social** (Facebook, Instagram, X, LinkedIn, TikTok, YouTube, Snapchat, WhatsApp), newsletter signup, "easyorders" credit.

---

## 4. Component-by-component spec (for implementation)

### 4.1 Header — variant `standard` (white, inline nav)
- White background (`#FFFFFF`); sticky; on-scroll elevation shadow.
- Logo left in Jost, orange `#F06736`, uppercase, wide tracking; width 100px.
- Nav links: Jost 16px/500, color `#F06736`, hover lighten; active = solid orange.
- Right cluster: Search, Wishlist (badge count), Cart (badge count) icon buttons.
- Mobile: hamburger → drawer; keeps wishlist/cart/lang.
- REUSE (per pearl pattern): `AffiliateLink`, `CartIconButton`, `WishlistIconButton`, `LanguageSwitcher`, `AuthNavItems`, `useNavigationStore`, `useScrollPosition`, `useTranslation`. Only the white/orange shell is theme-specific.

### 4.2 Announcement marquee
- Light strip, dark text `#131316`, Jost 12–16px, repeated "LIMITED EDITION COLLECTION" separated by `•`.
- RTL/localized aware (mirror pearl `AnnouncementMarquee` but default text + bullet separator).
- Enable/disable aware via `enabled` prop.

### 4.3 Hero — dark, centered, serif
- `backgroundColor: #141218`, optional `backgroundImage` (blurred product shot).
- Centered block: eyebrow (Jost 12px/600, tracking 4.2px, UPPER, white), H1 Newsreader 60px/300 `#FAF7F2`, subtitle Jost 16px `#FAF7F2`/80, white pill CTA "SHOP COLLECTION" (`ThemeHeroCTA`, align center).
- Maps to existing `hero-banner` section with settings: `alignment:'center'`, `height:'large'`, `backgroundColor:'#141218'`, `textColor:'#FAF7F2'`, `overlayOpacity` low.
- NOTE: differs from pearl hero (light, two-column image card). This theme customizes the section to dark/centered/full-bleed.

### 4.4 Category grid — 3×2 image cards
- 6 categories, grid `md:grid-cols-3` (2 rows). Card: `border-radius:0`, image top, "Shop" micro-label (Jost 11px UPPER tracking), UPPERCASE name (`#212A2F`).
- Maps to `category-grid` section; needs a Pearl-style `ThemeCategoryCard` variant with square corners + image + "Shop" label. (Pearl's current category card is text-ish; extend for image tiles.)

### 4.5 Product grid / carousel — Swiper rail
- H2 title (Jost 48px/500, centered, `#212A2F`, tracking 0.64px).
- `layout:'carousel'` → horizontal rail w/ Prev/Next (40×40 circular buttons).
- Product card (`ProductCard` platform component, themed):
  - `border-radius:8px`, white surface, subtle shadow on hover.
  - Discount pill: `-8%` style, bg `#F54E05`, white, rounded pill, top-left.
  - Hover actions: wishlist / quick view / add to cart icon buttons (flex, `border-radius:1px` per `--card-action-*`).
  - Name (Jost), old price strikethrough (muted `#6D6D74`), current price `#131316`.
  - Color-swatch dots for variants (small circles).
- Maps directly to existing `product-grid` section (`layout:'carousel'`, `columns`, `rows`).

### 4.6 Comparison slider (NON-CORE)
- Before/after image with circular drag handle ("iPhone 17 Pro Max" promo).
- **Not in current section registry.** Requires a new `section.comparison-slider` component + capability entry (see §6 gaps).

### 4.7 Shop the look (NON-CORE)
- Split band: promo image + "SHOP FULL LOOK" CTA + featured product list.
- **Not in current section registry.** New `section.shop-the-look` needed.

### 4.8 Smart Watches featured (NON-CORE variant)
- Large horizontal cards (image left / details right) with "Sale" badge.
- Can be approximated with `product-grid` + a wide card variant, or a dedicated `section.featured-products`.

### 4.9 Testimonials "What customers are saying" (NON-CORE)
- **Not in current section registry.** New `section.testimonials` needed.

### 4.10 Newsletter + Footer (black)
- Newsletter: email input + Subscribe button (solid orange or outlined).
- Footer: black `#000000`, white text, 3-col grid (Shop / Pages / Contact+Social), newsletter, payment placeholders, "easyorders" credit.
- REUSE pearl `PearlFooter` structure but recolor: orange `#F06736` accent instead of gold `#C9A863`; keep black bg + white text.

---

## 5. Mapping to the Shopixy theme contract

### 5.1 `ThemeConfig` draft (drop-in for `themeConfig.ts`)
```ts
import type { ThemeConfig } from '@/types';

export const hypeThemeConfig: ThemeConfig = {
    colors: {
        primary: '#F06736',     // brand / nav / CTA accent (--hd-text)
        secondary: '#212A2F',    // section heading ink (de-facto brand slate)
        background: '#FFFFFF',   // page canvas
        text: '#131316',         // body + price text
        muted: '#6D6D74',        // secondary / caption text
    },
    font: {
        family: "'Jost', sans-serif",
        familyAr: "'Cairo', 'Tajawal', 'Noto Sans Arabic'",
        baseSize: '16px',
    },
    ui: {
        borderRadius: '8px',     // product cards; category/header/buttons override to 0 / pill in components
        buttonStyle: 'solid',
    },
    layout: {
        productCardVariant: 'compact',
        headerVariant: 'standard',   // white inline nav (vs pearl 'centered')
    },
};

export default hypeThemeConfig;
```
> Hardcoded theme-specific accents (sale badge `#F54E05`, custom badge `#27CDE4`, hero `#141218`) live in the theme's `hypeTokens.ts` + section components — exactly like pearl hardcodes `#DC2626`/`#C9A863`.

### 5.2 `ThemeRegistry.ts` entry
Add (mirroring pearl):
```ts
{
    id: 'hype',
    name: 'Hype Electronics',
    version: '1.0.0',
    capabilities: THEME_CAPABILITIES,
},
```

### 5.3 Section coverage
| Reference section | Shopixy section type | Status |
|---|---|---|
| Hero | `section.hero-banner` | ✅ direct (dark/centered variant) |
| Shop By Category | `section.category-grid` | ✅ direct (image-tile variant) |
| Product rails | `section.product-grid` (`layout:'carousel'`) | ✅ direct |
| Comparison slider | — | ❌ new section |
| Shop the look | — | ❌ new section |
| Smart Watches featured | `section.product-grid` (wide variant) | ⚠️ partial |
| Testimonials | — | ❌ new section |
| Newsletter | footer/contact | ⚠️ partial (footer only) |
| Footer | `layout.footer` | ✅ direct (recolor) |
| Header | `layout.header` | ✅ direct (`standard` variant) |

---

## 6. Build gaps / what is NOT in the frozen contract

The frozen section contract is `{id,type,order,enabled,settings}` with capabilities `layout.header`, `layout.footer`, `section.hero-banner`, `section.product-grid`, `section.category-grid`. To fully reproduce the reference you must ADD (each is a capability + new component under `hype/sections/`):

1. `section.comparison-slider` — before/after drag slider.
2. `section.shop-the-look` — promo + featured list split.
3. `section.testimonials` — "What customers are saying".
4. Category-grid **image-tile variant** (square, "Shop" label) — extend existing component.
5. Product-grid **wide/featured card variant** (image-left/details-right) for Smart Watches.
6. Newsletter as a standalone section (currently only footer-embedded).

These are additive (host-side / new section types) and do **not** break the frozen bridge surface — consistent with ARCH-1.1/1.4R. Each new section still anchors only via `data-section-id`.

---

## 7. Proposed file scaffold (mirror `pearl/`)
```
PointOfSale.Storefront/src/storefront/themes/hype/
  themeConfig.ts                 # §5.1
  layout/
    hypeTokens.ts                # colors/spacing/typography helpers (color-free rhythm)
    header.tsx                   # white inline-nav 'standard' variant
    footer.tsx                   # black footer, orange accent
    AnnouncementMarquee.tsx      # LIMITED EDITION marquee + bullets
  sections/
    hero-banner.tsx              # dark centered serif variant
    category-grid.tsx            # 3x2 image-tile variant
    product-grid.tsx             # carousel + 8px card + sale/custom badges
    comparison-slider.tsx        # NEW (gap 1)
    shop-the-look.tsx            # NEW (gap 2)
    testimonials.tsx             # NEW (gap 3)
```
Plus: register `hype` in `ThemeRegistry.ts`, add `hypeTheme.test.ts` + `hypeLayout.test.tsx` (mirror `pearlTheme.test.ts` / `pearlLayout.test.tsx`), and load Jost + Newsreader in `index.html` (pearl already wires Newsreader; ensure Jost present).

---

## 8. Verification (after scaffold)
```bash
# Storefront type + bridge gate
cd PointOfSale.Storefront
npx tsc --noEmit -p tsconfig.json
npx vitest run src/storefront/themes/hype

# Registry sanity (no duplicate id, exactly one default)
npx vitest run src/storefront/themes/ThemeRegistry.test.ts

# Web bridge parity (unchanged surfaces)
cd ../PointOfSale.Web
npx vitest run src/editor-bridge --reporter=dot
```
Live check: load storefront with `?themePreviewToken` / `themeId=hype`, confirm header white+orange, dark hero, cyan/orange badges, carousels, black footer.

---

## 9. Summary of deliberate design decisions to replicate
- **Mixed radii**: sharp category cards + pill buttons + rounded (8px) product cards.
- **Two oranges**: `#F06736` (brand/nav) vs `#F54E05` (sale badge) — do not merge.
- **Cyan `#27CDE4`** for "New"/custom badges — a signature accent.
- **Dark hero + dark footer** bracketing an otherwise all-white page.
- **Serif display (Newsreader)** reserved for the hero H1 only; everything else Jost.
- **Carousel rails** for every product category, not static grids.

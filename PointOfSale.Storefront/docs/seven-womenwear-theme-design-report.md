# Seven Womenwear ("La Femmea") — Storefront Theme Design Report

**Source:** https://seven-womenwear.myeasyorders.com/
**Generated:** 2026-07-29
**Engine identified from CSS class prefixes:** `ab-*` (ab-header, ab-nav, ab-announce, ab-footer, ab-plist-*, ab-pgrid-*, ab-featured-*, ab-dropdown, ab-mobile-menu, ab-icon-btn, ab-cart-count), `rnw-hero`, `rnw-section`, `rnw-marquee`, `edition-hero-slider`, `newsletter-luxe`, `sale-time` → the site runs on the **EasyOrders / easy-orders.net** SaaS storefront engine. The report below is a faithful, measurable teardown you can use to rebuild the same look as a new **"seven"** theme inside `PointOfSale.Storefront`.

All colors, fonts, and sizes below were extracted live from the rendered page via `getComputedStyle` (computed styles) and `:root` CSS custom properties — verified, not vision-guessed. The brand stylesheet was also extracted from `https://files.easy-orders.net/1784821166811562687style.css`.

---

## 1. Overall Vibe

Women's fashion e-commerce store ("La Femmea"). **Light/white page** with a **deep mauve-terracotta (#9D7877) header + announcement bar + footer**. Accent color is a **warm red (#CD1515)** used for sale badges and sale prices. Typography is clean and modern: **Jost** as the primary UI/body font (400–800 weights), **Outfit** for the preloader and some display elements, and **Newsreader** (serif, 200/400) for hero display headings. The layout features a transparent sticky header that becomes white-on-dark over the homepage hero, a full-width hero carousel with serif display typography, category collection tiles, multiple product grids (plaza list, plaza grid, featured carousel), a countdown timer for archive sales, and a newsletter signup. Product photography is clean model shots. Prices are in EGP.

**Shape language:** Sharp corners throughout (border-radius: 0 on badges, buttons, cards). Hero CTA is a pill (99px radius). No rounded elements except the cart count badge (1rem radius) and search icon button (50% radius).

**Motion:** Swiper carousel with fade transitions, marquee animation for announcements, scroll-reveal animations (hs-inner-* keyframes for clip/mask/shine/stagger), preloader with word-dot-pulse animation.

**Effort verdict:** ~70% achievable via ThemeConfig tokens (palette, fonts, spacing). The hero section with serif display font, the transparent sticky header behavior, the countdown timer, and the marquee announcement require custom section/layout code. The product card with sharp sale badge and the "Add to cart" text button are theme-section hardcodes.

---

## 2. Exact Color Palette

All values extracted from `:root` CSS custom properties and computed styles.

| Token | Hex | CSS Var | Where Used |
|---|---|---|---|
| Page background | `#FFFFFF` | `--body-bg` | Body + all white sections |
| Header / announcement / footer bg | `#9D7877` (rgb 157,120,119) | `--hd-bg`, `--ft-bg`, `--ann-bg` | Header, announcement bar, footer |
| Header text | `#FFFFFF` | `--hd-text`, `--ft-text`, `--ann-text` | Header nav, announcement, footer |
| Sale badge bg | `#CD1515` (rgb 205,21,21) | `--product-badge-bg` | Discount badges (sharp, radius 0) |
| Sale badge text | `#FFFFFF` | `--product-badge-text` | Badge text |
| Sale price text | `#CD1515` (rgb 205,21,21) | `--product-price-text` | Current/sale price on product cards |
| Old price text | `#121212` (rgb 18,18,18) | — | Struck-through old price |
| Product name text | `#242424` (rgb 36,36,36) | — | Product card titles |
| Body text | `#131316` (rgb 19,19,22) | `--body-text` | General text, paragraphs |
| Hero heading text | `#FAF7F2` (rgb 250,247,242) | — | Hero H1/H2 over dark hero |
| Hero paragraph text | `#F4EE E6` (rgb 244,238,230) | — | Hero description text |
| Hero CTA text | `#141218` (rgb 20,18,24) | — | "SHOP NOW" button text |
| Hero CTA bg | `#FFFFFF` | — | "SHOP NOW" button bg |
| View All link | `#121212` (rgb 18,18,18) | — | "View All" links |
| Cart count bg | `#FFFFFF` | — | Cart count badge bg |
| Cart count text | `#000000` | — | Cart count badge text |
| Announcement text | `#FFFFFF` | `--ann-text` | Announcement bar |
| Countdown bg | `#9D7877` | — | Countdown timer bg |
| Countdown text | `#FFFFFF` | — | Countdown timer text |
| Newsletter bg | `#FFFFFF` | — | Newsletter section |
| Newsletter text | `#9D7877` | — | Newsletter heading/input |
| Mobile menu bg | `#FFFFFF` | — | Mobile menu panel |
| Mobile nav text | `#212A2F` (rgb 33,42,47) | — | Mobile menu links |
| Dropdown panel text | `#212A2F` (rgb 33,42,47) | — | Dropdown menu links |
| Search icon | `#FFFFFF` | — | Search button on dark header |
| A11y color | `255, 255, 255` | `--color-a11y` | Accessibility contrast |
| Color primary | `0, 0, 0` | `--color-primary` | Primary color (black) |

### Background colors observed on the page
- `#FFFFFF` (white) — page, product grids, category tiles, newsletter
- `#9D7877` (mauve-terracotta) — header, announcement, footer, countdown
- `#141218` (near-black) — hero overlay gradient base
- `rgba(12,10,14,0.x)` — hero overlay gradient (darkening)
- `rgba(0,0,0,0.x)` — various image overlays

### Text colors observed
- `#FFFFFF` (white) — header/footer/announcement text, hero headings
- `#CD1515` (red) — sale badges, sale prices
- `#121212` (near-black) — old prices, view all links
- `#131316` (near-black) — body text
- `#242424` (near-black) — product names
- `#212A2F` (dark) — mobile menu, dropdown links
- `#F4EE E6` (off-white) — hero description
- `#FAF7F2` (off-white) — hero headings

---

## 3. Typography

Multiple fonts are loaded from Google Fonts:

```
https://fonts.googleapis.com/css2?family=Jost:wght@400;500;600;700;800&display=swap
https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;700&display=swap
https://fonts.googleapis.com/css2?family=Newsreader:wght@200;400&display=swap
```

### Font roles
| Element | Font Family | Size | Weight | Color | Notes |
|---|---|---|---|---|---|
| Body / UI | Jost | 16px | 400 | #131316 | System fallback chain also applied |
| Hero H1 | Jost | 60px | 300 | #FAF7F2 | Left-aligned, line-height 63px |
| Hero H2 | ui-serif (Georgia/Cambria/Times) | 52px | 400 | #FAF7F2 | Centered, line-height 56.16px |
| Hero paragraph | Jost | 17px | 400 | #F4EE E6 | Line-height 28.05px |
| Hero CTA | Jost | 13px | 600 | #141218 | On white bg, pill radius 99px |
| Section H2 | ui-serif (Georgia/Cambria/Times) | 52px | 400 | #FAF7F2 | Centered, line-height 56.16px |
| Nav links | Jost | 12px | 400 | #FFFFFF | Uppercase |
| Product name | Jost | 16px | 400 | #242424 | Line-height 19.2px |
| Product price (current/sale) | Jost | 14px | 400 | #CD1515 | Desktop: 14px, Mobile: 12px |
| Product price (old) | Jost | 16px | — | #121212 | Line-through |
| Sale badge | Jost | 13px | 400 | #FFFFFF | On #CD1515 bg, padding 1px 6px |
| View All | Jost | 12px | 400 | #121212 | Underline |
| Footer heading | Jost | 24px | 400 | #FFFFFF | |
| Footer link | Jost | 16px | — | #FFFFFF | No underline |
| Announcement | System | 11px | 500 | #FFFFFF | Letter-spacing 0.03em |
| Countdown | System | 16px | 400 | #FFFFFF | |
| Mobile nav | Jost | 24px | 400 | #212A2F | Uppercase, letter-spacing 0.06em |
| Dropdown link | Jost | 12px | 300 | #212A2F | Padding 14px 24px |
| Add to cart | System | 12.8px | 600 | #9D7877 | Padding 8px 0 8px 16px |
| Preloader | Outfit | 3.5rem (desktop) / 2.2rem (mobile) | 400 | #FFFFFF | Letter-spacing -0.02em |
| Newsletter input | System | 15.2px | — | #9D7877 | |
| Newsletter btn | System | 12.8px | 600 | #9D7877 | |

### Font weights available
- Jost: 400, 500, 600, 700, 800
- Outfit: 300, 400, 500, 700
- Newsreader: 200, 400

### Key typography notes
- The hero H1 uses **Jost** (not Newsreader) at 60px/300 weight — confirmed from computed style.
- The hero H2 and section H2 use **ui-serif** (Georgia/Cambria/Times fallback) at 52px/400 — this is a system serif, NOT Newsreader. Newsreader is loaded but used for the preloader text ("La Femmea").
- All body text uses Jost with system fallback.
- Nav links are uppercase at 12px.
- Product names are NOT uppercase (unlike Pearl Watches which uses uppercase).
- The "Add to cart" button is a text button (no background), using the brand color #9D7877.

---

## 4. Layout & Component Design

### 4.1 Header
| Property | Value |
|---|---|
| Selector | `.ab-header` |
| Position | Absolute (becomes fixed on scroll) |
| Height | 66px (desktop) / 60px (mobile) |
| Background | Transparent on homepage (becomes `var(--hd-bg, #9D7877)` on scroll) |
| Text color | #FFFFFF (on homepage) |
| Inner layout | Grid: `1fr auto 1fr` (left nav, center logo, right icons) |
| Inner padding | 0px 40px |
| Sticky behavior | `transform: translateY(-100%)` when scrolling down, `translateY(0)` when scrolling up |
| Mobile strip height | 36px |

**Header structure:**
- `.header-col-left` — nav links (H O O D I E S, B L O U S E S & D R E S S E S, S W E A T P A N T S, S P O R T S W E A R, S H I P P I N G P O L I C Y, MORE)
- `.header-col-center` — logo "La Femmea"
- `.header-col-right` — Search, Cart icons

**Nav links:**
- Font: Jost 12px / 400 / uppercase
- Color: var(--hd-text) = #000000 (but #FFFFFF on homepage)
- Gap: 20px
- Hover: underline via `::after` pseudo-element (width 0 → 100%)

**Logo:**
- Font: Jost 16px / 400
- Color: #FFFFFF
- Height: 56px
- Filter: `brightness(0) invert(1)` on homepage (to make dark logo white)

**Icon buttons:**
- Size: 44px × 44px
- Border-radius: 50%
- Color: var(--hd-text)
- Search, Cart, Language, Register icons via CSS mask

**Cart count badge:**
- Background: #FFFFFF
- Text: #000000
- Font-size: 10px / 600
- Border-radius: 1rem
- Position: absolute top:7px right:4px

### 4.2 Announcement Bar
| Property | Value |
|---|---|
| Selector | `.ab-announce` |
| Background | #212A2F (rgb 33,42,47) |
| Text color | #FFFFFF |
| Font | System / 11px / 500 |
| Height | 35px |
| Position | Absolute, top:0, z-index:50 |
| Letter-spacing | 0.03em |
| Text-align | Center |

### 4.3 Hero Section
| Property | Value |
|---|---|
| Selector | `.rnw-hero`, `.eo-home-section`, `.edition-hero-slider` |
| Min-height | 625.6px |
| Background | Dark gradient overlay: `linear-gradient(120deg, rgba(12,10,14,0.58) 0%, rgba(12,10,14,0.4) 45%, rgba(12,10,14,0.22) 100%)` |
| Overlay base | rgba(12,10,14,...) |
| Text color | #131316 (body) but hero text is #FAF7F2 / #F4EE E6 |
| Display | Block |
| Padding | 0px |

**Hero H1:**
- Font: Jost / 60px / 300 / #FAF7F2 / left-aligned / line-height 63px

**Hero H2:**
- Font: ui-serif (Georgia/Cambria/Times) / 52px / 400 / #FAF7F2 / centered / line-height 56.16px

**Hero paragraph:**
- Font: Jost / 17px / 400 / #F4EE E6 / line-height 28.05px / max-width 100%

**Hero CTA ("SHOP NOW"):**
- Background: #FFFFFF
- Color: #141218
- Font: Jost / 13px / 600
- Border-radius: 99px (pill)
- Padding: 8.8px 21.6px
- Border: 0px

**Hero image:**
- Aspect ratio: auto (1502px × 625.594px)
- Object-fit: cover
- Max-width: 100%
- Border-radius: 0px

**Hero animation:**
- Scroll-reveal via `hs-inner-*` classes (clip-reveal, mask-reveal, shine-sweep, stagger-simple, lux-drift)
- Keyframes: `hs-inner-clip`, `hs-inner-mask`, `hs-inner-shine-fade`, `hs-inner-shine-move`, `hs-inner-stagger`

### 4.4 Category Collection Tiles
| Property | Value |
|---|---|
| Selector | `.rnw-collection-card`, `.rnw-collection-item` |
| Layout | Grid of 4 tiles (Hoodies, Blouses & Dresses, Sweatpants, Sports Wear) |
| Text | Centered, category name below image |

**Note:** Category cards were not directly measurable via selector — they appear as links with images and text labels in the accessibility snapshot.

### 4.5 Product Grids

#### Plaza List (ab-plist)
| Property | Value |
|---|---|
| Selector | `.ab-plist-item`, `.ab-plist-*` |
| Product image aspect ratio | 4 / 5 (`--plist-product-aspect-ratio`) |
| Product name | Jost / 16px / 400 / #242424 |
| Product price | Jost / 14px / 400 / #CD1515 (sale) |
| Old price | Jost / 16px / line-through / #121212 |
| Sale badge | #CD1515 bg / #FFFFFF text / Jost 13px / 400 / radius 0 / padding 1px 6px |
| Add to cart | Text button: #9D7877 / Jost 12.8px / 600 / padding 8px 0 8px 16px / radius 0 |

#### Plaza Grid (ab-pgrid)
| Property | Value |
|---|---|
| Selector | `.ab-pgrid-item`, `.ab-pgrid-*` |
| Product image aspect ratio | 4 / 5 (`--pgrid-product-aspect-ratio`) |
| Price size mobile | 12px (`--price-size-mobile`) |
| Price size desktop | 14px (`--price-size-desktop`) |
| Product name size mobile | 12px (`--name-product-size-mobile`) |
| Product name size desktop | 16px (`--name-product-size-desktop`) |

#### Featured Carousel (ab-featured)
| Property | Value |
|---|---|
| Selector | `.ab-featured-item`, `.ab-featured-*` |
| Product image aspect ratio | 4 / 5 (`--featured-product-aspect-ratio`) |
| Layout | Horizontal carousel (swiper) |
| Price | Flex row-reverse, gap 6px |
| Color swatches | 23px × 23px, 2px border, radius 0, margin 3px padding |

### 4.6 Countdown Timer
| Property | Value |
|---|---|
| Selector | `.sale-time`, `.rnw-countdown` |
| Background | #9D7877 |
| Text color | #FFFFFF |
| Font | System / 16px / 400 |
| Layout | Grid of DAYS/HOURS/MIN/SEC with numeric values |

### 4.7 Newsletter
| Property | Value |
|---|---|
| Selector | `.newsletter-luxe`, `.rnw-newsletter` |
| Background | #FFFFFF |
| Text color | #9D7877 |
| Padding | 20px 24px |
| Border-radius | 0px |

**Newsletter input:**
- Background: transparent
- Border: 0px
- Font-size: 15.2px
- Color: #9D7877
- Padding: 8px 0px

**Newsletter button:**
- Background: transparent
- Color: #9D7877
- Font-size: 12.8px / 600
- Padding: 8px 0 8px 16px
- Border-radius: 0px

### 4.8 Footer
| Property | Value |
|---|---|
| Selector | `.ab-footer` |
| Background | #9D7877 |
| Text color | #FFFFFF |
| Font | System / 16px |
| Padding | 0px |
| Text-align | Left |

**Footer heading:**
- Font: Jost / 24px / 400 / #FFFFFF

**Footer links:**
- Font: Jost / 16px / #FFFFFF / no underline

**Copyright:**
- Background: transparent
- Color: #FFFFFF
- Padding: 18px 0px
- Text-align: Left

### 4.9 Mobile Menu
| Property | Value |
|---|---|
| Selector | `.ab-mobile-menu` |
| Position | Fixed, top:0, left:0, bottom:0 |
| Width | 100% (max-width 94%) |
| Background | #FFFFFF |
| Transform | translateX(-100%) → translateX(0) when open |
| Transition | 0.35s cubic-bezier(.4,0,.2,1) |

**Mobile nav links:**
- Font: Jost / 24px / 400 / #212A2F / uppercase / letter-spacing 0.06em
- Border-bottom: 1px solid (color from body-text at 0.2 opacity)

### 4.10 Dropdown Menu
| Property | Value |
|---|---|
| Selector | `.ab-dropdown-panel` |
| Position | Absolute, top:100%, left:50%, transform: translateX(-50%) |
| Background | Transparent (inherits) |
| Border-radius | 10px |
| Box-shadow | 0 4px 16px rgba(0,0,0,0.1) |

**Dropdown links:**
- Font: Jost / 12px / 300 / #212A2F
- Padding: 14px 24px (first child: 14px 24px 10px, last child: 14px 24px 14px)
- Hover: background #F5F0EB

### 4.11 Preloader
| Property | Value |
|---|---|
| Selector | `.word-preloader` |
| Background | #141516 |
| Z-index | 2147483647 |
| Text font | Outfit / 3.5rem (desktop) / 2.2rem (mobile) / 400 / #FFFFFF |
| Letter-spacing | -0.02em |
| Dot | 12px × 12px (desktop) / 8px × 8px (mobile), #FFFFFF, border-radius 50%, animation: word-dot-pulse 1.2s infinite |

---

## 5. Button & Control Specs

| Control | Spec (measured) | Token/Style |
|---|---|---|
| Hero CTA ("SHOP NOW") | bg #FFFFFF, text #141218, Jost 13px/600, radius 99px, padding 8.8px 21.6px, border 0 | Pill button |
| Add to cart (product card) | bg transparent, text #9D7877, system 12.8px/600, radius 0, padding 8px 0 8px 16px, border 0 | Text button |
| Sale badge | bg #CD1515, text #FFFFFF, Jost 13px/400, radius 0, padding 1px 6px | Sharp badge |
| Search button | bg transparent, color #FFFFFF (on dark header), 50% radius, 44px × 44px | Icon button |
| Cart icon button | bg transparent, color var(--hd-text), 50% radius, 44px × 44px | Icon button |
| Cart count badge | bg #FFFFFF, text #000000, 10px/600, radius 1rem, min-width 18px, height 18px | Small badge |
| Newsletter submit | bg transparent, text #9D7877, system 12.8px/600, radius 0, padding 8px 0 8px 16px | Text button |
| Mobile nav link | border-bottom 1px solid rgba(0,0,0,0.2) | Separator |
| Dropdown link | padding 14px 24px, hover bg #F5F0EB | Menu item |
| Swiper pagination bullet | (not measurable — swiper default) | Carousel |

---

## 6. Spacing & Motion

### Spacing
- Header inner padding: 0px 40px
- Hero padding: 0px (full-width image)
- Newsletter padding: 20px 24px
- Product card add-to-cart padding: 8px 0 8px 16px
- Sale badge padding: 1px 6px
- Hero CTA padding: 8.8px 21.6px
- Mobile menu nav padding: 8px 0
- Dropdown link padding: 14px 24px
- Copyright padding: 18px 0

### Motion / Animations
| Animation | Keyframes | Purpose |
|---|---|---|
| ab-marquee | translateX(0) → translateX(-50%) | Announcement marquee |
| ab-fadeUp | translateY(30px) → 0, opacity 0 → 1 | Fade in up |
| ab-cardFadeIn | opacity 0 → 1 | Card fade in |
| ab-shimmer | background-position -200% → 200% | Loading shimmer |
| ab-mobileNavIn | translateY(8px) → 0, opacity 0 → 1 | Mobile menu item enter |
| word-dot-pulse | scale(0.85) → scale(1.15), opacity 0.6 → 1 | Preloader dot |
| hs-inner-clip | clip-path inset(0 0 100% 0) → inset(0 0 0 0) | Hero clip reveal |
| hs-inner-mask | clip-path + translateX | Hero mask reveal |
| hs-inner-shine-fade | opacity 0 → 1 | Shine fade |
| hs-inner-shine-move | translateX + skewX | Shine sweep |
| hs-inner-stagger | translateY(18px) → 0 | Staggered reveal |
| hs-inner-lux-drift | translateY(44px) → 0 | Luxury drift |

### Carousel
- Library: Swiper
- Transition: fade (opacity 0 → 1)
- Navigation: prev/next buttons, pagination bullets
- Swiper theme color: #007aff (default, not customized)

### Corner language
- **Sharp (radius 0):** Badges, buttons, cards, category tiles
- **Pill (radius 99px):** Hero CTA button
- **Round (radius 50%):** Icon buttons (search, cart, language)
- **Round (radius 1rem):** Cart count badge

---

## 7. Section Inventory → Shopixy Section Mapping

| Source section | Shopixy section type | Notes |
|---|---|---|
| Announcement bar | `layout.header` (announcement) | ThemeConfig token: `--ann-bg`, `--ann-text` |
| Transparent sticky header | `layout.header` | Custom: transparent → solid on scroll, logo filter inversion |
| Hero carousel (2 slides) | `section.hero-banner` | Serif display font (ui-serif), dark overlay gradient, scroll-reveal animation |
| Category collection tiles | `section.category-grid` | 4 tiles, image + label |
| Product grid (plaza list) | `section.product-grid` | Sharp sale badge, text "Add to cart" button, 4/5 aspect ratio |
| Product grid (plaza grid) | `section.product-grid` | Same as plist, different layout |
| Featured product carousel | `section.product-grid` (carousel variant) | Horizontal swiper, color swatches, price row |
| Countdown timer (archive sale) | **NEW** — needs custom section | Countdown widget, not in core THEME_CAPABILITIES |
| Newsletter signup | `section.newsletter-signup` | White bg, brand color text, text input + submit |
| Footer | `layout.footer` | Mauve-terracotta bg, 3-column layout |
| Mobile menu | `layout.header` (mobile) | Slide-in panel, full-height |

### Capability classification
| Section | Platform support | Reuse % | Decision |
|---|---|---|---|
| Header | `layout.header` | 80% | Token + custom transparent-scroll behavior |
| Footer | `layout.footer` | 90% | Token recolor only |
| Hero banner | `section.hero-banner` | 70% | Token + serif font + overlay gradient + animation |
| Category grid | `section.category-grid` | 85% | Token + image tiles |
| Product grid | `section.product-grid` | 80% | Token + sharp badge + text button |
| Newsletter | `section.newsletter-signup` | 90% | Token recolor only |
| Countdown timer | **NEW** | 0% | Needs new `section.countdown-timer` capability |
| Mobile menu | `layout.header` | 85% | Token + slide-in animation |

---

## 8. Fonts & Assets

### Google Fonts (extend `index.html` `<link>`)
```html
<link href="https://fonts.googleapis.com/css2?family=Jost:wght@400;500;600;700;800&display=swap" rel="stylesheet">
<link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;700&display=swap" rel="stylesheet">
<link href="https://fonts.googleapis.com/css2?family=Newsreader:wght@200;400&display=swap" rel="stylesheet">
```

### Font assignments
- **Jost** — UI/body/names/prices/nav/announcement/dropdown/mobile nav (primary)
- **Outfit** — Preloader text only
- **Newsreader** — Loaded but NOT used for hero headings (ui-serif is used instead); available for future use

### Arabic font
- No explicit Arabic font loaded. System fallback will be used. Recommend adding `Cairo` or `Tajawal` for Arabic support.

### Assets
- Logo: Text-based "La Femmea" (no image logo found)
- Hero images: Full-width model photography
- Product images: Studio/model shots, 4:5 aspect ratio
- Category images: Collection photography
- Icons: CSS mask-based SVG icons (search, cart, language, register)

---

## 9. Drop-in ThemeConfig Block

```ts
import type { ThemeConfig } from '@/types';

export const sevenThemeConfig: ThemeConfig = {
  colors: {
    primary: '#9D7877',   // Header/announcement/footer bg (mauve-terracotta)
    secondary: '#CD1515', // Sale badge bg + sale price (warm red)
    background: '#FFFFFF', // Page background
    text: '#131316',      // Body text (near-black)
    muted: '#242424',     // Product name text
  },
  font: {
    family: "'Jost', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Fira Sans', 'Droid Sans', 'Helvetica Neue', sans-serif",
    familyAr: "'Cairo', 'Tajawal', 'Noto Sans Arabic', sans-serif",
    baseSize: '16px',
  },
  ui: {
    borderRadius: '0px',      // Sharp corners throughout
    buttonStyle: 'text',      // Text buttons (transparent bg)
  },
  layout: {
    productCardVariant: 'compact',
    headerVariant: 'transparent',  // Custom: transparent → solid on scroll
  },
};
```

### Fonts to add to `index.html`
```html
<!-- Extend existing Google Fonts link -->
<link href="https://fonts.googleapis.com/css2?family=Jost:wght@400;500;600;700;800&family=Outfit:wght@300;400;500;700&display=swap" rel="stylesheet">
```

### CSS variables to set (via `cssVariables.ts` / `applyTheme`)
```css
:root {
  --hd-bg: #9D7877;           /* Header bg */
  --hd-text: #FFFFFF;         /* Header text */
  --ft-bg: #9D7877;           /* Footer bg */
  --ft-text: #FFFFFF;         /* Footer text */
  --ft-muted: rgba(255,255,255,0.6);  /* Footer muted */
  --ft-divider: #666666;      /* Footer divider */
  --ft-input-bg: rgba(247,247,250,0.08);  /* Footer input bg */
  --body-bg: #FFFFFF;         /* Page bg */
  --body-text: #131316;       /* Body text */
  --ann-bg: #212A2F;          /* Announcement bg */
  --ann-text: #FFFFFF;        /* Announcement text */
  --product-badge-bg: #CD1515;  /* Sale badge bg */
  --product-badge-text: #FFFFFF;  /* Sale badge text */
  --product-price-text: #CD1515;  /* Sale price */
  --price-size-mobile: 12px;
  --price-size-desktop: 14px;
  --name-product-size-mobile: 12px;
  --name-product-size-desktop: 16px;
  --plist-product-aspect-ratio: 4 / 5;
  --pgrid-product-aspect-ratio: 4 / 5;
  --featured-product-aspect-ratio: 4 / 5;
  --header-height-desktop: 66px;
  --header-height-mobile: 60px;
  --header-mobile-strip-height: 36px;
  --logo-height-desktop: 120px;
  --logo-height-mobile: 60px;
  --home-logo-filter: brightness(0) invert(1);
  --color-primary: 0, 0, 0;   /* Black (used for a11y) */
  --color-a11y: 255, 255, 255;  /* White (a11y) */
}
```

### ThemeConfig → CSS variable mapping
| ThemeConfig key | CSS variable | Measured value |
|---|---|---|
| `colors.primary` | `--hd-bg`, `--ft-bg` | #9D7877 |
| `colors.secondary` | `--product-badge-bg`, `--product-price-text` | #CD1515 |
| `colors.background` | `--body-bg` | #FFFFFF |
| `colors.text` | `--body-text` | #131316 |
| `colors.muted` | — (product name) | #242424 |
| `font.family` | (global font-family) | Jost |
| `ui.borderRadius` | (component-level) | 0px |
| `ui.buttonStyle` | (component-level) | text (transparent bg) |
| `layout.headerVariant` | — (custom) | transparent-scroll |

---

## 10. Gaps / Decisions

### Blocks NOT in core THEME_CAPABILITIES (need custom sections)
1. **Countdown timer** — The archive sale section with DAYS/HOURS/MIN/SEC countdown is a bespoke widget. Needs a new `section.countdown-timer` capability + component.
2. **Transparent sticky header** — The header is transparent on homepage and becomes solid on scroll. This requires custom scroll-behavior logic in the header component, not just token changes.
3. **Hero scroll-reveal animations** — The `hs-inner-*` animation classes (clip-reveal, mask-reveal, shine-sweep, stagger) are custom animation behaviors that need to be implemented in the hero section component.
4. **Preloader** — The word preloader with dot pulse animation is a custom widget. Needs a new `section.preloader` or layout-level component.
5. **Marquee announcement** — The announcement bar uses a marquee animation (`ab-marquee` with `ab-marquee-track.ab-running`). May need custom implementation depending on whether the platform's announcement supports marquee.

### Decisions
- **Hero display font:** The hero H1 uses Jost (not Newsreader). Newsreader is loaded but used for the preloader. The hero H2 uses ui-serif (system). This is unusual — typically the display font would be a Google Font. Document this as-is.
- **Shape language:** Sharp corners (radius 0) everywhere except hero CTA (pill) and icon buttons (round). This is a deliberate design choice — document it.
- **Button style:** Text buttons (transparent background) throughout, not solid buttons. The `buttonStyle: 'text'` in ThemeConfig reflects this.
- **Product names:** NOT uppercase (unlike Pearl Watches). Keep as-is.
- **Sale badge:** Sharp corners (radius 0), not rounded. The `--product-badge-bg` is #CD1515.
- **Header behavior:** Transparent on homepage, becomes solid on scroll. This is a custom behavior that needs to be implemented in the header component.
- **Arabic support:** No explicit Arabic font loaded. Recommend adding Cairo/Tajawal.

### Engine note
The site runs on EasyOrders SaaS (`easy-orders.net`). The design tokens are fully reusable in PointOfSale.Storefront. The class prefixes (`ab-*`, `rnw-*`, `eo-*`, `edition-hero-slider`, `newsletter-luxe`, `sale-time`) are EasyOrders-specific and will not map directly to Shopixy components — the token values (colors, fonts, sizes) are what matter for theme creation.

---

## 11. Implementation Guide

### Step 1: Register the theme
1. Add `"seven"` to `theme-authority/shopixy-theme-authority.json` with the ThemeConfig above.
2. Add `{ id: 'seven', name: 'Seven Womenwear', version: '1.0.0', capabilities: THEME_CAPABILITIES }` to `AVAILABLE_THEMES` in `ThemeRegistry.ts`.
3. Add an entry to `THEME_MARKETPLACE_REGISTRY` in `themeMarketplaceRegistry.js`.
4. Add `'seven'` to the `ThemeComponentTheme` union in `themeComponentTypes.ts`.
5. Add `'seven'` entries to all `Record<ThemeComponentTheme, ...>` maps in `ThemeComponents.tsx`.
6. Add `'seven'` entries to commerce Records in `CommerceBrowsingExperience.tsx`.

### Step 2: Scaffold the theme folder
```bash
cp -r fashion seven
# under PointOfSale.Storefront/src/storefront/themes/
```

### Step 3: Apply tokens
- Set CSS variables via `themeConfig.ts` + `cssVariables.ts` mapping.
- Add Google Fonts `<link>` to `index.html` (extend existing Jost link, add Outfit).
- Recolor header/footer/sections with the mauve-terracotta (#9D7877) palette.

### Step 4: Custom sections
- **Hero:** Override `themes/seven/sections/hero-banner.tsx` with serif display font, dark overlay gradient, and scroll-reveal animation classes.
- **Countdown timer:** Create new `section.countdown-timer` capability (or document as out-of-scope gap).
- **Transparent header:** Implement scroll-behavior in `themes/seven/layout/header.tsx`.
- **Product grid:** Override with sharp sale badge (#CD1515, radius 0) and text "Add to cart" button (#9D7877).
- **Newsletter:** Override with white bg + brand color text.
- **Footer:** Override with mauve-terracotta bg.

### Step 5: Verify
```bash
cd PointOfSale.Storefront
npx vitest run src/storefront/themes/seven/
npm run build
cd PointOfSale.Web
npx vitest run src/modules/storefront/themes/themeMarketplaceRegistry.test.ts
npm run build
```

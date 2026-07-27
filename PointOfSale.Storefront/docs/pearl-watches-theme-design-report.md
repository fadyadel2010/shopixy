# Pearl Watches Storefront Theme — Full Design Report

Source: https://pearl-watches.myeasyorders.com/
Engine identified from CSS class prefixes: `ab-*` (ab-footer, ab-plist-*, ab-pgrid-*, ab-marquee-*), `rnw-hero`, `eo-home-section`, `newsletter-luxe`, `shop-the-look`, `runway-hero` → the site runs on the **EasyOrders / easy-orders.net** SaaS storefront engine. The report below is a faithful, measurable teardown you can use to rebuild the same look as a **new "pearl" theme** inside `PointOfSale.Storefront`.

All colors, fonts, and sizes below were extracted live from the rendered page (computed styles), not guessed.

---

## 1. Overall Vibe

High-end editorial "everyday luxury" watch boutique. Mostly **light (white) page** with a few **near-black feature blocks** (hero, shop-the-look, runway, newsletter) that create contrast and focus on product photography. Accent color is a **bold red** (CTAs, sale marquee, discount badges) paired with a **gold** footer. Very clean, lots of vertical whitespace, thin uppercase micro-labels, serif display headings.

---

## 2. Exact Color Palette

| Token | Value | Where used |
|---|---|---|
| Page background | `#FFFFFF` (white) | Body + all product-grid sections |
| Hero / dark block bg | `#141218` (rgb 20,18,24) | Hero, "runway" second hero |
| Shop-the-look bg | `#1E1C24` (rgb 30,28,36) | "Shop the look" section |
| Newsletter bg | `#1D1C1C` (rgb 29,28,28) | Newsletter section |
| Footer bg | `#000000` (black) | Footer |
| Hero heading text | `#FAF7F2` (off-white) | Hero H1 "The spring edit" |
| Red accent (marquee) | `#DC2626` (rgb 220,38,38) | LIMITED EDITION scrolling strip |
| Red accent (badges) | `#CD1515` (rgb 205,21,21) | Sale / discount % badges |
| Gold (footer text) | `#C9A863` (rgb 201,168,99) | Footer links / headings |
| Section heading (navy) | `#313E61` (rgb 49,62,97) | H2 section titles (eg "Shop by collection") |
| Product name | `#212A2F` (rgb 33,42,47) | Card product title |
| Price / body text | `#131316` (rgb 19,19,22) | Prices, body copy |
| Nav links | `#FFFFFF` | Top nav over hero |

---

## 3. Typography

Two families loaded from Google Fonts:

- **Jost** (weights 400–800) — primary UI / body / product names / prices / nav / buttons.
- **Newsreader** (weights 200, 400) — serif **display** font for hero headings.

Measured specs:

| Element | Font | Size | Weight | Color | Notes |
|---|---|---|---|---|---|
| Hero H1 ("The spring edit") | Newsreader, serif | 60px | 300 | #FAF7F2 | centered |
| Section H2 ("Shop by collection", etc.) | (serif/light) | 48px | 200 | #313E61 | centered, letter-spacing 0.5px |
| Eyebrow micro-label ("NEW SEASON") | Jost | ~12px | 500 | white/uppercase | wide tracking |
| Nav links | Jost | 12px | 400 | #FFFFFF | uppercase |
| Product name | Jost | 12px | 400 | #212A2F | **uppercase** |
| Price | Jost | 16px | 400 | #131316 | |
| Discount badge | Jost | 11px | 400 | #FFFFFF | on #CD1515 |
| "View All" link | Jost | 12px | 400 | #313E61 | pill, radius 20px |

---

## 4. Page Structure (full section order)

1. **Demo marquee** — thin scrolling strip: "✦ This website is only a Demo site ✦ The products are not real" (site-specific, skip in your build).
2. **Fixed transparent header** — centered wordmark "Pearl Watches", nav links `HOME | MEN WATCHES | WOMEN WATCHES | KIDS WATCHES | WALLETS | MORE`, Search + Cart icons top-right. Transparent over hero, white text.
3. **Hero (centered)** — full-width dark watch photo; eyebrow `NEW SEASON`, H1 `The spring edit`, subtitle, red `SHOP COLLECTION` button.
4. **LIMITED EDITION COLLECTION marquee** — red (#DC2626) scrolling strip, white uppercase text.
5. **Shop by collection** — 4 equal image tiles: Men Watches / Women Watches / Kids Watches / Wallets (image + label, link to category).
6. **Men Watches row** — section title + `View All` (right) + horizontal product grid (8 cards).
7. **Shop the look** — dark (#1E1C24) block: large "City nights" editorial image on left + 3 stacked product thumbnails on right, `SHOP FULL LOOK` button.
8. **Women Watches row** — title + View All + 6-card grid.
9. **See the Transformation** — before/after image comparison slider (50% default).
10. **Kids Watches row** — title + View All + 6-card grid.
11. **Runway / second hero** — dark block repeating `The spring edit` + `SHOP COLLECTION` + `LOOKBOOK` buttons.
12. **Wallets grid** — title + 10-card grid (several discounted) + View All.
13. **Newsletter** — dark (#1D1C1C): "Subscribe to Our Newsletter", email input + `SUBSCRIBE` button, privacy note.
14. **Footer (black, gold text)** — columns: `Shop` (Men/Women/Kids Watches, Wallets), `Pages` (Terms, Shipping, Refund, Privacy), contact phone + email, social icons (Facebook, Instagram, X, TikTok, YouTube, WhatsApp, Snapchat), payment logos, "easyorders" credit.

---

## 5. Product Card Spec (critical detail)

- **Image**: portrait, measured ~360×414px (aspect ≈ 0.87). White card, no visible border/shadow by default.
- **Discount badge**: red `#CD1515`, white 11px Jost, `padding 1px 5px`, **no border radius** (sharp), sits top-left over the image.
- **Add to cart**: small circular icon-only button (38×38) — white icon, transparent bg; appears on the card (hover/always). No text label on the card face.
- **Color swatch**: tiny black dot (`#000`) under the name when variants exist.
- **Name**: Jost 12px, **uppercase**, `#212A2F`.
- **Price**: Jost 16px, `#131316`. Discounted items show old price struck-through + new price.
- **No** wishlist heart, **no** quick-view, **no** compare slider observed.

---

## 6. Button System

| Button | Style |
|---|---|
| SHOP COLLECTION / primary CTA | Solid **red** (#DC2626) bg, white text, square corners (radius 0), uppercase |
| SHOP FULL LOOK / SUBSCRIBE | Muted/gold outline or solid dark pill, white text |
| LOOKBOOK | Outlined (dark border), white text |
| View All | Plain text link, navy #313E61, 12px, pill radius 20px on container |

---

## 7. How This Maps to Your Storefront Theme System

Your `PointOfSale.Storefront` already has a clean multi-theme architecture:

- **Theme registry**: `src/storefront/themes/ThemeRegistry.ts` — `AVAILABLE_THEMES` array (currently `modern`, `minimal`, `fashion`).
- **Section resolver**: `src/storefront/themes/ThemeSectionResolver.ts` — uses `import.meta.glob('./*/sections/*.tsx')`, so **any folder you add under `themes/<id>/sections/` is auto-registered**.
- **Per-theme sections**: each theme has `layout/header.tsx`, `layout/footer.tsx`, `sections/hero-banner.tsx`, `sections/category-grid.tsx`, `sections/product-grid.tsx` (copy the `fashion` theme as your starting template — it's the closest match).
- **Theme config → CSS vars**: `src/theme/cssVariables.ts` maps `ThemeConfig` to `--color-*`, `--font-family`, `--border-radius`, etc. applied on `:root` by `applyTheme.ts`.
- **Theme selection**: `config.storefrontTheme` (string id) in `src/types/storeConfig.ts` → resolved by `ThemeResolver.ts` → consumed by `ThemeContext`.

So building "Pearl" = (a) add it to the registry, (b) copy `fashion` theme folder → `pearl`, (c) recolor/retype via `ThemeConfig` + section component edits, (d) set `storefrontTheme: 'pearl'`.

---

## 8. Implementation Guide — Create a "Pearl" Theme

### Step 1 — Register the theme
In `src/storefront/themes/ThemeRegistry.ts`, add to `AVAILABLE_THEMES`:
```ts
{
  id: 'pearl',
  name: 'Pearl',
  version: '1.0.0',
  capabilities: THEME_CAPABILITIES,
},
```

### Step 2 — Scaffold the theme folder
```bash
cd PointOfSale.Storefront/src/storefront/themes
cp -r fashion pearl
```
This gives you `pearl/layout/{header,footer}.tsx` and `pearl/sections/{hero-banner,category-grid,product-grid}.tsx`. The resolver picks them up automatically.

### Step 3 — ThemeConfig (colors + fonts)
In your store config (or `DEFAULT_STORE_CONFIG` for local dev), set:
```ts
storefrontTheme: 'pearl',
theme: {
  colors: {
    primary: '#DC2626',     // red CTA / accent
    secondary: '#C9A863',   // gold (footer)
    background: '#FFFFFF',
    text: '#131316',
    muted: '#313E61',       // navy section headings
  },
  font: {
    family: "'Jost', sans-serif",
    familyAr: "'Cairo', 'Tajawal', sans-serif",
    baseSize: '16px',
  },
  ui: {
    borderRadius: '0px',    // sharp, editorial corners
    buttonStyle: 'solid',
  },
  layout: {
    productCardVariant: 'compact',
    headerVariant: 'centered',
  },
},
```
Note: Newsreader (serif display) is loaded via the theme's own `<link>`/Google Fonts import in the header/hero section, not through `--font-family`. Add it in `pearl/layout/header.tsx` (or `index.html`):
```html
<link href="https://fonts.googleapis.com/css2?family=Jost:wght@400;500;600;700;800&family=Newsreader:wght@200;400&display=swap" rel="stylesheet">
```

### Step 4 — Recolor the sections
- **`hero-banner.tsx`**: dark `#141218` background, `Newsreader` 60px/300 for the H1, off-white `#FAF7F2` text, centered. Replace the existing gradient fallback with a solid dark block + `backgroundImage`. CTA = solid red `#DC2626`, square, uppercase.
- **`category-grid.tsx`**: 4 equal tiles, image + uppercase label, link to category.
- **`product-grid.tsx`**: portrait cards (~aspect 0.87), name 12px uppercase, price 16px, red `#CD1515` sharp discount badge top-left, circular add-to-cart icon button, optional black color swatch.
- **`footer.tsx`**: black `#000` bg, gold `#C9A863` text, columns Shop/Pages/contact/social/payment.
- **Newsletter + Shop-the-look + runway + transformation slider**: these are NOT in the core `capabilities` list, so add them as custom homepage sections (the resolver supports arbitrary section types via `getSectionResolutionCandidates`). Model them on the existing `src/components/sections/` defaults.

### Step 5 — Verify
```bash
cd PointOfSale.Storefront
npm run dev
# set VITE_ENABLE_STOREFRONT_THEME_DEV_OVERRIDE=true and localStorage 'storefront_theme'='pearl'
```
Confirm: white page, dark hero with serif title, red CTA, red LIMITED EDITION marquee, navy section headings, red discount badges, black/gold footer.

---

## 9. Quick Reference — "Pearl" Design Tokens (copy/paste)

```
BG_PAGE        #FFFFFF
DARK_BLOCK     #141218   (hero, runway)
DARK_BLOCK_2   #1E1C24   (shop the look)
NEWSLETTER     #1D1C1C
FOOTER         #000000
RED_ACCENT     #DC2626   (marquee, CTA)
RED_BADGE      #CD1515   (discount %)
GOLD           #C9A863   (footer text)
NAVY_HEADING   #313E61   (section h2)
HERO_TEXT      #FAF7F2
PRODUCT_NAME   #212A2F
PRICE_TEXT     #131316

FONTS          Jost (UI/body) + Newsreader (serif display)
HERO_H1        60px / 300 / Newsreader / #FAF7F2
SECTION_H2     48px / 200 / #313E61 / centered
PRODUCT_NAME   12px / uppercase / Jost / #212A2F
PRICE          16px / Jost / #131316
NAV            12px / uppercase / #FFFFFF
BADGE          11px / Jost / #FFF on #CD1515 / radius 0
RADIUS         sharp (0) corners, editorial
CURRENCY       EGP, thousands comma-separated
```

---

## 10. Gaps / Things to Decide
- The source is a **demo SaaS template** (EasyOrders). Some blocks (before/after slider, shop-the-look editorial, runway hero, newsletter) are not in your core `THEME_CAPABILITIES` and will need custom homepage sections.
- No mobile breakpoint spec was extracted here — recommend a 4-col → 2-col → 1-col responsive grid for product rows, matching the existing `fashion` theme's responsive classes.
- Arabic (RTL) support: keep `familyAr` so the gold/navy palette carries into RTL mode.

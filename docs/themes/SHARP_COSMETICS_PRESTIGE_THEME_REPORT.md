# Sharp Cosmetics / Vanilla Cosmetics Theme Design Report

Source storefront: https://sharp-cosmatics.myeasyorders.com/
Source custom script: https://files.easy-orders.net/1783329486450537222script.js
Observed custom stylesheet: https://files.easy-orders.net/1783329487867215404style.css
Inspection date: 2026-07-31

## 1. Executive Summary

The referenced storefront is a full-bleed cosmetics theme branded as "Vanilla Cosmetics". The visual direction is editorial beauty retail: large photographic banners, thin borders, low-radius square geometry, soft blush/pink accents, and sparse typography. It is not a card-heavy or dashboard-like ecommerce theme. The theme depends on strong product photography and section rhythm more than decoration.

The underlying customization appears to be an EasyOrders "Prestige" theme variant. The page uses a transparent/pinned header over the first hero, a muted blush announcement bar, horizontally moving or snapping product/collection sections, quick-add product controls, a before/after compare slider, countdown campaign hero, newsletter hero, and a large footer.

To recreate the theme in this storefront project, implement it as a reusable cosmetics/editorial theme preset with section components rather than a one-off homepage. The essential experience is:

- Minimal, transparent header over hero on the homepage.
- Full-bleed photographic hero sections, no rounded cards.
- Blush accent typography over a white base.
- Product grids with square media, hairline borders, compact prices, and circular quick-add controls.
- Mobile-first horizontal scrollers for collections/products.
- Desktop full-width editorial stacking with 4-column product rhythm.
- Interactive compare slider, cart drawer, menu drawer, search entry, countdown timer, and newsletter signup surface.

## 2. Brand And Visual Identity

### Brand Feel

- Category: cosmetics / skincare / beauty accessories.
- Tone: polished, clean, feminine, editorial, product-led.
- Layout personality: wide, image-led, restrained UI chrome.
- Visual hierarchy: photography first, then short uppercase campaign copy, then compact product commerce.

### Primary Visual Motifs

- Full-bleed beauty photography.
- Centered white logo over hero.
- Blush/pink text accents.
- Thin white outline CTA buttons on hero images.
- Hairline black product grid dividers.
- Square product imagery with no card radius.
- Mobile horizontal scroll sections with hidden scrollbars.

## 3. Design Tokens

### Color Palette

Use these tokens as the closest observed equivalents:

| Token | Value | Usage |
| --- | --- | --- |
| Page background | `#ffffff` | Main page and product cards |
| Primary text | `#131316` or `#101010` | Body and UI text |
| Accent pink | `#D05D61` / `#D15F61` | Product names, headings, footer text, header text |
| Announcement/footer blush | `#E7DFDB` | Announcement bar and footer background |
| Sale pink | `#F2627B` | Sale-related accents |
| Hairline border | `#000000` at `0.5px` | Product card dividers |
| Muted price text | `rgba(3, 3, 2, 0.76)` | Current price |
| Strike price | `rgba(3, 3, 2, 0.5)` | Old price |
| White overlay text | `#ffffff` | Hero title, collection tile text, CTA borders |

### Typography

Observed active type:

- Primary font: `Instrument Sans`.
- UI support font: `Manrope`.
- Display font variable exists: `Playfair Display`, but the rendered pages mostly use Instrument Sans.
- Arabic fallback: `Almarai`.

Recommended scale:

| Element | Desktop | Mobile | Weight | Notes |
| --- | --- | --- | --- | --- |
| Announcement | 12px | 12px | 500 | Uppercase, letter spacing around 1.44px |
| Nav links | 14-16px | 16px drawer links | 400 | Clean, minimal |
| Hero title | 44-64px desktop | 30-34px mobile | 700-800 | Uppercase |
| Hero subtitle | 12-16px | 12px | 600-700 | Uppercase |
| Hero CTA | 14px desktop | 12px mobile | 700 | Uppercase, wide letter spacing |
| Section heading | 24px desktop | 14px mobile | 400 | Accent pink |
| Collection tile title | 20-28px | 20px | 500 | Uppercase white |
| Product name | 14px | 14px | 400 | Accent pink |
| Product price | 10px | 10px | 400 | Very compact |

### Shape And Spacing

- Border radius: mostly `0px`; product/card UI is square.
- Hero button radius: `0px`.
- Quick-add radius: `50%`.
- Product grid divider: `0.5px solid #000`.
- Homepage desktop header height: about `58px`.
- Mobile header height: about `46px`.
- Announcement height: about `48px`.
- Desktop footer padding: about `72px 90px 20px`.
- Mobile footer padding: about `48px 24px 16px`.

## 4. Global Layout

### Page Frame

- Full-width sections with no max-width container on major homepage blocks.
- Avoid outer cards or boxed sections.
- Keep content flush to the viewport for hero, category grid, compare slider, and product rows.
- On desktop, homepage scroll height was around 7528px at 1440x1000 viewport.
- On mobile-width viewport, homepage scroll height was around 7769px.

### Header Stack

1. Announcement bar.
2. Pinned header.
3. First homepage hero starts underneath the header and visually sits behind it.

On homepage top state, the header is transparent/near-transparent over the hero. On non-hero pages such as collection and product pages, the header becomes white.

## 5. Header And Navigation

### Announcement Bar

Design:

- Background: `#E7DFDB`.
- Text: `WELCOME TO OUR STORE`.
- Text color: `#D15F61`.
- Uppercase, centered, 12px, medium weight, generous letter spacing.
- Left/right chevron controls.
- Height around 48px.

Behavior:

- Supports multiple announcement slides.
- Chevrons allow previous/next announcement.
- Theme CSS also includes marquee keyframes, so announcement marquee is supported even if not active in this store.

### Desktop Header

Layout at 1440px:

- Left: desktop nav links.
- Center: logo image.
- Right: language, search icon, cart icon.
- Header height: 58px.
- Header background on homepage hero: transparent/5% white mix.
- Header background on collection/product pages: white.
- Navigation overflow collapses extra links into a "More" dropdown.

Observed nav:

- Creame & Oils
- Eye Shadow
- Lipstick
- Accessories
- Shipping Policy
- Refund Policy, moved into More when space is limited

### Mobile Header

Layout:

- Left hamburger icon.
- Search icon near left.
- Center logo.
- Right cart icon.
- Header overlays hero.
- Mobile drawer contains logo, close button, collection/page links, search row, and language row.

Implementation notes:

- Header should listen to scroll and add a `scrolled` state.
- If the first content section is a hero/banner/video, mark it as "under nav" so the header starts transparent.
- Pin header only after reserving announcement/header height to prevent content jump.

## 6. Homepage Section Architecture

The full homepage sequence observed on desktop:

1. Full-screen hero: "NEW ARRIVALS", "CARE FOR YOUR BEAUTY", "SHOP NOW".
2. Shop By Collection image grid.
3. Eye Shadow product list.
4. Before/after compare slider: "LE DALIA", "SEE THE DIFFERENCE".
5. Featured Accessories editorial/product block.
6. Second full-bleed hero: "BEST SELLER", "MADE FOR SENSITIVE SKIN", "SHOP NOW".
7. Creame & Oils product list.
8. Promo countdown hero: "LIMITED TIME ONLY", "SUPER SALE UP TO 40% OFF".
9. Lipstick product grid.
10. Newsletter immersive block.
11. Footer.

## 7. Hero / Bottom Banner Component

### Desktop

- Full viewport-ish height: first hero rendered at 1000px high in a 1000px tall viewport.
- Full-bleed image with `object-fit: cover`.
- Header overlays the image.
- Logo remains visible over the image.
- Text alignment for first hero: bottom area, right side on desktop.
- CTA sits beside/below text depending on layout.

### Mobile

- Height around 635px.
- Text sits at the bottom-left.
- CTA becomes nearly full width, about 479px inside a 519px viewport.
- Button is transparent with white border.

### Content Style

- Hero title: uppercase, bold, white.
- Subtitle: uppercase, white.
- CTA: uppercase, white, transparent background, 1px white border, no radius.
- Avoid gradient hero backgrounds; use real product/beauty imagery.

## 8. Collection Tiles

Section heading:

- "Shop By Collection".
- Accent pink, centered.
- Desktop heading about 24px.
- Mobile heading about 14px.

Desktop layout:

- Large editorial image grid.
- First collection tile observed around 765x722 at 1440px.
- Grid fills full width and can be visually mosaic-like.

Mobile layout:

- Horizontal scroll row.
- Tiles are about `min(82vw, 360px)` wide.
- Hidden scrollbar.
- Scroll-snap enabled.
- Tile image zooms slightly on hover.

Tile style:

- Square/rectangular image tile.
- Dark overlay around 20%.
- Centered uppercase white title.
- No radius.

Collections:

- CREAME & OILS
- EYE SHADOW
- LIPSTICK
- ACCESSORIES

## 9. Product Cards

The product card is intentionally compact and grid-like.

### Structure

1. Square product image.
2. Sale badge: `SAVE -20%` or `SAVE 20%`.
3. Product name.
4. Current price.
5. Old price with strikethrough.
6. Quick-add circular control near lower center of image/card.

### Visual Style

- Card background: white.
- Border radius: 0.
- Product media background: light gray around `#E6E6E6`.
- Card divider: 0.5px black line.
- Product image: square, `object-fit: cover/contain` depending on source.
- Product name: accent pink, 14px, normal weight.
- Price: 10px, compact line-height around 7px.
- Old price: muted and struck.

### Homepage Product List

- Desktop product card width around 356px at 1440px.
- Mobile product card width around 400px in a horizontal carousel.
- Product row uses scroll snap and hidden scrollbar.
- Header row has title left and "View All" right on many sections.

### Collection Product Grid

On the Eye Shadow collection page:

- Header contains `EYE SHADOW` and a sort button.
- Sort default observed: "Highest Rate".
- Grid uses 4 columns at 1440px for this viewport state.
- Product image cards around 355x355.
- Products load through a skeleton state first, then real product cards.

## 10. Quick Add And Cart

Quick-add design:

- Circular button, about 34px on mobile/home product list.
- Contains a small plus icon.
- Label text exists but is visually compressed/hidden at rest.
- Hover state exposes/adds "Add to cart" label behavior in the CSS.

Behavior expected:

- Add product to cart without leaving the current page.
- Update cart badge/count.
- Support cart drawer/open state from header cart button.
- Use platform cart API rather than page reload.

## 11. Product Detail Page

Observed product page: `Eyeshadown palette`.

Desktop layout:

- White header, not transparent.
- Product content starts just under header.
- Left: large image gallery area, about 905px wide.
- Right: sticky/compact details column, about 444px wide.
- Details column begins with breadcrumb/category link.
- Product title uses accent pink, 20px, normal weight.
- Price row uses Manrope/Instrument Sans, 16px.
- Primary actions: `BUY NOW` and `Add To Cart`.
- Description accordion/toggle below actions.
- Gallery has zoom buttons, dot pagination, and lightbox next/previous controls.

Product content:

- Category/breadcrumb: EYE SHADOW.
- Title: Eyeshadown palette.
- Price: 1199 EGP, old price 1499 EGP.
- Description text is plain and restrained, 14px.

Implementation notes:

- Gallery should support 4 images, dots, zoom/lightbox, and keyboard-safe navigation.
- Keep detail column visually quiet: no heavy card, no rounded panel.
- Use buttons with strong full-width commerce affordance in the details column.

## 12. Compare Slider

Component:

- Full-width before/after image compare.
- Section title: "LE DALIA".
- Subtitle: "SEE THE DIFFERENCE".
- Desktop height around 900px.
- Mobile height around 377px.
- The split value is controlled by a CSS variable like `--psc-split`.
- Handle can be dragged horizontally.
- Keyboard support uses ArrowLeft/ArrowRight to move the split.

Implementation notes:

- Use pointer events on the scene.
- Do not block vertical page scrolling unless horizontal drag threshold is exceeded.
- Clip the top/right image with CSS based on split percent.
- Add accessible focus target for keyboard interaction.

## 13. Promo Countdown Hero

Observed content:

- "LIMITED TIME ONLY"
- "SUPER SALE UP TO 40% OFF"
- Body copy about a super sale.
- CTA: "SHOP NOW".
- Timer units: day, hours, minutes, seconds.

Behavior:

- Timer updates every second.
- Script stores rolling countdown remaining time in `localStorage`.
- Section is a full-bleed campaign hero, not a small promo card.

Implementation notes:

- Keep campaign text over image or solid editorial layout.
- Use large uppercase campaign title.
- Provide configurable duration/end date.
- Respect reduced-motion preferences.

## 14. Newsletter Section

Observed content:

- "KEEP ME UPDATED"
- "NEWSLETTER"
- Supporting subscription copy.
- CTA: "SUBSCRIBE".
- Consent/disclaimer text.

Design:

- Immersive section about 560px high on desktop.
- Full-width, image-led or strong editorial layout.
- No nested card treatment.

## 15. Footer

Design:

- Background: `#E7DFDB`.
- Text: `#D15F61`.
- Desktop padding: about `72px 90px 20px`.
- Mobile padding: about `48px 24px 16px`.
- Large, full-width footer.

Content groups:

- SHOP: collection links.
- PAGES: shipping, terms, refund, privacy.
- CONTACT US: phone and email.
- Newsletter/contact entry with envelope icon.

## 16. Motion And Interaction System

The custom script initializes these behavior groups:

- Font variable setup.
- Google font loading.
- Header scroll/pinned/transparent state.
- Desktop nav overflow measurement and "More" dropdown.
- Hero/slider initialization.
- Scroll reveal using `IntersectionObserver`.
- Before/after compare slider.
- Promo countdown with `localStorage`.
- CTA link hydration by fetching EasyOrders API slugs.
- Product/product-list rendering helpers.
- Search/cart/menu/language UI hooks.

Motion feel:

- Slow, smooth transitions.
- Hover image scale on collection tiles.
- Scroll reveal uses blur-to-clear fade rather than aggressive movement.
- Respect `prefers-reduced-motion`.

## 17. Responsive Behavior

### Desktop

- Header: nav left, logo center, utility icons right.
- First hero: full-height, text can sit to lower right.
- Collection section: full-width editorial grid.
- Product list: 4-up row/carousel feel with hairline dividers.
- Compare: tall 900px editorial section.
- Footer: multi-column.

### Mobile

- Header: hamburger/search, centered logo, cart.
- Hero: 635px image with bottom-left text and full-width outline CTA.
- Collections: horizontal snap scroller.
- Product lists: horizontal snap scroller.
- Product card: large square image, compact text.
- Footer: stacked sections.

Breakpoints to implement:

- Mobile: `< 768px`.
- Tablet: `768px - 1023px`.
- Desktop nav: `>= 1025px`.
- Wide product grid: `>= 1280px`.

## 18. Asset Requirements

To recreate the design convincingly, prioritize photography:

- Large hero beauty/cosmetic campaign image.
- Secondary hero skincare/product image.
- Four collection tile images.
- Square product images, preferably 900x900 or larger.
- Before/after compare pair.
- Logo image, preferably transparent white and dark variants.
- Simple line icons: menu, search, cart, chevrons, plus, envelope.

Observed asset examples from the source page:

- Logo: `https://files.easy-orders.net/1782320847543432690.webp`
- First hero mobile/desktop image: `https://files.easy-orders.net/1782320150803906544.jpg`
- Collection tile images: `1779435409177024049.jpg`, `1779435684544659768.jpg`, `1779435611753485406.jpg`, `1779436008937230344.jpg`
- Product images: `import-1777782153990457419.webp`, `import-1777782153073127449.webp`, `import-1777782152191310150.webp`
- Compare images: `1779715273691261862.png`, `1779715188610183989.png`

Use these only as references unless you have the right to reuse them.

## 19. Implementation Blueprint

Create the theme as a section-based preset:

1. `AnnouncementBar`
   - Slide/marquee support, blush background, chevrons.

2. `PrestigeHeader`
   - Transparent-over-hero mode.
   - White scrolled/non-hero mode.
   - Desktop overflow More menu.
   - Mobile drawer.
   - Search, language, cart controls.

3. `EditorialHero`
   - Full-bleed background image.
   - Configurable title/subtitle/CTA/alignment.
   - Under-nav mode.

4. `CollectionMosaic`
   - Desktop mosaic/grid.
   - Mobile horizontal snap list.

5. `ProductScroller`
   - Home row with title and View All.
   - Product cards with quick add.

6. `ProductGrid`
   - Collection page grid.
   - Loading skeleton.
   - Sort dropdown.

7. `CompareSlider`
   - Pointer drag, keyboard arrow support, split CSS variable.

8. `FeaturedCollection`
   - Editorial image plus products, with same product card pattern.

9. `CountdownHero`
   - Configurable end/rolling countdown.
   - Full-bleed campaign layout.

10. `NewsletterImmersive`
    - Full-width newsletter section.

11. `PrestigeFooter`
    - Blush footer with SHOP, PAGES, CONTACT, and newsletter/contact controls.

12. `ProductDetail`
    - Gallery + detail split.
    - Breadcrumb/category.
    - Buy Now and Add To Cart.
    - Description accordion.
    - Lightbox.

## 20. Must-Have CSS Contracts

Recommended custom properties:

```css
:root {
  --cos-bg: #ffffff;
  --cos-text: #131316;
  --cos-accent: #d05d61;
  --cos-accent-2: #d15f61;
  --cos-blush: #e7dfdb;
  --cos-sale: #f2627b;
  --cos-border: #000000;
  --cos-muted-price: rgba(3, 3, 2, 0.76);
  --cos-strike-price: rgba(3, 3, 2, 0.5);
  --cos-font-primary: "Instrument Sans", sans-serif;
  --cos-font-ui: "Manrope", "Instrument Sans", sans-serif;
  --cos-header-h-mobile: 46px;
  --cos-header-h-desktop: 58px;
  --cos-announce-h: 48px;
}
```

Core styling rules:

- Keep section backgrounds full-width.
- Use no card radius for editorial commerce surfaces.
- Use square product media.
- Use 0.5px or 1px hairline grid dividers.
- Use pink accent for headings/product names.
- Use transparent outline CTA buttons over hero imagery.
- Hide horizontal scrollbars while keeping scroll-snap.
- Use `prefers-reduced-motion` to disable scroll reveal and image scaling.

## 21. Build Priorities

### Phase 1: Visual Match

- Header/announcement.
- Homepage hero.
- Collection mosaic.
- Product card.
- Product scroller.
- Footer.

### Phase 2: Commerce Pages

- Collection grid with sort and loading skeleton.
- Product detail gallery/detail split.
- Cart drawer and quick add.

### Phase 3: Editorial/Interactive Sections

- Compare slider.
- Countdown hero.
- Newsletter immersive.
- Featured collection block.
- Scroll reveal animation.

### Phase 4: Polish

- Mobile drawer.
- Header overflow More menu.
- Product hover image swap.
- Gallery lightbox.
- Accessibility and keyboard controls.

## 22. Risks And Notes

- The exact theme assets and source code should not be copied unless licensed; recreate the design system and behavior with original code/assets.
- The source page has typo-like labels, for example `Creame & Oils` and `Eyeshadown palette`. Decide whether to intentionally preserve these strings for visual parity or correct them for production quality.
- Product/collection pages use platform-loaded data and can show skeletons briefly; implement loading states so the page does not look broken.
- This theme depends heavily on large, high-quality imagery. Without equivalent photography, the design will feel plain.
- Avoid adding rounded cards, gradients, decorative blobs, or heavy shadows; those would move away from the reference.


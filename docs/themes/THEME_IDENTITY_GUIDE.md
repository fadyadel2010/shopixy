# Shopixy Theme Identity Guide

Phase: D1 - Theme Identity Definition  
Status: Canonical design strategy, documentation only  
Scope: Storefront theme identity for Modern, Minimal, Fashion, Pearl, and Electronics  

## Purpose

Shopixy themes are not color presets. A theme is a complete storefront personality expressed through typography, spacing, shape, hierarchy, imagery, motion, product presentation, and tone. A customer should recognize the active theme before seeing its name.

This document is the canonical design reference for future theme implementation. It does not authorize React, CSS, token, builder, runtime, or component changes. The frozen storefront architecture remains the execution boundary.

## Platform Guardrails

- Theme implementation identity remains owned by the compiled storefront theme registry and backend-admitted store configuration.
- Themes may select and style presentation, but may not create business truth, publish CMS drafts, alter catalog authority, alter pricing, bypass checkout rules, or replace tenant configuration.
- Theme-specific design must come from visible interaction patterns and composition, not from single-token recoloring.
- Builder previews must render the real storefront runtime, not synthetic approximations.
- Each theme must retain Shopixy ecommerce usability: readable product information, trustworthy pricing, clear cart actions, accessible controls, and responsive behavior.

## Official Theme Roles

| Theme | Canonical Role |
|---|---|
| Modern | Confident contemporary retail default for broad merchants |
| Minimal | Quiet product-first storefront for curated catalogs and premium restraint |
| Fashion | Editorial, expressive, campaign-led shopping experience |
| Pearl | Luxury boutique identity for jewelry, watches, gifts, and refined lifestyle |
| Electronics | Technical, specification-led commerce for devices and performance products |

Note: repository evidence currently shows runtime folders for Modern, Minimal, Fashion, and Pearl. This Phase D1 brief defines Electronics as an official design identity for documentation purposes only; no registry or runtime implementation is changed here.

## Identity Principles

1. Typography must lead identity.
2. Spacing must be theme-specific, not incidental.
3. Product cards must look different even when product data is identical.
4. CTAs must communicate each theme's commercial posture.
5. Motion must reinforce the brand personality.
6. Imagery rules must be specific enough that merchants can select better assets.
7. Colors must support the theme, not carry it alone.

## Theme Summaries

### Modern

Modern is the primary Shopixy default: polished, energetic, clear, and commercially versatile. It should feel like a capable contemporary ecommerce brand that can handle general retail, home goods, cosmetics, sports, accessories, and mixed catalogs without looking generic.

Identity anchors: strong sans typography, balanced whitespace, medium radius, soft elevation, bold but orderly CTA hierarchy, bright product imagery, and modular sections that feel easy to scan.

### Minimal

Minimal is restraint as a product experience. It removes visual noise so the product, name, price, and one clear action can breathe. It fits curated stores, design goods, books, homeware, skincare, boutique essentials, and merchants with strong product photography.

Identity anchors: quiet typography, flat surfaces, generous negative space, low decoration, subtle borders, calm motion, and restrained CTAs.

### Fashion

Fashion is editorial commerce. It should feel campaign-led, image-forward, seasonal, and expressive. Products live inside a story: collection drops, lookbooks, style edits, runway-like image crops, and confident typographic rhythm.

Identity anchors: high-impact imagery, dramatic scale shifts, mixed typography, sharp or selectively rounded shapes, energetic hover states, strong badges, and editorial section sequencing.

### Pearl

Pearl is luxury boutique. It should feel refined, deliberate, polished, and intimate. It is less loud than Fashion and more ceremonial than Minimal. It works for jewelry, watches, premium gifts, fragrance, occasionwear, and high-touch lifestyle products.

Identity anchors: serif display moments, narrow uppercase labels, luxury spacing, thin dividers, low but premium elevation, portrait imagery, precise CTAs, and elegant restraint.

### Electronics

Electronics is technical confidence. It should feel structured, high-performance, spec-aware, and comparison-friendly. The design should help customers evaluate devices, accessories, variants, compatibility, performance claims, warranty signals, and price/value.

Identity anchors: compact information architecture, tabular rhythm, crisp sans or technical typography, hard or modest radii, controlled contrast, purposeful badges, direct CTAs, and responsive details.

## Comparison Matrix

| Theme | Audience | Density | Luxury | Motion | Typography | Spacing | Product Focus | CTA Style | Image Focus | Emotional Tone |
|---|---|---:|---:|---|---|---|---|---|---|---|
| Modern | Broad retailers and growing DTC stores | Medium | Medium | Smooth and responsive | Contemporary sans, confident headings | Balanced | Product plus promotion | Filled, clear, commercially warm | Bright, clean, lifestyle plus product | Trust, momentum, ease |
| Minimal | Curated shops, design goods, books, skincare | Low | Medium | Nearly invisible | Quiet sans, restrained hierarchy | Airy | Product object and price | Understated, low-volume | Clean, isolated, honest | Calm, focus, clarity |
| Fashion | Apparel, accessories, drops, beauty campaigns | Medium-low to editorial | Medium-high | Expressive and dynamic | Editorial display plus crisp UI | Dramatic | Image and collection story | Bold, campaign-like | Large crops, model/editorial-led | Desire, discovery, energy |
| Pearl | Jewelry, watches, luxury gifts, premium lifestyle | Low | High | Elegant and measured | Serif display plus refined UI sans | Luxury airy | Craft, detail, exclusivity | Precise, refined, ceremonial | Portrait, polished, intimate detail | Desire, trust, refinement |
| Electronics | Devices, gadgets, computer parts, appliances | Medium-high | Low-medium | Fast and functional | Technical sans, numeric emphasis | Compact and gridded | Specs, price, availability, comparison | Direct, high-contrast, action-led | Product renders, details, use-case proof | Confidence, control, performance |

## Differentiation Rules

### Modern vs Minimal

Modern uses visible commercial energy: larger CTAs, soft elevation, warmer section rhythm, and promotional blocks. Minimal reduces almost everything: flatter surfaces, quieter typography, fewer accents, and more disciplined whitespace.

### Fashion vs Pearl

Fashion is expressive and campaign-driven. Pearl is refined and boutique-driven. Fashion may use drama, asymmetry, and visual energy. Pearl should use ceremony, thin detail, and premium restraint.

### Modern vs Electronics

Modern sells through confidence and lifestyle clarity. Electronics sells through precision and decision support. Electronics must expose specs, compatibility, stock, and comparison cues more prominently.

### Minimal vs Pearl

Both are restrained, but Minimal is almost invisible as a frame around the product. Pearl has a distinct luxury voice: serif moments, boutique spacing, polished image framing, and elevated microcopy.

## Future Theme Recommendations

Future themes may naturally emerge from merchant needs, but they should not be designed in this phase.

- Artisan: handmade, craft, earthy, irregular, tactile.
- Grocery: dense, fast, promotion-heavy, basket-focused.
- Kids: playful, colorful, soft, friendly.
- Sports: kinetic, bold, performance-oriented.
- Bookshop: literary, catalog-rich, editorial but quiet.
- Furniture: spatial, architectural, room-scene driven.

Before any future theme is accepted, it must pass the same uniqueness test and receive a full standalone design guide.

## Uniqueness Test

Question: If screenshots from all themes were shown side-by-side with their names removed, would users immediately recognize that these are different themes?

Answer: PASS, if implementation follows these documents.

Why:

- Modern is recognizable through balanced commercial polish, medium density, soft cards, and clear modular hierarchy.
- Minimal is recognizable through quiet surfaces, flatness, low visual noise, and whitespace-led product framing.
- Fashion is recognizable through editorial composition, dramatic imagery, expressive type scale, and campaign-led flow.
- Pearl is recognizable through boutique luxury, serif display moments, precise uppercase micro-labels, polished portrait imagery, and thin elegant details.
- Electronics is recognizable through compact technical hierarchy, specification-first presentation, crisp geometry, direct CTAs, and high-information cards.

Failure conditions:

- If all themes share the same product-card proportions, they will blur together.
- If typography differs only by font family but not scale, weight, and hierarchy, identity will be weak.
- If badges and CTAs use the same shape and placement across all themes, screenshots will read as recolored variants.
- If all themes use identical hero layouts, the ecosystem will lack instant recognition.
- If Electronics does not surface specs and comparison structure, it will become Modern with sharper colors.
- If Pearl loses its serif/luxury rhythm, it will collapse into Minimal or Fashion.

## Final Certification

Design Identity Completeness: PASS. The theme system now has documented identity primitives covering personality, audience, typography, color philosophy, shapes, elevation, spacing, product presentation, CTAs, badges, motion, imagery, sections, journey, and constraints.

Consistency: PASS. All themes share ecommerce usability and platform guardrails while preserving distinct visual systems.

Differentiation: PASS. Each official theme has a different reason to exist and a different screenshot-level signature.

Future Expandability: PASS. Future themes can be evaluated against the same identity framework without reopening architecture.

Risks:

- Future implementation may over-rely on color tokens and flatten the intended identity differences.
- Merchant overrides may dilute theme identity if typography, radius, spacing, and product-card rules are too freely overridden.
- Electronics is documented as official by this phase, but current runtime evidence should be reconciled in a later implementation phase.

Recommendations:

- Treat each individual design guide as the source of truth for that theme.
- Add implementation acceptance criteria in the next phase that compare screenshots across all themes.
- Preserve theme-specific product-card geometry, CTA treatment, badge placement, and section rhythm as non-negotiable identity traits.

Final Certification: PASS


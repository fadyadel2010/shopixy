# Shopixy Canonical Theme Coverage Matrix

Canonical authority: `theme-authority/shopixy-theme-authority.json`

| Theme | Backend | Marketplace | Runtime | Resolver | Builder | Homepage | Sections | Components | Product Cards | Browsing | Purchase | Templates | Capabilities | Activation | Validation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| modern | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| minimal | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| fashion | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| pearl | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| phoenix | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| hype | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |

## Classification Decisions

Electronics: experimental. It remains declared in the canonical authority for experimental component/product-template coverage, but is not backend-available, marketplace-visible, runtime-available, or activation-enabled.

Phoenix: official. Runtime validation requires Phoenix to resolve as `phoenix`; fallback to Modern is not accepted for official coverage.

Hype: independent official theme. It owns independent component, product-card, browsing, purchase-profile, product-template, marketplace, runtime, and backend registrations. It is not an Electronics alias.

## Enforcement

CI parity enforcement is implemented in:

- `PointOfSale.Storefront/src/storefront/themes/ThemeRegistry.test.ts`
- `PointOfSale.Web/src/modules/storefront/themes/themeMarketplaceRegistry.test.ts`
- `PointOfSale.Tests/Storefront/StorefrontThemeRuntimeTokenTests.cs`

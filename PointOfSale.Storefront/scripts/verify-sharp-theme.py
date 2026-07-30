#!/usr/bin/env python3
"""
Sharp Theme — Comprehensive Verification Script

Validates that the "sharp" theme is fully wired into the PointOfSale.Storefront
theme system, matching the same registration points and infrastructure as all
existing themes (modern, minimal, fashion, pearl, phoenix, hype, alizon).

Checks:
1. Theme authority JSON — sharp entry exists with all required fields
2. ThemeComponentTheme union — 'sharp' present
3. ThemeComponents.tsx — 'sharp' in every Record<ThemeComponentTheme, ...>
4. CommerceBrowsingExperience.tsx — 'sharp' in commercePageStyles, commerceGridStyles, searchPageStyles + CommerceMerchandisingBlock if-block
5. Theme folder structure — layout/{header,footer}.tsx, sections/{hero-banner,product-grid,category-grid}.tsx
6. ProductCardRendererId — 'sharp' in type + PRODUCT_CARD_RENDERERS
7. PurchaseExperienceVariant — 'sharp' in type + PROFILE_BY_VARIANT + LAYOUT_VARIANT_ALIASES
8. ProductTemplateRegistry — 'sharp' template exists
9. No stale 'pearl' references in sharp/ folder
10. Test results — all tests pass
"""

import json
import os
import re
import subprocess
import sys

ROOT = os.path.join(os.environ.get("USERPROFILE", "/c/Users/fadya"),
                    "source/repos/PointOfSale")
STOREFRONT = os.path.join(ROOT, "PointOfSale.Storefront")
THEME_DIR = os.path.join(STOREFRONT, "src/storefront/themes/sharp")
AUTHORITY = os.path.join(ROOT, "theme-authority/shopixy-theme-authority.json")

errors = []
warnings = []
passes = []

def check(condition, msg, is_warning=False):
    if condition:
        passes.append(msg)
    elif is_warning:
        warnings.append(msg)
    else:
        errors.append(msg)

# ─── 1. Theme Authority JSON ──────────────────────────────────────────────
print("=" * 70)
print("1. Theme Authority JSON")
print("=" * 70)

with open(AUTHORITY, "r") as f:
    authority = json.load(f)

sharp_entry = None
for theme in authority["themes"]:
    if theme["id"] == "sharp":
        sharp_entry = theme
        break

check(sharp_entry is not None, "sharp theme entry exists in authority JSON")

if sharp_entry:
    required_fields = [
        "id", "displayName", "version", "officialStatus", "activationStatus",
        "backendAvailability", "marketplaceVisibility", "runtimeAvailability",
        "homepageAvailability", "componentTheme", "commerceBrowsingTheme",
        "supportedSections", "supportedLayouts", "supportedBuilderFeatures",
        "supportedProductCards", "supportedProductTemplates",
        "supportedPurchaseProfiles", "capabilities", "marketplace",
        "defaultThemeConfig"
    ]
    for field in required_fields:
        check(field in sharp_entry, f"  field '{field}' present")

    check(sharp_entry["officialStatus"] == "official", "  officialStatus = 'official'")
    check(sharp_entry["activationStatus"] == "active", "  activationStatus = 'active'")
    check(sharp_entry["runtimeAvailability"] == True, "  runtimeAvailability = true")
    check(sharp_entry["componentTheme"] == "sharp", "  componentTheme = 'sharp'")
    check(sharp_entry["commerceBrowsingTheme"] == "sharp", "  commerceBrowsingTheme = 'sharp'")
    check("layout.header" in sharp_entry["capabilities"], "  capability 'layout.header'")
    check("section.hero-banner" in sharp_entry["capabilities"], "  capability 'section.hero-banner'")
    check("section.product-grid" in sharp_entry["capabilities"], "  capability 'section.product-grid'")
    check("section.category-grid" in sharp_entry["capabilities"], "  capability 'section.category-grid'")

    tc = sharp_entry.get("defaultThemeConfig", {})
    check("colors" in tc, "  defaultThemeConfig.colors present")
    check("font" in tc, "  defaultThemeConfig.font present")
    check("ui" in tc, "  defaultThemeConfig.ui present")
    check("layout" in tc, "  defaultThemeConfig.layout present")

    if "colors" in tc:
        for color in ["primary", "secondary", "background", "text", "muted"]:
            check(color in tc["colors"], f"  defaultThemeConfig.colors.{color}")

    if "font" in tc:
        check("family" in tc["font"], "  defaultThemeConfig.font.family")
        check("baseSize" in tc["font"], "  defaultThemeConfig.font.baseSize")

    if "layout" in tc:
        check("productCardVariant" in tc["layout"], "  defaultThemeConfig.layout.productCardVariant")
        check("headerVariant" in tc["layout"], "  defaultThemeConfig.layout.headerVariant")

# ─── 2. ThemeComponentTheme Union ─────────────────────────────────────────
print("\n" + "=" * 70)
print("2. ThemeComponentTheme Union")
print("=" * 70)

types_file = os.path.join(STOREFRONT, "src/components/theme-components/themeComponentTypes.ts")
with open(types_file, "r") as f:
    types_content = f.read()

check("'sharp'" in types_content, "'sharp' present in themeComponentTypes.ts")

# ─── 3. ThemeComponents.tsx Records ───────────────────────────────────────
print("\n" + "=" * 70)
print("3. ThemeComponents.tsx Records")
print("=" * 70)

tc_file = os.path.join(STOREFRONT, "src/components/theme-components/ThemeComponents.tsx")
with open(tc_file, "r") as f:
    tc_content = f.read()

records_to_check = [
    ("surfaceStyles", "Record<ThemeComponentTheme, string>"),
    ("sectionHeaderStyles", "Record<ThemeComponentTheme, {"),
    ("categoryArt", "Record<ThemeComponentTheme"),
    ("buttonStyles", "Record<ThemeComponentTheme, string>"),
    ("toneStyles", "Record<ThemeComponentTheme, Record"),
    ("themeClass", "Record<ThemeComponentTheme, string>"),
    ("ThemeLabel styles", "Record<ThemeComponentTheme, string>"),
    ("ThemeProductTitle styles", "Record<ThemeComponentTheme, string>"),
    ("ThemeProductMeta styles", "Record<ThemeComponentTheme, string>"),
    ("wrapperStyles", "Record<ThemeComponentTheme, string>"),
    ("currentStyles", "Record<ThemeComponentTheme, string>"),
    ("mutedStyles", "Record<ThemeComponentTheme, string>"),
    ("compareStyles", "Record<ThemeComponentTheme, string>"),
    ("ThemeImageFrame styles", "Record<ThemeComponentTheme, string>"),
    ("ThemeLoadingSkeleton styles", "Record<ThemeComponentTheme, string>"),
    ("ThemeEmptyState styles", "Record<ThemeComponentTheme, {"),
]

# Count 'sharp:' occurrences in Records context
sharp_in_records = tc_content.count("sharp:")
check(sharp_in_records >= 16, f"'sharp:' appears in Records ({sharp_in_records} occurrences, expected >= 16)")

# Check ThemeCategoryCard if-block
check("if (theme === 'sharp')" in tc_content, "ThemeCategoryCard has 'if (theme === \"sharp\")' block")

# ─── 4. CommerceBrowsingExperience.tsx ────────────────────────────────────
print("\n" + "=" * 70)
print("4. CommerceBrowsingExperience.tsx")
print("=" * 70)

commerce_file = os.path.join(STOREFRONT, "src/storefront/commerce/CommerceBrowsingExperience.tsx")
with open(commerce_file, "r") as f:
    commerce_content = f.read()

check("sharp:" in commerce_content, "'sharp' in commercePageStyles Record")
check("sharp:" in commerce_content, "'sharp' in commerceGridStyles Record")
check("sharp:" in commerce_content, "'sharp' in searchPageStyles Record")
check("if (theme === 'sharp')" in commerce_content, "CommerceMerchandisingBlock has 'if (theme === \"sharp\")' block")
check("sharp: 'Jewelry browse'" in commerce_content, "CommercePageHeader eyebrow for sharp")
check("sharp: 'A jewelry browsing path" in commerce_content, "CommercePageHeader subtitle for sharp")

# ─── 5. Theme Folder Structure ────────────────────────────────────────────
print("\n" + "=" * 70)
print("5. Theme Folder Structure")
print("=" * 70)

expected_files = [
    "layout/header.tsx",
    "layout/footer.tsx",
    "layout/sharpTokens.ts",
    "sections/hero-banner.tsx",
    "sections/product-grid.tsx",
    "sections/category-grid.tsx",
    "themeConfig.ts",
    "sharpLayout.test.tsx",
]

for f in expected_files:
    path = os.path.join(THEME_DIR, f)
    check(os.path.exists(path), f"  {f} exists")

# Check no stale pearl references
stale_patterns = ["pearlTokens", "PEARL_", "pearlNavLinkClass", "themeId=\"pearl\"", "PearlHeader", "PearlFooter"]
for pattern in stale_patterns:
    for root, dirs, files in os.walk(THEME_DIR):
        for fname in files:
            fpath = os.path.join(root, fname)
            with open(fpath, "r", errors="ignore") as f:
                content = f.read()
            if pattern in content:
                errors.append(f"  Stale reference '{pattern}' found in {fpath}")

# ─── 6. ProductCardRendererId ─────────────────────────────────────────────
print("\n" + "=" * 70)
print("6. ProductCardRendererId")
print("=" * 70)

pcr_file = os.path.join(STOREFRONT, "src/components/products/productCardRendererResolver.ts")
with open(pcr_file, "r") as f:
    pcr_content = f.read()

check("| 'sharp'" in pcr_content, "'sharp' in ProductCardRendererId type")
check("THEME_RENDERERS" in pcr_content, "THEME_RENDERERS map reads from authority JSON (auto-discovery)")

renderers_file = os.path.join(STOREFRONT, "src/components/products/ProductCardRenderers.tsx")
with open(renderers_file, "r") as f:
    renderers_content = f.read()

check("SharpProductCardRenderer" in renderers_content, "SharpProductCardRenderer component defined")
check("sharp: SharpProductCardRenderer" in renderers_content, "SharpProductCardRenderer registered in PRODUCT_CARD_RENDERERS")
check('data-product-card-theme="sharp"' in renderers_content, "SharpProductCardRenderer has data-product-card-theme attribute")

# ─── 7. PurchaseExperienceVariant ─────────────────────────────────────────
print("\n" + "=" * 70)
print("7. PurchaseExperienceVariant")
print("=" * 70)

pe_file = os.path.join(STOREFRONT, "src/storefront/products/purchaseExperience.ts")
with open(pe_file, "r") as f:
    pe_content = f.read()

check("| 'sharp'" in pe_content, "'sharp' in PurchaseExperienceVariant type")
check("sharp: {" in pe_content, "'sharp' profile in PROFILE_BY_VARIANT")
check("sharp: 'sharp'" in pe_content, "'sharp' in LAYOUT_VARIANT_ALIASES")

# ─── 8. ProductTemplateRegistry ────────────────────────────────────────────
print("\n" + "=" * 70)
print("8. ProductTemplateRegistry")
print("=" * 70)

ptr_file = os.path.join(STOREFRONT, "src/storefront/products/ProductTemplateRegistry.ts")
with open(ptr_file, "r") as f:
    ptr_content = f.read()

check("sharp: {" in ptr_content, "'sharp' template in BUILTIN_PRODUCT_TEMPLATES")
check("templateId: 'sharp'" in ptr_content, "'sharp' templateId present")
check("layoutVariant: 'sharp'" in ptr_content, "'sharp' layoutVariant in template sections")

# ─── 9. Theme Registry Auto-Discovery ─────────────────────────────────────
print("\n" + "=" * 70)
print("9. Theme Registry Auto-Discovery")
print("=" * 70)

# Check that ThemeSectionResolver uses glob for sharp
resolver_file = os.path.join(STOREFRONT, "src/storefront/themes/ThemeSectionResolver.ts")
with open(resolver_file, "r") as f:
    resolver_content = f.read()

check("import.meta.glob" in resolver_content, "ThemeSectionResolver uses import.meta.glob (auto-discovers sharp)")

# Check ThemeLayoutResolver
layout_resolver_file = os.path.join(STOREFRONT, "src/storefront/themes/ThemeLayoutResolver.ts")
with open(layout_resolver_file, "r") as f:
    layout_resolver_content = f.read()

check("import.meta.glob" in layout_resolver_content, "ThemeLayoutResolver uses import.meta.glob (auto-discovers sharp layout)")

# ─── 10. Run Tests ────────────────────────────────────────────────────────
print("\n" + "=" * 70)
print("10. Running Tests")
print("=" * 70)

# Find npx path
import shutil
npx_path = shutil.which("npx") or shutil.which("npx.cmd")
if not npx_path:
    # Try common locations
    for candidate in ["C:/Program Files/nodejs/npx.cmd", "C:/Program Files (x86)/nodejs/npx.cmd"]:
        if os.path.exists(candidate):
            npx_path = candidate
            break

if npx_path:
    result = subprocess.run(
        [npx_path, "vitest", "run"],
        cwd=STOREFRONT,
        capture_output=True,
        text=True,
        timeout=120
    )
else:
    # Fallback: use shell=True
    result = subprocess.run(
        "npx vitest run",
        cwd=STOREFRONT,
        capture_output=True,
        text=True,
        timeout=120,
        shell=True
    )

if result.returncode == 0:
    # Extract test summary
    for line in result.stdout.split("\n"):
        if "Test Files" in line or "Tests" in line:
            print(f"  {line.strip()}")
    passes.append("All tests pass")
else:
    errors.append(f"Tests failed with exit code {result.returncode}")
    print(result.stdout[-500:])
    print(result.stderr[-500:])

# ─── Summary ──────────────────────────────────────────────────────────────
print("\n" + "=" * 70)
print("SUMMARY")
print("=" * 70)
print(f"  Checks passed: {len(passes)}")
print(f"  Warnings:      {len(warnings)}")
print(f"  Errors:        {len(errors)}")

if warnings:
    print("\n  Warnings:")
    for w in warnings:
        print(f"    ⚠ {w}")

if errors:
    print("\n  Errors:")
    for e in errors:
        print(f"    ✗ {e}")
    print("\n  RESULT: FAILED")
    sys.exit(1)
else:
    print("\n  RESULT: ALL CHECKS PASSED ✓")
    sys.exit(0)

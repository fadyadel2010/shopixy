/**
 * Pearl Theme — Sections tests (Phase T1.2A: Hero only)
 *
 * Follows the repo's node-env test convention (no jsdom): validates the hero
 * section's rendered contract via source-contract assertions and confirms the
 * section is discoverable by the theme section resolver glob. No DOM rendering
 * (the analytics singleton touches `document` at import time under node).
 */
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { join } from 'path';

const sectionsDir = join(__dirname, 'sections');
const heroSrc = readFileSync(join(sectionsDir, 'hero-banner.tsx'), 'utf8');
const categorySrc = readFileSync(join(sectionsDir, 'category-grid.tsx'), 'utf8');
const productGridSrc = readFileSync(join(sectionsDir, 'product-grid.tsx'), 'utf8');
const productCardSrc = readFileSync(join(__dirname, 'components', 'PearlProductCard.tsx'), 'utf8');
const bannerSrc = readFileSync(join(sectionsDir, 'banner.tsx'), 'utf8');
const newsletterSrc = readFileSync(join(sectionsDir, 'newsletter-signup.tsx'), 'utf8');
const textBlockSrc = readFileSync(join(sectionsDir, 'text-block.tsx'), 'utf8');

describe('Pearl sections — Hero Banner (Phase T1.2A)', () => {
    it('default dark block background matches the design report (#141218)', () => {
        expect(heroSrc).toContain("backgroundColor: backgroundColor || '#141218'");
    });

    it('uses Newsreader serif display for the H1 (typography integration)', () => {
        // Display font token resolves to Newsreader; applied to the H1.
        expect(heroSrc).toContain('PEARL_FONT_DISPLAY');
        // The token itself is defined as the Newsreader face (read as source to avoid runtime import).
        const tokensSrc = readFileSync(join(__dirname, 'layout', 'pearlTokens.ts'), 'utf8');
        expect(tokensSrc).toContain("font-['Newsreader',serif]");
        // Off-white hero text #FAF7F2
        expect(heroSrc).toContain('text-[#FAF7F2]');
    });

    it('renders a thin gold micro-label eyebrow (#C9A863)', () => {
        expect(heroSrc).toContain("t('home.newSeason'");
        expect(heroSrc).toContain('text-[#C9A863]');
        expect(heroSrc).toContain('uppercase');
    });

    it('CTA is solid red #DC2626, square (radius 0), uppercase — the Pearl accent', () => {
        expect(heroSrc).toContain("!bg-[#DC2626]");
        expect(heroSrc).toContain('!rounded-none');
        expect(heroSrc).toContain('uppercase');
    });

    it('reuses platform Button + AffiliateLink (no CTA logic duplication)', () => {
        expect(heroSrc).toContain("from '@/components/common'");
        expect(heroSrc).toContain('Button');
        expect(heroSrc).toContain("from '@/components/common/AffiliateLink'");
        expect(heroSrc).toContain('AffiliateLink');
    });

    it('consumes the standard section data contract (SectionRenderer passes section.data)', () => {
        // Same prop names the platform resolver forwards (title/subtitle/cta*/backgroundImage/...).
        expect(heroSrc).toContain('title,');
        expect(heroSrc).toContain('subtitle,');
        expect(heroSrc).toContain('ctaText,');
        expect(heroSrc).toContain('ctaLink,');
        expect(heroSrc).toContain('backgroundImage,');
        expect(heroSrc).toContain('overlayOpacity,');
    });

    it('is responsive and centered by default', () => {
        expect(heroSrc).toContain('max-w-7xl');
        expect(heroSrc).toContain('min-h-[88vh]');
        expect(heroSrc).toContain('items-center');
    });

    it('exposes an accessible landmark label', () => {
        expect(heroSrc).toContain("aria-label={t('home.heroLabel'");
    });

    it('default-exports a component discoverable by the section resolver glob', () => {
        expect(heroSrc).toContain('export default HeroBanner');
    });
});

describe('Pearl sections — Category Grid (Phase T1.2B)', () => {
    it('renders and default-exports a component discoverable by the section resolver glob', () => {
        expect(categorySrc).toContain('export default CategoryGrid');
        expect(categorySrc).toContain('<section');
    });

    it('preserves the existing CMS data contract (same flat props SectionRenderer forwards)', () => {
        expect(categorySrc).toContain('title, subtitle, source, maxItems, categoryIds, sectionId, columns');
    });

    it('reuses the existing Category infrastructure — no new APIs or data contracts', () => {
        expect(categorySrc).toContain("from '@/hooks'");
        expect(categorySrc).toContain('useCategories');
        expect(categorySrc).toContain('normalizeHomepageCollectionLimit');
        expect(categorySrc).toContain('normalizeHomepageGuidList');
        expect(categorySrc).toContain('resolveHomepageCollectionSource');
        expect(categorySrc).toContain('selectHomepageCollectionItems');
        expect(categorySrc).toContain("from '@/components/sections/homepageCollectionUtils'");
        expect(categorySrc).toContain('CategoryTreeDto');
        expect(categorySrc).toContain('AffiliateLink');
        expect(categorySrc).toContain('DataEmptyState');
    });

    it('applies Pearl typography tokens (Newsreader display + Jost body — no hardcoded font-family)', () => {
        expect(categorySrc).toContain('PEARL_FONT_DISPLAY');
        expect(categorySrc).toContain('PEARL_FONT_BODY');
        expect(categorySrc).toContain("from '../layout/pearlTokens'");
        expect(categorySrc).not.toContain('fontFamily:');
    });

    it('has CSS-only hover treatment (image scale, overlay, title color transition)', () => {
        expect(categorySrc).toContain('group-hover:scale-105');
        expect(categorySrc).toContain('group-hover:bg-black/15');
        expect(categorySrc).toContain('group-hover:text-[#DC2626]');
        expect(categorySrc).toContain('transition-transform');
        expect(categorySrc).toContain('transition-colors');
    });

    it('uses the existing responsive grid system (mobile/tablet/desktop columns)', () => {
        expect(categorySrc).toContain('grid grid-cols-2');
        expect(categorySrc).toContain('md:grid-cols-3');
        expect(categorySrc).toContain('xl:grid-cols-4');
    });

    it('has square editorial corners (no rounded card classes)', () => {
        expect(categorySrc).not.toContain('rounded-xl');
        expect(categorySrc).not.toContain('rounded-full');
    });

    it('exposes accessibility attributes (aria labels, semantic heading, focus-visible)', () => {
        expect(categorySrc).toContain('aria-label={title}');
        expect(categorySrc).toContain("aria-label={t('category.shopByCategory')}");
        expect(categorySrc).toContain('<h2');
        expect(categorySrc).toContain('<h3');
        expect(categorySrc).toContain('focus-visible:outline');
    });

    it('centers the title under an image-first tile (Pearl identity)', () => {
        expect(categorySrc).toContain('text-center');
        expect(categorySrc).toContain('aspect-[4/5]');
        expect(categorySrc).toContain('uppercase');
        expect(categorySrc).toContain('text-[#313E61]');
        expect(categorySrc).toContain('text-[#C9A863]');
    });

    it('preserves loading skeleton, error, and empty-state handling from the shared pattern', () => {
        expect(categorySrc).toContain('isLoading');
        expect(categorySrc).toContain('animate-pulse');
        expect(categorySrc).toContain("t('home.failedToLoadCategories')");
        expect(categorySrc).toContain("t('home.noCategoriesYet')");
    });
});

describe('Pearl product experience — Product Grid (Phase T1.2C)', () => {
    it('renders and default-exports a component discoverable by the section resolver glob', () => {
        expect(productGridSrc).toContain('export default ProductGrid');
        expect(productGridSrc).toContain('<section');
    });

    it('preserves the existing CMS data contract (same flat props SectionRenderer forwards)', () => {
        expect(productGridSrc).toContain('title,');
        expect(productGridSrc).toContain('subtitle,');
        expect(productGridSrc).toContain("layout = 'grid',");
        expect(productGridSrc).toContain('source,');
        expect(productGridSrc).toContain('maxItems,');
        expect(productGridSrc).toContain('productIds,');
        expect(productGridSrc).toContain('sectionId,');
        expect(productGridSrc).toContain('columns = 4,');
        expect(productGridSrc).toContain('rows = 2,');
    });

    it('reuses the existing product sourcing infrastructure — no new APIs', () => {
        expect(productGridSrc).toContain('useHomepageSectionProducts');
        expect(productGridSrc).toContain('normalizeHomepageCollectionLimit');
        expect(productGridSrc).toContain('normalizeHomepageGuidList');
        expect(productGridSrc).toContain('resolveHomepageCollectionSource');
        expect(productGridSrc).toContain("from '@/components/sections/homepageCollectionUtils'");
    });

    it('preserves loading / empty / error handling from the shared pattern', () => {
        expect(productGridSrc).toContain('SkeletonGrid');
        expect(productGridSrc).toContain('DataEmptyState');
        expect(productGridSrc).toContain("t('home.failedToLoad')");
        expect(productGridSrc).toContain("t('home.noProductsYet')");
        expect(productGridSrc).toContain('isCuratedSelectionInvalid');
    });

    it('uses the existing responsive grid system incl. carousel layout branch', () => {
        expect(productGridSrc).toContain('grid grid-cols-2');
        expect(productGridSrc).toContain('md:grid-cols-3');
        expect(productGridSrc).toContain('xl:grid-cols-4');
        expect(productGridSrc).toContain("layout === 'carousel'");
    });

    it('applies Pearl typography tokens and exposes an accessible section label', () => {
        expect(productGridSrc).toContain('PEARL_FONT_DISPLAY');
        expect(productGridSrc).toContain('PEARL_FONT_BODY');
        expect(productGridSrc).toContain("from '../layout/pearlTokens'");
        expect(productGridSrc).toContain('aria-label=');
        expect(productGridSrc).toContain('<h2');
    });
});

describe('Pearl product experience — Pearl Product Card (Phase T1.2C)', () => {
    it('renders a card and default-exports the component', () => {
        expect(productCardSrc).toContain('export default PearlProductCard');
    });

    it('reuses the platform image pipeline (lazy/responsive/placeholder/fallback)', () => {
        expect(productCardSrc).toContain('OptimizedImage');
        expect(productCardSrc).toContain("from '@/components/media'");
        expect(productCardSrc).toContain('resolveProductImage');
        expect(productCardSrc).toContain('sizes=');
    });

    it('reuses platform pricing logic with the exact three-branch precedence', () => {
        expect(productCardSrc).toContain('useCurrency');
        expect(productCardSrc).toContain('formatPrice');
        expect(productCardSrc).toContain('product.displayPriceText ?');
        expect(productCardSrc).toContain('product.pricing.compareAtPrice !== null && product.pricing.compareAtPrice !== undefined');
        expect(productCardSrc).toContain('formatPrice(product.pricing.currentPrice)');
        expect(productCardSrc).toContain('formatPrice(product.pricing.compareAtPrice)');
        expect(productCardSrc).toContain('line-through');
    });

    it('reuses platform badge / wishlist / affiliate / localization infrastructure', () => {
        expect(productCardSrc).toContain('ProductBadges');
        expect(productCardSrc).toContain('WishlistButton');
        expect(productCardSrc).toContain('AffiliateLink');
        expect(productCardSrc).toContain('useLocalizedContent');
        expect(productCardSrc).toContain('product.slug ? `/products/${product.slug}` : `/products/${product.id}`');
    });

    it('preserves the analytics product marker from the platform dispatcher', () => {
        expect(productCardSrc).toContain('data-ai-product-id={product.id}');
    });

    it('styles the discount badge sharp red via CSS-only restyle (logic untouched)', () => {
        expect(productCardSrc).toContain('!rounded-none');
        expect(productCardSrc).toContain('!bg-[#CD1515]');
    });

    it('applies Pearl typography tokens — no hardcoded font-family', () => {
        expect(productCardSrc).toContain('PEARL_FONT_BODY');
        expect(productCardSrc).toContain("from '../layout/pearlTokens'");
        expect(productCardSrc).not.toContain('fontFamily:');
    });

    it('has CSS-only hover treatment (image scale, shadow, title transition, CTA reveal)', () => {
        expect(productCardSrc).toContain('group-hover:scale-105');
        expect(productCardSrc).toContain('hover:shadow-');
        expect(productCardSrc).toContain('group-hover:text-[#DC2626]');
        expect(productCardSrc).toContain('group-hover:opacity-100');
        expect(productCardSrc).toContain('transition-transform');
    });

    it('CTA is the square red Pearl accent, in-stock gated like ProductCardDefault', () => {
        expect(productCardSrc).toContain('bg-[#DC2626]');
        expect(productCardSrc).toContain("t('product.addToCart')");
        expect(productCardSrc).toContain('product.isAvailable && product.inStock');
        expect(productCardSrc).toContain("t('product.outOfStock')");
    });

    it('exposes accessibility attributes (aria label, alt text, focus-visible ring)', () => {
        expect(productCardSrc).toContain('aria-label={displayName}');
        expect(productCardSrc).toContain('alt={displayName}');
        expect(productCardSrc).toContain('focus-visible:outline');
        expect(productCardSrc).toContain('<h3');
    });

    it('has square editorial corners (no rounded card classes)', () => {
        expect(productCardSrc).not.toContain('rounded-lg');
        expect(productCardSrc).not.toContain('rounded-md');
        expect(productCardSrc).not.toContain('rounded-xl');
    });
});

describe('Pearl experience — Luxury Promotional Banner (Phase T1.3)', () => {
    it('renders a section and default-exports for resolver discovery (CMS type: banner)', () => {
        expect(bannerSrc).toContain('export default BannerSection');
        expect(bannerSrc).toContain('<section');
    });

    it('preserves the existing banner data contract', () => {
        expect(bannerSrc).toContain('title,');
        expect(bannerSrc).toContain('subtitle,');
        expect(bannerSrc).toContain('ctaText,');
        expect(bannerSrc).toContain('ctaLink,');
        expect(bannerSrc).toContain('imageUrl,');
        expect(bannerSrc).toContain('backgroundImage,');
        expect(bannerSrc).toContain('imageUrl || backgroundImage');
    });

    it('reuses AffiliateLink + i18n and applies Pearl tokens (no hardcoded fonts)', () => {
        expect(bannerSrc).toContain("from '@/components/common/AffiliateLink'");
        expect(bannerSrc).toContain('useTranslation');
        expect(bannerSrc).toContain('PEARL_FONT_DISPLAY');
        expect(bannerSrc).toContain('PEARL_FONT_BODY');
        expect(bannerSrc).not.toContain('fontFamily:');
    });

    it('establishes Pearl identity: dark #1E1C24 block, gold eyebrow, red square CTA, no pill chrome', () => {
        expect(bannerSrc).toContain('bg-[#1E1C24]');
        expect(bannerSrc).toContain('text-[#C9A863]');
        expect(bannerSrc).toContain('bg-[#DC2626]');
        expect(bannerSrc).toContain('uppercase');
        expect(bannerSrc).not.toContain('rounded-');
    });

    it('has CSS-only hover, lazy image, focus states, and an aria label', () => {
        expect(bannerSrc).toContain('group-hover:scale-105');
        expect(bannerSrc).toContain('transition-transform');
        expect(bannerSrc).toContain('loading="lazy"');
        expect(bannerSrc).toContain('focus-visible:outline');
        expect(bannerSrc).toContain('aria-label=');
    });
});

describe('Pearl experience — Newsletter Signup (Phase T1.3)', () => {
    it('renders a section and default-exports for resolver discovery (CMS type: newsletter-signup)', () => {
        expect(newsletterSrc).toContain('export default NewsletterSignup');
        expect(newsletterSrc).toContain('<section');
    });

    it('preserves the existing newsletter data contract incl. i18n default-swap logic', () => {
        expect(newsletterSrc).toContain('heading,');
        expect(newsletterSrc).toContain('subheading,');
        expect(newsletterSrc).toContain('buttonText,');
        expect(newsletterSrc).toContain('backgroundColor,');
        expect(newsletterSrc).toContain('textColor,');
        expect(newsletterSrc).toContain("t('footer.stayUpdated')");
        expect(newsletterSrc).toContain("t('footer.subscribeText')");
        expect(newsletterSrc).toContain("t('footer.subscribe')");
        expect(newsletterSrc).toContain("t('footer.enterEmail')");
    });

    it('adds no business logic (form still preventDefault only)', () => {
        expect(newsletterSrc).toContain('event.preventDefault()');
        expect(newsletterSrc).not.toContain('fetch(');
        expect(newsletterSrc).not.toContain('axios');
    });

    it('establishes Pearl identity: dark #1D1C1C default, square red submit, tokens applied', () => {
        expect(newsletterSrc).toContain("backgroundColor || '#1D1C1C'");
        expect(newsletterSrc).toContain('bg-[#DC2626]');
        expect(newsletterSrc).toContain('PEARL_FONT_DISPLAY');
        expect(newsletterSrc).toContain('PEARL_FONT_BODY');
        expect(newsletterSrc).not.toContain('rounded-full');
    });

    it('exposes accessibility: labeled email input, semantic button, focus states, aria label', () => {
        expect(newsletterSrc).toContain('aria-label={t(\'footer.enterEmail\')}');
        expect(newsletterSrc).toContain('type="submit"');
        expect(newsletterSrc).toContain('focus-visible:outline');
        expect(newsletterSrc).toContain('aria-label={displayHeading}');
    });
});

describe('Pearl experience — Brand Story Text Block (Phase T1.3)', () => {
    it('renders a section and default-exports for resolver discovery (CMS type: text-block)', () => {
        expect(textBlockSrc).toContain('export default TextBlock');
        expect(textBlockSrc).toContain('<section');
    });

    it('preserves the existing text-block data contract and platform sanitizer', () => {
        expect(textBlockSrc).toContain('heading?:');
        expect(textBlockSrc).toContain('content?:');
        expect(textBlockSrc).toContain('alignment?:');
        expect(textBlockSrc).toContain('backgroundColor?:');
        expect(textBlockSrc).toContain('textColor?:');
        expect(textBlockSrc).toContain('maxWidth?:');
        expect(textBlockSrc).toContain("from '@/utils/htmlSanitizer'");
        expect(textBlockSrc).toContain('sanitizeRichTextHtml(content)');
    });

    it('establishes Pearl identity: navy serif heading, gold hairline divider, tokens applied', () => {
        expect(textBlockSrc).toContain('text-[#313E61]');
        expect(textBlockSrc).toContain('bg-[#C9A863]');
        expect(textBlockSrc).toContain('PEARL_FONT_DISPLAY');
        expect(textBlockSrc).toContain('PEARL_FONT_BODY');
        expect(textBlockSrc).not.toContain('fontFamily:');
    });

    it('uses a semantic heading and hides the decorative divider from AT', () => {
        expect(textBlockSrc).toContain('<h2');
        expect(textBlockSrc).toContain('aria-hidden="true"');
    });
});

/**
 * Alizon Theme — Layout tests (Phase A1.1)
 *
 * The Storefront test environment is node (no jsdom), and importing the
 * themed layout modules transitively loads the analytics singleton which
 * touches `document` at module-eval time. Following the repo's existing
 * architecture-test convention (which validates structure by reading source),
 * these tests assert:
 *   - pure layout logic (marquee resolution, nav classes, tokens),
 *   - the rendered markup contract via static structural assertions.
 *
 * This intentionally avoids DOM rendering so it stays in the node test env
 * alongside the rest of the suite.
 */
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { join } from 'path';
import { resolveAnnouncement } from './layout/AnnouncementMarquee';
import {
    ALIZON_CONTAINER,
    ALIZON_FONT_BODY,
    ALIZON_FONT_DISPLAY,
    ALIZON_NAV_LINK,
    alizonNavLinkClass,
} from './layout/alizonTokens';

const layoutDir = join(__dirname, 'layout');
const read = (name: string) => readFileSync(join(layoutDir, name), 'utf8');
const headerSrc = read('header.tsx');
const footerSrc = read('footer.tsx');
const marqueeSrc = read('AnnouncementMarquee.tsx');

describe('Alizon layout — tokens (global layout rhythm)', () => {
    it('exposes a shared container width + gutter', () => {
        expect(ALIZON_CONTAINER).toContain('max-w-7xl');
        expect(ALIZON_CONTAINER).toContain('px-4');
    });

    it('wires Jost (body) and Newsreader (display) typography', () => {
        expect(ALIZON_FONT_BODY).toContain('Jost');
        expect(ALIZON_FONT_DISPLAY).toContain('Newsreader');
    });

    it('nav link class reflects active state through authoritative appearance roles', () => {
        expect(alizonNavLinkClass(true)).toContain('text-primary-text');
        expect(alizonNavLinkClass(false)).toContain('hover:text-primary-text');
        expect(alizonNavLinkClass(true)).not.toBe(alizonNavLinkClass(false));
    });
});

describe('Alizon layout — Announcement Marquee (reusable primitive)', () => {
    it('is hidden when disabled', () => {
        expect(resolveAnnouncement({ enabled: false, text: 'Sale' }).visible).toBe(false);
    });

    it('shows explicit LTR text', () => {
        const r = resolveAnnouncement({ enabled: true, text: 'Limited Edition' });
        expect(r.visible).toBe(true);
        expect(r.text).toBe('Limited Edition');
        expect(r.isRTL).toBe(false);
    });

    it('prefers Arabic text + RTL when language is ar', () => {
        const r = resolveAnnouncement({
            enabled: true,
            text: 'Limited Edition',
            textAr: 'تشكيلة إصدار محدود',
            language: 'ar',
        });
        expect(r.isRTL).toBe(true);
        expect(r.text).toBe('تشكيلة إصدار محدود');
    });

    it('falls back to default text when none provided', () => {
        const r = resolveAnnouncement({ enabled: true, defaultText: 'Limited Edition Collection' });
        expect(r.visible).toBe(true);
        expect(r.text).toBe('Limited Edition Collection');
    });

    it('component is prop-driven and does not hardcode copy; defaults to dark brand', () => {
        expect(marqueeSrc).toContain('enabled');
        expect(marqueeSrc).toContain('textAr');
        expect(marqueeSrc).toContain("t('announcement.defaultText')");
        // Default accent is the Alizon dark token (imported + used as the
        // default param value for `accentClass`).
        expect(marqueeSrc).toContain('ALIZON_BRAND_BG');
        // No literal marquee copy baked into the component.
        expect(marqueeSrc).not.toMatch(/LIMITED EDITION/i);
    });
});

describe('Alizon layout — Header rendering contract', () => {
    it('renders a banner landmark, inline nav, and marquee above', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain('ariaLabel={labels.mainNavigation}');
        expect(headerSrc).toContain('<AnnouncementMarquee');
        expect(headerSrc).toContain('AnnouncementMarquee');
    });

    it('keeps the sticky reference layout but consumes merchant canvas and accent', () => {
        expect(headerSrc).toContain('sticky top-0');
        expect(headerSrc).toContain('bg-background');
        expect(headerSrc).toContain('text-primary-text');
        expect(headerSrc).not.toContain('text-[#FF0000]');
    });

    it('applies Jost nav typography (red, medium)', () => {
        expect(headerSrc).toContain('ALIZON_FONT_BODY');
        expect(headerSrc).toContain('ALIZON_NAV_LINK');
        expect(ALIZON_NAV_LINK).toContain('text-[16px]');
        expect(ALIZON_FONT_BODY).toContain('Jost');
    });

    it('consumes the runtime presentation contract and capability slots', () => {
        expect(headerSrc).toContain('HeaderPresentationProps');
        expect(headerSrc).toContain('NavigationSlot');
        expect(headerSrc).toContain('AuthenticationSlot');
        expect(headerSrc).not.toContain("from '@/stores/navigationStore'");
        expect(headerSrc).toContain("from '@/hooks/useScrollPosition'");
    });
});

describe('Alizon layout — Footer rendering contract', () => {
    it('renders footer landmark with authoritative navigation and social content', () => {
        expect(footerSrc).toContain('<footer');
        expect(footerSrc).toContain('labels.followUs');
        expect(footerSrc).toContain('labels.customerService');
        expect(footerSrc).not.toMatch(/Visa|Mastercard|Cash on Delivery/);
    });

    it('uses the black/white palette (reference site)', () => {
        expect(footerSrc).toContain('#121212');
        expect(footerSrc).toContain('text-white');
    });

    it('reuses platform footer infrastructure', () => {
        expect(footerSrc).toContain('FooterPresentationProps');
        expect(footerSrc).not.toContain("from '@/stores/footerStore'");
        expect(footerSrc).toContain("from '@/components/layout/FooterSocialIcon'");
        expect(footerSrc).toContain("from '@/components/common/AffiliateLink'");
    });
});

describe('Alizon layout — Responsive behavior', () => {
    it('header degrades gracefully: mobile toggle + desktop-only nav', () => {
        expect(headerSrc).toContain('aria-label={labels.menu}');
        expect(headerSrc).toContain('md:hidden');
        expect(headerSrc).toContain('md:flex');
    });

    it('footer grid is responsive', () => {
        expect(footerSrc).toContain('md:grid-cols');
    });
});

describe('Alizon layout — Accessibility (structural)', () => {
    it('header exposes banner + labelled nav + menu button aria', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain('ariaLabel={labels.mainNavigation}');
        expect(headerSrc).toContain('aria-expanded={mobileOpen}');
    });

    it('marquee exposes a labelled region', () => {
        expect(marqueeSrc).toContain("aria-label={t('announcement.label')");
        expect(marqueeSrc).toContain('role="region"');
    });
});

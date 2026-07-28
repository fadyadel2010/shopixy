/**
 * Hype Theme — Layout tests (Phase H1.1)
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
    HYPE_CONTAINER,
    HYPE_FONT_BODY,
    HYPE_FONT_DISPLAY,
    HYPE_NAV_LINK,
    hypeNavLinkClass,
} from './layout/hypeTokens';

const layoutDir = join(__dirname, 'layout');
const read = (name: string) => readFileSync(join(layoutDir, name), 'utf8');
const headerSrc = read('header.tsx');
const footerSrc = read('footer.tsx');
const marqueeSrc = read('AnnouncementMarquee.tsx');

describe('Hype layout — tokens (global layout rhythm)', () => {
    it('exposes a shared container width + gutter', () => {
        expect(HYPE_CONTAINER).toContain('max-w-7xl');
        expect(HYPE_CONTAINER).toContain('px-4');
    });

    it('wires Jost (body) and Newsreader (display) typography', () => {
        expect(HYPE_FONT_BODY).toContain('Jost');
        expect(HYPE_FONT_DISPLAY).toContain('Newsreader');
    });

    it('nav link class reflects active state (orange both states)', () => {
        expect(hypeNavLinkClass(true)).toContain('#F06736');
        expect(hypeNavLinkClass(false)).toContain('#F06736');
        expect(hypeNavLinkClass(true)).not.toBe(hypeNavLinkClass(false));
    });
});

describe('Hype layout — Announcement Marquee (reusable primitive)', () => {
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

    it('component is prop-driven and does not hardcode copy; defaults to orange', () => {
        expect(marqueeSrc).toContain('enabled');
        expect(marqueeSrc).toContain('textAr');
        expect(marqueeSrc).toContain("t('announcement.defaultText')");
        // Default accent is the Hype orange token (imported + used as the
        // default param value for `accentClass`).
        expect(marqueeSrc).toContain('HYPE_BRAND_BG');
        // No literal marquee copy baked into the component.
        expect(marqueeSrc).not.toMatch(/LIMITED EDITION/i);
    });
});

describe('Hype layout — Header rendering contract', () => {
    it('renders a banner landmark, inline nav, and marquee above', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain("t('accessibility.mainNavigation')");
        expect(headerSrc).toContain('<AnnouncementMarquee');
        expect(headerSrc).toContain('AnnouncementMarquee');
    });

    it('is a white sticky bar (reference site header) with orange nav', () => {
        expect(headerSrc).toContain('sticky top-0');
        expect(headerSrc).toContain('bg-white');
        // Brand orange (#F06736) is the nav/logo color.
        expect(headerSrc).toContain('#F06736');
    });

    it('applies Jost nav typography (orange, medium)', () => {
        expect(headerSrc).toContain('HYPE_FONT_BODY');
        expect(headerSrc).toContain('HYPE_NAV_LINK');
        expect(HYPE_NAV_LINK).toContain('text-[16px]');
        expect(HYPE_FONT_BODY).toContain('Jost');
    });

    it('reuses platform navigation/header infrastructure (no logic duplication)', () => {
        expect(headerSrc).toContain("from '@/components/common/CartBadge'");
        expect(headerSrc).toContain("from '@/components/common/WishlistBadge'");
        expect(headerSrc).toContain("from '@/stores/navigationStore'");
        expect(headerSrc).toContain("from '@/hooks/useScrollPosition'");
        expect(headerSrc).toContain("from '@/components/common/LanguageSwitcher'");
        expect(headerSrc).toContain("from '@/auth/components'");
    });
});

describe('Hype layout — Footer rendering contract', () => {
    it('renders footer landmark with shop links, social, payment area', () => {
        expect(footerSrc).toContain('<footer');
        expect(footerSrc).toContain("t('footer.paymentMethods')");
        expect(footerSrc).toContain("t('footer.followUs')");
        expect(footerSrc).toContain("t('footer.customerService')");
    });

    it('uses the black/orange palette (reference site)', () => {
        expect(footerSrc).toContain('bg-black');
        expect(footerSrc).toContain('#F06736');
    });

    it('reuses platform footer infrastructure', () => {
        expect(footerSrc).toContain("from '@/stores/footerStore'");
        expect(footerSrc).toContain("from '@/components/layout/FooterSocialIcon'");
        expect(footerSrc).toContain("from '@/components/common/AffiliateLink'");
    });
});

describe('Hype layout — Responsive behavior', () => {
    it('header degrades gracefully: mobile toggle + desktop-only nav', () => {
        expect(headerSrc).toContain('aria-label={t(\'header.menu\')}');
        expect(headerSrc).toContain('md:hidden');
        expect(headerSrc).toContain('md:flex');
    });

    it('footer grid is responsive', () => {
        expect(footerSrc).toContain('md:grid-cols');
    });
});

describe('Hype layout — Accessibility (structural)', () => {
    it('header exposes banner + labelled nav + menu button aria', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain('aria-label={t(\'accessibility.mainNavigation\')}');
        expect(headerSrc).toContain('aria-expanded={mobileOpen}');
    });

    it('marquee exposes a labelled region', () => {
        expect(marqueeSrc).toContain("aria-label={t('announcement.label')}");
        expect(marqueeSrc).toContain('role="region"');
    });
});

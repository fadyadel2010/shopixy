/**
 * Sharp Jewelry Theme — Layout tests (Phase T1.0)
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
    SHARP_CONTAINER,
    SHARP_FONT_BODY,
    SHARP_FONT_DISPLAY,
    SHARP_NAV_LINK,
    sharpNavLinkClass,
} from './layout/sharpTokens';

const layoutDir = join(__dirname, 'layout');
const read = (name: string) => readFileSync(join(layoutDir, name), 'utf8');
const headerSrc = read('header.tsx');
const footerSrc = read('footer.tsx');
const marqueeSrc = read('AnnouncementMarquee.tsx');

describe('Sharp layout — tokens (global layout rhythm)', () => {
    it('exposes a shared container width + gutter', () => {
        expect(SHARP_CONTAINER).toContain('max-w-7xl');
        expect(SHARP_CONTAINER).toContain('px-4');
    });

    it('wires Instrument Sans (body) and Playfair Display (display) typography', () => {
        expect(SHARP_FONT_BODY).toContain('Instrument Sans');
        expect(SHARP_FONT_DISPLAY).toContain('Playfair Display');
    });

    it('nav link class reflects active state', () => {
        expect(sharpNavLinkClass(true)).toContain('text-white');
        expect(sharpNavLinkClass(false)).toContain('text-white/60');
        expect(sharpNavLinkClass(true)).not.toBe(sharpNavLinkClass(false));
    });
});

describe('Sharp layout — Announcement Marquee (reusable primitive)', () => {
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

    it('component is prop-driven and does not hardcode copy', () => {
        expect(marqueeSrc).toContain('enabled');
        expect(marqueeSrc).toContain('textAr');
        expect(marqueeSrc).toContain("t('announcement.defaultText')");
        // No literal "LIMITED EDITION" baked into the component
        expect(marqueeSrc).not.toMatch(/LIMITED EDITION/i);
    });
});

describe('Sharp layout — Header rendering contract', () => {
    it('renders a banner landmark, centered nav, and marquee above', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain("t('accessibility.mainNavigation')");
        // Marquee is mounted above the header (copy lives in i18n, not hardcoded).
        expect(headerSrc).toContain('<AnnouncementMarquee');
        expect(headerSrc).toContain('AnnouncementMarquee');
    });

    it('is emerald bg and becomes elevated on scroll (sticky convention)', () => {
        expect(headerSrc).toContain('sticky top-0');
        expect(headerSrc).toContain('bg-[#013423]');
        expect(headerSrc).toContain('useScrollPosition');
    });

    it('applies Instrument Sans uppercase navigation typography', () => {
        // Body font token resolves to Instrument Sans; nav uses SHARP_NAV_LINK (14px uppercase, 0.2em tracking).
        expect(headerSrc).toContain('SHARP_FONT_BODY');
        expect(headerSrc).toContain('SHARP_NAV_LINK');
        expect(headerSrc).toContain('uppercase');
        expect(SHARP_NAV_LINK).toContain('tracking-[0.2em]');
        // The Instrument Sans face is wired through the token constant.
        expect(SHARP_FONT_BODY).toContain('Instrument Sans');
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

describe('Sharp layout — Footer rendering contract', () => {
    it('renders footer landmark with shop links, social, payment area', () => {
        expect(footerSrc).toContain('<footer');
        expect(footerSrc).toContain("t('footer.paymentMethods')");
        expect(footerSrc).toContain("t('footer.followUs')");
        expect(footerSrc).toContain("t('footer.customerService')");
    });

    it('uses the emerald/white luxury palette', () => {
        expect(footerSrc).toContain('bg-[#013423]');
        expect(footerSrc).toContain('text-white');
    });

    it('reuses platform footer infrastructure', () => {
        expect(footerSrc).toContain("from '@/stores/footerStore'");
        expect(footerSrc).toContain("from '@/components/layout/FooterSocialIcon'");
        expect(footerSrc).toContain("from '@/components/common/AffiliateLink'");
    });
});

describe('Sharp layout — Responsive behavior', () => {
    it('header degrades gracefully: mobile toggle + desktop-only nav', () => {
        expect(headerSrc).toContain("aria-label={t('header.menu')");
        expect(headerSrc).toContain('md:hidden');
        expect(headerSrc).toContain('md:flex');
    });

    it('footer grid is responsive', () => {
        expect(footerSrc).toContain('md:grid-cols');
    });
});

describe('Sharp layout — Accessibility (structural)', () => {
    it('header exposes banner + labelled nav + menu button aria', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain("aria-label={t('accessibility.mainNavigation')");
        expect(headerSrc).toContain('aria-expanded={mobileOpen}');
    });

    it('marquee exposes a labelled region', () => {
        expect(marqueeSrc).toContain("aria-label={t('announcement.label')");
        expect(marqueeSrc).toContain('role="region"');
    });
});

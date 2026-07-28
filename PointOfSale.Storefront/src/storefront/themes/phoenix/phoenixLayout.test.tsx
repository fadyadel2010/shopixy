/**
 * Phoenix Sports Theme — Layout tests (Phase T1.1)
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
    PHOENIX_CONTAINER,
    PHOENIX_FONT_BODY,
    PHOENIX_FONT_DISPLAY,
    PHOENIX_NAV_LINK,
    PHOENIX_HEADER_SHELL,
    phoenixNavLinkClass,
} from './layout/phoenixTokens';

const layoutDir = join(__dirname, 'layout');
const read = (name: string) => readFileSync(join(layoutDir, name), 'utf8');
const headerSrc = read('header.tsx');
const footerSrc = read('footer.tsx');
const marqueeSrc = read('AnnouncementMarquee.tsx');

describe('Phoenix layout — tokens (global layout rhythm)', () => {
    it('exposes a shared container width + gutter', () => {
        expect(PHOENIX_CONTAINER).toContain('max-w-7xl');
        expect(PHOENIX_CONTAINER).toContain('px-4');
    });

    it('wires Jost (body) and Playfair Display (display) typography', () => {
        expect(PHOENIX_FONT_BODY).toContain('Jost');
        expect(PHOENIX_FONT_DISPLAY).toContain('Playfair_Display');
    });

    it('floating header shell centers the white pill on the canvas', () => {
        expect(PHOENIX_HEADER_SHELL).toContain('max-w-[calc(100%-40px)]');
        expect(PHOENIX_HEADER_SHELL).toContain('mt-[19px]');
    });

    it('nav link class reflects active state', () => {
        expect(phoenixNavLinkClass(true)).toContain('text-black');
        expect(phoenixNavLinkClass(false)).toContain('text-black/60');
        expect(phoenixNavLinkClass(true)).not.toBe(phoenixNavLinkClass(false));
    });
});

describe('Phoenix layout — Announcement Marquee (reusable primitive)', () => {
    it('is hidden when disabled', () => {
        expect(resolveAnnouncement({ enabled: false, text: 'Sale' }).visible).toBe(false);
    });

    it('shows explicit LTR text', () => {
        const r = resolveAnnouncement({ enabled: true, text: 'Free Shipping' });
        expect(r.visible).toBe(true);
        expect(r.text).toBe('Free Shipping');
        expect(r.isRTL).toBe(false);
    });

    it('prefers Arabic text + RTL when language is ar', () => {
        const r = resolveAnnouncement({
            enabled: true,
            text: 'Free Shipping',
            textAr: 'شحن مجاني',
            language: 'ar',
        });
        expect(r.isRTL).toBe(true);
        expect(r.text).toBe('شحن مجاني');
    });

    it('falls back to default text when none provided', () => {
        const r = resolveAnnouncement({ enabled: true, defaultText: 'Phoenix Sports Collection' });
        expect(r.visible).toBe(true);
        expect(r.text).toBe('Phoenix Sports Collection');
    });

    it('component is prop-driven and does not hardcode copy', () => {
        expect(marqueeSrc).toContain('enabled');
        expect(marqueeSrc).toContain('textAr');
        expect(marqueeSrc).toContain("t('announcement.defaultText')");
        expect(marqueeSrc).not.toMatch(/FREE SHIPPING/i);
    });
});

describe('Phoenix layout — Header rendering contract', () => {
    it('renders a banner landmark, centered nav, and marquee above', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain("t('accessibility.mainNavigation')");
        expect(headerSrc).toContain('<AnnouncementMarquee');
        expect(headerSrc).toContain('AnnouncementMarquee');
    });

    it('is a floating white pill (rounded, white bg, shadow), sticky', () => {
        expect(headerSrc).toContain('sticky top-0');
        expect(headerSrc).toContain('bg-white');
        expect(headerSrc).toContain('rounded-xl');
        expect(headerSrc).toContain('PHOENIX_HEADER_SHELL');
    });

    it('applies Jost uppercase 12px navigation typography', () => {
        expect(headerSrc).toContain('PHOENIX_NAV_LINK');
        expect(headerSrc).toContain('uppercase');
        // Nav link composes PHOENIX_FONT_BODY (Jost) at 12px with 0.08em tracking.
        expect(PHOENIX_NAV_LINK).toContain('Jost');
        expect(PHOENIX_NAV_LINK).toContain('text-[12px]');
        expect(PHOENIX_NAV_LINK).toContain('tracking-[0.08em]');
    });

    it('uses Playfair Display for the brand wordmark', () => {
        expect(headerSrc).toContain('PHOENIX_FONT_DISPLAY');
        expect(PHOENIX_FONT_DISPLAY).toContain('Playfair_Display');
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

describe('Phoenix layout — Footer rendering contract', () => {
    it('renders footer landmark with shop links, social, payment area', () => {
        expect(footerSrc).toContain('<footer');
        expect(footerSrc).toContain("t('footer.paymentMethods')");
        expect(footerSrc).toContain("t('footer.followUs')");
        expect(footerSrc).toContain("t('footer.customerService')");
    });

    it('uses the black/white sports palette (footer #202021, white text)', () => {
        expect(footerSrc).toContain('bg-[#202021]');
        expect(footerSrc).toContain('text-white');
    });

    it('reuses platform footer infrastructure', () => {
        expect(footerSrc).toContain("from '@/stores/footerStore'");
        expect(footerSrc).toContain("from '@/components/layout/FooterSocialIcon'");
        expect(footerSrc).toContain("from '@/components/common/AffiliateLink'");
    });
});

describe('Phoenix layout — Responsive behavior', () => {
    it('header degrades gracefully: mobile toggle + desktop-only nav', () => {
        expect(headerSrc).toContain('aria-label={t(\'header.menu\')}');
        expect(headerSrc).toContain('md:hidden');
        expect(headerSrc).toContain('md:flex');
    });

    it('footer grid is responsive', () => {
        expect(footerSrc).toContain('md:grid-cols');
    });
});

describe('Phoenix layout — Accessibility (structural)', () => {
    it('header exposes banner + labelled nav + menu button aria', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain('aria-label={t(\'accessibility.mainNavigation\')}');
        expect(headerSrc).toContain('aria-expanded={mobileOpen}');
    });

    it('marquee exposes a labelled region', () => {
        expect(marqueeSrc).toContain("aria-label={t('announcement.label')}");
        expect(marqueeSrc).toContain("role=\"region\"");
    });
});

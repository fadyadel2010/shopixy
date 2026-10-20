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

    it('nav link class reflects active state using authoritative accent roles', () => {
        expect(hypeNavLinkClass(true)).toContain('text-primary-text');
        expect(hypeNavLinkClass(false)).toContain('hover:text-primary-text');
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

    it('component is prop-driven and does not hardcode copy; defaults to Appearance', () => {
        expect(marqueeSrc).toContain('enabled');
        expect(marqueeSrc).toContain('textAr');
        expect(marqueeSrc).toContain("t('announcement.defaultText')");
        // The default accent follows Appearance so the merchant palette stays
        // authoritative; an authored fill may still be passed explicitly.
        expect(marqueeSrc).toContain('bg-primary text-on-primary');
        expect(marqueeSrc).toContain('onColor');
        // No literal marquee copy baked into the component.
        expect(marqueeSrc).not.toMatch(/LIMITED EDITION/i);
    });
});

describe('Hype layout — Header rendering contract', () => {
    it('renders a banner landmark, inline nav, and marquee above', () => {
        expect(headerSrc).toContain('role="banner"');
        expect(headerSrc).toContain('ariaLabel={labels.mainNavigation}');
        expect(headerSrc).toContain('<AnnouncementMarquee');
        expect(headerSrc).toContain('AnnouncementMarquee');
    });

    it('retains the sticky layout while taking canvas and readable accent from Appearance', () => {
        expect(headerSrc).toContain('sticky top-0');
        expect(headerSrc).toContain('bg-background');
        expect(headerSrc).toContain('text-primary-text');
        expect(headerSrc).not.toContain('text-[#F06736]');
    });

    it('applies Jost nav typography (orange, medium)', () => {
        expect(headerSrc).toContain('HYPE_FONT_BODY');
        expect(headerSrc).toContain('HYPE_NAV_LINK');
        expect(HYPE_NAV_LINK).toContain('text-[16px]');
        expect(HYPE_FONT_BODY).toContain('Jost');
    });

    it('consumes the runtime presentation contract and capability slots', () => {
        expect(headerSrc).toContain('HeaderPresentationProps');
        expect(headerSrc).toContain('NavigationSlot');
        expect(headerSrc).toContain('AuthenticationSlot');
        expect(headerSrc).not.toContain("from '@/stores/navigationStore'");
        expect(headerSrc).toContain("from '@/hooks/useScrollPosition'");
    });
});

describe('Hype layout — Footer rendering contract', () => {
    it('renders footer landmark with authoritative navigation and social content', () => {
        expect(footerSrc).toContain('<footer');
        expect(footerSrc).toContain('labels.followUs');
        expect(footerSrc).toContain('labels.customerService');
        expect(footerSrc).not.toMatch(/Visa|Mastercard|Cash on Delivery/);
    });

    it('pairs merchant secondary chrome with its contrasting foreground', () => {
        expect(footerSrc).toContain('bg-secondary');
        expect(footerSrc).toContain('text-on-secondary');
        expect(footerSrc).not.toContain('text-[#F06736]');
    });

    it('reuses platform footer infrastructure', () => {
        expect(footerSrc).toContain('FooterPresentationProps');
        expect(footerSrc).not.toContain("from '@/stores/footerStore'");
        expect(footerSrc).toContain("from '@/components/layout/FooterSocialIcon'");
        expect(footerSrc).toContain("from '@/components/common/AffiliateLink'");
    });
});

describe('Hype layout — Responsive behavior', () => {
    it('header degrades gracefully: mobile toggle + desktop-only nav', () => {
        expect(headerSrc).toContain('aria-label={labels.menu}');
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
        expect(headerSrc).toContain('ariaLabel={labels.mainNavigation}');
        expect(headerSrc).toContain('aria-expanded={mobileOpen}');
    });

    it('marquee exposes a labelled region', () => {
        expect(marqueeSrc).toContain("aria-label={t('announcement.label')}");
        expect(marqueeSrc).toContain('role="region"');
    });
});

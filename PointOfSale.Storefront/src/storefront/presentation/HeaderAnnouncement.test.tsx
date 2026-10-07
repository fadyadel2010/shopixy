import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { HeaderAnnouncement } from './HeaderAnnouncement';
import { resolveAnnouncement as pearl } from '../themes/pearl/layout/AnnouncementMarquee';
import { resolveAnnouncement as sharp } from '../themes/sharp/layout/AnnouncementMarquee';
import { resolveAnnouncement as hype } from '../themes/hype/layout/AnnouncementMarquee';
import { resolveAnnouncement as alizon } from '../themes/alizon/layout/AnnouncementMarquee';
import { resolveAnnouncement as phoenix } from '../themes/phoenix/layout/AnnouncementMarquee';
import type { HeaderPresentationModel } from './models';

vi.mock('@/components/common/AffiliateLink', () => ({ AffiliateLink: ({ href, children, ...props }: any) => <a href={href} {...props}>{children}</a> }));
vi.mock('@/i18n', () => ({ useTranslation: () => ({ t: (key: string) => key === 'announcement.label' ? 'Announcement' : key, direction: 'rtl', language: 'ar' }) }));

describe('Header announcement presentation contract', () => {
    it('renders authored content/link/style and removes the entire region when disabled', () => {
        const model = { labels: { announcement: 'مجموعة جديدة' }, announcement: { enabled: true, text: 'Collection', textAr: 'مجموعة جديدة', link: '/products', backgroundColor: '#123456', textColor: '#ffffff' } } as HeaderPresentationModel;
        const markup = renderToStaticMarkup(<HeaderAnnouncement model={model} />);
        expect(markup).toContain('مجموعة جديدة');
        expect(markup).toContain('href="/products"');
        expect(markup).toContain('background-color:#123456');
        expect(markup).toContain('dir="rtl"');
        expect(renderToStaticMarkup(<HeaderAnnouncement model={{ ...model, announcement: { ...model.announcement!, enabled: false } }} />)).toBe('');
    });

    it.each([pearl, sharp, hype, alizon, phoenix])('each independent marquee respects visibility and Arabic content', (resolve) => {
        expect(resolve({ enabled: false, text: 'Hidden' }).visible).toBe(false);
        expect(resolve({ enabled: true, text: 'English', textAr: 'عربي', language: 'ar' }).text).toBe('عربي');
        expect(resolve({ enabled: true, text: '' }).visible).toBe(false);
    });
});

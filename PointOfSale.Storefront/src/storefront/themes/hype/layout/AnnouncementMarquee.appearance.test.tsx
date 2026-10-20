import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import AnnouncementMarquee from './AnnouncementMarquee';

describe('announcement color ownership', () => {
    it('inherits the contrasting primary foreground instead of forcing white', () => {
        const html=renderToStaticMarkup(<AnnouncementMarquee text="News" />);
        expect(html).toContain('bg-primary text-on-primary');
        expect(html).not.toContain('text-white');
    });
    it('derives a foreground for authored fills but never replaces authored text', () => {
        expect(renderToStaticMarkup(<AnnouncementMarquee text="News" backgroundColor="#f0c040" />))
            .toContain('color:#020617');
        expect(renderToStaticMarkup(<AnnouncementMarquee text="News" backgroundColor="#f0c040" textColor="#123456" />))
            .toContain('color:#123456');
    });
});

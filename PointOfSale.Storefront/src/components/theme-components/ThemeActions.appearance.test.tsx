import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { ThemeActionLink, ThemeAddToCartButton, ThemeBadge } from './ThemeComponents';

describe('runtime-owned action appearance', () => {
    it('does not mix a custom solid card action with the theme outline or hover palette', () => {
        const html = renderToStaticMarkup(<ThemeAddToCartButton themeId="cosmetics" appearance="custom" label="Add" onClick={() => {}}
            className="bg-primary text-on-primary hover:opacity-90" />);
        const classes = html.match(/class="([^"]+)"/)![1].split(' ');
        expect(classes).toContain('bg-primary');
        expect(classes).toContain('text-on-primary');
        expect(classes).toContain('focus-visible:ring-primary-text');
        expect(classes).not.toContain('bg-transparent');
        expect(classes).not.toContain('hover:bg-current');
    });

    it('pairs primary and text-filled badges without a second conflicting foreground', () => {
        for (const theme of ['modern','phoenix','hype','alizon','sharp','cosmetics']) {
            expect(renderToStaticMarkup(<ThemeBadge themeId={theme}>Featured</ThemeBadge>)).toContain('text-on-primary');
        }
        const html=renderToStaticMarkup(<ThemeBadge themeId="seven">Featured</ThemeBadge>);
        expect(html).toContain('text-on-text');
        expect(html.match(/class="([^"]+)"/)![1].split(' ')).not.toContain('text-primary');
    });

    it('uses the same custom contract for selection-required links', () => {
        const html = renderToStaticMarkup(<MemoryRouter><ThemeActionLink themeId="cosmetics" appearance="custom" href="/products/example"
            className="bg-primary text-on-primary">Select options</ThemeActionLink></MemoryRouter>);
        expect(html).toContain('href="/products/example"');
        expect(html).not.toContain('bg-transparent');
        expect(html).toContain('focus-visible:ring-primary-text');
    });
});

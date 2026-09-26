import { readFileSync } from 'fs';
import { join } from 'path';
import { describe, expect, it } from 'vitest';

const sourceRoot = join(__dirname, '..', '..');
const dedicatedThemeFooters = ['alizon', 'cosmetics', 'fashion', 'hype', 'minimal', 'pearl', 'phoenix', 'seven', 'sharp'];

describe('official theme Footer authority runtime', () => {
    it.each(dedicatedThemeFooters)('%s preserves every authored section and long-label wrapping', (theme) => {
        const source = readFileSync(join(__dirname, theme, 'layout', 'footer.tsx'), 'utf8');

        expect(source).toContain('sections.map((section)')
        expect(source).toContain('section.title')
        expect(source).toContain('section.links.map((link)')
        expect(source).toContain('break-words')
        expect(source).not.toContain('sections.flatMap')
    });

    it('renders one shared Footer runtime and preserves authored new-tab intent', () => {
        const layout = readFileSync(join(sourceRoot, 'storefront', 'layout', 'StorefrontLayout.tsx'), 'utf8');
        const runtime = readFileSync(join(sourceRoot, 'storefront', 'runtime', 'layout', 'LayoutRuntime.tsx'), 'utf8');

        expect(layout.match(/<FooterRuntime\b/g)).toHaveLength(1);
        expect(runtime).toContain('openInNewTab: link.openInNewTab');
        expect(runtime).toContain('useFooterStore(selectFooterSections)');
        expect(runtime).toContain('useNavigationStore(selectFooterLinks)');
    });
});

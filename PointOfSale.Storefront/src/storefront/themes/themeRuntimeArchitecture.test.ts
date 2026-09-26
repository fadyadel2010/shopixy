import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const ACTIVE_THEME_IDS = [
    'modern',
    'minimal',
    'fashion',
    'pearl',
    'phoenix',
    'hype',
    'alizon',
    'sharp',
    'cosmetics',
    'seven',
] as const;

const INSTALLED_THEME_IDS = [...ACTIVE_THEME_IDS, 'electronics'] as const;
const srcRoot = resolve(process.cwd(), 'src');
const themesRoot = resolve(srcRoot, 'storefront/themes');
const read = (relativePath: string) => readFileSync(resolve(srcRoot, relativePath), 'utf8');

function sourceFiles(directory: string): string[] {
    return readdirSync(directory).flatMap((entry) => {
        const path = resolve(directory, entry);
        if (statSync(path).isDirectory()) {
            return sourceFiles(path);
        }

        return /\.(ts|tsx)$/.test(entry) && !/\.test\.(ts|tsx)$/.test(entry) ? [path] : [];
    });
}

const installedThemeSources = INSTALLED_THEME_IDS.flatMap((themeId) => (
    sourceFiles(resolve(themesRoot, themeId)).map((path) => ({ path, source: readFileSync(path, 'utf8') }))
));

describe('Phase 2 Theme Runtime architecture enforcement', () => {
    it('keeps Navigation and Footer loading in the canonical Layout Runtime', () => {
        const layoutRuntime = read('storefront/runtime/layout/LayoutRuntime.tsx');
        const layout = read('storefront/layout/StorefrontLayout.tsx');

        expect(layoutRuntime).toContain('const loadNavigation = useNavigationStore');
        expect(layoutRuntime).toContain('void loadNavigation()');
        expect(layoutRuntime).toContain('const footerNavigationLinks = useNavigationStore(selectFooterLinks)');
        expect(layoutRuntime).toContain('footerNavigationLinks.map');
        expect(layoutRuntime).toContain('const loadFooter = useFooterStore');
        expect(layoutRuntime).toContain('void loadFooter()');
        expect(layout).toContain('<HeaderRuntime component={HeaderComponent} />');
        expect(layout).toContain('<FooterRuntime component={FooterComponent} />');

        for (const { path, source } of installedThemeSources) {
            expect(source, path).not.toMatch(/\b(?:loadNavigation|loadFooter|useNavigationStore|useFooterStore)\b/);
        }
    });

    it('prevents installed Themes from importing stores, APIs, queries, providers, or runtime owners', () => {
        const forbidden = [
            /from\s+['"]@\/(?:store(?:\/|['"])|stores\/)/,
            /from\s+['"]@\/services\//,
            /\bfetch\s*\(/,
            /\baxios\b/i,
            /@tanstack\/react-query/,
            /\b(?:useQuery|useMutation)\b/,
            /\bRuntimeSectionContainer\b/,
            /\bSearchRuntimeProvider\b/,
            /\bProductCardRuntime\b/,
            /\bProductTemplateResolver\b/,
            /\bThemePage(?:Resolver|Route)\b/,
        ];

        for (const { path, source } of installedThemeSources) {
            for (const pattern of forbidden) {
                expect(source, `${path} violates ${pattern}`).not.toMatch(pattern);
            }
        }
    });

    it('prevents theme presentation from inventing merchant content or payment support', () => {
        const sharedPresentationSources = [
            read('components/theme-components/ThemeComponents.tsx'),
            read('storefront/commerce/CommerceBrowsingExperience.tsx'),
        ];
        const sources = [
            ...installedThemeSources.map(({ source }) => source),
            ...sharedPresentationSources,
        ];
        const forbiddenContent = [
            'Campaign story',
            'Editorial storefront',
            'New season',
            'private collection showcase',
            'La Femmea',
            'Cash on Delivery',
            '>Visa<',
            '>Mastercard<',
            'Collection {String(index + 1)',
            'Drop {String(index + 1)',
            'HYPE-{String(index + 1)',
            'CAT-{String(index + 1)',
        ];

        for (const source of sources) {
            for (const content of forbiddenContent) {
                expect(source).not.toContain(content);
            }
        }
    });

    it('requires every active custom header to place every runtime-owned capability slot', () => {
        const requiredSlots = [
            'NavigationSlot',
            'SearchSlot',
            'AuthenticationSlot',
            'WishlistSlot',
            'CartSlot',
            'LanguageSlot',
        ];

        for (const themeId of ACTIVE_THEME_IDS.filter((themeId) => themeId !== 'modern')) {
            const header = read(`storefront/themes/${themeId}/layout/header.tsx`);
            expect(header).toContain('HeaderPresentationProps');
            for (const slot of requiredSlots) {
                expect(header, `${themeId} omitted ${slot}`).toContain(`<${slot}`);
            }
            expect(header).not.toMatch(/\.slice\s*\(/);
            expect(header).not.toMatch(/\.filter\s*\(/);
        }

        const defaultHeader = read('components/layout/Header.tsx');
        for (const slot of requiredSlots) {
            expect(defaultHeader, `default header omitted ${slot}`).toContain(`<${slot}`);
        }
        expect(defaultHeader).toContain('props.model.headerVariant');
    });

    it('requires every active Theme footer to consume the complete presentation model', () => {
        for (const themeId of ACTIVE_THEME_IDS) {
            const footer = themeId === 'modern'
                ? read('components/layout/Footer.tsx')
                : read(`storefront/themes/${themeId}/layout/footer.tsx`);
            expect(footer).toContain('FooterPresentationProps');
            expect(footer).toContain('navigation');
            expect(footer).toContain('navigation.links');
            expect(footer).not.toMatch(/navigation\.links\.length\s*>\s*0\s*\?\s*navigation\.links\s*:/);
            expect(footer).not.toMatch(/\.slice\s*\(/);
            expect(footer).not.toMatch(/\.filter\s*\(/);
        }
    });

    it('keeps canonical executable pages directly owned by the router', () => {
        const router = read('app/router.tsx');
        const canonicalPages = ['CartPage', 'CheckoutPage', 'SearchPage', 'AccountPage', 'WishlistPage'];

        for (const page of canonicalPages) {
            expect(router).toContain(`<${page} />`);
        }
        expect(router).not.toContain('ThemePageRoute');
        expect(existsSync(resolve(themesRoot, 'ThemePageResolver.ts'))).toBe(false);
        expect(existsSync(resolve(srcRoot, 'storefront/pages/ThemePageRoute.tsx'))).toBe(false);

        for (const themeId of INSTALLED_THEME_IDS) {
            expect(existsSync(resolve(themesRoot, themeId, 'pages'))).toBe(false);
        }
    });

    it('prevents Theme identity from selecting Runtime behavior or data cardinality', () => {
        const runtimeContainer = read('storefront/runtime/containers/RuntimeSectionContainer.tsx');
        const hypePresentation = read('storefront/themes/hype/sections/product-grid.tsx');

        expect(runtimeContainer).not.toMatch(/themeId\s*={2,3}\s*['"](?:hype|electronics)['"]/);
        expect(runtimeContainer).not.toMatch(/['"](?:hype|electronics)['"]\s*={2,3}\s*themeId/);
        expect(runtimeContainer).toContain("sectionProps.layout === 'carousel' ? 'carousel' : 'grid'");
        expect(runtimeContainer).toContain('const maxValue = 18');
        expect(hypePresentation).toContain("(displayStyle ?? 'carousel') === 'carousel'");
    });
});

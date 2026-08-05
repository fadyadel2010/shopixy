import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { Footer } from '@/components/layout/Footer';

const read = (relativePath: string) => readFileSync(resolve(process.cwd(), relativePath), 'utf8');

describe('post-certification navigation runtime alignment', () => {
    it('projects published navigation into header and footer presentation models', () => {
        const layoutRuntime = read('src/storefront/runtime/layout/LayoutRuntime.tsx');

        expect(layoutRuntime).toContain('const headerLinks = useNavigationStore(selectHeaderLinks)');
        expect(layoutRuntime).toContain('resolveNavLinkUrl(link)');
        expect(layoutRuntime).toContain('const footerNavigationLinks = useNavigationStore(selectFooterLinks)');
        expect(layoutRuntime).toContain('navigation: {');
        expect(layoutRuntime).toContain('links: footerNavigationLinks.map');
    });

    it('default theme footer renders runtime navigation without replacing footer sections', () => {
        const html = renderToStaticMarkup(
            <Footer
                model={{
                    storeName: 'Runtime Store',
                    currentYear: 2026,
                    copyright: 'Copyright',
                    navigation: {
                        links: [
                            {
                                key: 'cms-footer-link',
                                label: 'CMS Footer Link',
                                href: '/footer-navigation',
                                openInNewTab: false,
                            },
                        ],
                        isLoading: false,
                        error: null,
                    },
                    sections: [
                        {
                            key: 'footer-editor',
                            title: 'Footer Editor',
                            sortOrder: 0,
                            links: [
                                {
                                    key: 'footer-editor-content',
                                    label: 'Footer Editor Content',
                                    href: '/editor-content',
                                    openInNewTab: false,
                                },
                            ],
                        },
                    ],
                    socialLinks: [],
                    isLoading: false,
                    error: null,
                    labels: {
                        contact: 'Contact',
                        customerService: 'Customer service',
                        followUs: 'Follow us',
                        loading: 'Loading',
                        paymentMethods: 'Payment methods',
                        quickLinks: 'Quick links',
                    },
                }}
            />
        );

        expect(html).toContain('CMS Footer Link');
        expect(html).toContain('/footer-navigation');
        expect(html).toContain('Footer Editor Content');
        expect(html).toContain('/editor-content');
    });
});

import { describe, expect, it } from 'vitest';
import { resolveProductTemplate } from './ProductTemplateResolver';
import type { StoreConfig } from '../../types';

describe('ProductTemplateResolver', () => {
    it('prefers persisted tenant templates over built-in templates with the same id', () => {
        const config = {
            updatedAtUtc: '2026-07-28T00:00:00.000Z',
            productTemplates: {
                default: {
                    templateId: 'default',
                    name: 'Tenant Default',
                    sections: [
                        {
                            id: 'tenant_product_hero',
                            type: 'product-hero',
                            enabled: true,
                            order: 0,
                            settings: { layoutVariant: 'fashion' },
                        },
                    ],
                },
            },
        } as unknown as StoreConfig;

        const resolved = resolveProductTemplate('default', config);

        expect(resolved.name).toBe('Tenant Default');
        expect(resolved.sections).toHaveLength(1);
        expect(resolved.sections[0].id).toBe('runtime_product_hero_default');
        expect(resolved.sections[0].enabled).toBe(true);
        expect(resolved.sections[0].settings.layoutVariant).toBe('fashion');
    });

    it('does not let Product-page Builder sections replace Product Details composition', () => {
        const config = {
            updatedAtUtc: '2026-07-28T00:00:01.000Z',
            pages: {
                product: {
                    sections: [
                        {
                            id: 'configured_product_page_section',
                            type: 'product-specs',
                            enabled: true,
                            order: 0,
                            settings: {
                                layoutVariant: 'minimal',
                                title: 'Configured Details',
                            },
                        },
                    ],
                },
            },
            productTemplates: {},
        } as unknown as StoreConfig;

        const resolved = resolveProductTemplate(null, config);

        expect(resolved.sections[0].type).toBe('product-hero');
        expect(resolved.sections.map((section) => section.id)).not.toContain('configured_product_page_section');
    });

    it('falls back to the canonical product detail template when the configured product page is a listing section set', () => {
        const config = {
            updatedAtUtc: '2026-07-28T00:00:02.000Z',
            pages: {
                product: {
                    sections: [
                        {
                            id: 'configured_listing_grid',
                            type: 'product-grid',
                            enabled: true,
                            order: 0,
                            settings: {
                                title: 'All products',
                                subtitle: '',
                                source: 'all',
                                maxItems: 12,
                                layout: 'grid',
                                productIds: [],
                            },
                        },
                    ],
                },
            },
            productTemplates: {},
        } as unknown as StoreConfig;

        const resolved = resolveProductTemplate(null, config);

        expect(resolved.sections[0].type).toBe('product-hero');
        expect(resolved.sections.map((section) => section.type)).not.toContain('product-grid');
    });

    it('keeps the canonical Product Details runtime when a saved layout omits its hero', () => {
        const config = {
            updatedAtUtc: '2026-07-28T00:00:03.000Z',
            productTemplates: {
                editorial: {
                    templateId: 'editorial',
                    name: 'Editorial',
                    sections: [
                        {
                            id: 'editorial_specs',
                            type: 'product-specs',
                            enabled: true,
                            order: 1,
                            settings: { layoutVariant: 'default', title: 'Details' },
                        },
                    ],
                },
            },
        } as unknown as StoreConfig;

        const resolved = resolveProductTemplate('editorial', config);

        expect(resolved.sections[0].type).toBe('product-hero');
        expect(resolved.sections.map((section) => section.id)).toContain('editorial_specs');
    });

    it('replaces disabled and duplicate authored heroes with exactly one enabled Runtime hero', () => {
        const config = {
            updatedAtUtc: '2026-08-02T00:00:00.000Z',
            productTemplates: {
                unsafe: {
                    templateId: 'unsafe',
                    name: 'Unsafe legacy template',
                    sections: [
                        { id: 'hero_one', type: 'product-hero', enabled: false, order: 0, settings: { layoutVariant: 'minimal' } },
                        { id: 'hero_two', type: 'product-hero', enabled: true, order: 1, settings: { layoutVariant: 'fashion' } },
                        { id: 'related', type: 'related-products', enabled: true, order: 2, settings: { layoutVariant: 'default', title: 'Related', limit: 4 } },
                    ],
                },
            },
        } as unknown as StoreConfig;

        const resolved = resolveProductTemplate('unsafe', config);
        const heroes = resolved.sections.filter((section) => section.type === 'product-hero');

        expect(heroes).toHaveLength(1);
        expect(heroes[0]).toMatchObject({ enabled: true, order: 0 });
        expect(resolved.sections[1]).toMatchObject({ id: 'related', order: 1 });
    });
});

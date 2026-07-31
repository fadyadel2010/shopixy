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
                            settings: {
                                layoutVariant: 'fashion',
                                showDescription: true,
                                showMediaGallery: false,
                                showVariantSelector: true,
                                showAddToCart: true,
                                showSku: false,
                                showStockStatus: true,
                            },
                        },
                    ],
                },
            },
        } as unknown as StoreConfig;

        const resolved = resolveProductTemplate('default', config);

        expect(resolved.name).toBe('Tenant Default');
        expect(resolved.sections).toHaveLength(1);
        expect(resolved.sections[0].id).toBe('tenant_product_hero');
        expect(resolved.sections[0].settings.layoutVariant).toBe('fashion');
    });

    it('prefers the configured product page before built-ins when it contains product detail sections', () => {
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
                                useDescription: true,
                                items: [],
                            },
                        },
                    ],
                },
            },
            productTemplates: {},
        } as unknown as StoreConfig;

        const resolved = resolveProductTemplate(null, config);

        expect(resolved.sections).toHaveLength(1);
        expect(resolved.sections[0].id).toBe('configured_product_page_section');
        expect(resolved.sections[0].settings.title).toBe('Configured Details');
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
                                source: 'featured',
                                maxItems: 12,
                                columns: 4,
                                rows: 3,
                                showPrice: true,
                                showRating: false,
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
});

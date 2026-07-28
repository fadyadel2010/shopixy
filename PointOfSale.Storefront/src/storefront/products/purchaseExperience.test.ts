import { describe, expect, it } from 'vitest';
import { getBuiltinProductTemplate } from './ProductTemplateRegistry';
import {
    getPurchaseExperienceProfile,
    resolvePurchaseExperienceVariant,
} from './purchaseExperience';

describe('purchase experience profiles', () => {
    it('maps active storefront themes to distinct product detail philosophies', () => {
        expect(resolvePurchaseExperienceVariant('default', 'modern')).toBe('modern');
        expect(resolvePurchaseExperienceVariant('default', 'minimal')).toBe('minimal');
        expect(resolvePurchaseExperienceVariant('default', 'fashion')).toBe('fashion');
        expect(resolvePurchaseExperienceVariant('default', 'pearl')).toBe('pearl');
        expect(resolvePurchaseExperienceVariant('default', 'phoenix')).toBe('phoenix');
        expect(resolvePurchaseExperienceVariant('default', 'hype')).toBe('hype');
    });

    it('lets explicit product templates override default layout presentation', () => {
        expect(resolvePurchaseExperienceVariant('electronics', 'modern')).toBe('electronics');
        expect(resolvePurchaseExperienceVariant('fashion', 'minimal')).toBe('fashion');
        expect(resolvePurchaseExperienceVariant('pearl', 'fashion')).toBe('pearl');
    });

    it('gives every canonical D4 profile unique purchase language', () => {
        const profiles = ['modern', 'minimal', 'fashion', 'pearl', 'phoenix', 'hype', 'electronics'].map((variant) =>
            getPurchaseExperienceProfile(variant, 'modern')
        );

        expect(new Set(profiles.map((profile) => profile.philosophy)).size).toBe(7);
        expect(profiles.map((profile) => profile.relatedTitle)).toEqual([
            'Recommended Products',
            'Curated Selection',
            'Complete the Look',
            'Boutique Collection',
            'Training Rotation',
            'Compatible Drops',
            'Compatible Products',
        ]);
    });

    it('registers built-in product templates for theme-specific purchase journeys', () => {
        for (const templateId of ['modern', 'minimal', 'fashion', 'pearl', 'phoenix', 'hype', 'electronics']) {
            const template = getBuiltinProductTemplate(templateId);

            expect(template?.templateId).toBe(templateId);
            expect(template?.sections[0]?.type).toMatch(/^product-(hero|gallery)$/);
            expect(template?.sections.some((section) => section.type === 'product-hero')).toBe(true);
            expect(template?.sections.some((section) => section.type === 'product-specs')).toBe(true);
            expect(template?.sections.some((section) => section.type === 'related-products')).toBe(true);
        }
    });
});

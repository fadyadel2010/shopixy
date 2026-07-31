import type { StoreConfig, StorefrontProductTemplate } from '../../types';
import { getBuiltinProductTemplate } from './ProductTemplateRegistry';

const templateCache = new Map<string, StorefrontProductTemplate>();

const cloneTemplate = (template: StorefrontProductTemplate): StorefrontProductTemplate => ({
    ...template,
    sections: template.sections.map((section) => ({
        ...section,
        settings: { ...section.settings },
    })),
});

const PRODUCT_DETAIL_SECTION_TYPES = new Set([
    'product-hero',
    'product-gallery',
    'product-specs',
    'product-video',
]);

const isProductDetailPageTemplate = (template?: StorefrontProductTemplate | null): template is StorefrontProductTemplate =>
    Boolean(template?.sections?.some((section) => PRODUCT_DETAIL_SECTION_TYPES.has(section.type)));

export const resolveProductTemplate = (
    templateId: string | null | undefined,
    config?: StoreConfig | null
): StorefrontProductTemplate => {
    const resolvedTemplateId = templateId?.trim() || 'default';
    const cacheKey = `${config?.updatedAtUtc || 'default'}:${resolvedTemplateId}`;

    const cachedTemplate = templateCache.get(cacheKey);
    if (cachedTemplate) {
        return cloneTemplate(cachedTemplate);
    }

    const savedTemplate = config?.productTemplates?.[resolvedTemplateId];
    const hasConfiguredDefaultProductSections = (config?.pages?.product?.sections?.length ?? 0) > 0;
    const configuredDefaultProductPageTemplate = resolvedTemplateId === 'default' && hasConfiguredDefaultProductSections && config?.pages?.product
        ? {
            templateId: 'default',
            name: 'Default Template',
            sections: config.pages.product.sections || [],
        }
        : null;
    const defaultProductPageTemplate = isProductDetailPageTemplate(configuredDefaultProductPageTemplate)
        ? configuredDefaultProductPageTemplate
        : null;
    const builtinTemplate = getBuiltinProductTemplate(resolvedTemplateId);
    const fallbackTemplate = savedTemplate
        || defaultProductPageTemplate
        || builtinTemplate
        || getBuiltinProductTemplate('default')
        || { templateId: 'default', name: 'Default Template', sections: [] };

    const normalizedTemplate = cloneTemplate({
        templateId: fallbackTemplate.templateId || resolvedTemplateId,
        name: fallbackTemplate.name || resolvedTemplateId,
        sections: fallbackTemplate.sections || [],
    });

    if (import.meta.env.DEV) {
        console.info('[ProductTemplateResolver] Resolved template', {
            requestedTemplateId: templateId ?? null,
            resolvedTemplateId,
            usedBuiltinTemplate: !savedTemplate && !defaultProductPageTemplate && Boolean(builtinTemplate),
            usedSavedTemplate: Boolean(savedTemplate),
            usedDefaultProductPage: !savedTemplate && Boolean(defaultProductPageTemplate),
            ignoredDefaultProductPage: Boolean(configuredDefaultProductPageTemplate && !defaultProductPageTemplate),
            availableSavedTemplates: Object.keys(config?.productTemplates || {}),
            finalTemplateId: normalizedTemplate.templateId,
            finalSectionTypes: normalizedTemplate.sections.map((section) => section.type),
        });
    }

    templateCache.set(cacheKey, normalizedTemplate);
    return cloneTemplate(normalizedTemplate);
};

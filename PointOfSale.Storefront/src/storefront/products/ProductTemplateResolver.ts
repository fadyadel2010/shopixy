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

const injectProductDetailsRuntime = (
    template: StorefrontProductTemplate,
    builtinTemplate: StorefrontProductTemplate | null,
    fallbackTemplate: StorefrontProductTemplate | null
): StorefrontProductTemplate => {
    const declaredHero = template.sections.find((section) => section.type === 'product-hero');
    const builtinHero = builtinTemplate?.sections.find((section) => section.type === 'product-hero');
    const fallbackHero = fallbackTemplate?.sections.find((section) => section.type === 'product-hero');
    const layoutVariant = [declaredHero, builtinHero, fallbackHero]
        .map((section) => section?.settings?.layoutVariant)
        .find((value): value is string => typeof value === 'string' && value.length > 0) || 'default';
    const supplementalSections = template.sections
        .filter((section) => section.type !== 'product-hero' && section.type !== 'customer-reviews')
        .map((section, index) => ({ ...section, order: index + 1 }));

    return {
        ...template,
        sections: [
            {
                id: `runtime_product_hero_${template.templateId || 'default'}`,
                type: 'product-hero',
                enabled: true,
                order: 0,
                settings: { layoutVariant },
            },
            ...supplementalSections,
        ],
    };
};

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
    const builtinTemplate = getBuiltinProductTemplate(resolvedTemplateId);
    const fallbackTemplate = savedTemplate
        || builtinTemplate
        || getBuiltinProductTemplate('default')
        || { templateId: 'default', name: 'Default Template', sections: [] };

    const normalizedTemplate = cloneTemplate(injectProductDetailsRuntime({
        templateId: fallbackTemplate.templateId || resolvedTemplateId,
        name: fallbackTemplate.name || resolvedTemplateId,
        sections: fallbackTemplate.sections || [],
    }, builtinTemplate, getBuiltinProductTemplate('default')));

    if (import.meta.env.DEV) {
        console.info('[ProductTemplateResolver] Resolved template', {
            requestedTemplateId: templateId ?? null,
            resolvedTemplateId,
            usedBuiltinTemplate: !savedTemplate && Boolean(builtinTemplate),
            usedSavedTemplate: Boolean(savedTemplate),
            availableSavedTemplates: Object.keys(config?.productTemplates || {}),
            finalTemplateId: normalizedTemplate.templateId,
            finalSectionTypes: normalizedTemplate.sections.map((section) => section.type),
        });
    }

    templateCache.set(cacheKey, normalizedTemplate);
    return cloneTemplate(normalizedTemplate);
};

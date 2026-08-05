import type { ProductReadDto, StorefrontProductTemplate, StorefrontSection } from '../../types';
import { getSectionDefaultSettings } from '../runtime/sectionContract';

const createSection = (
    id: string,
    type: string,
    order: number,
    settings: Record<string, unknown> = {}
): StorefrontSection => ({
    id,
    type,
    enabled: true,
    order,
    settings: {
        ...getSectionDefaultSettings(type),
        ...settings,
    },
});

interface BuiltinTemplateDefinition {
    templateId: string;
    name: string;
    layoutVariant: string;
    specsTitle: string;
    testimonialTitle: string;
    relatedTitle: string;
    includeGallery?: boolean;
    includeVideo?: boolean;
}

const createBuiltinTemplate = (definition: BuiltinTemplateDefinition): StorefrontProductTemplate => {
    const sections: StorefrontSection[] = [
        createSection(`section_runtime_product_hero_${definition.templateId}`, 'product-hero', 0, {
            layoutVariant: definition.layoutVariant,
        }),
    ];

    if (definition.includeGallery) {
        sections.push(createSection(`product_gallery_${definition.templateId}`, 'product-gallery', sections.length, {
            layoutVariant: definition.layoutVariant,
            title: 'Gallery',
        }));
    }

    sections.push(createSection(`product_specs_${definition.templateId}`, 'product-specs', sections.length, {
        layoutVariant: definition.layoutVariant,
        title: definition.specsTitle,
    }));

    if (definition.includeVideo) {
        sections.push(createSection(`product_video_${definition.templateId}`, 'product-video', sections.length, {
            title: 'Watch It In Action',
        }));
    }

    sections.push(createSection(`testimonials_${definition.templateId}`, 'testimonials', sections.length, {
        title: definition.testimonialTitle,
        testimonials: [],
    }));
    sections.push(createSection(`related_products_${definition.templateId}`, 'related-products', sections.length, {
        layoutVariant: definition.layoutVariant,
        title: definition.relatedTitle,
        limit: 4,
    }));

    return {
        templateId: definition.templateId,
        name: definition.name,
        sections,
    };
};

const BUILTIN_PRODUCT_TEMPLATES: Record<string, StorefrontProductTemplate> = Object.fromEntries(
    [
        {
            templateId: 'default',
            name: 'Default Template',
            layoutVariant: 'default',
            specsTitle: 'Product Details',
            testimonialTitle: 'Customer Testimonials',
            relatedTitle: 'Recommended Products',
        },
        {
            templateId: 'modern',
            name: 'Modern Purchase Template',
            layoutVariant: 'modern',
            specsTitle: 'Product Details',
            testimonialTitle: 'Customer Testimonials',
            relatedTitle: 'Recommended Products',
        },
        {
            templateId: 'minimal',
            name: 'Minimal Purchase Template',
            layoutVariant: 'minimal',
            specsTitle: 'Details',
            testimonialTitle: 'Customer Notes',
            relatedTitle: 'Curated Selection',
        },
        {
            templateId: 'electronics',
            name: 'Electronics Template',
            layoutVariant: 'electronics',
            specsTitle: 'Technical Specifications',
            testimonialTitle: 'Customer Testimonials',
            relatedTitle: 'Compatible Products',
            includeVideo: true,
        },
        {
            templateId: 'fashion',
            name: 'Fashion Template',
            layoutVariant: 'fashion',
            specsTitle: 'Style Notes',
            testimonialTitle: 'Style Testimonials',
            relatedTitle: 'Complete the Look',
            includeGallery: true,
        },
        {
            templateId: 'pearl',
            name: 'Pearl Purchase Template',
            layoutVariant: 'pearl',
            specsTitle: 'Craftsmanship',
            testimonialTitle: 'Client Impressions',
            relatedTitle: 'Boutique Collection',
        },
        {
            templateId: 'phoenix',
            name: 'Phoenix Sports Purchase Template',
            layoutVariant: 'phoenix',
            specsTitle: 'Performance Details',
            testimonialTitle: 'Athlete Testimonials',
            relatedTitle: 'Training Rotation',
        },
        {
            templateId: 'hype',
            name: 'Hype Electronics Purchase Template',
            layoutVariant: 'hype',
            specsTitle: 'Tech Specifications',
            testimonialTitle: 'Community Testimonials',
            relatedTitle: 'Compatible Drops',
            includeVideo: true,
        },
        {
            templateId: 'alizon',
            name: 'Alizon Superstore Template',
            layoutVariant: 'alizon',
            specsTitle: 'Product Details',
            testimonialTitle: 'Customer Testimonials',
            relatedTitle: 'More Like This',
        },
        {
            templateId: 'cosmetics',
            name: 'Cosmetics Template',
            layoutVariant: 'cosmetics',
            specsTitle: 'Ingredients & Benefits',
            testimonialTitle: 'What Customers Say',
            relatedTitle: 'More To Explore',
        },
        {
            templateId: 'sharp',
            name: 'Sharp Jewelry Template',
            layoutVariant: 'sharp',
            specsTitle: 'Details',
            testimonialTitle: 'Client Testimonials',
            relatedTitle: 'Complete the Look',
        },
        {
            templateId: 'seven',
            name: 'Seven Womenwear Purchase Template',
            layoutVariant: 'fashion',
            specsTitle: 'Details',
            testimonialTitle: 'Style Testimonials',
            relatedTitle: 'Complete the Look',
        },
    ].map((definition) => {
        const template = createBuiltinTemplate(definition);
        return [template.templateId, template];
    })
);

export const getBuiltinProductTemplates = (): StorefrontProductTemplate[] =>
    Object.values(BUILTIN_PRODUCT_TEMPLATES).map((template) => ({
        ...template,
        sections: template.sections.map((section) => ({
            ...section,
            settings: { ...section.settings },
        })),
    }));

export const getBuiltinProductTemplate = (templateId?: string | null): StorefrontProductTemplate | null => {
    const resolvedTemplateId = templateId?.trim() || 'default';
    const template = BUILTIN_PRODUCT_TEMPLATES[resolvedTemplateId];

    if (!template) {
        return null;
    }

    return {
        ...template,
        sections: template.sections.map((section) => ({
            ...section,
            settings: { ...section.settings },
        })),
    };
};

export const getProductTemplateOptions = (
    savedTemplates?: Record<string, StorefrontProductTemplate> | null
): Array<{ id: string; name: string }> => {
    const options = new Map<string, string>();

    getBuiltinProductTemplates().forEach((template) => {
        options.set(template.templateId, template.name);
    });

    Object.entries(savedTemplates || {}).forEach(([templateId, template]) => {
        options.set(templateId, template?.name || templateId);
    });

    return Array.from(options.entries()).map(([id, name]) => ({ id, name }));
};

export const getProductTemplatePreviewProduct = (): ProductReadDto => ({
    id: 'preview-product',
    name: 'Preview Product',
    nameAr: 'Ù…Ù†ØªØ¬ ØªØ¬Ø±ÙŠØ¨ÙŠ',
    description: 'A sample product used to preview product templates inside the builder.',
    sku: 'PREVIEW-001',
    barcode: '1234567890123',
    price: 1299,
    effectivePrice: 1299,
    pricing: {
        currentPrice: 1299,
        compareAtPrice: null,
        discountedPrice: null,
        discountBadge: null,
    },
    categoryName: 'Preview Category',
    categoryId: 'preview-category',
    slug: 'preview-product',
    productTemplate: 'default',
    media: [
        {
            id: 'preview-media-1',
            mediaType: 'image',
            url: 'https://images.unsplash.com/photo-1511707171634-5f897ff02aa9?auto=format&fit=crop&w=1200&q=80',
            position: 1,
        },
        {
            id: 'preview-media-2',
            mediaType: 'image',
            url: 'https://images.unsplash.com/photo-1517336714739-489689fd1ca8?auto=format&fit=crop&w=1200&q=80',
            position: 2,
        },
    ],
    images: [
        'https://images.unsplash.com/photo-1511707171634-5f897ff02aa9?auto=format&fit=crop&w=1200&q=80',
        'https://images.unsplash.com/photo-1517336714739-489689fd1ca8?auto=format&fit=crop&w=1200&q=80',
    ],
    isAvailable: true,
    inStock: true,
    stockQuantity: 24,
    createdAtUtc: new Date().toISOString(),
    variants: [],
    hasVariants: false,
});

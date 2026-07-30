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

const BUILTIN_PRODUCT_TEMPLATES: Record<string, StorefrontProductTemplate> = {
    default: {
        templateId: 'default',
        name: 'Default Template',
        sections: [
            createSection('product_hero_default', 'product-hero', 0, {
                layoutVariant: 'default',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_default', 'product-specs', 1, {
                layoutVariant: 'default',
                title: 'Product Details',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_default', 'customer-reviews', 2, {
                layoutVariant: 'default',
                title: 'Customer Reviews',
                reviews: [],
            }),
            createSection('related_products_default', 'related-products', 3, {
                layoutVariant: 'default',
                title: 'Recommended Products',
                limit: 4,
                showPrice: true,
                showRating: true,
            }),
        ],
    },
    modern: {
        templateId: 'modern',
        name: 'Modern Purchase Template',
        sections: [
            createSection('product_hero_modern', 'product-hero', 0, {
                layoutVariant: 'modern',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_modern', 'product-specs', 1, {
                layoutVariant: 'modern',
                title: 'Product Details',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_modern', 'customer-reviews', 2, {
                layoutVariant: 'modern',
                title: 'Customer Reviews',
                reviews: [],
            }),
            createSection('related_products_modern', 'related-products', 3, {
                layoutVariant: 'modern',
                title: 'Recommended Products',
                limit: 4,
                showPrice: true,
                showRating: true,
            }),
        ],
    },
    minimal: {
        templateId: 'minimal',
        name: 'Minimal Purchase Template',
        sections: [
            createSection('product_hero_minimal', 'product-hero', 0, {
                layoutVariant: 'minimal',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_minimal', 'product-specs', 1, {
                layoutVariant: 'minimal',
                title: 'Details',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_minimal', 'customer-reviews', 2, {
                layoutVariant: 'minimal',
                title: 'Customer Notes',
                reviews: [],
            }),
            createSection('related_products_minimal', 'related-products', 3, {
                layoutVariant: 'minimal',
                title: 'Curated Selection',
                limit: 4,
                showPrice: true,
                showRating: false,
            }),
        ],
    },
    electronics: {
        templateId: 'electronics',
        name: 'Electronics Template',
        sections: [
            createSection('product_hero_electronics', 'product-hero', 0, {
                layoutVariant: 'electronics',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_electronics', 'product-specs', 1, {
                layoutVariant: 'electronics',
                title: 'Technical Specifications',
                useDescription: true,
                items: [],
            }),
            createSection('product_video_electronics', 'product-video', 2, {
                title: 'Watch It In Action',
                videoUrl: '',
                autoplay: false,
                showControls: true,
            }),
            createSection('customer_reviews_electronics', 'customer-reviews', 3, {
                layoutVariant: 'electronics',
                title: 'Verified Buyer Feedback',
                reviews: [],
            }),
            createSection('related_products_electronics', 'related-products', 4, {
                layoutVariant: 'electronics',
                title: 'Compatible Products',
                limit: 4,
                showPrice: true,
                showRating: true,
            }),
        ],
    },
    fashion: {
        templateId: 'fashion',
        name: 'Fashion Template',
        sections: [
            createSection('product_gallery_fashion', 'product-gallery', 0, {
                layoutVariant: 'fashion',
                title: 'Gallery',
                showThumbnails: true,
                maxImages: 6,
            }),
            createSection('product_hero_fashion', 'product-hero', 1, {
                layoutVariant: 'fashion',
                showDescription: true,
                showMediaGallery: false,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: false,
                showStockStatus: true,
            }),
            createSection('product_specs_fashion', 'product-specs', 2, {
                layoutVariant: 'fashion',
                title: 'Style Notes',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_fashion', 'customer-reviews', 3, {
                layoutVariant: 'fashion',
                title: 'Style Reviews',
                reviews: [],
            }),
            createSection('related_products_fashion', 'related-products', 4, {
                layoutVariant: 'fashion',
                title: 'Complete the Look',
                limit: 4,
                showPrice: true,
                showRating: false,
            }),
        ],
    },
    pearl: {
        templateId: 'pearl',
        name: 'Pearl Purchase Template',
        sections: [
            createSection('product_hero_pearl', 'product-hero', 0, {
                layoutVariant: 'pearl',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_pearl', 'product-specs', 1, {
                layoutVariant: 'pearl',
                title: 'Craftsmanship',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_pearl', 'customer-reviews', 2, {
                layoutVariant: 'pearl',
                title: 'Client Impressions',
                reviews: [],
            }),
            createSection('related_products_pearl', 'related-products', 3, {
                layoutVariant: 'pearl',
                title: 'Boutique Collection',
                limit: 4,
                showPrice: true,
                showRating: false,
            }),
        ],
    },
    phoenix: {
        templateId: 'phoenix',
        name: 'Phoenix Sports Purchase Template',
        sections: [
            createSection('product_hero_phoenix', 'product-hero', 0, {
                layoutVariant: 'phoenix',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_phoenix', 'product-specs', 1, {
                layoutVariant: 'phoenix',
                title: 'Performance Details',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_phoenix', 'customer-reviews', 2, {
                layoutVariant: 'phoenix',
                title: 'Athlete Notes',
                reviews: [],
            }),
            createSection('related_products_phoenix', 'related-products', 3, {
                layoutVariant: 'phoenix',
                title: 'Training Rotation',
                limit: 4,
                showPrice: true,
                showRating: false,
            }),
        ],
    },
    hype: {
        templateId: 'hype',
        name: 'Hype Electronics Purchase Template',
        sections: [
            createSection('product_hero_hype', 'product-hero', 0, {
                layoutVariant: 'hype',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_hype', 'product-specs', 1, {
                layoutVariant: 'hype',
                title: 'Tech Specifications',
                useDescription: true,
                items: [],
            }),
            createSection('product_video_hype', 'product-video', 2, {
                title: 'Watch It In Action',
                videoUrl: '',
                autoplay: false,
                showControls: true,
            }),
            createSection('customer_reviews_hype', 'customer-reviews', 3, {
                layoutVariant: 'hype',
                title: 'Verified Hype Feedback',
                reviews: [],
            }),
            createSection('related_products_hype', 'related-products', 4, {
                layoutVariant: 'hype',
                title: 'Compatible Drops',
                limit: 4,
                showPrice: true,
                showRating: true,
            }),
        ],
    },
    alizon: {
        templateId: 'alizon',
        name: 'Alizon Superstore Template',
        sections: [
            createSection('product_hero_alizon', 'product-hero', 0, {
                layoutVariant: 'alizon',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_alizon', 'product-specs', 1, {
                layoutVariant: 'alizon',
                title: 'Product Details',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_alizon', 'customer-reviews', 2, {
                layoutVariant: 'alizon',
                title: 'Customer Reviews',
                reviews: [],
            }),
            createSection('related_products_alizon', 'related-products', 3, {
                layoutVariant: 'alizon',
                title: 'More Like This',
                limit: 4,
                showPrice: true,
                showRating: true,
            }),
        ],
    },
    cosmetics: {
        templateId: 'cosmetics',
        name: 'Cosmetics Template',
        sections: [
            createSection('product_hero_cosmetics', 'product-hero', 0, {
                layoutVariant: 'cosmetics',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: false,
                showAddToCart: true,
                showSku: false,
                showStockStatus: true,
            }),
            createSection('product_specs_cosmetics', 'product-specs', 1, {
                layoutVariant: 'cosmetics',
                title: 'Ingredients & Benefits',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_cosmetics', 'customer-reviews', 2, {
                layoutVariant: 'cosmetics',
                title: 'What Customers Say',
                reviews: [],
            }),
            createSection('related_products_cosmetics', 'related-products', 3, {
                layoutVariant: 'cosmetics',
                title: 'More To Explore',
                limit: 4,
                showPrice: true,
                showRating: true,
            }),
        ],
    },
    sharp: {
        templateId: 'sharp',
        name: 'Sharp Jewelry Template',
        sections: [
            createSection('product_hero_sharp', 'product-hero', 0, {
                layoutVariant: 'sharp',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_sharp', 'product-specs', 1, {
                layoutVariant: 'sharp',
                title: 'Details',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_sharp', 'customer-reviews', 2, {
                layoutVariant: 'sharp',
                title: 'Customer Reviews',
                reviews: [],
            }),
            createSection('related_products_sharp', 'related-products', 3, {
                layoutVariant: 'sharp',
                title: 'Complete the Look',
                limit: 4,
                showPrice: true,
                showRating: false,
            }),
        ],
    },
    seven: {
        templateId: 'seven',
        name: 'Seven Womenwear Purchase Template',
        sections: [
            createSection('product_hero_seven', 'product-hero', 0, {
                layoutVariant: 'fashion',
                showDescription: true,
                showMediaGallery: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            }),
            createSection('product_specs_seven', 'product-specs', 1, {
                layoutVariant: 'fashion',
                title: 'Details',
                useDescription: true,
                items: [],
            }),
            createSection('customer_reviews_seven', 'customer-reviews', 2, {
                layoutVariant: 'fashion',
                title: 'Customer Reviews',
                reviews: [],
            }),
            createSection('related_products_seven', 'related-products', 3, {
                layoutVariant: 'fashion',
                title: 'Complete the Look',
                limit: 4,
                showPrice: true,
                showRating: false,
            }),
        ],
    },
};

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
    nameAr: 'منتج تجريبي',
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

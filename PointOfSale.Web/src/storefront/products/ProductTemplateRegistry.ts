import type { StorefrontProductTemplate, StorefrontSection } from '@/types/storeConfig'
import sectionAuthority from '@theme-authority/shopixy-theme-authority.json'

const getSectionDefaultSettings = (sectionType: string): Record<string, unknown> => {
  const contract = sectionAuthority.sectionContracts.find((candidate) => candidate.type === sectionType)
  if (!contract) {
    throw new Error(`[ProductTemplateRegistry] Missing canonical section contract for "${sectionType}".`)
  }

  return JSON.parse(JSON.stringify(contract.defaultSettings))
}

const createSection = (
  id: string,
  type: string,
  order: number,
  settings: Record<string, unknown> = {},
): StorefrontSection => ({
  id,
  type,
  enabled: true,
  order,
  settings: {
    ...getSectionDefaultSettings(type),
    ...settings,
  },
})

const BUILTIN_PRODUCT_TEMPLATES: Record<string, StorefrontProductTemplate> = {
  default: {
    templateId: 'default',
    name: 'Default Template',
    sections: [
      createSection('product_hero_default', 'product-hero', 0, { layoutVariant: 'default', showDescription: true, showMediaGallery: true, showVariantSelector: true, showAddToCart: true, showSku: true, showStockStatus: true }),
      createSection('product_specs_default', 'product-specs', 1, { layoutVariant: 'default', title: 'Product Details', useDescription: true, items: [] }),
      createSection('related_products_default', 'related-products', 2, { title: 'You May Also Like', limit: 4, showPrice: true, showRating: true }),
    ],
  },
  electronics: {
    templateId: 'electronics',
    name: 'Electronics Template',
    sections: [
      createSection('product_hero_electronics', 'product-hero', 0, { layoutVariant: 'electronics', showDescription: true, showMediaGallery: true, showVariantSelector: true, showAddToCart: true, showSku: true, showStockStatus: true }),
      createSection('product_specs_electronics', 'product-specs', 1, { layoutVariant: 'electronics', title: 'Technical Specifications', useDescription: true, items: [] }),
      createSection('product_video_electronics', 'product-video', 2, { title: 'Watch It In Action', videoUrl: '', autoplay: false, showControls: true }),
      createSection('customer_reviews_electronics', 'customer-reviews', 3, { title: 'Customer Reviews', reviews: [] }),
      createSection('related_products_electronics', 'related-products', 4, { title: 'Compare Similar Products', limit: 4, showPrice: true, showRating: true }),
    ],
  },
  fashion: {
    templateId: 'fashion',
    name: 'Fashion Template',
    sections: [
      createSection('product_gallery_fashion', 'product-gallery', 0, { title: 'Gallery', showThumbnails: true, maxImages: 6 }),
      createSection('product_hero_fashion', 'product-hero', 1, { layoutVariant: 'fashion', showDescription: true, showMediaGallery: false, showVariantSelector: true, showAddToCart: true, showSku: false, showStockStatus: true }),
      createSection('product_specs_fashion', 'product-specs', 2, { layoutVariant: 'fashion', title: 'Style Notes', useDescription: true, items: [] }),
      createSection('related_products_fashion', 'related-products', 3, { title: 'Complete The Look', limit: 4, showPrice: true, showRating: false }),
    ],
  },
  cosmetics: {
    templateId: 'cosmetics',
    name: 'Cosmetics Template',
    sections: [
      createSection('product_hero_cosmetics', 'product-hero', 0, { layoutVariant: 'cosmetics', showDescription: true, showMediaGallery: true, showVariantSelector: false, showAddToCart: true, showSku: false, showStockStatus: true }),
      createSection('product_specs_cosmetics', 'product-specs', 1, { layoutVariant: 'cosmetics', title: 'Ingredients & Benefits', useDescription: true, items: [] }),
      createSection('customer_reviews_cosmetics', 'customer-reviews', 2, { title: 'What Customers Say', reviews: [] }),
      createSection('related_products_cosmetics', 'related-products', 3, { title: 'More To Explore', limit: 4, showPrice: true, showRating: true }),
    ],
  },
}

export const getBuiltinProductTemplates = (): StorefrontProductTemplate[] =>
  Object.values(BUILTIN_PRODUCT_TEMPLATES).map((template) => ({
    ...template,
    sections: template.sections.map((section) => ({ ...section, settings: { ...section.settings } })),
  }))

export const getBuiltinProductTemplate = (templateId?: string | null): StorefrontProductTemplate | null => {
  const resolvedTemplateId = templateId?.trim() || 'default'
  const template = BUILTIN_PRODUCT_TEMPLATES[resolvedTemplateId]
  if (!template) return null
  return {
    ...template,
    sections: template.sections.map((section) => ({ ...section, settings: { ...section.settings } })),
  }
}

export const getProductTemplateOptions = (
  savedTemplates?: Record<string, StorefrontProductTemplate> | null,
): Array<{ id: string; name: string }> => {
  const options = new Map<string, string>()
  getBuiltinProductTemplates().forEach((template) => options.set(template.templateId, template.name))
  Object.entries(savedTemplates || {}).forEach(([templateId, template]) => options.set(templateId, template?.name || templateId))
  return Array.from(options.entries()).map(([id, name]) => ({ id, name }))
}

export const getProductTemplatePreviewProduct = () => ({
  id: 'preview-product',
  name: 'Preview Product',
  nameAr: 'منتج تجريبي',
  description: 'A sample product used to preview product templates inside the builder.',
  sku: 'PREVIEW-001',
  barcode: '1234567890123',
  price: 1299,
  effectivePrice: 1299,
  pricing: { currentPrice: 1299, compareAtPrice: null, discountedPrice: null, discountBadge: null },
  categoryName: 'Preview Category',
  categoryId: 'preview-category',
  slug: 'preview-product',
  productTemplate: 'default',
  media: [],
  images: [],
  isAvailable: true,
  inStock: true,
  stockQuantity: 24,
  createdAtUtc: new Date().toISOString(),
  variants: [],
  hasVariants: false,
})

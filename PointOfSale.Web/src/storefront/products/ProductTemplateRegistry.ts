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
      createSection('section_runtime_product_hero_default', 'product-hero', 0, { layoutVariant: 'default' }),
      createSection('product_specs_default', 'product-specs', 1, { layoutVariant: 'default', title: 'Product Details' }),
      createSection('related_products_default', 'related-products', 2, { title: 'You May Also Like', limit: 4 }),
    ],
  },
  electronics: {
    templateId: 'electronics',
    name: 'Electronics Template',
    sections: [
      createSection('section_runtime_product_hero_electronics', 'product-hero', 0, { layoutVariant: 'electronics' }),
      createSection('product_specs_electronics', 'product-specs', 1, { layoutVariant: 'electronics', title: 'Technical Specifications' }),
      createSection('product_video_electronics', 'product-video', 2, { title: 'Watch It In Action' }),
      createSection('testimonials_electronics', 'testimonials', 3, { title: 'Customer Testimonials', testimonials: [] }),
      createSection('related_products_electronics', 'related-products', 4, { title: 'Compare Similar Products', limit: 4 }),
    ],
  },
  fashion: {
    templateId: 'fashion',
    name: 'Fashion Template',
    sections: [
      createSection('section_runtime_product_hero_fashion', 'product-hero', 0, { layoutVariant: 'fashion' }),
      createSection('product_gallery_fashion', 'product-gallery', 1, { layoutVariant: 'fashion', title: 'Gallery' }),
      createSection('product_specs_fashion', 'product-specs', 2, { layoutVariant: 'fashion', title: 'Style Notes' }),
      createSection('related_products_fashion', 'related-products', 3, { layoutVariant: 'fashion', title: 'Complete The Look', limit: 4 }),
    ],
  },
  cosmetics: {
    templateId: 'cosmetics',
    name: 'Cosmetics Template',
    sections: [
      createSection('section_runtime_product_hero_cosmetics', 'product-hero', 0, { layoutVariant: 'cosmetics' }),
      createSection('product_specs_cosmetics', 'product-specs', 1, { layoutVariant: 'cosmetics', title: 'Ingredients & Benefits' }),
      createSection('testimonials_cosmetics', 'testimonials', 2, { title: 'What Customers Say', testimonials: [] }),
      createSection('related_products_cosmetics', 'related-products', 3, { layoutVariant: 'cosmetics', title: 'More To Explore', limit: 4 }),
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

export const getSupplementalProductTemplateSections = (sections?: StorefrontSection[] | null): StorefrontSection[] =>
  (sections || [])
    .filter((section) => section.type !== 'product-hero')
    .map((section, order) => ({ ...section, order, settings: { ...section.settings } }))

export const createMandatoryProductHeroSection = (
  templateId?: string | null,
  existingSections?: StorefrontSection[] | null,
): StorefrontSection => {
  const acceptedHero = existingSections?.find((section) => section.type === 'product-hero')
  const builtinHero = getBuiltinProductTemplate(templateId)?.sections.find((section) => section.type === 'product-hero')
    || getBuiltinProductTemplate('default')?.sections.find((section) => section.type === 'product-hero')

  return {
    id: acceptedHero?.id || `section_runtime_product_hero_${(templateId || 'default').replace(/[^A-Za-z0-9_-]/g, '_').slice(0, 48)}`,
    type: 'product-hero',
    enabled: true,
    order: 0,
    settings: {
      layoutVariant: typeof acceptedHero?.settings?.layoutVariant === 'string'
        ? acceptedHero.settings.layoutVariant
        : typeof builtinHero?.settings?.layoutVariant === 'string'
          ? builtinHero.settings.layoutVariant
        : 'default',
    },
  }
}

export const composeProductTemplateSections = (
  supplementalSections: StorefrontSection[],
  templateId?: string | null,
  existingSections?: StorefrontSection[] | null,
): StorefrontSection[] => [
  createMandatoryProductHeroSection(templateId, existingSections),
  ...getSupplementalProductTemplateSections(supplementalSections).map((section, index) => ({
    ...section,
    order: index + 1,
  })),
]

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

import {
    Grid3X3,
    Image,
    LayoutGrid,
    Mail,
    MessageCircle,
    PlayCircle,
    ShoppingBag,
    Star,
    Type,
} from 'lucide-react'

import { getContractDefaultSettings, getContractSettingsSchema, getSectionContract } from './sectionContractRegistry'

const BUILTIN_SECTION_DEFINITIONS = [
    {
        type: 'hero-banner',
        displayName: { en: 'Hero Banner', ar: 'البانر الرئيسي' },
        description: { en: 'Large hero section with background and CTA', ar: 'قسم رئيسي مع خلفية وزر' },
        icon: LayoutGrid,
        category: 'layout',
    },
    {
        type: 'product-grid',
        displayName: { en: 'Product Grid', ar: 'شبكة المنتجات' },
        description: { en: 'Display products in a grid or carousel', ar: 'عرض المنتجات في شبكة أو شريط' },
        icon: ShoppingBag,
        category: 'products',
    },
    {
        type: 'category-grid',
        displayName: { en: 'Category Grid', ar: 'شبكة التصنيفات' },
        description: { en: 'Display categories in a grid', ar: 'عرض التصنيفات في شبكة' },
        icon: Grid3X3,
        category: 'products',
    },
    {
        type: 'subcategory-grid',
        displayName: { en: 'Subcategory Grid', ar: 'شبكة التصنيفات الفرعية' },
        description: { en: 'Display subcategories in a grid', ar: 'عرض التصنيفات الفرعية في شبكة' },
        icon: Grid3X3,
        category: 'products',
    },
    {
        type: 'text-block',
        displayName: { en: 'Text Block', ar: 'كتلة نصية' },
        description: { en: 'Authored editorial content', ar: 'محتوى تحريري' },
        icon: Type,
        category: 'content',
    },
    {
        type: 'image-block',
        displayName: { en: 'Image Block', ar: 'كتلة صورة' },
        description: { en: 'Authored image content', ar: 'محتوى صورة' },
        icon: Image,
        category: 'content',
    },
    {
        type: 'video-block',
        displayName: { en: 'Video Block', ar: 'كتلة فيديو' },
        description: { en: 'Authored marketing video', ar: 'فيديو تسويقي' },
        icon: PlayCircle,
        category: 'content',
    },
    {
        type: 'newsletter-signup',
        displayName: { en: 'Newsletter Signup', ar: 'الاشتراك بالنشرة' },
        description: { en: 'Newsletter presentation section', ar: 'قسم عرض الاشتراك بالنشرة' },
        icon: Mail,
        category: 'marketing',
    },
    {
        type: 'testimonials',
        displayName: { en: 'Testimonials', ar: 'شهادات العملاء' },
        description: { en: 'Authored marketing quotes', ar: 'اقتباسات تسويقية' },
        icon: MessageCircle,
        category: 'social',
    },
    {
        type: 'featured-products',
        displayName: { en: 'Featured Products', ar: 'المنتجات المميزة' },
        description: { en: 'Highlighted product showcase', ar: 'عرض المنتجات المميزة' },
        icon: Star,
        category: 'products',
    },
    {
        type: 'product-hero',
        displayName: { en: 'Product Hero', ar: 'واجهة المنتج' },
        description: { en: 'Mandatory Product Details runtime', ar: 'قسم تفاصيل المنتج الإلزامي' },
        icon: ShoppingBag,
        category: 'runtime',
        runtimeOwned: true,
    },
    {
        type: 'product-gallery',
        displayName: { en: 'Product Gallery', ar: 'معرض المنتج' },
        description: { en: 'Supplemental product media placement', ar: 'موضع إضافي لوسائط المنتج' },
        icon: Image,
        category: 'products',
    },
    {
        type: 'product-specs',
        displayName: { en: 'Product Details', ar: 'تفاصيل المنتج' },
        description: { en: 'Supplemental product detail placement', ar: 'موضع إضافي لتفاصيل المنتج' },
        icon: LayoutGrid,
        category: 'products',
    },
    {
        type: 'product-video',
        displayName: { en: 'Product Video', ar: 'فيديو المنتج' },
        description: { en: 'Runtime-owned product video presentation', ar: 'عرض فيديو المنتج من وقت التشغيل' },
        icon: PlayCircle,
        category: 'products',
    },
    {
        type: 'related-products',
        displayName: { en: 'Related Products', ar: 'منتجات ذات صلة' },
        description: { en: 'Runtime-loaded related products', ar: 'منتجات ذات صلة يحمّلها وقت التشغيل' },
        icon: Star,
        category: 'products',
    },
]

class SectionRegistryClass {
    constructor() {
        this.sections = new Map()
        BUILTIN_SECTION_DEFINITIONS.forEach((definition) => this.register(definition))
    }

    register(definition) {
        const contract = getSectionContract(definition.type)
        if (!contract) {
            throw new Error(`[SectionRegistry] Missing canonical section contract for "${definition.type}".`)
        }

        this.sections.set(definition.type, {
            ...definition,
            defaultSettings: getContractDefaultSettings(definition.type),
            settingsSchema: getContractSettingsSchema(definition.type),
        })
    }

    get(type) {
        return this.sections.get(type)
    }

    getAll() {
        return Array.from(this.sections.values())
    }

    getByCategory(category) {
        return this.getAll().filter((section) => section.category === category)
    }

    getCategories() {
        return Array.from(new Set(this.getAll().map((section) => section.category)))
    }

    has(type) {
        return this.sections.has(type)
    }
}

export const sectionRegistry = new SectionRegistryClass()
export default sectionRegistry

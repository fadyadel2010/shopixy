/**
 * Section Registry
 * Phase 9.8: POS Integration - Unified Admin Experience
 *
 * Central registry for all section types with extensibility support.
 */

import {
    LayoutGrid,
    ShoppingBag,
    Grid3X3,
    Type,
    Image,
    PlayCircle,
    Mail,
    MessageCircle,
    Star,
} from 'lucide-react'

import { HeroBannerEditor, HeroBannerPreview } from '../components/sections/HeroBanner'
import { ProductGridEditor, ProductGridPreview } from '../components/sections/ProductGrid'
import { CategoryGridEditor, CategoryGridPreview } from '../components/sections/CategoryGrid'
import { SubcategoryGridEditor, SubcategoryGridPreview } from '../components/sections/SubcategoryGrid'
import { TextBlockEditor, TextBlockPreview } from '../components/sections/TextBlock'
import { ImageBlockEditor, ImageBlockPreview } from '../components/sections/ImageBlock'
import { VideoBlockEditor, VideoBlockPreview } from '../components/sections/VideoBlock'
import { NewsletterSignupEditor, NewsletterSignupPreview } from '../components/sections/NewsletterSignup'
import { TestimonialsEditor, TestimonialsPreview } from '../components/sections/Testimonials'
import { FeaturedProductsEditor, FeaturedProductsPreview } from '../components/sections/FeaturedProducts'
import { getContractDefaultSettings, getContractSettingsSchema, getSectionContract } from './sectionContractRegistry'

class SectionRegistryClass {
    constructor() {
        this.sections = new Map()
        this._registerBuiltInSections()
    }

    _registerBuiltInSections() {
        this.register({
            type: 'hero-banner',
            displayName: { en: 'Hero Banner', ar: 'البانر الرئيسي' },
            description: { en: 'Large hero section with background and CTA', ar: 'قسم رئيسي كبير مع خلفية وزر' },
            icon: LayoutGrid,
            category: 'layout',
            editor: HeroBannerEditor,
            rendererPreview: HeroBannerPreview,
        })

        this.register({
            type: 'product-grid',
            displayName: { en: 'Product Grid', ar: 'شبكة المنتجات' },
            description: { en: 'Display products in a grid layout', ar: 'عرض المنتجات في شكل شبكة' },
            icon: ShoppingBag,
            category: 'products',
            editor: ProductGridEditor,
            rendererPreview: ProductGridPreview,
        })

        this.register({
            type: 'category-grid',
            displayName: { en: 'Category Grid', ar: 'شبكة التصنيفات' },
            description: { en: 'Display categories in a grid', ar: 'عرض التصنيفات في شبكة' },
            icon: Grid3X3,
            category: 'products',
            editor: CategoryGridEditor,
            rendererPreview: CategoryGridPreview,
        })

        this.register({
            type: 'subcategory-grid',
            displayName: { en: 'Subcategory Grid', ar: 'شبكة الأقسام الفرعية' },
            description: { en: 'Display subcategories in a grid', ar: 'عرض الأقسام الفرعية في شبكة' },
            icon: Grid3X3,
            category: 'products',
            editor: SubcategoryGridEditor,
            rendererPreview: SubcategoryGridPreview,
        })

        this.register({
            type: 'text-block',
            displayName: { en: 'Text Block', ar: 'كتلة نصية' },
            description: { en: 'Rich text content section', ar: 'قسم محتوى نصي' },
            icon: Type,
            category: 'content',
            editor: TextBlockEditor,
            rendererPreview: TextBlockPreview,
        })

        this.register({
            type: 'image-block',
            displayName: { en: 'Image Block', ar: 'كتلة صورة' },
            description: { en: 'Single or multiple images', ar: 'صورة واحدة أو أكثر' },
            icon: Image,
            category: 'content',
            editor: ImageBlockEditor,
            rendererPreview: ImageBlockPreview,
        })

        this.register({
            type: 'video-block',
            displayName: { en: 'Video Block', ar: 'كتلة فيديو' },
            description: { en: 'Embed a video or YouTube content', ar: 'تضمين فيديو أو محتوى يوتيوب' },
            icon: PlayCircle,
            category: 'content',
            editor: VideoBlockEditor,
            rendererPreview: VideoBlockPreview,
        })

        this.register({
            type: 'newsletter-signup',
            displayName: { en: 'Newsletter Signup', ar: 'الاشتراك بالنشرة' },
            description: { en: 'Email subscription form', ar: 'نموذج اشتراك بالبريد الإلكتروني' },
            icon: Mail,
            category: 'marketing',
            editor: NewsletterSignupEditor,
            rendererPreview: NewsletterSignupPreview,
        })

        this.register({
            type: 'testimonials',
            displayName: { en: 'Testimonials', ar: 'شهادات العملاء' },
            description: { en: 'Customer testimonials section', ar: 'قسم شهادات العملاء' },
            icon: MessageCircle,
            category: 'social',
            editor: TestimonialsEditor,
            rendererPreview: TestimonialsPreview,
        })

        this.register({
            type: 'featured-products',
            displayName: { en: 'Featured Products', ar: 'المنتجات المميزة' },
            description: { en: 'Highlighted product showcase', ar: 'عرض المنتجات المميزة' },
            icon: Star,
            category: 'products',
            editor: FeaturedProductsEditor,
            rendererPreview: FeaturedProductsPreview,
        })

        this.register({
            type: 'product-hero',
            displayName: { en: 'Product Hero', ar: 'واجهة المنتج' },
            description: { en: 'Primary product details with price and add to cart', ar: 'تفاصيل المنتج الأساسية مع السعر والإضافة للسلة' },
            icon: ShoppingBag,
            category: 'products',
        })

        this.register({
            type: 'product-gallery',
            displayName: { en: 'Product Gallery', ar: 'معرض المنتج' },
            description: { en: 'Large image gallery for product media', ar: 'معرض صور كبير للمنتج' },
            icon: Image,
            category: 'products',
        })

        this.register({
            type: 'product-specs',
            displayName: { en: 'Product Specs', ar: 'مواصفات المنتج' },
            description: { en: 'Structured details and specification table', ar: 'تفاصيل منظمة وجدول مواصفات' },
            icon: LayoutGrid,
            category: 'products',
        })

        this.register({
            type: 'product-video',
            displayName: { en: 'Product Video', ar: 'فيديو المنتج' },
            description: { en: 'Video spotlight for the current product', ar: 'فيديو مميز للمنتج الحالي' },
            icon: PlayCircle,
            category: 'products',
        })

        this.register({
            type: 'customer-reviews',
            displayName: { en: 'Customer Reviews', ar: 'مراجعات العملاء' },
            description: { en: 'Editable review cards for product pages', ar: 'بطاقات مراجعات قابلة للتعديل لصفحات المنتج' },
            icon: MessageCircle,
            category: 'social',
        })

        this.register({
            type: 'related-products',
            displayName: { en: 'Related Products', ar: 'منتجات ذات صلة' },
            description: { en: 'Suggested products for the current product page', ar: 'منتجات مقترحة لصفحة المنتج الحالية' },
            icon: Star,
            category: 'products',
        })
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
        const categories = new Set(this.getAll().map((section) => section.category))
        return Array.from(categories)
    }

    has(type) {
        return this.sections.has(type)
    }
}

export const sectionRegistry = new SectionRegistryClass()
export default sectionRegistry

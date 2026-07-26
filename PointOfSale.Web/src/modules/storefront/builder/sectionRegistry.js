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

import { HeroBannerEditor, HeroBannerPreview, settingsSchema as heroBannerSettingsSchema } from '../components/sections/HeroBanner'
import { ProductGridEditor, ProductGridPreview, settingsSchema as productGridSettingsSchema } from '../components/sections/ProductGrid'
import { CategoryGridEditor, CategoryGridPreview, settingsSchema as categoryGridSettingsSchema } from '../components/sections/CategoryGrid'
import { SubcategoryGridEditor, SubcategoryGridPreview, settingsSchema as subcategoryGridSettingsSchema } from '../components/sections/SubcategoryGrid'
import { TextBlockEditor, TextBlockPreview, settingsSchema as textBlockSettingsSchema } from '../components/sections/TextBlock'
import { ImageBlockEditor, ImageBlockPreview, settingsSchema as imageBlockSettingsSchema } from '../components/sections/ImageBlock'
import { VideoBlockEditor, VideoBlockPreview, settingsSchema as videoBlockSettingsSchema } from '../components/sections/VideoBlock'
import { NewsletterSignupEditor, NewsletterSignupPreview, settingsSchema as newsletterSignupSettingsSchema } from '../components/sections/NewsletterSignup'
import { TestimonialsEditor, TestimonialsPreview, settingsSchema as testimonialsSettingsSchema } from '../components/sections/Testimonials'
import { FeaturedProductsEditor, FeaturedProductsPreview, settingsSchema as featuredProductsSettingsSchema } from '../components/sections/FeaturedProducts'

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
            defaultSettings: {
                title: 'Welcome to Our Store',
                subtitle: 'Discover amazing products at great prices',
                ctaText: 'Shop Now',
                ctaLink: '/products',
                backgroundImage: '',
                backgroundColor: '#1e40af',
                overlayOpacity: 45,
                textColor: '#ffffff',
                alignment: 'center',
                height: 'medium',
            },
            settingsSchema: heroBannerSettingsSchema,
            editor: HeroBannerEditor,
            rendererPreview: HeroBannerPreview,
        })

        this.register({
            type: 'product-grid',
            displayName: { en: 'Product Grid', ar: 'شبكة المنتجات' },
            description: { en: 'Display products in a grid layout', ar: 'عرض المنتجات في شكل شبكة' },
            icon: ShoppingBag,
            category: 'products',
            defaultSettings: {
                title: 'Featured Products',
                productSource: 'featured',
                categoryId: null,
                productIds: [],
                columns: 4,
                rows: 2,
                showPrice: true,
                showRating: true,
                showAddToCart: true,
            },
            settingsSchema: productGridSettingsSchema,
            editor: ProductGridEditor,
            rendererPreview: ProductGridPreview,
        })

        this.register({
            type: 'category-grid',
            displayName: { en: 'Category Grid', ar: 'شبكة التصنيفات' },
            description: { en: 'Display categories in a grid', ar: 'عرض التصنيفات في شبكة' },
            icon: Grid3X3,
            category: 'products',
            defaultSettings: {
                title: 'Shop by Category',
                columns: 3,
                categoryIds: [],
                showProductCount: true,
                cardStyle: 'overlay',
            },
            settingsSchema: categoryGridSettingsSchema,
            editor: CategoryGridEditor,
            rendererPreview: CategoryGridPreview,
        })

        this.register({
            type: 'subcategory-grid',
            displayName: { en: 'Subcategory Grid', ar: 'شبكة الأقسام الفرعية' },
            description: { en: 'Display subcategories in a grid', ar: 'عرض الأقسام الفرعية في شبكة' },
            icon: Grid3X3,
            category: 'products',
            defaultSettings: {
                title: 'Subcategories',
                columns: 3,
                showProductCount: true,
                cardStyle: 'overlay',
            },
            settingsSchema: subcategoryGridSettingsSchema,
            editor: SubcategoryGridEditor,
            rendererPreview: SubcategoryGridPreview,
        })

        this.register({
            type: 'text-block',
            displayName: { en: 'Text Block', ar: 'كتلة نصية' },
            description: { en: 'Rich text content section', ar: 'قسم محتوى نصي' },
            icon: Type,
            category: 'content',
            defaultSettings: {
                heading: '',
                content: 'Add your content here...',
                alignment: 'left',
                backgroundColor: '#ffffff',
                textColor: '#1f2937',
                maxWidth: 'full',
            },
            settingsSchema: textBlockSettingsSchema,
            editor: TextBlockEditor,
            rendererPreview: TextBlockPreview,
        })

        this.register({
            type: 'image-block',
            displayName: { en: 'Image Block', ar: 'كتلة صورة' },
            description: { en: 'Single or multiple images', ar: 'صورة واحدة أو أكثر' },
            icon: Image,
            category: 'content',
            defaultSettings: {
                images: [],
                layout: 'single',
                aspectRatio: '16:9',
                fullWidth: false,
            },
            settingsSchema: imageBlockSettingsSchema,
            editor: ImageBlockEditor,
            rendererPreview: ImageBlockPreview,
        })

        this.register({
            type: 'video-block',
            displayName: { en: 'Video Block', ar: 'كتلة فيديو' },
            description: { en: 'Embed a video or YouTube content', ar: 'تضمين فيديو أو محتوى يوتيوب' },
            icon: PlayCircle,
            category: 'content',
            defaultSettings: {
                videoUrl: '',
                autoplay: false,
                showControls: true,
            },
            settingsSchema: videoBlockSettingsSchema,
            editor: VideoBlockEditor,
            rendererPreview: VideoBlockPreview,
        })

        this.register({
            type: 'newsletter-signup',
            displayName: { en: 'Newsletter Signup', ar: 'الاشتراك بالنشرة' },
            description: { en: 'Email subscription form', ar: 'نموذج اشتراك بالبريد الإلكتروني' },
            icon: Mail,
            category: 'marketing',
            defaultSettings: {
                heading: 'Stay Updated',
                subheading: 'Subscribe to our newsletter',
                buttonText: 'Subscribe',
                backgroundColor: '#0f172a',
                textColor: '#ffffff',
                showName: false,
            },
            settingsSchema: newsletterSignupSettingsSchema,
            editor: NewsletterSignupEditor,
            rendererPreview: NewsletterSignupPreview,
        })

        this.register({
            type: 'testimonials',
            displayName: { en: 'Testimonials', ar: 'شهادات العملاء' },
            description: { en: 'Customer testimonials section', ar: 'قسم شهادات العملاء' },
            icon: MessageCircle,
            category: 'social',
            defaultSettings: {
                heading: 'What Our Customers Say',
                testimonials: [],
                layout: 'carousel',
                showRating: true,
                autoplay: true,
            },
            settingsSchema: testimonialsSettingsSchema,
            editor: TestimonialsEditor,
            rendererPreview: TestimonialsPreview,
        })

        this.register({
            type: 'featured-products',
            displayName: { en: 'Featured Products', ar: 'المنتجات المميزة' },
            description: { en: 'Highlighted product showcase', ar: 'عرض المنتجات المميزة' },
            icon: Star,
            category: 'products',
            defaultSettings: {
                title: 'Featured Products',
                productIds: [],
                limit: 8,
                showPrice: true,
                showRating: true,
                displayStyle: 'carousel',
                showBadge: true,
                badgeText: 'Featured',
            },
            settingsSchema: featuredProductsSettingsSchema,
            editor: FeaturedProductsEditor,
            rendererPreview: FeaturedProductsPreview,
        })

        this.register({
            type: 'product-hero',
            displayName: { en: 'Product Hero', ar: 'واجهة المنتج' },
            description: { en: 'Primary product details with price and add to cart', ar: 'تفاصيل المنتج الأساسية مع السعر والإضافة للسلة' },
            icon: ShoppingBag,
            category: 'products',
            defaultSettings: {
                showDescription: true,
                showVariantSelector: true,
                showAddToCart: true,
                showSku: true,
                showStockStatus: true,
            },
            settingsSchema: [
                { key: 'showDescription', type: 'toggle', label: { en: 'Show Description', ar: 'إظهار الوصف' } },
                { key: 'showVariantSelector', type: 'toggle', label: { en: 'Show Variant Selector', ar: 'إظهار خيارات المنتج' } },
                { key: 'showAddToCart', type: 'toggle', label: { en: 'Show Add To Cart', ar: 'إظهار زر الإضافة للسلة' } },
                { key: 'showSku', type: 'toggle', label: { en: 'Show SKU', ar: 'إظهار رمز المنتج' } },
                { key: 'showStockStatus', type: 'toggle', label: { en: 'Show Stock Status', ar: 'إظهار حالة المخزون' } },
            ],
        })

        this.register({
            type: 'product-gallery',
            displayName: { en: 'Product Gallery', ar: 'معرض المنتج' },
            description: { en: 'Large image gallery for product media', ar: 'معرض صور كبير للمنتج' },
            icon: Image,
            category: 'products',
            defaultSettings: {
                title: 'Gallery',
                showThumbnails: true,
                maxImages: 6,
            },
            settingsSchema: [
                { key: 'title', type: 'text', label: { en: 'Title', ar: 'العنوان' } },
                { key: 'showThumbnails', type: 'toggle', label: { en: 'Show Thumbnails', ar: 'إظهار الصور المصغرة' } },
                { key: 'maxImages', type: 'number', label: { en: 'Max Images', ar: 'أقصى عدد للصور' }, min: 1, max: 12 },
            ],
        })

        this.register({
            type: 'product-specs',
            displayName: { en: 'Product Specs', ar: 'مواصفات المنتج' },
            description: { en: 'Structured details and specification table', ar: 'تفاصيل منظمة وجدول مواصفات' },
            icon: LayoutGrid,
            category: 'products',
            defaultSettings: {
                title: 'Product Details',
                useDescription: true,
                items: [],
            },
            settingsSchema: [
                { key: 'title', type: 'text', label: { en: 'Title', ar: 'العنوان' } },
                { key: 'useDescription', type: 'toggle', label: { en: 'Use Product Description', ar: 'استخدام وصف المنتج' } },
                {
                    key: 'items',
                    type: 'list',
                    label: { en: 'Specifications', ar: 'المواصفات' },
                    itemLabel: 'Spec',
                    defaultItem: { label: '', value: '' },
                    fields: [
                        { key: 'label', type: 'text', label: { en: 'Label', ar: 'الاسم' } },
                        { key: 'value', type: 'text', label: { en: 'Value', ar: 'القيمة' } },
                    ],
                },
            ],
        })

        this.register({
            type: 'product-video',
            displayName: { en: 'Product Video', ar: 'فيديو المنتج' },
            description: { en: 'Video spotlight for the current product', ar: 'فيديو مميز للمنتج الحالي' },
            icon: PlayCircle,
            category: 'products',
            defaultSettings: {
                title: 'Watch It In Action',
                videoUrl: '',
                autoplay: false,
                showControls: true,
            },
            settingsSchema: [
                { key: 'title', type: 'text', label: { en: 'Title', ar: 'العنوان' } },
                { key: 'videoUrl', type: 'video', label: { en: 'Video URL', ar: 'رابط الفيديو' } },
                { key: 'autoplay', type: 'toggle', label: { en: 'Autoplay', ar: 'تشغيل تلقائي' } },
                { key: 'showControls', type: 'toggle', label: { en: 'Show Controls', ar: 'إظهار أدوات التحكم' } },
            ],
        })

        this.register({
            type: 'customer-reviews',
            displayName: { en: 'Customer Reviews', ar: 'مراجعات العملاء' },
            description: { en: 'Editable review cards for product pages', ar: 'بطاقات مراجعات قابلة للتعديل لصفحات المنتج' },
            icon: MessageCircle,
            category: 'social',
            defaultSettings: {
                title: 'Customer Reviews',
                reviews: [],
            },
            settingsSchema: [
                { key: 'title', type: 'text', label: { en: 'Title', ar: 'العنوان' } },
                {
                    key: 'reviews',
                    type: 'list',
                    label: { en: 'Reviews', ar: 'المراجعات' },
                    itemLabel: 'Review',
                    defaultItem: { name: '', rating: 5, text: '', avatar: '' },
                    fields: [
                        { key: 'name', type: 'text', label: { en: 'Name', ar: 'الاسم' } },
                        { key: 'avatar', type: 'image', label: { en: 'Avatar', ar: 'الصورة الرمزية' } },
                        { key: 'rating', type: 'number', label: { en: 'Rating', ar: 'التقييم' }, min: 1, max: 5 },
                        { key: 'text', type: 'textarea', label: { en: 'Review Text', ar: 'نص المراجعة' }, rows: 3 },
                    ],
                },
            ],
        })

        this.register({
            type: 'related-products',
            displayName: { en: 'Related Products', ar: 'منتجات ذات صلة' },
            description: { en: 'Suggested products for the current product page', ar: 'منتجات مقترحة لصفحة المنتج الحالية' },
            icon: Star,
            category: 'products',
            defaultSettings: {
                title: 'Related Products',
                limit: 4,
                showPrice: true,
                showRating: true,
            },
            settingsSchema: [
                { key: 'title', type: 'text', label: { en: 'Title', ar: 'العنوان' } },
                { key: 'limit', type: 'number', label: { en: 'Limit', ar: 'العدد' }, min: 1, max: 12 },
                { key: 'showPrice', type: 'toggle', label: { en: 'Show Price', ar: 'إظهار السعر' } },
                { key: 'showRating', type: 'toggle', label: { en: 'Show Rating', ar: 'إظهار التقييم' } },
            ],
        })
    }

    register(definition) {
        this.sections.set(definition.type, definition)
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

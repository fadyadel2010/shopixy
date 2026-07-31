import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import ProductMediaManager from '../components/ProductMediaManager'
import ProductVariantsTab from '../components/ProductVariantsTab'
import ProductPixelOverrideTab from '../components/ProductPixelOverrideTab'
import ProductAffiliateSettingsCard from '../components/ProductAffiliateSettingsCard'
import SidebarPanel from './SidebarPanel'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'
import { getProductTemplateOptions } from '@/storefront/products/ProductTemplateRegistry'
import { useAuthStore } from '../store/authStore'

const WorkspaceSection = forwardRef(function WorkspaceSection({ eyebrow, title, description, children, className = '', id, tabIndex }, ref) {
    return (
        <section
            ref={ref}
            id={id}
            tabIndex={tabIndex}
            className={`overflow-hidden rounded-2xl border border-gray-200 bg-white shadow-sm ${className}`}
        >
            <div className="border-b border-gray-100 px-6 py-5">
                {eyebrow && <p className="mb-2 text-xs font-semibold uppercase tracking-[0.18em] text-gray-400">{eyebrow}</p>}
                <h2 className="text-lg font-semibold text-gray-900">{title}</h2>
                {description && <p className="mt-1 text-sm text-gray-500">{description}</p>}
            </div>
            <div className="px-6 py-6">{children}</div>
        </section>
    )
})

function EmbeddedSection({ title, description, children }) {
    return (
        <div className="space-y-4">
            <div>
                <h4 className="text-sm font-semibold text-gray-900">{title}</h4>
                {description && <p className="mt-1 text-xs text-gray-500">{description}</p>}
            </div>
            {children}
        </div>
    )
}

function normalizePublishStatus(value) {
    if (typeof value === 'number' && Number.isFinite(value)) {
        return value
    }

    const normalizedValue = typeof value === 'string' ? value.trim().toLowerCase() : ''

    if (normalizedValue === 'draft') {
        return 0
    }

    if (normalizedValue === 'published') {
        return 1
    }

    if (normalizedValue === 'archived') {
        return 2
    }

    const parsedValue = Number.parseInt(value, 10)
    return Number.isFinite(parsedValue) ? parsedValue : 1
}

function isBlankValue(value) {
    return value == null || `${value}`.trim() === ''
}

function sanitizeSlugValue(value) {
    if (isBlankValue(value)) {
        return ''
    }

    return `${value}`
        .trim()
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-+|-+$/g, '')
}

function normalizeListValues(value) {
    if (Array.isArray(value)) {
        return value
            .map((item) => `${item ?? ''}`.trim())
            .filter(Boolean)
    }

    if (typeof value === 'string') {
        return value
            .split(/[,;\n]/)
            .map((item) => item.trim())
            .filter(Boolean)
    }

    return []
}

function normalizeFaqItems(value) {
    if (!Array.isArray(value)) {
        return []
    }

    return value
        .map((item) => {
            if (typeof item === 'string') {
                return {
                    question: item.trim(),
                    answer: ''
                }
            }

            return {
                question: `${item?.question ?? ''}`.trim(),
                answer: `${item?.answer ?? ''}`.trim()
            }
        })
        .filter((item) => item.question || item.answer)
}

function parseJsonValue(value, fallback) {
    if (value == null || value === '') {
        return fallback
    }

    if (typeof value !== 'string') {
        return value
    }

    try {
        return JSON.parse(value)
    } catch {
        return fallback
    }
}

function parseStructuredListValue(value) {
    return normalizeListValues(parseJsonValue(value, value))
}

function parseStructuredFaqValue(value) {
    return normalizeFaqItems(parseJsonValue(value, value))
}

function parseStructuredAiMetadata(value) {
    return parseJsonValue(value, null)
}

function createProductUnitDraft(overrides = {}) {
    return {
        localId: overrides.localId || `${Date.now()}-${Math.random().toString(36).slice(2)}`,
        productUnitId: overrides.productUnitId || overrides.id || null,
        unitId: overrides.unitId || '',
        unitName: overrides.unitName || '',
        unitSymbol: overrides.unitSymbol || '',
        conversionFactor: overrides.conversionFactor?.toString?.() || '1',
        purchasePrice: overrides.purchasePrice?.toString?.() || '',
        sellingPrice: overrides.sellingPrice?.toString?.() || '',
        barcode: overrides.barcode || '',
        isDefault: Boolean(overrides.isDefault),
        isBaseUnit: Boolean(overrides.isBaseUnit)
    }
}

function normalizeProductUnitDrafts(value) {
    if (!Array.isArray(value)) {
        return []
    }

    return value.map((unit) => createProductUnitDraft({
        localId: unit.id || unit.productUnitId,
        productUnitId: unit.id || unit.productUnitId,
        unitId: unit.unitId,
        unitName: unit.unitName,
        unitSymbol: unit.unitSymbol,
        conversionFactor: unit.conversionFactor,
        purchasePrice: unit.purchasePrice,
        sellingPrice: unit.sellingPrice,
        barcode: unit.barcode,
        isDefault: unit.isDefault,
        isBaseUnit: unit.isBaseUnit
    }))
}

function toPositiveNumber(value) {
    const parsed = Number.parseFloat(value)
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 0
}

function createProductAuthoringIdempotencyKey(productId) {
    const randomPart = globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`
    return `product-authoring:${productId || 'create'}:${randomPart}`
}

function buildProductAuthoringRequestOptions(productId, rowVersion, idempotencyKey) {
    const headers = {
        'Idempotency-Key': idempotencyKey
    }

    if (productId && rowVersion) {
        headers['If-Match'] = `"${rowVersion}"`
    }

    return { headers }
}

function buildGeneratedDescription(generatedProduct) {
    const features = normalizeListValues(generatedProduct?.features)
    const faqItems = normalizeFaqItems(generatedProduct?.faq)
    const sections = []
    const shortDescription = `${generatedProduct?.shortDescription ?? ''}`.trim()
    const description = `${generatedProduct?.description ?? ''}`.trim()

    if (shortDescription && shortDescription !== description) {
        sections.push(shortDescription)
    }

    if (description) {
        sections.push(description)
    }

    if (features.length > 0) {
        sections.push(`Features:\n${features.map((feature) => `- ${feature}`).join('\n')}`)
    }

    if (faqItems.length > 0) {
        sections.push(`FAQ:\n${faqItems.map((item) => {
            if (!item.answer) {
                return `- ${item.question}`
            }

            return `Q: ${item.question}\nA: ${item.answer}`
        }).join('\n\n')}`)
    }

    return sections.join('\n\n').trim()
}

const ProductForm = forwardRef(function ProductForm({
    productId,
    onClose,
    onSave,
    mode = 'modal',
    formId = 'product-form',
    hideActions = false,
    onMetaChange,
    focusSection
}, ref) {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency } = useSettings()
    const userRole = useAuthStore((state) => state.user?.role)
    const canManageAffiliateProducts = userRole === 'Owner' || userRole === 'Admin'
    const [loading, setLoading] = useState(false)
    const variantsTabRef = useRef(null)
    const variantsSectionRef = useRef(null)
    const formDataRef = useRef(null)
    const pendingSaveRef = useRef(null)
    const [categories, setCategories] = useState([])
    const [units, setUnits] = useState([])
    const [generatingBarcode, setGeneratingBarcode] = useState(false)
    const [error, setError] = useState('')
    const [productTemplateOptions, setProductTemplateOptions] = useState(getProductTemplateOptions())
    const [hasVariants, setHasVariants] = useState(false)
    const [defaultVariant, setDefaultVariant] = useState(null)
    const [productUnits, setProductUnits] = useState([])
    const [productVariantsSnapshot, setProductVariantsSnapshot] = useState([])
    const [acceptedRowVersion, setAcceptedRowVersion] = useState(null)
    const [simpleProductData, setSimpleProductData] = useState({
        sku: '',
        barcode: ''
    })
    const [formData, setFormData] = useState({
        name: '',
        nameAr: '',
        categoryId: '',
        description: '',
        descriptionAr: '',
        taxRate: '10',
        priceIncludesTax: false,
        trackStock: true,
        isActive: true,
        slug: '',
        metaTitle: '',
        metaDescription: '',
        metaKeywords: '',
        weight: '',
        length: '',
        width: '',
        height: '',
        weightUnit: 'kg',
        dimensionUnit: 'cm',
        publishStatus: 1,
        isOnlineVisible: true,
        isSearchable: true,
        isFeatured: false,
        lowStockThreshold: '',
        allowBackorder: false,
        brand: '',
        manufacturer: '',
        tags: '',
        videoUrl: '',
        productTemplate: 'default',
        features: [],
        faqs: [],
        aiGenerated: false,
        aiMetadata: null
    })

    const isWorkspace = mode === 'workspace'
    const isCreateMode = !productId
    const isVariantCreateMode = isCreateMode && hasVariants

    useEffect(() => {
        formDataRef.current = formData
    }, [formData])

    useEffect(() => {
        fetchCategories()
        fetchUnits()
        fetchProductTemplates()
        if (productId) {
            fetchProduct()
        }
    }, [productId])

    useEffect(() => {
        if (!onMetaChange) return
        onMetaChange({
            name: formData.name,
            isActive: formData.isActive,
            publishStatus: normalizePublishStatus(formData.publishStatus),
            isOnlineVisible: formData.isOnlineVisible,
            slug: formData.slug,
            hasVariants,
            loading
        })
    }, [
        formData.name,
        formData.isActive,
        formData.publishStatus,
        formData.isOnlineVisible,
        formData.slug,
        hasVariants,
        loading,
        onMetaChange
    ])

    useEffect(() => {
        if (focusSection !== 'variants' || loading || !productId || !hasVariants) {
            return
        }

        const focusTimer = window.setTimeout(() => {
            variantsSectionRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
            variantsSectionRef.current?.focus?.({ preventScroll: true })
        }, 0)

        return () => window.clearTimeout(focusTimer)
    }, [focusSection, hasVariants, loading, productId])

    const hasPendingVariantChanges = () => (
        !!(productId && hasVariants && variantsTabRef.current?.hasPendingChanges?.())
    )

    const requestDiscardPendingChanges = async () => {
        if (!hasPendingVariantChanges()) {
            return true
        }

        return await confirm({
            confirmLabel: t('common.leave') || 'Leave',
            description: t('variants.unsavedLeavePrompt') || 'Unsaved changes detected. Leave anyway?',
            title: t('common.warning') || 'Warning',
            variant: 'warning',
        })
    }

    useImperativeHandle(ref, () => ({
        hasPendingChanges() {
            return hasPendingVariantChanges()
        },
        async confirmDiscardPendingChanges() {
            return await requestDiscardPendingChanges()
        },
        applyAiGeneratedData(generatedProduct) {
            const currentFormData = formDataRef.current

            if (!currentFormData || !generatedProduct) {
                return { appliedFieldCount: 0, skippedFieldCount: 0 }
            }

            const nextFormData = { ...currentFormData }
            const tags = normalizeListValues(generatedProduct.tags).join(', ')
            const metaKeywords = normalizeListValues(generatedProduct.metaKeywords).join(', ')
            const generatedDescription = buildGeneratedDescription(generatedProduct)
            const counters = {
                appliedFieldCount: 0,
                skippedFieldCount: 0
            }

            const applyTextField = (field, value, transform = (input) => input) => {
                if (isBlankValue(value)) {
                    return
                }

                if (isBlankValue(nextFormData[field])) {
                    nextFormData[field] = transform(value)
                    counters.appliedFieldCount += 1
                    return
                }

                counters.skippedFieldCount += 1
            }

            applyTextField('name', generatedProduct.name)
            applyTextField('description', generatedDescription)
            applyTextField('brand', generatedProduct.brand)
            applyTextField('manufacturer', generatedProduct.manufacturer)
            applyTextField('tags', tags)
            applyTextField('slug', generatedProduct.slug, sanitizeSlugValue)
            applyTextField('metaTitle', generatedProduct.metaTitle)
            applyTextField('metaDescription', generatedProduct.metaDescription)
            applyTextField('metaKeywords', metaKeywords)

            nextFormData.features = normalizeListValues(generatedProduct.features)
            nextFormData.faqs = normalizeFaqItems(generatedProduct.faq)
            nextFormData.aiGenerated = true
            nextFormData.aiMetadata = {
                source: 'admin_ai_product_generator',
                generatedAtUtc: new Date().toISOString(),
                shortDescription: `${generatedProduct.shortDescription ?? ''}`.trim() || null,
                metaKeywords: normalizeListValues(generatedProduct.metaKeywords),
                featureCount: normalizeListValues(generatedProduct.features).length,
                faqCount: normalizeFaqItems(generatedProduct.faq).length
            }

            formDataRef.current = nextFormData
            setFormData(nextFormData)

            return counters
        }
    }), [productId, hasVariants, confirm, t])

    const fetchCategories = async () => {
        try {
            const response = await api.get('/categories')
            setCategories(response.data)
        } catch (err) {
            console.error('Failed to load categories', err)
        }
    }

    const fetchUnits = async () => {
        try {
            const response = await api.get('/units')
            setUnits(response.data)
        } catch (err) {
            console.error('Failed to load units', err)
        }
    }

    const fetchProductTemplates = async () => {
        try {
            const response = await api.get('/admin/store-config')
            setProductTemplateOptions(getProductTemplateOptions(response.data?.productTemplates))
        } catch (err) {
            console.error('Failed to load product templates', err)
            setProductTemplateOptions(getProductTemplateOptions())
        }
    }

    const fetchProduct = async () => {
        try {
            setLoading(true)
            const response = await api.get(`/products/${productId}`)
            const product = response.data
            const variantCount = Array.isArray(product.variants) ? product.variants.length : 0
            const derivedHasVariants = !!product.hasVariants || variantCount > 1

            setAcceptedRowVersion(product.rowVersion || product.RowVersion || null)
            setHasVariants(derivedHasVariants)
            setProductUnits(normalizeProductUnitDrafts(product.units))
            setProductVariantsSnapshot(Array.isArray(product.variants) ? product.variants : [])
            setFormData({
                name: product.name,
                nameAr: product.nameAr || '',
                categoryId: product.categoryId || '',
                description: product.description || '',
                descriptionAr: product.descriptionAr || '',
                taxRate: product.taxRate.toString(),
                priceIncludesTax: product.priceIncludesTax,
                trackStock: product.trackStock,
                isActive: product.isActive,
                slug: product.slug || '',
                metaTitle: product.metaTitle || '',
                metaDescription: product.metaDescription || '',
                metaKeywords: product.metaKeywords || '',
                weight: product.weight?.toString() || '',
                length: product.length?.toString() || '',
                width: product.width?.toString() || '',
                height: product.height?.toString() || '',
                weightUnit: product.weightUnit || 'kg',
                dimensionUnit: product.dimensionUnit || 'cm',
                publishStatus: normalizePublishStatus(product.publishStatus),
                isOnlineVisible: product.isOnlineVisible ?? true,
                isSearchable: product.isSearchable ?? true,
                isFeatured: product.isFeatured ?? false,
                lowStockThreshold: product.lowStockThreshold?.toString() || '',
                allowBackorder: product.allowBackorder ?? false,
                brand: product.brand || '',
                manufacturer: product.manufacturer || '',
                tags: product.tags || '',
                videoUrl: product.videoUrl || '',
                productTemplate: product.productTemplate || 'default',
                features: parseStructuredListValue(product.featuresJson ?? product.features),
                faqs: parseStructuredFaqValue(product.faqJson ?? product.faqs),
                aiGenerated: Boolean(product.aiGenerated),
                aiMetadata: parseStructuredAiMetadata(product.aiMetadataJson ?? product.aiMetadata)
            })

            if (!derivedHasVariants) {
                await fetchDefaultVariant()
            }
        } catch (err) {
            setError(t('products.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const fetchDefaultVariant = async () => {
        try {
            const response = await api.get(`/products/${productId}/variants`)
            const variants = response.data

            if (variants.length > 1) {
                setHasVariants(true)
                setDefaultVariant(null)
                return
            }

            const defaultVar = variants.find(v => v.isDefault)

            if (defaultVar) {
                setDefaultVariant(defaultVar)
                setSimpleProductData({
                    sku: defaultVar.sku || '',
                    barcode: defaultVar.barcode || ''
                })
            }
        } catch (err) {
            console.error('Failed to load default variant', err)
        }
    }

    const handleGenerateBarcode = async (type = 'EAN13') => {
        try {
            setGeneratingBarcode(true)
            const response = await api.post(`/products/generate-barcode?type=${type}`)
            setSimpleProductData(prev => ({ ...prev, barcode: response.data.barcode }))
        } catch (err) {
            setError(t('products.failedToGenerateBarcode'))
        } finally {
            setGeneratingBarcode(false)
        }
    }

    const handleModeChange = async (newHasVariants) => {
        if (newHasVariants && !hasVariants && simpleProductData.sku) {
            const confirmed = await confirm({
                confirmLabel: t('common.confirm'),
                description:
                    t('products.switchToVariantModeConfirm')
                    || 'Switching to variant mode will move SKU and stock identity to variants. Continue?',
                title: t('common.warning'),
                variant: 'warning',
            })

            if (!confirmed) {
                return
            }
        }

        setHasVariants(newHasVariants)

        if (newHasVariants && !productId) {
            setFormData(prev => ({
                ...prev,
                publishStatus: 0
            }))
        }
    }

    const getCompleteProductUnitDrafts = () => (
        productUnits.filter((unit) => unit.unitId)
    )

    const buildProductUnitsPayload = () => (
        getCompleteProductUnitDrafts().map((unit) => ({
            productUnitId: unit.productUnitId,
            unitId: unit.unitId,
            conversionFactor: Number.parseFloat(unit.conversionFactor) || 0,
            purchasePrice: unit.purchasePrice === '' ? null : Number.parseFloat(unit.purchasePrice) || 0,
            sellingPrice: unit.sellingPrice === '' ? null : Number.parseFloat(unit.sellingPrice) || 0,
            barcode: unit.barcode || null,
            isDefault: unit.isDefault,
            isBaseUnit: unit.isBaseUnit
        }))
    )

    const buildVariantAuthoringSnapshot = () => {
        if (!hasVariants) {
            return []
        }

        return productVariantsSnapshot.map((variant) => ({
            variantId: variant.id,
            sku: variant.sku || variant.SKU || null,
            barcode: variant.barcode || null,
            price: Number.parseFloat(variant.price) || 0,
            onlinePrice: variant.onlinePrice == null ? null : Number.parseFloat(variant.onlinePrice) || 0,
            costPrice: Number.parseFloat(variant.costPrice) || 0,
            isActive: variant.isActive !== false,
            isDefault: Boolean(variant.isDefault),
            attributes: Array.isArray(variant.attributes)
                ? variant.attributes.reduce((acc, attribute) => {
                    if (attribute.name) acc[attribute.name] = attribute.value || ''
                    return acc
                }, {})
                : {}
        }))
    }

    const handleAddProductUnit = () => {
        setProductUnits((prev) => [
            ...prev,
            createProductUnitDraft({
                isBaseUnit: prev.every((unit) => !unit.isBaseUnit),
                isDefault: prev.every((unit) => !unit.isDefault)
            })
        ])
    }

    const handleProductUnitChange = (localId, field, value) => {
        setProductUnits((prev) => prev.map((unit) => {
            if (unit.localId !== localId) return unit

            if (field === 'unitId') {
                return { ...unit, unitId: value }
            }

            if (field === 'conversionFactor' && unit.isBaseUnit) {
                return { ...unit, conversionFactor: '1' }
            }

            return { ...unit, [field]: value }
        }))
    }

    const handleSetBaseUnit = (unitId) => {
        setProductUnits((prev) => prev.map((unit) => ({
            ...unit,
            isBaseUnit: unit.unitId === unitId,
            conversionFactor: unit.unitId === unitId ? '1' : unit.conversionFactor
        })))
    }

    const handleSetDefaultUnit = (unitId) => {
        setProductUnits((prev) => prev.map((unit) => ({
            ...unit,
            isDefault: unit.unitId === unitId
        })))
    }

    const handleRemoveProductUnit = (localId) => {
        setProductUnits((prev) => prev.filter((unit) => unit.localId !== localId))
    }

    const getAuthoringValidationError = () => {
        const publishStatus = isVariantCreateMode ? 0 : normalizePublishStatus(formData.publishStatus)
        const isDraft = publishStatus === 0
        const isPublic = publishStatus === 1 && formData.isActive && formData.isOnlineVisible

        if (isDraft) {
            return null
        }

        if (!formData.name.trim()) {
            return t('products.productNameRequired') || 'Product name is required'
        }

        if (!formData.categoryId) {
            return t('products.categoryRequired')
        }

        if (!isPublic) {
            return null
        }

        if (hasVariants) {
            const hasActivePricedVariant = buildVariantAuthoringSnapshot().some((variant) => (
                variant.isActive && (toPositiveNumber(variant.onlinePrice) > 0 || toPositiveNumber(variant.price) > 0)
            ))

            return hasActivePricedVariant
                ? null
                : (t('products.activePricedVariantRequired') || 'At least one active priced variant is required before publication.')
        }

        const completeUnits = getCompleteProductUnitDrafts()
        const baseUnit = completeUnits.find((unit) => unit.isBaseUnit)
        const defaultUnit = completeUnits.find((unit) => unit.isDefault)

        if (!baseUnit) {
            return t('products.baseUnitRequired') || 'Base unit is required before publication.'
        }

        if (!defaultUnit) {
            return t('products.defaultUnitRequired') || 'Default unit is required before publication.'
        }

        if (toPositiveNumber(defaultUnit.sellingPrice) <= 0) {
            return t('products.defaultUnitPriceRequired') || 'Default unit price must be greater than zero before publication.'
        }

        if (completeUnits.some((unit) => toPositiveNumber(unit.conversionFactor) <= 0 || (unit.isBaseUnit && Number.parseFloat(unit.conversionFactor) !== 1))) {
            return t('products.productUnitsInvalid') || 'Product units must have valid conversion factors.'
        }

        return null
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        setLoading(true)
        setError('')

        if (!formData.categoryId) {
            setError(t('products.categoryRequired'))
            setLoading(false)
            return
        }

        const authoringValidationError = getAuthoringValidationError()
        if (authoringValidationError) {
            setError(authoringValidationError)
            setLoading(false)
            return
        }

        try {
            if (productId && hasVariants && variantsTabRef.current?.savePendingChanges) {
                const variantsSaved = await variantsTabRef.current.savePendingChanges()
                if (!variantsSaved) {
                    setLoading(false)
                    return
                }
            }

            const productData = {
                ...formData,
                taxRate: parseFloat(formData.taxRate) || 0,
                images: null,
                categoryId: formData.categoryId,
                hasVariants,
                sku: hasVariants ? null : simpleProductData.sku,
                barcode: hasVariants ? null : simpleProductData.barcode,
                weight: formData.weight ? parseFloat(formData.weight) : null,
                length: formData.length ? parseFloat(formData.length) : null,
                width: formData.width ? parseFloat(formData.width) : null,
                height: formData.height ? parseFloat(formData.height) : null,
                lowStockThreshold: formData.lowStockThreshold ? parseInt(formData.lowStockThreshold) : null,
                publishStatus: isVariantCreateMode ? 0 : normalizePublishStatus(formData.publishStatus),
                productTemplate: formData.isOnlineVisible ? (formData.productTemplate || 'default') : null,
                productUnits: isVariantCreateMode ? null : buildProductUnitsPayload(),
                variants: productId && hasVariants ? null : buildVariantAuthoringSnapshot()
            }

            if (import.meta.env.DEV) {
                console.info('[ProductForm] Submitting product payload', {
                    productId,
                    isOnlineVisible: formData.isOnlineVisible,
                    selectedProductTemplate: formData.productTemplate,
                    payloadProductTemplate: productData.productTemplate
                })
            }

            const saveFingerprint = JSON.stringify({
                productId: productId || null,
                rowVersion: productId ? acceptedRowVersion || null : null,
                productData
            })
            const idempotencyKey = pendingSaveRef.current?.fingerprint === saveFingerprint
                ? pendingSaveRef.current.key
                : createProductAuthoringIdempotencyKey(productId)
            pendingSaveRef.current = { fingerprint: saveFingerprint, key: idempotencyKey }

            let savedProduct
            const requestOptions = buildProductAuthoringRequestOptions(productId, acceptedRowVersion, idempotencyKey)

            if (productId) {
                const response = await api.put(`/products/${productId}`, productData, requestOptions)
                savedProduct = response.data || { id: productId }
                setAcceptedRowVersion(savedProduct.rowVersion || savedProduct.RowVersion || acceptedRowVersion)
            } else {
                const response = await api.post('/products', productData, requestOptions)
                savedProduct = response.data
                setAcceptedRowVersion(savedProduct.rowVersion || savedProduct.RowVersion || null)
            }

            invalidatePosRuntimeCache({ reason: 'product-updated' })
            onSave({
                ...savedProduct,
                hasVariants: savedProduct?.hasVariants ?? hasVariants
            })
            pendingSaveRef.current = null
        } catch (err) {
            setError(err.response?.data?.message || t('products.failedToSave'))
        } finally {
            setLoading(false)
        }
    }

    const handleChange = (field, value) => {
        setFormData(prev => ({ ...prev, [field]: value }))
    }

    const handleSimpleChange = (field, value) => {
        setSimpleProductData(prev => ({ ...prev, [field]: value }))
    }

    if (loading && productId) {
        return (
            <div className="flex items-center justify-center py-12">
                <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary-600 border-t-transparent"></div>
            </div>
        )
    }

    const completeProductUnitDrafts = getCompleteProductUnitDrafts()
    const baseUnitId = completeProductUnitDrafts.find((unit) => unit.isBaseUnit)?.unitId || ''
    const defaultUnitId = completeProductUnitDrafts.find((unit) => unit.isDefault)?.unitId || ''
    const defaultUnitDraft = completeProductUnitDrafts.find((unit) => unit.unitId === defaultUnitId)
    const productUnitsValid = completeProductUnitDrafts.length > 0
        && Boolean(baseUnitId)
        && Boolean(defaultUnitId)
        && completeProductUnitDrafts.every((unit) => toPositiveNumber(unit.conversionFactor) > 0)
        && (!completeProductUnitDrafts.find((unit) => unit.isBaseUnit) || Number.parseFloat(completeProductUnitDrafts.find((unit) => unit.isBaseUnit)?.conversionFactor) === 1)
    const simplePublicationReady = !hasVariants
        && Boolean(baseUnitId)
        && Boolean(defaultUnitId)
        && toPositiveNumber(defaultUnitDraft?.sellingPrice) > 0
        && productUnitsValid
    const variantPublicationReady = hasVariants
        && buildVariantAuthoringSnapshot().some((variant) => (
            variant.isActive && (toPositiveNumber(variant.onlinePrice) > 0 || toPositiveNumber(variant.price) > 0)
        ))
    const publicationReady = normalizePublishStatus(formData.publishStatus) === 0
        || !formData.isOnlineVisible
        || (hasVariants ? variantPublicationReady : simplePublicationReady)
    const readinessItems = [
        {
            label: t('products.baseUnitSelected') || 'Base Unit selected',
            ready: Boolean(baseUnitId)
        },
        {
            label: t('products.defaultUnitSelected') || 'Default Unit selected',
            ready: Boolean(defaultUnitId)
        },
        {
            label: t('products.productUnitsValid') || 'Product Units valid',
            ready: productUnitsValid || normalizePublishStatus(formData.publishStatus) === 0
        },
        {
            label: t('products.readyForPublication') || 'Ready for publication',
            ready: publicationReady
        }
    ]
    const selectedUnitIds = new Set(completeProductUnitDrafts.map((unit) => unit.unitId))

    const productTypeSection = (
        <WorkspaceSection
            eyebrow={isWorkspace ? (t('products.basicInfo') || 'Product Setup') : null}
            title={t('products.productType') || 'Product Type'}
            description={t('products.productTypeDescription') || 'Choose how this product is sold.'}
            className="border-purple-100 bg-gradient-to-r from-white to-purple-50"
        >
            <div className="grid gap-3 md:grid-cols-2" role="radiogroup" aria-label={t('products.productType') || 'Product Type'}>
                <label className={`flex cursor-pointer items-start gap-3 rounded-xl border bg-white p-4 transition ${
                    !hasVariants ? 'border-primary-300 ring-2 ring-primary-100' : 'border-gray-200 hover:border-gray-300'
                }`}>
                    <input
                        type="radio"
                        name="product-type"
                        checked={!hasVariants}
                        onChange={() => handleModeChange(false)}
                        className="mt-1 border-primary-300 text-primary-600 focus:ring-primary-500"
                    />
                    <div>
                        <span className="font-medium text-gray-900">
                            {t('products.simpleProduct') || 'Simple Product'}
                        </span>
                        <p className="mt-1 text-sm text-gray-500">
                            {t('products.simpleModeHint') || 'Single product sold through default Product Unit pricing.'}
                        </p>
                    </div>
                </label>

                <label className={`flex cursor-pointer items-start gap-3 rounded-xl border bg-white p-4 transition ${
                    hasVariants ? 'border-purple-300 ring-2 ring-purple-100' : 'border-gray-200 hover:border-gray-300'
                }`}>
                    <input
                        type="radio"
                        name="product-type"
                        checked={hasVariants}
                        onChange={() => handleModeChange(true)}
                        className="mt-1 border-purple-300 text-purple-600 focus:ring-purple-500"
                    />
                    <div>
                        <span className="font-medium text-gray-900">
                            {t('products.variantProduct') || 'Variant Product'}
                        </span>
                        <p className="mt-1 text-sm text-gray-500">
                            {t('products.variantModeHint') || 'Pricing, inventory, SKU and stock are configured after creating product variants.'}
                        </p>
                    </div>
                </label>
            </div>
        </WorkspaceSection>
    )

    const variantCreateGuidanceSection = isVariantCreateMode ? (
        <WorkspaceSection
            eyebrow={isWorkspace ? (t('variants.productVariants') || 'Variants') : null}
            title={t('products.variantProduct') || 'Variant Product'}
            description={t('products.variantCreatePricingDeferred') || 'Pricing, inventory, SKU and stock will be configured after creating product variants.'}
            className="border-blue-100 bg-gradient-to-r from-white to-blue-50"
        >
            <div className="rounded-xl border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-700">
                {t('products.variantCreateDraftNotice') || 'The parent product will be created as a draft, then opened on Variants so you can configure the sellable items.'}
            </div>
        </WorkspaceSection>
    ) : null

    const basicInformationSection = (
        <WorkspaceSection
            eyebrow={isWorkspace ? (t('products.basicInfo') || 'Product Content') : null}
            title={t('products.basicInfo') || 'Basic Information'}
            description={t('products.editProduct') || 'Core product details used across POS and storefront surfaces.'}
        >
            <div className="grid gap-4 md:grid-cols-2">
                <div className="md:col-span-2">
                    <label className="label">{t('products.productName')} *</label>
                    <input
                        type="text"
                        value={formData.name}
                        onChange={(e) => handleChange('name', e.target.value)}
                        className="input"
                        required
                    />
                </div>

                <div className="md:col-span-2">
                    <label className="label">{t('products.category')} *</label>
                    <select
                        value={formData.categoryId}
                        onChange={(e) => handleChange('categoryId', e.target.value)}
                        className="input"
                        required
                    >
                        <option value="">{t('products.selectCategory')}</option>
                        {categories.map(cat => (
                            <option key={cat.id} value={cat.id}>
                                {cat.parentCategoryName ? `${cat.parentCategoryName} > ` : ''}{cat.name}
                            </option>
                        ))}
                    </select>
                </div>

                <div className="md:col-span-2">
                    <label className="label">{t('categories.description')}</label>
                    <textarea
                        value={formData.description}
                        onChange={(e) => handleChange('description', e.target.value)}
                        className="input"
                        rows="4"
                    />
                </div>

                <div>
                    <label className="label">{t('products.taxRate')}</label>
                    <input
                        type="number"
                        step="0.01"
                        value={formData.taxRate}
                        onChange={(e) => handleChange('taxRate', e.target.value)}
                        className="input"
                    />
                </div>

                <div className="flex items-center rounded-xl border border-dashed border-gray-200 px-4 py-3">
                    <label className="flex items-center gap-2">
                        <input
                            type="checkbox"
                            checked={formData.priceIncludesTax}
                            onChange={(e) => handleChange('priceIncludesTax', e.target.checked)}
                            className="rounded"
                        />
                        <span className="text-sm text-gray-700">{t('products.priceIncludesTax')}</span>
                    </label>
                </div>
            </div>
        </WorkspaceSection>
    )

    const simpleIdentitySection = !hasVariants ? (
        <WorkspaceSection
            eyebrow={isWorkspace ? (t('products.identity') || 'Identity') : null}
            title={t('products.simpleIdentity') || 'Simple Product Identity'}
            description={t('products.simpleIdentityDescription') || 'SKU and barcode are product identity; prices are authored on Product Units.'}
            className="border-emerald-100 bg-gradient-to-r from-white to-emerald-50"
        >
            <div className="grid gap-4 md:grid-cols-2">
                <div>
                    <label className="label">{t('products.sku') || 'SKU'}</label>
                    <input
                        type="text"
                        value={simpleProductData.sku}
                        onChange={(e) => handleSimpleChange('sku', e.target.value)}
                        className="input"
                        placeholder={t('products.autoGenerated')}
                    />
                </div>

                <div>
                    <label className="label">{t('products.barcode')}</label>
                    <div className="flex gap-2">
                        <input
                            type="text"
                            value={simpleProductData.barcode}
                            onChange={(e) => handleSimpleChange('barcode', e.target.value)}
                            className="input flex-1"
                            placeholder={t('products.scanOrGenerate')}
                        />
                        <button
                            type="button"
                            onClick={() => handleGenerateBarcode('EAN13')}
                            disabled={generatingBarcode}
                            className="btn-secondary whitespace-nowrap"
                        >
                            {generatingBarcode ? t('products.generating') : t('products.generate')}
                        </button>
                    </div>
                </div>
            </div>

            <div className="mt-4 rounded-xl border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-700">
                {t('products.stockManagedViaWarehouses') || 'Stock is managed via Warehouses -> Opening Stock.'}
            </div>
        </WorkspaceSection>
    ) : null

    const productUnitsSection = (
        <WorkspaceSection
            eyebrow={isWorkspace ? (t('products.units') || 'Units') : null}
            title={t('products.productUnits') || 'Product Units'}
            description={t('products.productUnitsDescription') || 'Author base/default units, conversion factors, and canonical unit prices with the product.'}
            className="border-indigo-100 bg-gradient-to-r from-white to-indigo-50"
        >
            <div className="space-y-5">
                <div className="grid gap-4 md:grid-cols-2">
                    <div>
                        <label className="label">{t('products.baseUnit') || 'Base Unit'}</label>
                        <select
                            value={baseUnitId}
                            onChange={(e) => handleSetBaseUnit(e.target.value)}
                            className="input"
                        >
                            <option value="">{t('products.selectBaseUnit') || 'Select base unit'}</option>
                            {completeProductUnitDrafts.map((unit) => {
                                const unitOption = units.find((candidate) => candidate.id === unit.unitId)
                                return (
                                    <option key={unit.localId} value={unit.unitId}>
                                        {unitOption?.name || unit.unitName || unit.unitId}
                                    </option>
                                )
                            })}
                        </select>
                    </div>

                    <div>
                        <label className="label">{t('products.defaultUnit') || 'Default Unit'}</label>
                        <select
                            value={defaultUnitId}
                            onChange={(e) => handleSetDefaultUnit(e.target.value)}
                            className="input"
                        >
                            <option value="">{t('products.selectDefaultUnit') || 'Select default unit'}</option>
                            {completeProductUnitDrafts.map((unit) => {
                                const unitOption = units.find((candidate) => candidate.id === unit.unitId)
                                return (
                                    <option key={unit.localId} value={unit.unitId}>
                                        {unitOption?.name || unit.unitName || unit.unitId}
                                    </option>
                                )
                            })}
                        </select>
                    </div>
                </div>

                <div className="space-y-3">
                    {productUnits.length === 0 ? (
                        <div className="rounded-xl border border-dashed border-gray-300 bg-white px-4 py-5 text-sm text-gray-500">
                            {t('products.noProductUnits') || 'No units configured yet.'}
                        </div>
                    ) : productUnits.map((unit) => (
                        <div key={unit.localId} className="grid gap-3 rounded-xl border border-gray-200 bg-white p-4 md:grid-cols-[minmax(0,1.2fr)_0.7fr_0.8fr_0.8fr_auto]">
                            <div>
                                <label className="label">{t('products.unit') || 'Unit'}</label>
                                <select
                                    value={unit.unitId}
                                    onChange={(e) => handleProductUnitChange(unit.localId, 'unitId', e.target.value)}
                                    className="input"
                                >
                                    <option value="">{t('products.selectUnit') || 'Select unit'}</option>
                                    {units.map((unitOption) => (
                                        <option
                                            key={unitOption.id}
                                            value={unitOption.id}
                                            disabled={selectedUnitIds.has(unitOption.id) && unitOption.id !== unit.unitId}
                                        >
                                            {unitOption.name}{unitOption.symbol ? ` (${unitOption.symbol})` : ''}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            <div>
                                <label className="label">{t('products.conversionFactor') || 'Conversion'}</label>
                                <input
                                    type="number"
                                    min="0.0001"
                                    step="0.0001"
                                    value={unit.conversionFactor}
                                    onChange={(e) => handleProductUnitChange(unit.localId, 'conversionFactor', e.target.value)}
                                    className="input"
                                    disabled={unit.isBaseUnit}
                                />
                            </div>

                            <div>
                                <label className="label">{t('products.purchasePrice') || 'Purchase Price'}</label>
                                <input
                                    type="number"
                                    step="0.01"
                                    value={unit.purchasePrice}
                                    onChange={(e) => handleProductUnitChange(unit.localId, 'purchasePrice', e.target.value)}
                                    className="input"
                                    placeholder="0.00"
                                />
                            </div>

                            <div>
                                <label className="label">{t('products.sellingPrice') || 'Selling Price'}</label>
                                <input
                                    type="number"
                                    step="0.01"
                                    value={unit.sellingPrice}
                                    onChange={(e) => handleProductUnitChange(unit.localId, 'sellingPrice', e.target.value)}
                                    className="input"
                                    placeholder="0.00"
                                />
                            </div>

                            <div className="flex items-end">
                                <button
                                    type="button"
                                    onClick={() => handleRemoveProductUnit(unit.localId)}
                                    className="btn-secondary"
                                >
                                    {t('common.remove') || 'Remove'}
                                </button>
                            </div>
                        </div>
                    ))}
                </div>

                <div className="flex flex-wrap items-center justify-between gap-3">
                    <button
                        type="button"
                        onClick={handleAddProductUnit}
                        className="btn-secondary"
                    >
                        {t('products.addProductUnit') || 'Add Product Unit'}
                    </button>

                    <div className="flex flex-wrap gap-2">
                        {readinessItems.map((item) => (
                            <span
                                key={item.label}
                                className={`rounded-full px-3 py-1 text-xs font-medium ${
                                    item.ready
                                        ? 'bg-emerald-100 text-emerald-700'
                                        : 'bg-amber-100 text-amber-700'
                                }`}
                            >
                                {item.ready ? 'Ready' : 'Needs'}: {item.label}
                            </span>
                        ))}
                    </div>
                </div>
            </div>
        </WorkspaceSection>
    )

    const mediaSection = (
        <WorkspaceSection
            eyebrow={isWorkspace ? 'Media' : null}
            title="Media"
            description={t('products.viewOnStorefront') || 'Manage the media customers and staff see for this product.'}
        >
            {productId ? (
                <ProductMediaManager productId={productId} />
            ) : (
                <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                    Save the product first, then manage all images and videos from the Media Manager.
                </div>
            )}
        </WorkspaceSection>
    )

    const variantsSection = hasVariants ? (
        <WorkspaceSection
            ref={variantsSectionRef}
            id="product-variants"
            tabIndex={-1}
            eyebrow={isWorkspace ? (t('variants.productVariants') || 'Variants') : null}
            title={t('variants.productVariants') || 'Product Variants'}
            description={t('products.variantModeInfo') || 'Manage sellable variants, attributes, and pricing from one table.'}
            className="border-purple-100 bg-gradient-to-r from-white to-purple-50"
        >
            {productId ? (
                <ProductVariantsTab ref={variantsTabRef} productId={productId} forceMatrixMode={hasVariants} />
            ) : (
                <div className="rounded-xl border border-blue-200 bg-blue-50 p-4">
                    <p className="text-sm font-medium text-blue-800">
                        {t('variants.saveProductFirst') || 'Please save the product first to manage variants.'}
                    </p>
                    <p className="mt-1 text-sm text-blue-600">
                        {t('variants.autoCreateNote') || 'A default variant will be created automatically when you save the product.'}
                    </p>
                </div>
            )}
        </WorkspaceSection>
    ) : null

    const inventorySection = (
        <WorkspaceSection
            eyebrow={isWorkspace ? (t('products.inventoryNotice') || 'Inventory') : null}
            title={t('products.inventoryNotice') || 'Stock & Inventory'}
            description={t('products.inventoryManagedSeparately') || 'Stock quantities are managed separately in the inventory section.'}
            className="border-blue-100 bg-gradient-to-r from-white to-blue-50"
        >
            <div className="flex flex-wrap gap-2">
                <span className="rounded-full bg-blue-100 px-3 py-1 text-xs font-medium text-blue-700">
                    Warehouses -&gt; Opening Stock
                </span>
                <span className="rounded-full bg-blue-100 px-3 py-1 text-xs font-medium text-blue-700">
                    Inventory Management
                </span>
                <span className="rounded-full bg-blue-100 px-3 py-1 text-xs font-medium text-blue-700">
                    Purchases
                </span>
            </div>
        </WorkspaceSection>
    )

    const storefrontSettingsFields = (
        <div className="space-y-6">
            <EmbeddedSection title={t('products.productActive') || 'Status'}>
                <label className="flex items-center gap-3 rounded-xl border border-gray-200 px-4 py-3">
                    <input
                        type="checkbox"
                        checked={formData.isActive}
                        onChange={(e) => handleChange('isActive', e.target.checked)}
                        className="rounded"
                    />
                    <span className="text-sm font-medium text-gray-700">{t('products.productActive')}</span>
                </label>
            </EmbeddedSection>

            <EmbeddedSection
                title={t('products.publicationSettings') || 'Storefront Settings'}
                description={t('products.isOnlineVisible') || 'Publication, merchandising, and storefront visibility.'}
            >
                <div className="grid gap-4">
                    <div>
                        <label className="label">{t('products.publishStatus')}</label>
                        <select
                            value={normalizePublishStatus(formData.publishStatus)}
                            onChange={(e) => handleChange('publishStatus', normalizePublishStatus(e.target.value))}
                            className="input"
                        >
                            <option value={0}>{t('products.statusDraft')}</option>
                            <option value={1}>{t('products.statusPublished')}</option>
                            <option value={2}>{t('products.statusArchived')}</option>
                        </select>
                    </div>

                    <div>
                        <label className="label">{t('products.lowStockThreshold')}</label>
                        <input
                            type="number"
                            value={formData.lowStockThreshold}
                            onChange={(e) => handleChange('lowStockThreshold', e.target.value)}
                            className="input"
                            placeholder={t('products.autoFromMinStock')}
                        />
                    </div>

                    <div className="space-y-3 rounded-xl border border-gray-100 bg-gray-50 p-4">
                        <label className="flex items-center gap-2">
                            <input
                                type="checkbox"
                                checked={formData.isOnlineVisible}
                                onChange={(e) => handleChange('isOnlineVisible', e.target.checked)}
                                className="rounded"
                            />
                            <span className="text-sm text-gray-700">{t('products.isOnlineVisible')}</span>
                        </label>

                        <label className="flex items-center gap-2">
                            <input
                                type="checkbox"
                                checked={formData.isSearchable}
                                onChange={(e) => handleChange('isSearchable', e.target.checked)}
                                className="rounded"
                            />
                            <span className="text-sm text-gray-700">{t('products.isSearchable')}</span>
                        </label>

                        <label className="flex items-center gap-2">
                            <input
                                type="checkbox"
                                checked={formData.isFeatured}
                                onChange={(e) => handleChange('isFeatured', e.target.checked)}
                                className="rounded"
                            />
                            <span className="text-sm text-gray-700">{t('products.isFeatured')}</span>
                        </label>

                        <label className="flex items-center gap-2">
                            <input
                                type="checkbox"
                                checked={formData.allowBackorder}
                                onChange={(e) => handleChange('allowBackorder', e.target.checked)}
                                className="rounded"
                            />
                            <span className="text-sm text-gray-700">{t('products.allowBackorder')}</span>
                        </label>
                    </div>

                    {formData.isOnlineVisible && (
                        <div>
                            <label className="label">{t('products.storefrontLayout') || 'Storefront Layout'}</label>
                            <select
                                value={formData.productTemplate}
                                onChange={(e) => handleChange('productTemplate', e.target.value)}
                                className="input"
                            >
                                {productTemplateOptions.map((option) => (
                                    <option key={option.id} value={option.id}>
                                        {option.name}
                                    </option>
                                ))}
                            </select>
                        </div>
                    )}
                </div>
            </EmbeddedSection>
        </div>
    )

    const marketingFields = (
        <EmbeddedSection
            title={t('products.brandMetadata') || 'Marketing'}
            description={t('products.tags') || 'Branding, metadata, and merchandising fields.'}
        >
            <div className="grid gap-4">
                <div>
                    <label className="label">{t('products.brand')}</label>
                    <input
                        type="text"
                        value={formData.brand}
                        onChange={(e) => handleChange('brand', e.target.value)}
                        className="input"
                    />
                </div>

                <div>
                    <label className="label">{t('products.manufacturer')}</label>
                    <input
                        type="text"
                        value={formData.manufacturer}
                        onChange={(e) => handleChange('manufacturer', e.target.value)}
                        className="input"
                    />
                </div>

                <div>
                    <label className="label">{t('products.tags')}</label>
                    <input
                        type="text"
                        value={formData.tags}
                        onChange={(e) => handleChange('tags', e.target.value)}
                        className="input"
                        placeholder="tag1, tag2, tag3"
                    />
                </div>

                <div>
                    <label className="label">{t('products.videoUrl')}</label>
                    <input
                        type="url"
                        value={formData.videoUrl}
                        onChange={(e) => handleChange('videoUrl', e.target.value)}
                        className="input"
                        placeholder="https://youtube.com/..."
                    />
                </div>
            </div>
        </EmbeddedSection>
    )

    const seoFields = (
        <EmbeddedSection
            title={t('products.seoSettings') || 'SEO'}
            description={t('products.slugHint') || 'Search-facing metadata for the storefront product page.'}
        >
            <div className="grid gap-4">
                <div>
                    <label className="label">{t('products.slug')}</label>
                    <input
                        type="text"
                        value={formData.slug}
                        onChange={(e) => handleChange('slug', e.target.value.toLowerCase().replace(/\s+/g, '-'))}
                        className="input font-mono"
                        placeholder="product-url-slug"
                    />
                    <p className="mt-1 text-xs text-gray-500">{t('products.slugHint')}</p>
                </div>

                <div>
                    <label className="label">{t('products.metaTitle')}</label>
                    <input
                        type="text"
                        value={formData.metaTitle}
                        onChange={(e) => handleChange('metaTitle', e.target.value)}
                        className="input"
                        maxLength="70"
                    />
                </div>

                <div>
                    <label className="label">{t('products.metaKeywords')}</label>
                    <input
                        type="text"
                        value={formData.metaKeywords}
                        onChange={(e) => handleChange('metaKeywords', e.target.value)}
                        className="input"
                        placeholder="keyword1, keyword2, keyword3"
                    />
                </div>

                <div>
                    <label className="label">{t('products.metaDescription')}</label>
                    <textarea
                        value={formData.metaDescription}
                        onChange={(e) => handleChange('metaDescription', e.target.value)}
                        className="input"
                        rows="3"
                        maxLength="160"
                    />
                    <p className="mt-1 text-xs text-gray-500">{formData.metaDescription.length}/160</p>
                </div>
            </div>
        </EmbeddedSection>
    )

    const resolvePixelProductPrice = () => {
        if (hasVariants) {
            const representativeVariant = productVariantsSnapshot.find((variant) => variant.isDefault)
                || productVariantsSnapshot.find((variant) => variant.isActive !== false)
                || defaultVariant

            return toPositiveNumber(representativeVariant?.onlinePrice) || toPositiveNumber(representativeVariant?.price)
        }

        return toPositiveNumber(defaultUnitDraft?.sellingPrice)
    }

    const pixelOverrideSection = productId ? (
        <ProductPixelOverrideTab
            productId={productId}
            productPrice={resolvePixelProductPrice()}
        />
    ) : (
        <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3 text-sm text-gray-500">
            {t('variants.saveProductFirst') || 'Save the product first to configure advanced tracking.'}
        </div>
    )

    const affiliateSection = productId && canManageAffiliateProducts ? (
        <ProductAffiliateSettingsCard productId={productId} />
    ) : canManageAffiliateProducts ? (
        <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3 text-sm text-gray-500">
            {t('variants.saveProductFirst') || 'Save the product first to configure affiliate marketing.'}
        </div>
    ) : null

    const handleClose = async () => {
        if (await requestDiscardPendingChanges()) {
            onClose?.()
        }
    }

    const legacyActions = !hideActions && (
        <div className="flex gap-3">
            <button
                type="button"
                onClick={handleClose}
                className="flex-1 btn-secondary"
                disabled={loading}
            >
                {t('products.cancel')}
            </button>
            <button
                type="submit"
                className="flex-1 btn-primary"
                disabled={loading}
            >
                {loading ? t('products.saving') : productId ? t('products.update') : t('products.create')}
            </button>
        </div>
    )

    const modalSections = (
        <div className="space-y-6">
            {productTypeSection}
            {basicInformationSection}
            {simpleIdentitySection}
            {variantCreateGuidanceSection}
            {!isVariantCreateMode && productUnitsSection}
            {mediaSection}
            {!isVariantCreateMode && variantsSection}
            {!isVariantCreateMode && inventorySection}

            <WorkspaceSection title={t('products.publicationSettings') || 'Storefront Settings'}>
                {storefrontSettingsFields}
            </WorkspaceSection>

            <WorkspaceSection title={t('products.brandMetadata') || 'Marketing'}>
                {marketingFields}
            </WorkspaceSection>

            <WorkspaceSection title={t('products.seoSettings') || 'SEO'}>
                {seoFields}
            </WorkspaceSection>

            {pixelOverrideSection}
            {canManageAffiliateProducts && affiliateSection}
        </div>
    )

    const workspaceSections = (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
            <div className="space-y-6">
                {productTypeSection}
                {basicInformationSection}
                {simpleIdentitySection}
                {variantCreateGuidanceSection}
                {!isVariantCreateMode && productUnitsSection}
                {mediaSection}
                {!isVariantCreateMode && variantsSection}
                {!isVariantCreateMode && inventorySection}
            </div>

            <div className="space-y-4 xl:sticky xl:top-28 xl:self-start">
                <SidebarPanel
                    title={t('products.publicationSettings') || 'Storefront Settings'}
                    subtitle={t('products.isOnlineVisible') || 'Publication, visibility, and product template settings.'}
                >
                    {storefrontSettingsFields}
                </SidebarPanel>

                <SidebarPanel
                    title={t('products.brandMetadata') || 'Marketing'}
                    subtitle={t('products.tags') || 'Branding and merchandising details.'}
                    defaultOpen={false}
                >
                    {marketingFields}
                </SidebarPanel>

                <SidebarPanel
                    title={t('products.seoSettings') || 'SEO'}
                    subtitle={t('products.slugHint') || 'Meta fields for search and storefront pages.'}
                    defaultOpen={false}
                >
                    {seoFields}
                </SidebarPanel>

                <SidebarPanel
                    title={t('pixel.productOverride') || 'Pixel Override'}
                    subtitle={t('common.advanced') || 'Advanced conversion tracking controls.'}
                    defaultOpen={false}
                    bodyClassName="p-0"
                >
                    {pixelOverrideSection}
                </SidebarPanel>

                {canManageAffiliateProducts && (
                    <SidebarPanel
                        title={t('affiliate.title') || 'Affiliate Marketing'}
                        subtitle={t('products.marketplaceCategory') || 'Commission and marketplace category settings.'}
                        defaultOpen={false}
                        bodyClassName="p-0"
                    >
                        {affiliateSection}
                    </SidebarPanel>
                )}
            </div>
        </div>
    )

    return (
        <form id={formId} onSubmit={handleSubmit} className="space-y-6">
            {error && (
                <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-red-700">
                    {error}
                </div>
            )}

            {isWorkspace ? workspaceSections : modalSections}
            {legacyActions}
        </form>
    )
})

export default ProductForm

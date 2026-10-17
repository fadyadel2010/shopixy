import { forwardRef, useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api, { getSafeApiErrorMessage } from '../services/api'
import { useTranslation } from 'react-i18next'
import ProductMediaManager from '../components/ProductMediaManager'
import ProductVariantsTab from '../components/ProductVariantsTab'
import ProductPixelOverrideTab from '../components/ProductPixelOverrideTab'
import SidebarPanel from './SidebarPanel'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'
import { getProductTemplateOptions } from '@/storefront/products/ProductTemplateRegistry'
import { useToast } from '../components/Toast'
import { normalizeProductAuthoringVariants } from '../utils/productAuthoringVariants'
import { normalizePublishStatus } from '../utils/productStatus'
import { normalizeSkuPart, productSkuBase } from './productSku'
import { containsNonAscii } from '../security/textCharacterPolicy'

function isSkuConflict(error) {
    const data = error?.response?.data
    return error?.response?.status === 409 && [
        ...(data?.errors || []), ...(data?.data?.errors || [])
    ].some(item => (item.code || item.Code) === 'PRODUCT_SKU_CONFLICT')
}

function retryGeneratedSkuPayload(payload) {
    const newBase = productSkuBase(null, crypto.randomUUID())
    const replacements = {}
    const replaceSku = sku => {
        if (!/^PX-[A-F0-9]{32}(?:-V\d+)?$/.test(sku || '')) return sku
        const updated = newBase + (sku.match(/-V\d+$/)?.[0] || '')
        replacements[sku] = updated
        return updated
    }
    const next = {
        ...payload,
        sku: replaceSku(payload.sku),
        variants: payload.variants?.map(variant => ({ ...variant, sku: replaceSku(variant.sku) })) ?? payload.variants
    }
    return { next, replacements }
}

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

// WAVE 3: simple-product storefront price. Empty/zero = null (backend falls
// back to the POS price); a finite non-negative number is sent as the
// requested default-variant onlinePrice. Negative input is normalized to null
// here (the input is min-clamped) while the backend remains the authority
// that rejects negatives.
function parseSimpleStorefrontPrice(value) {
    if (value == null || `${value}`.trim() === '') {
        return null
    }

    const parsed = Number.parseFloat(value)
    return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function translateWithFallback(t, key, fallback) {
    const value = t(key)
    return value && value !== key ? value : fallback
}

const SAFE_AUTHORING_ERROR_CODES = new Set([
    'PRODUCT_ACTIVE_PRICED_VARIANT_REQUIRED',
    'PRODUCT_ATTRIBUTE_CONFLICT',
    'PRODUCT_BARCODE_CONFLICT',
    'PRODUCT_BARCODE_OWNERSHIP_CONFLICT',
    'PRODUCT_BARCODE_TOO_LONG',
    'PRODUCT_BASE_UNIT_CONVERSION_FACTOR_INVALID',
    'PRODUCT_BASE_UNIT_DUPLICATE',
    'PRODUCT_BASE_UNIT_REQUIRED',
    'PRODUCT_CATEGORY_NOT_LIVE',
    'PRODUCT_CATEGORY_REQUIRED',
    'PRODUCT_DEFAULT_UNIT_DUPLICATE',
    'PRODUCT_DEFAULT_UNIT_PRICE_REQUIRED',
    'PRODUCT_DEFAULT_UNIT_REQUIRED',
    'PRODUCT_NAME_REQUIRED',
    'PRODUCT_NAME_TOO_LONG',
    'PRODUCT_PUBLISH_STATUS_INVALID',
    'PRODUCT_SKU_CONFLICT',
    'PRODUCT_SKU_TOO_LONG',
    'PRODUCT_TAX_RATE_INVALID',
    'PRODUCT_TEMPLATE_TOO_LONG',
    'PRODUCT_TYPE_CONVERSION_UNSUPPORTED',
    'PRODUCT_VARIANT_ATTRIBUTE_NAME_TOO_LONG',
    'PRODUCT_VARIANT_ATTRIBUTE_VALUE_TOO_LONG',
    'PRODUCT_VARIANT_BARCODE_TOO_LONG',
    'PRODUCT_VARIANT_COST_PRICE_INVALID',
    'PRODUCT_VARIANT_IMAGE_URL_TOO_LONG',
    'PRODUCT_VARIANT_ONLINE_PRICE_INVALID',
    'PRODUCT_VARIANT_PRICE_INVALID',
    'PRODUCT_VARIANT_SKU_TOO_LONG',
    'PRODUCT_VARIANT_WEIGHT_INVALID',
    'PRODUCT_UNIT_CONVERSION_FACTORS_INVALID',
    'PRODUCT_UNIT_CONVERSION_FACTOR_REQUIRED',
    'PRODUCT_UNIT_DUPLICATE',
    'PRODUCT_UNIT_NOT_FOUND',
    'PRODUCT_UNIT_NAME_TOO_LONG',
    'PRODUCT_UNITS_REQUIRED',
    'SIMPLE_PRODUCT_VARIANT_STATE_INVALID',
    'VARIANT_HAS_COMMERCEHUB_LISTING',
    'VARIANT_HAS_COMMERCEHUB_ORDERS',
    'VARIANT_HAS_MOVEMENTS',
    'VARIANT_HAS_RESERVATIONS',
    'VARIANT_HAS_STOCK',
    'VARIANT_HAS_STOREFRONT_ORDERS',
    'VARIANT_USED_IN_PURCHASES',
    'VARIANT_USED_IN_RETURNS',
    'VARIANT_USED_IN_SALES',
    'VARIANT_USED_IN_TRANSFERS',
    'VARIANT_PRODUCT_VARIANT_STATE_REQUIRED'
])

const UNSAFE_ERROR_MESSAGE_PATTERN = /exception|stack\s*trace|\bat\s+\S+\(|sql|select\s+.+\s+from|system\.|authorization|bearer|tenant/i

function isSafeAuthoringMessage(value) {
    return typeof value === 'string'
        && value.trim().length > 0
        && value.length <= 240
        && !UNSAFE_ERROR_MESSAGE_PATTERN.test(value)
}

function extractApiErrorMessage(error, fallback, t) {
    const status = error?.response?.status
    const data = error?.response?.data
    const nestedData = data?.data

    if (status === 409) {
        // WAVE 4: a 409 is not always a concurrency conflict — the server
        // returns typed identity conflicts (SKU/barcode/attribute combination)
        // on the same status. Surface the precise allow-listed server message
        // when present; only genuine concurrency/idempotency conflicts keep
        // the generic refresh guidance.
        const conflictItems = [
            ...(Array.isArray(data?.errors) ? data.errors : []),
            ...(Array.isArray(nestedData?.errors) ? nestedData.errors : [])
        ]
        const conflictMessages = conflictItems
            .filter((item) => SAFE_AUTHORING_ERROR_CODES.has(item?.code || item?.Code))
            .map((item) => item?.message || item?.Message)
            .filter(isSafeAuthoringMessage)

        if (conflictMessages.length > 0) {
            return conflictMessages.join(' ')
        }

        return translateWithFallback(
            t,
            'products.concurrencyConflict',
            'This product changed while you were editing it. Refresh the product before retrying.'
        )
    }

    if (status === 404) {
        return translateWithFallback(
            t,
            'products.noLongerAvailable',
            'This product is no longer available. Return to Products and refresh the list.'
        )
    }

    if (!error?.response) {
        return translateWithFallback(
            t,
            'products.saveOutcomeUncertain',
            'The save outcome could not be confirmed. Refresh the product before retrying.'
        )
    }

    const validationItems = [
        ...(Array.isArray(data?.errors) ? data.errors : []),
        ...(Array.isArray(nestedData?.errors) ? nestedData.errors : [])
    ]
    const validationMessages = validationItems
        .filter((item) => SAFE_AUTHORING_ERROR_CODES.has(item?.code || item?.Code))
        .map((item) => item?.message || item?.Message)
        .filter(isSafeAuthoringMessage)

    if ((status === 400 || status === 422) && validationMessages.length > 0) {
        return validationMessages.join(' ')
    }

    if ((status === 400 || status === 422) && data?.errors && typeof data.errors === 'object' && !Array.isArray(data.errors)) {
        const messages = Object.values(data.errors)
            .flatMap((value) => Array.isArray(value) ? value : [value])
            .filter(isSafeAuthoringMessage)

        if (messages.length > 0) {
            return messages.join(' ')
        }
    }

    return getSafeApiErrorMessage(error, fallback)
}

function getProductRowVersion(product, fallback = null) {
    return product?.rowVersion || product?.RowVersion || fallback
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

function buildProductUnitsPayloadFromDrafts(productUnits) {
    return productUnits
        .filter((unit) => unit.unitId)
        .map((unit) => ({
            productUnitId: unit.productUnitId,
            unitId: unit.unitId,
            conversionFactor: Number.parseFloat(unit.conversionFactor) || 0,
            purchasePrice: unit.purchasePrice === '' ? null : Number.parseFloat(unit.purchasePrice) || 0,
            sellingPrice: unit.sellingPrice === '' ? null : Number.parseFloat(unit.sellingPrice) || 0,
            barcode: unit.barcode || null,
            isDefault: unit.isDefault,
            isBaseUnit: unit.isBaseUnit
        }))
}

function buildProductAuthoringPayloadFromDraft({
    formData,
    productId,
    hasVariants,
    simpleProductData,
    productUnits,
    variantAuthoringSummary,
    variantsOverride = null,
    includeVariants = false
}) {
    const isVariantCreateMode = !productId && hasVariants
    const variantSnapshot = Array.isArray(variantsOverride)
        ? normalizeProductAuthoringVariants(variantsOverride)
        : (hasVariants ? normalizeProductAuthoringVariants(variantAuthoringSummary) : [])

    return {
        ...formData,
        // WAVE 1: a blank slug is sent as null so the server slug authority
        // derives a deterministic tenant-unique slug instead of persisting ''.
        slug: sanitizeSlugValue(formData.slug) || null,
        taxRate: parseFloat(formData.taxRate) || 0,
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
        productTemplate: formData.productTemplate || 'default',
        // WAVE 3: simple-product storefront price. Sent as the requested
        // default-variant onlinePrice; null/zero = storefront uses POS price.
        onlinePrice: hasVariants ? null : parseSimpleStorefrontPrice(simpleProductData.storefrontPrice),
        productUnits: hasVariants ? null : buildProductUnitsPayloadFromDrafts(productUnits),
        variants: hasVariants
            ? (productId && !includeVariants ? null : variantSnapshot)
            : []
    }
}

const PRODUCT_AUTHORING_COMPARISON_IGNORED_FIELDS = new Set([
    'RowVersion',
    'rowVersion',
    'expectedVersion',
    'idempotency',
    'idempotencyKey',
    'localId',
    'reactKey',
    'key',
    'sortOrder'
])

function normalizeProductAuthoringText(value) {
    const normalized = `${value ?? ''}`.trim().replace(/\s+/g, ' ')
    return normalized || null
}

function stableStringify(value) {
    if (Array.isArray(value)) {
        return `[${value.map(stableStringify).join(',')}]`
    }

    if (value && typeof value === 'object') {
        return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableStringify(value[key])}`).join(',')}}`
    }

    return JSON.stringify(value)
}

function isEmptyCanonicalValue(value) {
    return value == null
        || (Array.isArray(value) && value.length === 0)
        || (value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0)
}

function canonicalizeProductAuthoringValue(value, fieldName = '') {
    if (value == null) {
        return null
    }

    if (typeof value === 'string') {
        return normalizeProductAuthoringText(value)
    }

    if (typeof value === 'number') {
        return Number.isFinite(value) ? Number(value) : null
    }

    if (typeof value === 'boolean') {
        return value
    }

    if (Array.isArray(value)) {
        return value
            .map((item) => canonicalizeProductAuthoringValue(item, fieldName))
            .filter((item) => !isEmptyCanonicalValue(item))
            .sort((left, right) => stableStringify(left).localeCompare(stableStringify(right)))
    }

    if (typeof value === 'object') {
        return Object.keys(value)
            .sort()
            .reduce((acc, key) => {
                if (PRODUCT_AUTHORING_COMPARISON_IGNORED_FIELDS.has(key)) {
                    return acc
                }

                const normalizedValue = canonicalizeProductAuthoringValue(value[key], key)
                if (!isEmptyCanonicalValue(normalizedValue)) {
                    acc[key] = normalizedValue
                }

                return acc
            }, {})
    }

    return value
}

function createCanonicalProductAuthoringSnapshot(payload, previousSnapshot = null) {
    const snapshot = canonicalizeProductAuthoringValue(payload)

    if (payload?.hasVariants === true && payload?.variants == null && previousSnapshot?.variants !== undefined) {
        snapshot.variants = previousSnapshot.variants
    }

    return snapshot
}

function hasSemanticProductAuthoringChanges(baselineSnapshot, currentPayload) {
    if (!baselineSnapshot) {
        return true
    }

    const currentSnapshot = createCanonicalProductAuthoringSnapshot(currentPayload, baselineSnapshot)
    return stableStringify(baselineSnapshot) !== stableStringify(currentSnapshot)
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

function createInitialProductFormData() {
    return {
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
    }
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
    const { showError, showSuccess } = useToast()
    const [loading, setLoading] = useState(false)
    const variantsTabRef = useRef(null)
    const skuDraftIdRef = useRef(crypto.randomUUID())
    const variantsSectionRef = useRef(null)
    const formDataRef = useRef(null)
    const pendingSaveRef = useRef(null)
    const pendingSavePromiseRef = useRef(null)
    const productIdentityGenerationRef = useRef(0)
    const lastProductSaveErrorRef = useRef('')
    const productAuthoringBaselineRef = useRef(null)
    const [categories, setCategories] = useState([])
    const [units, setUnits] = useState([])
    const [generatingBarcode, setGeneratingBarcode] = useState(false)
    const [error, setError] = useState('')
    const [productTemplateOptions, setProductTemplateOptions] = useState(getProductTemplateOptions())
    const [hasVariants, setHasVariants] = useState(false)
    const [defaultVariant, setDefaultVariant] = useState(null)
    const [productUnits, setProductUnits] = useState([])
    const [variantAuthoringSummary, setVariantAuthoringSummary] = useState([])
    const [acceptedRowVersion, setAcceptedRowVersion] = useState(null)
    const [productMediaRevision, setProductMediaRevision] = useState(0)
    const handleMediaChanged = useCallback((headers) => {
        const previous = headers?.['x-product-previous-version']
        const next = headers?.['x-product-version']
        if (previous && next) {
            setAcceptedRowVersion(current => String(current) === String(previous) ? next : current)
        }
        setProductMediaRevision(current => current + 1)
    }, [])
    const [simpleProductData, setSimpleProductData] = useState({
        sku: '',
        barcode: '',
        storefrontPrice: ''
    })
    const [formData, setFormData] = useState(createInitialProductFormData)
    // WAVE 1: slug auto-suggest tracks whether the user explicitly edited the slug.
    // Until touched, the slug preview follows the product name; backend remains
    // the slug authority and derives the stored slug when the field is blank.
    const isSlugManuallyEditedRef = useRef(false)
    // WAVE 1 bilingual UX: Arabic translations are optional progressive
    // disclosure. The storefront falls back to the primary name/description
    // when a translation is absent, so empty translations stay collapsed.
    const [showArabicName, setShowArabicName] = useState(false)
    const [showArabicDescription, setShowArabicDescription] = useState(false)

    const isWorkspace = mode === 'workspace'
    const isCreateMode = !productId
    const isVariantCreateMode = isCreateMode && hasVariants

    useEffect(() => {
        formDataRef.current = formData
    }, [formData])

    useEffect(() => {
        productIdentityGenerationRef.current += 1
        const identityGeneration = productIdentityGenerationRef.current
        let isCurrentProduct = true
        const initialFormData = createInitialProductFormData()

        formDataRef.current = initialFormData
        setFormData(initialFormData)
        isSlugManuallyEditedRef.current = false
        setShowArabicName(false)
        setShowArabicDescription(false)
        setHasVariants(false)
        setDefaultVariant(null)
        setProductUnits([])
        setVariantAuthoringSummary([])
        setAcceptedRowVersion(null)
        setSimpleProductData({ sku: '', barcode: '', storefrontPrice: '' })
        setError('')
        pendingSaveRef.current = null
        pendingSavePromiseRef.current = null
        productAuthoringBaselineRef.current = productId
            ? null
            : createCanonicalProductAuthoringSnapshot(buildProductAuthoringPayloadFromDraft({
                formData: initialFormData,
                productId: null,
                hasVariants: false,
                simpleProductData: { sku: '', barcode: '', storefrontPrice: '' },
                productUnits: [],
                variantAuthoringSummary: []
            }))

        fetchCategories()
        fetchUnits()
        fetchProductTemplates()
        if (productId) {
            void fetchProduct(productId, () => isCurrentProduct)
        } else {
            setLoading(false)
        }

        return () => {
            isCurrentProduct = false
            if (productIdentityGenerationRef.current === identityGeneration) {
                productIdentityGenerationRef.current += 1
            }
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

    const hasPendingProductChanges = () => {
        if (!productAuthoringBaselineRef.current) {
            return !productId
        }

        return hasSemanticProductAuthoringChanges(
            productAuthoringBaselineRef.current,
            buildProductAuthoringPayload({ includeVariants: false })
        )
    }

    const requestDiscardPendingChanges = async () => {
        if (pendingSavePromiseRef.current) {
            return false
        }

        if (!hasPendingProductChanges() && !hasPendingVariantChanges()) {
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
            return hasPendingProductChanges() || hasPendingVariantChanges()
        },
        async confirmDiscardPendingChanges() {
            return await requestDiscardPendingChanges()
        },
        applyAiGeneratedData(generatedProduct) {
            const currentFormData = formDataRef.current

            if (!currentFormData || !generatedProduct) {
                showError('Unable to apply the generated product data to the form.')
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

            if (nextFormData.slug) {
                isSlugManuallyEditedRef.current = true
            }

            formDataRef.current = nextFormData
            setFormData(nextFormData)

            if (counters.appliedFieldCount > 0) {
                showSuccess(
                    counters.skippedFieldCount > 0
                        ? 'AI content applied to empty fields. Existing values were left unchanged.'
                        : 'AI content applied to the product form.'
                )
            } else {
                showError('All matching product fields already have values, so nothing was changed.')
            }

            return counters
        }
    }), [productId, hasVariants, formData, productUnits, simpleProductData, variantAuthoringSummary, confirm, showError, showSuccess, t])

    const fetchCategories = async () => {
        try {
            const response = await api.get('/categories')
            setCategories(response.data)
        } catch {
            console.error('Failed to load categories')
        }
    }

    const fetchUnits = async () => {
        try {
            const response = await api.get('/units')
            setUnits(response.data)
        } catch {
            console.error('Failed to load units')
        }
    }

    const fetchProductTemplates = async () => {
        try {
            const response = await api.get('/admin/store-config')
            setProductTemplateOptions(getProductTemplateOptions(response.data?.productTemplates))
        } catch {
            console.error('Failed to load product templates')
            setProductTemplateOptions(getProductTemplateOptions())
        }
    }

    const fetchProduct = async (targetProductId, isCurrentProduct = () => true) => {
        try {
            setLoading(true)
            const response = await api.get(`/products/${targetProductId}`)
            if (!isCurrentProduct()) return

            const product = response.data
            const variantCount = Array.isArray(product.variants) ? product.variants.length : 0
            const derivedHasVariants = !!product.hasVariants || variantCount > 1
            const loadedProductUnits = normalizeProductUnitDrafts(product.units)
            const loadedFormData = {
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
            }
            let loadedHasVariants = derivedHasVariants
            let loadedSimpleProductData = { sku: '', barcode: '', storefrontPrice: '' }
            let loadedVariants = product.variants

            setAcceptedRowVersion(product.rowVersion || product.RowVersion || null)
            setHasVariants(derivedHasVariants)
            setProductUnits(loadedProductUnits)
            setFormData(loadedFormData)
            // A stored slug counts as explicitly set: never auto-overwrite it
            // from the name on edit (slug stability preserves storefront URLs).
            isSlugManuallyEditedRef.current = Boolean(product.slug)
            setShowArabicName(Boolean(product.nameAr))
            setShowArabicDescription(Boolean(product.descriptionAr))

            if (!derivedHasVariants) {
                const defaultVariantLoad = await fetchDefaultVariant(targetProductId, isCurrentProduct)
                if (!isCurrentProduct()) return

                loadedHasVariants = defaultVariantLoad?.hasVariants || false
                loadedSimpleProductData = defaultVariantLoad?.simpleProductData || loadedSimpleProductData
                loadedVariants = defaultVariantLoad?.variants || loadedVariants
            }

            productAuthoringBaselineRef.current = createCanonicalProductAuthoringSnapshot(buildProductAuthoringPayloadFromDraft({
                formData: loadedFormData,
                productId: targetProductId,
                hasVariants: loadedHasVariants,
                simpleProductData: loadedSimpleProductData,
                productUnits: loadedProductUnits,
                variantAuthoringSummary: loadedVariants,
                includeVariants: loadedHasVariants
            }))
        } catch {
            if (isCurrentProduct()) {
                setError(t('products.failedToLoad'))
            }
        } finally {
            if (isCurrentProduct()) {
                setLoading(false)
            }
        }
    }

    const fetchDefaultVariant = async (targetProductId = productId, isCurrentProduct = () => true) => {
        try {
            const response = await api.get(`/products/${targetProductId}/variants`)
            if (!isCurrentProduct()) return null

            const variants = response.data || []

            if (variants.length > 1) {
                setHasVariants(true)
                setDefaultVariant(null)
                return {
                    hasVariants: true,
                    variants,
                    simpleProductData: { sku: '', barcode: '', storefrontPrice: '' }
                }
            }

            const defaultVar = variants.find(v => v.isDefault)

            if (defaultVar) {
                const loadedSimpleProductData = {
                    sku: defaultVar.sku || '',
                    barcode: defaultVar.barcode || '',
                    // WAVE 3: simple-product storefront price (default-variant onlinePrice).
                    // Empty = storefront uses the POS (default-unit selling) price.
                    storefrontPrice: defaultVar.onlinePrice != null && defaultVar.onlinePrice !== ''
                        ? `${defaultVar.onlinePrice}`
                        : ''
                }

                setDefaultVariant(defaultVar)
                setSimpleProductData(loadedSimpleProductData)
                return {
                    hasVariants: false,
                    variants,
                    defaultVariant: defaultVar,
                    simpleProductData: loadedSimpleProductData
                }
            }

            return {
                hasVariants: false,
                variants,
                defaultVariant: null,
                simpleProductData: { sku: '', barcode: '', storefrontPrice: '' }
            }
        } catch {
            if (isCurrentProduct()) {
                console.error('Failed to load default variant')
            }
            return {
                hasVariants: false,
                variants: [],
                defaultVariant: null,
                simpleProductData: { sku: '', barcode: '', storefrontPrice: '' }
            }
        }
    }

    const handleGenerateBarcode = async (type = 'EAN13') => {
        try {
            setGeneratingBarcode(true)
            const response = await api.post(`/products/generate-barcode?type=${type}`)
            setSimpleProductData(prev => ({ ...prev, barcode: response.data.barcode }))
        } catch {
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

    const buildVariantAuthoringSnapshot = () => (
        hasVariants ? normalizeProductAuthoringVariants(variantAuthoringSummary) : []
    )

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

    const getCurrentVariantAuthoringSnapshot = (variantsOverride = null) => (
        Array.isArray(variantsOverride)
            ? normalizeProductAuthoringVariants(variantsOverride)
            : buildVariantAuthoringSnapshot()
    )

    const buildProductAuthoringPayload = ({ variantsOverride = null, includeVariants = false } = {}) => buildProductAuthoringPayloadFromDraft({
        formData,
        productId,
        hasVariants,
        simpleProductData,
        productUnits,
        variantAuthoringSummary,
        variantsOverride,
        includeVariants
    })

    const hasUnsavedProductChanges = hasPendingProductChanges()

    useEffect(() => {
        if (!hasUnsavedProductChanges || loading) {
            return undefined
        }

        const handleBeforeUnload = (event) => {
            event.preventDefault()
            event.returnValue = ''
        }

        window.addEventListener('beforeunload', handleBeforeUnload)
        return () => window.removeEventListener('beforeunload', handleBeforeUnload)
    }, [hasUnsavedProductChanges, loading])

    const handleVariantDraftChange = useCallback((draft) => {
        setVariantAuthoringSummary(normalizeProductAuthoringVariants(draft?.authoringVariants))
    }, [])

    const getAuthoringValidationError = (variantsOverride = null) => {
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
            const hasActivePricedVariant = getCurrentVariantAuthoringSnapshot(variantsOverride).some((variant) => (
                variant.isActive && (toPositiveNumber(variant.onlinePrice) > 0 || toPositiveNumber(variant.price) > 0)
            ))

            return hasActivePricedVariant
                ? null
                : translateWithFallback(t, 'products.activePricedVariantRequired', 'At least one active priced variant is required before publication.')
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

        // WAVE 3: storefront price is optional, but never negative.
        if (simpleProductData.storefrontPrice !== '' && !(Number.parseFloat(simpleProductData.storefrontPrice) >= 0)) {
            return translateWithFallback(t, 'products.storefrontPriceInvalid', 'Storefront price cannot be negative.')
        }

        if (completeUnits.some((unit) => toPositiveNumber(unit.conversionFactor) <= 0 || (unit.isBaseUnit && Number.parseFloat(unit.conversionFactor) !== 1))) {
            return t('products.productUnitsInvalid') || 'Product units must have valid conversion factors.'
        }

        return null
    }

    const performProductAuthoringSave = async ({ variantsOverride = null, includeVariants = false } = {}) => {
        const saveIdentityGeneration = productIdentityGenerationRef.current
        lastProductSaveErrorRef.current = ''

        if (!formData.categoryId) {
            const message = t('products.categoryRequired')
            lastProductSaveErrorRef.current = message
            setError(message)
            showError(message)
            return false
        }

        const authoringValidationError = getAuthoringValidationError(variantsOverride)
        if (authoringValidationError) {
            lastProductSaveErrorRef.current = authoringValidationError
            setError(authoringValidationError)
            showError(authoringValidationError)
            return false
        }

        try {
            let productData = buildProductAuthoringPayload({ variantsOverride, includeVariants })
            const effectiveRowVersion = acceptedRowVersion

            if (productId && !hasSemanticProductAuthoringChanges(productAuthoringBaselineRef.current, productData)) {
                const message = translateWithFallback(t, 'products.noChangesToSave', 'No changes to save.')
                showSuccess(message)
                return {
                    id: productId,
                    rowVersion: effectiveRowVersion || null,
                    hasVariants,
                    __productAuthoringNoChanges: true
                }
            }

            const saveFingerprint = JSON.stringify({
                productId: productId || null,
                rowVersion: productId ? effectiveRowVersion || null : null,
                productData
            })
            const idempotencyKey = pendingSaveRef.current?.fingerprint === saveFingerprint
                ? pendingSaveRef.current.key
                : createProductAuthoringIdempotencyKey(productId)
            pendingSaveRef.current = { fingerprint: saveFingerprint, key: idempotencyKey }

            let savedProduct
            let response
            let replacements = {}
            for (let attempt = 0; attempt < 3; attempt += 1) {
                const key = attempt === 0 ? idempotencyKey : createProductAuthoringIdempotencyKey(productId)
                const requestOptions = buildProductAuthoringRequestOptions(productId, effectiveRowVersion, key)
                try {
                    response = productId
                        ? await api.put(`/products/${productId}`, productData, requestOptions)
                        : await api.post('/products', productData, requestOptions)
                    break
                } catch (saveError) {
                    if (!isSkuConflict(saveError) || attempt === 2) throw saveError
                    const retry = retryGeneratedSkuPayload(productData)
                    if (Object.keys(retry.replacements).length === 0) throw saveError
                    for (const [original, current] of Object.entries(replacements)) {
                        replacements[original] = retry.replacements[current] || current
                    }
                    Object.assign(replacements, retry.replacements)
                    productData = retry.next
                }
            }

            if (productIdentityGenerationRef.current !== saveIdentityGeneration) {
                return { __productAuthoringStaleResponse: true }
            }
            if (Object.keys(replacements).length) {
                setSimpleProductData(prev => ({ ...prev, sku: replacements[prev.sku] || prev.sku }))
                variantsTabRef.current?.replaceGeneratedSkus?.(replacements)
            }

            if (productId) {
                savedProduct = response.data || { id: productId }
                if (productIdentityGenerationRef.current !== saveIdentityGeneration) {
                    return { __productAuthoringStaleResponse: true }
                }
                const returnedRowVersion = getProductRowVersion(savedProduct, acceptedRowVersion)
                setAcceptedRowVersion(returnedRowVersion)
                productAuthoringBaselineRef.current = createCanonicalProductAuthoringSnapshot(productData, productAuthoringBaselineRef.current)
            } else {
                savedProduct = response.data
                if (productIdentityGenerationRef.current !== saveIdentityGeneration) {
                    return { __productAuthoringStaleResponse: true }
                }
                const returnedRowVersion = getProductRowVersion(savedProduct)
                setAcceptedRowVersion(returnedRowVersion)
                productAuthoringBaselineRef.current = createCanonicalProductAuthoringSnapshot(productData)
            }

            invalidatePosRuntimeCache({ reason: 'product-updated' })
            showSuccess(t('common.updatedSuccessfully') || 'Saved successfully')
            onSave?.({
                ...savedProduct,
                hasVariants: savedProduct?.hasVariants ?? hasVariants
            })
            pendingSaveRef.current = null
            return savedProduct
        } catch (err) {
            if (productIdentityGenerationRef.current !== saveIdentityGeneration) {
                return { __productAuthoringStaleResponse: true }
            }
            const message = extractApiErrorMessage(err, t('products.failedToSave'), t)
            lastProductSaveErrorRef.current = message
            setError(message)
            showError(message)
            return false
        }
    }

    const saveProductAuthoring = (options = {}) => {
        if (pendingSavePromiseRef.current) {
            return pendingSavePromiseRef.current
        }

        const savePromise = performProductAuthoringSave(options)
            .finally(() => {
                if (pendingSavePromiseRef.current === savePromise) {
                    pendingSavePromiseRef.current = null
                }
            })

        pendingSavePromiseRef.current = savePromise
        return savePromise
    }

    const handleSaveVariantDraft = useCallback(async (variantDraft) => {
        setError('')

        const savedProduct = await saveProductAuthoring({
            variantsOverride: variantDraft?.authoringVariants || [],
            includeVariants: true
        })

        if (savedProduct?.__productAuthoringStaleResponse) {
            return savedProduct
        }

        if (!savedProduct) {
            throw new Error(lastProductSaveErrorRef.current || t('products.failedToSave'))
        }

        return savedProduct
    }, [acceptedRowVersion, formData, hasVariants, productId, variantAuthoringSummary, showError, showSuccess, t])

    const handleSubmit = async (e) => {
        e.preventDefault()
        const submitIdentityGeneration = productIdentityGenerationRef.current
        setLoading(true)
        setError('')

        try {
            let variantDraft = null
            let includeVariants = false

            if (productId && hasVariants && variantsTabRef.current?.hasPendingChanges?.()) {
                variantDraft = await variantsTabRef.current.prepareProductAuthoringDraft?.()
                if (!variantDraft) {
                    const variantError = variantsTabRef.current?.getLastError?.()
                    if (variantError) {
                        setError(variantError)
                        showError(variantError)
                    }
                    return
                }

                includeVariants = true
            }

            await saveProductAuthoring({
                variantsOverride: variantDraft?.authoringVariants || null,
                includeVariants
            })
        } finally {
            if (productIdentityGenerationRef.current === submitIdentityGeneration) {
                setLoading(false)
            }
        }
    }

    const handleChange = (field, value) => {
        if (field === 'slug') {
            isSlugManuallyEditedRef.current = true
            setFormData(prev => ({ ...prev, slug: sanitizeSlugValue(value) }))
            return
        }

        // Suggest only while creating: on edit the stored slug is stable and the
        // server backfills a missing slug, so typing a name must not dirty it.
        if (field === 'name' && isCreateMode && !isSlugManuallyEditedRef.current) {
            const suggestedSlug = sanitizeSlugValue(value)
            setFormData(prev => ({ ...prev, name: value, slug: suggestedSlug }))
            return
        }

        if (field === 'nameAr' && value && !showArabicName) {
            setShowArabicName(true)
        }

        if (field === 'descriptionAr' && value && !showArabicDescription) {
            setShowArabicDescription(true)
        }

        setFormData(prev => ({ ...prev, [field]: value }))
    }

    const handleSimpleChange = (field, value) => {
        setSimpleProductData(prev => ({ ...prev, [field]: value }))
    }

    const handleGenerateSimpleSku = () => {
        const nameSku = containsNonAscii(formData.name)
            ? '' : normalizeSkuPart(formData.name).toUpperCase()
        setSimpleProductData(prev => ({
            ...prev,
            sku: nameSku || productSkuBase(productId, skuDraftIdRef.current)
        }))
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
        ...(!hasVariants ? [
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
            }
        ] : []),
        {
            label: t('products.readyForPublication') || 'Ready for publication',
            ready: publicationReady
        }
    ]
    const selectedUnitIds = new Set(completeProductUnitDrafts.map((unit) => unit.unitId))

    // WAVE 3 (C6): the product model is fixed once created. Flipping
    // Simple <-> Variant rewires SKU, stock and price ownership, so the choice
    // is locked on edit; the backend additionally rejects destructive flips.
    const isProductTypeLocked = Boolean(productId)
    const productTypeSection = (
        <WorkspaceSection
            eyebrow={isWorkspace ? (t('products.basicInfo') || 'Product Setup') : null}
            title={t('products.productType') || 'Product Type'}
            description={t('products.productTypeDescription') || 'Choose how this product is sold.'}
            className="border-purple-100 bg-gradient-to-r from-white to-purple-50"
        >
            {isProductTypeLocked && (
                <div
                    className="mb-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
                    role="note"
                >
                    {t('products.productTypeLocked') || 'Product type is fixed after creation to protect pricing, stock and order history.'}
                </div>
            )}
            <div className="grid gap-3 md:grid-cols-2" role="radiogroup" aria-label={t('products.productType') || 'Product Type'}>
                <label className={`flex cursor-pointer items-start gap-3 rounded-xl border bg-white p-4 transition ${
                    !hasVariants ? 'border-primary-300 ring-2 ring-primary-100' : 'border-gray-200 hover:border-gray-300'
                } ${isProductTypeLocked ? 'cursor-not-allowed opacity-70' : ''}`}>
                    <input
                        type="radio"
                        name="product-type"
                        checked={!hasVariants}
                        onChange={() => handleModeChange(false)}
                        disabled={isProductTypeLocked}
                        className="mt-1 border-primary-300 text-primary-600 focus:ring-primary-500"
                    />
                    <div>
                        <span className="font-medium text-gray-900">
                            {t('products.simpleProduct') || 'Simple Product'}
                        </span>
                        <p className="mt-1 text-sm text-gray-500">
                            {t('products.simpleModeHint') || 'One sellable item: purchase price, POS price and an optional separate storefront price.'}
                        </p>
                    </div>
                </label>

                <label className={`flex cursor-pointer items-start gap-3 rounded-xl border bg-white p-4 transition ${
                    hasVariants ? 'border-purple-300 ring-2 ring-purple-100' : 'border-gray-200 hover:border-gray-300'
                } ${isProductTypeLocked ? 'cursor-not-allowed opacity-70' : ''}`}>
                    <input
                        type="radio"
                        name="product-type"
                        checked={hasVariants}
                        onChange={() => handleModeChange(true)}
                        disabled={isProductTypeLocked}
                        className="mt-1 border-purple-300 text-purple-600 focus:ring-purple-500"
                    />
                    <div>
                        <span className="font-medium text-gray-900">
                            {t('products.variantProduct') || 'Product with Variants'}
                        </span>
                        <p className="mt-1 text-sm text-gray-500">
                            {t('products.variantModeHint') || 'Multiple option combinations: each variant gets its own SKU, stock, POS price and storefront price.'}
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
                    <label className="label" htmlFor={`${formId}-name`}>{t('products.productName')} *</label>
                    <input
                        id={`${formId}-name`}
                        type="text"
                        value={formData.name}
                        onChange={(e) => handleChange('name', e.target.value)}
                        className="input"
                        required
                        maxLength={300}
                    />
                </div>

                <div className="md:col-span-2">
                    {showArabicName ? (
                        <>
                            <label className="label" htmlFor={`${formId}-name-ar`}>{t('products.productNameAr')}</label>
                            <input
                                id={`${formId}-name-ar`}
                                type="text"
                                value={formData.nameAr}
                                onChange={(e) => handleChange('nameAr', e.target.value)}
                                className="input text-start"
                                dir="rtl"
                                maxLength={300}
                                placeholder={t('products.arabicNamePlaceholder')}
                            />
                            <p className="mt-1 text-xs text-gray-500">{t('products.arabicFallbackHint')}</p>
                        </>
                    ) : (
                        <button
                            type="button"
                            onClick={() => setShowArabicName(true)}
                            className="text-sm font-medium text-primary-600 hover:text-primary-800"
                        >
                            + {t('products.addArabicTranslation')}
                        </button>
                    )}
                </div>

                <div className="md:col-span-2">
                    <label className="label" htmlFor={`${formId}-category`}>{t('products.category')} *</label>
                    <select
                        id={`${formId}-category`}
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
                    <label className="label" htmlFor={`${formId}-description`}>{t('categories.description')}</label>
                    <textarea
                        id={`${formId}-description`}
                        value={formData.description}
                        onChange={(e) => handleChange('description', e.target.value)}
                        className="input"
                        rows="4"
                    />
                </div>

                <div className="md:col-span-2">
                    {showArabicDescription ? (
                        <>
                            <label className="label" htmlFor={`${formId}-description-ar`}>{t('products.descriptionAr')}</label>
                            <textarea
                                id={`${formId}-description-ar`}
                                value={formData.descriptionAr}
                                onChange={(e) => handleChange('descriptionAr', e.target.value)}
                                className="input text-start"
                                dir="rtl"
                                rows="4"
                                placeholder={t('products.arabicDescriptionPlaceholder')}
                            />
                            <p className="mt-1 text-xs text-gray-500">{t('products.arabicFallbackHint')}</p>
                        </>
                    ) : (
                        <button
                            type="button"
                            onClick={() => setShowArabicDescription(true)}
                            className="text-sm font-medium text-primary-600 hover:text-primary-800"
                        >
                            + {t('products.addArabicDescription')}
                        </button>
                    )}
                </div>

                <div>
                    <label className="label" htmlFor={`${formId}-tax-rate`}>{t('products.taxRate')}</label>
                    <input
                        id={`${formId}-tax-rate`}
                        type="number"
                        step="0.01"
                        min="0"
                        max="100"
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
                    <label className="label" htmlFor={`${formId}-sku`}>{t('products.sku') || 'SKU'}</label>
                    <div className="flex gap-2">
                        <input
                            id={`${formId}-sku`}
                            type="text"
                            value={simpleProductData.sku}
                            onChange={(e) => handleSimpleChange('sku', e.target.value)}
                            className="input flex-1"
                            placeholder={t('products.autoGenerated')}
                            maxLength={100}
                        />
                        <button type="button" onClick={handleGenerateSimpleSku} className="btn-secondary whitespace-nowrap">
                            {t('variants.generateSku', 'Generate SKU')}
                        </button>
                    </div>
                </div>

                <div>
                    <label className="label" htmlFor={`${formId}-barcode`}>{t('products.barcode')}</label>
                    <div className="flex gap-2">
                        <input
                            id={`${formId}-barcode`}
                            type="text"
                            value={simpleProductData.barcode}
                            onChange={(e) => handleSimpleChange('barcode', e.target.value)}
                            className="input flex-1"
                            placeholder={t('products.scanOrGenerate')}
                            maxLength={100}
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

                <div className="rounded-xl border border-emerald-200 bg-emerald-50 p-4">
                    <div className="grid gap-4 md:grid-cols-2">
                        <div>
                            <label className="label" htmlFor={`${formId}-storefront-price`}>
                                {t('products.storefrontPrice') || 'Storefront Price'}
                            </label>
                            <input
                                id={`${formId}-storefront-price`}
                                type="number"
                                min="0"
                                step="0.01"
                                value={simpleProductData.storefrontPrice}
                                onChange={(e) => handleSimpleChange('storefrontPrice', e.target.value)}
                                className="input"
                                placeholder={t('products.storefrontPricePlaceholder') || 'Same as POS price'}
                            />
                            <p className="mt-1 text-xs text-gray-500">
                                {t('products.storefrontPriceHint') || 'Optional. When set, the storefront, cart and checkout use this price. POS keeps using the default unit selling price. Leave empty to sell online at the POS price.'}
                            </p>
                        </div>
                        <div className="flex items-end pb-1">
                            <p className="text-xs text-gray-500">
                                {t('products.channelPricingNote') || 'Purchase price and POS selling price are managed per unit above.'}
                            </p>
                        </div>
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
                <ProductMediaManager productId={productId} onMediaChanged={handleMediaChanged} />
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
                <ProductVariantsTab
                    ref={variantsTabRef}
                    productId={productId}
                    productMediaRevision={productMediaRevision}
                    forceMatrixMode={hasVariants}
                    productSkuPrefix={formData.name}
                    onVariantDraftChange={handleVariantDraftChange}
                    onSaveVariantDraft={handleSaveVariantDraft}
                />
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
                        aria-label={`${translateWithFallback(t, 'products.productStatus', 'Product status')}: ${formData.isActive ? translateWithFallback(t, 'products.active', 'Active') : translateWithFallback(t, 'products.inactive', 'Inactive')}`}
                    />
                    <span className="text-sm font-medium text-gray-700">
                        {translateWithFallback(t, 'products.productStatus', 'Product status')}: {formData.isActive
                            ? translateWithFallback(t, 'products.active', 'Active')
                            : translateWithFallback(t, 'products.inactive', 'Inactive')}
                    </span>
                </label>
                {hasVariants && (
                    <p className="mt-3 text-sm text-gray-600" role="note">
                        {translateWithFallback(
                            t,
                            'products.variantLifecycleStatusNote',
                            'Product and variant statuses are independent. Deactivating or archiving this product deactivates its variants; activating the product later does not reactivate them. Review each variant status before publishing.'
                        )}
                    </p>
                )}
            </EmbeddedSection>

            <EmbeddedSection
                title={t('products.publicationSettings') || 'Storefront Settings'}
                description={t('products.isOnlineVisible') || 'Publication, merchandising, and storefront visibility.'}
            >
                <div className="grid gap-4">
                    <div>
                        <label className="label" htmlFor={`${formId}-publish-status`}>{t('products.publishStatus')}</label>
                        <select
                            id={`${formId}-publish-status`}
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
                        <label className="label" htmlFor={`${formId}-low-stock-threshold`}>{t('products.lowStockThreshold')}</label>
                        <input
                            id={`${formId}-low-stock-threshold`}
                            type="number"
                            min="0"
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
                        onChange={(e) => handleChange('slug', e.target.value)}
                        className="input font-mono"
                        placeholder="product-url-slug"
                    />
                    <p className="mt-1 text-xs text-gray-500">{t('products.slugHint')}</p>
                    {productId && formData.slug ? (
                        <p className="mt-1 text-xs text-amber-600">{t('products.slugEditWarning')}</p>
                    ) : null}
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
            const currentVariants = buildVariantAuthoringSnapshot()
            const representativeVariant = currentVariants.find((variant) => variant.isDefault)
                || currentVariants.find((variant) => variant.isActive !== false)
                || defaultVariant

            return toPositiveNumber(representativeVariant?.onlinePrice) || toPositiveNumber(representativeVariant?.price)
        }

        // WAVE 3: pixel tracks the storefront-facing value.
        return toPositiveNumber(simpleProductData.storefrontPrice) || toPositiveNumber(defaultUnitDraft?.sellingPrice)
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
            {!hasVariants && productUnitsSection}
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
        </div>
    )

    const workspaceSections = (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
            <div className="space-y-6">
                {productTypeSection}
                {basicInformationSection}
                {simpleIdentitySection}
                {variantCreateGuidanceSection}
                {!hasVariants && productUnitsSection}
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
            </div>
        </div>
    )

    return (
        <form id={formId} onSubmit={handleSubmit} className="space-y-6">
            {error && (
                <div
                    ref={(node) => node?.focus()}
                    id={`${formId}-error`}
                    className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-red-700"
                    role="alert"
                    tabIndex={-1}
                >
                    {error}
                </div>
            )}

            {isWorkspace ? workspaceSections : modalSections}
            {legacyActions}
        </form>
    )
})

export default ProductForm

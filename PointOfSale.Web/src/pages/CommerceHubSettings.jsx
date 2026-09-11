import { useState, useEffect, useCallback, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { fetchAllPagedArray } from '../services/pagedArrayLoader'
import { useToast } from '../components/Toast'
import CommissionPreviewPanel from '../pos-admin/commercehub/CommissionPreviewPanel'
import { useFormatting } from '../hooks/useFormatting'
import { useAuthStore } from '../store/authStore'
import {
    deriveCurrentVendorScopedArray,
    deriveCurrentVendorScopedDto,
} from '../auth/commerceHubVendorIdentityAuthority'
import { deriveCommerceHubVendorEligibilityPresentation } from '../auth/posVendorSessionAuthority'

const normalizeAttributeKey = (value) =>
    String(value || '')
        .trim()
        .toLowerCase()
        .replace(/[\s.-]+/g, '_')
        .replace(/[^a-z0-9_]/g, '')
        .replace(/_{2,}/g, '_')
        .replace(/^_+|_+$/g, '')

const hasAttributeValue = (value) => {
    if (value === null || value === undefined) return false
    if (typeof value === 'string') return value.trim() !== ''
    return true
}

const safeParseAttributes = (attributesJson) => {
    if (!attributesJson) return {}

    try {
        const parsed = typeof attributesJson === 'string'
            ? JSON.parse(attributesJson)
            : attributesJson

        if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
            return {}
        }

        return Object.entries(parsed).reduce((acc, [key, value]) => {
            const normalizedKey = normalizeAttributeKey(key)
            if (!normalizedKey) return acc
            acc[normalizedKey] = value
            return acc
        }, {})
    } catch (error) {
        console.warn('Failed to parse CommerceHub attributes JSON:', error)
        return {}
    }
}

const safeParseOptions = (optionsJson) => {
    if (!optionsJson) return []

    try {
        const parsed = JSON.parse(optionsJson)
        return Array.isArray(parsed) ? parsed : []
    } catch (error) {
        console.warn('Failed to parse CommerceHub attribute options JSON:', error)
        return []
    }
}

const resolveEffectivePrice = (variantPrice, commerceHubPrice) => {
    const resolvedValue = commerceHubPrice ?? variantPrice ?? 0
    return Number.isFinite(resolvedValue) ? Number(resolvedValue) : 0
}

const getCommerceHubErrorMessage = (error, fallbackMessage) =>
    error?.response?.data?.message || error?.message || fallbackMessage

const getCommerceHubCurrencyPolicyMessage = (variant) =>
    variant?.commerceHubCurrencyPolicyMessage ||
    `CommerceHub currently supports EGP products only. This product uses ${variant?.productCurrency || 'an unsupported currency'} and cannot be published.`

const formatProductCurrencyPrice = (formatCurrency, amount, currency) => {
    const normalizedCurrency = typeof currency === 'string' ? currency.trim() : ''
    if (!normalizedCurrency) {
        return 'Currency unavailable'
    }

    return formatCurrency(amount, normalizedCurrency)
}

/**
 * CommerceHub Settings — Admin Control Panel
 * 
 * Features:
 * - Product publishing table with publish/unpublish toggles
 * - Global category cascade selector (parent → child)
 * - Dynamic attribute form based on selected category
 * - Validation before publish
 */
export default function CommerceHubSettings() {
    const { t } = useTranslation()
    const { isRTL, formatCurrency, formatNumber } = useFormatting()
    const { showSuccess, showError } = useToast()
    const currentUser = useAuthStore((state) => state.user)
    const currentVendorId = currentUser?.tenantId || ''
    const vendorEligibility = deriveCommerceHubVendorEligibilityPresentation(currentUser)
    const canManageCatalog = vendorEligibility?.canManageCatalog === true

    const [loading, setLoading] = useState(true)
    const [products, setProducts] = useState([])
    const [listingSearch, setListingSearch] = useState('')
    const [listingFilters, setListingFilters] = useState({
        status: '',
        categoryId: '',
        inventoryStatus: '',
        overrideEnabled: '',
    })
    const [listingRows, setListingRows] = useState([])
    const [listingKpis, setListingKpis] = useState(null)
    const [listingAnalytics, setListingAnalytics] = useState(null)
    const [listingNotifications, setListingNotifications] = useState([])
    const [selectedListingIds, setSelectedListingIds] = useState({})
    const [bulkSaving, setBulkSaving] = useState(false)
    const [categories, setCategories] = useState([])
    const [saving, setSaving] = useState({}) // { [variantId]: true }
    
    // Expanded product rows
    const [expanded, setExpanded] = useState({})
    
    // Per-variant editing state: { [variantId]: { categoryId, commerceHubPrice, attributesJson, attributes: {} } }
    const [editState, setEditState] = useState({})
    
    // Loaded attribute definitions keyed by categoryId
    const [categoryAttributes, setCategoryAttributes] = useState({})
    const categoryAttributesRef = useRef({})
    const pendingAttributeLoadsRef = useRef({})

    useEffect(() => {
        categoryAttributesRef.current = categoryAttributes
    }, [categoryAttributes])

    // ─── Data Loading ────────────────────────────────────────
    const fetchData = useCallback(async () => {
        setLoading(true)
        setProducts([])
        setCategories([])
        setListingRows([])
        setListingKpis(null)
        setListingAnalytics(null)
        setListingNotifications([])
        setSelectedListingIds({})
        try {
            const listingParams = {
                page: 1,
                pageSize: 50,
            }
            if (listingSearch.trim()) listingParams.q = listingSearch.trim()
            if (listingFilters.status) listingParams.status = listingFilters.status
            if (listingFilters.categoryId) listingParams.categoryId = listingFilters.categoryId
            if (listingFilters.inventoryStatus) listingParams.inventoryStatus = listingFilters.inventoryStatus
            if (listingFilters.overrideEnabled) listingParams.overrideEnabled = listingFilters.overrideEnabled === 'true'

            const [productsRes, categoriesRes, listingsRes, kpisRes, analyticsRes, notificationsRes] = await Promise.all([
                fetchAllPagedArray('/admin/commercehub/products', {}, { pageSize: 50 }),
                api.get('/commercehub/categories/tree'),
                api.get('/admin/commercehub/listings', { params: listingParams }),
                api.get('/admin/commercehub/listings/kpis'),
                api.get('/admin/commercehub/listings/analytics'),
                api.get('/admin/commercehub/listings/notifications')
            ])
            const scopedProducts = deriveCurrentVendorScopedArray(productsRes || [], currentUser)
            const scopedListings = deriveCurrentVendorScopedArray(listingsRes.data || [], currentUser)
            const scopedKpis = deriveCurrentVendorScopedDto(kpisRes.data || null, currentUser)
            const scopedAnalytics = deriveCurrentVendorScopedDto(analyticsRes.data || null, currentUser)
            const scopedNotifications = deriveCurrentVendorScopedArray(notificationsRes.data || [], currentUser)

            setProducts(scopedProducts)
            setCategories(categoriesRes.data || [])
            setListingRows(scopedListings)
            setListingKpis(scopedKpis)
            setListingAnalytics(scopedAnalytics)
            setListingNotifications(scopedNotifications)
            
            // Initialize edit state from existing listings
            const state = {}
            for (const product of scopedProducts) {
                for (const variant of (product.variants || [])) {
                    if (variant.listing) {
                        state[variant.variantId] = {
                            categoryId: variant.listing.categoryId,
                            commerceHubPrice: variant.listing.commerceHubPrice,
                            priceOverrideMutationVersion: variant.listing.priceOverrideMutationVersion ?? 0,
                            attributesJson: variant.listing.attributesJson,
                            attributes: safeParseAttributes(variant.listing.attributesJson)
                        }
                    }
                }
            }
            setEditState(state)
        } catch (error) {
            console.error('Failed to load CommerceHub data:', error)
            showError(getCommerceHubErrorMessage(error, t('commerceHub.loadFailed')))
        } finally {
            setLoading(false)
        }
    }, [currentUser, listingFilters, listingSearch, showError, t])

    useEffect(() => { fetchData() }, [fetchData])

    // ─── Category Tree Helpers ───────────────────────────────
    const rootCategories = categories.filter(c => !c.parentId)
    const getChildren = (parentId) => categories.filter(c => c.parentId === parentId)
    const getCategoryName = (categoryId) => {
        if (!categoryId) return ''

        const category = categories.find(item => item.id === categoryId)
        if (!category) return ''

        return isRTL ? (category.nameAr || category.name) : category.name
    }

    // ─── Load attributes for a category ─────────────────────
    const loadAttributes = useCallback(async (categoryId) => {
        if (!categoryId || categoryAttributesRef.current[categoryId]) return categoryAttributesRef.current[categoryId]
        if (pendingAttributeLoadsRef.current[categoryId]) return pendingAttributeLoadsRef.current[categoryId]

        const request = (async () => {
            try {
                const res = await api.get(`/commercehub/categories/${categoryId}/attributes`)
                const nextAttributes = res.data || []
                setCategoryAttributes(prev => {
                    if (prev[categoryId]) return prev
                    return { ...prev, [categoryId]: nextAttributes }
                })
                return nextAttributes
            } catch (error) {
                console.error('Failed to load attributes:', error)
                showError(getCommerceHubErrorMessage(error, t('commerceHub.loadFailed')))
                return []
            } finally {
                delete pendingAttributeLoadsRef.current[categoryId]
            }
        })()

        pendingAttributeLoadsRef.current[categoryId] = request

        return request
    }, [showError, t])

    useEffect(() => {
        const categoryIds = [...new Set(
            Object.values(editState)
                .map(state => state?.categoryId)
                .filter(Boolean)
        )]

        if (categoryIds.length === 0) return

        let isCancelled = false

        ;(async () => {
            const uncachedCategoryIds = categoryIds.filter(categoryId =>
                !categoryAttributesRef.current[categoryId]
            )

            if (uncachedCategoryIds.length === 0) return

            await Promise.all(uncachedCategoryIds.map(categoryId => loadAttributes(categoryId)))

            if (isCancelled) return
        })()

        return () => {
            isCancelled = true
        }
    }, [editState, loadAttributes])

    // ─── Edit State Helpers ─────────────────────────────────
    const getEditState = (variantId) => editState[variantId] || { categoryId: '', commerceHubPrice: '', attributes: {} }
    
    const updateEditState = (variantId, updates) => {
        setEditState(prev => ({
            ...prev,
            [variantId]: { ...getEditState(variantId), ...updates }
        }))
    }

    const handleCategoryChange = async (variantId, categoryId) => {
        updateEditState(variantId, { categoryId, attributes: {}, _pendingParent: null })
        if (categoryId) {
            await loadAttributes(categoryId)
        }
    }

    const handleAttributeChange = (variantId, attrKey, value) => {
        const current = getEditState(variantId)
        updateEditState(variantId, {
            attributes: { ...current.attributes, [attrKey]: value }
        })
    }

    // ─── Local State Update Helper ──────────────────────────
    // ─── Publish / Update / Unpublish ───────────────────────
    const handlePublish = async (variant) => {
        if (!canManageCatalog) {
            showError('Current backend vendor eligibility does not allow catalog publishing.')
            return
        }

        if (variant.isCommerceHubCurrencyEligible !== true) {
            showError(getCommerceHubCurrencyPolicyMessage(variant))
            return
        }

        if (variant.isCommerceHubPublicationEligible === false) {
            showError(variant.commerceHubPublicationEligibilityMessage || t('commerceHub.publicationBlocked'))
            return
        }

        const state = getEditState(variant.variantId)
        if (!state.categoryId) {
            showError(t('commerceHub.categoryRequired'))
            return
        }

        // Validate required attributes
        const attrs = categoryAttributes[state.categoryId] || []
        for (const attr of attrs) {
            if (attr.isRequired && !hasAttributeValue(state.attributes[attr.key])) {
                showError(t('commerceHub.attributeRequired', {
                    attribute: isRTL ? (attr.nameAr || attr.name) : attr.name,
                }))
                return
            }
        }

        setSaving(prev => ({ ...prev, [variant.variantId]: true }))
        try {
            const normalizedAttributes = Object.entries(state.attributes || {}).reduce((acc, [key, value]) => {
                const normalizedKey = normalizeAttributeKey(key)
                if (!normalizedKey || !hasAttributeValue(value)) return acc
                acc[normalizedKey] = value
                return acc
            }, {})

            const payload = {
                variantId: variant.variantId,
                commerceHubCategoryId: state.categoryId,
                commerceHubPrice: variant.isCommerceHubPriceOverrideEnabled
                    ? state.commerceHubPrice || null
                    : null,
                expectedPriceOverrideMutationVersion: state.priceOverrideMutationVersion ?? 0,
                attributesJson: Object.keys(normalizedAttributes).length > 0
                    ? normalizedAttributes
                    : null
            }

            if (variant.listing?.isPublished) {
                await api.put('/admin/commercehub/update', payload)
                showSuccess(t('commerceHub.messages.updated'))
            } else {
                await api.post('/admin/commercehub/publish', payload)
                showSuccess(t('commerceHub.messages.published'))
            }
            
            await fetchData()
        } catch (error) {
            showError(getCommerceHubErrorMessage(error, t('commerceHub.messages.publishFailed')))
        } finally {
            setSaving(prev => ({ ...prev, [variant.variantId]: false }))
        }
    }

    const handleUnpublish = async (variant) => {
        if (!canManageCatalog) {
            showError('Current backend vendor eligibility does not allow catalog publishing.')
            return
        }

        setSaving(prev => ({ ...prev, [variant.variantId]: true }))
        try {
            await api.delete('/admin/commercehub/unpublish', {
                data: { variantId: variant.variantId }
            })
            showSuccess(t('commerceHub.messages.unpublished'))
            
            await fetchData()
        } catch (error) {
            showError(getCommerceHubErrorMessage(error, t('commerceHub.messages.unpublishFailed')))
        } finally {
            setSaving(prev => ({ ...prev, [variant.variantId]: false }))
        }
    }

    const toggleListingSelection = (listingId) => {
        setSelectedListingIds(prev => ({
            ...prev,
            [listingId]: !prev[listingId]
        }))
    }

    const selectedListingIdList = Object.entries(selectedListingIds)
        .filter(([, selected]) => selected)
        .map(([listingId]) => listingId)

    const handleBulkOperation = async (operation) => {
        if (!canManageCatalog) {
            showError('Current backend vendor eligibility does not allow catalog publishing.')
            return
        }

        if (selectedListingIdList.length === 0) {
            showError('Select at least one listing first.')
            return
        }

        setBulkSaving(true)
        try {
            const response = await api.post('/admin/commercehub/listings/bulk', {
                operation,
                listingIds: selectedListingIdList
            })
            const failed = response.data?.failed ?? 0
            if (failed > 0) {
                showError(`${failed} listing operations failed. Review row outcomes and refresh.`)
            } else {
                showSuccess('Bulk operation completed.')
            }
            setSelectedListingIds({})
            await fetchData()
        } catch (error) {
            showError(getCommerceHubErrorMessage(error, 'Bulk listing operation failed.'))
        } finally {
            setBulkSaving(false)
        }
    }

    // ─── Dynamic Attribute Form ─────────────────────────────
    const renderAttributeForm = (variantId, categoryId) => {
        const attrs = categoryAttributes[categoryId]
        if (!attrs || attrs.length === 0) return null
        const state = getEditState(variantId)

        return (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3 mt-3">
                {attrs.map(attr => {
                    const value = state.attributes[attr.key] ?? ''
                    const options = safeParseOptions(attr.optionsJson)

                    return (
                        <div key={attr.id} className="space-y-1">
                            <label className="text-xs font-medium text-gray-600 flex items-center gap-1">
                                {isRTL ? (attr.nameAr || attr.name) : attr.name}
                                {attr.isRequired && <span className="text-red-500">*</span>}
                            </label>
                            {attr.type === 'select' ? (
                                <select
                                    value={value}
                                    onChange={e => handleAttributeChange(variantId, attr.key, e.target.value)}
                                    className="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 bg-white"
                                >
                                    <option value="">{t('commerceHub.selectPlaceholder')}</option>
                                    {options.map(opt => (
                                        <option key={opt} value={opt}>{opt}</option>
                                    ))}
                                </select>
                            ) : attr.type === 'number' ? (
                                <input
                                    type="number"
                                    value={value}
                                    onChange={e => handleAttributeChange(variantId, attr.key, e.target.value)}
                                    className="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                                    placeholder={isRTL ? (attr.nameAr || attr.name) : attr.name}
                                />
                            ) : (
                                <input
                                    type="text"
                                    value={value}
                                    onChange={e => handleAttributeChange(variantId, attr.key, e.target.value)}
                                    className="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                                    placeholder={isRTL ? (attr.nameAr || attr.name) : attr.name}
                                />
                            )}
                        </div>
                    )
                })}
            </div>
        )
    }

    // ─── Category Cascade Selector ──────────────────────────
    const renderCategorySelector = (variantId) => {
        const state = getEditState(variantId)
        const selectedCat = categories.find(c => c.id === state.categoryId)
        
        // Determine parent selection
        const selectedParentId = selectedCat?.parentId || state.categoryId
        const children = getChildren(selectedParentId)
        const isLeafCategory = selectedCat && !categories.some(c => c.parentId === selectedCat.id)

        return (
            <div className="flex flex-wrap gap-2 items-end">
                {/* Root category */}
                <div className="min-w-[160px]">
                    <label className="text-xs font-medium text-gray-500 mb-1 block">
                        {t('commerceHub.category')}
                    </label>
                    <select
                        value={selectedCat?.parentId ? selectedCat.parentId : (state.categoryId || '')}
                        onChange={e => {
                            const parentId = e.target.value
                            const parentChildren = getChildren(parentId)
                            if (parentChildren.length > 0) {
                                // Has children — don't set yet, wait for sub-category
                                handleCategoryChange(variantId, '')
                                updateEditState(variantId, { _pendingParent: parentId })
                            } else {
                                handleCategoryChange(variantId, parentId)
                            }
                        }}
                        className="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 bg-white"
                    >
                        <option value="">{t('commerceHub.selectPlaceholder')}</option>
                        {rootCategories.map(cat => (
                            <option key={cat.id} value={cat.id}>
                                {cat.iconEmoji} {isRTL ? (cat.nameAr || cat.name) : cat.name}
                            </option>
                        ))}
                    </select>
                </div>

                {/* Sub-category (if parent has children) */}
                {(() => {
                    const parentId = selectedCat?.parentId || state._pendingParent
                    if (!parentId) return null
                    const subs = getChildren(parentId)
                    if (subs.length === 0) return null

                    return (
                        <div className="min-w-[160px]">
                            <label className="text-xs font-medium text-gray-500 mb-1 block">
                                {t('commerceHub.subcategory')}
                            </label>
                            <select
                                value={isLeafCategory ? state.categoryId : ''}
                                onChange={e => handleCategoryChange(variantId, e.target.value)}
                                className="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 bg-white"
                            >
                                <option value="">{t('commerceHub.selectPlaceholder')}</option>
                                {subs.map(cat => (
                                    <option key={cat.id} value={cat.id}>
                                        {cat.iconEmoji} {isRTL ? (cat.nameAr || cat.name) : cat.name}
                                    </option>
                                ))}
                            </select>
                        </div>
                    )
                })()}
            </div>
        )
    }

    // ─── Render ─────────────────────────────────────────────
    if (loading) {
        return (
            <div className="flex items-center justify-center py-20">
                <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-blue-600"></div>
            </div>
        )
    }

    const publishedCount = products.reduce((acc, p) => 
        acc + p.variants.filter(v => v.listing?.isPublished).length, 0)
    const totalVariants = products.reduce((acc, p) => acc + p.variants.length, 0)

    return (
        <div className="space-y-6" dir={isRTL ? 'rtl' : 'ltr'}>
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                    <div className="w-12 h-12 bg-gradient-to-br from-purple-500 to-indigo-600 rounded-xl flex items-center justify-center shadow-lg">
                        <span className="text-2xl">🏪</span>
                    </div>
                    <div>
                        <h2 className="text-xl font-bold text-gray-900">
                            {t('commerceHub.title')}
                        </h2>
                        <p className="text-sm text-gray-500">
                            {t('commerceHub.subtitle')}
                        </p>
                    </div>
                </div>
                <button
                    onClick={fetchData}
                    className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
                >
                    <span className="text-sm">{t('common.refresh')}</span>
                </button>
            </div>

            {/* Stats */}
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div className="bg-white border border-gray-200 rounded-xl p-4">
                    <div className="text-xs text-gray-500 uppercase tracking-wide">{t('commerceHub.stats.totalProducts')}</div>
                    <div className="text-2xl font-bold text-gray-900 mt-1">{formatNumber(products.length)}</div>
                </div>
                <div className="bg-white border border-gray-200 rounded-xl p-4">
                    <div className="text-xs text-gray-500 uppercase tracking-wide">{t('commerceHub.stats.totalVariants')}</div>
                    <div className="text-2xl font-bold text-gray-900 mt-1">{formatNumber(totalVariants)}</div>
                </div>
                <div className="bg-white border border-green-200 rounded-xl p-4">
                    <div className="text-xs text-green-600 uppercase tracking-wide">{t('commerceHub.stats.published')}</div>
                    <div className="text-2xl font-bold text-green-700 mt-1">{formatNumber(publishedCount)}</div>
                </div>
            </div>

            {/* Authoritative Listing Operations Console */}
            <div className="bg-white border border-gray-200 rounded-xl p-5 space-y-4">
                <div className="flex items-start justify-between gap-4 flex-wrap">
                    <div>
                        <h3 className="text-lg font-semibold text-gray-900">Listing operations console</h3>
                        <p className="text-sm text-gray-500">
                            SQL-authoritative search, filters, suppression visibility, orphan visibility, KPIs, analytics, notifications, and bulk operations.
                        </p>
                    </div>
                    <div className="flex gap-2">
                        <button
                            onClick={() => handleBulkOperation('unpublish')}
                            disabled={bulkSaving || selectedListingIdList.length === 0 || !canManageCatalog}
                            className="px-3 py-2 text-sm font-medium text-red-700 bg-red-50 border border-red-200 rounded-lg disabled:opacity-50"
                        >
                            Bulk unpublish
                        </button>
                        <button
                            onClick={() => handleBulkOperation('remove_override')}
                            disabled={bulkSaving || selectedListingIdList.length === 0 || !canManageCatalog}
                            className="px-3 py-2 text-sm font-medium text-blue-700 bg-blue-50 border border-blue-200 rounded-lg disabled:opacity-50"
                        >
                            Bulk remove overrides
                        </button>
                    </div>
                </div>

                <div className="grid grid-cols-2 md:grid-cols-6 gap-3">
                    {[
                        ['Total', listingKpis?.totalListings],
                        ['Published', listingKpis?.publishedListings],
                        ['Unpublished', listingKpis?.unpublishedListings],
                        ['Suppressed', listingKpis?.suppressedListings],
                        ['Override enabled', listingKpis?.overrideEnabledListings],
                        ['Orphaned', listingKpis?.orphanedListings],
                    ].map(([label, value]) => (
                        <div key={label} className="border border-gray-100 rounded-lg p-3">
                            <div className="text-xs uppercase text-gray-500">{label}</div>
                            <div className="text-xl font-bold text-gray-900">{formatNumber(value ?? 0)}</div>
                        </div>
                    ))}
                </div>

                <div className="grid grid-cols-1 md:grid-cols-5 gap-3">
                    <input
                        value={listingSearch}
                        onChange={event => setListingSearch(event.target.value)}
                        placeholder="Search product, variant, SKU, barcode, category"
                        className="md:col-span-2 px-3 py-2 text-sm border border-gray-300 rounded-lg"
                    />
                    <select
                        value={listingFilters.status}
                        onChange={event => setListingFilters(prev => ({ ...prev, status: event.target.value }))}
                        className="px-3 py-2 text-sm border border-gray-300 rounded-lg bg-white"
                    >
                        <option value="">All statuses</option>
                        <option value="published">Published</option>
                        <option value="unpublished">Unpublished</option>
                        <option value="suppressed">Suppressed</option>
                        <option value="orphaned">Orphaned</option>
                    </select>
                    <select
                        value={listingFilters.inventoryStatus}
                        onChange={event => setListingFilters(prev => ({ ...prev, inventoryStatus: event.target.value }))}
                        className="px-3 py-2 text-sm border border-gray-300 rounded-lg bg-white"
                    >
                        <option value="">All inventory</option>
                        <option value="in_stock">In stock</option>
                        <option value="out_of_stock">Out of stock</option>
                        <option value="unavailable">Unavailable</option>
                    </select>
                    <select
                        value={listingFilters.overrideEnabled}
                        onChange={event => setListingFilters(prev => ({ ...prev, overrideEnabled: event.target.value }))}
                        className="px-3 py-2 text-sm border border-gray-300 rounded-lg bg-white"
                    >
                        <option value="">All overrides</option>
                        <option value="true">Override enabled</option>
                        <option value="false">No override</option>
                    </select>
                </div>

                {listingNotifications.length > 0 && (
                    <div className="space-y-2">
                        {listingNotifications.slice(0, 3).map(notification => (
                            <div key={`${notification.listingId}-${notification.type}`} className="text-sm border border-amber-200 bg-amber-50 text-amber-800 rounded-lg px-3 py-2">
                                {notification.message}
                            </div>
                        ))}
                    </div>
                )}

                <div className="overflow-x-auto border border-gray-100 rounded-lg">
                    <table className="min-w-full text-sm">
                        <thead className="bg-gray-50 text-gray-600">
                            <tr>
                                <th className="px-3 py-2 text-left">Select</th>
                                <th className="px-3 py-2 text-left">Listing</th>
                                <th className="px-3 py-2 text-left">Status</th>
                                <th className="px-3 py-2 text-left">Category</th>
                                <th className="px-3 py-2 text-left">Inventory</th>
                                <th className="px-3 py-2 text-left">Override</th>
                            </tr>
                        </thead>
                        <tbody>
                            {listingRows.map(row => (
                                <tr key={row.listingId} className="border-t border-gray-100">
                                    <td className="px-3 py-2">
                                        <input
                                            type="checkbox"
                                            checked={!!selectedListingIds[row.listingId]}
                                            onChange={() => toggleListingSelection(row.listingId)}
                                        />
                                    </td>
                                    <td className="px-3 py-2">
                                        <div className="font-medium text-gray-900">{row.productName}</div>
                                        <div className="text-xs text-gray-500">{row.sku || row.barcode || row.variantId}</div>
                                    </td>
                                    <td className="px-3 py-2">
                                        <div className="font-medium">
                                            {row.isSuppressed ? 'Suppressed' : row.isOrphaned ? 'Orphaned' : row.isPublished ? 'Published' : 'Unpublished'}
                                        </div>
                                        {(row.suppressionReason || row.orphanReason) && (
                                            <div className="text-xs text-gray-500 max-w-xs">{row.suppressionReason || row.orphanReason}</div>
                                        )}
                                    </td>
                                    <td className="px-3 py-2">{row.categoryName || '-'}</td>
                                    <td className="px-3 py-2">
                                        {row.inventoryStatus} ({formatNumber(row.inventoryAvailable ?? 0)})
                                    </td>
                                    <td className="px-3 py-2">
                                        {row.overrideEnabled ? formatCurrency(row.commerceHubPrice || 0, 'EGP') : 'Base price'}
                                    </td>
                                </tr>
                            ))}
                            {listingRows.length === 0 && (
                                <tr>
                                    <td colSpan={6} className="px-3 py-8 text-center text-gray-500">
                                        No listings match the current search and filters.
                                    </td>
                                </tr>
                            )}
                        </tbody>
                    </table>
                </div>

                {listingAnalytics && (
                    <div className="text-xs text-gray-500">
                        Last {listingAnalytics.days} days: {formatNumber(listingAnalytics.listingViews)} listing views,
                        {' '}{formatNumber(listingAnalytics.listingSearches)} searches,
                        {' '}{formatNumber(listingAnalytics.listingConversions)} conversions. Authority: {listingAnalytics.authorityOwner}.
                    </div>
                )}
            </div>

            {/* Products Table */}
            {products.length === 0 ? (
                <div className="bg-white border border-gray-200 rounded-xl p-12 text-center">
                    <span className="text-4xl">📦</span>
                    <h3 className="text-lg font-medium text-gray-900 mt-3">{t('commerceHub.emptyTitle')}</h3>
                    <p className="text-sm text-gray-500 mt-1">
                        {t('commerceHub.emptyDescription')}
                    </p>
                </div>
            ) : (
                <div className="space-y-3">
                    {products.map(product => (
                        <div key={product.productId} className="bg-white border border-gray-200 rounded-xl overflow-hidden">
                            {/* Product Header (click to expand) */}
                            <button
                                onClick={() => setExpanded(prev => ({ ...prev, [product.productId]: !prev[product.productId] }))}
                                className="w-full flex items-center gap-4 px-5 py-4 hover:bg-gray-50 transition-colors"
                            >
                                {/* Product Image */}
                                <div className="w-10 h-10 bg-gray-100 rounded-lg overflow-hidden flex-shrink-0">
                                    {product.image ? (
                                        <img src={product.image} alt="" className="w-full h-full object-cover" />
                                    ) : (
                                        <div className="w-full h-full flex items-center justify-center text-gray-400">📷</div>
                                    )}
                                </div>
                                
                                <div className={`flex-1 ${isRTL ? 'text-right' : 'text-left'}`}>
                                    <div className="font-medium text-gray-900">{product.name}</div>
                                    <div className="text-xs text-gray-500">
                                        {t('commerceHub.variantCount', {
                                            count: product.variants.length,
                                            formattedCount: formatNumber(product.variants.length),
                                        })}
                                        {product.variants.some(v => v.listing?.isPublished) && (
                                            <span
                                                className="ml-2 inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700"
                                            >
                                                <span aria-hidden="true">●</span>
                                                <span>{t('commerceHub.publishedBadge')}</span>
                                            </span>
                                        )}
                                    </div>
                                </div>
                                
                                <svg
                                    className={`w-5 h-5 text-gray-400 transition-transform ${expanded[product.productId] ? 'rotate-180' : ''}`}
                                    fill="none" stroke="currentColor" viewBox="0 0 24 24"
                                >
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                                </svg>
                            </button>

                            {/* Expanded Variant Rows */}
                            {expanded[product.productId] && (
                                <div className="border-t border-gray-100">
                                    {product.variants.map(variant => {
                                        const state = getEditState(variant.variantId)
                                        const isPublished = variant.listing?.isPublished
                                        const isSaving = saving[variant.variantId]
                                        const isPriceOverrideEnabled = variant.isCommerceHubPriceOverrideEnabled === true
                                        const effectivePrice = resolveEffectivePrice(
                                            variant.price,
                                            isPriceOverrideEnabled ? state.commerceHubPrice : null,
                                        )
                                        const isOverridePriceActive = isPriceOverrideEnabled
                                            && state.commerceHubPrice !== null
                                            && state.commerceHubPrice !== undefined
                                        const selectedCategoryName = getCategoryName(state.categoryId)
                                        const isCurrencyBlocked = variant.isCommerceHubCurrencyEligible !== true
                                        const currencyPolicyMessage = isCurrencyBlocked
                                            ? getCommerceHubCurrencyPolicyMessage(variant)
                                            : null
                                        const isPublicationBlocked = variant.isCommerceHubPublicationEligible === false
                                        const publicationEligibilityMessage = isPublicationBlocked
                                            ? variant.commerceHubPublicationEligibilityMessage || t('commerceHub.publicationBlocked')
                                            : null

                                        return (
                                            <div key={variant.variantId}
                                                className={`px-5 py-4 border-b border-gray-50 last:border-b-0 ${
                                                    isPublished ? 'bg-green-50/30' : ''
                                                }`}
                                            >
                                                {/* Variant Info Row */}
                                                <div className="flex items-start gap-4 flex-wrap">
                                                    {/* Variant details */}
                                                    <div className="flex-1 min-w-[200px]">
                                                        <div className="flex items-center gap-2">
                                                            <span className="text-sm font-mono text-gray-600">{variant.sku}</span>
                                                            {isPublished && (
                                                                <span className="w-2 h-2 rounded-full bg-green-500"></span>
                                                            )}
                                                        </div>
                                                        {variant.attributeSummary && (
                                                            <div className="text-xs text-gray-500 mt-0.5">{variant.attributeSummary}</div>
                                                        )}
                                                        <div className="text-sm font-medium text-gray-900 mt-1">
                                                            {formatProductCurrencyPrice(formatCurrency, variant.price || 0, variant.productCurrency)}
                                                        </div>
                                                        {isCurrencyBlocked && (
                                                            <div className="mt-2 text-xs text-red-700 bg-red-50 border border-red-100 rounded-lg px-2 py-1">
                                                                {currencyPolicyMessage}
                                                            </div>
                                                        )}
                                                        {isPublicationBlocked && (
                                                            <div className="mt-2 text-xs text-amber-800 bg-amber-50 border border-amber-100 rounded-lg px-2 py-1">
                                                                {publicationEligibilityMessage}
                                                            </div>
                                                        )}
                                                    </div>

                                                    {/* Category Selector */}
                                                    <div className="flex-1 min-w-[320px]">
                                                        {renderCategorySelector(variant.variantId)}
                                                    </div>

                                                    {/* Price Override */}
                                                    <div className="min-w-[130px]">
                                                        <label className="text-xs font-medium text-gray-500 mb-1 block">
                                                            {t('commerceHub.priceOverride')}
                                                        </label>
                                                        <input
                                                            type="number"
                                                            step="0.01"
                                                            min="0"
                                                            value={isPriceOverrideEnabled ? state.commerceHubPrice ?? '' : ''}
                                                            onChange={e => updateEditState(variant.variantId, { commerceHubPrice: e.target.value ? parseFloat(e.target.value) : null })}
                                                            placeholder={isPriceOverrideEnabled ? t('commerceHub.autoPrice') : t('commerceHub.productPrice')}
                                                            disabled={!isPriceOverrideEnabled}
                                                            title={!isPriceOverrideEnabled ? t('commerceHub.priceOverrideDisabled') : undefined}
                                                            className="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:cursor-not-allowed disabled:bg-gray-100 disabled:text-gray-500"
                                                        />
                                                        {!isPriceOverrideEnabled && (
                                                            <p className="mt-1 max-w-[180px] text-xs leading-4 text-gray-500">
                                                                {t('commerceHub.priceOverrideDisabled')}
                                                            </p>
                                                        )}
                                                    </div>

                                                    {/* Actions */}
                                                    <div className="flex items-end gap-2 min-w-[180px]">
                                                        <button
                                                            onClick={() => handlePublish(variant)}
                                                            disabled={isSaving || !state.categoryId || isCurrencyBlocked || isPublicationBlocked || !canManageCatalog}
                                                            title={currencyPolicyMessage || publicationEligibilityMessage || undefined}
                                                            className={`px-4 py-2 text-sm font-medium rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${
                                                                isPublished
                                                                    ? 'bg-blue-600 hover:bg-blue-700 text-white'
                                                                    : 'bg-green-600 hover:bg-green-700 text-white'
                                                            }`}
                                                        >
                                                            <span className="inline-flex items-center gap-1">
                                                                <span aria-hidden="true">{isPublished ? '↻' : '✦'}</span>
                                                                <span>
                                                                    {isSaving ? t('common.saving') : isPublished ? t('commerceHub.update') : t('commerceHub.publish')}
                                                                </span>
                                                            </span>
                                                        </button>
                                                        {isPublished && (
                                                            <button
                                                                onClick={() => handleUnpublish(variant)}
                                                                disabled={isSaving || !canManageCatalog}
                                                                title={t('commerceHub.unpublish')}
                                                                className="px-3 py-2 text-sm font-medium text-red-600 bg-red-50 border border-red-200 rounded-lg hover:bg-red-100 transition-colors disabled:opacity-50"
                                                            >
                                                                <span className="inline-flex items-center gap-1">
                                                                    <span aria-hidden="true">✕</span>
                                                                    <span>{t('commerceHub.unpublish')}</span>
                                                                </span>
                                                            </button>
                                                        )}
                                                    </div>
                                                </div>

                                                <CommissionPreviewPanel
                                                    vendorId={currentVendorId}
                                                    categoryId={state.categoryId || null}
                                                    categoryName={selectedCategoryName || null}
                                                    price={effectivePrice}
                                                    priceLabel={isOverridePriceActive ? t('commerceHub.commerceHubPrice') : t('commerceHub.productPrice')}
                                                    currencyCode="EGP"
                                                    onApplySuggestedPrice={isPriceOverrideEnabled
                                                        ? (suggestedPrice) => updateEditState(variant.variantId, { commerceHubPrice: suggestedPrice })
                                                        : undefined}
                                                />

                                                {/* Dynamic Attributes (shown when category is selected) */}
                                                {state.categoryId && renderAttributeForm(variant.variantId, state.categoryId)}
                                            </div>
                                        )
                                    })}
                                </div>
                            )}
                        </div>
                    ))}
                </div>
            )}
        </div>
    )
}

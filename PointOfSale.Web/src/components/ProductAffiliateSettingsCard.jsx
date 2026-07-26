import { useState, useEffect, useMemo, useCallback, useRef } from 'react'
import adminProductAffiliateApi from '../services/adminProductAffiliateApi'
import { useAuthStore } from '../store/authStore'
import { useAffiliateReadiness } from '../hooks/useAffiliateReadiness'
import AffiliateReadinessWarning from './AffiliateReadinessWarning'

/**
 * AF-4C + AF-30A + AF-30B: Affiliate Settings Card — embedded in ProductForm.
 *
 * STATE MACHINE:
 * - Loading    → fetch settings + category tree
 * - Disabled   → category dropdown + rate form + "Enable Affiliate" button
 * - Enabled    → category dropdown + rate form + "Save" + "Disable" buttons
 * - Saving     → locked form + spinner
 * - Error      → server message displayed, form NOT cleared
 *
 * RULES:
 * - Only shown when productId exists (edit mode) AND user is Owner/Admin
 * - AF-23: Checks readiness before allowing affiliate enable
 * - AF-30A: Category is REQUIRED to enable affiliate
 * - AF-30B: Category dropdown is tree-aware (Main headers not selectable)
 * - Commission rate displayed as percentage (0.10 → 10%)
 * - Validation: rate 0.01% – 50%, max cap ≥ 0
 * - GAP-2: Inactive categories shown as disabled with warning badge
 * - GAP-6: Uses enableWithCategory() instead of deprecated enable()
 */
export default function ProductAffiliateSettingsCard({ productId }) {
    const user = useAuthStore(state => state.user)
    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)
    const [error, setError] = useState('')
    const [success, setSuccess] = useState('')

    // AF-23: Readiness check
    const readiness = useAffiliateReadiness()

    // Affiliate state
    const [isEnabled, setIsEnabled] = useState(false)
    const [commissionRate, setCommissionRate] = useState('')
    const [maxCommissionPerItem, setMaxCommissionPerItem] = useState('')
    const [selectedCategoryId, setSelectedCategoryId] = useState(null)
    const [templateType, setTemplateType] = useState('')
    const [authoritySettings, setAuthoritySettings] = useState(null)
    const [dirtyFields, setDirtyFields] = useState({})
    const settingsRequestSeq = useRef(0)
    const categoryTreeRequestSeq = useRef(0)
    const mutationRequestSeq = useRef(0)
    const mutationInFlightRef = useRef(false)
    const authoritySettingsRef = useRef(null)
    const dirtyFieldsRef = useRef({})

    // AF-30B: Category tree state
    const [categoryTree, setCategoryTree] = useState([])
    const [categoryTreeLoading, setCategoryTreeLoading] = useState(true)

    // Only show for Owner/Admin roles
    const isAdmin = user?.role === 'Owner' || user?.role === 'Admin'

    // ─── Resolve selected category from tree cache (GAP-3) ────────
    const resolvedCategory = useMemo(() => {
        if (!selectedCategoryId || !categoryTree.length) return null
        for (const main of categoryTree) {
            if (main.id === selectedCategoryId) {
                return { ...main, parentName: null, parentSlug: null }
            }
            const sub = main.subcategories?.find(s => s.id === selectedCategoryId)
            if (sub) {
                return { ...sub, parentName: main.name, parentSlug: main.slug }
            }
        }
        return null // Category not found in tree (may have been deleted)
    }, [selectedCategoryId, categoryTree])

    // Is selected category inactive? (GAP-2 handling)
    const isCategoryInactive = resolvedCategory && !resolvedCategory.isActive
    const isCategoryMissing = selectedCategoryId && !resolvedCategory
    const isDirty = Object.values(dirtyFields).some(Boolean)
    const acceptedIsEnabled = Boolean(authoritySettings?.isEnabled)
    const unsavedChangesMessage = 'You have unsaved affiliate configuration changes. Leave without saving?'

    const normalizeSettings = useCallback((data) => ({
        isEnabled: Boolean(data?.isEnabled),
        commissionRate: data?.commissionRate != null ? (data.commissionRate * 100).toFixed(2) : '',
        maxCommissionPerItem: data?.maxCommissionPerItem != null ? data.maxCommissionPerItem.toString() : '',
        selectedCategoryId: data?.affiliateCategoryId || null,
        templateType: data?.templateType || '',
    }), [])

    const assertCurrentProductSettings = useCallback((data) => {
        if (`${data?.productId ?? ''}`.toLowerCase() !== `${productId}`.toLowerCase()) {
            throw new Error('Affiliate settings response did not match the current product.')
        }
    }, [productId])

    const applyFormSettings = useCallback((settings) => {
        setIsEnabled(settings.isEnabled)
        setCommissionRate(settings.commissionRate)
        setMaxCommissionPerItem(settings.maxCommissionPerItem)
        setSelectedCategoryId(settings.selectedCategoryId)
        setTemplateType(settings.templateType)
    }, [])

    const clearDirtyState = useCallback(() => {
        dirtyFieldsRef.current = {}
        setDirtyFields({})
    }, [])

    const applyAuthoritativeSettings = useCallback((data, { force = false } = {}) => {
        assertCurrentProductSettings(data)
        const nextSettings = normalizeSettings(data)
        authoritySettingsRef.current = nextSettings
        setAuthoritySettings(nextSettings)

        if (force || !Object.values(dirtyFieldsRef.current).some(Boolean)) {
            applyFormSettings(nextSettings)
            clearDirtyState()
        }
    }, [applyFormSettings, assertCurrentProductSettings, clearDirtyState, normalizeSettings])

    const markFieldDirty = useCallback((field, value) => {
        const baseline = authoritySettingsRef.current
        const isFieldDirty = baseline ? value !== baseline[field] : true
        const nextDirty = { ...dirtyFieldsRef.current, [field]: isFieldDirty }

        if (!isFieldDirty) {
            delete nextDirty[field]
        }

        dirtyFieldsRef.current = nextDirty
        setDirtyFields(nextDirty)
    }, [])

    const resetToAuthoritativeSettings = useCallback(() => {
        if (!authoritySettingsRef.current) return
        applyFormSettings(authoritySettingsRef.current)
        clearDirtyState()
        setError('')
        setSuccess('')
    }, [applyFormSettings, clearDirtyState])

    const beginMutation = useCallback(() => {
        if (mutationInFlightRef.current) return null

        mutationInFlightRef.current = true
        settingsRequestSeq.current += 1
        const currentMutation = ++mutationRequestSeq.current
        setSaving(true)
        return currentMutation
    }, [])

    const applyAcceptedMutationSettings = useCallback((data) => {
        settingsRequestSeq.current += 1
        applyAuthoritativeSettings(data, { force: true })
    }, [applyAuthoritativeSettings])

    const isCurrentMutation = useCallback((currentMutation) => (
        currentMutation === mutationRequestSeq.current
    ), [])

    const finishMutation = useCallback((currentMutation) => {
        if (!isCurrentMutation(currentMutation)) return

        mutationInFlightRef.current = false
        setSaving(false)
    }, [isCurrentMutation])

    // ─── Fetch on mount ──────────────────────────────────────────
    const fetchSettings = useCallback(async () => {
        const currentRequest = ++settingsRequestSeq.current
        try {
            const data = await adminProductAffiliateApi.getSettings(productId)
            if (currentRequest === settingsRequestSeq.current) {
                applyAuthoritativeSettings(data)
                setError('')
            }
        } catch (err) {
            console.error('Failed to load affiliate settings', err)
            if (currentRequest === settingsRequestSeq.current && !authoritySettingsRef.current) {
                setAuthoritySettings(null)
                setError('Unable to load current affiliate settings. Refresh and try again.')
            }
        }
    }, [applyAuthoritativeSettings, productId])

    const fetchCategoryTree = useCallback(async () => {
        const currentRequest = ++categoryTreeRequestSeq.current
        try {
            setCategoryTreeLoading(true)
            const data = await adminProductAffiliateApi.getCategoryTree()
            if (currentRequest === categoryTreeRequestSeq.current) {
                setCategoryTree(data || [])
            }
        } catch (err) {
            console.error('Failed to load category tree', err)
            if (currentRequest === categoryTreeRequestSeq.current) {
                setCategoryTree([])
            }
        } finally {
            if (currentRequest === categoryTreeRequestSeq.current) {
                setCategoryTreeLoading(false)
            }
        }
    }, [])

    useEffect(() => {
        if (productId && isAdmin) {
            let active = true
            authoritySettingsRef.current = null
            mutationRequestSeq.current += 1
            mutationInFlightRef.current = false
            setAuthoritySettings(null)
            clearDirtyState()
            setError('')
            setSuccess('')
            setSaving(false)
            setLoading(true)
            Promise.all([fetchSettings(), fetchCategoryTree()]).finally(() => {
                if (active) setLoading(false)
            })

            return () => {
                active = false
                settingsRequestSeq.current += 1
                categoryTreeRequestSeq.current += 1
            }
        }
    }, [productId, isAdmin, fetchSettings, fetchCategoryTree, clearDirtyState])

    useEffect(() => {
        if (!productId || !isAdmin) return undefined

        const refetchCurrentAuthority = () => {
            void fetchSettings()
            void fetchCategoryTree()
            void readiness.refetch?.({ showLoading: false })
        }

        const handleVisibility = () => {
            if (document.visibilityState === 'visible') refetchCurrentAuthority()
        }

        window.addEventListener('focus', refetchCurrentAuthority)
        window.addEventListener('online', refetchCurrentAuthority)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            window.removeEventListener('focus', refetchCurrentAuthority)
            window.removeEventListener('online', refetchCurrentAuthority)
            document.removeEventListener('visibilitychange', handleVisibility)
        }
    }, [productId, isAdmin, fetchSettings, fetchCategoryTree, readiness.refetch])

    useEffect(() => {
        if (!isDirty) return undefined

        const handleBeforeUnload = (event) => {
            event.preventDefault()
            event.returnValue = ''
        }

        window.addEventListener('beforeunload', handleBeforeUnload)

        return () => {
            window.removeEventListener('beforeunload', handleBeforeUnload)
        }
    }, [isDirty])

    useEffect(() => {
        if (!isDirty) return undefined

        window.history.pushState({ affiliateUnsavedChangesGuard: true }, '', window.location.href)

        const handlePopState = () => {
            if (window.confirm(unsavedChangesMessage)) {
                window.removeEventListener('popstate', handlePopState)
                setTimeout(() => window.history.back(), 0)
                return
            }

            window.history.pushState({ affiliateUnsavedChangesGuard: true }, '', window.location.href)
        }

        window.addEventListener('popstate', handlePopState)

        return () => {
            window.removeEventListener('popstate', handlePopState)
        }
    }, [isDirty, unsavedChangesMessage])

    // ─── Reconcile: always re-fetch after any mutation ────────────
    const reconcileAfterMutation = async () => {
        await fetchSettings()
    }

    const reconcileAfterError = async () => {
        await Promise.all([fetchSettings(), fetchCategoryTree()])
    }

    // ─── Enable Affiliate (GAP-6 FIX: uses enableWithCategory) ───
    const handleEnable = async () => {
        setError('')
        setSuccess('')

        // AF-23: Block if not ready
        if (!readiness.isReady) {
            setError('Affiliate system is not ready. Complete the setup requirements first.')
            return
        }

        // AF-30A: Category required
        if (!selectedCategoryId) {
            setError('Please select a marketplace category before enabling affiliate marketing.')
            return
        }

        // GAP-2: Block enable if selected category is inactive
        if (isCategoryInactive) {
            setError('The selected category is inactive. Please select a different category.')
            return
        }

        // Parse rate
        const ratePercent = parseFloat(commissionRate)
        if (isNaN(ratePercent) || ratePercent < 0.01 || ratePercent > 50) {
            setError('Commission rate must be between 0.01% and 50%')
            return
        }
        const rate = ratePercent / 100

        const maxCap = maxCommissionPerItem ? parseFloat(maxCommissionPerItem) : null
        if (maxCap !== null && maxCap <= 0) {
            setError('Max commission per item must be positive')
            return
        }

        const currentMutation = beginMutation()
        if (!currentMutation) return

        try {
            const result = await adminProductAffiliateApi.enableWithCategory(
                productId, rate, selectedCategoryId, maxCap
            )
            if (!isCurrentMutation(currentMutation)) return

            applyAcceptedMutationSettings(result)

            setSuccess('Affiliate marketing enabled successfully')
            setTimeout(() => setSuccess(''), 3000)
        } catch (err) {
            if (!isCurrentMutation(currentMutation)) return

            const msg = err.response?.data?.message || 'Failed to enable affiliate'
            setError(msg)
            // Race condition: re-fetch tree + settings on error
            await reconcileAfterError()
        } finally {
            finishMutation(currentMutation)
        }
    }

    // ─── Save Changes (update rate + category when already enabled) ──
    const handleSave = async () => {
        setError('')
        setSuccess('')

        if (!selectedCategoryId) {
            setError('Category is required while affiliate marketing is enabled.')
            return
        }

        if (isCategoryInactive) {
            setError('The selected category is inactive. Please select a different category.')
            return
        }

        const ratePercent = parseFloat(commissionRate)
        if (isNaN(ratePercent) || ratePercent < 0.01 || ratePercent > 50) {
            setError('Commission rate must be between 0.01% and 50%')
            return
        }
        const rate = ratePercent / 100

        const maxCap = maxCommissionPerItem ? parseFloat(maxCommissionPerItem) : null
        if (maxCap !== null && maxCap <= 0) {
            setError('Max commission per item must be positive')
            return
        }

        const currentMutation = beginMutation()
        if (!currentMutation) return

        try {
            const result = await adminProductAffiliateApi.enableWithCategory(
                productId, rate, selectedCategoryId, maxCap
            )
            if (!isCurrentMutation(currentMutation)) return

            applyAcceptedMutationSettings(result)

            setSuccess('Affiliate settings saved successfully')
            setTimeout(() => setSuccess(''), 3000)
        } catch (err) {
            if (!isCurrentMutation(currentMutation)) return

            const msg = err.response?.data?.message || 'Failed to save affiliate settings'
            setError(msg)
            await reconcileAfterError()
        } finally {
            finishMutation(currentMutation)
        }
    }

    // ─── Disable Affiliate ──────────────────────────────────────
    const handleDisable = async () => {
        setError('')
        setSuccess('')

        const currentMutation = beginMutation()
        if (!currentMutation) return

        try {
            const result = await adminProductAffiliateApi.disable(productId)
            if (!isCurrentMutation(currentMutation)) return

            applyAcceptedMutationSettings(result)

            setSuccess('Affiliate marketing disabled')
            setTimeout(() => setSuccess(''), 3000)
        } catch (err) {
            if (!isCurrentMutation(currentMutation)) return

            setError(err.response?.data?.message || 'Failed to disable affiliate')
            await reconcileAfterMutation()
        } finally {
            finishMutation(currentMutation)
        }
    }

    if (!isAdmin || !productId) return null

    // ─── Loading State ──────────────────────────────────────────
    if (loading || readiness.loading) {
        return (
            <div className="card border-2 border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50">
                <div className="flex items-center gap-3 animate-pulse">
                    <div className="w-12 h-12 bg-amber-100 rounded-xl"></div>
                    <div className="flex-1">
                        <div className="h-5 bg-amber-100 rounded w-48 mb-2"></div>
                        <div className="h-3 bg-amber-100 rounded w-64"></div>
                    </div>
                </div>
            </div>
        )
    }

    if (!authoritySettings && readiness.isReady) {
        return (
            <div className="card border-2 border-red-200 bg-red-50">
                <div className="flex items-start gap-4">
                    <div className="flex-shrink-0 w-12 h-12 bg-red-100 rounded-xl flex items-center justify-center">
                        <span className="text-2xl">!</span>
                    </div>
                    <div className="flex-1">
                        <h3 className="text-lg font-semibold text-red-900 mb-0.5">
                            Affiliate Marketing
                        </h3>
                        <p className="text-sm text-red-700">
                            {error || 'Current affiliate settings are unavailable.'}
                        </p>
                    </div>
                </div>
            </div>
        )
    }

    // AF-23: Show readiness warning if not ready
    if (!readiness.isReady) {
        return (
            <div className="card border-2 border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50">
                <div className="flex items-start gap-4 mb-4">
                    <div className="flex-shrink-0 w-12 h-12 bg-amber-100 rounded-xl flex items-center justify-center">
                        <span className="text-2xl">🤝</span>
                    </div>
                    <div className="flex-1">
                        <h3 className="text-lg font-semibold text-amber-900 mb-0.5">
                            Affiliate Marketing
                        </h3>
                        <p className="text-sm text-amber-700">
                            Enable commission-based referrals for this product
                        </p>
                    </div>
                </div>
                <AffiliateReadinessWarning
                    storefrontLive={readiness.storefrontLive}
                    hasPaymentMethod={readiness.hasPaymentMethod}
                    hasShippingMethod={readiness.hasShippingMethod}
                    missingRequirements={readiness.missingRequirements}
                    variant="inline"
                />
            </div>
        )
    }

    return (
        <div className="card border-2 border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50">
            {/* ═══ Header ═══ */}
            <div className="flex items-start gap-4">
                <div className="flex-shrink-0 w-12 h-12 bg-amber-100 rounded-xl flex items-center justify-center">
                    <span className="text-2xl">🤝</span>
                </div>
                <div className="flex-1">
                    <div className="flex items-center justify-between">
                        <div>
                            <h3 className="text-lg font-semibold text-amber-900 mb-0.5">
                                Affiliate Marketing
                            </h3>
                            <p className="text-sm text-amber-700">
                                Enable commission-based referrals for this product
                            </p>
                        </div>
                        {/* Status Badge */}
                        <span
                            className={`inline-flex items-center gap-1 px-2.5 py-0.5 text-xs font-medium rounded-full ${acceptedIsEnabled ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-700'}`}
                        >
                            <span className={`w-1.5 h-1.5 rounded-full ${acceptedIsEnabled ? 'bg-green-500' : 'bg-gray-400'}`}></span>
                            {acceptedIsEnabled ? 'Current: Active' : 'Current: Inactive'}
                        </span>
                    </div>
                </div>
            </div>

            {/* ═══ Settings Form ═══ */}
            <div className="mt-4 pt-4 border-t border-amber-200">
                {/* ── Category Dropdown (AF-30B) ── */}
                <div className="mb-4">
                    <label className="label" htmlFor="affiliate-category-select">Marketplace Category *</label>
                    <select
                        value={selectedCategoryId || ''}
                        onChange={(e) => {
                            const nextCategoryId = e.target.value || null
                            setSelectedCategoryId(nextCategoryId)
                            markFieldDirty('selectedCategoryId', nextCategoryId)
                            setError('')
                        }}
                        className={`input w-full ${isCategoryInactive ? 'border-amber-500' : ''} ${isCategoryMissing ? 'border-red-500' : ''}`}
                        disabled={saving || categoryTreeLoading}
                        id="affiliate-category-select"
                        aria-describedby="affiliate-category-help"
                        aria-invalid={Boolean(isCategoryInactive || isCategoryMissing)}
                    >
                        <option value="">— Select a category —</option>
                        {categoryTree.length === 0 && (
                            <option value="" disabled>No categories available</option>
                        )}
                        {categoryTree.map(main => (
                            <optgroup key={main.id} label={`${main.isActive ? '📁' : '📁 ⚠️'} ${main.name}`}>
                                {main.subcategories?.map(sub => (
                                    <option
                                        key={sub.id}
                                        value={sub.id}
                                        disabled={!sub.isActive}
                                    >
                                        {sub.name}{!sub.isActive ? ' (inactive)' : ''}
                                    </option>
                                ))}
                                {(!main.subcategories || main.subcategories.length === 0) && (
                                    <option value="" disabled>No subcategories</option>
                                )}
                            </optgroup>
                        ))}
                    </select>

                    {/* Category status indicators */}
                    <p
                        id="affiliate-category-help"
                        className={`text-xs mt-1 ${isCategoryInactive ? 'text-amber-600' : isCategoryMissing ? 'text-red-600' : 'text-gray-500'}`}
                    >
                        {isCategoryInactive
                            ? 'Selected category is inactive. Choose a different category or contact the platform owner.'
                            : isCategoryMissing
                                ? 'Previously assigned category no longer exists. Please select a new category.'
                                : resolvedCategory?.isActive
                                    ? `${resolvedCategory.parentName ? `${resolvedCategory.parentName} -> ` : ''}${resolvedCategory.name}`
                                    : categoryTree.length === 0 && !categoryTreeLoading
                                        ? 'No categories available. Contact the platform owner to set up marketplace categories.'
                                        : 'Choose the marketplace category this product should use for affiliate referrals.'}
                    </p>
                </div>

                {/* ── Commission Rate + Max Cap ── */}
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                        <label className="label" htmlFor="affiliate-commission-rate">Commission Rate (%) *</label>
                        <div className="relative">
                            <input
                                type="number"
                                step="0.01"
                                min="0.01"
                                max="50"
                                value={commissionRate}
                                onChange={(e) => {
                                    setCommissionRate(e.target.value)
                                    markFieldDirty('commissionRate', e.target.value)
                                }}
                                className="input pr-8"
                                placeholder="e.g. 10.00"
                                disabled={saving}
                                id="affiliate-commission-rate"
                                aria-describedby="affiliate-commission-rate-help"
                            />
                            <span className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 text-sm">%</span>
                        </div>
                        <p id="affiliate-commission-rate-help" className="text-xs text-gray-500 mt-1">
                            Percentage of paid amount earned by affiliates (0.01% – 50%)
                        </p>
                    </div>
                    <div>
                        <label className="label" htmlFor="affiliate-max-commission">Max Commission Per Item</label>
                        <input
                            type="number"
                            step="0.01"
                            min="0"
                            value={maxCommissionPerItem}
                            onChange={(e) => {
                                setMaxCommissionPerItem(e.target.value)
                                markFieldDirty('maxCommissionPerItem', e.target.value)
                            }}
                            className="input"
                            placeholder="No cap"
                            disabled={saving}
                            id="affiliate-max-commission"
                            aria-describedby="affiliate-max-commission-help"
                        />
                        <p id="affiliate-max-commission-help" className="text-xs text-gray-500 mt-1">
                            Optional cap on commission per item. Leave empty for no limit.
                        </p>
                    </div>
                </div>

                {/* ── Info Notice ── */}
                <div className="mt-3 p-3 bg-blue-50 border border-blue-200 rounded-lg">
                    <p className="text-xs text-blue-700">
                        💡 Commission is calculated at order time. Changing the rate does not affect past orders or existing earned commissions.
                    </p>
                </div>

                {/* ── Error / Success Messages ── */}
                {error && (
                    <div className="mt-3 p-3 bg-red-50 border border-red-200 text-red-700 text-sm rounded-lg" role="alert">
                        ⚠️ {error}
                    </div>
                )}
                {success && (
                    <div className="mt-3 p-3 bg-green-50 border border-green-200 text-green-700 text-sm rounded-lg" role="status" aria-live="polite">
                        ✅ {success}
                    </div>
                )}

                {/* ═══ Action Buttons ═══ */}
                {isDirty && (
                    <div className="mt-3 p-3 bg-amber-50 border border-amber-200 text-amber-700 text-sm rounded-lg" role="status" aria-live="polite">
                        Unsaved changes. Save to apply them, or use Reset Changes to discard them.
                    </div>
                )}

                <div className="mt-4 flex justify-end gap-2">
                    {isDirty && (
                        <button
                            type="button"
                            onClick={resetToAuthoritativeSettings}
                            disabled={saving}
                            className="btn-secondary flex items-center gap-2"
                            id="affiliate-reset-btn"
                        >
                            Reset Changes
                        </button>
                    )}

                    {/* Disable button — only visible when enabled */}
                    {acceptedIsEnabled && (
                        <button
                            type="button"
                            onClick={handleDisable}
                            disabled={saving}
                            className="btn-secondary flex items-center gap-2 text-red-600 border-red-200 hover:bg-red-50"
                            id="affiliate-disable-btn"
                        >
                            {saving ? (
                                <span className="animate-spin h-4 w-4 border-2 border-red-400 border-t-transparent rounded-full"></span>
                            ) : '🚫'} Disable Affiliate
                        </button>
                    )}

                    {/* Enable button — only visible when disabled */}
                    {!acceptedIsEnabled && (
                        <button
                            type="button"
                            onClick={handleEnable}
                            disabled={saving || !commissionRate || !selectedCategoryId || isCategoryInactive}
                            className="btn-primary flex items-center gap-2"
                            id="affiliate-enable-btn"
                        >
                            {saving ? (
                                <>
                                    <span className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full"></span>
                                    Enabling...
                                </>
                            ) : (
                                <>🚀 Enable Affiliate Marketing</>
                            )}
                        </button>
                    )}

                    {/* Save button — only visible when enabled */}
                    {acceptedIsEnabled && (
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={saving || !commissionRate || !selectedCategoryId || isCategoryInactive}
                            className="btn-primary flex items-center gap-2"
                            id="affiliate-save-btn"
                        >
                            {saving ? (
                                <>
                                    <span className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full"></span>
                                    Saving...
                                </>
                            ) : (
                                <>💾 Save Changes</>
                            )}
                        </button>
                    )}
                </div>
            </div>
        </div>
    )
}

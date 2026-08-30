import { useState, useEffect, useMemo, useCallback, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import adminProductAffiliateApi from '../services/adminProductAffiliateApi'
import { useAuthStore } from '../store/authStore'
import { useAffiliateReadiness } from '../hooks/useAffiliateReadiness'
import AffiliateReadinessWarning from './AffiliateReadinessWarning'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'

/**
 * AF-4C + AF-30A + AF-30B, Shopixy Affiliate Refactor C: Affiliate Settings Card.
 * Legacy compatibility component reused from ProductForm history and embedded in
 * the Affiliate Products management dialog only.
 *
 * STATE MACHINE:
 * - Loading    → fetch settings + category tree
 * - Disabled   → category dropdown + rate form + "Enable Affiliate" button
 * - Enabled    → category dropdown + rate form + "Save" + "Disable" buttons
 * - Saving     → locked form + spinner
 * - Error      → localized safe message displayed, form NOT cleared
 *
 * RULES:
 * - Only rendered when productId exists (edit mode) AND user is Owner/Admin
 * - AF-23: Checks readiness before allowing affiliate enable
 * - AF-30A: Category is REQUIRED to enable affiliate
 * - AF-30B: Category dropdown is tree-aware; inactive child-or-parent options are
 *   disabled. Categories are read/select-only global master data here.
 * - Commission rate displayed as percentage (0.10 → 10%), backend remains authoritative
 * - Validation: blank/non-numeric/non-finite/out-of-range/excess-precision inputs are
 *   rejected client-side; an empty max cap stays null and is never coerced to zero
 * - Mutations are serialized with a synchronous ref lock: double click / Enter /
 *   slow network produces ONE semantic mutation. No timed unlock, no auto-retries.
 * - Responses are guarded by product identity + request sequence; results for
 *   Product A can never update Product B.
 * - Backend exception/message text is never rendered: failures map to localized,
 *   stable UI wording keyed by response status class.
 */
const RATE_MIN_PERCENT = 0.01
const RATE_MAX_PERCENT = 50
// decimal(8,6) stores six fraction digits, which becomes four digits when shown as a percent.
const RATE_MAX_DECIMAL_PLACES = 4
const MAX_COMMISSION_DECIMAL_PLACES = 4
// decimal(18,4): up to 14 integer digits before the scale-limited fraction.
const MAX_COMMISSION_INTEGER_DIGITS = 14

const UNSIGNED_DECIMAL_PATTERN = /^\d+(\.\d+)?$/

function parseCommissionRatePercent(rawText) {
    const trimmed = String(rawText ?? '').trim()

    if (trimmed === '') {
        return { error: 'rateRequired' }
    }

    // Rejects scientific notation, Infinity/NaN spellings, signs, separators —
    // anything Number() would coerce surprisingly.
    if (!UNSIGNED_DECIMAL_PATTERN.test(trimmed)) {
        return { error: 'rateInvalid' }
    }

    const percent = Number(trimmed)
    if (!Number.isFinite(percent)) {
        return { error: 'rateInvalid' }
    }

    if (percent < RATE_MIN_PERCENT || percent > RATE_MAX_PERCENT) {
        return { error: 'rateRange' }
    }

    const decimalPart = trimmed.split('.')[1]
    if (decimalPart && decimalPart.length > RATE_MAX_DECIMAL_PLACES) {
        return { error: 'ratePrecision' }
    }

    return { fraction: percent / 100 }
}

function formatCommissionRatePercent(value) {
    const rate = Number(value)
    if (!Number.isFinite(rate)) return ''
    return (rate * 100).toFixed(RATE_MAX_DECIMAL_PLACES).replace(/\.?0+$/, '')
}

function parseMaxCommissionCap(rawText) {
    const trimmed = String(rawText ?? '').trim()

    // Empty means "no cap": null is preserved end-to-end, never coerced to zero.
    if (trimmed === '') {
        return { cap: null }
    }

    if (!UNSIGNED_DECIMAL_PATTERN.test(trimmed)) {
        return { error: 'maxInvalid' }
    }

    const cap = Number(trimmed)
    if (!Number.isFinite(cap)) {
        return { error: 'maxInvalid' }
    }

    if (cap <= 0) {
        return { error: 'maxPositive' }
    }

    const [integerPart, decimalPart] = trimmed.split('.')
    if (
        (decimalPart && decimalPart.length > MAX_COMMISSION_DECIMAL_PLACES)
        || integerPart.replace(/^0+(?=\d)/, '').length > MAX_COMMISSION_INTEGER_DIGITS
    ) {
        return { error: 'maxPrecision' }
    }

    return { cap }
}

// Failures map to stable, localized wording. Raw backend exception text, SQL,
// identifiers, and constraint names are never surfaced to the user.
function resolveMutationErrorKey(err) {
    const status = err?.response?.status

    if (status == null) {
        return 'network'
    }

    if (status === 400 || status === 422) {
        return 'rejected'
    }

    if (status === 401 || status === 403) {
        return 'forbidden'
    }

    if (status === 404) {
        return 'notFound'
    }

    if (status === 409) {
        return 'conflict'
    }

    if (status === 503) {
        return 'unavailable'
    }

    return 'server'
}

export default function ProductAffiliateSettingsCard({
    productId,
    onSettingsChanged,
    onBusyChange,
    onDirtyChange,
    onCancel,
}) {
    const { t } = useTranslation()
    const user = useAuthStore(state => state.user)
    const { confirm } = useConfirmDialog()
    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)
    const [error, setError] = useState('')
    const [errorField, setErrorField] = useState(null)
    const [success, setSuccess] = useState('')

    // AF-23: Readiness check
    const readiness = useAffiliateReadiness()

    // Affiliate state
    const [commissionRate, setCommissionRate] = useState('')
    const [maxCommissionPerItem, setMaxCommissionPerItem] = useState('')
    const [selectedCategoryId, setSelectedCategoryId] = useState(null)
    const [authoritySettings, setAuthoritySettings] = useState(null)
    const [dirtyFields, setDirtyFields] = useState({})
    const settingsRequestSeq = useRef(0)
    const categoryTreeRequestSeq = useRef(0)
    const mutationRequestSeq = useRef(0)
    const mutationInFlightRef = useRef(false)
    const authoritySettingsRef = useRef(null)
    const dirtyFieldsRef = useRef({})
    const mountedRef = useRef(false)

    // AF-30B: Category tree state
    const [categoryTree, setCategoryTree] = useState([])
    const [categoryTreeLoading, setCategoryTreeLoading] = useState(true)

    // Only show for Owner/Admin roles
    const isAdmin = user?.role === 'Owner' || user?.role === 'Admin'

    // A hosting surface refresh is observational. It must never make an already
    // accepted Affiliate mutation look like it failed, including when a host
    // callback throws synchronously or returns a rejected promise.
    const notifySettingsChanged = useCallback(() => {
        try {
            const callbackResult = onSettingsChanged?.()
            if (callbackResult && typeof callbackResult.catch === 'function') {
                void callbackResult.catch(() => undefined)
            }
        } catch {
            // The authoritative mutation has already succeeded; keep its result.
        }
    }, [onSettingsChanged])

    // ─── Resolve selected category from tree cache (GAP-3) ────────
    const resolvedCategory = useMemo(() => {
        if (!selectedCategoryId || !categoryTree.length) return null
        for (const main of categoryTree) {
            if (main.id === selectedCategoryId) {
                return { ...main, parentName: null, parentSlug: null }
            }
            const sub = main.subcategories?.find(s => s.id === selectedCategoryId)
            if (sub) {
                return {
                    ...sub,
                    parentName: main.name,
                    parentSlug: main.slug,
                    parentIsActive: main.isActive,
                }
            }
        }
        return null // Category not found in tree (may have been deleted)
    }, [selectedCategoryId, categoryTree])

    // Is selected category inactive? (GAP-2 handling)
    const isCategoryInactive = Boolean(resolvedCategory
        && (!resolvedCategory.isActive || resolvedCategory.parentIsActive === false))
    const isCategoryMissing = Boolean(selectedCategoryId && !resolvedCategory)
    const isDirty = Object.values(dirtyFields).some(Boolean)
    const acceptedIsEnabled = Boolean(authoritySettings?.isEnabled)
    const unsavedChangesMessage = t('affiliateSettingsCard.unsavedDialogDescription')

    const normalizeSettings = useCallback((data) => ({
        // Affiliate management owns persisted participation. The legacy
        // isEnabled field represents effective discovery eligibility, so prefer
        // the explicit policy state when the server supplies it.
        isEnabled: Boolean(data?.isPolicyEnabled ?? data?.isEnabled),
        commissionRate: data?.commissionRate != null ? formatCommissionRatePercent(data.commissionRate) : '',
        maxCommissionPerItem: data?.maxCommissionPerItem != null ? data.maxCommissionPerItem.toString() : '',
        selectedCategoryId: data?.affiliateCategoryId || null,
    }), [])

    const assertCurrentProductSettings = useCallback((data) => {
        if (`${data?.productId ?? ''}`.toLowerCase() !== `${productId}`.toLowerCase()) {
            throw new Error('Affiliate settings response did not match the current product.')
        }
    }, [productId])

    const applyFormSettings = useCallback((settings) => {
        setCommissionRate(settings.commissionRate)
        setMaxCommissionPerItem(settings.maxCommissionPerItem)
        setSelectedCategoryId(settings.selectedCategoryId)
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
        setErrorField(null)
        setSuccess('')
    }, [applyFormSettings, clearDirtyState])

    const beginMutation = useCallback(() => {
        // Synchronous lock taken BEFORE any await: rapid double click / Enter /
        // slow network collapses into one semantic mutation.
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
        mountedRef.current && currentMutation === mutationRequestSeq.current
    ), [])

    const finishMutation = useCallback((currentMutation) => {
        if (!isCurrentMutation(currentMutation)) return

        mutationInFlightRef.current = false
        setSaving(false)
    }, [isCurrentMutation])

    // ─── Fetch on mount ──────────────────────────────────────────
    useEffect(() => {
        mountedRef.current = true
        return () => {
            mountedRef.current = false
            mutationRequestSeq.current += 1
            mutationInFlightRef.current = false
        }
    }, [])

    useEffect(() => {
        onBusyChange?.(saving)
    }, [onBusyChange, saving])

    useEffect(() => {
        onDirtyChange?.(isDirty)
    }, [isDirty, onDirtyChange])

    useEffect(() => () => {
        onBusyChange?.(false)
        onDirtyChange?.(false)
    }, [onBusyChange, onDirtyChange])

    // NOTE: deliberately independent of the translation function so its identity
    // stays stable; a failed initial read renders through the localized
    // settings-unavailable fallback below without extra error state churn.
    const fetchSettings = useCallback(async ({ clearError = true } = {}) => {
        const currentRequest = ++settingsRequestSeq.current
        try {
            const data = await adminProductAffiliateApi.getSettings(productId)
            if (mountedRef.current && currentRequest === settingsRequestSeq.current) {
                applyAuthoritativeSettings(data)
                if (clearError) setError('')
            }
        } catch {
            // Stale reads and load failures never overwrite a newer authority state,
            // and policy/API errors are never logged or surfaced raw.
            if (mountedRef.current && currentRequest === settingsRequestSeq.current && !authoritySettingsRef.current) {
                setAuthoritySettings(null)
            }
        }
    }, [applyAuthoritativeSettings, productId])

    const fetchCategoryTree = useCallback(async () => {
        const currentRequest = ++categoryTreeRequestSeq.current
        try {
            setCategoryTreeLoading(true)
            const data = await adminProductAffiliateApi.getCategoryTree()
            if (mountedRef.current && currentRequest === categoryTreeRequestSeq.current) {
                setCategoryTree(Array.isArray(data) ? data : [])
            }
        } catch {
            if (mountedRef.current && currentRequest === categoryTreeRequestSeq.current) {
                setCategoryTree([])
            }
        } finally {
            if (mountedRef.current && currentRequest === categoryTreeRequestSeq.current) {
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
            setErrorField(null)
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
                mutationRequestSeq.current += 1
                mutationInFlightRef.current = false
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

        let confirmationPending = false

        const handlePopState = () => {
            if (confirmationPending) {
                window.history.pushState({ affiliateUnsavedChangesGuard: true }, '', window.location.href)
                return
            }

            confirmationPending = true
            void confirm({
                cancelLabel: t('affiliateSettingsCard.stayLabel'),
                confirmLabel: t('affiliateSettingsCard.leaveLabel'),
                description: unsavedChangesMessage,
                title: t('affiliateSettingsCard.unsavedDialogTitle'),
                variant: 'warning',
            }).then((confirmed) => {
                confirmationPending = false

                if (confirmed) {
                    window.removeEventListener('popstate', handlePopState)
                    setTimeout(() => window.history.back(), 0)
                    return
                }

                window.history.pushState({ affiliateUnsavedChangesGuard: true }, '', window.location.href)
            })
        }

        window.addEventListener('popstate', handlePopState)

        return () => {
            window.removeEventListener('popstate', handlePopState)
        }
    }, [confirm, isDirty, t, unsavedChangesMessage])

    // ─── Shared validation + submit path ─────────────────────────
    const validateDraft = useCallback(() => {
        if (!selectedCategoryId) {
            return { errorKey: t('affiliateSettingsCard.errors.categoryRequired'), field: 'category' }
        }

        if (isCategoryMissing) {
            return { errorKey: t('affiliateSettingsCard.errors.categoryInactive'), field: 'category' }
        }

        if (isCategoryInactive) {
            return { errorKey: t('affiliateSettingsCard.errors.categoryInactive'), field: 'category' }
        }

        const rate = parseCommissionRatePercent(commissionRate)
        if (rate.error) {
            return { errorKey: t(`affiliateSettingsCard.errors.${rate.error}`), field: 'rate' }
        }

        const cap = parseMaxCommissionCap(maxCommissionPerItem)
        if (cap.error) {
            return { errorKey: t(`affiliateSettingsCard.errors.${cap.error}`), field: 'max' }
        }

        return { rateFraction: rate.fraction, maxCap: cap.cap }
    }, [commissionRate, isCategoryInactive, isCategoryMissing, maxCommissionPerItem, selectedCategoryId, t])

    const runMutation = async ({ action, requireReadiness = false }) => {
        setError('')
        setErrorField(null)
        setSuccess('')

        if (requireReadiness && !readiness.isReady) {
            setError(t('affiliateSettingsCard.errors.notReady'))
            return
        }

        const validated = validateDraft()
        if (validated.errorKey) {
            setError(validated.errorKey)
            setErrorField(validated.field)
            return
        }

        const currentMutation = beginMutation()
        // Lock already held by an in-flight semantic mutation: this click is a no-op.
        if (!currentMutation) return

        try {
            const result = await adminProductAffiliateApi.enableWithCategory(
                productId, validated.rateFraction, selectedCategoryId, validated.maxCap
            )
            if (!isCurrentMutation(currentMutation)) return

            applyAcceptedMutationSettings(result)

            // Reconcile from canonical server authority while keeping any local edits;
            // sequence guards ensure older responses can never overwrite this state.
            void fetchSettings()

            notifySettingsChanged()

            if (!isCurrentMutation(currentMutation)) return
            setSuccess(action === 'enable'
                ? t('affiliateSettingsCard.successEnabled')
                : t('affiliateSettingsCard.successSaved'))
        } catch (err) {
            if (!isCurrentMutation(currentMutation)) return

            setError(t(`affiliateSettingsCard.mutationErrors.${resolveMutationErrorKey(err)}`))
            await Promise.all([fetchSettings({ clearError: false }), fetchCategoryTree()])
        } finally {
            finishMutation(currentMutation)
        }
    }

    // ─── Enable Affiliate (uses atomic enable-with-category upsert) ──
    const handleEnable = () => {
        void runMutation({ action: 'enable', requireReadiness: true })
    }

    // ─── Save Changes (update rate/category/cap while enabled) ───
    const handleSave = () => {
        void runMutation({ action: 'save' })
    }

    // ─── Disable Affiliate (preserves policy fields server-side) ───
    const handleDisable = async () => {
        setError('')
        setErrorField(null)
        setSuccess('')

        const currentMutation = beginMutation()
        if (!currentMutation) return

        try {
            const result = await adminProductAffiliateApi.disable(productId)
            if (!isCurrentMutation(currentMutation)) return

            applyAcceptedMutationSettings(result)
            void fetchSettings()

            notifySettingsChanged()

            if (!isCurrentMutation(currentMutation)) return
            setSuccess(t('affiliateSettingsCard.successDisabled'))
        } catch (err) {
            if (!isCurrentMutation(currentMutation)) return

            setError(t(`affiliateSettingsCard.mutationErrors.${resolveMutationErrorKey(err)}`))
            await fetchSettings({ clearError: false })
        } finally {
            finishMutation(currentMutation)
        }
    }

    if (!isAdmin || !productId) return null

    // ─── Loading State ──────────────────────────────────────────
    if (loading || readiness.loading) {
        return (
            <div className="card border-2 border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50" role="status" aria-live="polite">
                <div className="flex items-center gap-3 animate-pulse">
                    <div className="w-12 h-12 bg-amber-100 rounded-xl"></div>
                    <div className="flex-1">
                        <div className="h-5 bg-amber-100 rounded w-48 mb-2"></div>
                        <div className="h-3 bg-amber-100 rounded w-64"></div>
                    </div>
                    <span className="sr-only">{t('affiliateSettingsCard.loading')}</span>
                </div>
            </div>
        )
    }

    if (!authoritySettings && readiness.isReady) {
        return (
            <div className="card border-2 border-red-200 bg-red-50">
                <div className="flex items-start gap-4">
                    <div className="flex-shrink-0 w-12 h-12 bg-red-100 rounded-xl flex items-center justify-center">
                        <span aria-hidden="true" className="text-2xl">!</span>
                    </div>
                    <div className="flex-1">
                        <h3 className="text-lg font-semibold text-red-900 mb-0.5">
                            {t('affiliateSettingsCard.title')}
                        </h3>
                        <p className="text-sm text-red-700">
                            {error || t('affiliateSettingsCard.settingsLoadError')}
                        </p>
                    </div>
                </div>
            </div>
        )
    }

    // AF-23: Show readiness warning if not ready
    if (!readiness.isReady && !acceptedIsEnabled) {
        return (
            <div className="card border-2 border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50">
                <div className="flex items-start gap-4 mb-4">
                    <div className="flex-shrink-0 w-12 h-12 bg-amber-100 rounded-xl flex items-center justify-center">
                        <span aria-hidden="true" className="text-2xl">🤝</span>
                    </div>
                    <div className="flex-1">
                        <h3 className="text-lg font-semibold text-amber-900 mb-0.5">
                            {t('affiliateSettingsCard.title')}
                        </h3>
                        <p className="text-sm text-amber-700">
                            {t('affiliateSettingsCard.subtitle')}
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

    const categoryHasNoOptions = !categoryTreeLoading && categoryTree.length === 0

    return (
        <div className="card border-2 border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50">
            {/* ═══ Header ═══ */}
            <div className="flex items-start gap-4">
                <div className="flex-shrink-0 w-12 h-12 bg-amber-100 rounded-xl flex items-center justify-center">
                    <span aria-hidden="true" className="text-2xl">🤝</span>
                </div>
                <div className="flex-1">
                    <div className="flex items-start justify-between gap-2">
                        <div>
                            <h3 className="text-lg font-semibold text-amber-900 mb-0.5">
                                {t('affiliateSettingsCard.title')}
                            </h3>
                            <p className="text-sm text-amber-700">
                                {t('affiliateSettingsCard.subtitle')}
                            </p>
                        </div>
                        {/* Status Badge — explicit policy participation wording */}
                        <span
                            data-testid="affiliate-policy-status"
                            className={`inline-flex items-center gap-1 px-2.5 py-0.5 text-xs font-medium rounded-full ${acceptedIsEnabled ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-700'}`}
                        >
                            <span aria-hidden="true" className={`w-1.5 h-1.5 rounded-full ${acceptedIsEnabled ? 'bg-green-500' : 'bg-gray-400'}`}></span>
                            {acceptedIsEnabled
                                ? t('affiliateSettingsCard.statusEnabled')
                                : t('affiliateSettingsCard.statusDisabled')}
                        </span>
                    </div>
                </div>
            </div>

            {/* ═══ Settings Form ═══ */}
            <div className="mt-4 pt-4 border-t border-amber-200">
                {!readiness.isReady && (
                    <div className="mb-4">
                        <AffiliateReadinessWarning
                            storefrontLive={readiness.storefrontLive}
                            hasPaymentMethod={readiness.hasPaymentMethod}
                            hasShippingMethod={readiness.hasShippingMethod}
                            missingRequirements={readiness.missingRequirements}
                            variant="inline"
                        />
                    </div>
                )}
                {/* ── Category Dropdown (AF-30B, read/select-only) ── */}
                <div className="mb-4">
                    <label className="label" htmlFor="affiliate-category-select">{t('affiliateSettingsCard.categoryLabel')}</label>
                    <select
                        value={selectedCategoryId || ''}
                        onChange={(e) => {
                            const nextCategoryId = e.target.value || null
                            setSelectedCategoryId(nextCategoryId)
                            markFieldDirty('selectedCategoryId', nextCategoryId)
                            setError('')
                            setErrorField(null)
                        }}
                        className={`input w-full ${isCategoryInactive ? 'border-amber-500' : ''} ${isCategoryMissing ? 'border-red-500' : ''}`}
                        disabled={saving || categoryTreeLoading || categoryHasNoOptions}
                        id="affiliate-category-select"
                        aria-describedby={`affiliate-category-help${errorField === 'category' ? ' affiliate-settings-error' : ''}`}
                        aria-invalid={Boolean(isCategoryInactive || isCategoryMissing || errorField === 'category') || undefined}
                    >
                        {!categoryHasNoOptions && (
                            <option value="">{t('affiliateSettingsCard.selectPlaceholder')}</option>
                        )}
                        {categoryHasNoOptions && (
                            <option value="">{t('affiliateSettingsCard.categoriesUnavailableOption')}</option>
                        )}
                        {categoryTree.map(main => (
                            <optgroup key={main.id} label={`${main.isActive ? '' : '⚠️ '}${main.name}`}>
                                <option
                                    value={main.id}
                                    disabled={!main.isActive}
                                >
                                    {`${main.name}${main.isActive ? '' : ` ${t('affiliateSettingsCard.inactiveSuffix')}`}`}
                                </option>
                                {main.subcategories?.map(sub => (
                                    <option
                                        key={sub.id}
                                        value={sub.id}
                                        disabled={!main.isActive || !sub.isActive}
                                    >
                                        {`${sub.name}${(!main.isActive || !sub.isActive) ? ` ${t('affiliateSettingsCard.inactiveSuffix')}` : ''}`}
                                    </option>
                                ))}
                            </optgroup>
                        ))}
                    </select>

                    {/* Category status indicators */}
                    <p
                        id="affiliate-category-help"
                        className={`text-xs mt-1 ${isCategoryInactive ? 'text-amber-600' : isCategoryMissing ? 'text-red-600' : 'text-gray-500'}`}
                    >
                        {isCategoryInactive
                            ? t('affiliateSettingsCard.categoryHelpInactive')
                            : isCategoryMissing
                                ? t('affiliateSettingsCard.categoryHelpMissing')
                                : resolvedCategory?.isActive
                                    ? `${resolvedCategory.parentName ? `${resolvedCategory.parentName} -> ` : ''}${resolvedCategory.name}`
                                    : categoryHasNoOptions
                                        ? t('affiliateSettingsCard.categoryHelpEmpty')
                                        : t('affiliateSettingsCard.categoryHelpDefault')}
                    </p>
                </div>

                {/* ── Commission Rate + Max Cap ── */}
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                        <label className="label" htmlFor="affiliate-commission-rate">{t('affiliateSettingsCard.rateLabel')}</label>
                        <div className="relative">
                            <input
                                type="number"
                                dir="ltr"
                                step="0.0001"
                                min="0.01"
                                max="50"
                                value={commissionRate}
                                onChange={(e) => {
                                    setCommissionRate(e.target.value)
                                    markFieldDirty('commissionRate', e.target.value)
                                }}
                                className="input pe-8"
                                placeholder="10.00"
                                disabled={saving}
                                id="affiliate-commission-rate"
                                aria-describedby={`affiliate-commission-rate-help${errorField === 'rate' ? ' affiliate-settings-error' : ''}`}
                                aria-invalid={errorField === 'rate' || undefined}
                            />
                            <span aria-hidden="true" className="absolute end-3 top-1/2 -translate-y-1/2 text-gray-400 text-sm">%</span>
                        </div>
                        <p id="affiliate-commission-rate-help" className="text-xs text-gray-500 mt-1">
                            {t('affiliateSettingsCard.rateHelp')}
                        </p>
                    </div>
                    <div>
                        <label className="label" htmlFor="affiliate-max-commission">{t('affiliateSettingsCard.maxLabel')}</label>
                        <input
                            type="number"
                            dir="ltr"
                            step="0.0001"
                            min="0.0001"
                            value={maxCommissionPerItem}
                            onChange={(e) => {
                                setMaxCommissionPerItem(e.target.value)
                                markFieldDirty('maxCommissionPerItem', e.target.value)
                            }}
                            className="input"
                            placeholder={t('affiliateSettingsCard.maxPlaceholder')}
                            disabled={saving}
                            id="affiliate-max-commission"
                            aria-describedby={`affiliate-max-commission-help${errorField === 'max' ? ' affiliate-settings-error' : ''}`}
                            aria-invalid={errorField === 'max' || undefined}
                        />
                        <p id="affiliate-max-commission-help" className="text-xs text-gray-500 mt-1">
                            {t('affiliateSettingsCard.maxHelp')}
                        </p>
                    </div>
                </div>

                {/* ── Info Notice ── */}
                <div className="mt-3 p-3 bg-blue-50 border border-blue-200 rounded-lg">
                    <p className="text-xs text-blue-700">
                        {t('affiliateSettingsCard.infoNotice')}
                    </p>
                </div>

                {/* ── Error / Success Messages ── */}
                {error && (
                    <div id="affiliate-settings-error" className="mt-3 p-3 bg-red-50 border border-red-200 text-red-700 text-sm rounded-lg break-words" role="alert">
                        {error}
                    </div>
                )}
                {success && (
                    <div className="mt-3 p-3 bg-green-50 border border-green-200 text-green-700 text-sm rounded-lg" role="status" aria-live="polite">
                        {success}
                    </div>
                )}

                {/* ═══ Action Buttons ═══ */}
                {isDirty && (
                    <div className="mt-3 p-3 bg-amber-50 border border-amber-200 text-amber-700 text-sm rounded-lg" role="status" aria-live="polite">
                        {t('affiliateSettingsCard.unsavedBanner')}
                    </div>
                )}

                <div className="mt-4 flex flex-wrap justify-end gap-2">
                    {onCancel && (
                        <button
                            type="button"
                            onClick={onCancel}
                            disabled={saving}
                            className="btn-secondary"
                        >
                            {t('affiliateSettingsCard.cancelButton')}
                        </button>
                    )}

                    {isDirty && (
                        <button
                            type="button"
                            onClick={resetToAuthoritativeSettings}
                            disabled={saving}
                            className="btn-secondary flex items-center gap-2"
                            id="affiliate-reset-btn"
                        >
                            {t('affiliateSettingsCard.resetChanges')}
                        </button>
                    )}

                    {/* Disable button — only visible when policy is enabled */}
                    {acceptedIsEnabled && (
                        <button
                            type="button"
                            onClick={handleDisable}
                            disabled={saving}
                            className="btn-secondary flex items-center gap-2 text-red-600 border-red-200 hover:bg-red-50"
                            id="affiliate-disable-btn"
                        >
                            {saving && (
                                <span aria-hidden="true" className="animate-spin h-4 w-4 border-2 border-red-400 border-t-transparent rounded-full"></span>
                            )}
                            {saving
                                ? t('affiliateSettingsCard.workingLabel')
                                : t('affiliateSettingsCard.disableButton')}
                        </button>
                    )}

                    {/* Enable button — only visible when policy is disabled */}
                    {!acceptedIsEnabled && (
                        <button
                            type="button"
                            onClick={handleEnable}
                            disabled={saving || categoryTreeLoading || categoryHasNoOptions}
                            className="btn-primary flex items-center gap-2"
                            id="affiliate-enable-btn"
                        >
                            {saving && (
                                <span aria-hidden="true" className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full"></span>
                            )}
                            {saving
                                ? t('affiliateSettingsCard.enablingLabel')
                                : t('affiliateSettingsCard.enableButton')}
                        </button>
                    )}

                    {/* Save button — only visible when policy is enabled */}
                    {acceptedIsEnabled && (
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={saving || categoryTreeLoading || categoryHasNoOptions}
                            className="btn-primary flex items-center gap-2"
                            id="affiliate-save-btn"
                        >
                            {saving && (
                                <span aria-hidden="true" className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full"></span>
                            )}
                            {saving
                                ? t('affiliateSettingsCard.savingLabel')
                                : t('affiliateSettingsCard.saveButton')}
                        </button>
                    )}
                </div>
            </div>
        </div>
    )
}

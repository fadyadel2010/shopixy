import { useState, useEffect, useRef } from 'react'
import api from '../services/api'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext' // To format currency if needed, though preview returns formatted string mostly? No, DTO has decimals.

const DEFAULT_PREVIEW_MODEL = null

function createDefaultOverrideModel(productId = null) {
    return {
        productId,
        isEnabled: false,
        strategy: 'PERCENTAGE',
        value: 100,
        applyToMeta: true,
        applyToGoogle: true,
        applyToTikTok: true
    }
}

export default function ProductPixelOverrideTab({ productId, productPrice }) {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    const [loading, setLoading] = useState(false)
    const [override, setOverride] = useState(() => createDefaultOverrideModel(productId))
    const [preview, setPreview] = useState(DEFAULT_PREVIEW_MODEL)
    const [previewLoading, setPreviewLoading] = useState(false)
    const [previewValueInput, setPreviewValueInput] = useState(() => createDefaultOverrideModel(productId).value)
    const [debouncedPreviewValue, setDebouncedPreviewValue] = useState(() => createDefaultOverrideModel(productId).value)
    const overrideRequestIdRef = useRef(0)
    const previewRequestIdRef = useRef(0)
    const localEditsRef = useRef(false)
    const isOverrideCurrent = override.productId === (productId ?? null)
    const currentOverride = isOverrideCurrent ? override : createDefaultOverrideModel(productId)

    const updateOverride = (updates) => {
        localEditsRef.current = true

        setOverride(prev => {
            const baseOverride = prev.productId === (productId ?? null)
                ? prev
                : createDefaultOverrideModel(productId)
            const resolvedUpdates = typeof updates === 'function'
                ? updates(baseOverride)
                : updates

            return {
                ...baseOverride,
                ...resolvedUpdates,
                productId: productId ?? null
            }
        })
    }

    const applyOverrideModel = (nextProductId, nextOverride = null) => {
        const defaultOverride = createDefaultOverrideModel(nextProductId)
        const resolvedOverride = nextOverride
            ? {
                ...defaultOverride,
                ...nextOverride,
                productId: nextOverride.productId ?? nextProductId ?? null
            }
            : defaultOverride

        setOverride(resolvedOverride)
        setPreview(DEFAULT_PREVIEW_MODEL)
        setPreviewLoading(false)
        setPreviewValueInput(resolvedOverride.value)
        setDebouncedPreviewValue(resolvedOverride.value)

        return resolvedOverride
    }

    useEffect(() => {
        const handler = setTimeout(() => {
            setDebouncedPreviewValue(previewValueInput)
        }, 500)

        return () => {
            clearTimeout(handler)
        }
    }, [previewValueInput])

    // Load existing override
    useEffect(() => {
        localEditsRef.current = false
        applyOverrideModel(productId)

        if (!productId) {
            setLoading(false)

            return () => {
                overrideRequestIdRef.current += 1
                previewRequestIdRef.current += 1
            }
        }

        const fetchOverride = async () => {
            const requestId = ++overrideRequestIdRef.current

            try {
                setLoading(true)
                const response = await api.get(`/admin/pixel-tracking/products/${productId}`)

                if (requestId !== overrideRequestIdRef.current) {
                    return
                }

                if (response.data && !localEditsRef.current) {
                    applyOverrideModel(productId, response.data)
                }
            } catch (err) {
                if (requestId !== overrideRequestIdRef.current) {
                    return
                }

                if (err.response && err.response.status === 404) {
                    if (localEditsRef.current) {
                        return
                    }

                    applyOverrideModel(productId)
                    return
                }

                console.error('Failed to load pixel override', err)
            } finally {
                if (requestId === overrideRequestIdRef.current) {
                    setLoading(false)
                }
            }
        }

        void fetchOverride()

        return () => {
            overrideRequestIdRef.current += 1
            previewRequestIdRef.current += 1
        }
    }, [productId])

    // Save handler
    const handleSave = async () => {
        if (!productId || !isOverrideCurrent) {
            return
        }

        const parsedValue = parseFloat(currentOverride.value)

        if (!Number.isFinite(parsedValue) || parsedValue < 0) {
            alert(t('pixel.invalidValue', { defaultValue: 'Enter a valid non-negative value.' }))
            return
        }

        try {
            setLoading(true)
            await api.put(`/admin/pixel-tracking/products/${productId}`, {
                isEnabled: currentOverride.isEnabled,
                strategy: currentOverride.strategy,
                value: parsedValue,
                applyToMeta: currentOverride.applyToMeta,
                applyToGoogle: currentOverride.applyToGoogle,
                applyToTikTok: currentOverride.applyToTikTok
            })
            // Show success toast? (Maybe parent handles notifications, but here we can just be silent or use specific hook)
            alert(t('common.saved') || 'Saved successfully')
        } catch (err) {
            console.error('Failed to save pixel override', err)
            alert(t('common.error') || 'Error saving')
        } finally {
            setLoading(false)
        }
    }

    useEffect(() => {
        const parsedProductPrice = Number(productPrice)
        const hasValidProductPrice = Number.isFinite(parsedProductPrice) && parsedProductPrice > 0

        if (!currentOverride.isEnabled || !productId || !hasValidProductPrice) {
            previewRequestIdRef.current += 1
            setPreviewLoading(false)
            setPreview(DEFAULT_PREVIEW_MODEL)

            return () => {
                previewRequestIdRef.current += 1
            }
        }

        const requestId = ++previewRequestIdRef.current

        const fetchPreview = async () => {
            try {
                setPreviewLoading(true)

                const response = await api.get(`/admin/pixel-tracking/products/${productId}/preview`, {
                    params: {
                        strategyStr: currentOverride.strategy,
                        value: parseFloat(debouncedPreviewValue) || 0,
                        lineTotal: parsedProductPrice
                    }
                })

                if (requestId !== previewRequestIdRef.current) {
                    return
                }

                setPreview(response.data)
            } catch (err) {
                if (requestId !== previewRequestIdRef.current) {
                    return
                }

                if (err.response && err.response.status === 404) {
                    if (localEditsRef.current) {
                        return
                    }

                    applyOverrideModel(productId)
                    return
                }

                console.error('Failed to get preview', err)
            } finally {
                if (requestId === previewRequestIdRef.current) {
                    setPreviewLoading(false)
                }
            }
        }

        void fetchPreview()

        return () => {
            previewRequestIdRef.current += 1
        }
    }, [currentOverride.isEnabled, currentOverride.productId, currentOverride.strategy, debouncedPreviewValue, productId, productPrice])

    if (!productId) {
        return null
    }

    return (
        <div className="card bg-gradient-to-r from-pink-50 to-orange-50 border-2 border-pink-200">
            <div className="flex items-center justify-between mb-4">
                <div className="flex items-center gap-2">
                    <span className="text-xl">📊</span>
                    <h3 className="text-lg font-semibold text-pink-900">
                        {t('pixel.productOverride') || 'Pixel Tracking Override'}
                    </h3>
                    <span className="px-2 py-0.5 text-xs font-medium bg-pink-100 text-pink-700 rounded-full">
                        {t('common.advanced') || 'Advanced'}
                    </span>
                </div>
                {/* Save button specific to this section if needed, or rely on form auto-save/global save? 
                    The ProductForm has a global save. But this component fetches/saves independently?
                    Currently ProductForm saves `products/{id}`. 
                    Ideally, this should be part of the main form payload, OR saved independently.
                    
                    Given the architecture of ProductForm (monolithic put), usually separate resources are saved separately.
                    However, `ProductForm`'s `handleSubmit` is monolithic. 
                    
                    DECISION: Make this component self-contained for persistence OR lift state up.
                    Lifting state up is compliant with ProductForm pattern, but `ProductPixelOverride` is a separate entity/service.
                    Mixing it into `Product` entity update might require changing `ProductService` and `ProductController`.
                    
                    Reference: `ProductVariantsTab` manages its own data? 
                    Let's check `ProductVariantsTab`... It likely edits variants independently.
                    
                    For now, I'll add a "Save Override" button inside this card to keep it decoupled from the main Product entity updates.
                    This handles the M2 requirement without touching the risky core Product update logic.
                */}
                <button
                    type="button"
                    onClick={handleSave}
                    disabled={loading || !isOverrideCurrent}
                    className="btn-xs btn-primary"
                >
                    {loading ? '...' : (t('common.save') || 'Save Config')}
                </button>
            </div>

            <div className="space-y-4">
                {/* Enable Toggle */}
                <div className="flex items-center justify-between p-3 bg-white rounded-lg border border-pink-100 shadow-sm">
                    <div>
                        <span className="font-medium text-gray-900">{t('pixel.enableOverride') || 'Enable Override'}</span>
                        <p className="text-xs text-gray-500">{t('pixel.enableHint') || 'Custom conversion value for this product'}</p>
                    </div>
                    <label className="relative inline-flex items-center cursor-pointer">
                        <input
                            type="checkbox"
                            className="sr-only peer"
                            checked={currentOverride.isEnabled}
                            onChange={(e) => updateOverride({ isEnabled: e.target.checked })}
                        />
                        <div className="w-11 h-6 bg-gray-200 peer-focus:outline-none peer-focus:ring-4 peer-focus:ring-pink-300 rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-pink-600"></div>
                    </label>
                </div>

                {currentOverride.isEnabled && (
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6 animate-fadeIn">

                        {/* Configuration */}
                        <div className="space-y-4">
                            {/* Strategy */}
                            <div>
                                <label className="label">{t('pixel.strategy') || 'Strategy'}</label>
                                <div className="flex gap-2">
                                    <button
                                        type="button"
                                        onClick={() => updateOverride({ strategy: 'PERCENTAGE' })}
                                        className={`flex-1 py-2 px-3 rounded-md text-sm font-medium border transition-colors ${currentOverride.strategy === 'PERCENTAGE'
                                            ? 'bg-pink-100 border-pink-500 text-pink-800'
                                            : 'bg-white border-gray-300 text-gray-700 hover:bg-gray-50'
                                            }`}
                                    >
                                        % {t('pixel.percentage') || 'Percentage'}
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => updateOverride({ strategy: 'FIXED' })}
                                        className={`flex-1 py-2 px-3 rounded-md text-sm font-medium border transition-colors ${currentOverride.strategy === 'FIXED'
                                            ? 'bg-pink-100 border-pink-500 text-pink-800'
                                            : 'bg-white border-gray-300 text-gray-700 hover:bg-gray-50'
                                            }`}
                                    >
                                        $ {t('pixel.fixed') || 'Fixed Amount'}
                                    </button>
                                </div>
                            </div>

                            {/* Value */}
                            <div>
                                <label className="label">
                                    {currentOverride.strategy === 'PERCENTAGE'
                                        ? (t('pixel.percentageValue') || 'Percentage Value (%)')
                                        : (t('pixel.fixedValue') || 'Fixed Value ($)')
                                    }
                                </label>
                                <input
                                    type="number"
                                    value={currentOverride.value}
                                    onChange={(e) => {
                                        updateOverride({ value: e.target.value })
                                        setPreviewValueInput(e.target.value)
                                    }}
                                    className="input"
                                    min="0"
                                    step={currentOverride.strategy === 'PERCENTAGE' ? "1" : "0.01"}
                                />
                                <p className="text-xs text-gray-500 mt-1">
                                    {currentOverride.strategy === 'PERCENTAGE'
                                        ? t('productPixel.override.example.percentage')
                                        : t('productPixel.override.example.fixed')
                                    }
                                </p>
                            </div>

                            {/* Providers */}
                            <div>
                                <label className="label mb-2">{t('pixel.applyTo') || 'Apply To'}</label>
                                <div className="space-y-2">
                                    <label className="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            className="rounded text-pink-600 focus:ring-pink-500"
                                            checked={currentOverride.applyToMeta}
                                            onChange={(e) => updateOverride({ applyToMeta: e.target.checked })}
                                        />
                                        <span>Meta / Facebook</span>
                                    </label>
                                    <label className="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            className="rounded text-pink-600 focus:ring-pink-500"
                                            checked={currentOverride.applyToGoogle}
                                            onChange={(e) => updateOverride({ applyToGoogle: e.target.checked })}
                                        />
                                        <span>Google Analytics 4</span>
                                    </label>
                                    <label className="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            className="rounded text-pink-600 focus:ring-pink-500"
                                            checked={currentOverride.applyToTikTok}
                                            onChange={(e) => updateOverride({ applyToTikTok: e.target.checked })}
                                        />
                                        <span>TikTok</span>
                                    </label>
                                </div>
                            </div>
                        </div>

                        {/* Preview Panel */}
                        <div className="bg-gray-900 rounded-lg p-4 text-white shadow-lg flex flex-col justify-center">
                            <h4 className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-4 border-b border-gray-700 pb-2">
                                {t('productPixel.override.previewLabel')}
                            </h4>

                            {previewLoading ? (
                                <div className="flex justify-center py-4">
                                    <div className="animate-spin h-6 w-6 border-2 border-pink-500 border-t-transparent rounded-full"></div>
                                </div>
                            ) : preview ? (
                                <div className="space-y-4">
                                    <div className="flex justify-between items-end">
                                        <span className="text-sm text-gray-400">{t('productPixel.override.actualPrice')}</span>
                                        <span className="text-lg font-mono text-gray-300 delete line-through">
                                            {formatCurrency(preview.actualOrderTotal)}
                                        </span>
                                    </div>
                                    <div className="flex justify-between items-end">
                                        <span className="text-sm text-pink-400 font-medium">{t('productPixel.override.reportedValue')}</span>
                                        <div className="text-right">
                                            <span className="text-3xl font-bold font-mono text-white">
                                                {formatCurrency(preview.pixelReportedValue)}
                                            </span>
                                            <p className="text-xs text-green-400 mt-1">
                                                {preview.strategyDescription}
                                            </p>
                                        </div>
                                    </div>

                                    <div className="bg-gray-800 rounded p-2 text-xs text-gray-400 mt-4">
                                        <p>{t('productPixel.override.differenceLabel', {
                                            amount: formatCurrency(preview.overrideDifference),
                                            percentage: preview.overridePercentage
                                        })}</p>
                                    </div>
                                </div>
                            ) : (
                                <p className="text-sm text-gray-500 text-center">
                                    {t('productPixel.override.emptyState')}
                                </p>
                            )}
                        </div>
                    </div>
                )}
            </div>
        </div>
    )
}

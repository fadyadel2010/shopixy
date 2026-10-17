import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'

function sortMedia(items) {
    return [...items].sort((left, right) => {
        if (left.position !== right.position) {
            return left.position - right.position
        }

        return new Date(left.createdAtUtc || 0).getTime() - new Date(right.createdAtUtc || 0).getTime()
    })
}

export default function VariantMediaPicker({
    open,
    productId,
    selectedMediaId,
    onClose,
    onSelect
}) {
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState('')
    const [media, setMedia] = useState([])
    const { t } = useTranslation()
    const tx = useCallback((key, fallback) => {
        const value = t(key)
        return value === key ? fallback : value
    }, [t])

    useEffect(() => {
        if (!open || !productId) {
            return
        }

        let isMounted = true

        const fetchMedia = async () => {
            try {
                setLoading(true)
                const response = await api.get(`/admin/products/${productId}/media`)

                if (isMounted) {
                    setMedia(sortMedia((response.data || []).filter(item => item.mediaType === 'image')))
                    setError('')
                }
            } catch {
                if (isMounted) {
                    setError(tx('variants.failedToLoadMedia', 'Failed to load product images.'))
                }
            } finally {
                if (isMounted) {
                    setLoading(false)
                }
            }
        }

        fetchMedia()

        return () => {
            isMounted = false
        }
    }, [open, productId, tx])

    const hasMedia = useMemo(() => media.length > 0, [media])

    if (!open) {
        return null
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
            <div
                className="w-full max-w-5xl overflow-hidden rounded-3xl bg-white shadow-2xl"
                role="dialog"
                aria-modal="true"
                aria-labelledby="variant-media-picker-title"
            >
                <div className="flex items-center justify-between border-b border-gray-200 px-6 py-5">
                    <div>
                        <h3 id="variant-media-picker-title" className="text-lg font-semibold text-gray-900">{tx('variants.selectVariantImage', 'Select Variant Image')}</h3>
                        <p className="mt-1 text-sm text-gray-500">
                            {tx('variants.selectVariantImageHint', 'Choose one image from the product media gallery. Videos are not available for variants.')}
                        </p>
                    </div>

                    <button
                        type="button"
                        onClick={onClose}
                        aria-label={tx('common.close', 'Close')}
                        className="rounded-full p-2 text-gray-500 transition hover:bg-gray-100 hover:text-gray-900"
                    >
                        <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>

                <div className="max-h-[70vh] overflow-y-auto px-6 py-6">
                    {loading ? (
                        <div className="flex items-center justify-center py-16">
                            <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary-600 border-t-transparent" />
                        </div>
                    ) : error ? (
                        <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700" role="alert">
                            {error}
                        </div>
                    ) : hasMedia ? (
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
                            {media.map((item) => {
                                const isSelected = item.id === selectedMediaId

                                return (
                                    <button
                                        key={item.id}
                                        type="button"
                                        onClick={() => onSelect(item)}
                                        className={`overflow-hidden rounded-2xl border text-left shadow-sm transition ${isSelected ? 'border-primary-500 ring-2 ring-primary-200' : 'border-gray-200 hover:-translate-y-0.5 hover:shadow-md'}`}
                                    >
                                        <div className="relative aspect-[4/3] overflow-hidden bg-gray-100">
                                            <img src={item.url} alt={tx('variants.variantMediaOption', 'Variant media option')} className="h-full w-full object-cover" />
                                            {item.position === 1 && (
                                                <span className="absolute left-3 top-3 rounded-full bg-primary-600 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.12em] text-white">
                                                    {tx('variants.primary', 'Primary')}
                                                </span>
                                            )}
                                            {isSelected && (
                                                <span className="absolute right-3 top-3 rounded-full bg-white/95 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.12em] text-primary-700">
                                                    {tx('variants.selected', 'Selected')}
                                                </span>
                                            )}
                                        </div>

                                        <div className="px-4 py-3">
                                            <p className="truncate text-sm font-semibold text-gray-900">{tx('variants.galleryImagePosition', 'Gallery Image #{{position}}').replace('{{position}}', item.position)}</p>
                                            <p className="mt-1 truncate text-xs text-gray-500">{item.url}</p>
                                        </div>
                                    </button>
                                )
                            })}
                        </div>
                    ) : (
                        <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-800">
                            {tx('variants.noProductImages', 'No product images are available yet. Upload product images in the Media section first.')}
                        </div>
                    )}
                </div>

                <div className="flex flex-col gap-3 border-t border-gray-200 px-6 py-4 sm:flex-row sm:items-center sm:justify-between">
                    <button
                        type="button"
                        onClick={() => onSelect(null)}
                        className="inline-flex items-center justify-center rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-gray-50"
                    >
                        {tx('variants.clearVariantImage', 'Clear Variant Image')}
                    </button>

                    <button
                        type="button"
                        onClick={onClose}
                        className="inline-flex items-center justify-center rounded-lg bg-gray-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-black"
                    >
                        {tx('variants.done', 'Done')}
                    </button>
                </div>
            </div>
        </div>
    )
}

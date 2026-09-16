import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { createLatestRequestGuard } from '../hooks/useLatestRequestGuard'

const LANGUAGE_OPTIONS = [
    { value: 'English', label: 'English' },
    { value: 'Arabic', label: 'Arabic' }
]

const COUNTRY_OPTIONS = [
    { value: '', label: 'Auto' },
    { value: 'Egypt', label: 'Egypt' },
    { value: 'Saudi Arabia', label: 'Saudi Arabia' },
    { value: 'United Arab Emirates', label: 'United Arab Emirates' },
    { value: 'United States', label: 'United States' },
    { value: 'United Kingdom', label: 'United Kingdom' }
]

const TONE_OPTIONS = [
    { value: 'marketing', label: 'Marketing' },
    { value: 'neutral', label: 'Neutral' },
    { value: 'technical', label: 'Technical' }
]

function PreviewChipList({ items, emptyLabel = 'None' }) {
    if (!items || items.length === 0) {
        return <span className="text-sm text-gray-500">{emptyLabel}</span>
    }

    return (
        <div className="flex flex-wrap gap-2">
            {items.map((item, index) => (
                <span
                    key={`${item}-${index}`}
                    className="rounded-full bg-gray-100 px-3 py-1 text-xs font-medium text-gray-700"
                >
                    {item}
                </span>
            ))}
        </div>
    )
}

export default function AiProductGeneratorModal({ isOpen, onClose, onApply }) {
    const { t } = useTranslation()
    const [formData, setFormData] = useState({
        productIdea: '',
        language: 'English',
        country: '',
        tone: 'marketing',
        featureCount: 5,
        faqCount: 3
    })
    const [generatedProduct, setGeneratedProduct] = useState(null)
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState('')
    // WAVE 3 (B): generation request identity. Only the latest request may
    // touch modal state; closing the modal or starting a newer generation
    // invalidates in-flight responses so stale results can never overwrite
    // newer state. Unmount invalidates via the same guard.
    const generationGuardRef = useRef(null)
    const generationInFlightRef = useRef(false)
    const appliedGenerationRef = useRef(false)
    const previewHeadingRef = useRef(null)
    const errorAlertRef = useRef(null)
    if (generationGuardRef.current === null) {
        generationGuardRef.current = createLatestRequestGuard()
    }

    useEffect(() => {
        const guard = generationGuardRef.current
        return () => {
            guard?.invalidate()
        }
    }, [])

    useEffect(() => {
        if (isOpen) {
            return
        }

        // Invalidate any in-flight generation when the modal closes so a late
        // response cannot repopulate a closed/reset modal.
        generationGuardRef.current?.invalidate()
        generationInFlightRef.current = false
        appliedGenerationRef.current = false
        setGeneratedProduct(null)
        setError('')
        setLoading(false)
    }, [isOpen])

    useEffect(() => {
        if (error && !loading) {
            errorAlertRef.current?.focus?.({ preventScroll: false })
        }
    }, [error, loading])

    if (!isOpen) {
        return null
    }

    const handleChange = (field, value) => {
        setFormData((prev) => ({
            ...prev,
            [field]: value
        }))
    }

    const handleGenerate = async (event) => {
        event.preventDefault()

        // WAVE 3 (B3): duplicate-click protection. The submit button is also
        // disabled while loading; this guard covers keyboard/assistive double
        // activation so exactly one API call is issued per generation.
        if (generationInFlightRef.current) {
            return
        }

        if (!formData.productIdea.trim()) {
            setError('Please enter a product idea before generating.')
            return
        }

        const guard = generationGuardRef.current
        const requestId = guard.beginRequest()
        generationInFlightRef.current = true

        try {
            setLoading(true)
            setError('')
            setGeneratedProduct(null)

            const response = await api.post('/admin/ai/products/generate', {
                productIdea: formData.productIdea.trim(),
                language: formData.language,
                country: formData.country,
                tone: formData.tone,
                featureCount: Number(formData.featureCount),
                faqCount: Number(formData.faqCount)
            })

            // WAVE 3 (B4): stale responses (superseded request, closed modal,
            // unmounted component) must never overwrite current state.
            if (!guard.isLatestRequest(requestId)) {
                return
            }

            setGeneratedProduct(response.data)
            appliedGenerationRef.current = false
            window.setTimeout(() => {
                if (guard.isLatestRequest(requestId)) {
                    previewHeadingRef.current?.focus?.({ preventScroll: false })
                }
            }, 0)
        } catch (err) {
            if (!guard.isLatestRequest(requestId)) {
                return
            }

            setError(
                err.response?.data?.message
                || err.response?.data?.error
                || 'Unable to generate product content right now. Please try again.'
            )
        } finally {
            // Only the latest request restores controls; a stale finisher must
            // not clear a newer generation's loading state.
            if (guard.isLatestRequest(requestId)) {
                generationInFlightRef.current = false
                setLoading(false)
            }
        }
    }

    const handleApply = () => {
        if (!generatedProduct) {
            return
        }

        // WAVE 3 (B2): the generated draft may be applied exactly once per
        // generation, preventing double application from repeated activation.
        if (appliedGenerationRef.current) {
            return
        }
        appliedGenerationRef.current = true

        const shouldClose = onApply?.(generatedProduct)
        if (shouldClose !== false) {
            onClose?.()
        }
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/50 px-4 py-6">
            <div
                role="dialog"
                aria-modal="true"
                aria-labelledby="ai-product-generator-title"
                className="max-h-[90vh] w-full max-w-5xl overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-2xl"
            >
                <div className="flex items-start justify-between gap-4 border-b border-slate-100 bg-gradient-to-r from-amber-50 via-white to-cyan-50 px-6 py-5">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-amber-600">{t('products.aiGenerator.adminAi', 'Admin AI')}</p>
                        <h2 id="ai-product-generator-title" className="mt-2 text-2xl font-semibold text-slate-900">{t('products.aiGenerator.title', 'Create Product with AI')}</h2>
                        <p className="mt-1 text-sm text-slate-500">
                            {t('products.aiGenerator.description', 'Generate draft product content from a short idea, then apply only into empty form fields.')}
                        </p>
                    </div>

                    <button
                        type="button"
                        onClick={onClose}
                        className="rounded-full p-2 text-slate-400 transition hover:bg-white hover:text-slate-700"
                        aria-label={t('common.close') || 'Close'}
                    >
                        <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>

                <div className="grid max-h-[calc(90vh-96px)] grid-cols-1 overflow-y-auto lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)]">
                    <form onSubmit={handleGenerate} className="space-y-6 border-r border-slate-100 px-6 py-6" aria-busy={loading}>
                        <div>
                            <label className="label">{t('products.aiGenerator.productIdea', 'Product Idea / Description *')}</label>
                            <textarea
                                value={formData.productIdea}
                                onChange={(event) => handleChange('productIdea', event.target.value)}
                                className="input min-h-36"
                                placeholder={t('products.aiGenerator.productIdeaPlaceholder', 'Wireless RGB gaming mouse 16000 DPI lightweight')}
                                required
                                disabled={loading}
                            />
                        </div>

                        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-1">
                            <div>
                                <label className="label">{t('products.aiGenerator.language', 'Language')}</label>
                                <select
                                    value={formData.language}
                                    onChange={(event) => handleChange('language', event.target.value)}
                                    className="input"
                                    disabled={loading}
                                >
                                    {LANGUAGE_OPTIONS.map((option) => (
                                        <option key={option.value} value={option.value}>
                                            {t(`products.aiGenerator.languageOptions.${option.value.toLowerCase()}`, option.label)}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            <div>
                                <label className="label">{t('products.aiGenerator.country', 'Country')}</label>
                                <select
                                    value={formData.country}
                                    onChange={(event) => handleChange('country', event.target.value)}
                                    className="input"
                                    disabled={loading}
                                >
                                    {COUNTRY_OPTIONS.map((option) => (
                                        <option key={option.label} value={option.value}>
                                            {t(`products.aiGenerator.countryOptions.${option.value || 'auto'}`, option.label)}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            <div>
                                <label className="label">{t('products.aiGenerator.tone', 'Tone')}</label>
                                <select
                                    value={formData.tone}
                                    onChange={(event) => handleChange('tone', event.target.value)}
                                    className="input"
                                    disabled={loading}
                                >
                                    {TONE_OPTIONS.map((option) => (
                                        <option key={option.value} value={option.value}>
                                            {t(`products.aiGenerator.toneOptions.${option.value}`, option.label)}
                                        </option>
                                    ))}
                                </select>
                            </div>
                        </div>

                        <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                            <h3 className="text-sm font-semibold text-slate-900">{t('products.aiGenerator.advancedOptions', 'Advanced Options')}</h3>
                            <div className="mt-4 grid gap-4 md:grid-cols-2 lg:grid-cols-1">
                                <div>
                                    <label className="label">{t('products.aiGenerator.featureCount', 'Number of features')}</label>
                                    <input
                                        type="number"
                                        min="1"
                                        max="12"
                                        value={formData.featureCount}
                                        onChange={(event) => handleChange('featureCount', event.target.value)}
                                        className="input"
                                        disabled={loading}
                                    />
                                </div>

                                <div>
                                    <label className="label">{t('products.aiGenerator.faqCount', 'Number of FAQs')}</label>
                                    <input
                                        type="number"
                                        min="0"
                                        max="12"
                                        value={formData.faqCount}
                                        onChange={(event) => handleChange('faqCount', event.target.value)}
                                        className="input"
                                        disabled={loading}
                                    />
                                </div>
                            </div>
                        </div>

                        {loading && (
                            <div
                                role="status"
                                aria-live="polite"
                                className="flex items-center gap-3 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
                            >
                                <span
                                    aria-hidden="true"
                                    className="h-4 w-4 shrink-0 animate-spin rounded-full border-2 border-amber-500 border-t-transparent"
                                />
                                <span>{t('products.aiGenerator.loadingStatus', 'AI generation is in progress. The form is locked until it completes.')}</span>
                            </div>
                        )}

                        {error && !loading && (
                            <div
                                ref={errorAlertRef}
                                role="alert"
                                tabIndex={-1}
                                className="rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700"
                            >
                                {error}
                            </div>
                        )}

                        <div className="flex flex-wrap gap-3">
                            <button
                                type="submit"
                                disabled={loading}
                                aria-disabled={loading}
                                className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:bg-slate-400"
                            >
                                {loading && (
                                    <span
                                        aria-hidden="true"
                                        className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent"
                                    />
                                )}
                                {loading
                                    ? t('products.aiGenerator.generating', 'Generating...')
                                    : t('products.aiGenerator.generate', 'Generate Product')}
                            </button>

                            <button
                                type="button"
                                onClick={onClose}
                                className="inline-flex items-center rounded-xl border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                            >
                                {t('common.cancel') || 'Cancel'}
                            </button>
                        </div>
                    </form>

                    <div className="space-y-6 px-6 py-6">
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-400">{t('products.aiGenerator.preview', 'Preview')}</p>
                            <h3 ref={previewHeadingRef} tabIndex={-1} className="mt-2 text-lg font-semibold text-slate-900 outline-none">{t('products.aiGenerator.previewTitle', 'Generated Product Draft')}</h3>
                            <p className="mt-1 text-sm text-slate-500">
                                {t('products.aiGenerator.previewDescription', 'Review the content before applying it to the product form.')}
                            </p>
                        </div>

                        {!generatedProduct ? (
                            <div className="flex min-h-80 items-center justify-center rounded-3xl border border-dashed border-slate-200 bg-slate-50 px-6 text-center text-sm text-slate-500">
                                {t('products.aiGenerator.previewEmpty', 'Generated product content will appear here after you run the AI generator.')}
                            </div>
                        ) : (
                            <>
                                <div className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
                                    <div className="flex flex-wrap items-start justify-between gap-4">
                                        <div>
                                            <h4 className="text-xl font-semibold text-slate-900">{generatedProduct.name || 'Untitled product'}</h4>
                                            <p className="mt-2 text-sm text-slate-600">{generatedProduct.shortDescription}</p>
                                        </div>
                                        <div className="rounded-2xl bg-slate-100 px-3 py-2 text-xs font-medium text-slate-600">
                                            Slug: {generatedProduct.slug || 'n/a'}
                                        </div>
                                    </div>

                                    <div className="mt-5 grid gap-5 xl:grid-cols-2">
                                        <div>
                                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">Description</p>
                                            <p className="mt-2 whitespace-pre-wrap text-sm leading-6 text-slate-700">
                                                {generatedProduct.description}
                                            </p>
                                        </div>

                                        <div className="space-y-4">
                                            <div>
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">Brand</p>
                                                <p className="mt-2 text-sm text-slate-700">
                                                    {generatedProduct.brand || 'Not provided'}
                                                </p>
                                            </div>

                                            <div>
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">Manufacturer</p>
                                                <p className="mt-2 text-sm text-slate-700">
                                                    {generatedProduct.manufacturer || 'Not provided'}
                                                </p>
                                            </div>

                                            <div>
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">Tags</p>
                                                <div className="mt-2">
                                                    <PreviewChipList items={generatedProduct.tags} />
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                </div>

                                <div className="grid gap-5 xl:grid-cols-2">
                                    <div className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">SEO</p>
                                        <div className="mt-4 space-y-4">
                                            <div>
                                                <p className="text-sm font-medium text-slate-900">Meta Title</p>
                                                <p className="mt-1 text-sm text-slate-600">{generatedProduct.metaTitle}</p>
                                            </div>
                                            <div>
                                                <p className="text-sm font-medium text-slate-900">Meta Description</p>
                                                <p className="mt-1 text-sm text-slate-600">{generatedProduct.metaDescription}</p>
                                            </div>
                                            <div>
                                                <p className="text-sm font-medium text-slate-900">Meta Keywords</p>
                                                <div className="mt-2">
                                                    <PreviewChipList items={generatedProduct.metaKeywords} />
                                                </div>
                                            </div>
                                        </div>
                                    </div>

                                    <div className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">Features</p>
                                        <ul className="mt-4 space-y-3 text-sm text-slate-700">
                                            {(generatedProduct.features || []).map((feature) => (
                                                <li key={feature} className="flex gap-3">
                                                    <span className="mt-1 h-2 w-2 rounded-full bg-amber-500" />
                                                    <span>{feature}</span>
                                                </li>
                                            ))}
                                        </ul>
                                    </div>
                                </div>

                                <div className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">FAQ</p>
                                    <div className="mt-4 space-y-4">
                                        {(generatedProduct.faq || []).length === 0 ? (
                                            <p className="text-sm text-slate-500">No FAQ items were generated.</p>
                                        ) : (
                                            generatedProduct.faq.map((item, index) => (
                                                <div key={`${item.question}-${index}`} className="rounded-2xl bg-slate-50 p-4">
                                                    <p className="text-sm font-medium text-slate-900">{item.question}</p>
                                                    <p className="mt-2 text-sm leading-6 text-slate-600">{item.answer}</p>
                                                </div>
                                            ))
                                        )}
                                    </div>
                                </div>

                                <div className="flex flex-wrap items-center justify-between gap-3 rounded-3xl border border-emerald-200 bg-emerald-50 px-5 py-4">
                                    <p className="text-sm text-emerald-800">
                                        {t('products.aiGenerator.applyHint', 'Applying will only fill empty form fields. Existing product values stay untouched.')}
                                    </p>
                                    <button
                                        type="button"
                                        onClick={handleApply}
                                        disabled={loading}
                                        className="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-emerald-300"
                                    >
                                        {t('products.aiGenerator.apply', 'Apply to Product Form')}
                                    </button>
                                </div>
                            </>
                        )}
                    </div>
                </div>
            </div>
        </div>
    )
}

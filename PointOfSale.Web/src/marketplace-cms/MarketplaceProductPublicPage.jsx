import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useLocation, useParams } from 'react-router-dom'
import { useFormatting } from '../hooks/useFormatting'
import { fetchMarketplaceFooter } from './marketplaceFooterPublicService'
import { fetchMarketplaceProduct } from './marketplaceProductPublicService'
import MarketplaceHeader from './MarketplaceHeader'
import MarketplacePublicFooter from './MarketplacePublicFooter'
import useLanguage from './useLanguage'
import { buildMarketplaceProductPath, normalizeMarketplaceLanguage } from './marketplaceLanguage'
import { getMarketplaceTranslationState } from './marketplaceTranslationUi'

const CURRENCY_UNAVAILABLE_LABEL = 'Currency unavailable'

function formatBackendCurrency(formatCurrency, value, currency) {
    const normalizedCurrency = String(currency || '').trim().toUpperCase()
    if (!normalizedCurrency) {
        return CURRENCY_UNAVAILABLE_LABEL
    }

    return formatCurrency(value, normalizedCurrency)
}

export function MarketplaceProductPublicPage() {
    const { lang, productId } = useParams()
    const location = useLocation()
    const { currentLang, isRTL } = useLanguage(lang)
    const { t } = useTranslation()
    const { formatCurrency } = useFormatting()
    const copy = useMemo(
        () => ({
            badge: t('marketplacePublic.product.badge'),
            autoTranslated: t('marketplacePublic.common.autoTranslated'),
            partialTranslationWarning: t('marketplacePublic.product.partialTranslationWarning'),
            categoryFallbackHint: t('marketplacePublic.common.categoryFallbackHint'),
            notFoundTitle: t('marketplacePublic.product.notFoundTitle'),
            notFoundMessage: t('marketplacePublic.product.notFoundMessage'),
            networkErrorMessage: t('marketplacePublic.product.networkErrorMessage', {
                defaultValue: t('common.failedToLoadData'),
            }),
            categoryLabel: t('marketplacePublic.product.categoryLabel'),
            vendorLabel: t('marketplacePublic.product.vendorLabel'),
            descriptionLabel: t('marketplacePublic.product.descriptionLabel'),
            featuresLabel: t('marketplacePublic.product.featuresLabel'),
            faqsLabel: t('marketplacePublic.product.faqsLabel'),
            unavailable: t('marketplacePublic.common.unavailable'),
            available: t('marketplacePublic.common.available'),
            followLabel: t('marketplacePublic.common.followLabel'),
            goToAdmin: t('marketplacePublic.common.goToAdmin'),
            loading: t('marketplacePublic.product.loading'),
            imageAlt: t('marketplacePublic.product.imageAlt'),
        }),
        [t],
    )
    const [product, setProduct] = useState(null)
    const [footer, setFooter] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')

    useEffect(() => {
        if (!productId) {
            return
        }

        let cancelled = false

        async function loadProduct() {
            setLoading(true)
            setError('')

            try {
                const [productResult, footerResult] = await Promise.allSettled([
                    fetchMarketplaceProduct(productId, currentLang),
                    fetchMarketplaceFooter(currentLang),
                ])

                if (productResult.status !== 'fulfilled') {
                    throw productResult.reason
                }

                if (!cancelled) {
                    setProduct(productResult.value)
                    setFooter(footerResult.status === 'fulfilled' ? footerResult.value : null)
                }
            } catch (loadError) {
                if (!cancelled) {
                    const status =
                        loadError?.status
                        ?? loadError?.response?.status
                        ?? (typeof Response !== 'undefined' && loadError instanceof Response ? loadError.status : undefined)

                    console.error('Failed to load marketplace product', loadError)
                    setError(status === 404 ? copy.notFoundMessage : copy.networkErrorMessage)
                }
            } finally {
                if (!cancelled) {
                    setLoading(false)
                }
            }
        }

        loadProduct()

        return () => {
            cancelled = true
        }
    }, [copy.networkErrorMessage, copy.notFoundMessage, currentLang, productId])

    useEffect(() => {
        if (!product || loading) {
            return
        }

        applySeo(product, currentLang)
    }, [currentLang, loading, product])

    const translationState = useMemo(
        () => getMarketplaceTranslationState(product, currentLang),
        [currentLang, product],
    )

    if (!productId) {
        return <MarketplaceProductNotFound language={currentLang} copy={copy} />
    }

    if (!lang) {
        return <Navigate to={`${buildMarketplaceProductPath(currentLang, productId)}${location.search}`} replace />
    }

    if (loading) {
        return <MarketplaceProductLoadingState language={currentLang} copy={copy} />
    }

    if (error || !product) {
        return <MarketplaceProductNotFound language={currentLang} copy={copy} message={error || copy.notFoundMessage} />
    }

    return (
        <div
            dir={isRTL ? 'rtl' : 'ltr'}
            className="min-h-screen bg-[radial-gradient(circle_at_top,_rgba(251,191,36,0.18),_transparent_38%),linear-gradient(180deg,#fff8eb_0%,#ffffff_55%)] px-6 py-16"
        >
            <div className="mx-auto max-w-6xl space-y-8">
                <MarketplaceHeader
                    language={currentLang}
                    badge={copy.badge}
                    title={product.name}
                    aside={
                        <div className="rounded-[28px] border border-slate-200 bg-slate-950 px-6 py-5 text-white shadow-[0_20px_60px_-40px_rgba(15,23,42,0.7)]">
                            <p className="text-xs uppercase tracking-[0.22em] text-slate-300">{product.currency}</p>
                            <p className="mt-2 text-3xl font-semibold">
                                {formatBackendCurrency(formatCurrency, product.price, product.currency)}
                            </p>
                        </div>
                    }
                >
                    <div className={`space-y-4 ${isRTL ? 'text-right' : 'text-left'}`}>
                        <div className={`flex flex-wrap gap-3 text-sm text-slate-600 ${isRTL ? 'justify-end' : 'justify-start'}`}>
                            {product.category ? (
                                <span
                                    title={translationState.categoryUsesFallback ? copy.categoryFallbackHint : undefined}
                                    className="inline-flex rounded-full border border-amber-200 bg-amber-50 px-3 py-1"
                                >
                                    {copy.categoryLabel}: {product.category}
                                </span>
                            ) : null}
                            <span className="inline-flex rounded-full border border-slate-200 bg-slate-50 px-3 py-1">
                                {copy.vendorLabel}: {product.vendorName}
                            </span>
                            <span className="inline-flex rounded-full border border-slate-200 bg-slate-50 px-3 py-1">
                                {product.isAvailable ? copy.available : copy.unavailable}
                            </span>
                        </div>

                        <div className={`flex flex-wrap gap-3 ${isRTL ? 'justify-end' : 'justify-start'}`}>
                            {translationState.showAutoTranslationBadge ? (
                                <p className="inline-flex items-center gap-2 rounded-full border border-sky-200 bg-sky-50 px-4 py-2 text-sm font-medium text-sky-700">
                                    <span aria-hidden="true">{'\ud83c\udf10'}</span>
                                    <span>{copy.autoTranslated}</span>
                                </p>
                            ) : null}

                            {translationState.showPartialTranslationWarning ? (
                                <p className="inline-flex items-center gap-2 rounded-full border border-amber-200 bg-amber-50 px-4 py-2 text-sm font-medium text-amber-800">
                                    <span aria-hidden="true">{'\u26a0\ufe0f'}</span>
                                    <span>{copy.partialTranslationWarning}</span>
                                </p>
                            ) : null}
                        </div>
                    </div>
                </MarketplaceHeader>

                <section className="grid gap-8 lg:grid-cols-[1.15fr_0.85fr]">
                    <div className="space-y-6">
                        {(product.images || []).length > 0 ? (
                            <div className="overflow-hidden rounded-[34px] border border-amber-100 bg-white shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                                <img
                                    src={product.images[0]}
                                    alt={product.name || copy.imageAlt}
                                    className="h-full w-full object-cover"
                                    loading="lazy"
                                />
                            </div>
                        ) : null}

                        {product.description ? (
                            <article className={`rounded-[30px] border border-amber-100 bg-white px-8 py-8 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)] ${isRTL ? 'text-right' : 'text-left'}`}>
                                <h2 className="text-sm font-semibold uppercase tracking-[0.22em] text-amber-700">
                                    {copy.descriptionLabel}
                                </h2>
                                <div className="mt-5 whitespace-pre-line text-base leading-8 text-slate-700">
                                    {product.description}
                                </div>
                            </article>
                        ) : null}
                    </div>

                    <div className="space-y-6">
                        {(product.features || []).length > 0 ? (
                            <section className="rounded-[30px] border border-amber-100 bg-white px-8 py-8 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                                <h2 className={`text-sm font-semibold uppercase tracking-[0.22em] text-amber-700 ${isRTL ? 'text-right' : 'text-left'}`}>
                                    {copy.featuresLabel}
                                </h2>
                                <ul className="mt-5 space-y-3">
                                    {product.features.map((feature) => (
                                        <li
                                            key={feature}
                                            className={`flex items-start gap-3 rounded-2xl border border-amber-100 bg-amber-50/70 px-4 py-3 text-sm text-slate-700 ${isRTL ? 'flex-row-reverse text-right' : 'text-left'}`}
                                        >
                                            <span className="mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full bg-amber-400" aria-hidden="true" />
                                            <span>{feature}</span>
                                        </li>
                                    ))}
                                </ul>
                            </section>
                        ) : null}

                        {(product.faqs || []).length > 0 ? (
                            <section className="rounded-[30px] border border-amber-100 bg-white px-8 py-8 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                                <h2 className={`text-sm font-semibold uppercase tracking-[0.22em] text-amber-700 ${isRTL ? 'text-right' : 'text-left'}`}>
                                    {copy.faqsLabel}
                                </h2>
                                <div className="mt-5 space-y-4">
                                    {product.faqs.map((item, index) => (
                                        <details key={`${item.question}-${index}`} className="rounded-2xl border border-amber-100 bg-amber-50/70 px-5 py-4">
                                            <summary className={`cursor-pointer list-none text-base font-medium text-slate-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {item.question}
                                            </summary>
                                            <p className={`mt-3 whitespace-pre-line text-sm leading-7 text-slate-600 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {item.answer}
                                            </p>
                                        </details>
                                    ))}
                                </div>
                            </section>
                        ) : null}
                    </div>
                </section>

                <MarketplacePublicFooter footer={footer} language={currentLang} followLabel={copy.followLabel} />
            </div>
        </div>
    )
}

function MarketplaceProductLoadingState({ language, copy }) {
    return (
        <div
            dir={language === 'ar' ? 'rtl' : 'ltr'}
            className="min-h-screen bg-[radial-gradient(circle_at_top,_rgba(251,191,36,0.18),_transparent_38%),linear-gradient(180deg,#fff8eb_0%,#ffffff_55%)] px-6 py-16"
        >
            <div className="mx-auto max-w-6xl space-y-8">
                <MarketplaceHeader
                    language={language}
                    badge={copy.badge}
                    title={copy.loading}
                    description={null}
                    aside={
                        <div className="animate-pulse rounded-[28px] border border-slate-200 bg-slate-950 px-6 py-5 shadow-[0_20px_60px_-40px_rgba(15,23,42,0.7)]">
                            <div className="h-3 w-12 rounded-full bg-slate-700" />
                            <div className="mt-3 h-8 w-28 rounded-full bg-slate-700" />
                        </div>
                    }
                >
                    <div className={`flex flex-wrap gap-3 ${language === 'ar' ? 'justify-end' : 'justify-start'}`}>
                        <div className="h-9 w-32 animate-pulse rounded-full bg-amber-100" />
                        <div className="h-9 w-28 animate-pulse rounded-full bg-slate-100" />
                        <div className="h-9 w-32 animate-pulse rounded-full bg-slate-100" />
                    </div>
                </MarketplaceHeader>

                <section className="grid gap-8 lg:grid-cols-[1.15fr_0.85fr]">
                    <div className="space-y-6">
                        <div className="h-[25rem] animate-pulse rounded-[34px] border border-amber-100 bg-white shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]" />
                        <div className="animate-pulse rounded-[30px] border border-amber-100 bg-white px-8 py-8 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                            <div className="h-4 w-28 rounded-full bg-amber-100" />
                            <div className="mt-5 h-4 w-full rounded-full bg-slate-100" />
                            <div className="mt-3 h-4 w-11/12 rounded-full bg-slate-100" />
                            <div className="mt-3 h-4 w-10/12 rounded-full bg-slate-100" />
                        </div>
                    </div>
                    <div className="space-y-6">
                        <div className="animate-pulse rounded-[30px] border border-amber-100 bg-white px-8 py-8 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                            <div className="h-4 w-24 rounded-full bg-amber-100" />
                            <div className="mt-5 space-y-3">
                                <div className="h-14 rounded-2xl bg-amber-50" />
                                <div className="h-14 rounded-2xl bg-amber-50" />
                                <div className="h-14 rounded-2xl bg-amber-50" />
                            </div>
                        </div>
                        <div className="animate-pulse rounded-[30px] border border-amber-100 bg-white px-8 py-8 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                            <div className="h-4 w-40 rounded-full bg-amber-100" />
                            <div className="mt-5 space-y-3">
                                <div className="h-20 rounded-2xl bg-amber-50" />
                                <div className="h-20 rounded-2xl bg-amber-50" />
                            </div>
                        </div>
                    </div>
                </section>
            </div>
        </div>
    )
}

function MarketplaceProductNotFound({ language, copy, message = copy.notFoundMessage }) {
    return (
        <div
            dir={normalizeMarketplaceLanguage(language) === 'ar' ? 'rtl' : 'ltr'}
            className="min-h-screen bg-[radial-gradient(circle_at_top,_rgba(251,191,36,0.18),_transparent_38%),linear-gradient(180deg,#fff8eb_0%,#ffffff_55%)] px-6 py-16"
        >
            <div className="mx-auto max-w-3xl rounded-[40px] border border-amber-100 bg-white/95 px-8 py-14 text-center shadow-[0_30px_90px_-50px_rgba(120,53,15,0.35)]">
                <p className="text-xs font-semibold uppercase tracking-[0.24em] text-amber-700">{copy.badge}</p>
                <h1 className="mt-4 text-3xl font-semibold text-slate-950">{copy.notFoundTitle}</h1>
                <p className="mt-3 text-sm leading-6 text-slate-500">{message}</p>
                <Link
                    to="/super-admin/login"
                    className="mt-8 inline-flex rounded-full bg-slate-950 px-5 py-3 text-sm font-medium text-white transition hover:bg-slate-800"
                >
                    {copy.goToAdmin}
                </Link>
            </div>
        </div>
    )
}

function applySeo(product, language) {
    const origin = window.location.origin
    const canonicalUrl = `${origin}/${language}/product/${product.productId}`
    document.title = product.name
    setMeta('description', product.description || '')
    setMeta('robots', 'index, follow')
    setMetaProperty('og:title', product.name)
    setMetaProperty('og:description', product.description || '')
    setMetaProperty('og:image', product.images?.[0] || '')
    setLink('canonical', canonicalUrl)
    setAlternateLink('en', `${origin}/en/product/${product.productId}`)
    setAlternateLink('ar', `${origin}/ar/product/${product.productId}`)
    setAlternateLink('x-default', `${origin}/en/product/${product.productId}`)
}

function setMeta(name, content) {
    let element = document.querySelector(`meta[name="${name}"]`)
    if (!element) {
        element = document.createElement('meta')
        element.setAttribute('name', name)
        document.head.appendChild(element)
    }

    element.setAttribute('content', content)
}

function setMetaProperty(property, content) {
    let element = document.querySelector(`meta[property="${property}"]`)
    if (!element) {
        element = document.createElement('meta')
        element.setAttribute('property', property)
        document.head.appendChild(element)
    }

    element.setAttribute('content', content)
}

function setLink(rel, href) {
    let element = document.querySelector(`link[rel="${rel}"]`)
    if (!element) {
        element = document.createElement('link')
        element.setAttribute('rel', rel)
        document.head.appendChild(element)
    }

    element.setAttribute('href', href)
}

function setAlternateLink(hreflang, href) {
    let element = document.querySelector(`link[rel="alternate"][hreflang="${hreflang}"]`)
    if (!element) {
        element = document.createElement('link')
        element.setAttribute('rel', 'alternate')
        element.setAttribute('hreflang', hreflang)
        document.head.appendChild(element)
    }

    element.setAttribute('href', href)
}

export default MarketplaceProductPublicPage

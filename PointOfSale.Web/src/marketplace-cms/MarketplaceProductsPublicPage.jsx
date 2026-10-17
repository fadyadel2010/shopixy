import { startTransition, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { useFormatting } from '../hooks/useFormatting'
import { fetchMarketplaceFooter } from './marketplaceFooterPublicService'
import { fetchMarketplaceProducts } from './marketplaceProductsPublicService'
import MarketplaceHeader from './MarketplaceHeader'
import MarketplacePublicFooter from './MarketplacePublicFooter'
import useLanguage from './useLanguage'
import {
    buildMarketplacePagePath,
    buildMarketplaceProductPath,
} from './marketplaceLanguage'
import { getMarketplaceTranslationState } from './marketplaceTranslationUi'

const PAGE_SIZE = 12
const CURRENCY_UNAVAILABLE_LABEL = 'Currency unavailable'

function formatBackendCurrency(formatCurrency, value, currency) {
    const normalizedCurrency = String(currency || '').trim().toUpperCase()
    if (!normalizedCurrency) {
        return CURRENCY_UNAVAILABLE_LABEL
    }

    return formatCurrency(value, normalizedCurrency)
}

export function MarketplaceProductsPublicPage() {
    const { lang } = useParams()
    const location = useLocation()
    const [searchParams, setSearchParams] = useSearchParams()
    const { currentLang, isRTL } = useLanguage(lang)
    const { t } = useTranslation()
    const { formatCurrency, formatNumber } = useFormatting()
    const copy = useMemo(
        () => ({
            badge: t('marketplacePublic.products.badge'),
            title: t('marketplacePublic.products.title'),
            description: t('marketplacePublic.products.description'),
            searchPlaceholder: t('marketplacePublic.products.searchPlaceholder'),
            searchAction: t('common.search'),
            clearAction: t('marketplacePublic.products.clearAction'),
            allCategories: t('marketplacePublic.products.allCategories'),
            noResultsTitle: t('marketplacePublic.products.noResultsTitle'),
            noResultsMessage: t('marketplacePublic.products.noResultsMessage'),
            errorTitle: t('marketplacePublic.products.errorTitle'),
            errorAction: t('marketplacePublic.products.errorAction'),
            autoTranslated: t('marketplacePublic.common.autoTranslated'),
            categoryFallbackHint: t('marketplacePublic.common.categoryFallbackHint'),
            available: t('marketplacePublic.common.available'),
            unavailable: t('marketplacePublic.common.unavailable'),
            resultsLabel: t('marketplacePublic.products.resultsLabel'),
            pageLabel: t('marketplacePublic.products.pageLabel'),
            followLabel: t('marketplacePublic.common.followLabel'),
            loading: t('marketplacePublic.products.loading'),
            imageFallback: t('marketplacePublic.common.imageFallback'),
        }),
        [t],
    )
    const [draftSearch, setDraftSearch] = useState(searchParams.get('q') || '')
    const [productsResponse, setProductsResponse] = useState(null)
    const [footer, setFooter] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')
    const [reloadNonce, setReloadNonce] = useState(0)

    const activeSearch = (searchParams.get('q') || '').trim()
    const activeCategory = searchParams.get('category') || ''
    const activePage = Math.max(1, Number.parseInt(searchParams.get('page') || '1', 10) || 1)

    useEffect(() => {
        setDraftSearch(searchParams.get('q') || '')
    }, [searchParams])

    useEffect(() => {
        let cancelled = false

        async function loadProducts() {
            setLoading(true)
            setError('')

            try {
                const [productsResult, footerResult] = await Promise.allSettled([
                    fetchMarketplaceProducts({
                        language: currentLang,
                        search: activeSearch,
                        category: activeCategory,
                        page: activePage,
                        pageSize: PAGE_SIZE,
                    }),
                    fetchMarketplaceFooter(currentLang),
                ])

                if (productsResult.status !== 'fulfilled') {
                    throw productsResult.reason
                }

                if (!cancelled) {
                    setProductsResponse(productsResult.value)
                    setFooter(footerResult.status === 'fulfilled' ? footerResult.value : null)
                }
            } catch (loadError) {
                if (!cancelled) {
                    console.error('Failed to load marketplace products', loadError)
                    setError(copy.errorTitle)
                }
            } finally {
                if (!cancelled) {
                    setLoading(false)
                }
            }
        }

        loadProducts()

        return () => {
            cancelled = true
        }
    }, [activeCategory, activePage, activeSearch, copy.errorTitle, currentLang, reloadNonce])

    useEffect(() => {
        if (loading) {
            return
        }

        applySeo(currentLang, activeSearch, activeCategory, activePage, copy)
    }, [activeCategory, activePage, activeSearch, copy, currentLang, loading])

    const items = useMemo(() => productsResponse?.items || [], [productsResponse?.items])
    const totalCount = productsResponse?.totalCount || 0
    const totalPages = productsResponse?.totalPages || 0

    const categories = useMemo(() => {
        const values = new Set(items.map((item) => item.category).filter(Boolean))
        if (activeCategory) {
            values.add(activeCategory)
        }

        return Array.from(values).sort((left, right) =>
            String(left).localeCompare(String(right), currentLang === 'ar' ? 'ar' : 'en'),
        )
    }, [activeCategory, currentLang, items])

    if (!lang) {
        return <Navigate to={`${buildMarketplacePagePath(currentLang, 'products')}${location.search}`} replace />
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
                    title={copy.title}
                    description={copy.description}
                    showBrowseLink={false}
                    aside={
                        <div className={`rounded-[28px] border border-slate-200 bg-white px-6 py-5 shadow-[0_20px_60px_-40px_rgba(15,23,42,0.35)] ${isRTL ? 'text-right' : 'text-left'}`}>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                {formatNumber(totalCount)} {copy.resultsLabel}
                            </p>
                            <p className="mt-2 text-2xl font-semibold text-slate-950">
                                {copy.pageLabel} {formatNumber(activePage)}
                            </p>
                        </div>
                    }
                >
                    <div className="space-y-5">
                        <form
                            onSubmit={(event) => {
                                event.preventDefault()
                                updateFilters(setSearchParams, {
                                    q: draftSearch.trim(),
                                    category: activeCategory,
                                    page: '1',
                                })
                            }}
                            className="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto_auto]"
                        >
                            <input
                                value={draftSearch}
                                onChange={(event) => setDraftSearch(event.target.value)}
                                placeholder={copy.searchPlaceholder}
                                className={`rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-amber-300 focus:ring-2 focus:ring-amber-100 ${isRTL ? 'text-right' : 'text-left'}`}
                            />
                            <button
                                type="submit"
                                className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-medium text-white transition hover:bg-slate-800"
                            >
                                {copy.searchAction}
                            </button>
                            <button
                                type="button"
                                onClick={() => {
                                    setDraftSearch('')
                                    updateFilters(setSearchParams, {
                                        q: '',
                                        category: '',
                                        page: '1',
                                    })
                                }}
                                className="rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-medium text-slate-600 transition hover:border-slate-300 hover:text-slate-950"
                            >
                                {copy.clearAction}
                            </button>
                        </form>

                        <div className={`flex flex-wrap gap-3 ${isRTL ? 'justify-end' : 'justify-start'}`}>
                            <button
                                type="button"
                                onClick={() => updateFilters(setSearchParams, { q: activeSearch, category: '', page: '1' })}
                                className={[
                                    'rounded-full px-4 py-2 text-sm font-medium transition',
                                    activeCategory
                                        ? 'border border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:text-slate-950'
                                        : 'border border-slate-900 bg-slate-900 text-white shadow-sm',
                                ].join(' ')}
                            >
                                {copy.allCategories}
                            </button>
                            {categories.map((category) => (
                                <button
                                    key={category}
                                    type="button"
                                    onClick={() => updateFilters(setSearchParams, { q: activeSearch, category, page: '1' })}
                                    className={[
                                        'rounded-full px-4 py-2 text-sm font-medium transition',
                                        activeCategory === category
                                            ? 'border border-slate-900 bg-slate-900 text-white shadow-sm'
                                            : 'border border-amber-200 bg-amber-50 text-amber-800 hover:border-amber-300 hover:bg-amber-100',
                                    ].join(' ')}
                                >
                                    {category}
                                </button>
                            ))}
                        </div>
                    </div>
                </MarketplaceHeader>

                {loading ? <MarketplaceProductsLoadingState language={currentLang} copy={copy} /> : null}

                {!loading && error ? (
                    <section className="rounded-[36px] border border-rose-200 bg-white px-8 py-10 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                        <h2 className={`text-xl font-semibold text-slate-950 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {copy.errorTitle}
                        </h2>
                        <div className={`mt-4 ${isRTL ? 'text-right' : 'text-left'}`}>
                            <button
                                type="button"
                                onClick={() => setReloadNonce((value) => value + 1)}
                                className="rounded-full bg-slate-950 px-5 py-3 text-sm font-medium text-white transition hover:bg-slate-800"
                            >
                                {copy.errorAction}
                            </button>
                        </div>
                    </section>
                ) : null}

                {!loading && !error && items.length === 0 ? (
                    <section className="rounded-[36px] border border-amber-100 bg-white px-8 py-12 text-center shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                        <h2 className="text-2xl font-semibold text-slate-950">{copy.noResultsTitle}</h2>
                        <p className="mt-3 text-sm leading-7 text-slate-500">{copy.noResultsMessage}</p>
                    </section>
                ) : null}

                {!loading && !error && items.length > 0 ? (
                    <>
                        <section className="grid gap-6 md:grid-cols-2 xl:grid-cols-3">
                            {items.map((product) => (
                                <MarketplaceProductCard
                                    key={product.productId}
                                    product={product}
                                    language={currentLang}
                                    copy={copy}
                                    isRTL={isRTL}
                                    formatCurrency={formatCurrency}
                                />
                            ))}
                        </section>

                        {totalPages > 1 ? (
                            <nav className={`flex items-center justify-between gap-4 rounded-[30px] border border-amber-100 bg-white px-6 py-5 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)] ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <button
                                    type="button"
                                    disabled={activePage <= 1}
                                    onClick={() => updateFilters(setSearchParams, {
                                        q: activeSearch,
                                        category: activeCategory,
                                        page: String(Math.max(1, activePage - 1)),
                                    })}
                                    className="rounded-full border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                                >
                                    {t('common.previous')}
                                </button>
                                <p className="text-sm text-slate-500">
                                    {t('marketplacePublic.products.pageSummary', {
                                        page: formatNumber(activePage),
                                        total: formatNumber(Math.max(totalPages, 1)),
                                    })}
                                </p>
                                <button
                                    type="button"
                                    disabled={activePage >= totalPages}
                                    onClick={() => updateFilters(setSearchParams, {
                                        q: activeSearch,
                                        category: activeCategory,
                                        page: String(Math.min(totalPages, activePage + 1)),
                                    })}
                                    className="rounded-full border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                                >
                                    {t('common.next')}
                                </button>
                            </nav>
                        ) : null}
                    </>
                ) : null}

                <MarketplacePublicFooter footer={footer} language={currentLang} followLabel={copy.followLabel} />
            </div>
        </div>
    )
}

function MarketplaceProductsLoadingState({ language, copy }) {
    return (
        <section className="space-y-6">
            <div className={`rounded-[32px] border border-amber-100 bg-white px-8 py-6 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)] ${language === 'ar' ? 'text-right' : 'text-left'}`}>
                <p className="text-sm font-medium text-slate-500">{copy.loading}</p>
            </div>
            <div className="grid gap-6 md:grid-cols-2 xl:grid-cols-3">
                {Array.from({ length: 6 }).map((_, index) => (
                    <div key={index} className="animate-pulse rounded-[30px] border border-amber-100 bg-white p-5 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                        <div className="h-52 rounded-[24px] bg-amber-50" />
                        <div className="mt-5 h-4 w-24 rounded-full bg-amber-100" />
                        <div className="mt-4 h-6 w-4/5 rounded-full bg-slate-100" />
                        <div className="mt-3 h-4 w-2/3 rounded-full bg-slate-100" />
                        <div className="mt-6 h-10 rounded-2xl bg-slate-100" />
                    </div>
                ))}
            </div>
        </section>
    )
}

function MarketplaceProductCard({ product, language, copy, isRTL, formatCurrency }) {
    const translationState = getMarketplaceTranslationState(product, language)

    return (
        <Link
            to={buildMarketplaceProductPath(language, product.productId)}
            className="group overflow-hidden rounded-[30px] border border-amber-100 bg-white p-5 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)] transition duration-200 hover:-translate-y-1 hover:shadow-[0_30px_90px_-52px_rgba(120,53,15,0.4)]"
        >
            <div className="overflow-hidden rounded-[24px] bg-amber-50">
                {product.images?.[0] ? (
                    <img
                        src={product.images[0]}
                        alt={product.name}
                        className="h-56 w-full object-cover transition duration-300 group-hover:scale-[1.03]"
                        loading="lazy"
                    />
                ) : (
                    <div className="flex h-56 items-center justify-center text-sm text-amber-700">
                        {copy.imageFallback}
                    </div>
                )}
            </div>

            <div className={`mt-5 space-y-4 ${isRTL ? 'text-right' : 'text-left'}`}>
                <div className={`flex flex-wrap gap-2 ${isRTL ? 'justify-end' : 'justify-start'}`}>
                    {product.category ? (
                        <span
                            title={translationState.categoryUsesFallback ? copy.categoryFallbackHint : undefined}
                            className="inline-flex rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-medium text-amber-800"
                        >
                            {product.category}
                        </span>
                    ) : null}
                    <span className="inline-flex rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs font-medium text-slate-600">
                        {product.isAvailable ? copy.available : copy.unavailable}
                    </span>
                </div>

                <div>
                    <h2 className="text-xl font-semibold tracking-tight text-slate-950">{product.name}</h2>
                    <p className="mt-2 text-sm text-slate-500">{product.vendorName}</p>
                </div>

                <div className={`flex flex-wrap gap-2 ${isRTL ? 'justify-end' : 'justify-start'}`}>
                    {translationState.showAutoTranslationBadge ? (
                        <span className="inline-flex items-center gap-2 rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-medium text-sky-700">
                            <span aria-hidden="true">{'\ud83c\udf10'}</span>
                            <span>{copy.autoTranslated}</span>
                        </span>
                    ) : null}
                </div>

                <div className={`flex items-center justify-between gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <p className="text-lg font-semibold text-slate-950">
                        {formatBackendCurrency(formatCurrency, product.price, product.currency)}
                    </p>
                    <span className="text-sm font-medium text-slate-400 transition group-hover:text-slate-950">
                        {isRTL ? '\u2190' : '\u2192'}
                    </span>
                </div>
            </div>
        </Link>
    )
}

function updateFilters(setSearchParams, values) {
    startTransition(() => {
        const nextParams = new URLSearchParams()

        Object.entries(values).forEach(([key, value]) => {
            if (value && String(value).trim()) {
                nextParams.set(key, String(value).trim())
            }
        })

        setSearchParams(nextParams, { replace: true })
    })
}

function applySeo(language, search, category, page, copy) {
    const origin = window.location.origin
    const url = new URL(`${origin}/${language}/products`)

    if (search) {
        url.searchParams.set('q', search)
    }

    if (category) {
        url.searchParams.set('category', category)
    }

    if (page > 1) {
        url.searchParams.set('page', String(page))
    }

    const pageTitle = search ? `${copy.title} - ${search}` : copy.title
    const description = copy.description
    document.title = pageTitle
    setMeta('description', description)
    setMeta('robots', 'index, follow')
    setMetaProperty('og:title', pageTitle)
    setMetaProperty('og:description', description)
    setLink('canonical', url.toString())
    setAlternateLink('en', `${origin}/en/products`)
    setAlternateLink('ar', `${origin}/ar/products`)
    setAlternateLink('x-default', `${origin}/en/products`)
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

export default MarketplaceProductsPublicPage

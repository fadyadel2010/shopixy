import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useMarketplaceSearch } from '../../hooks/useMarketplaceSearch'
import { ServiceCard } from '../../components/ServiceCard'
import { useFormatting } from '../../../hooks/useFormatting'
import type { ServiceSearchRequest } from '../../types'

function useDebounce<T>(value: T, delay: number): T {
    const [debouncedValue, setDebouncedValue] = useState(value)

    useEffect(() => {
        const timer = window.setTimeout(() => setDebouncedValue(value), delay)
        return () => window.clearTimeout(timer)
    }, [value, delay])

    return debouncedValue
}

const PAGE_SIZE = 20
const MAX_SEARCH_LENGTH = 200

const CATEGORY_VALUES = [
    '',
    'Marketing',
    'Design',
    'Development',
    'Video',
    'Writing',
    'Music',
    'Business',
    'Consulting',
] as const

const SORT_VALUES: ServiceSearchRequest['sort'][] = [
    'relevance',
    'rating',
    'newest',
    'price_low',
    'price_high',
]

const normalizePage = (value: string | null) => {
    const parsed = Number(value)
    return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : 1
}

export function DiscoverTab() {
    const { t } = useTranslation()
    const { formatCurrency, formatNumber, isRTL } = useFormatting()
    const [urlParams, setUrlParams] = useSearchParams()

    const rawQuery = urlParams.get('q') || ''
    const urlQuery = rawQuery.slice(0, MAX_SEARCH_LENGTH)
    const rawCategory = urlParams.get('category') || ''
    const rawSort = urlParams.get('sort') || 'relevance'
    const category = CATEGORY_VALUES.includes(rawCategory as typeof CATEGORY_VALUES[number])
        ? rawCategory
        : ''
    const sort = SORT_VALUES.includes(rawSort as ServiceSearchRequest['sort'])
        ? rawSort as ServiceSearchRequest['sort']
        : 'relevance'
    const page = normalizePage(urlParams.get('page'))
    const [searchInput, setSearchInput] = useState(urlQuery)
    const debouncedQuery = useDebounce(searchInput.trim(), 300)
    const isSyncingFromUrl = useRef(false)

    useEffect(() => {
        setSearchInput(current => {
            if (current === urlQuery) {
                return current
            }

            isSyncingFromUrl.current = true
            return urlQuery
        })
    }, [urlQuery])

    useEffect(() => {
        const hasInvalidCategory = rawCategory !== category
        const hasInvalidSort = rawSort !== sort
        const hasInvalidPage = urlParams.has('page') && String(page) !== urlParams.get('page')
        const hasInvalidQuery = rawQuery !== urlQuery

        if (!hasInvalidCategory && !hasInvalidSort && !hasInvalidPage && !hasInvalidQuery) {
            return
        }

        setUrlParams(current => {
            const next = new URLSearchParams(current)
            if (hasInvalidCategory) next.delete('category')
            if (hasInvalidSort) next.delete('sort')
            if (hasInvalidPage) next.delete('page')
            if (hasInvalidQuery) next.set('q', urlQuery)
            return next
        }, { replace: true })
    }, [category, page, rawCategory, rawQuery, rawSort, setUrlParams, sort, urlParams, urlQuery])

    useEffect(() => {
        if (isSyncingFromUrl.current) {
            if (debouncedQuery === urlQuery) {
                isSyncingFromUrl.current = false
            }
            return
        }

        if (debouncedQuery === urlQuery) {
            return
        }

        setUrlParams(current => {
            const next = new URLSearchParams(current)
            if (debouncedQuery) {
                next.set('q', debouncedQuery)
            } else {
                next.delete('q')
            }
            next.delete('page')
            return next
        }, { replace: true })
    }, [debouncedQuery, setUrlParams, urlQuery])

    const request = useMemo<ServiceSearchRequest>(() => ({
        query: urlQuery || undefined,
        category: category || undefined,
        page,
        pageSize: PAGE_SIZE,
        sort,
    }), [category, page, sort, urlQuery])

    const {
        data: searchData,
        isLoading,
        isFetching,
        isError,
        isRefetchError,
        refetch,
    } = useMarketplaceSearch(request)
    const authorityFailed = isError || isRefetchError
    const isValidating = isLoading || isFetching
    const data = authorityFailed || isValidating ? undefined : searchData

    const updateDiscoveryParams = (
        updates: { category?: string; sort?: ServiceSearchRequest['sort']; page?: number },
        replace = false,
    ) => {
        setUrlParams(current => {
            const next = new URLSearchParams(current)

            if (updates.category !== undefined) {
                if (updates.category) next.set('category', updates.category)
                else next.delete('category')
            }

            if (updates.sort !== undefined) {
                if (updates.sort === 'relevance') next.delete('sort')
                else next.set('sort', updates.sort)
            }

            if (updates.page !== undefined) {
                if (updates.page <= 1) next.delete('page')
                else next.set('page', String(updates.page))
            }

            return next
        }, { replace })
    }

    const totalPages = data ? Math.ceil(data.totalCount / data.pageSize) : 0

    useEffect(() => {
        if (data && data.totalCount > 0 && page > totalPages) {
            setUrlParams(current => {
                const next = new URLSearchParams(current)
                if (totalPages <= 1) next.delete('page')
                else next.set('page', String(totalPages))
                return next
            }, { replace: true })
        }
    }, [data, page, setUrlParams, totalPages])

    return (
        <div className="space-y-6" dir={isRTL ? 'rtl' : 'ltr'}>
            <div className="card">
                <div className="flex flex-col md:flex-row gap-4">
                    <div className="flex-1">
                        <input
                            type="text"
                            placeholder={t('servicesMarketplace.discover.searchPlaceholder')}
                            className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                            maxLength={MAX_SEARCH_LENGTH}
                            value={searchInput}
                            onChange={(event) => setSearchInput(event.target.value)}
                        />
                    </div>
                    <select
                        className="input md:w-48"
                        value={category}
                        onChange={(event) =>
                            updateDiscoveryParams({ category: event.target.value, page: 1 })}
                    >
                        {CATEGORY_VALUES.map((categoryValue) => (
                            <option key={categoryValue || 'all'} value={categoryValue}>
                                {t(
                                    categoryValue
                                        ? `servicesMarketplace.categories.${categoryValue}`
                                        : 'servicesMarketplace.discover.allCategories',
                                )}
                            </option>
                        ))}
                    </select>
                    <select
                        className="input md:w-48"
                        value={sort}
                        onChange={(event) =>
                            updateDiscoveryParams({
                                sort: event.target.value as ServiceSearchRequest['sort'],
                                page: 1,
                            })}
                    >
                        {SORT_VALUES.map((sortValue) => (
                            <option key={sortValue} value={sortValue}>
                                {t(`servicesMarketplace.discover.sort.${sortValue}`)}
                            </option>
                        ))}
                    </select>
                </div>
            </div>

            {isValidating ? (
                <div className="flex justify-center py-12">
                    <div className="text-gray-500">{t('servicesMarketplace.discover.loading')}</div>
                </div>
            ) : null}

            {!isValidating && authorityFailed ? (
                <div className="card text-center py-12">
                    <p className="text-gray-600 mb-4">
                        {t('servicesMarketplace.discover.searchFailed', {
                            defaultValue: 'Search is temporarily unavailable. Current results were cleared.',
                        })}
                    </p>
                    <button className="btn-secondary" onClick={() => void refetch()}>
                        {t('common.retry', { defaultValue: 'Retry' })}
                    </button>
                </div>
            ) : null}

            {!isValidating && data && data.items.length > 0 ? (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
                    {data.items.map((service) => (
                        <ServiceCard key={service.serviceId} service={service} formatCurrency={formatCurrency} formatNumber={formatNumber} isRTL={isRTL} />
                    ))}
                </div>
            ) : null}

            {!isValidating && data && data.totalCount === 0 ? (
                <div className="card text-center py-12">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">
                        {t('servicesMarketplace.discover.emptyTitle')}
                    </h3>
                    <p className="text-gray-500">{t('servicesMarketplace.discover.emptyDescription')}</p>
                </div>
            ) : null}

            {data && data.totalCount > 0 && totalPages > 1 ? (
                <div className={`flex justify-center items-center gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <button
                        className="btn-secondary"
                        disabled={page === 1}
                        onClick={() => updateDiscoveryParams({ page: page - 1 })}
                    >
                        {t('common.previous')}
                    </button>
                    <span className="px-4 py-2 text-gray-700">
                        {t('servicesMarketplace.discover.pageSummary', {
                            page: formatNumber(page),
                            total: formatNumber(totalPages),
                        })}
                    </span>
                    <button
                        className="btn-secondary"
                        disabled={page >= totalPages}
                        onClick={() => updateDiscoveryParams({ page: page + 1 })}
                    >
                        {t('common.next')}
                    </button>
                </div>
            ) : null}
        </div>
    )
}

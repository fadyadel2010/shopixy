import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../../../../services/api'

export function CuratedProductSelector({ ids = [], onChange, maxItems = 8, limit = 50 }) {
    const { t, i18n } = useTranslation()
    const [query, setQuery] = useState('')
    const [results, setResults] = useState([])
    const [selected, setSelected] = useState([])
    const [searchError, setSearchError] = useState(false)
    const [loading, setLoading] = useState(true)
    const [loadingSelected, setLoadingSelected] = useState(ids.length > 0)
    const selectedKey = ids.join(',')

    useEffect(() => {
        const controller = new AbortController()
        if (!ids.length) {
            setSelected([])
            setLoadingSelected(false)
            return () => controller.abort()
        }
        setLoadingSelected(true)
        api.get('/products/lookup/selected', { params: { ids }, paramsSerializer: { indexes: null }, signal: controller.signal })
            .then(({ data }) => setSelected(Array.isArray(data) ? data : []))
            .catch(() => { if (!controller.signal.aborted) setSearchError(true) })
            .finally(() => { if (!controller.signal.aborted) setLoadingSelected(false) })
        return () => controller.abort()
    }, [selectedKey]) // Persisted IDs, never cached display names, are the authority.

    useEffect(() => {
        const controller = new AbortController()
        setLoading(true)
        const timer = setTimeout(() => {
            setLoading(true)
            api.get('/products/lookup', {
                params: { search: query.trim(), isActive: true, pageSize: 30 },
                signal: controller.signal,
            }).then(({ data }) => {
                setResults(Array.isArray(data) ? data : [])
                setSearchError(false)
            }).catch(() => { if (!controller.signal.aborted) setSearchError(true) })
                .finally(() => { if (!controller.signal.aborted) setLoading(false) })
        }, 250)
        return () => { clearTimeout(timer); controller.abort() }
    }, [query])

    const name = (product) => i18n.language === 'ar' && product.nameAr ? product.nameAr : product.name

    return (
        <div className="space-y-3">
            <label className="block text-sm font-medium text-gray-700" htmlFor="curated-product-search">
                {t('storefront.homepageEditor.productSearch')}
            </label>
            <input id="curated-product-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)}
                placeholder={t('storefront.homepageEditor.productSearchPlaceholder')}
                className="w-full rounded-lg border border-gray-300 px-3 py-2" />
            {searchError && <p role="alert" className="text-sm text-red-600">{t('storefront.homepageEditor.productLookupFailed')}</p>}
            {loading && <p className="text-sm text-gray-500">{t('storefront.homepageEditor.searching')}</p>}
            <div className="max-h-40 overflow-auto rounded-lg border border-gray-200" aria-label={t('storefront.homepageEditor.searchResults')}>
                {!loading && results.length === 0 && <p className="p-3 text-sm text-gray-500">{t('storefront.homepageEditor.noProducts')}</p>}
                {results.map((product) => (
                    <button key={product.id} type="button" disabled={ids.some((id) => id.toLowerCase() === product.id.toLowerCase()) || ids.length >= limit}
                        onClick={() => onChange([...ids, product.id])}
                        className="block w-full border-b border-gray-100 px-3 py-2 text-start text-sm hover:bg-indigo-50 disabled:opacity-50">
                        {name(product)} {product.sku && <span dir="ltr" className="text-gray-500">· {product.sku}</span>}
                        {product.hasVariants && <span className="ms-2 text-xs text-gray-500">{t('storefront.homepageEditor.variants')}</span>}
                    </button>
                ))}
            </div>
            <p className="text-xs text-gray-500">{t('storefront.homepageEditor.displayLimit', { count: Number(maxItems) || 8, selected: ids.length, limit })}</p>
            <ol className="space-y-2" aria-label={t('storefront.homepageEditor.selectedProducts')}>
                {ids.map((id, index) => {
                    const product = selected.find((item) => item.id.toLowerCase() === id.toLowerCase())
                    return <li key={id} className="flex items-center gap-2 rounded-lg border border-gray-200 p-2 text-sm">
                        <span className="min-w-0 flex-1 truncate">{product
                            ? <>{name(product)} {product.sku && <span dir="ltr" className="text-gray-500">· {product.sku}</span>}{!product.isActive && <span className="text-amber-700"> · {t('storefront.homepageEditor.unavailable')}</span>}</>
                            : loadingSelected ? t('storefront.homepageEditor.searching') : searchError ? t('storefront.homepageEditor.productLookupFailed') : t('storefront.homepageEditor.unavailable')}</span>
                        <button type="button" aria-label={t('storefront.homepageEditor.moveUp', { name: product ? name(product) : index + 1 })}
                            disabled={index === 0} onClick={() => { const next = [...ids]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; onChange(next) }}>↑</button>
                        <button type="button" aria-label={t('storefront.homepageEditor.moveDown', { name: product ? name(product) : index + 1 })}
                            disabled={index === ids.length - 1} onClick={() => { const next = [...ids]; [next[index + 1], next[index]] = [next[index], next[index + 1]]; onChange(next) }}>↓</button>
                        <button type="button" className="text-red-600" aria-label={t('storefront.homepageEditor.removeProduct', { name: product ? name(product) : index + 1 })}
                            onClick={() => onChange(ids.filter((_, position) => position !== index))}>×</button>
                    </li>
                })}
            </ol>
        </div>
    )
}

import { useEffect } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { ChevronRight } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { resolveStorefrontScreen } from './storefrontInformationArchitecture'

export function StorefrontBreadcrumbs({
    appendCurrentLabel = false,
    className = '',
    currentLabel = '',
    onNavigate,
}) {
    const location = useLocation()
    const { t, i18n } = useTranslation()
    const screen = resolveStorefrontScreen(location.pathname, location.search)
    const isRTL = i18n.language === 'ar'

    const title = currentLabel || (screen ? t(screen.titleKey, screen.fallbackTitle) : null)
    const breadcrumbs = screen?.breadcrumbs
        ? screen.breadcrumbs.map((item, index) => (
            currentLabel && !appendCurrentLabel && index === screen.breadcrumbs.length - 1
                ? { ...item, customLabel: currentLabel }
                : item
        ))
        : []

    if (currentLabel && appendCurrentLabel && breadcrumbs.length > 0) {
        const previousCurrent = breadcrumbs[breadcrumbs.length - 1]
        breadcrumbs[breadcrumbs.length - 1] = {
            ...previousCurrent,
            path: `${location.pathname}${location.search}`,
        }
        breadcrumbs.push({ customLabel: currentLabel, fallbackLabel: currentLabel, labelKey: 'dynamic-current' })
    }

    useEffect(() => {
        if (!title) return undefined

        const previousTitle = document.title
        document.title = `${title} | Storefront Administration`
        return () => {
            document.title = previousTitle
        }
    }, [title])

    if (!screen) return null

    return (
        <nav aria-label={t('storefront.ia.breadcrumbLabel', 'Storefront breadcrumb')} className={className}>
            <ol className="flex flex-wrap items-center gap-1 text-sm text-gray-600">
                {breadcrumbs.map((item, index) => {
                    const isCurrent = index === breadcrumbs.length - 1
                    const label = item.customLabel || t(item.labelKey, item.fallbackLabel)

                    return (
                        <li key={`${item.labelKey}-${index}`} className="flex items-center gap-1">
                            {index > 0 ? (
                                <ChevronRight
                                    aria-hidden="true"
                                    size={16}
                                    className={`text-gray-400 ${isRTL ? 'rotate-180' : ''}`}
                                />
                            ) : null}
                            {isCurrent || !item.path ? (
                                <span aria-current={isCurrent ? 'page' : undefined} className={isCurrent ? 'font-medium text-gray-900' : ''}>
                                    {label}
                                </span>
                            ) : (
                                <Link
                                    className="rounded hover:text-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                                    onClick={(event) => {
                                        if (onNavigate?.(item.path) === false) event.preventDefault()
                                    }}
                                    to={item.path}
                                >
                                    {label}
                                </Link>
                            )}
                        </li>
                    )
                })}
            </ol>
        </nav>
    )
}

export default StorefrontBreadcrumbs

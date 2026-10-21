import { useContext, useEffect } from 'react'
import { matchRoutes, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'
import { AdminRouteContext, useSmartNavigation } from './SmartNavigationContext'
import { resolveStorefrontScreen, StorefrontBreadcrumbs } from '../modules/storefront/informationArchitecture'

export default function NavigationBreadcrumbs() {
    const { entries } = useSmartNavigation()
    const routes = useContext(AdminRouteContext)
    const { language } = useRTL()
    const { t } = useTranslation()
    const location = useLocation()
    const existing = resolveStorefrontScreen(location.pathname, location.search)
    const ownsBreadcrumb = matchRoutes(routes, location)?.some(match => match.route.handle?.ownsNavigationBreadcrumb)
        || (existing && location.pathname === '/settings')
    const entry = entries.find(value => value.to.pathname === location.pathname && (!value.to.search || value.to.search === location.search))
    const title = !existing && entry ? `${entry.labels[language]} | ${entry.modules[language]}` : null
    useEffect(() => {
        if (!title) return
        const previous = document.title
        document.title = title
        return () => { document.title = previous }
    }, [title])
    if (ownsBreadcrumb || !entry) return null
    if (existing) return <StorefrontBreadcrumbs className="mb-3" />
    return <nav aria-label={t('smartNavigation.breadcrumb')} className="mb-3 text-xs text-gray-500">
        <bdi>{entry.modules[language]}</bdi>{entry.breadcrumbs[language].map((label, index) => <span key={index}> / <bdi aria-current={index === entry.breadcrumbs[language].length - 1 ? 'page' : undefined}>{label}</bdi></span>)}
    </nav>
}

import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { StorefrontBreadcrumbs } from '../modules/storefront/informationArchitecture'

/**
 * OperationsOverview
 *
 * Canonical entry-point screen for the Storefront Operations domain.
 *
 * Source: ONLINE_STORE_CANONICAL_BLUEPRINT.md §8.1, §10.2
 * Wave 1 — Information Architecture Migration
 *
 * This screen is intentionally lightweight: it provides orientation and quick
 * navigation (deep links) to each canonical Operations sub-group. It is a
 * presentation component only — no runtime, backend. API, DTO, or authorization
 * changes are introduced. The component is rendered at the canonical route
 * /ecommerce/overview.
 */
const OPERATIONS_TILES = [
    {
        key: 'overviewOrders',
        labelKey: 'storefront.operationsOrders',
        fallbackLabel: 'Orders',
        target: '/ecommerce/orders',
    },
    {
        key: 'overviewPayments',
        labelKey: 'storefront.operationsPayments',
        fallbackLabel: 'Payments',
        target: '/ecommerce/payment-methods',
    },
    {
        key: 'overviewCatalog',
        labelKey: 'storefront.operationsCatalog',
        fallbackLabel: 'Catalog',
        target: '/ecommerce/coupons',
    },
    {
        key: 'overviewShipping',
        labelKey: 'storefront.operationsShipping',
        fallbackLabel: 'Shipping',
        target: '/ecommerce/shipping-methods',
    },
    {
        key: 'overviewMarketing',
        labelKey: 'storefront.operationsMarketing',
        fallbackLabel: 'Marketing',
        target: '/ecommerce/pixel-tracking',
    },
    {
        key: 'overviewSettings',
        labelKey: 'storefront.operationsSettings',
        fallbackLabel: 'Settings',
        target: '/settings/analytics',
    },
]

export default function OperationsOverview() {
    const { t } = useTranslation()

    return (
        <div className="operations-overview container mx-auto py-10">
            <StorefrontBreadcrumbs className="mb-6" />

            <h1 className="text-2xl font-bold mb-6">{t('storefront.operationsOverview', 'Overview')}</h1>

            <p className="text-gray-600 mb-8 max-w-2xl">
                {t(
                    'storefront.operationsOverviewDescription',
                    'Manage orders, payments, catalog, shipping, and marketing for your online store. ' +
                        'Select a section below to begin.',
                )}
            </p>

            <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-4">
                {OPERATIONS_TILES.map((tile) => (
                    <NavLink
                        key={tile.key}
                        to={tile.target}
                        className="block p-4 border border-gray-200 rounded-lg hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-blue-500"
                    >
                        <span className="font-medium text-gray-900">{t(tile.labelKey, tile.fallbackLabel)}</span>
                    </NavLink>
                ))}
            </div>
        </div>
    )
}

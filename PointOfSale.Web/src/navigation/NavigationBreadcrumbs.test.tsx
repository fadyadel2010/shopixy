import { act } from 'react'
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { createRoutesFromChildren, MemoryRouter, Route } from 'react-router-dom'
import { I18nextProvider } from 'react-i18next'
import { AdminRouteContext, SmartNavigationContext } from './SmartNavigationContext'
import NavigationBreadcrumbs from './NavigationBreadcrumbs'
import { buildNavigationRegistry } from './navigationRegistry'
import { navigationOwner } from './navigation.fixture'
import i18n from '../i18n/config'

const renderBreadcrumb = (path: string, ownsBreadcrumb = false) => {
    const routes = createRoutesFromChildren(<Route path="/" handle={{ application: 'pos' }}>
        <Route path="ecommerce/payment-methods" element={<p>Payments</p>} handle={{ ownsNavigationBreadcrumb: ownsBreadcrumb }} />
        <Route path="products" element={<p>Products</p>} />
    </Route>)
    return render(<I18nextProvider i18n={i18n}><MemoryRouter initialEntries={[path]}>
        <AdminRouteContext.Provider value={routes}><SmartNavigationContext.Provider value={{
            entries: buildNavigationRegistry(routes, navigationOwner, i18n), favorites: [], recent: [],
            toggleFavorite() {}, clearRecent() {}, recordVisit() {}, scope: 'test', user: navigationOwner,
        }}><NavigationBreadcrumbs /></SmartNavigationContext.Provider></AdminRouteContext.Provider>
    </MemoryRouter></I18nextProvider>)
}

describe('navigation-facing breadcrumbs and document titles', () => {
    afterEach(cleanup)
    it('reuses existing Storefront breadcrumbs for screens without a page-owned breadcrumb and updates locale', async () => {
        await i18n.changeLanguage('en')
        renderBreadcrumb('/ecommerce/payment-methods')
        expect(screen.getByRole('navigation')).toHaveTextContent('Payment Methods')
        expect(document.title).toContain('Storefront Administration')
        await act(() => i18n.changeLanguage('ar'))
        expect(screen.getByRole('navigation')).toHaveTextContent('طرق الدفع')
        expect(document.title).toContain('إدارة المتجر الإلكتروني')
    })
    it('does not duplicate breadcrumbs already owned by the route/page frame', async () => {
        await i18n.changeLanguage('en')
        renderBreadcrumb('/ecommerce/payment-methods', true)
        expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    })
    it('uses menu hierarchy and module for POS screens without changing their route', async () => {
        await i18n.changeLanguage('en')
        renderBreadcrumb('/products')
        expect(screen.getByRole('navigation')).toHaveTextContent('POS Admin / Database / Item Card')
        await act(() => i18n.changeLanguage('ar'))
        expect(screen.getByRole('navigation')).toHaveTextContent('إدارة نقاط البيع')
        expect(document.title).toContain('كارت الصنف')
    })
})

import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createRoutesFromChildren, Link, MemoryRouter, Outlet, Route, useLocation } from 'react-router-dom'
import { I18nextProvider } from 'react-i18next'
import CommandPalette from './CommandPalette'
import SmartNavigationProvider from './SmartNavigationProvider'
import AdminNavigationRoutes from './AdminNavigationRoutes'
import { AdminRouteContext } from './SmartNavigationContext'
import { SidebarFavorites } from './SidebarFavorites'
import { useSmartNavigation } from './SmartNavigationContext'
import PosPermissionRouteGuard from '../components/PosPermissionRouteGuard'
import { useAuthStore } from '../store/authStore'
import { navigationOwner } from './navigation.fixture'
import { navigationStorageKey } from './navigationPreferences'
import i18n from '../i18n/config'

function Favorites() {
    const navigation = useSmartNavigation()!
    return <SidebarFavorites navigation={navigation} onNavigate={() => {}} />
}
function Content() {
    const location = useLocation()
    return <><CommandPalette /><Favorites /><input aria-label="Ordinary input" /><textarea aria-label="Ordinary textarea" />
        <div contentEditable suppressContentEditableWarning aria-label="Editor" />
        <output data-testid="route">{location.pathname}{location.search}</output><Outlet /></>
}
function Layout() { return <SmartNavigationProvider><Content /></SmartNavigationProvider> }
const routeElements = <Route path="/" element={<Layout />} handle={{ application: 'pos' }}>
    <Route element={<PosPermissionRouteGuard permission="dashboard.view" />}><Route index element={<p>Home page</p>} /></Route>
    <Route element={<PosPermissionRouteGuard permission="catalog.view" />}><Route path="products" element={<p>Catalog page</p>} /></Route>
    <Route element={<PosPermissionRouteGuard permission="storefront.payments.manage" />}><Route path="ecommerce/payment-methods" element={<p>Payment page</p>} /></Route>
    <Route element={<PosPermissionRouteGuard permission="reports.financial.view" />}><Route path="treasury" element={<p>Treasury page</p>} /></Route>
    <Route path="not-registered" element={<p>Other page</p>} />
    <Route path="sidebar" element={<Link to="/products">Ordinary navigation</Link>} />
</Route>
const routes = createRoutesFromChildren(routeElements)
const mount = (path = '/') => render(<I18nextProvider i18n={i18n}><MemoryRouter initialEntries={[path]}>
    <AdminRouteContext.Provider value={routes}><AdminNavigationRoutes>{routeElements}</AdminNavigationRoutes></AdminRouteContext.Provider>
</MemoryRouter></I18nextProvider>)
const open = async () => {
    fireEvent.keyDown(document.body, { ctrlKey: true, key: 'k' })
    return screen.findByRole('combobox')
}
const palette = () => screen.getByRole('dialog')
const stored = () => JSON.parse(localStorage.getItem(navigationStorageKey(navigationOwner)!) || '{}')

describe('integrated navigation dialog, accepted visits and actor preferences', () => {
    beforeEach(async () => {
        localStorage.clear()
        useAuthStore.getState().login(navigationOwner)
        await i18n.changeLanguage('en')
    })
    afterEach(cleanup)

    it('opens visibly or with Ctrl+K, focuses input, traps Tab, escapes and restores the original focus', async () => {
        const user = userEvent.setup()
        mount()
        const trigger = screen.getByRole('button', { name: 'Find a page' })
        trigger.focus()
        const input = await open()
        expect(input).toHaveFocus()
        await user.tab({ shift: true })
        expect(palette()).toContainElement(document.activeElement as HTMLElement)
        fireEvent.keyDown(palette(), { key: 'Escape' })
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(trigger).toHaveFocus()
        await user.click(trigger)
        expect(await screen.findByRole('combobox')).toHaveFocus()
    })

    it('preserves typing and unrelated modal shortcuts', async () => {
        mount()
        for (const label of ['Ordinary input', 'Ordinary textarea', 'Editor']) {
            fireEvent.keyDown(screen.getByLabelText(label), { ctrlKey: true, key: 'k' })
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
        }
        const modal = document.createElement('div')
        modal.setAttribute('role', 'dialog')
        document.body.append(modal)
        fireEvent.keyDown(document.body, { ctrlKey: true, key: 'k' })
        expect(screen.queryByTestId('navigation-palette')).not.toBeInTheDocument()
        modal.remove()
        expect(await open()).toHaveFocus()
    })

    it('supports repeated bilingual queries, empty/no-results, ArrowUp/Down and Enter route navigation', async () => {
        mount()
        const input = await open()
        for (const query of ['Payment Methods', 'طرق الدفع', 'Payment options']) {
            fireEvent.change(input, { target: { value: query } })
            expect(within(palette()).getAllByRole('row')).toHaveLength(1)
            expect(palette()).toHaveTextContent('Storefront management')
            expect(palette()).toHaveTextContent('Operations')
        }
        fireEvent.change(input, { target: { value: 'unfindable-xyz' } })
        expect(palette()).toHaveTextContent('No matching pages')
        fireEvent.keyDown(input, { key: 'Enter' })
        expect(screen.getByTestId('route')).toHaveTextContent('/')
        fireEvent.change(input, { target: { value: '' } })
        const initial = input.getAttribute('aria-activedescendant')
        fireEvent.keyDown(input, { key: 'ArrowDown' })
        expect(input.getAttribute('aria-activedescendant')).not.toBe(initial)
        fireEvent.keyDown(input, { key: 'ArrowUp' })
        expect(input.getAttribute('aria-activedescendant')).toBe(initial)
        fireEvent.change(input, { target: { value: 'Products' } })
        fireEvent.keyDown(input, { key: 'Enter' })
        await screen.findByText('Catalog page')
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(stored().recent[0]).toBe('pos:nav.itemCard')
    })

    it('adds/removes favorites without navigating, persists remounts and localizes identity in RTL', async () => {
        const view = mount()
        const input = await open()
        fireEvent.change(input, { target: { value: 'Payment Methods' } })
        fireEvent.click(screen.getByRole('button', { name: 'Add Payment Methods to favorites' }))
        expect(stored().favorites).toEqual(['storefront:payment-methods'])
        expect(screen.getByTestId('route').textContent).toBe('/')
        view.unmount()
        mount()
        expect(stored().favorites).toEqual(['storefront:payment-methods'])
        await open()
        fireEvent.click(within(palette()).getByRole('button', { name: 'Favorites' }))
        await act(() => i18n.changeLanguage('ar'))
        expect(palette()).toHaveAttribute('dir', 'rtl')
        expect(palette()).toHaveTextContent('طرق الدفع')
        expect(palette()).toHaveTextContent('إدارة المتجر الإلكتروني')
        expect(palette().textContent).not.toMatch(/smartNavigation\.|nav\./)
        expect(stored().favorites).toEqual(['storefront:payment-methods'])
        fireEvent.click(within(palette()).getByRole('button', { name: 'إزالة طرق الدفع من المفضلة' }))
        expect(stored().favorites).toEqual([])
        expect(palette()).toHaveTextContent('لا توجد صفحات مفضلة')
    })

    it('records ordinary successful navigation, deduplicates and clears without immediately re-adding the current page', async () => {
        mount('/sidebar')
        fireEvent.click(screen.getByRole('link', { name: 'Ordinary navigation' }))
        await screen.findByText('Catalog page')
        expect(stored().recent).toEqual(['pos:nav.itemCard'])
        await open()
        fireEvent.click(within(palette()).getByRole('button', { name: 'Recent' }))
        fireEvent.click(within(palette()).getByRole('button', { name: 'Clear recent pages' }))
        expect(palette()).toHaveTextContent('No recent pages yet')
        expect(stored().recent).toEqual([])
        await act(() => i18n.changeLanguage('ar'))
        expect(stored().recent).toEqual([])
    })

    it('does not record unregistered or guard-denied destinations or sensitive query values', async () => {
        useAuthStore.getState().hydrateSession({ ...navigationOwner, effectivePermissions: ['dashboard.view'] })
        mount('/products?password=private')
        await screen.findByText('Home page')
        expect(stored().recent).toEqual(['pos:dashboard'])
        expect(JSON.stringify(stored())).not.toContain('private')
        const input = await open()
        fireEvent.change(input, { target: { value: 'Products' } })
        expect(palette()).toHaveTextContent('No matching pages')
    })

    it('removes restricted results/preferences on authority refresh and isolates live user/tenant changes', async () => {
        mount()
        await open()
        fireEvent.click(screen.getByRole('button', { name: 'Add Payment Methods to favorites' }))
        act(() => useAuthStore.getState().hydrateSession({ ...navigationOwner, effectivePermissions: ['dashboard.view'] }))
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
        await open()
        expect(palette()).not.toHaveTextContent('Payment Methods')
        expect(stored().favorites).toEqual([])
        act(() => useAuthStore.getState().hydrateSession({ ...navigationOwner, id: '33333333-3333-4333-8333-333333333333' }))
        expect(screen.getByTestId('navigation-favorites')).toHaveTextContent('Favorites (0)')
        const tenantId = '44444444-4444-4444-8444-444444444444'
        act(() => useAuthStore.getState().hydrateSession({ ...navigationOwner, tenantId,
            vendorLifecycleAuthority: { ...navigationOwner.vendorLifecycleAuthority, vendorId: tenantId } }))
        expect(screen.getByTestId('navigation-favorites')).toHaveTextContent('Favorites (0)')
    })
})

import { describe, expect, it } from 'vitest'
import { isValidElement } from 'react'
import { createRoutesFromChildren, matchRoutes, Navigate, Route } from 'react-router-dom'
import { appRouteElements } from '../App'
import { buildAdminMenu } from '../components/Sidebar'
import i18n from '../i18n/config'
import { SCREENS } from '../modules/storefront/informationArchitecture/storefrontInformationArchitecture'
import { buildNavigationRegistry, normalizeNavigationSearch, resolveNavigationVisit, searchNavigation } from './navigationRegistry'
import { navigationOwner } from './navigation.fixture'
import type { NavigationMenuNode } from './navigation.types'

const routes = createRoutesFromChildren(appRouteElements())
const entries = buildNavigationRegistry(routes, navigationOwner, i18n)
const byPath = (path: string) => entries.find(entry => entry.to.pathname === path)!

describe('actual Admin route/menu navigation inventory', () => {
    it('has unique stable identities and only actual non-redirect, static POS-layout routes', () => {
        expect(entries.length).toBeGreaterThan(90)
        expect(new Set(entries.map(entry => entry.id)).size).toBe(entries.length)
        expect(new Set(entries.map(entry => JSON.stringify(entry.to))).size).toBe(entries.length)
        for (const entry of entries) {
            const matches = matchRoutes(routes, entry.to)!
            const leaf = matches[matches.length - 1].route
            expect(leaf.id).toBe(entry.routeId)
            expect(isValidElement(leaf.element) && leaf.element.type).not.toBe(Navigate)
            expect(matches.some(match => match.route.handle?.application === 'pos')).toBe(true)
            expect(entry.to.pathname).not.toMatch(/[:*]/)
        }
    })

    it('covers every menu destination, including intentional duplicate placements only once', () => {
        const paths: string[] = []
        const visit = (nodes: NavigationMenuNode[]) => nodes.forEach(node => {
            if (node.path) paths.push(node.path)
            if (node.children) visit(node.children)
        })
        visit(buildAdminMenu((key, fallback) => ({ key, fallback }), navigationOwner))
        const destinations = entries.map(entry => `${entry.to.pathname}${entry.to.search || ''}`)
        expect([...new Set(paths)].sort()).toEqual(destinations.sort())
        expect(entries.filter(entry => entry.to.pathname === '/sales-history')).toHaveLength(1)
    })

    it('validates every menu/group label and every owned Storefront title/breadcrumb in BOTH locale resources without fallback', () => {
        const keys = new Set<string>()
        const visit = (nodes: NavigationMenuNode[]) => nodes.forEach(node => {
            keys.add(node.label.key)
            if (node.children) visit(node.children)
        })
        visit(buildAdminMenu((key, fallback) => ({ key, fallback }), navigationOwner))
        Object.values(SCREENS).forEach(screen => {
            keys.add(screen.titleKey)
            screen.breadcrumbs.forEach(crumb => keys.add(crumb.labelKey))
        })
        for (const locale of ['en', 'ar']) {
            for (const key of keys) {
                const label = i18n.getResource(locale, 'translation', key)
                expect(label, `${locale}:${key}`).toBeTypeOf('string')
                expect(label.trim(), `${locale}:${key}`).not.toBe('')
                expect(label, `${locale}:${key}`).not.toBe(key)
                if (locale === 'ar') expect(label, key).toMatch(/[\u0600-\u06ff]/)
            }
            const ui = i18n.getResource(locale, 'translation', 'smartNavigation')
            expect(ui).toBeTruthy()
            for (const entry of entries) {
                expect(entry.labels[locale as 'en' | 'ar']).not.toBe(entry.labelKey)
                expect(entry.breadcrumbs[locale as 'en' | 'ar'].length).toBeGreaterThan(0)
            }
        }
    })

    it('distinguishes similarly named finance dashboards and product contexts without inventing a merchant Products route', () => {
        const finance = searchNavigation(entries, 'Finance Dashboard')
        expect(finance.slice(0, 2).map(entry => entry.application)).toEqual(['pos', 'commercehub'])
        expect(finance[0].id).not.toBe(finance[1].id)
        const products = searchNavigation(entries, 'Products')
        expect(products.some(entry => entry.to.pathname === '/products')).toBe(true)
        expect(products.some(entry => entry.to.pathname === '/admin/affiliates/products')).toBe(true)
        expect(byPath('/admin/commercehub').application).toBe('commercehub')
        expect(searchNavigation(entries, 'Merchant catalog')[0].to.pathname).toBe('/admin/commercehub')
    })

    it.each(['Payment Methods', 'طرق الدفع', 'Payment options', 'وسائل الدفع'])('matches bilingual exact names/aliases: %s', query => {
        expect(searchNavigation(entries, query)[0].to.pathname).toBe('/ecommerce/payment-methods')
    })

    it('ranks exact and prefix labels before context, normalizes Arabic marks, and rejects accidental character matches', () => {
        expect(searchNavigation(entries, 'POS')[0].to.pathname).toBe('/pos')
        expect(searchNavigation(entries, 'Payment Meth')[0].to.pathname).toBe('/ecommerce/payment-methods')
        expect(searchNavigation(entries, 'Storefront Payments').some(entry => entry.to.pathname === '/ecommerce/payment-methods')).toBe(true)
        expect(searchNavigation(entries, 'طُرُق الدَّفْع')[0].to.pathname).toBe('/ecommerce/payment-methods')
        expect(normalizeNavigationSearch(' إِعدادات ')).toBe('اعدادات')
        expect(searchNavigation(entries, 'a')).toEqual([])
        expect(searchNavigation(entries, 'no-such-screen-xyz')).toEqual([])
        expect(searchNavigation(entries, '')).toEqual(entries)
    })

    it('intersects menu AND actual route authority; does not expose separate applications', () => {
        const restricted = buildNavigationRegistry(routes, { ...navigationOwner, effectivePermissions: ['reports.operational.view', 'dashboard.view'], navigationCapabilities: [] }, i18n)
        expect(restricted.some(entry => entry.to.pathname === '/sales-history')).toBe(false)
        expect(restricted.some(entry => entry.to.pathname === '/ecommerce/payment-methods')).toBe(false)
        expect(restricted.some(entry => entry.application === 'commercehub')).toBe(false)
        expect(entries.some(entry => entry.to.pathname?.startsWith('/super-admin') || entry.to.pathname?.startsWith('/affiliate/'))).toBe(false)
        expect(buildNavigationRegistry(routes, null, i18n)).toEqual([])
    })

    it('fails closed for unregistered menu destinations and disabled capabilities', () => {
        const minimal = createRoutesFromChildren(<Route path="/" handle={{ application: 'pos' }}><Route index element={<p>Home</p>} /></Route>)
        expect(buildNavigationRegistry(minimal, navigationOwner, i18n).map(entry => entry.to.pathname)).toEqual(['/'])
        const blocked = buildNavigationRegistry(routes, { ...navigationOwner, navigationCapabilities: [] }, i18n)
        expect(blocked.some(entry => entry.application === 'commercehub')).toBe(false)
    })

    it('resolves only registered static visits and declared selectors without retaining arbitrary sensitive queries', () => {
        const resolve = (pathname: string, search = '') => resolveNavigationVisit(entries, routes, { pathname, search })
        expect(resolve('/products', '?customer=private&token=secret')?.id).toBe(byPath('/products').id)
        expect(resolve('/financial-accounts', '?tab=sub&token=secret')?.labels.en).toBe('Sub Accounts')
        expect(resolve('/financial-accounts', '?tab=not-registered')).toBeNull()
        expect(resolve('/storefront/navigation', '?view=footer')?.labels.en).toBe('Footer Links')
        expect(resolve('/commercehub/orders/private-order')).toBeNull()
        expect(resolve('/products/private-product/edit')).toBeNull()
        expect(resolve('/not-registered')).toBeNull()
        expect(resolve('/super-admin/finance')).toBeNull()
    })
})

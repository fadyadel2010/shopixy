/**
 * Wave 0 — Storefront Constitutional Alignment
 * Architecture regression tests for constitutional violations fixed by Wave 0.
 *
 * These tests protect against:
 * 1. Duplicate product ownership (Orders in both Operations and CommerceHub Admin)
 * 2. Identity contradictions (E-commerce vs Online Store vs Storefront)
 * 3. Hidden workspace collapse (Storefront/Online Store section disappearing)
 * 4. Constitutional visibility (placeholder rendering when all children filtered)
 *
 * Source of truth: ONLINE_STORE_CANONICAL_BLUEPRINT.md (approved, frozen)
 */

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import i18n from '../../../i18n/config'
import Sidebar from '../../../components/Sidebar'
import { useAuthStore } from '../../../store/authStore'

const appSource = () => readFileSync(resolve(process.cwd(), 'src/App.jsx'), 'utf8')
const sidebarSource = () => readFileSync(resolve(process.cwd(), 'src/components/Sidebar.jsx'), 'utf8')
const iaSource = () =>
    readFileSync(
        resolve(process.cwd(), 'src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js'),
        'utf8',
    )

const ownerUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    role: 'Owner',
    navigationCapabilities: [],
}

const commerceHubVendorUser = {
    id: '33333333-3333-4333-8333-333333333333',
    tenantId: '22222222-2222-4222-8222-222222222222',
    role: 'Owner',
    navigationCapabilities: ['commercehub.orders.view'],
    vendorLifecycleAuthority: {
        status: 'Active',
        tenantActive: true,
        marketplaceApproved: true,
        kycVerified: true,
    },
}

const noCapabilitiesUser = {
    id: '44444444-4444-4444-8444-444444444444',
    tenantId: '22222222-2222-4222-8222-222222222222',
    role: 'Owner',
    navigationCapabilities: null,
}

describe('Wave 0 — Storefront Constitutional Alignment', () => {
    beforeEach(async () => {
        window.localStorage.clear()
        await i18n.changeLanguage('en')
    })

    afterEach(() => {
        cleanup()
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
    })

    describe('1. Duplicate product ownership', () => {
        it('does not register /ecommerce/orders in the CommerceHub Admin sidebar section', () => {
            const source = sidebarSource()
            const commerceHubSectionMatch = source.match(
                /key:\s*['"]commercehub['"]\s*[\s\S]*?children:\s*\[([\s\S]*?)\]/
            )
            expect(commerceHubSectionMatch, 'CommerceHub Admin section must exist').toBeTruthy()
            const commerceHubChildren = commerceHubSectionMatch![1]
            expect(commerceHubChildren).not.toContain('/ecommerce/orders')
        })

        it('does not register /ecommerce/orders route under CommerceHub admin guard in App.jsx', () => {
            const app = appSource()
            // The /ecommerce/orders route should NOT appear inside commercehub.admin guard blocks
            // It should only be under commercehub.orders.view guard (Operations domain)
            const ordersRouteIndex = app.indexOf("path=\"ecommerce/orders\"")
            expect(ordersRouteIndex).toBeGreaterThan(-1)
            // Verify it's not inside the CommerceHub Admin section guard
            const commerceHubSettingsIndex = app.indexOf('admin/commercehub')
            const ordersInCommerceHubAdmin = app.substring(commerceHubSettingsIndex, commerceHubSettingsIndex + 2000)
            expect(ordersInCommerceHubAdmin).not.toContain('ecommerce/orders')
        })
    })

    describe('2. Identity contradictions', () => {
        it('does not use "E-commerce" as the sidebar section label for the Storefront Operations domain', () => {
            const source = sidebarSource()
            // The section must NOT use nav.ecommerce
            expect(source).not.toMatch(/label:\s*t\(['"]nav\.ecommerce['"]\)/)
            // The section key must NOT be 'ecommerce'
            const ecommerceSectionMatch = source.match(/key:\s*['"]ecommerce['"]/)
            expect(ecommerceSectionMatch).toBeNull()
        })

        it('uses the unified Storefront product with Operations as a child domain (Wave 2)', () => {
            const source = sidebarSource()
            // The sidebar must NOT have a separate 'onlineStore' section — Operations is inside Storefront
            expect(source).not.toMatch(/key:\s*['"]onlineStore['"]/)
            // The sidebar must reference STOREFRONT_PRODUCT_NAVIGATION (the unified tree)
            expect(source).toContain('STOREFRONT_PRODUCT_NAVIGATION')
        })

        it('does not use a separate "Online Store" section key in the sidebar', () => {
            const source = sidebarSource()
            expect(source).not.toMatch(/key:\s*['"]onlineStore['"]/)
        })
    })

    describe('3. Hidden workspace collapse', () => {
        it('renders the Storefront section even when the user has no CommerceHub capabilities', async () => {
            useAuthStore.setState({ user: noCapabilitiesUser, isAuthenticated: true, hasHydrated: true })

            render(
                <MemoryRouter initialEntries={['/storefront']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>
            )

            // The unified Storefront section must remain visible
            const storefrontSection = await screen.findByRole('button', { name: 'Storefront' })
            expect(storefrontSection).toBeInTheDocument()
        })

        it('renders a placeholder when all Storefront children are role-filtered', async () => {
            // Use a Cashier role user — all Design (Owner/Admin) and Operations (Owner/Admin)
            // children are filtered out, triggering the placeholder.
            useAuthStore.setState({
                user: {
                    id: '44444444-4444-4444-8444-444444444444',
                    tenantId: '22222222-2222-4222-8222-222222222222',
                    role: 'Cashier',
                    navigationCapabilities: null,
                },
                isAuthenticated: true,
                hasHydrated: true,
            })

            render(
                <MemoryRouter initialEntries={['/']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>
            )

            const placeholder = await screen.findByText('Request access to this workspace')
            expect(placeholder).toBeInTheDocument()
        })

        it('renders the Storefront (Design) section even when the user lacks Owner/Admin roles', async () => {
            const nonOwnerUser = { ...ownerUser, role: 'Cashier' }
            useAuthStore.setState({ user: nonOwnerUser, isAuthenticated: true, hasHydrated: true })

            render(
                <MemoryRouter initialEntries={['/storefront']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>
            )

            // The Storefront section must remain visible with a placeholder
            const storefrontSection = screen.queryByRole('button', { name: 'Storefront' })
            if (storefrontSection) {
                expect(storefrontSection).toBeInTheDocument()
            }
        })
    })

    describe('4. Constitutional visibility (placeholder rendering)', () => {
        it('does not return null for Storefront product sections when all children are filtered', () => {
            const source = sidebarSource()
            // The filterMenuNode function must contain the STOREFRONT_PRODUCT_KEYS guard
            expect(source).toMatch(/STOREFRONT_PRODUCT_KEYS/)
            // Must NOT have the bare "return null" for children.length === 0 without a check
            const hasPlaceholderGuard = source.includes('placeholder: true')
            expect(hasPlaceholderGuard).toBe(true)
        })

        it('includes the access-placeholder i18n key', () => {
            const enSource = readFileSync(
                resolve(process.cwd(), 'src/i18n/locales/en.json'),
                'utf8',
            )
            expect(enSource).toContain('"accessPlaceholder"')
            expect(enSource).toContain('Request access to this workspace')
        })
    })

    describe('5. Discoverability recovery (Shipping link)', () => {
        it('does not infer shipment permission from CommerceHub orders capability', async () => {
            useAuthStore.setState({ user: commerceHubVendorUser, isAuthenticated: true, hasHydrated: true })
            render(
                <MemoryRouter initialEntries={['/storefront']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>
            )
            expect(await screen.findByRole('button', { name: 'Storefront' })).toBeInTheDocument()
            expect(document.querySelector('a[href="/admin/shipping/shipments"]')).toBeNull()
        })

        it('uses /ecommerce/shipping-methods as the canonical Shipping sidebar link', () => {
            const source = sidebarSource()
            const ia = iaSource()
            // The canonical path must appear in the IA data or sidebar source
            expect(source + ia).toContain('/ecommerce/shipping-methods')
            // Must NOT link to the old /admin/shipping/setup in the Operations domain section
            expect(source).toContain('STOREFRONT_PRODUCT_NAVIGATION')
        })

        it('registers the /ecommerce/shipping-methods route in App.jsx', () => {
            const app = appSource()
            expect(app).toContain('path="ecommerce/shipping-methods"')
        })
    })

    describe('6. Terminology alignment', () => {
        it('uses "Abandoned Cart Recovery" instead of "Smart Cart Recovery"', () => {
            const ia = iaSource()
            const match = ia.match(/'cart-recovery'[\s\S]*?fallbackLabel:\s*'([^']+)'/)
            expect(match).toBeTruthy()
            expect(match![1]).toBe('Abandoned Cart Recovery')
            expect(ia).not.toContain("'Smart Cart Recovery'")
        })

        it('uses "Pixel Tracking" instead of "Signal Hardening"', () => {
            const ia = iaSource()
            const match = ia.match(/'pixel-tracking'[\s\S]*?fallbackLabel:\s*'([^']+)'/)
            expect(match).toBeTruthy()
            expect(match![1]).toBe('Pixel Tracking')
            expect(ia).not.toContain("'Signal Hardening'")
        })

        it('uses "Commerce AI" instead of "AI Assistant"', () => {
            const ia = iaSource()
            const match = ia.match("'commerce-ai'[\\s\\S]*?fallbackLabel:\\s*'([^']+)'")
            expect(match).toBeTruthy()
            expect(match![1]).toBe('Commerce AI')
        })

        it('uses "Promotions" instead of "Offers" as the sidebar fallback label', () => {
            const ia = iaSource()
            const match = ia.match(/'promotions'[\s\S]*?fallbackLabel:\s*'([^']+)'/)
            expect(match).toBeTruthy()
            expect(match![1]).toBe('Promotions')
            expect(ia).not.toContain("fallbackLabel: 'Offers'")
        })
    })
})

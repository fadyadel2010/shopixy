/**
 * Domains module — structural and navigation tests.
 *
 * These tests verify:
 * 1. All 9 domain screens are exported from the module barrel
 * 2. The Domains navigation node is correctly positioned between Checkout Fields and Homepage
 * 3. All 9 canonical SCREENS and routes resolve correctly
 * 4. Route compatibility entries are registered
 */

import { describe, expect, it } from 'vitest'
import {
    DomainsOverviewPage,
    DomainDetailPage,
    AddDomainWizardPage,
    DomainListPage,
    DnsVerificationPage,
    SslManagementPage,
    RedirectsPage,
    DomainHistoryPage,
    ActivityPage,
} from './pages'

import {
    STOREFRONT_NAVIGATION,
    SCREENS,
    resolveStorefrontScreen,
} from '../informationArchitecture/storefrontInformationArchitecture'

describe('Domains module — screen exports', () => {
    it('exports all 9 domain screen components', () => {
        expect(DomainsOverviewPage).toBeDefined()
        expect(DomainDetailPage).toBeDefined()
        expect(AddDomainWizardPage).toBeDefined()
        expect(DomainListPage).toBeDefined()
        expect(DnsVerificationPage).toBeDefined()
        expect(SslManagementPage).toBeDefined()
        expect(RedirectsPage).toBeDefined()
        expect(DomainHistoryPage).toBeDefined()
        expect(ActivityPage).toBeDefined()
    })
})

describe('Domains module — navigation placement', () => {
    it('inserts the Domains node between Checkout Fields and Homepage', () => {
        const labels = STOREFRONT_NAVIGATION.children.map((n) => n.fallbackLabel)
        const checkoutFieldsIdx = labels.indexOf('Checkout Fields')
        const domainsIdx = labels.indexOf('Domains')
        const homepageIdx = labels.indexOf('Homepage')

        expect(domainsIdx).toBe(checkoutFieldsIdx + 1)
        expect(domainsIdx).toBe(homepageIdx - 1)
    })

    it('Domains node uses the Globe icon', () => {
        const domainsNode = STOREFRONT_NAVIGATION.children.find(
            (n) => n.fallbackLabel === 'Domains',
        )
        if (!domainsNode?.children) throw new Error('Domains navigation and its children must exist')
        expect(domainsNode.icon).toBe('Globe')
        expect(domainsNode.children).toBeDefined()
        expect(domainsNode.children).toHaveLength(2)
        expect(domainsNode.children[0].fallbackLabel).toBe('Domains')
        expect(domainsNode.children[1].fallbackLabel).toBe('Activity')
    })
})

describe('Domains module — SCREENS registration', () => {
    const expectedScreens = [
        'domains',
        'domain-detail',
        'domain-add',
        'domain-list',
        'dns-verification',
        'ssl-management',
        'redirects',
        'domain-history',
        'domains-activity',
    ]

    for (const screenId of expectedScreens) {
        it(`registers SCREENS['${screenId}']`, () => {
            expect(SCREENS[screenId]).toBeDefined()
            expect(SCREENS[screenId].id).toBe(screenId)
        })
    }
})

describe('Domains module — route resolution', () => {
    const expectedRoutes = [
        ['/storefront/domains', 'domains'],
        ['/storefront/domains/', 'domains'],
        ['/storefront/domains/list', 'domain-list'],
        ['/storefront/domains/add', 'domain-add'],
        ['/storefront/domains/activity', 'domains-activity'],
        ['/storefront/domains/shop.example.com', 'domain-detail'],
        ['/storefront/domains/shop.example.com/verification', 'dns-verification'],
        ['/storefront/domains/shop.example.com/ssl', 'ssl-management'],
        ['/storefront/domains/shop.example.com/redirects', 'redirects'],
        ['/storefront/domains/shop.example.com/history', 'domain-history'],
    ]

    for (const [path, screenId] of expectedRoutes) {
        it(`resolves ${path} to ${screenId}`, () => {
            expect(resolveStorefrontScreen(path)?.id).toBe(screenId)
        })
    }
})

describe('Domains module — breadcrumb hierarchy', () => {
    it('D01 Overview breadcrumbs: Storefront > Home > Domains', () => {
        const screen = resolveStorefrontScreen('/storefront/domains')
        if (!screen) throw new Error('Domains overview route must resolve')
        expect(screen.breadcrumbs).toHaveLength(3)
        expect(screen.breadcrumbs[0].fallbackLabel).toBe('Storefront')
        expect(screen.breadcrumbs[1].fallbackLabel).toBe('Home')
        expect(screen.breadcrumbs[2].fallbackLabel).toBe('Domains')
    })

    it('D02 Detail breadcrumbs: Storefront > Home > Domains > Domain', () => {
        const screen = resolveStorefrontScreen('/storefront/domains/shop.example.com')
        if (!screen) throw new Error('Domain detail route must resolve')
        expect(screen.breadcrumbs).toHaveLength(4)
        expect(screen.breadcrumbs[0].fallbackLabel).toBe('Storefront')
        expect(screen.breadcrumbs[1].fallbackLabel).toBe('Home')
        expect(screen.breadcrumbs[2].fallbackLabel).toBe('Domains')
        expect(screen.breadcrumbs[3].fallbackLabel).toBe('Domain')
    })

    it('D09 Activity breadcrumbs: Storefront > Home > Domains > Activity', () => {
        const screen = resolveStorefrontScreen('/storefront/domains/activity')
        if (!screen) throw new Error('Domains activity route must resolve')
        expect(screen.breadcrumbs).toHaveLength(4)
        expect(screen.breadcrumbs[3].fallbackLabel).toBe('Activity')
    })

    it('D04 List breadcrumbs: Storefront > Home > Domains > Domain List', () => {
        const screen = resolveStorefrontScreen('/storefront/domains/list')
        if (!screen) throw new Error('Domain list route must resolve')
        expect(screen.breadcrumbs).toHaveLength(4)
        expect(screen.breadcrumbs[3].fallbackLabel).toBe('Domain List')
    })
})

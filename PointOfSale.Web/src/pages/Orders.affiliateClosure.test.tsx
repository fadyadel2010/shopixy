import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import i18n from '../i18n/config'
import { admitBackendAffiliateAttributionPresentation } from '../auth/commerceHubVendorIdentityAuthority'
import { OrderDetailsPanel } from './Orders.jsx'

const order = () => ({
    orderId: '33333333-3333-4333-8333-333333333333',
    tenantId: '11111111-1111-4111-8111-111111111111',
    orderNumber: 'AFFILIATE_PHASE4_CLOSURE_20261021_UI', source: 'Ecommerce',
    status: 'Pending', paymentStatus: 'Pending', fulfillmentStatus: 'Created', items: [], payments: [],
    affiliateAttribution: {
        tenantId: '11111111-1111-4111-8111-111111111111', orderId: '33333333-3333-4333-8333-333333333333',
        attributionId: '44444444-4444-4444-8444-444444444444',
        affiliateAccountId: '55555555-5555-4555-8555-555555555555', referralCode: 'CLOSURE-REF',
        promotionLinkId: '66666666-6666-4666-8666-666666666666', source: 'ReferralLink',
        attributedAtUtc: '2026-10-21T10:00:00Z',
    },
})

describe('Affiliate closure: actual order drawer lineage presentation', () => {
    beforeEach(async () => { await i18n.changeLanguage('en') })

    it('renders the localized attribution section only from admitted backend evidence', () => {
        render(<OrderDetailsPanel order={admitBackendAffiliateAttributionPresentation(order())} onClose={() => {}} />)
        expect(screen.getByText('Affiliate attribution')).toBeVisible()
        expect(screen.getByText('CLOSURE-REF')).toBeVisible()
        expect(screen.getByText('55555555-5555-4555-8555-555555555555')).toBeVisible()
    })

    it('rejects substituted cross-order evidence rather than rendering it', () => {
        const forged = order()
        forged.affiliateAttribution.orderId = '77777777-7777-4777-8777-777777777777'
        expect(() => admitBackendAffiliateAttributionPresentation(forged)).toThrow(/cross-order attribution evidence/)
    })

    it('suppresses incomplete evidence and renders the localized warning', () => {
        const forged = order()
        forged.affiliateAttribution.referralCode = ''
        render(<OrderDetailsPanel order={admitBackendAffiliateAttributionPresentation(forged)} onClose={() => {}} />)
        expect(screen.queryByText('Affiliate attribution')).not.toBeInTheDocument()
        expect(screen.queryByText('CLOSURE-REF')).not.toBeInTheDocument()
        expect(screen.getByRole('status')).toBeVisible()
    })
})

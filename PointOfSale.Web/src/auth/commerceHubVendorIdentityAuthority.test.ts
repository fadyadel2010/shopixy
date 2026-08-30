import { describe, expect, it } from 'vitest'
import {
    AFFILIATE_ATTRIBUTION_WARNING_CODE,
    admitBackendAffiliateAttributionPresentation,
    deriveCurrentStoreIdentityPresentation,
    deriveCurrentVendorScopedArray,
    deriveCurrentVendorScopedDto,
    isCurrentVendorTenantAuthority,
    requireBackendAffiliateAttributionAuthority,
    requireBackendMarketplaceLineageAuthority,
    requireBackendOrderSourceAuthority,
    requireCurrentVendorOrderVisibilityAuthority,
    requireCurrentVendorOrderVisibilityListAuthority,
    resolveCurrentVendorIdentityAuthority,
} from './commerceHubVendorIdentityAuthority'

const currentUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
}

const marketplaceOrder = {
    tenantId: currentUser.tenantId,
    orderId: '33333333-3333-4333-8333-333333333333',
    orderNumber: 'CH-SUB-1001',
    source: 'Marketplace',
    sourceOrderId: '44444444-4444-4444-8444-444444444444',
    sourceLabel: 'Marketplace',
    isMarketplaceOrder: true,
    marketplaceLineage: {
        tenantId: currentUser.tenantId,
        orderId: '33333333-3333-4333-8333-333333333333',
        marketplaceOrderId: '44444444-4444-4444-8444-444444444444',
        marketplaceName: 'CommerceHub',
        marketplaceOrderNumber: 'CH-1001',
        marketplaceSubOrderNumber: 'CH-SUB-1001',
        externalCustomerId: '55555555-5555-4555-8555-555555555555',
        vendorId: currentUser.tenantId,
        vendorName: 'Backend Vendor',
        marketplaceSourceType: 'Marketplace',
        marketplaceStatus: 'Pending',
        marketplacePaymentStatus: 'Pending',
        currency: 'EGP',
        subtotal: 100,
        shipping: 10,
        total: 110,
        paymentReferenceId: 'pay-ref-1',
        paymentSnapshotJson: '{"provider":"Paymob"}',
        shippingMethodId: '66666666-6666-4666-8666-666666666666',
        shippingMethodName: 'Courier',
        shippingSnapshotJson: '{"method":"Courier"}',
        commissionSnapshotJson: '{"rate":10}',
    },
}

describe('CommerceHub vendor identity authority', () => {
    it('derives current vendor identity only from backend-hydrated session user', () => {
        expect(resolveCurrentVendorIdentityAuthority(currentUser)).toEqual({
            tenantId: currentUser.tenantId,
            userId: currentUser.id,
        })

        expect(resolveCurrentVendorIdentityAuthority(null)).toBeNull()
        expect(resolveCurrentVendorIdentityAuthority({ id: 'user-1', tenantId: currentUser.tenantId })).toBeNull()
        expect(resolveCurrentVendorIdentityAuthority({ id: currentUser.id, tenantId: 'tenant-1' })).toBeNull()
    })

    it('rejects foreign vendor, store, ownership, finance, and analytics DTO scope', () => {
        expect(isCurrentVendorTenantAuthority({ tenantId: currentUser.tenantId }, currentUser)).toBe(true)
        expect(deriveCurrentVendorScopedDto({ tenantId: currentUser.tenantId, total: 10 }, currentUser)).toEqual({
            tenantId: currentUser.tenantId,
            total: 10,
        })
        expect(deriveCurrentVendorScopedDto({ tenantId: '33333333-3333-4333-8333-333333333333' }, currentUser)).toBeNull()
        expect(deriveCurrentVendorScopedDto({ tenantId: currentUser.tenantId }, null)).toBeNull()
        expect(deriveCurrentVendorScopedDto({ total: 10 }, currentUser)).toBeNull()
    })

    it('fails closed for mixed ownership arrays instead of dropping only the foreign row', () => {
        expect(deriveCurrentVendorScopedArray([
            { tenantId: currentUser.tenantId, listingId: 'listing-1' },
            { tenantId: '33333333-3333-4333-8333-333333333333', listingId: 'foreign-listing' },
        ], currentUser)).toEqual([])
    })

    it('validates store identity presentation without repairing malformed status or name', () => {
        expect(deriveCurrentStoreIdentityPresentation({
            tenantId: currentUser.tenantId,
            storeName: 'Backend Store',
            storefrontStatus: 'live',
            themeId: 'modern',
            rowVersion: '7',
        }, currentUser)).toMatchObject({
            tenantId: currentUser.tenantId,
            storeName: 'Backend Store',
            storefrontStatus: 'live',
            themeId: 'modern',
            rowVersion: '7',
        })

        expect(deriveCurrentStoreIdentityPresentation({
            tenantId: currentUser.tenantId,
            storeName: '',
            storefrontStatus: 'live',
            themeId: 'modern',
            rowVersion: '7',
        }, currentUser)).toBeNull()
        expect(deriveCurrentStoreIdentityPresentation({
            tenantId: currentUser.tenantId,
            storeName: 'Backend Store',
            storefrontStatus: 'fabricated',
            themeId: 'modern',
            rowVersion: '7',
        }, currentUser)).toBeNull()
        expect(deriveCurrentStoreIdentityPresentation({
            tenantId: currentUser.tenantId,
            storeName: 'Backend Store',
            storefrontStatus: 'live',
            themeId: '',
            rowVersion: '7',
        }, currentUser)).toBeNull()
        expect(deriveCurrentStoreIdentityPresentation({
            tenantId: currentUser.tenantId,
            storeName: 'Backend Store',
            storefrontStatus: 'live',
            themeId: 'modern',
            rowVersion: '',
        }, currentUser)).toBeNull()
    })

    it('requires vendor order detail visibility to match the current route target', () => {
        const order = {
            tenantId: currentUser.tenantId,
            orderId: 'order-1',
            orderNumber: 'CH-1001',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
        }

        expect(requireCurrentVendorOrderVisibilityAuthority(order, currentUser, 'order-1')).toEqual(order)
        expect(() => requireCurrentVendorOrderVisibilityAuthority(order, currentUser, 'order-2')).toThrow(/order visibility authority/)
        expect(() => requireCurrentVendorOrderVisibilityAuthority({ ...order, tenantId: '33333333-3333-4333-8333-333333333333' }, currentUser, 'order-1')).toThrow(/order visibility authority/)
        expect(() => requireCurrentVendorOrderVisibilityAuthority(order, null, 'order-1')).toThrow(/order visibility authority/)
    })

    it('requires backend order source evidence before storefront lineage presentation', () => {
        expect(requireBackendOrderSourceAuthority({
            tenantId: currentUser.tenantId,
            orderId: 'order-1',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
        })).toEqual({
            source: 'Ecommerce',
            label: 'Storefront',
            tone: 'slate',
        })

        expect(requireBackendOrderSourceAuthority(marketplaceOrder)).toEqual({
            source: 'Marketplace',
            label: 'Marketplace',
            tone: 'sky',
        })

        expect(() => requireBackendOrderSourceAuthority({
            tenantId: currentUser.tenantId,
            orderId: 'order-1',
            isMarketplaceOrder: false,
        })).toThrow(/order source evidence/)
        expect(() => requireBackendOrderSourceAuthority({
            tenantId: currentUser.tenantId,
            orderId: 'order-1',
            source: 'Manual',
            isMarketplaceOrder: false,
        })).toThrow(/non-storefront source evidence/)
        expect(() => requireBackendOrderSourceAuthority({
            tenantId: currentUser.tenantId,
            orderId: 'order-2',
            source: 'Ecommerce',
            isMarketplaceOrder: true,
        })).toThrow(/contradictory marketplace source evidence/)
        expect(() => requireBackendOrderSourceAuthority({
            tenantId: currentUser.tenantId,
            orderId: '33333333-3333-4333-8333-333333333333',
            source: 'Marketplace',
            sourceLabel: 'Marketplace',
            isMarketplaceOrder: true,
        })).toThrow(/missing lineage evidence/)
        expect(() => requireBackendOrderSourceAuthority({
            tenantId: currentUser.tenantId,
            orderId: 'order-1',
            source: 'Ecommerce',
            sourceLabel: 'Storefront',
            isMarketplaceOrder: false,
        })).toThrow(/contradictory source label evidence/)
        expect(() => requireBackendOrderSourceAuthority({
            tenantId: currentUser.tenantId,
            orderId: 'order-1',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
            marketplaceLineage: marketplaceOrder.marketplaceLineage,
        })).toThrow(/marketplace evidence on a non-marketplace order/)
    })

    it('requires backend Marketplace lineage evidence before Marketplace origin presentation', () => {
        expect(requireBackendMarketplaceLineageAuthority(marketplaceOrder)).toMatchObject({
            tenantId: currentUser.tenantId,
            orderId: marketplaceOrder.orderId,
            marketplaceOrderId: marketplaceOrder.marketplaceLineage.marketplaceOrderId,
            marketplaceName: 'CommerceHub',
            marketplaceOrderNumber: 'CH-1001',
            marketplaceSubOrderNumber: 'CH-SUB-1001',
            vendorId: currentUser.tenantId,
            vendorName: 'Backend Vendor',
            marketplaceSourceType: 'Marketplace',
            marketplaceStatus: 'Pending',
            marketplacePaymentStatus: 'Pending',
            currency: 'EGP',
            subtotal: 100,
            shipping: 10,
            total: 110,
        })

        expect(() => requireBackendMarketplaceLineageAuthority({
            ...marketplaceOrder,
            sourceOrderId: '77777777-7777-4777-8777-777777777777',
        })).toThrow(/source identity evidence/)
        expect(() => requireBackendMarketplaceLineageAuthority({
            ...marketplaceOrder,
            marketplaceLineage: {
                ...marketplaceOrder.marketplaceLineage,
                marketplaceOrderId: 'not-a-guid',
            },
        })).toThrow(/cross-scope lineage evidence/)
        expect(() => requireBackendMarketplaceLineageAuthority({
            ...marketplaceOrder,
            marketplaceLineage: {
                ...marketplaceOrder.marketplaceLineage,
                vendorId: '77777777-7777-4777-8777-777777777777',
            },
        })).toThrow(/cross-scope lineage evidence/)
        expect(() => requireBackendMarketplaceLineageAuthority({
            ...marketplaceOrder,
            marketplaceLineage: {
                ...marketplaceOrder.marketplaceLineage,
                marketplaceSourceType: 'Frontend',
            },
        })).toThrow(/incomplete lineage evidence/)
        expect(() => requireBackendMarketplaceLineageAuthority({
            ...marketplaceOrder,
            marketplaceLineage: {
                ...marketplaceOrder.marketplaceLineage,
                paymentSnapshotJson: '{bad-json',
            },
        })).toThrow(/payment metadata/)
    })

    it('requires backend affiliate attribution evidence before affiliate lineage presentation', () => {
        const order = {
            tenantId: currentUser.tenantId,
            orderId: '33333333-3333-4333-8333-333333333333',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
            affiliateAttribution: {
                tenantId: currentUser.tenantId,
                orderId: '33333333-3333-4333-8333-333333333333',
                attributionId: '44444444-4444-4444-8444-444444444444',
                affiliateAccountId: '55555555-5555-4555-8555-555555555555',
                referralCode: 'AFFCODE1',
                promotionLinkId: '66666666-6666-4666-8666-666666666666',
                source: 'ReferralLink',
                attributedAtUtc: '2026-07-01T10:00:00Z',
            },
        }

        expect(requireBackendAffiliateAttributionAuthority(order)).toMatchObject({
            tenantId: currentUser.tenantId,
            orderId: order.orderId,
            affiliateAccountId: '55555555-5555-4555-8555-555555555555',
            referralCode: 'AFFCODE1',
            source: 'ReferralLink',
        })

        expect(requireBackendAffiliateAttributionAuthority({
            tenantId: currentUser.tenantId,
            orderId: order.orderId,
            affiliateAttribution: null,
        })).toBeNull()
        expect(() => requireBackendAffiliateAttributionAuthority({
            ...order,
            affiliateAttribution: { ...order.affiliateAttribution, orderId: '77777777-7777-4777-8777-777777777777' },
        })).toThrow(/cross-order attribution evidence/)
        expect(() => requireBackendAffiliateAttributionAuthority({
            ...order,
            affiliateAttribution: { ...order.affiliateAttribution, referralCode: '' },
        })).toThrow(/incomplete attribution evidence/)
        expect(() => requireBackendAffiliateAttributionAuthority({
            ...order,
            affiliateAttribution: { ...order.affiliateAttribution, source: 'Frontend' },
        })).toThrow(/incomplete attribution evidence/)
    })

    it('accepts RFC 9562 UUIDv7 affiliate identifiers returned by the .NET Guid contract', () => {
        const order = {
            tenantId: currentUser.tenantId,
            orderId: 'fc456d07-0cfe-4cc0-af0c-23862534d45a',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
            affiliateAttribution: {
                tenantId: currentUser.tenantId,
                orderId: 'fc456d07-0cfe-4cc0-af0c-23862534d45a',
                attributionId: '01a03efa-bb23-7dce-8c4c-b899d545f6f5',
                affiliateAccountId: '01a03e11-e234-7d40-b77e-99f9463c7a40',
                referralCode: 'DMFOQ21IP3',
                promotionLinkId: '01a03e6b-3ff1-7c1b-9cf2-a340244c0f25',
                source: 'ReferralLink',
                attributedAtUtc: '2026-08-26T16:50:14.431971Z',
            },
        }

        expect(requireBackendAffiliateAttributionAuthority(order)).toMatchObject({
            attributionId: order.affiliateAttribution.attributionId,
            affiliateAccountId: order.affiliateAttribution.affiliateAccountId,
            promotionLinkId: order.affiliateAttribution.promotionLinkId,
        })
    })

    it.each([
        ['attribution ID', { attributionId: 'not-a-guid' }],
        ['Affiliate account ID', { affiliateAccountId: 'not-a-guid' }],
        ['referral code', { referralCode: '' }],
        ['promotion link ID', { promotionLinkId: 'not-a-guid' }],
        ['source', { source: 'Frontend' }],
        ['timestamp', { attributedAtUtc: 'not-a-date' }],
    ])('rejects invalid present %s evidence without admitting it', (_field, change) => {
        const order = {
            tenantId: currentUser.tenantId,
            orderId: '33333333-3333-4333-8333-333333333333',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
            affiliateAttribution: {
                tenantId: currentUser.tenantId,
                orderId: '33333333-3333-4333-8333-333333333333',
                attributionId: '0198ef41-fd7c-7a34-8c12-111111111111',
                affiliateAccountId: '0198ef41-fd7c-7a34-8c12-222222222222',
                referralCode: 'AFFCODE1',
                promotionLinkId: '0198ef41-fd7c-7a34-8c12-333333333333',
                source: 'ReferralLink',
                attributedAtUtc: '2026-08-26T16:40:00Z',
                ...change,
            },
        }

        expect(() => requireBackendAffiliateAttributionAuthority(order)).toThrow(/incomplete attribution evidence/)
        expect(admitBackendAffiliateAttributionPresentation(order)).toMatchObject({
            affiliateAttribution: null,
            affiliateAttributionWarning: {
                code: AFFILIATE_ATTRIBUTION_WARNING_CODE,
            },
        })
    })

    it('accepts absent attribution and a missing optional promotion link without requiring detail-only evidence', () => {
        const normalOrder = {
            tenantId: currentUser.tenantId,
            orderId: '33333333-3333-4333-8333-333333333333',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
            affiliateAttribution: null,
        }
        const legacyAffiliateOrder = {
            ...normalOrder,
            affiliateAttribution: {
                tenantId: currentUser.tenantId,
                orderId: normalOrder.orderId,
                attributionId: '0198ef41-fd7c-7a34-8c12-111111111111',
                affiliateAccountId: '0198ef41-fd7c-7a34-8c12-222222222222',
                referralCode: 'AFFCODE1',
                promotionLinkId: null,
                source: 'ReferralLink',
                attributedAtUtc: '2026-08-26T16:40:00Z',
            },
        }

        expect(requireBackendAffiliateAttributionAuthority(normalOrder)).toBeNull()
        expect(requireBackendAffiliateAttributionAuthority(legacyAffiliateOrder)).toMatchObject({ promotionLinkId: null })
        expect(requireBackendAffiliateAttributionAuthority(legacyAffiliateOrder)).not.toHaveProperty('items')
        expect(requireBackendAffiliateAttributionAuthority(legacyAffiliateOrder)).not.toHaveProperty('commission')
    })

    it('keeps malformed optional Affiliate evidence row-local while preserving canonical list and detail reads', () => {
        const malformedOrder = {
            tenantId: currentUser.tenantId,
            orderId: '33333333-3333-4333-8333-333333333333',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
            affiliateAttribution: {
                tenantId: currentUser.tenantId,
                orderId: '33333333-3333-4333-8333-333333333333',
                attributionId: 'not-a-guid',
                affiliateAccountId: '55555555-5555-4555-8555-555555555555',
                referralCode: 'AFFCODE1',
                source: 'ReferralLink',
                attributedAtUtc: '2026-07-01T10:00:00Z',
            },
        }
        const normalOrder = {
            tenantId: currentUser.tenantId,
            orderId: '44444444-4444-4444-8444-444444444444',
            source: 'Ecommerce',
            isMarketplaceOrder: false,
        }

        const admittedList = requireCurrentVendorOrderVisibilityListAuthority({
            items: [normalOrder, malformedOrder],
        }, currentUser)
        const admittedDetail = requireCurrentVendorOrderVisibilityAuthority(
            malformedOrder,
            currentUser,
            malformedOrder.orderId,
        )

        expect(admittedList.items).toHaveLength(2)
        expect(admittedList.items[0]).toEqual(normalOrder)
        expect(admittedList.items[1]).toMatchObject({
            orderId: malformedOrder.orderId,
            affiliateAttribution: null,
            affiliateAttributionWarning: { code: AFFILIATE_ATTRIBUTION_WARNING_CODE },
        })
        expect(admittedDetail).toMatchObject({
            orderId: malformedOrder.orderId,
            affiliateAttribution: null,
            affiliateAttributionWarning: { code: AFFILIATE_ATTRIBUTION_WARNING_CODE },
        })
    })

    it('does not apply Affiliate attribution presentation to CommerceHub Marketplace orders', () => {
        expect(() => requireBackendOrderSourceAuthority({
            ...marketplaceOrder,
            affiliateAttribution: {
                tenantId: currentUser.tenantId,
                orderId: marketplaceOrder.orderId,
            },
        })).toThrow(/affiliate evidence on a Marketplace order/)
    })

    it('requires every vendor order list row to match the current tenant and rejects replayed identities', () => {
        const response = {
            items: [{ tenantId: currentUser.tenantId, orderId: 'order-1', source: 'Ecommerce', isMarketplaceOrder: false }],
        }

        expect(requireCurrentVendorOrderVisibilityListAuthority(response, currentUser)).toEqual(response)
        expect(() => requireCurrentVendorOrderVisibilityListAuthority({
            items: [{ tenantId: '33333333-3333-4333-8333-333333333333', orderId: 'foreign-order', source: 'Ecommerce', isMarketplaceOrder: false }],
        }, currentUser)).toThrow(/order visibility authority/)
        expect(() => requireCurrentVendorOrderVisibilityListAuthority({
            items: [
                { tenantId: currentUser.tenantId, orderId: 'order-1', source: 'Ecommerce', isMarketplaceOrder: false },
                { tenantId: currentUser.tenantId, orderId: 'ORDER-1', source: 'Ecommerce', isMarketplaceOrder: false },
            ],
        }, currentUser)).toThrow(/order visibility authority/)
        expect(() => requireCurrentVendorOrderVisibilityListAuthority({
            items: [{ tenantId: currentUser.tenantId, orderId: 'order-1', isMarketplaceOrder: false }],
        }, currentUser)).toThrow(/order source evidence/)
        const admitted = requireCurrentVendorOrderVisibilityListAuthority({
            items: [{
                tenantId: currentUser.tenantId,
                orderId: '33333333-3333-4333-8333-333333333333',
                source: 'Ecommerce',
                isMarketplaceOrder: false,
                affiliateAttribution: {
                    tenantId: currentUser.tenantId,
                    orderId: '77777777-7777-4777-8777-777777777777',
                    attributionId: '44444444-4444-4444-8444-444444444444',
                    affiliateAccountId: '55555555-5555-4555-8555-555555555555',
                    referralCode: 'AFFCODE1',
                    source: 'ReferralLink',
                    attributedAtUtc: '2026-07-01T10:00:00Z',
                },
            }],
        }, currentUser)
        expect(admitted.items[0]).toMatchObject({
            affiliateAttribution: null,
            affiliateAttributionWarning: { code: AFFILIATE_ATTRIBUTION_WARNING_CODE },
        })
    })
})

import { describe, expect, it } from 'vitest'
import { deriveServiceRequestContractPresentation } from './contractPresentation'
import { deriveServiceRequestLifecyclePresentation } from './status'

const request = (overrides = {}) => ({
    requestId: 'request-1',
    serviceId: 'service-1',
    serviceTitle: 'Launch campaign',
    serviceCategory: 'Marketing',
    requestMessage: 'Promote the summer launch.',
    startingPrice: 500,
    pricingType: 'Fixed' as const,
    currency: 'usd',
    packages: JSON.stringify([{ name: 'Standard', description: 'Campaign setup', price: 500, deliveryDays: 7, revisions: 2 }]),
    status: 'Accepted',
    canChat: true,
    canViewChatHistory: true,
    canSendChat: true,
    canConfirmCompletion: false,
    canCancel: true,
    canReview: false,
    ...overrides,
})

const contractFor = (value = {}) => {
    const dto = request(value)
    return deriveServiceRequestContractPresentation(dto, deriveServiceRequestLifecyclePresentation(dto))
}

describe('deriveServiceRequestContractPresentation', () => {
    it('derives accepted contract presentation from backend request snapshots', () => {
        const contract = contractFor()

        expect(contract).toMatchObject({
            requestId: 'request-1',
            serviceId: 'service-1',
            title: 'Launch campaign',
            category: 'Marketing',
            scope: 'Promote the summer launch.',
            price: 500,
            pricingType: 'Fixed',
            currency: 'USD',
        })
        expect(contract?.packages).toEqual([{ name: 'Standard', description: 'Campaign setup', price: 500, deliveryDays: 7, revisions: 2 }])
    })

    it('suppresses contract presentation for non-contract lifecycle state', () => {
        expect(contractFor({ status: 'Pending', canChat: false, canViewChatHistory: false, canSendChat: false })).toBeNull()
        expect(contractFor({ status: 'Rejected', canChat: false, canSendChat: false })).toBeNull()
        expect(contractFor({ status: 'Cancelled', canChat: false, canSendChat: false })).toBeNull()
        expect(contractFor({ status: 'Fabricated' })).toBeNull()
    })

    it('fails closed for missing identity and suppresses malformed pricing/packages', () => {
        expect(contractFor({ requestId: ' ' })).toBeNull()
        expect(contractFor({ serviceId: '' })).toBeNull()
        expect(contractFor({ serviceTitle: null })).toBeNull()

        const contract = contractFor({ startingPrice: -1, pricingType: 'Fixed', currency: 'BAD!', packages: '{' })

        expect(contract?.price).toBeNull()
        expect(contract?.pricingType).toBeNull()
        expect(contract?.currency).toBeNull()
        expect(contract?.packages).toEqual([])
    })

    it('does not compose incomplete package entries into deliverable, timeline, or revision truth', () => {
        const contract = contractFor({
            packages: JSON.stringify([
                { title: 'Basic', deliveryDays: 3, revisions: 1 },
                { description: 'Missing name', price: 100 },
                { name: 'Invalid numbers', price: Number.NaN, deliveryDays: -1, revisions: 1.5 },
            ]),
        })

        expect(contract?.packages).toEqual([
            { name: 'Basic', description: null, price: null, deliveryDays: 3, revisions: 1 },
            { name: 'Invalid numbers', description: null, price: null, deliveryDays: null, revisions: null },
        ])
    })
})

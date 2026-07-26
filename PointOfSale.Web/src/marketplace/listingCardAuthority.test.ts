import { describe, expect, it } from 'vitest'
import { deriveListingCardPresentation } from './listingCardAuthority'
import type { ServiceResult } from './types'

const serviceResult = (overrides: Partial<ServiceResult> = {}): ServiceResult => ({
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  title: 'Authoritative service',
  description: 'Current backend description',
  categoryId: 'Development',
  providerProfileId: '00a12dc1-561f-4406-8088-1ff76375ef25',
  providerDisplayName: 'Current Provider',
  averageRating: 4.5,
  totalReviews: 8,
  trustBadge: 'Rising',
  totalRequests: 10,
  completedRequests: 8,
  completionRate: 0.8,
  createdAt: '2026-06-14T00:00:00Z',
  startingPrice: 0,
  pricingType: 'Fixed',
  currency: 'usd',
  serviceImages: '["marketplace/services/current/card.png"]',
  ...overrides,
})

describe('deriveListingCardPresentation', () => {
  it('preserves valid zero pricing and current backend summaries', () => {
    const card = deriveListingCardPresentation(serviceResult())

    expect(card).toMatchObject({
      startingPrice: 0,
      currency: 'USD',
      category: 'Development',
      averageRating: 4.5,
      totalReviews: 8,
      trustBadge: 'Rising',
      completedRequests: 8,
      completionRate: 0.8,
      primaryImage: '/marketplace/services/current/card.png',
    })
  })

  it('fails closed when required service or provider identity is absent', () => {
    expect(deriveListingCardPresentation(serviceResult({ serviceId: ' ' }))).toBeNull()
    expect(deriveListingCardPresentation(serviceResult({ providerProfileId: '' }))).toBeNull()
    expect(deriveListingCardPresentation(serviceResult({ providerDisplayName: '' }))).toBeNull()
  })

  it('suppresses malformed rating, trust, analytics, pricing, and media values', () => {
    const card = deriveListingCardPresentation(serviceResult({
      averageRating: 9,
      totalReviews: -1,
      trustBadge: 'Fabricated',
      totalRequests: 2,
      completedRequests: 8,
      completionRate: 4,
      startingPrice: -10,
      currency: 'not-currency',
      serviceImages: '["https://attacker.example/card.png"]',
    }))

    expect(card).toMatchObject({
      averageRating: null,
      totalReviews: null,
      trustBadge: null,
      completedRequests: null,
      completionRate: null,
      startingPrice: null,
      pricingType: null,
      currency: null,
      primaryImage: null,
    })
  })

  it('suppresses analytics when the backend KPI rate contradicts its counts', () => {
    const card = deriveListingCardPresentation(serviceResult({
      totalRequests: 10,
      completedRequests: 7,
      completionRate: 0.8,
    }))

    expect(card).toMatchObject({
      completedRequests: null,
      completionRate: null,
    })
  })

  it('suppresses the aggregate bundle when reviews exceed final completions', () => {
    const card = deriveListingCardPresentation(serviceResult({
      averageRating: 4.5,
      totalReviews: 8,
      totalRequests: 10,
      completedRequests: 7,
      completionRate: 0.7,
    }))

    expect(card).toMatchObject({
      averageRating: null,
      totalReviews: null,
      trustBadge: null,
      completedRequests: null,
      completionRate: null,
    })
  })

  it('rejects replayed badges and impossible average/count pairs', () => {
    expect(deriveListingCardPresentation(serviceResult({
      averageRating: 5,
      totalReviews: 0,
      trustBadge: 'New',
    }))?.averageRating).toBeNull()

    expect(deriveListingCardPresentation(serviceResult({
      averageRating: 4.8,
      totalReviews: 60,
      trustBadge: 'Rising',
    }))?.trustBadge).toBeNull()
  })
})

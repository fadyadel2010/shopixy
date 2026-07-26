import { describe, expect, it } from 'vitest'
import {
  deriveServiceDetailPresentation,
  deriveServiceReviewPresentation,
} from './serviceDetailAuthority'
import type { ServiceDetails, ServiceReview } from './types'

const serviceDetails = (overrides: Partial<ServiceDetails> = {}): ServiceDetails => ({
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  title: 'Current service',
  description: 'Current backend description',
  category: 'Development',
  providerProfileId: '00a12dc1-561f-4406-8088-1ff76375ef25',
  providerDisplayName: 'Current Provider',
  isActive: true,
  createdAt: '2026-06-14T00:00:00Z',
  startingPrice: 0,
  pricingType: 'Fixed',
  currency: 'usd',
  serviceImages: JSON.stringify([
    'marketplace/services/current/detail.png',
    'https://attacker.example/replayed.png',
  ]),
  requestEntry: {
    canRequest: true,
    reason: 'Eligible',
  },
  providerProfile: {
    bio: 'Current bio',
    profileImageUrl: 'https://cdn.example.test/provider.png',
    location: 'Cairo',
    skills: '["TypeScript",""]',
    portfolioLinks: '["https://portfolio.example.test","javascript:alert(1)"]',
  },
  trust: {
    averageRating: 4.5,
    totalReviews: 8,
    trustBadge: 'Rising',
    ratingDistribution: { 5: 5, 4: 2, 3: 1, 2: 0, 1: 0 },
    ratingPercentages: { 5: 62.5, 4: 25, 3: 12.5, 2: 0, 1: 0 },
  },
  ...overrides,
})

const serviceReview = (overrides: Partial<ServiceReview> = {}): ServiceReview => ({
  reviewId: 'ce210a55-f82c-442a-b4e2-4cc9188640e5',
  serviceRequestId: '69c9e180-5907-49d3-9096-4207fd96ff2a',
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  reviewerUserId: '105b530e-8e3d-4c69-9c72-0d395da91f4d',
  providerUserId: 'f2c6b9af-57fb-4fb0-9ca7-83363f078fdd',
  rating: 5,
  comment: 'Current review',
  createdAt: '2026-06-14T00:00:00Z',
  updatedAt: '2026-06-14T00:00:00Z',
  buyerName: 'Current Buyer',
  marketingEvidence: {
    campaignName: 'Launch',
    platform: 'Google',
    startDate: '2026-05-01T00:00:00Z',
    endDate: '2026-05-31T00:00:00Z',
    evidenceType: 'PosVerified',
    ordersCount: 12,
    revenueAmount: 5000,
  },
  ...overrides,
})

describe('deriveServiceDetailPresentation', () => {
  it('keeps valid zero pricing and only backend-owned service media keys', () => {
    const detail = deriveServiceDetailPresentation(serviceDetails())

    expect(detail).toMatchObject({
      startingPrice: 0,
      pricingType: 'Fixed',
      currency: 'USD',
      serviceImages: ['/marketplace/services/current/detail.png'],
      requestEntry: {
        canRequest: true,
        reason: 'Eligible',
        activeRequestId: null,
      },
      providerProfile: {
        skills: ['TypeScript'],
        portfolioLinks: ['https://portfolio.example.test'],
      },
    })
  })

  it('fails closed for inactive or incomplete service/provider identity', () => {
    expect(deriveServiceDetailPresentation(serviceDetails({ isActive: false }))).toBeNull()
    expect(deriveServiceDetailPresentation(serviceDetails({ serviceId: '' }))).toBeNull()
    expect(deriveServiceDetailPresentation(serviceDetails({ providerProfileId: '' }))).toBeNull()
    expect(deriveServiceDetailPresentation(serviceDetails({ providerDisplayName: '' }))).toBeNull()
  })

  it('suppresses malformed pricing, trust, profile links, and media', () => {
    const detail = deriveServiceDetailPresentation(serviceDetails({
      startingPrice: -1,
      currency: 'invalid',
      serviceImages: '["../secret.png"]',
      providerProfile: {
        profileImageUrl: 'javascript:alert(1)',
        portfolioLinks: '["file:///secret"]',
      },
      trust: {
        averageRating: 9,
        totalReviews: 1,
        trustBadge: 'Fabricated',
        ratingDistribution: { 5: 1, 4: 0, 3: 0, 2: 0, 1: 0 },
        ratingPercentages: { 5: 100, 4: 0, 3: 0, 2: 0, 1: 0 },
      },
    }))

    expect(detail).toMatchObject({
      startingPrice: null,
      pricingType: null,
      currency: null,
      serviceImages: [],
      trust: null,
      providerProfile: {
        profileImageUrl: null,
        portfolioLinks: [],
      },
    })
  })

  it('rejects trust summaries whose distribution does not match backend review count', () => {
    const detail = deriveServiceDetailPresentation(serviceDetails({
      trust: {
        averageRating: 4.5,
        totalReviews: 8,
        trustBadge: 'Trusted',
        ratingDistribution: { 5: 8, 4: 2, 3: 1, 2: 0, 1: 0 },
        ratingPercentages: { 5: 72.73, 4: 18.18, 3: 9.09, 2: 0, 1: 0 },
      },
    }))

    expect(detail?.trust).toBeNull()
  })

  it('rejects distributions whose weighted average contradicts the backend average', () => {
    const detail = deriveServiceDetailPresentation(serviceDetails({
      trust: {
        averageRating: 5,
        totalReviews: 8,
        trustBadge: 'Rising',
        ratingDistribution: { 5: 5, 4: 2, 3: 1, 2: 0, 1: 0 },
        ratingPercentages: { 5: 62.5, 4: 25, 3: 12.5, 2: 0, 1: 0 },
      },
    }))

    expect(detail?.trust).toBeNull()
  })

  it('fails request entry closed unless backend eligibility is internally consistent', () => {
    expect(deriveServiceDetailPresentation(serviceDetails({
      requestEntry: {
        canRequest: false,
        reason: 'ActiveRequestExists',
        activeRequestId: '6d57c340-c462-4d7b-9507-daeb4372226c',
      },
    }))?.requestEntry).toEqual({
      canRequest: false,
      reason: 'ActiveRequestExists',
      activeRequestId: '6d57c340-c462-4d7b-9507-daeb4372226c',
    })

    expect(deriveServiceDetailPresentation(serviceDetails({
      requestEntry: {
        canRequest: true,
        reason: 'OwnService',
      },
    }))?.requestEntry).toEqual({
      canRequest: false,
      reason: 'Unavailable',
      activeRequestId: null,
    })

    expect(deriveServiceDetailPresentation(serviceDetails({
      requestEntry: undefined,
    }))?.requestEntry.canRequest).toBe(false)
  })
})

describe('deriveServiceReviewPresentation', () => {
  it('keeps bounded review evidence without inventing revenue currency', () => {
    const review = deriveServiceReviewPresentation(serviceReview())

    expect(review).toMatchObject({
      rating: 5,
      buyerInitial: 'C',
      marketingEvidence: {
        evidenceType: 'PosVerified',
        ordersCount: 12,
      },
    })
    expect(review?.marketingEvidence).not.toHaveProperty('revenueAmount')
  })

  it('rejects malformed reviews and suppresses malformed evidence', () => {
    expect(deriveServiceReviewPresentation(serviceReview({ rating: 8 }))).toBeNull()
    expect(deriveServiceReviewPresentation(serviceReview({
      updatedAt: '2026-06-14T00:00:01Z',
    }))).toBeNull()

    const review = deriveServiceReviewPresentation(serviceReview({
      marketingEvidence: {
        campaignName: 'Launch',
        platform: 'Google',
        startDate: '2026-06-01T00:00:00Z',
        endDate: '2026-05-01T00:00:00Z',
        evidenceType: 'PosVerified',
        ordersCount: -1,
      },
    }))

    expect(review?.marketingEvidence).toBeNull()
  })
})

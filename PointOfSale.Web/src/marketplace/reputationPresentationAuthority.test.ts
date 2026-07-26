import { describe, expect, it } from 'vitest'
import {
  deriveProviderReputationStatsPresentation,
  deriveReputationDistributionPresentation,
  deriveReputationSummaryPresentation,
} from './reputationPresentationAuthority'

describe('reputation presentation authority', () => {
  it('accepts coherent backend reputation without recomputing displayed values', () => {
    expect(deriveReputationSummaryPresentation({
      averageRating: 4.5,
      totalReviews: 24,
      trustBadge: 'Trusted',
    })).toEqual({
      averageRating: 4.5,
      totalReviews: 24,
      trustBadge: 'Trusted',
    })

    expect(deriveReputationSummaryPresentation({
      averageRating: 0,
      totalReviews: 0,
      trustBadge: 'New',
    })).not.toBeNull()
  })

  it('rejects impossible average, count, and badge combinations', () => {
    expect(deriveReputationSummaryPresentation({
      averageRating: 5,
      totalReviews: 0,
      trustBadge: 'New',
    })).toBeNull()
    expect(deriveReputationSummaryPresentation({
      averageRating: 0,
      totalReviews: 4,
      trustBadge: 'New',
    })).toBeNull()
    expect(deriveReputationSummaryPresentation({
      averageRating: 4.8,
      totalReviews: 60,
      trustBadge: 'Rising',
    })).toBeNull()
    expect(deriveReputationSummaryPresentation({
      averageRating: 4.8,
      totalReviews: 60,
      trustBadge: 'Fabricated',
    })).toBeNull()
  })

  it('requires distributions to match both backend total and backend average', () => {
    expect(deriveReputationDistributionPresentation({
      averageRating: 4.5,
      totalReviews: 8,
      trustBadge: 'Rising',
      ratingDistribution: { 5: 5, 4: 2, 3: 1, 2: 0, 1: 0 },
      ratingPercentages: { 5: 62.5, 4: 25, 3: 12.5, 2: 0, 1: 0 },
    })).toMatchObject({
      averageRating: 4.5,
      totalReviews: 8,
      trustBadge: 'Rising',
    })

    expect(deriveReputationDistributionPresentation({
      averageRating: 5,
      totalReviews: 8,
      trustBadge: 'Rising',
      ratingDistribution: { 5: 5, 4: 2, 3: 1, 2: 0, 1: 0 },
      ratingPercentages: { 5: 62.5, 4: 25, 3: 12.5, 2: 0, 1: 0 },
    })).toBeNull()

    expect(deriveReputationDistributionPresentation({
      averageRating: 4.5,
      totalReviews: 7,
      trustBadge: 'Rising',
      ratingDistribution: { 5: 5, 4: 2, 3: 1, 2: 0, 1: 0 },
      ratingPercentages: { 5: 71.43, 4: 28.57, 3: 14.29, 2: 0, 1: 0 },
    })).toBeNull()

    expect(deriveReputationDistributionPresentation({
      averageRating: 4.5,
      totalReviews: 8,
      trustBadge: 'Rising',
      ratingDistribution: { 5: 5, 4: 2, 3: 1, 2: 0, 1: 0 },
      ratingPercentages: { 5: 100, 4: 0, 3: 0, 2: 0, 1: 0 },
    })).toBeNull()
  })

  it('fails provider statistics closed instead of substituting frontend zeroes', () => {
    expect(deriveProviderReputationStatsPresentation({
      servicesOffered: 3,
      completedRequests: 20,
      averageRating: 4.75,
      totalReviews: 20,
      trustBadge: 'Trusted',
    })).toMatchObject({
      servicesOffered: 3,
      completedRequests: 20,
      reputation: {
        averageRating: 4.75,
        totalReviews: 20,
        trustBadge: 'Trusted',
      },
    })

    expect(deriveProviderReputationStatsPresentation({
      servicesOffered: 3,
      completedRequests: 12,
      averageRating: Number.NaN,
      totalReviews: 20,
      trustBadge: 'Trusted',
    })).toBeNull()

    expect(deriveProviderReputationStatsPresentation({
      servicesOffered: 3,
      completedRequests: 19,
      averageRating: 4.75,
      totalReviews: 20,
      trustBadge: 'Trusted',
    })).toBeNull()
  })
})

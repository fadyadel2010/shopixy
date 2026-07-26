import { describe, expect, it } from 'vitest'
import { deriveProviderAnalyticsDashboardPresentation } from './dashboardTrendAuthority'

describe('dashboard trend authority', () => {
  const providerStats = {
    tenantId: '11111111-1111-1111-1111-111111111111',
    providerUserId: '22222222-2222-2222-2222-222222222222',
    providerProfileId: '33333333-3333-3333-3333-333333333333',
    servicesOffered: 3,
    completedRequests: 20,
    averageRating: 4.75,
    totalReviews: 20,
    trustBadge: 'Trusted',
  } as const

  const expected = {
    authorityScope: `${providerStats.tenantId}:${providerStats.providerUserId}`,
    providerProfileId: providerStats.providerProfileId,
  }

  it('renders dashboard cards only from accepted backend analytics authority', () => {
    expect(deriveProviderAnalyticsDashboardPresentation(providerStats, expected)).toEqual({
      cards: expect.objectContaining({
        tenantId: providerStats.tenantId,
        providerUserId: providerStats.providerUserId,
        providerProfileId: providerStats.providerProfileId,
        servicesOffered: 3,
        completedRequests: 20,
      }),
      trends: [],
      historicalMetrics: [],
      charts: [],
    })
  })

  it('rejects actor, tenant, profile, and metric replay before dashboard presentation', () => {
    expect(deriveProviderAnalyticsDashboardPresentation(providerStats, {
      ...expected,
      authorityScope: `${providerStats.tenantId}:foreign-user`,
    })).toBeNull()

    expect(deriveProviderAnalyticsDashboardPresentation(providerStats, {
      ...expected,
      providerProfileId: 'foreign-profile',
    })).toBeNull()

    expect(deriveProviderAnalyticsDashboardPresentation({
      ...providerStats,
      totalReviews: 21,
    }, expected)).toBeNull()
  })

  it('fails closed for unsupported trend history growth comparison chart and graph DTOs', () => {
    for (const key of [
      'trend',
      'trends',
      'historicalMetrics',
      'growthMetrics',
      'comparisonMetrics',
      'chart',
      'graphs',
      'performanceSummary',
    ]) {
      expect(deriveProviderAnalyticsDashboardPresentation({
        ...providerStats,
        [key]: [{ value: 999 }],
      }, expected)).toBeNull()
    }
  })
})

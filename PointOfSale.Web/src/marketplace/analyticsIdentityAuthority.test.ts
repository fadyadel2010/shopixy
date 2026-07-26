import { describe, expect, it } from 'vitest'
import {
  deriveProviderAnalyticsPresentation,
  deriveServiceAnalyticsPresentation,
} from './analyticsIdentityAuthority'

describe('analytics identity authority', () => {
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

  it('accepts provider analytics only for the current actor and profile identity', () => {
    expect(deriveProviderAnalyticsPresentation(providerStats, {
      authorityScope: `${providerStats.tenantId}:${providerStats.providerUserId}`,
      providerProfileId: providerStats.providerProfileId,
    })).toMatchObject({
      providerProfileId: providerStats.providerProfileId,
      servicesOffered: 3,
      completedRequests: 20,
    })
  })

  it('rejects actor, tenant, profile, and malformed metric replay', () => {
    expect(deriveProviderAnalyticsPresentation(providerStats, {
      authorityScope: `${providerStats.tenantId}:foreign-user`,
      providerProfileId: providerStats.providerProfileId,
    })).toBeNull()

    expect(deriveProviderAnalyticsPresentation(providerStats, {
      authorityScope: `${providerStats.tenantId}:${providerStats.providerUserId}`,
      providerProfileId: 'foreign-profile',
    })).toBeNull()

    expect(deriveProviderAnalyticsPresentation({
      ...providerStats,
      completedRequests: -1,
    }, {
      authorityScope: `${providerStats.tenantId}:${providerStats.providerUserId}`,
      providerProfileId: providerStats.providerProfileId,
    })).toBeNull()

    expect(deriveProviderAnalyticsPresentation({
      ...providerStats,
      completedRequests: 19,
      totalReviews: 20,
    }, {
      authorityScope: `${providerStats.tenantId}:${providerStats.providerUserId}`,
      providerProfileId: providerStats.providerProfileId,
    })).toBeNull()
  })

  it('requires service KPI arithmetic to match backend counts exactly', () => {
    expect(deriveServiceAnalyticsPresentation({
      totalRequests: 10,
      completedRequests: 7,
      completionRate: 0.7,
    })).toEqual({
      totalRequests: 10,
      completedRequests: 7,
      completionRate: 0.7,
    })

    expect(deriveServiceAnalyticsPresentation({
      totalRequests: 10,
      completedRequests: 7,
      completionRate: 0.8,
    })).toBeNull()

    expect(deriveServiceAnalyticsPresentation({
      totalRequests: 0,
      completedRequests: 0,
      completionRate: 0.1,
    })).toBeNull()
  })
})

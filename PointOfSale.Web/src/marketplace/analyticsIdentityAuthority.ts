import type { MarketplaceProfileStats, ServiceResult } from './types'
import {
  deriveProviderReputationStatsPresentation,
  type ReputationSummaryPresentation,
} from './reputationPresentationAuthority'

const normalizeIdentity = (value: unknown) => {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : ''
  return normalized || null
}

const nonNegativeInteger = (value: unknown) => {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isSafeInteger(parsed) && parsed >= 0 ? parsed : null
}

const finiteNumber = (value: unknown) => {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

export type ProviderAnalyticsPresentation = Readonly<{
  tenantId: string
  providerUserId: string
  providerProfileId: string
  servicesOffered: number
  completedRequests: number
  reputation: ReputationSummaryPresentation
}>

export const deriveProviderAnalyticsPresentation = (
  stats: MarketplaceProfileStats | null | undefined,
  expected: {
    authorityScope: string
    providerProfileId: unknown
  },
): ProviderAnalyticsPresentation | null => {
  const tenantId = normalizeIdentity(stats?.tenantId)
  const providerUserId = normalizeIdentity(stats?.providerUserId)
  const providerProfileId = normalizeIdentity(stats?.providerProfileId)
  const expectedProfileId = normalizeIdentity(expected.providerProfileId)
  const expectedScope = normalizeIdentity(expected.authorityScope)
  const aggregate = deriveProviderReputationStatsPresentation(stats)

  if (
    !tenantId
    || !providerUserId
    || !providerProfileId
    || !expectedProfileId
    || expectedScope !== `${tenantId}:${providerUserId}`
    || providerProfileId !== expectedProfileId
    || !aggregate
  ) {
    return null
  }

  return {
    tenantId,
    providerUserId,
    providerProfileId,
    ...aggregate,
  }
}

export type ServiceAnalyticsPresentation = Readonly<{
  totalRequests: number
  completedRequests: number
  completionRate: number
}>

export const deriveServiceAnalyticsPresentation = (
  service: Pick<ServiceResult, 'totalRequests' | 'completedRequests' | 'completionRate'>,
): ServiceAnalyticsPresentation | null => {
  const totalRequests = nonNegativeInteger(service.totalRequests)
  const completedRequests = nonNegativeInteger(service.completedRequests)
  const completionRate = finiteNumber(service.completionRate)

  if (
    totalRequests === null
    || completedRequests === null
    || completedRequests > totalRequests
    || completionRate === null
    || completionRate < 0
    || completionRate > 1
  ) {
    return null
  }

  const expectedRate = totalRequests === 0 ? 0 : completedRequests / totalRequests
  if (Math.abs(completionRate - expectedRate) > 1e-12) {
    return null
  }

  return {
    totalRequests,
    completedRequests,
    completionRate,
  }
}

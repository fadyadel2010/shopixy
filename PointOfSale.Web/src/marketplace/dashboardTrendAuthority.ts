import type { MarketplaceProfileStats } from './types'
import {
  deriveProviderAnalyticsPresentation,
  type ProviderAnalyticsPresentation,
} from './analyticsIdentityAuthority'

const unsupportedDashboardTruthKeys = new Set([
  'dashboard',
  'trend',
  'trends',
  'trendMetrics',
  'historical',
  'history',
  'historicalMetrics',
  'chart',
  'charts',
  'graph',
  'graphs',
  'growth',
  'growthMetrics',
  'comparison',
  'comparisonMetrics',
  'performanceSummary',
])

const hasUnsupportedDashboardTruth = (value: unknown) => {
  if (!value || typeof value !== 'object') {
    return false
  }

  return Object.keys(value).some((key) => unsupportedDashboardTruthKeys.has(key))
}

export type ProviderAnalyticsDashboardPresentation = Readonly<{
  cards: ProviderAnalyticsPresentation
  trends: readonly never[]
  historicalMetrics: readonly never[]
  charts: readonly never[]
}>

export const deriveProviderAnalyticsDashboardPresentation = (
  stats: MarketplaceProfileStats | null | undefined,
  expected: {
    authorityScope: string
    providerProfileId: unknown
  },
): ProviderAnalyticsDashboardPresentation | null => {
  if (hasUnsupportedDashboardTruth(stats)) {
    return null
  }

  const cards = deriveProviderAnalyticsPresentation(stats, expected)
  if (!cards) {
    return null
  }

  return {
    cards,
    trends: [],
    historicalMetrics: [],
    charts: [],
  }
}

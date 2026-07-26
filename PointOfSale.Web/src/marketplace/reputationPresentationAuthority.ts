export const reputationBadges = [
  'New',
  'Rising',
  'Trusted',
  'TopRated',
  'Standard',
] as const

export type ReputationBadge = typeof reputationBadges[number]

const reputationBadgeSet = new Set<string>(reputationBadges)

const finiteNumber = (value: unknown) => {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

const nonNegativeInteger = (value: unknown) => {
  const parsed = finiteNumber(value)
  return parsed !== null && Number.isSafeInteger(parsed) && parsed >= 0 ? parsed : null
}

const isBadgeConsistent = (
  badge: ReputationBadge,
  averageRating: number,
  totalReviews: number,
) => {
  if (totalReviews < 5) return badge === 'New'
  if (averageRating >= 4.6 && totalReviews >= 50) return badge === 'TopRated'
  if (averageRating >= 4.3 && totalReviews >= 20) return badge === 'Trusted'
  if (averageRating >= 4 && totalReviews >= 5) return badge === 'Rising'
  return badge === 'Standard'
}

export type ReputationSummaryPresentation = Readonly<{
  averageRating: number
  totalReviews: number
  trustBadge: ReputationBadge
}>

export const deriveReputationSummaryPresentation = (
  summary: {
    averageRating?: unknown
    totalReviews?: unknown
    trustBadge?: unknown
  } | null | undefined,
): ReputationSummaryPresentation | null => {
  const averageRating = finiteNumber(summary?.averageRating)
  const totalReviews = nonNegativeInteger(summary?.totalReviews)
  const trustBadge = typeof summary?.trustBadge === 'string'
    && reputationBadgeSet.has(summary.trustBadge)
    ? summary.trustBadge as ReputationBadge
    : null

  if (
    averageRating === null
    || totalReviews === null
    || trustBadge === null
    || averageRating < 0
    || averageRating > 5
    || (totalReviews === 0 && averageRating !== 0)
    || (totalReviews > 0 && averageRating < 1)
    || !isBadgeConsistent(trustBadge, averageRating, totalReviews)
  ) {
    return null
  }

  return {
    averageRating,
    totalReviews,
    trustBadge,
  }
}

export type ReputationDistributionPresentation = ReputationSummaryPresentation & Readonly<{
  ratingDistribution: readonly Readonly<[
    rating: number,
    count: number,
    percentage: number,
  ]>[]
}>

export const deriveReputationDistributionPresentation = (
  summary: {
    averageRating?: unknown
    totalReviews?: unknown
    trustBadge?: unknown
    ratingDistribution?: Readonly<Record<number, unknown>>
    ratingPercentages?: Readonly<Record<number, unknown>>
  } | null | undefined,
): ReputationDistributionPresentation | null => {
  const reputation = deriveReputationSummaryPresentation(summary)
  if (!reputation) return null

  const distribution: Array<Readonly<[number, number, number]>> = []
  let totalRatings = 0
  let weightedRatings = 0

  for (const rating of [5, 4, 3, 2, 1]) {
    const count = nonNegativeInteger(summary?.ratingDistribution?.[rating])
    const percentage = finiteNumber(summary?.ratingPercentages?.[rating])
    if (
      count === null
      || percentage === null
      || percentage < 0
      || percentage > 100
    ) {
      return null
    }

    const expectedPercentage = reputation.totalReviews === 0
      ? 0
      : Math.round(((count * 100) / reputation.totalReviews + Number.EPSILON) * 100) / 100
    if (Math.abs(percentage - expectedPercentage) > 1e-9) return null

    distribution.push([rating, count, percentage])
    totalRatings += count
    weightedRatings += rating * count
  }

  if (totalRatings !== reputation.totalReviews) return null

  const distributionAverage = totalRatings === 0
    ? 0
    : Math.round((weightedRatings / totalRatings) * 100) / 100
  if (distributionAverage !== reputation.averageRating) return null

  return {
    ...reputation,
    ratingDistribution: distribution,
  }
}

export type ProviderReputationStatsPresentation = Readonly<{
  servicesOffered: number
  completedRequests: number
  reputation: ReputationSummaryPresentation
}>

export const deriveProviderReputationStatsPresentation = (
  stats: {
    servicesOffered?: unknown
    completedRequests?: unknown
    averageRating?: unknown
    totalReviews?: unknown
    trustBadge?: unknown
  } | null | undefined,
): ProviderReputationStatsPresentation | null => {
  const servicesOffered = nonNegativeInteger(stats?.servicesOffered)
  const completedRequests = nonNegativeInteger(stats?.completedRequests)
  const reputation = deriveReputationSummaryPresentation(stats)

  if (
    servicesOffered === null
    || completedRequests === null
    || !reputation
    || reputation.totalReviews > completedRequests
  ) {
    return null
  }

  return {
    servicesOffered,
    completedRequests,
    reputation,
  }
}

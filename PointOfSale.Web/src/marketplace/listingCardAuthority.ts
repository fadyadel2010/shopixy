import type { ServiceResult } from './types'
import { deriveReputationSummaryPresentation } from './reputationPresentationAuthority'
import { deriveServiceAnalyticsPresentation } from './analyticsIdentityAuthority'

const pricingTypes = new Set(['Fixed', 'Hourly', 'Package'])
const serviceMediaPrefix = 'marketplace/services/'

const nonEmpty = (value: unknown) => {
  const normalized = typeof value === 'string' ? value.trim() : ''
  return normalized || null
}

const finiteNumber = (value: unknown) => {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

const parsePrimaryImage = (rawImages: string | undefined) => {
  if (!rawImages) return null

  try {
    const images = JSON.parse(rawImages)
    if (!Array.isArray(images) || typeof images[0] !== 'string') return null

    const storageKey = images[0].trim().replace(/\\/g, '/')
    if (
      !storageKey.startsWith(serviceMediaPrefix)
      || storageKey.includes('..')
      || storageKey.includes('?')
      || storageKey.includes('#')
    ) {
      return null
    }

    return `/${storageKey.split('/').map(encodeURIComponent).join('/')}`
  } catch {
    return null
  }
}

export type ListingCardPresentation = Readonly<{
  serviceId: string
  title: string
  description: string
  category: string | null
  providerDisplayName: string
  providerInitial: string
  averageRating: number | null
  totalReviews: number | null
  trustBadge: string | null
  completedRequests: number | null
  completionRate: number | null
  startingPrice: number | null
  pricingType: 'Fixed' | 'Hourly' | 'Package' | null
  currency: string | null
  primaryImage: string | null
}>

export const deriveListingCardPresentation = (
  service: ServiceResult,
): ListingCardPresentation | null => {
  const serviceId = nonEmpty(service.serviceId)
  const providerProfileId = nonEmpty(service.providerProfileId)
  const title = nonEmpty(service.title)
  const providerDisplayName = nonEmpty(service.providerDisplayName)

  if (!serviceId || !providerProfileId || !title || !providerDisplayName) {
    return null
  }

  const reputation = deriveReputationSummaryPresentation(service)
  const analytics = deriveServiceAnalyticsPresentation(service)
  const aggregatesAreConsistent = !reputation
    || !analytics
    || reputation.totalReviews <= analytics.completedRequests
  const acceptedReputation = aggregatesAreConsistent ? reputation : null
  const acceptedAnalytics = aggregatesAreConsistent ? analytics : null

  const startingPrice = service.startingPrice == null
    ? null
    : finiteNumber(service.startingPrice)
  const currency = nonEmpty(service.currency)?.toUpperCase() ?? null
  const pricingType = pricingTypes.has(service.pricingType ?? '')
    ? service.pricingType as ListingCardPresentation['pricingType']
    : null
  const hasValidPrice = startingPrice !== null
    && startingPrice >= 0
    && currency !== null
    && /^[A-Z]{3}$/.test(currency)
    && pricingType !== null

  return {
    serviceId,
    title,
    description: nonEmpty(service.description) ?? '',
    category: nonEmpty(service.categoryId),
    providerDisplayName,
    providerInitial: providerDisplayName.charAt(0).toUpperCase(),
    averageRating: acceptedReputation?.averageRating ?? null,
    totalReviews: acceptedReputation?.totalReviews ?? null,
    trustBadge: acceptedReputation?.trustBadge ?? null,
    completedRequests: acceptedAnalytics?.completedRequests ?? null,
    completionRate: acceptedAnalytics?.completionRate ?? null,
    startingPrice: hasValidPrice ? startingPrice : null,
    pricingType: hasValidPrice ? pricingType : null,
    currency: hasValidPrice ? currency : null,
    primaryImage: parsePrimaryImage(service.serviceImages),
  }
}

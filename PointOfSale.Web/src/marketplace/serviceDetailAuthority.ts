import type { ServiceDetails, ServiceReview } from './types'
import { deriveReputationDistributionPresentation } from './reputationPresentationAuthority'

const pricingTypes = new Set(['Fixed', 'Hourly', 'Package'])
const evidenceTypes = new Set(['SelfReported', 'PosVerified'])
const serviceMediaPrefix = 'marketplace/services/'
const requestEntryReasons = new Set([
  'Eligible',
  'OwnService',
  'ActiveRequestExists',
  'Unavailable',
  'ActorUnavailable',
])

const nonEmpty = (value: unknown) => {
  const normalized = typeof value === 'string' ? value.trim() : ''
  return normalized || null
}

const finiteNumber = (value: unknown) => {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

const nonNegativeInteger = (value: unknown) => {
  const parsed = finiteNumber(value)
  return parsed !== null && Number.isSafeInteger(parsed) && parsed >= 0 ? parsed : null
}

const safeDate = (value: unknown) => {
  const normalized = nonEmpty(value)
  return normalized && Number.isFinite(Date.parse(normalized)) ? normalized : null
}

const safeWebUrl = (value: unknown) => {
  const normalized = nonEmpty(value)
  if (!normalized) return null

  try {
    const url = new URL(normalized, 'http://localhost')
    return url.protocol === 'http:' || url.protocol === 'https:' ? normalized : null
  } catch {
    return null
  }
}

const parseRequestEntry = (requestEntry: ServiceDetails['requestEntry']) => {
  const reason = nonEmpty(requestEntry?.reason)
  const canRequest = requestEntry?.canRequest === true
  const activeRequestId = nonEmpty(requestEntry?.activeRequestId)

  if (!reason || !requestEntryReasons.has(reason)) {
    return {
      canRequest: false,
      reason: 'Unavailable',
      activeRequestId: null,
    } as const
  }

  if (canRequest && reason !== 'Eligible') {
    return {
      canRequest: false,
      reason: 'Unavailable',
      activeRequestId: null,
    } as const
  }

  if (!canRequest && reason === 'Eligible') {
    return {
      canRequest: false,
      reason: 'Unavailable',
      activeRequestId: null,
    } as const
  }

  return {
    canRequest,
    reason,
    activeRequestId: reason === 'ActiveRequestExists' ? activeRequestId : null,
  } as const
}

const parseStringArray = (rawValue: unknown, maxItems: number) => {
  if (!nonEmpty(rawValue)) return []

  try {
    const values = JSON.parse(String(rawValue))
    if (!Array.isArray(values)) return []

    return values
      .slice(0, maxItems)
      .map(nonEmpty)
      .filter((value): value is string => value !== null)
  } catch {
    return []
  }
}

const parseServiceImages = (rawImages: unknown) => parseStringArray(rawImages, 20)
  .map((image) => image.replace(/\\/g, '/'))
  .filter((storageKey) =>
    storageKey.startsWith(serviceMediaPrefix)
    && !storageKey.includes('..')
    && !storageKey.includes('?')
    && !storageKey.includes('#'))
  .map((storageKey) => `/${storageKey.split('/').map(encodeURIComponent).join('/')}`)

const parseTrust = (trust: ServiceDetails['trust']) => {
  return deriveReputationDistributionPresentation(trust)
}

export type ServiceDetailPresentation = Readonly<{
  serviceId: string
  title: string
  description: string
  category: string | null
  providerProfileId: string
  providerDisplayName: string
  isActive: true
  startingPrice: number | null
  pricingType: 'Fixed' | 'Hourly' | 'Package' | null
  currency: string | null
  serviceImages: readonly string[]
  requestEntry: ReturnType<typeof parseRequestEntry>
  providerProfile: Readonly<{
    bio: string | null
    profileImageUrl: string | null
    location: string | null
    skills: readonly string[]
    portfolioLinks: readonly string[]
  }>
  trust: ReturnType<typeof parseTrust>
}>

export const deriveServiceDetailPresentation = (
  service: ServiceDetails,
): ServiceDetailPresentation | null => {
  const serviceId = nonEmpty(service.serviceId)
  const providerProfileId = nonEmpty(service.providerProfileId)
  const providerDisplayName = nonEmpty(service.providerDisplayName)
  const title = nonEmpty(service.title)
  const description = nonEmpty(service.description)

  if (
    !serviceId
    || !providerProfileId
    || !providerDisplayName
    || !title
    || !description
    || service.isActive !== true
  ) {
    return null
  }

  const startingPrice = service.startingPrice == null
    ? null
    : finiteNumber(service.startingPrice)
  const currency = nonEmpty(service.currency)?.toUpperCase() ?? null
  const pricingType = pricingTypes.has(service.pricingType ?? '')
    ? service.pricingType as ServiceDetailPresentation['pricingType']
    : null
  const hasValidPricing = startingPrice !== null
    && startingPrice >= 0
    && currency !== null
    && /^[A-Z]{3}$/.test(currency)
    && pricingType !== null

  return {
    serviceId,
    title,
    description,
    category: nonEmpty(service.category),
    providerProfileId,
    providerDisplayName,
    isActive: true,
    startingPrice: hasValidPricing ? startingPrice : null,
    pricingType: hasValidPricing ? pricingType : null,
    currency: hasValidPricing ? currency : null,
    serviceImages: parseServiceImages(service.serviceImages),
    requestEntry: parseRequestEntry(service.requestEntry),
    providerProfile: {
      bio: nonEmpty(service.providerProfile?.bio),
      profileImageUrl: safeWebUrl(service.providerProfile?.profileImageUrl),
      location: nonEmpty(service.providerProfile?.location),
      skills: parseStringArray(service.providerProfile?.skills, 30),
      portfolioLinks: parseStringArray(service.providerProfile?.portfolioLinks, 20)
        .map(safeWebUrl)
        .filter((link): link is string => link !== null),
    },
    trust: parseTrust(service.trust),
  }
}

export type ServiceReviewPresentation = Readonly<{
  reviewId: string
  serviceRequestId: string
  serviceId: string
  reviewerUserId: string
  providerUserId: string
  buyerName: string
  buyerInitial: string
  rating: number
  comment: string | null
  createdAt: string
  updatedAt: string
  marketingEvidence: Readonly<{
    campaignName: string
    platform: string
    startDate: string
    endDate: string
    evidenceType: 'SelfReported' | 'PosVerified'
    ordersCount: number | null
  }> | null
}>

export const deriveServiceReviewPresentation = (
  review: ServiceReview,
): ServiceReviewPresentation | null => {
  const reviewId = nonEmpty(review.reviewId)
  const serviceRequestId = nonEmpty(review.serviceRequestId)
  const serviceId = nonEmpty(review.serviceId)
  const reviewerUserId = nonEmpty(review.reviewerUserId)
  const providerUserId = nonEmpty(review.providerUserId)
  const buyerName = nonEmpty(review.buyerName)
  const rating = finiteNumber(review.rating)
  const createdAt = safeDate(review.createdAt)
  const updatedAt = safeDate(review.updatedAt)

  if (
    !reviewId
    || !serviceRequestId
    || !serviceId
    || !reviewerUserId
    || !providerUserId
    || !buyerName
    || rating === null
    || !Number.isInteger(rating)
    || rating < 1
    || rating > 5
    || !createdAt
    || !updatedAt
    || updatedAt !== createdAt
  ) {
    return null
  }

  const evidence = review.marketingEvidence
  const evidenceType = nonEmpty(evidence?.evidenceType)
  const campaignName = nonEmpty(evidence?.campaignName)
  const platform = nonEmpty(evidence?.platform)
  const startDate = safeDate(evidence?.startDate)
  const endDate = safeDate(evidence?.endDate)
  const ordersCount = evidence?.ordersCount == null
    ? null
    : nonNegativeInteger(evidence.ordersCount)
  const hasValidEvidence = !evidence || (
    evidenceType !== null
    && evidenceTypes.has(evidenceType)
    && campaignName !== null
    && platform !== null
    && startDate !== null
    && endDate !== null
    && Date.parse(endDate) >= Date.parse(startDate)
    && (evidenceType === 'SelfReported'
      ? ordersCount === null
      : ordersCount !== null)
  )

  return {
    reviewId,
    serviceRequestId,
    serviceId,
    reviewerUserId,
    providerUserId,
    buyerName,
    buyerInitial: buyerName.charAt(0).toUpperCase(),
    rating,
    comment: nonEmpty(review.comment),
    createdAt,
    updatedAt,
    marketingEvidence: evidence && hasValidEvidence ? {
      campaignName: campaignName!,
      platform: platform!,
      startDate: startDate!,
      endDate: endDate!,
      evidenceType: evidenceType as 'SelfReported' | 'PosVerified',
      ordersCount,
    } : null,
  }
}

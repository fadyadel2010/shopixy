import { deriveServiceReviewPresentation } from './serviceDetailAuthority'
import type { ServiceReviewHistory } from './types'

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const isoTimestampPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/

const normalizedGuid = (value: unknown) => {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : ''
  return guidPattern.test(normalized) ? normalized : null
}

const validAuditTimestamp = (value: unknown) =>
  typeof value === 'string'
  && isoTimestampPattern.test(value)
  && Number.isFinite(Date.parse(value))

export type ServiceReviewHistoryPresentation = Readonly<{
  serviceId: string
  providerProfileId: string
  providerUserId: string
  totalReviews: number
  items: readonly NonNullable<ReturnType<typeof deriveServiceReviewPresentation>>[]
}>

export const deriveServiceReviewHistoryPresentation = (
  history: ServiceReviewHistory,
  expectedServiceId: string,
  expectedProviderProfileId: string,
): ServiceReviewHistoryPresentation | null => {
  const serviceId = normalizedGuid(history?.serviceId)
  const providerProfileId = normalizedGuid(history?.providerProfileId)
  const providerUserId = normalizedGuid(history?.providerUserId)
  const expectedService = normalizedGuid(expectedServiceId)
  const expectedProfile = normalizedGuid(expectedProviderProfileId)

  if (
    !serviceId
    || !providerProfileId
    || !providerUserId
    || serviceId !== expectedService
    || providerProfileId !== expectedProfile
    || !Number.isSafeInteger(history?.totalReviews)
    || history.totalReviews < 0
    || !Array.isArray(history?.items)
    || history.items.length !== history.totalReviews
  ) {
    return null
  }

  const reviewIds = new Set<string>()
  const requestIds = new Set<string>()
  const items: NonNullable<ReturnType<typeof deriveServiceReviewPresentation>>[] = []

  for (const review of history.items) {
    const presentation = deriveServiceReviewPresentation(review)
    const reviewId = normalizedGuid(review?.reviewId)
    const requestId = normalizedGuid(review?.serviceRequestId)
    const rowServiceId = normalizedGuid(review?.serviceId)
    const rowProviderUserId = normalizedGuid(review?.providerUserId)
    const reviewerUserId = normalizedGuid(review?.reviewerUserId)

    if (
      !presentation
      || !reviewId
      || !requestId
      || !reviewerUserId
      || rowServiceId !== serviceId
      || rowProviderUserId !== providerUserId
      || !validAuditTimestamp(review.createdAt)
      || !validAuditTimestamp(review.updatedAt)
      || review.updatedAt !== review.createdAt
      || reviewIds.has(reviewId)
      || requestIds.has(requestId)
    ) {
      return null
    }

    const previous = items[items.length - 1]
    if (previous) {
      const previousTime = Date.parse(previous.createdAt)
      const currentTime = Date.parse(presentation.createdAt)
      if (
        currentTime > previousTime
        || (currentTime === previousTime
          && presentation.reviewId >= previous.reviewId)
      ) {
        return null
      }
    }

    reviewIds.add(reviewId)
    requestIds.add(requestId)
    items.push(presentation)
  }

  return {
    serviceId,
    providerProfileId,
    providerUserId,
    totalReviews: history.totalReviews,
    items,
  }
}

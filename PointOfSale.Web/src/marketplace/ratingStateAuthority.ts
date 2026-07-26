import type { ServiceRequest, ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'

export type ServiceRequestRatingStatePresentation = Readonly<{
  requestId: string
  serviceId: string
  buyerUserId: string
  providerUserId: string
  state: 'unrated' | 'rated'
  canRate: boolean
  reviewId: string | null
}>

type RatingStateRequest = Pick<
  ServiceRequest,
  'requestId' | 'serviceId' | 'buyerUserId' | 'providerUserId' | 'canReview' | 'existingReviewId'
> | Pick<
  ServiceRequestDetails,
  'requestId' | 'serviceId' | 'buyerUserId' | 'providerUserId' | 'capabilities'
>

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const normalizedGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) ? normalized : null
}

const ratingAuthority = (request: RatingStateRequest) => {
  if ('capabilities' in request) {
    return {
      canReview: request.capabilities?.canReview,
      existingReviewId: request.capabilities?.existingReviewId,
    }
  }

  return {
    canReview: request.canReview,
    existingReviewId: request.existingReviewId,
  }
}

export function deriveServiceRequestRatingStatePresentation(
  request: RatingStateRequest,
  lifecycle: ServiceRequestLifecyclePresentation | null,
  currentUserId: unknown,
): ServiceRequestRatingStatePresentation | null {
  if (!lifecycle || lifecycle.statusKey !== 'completedByBuyer') {
    return null
  }

  const requestId = normalizedGuid(request.requestId)
  const serviceId = normalizedGuid(request.serviceId)
  const buyerUserId = normalizedGuid(request.buyerUserId)
  const providerUserId = normalizedGuid(request.providerUserId)
  const actorUserId = normalizedGuid(currentUserId)

  if (
    !requestId
    || !serviceId
    || !buyerUserId
    || !providerUserId
    || !actorUserId
    || buyerUserId === providerUserId
    || actorUserId !== buyerUserId
  ) {
    return null
  }

  const authority = ratingAuthority(request)
  if (typeof authority.canReview !== 'boolean') {
    return null
  }

  const rawReviewId = authority.existingReviewId
  const reviewId = normalizedGuid(rawReviewId)
  if (rawReviewId !== null && rawReviewId !== undefined && !reviewId) {
    return null
  }

  if (authority.canReview) {
    if (!lifecycle.canReview || reviewId !== null) {
      return null
    }

    return {
      requestId,
      serviceId,
      buyerUserId,
      providerUserId,
      state: 'unrated',
      canRate: true,
      reviewId: null,
    }
  }

  if (lifecycle.canReview || reviewId === null) {
    return null
  }

  return {
    requestId,
    serviceId,
    buyerUserId,
    providerUserId,
    state: 'rated',
    canRate: false,
    reviewId,
  }
}

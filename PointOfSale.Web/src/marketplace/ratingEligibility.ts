import type { ServiceRequest, ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'
import { deriveServiceRequestRatingStatePresentation } from './ratingStateAuthority'

export type ServiceRequestRatingEligibility = Readonly<{
  requestId: string
  serviceId: string
  buyerUserId: string
  providerUserId: string
  canRate: boolean
  existingReviewId: string | null
}>

type RatingEligibilityRequest = Pick<
  ServiceRequest,
  'requestId' | 'serviceId' | 'buyerUserId' | 'providerUserId' | 'canReview' | 'existingReviewId'
> | Pick<
  ServiceRequestDetails,
  'requestId' | 'serviceId' | 'buyerUserId' | 'providerUserId' | 'capabilities'
>

export function deriveServiceRequestRatingEligibility(
  request: RatingEligibilityRequest,
  lifecycle: ServiceRequestLifecyclePresentation | null,
  currentUserId: unknown,
): ServiceRequestRatingEligibility | null {
  const ratingState = deriveServiceRequestRatingStatePresentation(
    request,
    lifecycle,
    currentUserId,
  )

  return ratingState ? {
    requestId: ratingState.requestId,
    serviceId: ratingState.serviceId,
    buyerUserId: ratingState.buyerUserId,
    providerUserId: ratingState.providerUserId,
    canRate: ratingState.canRate,
    existingReviewId: ratingState.reviewId,
  } : null
}

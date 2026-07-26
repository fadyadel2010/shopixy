import { getMarketplaceAuthorityScope } from './authority'
import { deriveServiceRequestCompletionStatePresentation } from './completionStateAuthority'
import { deriveServiceRequestRatingEligibility } from './ratingEligibility'
import { deriveServiceRequestDetailsLifecyclePresentation } from './status'
import type {
  CreateReviewRequest,
  ServiceRequestDetails,
  ServiceReviewCreationAuthority,
} from './types'

type ExecuteRatingWorkflowOptions = Readonly<{
  review: CreateReviewRequest
  authorityScope: string
  actorUserId: unknown
  revalidate: () => Promise<ServiceRequestDetails>
  mutate: (review: CreateReviewRequest) => Promise<ServiceReviewCreationAuthority>
  getCurrentAuthorityScope?: () => string
}>

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const isoTimestampPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/
const marketingPlatforms = new Set([
  'Facebook',
  'Instagram',
  'Google',
  'TikTok',
  'Twitter',
  'LinkedIn',
  'YouTube',
  'Snapchat',
  'Email',
  'WhatsApp',
  'Other',
])
const ratingExecutions = new Set<string>()

const normalizedGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) ? normalized : null
}

const validTimestamp = (value: unknown) => {
  const normalized = String(value ?? '').trim()
  return isoTimestampPattern.test(normalized) && Number.isFinite(Date.parse(normalized))
}

export function deriveServiceReviewSubmissionAuthority(
  review: CreateReviewRequest,
  request: ServiceRequestDetails,
): CreateReviewRequest | null {
  const requestId = normalizedGuid(review?.serviceRequestId)
  const authoritativeRequestId = normalizedGuid(request?.requestId)
  const comment = review?.comment

  if (
    !requestId
    || requestId !== authoritativeRequestId
    || !Number.isInteger(review?.rating)
    || review.rating < 1
    || review.rating > 5
    || (comment !== undefined && (typeof comment !== 'string' || comment.length > 5000))
  ) {
    return null
  }

  const isMarketing = String(request.serviceCategory ?? '').trim().toLowerCase() === 'marketing'
  if (isMarketing !== (review.evidence !== undefined)) {
    return null
  }

  if (!review.evidence) {
    return {
      serviceRequestId: requestId,
      rating: review.rating,
      ...(comment === undefined ? {} : { comment }),
    }
  }

  const campaignName = String(review.evidence.campaignName ?? '').trim()
  const platform = String(review.evidence.platform ?? '')
  const startDate = String(review.evidence.startDate ?? '').trim()
  const endDate = String(review.evidence.endDate ?? '').trim()
  if (
    !campaignName
    || campaignName.length > 200
    || !marketingPlatforms.has(platform)
    || !validTimestamp(startDate)
    || !validTimestamp(endDate)
    || Date.parse(endDate) < Date.parse(startDate)
  ) {
    return null
  }

  return {
    serviceRequestId: requestId,
    rating: review.rating,
    ...(comment === undefined ? {} : { comment }),
    evidence: {
      campaignName,
      platform: platform as NonNullable<CreateReviewRequest['evidence']>['platform'],
      startDate,
      endDate,
    },
  }
}

const deriveEligibility = (
  request: ServiceRequestDetails,
  actorUserId: unknown,
) => {
  const lifecycleCandidate = deriveServiceRequestDetailsLifecyclePresentation(request)
  const completionState = deriveServiceRequestCompletionStatePresentation(
    request,
    lifecycleCandidate,
    actorUserId,
  )
  return deriveServiceRequestRatingEligibility(
    request,
    completionState?.lifecycle ?? null,
    actorUserId,
  )
}

export function deriveServiceReviewCreationAuthority(
  response: ServiceReviewCreationAuthority | null | undefined,
  review: CreateReviewRequest,
  request: ServiceRequestDetails,
  actorUserId: unknown,
): ServiceReviewCreationAuthority | null {
  const reviewId = normalizedGuid(response?.reviewId)
  const requestId = normalizedGuid(response?.serviceRequestId)
  const serviceId = normalizedGuid(response?.serviceId)
  const reviewerUserId = normalizedGuid(response?.reviewerUserId)
  const providerUserId = normalizedGuid(response?.providerUserId)
  const actorId = normalizedGuid(actorUserId)

  if (
    !reviewId
    || requestId !== normalizedGuid(review.serviceRequestId)
    || requestId !== normalizedGuid(request.requestId)
    || serviceId !== normalizedGuid(request.serviceId)
    || reviewerUserId !== actorId
    || reviewerUserId !== normalizedGuid(request.buyerUserId)
    || providerUserId !== normalizedGuid(request.providerUserId)
    || reviewerUserId === providerUserId
    || response?.rating !== review.rating
    || !Number.isInteger(response.rating)
    || response.rating < 1
    || response.rating > 5
    || !validTimestamp(response.createdAt)
  ) {
    return null
  }

  return response
}

export async function executeServiceRequestRatingWorkflow({
  review,
  authorityScope,
  actorUserId,
  revalidate,
  mutate,
  getCurrentAuthorityScope = getMarketplaceAuthorityScope,
}: ExecuteRatingWorkflowOptions): Promise<ServiceReviewCreationAuthority> {
  const requestId = normalizedGuid(review.serviceRequestId)
  if (
    !requestId
    || authorityScope === 'ineligible'
  ) {
    throw new Error('Invalid rating workflow identity')
  }

  const executionKey = `${authorityScope}:${requestId}`
  if (ratingExecutions.has(executionKey)) {
    throw new Error('Rating workflow already in progress')
  }

  ratingExecutions.add(executionKey)
  try {
    if (authorityScope !== getCurrentAuthorityScope()) {
      throw new Error('Rating actor authority changed')
    }

    const preflightRequest = await revalidate()
    const preflightEligibility = deriveEligibility(preflightRequest, actorUserId)
    if (!preflightEligibility?.canRate || preflightEligibility.requestId !== requestId) {
      throw new Error('Current rating eligibility is unavailable')
    }

    const submissionAuthority = deriveServiceReviewSubmissionAuthority(review, preflightRequest)
    if (!submissionAuthority) {
      throw new Error('Invalid rating submission')
    }

    if (authorityScope !== getCurrentAuthorityScope()) {
      throw new Error('Rating actor authority changed')
    }

    let mutationResponse: ServiceReviewCreationAuthority
    try {
      mutationResponse = await mutate(submissionAuthority)
    } catch (error) {
      await revalidate().catch(() => null)
      throw error
    }

    if (authorityScope !== getCurrentAuthorityScope()) {
      throw new Error('Rating actor authority changed')
    }

    const creationAuthority = deriveServiceReviewCreationAuthority(
      mutationResponse,
      submissionAuthority,
      preflightRequest,
      actorUserId,
    )
    if (!creationAuthority) {
      throw new Error('Invalid rating creation response')
    }

    const settledRequest = await revalidate()
    const settledEligibility = deriveEligibility(settledRequest, actorUserId)
    if (
      authorityScope !== getCurrentAuthorityScope()
      || !settledEligibility
      || settledEligibility.canRate
      || settledEligibility.existingReviewId !== normalizedGuid(creationAuthority.reviewId)
    ) {
      throw new Error('Rating authority revalidation failed')
    }

    return creationAuthority
  } finally {
    ratingExecutions.delete(executionKey)
  }
}

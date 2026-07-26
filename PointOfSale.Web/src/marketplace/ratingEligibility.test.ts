import { describe, expect, it } from 'vitest'
import { deriveServiceRequestRatingEligibility } from './ratingEligibility'
import { deriveServiceRequestLifecyclePresentation } from './status'
import type { ServiceRequest } from './types'

const requestId = '11111111-1111-4111-8111-111111111111'
const serviceId = '22222222-2222-4222-8222-222222222222'
const buyerUserId = '33333333-3333-4333-8333-333333333333'
const providerUserId = '44444444-4444-4444-8444-444444444444'
const reviewId = '55555555-5555-4555-8555-555555555555'

const request = (overrides: Partial<ServiceRequest> = {}): ServiceRequest => ({
  requestId,
  serviceId,
  buyerUserId,
  providerUserId,
  status: 'CompletedByBuyer',
  createdAt: '2026-06-15T00:00:00.000Z',
  canChat: false,
  canViewChatHistory: true,
  canSendChat: false,
  canConfirmCompletion: false,
  canCancel: false,
  canReview: true,
  canAccept: false,
  canReject: false,
  canMarkCompleted: false,
  existingReviewId: null,
  ...overrides,
})

const eligibility = (value: ServiceRequest, actorId: unknown = buyerUserId) => {
  const lifecycle = deriveServiceRequestLifecyclePresentation(value)
  return deriveServiceRequestRatingEligibility(value, lifecycle, actorId)
}

describe('service marketplace frontend rating eligibility authority', () => {
  it('grants derivative presentation only to the current buyer after final completion', () => {
    expect(eligibility(request())).toEqual({
      requestId,
      serviceId,
      buyerUserId,
      providerUserId,
      canRate: true,
      existingReviewId: null,
    })
  })

  it('rejects providers, foreign actors, self-rating, and malformed identity', () => {
    expect(eligibility(request(), providerUserId)).toBeNull()
    expect(eligibility(request(), reviewId)).toBeNull()
    expect(eligibility(request({ providerUserId: buyerUserId }))).toBeNull()
    expect(eligibility(request({ buyerUserId: 'not-a-guid' }))).toBeNull()
  })

  it.each(['Pending', 'Accepted', 'CompletedByProvider', 'Cancelled', 'Rejected'])(
    'rejects rating capability in %s state',
    (status) => {
      expect(eligibility(request({ status }))).toBeNull()
    },
  )

  it('represents already-rated authority without exposing another action', () => {
    expect(eligibility(request({
      canReview: false,
      existingReviewId: reviewId,
    }))).toEqual({
      requestId,
      serviceId,
      buyerUserId,
      providerUserId,
      canRate: false,
      existingReviewId: reviewId,
    })
  })

  it('fails closed for missing, contradictory, or malformed capability DTOs', () => {
    expect(eligibility(request({ canReview: false, existingReviewId: null }))).toBeNull()
    expect(eligibility(request({ canReview: true, existingReviewId: reviewId }))).toBeNull()
    expect(eligibility(request({ canReview: false, existingReviewId: 'invalid' }))).toBeNull()
  })
})

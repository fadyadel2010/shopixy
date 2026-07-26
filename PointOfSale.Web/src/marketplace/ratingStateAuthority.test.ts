import { describe, expect, it } from 'vitest'
import { deriveServiceRequestRatingStatePresentation } from './ratingStateAuthority'
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
  completionEvidence: {
    requestId,
    status: 'CompletedByBuyer',
    providerCompletion: {
      evidenceId: '66666666-6666-4666-8666-666666666666',
      requestId,
      evidenceType: 'ProviderCompletion',
      occurredAt: '2026-06-14T10:00:00.000Z',
      actorRole: 'Provider',
      actorUserId: providerUserId,
    },
    buyerConfirmation: {
      evidenceId: '77777777-7777-4777-8777-777777777777',
      requestId,
      evidenceType: 'BuyerConfirmation',
      occurredAt: '2026-06-14T11:00:00.000Z',
      actorRole: 'Buyer',
      actorUserId: buyerUserId,
    },
    notes: [],
    attachments: [],
    deliverables: [],
  },
  createdAt: '2026-06-14T09:00:00.000Z',
  canChat: false,
  canViewChatHistory: true,
  canSendChat: false,
  canConfirmCompletion: false,
  canCancel: false,
  canReview: true,
  existingReviewId: null,
  canAccept: false,
  canReject: false,
  canMarkCompleted: false,
  ...overrides,
})

const presentation = (
  value: ServiceRequest,
  actorUserId: unknown = buyerUserId,
) => deriveServiceRequestRatingStatePresentation(
  value,
  deriveServiceRequestLifecyclePresentation(value),
  actorUserId,
)

describe('service marketplace frontend rating state authority', () => {
  it('derives the only valid unrated state from current buyer capability', () => {
    expect(presentation(request())).toMatchObject({
      state: 'unrated',
      canRate: true,
      reviewId: null,
    })
  })

  it('derives the only valid rated state from backend review identity', () => {
    expect(presentation(request({
      canReview: false,
      existingReviewId: reviewId,
    }))).toMatchObject({
      state: 'rated',
      canRate: false,
      reviewId,
    })
  })

  it('rejects contradictory, missing, malformed, and pre-completion rating state', () => {
    expect(presentation(request({ canReview: false, existingReviewId: null }))).toBeNull()
    expect(presentation(request({ canReview: true, existingReviewId: reviewId }))).toBeNull()
    expect(presentation(request({ canReview: false, existingReviewId: 'invalid' }))).toBeNull()
    expect(presentation(request({ canReview: undefined as unknown as boolean }))).toBeNull()
    expect(presentation(request({ status: 'CompletedByProvider', canReview: false }))).toBeNull()
  })

  it('withholds review ownership from providers, foreign actors, and self-owned requests', () => {
    expect(presentation(request(), providerUserId)).toBeNull()
    expect(presentation(request(), reviewId)).toBeNull()
    expect(presentation(request({ providerUserId: buyerUserId }))).toBeNull()
  })
})

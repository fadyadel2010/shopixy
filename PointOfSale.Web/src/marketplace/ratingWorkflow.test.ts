import { describe, expect, it, vi } from 'vitest'
import {
  deriveServiceReviewSubmissionAuthority,
  deriveServiceReviewCreationAuthority,
  executeServiceRequestRatingWorkflow,
} from './ratingWorkflow'
import type {
  CreateReviewRequest,
  ServiceRequestDetails,
  ServiceReviewCreationAuthority,
} from './types'

const requestId = '11111111-1111-4111-8111-111111111111'
const serviceId = '22222222-2222-4222-8222-222222222222'
const buyerUserId = '33333333-3333-4333-8333-333333333333'
const providerUserId = '44444444-4444-4444-8444-444444444444'
const reviewId = '55555555-5555-4555-8555-555555555555'
const scope = 'tenant:user'

const review: CreateReviewRequest = {
  serviceRequestId: requestId,
  rating: 5,
  comment: 'Excellent',
}

const details = (
  canReview: boolean,
  existingReviewId: string | null,
): ServiceRequestDetails => ({
  requestId,
  serviceId,
  buyerUserId,
  providerUserId,
  status: 'CompletedByBuyer',
  createdAt: '2026-06-15T00:00:00.000Z',
  completionEvidence: {
    requestId,
    status: 'CompletedByBuyer',
    providerCompletion: {
      evidenceId: `${requestId}:provider-completion`,
      requestId,
      evidenceType: 'ProviderCompletion',
      occurredAt: '2026-06-15T01:00:00.000Z',
      actorRole: 'Provider',
      actorUserId: providerUserId,
    },
    buyerConfirmation: {
      evidenceId: `${requestId}:buyer-confirmation`,
      requestId,
      evidenceType: 'BuyerConfirmation',
      occurredAt: '2026-06-15T02:00:00.000Z',
      actorRole: 'Buyer',
      actorUserId: buyerUserId,
    },
    notes: [],
    attachments: [],
    deliverables: [],
  },
  capabilities: {
    canAccept: false,
    canReject: false,
    canMarkCompleted: false,
    canConfirmCompletion: false,
    canCancel: false,
    canChat: false,
    canViewChatHistory: true,
    canSendChat: false,
    canReview,
    existingReviewId,
  },
})

const response: ServiceReviewCreationAuthority = {
  reviewId,
  serviceRequestId: requestId,
  serviceId,
  reviewerUserId: buyerUserId,
  providerUserId,
  rating: 5,
  createdAt: '2026-06-15T03:00:00.000Z',
}

describe('service marketplace frontend rating workflow authority', () => {
  it('validates backend-issued review identity and participant lineage', () => {
    expect(deriveServiceReviewCreationAuthority(response, review, details(true, null), buyerUserId))
      .toEqual(response)
    expect(deriveServiceReviewCreationAuthority(
      { ...response, reviewerUserId: providerUserId },
      review,
      details(true, null),
      buyerUserId,
    )).toBeNull()
    expect(deriveServiceReviewCreationAuthority(
      { ...response, serviceRequestId: reviewId },
      review,
      details(true, null),
      buyerUserId,
    )).toBeNull()
  })

  it('binds the submitted payload to the freshly revalidated request category', () => {
    expect(deriveServiceReviewSubmissionAuthority(review, details(true, null))).toEqual(review)
    expect(deriveServiceReviewSubmissionAuthority(
      { ...review, comment: 'x'.repeat(5001) },
      details(true, null),
    )).toBeNull()

    const marketingRequest = { ...details(true, null), serviceCategory: 'Marketing' }
    expect(deriveServiceReviewSubmissionAuthority(review, marketingRequest)).toBeNull()
    expect(deriveServiceReviewSubmissionAuthority({
      ...review,
      evidence: {
        campaignName: ' Launch ',
        platform: 'Google',
        startDate: '2026-06-01T00:00:00.000Z',
        endDate: '2026-06-10T00:00:00.000Z',
      },
    }, marketingRequest)).toEqual({
      ...review,
      evidence: {
        campaignName: 'Launch',
        platform: 'Google',
        startDate: '2026-06-01T00:00:00.000Z',
        endDate: '2026-06-10T00:00:00.000Z',
      },
    })
  })

  it('requires fresh eligibility before submit and fresh duplicate revocation after submit', async () => {
    const revalidate = vi.fn()
      .mockResolvedValueOnce(details(true, null))
      .mockResolvedValueOnce(details(false, reviewId))
    const mutate = vi.fn().mockResolvedValue(response)

    await expect(executeServiceRequestRatingWorkflow({
      review,
      authorityScope: scope,
      actorUserId: buyerUserId,
      revalidate,
      mutate,
      getCurrentAuthorityScope: () => scope,
    })).resolves.toEqual(response)

    expect(revalidate).toHaveBeenCalledTimes(2)
    expect(mutate).toHaveBeenCalledTimes(1)
    expect(mutate).toHaveBeenCalledWith(review)
  })

  it('rejects stale capabilities before mutation', async () => {
    const mutate = vi.fn().mockResolvedValue(response)

    await expect(executeServiceRequestRatingWorkflow({
      review,
      authorityScope: scope,
      actorUserId: buyerUserId,
      revalidate: vi.fn().mockResolvedValue(details(false, reviewId)),
      mutate,
      getCurrentAuthorityScope: () => scope,
    })).rejects.toThrow('Current rating eligibility is unavailable')

    expect(mutate).not.toHaveBeenCalled()
  })

  it('rejects actor or tenant replacement across network boundaries', async () => {
    let currentScope = scope
    const revalidate = vi.fn().mockImplementation(async () => {
      currentScope = 'replacement:actor'
      return details(true, null)
    })

    await expect(executeServiceRequestRatingWorkflow({
      review,
      authorityScope: scope,
      actorUserId: buyerUserId,
      revalidate,
      mutate: vi.fn().mockResolvedValue(response),
      getCurrentAuthorityScope: () => currentScope,
    })).rejects.toThrow('Rating actor authority changed')
  })

  it('rejects malformed success and failure to revoke eligibility', async () => {
    await expect(executeServiceRequestRatingWorkflow({
      review,
      authorityScope: scope,
      actorUserId: buyerUserId,
      revalidate: vi.fn()
        .mockResolvedValueOnce(details(true, null))
        .mockResolvedValueOnce(details(true, null)),
      mutate: vi.fn().mockResolvedValue({ ...response, rating: 4 }),
      getCurrentAuthorityScope: () => scope,
    })).rejects.toThrow('Invalid rating creation response')
  })

  it('treats timeout-after-commit as ambiguous failure and revalidates without inventing success', async () => {
    const timeout = new Error('timeout')
    const revalidate = vi.fn()
      .mockResolvedValueOnce(details(true, null))
      .mockResolvedValueOnce(details(false, reviewId))

    await expect(executeServiceRequestRatingWorkflow({
      review,
      authorityScope: scope,
      actorUserId: buyerUserId,
      revalidate,
      mutate: vi.fn().mockRejectedValue(timeout),
      getCurrentAuthorityScope: () => scope,
    })).rejects.toBe(timeout)

    expect(revalidate).toHaveBeenCalledTimes(2)
  })

  it('synchronously fences concurrent submissions for the same actor and request', async () => {
    let releaseMutation!: (value: ServiceReviewCreationAuthority) => void
    const mutation = new Promise<ServiceReviewCreationAuthority>((resolve) => {
      releaseMutation = resolve
    })
    const first = executeServiceRequestRatingWorkflow({
      review,
      authorityScope: scope,
      actorUserId: buyerUserId,
      revalidate: vi.fn()
        .mockResolvedValueOnce(details(true, null))
        .mockResolvedValueOnce(details(false, reviewId)),
      mutate: vi.fn().mockReturnValue(mutation),
      getCurrentAuthorityScope: () => scope,
    })

    await Promise.resolve()

    await expect(executeServiceRequestRatingWorkflow({
      review,
      authorityScope: scope,
      actorUserId: buyerUserId,
      revalidate: vi.fn().mockResolvedValue(details(true, null)),
      mutate: vi.fn().mockResolvedValue(response),
      getCurrentAuthorityScope: () => scope,
    })).rejects.toThrow('Rating workflow already in progress')

    releaseMutation(response)
    await expect(first).resolves.toEqual(response)
  })
})

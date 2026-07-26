import { describe, expect, it } from 'vitest'
import { deriveServiceReviewHistoryPresentation } from './ratingTimelineAuthority'
import type { ServiceReview, ServiceReviewHistory } from './types'

const serviceId = 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0'
const providerProfileId = '00a12dc1-561f-4406-8088-1ff76375ef25'
const providerUserId = 'f2c6b9af-57fb-4fb0-9ca7-83363f078fdd'

const review = (overrides: Partial<ServiceReview> = {}): ServiceReview => ({
  reviewId: 'ce210a55-f82c-442a-b4e2-4cc9188640e5',
  serviceRequestId: '69c9e180-5907-49d3-9096-4207fd96ff2a',
  serviceId,
  reviewerUserId: '105b530e-8e3d-4c69-9c72-0d395da91f4d',
  providerUserId,
  rating: 5,
  comment: 'Current review',
  createdAt: '2026-06-14T00:00:00Z',
  updatedAt: '2026-06-14T00:00:00Z',
  buyerName: 'Current Buyer',
  marketingEvidence: null,
  ...overrides,
})

const history = (
  items: readonly ServiceReview[],
  overrides: Partial<ServiceReviewHistory> = {},
): ServiceReviewHistory => ({
  serviceId,
  providerProfileId,
  providerUserId,
  totalReviews: items.length,
  items,
  ...overrides,
})

describe('service marketplace frontend rating and reputation timeline authority', () => {
  it('accepts backend order without sorting, repairing, or deriving audit identity', () => {
    const current = review()
    const older = review({
      reviewId: '4b685e31-5508-4a48-a6cf-2c987fd836a6',
      serviceRequestId: 'c9de04ff-5c96-4619-ae4d-e8df5d6570ae',
      reviewerUserId: '61290ac1-5f5a-4d82-aa62-bb0bbf017f3b',
      buyerName: 'Earlier Buyer',
      createdAt: '2026-06-13T00:00:00Z',
      updatedAt: '2026-06-13T00:00:00Z',
    })

    const presentation = deriveServiceReviewHistoryPresentation(
      history([current, older]),
      serviceId,
      providerProfileId,
    )

    expect(presentation?.items.map((item) => item.reviewId)).toEqual([
      current.reviewId,
      older.reviewId,
    ])
    expect(presentation?.totalReviews).toBe(2)
  })

  it('rejects reordered and nondeterministic equal-time chronology', () => {
    const earlierId = review({
      reviewId: '4b685e31-5508-4a48-a6cf-2c987fd836a6',
      serviceRequestId: 'c9de04ff-5c96-4619-ae4d-e8df5d6570ae',
    })

    expect(deriveServiceReviewHistoryPresentation(
      history([earlierId, review()]),
      serviceId,
      providerProfileId,
    )).toBeNull()

    expect(deriveServiceReviewHistoryPresentation(
      history([
        review({
          createdAt: '2026-06-13T00:00:00Z',
          updatedAt: '2026-06-13T00:00:00Z',
        }),
        earlierId,
      ]),
      serviceId,
      providerProfileId,
    )).toBeNull()
  })

  it('rejects duplicate, omitted, foreign, and attribution-drifted review history', () => {
    const current = review()

    expect(deriveServiceReviewHistoryPresentation(
      history([current, current]),
      serviceId,
      providerProfileId,
    )).toBeNull()
    expect(deriveServiceReviewHistoryPresentation(
      history([current], { totalReviews: 2 }),
      serviceId,
      providerProfileId,
    )).toBeNull()
    expect(deriveServiceReviewHistoryPresentation(
      history([review({ serviceId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' })]),
      serviceId,
      providerProfileId,
    )).toBeNull()
    expect(deriveServiceReviewHistoryPresentation(
      history([review({ providerUserId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb' })]),
      serviceId,
      providerProfileId,
    )).toBeNull()
    expect(deriveServiceReviewHistoryPresentation(
      history([review({ reviewerUserId: 'invalid' })]),
      serviceId,
      providerProfileId,
    )).toBeNull()
  })

  it('rejects mutable, malformed, and replayed timestamp metadata', () => {
    expect(deriveServiceReviewHistoryPresentation(
      history([review({ updatedAt: '2026-06-14T00:00:01Z' })]),
      serviceId,
      providerProfileId,
    )).toBeNull()
    expect(deriveServiceReviewHistoryPresentation(
      history([review({ createdAt: 'June 14, 2026', updatedAt: 'June 14, 2026' })]),
      serviceId,
      providerProfileId,
    )).toBeNull()
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../core/superAdminApiClient', () => ({
    superAdminApiClient: {
        get: vi.fn(),
        post: vi.fn(),
    },
}))

import { superAdminApiClient } from '../core/superAdminApiClient'
import {
    approveSuperAdminReview,
    getSuperAdminReviewDetails,
    getSuperAdminReviews,
    rejectSuperAdminReview,
    type SuperAdminReviewDetails,
    type SuperAdminReviewsPage,
} from './reviewsService'

const mockedGet = vi.mocked(superAdminApiClient.get)
const mockedPost = vi.mocked(superAdminApiClient.post)

describe('reviewsService', () => {
    beforeEach(() => {
        mockedGet.mockReset()
        mockedPost.mockReset()
    })

    it('builds paginated moderation queries with filters', async () => {
        const expectedPage: SuperAdminReviewsPage = {
            items: [],
            page: 2,
            pageSize: 25,
            totalCount: 12,
            totalPages: 1,
        }

        mockedGet.mockResolvedValue({ data: expectedPage })

        const result = await getSuperAdminReviews({
            page: 2,
            pageSize: 25,
            status: 'Pending',
            rating: 5,
            productId: '  product-123  ',
        })

        expect(mockedGet).toHaveBeenCalledWith('/reviews?page=2&pageSize=25&status=Pending&rating=5&productId=product-123')
        expect(result).toEqual(expectedPage)
    })

    it('loads review details by id', async () => {
        const expectedDetails = createReviewDetails()

        mockedGet.mockResolvedValue({ data: expectedDetails })

        const result = await getSuperAdminReviewDetails(expectedDetails.id)

        expect(mockedGet).toHaveBeenCalledWith(`/reviews/${expectedDetails.id}`)
        expect(result).toEqual(expectedDetails)
    })

    it('approves a review with a trimmed moderation reason', async () => {
        const expectedDetails = createReviewDetails()

        mockedPost.mockResolvedValue({ data: expectedDetails })

        const result = await approveSuperAdminReview(expectedDetails.id, '  Looks legitimate  ')

        expect(mockedPost).toHaveBeenCalledWith(`/reviews/${expectedDetails.id}/approve`, {
            reason: 'Looks legitimate',
        })
        expect(result).toEqual(expectedDetails)
    })

    it('rejects a review without sending an empty reason payload value', async () => {
        const expectedDetails = createReviewDetails()

        mockedPost.mockResolvedValue({ data: expectedDetails })

        const result = await rejectSuperAdminReview(expectedDetails.id, '   ')

        expect(mockedPost).toHaveBeenCalledWith(`/reviews/${expectedDetails.id}/reject`, {
            reason: undefined,
        })
        expect(result).toEqual(expectedDetails)
    })
})

function createReviewDetails(): SuperAdminReviewDetails {
    return {
        id: 'review-1',
        productId: 'product-1',
        productName: 'Marketplace Espresso Machine',
        userId: 'user-1',
        customerName: 'Amina Salah',
        customerEmail: 'amina@example.com',
        rating: 5,
        title: 'Excellent',
        comment: 'Arrived quickly and works perfectly.',
        isVerifiedPurchase: true,
        status: 'Pending',
        createdAtUtc: '2026-03-31T10:00:00Z',
        updatedAtUtc: '2026-03-31T10:05:00Z',
        userHistory: {
            userId: 'user-1',
            customerName: 'Amina Salah',
            customerEmail: 'amina@example.com',
            totalReviews: 3,
            approvedReviews: 2,
            pendingReviews: 1,
            rejectedReviews: 0,
            recentReviews: [],
        },
    }
}

import { superAdminApiClient } from '../core/superAdminApiClient'

export type SuperAdminReviewStatus = 'Pending' | 'Approved' | 'Rejected'

export interface SuperAdminReviewsFilters {
    page: number
    pageSize: number
    status?: SuperAdminReviewStatus | ''
    rating?: number | ''
    productId?: string
}

export interface SuperAdminReviewListItem {
    id: string
    productId: string
    productName: string
    userId: string
    customerName: string
    customerEmail: string
    rating: number
    title?: string | null
    comment: string
    isVerifiedPurchase: boolean
    status: SuperAdminReviewStatus
    createdAtUtc: string
    updatedAtUtc?: string | null
}

export interface SuperAdminReviewUserHistoryEntry {
    reviewId: string
    productId: string
    productName: string
    rating: number
    status: SuperAdminReviewStatus
    isVerifiedPurchase: boolean
    createdAtUtc: string
    updatedAtUtc?: string | null
}

export interface SuperAdminReviewUserHistory {
    userId: string
    customerName: string
    customerEmail: string
    totalReviews: number
    approvedReviews: number
    pendingReviews: number
    rejectedReviews: number
    recentReviews: SuperAdminReviewUserHistoryEntry[]
}

export interface SuperAdminReviewDetails {
    id: string
    productId: string
    productName: string
    userId: string
    customerName: string
    customerEmail: string
    rating: number
    title?: string | null
    comment: string
    isVerifiedPurchase: boolean
    status: SuperAdminReviewStatus
    createdAtUtc: string
    updatedAtUtc?: string | null
    userHistory: SuperAdminReviewUserHistory
}

export interface SuperAdminReviewsPage {
    items: SuperAdminReviewListItem[]
    page: number
    pageSize: number
    totalCount: number
    totalPages: number
}

export const SUPER_ADMIN_REVIEW_STATUS_OPTIONS: SuperAdminReviewStatus[] = ['Pending', 'Approved', 'Rejected']

function buildReviewQueryParams(filters: SuperAdminReviewsFilters): URLSearchParams {
    const params = new URLSearchParams()

    params.set('page', String(filters.page))
    params.set('pageSize', String(filters.pageSize))

    if (filters.status) {
        params.set('status', filters.status)
    }

    if (typeof filters.rating === 'number' && Number.isFinite(filters.rating)) {
        params.set('rating', String(filters.rating))
    }

    if (filters.productId?.trim()) {
        params.set('productId', filters.productId.trim())
    }

    return params
}

export async function getSuperAdminReviews(filters: SuperAdminReviewsFilters): Promise<SuperAdminReviewsPage> {
    const params = buildReviewQueryParams(filters)
    const response = await superAdminApiClient.get<SuperAdminReviewsPage>(`/reviews?${params.toString()}`)
    return response.data
}

export async function getSuperAdminReviewDetails(reviewId: string): Promise<SuperAdminReviewDetails> {
    const response = await superAdminApiClient.get<SuperAdminReviewDetails>(`/reviews/${reviewId}`)
    return response.data
}

export async function approveSuperAdminReview(reviewId: string, reason?: string): Promise<SuperAdminReviewDetails> {
    const response = await superAdminApiClient.post<SuperAdminReviewDetails, { reason?: string }>(
        `/reviews/${reviewId}/approve`,
        { reason: reason?.trim() || undefined },
    )

    return response.data
}

export async function rejectSuperAdminReview(reviewId: string, reason?: string): Promise<SuperAdminReviewDetails> {
    const response = await superAdminApiClient.post<SuperAdminReviewDetails, { reason?: string }>(
        `/reviews/${reviewId}/reject`,
        { reason: reason?.trim() || undefined },
    )

    return response.data
}

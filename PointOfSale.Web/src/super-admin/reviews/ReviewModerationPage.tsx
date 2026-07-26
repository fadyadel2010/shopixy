import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
    CheckCircle2,
    Clock3,
    Filter,
    MessageSquareQuote,
    RefreshCw,
    Search,
    ShieldAlert,
    ShieldCheck,
    Star,
    UserRound,
    X,
    XCircle,
} from 'lucide-react'
import { type ReactNode, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'

import {
    approveSuperAdminReview,
    getSuperAdminReviewDetails,
    getSuperAdminReviews,
    rejectSuperAdminReview,
    SUPER_ADMIN_REVIEW_STATUS_OPTIONS,
    type SuperAdminReviewDetails,
    type SuperAdminReviewListItem,
    type SuperAdminReviewsFilters,
    type SuperAdminReviewStatus,
} from './reviewsService'

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 25
const pageSizeOptions = [25, 50, 100]

function parsePositiveInt(value: string | null, fallback: number): number {
    const parsed = Number.parseInt(value ?? '', 10)
    return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function parseStatus(value: string | null): SuperAdminReviewStatus | '' {
    return SUPER_ADMIN_REVIEW_STATUS_OPTIONS.includes(value as SuperAdminReviewStatus)
        ? (value as SuperAdminReviewStatus)
        : ''
}

function parseRating(value: string | null): number | '' {
    const parsed = Number.parseInt(value ?? '', 10)
    return Number.isFinite(parsed) && parsed >= 1 && parsed <= 5 ? parsed : ''
}

function buildFilters(searchParams: URLSearchParams): SuperAdminReviewsFilters {
    return {
        page: parsePositiveInt(searchParams.get('page'), DEFAULT_PAGE),
        pageSize: parsePositiveInt(searchParams.get('pageSize'), DEFAULT_PAGE_SIZE),
        status: parseStatus(searchParams.get('status')),
        rating: parseRating(searchParams.get('rating')),
        productId: searchParams.get('productId') ?? '',
    }
}

function formatTimestamp(value?: string | null): string {
    if (!value) {
        return 'Awaiting sync'
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return 'Awaiting sync'
    }

    return parsed.toLocaleString()
}

function getErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof Error && error.message) {
        return error.message
    }

    return fallback
}

function getStatusClasses(status: SuperAdminReviewStatus): string {
    switch (status) {
        case 'Approved':
            return 'border-emerald-200 bg-emerald-50 text-emerald-900'
        case 'Rejected':
            return 'border-rose-200 bg-rose-50 text-rose-900'
        default:
            return 'border-amber-200 bg-amber-50 text-amber-900'
    }
}

function StatCard({
    label,
    value,
    icon,
    tone,
}: {
    label: string
    value: string
    icon: ReactNode
    tone: string
}) {
    return (
        <div className={`rounded-[28px] border px-5 py-4 shadow-sm ${tone}`}>
            <div className="flex items-center justify-between gap-3">
                <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-75">{label}</p>
                <div className="rounded-2xl bg-white/70 p-2">{icon}</div>
            </div>
            <p className="mt-4 text-3xl font-semibold">{value}</p>
        </div>
    )
}

function ReviewActionButton({
    tone,
    disabled,
    onClick,
    children,
}: {
    tone: 'approve' | 'reject'
    disabled?: boolean
    onClick: () => void
    children: ReactNode
}) {
    return (
        <button
            type="button"
            disabled={disabled}
            onClick={onClick}
            className={[
                'inline-flex items-center justify-center rounded-2xl px-3 py-2 text-xs font-semibold transition disabled:cursor-not-allowed disabled:opacity-60',
                tone === 'approve'
                    ? 'bg-emerald-600 text-white hover:bg-emerald-700'
                    : 'bg-rose-600 text-white hover:bg-rose-700',
            ].join(' ')}
        >
            {children}
        </button>
    )
}

function ReviewsTable({
    reviews,
    isLoading,
    onSelectReview,
    onApprove,
    onReject,
    actionDisabled,
}: {
    reviews: SuperAdminReviewListItem[]
    isLoading: boolean
    onSelectReview: (reviewId: string) => void
    onApprove: (reviewId: string) => void
    onReject: (reviewId: string) => void
    actionDisabled: boolean
}) {
    if (isLoading) {
        return (
            <div className="space-y-3">
                {Array.from({ length: 6 }).map((_, index) => (
                    <div key={index} className="h-20 animate-pulse rounded-[24px] bg-slate-200/80" />
                ))}
            </div>
        )
    }

    if (reviews.length === 0) {
        return (
            <div className="rounded-[32px] border border-dashed border-slate-300 bg-white px-6 py-14 text-center">
                <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100 text-slate-500">
                    <MessageSquareQuote className="h-6 w-6" />
                </div>
                <h3 className="mt-5 text-2xl font-semibold text-slate-950">No reviews match these filters</h3>
                <p className="mt-3 text-sm leading-6 text-slate-500">
                    Try widening the moderation queue filters to bring more marketplace reviews into view.
                </p>
            </div>
        )
    }

    return (
        <div className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
            <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-slate-200">
                    <thead className="bg-slate-50">
                        <tr className="text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                            <th className="px-5 py-4">Product</th>
                            <th className="px-5 py-4">User</th>
                            <th className="px-5 py-4">Rating</th>
                            <th className="px-5 py-4">Status</th>
                            <th className="px-5 py-4">Created</th>
                            <th className="px-5 py-4 text-right">Actions</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                        {reviews.map((review) => (
                            <tr key={review.id} className="align-top">
                                <td className="px-5 py-4">
                                    <button type="button" className="space-y-1 text-left" onClick={() => onSelectReview(review.id)}>
                                        <p className="font-semibold text-slate-950">{review.productName}</p>
                                        <p className="text-xs text-slate-500">{review.productId}</p>
                                    </button>
                                </td>
                                <td className="px-5 py-4">
                                    <div className="space-y-1">
                                        <p className="font-semibold text-slate-950">{review.customerName}</p>
                                        <p className="text-sm text-slate-500">{review.customerEmail}</p>
                                    </div>
                                </td>
                                <td className="px-5 py-4">
                                    <div className="inline-flex items-center gap-2 rounded-full bg-amber-50 px-3 py-2 text-sm font-semibold text-amber-900">
                                        <Star className="h-4 w-4 fill-current" />
                                        {review.rating}.0
                                    </div>
                                </td>
                                <td className="px-5 py-4">
                                    <div className={`inline-flex rounded-full border px-3 py-2 text-sm font-semibold ${getStatusClasses(review.status)}`}>
                                        {review.status}
                                    </div>
                                </td>
                                <td className="px-5 py-4 text-sm text-slate-600">{formatTimestamp(review.createdAtUtc)}</td>
                                <td className="px-5 py-4">
                                    <div className="flex justify-end gap-2">
                                        <button
                                            type="button"
                                            className="rounded-2xl border border-slate-200 px-3 py-2 text-xs font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                            onClick={() => onSelectReview(review.id)}
                                        >
                                            Details
                                        </button>
                                        <ReviewActionButton tone="approve" disabled={actionDisabled} onClick={() => onApprove(review.id)}>
                                            Approve
                                        </ReviewActionButton>
                                        <ReviewActionButton tone="reject" disabled={actionDisabled} onClick={() => onReject(review.id)}>
                                            Reject
                                        </ReviewActionButton>
                                    </div>
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    )
}

function ReviewDetailsDrawer({
    isOpen,
    details,
    isLoading,
    isError,
    errorMessage,
    moderationNote,
    onModerationNoteChange,
    onClose,
    onRetry,
    onApprove,
    onReject,
    isMutating,
}: {
    isOpen: boolean
    details?: SuperAdminReviewDetails
    isLoading: boolean
    isError: boolean
    errorMessage?: string | null
    moderationNote: string
    onModerationNoteChange: (value: string) => void
    onClose: () => void
    onRetry: () => void
    onApprove: () => void
    onReject: () => void
    isMutating: boolean
}) {
    if (!isOpen) {
        return null
    }

    return (
        <div className="fixed inset-0 z-50">
            <button
                type="button"
                className="absolute inset-0 bg-slate-950/45 backdrop-blur-sm"
                aria-label="Close review details"
                onClick={onClose}
            />

            <aside className="absolute inset-y-0 right-0 flex w-full max-w-3xl flex-col border-l border-white/20 bg-white shadow-2xl shadow-slate-950/40">
                <div className="flex items-start justify-between gap-4 border-b border-slate-200 px-6 py-5">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-emerald-600">Review details</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">Moderation workspace</h2>
                    </div>

                    <button
                        type="button"
                        className="flex h-11 w-11 items-center justify-center rounded-2xl border border-slate-200 bg-white text-slate-700"
                        onClick={onClose}
                    >
                        <X className="h-5 w-5" />
                    </button>
                </div>

                <div className="flex-1 overflow-y-auto px-6 py-6">
                    {isLoading ? (
                        <div className="space-y-4">
                            {Array.from({ length: 6 }).map((_, index) => (
                                <div key={index} className="h-20 animate-pulse rounded-[24px] bg-slate-200/80" />
                            ))}
                        </div>
                    ) : isError ? (
                        <div className="rounded-[28px] border border-rose-200 bg-rose-50 px-5 py-5">
                            <p className="text-base font-semibold text-rose-950">Unable to load review details</p>
                            <p className="mt-2 text-sm leading-6 text-rose-800">{errorMessage}</p>
                            <button
                                type="button"
                                className="mt-4 inline-flex items-center gap-2 rounded-2xl bg-rose-600 px-4 py-3 text-sm font-semibold text-white"
                                onClick={onRetry}
                            >
                                <RefreshCw className="h-4 w-4" />
                                Retry
                            </button>
                        </div>
                    ) : details ? (
                        <div className="space-y-6">
                            <section className="rounded-[32px] border border-slate-200 bg-slate-50 px-5 py-5">
                                <div className="flex flex-wrap items-start justify-between gap-4">
                                    <div>
                                        <div className={`inline-flex rounded-full border px-3 py-2 text-sm font-semibold ${getStatusClasses(details.status)}`}>
                                            {details.status}
                                        </div>
                                        <h3 className="mt-4 text-2xl font-semibold text-slate-950">{details.productName}</h3>
                                        <p className="mt-2 text-sm text-slate-500">Product ID: {details.productId}</p>
                                    </div>

                                    <div className="rounded-[24px] border border-amber-200 bg-amber-50 px-4 py-4 text-center text-amber-950">
                                        <div className="flex items-center justify-center gap-2">
                                            <Star className="h-5 w-5 fill-current" />
                                            <span className="text-3xl font-semibold">{details.rating}.0</span>
                                        </div>
                                        <p className="mt-2 text-xs font-semibold uppercase tracking-[0.18em] text-amber-800">Customer rating</p>
                                    </div>
                                </div>
                            </section>

                            <section className="grid gap-4 lg:grid-cols-2">
                                <div className="rounded-[28px] border border-slate-200 bg-white px-5 py-5 shadow-sm">
                                    <div className="flex items-center gap-3">
                                        <div className="rounded-2xl bg-slate-100 p-3 text-slate-700">
                                            <UserRound className="h-5 w-5" />
                                        </div>
                                        <div>
                                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Customer</p>
                                            <p className="mt-1 text-lg font-semibold text-slate-950">{details.customerName}</p>
                                            <p className="text-sm text-slate-500">{details.customerEmail}</p>
                                        </div>
                                    </div>

                                    <div className="mt-5 flex flex-wrap gap-2">
                                        {details.isVerifiedPurchase ? (
                                            <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm font-semibold text-emerald-900">
                                                <ShieldCheck className="h-4 w-4" />
                                                Verified Purchase
                                            </div>
                                        ) : null}
                                        <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-2 text-sm font-semibold text-slate-700">
                                            <Clock3 className="h-4 w-4" />
                                            Created {formatTimestamp(details.createdAtUtc)}
                                        </div>
                                    </div>
                                </div>

                                <div className="rounded-[28px] border border-slate-200 bg-white px-5 py-5 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Customer history</p>
                                    <div className="mt-5 grid grid-cols-2 gap-3">
                                        <div className="rounded-2xl bg-slate-50 px-4 py-4">
                                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Total</p>
                                            <p className="mt-2 text-2xl font-semibold text-slate-950">{details.userHistory.totalReviews}</p>
                                        </div>
                                        <div className="rounded-2xl bg-emerald-50 px-4 py-4">
                                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-emerald-700">Approved</p>
                                            <p className="mt-2 text-2xl font-semibold text-emerald-950">{details.userHistory.approvedReviews}</p>
                                        </div>
                                        <div className="rounded-2xl bg-amber-50 px-4 py-4">
                                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-amber-700">Pending</p>
                                            <p className="mt-2 text-2xl font-semibold text-amber-950">{details.userHistory.pendingReviews}</p>
                                        </div>
                                        <div className="rounded-2xl bg-rose-50 px-4 py-4">
                                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-rose-700">Rejected</p>
                                            <p className="mt-2 text-2xl font-semibold text-rose-950">{details.userHistory.rejectedReviews}</p>
                                        </div>
                                    </div>
                                </div>
                            </section>

                            <section className="rounded-[28px] border border-slate-200 bg-white px-5 py-5 shadow-sm">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Review content</p>
                                {details.title ? <h4 className="mt-4 text-xl font-semibold text-slate-950">{details.title}</h4> : null}
                                <p className="mt-4 whitespace-pre-wrap text-sm leading-7 text-slate-700">{details.comment}</p>
                            </section>

                            <section className="rounded-[28px] border border-slate-200 bg-white px-5 py-5 shadow-sm">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Moderation note</p>
                                <textarea
                                    value={moderationNote}
                                    onChange={(event) => onModerationNoteChange(event.target.value)}
                                    rows={4}
                                    placeholder="Optional reason saved into the moderation audit trail."
                                    className="mt-4 w-full rounded-[24px] border border-slate-200 px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                />

                                <div className="mt-4 flex flex-wrap gap-3">
                                    <button
                                        type="button"
                                        className="inline-flex items-center gap-2 rounded-2xl bg-emerald-600 px-4 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-emerald-700 disabled:cursor-not-allowed disabled:opacity-60"
                                        disabled={isMutating}
                                        onClick={onApprove}
                                    >
                                        <CheckCircle2 className="h-4 w-4" />
                                        Approve review
                                    </button>
                                    <button
                                        type="button"
                                        className="inline-flex items-center gap-2 rounded-2xl bg-rose-600 px-4 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-rose-700 disabled:cursor-not-allowed disabled:opacity-60"
                                        disabled={isMutating}
                                        onClick={onReject}
                                    >
                                        <XCircle className="h-4 w-4" />
                                        Reject review
                                    </button>
                                </div>
                            </section>

                            <section className="rounded-[28px] border border-slate-200 bg-white px-5 py-5 shadow-sm">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Recent review history</p>
                                <div className="mt-4 space-y-3">
                                    {details.userHistory.recentReviews.length === 0 ? (
                                        <div className="rounded-2xl bg-slate-50 px-4 py-4 text-sm text-slate-500">
                                            No historical reviews found for this customer yet.
                                        </div>
                                    ) : (
                                        details.userHistory.recentReviews.map((review) => (
                                            <div
                                                key={review.reviewId}
                                                className="flex flex-col gap-3 rounded-[24px] border border-slate-200 px-4 py-4 sm:flex-row sm:items-center sm:justify-between"
                                            >
                                                <div>
                                                    <p className="font-semibold text-slate-950">{review.productName}</p>
                                                    <p className="mt-1 text-sm text-slate-500">{formatTimestamp(review.createdAtUtc)}</p>
                                                </div>
                                                <div className="flex flex-wrap items-center gap-2">
                                                    <div className="inline-flex items-center gap-2 rounded-full bg-amber-50 px-3 py-2 text-sm font-semibold text-amber-900">
                                                        <Star className="h-4 w-4 fill-current" />
                                                        {review.rating}.0
                                                    </div>
                                                    <div className={`inline-flex rounded-full border px-3 py-2 text-sm font-semibold ${getStatusClasses(review.status)}`}>
                                                        {review.status}
                                                    </div>
                                                </div>
                                            </div>
                                        ))
                                    )}
                                </div>
                            </section>
                        </div>
                    ) : null}
                </div>
            </aside>
        </div>
    )
}

export default function ReviewModerationPage() {
    const [searchParams, setSearchParams] = useSearchParams()
    const queryClient = useQueryClient()
    const filters = useMemo(() => buildFilters(searchParams), [searchParams])
    const [selectedReviewId, setSelectedReviewId] = useState<string | null>(null)
    const [productIdInput, setProductIdInput] = useState(filters.productId ?? '')
    const [moderationNote, setModerationNote] = useState('')

    useEffect(() => {
        setProductIdInput(filters.productId ?? '')
    }, [filters.productId])

    useEffect(() => {
        setModerationNote('')
    }, [selectedReviewId])

    const updateParams = (updates: Record<string, string | null | undefined>) => {
        setSearchParams((previous) => {
            const next = new URLSearchParams(previous)

            Object.entries(updates).forEach(([key, value]) => {
                if (!value) {
                    next.delete(key)
                    return
                }

                next.set(key, value)
            })

            return next
        })
    }

    const reviewsQuery = useQuery({
        queryKey: ['super-admin', 'reviews', filters],
        queryFn: () => getSuperAdminReviews(filters),
        placeholderData: (previous) => previous,
    })

    const detailsQuery = useQuery({
        queryKey: ['super-admin', 'reviews', 'details', selectedReviewId],
        queryFn: () => getSuperAdminReviewDetails(selectedReviewId as string),
        enabled: Boolean(selectedReviewId),
    })

    const approveMutation = useMutation({
        mutationFn: async (reviewId: string) => approveSuperAdminReview(reviewId, moderationNote),
        onSuccess: async (result) => {
            queryClient.setQueryData(['super-admin', 'reviews', 'details', result.id], result)
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'reviews'] })
        },
    })

    const rejectMutation = useMutation({
        mutationFn: async (reviewId: string) => rejectSuperAdminReview(reviewId, moderationNote),
        onSuccess: async (result) => {
            queryClient.setQueryData(['super-admin', 'reviews', 'details', result.id], result)
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'reviews'] })
        },
    })

    const reviews = reviewsQuery.data?.items ?? []
    const pendingCount = reviews.filter((review) => review.status === 'Pending').length
    const approvedCount = reviews.filter((review) => review.status === 'Approved').length
    const rejectedCount = reviews.filter((review) => review.status === 'Rejected').length
    const verifiedCount = reviews.filter((review) => review.isVerifiedPurchase).length
    const isMutating = approveMutation.isPending || rejectMutation.isPending

    if (reviewsQuery.isError && !reviewsQuery.data) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <ShieldAlert className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">Review moderation unavailable</h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {getErrorMessage(reviewsQuery.error, 'Unable to load the marketplace review queue.')}
                    </p>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                        onClick={() => void reviewsQuery.refetch()}
                    >
                        <RefreshCw className="h-4 w-4" />
                        Retry request
                    </button>
                </div>
            </div>
        )
    }

    return (
        <>
            <div className="space-y-6">
                <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                    <div className="relative overflow-hidden px-6 py-6 sm:px-8 sm:py-8">
                        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.18),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(59,130,246,0.12),_transparent_26%)]" />
                        <div className="relative flex flex-col gap-6">
                            <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                                <div className="max-w-3xl">
                                    <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                                        <ShieldCheck className="h-4 w-4" />
                                        Reviews Moderation
                                    </div>
                                    <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                                        Moderate customer trust signals for the global Shopixy marketplace
                                    </h1>
                                    <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">
                                        Review every marketplace rating in one queue, inspect customer history before deciding, and keep the public product experience approved-only and consistent with backend moderation.
                                    </p>
                                </div>

                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-3xl bg-slate-950 px-5 py-4 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                                    disabled={reviewsQuery.isFetching}
                                    onClick={() => void reviewsQuery.refetch()}
                                >
                                    <RefreshCw className={`h-4 w-4 ${reviewsQuery.isFetching ? 'animate-spin' : ''}`} />
                                    {reviewsQuery.isFetching ? 'Refreshing' : 'Refresh queue'}
                                </button>
                            </div>

                            <div className="grid gap-3 lg:grid-cols-4">
                                <StatCard
                                    label="Pending on page"
                                    value={String(pendingCount)}
                                    icon={<Clock3 className="h-5 w-5 text-amber-700" />}
                                    tone="border-amber-200 bg-amber-50 text-amber-950"
                                />
                                <StatCard
                                    label="Approved on page"
                                    value={String(approvedCount)}
                                    icon={<CheckCircle2 className="h-5 w-5 text-emerald-700" />}
                                    tone="border-emerald-200 bg-emerald-50 text-emerald-950"
                                />
                                <StatCard
                                    label="Rejected on page"
                                    value={String(rejectedCount)}
                                    icon={<XCircle className="h-5 w-5 text-rose-700" />}
                                    tone="border-rose-200 bg-rose-50 text-rose-950"
                                />
                                <StatCard
                                    label="Verified purchases"
                                    value={String(verifiedCount)}
                                    icon={<ShieldCheck className="h-5 w-5 text-sky-700" />}
                                    tone="border-sky-200 bg-sky-50 text-sky-950"
                                />
                            </div>
                        </div>
                    </div>

                    <div className="grid gap-4 border-t border-slate-100 bg-slate-50/80 px-6 py-5 text-sm text-slate-600 sm:grid-cols-3 sm:px-8">
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Current page</p>
                            <p className="mt-2 font-medium text-slate-900">
                                Page {reviewsQuery.data?.page ?? filters.page} of {reviewsQuery.data?.totalPages ?? 1}
                            </p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Matching reviews</p>
                            <p className="mt-2 font-medium text-slate-900">{reviewsQuery.data?.totalCount ?? 0}</p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Product filter mode</p>
                            <p className="mt-2 font-medium text-slate-900">{filters.productId ? 'Exact Product ID' : 'All products'}</p>
                        </div>
                    </div>
                </section>

                <section className="rounded-[32px] border border-slate-200 bg-white px-6 py-5 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Queue filters</p>
                            <h2 className="mt-2 text-2xl font-semibold text-slate-950">Slice the moderation queue quickly</h2>
                        </div>

                        <button
                            type="button"
                            className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            onClick={() => {
                                setProductIdInput('')
                                setSearchParams({
                                    page: String(DEFAULT_PAGE),
                                    pageSize: String(DEFAULT_PAGE_SIZE),
                                })
                            }}
                        >
                            <Filter className="h-4 w-4" />
                            Reset filters
                        </button>
                    </div>

                    <div className="mt-5 grid gap-4 lg:grid-cols-4">
                        <label className="space-y-2 text-sm">
                            <span className="font-semibold text-slate-700">Status</span>
                            <select
                                value={filters.status}
                                onChange={(event) =>
                                    updateParams({
                                        status: event.target.value || null,
                                        page: String(DEFAULT_PAGE),
                                    })
                                }
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            >
                                <option value="">All statuses</option>
                                {SUPER_ADMIN_REVIEW_STATUS_OPTIONS.map((status) => (
                                    <option key={status} value={status}>
                                        {status}
                                    </option>
                                ))}
                            </select>
                        </label>

                        <label className="space-y-2 text-sm">
                            <span className="font-semibold text-slate-700">Rating</span>
                            <select
                                value={filters.rating}
                                onChange={(event) =>
                                    updateParams({
                                        rating: event.target.value || null,
                                        page: String(DEFAULT_PAGE),
                                    })
                                }
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            >
                                <option value="">All ratings</option>
                                {[5, 4, 3, 2, 1].map((rating) => (
                                    <option key={rating} value={rating}>
                                        {rating} stars
                                    </option>
                                ))}
                            </select>
                        </label>

                        <label className="space-y-2 text-sm lg:col-span-2">
                            <span className="font-semibold text-slate-700">Product ID</span>
                            <div className="flex gap-3">
                                <div className="relative flex-1">
                                    <Search className="pointer-events-none absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                                    <input
                                        value={productIdInput}
                                        onChange={(event) => setProductIdInput(event.target.value)}
                                        placeholder="Paste a marketplace product GUID to filter exactly"
                                        className="w-full rounded-2xl border border-slate-200 bg-white py-3 pl-11 pr-4 text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                    />
                                </div>
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                                    onClick={() =>
                                        updateParams({
                                            productId: productIdInput.trim() || null,
                                            page: String(DEFAULT_PAGE),
                                        })
                                    }
                                >
                                    Apply
                                </button>
                            </div>
                        </label>
                    </div>
                </section>

                {reviewsQuery.isError && reviewsQuery.data ? (
                    <div className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900 shadow-sm">
                        Showing the most recent cached moderation queue while the latest refresh is being retried.
                    </div>
                ) : null}

                {(approveMutation.isError || rejectMutation.isError) ? (
                    <div className="rounded-[28px] border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-900 shadow-sm">
                        {getErrorMessage(approveMutation.error ?? rejectMutation.error, 'Unable to update the review status right now.')}
                    </div>
                ) : null}

                <ReviewsTable
                    reviews={reviews}
                    isLoading={reviewsQuery.isLoading && !reviewsQuery.data}
                    onSelectReview={setSelectedReviewId}
                    onApprove={(reviewId) => approveMutation.mutate(reviewId)}
                    onReject={(reviewId) => rejectMutation.mutate(reviewId)}
                    actionDisabled={isMutating}
                />

                <section className="flex flex-col gap-4 rounded-[32px] border border-slate-200 bg-white px-6 py-5 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)] lg:flex-row lg:items-center lg:justify-between">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Pagination</p>
                        <p className="mt-2 text-sm leading-6 text-slate-700">
                            Page {filters.page} of {reviewsQuery.data?.totalPages || 1} with {reviewsQuery.data?.totalCount || 0} matching marketplace reviews.
                        </p>
                    </div>

                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                        <label className="flex items-center gap-3 text-sm text-slate-600">
                            <span>Page size</span>
                            <select
                                value={filters.pageSize}
                                onChange={(event) =>
                                    updateParams({
                                        pageSize: event.target.value,
                                        page: String(DEFAULT_PAGE),
                                    })
                                }
                                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            >
                                {pageSizeOptions.map((size) => (
                                    <option key={size} value={size}>
                                        {size}
                                    </option>
                                ))}
                            </select>
                        </label>

                        <div className="flex items-center gap-3">
                            <button
                                type="button"
                                className="inline-flex items-center justify-center rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
                                disabled={filters.page <= 1}
                                onClick={() => updateParams({ page: String(Math.max(1, filters.page - 1)) })}
                            >
                                Previous
                            </button>
                            <button
                                type="button"
                                className="inline-flex items-center justify-center rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                                disabled={filters.page >= Math.max(1, reviewsQuery.data?.totalPages ?? 0)}
                                onClick={() => updateParams({ page: String(filters.page + 1) })}
                            >
                                Next
                            </button>
                        </div>
                    </div>
                </section>
            </div>

            <ReviewDetailsDrawer
                isOpen={Boolean(selectedReviewId)}
                details={detailsQuery.data}
                isLoading={detailsQuery.isLoading}
                isError={detailsQuery.isError}
                errorMessage={detailsQuery.isError ? getErrorMessage(detailsQuery.error, 'Unable to load review details.') : null}
                moderationNote={moderationNote}
                onModerationNoteChange={setModerationNote}
                onClose={() => setSelectedReviewId(null)}
                onRetry={() => void detailsQuery.refetch()}
                onApprove={() => {
                    if (selectedReviewId) {
                        approveMutation.mutate(selectedReviewId)
                    }
                }}
                onReject={() => {
                    if (selectedReviewId) {
                        rejectMutation.mutate(selectedReviewId)
                    }
                }}
                isMutating={isMutating}
            />
        </>
    )
}

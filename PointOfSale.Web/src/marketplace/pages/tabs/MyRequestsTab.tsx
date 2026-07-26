import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { marketplaceApi } from '../../api/marketplaceApi'
import { marketplaceKeys } from '../../queryKeys'
import { ReviewModal } from '../../components/ReviewModal'
import { useFormatting } from '../../../hooks/useFormatting'
import { deriveServiceRequestLifecyclePresentation } from '../../status'
import { deriveServiceRequestContractPresentation } from '../../contractPresentation'
import { deriveServiceRequestCompletionEligibility } from '../../completionEligibility'
import { executeServiceRequestCompletionWorkflow } from '../../completionWorkflow'
import { deriveServiceRequestCompletionStatePresentation } from '../../completionStateAuthority'
import { deriveServiceRequestRatingEligibility } from '../../ratingEligibility'
import { deriveServiceRequestRatingStatePresentation } from '../../ratingStateAuthority'
import { executeServiceRequestRatingWorkflow } from '../../ratingWorkflow'
import { RatingStateBadge } from '../../components/RatingStateBadge'
import type { CreateReviewRequest } from '../../types'
import { useAuthStore } from '../../../store/authStore'
import {
    marketplaceAuthorityQueryOptions,
    publishMarketplaceAuthorityInvalidation,
    useMarketplaceAuthorityScope,
} from '../../authority'

export function MyRequestsTab() {
    const { t } = useTranslation()
    const { formatCurrency, formatDate, isRTL } = useFormatting()
    const navigate = useNavigate()
    const queryClient = useQueryClient()
    const authorityScope = useMarketplaceAuthorityScope()
    const { user } = useAuthStore()
    const currentUserId = user?.id ?? user?.userId
    const [pendingRequestIds, setPendingRequestIds] = useState(() => new Set<string>())
    const pendingRequestIdsRef = useRef(new Set<string>())
    const [reviewingRequest, setReviewingRequest] = useState<{
        requestId: string
        serviceCategory?: string
    } | null>(null)

    const { data: requestData = [], isLoading, isFetching, isError } = useQuery({
        queryKey: marketplaceKeys.myRequests(authorityScope),
        queryFn: marketplaceApi.getMyRequests,
        ...marketplaceAuthorityQueryOptions,
    })
    const requests = isError ? [] : requestData
    const lifecycleRows = requests
        .map((request: any) => {
            const lifecycleCandidate = deriveServiceRequestLifecyclePresentation(request)
            const completionState = deriveServiceRequestCompletionStatePresentation(
                request,
                lifecycleCandidate,
                currentUserId,
            )
            const lifecycle = completionState?.lifecycle ?? null
            return {
                request,
                lifecycle,
                completionEligibility: deriveServiceRequestCompletionEligibility(
                    request,
                    lifecycle,
                    currentUserId,
                    'buyer',
                ),
                ratingEligibility: deriveServiceRequestRatingEligibility(
                    request,
                    lifecycle,
                    currentUserId,
                ),
                ratingState: deriveServiceRequestRatingStatePresentation(
                    request,
                    lifecycle,
                    currentUserId,
                ),
            }
        })
        .filter((row): row is {
            request: any
            lifecycle: NonNullable<ReturnType<typeof deriveServiceRequestLifecyclePresentation>>
            completionEligibility: ReturnType<typeof deriveServiceRequestCompletionEligibility>
            ratingEligibility: ReturnType<typeof deriveServiceRequestRatingEligibility>
            ratingState: ReturnType<typeof deriveServiceRequestRatingStatePresentation>
        } => row.lifecycle !== null)

    useEffect(() => {
        if (!reviewingRequest) {
            return
        }

        const stillReviewable = lifecycleRows.some((row) =>
            row.request.requestId === reviewingRequest.requestId && row.ratingEligibility?.canRate === true)
        if (!stillReviewable) {
            setReviewingRequest(null)
        }
    }, [lifecycleRows, reviewingRequest])

    const beginPendingRequest = (requestId: string) => {
        if (pendingRequestIdsRef.current.has(requestId)) {
            return false
        }

        const nextPendingRequestIds = new Set(pendingRequestIdsRef.current)
        nextPendingRequestIds.add(requestId)
        pendingRequestIdsRef.current = nextPendingRequestIds
        setPendingRequestIds(nextPendingRequestIds)
        return true
    }

    const endPendingRequest = (requestId: string) => {
        if (!pendingRequestIdsRef.current.has(requestId)) {
            return
        }

        const nextPendingRequestIds = new Set(pendingRequestIdsRef.current)
        nextPendingRequestIds.delete(requestId)
        pendingRequestIdsRef.current = nextPendingRequestIds
        setPendingRequestIds(nextPendingRequestIds)
    }

    const confirmCompletionMutation = useMutation({
        mutationFn: (requestId: string) => executeServiceRequestCompletionWorkflow({
            requestId,
            authorityScope,
            actorUserId: currentUserId,
            kind: 'confirmCompletion',
            mutate: () => marketplaceApi.confirmCompletion(requestId),
            revalidate: () => marketplaceApi.getRequestDetails(requestId),
        }),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
        onSettled: () => {
            publishMarketplaceAuthorityInvalidation(authorityScope)
            queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
    })

    const submitReviewMutation = useMutation({
        mutationFn: (review: CreateReviewRequest) => executeServiceRequestRatingWorkflow({
            review,
            authorityScope,
            actorUserId: currentUserId,
            revalidate: () => marketplaceApi.getRequestDetails(review.serviceRequestId),
            mutate: (validatedReview) => marketplaceApi.createReview(validatedReview),
        }),
        retry: false,
        networkMode: 'online',
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
        onSettled: () => {
            publishMarketplaceAuthorityInvalidation(authorityScope)
            queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
    })

    const handleConfirmCompletion = async (requestId: string) => {
        if (pendingRequestIdsRef.current.has(requestId)) {
            return
        }

        if (!window.confirm(t('servicesMarketplace.confirm.confirmCompletionDescription'))) {
            return
        }

        if (!beginPendingRequest(requestId)) {
            return
        }

        try {
            await confirmCompletionMutation.mutateAsync(requestId)
        } catch (error) {
            window.alert(t('servicesMarketplace.feedback.confirmCompletionFailed'))
        } finally {
            endPendingRequest(requestId)
        }
    }

    const handleSubmitReview = async (review: CreateReviewRequest) => {
        await submitReviewMutation.mutateAsync(review)
    }

    if (isLoading || isFetching) {
        return (
            <div className="flex items-center justify-center py-12">
                <div className="text-sm text-gray-500">{t('common.loading')}</div>
            </div>
        )
    }

    if (lifecycleRows.length === 0) {
        return (
            <div className="text-center py-12">
                <p className="text-gray-500">{t('servicesMarketplace.myRequests.emptyTitle')}</p>
            </div>
        )
    }

    return (
        <>
            <div className="space-y-4" dir={isRTL ? 'rtl' : 'ltr'}>
                {lifecycleRows.map(({ request, lifecycle, completionEligibility, ratingEligibility, ratingState }) => {
                    const requestActionsPending = pendingRequestIds.has(request.requestId)
                    const contract = deriveServiceRequestContractPresentation(request, lifecycle)

                    return (
                        <div key={request.requestId} className="card">
                            <div className="flex items-start justify-between gap-4">
                                <div>
                                    <h3 className="text-lg font-semibold text-gray-900">
                                        {request.serviceTitle || t('servicesMarketplace.requestCard.serviceRequest')}
                                    </h3>
                                    <p className="text-sm text-gray-500 mt-1">
                                        {t('servicesMarketplace.requestCard.provider')}:{' '}
                                        <span className="font-medium text-gray-700">
                                            {request.providerDisplayName}
                                        </span>
                                    </p>
                                    <p className="text-xs text-gray-400 mt-2">
                                        {t('servicesMarketplace.requestCard.created')}:{' '}
                                        {formatDate(request.createdAt)}
                                    </p>
                                </div>
                                <span className={`px-2.5 py-0.5 text-xs font-semibold rounded-full ${lifecycle.statusClass}`}>
                                    {t(`servicesMarketplace.status.${lifecycle.statusKey}`, { defaultValue: lifecycle.statusLabelFallback })}
                                </span>
                            </div>

                            {contract ? (
                                <div className="mt-4 rounded-lg border border-gray-100 bg-gray-50 p-3 text-sm text-gray-700">
                                    <div className="font-medium text-gray-900">{contract.title}</div>
                                    {contract.category ? <div className="mt-1 text-xs text-gray-500">{contract.category}</div> : null}
                                    {contract.scope ? <p className="mt-2 italic text-gray-600">"{contract.scope}"</p> : null}
                                    {contract.price !== null ? (
                                        <div className="mt-2 font-semibold text-gray-900">
                                            {formatCurrency(contract.price, contract.currency || undefined)}
                                            {contract.pricingType === 'Hourly' ? <span className="font-normal text-gray-500"> / hour</span> : null}
                                        </div>
                                    ) : null}
                                </div>
                            ) : null}

                            <div className="mt-4 flex flex-wrap gap-2">
                                {ratingState ? <RatingStateBadge ratingState={ratingState} /> : null}

                                {lifecycle.canViewChatHistory ? (
                                    <button
                                        onClick={() => navigate(`/marketplace/chat/${request.requestId}`)}
                                        className="btn-secondary"
                                    >
                                        {t('servicesMarketplace.requestCard.openChat')}
                                    </button>
                                ) : null}

                                {completionEligibility?.canConfirmCompletion ? (
                                    <button
                                        onClick={() => handleConfirmCompletion(request.requestId)}
                                        disabled={requestActionsPending}
                                        className="btn-primary disabled:cursor-not-allowed disabled:opacity-60"
                                    >
                                        {t('servicesMarketplace.requestCard.confirmCompletion')}
                                    </button>
                                ) : null}

                                {ratingEligibility?.canRate ? (
                                    <button
                                        onClick={() =>
                                            setReviewingRequest({
                                                requestId: request.requestId,
                                                serviceCategory: request.serviceCategory,
                                            })
                                        }
                                        className="btn-primary bg-green-600 hover:bg-green-700"
                                    >
                                        {t('servicesMarketplace.myRequests.leaveReview')}
                                    </button>
                                ) : null}
                            </div>
                        </div>
                    )
                })}
            </div>

            {reviewingRequest ? (
                <ReviewModal
                    key={reviewingRequest.requestId}
                    requestId={reviewingRequest.requestId}
                    serviceCategory={reviewingRequest.serviceCategory}
                    onSubmit={handleSubmitReview}
                    onClose={() => setReviewingRequest(null)}
                />
            ) : null}
        </>
    )
}

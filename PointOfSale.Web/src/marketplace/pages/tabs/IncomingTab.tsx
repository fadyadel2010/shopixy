import { useRef, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useFormatting } from '../../../hooks/useFormatting'
import { marketplaceApi } from '../../api/marketplaceApi'
import { marketplaceKeys } from '../../queryKeys'
import { deriveServiceRequestLifecyclePresentation } from '../../status'
import { deriveServiceRequestLifecycleMutationAuthority } from '../../status'
import { deriveServiceRequestContractPresentation } from '../../contractPresentation'
import { deriveServiceRequestCompletionEligibility } from '../../completionEligibility'
import { executeServiceRequestCompletionWorkflow } from '../../completionWorkflow'
import { deriveServiceRequestCompletionStatePresentation } from '../../completionStateAuthority'
import { deriveServiceRequestRatingStatePresentation } from '../../ratingStateAuthority'
import { useAuthStore } from '../../../store/authStore'
import {
    marketplaceAuthorityQueryOptions,
    publishMarketplaceAuthorityInvalidation,
    useMarketplaceAuthorityScope,
} from '../../authority'

export function IncomingTab() {
    const { t } = useTranslation()
    const { formatCurrency, formatDate, formatNumber, isRTL } = useFormatting()
    const navigate = useNavigate()
    const queryClient = useQueryClient()
    const authorityScope = useMarketplaceAuthorityScope()
    const { user } = useAuthStore()
    const currentUserId = user?.id ?? user?.userId
    const [pendingRequestIds, setPendingRequestIds] = useState(() => new Set<string>())
    const pendingRequestIdsRef = useRef(new Set<string>())

    const { data: requestData = [], isLoading, isFetching, isError } = useQuery({
        queryKey: marketplaceKeys.incomingRequests(authorityScope),
        queryFn: marketplaceApi.getIncomingRequests,
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
                    'provider',
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
            ratingState: ReturnType<typeof deriveServiceRequestRatingStatePresentation>
        } => row.lifecycle !== null)

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

    const acceptMutation = useMutation({
        mutationFn: async (requestId: string) => {
            const response = await marketplaceApi.acceptRequest(requestId)
            if (!deriveServiceRequestLifecycleMutationAuthority(response, 'accept')) {
                throw new Error('Invalid request accept lifecycle response')
            }
            return response
        },
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
        onSettled: async () => {
            publishMarketplaceAuthorityInvalidation(authorityScope)
            await queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
    })

    const markCompletedMutation = useMutation({
        mutationFn: (requestId: string) => executeServiceRequestCompletionWorkflow({
            requestId,
            authorityScope,
            actorUserId: currentUserId,
            kind: 'markCompleted',
            mutate: () => marketplaceApi.markCompleted(requestId),
            revalidate: () => marketplaceApi.getRequestDetails(requestId),
        }),
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
        onSettled: async () => {
            publishMarketplaceAuthorityInvalidation(authorityScope)
            await queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
    })

    const rejectMutation = useMutation({
        mutationFn: async (requestId: string) => {
            const response = await marketplaceApi.rejectRequest(requestId)
            if (!deriveServiceRequestLifecycleMutationAuthority(response, 'reject')) {
                throw new Error('Invalid request rejection lifecycle response')
            }
            return response
        },
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
        onSettled: async () => {
            publishMarketplaceAuthorityInvalidation(authorityScope)
            await queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
    })

    const handleAccept = async (requestId: string) => {
        if (pendingRequestIdsRef.current.has(requestId)) {
            return
        }

        if (!window.confirm(t('servicesMarketplace.confirm.acceptRequestDescription'))) {
            return
        }

        if (!beginPendingRequest(requestId)) {
            return
        }

        try {
            await acceptMutation.mutateAsync(requestId)
        } catch (error) {
            window.alert(t('servicesMarketplace.feedback.acceptFailed'))
        } finally {
            endPendingRequest(requestId)
        }
    }

    const handleMarkCompleted = async (requestId: string) => {
        if (pendingRequestIdsRef.current.has(requestId)) {
            return
        }

        if (!window.confirm(t('servicesMarketplace.confirm.markCompletedDescription'))) {
            return
        }

        if (!beginPendingRequest(requestId)) {
            return
        }

        try {
            await markCompletedMutation.mutateAsync(requestId)
        } catch (error) {
            window.alert(t('servicesMarketplace.feedback.markCompletedFailed'))
        } finally {
            endPendingRequest(requestId)
        }
    }

    const handleReject = async (requestId: string) => {
        if (pendingRequestIdsRef.current.has(requestId)) {
            return
        }

        const reason = window.prompt(t('servicesMarketplace.confirm.rejectReasonPlaceholder'))
        if (reason === null) {
            return
        }

        if (!beginPendingRequest(requestId)) {
            return
        }

        try {
            await rejectMutation.mutateAsync(requestId)
        } catch (error) {
            window.alert(t('servicesMarketplace.feedback.rejectFailed'))
        } finally {
            endPendingRequest(requestId)
        }
    }

    if (isLoading || isFetching) {
        return (
            <div className="flex justify-center py-12">
                <div className="text-gray-500">{t('servicesMarketplace.incoming.loading')}</div>
            </div>
        )
    }

    return (
        <div className="space-y-6" dir={isRTL ? 'rtl' : 'ltr'}>
            <div className="flex items-center justify-between">
                <div className={isRTL ? 'text-right' : 'text-left'}>
                    <h2 className="text-xl font-bold text-gray-900">
                        {t('servicesMarketplace.incoming.title')}
                    </h2>
                    <p className="text-sm text-gray-500 mt-1">
                        {t('servicesMarketplace.incoming.subtitle')}
                    </p>
                </div>
                <span className="px-3 py-1 bg-indigo-100 text-indigo-700 text-sm font-medium rounded-full">
                    {formatNumber(lifecycleRows.length)} {t('servicesMarketplace.incoming.requestCountLabel')}
                </span>
            </div>

            {lifecycleRows.length > 0 ? (
                <div className="space-y-4">
                    {lifecycleRows.map(({ request, lifecycle, completionEligibility }) => {
                        const requestActionsPending = pendingRequestIds.has(request.requestId)
                        const contract = deriveServiceRequestContractPresentation(request, lifecycle)

                        return (
                            <div key={request.requestId} className="card hover:shadow-lg transition-shadow">
                                <div className="flex justify-between items-start mb-4 gap-4">
                                    <div className="flex-1">
                                        <h3 className="text-xl font-bold text-gray-900 mb-2">
                                            {request.serviceTitle}
                                        </h3>
                                        <p className="text-sm text-gray-600 mb-1">
                                            <span className="font-medium">
                                                {t('servicesMarketplace.incoming.buyer')}:
                                            </span>{' '}
                                            {request.buyerDisplayName}
                                        </p>
                                        <p className="text-xs text-gray-500">
                                            {t('servicesMarketplace.requestCard.created')}:{' '}
                                            {formatDate(request.createdAt)}
                                        </p>
                                    </div>
                                    <span className={`px-3 py-1 text-xs font-semibold rounded-full ${lifecycle.statusClass}`}>
                                        {t(`servicesMarketplace.status.${lifecycle.statusKey}`, { defaultValue: lifecycle.statusLabelFallback })}
                                    </span>
                                </div>

                                {contract ? (
                                    <div className="mb-4 rounded-lg border border-gray-100 bg-gray-50 p-3 text-sm text-gray-700">
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

                                {request.requestMessage ? (
                                    <div className="mb-4 p-3 bg-gray-50 rounded-lg">
                                        <p className="text-sm text-gray-700">{request.requestMessage}</p>
                                    </div>
                                ) : null}

                                <div className="flex gap-2 flex-wrap">
                                    {lifecycle.canAccept ? (
                                        <button
                                            className="btn-primary text-sm disabled:cursor-not-allowed disabled:opacity-60"
                                            disabled={requestActionsPending}
                                            onClick={() => handleAccept(request.requestId)}
                                        >
                                            {t('servicesMarketplace.actions.acceptRequest')}
                                        </button>
                                    ) : null}

                                    {completionEligibility?.canMarkCompleted ? (
                                        <button
                                            className="btn-primary text-sm bg-green-600 hover:bg-green-700 disabled:cursor-not-allowed disabled:opacity-60"
                                            disabled={requestActionsPending}
                                            onClick={() => handleMarkCompleted(request.requestId)}
                                        >
                                            {t('servicesMarketplace.actions.markCompleted')}
                                        </button>
                                    ) : null}

                                    {lifecycle.canViewChatHistory ? (
                                        <button
                                            className="btn-secondary text-sm"
                                            onClick={() => navigate(`/marketplace/chat/${request.requestId}`)}
                                        >
                                            {t('servicesMarketplace.requestCard.openChat')}
                                        </button>
                                    ) : null}

                                    {lifecycle.canReject ? (
                                        <button
                                            className="btn-secondary text-sm text-red-600 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-60"
                                            disabled={requestActionsPending}
                                            onClick={() => handleReject(request.requestId)}
                                        >
                                            {t('servicesMarketplace.actions.reject')}
                                        </button>
                                    ) : null}
                                </div>
                            </div>
                        )
                    })}
                </div>
            ) : (
                <div className="card text-center py-12">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">
                        {t('servicesMarketplace.incoming.emptyTitle')}
                    </h3>
                    <p className="text-gray-500">
                        {t('servicesMarketplace.incoming.emptyDescription')}
                    </p>
                </div>
            )}
        </div>
    )
}

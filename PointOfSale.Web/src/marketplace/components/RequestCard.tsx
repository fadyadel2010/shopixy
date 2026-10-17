import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import type { ServiceRequest } from '../types'
import type { PresentationFormatting } from '../presentationFormatting'
import { deriveServiceRequestLifecyclePresentation } from '../status'
import { deriveServiceRequestContractPresentation } from '../contractPresentation'
import { deriveServiceRequestCompletionEligibility } from '../completionEligibility'
import { deriveServiceRequestCompletionStatePresentation } from '../completionStateAuthority'
import { deriveServiceRequestRatingStatePresentation } from '../ratingStateAuthority'
import { RatingStateBadge } from './RatingStateBadge'
import { useAuthStore } from '../../store/authStore'

interface RequestCardProps extends Pick<PresentationFormatting, 'formatCurrency' | 'formatDate'> {
    request: ServiceRequest
    showActions?: boolean
    onAccept?: (requestId: string) => void
    onMarkCompleted?: (requestId: string) => void
    onConfirmCompletion?: (requestId: string) => void
    onCancel?: (requestId: string) => void
}

export function RequestCard({
    request,
    showActions = true,
    onAccept,
    onMarkCompleted,
    onConfirmCompletion,
    onCancel,
    formatCurrency,
    formatDate,
}: RequestCardProps) {
    const { t } = useTranslation()
    const { user } = useAuthStore()
    const currentUserId = user?.id ?? user?.userId
    const lifecycleCandidate = deriveServiceRequestLifecyclePresentation(request)
    const completionState = deriveServiceRequestCompletionStatePresentation(
        request,
        lifecycleCandidate,
        currentUserId,
    )
    const lifecycle = completionState?.lifecycle ?? null
    const completionEligibility = deriveServiceRequestCompletionEligibility(request, lifecycle, currentUserId)
    const ratingState = deriveServiceRequestRatingStatePresentation(request, lifecycle, currentUserId)
    const contract = deriveServiceRequestContractPresentation(request, lifecycle)

    if (!lifecycle) {
        return null
    }

    return (
        <div className="card mb-4 hover:shadow-lg transition-shadow duration-200">
            <div className="flex items-start justify-between mb-3">
                <div>
                    <h4 className="text-lg font-semibold text-gray-900">
                        {request.serviceTitle || t('servicesMarketplace.requestCard.serviceRequest')}
                    </h4>
                    {request.providerDisplayName && (
                        <p className="text-sm text-gray-500 mt-0.5">
                            {t('servicesMarketplace.requestCard.provider')}:{' '}
                            <span className="font-medium text-gray-700">{request.providerDisplayName}</span>
                        </p>
                    )}
                </div>
                <span className={`px-2.5 py-0.5 text-xs font-semibold rounded-full whitespace-nowrap ${lifecycle.statusClass}`}>
                    {t(`servicesMarketplace.status.${lifecycle.statusKey}`, { defaultValue: lifecycle.statusLabelFallback })}
                </span>
            </div>

            {request.requestMessage && (
                <div className="bg-gray-50 rounded-lg p-3 mb-3">
                    <p className="text-sm text-gray-600 italic">"{request.requestMessage}"</p>
                </div>
            )}

            {contract ? (
                <div className="mb-3 rounded-lg border border-gray-100 bg-white p-3 text-sm text-gray-700">
                    <div className="font-medium text-gray-900">{contract.title}</div>
                    {contract.category ? <div className="mt-1 text-xs text-gray-500">{contract.category}</div> : null}
                    {contract.price !== null ? (
                        <div className="mt-2 font-semibold text-gray-900">
                            {formatCurrency(contract.price, contract.currency || undefined)}
                            {contract.pricingType === 'Hourly' ? <span className="font-normal text-gray-500"> / hour</span> : null}
                        </div>
                    ) : null}
                </div>
            ) : null}

            <div className="text-xs text-gray-400 mb-4">
                {t('servicesMarketplace.requestCard.created')}: {formatDate(request.createdAt)}
            </div>

            {ratingState ? (
                <div className="mb-3">
                    <RatingStateBadge ratingState={ratingState} />
                </div>
            ) : null}

            {showActions && (
                <div className="flex flex-wrap items-center gap-2 pt-3 border-t border-gray-100">
                    {lifecycle.canAccept && onAccept && (
                        <button
                            onClick={() => onAccept(request.requestId)}
                            className="inline-flex items-center px-3 py-1.5 text-xs font-medium text-white bg-green-600 hover:bg-green-700 rounded-lg transition-colors"
                        >
                            {t('servicesMarketplace.requestCard.accept')}
                        </button>
                    )}

                    {completionEligibility?.canMarkCompleted && onMarkCompleted && (
                        <button
                            onClick={() => onMarkCompleted(request.requestId)}
                            className="btn-primary text-sm py-1.5 px-3"
                        >
                            {t('servicesMarketplace.requestCard.markCompleted')}
                        </button>
                    )}

                    {completionEligibility?.canConfirmCompletion && onConfirmCompletion && (
                        <button
                            onClick={() => onConfirmCompletion(request.requestId)}
                            className="inline-flex items-center px-3 py-1.5 text-xs font-medium text-white bg-green-600 hover:bg-green-700 rounded-lg transition-colors"
                        >
                            {t('servicesMarketplace.requestCard.confirmCompletion')}
                        </button>
                    )}

                    {lifecycle.canViewChatHistory && (
                        <Link
                            to={`/marketplace/chat/${request.requestId}`}
                            className="inline-flex items-center px-3 py-1.5 text-xs font-medium text-indigo-700 bg-indigo-100 hover:bg-indigo-200 rounded-lg transition-colors"
                        >
                            {t('servicesMarketplace.requestCard.openChat')}
                        </Link>
                    )}

                    {lifecycle.canCancel && onCancel && (
                        <button
                            onClick={() => onCancel(request.requestId)}
                            className="btn-danger text-sm py-1.5 px-3"
                        >
                            {t('servicesMarketplace.requestCard.cancel')}
                        </button>
                    )}
                </div>
            )}
        </div>
    )
}

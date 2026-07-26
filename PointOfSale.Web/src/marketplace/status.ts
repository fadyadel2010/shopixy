import type { ServiceRequest, ServiceRequestDetails } from './types'

export type ServiceRequestLifecycleStatusKey =
    | 'pending'
    | 'accepted'
    | 'completedByProvider'
    | 'completedByBuyer'
    | 'cancelled'
    | 'rejected'

export type ServiceRequestLifecyclePresentation = Readonly<{
    statusKey: ServiceRequestLifecycleStatusKey
    statusLabelFallback: string
    statusClass: string
    canAccept: boolean
    canReject: boolean
    canMarkCompleted: boolean
    canConfirmCompletion: boolean
    canCancel: boolean
    canReview: boolean
    canViewChatHistory: boolean
    canSendChat: boolean
    canChat: boolean
}>

export type ServiceRequestLifecycleMutationKind =
    | 'accept'
    | 'reject'
    | 'cancel'
    | 'markCompleted'
    | 'confirmCompletion'

export type ServiceRequestLifecycleMutationAuthority = Readonly<{
    statusKey: ServiceRequestLifecycleStatusKey
}>

const lifecycleStatusByBackendValue: Record<string, ServiceRequestLifecycleStatusKey> = {
    Pending: 'pending',
    Accepted: 'accepted',
    CompletedByProvider: 'completedByProvider',
    CompletedByBuyer: 'completedByBuyer',
    Cancelled: 'cancelled',
    Rejected: 'rejected',
}

export const serviceRequestStatusColors: Record<ServiceRequestLifecycleStatusKey, string> = {
    pending: 'bg-yellow-100 text-yellow-800',
    accepted: 'bg-blue-100 text-blue-800',
    completedByProvider: 'bg-green-100 text-green-800',
    completedByBuyer: 'bg-emerald-100 text-emerald-800',
    cancelled: 'bg-red-100 text-red-800',
    rejected: 'bg-red-100 text-red-800',
}

export function normalizeServiceRequestStatusKey(status?: string | null): ServiceRequestLifecycleStatusKey | null {
    return typeof status === 'string'
        ? lifecycleStatusByBackendValue[status] ?? null
        : null
}

const allowsLifecycleAction = (statusKey: ServiceRequestLifecycleStatusKey, action: keyof ServiceRequestLifecyclePresentation) => {
    switch (action) {
        case 'canAccept':
        case 'canReject':
            return statusKey === 'pending'
        case 'canMarkCompleted':
            return statusKey === 'accepted'
        case 'canConfirmCompletion':
            return statusKey === 'completedByProvider'
        case 'canCancel':
            return statusKey === 'pending' || statusKey === 'accepted'
        case 'canReview':
            return statusKey === 'completedByBuyer'
        case 'canSendChat':
        case 'canChat':
            return statusKey === 'accepted' || statusKey === 'completedByProvider'
        case 'canViewChatHistory':
            return statusKey !== 'pending'
        default:
            return false
    }
}

const backendBoolean = (value: unknown) => value === true

const expectedLifecycleMutationStatus: Record<ServiceRequestLifecycleMutationKind, ServiceRequestLifecycleStatusKey> = {
    accept: 'accepted',
    reject: 'rejected',
    cancel: 'cancelled',
    markCompleted: 'completedByProvider',
    confirmCompletion: 'completedByBuyer',
}

export function deriveServiceRequestLifecyclePresentation(
    request: Pick<ServiceRequest, 'status' | 'canAccept' | 'canReject' | 'canMarkCompleted' | 'canConfirmCompletion' | 'canCancel' | 'canReview' | 'canChat' | 'canViewChatHistory' | 'canSendChat'>,
): ServiceRequestLifecyclePresentation | null {
    const statusKey = normalizeServiceRequestStatusKey(request.status)
    if (!statusKey) {
        return null
    }

    const canViewChatHistory = backendBoolean(request.canViewChatHistory)
        && allowsLifecycleAction(statusKey, 'canViewChatHistory')
    const canSendChat = backendBoolean(request.canSendChat)
        && allowsLifecycleAction(statusKey, 'canSendChat')

    return {
        statusKey,
        statusLabelFallback: String(request.status || ''),
        statusClass: serviceRequestStatusColors[statusKey],
        canAccept: backendBoolean(request.canAccept) && allowsLifecycleAction(statusKey, 'canAccept'),
        canReject: backendBoolean(request.canReject) && allowsLifecycleAction(statusKey, 'canReject'),
        canMarkCompleted: backendBoolean(request.canMarkCompleted) && allowsLifecycleAction(statusKey, 'canMarkCompleted'),
        canConfirmCompletion: backendBoolean(request.canConfirmCompletion) && allowsLifecycleAction(statusKey, 'canConfirmCompletion'),
        canCancel: backendBoolean(request.canCancel) && allowsLifecycleAction(statusKey, 'canCancel'),
        canReview: backendBoolean(request.canReview) && allowsLifecycleAction(statusKey, 'canReview'),
        canViewChatHistory,
        canSendChat,
        canChat: canSendChat,
    }
}

export function deriveServiceRequestDetailsLifecyclePresentation(
    request: Pick<ServiceRequestDetails, 'status' | 'capabilities'>,
): ServiceRequestLifecyclePresentation | null {
    return deriveServiceRequestLifecyclePresentation({
        status: request.status,
        canAccept: request.capabilities.canAccept,
        canReject: request.capabilities.canReject,
        canMarkCompleted: request.capabilities.canMarkCompleted,
        canConfirmCompletion: request.capabilities.canConfirmCompletion,
        canCancel: request.capabilities.canCancel,
        canReview: request.capabilities.canReview,
        canChat: request.capabilities.canChat,
        canViewChatHistory: request.capabilities.canViewChatHistory,
        canSendChat: request.capabilities.canSendChat,
    })
}

export function deriveServiceRequestLifecycleMutationAuthority(
    response: { status?: unknown } | null | undefined,
    mutationKind: ServiceRequestLifecycleMutationKind,
): ServiceRequestLifecycleMutationAuthority | null {
    const statusKey = normalizeServiceRequestStatusKey(typeof response?.status === 'string' ? response.status : null)
    if (!statusKey || statusKey !== expectedLifecycleMutationStatus[mutationKind]) {
        return null
    }

    return { statusKey }
}

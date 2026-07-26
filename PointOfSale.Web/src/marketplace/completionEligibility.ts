import type { ServiceRequest, ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'

export type CompletionEligibilityActorRole = 'buyer' | 'provider'

export type ServiceRequestCompletionEligibility = Readonly<{
  requestId: string
  actorRole: CompletionEligibilityActorRole
  canMarkCompleted: boolean
  canConfirmCompletion: boolean
}>

type CompletionEligibilityRequest = Pick<
  ServiceRequest,
  'requestId' | 'serviceId' | 'buyerUserId' | 'providerUserId' | 'canMarkCompleted' | 'canConfirmCompletion'
> | Pick<
  ServiceRequestDetails,
  'requestId' | 'serviceId' | 'buyerUserId' | 'providerUserId' | 'capabilities'
>

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const normalizedGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) ? normalized : null
}

const completionCapabilities = (request: CompletionEligibilityRequest) => {
  if ('capabilities' in request) {
    return {
      canMarkCompleted: request.capabilities?.canMarkCompleted === true,
      canConfirmCompletion: request.capabilities?.canConfirmCompletion === true,
    }
  }

  return {
    canMarkCompleted: request.canMarkCompleted === true,
    canConfirmCompletion: request.canConfirmCompletion === true,
  }
}

export function deriveServiceRequestCompletionEligibility(
  request: CompletionEligibilityRequest,
  lifecycle: ServiceRequestLifecyclePresentation | null,
  currentUserId: unknown,
  expectedActorRole?: CompletionEligibilityActorRole,
): ServiceRequestCompletionEligibility | null {
  if (!lifecycle) {
    return null
  }

  const requestId = normalizedGuid(request.requestId)
  const serviceId = normalizedGuid(request.serviceId)
  const buyerUserId = normalizedGuid(request.buyerUserId)
  const providerUserId = normalizedGuid(request.providerUserId)
  const actorUserId = normalizedGuid(currentUserId)

  if (
    !requestId
    || !serviceId
    || !buyerUserId
    || !providerUserId
    || !actorUserId
    || buyerUserId === providerUserId
  ) {
    return null
  }

  const actorRole = actorUserId === buyerUserId
    ? 'buyer'
    : actorUserId === providerUserId
      ? 'provider'
      : null

  if (!actorRole || (expectedActorRole && actorRole !== expectedActorRole)) {
    return null
  }

  const capabilities = completionCapabilities(request)
  if (
    (capabilities.canMarkCompleted && actorRole !== 'provider')
    || (capabilities.canConfirmCompletion && actorRole !== 'buyer')
    || (capabilities.canMarkCompleted && capabilities.canConfirmCompletion)
  ) {
    return null
  }

  return {
    requestId,
    actorRole,
    canMarkCompleted: actorRole === 'provider'
      && lifecycle.statusKey === 'accepted'
      && lifecycle.canMarkCompleted
      && capabilities.canMarkCompleted,
    canConfirmCompletion: actorRole === 'buyer'
      && lifecycle.statusKey === 'completedByProvider'
      && lifecycle.canConfirmCompletion
      && capabilities.canConfirmCompletion,
  }
}

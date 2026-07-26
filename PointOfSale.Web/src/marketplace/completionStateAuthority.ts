import { deriveCompletionEvidencePresentation } from './completionEvidenceAuthority'
import type { CompletionEvidencePresentation } from './completionEvidenceAuthority'
import type { ServiceRequest, ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'

type CompletionStateRequest = Pick<
  ServiceRequest,
  'requestId' | 'buyerUserId' | 'providerUserId' | 'completionEvidence'
> | Pick<
  ServiceRequestDetails,
  'requestId' | 'buyerUserId' | 'providerUserId' | 'completionEvidence'
>

export type ServiceRequestCompletionStatePresentation = Readonly<{
  lifecycle: ServiceRequestLifecyclePresentation
  completionEvidence: CompletionEvidencePresentation | null
}>

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const normalizeGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) ? normalized : null
}

export function deriveServiceRequestCompletionStatePresentation(
  request: CompletionStateRequest,
  lifecycle: ServiceRequestLifecyclePresentation | null,
  currentUserId: unknown,
): ServiceRequestCompletionStatePresentation | null {
  if (!lifecycle || !request.completionEvidence) {
    return null
  }

  const requestId = normalizeGuid(request.requestId)
  const buyerUserId = normalizeGuid(request.buyerUserId)
  const providerUserId = normalizeGuid(request.providerUserId)
  const actorUserId = normalizeGuid(currentUserId)
  const evidence = request.completionEvidence

  if (
    !requestId
    || !buyerUserId
    || !providerUserId
    || buyerUserId === providerUserId
    || (actorUserId !== buyerUserId && actorUserId !== providerUserId)
    || normalizeGuid(evidence.requestId) !== requestId
    || evidence.status !== lifecycle.statusLabelFallback
    || !Array.isArray(evidence.notes)
    || evidence.notes.length !== 0
    || !Array.isArray(evidence.attachments)
    || evidence.attachments.length !== 0
    || !Array.isArray(evidence.deliverables)
    || evidence.deliverables.length !== 0
  ) {
    return null
  }

  const isCompleted = lifecycle.statusKey === 'completedByProvider'
    || lifecycle.statusKey === 'completedByBuyer'

  if (!isCompleted) {
    return evidence.providerCompletion === null && evidence.buyerConfirmation === null
      ? { lifecycle, completionEvidence: null }
      : null
  }

  const completionEvidence = deriveCompletionEvidencePresentation(
    request,
    lifecycle,
    actorUserId,
  )

  return completionEvidence
    ? { lifecycle, completionEvidence }
    : null
}

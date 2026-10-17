import type { ServiceRequestDetails } from './types'
import type { ServiceRequestCompletionStatePresentation } from './completionStateAuthority'
import {
  deriveRequestAuditTimelinePresentation,
  type RequestAuditTimelineEventPresentation,
} from './timelineAuthority'

type CompletionTimelineRequest = Pick<
  ServiceRequestDetails,
  | 'requestId'
  | 'buyerUserId'
  | 'providerUserId'
  | 'status'
  | 'createdAt'
  | 'auditTimeline'
>

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const isoTimestampPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/

const normalizeGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) ? normalized : null
}

const timestampValue = (value: unknown) => {
  if (
    typeof value !== 'string'
    || value.trim() !== value
    || !isoTimestampPattern.test(value)
  ) {
    return null
  }

  const timestamp = Date.parse(value)
  return Number.isFinite(timestamp) ? timestamp : null
}

const hasBackendEventIdentity = (
  event: RequestAuditTimelineEventPresentation,
  requestId: string,
) => {
  const requestIdentity = requestId.replace(/-/g, '')
  return new RegExp(`^${requestIdentity}:${event.eventType}:\\d+$`, 'i').test(event.eventId)
}

export function deriveCompletionTimelineAuditPresentation(
  request: CompletionTimelineRequest,
  completionState: ServiceRequestCompletionStatePresentation | null,
  currentUserId: unknown,
): readonly RequestAuditTimelineEventPresentation[] | null {
  if (!completionState) return null

  const requestId = normalizeGuid(request.requestId)
  const buyerUserId = normalizeGuid(request.buyerUserId)
  const providerUserId = normalizeGuid(request.providerUserId)
  const actorUserId = normalizeGuid(currentUserId)
  const createdAt = timestampValue(request.createdAt)

  if (
    !requestId
    || !buyerUserId
    || !providerUserId
    || buyerUserId === providerUserId
    || (actorUserId !== buyerUserId && actorUserId !== providerUserId)
    || createdAt === null
  ) {
    return null
  }

  const timeline = deriveRequestAuditTimelinePresentation(request, completionState.lifecycle)
  if (!timeline) return null

  for (const event of timeline) {
    if (!hasBackendEventIdentity(event, requestId)) return null

    const occurredAt = timestampValue(event.occurredAt)
    if (occurredAt === null) return null

    switch (event.eventType) {
      case 'Requested':
        if (event.actorRole !== 'Buyer' || normalizeGuid(event.actorUserId) !== buyerUserId || occurredAt !== createdAt) {
          return null
        }
        break
      case 'Accepted':
      case 'Rejected':
        if (event.actorRole !== 'Provider' || normalizeGuid(event.actorUserId) !== providerUserId) {
          return null
        }
        break
      case 'Cancelled':
        if (event.actorRole !== null || event.actorUserId !== null) {
          return null
        }
        break
      case 'CompletedByProvider': {
        const providerCompletion = completionState.completionEvidence?.providerCompletion
        if (
          !providerCompletion
          || event.actorRole !== 'Provider'
          || normalizeGuid(event.actorUserId) !== providerUserId
          || occurredAt !== timestampValue(providerCompletion.occurredAt)
        ) {
          return null
        }
        break
      }
      case 'CompletedByBuyer': {
        const buyerConfirmation = completionState.completionEvidence?.buyerConfirmation
        if (
          !buyerConfirmation
          || event.actorRole !== 'Buyer'
          || normalizeGuid(event.actorUserId) !== buyerUserId
          || occurredAt !== timestampValue(buyerConfirmation.occurredAt)
        ) {
          return null
        }
        break
      }
    }
  }

  return timeline
}

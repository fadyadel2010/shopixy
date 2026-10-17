import type { ServiceRequestAuditEvent, ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'
import { normalizeServiceRequestStatusKey } from './status'

type AuditEventType =
  | 'Requested'
  | 'Accepted'
  | 'Rejected'
  | 'Cancelled'
  | 'CompletedByProvider'
  | 'CompletedByBuyer'

export type RequestAuditTimelineEventPresentation = Readonly<{
  eventId: string
  eventType: AuditEventType
  statusKey: NonNullable<ReturnType<typeof normalizeServiceRequestStatusKey>>
  occurredAt: string
  actorRole: 'Buyer' | 'Provider' | null
  actorUserId: string | null
}>

const eventStatus: Record<AuditEventType, NonNullable<ReturnType<typeof normalizeServiceRequestStatusKey>>> = {
  Requested: 'pending',
  Accepted: 'accepted',
  Rejected: 'rejected',
  Cancelled: 'cancelled',
  CompletedByProvider: 'completedByProvider',
  CompletedByBuyer: 'completedByBuyer',
}

const requiredEventsByStatus: Record<NonNullable<ReturnType<typeof normalizeServiceRequestStatusKey>>, AuditEventType[]> = {
  pending: ['Requested'],
  accepted: ['Requested', 'Accepted'],
  rejected: ['Requested', 'Rejected'],
  cancelled: ['Requested', 'Cancelled'],
  completedByProvider: ['Requested', 'Accepted', 'CompletedByProvider'],
  completedByBuyer: ['Requested', 'Accepted', 'CompletedByProvider', 'CompletedByBuyer'],
}

const allowedEventsByStatus: Record<NonNullable<ReturnType<typeof normalizeServiceRequestStatusKey>>, Set<AuditEventType>> = {
  pending: new Set(['Requested']),
  accepted: new Set(['Requested', 'Accepted']),
  rejected: new Set(['Requested', 'Rejected']),
  cancelled: new Set(['Requested', 'Accepted', 'Cancelled']),
  completedByProvider: new Set(['Requested', 'Accepted', 'CompletedByProvider']),
  completedByBuyer: new Set(['Requested', 'Accepted', 'CompletedByProvider', 'CompletedByBuyer']),
}

const isAuditEventType = (value: unknown): value is AuditEventType =>
  typeof value === 'string' && Object.prototype.hasOwnProperty.call(eventStatus, value)

const nonEmpty = (value: unknown) => {
  const text = String(value ?? '').trim()
  return text || null
}

const validIsoDate = (value: unknown) => {
  const text = nonEmpty(value)
  if (!text || !Number.isFinite(Date.parse(text))) return null
  return text
}

const normalizeActorRole = (value: unknown) => {
  if (value === null || value === undefined || value === '') return null
  return value === 'Buyer' || value === 'Provider' ? value : undefined
}

export function deriveRequestAuditTimelinePresentation(
  request: Pick<ServiceRequestDetails, 'requestId' | 'status' | 'auditTimeline'>,
  lifecycle: ServiceRequestLifecyclePresentation | null,
): readonly RequestAuditTimelineEventPresentation[] | null {
  if (!lifecycle || !Array.isArray(request.auditTimeline)) return null

  const currentStatus = normalizeServiceRequestStatusKey(request.status)
  if (currentStatus !== lifecycle.statusKey) return null

  const seenIds = new Set<string>()
  const seenTypes = new Set<AuditEventType>()
  const allowedEvents = allowedEventsByStatus[currentStatus]
  const events: RequestAuditTimelineEventPresentation[] = []
  let lastTimestamp = -Infinity

  for (const raw of request.auditTimeline as readonly ServiceRequestAuditEvent[]) {
    const eventId = nonEmpty(raw?.eventId)
    const requestId = nonEmpty(raw?.requestId)
    const occurredAt = validIsoDate(raw?.occurredAt)
    const actorRole = normalizeActorRole(raw?.actorRole)
    const actorUserId = raw?.actorUserId === null || raw?.actorUserId === undefined ? null : nonEmpty(raw.actorUserId)

    if (!eventId || requestId !== request.requestId || !isAuditEventType(raw?.eventType) || !occurredAt || actorRole === undefined) {
      return null
    }

    if (seenIds.has(eventId) || seenTypes.has(raw.eventType) || !allowedEvents.has(raw.eventType)) {
      return null
    }

    const statusKey = normalizeServiceRequestStatusKey(raw.status)
    if (statusKey !== eventStatus[raw.eventType]) {
      return null
    }

    const timestamp = Date.parse(occurredAt)
    if (timestamp < lastTimestamp) {
      return null
    }

    lastTimestamp = timestamp
    seenIds.add(eventId)
    seenTypes.add(raw.eventType)
    events.push({
      eventId,
      eventType: raw.eventType,
      statusKey,
      occurredAt,
      actorRole,
      actorUserId,
    })
  }

  if (!requiredEventsByStatus[currentStatus].every((eventType) => seenTypes.has(eventType))) {
    return null
  }

  return events
}

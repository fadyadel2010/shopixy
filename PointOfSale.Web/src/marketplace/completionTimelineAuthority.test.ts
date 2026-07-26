import { describe, expect, it } from 'vitest'
import { deriveServiceRequestCompletionStatePresentation } from './completionStateAuthority'
import { deriveCompletionTimelineAuditPresentation } from './completionTimelineAuthority'
import { deriveServiceRequestDetailsLifecyclePresentation } from './status'
import type { ServiceRequestDetails } from './types'

const requestId = '3be1b510-0f53-4d37-907e-eabdc7fe78cb'
const buyerUserId = '13ed2b6d-c48f-4794-a210-0c7ab703e52f'
const providerUserId = 'f9c54195-8e39-4b95-a1be-3050f5f1175e'
const providerCompletedAt = '2026-06-14T00:02:00Z'
const buyerConfirmedAt = '2026-06-14T00:03:00Z'

const eventId = (eventType: string, ticks: string) =>
  `${requestId.replaceAll('-', '')}:${eventType}:${ticks}`

const requestDetails = (overrides: Partial<ServiceRequestDetails> = {}): ServiceRequestDetails => ({
  requestId,
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId,
  providerUserId,
  status: 'CompletedByBuyer',
  createdAt: '2026-06-14T00:00:00Z',
  auditTimeline: [
    {
      eventId: eventId('Requested', '638854560000000000'),
      requestId,
      eventType: 'Requested',
      status: 'Pending',
      occurredAt: '2026-06-14T00:00:00Z',
      actorRole: 'Buyer',
      actorUserId: buyerUserId,
    },
    {
      eventId: eventId('Accepted', '638854560600000000'),
      requestId,
      eventType: 'Accepted',
      status: 'Accepted',
      occurredAt: '2026-06-14T00:01:00Z',
      actorRole: 'Provider',
      actorUserId: providerUserId,
    },
    {
      eventId: eventId('CompletedByProvider', '638854561200000000'),
      requestId,
      eventType: 'CompletedByProvider',
      status: 'CompletedByProvider',
      occurredAt: providerCompletedAt,
      actorRole: 'Provider',
      actorUserId: providerUserId,
    },
    {
      eventId: eventId('CompletedByBuyer', '638854561800000000'),
      requestId,
      eventType: 'CompletedByBuyer',
      status: 'CompletedByBuyer',
      occurredAt: buyerConfirmedAt,
      actorRole: 'Buyer',
      actorUserId: buyerUserId,
    },
  ],
  completionEvidence: {
    requestId,
    status: 'CompletedByBuyer',
    providerCompletion: {
      evidenceId: `${requestId.replaceAll('-', '')}:ProviderCompletion:638854561200000000`,
      requestId,
      evidenceType: 'ProviderCompletion',
      occurredAt: providerCompletedAt,
      actorRole: 'Provider',
      actorUserId: providerUserId,
    },
    buyerConfirmation: {
      evidenceId: `${requestId.replaceAll('-', '')}:BuyerConfirmation:638854561800000000`,
      requestId,
      evidenceType: 'BuyerConfirmation',
      occurredAt: buyerConfirmedAt,
      actorRole: 'Buyer',
      actorUserId: buyerUserId,
    },
    notes: [],
    attachments: [],
    deliverables: [],
  },
  capabilities: {
    canAccept: false,
    canReject: false,
    canMarkCompleted: false,
    canConfirmCompletion: false,
    canCancel: false,
    canChat: false,
    canViewChatHistory: true,
    canSendChat: false,
    canReview: true,
  },
  ...overrides,
})

const timelineFor = (request: ServiceRequestDetails, actorId = buyerUserId) => {
  const lifecycle = deriveServiceRequestDetailsLifecyclePresentation(request)
  const completionState = deriveServiceRequestCompletionStatePresentation(request, lifecycle, actorId)
  return deriveCompletionTimelineAuditPresentation(request, completionState, actorId)
}

describe('service marketplace frontend completion timeline and audit authority', () => {
  it('accepts backend completion history only when audit and completion evidence agree', () => {
    expect(timelineFor(requestDetails())).toEqual([
      expect.objectContaining({ eventType: 'Requested', actorUserId: buyerUserId }),
      expect.objectContaining({ eventType: 'Accepted', actorUserId: providerUserId }),
      expect.objectContaining({ eventType: 'CompletedByProvider', occurredAt: providerCompletedAt }),
      expect.objectContaining({ eventType: 'CompletedByBuyer', occurredAt: buyerConfirmedAt }),
    ])
  })

  it('rejects actor drift, identity drift, and requested timestamp drift', () => {
    const valid = requestDetails()
    const events = valid.auditTimeline!

    expect(timelineFor(requestDetails({
      auditTimeline: [{ ...events[0], actorUserId: providerUserId }, ...events.slice(1)],
    }))).toBeNull()
    expect(timelineFor(requestDetails({
      auditTimeline: [{ ...events[0], eventId: 'frontend:Requested:1' }, ...events.slice(1)],
    }))).toBeNull()
    expect(timelineFor(requestDetails({
      auditTimeline: [{ ...events[0], occurredAt: '2026-06-14T00:00:01Z' }, ...events.slice(1)],
    }))).toBeNull()
  })

  it('rejects completion chronology that disagrees with validated completion evidence', () => {
    const valid = requestDetails()
    const events = valid.auditTimeline!

    expect(timelineFor(requestDetails({
      auditTimeline: [
        ...events.slice(0, 2),
        { ...events[2], occurredAt: '2026-06-14T00:02:30Z' },
        events[3],
      ],
    }))).toBeNull()
    expect(timelineFor(requestDetails({
      auditTimeline: [
        ...events.slice(0, 3),
        { ...events[3], actorUserId: providerUserId },
      ],
    }))).toBeNull()
  })

  it('rejects foreign actors even when a replayed DTO is otherwise coherent', () => {
    expect(timelineFor(requestDetails(), 'c73b735b-6b9b-4dd9-a781-f4555b9bbd66')).toBeNull()
  })
})

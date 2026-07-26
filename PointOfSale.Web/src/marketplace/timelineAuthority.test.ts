import { describe, expect, it } from 'vitest'
import { deriveRequestAuditTimelinePresentation } from './timelineAuthority'
import { deriveServiceRequestDetailsLifecyclePresentation } from './status'
import type { ServiceRequestDetails } from './types'

const requestDetails = (overrides: Partial<ServiceRequestDetails> = {}): ServiceRequestDetails => ({
  requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId: '13ed2b6d-c48f-4794-a210-0c7ab703e52f',
  providerUserId: 'f9c54195-8e39-4b95-a1be-3050f5f1175e',
  status: 'Accepted',
  createdAt: '2026-06-14T00:00:00Z',
  auditTimeline: [
    {
      eventId: 'requested',
      requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
      eventType: 'Requested',
      status: 'Pending',
      occurredAt: '2026-06-14T00:00:00Z',
      actorRole: 'Buyer',
      actorUserId: '13ed2b6d-c48f-4794-a210-0c7ab703e52f',
    },
    {
      eventId: 'accepted',
      requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
      eventType: 'Accepted',
      status: 'Accepted',
      occurredAt: '2026-06-14T00:01:00Z',
      actorRole: 'Provider',
      actorUserId: 'f9c54195-8e39-4b95-a1be-3050f5f1175e',
    },
  ],
  capabilities: {
    canAccept: false,
    canReject: false,
    canMarkCompleted: true,
    canConfirmCompletion: false,
    canCancel: true,
    canChat: true,
    canViewChatHistory: true,
    canSendChat: true,
    canReview: false,
  },
  ...overrides,
})

const timelineFor = (request: ServiceRequestDetails) =>
  deriveRequestAuditTimelinePresentation(request, deriveServiceRequestDetailsLifecyclePresentation(request))

describe('request timeline and audit presentation authority', () => {
  it('accepts only backend audit timeline events consistent with current lifecycle authority', () => {
    expect(timelineFor(requestDetails())).toEqual([
      expect.objectContaining({ eventType: 'Requested', statusKey: 'pending', actorRole: 'Buyer' }),
      expect.objectContaining({ eventType: 'Accepted', statusKey: 'accepted', actorRole: 'Provider' }),
    ])
  })

  it('rejects fabricated, duplicate, reordered, or foreign-request audit events', () => {
    const valid = requestDetails()
    expect(timelineFor(requestDetails({ auditTimeline: [...valid.auditTimeline!].reverse() }))).toBeNull()
    expect(timelineFor(requestDetails({ auditTimeline: [valid.auditTimeline![0], valid.auditTimeline![0]] }))).toBeNull()
    expect(timelineFor(requestDetails({ auditTimeline: [{ ...valid.auditTimeline![0], eventType: 'Fabricated' }] }))).toBeNull()
    expect(timelineFor(requestDetails({ auditTimeline: [{ ...valid.auditTimeline![0], requestId: 'foreign' }] }))).toBeNull()
  })

  it('rejects lifecycle history that contradicts the current backend status', () => {
    const valid = requestDetails()
    expect(timelineFor(requestDetails({ status: 'Accepted', auditTimeline: [valid.auditTimeline![0]] }))).toBeNull()
    expect(timelineFor(requestDetails({ status: 'Accepted', auditTimeline: [...valid.auditTimeline!, {
      eventId: 'completed',
      requestId: valid.requestId,
      eventType: 'CompletedByProvider',
      status: 'CompletedByProvider',
      occurredAt: '2026-06-14T00:02:00Z',
      actorRole: 'Provider',
      actorUserId: valid.providerUserId,
    }] }))).toBeNull()
  })
})

import { describe, expect, it } from 'vitest'
import { deriveChatIdentityAuthority, deriveChatMessagesAuthority } from './chatAuthority'
import type { ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'

const requestId = '3be1b510-0f53-4d37-907e-eabdc7fe78cb'
const serviceId = 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0'
const buyerUserId = '13ed2b6d-c48f-4794-a210-0c7ab703e52f'
const providerUserId = 'f9c54195-8e39-4b95-a1be-3050f5f1175e'

const lifecycle: ServiceRequestLifecyclePresentation = {
  statusKey: 'accepted',
  statusLabelFallback: 'Accepted',
  statusClass: 'accepted',
  canAccept: false,
  canReject: false,
  canMarkCompleted: true,
  canConfirmCompletion: false,
  canCancel: true,
  canReview: false,
  canViewChatHistory: true,
  canSendChat: true,
  canChat: true,
}

const request: ServiceRequestDetails = {
  requestId,
  serviceId,
  buyerUserId,
  providerUserId,
  status: 'Accepted',
  createdAt: '2026-06-14T00:00:00Z',
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
}

const identity = deriveChatIdentityAuthority(request, requestId, buyerUserId, lifecycle)!

const message = (
  id: string,
  createdAt: string,
  overrides: Record<string, unknown> = {},
) => ({
  id,
  serviceRequestId: requestId,
  senderUserId: buyerUserId,
  senderRole: 'Buyer',
  messageType: 'Text',
  body: 'Authoritative message',
  createdAt,
  ...overrides,
})

describe('service marketplace frontend message authority', () => {
  it('accepts backend chronology only with deterministic equal-timestamp identity order', () => {
    const firstId = '11111111-1111-4111-8111-111111111111'
    const secondId = '22222222-2222-4222-8222-222222222222'
    const timestamp = '2026-06-14T00:00:00.1234567Z'

    expect(deriveChatMessagesAuthority([
      message(firstId, timestamp),
      message(secondId, timestamp),
    ], identity)).toHaveLength(2)

    expect(deriveChatMessagesAuthority([
      message(secondId, timestamp),
      message(firstId, timestamp),
    ], identity)).toBeNull()
  })

  it('rejects frontend-shaped chronology and malformed attachment presentation', () => {
    const id = '11111111-1111-4111-8111-111111111111'

    expect(deriveChatMessagesAuthority([
      message(id, '2026-06-14 00:00:00'),
    ], identity)).toBeNull()

    expect(deriveChatMessagesAuthority([
      message(id, '2026-06-14T00:00:00Z', {
        body: '',
        attachmentId: '22222222-2222-4222-8222-222222222222',
        attachmentFileName: '../foreign.pdf',
      }),
    ], identity)).toBeNull()
  })

  it('rejects duplicate, foreign-request, and actor-misattributed message replay', () => {
    const id = '11111111-1111-4111-8111-111111111111'
    const authoritative = message(id, '2026-06-14T00:00:00Z')

    expect(deriveChatMessagesAuthority([authoritative, authoritative], identity)).toBeNull()
    expect(deriveChatMessagesAuthority([
      { ...authoritative, serviceRequestId: serviceId },
    ], identity)).toBeNull()
    expect(deriveChatMessagesAuthority([
      { ...authoritative, senderRole: 'Provider' },
    ], identity)).toBeNull()
  })
})

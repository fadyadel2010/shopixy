import { describe, expect, it } from 'vitest'
import {
  deriveChatIdentityAuthority,
  deriveChatMessagesAuthority,
  isCurrentChatRealtimeEvent,
} from './chatAuthority'
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

describe('chat identity and access authority', () => {
  it('binds route access to current backend participant identity', () => {
    expect(deriveChatIdentityAuthority(request, requestId, buyerUserId, lifecycle)).toMatchObject({
      requestId,
      buyerUserId,
      providerUserId,
      currentUserId: buyerUserId,
      currentUserRole: 'Buyer',
      canViewChatHistory: true,
      canSendChat: true,
    })

    expect(deriveChatIdentityAuthority(request, serviceId, buyerUserId, lifecycle)).toBeNull()
    expect(deriveChatIdentityAuthority(request, requestId, serviceId, lifecycle)).toBeNull()
    expect(deriveChatIdentityAuthority(
      { ...request, providerUserId: buyerUserId },
      requestId,
      buyerUserId,
      lifecycle,
    )).toBeNull()
    expect(deriveChatIdentityAuthority(
      request,
      requestId,
      buyerUserId,
      { ...lifecycle, canViewChatHistory: false, canSendChat: false, canChat: false },
    )).toBeNull()
  })

  it('accepts only ordered messages bound to authoritative participants and roles', () => {
    const identity = deriveChatIdentityAuthority(request, requestId, buyerUserId, lifecycle)!
    const messages = [
      {
        id: 'c565ad83-7255-4e0a-b04a-d0fed589ddc3',
        serviceRequestId: requestId,
        senderUserId: buyerUserId,
        senderRole: 'Buyer',
        messageType: 'Text',
        body: 'Hello',
        createdAt: '2026-06-14T00:00:00Z',
      },
      {
        id: 'bbf2607f-1f13-48f3-a1d2-6fc75fde2893',
        serviceRequestId: requestId,
        senderUserId: providerUserId,
        senderRole: 'Provider',
        messageType: 'System',
        body: 'Request accepted by provider',
        createdAt: '2026-06-14T00:01:00Z',
      },
    ]

    expect(deriveChatMessagesAuthority(messages, identity)).toEqual(messages)
    expect(deriveChatMessagesAuthority(
      [{ ...messages[0], serviceRequestId: serviceId }],
      identity,
    )).toBeNull()
    expect(deriveChatMessagesAuthority(
      [{ ...messages[0], senderRole: 'Provider' }],
      identity,
    )).toBeNull()
    expect(deriveChatMessagesAuthority(
      [{ ...messages[0], senderUserId: serviceId }],
      identity,
    )).toBeNull()
    expect(deriveChatMessagesAuthority(
      [messages[1], messages[0]],
      identity,
    )).toBeNull()
    expect(deriveChatMessagesAuthority(
      [messages[0], { ...messages[0] }],
      identity,
    )).toBeNull()
  })

  it('rejects malformed attachment and realtime identity payloads', () => {
    const identity = deriveChatIdentityAuthority(request, requestId, providerUserId, lifecycle)!
    const message = {
      id: 'c565ad83-7255-4e0a-b04a-d0fed589ddc3',
      serviceRequestId: requestId,
      senderUserId: providerUserId,
      senderRole: 'Provider',
      messageType: 'Text',
      body: '',
      createdAt: '2026-06-14T00:00:00Z',
      attachmentId: 'c565ad83-7255-4e0a-b04a-d0fed589ddc3',
      attachmentFileName: 'evidence.pdf',
    }

    expect(deriveChatMessagesAuthority([message], identity)).toEqual([message])
    expect(deriveChatMessagesAuthority(
      [{ ...message, attachmentFileName: undefined }],
      identity,
    )).toBeNull()
    expect(deriveChatMessagesAuthority(
      [{ ...message, attachmentId: 'bbf2607f-1f13-48f3-a1d2-6fc75fde2893' }],
      identity,
    )).toBeNull()
    expect(isCurrentChatRealtimeEvent(message, identity)).toBe(true)
    expect(isCurrentChatRealtimeEvent({ ...message, serviceRequestId: serviceId }, identity)).toBe(false)
    expect(isCurrentChatRealtimeEvent({ serviceRequestId: requestId }, identity)).toBe(false)
  })
})

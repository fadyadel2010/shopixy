import { describe, expect, it } from 'vitest'
import {
  deriveChatHistoryPageAuthority,
  mergeChatHistoryPagesAuthority,
  type ChatIdentityAuthority,
} from './chatAuthority'

const requestId = '3be1b510-0f53-4d37-907e-eabdc7fe78cb'
const buyerUserId = '13ed2b6d-c48f-4794-a210-0c7ab703e52f'

const identity: ChatIdentityAuthority = {
  requestId,
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId,
  providerUserId: 'f9c54195-8e39-4b95-a1be-3050f5f1175e',
  currentUserId: buyerUserId,
  currentUserRole: 'Buyer',
  canViewChatHistory: true,
  canSendChat: true,
}

const message = (id: string, createdAt: string) => ({
  id,
  serviceRequestId: requestId,
  senderUserId: buyerUserId,
  senderRole: 'Buyer',
  messageType: 'Text',
  body: id,
  createdAt,
})

const olderId = '11111111-1111-4111-8111-111111111111'
const boundaryId = '22222222-2222-4222-8222-222222222222'
const newestId = '33333333-3333-4333-8333-333333333333'

describe('service marketplace frontend chat history authority', () => {
  it('accepts backend-owned page identity and cursor metadata only', () => {
    const payload = {
      requestId,
      beforeMessageId: null,
      pageSize: 2,
      hasMore: true,
      nextBeforeMessageId: boundaryId,
      items: [
        message(boundaryId, '2026-06-14T00:01:00Z'),
        message(newestId, '2026-06-14T00:02:00Z'),
      ],
    }

    expect(deriveChatHistoryPageAuthority(payload, identity, null)).toEqual(payload)
    expect(deriveChatHistoryPageAuthority(
      { ...payload, requestId: identity.serviceId },
      identity,
      null,
    )).toBeNull()
    expect(deriveChatHistoryPageAuthority(
      { ...payload, beforeMessageId: olderId },
      identity,
      null,
    )).toBeNull()
    expect(deriveChatHistoryPageAuthority(
      { ...payload, nextBeforeMessageId: newestId },
      identity,
      null,
    )).toBeNull()
  })

  it('merges contiguous older pages without sorting, deduplicating, or repairing them', () => {
    const newestPage = deriveChatHistoryPageAuthority({
      requestId,
      beforeMessageId: null,
      pageSize: 2,
      hasMore: true,
      nextBeforeMessageId: boundaryId,
      items: [
        message(boundaryId, '2026-06-14T00:01:00Z'),
        message(newestId, '2026-06-14T00:02:00Z'),
      ],
    }, identity, null)!
    const olderPage = deriveChatHistoryPageAuthority({
      requestId,
      beforeMessageId: boundaryId,
      pageSize: 2,
      hasMore: false,
      nextBeforeMessageId: null,
      items: [
        message(olderId, '2026-06-14T00:00:00Z'),
      ],
    }, identity, boundaryId)!

    expect(mergeChatHistoryPagesAuthority([newestPage, olderPage], identity)?.map((row) => row.id))
      .toEqual([olderId, boundaryId, newestId])
    expect(mergeChatHistoryPagesAuthority([
      newestPage,
      { ...olderPage, beforeMessageId: newestId },
    ], identity)).toBeNull()
    expect(mergeChatHistoryPagesAuthority([
      newestPage,
      { ...olderPage, items: [newestPage.items[0]] },
    ], identity)).toBeNull()
  })

  it('rejects truncated, fabricated, and contradictory page windows', () => {
    expect(deriveChatHistoryPageAuthority({
      requestId,
      beforeMessageId: null,
      pageSize: 2,
      hasMore: true,
      nextBeforeMessageId: olderId,
      items: [message(olderId, '2026-06-14T00:00:00Z')],
    }, identity, null)).toBeNull()

    expect(deriveChatHistoryPageAuthority({
      requestId,
      beforeMessageId: null,
      pageSize: 2,
      hasMore: false,
      nextBeforeMessageId: olderId,
      items: [message(olderId, '2026-06-14T00:00:00Z')],
    }, identity, null)).toBeNull()
  })
})

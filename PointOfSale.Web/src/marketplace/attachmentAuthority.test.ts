import { describe, expect, it } from 'vitest'
import {
  buildAttachmentDownloadPath,
  deriveChatAttachmentUploadAuthority,
  normalizeAttachmentFileName,
} from './attachmentAuthority'
import type { ChatIdentityAuthority } from './chatAuthority'

const identity: ChatIdentityAuthority = {
  requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId: '13ed2b6d-c48f-4794-a210-0c7ab703e52f',
  providerUserId: 'f9c54195-8e39-4b95-a1be-3050f5f1175e',
  currentUserId: '13ed2b6d-c48f-4794-a210-0c7ab703e52f',
  currentUserRole: 'Buyer',
  canViewChatHistory: true,
  canSendChat: true,
}

const payload = {
  objectKey: `tenants/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/marketplace-chat-attachments/${identity.requestId}/${identity.currentUserId}/evidence.pdf`,
  fileName: 'evidence.pdf',
}

describe('service marketplace frontend attachment authority', () => {
  it('accepts only upload responses bound to the current request and actor', () => {
    expect(deriveChatAttachmentUploadAuthority(payload, identity)).toEqual(payload)

    expect(deriveChatAttachmentUploadAuthority({
      ...payload,
      objectKey: payload.objectKey.replace(identity.requestId, identity.serviceId),
    }, identity)).toBeNull()

    expect(deriveChatAttachmentUploadAuthority({
      ...payload,
      objectKey: payload.objectKey.replace(identity.currentUserId, identity.providerUserId),
    }, identity)).toBeNull()
  })

  it('rejects URL, traversal, filename, and malformed tenant replay', () => {
    expect(deriveChatAttachmentUploadAuthority({
      ...payload,
      objectKey: `https://evil.example/${payload.objectKey}`,
    }, identity)).toBeNull()
    expect(deriveChatAttachmentUploadAuthority({
      ...payload,
      objectKey: payload.objectKey.replace('evidence.pdf', '../evidence.pdf'),
    }, identity)).toBeNull()
    expect(deriveChatAttachmentUploadAuthority({
      ...payload,
      fileName: 'other.pdf',
    }, identity)).toBeNull()
    expect(deriveChatAttachmentUploadAuthority({
      ...payload,
      objectKey: payload.objectKey.replace('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'not-a-tenant'),
    }, identity)).toBeNull()
  })

  it('keeps filename presentation bounded and download routes server-authorized', () => {
    expect(normalizeAttachmentFileName('evidence.pdf')).toBe('evidence.pdf')
    expect(normalizeAttachmentFileName(' ../evidence.pdf')).toBeNull()
    expect(normalizeAttachmentFileName('folder/evidence.pdf')).toBeNull()
    expect(buildAttachmentDownloadPath('11111111-1111-4111-8111-111111111111'))
      .toBe('/api/marketplace/attachments/11111111-1111-4111-8111-111111111111')
  })
})

import type { ChatIdentityAuthority } from './chatAuthority'
import { containsAsciiControl } from '../security/textCharacterPolicy'

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const normalizeGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) && normalized !== '00000000-0000-0000-0000-000000000000'
    ? normalized
    : null
}

export const normalizeAttachmentFileName = (value: unknown) => {
  if (typeof value !== 'string' || value.trim() !== value) {
    return null
  }

  return value.length > 0
    && value.length <= 255
    && !containsAsciiControl(value)
    && !value.includes('/') && !value.includes('\\')
    && value !== '.'
    && value !== '..'
      ? value
      : null
}

export const buildAttachmentDownloadPath = (attachmentId: string) =>
  `/api/marketplace/attachments/${encodeURIComponent(attachmentId)}`

export function deriveChatAttachmentUploadAuthority(
  payload: unknown,
  identity: ChatIdentityAuthority,
) {
  if (!payload || typeof payload !== 'object') {
    return null
  }

  const row = payload as Record<string, unknown>
  if (
    typeof row.objectKey !== 'string'
    || row.objectKey.trim() !== row.objectKey
    || row.objectKey.includes('\\')
    || row.objectKey.includes('..')
    || row.objectKey.includes('?')
    || row.objectKey.includes('#')
  ) {
    return null
  }

  const segments = row.objectKey.split('/')
  if (
    segments.length !== 6
    || segments[0] !== 'tenants'
    || !normalizeGuid(segments[1])
    || segments[2] !== 'marketplace-chat-attachments'
    || normalizeGuid(segments[3]) !== identity.requestId
    || normalizeGuid(segments[4]) !== identity.currentUserId
  ) {
    return null
  }

  const fileName = normalizeAttachmentFileName(row.fileName)
  if (!fileName || segments[5] !== fileName) {
    return null
  }

  return Object.freeze({
    objectKey: row.objectKey,
    fileName,
  })
}

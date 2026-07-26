import type {
  ChatHistoryPage,
  ChatMessage,
  ServiceRequestDetails,
} from './types'
import type { ServiceRequestLifecyclePresentation } from './status'
import { normalizeAttachmentFileName } from './attachmentAuthority'

export type ChatParticipantRole = 'Buyer' | 'Provider'

export type ChatIdentityAuthority = Readonly<{
  requestId: string
  serviceId: string
  buyerUserId: string
  providerUserId: string
  currentUserId: string
  currentUserRole: ChatParticipantRole
  canViewChatHistory: boolean
  canSendChat: boolean
}>

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const isoTimestampPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/

const normalizeGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) && normalized !== '00000000-0000-0000-0000-000000000000'
    ? normalized
    : null
}

const normalizeDate = (value: unknown) => {
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

export function deriveChatIdentityAuthority(
  request: Pick<ServiceRequestDetails, 'requestId' | 'serviceId' | 'buyerUserId' | 'providerUserId'>,
  routeRequestId: unknown,
  currentUserId: unknown,
  lifecycle: ServiceRequestLifecyclePresentation | null,
): ChatIdentityAuthority | null {
  const requestId = normalizeGuid(request.requestId)
  const routeId = normalizeGuid(routeRequestId)
  const serviceId = normalizeGuid(request.serviceId)
  const buyerUserId = normalizeGuid(request.buyerUserId)
  const providerUserId = normalizeGuid(request.providerUserId)
  const actorUserId = normalizeGuid(currentUserId)

  if (
    !lifecycle
    || !lifecycle.canViewChatHistory
    || !requestId
    || requestId !== routeId
    || !serviceId
    || !buyerUserId
    || !providerUserId
    || buyerUserId === providerUserId
    || !actorUserId
  ) {
    return null
  }

  const currentUserRole = actorUserId === buyerUserId
    ? 'Buyer'
    : actorUserId === providerUserId
      ? 'Provider'
      : null

  if (!currentUserRole) {
    return null
  }

  return {
    requestId,
    serviceId,
    buyerUserId,
    providerUserId,
    currentUserId: actorUserId,
    currentUserRole,
    canViewChatHistory: true,
    canSendChat: lifecycle.canSendChat,
  }
}

export function deriveChatMessagesAuthority(
  payload: unknown,
  identity: ChatIdentityAuthority,
): readonly ChatMessage[] | null {
  if (!Array.isArray(payload)) {
    return null
  }

  const messageIds = new Set<string>()
  const messages: ChatMessage[] = []
  let previousTimestamp = Number.NEGATIVE_INFINITY
  let previousMessageId = ''

  for (const candidate of payload) {
    if (!candidate || typeof candidate !== 'object') {
      return null
    }

    const row = candidate as Record<string, unknown>
    const id = normalizeGuid(row.id)
    const serviceRequestId = normalizeGuid(row.serviceRequestId)
    const senderUserId = normalizeGuid(row.senderUserId)
    const senderRole = row.senderRole === 'Buyer' || row.senderRole === 'Provider'
      ? row.senderRole
      : null
    const messageType = row.messageType === 'Text' || row.messageType === 'System'
      ? row.messageType
      : null
    const createdTimestamp = normalizeDate(row.createdAt)
    const body = typeof row.body === 'string' ? row.body : null

    if (
      !id
      || messageIds.has(id)
      || serviceRequestId !== identity.requestId
      || !senderUserId
      || !senderRole
      || !messageType
      || createdTimestamp === null
      || createdTimestamp < previousTimestamp
      || (createdTimestamp === previousTimestamp && id <= previousMessageId)
      || body === null
    ) {
      return null
    }

    const expectedSenderRole = senderUserId === identity.buyerUserId
      ? 'Buyer'
      : senderUserId === identity.providerUserId
        ? 'Provider'
        : null

    if (!expectedSenderRole || senderRole !== expectedSenderRole) {
      return null
    }

    if (messageType === 'System' && expectedSenderRole !== 'Provider') {
      return null
    }

    const attachmentId = row.attachmentId === null || row.attachmentId === undefined
      ? undefined
      : normalizeGuid(row.attachmentId)
    const attachmentFileName = row.attachmentFileName === null || row.attachmentFileName === undefined
      ? undefined
      : normalizeAttachmentFileName(row.attachmentFileName)

    if (
      attachmentId === null
      || attachmentFileName === null
      || (!!attachmentId !== !!attachmentFileName)
      || (attachmentId !== undefined && attachmentId !== id)
      || (messageType === 'Text' && body.trim().length === 0 && !attachmentId)
      || (messageType === 'System' && (body.trim().length === 0 || attachmentId))
    ) {
      return null
    }

    messageIds.add(id)
    previousTimestamp = createdTimestamp
    previousMessageId = id
    messages.push({
      id,
      serviceRequestId,
      senderUserId,
      senderRole,
      messageType,
      body,
      createdAt: row.createdAt as string,
      attachmentId,
      attachmentFileName,
    })
  }

  return messages
}

export function deriveChatHistoryPageAuthority(
  payload: unknown,
  identity: ChatIdentityAuthority,
  expectedBeforeMessageId: string | null,
): ChatHistoryPage | null {
  if (!payload || typeof payload !== 'object') {
    return null
  }

  const page = payload as Record<string, unknown>
  if (
    !Object.prototype.hasOwnProperty.call(page, 'beforeMessageId')
    || !Object.prototype.hasOwnProperty.call(page, 'nextBeforeMessageId')
  ) {
    return null
  }

  const requestId = normalizeGuid(page.requestId)
  const beforeMessageId = page.beforeMessageId === null
    ? null
    : normalizeGuid(page.beforeMessageId)
  const nextBeforeMessageId = page.nextBeforeMessageId === null
    ? null
    : normalizeGuid(page.nextBeforeMessageId)
  const pageSize = page.pageSize
  const hasMore = page.hasMore
  const items = deriveChatMessagesAuthority(page.items, identity)

  if (
    requestId !== identity.requestId
    || beforeMessageId !== expectedBeforeMessageId
    || !Number.isInteger(pageSize)
    || (pageSize as number) < 1
    || (pageSize as number) > 100
    || typeof hasMore !== 'boolean'
    || !items
    || items.length > (pageSize as number)
  ) {
    return null
  }

  if (
    (hasMore && (items.length !== pageSize || nextBeforeMessageId !== items[0]?.id))
    || (!hasMore && nextBeforeMessageId !== null)
    || (items.length === 0 && hasMore)
  ) {
    return null
  }

  return {
    requestId,
    beforeMessageId,
    pageSize: pageSize as number,
    hasMore,
    nextBeforeMessageId,
    items,
  }
}

export function mergeChatHistoryPagesAuthority(
  pages: readonly ChatHistoryPage[],
  identity: ChatIdentityAuthority,
): readonly ChatMessage[] | null {
  if (
    pages.length === 0
    || pages[0].beforeMessageId !== null
    || pages.some((page) => page.requestId !== identity.requestId)
  ) {
    return null
  }

  for (let index = 1; index < pages.length; index += 1) {
    const newerPage = pages[index - 1]
    const olderPage = pages[index]
    if (
      !newerPage.hasMore
      || !newerPage.nextBeforeMessageId
      || olderPage.beforeMessageId !== newerPage.nextBeforeMessageId
    ) {
      return null
    }
  }

  return deriveChatMessagesAuthority(
    [...pages].reverse().flatMap((page) => page.items),
    identity,
  )
}

export function isCurrentChatRealtimeEvent(
  payload: unknown,
  identity: ChatIdentityAuthority,
) {
  if (!payload || typeof payload !== 'object') {
    return false
  }

  const row = payload as Record<string, unknown>
  return normalizeGuid(row.id) !== null
    && normalizeGuid(row.serviceRequestId) === identity.requestId
}

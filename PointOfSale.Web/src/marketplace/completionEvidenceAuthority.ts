import type { ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'

type CompletionEvidenceKind = 'ProviderCompletion' | 'BuyerConfirmation'
type CompletionEvidenceRole = 'Provider' | 'Buyer'

export type CompletionEvidencePhasePresentation = Readonly<{
  evidenceId: string
  evidenceType: CompletionEvidenceKind
  occurredAt: string
  actorRole: CompletionEvidenceRole
  actorUserId: string
}>

export type CompletionEvidencePresentation = Readonly<{
  requestId: string
  status: 'completedByProvider' | 'completedByBuyer'
  providerCompletion: CompletionEvidencePhasePresentation
  buyerConfirmation: CompletionEvidencePhasePresentation | null
  notes: readonly never[]
  attachments: readonly never[]
  deliverables: readonly never[]
}>

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const isoTimestampPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/

const normalizeGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) && normalized !== '00000000-0000-0000-0000-000000000000'
    ? normalized
    : null
}

const normalizeTimestamp = (value: unknown) => {
  if (
    typeof value !== 'string'
    || value.trim() !== value
    || !isoTimestampPattern.test(value)
    || !Number.isFinite(Date.parse(value))
  ) {
    return null
  }

  return value
}

const derivePhase = (
  payload: unknown,
  requestId: string,
  expectedType: CompletionEvidenceKind,
  expectedRole: CompletionEvidenceRole,
  expectedActorUserId: string,
): CompletionEvidencePhasePresentation | null => {
  if (!payload || typeof payload !== 'object') {
    return null
  }

  const row = payload as Record<string, unknown>
  const evidenceId = typeof row.evidenceId === 'string' && row.evidenceId.trim() === row.evidenceId
    ? row.evidenceId
    : null
  const occurredAt = normalizeTimestamp(row.occurredAt)

  if (
    !evidenceId
    || evidenceId.length > 200
    || normalizeGuid(row.requestId) !== requestId
    || row.evidenceType !== expectedType
    || row.actorRole !== expectedRole
    || normalizeGuid(row.actorUserId) !== expectedActorUserId
    || !occurredAt
  ) {
    return null
  }

  return {
    evidenceId,
    evidenceType: expectedType,
    occurredAt,
    actorRole: expectedRole,
    actorUserId: expectedActorUserId,
  }
}

export function deriveCompletionEvidencePresentation(
  request: Pick<
    ServiceRequestDetails,
    'requestId' | 'buyerUserId' | 'providerUserId' | 'completionEvidence'
  >,
  lifecycle: ServiceRequestLifecyclePresentation | null,
  currentUserId: unknown,
): CompletionEvidencePresentation | null {
  if (
    !lifecycle
    || (lifecycle.statusKey !== 'completedByProvider' && lifecycle.statusKey !== 'completedByBuyer')
    || !request.completionEvidence
  ) {
    return null
  }

  const requestId = normalizeGuid(request.requestId)
  const buyerUserId = normalizeGuid(request.buyerUserId)
  const providerUserId = normalizeGuid(request.providerUserId)
  const actorUserId = normalizeGuid(currentUserId)
  const evidence = request.completionEvidence

  if (
    !requestId
    || !buyerUserId
    || !providerUserId
    || buyerUserId === providerUserId
    || (actorUserId !== buyerUserId && actorUserId !== providerUserId)
    || normalizeGuid(evidence.requestId) !== requestId
    || evidence.status !== (
      lifecycle.statusKey === 'completedByProvider'
        ? 'CompletedByProvider'
        : 'CompletedByBuyer'
    )
    || !Array.isArray(evidence.notes)
    || evidence.notes.length !== 0
    || !Array.isArray(evidence.attachments)
    || evidence.attachments.length !== 0
    || !Array.isArray(evidence.deliverables)
    || evidence.deliverables.length !== 0
  ) {
    return null
  }

  const providerCompletion = derivePhase(
    evidence.providerCompletion,
    requestId,
    'ProviderCompletion',
    'Provider',
    providerUserId,
  )
  if (!providerCompletion) {
    return null
  }

  const buyerConfirmation = evidence.buyerConfirmation === null
    ? null
    : derivePhase(
        evidence.buyerConfirmation,
        requestId,
        'BuyerConfirmation',
        'Buyer',
        buyerUserId,
      )

  if (
    lifecycle.statusKey === 'completedByProvider'
      ? buyerConfirmation !== null
      : !buyerConfirmation
        || Date.parse(buyerConfirmation.occurredAt) < Date.parse(providerCompletion.occurredAt)
        || buyerConfirmation.evidenceId === providerCompletion.evidenceId
  ) {
    return null
  }

  return {
    requestId,
    status: lifecycle.statusKey,
    providerCompletion,
    buyerConfirmation,
    notes: [],
    attachments: [],
    deliverables: [],
  }
}

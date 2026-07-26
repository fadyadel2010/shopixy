const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const nonEmpty = (value: unknown) => {
  const normalized = typeof value === 'string' ? value.trim() : ''
  return normalized || null
}

export type CreatedServiceRequestAuthority = Readonly<{
  requestId: string
  serviceId: string
  status: 'Pending'
  createdAt: string | null
}>

export const deriveCreatedServiceRequestAuthority = (
  response: unknown,
  submittedServiceId: string,
): CreatedServiceRequestAuthority | null => {
  const dto = response as {
    requestId?: unknown
    serviceId?: unknown
    status?: unknown
    createdAt?: unknown
  } | null | undefined

  const requestId = nonEmpty(dto?.requestId)
  const responseServiceId = nonEmpty(dto?.serviceId)
  const submittedId = nonEmpty(submittedServiceId)
  const status = nonEmpty(dto?.status)
  const createdAt = nonEmpty(dto?.createdAt)

  if (
    !requestId
    || !uuidPattern.test(requestId)
    || !responseServiceId
    || !submittedId
    || responseServiceId.toLowerCase() !== submittedId.toLowerCase()
    || status !== 'Pending'
  ) {
    return null
  }

  return {
    requestId,
    serviceId: responseServiceId,
    status: 'Pending',
    createdAt: createdAt && Number.isFinite(Date.parse(createdAt)) ? createdAt : null,
  }
}

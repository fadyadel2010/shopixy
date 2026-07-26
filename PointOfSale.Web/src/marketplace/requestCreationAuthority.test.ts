import { describe, expect, it } from 'vitest'
import { deriveCreatedServiceRequestAuthority } from './requestCreationAuthority'

const createdResponse = (overrides: Record<string, unknown> = {}) => ({
  requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  status: 'Pending',
  createdAt: '2026-06-14T00:00:00Z',
  ...overrides,
})

describe('deriveCreatedServiceRequestAuthority', () => {
  it('accepts only a backend-created pending request for the submitted service', () => {
    expect(deriveCreatedServiceRequestAuthority(
      createdResponse(),
      'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
    )).toEqual({
      requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
      serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
      status: 'Pending',
      createdAt: '2026-06-14T00:00:00Z',
    })
  })

  it('rejects malformed, stale, or cross-service success-shaped responses', () => {
    expect(deriveCreatedServiceRequestAuthority(
      createdResponse({ requestId: '' }),
      'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
    )).toBeNull()

    expect(deriveCreatedServiceRequestAuthority(
      createdResponse({ serviceId: '53aca368-3f1d-4e49-b45e-f9de34d1aa92' }),
      'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
    )).toBeNull()

    expect(deriveCreatedServiceRequestAuthority(
      createdResponse({ status: 'Accepted' }),
      'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
    )).toBeNull()
  })
})

import { describe, expect, it } from 'vitest'
import { buildIdempotencyHeaders } from './requestSafety'

describe('request safety headers', () => {
    it('includes the POS cart id only when supplied', () => {
        expect(buildIdempotencyHeaders('idem-1', 'attempt-1', 'cart-1')).toEqual({
            'Idempotency-Key': 'idem-1',
            'X-Request-Id': 'idem-1',
            'X-Payment-Attempt-Id': 'attempt-1',
            'X-Pos-Cart-Id': 'cart-1',
        })

        expect(buildIdempotencyHeaders('idem-1', 'attempt-1')).not.toHaveProperty('X-Pos-Cart-Id')
    })
})

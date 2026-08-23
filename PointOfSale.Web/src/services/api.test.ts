import { describe, expect, it } from 'vitest'
import api, {
    API_REQUEST_TIMEOUT_MESSAGE,
    API_REQUEST_TIMEOUT_MS,
    getRetryDelayMs,
    getSafeApiErrorMessage,
    isRetryableApiError,
    isRequestTimeout,
} from './api'

describe('admin api client timeout hardening', () => {
    it('sets a finite request timeout on the shared axios client', () => {
        expect(api.defaults.timeout).toBe(API_REQUEST_TIMEOUT_MS)
        expect(api.defaults.timeoutErrorMessage).toBe(API_REQUEST_TIMEOUT_MESSAGE)
    })

    it('detects timeout failures consistently', () => {
        expect(isRequestTimeout({ code: 'ECONNABORTED' })).toBe(true)
        expect(isRequestTimeout({ code: 'ETIMEDOUT' })).toBe(true)
        expect(isRequestTimeout({ message: API_REQUEST_TIMEOUT_MESSAGE })).toBe(true)
        expect(isRequestTimeout({ message: 'timeout of 30000ms exceeded' })).toBe(true)
        expect(isRequestTimeout({ code: 'ERR_CANCELED' })).toBe(false)
    })

    it('marks only transient failures as retryable', () => {
        expect(isRetryableApiError({ response: { status: 503 } })).toBe(true)
        expect(isRetryableApiError({ response: { status: 429 } })).toBe(true)
        expect(isRetryableApiError({ code: 'ECONNABORTED' })).toBe(true)
        expect(isRetryableApiError({ response: { status: 400 } })).toBe(false)
        expect(isRetryableApiError({ code: 'ERR_CANCELED' })).toBe(false)
    })

    it('honors Retry-After while capping retry delays', () => {
        expect(getRetryDelayMs(1, { response: { headers: { 'retry-after': '1' } } })).toBe(1000)
        expect(getRetryDelayMs(1, { response: { headers: { 'retry-after': '60' } } })).toBe(2000)
    })

    it('does not expose unsafe backend details in production-facing messages', () => {
        expect(getSafeApiErrorMessage({
            response: {
                status: 500,
                data: { message: 'System.Exception: SQL SELECT * FROM Users stack trace' },
            },
        })).toBe('Service is temporarily unavailable. Please try again.')

        expect(getSafeApiErrorMessage({
            response: {
                status: 409,
                data: { message: 'Insufficient stock' },
            },
        })).toBe('Insufficient stock')
    })

    it('can keep an idempotency key out of the request correlation header', () => {
        const requestHandlers = (api.interceptors.request as unknown as {
            handlers: Array<{ fulfilled: (config: Record<string, unknown>) => Record<string, unknown> }>
        }).handlers
        const applyRequestInterceptor = requestHandlers.at(-1)?.fulfilled

        expect(applyRequestInterceptor).toBeTypeOf('function')

        const config = applyRequestInterceptor?.({
            method: 'post',
            headers: { 'Idempotency-Key': 'sensitive-bulk-key' },
            suppressIdempotencyRequestIdMirroring: true,
        }) as { headers: Record<string, string> }

        expect(config.headers['Idempotency-Key']).toBe('sensitive-bulk-key')
        expect(config.headers['X-Request-Id']).toBeUndefined()
    })
})

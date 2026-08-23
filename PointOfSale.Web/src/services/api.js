import axios from 'axios'
import { normalizeDocumentLanguage } from '../i18n/documentLanguage'
import { appendCsrfHeader } from '../utils/csrf'
import { clearLegacyPosTokenStorage, useAuthStore } from '../store/authStore'
import { invalidatePosRuntimeCache } from './posRuntimeCache'
import {
    dispatchPosVendorSessionAuthorityLost,
    isCommerceHubVendorSessionAuthorityEndpoint,
    requirePosVendorSessionUserAuthority,
} from '../auth/posVendorSessionAuthority'

export const API_REQUEST_TIMEOUT_MS = 30000
export const API_REQUEST_TIMEOUT_MESSAGE = 'Request timeout after 30 seconds'
export const API_RETRY_MAX_ATTEMPTS = 2
export const API_RETRY_BASE_DELAY_MS = 250
export const API_RETRY_MAX_DELAY_MS = 2000

const IDEMPOTENT_RETRY_METHODS = new Set(['get', 'head', 'options'])
const RETRYABLE_STATUS_CODES = new Set([408, 429, 500, 502, 503, 504])

const resolveRequestLanguage = () => {
    const persistedLanguage = typeof localStorage !== 'undefined'
        ? localStorage.getItem('i18nextLng')
        : ''
    const documentLanguage = typeof document !== 'undefined'
        ? document.documentElement?.lang || document.body?.getAttribute('lang') || ''
        : ''
    const requestLanguage = documentLanguage || persistedLanguage

    return normalizeDocumentLanguage(requestLanguage)
}

const api = axios.create({
    baseURL: '/api',
    timeout: API_REQUEST_TIMEOUT_MS,
    timeoutErrorMessage: API_REQUEST_TIMEOUT_MESSAGE,
    headers: {
        'Content-Type': 'application/json',
    },
    withCredentials: true,
})

let refreshPromise = null
let logoutPromise = null

const isAuthEndpoint = (url = '') => (
    url.includes('/auth/login')
    || url.includes('/auth/register')
    || url.includes('/auth/session')
    || url.includes('/auth/refresh')
    || url.includes('/auth/logout')
)

const clearPosAuthStorage = () => {
    invalidatePosRuntimeCache({ reason: 'auth-cleared', broadcast: false })
    clearLegacyPosTokenStorage()
    useAuthStore.getState().logout()
}

const forceLogoutOnce = () => {
    if (!logoutPromise) {
        logoutPromise = Promise.resolve()
            .then(() => {
                clearPosAuthStorage()

                // Normalize path by removing trailing slash
                const normalizedPath = window.location.pathname.replace(/\/$/, '')
                
                // Strip locale prefix (e.g., /en, /ar) to avoid treating locale-prefixed protected routes as public
                const localeMatch = normalizedPath.match(/^\/[a-z]{2}(\/|$)/)
                const pathWithoutLocale = localeMatch 
                    ? normalizedPath.slice(3) || '/' // Remove "/xx" prefix, default to "/" if empty
                    : normalizedPath
                
                const isNonPosPublicPath =
                    pathWithoutLocale === '/login'
                    || pathWithoutLocale === '/register'
                    || pathWithoutLocale === '/verify-email'
                    || pathWithoutLocale.startsWith('/affiliate')
                    || pathWithoutLocale.startsWith('/super-admin')

                if (!isNonPosPublicPath) {
                    window.location.href = '/login'
                }
            })
            .finally(() => {
                logoutPromise = null
            })
    }

    return logoutPromise
}

const refreshAccessToken = async () => {
    if (!refreshPromise) {
        refreshPromise = (async () => {
            await axios.post(
                '/api/auth/refresh',
                {},
                {
                    withCredentials: true,
                    timeout: API_REQUEST_TIMEOUT_MS,
                    timeoutErrorMessage: API_REQUEST_TIMEOUT_MESSAGE,
                    headers: appendCsrfHeader(
                        {
                            'Content-Type': 'application/json',
                        },
                        'post',
                    ),
                },
            )

            const sessionResponse = await axios.get('/api/auth/session', {
                withCredentials: true,
                timeout: API_REQUEST_TIMEOUT_MS,
                timeoutErrorMessage: API_REQUEST_TIMEOUT_MESSAGE,
                headers: {
                    'Content-Type': 'application/json',
                },
            })

            useAuthStore.getState().hydrateSession(
                requirePosVendorSessionUserAuthority(sessionResponse.data?.user ?? null),
            )
        })().finally(() => {
            refreshPromise = null
        })
    }

    return refreshPromise
}

export const isRequestCanceled = (error) => (
    axios.isCancel?.(error)
    || error?.code === 'ERR_CANCELED'
    || error?.name === 'CanceledError'
)

export const isRequestTimeout = (error) => (
    error?.code === 'ECONNABORTED'
    || error?.code === 'ETIMEDOUT'
    || error?.message === API_REQUEST_TIMEOUT_MESSAGE
    || (typeof error?.message === 'string' && error.message.toLowerCase().includes('timeout'))
)

export const isRetryableApiError = (error) => {
    if (isRequestCanceled(error)) {
        return false
    }

    if (isRequestTimeout(error) || error?.code === 'ERR_NETWORK') {
        return true
    }

    return RETRYABLE_STATUS_CODES.has(error?.response?.status)
}

const getRetryAfterDelayMs = (retryAfterHeader) => {
    if (!retryAfterHeader) {
        return null
    }

    const retryAfterSeconds = Number(retryAfterHeader)
    if (Number.isFinite(retryAfterSeconds) && retryAfterSeconds >= 0) {
        return Math.min(API_RETRY_MAX_DELAY_MS, retryAfterSeconds * 1000)
    }

    const retryAt = Date.parse(retryAfterHeader)
    if (Number.isFinite(retryAt)) {
        return Math.min(API_RETRY_MAX_DELAY_MS, Math.max(0, retryAt - Date.now()))
    }

    return null
}

export const getRetryDelayMs = (attempt, error) => {
    const retryAfterDelay = getRetryAfterDelayMs(error?.response?.headers?.['retry-after'])
    if (retryAfterDelay != null) {
        return retryAfterDelay
    }

    const exponentialDelay = API_RETRY_BASE_DELAY_MS * (2 ** Math.max(0, attempt - 1))
    return Math.min(API_RETRY_MAX_DELAY_MS, exponentialDelay)
}

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

const shouldRetryRequest = (error) => {
    const config = error?.config
    const method = String(config?.method || 'get').toLowerCase()

    if (!config || isAuthEndpoint(config.url) || !IDEMPOTENT_RETRY_METHODS.has(method)) {
        return false
    }

    if (!isRetryableApiError(error)) {
        return false
    }

    const retryCount = Number(config.__retryCount || 0)
    return retryCount < API_RETRY_MAX_ATTEMPTS
}

const BACKEND_DETAIL_LEAK_PATTERNS = [
    /exception/i,
    /stack trace/i,
    /\bat\s+\S+\(/i,
    /sql/i,
    /select\s+.+\s+from/i,
    /system\./i,
]

const isSafeBackendMessage = (message) => (
    typeof message === 'string'
    && message.trim().length > 0
    && message.length <= 180
    && !BACKEND_DETAIL_LEAK_PATTERNS.some((pattern) => pattern.test(message))
)

export const getSafeApiErrorMessage = (
    error,
    fallback = 'Request failed. Please try again.',
) => {
    if (isRequestTimeout(error)) {
        return 'Request timed out. Please verify the latest data before trying again.'
    }

    if (isRequestCanceled(error)) {
        return 'Request was canceled.'
    }

    const status = error?.response?.status
    const backendMessage = error?.response?.data?.message

    if ((status === 400 || status === 409 || status === 422) && isSafeBackendMessage(backendMessage)) {
        return backendMessage.trim()
    }

    if (status === 401) {
        return 'Your session expired. Please sign in again.'
    }

    if (status === 403) {
        return 'You do not have permission to perform this action.'
    }

    if (status === 404) {
        return 'The requested record was not found.'
    }

    if (status === 429) {
        return 'Too many requests. Please wait and try again.'
    }

    if (status >= 500) {
        return 'Service is temporarily unavailable. Please try again.'
    }

    return fallback
}

api.interceptors.request.use(
    (config) => {
        config.headers = config.headers || {}
        config.withCredentials = true
        const idempotencyKey = config.idempotencyKey || config.headers?.['Idempotency-Key'] || config.headers?.['X-Request-Id']
        if (idempotencyKey) {
            config.headers['Idempotency-Key'] = idempotencyKey
            if (!config.suppressIdempotencyRequestIdMirroring) {
                config.headers['X-Request-Id'] = idempotencyKey
            }
        }

        config.headers['Accept-Language'] = resolveRequestLanguage()
        config.headers = appendCsrfHeader(config.headers, config.method)

        return config
    },
    (error) => Promise.reject(error),
)

api.interceptors.response.use(
    (response) => {
        if (import.meta.env.DEV) {
            const url = response.config.url || ''
            if (url.includes('/cashbox') && !url.includes('/treasury')) {
                console.warn('LEGACY CASHBOX API USED:', url, '- Migrate to /api/treasury/*')
            }
            if (url.includes('/visa') && !url.includes('/treasury')) {
                console.warn('LEGACY VISA API USED:', url, '- Migrate to /api/treasury/*')
            }
            if (url.includes('/bank') && !url.includes('/treasury')) {
                console.warn('LEGACY BANK API USED:', url, '- Migrate to /api/treasury/*')
            }
        }
        return response
    },
    async (error) => {
        const originalRequest = error.config

        if (!originalRequest || isAuthEndpoint(originalRequest.url)) {
            return Promise.reject(error)
        }

        if (
            (error.response?.status === 401 || error.response?.status === 403)
            && isCommerceHubVendorSessionAuthorityEndpoint(originalRequest.url)
        ) {
            dispatchPosVendorSessionAuthorityLost()
        }

        if (error.response?.status === 401 && !originalRequest._retry) {
            originalRequest._retry = true

            try {
                await refreshAccessToken()
                return api(originalRequest)
            } catch (refreshError) {
                if (import.meta.env.DEV) {
                    // Axios errors can retain request headers and transport
                    // diagnostics. Keep development logging useful without
                    // exposing authorization metadata or raw backend detail.
                    console.error('Token refresh failed')
                }
                await forceLogoutOnce()
                return Promise.reject(refreshError)
            }
        }

        if (shouldRetryRequest(error)) {
            originalRequest.__retryCount = Number(originalRequest.__retryCount || 0) + 1
            await delay(getRetryDelayMs(originalRequest.__retryCount, error))
            return api(originalRequest)
        }

        return Promise.reject(error)
    },
)

export default api

/**
 * Affiliate Auth API Service
 *
 * Uses HttpOnly cookies for session tokens. The browser handles token transport
 * automatically; JavaScript only keeps non-sensitive profile metadata.
 *
 * Per-request auth-failure policy (Axios config key `affiliateAuthFailurePolicy`):
 * - `terminal-logout` (default): a terminal 401 clears local cache and navigates
 *   to the login page once.
 * - `caller-owned-passive`: the caller owns session settlement (e.g. passive
 *   session probes); failures clear local cache and reject without navigating,
 *   so public login/register pages settle unauthenticated instead of looping.
 */
import axios from 'axios'
import { appendCsrfHeader } from '../utils/csrf'

export const AFFILIATE_AUTH_REQUEST_TIMEOUT_MS = 30000
export const AFFILIATE_AUTH_REQUEST_TIMEOUT_MESSAGE = 'Affiliate auth request timeout after 30 seconds'

const affiliateAxios = axios.create({
    baseURL: '/api',
    timeout: AFFILIATE_AUTH_REQUEST_TIMEOUT_MS,
    timeoutErrorMessage: AFFILIATE_AUTH_REQUEST_TIMEOUT_MESSAGE,
    headers: {
        'Content-Type': 'application/json',
    },
    withCredentials: true,
})

const STORAGE_KEYS = {
    ACCOUNT_ID: 'affiliate_accountId',
    EMAIL: 'affiliate_email',
    DISPLAY_NAME: 'affiliate_displayName',
    REFERRAL_CODE: 'affiliate_referralCode',
}

export const AFFILIATE_AUTH_STORAGE_KEYS = STORAGE_KEYS
export const AFFILIATE_AUTH_SESSION_EVENT_KEY = 'affiliate_session_event'

export const AFFILIATE_AUTH_FAILURE_POLICY = {
    TERMINAL_LOGOUT: 'terminal-logout',
    CALLER_OWNED_PASSIVE: 'caller-owned-passive',
}

const AFFILIATE_LOGIN_PATH = '/affiliate/login'
const AFFILIATE_PORTAL_PATH = '/affiliate/portal'

export const resolveAffiliatePortalReturnUrl = (candidate) => {
    if (typeof candidate !== 'string') return AFFILIATE_PORTAL_PATH

    const value = candidate.trim()
    if (!/^\/affiliate\/portal(?:[?#]|$)/.test(value)) return AFFILIATE_PORTAL_PATH

    return value
}

const affiliateLoginPathForCurrentLocation = () => {
    const currentPath = `${window.location.pathname}${window.location.search}${window.location.hash}`
    const returnUrl = resolveAffiliatePortalReturnUrl(currentPath)

    return returnUrl === AFFILIATE_PORTAL_PATH
        ? AFFILIATE_LOGIN_PATH
        : `${AFFILIATE_LOGIN_PATH}?returnUrl=${encodeURIComponent(returnUrl)}`
}

let navigateToAffiliateLoginPage = () => {
    window.location.href = AFFILIATE_LOGIN_PATH
}

export const setAffiliateLoginNavigator = (navigator) => {
    navigateToAffiliateLoginPage = navigator
}

const LEGACY_TOKEN_KEYS = ['affiliate_accessToken', 'affiliate_refreshToken']

let refreshPromise = null
let restorePromise = null
let logoutPromise = null

const isAffiliateAuthEndpoint = (url = '') => url.includes('/affiliate/auth/')

const clearAffiliateAuthStorage = () => {
    [...Object.values(STORAGE_KEYS), ...LEGACY_TOKEN_KEYS].forEach((key) => localStorage.removeItem(key))
}

const publishAffiliateSessionEvent = (state) => {
    localStorage.setItem(
        AFFILIATE_AUTH_SESSION_EVENT_KEY,
        JSON.stringify({ state, at: Date.now(), nonce: Math.random().toString(36).slice(2) }),
    )
}

const settleAffiliateUnauthenticated = () => {
    clearAffiliateAuthStorage()
    publishAffiliateSessionEvent('unauthenticated')
}

const affiliateAuthorityReadConfig = {
    headers: {
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
    affiliateAuthFailurePolicy: AFFILIATE_AUTH_FAILURE_POLICY.CALLER_OWNED_PASSIVE,
}

const cacheAffiliateProfile = (profile) => {
    clearAffiliateAuthStorage()

    if (!profile) {
        return
    }

    if (profile.affiliateAccountId) {
        localStorage.setItem(STORAGE_KEYS.ACCOUNT_ID, String(profile.affiliateAccountId))
    }
    if (profile.email) {
        localStorage.setItem(STORAGE_KEYS.EMAIL, profile.email)
    }
    if (profile.displayName) {
        localStorage.setItem(STORAGE_KEYS.DISPLAY_NAME, profile.displayName)
    }
    if (profile.referralCode) {
        localStorage.setItem(STORAGE_KEYS.REFERRAL_CODE, profile.referralCode)
    }
}

const forceAffiliateLogoutOnce = () => {
    if (!logoutPromise) {
        logoutPromise = Promise.resolve()
            .then(() => {
                settleAffiliateUnauthenticated()
                navigateToAffiliateLoginPage(affiliateLoginPathForCurrentLocation())
            })
            .finally(() => {
                logoutPromise = null
            })
    }

    return logoutPromise
}

const refreshAffiliateSession = async () => {
    if (!refreshPromise) {
        refreshPromise = axios.post(
            '/api/affiliate/auth/refresh',
            {},
            {
                withCredentials: true,
                timeout: AFFILIATE_AUTH_REQUEST_TIMEOUT_MS,
                timeoutErrorMessage: AFFILIATE_AUTH_REQUEST_TIMEOUT_MESSAGE,
                headers: appendCsrfHeader(
                    {
                        'Content-Type': 'application/json',
                    },
                    'post',
                ),
            },
        )
            .then((response) => {
                cacheAffiliateProfile(response.data)
                return response.data
            })
            .finally(() => {
                refreshPromise = null
            })
    }

    return refreshPromise
}

affiliateAxios.interceptors.request.use(
    (config) => {
        config.withCredentials = true
        config.headers = appendCsrfHeader(config.headers || {}, config.method)
        return config
    },
    (error) => Promise.reject(error),
)

const resolveAffiliateAuthFailurePolicy = (config) => (
    config?.affiliateAuthFailurePolicy ?? AFFILIATE_AUTH_FAILURE_POLICY.TERMINAL_LOGOUT
)

const rejectTerminalAffiliateAuthFailure = async (error, requestConfig) => {
    if (resolveAffiliateAuthFailurePolicy(requestConfig) === AFFILIATE_AUTH_FAILURE_POLICY.CALLER_OWNED_PASSIVE) {
        settleAffiliateUnauthenticated()
    } else {
        await forceAffiliateLogoutOnce()
    }

    return Promise.reject(error)
}

affiliateAxios.interceptors.response.use(
    (response) => response,
    async (error) => {
        const originalRequest = error.config

        if (!originalRequest || isAffiliateAuthEndpoint(originalRequest.url)) {
            return Promise.reject(error)
        }

        if (error.response?.status !== 401) {
            return Promise.reject(error)
        }

        if (originalRequest._retry) {
            return rejectTerminalAffiliateAuthFailure(error, originalRequest)
        }

        originalRequest._retry = true

        try {
            await refreshAffiliateSession()
            return affiliateAxios(originalRequest)
        } catch {
            return rejectTerminalAffiliateAuthFailure(error, originalRequest)
        }
    },
)

export const affiliateAuthApi = {
    register: async (data) => {
        const response = await affiliateAxios.post('/affiliate/auth/register', data)
        return response.data
    },

    login: async (data) => {
        const response = await affiliateAxios.post('/affiliate/auth/login', data)
        cacheAffiliateProfile(response.data)
        publishAffiliateSessionEvent('authenticated')
        return response.data
    },

    verifyEmail: async (token) => {
        const response = await affiliateAxios.post('/affiliate/auth/verify-email', { token })
        return response.data
    },

    resendVerification: async (email) => {
        const response = await affiliateAxios.post('/affiliate/auth/resend-verification', { email })
        return response.data
    },

    forgotPassword: async (email) => {
        const response = await affiliateAxios.post('/affiliate/auth/forgot-password', { email })
        return response.data
    },

    resetPassword: async (token, newPassword) => {
        const response = await affiliateAxios.post('/affiliate/auth/reset-password', { token, newPassword })
        return response.data
    },

    restoreSession: () => {
        if (!restorePromise) {
            restorePromise = affiliateAxios.get('/affiliate/profile', affiliateAuthorityReadConfig)
                .then((response) => {
                    cacheAffiliateProfile(response.data)
                    return response.data
                })
                .finally(() => {
                    restorePromise = null
                })
        }

        return restorePromise
    },

    logout: async () => {
        try {
            await affiliateAxios.post('/affiliate/auth/logout', {})
        } catch {
            // Best effort: client-side cleanup still runs.
        } finally {
            settleAffiliateUnauthenticated()
            logoutPromise = null
            navigateToAffiliateLoginPage(AFFILIATE_LOGIN_PATH)
        }
    },

    getStoredProfile: () => ({
        email: localStorage.getItem(STORAGE_KEYS.EMAIL),
        displayName: localStorage.getItem(STORAGE_KEYS.DISPLAY_NAME),
        referralCode: localStorage.getItem(STORAGE_KEYS.REFERRAL_CODE),
        accountId: localStorage.getItem(STORAGE_KEYS.ACCOUNT_ID),
    }),

    clearSessionPresentation: clearAffiliateAuthStorage,

    STORAGE_KEYS,
}

export { affiliateAxios }

export default affiliateAuthApi

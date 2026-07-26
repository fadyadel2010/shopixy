/**
 * Affiliate Auth API Service
 *
 * Uses HttpOnly cookies for session tokens. The browser handles token transport
 * automatically; JavaScript only keeps non-sensitive profile metadata.
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

const LEGACY_TOKEN_KEYS = ['affiliate_accessToken', 'affiliate_refreshToken']

let refreshPromise = null
let logoutPromise = null

const isAffiliateAuthEndpoint = (url = '') => url.includes('/affiliate/auth/')

const clearAffiliateAuthStorage = () => {
    [...Object.values(STORAGE_KEYS), ...LEGACY_TOKEN_KEYS].forEach((key) => localStorage.removeItem(key))
}

const affiliateAuthorityReadConfig = {
    headers: {
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
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
                clearAffiliateAuthStorage()
                window.location.href = '/affiliate/login'
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

affiliateAxios.interceptors.response.use(
    (response) => response,
    async (error) => {
        const originalRequest = error.config

        if (!originalRequest || isAffiliateAuthEndpoint(originalRequest.url)) {
            return Promise.reject(error)
        }

        if (error.response?.status === 401 && !originalRequest._retry) {
            originalRequest._retry = true

            try {
                await refreshAffiliateSession()
                return affiliateAxios(originalRequest)
            } catch {
                await forceAffiliateLogoutOnce()
                return Promise.reject(error)
            }
        }

        return Promise.reject(error)
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
        return response.data
    },

    restoreSession: async () => {
        try {
            const response = await affiliateAxios.get('/affiliate/profile', affiliateAuthorityReadConfig)
            cacheAffiliateProfile(response.data)
            return response.data
        } catch (error) {
            clearAffiliateAuthStorage()
            throw error
        }
    },

    logout: async () => {
        try {
            await affiliateAxios.post('/affiliate/auth/logout', {})
        } catch {
            // Best effort: client-side cleanup still runs.
        } finally {
            clearAffiliateAuthStorage()
            logoutPromise = null
            window.location.href = '/affiliate/login'
        }
    },

    isAuthenticated: () => !!localStorage.getItem(STORAGE_KEYS.ACCOUNT_ID),

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

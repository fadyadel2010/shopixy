import axios, { type AxiosError } from 'axios'
import { appendCsrfHeader } from '../../utils/csrf'
import type {
    SuperAdminApiErrorResponse,
    SuperAdminApiSuccessResponse,
    SuperAdminLoginRequest,
    SuperAdminRefreshRequest,
    SuperAdminSessionResponse,
} from './superAdminAuth.types'

const SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MS = 30000
const SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MESSAGE = 'Super Admin auth request timeout after 30 seconds'

function createRequestId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID()
    }

    return `sa-auth-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

export class SuperAdminAuthError extends Error {
    status?: number
    code?: string
    requestId?: string

    constructor(message: string, options?: { status?: number; code?: string; requestId?: string }) {
        super(message)
        this.name = 'SuperAdminAuthError'
        this.status = options?.status
        this.code = options?.code
        this.requestId = options?.requestId
    }
}

function normalizeAuthError(error: unknown): SuperAdminAuthError {
    const axiosError = error as AxiosError<SuperAdminApiErrorResponse>

    if (
        axiosError.code === 'ECONNABORTED' ||
        axiosError.code === 'ETIMEDOUT' ||
        axiosError.message === SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MESSAGE ||
        axiosError.message?.toLowerCase().includes('timeout')
    ) {
        return new SuperAdminAuthError('Super Admin authentication request timed out. Please try again.', {
            code: 'REQUEST_TIMEOUT',
        })
    }

    if (axiosError.response?.data?.error) {
        return new SuperAdminAuthError(
            axiosError.response.data.error.message || 'Authentication request failed.',
            {
                status: axiosError.response.status,
                code: axiosError.response.data.error.code,
                requestId: axiosError.response.data.meta?.requestId,
            },
        )
    }

    if (axiosError.response) {
        return new SuperAdminAuthError('Authentication request failed.', {
            status: axiosError.response.status,
        })
    }

    if (axiosError.request) {
        return new SuperAdminAuthError('Unable to reach the Super Admin service right now.')
    }

    return new SuperAdminAuthError('Something went wrong while contacting the Super Admin service.')
}

function unwrapSessionResponse(
    response: SuperAdminApiSuccessResponse<SuperAdminSessionResponse>,
): SuperAdminSessionResponse {
    if (!response.success || !response.data) {
        throw new SuperAdminAuthError('Super Admin response payload was invalid.')
    }

    return response.data
}

function buildJsonHeaders(method: string) {
    return {
        'X-Request-Id': createRequestId(),
        ...appendCsrfHeader(
            {
                'Content-Type': 'application/json',
            },
            method,
        ),
    }
}

export async function loginSuperAdmin(
    payload: SuperAdminLoginRequest,
): Promise<SuperAdminSessionResponse> {
    try {
        const response = await axios.post<SuperAdminApiSuccessResponse<SuperAdminSessionResponse>>(
            '/api/super-admin/auth/login',
            payload,
            {
                headers: buildJsonHeaders('post'),
                withCredentials: true,
                timeout: SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MS,
                timeoutErrorMessage: SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MESSAGE,
            },
        )

        return unwrapSessionResponse(response.data)
    } catch (error) {
        throw normalizeAuthError(error)
    }
}

export async function refreshSuperAdminSession(
    payload: SuperAdminRefreshRequest = {},
): Promise<SuperAdminSessionResponse> {
    try {
        const response = await axios.post<SuperAdminApiSuccessResponse<SuperAdminSessionResponse>>(
            '/api/super-admin/auth/refresh',
            payload,
            {
                headers: buildJsonHeaders('post'),
                withCredentials: true,
                timeout: SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MS,
                timeoutErrorMessage: SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MESSAGE,
            },
        )

        return unwrapSessionResponse(response.data)
    } catch (error) {
        throw normalizeAuthError(error)
    }
}

export async function getSuperAdminSession(): Promise<SuperAdminSessionResponse> {
    try {
        const response = await axios.get<SuperAdminApiSuccessResponse<SuperAdminSessionResponse>>(
            '/api/super-admin/auth/session',
            {
                headers: {
                    'X-Request-Id': createRequestId(),
                },
                withCredentials: true,
                timeout: SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MS,
                timeoutErrorMessage: SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MESSAGE,
            },
        )

        return unwrapSessionResponse(response.data)
    } catch (error) {
        throw normalizeAuthError(error)
    }
}

export async function logoutSuperAdmin(): Promise<void> {
    try {
        await axios.post(
            '/api/super-admin/auth/logout',
            {},
            {
                headers: buildJsonHeaders('post'),
                withCredentials: true,
                timeout: SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MS,
                timeoutErrorMessage: SUPER_ADMIN_AUTH_REQUEST_TIMEOUT_MESSAGE,
            },
        )
    } catch {
        // Best-effort logout - even if the server call fails,
        // the client will still clear local state.
    }
}

const CSRF_COOKIE_NAME = 'csrf_token'
const CSRF_HEADER_NAME = 'X-CSRF-Token'
const UNSAFE_METHODS = new Set(['post', 'put', 'patch', 'delete'])

export const getCsrfToken = () => {
    if (typeof document === 'undefined' || typeof document.cookie !== 'string') {
        return ''
    }

    const cookiePrefix = `${CSRF_COOKIE_NAME}=`
    const cookieValue = document.cookie
        .split(';')
        .map((part) => part.trim())
        .find((part) => part.startsWith(cookiePrefix))

    if (!cookieValue) {
        return ''
    }

    return decodeURIComponent(cookieValue.substring(cookiePrefix.length))
}

export const isUnsafeHttpMethod = (method = 'get') => (
    UNSAFE_METHODS.has(String(method).toLowerCase())
)

export const appendCsrfHeader = (headers = {}, method = 'get') => {
    if (!isUnsafeHttpMethod(method)) {
        return headers
    }

    const csrfToken = getCsrfToken()
    if (!csrfToken) {
        return headers
    }

    return {
        ...headers,
        [CSRF_HEADER_NAME]: csrfToken,
    }
}

export const getCsrfHeaderName = () => CSRF_HEADER_NAME

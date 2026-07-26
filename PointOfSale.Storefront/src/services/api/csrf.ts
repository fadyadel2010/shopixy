const CSRF_COOKIE_NAME = 'csrf_token';
const CSRF_HEADER_NAME = 'X-CSRF-Token';
const UNSAFE_METHODS = new Set(['post', 'put', 'patch', 'delete']);

export function appendCsrfHeader(
    headers: Record<string, string>,
    method?: string
): Record<string, string> {
    if (!method || !UNSAFE_METHODS.has(method.toLowerCase())) {
        return headers;
    }

    if (typeof document === 'undefined') {
        return headers;
    }

    const cookieValue = document.cookie
        .split(';')
        .map((part) => part.trim())
        .find((part) => part.startsWith(`${CSRF_COOKIE_NAME}=`));

    if (!cookieValue) {
        return headers;
    }

    const rawToken = cookieValue.slice(CSRF_COOKIE_NAME.length + 1);
    
    // Defensively handle malformed percent-encoding in cookie
    try {
        return {
            ...headers,
            [CSRF_HEADER_NAME]: decodeURIComponent(rawToken)
        };
    } catch {
        // If decoding fails, use the raw token value as fallback
        // This prevents client-side exceptions from malformed cookies
        return {
            ...headers,
            [CSRF_HEADER_NAME]: rawToken
        };
    }
}

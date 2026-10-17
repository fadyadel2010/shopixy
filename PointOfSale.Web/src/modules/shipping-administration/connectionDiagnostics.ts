export const connectionFailureCategory = (code?: string | null) => {
    if (/CREDENTIAL_.*_(MISSING|INVALID)|INVALID_CREDENTIAL_CONFIGURATION/.test(code || '')) return 'field'
    if (/INVALID_CREDENTIALS|AUTHORIZATION_FAILED|ARAMEX_ERR/.test(code || '')) return 'authentication'
    if (/ENVIRONMENT|ENDPOINT/.test(code || '')) return 'environment'
    if (/TIMEOUT/.test(code || '')) return 'timeout'
    if (/NETWORK|CONNECTION_ERROR/.test(code || '')) return 'network'
    if (/ACCESS_DENIED|HTTP_403/.test(code || '')) return 'access'
    return 'response'
}

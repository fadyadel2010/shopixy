import { useCallback, useEffect, useRef, useState } from 'react'

// Reads and mutations belong to one mounted account/store workspace. State alone
// cannot exclude two submissions dispatched in the same browser tick.
export function useOfferOperations(user) {
    const scope = `${user?.tenantId || ''}:${user?.id || user?.userId || ''}`
    const currentScope = useRef(scope)
    currentScope.current = scope
    const mounted = useRef(true)
    const reads = useRef({})
    const mutation = useRef(null)
    const [busy, setBusy] = useState(false)
    useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
    const beginRead = useCallback((channel) => {
        const token = { scope: currentScope.current }
        reads.current[channel] = token
        return () => mounted.current && currentScope.current === token.scope && reads.current[channel] === token
    }, [])
    const beginMutation = () => {
        if (mutation.current) return null
        const token = { scope }
        mutation.current = token
        setBusy(true)
        return {
            current: () => mounted.current && currentScope.current === token.scope,
            finish: () => { if (mutation.current === token) { mutation.current = null; if (mounted.current) setBusy(false) } },
        }
    }
    return { busy, beginRead, beginMutation }
}

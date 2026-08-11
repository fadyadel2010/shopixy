/**
 * useDomainReadQuery — Unified base hook for DOMAIN-3.1-B read-path data fetching.
 *
 * Eliminates the duplicated boilerplate (request sequencing, mount guarding,
 * visibility-change refresh, error classification, state machine) across the
 * 7 domain read hooks. Each specialized hook wraps this with a single
 * `fetchFn` and an `isEmpty` predicate.
 *
 * State machine:
 *   loading  — first fetch in progress
 *   refreshing — background re-fetch while data still present
 *   success   — data loaded (non-empty)
 *   empty     — data loaded but no items returned (determined by isEmpty predicate)
 *   error     — generic network/server error
 *   unauthorized — 401/403
 *   not-found   — 404
 *   idle      — initial, never loaded
 */
import { useState, useEffect, useCallback, useRef } from 'react'
import { LOAD_STATES, classifyError, getDomainErrorMessage } from './useDomainLoadState'

/**
 * @param {Function} fetchFn  — async function that performs the API call, returns data
 * @param {Function|null} isEmpty  — predicate(data) => boolean, determines if loaded data is "empty".
 *                                   Pass null if empty check is not applicable (e.g. single-resource detail).
 * @param {Array} deps       — dependency array that triggers re-fetch when changed
 * @returns {{ state, data, error, refetch, refresh, isLoading, isRefreshing,
 *            isEmpty, isNotFound, isUnauthorized, isError, isSuccess }}
 */
export function useDomainReadQuery(fetchFn, isEmpty, deps = []) {
    const [state, setState] = useState(LOAD_STATES.LOADING)
    const [data, setData] = useState(null)
    const [error, setError] = useState(null)
    const requestSeq = useRef(0)
    const isMounted = useRef(true)

    // Store latest fetchFn, isEmpty, and deps in refs so the useCallback
    // identity stays stable across renders (preventing infinite re-fetch loops).
    const fetchFnRef = useRef(fetchFn)
    fetchFnRef.current = fetchFn

    const isEmptyRef = useRef(isEmpty)
    isEmptyRef.current = isEmpty

    const depsRef = useRef(deps)
    depsRef.current = deps

    const fetchData = useCallback(async ({ silent = false } = {}) => {
        if (!fetchFnRef.current) return
        const currentRequest = ++requestSeq.current

        if (!silent && isMounted.current) {
            setState(LOAD_STATES.LOADING)
        } else if (silent && isMounted.current) {
            setState(LOAD_STATES.REFRESHING)
        }

        try {
            const result = await fetchFnRef.current()

            if (currentRequest !== requestSeq.current || !isMounted.current) return

            const emptyCheck = isEmptyRef.current
            if (!emptyCheck || !emptyCheck(result)) {
                setData(result)
                setState(LOAD_STATES.SUCCESS)
                setError(null)
            } else {
                setData(result || null)
                setState(LOAD_STATES.EMPTY)
                setError(null)
            }
        } catch (err) {
            if (currentRequest !== requestSeq.current || !isMounted.current) return

            const errorState = classifyError(err)
            setError(getDomainErrorMessage(err))
            setState(errorState)
        }
    }, depsRef.current)

    useEffect(() => {
        isMounted.current = true
        void fetchData()

        const handleVisibility = () => {
            if (document.visibilityState === 'visible') {
                void fetchData({ silent: true })
            }
        }

        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            isMounted.current = false
            requestSeq.current += 1
            document.removeEventListener('visibilitychange', handleVisibility)
        }
    }, [fetchData])

    return {
        state,
        data,
        error,
        isLoading: state === LOAD_STATES.LOADING,
        isRefreshing: state === LOAD_STATES.REFRESHING,
        isEmpty: state === LOAD_STATES.EMPTY,
        isError: state === LOAD_STATES.ERROR,
        isUnauthorized: state === LOAD_STATES.UNAUTHORIZED,
        isNotFound: state === LOAD_STATES.NOT_FOUND,
        isSuccess: state === LOAD_STATES.SUCCESS,
        refetch: () => fetchData({ silent: false }),
        refresh: () => fetchData({ silent: true }),
    }
}

export default useDomainReadQuery

import { useCallback, useEffect, useRef, useState } from 'react'
import { useDebounce } from './useDebounce'

const DEFAULT_LOOKUP_PAGE_SIZE = 20

export function useAsyncLookup({
    loadOptions,
    enabled = true,
    loadOnEmptyQuery = true,
    pageSize = DEFAULT_LOOKUP_PAGE_SIZE,
    initialQuery = '',
}) {
    const [query, setQuery] = useState(initialQuery)
    const [options, setOptions] = useState([])
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState('')
    const [hasMore, setHasMore] = useState(false)
    const [page, setPage] = useState(1)
    const requestSequenceRef = useRef(0)
    const loadingRef = useRef(false)
    const pageRef = useRef(page)
    const isMountedRef = useRef(true)
    const debouncedQuery = useDebounce(query, 300)

    const syncPage = useCallback(nextPage => {
        pageRef.current = nextPage
        setPage(nextPage)
    }, [])

    useEffect(() => {
        return () => {
            isMountedRef.current = false
            requestSequenceRef.current += 1
            loadingRef.current = false
        }
    }, [])

    const reset = useCallback(() => {
        requestSequenceRef.current += 1
        loadingRef.current = false

        if (!isMountedRef.current) {
            return
        }

        setOptions([])
        setLoading(false)
        setError('')
        setHasMore(false)
        syncPage(1)
    }, [syncPage])

    const fetchPage = useCallback(async ({ nextQuery, nextPage, append }) => {
        if (!enabled) {
            loadingRef.current = false
            return
        }

        const requestId = requestSequenceRef.current + 1
        requestSequenceRef.current = requestId
        loadingRef.current = true
        setLoading(true)
        setError('')

        try {
            const response = await loadOptions({
                search: nextQuery,
                page: nextPage,
                pageSize,
            })

            if (requestId !== requestSequenceRef.current) {
                return
            }

            const nextItems = Array.isArray(response?.items) ? response.items : []
            setOptions(currentOptions => (append ? [...currentOptions, ...nextItems] : nextItems))
            setHasMore(Boolean(response?.hasMore))
            syncPage(nextPage)
        } catch (err) {
            if (requestId !== requestSequenceRef.current) {
                return
            }

            setError(err.response?.data?.message || err.message || '')
            if (!append) {
                setOptions([])
            }
            setHasMore(false)
        } finally {
            if (requestId === requestSequenceRef.current) {
                loadingRef.current = false

                if (isMountedRef.current) {
                    setLoading(false)
                }
            }
        }
    }, [enabled, loadOptions, pageSize, syncPage])

    useEffect(() => {
        if (!enabled) {
            reset()
            return
        }

        if (!loadOnEmptyQuery && !debouncedQuery.trim()) {
            reset()
            return
        }

        fetchPage({
            nextQuery: debouncedQuery,
            nextPage: 1,
            append: false,
        })
    }, [debouncedQuery, enabled, fetchPage, loadOnEmptyQuery, reset])

    const loadMore = useCallback(async () => {
        if (loadingRef.current || !hasMore || !enabled) {
            return
        }

        const nextPage = pageRef.current + 1
        loadingRef.current = true

        await fetchPage({
            nextQuery: debouncedQuery,
            nextPage,
            append: true,
        })
    }, [debouncedQuery, enabled, fetchPage, hasMore])

    const refresh = useCallback(async () => {
        if (!enabled) {
            return
        }

        await fetchPage({
            nextQuery: debouncedQuery,
            nextPage: 1,
            append: false,
        })
    }, [debouncedQuery, enabled, fetchPage])

    return {
        query,
        setQuery,
        options,
        loading,
        error,
        hasMore,
        loadMore,
        refresh,
        reset,
    }
}

import { StrictMode } from 'react'
import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useAsyncLookup } from './useAsyncLookup'

describe('useAsyncLookup lifecycle', () => {
    it('settles loading and exposes results under React Strict Mode', async () => {
        const loadOptions = vi.fn().mockResolvedValue({
            items: [{ id: 'rep-1', name: 'Sales Rep' }],
            hasMore: false,
        })

        const { result } = renderHook(() => useAsyncLookup({ loadOptions }), {
            wrapper: ({ children }) => <StrictMode>{children}</StrictMode>,
        })

        await waitFor(() => {
            expect(result.current.loading).toBe(false)
            expect(result.current.options).toEqual([{ id: 'rep-1', name: 'Sales Rep' }])
        })
    })
})

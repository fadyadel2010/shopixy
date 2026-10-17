import { cleanup, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { useCanAccessStorefront, useHasPermission, useIsEmailVerified } from './usePosSessionPermission'

const state = vi.hoisted((): { user: { role: string; emailVerified: boolean; effectivePermissions: string[] } | null } => ({ user: null }))
vi.mock('../store/authStore', () => ({ useAuthStore: (select: (value: typeof state) => unknown) => select(state) }))
afterEach(cleanup)
beforeEach(() => { state.user = null })

it('reads server-issued permission truth and does not infer it from an Owner role', () => {
    state.user = { role: 'Owner', emailVerified: false, effectivePermissions: [] }
    const view = renderHook(() => ({ allowed: useHasPermission('catalog.publish'), unknown: useHasPermission('invented.permission'), verified: useIsEmailVerified() }))
    expect(view.result.current).toEqual({ allowed: false, unknown: false, verified: false })
    state.user = { ...state.user, effectivePermissions: ['catalog.publish'], emailVerified: true }; view.rerender()
    expect(view.result.current).toEqual({ allowed: true, unknown: false, verified: true })
    state.user = null; view.rerender()
    expect(view.result.current).toEqual({ allowed: false, unknown: false, verified: false })
})

it('preserves the separate existing storefront-role projection', () => {
    const view = renderHook(useCanAccessStorefront)
    expect(view.result.current).toBe(false)
    state.user = { role: 'Manager', emailVerified: true, effectivePermissions: ['storefront.design.manage'] }; view.rerender()
    expect(view.result.current).toBe(false)
    state.user = { ...state.user, role: 'Admin' }; view.rerender()
    expect(view.result.current).toBe(true)
})

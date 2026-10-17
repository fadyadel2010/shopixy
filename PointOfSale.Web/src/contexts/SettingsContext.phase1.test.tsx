import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SettingsProvider, useSettings } from './SettingsContext'

const state = vi.hoisted(() => ({ isAuthenticated: true, hasHydrated: true, user: { tenantId: 'tenant-a' } }))
const get = vi.hoisted(() => vi.fn())
vi.mock('../services/api', () => ({ default: { get } }))
vi.mock('../store/authStore', () => ({ useAuthStore: (selector: (value: typeof state) => unknown) => selector(state) }))
function Probe() { const { settings } = useSettings(); return <p>{settings.storefrontUrl || 'No store URL'}</p> }
function App() { return <SettingsProvider><Probe /></SettingsProvider> }

describe('Settings links respect the active authenticated tenant', () => {
    beforeEach(() => { vi.resetAllMocks(); state.isAuthenticated = true; state.user = { tenantId: 'tenant-a' } })
    afterEach(cleanup)
    it('does not restore an old store URL after logout while its read is pending', async () => {
        let accept!: (value: unknown) => void
        get.mockImplementation(() => new Promise(resolve => { accept = resolve }))
        const rendered = render(<App />)
        state.isAuthenticated = false
        rendered.rerender(<App />)
        await act(async () => accept({ data: { storefrontUrl: 'https://old.example.test' } }))
        expect(screen.getByText('No store URL')).toBeInTheDocument()
    })
    it('reloads on tenant change and ignores the previous tenant response', async () => {
        let old!: (value: unknown) => void
        get.mockImplementationOnce(() => new Promise(resolve => { old = resolve }))
            .mockResolvedValueOnce({ data: { storefrontUrl: 'https://new.example.test' } })
        const rendered = render(<App />)
        state.user = { tenantId: 'tenant-b' }
        rendered.rerender(<App />)
        expect(await screen.findByText('https://new.example.test')).toBeInTheDocument()
        await act(async () => old({ data: { storefrontUrl: 'https://old.example.test' } }))
        expect(screen.getByText('https://new.example.test')).toBeInTheDocument()
    })
})

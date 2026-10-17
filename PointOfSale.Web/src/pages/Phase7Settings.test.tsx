import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import en from '../i18n/locales/en.json'

const m = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), list: vi.fn(), update: vi.fn(), enable: vi.fn(), disable: vi.fn(), rotate: vi.fn(), success: vi.fn(), error: vi.fn() }))
function translate(key: string, fallback?: unknown): unknown {
    const value = key.split('.').reduce<unknown>((current, part) => current && typeof current === 'object' ? (current as Record<string, unknown>)[part] : undefined, en)
    return value ?? (typeof fallback === 'string' ? fallback : key)
}
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: translate, i18n: { language: 'en' } }) }))
vi.mock('../services/api', () => ({ default: { get: m.get, put: m.put } }))
vi.mock('../store/authStore', () => ({ useAuthStore: () => ({ user: { id: 'qa', tenantId: 'qa-tenant' } }) }))
vi.mock('../components/Toast', () => ({ useToast: () => ({ showSuccess: m.success, showError: m.error }) }))
vi.mock('../modules/storefront/services/authProvidersApi', () => ({ default: { getAuthProviders: m.list, updateAuthProvider: m.update, enableAuthProvider: m.enable, disableAuthProvider: m.disable, rotateSecret: m.rotate } }))
import CommerceAI from './CommerceAI'
import AuthProvidersPage from '../modules/storefront/pages/AuthProvidersPage'

const config = { rowVersion: '101', storeName: 'do-not-replace', commerceAi: { enabled: true, widgetStyle: 'minimal', widgetPosition: 'bottom-right', autoGreetingEnabled: true, autoGreetingMessage: 'Original', personality: 'friendly' } }
const providers = [{ provider: 'Google', isEnabled: false, clientId: 'qa-client', hasClientSecret: true, revision: '101' }, { provider: 'Facebook', isEnabled: false, clientId: '', hasClientSecret: false, revision: '0' }]
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r }); return { promise, resolve } }

beforeEach(() => {
    cleanup(); vi.resetAllMocks()
    m.get.mockImplementation((url: string) => Promise.resolve({ data: url.includes('insights') ? { chatOpened: 0, draftCreated: 0 } : structuredClone(config) }))
    m.list.mockResolvedValue({ providers: structuredClone(providers) })
    m.put.mockResolvedValue({ data: { ...config, rowVersion: '102' } })
    m.update.mockResolvedValue({ success: true }); m.enable.mockResolvedValue({ success: true })
})

describe('Phase7 assistant experience settings', () => {
    it('does not show editable defaults after a failed authoritative read; retry recovers', async () => {
        m.get.mockRejectedValueOnce(new Error('unavailable'))
        render(<CommerceAI />)
        await screen.findByRole('alert')
        expect(screen.queryByRole('button', { name: 'Save Changes' })).toBeNull()
        fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
        expect(await screen.findByRole('textbox')).toHaveValue('Original')
    })
    it('saves only the AI configuration with reviewed version, and suppresses duplicate submissions', async () => {
        const pending = deferred<{ data: typeof config }>(); m.put.mockReturnValue(pending.promise)
        render(<CommerceAI />); await screen.findByRole('textbox')
        const save = screen.getByRole('button', { name: 'Save Changes' })
        act(() => { fireEvent.click(save); fireEvent.click(save) })
        expect(m.put).toHaveBeenCalledTimes(1)
        expect(m.put).toHaveBeenCalledWith('/admin/store-config/commerce-ai', config.commerceAi, { headers: { 'If-Match': '"101"' } })
        await act(async () => pending.resolve({ data: config }))
    })
    it('does not announce complete success when accepted save cannot be read back', async () => {
        render(<CommerceAI />); await screen.findByRole('textbox')
        m.get.mockRejectedValue(new Error('readback unavailable'))
        fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))
        await waitFor(() => expect(m.error).toHaveBeenCalledWith(en.phase7.savedReadbackFailed))
        expect(m.success).not.toHaveBeenCalled()
        expect(screen.getByRole('textbox')).toHaveValue('Original')
    })
    it('retains a failed candidate and reports a stale revision rather than retrying blindly', async () => {
        m.put.mockRejectedValue({ response: { status: 409 } })
        render(<CommerceAI />); const greeting = await screen.findByRole('textbox')
        fireEvent.change(greeting, { target: { value: 'P7QA candidate' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))
        await waitFor(() => expect(m.error).toHaveBeenCalledWith(en.phase7.conflict))
        expect(greeting).toHaveValue('P7QA candidate')
    })
    it('does not report save success after leaving the workspace', async () => {
        const pending = deferred<{ data: typeof config }>(); m.put.mockReturnValue(pending.promise)
        const view = render(<CommerceAI />); await screen.findByRole('textbox')
        fireEvent.click(screen.getByRole('button', { name: 'Save Changes' })); view.unmount()
        await act(async () => pending.resolve({ data: config }))
        expect(m.success).not.toHaveBeenCalled()
    })
})

describe('Phase7 authentication settings', () => {
    const card = () => screen.getByRole('heading', { name: 'Google' }).closest('.provider-card') as HTMLElement
    it('shows a retryable error, not an empty provider catalog', async () => {
        m.list.mockRejectedValueOnce(new Error('unavailable')); render(<AuthProvidersPage />)
        await screen.findByRole('alert'); fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
        await screen.findByRole('heading', { name: 'Google' })
    })
    it('keeps secrets write-only and sends the reviewed revision without a masking placeholder', async () => {
        render(<AuthProvidersPage />); await screen.findByRole('heading', { name: 'Google' })
        fireEvent.click(within(card()).getByRole('button', { name: 'Edit' }))
        expect(within(card()).getByLabelText('Client Secret')).toHaveValue('')
        fireEvent.click(within(card()).getByRole('button', { name: 'Save' }))
        await waitFor(() => expect(m.update).toHaveBeenCalledWith('Google', { clientId: 'qa-client', additionalScopes: '' }, '101'))
    })
    it('suppresses duplicate writes and qualifies a failed post-save readback', async () => {
        const pending = deferred<{ success: boolean }>(); m.update.mockReturnValue(pending.promise)
        render(<AuthProvidersPage />); await screen.findByRole('heading', { name: 'Google' })
        fireEvent.click(within(card()).getByRole('button', { name: 'Edit' }))
        const save = within(card()).getByRole('button', { name: 'Save' })
        act(() => { fireEvent.click(save); fireEvent.click(save) })
        expect(m.update).toHaveBeenCalledTimes(1)
        m.list.mockRejectedValue(new Error('readback'))
        await act(async () => pending.resolve({ success: true }))
        expect(m.success).not.toHaveBeenCalled()
        expect(m.error).toHaveBeenCalledWith(en.phase7.savedReadbackFailed)
    })
    it('preserves candidate values after rejected update', async () => {
        m.update.mockRejectedValue({ response: { status: 409 } }); render(<AuthProvidersPage />)
        await screen.findByRole('heading', { name: 'Google' }); fireEvent.click(within(card()).getByRole('button', { name: 'Edit' }))
        fireEvent.change(within(card()).getByLabelText('Client ID'), { target: { value: 'P7QA candidate' } })
        fireEvent.click(within(card()).getByRole('button', { name: 'Save' }))
        await waitFor(() => expect(m.error).toHaveBeenCalledWith(en.phase7.conflict))
        expect(within(card()).getByLabelText('Client ID')).toHaveValue('P7QA candidate')
    })
    it('serializes an explicit cleared Client ID and scopes instead of an omitted partial update', async () => {
        render(<AuthProvidersPage />); await screen.findByRole('heading', { name: 'Google' })
        fireEvent.click(within(card()).getByRole('button', { name: 'Edit' }))
        fireEvent.change(within(card()).getByLabelText('Client ID'), { target: { value: '' } })
        fireEvent.click(within(card()).getByRole('button', { name: 'Save' }))
        await waitFor(() => expect(m.update).toHaveBeenCalledWith('Google', { clientId: '', additionalScopes: '' }, '101'))
    })
    it('refreshing a list does not silently authorize an old editor against a newer revision', async () => {
        render(<AuthProvidersPage />); await screen.findByRole('heading', { name: 'Google' })
        fireEvent.click(within(card()).getByRole('button', { name: 'Edit' }))
        fireEvent.change(within(card()).getByLabelText('Client ID'), { target: { value: 'P7QA reviewed candidate' } })
        m.list.mockResolvedValueOnce({ providers: providers.map(p => ({ ...p, revision: '202' })) })
        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
        await screen.findByRole('heading', { name: 'Google' })
        fireEvent.click(within(card()).getByRole('button', { name: 'Save' }))
        await waitFor(() => expect(m.update).toHaveBeenCalledWith('Google', { clientId: 'P7QA reviewed candidate', additionalScopes: '' }, '101'))
    })
    it('never emits success for a mutation completed after workspace unmount', async () => {
        const pending = deferred<{ success: boolean }>(); m.enable.mockReturnValue(pending.promise)
        const view = render(<AuthProvidersPage />); await screen.findByRole('heading', { name: 'Google' })
        fireEvent.click(within(card()).getByRole('button', { name: 'Enable' })); view.unmount()
        await act(async () => pending.resolve({ success: true })); expect(m.success).not.toHaveBeenCalled()
    })
})

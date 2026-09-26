import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ThemeEditorPage, validateThemeCandidate } from './ThemeEditorPage'

const getStoreConfig = vi.hoisted(() => vi.fn())
const getThemePresets = vi.hoisted(() => vi.fn())
const updateThemeOverrides = vi.hoisted(() => vi.fn())
const publishTheme = vi.hoisted(() => vi.fn())
const showSuccess = vi.hoisted(() => vi.fn())
const showError = vi.hoisted(() => vi.fn())
const showInfo = vi.hoisted(() => vi.fn())
const language = vi.hoisted(() => ({ current: 'en' }))

vi.mock('../services', () => ({
    storeConfigService: {
        getStoreConfig,
        getThemePresets,
        updateThemeOverrides,
        publishTheme,
    },
}))

vi.mock('../../../components/Toast', () => ({
    useToast: () => ({ showSuccess, showError, showInfo }),
}))

vi.mock('../../../store/authStore', () => ({
    useAuthStore: () => ({ user: { tenantId: 'tenant-1' } }),
}))

vi.mock('../../../auth/commerceHubVendorIdentityAuthority', () => ({
    deriveCurrentStoreIdentityPresentation: (value: unknown) => value,
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ i18n: { language: language.current } }),
}))

vi.mock('../authoringPlatform', () => ({
    AuthoringActionBar: ({ title, status, previewAction, discardAction, saveAction, publishAction }: any) => (
        <header>
            <h1>{title}</h1>
            {status}
            <button disabled={previewAction.disabled} onClick={previewAction.onClick}>{previewAction.label}</button>
            <button disabled={discardAction.disabled} onClick={discardAction.onClick}>{discardAction.label}</button>
            <button disabled={saveAction.disabled} onClick={saveAction.onClick}>{saveAction.label}</button>
            <button disabled={publishAction.disabled} onClick={publishAction.onClick}>{publishAction.label}</button>
        </header>
    ),
    AuthoringConfirmDialog: ({ open, title, confirmLabel, onConfirm }: any) => open ? (
        <div role="dialog" aria-label={title}><button onClick={onConfirm}>{confirmLabel}</button></div>
    ) : null,
    AuthoringConflictDialog: () => null,
    AuthoringFeedback: ({ title, message }: any) => <div>{title} {message}</div>,
    AuthoringLoadingState: ({ label }: any) => <div>{label}</div>,
    AuthoringStatusBadge: ({ state }: any) => <span>{state === 'dirty' ? 'Unsaved changes' : state}</span>,
    UnsavedChangesDialog: () => null,
    useAuthoringGuard: () => ({ requestNavigation: vi.fn(), unsavedDialogProps: {} }),
}))

const colors = {
    primary: '#2563eb',
    secondary: '#0f172a',
    background: '#ffffff',
    text: '#020617',
    muted: '#64748b',
}

function config(overrides: Record<string, unknown> = {}) {
    return {
        tenantId: 'tenant-1',
        rowVersion: '7',
        themePresetKey: 'default',
        themeLivePresetKey: 'default',
        draftTheme: { presetKey: 'default', colors, font: 'Inter', baseSize: '16px', borderRadius: 8 },
        liveTheme: { presetKey: 'default', colors, font: 'Inter', baseSize: '16px', borderRadius: 8 },
        ...overrides,
    }
}

const presets = [{
    key: 'default',
    name: 'Default Modern',
    baseTheme: {
        colors,
        typography: { fontFamily: 'Inter', baseSize: '16px' },
        radius: { base: '8px' },
    },
}]

async function renderEditor() {
    render(<ThemeEditorPage />)
    await screen.findByRole('heading', { name: 'Colors & Typography' })
}

describe('ThemeEditorPage Colors and Typography authority', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        language.current = 'en'
        getStoreConfig.mockResolvedValue(config())
        getThemePresets.mockResolvedValue(presets)
        updateThemeOverrides.mockResolvedValue(config({ rowVersion: '8' }))
        publishTheme.mockResolvedValue(config({ rowVersion: '9' }))
        Element.prototype.scrollIntoView = vi.fn()
    })

    it('renders labelled color, font, base-size, radius, and local preview controls', async () => {
        await renderEditor()

        expect(screen.getByLabelText('Primary Color')).toHaveValue('#2563eb')
        expect(screen.getByLabelText('Secondary Color')).toHaveValue('#0f172a')
        expect(screen.getByLabelText('Font Family')).toHaveValue('Inter')
        expect(screen.getByLabelText('Base Font Size: 16px')).toHaveValue('16')
        expect(screen.getByLabelText('Border Radius: 8px')).toHaveValue('8')
        expect(screen.getByLabelText('Appearance candidate preview').querySelector('.shadow-lg')).toHaveStyle({ fontFamily: 'Inter', fontSize: '16px' })
    })

    it('matches backend validation for hex, font, and base-size formats', () => {
        expect(validateThemeCandidate({ colors: { ...colors, primary: '#AbC' }, font: 'Cairo', baseSize: '24px' }).isValid).toBe(true)
        expect(validateThemeCandidate({ colors: { ...colors, primary: 'javascript:alert(1)' }, font: 'Inter', baseSize: '16px' }).isValid).toBe(false)
        expect(validateThemeCandidate({ colors, font: 'Jost', baseSize: '16px' }).font).toMatch(/supported/)
        expect(validateThemeCandidate({ colors, font: 'Inter', baseSize: '25px' }).baseSize).toMatch(/12px/)
    })

    it('blocks malformed colors before mutation and exposes an accessible error', async () => {
        await renderEditor()
        fireEvent.change(screen.getByLabelText('Primary Color'), { target: { value: 'red; background:url(javascript:alert(1))' } })

        expect(screen.getByRole('alert')).toHaveTextContent('Use #RGB or #RRGGBB with no spaces.')
        expect(screen.getByRole('button', { name: 'Save Draft' })).toBeDisabled()
        expect(screen.getByRole('button', { name: 'Publish Colors & Typography' })).toBeDisabled()
        expect(updateThemeOverrides).not.toHaveBeenCalled()
    })

    it('saves every supported token with row-version evidence and reload-compatible base size', async () => {
        updateThemeOverrides.mockResolvedValue(config({
            rowVersion: '8',
            draftTheme: {
                presetKey: 'default',
                colors: { ...colors, primary: '#123456' },
                font: 'Cairo',
                baseSize: '18px',
                borderRadius: 12,
            },
        }))
        await renderEditor()
        fireEvent.change(screen.getByLabelText('Primary Color'), { target: { value: '#123456' } })
        fireEvent.change(screen.getByLabelText('Font Family'), { target: { value: 'Cairo' } })
        fireEvent.change(screen.getByLabelText(/Base Font Size/), { target: { value: '18' } })
        fireEvent.change(screen.getByLabelText(/Border Radius/), { target: { value: '12' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))

        await waitFor(() => expect(updateThemeOverrides).toHaveBeenCalledWith({
            presetKey: 'default',
            colors: { ...colors, primary: '#123456' },
            typography: { fontFamily: 'Cairo', baseSize: '18px' },
            borderRadius: 12,
        }, { headers: { 'If-Match': '"7"' } }))
        expect(await screen.findByText(/accepted Appearance draft is up to date/)).toBeInTheDocument()
        expect(screen.getByLabelText(/Base Font Size/)).toHaveValue('18')
    })

    it('discards only local edits and does not issue a write', async () => {
        await renderEditor()
        fireEvent.change(screen.getByLabelText('Primary Color'), { target: { value: '#abcdef' } })
        expect(screen.getByLabelText('Primary Color')).toHaveValue('#abcdef')
        fireEvent.click(screen.getByRole('button', { name: 'Discard' }))

        expect(screen.getByLabelText('Primary Color')).toHaveValue('#2563eb')
        expect(updateThemeOverrides).not.toHaveBeenCalled()
    })

    it('retains a newer edit made while Save Draft is in flight', async () => {
        let resolveSave!: (value: ReturnType<typeof config>) => void
        updateThemeOverrides.mockImplementation(() => new Promise((resolve) => { resolveSave = resolve }))
        await renderEditor()
        fireEvent.change(screen.getByLabelText('Primary Color'), { target: { value: '#111111' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(updateThemeOverrides).toHaveBeenCalledTimes(1))
        fireEvent.change(screen.getByLabelText('Primary Color'), { target: { value: '#222222' } })

        await act(async () => resolveSave(config({
            rowVersion: '8',
            draftTheme: { presetKey: 'default', colors: { ...colors, primary: '#111111' }, font: 'Inter', baseSize: '16px', borderRadius: 8 },
        })))

        expect(screen.getByLabelText('Primary Color')).toHaveValue('#222222')
        expect(screen.getByText('Unsaved changes')).toBeInTheDocument()
        expect(screen.getByText(/Newer unsaved edits were retained/)).toBeInTheDocument()
    })

    it('does not reload or lose local edits on focus, visibility, or copy events', async () => {
        await renderEditor()
        fireEvent.change(screen.getByLabelText('Primary Color'), { target: { value: '#345678' } })
        act(() => {
            window.dispatchEvent(new Event('blur'))
            window.dispatchEvent(new Event('focus'))
            document.dispatchEvent(new Event('visibilitychange'))
            document.dispatchEvent(new Event('copy'))
        })

        expect(screen.getByLabelText('Primary Color')).toHaveValue('#345678')
        expect(getStoreConfig).toHaveBeenCalledTimes(1)
        expect(screen.queryByText(/Restoring session/i)).not.toBeInTheDocument()
    })

    it('renders Arabic labels while retaining the same candidate contract', async () => {
        language.current = 'ar'
        await renderEditor()

        expect(screen.getByLabelText('اللون الأساسي')).toHaveValue('#2563eb')
        expect(screen.getByLabelText('نوع الخط')).toHaveValue('Inter')
        expect(screen.getByLabelText(/حجم الخط الأساسي/)).toHaveValue('16')
    })
})

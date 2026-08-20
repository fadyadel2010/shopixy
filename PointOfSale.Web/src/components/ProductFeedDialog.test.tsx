import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ProductFeedDialog from './ProductFeedDialog.jsx'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const confirmMock = vi.hoisted(() => vi.fn())
const showSuccessMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const clipboardWriteMock = vi.hoisted(() => vi.fn())
const rtlMock = vi.hoisted(() => ({ isRTL: false, dir: 'ltr' }))

const mockT = vi.hoisted(() => (key: string, options?: Record<string, unknown>) => {
    if (!options || typeof options !== 'object') return key
    return Object.entries(options).reduce(
        (acc, [name, value]) => acc.split(`{{${name}}}`).join(String(value)),
        key,
    )
})

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
    },
}))

vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: confirmMock }),
}))

vi.mock('./Toast', () => ({
    useToast: () => ({
        showSuccess: showSuccessMock,
        showError: showErrorMock,
    }),
}))

vi.mock('../hooks/useRTL', () => ({
    useRTL: () => rtlMock,
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: mockT }),
}))

function feedListFor(url: string) {
    if (url.includes('/meta/')) {
        return [{ tokenId: 'meta-token', channel: 'meta', createdAtUtc: '2026-08-20T00:00:00Z', revokedAtUtc: null }]
    }
    if (url.includes('/tiktok/')) {
        return [{ tokenId: 'tiktok-token', channel: 'tiktok', createdAtUtc: '2026-08-19T00:00:00Z', revokedAtUtc: '2026-08-20T00:00:00Z' }]
    }
    return []
}

function renderDialog(onOpenChange = vi.fn()) {
    render(<ProductFeedDialog open onOpenChange={onOpenChange} />)
    return onOpenChange
}

function installClipboardMock() {
    Object.defineProperty(window.navigator, 'clipboard', {
        configurable: true,
        writable: true,
        value: {
            writeText: clipboardWriteMock,
        },
    })
    vi.stubGlobal('navigator', {
        ...window.navigator,
        clipboard: {
            writeText: clipboardWriteMock,
        },
    })
}

beforeEach(() => {
    vi.clearAllMocks()
    rtlMock.isRTL = false
    rtlMock.dir = 'ltr'
    apiGetMock.mockImplementation((url: string) => Promise.resolve({ data: feedListFor(url) }))
    apiPostMock.mockImplementation((url: string) => {
        if (url.endsWith('/rotate')) {
            return Promise.resolve({
                data: {
                    tokenId: 'rotated-token',
                    channel: 'meta',
                    feedUrl: 'https://api.shopixy.test/api/public/feeds/meta/rotated-secret',
                    createdAtUtc: '2026-08-20T01:00:00Z',
                },
            })
        }
        if (url.endsWith('/revoke')) {
            return Promise.resolve({ data: undefined })
        }
        return Promise.resolve({
            data: {
                tokenId: 'created-token',
                channel: 'google',
                feedUrl: 'https://api.shopixy.test/api/public/feeds/google/created-secret',
                createdAtUtc: '2026-08-20T01:00:00Z',
            },
        })
    })
    confirmMock.mockResolvedValue(true)
    clipboardWriteMock.mockResolvedValue(undefined)
    installClipboardMock()
    localStorage.clear()
    sessionStorage.clear()
})

describe('ProductFeedDialog', () => {
    it('renders all channels and derives configured states from safe metadata', async () => {
        renderDialog()

        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy())
        for (const channel of ['Meta', 'Google', 'TikTok', 'Snapchat']) {
            expect(screen.getByRole('tab', { name: new RegExp(channel) })).toBeTruthy()
        }

        expect(within(screen.getByRole('tab', { name: /Meta/ })).getByText('productFeed.status.active')).toBeTruthy()
        expect(within(screen.getByRole('tab', { name: /Google/ })).getByText('productFeed.status.notConfigured')).toBeTruthy()
        expect(within(screen.getByRole('tab', { name: /TikTok/ })).getByText('productFeed.status.revoked')).toBeTruthy()
    })

    it('creates a feed once, copies the one-time URL, and clears the raw secret on Done', async () => {
        const user = userEvent.setup()
        installClipboardMock()
        renderDialog()

        await user.click(await screen.findByRole('tab', { name: /Google/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))

        await waitFor(() => expect(apiPostMock).toHaveBeenCalledWith('/admin/feeds/google/tokens'))
        expect(await screen.findByText('https://api.shopixy.test/api/public/feeds/google/created-secret')).toBeTruthy()

        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
        expect(clipboardWriteMock).toHaveBeenCalledWith('https://api.shopixy.test/api/public/feeds/google/created-secret')
        expect(showSuccessMock).toHaveBeenCalledWith('productFeed.copied')

        await user.click(screen.getByRole('button', { name: 'productFeed.done' }))
        expect(screen.queryByText('https://api.shopixy.test/api/public/feeds/google/created-secret')).toBeNull()
        expect(localStorage.length).toBe(0)
        expect(sessionStorage.length).toBe(0)
    })

    it('rotates only after confirmation and removes the old URL from the rendered UI', async () => {
        const user = userEvent.setup()
        renderDialog()

        await waitFor(() => expect(screen.getByText('productFeed.maskedUrl')).toBeTruthy())
        expect(screen.queryByText(/old-secret/)).toBeNull()

        await user.click(screen.getByRole('button', { name: 'productFeed.rotateAria' }))

        expect(confirmMock).toHaveBeenCalledWith(expect.objectContaining({
            description: 'productFeed.rotateDescription',
            confirmTone: 'warning',
        }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledWith('/admin/feeds/meta/tokens/meta-token/rotate'))
        expect(await screen.findByText('https://api.shopixy.test/api/public/feeds/meta/rotated-secret')).toBeTruthy()
        expect(screen.queryByText(/old-secret/)).toBeNull()
    })

    it('revokes after destructive confirmation, refreshes metadata, and clears one-time state', async () => {
        const user = userEvent.setup()
        renderDialog()

        await user.click(await screen.findByRole('button', { name: 'productFeed.revokeAria' }))

        expect(confirmMock).toHaveBeenCalledWith(expect.objectContaining({
            description: 'productFeed.revokeDescription',
            confirmTone: 'danger',
        }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledWith('/admin/feeds/meta/tokens/meta-token/revoke'))
        expect(apiGetMock.mock.calls.filter((call) => call[0] === '/admin/feeds/meta/tokens').length).toBeGreaterThan(1)
    })

    it('clears a one-time raw URL when the dialog closes', async () => {
        const user = userEvent.setup()
        const onOpenChange = renderDialog()

        await user.click(await screen.findByRole('tab', { name: /Google/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))
        expect(await screen.findByText('https://api.shopixy.test/api/public/feeds/google/created-secret')).toBeTruthy()

        await user.click(screen.getByRole('button', { name: 'common.close' }))

        expect(onOpenChange).toHaveBeenCalledWith(false)
        expect(screen.queryByText('https://api.shopixy.test/api/public/feeds/google/created-secret')).toBeNull()
    })

    it('sets dialog direction from the RTL system even though Radix portals outside the Products page', async () => {
        rtlMock.isRTL = true
        rtlMock.dir = 'rtl'

        renderDialog()

        expect(await screen.findByRole('dialog')).toHaveAttribute('dir', 'rtl')
    })
})

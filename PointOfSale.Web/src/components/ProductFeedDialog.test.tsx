import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ProductFeedDialog from './ProductFeedDialog.jsx'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const confirmMock = vi.hoisted(() => vi.fn())
const showSuccessMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const clipboardWriteMock = vi.hoisted(() => vi.fn())
const windowOpenMock = vi.hoisted(() => vi.fn())
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

function renderDialogSessionHarness(initialSessionFeedUrls: Record<string, string> = {}) {
    function Harness() {
        const [open, setOpen] = useState(true)
        const [sessionFeedUrls, setSessionFeedUrls] = useState(initialSessionFeedUrls)

        const handleSessionFeedUrlChange = (channel: string, feedUrl: string | null) => {
            setSessionFeedUrls((current) => {
                if (!feedUrl) {
                    const next = { ...current }
                    delete next[channel]
                    return next
                }

                return {
                    ...current,
                    [channel]: feedUrl,
                }
            })
        }

        return (
            <>
                <button type="button" onClick={() => setOpen(true)}>reopen-feed-dialog</button>
                <ProductFeedDialog
                    open={open}
                    onOpenChange={setOpen}
                    sessionFeedUrls={sessionFeedUrls}
                    onSessionFeedUrlChange={handleSessionFeedUrlChange}
                />
            </>
        )
    }

    return render(<Harness />)
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
    Object.defineProperty(window, 'open', {
        configurable: true,
        writable: true,
        value: windowOpenMock,
    })
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

    it('keeps a created URL in page-session memory after Done, close, and reopen', async () => {
        const user = userEvent.setup()
        installClipboardMock()
        let googleActive = false
        const rawUrl = 'https://api.shopixy.test/api/public/feeds/google/created-secret'
        const searchBefore = window.location.search

        apiGetMock.mockImplementation((url: string) => {
            if (url.includes('/google/')) {
                return Promise.resolve({
                    data: googleActive
                        ? [{ tokenId: 'created-token', channel: 'google', createdAtUtc: '2026-08-20T01:00:00Z', revokedAtUtc: null }]
                        : [],
                })
            }
            return Promise.resolve({ data: feedListFor(url) })
        })
        apiPostMock.mockImplementation((url: string) => {
            if (url === '/admin/feeds/google/tokens') {
                googleActive = true
                return Promise.resolve({
                    data: {
                        tokenId: 'created-token',
                        channel: 'google',
                        feedUrl: rawUrl,
                        createdAtUtc: '2026-08-20T01:00:00Z',
                    },
                })
            }
            return Promise.resolve({ data: undefined })
        })

        renderDialogSessionHarness()

        await user.click(await screen.findByRole('tab', { name: /Google/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))
        expect(await screen.findByText(rawUrl)).toBeTruthy()

        await user.click(screen.getByRole('button', { name: 'productFeed.done' }))
        expect(screen.queryByText(rawUrl)).toBeNull()
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
        await user.click(screen.getByRole('button', { name: 'productFeed.openFeedAria' }))

        expect(clipboardWriteMock).toHaveBeenLastCalledWith(rawUrl)
        expect(windowOpenMock).toHaveBeenLastCalledWith(rawUrl, '_blank', 'noopener,noreferrer')

        await user.click(screen.getByRole('button', { name: 'common.close' }))
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
        await user.click(screen.getByRole('button', { name: 'reopen-feed-dialog' }))
        await user.click(await screen.findByRole('tab', { name: /Google/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
        await user.click(screen.getByRole('button', { name: 'productFeed.openFeedAria' }))

        expect(clipboardWriteMock).toHaveBeenLastCalledWith(rawUrl)
        expect(windowOpenMock).toHaveBeenLastCalledWith(rawUrl, '_blank', 'noopener,noreferrer')
        expect(localStorage.length).toBe(0)
        expect(sessionStorage.length).toBe(0)
        expect(window.location.search).toBe(searchBefore)
    })

    it('clears page-session feed URLs after remount even when metadata is active', async () => {
        const user = userEvent.setup()
        apiGetMock.mockImplementation((url: string) => {
            if (url.includes('/google/')) {
                return Promise.resolve({ data: [{ tokenId: 'created-token', channel: 'google', createdAtUtc: '2026-08-20T01:00:00Z', revokedAtUtc: null }] })
            }
            return Promise.resolve({ data: feedListFor(url) })
        })

        const first = renderDialogSessionHarness({ google: 'https://api.shopixy.test/api/public/feeds/google/created-secret' })
        await user.click(await screen.findByRole('tab', { name: /Google/ }))
        expect(await screen.findByRole('button', { name: 'productFeed.copyUrl' })).toBeTruthy()
        first.unmount()

        renderDialogSessionHarness()
        await user.click(await screen.findByRole('tab', { name: /Google/ }))

        expect(await screen.findByText('productFeed.sessionUrlUnavailable')).toBeTruthy()
        expect(screen.queryByRole('button', { name: 'productFeed.copyUrl' })).toBeNull()
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

    it('replaces the session URL on rotate and never copies the old URL', async () => {
        const user = userEvent.setup()
        installClipboardMock()
        renderDialogSessionHarness({ meta: 'https://api.shopixy.test/api/public/feeds/meta/old-secret' })

        await waitFor(() => expect(screen.getByText('productFeed.maskedUrl')).toBeTruthy())
        await user.click(screen.getByRole('button', { name: 'productFeed.rotateAria' }))
        expect(await screen.findByText('https://api.shopixy.test/api/public/feeds/meta/rotated-secret')).toBeTruthy()

        await user.click(screen.getByRole('button', { name: 'productFeed.done' }))
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))

        expect(clipboardWriteMock).toHaveBeenLastCalledWith('https://api.shopixy.test/api/public/feeds/meta/rotated-secret')
        expect(clipboardWriteMock).not.toHaveBeenCalledWith('https://api.shopixy.test/api/public/feeds/meta/old-secret')
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

    it('removes the session URL on revoke', async () => {
        const user = userEvent.setup()
        renderDialogSessionHarness({ meta: 'https://api.shopixy.test/api/public/feeds/meta/old-secret' })

        await user.click(await screen.findByRole('button', { name: 'productFeed.revokeAria' }))

        await waitFor(() => expect(apiPostMock).toHaveBeenCalledWith('/admin/feeds/meta/tokens/meta-token/revoke'))
        expect(await screen.findByText('productFeed.sessionUrlUnavailable')).toBeTruthy()
        expect(screen.queryByRole('button', { name: 'productFeed.copyUrl' })).toBeNull()
    })

    it('keeps session URLs isolated by channel', async () => {
        const user = userEvent.setup()
        installClipboardMock()
        apiGetMock.mockImplementation((url: string) => {
            const channel = ['meta', 'google', 'tiktok', 'snapchat'].find((item) => url.includes(`/${item}/`))
            return Promise.resolve({
                data: channel
                    ? [{ tokenId: `${channel}-token`, channel, createdAtUtc: '2026-08-20T00:00:00Z', revokedAtUtc: null }]
                    : [],
            })
        })
        renderDialogSessionHarness({
            meta: 'https://api.shopixy.test/api/public/feeds/meta/meta-secret',
            google: 'https://api.shopixy.test/api/public/feeds/google/google-secret',
            tiktok: 'https://api.shopixy.test/api/public/feeds/tiktok/tiktok-secret',
            snapchat: 'https://api.shopixy.test/api/public/feeds/snapchat/snapchat-secret',
        })

        for (const [label, url] of [
            ['Meta', 'https://api.shopixy.test/api/public/feeds/meta/meta-secret'],
            ['Google', 'https://api.shopixy.test/api/public/feeds/google/google-secret'],
            ['TikTok', 'https://api.shopixy.test/api/public/feeds/tiktok/tiktok-secret'],
            ['Snapchat', 'https://api.shopixy.test/api/public/feeds/snapchat/snapchat-secret'],
        ] as const) {
            await user.click(await screen.findByRole('tab', { name: new RegExp(label) }))
            await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
            expect(clipboardWriteMock).toHaveBeenLastCalledWith(url)
        }
    })

    it('handles clipboard failures without exposing the raw URL', async () => {
        const user = userEvent.setup()
        installClipboardMock()
        clipboardWriteMock.mockRejectedValueOnce(new Error('denied'))
        renderDialogSessionHarness({ meta: 'https://api.shopixy.test/api/public/feeds/meta/meta-secret' })

        await waitFor(() => expect(screen.getByText('productFeed.maskedUrl')).toBeTruthy())
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))

        expect(showErrorMock).toHaveBeenCalledWith('productFeed.copyFailed')
        expect(showErrorMock).not.toHaveBeenCalledWith(expect.stringContaining('meta-secret'))
    })

    it('does not open the masked URL when no raw session URL exists', async () => {
        renderDialog()

        await waitFor(() => expect(screen.getByText('productFeed.maskedUrl')).toBeTruthy())

        expect(screen.getByText('productFeed.sessionUrlUnavailable')).toBeTruthy()
        expect(screen.queryByRole('button', { name: 'productFeed.openFeedAria' })).toBeNull()
        expect(windowOpenMock).not.toHaveBeenCalled()
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

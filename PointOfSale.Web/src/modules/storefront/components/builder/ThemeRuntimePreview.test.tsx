import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ThemeRuntimePreview } from './ThemeRuntimePreview'

const config = {
    tenantId: 'tenant-a', storefrontTheme: 'pearl', pages: { home: { sections: [{ id: 'draft', enabled: true, order: 0, settings: { backgroundImage: '/private-capability' } }] } },
    previewChrome: { runtimeUrl: 'https://shop.example.test/draft-preview.html', navigation: { header: { announcement: { text: 'Draft announcement' }, links: [] }, footer: { links: [] } }, footerByLanguage: { en: {}, ar: {} } },
}

describe('Homepage actual theme runtime viewport', () => {
    it('keeps a real 390x844 runtime viewport when Admin framing is narrower, scaling only the outer presentation', async () => {
        let measure: (() => void) | undefined
        vi.stubGlobal('ResizeObserver', class {
            constructor(callback: () => void) { measure = callback }
            observe() {}
            disconnect() {}
        })
        const { unmount } = render(<ThemeRuntimePreview config={config} tenantId="tenant-a" language="en" viewportWidth={390} />)
        const iframe = screen.getByTitle('Selected theme Storefront draft preview') as HTMLIFrameElement
        Object.defineProperty(iframe.parentElement!, 'clientWidth', { value: 360 })
        measure?.()
        await waitFor(() => expect(iframe.style.transform).toBe(`scale(${360 / 390})`))
        expect(iframe.style.width).toBe('390px')
        expect(iframe.style.height).toBe('844px')
        expect(iframe.parentElement?.className).toContain('overflow-clip')
        unmount()
        vi.unstubAllGlobals()
    })
    it('sends the admitted draft only after exact-origin/frame/session handshake, and reload resends it', async () => {
        const { rerender } = render(<ThemeRuntimePreview config={config} tenantId="tenant-a" language="en" />)
        const iframe = screen.getByTitle('Selected theme Storefront draft preview') as HTMLIFrameElement
        const post = vi.spyOn(iframe.contentWindow!, 'postMessage')
        const nonce = new URL(iframe.src).hash.slice(1)
        const message = (origin: string, source: MessageEventSource | null, type = 'ready') => fireEvent(window, new MessageEvent('message', {
            origin, source, data: { protocol: 'shopixy.admin-draft-preview.v1', type, nonce },
        }))
        message('https://evil.example.test', iframe.contentWindow)
        message('https://shop.example.test', window)
        expect(post).not.toHaveBeenCalled()
        message('https://shop.example.test', iframe.contentWindow)
        expect(post).toHaveBeenCalledWith(expect.objectContaining({ config, language: 'en', nonce }), 'https://shop.example.test')
        message('https://shop.example.test', iframe.contentWindow, 'applied')
        rerender(<ThemeRuntimePreview config={config} tenantId="tenant-a" language="ar" />)
        await waitFor(() => expect(post).toHaveBeenCalledWith(expect.objectContaining({ config, language: 'ar' }), 'https://shop.example.test'))
        message('https://shop.example.test', iframe.contentWindow)
        expect(post).toHaveBeenCalledTimes(3)
        expect(iframe.getAttribute('sandbox')).toBe('allow-scripts allow-same-origin')
    })
    it.each([
        [{ ...config, tenantId: 'tenant-b' }],
        [{ ...config, previewChrome: null }],
        [{ ...config, previewChrome: { ...config.previewChrome, runtimeUrl: 'javascript:alert(1)' } }],
        [{ ...config, previewChrome: { ...config.previewChrome, runtimeUrl: 'https://shop.example.test/' } }],
    ])('rejects an unavailable/foreign/unsafe preview authority', invalid => {
        const { container } = render(<ThemeRuntimePreview config={invalid} tenantId="tenant-a" language="en" />)
        expect(container.querySelector('iframe')).toBeNull()
        expect(screen.getByRole('alert')).toHaveTextContent('authority is unavailable')
    })
})

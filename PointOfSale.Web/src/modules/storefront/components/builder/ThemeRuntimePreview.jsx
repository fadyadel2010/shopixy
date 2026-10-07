import { useEffect, useMemo, useRef, useState } from 'react'

const PROTOCOL = 'shopixy.admin-draft-preview.v1'

export function ThemeRuntimePreview({ config, tenantId, language, viewportWidth }) {
    const frame = useRef(null)
    const viewport = useRef(null)
    const [scale, setScale] = useState(1)
    const connected = useRef(false)
    const [error, setError] = useState(null)
    const nonce = useMemo(() => crypto.randomUUID(), [])
    const url = useMemo(() => {
        try {
            if (!config?.previewChrome || config.tenantId !== tenantId) return null
            const candidate = new URL(config.previewChrome.runtimeUrl)
            if (!['http:', 'https:'].includes(candidate.protocol) || candidate.pathname !== '/draft-preview.html') return null
            candidate.searchParams.set('tenant', tenantId)
            candidate.hash = nonce
            return candidate
        } catch { return null }
    }, [config, nonce, tenantId])

    useEffect(() => {
        if (!url) return undefined
        let timer = setTimeout(() => setError(language === 'ar' ? 'تعذر الاتصال بمعاينة المتجر.' : 'Storefront preview could not be reached.'), 15000)
        const send = () => frame.current?.contentWindow?.postMessage({ protocol: PROTOCOL, type: 'apply', nonce, config, language }, url.origin)
        const receive = event => {
            if (event.source !== frame.current?.contentWindow || event.origin !== url.origin
                || event.data?.protocol !== PROTOCOL || event.data.nonce !== nonce) return
            if (event.data.type === 'ready') { connected.current = true; send() }
            if (event.data.type === 'applied') { clearTimeout(timer); setError(null) }
        }
        window.addEventListener('message', receive)
        if (connected.current) send()
        return () => { clearTimeout(timer); window.removeEventListener('message', receive) }
    }, [config, language, nonce, url])

    useEffect(() => {
        if (!viewportWidth || !viewport.current) { setScale(1); return undefined }
        const measure = () => setScale(Math.min(1, viewport.current.clientWidth / viewportWidth))
        measure()
        const observer = new ResizeObserver(measure)
        observer.observe(viewport.current)
        return () => observer.disconnect()
    }, [viewportWidth])

    if (!url) return <p role="alert">{language === 'ar' ? 'سلطة معاينة المتجر غير متاحة.' : 'Storefront preview authority is unavailable.'}</p>
    return <>
        {error && <p role="alert" className="bg-red-50 p-4 text-red-700">{error}</p>}
        <div ref={viewport} dir="ltr" className="relative w-full overflow-clip" style={{ height: 844 * scale }}>
            <iframe ref={frame} src={url.toString()} title={language === 'ar' ? 'معاينة المتجر بالثيم المحدد' : 'Selected theme Storefront draft preview'}
                className="absolute left-0 top-0 block border-0" style={{ width: viewportWidth || '100%', height: 844, transform: `scale(${scale})`, transformOrigin: 'top left' }}
                sandbox="allow-scripts allow-same-origin" referrerPolicy="no-referrer" />
        </div>
    </>
}

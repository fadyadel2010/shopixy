import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../../../../services/api'

export const HOMEPAGE_MEDIA_PREFIX = '/api/storefront/homepage-media/'
export function isValidImageUrl(value) {
    if (!value) return true
    if (/\s|\\/.test(value) || value.startsWith('//')) return false
    if (value.startsWith('/')) return true
    if (!value.includes(':')) return true // Preserve canonical schema's existing relative image paths.
    try {
        const url = new URL(value)
        return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password
    } catch { return false }
}

export function HomepageImageField({ label, value = '', onChange }) {
    const { t } = useTranslation()
    const id = useId()
    const input = useRef(null)
    const generation = useRef(0)
    const [busy, setBusy] = useState(false)
    const [error, setError] = useState('')
    const [source, setSource] = useState('')
    const canonical = value.startsWith(HOMEPAGE_MEDIA_PREFIX)

    useEffect(() => {
        let active = true
        setError('')
        setSource('')
        if (!value) return () => { active = false }
        if (!isValidImageUrl(value)) {
            setError(t('homepageMedia.invalid'))
        } else if (canonical) {
            const mediaId = value.slice(HOMEPAGE_MEDIA_PREFIX.length)
            api.get(`/admin/homepage-media/${mediaId}/preview`).then(({ data }) => {
                if (active) setSource(data.url)
            }).catch(() => { if (active) setError(t('homepageMedia.invalid')) })
        } else setSource(value)
        return () => { active = false }
    }, [canonical, t, value])

    useEffect(() => () => { generation.current += 1 }, [])

    const upload = async (file) => {
        if (!file) return
        if (!['image/jpeg', 'image/png', 'image/webp', 'image/gif'].includes(file.type) || file.size > 10 * 1024 * 1024 || file.size === 0) {
            setError(t('homepageMedia.invalidFile'))
            return
        }
        const request = ++generation.current
        setBusy(true)
        setError('')
        try {
            const form = new FormData()
            form.append('file', file)
            const { data } = await api.post('/admin/homepage-media', form, { headers: { 'Content-Type': 'multipart/form-data' } })
            if (generation.current === request) onChange(data.reference)
        } catch {
            if (generation.current === request) setError(t('homepageMedia.error'))
        } finally {
            if (generation.current === request) setBusy(false)
            if (input.current) input.current.value = ''
        }
    }

    return <div className="min-w-0 space-y-2">
        <label htmlFor={`${id}-file`} className="block text-sm font-medium text-gray-700">{label}</label>
        <input ref={input} id={`${id}-file`} type="file" accept="image/jpeg,image/png,image/webp,image/gif"
            aria-label={label} disabled={busy} onChange={(event) => void upload(event.target.files?.[0])} className="sr-only" />
        <div className="flex flex-wrap gap-2">
            <button type="button" disabled={busy} onClick={() => input.current?.click()} className="rounded-lg bg-indigo-600 px-3 py-2 text-sm text-white disabled:opacity-50">
                {busy ? t('homepageMedia.uploading') : t(value ? 'homepageMedia.replace' : 'homepageMedia.upload')}
            </button>
            {value && <button type="button" disabled={busy} onClick={() => onChange('')} className="rounded-lg border px-3 py-2 text-sm text-red-700">{t('homepageMedia.remove')}</button>}
        </div>
        <p className="text-xs text-gray-500">{t('homepageMedia.limits')}</p>
        <p role="status" aria-live="polite" className="text-xs text-gray-600">{t(busy ? 'homepageMedia.uploading' : value ? 'homepageMedia.selected' : 'homepageMedia.noImage')}</p>
        {!canonical && <><label htmlFor={`${id}-url`} className="block text-xs text-gray-600">{t('homepageMedia.url')}</label>
            <input id={`${id}-url`} type="url" value={value} disabled={busy} onChange={(event) => onChange(event.target.value)}
                className="w-full min-w-0 rounded-lg border px-3 py-2" /></>}
        {source && <img src={source} alt={t('homepageMedia.preview')} onError={() => setError(t('homepageMedia.invalid'))} className="h-24 w-full rounded object-cover" />}
        {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    </div>
}

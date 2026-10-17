import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../../../services/api'

export default function BostaPickupSelector({ accountId, environment, configurationVersion, disabled, onSelected }) {
    const { t } = useTranslation()
    const [locations, setLocations] = useState([])
    const [busy, setBusy] = useState(false)
    const [error, setError] = useState('')
    const [selected, setSelected] = useState('')
    const [selection, setSelection] = useState(null)
    const target = useRef(null)
    const pending = useRef(false)
    useEffect(() => {
        target.current = {}; pending.current = false
        setLocations([]); setSelected(''); setSelection(null); setError(''); setBusy(false)
        return () => { target.current = null }
    }, [accountId, environment, configurationVersion])
    const load = async () => {
        if (disabled || pending.current) return
        const identity = target.current
        pending.current = true; setBusy(true); setError(''); setLocations([]); setSelected(''); setSelection(null)
        try {
            const { data } = await api.get(`/admin/shipping/carrier-accounts/${accountId}/bosta/pickup-locations`, { headers: { 'Cache-Control': 'no-store', Pragma: 'no-cache' } })
            const ids = new Set()
            if (data?.accountId !== accountId || data?.environment !== environment || !Array.isArray(data?.locations) ||
                data.locations.some(location => {
                    if (typeof location?.id !== 'string' || !/^[A-Za-z0-9_-]{1,200}$/.test(location.id) || ids.has(location.id)) return true
                    ids.add(location.id)
                    return ['cityName', 'districtName'].some(key => location[key] != null && (typeof location[key] !== 'string' || location[key].length > 200))
                }))
                throw new Error('BOSTA_PICKUP_RESPONSE_TARGET_MISMATCH')
            if (data.pickupSelection != null && (!['ProviderDefault', 'Explicit'].includes(data.pickupSelection.mode) ||
                (data.pickupSelection.resolvedLocationId != null && !ids.has(data.pickupSelection.resolvedLocationId))))
                throw new Error('BOSTA_PICKUP_RESPONSE_TARGET_MISMATCH')
            if (target.current === identity) { setLocations(data.locations); setSelection(data.pickupSelection || null) }
        } catch {
            if (target.current === identity) setError(t('shippingAdministration.bostaPickup.loadError'))
        } finally {
            if (target.current === identity) { pending.current = false; setBusy(false) }
        }
    }
    return <section className="space-y-2 rounded border border-blue-100 bg-blue-50 p-3">
        <h5 className="text-sm font-semibold">{t('shippingAdministration.bostaPickup.title')}</h5>
        <p className="text-xs text-gray-700">{t('shippingAdministration.bostaPickup.boundary')}</p>
        <button type="button" className="btn-secondary" disabled={disabled || busy} onClick={load}>{t('shippingAdministration.bostaPickup.load')}</button>
        {busy && <p role="status">{t('shippingAdministration.bostaPickup.loading')}</p>}
        {selection?.mode === 'ProviderDefault' && selection.resolvedLocationId && selection.verifiedAtUtc && <p role="status" className="text-sm text-blue-900">
            {t('shippingAdministration.bostaPickup.defaultVerified', { id: selection.resolvedLocationId })}
        </p>}
        {selection?.errorCode && <p role="alert" className="text-sm text-amber-900">{t('shippingAdministration.bostaPickup.verificationRequired')} <bdi>{selection.errorCode}</bdi></p>}
        {locations.length > 0 && <label className="block text-sm">{t('shippingAdministration.bostaPickup.select')}
            <select aria-label={t('shippingAdministration.bostaPickup.select')} className="input w-full" disabled={disabled || busy} value={selected} onChange={event => {
                const location = locations.find(item => item.id === event.target.value)
                if (location) { setSelected(location.id); onSelected(location.id) }
            }}>
                <option value="">{t('shippingAdministration.bostaPickup.select')}</option>
                {locations.map(location => <option key={location.id} value={location.id}>{location.id} — {location.cityName || ''} / {location.districtName || ''}{location.isDefault ? ` (${t('shippingAdministration.bostaPickup.defaultLabel')})` : ''}</option>)}
            </select>
        </label>}
        {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    </section>
}

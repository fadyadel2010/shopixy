import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { loadBostaNativeGeography, saveBostaNativeDestination, validateBostaNativeDestination } from '../bostaNativeApi'

export default function BostaNativeDestinationSelector({ accountId, environment, configurationVersion, disabled, shipmentId }) {
    const { t } = useTranslation()
    const [places, setPlaces] = useState([])
    const [city, setCity] = useState('')
    const [district, setDistrict] = useState('')
    const [busy, setBusy] = useState(false)
    const [feedback, setFeedback] = useState(null)
    const [saved, setSaved] = useState(false)
    const scope = useRef(null)
    const pending = useRef(false)
    useEffect(() => {
        scope.current = {}; pending.current = false; setPlaces([]); setCity(''); setDistrict(''); setFeedback(null); setBusy(false); setSaved(false)
        return () => { scope.current = null }
    }, [accountId, environment, configurationVersion, shipmentId, disabled])
    const run = async operation => {
        if (disabled || pending.current || saved) return
        const identity = scope.current
        pending.current = true; setBusy(true); setFeedback(null)
        try { await operation(identity) }
        catch (error) {
            if (scope.current === identity) setFeedback({ error: true, code: error.response?.data?.code || 'BOSTA_DESTINATION_VALIDATION_FAILED' })
        } finally { if (scope.current === identity) { pending.current = false; setBusy(false) } }
    }
    const load = () => run(async identity => {
        const data = await loadBostaNativeGeography(accountId, city || undefined)
        if (data.accountId !== accountId || (environment && data.environment !== environment) || !Array.isArray(data.places)) throw new Error('BOSTA_REFERENCE_TARGET_MISMATCH')
        if (scope.current === identity) { setPlaces(data.places); setDistrict('') }
    })
    const validate = () => run(async identity => {
        const result = await validateBostaNativeDestination(accountId, city, district)
        if (result.accountId !== accountId || result.validated !== true || result.serviceable !== true) throw new Error('BOSTA_DESTINATION_VALIDATION_FAILED')
        if (scope.current === identity) setFeedback({ error: false, key: 'validated' })
    })
    const save = () => run(async identity => {
        await saveBostaNativeDestination(accountId, shipmentId, city, district)
        if (scope.current === identity) { setSaved(true); setFeedback({ error: false, key: 'snapshotSaved' }) }
    })
    const blocked = disabled || busy || saved
    const districts = places.filter(p => p.type === 'district' && p.parentId === city)
    const selectedCity = places.find(p => p.type === 'city' && p.id === city)
    const selectedDistrict = districts.find(p => p.id === district)
    const valid = selectedCity?.dropOffAvailable === true && selectedDistrict?.dropOffAvailable === true
    return <section className="space-y-3 rounded border border-gray-200 bg-white p-3" aria-label={t('shippingAdministration.native.title')}>
        <h4 className="font-semibold">{t('shippingAdministration.native.title')}</h4>
        <p className="text-sm text-gray-600">{t('shippingAdministration.native.boundary')}</p>
        <button type="button" className="btn-secondary" disabled={blocked} onClick={load}>{t(city ? 'shippingAdministration.native.loadDistricts' : 'shippingAdministration.native.loadCities')}</button>
        <label className="block text-sm">{t('shippingAdministration.native.city')}
            <select className="input mt-1 w-full" value={city} disabled={blocked || !places.length} onChange={e => { setCity(e.target.value); setDistrict(''); setPlaces(previous => previous.filter(p => p.type !== 'district')); setFeedback(null) }}>
                <option value="">{t('shippingAdministration.native.choose')}</option>
                {places.filter(p => p.type === 'city').map(p => <option key={p.id} value={p.id} disabled={!p.dropOffAvailable}>{p.name} — {p.id}{!p.dropOffAvailable ? ` — ${t('shippingAdministration.native.unserviceable')}` : ''}</option>)}
            </select>
        </label>
        <label className="block text-sm">{t('shippingAdministration.native.district')}
            <select className="input mt-1 w-full" value={district} disabled={blocked || !districts.length} onChange={e => { setDistrict(e.target.value); setFeedback(null) }}>
                <option value="">{t('shippingAdministration.native.choose')}</option>
                {districts.map(p => <option key={p.id} value={p.id} disabled={!p.dropOffAvailable}>{p.name} — {p.id}{!p.dropOffAvailable ? ` — ${t('shippingAdministration.native.unserviceable')}` : ''}</option>)}
            </select>
        </label>
        <div className="flex flex-wrap gap-2">
            <button type="button" className="btn-secondary" disabled={blocked || !valid} onClick={validate}>{t('shippingAdministration.native.validate')}</button>
            {shipmentId && <button type="button" className="btn-secondary" disabled={blocked || !valid} onClick={save}>{t('shippingAdministration.native.saveSnapshot')}</button>}
        </div>
        {!valid && <p className="text-sm text-amber-800">{t('shippingAdministration.native.selectionRequired')}</p>}
        {feedback && <p role={feedback.error ? 'alert' : 'status'} className="text-sm">{feedback.error ? t(`shippingAdministration.native.errors.${feedback.code}`, { defaultValue: t('shippingAdministration.native.failed') }) : t(`shippingAdministration.native.${feedback.key}`)}</p>}
    </section>
}

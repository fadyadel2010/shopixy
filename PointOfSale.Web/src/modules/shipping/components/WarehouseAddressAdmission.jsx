import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../../../services/api'

const levels = ['countryPlaceId', 'administrativeAreaPlaceId', 'localityPlaceId', 'districtPlaceId', 'zonePlaceId']
const kinds = ['Country', 'AdministrativeArea', 'Locality', 'District', 'Zone']
const guid = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i
const emptyPlaces = () => Object.fromEntries(levels.map(key => [key, []]))

export default function WarehouseAddressAdmission({ warehouse, onClose }) {
    const { t, i18n } = useTranslation()
    const [form, setForm] = useState({ ...Object.fromEntries(levels.map(key => [key, ''])), streetLine1: '', postalCode: '', reason: '' })
    const [places, setPlaces] = useState(emptyPlaces)
    const [busy, setBusy] = useState(false)
    const [error, setError] = useState('')
    const [evidence, setEvidence] = useState(null)
    const sequence = useRef(0)
    const failure = err => `${err.response?.status === 403 ? t('shipping.warehouseAdmission.denied') : (err.response?.data?.message || t('shipping.warehouseAdmission.failed'))} ${err.response?.data?.code || ''}`
    const load = async (index, next, request) => {
        setBusy(true); setError('')
        try {
            const params = index ? { countryPlaceId: next.countryPlaceId, ...(index > 1 ? { parentPlaceId: next[levels[index - 1]] } : {}) } : {}
            const { data } = await api.get(`/warehouses/${warehouse.id}/address-admission/places`, { params })
            if (request !== sequence.current) return
            if (!guid.test(data.catalogReleaseId || '') || !Array.isArray(data.places) || data.places.some(p => !guid.test(p?.placeId || '') || typeof p.displayName !== 'string')) throw new Error('Invalid places contract')
            setPlaces(current => ({ ...current, [levels[index]]: data.places.filter(p => p.placeKind === kinds[index]) }))
        } catch (err) { if (request === sequence.current) setError(failure(err)) }
        finally { if (request === sequence.current) setBusy(false) }
    }
    useEffect(() => {
        load(0, form, ++sequence.current)
        return () => { sequence.current++ }
    }, [warehouse.id])
    const select = (index, value) => {
        const next = { ...form, [levels[index]]: value }
        for (const key of levels.slice(index + 1)) next[key] = ''
        setForm(next)
        setPlaces(current => ({ ...current, ...Object.fromEntries(levels.slice(index + 1).map(key => [key, []])) }))
        const request = ++sequence.current
        if (value && index < levels.length - 1) load(index + 1, next, request)
    }
    const country = places.countryPlaceId.find(p => p.placeId === form.countryPlaceId)
    const valid = levels.slice(0, 3).every(key => Boolean(form[key])) && form.streetLine1.trim() === warehouse.address?.trim() &&
        Boolean(form.reason.trim()) && (country?.postalCodePolicy !== 'Required' || Boolean(form.postalCode.trim()))
    const submit = async event => {
        event.preventDefault()
        if (!valid || busy || evidence) return
        setBusy(true); setError('')
        try {
            const { data } = await api.post(`/warehouses/${warehouse.id}/address-admission`, {
                ...form, districtPlaceId: form.districtPlaceId || null, zonePlaceId: form.zonePlaceId || null, postalCode: form.postalCode.trim() || null
            })
            if (data.warehouseId !== warehouse.id || !guid.test(data.addressAdmissionEvidenceId || '') || !guid.test(data.catalogReleaseId || '') || !/^[a-f0-9]{64}$/i.test(data.addressFingerprintSha256 || ''))
                throw new Error('Unconfirmed address admission')
            setEvidence(data.addressAdmissionEvidenceId)
        } catch (err) { setError(failure(err)) }
        finally { setBusy(false) }
    }
    return <section dir={i18n.dir()} role="dialog" aria-modal="true" aria-label={t('shipping.warehouseAdmission.title')}
        className="fixed inset-0 z-50 bg-black/50 flex items-center justify-center p-4">
        <div className="bg-white rounded-xl p-5 w-full max-w-xl max-h-[90vh] overflow-y-auto min-w-0 break-words">
            <h2 className="text-lg font-semibold">{t('shipping.warehouseAdmission.title')}</h2>
            <p className="text-sm my-2">{t('shipping.warehouseAdmission.description')}</p>
            <p className="text-sm mb-3">{warehouse.name}: {warehouse.address}</p>
            <form onSubmit={submit} className="space-y-3">
                <fieldset disabled={busy || Boolean(evidence)} className="space-y-3">
                    {levels.map((key, index) => <label key={key} className="block text-sm">{t(`shipping.warehouseAdmission.${key}`)}
                        <select aria-label={t(`shipping.warehouseAdmission.${key}`)} className="input w-full" value={form[key]} required={index < 3}
                            disabled={index > 0 && !form[levels[index - 1]]} onChange={e => select(index, e.target.value)}>
                            <option value="">{t('shipping.warehouseAdmission.select')}</option>
                            {places[key].map(p => <option key={p.placeId} value={p.placeId}>{p.canonicalCode} — {p.displayName}</option>)}
                        </select>
                    </label>)}
                    {country && <p className="text-xs">{t('shipping.warehouseAdmission.rules')}: <bdi>{country.postalCodePolicy} {country.postalCodeRegex}</bdi></p>}
                    {[['streetLine1', 'street', 300], ['postalCode', 'postal', 32], ['reason', 'reason', 1000]].map(([key, label, max]) =>
                        <label key={key} className="block text-sm">{t(`shipping.warehouseAdmission.${label}`)}
                            <input aria-label={t(`shipping.warehouseAdmission.${label}`)} className="input w-full" maxLength={max} required={key !== 'postalCode'}
                                value={form[key]} onChange={e => setForm({ ...form, [key]: e.target.value })} />
                        </label>)}
                </fieldset>
                {busy && <p role="status">{t('shipping.warehouseAdmission.loading')}</p>}
                {error && <p role="alert" className="text-red-700">{error}</p>}
                {evidence && <p role="status" className="text-green-700">{t('shipping.warehouseAdmission.saved')} <bdi>{evidence}</bdi></p>}
                <div className="flex gap-3 flex-wrap">
                    <button type="submit" className="btn-primary" disabled={busy || !valid || Boolean(evidence)}>{t('shipping.warehouseAdmission.save')}</button>
                    <button type="button" className="btn-secondary" onClick={onClose} disabled={busy}>{t('shipping.warehouseAdmission.close')}</button>
                </div>
            </form>
        </div>
    </section>
}

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../../../services/api'

const emptyMeasurement = () => ({ length: '', width: '', height: '', dimensionUnit: 'cm', grossWeight: '', weightUnit: 'kg', pieceReference: '' })
const admitView = (data, shipmentId) => {
    if (!data || typeof data !== 'object' || (data.shipmentId && data.shipmentId !== shipmentId)) {
        throw new Error('PARCEL_RESPONSE_TARGET_MISMATCH')
    }
    return { ...data, canQuote: data.shipmentId === shipmentId && data.canQuote === true &&
        Array.isArray(data.quoteBlockers) && data.quoteBlockers.length === 0,
        quoteBlockers: Array.isArray(data.quoteBlockers) ? data.quoteBlockers.filter(code => typeof code === 'string') : [] }
}

export default function ParcelEvidenceForm({ shipmentId }) {
    const { t, i18n } = useTranslation()
    const [view, setView] = useState(null)
    const [form, setForm] = useState(emptyMeasurement)
    const [error, setError] = useState('')
    const [busy, setBusy] = useState(false)
    const [quotes, setQuotes] = useState([])
    const [saved, setSaved] = useState(false)
    const activeTargetRef = useRef(null)
    const pendingOperationRef = useRef(null)
    useEffect(() => {
        let active = true
        const target = { shipmentId }
        activeTargetRef.current = target
        pendingOperationRef.current = null
        setBusy(false)
        setView(null); setSaved(false); setQuotes([]); setError('')
        setForm(emptyMeasurement())
        api.get(`/admin/shipments/${shipmentId}/parcel-evidence`).then(({ data }) => {
            if (!active) return
            setView(admitView(data, shipmentId))
            if (data.evidence) { setForm(data.evidence.measurement); setSaved(true) }
            else if (data.requiresGoodsDeclaration === true) setForm({ ...emptyMeasurement(), goods: { description: '', value: '', currency: 'EGP' } })
        }).catch(err => { if (active) setError(err.response?.data?.code || (err.message === 'PARCEL_RESPONSE_TARGET_MISMATCH' ? err.message : 'shipping.parcel.loadError')) })
        return () => {
            active = false
            activeTargetRef.current = null
            pendingOperationRef.current = null
        }
    }, [shipmentId])
    const goodsValid = view?.requiresGoodsDeclaration !== true || (
        typeof form.goods?.description === 'string' && form.goods.description.trim().length > 0 && form.goods.description.length <= 1000
        && !/[\r\n\t]/.test(form.goods.description) && /^\d+(?:\.\d{1,2})?$/.test(String(form.goods.value))
        && Number(form.goods.value) > 0 && Number.isSafeInteger(Math.round(Number(form.goods.value) * 100))
        && form.goods.currency === 'EGP')
    const valid = goodsValid && ['length', 'width', 'height', 'grossWeight'].every(k => form[k] !== '' && Number.isFinite(Number(form[k])) && Number(form[k]) > 0)
        && /^[A-Za-z0-9-]{1,35}$/.test(form.pieceReference)
    const save = async event => {
        event.preventDefault()
        if (!valid || !view?.canMeasure || saved || pendingOperationRef.current) return
        const target = activeTargetRef.current
        if (!target || target.shipmentId !== shipmentId) return
        const operation = { target }
        pendingOperationRef.current = operation
        setBusy(true); setError('')
        try {
            await api.put(`/admin/shipments/${shipmentId}/parcel-evidence`, {
                ...form, length: Number(form.length), width: Number(form.width), height: Number(form.height), grossWeight: Number(form.grossWeight), packageCount: 1,
                ...(view.requiresGoodsDeclaration === true ? { goods: { description: form.goods.description.trim(), value: Number(form.goods.value), currency: 'EGP' } } : {})
            })
            if (activeTargetRef.current !== target) return
            setSaved(true)
            // Measurement persistence is not quote permission. Refresh current
            // backend eligibility; a read failure cannot retain earlier readiness.
            setView(previous => previous ? { ...previous, canQuote: false } : null)
            try {
                const { data } = await api.get(`/admin/shipments/${shipmentId}/parcel-evidence`)
                if (activeTargetRef.current === target) setView(admitView(data, shipmentId))
            } catch (err) {
                if (activeTargetRef.current === target) setError(err.response?.data?.code ||
                    (err.message === 'PARCEL_RESPONSE_TARGET_MISMATCH' ? err.message : t('shipping.parcel.loadError')))
            }
        } catch (err) {
            if (activeTargetRef.current === target) setError(err.response?.data?.code || t('shipping.parcel.saveError'))
        } finally {
            if (pendingOperationRef.current === operation) {
                pendingOperationRef.current = null
                setBusy(false)
            }
        }
    }
    const quote = async () => {
        if (!saved || view?.canQuote !== true || pendingOperationRef.current) return
        const target = activeTargetRef.current
        if (!target || target.shipmentId !== shipmentId) return
        const operation = { target }
        pendingOperationRef.current = operation
        setBusy(true); setError(''); setQuotes([])
        try {
            const { data } = await api.post(`/admin/shipments/${shipmentId}/parcel-quote`)
            if (activeTargetRef.current === target) setQuotes(data.quotes || [])
        } catch (err) {
            if (activeTargetRef.current === target) setError(err.response?.data?.errorCode || t('shipping.parcel.quoteError'))
        } finally {
            if (pendingOperationRef.current === operation) {
                pendingOperationRef.current = null
                setBusy(false)
            }
        }
    }
    return <section dir={i18n.language === 'ar' ? 'rtl' : 'ltr'} className="bg-white border border-gray-200 rounded-xl p-6" aria-label={t('shipping.parcel.title')}>
        <h3 className="text-lg font-semibold mb-2">{t('shipping.parcel.title')}</h3>
        <p className="text-sm text-gray-600 mb-4">{t('shipping.parcel.explanation')}</p>
        <p className="text-sm mb-4">{t('shipping.parcel.singlePackage')}</p>
        {view?.contents?.map(item => <p key={item.orderItemId} className="text-xs mb-2"><code>{item.productId}</code> × {item.quantity}</p>)}
        <form onSubmit={save}>
            <fieldset disabled={busy || saved || !view?.canMeasure} className="grid grid-cols-2 gap-4">
                {['length', 'width', 'height', 'grossWeight'].map(key => <label key={key} className="text-sm">
                    {t(`shipping.parcel.${key}`)} ({key === 'grossWeight' ? form.weightUnit : form.dimensionUnit})
                    <input aria-label={t(`shipping.parcel.${key}`)} type="number" min="0.000001" step="any" required dir="ltr" value={form[key]}
                        onChange={e => setForm({ ...form, [key]: e.target.value })} className="block w-full border rounded p-2" />
                </label>)}
                <label>{t('shipping.parcel.dimensionUnit')}<select aria-label={t('shipping.parcel.dimensionUnit')} value={form.dimensionUnit} onChange={e => setForm({ ...form, dimensionUnit: e.target.value })} className="block border rounded p-2">
                    {['cm', 'mm', 'in'].map(unit => <option key={unit}>{unit}</option>)}
                </select></label>
                <label>{t('shipping.parcel.weightUnit')}<select aria-label={t('shipping.parcel.weightUnit')} value={form.weightUnit} onChange={e => setForm({ ...form, weightUnit: e.target.value })} className="block border rounded p-2">
                    {['kg', 'g', 'lb', 'oz'].map(unit => <option key={unit}>{unit}</option>)}
                </select></label>
                <label className="col-span-2">{t('shipping.parcel.pieceReference')}<input aria-label={t('shipping.parcel.pieceReference')} required maxLength={35} pattern="[A-Za-z0-9-]+" dir="ltr" value={form.pieceReference} onChange={e => setForm({ ...form, pieceReference: e.target.value })} className="block border rounded p-2 w-full" /></label>
                {view?.requiresGoodsDeclaration === true && <>
                    <p className="col-span-2 text-sm text-amber-800">{t('shipping.parcel.goodsExplanation')}</p>
                    <label className="col-span-2">{t('shipping.parcel.goodsDescription')}<input aria-label={t('shipping.parcel.goodsDescription')} required maxLength={1000} value={form.goods?.description ?? ''}
                        onChange={e => setForm({ ...form, goods: { ...form.goods, description: e.target.value } })} className="block border rounded p-2 w-full" /></label>
                    <label className="col-span-2">{t('shipping.parcel.goodsValue')} (EGP)<input aria-label={t('shipping.parcel.goodsValue')} type="number" required min="0.01" step="0.01" dir="ltr" value={form.goods?.value ?? ''}
                        onChange={e => setForm({ ...form, goods: { ...form.goods, value: e.target.value } })} className="block border rounded p-2 w-full" /></label>
                </>}
            </fieldset>
            <p className="text-sm my-3">{t('shipping.parcel.immutable')}</p>
            {!valid && !saved && <p className="text-sm text-amber-800 mb-3">{t('shipping.parcel.required')}</p>}
            <button disabled={busy || saved || !view?.canMeasure || !valid} className="px-4 py-2 rounded bg-blue-600 text-white disabled:opacity-50">{t('shipping.parcel.save')}</button>
        </form>
        {saved && <p role="status" className="text-green-700 my-3">{t('shipping.parcel.saved')}</p>}
        {view?.isInternational && <p className="text-amber-800 my-3">{t('shipping.parcel.customsBlocked')}</p>}
        {view && view.isInternational == null && <p className="text-amber-800 my-3">{t('shipping.parcel.routeUnresolved')}</p>}
        {view?.registrationState && <p className="text-sm my-3">{t('shipping.parcel.registration')}: {view.registrationState} {view.registrationError}</p>}
        <button onClick={quote} disabled={busy || !saved || view?.canQuote !== true} className="px-4 py-2 border rounded my-3 disabled:opacity-50">{t('shipping.parcel.estimate')}</button>
        {view?.quoteBlockers?.length > 0 && <p className="text-sm text-amber-800 my-2">{view.quoteBlockers.map(code => `${t(`shipping.parcel.codes.${code}`, { defaultValue: code })} (${code})`).join(' — ')}</p>}
        <p className="text-xs text-gray-600">{t('shipping.parcel.estimateOnly')}</p>
        {quotes.map((q, i) => <p key={i}>{q.serviceName}: {q.cost} {q.currency}</p>)}
        {error && <p role="alert" className="text-red-700 mt-3">{t('shipping.parcel.error')}: {t(`shipping.parcel.codes.${error}`, { defaultValue: t(error) })} ({error})</p>}
    </section>
}

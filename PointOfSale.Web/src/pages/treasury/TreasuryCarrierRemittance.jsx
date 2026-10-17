import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../../hooks/useRTL'
import { useAuthStore } from '../../store/authStore'
import { hasPosPermission } from '../../auth/posPermissions'
import { admitCarrierReceipt, admitCarrierSelection, getCarrierRemittanceOrders, getCarrierRemittance, receiveCarrierCash } from '../../services/carrierRemittanceApi'

export default function TreasuryCarrierRemittance() {
    const user = useAuthStore(s => s.user)
    const { t } = useTranslation()
    if (!hasPosPermission(user, 'storefront.payments.manage') || !hasPosPermission(user, 'finance.operate'))
        return <p role="alert">{t('carrierRemittance.unauthorized')}</p>
    const key = `carrier-receipt:${user?.tenantId}:${user?.id}`
    return <ReceiptWorkspace key={key} storageKey={key} />
}

function ReceiptWorkspace({ storageKey }) {
    const { t } = useTranslation()
    const { dir, locale } = useRTL()
    const [page, setPage] = useState(1)
    const [data, setData] = useState(null)
    const [selected, setSelected] = useState([])
    const [receivedAt, setReceivedAt] = useState('')
    const [reference, setReference] = useState('')
    const [notes, setNotes] = useState('')
    const [evidenceReference, setEvidenceReference] = useState('')
    const [attested, setAttested] = useState(false)
    const [loading, setLoading] = useState(true)
    const [busy, setBusy] = useState(false)
    const [review, setReview] = useState(false)
    const [error, setError] = useState(null)
    const [result, setResult] = useState(null)
    const [refresh, setRefresh] = useState(0)
    const [pending, setPending] = useState(() => {
        try { return JSON.parse(sessionStorage.getItem(storageKey)) } catch { return null }
    })
    const submitting = useRef(false)
    const expectedReceipt = useRef(null)
    const binding = order => `${order.carrierCode}:${order.configurationId}:${order.accountId}:${order.environment}:${order.currency}`
    const chosen = data?.orders.filter(o => selected.includes(o.orderId)) || []
    // Selection amounts have SQL numeric(18,2) precision. Sum their minor units
    // before comparing the reviewed amount with the exact Posted receipt.
    const total = chosen.reduce((sum, order) => sum + Math.round(order.amount * 100), 0) / 100
    const locked = busy || !!pending || review
    const valid = selected.length > 0 && selected.length <= 100 && reference.trim() && receivedAt &&
        Number.isFinite(new Date(receivedAt).getTime()) && new Date(receivedAt) <= new Date() && attested
    const money = (value, currency = data?.currency) => currency
        ? new Intl.NumberFormat(locale, { style: 'currency', currency }).format(value) : '—'

    const accept = (value, request, expected) => {
        admitCarrierReceipt(value, request, expected)
        setResult(value)
        setPending(null)
        sessionStorage.removeItem(storageKey)
        sessionStorage.setItem(`${storageKey}:last`, value.operationId)
        sessionStorage.setItem(`${storageKey}:last-request`, JSON.stringify(request))
        setReview(false)
        setAttested(false)
        setReference('')
        setReceivedAt('')
        setNotes('')
        setEvidenceReference('')
        setRefresh(n => n + 1)
    }
    useEffect(() => {
        const controller = new AbortController()
        const id = pending?.operationId || sessionStorage.getItem(`${storageKey}:last`)
        try { expectedReceipt.current = JSON.parse(sessionStorage.getItem(`${storageKey}:expected`)) } catch { expectedReceipt.current = null }
        if (id) getCarrierRemittance(id, controller.signal).then(value => {
            if (!controller.signal.aborted) {
                let original = pending
                if (!original) { try { original = JSON.parse(sessionStorage.getItem(`${storageKey}:last-request`)) } catch { /* Old session has only an operation locator. */ } }
                accept(value, original || { operationId: id }, expectedReceipt.current)
            }
        }).catch(() => { /* Preserve exact request until a Posted result is recovered. */ })
        return () => controller.abort()
    }, [storageKey])
    useEffect(() => {
        const controller = new AbortController()
        setLoading(true); setData(null); setSelected([])
        getCarrierRemittanceOrders(page, controller.signal).then(value => {
            if (!controller.signal.aborted) { setData(admitCarrierSelection(value)); setError(current => current === 'loadError' ? null : current) }
        }).catch(() => { if (!controller.signal.aborted) setError('loadError') })
            .finally(() => { if (!controller.signal.aborted) setLoading(false) })
        return () => controller.abort()
    }, [page, refresh])

    const submit = async () => {
        if (submitting.current || (!pending && !valid)) return
        const request = pending || { operationId: crypto.randomUUID(), orderIds: [...selected].sort(),
            receivedAt: new Date(receivedAt).toISOString(), receiptReference: reference.trim().normalize('NFKC'), actualReceiptAttested: true,
            receiptChannel: 'PhysicalMerchantCash', notes: notes.trim() || null, evidenceReference: evidenceReference.trim() || null }
        // A lost response/reload must keep exactly the original identity and receipt facts.
        if (!pending) {
            expectedReceipt.current = { receivedAmount: total, currency: data.currency, carrierCode: chosen[0].carrierCode,
                configurationId: chosen[0].configurationId, accountId: chosen[0].accountId, environment: chosen[0].environment }
            sessionStorage.setItem(`${storageKey}:expected`, JSON.stringify(expectedReceipt.current))
        }
        sessionStorage.setItem(storageKey, JSON.stringify(request))
        setPending(request); submitting.current = true; setBusy(true); setError(null); setResult(null)
        try { accept(await receiveCarrierCash(request), request, expectedReceipt.current) }
        catch (failure) { setError(failure?.response?.status === 409 ? 'reviewRequired' : 'unknownOutcome') }
        finally { submitting.current = false; setBusy(false) }
    }
    return <div dir={dir} className="space-y-5 max-w-5xl mx-auto">
        <Link to="/treasury/clearing-aging" className="underline">{t('common.back')}</Link>
        <h1 className="text-2xl font-bold">{t('carrierRemittance.title')}</h1>
        <p>{t('carrierRemittance.description')}</p>
        <p className="card bg-amber-50">{t('carrierRemittance.boundary')}</p>
        {error && <p role="alert" className="card bg-red-50">{t(`carrierRemittance.${error}`)}</p>}
        {result && <section role="status" className="card bg-green-50 space-y-2">
            <h2>{t('carrierRemittance.posted')}</h2>
            <p><bdi>{result.operationId}</bdi> · <bdi>{result.journalReference}</bdi></p>
            <p>{money(result.receivedAmount, result.currency)} · {t('carrierRemittance.operatorAttested')}</p>
            <p>{t('carrierRemittance.receiptReference')}: <bdi>{result.receiptReference}</bdi></p>
            <p>{t('carrierRemittance.receivedAt')}: <bdi>{new Date(result.receivedAtUtc).toLocaleString(locale)}</bdi></p>
            <Link to={`/treasury/general-ledger?accountId=${result.destinationAccountId}`} className="underline">{t('carrierRemittance.viewLedger')}</Link>
        </section>}
        {pending && <section className="card bg-amber-50 space-y-2">
            <p>{t('carrierRemittance.unknownOutcome')}</p><bdi>{pending.operationId}</bdi>
            <button className="btn-primary" disabled={busy} onClick={submit}>{t('carrierRemittance.retrySame')}</button>
        </section>}
        {loading ? <p role="status">{t('common.loading')}</p> : data && <>
            <section className="card space-y-3">
                <h2>{t('carrierRemittance.eligibleOrders')}</h2>
                {!data.orders.length && <p>{t('carrierRemittance.empty')}</p>}
                <p>{t('carrierRemittance.sameBinding')}</p>
                {data.orders.map(order => <label key={order.orderId} className="flex gap-3 items-center py-2">
                    <input type="checkbox" disabled={locked || (chosen.length > 0 && binding(order) !== binding(chosen[0]))}
                        checked={selected.includes(order.orderId)} onChange={e => setSelected(ids => e.target.checked ? [...ids, order.orderId] : ids.filter(id => id !== order.orderId))} />
                    <span><bdi>{order.orderReference}</bdi><span className="block text-sm"><bdi>{order.carrierCode} · {order.accountId} · {order.environment} · {order.currency}</bdi></span></span>
                    <bdi className="ms-auto">{money(order.amount, order.currency)}</bdi>
                </label>)}
                <button className="btn-secondary" disabled={page === 1 || locked} onClick={() => setPage(p => p - 1)}>{t('common.previous')}</button>
                <button className="btn-secondary" disabled={!data.hasMore || locked} onClick={() => setPage(p => p + 1)}>{t('common.next')}</button>
                <button className="btn-secondary" disabled={locked} onClick={() => setRefresh(n => n + 1)}>{t('common.refresh')}</button>
            </section>
            <section className="card space-y-4">
                <p>{t('carrierRemittance.posting')}: <bdi>Dr 1010 / Cr 1130</bdi> · {money(total)} · {t('carrierRemittance.zeroFees')}</p>
                <label className="block">{t('carrierRemittance.receivedAt')}<input className="input block" type="datetime-local" value={receivedAt} disabled={locked} onChange={e => setReceivedAt(e.target.value)} /></label>
                <label className="block">{t('carrierRemittance.receiptReference')}<input className="input block" maxLength={100} value={reference} disabled={locked} onChange={e => setReference(e.target.value)} /></label>
                <label className="block">{t('carrierRemittance.notes')}<textarea className="input block" maxLength={1000} value={notes} disabled={locked} onChange={e => setNotes(e.target.value)} /></label>
                <label className="block">{t('carrierRemittance.evidenceReference')}<input className="input block" maxLength={500} value={evidenceReference} disabled={locked} onChange={e => setEvidenceReference(e.target.value)} /></label>
                <label className="flex gap-3"><input type="checkbox" checked={attested} disabled={locked} onChange={e => setAttested(e.target.checked)} />{t('carrierRemittance.attestation')}</label>
                {review ? <div className="space-y-3"><p>{t('carrierRemittance.confirmText')}</p>
                    <button className="btn-primary" disabled={busy || !!pending || !valid} onClick={submit}>{t('carrierRemittance.confirm')}</button>
                    <button className="btn-secondary" disabled={busy || !!pending} onClick={() => setReview(false)}>{t('common.cancel')}</button>
                </div> : <button className="btn-primary" disabled={!valid || !!pending || busy} onClick={() => setReview(true)}>{t('carrierRemittance.review')}</button>}
            </section>
        </>}
    </div>
}

import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../../hooks/useRTL'
import { useAuthStore } from '../../store/authStore'
import { getManualCodOrders, getManualCodSettlement, settleManualCod } from '../../services/manualCodSettlementApi'

export default function TreasuryManualCodSettlement() {
    const { t } = useTranslation()
    const { dir, locale } = useRTL()
    const user = useAuthStore(state => state.user)
    const storageKey = `manual-cod:${user?.tenantId}:${user?.id}`
    const [page, setPage] = useState(1)
    const [data, setData] = useState(null)
    const [selected, setSelected] = useState([])
    const [loading, setLoading] = useState(true)
    const [busy, setBusy] = useState(false)
    const [error, setError] = useState(null)
    const [result, setResult] = useState(null)
    const [confirm, setConfirm] = useState(false)
    const [refresh, setRefresh] = useState(0)
    const [pending, setPending] = useState(() => {
        try {
            const value = JSON.parse(sessionStorage.getItem(storageKey))
            return value?.settlementId && Array.isArray(value.orderIds) ? value : null
        } catch { return null }
    })
    const submitting = useRef(false)
    useEffect(() => {
        const controller = new AbortController()
        const lastId = pending?.settlementId || sessionStorage.getItem(`${storageKey}:last`)
        if (lastId) getManualCodSettlement(lastId, controller.signal).then(value => {
            if (!controller.signal.aborted) {
                setResult(value)
                setPending(null)
                sessionStorage.removeItem(storageKey)
                sessionStorage.setItem(`${storageKey}:last`, value.settlementId)
            }
        }).catch(() => { /* Unknown operation retains its original retry request. */ })
        return () => controller.abort()
    }, [storageKey])
    useEffect(() => {
        const controller = new AbortController()
        setLoading(true)
        setData(null)
        setSelected([])
        getManualCodOrders(page, controller.signal).then(value => {
            if (!controller.signal.aborted) { setData(value); setError(null) }
        }).catch(() => {
            if (!controller.signal.aborted) setError('loadError')
        }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
        return () => controller.abort()
    }, [page, refresh])
    const money = value => new Intl.NumberFormat(locale, { style: 'currency', currency: data?.currency || result?.currency || 'EGP' }).format(value)
    const total = (data?.orders || []).filter(order => selected.includes(order.orderId)).reduce((sum, order) => sum + order.amount, 0)
    const submit = async () => {
        if (submitting.current) return
        const request = pending || { settlementId: crypto.randomUUID(), orderIds: [...selected].sort() }
        if (!request.orderIds.length) return
        // Persist the exact request BEFORE sending it. Reload/timeouts always retry this operation identity.
        sessionStorage.setItem(storageKey, JSON.stringify(request))
        setPending(request)
        submitting.current = true
        setBusy(true)
        setError(null)
        try {
            const value = await settleManualCod(request)
            setResult(value)
            sessionStorage.removeItem(storageKey)
            sessionStorage.setItem(`${storageKey}:last`, value.settlementId)
            setPending(null)
            setConfirm(false)
            setRefresh(value => value + 1)
        } catch (failure) {
            // A failed/unknown response never authorizes a new operation for these orders.
            setError(failure?.response?.status === 409 ? 'conflict' : 'retryNotice')
        } finally { submitting.current = false; setBusy(false) }
    }
    return <div dir={dir} className="space-y-5 max-w-5xl mx-auto">
        <Link className="text-primary-700 underline" to="/treasury/clearing-aging">{t('common.back')}</Link>
        <h1 className="text-2xl font-bold">{t('manualCod.title')}</h1>
        <p className="text-gray-600">{t('manualCod.description')}</p>
        {result && <section role="status" className="card bg-green-50 border-green-200 space-y-2">
            <h2 className="font-bold">{t('manualCod.success')}</h2>
            <p>{t('manualCod.settlementReference')}: <bdi className="break-all">{result.settlementId}</bdi></p>
            <p>{t('manualCod.journalReference')}: <bdi>{result.journalReference}</bdi></p>
            <p>{money(result.grossAmount)} · {result.settledOrderCount} {t('manualCod.orders')} · {t('manualCod.posted')}</p>
            <Link className="underline" to={`/treasury/general-ledger?accountId=${result.destinationAccountId}`}>{t('manualCod.viewLedger')}</Link>
        </section>}
        {error && <div role="alert" className="card bg-red-50 text-red-800">{t(`manualCod.${error}`)}</div>}
        {pending && <section className="card bg-amber-50 space-y-3">
            <p>{t('manualCod.retryNotice')}</p>
            <p className="break-all"><bdi>{pending.settlementId}</bdi></p>
            <button className="btn-primary" disabled={busy} onClick={submit}>{t('manualCod.retrySame')}</button>
        </section>}
        {loading ? <p role="status">{t('common.loading')}</p> : data && <>
            <section className="card space-y-3">
                <h2 className="font-bold">{t('manualCod.eligibleOrders')}</h2>
                {!data.orders.length && <p>{t('manualCod.empty')}</p>}
                <div className="divide-y divide-gray-200">
                    {data.orders.map(order => <label key={order.orderId} className="flex items-center gap-3 py-3">
                        <input type="checkbox" checked={selected.includes(order.orderId)} disabled={busy || !!pending || confirm}
                            onChange={event => setSelected(ids => event.target.checked ? [...ids, order.orderId] : ids.filter(id => id !== order.orderId))} />
                        <span className="flex-1 min-w-0"><bdi className="break-all font-medium">{order.orderReference}</bdi>
                            <span className="block text-sm text-green-700">{t('manualCod.collectedPosted')}</span></span>
                        <bdi className="text-end">{money(order.amount)}</bdi>
                    </label>)}
                </div>
                <div className="flex flex-wrap gap-3">
                    <button className="btn-secondary" disabled={page === 1 || busy || !!pending || confirm} onClick={() => setPage(value => value - 1)}>{t('common.previous')}</button>
                    <button className="btn-secondary" disabled={!data.hasMore || busy || !!pending || confirm} onClick={() => setPage(value => value + 1)}>{t('common.next')}</button>
                    <button className="btn-secondary" disabled={busy || !!pending || confirm} onClick={() => setRefresh(value => value + 1)}>{t('common.refresh')}</button>
                </div>
            </section>
            <section className="card space-y-3">
                <p className="font-bold">{t('manualCod.destination')}: {t('manualCod.cashbox')} <bdi>1010</bdi></p>
                <p>{t('manualCod.selectedTotal')}: <bdi>{money(total)}</bdi> · <bdi>{data.currency}</bdi></p>
                <div className="bg-slate-50 p-3 rounded-lg space-y-2">
                    <p>{t('manualCod.debit')} {t('manualCod.cashbox')} <bdi>1010</bdi>: <bdi>{money(total)}</bdi></p>
                    <p>{t('manualCod.credit')} {t('manualCod.clearing')} <bdi>1130</bdi>: <bdi>{money(total)}</bdi></p>
                </div>
                {confirm ? <div className="space-y-3">
                    <p>{t('manualCod.confirmText')}</p>
                    <div className="flex flex-wrap gap-3">
                        <button className="btn-primary" disabled={busy || !!pending} onClick={submit}>{t('manualCod.confirm')}</button>
                        <button className="btn-secondary" disabled={busy || !!pending} onClick={() => setConfirm(false)}>{t('common.cancel')}</button>
                    </div>
                </div> : <button className="btn-primary w-full sm:w-auto" disabled={!selected.length || busy || !!pending} onClick={() => setConfirm(true)}>{t('manualCod.review')}</button>}
            </section>
        </>}
        {!loading && !data && <button className="btn-secondary" onClick={() => setRefresh(value => value + 1)}>{t('common.retry')}</button>}
    </div>
}

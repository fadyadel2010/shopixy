import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../../hooks/useRTL'
import { useAuthStore } from '../../store/authStore'
import { getManualWalletPayments, previewManualWalletSettlement, settleManualWallet, getManualWalletSettlement } from '../../services/manualWalletSettlementApi'

export default function TreasuryManualWalletSettlement() {
    const { t } = useTranslation()
    const { dir, locale } = useRTL()
    const user = useAuthStore(state => state.user)
    const storageKey = `manual-wallet:${user?.tenantId}:${user?.id}`
    const [data, setData] = useState(null)
    const [page, setPage] = useState(1)
    const [refresh, setRefresh] = useState(0)
    const [selected, setSelected] = useState([])
    const [preview, setPreview] = useState(null)
    const [attested, setAttested] = useState(false)
    const [loading, setLoading] = useState(true)
    const [busy, setBusy] = useState(false)
    const [error, setError] = useState(null)
    const [result, setResult] = useState(null)
    const [pending, setPending] = useState(() => {
        try {
            const value = JSON.parse(sessionStorage.getItem(storageKey))
            return value?.settlementId && value.previewToken && Array.isArray(value.orderIds) ? value : null
        } catch { return null }
    })
    const submitting = useRef(false)
    const complete = value => {
        setResult(value)
        sessionStorage.removeItem(storageKey)
        sessionStorage.setItem(`${storageKey}:last`, value.settlementId)
        setPending(null)
        setPreview(null)
        setAttested(false)
        setRefresh(value => value + 1)
    }
    useEffect(() => {
        const controller = new AbortController()
        const id = pending?.settlementId || sessionStorage.getItem(`${storageKey}:last`)
        if (id) getManualWalletSettlement(id, controller.signal).then(value => {
            if (!controller.signal.aborted) complete(value)
        }).catch(() => { /* Preserve uncertain operation identity; read failure never authorizes another posting. */ })
        return () => controller.abort()
        // Session authority, not mutable UI selection, controls recovery.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [storageKey])
    useEffect(() => {
        const controller = new AbortController()
        setLoading(true)
        setData(null)
        setSelected([])
        setPreview(null)
        getManualWalletPayments(page, controller.signal).then(value => {
            if (!controller.signal.aborted) setData(value)
        }).catch(() => { if (!controller.signal.aborted) setError('loadError') })
            .finally(() => { if (!controller.signal.aborted) setLoading(false) })
        return () => controller.abort()
    }, [page, refresh])
    const money = value => new Intl.NumberFormat(locale, { style: 'currency', currency: data?.currency || result?.currency || 'EGP' }).format(value)
    const review = async () => {
        if (submitting.current || !selected.length || pending) return
        submitting.current = true
        setBusy(true)
        setError(null)
        try { setPreview(await previewManualWalletSettlement([...selected].sort())); setAttested(false) }
        catch { setError('conflict'); setRefresh(value => value + 1) }
        finally { submitting.current = false; setBusy(false) }
    }
    const submit = async () => {
        if (submitting.current || (!pending && (!preview || !attested))) return
        const request = pending || { settlementId: crypto.randomUUID(), orderIds: preview.payments.map(p => p.orderId).sort(), previewToken: preview.previewToken }
        sessionStorage.setItem(storageKey, JSON.stringify(request))
        setPending(request)
        submitting.current = true
        setBusy(true)
        setError(null)
        try { complete(await settleManualWallet(request)) }
        catch (failure) {
            // Only a definitive conflict plus an authoritative not-found read permits a NEW preview/identity.
            if (failure?.response?.status === 409) {
                try { complete(await getManualWalletSettlement(request.settlementId)) }
                catch (readFailure) {
                    if (readFailure?.response?.status === 404) {
                        sessionStorage.removeItem(storageKey)
                        setPending(null)
                        setPreview(null)
                        setRefresh(value => value + 1)
                    }
                    setError('conflict')
                }
            } else setError('retryNotice')
        } finally { submitting.current = false; setBusy(false) }
    }
    return <div dir={dir} className="space-y-5 max-w-5xl mx-auto">
        <Link className="text-primary-700 underline" to="/treasury/clearing-aging">{t('common.back')}</Link>
        <h1 className="text-2xl font-bold">{t('manualWalletSettlement.title')}</h1>
        <p className="text-gray-600">{t('manualWalletSettlement.description')}</p>
        {result && <section role="status" className="card bg-green-50 border-green-200 space-y-2">
            <h2 className="font-bold">{t('manualWalletSettlement.success')}</h2>
            <p>{t('manualWalletSettlement.settlementReference')}: <bdi className="break-all">{result.settlementId}</bdi></p>
            <p>{t('manualWalletSettlement.journalReference')}: <bdi>{result.journalReference}</bdi></p>
            <p>{money(result.grossAmount)} · {result.settledOrderCount} {t('manualWalletSettlement.payments')}</p>
            <Link className="underline" to={`/treasury/general-ledger?accountId=${result.destinationAccountId}`}>{t('manualWalletSettlement.viewLedger')}</Link>
        </section>}
        {error && <div role="alert" className="card bg-red-50 text-red-800">{t(`manualWalletSettlement.${error}`)}</div>}
        {pending && <section className="card bg-amber-50 space-y-3">
            <p>{t('manualWalletSettlement.retryNotice')}</p><bdi className="break-all">{pending.settlementId}</bdi>
            <button className="btn-primary" disabled={busy} onClick={submit}>{t('manualWalletSettlement.retrySame')}</button>
        </section>}
        {loading ? <p role="status">{t('common.loading')}</p> : data && <>
            <section className="card space-y-3">
                <h2 className="font-bold">{t('manualWalletSettlement.eligiblePayments')}</h2>
                <p>{t('manualWalletSettlement.clearing')} <bdi>{data.sourceAccountCode}</bdi>: <bdi>{money(data.clearingBalance)}</bdi></p>
                <p>{t('manualWalletSettlement.cashbox')} <bdi>{data.destinationAccountCode}</bdi>: <bdi>{money(data.cashboxBalance)}</bdi></p>
                {!data.payments.length && <p>{t('manualWalletSettlement.empty')}</p>}
                <div className="divide-y divide-gray-200">
                    {data.payments.map(payment => <label key={payment.orderId} className="flex items-center gap-3 py-3">
                        <input type="checkbox" checked={selected.includes(payment.orderId)} disabled={!payment.eligible || busy || !!pending || !!preview}
                            onChange={event => setSelected(ids => event.target.checked ? [...ids, payment.orderId] : ids.filter(id => id !== payment.orderId))} />
                        <span className="flex-1 min-w-0"><bdi className="break-all font-medium">{payment.orderReference}</bdi>
                            <bdi className="block break-all text-sm">{payment.paymentReference}</bdi>
                            {payment.customerDisplayName && <span className="block text-sm">{payment.customerDisplayName}</span>}
                            <span className={`block text-sm ${payment.eligible ? 'text-green-700' : 'text-red-700'}`}>
                                {payment.eligible ? t('manualWalletSettlement.approvedPosted') : t(`manualWalletSettlement.blocks.${payment.blockedReason}`, t('manualWalletSettlement.blocked'))}
                            </span>
                            <span className="block text-sm">{t('manualWalletSettlement.approvedAt')}: {new Date(payment.approvedAtUtc).toLocaleString(locale)} · {Math.max(0, Math.floor((Date.now() - new Date(payment.approvedAtUtc)) / 86400000))} {t('common.days')}</span>
                        </span>
                        <span className="text-end"><bdi>{money(payment.amount)}</bdi><span className="block text-sm">{t('manualWalletSettlement.remaining')}: <bdi>{money(payment.remainingAmount)}</bdi></span></span>
                    </label>)}
                </div>
                <div className="flex flex-wrap gap-3">
                    <button className="btn-secondary" disabled={page === 1 || busy || !!pending || !!preview} onClick={() => setPage(value => value - 1)}>{t('common.previous')}</button>
                    <button className="btn-secondary" disabled={!data.hasMore || busy || !!pending || !!preview} onClick={() => setPage(value => value + 1)}>{t('common.next')}</button>
                    <button className="btn-secondary" disabled={busy || !!pending} onClick={() => setRefresh(value => value + 1)}>{t('common.refresh')}</button>
                </div>
            </section>
            <section className="card space-y-3">
                {preview ? <>
                    <h2 className="font-bold">{t('manualWalletSettlement.serverPreview')}</h2>
                    {preview.payments.map(payment => <p key={payment.orderId}><bdi>{payment.orderReference} · {payment.paymentReference} · {money(payment.remainingAmount)}</bdi></p>)}
                    <p>{t('manualWalletSettlement.debit')} {t('manualWalletSettlement.cashbox')} <bdi>{preview.destinationAccountCode}: {money(preview.grossAmount)}</bdi></p>
                    <p>{t('manualWalletSettlement.credit')} {t('manualWalletSettlement.clearing')} <bdi>{preview.sourceAccountCode}: {money(preview.grossAmount)}</bdi></p>
                    <p>{t('manualWalletSettlement.confirmText')}</p>
                    <label className="flex items-center gap-2"><input type="checkbox" checked={attested} disabled={busy || !!pending} onChange={event => setAttested(event.target.checked)} />{t('manualWalletSettlement.attestation')}</label>
                    <div className="flex flex-wrap gap-3">
                        <button className="btn-primary" disabled={!attested || busy || !!pending} onClick={submit}>{t('manualWalletSettlement.confirm')}</button>
                        <button className="btn-secondary" disabled={busy || !!pending} onClick={() => setPreview(null)}>{t('common.cancel')}</button>
                    </div>
                </> : <button className="btn-primary" disabled={!selected.length || busy || !!pending} onClick={review}>{t('manualWalletSettlement.review')}</button>}
            </section>
        </>}
        {!loading && !data && <button className="btn-secondary" onClick={() => setRefresh(value => value + 1)}>{t('common.retry')}</button>}
    </div>
}

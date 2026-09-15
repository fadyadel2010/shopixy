import { useCallback, useEffect, useRef, useState } from 'react'
import { CheckCircle2, Landmark, Loader2, Plus, RotateCcw, ShieldCheck, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useToast } from '../components/Toast'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'
import { isRequestCanceled } from '../services/api'
import { formatDate, formatDateTime } from '../utils/dateFormat'
import {
    getCapitalizationErrorMessage,
    resolveCapitalizationAttempt,
    TREASURY_CAPITALIZATION_DECLARATION,
    treasuryInitialCapitalizationApi,
} from '../services/treasuryInitialCapitalizationApi'

const today = () => new Date().toISOString().slice(0, 10)

const emptyForm = () => ({
    cutoverDate: today(),
    evidenceReference: '',
    notes: '',
    allocations: [{ treasuryAccountId: '', amount: '' }],
    declarationAcknowledged: false,
    confirmationAcknowledged: false,
})

const statusClass = (status) => status === 'Posted'
    ? 'bg-emerald-100 text-emerald-800'
    : status === 'Reversed'
        ? 'bg-slate-200 text-slate-700'
        : 'bg-amber-100 text-amber-800'

export default function TreasuryInitialCapitalization() {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const [destinations, setDestinations] = useState([])
    const [documents, setDocuments] = useState([])
    const [form, setForm] = useState(emptyForm)
    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)
    const [pageError, setPageError] = useState('')
    const [formError, setFormError] = useState('')
    const [postedDocument, setPostedDocument] = useState(null)
    const [reversal, setReversal] = useState(null)
    const [reversing, setReversing] = useState(false)
    const createAttemptRef = useRef(null)
    const reversalAttemptRef = useRef(null)
    const reversalReasonRef = useRef(null)
    const reversalTriggerRef = useRef(null)

    const load = useCallback(async (signal) => {
        setLoading(true)
        setPageError('')
        try {
            const [destinationData, documentData] = await Promise.all([
                treasuryInitialCapitalizationApi.listDestinations({ signal }),
                treasuryInitialCapitalizationApi.list({ signal }),
            ])
            setDestinations(destinationData)
            setDocuments(documentData)
            if (documentData[0]?.status === 'Reversed') {
                setForm((current) => ({
                    ...current,
                    cutoverDate: documentData[0].cutoverDate.slice(0, 10),
                }))
            }
        } catch (error) {
            if (isRequestCanceled(error)) return
            const message = getCapitalizationErrorMessage(error, t, 'treasuryCapitalization.loadFailed')
            setPageError(message)
            showError(message)
        } finally {
            if (!signal?.aborted) setLoading(false)
        }
    }, [showError, t])

    useEffect(() => {
        const controller = new AbortController()
        void load(controller.signal)
        return () => controller.abort()
    }, [load])

    useEffect(() => {
        if (reversal) reversalReasonRef.current?.focus()
    }, [reversal])

    const activeDocument = documents.find((document) => document.status !== 'Reversed')
    const predecessor = !activeDocument && documents[0]?.status === 'Reversed' ? documents[0] : null
    const total = form.allocations.reduce((sum, allocation) => sum + (Number(allocation.amount) || 0), 0)

    const destinationName = (destination) => (
        isRTL && destination.nameAr ? destination.nameAr : destination.name
    )

    const journalHref = (document) => {
        const ledgerAccountId = document.allocations?.[0]?.ledgerAccountId
        return ledgerAccountId ? `/treasury/general-ledger?accountId=${ledgerAccountId}` : null
    }

    const updateAllocation = (index, field, value) => {
        setForm((current) => ({
            ...current,
            allocations: current.allocations.map((allocation, allocationIndex) => (
                allocationIndex === index ? { ...allocation, [field]: value } : allocation
            )),
        }))
    }

    const addAllocation = () => {
        setForm((current) => ({
            ...current,
            allocations: [...current.allocations, { treasuryAccountId: '', amount: '' }],
        }))
    }

    const removeAllocation = (index) => {
        setForm((current) => ({
            ...current,
            allocations: current.allocations.filter((_, allocationIndex) => allocationIndex !== index),
        }))
    }

    const submit = async (event) => {
        event.preventDefault()
        setFormError('')
        setPostedDocument(null)

        const allocations = form.allocations.map((allocation) => ({
            treasuryAccountId: allocation.treasuryAccountId,
            amount: Number(allocation.amount),
        }))
        const selectedIds = allocations.map((allocation) => allocation.treasuryAccountId)
        if (selectedIds.some((id) => !id) || new Set(selectedIds).size !== selectedIds.length ||
            allocations.some((allocation) => !Number.isFinite(allocation.amount) || allocation.amount <= 0)) {
            setFormError(t('treasuryCapitalization.errors.invalidAllocation'))
            return
        }

        const request = {
            cutoverDate: `${form.cutoverDate}T00:00:00.000Z`,
            allocations,
            evidenceReference: form.evidenceReference.trim(),
            notes: form.notes.trim() || null,
            declarationVersion: TREASURY_CAPITALIZATION_DECLARATION,
            declarationAcknowledged: form.declarationAcknowledged,
            predecessorCapitalizationId: predecessor?.id || null,
        }
        const fingerprint = JSON.stringify(request)
        const attempt = resolveCapitalizationAttempt(createAttemptRef.current, fingerprint)
        createAttemptRef.current = attempt

        setSaving(true)
        try {
            const result = await treasuryInitialCapitalizationApi.create({
                operationId: attempt.operationId,
                ...request,
            })
            setPostedDocument(result)
            showSuccess(t('treasuryCapitalization.posted'))
            await load()
        } catch (error) {
            const message = getCapitalizationErrorMessage(error, t)
            setFormError(message)
            showError(message)
        } finally {
            setSaving(false)
        }
    }

    const submitReversal = async (event) => {
        event.preventDefault()
        if (!reversal) return
        setFormError('')
        const request = { reason: reversal.reason.trim(), confirmed: reversal.confirmed }
        const fingerprint = JSON.stringify({ id: reversal.document.id, ...request })
        const attempt = resolveCapitalizationAttempt(reversalAttemptRef.current, fingerprint)
        reversalAttemptRef.current = attempt

        setReversing(true)
        try {
            await treasuryInitialCapitalizationApi.reverse(reversal.document.id, {
                operationId: attempt.operationId,
                ...request,
            })
            setReversal(null)
            showSuccess(t('treasuryCapitalization.reversed'))
            await load()
        } catch (error) {
            const message = getCapitalizationErrorMessage(error, t)
            setFormError(message)
            showError(message)
        } finally {
            setReversing(false)
        }
    }

    if (loading) {
        return (
            <div className="flex min-h-[24rem] items-center justify-center" aria-label={t('common.loading')}>
                <Loader2 className="h-8 w-8 animate-spin text-indigo-600" />
            </div>
        )
    }

    return (
        <main dir={dir} className="mx-auto max-w-7xl space-y-6 px-4 py-6 sm:px-6 lg:px-8">
            <header className="overflow-hidden rounded-3xl bg-slate-950 text-white shadow-xl">
                <div className="grid gap-6 px-6 py-8 md:grid-cols-[1fr_auto] md:items-end md:px-10">
                    <div>
                        <div className="mb-4 inline-flex items-center gap-2 rounded-full border border-cyan-300/30 bg-cyan-300/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-cyan-200">
                            <ShieldCheck className="h-4 w-4" />
                            {t('treasuryCapitalization.ownerAuthority')}
                        </div>
                        <h1 className="text-3xl font-black tracking-tight sm:text-4xl">{t('treasuryCapitalization.title')}</h1>
                        <p className="mt-3 max-w-3xl text-sm leading-6 text-slate-300 sm:text-base">{t('treasuryCapitalization.subtitle')}</p>
                    </div>
                    <div className="rounded-2xl border border-white/10 bg-white/5 px-5 py-4 text-sm">
                        <div className="text-slate-400">{t('treasuryCapitalization.accountingRule')}</div>
                        <div className="mt-1 font-bold text-white">Dr Treasury assets / Cr 3010</div>
                    </div>
                </div>
            </header>

            {pageError && <div role="alert" className="rounded-2xl border border-red-200 bg-red-50 p-4 text-red-800">{pageError}</div>}
            {formError && <div role="alert" className="rounded-2xl border border-red-200 bg-red-50 p-4 text-red-800">{formError}</div>}

            {postedDocument && (
                <section className="rounded-2xl border border-emerald-200 bg-emerald-50 p-5 text-emerald-900" aria-label={t('treasuryCapitalization.postingReceipt')}>
                    <div className="flex items-start gap-3">
                        <CheckCircle2 className="mt-0.5 h-6 w-6 shrink-0" />
                        <div>
                            <h2 className="font-bold">{t('treasuryCapitalization.posted')}</h2>
                            <p className="mt-1 text-sm">{postedDocument.documentNumber} · {t('treasuryCapitalization.journalId')}: {postedDocument.postedJournalId}</p>
                            <p className="mt-1 break-all text-xs">{t('treasuryCapitalization.traceId')}: {postedDocument.traceId}</p>
                            {journalHref(postedDocument) && <a href={journalHref(postedDocument)} className="mt-2 inline-block text-sm font-bold underline">{t('treasuryCapitalization.viewJournal')}</a>}
                        </div>
                    </div>
                </section>
            )}

            {activeDocument ? (
                <section className="rounded-2xl border border-emerald-200 bg-white p-6 shadow-sm">
                    <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
                        <div>
                            <p className="text-xs font-bold uppercase tracking-wider text-emerald-700">{t('treasuryCapitalization.activeAuthority')}</p>
                            <h2 className="mt-1 text-2xl font-bold text-slate-950">{activeDocument.documentNumber}</h2>
                            <p className="mt-2 text-sm text-slate-600">{t('treasuryCapitalization.activeBlocked')}</p>
                        </div>
                        <div className="text-start sm:text-end">
                            <div className="text-3xl font-black text-slate-950">{formatCurrency(activeDocument.totalAmount)}</div>
                            <span className={`mt-2 inline-block rounded-full px-3 py-1 text-xs font-bold ${statusClass(activeDocument.status)}`}>
                                {t(`treasuryCapitalization.status.${activeDocument.status.toLowerCase()}`)}
                            </span>
                        </div>
                    </div>
                    <div className="mt-5 grid gap-3 sm:grid-cols-2">
                        {activeDocument.allocations.map((allocation) => {
                            const destination = destinations.find((item) => item.treasuryAccountId === allocation.treasuryAccountId)
                            return (
                                <div key={allocation.id} className="rounded-xl bg-slate-50 px-4 py-3 text-sm">
                                    <div className="font-semibold text-slate-700">{allocation.ledgerAccountCode} · {allocation.treasuryAccountName}</div>
                                    <div className="mt-1 text-slate-500">{t('treasuryCapitalization.currentBalance')}: <strong className="text-slate-950">{formatCurrency(destination?.currentBalance ?? 0)}</strong></div>
                                </div>
                            )
                        })}
                    </div>
                </section>
            ) : (
                <form onSubmit={submit} className="grid gap-6 lg:grid-cols-[minmax(0,1.45fr)_minmax(20rem,0.75fr)]">
                    <section className="space-y-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm sm:p-7">
                        <div>
                            <p className="text-xs font-bold uppercase tracking-wider text-indigo-600">{predecessor ? t('treasuryCapitalization.replacement') : t('treasuryCapitalization.firstDeclaration')}</p>
                            <h2 className="mt-1 text-xl font-bold text-slate-950">{t('treasuryCapitalization.sourceEvidence')}</h2>
                        </div>
                        <div className="grid gap-4 sm:grid-cols-2">
                            <label className="text-sm font-semibold text-slate-700">
                                {t('treasuryCapitalization.cutoverDate')}
                                <input type="date" required max={today()} value={form.cutoverDate} disabled={Boolean(predecessor)} onChange={(event) => setForm({ ...form, cutoverDate: event.target.value })} className="mt-2 w-full rounded-xl border border-slate-300 px-3 py-2.5 font-normal outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100" />
                            </label>
                            <label className="text-sm font-semibold text-slate-700">
                                {t('treasuryCapitalization.evidenceReference')}
                                <input type="text" required maxLength={200} value={form.evidenceReference} onChange={(event) => setForm({ ...form, evidenceReference: event.target.value })} className="mt-2 w-full rounded-xl border border-slate-300 px-3 py-2.5 font-normal outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100" placeholder={t('treasuryCapitalization.evidencePlaceholder')} />
                            </label>
                        </div>
                        <label className="block text-sm font-semibold text-slate-700">
                            {t('treasuryCapitalization.notes')}
                            <textarea maxLength={1000} rows={3} value={form.notes} onChange={(event) => setForm({ ...form, notes: event.target.value })} className="mt-2 w-full resize-y rounded-xl border border-slate-300 px-3 py-2.5 font-normal outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100" />
                        </label>

                        <div className="border-t border-slate-200 pt-6">
                            <div className="flex items-center justify-between gap-3">
                                <div>
                                    <h2 className="text-xl font-bold text-slate-950">{t('treasuryCapitalization.allocations')}</h2>
                                    <p className="mt-1 text-sm text-slate-500">{t('treasuryCapitalization.allocationsHelp')}</p>
                                </div>
                                <button type="button" onClick={addAllocation} disabled={form.allocations.length >= destinations.length} className="inline-flex items-center gap-2 rounded-xl border border-indigo-200 px-3 py-2 text-sm font-bold text-indigo-700 hover:bg-indigo-50 disabled:cursor-not-allowed disabled:opacity-40">
                                    <Plus className="h-4 w-4" /> {t('treasuryCapitalization.addAllocation')}
                                </button>
                            </div>
                            {destinations.length === 0 ? (
                                <p className="mt-5 rounded-xl bg-amber-50 p-4 text-sm text-amber-900">{t('treasuryCapitalization.noDestinations')}</p>
                            ) : (
                                <div className="mt-5 space-y-3">
                                    {form.allocations.map((allocation, index) => (
                                        <div key={index} className="grid gap-3 rounded-2xl bg-slate-50 p-4 sm:grid-cols-[1fr_11rem_auto] sm:items-end">
                                            <label className="text-sm font-semibold text-slate-700">
                                                {t('treasuryCapitalization.destination')}
                                                <select required value={allocation.treasuryAccountId} onChange={(event) => updateAllocation(index, 'treasuryAccountId', event.target.value)} className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2.5 font-normal">
                                                    <option value="">{t('treasuryCapitalization.selectDestination')}</option>
                                                    {destinations.map((destination) => (
                                                        <option key={destination.treasuryAccountId} value={destination.treasuryAccountId} disabled={form.allocations.some((item, itemIndex) => itemIndex !== index && item.treasuryAccountId === destination.treasuryAccountId)}>
                                                            {destinationName(destination)} · {destination.ledgerAccountCode}
                                                        </option>
                                                    ))}
                                                </select>
                                            </label>
                                            <label className="text-sm font-semibold text-slate-700">
                                                {t('treasuryCapitalization.amount')}
                                                <input type="number" required min="0.01" step="0.01" inputMode="decimal" value={allocation.amount} onChange={(event) => updateAllocation(index, 'amount', event.target.value)} className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2.5 font-normal" />
                                            </label>
                                            <button type="button" aria-label={t('treasuryCapitalization.removeAllocation')} onClick={() => removeAllocation(index)} disabled={form.allocations.length === 1} className="rounded-xl p-3 text-red-600 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-30">
                                                <Trash2 className="h-5 w-5" />
                                            </button>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    </section>

                    <aside className="self-start rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:sticky lg:top-6">
                        <div className="flex items-center gap-3">
                            <div className="rounded-xl bg-indigo-100 p-2.5 text-indigo-700"><Landmark className="h-5 w-5" /></div>
                            <div>
                                <p className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('treasuryCapitalization.confirmationSummary')}</p>
                                <h2 className="font-bold text-slate-950">{formatCurrency(total)}</h2>
                            </div>
                        </div>
                        <div className="mt-5 space-y-3 text-sm">
                            {form.allocations.map((allocation, index) => {
                                const destination = destinations.find((item) => item.treasuryAccountId === allocation.treasuryAccountId)
                                return (
                                    <div key={index} className="flex justify-between gap-4 border-b border-slate-100 pb-3">
                                        <span className="text-slate-600">Dr {destination ? `${destination.ledgerAccountCode} ${destinationName(destination)}` : t('treasuryCapitalization.destination')}</span>
                                        <strong className="whitespace-nowrap text-slate-950">{formatCurrency(Number(allocation.amount) || 0)}</strong>
                                    </div>
                                )
                            })}
                            <div className="flex justify-between gap-4 rounded-xl bg-slate-950 px-4 py-3 text-white">
                                <span>Cr 3010 · {t('treasuryCapitalization.openingEquity')}</span>
                                <strong className="whitespace-nowrap">{formatCurrency(total)}</strong>
                            </div>
                        </div>
                        <div className="mt-6 space-y-4">
                            <label className="flex cursor-pointer items-start gap-3 text-sm leading-6 text-slate-700">
                                <input type="checkbox" checked={form.declarationAcknowledged} onChange={(event) => setForm({ ...form, declarationAcknowledged: event.target.checked })} className="mt-1 h-4 w-4 rounded border-slate-300 text-indigo-600" />
                                <span>{t('treasuryCapitalization.declaration')}</span>
                            </label>
                            <label className="flex cursor-pointer items-start gap-3 text-sm leading-6 text-slate-700">
                                <input type="checkbox" checked={form.confirmationAcknowledged} onChange={(event) => setForm({ ...form, confirmationAcknowledged: event.target.checked })} className="mt-1 h-4 w-4 rounded border-slate-300 text-indigo-600" />
                                <span>{t('treasuryCapitalization.confirmation')}</span>
                            </label>
                        </div>
                        <button type="submit" disabled={saving || destinations.length === 0 || !form.declarationAcknowledged || !form.confirmationAcknowledged} className="mt-6 inline-flex w-full items-center justify-center gap-2 rounded-xl bg-indigo-600 px-4 py-3 font-bold text-white shadow-lg shadow-indigo-200 hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-slate-300 disabled:shadow-none">
                            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
                            {saving ? t('treasuryCapitalization.posting') : t('treasuryCapitalization.post')}
                        </button>
                        <p className="mt-3 text-center text-xs text-slate-500">{t('treasuryCapitalization.reversalOnly')}</p>
                    </aside>
                </form>
            )}

            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm sm:p-7">
                <div>
                    <p className="text-xs font-bold uppercase tracking-wider text-indigo-600">{t('treasuryCapitalization.auditTrail')}</p>
                    <h2 className="mt-1 text-xl font-bold text-slate-950">{t('treasuryCapitalization.history')}</h2>
                </div>
                {documents.length === 0 ? (
                    <p className="mt-5 rounded-xl bg-slate-50 p-5 text-sm text-slate-600">{t('treasuryCapitalization.noHistory')}</p>
                ) : (
                    <div className="mt-5 space-y-4">
                        {documents.map((document) => (
                            <article key={document.id} className="rounded-2xl border border-slate-200 p-5">
                                <div className="flex flex-col justify-between gap-4 md:flex-row md:items-start">
                                    <div>
                                        <div className="flex flex-wrap items-center gap-2">
                                            <h3 className="font-bold text-slate-950">{document.documentNumber}</h3>
                                            <span className={`rounded-full px-2.5 py-1 text-xs font-bold ${statusClass(document.status)}`}>{t(`treasuryCapitalization.status.${document.status.toLowerCase()}`)}</span>
                                        </div>
                                         <p className="mt-2 text-sm text-slate-600">{formatDate(document.cutoverDate, locale)} · {document.evidenceReference}</p>
                                         <p className="mt-1 break-all text-xs text-slate-500">{t('treasuryCapitalization.journalId')}: {document.postedJournalId || '-'}</p>
                                         {journalHref(document) && <a href={journalHref(document)} className="mt-1 inline-block text-xs font-bold text-indigo-700 underline">{t('treasuryCapitalization.viewJournal')}</a>}
                                         {document.reversalJournalId && <p className="mt-1 break-all text-xs text-slate-500">{t('treasuryCapitalization.reversalJournalId')}: {document.reversalJournalId}</p>}
                                    </div>
                                    <div className={isRTL ? 'md:text-left' : 'md:text-right'}>
                                        <div className="text-xl font-black text-slate-950">{formatCurrency(document.totalAmount)}</div>
                                        <div className="mt-1 text-xs text-slate-500">{formatDateTime(document.createdAtUtc, locale)}</div>
                                         {document.status === 'Posted' && (
                                             <button type="button" aria-haspopup="dialog" onClick={(event) => { reversalTriggerRef.current = event.currentTarget; reversalAttemptRef.current = null; setReversal({ document, reason: '', confirmed: false }) }} className="mt-3 inline-flex items-center gap-2 rounded-lg border border-red-200 px-3 py-2 text-sm font-bold text-red-700 hover:bg-red-50">
                                                <RotateCcw className="h-4 w-4" /> {t('treasuryCapitalization.reverse')}
                                            </button>
                                        )}
                                    </div>
                                </div>
                                <div className="mt-4 grid gap-2 sm:grid-cols-2">
                                    {document.allocations.map((allocation) => (
                                        <div key={allocation.id} className="flex justify-between gap-3 rounded-xl bg-slate-50 px-4 py-3 text-sm">
                                            <span className="text-slate-600">Dr {allocation.ledgerAccountCode} · {allocation.treasuryAccountName}</span>
                                            <strong>{formatCurrency(allocation.amount)}</strong>
                                        </div>
                                    ))}
                                    <div className="flex justify-between gap-3 rounded-xl bg-slate-950 px-4 py-3 text-sm text-white">
                                        <span>Cr {document.openingEquity.accountCode} · {document.openingEquity.accountName}</span>
                                        <strong>{formatCurrency(document.totalAmount)}</strong>
                                    </div>
                                </div>
                            </article>
                        ))}
                    </div>
                )}
            </section>

             {reversal && (
                 <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4" role="presentation">
                     <form onSubmit={submitReversal} onKeyDown={(event) => { if (event.key === 'Escape' && !reversing) { setReversal(null); requestAnimationFrame(() => reversalTriggerRef.current?.focus()) } }} role="dialog" aria-modal="true" aria-labelledby="capitalization-reversal-title" aria-describedby="capitalization-reversal-warning" className="w-full max-w-xl rounded-2xl bg-white p-6 shadow-2xl">
                         <h2 id="capitalization-reversal-title" className="text-xl font-bold text-slate-950">{t('treasuryCapitalization.reverseTitle')}</h2>
                         <p id="capitalization-reversal-warning" className="mt-2 text-sm leading-6 text-slate-600">{t('treasuryCapitalization.reverseWarning', { documentNumber: reversal.document.documentNumber })}</p>
                        <label className="mt-5 block text-sm font-semibold text-slate-700">
                            {t('treasuryCapitalization.reversalReason')}
                             <textarea ref={reversalReasonRef} required maxLength={500} rows={4} value={reversal.reason} onChange={(event) => setReversal({ ...reversal, reason: event.target.value })} className="mt-2 w-full rounded-xl border border-slate-300 px-3 py-2.5 font-normal" />
                        </label>
                        <label className="mt-4 flex cursor-pointer items-start gap-3 text-sm leading-6 text-slate-700">
                            <input type="checkbox" checked={reversal.confirmed} onChange={(event) => setReversal({ ...reversal, confirmed: event.target.checked })} className="mt-1 h-4 w-4 rounded border-slate-300 text-red-600" />
                            <span>{t('treasuryCapitalization.reversalConfirmation')}</span>
                        </label>
                        <div className={`mt-6 flex gap-3 ${isRTL ? 'justify-start' : 'justify-end'}`}>
                             <button type="button" onClick={() => { setReversal(null); requestAnimationFrame(() => reversalTriggerRef.current?.focus()) }} className="rounded-xl border border-slate-300 px-4 py-2.5 font-semibold text-slate-700">{t('common.cancel')}</button>
                            <button type="submit" disabled={reversing || !reversal.reason.trim() || !reversal.confirmed} className="inline-flex items-center gap-2 rounded-xl bg-red-600 px-4 py-2.5 font-bold text-white disabled:cursor-not-allowed disabled:bg-slate-300">
                                {reversing && <Loader2 className="h-4 w-4 animate-spin" />}
                                {t('treasuryCapitalization.confirmReversal')}
                            </button>
                        </div>
                    </form>
                </div>
            )}
        </main>
    )
}

import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useToast } from '../components/Toast'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'
import { useAuthStore } from '../store/authStore'
import { formatDate, formatDateInputValue } from '../utils/dateFormat'
import {
    canAdministerSupplierCards,
    formatSupplierCardLabel,
    getSupplierCardErrorMessage,
    resolveSupplierCardAttempt,
    supplierCardFundingApi,
} from '../services/supplierCardFundingApi'

const today = () => formatDateInputValue(new Date())

const oneMonthAgo = () => {
    const value = new Date()
    value.setMonth(value.getMonth() - 1)
    return formatDateInputValue(value)
}

const emptyCreateForm = {
    displayName: '',
    instrumentType: 'FundedCompanyCard',
    issuer: '',
    last4: '',
    ledgerAccountCode: '',
    ledgerAccountName: '',
    fundingSourceTreasuryAccountId: '',
    isActive: true,
}

const emptyFundingForm = {
    instrumentId: '',
    fundingSourceTreasuryAccountId: '',
    amount: '',
    fundingDate: today(),
    referenceNumber: '',
    notes: '',
}

const statementCategory = (transactionType) => {
    if (transactionType === 'SupplierCardFunding') return 'funding'
    if (transactionType === 'PurchaseInvoice') return 'supplierPurchases'
    if (transactionType === 'SupplierPayment') return 'supplierPayments'
    if (transactionType === 'PurchaseReturn') return 'returns'
    if (transactionType === 'Reversal') return 'reversals'
    return 'other'
}

function Dialog({ title, children, onClose, closeLabel, wide = false }) {
    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/55 p-4" role="presentation">
            <section
                aria-label={title}
                className={`max-h-[calc(100vh-2rem)] w-full overflow-y-auto rounded-2xl bg-white shadow-2xl ${wide ? 'max-w-6xl' : 'max-w-2xl'}`}
                role="dialog"
                aria-modal="true"
            >
                <header className="sticky top-0 z-10 flex items-center justify-between gap-4 border-b border-slate-200 bg-white px-5 py-4">
                    <h2 className="text-xl font-bold text-slate-900">{title}</h2>
                    <button type="button" onClick={onClose} className="rounded-lg px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-100">
                        {closeLabel}
                    </button>
                </header>
                {children}
            </section>
        </div>
    )
}

export default function SupplierCardFundingInstruments() {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const userRole = useAuthStore(state => state.user?.role)
    const canAdminister = canAdministerSupplierCards(userRole)
    const [instruments, setInstruments] = useState([])
    const [sources, setSources] = useState([])
    const [loading, setLoading] = useState(true)
    const [pageError, setPageError] = useState('')
    const [editor, setEditor] = useState(null)
    const [editorError, setEditorError] = useState('')
    const [saving, setSaving] = useState(false)
    const [funding, setFunding] = useState(null)
    const [fundingError, setFundingError] = useState('')
    const [statementView, setStatementView] = useState(null)
    const [statementLoading, setStatementLoading] = useState(false)
    const [statementError, setStatementError] = useState('')
    const fundingAttemptRef = useRef(null)

    const load = useCallback(async () => {
        setLoading(true)
        setPageError('')
        try {
            const [instrumentData, sourceData] = await Promise.all([
                supplierCardFundingApi.list(),
                supplierCardFundingApi.listFundingSources(),
            ])
            setInstruments(instrumentData)
            setSources(sourceData)
        } catch (error) {
            const message = getSupplierCardErrorMessage(error, t, 'supplierCards.loadFailed')
            setPageError(message)
            showError(message)
        } finally {
            setLoading(false)
        }
    }, [showError, t])

    useEffect(() => { void load() }, [load])

    const openCreate = () => {
        setEditorError('')
        setEditor({ mode: 'create', form: { ...emptyCreateForm } })
    }

    const openEdit = (instrument) => {
        setEditorError('')
        setEditor({
            mode: 'edit',
            instrument,
            form: {
                displayName: instrument.displayName,
                instrumentType: instrument.instrumentType,
                issuer: instrument.issuer || '',
                last4: instrument.last4 || '',
                isActive: instrument.isActive,
            },
        })
    }

    const saveInstrument = async (event) => {
        event.preventDefault()
        setEditorError('')
        setSaving(true)
        try {
            if (editor.mode === 'create') {
                const { instrumentType: _instrumentType, ...request } = editor.form
                await supplierCardFundingApi.create(request)
                showSuccess(t('supplierCards.created'))
            } else {
                await supplierCardFundingApi.update(editor.instrument.id, {
                    displayName: editor.form.displayName,
                    issuer: editor.form.issuer,
                    last4: editor.form.last4,
                    isActive: editor.form.isActive,
                    expectedAuthorityVersion: editor.instrument.authorityVersion,
                })
                showSuccess(t('supplierCards.updated'))
            }
            setEditor(null)
            await load()
        } catch (error) {
            const message = getSupplierCardErrorMessage(error, t, 'supplierCards.saveFailed')
            setEditorError(message)
            showError(message)
        } finally {
            setSaving(false)
        }
    }

    const toggleActive = async (instrument) => {
        setSaving(true)
        try {
            await supplierCardFundingApi.update(instrument.id, {
                displayName: instrument.displayName,
                issuer: instrument.issuer,
                last4: instrument.last4,
                isActive: !instrument.isActive,
                expectedAuthorityVersion: instrument.authorityVersion,
            })
            showSuccess(t(instrument.isActive ? 'supplierCards.deactivated' : 'supplierCards.activated'))
            await load()
        } catch (error) {
            showError(getSupplierCardErrorMessage(error, t, 'supplierCards.saveFailed'))
        } finally {
            setSaving(false)
        }
    }

    const openFunding = (instrument = null) => {
        fundingAttemptRef.current = null
        setFundingError('')
        setFunding({
            ...emptyFundingForm,
            fundingDate: today(),
            instrumentId: instrument?.id || '',
        })
    }

    const selectedFundingInstrument = instruments.find(item => item.id === funding?.instrumentId)
    const allowedFundingSources = selectedFundingInstrument
        ? sources.filter(source => source.id === selectedFundingInstrument.fundingSourceTreasuryAccountId)
        : []

    const submitFunding = async (event) => {
        event.preventDefault()
        setFundingError('')
        const instrument = instruments.find(item => item.id === funding.instrumentId)
        const source = sources.find(item => item.id === funding.fundingSourceTreasuryAccountId)
        const amount = Number(funding.amount)
        if (!instrument?.isActive) {
            setFundingError(t('supplierCards.errors.instrumentUnavailable'))
            return
        }
        if (!source || source.id !== instrument.fundingSourceTreasuryAccountId) {
            setFundingError(t('supplierCards.errors.fundingSourceUnavailable'))
            return
        }
        if (!Number.isFinite(amount) || amount <= 0) {
            setFundingError(t('supplierCards.errors.amountPositive'))
            return
        }

        const request = {
            fundingInstrumentVersion: instrument.authorityVersion,
            fundingSourceTreasuryAccountId: source.id,
            amount,
            fundingDate: new Date(`${funding.fundingDate}T00:00:00.000Z`).toISOString(),
            referenceNumber: funding.referenceNumber.trim() || null,
            notes: funding.notes.trim() || null,
        }
        const fingerprint = JSON.stringify({ instrumentId: instrument.id, ...request })
        const attempt = resolveSupplierCardAttempt(
            fundingAttemptRef.current,
            fingerprint,
            'supplier-card-funding',
        )
        fundingAttemptRef.current = attempt
        setSaving(true)
        try {
            await supplierCardFundingApi.fund(instrument.id, request, attempt.idempotencyKey)
            fundingAttemptRef.current = null
            setFunding(null)
            showSuccess(t('supplierCards.funded'))
            await load()
        } catch (error) {
            const message = getSupplierCardErrorMessage(error, t, 'supplierCards.fundingFailed')
            setFundingError(message)
            showError(message)
        } finally {
            setSaving(false)
        }
    }

    const loadStatement = async (instrument, from = oneMonthAgo(), to = today()) => {
        setStatementLoading(true)
        setStatementError('')
        setStatementView(current => ({ instrument, from, to, data: current?.instrument.id === instrument.id ? current.data : null }))
        try {
            const data = await supplierCardFundingApi.statement(instrument.id, { from, to })
            setStatementView({ instrument, from, to, data })
        } catch (error) {
            const message = getSupplierCardErrorMessage(error, t, 'supplierCards.statementFailed')
            setStatementError(message)
            showError(message)
        } finally {
            setStatementLoading(false)
        }
    }

    const statementTotals = (statementView?.data?.transactions || []).reduce((totals, row) => {
        const category = statementCategory(row.transactionType)
        totals[category] = (totals[category] || 0) + Math.abs(Number(row.netMovement) || 0)
        return totals
    }, {})

    return (
        <div className="mx-auto w-full max-w-[1500px] space-y-6" dir={dir}>
            <header className="flex flex-wrap items-start justify-between gap-4">
                <div>
                    <h1 className="text-2xl font-bold text-slate-900">{t('supplierCards.title')}</h1>
                    <p className="mt-1 max-w-3xl text-sm text-slate-600">{t('supplierCards.subtitle')}</p>
                </div>
                <div className="flex w-full flex-wrap gap-2 sm:w-auto">
                    <button type="button" onClick={() => openFunding()} className="btn-secondary" disabled={loading || instruments.every(item => !item.isActive)}>
                        {t('supplierCards.fundCard')}
                    </button>
                    {canAdminister && (
                        <button type="button" onClick={openCreate} className="btn-primary" disabled={loading}>
                            {t('supplierCards.createCard')}
                        </button>
                    )}
                </div>
            </header>

            <div className="rounded-xl border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-900">
                <strong>{t('supplierCards.authorityTitle')}</strong> {t('supplierCards.authorityExplanation')}
                <span className="mt-2 block rounded-lg bg-amber-100 px-3 py-2 font-semibold text-amber-900">
                    {t('treasury.supplierCardRestricted', 'Restricted card funding for supplier purchases and payments only. Not available cash, not customer-card clearing, and not a Treasury transfer destination.')}
                </span>
            </div>

            {pageError && (
                <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800" role="alert">
                    <span>{pageError}</span>
                    <button type="button" onClick={() => void load()} className="font-semibold underline">{t('common.retry')}</button>
                </div>
            )}

            <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
                <div className="overflow-x-auto">
                    <table className="min-w-[1180px] w-full divide-y divide-slate-200">
                        <thead className="bg-slate-50">
                            <tr>
                                {['instrument', 'type', 'maskedIdentifier', 'issuer', 'account', 'fundingSource', 'status', 'balance', 'actions'].map(key => (
                                    <th key={key} scope="col" className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-slate-600">{t(`supplierCards.${key}`)}</th>
                                ))}
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100">
                            {loading && <tr><td colSpan="9" className="px-4 py-12 text-center text-slate-500">{t('common.loading')}</td></tr>}
                            {!loading && instruments.length === 0 && <tr><td colSpan="9" className="px-4 py-12 text-center text-slate-500">{t('supplierCards.noInstruments')}</td></tr>}
                            {!loading && instruments.map(instrument => (
                                <tr key={instrument.id} className="align-top hover:bg-slate-50/70">
                                    <td className="max-w-56 px-4 py-4 font-semibold text-slate-900"><span className="break-words">{instrument.displayName}</span></td>
                                    <td className="px-4 py-4 text-sm text-slate-700">{t('supplierCards.fundedCompanyCard')}</td>
                                    <td className="px-4 py-4 font-mono text-sm text-slate-700">{instrument.last4 ? `**** ${instrument.last4}` : t('common.none')}</td>
                                    <td className="max-w-44 px-4 py-4 text-sm text-slate-700"><span className="break-words">{instrument.issuer || t('common.none')}</span></td>
                                    <td className="max-w-64 px-4 py-4 text-sm text-slate-700">
                                        <bdi className="block font-mono">{instrument.ledgerAccountCode}</bdi>
                                        <span className="block break-words text-slate-500">{instrument.ledgerAccountName}</span>
                                    </td>
                                    <td className="max-w-56 px-4 py-4 text-sm text-slate-700">
                                        <span className="block break-words">{instrument.fundingSourceName}</span>
                                        <span className="text-xs text-slate-500">{t(`supplierCards.sourceTypes.${instrument.fundingSourceType}`, instrument.fundingSourceType)}</span>
                                    </td>
                                    <td className="px-4 py-4">
                                        <span className={`inline-flex rounded-full px-2.5 py-1 text-xs font-semibold ${instrument.isActive ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-200 text-slate-700'}`}>
                                            {instrument.isActive ? t('common.active') : t('common.inactive')}
                                        </span>
                                    </td>
                                    <td className="whitespace-nowrap px-4 py-4 font-bold text-slate-900">{formatCurrency(instrument.availableBalance)}</td>
                                    <td className="px-4 py-4">
                                        <div className="flex flex-wrap gap-x-3 gap-y-2 text-sm font-semibold">
                                            {canAdminister && <button type="button" className="text-blue-700 hover:underline" onClick={() => openEdit(instrument)}>{t('common.edit')}</button>}
                                            {canAdminister && <button type="button" disabled={saving} className={instrument.isActive ? 'text-red-700 hover:underline' : 'text-emerald-700 hover:underline'} onClick={() => void toggleActive(instrument)}>{instrument.isActive ? t('supplierCards.deactivateCard') : t('common.activate')}</button>}
                                            <button type="button" disabled={!instrument.isActive} className="text-blue-700 hover:underline disabled:text-slate-400" onClick={() => openFunding(instrument)}>{t('supplierCards.fund')}</button>
                                            <button type="button" className="text-blue-700 hover:underline" onClick={() => void loadStatement(instrument)}>{t('supplierCards.statement')}</button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </section>

            {editor && (
                <Dialog title={t(editor.mode === 'create' ? 'supplierCards.createCard' : 'supplierCards.editCard')} onClose={() => !saving && setEditor(null)} closeLabel={t('common.close')}>
                    <form onSubmit={saveInstrument} className="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2">
                        <label className="sm:col-span-2">
                            <span className="label">{t('supplierCards.displayName')}</span>
                            <input className="input w-full" required maxLength="120" value={editor.form.displayName} onChange={event => setEditor({ ...editor, form: { ...editor.form, displayName: event.target.value } })} />
                        </label>
                        <label>
                            <span className="label">{t('supplierCards.type')}</span>
                            <select className="input w-full bg-slate-100" value={editor.form.instrumentType} disabled><option value="FundedCompanyCard">{t('supplierCards.fundedCompanyCard')}</option></select>
                        </label>
                        <label>
                            <span className="label">{t('supplierCards.issuer')}</span>
                            <input className="input w-full" maxLength="100" value={editor.form.issuer} onChange={event => setEditor({ ...editor, form: { ...editor.form, issuer: event.target.value } })} placeholder={t('supplierCards.issuerPlaceholder')} />
                        </label>
                        <label>
                            <span className="label">{t('supplierCards.last4')}</span>
                            <input className="input w-full" inputMode="numeric" pattern="[0-9]{4}" maxLength="4" value={editor.form.last4} onChange={event => setEditor({ ...editor, form: { ...editor.form, last4: event.target.value.replace(/\D/g, '') } })} placeholder="4521" />
                        </label>
                        {editor.mode === 'create' ? (
                            <>
                                <label>
                                    <span className="label">{t('supplierCards.accountCode')}</span>
                                    <input className="input w-full font-mono" required maxLength="20" value={editor.form.ledgerAccountCode} onChange={event => setEditor({ ...editor, form: { ...editor.form, ledgerAccountCode: event.target.value } })} />
                                </label>
                                <label>
                                    <span className="label">{t('supplierCards.accountName')}</span>
                                    <input className="input w-full" required maxLength="200" value={editor.form.ledgerAccountName} onChange={event => setEditor({ ...editor, form: { ...editor.form, ledgerAccountName: event.target.value } })} />
                                </label>
                                <label>
                                    <span className="label">{t('supplierCards.fundingSource')}</span>
                                    <select className="input w-full" required value={editor.form.fundingSourceTreasuryAccountId} onChange={event => setEditor({ ...editor, form: { ...editor.form, fundingSourceTreasuryAccountId: event.target.value } })}>
                                        <option value="">{t('supplierCards.selectFundingSource')}</option>
                                        {sources.map(source => <option key={source.id} value={source.id}>{source.name} ({t(`supplierCards.sourceTypes.${source.type}`, source.type)})</option>)}
                                    </select>
                                </label>
                            </>
                        ) : (
                            <div className="sm:col-span-2 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm text-slate-600">
                                {t('supplierCards.immutableAuthority')}
                                <div className="mt-2 font-mono text-xs">{editor.instrument.ledgerAccountCode} · {editor.instrument.fundingSourceName}</div>
                            </div>
                        )}
                        <label className="flex items-center gap-2 self-end py-2">
                            <input type="checkbox" checked={editor.form.isActive} onChange={event => setEditor({ ...editor, form: { ...editor.form, isActive: event.target.checked } })} />
                            <span className="text-sm font-medium text-slate-700">{t('supplierCards.activeState')}</span>
                        </label>
                        {sources.length === 0 && editor.mode === 'create' && <p className="sm:col-span-2 text-sm text-red-700" role="alert">{t('supplierCards.noFundingSource')}</p>}
                        {editorError && <p className="sm:col-span-2 text-sm text-red-700" role="alert">{editorError}</p>}
                        <div className={`flex gap-3 border-t border-slate-200 pt-4 sm:col-span-2 ${isRTL ? 'justify-start' : 'justify-end'}`}>
                            <button type="button" className="btn-secondary" disabled={saving} onClick={() => setEditor(null)}>{t('common.cancel')}</button>
                            <button type="submit" className="btn-primary" disabled={saving || (editor.mode === 'create' && sources.length === 0)}>{saving ? t('common.processing') : t('common.save')}</button>
                        </div>
                    </form>
                </Dialog>
            )}

            {funding && (
                <Dialog title={t('supplierCards.fundCard')} onClose={() => !saving && setFunding(null)} closeLabel={t('common.close')}>
                    <form onSubmit={submitFunding} className="space-y-4 p-5">
                        <label className="block">
                            <span className="label">{t('supplierCards.fundingInstrument')}</span>
                            <select className="input w-full" required value={funding.instrumentId} onChange={event => setFunding({ ...funding, instrumentId: event.target.value, fundingSourceTreasuryAccountId: '' })} disabled={saving}>
                                <option value="">{t('supplierCards.selectInstrument')}</option>
                                {instruments.filter(item => item.isActive).map(instrument => <option key={instrument.id} value={instrument.id}>{formatSupplierCardLabel(instrument)} · {formatCurrency(instrument.availableBalance)}</option>)}
                            </select>
                        </label>
                        <label className="block">
                            <span className="label">{t('supplierCards.fundingSource')}</span>
                            <select className="input w-full" required value={funding.fundingSourceTreasuryAccountId} onChange={event => setFunding({ ...funding, fundingSourceTreasuryAccountId: event.target.value })} disabled={saving || !selectedFundingInstrument || allowedFundingSources.length === 0}>
                                <option value="">{t('supplierCards.selectFundingSource')}</option>
                                {allowedFundingSources.map(source => <option key={source.id} value={source.id}>{source.name} ({t(`supplierCards.sourceTypes.${source.type}`, source.type)})</option>)}
                            </select>
                        </label>
                        {selectedFundingInstrument && allowedFundingSources.length === 0 && <p className="text-sm text-red-700" role="alert">{t('supplierCards.errors.fundingSourceUnavailable')}</p>}
                        <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-center font-semibold text-emerald-900">
                            {t('supplierCards.fundingFlow')}
                        </div>
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                            <label>
                                <span className="label">{t('supplierCards.fundingAmount')}</span>
                                <input className="input w-full" type="number" min="0.01" step="0.01" required value={funding.amount} onChange={event => setFunding({ ...funding, amount: event.target.value })} disabled={saving} />
                            </label>
                            <label>
                                <span className="label">{t('supplierCards.fundingDate')}</span>
                                <input className="input w-full" type="date" required value={funding.fundingDate} onChange={event => setFunding({ ...funding, fundingDate: event.target.value })} disabled={saving} />
                            </label>
                        </div>
                        <label className="block">
                            <span className="label">{t('supplierCards.referenceNumber')}</span>
                            <input className="input w-full" maxLength="100" value={funding.referenceNumber} onChange={event => setFunding({ ...funding, referenceNumber: event.target.value })} disabled={saving} />
                        </label>
                        <label className="block">
                            <span className="label">{t('supplierCards.notes')}</span>
                            <textarea className="input w-full" rows="3" maxLength="1000" value={funding.notes} onChange={event => setFunding({ ...funding, notes: event.target.value })} disabled={saving} />
                        </label>
                        {fundingError && <p className="text-sm text-red-700" role="alert">{fundingError}</p>}
                        <div className={`flex gap-3 border-t border-slate-200 pt-4 ${isRTL ? 'justify-start' : 'justify-end'}`}>
                            <button type="button" className="btn-secondary" disabled={saving} onClick={() => setFunding(null)}>{t('common.cancel')}</button>
                            <button type="submit" className="btn-primary" disabled={saving || !selectedFundingInstrument || allowedFundingSources.length === 0}>{saving ? t('common.processing') : t('supplierCards.confirmFunding')}</button>
                        </div>
                    </form>
                </Dialog>
            )}

            {statementView && (
                <Dialog title={`${t('supplierCards.cardStatement')} · ${formatSupplierCardLabel(statementView.instrument)}`} onClose={() => setStatementView(null)} closeLabel={t('common.close')} wide>
                    <div className="space-y-5 p-5">
                        <form className="flex flex-wrap items-end gap-3" onSubmit={event => { event.preventDefault(); void loadStatement(statementView.instrument, statementView.from, statementView.to) }}>
                            <label><span className="label">{t('supplierCards.from')}</span><input className="input" type="date" value={statementView.from} onChange={event => setStatementView({ ...statementView, from: event.target.value })} /></label>
                            <label><span className="label">{t('supplierCards.to')}</span><input className="input" type="date" value={statementView.to} onChange={event => setStatementView({ ...statementView, to: event.target.value })} /></label>
                            <button type="submit" className="btn-secondary" disabled={statementLoading}>{statementLoading ? t('common.loading') : t('supplierCards.loadStatement')}</button>
                        </form>
                        {statementError && <p className="text-sm text-red-700" role="alert">{statementError}</p>}
                        {statementView.data && (
                            <>
                                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-4 xl:grid-cols-7">
                                    {[
                                        ['openingBalance', statementView.data.openingBalance],
                                        ['funding', statementTotals.funding || 0],
                                        ['supplierPurchases', statementTotals.supplierPurchases || 0],
                                        ['supplierPayments', statementTotals.supplierPayments || 0],
                                        ['returns', statementTotals.returns || 0],
                                        ['reversals', statementTotals.reversals || 0],
                                        ['closingBalance', statementView.data.closingBalance],
                                    ].map(([key, value]) => <div key={key} className="min-w-0 rounded-xl border border-slate-200 bg-slate-50 p-3"><div className="text-xs font-medium text-slate-500">{t(`supplierCards.${key}`)}</div><div className="mt-1 break-words font-bold text-slate-900">{formatCurrency(value)}</div></div>)}
                                </div>
                                <div className="overflow-x-auto rounded-xl border border-slate-200">
                                    <table className="min-w-[1080px] w-full divide-y divide-slate-200">
                                        <thead className="bg-slate-50"><tr>{['date', 'transactionType', 'reference', 'sourceDocument', 'journal', 'description', 'debit', 'credit', 'runningBalance'].map(key => <th key={key} scope="col" className="px-3 py-3 text-start text-xs font-semibold uppercase text-slate-600">{t(`supplierCards.${key}`)}</th>)}</tr></thead>
                                        <tbody className="divide-y divide-slate-100">
                                            {statementView.data.transactions.length === 0 && <tr><td colSpan="9" className="px-4 py-10 text-center text-slate-500">{t('supplierCards.noStatementTransactions')}</td></tr>}
                                            {statementView.data.transactions.map((row, index) => (
                                                <tr key={`${row.journalId}-${index}`}>
                                                    <td className="whitespace-nowrap px-3 py-3 text-sm">{formatDate(row.date, locale)}</td>
                                                    <td className="whitespace-nowrap px-3 py-3 text-sm">{t(`supplierCards.transactionTypes.${statementCategory(row.transactionType)}`, row.transactionType)}</td>
                                                    <td className="max-w-48 px-3 py-3 text-sm"><span className="break-words">{row.sourceReference || t('common.none')}</span></td>
                                                    <td className="max-w-48 break-all px-3 py-3 font-mono text-xs"><bdi>{row.sourceDocumentId}</bdi></td>
                                                    <td className="max-w-48 break-all px-3 py-3 font-mono text-xs"><bdi>{row.journalId}</bdi></td>
                                                    <td className="max-w-64 px-3 py-3 text-sm"><span className="break-words">{row.description}</span></td>
                                                    <td className="whitespace-nowrap px-3 py-3 text-sm">{formatCurrency(row.debit)}</td>
                                                    <td className="whitespace-nowrap px-3 py-3 text-sm">{formatCurrency(row.credit)}</td>
                                                    <td className="whitespace-nowrap px-3 py-3 font-bold">{formatCurrency(row.runningBalance)}</td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            </>
                        )}
                    </div>
                </Dialog>
            )}
        </div>
    )
}

/**
 * AF-38: Payouts / Withdrawals Tab
 *
 * Premium self-service withdrawal UI:
 * - Balance overview (available, approved, paid, pending)
 * - Withdrawal request form with live validation
 * - Paginated history table with status badges
 * - RTL + dark mode supported
 */
import { useState, useEffect, useCallback } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import affiliateApi from '../../../../services/affiliateApi'
import { Wallet, ArrowDownCircle, Clock, CheckCircle2, XCircle, Banknote, AlertTriangle, ArrowRight, Loader2 } from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({ opacity: 1, y: 0, transition: { delay: i * 0.06, duration: 0.4 } })
}

const STATUS_CONFIG = {
    Pending: { color: 'amber', icon: Clock, gradient: 'from-amber-500/10 to-amber-600/5', border: 'border-amber-500/20', text: 'text-amber-400', bg: 'bg-amber-500/10' },
    Approved: { color: 'blue', icon: CheckCircle2, gradient: 'from-blue-500/10 to-blue-600/5', border: 'border-blue-500/20', text: 'text-blue-400', bg: 'bg-blue-500/10' },
    Rejected: { color: 'red', icon: XCircle, gradient: 'from-red-500/10 to-red-600/5', border: 'border-red-500/20', text: 'text-red-400', bg: 'bg-red-500/10' },
    Paid: { color: 'emerald', icon: CheckCircle2, gradient: 'from-emerald-500/10 to-emerald-600/5', border: 'border-emerald-500/20', text: 'text-emerald-400', bg: 'bg-emerald-500/10' },
}

export default function PayoutsTab({ formatCurrency }) {
    const { t } = useLanguage()
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [page, setPage] = useState(1)

    // Request form state
    const [showForm, setShowForm] = useState(false)
    const [amount, setAmount] = useState('')
    const [submitting, setSubmitting] = useState(false)
    const [feedback, setFeedback] = useState(null) // { type: 'success'|'error', message }

    const loadPayouts = useCallback(async (p = 1) => {
        try {
            setLoading(true)
            const result = await affiliateApi.getPayouts(p, 10)
            setData(result)
            setPage(p)
        } catch {
            setData(null)
        } finally {
            setLoading(false)
        }
    }, [])

    useEffect(() => { loadPayouts() }, [loadPayouts])

    const handleRequestPayout = async () => {
        const numAmount = parseFloat(amount)
        if (isNaN(numAmount) || numAmount < 100) {
            setFeedback({ type: 'error', message: t('portal.payoutsTab.belowThreshold') })
            return
        }
        if (data && numAmount > data.availableForWithdrawal) {
            setFeedback({ type: 'error', message: t('portal.payoutsTab.insufficientFunds') })
            return
        }

        setSubmitting(true)
        setFeedback(null)
        try {
            await affiliateApi.requestPayout(numAmount)
            setFeedback({ type: 'success', message: t('portal.payoutsTab.successDesc') })
            setShowForm(false)
            setAmount('')
            // Reload to show new request and updated balance
            await loadPayouts(1)
        } catch (err) {
            const msg = err.response?.data?.message || t('portal.payoutsTab.errorTitle')
            setFeedback({ type: 'error', message: msg })
        } finally {
            setSubmitting(false)
        }
    }

    // ── Loading skeleton ──
    if (loading && !data) {
        return (
            <div className="space-y-4">
                {[...Array(3)].map((_, i) => (
                    <div key={i} className="h-28 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] animate-pulse" />
                ))}
            </div>
        )
    }

    const bal = data || {
        totalApproved: 0, totalPaid: 0, totalPendingRequests: 0,
        availableForWithdrawal: 0, minimumThreshold: 100, canWithdraw: false,
        currency: 'EGP', history: []
    }

    const balanceCards = [
        {
            label: t('portal.payoutsTab.availableBalance'),
            value: bal.availableForWithdrawal,
            icon: Wallet,
            color: '#10B981',
            accent: 'from-emerald-500/15 to-emerald-600/5',
            borderColor: 'border-emerald-500/20',
            highlight: true,
        },
        {
            label: t('portal.payoutsTab.totalApproved'),
            value: bal.totalApproved,
            icon: CheckCircle2,
            color: '#6C63FF',
            accent: 'from-[#6C63FF]/15 to-[#8B5CF6]/5',
            borderColor: 'border-[#6C63FF]/20'
        },
        {
            label: t('portal.payoutsTab.totalPaid'),
            value: bal.totalPaid,
            icon: Banknote,
            color: '#22C55E',
            accent: 'from-green-500/15 to-green-600/5',
            borderColor: 'border-green-500/20'
        },
        {
            label: t('portal.payoutsTab.pendingReserved'),
            value: bal.totalPendingRequests,
            icon: Clock,
            color: '#F59E0B',
            accent: 'from-amber-500/15 to-amber-600/5',
            borderColor: 'border-amber-500/20'
        },
    ]

    const numAmount = parseFloat(amount)
    const isValidAmount = !isNaN(numAmount) && numAmount >= 100 && numAmount <= bal.availableForWithdrawal

    return (
        <div className="space-y-6">
            {/* ── Feedback Toast ── */}
            <AnimatePresence>
                {feedback && (
                    <motion.div
                        initial={{ opacity: 0, y: -12 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -12 }}
                        className={`p-4 rounded-xl border flex items-center gap-3 ${feedback.type === 'success'
                            ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400'
                            : 'bg-red-500/10 border-red-500/20 text-red-400'
                            }`}
                    >
                        {feedback.type === 'success' ? <CheckCircle2 className="w-5 h-5 flex-shrink-0" /> : <AlertTriangle className="w-5 h-5 flex-shrink-0" />}
                        <div>
                            <p className="font-medium text-sm">
                                {feedback.type === 'success' ? t('portal.payoutsTab.successTitle') : t('portal.payoutsTab.errorTitle')}
                            </p>
                            <p className="text-xs opacity-80 mt-0.5">{feedback.message}</p>
                        </div>
                        <button onClick={() => setFeedback(null)} className="ml-auto text-current opacity-60 hover:opacity-100">×</button>
                    </motion.div>
                )}
            </AnimatePresence>

            {/* ── Balance Cards ── */}
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
                {balanceCards.map((card, idx) => {
                    const Icon = card.icon
                    return (
                        <motion.div
                            key={card.label}
                            variants={fadeUp}
                            initial="hidden"
                            animate="visible"
                            custom={idx}
                            className={`relative rounded-xl border ${card.borderColor} bg-gradient-to-br ${card.accent} p-4 overflow-hidden ${card.highlight ? 'ring-1 ring-emerald-500/30' : ''}`}
                        >
                            <div className="flex items-center gap-2 mb-2">
                                <Icon className="w-4 h-4" style={{ color: card.color }} />
                                <span className="text-[10px] font-medium uppercase tracking-widest text-gray-500 dark:text-gray-400">
                                    {card.label}
                                </span>
                            </div>
                            <p className={`text-xl font-bold ${card.highlight ? 'text-emerald-400' : 'text-gray-900 dark:text-white'}`}>
                                {formatCurrency(card.value, bal.currency)}
                            </p>
                            {card.highlight && (
                                <div className="absolute top-0 right-0 w-20 h-20 bg-emerald-500/5 rounded-full -translate-y-1/2 translate-x-1/2" />
                            )}
                        </motion.div>
                    )
                })}
            </div>

            {/* ── Withdrawal Action ── */}
            <motion.div
                variants={fadeUp}
                initial="hidden"
                animate="visible"
                custom={4}
                className="rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] p-5"
            >
                <div className="flex items-center justify-between mb-4">
                    <div className="flex items-center gap-2">
                        <ArrowDownCircle className="w-5 h-5 text-[#6C63FF]" />
                        <h3 className="text-base font-semibold text-gray-900 dark:text-white">{t('portal.payoutsTab.title')}</h3>
                    </div>
                    <div className="flex items-center gap-2 text-xs text-gray-500">
                        <span>{t('portal.payoutsTab.minimumThreshold')}:</span>
                        <span className="font-mono font-medium text-gray-700 dark:text-gray-300">{formatCurrency(bal.minimumThreshold, bal.currency)}</span>
                    </div>
                </div>

                <AnimatePresence mode="wait">
                    {!showForm ? (
                        <motion.div key="button" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
                            <button
                                onClick={() => { setShowForm(true); setFeedback(null) }}
                                disabled={!bal.canWithdraw}
                                className={`w-full py-3 rounded-xl font-medium text-sm transition-all duration-300 ${bal.canWithdraw
                                    ? 'bg-gradient-to-r from-[#6C63FF] to-[#8B5CF6] text-white hover:shadow-lg hover:shadow-[#6C63FF]/20 hover:-translate-y-0.5 active:translate-y-0'
                                    : 'bg-gray-100 dark:bg-white/[0.04] text-gray-400 dark:text-gray-600 cursor-not-allowed'
                                    }`}
                            >
                                <span className="flex items-center justify-center gap-2">
                                    <Wallet className="w-4 h-4" />
                                    {t('portal.payoutsTab.withdrawButton')}
                                </span>
                            </button>
                            {!bal.canWithdraw && (
                                <p className="text-xs text-gray-500 mt-2 text-center">
                                    {bal.availableForWithdrawal < bal.minimumThreshold
                                        ? t('portal.payoutsTab.belowThreshold')
                                        : t('portal.payoutsTab.insufficientFunds')
                                    }
                                </p>
                            )}
                        </motion.div>
                    ) : (
                        <motion.div key="form" initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -8 }} className="space-y-4">
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1.5">{t('portal.payoutsTab.amountLabel')}</label>
                                <div className="relative">
                                    <input
                                        type="number"
                                        min="100"
                                        max={bal.availableForWithdrawal}
                                        step="0.01"
                                        value={amount}
                                        onChange={(e) => setAmount(e.target.value)}
                                        placeholder={t('portal.payoutsTab.amountPlaceholder')}
                                        className="w-full px-4 py-3 rounded-xl border border-gray-200 dark:border-white/[0.08] bg-gray-50 dark:bg-white/[0.03] text-gray-900 dark:text-white placeholder:text-gray-400 dark:placeholder:text-gray-600 text-lg font-mono focus:outline-none focus:ring-2 focus:ring-[#6C63FF]/30 focus:border-[#6C63FF]/50 transition-all"
                                        autoFocus
                                    />
                                    <span className="absolute right-3 top-1/2 -translate-y-1/2 text-xs text-gray-400 font-medium">{bal.currency}</span>
                                </div>
                                {amount && !isValidAmount && (
                                    <p className="text-[11px] text-red-400 mt-1">
                                        {numAmount < 100
                                            ? `${t('portal.payoutsTab.minimumThreshold')}: ${formatCurrency(100, bal.currency)}`
                                            : `${t('portal.payoutsTab.availableBalance')}: ${formatCurrency(bal.availableForWithdrawal, bal.currency)}`
                                        }
                                    </p>
                                )}
                                {amount && isValidAmount && (
                                    <p className="text-[11px] text-emerald-400 mt-1 flex items-center gap-1">
                                        <CheckCircle2 className="w-3 h-3" /> {formatCurrency(numAmount, bal.currency)}
                                    </p>
                                )}
                            </div>

                            <div className="flex gap-3">
                                <button
                                    onClick={() => { setShowForm(false); setAmount(''); setFeedback(null) }}
                                    className="flex-1 py-2.5 rounded-xl border border-gray-200 dark:border-white/[0.08] text-gray-600 dark:text-gray-400 text-sm font-medium hover:bg-gray-50 dark:hover:bg-white/[0.03] transition-all"
                                >
                                    {t('portal.payoutsTab.cancel')}
                                </button>
                                <button
                                    onClick={handleRequestPayout}
                                    disabled={!isValidAmount || submitting}
                                    className={`flex-1 py-2.5 rounded-xl text-sm font-medium transition-all duration-300 flex items-center justify-center gap-2 ${isValidAmount && !submitting
                                        ? 'bg-gradient-to-r from-[#6C63FF] to-[#8B5CF6] text-white hover:shadow-lg hover:shadow-[#6C63FF]/20'
                                        : 'bg-gray-100 dark:bg-white/[0.04] text-gray-400 dark:text-gray-600 cursor-not-allowed'
                                        }`}
                                >
                                    {submitting ? (
                                        <><Loader2 className="w-4 h-4 animate-spin" /> {t('portal.payoutsTab.withdrawing')}</>
                                    ) : (
                                        <><ArrowRight className="w-4 h-4" /> {t('portal.payoutsTab.confirm')}</>
                                    )}
                                </button>
                            </div>
                        </motion.div>
                    )}
                </AnimatePresence>
            </motion.div>

            {/* ── History Table ── */}
            <motion.div
                variants={fadeUp}
                initial="hidden"
                animate="visible"
                custom={5}
                className="rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] overflow-hidden"
            >
                <div className="px-5 py-4 border-b border-gray-200/50 dark:border-white/[0.04]">
                    <h3 className="text-sm font-semibold text-gray-900 dark:text-white">{t('portal.payoutsTab.history')}</h3>
                </div>

                {!bal.history?.length ? (
                    <div className="text-center py-16">
                        <Wallet className="w-12 h-12 text-gray-300 dark:text-gray-700 mx-auto mb-3" />
                        <p className="text-sm font-medium text-gray-500 dark:text-gray-400">{t('portal.payoutsTab.noHistory')}</p>
                        <p className="text-xs text-gray-400 dark:text-gray-600 mt-1">{t('portal.payoutsTab.noHistoryDesc')}</p>
                    </div>
                ) : (
                    <>
                        {/* Mobile cards */}
                        <div className="md:hidden divide-y divide-gray-100 dark:divide-white/[0.04]">
                            {bal.history.map((req) => {
                                const cfg = STATUS_CONFIG[req.status] || STATUS_CONFIG.Pending
                                const StatusIcon = cfg.icon
                                return (
                                    <div key={req.id} className="p-4 space-y-2">
                                        <div className="flex items-center justify-between">
                                            <div className="flex flex-col">
                                                <span className="text-sm font-mono font-semibold text-gray-900 dark:text-white">
                                                    {formatCurrency(req.requestedAmount, req.currency)}
                                                </span>
                                                {req.remainingAmount != null && req.remainingAmount < req.requestedAmount && req.remainingAmount > 0 && req.status !== 'Rejected' && (
                                                    <span className="text-xs text-orange-500 font-mono mt-0.5">Left: {formatCurrency(req.remainingAmount, req.currency)}</span>
                                                )}
                                            </div>
                                            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.bg} ${cfg.text} border ${cfg.border}`}>
                                                <StatusIcon className="w-3 h-3" />
                                                {t(`portal.payoutsTab.status${req.status}`)}
                                            </span>
                                        </div>
                                        <div className="flex items-center justify-between text-xs text-gray-500">
                                            <span>{new Date(req.requestedAtUtc).toLocaleDateString()}</span>
                                            {req.paymentReference && (
                                                <span className="font-mono text-[10px] truncate max-w-[120px]">{req.paymentReference}</span>
                                            )}
                                        </div>
                                    </div>
                                )
                            })}
                        </div>

                        {/* Desktop table */}
                        <div className="hidden md:block overflow-x-auto">
                            <table className="w-full text-sm">
                                <thead>
                                    <tr className="border-b border-gray-100 dark:border-white/[0.04]">
                                        <th className="text-left px-5 py-3 text-[10px] font-medium uppercase tracking-widest text-gray-400">{t('portal.payoutsTab.amount')}</th>
                                        <th className="text-left px-5 py-3 text-[10px] font-medium uppercase tracking-widest text-gray-400">{t('portal.payoutsTab.status')}</th>
                                        <th className="text-left px-5 py-3 text-[10px] font-medium uppercase tracking-widest text-gray-400">{t('portal.payoutsTab.date')}</th>
                                        <th className="text-left px-5 py-3 text-[10px] font-medium uppercase tracking-widest text-gray-400">{t('portal.payoutsTab.reference')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-100 dark:divide-white/[0.04]">
                                    {bal.history.map((req) => {
                                        const cfg = STATUS_CONFIG[req.status] || STATUS_CONFIG.Pending
                                        const StatusIcon = cfg.icon
                                        return (
                                            <tr key={req.id} className="hover:bg-gray-50/50 dark:hover:bg-white/[0.01] transition-colors">
                                                <td className="px-5 py-3.5 font-mono font-medium text-gray-900 dark:text-white">
                                                    {formatCurrency(req.requestedAmount, req.currency)}
                                                    {req.approvedAmount != null && req.approvedAmount !== req.requestedAmount && (
                                                        <span className="text-xs text-gray-500 ml-1">
                                                            Appr: {formatCurrency(req.approvedAmount, req.currency)}
                                                        </span>
                                                    )}
                                                    {req.remainingAmount != null && req.remainingAmount < req.requestedAmount && req.remainingAmount > 0 && req.status !== 'Rejected' && (
                                                        <span className="text-xs text-orange-500 block">Left: {formatCurrency(req.remainingAmount, req.currency)}</span>
                                                    )}
                                                </td>
                                                <td className="px-5 py-3.5">
                                                    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[11px] font-medium ${cfg.bg} ${cfg.text} border ${cfg.border}`}>
                                                        <StatusIcon className="w-3 h-3" />
                                                        {t(`portal.payoutsTab.status${req.status}`)}
                                                    </span>
                                                </td>
                                                <td className="px-5 py-3.5 text-gray-500 text-xs">
                                                    {new Date(req.requestedAtUtc).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}
                                                    {req.paidAtUtc && (
                                                        <span className="block text-[10px] text-emerald-500 mt-0.5">
                                                            Paid: {new Date(req.paidAtUtc).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}
                                                        </span>
                                                    )}
                                                </td>
                                                <td className="px-5 py-3.5 text-gray-500 font-mono text-xs truncate max-w-[200px]">
                                                    {req.paymentReference || '—'}
                                                </td>
                                            </tr>
                                        )
                                    })}
                                </tbody>
                            </table>
                        </div>

                        {/* Pagination */}
                        {bal.history.length >= 10 && (
                            <div className="px-5 py-3 border-t border-gray-100 dark:border-white/[0.04] flex items-center justify-center gap-2">
                                <button
                                    onClick={() => loadPayouts(page - 1)}
                                    disabled={page <= 1 || loading}
                                    className="px-3 py-1.5 rounded-lg text-xs font-medium text-gray-500 hover:text-gray-700 dark:hover:text-gray-300 hover:bg-gray-50 dark:hover:bg-white/[0.03] disabled:opacity-30 disabled:cursor-not-allowed transition-all"
                                >
                                    ← Previous
                                </button>
                                <span className="text-xs text-gray-500">Page {page}</span>
                                <button
                                    onClick={() => loadPayouts(page + 1)}
                                    disabled={loading}
                                    className="px-3 py-1.5 rounded-lg text-xs font-medium text-gray-500 hover:text-gray-700 dark:hover:text-gray-300 hover:bg-gray-50 dark:hover:bg-white/[0.03] disabled:opacity-30 disabled:cursor-not-allowed transition-all"
                                >
                                    Next →
                                </button>
                            </div>
                        )}
                    </>
                )}
            </motion.div>
        </div>
    )
}

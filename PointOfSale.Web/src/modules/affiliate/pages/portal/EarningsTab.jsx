/**
 * AF-15: Earnings Tab — Settlement summary with progress bars
 */
import { useState, useEffect } from 'react'
import { motion } from 'framer-motion'
import affiliateApi from '../../../../services/affiliateApi'
import { DollarSign, TrendingUp, CheckCircle2, ArrowRight } from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({ opacity: 1, y: 0, transition: { delay: i * 0.06, duration: 0.4 } })
}

export default function EarningsTab({ formatCurrency }) {
    const { t } = useLanguage()
    const [data, setData] = useState([])
    const [loading, setLoading] = useState(true)

    useEffect(() => {
        affiliateApi.getEarningsSummary()
            .then(d => setData(d))
            .catch(() => setData([]))
            .finally(() => setLoading(false))
    }, [])

    if (loading) {
        return (
            <div className="space-y-4">
                {[...Array(2)].map((_, i) => (
                    <div key={i} className="h-48 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] animate-pulse" />
                ))}
            </div>
        )
    }

    if (!data.length) {
        return (
            <div className="text-center py-20">
                <DollarSign className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">{t('portal.earningsTab.noEarnings')}</h3>
                <p className="text-gray-500 text-sm">{t('portal.earningsTab.noEarningsDesc')}</p>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {data.map((summary, idx) => {
                const paidPct = summary.totalEarned > 0 ? Math.round((summary.totalPaid / summary.totalEarned) * 100) : 0
                const approvedPct = summary.totalEarned > 0 ? Math.round(((summary.totalApproved - summary.totalPaid) / summary.totalEarned) * 100) : 0

                return (
                    <motion.div
                        key={summary.currency}
                        variants={fadeUp}
                        initial="hidden"
                        animate="visible"
                        custom={idx}
                        className="rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] p-6"
                    >
                        <div className="flex items-center justify-between mb-6">
                            <h3 className="text-base font-semibold text-gray-900 dark:text-white">{summary.currency} {t('portal.earningsTab.earnings')}</h3>
                            <span className="text-xs px-3 py-1 rounded-full bg-white dark:bg-white/[0.04] text-gray-600 dark:text-gray-400 border border-gray-200 dark:border-white/[0.06]">
                                {summary.currency}
                            </span>
                        </div>

                        {/* Summary cards */}
                        <div className="grid grid-cols-2 md:grid-cols-5 gap-3 mb-6">
                            {[
                                { label: t('portal.earningsTab.totalEarned'), value: summary.totalEarned, color: '#6C63FF' },
                                { label: t('portal.earningsTab.approved'), value: summary.totalApproved, color: '#10B981' },
                                { label: t('portal.earningsTab.paid'), value: summary.totalPaid, color: '#22C55E' },
                                { label: t('portal.earningsTab.reversed'), value: summary.totalReversed, color: '#EF4444' },
                                { label: t('portal.earningsTab.netEarnings'), value: summary.netEarnings, color: '#A855F7' },
                            ].map((item, i) => (
                                <div key={i} className="p-3 rounded-lg border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]">
                                    <p className="text-[10px] font-medium uppercase tracking-widest mb-1" style={{ color: item.color + '99' }}>
                                        {item.label}
                                    </p>
                                    <p className="text-lg font-bold text-gray-900 dark:text-white">
                                        {formatCurrency(item.value, summary.currency)}
                                    </p>
                                </div>
                            ))}
                        </div>

                        {/* Settlement Progress */}
                        <div>
                            <div className="flex items-center justify-between text-xs text-gray-500 mb-2">
                                <span>{t('portal.earningsTab.settlementProgress')}</span>
                                <span>{paidPct}% {t('portal.earningsTab.paidLabel')}</span>
                            </div>
                            <div className="w-full bg-white dark:bg-white/[0.04] rounded-full h-2.5 overflow-hidden">
                                <div className="h-full flex">
                                    <div className="bg-green-500 transition-all duration-500" style={{ width: `${paidPct}%` }} />
                                    <div className="bg-emerald-500/40 transition-all duration-500" style={{ width: `${approvedPct}%` }} />
                                </div>
                            </div>
                            <div className="flex items-center gap-4 mt-2 text-[10px] text-gray-500">
                                <span className="flex items-center gap-1"><span className="w-2 h-2 bg-green-500 rounded-full" /> {t('portal.earningsTab.paid')}</span>
                                <span className="flex items-center gap-1"><span className="w-2 h-2 bg-emerald-500/40 rounded-full" /> {t('portal.earningsTab.approved')}</span>
                                <span className="flex items-center gap-1"><span className="w-2 h-2 bg-white dark:bg-white/[0.04] rounded-full" /> {t('portal.earningsTab.pending')}</span>
                            </div>
                        </div>

                        {/* Reversed warning */}
                        {(summary.totalReversed || 0) > 0 && (
                            <div className="mt-4 p-3 rounded-lg bg-red-500/5 border border-red-500/10 text-sm text-red-400 flex items-center gap-2">
                                ⚠️ {t('portal.earningsTab.reversedWarning')}: {formatCurrency(summary.totalReversed, summary.currency)}
                            </div>
                        )}
                    </motion.div>
                )
            })}
        </div>
    )
}

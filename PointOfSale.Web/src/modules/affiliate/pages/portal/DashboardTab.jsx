/**
 * AF-15: Dashboard Tab — KPI cards + Settlement Pipeline + Charts
 */
import { useState, useEffect } from 'react'
import { motion } from 'framer-motion'
import { LineChart, Line, BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts'
import affiliateApi from '../../../../services/affiliateApi'
import {
    DollarSign, TrendingUp, ShoppingBag,
    ArrowUpRight, ArrowDownRight, CheckCircle2
} from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({ opacity: 1, y: 0, transition: { delay: i * 0.06, duration: 0.4 } })
}

function SkeletonCard() {
    return (
        <div className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] animate-pulse">
            <div className="h-3 w-20 bg-white/5 rounded mb-3" />
            <div className="h-7 w-28 bg-white/5 rounded mb-2" />
            <div className="h-3 w-16 bg-white dark:bg-white/[0.03] rounded" />
        </div>
    )
}

function KPICard({ label, value, icon: Icon, change, color = '#6C63FF', index = 0 }) {
    const isPositive = change > 0
    return (
        <motion.div
            variants={fadeUp}
            initial="hidden"
            animate="visible"
            custom={index}
            className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] hover:bg-gray-50 dark:hover:bg-white/[0.03] transition-colors group"
        >
            <div className="flex items-center justify-between mb-3">
                <span className="text-xs font-medium text-gray-500 uppercase tracking-wider">{label}</span>
                <div className="w-8 h-8 rounded-lg flex items-center justify-center" style={{ backgroundColor: `${color}15` }}>
                    <Icon className="w-4 h-4" style={{ color }} />
                </div>
            </div>
            <p className="text-2xl font-bold text-gray-900 dark:text-white mb-1">{value}</p>
            {change != null && (
                <div className={`flex items-center gap-1 text-xs ${isPositive ? 'text-emerald-400' : 'text-red-400'}`}>
                    {isPositive ? <ArrowUpRight className="w-3 h-3" /> : <ArrowDownRight className="w-3 h-3" />}
                    <span>{Math.abs(change)}% vs last period</span>
                </div>
            )}
        </motion.div>
    )
}

function PipelineStep({ label, value, active, color }) {
    return (
        <div className={`flex-1 text-center p-4 rounded-xl border transition-all ${active ? `border-${color}-500/20 bg-${color}-500/5` : 'border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]'
            }`}>
            <p className="text-[10px] font-medium text-gray-500 uppercase tracking-widest mb-1">{label}</p>
            <p className={`text-lg font-bold ${active ? 'text-gray-900 dark:text-white' : 'text-gray-600'}`}>{value}</p>
        </div>
    )
}

// Custom chart tooltip
const ChartTooltip = ({ active, payload, label }) => {
    if (!active || !payload?.length) return null
    return (
        <div className="bg-white dark:bg-[#1A2133] border border-gray-200 dark:border-white/10 rounded-lg p-3 shadow-xl text-xs">
            <p className="text-gray-600 dark:text-gray-400 mb-1">{label}</p>
            {payload.map((p, i) => (
                <p key={i} className="font-medium" style={{ color: p.color }}>
                    {p.name}: {typeof p.value === 'number' ? p.value.toLocaleString() : p.value}
                </p>
            ))}
        </div>
    )
}

export default function DashboardTab({ formatCurrency }) {
    const { t } = useLanguage()
    const [data, setData] = useState(null)
    const [analytics, setAnalytics] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)

    useEffect(() => {
        Promise.all([
            affiliateApi.getDashboard(),
            affiliateApi.getAnalyticsSummary().catch(() => null)
        ])
            .then(([d, a]) => { setData(d); setAnalytics(a); setError(null) })
            .catch(e => setError(e?.response?.data?.message || 'Failed to load'))
            .finally(() => setLoading(false))
    }, [])

    if (loading) {
        return (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                {[...Array(4)].map((_, i) => <SkeletonCard key={i} />)}
            </div>
        )
    }

    if (error) {
        return (
            <div className="text-center py-20">
                <p className="text-gray-500">⚠️ {error}</p>
            </div>
        )
    }

    const chartData = analytics?.dailyData || []
    const earningsByCurrency = Array.isArray(data?.earningsByCurrency) ? data.earningsByCurrency : []

    return (
        <div className="space-y-6">
            {/* KPI Cards */}
            {earningsByCurrency.map((summary, currencyIndex) => (
                <div key={summary.currency} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                    <KPICard label={`${t('portal.dashboardStats.totalEarned')} (${summary.currency})`} value={formatCurrency(summary.totalEarned || 0, summary.currency)} icon={DollarSign} color="#6C63FF" index={currencyIndex * 4} />
                    <KPICard label={`${t('portal.dashboardStats.approved')} (${summary.currency})`} value={formatCurrency(summary.totalApproved || 0, summary.currency)} icon={CheckCircle2} color="#10B981" index={currencyIndex * 4 + 1} />
                    <KPICard label={t('portal.dashboardStats.totalOrders')} value={data?.totalOrders || 0} icon={ShoppingBag} color="#F59E0B" index={currencyIndex * 4 + 2} />
                    <KPICard label={`${t('portal.dashboardStats.netEarnings')} (${summary.currency})`} value={formatCurrency(summary.netEarnings || 0, summary.currency)} icon={TrendingUp} color="#EC4899" index={currencyIndex * 4 + 3} />
                </div>
            ))}

            {/* Settlement Pipeline */}
            <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={4}
                className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]">
                <h3 className="text-sm font-semibold text-gray-900 dark:text-white mb-4">{t('portal.dashboardStats.pipeline')}</h3>
                <div className="space-y-3">
                    {earningsByCurrency.map((summary) => (
                        <div key={summary.currency} className="flex items-center gap-2">
                            <PipelineStep label={`${t('portal.dashboardStats.earned')} (${summary.currency})`} value={formatCurrency(summary.totalEarned || 0, summary.currency)} active color="blue" />
                            <div className="text-gray-600">→</div>
                            <PipelineStep label={`${t('portal.dashboardStats.approved')} (${summary.currency})`} value={formatCurrency(summary.totalApproved || 0, summary.currency)} active={(summary.totalApproved || 0) > 0} color="emerald" />
                            <div className="text-gray-600">→</div>
                            <PipelineStep label={`${t('portal.dashboardStats.paid')} (${summary.currency})`} value={formatCurrency(summary.totalPaid || 0, summary.currency)} active={(summary.totalPaid || 0) > 0} color="green" />
                        </div>
                    ))}
                </div>
            </motion.div>

            {/* Charts */}
            <div className="grid lg:grid-cols-2 gap-4">
                {/* Clicks Chart */}
                <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={5}
                    className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]">
                    <h3 className="text-sm font-semibold text-gray-900 dark:text-white mb-4">{t('portal.dashboardStats.clicksChartTitle')}</h3>
                    <div className="h-48" style={{ minWidth: 0, minHeight: 192 }}>
                        <ResponsiveContainer width="100%" height="100%">
                            <BarChart data={chartData}>
                                <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.04)" />
                                <XAxis dataKey="date" tick={{ fill: '#6B7280', fontSize: 11 }} axisLine={false} tickLine={false} />
                                <YAxis tick={{ fill: '#6B7280', fontSize: 11 }} axisLine={false} tickLine={false} />
                                <Tooltip content={<ChartTooltip />} />
                                <Bar dataKey="clicks" fill="#6C63FF" radius={[4, 4, 0, 0]} name={t('portal.dashboardStats.clicks')} />
                            </BarChart>
                        </ResponsiveContainer>
                    </div>
                </motion.div>

                {/* Orders Chart */}
                <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={6}
                    className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]">
                    <h3 className="text-sm font-semibold text-gray-900 dark:text-white mb-4">{t('portal.dashboardStats.ordersChartTitle')}</h3>
                    <div className="h-48" style={{ minWidth: 0, minHeight: 192 }}>
                        <ResponsiveContainer width="100%" height="100%">
                            <LineChart data={chartData}>
                                <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.04)" />
                                <XAxis dataKey="date" tick={{ fill: '#6B7280', fontSize: 11 }} axisLine={false} tickLine={false} />
                                <YAxis tick={{ fill: '#6B7280', fontSize: 11 }} axisLine={false} tickLine={false} />
                                <Tooltip content={<ChartTooltip />} />
                                <Line type="monotone" dataKey="orders" stroke="#10B981" strokeWidth={2} dot={{ fill: '#10B981', r: 3 }} name={t('portal.dashboardStats.orders')} />
                            </LineChart>
                        </ResponsiveContainer>
                    </div>
                </motion.div>
            </div>
        </div>
    )
}

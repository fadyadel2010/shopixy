/**
 * AF-24: Analytics Tab — Full performance intelligence dashboard
 * 
 * Sections:
 *   1. KPI Cards (Clicks, Orders, Earnings, Conversion Rate)
 *   2. Earnings Trend (Line Chart)
 *   3. Top Products Table
 *   4. Top Stores Table
 * 
 * All data from MongoDB daily projections via /api/affiliate/analytics/*
 * Fully bilingual (EN/AR) via useLanguage() i18n hook.
 */
import { useState, useEffect, useCallback } from 'react'
import { motion } from 'framer-motion'
import {
    LineChart, Line, BarChart, Bar, AreaChart, Area,
    XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer
} from 'recharts'
import affiliateApi from '../../../../services/affiliateApi'
import {
    MousePointerClick, ShoppingBag, DollarSign, TrendingUp,
    ArrowUpRight, Package, Store, Calendar, Loader2
} from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({ opacity: 1, y: 0, transition: { delay: i * 0.06, duration: 0.4 } })
}

const RANGE_KEYS = [
    { value: 7, key: 'range7' },
    { value: 14, key: 'range14' },
    { value: 30, key: 'range30' },
    { value: 90, key: 'range90' },
    { value: 365, key: 'range365' },
]

function SkeletonCard() {
    return (
        <div className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] animate-pulse">
            <div className="h-3 w-20 bg-white/5 rounded mb-3" />
            <div className="h-7 w-28 bg-white/5 rounded mb-2" />
            <div className="h-3 w-16 bg-white dark:bg-white/[0.03] rounded" />
        </div>
    )
}

function SkeletonTable() {
    return (
        <div className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] animate-pulse space-y-3">
            <div className="h-4 w-32 bg-white/5 rounded" />
            {[...Array(5)].map((_, i) => (
                <div key={i} className="flex gap-4">
                    <div className="h-3 w-40 bg-white dark:bg-white/[0.03] rounded" />
                    <div className="h-3 w-20 bg-white dark:bg-white/[0.03] rounded ml-auto" />
                </div>
            ))}
        </div>
    )
}

function KPICard({ label, value, icon: Icon, color = '#6C63FF', index = 0, suffix = '' }) {
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
            <p className="text-2xl font-bold text-gray-900 dark:text-white mb-1">
                {value}{suffix && <span className="text-sm text-gray-500 ml-1">{suffix}</span>}
            </p>
        </motion.div>
    )
}

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

function formatCurrencyValue(val) {
    if (val == null) return '—'
    return new Intl.NumberFormat('en-US', {
        style: 'currency', currency: 'EGP',
        minimumFractionDigits: 2
    }).format(val)
}

export default function AnalyticsTab() {
    const { t } = useLanguage()
    const [range, setRange] = useState(30)
    const [summary, setSummary] = useState(null)
    const [trend, setTrend] = useState([])
    const [topProducts, setTopProducts] = useState([])
    const [topStores, setTopStores] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)

    const ta = (key) => t(`portal.analyticsTab.${key}`)

    const loadData = useCallback(async (r) => {
        setLoading(true)
        setError(null)
        try {
            const [s, tr, p, st] = await Promise.all([
                affiliateApi.getAnalyticsSummary(r),
                affiliateApi.getAnalyticsTrend(r),
                affiliateApi.getAnalyticsTopProducts(r, 10),
                affiliateApi.getAnalyticsTopStores(r, 10),
            ])
            setSummary(s)
            setTrend(tr)
            setTopProducts(p)
            setTopStores(st)
        } catch (e) {
            setError(e?.response?.data?.message || ta('failedToLoad'))
        } finally {
            setLoading(false)
        }
    }, [])

    useEffect(() => { loadData(range) }, [range, loadData])

    // Pluralization helper
    const pluralize = (count, singularKey, pluralKey) =>
        `${count} ${count === 1 ? ta(singularKey) : ta(pluralKey)}`

    if (error) {
        return (
            <div className="text-center py-20">
                <p className="text-gray-500">⚠️ {error}</p>
                <button
                    onClick={() => loadData(range)}
                    className="mt-3 px-4 py-2 text-sm rounded-lg bg-[#6C63FF]/10 text-[#6C63FF] hover:bg-[#6C63FF]/20 transition-colors"
                >
                    {ta('retry')}
                </button>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {/* Header + Range Selector */}
            <div className="flex items-center justify-between">
                <div>
                    <h2 className="text-lg font-bold text-gray-900 dark:text-white">{ta('title')}</h2>
                    <p className="text-xs text-gray-500 mt-0.5">{ta('subtitle')}</p>
                </div>
                <div className="flex items-center gap-1 p-1 rounded-xl bg-white dark:bg-white/[0.03] border border-gray-200 dark:border-white/[0.06]">
                    {RANGE_KEYS.map(r => (
                        <button
                            key={r.value}
                            onClick={() => setRange(r.value)}
                            className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-all ${range === r.value
                                    ? 'bg-[#6C63FF]/15 text-[#6C63FF] border border-[#6C63FF]/25'
                                    : 'text-gray-500 hover:text-gray-700 dark:hover:text-gray-300 border border-transparent'
                                }`}
                        >
                            {ta(r.key)}
                        </button>
                    ))}
                </div>
            </div>

            {/* Loading */}
            {loading ? (
                <div className="space-y-6">
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                        {[...Array(4)].map((_, i) => <SkeletonCard key={i} />)}
                    </div>
                    <SkeletonTable />
                </div>
            ) : (
                <>
                    {/* KPI Cards */}
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                        <KPICard
                            label={ta('totalClicks')}
                            value={(summary?.totalClicks || 0).toLocaleString()}
                            icon={MousePointerClick}
                            color="#6C63FF"
                            index={0}
                        />
                        <KPICard
                            label={ta('totalOrders')}
                            value={(summary?.totalOrders || 0).toLocaleString()}
                            icon={ShoppingBag}
                            color="#F59E0B"
                            index={1}
                        />
                        <KPICard
                            label={ta('totalEarnings')}
                            value={formatCurrencyValue(summary?.totalEarnings || 0)}
                            icon={DollarSign}
                            color="#10B981"
                            index={2}
                        />
                        <KPICard
                            label={ta('conversionRate')}
                            value={(summary?.conversionRate || 0).toFixed(2)}
                            icon={TrendingUp}
                            color="#EC4899"
                            index={3}
                            suffix="%"
                        />
                    </div>

                    {/* Earnings Trend Chart */}
                    <motion.div
                        variants={fadeUp} initial="hidden" animate="visible" custom={4}
                        className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]"
                    >
                        <div className="flex items-center justify-between mb-4">
                            <h3 className="text-sm font-semibold text-gray-900 dark:text-white">{ta('earningsTrend')}</h3>
                            <div className="flex items-center gap-4 text-[10px] text-gray-500">
                                <span className="flex items-center gap-1.5">
                                    <span className="w-2.5 h-2.5 rounded-full bg-[#6C63FF]" /> {ta('earnings')}
                                </span>
                                <span className="flex items-center gap-1.5">
                                    <span className="w-2.5 h-2.5 rounded-full bg-emerald-400" /> {ta('orders')}
                                </span>
                            </div>
                        </div>
                        <div className="h-56" style={{ minWidth: 0, minHeight: 224 }}>
                            {trend.length > 0 ? (
                                <ResponsiveContainer width="100%" height="100%">
                                    <AreaChart data={trend}>
                                        <defs>
                                            <linearGradient id="earningsGrad" x1="0" y1="0" x2="0" y2="1">
                                                <stop offset="5%" stopColor="#6C63FF" stopOpacity={0.3} />
                                                <stop offset="95%" stopColor="#6C63FF" stopOpacity={0} />
                                            </linearGradient>
                                        </defs>
                                        <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.04)" />
                                        <XAxis
                                            dataKey="date"
                                            tick={{ fill: '#6B7280', fontSize: 10 }}
                                            axisLine={false}
                                            tickLine={false}
                                            tickFormatter={d => d?.substring(5) || d}
                                        />
                                        <YAxis
                                            yAxisId="left"
                                            tick={{ fill: '#6B7280', fontSize: 10 }}
                                            axisLine={false}
                                            tickLine={false}
                                        />
                                        <YAxis
                                            yAxisId="right"
                                            orientation="right"
                                            tick={{ fill: '#6B7280', fontSize: 10 }}
                                            axisLine={false}
                                            tickLine={false}
                                        />
                                        <Tooltip content={<ChartTooltip />} />
                                        <Area
                                            yAxisId="left"
                                            type="monotone"
                                            dataKey="earnings"
                                            stroke="#6C63FF"
                                            fill="url(#earningsGrad)"
                                            strokeWidth={2}
                                            name={ta('earnings')}
                                        />
                                        <Line
                                            yAxisId="right"
                                            type="monotone"
                                            dataKey="orders"
                                            stroke="#10B981"
                                            strokeWidth={2}
                                            dot={{ fill: '#10B981', r: 2 }}
                                            name={ta('orders')}
                                        />
                                    </AreaChart>
                                </ResponsiveContainer>
                            ) : (
                                <div className="h-full flex items-center justify-center text-gray-600 text-sm">
                                    {ta('noDataInPeriod')}
                                </div>
                            )}
                        </div>
                    </motion.div>

                    {/* Clicks vs Orders Chart */}
                    <motion.div
                        variants={fadeUp} initial="hidden" animate="visible" custom={5}
                        className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]"
                    >
                        <h3 className="text-sm font-semibold text-gray-900 dark:text-white mb-4">{ta('clicksVsConversions')}</h3>
                        <div className="h-48" style={{ minWidth: 0, minHeight: 192 }}>
                            {trend.length > 0 ? (
                                <ResponsiveContainer width="100%" height="100%">
                                    <BarChart data={trend}>
                                        <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.04)" />
                                        <XAxis
                                            dataKey="date"
                                            tick={{ fill: '#6B7280', fontSize: 10 }}
                                            axisLine={false}
                                            tickLine={false}
                                            tickFormatter={d => d?.substring(5) || d}
                                        />
                                        <YAxis tick={{ fill: '#6B7280', fontSize: 10 }} axisLine={false} tickLine={false} />
                                        <Tooltip content={<ChartTooltip />} />
                                        <Bar dataKey="clicks" fill="#6C63FF" radius={[4, 4, 0, 0]} name={ta('clicks')} opacity={0.6} />
                                        <Bar dataKey="orders" fill="#10B981" radius={[4, 4, 0, 0]} name={ta('orders')} />
                                    </BarChart>
                                </ResponsiveContainer>
                            ) : (
                                <div className="h-full flex items-center justify-center text-gray-600 text-sm">
                                    {ta('noDataInPeriod')}
                                </div>
                            )}
                        </div>
                    </motion.div>

                    {/* Top Products + Top Stores Grid */}
                    <div className="grid lg:grid-cols-2 gap-4">
                        {/* Top Products */}
                        <motion.div
                            variants={fadeUp} initial="hidden" animate="visible" custom={6}
                            className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]"
                        >
                            <div className="flex items-center gap-2 mb-4">
                                <Package className="w-4 h-4 text-[#6C63FF]" />
                                <h3 className="text-sm font-semibold text-gray-900 dark:text-white">{ta('topProductsByEarnings')}</h3>
                            </div>
                            {topProducts.length > 0 ? (
                                <div className="space-y-2">
                                    {topProducts.map((p, i) => (
                                        <div
                                            key={p.productId}
                                            className="flex items-center gap-3 p-3 rounded-lg bg-white dark:bg-white/[0.02] hover:bg-gray-50 dark:hover:bg-white/[0.04] transition-colors"
                                        >
                                            <span className="text-xs font-bold text-gray-600 w-5 text-center">
                                                {i + 1}
                                            </span>
                                            <div className="flex-1 min-w-0">
                                                <p className="text-sm text-gray-900 dark:text-white truncate">{p.name || ta('unknownProduct')}</p>
                                                <p className="text-[10px] text-gray-500">{pluralize(p.orders, 'order', 'orderPlural')}</p>
                                            </div>
                                            <span className="text-sm font-semibold text-emerald-400">
                                                {formatCurrencyValue(p.earnings)}
                                            </span>
                                        </div>
                                    ))}
                                </div>
                            ) : (
                                <div className="text-center py-8 text-gray-600 text-sm">
                                    {ta('noProductEarnings')}
                                </div>
                            )}
                        </motion.div>

                        {/* Top Stores */}
                        <motion.div
                            variants={fadeUp} initial="hidden" animate="visible" custom={7}
                            className="p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]"
                        >
                            <div className="flex items-center gap-2 mb-4">
                                <Store className="w-4 h-4 text-[#F59E0B]" />
                                <h3 className="text-sm font-semibold text-gray-900 dark:text-white">{ta('topStoresByEarnings')}</h3>
                            </div>
                            {topStores.length > 0 ? (
                                <div className="space-y-2">
                                    {topStores.map((s, i) => {
                                        const maxEarnings = topStores[0]?.earnings || 1
                                        const barWidth = Math.max(5, (s.earnings / maxEarnings) * 100)
                                        return (
                                            <div
                                                key={s.tenantId}
                                                className="relative p-3 rounded-lg bg-white dark:bg-white/[0.02] hover:bg-gray-50 dark:hover:bg-white/[0.04] transition-colors overflow-hidden"
                                            >
                                                {/* Progress bar */}
                                                <div
                                                    className="absolute inset-y-0 left-0 bg-[#F59E0B]/5 rounded-lg transition-all"
                                                    style={{ width: `${barWidth}%` }}
                                                />
                                                <div className="relative flex items-center gap-3">
                                                    <span className="text-xs font-bold text-gray-600 w-5 text-center">
                                                        {i + 1}
                                                    </span>
                                                    <div className="flex-1 min-w-0">
                                                        <p className="text-sm text-gray-900 dark:text-white truncate">{s.storeName}</p>
                                                        <p className="text-[10px] text-gray-500">
                                                            {pluralize(s.orders, 'order', 'orderPlural')} · {pluralize(s.clicks || 0, 'click', 'clickPlural')}
                                                        </p>
                                                    </div>
                                                    <span className="text-sm font-semibold text-amber-400">
                                                        {formatCurrencyValue(s.earnings)}
                                                    </span>
                                                </div>
                                            </div>
                                        )
                                    })}
                                </div>
                            ) : (
                                <div className="text-center py-8 text-gray-600 text-sm">
                                    {ta('noStoreEarnings')}
                                </div>
                            )}
                        </motion.div>
                    </div>
                </>
            )}
        </div>
    )
}

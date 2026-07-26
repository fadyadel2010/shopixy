import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useToast } from '../components/Toast'
import api from '../services/api'
import { useReactToPrint } from 'react-to-print'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'
import { formatDate, formatTime } from '../utils/dateFormat'

export default function DailyCloseReport() {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()
    const { dir, isRTL, locale } = useRTL()
    const { showError } = useToast()
    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)
    const [date, setDate] = useState(new Date().toISOString().split('T')[0])
    const printRef = useRef()

    const handlePrint = useReactToPrint({
        content: () => printRef.current,
    })

    useEffect(() => {
        fetchReport()
    }, [date])

    const fetchReport = async () => {
        setLoading(true)
        try {
            const response = await api.get('/reports/daily-close', { params: { date } })
            setData(response.data)
        } catch (error) {
            console.error('Failed to fetch daily close report:', error)
            showError(t('dailyClose.failedToLoad'))
            setData(null)
        } finally {
            setLoading(false)
        }
    }

    return (
        <div dir={dir} className="space-y-6">
            <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('dailyClose.title')}</h1>
                    <p className="text-sm text-gray-500">{t('dailyClose.subtitle')}</p>
                </div>

                <div className="flex flex-wrap gap-3">
                    <input
                        type="date"
                        value={date}
                        onChange={(e) => setDate(e.target.value)}
                        className="rounded-lg border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500"
                    />
                    <button
                        onClick={handlePrint}
                        disabled={!data}
                        className="inline-flex items-center px-4 py-2 border border-transparent rounded-lg shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50"
                    >
                        <span>🖨️</span> {t('common.print')}
                    </button>
                    <button
                        onClick={fetchReport}
                        className="inline-flex items-center px-4 py-2 border border-gray-300 rounded-lg shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                    >
                        <span>🔄</span> {t('common.refresh')}
                    </button>
                </div>
            </div>

            {loading ? (
                <div className="flex justify-center p-12">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
                </div>
            ) : data ? (
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
                    {/* The Z-Report Logic - Printable Section */}
                    <div className="bg-white p-8 rounded-xl shadow-sm border border-gray-100" ref={printRef}>
                        <div className="text-center border-b pb-4 mb-6">
                            <h2 className="text-xl font-bold uppercase tracking-wider">{t('dailyClose.zReport')}</h2>
                            <p className="text-gray-500 text-sm mt-1">{formatDate(data.date, locale)}</p>
                        </div>

                        <div className="space-y-6">
                            {/* Summary Section */}
                            <div className="bg-gray-50 p-4 rounded-lg">
                                <div className="flex justify-between items-center mb-2">
                                    <span className="text-gray-600">{t('dailyClose.totalTransactions')}</span>
                                    <span className="font-bold">{data.totalTransactions}</span>
                                </div>
                                <div className="flex justify-between items-center text-lg">
                                    <span className="font-bold text-gray-900">{t('dailyClose.totalSales')}</span>
                                    <span className="font-bold text-green-600">{formatCurrency(data.totalSales)}</span>
                                </div>
                            </div>

                            {/* Cash Drawer Reconciliation */}
                            <div className="space-y-3">
                                <h3 className="font-bold text-gray-900 uppercase text-xs border-b pb-1">{t('dailyClose.cashReconciliation')}</h3>

                                <LineItem label={t('dailyClose.openingCash')} amount={data.openingCashBalance} />
                                <LineItem label={t('dailyClose.cashSales')} amount={data.cashSales} textClass="text-green-600" />
                                <LineItem label={t('dailyClose.manualIn')} amount={data.manualCashIn} textClass="text-green-600" />
                                <LineItem label={t('dailyClose.cashReturns')} amount={-data.cashReturns} textClass="text-red-500" />
                                <LineItem label={t('dailyClose.cashExpenses')} amount={-data.cashExpenses} textClass="text-red-500" />
                                <LineItem label={t('dailyClose.cashPurchases')} amount={-data.cashPurchases} textClass="text-red-500" />
                                <LineItem label={t('dailyClose.manualOut')} amount={-data.manualCashOut} textClass="text-red-500" />

                                <div className="border-t pt-2 mt-2 flex justify-between items-center">
                                    <span className="font-bold text-gray-900">{t('dailyClose.expectedInDrawer')}</span>
                                    <span className="font-bold text-xl text-gray-900 border-2 border-gray-900 px-2 py-1 rounded">
                                        {formatCurrency(data.expectedClosingCash)}
                                    </span>
                                </div>
                            </div>

                            {/* Card/Bank Breakdown */}
                            <div className="space-y-3 pt-4">
                                <h3 className="font-bold text-gray-900 uppercase text-xs border-b pb-1">{t('dailyClose.nonCashTotals')}</h3>
                                <LineItem label={t('dailyClose.cardSales')} amount={data.cardSales} />
                                <LineItem label={t('dailyClose.cardReturns')} amount={-data.cardReturns} textClass="text-red-500" />
                            </div>

                            {/* Footer Signature Area */}
                            <div className="pt-12 mt-8 border-t border-dashed">
                                <div className="flex justify-between text-xs text-gray-400">
                                    <div className="text-center w-1/3">
                                        <div className="border-b mb-2 h-8"></div>
                                        <p>{t('dailyClose.cashierSignature')}</p>
                                    </div>
                                    <div className="text-center w-1/3">
                                        <div className="border-b mb-2 h-8"></div>
                                        <p>{t('dailyClose.managerSignature')}</p>
                                    </div>
                                </div>
                                <p className="text-center text-[10px] text-gray-300 mt-4">
                                    {t('dailyClose.systemGenerated')}
                                </p>
                            </div>
                        </div>
                    </div>

                    {/* Detailed Movements List (Right Side) */}
                    <div className="bg-white p-6 rounded-xl shadow-sm h-fit">
                        <h3 className="text-lg font-bold text-gray-900 mb-4">{t('dailyClose.movementsLog')}</h3>
                        <div className={`overflow-y-auto max-h-[600px] ${isRTL ? 'pl-2' : 'pr-2'}`}>
                            {data.movements && data.movements.length > 0 ? (
                                <div className="space-y-4">
                                    {data.movements.map((move, idx) => (
                                        <div key={idx} className="flex justify-between items-center p-3 bg-gray-50 rounded-lg hover:bg-gray-100 transition-colors">
                                            <div className="flex items-center gap-3">
                                                <div className={`w-2 h-2 rounded-full ${move.amount >= 0 ? 'bg-green-500' : 'bg-red-500'}`}></div>
                                                <div>
                                                    <p className="text-sm font-medium text-gray-900">{move.description || move.type}</p>
                                                    <p className="text-xs text-gray-500">{formatTime(move.time, locale)} • {move.reference}</p>
                                                </div>
                                            </div>
                                            <span className={`font-mono text-sm ${move.amount >= 0 ? 'text-green-600' : 'text-red-500'}`}>
                                                {formatCurrency(move.amount)}
                                            </span>
                                        </div>
                                    ))}
                                </div>
                            ) : (
                                <p className="text-gray-500 text-center py-8">{t('common.noData')}</p>
                            )}
                        </div>
                    </div>
                </div>
            ) : (
                <div className="text-center p-12 bg-white rounded-lg shadow">
                    <p className="text-gray-500">{t('common.loading')}</p>
                </div>
            )}
        </div>
    )

    function LineItem({ label, amount, textClass = 'text-gray-700' }) {
        if (!amount && amount !== 0) return null
        return (
            <div className="flex justify-between items-center text-sm">
                <span className="text-gray-600">{label}</span>
                <span className={`font-medium ${textClass}`}>{formatCurrency(amount)}</span>
            </div>
        )
    }
}

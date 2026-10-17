/**
 * Clearing Aging Report Page
 * 
 * PHASE 5: Treasury & Finance Control Center
 * 
 * Shows pending balances in clearing accounts with aging buckets.
 */

import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import * as finApi from '../../services/financialReportsApi'
import { useRTL } from '../../hooks/useRTL'
import { formatDate, formatDateInputValue, formatDateTime, parseDateInput } from '../../utils/dateFormat'
import { useAuthStore } from '../../store/authStore'
import { hasPosPermission } from '../../auth/posPermissions'

export default function TreasuryClearingAging() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const { showError, showSuccess } = useToast()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()
    const user = useAuthStore(state => state.user)

    const [loading, setLoading] = useState(true)
    const [report, setReport] = useState(null)
    const [asOfDate, setAsOfDate] = useState(new Date())

    useEffect(() => {
        loadReport()
    }, [asOfDate])

    const loadReport = async () => {
        try {
            setLoading(true)
            const data = await finApi.getClearingAging(asOfDate)
            setReport(data)
        } catch (error) {
            console.error('Failed to load clearing aging:', error)
            showError(t('treasury.loadError', 'Failed to load report'))
        } finally {
            setLoading(false)
        }
    }

    const exportToExcel = () => {
        import('xlsx').then(XLSX => {
            const data = report.lines?.map(l => ({
                [t('treasury.accountCode', 'Account Code')]: l.accountCode,
                [t('treasury.accountName', 'Account Name')]: l.accountName,
                [t('treasury.pending', 'Pending Amount')]: l.pendingAmount,
                [t('treasury.oldestDate', 'Oldest Date')]: l.oldestTransactionDate ? formatDate(l.oldestTransactionDate, locale) : '',
                [t('treasury.ageDays', 'Age (Days)')]: l.ageDays,
                [t('treasury.transactions', 'Transactions')]: l.transactionCount,
                [t('treasury.current', 'Current (0-7 days)')]: l.current,
                [t('treasury.days8To14', '8-14 Days')]: l.days8To14,
                [t('treasury.days15To30', '15-30 Days')]: l.days15To30,
                [t('treasury.over30Days', 'Over 30 Days')]: l.over30Days,
                [t('treasury.status', 'Status')]: (l.ageDays || 0) > 7 ? t('treasury.late', 'Late') : t('treasury.ok', 'OK')
            })) || []

            const ws = XLSX.utils.json_to_sheet(data)
            const wb = XLSX.utils.book_new()
            XLSX.utils.book_append_sheet(wb, ws, t('treasury.clearingAging', 'Clearing Aging').slice(0, 31))
            XLSX.writeFile(wb, `ClearingAging_${formatDateInputValue(asOfDate)}.xlsx`)
            showSuccess(t('common.exportSuccess', 'Exported successfully'))
        })
    }

    const exportToPdf = () => {
        import('jspdf').then(({ default: jsPDF }) => {
            import('jspdf-autotable').then(() => {
                const doc = new jsPDF('landscape')

                doc.setFontSize(18)
                doc.text(t('treasury.clearingAging', 'Clearing Accounts Aging Report'), 14, 22)
                doc.setFontSize(11)
                doc.text(`${t('treasury.asOf', 'As of')}: ${formatDate(asOfDate, locale)}`, 14, 32)
                doc.text(`${t('treasury.totalPending', 'Total Pending')}: ${formatCurrency(report.totalPending)}`, 14, 40)

                doc.autoTable({
                    startY: 48,
                    head: [[t('treasury.account', 'Account'), t('treasury.pending', 'Pending'), t('treasury.age', 'Age'), t('treasury.current', 'Current'), t('treasury.days8To14', '8-14 Days'), t('treasury.days15To30', '15-30 Days'), t('treasury.over30Days', '30+ Days'), t('treasury.status', 'Status')]],
                    body: report.lines?.map(l => [
                        `${l.accountCode} - ${l.accountName}`,
                        formatCurrency(l.pendingAmount),
                        `${l.ageDays || 0} ${t('common.days', 'days')}`,
                        formatCurrency(l.current || 0),
                        formatCurrency(l.days8To14 || 0),
                        formatCurrency(l.days15To30 || 0),
                        formatCurrency(l.over30Days || 0),
                        (l.ageDays || 0) > 7 ? `⚠️ ${t('treasury.late', 'Late')}` : `✓ ${t('treasury.ok', 'OK')}`
                    ]) || [],
                    theme: 'striped',
                    styles: { fontSize: 9 },
                    headStyles: { fillColor: [251, 146, 60] }
                })

                doc.save(`ClearingAging_${formatDateInputValue(asOfDate)}.pdf`)
                showSuccess(t('common.exportSuccess', 'Exported successfully'))
            })
        })
    }

    // Calculate totals by aging bucket
    const agingTotals = {
        current: report?.lines?.reduce((sum, l) => sum + (l.current || 0), 0) || 0,
        days8To14: report?.lines?.reduce((sum, l) => sum + (l.days8To14 || 0), 0) || 0,
        days15To30: report?.lines?.reduce((sum, l) => sum + (l.days15To30 || 0), 0) || 0,
        over30Days: report?.lines?.reduce((sum, l) => sum + (l.over30Days || 0), 0) || 0
    }

    // Count late accounts
    const lateAccounts = report?.lines?.filter(l => (l.ageDays || 0) > 7).length || 0

    if (loading) {
        return (
            <div className="flex items-center justify-center h-96">
                <div className="text-center">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mx-auto" aria-hidden="true"></div>
                    <p className="mt-4 text-gray-600" role="status">{t('common.loading')}</p>
                </div>
            </div>
        )
    }

    return (
        <div dir={dir} className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="financial" />

            {/* Header */}
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-4">
                    <button onClick={() => navigate('/treasury')} className="self-start text-gray-500 hover:text-gray-700">
                        <span aria-hidden="true" className="inline-block rtl:rotate-180">←</span> {t('common.back')}
                    </button>
                    <div>
                        <h2 className="text-2xl font-bold text-gray-900">
                            <span aria-hidden="true">⚠️ </span>{t('treasury.clearingAging', 'Clearing Accounts Aging')}
                        </h2>
                        <p className="text-gray-600">
                            {t('treasury.pendingSettlements', 'Money in transit pending settlement')}
                        </p>
                    </div>
                </div>

                <div className="flex w-full flex-wrap items-center gap-3 md:w-auto">
                    <label htmlFor="treasury-clearing-aging-date" className="text-sm text-gray-600">{t('treasury.asOf', 'As of')}:</label>
                    <input
                        id="treasury-clearing-aging-date"
                        type="date"
                        value={formatDateInputValue(asOfDate)}
                        onChange={(e) => {
                            const date = parseDateInput(e.target.value)
                            if (date) setAsOfDate(date)
                        }}
                        className="input min-w-0 flex-1 md:flex-none"
                    />
                    <button onClick={exportToExcel} disabled={!report?.lines?.length} className="btn-secondary flex-1 text-sm sm:flex-none">📊 Excel</button>
                    <button onClick={exportToPdf} disabled={!report?.lines?.length} className="btn-secondary flex-1 text-sm sm:flex-none">📄 PDF</button>
                </div>
            </div>

            {/* Evidence boundary: clearing is read-only money in transit. */}
            <div className="p-4 rounded-lg bg-slate-50 border border-slate-200" role="note">
                {hasPosPermission(user, 'storefront.payments.manage') && <button className="btn-primary mb-3" onClick={() => navigate('/treasury/manual-cod-settlements')}>
                    {t('manualCod.title')}
                </button>}
                {hasPosPermission(user, 'storefront.payments.manage') && hasPosPermission(user, 'finance.operate') &&
                    <button className="btn-primary mb-3 ms-3" onClick={() => navigate('/treasury/carrier-remittances')}>{t('carrierRemittance.title')}</button>}
                <p className="text-sm text-slate-700">
                    <strong>{t('treasury.inTransit', 'In transit')}: </strong>
                     {t('treasury.clearingEvidenceBoundary', 'Clearing balances settle only through authenticated provider or carrier evidence in the payment lifecycle. Manual transfer or settlement from this screen is not possible.')}
                 </p>
                 <p className="text-sm text-slate-700 mt-2">
                     {t('treasury.clearingSettlementUnavailable', 'COD and gateway clearing settlement is currently unavailable because authenticated carrier remittance or provider payout evidence is not integrated. Clearing balances cannot be moved through Treasury Transfers.')}
                 </p>
            </div>

            {/* Status Banner */}
            <div role="status" className={`p-4 rounded-lg ${lateAccounts === 0
                ? 'bg-green-50 border border-green-200'
                : 'bg-amber-50 border border-amber-200'
                }`}>
                <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                    <div className="flex items-center gap-3">
                        <span className="text-3xl" aria-hidden="true">{lateAccounts === 0 ? '✅' : '⚠️'}</span>
                        <div>
                            <p className={`font-semibold ${lateAccounts === 0 ? 'text-green-800' : 'text-amber-800'}`}>
                                {lateAccounts === 0
                                    ? t('treasury.allOnTime', 'All Settlements On Time')
                                    : t('treasury.accountsLateCount', '{{count}} account is late', { count: lateAccounts })}
                            </p>
                            <p className="text-sm text-gray-600">
                                {report?.accountsWithBalance || 0} {t('treasury.accountsWithPending', 'accounts with pending balances')}
                            </p>
                        </div>
                    </div>
                    <div className="text-end">
                        <p className="text-sm text-gray-500">{t('treasury.totalPending', 'Total Pending')}</p>
                        <p className="text-2xl font-bold text-amber-700">
                            {formatCurrency(report?.totalPending || 0)}
                        </p>
                    </div>
                </div>
            </div>

            {/* Aging Buckets Summary */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                <div className="card bg-green-50 border-green-200">
                    <p className="text-sm text-green-600">{t('treasury.current', 'Current (0-7 days)')}</p>
                    <p className="text-2xl font-bold text-green-900">{formatCurrency(agingTotals.current)}</p>
                </div>
                <div className="card bg-yellow-50 border-yellow-200">
                    <p className="text-sm text-yellow-600">{t('treasury.days8To14', '8-14 Days')}</p>
                    <p className="text-2xl font-bold text-yellow-900">{formatCurrency(agingTotals.days8To14)}</p>
                </div>
                <div className="card bg-orange-50 border-orange-200">
                    <p className="text-sm text-orange-600">{t('treasury.days15To30', '15-30 Days')}</p>
                    <p className="text-2xl font-bold text-orange-900">{formatCurrency(agingTotals.days15To30)}</p>
                </div>
                <div className="card bg-red-50 border-red-200">
                    <p className="text-sm text-red-600">{t('treasury.over30Days', 'Over 30 Days')}</p>
                    <p className="text-2xl font-bold text-red-900">{formatCurrency(agingTotals.over30Days)}</p>
                </div>
            </div>

            {/* Clearing Accounts Table */}
            <div className="card overflow-hidden">
                {report?.lines?.length > 0 ? (
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-amber-50">
                                <tr>
                                    <th scope="col" className="px-4 py-3 text-start text-xs font-medium text-gray-700 uppercase">
                                        {t('treasury.account', 'Account')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-gray-700 uppercase">
                                        {t('treasury.pending', 'Pending')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-center text-xs font-medium text-gray-700 uppercase">
                                        {t('treasury.oldestAge', 'Oldest Age')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-center text-xs font-medium text-gray-700 uppercase">
                                        {t('treasury.transactions', 'Txns')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-green-700 uppercase bg-green-50">
                                        {t('treasury.current', '0-7 Days')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-yellow-700 uppercase bg-yellow-50">
                                        {t('treasury.days8To14', '8-14 Days')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-orange-700 uppercase bg-orange-50">
                                        {t('treasury.days15To30', '15-30 Days')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-red-700 uppercase bg-red-50">
                                        {t('treasury.over30Days', '30+ Days')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-center text-xs font-medium text-gray-700 uppercase">
                                        {t('treasury.status', 'Status')}
                                    </th>
                                    <th scope="col" className="px-4 py-3 text-center text-xs font-medium text-gray-700 uppercase">
                                        {t('treasury.actions', 'Actions')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {report.lines.map((line) => {
                                    const isLate = (line.ageDays || 0) > 7
                                    return (
                                        <tr
                                            key={line.accountId}
                                            className={`hover:bg-gray-50 ${isLate ? 'bg-red-50/50' : ''}`}
                                        >
                                            <td className="px-4 py-3">
                                                <button type="button" className="text-start font-medium text-gray-900 hover:underline" onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>{line.accountName}</button>
                                                <div className="text-xs text-gray-500 font-mono"><bdi>{line.accountCode}</bdi></div>
                                            </td>
                                            <td className="px-4 py-3 text-end font-semibold text-amber-700">
                                                {formatCurrency(line.pendingAmount)}
                                            </td>
                                            <td className="px-4 py-3 text-center">
                                                <span className={`font-medium ${isLate ? 'text-red-600' : 'text-gray-700'}`}>
                                                    {line.ageDays || 0} {t('common.days', 'days')}
                                                </span>
                                                {line.oldestTransactionDate && (
                                                    <div className="text-xs text-gray-400">
                                                        {formatDate(line.oldestTransactionDate, locale)}
                                                    </div>
                                                )}
                                            </td>
                                            <td className="px-4 py-3 text-center text-gray-600">
                                                {line.transactionCount || 0}
                                            </td>
                                            <td className="px-4 py-3 text-end text-sm text-green-700 bg-green-50/50">
                                                {(line.current || 0) > 0 ? formatCurrency(line.current) : '-'}
                                            </td>
                                            <td className="px-4 py-3 text-end text-sm text-yellow-700 bg-yellow-50/50">
                                                {(line.days8To14 || 0) > 0 ? formatCurrency(line.days8To14) : '-'}
                                            </td>
                                            <td className="px-4 py-3 text-end text-sm text-orange-700 bg-orange-50/50">
                                                {(line.days15To30 || 0) > 0 ? formatCurrency(line.days15To30) : '-'}
                                            </td>
                                            <td className="px-4 py-3 text-end text-sm text-red-700 bg-red-50/50">
                                                {(line.over30Days || 0) > 0 ? formatCurrency(line.over30Days) : '-'}
                                            </td>
                                            <td className="px-4 py-3 text-center">
                                                {isLate ? (
                                                    <span className="inline-flex px-2 py-1 text-xs font-semibold rounded-full bg-red-100 text-red-800">
                                                        🔴 {t('treasury.late', 'LATE')}
                                                    </span>
                                                ) : (
                                                    <span className="inline-flex px-2 py-1 text-xs font-semibold rounded-full bg-green-100 text-green-800">
                                                        🟢 {t('treasury.ok', 'OK')}
                                                    </span>
                                                )}
                                            </td>
                                            <td className="px-4 py-3 text-center">
                                                <button
                                                    type="button"
                                                    disabled
                                                    className="px-3 py-1.5 bg-gray-200 text-gray-600 text-xs font-medium rounded cursor-not-allowed"
                                                    title={t(
                                                        'treasury.settlementRequiresEvidence',
                                                        'Settlement is recorded from provider or carrier evidence linked to eligible orders.'
                                                    )}
                                                >
                                                    {t('treasury.awaitingProviderSettlement', 'Provider workflow')}
                                                </button>
                                            </td>
                                        </tr>
                                    )
                                })}
                            </tbody>
                            <tfoot className="bg-gray-100">
                                <tr className="font-bold">
                                    <td className="px-4 py-3 text-gray-900">
                                        {t('common.totals', 'TOTALS')}
                                    </td>
                                    <td className="px-4 py-3 text-end text-amber-800">
                                        {formatCurrency(report.totalPending)}
                                    </td>
                                    <td className="px-4 py-3 text-center text-gray-600">
                                        {report.oldestAgeDays || 0} {t('common.days', 'days')}
                                    </td>
                                    <td className="px-4 py-3"></td>
                                    <td className="px-4 py-3 text-end text-green-800 bg-green-50">
                                        {formatCurrency(agingTotals.current)}
                                    </td>
                                    <td className="px-4 py-3 text-end text-yellow-800 bg-yellow-50">
                                        {formatCurrency(agingTotals.days8To14)}
                                    </td>
                                    <td className="px-4 py-3 text-end text-orange-800 bg-orange-50">
                                        {formatCurrency(agingTotals.days15To30)}
                                    </td>
                                    <td className="px-4 py-3 text-end text-red-800 bg-red-50">
                                        {formatCurrency(agingTotals.over30Days)}
                                    </td>
                                    <td></td>
                                    <td></td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>
                ) : (
                    <div className="flex flex-col items-center justify-center py-16">
                        <span className="text-6xl mb-4" aria-hidden="true">✨</span>
                        <h3 className="text-xl font-semibold text-green-700">
                            {t('treasury.allSettled', 'All Cleared!')}
                        </h3>
                        <p className="text-gray-500 mt-2">
                            {t('treasury.noPendingClearing', 'No pending balances in clearing accounts')}
                        </p>
                    </div>
                )}
            </div>

            {/* Risk Legend */}
            <div className="card bg-gray-50">
                <h4 className="text-sm font-semibold text-gray-700 mb-3">
                    {t('treasury.settlementRules', 'Settlement Risk Rules')}
                </h4>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                    <div className="flex items-center gap-2">
                        <span className="w-4 h-4 rounded-full bg-green-500"></span>
                        <span>{t('treasury.current', '0-7 days')}: {t('treasury.normalSettlement', 'Normal')}</span>
                    </div>
                    <div className="flex items-center gap-2">
                        <span className="w-4 h-4 rounded-full bg-yellow-500"></span>
                        <span>{t('treasury.days8To14', '8-14 days')}: {t('treasury.monitorSettlement', 'Monitor')}</span>
                    </div>
                    <div className="flex items-center gap-2">
                        <span className="w-4 h-4 rounded-full bg-orange-500"></span>
                        <span>{t('treasury.days15To30', '15-30 days')}: {t('treasury.escalateSettlement', 'Escalate')}</span>
                    </div>
                    <div className="flex items-center gap-2">
                        <span className="w-4 h-4 rounded-full bg-red-500"></span>
                        <span>{t('treasury.over30Days', '30+ days')}: {t('treasury.criticalSettlement', 'Critical')}</span>
                    </div>
                </div>
            </div>

            {/* Footer */}
            <div className="text-center py-4 text-sm text-gray-500">
                {t('treasury.ledgerSourced', 'Sourced from immutable ledger')} •
                {t('treasury.generated', 'Generated')}: {formatDateTime(new Date(), locale)}
            </div>
        </div>
    )
}

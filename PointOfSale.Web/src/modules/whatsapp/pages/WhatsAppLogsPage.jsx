import React, { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { RefreshCw } from 'lucide-react'
import { useWhatsAppAnalytics } from '../hooks/useWhatsAppAnalytics'
import { useToast } from '../../../components/Toast'
import MessageDetailsModal from '../components/MessageDetailsModal'
import { getWhatsAppMessageStatusKey, getWhatsAppTemplateTypeKey } from '../i18n'

export default function WhatsAppLogsPage() {
    const { t } = useTranslation()
    const { getLogs, loading } = useWhatsAppAnalytics()
    const { showError } = useToast()

    // State
    const [logs, setLogs] = useState([])
    const [filters, setFilters] = useState({ status: '', type: '', dateFrom: '', dateTo: '' })
    const [page, setPage] = useState(1)
    const [pagination, setPagination] = useState({ totalPages: 1, totalCount: 0 })
    const [selectedMessage, setSelectedMessage] = useState(null)

    const fetchLogs = async () => {
        try {
            const data = await getLogs(filters, page)
            setLogs(data.items || [])
            setPagination({
                totalPages: data.totalPages,
                totalCount: data.totalCount
            })
        } catch (err) {
            showError(err.message)
        }
    }

    useEffect(() => {
        fetchLogs()
    }, [page, filters]) // Auto-fetch on filter/page change

    const handleFilterChange = (e) => {
        const { name, value } = e.target
        setFilters(prev => ({ ...prev, [name]: value }))
        setPage(1) // Reset to first page
    }

    return (
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
            <div className="sm:flex sm:items-center justify-between mb-6">
                <div>
                    <h1 className="text-2xl font-semibold text-gray-900">{t('whatsapp.logs.title')}</h1>
                    <p className="mt-2 text-sm text-gray-700">
                        {t('whatsapp.logs.subtitle')}
                    </p>
                </div>
                <button
                    onClick={fetchLogs}
                    className="inline-flex items-center p-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none"
                    title={t('whatsapp.logs.refresh')}
                >
                    <RefreshCw className={`h-5 w-5 ${loading ? 'animate-spin' : ''}`} />
                </button>
            </div>

            {/* Filters */}
            <div className="bg-white shadow rounded-lg p-4 mb-6 grid grid-cols-1 gap-4 sm:grid-cols-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700">{t('common.status')}</label>
                    <select
                        name="status"
                        value={filters.status}
                        onChange={handleFilterChange}
                        className="mt-1 block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
                    >
                        <option value="">{t('whatsapp.logs.filters.allStatuses')}</option>
                        <option value="Sent">{t('whatsapp.logs.statuses.sent')}</option>
                        <option value="Failed">{t('whatsapp.logs.statuses.failed')}</option>
                        <option value="Delivered">{t('whatsapp.logs.statuses.delivered')}</option>
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700">{t('whatsapp.logs.columns.type')}</label>
                    <select
                        name="type"
                        value={filters.type}
                        onChange={handleFilterChange}
                        className="mt-1 block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
                    >
                        <option value="">{t('whatsapp.logs.filters.allTypes')}</option>
                        <option value="OrderConfirmation">{t('whatsapp.types.orderConfirmation')}</option>
                        <option value="Otp">{t('whatsapp.types.otp')}</option>
                    </select>
                </div>
                {/* TODO: Add Date Picker Integration */}
            </div>

            {/* Table */}
            <div className="flex flex-col">
                <div className="-my-2 overflow-x-auto sm:-mx-6 lg:-mx-8">
                    <div className="py-2 align-middle inline-block min-w-full sm:px-6 lg:px-8">
                        <div className="shadow overflow-hidden border-b border-gray-200 sm:rounded-lg">
                            <table className="min-w-full divide-y divide-gray-200">
                                <thead className="bg-gray-50">
                                    <tr>
                                        <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.date')}</th>
                                        <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('whatsapp.logs.columns.recipient')}</th>
                                        <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('whatsapp.logs.columns.type')}</th>
                                        <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.status')}</th>
                                        <th scope="col" className="relative px-6 py-3">
                                            <span className="sr-only">{t('common.view')}</span>
                                        </th>
                                    </tr>
                                </thead>
                                <tbody className="bg-white divide-y divide-gray-200">
                                    {logs.map((log) => (
                                        <tr key={log.id}>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                {new Date(log.createdAtUtc).toLocaleString()}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {log.phoneNumber}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                {t(getWhatsAppTemplateTypeKey(log.type) || '') || log.type}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full 
                                                    ${log.status === 'Sent' ? 'bg-green-100 text-green-800' :
                                                        log.status === 'Failed' ? 'bg-red-100 text-red-800' : 'bg-gray-100 text-gray-800'}`}>
                                                    {t(getWhatsAppMessageStatusKey(log.status) || '') || log.status}
                                                </span>
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                                                <button
                                                    onClick={() => setSelectedMessage(log)}
                                                    className="text-indigo-600 hover:text-indigo-900"
                                                >
                                                    {t('common.view')}
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                    {logs.length === 0 && (
                                        <tr>
                                            <td colSpan="5" className="px-6 py-4 text-center text-sm text-gray-500">
                                                {t('whatsapp.logs.empty')}
                                            </td>
                                        </tr>
                                    )}
                                </tbody>
                            </table>
                            {/* Pagination */}
                            {pagination.totalPages > 1 && (
                                <div className="bg-white px-4 py-3 flex items-center justify-between border-t border-gray-200 sm:px-6">
                                    <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                                        <div>
                                            <p className="text-sm text-gray-700">
                                                {t('whatsapp.logs.pageOf', { page, totalPages: pagination.totalPages })}
                                            </p>
                                        </div>
                                        <div>
                                            <nav className="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
                                                <button
                                                    onClick={() => setPage(p => Math.max(1, p - 1))}
                                                    disabled={page === 1}
                                                    className="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50"
                                                >
                                                    {t('common.previous')}
                                                </button>
                                                <button
                                                    onClick={() => setPage(p => Math.min(pagination.totalPages, p + 1))}
                                                    disabled={page === pagination.totalPages}
                                                    className="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50"
                                                >
                                                    {t('common.next')}
                                                </button>
                                            </nav>
                                        </div>
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            </div>

            {/* details Modal */}
            {selectedMessage && (
                <MessageDetailsModal
                    message={selectedMessage}
                    onClose={() => setSelectedMessage(null)}
                />
            )}
        </div>
    )
}

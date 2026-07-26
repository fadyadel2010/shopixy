import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { getHeldSales, removeHeldSale } from '../utils/localStorage'

export default function HoldSalesModal({ onRetrieve, onClose }) {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const [heldSales, setHeldSales] = useState([])
    const [search, setSearch] = useState('')

    useEffect(() => {
        loadHeldSales()
    }, [])

    const loadHeldSales = () => {
        const sales = getHeldSales()
        setHeldSales(sales)
    }

    const handleRetrieve = (sale) => {
        onRetrieve(sale)
        removeHeldSale(sale.id)
        onClose()
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: 'Delete this held sale?',
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) {
            return
        }

        removeHeldSale(id)
        loadHeldSales()
    }

    const filteredSales = heldSales.filter(sale => {
        if (!search) return true
        const searchLower = search.toLowerCase()
        return (
            sale.referenceNumber.toLowerCase().includes(searchLower) ||
            (sale.customer?.name || '').toLowerCase().includes(searchLower)
        )
    })

    const formatTime = (dateString) => {
        const date = new Date(dateString)
        const now = new Date()
        const diffMinutes = Math.floor((now - date) / (1000 * 60))

        if (diffMinutes < 60) {
            return `${diffMinutes} min ago`
        } else if (diffMinutes < 1440) {
            const hours = Math.floor(diffMinutes / 60)
            return `${hours} hour${hours > 1 ? 's' : ''} ago`
        } else {
            return date.toLocaleDateString() + ' ' + date.toLocaleTimeString()
        }
    }

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-lg shadow-xl max-w-3xl w-full max-h-[85vh] overflow-hidden flex flex-col">
                <div className="p-6 flex-1 overflow-y-auto">
                    {/* Header */}
                    <div className="flex justify-between items-start mb-6">
                        <div>
                            <h3 className="text-2xl font-bold text-gray-900">Held Sales</h3>
                            <p className="text-sm text-gray-500 mt-1">
                                {heldSales.length} sale{heldSales.length !== 1 ? 's' : ''} on hold • Press F5 or Esc to close
                            </p>
                        </div>
                        <button
                            onClick={onClose}
                            className="text-gray-400 hover:text-gray-600"
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {/* Search */}
                    {heldSales.length > 0 && (
                        <div className="mb-4">
                            <input
                                type="text"
                                placeholder="Search by reference or customer name..."
                                value={search}
                                onChange={(e) => setSearch(e.target.value)}
                                className="input w-full"
                                autoFocus
                            />
                        </div>
                    )}

                    {/* Held Sales List */}
                    {filteredSales.length === 0 ? (
                        <div className="flex flex-col items-center justify-center py-12 text-gray-500">
                            <div className="text-6xl mb-4">⏸️</div>
                            <div className="text-xl font-medium">
                                {heldSales.length === 0 ? 'No held sales' : 'No matching sales'}
                            </div>
                            <div className="text-sm mt-2">
                                {heldSales.length === 0
                                    ? 'Press F4 to hold the current sale'
                                    : 'Try a different search term'}
                            </div>
                        </div>
                    ) : (
                        <div className="space-y-3">
                            {filteredSales.map((sale) => (
                                <div
                                    key={sale.id}
                                    className="border-2 border-gray-200 rounded-lg p-4 hover:border-primary-500 transition-colors"
                                >
                                    <div className="flex justify-between items-start mb-3">
                                        <div>
                                            <div className="font-bold text-lg text-gray-900">
                                                {sale.referenceNumber}
                                            </div>
                                            <div className="text-sm text-gray-500">
                                                {formatTime(sale.heldAt)}
                                            </div>
                                        </div>
                                        <div className="text-right">
                                            <div className="text-2xl font-bold text-primary-600">
                                                ${sale.total.toFixed(2)}
                                            </div>
                                            <div className="text-sm text-gray-500">
                                                {sale.itemCount} item{sale.itemCount !== 1 ? 's' : ''}
                                            </div>
                                        </div>
                                    </div>

                                    {sale.customer && (
                                        <div className="mb-3 text-sm">
                                            <span className="text-gray-600">Customer:</span>{' '}
                                            <span className="font-medium">{sale.customer.name}</span>
                                        </div>
                                    )}

                                    {/* Item Preview */}
                                    <div className="mb-3 text-sm text-gray-600">
                                        {sale.items.slice(0, 3).map((item, idx) => (
                                            <div key={idx}>
                                                • {item.name} x{item.quantity}
                                            </div>
                                        ))}
                                        {sale.items.length > 3 && (
                                            <div className="text-gray-500">
                                                + {sale.items.length - 3} more item{sale.items.length - 3 !== 1 ? 's' : ''}
                                            </div>
                                        )}
                                    </div>

                                    {/* Actions */}
                                    <div className="flex gap-2">
                                        <button
                                            onClick={() => handleRetrieve(sale)}
                                            className="flex-1 btn-primary"
                                        >
                                            ▶️ Retrieve Sale
                                        </button>
                                        <button
                                            onClick={() => handleDelete(sale.id)}
                                            className="btn-secondary px-4"
                                        >
                                            🗑️
                                        </button>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}

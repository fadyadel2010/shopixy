import {
    AlertCircle,
    ArrowDown,
    ArrowUp,
    ArrowUpDown,
    ChevronRight,
    Clock3,
    Package2,
} from 'lucide-react'
import {
    isFailedPaymentStatus,
    isPendingOrderStatus,
    type SuperAdminOrderListItem,
    type SuperAdminOrdersSortField,
    type SuperAdminSortDirection,
} from './ordersService'

interface OrdersTableProps {
    orders: SuperAdminOrderListItem[]
    isLoading: boolean
    isFetching: boolean
    selectedOrderId?: string | null
    sortBy: SuperAdminOrdersSortField
    sortDirection: SuperAdminSortDirection
    onSortChange: (field: SuperAdminOrdersSortField) => void
    onSelectOrder: (orderId: string) => void
}

const STUCK_THRESHOLD_MINUTES = 120

const tableColumns: Array<{ key: SuperAdminOrdersSortField; label: string }> = [
    { key: 'OrderNumber', label: 'Order ID' },
    { key: 'Customer', label: 'Customer' },
    { key: 'Tenant', label: 'Tenant' },
    { key: 'VendorCount', label: 'Vendors' },
    { key: 'TotalAmount', label: 'Total Amount' },
    { key: 'Status', label: 'Status' },
    { key: 'PaymentStatus', label: 'Payment Status' },
    { key: 'CreatedAt', label: 'Created At' },
]

function formatCurrency(amount: number, currency: string): string {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: currency || 'EGP',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(amount)
}

function formatDateTime(value: string): string {
    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return 'Unavailable'
    }

    return parsed.toLocaleString()
}

function truncateId(value: string): string {
    return value.length <= 12 ? value : `${value.slice(0, 8)}...${value.slice(-4)}`
}

function getOrderStatusTone(status: string): string {
    switch (status) {
        case 'Completed':
        case 'Delivered':
            return 'border-emerald-200 bg-emerald-50 text-emerald-900'
        case 'Pending':
        case 'OnHold':
            return 'border-amber-200 bg-amber-50 text-amber-900'
        case 'Cancelled':
        case 'Refunded':
            return 'border-rose-200 bg-rose-50 text-rose-900'
        default:
            return 'border-sky-200 bg-sky-50 text-sky-900'
    }
}

function getPaymentStatusTone(status: string): string {
    switch (status) {
        case 'Failed':
        case 'Rejected':
            return 'border-rose-200 bg-rose-50 text-rose-900'
        case 'Collected':
        case 'Paid':
        case 'Settled':
            return 'border-emerald-200 bg-emerald-50 text-emerald-900'
        case 'Refunded':
        case 'PartiallyRefunded':
            return 'border-slate-200 bg-slate-100 text-slate-800'
        default:
            return 'border-amber-200 bg-amber-50 text-amber-900'
    }
}

function getRelativeAge(value: string): {
    label: string
    minutesOld: number | null
    isStuck: boolean
} {
    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return {
            label: 'Awaiting sync',
            minutesOld: null,
            isStuck: false,
        }
    }

    const diffMs = Math.max(0, Date.now() - parsed.getTime())
    const minutesOld = Math.floor(diffMs / 60_000)

    if (minutesOld < 1) {
        return { label: 'Created just now', minutesOld, isStuck: false }
    }

    if (minutesOld < 60) {
        return {
            label: `Created ${minutesOld} minute${minutesOld === 1 ? '' : 's'} ago`,
            minutesOld,
            isStuck: minutesOld >= STUCK_THRESHOLD_MINUTES,
        }
    }

    const hoursOld = Math.floor(minutesOld / 60)
    if (hoursOld < 24) {
        return {
            label: `Created ${hoursOld} hour${hoursOld === 1 ? '' : 's'} ago`,
            minutesOld,
            isStuck: minutesOld >= STUCK_THRESHOLD_MINUTES,
        }
    }

    const daysOld = Math.floor(hoursOld / 24)
    return {
        label: `Created ${daysOld} day${daysOld === 1 ? '' : 's'} ago`,
        minutesOld,
        isStuck: minutesOld >= STUCK_THRESHOLD_MINUTES,
    }
}

function TableSkeleton() {
    return (
        <tbody className="divide-y divide-slate-100">
            {Array.from({ length: 8 }).map((_, index) => (
                <tr key={`orders-skeleton-${index}`} className="animate-pulse">
                    <td className="px-5 py-4">
                        <div className="h-4 w-28 rounded-full bg-slate-200" />
                        <div className="mt-2 h-3 w-36 rounded-full bg-slate-100" />
                    </td>
                    {Array.from({ length: 7 }).map((__, cellIndex) => (
                        <td key={`orders-skeleton-${index}-${cellIndex}`} className="px-5 py-4">
                            <div className="h-4 w-24 rounded-full bg-slate-100" />
                        </td>
                    ))}
                    <td className="px-5 py-4">
                        <div className="h-4 w-32 rounded-full bg-slate-100" />
                    </td>
                </tr>
            ))}
        </tbody>
    )
}

function SortButton({
    label,
    active,
    direction,
    onClick,
}: {
    label: string
    active: boolean
    direction: SuperAdminSortDirection
    onClick: () => void
}) {
    return (
        <button
            type="button"
            className="inline-flex items-center gap-2 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500 transition hover:text-slate-900"
            onClick={onClick}
        >
            <span>{label}</span>
            {active ? (
                direction === 'Asc' ? <ArrowUp className="h-4 w-4" /> : <ArrowDown className="h-4 w-4" />
            ) : (
                <ArrowUpDown className="h-4 w-4" />
            )}
        </button>
    )
}

export default function OrdersTable({
    orders,
    isLoading,
    isFetching,
    selectedOrderId,
    sortBy,
    sortDirection,
    onSortChange,
    onSelectOrder,
}: OrdersTableProps) {
    return (
        <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
            <div className="flex flex-col gap-3 border-b border-slate-100 px-6 py-5 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                        Orders Table
                    </p>
                    <h2 className="mt-2 text-2xl font-semibold text-slate-950">
                        Platform-wide CommerceHub orders
                    </h2>
                </div>

                <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-medium text-slate-700">
                    <Package2 className="h-4 w-4" />
                    {isFetching && !isLoading ? 'Refreshing current results' : `${orders.length} rows loaded`}
                </div>
            </div>

            <div className="overflow-x-auto">
                <table className="min-w-[1240px] w-full divide-y divide-slate-100">
                    <thead className="bg-slate-50/90">
                        <tr>
                            {tableColumns.map((column) => (
                                <th key={column.key} className="px-5 py-4 text-left">
                                    <SortButton
                                        label={column.label}
                                        active={sortBy === column.key}
                                        direction={sortDirection}
                                        onClick={() => onSortChange(column.key)}
                                    />
                                </th>
                            ))}
                            <th className="px-5 py-4 text-right text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                Inspect
                            </th>
                        </tr>
                    </thead>

                    {isLoading ? <TableSkeleton /> : null}

                    {!isLoading ? (
                        <tbody className="divide-y divide-slate-100">
                            {orders.length === 0 ? (
                                <tr>
                                    <td colSpan={tableColumns.length + 1} className="px-6 py-16 text-center">
                                        <div className="mx-auto max-w-md">
                                            <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100 text-slate-500">
                                                <Package2 className="h-6 w-6" />
                                            </div>
                                            <h3 className="mt-5 text-xl font-semibold text-slate-950">
                                                No orders matched these filters
                                            </h3>
                                            <p className="mt-3 text-sm leading-6 text-slate-500">
                                                Adjust the search, payment filter, or date range to broaden the operational view.
                                            </p>
                                        </div>
                                    </td>
                                </tr>
                            ) : (
                                orders.map((order) => {
                                    const hasFailedPayment = isFailedPaymentStatus(order.paymentStatus)
                                    const isPending = isPendingOrderStatus(order.status)
                                    const age = getRelativeAge(order.createdAt)
                                    const isStuckPending = isPending && age.isStuck

                                    const rowTone = hasFailedPayment
                                        ? 'bg-rose-50/65 hover:bg-rose-50'
                                        : isStuckPending
                                            ? 'bg-amber-100/70 hover:bg-amber-100/80'
                                            : isPending
                                                ? 'bg-amber-50/65 hover:bg-amber-50'
                                                : 'bg-white hover:bg-slate-50/80'

                                    const isSelected = selectedOrderId === order.orderId

                                    return (
                                        <tr
                                            key={order.orderId}
                                            tabIndex={0}
                                            className={[
                                                'cursor-pointer transition focus:outline-none focus:ring-2 focus:ring-emerald-200 focus:ring-inset',
                                                rowTone,
                                                isSelected ? 'ring-2 ring-emerald-200 ring-inset' : '',
                                            ].join(' ')}
                                            onClick={() => onSelectOrder(order.orderId)}
                                            onKeyDown={(event) => {
                                                if (event.key === 'Enter' || event.key === ' ') {
                                                    event.preventDefault()
                                                    onSelectOrder(order.orderId)
                                                }
                                            }}
                                        >
                                            <td className="px-5 py-4 align-top">
                                                <div className="space-y-2">
                                                    <div className="text-sm font-semibold text-slate-950">
                                                        {order.orderNumber}
                                                    </div>
                                                    <div className="font-mono text-xs text-slate-500">
                                                        {truncateId(order.orderId)}
                                                    </div>

                                                    <div className="flex flex-wrap gap-2">
                                                        {hasFailedPayment ? (
                                                            <div className="inline-flex items-center gap-1 rounded-full border border-rose-200 bg-rose-100 px-2 py-1 text-[11px] font-semibold text-rose-900">
                                                                <AlertCircle className="h-3.5 w-3.5" />
                                                                Failed payment
                                                            </div>
                                                        ) : null}

                                                        {isStuckPending ? (
                                                            <div className="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-200 px-2 py-1 text-[11px] font-semibold text-amber-950">
                                                                <Clock3 className="h-3.5 w-3.5" />
                                                                Stuck 2h+
                                                            </div>
                                                        ) : null}

                                                        {isPending && !isStuckPending ? (
                                                            <div className="inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-100 px-2 py-1 text-[11px] font-semibold text-amber-900">
                                                                <Clock3 className="h-3.5 w-3.5" />
                                                                Pending review
                                                            </div>
                                                        ) : null}
                                                    </div>
                                                </div>
                                            </td>

                                            <td className="px-5 py-4 align-top">
                                                <div className="text-sm font-medium text-slate-900">
                                                    {order.customerName || 'Unknown customer'}
                                                </div>
                                            </td>

                                            <td className="px-5 py-4 align-top">
                                                <div className="text-sm font-medium text-slate-900">
                                                    {order.tenantName}
                                                </div>
                                                <div className="mt-1 text-xs text-slate-500">
                                                    {order.subOrders.slice(0, 2).map((subOrder) => subOrder.vendorName).join(', ') || 'No vendor summary'}
                                                </div>
                                            </td>

                                            <td className="px-5 py-4 align-top">
                                                <div className="text-sm font-semibold text-slate-900">
                                                    {order.vendorCount} {order.vendorCount === 1 ? 'vendor' : 'vendors'}
                                                </div>
                                                <div className="mt-1 text-xs text-slate-500">
                                                    {order.vendorCount > 1 ? 'Multi-vendor split order' : 'Single vendor'}
                                                </div>
                                            </td>

                                            <td className="px-5 py-4 align-top">
                                                <div className="text-sm font-semibold text-slate-950">
                                                    {formatCurrency(order.totalAmount, order.currency)}
                                                </div>
                                            </td>

                                            <td className="px-5 py-4 align-top">
                                                <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${getOrderStatusTone(order.status)}`}>
                                                    {order.status}
                                                </span>
                                            </td>

                                            <td className="px-5 py-4 align-top">
                                                <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${getPaymentStatusTone(order.paymentStatus)}`}>
                                                    {order.paymentStatus}
                                                </span>
                                            </td>

                                            <td className="px-5 py-4 align-top">
                                                <div className="text-sm font-medium text-slate-900">{age.label}</div>
                                                <div className="mt-1 text-xs text-slate-500">
                                                    {formatDateTime(order.createdAt)}
                                                </div>
                                            </td>

                                            <td className="px-5 py-4 align-top text-right">
                                                <span className="inline-flex items-center gap-2 text-sm font-semibold text-emerald-700">
                                                    Details
                                                    <ChevronRight className="h-4 w-4" />
                                                </span>
                                            </td>
                                        </tr>
                                    )
                                })
                            )}
                        </tbody>
                    ) : null}
                </table>
            </div>
        </section>
    )
}

/**
 * Order History Section
 * Phase F2.Orders: Customer Order History
 * 
 * Displays customer's order history in the account page.
 */

import React, { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useAuthStore, selectIsAuthenticated } from '@/auth/store/authStore';
import { getOrders, type CustomerOrder, type CustomerOrdersResponse } from '@/auth/api/accountApi';
import { formatMoney, resolveProductImage } from '@/utils';
import { ORDER_STATUS_CONFIG, PAYMENT_STATUS_CONFIG } from '@/types/order';

// Local date formatter
const formatDate = (dateString: string, locale: string): string => {
    try {
        const date = new Date(dateString);
        return date.toLocaleDateString(locale, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
        });
    } catch {
        return dateString;
    }
};

interface OrderCardProps {
    order: CustomerOrder;
}

const OrderCard: React.FC<OrderCardProps> = ({ order }) => {
    const { t, language } = useTranslation();

    const statusConfig = ORDER_STATUS_CONFIG[order.status] || ORDER_STATUS_CONFIG.Pending;
    const paymentConfig = PAYMENT_STATUS_CONFIG[order.paymentStatus] || PAYMENT_STATUS_CONFIG.Pending;

    // Format date based on language
    const formattedDate = formatDate(order.createdAt, language === 'ar' ? 'ar-EG' : 'en-US');

    return (
        <div className="bg-background border border-muted/10 rounded-2xl overflow-hidden hover:shadow-lg hover:border-primary/20 transition-all duration-300 group">
            {/* Order Header - Gradient Background */}
            <div className="bg-gradient-to-r from-primary/5 via-primary/3 to-transparent px-5 py-4 border-b border-muted/10">
                <div className="flex items-center justify-between flex-wrap gap-3">
                    <div className="flex items-center gap-4">
                        {/* Order Icon */}
                        <div className="w-10 h-10 bg-primary/10 rounded-xl flex items-center justify-center">
                            <svg className="w-5 h-5 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                    d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
                            </svg>
                        </div>
                        <div>
                            <p className="text-xs text-muted uppercase tracking-wider mb-0.5">
                                {t('account.orders.orderNumber')}
                            </p>
                            <p className="font-mono font-bold text-lg text-text">
                                {order.orderNumber}
                            </p>
                        </div>
                    </div>

                    {/* Status Badge */}
                    <div className={`inline-flex items-center gap-1.5 px-4 py-2 rounded-full text-sm font-semibold ${statusConfig.bgColor} ${statusConfig.color} shadow-sm`}>
                        <span className="text-base">{statusConfig.icon}</span>
                        <span>{t(`account.orders.status.${order.status.toLowerCase()}`, { defaultValue: order.status })}</span>
                    </div>
                </div>
            </div>

            {/* Order Content */}
            <div className="p-5">
                {/* Cancellation Reason Alert */}
                {order.status.toLowerCase() === 'cancelled' && order.cancellationReason && (
                    <div className="mb-5 p-3 bg-red-50 text-red-700 rounded-xl border border-red-100 flex items-start gap-3">
                        <svg className="w-5 h-5 flex-shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                        </svg>
                        <div className="text-sm">
                            <p className="font-semibold">{t('account.orders.cancellationReason', { defaultValue: 'Cancellation Reason' })}:</p>
                            <p>{order.cancellationReason}</p>
                        </div>
                    </div>
                )}

                {/* Product Items Grid */}
                <div className="mb-5">
                    <p className="text-xs text-muted uppercase tracking-wider mb-3">
                        {t('account.orders.items')} ({order.itemCount})
                    </p>
                    <div className="flex items-center gap-3 overflow-x-auto pb-2 scrollbar-thin">
                        {order.items.slice(0, 4).map((item, idx) => {
                            const imageSrc = resolveProductImage(item);

                            return (
                            <div
                                key={idx}
                                className="relative flex-shrink-0 group/item"
                                title={item.productName}
                            >
                                <div className="w-16 h-16 bg-muted/5 rounded-xl border border-muted/10 flex items-center justify-center overflow-hidden group-hover/item:border-primary/30 transition-colors">
                                    <img
                                        src={imageSrc}
                                        alt={item.productName}
                                        className="w-full h-full object-contain"
                                        loading="lazy"
                                    />
                                </div>
                                {item.quantity > 1 && (
                                    <span className="absolute -top-1.5 -right-1.5 w-5 h-5 bg-primary text-white text-xs rounded-full flex items-center justify-center font-medium shadow-sm">
                                        {item.quantity}
                                    </span>
                                )}
                            </div>
                            );
                        })}
                        {order.itemCount > 4 && (
                            <div className="w-16 h-16 bg-gradient-to-br from-muted/10 to-muted/5 rounded-xl border border-dashed border-muted/20 flex-shrink-0 flex items-center justify-center">
                                <span className="text-sm font-medium text-muted">+{order.itemCount - 4}</span>
                            </div>
                        )}
                    </div>
                </div>

                {/* Order Info Grid */}
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-5 p-4 bg-muted/5 rounded-xl">
                    <div>
                        <p className="text-xs text-muted mb-1">{t('account.orders.date')}</p>
                        <p className="font-medium text-text text-sm">{formattedDate}</p>
                    </div>
                    <div>
                        <p className="text-xs text-muted mb-1">{t('account.orders.payment.label')}</p>
                        <span className={`inline-flex items-center gap-1 text-xs px-2 py-1 rounded-lg ${paymentConfig.bgColor} ${paymentConfig.color}`}>
                            {t(`account.orders.payment.${order.paymentStatus.toLowerCase()}`, { defaultValue: order.paymentStatus })}
                        </span>
                    </div>
                    <div>
                        <p className="text-xs text-muted mb-1">{t('account.orders.shipping')}</p>
                        <p className="font-medium text-text text-sm truncate">
                            {order.shippingMethodName || t('account.orders.standard')}
                        </p>
                    </div>
                    <div>
                        <p className="text-xs text-muted mb-1">{t('account.orders.paymentMethod')}</p>
                        <p className="font-medium text-text text-sm truncate">
                            {order.paymentMethodName || '-'}
                        </p>
                    </div>
                </div>

                {/* Footer */}
                <div className="flex items-center justify-between pt-4 border-t border-muted/10">
                    <div>
                        <p className="text-xs text-muted">{t('account.orders.total')}</p>
                        <p className="text-xl font-bold text-text">{formatMoney(order.total, order.currency)}</p>
                    </div>
                    <Link
                        to={`/orders/${order.id}/track`}
                        className="inline-flex items-center gap-2 px-5 py-2.5 bg-primary/10 hover:bg-primary text-primary hover:text-white rounded-xl font-medium text-sm transition-all duration-200 group-hover:shadow-md"
                    >
                        <span>{t('account.orders.viewDetails')}</span>
                        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                        </svg>
                    </Link>
                </div>
            </div>
        </div>
    );
};

const OrdersSkeleton: React.FC = () => (
    <div className="space-y-4">
        {[1, 2, 3].map((n) => (
            <div key={n} className="bg-muted/5 border border-muted/10 rounded-xl p-4 animate-pulse">
                <div className="flex justify-between mb-4">
                    <div className="space-y-2">
                        <div className="h-3 w-16 bg-muted/20 rounded"></div>
                        <div className="h-4 w-24 bg-muted/20 rounded"></div>
                    </div>
                    <div className="h-6 w-20 bg-muted/20 rounded-full"></div>
                </div>
                <div className="flex gap-2 mb-4">
                    {[1, 2, 3].map((i) => (
                        <div key={i} className="w-12 h-12 bg-muted/20 rounded-lg"></div>
                    ))}
                </div>
                <div className="flex justify-between pt-3 border-t border-muted/10">
                    <div className="space-y-1">
                        <div className="h-3 w-20 bg-muted/20 rounded"></div>
                        <div className="h-4 w-16 bg-muted/20 rounded"></div>
                    </div>
                    <div className="h-5 w-24 bg-muted/20 rounded"></div>
                </div>
            </div>
        ))}
    </div>
);

export const OrderHistorySection: React.FC = () => {
    const { t } = useTranslation();
    const isAuthenticated = useAuthStore(selectIsAuthenticated);
    const [orders, setOrders] = useState<CustomerOrder[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [page, setPage] = useState(1);
    const [totalPages, setTotalPages] = useState(1);
    const [totalCount, setTotalCount] = useState(0);

    const loadOrders = useCallback(async () => {
        if (!isAuthenticated) {
            setLoading(false);
            return;
        }

        try {
            setLoading(true);
            setError(null);
            const response: CustomerOrdersResponse = await getOrders(page, 5);
            setOrders(response.orders);
            setTotalPages(response.totalPages);
            setTotalCount(response.totalCount);
        } catch (err) {
            console.error('[OrderHistory] Failed to load orders:', err);
            setError(t('account.orders.loadError'));
        } finally {
            setLoading(false);
        }
    }, [isAuthenticated, page, t]);

    useEffect(() => {
        loadOrders();
    }, [loadOrders]);

    return (
        <div className="bg-secondary/5 border border-muted/10 rounded-xl p-6 shadow-sm">
            {/* Header */}
            <div className="flex items-center gap-4 mb-6">
                <div className="w-12 h-12 bg-primary/10 rounded-full flex items-center justify-center">
                    <svg className="w-6 h-6 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                            d="M16 11V7a4 4 0 00-8 0v4M5 9h14l1 12H4L5 9z" />
                    </svg>
                </div>
                <div>
                    <h2 className="text-lg font-semibold text-text">
                        {t('account.orders.title')}
                    </h2>
                    <p className="text-sm text-muted">
                        {totalCount > 0
                            ? t('account.orders.count', { count: totalCount })
                            : t('account.orders.description')
                        }
                    </p>
                </div>
            </div>

            {/* Content */}
            {loading ? (
                <OrdersSkeleton />
            ) : error ? (
                <div className="text-center py-8">
                    <p className="text-red-500 mb-4">{error}</p>
                    <button
                        onClick={() => void loadOrders()}
                        className="text-primary hover:underline"
                    >
                        {t('common.retry')}
                    </button>
                </div>
            ) : orders.length === 0 ? (
                <div className="text-center py-8 bg-muted/5 rounded-lg border border-dashed border-muted/20">
                    <div className="text-4xl mb-3">📦</div>
                    <p className="text-muted mb-2">{t('account.orders.empty')}</p>
                    <Link to="/products" className="text-primary hover:underline text-sm">
                        {t('account.orders.startShopping')}
                    </Link>
                </div>
            ) : (
                <>
                    <div className="space-y-4">
                        {orders.map((order) => (
                            <OrderCard key={order.id} order={order} />
                        ))}
                    </div>

                    {/* Pagination */}
                    {totalPages > 1 && (
                        <div className="flex items-center justify-center gap-2 mt-6">
                            <button
                                onClick={() => setPage(p => Math.max(1, p - 1))}
                                disabled={page === 1}
                                className="px-3 py-1.5 text-sm border border-muted/20 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed hover:bg-muted/10 transition-colors"
                            >
                                {t('common.previous')}
                            </button>
                            <span className="text-sm text-muted">
                                {t('common.page')} {page} / {totalPages}
                            </span>
                            <button
                                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                                disabled={page === totalPages}
                                className="px-3 py-1.5 text-sm border border-muted/20 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed hover:bg-muted/10 transition-colors"
                            >
                                {t('common.next')}
                            </button>
                        </div>
                    )}
                </>
            )}
        </div>
    );
};

export default OrderHistorySection;

/**
 * Order Summary Card Component
 * 
 * Displays order summary information including order number,
 * date, payment method, shipping method, and total.
 * Read-only, clean layout.
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

import React from 'react';
import { formatMoney } from '@/utils';
import type { OrderDto } from '@/types/order';

// ============================================================================
// Types
// ============================================================================

interface OrderSummaryCardProps {
    order: OrderDto;
    className?: string;
}

// ============================================================================
// Summary Row Component
// ============================================================================

interface SummaryRowProps {
    label: string;
    value: React.ReactNode;
    emphasized?: boolean;
}

const SummaryRow: React.FC<SummaryRowProps> = ({ label, value, emphasized = false }) => {
    return (
        <div className="flex justify-between items-center py-2">
            <span className="text-muted text-sm">{label}</span>
            <span
                className={`
                    font-medium 
                    ${emphasized ? 'text-lg text-text' : 'text-text'}
                `}
            >
                {value}
            </span>
        </div>
    );
};

// ============================================================================
// Payment Method Badge
// ============================================================================

interface PaymentMethodBadgeProps {
    method: 'cod' | 'online';
    status: 'pending' | 'completed' | 'failed' | 'refunded';
}

const PaymentMethodBadge: React.FC<PaymentMethodBadgeProps> = ({ method, status }) => {
    const methodLabel = method === 'cod' ? 'Cash on Delivery' : 'Online Payment';

    let statusBadge: React.ReactNode = null;
    if (status === 'completed') {
        statusBadge = (
            <span className="inline-flex items-center gap-1 text-xs text-green-700 bg-green-100 px-2 py-0.5 rounded-full">
                <svg className="w-3 h-3" fill="currentColor" viewBox="0 0 20 20">
                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                </svg>
                Paid
            </span>
        );
    } else if (status === 'pending') {
        statusBadge = (
            <span className="text-xs text-amber-700 bg-amber-100 px-2 py-0.5 rounded-full">
                Pending
            </span>
        );
    }

    return (
        <div className="flex items-center gap-2">
            <span>{methodLabel}</span>
            {statusBadge}
        </div>
    );
};

// ============================================================================
// Main Component
// ============================================================================

export const OrderSummaryCard: React.FC<OrderSummaryCardProps> = ({
    order,
    className = '',
}) => {
    const formattedDate = new Date(order.createdAt).toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
    });

    return (
        <div
            className={`
                bg-background border border-muted/10 rounded-xl overflow-hidden
                ${className}
            `}
        >
            {/* Header */}
            <div className="bg-muted/5 px-5 py-4 border-b border-muted/10">
                <h2 className="text-lg font-semibold text-text flex items-center gap-2">
                    <svg
                        className="w-5 h-5 text-primary"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
                        />
                    </svg>
                    Order Summary
                </h2>
            </div>

            {/* Content */}
            <div className="p-5 space-y-1">
                {/* Order Number */}
                <div className="pb-3 mb-3 border-b border-muted/10">
                    <p className="text-xs text-muted uppercase tracking-wide mb-1">
                        Order Number
                    </p>
                    <p className="text-lg font-bold text-text font-mono">
                        #{order.orderNumber}
                    </p>
                </div>

                {/* Order Details */}
                <SummaryRow
                    label="Order Date"
                    value={formattedDate}
                />

                <SummaryRow
                    label="Payment Method"
                    value={
                        <PaymentMethodBadge
                            method={order.paymentMethod}
                            status={order.paymentStatus}
                        />
                    }
                />

                {order.shippingMethod && (
                    <SummaryRow
                        label="Shipping Method"
                        value={
                            <span>
                                {order.shippingMethod.name}
                                {order.shippingMethod.estimatedDays && (
                                    <span className="text-xs text-muted ml-1">
                                        ({order.shippingMethod.estimatedDays} days)
                                    </span>
                                )}
                            </span>
                        }
                    />
                )}

                {/* Tracking Info */}
                {order.trackingNumber && (
                    <SummaryRow
                        label="Tracking Number"
                        value={
                            <span className="font-mono text-primary">
                                {order.trackingNumber}
                            </span>
                        }
                    />
                )}

                {/* Pricing Breakdown */}
                <div className="pt-3 mt-3 border-t border-muted/10 space-y-1">
                    <SummaryRow
                        label="Subtotal"
                        value={formatMoney(order.subtotal, order.currency)}
                    />

                    <SummaryRow
                        label="Shipping"
                        value={
                            order.shippingCost === 0
                                ? <span className="text-green-600">Free</span>
                                : formatMoney(order.shippingCost, order.currency)
                        }
                    />

                    {/* PHASE: Promotions Engine - Promotion Discount */}
                    {order.promotionDiscountAmount && order.promotionDiscountAmount > 0 && (
                        <div className="flex justify-between items-center py-2">
                            <span className="text-red-500 text-sm flex items-center gap-1">
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
                                </svg>
                                Promotion Savings
                            </span>
                            <span className="text-red-500 font-medium">
                                -{formatMoney(order.promotionDiscountAmount, order.currency)}
                            </span>
                        </div>
                    )}

                    {/* Coupon Discount */}
                    {order.discountAmount > 0 && (
                        <SummaryRow
                            label={order.couponCode ? `Coupon (${order.couponCode})` : 'Coupon'}
                            value={
                                <span className="text-green-600">
                                    -{formatMoney(order.discountAmount, order.currency)}
                                </span>
                            }
                        />
                    )}

                    {order.taxAmount > 0 && (
                        <SummaryRow
                            label="Tax"
                            value={formatMoney(order.taxAmount, order.currency)}
                        />
                    )}

                    {/* Total */}
                    <div className="pt-3 mt-2 border-t border-muted/10">
                        <div className="flex justify-between items-center">
                            <span className="font-semibold text-text">Total</span>
                            <span className="text-xl font-bold text-primary">
                                {formatMoney(order.total, order.currency)}
                            </span>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

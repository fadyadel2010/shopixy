/**
 * Shipment Tracking Component
 * 
 * Displays shipment tracking information and timeline for customers.
 * Read-only display using order timeline data.
 * 
 * Phase: Storefront Shipment Tracking Enhancement
 * 
 * RULES:
 * - Read-only (no mutations)
 * - Uses existing order timeline data
 * - No business logic
 * - CQRS compliant (MongoDB data only)
 */

import React from 'react';
import { useTranslation } from '@/i18n';

// ============================================================================
// Types
// ============================================================================

interface ShipmentTrackingProps {
    trackingNumber?: string;
    carrierName?: string;
    trackingUrl?: string;
    timeline?: Array<{
        at: string;
        type: string;
        from?: string;
        to?: string;
        description?: string;
    }>;
}

// Icons mapping
const SHIPMENT_ICONS: Record<string, string> = {
    'OrderCreated': '📝',
    'MarkedAsPaid': '💳',
    'Shipped': '🚚',
    'InTransit': '🚛',
    'OutForDelivery': '🏃',
    'Delivered': '✅',
    'Cancelled': '❌',
    'Returned': '↩️',
    'StatusChanged': '📦',
    'ShipmentChanged': '🚚',
};

// ============================================================================
// Main Component
// ============================================================================

export const ShipmentTracking: React.FC<ShipmentTrackingProps> = ({
    trackingNumber,
    carrierName,
    trackingUrl,
    timeline = [],
}) => {
    const { t, language } = useTranslation();
    // Filter timeline to shipping-related events only
    const shipmentEvents = timeline.filter(event =>
        event.type === 'Shipped' ||
        event.type === 'Delivered' ||
        event.type === 'ShipmentChanged' ||
        event.description?.toLowerCase().includes('ship') ||
        event.description?.toLowerCase().includes('deliver')
    );

    // If no shipment info at all, don't render
    if (!trackingNumber && shipmentEvents.length === 0) {
        return (
            <div className="bg-muted/5 border border-muted/10 rounded-xl p-6 text-center">
                <div className="text-4xl mb-3">📦</div>
                <p className="text-muted">{t('order.tracking.shipmentUnavailable')}</p>
            </div>
        );
    }

    return (
        <div className="bg-white border border-muted/10 rounded-xl p-6">
            <h2 className="text-lg font-semibold text-text mb-4 flex items-center gap-2">
                <svg className="w-5 h-5 text-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"
                    />
                </svg>
                {t('order.tracking.shipment')}
            </h2>

            {/* Tracking Info */}
            {(trackingNumber || carrierName) && (
                <div className="bg-muted/5 rounded-lg p-4 mb-4 space-y-2">
                    {carrierName && (
                        <div className="flex justify-between items-center">
                            <span className="text-sm text-muted">{t('order.tracking.carrier')}</span>
                            <span className="font-medium text-text">{carrierName}</span>
                        </div>
                    )}
                    {trackingNumber && (
                        <div className="flex justify-between items-center">
                            <span className="text-sm text-muted">{t('order.tracking.trackingNumber')}</span>
                            <span className="font-mono text-primary font-medium text-sm">
                                {trackingNumber}
                            </span>
                        </div>
                    )}
                </div>
            )}

            {/* Track with Carrier Button */}
            {trackingUrl && trackingNumber && (
                <a
                    href={trackingUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="
                        block w-full py-2.5 px-4 rounded-lg text-center
                        bg-primary/10 text-primary font-medium text-sm
                        hover:bg-primary/20 transition-colors
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        mb-4
                    "
                >
                    {t('order.tracking.trackWith', { carrier: carrierName || t('order.tracking.carrier') })}
                </a>
            )}

            {/* Shipment Timeline */}
            {shipmentEvents.length > 0 && (
                <div>
                    <h3 className="text-sm font-semibold text-muted mb-3">{t('order.tracking.shipmentHistory')}</h3>
                    <div className="space-y-3">
                        {shipmentEvents.map((event, index) => (
                            <div
                                key={index}
                                className="flex gap-3 relative"
                            >
                                {/* Timeline line */}
                                {index < shipmentEvents.length - 1 && (
                                    <div className="absolute left-4 top-8 bottom-0 w-0.5 bg-muted/20" />
                                )}

                                {/* Event icon */}
                                <div className="relative flex-shrink-0 w-8 h-8 rounded-full bg-primary/10 text-primary flex items-center justify-center text-sm">
                                    {SHIPMENT_ICONS[event.type] || '📦'}
                                </div>

                                {/* Event details */}
                                <div className="flex-1 min-w-0 pt-0.5">
                                    <div className="flex justify-between items-start gap-2">
                                        <p className="font-medium text-text text-sm">
                                            {event.description || event.type}
                                        </p>
                                        <time className="text-xs text-muted flex-shrink-0">
                                            {new Date(event.at).toLocaleDateString(language === 'ar' ? 'ar-EG' : 'en-US', {
                                                month: 'short',
                                                day: 'numeric',
                                                hour: '2-digit',
                                                minute: '2-digit',
                                            })}
                                        </time>
                                    </div>
                                    {event.to && (
                                        <p className="text-xs text-muted mt-0.5">
                                            {t('order.status')}: {event.to}
                                        </p>
                                    )}
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {/* Help Text */}
            {trackingNumber && shipmentEvents.length === 0 && (
                <p className="text-sm text-muted text-center mt-4">
                    Tracking information will be updated by the carrier
                </p>
            )}
        </div>
    );
};

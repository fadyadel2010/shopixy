import type { ReactElement } from 'react';
import type { AdminOrderCommand, AdminOrderDetails, AdminOrderFilters } from '../services/adminOrdersApi';
import type { CancellationOutcome, ShipmentActionAuthority } from '../modules/shipping/shippingTypes';
export interface OrderDetailsPanelProps {
    order: AdminOrderDetails | null; onClose(): void;
    isLoading?: boolean; isError?: boolean; error?: unknown; onRetry?: () => void; isRetrying?: boolean;
    onStatusChange?: (request: { newStatus: string; reason?: string }) => void; isStatusUpdating?: boolean; statusUpdateError?: unknown;
    fulfillmentAuthority?: ShipmentActionAuthority | null; fulfillmentAuthorityError?: unknown; isFulfillmentAuthorityLoading?: boolean;
    onFulfillmentAction?: (request: unknown) => void; isFulfillmentActionUpdating?: boolean; fulfillmentActionError?: unknown;
    paymentAuthority?: unknown; paymentAuthorityError?: unknown; isPaymentAuthorityLoading?: boolean; onPaymentAction?: (request: unknown) => void;
    isPaymentActionUpdating?: boolean; paymentActionError?: unknown;
    onAdministrativeAction?: (request: AdminOrderCommand) => void | Promise<void>; isAdministrativeActionUpdating?: boolean;
    administrativeActionError?: unknown; administrativeActionResult?: CancellationOutcome | null;
    onConfirmReturnReceipt?: (request: unknown) => void; isReturnReceiptUpdating?: boolean; returnReceiptError?: unknown;
    customerTrust?: unknown; isCustomerTrustLoading?: boolean; customerTrustError?: unknown; onRetryCustomerTrust?: () => void;
    onCustomerPhoneAction?: (request: unknown) => void; isCustomerPhoneActionUpdating?: boolean; customerPhoneActionError?: unknown;
    sessionIntelligence?: unknown; isSessionIntelligenceLoading?: boolean; sessionIntelligenceError?: unknown; onRetrySessionIntelligence?: () => void;
}
export function OrderDetailsPanel(props: OrderDetailsPanelProps): ReactElement;
export default function Orders(): ReactElement;
export function buildManualDispatchPayload(shipmentId: string | undefined, trackingNumber: string): { shipmentId: string; trackingNumber: string };
export function normalizeFilters(params: URLSearchParams): AdminOrderFilters;
export function getDirectOrderStatusMutationOptions(order: {
    status?: string; rowVersion?: string; availableOrderActions?: Array<{ code?: string; targetStatus?: string }>;
} | null): string[];
export function isFulfillmentActionExecutableFromOrdersDrawer(action: { code?: string } | null): boolean;
export function isPaymentActionExecutableFromOrdersDrawer(action: { actionCode?: string } | null): boolean;
export function validateManualDeliveryEvidence(evidence: { deliveryReference?: string; proofOfDeliveryUrl?: string; recipientName?: string }, translate?: (key: string) => string): string | null;
export function validateReturnReceiptEvidence(evidence: { resellableQuantity?: number | string; damagedQuantity?: number | string }, quantity: number, translate?: (key: string) => string): string | null;
export function getLifecycleMutationErrorMessage(error: unknown, translate?: (key: string) => string): string;

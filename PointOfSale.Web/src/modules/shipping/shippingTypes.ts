export interface ShipmentPresentation {
    id?: string; shipmentId?: string; orderId?: string; orderNumber?: string; shipmentNumber?: string; status?: string;
    carrierCode?: string; carrierName?: string; serviceLevel?: string; shippingMethodName?: string; shippingCost?: number; currency?: string;
    recipientName?: string; recipientPhone?: string; recipientEmail?: string; shippingAddress?: string;
    recipient?: { name?: string; phone?: string; email?: string; address?: string; city?: string; state?: string; country?: string; zipCode?: string };
    shippingCity?: string; shippingState?: string; shippingZipCode?: string; shippingCountry?: string;
    externalRegistrationState?: string; externalRegistrationError?: string | null; externalRegistrationAttemptCount?: number;
    externalRegistrationRecoveryRequired?: boolean;
    awbNumber?: string | null; trackingNumber?: string | null; trackingUrl?: string | null; labelFormat?: string | null; labelData?: string | null; labelUrl?: string | null;
    isCashOnDelivery?: boolean; codAmount?: number; codCollected?: boolean; codCollectedAmount?: number | null;
    codCollectionDate?: string | null; createdAtUtc?: string; updatedAtUtc?: string;
    readyForPickupAtUtc?: string | null; pickedUpAtUtc?: string | null; inTransitAtUtc?: string | null;
    outForDeliveryAtUtc?: string | null; deliveredAtUtc?: string | null; cancelledAtUtc?: string | null; returnedAtUtc?: string | null;
    cancellation?: CancellationOutcome | null;
}
export interface ShipmentActionAuthority {
    orderId: string; currentStatus?: string; availableActions: Array<{
        code: string; label: string; targetStatus: string; requiresConfirmation: boolean; requiredFields: string[]; iconHint?: string;
    }>;
}
export interface CancellationOutcome {
    outcome: 'Confirmed' | 'Requested' | 'Unsupported' | 'ReconciliationRequired';
    shipmentId?: string; operationId?: string; code?: string; message?: string; currentStatus?: string;
    confirmationBasis?: string; confirmedAtUtc?: string; externalConfirmationExists?: boolean;
}

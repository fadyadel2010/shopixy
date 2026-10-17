import type { CartDto } from '../types';

interface CartFailureLike {
    message?: string;
    response?: {
        status?: number;
        data?: {
            code?: string;
            message?: string;
        };
    };
}

export interface CartFetchFailureResolution {
    nextCart: CartDto | null;
    message: string;
    preserveLastValidCart: boolean;
}

function getFailureCode(error: unknown): string | undefined {
    const candidate = error as CartFailureLike | undefined;
    return candidate?.response?.data?.code;
}

function getFailureStatus(error: unknown): number | undefined {
    const candidate = error as CartFailureLike | undefined;
    return candidate?.response?.status;
}

function getFailureMessage(error: unknown): string | undefined {
    const candidate = error as CartFailureLike | undefined;
    return candidate?.response?.data?.message || candidate?.message;
}

function isNetworkFailure(error: unknown): boolean {
    const message = getFailureMessage(error);
    return message?.trim().toLowerCase() === 'network error';
}

export function resolveCartFetchFailure(
    previousCart: CartDto | null,
    emptyCart: CartDto,
    error: unknown
): CartFetchFailureResolution {
    const code = getFailureCode(error);
    const status = getFailureStatus(error);
    const message = getFailureMessage(error);
    const hasPreviousCart = !!previousCart;

    if (code === 'CART_NOT_FOUND' || status === 404) {
        return {
            nextCart: emptyCart,
            message: 'Your cart is no longer available. Please add your items again.',
            preserveLastValidCart: false,
        };
    }

    if (status === 400 || status === 401 || status === 403 || status === 422) {
        return {
            nextCart: emptyCart,
            message: 'Your cart could not be authorized or validated. Please reload it before continuing.',
            preserveLastValidCart: false,
        };
    }

    if (isNetworkFailure(error)) {
        return {
            nextCart: previousCart ?? emptyCart,
            message: hasPreviousCart
                ? 'Network error while syncing your cart. Previously loaded items are unconfirmed; refresh your cart before checkout.'
                : 'Network error while loading your cart. Please try again.',
            preserveLastValidCart: hasPreviousCart,
        };
    }

    return {
        nextCart: previousCart ?? emptyCart,
        message: hasPreviousCart
            ? 'We could not refresh your cart. Previously loaded items are unconfirmed; refresh your cart before checkout.'
            : message || 'We could not load your cart right now. Please try again.',
        preserveLastValidCart: hasPreviousCart,
    };
}

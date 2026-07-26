import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { setSessionId } from '@/bootstrap';
import { recoverCart } from '@/services/api';
import { useCartStore } from '@/store/cartStore';
import type { CartDto, RecoveredCartResponse } from '@/types';

interface RecoveryError {
    title: string;
    message: string;
    ctaLabel: string;
    ctaHref: string;
}

const DEFAULT_ERROR: RecoveryError = {
    title: 'Recovery Link Unavailable',
    message: 'This cart recovery link has expired or is no longer available. Please return to the store and continue shopping.',
    ctaLabel: 'Go to Cart',
    ctaHref: '/cart',
};

function isRecoveredCartSnapshot(snapshot: RecoveredCartResponse['cartSnapshot']): snapshot is CartDto {
    if (!snapshot) {
        return false;
    }

    if (!snapshot.cartId || !snapshot.tenantId || !snapshot.ownerType || !snapshot.ownerId) {
        return false;
    }

    if (!Array.isArray(snapshot.items) || snapshot.items.length === 0) {
        return false;
    }

    return snapshot.items.every((item) =>
        !!item.productId &&
        !!item.productName &&
        typeof item.quantity === 'number' &&
        item.quantity > 0 &&
        typeof item.unitPrice === 'number' &&
        item.unitPrice >= 0 &&
        typeof item.lineTotal === 'number' &&
        item.lineTotal >= 0);
}

export default function RecoverCartPage() {
    const { token } = useParams<{ token: string }>();
    const navigate = useNavigate();
    const [status, setStatus] = useState<'loading' | 'error'>('loading');

    useEffect(() => {
        if (!token) {
            setStatus('error');
            return;
        }

        const restore = async () => {
            try {
                const response = await recoverCart(token);

                if (!isRecoveredCartSnapshot(response.cartSnapshot) ||
                    response.cartSnapshot.cartId !== response.cartId ||
                    response.cartSnapshot.tenantId !== response.tenantId) {
                    console.error('[RecoverCart] Recovery payload is missing a valid cart snapshot.');
                    setStatus('error');
                    return;
                }

                if (response.sessionId) {
                    setSessionId(response.sessionId);
                }

                useCartStore.getState().restoreRecoveredCart(response.cartSnapshot, response.message);
                navigate('/cart', { replace: true });
            } catch (error) {
                console.error('[RecoverCart] Recovery failed:', error);
                setStatus('error');
            }
        };

        void restore();
    }, [navigate, token]);

    if (status === 'error') {
        const isExternalUrl = DEFAULT_ERROR.ctaHref.startsWith('http://') || DEFAULT_ERROR.ctaHref.startsWith('https://');

        return (
            <div className="min-h-screen flex items-center justify-center bg-gray-50">
                <div className="text-center p-8 max-w-md">
                    <h1 className="text-2xl font-bold text-gray-900 mb-2">
                        {DEFAULT_ERROR.title}
                    </h1>
                    <p className="text-gray-600 mb-6">
                        {DEFAULT_ERROR.message}
                    </p>
                    {isExternalUrl ? (
                        <a
                            href={DEFAULT_ERROR.ctaHref}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-block px-6 py-3 bg-indigo-600 text-white rounded-lg font-medium hover:bg-indigo-700 transition-colors"
                        >
                            {DEFAULT_ERROR.ctaLabel}
                        </a>
                    ) : (
                        <Link
                            to={DEFAULT_ERROR.ctaHref}
                            className="inline-block px-6 py-3 bg-indigo-600 text-white rounded-lg font-medium hover:bg-indigo-700 transition-colors"
                        >
                            {DEFAULT_ERROR.ctaLabel}
                        </Link>
                    )}
                </div>
            </div>
        );
    }

    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-50">
            <div className="text-center">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600 mx-auto mb-4" />
                <p className="text-gray-600">Restoring your cart...</p>
            </div>
        </div>
    );
}

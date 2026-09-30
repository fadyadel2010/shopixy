import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { setSessionId } from '@/bootstrap';
import { useTranslation } from '@/i18n';
import { recoverCart } from '@/services/api';
import { useCartStore } from '@/store/cartStore';
import type { CartDto, RecoveredCartResponse } from '@/types';

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
    const { t } = useTranslation();
    const { token } = useParams<{ token: string }>();
    const navigate = useNavigate();
    const [status, setStatus] = useState<'loading' | 'error'>('loading');
    const recoveryStartedRef = useRef(false);

    useEffect(() => {
        if (recoveryStartedRef.current) {
            return;
        }
        recoveryStartedRef.current = true;

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
                    setStatus('error');
                    return;
                }

                if (response.sessionId) {
                    setSessionId(response.sessionId);
                }

                useCartStore.getState().restoreRecoveredCart(response.cartSnapshot, response.message);
                navigate('/cart', { replace: true });
            } catch {
                setStatus('error');
            }
        };

        void restore();
    }, [navigate, token]);

    if (status === 'error') {
        return (
            <main className="min-h-screen flex items-center justify-center bg-gray-50 px-4" aria-labelledby="recovery-error-title">
                <div className="box-border w-full max-w-md p-8 text-center">
                    <h1 id="recovery-error-title" className="text-2xl font-bold text-gray-900 mb-2">
                        {t('cartRecovery.unavailableTitle')}
                    </h1>
                    <p className="text-gray-600 mb-6">
                        {t('cartRecovery.unavailableMessage')}
                    </p>
                    <Link
                        to="/cart"
                        className="inline-block px-6 py-3 bg-indigo-600 text-white rounded-lg font-medium hover:bg-indigo-700 transition-colors"
                    >
                        {t('cartRecovery.goToCart')}
                    </Link>
                </div>
            </main>
        );
    }

    return (
        <main className="min-h-screen flex items-center justify-center bg-gray-50" aria-live="polite" aria-busy="true">
            <div className="text-center">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600 mx-auto mb-4" />
                <p className="text-gray-600">{t('cartRecovery.restoring')}</p>
            </div>
        </main>
    );
}

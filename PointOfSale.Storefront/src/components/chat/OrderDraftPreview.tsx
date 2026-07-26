/**
 * AI Commerce Assistant - Order Draft Preview
 * Shows the order draft created by the AI assistant.
 */

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useCartStore } from '@/store';
import type { OrderDraft } from '../../types/chat';

interface OrderDraftPreviewProps {
    draft: OrderDraft;
    onAccept: () => void;
    onDismiss: () => void;
    isRTL: boolean;
}

const OrderDraftPreview: React.FC<OrderDraftPreviewProps> = ({
    draft,
    onAccept,
    onDismiss,
    isRTL,
}) => {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const applyDraft = useCartStore((state) => state.applyDraft);
    const [isApplying, setIsApplying] = useState(false);

    const handleAccept = async () => {
        setIsApplying(true);
        try {
            const success = await applyDraft(draft);
            if (success) {
                onAccept(); // Calls onAccept prop which might close modal
                navigate('/checkout');
            }
        } finally {
            setIsApplying(false);
        }
    };

    // Secondary action: Review in Cart
    const handleReviewInCart = async () => {
        setIsApplying(true);
        try {
            const success = await applyDraft(draft);
            if (success) {
                onAccept();
                navigate('/cart');
            }
        } finally {
            setIsApplying(false);
        }
    };

    if (!draft.items || draft.items.length === 0) {
        return null;
    }

    const intentLabels: Record<string, { en: string; ar: string }> = {
        create: { en: 'New Order', ar: 'طلب جديد' },
        update: { en: 'Updated Order', ar: 'تحديث الطلب' },
        confirm: { en: 'Ready to Order', ar: 'جاهز للطلب' },
    };

    const intentLabel = intentLabels[draft.intent];

    return (
        <div className="order-draft-preview" dir={isRTL ? 'rtl' : 'ltr'}>
            <div className="order-draft-preview__header">
                <span className="order-draft-preview__badge">
                    {intentLabel ? (isRTL ? intentLabel.ar : intentLabel.en) : draft.intent}
                </span>
                <button
                    className="order-draft-preview__dismiss"
                    onClick={onDismiss}
                    aria-label={t('chat.draft.dismiss', 'Dismiss')}
                >
                    <svg
                        xmlns="http://www.w3.org/2000/svg"
                        width="14"
                        height="14"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="2"
                    >
                        <line x1="18" y1="6" x2="6" y2="18" />
                        <line x1="6" y1="6" x2="18" y2="18" />
                    </svg>
                </button>
            </div>

            <div className="order-draft-preview__items">
                {draft.items.map((item, index) => (
                    <div key={index} className="order-draft-preview__item">
                        {item.productImage && (
                            <img src={item.productImage} alt={item.productName} className="order-draft-preview__image" />
                        )}
                        <span className="order-draft-preview__quantity">{item.quantity}×</span>
                        <span className="order-draft-preview__product">
                            {item.productName || `${t('chat.draft.product', 'Product')} #${index + 1}`}
                        </span>
                    </div>
                ))}
            </div>

            <div className="order-draft-preview__actions">
                <button
                    className="order-draft-preview__accept"
                    onClick={handleAccept}
                    disabled={isApplying}
                >
                    {isApplying ? t('common.processing', 'Processing...') : t('chat.draft.checkout', 'Proceed to Checkout')}
                </button>
                <button
                    className="order-draft-preview__edit"
                    onClick={handleReviewInCart}
                    disabled={isApplying}
                >
                    {t('chat.draft.edit', 'Review in Cart')}
                </button>
            </div>
        </div>
    );
};

export default OrderDraftPreview;

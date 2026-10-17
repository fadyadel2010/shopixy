/**
 * Cart Item Row Component
 * 
 * Displays a single cart item with quantity controls.
 * Mobile-first responsive design.
 * Uses centralized formatMoney utility.
 */

import React, { useState, useCallback } from 'react';
import { formatMoney, resolveProductImage } from '@/utils';
import type { CartItemDto } from '@/types';
import { OptimizedImage } from '@/components/media';

interface CartItemRowProps {
    /** Cart item data */
    item: CartItemDto;
    /** Whether this item is being updated */
    isUpdating: boolean;
    /** Currency code */
    currency: string;
    /** Quantity change callback - PHASE 11.7: variantId is now required */
    onQuantityChange: (productId: string, quantity: number, variantId?: string) => Promise<void>;
    /** Remove item callback - PHASE 11.7: variantId is now required */
    onRemove: (productId: string, variantId?: string) => Promise<void>;
}

/**
 * Cart Item Row Component
 */
export const CartItemRow: React.FC<CartItemRowProps> = ({
    item,
    isUpdating,
    currency,
    onQuantityChange,
    onRemove,
}) => {
    const [localQuantity, setLocalQuantity] = useState(item.quantity);
    const [isRemoving, setIsRemoving] = useState(false);

    // Sync local quantity with prop
    React.useEffect(() => {
        setLocalQuantity(item.quantity);
    }, [item.quantity]);

    /**
     * Handle quantity decrease
     */
    const handleDecrease = useCallback(async () => {
        if (localQuantity <= 1 || isUpdating) return;

        const newQuantity = localQuantity - 1;
        setLocalQuantity(newQuantity);
        // PHASE 11.7: Pass variantId for variant-centric operations
        await onQuantityChange(item.productId, newQuantity, item.variantId);
    }, [localQuantity, isUpdating, item.productId, item.variantId, onQuantityChange]);

    /**
     * PHASE 11.7.1: Handle quantity increase with availableStock check
     * Prevents unnecessary API calls when max stock is reached
     */
    const handleIncrease = useCallback(async () => {
        if (isUpdating) return;

        // Check max quantity (system limit)
        if (item.maxQuantity && localQuantity >= item.maxQuantity) {
            return;
        }

        // PHASE 11.7.1: Check available stock (prevents overselling)
        if (item.availableStock !== undefined && localQuantity >= item.availableStock) {
            return;
        }

        const newQuantity = localQuantity + 1;
        setLocalQuantity(newQuantity);
        // PHASE 11.7: Pass variantId for variant-centric operations
        await onQuantityChange(item.productId, newQuantity, item.variantId);
    }, [localQuantity, isUpdating, item.productId, item.variantId, item.maxQuantity, item.availableStock, onQuantityChange]);

    /**
     * Handle remove
     */
    const handleRemove = useCallback(async () => {
        setIsRemoving(true);
        // PHASE 11.7: Pass variantId for variant-centric operations
        await onRemove(item.productId, item.variantId);
    }, [item.productId, item.variantId, onRemove]);

    // PHASE 11.7.1: Check both max quantity and available stock
    const isAtMaxQuantity = item.maxQuantity !== undefined && localQuantity >= item.maxQuantity;
    const isAtMaxStock = item.availableStock !== undefined && localQuantity >= item.availableStock;
    const isAtMaxLimit = isAtMaxQuantity || isAtMaxStock;
    const isDisabled = isUpdating || isRemoving;
    const imageSrc = resolveProductImage(item);

    return (
        <div
            className={`flex flex-col sm:flex-row gap-4 p-4 border-b border-muted/10 last:border-b-0 transition-opacity ${isRemoving ? 'opacity-50' : ''
                }`}
        >
            {/* Product Image */}
            <div className="flex gap-4">
                <a
                    href={`/products/${encodeURIComponent(item.productId)}`}
                    className="flex-shrink-0"
                >
                    <div className="w-20 h-20 bg-muted/10 rounded-lg overflow-hidden">
                        <OptimizedImage
                            src={imageSrc}
                            alt={item.productName}
                            sizes="80px"
                            className="w-full h-full object-cover"
                        />
                    </div>
                </a>

                {/* Product Info */}
                <div className="flex-1 min-w-0">
                    <a
                        href={`/products/${encodeURIComponent(item.productId)}`}
                        className="block hover:text-primary transition-colors"
                    >
                        <h3 className="font-medium text-text line-clamp-2">
                            {item.productName}
                        </h3>
                    </a>
                    {/* PHASE 11.3: Display variant attributes (e.g., "Red / XL") */}
                    {item.variantAttributes && item.variantAttributes.length > 0 && (
                        <p className="text-sm text-primary/80 mt-0.5">
                            {item.variantAttributes.map(a => a.value).join(' / ')}
                        </p>
                    )}
                    <p className="text-sm text-muted mt-1">
                        SKU: {item.sku}
                    </p>
                    <p className="text-sm text-text mt-1">
                        {formatMoney(item.unitPrice, currency)} each
                    </p>
                </div>
            </div>

            {/* Quantity and Actions */}
            <div className="flex items-center justify-between sm:justify-end gap-4 sm:gap-6">
                {/* Quantity Controls */}
                <div className="flex items-center gap-1">
                    <button
                        onClick={handleDecrease}
                        disabled={isDisabled || localQuantity <= 1}
                        className="w-9 h-9 flex items-center justify-center rounded-lg border border-muted/20 text-text hover:bg-muted/10 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                        aria-label="Decrease quantity"
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M20 12H4" />
                        </svg>
                    </button>

                    <span className="w-10 text-center font-medium text-text">
                        {localQuantity}
                    </span>

                    {/* PHASE 11.7.1: Disable + button when at max stock or max quantity */}
                    <button
                        onClick={handleIncrease}
                        disabled={isDisabled || isAtMaxLimit}
                        className="w-9 h-9 flex items-center justify-center rounded-lg border border-muted/20 text-text hover:bg-muted/10 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                        aria-label="Increase quantity"
                        title={isAtMaxStock ? "Maximum available quantity reached" : isAtMaxQuantity ? "Maximum quantity reached" : ""}
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                        </svg>
                    </button>
                </div>

                {/* Line Total */}
                <div className="min-w-[80px] text-right">
                    <p className="font-semibold text-text">
                        {formatMoney(item.lineTotal, currency)}
                    </p>
                </div>

                {/* Remove Button */}
                <button
                    onClick={handleRemove}
                    disabled={isDisabled}
                    className="w-9 h-9 flex items-center justify-center rounded-lg text-muted hover:text-red-600 hover:bg-red-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    aria-label="Remove from cart"
                >
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                    </svg>
                </button>
            </div>

            {/* PHASE 11.7.1: Max quantity/stock warning with distinct messages */}
            {isAtMaxStock && (
                <p className="text-xs text-amber-600 mt-2 sm:mt-0 sm:ml-auto">
                    Maximum available quantity reached
                </p>
            )}
            {isAtMaxQuantity && !isAtMaxStock && (
                <p className="text-xs text-amber-600 mt-2 sm:mt-0 sm:ml-auto">
                    Maximum quantity reached
                </p>
            )}
        </div>
    );
};

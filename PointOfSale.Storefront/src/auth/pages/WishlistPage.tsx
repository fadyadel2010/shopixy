/**
 * Wishlist Page
 * Phase 10.3.6 - Wishlist (Production-grade)
 * Phase 10.3.6.1 - Enhanced with "Move to Cart" functionality
 * 
 * Full wishlist view for authenticated users.
 * Protected route - requires authentication.
 */

import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Layout } from '@/components/layout';
import { useCategories, useCurrency } from '@/hooks';
import { useTranslation } from '@/i18n';
import { useWishlist } from '@/hooks/useWishlist';
import ProductBadges from '@/components/products/ProductBadges';
import { WishlistButton } from '@/components/products/WishlistButton';
import { ProtectedRoute } from '@/core/guards';
import { httpClient } from '@/services/api/httpClient';
import { useCartStore } from '@/store';
import type { ProductReadDto } from '@/types';
import { resolveProductImage } from '@/utils';
import { OptimizedImage } from '@/components/media';

/**
 * Wishlist Page Content - Protected
 */
const WishlistPageContent: React.FC = () => {
    const { t, direction, language } = useTranslation();
    const { categories } = useCategories();
    const { items, count, isLoading, error, clearWishlist, refresh, removeItem } = useWishlist();
    const addToCart = useCartStore((state) => state.addItem);
    const { formatPrice } = useCurrency();

    // Product details state
    const [products, setProducts] = useState<Map<string, ProductReadDto | null>>(new Map());
    const [loadingProducts, setLoadingProducts] = useState(false);
    const [addingToCart, setAddingToCart] = useState<string | null>(null);
    const [cartMessage, setCartMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

    // Debug logging
    useEffect(() => {
        console.log('[WishlistPage] State:', {
            itemsCount: items.length,
            storeCount: count,
            productsMapSize: products.size,
            isLoading,
            loadingProducts
        });
        if (items.length > 0) {
            console.log('[WishlistPage] Items:', items);
        }
    }, [items, count, products, isLoading, loadingProducts]);

    // Fetch product details for wishlist items
    useEffect(() => {
        const fetchProductDetails = async () => {
            if (items.length === 0) {
                setProducts(new Map());
                return;
            }

            setLoadingProducts(true);
            const newProducts = new Map<string, ProductReadDto | null>();

            try {
                // Fetch product details in parallel
                const promises = items.map(async (item) => {
                    try {
                        const response = await httpClient.get<ProductReadDto>(
                            `/api/storefront/products/by-id/${item.productId}`
                        );
                        return { productId: item.productId, product: response.data };
                    } catch {
                        // Product might be deleted or unavailable
                        return { productId: item.productId, product: null };
                    }
                });

                const results = await Promise.all(promises);
                results.forEach(({ productId, product }) => {
                    newProducts.set(productId, product);
                });
            } catch (err) {
                console.error('[WishlistPage] Failed to fetch product details:', err);
            } finally {
                setProducts(newProducts);
                setLoadingProducts(false);
            }
        };

        fetchProductDetails();
    }, [items]);

    // Initial fetch
    useEffect(() => {
        refresh();
    }, [refresh]);

    // Clear message after timeout
    useEffect(() => {
        if (cartMessage) {
            const timer = setTimeout(() => setCartMessage(null), 3000);
            return () => clearTimeout(timer);
        }
    }, [cartMessage]);

    const handleClearWishlist = async () => {
        if (!window.confirm(t('wishlist.clearConfirm'))) {
            return;
        }
        try {
            await clearWishlist();
        } catch (err) {
            console.error('[WishlistPage] Failed to clear wishlist:', err);
        }
    };

    const handleMoveToCart = async (productId: string, productName: string) => {
        setAddingToCart(productId);
        try {
            // Add to cart
            const success = await addToCart(productId, 1);
            if (success) {
                // Remove from wishlist
                await removeItem(productId);
                setCartMessage({
                    type: 'success',
                    text: `${productName} ${t('cart.itemAdded', { defaultValue: 'added to cart' })}`
                });
            } else {
                setCartMessage({
                    type: 'error',
                    text: t('wishlist.errors.addFailed', { defaultValue: 'Failed to add to cart' })
                });
            }
        } catch (err) {
            console.error('[WishlistPage] Failed to move to cart:', err);
            setCartMessage({
                type: 'error',
                text: t('wishlist.errors.addFailed', { defaultValue: 'Failed to add to cart' })
            });
        } finally {
            setAddingToCart(null);
        }
    };

    const handleAddToCart = async (productId: string, productName: string) => {
        setAddingToCart(productId);
        try {
            const success = await addToCart(productId, 1);
            if (success) {
                setCartMessage({
                    type: 'success',
                    text: `${productName} ${t('cart.itemAdded', { defaultValue: 'added to cart' })}`
                });
            } else {
                setCartMessage({
                    type: 'error',
                    text: t('wishlist.errors.addFailed', { defaultValue: 'Failed to add to cart' })
                });
            }
        } catch (err) {
            console.error('[WishlistPage] Failed to add to cart:', err);
            setCartMessage({
                type: 'error',
                text: t('wishlist.errors.addFailed', { defaultValue: 'Failed to add to cart' })
            });
        } finally {
            setAddingToCart(null);
        }
    };

    const getProductName = (product: ProductReadDto) => {
        if (language === 'ar' && product.nameAr) {
            return product.nameAr;
        }
        return product.name;
    };

    const renderEmptyState = () => (
        <div className="text-center py-16">
            <div className="w-24 h-24 mx-auto mb-6 bg-primary/10 rounded-full flex items-center justify-center">
                <svg className="w-12 h-12 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                        d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" />
                </svg>
            </div>
            <h2 className="text-2xl font-semibold text-text mb-2">
                {t('wishlist.empty')}
            </h2>
            <p className="text-muted max-w-md mx-auto mb-8">
                {t('wishlist.emptyDescription')}
            </p>
            <Link
                to="/products"
                className="inline-flex items-center gap-2 px-6 py-3 bg-primary text-white rounded-lg hover:bg-primary/90 transition-colors font-medium"
            >
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M16 11V7a4 4 0 00-8 0v4M5 9h14l1 12H4L5 9z" />
                </svg>
                {t('wishlist.continueShopping')}
            </Link>
        </div>
    );

    const renderWishlistItems = () => (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
            {items.map((item) => {
                const product = products.get(item.productId);
                const isAddingThis = addingToCart === item.productId;

                // 1. Loading State (Not in map yet)
                if (!products.has(item.productId)) {
                    return (
                        <div key={item.productId} className="bg-muted/5 rounded-lg p-4 animate-pulse">
                            <div className="aspect-square bg-muted/20 rounded-lg mb-4" />
                            <div className="h-4 bg-muted/20 rounded w-3/4 mb-2" />
                            <div className="h-4 bg-muted/20 rounded w-1/2" />
                        </div>
                    );
                }

                // 2. Not Found State (In map, but null)
                if (!product) {
                    return (
                        <div key={item.productId} className="bg-red-50 border border-red-100 rounded-lg p-4 flex flex-col items-center justify-center min-h-[300px] text-center">
                            <div className="w-16 h-16 mb-4 bg-red-100 rounded-full flex items-center justify-center text-red-400">
                                <svg className="w-8 h-8" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                                        d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636" />
                                </svg>
                            </div>
                            <p className="text-red-700 font-medium mb-1">
                                {t('product.notFound', { defaultValue: 'Product Unavailable' })}
                            </p>
                            <p className="text-red-500 text-xs mb-6 max-w-[200px]">
                                {t('product.notFoundDesc', { defaultValue: 'This product may have been removed or is no longer available.' })}
                            </p>

                            <button
                                onClick={() => removeItem(item.productId)}
                                className="flex items-center gap-2 px-4 py-2 bg-white border border-red-200 text-red-600 rounded-lg hover:bg-red-50 transition-colors text-sm font-medium shadow-sm"
                            >
                                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                                </svg>
                                {t('common.remove')}
                            </button>
                        </div>
                    );
                }

                return (
                    <div
                        key={item.productId}
                        className="group relative bg-background border border-muted/20 rounded-lg overflow-hidden hover:shadow-lg transition-all"
                    >
                        {/* Wishlist Button */}
                        <div className="absolute top-2 right-2 z-10">
                            <WishlistButton productId={product.id} size="small" />
                        </div>

                        <Link
                            to={product.slug ? `/products/${product.slug}` : `/products/${product.id}`}
                            className="block"
                        >
                            {/* Image */}
                            <div className="aspect-square bg-muted/5 relative overflow-hidden">
                                <ProductBadges product={product} />
                                <OptimizedImage
                                    src={resolveProductImage(product)}
                                    alt={getProductName(product)}
                                    sizes="(max-width: 768px) 50vw, (max-width: 1280px) 25vw, 320px"
                                    className="absolute inset-0 w-full h-full object-contain group-hover:scale-105 transition-transform duration-300"
                                />

                                {/* Out of Stock Badge */}
                                {!product.isAvailable && (
                                    <div className="absolute top-2 left-2 bg-muted/90 text-white text-xs font-medium px-3 py-1.5 rounded-full">
                                        {t('product.outOfStock')}
                                    </div>
                                )}
                            </div>

                            {/* Content */}
                            <div className="p-4">
                                <h3 className="text-text font-semibold text-base line-clamp-2 group-hover:text-primary transition-colors mb-2">
                                    {getProductName(product)}
                                </h3>
                                <div className="flex items-center justify-between">
                                    <span className="text-primary font-bold text-lg">
                                        {formatPrice(product.price)}
                                    </span>
                                    <span className="text-xs text-muted">
                                        {new Date(item.addedAtUtc).toLocaleDateString()}
                                    </span>
                                </div>
                            </div>
                        </Link>

                        {/* Action Buttons */}
                        <div className="p-4 pt-0 flex gap-2">
                            {/* Move to Cart Button (removes from wishlist) */}
                            <button
                                onClick={() => handleMoveToCart(product.id, getProductName(product))}
                                disabled={!product.isAvailable || isAddingThis}
                                className="flex-1 flex items-center justify-center gap-2 px-3 py-2.5 bg-primary text-white text-sm font-medium rounded-lg hover:bg-primary/90 disabled:bg-muted disabled:cursor-not-allowed transition-colors"
                                title={t('wishlist.moveToCart', { defaultValue: 'Move to Cart' })}
                            >
                                {isAddingThis ? (
                                    <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                                ) : (
                                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                            d="M3 3h2l.4 2M7 13h10l4-8H5.4M7 13L5.4 5M7 13l-2.293 2.293c-.63.63-.184 1.707.707 1.707H17m0 0a2 2 0 100 4 2 2 0 000-4zm-8 2a2 2 0 11-4 0 2 2 0 014 0z" />
                                    </svg>
                                )}
                                <span className="hidden sm:inline">{t('wishlist.addToCart')}</span>
                            </button>

                            {/* Add to Cart Button (keeps in wishlist) */}
                            <button
                                onClick={() => handleAddToCart(product.id, getProductName(product))}
                                disabled={!product.isAvailable || isAddingThis}
                                className="p-2.5 border border-primary/30 text-primary rounded-lg hover:bg-primary/10 disabled:border-muted disabled:text-muted disabled:cursor-not-allowed transition-colors"
                                title={t('product.addToCart')}
                            >
                                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                        d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
                                </svg>
                            </button>
                        </div>
                    </div>
                );
            })}
        </div>
    );

    return (
        <Layout categories={categories}>
            <div dir={direction} className="min-h-[60vh] bg-background">
                <div className="container mx-auto px-4 py-12">
                    {/* Toast Message */}
                    {cartMessage && (
                        <div
                            className={`fixed top-20 right-4 z-50 px-4 py-3 rounded-lg shadow-lg animate-fade-in ${cartMessage.type === 'success'
                                ? 'bg-green-50 border border-green-200 text-green-700'
                                : 'bg-red-50 border border-red-200 text-red-600'
                                }`}
                        >
                            <div className="flex items-center gap-2">
                                {cartMessage.type === 'success' ? (
                                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                    </svg>
                                ) : (
                                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                    </svg>
                                )}
                                {cartMessage.text}
                            </div>
                        </div>
                    )}

                    {/* Header */}
                    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-8">
                        <div>
                            <h1 className="text-3xl font-bold text-text mb-2">
                                {t('wishlist.title')}
                            </h1>
                            <p className="text-muted">
                                {t('wishlist.count', { count: count })}
                            </p>
                        </div>

                        {count > 0 && (
                            <button
                                onClick={handleClearWishlist}
                                className="flex items-center gap-2 px-4 py-2 border border-red-200 text-red-600 rounded-lg hover:bg-red-50 transition-colors text-sm font-medium"
                            >
                                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                        d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                                </svg>
                                {t('wishlist.clear')}
                            </button>
                        )}
                    </div>

                    {/* Error State */}
                    {error && (
                        <div className="bg-red-50 border border-red-200 text-red-600 px-4 py-3 rounded-lg mb-6">
                            {error}
                        </div>
                    )}

                    {/* Loading State */}
                    {(isLoading || loadingProducts) && count > 0 && (
                        <div className="flex justify-center py-8">
                            <div className="w-8 h-8 border-4 border-primary/30 border-t-primary rounded-full animate-spin" />
                        </div>
                    )}

                    {/* Content */}
                    {!isLoading && count === 0 && renderEmptyState()}
                    {!isLoading && count > 0 && renderWishlistItems()}

                    {/* Back Link */}
                    <div className="mt-12 pt-8 border-t border-muted/20">
                        <Link
                            to="/account"
                            className="text-muted hover:text-text transition-colors flex items-center gap-2"
                        >
                            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                    d="M10 19l-7-7m0 0l7-7m-7 7h18" />
                            </svg>
                            {t('common.back')}
                        </Link>
                    </div>
                </div>
            </div>
        </Layout>
    );
};

/**
 * Wishlist Page with ProtectedRoute wrapper
 */
export const WishlistPage: React.FC = () => {
    return (
        <ProtectedRoute>
            <WishlistPageContent />
        </ProtectedRoute>
    );
};

export default WishlistPage;

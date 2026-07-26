/**
 * AF-28: Minimal Fast Checkout Landing Page Template
 * 
 * Distraction-free, conversion-focused single-card layout.
 * Designed for quick purchases — product image + essential info + CTA.
 * 
 * RENDERING ONLY — no cart/checkout logic.
 * CTA behavior is delegated via onBuyNow callback.
 */

import React from 'react';
import type { LandingTemplateProps } from './TemplateRegistry';
import '../AffiliateLandingPage.css';

export const MinimalFastCheckoutTemplate: React.FC<LandingTemplateProps> = ({
    data,
    selectedVariant,
    onVariantSelect,
    activeImageIndex,
    onBuyNow,
    funnelLoading,
    funnelError,
    formatPrice,
    effectivePrice,
    discountedPrice,
    displayPrice,
    isInStock,
    isDark,
}) => {
    const { product, affiliate, tenant, ctaConfig } = data;
    const activeImage = product.images[activeImageIndex] || product.mainImage;

    const ctaText = funnelLoading ? 'Processing...' : (ctaConfig?.buttonText || (isInStock ? 'Quick Checkout →' : 'Sold Out'));

    return (
        <div className={`landing-page ${isDark ? 'landing-page--dark' : 'landing-page--light'} minimal-template`}>
            {/* Ambient background glow */}
            <div className="minimal-bg-glow" />

            <div className="minimal-container">
                {/* Visual Block */}
                <div className="minimal-visual">
                    {activeImage ? (
                        <div className="minimal-image-wrap">
                            {product.hasActivePromotion && product.promotionBadge && (
                                <span className="minimal-promo-float">{product.promotionBadge}</span>
                            )}
                            <img src={activeImage} alt={product.name} />
                        </div>
                    ) : (
                        <div className="minimal-placeholder">
                            <svg width="64" height="64" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1} d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" /></svg>
                        </div>
                    )}

                    {/* Trust Badges */}
                    <div className="minimal-trust">
                        <span className="trust-badge">⚡ Instant Order</span>
                        <span className="trust-badge">✅ Verified Store</span>
                        <span className="trust-badge">🔒 Secure Pay</span>
                    </div>
                </div>

                {/* Conversion Block */}
                <div className="minimal-conversion">
                    {tenant?.storeName && (
                        <div className="minimal-store">{tenant.storeName}</div>
                    )}

                    <h1 className="minimal-product-name">{product.name}</h1>

                    {product.brand && (
                        <p className="minimal-brand">by {product.brand}</p>
                    )}

                    <div className="minimal-pricing">
                        <span className="minimal-price">{formatPrice(displayPrice)}</span>
                        {discountedPrice !== null && (
                            <span className="minimal-original-price">{formatPrice(effectivePrice)}</span>
                        )}
                        {product.hasActivePromotion && product.promotionBadge && (
                            <span className="minimal-badge">{product.promotionBadge}</span>
                        )}
                    </div>

                    {/* Description snippet */}
                    {product.description && (
                        <p className="minimal-description">
                            {product.description.substring(0, 150)}{product.description.length > 150 ? '...' : ''}
                        </p>
                    )}

                    {/* Minimal Variant Picker */}
                    {product.hasVariants && product.variants.length > 0 && (
                        <div className="minimal-variants">
                            <span className="minimal-variants__label">Select Option</span>
                            <div className="minimal-variant-grid">
                                {product.variants.map(v => (
                                    <button
                                        key={v.variantId}
                                        className={`minimal-variant-btn ${selectedVariant?.variantId === v.variantId ? 'active' : ''}`}
                                        onClick={() => onVariantSelect(v)}
                                        disabled={!v.inStock}
                                    >
                                        {v.sku || 'Option'}
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    {funnelError && (
                        <div className="lp-alert lp-alert--error">{funnelError}</div>
                    )}

                    {/* Hyper-focused CTA */}
                    <button
                        className="minimal-cta-btn"
                        onClick={onBuyNow}
                        disabled={funnelLoading || isInStock === false || (product.hasVariants && !selectedVariant)}
                    >
                        {funnelLoading && <span className="lp-cta__spinner" />}
                        {ctaText}
                    </button>

                    <div className="minimal-stock-status">
                        {isInStock === true && <span className="minimal-stock-ready">✓ In Stock – Ready to go</span>}
                        {isInStock === false && <span className="minimal-stock-out">Out of Stock</span>}
                        {isInStock === null && <span>Checking stock...</span>}
                    </div>

                    {/* Optional Affiliate Trust */}
                    {affiliate?.isValid && (
                        <div className="minimal-affiliate-trust">
                            Referred by <strong>{affiliate.displayName}</strong>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};

/**
 * AF-28: Classic Landing Page Template
 * 
 * The default landing page template with a product gallery on the left
 * and details/CTA on the right. Premium e-commerce aesthetic.
 * 
 * RENDERING ONLY — no cart/checkout logic.
 * CTA behavior is delegated via onBuyNow callback.
 */

import React from 'react';
import type { LandingTemplateProps } from './TemplateRegistry';
import '../AffiliateLandingPage.css';

export const ClassicTemplate: React.FC<LandingTemplateProps> = ({
    data,
    selectedVariant,
    onVariantSelect,
    activeImageIndex,
    onImageSelect,
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

    // AF-27B §7: Use backend-driven CTA text
    const ctaText = funnelLoading ? 'Processing...' : (ctaConfig?.buttonText || (isInStock ? 'Buy Now' : 'Out of Stock'));

    return (
        <div className={`landing-page ${isDark ? 'landing-page--dark' : 'landing-page--light'}`}>
            {/* Store Header Bar */}
            <div className="lp-topbar">
                <div className="lp-topbar__inner">
                    {tenant?.logoUrl ? (
                        <img src={tenant.logoUrl} alt={tenant.storeName} className="lp-topbar__logo" />
                    ) : (
                        <span className="lp-topbar__store-name">{tenant?.storeName}</span>
                    )}
                    {affiliate?.isValid && affiliate.displayName && (
                        <span className="lp-topbar__affiliate">
                            <span className="lp-topbar__aff-dot" />
                            Curated by {affiliate.displayName}
                        </span>
                    )}
                </div>
            </div>

            {/* Hero Section */}
            <div className="lp-hero">
                {/* Product Gallery */}
                <div className="lp-gallery lp-slide-left">
                    <div className="lp-gallery__main">
                        {/* Promotion Badge */}
                        {product.hasActivePromotion && product.promotionBadge && (
                            <span className="lp-gallery__badge">
                                {product.promotionBadge}
                            </span>
                        )}

                        {activeImage ? (
                            <img
                                src={activeImage}
                                alt={product.name}
                                loading="eager"
                            />
                        ) : (
                            <div className="lp-gallery__placeholder">
                                <svg width="80" height="80" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1}
                                        d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
                                </svg>
                            </div>
                        )}
                    </div>

                    {/* Thumbnails */}
                    {product.images.length > 1 && (
                        <div className="lp-gallery__thumbnails">
                            {product.images.slice(0, 6).map((img, i) => (
                                <button
                                    key={i}
                                    className={`lp-gallery__thumb ${activeImageIndex === i ? 'lp-gallery__thumb--active' : ''}`}
                                    onClick={() => onImageSelect(i)}
                                    aria-label={`View image ${i + 1}`}
                                >
                                    <img src={img} alt={`${product.name} ${i + 1}`} />
                                </button>
                            ))}
                        </div>
                    )}
                </div>

                {/* Product Details */}
                <div className="lp-details lp-slide-right">
                    {/* Category */}
                    {product.categoryName && (
                        <span className="lp-details__category lp-fade-in lp-delay-1">
                            {product.categoryName}
                        </span>
                    )}

                    {/* Product Name */}
                    <h1 className="lp-details__name lp-fade-in lp-delay-2" id="lp-product-name">
                        {product.name}
                    </h1>

                    {/* Brand */}
                    {product.brand && (
                        <p className="lp-details__brand lp-fade-in lp-delay-2">
                            by {product.brand}
                        </p>
                    )}

                    {/* Pricing */}
                    <div className="lp-pricing lp-fade-in lp-delay-3">
                        <span className="lp-pricing__current">
                            {formatPrice(displayPrice)}
                        </span>
                        {discountedPrice !== null && (
                            <>
                                <span className="lp-pricing__original">
                                    {formatPrice(effectivePrice)}
                                </span>
                                <span className="lp-pricing__savings">
                                    Save {formatPrice(effectivePrice - displayPrice)}
                                </span>
                            </>
                        )}
                    </div>

                    {/* Stock Status */}
                    <div className={`lp-stock ${isInStock === null ? 'lp-stock--unknown text-gray-500' : (isInStock ? 'lp-stock--in' : 'lp-stock--out')} lp-fade-in lp-delay-3`}>
                        <span className="lp-stock__dot" />
                        {isInStock === null ? 'Checking availability...' : (isInStock ? 'In Stock — Ready to Ship' : 'Currently Out of Stock')}
                    </div>

                    {/* Variant Selector */}
                    {product.hasVariants && product.variants.length > 0 && (
                        <div className="lp-variants lp-fade-in lp-delay-3">
                            {(() => {
                                const attrGroups = new Map<string, Set<string>>();
                                product.variants.forEach(v => {
                                    v.attributes.forEach(a => {
                                        if (!attrGroups.has(a.name)) {
                                            attrGroups.set(a.name, new Set());
                                        }
                                        attrGroups.get(a.name)!.add(a.value);
                                    });
                                });

                                if (attrGroups.size === 0) {
                                    return (
                                        <div>
                                            <span className="lp-variants__label">Options</span>
                                            <div className="lp-variants__options">
                                                {product.variants.map(v => (
                                                    <button
                                                        key={v.variantId}
                                                        className={`lp-variant-btn ${selectedVariant?.variantId === v.variantId ? 'lp-variant-btn--selected' : ''}`}
                                                        onClick={() => onVariantSelect(v)}
                                                        disabled={!v.inStock}
                                                    >
                                                        {v.sku || 'Option'}
                                                    </button>
                                                ))}
                                            </div>
                                        </div>
                                    );
                                }

                                return Array.from(attrGroups.entries()).map(([name, values]) => (
                                    <div key={name}>
                                        <span className="lp-variants__label">{name}</span>
                                        <div className="lp-variants__options">
                                            {Array.from(values).map(value => {
                                                const matchingVariant = product.variants.find(v =>
                                                    v.attributes.some(a => a.name === name && a.value === value)
                                                );
                                                const isSelected = selectedVariant?.attributes.some(
                                                    a => a.name === name && a.value === value
                                                );

                                                return (
                                                    <button
                                                        key={`${name}-${value}`}
                                                        className={`lp-variant-btn ${isSelected ? 'lp-variant-btn--selected' : ''}`}
                                                        onClick={() => matchingVariant && onVariantSelect(matchingVariant)}
                                                        disabled={!matchingVariant?.inStock}
                                                    >
                                                        {value}
                                                    </button>
                                                );
                                            })}
                                        </div>
                                    </div>
                                ));
                            })()}
                        </div>
                    )}

                    {/* CTA Button */}
                    <button
                        id="lp-buy-now"
                        className={`lp-cta lp-fade-in lp-delay-4 ${funnelLoading ? 'lp-cta--loading' : ''} ${isInStock === false ? 'lp-cta--oos' : ''}`}
                        onClick={onBuyNow}
                        disabled={isInStock === false || funnelLoading}
                    >
                        {funnelLoading ? (
                            <>
                                <span className="lp-cta__spinner" />
                                {ctaText}
                            </>
                        ) : (
                            ctaText
                        )}
                    </button>

                    {/* Funnel Error */}
                    {funnelError && (
                        <div className="lp-alert lp-alert--error" role="alert">
                            {funnelError}
                        </div>
                    )}

                    {/* Trust Badges */}
                    <div className="lp-trust lp-fade-in lp-delay-5">
                        <div className="lp-trust__item">
                            <span className="lp-trust__icon">🔒</span>
                            Secure Checkout
                        </div>
                        <div className="lp-trust__item">
                            <span className="lp-trust__icon">🚚</span>
                            Fast Delivery
                        </div>
                        <div className="lp-trust__item">
                            <span className="lp-trust__icon">↩️</span>
                            Easy Returns
                        </div>
                    </div>

                    {/* Description */}
                    {product.description && (
                        <div className="lp-description lp-fade-in lp-delay-5">
                            <h2 className="lp-description__title">About This Product</h2>
                            <p className="lp-description__text">{product.description}</p>
                        </div>
                    )}
                </div>
            </div>

            {/* Footer */}
            <footer className="lp-footer">
                <p>
                    Powered by <span className="lp-footer__store">{tenant.storeName}</span>
                    {affiliate.displayName && (
                        <span> · Shared by {affiliate.displayName}</span>
                    )}
                </p>
            </footer>
        </div>
    );
};

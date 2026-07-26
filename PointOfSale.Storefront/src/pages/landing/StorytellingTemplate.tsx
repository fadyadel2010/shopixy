/**
 * AF-28: Storytelling Landing Page Template
 * 
 * Narrative-driven, high-conversion layout designed for emotional engagement.
 * Uses full-width hero, scrolling narrative blocks, and a floating bottom CTA.
 * 
 * RENDERING ONLY — no cart/checkout logic.
 * CTA behavior is delegated via onBuyNow callback.
 */

import React from 'react';
import type { LandingTemplateProps } from './TemplateRegistry';
import '../AffiliateLandingPage.css';

export const StorytellingTemplate: React.FC<LandingTemplateProps> = ({
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

    const ctaText = funnelLoading ? 'Processing...' : (ctaConfig?.buttonText || (isInStock ? 'Secure Yours Now' : 'Out of Stock'));

    return (
        <div className={`landing-page ${isDark ? 'landing-page--dark' : 'landing-page--light'} storytelling-template`}>
            {/* Immersive Hero with Background Overlay */}
            <header className="story-hero">
                <div className="story-hero__bg-glow" />
                <div className="story-hero__content">
                    {tenant?.logoUrl && (
                        <div className="story-hero__logo">
                            <img src={tenant.logoUrl} alt={tenant.storeName} />
                        </div>
                    )}
                    <div className="story-hero__eyebrow">
                        {product.categoryName && <span>{product.categoryName}</span>}
                        {product.brand && <span className="story-hero__brand">by {product.brand}</span>}
                    </div>
                    <h1 className="story-hero__title">{product.name}</h1>
                    <p className="story-hero__subtitle">
                        {product.description?.substring(0, 120) || 'Discover something extraordinary'}
                    </p>

                    <div className="story-hero__price">
                        <span className="current-price">{formatPrice(displayPrice)}</span>
                        {discountedPrice !== null && (
                            <>
                                <span className="original-price">{formatPrice(effectivePrice)}</span>
                                <span className="story-hero__save-badge">
                                    Save {formatPrice(effectivePrice - displayPrice)}
                                </span>
                            </>
                        )}
                    </div>

                    <button
                        className="story-hero__cta"
                        onClick={onBuyNow}
                        disabled={funnelLoading || isInStock === false}
                    >
                        {funnelLoading && <span className="lp-cta__spinner" />}
                        {ctaText}
                    </button>

                    {affiliate?.isValid && (
                        <p className="story-hero__trust">
                            <span className="story-hero__trust-icon">⭐</span>
                            Recommended by {affiliate.displayName}
                        </p>
                    )}
                </div>
            </header>

            {/* Product Showcase — Split narrative block */}
            <section className="story-narrative">
                <div className="story-narrative__image-block">
                    {activeImage ? (
                        <div className="story-image-frame">
                            {product.hasActivePromotion && product.promotionBadge && (
                                <span className="story-promo-badge">{product.promotionBadge}</span>
                            )}
                            <img src={activeImage} alt={product.name} className="story-main-image" />
                        </div>
                    ) : (
                        <div className="story-placeholder">
                            <svg width="64" height="64" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1} d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" /></svg>
                        </div>
                    )}

                    {product.images.length > 1 && (
                        <div className="story-thumbnails">
                            {product.images.slice(0, 5).map((img, idx) => (
                                <button
                                    key={idx}
                                    className={`thumb-btn ${activeImageIndex === idx ? 'thumb-active' : ''}`}
                                    onClick={() => onImageSelect(idx)}
                                >
                                    <img src={img} alt={`View ${idx + 1}`} />
                                </button>
                            ))}
                        </div>
                    )}
                </div>

                <div className="story-narrative__text">
                    <h2 className="story-section-heading">Why You'll Love This</h2>
                    <p className="lp-prose">{product.description || 'No description available.'}</p>

                    {/* Variants if any */}
                    {product.hasVariants && product.variants.length > 0 && (
                        <div className="story-variants">
                            <h3 className="story-variants__heading">Choose Your Option</h3>
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
                    )}

                    {/* Stock Status */}
                    <div className={`story-stock ${isInStock === true ? 'story-stock--in' : isInStock === false ? 'story-stock--out' : ''}`}>
                        <span className="story-stock__dot" />
                        {isInStock === null ? 'Checking availability...' : (isInStock ? 'In Stock — Ready to Ship' : 'Currently Out of Stock')}
                    </div>
                </div>
            </section>

            {/* Benefits & Social Proof */}
            <section className="story-benefits">
                <h2 className="story-benefits__title">What Sets Us Apart</h2>
                <div className="benefit-grid">
                    <div className="benefit-item">
                        <div className="benefit-icon-wrap">
                            <span className="benefit-icon">✨</span>
                        </div>
                        <h4>Premium Quality</h4>
                        <p>Crafted to the highest standards with materials built to last.</p>
                    </div>
                    <div className="benefit-item">
                        <div className="benefit-icon-wrap">
                            <span className="benefit-icon">🚚</span>
                        </div>
                        <h4>Fast Shipping</h4>
                        <p>Ships directly from {tenant?.storeName || 'our warehouse'}.</p>
                    </div>
                    <div className="benefit-item">
                        <div className="benefit-icon-wrap">
                            <span className="benefit-icon">🔒</span>
                        </div>
                        <h4>Secure Checkout</h4>
                        <p>Your data and payment are always encrypted and protected.</p>
                    </div>
                    <div className="benefit-item">
                        <div className="benefit-icon-wrap">
                            <span className="benefit-icon">↩️</span>
                        </div>
                        <h4>Easy Returns</h4>
                        <p>Not satisfied? Return it hassle-free within 30 days.</p>
                    </div>
                </div>
            </section>

            {/* Final CTA */}
            <footer className="story-footer">
                {funnelError && (
                    <div className="lp-alert lp-alert--error">{funnelError}</div>
                )}
                <div className="story-footer__cta-wrap">
                    <p className="story-footer__heading">Ready to Get Yours?</p>
                    <button
                        className="story-footer__cta-btn"
                        onClick={onBuyNow}
                        disabled={funnelLoading || isInStock === false}
                    >
                        {funnelLoading && <span className="lp-cta__spinner" />}
                        {ctaText}
                    </button>
                    {isInStock === false && (
                        <p className="stock-notice error">Currently out of stock</p>
                    )}
                    {isInStock === true && (
                        <p className="stock-notice success">✓ In Stock & Ready to Ship</p>
                    )}
                </div>
                <div className="story-footer__attribution">
                    Powered by <strong>{tenant.storeName}</strong>
                    {affiliate.displayName && (
                        <span> · Shared by {affiliate.displayName}</span>
                    )}
                </div>
            </footer>
        </div>
    );
};

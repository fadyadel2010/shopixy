/**
 * AF-27A/27B: Affiliate Landing Page
 * 
 * Template-driven, tenant-aware, affiliate-scoped landing page.
 * Renders product information using backend-driven template selection.
 * 
 * ROUTE: /p/:referralCode/:slug
 * 
 * AF-27B §8: Template Abstraction Layer
 * Instead of hardcoded if/else, uses TemplateRegistry:
 *   1. Backend sends templateType: "Classic" | "Storytelling" | ...
 *   2. TemplateRegistry resolves to correct React component
 *   3. New templates = register + zero page changes
 * 
 * AF-27B §7: Backend-Driven CTA
 *   ctaConfig.buttonText → CTA label
 *   ctaConfig.singleProductMode → qty=1, no accumulation
 *   ctaConfig.includeAffiliateAttribution → false when affiliate suspended
 * 
 * AF-27B §2: Affiliate Suspension Graceful Degradation
 *   If affiliate.isValid === false → CTA proceeds without attribution
 *   Page still renders (SEO preserved)
 * 
 * ARCHITECTURAL BOUNDARIES:
 * - This is ORCHESTRATION LAYER only
 * - Template components handle RENDERING
 * - No cart state management (delegated to funnel endpoint)
 * - No checkout logic
 * - No commission logic
 * - Server validates everything
 */

import React, { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useSearchParams, Link } from 'react-router-dom'; // AF-27E: Re-added useSearchParams for template preview
import { fetchLandingPage, type LandingPageData, type LandingPageVariant } from '@/services/api/storefrontApi';
import { useAffiliateFunnel } from '@/hooks/useAffiliateFunnel';
import { getTemplate } from '@/pages/landing';
import './AffiliateLandingPage.css';

// Ensure templates are registered (side-effect import)
import '@/pages/landing';

// ============================================================================
// Landing Page Orchestrator
// ============================================================================

export const AffiliateLandingPage: React.FC = () => {
    const { referralCode, slug } = useParams<{ referralCode: string; slug: string }>();
    const [searchParams] = useSearchParams(); // AF-27E: Re-added for template preview
    const [data, setData] = useState<LandingPageData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<{ code: string; message: string } | null>(null);
    const [selectedVariant, setSelectedVariant] = useState<LandingPageVariant | null>(null);
    const [activeImageIndex, setActiveImageIndex] = useState(0);

    const { startFunnel, isLoading: funnelLoading, error: funnelError } = useAffiliateFunnel();

    // Detect theme preference
    const isDark = useMemo(() => {
        if (typeof window !== 'undefined') {
            return window.matchMedia('(prefers-color-scheme: dark)').matches;
        }
        return false;
    }, []);

    // ====================================================================
    // Fetch landing page data
    // ====================================================================
    useEffect(() => {
        const loadLandingPage = async () => {
            if (!referralCode || !slug) {
                setError({ code: 'INVALID_URL', message: 'Invalid landing page URL.' });
                setLoading(false);
                return;
            }

            try {
                setLoading(true);
                setError(null);
                const templateOverride = searchParams.get('template') || undefined; // AF-27E: Preview support
                const result = await fetchLandingPage(referralCode, slug, templateOverride);
                setData(result);

                // Auto-select default variant if variants exist
                if (result.product.hasVariants && result.product.variants.length > 0) {
                    const defaultVariant = result.product.variants.find(v => v.inStock)
                        || result.product.variants[0];
                    setSelectedVariant(defaultVariant);
                }

                // AF-28: LandingTemplateRendered analytics emission
                console.info('[AF-28:Analytics] LandingTemplateRendered', {
                    TemplateType: result.templateType,
                    ProductSlug: result.product.slug,
                    TenantSlug: window.location.host.split('.')[0]
                });
            } catch (err: any) {
                const code = err?.response?.data?.code || err?.response?.data?.Code || 'LOAD_ERROR';
                const message = err?.response?.data?.message
                    || err?.response?.data?.Message
                    || 'Unable to load this page. The product or link may no longer be available.';
                setError({ code, message });
            } finally {
                setLoading(false);
            }
        };

        loadLandingPage();
    }, [referralCode, slug]);

    // ====================================================================
    // CTA Handler — Starts funnel flow
    // AF-27B §2: Respects ctaConfig.includeAffiliateAttribution
    // ====================================================================
    const handleBuyNow = useCallback(async () => {
        if (!data || !referralCode) return; // AF-27E

        // AF-27B §2: If affiliate is invalid/suspended, don't include affiliate code
        const shouldIncludeAffiliate = data.ctaConfig?.includeAffiliateAttribution !== false
            && data.affiliate.isValid;

        await startFunnel({
            productId: data.product.productId,
            referralCode: shouldIncludeAffiliate ? referralCode : undefined, // AF-27E
            quantity: data.ctaConfig?.defaultQuantity || 1,
            variantId: selectedVariant?.variantId,
            singleProductMode: data.ctaConfig?.singleProductMode ?? true,
            templateType: data.templateType,
            productSlug: data.product.slug,
            tenantSlug: window.location.host.split('.')[0], // Observability only, NOT used for URL generation
        });
    }, [data, referralCode, selectedVariant, startFunnel]); // AF-27E

    // ====================================================================
    // Price formatting
    // ====================================================================
    const formatPrice = useCallback((price: number) => {
        const currency = (data?.product.currency || data?.tenant.currency || '').trim().toUpperCase();
        if (!currency) {
            return 'Currency unavailable';
        }

        try {
            return new Intl.NumberFormat('en-US', {
                style: 'currency',
                currency,
                minimumFractionDigits: 0,
                maximumFractionDigits: 2,
            }).format(price);
        } catch {
            return 'Currency unavailable';
        }
    }, [data]);

    // ====================================================================
    // Price calculations
    // ====================================================================
    const effectivePrice = useMemo(() => {
        if (selectedVariant) return selectedVariant.price;
        return data?.product.price || 0;
    }, [data, selectedVariant]);

    const discountedPrice = useMemo(() => {
        if (!data?.product.hasActivePromotion || !data.product.promotionDiscountValue) return null;

        const basePrice = effectivePrice;
        if (data.product.promotionDiscountType === 'percentage') {
            return basePrice * (1 - data.product.promotionDiscountValue / 100);
        } else if (data.product.promotionDiscountType === 'fixed') {
            return Math.max(0, basePrice - data.product.promotionDiscountValue);
        }
        return null;
    }, [data, effectivePrice]);

    const displayPrice = discountedPrice ?? effectivePrice;

    // AF-27C §2: Allows passing the 'Unknown' availability state (null) down to templates
    const isInStock = selectedVariant ? selectedVariant.inStock : (data?.product.inStock ?? null);

    // ====================================================================
    // Render: Loading skeleton
    // ====================================================================
    if (loading) {
        return (
            <div className={`landing-page ${isDark ? 'landing-page--dark' : 'landing-page--light'}`}>
                <div className="lp-skeleton">
                    <div className="lp-skeleton__pulse lp-skeleton__image" />
                    <div className="lp-skeleton__details">
                        <div className="lp-skeleton__pulse lp-skeleton__line lp-skeleton__line--sm" />
                        <div className="lp-skeleton__pulse lp-skeleton__line lp-skeleton__line--lg" />
                        <div className="lp-skeleton__pulse lp-skeleton__line lp-skeleton__line--xl" />
                        <div className="lp-skeleton__pulse lp-skeleton__line" />
                        <div className="lp-skeleton__pulse lp-skeleton__line" />
                        <div className="lp-skeleton__pulse lp-skeleton__btn" />
                    </div>
                </div>
            </div>
        );
    }

    // ====================================================================
    // Render: Error state
    // ====================================================================
    if (error || !data) {
        return (
            <div className={`landing-page ${isDark ? 'landing-page--dark' : 'landing-page--light'}`}>
                <div className="lp-error">
                    <div className="lp-error__icon">
                        {error?.code === 'AFFILIATE_NOT_FOUND' ? '🔗' :
                            error?.code === 'PRODUCT_NOT_FOUND' ? '📦' : '⚠️'}
                    </div>
                    <h1 className="lp-error__title">
                        {error?.code === 'AFFILIATE_NOT_FOUND' ? 'Link Not Found' :
                            error?.code === 'PRODUCT_NOT_FOUND' ? 'Product Not Available' :
                                'Something Went Wrong'}
                    </h1>
                    <p className="lp-error__message">{error?.message || 'This page could not be loaded.'}</p>
                    <Link to="/" className="lp-error__action">
                        Visit Store
                    </Link>
                </div>
            </div>
        );
    }

    // ====================================================================
    // AF-27B §8: Template Resolution via TemplateRegistry
    // Backend sends templateType, TemplateRegistry resolves component.
    // Fallback: Classic template if unknown type.
    // ====================================================================
    const TemplateComponent = getTemplate(data.templateType);

    if (!TemplateComponent) {
        // Defensive fallback — should never happen if Classic is registered
        return (
            <div className={`landing-page ${isDark ? 'landing-page--dark' : 'landing-page--light'}`}>
                <div className="lp-error">
                    <div className="lp-error__icon">⚙️</div>
                    <h1 className="lp-error__title">Template Not Available</h1>
                    <p className="lp-error__message">
                        The landing page template "{data.templateType}" is not yet supported.
                    </p>
                    <Link to="/" className="lp-error__action">
                        Visit Store
                    </Link>
                </div>
            </div>
        );
    }

    // ====================================================================
    // Render: Template-driven landing page
    // ====================================================================
    return (
        <>
            {/* SEO */}
            <title>{`${data.product.name} — ${data.tenant.storeName}`}</title>
            <meta name="description" content={data.product.description?.substring(0, 160) || data.product.name} />

            <TemplateComponent
                data={data}
                selectedVariant={selectedVariant}
                onVariantSelect={setSelectedVariant}
                activeImageIndex={activeImageIndex}
                onImageSelect={setActiveImageIndex}
                onBuyNow={handleBuyNow}
                funnelLoading={funnelLoading}
                funnelError={funnelError}
                formatPrice={formatPrice}
                effectivePrice={effectivePrice}
                discountedPrice={discountedPrice}
                displayPrice={displayPrice}
                isInStock={isInStock}
                isDark={isDark}
            />
        </>
    );
};

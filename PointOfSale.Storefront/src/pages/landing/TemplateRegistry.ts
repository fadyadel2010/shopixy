/**
 * AF-27B §8 / AF-28: Template Abstraction Layer
 * 
 * TemplateRegistry maps backend-driven TemplateType strings
 * to React components. Supports multi-template landing pages
 * with version-safe fallback to Classic:v1.
 * 
 * USAGE:
 *   const Template = getTemplate(data.templateType); // "Classic" → ClassicTemplate
 *   return <Template data={data} ... />;
 * 
 * ADDING A NEW TEMPLATE:
 *   1. Create component implementing LandingTemplateProps
 *   2. Register in TEMPLATE_REGISTRY below
 *   3. Backend sends new TemplateType string
 *   4. Frontend auto-resolves — zero code changes needed
 * 
 * ARCHITECTURAL BOUNDARIES:
 *   - Templates are RENDERING ONLY
 *   - No cart/checkout logic inside templates
 *   - CTA behavior delegated via onBuyNow callback
 *   - Variant selection delegated via callbacks
 */

import React from 'react';
import type { LandingPageData, LandingPageVariant } from '@/services/api/storefrontApi';

// ============================================================================
// Template Props Contract
// ============================================================================

/**
 * Props contract that all landing page templates must implement.
 * This ensures consistent behavior across different visual designs.
 */
export interface LandingTemplateProps {
    /** Full landing page data from backend */
    data: LandingPageData;
    /** Currently selected variant (null if no variants) */
    selectedVariant: LandingPageVariant | null;
    /** Callback when user selects a variant */
    onVariantSelect: (variant: LandingPageVariant) => void;
    /** Active gallery image index */
    activeImageIndex: number;
    /** Callback when user changes gallery image */
    onImageSelect: (index: number) => void;
    /** CTA buy now handler */
    onBuyNow: () => void;
    /** Whether funnel request is in progress */
    funnelLoading: boolean;
    /** Funnel error message (if any) */
    funnelError: string | null;
    /** Price formatting helper */
    formatPrice: (price: number) => string;
    /** Computed effective price */
    effectivePrice: number;
    /** Computed discounted price (null if no promotion) */
    discountedPrice: number | null;
    /** Computed display price (discounted or effective) */
    displayPrice: number;
    /** Whether current selection is in stock (null means checking availability) */
    isInStock: boolean | null;
    /** Theme preference */
    isDark: boolean;
}

// ============================================================================
// Template Registry
// ============================================================================

type TemplateComponent = React.ComponentType<LandingTemplateProps>;

/**
 * Registry mapping TemplateType strings to React components.
 * 
 * AF-28 templates:
 *   Classic:v1 — Standard product landing layout
 *   Storytelling:v1 — Narrative-driven high-conversion layout
 *   MinimalFastCheckout:v1 — Distraction-free quick purchase layout
 */
const TEMPLATE_REGISTRY: Record<string, TemplateComponent> = {
    // Templates are auto-registered via pages/landing/index.ts on module import.
    // Registered templates: Classic:v1, Storytelling:v1, MinimalFastCheckout:v1
};

/**
 * Register a template component in the registry.
 * Called during module initialization.
 */
export function registerTemplate(name: string, component: TemplateComponent): void {
    TEMPLATE_REGISTRY[name] = component;
}

/**
 * Get a template component by name.
 * Falls back to the first registered template or null.
 * 
 * @param templateType - Backend-driven template type string
 * @returns Template component or null if none registered
 */
export function getTemplate(templateType: string): TemplateComponent | null {
    if (TEMPLATE_REGISTRY[templateType]) return TEMPLATE_REGISTRY[templateType];

    // AF-27D: Safe Template Fallback
    // If e.g. "Storytelling:v2" is requested but not registered, fall back to safe default
    if (import.meta.env.DEV) {
        console.warn(`[TemplateRegistry] Template missing: ${templateType}. Falling back to Classic:v1`);
    }

    return TEMPLATE_REGISTRY['Classic:v1'] ?? TEMPLATE_REGISTRY['Classic'] ?? null;
}

/**
 * Check if a template is registered.
 */
export function hasTemplate(templateType: string): boolean {
    return templateType in TEMPLATE_REGISTRY;
}

/**
 * Get all registered template names.
 */
export function getRegisteredTemplates(): string[] {
    return Object.keys(TEMPLATE_REGISTRY);
}

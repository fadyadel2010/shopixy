/**
 * AF-12: Affiliate-Aware Link Component
 * 
 * Drop-in replacement for <a> tags that automatically preserves ?ref= parameter.
 * 
 * USAGE:
 * Replace: <a href="/products">Shop</a>
 * With:    <AffiliateLink href="/products">Shop</AffiliateLink>
 * 
 * FEATURES:
 * - Automatic ref parameter preservation
 * - Supports all standard anchor attributes
 * - Handles external links (no ref added)
 * - Prevents duplicate ref parameters
 */

import React, { AnchorHTMLAttributes } from 'react';
import { useBuildAffiliateUrl } from '@/hooks/useAffiliateNavigation';

interface AffiliateLinkProps extends AnchorHTMLAttributes<HTMLAnchorElement> {
    href: string;
    children: React.ReactNode;
}

export const AffiliateLink: React.FC<AffiliateLinkProps> = ({ 
    href, 
    children, 
    ...props 
}) => {
    const buildAffiliateUrl = useBuildAffiliateUrl();

    // Check if link is external
    const isExternal = href.startsWith('http://') || 
                      href.startsWith('https://') || 
                      href.startsWith('//');

    // Only add ref to internal links
    const finalHref = isExternal ? href : buildAffiliateUrl(href);

    return (
        <a href={finalHref} {...props}>
            {children}
        </a>
    );
};

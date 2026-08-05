/**
 * Phoenix Sports Theme — Footer (Phase T1.1)
 *
 * Layout identity: solid black footer (#202021) with white text, columns for
 * Shop, Pages, Contact, Social, and a Payment area. Placeholder content is used
 * for the payment row (no real payment-method logos are bundled yet).
 *
 * Runtime injects a localized footer presentation model. Only the black/white
 * presentation and the payment placeholder are Phoenix-specific.
 */

import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';
import { PHOENIX_CONTAINER, PHOENIX_EYEBROW, PHOENIX_FONT_BODY, PHOENIX_FONT_DISPLAY } from './phoenixTokens';

const PhoenixFooter: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, labels, navigation, sections, socialLinks, storeName } = model;
    const sectionLinks = sections.flatMap((section) => section.links);

    return (
        <footer className={`mt-auto bg-[#202021] ${PHOENIX_FONT_BODY} text-white`}>
            <div className={`${PHOENIX_CONTAINER} py-12 md:py-16`}>
                <div className="grid grid-cols-1 gap-10 md:grid-cols-[1.4fr,1fr,1fr]">
                    {/* Brand + contact */}
                    <div>
                        <p className={`${PHOENIX_EYEBROW} text-white/60`}>{labels.customerService}</p>
                        <h2 className={`mt-4 max-w-xl text-3xl font-light uppercase tracking-[0.18em] text-white md:text-4xl ${PHOENIX_FONT_DISPLAY}`}>
                            {storeName}
                        </h2>
                        <p className="mt-4 max-w-xl text-sm leading-7 text-white/70">
                            {copyright}
                        </p>
                        <p className="mt-3 text-sm text-white/70">{labels.contact}</p>
                    </div>

                    <div>
                        <p className={`${PHOENIX_EYEBROW} text-white/60`}>{labels.quickLinks}</p>
                        <div className="mt-4 space-y-3">
                            {navigation.links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.href}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className="block text-sm uppercase tracking-[0.18em] text-white/75 transition-colors hover:text-white"
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </div>
                        {sectionLinks.length > 0 ? (
                            <div className="mt-6 space-y-3 border-t border-white/10 pt-5">
                                <p className={`${PHOENIX_EYEBROW} text-white/45`}>
                                    {sections[0]?.title || labels.customerService}
                                </p>
                                {sectionLinks.map((link) => (
                                    <AffiliateLink
                                        key={`section-${link.key}`}
                                        href={link.href}
                                        target={link.openInNewTab ? '_blank' : undefined}
                                        rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                        className="block text-sm uppercase tracking-[0.18em] text-white/60 transition-colors hover:text-white"
                                    >
                                        {link.label}
                                    </AffiliateLink>
                                ))}
                            </div>
                        ) : null}
                    </div>

                    {/* Social */}
                    <div>
                        <p className={`${PHOENIX_EYEBROW} text-white/60`}>{labels.followUs}</p>
                        {socialLinks.length > 0 ? (
                            <div className="mt-4 flex flex-wrap gap-3">
                                {socialLinks.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.href}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        className="inline-flex items-center gap-2 rounded-full border border-white/20 px-3 py-2 text-xs uppercase tracking-[0.18em] text-white/70 transition-colors hover:border-white/50 hover:text-white"
                                    >
                                        <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                                        {link.platform}
                                    </AffiliateLink>
                                ))}
                            </div>
                        ) : (
                            <p className="mt-4 text-sm text-white/60">{labels.followUs}</p>
                        )}
                    </div>
                </div>

                {/* Payment area (placeholder) */}
                <div className="mt-10 border-t border-white/15 pt-6">
                    <p className={`${PHOENIX_EYEBROW} text-white/50`}>{labels.paymentMethods}</p>
                    <div className="mt-3 flex flex-wrap gap-2 text-xs uppercase tracking-[0.18em] text-white/60">
                        <span className="rounded-sm border border-white/20 px-3 py-1">Visa</span>
                        <span className="rounded-sm border border-white/20 px-3 py-1">Mastercard</span>
                        <span className="rounded-sm border border-white/20 px-3 py-1">Cash on Delivery</span>
                    </div>
                </div>
            </div>
        </footer>
    );
};

export default PhoenixFooter;

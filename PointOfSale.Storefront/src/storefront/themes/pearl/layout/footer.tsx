/**
 * Pearl Theme — Footer (Phase T1.1)
 *
 * Layout identity: luxury black footer with gold text, columns for Shop,
 * Pages, Contact, and Social content.
 *
 * Runtime injects a localized footer presentation model. Only the black/gold
 * presentation is Pearl-specific.
 */

import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';
import { PEARL_CONTAINER, PEARL_EYEBROW, PEARL_FONT_BODY } from './pearlTokens';

const PearlFooter: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, labels, navigation, sections, socialLinks, storeName } = model;

    return (
        <footer className={`mt-auto bg-black ${PEARL_FONT_BODY} text-[#C9A863]`}>
            <div className={`${PEARL_CONTAINER} py-12 md:py-16`}>
                <div className="grid grid-cols-1 gap-10 md:grid-cols-[1.4fr,1fr,1fr]">
                    {/* Brand + contact */}
                    <div>
                        <p className={PEARL_EYEBROW}>{labels.customerService}</p>
                        <h2 className="mt-4 max-w-xl text-3xl font-light uppercase tracking-[0.18em] text-[#C9A863] md:text-4xl">
                            {storeName}
                        </h2>
                        <p className="mt-4 max-w-xl text-sm leading-7 text-[#C9A863]/70">
                            {copyright}
                        </p>
                        <p className="mt-3 text-sm text-[#C9A863]/70">{labels.contact}</p>
                    </div>

                    <div>
                        <p className={`${PEARL_EYEBROW} text-[#C9A863]`}>{labels.quickLinks}</p>
                        <div className="mt-4 space-y-3">
                            {navigation.links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.href}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className="block min-w-0 break-words text-sm uppercase tracking-[0.18em] text-[#C9A863]/75 transition-colors hover:text-[#C9A863]"
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </div>
                        {sections.length > 0 ? (
                            <div className="mt-6 space-y-6 border-t border-[#C9A863]/10 pt-5">
                                {sections.map((section) => (
                                    <section key={section.key} className="min-w-0 space-y-3">
                                        <h3 className={`${PEARL_EYEBROW} text-[#C9A863]`}>{section.title}</h3>
                                        {section.links.map((link) => (
                                            <AffiliateLink
                                                key={link.key}
                                                href={link.href}
                                                target={link.openInNewTab ? '_blank' : undefined}
                                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                                className="block min-w-0 break-words text-sm uppercase tracking-[0.18em] text-[#C9A863] hover:underline"
                                            >
                                                {link.label}
                                            </AffiliateLink>
                                        ))}
                                    </section>
                                ))}
                            </div>
                        ) : null}
                    </div>

                    {/* Social */}
                    <div>
                        <p className={`${PEARL_EYEBROW} text-[#C9A863]`}>{labels.followUs}</p>
                        {socialLinks.length > 0 ? (
                            <div className="mt-4 flex flex-wrap gap-3">
                                {socialLinks.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.href}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        className="inline-flex items-center gap-2 rounded-full border border-[#C9A863]/30 px-3 py-2 text-xs uppercase tracking-[0.18em] text-[#C9A863]/70 transition-colors hover:border-[#C9A863]/60 hover:text-[#C9A863]"
                                    >
                                        <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                                        {link.platform}
                                    </AffiliateLink>
                                ))}
                            </div>
                        ) : (
                            <p className="mt-4 text-sm text-[#C9A863]">{labels.followUs}</p>
                        )}
                    </div>
                </div>
            </div>
        </footer>
    );
};

export default PearlFooter;

/**
 * Alizon Theme — Footer (Phase A1.1)
 *
 * Layout identity: black footer with white text, columns for Shop,
 * Pages, Contact, and Social content.
 *
 * Runtime injects a localized footer presentation model. Only the black/white
 * presentation is Alizon-specific.
 */

import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';
import { ALIZON_CONTAINER, ALIZON_EYEBROW, ALIZON_FONT_BODY } from './alizonTokens';

const AlizonFooter: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, labels, navigation, sections, socialLinks, storeName } = model;

    return (
        <footer className={`mt-auto bg-[#121212] ${ALIZON_FONT_BODY} text-white`}>
            <div className={`${ALIZON_CONTAINER} py-12 md:py-16`}>
                <div className="grid grid-cols-1 gap-10 md:grid-cols-[1.4fr,1fr,1fr]">
                    {/* Brand + contact */}
                    <div>
                        <p className={ALIZON_EYEBROW}>{labels.customerService}</p>
                        <h2 className="mt-4 max-w-xl text-3xl font-medium uppercase tracking-[0.18em] text-white md:text-4xl">
                            {storeName}
                        </h2>
                        <p className="mt-4 max-w-xl text-sm leading-7 text-white/70">
                            {copyright}
                        </p>
                        <p className="mt-3 text-sm text-white/70">{labels.contact}</p>
                    </div>

                    <div>
                        <p className={`${ALIZON_EYEBROW} text-white/60`}>{labels.quickLinks}</p>
                        <div className="mt-4 space-y-3">
                            {navigation.links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.href}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className="block min-w-0 break-words text-sm uppercase tracking-[0.18em] text-white/75 transition-colors hover:text-white"
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </div>
                        {sections.length > 0 ? (
                            <div className="mt-6 space-y-6 border-t border-white/10 pt-5">
                                {sections.map((section) => (
                                    <section key={section.key} className="min-w-0 space-y-3">
                                        <h3 className={`${ALIZON_EYEBROW} text-white/45`}>{section.title}</h3>
                                        {section.links.map((link) => (
                                            <AffiliateLink
                                                key={link.key}
                                                href={link.href}
                                                target={link.openInNewTab ? '_blank' : undefined}
                                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                                className="block min-w-0 break-words text-sm uppercase tracking-[0.18em] text-white/60 transition-colors hover:text-white"
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
                        <p className={`${ALIZON_EYEBROW} text-white/60`}>{labels.followUs}</p>
                        {socialLinks.length > 0 ? (
                            <div className="mt-4 flex flex-wrap gap-3">
                                {socialLinks.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.href}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        className="inline-flex items-center gap-2 rounded-full border border-white/30 px-3 py-2 text-xs uppercase tracking-[0.18em] text-white/70 transition-colors hover:border-white/60 hover:text-white"
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
            </div>
        </footer>
    );
};

export default AlizonFooter;

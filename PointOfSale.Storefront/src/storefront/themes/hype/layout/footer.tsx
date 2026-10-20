/**
 * Hype Theme — Footer (Phase H1.1)
 *
 * Layout identity: black footer with orange (#F06736) text, columns for Shop,
 * Pages, Contact, and Social content.
 *
 * Runtime injects a localized footer presentation model. Only the black/orange
 * presentation is Hype-specific.
 */

import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';
import { HYPE_CONTAINER, HYPE_EYEBROW, HYPE_FONT_BODY } from './hypeTokens';

const HypeFooter: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, labels, navigation, sections, socialLinks, storeName } = model;

    return (
        <footer className={`mt-auto bg-secondary ${HYPE_FONT_BODY} text-on-secondary`}>
            <div className={`${HYPE_CONTAINER} py-12 md:py-16`}>
                <div className="grid grid-cols-1 gap-10 md:grid-cols-[1.4fr,1fr,1fr]">
                    {/* Brand + contact */}
                    <div>
                        <p className={HYPE_EYEBROW}>{labels.customerService}</p>
                        <h2 className="mt-4 max-w-xl text-3xl font-medium uppercase tracking-[0.18em] text-on-secondary md:text-4xl">
                            {storeName}
                        </h2>
                        <p className="mt-4 max-w-xl text-sm leading-7 text-on-secondary">
                            {copyright}
                        </p>
                        <p className="mt-3 text-sm text-on-secondary">{labels.contact}</p>
                    </div>

                    <div>
                        <p className={`${HYPE_EYEBROW} text-on-secondary`}>{labels.quickLinks}</p>
                        <div className="mt-4 space-y-3">
                            {navigation.links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.href}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className="block min-w-0 break-words text-sm uppercase tracking-[0.18em] text-on-secondary hover:underline"
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </div>
                        {sections.length > 0 ? (
                            <div className="mt-6 space-y-6 border-t border-on-secondary/50 pt-5">
                                {sections.map((section) => (
                                    <section key={section.key} className="min-w-0 space-y-3">
                                        <h3 className={`${HYPE_EYEBROW} text-on-secondary`}>{section.title}</h3>
                                        {section.links.map((link) => (
                                            <AffiliateLink
                                                key={link.key}
                                                href={link.href}
                                                target={link.openInNewTab ? '_blank' : undefined}
                                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                                className="block min-w-0 break-words text-sm uppercase tracking-[0.18em] text-on-secondary hover:underline"
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
                        <p className={`${HYPE_EYEBROW} text-on-secondary`}>{labels.followUs}</p>
                        {socialLinks.length > 0 ? (
                            <div className="mt-4 flex flex-wrap gap-3">
                                {socialLinks.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.href}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        className="inline-flex items-center gap-2 rounded-full border border-current px-3 py-2 text-xs uppercase tracking-[0.18em] text-on-secondary hover:underline"
                                    >
                                        <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                                        {link.platform}
                                    </AffiliateLink>
                                ))}
                            </div>
                        ) : (
                            <p className="mt-4 text-sm text-on-secondary">{labels.followUs}</p>
                        )}
                    </div>
                </div>
            </div>
        </footer>
    );
};

export default HypeFooter;

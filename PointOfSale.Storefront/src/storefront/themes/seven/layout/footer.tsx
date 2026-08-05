import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';

const SevenFooter: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, labels, navigation, sections, socialLinks, storeName } = model;
    const sectionLinks = sections.flatMap((section) => section.links);

    return (
        <footer className="mt-auto bg-[#9D7877] text-white">
            <div className="mx-auto max-w-7xl px-4 py-12 md:py-16">
                <div className="grid gap-10 md:grid-cols-[1.3fr,1fr]">
                    <div>
                        <h2 className="text-3xl font-medium uppercase tracking-[-0.02em] md:text-4xl">
                            {storeName || 'La Femmea'}
                        </h2>
                        <p className="mt-4 max-w-xl text-sm leading-7 text-white/80">
                            {copyright}
                        </p>
                    </div>

                    <div className="grid gap-8 md:grid-cols-2">
                        <div>
                            <p className="text-xs uppercase tracking-[0.3em] text-white/50">{labels.quickLinks}</p>
                            <div className="mt-4 space-y-3">
                                {navigation.links.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.href}
                                        target={link.openInNewTab ? '_blank' : undefined}
                                        rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                        className="block text-sm uppercase tracking-[0.16em] text-white/75 transition-colors hover:text-white"
                                    >
                                        {link.label}
                                    </AffiliateLink>
                                ))}
                            </div>
                            {sectionLinks.length > 0 ? (
                                <div className="mt-6 space-y-3 border-t border-white/10 pt-5">
                                    <p className="text-xs uppercase tracking-[0.3em] text-white/40">
                                        {sections[0]?.title || labels.customerService}
                                    </p>
                                    {sectionLinks.map((link) => (
                                        <AffiliateLink
                                            key={`section-${link.key}`}
                                            href={link.href}
                                            target={link.openInNewTab ? '_blank' : undefined}
                                            rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                            className="block text-sm uppercase tracking-[0.16em] text-white/55 transition-colors hover:text-white"
                                        >
                                            {link.label}
                                        </AffiliateLink>
                                    ))}
                                </div>
                            ) : null}
                        </div>
                        <div>
                            <p className="text-xs uppercase tracking-[0.3em] text-white/50">Connect</p>
                            {socialLinks.length > 0 ? (
                                <div className="mt-4 flex flex-wrap gap-3">
                                    {socialLinks.map((link) => (
                                        <AffiliateLink
                                            key={link.key}
                                            href={link.href}
                                            target="_blank"
                                            rel="noopener noreferrer"
                                            className="inline-flex items-center gap-2 rounded-full border border-white/10 px-3 py-2 text-xs uppercase tracking-[0.16em] text-white/70 transition-colors hover:border-white/25 hover:text-white"
                                        >
                                            <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                                            {link.platform}
                                        </AffiliateLink>
                                    ))}
                                </div>
                            ) : (
                                <div className="mt-4 space-y-3 text-sm text-white/60">
                                    <p>{labels.customerService}</p>
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            </div>
        </footer>
    );
};

export default SevenFooter;

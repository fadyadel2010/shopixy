import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';

const SevenFooter: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, labels, navigation, sections, socialLinks, storeName } = model;

    return (
        <footer className="mt-auto bg-primary text-on-primary">
            <div className="mx-auto max-w-7xl px-4 py-12 md:py-16">
                <div className="grid gap-10 md:grid-cols-[1.3fr,1fr]">
                    <div>
                        <h2 className="text-3xl font-medium uppercase tracking-[-0.02em] md:text-4xl">
                            {storeName}
                        </h2>
                        <p className="mt-4 max-w-xl text-sm leading-7 text-on-primary">
                            {copyright}
                        </p>
                    </div>

                    <div className="grid gap-8 md:grid-cols-2">
                        <div>
                            <p className="text-xs uppercase tracking-[0.3em] text-on-primary">{labels.quickLinks}</p>
                            <div className="mt-4 space-y-3">
                                {navigation.links.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.href}
                                        target={link.openInNewTab ? '_blank' : undefined}
                                        rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                        className="block min-w-0 break-words text-sm uppercase tracking-[0.16em] text-on-primary hover:underline"
                                    >
                                        {link.label}
                                    </AffiliateLink>
                                ))}
                            </div>
                            {sections.length > 0 ? (
                                <div className="mt-6 space-y-6 border-t border-on-primary/50 pt-5">
                                    {sections.map((section) => (
                                        <section key={section.key} className="min-w-0 space-y-3">
                                            <h3 className="text-xs uppercase tracking-[0.3em] text-on-primary">{section.title}</h3>
                                            {section.links.map((link) => (
                                                <AffiliateLink
                                                    key={link.key}
                                                    href={link.href}
                                                    target={link.openInNewTab ? '_blank' : undefined}
                                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                                    className="block min-w-0 break-words text-sm uppercase tracking-[0.16em] text-on-primary hover:underline"
                                                >
                                                    {link.label}
                                                </AffiliateLink>
                                            ))}
                                        </section>
                                    ))}
                                </div>
                            ) : null}
                        </div>
                        <div>
                            <p className="text-xs uppercase tracking-[0.3em] text-on-primary">{labels.followUs}</p>
                            {socialLinks.length > 0 ? (
                                <div className="mt-4 flex flex-wrap gap-3">
                                    {socialLinks.map((link) => (
                                        <AffiliateLink
                                            key={link.key}
                                            href={link.href}
                                            target="_blank"
                                            rel="noopener noreferrer"
                                            className="inline-flex items-center gap-2 rounded-full border border-current px-3 py-2 text-xs uppercase tracking-[0.16em] text-on-primary hover:underline"
                                        >
                                            <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                                            {link.platform}
                                        </AffiliateLink>
                                    ))}
                                </div>
                            ) : (
                                <div className="mt-4 space-y-3 text-sm text-on-primary">
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

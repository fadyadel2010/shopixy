import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';

const MinimalFooter: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, navigation, sections, socialLinks, storeName } = model;

    return (
        <footer className="mt-auto border-t border-border bg-background text-muted">
            <div className="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-10 md:flex-row md:items-center md:justify-between">
                <div>
                    <p className="text-sm uppercase tracking-[0.22em] text-muted">{storeName}</p>
                    <p className="mt-2 text-sm text-muted">{copyright}</p>
                </div>

                <div className="flex flex-col items-start gap-4 md:items-end">
                    <nav className="flex flex-wrap gap-x-6 gap-y-3" aria-label="Minimal footer navigation">
                        {navigation.links.map((link) => (
                            <AffiliateLink
                                key={link.key}
                                href={link.href}
                                target={link.openInNewTab ? '_blank' : undefined}
                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                className="min-w-0 break-words text-sm text-muted transition-colors hover:text-text"
                            >
                                {link.label}
                            </AffiliateLink>
                        ))}
                    </nav>

                    {sections.length > 0 ? (
                        <div className="flex flex-wrap items-start gap-x-8 gap-y-5">
                            {sections.map((section) => (
                                <section key={section.key} className="min-w-0">
                                    <h3 className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">{section.title}</h3>
                                    <div className="mt-2 flex flex-wrap gap-x-6 gap-y-3">
                                        {section.links.map((link) => (
                                            <AffiliateLink
                                                key={link.key}
                                                href={link.href}
                                                target={link.openInNewTab ? '_blank' : undefined}
                                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                                className="min-w-0 break-words text-sm text-muted transition-colors hover:text-text"
                                            >
                                                {link.label}
                                            </AffiliateLink>
                                        ))}
                                    </div>
                                </section>
                            ))}
                        </div>
                    ) : null}

                    {socialLinks.length > 0 ? (
                        <div className="flex flex-wrap items-center gap-3">
                            {socialLinks.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.href}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-muted transition-colors hover:border-text hover:text-text"
                                >
                                    <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                                    {link.platform}
                                </AffiliateLink>
                            ))}
                        </div>
                    ) : null}
                </div>
            </div>
        </footer>
    );
};

export default MinimalFooter;

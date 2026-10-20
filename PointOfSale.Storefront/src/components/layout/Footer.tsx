import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from './FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';

export const Footer: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, labels, navigation, sections, socialLinks, storeName } = model;

    return (
        <footer className="mt-auto bg-secondary text-white">
            <div className="container mx-auto px-4 py-10 md:py-12">
                <div className="grid grid-cols-1 gap-8 md:grid-cols-[minmax(0,1.2fr)_minmax(0,2fr)]">
                    <div>
                        <h3 className="mb-4 text-xl font-bold tracking-tight">{storeName}</h3>
                        <p className="max-w-xs text-sm leading-relaxed text-white/60">
                            {copyright}
                        </p>

                        {socialLinks.length > 0 ? (
                            <div className="mt-6 flex flex-wrap gap-3">
                                {socialLinks.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.href}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        className="inline-flex items-center gap-2 rounded-full border border-white/15 bg-white/5 px-4 py-2 text-sm text-white/75 transition hover:border-primary/50 hover:bg-white/10 hover:text-white"
                                    >
                                        <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                                        <span>{link.platform}</span>
                                    </AffiliateLink>
                                ))}
                            </div>
                        ) : null}
                    </div>

                    <div className="grid grid-cols-1 gap-8 sm:grid-cols-2 xl:grid-cols-3">
                        {navigation.links.length > 0 ? (
                            <div>
                                <h4 className="mb-4 text-sm font-semibold uppercase tracking-wider text-white/90">
                                    {labels.quickLinks}
                                </h4>
                                <ul className="space-y-3">
                                    {navigation.links.map((link) => (
                                        <li key={link.key}>
                                            <AffiliateLink
                                                href={link.href}
                                                target={link.openInNewTab ? '_blank' : undefined}
                                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                                className="inline-flex min-w-0 items-center gap-2 break-words text-sm text-white/60 transition-colors hover:text-white"
                                            >
                                                {link.label}
                                            </AffiliateLink>
                                        </li>
                                    ))}
                                </ul>
                            </div>
                        ) : null}

                        {sections.length > 0 ? (
                            sections.map((section) => (
                                <div key={section.key}>
                                    <h4 className="mb-4 text-sm font-semibold uppercase tracking-wider text-white/90">
                                        {section.title}
                                    </h4>
                                    <ul className="space-y-3">
                                        {section.links.map((link) => (
                                            <li key={link.key}>
                                                <AffiliateLink
                                                    href={link.href}
                                                    target={link.openInNewTab ? '_blank' : undefined}
                                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                                    className="inline-flex min-w-0 items-center gap-2 break-words text-sm text-white/60 transition-colors hover:text-white"
                                                >
                                                    <span className="h-1.5 w-1.5 rounded-full bg-primary opacity-0 transition-opacity group-hover:opacity-100" />
                                                    {link.label}
                                                </AffiliateLink>
                                            </li>
                                        ))}
                                    </ul>
                                </div>
                            ))
                        ) : (
                            <div>
                                <h4 className="mb-4 text-sm font-semibold uppercase tracking-wider text-white/90">
                                    {labels.customerService}
                                </h4>
                                <p className="text-sm text-white/50">{labels.loading}</p>
                            </div>
                        )}
                    </div>
                </div>
            </div>

            <div className="border-t border-white/10">
                <div className="container mx-auto px-4 py-6">
                    <div className="flex flex-col items-center justify-between gap-4 sm:flex-row">
                        <p className="text-center text-sm text-white/70 sm:text-left">
                            {copyright}
                        </p>
                        <div className="flex flex-wrap items-center justify-center gap-4 text-xs text-white/40">
                            {socialLinks.map((link) => (
                                <AffiliateLink
                                    key={`bottom-${link.key}`}
                                    href={link.href}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="transition-colors hover:text-white/70"
                                >
                                    {link.platform}
                                </AffiliateLink>
                            ))}
                        </div>
                    </div>
                </div>
            </div>
        </footer>
    );
};

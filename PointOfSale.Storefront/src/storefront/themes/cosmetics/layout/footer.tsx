import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import type { FooterPresentationProps } from '@/storefront/presentation/models';
import { COSMETICS_FONT } from './cosmeticsTokens';

const CosmeticsFooter: React.FC<FooterPresentationProps> = ({ model }) => {
    const { copyright, labels, navigation, sections, socialLinks, storeName } = model;
    const displayStoreName = storeName?.trim() ?? '';

    return (
        <footer className={`${COSMETICS_FONT} mt-auto bg-[#E7DFDB] px-6 py-12 text-[#D15F61] md:px-[90px] md:py-[72px]`}>
            <div className="grid gap-10 md:grid-cols-[1.1fr_repeat(3,minmax(0,1fr))]">
                <div>
                    {displayStoreName ? (
                        <>
                            <h2 className="text-3xl font-light tracking-[-0.03em]">{displayStoreName}</h2>
                            <p className="mt-4 max-w-sm text-sm leading-7 text-[#D15F61]/80">
                                {copyright}
                            </p>
                        </>
                    ) : null}
                    <div className="mt-5 flex flex-wrap gap-3">
                        {socialLinks.map((link) => (
                            <AffiliateLink
                                key={link.key}
                                href={link.href}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="inline-flex h-10 w-10 items-center justify-center border border-[#D15F61]/30"
                                aria-label={link.platform}
                            >
                                <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                            </AffiliateLink>
                        ))}
                    </div>
                </div>

                {navigation.links.length > 0 ? (
                    <div>
                        <h3 className="text-xs font-semibold uppercase tracking-[0.18em]">{labels.quickLinks}</h3>
                        <div className="mt-5 space-y-3">
                            {navigation.links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.href}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className="block text-sm text-[#D15F61]/85 transition-opacity hover:opacity-70"
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </div>
                    </div>
                ) : null}

                {sections.map((section) => (
                    <div key={section.key}>
                        <h3 className="text-xs font-semibold uppercase tracking-[0.18em]">{section.title}</h3>
                        <div className="mt-5 space-y-3">
                            {section.links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.href}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className="block text-sm text-[#D15F61]/85 transition-opacity hover:opacity-70"
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </div>
                    </div>
                ))}
            </div>
        </footer>
    );
};

export default CosmeticsFooter;

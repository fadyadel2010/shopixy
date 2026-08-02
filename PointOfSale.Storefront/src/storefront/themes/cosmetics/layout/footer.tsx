import React, { useEffect, useMemo } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import { useStoreConfigStore, selectStoreName } from '@/store';
import { useFooterStore, selectFooterSections, selectFooterSocialLinks } from '@/stores/footerStore';
import { useTranslation } from '@/i18n';
import { COSMETICS_FONT } from './cosmeticsTokens';

const CosmeticsFooter: React.FC = () => {
    const { t, language } = useTranslation();
    const storeName = useStoreConfigStore(selectStoreName);
    const loadFooter = useFooterStore((state) => state.loadFooter);
    const sections = useFooterStore(selectFooterSections);
    const socialLinks = useFooterStore(selectFooterSocialLinks);
    const currentYear = new Date().getFullYear();
    const displayStoreName = storeName?.trim() ?? '';

    useEffect(() => {
        loadFooter();
    }, [loadFooter]);

    const resolvedSections = useMemo(() => {
        return sections.map((section) => ({
            title: section.title,
            links: section.links.map((link) => ({
                label: language === 'ar' && link.labelAr ? link.labelAr : link.label,
                url: link.url,
                openInNewTab: link.openInNewTab,
            })),
        }));
    }, [sections, language]);

    return (
        <footer className={`${COSMETICS_FONT} mt-auto bg-[#E7DFDB] px-6 py-12 text-[#D15F61] md:px-[90px] md:py-[72px]`}>
            <div className="grid gap-10 md:grid-cols-[1.1fr_repeat(3,minmax(0,1fr))]">
                <div>
                    {displayStoreName ? (
                        <>
                            <h2 className="text-3xl font-light tracking-[-0.03em]">{displayStoreName}</h2>
                            <p className="mt-4 max-w-sm text-sm leading-7 text-[#D15F61]/80">
                                {t('footer.copyright', { year: currentYear, storeName: displayStoreName })}
                            </p>
                        </>
                    ) : null}
                    <div className="mt-5 flex flex-wrap gap-3">
                        {socialLinks.map((link) => (
                            <AffiliateLink
                                key={`${link.platform}-${link.url}`}
                                href={link.url}
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

                {resolvedSections.slice(0, 3).map((section) => (
                    <div key={section.title}>
                        <h3 className="text-xs font-semibold uppercase tracking-[0.18em]">{section.title}</h3>
                        <div className="mt-5 space-y-3">
                            {section.links.map((link) => (
                                <AffiliateLink
                                    key={`${link.url}-${link.label}`}
                                    href={link.url}
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

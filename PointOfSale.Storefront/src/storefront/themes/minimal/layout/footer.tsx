import React, { useEffect, useMemo } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import { useStoreConfigStore, selectStoreName } from '@/store';
import { useFooterStore, selectFooterSections, selectFooterSocialLinks } from '@/stores/footerStore';
import { useTranslation } from '@/i18n';

const MinimalFooter: React.FC = () => {
    const { t, language } = useTranslation();
    const storeName = useStoreConfigStore(selectStoreName);
    const loadFooter = useFooterStore((state) => state.loadFooter);
    const sections = useFooterStore(selectFooterSections);
    const socialLinks = useFooterStore(selectFooterSocialLinks);
    const currentYear = new Date().getFullYear();

    useEffect(() => {
        loadFooter();
    }, [loadFooter]);

    const links = useMemo(() => {
        return sections.flatMap((section) => section.links).map((link, index) => ({
            key: `${link.url || link.label}-${index}`,
            label: language === 'ar' && link.labelAr ? link.labelAr : link.label,
            url: link.url,
            openInNewTab: link.openInNewTab
        }));
    }, [sections, language]);

    return (
        <footer className="mt-auto border-t border-slate-200 bg-white">
            <div className="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-10 md:flex-row md:items-center md:justify-between">
                <div>
                    <p className="text-sm uppercase tracking-[0.22em] text-slate-500">{storeName}</p>
                    <p className="mt-2 text-sm text-slate-500">{t('footer.copyright', { year: currentYear, storeName })}</p>
                </div>

                <div className="flex flex-col items-start gap-4 md:items-end">
                    <nav className="flex flex-wrap gap-x-6 gap-y-3" aria-label="Minimal footer navigation">
                        {links.map((link) => (
                            <AffiliateLink
                                key={link.key}
                                href={link.url}
                                target={link.openInNewTab ? '_blank' : undefined}
                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                className="text-sm text-slate-500 transition-colors hover:text-slate-900"
                            >
                                {link.label}
                            </AffiliateLink>
                        ))}
                    </nav>

                    {socialLinks.length > 0 ? (
                        <div className="flex flex-wrap items-center gap-3">
                            {socialLinks.map((link) => (
                                <AffiliateLink
                                    key={`${link.platform}-${link.url}`}
                                    href={link.url}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="inline-flex items-center gap-2 rounded-full border border-slate-200 px-3 py-2 text-xs font-medium text-slate-600 transition-colors hover:border-slate-300 hover:text-slate-900"
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

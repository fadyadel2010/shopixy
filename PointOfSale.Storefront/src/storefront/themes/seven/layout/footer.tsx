import React, { useEffect, useMemo } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import { useStoreConfigStore, selectStoreName } from '@/store';
import { useFooterStore, selectFooterSections, selectFooterSocialLinks } from '@/stores/footerStore';
import { useTranslation } from '@/i18n';

const SevenFooter: React.FC = () => {
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
        <footer className="mt-auto bg-[#9D7877] text-white">
            <div className="mx-auto max-w-7xl px-4 py-12 md:py-16">
                <div className="grid gap-10 md:grid-cols-[1.3fr,1fr]">
                    <div>
                        <h2 className="text-3xl font-medium uppercase tracking-[-0.02em] md:text-4xl">
                            {storeName || 'La Femmea'}
                        </h2>
                        <p className="mt-4 max-w-xl text-sm leading-7 text-white/80">
                            {t('footer.copyright', { year: currentYear, storeName })}
                        </p>
                    </div>

                    <div className="grid gap-8 md:grid-cols-2">
                        <div>
                            <p className="text-xs uppercase tracking-[0.3em] text-white/50">{sections[0]?.title || 'Navigate'}</p>
                            <div className="mt-4 space-y-3">
                                {links.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.url}
                                        target={link.openInNewTab ? '_blank' : undefined}
                                        rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                        className="block text-sm uppercase tracking-[0.16em] text-white/75 transition-colors hover:text-white"
                                    >
                                        {link.label}
                                    </AffiliateLink>
                                ))}
                            </div>
                        </div>
                        <div>
                            <p className="text-xs uppercase tracking-[0.3em] text-white/50">Connect</p>
                            {socialLinks.length > 0 ? (
                                <div className="mt-4 flex flex-wrap gap-3">
                                    {socialLinks.map((link) => (
                                        <AffiliateLink
                                            key={`${link.platform}-${link.url}`}
                                            href={link.url}
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
                                    <p>{t('footer.customerService')}</p>
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

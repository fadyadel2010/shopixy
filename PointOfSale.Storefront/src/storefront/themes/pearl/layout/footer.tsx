/**
 * Pearl Theme — Footer (Phase T1.1)
 *
 * Layout identity: luxury black footer with gold text, columns for Shop,
 * Pages, Contact, Social, and a Payment area. Placeholder content is used for
 * the payment row (no real payment-method logos are bundled yet).
 *
 * REUSE: Reuses the platform footer infrastructure instead of reimplementing
 * data access:
 *   - useFooterStore          (shop/pages sections + social links from CMS)
 *   - FooterSocialIcon        (platform social icon resolver)
 *   - useStoreConfigStore     (store name)
 *   - AffiliateLink           (themed links)
 *   - useTranslation          (i18n + RTL)
 * Only the black/gold presentation and the payment placeholder are Pearl-specific.
 */

import React, { useEffect, useMemo } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { FooterSocialIcon } from '@/components/layout/FooterSocialIcon';
import { useStoreConfigStore, selectStoreName } from '@/store';
import { useFooterStore, selectFooterSections, selectFooterSocialLinks } from '@/stores/footerStore';
import { useTranslation } from '@/i18n';
import { PEARL_CONTAINER, PEARL_EYEBROW, PEARL_FONT_BODY, PEARL_SECTION_PADDING } from './pearlTokens';

const PearlFooter: React.FC = () => {
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
            openInNewTab: link.openInNewTab,
        }));
    }, [sections, language]);

    return (
        <footer className={`mt-auto bg-black ${PEARL_FONT_BODY} text-[#C9A863]`}>
            <div className={`${PEARL_CONTAINER} ${PEARL_SECTION_PADDING}`}>
                <div className="grid grid-cols-1 gap-10 md:grid-cols-[1.4fr,1fr,1fr]">
                    {/* Brand + contact */}
                    <div>
                        <p className={PEARL_EYEBROW}>{t('footer.customerService')}</p>
                        <h2 className="mt-4 max-w-xl text-3xl font-light uppercase tracking-[0.18em] text-[#C9A863] md:text-4xl">
                            {storeName}
                        </h2>
                        <p className="mt-4 max-w-xl text-sm leading-7 text-[#C9A863]/70">
                            {t('footer.copyright', { year: currentYear, storeName })}
                        </p>
                        <p className="mt-3 text-sm text-[#C9A863]/70">{t('footer.contact')}</p>
                    </div>

                    {/* Shop links */}
                    <div>
                        <p className={`${PEARL_EYEBROW} text-[#C9A863]/60`}>
                            {sections[0]?.title || t('footer.quickLinks')}
                        </p>
                        <div className="mt-4 space-y-3">
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className="block text-sm uppercase tracking-[0.18em] text-[#C9A863]/75 transition-colors hover:text-[#C9A863]"
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </div>
                    </div>

                    {/* Social */}
                    <div>
                        <p className={`${PEARL_EYEBROW} text-[#C9A863]/60`}>{t('footer.followUs')}</p>
                        {socialLinks.length > 0 ? (
                            <div className="mt-4 flex flex-wrap gap-3">
                                {socialLinks.map((link) => (
                                    <AffiliateLink
                                        key={`${link.platform}-${link.url}`}
                                        href={link.url}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        className="inline-flex items-center gap-2 rounded-full border border-[#C9A863]/30 px-3 py-2 text-xs uppercase tracking-[0.18em] text-[#C9A863]/70 transition-colors hover:border-[#C9A863]/60 hover:text-[#C9A863]"
                                    >
                                        <FooterSocialIcon platform={link.icon || link.platform} className="h-4 w-4" />
                                        {link.platform}
                                    </AffiliateLink>
                                ))}
                            </div>
                        ) : (
                            <p className="mt-4 text-sm text-[#C9A863]/60">{t('footer.followUs')}</p>
                        )}
                    </div>
                </div>

                {/* Payment area (placeholder) */}
                <div className="mt-10 border-t border-[#C9A863]/15 pt-6">
                    <p className={`${PEARL_EYEBROW} text-[#C9A863]/50`}>{t('footer.paymentMethods')}</p>
                    <div className="mt-3 flex flex-wrap gap-2 text-xs uppercase tracking-[0.18em] text-[#C9A863]/60">
                        <span className="rounded-sm border border-[#C9A863]/20 px-3 py-1">Visa</span>
                        <span className="rounded-sm border border-[#C9A863]/20 px-3 py-1">Mastercard</span>
                        <span className="rounded-sm border border-[#C9A863]/20 px-3 py-1">Cash on Delivery</span>
                    </div>
                </div>
            </div>
        </footer>
    );
};

export default PearlFooter;

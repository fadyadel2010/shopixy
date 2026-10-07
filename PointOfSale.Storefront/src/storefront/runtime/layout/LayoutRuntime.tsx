import React, { useEffect, useMemo } from 'react';
import type { ComponentType } from 'react';
import { useLocation } from 'react-router-dom';
import { AuthNavItems } from '@/auth/components';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { CartIconButton } from '@/components/common/CartBadge';
import { LanguageSwitcher } from '@/components/common/LanguageSwitcher';
import { WishlistIconButton } from '@/components/common/WishlistBadge';
import { SearchRuntimeTrigger } from '@/components/search';
import { useTranslation } from '@/i18n';
import { selectLayout, selectStoreName, useStoreConfigStore } from '@/store';
import {
    selectFooterSections,
    selectFooterSocialLinks,
    useFooterStore,
} from '@/stores/footerStore';
import {
    resolveNavLinkUrl,
    selectFooterLinks,
    selectHeaderLinks,
    selectNavigationError,
    selectNavigationLoading,
    useNavigationStore,
} from '@/stores/navigationStore';
import type {
    AuthenticationSlotProps,
    CartSlotProps,
    FooterPresentationModel,
    FooterPresentationProps,
    HeaderPresentationModel,
    HeaderPresentationProps,
    HeaderRuntimeSlots,
    LanguageSlotProps,
    NavigationSlotProps,
    SearchSlotProps,
    WishlistSlotProps,
} from '@/storefront/presentation/models';

const HeaderRuntimeContext = React.createContext<HeaderPresentationModel | null>(null);

function useHeaderRuntimeModel(): HeaderPresentationModel {
    const model = React.useContext(HeaderRuntimeContext);
    if (!model) {
        throw new Error('[LayoutRuntime] Header capability slots require HeaderRuntime ownership.');
    }

    return model;
}

const NavigationSlot: React.FC<NavigationSlotProps> = ({
    className,
    ariaLabel,
    beforeLinks,
    afterLinks,
    linkClassName,
    onLinkClick,
    renderLinkContent,
}) => {
    const model = useHeaderRuntimeModel();

    if (model.navigation.links.length === 0 && !beforeLinks && !afterLinks) {
        return null;
    }

    return (
        <nav className={className} aria-label={ariaLabel ?? model.labels.mainNavigation}>
            {beforeLinks}
            {model.navigation.links.map((link) => (
                <AffiliateLink
                    key={link.key}
                    href={link.href}
                    target={link.openInNewTab ? '_blank' : undefined}
                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                    onClick={() => onLinkClick?.(link)}
                    className={typeof linkClassName === 'function' ? linkClassName(link) : linkClassName}
                >
                    {renderLinkContent?.(link) ?? link.label}
                </AffiliateLink>
            ))}
            {afterLinks}
        </nav>
    );
};

const SearchSlot: React.FC<SearchSlotProps> = (props) => <SearchRuntimeTrigger {...props} />;
const AuthenticationSlot: React.FC<AuthenticationSlotProps> = (props) => <AuthNavItems {...props} />;
const WishlistSlot: React.FC<WishlistSlotProps> = (props) => <WishlistIconButton {...props} />;
const CartSlot: React.FC<CartSlotProps> = (props) => <CartIconButton {...props} />;
const LanguageSlot: React.FC<LanguageSlotProps> = (props) => <LanguageSwitcher {...props} />;

const HEADER_RUNTIME_SLOTS: HeaderRuntimeSlots = Object.freeze({
    NavigationSlot,
    SearchSlot,
    AuthenticationSlot,
    WishlistSlot,
    CartSlot,
    LanguageSlot,
});

function isActiveNavigationHref(pathname: string, href: string): boolean {
    if (href === '/') {
        return pathname === '/';
    }

    return pathname.startsWith(href);
}

interface HeaderRuntimeProps {
    component: ComponentType<HeaderPresentationProps>;
}

export const HeaderRuntime: React.FC<HeaderRuntimeProps> = ({ component: Component }) => {
    const { t, language } = useTranslation();
    const location = useLocation();
    const storeName = useStoreConfigStore(selectStoreName);
    const layout = useStoreConfigStore(selectLayout);
    const loadNavigation = useNavigationStore((state) => state.loadNavigation);
    const headerLinks = useNavigationStore(selectHeaderLinks);
    const isLoading = useNavigationStore(selectNavigationLoading);
    const error = useNavigationStore(selectNavigationError);
    const announcement = useNavigationStore((state) => state.navigation?.header.announcement);

    useEffect(() => {
        void loadNavigation();
    }, [loadNavigation]);

    const model = useMemo<HeaderPresentationModel>(() => ({
        announcement,
        storeName,
        headerVariant: layout.headerVariant,
        isHome: location.pathname === '/',
        navigation: {
            links: headerLinks.map((link, index) => {
                const href = resolveNavLinkUrl(link);

                return {
                    key: `${link.slug || link.path || link.label}-${index}`,
                    index,
                    label: language === 'ar' && link.labelAr ? link.labelAr : link.label,
                    href,
                    openInNewTab: link.openInNewTab,
                    isActive: isActiveNavigationHref(location.pathname, href),
                };
            }),
            isLoading,
            error,
        },
        labels: {
            account: t('header.account'),
            announcement: announcement
                ? (language === 'ar' && announcement.textAr ? announcement.textAr : announcement.text)
                : t('announcement.defaultText'),
            closeMenu: t('header.closeMenu'),
            mainNavigation: t('accessibility.mainNavigation'),
            menu: t('header.menu'),
            next: t('common.next'),
            previous: t('common.previous'),
            search: t('common.search'),
            searchProducts: t('header.searchProducts'),
            viewCart: t('header.viewCart'),
        },
    }), [announcement, error, headerLinks, isLoading, language, layout.headerVariant, location.pathname, storeName, t]);

    return (
        <HeaderRuntimeContext.Provider value={model}>
            <Component model={model} slots={HEADER_RUNTIME_SLOTS} />
        </HeaderRuntimeContext.Provider>
    );
};

interface FooterRuntimeProps {
    component: ComponentType<FooterPresentationProps>;
}

export const FooterRuntime: React.FC<FooterRuntimeProps> = ({ component: Component }) => {
    const { t, language } = useTranslation();
    const storeName = useStoreConfigStore(selectStoreName);
    const loadFooter = useFooterStore((state) => state.loadFooter);
    const sections = useFooterStore(selectFooterSections);
    const socialLinks = useFooterStore(selectFooterSocialLinks);
    const isLoading = useFooterStore((state) => state.loading);
    const error = useFooterStore((state) => state.error);
    const footerNavigationLinks = useNavigationStore(selectFooterLinks);
    const isNavigationLoading = useNavigationStore(selectNavigationLoading);
    const navigationError = useNavigationStore(selectNavigationError);

    useEffect(() => {
        void loadFooter();
    }, [language, loadFooter]);

    const model = useMemo<FooterPresentationModel>(() => {
        const currentYear = new Date().getFullYear();

        return {
            storeName,
            currentYear,
            copyright: t('footer.copyright', { year: currentYear, storeName }),
            navigation: {
                links: footerNavigationLinks.map((link, index) => ({
                    key: `${link.slug || link.path || link.label}-${index}`,
                    label: language === 'ar' && link.labelAr ? link.labelAr : link.label,
                    href: resolveNavLinkUrl(link),
                    openInNewTab: link.openInNewTab,
                })),
                isLoading: isNavigationLoading,
                error: navigationError,
            },
            sections: sections.map((section) => ({
                key: `${section.title}-${section.sortOrder}`,
                title: section.title,
                sortOrder: section.sortOrder,
                links: section.links.map((link, index) => ({
                    key: `${link.url}-${link.label}-${index}`,
                    label: language === 'ar' && link.labelAr ? link.labelAr : link.label,
                    href: link.url,
                    openInNewTab: link.openInNewTab,
                })),
            })),
            socialLinks: socialLinks.map((link, index) => ({
                key: `${link.platform}-${link.url}-${index}`,
                platform: link.platform,
                href: link.url,
                icon: link.icon,
            })),
            isLoading,
            error,
            labels: {
                contact: t('footer.contact'),
                customerService: t('footer.customerService'),
                followUs: t('footer.followUs'),
                loading: t('common.loading'),
                paymentMethods: t('footer.paymentMethods'),
                quickLinks: t('footer.quickLinks'),
            },
        };
    }, [error, footerNavigationLinks, isLoading, isNavigationLoading, language, navigationError, sections, socialLinks, storeName, t]);

    return <Component model={model} />;
};

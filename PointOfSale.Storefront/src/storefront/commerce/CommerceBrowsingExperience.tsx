import { AffiliateLink } from '@/components/common/AffiliateLink';
import { ThemeBadge, ThemeEmptyState } from '@/components/theme-components';
import type { ThemeComponentTheme } from '@/components/theme-components';
import type { ProductDiscoveryFacets, ProductListItemDto } from '@/types';
import { resolveProductImage } from '@/utils';
import { normalizeCommerceTheme } from './commerceTheme';

export { normalizeCommerceTheme } from './commerceTheme';

function cx(...classes: Array<string | false | null | undefined>): string {
    return classes.filter(Boolean).join(' ');
}

export const commercePageStyles: Record<ThemeComponentTheme, {
    page: string;
    container: string;
    breadcrumb: string;
    header: string;
    eyebrow: string;
    title: string;
    subtitle: string;
    controls: string;
    controlPanel: string;
    categoryNav: string;
    main: string;
    sidebar: string;
    resultPanel: string;
    activeFilters: string;
    pagination: string;
}> = {
    modern: {
        page: 'bg-[linear-gradient(180deg,rgba(37,99,235,0.06)_0%,transparent_18rem)]',
        container: 'container mx-auto px-4 py-6 md:py-10',
        breadcrumb: 'mb-5 text-sm',
        header: 'mb-7 rounded-md border border-muted/15 bg-background/90 p-5 shadow-sm backdrop-blur md:p-6',
        eyebrow: 'text-xs font-bold uppercase tracking-[0.18em] text-primary',
        title: 'text-2xl font-bold tracking-tight text-text md:text-4xl',
        subtitle: 'mt-2 max-w-2xl text-sm leading-6 text-muted',
        controls: 'rounded-md border border-muted/15 bg-background p-3 shadow-sm',
        controlPanel: 'flex items-center gap-3 flex-wrap',
        categoryNav: 'mb-7 rounded-md border border-muted/15 bg-background/90 p-3 shadow-sm',
        main: 'flex gap-6',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-6 rounded-md border border-primary/15 bg-primary/5 p-3',
        pagination: 'mt-10 border-t border-muted/20 pt-8',
    },
    minimal: {
        page: 'bg-background',
        container: 'container mx-auto px-4 py-8 md:py-14',
        breadcrumb: 'mb-8 border-b border-muted/15 pb-4 text-sm',
        header: 'mb-10 border-b border-muted/20 pb-8',
        eyebrow: 'text-[11px] font-medium uppercase tracking-[0.16em] text-muted',
        title: 'max-w-3xl text-2xl font-normal leading-tight text-text md:text-4xl',
        subtitle: 'mt-3 max-w-2xl text-sm leading-7 text-muted',
        controls: 'border-y border-muted/15 py-4',
        controlPanel: 'flex items-center gap-3 flex-wrap',
        categoryNav: 'mb-10 border-b border-muted/15 pb-5',
        main: 'flex gap-8',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-8 border-y border-muted/15 py-3',
        pagination: 'mt-14 border-t border-muted/20 pt-8',
    },
    fashion: {
        page: 'bg-background',
        container: 'container mx-auto px-4 py-6 md:py-12',
        breadcrumb: 'mb-5 text-sm',
        header: 'mb-8 border-l-4 border-text pl-5',
        eyebrow: 'text-xs font-black uppercase tracking-[0.3em] text-primary',
        title: 'max-w-4xl text-4xl font-black uppercase leading-[0.9] text-text md:text-6xl',
        subtitle: 'mt-4 max-w-2xl text-sm font-medium leading-7 text-muted',
        controls: 'border-y-2 border-text bg-background py-4',
        controlPanel: 'flex items-center gap-3 flex-wrap',
        categoryNav: 'mb-8 border-b-2 border-text pb-5',
        main: 'flex gap-7',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-7 border-l-4 border-primary bg-muted/5 p-4',
        pagination: 'mt-12 border-t-2 border-text pt-8',
    },
    pearl: {
        page: 'bg-[linear-gradient(180deg,rgba(245,245,244,0.72)_0%,transparent_22rem)]',
        container: 'container mx-auto px-4 py-8 md:py-14',
        breadcrumb: 'mx-auto mb-7 max-w-5xl text-center text-sm',
        header: 'mx-auto mb-10 max-w-5xl border border-secondary/25 bg-background/95 px-6 py-8 text-center shadow-[0_18px_40px_rgba(15,23,42,0.04)] md:px-10',
        eyebrow: 'text-[11px] font-semibold uppercase tracking-[0.32em] text-secondary',
        title: 'mx-auto max-w-3xl font-serif text-3xl font-normal leading-tight text-text md:text-5xl',
        subtitle: 'mx-auto mt-4 max-w-2xl text-sm leading-7 text-muted',
        controls: 'mx-auto max-w-5xl border border-secondary/25 bg-background/95 p-4 shadow-[0_12px_32px_rgba(15,23,42,0.04)]',
        controlPanel: 'flex items-center gap-3 flex-wrap justify-center',
        categoryNav: 'mx-auto mb-9 max-w-5xl border-y border-secondary/20 py-5',
        main: 'mx-auto flex max-w-7xl gap-7',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mx-auto mb-7 max-w-5xl border border-secondary/20 bg-background/95 p-4',
        pagination: 'mt-14 border-t border-secondary/25 pt-8',
    },
    phoenix: {
        page: 'bg-[linear-gradient(180deg,rgba(205,21,21,0.08)_0%,transparent_20rem)]',
        container: 'container mx-auto px-4 py-6 md:py-10',
        breadcrumb: 'mb-5 text-sm',
        header: 'mb-7 border border-black/10 bg-background p-5 shadow-sm md:p-6',
        eyebrow: 'text-xs font-bold uppercase tracking-[0.22em] text-primary',
        title: 'text-3xl font-black uppercase leading-none text-text md:text-5xl',
        subtitle: 'mt-3 max-w-2xl text-sm leading-6 text-muted',
        controls: 'border border-black/10 bg-background p-3 shadow-sm',
        controlPanel: 'flex items-center gap-3 flex-wrap',
        categoryNav: 'mb-7 border-y border-black/10 py-4',
        main: 'flex gap-6',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-6 border border-primary/25 bg-primary/5 p-3',
        pagination: 'mt-10 border-t border-black/10 pt-8',
    },
    hype: {
        page: 'bg-[linear-gradient(180deg,rgba(240,103,54,0.08)_0%,transparent_20rem)]',
        container: 'container mx-auto px-4 py-5 md:py-8',
        breadcrumb: 'mb-4 text-xs font-semibold uppercase tracking-[0.12em]',
        header: 'mb-5 rounded-lg border border-muted/20 bg-background p-4 shadow-sm md:p-5',
        eyebrow: 'text-[11px] font-bold uppercase tracking-[0.14em] text-primary',
        title: 'text-2xl font-black leading-tight text-text md:text-4xl',
        subtitle: 'mt-2 max-w-3xl text-sm leading-6 text-muted',
        controls: 'rounded-lg border border-muted/20 bg-background p-3 shadow-sm',
        controlPanel: 'flex items-center gap-2 flex-wrap',
        categoryNav: 'mb-5 rounded-lg border border-muted/20 bg-background p-2',
        main: 'flex gap-4',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-5 rounded-lg border border-primary/20 bg-primary/5 p-3',
        pagination: 'mt-8 border-t border-muted/25 pt-6',
    },
    electronics: {
        page: 'bg-[linear-gradient(180deg,rgba(14,165,233,0.07)_0%,transparent_20rem)]',
        container: 'container mx-auto px-4 py-5 md:py-8',
        breadcrumb: 'mb-4 font-mono text-xs',
        header: 'mb-5 rounded-md border border-muted/25 bg-background p-4 shadow-sm ring-1 ring-muted/5 md:p-5',
        eyebrow: 'font-mono text-[11px] font-bold uppercase tracking-[0.08em] text-primary',
        title: 'text-2xl font-black leading-tight text-text md:text-4xl',
        subtitle: 'mt-2 max-w-3xl text-sm leading-6 text-muted',
        controls: 'rounded-md border border-muted/25 bg-background p-3 shadow-sm',
        controlPanel: 'flex items-center gap-2 flex-wrap',
        categoryNav: 'mb-5 rounded-md border border-muted/25 bg-background p-2',
        main: 'flex gap-4',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-5 rounded-md border border-primary/20 bg-primary/5 p-3',
        pagination: 'mt-8 border-t border-muted/25 pt-6',
    },
    alizon: {
        page: 'bg-[linear-gradient(180deg,rgba(255,0,0,0.04)_0%,transparent_18rem)]',
        container: 'container mx-auto px-4 py-6 md:py-10',
        breadcrumb: 'mb-5 text-sm',
        header: 'mb-7 rounded border border-muted/20 bg-background p-5 shadow-sm md:p-6',
        eyebrow: 'text-xs font-bold uppercase tracking-[0.14em] text-primary',
        title: 'text-2xl font-bold leading-tight text-text md:text-4xl',
        subtitle: 'mt-3 max-w-2xl text-sm leading-6 text-muted',
        controls: 'rounded border border-muted/20 bg-background p-3 shadow-sm',
        controlPanel: 'flex items-center gap-2 flex-wrap',
        categoryNav: 'mb-5 rounded border border-muted/20 bg-background p-2',
        main: 'flex gap-4',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-5 rounded border border-primary/20 bg-primary/5 p-3',
        pagination: 'mt-8 border-t border-muted/25 pt-6',
    },
    sharp: {
        page: 'bg-[linear-gradient(180deg,rgba(1,52,35,0.04)_0%,transparent_22rem)]',
        container: 'container mx-auto px-4 py-8 md:py-14',
        breadcrumb: 'mx-auto mb-7 max-w-5xl text-center text-sm',
        header: 'mx-auto mb-10 max-w-5xl border border-muted/20 bg-background/95 px-6 py-8 text-center shadow-[0_4px_16px_rgba(1,52,35,0.04)] md:px-10',
        eyebrow: 'text-[11px] font-semibold uppercase tracking-[0.2em] text-secondary',
        title: 'mx-auto max-w-3xl text-2xl font-semibold leading-tight text-text md:text-4xl',
        subtitle: 'mx-auto mt-4 max-w-2xl text-sm leading-6 text-muted',
        controls: 'mx-auto max-w-5xl border border-muted/20 bg-background/95 p-4 shadow-[0_4px_16px_rgba(1,52,35,0.04)]',
        controlPanel: 'flex items-center gap-3 flex-wrap justify-center',
        categoryNav: 'mx-auto mb-9 max-w-5xl border-y border-muted/20 py-5',
        main: 'mx-auto flex max-w-7xl gap-7',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mx-auto mb-7 max-w-5xl border border-muted/20 bg-background/95 p-4',
        pagination: 'mt-14 border-t border-muted/25 pt-8',
    },
    cosmetics: {
        page: 'bg-white',
        container: 'mx-auto w-full px-0 py-0',
        breadcrumb: 'mx-auto mb-5 max-w-7xl px-4 pt-6 text-sm',
        header: 'mx-auto mb-7 flex max-w-7xl items-center justify-between px-3 pt-3',
        eyebrow: 'text-[11px] font-medium uppercase tracking-[0.18em] text-primary',
        title: 'text-2xl font-bold uppercase leading-tight text-text md:text-4xl',
        subtitle: 'mt-2 max-w-2xl text-sm leading-6 text-muted',
        controls: 'mx-auto mb-3 max-w-7xl border-y border-black/10 bg-background px-3 py-3',
        controlPanel: 'flex items-center gap-3 flex-wrap justify-end',
        categoryNav: 'mx-auto mb-6 max-w-7xl border-y border-primary/20 px-3 py-4',
        main: 'mx-auto flex max-w-7xl gap-0 px-0',
        sidebar: 'hidden lg:block lg:px-3',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mx-3 mb-5 border-y border-primary/20 bg-background py-3',
        pagination: 'mx-3 mt-10 border-t border-black/10 pt-8',
    },
    seven: {
        page: 'bg-[linear-gradient(180deg,rgba(157,120,119,0.04)_0%,transparent_22rem)]',
        container: 'container mx-auto px-4 py-6 md:py-12',
        breadcrumb: 'mb-5 text-sm',
        header: 'mb-8 border-l-4 border-text pl-5',
        eyebrow: 'text-xs font-black uppercase tracking-[0.34em] text-primary',
        title: 'mt-3 max-w-3xl text-4xl font-black uppercase leading-[0.95] text-text md:text-5xl',
        subtitle: 'mt-4 max-w-2xl text-sm font-medium leading-7 text-muted',
        controls: 'border-y-2 border-text bg-background py-4',
        controlPanel: 'flex items-center gap-3 flex-wrap',
        categoryNav: 'mb-8 border-b-2 border-text pb-5',
        main: 'flex gap-7',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-7 border-l-4 border-primary bg-muted/5 p-4',
        pagination: 'mt-12 border-t-2 border-text pt-8',
    },
    default: {
        page: 'bg-background',
        container: 'container mx-auto px-4 py-6 md:py-10',
        breadcrumb: 'mb-4 text-sm',
        header: 'mb-6',
        eyebrow: 'text-xs font-semibold uppercase tracking-[0.16em] text-primary',
        title: 'text-2xl font-bold text-text md:text-3xl',
        subtitle: 'mt-1 text-sm text-muted',
        controls: '',
        controlPanel: 'flex items-center gap-3 flex-wrap',
        categoryNav: 'mb-6',
        main: 'flex gap-6',
        sidebar: 'hidden lg:block',
        resultPanel: 'min-w-0 flex-1',
        activeFilters: 'mb-6',
        pagination: 'mt-10 border-t border-muted/20 pt-8',
    },
};

export const commerceGridStyles: Record<ThemeComponentTheme, string> = {
    modern: 'grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-4 md:gap-5',
    minimal: 'grid grid-cols-2 gap-x-5 gap-y-12 md:grid-cols-3 xl:grid-cols-4 md:gap-x-8',
    fashion: 'grid grid-cols-1 gap-x-6 gap-y-12 sm:grid-cols-2 xl:grid-cols-3',
    pearl: 'grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-4',
    phoenix: 'grid grid-cols-1 gap-5 sm:grid-cols-2 xl:grid-cols-4',
    hype: 'grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4',
    electronics: 'grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4',
    alizon: 'grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4',
    sharp: 'grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-4',
    cosmetics: 'grid grid-cols-2 gap-0 md:grid-cols-3 xl:grid-cols-4',
    seven: 'grid grid-cols-1 gap-x-6 gap-y-12 sm:grid-cols-2 xl:grid-cols-3',
    default: 'grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-4 md:gap-6',
};

export function getCommerceGridItemClass(themeId: string | null | undefined, index: number): string {
    const theme = normalizeCommerceTheme(themeId);

    if (theme === 'fashion' && index === 0) {
        return 'sm:col-span-2 xl:col-span-2';
    }

    if (theme === 'pearl' && index > 0 && index % 7 === 0) {
        return 'xl:col-span-2';
    }

    if (theme === 'phoenix' && index === 0) {
        return 'sm:col-span-2';
    }

    if (theme === 'cosmetics') {
        return 'min-h-full';
    }

    if (theme === 'electronics' || theme === 'hype') {
        return 'min-h-full';
    }

    return '';
}

export function CommercePageHeader({
    themeId,
    title,
    resultText,
    categoryName,
}: {
    themeId?: string | null;
    title: string;
    resultText?: string;
    categoryName?: string;
}) {
    const theme = normalizeCommerceTheme(themeId);
    const styles = commercePageStyles[theme];
    const eyebrow = {
        modern: 'Shop faster',
        minimal: 'Browse quietly',
        fashion: 'Collection edit',
        pearl: 'Private selection',
        phoenix: 'Performance edit',
        hype: 'Tech drop',
        electronics: 'Product matrix',
        alizon: 'Superstore browse',
        sharp: 'Jewelry browse',
        cosmetics: 'Beauty edit',
        seven: 'Collection edit',
        default: 'Browse',
    }[theme];
    const subtitle = {
        modern: categoryName ? 'Promotions, categories, and essentials stay close so discovery keeps moving.' : 'A balanced catalog view for quick product discovery.',
        minimal: 'A quieter catalog view with fewer interruptions and more room to compare.',
        fashion: categoryName ? 'An editorial path through the current collection.' : 'Collections lead the rhythm, with products arranged like a buying edit.',
        pearl: 'A boutique browsing pace with product detail treated as part of the presentation.',
        phoenix: 'Activewear browsing keeps launches, stock, and movement-ready products prominent.',
        hype: 'A fast electronics browsing path for comparing drops, offers, and availability.',
        electronics: 'Availability, sorting, and facets are kept prominent for comparison-led decisions.',
        alizon: 'A general-merchandise browsing path with product shelves organized by category.',
        sharp: 'A jewelry browsing path with product carousels organized by collection.',
        cosmetics: 'A beauty browsing path with square product cards, compact pricing, and editorial collection rhythm.',
        seven: 'An editorial path through the current collection.',
        default: 'Browse available products.',
    }[theme];

    return (
        <div className={styles.header}>
            <p className={styles.eyebrow}>{eyebrow}</p>
            <h1 className={styles.title}>{title}</h1>
            <p className={styles.subtitle}>{resultText ? `${resultText}. ${subtitle}` : subtitle}</p>
        </div>
    );
}

export function CommerceMerchandisingBlock({
    themeId,
    products,
    facets,
    categoryName,
    isRTL,
}: {
    themeId?: string | null;
    products: ProductListItemDto[];
    facets: ProductDiscoveryFacets | null;
    categoryName?: string;
    isRTL?: boolean;
}) {
    if (products.length === 0) {
        return null;
    }

    const theme = normalizeCommerceTheme(themeId);
    const promotedProduct = products.find(product => product.hasActivePromotion) ?? products[0];
    const imageSrc = resolveProductImage(promotedProduct);
    const promotedCount = facets?.promotions?.promotedCount ?? products.filter(product => product.hasActivePromotion).length;
    const inStockCount = facets?.stock?.inStockCount ?? products.filter(product => product.inStock).length;
    const brandCount = facets?.brands?.length ?? 0;
    const topCategories = (facets?.categories ?? []).slice(0, 3).map(category => category.name).filter(Boolean);

    if (theme === 'minimal') {
        return (
            <section className="mb-10 border-y border-muted/15 py-4" aria-label="Browsing summary">
                <div className={cx('flex flex-wrap items-center justify-between gap-4 text-sm text-muted', isRTL && 'flex-row-reverse text-right')}>
                    <span>{categoryName ? `Curated in ${categoryName}` : 'Curated product browsing'}</span>
                    <span>{inStockCount} available now</span>
                    {topCategories.length > 0 ? <span>{topCategories.join(' / ')}</span> : null}
                </div>
            </section>
        );
    }

    if (theme === 'fashion') {
        return (
            <section className="mb-9 grid gap-4 border-y-2 border-text py-5 md:grid-cols-[minmax(0,1fr)_16rem]" aria-label="Collection feature">
                <div className={cx('self-end', isRTL && 'text-right')}>
                    <p className="text-xs font-black uppercase tracking-[0.3em] text-primary">Campaign focus</p>
                    <h2 className="mt-3 max-w-2xl text-3xl font-black uppercase leading-none text-text md:text-5xl">
                        {categoryName || promotedProduct.categoryName || 'The current edit'}
                    </h2>
                    <p className="mt-4 max-w-xl text-sm font-medium leading-7 text-muted">
                        {promotedCount > 0
                            ? `${promotedCount} promoted products are surfaced inside the same catalog flow.`
                            : 'Products stay arranged for editorial scanning without changing availability or pricing rules.'}
                    </p>
                </div>
                <AffiliateLink href={promotedProduct.slug ? `/products/${promotedProduct.slug}` : `/products/${promotedProduct.id}`} className="group block">
                    <div className="aspect-[3/4] overflow-hidden bg-muted/10">
                        <img src={imageSrc} alt="" className="h-full w-full object-cover transition-transform duration-700 group-hover:scale-105" loading="lazy" />
                    </div>
                </AffiliateLink>
            </section>
        );
    }

    if (theme === 'pearl') {
        return (
            <section className="mx-auto mb-10 max-w-5xl border border-secondary/25 bg-background/90 px-6 py-6 text-center shadow-[0_18px_40px_rgba(15,23,42,0.04)]" aria-label="Boutique curation">
                <p className="text-[11px] font-semibold uppercase tracking-[0.32em] text-secondary">Boutique curation</p>
                <p className="mx-auto mt-3 max-w-2xl text-sm leading-7 text-muted">
                    {topCategories.length > 0
                        ? `Explore ${topCategories.join(', ')} with ${inStockCount} available pieces.`
                        : `Explore ${inStockCount} available pieces selected from the current catalog.`}
                </p>
            </section>
        );
    }

    if (theme === 'phoenix') {
        return (
            <section className="mb-8 border-y border-black/10 py-5" aria-label="Performance curation">
                <p className="text-xs font-bold uppercase tracking-[0.22em] text-primary">Performance curation</p>
                <p className="mt-3 max-w-2xl text-sm leading-7 text-muted">
                    {topCategories.length > 0
                        ? `${topCategories.join(', ')} stay ready with ${inStockCount} available products.`
                        : `${inStockCount} available products are ready for the current drop.`}
                </p>
            </section>
        );
    }

    if (theme === 'sharp') {
        return (
            <section className="mx-auto mb-10 max-w-5xl border border-muted/20 bg-background/95 px-6 py-6 text-center shadow-[0_4px_16px_rgba(1,52,35,0.04)]" aria-label="Jewelry curation">
                <p className="text-[11px] font-semibold uppercase tracking-[0.2em] text-secondary">Jewelry curation</p>
                <p className="mx-auto mt-3 max-w-2xl text-sm leading-7 text-muted">
                    {topCategories.length > 0
                        ? `Explore ${topCategories.join(', ')} with ${inStockCount} available pieces.`
                        : `Explore ${inStockCount} available pieces selected from the current catalog.`}
                </p>
            </section>
        );
    }

    if (theme === 'cosmetics') {
        return (
            <section className="mx-3 mb-8 border-y border-primary/20 bg-background py-5 text-center" aria-label="Beauty curation">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-primary">Beauty curation</p>
                <p className="mx-auto mt-3 max-w-2xl text-sm leading-7 text-muted">
                    {topCategories.length > 0
                        ? `Explore ${topCategories.join(', ')} with ${inStockCount} available beauty essentials.`
                        : `Explore ${inStockCount} available beauty essentials selected from the current catalog.`}
                </p>
            </section>
        );
    }

    if (theme === 'electronics' || theme === 'hype' || theme === 'alizon') {
        return (
            <section className="mb-5 grid gap-3 rounded-md border border-muted/25 bg-background p-3 sm:grid-cols-3" aria-label="Decision support summary">
                <Metric label="Available" value={String(inStockCount)} />
                <Metric label="Brands" value={String(brandCount)} />
                <Metric label="Offers" value={String(promotedCount)} />
            </section>
        );
    }

    return (
        <section className="mb-7 rounded-md border border-primary/15 bg-primary/5 p-4" aria-label="Merchandising summary">
            <div className={cx('flex flex-wrap items-center justify-between gap-3', isRTL && 'flex-row-reverse text-right')}>
                <div>
                    <p className="text-xs font-bold uppercase tracking-[0.16em] text-primary">Featured browsing</p>
                    <p className="mt-1 text-sm text-muted">
                        {promotedCount > 0 ? `${promotedCount} active offers are visible in this catalog view.` : 'Browse featured products with current pricing and stock evidence.'}
                    </p>
                </div>
                {promotedCount > 0 ? <ThemeBadge themeId={theme} tone="accent">{promotedCount} offers</ThemeBadge> : null}
            </div>
        </section>
    );
}

function Metric({ label, value }: { label: string; value: string }) {
    return (
        <div className="rounded-sm border border-muted/20 bg-muted/5 px-3 py-2">
            <p className="font-mono text-[10px] font-bold uppercase tracking-[0.08em] text-muted">{label}</p>
            <p className="mt-1 font-mono text-2xl font-black leading-none text-text">{value}</p>
        </div>
    );
}

export function CommerceEmptyState({
    themeId,
    title,
    message,
}: {
    themeId?: string | null;
    title: string;
    message?: string;
}) {
    return (
        <ThemeEmptyState
            themeId={themeId}
            title={title}
            message={message}
            className="min-h-[18rem]"
        />
    );
}

export const searchPageStyles: Record<ThemeComponentTheme, {
    page: string;
    header: string;
    eyebrow: string;
    title: string;
    resultWrap: string;
    tabs: string;
    activeTab: string;
    inactiveTab: string;
    meta: string;
    list: string;
    pagerButton: string;
    disabledPagerButton: string;
}> = {
    modern: {
        page: 'flex-1 container mx-auto px-4 py-8',
        header: 'mx-auto mb-8 max-w-3xl rounded-md border border-muted/15 bg-background p-5 shadow-sm',
        eyebrow: 'mb-2 text-xs font-bold uppercase tracking-[0.18em] text-primary',
        title: 'mb-4 text-2xl font-bold text-text md:text-3xl',
        resultWrap: 'mx-auto max-w-4xl',
        tabs: 'mb-6 flex gap-1 overflow-x-auto pb-2',
        activeTab: 'bg-primary text-white',
        inactiveTab: 'bg-muted/10 text-muted hover:bg-muted/20 hover:text-text',
        meta: 'mb-4 text-sm text-muted',
        list: 'overflow-hidden rounded-md border border-muted/20 bg-background shadow-sm divide-y divide-muted/10',
        pagerButton: 'bg-muted/10 text-text hover:bg-muted/20',
        disabledPagerButton: 'cursor-not-allowed bg-muted/5 text-muted',
    },
    minimal: {
        page: 'flex-1 container mx-auto px-4 py-10',
        header: 'mx-auto mb-10 max-w-3xl border-b border-muted/20 pb-8',
        eyebrow: 'mb-2 text-[11px] font-medium uppercase tracking-[0.16em] text-muted',
        title: 'mb-4 text-2xl font-normal text-text md:text-3xl',
        resultWrap: 'mx-auto max-w-4xl',
        tabs: 'mb-8 flex gap-4 overflow-x-auto border-b border-muted/15 pb-3',
        activeTab: 'border-b border-text text-text',
        inactiveTab: 'text-muted hover:text-text',
        meta: 'mb-5 text-sm text-muted',
        list: 'border-y border-muted/20 bg-background divide-y divide-muted/10',
        pagerButton: 'border border-muted/20 text-text hover:bg-muted/10',
        disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
    },
    fashion: {
        page: 'flex-1 container mx-auto px-4 py-8',
        header: 'mx-auto mb-8 max-w-4xl border-l-4 border-text pl-5',
        eyebrow: 'mb-3 text-xs font-black uppercase tracking-[0.3em] text-primary',
        title: 'mb-5 text-4xl font-black uppercase leading-none text-text md:text-5xl',
        resultWrap: 'mx-auto max-w-5xl',
        tabs: 'mb-7 flex gap-2 overflow-x-auto border-y-2 border-text py-3',
        activeTab: 'bg-text text-background',
        inactiveTab: 'text-text hover:bg-muted/10',
        meta: 'mb-5 text-sm font-medium uppercase tracking-[0.08em] text-muted',
        list: 'border-y-2 border-text bg-background divide-y divide-muted/15',
        pagerButton: 'border-2 border-text text-text hover:bg-text hover:text-background',
        disabledPagerButton: 'cursor-not-allowed border-2 border-muted/20 text-muted',
    },
    pearl: {
        page: 'flex-1 container mx-auto px-4 py-10',
        header: 'mx-auto mb-10 max-w-3xl border border-secondary/25 bg-background px-6 py-8 text-center shadow-[0_18px_40px_rgba(15,23,42,0.04)]',
        eyebrow: 'mb-3 text-[11px] font-semibold uppercase tracking-[0.32em] text-secondary',
        title: 'mb-5 font-serif text-3xl font-normal text-text md:text-4xl',
        resultWrap: 'mx-auto max-w-4xl',
        tabs: 'mb-7 flex justify-center gap-2 overflow-x-auto border-y border-secondary/20 py-3',
        activeTab: 'border border-secondary bg-secondary text-white',
        inactiveTab: 'border border-secondary/20 text-muted hover:text-text',
        meta: 'mb-5 text-center text-sm text-muted',
        list: 'border border-secondary/25 bg-background shadow-[0_18px_40px_rgba(15,23,42,0.04)] divide-y divide-secondary/10',
        pagerButton: 'border border-secondary/25 text-text hover:border-secondary',
        disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
    },
    phoenix: {
        page: 'flex-1 container mx-auto px-4 py-8',
        header: 'mx-auto mb-8 max-w-4xl border border-black/10 bg-background p-5 shadow-sm',
        eyebrow: 'mb-3 text-xs font-bold uppercase tracking-[0.22em] text-primary',
        title: 'mb-5 text-4xl font-black uppercase leading-none text-text md:text-5xl',
        resultWrap: 'mx-auto max-w-5xl',
        tabs: 'mb-7 flex gap-2 overflow-x-auto border-y border-black/10 py-3',
        activeTab: 'bg-primary text-white',
        inactiveTab: 'border border-black/10 text-text hover:border-primary/40',
        meta: 'mb-5 text-sm font-semibold uppercase tracking-[0.08em] text-muted',
        list: 'border border-black/10 bg-background shadow-sm divide-y divide-black/10',
        pagerButton: 'border border-black/10 text-text hover:border-primary/50',
        disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
    },
    hype: {
        page: 'flex-1 container mx-auto px-4 py-6',
        header: 'mx-auto mb-6 max-w-4xl rounded-lg border border-muted/20 bg-background p-4 shadow-sm',
        eyebrow: 'mb-2 text-[11px] font-bold uppercase tracking-[0.14em] text-primary',
        title: 'mb-4 text-2xl font-black text-text md:text-3xl',
        resultWrap: 'mx-auto max-w-5xl',
        tabs: 'mb-5 flex gap-1 overflow-x-auto pb-2',
        activeTab: 'bg-primary text-white',
        inactiveTab: 'border border-muted/20 text-muted hover:border-primary/40 hover:text-text',
        meta: 'mb-3 text-xs font-semibold text-muted',
        list: 'overflow-hidden rounded-lg border border-muted/20 bg-background divide-y divide-muted/10',
        pagerButton: 'border border-muted/20 text-text hover:border-primary/50',
        disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
    },
    electronics: {
        page: 'flex-1 container mx-auto px-4 py-6',
        header: 'mx-auto mb-6 max-w-4xl rounded-md border border-muted/25 bg-background p-4 shadow-sm',
        eyebrow: 'mb-2 font-mono text-[11px] font-bold uppercase tracking-[0.08em] text-primary',
        title: 'mb-4 text-2xl font-black text-text md:text-3xl',
        resultWrap: 'mx-auto max-w-5xl',
        tabs: 'mb-5 flex gap-1 overflow-x-auto pb-2',
        activeTab: 'bg-primary text-white',
        inactiveTab: 'border border-muted/25 text-muted hover:border-primary/40 hover:text-text',
        meta: 'mb-3 font-mono text-xs text-muted',
        list: 'overflow-hidden rounded-md border border-muted/25 bg-background divide-y divide-muted/10',
        pagerButton: 'border border-muted/25 text-text hover:border-primary/50',
        disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
    },
    alizon: {
        page: 'flex-1 container mx-auto px-4 py-6',
        header: 'mx-auto mb-6 max-w-4xl rounded-md border border-muted/25 bg-background p-4 shadow-sm',
        eyebrow: 'mb-2 text-[11px] font-bold uppercase tracking-[0.14em] text-primary',
        title: 'mb-4 text-2xl font-black text-text md:text-3xl',
        resultWrap: 'mx-auto max-w-5xl',
        tabs: 'mb-5 flex gap-1 overflow-x-auto pb-2',
        activeTab: 'bg-primary text-white',
        inactiveTab: 'border border-muted/25 text-muted hover:border-primary/40 hover:text-text',
        meta: 'mb-3 text-xs font-semibold text-muted',
        list: 'overflow-hidden rounded-md border border-muted/25 bg-background divide-y divide-muted/10',
        pagerButton: 'border border-muted/25 text-text hover:border-primary/50',
        disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
    },
    sharp: {
        page: 'flex-1 container mx-auto px-4 py-10',
        header: 'mx-auto mb-10 max-w-3xl border border-muted/20 bg-background px-6 py-8 text-center shadow-[0_4px_16px_rgba(1,52,35,0.04)]',
        eyebrow: 'mb-3 text-[11px] font-semibold uppercase tracking-[0.2em] text-secondary',
        title: 'mb-5 text-2xl font-semibold text-text md:text-4xl',
        resultWrap: 'mx-auto max-w-4xl',
        tabs: 'mb-7 flex justify-center gap-2 overflow-x-auto border-y border-muted/20 py-3',
        activeTab: 'border border-secondary bg-secondary text-white',
        inactiveTab: 'border border-muted/20 text-muted hover:text-text',
        meta: 'mb-5 text-center text-sm text-muted',
        list: 'border border-muted/25 bg-background shadow-[0_4px_16px_rgba(1,52,35,0.04)] divide-y divide-muted/10',
        pagerButton: 'border border-muted/25 text-text hover:border-secondary',
        disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
    },
    cosmetics: {
        page: 'flex-1 bg-white px-0 py-8',
        header: 'mx-auto mb-8 max-w-4xl border-y border-primary/20 px-4 py-8 text-center',
        eyebrow: 'mb-3 text-[11px] font-semibold uppercase tracking-[0.18em] text-primary',
        title: 'mb-5 text-3xl font-bold uppercase leading-none text-text md:text-5xl',
        resultWrap: 'mx-auto max-w-5xl px-4',
        tabs: 'mb-7 flex justify-center gap-2 overflow-x-auto border-y border-primary/20 py-3',
        activeTab: 'bg-primary text-white',
        inactiveTab: 'border border-primary/20 text-primary hover:bg-primary hover:text-white',
        meta: 'mb-5 text-center text-sm text-muted',
        list: 'border-y border-primary/20 bg-background divide-y divide-primary/10',
        pagerButton: 'border border-primary/25 text-primary hover:bg-primary hover:text-white',
        disabledPagerButton: 'cursor-not-allowed border border-muted/10 text-muted',
    },
    seven: {
        page: 'flex-1 container mx-auto px-4 py-8',
        header: 'mx-auto mb-8 max-w-4xl border-l-4 border-text pl-5',
        eyebrow: 'mb-3 text-xs font-black uppercase tracking-[0.34em] text-primary',
        title: 'mb-5 text-4xl font-black uppercase leading-none text-text md:text-5xl',
        resultWrap: 'mx-auto max-w-5xl',
        tabs: 'mb-7 flex gap-2 overflow-x-auto border-y-2 border-text py-3',
        activeTab: 'bg-text text-background',
        inactiveTab: 'text-text hover:bg-muted/10',
        meta: 'mb-5 text-sm font-medium uppercase tracking-[0.08em] text-muted',
        list: 'border-y-2 border-text bg-background divide-y divide-muted/15',
        pagerButton: 'border-2 border-text text-text hover:bg-text hover:text-background',
        disabledPagerButton: 'cursor-not-allowed border-2 border-muted/20 text-muted',
    },
    default: {
        page: 'flex-1 container mx-auto px-4 py-8',
        header: 'mx-auto mb-8 max-w-3xl',
        eyebrow: 'mb-2 text-xs font-semibold uppercase tracking-[0.16em] text-primary',
        title: 'mb-4 text-2xl font-bold text-text md:text-3xl',
        resultWrap: 'mx-auto max-w-4xl',
        tabs: 'mb-6 flex gap-1 overflow-x-auto pb-2',
        activeTab: 'bg-primary text-white',
        inactiveTab: 'bg-muted/10 text-muted hover:bg-muted/20 hover:text-text',
        meta: 'mb-4 text-sm text-muted',
        list: 'overflow-hidden rounded-xl border border-muted/20 bg-background divide-y divide-muted/10',
        pagerButton: 'bg-muted/10 text-text hover:bg-muted/20',
        disabledPagerButton: 'cursor-not-allowed bg-muted/5 text-muted',
    },
};

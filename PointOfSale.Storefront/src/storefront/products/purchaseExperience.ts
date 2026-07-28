import themeAuthority from '@theme-authority/shopixy-theme-authority.json';

export type PurchaseExperienceVariant =
    | 'modern'
    | 'minimal'
    | 'fashion'
    | 'pearl'
    | 'phoenix'
    | 'hype'
    | 'electronics'
    | 'cosmetics';

export interface PurchaseExperienceProfile {
    variant: PurchaseExperienceVariant;
    philosophy: string;
    categoryLabel: string;
    highlightsLabel: string;
    trustHeading: string;
    stockLabel: string;
    skuLabel: string;
    selectedOptionLabel: string;
    relatedTitle: string;
    reviewsTitle: string;
    specsTitle: string;
    featureHeading: string;
    faqHeading: string;
    galleryEyebrow: string;
    galleryAspectClassName: string;
    galleryMaxThumbnails: number;
    sectionClassName: string;
    heroGridClassName: string;
    mediaWrapClassName: string;
    buyBoxClassName: string;
    categoryChipClassName: string;
    titleClassName: string;
    priceClassName: string;
    salePriceClassName: string;
    comparePriceClassName: string;
    promoBadgeClassName: string;
    highlightsPanelClassName: string;
    highlightItemClassName: string;
    infoCardClassName: string;
    stockBadgeClassName: string;
    ctaClassName: string;
    secondaryCtaClassName: string;
    trustGridClassName: string;
    trustItemClassName: string;
    mobileBarClassName: string;
    specsShellClassName: string;
    specsFeatureCardClassName: string;
    specsFaqCardClassName: string;
    relatedShellClassName: string;
    relatedGridClassName: string;
    reviewShellClassName: string;
    reviewCardClassName: string;
    galleryShellClassName: string;
}

const PROFILE_BY_VARIANT: Record<PurchaseExperienceVariant, PurchaseExperienceProfile> = {
    modern: {
        variant: 'modern',
        philosophy: 'Fast confident purchasing',
        categoryLabel: 'Category',
        highlightsLabel: 'Quick Scan',
        trustHeading: 'Purchase Confidence',
        stockLabel: 'Availability',
        skuLabel: 'SKU',
        selectedOptionLabel: 'Selected Option',
        relatedTitle: 'Recommended Products',
        reviewsTitle: 'Customer Reviews',
        specsTitle: 'Product Details',
        featureHeading: 'Highlights',
        faqHeading: 'Questions',
        galleryEyebrow: 'Product gallery',
        galleryAspectClassName: 'aspect-square',
        galleryMaxThumbnails: 8,
        sectionClassName: 'border-y border-slate-200 bg-white',
        heroGridClassName: 'grid gap-8 lg:grid-cols-[minmax(0,1.05fr)_minmax(360px,0.95fr)]',
        mediaWrapClassName: 'lg:sticky lg:top-24 lg:self-start',
        buyBoxClassName: 'space-y-5 rounded-lg border border-slate-200 bg-white p-5 shadow-sm',
        categoryChipClassName: 'inline-flex rounded-md border border-slate-200 bg-slate-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-slate-600',
        titleClassName: 'text-3xl font-bold text-slate-950 md:text-4xl',
        priceClassName: 'text-3xl font-bold text-primary',
        salePriceClassName: 'text-3xl font-bold text-red-600',
        comparePriceClassName: 'text-lg text-slate-500 line-through',
        promoBadgeClassName: 'inline-flex rounded-md bg-red-600 px-3 py-1 text-xs font-semibold text-white',
        highlightsPanelClassName: 'rounded-lg border border-slate-200 bg-slate-50 p-4',
        highlightItemClassName: 'rounded-md bg-white px-3 py-2 text-sm font-medium text-slate-900 shadow-sm',
        infoCardClassName: 'rounded-lg border border-slate-200 bg-white px-4 py-3 shadow-sm',
        stockBadgeClassName: 'inline-flex items-center rounded-md px-3 py-1 text-sm font-semibold',
        ctaClassName: 'rounded-md bg-primary px-6 py-3 font-semibold text-white transition-opacity hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-primary/30 disabled:cursor-not-allowed disabled:opacity-50',
        secondaryCtaClassName: 'rounded-md border border-slate-300 px-6 py-3 font-semibold text-slate-700 transition-colors hover:border-primary hover:text-primary focus:outline-none focus:ring-2 focus:ring-primary/20',
        trustGridClassName: 'grid gap-3 sm:grid-cols-2',
        trustItemClassName: 'rounded-lg border border-slate-200 bg-slate-50 px-4 py-3',
        mobileBarClassName: 'border-slate-200 bg-white/95 text-slate-950 shadow-[0_-12px_34px_-24px_rgba(15,23,42,0.45)] backdrop-blur',
        specsShellClassName: 'rounded-lg border border-slate-200 bg-white p-6 shadow-sm',
        specsFeatureCardClassName: 'rounded-lg border border-slate-200 bg-slate-50 px-4 py-4 text-sm leading-7 text-slate-900',
        specsFaqCardClassName: 'rounded-lg border border-slate-200 bg-white px-5 py-4 shadow-sm',
        relatedShellClassName: 'rounded-lg border border-slate-200 bg-white p-6 shadow-sm',
        relatedGridClassName: 'grid gap-4 sm:grid-cols-2 lg:grid-cols-4',
        reviewShellClassName: 'rounded-lg border border-slate-200 bg-white p-6 shadow-sm',
        reviewCardClassName: 'rounded-lg border border-slate-200 bg-slate-50 p-5',
        galleryShellClassName: 'rounded-lg border border-slate-200 bg-white p-4 shadow-sm sm:p-5',
    },
    minimal: {
        variant: 'minimal',
        philosophy: 'Calm product appreciation',
        categoryLabel: 'Object',
        highlightsLabel: 'Essentials',
        trustHeading: 'Quiet Reassurance',
        stockLabel: 'Stock',
        skuLabel: 'Reference',
        selectedOptionLabel: 'Choice',
        relatedTitle: 'Curated Selection',
        reviewsTitle: 'Customer Notes',
        specsTitle: 'Details',
        featureHeading: 'Notes',
        faqHeading: 'Questions',
        galleryEyebrow: 'Product study',
        galleryAspectClassName: 'aspect-[4/5]',
        galleryMaxThumbnails: 5,
        sectionClassName: 'bg-white',
        heroGridClassName: 'grid gap-12 lg:grid-cols-[minmax(0,1.2fr)_minmax(320px,0.8fr)]',
        mediaWrapClassName: 'lg:sticky lg:top-24 lg:self-start',
        buyBoxClassName: 'space-y-7 border-t border-slate-200 bg-white pt-6',
        categoryChipClassName: 'inline-flex border-b border-slate-300 pb-1 text-xs font-medium uppercase tracking-[0.18em] text-slate-500',
        titleClassName: 'text-3xl font-normal text-slate-950 md:text-4xl',
        priceClassName: 'text-2xl font-medium text-slate-950',
        salePriceClassName: 'text-2xl font-medium text-slate-950',
        comparePriceClassName: 'text-base text-slate-400 line-through',
        promoBadgeClassName: 'inline-flex border border-slate-300 px-3 py-1 text-xs font-medium text-slate-700',
        highlightsPanelClassName: 'border-y border-slate-200 py-4',
        highlightItemClassName: 'border-b border-slate-100 py-2 text-sm text-slate-700 last:border-b-0',
        infoCardClassName: 'border-b border-slate-200 py-3',
        stockBadgeClassName: 'inline-flex items-center border border-slate-300 px-3 py-1 text-sm',
        ctaClassName: 'rounded-none bg-slate-950 px-6 py-3 font-medium text-white transition-opacity hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:cursor-not-allowed disabled:opacity-50',
        secondaryCtaClassName: 'border-b border-slate-400 px-0 py-2 font-medium text-slate-700 transition-colors hover:text-slate-950 focus:outline-none focus:ring-2 focus:ring-slate-300',
        trustGridClassName: 'grid gap-0 divide-y divide-slate-200 border-y border-slate-200',
        trustItemClassName: 'py-3',
        mobileBarClassName: 'border-slate-200 bg-white/95 text-slate-950 shadow-[0_-10px_28px_-24px_rgba(15,23,42,0.35)] backdrop-blur',
        specsShellClassName: 'border-t border-slate-200 bg-white py-8',
        specsFeatureCardClassName: 'border-b border-slate-200 py-4 text-sm leading-7 text-slate-700',
        specsFaqCardClassName: 'border-b border-slate-200 py-4',
        relatedShellClassName: 'border-t border-slate-200 bg-white py-8',
        relatedGridClassName: 'grid gap-8 sm:grid-cols-2 lg:grid-cols-4',
        reviewShellClassName: 'border-t border-slate-200 bg-white py-8',
        reviewCardClassName: 'border-b border-slate-200 py-5',
        galleryShellClassName: 'bg-white',
    },
    fashion: {
        variant: 'fashion',
        philosophy: 'Emotional purchasing',
        categoryLabel: 'Collection',
        highlightsLabel: 'Campaign Notes',
        trustHeading: 'Brand Confidence',
        stockLabel: 'Availability',
        skuLabel: 'Style Code',
        selectedOptionLabel: 'Chosen Fit',
        relatedTitle: 'Complete the Look',
        reviewsTitle: 'Style Reviews',
        specsTitle: 'Style Notes',
        featureHeading: 'Styling Cues',
        faqHeading: 'Fit Questions',
        galleryEyebrow: 'Campaign gallery',
        galleryAspectClassName: 'aspect-[3/4]',
        galleryMaxThumbnails: 6,
        sectionClassName: 'bg-[#fffaf5]',
        heroGridClassName: 'grid gap-8 lg:grid-cols-[minmax(0,1.15fr)_minmax(340px,0.85fr)]',
        mediaWrapClassName: 'lg:sticky lg:top-24 lg:self-start',
        buyBoxClassName: 'space-y-6 rounded-lg border border-stone-200 bg-white/90 p-6 shadow-[0_22px_60px_-42px_rgba(120,53,15,0.42)]',
        categoryChipClassName: 'inline-flex rounded-none border-b border-stone-400 pb-1 text-xs font-semibold uppercase tracking-[0.24em] text-stone-600',
        titleClassName: 'text-4xl font-semibold text-stone-950 md:text-5xl',
        priceClassName: 'text-3xl font-semibold text-stone-950',
        salePriceClassName: 'text-3xl font-semibold text-red-700',
        comparePriceClassName: 'text-lg text-stone-500 line-through',
        promoBadgeClassName: 'inline-flex bg-stone-950 px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-white',
        highlightsPanelClassName: 'rounded-lg border border-stone-200 bg-stone-50 p-4',
        highlightItemClassName: 'rounded-md bg-white px-3 py-2 text-sm font-medium text-stone-900',
        infoCardClassName: 'rounded-lg border border-stone-200 bg-stone-50/70 px-4 py-3',
        stockBadgeClassName: 'inline-flex items-center rounded-md px-3 py-1 text-sm font-semibold',
        ctaClassName: 'rounded-none bg-stone-950 px-6 py-3 font-semibold text-white transition-opacity hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-stone-400 disabled:cursor-not-allowed disabled:opacity-50',
        secondaryCtaClassName: 'rounded-none border border-stone-300 px-6 py-3 font-semibold text-stone-700 transition-colors hover:border-stone-950 hover:text-stone-950 focus:outline-none focus:ring-2 focus:ring-stone-300',
        trustGridClassName: 'grid gap-3 sm:grid-cols-2',
        trustItemClassName: 'rounded-lg border border-stone-200 bg-white px-4 py-3',
        mobileBarClassName: 'border-stone-200 bg-[#fffaf5]/95 text-stone-950 shadow-[0_-12px_34px_-24px_rgba(120,53,15,0.45)] backdrop-blur',
        specsShellClassName: 'rounded-lg border border-stone-200 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(120,53,15,0.35)]',
        specsFeatureCardClassName: 'rounded-lg border border-stone-200 bg-stone-50 px-4 py-4 text-sm leading-7 text-stone-800',
        specsFaqCardClassName: 'rounded-lg border border-stone-200 bg-white px-5 py-4 shadow-sm',
        relatedShellClassName: 'rounded-lg border border-stone-200 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(120,53,15,0.35)]',
        relatedGridClassName: 'grid gap-5 sm:grid-cols-2 lg:grid-cols-4',
        reviewShellClassName: 'rounded-lg border border-stone-200 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(120,53,15,0.3)]',
        reviewCardClassName: 'rounded-lg border border-stone-200 bg-stone-50 p-5',
        galleryShellClassName: 'rounded-lg border border-stone-200 bg-white p-4 shadow-[0_22px_60px_-42px_rgba(120,53,15,0.35)] sm:p-5',
    },
    pearl: {
        variant: 'pearl',
        philosophy: 'Luxury purchasing',
        categoryLabel: 'Atelier',
        highlightsLabel: 'Craft Notes',
        trustHeading: 'Boutique Assurance',
        stockLabel: 'Availability',
        skuLabel: 'Reference',
        selectedOptionLabel: 'Selected Finish',
        relatedTitle: 'Boutique Collection',
        reviewsTitle: 'Client Impressions',
        specsTitle: 'Craftsmanship',
        featureHeading: 'Refinements',
        faqHeading: 'Concierge Notes',
        galleryEyebrow: 'Luxury showcase',
        galleryAspectClassName: 'aspect-[4/5]',
        galleryMaxThumbnails: 5,
        sectionClassName: 'bg-[#fbfaf7]',
        heroGridClassName: 'grid gap-10 lg:grid-cols-[minmax(0,1.08fr)_minmax(360px,0.92fr)]',
        mediaWrapClassName: 'lg:sticky lg:top-24 lg:self-start',
        buyBoxClassName: 'space-y-6 border border-[#d8c7a3] bg-[#fffefa] p-6 shadow-[0_24px_70px_-48px_rgba(54,44,30,0.45)]',
        categoryChipClassName: 'inline-flex border-b border-[#b99a5f] pb-1 text-xs font-semibold uppercase tracking-[0.2em] text-[#7b6334]',
        titleClassName: 'font-serif text-4xl font-normal text-[#241d16] md:text-5xl',
        priceClassName: 'font-serif text-3xl text-[#241d16]',
        salePriceClassName: 'font-serif text-3xl text-[#7f1d1d]',
        comparePriceClassName: 'text-lg text-[#8a7c67] line-through',
        promoBadgeClassName: 'inline-flex border border-[#b99a5f] bg-[#fbfaf7] px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-[#6b5227]',
        highlightsPanelClassName: 'border-y border-[#d8c7a3] bg-[#fbfaf7] py-4',
        highlightItemClassName: 'border-b border-[#e5dcc8] py-2 text-sm text-[#3f3529] last:border-b-0',
        infoCardClassName: 'border border-[#d8c7a3] bg-white px-4 py-3',
        stockBadgeClassName: 'inline-flex items-center border border-[#d8c7a3] px-3 py-1 text-sm font-medium',
        ctaClassName: 'rounded-none bg-[#241d16] px-6 py-3 font-semibold text-white transition-opacity hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-[#b99a5f]/40 disabled:cursor-not-allowed disabled:opacity-50',
        secondaryCtaClassName: 'rounded-none border border-[#b99a5f] px-6 py-3 font-semibold text-[#4f3d20] transition-colors hover:bg-[#f4efe2] focus:outline-none focus:ring-2 focus:ring-[#b99a5f]/30',
        trustGridClassName: 'grid gap-3 sm:grid-cols-2',
        trustItemClassName: 'border border-[#d8c7a3] bg-white px-4 py-3',
        mobileBarClassName: 'border-[#d8c7a3] bg-[#fffefa]/95 text-[#241d16] shadow-[0_-12px_34px_-24px_rgba(54,44,30,0.45)] backdrop-blur',
        specsShellClassName: 'border border-[#d8c7a3] bg-[#fffefa] p-6 shadow-[0_24px_70px_-48px_rgba(54,44,30,0.35)]',
        specsFeatureCardClassName: 'border border-[#d8c7a3] bg-white px-4 py-4 text-sm leading-7 text-[#3f3529]',
        specsFaqCardClassName: 'border border-[#d8c7a3] bg-white px-5 py-4 shadow-sm',
        relatedShellClassName: 'border border-[#d8c7a3] bg-[#fffefa] p-6 shadow-[0_24px_70px_-48px_rgba(54,44,30,0.35)]',
        relatedGridClassName: 'grid gap-6 sm:grid-cols-2 lg:grid-cols-4',
        reviewShellClassName: 'border border-[#d8c7a3] bg-[#fffefa] p-6 shadow-[0_24px_70px_-48px_rgba(54,44,30,0.32)]',
        reviewCardClassName: 'border border-[#d8c7a3] bg-white p-5',
        galleryShellClassName: 'border border-[#d8c7a3] bg-[#fffefa] p-4 shadow-[0_24px_70px_-48px_rgba(54,44,30,0.35)] sm:p-5',
    },
    phoenix: {
        variant: 'phoenix',
        philosophy: 'Performance purchasing',
        categoryLabel: 'Drop',
        highlightsLabel: 'Performance Notes',
        trustHeading: 'Ready To Move',
        stockLabel: 'Availability',
        skuLabel: 'Style Code',
        selectedOptionLabel: 'Selected Fit',
        relatedTitle: 'Training Rotation',
        reviewsTitle: 'Athlete Notes',
        specsTitle: 'Performance Details',
        featureHeading: 'Movement Cues',
        faqHeading: 'Fit Questions',
        galleryEyebrow: 'Performance gallery',
        galleryAspectClassName: 'aspect-[3/4]',
        galleryMaxThumbnails: 6,
        sectionClassName: 'bg-[#ece9e2]',
        heroGridClassName: 'grid gap-8 lg:grid-cols-[minmax(0,1.1fr)_minmax(340px,0.9fr)]',
        mediaWrapClassName: 'lg:sticky lg:top-24 lg:self-start',
        buyBoxClassName: 'space-y-6 border border-black/10 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(19,19,22,0.42)]',
        categoryChipClassName: 'inline-flex border-l-2 border-[#cd1515] pl-2 text-xs font-bold uppercase tracking-[0.22em] text-[#131316]',
        titleClassName: 'text-4xl font-black uppercase leading-none text-[#131316] md:text-5xl',
        priceClassName: 'text-3xl font-black text-[#131316]',
        salePriceClassName: 'text-3xl font-black text-[#cd1515]',
        comparePriceClassName: 'text-lg font-semibold text-[#202021]/60 line-through',
        promoBadgeClassName: 'inline-flex bg-[#cd1515] px-3 py-1 text-xs font-bold uppercase tracking-[0.14em] text-white',
        highlightsPanelClassName: 'border-y border-black/10 py-4',
        highlightItemClassName: 'border-b border-black/10 py-2 text-sm font-semibold text-[#131316] last:border-b-0',
        infoCardClassName: 'border border-black/10 bg-white px-4 py-3',
        stockBadgeClassName: 'inline-flex items-center border border-black/10 px-3 py-1 text-sm font-bold',
        ctaClassName: 'rounded-none bg-[#cd1515] px-6 py-3 font-bold uppercase tracking-[0.08em] text-white transition-opacity hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-[#cd1515]/30 disabled:cursor-not-allowed disabled:opacity-50',
        secondaryCtaClassName: 'rounded-none border border-[#131316] px-6 py-3 font-bold uppercase tracking-[0.08em] text-[#131316] transition-colors hover:bg-[#131316] hover:text-white focus:outline-none focus:ring-2 focus:ring-[#131316]/20',
        trustGridClassName: 'grid gap-3 sm:grid-cols-2',
        trustItemClassName: 'border border-black/10 bg-white px-4 py-3',
        mobileBarClassName: 'border-black/10 bg-[#ece9e2]/95 text-[#131316] shadow-[0_-12px_34px_-24px_rgba(19,19,22,0.45)] backdrop-blur',
        specsShellClassName: 'border border-black/10 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(19,19,22,0.35)]',
        specsFeatureCardClassName: 'border border-black/10 bg-[#ece9e2] px-4 py-4 text-sm leading-7 text-[#131316]',
        specsFaqCardClassName: 'border border-black/10 bg-white px-5 py-4 shadow-sm',
        relatedShellClassName: 'border border-black/10 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(19,19,22,0.35)]',
        relatedGridClassName: 'grid gap-5 sm:grid-cols-2 lg:grid-cols-4',
        reviewShellClassName: 'border border-black/10 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(19,19,22,0.3)]',
        reviewCardClassName: 'border border-black/10 bg-[#ece9e2] p-5',
        galleryShellClassName: 'border border-black/10 bg-white p-4 shadow-[0_22px_60px_-42px_rgba(19,19,22,0.35)] sm:p-5',
    },
    hype: {
        variant: 'hype',
        philosophy: 'Launch-drop buying confidence',
        categoryLabel: 'Drop Category',
        highlightsLabel: 'Decision Specs',
        trustHeading: 'Hype Confidence',
        stockLabel: 'Live Stock',
        skuLabel: 'Model / SKU',
        selectedOptionLabel: 'Selected Setup',
        relatedTitle: 'Compatible Drops',
        reviewsTitle: 'Verified Hype Feedback',
        specsTitle: 'Tech Specifications',
        featureHeading: 'Core Specs',
        faqHeading: 'Support Questions',
        galleryEyebrow: 'Drop media',
        galleryAspectClassName: 'aspect-[4/3]',
        galleryMaxThumbnails: 8,
        sectionClassName: 'border-y border-slate-200 bg-white',
        heroGridClassName: 'grid gap-6 lg:grid-cols-[minmax(0,0.95fr)_minmax(400px,1.05fr)]',
        mediaWrapClassName: 'lg:sticky lg:top-24 lg:self-start',
        buyBoxClassName: 'space-y-5 rounded-lg border border-slate-200 bg-white p-5 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.45)]',
        categoryChipClassName: 'inline-flex rounded-full border border-[#F06736]/25 bg-[#F06736]/10 px-3 py-1 text-xs font-bold uppercase tracking-[0.14em] text-[#F06736]',
        titleClassName: 'text-3xl font-black text-[#131316] md:text-4xl',
        priceClassName: 'text-3xl font-black text-[#F06736]',
        salePriceClassName: 'text-3xl font-black text-[#F54E05]',
        comparePriceClassName: 'text-lg text-slate-500 line-through',
        promoBadgeClassName: 'inline-flex rounded-sm bg-[#27CDE4] px-3 py-1 text-xs font-bold uppercase tracking-[0.12em] text-[#141218]',
        highlightsPanelClassName: 'rounded-lg border border-[#F06736]/20 bg-[#F06736]/5 p-4',
        highlightItemClassName: 'rounded-md border border-slate-100 bg-white px-3 py-2 text-sm font-semibold text-[#131316]',
        infoCardClassName: 'rounded-lg border border-slate-200 bg-slate-50 px-4 py-3',
        stockBadgeClassName: 'inline-flex items-center rounded-full px-3 py-1 text-sm font-bold',
        ctaClassName: 'rounded-full bg-[#F06736] px-6 py-3 font-bold text-white transition-opacity hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-[#F06736]/30 disabled:cursor-not-allowed disabled:opacity-50',
        secondaryCtaClassName: 'rounded-full border border-slate-300 px-6 py-3 font-bold text-slate-700 transition-colors hover:border-[#F06736] hover:text-[#F06736] focus:outline-none focus:ring-2 focus:ring-[#F06736]/20',
        trustGridClassName: 'grid gap-3 sm:grid-cols-2',
        trustItemClassName: 'rounded-lg border border-slate-200 bg-white px-4 py-3',
        mobileBarClassName: 'border-slate-200 bg-white/95 text-[#131316] shadow-[0_-12px_34px_-24px_rgba(15,23,42,0.45)] backdrop-blur',
        specsShellClassName: 'rounded-lg border border-slate-200 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.35)]',
        specsFeatureCardClassName: 'rounded-lg border border-slate-200 bg-slate-50 px-4 py-4 text-sm leading-7 text-[#131316]',
        specsFaqCardClassName: 'rounded-lg border border-slate-200 bg-white px-5 py-4 shadow-sm',
        relatedShellClassName: 'rounded-lg border border-slate-200 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.35)]',
        relatedGridClassName: 'grid gap-4 sm:grid-cols-2 lg:grid-cols-4',
        reviewShellClassName: 'rounded-lg border border-slate-200 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.3)]',
        reviewCardClassName: 'rounded-lg border border-slate-200 bg-slate-50 p-5',
        galleryShellClassName: 'rounded-lg border border-slate-200 bg-white p-4 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.35)] sm:p-5',
    },
    electronics: {
        variant: 'electronics',
        philosophy: 'Technical buying confidence',
        categoryLabel: 'Category',
        highlightsLabel: 'Decision Support',
        trustHeading: 'Technical Confidence',
        stockLabel: 'Live Availability',
        skuLabel: 'Model / SKU',
        selectedOptionLabel: 'Configuration',
        relatedTitle: 'Compatible Products',
        reviewsTitle: 'Verified Buyer Feedback',
        specsTitle: 'Technical Specifications',
        featureHeading: 'Core Specifications',
        faqHeading: 'Support Questions',
        galleryEyebrow: 'Technical media',
        galleryAspectClassName: 'aspect-[4/3]',
        galleryMaxThumbnails: 8,
        sectionClassName: 'border-y border-slate-200 bg-[#f7fbff]',
        heroGridClassName: 'grid gap-6 lg:grid-cols-[minmax(0,0.95fr)_minmax(400px,1.05fr)]',
        mediaWrapClassName: 'lg:sticky lg:top-24 lg:self-start',
        buyBoxClassName: 'space-y-5 rounded-lg border border-slate-300 bg-white p-5 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.45)]',
        categoryChipClassName: 'inline-flex rounded-md border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-sky-800',
        titleClassName: 'text-3xl font-bold text-slate-950 md:text-4xl',
        priceClassName: 'text-3xl font-bold text-sky-700',
        salePriceClassName: 'text-3xl font-bold text-red-600',
        comparePriceClassName: 'text-lg text-slate-500 line-through',
        promoBadgeClassName: 'inline-flex rounded-md bg-sky-700 px-3 py-1 text-xs font-semibold text-white',
        highlightsPanelClassName: 'rounded-lg border border-sky-200 bg-sky-50 p-4',
        highlightItemClassName: 'rounded-md border border-sky-100 bg-white px-3 py-2 text-sm font-semibold text-slate-900',
        infoCardClassName: 'rounded-lg border border-slate-300 bg-slate-50 px-4 py-3',
        stockBadgeClassName: 'inline-flex items-center rounded-md px-3 py-1 text-sm font-semibold',
        ctaClassName: 'rounded-md bg-sky-700 px-6 py-3 font-semibold text-white transition-opacity hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-sky-300 disabled:cursor-not-allowed disabled:opacity-50',
        secondaryCtaClassName: 'rounded-md border border-slate-300 px-6 py-3 font-semibold text-slate-700 transition-colors hover:border-sky-700 hover:text-sky-800 focus:outline-none focus:ring-2 focus:ring-sky-200',
        trustGridClassName: 'grid gap-3 sm:grid-cols-2',
        trustItemClassName: 'rounded-lg border border-slate-300 bg-white px-4 py-3',
        mobileBarClassName: 'border-slate-300 bg-[#f7fbff]/95 text-slate-950 shadow-[0_-12px_34px_-24px_rgba(15,23,42,0.45)] backdrop-blur',
        specsShellClassName: 'rounded-lg border border-slate-300 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.35)]',
        specsFeatureCardClassName: 'rounded-lg border border-slate-300 bg-slate-50 px-4 py-4 font-mono text-sm leading-7 text-slate-900',
        specsFaqCardClassName: 'rounded-lg border border-slate-300 bg-white px-5 py-4 shadow-sm',
        relatedShellClassName: 'rounded-lg border border-slate-300 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.35)]',
        relatedGridClassName: 'grid gap-4 sm:grid-cols-2 lg:grid-cols-4',
        reviewShellClassName: 'rounded-lg border border-slate-300 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.3)]',
        reviewCardClassName: 'rounded-lg border border-slate-300 bg-slate-50 p-5',
        galleryShellClassName: 'rounded-lg border border-slate-300 bg-white p-4 shadow-[0_22px_60px_-42px_rgba(15,23,42,0.35)] sm:p-5',
    },
    cosmetics: {
        variant: 'cosmetics',
        philosophy: 'Sensory product confidence',
        categoryLabel: 'Ritual',
        highlightsLabel: 'Benefits',
        trustHeading: 'Care Confidence',
        stockLabel: 'Availability',
        skuLabel: 'Reference',
        selectedOptionLabel: 'Selected Option',
        relatedTitle: 'More To Explore',
        reviewsTitle: 'What Customers Say',
        specsTitle: 'Ingredients & Benefits',
        featureHeading: 'Benefits',
        faqHeading: 'Questions',
        galleryEyebrow: 'Product gallery',
        galleryAspectClassName: 'aspect-[4/5]',
        galleryMaxThumbnails: 6,
        sectionClassName: 'bg-[#fff7fb]',
        heroGridClassName: 'grid gap-8 lg:grid-cols-[minmax(0,1fr)_minmax(340px,1fr)]',
        mediaWrapClassName: 'lg:sticky lg:top-24 lg:self-start',
        buyBoxClassName: 'space-y-5 rounded-lg border border-rose-100 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(190,24,93,0.28)]',
        categoryChipClassName: 'inline-flex rounded-md border border-rose-200 bg-rose-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-rose-700',
        titleClassName: 'text-3xl font-semibold text-rose-950 md:text-4xl',
        priceClassName: 'text-3xl font-bold text-rose-700',
        salePriceClassName: 'text-3xl font-bold text-red-600',
        comparePriceClassName: 'text-lg text-rose-400 line-through',
        promoBadgeClassName: 'inline-flex rounded-md bg-rose-600 px-3 py-1 text-xs font-semibold text-white',
        highlightsPanelClassName: 'rounded-lg border border-rose-100 bg-rose-50 p-4',
        highlightItemClassName: 'rounded-md bg-white px-3 py-2 text-sm font-medium text-rose-950 shadow-sm',
        infoCardClassName: 'rounded-lg border border-rose-100 bg-white px-4 py-3 shadow-sm',
        stockBadgeClassName: 'inline-flex items-center rounded-md px-3 py-1 text-sm font-semibold',
        ctaClassName: 'rounded-md bg-rose-600 px-6 py-3 font-semibold text-white transition-opacity hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-rose-200 disabled:cursor-not-allowed disabled:opacity-50',
        secondaryCtaClassName: 'rounded-md border border-rose-200 px-6 py-3 font-semibold text-rose-700 transition-colors hover:border-rose-500 hover:text-rose-800 focus:outline-none focus:ring-2 focus:ring-rose-200',
        trustGridClassName: 'grid gap-3 sm:grid-cols-2',
        trustItemClassName: 'rounded-lg border border-rose-100 bg-white px-4 py-3',
        mobileBarClassName: 'border-rose-100 bg-white/95 text-rose-950 shadow-[0_-12px_34px_-24px_rgba(190,24,93,0.28)] backdrop-blur',
        specsShellClassName: 'rounded-lg border border-rose-100 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(190,24,93,0.22)]',
        specsFeatureCardClassName: 'rounded-lg border border-rose-100 bg-rose-50 px-4 py-4 text-sm leading-7 text-rose-950',
        specsFaqCardClassName: 'rounded-lg border border-rose-100 bg-white px-5 py-4 shadow-sm',
        relatedShellClassName: 'rounded-lg border border-rose-100 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(190,24,93,0.22)]',
        relatedGridClassName: 'grid gap-4 sm:grid-cols-2 lg:grid-cols-4',
        reviewShellClassName: 'rounded-lg border border-rose-100 bg-white p-6 shadow-[0_22px_60px_-42px_rgba(190,24,93,0.22)]',
        reviewCardClassName: 'rounded-lg border border-rose-100 bg-rose-50 p-5',
        galleryShellClassName: 'rounded-lg border border-rose-100 bg-white p-4 shadow-[0_22px_60px_-42px_rgba(190,24,93,0.22)] sm:p-5',
    },
};

const LAYOUT_VARIANT_ALIASES: Record<string, PurchaseExperienceVariant> = {
    default: 'modern',
    modern: 'modern',
    minimal: 'minimal',
    fashion: 'fashion',
    pearl: 'pearl',
    phoenix: 'phoenix',
    hype: 'hype',
    electronics: 'electronics',
    cosmetics: 'cosmetics',
};

const THEME_VARIANT_ALIASES = new Map(
    themeAuthority.themes.map((theme) => [theme.id, theme.supportedPurchaseProfiles[0]])
);

function normalize(value?: string | null): string {
    return value?.trim().toLowerCase() || '';
}

export function resolvePurchaseExperienceVariant(
    layoutVariant?: string | null,
    themeId?: string | null
): PurchaseExperienceVariant {
    const normalizedLayout = normalize(layoutVariant);

    if (normalizedLayout && normalizedLayout !== 'default') {
        return LAYOUT_VARIANT_ALIASES[normalizedLayout] ?? 'modern';
    }

    return (THEME_VARIANT_ALIASES.get(normalize(themeId)) as PurchaseExperienceVariant | undefined) ?? 'modern';
}

export function getPurchaseExperienceProfile(
    layoutVariant?: string | null,
    themeId?: string | null
): PurchaseExperienceProfile {
    return PROFILE_BY_VARIANT[resolvePurchaseExperienceVariant(layoutVariant, themeId)];
}

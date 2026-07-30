import React from 'react';
import {
    ThemeLabel,
    ThemeProductMeta,
    ThemeProductTitle,
    ThemeSurface,
} from '@/components/theme-components';
import {
    ProductCardLink,
    ProductCardMedia,
    ProductCardPrice,
    ProductCardQuickAddButton,
    ProductCardRuntime,
    ProductCardWishlist,
} from './ProductCardRuntime';
import type { ProductCardRendererId } from './productCardRendererResolver';

export type ProductCardRenderer = React.FC<{ runtime: ProductCardRuntime }>;

export const ModernProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="modern"
        className="group relative"
        data-product-card-theme="modern"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-3 top-3 z-20">
            <ProductCardWishlist runtime={runtime} size="small" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-square overflow-hidden bg-muted/5"
                imageClassName="h-full w-full object-cover transition-transform duration-slow group-hover:scale-105"
                sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 320px"
            />

            <div className="space-y-3 p-4 sm:p-5">
                {runtime.categoryName ? (
                    <ThemeProductMeta themeId="modern">
                        {runtime.categoryName}
                    </ThemeProductMeta>
                ) : null}

                <ThemeProductTitle themeId="modern">
                    {runtime.displayName}
                </ThemeProductTitle>

                <div className="flex items-center justify-between gap-3">
                    <ProductCardPrice runtime={runtime} />
                    <ThemeLabel themeId="modern">
                        {runtime.inStock ? runtime.labels.inStock : runtime.labels.outOfStock}
                    </ThemeLabel>
                </div>
            </div>
        </ProductCardLink>
    </ThemeSurface>
);

export const MinimalProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="minimal"
        className="group relative p-2"
        data-product-card-theme="minimal"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-3 top-3 z-20">
            <ProductCardWishlist runtime={runtime} size="small" />
        </div>

        <ProductCardLink runtime={runtime} className="grid gap-3">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-[5/6] overflow-hidden bg-muted/5"
                imageClassName="h-full w-full object-cover transition-opacity duration-fast group-hover:opacity-90"
                sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 280px"
            />

            <div className="grid gap-2 px-1 pb-1">
                <div className="flex items-start justify-between gap-3">
                    <ThemeProductTitle themeId="minimal">
                        {runtime.displayName}
                    </ThemeProductTitle>
                    {runtime.categoryName ? (
                        <ThemeProductMeta themeId="minimal" className="max-w-[7rem] truncate text-right">
                            {runtime.categoryName}
                        </ThemeProductMeta>
                    ) : null}
                </div>

                <ProductCardPrice
                    runtime={runtime}
                    className="flex items-baseline gap-2 border-t border-muted/15 pt-2"
                    textClassName="text-sm font-medium text-muted"
                    currentClassName="text-sm font-semibold text-text"
                    saleClassName="text-sm font-semibold text-primary"
                    compareClassName="text-xs text-muted line-through"
                />
            </div>
        </ProductCardLink>
    </ThemeSurface>
);

export const FashionProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="fashion"
        className="group relative h-full bg-white"
        data-product-card-theme="fashion"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-4 top-4 z-20">
            <ProductCardWishlist runtime={runtime} size="medium" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-[4/5] overflow-hidden border border-slate-200 bg-slate-50"
                imageClassName="h-full w-full object-contain p-4 transition-transform duration-slow group-hover:scale-[1.04]"
                sizes="(max-width: 768px) 90vw, (max-width: 1280px) 40vw, 420px"
            />

            <div className="space-y-3 py-4">
                {runtime.categoryName ? (
                    <ThemeProductMeta themeId="fashion" className="text-primary">
                        {runtime.categoryName}
                    </ThemeProductMeta>
                ) : null}

                <ThemeProductTitle themeId="fashion" className="min-h-[4.25rem] overflow-hidden [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">
                    {runtime.displayName}
                </ThemeProductTitle>

                {runtime.alternateName ? (
                    <ThemeProductMeta themeId="fashion" dir={runtime.alternateName.dir}>
                        {runtime.alternateName.value}
                    </ThemeProductMeta>
                ) : null}

                <div className="flex items-end justify-between gap-4 border-t border-muted/15 pt-4">
                    <ProductCardPrice
                        runtime={runtime}
                        className="flex flex-col gap-1"
                        textClassName="text-base font-medium text-muted"
                        currentClassName="text-xl font-semibold text-text"
                        saleClassName="text-xl font-semibold text-primary"
                        compareClassName="text-sm text-muted line-through"
                    />
                    <ThemeLabel themeId="fashion">
                        {runtime.inStock ? runtime.labels.inStock : runtime.labels.outOfStock}
                    </ThemeLabel>
                </div>
            </div>
        </ProductCardLink>
    </ThemeSurface>
);

export const SevenProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="seven"
        className="group relative h-full bg-white"
        data-product-card-theme="seven"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-4 top-4 z-20">
            <ProductCardWishlist runtime={runtime} size="medium" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-[4/5] overflow-hidden bg-white"
                imageClassName="h-full w-full object-contain p-4 transition-transform duration-slow group-hover:scale-[1.04]"
                sizes="(max-width: 768px) 90vw, (max-width: 1280px) 40vw, 420px"
            />

            {runtime.price.kind === 'sale' ? (
                <div className="absolute left-3 top-3 z-10 rounded-none bg-[#CD1515] px-2.5 py-1 text-[10px] font-black uppercase tracking-[0.12em] text-white">
                    Sale!
                </div>
            ) : null}

            <div className="space-y-3 py-4">
                {runtime.categoryName ? (
                    <ThemeProductMeta themeId="seven" className="text-primary">
                        {runtime.categoryName}
                    </ThemeProductMeta>
                ) : null}

                <ThemeProductTitle themeId="seven" className="min-h-[4.25rem] overflow-hidden [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">
                    {runtime.displayName}
                </ThemeProductTitle>

                {runtime.alternateName ? (
                    <ThemeProductMeta themeId="seven" dir={runtime.alternateName.dir}>
                        {runtime.alternateName.value}
                    </ThemeProductMeta>
                ) : null}

                <div className="flex items-end justify-between gap-4 border-t border-muted/15 pt-4">
                    <ProductCardPrice
                        runtime={runtime}
                        className="flex flex-col gap-1"
                        textClassName="text-base font-medium text-muted"
                        currentClassName="text-xl font-semibold text-text"
                        saleClassName="text-xl font-semibold text-[#CD1515]"
                        compareClassName="text-sm text-muted line-through"
                    />
                    {runtime.canQuickAdd ? (
                        <ProductCardQuickAddButton
                            runtime={runtime}
                            className="text-xs font-black uppercase tracking-[0.12em] text-primary"
                        />
                    ) : (
                        <ThemeLabel themeId="seven">
                            {runtime.inStock ? runtime.labels.inStock : runtime.labels.outOfStock}
                        </ThemeLabel>
                    )}
                </div>
            </div>
        </ProductCardLink>
    </ThemeSurface>
);

export const PearlProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="pearl"
        className="group relative"
        data-product-card-theme="pearl"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-3 top-3 z-20">
            <ProductCardWishlist runtime={runtime} size="small" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-[4/5] overflow-hidden bg-muted/5"
                imageClassName="h-full w-full object-cover transition-transform duration-slow group-hover:scale-[1.03]"
                sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 360px"
            />

            <div className="space-y-3 p-4">
                {runtime.categoryName ? (
                    <ThemeProductMeta themeId="pearl">
                        {runtime.categoryName}
                    </ThemeProductMeta>
                ) : null}

                <ThemeProductTitle themeId="pearl">
                    {runtime.displayName}
                </ThemeProductTitle>

                <div className="flex items-center justify-between gap-3 border-t border-secondary/30 pt-3">
                    <ProductCardPrice
                        runtime={runtime}
                        className="flex items-baseline gap-2"
                        textClassName="text-sm font-medium text-muted"
                        currentClassName="text-base font-semibold text-text"
                        saleClassName="text-base font-semibold text-primary"
                        compareClassName="text-xs text-muted line-through"
                    />
                    <span className="h-2 w-2 rounded-full bg-secondary" aria-hidden="true" />
                </div>
            </div>
        </ProductCardLink>
    </ThemeSurface>
);

export const PhoenixProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="phoenix"
        className="group relative"
        data-product-card-theme="phoenix"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-4 top-4 z-20">
            <ProductCardWishlist runtime={runtime} size="small" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-[3/4] overflow-hidden bg-muted/5"
                imageClassName="h-full w-full object-cover transition-transform duration-slow group-hover:scale-[1.04]"
                sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 360px"
            />

            <div className="space-y-3 p-4">
                {runtime.categoryName ? (
                    <ThemeProductMeta themeId="phoenix">
                        {runtime.categoryName}
                    </ThemeProductMeta>
                ) : null}

                <ThemeProductTitle themeId="phoenix">
                    {runtime.displayName}
                </ThemeProductTitle>

                <div className="flex items-center justify-between gap-3 border-t border-black/10 pt-3">
                    <ProductCardPrice
                        runtime={runtime}
                        currentClassName="text-lg font-black text-primary"
                        saleClassName="text-lg font-black text-primary"
                        compareClassName="text-xs font-semibold text-muted line-through"
                    />
                    <ThemeLabel themeId="phoenix">
                        {runtime.inStock ? runtime.labels.inStock : runtime.labels.outOfStock}
                    </ThemeLabel>
                </div>
            </div>
        </ProductCardLink>
    </ThemeSurface>
);

export const ElectronicsProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="electronics"
        className="group relative"
        data-product-card-theme="electronics"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-3 top-3 z-20">
            <ProductCardWishlist runtime={runtime} size="small" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-product overflow-hidden bg-muted/5"
                imageClassName="h-full w-full object-contain p-4 transition-transform duration-normal group-hover:scale-105"
                sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 320px"
            />

            <div className="space-y-3 p-4">
                <ThemeProductTitle themeId="electronics">
                    {runtime.displayName}
                </ThemeProductTitle>
                <div className="flex items-center justify-between gap-3">
                    <ProductCardPrice runtime={runtime} />
                </div>
            </div>
        </ProductCardLink>

        <div className="px-4 pb-4">
            <ProductCardQuickAddButton
                runtime={runtime}
                className="inline-flex min-h-9 w-full items-center justify-center gap-1 rounded-md bg-primary px-3 py-2 text-xs font-semibold uppercase tracking-[0.12em] text-white transition-colors duration-fast hover:bg-primary/90 disabled:bg-muted disabled:text-white disabled:cursor-not-allowed"
            />
        </div>
    </ThemeSurface>
);

export const HypeProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="hype"
        className="group relative"
        data-product-card-theme="hype"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-3 top-3 z-20">
            <ProductCardWishlist runtime={runtime} size="small" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-product overflow-hidden bg-muted/5"
                imageClassName="h-full w-full object-contain p-4 transition-transform duration-normal group-hover:scale-105"
                sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 320px"
            />

            <div className="space-y-3 p-4">
                <ThemeProductTitle themeId="hype">
                    {runtime.displayName}
                </ThemeProductTitle>
                <div className="flex items-center justify-between gap-3">
                    <ProductCardPrice runtime={runtime} />
                </div>
            </div>
        </ProductCardLink>

        <div className="px-4 pb-4">
            <ProductCardQuickAddButton
                runtime={runtime}
                className="inline-flex min-h-9 w-full items-center justify-center gap-1 rounded-full bg-primary px-3 py-2 text-xs font-bold uppercase tracking-[0.12em] text-white transition-colors duration-fast hover:bg-primary/90 disabled:bg-muted disabled:text-white disabled:cursor-not-allowed"
            />
        </div>
    </ThemeSurface>
);

export const AlizonProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="alizon"
        className="group relative"
        data-product-card-theme="alizon"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-3 top-3 z-20">
            <ProductCardWishlist runtime={runtime} size="small" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-[4/3] overflow-hidden bg-muted/5"
                imageClassName="h-full w-full object-contain p-4 transition-transform duration-normal group-hover:scale-105"
                sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 320px"
            />

            <div className="space-y-3 p-4">
                <ThemeProductTitle themeId="alizon">
                    {runtime.displayName}
                </ThemeProductTitle>
                <div className="flex items-center justify-between gap-3">
                    <ProductCardPrice runtime={runtime} />
                </div>
            </div>
        </ProductCardLink>

        <div className="px-4 pb-4">
            <ProductCardQuickAddButton
                runtime={runtime}
                className="inline-flex min-h-9 w-full items-center justify-center gap-1 rounded-none bg-primary px-3 py-2 text-xs font-semibold uppercase tracking-[0.12em] text-white transition-colors duration-fast hover:bg-primary/90 disabled:bg-muted disabled:text-white disabled:cursor-not-allowed"
            />
        </div>
    </ThemeSurface>
);

export const SharpProductCardRenderer: ProductCardRenderer = ({ runtime }) => (
    <ThemeSurface
        themeId="sharp"
        className="group relative"
        data-product-card-theme="sharp"
        data-product-card-layout={runtime.layout}
    >
        <div className="absolute right-3 top-3 z-20">
            <ProductCardWishlist runtime={runtime} size="small" />
        </div>

        <ProductCardLink runtime={runtime} className="block">
            <ProductCardMedia
                runtime={runtime}
                className="relative aspect-square overflow-hidden bg-muted/5"
                imageClassName="h-full w-full object-cover transition-transform duration-slow group-hover:scale-105"
                sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 320px"
            />

            <div className="space-y-3 p-4">
                {runtime.categoryName ? (
                    <ThemeProductMeta themeId="sharp">
                        {runtime.categoryName}
                    </ThemeProductMeta>
                ) : null}

                <ThemeProductTitle themeId="sharp">
                    {runtime.displayName}
                </ThemeProductTitle>

                <div className="flex items-center justify-between gap-3 border-t border-secondary/30 pt-3">
                    <ProductCardPrice
                        runtime={runtime}
                        className="flex items-baseline gap-2"
                        textClassName="text-sm font-medium text-muted"
                        currentClassName="text-base font-semibold text-text"
                        saleClassName="text-base font-semibold text-primary"
                        compareClassName="text-xs text-muted line-through"
                    />
                    <span className="h-2 w-2 rounded-full bg-secondary" aria-hidden="true" />
                </div>
            </div>
        </ProductCardLink>
    </ThemeSurface>
);

export const PRODUCT_CARD_RENDERERS: Record<ProductCardRendererId, ProductCardRenderer> = {
    modern: ModernProductCardRenderer,
    minimal: MinimalProductCardRenderer,
    fashion: FashionProductCardRenderer,
    pearl: PearlProductCardRenderer,
    phoenix: PhoenixProductCardRenderer,
    hype: HypeProductCardRenderer,
    electronics: ElectronicsProductCardRenderer,
    alizon: AlizonProductCardRenderer,
    sharp: SharpProductCardRenderer,
    seven: SevenProductCardRenderer,
};

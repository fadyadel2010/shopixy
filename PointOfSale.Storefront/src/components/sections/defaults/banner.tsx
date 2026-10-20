import React from 'react';
import { MediaFrame, OptimizedImage } from '@/components/media';
import { AffiliateLink } from '@/components/common/AffiliateLink';

const BannerSection: React.FC<{
    title?: string; subtitle?: string; ctaText?: string; ctaLink?: string;
    imageUrl?: string; backgroundImage?: string;
}> = ({
    title,
    subtitle,
    ctaText,
    ctaLink,
    imageUrl,
    backgroundImage,
}) => {
    const artwork = imageUrl || backgroundImage;

    return (
        <section className="py-12 px-4">
            <div className="container mx-auto">
                <div className="relative overflow-hidden rounded-[32px] border border-slate-200 bg-[linear-gradient(135deg,#0f172a_0%,#1e293b_48%,#f59e0b_145%)] text-white shadow-[0_28px_90px_-52px_rgba(245,158,11,0.8)]">
                    <div className="grid gap-8 px-6 py-8 md:px-8 lg:grid-cols-[1fr_auto] lg:items-center">
                        <div className="max-w-3xl">
                            <p className="text-sm font-semibold uppercase tracking-[0.26em] text-white/70">
                                Featured promotion
                            </p>
                            <h2 className="mt-4 text-3xl font-semibold tracking-tight md:text-4xl">
                                {title || 'Promote your best offer'}
                            </h2>
                            {subtitle ? (
                                <p className="mt-4 max-w-2xl text-base leading-7 text-white/78">
                                    {subtitle}
                                </p>
                            ) : null}

                            {ctaText ? (
                                <AffiliateLink
                                    href={ctaLink || '/products'}
                                    className="mt-6 inline-flex rounded-full bg-white px-6 py-3 text-sm font-semibold text-slate-950 transition-opacity hover:opacity-90"
                                >
                                    {ctaText}
                                </AffiliateLink>
                            ) : null}
                        </div>

                        <MediaFrame role="banner" className="rounded-[24px] border border-white/15 bg-white/10 lg:w-[20rem]">
                            {artwork ? (
                                <OptimizedImage
                                    src={artwork}
                                    alt={title || 'Banner artwork'}
                                    className="h-48 w-full object-cover"
                                />
                            ) : (
                                <div className="grid h-48 place-items-center text-sm text-white/72">
                                    Campaign artwork
                                </div>
                            )}
                        </MediaFrame>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default BannerSection;

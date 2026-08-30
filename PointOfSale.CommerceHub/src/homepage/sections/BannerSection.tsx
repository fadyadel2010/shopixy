import { CheckCircle2 } from "lucide-react";
import { Link } from "react-router-dom";

import type { BannerSectionData } from "@/homepage/types";
import { resolveSafeNavigationTarget } from "@/security/safeNavigationTarget";
import { Button } from "@/ui/button";

type BannerSectionProps = {
  data: BannerSectionData;
};

export const BannerSection = ({ data }: BannerSectionProps) => {
  return (
    <section className="brand-gradient relative overflow-hidden rounded-[2rem] px-6 py-8 text-white shadow-glow md:px-8 md:py-10">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(255,255,255,0.2),transparent_28%)]" aria-hidden="true" />
      <div className="relative grid gap-6 xl:grid-cols-[minmax(0,1fr)_auto] xl:items-center">
        <div className="space-y-4">
          <p className="text-xs font-semibold uppercase tracking-[0.28em] text-white/80">Shopixy promotion</p>
          <div className="space-y-3">
            <h2 className="max-w-3xl font-display text-3xl font-bold tracking-tight text-balance md:text-4xl">{data.title}</h2>
            <p className="max-w-2xl text-sm leading-7 text-white/82 md:text-base">{data.subtitle}</p>
          </div>

          <div className="grid gap-3 md:grid-cols-3">
            {[
              "Persisted by schema, not hardcoded markup",
              "Ready for admin-driven ordering and visibility",
              "Designed to evolve into a Shopify-like builder"
            ].map((highlight) => (
              <div key={highlight} className="rounded-[1.5rem] border border-white/20 bg-white/10 px-4 py-4 backdrop-blur">
                <div className="flex items-start gap-3">
                  <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-white" />
                  <p className="text-sm leading-6 text-white/88">{highlight}</p>
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="flex flex-col gap-3 xl:min-w-[15rem]">
          <Button asChild variant="secondary" size="lg" className="bg-white text-slate-950 hover:bg-white/92">
            <Link to={resolveSafeNavigationTarget(data.ctaLink)}>{data.ctaText}</Link>
          </Button>
          <img
            src={data.imageUrl}
            alt={data.title}
            className="h-32 w-full rounded-[1.5rem] border border-white/20 object-cover shadow-soft"
            loading="lazy"
          />
        </div>
      </div>
    </section>
  );
};

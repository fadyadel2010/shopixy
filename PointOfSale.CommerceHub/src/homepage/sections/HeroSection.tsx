import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";

import type { HeroSectionData } from "@/homepage/types";
import { Button } from "@/ui/button";

type HeroSectionProps = {
  data: HeroSectionData;
};

export const HeroSection = ({ data }: HeroSectionProps) => {
  return (
    <section className="relative overflow-hidden rounded-[2.5rem] border border-white/70 bg-white shadow-card">
      <div className="absolute inset-0 grid xl:grid-cols-[1.15fr_0.85fr]" aria-hidden="true">
        <div className="brand-gradient-soft" />
        <div className="bg-slate-950/4" />
      </div>

      <div className="relative grid gap-8 px-6 py-8 md:px-8 md:py-10 xl:grid-cols-[minmax(0,1.1fr)_minmax(20rem,0.9fr)] xl:items-center xl:px-10 xl:py-12">
        <div className="space-y-6">
          <div className="inline-flex rounded-full border border-primary/15 bg-primary/8 px-4 py-2 text-[0.7rem] font-semibold uppercase tracking-[0.26em] text-primary">
            Shopixy Marketplace
          </div>

          <div className="space-y-4">
            <h1 className="max-w-4xl font-display text-4xl font-bold leading-tight text-balance text-foreground md:text-6xl">
              {data.title}
            </h1>
            <p className="max-w-2xl text-base leading-7 text-muted-foreground md:text-lg">{data.subtitle}</p>
          </div>

          <div className="flex flex-wrap gap-3">
            <Button asChild size="lg" className="gap-2">
              <Link to={data.ctaLink}>
                {data.ctaText}
                <ArrowRight className="h-4 w-4" />
              </Link>
            </Button>
            <Button asChild size="lg" variant="secondary">
              <Link to="/products">Explore all products</Link>
            </Button>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            {[
              "Curated multi-vendor discovery",
              "Builder-ready section architecture",
              "Premium storefront polish"
            ].map((highlight) => (
              <div key={highlight} className="rounded-[1.5rem] border border-white/70 bg-white/80 px-4 py-4 shadow-soft backdrop-blur">
                <p className="text-sm font-semibold text-foreground">{highlight}</p>
              </div>
            ))}
          </div>
        </div>

        <div className="relative overflow-hidden rounded-[2rem] border border-slate-200/70 bg-slate-950 shadow-[0_30px_80px_-50px_rgba(15,23,42,0.85)]">
          <img src={data.imageUrl} alt={data.title} className="h-full min-h-[18rem] w-full object-cover" loading="eager" />
          <div className="absolute inset-0 bg-gradient-to-t from-slate-950/90 via-slate-950/20 to-transparent" />
          <div className="absolute inset-x-0 bottom-0 space-y-2 p-6 text-white">
            <p className="text-xs font-semibold uppercase tracking-[0.24em] text-white/70">Builder-ready storytelling</p>
            <p className="max-w-md text-sm leading-6 text-white/82">
              Each homepage block now comes from persisted section data, giving Shopixy a clean runway into a future visual builder.
            </p>
          </div>
        </div>
      </div>
    </section>
  );
};

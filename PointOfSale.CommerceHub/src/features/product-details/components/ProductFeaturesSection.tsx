import { CheckCircle2, Sparkles } from "lucide-react";

type ProductFeaturesSectionProps = {
  features: string[];
};

export const ProductFeaturesSection = ({ features }: ProductFeaturesSectionProps) => {
  if (features.length === 0) {
    return null;
  }

  return (
    <div className="space-y-6">
      <div className="rounded-[2rem] border border-white/75 bg-white/92 p-6 shadow-[0_30px_60px_-42px_rgba(15,23,42,0.45)]">
        <div className="flex items-start gap-4">
          <div className="brand-gradient-soft rounded-[1.5rem] p-3 text-primary">
            <Sparkles className="h-5 w-5" />
          </div>
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">Feature set</p>
            <h2 className="text-2xl font-black tracking-tight text-foreground">What stands out about this product</h2>
            <p className="text-sm leading-7 text-muted-foreground">
              Structured highlights come directly from the product data model, so this section stays clean and consistent across listings.
            </p>
          </div>
        </div>
      </div>

      <ul className="grid gap-4 md:grid-cols-2">
        {features.map((feature) => (
          <li
            key={feature}
            className="flex gap-3 rounded-[1.75rem] border border-white/75 bg-white/88 p-5 shadow-[0_24px_48px_-40px_rgba(15,23,42,0.4)]"
          >
            <div className="mt-0.5 rounded-full bg-emerald-50 p-2 text-emerald-600">
              <CheckCircle2 className="h-4 w-4" />
            </div>
            <p className="text-sm leading-7 text-foreground">{feature}</p>
          </li>
        ))}
      </ul>
    </div>
  );
};

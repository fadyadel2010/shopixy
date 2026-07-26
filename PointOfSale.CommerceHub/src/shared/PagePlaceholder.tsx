import { ProductGridSkeleton } from "@/commerce/components/ProductGridSkeleton";
import { Button } from "@/ui/button";

type PagePlaceholderProps = {
  eyebrow: string;
  title: string;
  description: string;
};

export const PagePlaceholder = ({ eyebrow, title, description }: PagePlaceholderProps) => {
  return (
    <section className="space-y-8">
      <div className="surface-panel overflow-hidden px-6 py-8 md:px-10 md:py-10">
        <p className="text-sm font-semibold uppercase tracking-[0.24em] text-primary">{eyebrow}</p>
        <h1 className="mt-4 max-w-3xl font-display text-4xl text-balance text-foreground md:text-5xl">{title}</h1>
        <p className="mt-4 max-w-2xl text-base leading-7 text-muted-foreground">{description}</p>
        <div className="mt-6 flex flex-wrap gap-3">
          <Button>Foundation Ready</Button>
          <Button variant="outline">API Wiring Next</Button>
        </div>
      </div>

      <ProductGridSkeleton count={4} />
    </section>
  );
};

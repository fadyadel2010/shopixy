import { ChevronRight, Layers3 } from "lucide-react";

import type { CommerceHubCategory } from "@/types";
import { Badge } from "@/ui/badge";
import { Card, CardContent } from "@/ui/card";

type CategoryCardProps = {
  category: CommerceHubCategory;
};

export const CategoryCard = ({ category }: CategoryCardProps) => {
  return (
    <Card className="group h-full overflow-hidden border-white/70 bg-white/88 transition-all duration-300 hover:-translate-y-1.5 hover:shadow-soft">
      <CardContent className="flex h-full flex-col gap-5 p-5">
        <div className="flex items-start justify-between gap-3">
          <div className="brand-gradient-soft flex h-14 w-14 items-center justify-center rounded-[1.35rem] text-primary">
            {category.image ? (
              <img src={category.image} alt={category.name} className="h-full w-full rounded-[1.35rem] object-cover" />
            ) : (
              <Layers3 className="h-5 w-5" />
            )}
          </div>
          <div className="flex items-center gap-1 rounded-full bg-secondary px-3 py-1.5 text-xs font-semibold uppercase tracking-[0.2em] text-secondary-foreground transition-colors group-hover:bg-primary/10 group-hover:text-primary">
            Open
            <ChevronRight className="h-3.5 w-3.5" />
          </div>
        </div>

        <div className="space-y-3">
          <div className="space-y-2">
            <p className="text-lg font-bold tracking-tight text-foreground">{category.name}</p>
            <p className="text-sm leading-6 text-muted-foreground">
              {category.productCount
                ? `${category.productCount} products ready to browse.`
                : "Merchandising-ready category block for Shopixy discovery."}
            </p>
          </div>

          <div className="flex flex-wrap gap-2">
            {typeof category.level === "number" ? <Badge variant="muted">Level {category.level + 1}</Badge> : null}
            {category.hasAttributes ? <Badge variant="accent">Filter rich</Badge> : null}
          </div>
        </div>
      </CardContent>
    </Card>
  );
};

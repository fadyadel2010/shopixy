import { ChevronDown } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";

import { cn } from "@/utils";

type FilterSectionProps = {
  title: string;
  description?: string;
  children: ReactNode;
  collapsible?: boolean;
  defaultOpen?: boolean;
  forceOpen?: boolean;
};

export const FilterSection = ({
  title,
  description,
  children,
  collapsible = true,
  defaultOpen = false,
  forceOpen = false
}: FilterSectionProps) => {
  const [isOpen, setIsOpen] = useState(defaultOpen || forceOpen);

  useEffect(() => {
    if (forceOpen) {
      setIsOpen(true);
    }
  }, [forceOpen]);

  return (
    <section className="border-b border-border/60 pb-5 last:border-b-0 last:pb-0">
      {collapsible ? (
        <button
          type="button"
          onClick={() => setIsOpen((current) => !current)}
          className="flex w-full items-center justify-between gap-3 text-left"
          aria-expanded={isOpen}
        >
          <div className="space-y-1">
            <h3 className="text-sm font-semibold text-foreground">{title}</h3>
            {description ? <p className="text-xs text-muted-foreground">{description}</p> : null}
          </div>
          <ChevronDown className={cn("h-4 w-4 shrink-0 text-muted-foreground transition-transform", isOpen && "rotate-180")} />
        </button>
      ) : (
        <div className="space-y-1">
          <h3 className="text-sm font-semibold text-foreground">{title}</h3>
          {description ? <p className="text-xs text-muted-foreground">{description}</p> : null}
        </div>
      )}

      {!collapsible || isOpen ? <div className="mt-3">{children}</div> : null}
    </section>
  );
};

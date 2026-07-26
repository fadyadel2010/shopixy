import type { ReactNode } from "react";

import { Button } from "@/ui/button";

type AutocompleteSectionProps = {
  title: string;
  icon: ReactNode;
  actionLabel?: string;
  onAction?: () => void;
  children: ReactNode;
};

export const AutocompleteSection = ({
  title,
  icon,
  actionLabel,
  onAction,
  children
}: AutocompleteSectionProps) => {
  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between gap-3 px-1">
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
          <span className="text-primary">{icon}</span>
          <span>{title}</span>
        </div>

        {actionLabel && onAction ? (
          <Button type="button" variant="ghost" size="sm" className="h-auto px-2 py-1 text-xs" onClick={onAction}>
            {actionLabel}
          </Button>
        ) : null}
      </div>

      <div className="space-y-2">{children}</div>
    </section>
  );
};

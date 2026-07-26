import type { ReactNode } from "react";
import { Inbox } from "lucide-react";

import { Button } from "@/ui/button";

type EmptyStateProps = {
  title: string;
  description: string;
  icon?: ReactNode;
  actionLabel?: string;
  onAction?: () => void;
  children?: ReactNode;
};

export const EmptyState = ({ title, description, icon, actionLabel, onAction, children }: EmptyStateProps) => {
  return (
    <div className="relative overflow-hidden rounded-[2rem] border border-white/70 bg-white/78 px-6 py-10 text-center shadow-card backdrop-blur-xl">
      <div className="absolute inset-x-10 top-0 h-32 rounded-full bg-primary/8 blur-3xl" aria-hidden="true" />
      <div className="brand-gradient-soft relative mx-auto flex h-16 w-16 items-center justify-center rounded-[1.5rem] text-primary shadow-soft">
        {icon ?? <Inbox className="h-6 w-6" />}
      </div>
      <p className="relative mt-5 font-display text-2xl font-bold text-foreground">{title}</p>
      <p className="relative mx-auto mt-3 max-w-xl text-sm leading-7 text-muted-foreground">{description}</p>
      {actionLabel && onAction ? (
        <div className="relative mt-6">
          <Button type="button" variant="secondary" onClick={onAction}>
            {actionLabel}
          </Button>
        </div>
      ) : null}
      {children ? <div className="relative mt-8">{children}</div> : null}
    </div>
  );
};

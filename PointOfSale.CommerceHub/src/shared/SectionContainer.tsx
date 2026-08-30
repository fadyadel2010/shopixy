import type { ReactNode } from "react";
import { Link } from "react-router-dom";

import { SectionHeading } from "@/shared/SectionHeading";
import { resolveSafeNavigationTarget } from "@/security/safeNavigationTarget";
import { Button, type ButtonProps } from "@/ui/button";
import { cn } from "@/utils/cn";

type SectionAction = {
  label: string;
  href: string;
  variant?: ButtonProps["variant"];
};

type SectionContainerProps = {
  eyebrow?: string;
  title?: string;
  description?: string;
  action?: SectionAction;
  children: ReactNode;
  className?: string;
  contentClassName?: string;
  headerClassName?: string;
};

export const SectionContainer = ({
  eyebrow,
  title,
  description,
  action,
  children,
  className,
  contentClassName,
  headerClassName
}: SectionContainerProps) => {
  return (
    <section className={cn("space-y-6", className)}>
      {eyebrow || title || description || action ? (
        <div className={cn("flex flex-col gap-4 md:flex-row md:items-end md:justify-between", headerClassName)}>
          <SectionHeading eyebrow={eyebrow} title={title ?? ""} description={description} />
          {action ? (
            <Button asChild variant={action.variant ?? "ghost"}>
              <Link to={resolveSafeNavigationTarget(action.href)}>{action.label}</Link>
            </Button>
          ) : null}
        </div>
      ) : null}
      <div className={contentClassName}>{children}</div>
    </section>
  );
};

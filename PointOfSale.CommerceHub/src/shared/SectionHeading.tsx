type SectionHeadingProps = {
  eyebrow?: string;
  title: string;
  description?: string;
};

export const SectionHeading = ({ eyebrow, title, description }: SectionHeadingProps) => {
  return (
    <div className="space-y-3">
      {eyebrow ? <p className="text-xs font-semibold uppercase tracking-[0.28em] text-primary">{eyebrow}</p> : null}
      <div className="space-y-2">
        <h2 className="font-display text-3xl font-bold tracking-tight text-foreground md:text-4xl">{title}</h2>
        {description ? <p className="max-w-2xl text-sm leading-7 text-muted-foreground md:text-base">{description}</p> : null}
      </div>
    </div>
  );
};

import { memo } from "react";

type AttributeGroup = {
  name: string;
  values: string[];
};

type VariantOptionState = {
  disabled: boolean;
  selected: boolean;
  inStock: boolean;
};

type VariantSelectorProps = {
  groups: AttributeGroup[];
  selectedOptions: Record<string, string>;
  onSelect: (attributeName: string, value: string) => void;
  getOptionState: (attributeName: string, value: string) => VariantOptionState;
};

export const VariantSelector = memo(({
  groups,
  selectedOptions,
  onSelect,
  getOptionState
}: VariantSelectorProps) => {
  if (groups.length === 0) {
    return null;
  }

  return (
    <div className="space-y-6">
      {groups.map((group) => (
        <div key={group.name} className="rounded-[1.5rem] border border-white/70 bg-white/75 p-4 shadow-soft">
          <div className="flex items-center justify-between gap-3">
            <p className="text-sm font-semibold uppercase tracking-[0.18em] text-foreground">{group.name}</p>
            <p className="rounded-full bg-secondary px-3 py-1 text-xs font-semibold text-secondary-foreground">
              {selectedOptions[group.name] ?? "Choose an option"}
            </p>
          </div>

          <div className="mt-4 flex flex-wrap gap-2">
            {group.values.map((value) => {
              const state = getOptionState(group.name, value);

              return (
                <button
                  key={`${group.name}-${value}`}
                  type="button"
                  onClick={() => onSelect(group.name, value)}
                  disabled={state.disabled}
                  className={[
                    "rounded-[1.15rem] border px-4 py-3 text-sm font-semibold transition-all",
                    state.selected
                      ? "brand-gradient border-transparent text-white shadow-glow"
                      : state.disabled
                        ? "cursor-not-allowed border-border/60 bg-slate-100 text-slate-400 line-through"
                        : "border-border bg-white text-foreground hover:-translate-y-0.5 hover:border-primary/40 hover:bg-primary/5"
                  ].join(" ")}
                  aria-pressed={state.selected}
                >
                  {value}
                </button>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
});

VariantSelector.displayName = "VariantSelector";

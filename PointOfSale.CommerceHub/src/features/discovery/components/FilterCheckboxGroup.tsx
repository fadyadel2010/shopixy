import { Check } from "lucide-react";

import { cn } from "@/utils";

export type FilterCheckboxOption = {
  value: string;
  label: string;
  count?: number;
  disabled?: boolean;
};

type FilterCheckboxGroupProps = {
  options: FilterCheckboxOption[];
  selectedValues: string[];
  onChange: (values: string[]) => void;
  emptyLabel?: string;
};

export const FilterCheckboxGroup = ({
  options,
  selectedValues,
  onChange,
  emptyLabel = "No options available"
}: FilterCheckboxGroupProps) => {
  const sortedOptions = [...options]
    .filter((option) => (option.count ?? 1) > 0 || selectedValues.includes(option.value))
    .sort((left, right) => {
      const countDifference = (right.count ?? 0) - (left.count ?? 0);
      if (countDifference !== 0) {
        return countDifference;
      }

      return left.label.localeCompare(right.label);
    });

  if (sortedOptions.length === 0) {
    return <p className="text-xs text-muted-foreground">{emptyLabel}</p>;
  }

  return (
    <div className="max-h-72 space-y-2 overflow-y-auto pr-1">
      {sortedOptions.map((option) => {
        const isSelected = selectedValues.includes(option.value);
        const isDisabled = option.disabled || ((option.count ?? 0) <= 0 && !isSelected);

        return (
          <label
            key={option.value}
            className={cn(
              "flex items-center gap-3 rounded-2xl border px-3 py-2.5 transition-colors",
              isDisabled && !isSelected ? "cursor-not-allowed opacity-50" : "cursor-pointer",
              isSelected
                ? "border-primary/30 bg-primary/8"
                : "border-border/70 bg-white hover:border-primary/20 hover:bg-primary/5"
            )}
          >
            <input
              type="checkbox"
              checked={isSelected}
              disabled={isDisabled}
              onChange={(event) =>
                onChange(
                  event.target.checked
                    ? [...selectedValues, option.value]
                    : selectedValues.filter((value) => value !== option.value)
                )
              }
              className="sr-only"
            />
            <span
              className={cn(
                "flex h-4 w-4 shrink-0 items-center justify-center rounded border",
                isSelected ? "border-primary bg-primary text-primary-foreground" : "border-border bg-background"
              )}
              aria-hidden="true"
            >
              {isSelected ? <Check className="h-3 w-3" /> : null}
            </span>
            <span className="min-w-0 flex-1 text-sm text-foreground">{option.label}</span>
            {option.count !== undefined ? (
              <span className="shrink-0 text-xs text-muted-foreground">{option.count}</span>
            ) : null}
          </label>
        );
      })}
    </div>
  );
};

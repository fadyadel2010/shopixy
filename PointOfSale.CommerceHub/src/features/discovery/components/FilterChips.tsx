import { X } from "lucide-react";

type FilterChip = {
  id: string;
  label: string;
  onRemove: () => void;
};

type FilterChipsProps = {
  chips: FilterChip[];
  onClearAll?: () => void;
};

export const FilterChips = ({ chips, onClearAll }: FilterChipsProps) => {
  if (chips.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      {chips.map((chip) => (
        <button
          key={chip.id}
          type="button"
          onClick={chip.onRemove}
          className="inline-flex items-center gap-2 rounded-full border border-border/80 bg-white px-3 py-2 text-sm font-medium text-foreground transition-colors hover:bg-muted"
        >
          <span>{chip.label}</span>
          <X className="h-3.5 w-3.5 text-muted-foreground" />
        </button>
      ))}

      {onClearAll ? (
        <button
          type="button"
          onClick={onClearAll}
          className="text-sm font-semibold text-muted-foreground transition-colors hover:text-foreground"
        >
          Clear all
        </button>
      ) : null}
    </div>
  );
};

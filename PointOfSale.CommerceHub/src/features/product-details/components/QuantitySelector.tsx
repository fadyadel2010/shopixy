import { Minus, Plus } from "lucide-react";

type QuantitySelectorProps = {
  value: number;
  min?: number;
  max: number;
  disabled?: boolean;
  onChange: (value: number) => void;
};

export const QuantitySelector = ({
  value,
  min = 1,
  max,
  disabled = false,
  onChange
}: QuantitySelectorProps) => {
  const canDecrease = !disabled && value > min;
  const canIncrease = !disabled && value < max;

  return (
    <div className="inline-flex items-center rounded-full border border-white/80 bg-white/88 p-1 shadow-soft backdrop-blur">
      <button
        type="button"
        onClick={() => onChange(Math.max(min, value - 1))}
        disabled={!canDecrease}
        className="flex h-10 w-10 items-center justify-center rounded-full text-foreground transition-colors hover:bg-secondary disabled:cursor-not-allowed disabled:text-slate-300"
        aria-label="Decrease quantity"
      >
        <Minus className="h-4 w-4" />
      </button>

      <div className="min-w-10 rounded-full bg-secondary px-3 py-2 text-center text-sm font-semibold text-foreground">
        {value}
      </div>

      <button
        type="button"
        onClick={() => onChange(Math.min(max, value + 1))}
        disabled={!canIncrease}
        className="flex h-10 w-10 items-center justify-center rounded-full text-foreground transition-colors hover:bg-secondary disabled:cursor-not-allowed disabled:text-slate-300"
        aria-label="Increase quantity"
      >
        <Plus className="h-4 w-4" />
      </button>
    </div>
  );
};

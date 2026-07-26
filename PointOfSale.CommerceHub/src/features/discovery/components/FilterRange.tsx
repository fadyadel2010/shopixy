import { useEffect, useState } from "react";

import { Input } from "@/ui/input";
import { formatRangeLabel } from "@/features/discovery/filter-formatting";

type FilterRangeProps = {
  min?: number;
  max?: number;
  availableMin?: number | null;
  availableMax?: number | null;
  onChange: (range: { min?: number; max?: number }) => void;
};

const parseNumber = (value: string) => {
  if (!value.trim()) {
    return undefined;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
};

const clampValue = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

const resolveStep = (availableMin?: number | null, availableMax?: number | null) => {
  if (availableMin === undefined || availableMin === null || availableMax === undefined || availableMax === null) {
    return 1;
  }

  const span = Math.abs(availableMax - availableMin);
  if (span <= 20) {
    return 1;
  }

  if (span <= 100) {
    return 5;
  }

  if (span <= 1000) {
    return 10;
  }

  return Math.max(1, Math.round(span / 100));
};

export const FilterRange = ({
  min,
  max,
  availableMin,
  availableMax,
  onChange
}: FilterRangeProps) => {
  const [minInput, setMinInput] = useState(min?.toString() ?? "");
  const [maxInput, setMaxInput] = useState(max?.toString() ?? "");
  const hasSlider =
    availableMin !== undefined &&
    availableMin !== null &&
    availableMax !== undefined &&
    availableMax !== null &&
    availableMax > availableMin;
  const sliderMin = hasSlider ? availableMin : 0;
  const sliderMax = hasSlider ? availableMax : 0;
  const sliderStep = resolveStep(availableMin, availableMax);
  const selectedMin = hasSlider
    ? clampValue(min ?? sliderMin, sliderMin, max ?? sliderMax)
    : undefined;
  const selectedMax = hasSlider
    ? clampValue(max ?? sliderMax, min ?? sliderMin, sliderMax)
    : undefined;

  useEffect(() => {
    setMinInput(min?.toString() ?? "");
  }, [min]);

  useEffect(() => {
    setMaxInput(max?.toString() ?? "");
  }, [max]);

  return (
    <div className="space-y-3">
      {hasSlider ? (
        <div className="rounded-2xl border border-border/70 bg-white px-4 py-3">
          <div className="space-y-3">
            <div className="space-y-1.5">
              <div className="flex items-center justify-between text-xs font-medium text-muted-foreground">
                <span>Minimum</span>
                <span>{formatRangeLabel(selectedMin, undefined)}</span>
              </div>
              <input
                type="range"
                min={sliderMin}
                max={sliderMax}
                step={sliderStep}
                value={selectedMin ?? sliderMin}
                onChange={(event) => {
                  const nextMin = Math.min(Number(event.target.value), selectedMax ?? sliderMax);
                  setMinInput(nextMin.toString());
                  onChange({
                    min: nextMin,
                    max: selectedMax
                  });
                }}
                className="h-2 w-full cursor-pointer accent-primary"
              />
            </div>

            <div className="space-y-1.5">
              <div className="flex items-center justify-between text-xs font-medium text-muted-foreground">
                <span>Maximum</span>
                <span>{formatRangeLabel(undefined, selectedMax)}</span>
              </div>
              <input
                type="range"
                min={sliderMin}
                max={sliderMax}
                step={sliderStep}
                value={selectedMax ?? sliderMax}
                onChange={(event) => {
                  const nextMax = Math.max(Number(event.target.value), selectedMin ?? sliderMin);
                  setMaxInput(nextMax.toString());
                  onChange({
                    min: selectedMin,
                    max: nextMax
                  });
                }}
                className="h-2 w-full cursor-pointer accent-primary"
              />
            </div>
          </div>
        </div>
      ) : null}

      <div className="grid grid-cols-2 gap-3">
        <Input
          inputMode="decimal"
          placeholder={availableMin !== undefined && availableMin !== null ? `${availableMin}` : "Min"}
          value={minInput}
          onChange={(event) => {
            const nextValue = event.target.value;
            setMinInput(nextValue);
            const parsedMin = parseNumber(nextValue);
            onChange({
              min: hasSlider && parsedMin !== undefined ? clampValue(parsedMin, sliderMin, parseNumber(maxInput) ?? sliderMax) : parsedMin,
              max: parseNumber(maxInput)
            });
          }}
        />
        <Input
          inputMode="decimal"
          placeholder={availableMax !== undefined && availableMax !== null ? `${availableMax}` : "Max"}
          value={maxInput}
          onChange={(event) => {
            const nextValue = event.target.value;
            setMaxInput(nextValue);
            const parsedMax = parseNumber(nextValue);
            onChange({
              min: parseNumber(minInput),
              max: hasSlider && parsedMax !== undefined ? clampValue(parsedMax, parseNumber(minInput) ?? sliderMin, sliderMax) : parsedMax
            });
          }}
        />
      </div>
      {availableMin !== undefined || availableMax !== undefined ? (
        <p className="text-xs text-muted-foreground">
          Available range: {formatRangeLabel(availableMin ?? undefined, availableMax ?? undefined)}
        </p>
      ) : null}
    </div>
  );
};

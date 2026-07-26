import { ChevronDown } from "lucide-react";
import { useEffect, useId, useState, type KeyboardEvent, type ReactNode } from "react";

export type ProductTabItem = {
  id: string;
  label: string;
  lazy?: boolean;
  content: () => ReactNode;
};

type ProductTabsProps = {
  items: ProductTabItem[];
  defaultTabId?: string;
};

export const ProductTabs = ({ items, defaultTabId }: ProductTabsProps) => {
  const fallbackTabId = defaultTabId && items.some((item) => item.id === defaultTabId) ? defaultTabId : items[0]?.id ?? "";
  const [activeTabId, setActiveTabId] = useState(fallbackTabId);
  const [visitedTabIds, setVisitedTabIds] = useState<string[]>(fallbackTabId ? [fallbackTabId] : []);
  const groupId = useId();

  useEffect(() => {
    if (!items.some((item) => item.id === activeTabId)) {
      setActiveTabId(fallbackTabId);
    }
  }, [activeTabId, fallbackTabId, items]);

  useEffect(() => {
    if (!activeTabId) {
      return;
    }

    setVisitedTabIds((currentIds) => (currentIds.includes(activeTabId) ? currentIds : [...currentIds, activeTabId]));
  }, [activeTabId]);

  if (items.length === 0) {
    return null;
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, currentIndex: number) => {
    const totalTabs = items.length;

    if (totalTabs <= 1) {
      return;
    }

    const moveTo = (index: number) => {
      const nextIndex = (index + totalTabs) % totalTabs;
      const nextTab = items[nextIndex];

      if (!nextTab) {
        return;
      }

      setActiveTabId(nextTab.id);
      document.getElementById(`${groupId}-tab-${nextTab.id}`)?.focus();
    };

    switch (event.key) {
      case "ArrowRight":
      case "ArrowDown":
        event.preventDefault();
        moveTo(currentIndex + 1);
        break;
      case "ArrowLeft":
      case "ArrowUp":
        event.preventDefault();
        moveTo(currentIndex - 1);
        break;
      case "Home":
        event.preventDefault();
        moveTo(0);
        break;
      case "End":
        event.preventDefault();
        moveTo(totalTabs - 1);
        break;
      default:
        break;
    }
  };

  const shouldRender = (item: ProductTabItem) => !item.lazy || item.id === activeTabId || visitedTabIds.includes(item.id);

  return (
    <div className="space-y-4">
      <div className="hidden md:block">
        <div
          role="tablist"
          aria-label="Product details sections"
          className="inline-flex flex-wrap gap-2 rounded-full border border-white/70 bg-white/85 p-2 shadow-[0_24px_48px_-36px_rgba(15,23,42,0.4)]"
        >
          {items.map((item, index) => {
            const isActive = item.id === activeTabId;

            return (
              <button
                key={item.id}
                id={`${groupId}-tab-${item.id}`}
                type="button"
                role="tab"
                aria-selected={isActive}
                aria-controls={`${groupId}-panel-${item.id}`}
                tabIndex={isActive ? 0 : -1}
                className={[
                  "rounded-full px-5 py-3 text-sm font-semibold transition",
                  isActive
                    ? "bg-primary text-primary-foreground shadow-soft"
                    : "text-muted-foreground hover:bg-secondary/80 hover:text-foreground"
                ].join(" ")}
                onClick={() => setActiveTabId(item.id)}
                onKeyDown={(event) => handleKeyDown(event, index)}
              >
                {item.label}
              </button>
            );
          })}
        </div>

        <div className="mt-6">
          {items.map((item) => {
            const isActive = item.id === activeTabId;

            if (!shouldRender(item)) {
              return null;
            }

            return (
              <div
                key={item.id}
                id={`${groupId}-panel-${item.id}`}
                role="tabpanel"
                aria-labelledby={`${groupId}-tab-${item.id}`}
                hidden={!isActive}
                className={isActive ? "animate-[fadeIn_220ms_ease-out]" : undefined}
              >
                {item.content()}
              </div>
            );
          })}
        </div>
      </div>

      <div className="space-y-3 md:hidden">
        {items.map((item) => {
          const isActive = item.id === activeTabId;

          return (
            <section
              key={item.id}
              className={[
                "overflow-hidden rounded-[1.75rem] border bg-white/92 shadow-[0_24px_48px_-38px_rgba(15,23,42,0.42)]",
                isActive ? "border-primary/25" : "border-white/70"
              ].join(" ")}
            >
              <button
                id={`${groupId}-mobile-tab-${item.id}`}
                type="button"
                className="flex w-full items-center justify-between gap-3 px-5 py-4 text-left"
                aria-expanded={isActive}
                aria-controls={`${groupId}-mobile-panel-${item.id}`}
                onClick={() => setActiveTabId(item.id)}
              >
                <span className="text-base font-semibold text-foreground">{item.label}</span>
                <ChevronDown
                  className={[
                    "h-4 w-4 shrink-0 text-muted-foreground transition-transform",
                    isActive ? "rotate-180" : ""
                  ].join(" ")}
                />
              </button>

              {isActive && shouldRender(item) ? (
                <div
                  id={`${groupId}-mobile-panel-${item.id}`}
                  role="region"
                  aria-labelledby={`${groupId}-mobile-tab-${item.id}`}
                  className="border-t border-border/60 px-4 py-4"
                >
                  {item.content()}
                </div>
              ) : null}
            </section>
          );
        })}
      </div>
    </div>
  );
};

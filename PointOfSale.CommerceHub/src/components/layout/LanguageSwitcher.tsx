import { Globe, Check } from "lucide-react";
import { useRef, useState, useEffect } from "react";

import { selectLanguage, selectSetLanguage, useLanguageStore } from "@/store/language-store";
import type { SupportedLanguage } from "@/store/language-store";

const languages: { code: SupportedLanguage; label: string; nativeLabel: string; flag: string }[] = [
  { code: "en", label: "English", nativeLabel: "English", flag: "🇺🇸" },
  { code: "ar", label: "Arabic", nativeLabel: "العربية", flag: "🇸🇦" }
];

export const LanguageSwitcher = () => {
  const language = useLanguageStore(selectLanguage);
  const setLanguage = useLanguageStore(selectSetLanguage);
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  // Close on outside click
  useEffect(() => {
    const handler = (event: MouseEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };

    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  // Close on Escape
  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };

    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, []);

  const current = languages.find((l) => l.code === language) ?? languages[0];

  const handleSelect = (code: SupportedLanguage) => {
    setLanguage(code);
    setOpen(false);
  };

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        aria-label={`Current language: ${current.label}. Click to change.`}
        aria-expanded={open}
        aria-haspopup="listbox"
        className="inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-white/80 px-3 py-2 text-xs font-semibold text-muted-foreground transition-all hover:border-primary/40 hover:bg-white hover:text-foreground hover:shadow-sm"
      >
        <Globe className="h-3.5 w-3.5" />
        <span className="hidden sm:inline">{current.flag}</span>
        <span className="hidden sm:inline">{current.nativeLabel}</span>
        <svg
          className={`h-3 w-3 transition-transform duration-200 ${open ? "rotate-180" : ""}`}
          fill="none"
          stroke="currentColor"
          strokeWidth={2.5}
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 8.25 12 15.75l-7.5-7.5" />
        </svg>
      </button>

      {open ? (
        <div
          role="listbox"
          aria-label="Select language"
          className="absolute right-0 top-full z-50 mt-2 min-w-[10rem] animate-fade-up overflow-hidden rounded-xl border border-white/70 bg-white/95 shadow-card backdrop-blur-xl"
        >
          {languages.map((lang) => {
            const isActive = lang.code === language;

            return (
              <button
                key={lang.code}
                role="option"
                aria-selected={isActive}
                type="button"
                onClick={() => handleSelect(lang.code)}
                className={[
                  "flex w-full items-center gap-2.5 px-3.5 py-2.5 text-sm transition-all",
                  isActive
                    ? "bg-secondary font-semibold text-primary"
                    : "text-muted-foreground hover:bg-muted/60 hover:text-foreground"
                ].join(" ")}
              >
                <span className="text-base">{lang.flag}</span>
                <span className="flex-1 text-left">{lang.nativeLabel}</span>
                {isActive ? <Check className="h-3.5 w-3.5 text-primary" /> : null}
              </button>
            );
          })}
        </div>
      ) : null}
    </div>
  );
};

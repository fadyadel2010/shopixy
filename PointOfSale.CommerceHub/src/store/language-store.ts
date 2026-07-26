import { create } from "zustand";

export type SupportedLanguage = "en" | "ar";

type LanguageState = {
  language: SupportedLanguage;
  setLanguage: (language: SupportedLanguage) => void;
};

const STORAGE_KEY = "commercehub_language";

const getInitialLanguage = (): SupportedLanguage => {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "ar" || stored === "en") return stored;
  } catch {
    // SSR or localStorage unavailable
  }

  // Detect from browser preference
  const browserLang = navigator.language?.toLowerCase() ?? "";
  return browserLang.startsWith("ar") ? "ar" : "en";
};

export const useLanguageStore = create<LanguageState>((set) => ({
  language: getInitialLanguage(),
  setLanguage: (language) => {
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Ignore storage errors
    }
    set({ language });
  }
}));

export const selectLanguage = (state: LanguageState) => state.language;
export const selectSetLanguage = (state: LanguageState) => state.setLanguage;
export const getLanguage = () => useLanguageStore.getState().language;

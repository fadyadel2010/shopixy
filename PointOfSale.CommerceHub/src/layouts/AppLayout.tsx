import { useQueryClient } from "@tanstack/react-query";
import { Outlet } from "react-router-dom";
import { useEffect, useRef } from "react";

import { Footer } from "@/components/layout/Footer";
import { Header } from "@/components/layout/Header";
import { selectLanguage, useLanguageStore } from "@/store/language-store";

export const AppLayout = () => {
  const language = useLanguageStore(selectLanguage);
  const queryClient = useQueryClient();
  const isInitialRender = useRef(true);

  // Sync document direction and lang attribute with language store
  useEffect(() => {
    const dir = language === "ar" ? "rtl" : "ltr";
    document.documentElement.setAttribute("dir", dir);
    document.documentElement.setAttribute("lang", language);

    // Invalidate all cached queries when language changes (skip first render)
    if (isInitialRender.current) {
      isInitialRender.current = false;
    } else {
      queryClient.invalidateQueries();
    }
  }, [language, queryClient]);

  return (
    <div className="flex min-h-screen flex-col">
      <Header />
      <main className="container flex-1 py-8 md:py-10">
        <Outlet />
      </main>
      <Footer />
    </div>
  );
};

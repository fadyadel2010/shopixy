import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";

import { marketplaceContentService } from "@/services/marketplace-content-service";
import { brand } from "@/shared/brand";

const socialIcon = (platform: string) => {
  const normalized = platform.trim().toLowerCase();

  switch (normalized) {
    case "facebook":
      return (
        <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M24 12.073C24 5.404 18.627 0 12 0S0 5.404 0 12.073c0 6.019 4.388 11.01 10.125 11.927v-8.438H7.078v-3.49h3.047V9.41c0-3.017 1.792-4.685 4.533-4.685 1.313 0 2.686.236 2.686.236v2.964h-1.514c-1.49 0-1.956.93-1.956 1.885v2.262h3.328l-.532 3.49h-2.796V24C19.612 23.083 24 18.092 24 12.073Z" />
        </svg>
      );
    case "instagram":
      return (
        <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M12 2.163c3.204 0 3.584.012 4.85.07 3.251.148 4.77 1.69 4.918 4.918.058 1.265.069 1.645.069 4.849 0 3.204-.012 3.584-.069 4.849-.149 3.227-1.664 4.771-4.919 4.919-1.266.058-1.645.07-4.849.07-3.204 0-3.583-.012-4.849-.07-3.259-.149-4.771-1.699-4.919-4.92C2.175 15.583 2.163 15.204 2.163 12c0-3.204.013-3.583.07-4.849.149-3.227 1.664-4.77 4.919-4.919C8.417 2.175 8.796 2.163 12 2.163Zm0-2.163C8.741 0 8.333.014 7.052.072 2.695.272.273 2.69.073 7.052.014 8.333 0 8.741 0 12c0 3.259.014 3.668.072 4.948.2 4.359 2.618 6.78 6.98 6.98C8.333 23.986 8.741 24 12 24c3.259 0 3.668-.014 4.948-.072 4.354-.2 6.782-2.618 6.98-6.98C23.986 15.668 24 15.259 24 12c0-3.259-.014-3.667-.072-4.948-.196-4.354-2.617-6.78-6.979-6.98C15.668.014 15.259 0 12 0Zm0 5.838A6.162 6.162 0 1 0 12 18.162a6.162 6.162 0 0 0 0-12.324Zm0 10.162A4 4 0 1 1 12 8a4 4 0 0 1 0 8Zm6.406-11.845a1.44 1.44 0 1 0 0 2.881 1.44 1.44 0 0 0 0-2.881Z" />
        </svg>
      );
    case "twitter":
    case "x":
      return (
        <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M18.901 1.154h3.68l-8.043 9.193L24 22.847h-7.406l-5.8-7.584-6.638 7.584H.474l8.603-9.832L0 1.154h7.594l5.243 6.932 6.064-6.932Zm-1.291 19.49h2.039L6.486 3.244H4.298L17.61 20.644Z" />
        </svg>
      );
    case "tiktok":
      return (
        <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M19.589 6.686a4.793 4.793 0 0 1-3.77-4.718h-3.17v12.702a2.896 2.896 0 1 1-2.896-2.896c.357 0 .699.066 1.015.186V8.734a6.07 6.07 0 0 0-1.015-.086A6.066 6.066 0 1 0 15.82 14.71V8.244a7.958 7.958 0 0 0 4.678 1.515V6.686h-.909Z" />
        </svg>
      );
    default:
      return <span className="text-[0.65rem] font-semibold uppercase">{normalized.slice(0, 1) || "?"}</span>;
  }
};

export const Footer = () => {
  const footerQuery = useQuery({
    queryKey: ["marketplace-footer"],
    queryFn: ({ signal }) => marketplaceContentService.getFooter(signal)
  });

  const sections = footerQuery.data?.sections ?? [];
  const socialLinks = footerQuery.data?.socialLinks ?? [];

  return (
    <footer className="border-t border-white/60 bg-white/55 backdrop-blur-xl">
      <div className="container py-8 md:py-10">
        <div className="surface-muted grid gap-6 px-6 py-6 md:grid-cols-[minmax(0,1.2fr)_minmax(0,2fr)]">
          <div>
            <p className="font-display text-xl font-bold tracking-tight text-foreground">{brand.fullName}</p>
            <p className="mt-2 max-w-xl text-sm text-muted-foreground">
              {brand.tagline} Built for reliable marketplace growth, clean operations, and merchant trust.
            </p>

            {socialLinks.length > 0 ? (
              <div className="mt-5 flex flex-wrap gap-3">
                {socialLinks.map((link) => (
                  <a
                    key={`${link.platform}-${link.url}`}
                    href={link.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-2 rounded-full border border-border/80 bg-white/80 px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground transition hover:border-primary/40 hover:text-foreground"
                  >
                    {socialIcon(link.icon || link.platform)}
                    {link.platform}
                  </a>
                ))}
              </div>
            ) : null}
          </div>

          <div className="grid gap-6 sm:grid-cols-2 xl:grid-cols-3">
            {sections.map((section) => (
              <div key={`${section.title}-${section.sortOrder}`}>
                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">{section.title}</p>
                <div className="mt-4 space-y-3">
                  {section.links.map((link) =>
                    link.type === "Internal" || link.type === "Cms" ? (
                      <Link
                        key={`${link.url}-${link.label}`}
                        className="block text-sm text-muted-foreground transition-colors hover:text-foreground"
                        to={link.url}
                      >
                        {link.label}
                      </Link>
                    ) : (
                      <a
                        key={`${link.url}-${link.label}`}
                        className="block text-sm text-muted-foreground transition-colors hover:text-foreground"
                        href={link.url}
                        target={link.openInNewTab ? "_blank" : undefined}
                        rel={link.openInNewTab ? "noopener noreferrer" : undefined}
                      >
                        {link.label}
                      </a>
                    )
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </footer>
  );
};

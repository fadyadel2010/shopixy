import { useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { useParams } from "react-router-dom";

import { marketplaceContentService } from "@/services/marketplace-content-service";
import { EmptyState } from "@/shared/EmptyState";
import { LoadingSkeleton } from "@/shared/LoadingSkeleton";
import type { MarketplaceCmsPageBlock } from "@/types";
import { useDocumentTitle } from "@/utils";

const sanitizeHtml = (html: string) => {
  if (!html) {
    return "";
  }

  const parser = new DOMParser();
  const documentFragment = parser.parseFromString(html, "text/html");

  documentFragment.querySelectorAll("script,style,iframe,object,embed,form,input,button,textarea,select,meta,link").forEach((node) => {
    node.remove();
  });

  documentFragment.querySelectorAll("*").forEach((element) => {
    Array.from(element.attributes).forEach((attribute) => {
      const name = attribute.name.toLowerCase();
      const value = attribute.value.trim().toLowerCase();

      if (name.startsWith("on") || name === "style") {
        element.removeAttribute(attribute.name);
        return;
      }

      if ((name === "href" || name === "src") && value.startsWith("javascript:")) {
        element.removeAttribute(attribute.name);
      }
    });
  });

  return documentFragment.body.innerHTML;
};

const renderBlock = (block: MarketplaceCmsPageBlock, index: number) => {
  switch (block.type) {
    case "text":
      return (
        <section key={`${block.type}-${index}`} className="rounded-[2rem] border border-border/80 bg-white px-6 py-6 shadow-soft">
          <p className="whitespace-pre-line text-base leading-8 text-muted-foreground">{String(block.data.text ?? "")}</p>
        </section>
      );
    case "faq": {
      const items = Array.isArray(block.data.items) ? (block.data.items as Array<{ question: string; answer: string }>) : [];
      return (
        <section key={`${block.type}-${index}`} className="rounded-[2rem] border border-border/80 bg-white px-6 py-6 shadow-soft">
          <div className="space-y-4">
            {items.map((item, itemIndex) => (
              <details key={itemIndex} className="rounded-2xl border border-border/80 bg-background/60 px-4 py-3">
                <summary className="cursor-pointer text-sm font-semibold text-foreground">{item.question}</summary>
                <p className="mt-3 whitespace-pre-line text-sm leading-7 text-muted-foreground">{item.answer}</p>
              </details>
            ))}
          </div>
        </section>
      );
    }
    default:
      return null;
  }
};

export const MarketplaceContentPage = () => {
  const { slug } = useParams<{ slug: string }>();
  const normalizedSlug = slug?.trim().toLowerCase() || "";
  const pageQuery = useQuery({
    queryKey: ["marketplace-page", normalizedSlug],
    queryFn: ({ signal }) => marketplaceContentService.getPage(normalizedSlug, signal),
    enabled: normalizedSlug.length > 0
  });

  useDocumentTitle(pageQuery.data?.seo.metaTitle || pageQuery.data?.title || "Shopixy");

  useEffect(() => {
    if (!pageQuery.data) {
      return;
    }

    const descriptionContent = pageQuery.data.seo.metaDescription || "";
    let descriptionMeta = document.querySelector('meta[name="description"]');
    if (!descriptionMeta) {
      descriptionMeta = document.createElement("meta");
      descriptionMeta.setAttribute("name", "description");
      document.head.appendChild(descriptionMeta);
    }

    descriptionMeta.setAttribute("content", descriptionContent);
  }, [pageQuery.data]);

  if (!normalizedSlug) {
    return <EmptyState title="Page unavailable" description="The requested marketplace page could not be resolved." />;
  }

  if (pageQuery.isPending) {
    return <LoadingSkeleton className="min-h-[320px]" lines={4} />;
  }

  if (pageQuery.isError || !pageQuery.data) {
    return (
      <EmptyState
        title="Page unavailable"
        description={pageQuery.error?.message || "We couldn't load this marketplace page right now."}
      />
    );
  }

  const page = pageQuery.data;

  return (
    <div className="space-y-8">
      <header className="surface-panel rounded-[2rem] px-6 py-8 shadow-soft md:px-8 md:py-10">
        <p className="text-xs font-semibold uppercase tracking-[0.3em] text-primary">Marketplace page</p>
        <h1 className="mt-4 font-display text-4xl font-black tracking-tight text-foreground">{page.title}</h1>
        {page.seo.metaDescription ? (
          <p className="mt-4 max-w-3xl text-base leading-8 text-muted-foreground">{page.seo.metaDescription}</p>
        ) : null}
      </header>

      {page.contentType === "RichText" && typeof page.content === "string" ? (
        <article className="prose prose-slate max-w-none rounded-[2rem] border border-border/80 bg-white px-6 py-8 shadow-soft md:px-8">
          <div dangerouslySetInnerHTML={{ __html: sanitizeHtml(page.content) }} />
        </article>
      ) : (
        <div className="space-y-6">{Array.isArray(page.content) ? page.content.map(renderBlock) : null}</div>
      )}
    </div>
  );
};

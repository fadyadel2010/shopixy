import { ChevronDown, MessageCircleQuestion } from "lucide-react";
import { useState } from "react";

import type { CommerceHubProductFaq } from "@/types";

type ProductFaqSectionProps = {
  faqs: CommerceHubProductFaq[];
};

export const ProductFaqSection = ({ faqs }: ProductFaqSectionProps) => {
  const [openIndex, setOpenIndex] = useState(0);

  if (faqs.length === 0) {
    return null;
  }

  return (
    <div className="space-y-6">
      <div className="rounded-[2rem] border border-white/75 bg-white/92 p-6 shadow-[0_30px_60px_-42px_rgba(15,23,42,0.45)]">
        <div className="flex items-start gap-4">
          <div className="brand-gradient-soft rounded-[1.5rem] p-3 text-primary">
            <MessageCircleQuestion className="h-5 w-5" />
          </div>
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">Common questions</p>
            <h2 className="text-2xl font-black tracking-tight text-foreground">Quick answers before checkout</h2>
            <p className="text-sm leading-7 text-muted-foreground">
              Frequently asked questions stay structured, which keeps product pages easier to scan on both desktop and mobile.
            </p>
          </div>
        </div>
      </div>

      <div className="space-y-3">
        {faqs.map((faq, index) => {
          const isOpen = index === openIndex;
          const buttonId = `product-faq-trigger-${index}`;
          const panelId = `product-faq-panel-${index}`;

          return (
            <section
              key={`${faq.question}-${index}`}
              className={[
                "overflow-hidden rounded-[1.75rem] border bg-white/90 shadow-[0_24px_48px_-40px_rgba(15,23,42,0.38)]",
                isOpen ? "border-primary/25" : "border-white/75"
              ].join(" ")}
            >
              <button
                id={buttonId}
                type="button"
                className="flex w-full items-center justify-between gap-4 px-5 py-5 text-left"
                aria-expanded={isOpen}
                aria-controls={panelId}
                onClick={() => setOpenIndex((currentIndex) => (currentIndex === index ? -1 : index))}
              >
                <span className="text-base font-semibold text-foreground">{faq.question}</span>
                <ChevronDown
                  className={[
                    "h-4 w-4 shrink-0 text-muted-foreground transition-transform",
                    isOpen ? "rotate-180" : ""
                  ].join(" ")}
                />
              </button>

              {isOpen ? (
                <div id={panelId} role="region" aria-labelledby={buttonId} className="border-t border-border/60 px-5 py-5">
                  <p className="text-sm leading-7 text-muted-foreground">
                    {faq.answer || "We’ll add the seller’s answer here as soon as it is available."}
                  </p>
                </div>
              ) : null}
            </section>
          );
        })}
      </div>
    </div>
  );
};

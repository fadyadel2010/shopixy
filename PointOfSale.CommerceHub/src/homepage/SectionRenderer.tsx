import type { ResolvedHomepageSection } from "@/homepage/types";

import { SectionErrorBoundary } from "./SectionErrorBoundary";
import { BannerSection } from "./sections/BannerSection";
import { CategorySection } from "./sections/CategorySection";
import { HeroSection } from "./sections/HeroSection";
import { ProductGridSection } from "./sections/ProductGridSection";

type SectionRendererProps = {
  sections: ResolvedHomepageSection[];
};

export const SectionRenderer = ({ sections }: SectionRendererProps) => {
  return (
    <div className="space-y-8 md:space-y-10">
      {sections.map((section, index) => {
        const style = {
          animationDelay: `${index * 90}ms`
        };

        let content: JSX.Element | null = null;

        switch (section.type) {
          case "hero":
            content = <HeroSection data={section.data} />;
            break;
          case "products":
            content = <ProductGridSection data={section.data} />;
            break;
          case "banner":
            content = <BannerSection data={section.data} />;
            break;
          case "categories":
            content = <CategorySection data={section.data} />;
            break;
          default:
            content = null;
            break;
        }

        if (!content) {
          return null;
        }

        return (
          <div key={section.id} className="animate-enter" style={style}>
            <SectionErrorBoundary sectionId={section.id} sectionType={section.type}>
              {content}
            </SectionErrorBoundary>
          </div>
        );
      })}
    </div>
  );
};

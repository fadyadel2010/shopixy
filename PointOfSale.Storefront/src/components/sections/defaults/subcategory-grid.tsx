import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useCategories } from '@/hooks';
import { useTranslation } from '@/i18n';

const SUBCATEGORY_ICONS = [
    '\u{1F4E6}',
    '\u{1F455}',
    '\u{1F4BB}',
    '\u{1F3E0}',
    '\u{1F3AE}',
    '\u{1F4DA}',
    '\u{1F3A8}',
    '\u{1F37D}\uFE0F',
    '\u{1F484}',
    '\u{1F3C3}',
];

const SubcategoryGrid: React.FC<any> = ({ title, columns = 3 }) => {
    const { t, language } = useTranslation();
    const { categories, isLoading, error } = useCategories();

    const getCategoryIcon = (index: number): string => SUBCATEGORY_ICONS[index % SUBCATEGORY_ICONS.length];

    const getCategoryName = (category: { name: string; nameAr?: string }) => {
        if (language === 'ar' && category.nameAr) {
            return category.nameAr;
        }

        return category.name;
    };

    const allSubcategories = categories.flatMap((category) => category.children || []);

    const displayTitle = (!title || title === 'Subcategories')
        ? t('category.subcategories')
        : title;

    if (!isLoading && allSubcategories.length === 0) {
        return null;
    }

    return (
        <section className="py-12 px-4 container mx-auto">
            <div className="text-center mb-8">
                <h2 className="text-2xl font-bold text-text">{displayTitle}</h2>
            </div>

            {isLoading ? (
                <div className={`grid grid-cols-2 md:grid-cols-${columns} gap-6`}>
                    {[...Array(6)].map((_, index) => (
                        <div key={index} className="h-40 bg-muted/10 rounded-xl animate-pulse" />
                    ))}
                </div>
            ) : error ? (
                <div className="text-center py-8 text-muted">{t('home.failedToLoadCategories')}</div>
            ) : (
                <div className={`grid grid-cols-2 md:grid-cols-${columns} gap-6`}>
                    {allSubcategories.slice(0, 6).map((subcategory, index) => (
                        <AffiliateLink
                            key={subcategory.id}
                            href={`/products?category=${subcategory.id}`}
                            className="group relative rounded-xl overflow-hidden h-40 bg-gradient-to-br from-secondary/10 to-primary/10 flex flex-col items-center justify-center hover:shadow-lg transition-all border border-muted/10 hover:border-secondary/30"
                        >
                            <div className="text-4xl mb-3 group-hover:scale-110 transition-transform">
                                {getCategoryIcon(index + 3)}
                            </div>
                            <span className="text-lg font-semibold text-text group-hover:text-secondary transition-colors">
                                {getCategoryName(subcategory)}
                            </span>
                        </AffiliateLink>
                    ))}
                </div>
            )}
        </section>
    );
};

export default SubcategoryGrid;

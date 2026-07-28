import React from 'react';
import { getPurchaseExperienceProfile } from '@/storefront/products/purchaseExperience';
import { useTheme } from '@/storefront/themes/ThemeContext';

interface ReviewItem {
    name?: string;
    rating?: number;
    text?: string;
    avatar?: string;
}

interface CustomerReviewsProps {
    title?: string;
    reviews?: ReviewItem[];
    layoutVariant?: string;
}

const CustomerReviews: React.FC<CustomerReviewsProps> = ({
    title,
    reviews = [],
    layoutVariant = 'default',
}) => {
    const { themeId } = useTheme();
    const profile = getPurchaseExperienceProfile(layoutVariant, themeId);
    const items = Array.isArray(reviews) ? reviews.filter((review) => review?.name || review?.text) : [];

    if (items.length === 0) {
        return null;
    }

    return (
        <section className={`${profile.sectionClassName} px-4 py-8`} data-purchase-experience={profile.variant}>
            <div className="container mx-auto">
                <div className={profile.reviewShellClassName}>
                    <h2 className="text-2xl font-bold text-text">{title || profile.reviewsTitle}</h2>

                    <div className="mt-6 grid gap-4 md:grid-cols-2">
                        {items.map((review, index) => {
                            const rating = Math.max(1, Math.min(5, Number(review.rating || 5)));

                            return (
                                <article key={`${review.name || 'review'}-${index}`} className={profile.reviewCardClassName}>
                                    <div className="flex items-center gap-3">
                                        {review.avatar ? (
                                            <img
                                                src={review.avatar}
                                                alt={review.name || 'Customer'}
                                                className="h-12 w-12 rounded-full object-cover"
                                            />
                                        ) : (
                                            <div className="flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 font-semibold text-primary">
                                                {(review.name || 'C').charAt(0).toUpperCase()}
                                            </div>
                                        )}
                                        <div>
                                            <div className="font-semibold text-text">{review.name || 'Customer'}</div>
                                            <div className="text-sm text-yellow-500" aria-label={`${rating} out of 5 stars`}>
                                                {'★'.repeat(rating)}
                                            </div>
                                        </div>
                                    </div>

                                    {review.text ? <p className="mt-4 leading-7 text-muted">{review.text}</p> : null}
                                </article>
                            );
                        })}
                    </div>
                </div>
            </div>
        </section>
    );
};

export default CustomerReviews;

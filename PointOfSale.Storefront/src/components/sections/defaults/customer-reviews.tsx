import React from 'react';

interface ReviewItem {
    name?: string;
    rating?: number;
    text?: string;
    avatar?: string;
}

interface CustomerReviewsProps {
    title?: string;
    reviews?: ReviewItem[];
}

const CustomerReviews: React.FC<CustomerReviewsProps> = ({ title, reviews = [] }) => {
    const items = Array.isArray(reviews) ? reviews.filter((review) => review?.name || review?.text) : [];

    if (items.length === 0) {
        return null;
    }

    return (
        <section className="container mx-auto px-4 py-8">
            <div className="rounded-3xl border border-muted/20 bg-background p-6 shadow-sm">
                <h2 className="text-2xl font-bold text-text">{title || 'Customer Reviews'}</h2>

                <div className="mt-6 grid gap-4 md:grid-cols-2">
                    {items.map((review, index) => (
                        <article key={`${review.name || 'review'}-${index}`} className="rounded-2xl border border-muted/20 bg-muted/5 p-5">
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
                                    <div className="text-sm text-yellow-500">{'★'.repeat(Math.max(1, Math.min(5, Number(review.rating || 5))))}</div>
                                </div>
                            </div>

                            {review.text ? <p className="mt-4 leading-7 text-muted">{review.text}</p> : null}
                        </article>
                    ))}
                </div>
            </div>
        </section>
    );
};

export default CustomerReviews;

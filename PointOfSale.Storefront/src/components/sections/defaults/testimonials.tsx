import React from 'react';
import { useTranslation } from '@/i18n';

interface TestimonialItem {
    name?: string;
    avatar?: string;
    text?: string;
}

interface TestimonialsProps {
    title?: string;
    subtitle?: string;
    testimonials?: TestimonialItem[];
}

const Testimonials: React.FC<TestimonialsProps> = ({ title, subtitle, testimonials }) => {
    const { t } = useTranslation();
    const items = Array.isArray(testimonials) ? testimonials : [];

    return (
        <section className="py-12 px-4 bg-muted/5">
            <div className="container mx-auto">
                <h2 className="text-2xl font-bold text-text text-center mb-8">
                    {title || t('home.whatCustomersSay')}
                </h2>
                {subtitle ? <p className="mx-auto mb-8 max-w-2xl text-center text-sm leading-6 text-muted">{subtitle}</p> : null}

                {items.length === 0 ? (
                    <div className="rounded-2xl border border-dashed border-muted/30 bg-background px-6 py-12 text-center text-sm text-muted">
                        Add testimonials in the builder to display customer feedback.
                    </div>
                ) : (
                    <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
                        {items.map((testimonial, index) => (
                            <div
                                key={`${testimonial.name || 'testimonial'}-${index}`}
                                className="min-w-[280px] rounded-xl border bg-white p-6 shadow-sm"
                            >
                                <p className="mb-4 italic text-text">"{testimonial.text}"</p>
                                <div className="flex items-center gap-3">
                                    {testimonial.avatar ? (
                                        <img
                                            src={testimonial.avatar}
                                            alt={testimonial.name || 'Customer avatar'}
                                            className="h-10 w-10 rounded-full object-cover"
                                        />
                                    ) : (
                                        <div className="flex h-10 w-10 items-center justify-center rounded-full bg-primary/10 font-semibold text-primary">
                                            {(testimonial.name || '?')[0]}
                                        </div>
                                    )}
                                    <p className="font-semibold text-text">{testimonial.name}</p>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </section>
    );
};

export default Testimonials;

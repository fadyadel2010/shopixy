import React from 'react';
import { useTranslation } from '@/i18n';

interface NewsletterSignupProps {
    heading?: string;
    subheading?: string;
    buttonText?: string;
    backgroundColor?: string;
    textColor?: string;
}

const NewsletterSignup: React.FC<NewsletterSignupProps> = ({
    heading,
    subheading,
    buttonText,
    backgroundColor,
    textColor,
}) => {
    const { t } = useTranslation();

    const displayHeading = (!heading || heading === 'Stay Updated')
        ? t('footer.stayUpdated')
        : heading;
    const displaySubheading = (!subheading || subheading === 'Subscribe to our newsletter for exclusive offers and updates')
        ? t('footer.subscribeText')
        : subheading;
    const displayButtonText = (!buttonText || buttonText === 'Subscribe')
        ? t('footer.subscribe')
        : buttonText;

    return (
        <section
            className="py-16 px-4"
            style={{
                backgroundColor: backgroundColor || '#0f172a',
                color: textColor || '#ffffff',
            }}
        >
            <div className="max-w-2xl mx-auto text-center">
                <h2 className="text-2xl md:text-3xl font-bold mb-4">{displayHeading}</h2>
                <p className="opacity-80 mb-8">{displaySubheading}</p>
                <form className="flex flex-col sm:flex-row gap-4 justify-center" onSubmit={(event) => event.preventDefault()}>
                    <input
                        type="email"
                        placeholder={t('footer.enterEmail')}
                        className="px-6 py-3 rounded-full text-text bg-background placeholder:text-muted flex-1 max-w-md"
                    />
                    <button
                        type="submit"
                        className="px-8 py-3 bg-primary text-on-primary rounded-full font-semibold hover:opacity-90 transition-opacity"
                    >
                        {displayButtonText}
                    </button>
                </form>
            </div>
        </section>
    );
};

export default NewsletterSignup;

import React, { useMemo } from 'react';
import { sanitizeRichTextHtml } from '@/utils/htmlSanitizer';

export interface TextBlockProps {
    heading?: string | React.ReactNode;
    body?: string;
    alignment?: 'left' | 'center' | 'right';
    maxWidth?: string;
}

const maxWidthClasses: Record<string, string> = {
    narrow: 'max-w-2xl',
    default: 'max-w-4xl',
    wide: 'max-w-6xl',
};

const TextBlock: React.FC<TextBlockProps> = ({ heading, body, alignment, maxWidth = 'default' }) => {
    const sanitizedBody = useMemo(() => sanitizeRichTextHtml(body), [body]);

    return (
        <section
            className="py-12 px-4"
            style={{
                textAlign: alignment || 'left',
            }}
        >
            <div className={`mx-auto ${maxWidthClasses[maxWidth] || maxWidthClasses.default}`}>
                {heading && <h2 className="text-2xl font-bold mb-4">{heading}</h2>}
                {sanitizedBody && (
                    <div
                        className="prose prose-lg max-w-none"
                        dangerouslySetInnerHTML={{ __html: sanitizedBody }}
                    />
                )}
            </div>
        </section>
    );
};

export default TextBlock;

import React, { useMemo } from 'react';
import { sanitizeRichTextHtml } from '@/utils/htmlSanitizer';

export interface TextBlockProps {
    heading?: string | React.ReactNode;
    content?: string;
    alignment?: 'left' | 'center' | 'right';
    backgroundColor?: string;
    textColor?: string;
    maxWidth?: string;
}

const TextBlock: React.FC<TextBlockProps> = ({ heading, content, alignment, backgroundColor, textColor, maxWidth }) => {
    const sanitizedContent = useMemo(() => sanitizeRichTextHtml(content), [content]);

    return (
        <section
            className="py-12 px-4"
            style={{
                backgroundColor: backgroundColor || 'transparent',
                color: textColor || 'inherit',
                textAlign: alignment || 'left',
            }}
        >
            <div 
                className="container mx-auto"
                style={{ maxWidth: maxWidth || '896px' }}
            >
                {heading && <h2 className="text-2xl font-bold mb-4">{heading}</h2>}
                {sanitizedContent && (
                    <div
                        className="prose prose-lg max-w-none"
                        dangerouslySetInnerHTML={{ __html: sanitizedContent }}
                    />
                )}
            </div>
        </section>
    );
};

export default TextBlock;

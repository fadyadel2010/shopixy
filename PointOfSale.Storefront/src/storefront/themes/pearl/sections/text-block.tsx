/**
 * Pearl Theme — Brand Story / Editorial Text Block (Phase T1.3)
 *
 * REUSE DECISION
 * --------------
 * ADAPTATION (~85% reuse) of `components/sections/defaults/text-block.tsx`.
 * Contract preserved verbatim: `heading, content, alignment, backgroundColor,
 * textColor, maxWidth`. Rich-text safety stays platform-owned via the SAME
 * `sanitizeRichTextHtml` utility (no sanitizer duplication).
 *
 * Pearl identity (design report §1/§3): centered-by-default editorial rhythm,
 * gold hairline divider, Newsreader light heading in navy `#313E61`, Jost
 * body with relaxed leading and generous whitespace.
 */

import React, { useMemo } from 'react';
import { sanitizeRichTextHtml } from '@/utils/htmlSanitizer';
import { PEARL_FONT_BODY, PEARL_FONT_DISPLAY } from '../layout/pearlTokens';

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
            className="px-4 py-16 sm:py-20"
            style={{
                backgroundColor: backgroundColor || 'transparent',
                color: textColor || 'inherit',
                textAlign: alignment || 'center',
            }}
        >
            <div
                className="container mx-auto"
                style={{ maxWidth: maxWidth || '768px' }}
            >
                {heading && (
                    <>
                        <h2 className={`text-[clamp(1.75rem,3.5vw,2.5rem)] font-extralight leading-tight tracking-[0.02em] text-[#313E61] ${PEARL_FONT_DISPLAY}`}>
                            {heading}
                        </h2>
                        <div className="mx-auto mt-6 mb-8 h-px w-16 bg-[#C9A863]" aria-hidden="true" />
                    </>
                )}
                {sanitizedContent && (
                    <div
                        className={`prose prose-lg max-w-none leading-8 text-[#131316]/80 ${PEARL_FONT_BODY}`}
                        dangerouslySetInnerHTML={{ __html: sanitizedContent }}
                    />
                )}
            </div>
        </section>
    );
};

export default TextBlock;

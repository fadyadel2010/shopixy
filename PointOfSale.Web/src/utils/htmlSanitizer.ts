import DOMPurify from 'dompurify';

const ALLOWED_TAGS = [
    'a',
    'b',
    'blockquote',
    'br',
    'code',
    'div',
    'em',
    'h1',
    'h2',
    'h3',
    'h4',
    'h5',
    'h6',
    'hr',
    'i',
    'li',
    'ol',
    'p',
    'pre',
    's',
    'span',
    'strong',
    'u',
    'ul',
] as const;

const ALLOWED_ATTR = ['href', 'rel', 'target'] as const;

const SANITIZE_CONFIG = {
    ALLOWED_TAGS: [...ALLOWED_TAGS],
    ALLOWED_ATTR: [...ALLOWED_ATTR],
    ALLOW_ARIA_ATTR: false,
    ALLOW_DATA_ATTR: false,
    ALLOW_UNKNOWN_PROTOCOLS: false,
};

const SAFE_ANCHOR_PROTOCOLS = new Set(['http:', 'https:', 'mailto:', 'tel:']);
const SAFE_ANCHOR_TARGETS = new Set(['_blank', '_self']);

const HOOKS_REGISTERED_SYMBOL = Symbol('htmlSanitizer.hooksRegistered');

function isSafeAnchorHref(value: string): boolean {
    const trimmedValue = value.trim();
    if (!trimmedValue) {
        return false;
    }

    // Reject protocol-relative URLs (//evil.example.com)
    if (trimmedValue.startsWith('//')) {
        return false;
    }

    if (trimmedValue.startsWith('/') || trimmedValue.startsWith('./') || trimmedValue.startsWith('../')) {
        return true;
    }

    if (trimmedValue.startsWith('#') || trimmedValue.startsWith('?')) {
        return true;
    }

    try {
        const parsedUrl = new URL(trimmedValue, 'https://sanitizer.local');
        return SAFE_ANCHOR_PROTOCOLS.has(parsedUrl.protocol);
    } catch {
        return false;
    }
}

function mergeRelValue(existingRel: string | null): string {
    const relTokens = new Set(
        (existingRel ?? '')
            .split(/\s+/)
            .map((token) => token.trim().toLowerCase())
            .filter(Boolean),
    );

    relTokens.add('noopener');
    relTokens.add('noreferrer');

    return Array.from(relTokens).join(' ');
}

function ensureHooksRegistered(): void {
    // Check if hooks are already registered on this DOMPurify instance
    if ((DOMPurify as any)[HOOKS_REGISTERED_SYMBOL]) {
        return;
    }

    DOMPurify.addHook('afterSanitizeAttributes', (node) => {
        if (!(node instanceof Element)) {
            return;
        }

        Array.from(node.attributes).forEach((attribute) => {
            if (attribute.name.toLowerCase().startsWith('on')) {
                node.removeAttribute(attribute.name);
            }
        });

        if (!(node instanceof HTMLAnchorElement)) {
            return;
        }

        const href = node.getAttribute('href');
        if (!href || !isSafeAnchorHref(href)) {
            node.removeAttribute('href');
        }

        const target = node.getAttribute('target');
        if (target && !SAFE_ANCHOR_TARGETS.has(target.toLowerCase())) {
            node.removeAttribute('target');
        }

        if (node.getAttribute('target') === '_blank') {
            node.setAttribute('rel', mergeRelValue(node.getAttribute('rel')));
            return;
        }

        node.removeAttribute('rel');
    });

    // Mark this DOMPurify instance as having hooks registered
    (DOMPurify as any)[HOOKS_REGISTERED_SYMBOL] = true;
}

export function sanitizeRichTextHtml(html: string | null | undefined): string {
    if (typeof html !== 'string' || !html.trim()) {
        return '';
    }

    ensureHooksRegistered();
    return String(DOMPurify.sanitize(html, SANITIZE_CONFIG));
}

// HMR cleanup: Remove hooks during hot reload to prevent duplicates
if (import.meta.hot) {
    import.meta.hot.dispose(() => {
        DOMPurify.removeAllHooks();
        delete (DOMPurify as any)[HOOKS_REGISTERED_SYMBOL];
    });
}

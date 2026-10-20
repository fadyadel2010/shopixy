import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { readFileSync } from 'node:fs';
import { MediaFrame } from './MediaFrame';
import { ProductMediaViewer } from '@/components/products/ProductMediaViewer';

describe('bounded Storefront media contracts', () => {
    it.each(['product-card','product-detail','hero','banner','content','category'] as const)('%s reserves role space before images load', role => {
        const html=renderToStaticMarkup(<MediaFrame role={role}><img src="/portrait.png" alt="Product" /></MediaFrame>);
        expect(html).toContain(`data-media-role="${role}"`);
        expect(html).toContain(`sf-media--${role}`);
    });
    it('keeps the authored CMS ratio independent of source dimensions', () => {
        expect(renderToStaticMarkup(<MediaFrame role="content" aspectRatio="21/9" />)).toContain('aspect-ratio:21/9');
    });
    it('gallery images default to contain in a bounded frame', () => {
        const html=renderToStaticMarkup(<ProductMediaViewer productName="Tall product" media={{id:'1',mediaType:'image',url:'/tall.png'} as never} />);
        expect(html).toContain('sf-media--product-detail');
        expect(html).toContain('object-contain');
    });
    it('loaded/error/delayed media cannot contribute intrinsic minimum size', () => {
        const css=readFileSync(new URL('./media.css',import.meta.url),'utf8');
        expect(css).toContain('position: absolute');
        expect(css).toContain('width: min(100%, 600px, 65svh)');
        expect(css).toContain('aspect-ratio: 1 / 1');
        expect(css).toContain('object-fit: contain');
    });
});

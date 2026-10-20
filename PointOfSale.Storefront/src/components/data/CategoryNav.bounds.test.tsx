import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { CategoryNav } from './CategoryNav';

describe('category navigation containment', () => {
    it('does not assume an outer 16px gutter in compact theme layouts', () => {
        const html=renderToStaticMarkup(<CategoryNav categories={[{id:'1',name:'Category'} as never]} onSelect={()=>{}} />);
        expect(html).toContain('max-w-full');
        expect(html).not.toContain('-mx-4');
        expect(html).toContain('overflow-x-auto');
        expect(html).toContain('text-on-primary');
    });
});

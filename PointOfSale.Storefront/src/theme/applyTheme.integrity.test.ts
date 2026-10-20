import { afterEach, describe, expect, it, vi } from 'vitest';
import { applyTheme } from './applyTheme';
import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig';
import { themeToCssVariables } from './cssVariables';

describe('theme application isolation and replacement', () => {
    afterEach(() => vi.unstubAllGlobals());
    it('replaces all owned tokens on light/dark/light switching without touching unrelated styles', () => {
        const properties=new Map<string,string>([['--admin-color','keep'],['--section-owned','preserve']]);
        const setProperty=vi.fn((key:string,value:string)=>properties.set(key,value));
        const bodyStyle={backgroundColor:'',color:''};
        vi.stubGlobal('document',{documentElement:{style:{setProperty}},body:{style:bodyStyle}});
        const light=DEFAULT_STORE_CONFIG.theme;
        const dark={...light,colors:{primary:'#3b82f6',secondary:'#1e293b',background:'#0f172a',text:'#f8fafc',muted:'#94a3b8'}};
        for(const theme of [light,dark,light]) {
            applyTheme(theme);
            for(const [key,value] of Object.entries(themeToCssVariables(theme)))expect(properties.get(key)).toBe(value);
        }
        expect(properties.get('--admin-color')).toBe('keep');
        expect(properties.get('--section-owned')).toBe('preserve');
        expect(bodyStyle).toEqual({backgroundColor:'',color:''});
        expect(properties.get('--color-background-rgb')).toBe('255, 255, 255');
    });
});

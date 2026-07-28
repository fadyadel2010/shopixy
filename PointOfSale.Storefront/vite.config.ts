import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'
import { createHash } from 'node:crypto'

const viteReactRefreshPreamble = [
    'import { injectIntoGlobalHook } from "/@react-refresh";',
    'injectIntoGlobalHook(window);',
    'window.$RefreshReg$ = () => {};',
    'window.$RefreshSig$ = () => (type) => type;',
].join('\n')

function createSha256Source(value: string) {
    return `'sha256-${createHash('sha256').update(value).digest('base64')}'`
}

function createStorefrontCsp({ allowReactRefreshPreamble }: { allowReactRefreshPreamble: boolean }) {
    const scriptSources = [
        "'self'",
        ...(allowReactRefreshPreamble ? [createSha256Source(viteReactRefreshPreamble)] : []),
        'https://js.stripe.com',
        'https://accounts.google.com',
        'https://connect.facebook.net',
    ]

    return [
        "default-src 'self'",
        `script-src ${scriptSources.join(' ')}`,
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
        "font-src 'self' https://fonts.gstatic.com data:",
        "img-src 'self' data: blob: https:",
        "connect-src 'self' https: ws: wss:",
        "frame-src 'self' https://js.stripe.com https://hooks.stripe.com https://www.facebook.com https://accounts.google.com https://*.google.com",
        "object-src 'none'",
        "base-uri 'self'",
        "frame-ancestors 'none'",
        "form-action 'self'",
    ].join('; ')
}

// https://vitejs.dev/config/
export default defineConfig(({ command }) => {
    const isDevServer = command === 'serve'
    const storefrontCsp = createStorefrontCsp({
        allowReactRefreshPreamble: isDevServer,
    })

    return {
        plugins: [
            react(),
            {
                name: 'shopixy-storefront-csp',
                transformIndexHtml() {
                    if (isDevServer) {
                        return []
                    }

                    return [
                        {
                            tag: 'meta',
                            attrs: {
                                'http-equiv': 'Content-Security-Policy',
                                content: storefrontCsp,
                            },
                            injectTo: 'head-prepend',
                        },
                    ]
                },
            },
        ],
        resolve: {
            alias: {
                '@': path.resolve(__dirname, './src'),
                '@theme-authority': path.resolve(__dirname, '../theme-authority'),
            },
        },
        server: {
            fs: {
                allow: [path.resolve(__dirname), path.resolve(__dirname, '../theme-authority')],
            },
            headers: {
                'Content-Security-Policy': storefrontCsp,
            },
            port: 5174,
            proxy: {
                '/api': {
                    target: 'http://localhost:5010',
                    changeOrigin: false,
                    secure: false,
                    // IMPORTANT: Configure cookie handling for auth
                    // cookieDomainRewrite rewrites the cookie domain to match the frontend
                    cookieDomainRewrite: {
                        'localhost': 'localhost'
                    },
                    // Preserve Set-Cookie headers from the backend
                    configure: (proxy) => {
                        proxy.on('proxyRes', (proxyRes) => {
                            // Log cookies being set for debugging
                            const setCookie = proxyRes.headers['set-cookie'];
                            if (setCookie) {
                                console.log('[Proxy] Set-Cookie headers:', setCookie.length);
                            }
                        });
                    },
                },
            },
        },
        preview: {
            headers: {
                'Content-Security-Policy': storefrontCsp,
            },
        },
    }
})

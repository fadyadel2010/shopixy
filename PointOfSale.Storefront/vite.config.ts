import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

const storefrontCsp = [
    "default-src 'self'",
    "script-src 'self' https://js.stripe.com https://accounts.google.com https://connect.facebook.net",
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

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [react()],
    resolve: {
        alias: {
            '@': path.resolve(__dirname, './src'),
        },
    },
    server: {
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
})

import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

process.env.BROWSERSLIST_IGNORE_OLD_DATA = '1'

const adminCsp = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline' https://js.stripe.com https://accounts.google.com https://connect.facebook.net",
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
      '@web': path.resolve(__dirname, './src'),
    },
  },
  build: {
    // Increased from default 500 KB to accommodate large vendor bundles (React, UI libraries, etc.)
    chunkSizeWarningLimit: 5000,
    rollupOptions: {
      onwarn(warning, warn) {
        // Suppress known safe warnings from node_modules
        if (warning.code === 'THIS_IS_UNDEFINED') {
          // Ignore "use of `this` keyword" warnings from third-party libraries
          return
        }

        if (
          warning.message?.includes('contains an annotation that Rollup cannot interpret')
          || warning.message?.includes('dynamically imported by')
          || warning.message?.includes('[baseline-browser-mapping]')
        ) {
          return
        }

        // Forward all other warnings to default handler
        warn(warning)
      },
    },
  },
  server: {
    headers: {
      'Content-Security-Policy': adminCsp,
    },
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5010',
        changeOrigin: true,
        secure: false
      },
      '/hubs': {
        target: 'http://localhost:5010',
        changeOrigin: true,
        secure: false,
        ws: true
      }
    }
  },
  preview: {
    headers: {
      'Content-Security-Policy': adminCsp,
    },
  }
})

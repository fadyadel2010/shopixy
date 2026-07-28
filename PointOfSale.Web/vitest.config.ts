import { defineConfig } from 'vitest/config';
import path from 'path';

export default defineConfig({
  test: {
    globals: true,
    environment: 'jsdom', // Use jsdom for React component tests
    include: ['src/**/*.spec.ts', 'src/**/*.test.ts', 'src/**/*.spec.tsx', 'src/**/*.test.tsx'],
    setupFiles: ['./src/test/setup.ts'],
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, '../PointOfSale.Storefront/src'),
      '@web': path.resolve(__dirname, './src'),
      '@theme-authority': path.resolve(__dirname, '../theme-authority'),
    },
  },
});

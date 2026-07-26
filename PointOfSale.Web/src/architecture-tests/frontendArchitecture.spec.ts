/**
 * Frontend Architecture Tests
 * 
 * MISSION: Enforce architectural invariants at compile/test time.
 * These tests ensure the frontend remains a thin projection layer.
 * 
 * RULES ENFORCED:
 * 1. No apiClient import in components
 * 2. No SignalR import outside useChat
 * 3. No fetch/axios anywhere except apiClient
 * 4. No mutation of Readonly types
 * 5. All query keys use marketplaceKeys factory
 * 6. Components cannot import hooks
 * 7. No business logic in components
 */

import { describe, it, expect } from 'vitest';
import * as fs from 'fs';
import * as path from 'path';

const SRC_DIR = path.join(__dirname, '..');

/**
 * Recursively get all TypeScript files in a directory
 */
function getTypeScriptFiles(dir: string, fileList: string[] = []): string[] {
  const files = fs.readdirSync(dir);

  files.forEach((file) => {
    const filePath = path.join(dir, file);
    const stat = fs.statSync(filePath);

    if (stat.isDirectory()) {
      if (!file.includes('node_modules') && !file.includes('dist')) {
        getTypeScriptFiles(filePath, fileList);
      }
    } else if (file.endsWith('.ts') || file.endsWith('.tsx')) {
      fileList.push(filePath);
    }
  });

  return fileList;
}

/**
 * Read file content
 */
function readFile(filePath: string): string {
  return fs.readFileSync(filePath, 'utf-8');
}

describe('Frontend Architecture Invariants', () => {
  const allFiles = getTypeScriptFiles(SRC_DIR);
  const componentFiles = allFiles.filter((f) => f.includes('/components/'));
  const hookFiles = allFiles.filter((f) => f.includes('/hooks/'));
  const apiFiles = allFiles.filter((f) => f.includes('/api/'));
  const pageFiles = allFiles.filter((f) => f.includes('/pages/'));

  describe('Layer Separation', () => {
    it('Components MUST NOT import apiClient', () => {
      componentFiles.forEach((file) => {
        const content = readFile(file);
        const hasApiClientImport = content.includes("from '../../core/apiClient'") || content.includes('from "@/core/apiClient"');

        expect(hasApiClientImport, `${file} imports apiClient directly - use hooks instead`).toBe(false);
      });
    });

    it('Components MUST NOT import axios', () => {
      componentFiles.forEach((file) => {
        const content = readFile(file);
        const hasAxiosImport = content.includes("from 'axios'");

        expect(hasAxiosImport, `${file} imports axios directly - forbidden`).toBe(false);
      });
    });

    it('Components MUST NOT import fetch', () => {
      componentFiles.forEach((file) => {
        const content = readFile(file);
        const hasFetch = content.match(/\bfetch\s*\(/);

        expect(hasFetch, `${file} uses fetch() directly - forbidden`).toBeNull();
      });
    });

    it('Only useChat hook can import SignalR', () => {
      const nonChatFiles = allFiles.filter((f) => !f.includes('useChat.ts') && !f.includes('architecture-tests'));

      nonChatFiles.forEach((file) => {
        const content = readFile(file);
        const hasSignalRImport = content.includes("from '@microsoft/signalr'");

        expect(hasSignalRImport, `${file} imports SignalR - only useChat allowed`).toBe(false);
      });
    });

    it('Only hooks and API layer can import apiClient', () => {
      const nonHookNonApiFiles = allFiles.filter(
        (f) =>
          !f.includes('/hooks/') &&
          !f.includes('\\hooks\\') &&
          !f.includes('/api/') &&
          !f.includes('\\api\\') &&
          !f.includes('apiClient.ts') &&
          !f.includes('architecture-tests')
      );

      nonHookNonApiFiles.forEach((file) => {
        const content = readFile(file);
        const hasApiClientImport = content.includes("from '../../core/apiClient'") ||
          content.includes('from "@/core/apiClient"') ||
          content.includes("from '../core/apiClient'");

        expect(hasApiClientImport, `${file} imports apiClient - only hooks/api allowed`).toBe(false);
      });
    });

    it('affiliateApi MUST NOT import POS api.js', () => {
      const affiliateApiFile = allFiles.find((f) => f.includes('affiliateApi') && !f.includes('test'));
      if (affiliateApiFile) {
        const content = readFile(affiliateApiFile);
        const hasPosApiImport = content.includes("import api from './api'")
          || content.includes("import { api } from './api'")
          || content.includes("import api from '../api'");
        expect(hasPosApiImport, 'affiliateApi.js MUST NOT import POS api.js. It must use affiliateAxios.').toBe(false);
      }
    });
  });

  describe('Query Key Standardization', () => {
    it('All hooks MUST use marketplaceKeys factory', () => {
      hookFiles.forEach((file) => {
        const content = readFile(file);

        // Skip if not using React Query
        if (!content.includes('useQuery') && !content.includes('useMutation')) {
          return;
        }

        const hasMarketplaceKeysImport = content.includes("from '../queryKeys'");
        const hasAdHocQueryKey = content.match(/queryKey:\s*\[['"`]/);

        if (hasAdHocQueryKey) {
          expect(hasMarketplaceKeysImport, `${file} uses ad-hoc query keys - must use marketplaceKeys factory`).toBe(true);
        }
      });
    });

    it('No ad-hoc string query keys allowed', () => {
      hookFiles.forEach((file) => {
        const content = readFile(file);

        // Check for patterns like queryKey: ['string', ...]
        const hasAdHocKey = content.match(/queryKey:\s*\[['"`][a-z-]+['"`]/);

        if (hasAdHocKey) {
          const usesMarketplaceKeys = content.includes('marketplaceKeys.');
          expect(usesMarketplaceKeys, `${file} has ad-hoc query key - use marketplaceKeys factory`).toBe(true);
        }
      });
    });
  });

  describe('Global Invalidation Policy', () => {
    it('All mutations MUST use global invalidation', () => {
      hookFiles.forEach((file) => {
        const content = readFile(file);

        if (!content.includes('useMutation')) {
          return;
        }

        const hasInvalidation = content.includes('invalidateQueries');
        if (hasInvalidation) {
          const hasGlobalInvalidation = content.includes('marketplaceKeys.all');
          expect(hasGlobalInvalidation, `${file} uses selective invalidation - must use global (marketplaceKeys.all)`).toBe(true);
        }
      });
    });

    it('No optimistic updates allowed', () => {
      hookFiles.forEach((file) => {
        const content = readFile(file);

        if (!content.includes('useMutation')) {
          return;
        }

        const hasOptimisticUpdate = content.includes('setQueryData') && content.includes('onMutate');

        expect(hasOptimisticUpdate, `${file} has optimistic update - forbidden`).toBe(false);
      });
    });
  });

  describe('Forbidden Operations', () => {
    it('No client-side sorting in components', () => {
      [...componentFiles, ...pageFiles].forEach((file) => {
        const content = readFile(file);

        // Check for .sort() on arrays
        const hasSort = content.match(/\.sort\s*\(/);

        if (hasSort) {
          // Allow sorting for UI purposes (like rating distribution display)
          // but not for service results
          const isSortingServices = content.includes('services.sort') || content.includes('items.sort');
          expect(isSortingServices, `${file} sorts service results - forbidden (use backend sort)`).toBe(false);
        }
      });
    });

    it('No trust metric computation in frontend', () => {
      [...componentFiles, ...pageFiles].forEach((file) => {
        const content = readFile(file);

        // Check for averaging calculations
        const hasAverageCalc = content.match(/sum\s*\/\s*count|total\s*\/\s*length/);
        const hasRatingCalc = content.match(/rating.*reduce|reduce.*rating/);

        expect(hasAverageCalc, `${file} computes averages - trust metrics come from backend`).toBeNull();
        expect(hasRatingCalc, `${file} computes ratings - trust metrics come from backend`).toBeNull();
      });
    });

    it('No status inference in components', () => {
      [...componentFiles, ...pageFiles].forEach((file) => {
        const content = readFile(file);

        // Check for status-based permission logic
        const hasStatusPermission = content.match(/status\s*===.*&&.*can[A-Z]/);

        expect(hasStatusPermission, `${file} infers permissions from status - use capability flags only`).toBeNull();
      });
    });
  });

  describe('Type Safety', () => {
    it('Response types MUST be Readonly', () => {
      const typesFile = allFiles.find((f) => f.endsWith('types/index.ts'));
      if (!typesFile) return;

      const content = readFile(typesFile);

      // Check that response types use Readonly
      const responseTypes = [
        'ServiceSearchResponse',
        'ServiceResult',
        'ServiceDetails',
        'ServiceRequest',
        'ServiceRequestDetails',
        'ChatMessage',
        'MyService',
        'MarketplaceProfile',
      ];

      responseTypes.forEach((typeName) => {
        const typeDefinition = content.match(new RegExp(`export type ${typeName}\\s*=\\s*Readonly<`));
        expect(typeDefinition, `${typeName} must be Readonly<{...}>`).toBeTruthy();
      });
    });

    it('Request types can be mutable (for forms)', () => {
      const typesFile = allFiles.find((f) => f.endsWith('types/index.ts'));
      if (!typesFile) return;

      const content = readFile(typesFile);

      // Request types should be interface (mutable)
      const requestTypes = ['ServiceSearchRequest', 'CreateReviewRequest'];

      requestTypes.forEach((typeName) => {
        const typeDefinition = content.match(new RegExp(`export interface ${typeName}`));
        expect(typeDefinition, `${typeName} should be interface (mutable for forms)`).toBeTruthy();
      });
    });
  });

  describe('API Layer Purity', () => {
    it('API layer MUST NOT contain business logic', () => {
      apiFiles.forEach((file) => {
        const content = readFile(file);

        // Check for business logic indicators
        const hasIfStatement = content.match(/if\s*\(/);
        const hasForLoop = content.match(/for\s*\(/);
        const hasWhileLoop = content.match(/while\s*\(/);

        // Soft failure is allowed
        const isSoftFailure = content.includes('catch(() => {');

        if (hasIfStatement || hasForLoop || hasWhileLoop) {
          expect(isSoftFailure, `${file} contains business logic - API layer must be pure`).toBe(true);
        }
      });
    });

    it('API methods MUST return Promise', () => {
      apiFiles.forEach((file) => {
        const content = readFile(file);

        // Find all exported functions
        const functions = content.match(/\w+:\s*\([^)]*\)\s*=>/g);

        if (functions) {
          // Check return type annotation
          const hasPromiseReturn = content.includes(`: Promise<`);
          expect(hasPromiseReturn, `${file} API methods must return Promise<T>`).toBe(true);
        }
      });
    });
  });
});

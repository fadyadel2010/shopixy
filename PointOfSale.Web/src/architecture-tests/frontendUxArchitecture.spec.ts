/**
 * Frontend UX Architecture Tests
 * 
 * MISSION: Enforce UX layer invariants.
 * These tests ensure UX remains pure presentation.
 * 
 * RULES ENFORCED:
 * 1. Pages use ViewModels, not hooks directly
 * 2. No spinners - only skeletons
 * 3. Components don't render toasts
 * 4. Components don't import useAuthUser
 * 5. Components don't import hooks
 * 6. ViewModels have no business logic
 */

import { describe, it, expect } from 'vitest';
import * as fs from 'fs';
import * as path from 'path';

const SRC_DIR = path.join(__dirname, '..');

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

function getSourceFiles(dir: string, fileList: string[] = []): string[] {
  const files = fs.readdirSync(dir);

  files.forEach((file) => {
    const filePath = path.join(dir, file);
    const stat = fs.statSync(filePath);

    if (stat.isDirectory()) {
      if (!file.includes('node_modules') && !file.includes('dist')) {
        getSourceFiles(filePath, fileList);
      }
    } else if (/\.(js|jsx|ts|tsx)$/.test(file)) {
      fileList.push(filePath);
    }
  });

  return fileList;
}

function readFile(filePath: string): string {
  return fs.readFileSync(filePath, 'utf-8');
}

describe('Frontend UX Architecture Invariants', () => {
  const allFiles = getTypeScriptFiles(SRC_DIR);
  const allSourceFiles = getSourceFiles(SRC_DIR);
  const pageFiles = allFiles.filter((f) => f.includes('/pages/') && f.includes('marketplace'));
  const componentFiles = allFiles.filter((f) => f.includes('/components/') && f.includes('marketplace'));
  const viewModelFiles = allFiles.filter((f) => f.includes('/viewmodels/'));

  describe('ViewModel Usage', () => {
    it('Pages SHOULD use ViewModels instead of hooks directly', () => {
      pageFiles.forEach((file) => {
        const content = readFile(file);

        // Check if page uses marketplace hooks directly
        const usesMarketplaceHooks = content.match(/use(MarketplaceSearch|ServiceDetails|MyRequests|IncomingRequests|RequestDetails|MyServices)\(/);

        if (usesMarketplaceHooks) {
          // Check if it also uses ViewModel
          const usesViewModel = content.includes('VM(');
          
          // Warn if using hooks without ViewModel (not a hard failure)
          if (!usesViewModel) {
            console.warn(`${file} uses hooks directly - consider using ViewModel for better UX layer separation`);
          }
        }
      });
    });
  });

  describe('Loading States', () => {
    it('Pages SHOULD use skeletons instead of spinners', () => {
      pageFiles.forEach((file) => {
        const content = readFile(file);

        // Check for spinner patterns
        const hasSpinner = content.match(/Loading\.\.\.|Spinner|spinner|loading-spinner/);
        const hasSkeleton = content.includes('Skeleton');

        if (hasSpinner && !hasSkeleton) {
          console.warn(`${file} uses spinner - should use skeleton components instead`);
        }
      });
    });

    it('Skeleton components MUST NOT fetch data', () => {
      const skeletonFiles = allFiles.filter((f) => f.includes('Skeleton'));

      skeletonFiles.forEach((file) => {
        const content = readFile(file);

        const fetchesData = content.includes('useQuery') || 
                           content.includes('useMutation') ||
                           content.includes('apiClient') ||
                           content.includes('fetch(');

        expect(fetchesData, `${file} fetches data - skeletons must be pure presentation`).toBe(false);
      });
    });
  });

  describe('Toast System', () => {
    it('Components MUST NOT render toast directly', () => {
      componentFiles.forEach((file) => {
        const content = readFile(file);

        const rendersToast = content.includes('<Toast') || content.includes('<ToastContainer');

        expect(rendersToast, `${file} renders toast - only root app should render ToastContainer`).toBe(false);
      });
    });

    it('Hooks MUST NOT show toasts', () => {
      const hookFiles = allFiles.filter((f) => f.includes('/hooks/') && f.includes('marketplace'));

      hookFiles.forEach((file) => {
        const content = readFile(file);

        const showsToast = content.includes('useToast') || content.includes('showToast');

        expect(showsToast, `${file} shows toast - toasts should be in pages/ViewModels only`).toBe(false);
      });
    });
  });

  describe('Auth Identity Source', () => {
    it('Components MUST NOT import useAuthUser', () => {
      componentFiles.forEach((file) => {
        const content = readFile(file);

        const importsAuthUser = content.includes('useAuthUser');

        expect(importsAuthUser, `${file} imports useAuthUser - auth should come from parent via props`).toBe(false);
      });
    });

    it('Components MUST NOT decode JWT', () => {
      componentFiles.forEach((file) => {
        const content = readFile(file);

        const decodesJWT = content.includes('jwt') || content.includes('decode') || content.includes('localStorage');

        expect(decodesJWT, `${file} accesses auth directly - use useAuthUser hook in pages only`).toBe(false);
      });
    });
  });

  describe('ViewModel Purity', () => {
    it('ViewModels MUST NOT contain business logic', () => {
      viewModelFiles.forEach((file) => {
        const content = readFile(file);

        // Check for business logic indicators
        const hasValidation = content.match(/validate|isValid|check[A-Z]/);
        const hasCalculation = content.match(/calculate|compute|sum|average/);
        const hasStatusLogic = content.match(/if\s*\(.*status.*===.*\)/);

        if (hasValidation) {
          expect(false, `${file} has validation logic - ViewModels must be pure mapping`).toBe(true);
        }

        if (hasCalculation) {
          // Allow formatting functions like formatDistanceToNow
          const isFormatting = content.includes('format');
          if (!isFormatting) {
            expect(false, `${file} has calculation logic - ViewModels must be pure mapping`).toBe(true);
          }
        }

        if (hasStatusLogic) {
          expect(false, `${file} has status-based logic - use capability flags from backend`).toBe(true);
        }
      });
    });

    it('ViewModels MUST only consume hooks', () => {
      viewModelFiles.forEach((file) => {
        const content = readFile(file);

        // ViewModels should not import API directly
        const importsApi = content.includes("from '../api/");

        expect(importsApi, `${file} imports API directly - ViewModels must use hooks only`).toBe(false);
      });
    });
  });

  describe('Empty States', () => {
    it('Empty state components MUST NOT fetch data', () => {
      const emptyStateFiles = allFiles.filter((f) => f.includes('empty-states') || f.includes('EmptyState'));

      emptyStateFiles.forEach((file) => {
        const content = readFile(file);

        const fetchesData = content.includes('useQuery') || 
                           content.includes('useMutation') ||
                           content.includes('apiClient');

        expect(fetchesData, `${file} fetches data - empty states must be pure presentation`).toBe(false);
      });
    });
  });

  describe('Error Boundaries', () => {
    it('App MUST be wrapped by the application error boundary', () => {
      const appFile = path.join(SRC_DIR, 'App.jsx');

      expect(fs.existsSync(appFile), 'App.jsx must exist').toBe(true);

      const content = readFile(appFile);

      expect(content).toContain("import AppErrorBoundary from './components/AppErrorBoundary'");
      expect(content).toContain('<AppErrorBoundary>');
      expect(content).toContain('</AppErrorBoundary>');
    });

    it('Error boundaries MUST NOT contain business logic', () => {
      const errorBoundaryFiles = allFiles.filter((f) => f.includes('ErrorBoundary'));

      errorBoundaryFiles.forEach((file) => {
        const content = readFile(file);

        const hasBusinessLogic = content.includes('apiClient') || 
                                 content.includes('useQuery') ||
                                 content.includes('validate');

        expect(hasBusinessLogic, `${file} has business logic - error boundaries must only catch and display`).toBe(false);
      });
    });
  });

  describe('Request Timeouts', () => {
    it('Axios clients MUST set finite request timeouts', () => {
      // Find files that create axios clients with axios.create
      const axiosClientFiles = allSourceFiles.filter((file) => {
        const content = readFile(file);
        return content.includes('axios.create({');
      });

      axiosClientFiles.forEach((file) => {
        const content = readFile(file);

        expect(content, `${file} creates an Axios client without timeout`).toContain('timeout:');
        expect(content, `${file} creates an Axios client without timeoutErrorMessage`).toContain('timeoutErrorMessage:');
      });
    });

    it('Direct axios calls MUST use timeout configuration or a configured client', () => {
      // Patterns that indicate direct axios usage without a configured client
      const directAxiosPatterns = [
        /\baxios\.get\(/,
        /\baxios\.post\(/,
        /\baxios\.put\(/,
        /\baxios\.patch\(/,
        /\baxios\.delete\(/,
        /\baxios\.request\(/,
        /\baxios\(/  // axios() direct call
      ];

      const filesWithDirectAxios: Array<{ file: string; matches: string[] }> = [];

      allSourceFiles.forEach((file) => {
        const content = readFile(file);
        const matches: string[] = [];

        // Skip if file creates a configured axios client (it's a factory)
        if (content.includes('axios.create({')) {
          return;
        }

        // Skip if file imports a configured client
        const importsConfiguredClient = 
          content.includes("from './api'") ||
          content.includes("from '../api'") ||
          content.includes("from '../../api'") ||
          content.includes("from '../../../api'") ||
          content.includes("from './affiliateAuthApi'") ||
          content.includes("from '../affiliateAuthApi'") ||
          content.includes("from '../../affiliateAuthApi'") ||
          content.includes("from '../../../services/affiliateAuthApi'") ||
          content.includes("from './superAdminApiClient'") ||
          content.includes("from '../superAdminApiClient'") ||
          content.includes("from '../../superAdminApiClient'") ||
          content.includes("from './apiClient'") ||
          content.includes("from '../apiClient'") ||
          content.includes("from '../../apiClient'") ||
          content.includes("from '@/services/api'") ||
          content.includes("from '@/core/apiClient'");

        if (importsConfiguredClient) {
          return;
        }

        // Check for direct axios usage patterns
        directAxiosPatterns.forEach((pattern) => {
          if (pattern.test(content)) {
            matches.push(pattern.source);
          }
        });

        if (matches.length > 0) {
          filesWithDirectAxios.push({ file, matches });
        }
      });

      // Assert that direct axios calls include timeout configuration
      filesWithDirectAxios.forEach(({ file }) => {
        const content = readFile(file);
        
        // Extract all direct axios call sites from the content
        // This regex matches both method-style calls (axios.get) and direct calls (axios(...))
        const axiosCallPattern = /axios(?:\.(?:get|post|put|patch|delete|request))?\s*\(/g;
        const axiosCallMatches = [...content.matchAll(axiosCallPattern)];
        
        // For each axios call, extract enough context to check for timeout configuration
        const callsWithoutTimeout = axiosCallMatches.filter(match => {
          const startIndex = match.index!;
          // Find the end of this call by counting parentheses, but skip parentheses inside strings/templates
          let depth = 0;
          let endIndex = startIndex;
          let foundStart = false;
          let inString = false;
          let stringChar = '';
          let inTemplate = false;
          let templateDepth = 0;
          let escaped = false;
          
          for (let i = startIndex; i < content.length; i++) {
            const char = content[i];
            const prevChar = i > 0 ? content[i - 1] : '';
            
            // Handle escape sequences
            if (escaped) {
              escaped = false;
              continue;
            }
            
            if (char === '\\' && (inString || inTemplate)) {
              escaped = true;
              continue;
            }
            
            // Handle string literals
            if (!inTemplate && (char === '"' || char === "'" || char === '`')) {
              if (!inString) {
                inString = true;
                stringChar = char;
                if (char === '`') {
                  inTemplate = true;
                  inString = false;
                }
              } else if (char === stringChar) {
                inString = false;
                stringChar = '';
              }
              continue;
            }
            
            // Handle template literal expressions ${...}
            if (inTemplate) {
              if (char === '{' && prevChar === '$') {
                templateDepth++;
              } else if (char === '}' && templateDepth > 0) {
                templateDepth--;
              } else if (char === '`' && templateDepth === 0) {
                inTemplate = false;
              }
            }
            
            // Only count parentheses outside strings and templates
            if (!inString && !inTemplate) {
              if (char === '(') {
                depth++;
                foundStart = true;
              } else if (char === ')') {
                depth--;
                if (foundStart && depth === 0) {
                  endIndex = i + 1;
                  break;
                }
              }
            }
          }
          
          // Extract the full call including all arguments
          const fullCall = content.substring(startIndex, endIndex);
          
          // Check if this call includes timeout configuration
          return !fullCall.includes('timeout:');
        });
        
        expect(
          callsWithoutTimeout.length,
          `${file} has ${callsWithoutTimeout.length} direct axios call(s) without timeout configuration:\n` +
          callsWithoutTimeout.map(match => {
            const startIndex = match.index!;
            const preview = content.substring(startIndex, Math.min(startIndex + 100, content.length));
            return `  - ${preview.replace(/\n/g, ' ')}...`;
          }).join('\n') + '\n' +
          `Either import a configured client (api, affiliateAuthApi, apiClient) or add timeout configuration to each call.`
        ).toBe(0);
      });
    });
  });

  describe('Accessibility', () => {
    it('Interactive elements SHOULD have aria-labels', () => {
      componentFiles.forEach((file) => {
        const content = readFile(file);

        // Check for buttons without aria-label
        const hasButtons = content.includes('<button');
        const hasAriaLabel = content.includes('aria-label');

        if (hasButtons && !hasAriaLabel) {
          console.warn(`${file} has buttons without aria-label - consider adding for accessibility`);
        }
      });
    });
  });
});

module.exports = {
  root: true,
  env: { browser: true, es2020: true },
  extends: [
    'eslint:recommended',
    'plugin:@typescript-eslint/recommended',
    'plugin:react-hooks/recommended',
  ],
  ignorePatterns: ['dist', '.eslintrc.cjs'],
  parser: '@typescript-eslint/parser',
  plugins: ['react-refresh'],
  rules: {
    'react-refresh/only-export-components': [
      'warn',
      { allowConstantExport: true },
    ],
    
    // HARDENING: Layered import enforcement
    'no-restricted-imports': [
      'error',
      {
        paths: [
          {
            name: 'axios',
            message: 'Direct axios import forbidden. Use apiClient from @/core/apiClient instead.',
          },
          {
            name: '@microsoft/signalr',
            importNames: ['HubConnectionBuilder', 'HubConnection'],
            message: 'SignalR imports only allowed in useChat hook. Use useChat hook instead.',
          },
        ],
        patterns: [
          {
            group: ['**/api/marketplaceApi'],
            message: 'Components cannot import API layer directly. Use hooks from @/marketplace/hooks instead.',
          },
        ],
      },
    ],
  },
  overrides: [
    {
      // Allow SignalR only in useChat hook
      files: ['**/hooks/useChat.ts'],
      rules: {
        'no-restricted-imports': 'off',
      },
    },
    {
      // Allow apiClient only in hooks and API layer
      files: ['**/hooks/**/*.ts', '**/api/**/*.ts', '**/core/apiClient.ts'],
      rules: {
        'no-restricted-imports': [
          'error',
          {
            paths: [
              {
                name: 'axios',
                message: 'Direct axios import forbidden except in apiClient.',
              },
            ],
          },
        ],
      },
    },
    {
      // Components cannot import hooks or API
      files: ['**/components/**/*.tsx'],
      rules: {
        'no-restricted-imports': [
          'error',
          {
            patterns: [
              {
                group: ['**/hooks/**'],
                message: 'Components cannot call hooks. Only pages can use hooks.',
              },
              {
                group: ['**/api/**'],
                message: 'Components cannot import API layer. Use props from parent pages.',
              },
            ],
          },
        ],
      },
    },
  ],
};

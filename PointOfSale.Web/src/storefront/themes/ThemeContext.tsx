import React, { createContext, useContext } from 'react'
import { selectConfig, useStoreConfigStore } from '@/store/storeConfigStore'

interface ThemeContextValue {
  themeId: string
}

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined)

export const ThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const config = useStoreConfigStore(selectConfig)
  const themeId = config.storefrontTheme || config.themeId || 'modern'

  return <ThemeContext.Provider value={{ themeId }}>{children}</ThemeContext.Provider>
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext)
  if (!context) {
    throw new Error('useTheme must be used within a ThemeProvider.')
  }
  return context
}

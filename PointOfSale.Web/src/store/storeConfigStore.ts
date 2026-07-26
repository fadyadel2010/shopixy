import { create } from 'zustand'
import { DEFAULT_STORE_CONFIG, type StoreConfig } from '@/types/storeConfig'

interface StoreConfigState {
  config: StoreConfig
  isLoading: boolean
  isLoaded: boolean
  error: string | null
  setConfig: (config: StoreConfig) => void
  setLoading: (loading: boolean) => void
  setError: (error: string | null) => void
  reset: () => void
}

export const useStoreConfigStore = create<StoreConfigState>((set) => ({
  config: DEFAULT_STORE_CONFIG,
  isLoading: true,
  isLoaded: false,
  error: null,
  setConfig: (config) => set({ config, isLoading: false, isLoaded: true, error: null }),
  setLoading: (isLoading) => set({ isLoading }),
  setError: (error) => set({ error, isLoading: false }),
  reset: () => set({ config: DEFAULT_STORE_CONFIG, isLoading: true, isLoaded: false, error: null }),
}))

export const selectConfig = (state: StoreConfigState) => state.config

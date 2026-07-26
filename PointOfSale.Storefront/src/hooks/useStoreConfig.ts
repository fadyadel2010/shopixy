import { useState, useEffect } from 'react';
import { StoreConfig, DEFAULT_STORE_CONFIG } from '../types/storeConfig';
import { fetchStoreConfig } from '../services/api';

export const useStoreConfig = () => {
    const [config, setConfig] = useState<StoreConfig>(DEFAULT_STORE_CONFIG);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const loadConfig = async () => {
            try {
                // Fetch config using the API service
                const data = await fetchStoreConfig();
                setConfig(data);
            } catch (err: any) {
                console.error('Failed to load store config', err);
                setError(err.message || 'Failed to load configuration');
                // Fallback to default config on error
                setConfig(DEFAULT_STORE_CONFIG);
            } finally {
                setLoading(false);
            }
        };

        loadConfig();
    }, []);

    return { config, loading, error };
};

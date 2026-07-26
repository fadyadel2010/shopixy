// Script to clear all WhatsApp auth states from Redis
import { createClient } from 'redis';
import { config } from './config';

async function clearAuthStates() {
    const redis = createClient({
        socket: {
            host: config.redis.host,
            port: config.redis.port
        },
        password: config.redis.password,
        database: config.redis.db
    });

    await redis.connect();
    console.log('Connected to Redis');

    // Find all WhatsApp auth keys
    const authKeys = await redis.keys('whatsapp:auth:*');
    const sessionKeys = await redis.keys('whatsapp:session:*');

    console.log('Found auth keys:', authKeys);
    console.log('Found session keys:', sessionKeys);

    // Delete all auth states
    for (const key of authKeys) {
        await redis.del(key);
        console.log('Deleted:', key);
    }

    // Delete all session metadata
    for (const key of sessionKeys) {
        await redis.del(key);
        console.log('Deleted:', key);
    }

    console.log('All WhatsApp auth states and sessions cleared!');
    await redis.quit();
}

clearAuthStates().catch(console.error);

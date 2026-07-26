import pino from 'pino';
import { config } from '../config';

/**
 * Create logger instance
 */
export const logger = pino({
  level: config.logging.level,
  transport: config.nodeEnv === 'development' ? {
    target: 'pino-pretty',
    options: {
      colorize: true,
      translateTime: 'SYS:standard',
      ignore: 'pid,hostname'
    }
  } : undefined
});

/**
 * Create child logger with context
 */
export function createLogger(context: string) {
  return logger.child({ context });
}

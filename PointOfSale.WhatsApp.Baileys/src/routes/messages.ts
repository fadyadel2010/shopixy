/**
 * Message Sending Routes
 * 
 * Handles HTTP endpoints for WhatsApp message sending:
 * - POST /messages/send - Send a message
 */

import { Router, Request, Response } from 'express';
import { MessageSender } from '../services/MessageSender';
import { MessageType, MessageSendResponse } from '../models/Message';
import { createLogger } from '../utils/logger';

const logger = createLogger('MessageRoutes');

export function createMessageRoutes(messageSender: MessageSender): Router {
  const router = Router();

  /**
   * POST /messages/send
   * Send a WhatsApp message
   */
  router.post('/send', async (req: Request, res: Response): Promise<void> => {
    const { tenantId, phone, text, type, storeName } = req.body;

    try {
      logger.info({ tenantId, phone, type }, 'Send message request');

      // Validate required fields
      if (!tenantId || tenantId.trim() === '') {
        res.status(400).json({
          success: false,
          error: 'Tenant ID is required'
        } as MessageSendResponse);
        return;
      }

      if (!phone || phone.trim() === '') {
        res.status(400).json({
          success: false,
          error: 'Phone number is required'
        } as MessageSendResponse);
        return;
      }

      if (!text || text.trim() === '') {
        res.status(400).json({
          success: false,
          error: 'Message text is required'
        } as MessageSendResponse);
        return;
      }

      // Validate message type
      const messageType = type || MessageType.TEXT;
      if (!Object.values(MessageType).includes(messageType)) {
        res.status(400).json({
          success: false,
          error: `Invalid message type. Must be one of: ${Object.values(MessageType).join(', ')}`
        } as MessageSendResponse);
        return;
      }

      // Send message with 5 second timeout
      const result = await messageSender.sendMessage({
        tenantId,
        phone,
        text,
        type: messageType,
        timeout: 5000,
        storeName: storeName || undefined
      });

      // Return appropriate status code
      const statusCode = result.success ? 200 : 400;
      res.status(statusCode).json(result);
    } catch (error) {
      logger.error({ err: error, tenantId, phone }, 'Failed to send message');
      res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Failed to send message'
      } as MessageSendResponse);
    }
  });

  return router;
}

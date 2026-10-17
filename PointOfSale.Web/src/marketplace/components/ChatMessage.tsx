import { useTranslation } from 'react-i18next';
import type { ChatMessage as ChatMessageType } from '../types';
import type { PresentationFormatting } from '../presentationFormatting';
import { buildAttachmentDownloadPath } from '../attachmentAuthority';

interface ChatMessageProps extends Pick<PresentationFormatting, 'dir' | 'formatTime'> {
  message: ChatMessageType;
  isOwnMessage: boolean;
}

/**
 * ChatMessage - Professional chat message bubble with attachment support.
 * Features:
 * - Backend-authorized attachment download
 * - Sender avatars
 * - Timestamps
 * - System messages
 */
export function ChatMessage({ message, isOwnMessage, dir, formatTime }: ChatMessageProps) {
  const { t } = useTranslation();
  const isSystem = message.messageType === 'System';
  const attachmentHref = message.attachmentId
    ? buildAttachmentDownloadPath(message.attachmentId)
    : null;
  const senderLabel = message.senderRole === 'Buyer'
    ? t('servicesMarketplace.chat.roleBuyer')
    : t('servicesMarketplace.chat.roleProvider');
  const senderInitial = senderLabel.charAt(0) || (message.senderRole === 'Buyer' ? 'B' : 'P');
  const attachmentName = message.attachmentFileName || '';

  if (isSystem) {
    return (
      <div className="flex justify-center my-3" dir={dir}>
        <div className="px-4 py-2 bg-gray-100 text-gray-600 text-xs rounded-full italic flex items-center gap-2">
          <svg className="w-3.5 h-3.5" fill="currentColor" viewBox="0 0 20 20">
            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
          </svg>
          {message.body}
        </div>
      </div>
    );
  }

  return (
    <div className={`flex gap-3 mb-4 ${isOwnMessage ? 'flex-row-reverse' : 'flex-row'}`} dir={dir}>
      <div className={`flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-white text-sm font-semibold ${
        isOwnMessage ? 'bg-indigo-600' : 'bg-gray-400'
      }`}>
        {senderInitial}
      </div>

      <div className={`flex flex-col max-w-[70%] ${isOwnMessage ? 'items-end' : 'items-start'}`}>
        <div className={`flex items-center gap-2 mb-1 text-xs ${isOwnMessage ? 'flex-row-reverse' : 'flex-row'}`}>
          <span className="font-medium text-gray-700">
            {senderLabel}
          </span>
          <span className="text-gray-400">
            {formatTime(message.createdAt, { hour: '2-digit', minute: '2-digit' })}
          </span>
        </div>

        <div
          className={`px-4 py-2.5 rounded-2xl shadow-sm ${
            isOwnMessage
              ? 'bg-indigo-600 text-white rounded-br-md'
              : 'bg-white text-gray-900 border border-gray-200 rounded-bl-md'
          }`}
        >
          {message.body && (
            <p className="text-sm leading-relaxed whitespace-pre-line break-words">
              {message.body}
            </p>
          )}

          {attachmentHref && attachmentName && (
            <div className={`${message.body ? 'mt-2' : ''}`}>
              <a
                href={attachmentHref}
                target="_blank"
                rel="noopener noreferrer"
                className={`flex items-center gap-2 px-3 py-2 rounded-lg transition-colors ${
                  isOwnMessage
                    ? 'bg-indigo-700 hover:bg-indigo-800'
                    : 'bg-gray-50 hover:bg-gray-100'
                }`}
              >
                <svg className="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                </svg>
                <span className="text-sm font-medium truncate">{attachmentName}</span>
              </a>
            </div>
          )}
        </div>

        {isOwnMessage && (
          <div className="flex items-center gap-1 mt-1 text-xs text-gray-400">
            <svg className="w-3.5 h-3.5" fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" />
            </svg>
            <span>{t('servicesMarketplace.chat.sent')}</span>
          </div>
        )}
      </div>
    </div>
  );
}

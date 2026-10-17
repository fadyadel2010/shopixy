import type { HttpRequestOptions } from '../../../../core/httpContracts';
import type { ApiJson } from '../../../../services/apiJson';
export interface DomainCommand { hostName?: string; domainType?: string | number; canonicalPolicy?: string | number; redirectType?: string | number; redirectTargetDomainId?: string | null; expectedVersion?: number; verificationMethod?: string | number; reason?: string; operationId?: string; [key: string]: unknown }
type Command = (domainId: string, request?: DomainCommand, options?: HttpRequestOptions) => Promise<ApiJson>;
export const domainManagementService: {
    getDashboardSummary(options?: HttpRequestOptions): Promise<{ totalDomains: number; [key: string]: ApiJson }>;
    getDomainList(params?: Record<string, unknown>, options?: HttpRequestOptions): Promise<ApiJson>;
    getDomainDetail(id: string, options?: HttpRequestOptions): Promise<ApiJson>; getDomainVerification(id: string, options?: HttpRequestOptions): Promise<ApiJson>;
    getDomainSsl(id: string, options?: HttpRequestOptions): Promise<ApiJson>; getDomainSslCertificate(id: string, options?: HttpRequestOptions): Promise<ApiJson>;
    getDomainSslOperations(id: string, params?: Record<string, unknown>, options?: HttpRequestOptions): Promise<ApiJson>;
    getDomainSslOperation(id: string, operationId: string, options?: HttpRequestOptions): Promise<ApiJson>;
    getDomainRedirects(id: string, options?: HttpRequestOptions): Promise<ApiJson>; getDomainHistory(id: string, params?: Record<string, unknown>, options?: HttpRequestOptions): Promise<ApiJson>;
    getActivitySummary(params?: Record<string, unknown>, options?: HttpRequestOptions): Promise<ApiJson>;
    createDomain(request: DomainCommand, options?: HttpRequestOptions): Promise<ApiJson>; updateDomain: Command; startDomainVerification: Command; verifyDomainNow: Command; retryDomainVerification: Command; refreshVerificationToken: Command;
    provisionSslCertificate: Command; activateSslCertificate: Command; renewSslCertificate: Command; revokeSslCertificate: Command; retrySslOperation: Command; refreshSslCertificate: Command;
    archiveDomain: Command; restoreDomain: Command; deleteDomain: Command; promotePrimaryDomain: Command;
};
export default domainManagementService;

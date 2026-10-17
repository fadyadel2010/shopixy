import type { ApiJson } from '../../../../services/apiJson';
import type { DomainCommand, domainManagementService } from '../services/domainManagementService';
type Options = { suppressError?: boolean };
type Command = (id: string, request?: DomainCommand, options?: Options) => Promise<ApiJson>;
export interface DomainMutationState { isMutating: boolean; error: string | null; mutationError: string | null; errorCode: string | null; isConcurrencyConflict: boolean; serverVersion: number | null; lastMutation: string | null; mutationStatus: string }
export function useDomainMutations(options?: { mutationOptions?: { onSuccess?(value: ApiJson, operationName: string): void; onError?(error: unknown, operationName: string): void } }): DomainMutationState & {
    createDomain(request: DomainCommand, options?: Options): ReturnType<typeof domainManagementService.createDomain>; updateDomain: Command; startDomainVerification: Command; verifyDomainNow: Command; retryDomainVerification: Command; refreshVerificationToken: Command;
    provisionSslCertificate: Command; activateSslCertificate: Command; renewSslCertificate: Command; revokeSslCertificate: Command; retrySslOperation: Command; refreshSslCertificate: Command;
    archiveDomain: Command; restoreDomain: Command; deleteDomain: Command; promotePrimaryDomain: Command; clearError(): void;
};
export default useDomainMutations;

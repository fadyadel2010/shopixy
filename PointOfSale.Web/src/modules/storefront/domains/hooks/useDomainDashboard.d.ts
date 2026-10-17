import type { domainManagementService } from '../services/domainManagementService';
import type { DomainLoadState } from './useDomainLoadState';
export interface DomainReadState<T> { state: DomainLoadState; data: T | null; error: string | null; isLoading: boolean; isRefreshing: boolean; isEmpty: boolean; isError: boolean; isUnauthorized: boolean; isNotFound: boolean; isSuccess: boolean; refetch(): Promise<void>; refresh(): Promise<void> }
export function useDomainDashboard(): DomainReadState<Awaited<ReturnType<typeof domainManagementService.getDashboardSummary>>>;
export default useDomainDashboard;

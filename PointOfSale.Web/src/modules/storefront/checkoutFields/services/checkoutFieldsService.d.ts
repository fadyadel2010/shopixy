import type { HttpRequestOptions } from '../../../../core/httpContracts';
import type { ApiJson } from '../../../../services/apiJson';
export const checkoutFieldsService: {
    getFields(options?: HttpRequestOptions): Promise<ApiJson>; initializeSystemFields(options?: HttpRequestOptions): Promise<ApiJson>;
    createField(request: unknown, options?: HttpRequestOptions): Promise<ApiJson>; updateField(id: string, request: unknown, options?: HttpRequestOptions): Promise<ApiJson>;
    deleteField(id: string, options?: HttpRequestOptions): Promise<void>; reorderFields(fields: unknown[], options?: HttpRequestOptions): Promise<ApiJson>;
};
export default checkoutFieldsService;

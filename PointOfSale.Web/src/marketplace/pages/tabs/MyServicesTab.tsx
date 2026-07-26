import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Plus, BarChart3 } from 'lucide-react'
import { marketplaceApi } from '../../api/marketplaceApi'
import { marketplaceKeys } from '../../queryKeys'
import { ServiceFormModal } from '../../components/ServiceFormModal'
import { useFormatting } from '../../../hooks/useFormatting'
import type { CreateServiceRequest } from '../../types'
import {
    marketplaceAuthorityQueryOptions,
    publishMarketplaceAuthorityInvalidation,
    useMarketplaceAuthorityScope,
} from '../../authority'
import { deriveProviderServicePresentation } from '../../providerServiceAuthority'

export function MyServicesTab() {
    const { t } = useTranslation()
    const { formatCurrency, isRTL } = useFormatting()
    const navigate = useNavigate()
    const queryClient = useQueryClient()
    const authorityScope = useMarketplaceAuthorityScope()
    const [showCreateModal, setShowCreateModal] = useState(false)
    const [editingService, setEditingService] = useState<any>(null)

    const { data: serviceData = [], isLoading, isFetching, isError } = useQuery({
        queryKey: marketplaceKeys.myServices(authorityScope),
        queryFn: marketplaceApi.getMyServices,
        ...marketplaceAuthorityQueryOptions,
    })
    const services = isError || isFetching
        ? []
        : serviceData
            .map(deriveProviderServicePresentation)
            .filter((service): service is NonNullable<ReturnType<typeof deriveProviderServicePresentation>> => service !== null)

    const createServiceMutation = useMutation({
        mutationFn: (data: CreateServiceRequest) => marketplaceApi.createService(data),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
            setShowCreateModal(false)
        },
        onSettled: () => {
            publishMarketplaceAuthorityInvalidation(authorityScope)
            queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
    })

    const updateServiceMutation = useMutation({
        mutationFn: ({ id, data }: { id: string; data: CreateServiceRequest }) =>
            marketplaceApi.updateService(id, data),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
            setEditingService(null)
        },
        onSettled: () => {
            publishMarketplaceAuthorityInvalidation(authorityScope)
            queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
        },
    })

    const handleSubmitCreate = async (data: CreateServiceRequest) => {
        await createServiceMutation.mutateAsync(data)
    }

    const handleSubmitEdit = async (data: CreateServiceRequest) => {
        if (editingService) {
            await updateServiceMutation.mutateAsync({ id: editingService.serviceId, data })
        }
    }

    if (isLoading || isFetching) {
        return (
            <div className="flex justify-center py-12">
                <div className="text-gray-500">{t('servicesMarketplace.myServices.loading')}</div>
            </div>
        )
    }

    return (
        <div className="space-y-6" dir={isRTL ? 'rtl' : 'ltr'}>
            <div className="flex items-center justify-between">
                <div className={isRTL ? 'text-right' : 'text-left'}>
                    <h2 className="text-xl font-bold text-gray-900">
                        {t('servicesMarketplace.myServices.title')}
                    </h2>
                    <p className="text-sm text-gray-500 mt-1">
                        {t('servicesMarketplace.myServices.subtitle')}
                    </p>
                </div>
                <button className="btn-primary inline-flex items-center gap-2" onClick={() => setShowCreateModal(true)}>
                    <Plus className="h-4 w-4" />
                    {t('servicesMarketplace.myServices.create')}
                </button>
            </div>

            {services.length > 0 ? (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                    {services.map((service) => {
                        return (
                            <div key={service.serviceId} className="card hover:shadow-lg transition-shadow">
                                <div className="relative h-40 bg-gradient-to-br from-indigo-500 to-purple-600 rounded-lg mb-4 overflow-hidden">
                                    {service.primaryImage ? (
                                        <img
                                            src={service.primaryImage}
                                            alt={service.title}
                                            className="w-full h-full object-cover"
                                        />
                                    ) : (
                                        <div className="w-full h-full flex items-center justify-center text-white text-5xl">
                                            <span aria-hidden="true">+</span>
                                        </div>
                                    )}

                                    <div className={`absolute top-3 ${isRTL ? 'left-3' : 'right-3'}`}>
                                        <span
                                            className={`px-3 py-1 text-xs font-semibold rounded-full ${
                                                service.isActive
                                                    ? 'bg-green-100 text-green-800 border border-green-200'
                                                    : 'bg-gray-100 text-gray-800 border border-gray-200'
                                            }`}
                                        >
                                            {service.isActive ? t('common.active') : t('common.inactive')}
                                        </span>
                                    </div>
                                </div>

                                <h3 className="text-lg font-semibold text-gray-900 mb-2 line-clamp-1">
                                    {service.title}
                                </h3>
                                <p className="text-sm text-gray-600 mb-4 line-clamp-2">
                                    {service.description}
                                </p>

                                {service.startingPrice != null ? (
                                    <div className="mb-4 pb-4 border-b border-gray-100">
                                        <div className="text-xs text-gray-500">
                                            {t('servicesMarketplace.serviceCard.startingAt')}
                                        </div>
                                        <div className="text-xl font-bold text-indigo-600">
                                            {formatCurrency(service.startingPrice, service.currency || undefined)}
                                            {service.pricingType === 'Hourly' ? (
                                                <span className="text-sm font-normal">
                                                    {' '}
                                                    {t('servicesMarketplace.common.perHour')}
                                                </span>
                                            ) : null}
                                        </div>
                                    </div>
                                ) : null}

                                <div className="flex gap-2">
                                    <button
                                        className="btn-secondary flex-1 text-sm"
                                        onClick={() => setEditingService(service)}
                                    >
                                        {t('common.edit')}
                                    </button>
                                    <button
                                        className="btn-secondary text-sm inline-flex items-center gap-1"
                                        onClick={() => navigate(`/marketplace/service/${service.serviceId}`)}
                                    >
                                        <BarChart3 className="h-4 w-4" />
                                        {t('common.view')}
                                    </button>
                                </div>
                            </div>
                        )
                    })}
                </div>
            ) : (
                <div className="card text-center py-12">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">
                        {t('servicesMarketplace.myServices.emptyTitle')}
                    </h3>
                    <p className="text-gray-500 mb-6">
                        {t('servicesMarketplace.myServices.emptyDescription')}
                    </p>
                    <button className="btn-primary" onClick={() => setShowCreateModal(true)}>
                        {t('servicesMarketplace.myServices.createFirst')}
                    </button>
                </div>
            )}

            {showCreateModal ? (
                <ServiceFormModal
                    onSubmit={handleSubmitCreate}
                    onClose={() => setShowCreateModal(false)}
                />
            ) : null}

            {editingService ? (
                <ServiceFormModal
                    serviceId={editingService.serviceId}
                    initialData={editingService.source}
                    onSubmit={handleSubmitEdit}
                    onClose={() => setEditingService(null)}
                />
            ) : null}
        </div>
    )
}

import { useReducer } from 'react'
import type {
    PaymentProviderAdministrationState,
    PaymentProviderActionResult,
    PaymentProviderHealth,
    PaymentProviderConfiguration,
    PaymentProviderSummary,
} from './paymentProviderTypes'

type PaymentProviderAdministrationAction =
    | { type: 'load:start' }
    | { type: 'load:success'; providers: PaymentProviderSummary[] }
    | { type: 'load:error'; errors: string[] }
    | { type: 'select'; providerCode: string }
    | { type: 'configuration:success'; configuration: PaymentProviderConfiguration; errors?: string[] }
    | { type: 'configuration:update'; configuration: PaymentProviderConfiguration }
    | { type: 'configuration:reset' }
    | { type: 'validation:set'; errors: string[] }
    | { type: 'dirty:set'; dirty: boolean }
    | { type: 'save:start' }
    | { type: 'save:success'; configuration: PaymentProviderConfiguration; errors?: string[] }
    | { type: 'save:error'; errors: string[] }
    | { type: 'test:start' }
    | { type: 'test:success'; result: PaymentProviderActionResult; health: PaymentProviderHealth }
    | { type: 'test:error'; errors: string[] }

const initialState: PaymentProviderAdministrationState = {
    loading: false,
    saving: false,
    runningTest: false,
    refreshing: false,
    latestResult: null,
    historicalResult: null,
    dirty: false,
    successMessage: null,
    failureMessage: null,
    selectedProviderCode: null,
    providerList: [],
    configuration: null,
    originalConfiguration: null,
    validationErrors: [],
}

function reducer(
    state: PaymentProviderAdministrationState,
    action: PaymentProviderAdministrationAction,
): PaymentProviderAdministrationState {
    switch (action.type) {
        case 'load:start':
            return { ...state, loading: true, validationErrors: [], successMessage: null, failureMessage: null }
        case 'load:success':
            return {
                ...state,
                loading: false,
                providerList: action.providers,
                selectedProviderCode: state.selectedProviderCode ?? action.providers[0]?.providerCode ?? null,
                validationErrors: [],
                successMessage: null,
                failureMessage: null,
            }
        case 'load:error':
            return { ...state, loading: false, validationErrors: action.errors, successMessage: null, failureMessage: action.errors[0] ?? null }
        case 'select':
            return {
                ...state,
                selectedProviderCode: action.providerCode,
                configuration: null,
                originalConfiguration: null,
                dirty: false,
                runningTest: false,
                refreshing: false,
                latestResult: null,
                historicalResult: null,
                validationErrors: [],
                successMessage: null,
                failureMessage: null,
            }
        case 'configuration:success':
            return {
                ...state,
                configuration: action.configuration,
                originalConfiguration: action.configuration,
                latestResult: action.configuration.health?.latestResult ?? null,
                historicalResult: action.configuration.health?.previousResult ?? null,
                dirty: false,
                validationErrors: action.errors ?? [],
                successMessage: null,
                failureMessage: null,
            }
        case 'configuration:update':
            return {
                ...state,
                configuration: action.configuration,
                dirty: true,
                successMessage: null,
                failureMessage: null,
            }
        case 'configuration:reset':
            return {
                ...state,
                configuration: state.originalConfiguration,
                dirty: false,
                validationErrors: [],
                successMessage: null,
                failureMessage: null,
            }
        case 'validation:set':
            return { ...state, validationErrors: action.errors }
        case 'dirty:set':
            return { ...state, dirty: action.dirty, successMessage: action.dirty ? null : state.successMessage }
        case 'save:start':
            return { ...state, saving: true, validationErrors: [], successMessage: null, failureMessage: null }
        case 'save:success':
            return {
                ...state,
                saving: false,
                configuration: action.configuration,
                originalConfiguration: action.configuration,
                dirty: false,
                providerList: state.providerList.map((provider) =>
                    provider.providerCode === action.configuration.providerCode ? action.configuration : provider,
                ),
                validationErrors: action.errors ?? [],
                successMessage: 'Payment provider configuration saved.',
                failureMessage: null,
            }
        case 'save:error':
            return { ...state, saving: false, validationErrors: action.errors, successMessage: null, failureMessage: action.errors[0] ?? null }
        case 'test:start':
            return { ...state, runningTest: true, refreshing: true, successMessage: null, failureMessage: null }
        case 'test:success':
            return {
                ...state,
                runningTest: false,
                refreshing: false,
                latestResult: action.result,
                historicalResult: state.latestResult,
                configuration: state.configuration
                    ? {
                        ...state.configuration,
                        status: action.health.status,
                        health: action.health,
                    }
                    : state.configuration,
                providerList: state.providerList.map((provider) =>
                    provider.providerCode === action.result.provider
                        ? {
                            ...provider,
                            status: action.health.status,
                            health: action.health,
                        }
                        : provider,
                ),
                successMessage: action.result.success ? 'Payment provider connection verified.' : null,
                failureMessage: action.result.success ? null : action.result.message,
            }
        case 'test:error':
            return {
                ...state,
                runningTest: false,
                refreshing: false,
                validationErrors: action.errors,
                successMessage: null,
                failureMessage: action.errors[0] ?? null,
            }
        default:
            return state
    }
}

export function usePaymentProviderAdministrationState() {
    return useReducer(reducer, initialState)
}

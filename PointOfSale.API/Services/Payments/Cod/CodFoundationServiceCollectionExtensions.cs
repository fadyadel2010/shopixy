using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;

namespace PointOfSale.API.Services.Payments.Cod;

public static class CodFoundationServiceCollectionExtensions
{
    public const string RegistrationName = "cod_reference_workflow_foundation";
    public const string CollectionEvidenceRegistrationName = "cod_collection_evidence_foundation";
    public const string RuntimeAdmissionRegistrationName = "cod_runtime_admission_foundation";
    public const string RuntimeAdmissionApplicationRegistrationName = "cod_runtime_admission_application_contract";
    public const string RuntimeAdmissionBridgeRegistrationName = "cod_runtime_admission_bridge";
    public const string AdmissionLifecycleRegistrationName = "cod_admission_lifecycle";
    public const string WorkflowOrchestratorRegistrationName = "cod_workflow_orchestrator";

    public static IServiceCollection AddCodReferenceWorkflowFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ICodReferenceWorkflowHealthContributor)))
        {
            throw new InvalidOperationException("COD Reference Workflow foundation duplicate registration is not allowed.");
        }

        services.AddOptions<CodReferenceWorkflowOptions>()
            .Bind(configuration.GetSection(CodReferenceWorkflowOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CodReferenceWorkflowOptions>, CodReferenceWorkflowOptionsValidator>();
        services.TryAddSingleton<ICodAuthorityContractCatalog, CodAuthorityContractCatalog>();
        services.TryAddSingleton<CodReferenceWorkflowTelemetryDefinitions>();
        services.AddSingleton<ICodReferenceWorkflowHealthContributor, CodReferenceWorkflowHealthContributor>();

        return services;
    }

    public static IServiceCollection AddCodCollectionEvidenceFoundation(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ICodCollectionEvidenceCatalog)))
        {
            throw new InvalidOperationException("COD Collection Evidence foundation duplicate registration is not allowed.");
        }

        services.AddSingleton<ICodCollectionEvidenceCatalog, CodCollectionEvidenceCatalog>();

        return services;
    }

    public static IServiceCollection AddCodRuntimeAdmissionFoundation(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ICodRuntimeAdmissionCatalog)))
        {
            throw new InvalidOperationException("COD Runtime Admission foundation duplicate registration is not allowed.");
        }

        services.AddSingleton<ICodRuntimeAdmissionCatalog, CodRuntimeAdmissionCatalog>();

        return services;
    }

    public static IServiceCollection AddCodRuntimeAdmissionApplicationContract(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ICodRuntimeAdmissionApplicationService)))
        {
            throw new InvalidOperationException("COD Runtime Admission application contract duplicate registration is not allowed.");
        }

        services.TryAddSingleton<ICodRuntimeAdmissionCatalog, CodRuntimeAdmissionCatalog>();
        services.AddScoped<ICodRuntimeAdmissionApplicationService, CodRuntimeAdmissionApplicationService>();

        return services;
    }

    public static IServiceCollection AddCodRuntimeAdmissionBridge(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ICodRuntimeAdmissionBridge)))
        {
            throw new InvalidOperationException("COD Runtime Admission Bridge duplicate registration is not allowed.");
        }

        services.AddScoped<ICodRuntimeAdmissionBridge, CodRuntimeAdmissionBridge>();

        return services;
    }

    public static IServiceCollection AddCodAdmissionLifecycle(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ICodAdmissionLifecycle)))
        {
            throw new InvalidOperationException("COD Admission Lifecycle duplicate registration is not allowed.");
        }

        services.AddScoped<ICodAdmissionLifecycle, CodAdmissionLifecycle>();

        return services;
    }

    public static IServiceCollection AddCodWorkflowOrchestrator(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ICodWorkflowOrchestrator)))
        {
            throw new InvalidOperationException("COD Workflow Orchestrator duplicate registration is not allowed.");
        }

        services.AddScoped<ICodWorkflowOrchestrator, CodWorkflowOrchestrator>();

        return services;
    }
}

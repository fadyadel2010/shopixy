using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Data;

/// <summary>
/// Interceptor that enforces marketplace invariants at the EF Core level.
/// Prevents direct manipulation of ServiceRequest.Status and unauthorized ServiceReview creation.
/// </summary>
public class MarketplaceInvariantInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is null)
            return result;

        ValidateMarketplaceInvariants(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            ValidateMarketplaceInvariants(eventData.Context);

        return ValueTask.FromResult(result);
    }

    private static void ValidateMarketplaceInvariants(DbContext context)
    {
        var serviceRequestEntries = context.ChangeTracker.Entries<ServiceRequest>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in serviceRequestEntries)
        {
            ValidateImmutableRequestAuthority(entry);

            // Check if Status was modified directly (not through entity methods)
            if (entry.Property(nameof(ServiceRequest.Status)).IsModified)
            {
                var originalStatus = (ServiceRequestStatus)entry.Property(nameof(ServiceRequest.Status)).OriginalValue!;
                var currentStatus = entry.Entity.Status;

                ValidateStatusTransition(originalStatus, currentStatus);

                // Validate timestamp consistency
                ValidateStatusTimestamps(entry.Entity, originalStatus, currentStatus);
            }

            ValidateLifecycleTimestampMutation(entry);
        }

        var reviewEntries = context.ChangeTracker.Entries<ServiceReview>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

        foreach (var entry in reviewEntries)
        {
            if (entry.State != EntityState.Added)
                throw new InvalidOperationException("ServiceReview rows are immutable after creation");
        }

        var marketingEvidenceEntries = context.ChangeTracker.Entries<MarketingReviewEvidence>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted);

        foreach (var entry in marketingEvidenceEntries)
        {
            throw new InvalidOperationException("MarketingReviewEvidence rows are immutable after creation");
        }
    }

    private static void ValidateStatusTimestamps(ServiceRequest request, ServiceRequestStatus originalStatus, ServiceRequestStatus currentStatus)
    {
        // Ensure timestamps are set correctly for status transitions
        if (currentStatus == ServiceRequestStatus.Accepted && request.AcceptedAt == null)
            throw new InvalidOperationException("AcceptedAt must be set when transitioning to Accepted");

        if (currentStatus == ServiceRequestStatus.CompletedByProvider && request.CompletedByProviderAt == null)
            throw new InvalidOperationException("CompletedByProviderAt must be set when transitioning to CompletedByProvider");

        if (currentStatus == ServiceRequestStatus.CompletedByBuyer && request.CompletedByBuyerAt == null)
            throw new InvalidOperationException("CompletedByBuyerAt must be set when transitioning to CompletedByBuyer");

        if (currentStatus == ServiceRequestStatus.Cancelled && request.CancelledAt == null)
            throw new InvalidOperationException("CancelledAt must be set when transitioning to Cancelled");

        if (currentStatus == ServiceRequestStatus.Rejected && request.RejectedAt == null)
            throw new InvalidOperationException("RejectedAt must be set when transitioning to Rejected");
    }

    private static void ValidateStatusTransition(ServiceRequestStatus originalStatus, ServiceRequestStatus currentStatus)
    {
        var valid = (originalStatus, currentStatus) switch
        {
            (ServiceRequestStatus.Pending, ServiceRequestStatus.Accepted) => true,
            (ServiceRequestStatus.Pending, ServiceRequestStatus.Rejected) => true,
            (ServiceRequestStatus.Pending, ServiceRequestStatus.Cancelled) => true,
            (ServiceRequestStatus.Accepted, ServiceRequestStatus.CompletedByProvider) => true,
            (ServiceRequestStatus.Accepted, ServiceRequestStatus.Cancelled) => true,
            (ServiceRequestStatus.CompletedByProvider, ServiceRequestStatus.CompletedByBuyer) => true,
            _ => false
        };

        if (!valid)
            throw new InvalidOperationException($"Invalid ServiceRequest transition from {originalStatus} to {currentStatus}");
    }

    private static void ValidateImmutableRequestAuthority(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ServiceRequest> entry)
    {
        var immutableProperties = new[]
        {
            nameof(ServiceRequest.TenantId),
            nameof(ServiceRequest.ServiceId),
            nameof(ServiceRequest.BuyerUserId),
            nameof(ServiceRequest.ProviderUserId),
            nameof(ServiceRequest.ProviderProfileId),
            nameof(ServiceRequest.ServiceTitleSnapshot),
            nameof(ServiceRequest.ServiceCategorySnapshot),
            nameof(ServiceRequest.ServiceStartingPriceSnapshot),
            nameof(ServiceRequest.ServicePricingTypeSnapshot),
            nameof(ServiceRequest.ServiceCurrencySnapshot),
            nameof(ServiceRequest.ServicePackagesSnapshot),
            nameof(ServiceRequest.RequestMessage),
            nameof(ServiceRequest.CreatedAt)
        };

        foreach (var propertyName in immutableProperties)
        {
            if (entry.Property(propertyName).IsModified)
                throw new InvalidOperationException($"ServiceRequest {propertyName} is immutable after creation");
        }
    }

    private static void ValidateLifecycleTimestampMutation(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ServiceRequest> entry)
    {
        var statusModified = entry.Property(nameof(ServiceRequest.Status)).IsModified;
        var lifecycleTimestamps = new[]
        {
            nameof(ServiceRequest.AcceptedAt),
            nameof(ServiceRequest.RejectedAt),
            nameof(ServiceRequest.CompletedByProviderAt),
            nameof(ServiceRequest.CompletedByBuyerAt),
            nameof(ServiceRequest.CancelledAt)
        };

        foreach (var propertyName in lifecycleTimestamps)
        {
            if (entry.Property(propertyName).IsModified && !statusModified)
                throw new InvalidOperationException($"ServiceRequest {propertyName} can only change with its SQL lifecycle transition");
        }
    }
}

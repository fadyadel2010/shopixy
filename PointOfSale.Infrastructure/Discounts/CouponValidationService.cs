using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Validation;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Discounts;

public sealed class CouponValidationService : ICouponValidationService
{
    private const string CouponReservationOrderUniqueIndex = "IX_CouponReservations_TenantId_OrderId";
    private const string CouponReservationCustomerUniqueIndex = "IX_CouponReservations_TenantId_CouponId_CustomerId";
    private const string CouponUsageOrderUniqueIndex = "IX_CouponUsages_TenantId_OrderId";

    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CouponValidationService> _logger;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;

    public CouponValidationService(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ILogger<CouponValidationService> logger,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _reliabilityTelemetry = reliabilityTelemetry;
    }

    public async Task<CouponValidationDecision> ValidateCouponByCodeAsync(
        Guid tenantId,
        string couponCode,
        Guid? customerId,
        CouponValidationSnapshot snapshot,
        string correlationId,
        Guid? excludeOrderId = null,
        CancellationToken cancellationToken = default)
    {
        var authenticatedCustomer = RequireAuthenticatedCustomer(customerId);
        if (!authenticatedCustomer.IsAuthenticated)
        {
            return CouponValidationDecision.Failure(
                authenticatedCustomer.ErrorCode!,
                authenticatedCustomer.ErrorMessage!);
        }

        var normalizedCode = NormalizeCouponCode(couponCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return CouponValidationDecision.Failure(
                CouponErrorCodes.Invalid,
                "Coupon code is required.");
        }

        var coupon = await _dbContext.Coupons
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                entity => entity.TenantId == tenantId &&
                          entity.NormalizedCode == normalizedCode &&
                          !entity.IsDeleted,
                cancellationToken);

        if (coupon == null || coupon.IsDeleted)
        {
            return CouponValidationDecision.Failure(CouponErrorCodes.Invalid, "Invalid coupon code");
        }

        return await ValidateCouponInternalAsync(
            coupon,
            normalizedCode,
            authenticatedCustomer.CustomerId,
            snapshot,
            correlationId,
            excludeOrderId,
            cancellationToken);
    }

    public async Task<CouponValidationDecision> ValidateCouponByIdAsync(
        Guid tenantId,
        Guid couponId,
        string couponCode,
        Guid? customerId,
        CouponValidationSnapshot snapshot,
        string correlationId,
        Guid? excludeOrderId = null,
        CancellationToken cancellationToken = default)
    {
        var authenticatedCustomer = RequireAuthenticatedCustomer(customerId);
        if (!authenticatedCustomer.IsAuthenticated)
        {
            return CouponValidationDecision.Failure(
                authenticatedCustomer.ErrorCode!,
                authenticatedCustomer.ErrorMessage!);
        }

        var coupon = await _dbContext.Coupons
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                entity => entity.TenantId == tenantId &&
                          entity.Id == couponId,
                cancellationToken);

        if (coupon == null || coupon.IsDeleted)
        {
            return CouponValidationDecision.Failure(CouponErrorCodes.NotFound, "Coupon not found");
        }

        return await ValidateCouponInternalAsync(
            coupon,
            NormalizeCouponCode(couponCode),
            authenticatedCustomer.CustomerId,
            snapshot,
            correlationId,
            excludeOrderId,
            cancellationToken);
    }

    public async Task<CouponReservationDecision> ReserveCouponAsync(
        Guid tenantId,
        Guid couponId,
        string couponCode,
        Guid orderId,
        Guid? customerId,
        CouponValidationSnapshot snapshot,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionalMutation("Coupon reservation");

        if (orderId == Guid.Empty)
        {
            throw new CouponAuthorityException("COUPON_ORDER_REQUIRED", "Coupon reservation requires a valid order.");
        }

        await LockCouponForCapacityMutationAsync(tenantId, couponId, cancellationToken);

        var validation = await ValidateCouponByIdAsync(
            tenantId,
            couponId,
            couponCode,
            customerId,
            snapshot,
            correlationId,
            excludeOrderId: orderId,
            cancellationToken);

        if (!validation.IsValid || !validation.CouponId.HasValue || string.IsNullOrWhiteSpace(validation.CouponCode))
        {
            throw new CouponAuthorityException(
                validation.ErrorCode ?? CouponErrorCodes.Invalid,
                validation.ErrorMessage ?? "Coupon is no longer valid.");
        }

        var existingReservation = await _dbContext.CouponReservations
            .FirstOrDefaultAsync(
                reservation => reservation.TenantId == tenantId &&
                               reservation.OrderId == orderId &&
                               !reservation.IsDeleted,
                cancellationToken);

        if (existingReservation != null)
        {
            if (existingReservation.CouponId != validation.CouponId.Value)
            {
                throw new CouponAuthorityException(
                    "COUPON_ORDER_ALREADY_RESERVED",
                    "This order already has a different coupon reserved.");
            }

            if (existingReservation.Status == CouponReservationStatus.Released)
            {
                existingReservation.Status = CouponReservationStatus.Reserved;
                existingReservation.ReleasedAtUtc = null;
                existingReservation.ConfirmedAtUtc = null;
                existingReservation.CorrelationId = correlationId;
                await _dbContext.SaveChangesAsync(cancellationToken);

                return CouponReservationDecision.Reserved(
                    validation.CouponId.Value,
                    validation.CouponCode,
                    existingReservation.Id,
                    validation.CalculatedDiscountAmount ?? 0m,
                    wasCreated: false,
                    wasIdempotent: false,
                    existingReservation);
            }

            return CouponReservationDecision.Reserved(
                validation.CouponId.Value,
                validation.CouponCode,
                existingReservation.Id,
                validation.CalculatedDiscountAmount ?? 0m,
                wasCreated: false,
                wasIdempotent: true,
                existingReservation);
        }

        var reservation = new CouponReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CouponId = validation.CouponId.Value,
            CustomerId = customerId!.Value,
            OrderId = orderId,
            Status = CouponReservationStatus.Reserved,
            CorrelationId = correlationId,
            ReservedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CouponReservations.Add(reservation);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return CouponReservationDecision.Reserved(
                validation.CouponId.Value,
                validation.CouponCode,
                reservation.Id,
                validation.CalculatedDiscountAmount ?? 0m,
                wasCreated: true,
                wasIdempotent: false,
                reservation);
        }
        catch (DbUpdateException ex)
        {
            Detach(reservation);

            if (IsDuplicateReservationOrderException(ex))
            {
                var persistedReservation = await _dbContext.CouponReservations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item => item.TenantId == tenantId &&
                                item.OrderId == orderId &&
                                !item.IsDeleted,
                        cancellationToken);

                if (persistedReservation != null &&
                    persistedReservation.CouponId == validation.CouponId.Value &&
                    persistedReservation.Status != CouponReservationStatus.Released)
                {
                    return CouponReservationDecision.Reserved(
                        validation.CouponId.Value,
                        validation.CouponCode,
                        persistedReservation.Id,
                        validation.CalculatedDiscountAmount ?? 0m,
                        wasCreated: false,
                        wasIdempotent: true,
                        persistedReservation);
                }
            }

            if (IsDuplicateReservationCustomerException(ex))
            {
                throw new CouponAuthorityException(
                    CouponErrorCodes.ReservationExists,
                    "This coupon is already reserved by another pending order. Complete or cancel that order, or remove the coupon to continue.",
                    ex);
            }

            throw;
        }
    }

    public async Task<CouponConfirmationDecision> ConfirmCouponUsageAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionalMutation("Coupon confirmation");

        var order = await _dbContext.Orders
            .Include(entity => entity.Items)
            .FirstOrDefaultAsync(
                entity => entity.TenantId == tenantId &&
                          entity.Id == orderId &&
                          !entity.IsDeleted,
                cancellationToken);

        if (order == null || !order.CouponId.HasValue || string.IsNullOrWhiteSpace(order.CouponCode))
        {
            return CouponConfirmationDecision.None();
        }

        var customerId = ResolveCouponCustomerId(order);
        var existingUsage = await _dbContext.CouponUsages
            .FirstOrDefaultAsync(
                usage => usage.TenantId == tenantId &&
                         usage.OrderId == orderId &&
                         !usage.IsDeleted,
                cancellationToken);

        var reservation = await _dbContext.CouponReservations
            .FirstOrDefaultAsync(
                entity => entity.TenantId == tenantId &&
                          entity.OrderId == orderId &&
                          !entity.IsDeleted,
                cancellationToken);

        if (existingUsage != null)
        {
            if (reservation != null && reservation.Status == CouponReservationStatus.Reserved)
            {
                reservation.Status = CouponReservationStatus.Confirmed;
                reservation.ConfirmedAtUtc ??= DateTime.UtcNow;
                reservation.ReleasedAtUtc = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return CouponConfirmationDecision.Confirmed(
                existingUsage.CouponId,
                existingUsage.CouponCode,
                existingUsage.Id,
                wasConfirmed: false,
                wasAlreadyConfirmed: true,
                createdLegacyReservation: false,
                confirmedUsageCount: await GetTotalConfirmedUsageCountAsync(tenantId, existingUsage.CouponId, cancellationToken),
                customerUsageCount: customerId.HasValue
                    ? await GetCustomerConfirmedUsageCountAsync(tenantId, existingUsage.CouponId, customerId.Value, cancellationToken)
                    : 0,
                existingUsage);
        }

        await LockCouponForCapacityMutationAsync(tenantId, order.CouponId.Value, cancellationToken);

        var snapshot = await BuildOrderSnapshotAsync(order, cancellationToken);
        var validation = await ValidateCouponByIdAsync(
            tenantId,
            order.CouponId.Value,
            order.CouponCode,
            customerId,
            snapshot,
            correlationId,
            excludeOrderId: orderId,
            cancellationToken);

        if (!validation.IsValid || !validation.CouponId.HasValue || string.IsNullOrWhiteSpace(validation.CouponCode))
        {
            throw new CouponAuthorityException(
                validation.ErrorCode ?? CouponErrorCodes.Invalid,
                validation.ErrorMessage ?? "Coupon confirmation failed because the coupon is no longer valid.");
        }

        var createdLegacyReservation = false;
        if (reservation == null)
        {
            reservation = new CouponReservation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CouponId = validation.CouponId.Value,
                CustomerId = customerId!.Value,
                OrderId = orderId,
                Status = CouponReservationStatus.Confirmed,
                CorrelationId = correlationId,
                ReservedAtUtc = order.CreatedAt == default ? DateTime.UtcNow : order.CreatedAt,
                ConfirmedAtUtc = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.CouponReservations.Add(reservation);
            createdLegacyReservation = true;
        }
        else
        {
            if (reservation.Status == CouponReservationStatus.Released)
            {
                throw new CouponAuthorityException(
                    CouponErrorCodes.ReservationMissing,
                    "Coupon reservation has already been released for this order.");
            }

            reservation.Status = CouponReservationStatus.Confirmed;
            reservation.ConfirmedAtUtc ??= DateTime.UtcNow;
            reservation.ReleasedAtUtc = null;
        }

        var couponUsage = new CouponUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CouponId = validation.CouponId.Value,
            OrderId = orderId,
            CustomerId = customerId,
            CouponCode = validation.CouponCode,
            DiscountAmount = validation.CalculatedDiscountAmount ?? 0m,
            OrderSubtotal = order.Subtotal,
            UsedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CouponUsages.Add(couponUsage);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            Detach(couponUsage);

            if (IsDuplicateUsageOrderException(ex))
            {
                var persistedUsage = await _dbContext.CouponUsages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        usage => usage.TenantId == tenantId &&
                                 usage.OrderId == orderId &&
                                 !usage.IsDeleted,
                        cancellationToken);

                if (persistedUsage != null)
                {
                    return CouponConfirmationDecision.Confirmed(
                        persistedUsage.CouponId,
                        persistedUsage.CouponCode,
                        persistedUsage.Id,
                        wasConfirmed: false,
                        wasAlreadyConfirmed: true,
                        createdLegacyReservation: false,
                        confirmedUsageCount: await GetTotalConfirmedUsageCountAsync(tenantId, persistedUsage.CouponId, cancellationToken),
                        customerUsageCount: customerId.HasValue
                            ? await GetCustomerConfirmedUsageCountAsync(tenantId, persistedUsage.CouponId, customerId.Value, cancellationToken)
                            : 0,
                        persistedUsage);
                }
            }

            throw;
        }

        var confirmedUsageCount = await GetTotalConfirmedUsageCountAsync(tenantId, validation.CouponId.Value, cancellationToken);
        await SetCouponCurrentUsageAsync(tenantId, validation.CouponId.Value, confirmedUsageCount, cancellationToken);

        var customerUsageCount = customerId.HasValue
            ? await GetCustomerConfirmedUsageCountAsync(tenantId, validation.CouponId.Value, customerId.Value, cancellationToken)
            : 0;

        var orderNumber = string.IsNullOrWhiteSpace(order.OrderNumber) ? null : order.OrderNumber;

        await _eventPublisher.PublishAsync(new CouponConsumedEvent
        {
            TenantId = tenantId,
            CouponUsageId = couponUsage.Id,
            CouponId = validation.CouponId.Value,
            CouponCode = validation.CouponCode,
            OrderId = orderId,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            DiscountAmount = couponUsage.DiscountAmount,
            NewTotalUsageCount = confirmedUsageCount,
            CustomerUsageCount = customerUsageCount,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CouponConfirmationDecision.Confirmed(
            validation.CouponId.Value,
            validation.CouponCode,
            couponUsage.Id,
            wasConfirmed: true,
            wasAlreadyConfirmed: false,
            createdLegacyReservation,
            confirmedUsageCount,
            customerUsageCount,
            couponUsage);
    }

    public async Task<CouponReleaseDecision> ReleaseCouponReservationAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionalMutation("Coupon release");

        var reservation = await _dbContext.CouponReservations
            // Expiration runs in a tenant-agnostic worker. The explicit tenant
            // predicate below is authority; an ambient tenant filter must not
            // silently hide the reservation and strand coupon capacity.
            .IgnoreQueryFilters()
            .Include(entity => entity.Coupon)
            .FirstOrDefaultAsync(
                entity => entity.TenantId == tenantId &&
                          entity.OrderId == orderId &&
                          !entity.IsDeleted,
                cancellationToken);

        if (reservation == null)
        {
            return CouponReleaseDecision.None();
        }

        var hadConfirmedUsage = await _dbContext.CouponUsages
            .IgnoreQueryFilters()
            .AnyAsync(
                usage => usage.TenantId == tenantId &&
                         usage.OrderId == orderId &&
                         !usage.IsDeleted,
                cancellationToken);

        if (!hadConfirmedUsage && reservation.Status == CouponReservationStatus.Reserved)
        {
            reservation.Status = CouponReservationStatus.Released;
            reservation.ReleasedAtUtc = DateTime.UtcNow;
            reservation.CorrelationId = correlationId;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[Coupon] Reservation released | TenantId: {TenantId} | OrderId: {OrderId} | CouponId: {CouponId} | Reason: {Reason} | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                reservation.CouponId,
                reason,
                correlationId);

            return CouponReleaseDecision.Released(
                reservation.CouponId,
                reservation.Coupon?.Code ?? string.Empty,
                reservation.Id,
                wasReleased: true,
                hadConfirmedUsage: false);
        }

        return CouponReleaseDecision.Released(
            reservation.CouponId,
            reservation.Coupon?.Code ?? string.Empty,
            reservation.Id,
            wasReleased: false,
            hadConfirmedUsage: hadConfirmedUsage);
    }

    public Task<int> GetTotalConfirmedUsageCountAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.CouponUsages
            .CountAsync(
                usage => usage.TenantId == tenantId &&
                         usage.CouponId == couponId &&
                         !usage.IsDeleted,
                cancellationToken);
    }

    public Task<int> GetCustomerConfirmedUsageCountAsync(
        Guid tenantId,
        Guid couponId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.CouponUsages
            .CountAsync(
                usage => usage.TenantId == tenantId &&
                         usage.CouponId == couponId &&
                         usage.CustomerId == customerId &&
                         !usage.IsDeleted,
                cancellationToken);
    }

    public Task<bool> CustomerHasCompletedOrdersAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Orders.AnyAsync(
            order => order.TenantId == tenantId &&
                     (order.OnlineCustomerId == customerId || order.CustomerId == customerId) &&
                     order.Status != OrderStatus.Cancelled &&
                     order.Status != OrderStatus.Refunded &&
                     order.FulfillmentStatus != FulfillmentStatus.Returned &&
                     order.PaymentStatus == PaymentStatus.Collected,
            cancellationToken);
    }

    private async Task<CouponValidationDecision> ValidateCouponInternalAsync(
        Coupon coupon,
        string normalizedSuppliedCode,
        Guid customerId,
        CouponValidationSnapshot snapshot,
        string correlationId,
        Guid? excludeOrderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedSuppliedCode))
        {
            return CouponValidationDecision.Failure(
                CouponErrorCodes.CodeMismatch,
                "Coupon code does not match");
        }

        var normalizedStoredCode = NormalizeCouponCode(coupon.Code);
        if (!string.Equals(normalizedStoredCode, normalizedSuppliedCode, StringComparison.Ordinal))
        {
            return CouponValidationDecision.Failure(
                CouponErrorCodes.CodeMismatch,
                "Coupon code does not match");
        }

        var couponConfigurationError = DiscountValidation.GetCouponValidationError(
            coupon.DiscountType,
            coupon.DiscountValue,
            coupon.MinOrderAmount,
            coupon.MaxDiscountAmount,
            coupon.MaxUsage,
            coupon.MaxUsagePerCustomer,
            coupon.StartDate,
            coupon.EndDate);

        if (couponConfigurationError != null)
        {
            _reliabilityTelemetry?.RecordCouponInvalidConfiguration(coupon.TenantId, "coupon_validation_service", couponConfigurationError);
            return CouponValidationDecision.Failure(
                CouponErrorCodes.InvalidConfig,
                "This coupon has an invalid configuration.");
        }

        var now = DateTime.UtcNow;
        if (!coupon.IsActive)
        {
            return CouponValidationDecision.Failure(CouponErrorCodes.Inactive, "This coupon is no longer active");
        }

        if (coupon.StartDate.HasValue && now < coupon.StartDate.Value)
        {
            return CouponValidationDecision.Failure(CouponErrorCodes.NotYetValid, "This coupon is not yet valid");
        }

        if (coupon.EndDate.HasValue && now > coupon.EndDate.Value)
        {
            return CouponValidationDecision.Failure(CouponErrorCodes.Expired, "This coupon has expired");
        }

        if (coupon.MinOrderAmount.HasValue && snapshot.OrderSubtotal < coupon.MinOrderAmount.Value)
        {
            return CouponValidationDecision.Failure(
                CouponErrorCodes.MinimumNotMet,
                $"Minimum order amount of {coupon.MinOrderAmount:C} is required");
        }

        var confirmedUsageCount = await GetTotalConfirmedUsageCountAsync(coupon.TenantId, coupon.Id, cancellationToken);
        var (activeReservationCount, activeCustomerReservationCount) = await GetActiveReservationCountsAsync(
            coupon.TenantId, coupon.Id, customerId, excludeOrderId, cancellationToken);
        if (coupon.MaxUsage.HasValue &&
            confirmedUsageCount + activeReservationCount >= coupon.MaxUsage.Value)
        {
            _reliabilityTelemetry?.RecordCouponUsageLimitReached(coupon.TenantId, "global");
            return CouponValidationDecision.Failure(
                CouponErrorCodes.MaxUsageExceeded,
                "This coupon has reached its usage limit");
        }

        var customerConfirmedUsageCount = coupon.MaxUsagePerCustomer.HasValue
            ? await GetCustomerConfirmedUsageCountAsync(coupon.TenantId, coupon.Id, customerId, cancellationToken)
            : 0;
        // SQL permits one active reservation per tenant/coupon/customer, including
        // unlimited and non-first-order coupons. Preview must not promise a quote
        // that the final locked reservation is guaranteed to reject.

        if (coupon.MaxUsagePerCustomer.HasValue &&
            customerConfirmedUsageCount + activeCustomerReservationCount >= coupon.MaxUsagePerCustomer.Value)
        {
            _reliabilityTelemetry?.RecordCouponUsageLimitReached(coupon.TenantId, "customer");
            return CouponValidationDecision.Failure(
                CouponErrorCodes.CustomerMaxExceeded,
                "You have already used this coupon the maximum number of times");
        }

        if (activeCustomerReservationCount > 0)
        {
            return CouponValidationDecision.Failure(
                CouponErrorCodes.ReservationExists,
                "This coupon is already reserved by another pending order. Complete or cancel that order, or remove the coupon to continue.");
        }

        if (coupon.FirstOrderOnly)
        {
            if (await CustomerHasCompletedOrdersAsync(coupon.TenantId, customerId, cancellationToken))
            {
                return CouponValidationDecision.Failure(
                    CouponErrorCodes.FirstOrderOnly,
                    "This coupon is only valid for first-time orders");
            }

            if (await HasActiveFirstOrderReservationAsync(coupon.TenantId, customerId, excludeOrderId, cancellationToken))
            {
                return CouponValidationDecision.Failure(
                    CouponErrorCodes.FirstOrderOnly,
                    "This coupon is already reserved by another pending order.");
            }
        }

        var scope = ParseScope(coupon, correlationId);
        var scopeFailure = ValidateScope(coupon.TenantId, scope, snapshot.CartItems, correlationId);
        if (scopeFailure != null)
        {
            return CouponValidationDecision.Failure(scopeFailure.Value.ErrorCode, scopeFailure.Value.ErrorMessage);
        }

        var calculatedDiscountAmount = CalculateDiscount(coupon, snapshot.OrderSubtotal);
        if (snapshot.AppliedDiscountAmount > 0m &&
            RoundCurrency(snapshot.AppliedDiscountAmount) != calculatedDiscountAmount)
        {
            _reliabilityTelemetry?.RecordCouponSplitBrainDetected(coupon.TenantId, "coupon_validation_service", "discount_mismatch");
            return CouponValidationDecision.Failure(
                CouponErrorCodes.DiscountMismatch,
                "Applied coupon discount no longer matches the authoritative coupon rules.");
        }

        return CouponValidationDecision.Success(
            coupon.Id,
            coupon.Code,
            coupon.DiscountType == DiscountType.Percentage ? "percentage" : "fixed",
            coupon.DiscountValue,
            calculatedDiscountAmount,
            confirmedUsageCount,
            customerConfirmedUsageCount,
            activeReservationCount,
            activeCustomerReservationCount);
    }

    private async Task<CouponValidationSnapshot> BuildOrderSnapshotAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var productIds = order.Items
            .Select(item => item.ProductId)
            .Where(productId => productId != Guid.Empty)
            .Distinct()
            .ToList();

        Dictionary<Guid, Guid> categoryLookup = new();
        if (productIds.Count > 0)
        {
            categoryLookup = await _dbContext.Products
                .AsNoTracking()
                .Where(product => productIds.Contains(product.Id) && !product.IsDeleted)
                .Select(product => new { product.Id, product.CategoryId })
                .ToDictionaryAsync(product => product.Id, product => product.CategoryId, cancellationToken);
        }

        return new CouponValidationSnapshot
        {
            OrderSubtotal = order.Subtotal,
            AppliedDiscountAmount = order.DiscountAmount,
            CartItems = order.Items
                .Select(item => new CouponScopeValidationItem(
                    item.ProductId,
                    categoryLookup.GetValueOrDefault(item.ProductId)))
                .ToList()
        };
    }

    private async Task<(int Total, int Customer)> GetActiveReservationCountsAsync(
        Guid tenantId,
        Guid couponId,
        Guid customerId,
        Guid? excludeOrderId,
        CancellationToken cancellationToken)
    {
        var counts = await _dbContext.CouponReservations
            .Where(reservation => reservation.TenantId == tenantId &&
                                reservation.CouponId == couponId &&
                                reservation.Status == CouponReservationStatus.Reserved &&
                                !reservation.IsDeleted &&
                                (!excludeOrderId.HasValue || reservation.OrderId != excludeOrderId.Value))
            .GroupBy(_ => 1)
            .Select(group => new { Total = group.Count(), Customer = group.Count(reservation => reservation.CustomerId == customerId) })
            .FirstOrDefaultAsync(cancellationToken);
        return (counts?.Total ?? 0, counts?.Customer ?? 0);
    }

    private async Task<bool> HasActiveFirstOrderReservationAsync(
        Guid tenantId,
        Guid customerId,
        Guid? excludeOrderId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CouponReservations
            .Where(reservation => reservation.TenantId == tenantId &&
                                  reservation.CustomerId == customerId &&
                                  reservation.Status == CouponReservationStatus.Reserved &&
                                  !reservation.IsDeleted &&
                                  (!excludeOrderId.HasValue || reservation.OrderId != excludeOrderId.Value))
            .Join(
                _dbContext.Coupons.IgnoreQueryFilters(),
                reservation => reservation.CouponId,
                coupon => coupon.Id,
                (reservation, coupon) => new { reservation, coupon })
            .AnyAsync(
                item => !item.coupon.IsDeleted && item.coupon.FirstOrderOnly,
                cancellationToken);
    }

    private async Task SetCouponCurrentUsageAsync(
        Guid tenantId,
        Guid couponId,
        int confirmedUsageCount,
        CancellationToken cancellationToken)
    {
        var rowsAffected = await _dbContext.Coupons
            .IgnoreQueryFilters()
            .Where(coupon => coupon.TenantId == tenantId &&
                             coupon.Id == couponId &&
                             !coupon.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(coupon => coupon.CurrentUsage, _ => confirmedUsageCount),
                cancellationToken);

        if (rowsAffected == 0)
        {
            throw new CouponAuthorityException(
                CouponErrorCodes.NotFound,
                "Coupon could not be updated while confirming usage.");
        }
    }

    private static CouponAuthenticatedCustomerResult RequireAuthenticatedCustomer(Guid? customerId)
    {
        if (!customerId.HasValue || customerId.Value == Guid.Empty)
        {
            return CouponAuthenticatedCustomerResult.Failure(
                CouponErrorCodes.AuthenticationRequired,
                "You must sign in to use coupons");
        }

        return CouponAuthenticatedCustomerResult.Success(customerId.Value);
    }

    private static Guid? ResolveCouponCustomerId(Order order)
    {
        if (order.OnlineCustomerId.HasValue && order.OnlineCustomerId.Value != Guid.Empty)
        {
            return order.OnlineCustomerId.Value;
        }

        if (order.CustomerId.HasValue && order.CustomerId.Value != Guid.Empty)
        {
            return order.CustomerId.Value;
        }

        return null;
    }

    private void EnsureTransactionalMutation(string operationName)
    {
        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException($"{operationName} must execute inside an open SQL transaction.");
        }
    }

    private async Task LockCouponForCapacityMutationAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        var providerName = _dbContext.Database.ProviderName ?? string.Empty;
        Coupon? lockedCoupon;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            lockedCoupon = await _dbContext.Coupons
                .FromSqlInterpolated($@"
                    SELECT xmin, * FROM ""Coupons""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {couponId}
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            lockedCoupon = await _dbContext.Coupons
                .FromSqlInterpolated($@"
                    SELECT * FROM [Coupons] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {couponId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Database provider '{ProviderName}' does not support coupon capacity row locks. " +
                "This fallback is intended for tests only.",
                providerName);

            lockedCoupon = await _dbContext.Coupons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    coupon => coupon.TenantId == tenantId &&
                              coupon.Id == couponId &&
                              !coupon.IsDeleted,
                    cancellationToken);
        }

        if (lockedCoupon == null)
        {
            _logger.LogWarning(
                "[Coupon] Capacity lock could not find coupon | TenantId: {TenantId} | CouponId: {CouponId}",
                tenantId,
                couponId);
        }
    }

    private static string NormalizeCouponCode(string couponCode) =>
        couponCode?.Trim().ToUpperInvariant() ?? string.Empty;

    private static decimal CalculateDiscount(Coupon coupon, decimal orderSubtotal)
    {
        if (!DiscountValidation.TryValidateCoupon(
                coupon.DiscountType,
                coupon.DiscountValue,
                coupon.MinOrderAmount,
                coupon.MaxDiscountAmount,
                coupon.MaxUsage,
                coupon.MaxUsagePerCustomer,
                coupon.StartDate,
                coupon.EndDate,
                out _))
        {
            return 0m;
        }

        decimal discount = coupon.DiscountType == DiscountType.Percentage
            ? orderSubtotal * (coupon.DiscountValue / 100m)
            : coupon.DiscountValue;

        if (coupon.MaxDiscountAmount.HasValue && discount > coupon.MaxDiscountAmount.Value)
        {
            discount = coupon.MaxDiscountAmount.Value;
        }

        return RoundCurrency(Math.Min(discount, orderSubtotal));
    }

    private static decimal RoundCurrency(decimal value) => CurrencyHelper.Round(value);

    private CouponScopeDescriptor ParseScope(Coupon coupon, string correlationId)
    {
        try
        {
            return new CouponScopeDescriptor(
                ParseGuidCollection(coupon.ApplicableProductIds),
                ParseGuidCollection(coupon.ApplicableCategoryIds),
                ParseGuidCollection(coupon.ExcludedProductIds));
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "[Coupon] Scope parsing failed | TenantId: {TenantId} | CouponId: {CouponId} | CorrelationId: {CorrelationId}",
                coupon.TenantId,
                coupon.Id,
                correlationId);

            throw new CouponAuthorityException(
                CouponErrorCodes.InvalidConfig,
                "Coupon scope configuration is invalid.",
                ex);
        }
    }

    private static List<Guid> ParseGuidCollection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<Guid>();
        }

        var values = JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
        return values.Where(value => value != Guid.Empty).Distinct().ToList();
    }

    private ScopeValidationFailure? ValidateScope(
        Guid tenantId,
        CouponScopeDescriptor scope,
        IReadOnlyCollection<CouponScopeValidationItem> cartItems,
        string correlationId)
    {
        if (scope.RequiresCartItems && (cartItems == null || cartItems.Count == 0))
        {
            _reliabilityTelemetry?.RecordCouponSplitBrainDetected(tenantId, "coupon_validation_service", "missing_scope_items");
            return new ScopeValidationFailure(
                CouponErrorCodes.NotApplicable,
                "This coupon cannot be validated for the current cart.");
        }

        var cartProductIds = cartItems
            .Select(item => item.ProductId)
            .Where(productId => productId != Guid.Empty)
            .ToHashSet();

        if (scope.ExcludedProductIds.Count > 0 &&
            scope.ExcludedProductIds.Any(cartProductIds.Contains))
        {
            return new ScopeValidationFailure(
                CouponErrorCodes.NotApplicable,
                "This coupon cannot be used with one or more items in your cart.");
        }

        if (scope.ApplicableProductIds.Count > 0 &&
            !scope.ApplicableProductIds.Any(cartProductIds.Contains))
        {
            return new ScopeValidationFailure(
                CouponErrorCodes.NotApplicable,
                "This coupon is not applicable to the products in your cart.");
        }

        if (scope.ApplicableCategoryIds.Count > 0)
        {
            var cartCategoryIds = cartItems
                .Select(item => item.CategoryId)
                .Where(categoryId => categoryId != Guid.Empty)
                .ToHashSet();

            if (!scope.ApplicableCategoryIds.Any(cartCategoryIds.Contains))
            {
                _logger.LogWarning(
                    "[Coupon] Category scope check failed | CorrelationId: {CorrelationId}",
                    correlationId);

                return new ScopeValidationFailure(
                    CouponErrorCodes.NotApplicable,
                    "This coupon is not applicable to the categories in your cart.");
            }
        }

        return null;
    }

    private static bool IsDuplicateReservationOrderException(DbUpdateException exception)
    {
        return IsUniqueConstraintViolation(
            exception,
            CouponReservationOrderUniqueIndex,
            "CouponReservations.TenantId",
            "CouponReservations.OrderId");
    }

    private static bool IsDuplicateReservationCustomerException(DbUpdateException exception)
    {
        return IsUniqueConstraintViolation(
            exception,
            CouponReservationCustomerUniqueIndex,
            "CouponReservations.TenantId",
            "CouponReservations.CouponId",
            "CouponReservations.CustomerId");
    }

    private static bool IsDuplicateUsageOrderException(DbUpdateException exception)
    {
        return IsUniqueConstraintViolation(
            exception,
            CouponUsageOrderUniqueIndex,
            "CouponUsages.TenantId",
            "CouponUsages.OrderId");
    }

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception,
        string constraintName,
        params string[] sqliteColumnHints)
    {
        if (exception.InnerException is PostgresException postgresException &&
            postgresException.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            if (string.Equals(postgresException.ConstraintName, constraintName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(postgresException.Detail) &&
                sqliteColumnHints.All(hint => postgresException.Detail.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        var fullMessage = string.Join(
            Environment.NewLine,
            new[]
            {
                exception.Message,
                exception.InnerException?.Message ?? string.Empty
            });

        return fullMessage.Contains(constraintName, StringComparison.OrdinalIgnoreCase) ||
               sqliteColumnHints.All(hint => fullMessage.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private void Detach(object entity)
    {
        var entry = _dbContext.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    private readonly record struct CouponAuthenticatedCustomerResult(
        bool IsAuthenticated,
        Guid CustomerId,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public static CouponAuthenticatedCustomerResult Success(Guid customerId) =>
            new(true, customerId, null, null);

        public static CouponAuthenticatedCustomerResult Failure(string errorCode, string errorMessage) =>
            new(false, Guid.Empty, errorCode, errorMessage);
    }

    private readonly record struct CouponScopeDescriptor(
        IReadOnlyCollection<Guid> ApplicableProductIds,
        IReadOnlyCollection<Guid> ApplicableCategoryIds,
        IReadOnlyCollection<Guid> ExcludedProductIds)
    {
        public bool RequiresCartItems =>
            ApplicableProductIds.Count > 0 ||
            ApplicableCategoryIds.Count > 0 ||
            ExcludedProductIds.Count > 0;
    }

    private readonly record struct ScopeValidationFailure(string ErrorCode, string ErrorMessage);
}

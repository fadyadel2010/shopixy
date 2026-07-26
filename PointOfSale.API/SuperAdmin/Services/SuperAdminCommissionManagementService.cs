using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminCommissionManagementService
{
    Task<IReadOnlyList<SuperAdminCommissionRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default);

    Task<SuperAdminCommissionRuleDto> CreateRuleAsync(
        SuperAdminUpsertCommissionRuleRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminCommissionRuleDto> UpdateRuleAsync(
        Guid id,
        SuperAdminUpsertCommissionRuleRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminCommissionRuleDto> DeactivateRuleAsync(
        Guid id,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminCommissionResolutionPreviewDto> ResolvePreviewAsync(
        Guid? vendorId,
        Guid? categoryId,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminCommissionValidationException : Exception
{
    public SuperAdminCommissionValidationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed class SuperAdminCommissionManagementService : ISuperAdminCommissionManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommissionService _commissionService;

    public SuperAdminCommissionManagementService(
        ApplicationDbContext dbContext,
        ICommissionService commissionService)
    {
        _dbContext = dbContext;
        _commissionService = commissionService;
    }

    public async Task<IReadOnlyList<SuperAdminCommissionRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.CommissionRules
            .AsNoTracking()
            .OrderByDescending(rule => rule.IsActive)
            .ThenBy(rule => rule.ScopeType)
            .ThenByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.CreatedAt)
            .Select(rule => Map(rule))
            .ToListAsync(cancellationToken);
    }

    public async Task<SuperAdminCommissionRuleDto> CreateRuleAsync(
        SuperAdminUpsertCommissionRuleRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var scopeType = RequireScopeType(request);
        var rate = RequireRate(request);
        var scopeId = NormalizeScopeId(scopeType, request.ScopeId);

        await ValidateScopeTargetAsync(scopeType, scopeId, cancellationToken);
        await ValidateRuleConflictsAsync(scopeType, scopeId, request.Priority, ruleIdToIgnore: null, ruleWillBeActive: true, cancellationToken);

        var rule = new CommissionRule
        {
            Id = Guid.NewGuid(),
            ScopeType = scopeType,
            ScopeId = scopeId,
            Rate = rate,
            IsActive = true,
            Priority = request.Priority,
            CreatedBy = actorId
        };

        _dbContext.CommissionRules.Add(rule);
        await SaveChangesAndInvalidateCacheAsync(cancellationToken);

        return Map(rule);
    }

    public async Task<SuperAdminCommissionRuleDto> UpdateRuleAsync(
        Guid id,
        SuperAdminUpsertCommissionRuleRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.CommissionRules
            .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted, cancellationToken);

        if (rule == null)
        {
            throw new SuperAdminCommissionValidationException(
                StatusCodes.Status404NotFound,
                "COMMISSION_RULE_NOT_FOUND",
                "Commission rule was not found.");
        }

        var scopeType = RequireScopeType(request);
        var rate = RequireRate(request);
        var scopeId = NormalizeScopeId(scopeType, request.ScopeId);

        if (rule.IsActive &&
            rule.ScopeType == CommissionRuleScopeType.Global &&
            scopeType != CommissionRuleScopeType.Global)
        {
            await EnsureAnotherActiveGlobalRuleExistsAsync(id, cancellationToken);
        }

        await ValidateScopeTargetAsync(scopeType, scopeId, cancellationToken);
        await ValidateRuleConflictsAsync(scopeType, scopeId, request.Priority, id, rule.IsActive, cancellationToken);

        rule.ScopeType = scopeType;
        rule.ScopeId = scopeId;
        rule.Rate = rate;
        rule.Priority = request.Priority;
        rule.UpdatedBy = actorId;

        await SaveChangesAndInvalidateCacheAsync(cancellationToken);

        return Map(rule);
    }

    public async Task<SuperAdminCommissionRuleDto> DeactivateRuleAsync(
        Guid id,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.CommissionRules
            .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted, cancellationToken);

        if (rule == null)
        {
            throw new SuperAdminCommissionValidationException(
                StatusCodes.Status404NotFound,
                "COMMISSION_RULE_NOT_FOUND",
                "Commission rule was not found.");
        }

        if (!rule.IsActive)
        {
            return Map(rule);
        }

        if (rule.ScopeType == CommissionRuleScopeType.Global)
        {
            await EnsureAnotherActiveGlobalRuleExistsAsync(id, cancellationToken);
        }

        rule.IsActive = false;
        rule.UpdatedBy = actorId;

        await SaveChangesAndInvalidateCacheAsync(cancellationToken);

        return Map(rule);
    }

    public Task<SuperAdminCommissionResolutionPreviewDto> ResolvePreviewAsync(
        Guid? vendorId,
        Guid? categoryId,
        CancellationToken cancellationToken = default)
    {
        CommissionResolutionResult resolution;
        try
        {
            resolution = _commissionService.ResolveCommission(vendorId, categoryId);
        }
        catch (CommissionAuthorityException ex)
        {
            throw new SuperAdminCommissionValidationException(
                StatusCodes.Status409Conflict,
                ex.Code,
                ex.Message);
        }

        return Task.FromResult(new SuperAdminCommissionResolutionPreviewDto
        {
            VendorId = vendorId,
            CategoryId = categoryId,
            Rate = resolution.Rate,
            Source = resolution.Source,
            RuleId = resolution.RuleId,
            Priority = resolution.Priority,
            ScopeType = resolution.ScopeType,
            ScopeId = resolution.ScopeId,
            UsedLegacyFallback = resolution.UsedLegacyFallback,
            CacheStrategy = "MemoryCache"
        });
    }

    private async Task ValidateScopeTargetAsync(
        CommissionRuleScopeType scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken)
    {
        switch (scopeType)
        {
            case CommissionRuleScopeType.Global:
                return;

            case CommissionRuleScopeType.Category:
            {
                var exists = await _dbContext.CommerceHubCategories
                    .AsNoTracking()
                    .AnyAsync(category => category.Id == scopeId, cancellationToken);

                if (!exists)
                {
                    throw new SuperAdminCommissionValidationException(
                        StatusCodes.Status400BadRequest,
                        "COMMISSION_SCOPE_TARGET_NOT_FOUND",
                        "The specified CommerceHub category does not exist.");
                }

                return;
            }

            case CommissionRuleScopeType.Vendor:
            {
                var exists = await _dbContext.Tenants
                    .AsNoTracking()
                    .AnyAsync(tenant => tenant.Id == scopeId && tenant.Status != TenantStatus.Deleted, cancellationToken);

                if (!exists)
                {
                    throw new SuperAdminCommissionValidationException(
                        StatusCodes.Status400BadRequest,
                        "COMMISSION_SCOPE_TARGET_NOT_FOUND",
                        "The specified vendor does not exist.");
                }

                return;
            }

            default:
                throw new SuperAdminCommissionValidationException(
                    StatusCodes.Status400BadRequest,
                    "COMMISSION_RULE_INVALID",
                    "Unsupported commission scope type.");
        }
    }

    private async Task ValidateRuleConflictsAsync(
        CommissionRuleScopeType scopeType,
        Guid? scopeId,
        int priority,
        Guid? ruleIdToIgnore,
        bool ruleWillBeActive,
        CancellationToken cancellationToken)
    {
        if (!ruleWillBeActive)
        {
            return;
        }

        if (scopeType == CommissionRuleScopeType.Global)
        {
            var hasAnotherActiveGlobal = await _dbContext.CommissionRules
                .AsNoTracking()
                .AnyAsync(rule =>
                    !rule.IsDeleted &&
                    rule.IsActive &&
                    rule.ScopeType == CommissionRuleScopeType.Global &&
                    rule.Id != ruleIdToIgnore,
                    cancellationToken);

            if (hasAnotherActiveGlobal)
            {
                throw new SuperAdminCommissionValidationException(
                    StatusCodes.Status409Conflict,
                    "COMMISSION_GLOBAL_ALREADY_EXISTS",
                    "Another active global commission rule already exists. Deactivate it before creating or promoting a new global rule.");
            }
        }

        var hasConflictingPriority = await _dbContext.CommissionRules
            .AsNoTracking()
            .AnyAsync(rule =>
                !rule.IsDeleted &&
                rule.IsActive &&
                rule.ScopeType == scopeType &&
                rule.ScopeId == scopeId &&
                rule.Priority == priority &&
                rule.Id != ruleIdToIgnore,
                cancellationToken);

        if (hasConflictingPriority)
        {
            throw new SuperAdminCommissionValidationException(
                StatusCodes.Status409Conflict,
                "COMMISSION_RULE_CONFLICT",
                $"Another active {scopeType} commission rule already uses the same scope target and priority ({priority}).");
        }
    }

    private async Task EnsureAnotherActiveGlobalRuleExistsAsync(Guid ruleIdToExclude, CancellationToken cancellationToken)
    {
        var hasAnotherActiveGlobal = await _dbContext.CommissionRules
            .AsNoTracking()
            .AnyAsync(rule =>
                !rule.IsDeleted &&
                rule.IsActive &&
                rule.ScopeType == CommissionRuleScopeType.Global &&
                rule.Id != ruleIdToExclude,
                cancellationToken);

        if (!hasAnotherActiveGlobal)
        {
            throw new SuperAdminCommissionValidationException(
                StatusCodes.Status409Conflict,
                "COMMISSION_GLOBAL_REQUIRED",
                "At least one active global commission rule must remain configured.");
        }
    }

    private static CommissionRuleScopeType RequireScopeType(SuperAdminUpsertCommissionRuleRequest request)
    {
        if (!request.ScopeType.HasValue)
        {
            throw new SuperAdminCommissionValidationException(
                StatusCodes.Status400BadRequest,
                "COMMISSION_RULE_INVALID",
                "ScopeType is required.");
        }

        return request.ScopeType.Value;
    }

    private static decimal RequireRate(SuperAdminUpsertCommissionRuleRequest request)
    {
        if (!request.Rate.HasValue || request.Rate.Value < 0m || request.Rate.Value > 1m)
        {
            throw new SuperAdminCommissionValidationException(
                StatusCodes.Status400BadRequest,
                "COMMISSION_RULE_INVALID",
                "Rate must be between 0 and 1.");
        }

        return CommissionCalculator.RoundRate(request.Rate.Value);
    }

    private static Guid? NormalizeScopeId(CommissionRuleScopeType scopeType, Guid? scopeId)
    {
        if (scopeType == CommissionRuleScopeType.Global)
        {
            if (scopeId.HasValue)
            {
                throw new SuperAdminCommissionValidationException(
                    StatusCodes.Status400BadRequest,
                    "COMMISSION_RULE_INVALID",
                    "Global commission rules cannot include a ScopeId.");
            }

            return null;
        }

        if (!scopeId.HasValue || scopeId.Value == Guid.Empty)
        {
            throw new SuperAdminCommissionValidationException(
                StatusCodes.Status400BadRequest,
                "COMMISSION_RULE_INVALID",
                "Category and vendor commission rules require a valid ScopeId.");
        }

        return scopeId.Value;
    }

    private static SuperAdminCommissionRuleDto Map(CommissionRule rule)
    {
        return new SuperAdminCommissionRuleDto
        {
            Id = rule.Id,
            ScopeType = rule.ScopeType,
            ScopeId = rule.ScopeId,
            Rate = rule.Rate,
            IsActive = rule.IsActive,
            Priority = rule.Priority,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }

    private async Task SaveChangesAndInvalidateCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _commissionService.InvalidateRulesCache();
        }
        catch (DbUpdateException exception) when (TryTranslateConstraintViolation(exception, out var validationException))
        {
            throw validationException;
        }
    }

    private static bool TryTranslateConstraintViolation(
        DbUpdateException exception,
        out SuperAdminCommissionValidationException validationException)
    {
        if (exception.InnerException is not PostgresException postgresException)
        {
            validationException = null!;
            return false;
        }

        if (postgresException.SqlState == "23505")
        {
            validationException = postgresException.ConstraintName switch
            {
                "IX_CommissionRules_ActiveGlobal" => new SuperAdminCommissionValidationException(
                    StatusCodes.Status409Conflict,
                    "COMMISSION_GLOBAL_ALREADY_EXISTS",
                    "Another active global commission rule already exists. Deactivate it before creating or promoting a new global rule."),
                "IX_CommissionRules_ActiveScopePriority" => new SuperAdminCommissionValidationException(
                    StatusCodes.Status409Conflict,
                    "COMMISSION_RULE_CONFLICT",
                    "Another active commission rule already uses the same scope target and priority."),
                _ => new SuperAdminCommissionValidationException(
                    StatusCodes.Status409Conflict,
                    "COMMISSION_RULE_CONFLICT",
                    "Commission rule write conflicted with an existing active rule.")
            };

            return true;
        }

        if (postgresException.SqlState == "23514")
        {
            validationException = postgresException.ConstraintName switch
            {
                "CK_CommissionRules_Rate" => new SuperAdminCommissionValidationException(
                    StatusCodes.Status400BadRequest,
                    "COMMISSION_RULE_INVALID",
                    "Rate must be between 0 and 1."),
                "CK_CommissionRules_Scope" => new SuperAdminCommissionValidationException(
                    StatusCodes.Status400BadRequest,
                    "COMMISSION_RULE_INVALID",
                    "Global rules require a null ScopeId, while category and vendor rules require a non-null ScopeId."),
                _ => new SuperAdminCommissionValidationException(
                    StatusCodes.Status400BadRequest,
                    "COMMISSION_RULE_INVALID",
                    "Commission rule failed a database validation constraint.")
            };

            return true;
        }

        validationException = null!;
        return false;
    }
}

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/treasury/initial-capitalizations")]
[Authorize(Roles = "Owner")]
public sealed class TreasuryInitialCapitalizationsController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly ITreasuryInitialCapitalizationService _service;

    public TreasuryInitialCapitalizationsController(
        ITenantContext tenantContext,
        ITreasuryInitialCapitalizationService service)
    {
        _tenantContext = tenantContext;
        _service = service;
    }

    [HttpGet("destinations")]
    public async Task<ActionResult<IReadOnlyList<TreasuryInitialCapitalizationDestinationResponse>>> GetDestinations(
        CancellationToken cancellationToken)
    {
        var destinations = await _service.GetEligibleDestinationsAsync(
            _tenantContext.RequiredTenantId,
            cancellationToken);
        return Ok(destinations.Select(item => new TreasuryInitialCapitalizationDestinationResponse(
            item.TreasuryAccountId,
            item.Name,
            item.NameAr,
            item.Type.ToString(),
            item.LedgerAccountId,
            item.LedgerAccountCode,
            item.LedgerAccountName,
            item.CurrentBalance)).ToList());
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TreasuryInitialCapitalizationResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var documents = await _service.GetAllAsync(_tenantContext.RequiredTenantId, cancellationToken);
        return Ok(documents.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TreasuryInitialCapitalizationResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var document = await _service.GetByIdAsync(_tenantContext.RequiredTenantId, id, cancellationToken);
        return document == null ? NotFound() : Ok(ToResponse(document));
    }

    [HttpPost]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<ActionResult<TreasuryInitialCapitalizationResponse>> Create(
        CreateTreasuryInitialCapitalizationRequest request,
        CancellationToken cancellationToken)
    {
        var document = await _service.CreateAndPostAsync(
            _tenantContext.RequiredTenantId,
            GetRequiredUserId(),
            new CreateTreasuryInitialCapitalizationCommand(
                request.OperationId,
                request.CutoverDate,
                request.Allocations.Select(item =>
                    new TreasuryInitialCapitalizationAllocationCommand(item.TreasuryAccountId, item.Amount)).ToList(),
                request.EvidenceReference,
                request.Notes,
                request.DeclarationVersion,
                request.DeclarationAcknowledged,
                request.PredecessorCapitalizationId),
            cancellationToken);

        return Ok(ToResponse(document));
    }

    [HttpPost("{id:guid}/reverse")]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<ActionResult<TreasuryInitialCapitalizationResponse>> Reverse(
        Guid id,
        ReverseTreasuryInitialCapitalizationRequest request,
        CancellationToken cancellationToken)
    {
        var document = await _service.ReverseAsync(
            _tenantContext.RequiredTenantId,
            GetRequiredUserId(),
            id,
            new ReverseTreasuryInitialCapitalizationCommand(
                request.OperationId,
                request.Reason,
                request.Confirmed),
            cancellationToken);
        return Ok(ToResponse(document));
    }

    private Guid GetRequiredUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
        {
            throw new TreasuryInitialCapitalizationException(
                "AUTHORIZATION_REQUIRED",
                "A valid Owner identity is required.",
                TreasuryInitialCapitalizationErrorKind.Forbidden);
        }
        return userId;
    }

    private TreasuryInitialCapitalizationResponse ToResponse(TreasuryInitialCapitalization document) => new(
        document.Id,
        document.DocumentNumber,
        document.OperationId,
        document.CutoverDate,
        document.TotalAmount,
        document.Status.ToString(),
        document.DeclarationVersion,
        document.EvidenceReference,
        document.Notes,
        document.CreatedByUserId,
        document.CreatedAtUtc,
        document.PostedAtUtc,
        document.PostedJournalId,
        document.ReversalJournalId,
        document.PredecessorCapitalizationId,
        document.MutationVersion,
        new TreasuryInitialCapitalizationOffsetResponse(
            document.OpeningEquityLedgerAccountId,
            document.OpeningEquityAccountCodeSnapshot,
            document.OpeningEquityAccountNameSnapshot),
        document.Allocations
            .OrderBy(item => item.TreasuryAccountTypeSnapshot)
            .ThenBy(item => item.TreasuryAccountNameSnapshot)
            .Select(item => new TreasuryInitialCapitalizationAllocationResponse(
                item.Id,
                item.TreasuryAccountId,
                item.LedgerAccountId,
                item.TreasuryAccountTypeSnapshot.ToString(),
                item.TreasuryAccountNameSnapshot,
                item.LedgerAccountCodeSnapshot,
                item.LedgerAccountNameSnapshot,
                item.Amount))
            .ToList(),
        RequestContextMetadata.GetCorrelationId(HttpContext));
}

public sealed record CreateTreasuryInitialCapitalizationRequest(
    Guid OperationId,
    DateTime CutoverDate,
    [Required, MinLength(1)] IReadOnlyList<CreateTreasuryInitialCapitalizationAllocationRequest> Allocations,
    [Required, MaxLength(200)] string EvidenceReference,
    [MaxLength(1000)] string? Notes,
    [Required, MaxLength(64)] string DeclarationVersion,
    bool DeclarationAcknowledged,
    Guid? PredecessorCapitalizationId = null);

public sealed record CreateTreasuryInitialCapitalizationAllocationRequest(
    Guid TreasuryAccountId,
    decimal Amount);

public sealed record ReverseTreasuryInitialCapitalizationRequest(
    Guid OperationId,
    [Required, MaxLength(500)] string Reason,
    bool Confirmed);

public sealed record TreasuryInitialCapitalizationDestinationResponse(
    Guid TreasuryAccountId,
    string Name,
    string? NameAr,
    string Type,
    Guid LedgerAccountId,
    string LedgerAccountCode,
    string LedgerAccountName,
    decimal CurrentBalance);

public sealed record TreasuryInitialCapitalizationOffsetResponse(
    Guid LedgerAccountId,
    string AccountCode,
    string AccountName);

public sealed record TreasuryInitialCapitalizationAllocationResponse(
    Guid Id,
    Guid TreasuryAccountId,
    Guid LedgerAccountId,
    string TreasuryAccountType,
    string TreasuryAccountName,
    string LedgerAccountCode,
    string LedgerAccountName,
    decimal Amount);

public sealed record TreasuryInitialCapitalizationResponse(
    Guid Id,
    string DocumentNumber,
    Guid OperationId,
    DateTime CutoverDate,
    decimal TotalAmount,
    string Status,
    string DeclarationVersion,
    string EvidenceReference,
    string? Notes,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime? PostedAtUtc,
    Guid? PostedJournalId,
    Guid? ReversalJournalId,
    Guid? PredecessorCapitalizationId,
    int MutationVersion,
    TreasuryInitialCapitalizationOffsetResponse OpeningEquity,
    IReadOnlyList<TreasuryInitialCapitalizationAllocationResponse> Allocations,
    string TraceId);

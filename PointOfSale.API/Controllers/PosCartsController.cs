using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.PosCart;
using PointOfSale.API.Services.Pos;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/pos-carts")]
[Authorize(Roles = "Owner,Admin,Manager,Cashier")]
public sealed class PosCartsController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private readonly IPosCartRuntimeService _cartRuntime;

    public PosCartsController(IPosCartRuntimeService cartRuntime)
    {
        _cartRuntime = cartRuntime;
    }

    [HttpPost]
    public async Task<ActionResult<PosCartResponse>> Create(
        CreatePosCartRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveCashierId(out var cashierId))
        {
            return Unauthorized(new { message = "Authenticated cashier identity is required." });
        }

        var idempotency = ResolveIdempotencyKey();
        if (idempotency == null)
        {
            return BadRequest(new { message = "Idempotency-Key header is required." });
        }

        return ToActionResult(await _cartRuntime.CreateAsync(request, cashierId, idempotency, cancellationToken));
    }

    [HttpGet("{cartId:guid}")]
    public async Task<ActionResult<PosCartResponse>> Get(Guid cartId, CancellationToken cancellationToken)
    {
        if (!TryResolveCashierId(out var cashierId))
        {
            return Unauthorized(new { message = "Authenticated cashier identity is required." });
        }

        return ToActionResult(await _cartRuntime.GetAsync(cartId, cashierId, cancellationToken));
    }

    [HttpPost("{cartId:guid}/items")]
    public async Task<ActionResult<PosCartResponse>> AddItem(
        Guid cartId,
        UpsertPosCartItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveCashierId(out var cashierId))
        {
            return Unauthorized(new { message = "Authenticated cashier identity is required." });
        }

        var idempotency = ResolveIdempotencyKey();
        if (idempotency == null)
        {
            return BadRequest(new { message = "Idempotency-Key header is required." });
        }

        return ToActionResult(await _cartRuntime.AddItemAsync(cartId, request, cashierId, idempotency, cancellationToken));
    }

    [HttpPut("{cartId:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<PosCartResponse>> UpdateItem(
        Guid cartId,
        Guid itemId,
        UpdatePosCartItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveCashierId(out var cashierId))
        {
            return Unauthorized(new { message = "Authenticated cashier identity is required." });
        }

        var idempotency = ResolveIdempotencyKey();
        if (idempotency == null)
        {
            return BadRequest(new { message = "Idempotency-Key header is required." });
        }

        return ToActionResult(await _cartRuntime.UpdateItemAsync(cartId, itemId, request, cashierId, idempotency, cancellationToken));
    }

    [HttpDelete("{cartId:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<PosCartResponse>> RemoveItem(
        Guid cartId,
        Guid itemId,
        [FromBody] PosCartLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveCashierId(out var cashierId))
        {
            return Unauthorized(new { message = "Authenticated cashier identity is required." });
        }

        var idempotency = ResolveIdempotencyKey();
        if (idempotency == null)
        {
            return BadRequest(new { message = "Idempotency-Key header is required." });
        }

        return ToActionResult(await _cartRuntime.RemoveItemAsync(cartId, itemId, request, cashierId, idempotency, cancellationToken));
    }

    [HttpPost("{cartId:guid}/hold")]
    public Task<ActionResult<PosCartResponse>> Hold(Guid cartId, PosCartLifecycleRequest request, CancellationToken cancellationToken) =>
        Transition(cartId, "Hold", request, cancellationToken);

    [HttpPost("{cartId:guid}/resume")]
    public Task<ActionResult<PosCartResponse>> Resume(Guid cartId, PosCartLifecycleRequest request, CancellationToken cancellationToken) =>
        Transition(cartId, "Resume", request, cancellationToken);

    [HttpPost("{cartId:guid}/cancel")]
    public Task<ActionResult<PosCartResponse>> Cancel(Guid cartId, PosCartLifecycleRequest request, CancellationToken cancellationToken) =>
        Transition(cartId, "Cancel", request, cancellationToken);

    [HttpPost("{cartId:guid}/expire")]
    public Task<ActionResult<PosCartResponse>> Expire(Guid cartId, PosCartLifecycleRequest request, CancellationToken cancellationToken) =>
        Transition(cartId, "Expire", request, cancellationToken);

    [HttpPost("{cartId:guid}/recover")]
    public Task<ActionResult<PosCartResponse>> Recover(Guid cartId, PosCartLifecycleRequest request, CancellationToken cancellationToken) =>
        Transition(cartId, "Recover", request, cancellationToken);

    [HttpPost("{cartId:guid}/abandon")]
    public Task<ActionResult<PosCartResponse>> Abandon(Guid cartId, PosCartLifecycleRequest request, CancellationToken cancellationToken) =>
        Transition(cartId, "Abandon", request, cancellationToken);

    private async Task<ActionResult<PosCartResponse>> Transition(
        Guid cartId,
        string transition,
        PosCartLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveCashierId(out var cashierId))
        {
            return Unauthorized(new { message = "Authenticated cashier identity is required." });
        }

        var idempotency = ResolveIdempotencyKey();
        if (idempotency == null)
        {
            return BadRequest(new { message = "Idempotency-Key header is required." });
        }

        return ToActionResult(await _cartRuntime.TransitionAsync(cartId, transition, request, cashierId, idempotency, cancellationToken));
    }

    private ActionResult<PosCartResponse> ToActionResult(PosCartServiceResult<PosCartResponse> result)
    {
        if (result.Value != null)
        {
            return StatusCode(result.StatusCode, result.Value);
        }

        return StatusCode(result.StatusCode, new { message = result.Message });
    }

    private string? ResolveIdempotencyKey()
    {
        var value = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private bool TryResolveCashierId(out Guid cashierId)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                  User.FindFirstValue("sub") ??
                  User.FindFirstValue("userId");
        return Guid.TryParse(raw, out cashierId) && cashierId != Guid.Empty;
    }
}

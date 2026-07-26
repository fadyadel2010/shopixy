using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.PosCart;

namespace PointOfSale.API.Services.Pos;

public interface IPosCartRuntimeService
{
    Task<PosCartServiceResult<PosCartResponse>> CreateAsync(
        CreatePosCartRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PosCartServiceResult<PosCartResponse>> GetAsync(
        Guid cartId,
        Guid cashierId,
        CancellationToken cancellationToken = default);

    Task<PosCartServiceResult<PosCartResponse>> AddItemAsync(
        Guid cartId,
        UpsertPosCartItemRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PosCartServiceResult<PosCartResponse>> UpdateItemAsync(
        Guid cartId,
        Guid itemId,
        UpdatePosCartItemRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PosCartServiceResult<PosCartResponse>> RemoveItemAsync(
        Guid cartId,
        Guid itemId,
        PosCartLifecycleRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PosCartServiceResult<PosCartResponse>> TransitionAsync(
        Guid cartId,
        string transition,
        PosCartLifecycleRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PosCartCheckoutPreparation> PrepareCheckoutAsync(
        Guid cartId,
        CreateSaleRequest request,
        Guid cashierId,
        CancellationToken cancellationToken = default);

    Task CompleteFromSaleAsync(
        Guid cartId,
        Guid saleId,
        Guid cashierId,
        CancellationToken cancellationToken = default);
}

public sealed record PosCartServiceResult<T>(int StatusCode, T? Value, string? Message = null)
{
    public static PosCartServiceResult<T> Ok(T value) => new(StatusCodes.Status200OK, value);
    public static PosCartServiceResult<T> Created(T value) => new(StatusCodes.Status201Created, value);
    public static PosCartServiceResult<T> BadRequest(string message) => new(StatusCodes.Status400BadRequest, default, message);
    public static PosCartServiceResult<T> NotFound(string message) => new(StatusCodes.Status404NotFound, default, message);
    public static PosCartServiceResult<T> Conflict(string message) => new(StatusCodes.Status409Conflict, default, message);
}

public sealed record PosCartCheckoutPreparation(
    bool Succeeded,
    CreateSaleRequest Request,
    int StatusCode = StatusCodes.Status200OK,
    string? Message = null)
{
    public static PosCartCheckoutPreparation Success(CreateSaleRequest request) => new(true, request);
    public static PosCartCheckoutPreparation Fail(int statusCode, string message, CreateSaleRequest request) =>
        new(false, request, statusCode, message);
}

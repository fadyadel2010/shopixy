using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.Router;

/// <summary>
/// Model router interface.
/// Routes requests to available models with fallback logic.
/// 
/// PHASE 2 RULES:
/// - Router decides which model to use
/// - Router handles fallback when models fail
/// - Router NEVER exposes model identity to customer
/// - Customer experience remains identical regardless of model
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Route a request to the best available model.
    /// Will automatically fallback to lower-priority models if primary fails.
    /// </summary>
    /// <param name="context">Commerce context with all data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chat response (identical regardless of which model was used)</returns>
    Task<ChatResponse> RouteAsync(
        CommerceContext context,
        CancellationToken cancellationToken = default);
}

using System.Threading;
using System.Threading.Tasks;
using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI;

/// <summary>
/// Routes messages to the AI provider and processes responses.
/// Model-agnostic - works with any LLM that supports chat completions.
/// </summary>
public interface IAIRouterService
{
    /// <summary>
    /// Process a customer message with the given commerce context.
    /// </summary>
    /// <param name="context">Complete commerce context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Structured, validated response</returns>
    Task<ChatResponse> ProcessAsync(
        CommerceContext context,
        CancellationToken cancellationToken = default);
}

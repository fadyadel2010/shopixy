using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

/// <summary>
/// Circuit Breaker for WhatsApp messaging with full state machine.
/// States: Closed -> Open -> HalfOpen -> (Success: Closed, Failure: Open)
/// </summary>
public class WhatsAppCircuitBreaker : IWhatsAppCircuitBreaker
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<WhatsAppCircuitBreaker> _logger;
    
    // Configuration constants
    private const int FailureThreshold = 5;
    private const int OpenStateDurationSeconds = 60;
    private const int HalfOpenTestRequests = 2; // Number of test requests in half-open before closing
    
    public WhatsAppCircuitBreaker(
        IDistributedCache cache,
        ILogger<WhatsAppCircuitBreaker> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Check if circuit is open for an account.
    /// IMPORTANT: Fails SAFE - if Redis is unavailable, treats circuit as OPEN to prevent message flood.
    /// </summary>
    public async Task<bool> IsOpenAsync(Guid accountId)
    {
        try
        {
            var state = await GetStateAsync(accountId);
            
            if (state == CircuitState.OPEN)
            {
                // Check if it's time to transition to half-open
                var openKey = GetOpenKey(accountId);
                var openTime = await _cache.GetStringAsync(openKey);
                
                if (string.IsNullOrEmpty(openTime))
                {
                    // Open state expired, transition to half-open
                    await SetStateAsync(accountId, CircuitState.HALFOPEN);
                    _logger.LogInformation("Circuit Breaker transitioning to HALF-OPEN for Account {AccountId}", accountId);
                    return false; // Allow test request
                }
                
                return true; // Still open
            }
            
            return false; // Closed or HalfOpen allows requests
        }
        catch (Exception ex)
        {
            // FAIL-SAFE: If Redis is unavailable, treat circuit as OPEN to prevent message flood
            // This is intentional - we prefer to block messages than potentially flood WhatsApp
            // during an infrastructure outage which could result in bans.
            _logger.LogError(ex, 
                "Circuit Breaker Redis check failed for Account {AccountId}. FAILING SAFE (treating as OPEN)", 
                accountId);
            return true; // Fail safe - block the message
        }
    }

    public async Task RecordSuccessAsync(Guid accountId)
    {
        var state = await GetStateAsync(accountId);
        
        if (state == CircuitState.HALFOPEN)
        {
            // Increment success counter
            var successKey = GetHalfOpenSuccessKey(accountId);
            var currentStr = await _cache.GetStringAsync(successKey);
            int current = 0;
            if (!string.IsNullOrEmpty(currentStr))
            {
                int.TryParse(currentStr, out current);
            }
            
            current++;
            
            if (current >= HalfOpenTestRequests)
            {
                // Enough successful tests, close the circuit
                await CloseCircuitAsync(accountId);
            }
            else
            {
                await _cache.SetStringAsync(successKey, current.ToString(), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
        }
        else
        {
            // Reset failure count on success in closed state
            var failureKey = GetFailureCountKey(accountId);
            await _cache.RemoveAsync(failureKey);
        }
    }

    public async Task RecordFailureAsync(Guid accountId)
    {
        var state = await GetStateAsync(accountId);
        
        if (state == CircuitState.HALFOPEN)
        {
            // Failed during half-open, go back to open
            await OpenCircuitAsync(accountId);
            _logger.LogWarning("Circuit Breaker REOPENED (from HALF-OPEN) for Account {AccountId}", accountId);
            return;
        }
        
        // Normal closed state - count failures
        var failureKey = GetFailureCountKey(accountId);
        var currentStr = await _cache.GetStringAsync(failureKey);
        int current = 0;
        if (!string.IsNullOrEmpty(currentStr))
        {
            int.TryParse(currentStr, out current);
        }
        
        current++;
        
        if (current >= FailureThreshold)
        {
            await OpenCircuitAsync(accountId);
            await _cache.RemoveAsync(failureKey);
        }
        else
        {
            await _cache.SetStringAsync(failureKey, current.ToString(), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            });
        }
    }
    
    private async Task OpenCircuitAsync(Guid accountId)
    {
        var openKey = GetOpenKey(accountId);
        _logger.LogWarning("Circuit Breaker OPENED for Account {AccountId} due to failures", accountId);
        
        await _cache.SetStringAsync(openKey, DateTime.UtcNow.ToString("O"), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(OpenStateDurationSeconds)
        });
        
        await SetStateAsync(accountId, CircuitState.OPEN);
        
        // Clear half-open success counter
        await _cache.RemoveAsync(GetHalfOpenSuccessKey(accountId));
    }
    
    private async Task CloseCircuitAsync(Guid accountId)
    {
        _logger.LogInformation("Circuit Breaker CLOSED for Account {AccountId} after successful tests", accountId);
        
        await SetStateAsync(accountId, CircuitState.CLOSED);
        await _cache.RemoveAsync(GetOpenKey(accountId));
        await _cache.RemoveAsync(GetHalfOpenSuccessKey(accountId));
        await _cache.RemoveAsync(GetFailureCountKey(accountId));
    }
    
    private async Task<CircuitState> GetStateAsync(Guid accountId)
    {
        var stateKey = GetStateKey(accountId);
        var stateStr = await _cache.GetStringAsync(stateKey);
        
        if (string.IsNullOrEmpty(stateStr))
            return CircuitState.CLOSED;
        
        return Enum.TryParse<CircuitState>(stateStr, out var state) ? state : CircuitState.CLOSED;
    }
    
    private async Task SetStateAsync(Guid accountId, CircuitState state)
    {
        var stateKey = GetStateKey(accountId);
        
        if (state == CircuitState.CLOSED)
        {
            await _cache.RemoveAsync(stateKey);
        }
        else
        {
            await _cache.SetStringAsync(stateKey, state.ToString(), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });
        }
    }

    private string GetStateKey(Guid accountId) => $"whatsapp:circuit:state:{accountId}";
    private string GetFailureCountKey(Guid accountId) => $"whatsapp:circuit:failures:{accountId}";
    private string GetOpenKey(Guid accountId) => $"whatsapp:circuit:open:{accountId}";
    private string GetHalfOpenSuccessKey(Guid accountId) => $"whatsapp:circuit:halfopen:{accountId}";
    
    private enum CircuitState
    {
        CLOSED,
        OPEN,
        HALFOPEN
    }
}


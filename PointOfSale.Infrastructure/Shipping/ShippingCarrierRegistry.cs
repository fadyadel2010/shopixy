using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>
/// Registry of all available shipping carrier adapters.
/// 
/// ARCHITECTURAL RULES:
/// - Adapters are registered at startup via DI
/// - Registry returns adapter by carrier code
/// - Registry never creates adapters, only resolves them
/// 
/// Adding a new carrier:
/// 1. Create adapter implementing IShippingCarrierAdapter
/// 2. Register adapter with DI: services.AddSingleton<IShippingCarrierAdapter, MyAdapter>()
/// 3. Registry automatically discovers it
/// </summary>
public interface IShippingCarrierRegistry
{
    /// <summary>
    /// Get adapter by carrier code.
    /// Returns null if carrier not found.
    /// </summary>
    IShippingCarrierAdapter? GetAdapter(string carrierCode);
    
    /// <summary>
    /// Get all registered adapters.
    /// </summary>
    IReadOnlyList<IShippingCarrierAdapter> GetAllAdapters();
    
    /// <summary>
    /// Check if carrier is registered.
    /// </summary>
    bool IsCarrierSupported(string carrierCode);
    
    /// <summary>
    /// Get list of all supported carrier codes.
    /// </summary>
    IReadOnlyList<string> GetSupportedCarrierCodes();
}

/// <summary>
/// Implementation of carrier registry.
/// </summary>
public class ShippingCarrierRegistry : IShippingCarrierRegistry
{
    private readonly Dictionary<string, IShippingCarrierAdapter> _adapters;
    private readonly ILogger<ShippingCarrierRegistry> _logger;
    
    public ShippingCarrierRegistry(
        IEnumerable<IShippingCarrierAdapter> adapters,
        ILogger<ShippingCarrierRegistry> logger)
    {
        _logger = logger;
        _adapters = new Dictionary<string, IShippingCarrierAdapter>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var adapter in adapters)
        {
            if (_adapters.ContainsKey(adapter.CarrierCode))
            {
                _logger.LogWarning(
                    "Duplicate carrier adapter registration for {CarrierCode}. Keeping first.", 
                    adapter.CarrierCode);
                continue;
            }
            
            _adapters[adapter.CarrierCode] = adapter;
            _logger.LogInformation(
                "Registered shipping carrier adapter: {CarrierCode} ({DisplayName})",
                adapter.CarrierCode,
                adapter.DisplayName);
        }
        
        _logger.LogInformation(
            "Shipping carrier registry initialized with {Count} adapters: {Carriers}",
            _adapters.Count,
            string.Join(", ", _adapters.Keys));
    }
    
    public IShippingCarrierAdapter? GetAdapter(string carrierCode)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
            return null;
            
        _adapters.TryGetValue(carrierCode.ToLowerInvariant(), out var adapter);
        return adapter;
    }
    
    public IReadOnlyList<IShippingCarrierAdapter> GetAllAdapters()
    {
        return _adapters.Values.ToList().AsReadOnly();
    }
    
    public bool IsCarrierSupported(string carrierCode)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
            return false;
            
        return _adapters.ContainsKey(carrierCode.ToLowerInvariant());
    }
    
    public IReadOnlyList<string> GetSupportedCarrierCodes()
    {
        return _adapters.Keys.ToList().AsReadOnly();
    }
}

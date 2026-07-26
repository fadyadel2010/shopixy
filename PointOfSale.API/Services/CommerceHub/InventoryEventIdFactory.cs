using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.API.Services.CommerceHub;

public static class InventoryEventIdFactory
{
    public static Guid Create(params object?[] parts)
    {
        var payload = string.Join("|", parts.Select(part => part?.ToString() ?? string.Empty));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        Array.Resize(ref hash, 16);
        return new Guid(hash);
    }
}

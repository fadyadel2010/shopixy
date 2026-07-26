using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Core.Payments.Runtime.Domain;

internal static class PaymentRuntimeDeterminism
{
    public static Guid CreateId(string scope, params string[] components)
    {
        var source = string.Join('|', new[] { scope }.Concat(components));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var bytes = hash.Take(16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

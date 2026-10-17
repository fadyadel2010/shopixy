namespace PointOfSale.Infrastructure.Shipping;

// Like the other source-specific mutation scopes, this is internal server
// authority, never an HTTP/body field or a general shipment mutation permission.
internal static class CancellationPersistenceAuthority
{
    private static readonly AsyncLocal<int> Depth = new();
    internal static bool IsActive => Depth.Value > 0;
    internal static IDisposable Begin() { Depth.Value++; return new Scope(); }
    private sealed class Scope : IDisposable { public void Dispose() => Depth.Value--; }
}

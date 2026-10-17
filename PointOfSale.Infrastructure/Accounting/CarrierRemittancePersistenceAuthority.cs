namespace PointOfSale.Infrastructure.Accounting;

internal static class CarrierRemittancePersistenceAuthority
{
    private static readonly AsyncLocal<int> Depth = new();
    internal static bool IsActive => Depth.Value > 0;
    internal static IDisposable Begin() { Depth.Value++; return new Scope(); }
    private sealed class Scope : IDisposable { public void Dispose() => Depth.Value--; }
}

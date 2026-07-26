using System.Threading;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Restricts ledger balance and reversal-state persistence to the posting engine.
/// </summary>
public static class LedgerMutationScope
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool IsAuthorized => Depth.Value > 0;

    public static IDisposable Authorize()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Depth.Value = Math.Max(0, Depth.Value - 1);
            _disposed = true;
        }
    }
}

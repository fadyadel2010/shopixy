namespace PointOfSale.Infrastructure.Data;

public static class UserGovernanceMutationScope
{
    private static readonly AsyncLocal<int> CertificationDepth = new();

    public static bool IsCertificationMutationAuthorized => CertificationDepth.Value > 0;

    public static IDisposable BeginCertificationMutation()
    {
        CertificationDepth.Value++;
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

            CertificationDepth.Value = Math.Max(0, CertificationDepth.Value - 1);
            _disposed = true;
        }
    }
}

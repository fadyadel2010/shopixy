using Microsoft.AspNetCore.DataProtection;

namespace PointOfSale.Tests.Infrastructure;

internal static class TestDataProtectionProviderFactory
{
    private static readonly Lazy<IDataProtectionProvider> Provider = new(
        () =>
        {
            var keysPath = TestConfiguration.GetDataProtectionKeysPath();
            var applicationName = TestConfiguration.GetDataProtectionApplicationName();

            if (string.IsNullOrWhiteSpace(keysPath))
            {
                throw new InvalidOperationException(
                    "Data protection keys path is not configured. Ensure TestConfiguration.GetDataProtectionKeysPath() returns a valid path.");
            }

            if (string.IsNullOrWhiteSpace(applicationName))
            {
                throw new InvalidOperationException(
                    "Data protection application name is not configured. Ensure TestConfiguration.GetDataProtectionApplicationName() returns a valid name.");
            }

            return DataProtectionProvider.Create(
                new DirectoryInfo(keysPath),
                builder => builder.SetApplicationName(applicationName));
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IDataProtectionProvider CreateProvider() => Provider.Value;
}

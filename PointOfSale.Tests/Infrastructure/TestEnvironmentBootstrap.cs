using System.Globalization;
using System.Runtime.CompilerServices;

namespace PointOfSale.Tests.Infrastructure;

internal static class TestEnvironmentBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        Directory.CreateDirectory(TestConfiguration.GetSharedTempRoot());
        Directory.CreateDirectory(TestConfiguration.GetDataProtectionKeysPath());
    }
}

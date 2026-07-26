namespace PointOfSale.Tests.Infrastructure;

internal static class RealPostgresTestSupport
{
    public const string RequireRealPostgresEnvVar = "REQUIRE_REAL_POSTGRES_TESTS";

    public static bool TryCreateScope(
        out TestDatabaseScope? databaseScope,
        out Guid tenantId,
        out TestTenantContext? tenantContext)
    {
        databaseScope = null;
        tenantId = Guid.Empty;
        tenantContext = null;

        try
        {
            databaseScope = TestDatabaseScope.Open();
            tenantId = Guid.NewGuid();
            tenantContext = new TestTenantContext(tenantId);
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionStrings:DefaultConnection", StringComparison.Ordinal))
        {
            if (RequiresRealPostgres())
            {
                throw new InvalidOperationException(
                    "Real PostgreSQL validation was required, but no ConnectionStrings:DefaultConnection value was available for the test harness.",
                    ex);
            }

            return false;
        }
    }

    public static bool RequiresRealPostgres()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(RequireRealPostgresEnvVar),
            "1",
            StringComparison.Ordinal);
    }
}

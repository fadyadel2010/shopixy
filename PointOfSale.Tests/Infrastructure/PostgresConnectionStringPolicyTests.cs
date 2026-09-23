using Npgsql;
using PointOfSale.Infrastructure.Configuration;

namespace PointOfSale.Tests.Infrastructure;

public sealed class PostgresConnectionStringPolicyTests
{
    [Fact]
    public void NormalizeForHealthChecks_DisablesPoolingForOneShotProbe()
    {
        var normalized = PostgresConnectionStringPolicy.NormalizeForHealthChecks(
            "Host=localhost;Database=pointofsale;Username=test;Password=test",
            isDevelopment: false);

        var builder = new NpgsqlConnectionStringBuilder(normalized);

        Assert.False(builder.Pooling);
        Assert.Equal(5, builder.Timeout);
        Assert.Equal(5, builder.CommandTimeout);
    }

    [Fact]
    public void Normalize_PreservesApplicationPooling()
    {
        var normalized = PostgresConnectionStringPolicy.Normalize(
            "Host=localhost;Database=pointofsale;Username=test;Password=test",
            isDevelopment: false);

        Assert.True(new NpgsqlConnectionStringBuilder(normalized).Pooling);
    }
}

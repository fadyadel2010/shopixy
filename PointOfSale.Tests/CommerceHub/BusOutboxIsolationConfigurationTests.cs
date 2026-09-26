using System.IO;

namespace PointOfSale.Tests.CommerceHub;

public sealed class BusOutboxIsolationConfigurationTests
{
    [Fact]
    public void BusOutboxUsesReadCommittedIsolation()
    {
        var source = ReadRepoFile("PointOfSale.API/Program.cs");
        const string marker = "AddEntityFrameworkOutbox<ApplicationDbContext>";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("});", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var block = source[start..end];

        Assert.Contains("UsePostgres()", block, StringComparison.Ordinal);
        Assert.Contains("UseBusOutbox()", block, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.ReadCommitted", block, StringComparison.Ordinal);
        Assert.DoesNotContain("IsolationLevel.Serializable", block, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}

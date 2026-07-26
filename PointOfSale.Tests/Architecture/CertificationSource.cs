namespace PointOfSale.Tests.Architecture;

internal static class CertificationSource
{
    public static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("POINT_OF_SALE_SOLUTION_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) &&
            File.Exists(Path.Combine(configuredRoot, "PointOfSale.sln")))
        {
            return configuredRoot;
        }

        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}

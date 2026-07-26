using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PointOfSale.Tests.Infrastructure;

internal static class TestConfiguration
{
    private static readonly Lazy<string> SolutionRoot = new(ResolveSolutionRoot, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<JsonDocument> Settings = new(LoadSettings, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string GetSolutionRoot() => SolutionRoot.Value;

    public static string GetRequiredConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString.Trim();
        }

        return GetRequiredJsonString("ConnectionStrings", "DefaultConnection");
    }

    public static string GetDatabasePrefix()
    {
        var configured = Environment.GetEnvironmentVariable("Testing__DatabasePrefix")
            ?? TryGetJsonString("Testing", "DatabasePrefix");

        return string.IsNullOrWhiteSpace(configured)
            ? "pointofsale_tests"
            : configured.Trim();
    }

    public static string GetSharedTempRoot()
    {
        var configured = Environment.GetEnvironmentVariable("Testing__TempRoot")
            ?? TryGetJsonString("Testing", "TempRoot");

        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(GetSolutionRoot(), "temp", "test-environment")
            : NormalizePath(configured);
    }

    public static string GetDataProtectionKeysPath()
    {
        var configured = Environment.GetEnvironmentVariable("DataProtection__KeysPath")
            ?? TryGetJsonString("DataProtection", "KeysPath");

        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(GetSharedTempRoot(), "data-protection-keys")
            : NormalizePath(configured);
    }

    public static string GetDataProtectionApplicationName()
    {
        var configured = Environment.GetEnvironmentVariable("DataProtection__ApplicationName")
            ?? TryGetJsonString("DataProtection", "ApplicationName");

        return string.IsNullOrWhiteSpace(configured)
            ? "PointOfSale.Test"
            : configured.Trim();
    }

    public static string BuildDeterministicSuffix(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private static JsonDocument LoadSettings()
    {
        var solutionRoot = GetSolutionRoot();
        var settingsPath = Path.Combine(solutionRoot, "PointOfSale.API", "appsettings.Test.json");
        var localSettingsPath = Path.Combine(solutionRoot, "PointOfSale.API", "appsettings.Test.local.json");
        
        var json = File.ReadAllText(settingsPath);
        using var baseSettings = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        // If local override exists, merge it (local values take precedence)
        if (File.Exists(localSettingsPath))
        {
            var localJson = File.ReadAllText(localSettingsPath);
            using var localSettings = JsonDocument.Parse(localJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            return MergeJsonDocuments(baseSettings, localSettings);
        }

        // Clone the baseSettings before disposing to return a new document
        return CloneJsonDocument(baseSettings);
    }

    private static JsonDocument CloneJsonDocument(JsonDocument source)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        
        source.RootElement.WriteTo(writer);
        writer.Flush();
        
        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }

    private static JsonDocument MergeJsonDocuments(JsonDocument baseDoc, JsonDocument overrideDoc)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        MergeJsonElements(writer, baseDoc.RootElement, overrideDoc.RootElement);
        writer.Flush();

        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }

    private static void MergeJsonElements(Utf8JsonWriter writer, JsonElement baseElement, JsonElement overrideElement)
    {
        if (overrideElement.ValueKind != JsonValueKind.Object || baseElement.ValueKind != JsonValueKind.Object)
        {
            overrideElement.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();

        var processedProperties = new HashSet<string>();

        // Write override properties (they take precedence)
        foreach (var property in overrideElement.EnumerateObject())
        {
            processedProperties.Add(property.Name);
            writer.WritePropertyName(property.Name);

            if (baseElement.TryGetProperty(property.Name, out var baseValue) &&
                property.Value.ValueKind == JsonValueKind.Object &&
                baseValue.ValueKind == JsonValueKind.Object)
            {
                MergeJsonElements(writer, baseValue, property.Value);
            }
            else
            {
                property.Value.WriteTo(writer);
            }
        }

        // Write base properties that weren't overridden
        foreach (var property in baseElement.EnumerateObject())
        {
            if (!processedProperties.Contains(property.Name))
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    private static string ResolveSolutionRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("POINT_OF_SALE_SOLUTION_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) &&
            File.Exists(Path.Combine(configuredRoot, "PointOfSale.sln")))
        {
            return configuredRoot;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PointOfSale.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the PointOfSale solution root for test configuration.");
    }

    private static string NormalizePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(GetSolutionRoot(), path));
    }

    private static string GetRequiredJsonString(params string[] path)
    {
        var value = TryGetJsonString(path);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Missing required test configuration value: {string.Join(':', path)}");
    }

    private static string? TryGetJsonString(params string[] path)
    {
        var current = Settings.Value.RootElement;

        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        // Only return a value for actual string types in JSON.
        // Non-string types (objects, arrays, numbers, booleans) return null
        // to avoid unexpected behavior from GetRawText() which includes quotes and raw JSON.
        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : null;
    }
}

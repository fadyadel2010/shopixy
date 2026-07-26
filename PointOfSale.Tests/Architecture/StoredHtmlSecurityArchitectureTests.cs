using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class StoredHtmlSecurityArchitectureTests
{
    private static readonly Regex DangerouslySetInnerHtmlRegex = new(
        @"dangerouslySetInnerHTML\s*=\s*\{\{\s*__html:\s*(?<expression>[^}]+)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InnerHtmlAssignmentRegex = new(
        @"innerHTML\s*=\s*(?<expression>[^\r\n;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Strict validation: requires sanitized-prefixed identifiers and rejects "unsanitized..."
    private static readonly Regex SanitizedIdentifierRegex = new(
        @"\bsanitized\w*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Strict validation: requires exact call pattern for sanitizeRichTextHtml
    private static readonly Regex SanitizeCallRegex = new(
        @"\bsanitizeRichTextHtml\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Approved sanitizer helper functions that wrap sanitizeRichTextHtml
    private static readonly string[] ApprovedSanitizerHelpers = new[]
    {
        "sanitizeRichTextHtml",
        "ensureRichText"
    };

    // Regex to match any approved sanitizer helper call
    private static readonly Regex ApprovedSanitizerCallRegex = new(
        @"\b(sanitizeRichTextHtml|ensureRichText)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void FrontendHtmlRenderSinks_MustUseSanitizedHtml()
    {
        var solutionRoot = GetSolutionRoot();
        var sourceFiles = EnumerateFrontendSourceFiles().ToArray();
        var violations = new List<string>();

        foreach (var file in sourceFiles)
        {
            var source = File.ReadAllText(file);

            foreach (Match match in DangerouslySetInnerHtmlRegex.Matches(source))
            {
                var expression = match.Groups["expression"].Value.Trim();
                if (IsSanitizedExpression(expression))
                {
                    continue;
                }

                violations.Add($"{Path.GetRelativePath(solutionRoot, file)} => {expression}");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Unsanitized HTML render sinks were found:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void FrontendInnerHtmlAssignments_MustUseSanitizedExpressions()
    {
        var solutionRoot = GetSolutionRoot();
        var sourceFiles = EnumerateFrontendSourceFiles().ToArray();
        var violations = new List<string>();

        foreach (var file in sourceFiles)
        {
            var source = File.ReadAllText(file);

            foreach (Match match in InnerHtmlAssignmentRegex.Matches(source))
            {
                var expression = match.Groups["expression"].Value.Trim();
                if (IsSanitizedExpression(expression))
                {
                    continue;
                }

                violations.Add($"{Path.GetRelativePath(solutionRoot, file)} => {expression}");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Unsanitized innerHTML assignments were found:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IEnumerable<string> EnumerateFrontendSourceFiles()
    {
        var solutionRoot = GetSolutionRoot();
        var frontendRoots = new[]
        {
            Path.Combine(solutionRoot, "PointOfSale.Storefront", "src"),
            Path.Combine(solutionRoot, "PointOfSale.Web", "src"),
        };

        return frontendRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(path =>
                path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validates that an expression uses sanitized HTML with strict token/regex-based checks.
    /// Prevents false negatives like "unsanitizedHtml" from bypassing the security gate.
    /// Supports both direct sanitizer calls and approved wrapper helpers.
    /// </summary>
    private static bool IsSanitizedExpression(string expression)
    {
        // Check for any approved sanitizer helper function call (sanitizeRichTextHtml, ensureRichText, etc.)
        if (ApprovedSanitizerCallRegex.IsMatch(expression))
        {
            return true;
        }

        // Check for "sanitized" identifier with word boundaries (prevents "unsanitized" from matching)
        if (SanitizedIdentifierRegex.IsMatch(expression))
        {
            return true;
        }

        return false;
    }

    private static string GetSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.GetFiles(dir, "*.sln").Any())
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new InvalidOperationException("Could not find solution root");
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

const string apiBase = "http://localhost:5010/api";
const string webBase = "http://localhost:5173";
const string tenantId = "c9c5c1ae-949e-498e-bc63-07242daeb49d";

var outputRoot = Path.Combine(Path.GetTempPath(), "opencode", "purchase-return-browser-evidence");
Directory.CreateDirectory(outputRoot);

using var client = new HttpClient();
using var tokenResponse = await client.PostAsJsonAsync($"{apiBase}/test-auth/token", new { tenantId });
tokenResponse.EnsureSuccessStatusCode();
using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
var token = tokenDocument.RootElement.GetProperty("token").GetString()
    ?? throw new InvalidOperationException("Test token response did not contain a token.");

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Channel = "msedge",
    Headless = true,
});

var results = new List<BrowserResult>();
foreach (var viewport in new[]
         {
             new Viewport("desktop", 1440, 1000),
             new Viewport("mobile", 390, 844),
         })
{
    var apiRequests = new List<string>();
    var blockedMutations = new List<string>();
    var failedResponses = new List<string>();
    var requestFailures = new List<string>();
    var consoleErrors = new List<string>();
    var assertions = new Dictionary<string, bool>();

    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = viewport.Width, Height = viewport.Height },
        Locale = "en-US",
    });
    await context.AddInitScriptAsync("localStorage.setItem('i18nextLng', 'en');");
    await context.RouteAsync("**/api/**", async route =>
    {
        var method = route.Request.Method.ToUpperInvariant();
        if (method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            blockedMutations.Add($"{method} {SafeUrl(route.Request.Url)}");
            await route.AbortAsync();
            return;
        }

        var headers = new Dictionary<string, string>(route.Request.Headers, StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Bearer {token}",
        };
        await route.ContinueAsync(new RouteContinueOptions { Headers = headers });
    });

    var page = await context.NewPageAsync();
    page.Request += (_, request) =>
    {
        if (request.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase))
            apiRequests.Add($"{request.Method} {SafeUrl(request.Url)}");
    };
    page.RequestFailed += (_, request) => requestFailures.Add($"{request.Method} {SafeUrl(request.Url)}: {request.Failure}");
    page.Response += (_, response) =>
    {
        if (response.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase) && response.Status >= 400)
            failedResponses.Add($"{response.Status} {SafeUrl(response.Url)}");
    };
    page.Console += (_, message) =>
    {
        if (message.Type == "error") consoleErrors.Add(message.Text);
    };
    page.PageError += (_, error) => consoleErrors.Add(error);

    await page.GotoAsync($"{webBase}/purchase-returns", new PageGotoOptions
    {
        WaitUntil = WaitUntilState.DOMContentLoaded,
        Timeout = 120_000,
    });
    await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Purchase Returns" })
        .WaitForAsync(new LocatorWaitForOptions { Timeout = 120_000 });
    await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Purchase Return" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "New Purchase Return" })
        .WaitForAsync(new LocatorWaitForOptions { Timeout = 120_000 });

    var body = await page.Locator("body").InnerTextAsync();
    var radios = page.Locator("input[name=purchaseReturnSettlement]");
    assertions["authenticatedRoute"] = !page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase);
    assertions["twoSettlementChoices"] = await radios.CountAsync() == 2;
    assertions["accountCreditRendered"] = body.Contains("Credit Supplier Account", StringComparison.OrdinalIgnoreCase);
    assertions["cashRefundRendered"] = body.Contains("Refund to Cash", StringComparison.OrdinalIgnoreCase);
    assertions["cashboxEffectRendered"] = body.Contains("cashbox", StringComparison.OrdinalIgnoreCase);
    assertions["accountCreditDefault"] = await radios.Nth(0).IsCheckedAsync() && !await radios.Nth(1).IsCheckedAsync();
    await radios.Nth(1).CheckAsync();
    assertions["cashChoiceSelectable"] = !await radios.Nth(0).IsCheckedAsync() && await radios.Nth(1).IsCheckedAsync();
    assertions["noBusinessMutation"] = blockedMutations.Count == 0;
    assertions["noFailedApiResponses"] = failedResponses.Count == 0;
    assertions["noRequestFailures"] = requestFailures.Count == 0;
    assertions["noUnexpectedConsoleErrors"] = consoleErrors.All(IsKnownConsoleDiagnostic);

    var screenshotPath = Path.Combine(outputRoot, $"purchase-return-{viewport.Name}.png");
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
    results.Add(new BrowserResult(
        viewport.Name,
        $"{viewport.Width}x{viewport.Height}",
        assertions,
        apiRequests,
        blockedMutations,
        failedResponses,
        requestFailures,
        consoleErrors,
        screenshotPath));
}

var evidencePath = Path.Combine(outputRoot, "browser-evidence.json");
await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new
{
    generatedAtUtc = DateTimeOffset.UtcNow,
    tenantId,
    results,
}, new JsonSerializerOptions { WriteIndented = true }));

var passed = results.Count(result => result.Passed);
Console.WriteLine(JsonSerializer.Serialize(new
{
    evidencePath,
    cases = results.Count,
    passed,
    failed = results.Count - passed,
    results = results.Select(result => new { result.Viewport, result.Size, result.Passed, result.Assertions }),
}, new JsonSerializerOptions { WriteIndented = true }));

if (passed != results.Count) Environment.ExitCode = 1;

static string SafeUrl(string value)
{
    var uri = new Uri(value);
    return uri.GetLeftPart(UriPartial.Path);
}

static bool IsKnownConsoleDiagnostic(string value) =>
    value.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase)
    && value.Contains("meta", StringComparison.OrdinalIgnoreCase);

sealed record Viewport(string Name, int Width, int Height);

sealed record BrowserResult(
    string Viewport,
    string Size,
    IReadOnlyDictionary<string, bool> Assertions,
    IReadOnlyList<string> ApiRequests,
    IReadOnlyList<string> BlockedMutations,
    IReadOnlyList<string> FailedResponses,
    IReadOnlyList<string> RequestFailures,
    IReadOnlyList<string> ConsoleErrors,
    string ScreenshotPath)
{
    public bool Passed => Assertions.Values.All(value => value);
}

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

const string apiBase = "http://localhost:5010/api";
const string webBase = "http://localhost:5173";
const string tenantId = "c43aebf2-4cc4-406a-a2db-4df06d40bce5";
const string expectedCash = "5,234.84";
const string expectedClearing = "14.00";

using var client = new HttpClient();
using var tokenResponse = await client.PostAsJsonAsync(
    $"{apiBase}/test-auth/token",
    new { tenantId });
tokenResponse.EnsureSuccessStatusCode();
using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
var token = tokenDocument.RootElement.GetProperty("token").GetString()
    ?? throw new InvalidOperationException("Test token response did not contain a token.");

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Channel = "msedge",
    Headless = true
});

var results = new List<object>();
foreach (var viewport in new[]
         {
             new { Name = "desktop", Width = 1440, Height = 1000 },
             new { Name = "mobile", Width = 390, Height = 844 }
         })
{
    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize
        {
            Width = viewport.Width,
            Height = viewport.Height
        },
        Locale = "en-US"
    });

    await context.RouteAsync("**/api/**", async route =>
    {
        var headers = new Dictionary<string, string>(route.Request.Headers, StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Bearer {token}"
        };
        await route.ContinueAsync(new RouteContinueOptions { Headers = headers });
    });

    var page = await context.NewPageAsync();
    var consoleErrors = new List<string>();
    var failedResponses = new List<string>();
    var apiRequests = new List<string>();
    page.Request += (_, request) =>
    {
        if (request.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase))
        {
            apiRequests.Add(request.Url);
        }
    };
    page.Console += (_, message) =>
    {
        if (message.Type == "error")
        {
            consoleErrors.Add(message.Text);
        }
    };
    page.PageError += (_, error) => consoleErrors.Add(error);
    page.Response += (_, response) =>
    {
        if (response.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase) && response.Status >= 400)
        {
            failedResponses.Add($"{response.Status} {response.Url}");
        }
    };

    var cashResponseSource = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
    page.Response += (_, response) =>
    {
        if (response.Url.Contains("/api/financialreports/cash-flow", StringComparison.OrdinalIgnoreCase))
        {
            cashResponseSource.TrySetResult(response);
        }
    };

    await page.GotoAsync($"{webBase}/treasury/cash-flow", new PageGotoOptions
    {
        WaitUntil = WaitUntilState.DOMContentLoaded,
        Timeout = 120_000
    });
    var observedCashResponse = await Task.WhenAny(
        cashResponseSource.Task,
        Task.Delay(TimeSpan.FromSeconds(30)));
    if (observedCashResponse != cashResponseSource.Task)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            viewport = viewport.Name,
            failure = "Cash Flow API request was not observed.",
            page.Url,
            body = await page.Locator("body").InnerTextAsync(),
            apiRequests,
            consoleErrors,
            failedResponses
        }, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    var cashResponse = await cashResponseSource.Task;
    await page.WaitForFunctionAsync(
        "expected => document.body.innerText.includes(expected)",
        expectedCash,
        new PageWaitForFunctionOptions { Timeout = 120_000 });
    var cashBody = await page.Locator("body").InnerTextAsync();

    await page.GotoAsync($"{webBase}/treasury/hub", new PageGotoOptions
    {
        WaitUntil = WaitUntilState.DOMContentLoaded,
        Timeout = 120_000
    });
    await page.WaitForFunctionAsync(
        "expected => document.body.innerText.includes(expected.cash) && document.body.innerText.includes(expected.clearing)",
        new { cash = expectedCash, clearing = expectedClearing },
        new PageWaitForFunctionOptions { Timeout = 120_000 });
    var hubBody = await page.Locator("body").InnerTextAsync();

    results.Add(new
    {
        viewport = viewport.Name,
        size = $"{viewport.Width}x{viewport.Height}",
        cashFlowHttpStatus = cashResponse.Status,
        cashFlowRendered = cashBody.Contains("Cash Flow", StringComparison.OrdinalIgnoreCase),
        cashFlowReconciled = cashBody.Contains("RECONCILED", StringComparison.OrdinalIgnoreCase),
        cashFlowClosingRendered = cashBody.Contains(expectedCash, StringComparison.Ordinal),
        adjustmentRendered = cashBody.Contains("Ledger-derived non-cash settlement adjustment", StringComparison.Ordinal),
        hubCashAndBankRendered = hubBody.Contains("Total Cash & Bank", StringComparison.OrdinalIgnoreCase)
            && hubBody.Contains(expectedCash, StringComparison.Ordinal),
        hubClearingRendered = hubBody.Contains("Pending Clearing", StringComparison.OrdinalIgnoreCase)
            && hubBody.Contains(expectedClearing, StringComparison.Ordinal),
        finalUrl = page.Url,
        consoleErrors,
        failedResponses
    });
}

Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

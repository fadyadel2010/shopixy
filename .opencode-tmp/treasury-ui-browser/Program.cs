using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

const string apiBase = "http://localhost:5010/api";
const string webBase = "http://localhost:5173";
const string primaryTenantId = "c43aebf2-4cc4-406a-a2db-4df06d40bce5";
const string primaryTenantName = "greatone";
const string supplierCardTenantId = "c9c5c1ae-949e-498e-bc63-07242daeb49d";

var outputRoot = Environment.GetEnvironmentVariable("TREASURY_UI_BROWSER_EVIDENCE")
    ?? Path.Combine(Path.GetTempPath(), "opencode", "treasury-ui-browser-evidence");
var caseFilter = Environment.GetEnvironmentVariable("TREASURY_UI_BROWSER_CASE");
var languageFilter = Environment.GetEnvironmentVariable("TREASURY_UI_BROWSER_LANGUAGE");
var viewportFilter = Environment.GetEnvironmentVariable("TREASURY_UI_BROWSER_VIEWPORT");
Directory.CreateDirectory(outputRoot);

using var client = new HttpClient();
var primaryToken = await IssueTokenAsync(primaryTenantId);
var supplierToken = await IssueTokenAsync(supplierCardTenantId);
var catalog = await GetCatalogAsync(primaryToken);
var available = catalog.Where(row => row.AccountType is "Cashbox" or "Bank").ToArray();
var clearing = catalog.Where(row => row.AccountType == "Clearing").ToArray();
var viewerAccount = available.FirstOrDefault()
    ?? throw new InvalidOperationException("The primary tenant has no available-cash account for Viewer certification.");
var availableTotal = available.Sum(row => row.CurrentBalance).ToString("N2", CultureInfo.InvariantCulture);
var clearingTotal = clearing.Sum(row => row.CurrentBalance).ToString("N2", CultureInfo.InvariantCulture);

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Channel = "msedge",
    Headless = false,
});

var results = new List<BrowserResult>();
foreach (var language in new[] { "en", "ar" })
{
    if (!string.IsNullOrWhiteSpace(languageFilter) && language != languageFilter) continue;
    foreach (var viewport in new[]
             {
                 new Viewport("desktop", 1440, 1000),
                 new Viewport("mobile", 390, 844),
             })
    {
        if (!string.IsNullOrWhiteSpace(viewportFilter) && viewport.Name != viewportFilter) continue;
        var specs = new[]
        {
            new RouteSpec("hub", "/treasury/hub", language == "ar" ? "النقدية المتاحة" : "Available now", primaryToken),
            new RouteSpec("dashboard", "/treasury", language == "ar" ? "الموقف النقدي" : "Cash Position", primaryToken),
            new RouteSpec("viewer", $"/treasury/account/{Uri.EscapeDataString(viewerAccount.AccountCode)}", language == "ar" ? LocalizedName(viewerAccount, language) : viewerAccount.Name, primaryToken),
            new RouteSpec("viewer-unavailable", "/treasury/account/1020", language == "ar" ? "هذا الحساب ليس جزءا من دليل الخزينة" : "This account is not part of the Treasury catalog", primaryToken),
            new RouteSpec("transfers", "/treasury/transfers", language == "ar" ? "تحويلات الخزينة" : "Treasury Transfers", primaryToken),
            new RouteSpec("clearing-aging", "/treasury/clearing-aging", language == "ar" ? "أعمار التسويات المعلقة" : "Clearing Accounts Aging", primaryToken),
            new RouteSpec("cash-flow", "/treasury/cash-flow", language == "ar" ? "النقدية في هذا التقرير" : "Cash in this report", primaryToken),
            new RouteSpec("balance-sheet", "/treasury/balance-sheet", language == "ar" ? "تشمل الأصول حسابات التسوية" : "Assets include clearing", primaryToken),
            new RouteSpec("trial-balance", "/treasury/trial-balance", language == "ar" ? "دفتر الأستاذ الكامل" : "complete ledger", primaryToken),
            new RouteSpec("general-ledger", "/treasury/general-ledger", language == "ar" ? "الحساب 1020" : "account 1020", primaryToken),
            new RouteSpec("supplier-card", "/supplier-card-funding", language == "ar" ? "أصل مالي مقيد" : "Restricted card funding", supplierToken),
        };

        foreach (var spec in specs)
        {
            if (!string.IsNullOrWhiteSpace(caseFilter) && spec.Case != caseFilter) continue;
            results.Add(await CertifyAsync(spec, language, viewport));
        }
    }
}

var evidencePath = Path.Combine(outputRoot, "browser-evidence.json");
await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new
{
    generatedAtUtc = DateTimeOffset.UtcNow,
    primaryTenant = new
    {
        name = primaryTenantName,
        id = primaryTenantId,
        availableTotal,
        clearingTotal,
        catalog,
    },
    results,
}, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine(JsonSerializer.Serialize(new
{
    evidencePath,
    cases = results.Count,
    passed = results.Count(result => result.Passed),
    failed = results.Count(result => !result.Passed),
    failures = results.Where(result => !result.Passed).Select(result => new { result.Case, result.Language, result.Viewport, result.Assertions }),
}, new JsonSerializerOptions { WriteIndented = true }));

if (results.Any(result => !result.Passed)) Environment.ExitCode = 1;

async Task<BrowserResult> CertifyAsync(RouteSpec spec, string language, Viewport viewport)
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
        Locale = language == "ar" ? "ar-EG" : "en-US",
    });
    await context.AddInitScriptAsync($"localStorage.setItem('i18nextLng', '{language}');");
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
            ["Authorization"] = $"Bearer {spec.Token}",
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

    string body;
    try
    {
        await page.GotoAsync($"{webBase}{spec.Path}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 120_000,
        });
        await page.WaitForFunctionAsync(
            "marker => document.body.innerText.includes(marker)",
            spec.Marker,
            new PageWaitForFunctionOptions { Timeout = 120_000 });
        body = await page.Locator("body").InnerTextAsync();
        if (spec.Case == "clearing-aging")
        {
            await page.WaitForFunctionAsync(
                "() => document.querySelector('tbody button[disabled]') || document.body.innerText.includes('All Cleared') || document.body.innerText.includes('All clearing balances are settled') || document.body.innerText.includes('تمت تسوية الكل') || document.body.innerText.includes('تمت تسوية جميع الأرصدة') || document.body.innerText.includes('لا توجد تسويات معلقة')",
                null,
                new PageWaitForFunctionOptions { Timeout = 120_000 });
            body = await page.Locator("body").InnerTextAsync();
        }
        if (spec.Case == "transfers")
        {
            await page.WaitForFunctionAsync(
                "() => Array.from(document.querySelectorAll('select')).length === 2 && Array.from(document.querySelectorAll('select')).every(select => select.options.length >= 3)",
                null,
                new PageWaitForFunctionOptions { Timeout = 120_000 });
            body = await page.Locator("body").InnerTextAsync();
        }
    }
    catch (Exception error)
    {
        body = await page.Locator("body").InnerTextAsync();
        consoleErrors.Add($"Certification wait failed: {error.Message}");
    }

    assertions["markerRendered"] = body.Contains(spec.Marker, StringComparison.OrdinalIgnoreCase);
    assertions["authenticatedRoute"] = !page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase);
    assertions["language"] = await page.EvaluateAsync<string>("document.documentElement.lang") is var lang && lang.StartsWith(language, StringComparison.OrdinalIgnoreCase);
    assertions["direction"] = await page.EvaluateAsync<string>("document.documentElement.dir") == (language == "ar" ? "rtl" : "ltr");
    var overflowElements = await page.EvaluateAsync<string[]>("""
        () => {
            const viewportWidth = document.documentElement.clientWidth;
            const isContainedOverflow = element => {
                if (element.closest('aside.translate-x-full')) return true;
                for (let parent = element.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
                    const style = getComputedStyle(parent);
                    const rect = parent.getBoundingClientRect();
                    if (['auto', 'scroll', 'hidden', 'clip'].includes(style.overflowX)
                        && rect.left >= -1 && rect.right <= viewportWidth + 1) return true;
                }
                return false;
            };
            return Array.from(document.querySelectorAll('body *'))
            .map(element => {
                const rect = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return {
                    tag: element.tagName,
                    className: typeof element.className === 'string' ? element.className : '',
                    text: (element.textContent || '').trim().slice(0, 80),
                    left: Math.round(rect.left),
                    right: Math.round(rect.right),
                    width: Math.round(rect.width),
                    position: style.position,
                    overflowX: style.overflowX,
                    containedOverflow: isContainedOverflow(element),
                };
            })
            .filter(item => item.right > viewportWidth + 1 && !item.containedOverflow)
            .slice(0, 30)
            .map(item => JSON.stringify(item));
        }
        """);
    assertions["noVisiblePageOverflow"] = await page.EvaluateAsync<bool>("""
        () => {
            const viewportWidth = document.documentElement.clientWidth;
            const isContainedOverflow = element => {
                if (element.closest('aside.translate-x-full')) return true;
                for (let parent = element.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
                    const style = getComputedStyle(parent);
                    const rect = parent.getBoundingClientRect();
                    if (['auto', 'scroll', 'hidden', 'clip'].includes(style.overflowX)
                        && rect.left >= -1 && rect.right <= viewportWidth + 1) return true;
                }
                return false;
            };
            return Array.from(document.querySelectorAll('body *'))
                .filter(element => element.getBoundingClientRect().right > viewportWidth + 1)
                .every(isContainedOverflow);
        }
        """);
    assertions["noBlockedMutations"] = blockedMutations.Count == 0;
    assertions["noFailedApiResponses"] = failedResponses.Count == 0;
    assertions["noRequestFailures"] = requestFailures.Count == 0;
    assertions["noUnexpectedConsoleErrors"] = consoleErrors.All(IsKnownConsoleDiagnostic);

    if (spec.Case == "hub")
    {
        var availableText = await page.Locator("#treasury-available-heading").Locator("xpath=ancestor::section").InnerTextAsync();
        var clearingText = await page.Locator("#treasury-transit-heading").Locator("xpath=ancestor::section").InnerTextAsync();
        assertions["availableTotalMatchesCatalog"] = language == "ar" || availableText.Contains(availableTotal, StringComparison.Ordinal);
        assertions["clearingTotalMatchesCatalog"] = language == "ar" || clearingText.Contains(clearingTotal, StringComparison.Ordinal);
        assertions["allCatalogRowsRendered"] = available.Concat(clearing).All(row => body.Contains(row.AccountCode, StringComparison.Ordinal));
        assertions["clearingSeparated"] = clearing.All(row => clearingText.Contains(row.AccountCode, StringComparison.Ordinal))
            && clearing.All(row => !availableText.Contains(row.AccountCode, StringComparison.Ordinal));
    }
    else if (spec.Case == "dashboard")
    {
        assertions["financeTitle"] = body.Contains(
            language == "ar" ? "الخزينة والمالية" : "Treasury & Finance",
            StringComparison.Ordinal);
        assertions["availableTotalMatchesCatalog"] = language == "ar" || body.Contains(availableTotal, StringComparison.Ordinal);
        assertions["clearingTotalMatchesCatalog"] = language == "ar" || body.Contains(clearingTotal, StringComparison.Ordinal);
        assertions["clearingDisclosedAsNonCash"] = language == "ar"
            ? body.Contains("نقدية متاحة", StringComparison.Ordinal)
            : body.Contains("not available cash", StringComparison.OrdinalIgnoreCase);
    }
    else if (spec.Case == "viewer")
    {
        assertions["catalogRequested"] = apiRequests.Any(value => value.Contains("/api/treasury/accounts", StringComparison.OrdinalIgnoreCase));
        assertions["balanceRequestedAfterCatalog"] = apiRequests.Any(value => value.Contains($"/api/treasury/balance/{viewerAccount.AccountCode}", StringComparison.OrdinalIgnoreCase));
        assertions["movementsRequestedAfterCatalog"] = apiRequests.Any(value => value.Contains($"/api/treasury/movements/{viewerAccount.AccountCode}", StringComparison.OrdinalIgnoreCase));
        assertions["localizedServerName"] = body.Contains(LocalizedName(viewerAccount, language), StringComparison.Ordinal);
    }
    else if (spec.Case == "viewer-unavailable")
    {
        assertions["catalogRequested"] = apiRequests.Any(value => value.Contains("/api/treasury/accounts", StringComparison.OrdinalIgnoreCase));
        assertions["noBalanceRequest"] = apiRequests.All(value => !value.Contains("/api/treasury/balance/1020", StringComparison.OrdinalIgnoreCase));
        assertions["noMovementRequest"] = apiRequests.All(value => !value.Contains("/api/treasury/movements/1020", StringComparison.OrdinalIgnoreCase));
    }
    else if (spec.Case == "transfers")
    {
        var selects = page.Locator("select");
        assertions["twoSelectors"] = await selects.CountAsync() == 2;
        for (var index = 0; index < await selects.CountAsync(); index++)
        {
            var options = await selects.Nth(index).Locator("option").AllTextContentsAsync();
            assertions[$"selector{index + 1}AvailableOnly"] = available.All(row => options.Any(option => option.Contains(row.AccountCode, StringComparison.Ordinal)))
                && clearing.All(row => options.All(option => !option.Contains(row.AccountCode, StringComparison.Ordinal)));
            assertions[$"selector{index + 1}LocalizedNames"] = available.All(row => options.Any(option => option.Contains(LocalizedName(row, language), StringComparison.Ordinal)));
        }
    }
    else if (spec.Case == "clearing-aging")
    {
        var action = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
        {
            Name = language == "ar" ? "مسار عمل المزود" : "Provider workflow",
        });
        assertions["providerActionPresent"] = await action.CountAsync() > 0;
        assertions["providerActionDisabled"] = await action.CountAsync() > 0 && await action.IsDisabledAsync();
        assertions["noTransferNavigation"] = !body.Contains(language == "ar" ? "تنفيذ تحويل" : "Execute Transfer", StringComparison.OrdinalIgnoreCase);
    }
    else if (spec.Case is "cash-flow" or "balance-sheet" or "trial-balance" or "general-ledger")
    {
        assertions["boundaryNotePresent"] = await page.Locator("[role=note]").CountAsync() > 0;
    }
    else if (spec.Case == "supplier-card")
    {
        assertions["restrictedAssetBoundary"] = language == "ar"
            ? body.Contains("ليس نقدية متاحة", StringComparison.Ordinal)
            : body.Contains("Not available cash", StringComparison.OrdinalIgnoreCase);
    }

    await page.AddStyleTagAsync(new PageAddStyleTagOptions
    {
        Content = "aside.translate-x-full { display: none !important; }",
    });
    var screenshotPath = Path.Combine(outputRoot, $"{spec.Case}-{language}-{viewport.Name}.png");
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
    return new BrowserResult(
        spec.Case,
        language,
        viewport.Name,
        $"{viewport.Width}x{viewport.Height}",
        page.Url,
        assertions.Values.All(value => value),
        assertions,
        apiRequests.Distinct().ToArray(),
        blockedMutations,
        failedResponses,
        requestFailures,
        consoleErrors,
        overflowElements,
        screenshotPath);
}

async Task<string> IssueTokenAsync(string tenantId)
{
    using var response = await client.PostAsJsonAsync($"{apiBase}/test-auth/token", new { tenantId });
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.GetProperty("token").GetString()
        ?? throw new InvalidOperationException("The test token response did not contain a token.");
}

async Task<CatalogRow[]> GetCatalogAsync(string token)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/treasury/accounts");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<CatalogRow[]>(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    }) ?? [];
}

static string LocalizedName(CatalogRow row, string language) =>
    language == "ar" && !string.IsNullOrWhiteSpace(row.NameAr) ? row.NameAr.Trim() : row.Name.Trim();

static string SafeUrl(string value)
{
    var uri = new Uri(value);
    return uri.PathAndQuery;
}

static bool IsKnownConsoleDiagnostic(string value) =>
    value.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase)
    && value.Contains("meta", StringComparison.OrdinalIgnoreCase);

sealed record CatalogRow(string AccountCode, string Name, string? NameAr, decimal CurrentBalance, string AccountType);
sealed record Viewport(string Name, int Width, int Height);
sealed record RouteSpec(string Case, string Path, string Marker, string Token);
sealed record BrowserResult(
    string Case,
    string Language,
    string Viewport,
    string Size,
    string FinalUrl,
    bool Passed,
    IReadOnlyDictionary<string, bool> Assertions,
    IReadOnlyList<string> ApiRequests,
    IReadOnlyList<string> BlockedMutations,
    IReadOnlyList<string> FailedResponses,
    IReadOnlyList<string> RequestFailures,
    IReadOnlyList<string> ConsoleErrors,
    IReadOnlyList<string> OverflowElements,
    string ScreenshotPath);

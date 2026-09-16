using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Npgsql;

var email = Environment.GetEnvironmentVariable("WAVE2_EMAIL")
    ?? throw new InvalidOperationException("WAVE2_EMAIL is required.");
var password = Environment.GetEnvironmentVariable("WAVE2_PASSWORD")
    ?? throw new InvalidOperationException("WAVE2_PASSWORD is required.");

const string apiBase = "http://localhost:5010";
const string webBase = "http://localhost:5173";
const string expectedTenant = "c9c5c1ae-949e-498e-bc63-07242daeb49d";
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var productAName = $"WAVE4 INDEPENDENT CERT {stamp}";
var productBName = $"WAVE4 COLLISION CERT {stamp}";
var evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "evidence");
Directory.CreateDirectory(evidenceDirectory);

var failures = new List<string>();
var evidence = new Dictionary<string, object?>();
string productAId = "";
string productBId = "";
string tenantId = "";

void Check(string name, bool passed, string detail = "")
{
    evidence[name] = new { result = passed ? "PASS" : "FAIL", detail };
    if (!passed)
    {
        failures.Add(name);
    }
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name} {detail}".TrimEnd());
}

static string Truncate(string value, int length = 450) =>
    value.Length <= length ? value : value[..length];

static string FindString(JsonElement element, params string[] names)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.ToString();
            }
            var nested = FindString(property.Value, names);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray())
        {
            var nested = FindString(item, names);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }
    }
    return "";
}

static JsonElement FindArray(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Array)
    {
        return element;
    }
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var name in new[] { "data", "items", "results" })
        {
            if (element.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
            {
                return candidate;
            }
        }
    }
    return default;
}

static string DisplayName(JsonElement variant, string defaultLabel = "Default Variant")
{
    if (!variant.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Array)
    {
        return defaultLabel;
    }
    var parts = attributes.EnumerateArray()
        .Select(attribute => new
        {
            Name = FindString(attribute, "name"),
            Value = FindString(attribute, "value"),
            SortOrder = int.TryParse(FindString(attribute, "sortOrder"), out var order) ? order : 0
        })
        .OrderBy(attribute => attribute.SortOrder)
        .ToArray();
    return parts.Length switch
    {
        0 => defaultLabel,
        1 => $"{parts[0].Name}: {parts[0].Value}",
        _ => string.Join(" / ", parts.Select(part => part.Value))
    };
}

static string ReadReason(JsonElement response, bool arabic)
{
    if (response.TryGetProperty("reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
    {
        var messages = reasons.EnumerateArray()
            .Select(reason => FindString(reason, arabic ? "messageAr" : "messageEn"))
            .Where(message => !string.IsNullOrWhiteSpace(message));
        var joined = string.Join(" ", messages);
        if (!string.IsNullOrWhiteSpace(joined))
        {
            return joined;
        }
    }
    return FindString(response, arabic ? "messageAr" : "messageEn");
}

var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
using var api = new HttpClient(new HttpClientHandler { UseCookies = false })
{
    BaseAddress = new Uri(apiBase),
    Timeout = TimeSpan.FromSeconds(45)
};

void HarvestCookies(HttpResponseMessage response)
{
    if (!response.Headers.TryGetValues("Set-Cookie", out var values))
    {
        return;
    }
    foreach (var value in values)
    {
        var pair = value.Split(';', 2)[0];
        var separator = pair.IndexOf('=');
        if (separator > 0)
        {
            cookies[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
        }
    }
}

async Task<HttpResponseMessage> SendApi(HttpRequestMessage request)
{
    if (cookies.Count > 0)
    {
        request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
    }
    if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head
        && cookies.TryGetValue("csrf_token", out var csrf))
    {
        request.Headers.TryAddWithoutValidation("X-CSRF-Token", csrf);
    }
    var response = await api.SendAsync(request);
    HarvestCookies(response);
    return response;
}

async Task<bool> LoginApi()
{
    for (var attempt = 1; attempt <= 5; attempt++)
    {
        cookies.Clear();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = email, password })
        };
        using var response = await SendApi(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }
        if ((int)response.StatusCode != 429 || attempt == 5)
        {
            return false;
        }
        await Task.Delay(TimeSpan.FromSeconds(30));
    }
    return false;
}

async Task<(HttpStatusCode status, string body)> ApiJson(
    HttpMethod method,
    string url,
    object? body = null,
    string? idempotencyKey = null,
    string? rowVersion = null)
{
    using var request = new HttpRequestMessage(method, url);
    if (body != null)
    {
        request.Content = JsonContent.Create(body);
    }
    if (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
    }
    if (!string.IsNullOrWhiteSpace(rowVersion))
    {
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion.Trim('"')}\"");
    }
    using var response = await SendApi(request);
    return (response.StatusCode, await response.Content.ReadAsStringAsync());
}

var settings = JsonDocument.Parse(await File.ReadAllTextAsync("PointOfSale.API/appsettings.Development.local.json"));
var connectionString = settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()
    ?? throw new InvalidOperationException("DefaultConnection is missing.");

async Task<string> ReadProductFingerprint(string productId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT jsonb_build_object(
            'product', jsonb_build_object(
                'id', p."Id", 'isDeleted', p."IsDeleted", 'isActive', p."IsActive",
                'updatedAt', p."UpdatedAt", 'xmin', p.xmin::text),
            'variants', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                    'id', v."Id", 'sku', v."SKU", 'isDeleted', v."IsDeleted",
                    'isActive', v."IsActive", 'isDefault', v."IsDefault",
                    'updatedAt', v."UpdatedAt") ORDER BY v."Id")
                FROM "ProductVariants" v WHERE v."ProductId" = p."Id"
            ), '[]'::jsonb)
        )::text
        FROM "Products" p WHERE p."Id" = @productId
        """, connection);
    command.Parameters.AddWithValue("productId", Guid.Parse(productId));
    return (await command.ExecuteScalarAsync())?.ToString() ?? "";
}

async Task<(string productId, string productName, string variantId, string sku, string displayName, string responseBody)> FindProtectedVariant()
{
    var candidates = new List<(Guid ProductId, string ProductName, Guid VariantId, string Sku)>();
    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT p."Id", p."Name", v."Id", COALESCE(v."SKU", '')
            FROM "Products" p
            JOIN "ProductVariants" v ON v."ProductId" = p."Id"
            WHERE p."TenantId" = @tenantId AND p."IsDeleted" = FALSE
              AND p."HasVariants" = TRUE AND v."IsDeleted" = FALSE
              AND (SELECT COUNT(*) FROM "ProductVariants" siblings
                   WHERE siblings."ProductId" = p."Id" AND siblings."IsDeleted" = FALSE) > 1
            ORDER BY p."UpdatedAt" DESC, v."IsDefault", v."UpdatedAt" DESC
            """, connection);
        command.Parameters.AddWithValue("tenantId", Guid.Parse(tenantId));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            candidates.Add((reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3)));
        }
    }

    foreach (var candidate in candidates)
    {
        var check = await ApiJson(HttpMethod.Get, $"/api/products/{candidate.ProductId}/variants/{candidate.VariantId}/can-delete");
        if (check.status != HttpStatusCode.OK)
        {
            continue;
        }
        using var checkDocument = JsonDocument.Parse(check.body);
        if (!checkDocument.RootElement.TryGetProperty("canDelete", out var canDelete) || canDelete.GetBoolean())
        {
            continue;
        }
        var variants = await ApiJson(HttpMethod.Get, $"/api/products/{candidate.ProductId}/variants");
        if (variants.status != HttpStatusCode.OK)
        {
            continue;
        }
        using var variantsDocument = JsonDocument.Parse(variants.body);
        var variant = FindArray(variantsDocument.RootElement).EnumerateArray()
            .FirstOrDefault(item => FindString(item, "id", "variantId").Equals(candidate.VariantId.ToString(), StringComparison.OrdinalIgnoreCase));
        if (variant.ValueKind != JsonValueKind.Object)
        {
            continue;
        }
        return (candidate.ProductId.ToString(), candidate.ProductName, candidate.VariantId.ToString(), candidate.Sku, DisplayName(variant), check.body);
    }
    return ("", "", "", "", "", "");
}

object ProductBody(string name, string categoryId, string unitId, string skuPrefix, string attributeName, params string[] values) => new
{
    name,
    description = "Isolated Wave 4 independent certification product",
    categoryId,
    purchasePrice = 5m,
    salePrice = 12m,
    onlinePrice = 12m,
    priceIncludesTax = false,
    taxRate = 0m,
    isActive = true,
    trackStock = false,
    allowBackorder = false,
    hasVariants = true,
    publishStatus = 0,
    isOnlineVisible = false,
    isSearchable = false,
    isFeatured = false,
    productUnits = new object[]
    {
        new { unitId, conversionFactor = 1m, purchasePrice = 5m, sellingPrice = 12m, isDefault = true, isBaseUnit = true }
    },
    variants = values.Select((value, index) => new
    {
        variantId = (string?)null,
        sku = $"{skuPrefix}-{index + 1}",
        barcode = (string?)null,
        price = 12m,
        onlinePrice = 12m,
        costPrice = 5m,
        isActive = true,
        isDefault = index == 0,
        attributes = new Dictionary<string, string> { [attributeName] = value }
    }).ToArray()
};

async Task LoginBrowser(IPage page, bool arabic = false)
{
    await page.GotoAsync(webBase + "/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    if (arabic)
    {
        await page.EvaluateAsync("() => localStorage.setItem('i18nextLng', 'ar')");
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }
    await page.Locator("input[type=text], input[type=email]").First.FillAsync(email);
    await page.Locator("input[type=password]").FillAsync(password);
    await page.Locator("button[type=submit]").ClickAsync();
    await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
        new PageWaitForURLOptions { Timeout = 30_000 });
}

async Task OpenEditor(IPage page, string productId)
{
    await page.GotoAsync($"{webBase}/products/{productId}/edit?focus=variants",
        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.GetByRole(AriaRole.Table).WaitForAsync(new LocatorWaitForOptions
    {
        State = WaitForSelectorState.Visible,
        Timeout = 30_000
    });
}

async Task ConfirmDeleteSelected(IPage page, string deleteButtonPattern, string confirmPattern)
{
    await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(deleteButtonPattern) }).ClickAsync();
    var dialog = page.GetByRole(AriaRole.Dialog);
    await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    await dialog.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(confirmPattern) }).ClickAsync();
}

async Task CleanupProduct(string productId, string suffix)
{
    if (!Guid.TryParse(productId, out _))
    {
        return;
    }
    await LoginApi();
    var current = await ApiJson(HttpMethod.Get, $"/api/products/{productId}");
    if (current.status == HttpStatusCode.NotFound)
    {
        Check($"cleanup-{suffix}", true, "already absent");
        return;
    }
    if (current.status != HttpStatusCode.OK)
    {
        Check($"cleanup-{suffix}", false, $"GET {(int)current.status}");
        return;
    }
    using var currentDocument = JsonDocument.Parse(current.body);
    var rowVersion = FindString(currentDocument.RootElement, "rowVersion", "version", "projectionMutationVersion");
    var deleted = await ApiJson(HttpMethod.Delete, $"/api/products/{productId}", idempotencyKey: $"wave4-independent-cleanup-{stamp}-{suffix}", rowVersion: rowVersion);
    Check($"cleanup-{suffix}", deleted.status is HttpStatusCode.OK or HttpStatusCode.NoContent,
        $"{(int)deleted.status} {Truncate(deleted.body, 180)}");
}

try
{
    var apiLoginPassed = await LoginApi();
    Check("api-login", apiLoginPassed);
    if (!apiLoginPassed)
    {
        throw new InvalidOperationException("API login failed after rate-limit retries.");
    }
    var session = await ApiJson(HttpMethod.Get, "/api/auth/session");
    tenantId = JsonDocument.Parse(session.body).RootElement.ValueKind == JsonValueKind.Object
        ? FindString(JsonDocument.Parse(session.body).RootElement, "tenantId")
        : "";
    Check("tenant-ahramstore", tenantId.Equals(expectedTenant, StringComparison.OrdinalIgnoreCase), tenantId);

    var categories = await ApiJson(HttpMethod.Get, "/api/categories");
    var units = await ApiJson(HttpMethod.Get, "/api/units");
    using var categoriesDocument = JsonDocument.Parse(categories.body);
    using var unitsDocument = JsonDocument.Parse(units.body);
    var categoryId = FindString(FindArray(categoriesDocument.RootElement)[0], "id");
    var unitId = FindString(FindArray(unitsDocument.RootElement)[0], "id");
    Check("reference-category", Guid.TryParse(categoryId, out _), categoryId);
    Check("reference-unit", Guid.TryParse(unitId, out _), unitId);

    var protectedVariant = await FindProtectedVariant();
    Check("protected-variant-found", Guid.TryParse(protectedVariant.variantId, out _),
        $"product={protectedVariant.productName}; sku={protectedVariant.sku}");
    evidence["protectedCanDeleteResponse"] = string.IsNullOrWhiteSpace(protectedVariant.responseBody)
        ? null
        : JsonSerializer.Deserialize<object>(protectedVariant.responseBody);
    var protectedBefore = Guid.TryParse(protectedVariant.productId, out _)
        ? await ReadProductFingerprint(protectedVariant.productId)
        : "";

    var createA = await ApiJson(HttpMethod.Post, "/api/products",
        ProductBody(productAName, categoryId, unitId, "W4I-SEED-A", "Size", "256", "512"),
        "wave4-independent-create-a-" + stamp);
    Check("create-isolated-a", createA.status is HttpStatusCode.OK or HttpStatusCode.Created,
        $"{(int)createA.status} {Truncate(createA.body, 220)}");
    using (var createDocument = JsonDocument.Parse(createA.body))
    {
        productAId = FindString(createDocument.RootElement, "id", "productId");
    }
    Check("create-isolated-a-id", Guid.TryParse(productAId, out _), productAId);

    var createB = await ApiJson(HttpMethod.Post, "/api/products",
        ProductBody(productBName, categoryId, unitId, "W4I-SEED-B", "Style", "One", "Two"),
        "wave4-independent-create-b-" + stamp);
    Check("create-isolated-b", createB.status is HttpStatusCode.OK or HttpStatusCode.Created,
        $"{(int)createB.status} {Truncate(createB.body, 220)}");
    using (var createDocument = JsonDocument.Parse(createB.body))
    {
        productBId = FindString(createDocument.RootElement, "id", "productId");
    }
    Check("create-isolated-b-id", Guid.TryParse(productBId, out _), productBId);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = "msedge",
        Headless = false,
        SlowMo = 45
    });

    // English desktop: name semantics, contextual SKU generation, persistence, Advanced, and clean deletion.
    var desktopContext = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        Locale = "en-US"
    });
    var desktop = await desktopContext.NewPageAsync();
    var desktopFailures = new ConcurrentBag<string>();
    desktop.Response += (_, response) =>
    {
        if (response.Status >= 400 && !response.Url.Contains("/api/auth/session"))
        {
            desktopFailures.Add($"{response.Status} {response.Url}");
        }
    };
    await LoginBrowser(desktop);
    var authStorageState = await desktopContext.StorageStateAsync();
    await OpenEditor(desktop, productAId);
    Check("desktop-name-size-256", await desktop.GetByText("Size: 256", new() { Exact = true }).CountAsync() >= 1);
    Check("desktop-name-size-512", await desktop.GetByText("Size: 512", new() { Exact = true }).CountAsync() >= 1);
    Check("desktop-no-bare-name-256", await desktop.GetByRole(AriaRole.Cell, new() { Name = "256", Exact = true }).CountAsync() == 0);
    var advanced = desktop.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Advanced Variant Tools") });
    Check("advanced-collapsed-default", await advanced.GetAttributeAsync("aria-expanded") == "false");

    await desktop.GetByLabel("Select variant: Size: 256", new() { Exact = true }).CheckAsync();
    await desktop.GetByLabel("Select variant: Size: 512", new() { Exact = true }).CheckAsync();
    await desktop.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Generate SKU for 2 Selected") }).ClickAsync();
    var sku256Input = desktop.GetByLabel("Size: 256: SKU", new() { Exact = true });
    var sku512Input = desktop.GetByLabel("Size: 512: SKU", new() { Exact = true });
    var sku256 = await sku256Input.InputValueAsync();
    var sku512 = await sku512Input.InputValueAsync();
    Check("generated-skus-contextual", sku256 != "256" && sku512 != "512" && sku256.EndsWith("-256") && sku512.EndsWith("-512"), $"{sku256}|{sku512}");
    Check("generated-skus-distinct", !sku256.Equals(sku512, StringComparison.OrdinalIgnoreCase));
    var skuSaveResponseTask = desktop.WaitForResponseAsync(
        response => response.Request.Method == "PUT" && response.Url.Contains($"/api/products/{productAId}"),
        new PageWaitForResponseOptions { Timeout = 45_000 });
    await desktop.GetByRole(AriaRole.Button, new() { Name = "Save Matrix", Exact = true }).ClickAsync();
    var skuSaveResponse = await skuSaveResponseTask;
    Check("sku-save-put-200", skuSaveResponse.Status == 200, skuSaveResponse.Status.ToString());
    await desktop.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await desktop.GetByRole(AriaRole.Table).WaitForAsync();
    Check("sku-reload-stable", await desktop.GetByLabel("Size: 256: SKU", new() { Exact = true }).InputValueAsync() == sku256
        && await desktop.GetByLabel("Size: 512: SKU", new() { Exact = true }).InputValueAsync() == sku512);

    await advanced.ClickAsync();
    Check("advanced-expanded", await advanced.GetAttributeAsync("aria-expanded") == "true");
    foreach (var heading in new[] { "Variant Templates", "Choose Attributes", "SKU Builder", "Combination Rules", "Workspace Statistics" })
    {
        Check("advanced-section-" + heading.Replace(" ", "-").ToLowerInvariant(), await desktop.GetByText(heading, new() { Exact = true }).CountAsync() >= 1);
    }
    await advanced.ClickAsync();
    Check("advanced-recollapsed", await advanced.GetAttributeAsync("aria-expanded") == "false");

    await desktop.GetByLabel("Select variant: Size: 512", new() { Exact = true }).CheckAsync();
    await ConfirmDeleteSelected(desktop, "^Delete 1 Selected$", "^Delete$");
    var cleanDeleteResponseTask = desktop.WaitForResponseAsync(
        response => response.Request.Method == "GET" && response.Url.Contains("/can-delete"),
        new PageWaitForResponseOptions { Timeout = 30_000 });
    var cleanPutResponseTask = desktop.WaitForResponseAsync(
        response => response.Request.Method == "PUT" && response.Url.Contains($"/api/products/{productAId}"),
        new PageWaitForResponseOptions { Timeout = 45_000 });
    await desktop.GetByRole(AriaRole.Button, new() { Name = "Save Matrix", Exact = true }).ClickAsync();
    var cleanDeleteResponse = await cleanDeleteResponseTask;
    var cleanPutResponse = await cleanPutResponseTask;
    var cleanDeleteBody = await cleanDeleteResponse.TextAsync();
    Check("clean-can-delete-true", cleanDeleteResponse.Status == 200 && cleanDeleteBody.Contains("\"canDelete\":true", StringComparison.OrdinalIgnoreCase), Truncate(cleanDeleteBody));
    Check("clean-delete-put-200", cleanPutResponse.Status == 200, cleanPutResponse.Status.ToString());
    await desktop.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await desktop.GetByRole(AriaRole.Table).WaitForAsync();
    Check("clean-delete-ui-removed", await desktop.GetByText("Size: 512", new() { Exact = true }).CountAsync() == 0);
    Check("desktop-no-document-overflow", !await desktop.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"));
    var desktopShot = Path.Combine(evidenceDirectory, "wave4-independent-desktop-en.png");
    await desktop.ScreenshotAsync(new PageScreenshotOptions { Path = desktopShot, FullPage = true });
    evidence["desktopScreenshot"] = desktopShot;
    Check("desktop-no-unexpected-http-failures", desktopFailures.IsEmpty, string.Join("; ", desktopFailures));
    await desktopContext.CloseAsync();

    // Precise 409 message: isolated B attempts to reuse isolated A's live SKU.
    var collisionContext = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        Locale = "en-US",
        StorageState = authStorageState
    });
    var collisionPage = await collisionContext.NewPageAsync();
    await OpenEditor(collisionPage, productBId);
    await collisionPage.GetByLabel("Style: One: SKU", new() { Exact = true }).FillAsync(sku256);
    var collisionResponseTask = collisionPage.WaitForResponseAsync(
        response => response.Request.Method == "PUT" && response.Url.Contains($"/api/products/{productBId}"),
        new PageWaitForResponseOptions { Timeout = 45_000 });
    await collisionPage.GetByRole(AriaRole.Button, new() { Name = "Save Matrix", Exact = true }).ClickAsync();
    var collisionResponse = await collisionResponseTask;
    var collisionBody = await collisionResponse.TextAsync();
    Check("collision-put-409", collisionResponse.Status == 409, $"{collisionResponse.Status} {Truncate(collisionBody)}");
    var collisionAlert = collisionPage.Locator("#variant-matrix-error");
    await collisionAlert.WaitForAsync();
    var collisionText = await collisionAlert.InnerTextAsync();
    Check("collision-ui-precise-message", collisionText.Contains("SKU is already assigned to another product variant.", StringComparison.Ordinal), Truncate(collisionText));
    Check("collision-ui-not-generic", !collisionText.Contains("changed by another user", StringComparison.OrdinalIgnoreCase));
    await collisionPage.WaitForFunctionAsync("() => document.activeElement?.id === 'variant-matrix-error'");
    Check("collision-alert-focused", await collisionAlert.EvaluateAsync<bool>("element => document.activeElement === element"));
    await collisionContext.CloseAsync();

    // Protected deletion in English: actual authoritative response, no PUT, focused nearby alert, no DB mutation.
    if (Guid.TryParse(protectedVariant.productId, out _))
    {
        var protectedContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            Locale = "en-US",
            StorageState = authStorageState
        });
        var protectedPage = await protectedContext.NewPageAsync();
        var protectedPutCount = 0;
        protectedPage.Request += (_, request) =>
        {
            if (request.Method == "PUT" && request.Url.Contains($"/api/products/{protectedVariant.productId}"))
            {
                Interlocked.Increment(ref protectedPutCount);
            }
        };
        await OpenEditor(protectedPage, protectedVariant.productId);
        var protectedSelector = protectedPage.GetByLabel($"Select variant: {protectedVariant.displayName}", new() { Exact = true });
        Check("protected-row-visible-en", await protectedSelector.CountAsync() == 1, protectedVariant.displayName);
        await protectedSelector.CheckAsync();
        await ConfirmDeleteSelected(protectedPage, "^Delete 1 Selected$", "^Delete$");
        var preflightResponseTask = protectedPage.WaitForResponseAsync(
            response => response.Request.Method == "GET" && response.Url.Contains($"/variants/{protectedVariant.variantId}/can-delete"),
            new PageWaitForResponseOptions { Timeout = 30_000 });
        await protectedPage.GetByRole(AriaRole.Button, new() { Name = "Save Matrix", Exact = true }).ClickAsync();
        var preflightResponse = await preflightResponseTask;
        var preflightBody = await preflightResponse.TextAsync();
        await protectedPage.WaitForTimeoutAsync(500);
        var alert = protectedPage.GetByRole(AriaRole.Alert).Last;
        await alert.WaitForAsync();
        var alertText = await alert.InnerTextAsync();
        using var protectedResponseDocument = JsonDocument.Parse(protectedVariant.responseBody);
        var expectedEnglishReason = ReadReason(protectedResponseDocument.RootElement, false);
        Check("protected-can-delete-200-false", preflightResponse.Status == 200 && preflightBody.Contains("\"canDelete\":false", StringComparison.OrdinalIgnoreCase), Truncate(preflightBody));
        Check("protected-no-put-en", protectedPutCount == 0, $"count={protectedPutCount}");
        Check("protected-alert-heading-en", alertText.Contains("Variant matrix was not saved.", StringComparison.Ordinal));
        Check("protected-alert-exact-variant-en", alertText.Contains(protectedVariant.displayName, StringComparison.Ordinal) && alertText.Contains(protectedVariant.sku, StringComparison.Ordinal), Truncate(alertText));
        Check("protected-alert-reason-en", !string.IsNullOrWhiteSpace(expectedEnglishReason) && alertText.Contains(expectedEnglishReason, StringComparison.Ordinal), expectedEnglishReason);
        Check("protected-alert-focused-en", await alert.EvaluateAsync<bool>("element => document.activeElement === element"));
        var protectedShot = Path.Combine(evidenceDirectory, "wave4-protected-delete-en.png");
        await protectedPage.ScreenshotAsync(new PageScreenshotOptions { Path = protectedShot, FullPage = true });
        evidence["protectedEnglishScreenshot"] = protectedShot;
        await protectedContext.CloseAsync();

        var protectedAfter = await ReadProductFingerprint(protectedVariant.productId);
        Check("protected-db-no-mutation-en", protectedBefore == protectedAfter);

        // Repeat the blocked path under Arabic RTL to prove localized authoritative feedback.
        var arabicContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            Locale = "ar-EG",
            StorageState = authStorageState
        });
        var arabicPage = await arabicContext.NewPageAsync();
        var arabicPutCount = 0;
        arabicPage.Request += (_, request) =>
        {
            if (request.Method == "PUT" && request.Url.Contains($"/api/products/{protectedVariant.productId}"))
            {
                Interlocked.Increment(ref arabicPutCount);
            }
        };
        await arabicPage.GotoAsync(webBase, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await arabicPage.EvaluateAsync("() => localStorage.setItem('i18nextLng', 'ar')");
        await OpenEditor(arabicPage, protectedVariant.productId);
        Check("protected-ar-rtl", await arabicPage.EvaluateAsync<string>("() => document.documentElement.dir") == "rtl");
        var arabicSelect = arabicPage.Locator("input[type=checkbox]").Filter(new LocatorFilterOptions
        {
            Has = arabicPage.Locator("xpath=..")
        });
        var targetCheckbox = arabicPage.Locator($"input[aria-label$='{protectedVariant.displayName.Replace("'", "\\'")}']").First;
        Check("protected-row-visible-ar", await targetCheckbox.CountAsync() == 1, protectedVariant.displayName);
        await targetCheckbox.CheckAsync();
        await ConfirmDeleteSelected(arabicPage, "حذف 1 محدد", "^حذف$");
        var arabicPreflightTask = arabicPage.WaitForResponseAsync(
            response => response.Request.Method == "GET" && response.Url.Contains($"/variants/{protectedVariant.variantId}/can-delete"),
            new PageWaitForResponseOptions { Timeout = 30_000 });
        await arabicPage.GetByRole(AriaRole.Button, new() { Name = "حفظ المصفوفة", Exact = true }).ClickAsync();
        await arabicPreflightTask;
        await arabicPage.WaitForTimeoutAsync(500);
        var arabicAlert = arabicPage.GetByRole(AriaRole.Alert).Last;
        var arabicAlertText = await arabicAlert.InnerTextAsync();
        var expectedArabicReason = ReadReason(protectedResponseDocument.RootElement, true);
        Check("protected-no-put-ar", arabicPutCount == 0, $"count={arabicPutCount}");
        Check("protected-alert-heading-ar", arabicAlertText.Contains("لم يتم حفظ مصفوفة المتغيرات.", StringComparison.Ordinal));
        Check("protected-alert-reason-ar", !string.IsNullOrWhiteSpace(expectedArabicReason) && arabicAlertText.Contains(expectedArabicReason, StringComparison.Ordinal), expectedArabicReason);
        Check("protected-alert-no-en-leak", !arabicAlertText.Contains(expectedEnglishReason, StringComparison.Ordinal));
        Check("protected-alert-focused-ar", await arabicAlert.EvaluateAsync<bool>("element => document.activeElement === element"));
        var arabicShot = Path.Combine(evidenceDirectory, "wave4-protected-delete-ar.png");
        await arabicPage.ScreenshotAsync(new PageScreenshotOptions { Path = arabicShot, FullPage = true });
        evidence["protectedArabicScreenshot"] = arabicShot;
        await arabicContext.CloseAsync();
        var protectedAfterArabic = await ReadProductFingerprint(protectedVariant.productId);
        Check("protected-db-no-mutation-ar", protectedBefore == protectedAfterArabic);
    }

    // Mobile English and Arabic responsive checks use the isolated surviving product.
    async Task RunMobile(string tag, bool arabic)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            Locale = arabic ? "ar-EG" : "en-US",
            StorageState = authStorageState
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(webBase, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync($"() => localStorage.setItem('i18nextLng', '{(arabic ? "ar" : "en")}')");
        await OpenEditor(page, productAId);
        Check($"mobile-{tag}-direction", await page.EvaluateAsync<string>("() => document.documentElement.dir") == (arabic ? "rtl" : "ltr"));
        Check($"mobile-{tag}-no-document-overflow", !await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"));
        Check($"mobile-{tag}-matrix-scroll-container", await page.Locator("div.overflow-x-auto").EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth"));
        var advancedButton = page.GetByRole(AriaRole.Button, new()
        {
            NameRegex = new Regex(arabic ? "أدوات المتغيرات المتقدمة" : "Advanced Variant Tools")
        });
        Check($"mobile-{tag}-advanced-reachable", await advancedButton.IsVisibleAsync());
        var shot = Path.Combine(evidenceDirectory, $"wave4-mobile-{tag}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = shot, FullPage = true });
        evidence[$"mobile{tag}Screenshot"] = shot;
        await context.CloseAsync();
    }

    await RunMobile("en", arabic: false);
    await RunMobile("ar", arabic: true);

    // API and SQL reconciliation after successful clean deletion.
    await LoginApi();
    var finalVariants = await ApiJson(HttpMethod.Get, $"/api/products/{productAId}/variants");
    using (var finalDocument = JsonDocument.Parse(finalVariants.body))
    {
        var live = FindArray(finalDocument.RootElement).EnumerateArray().ToArray();
        Check("api-isolated-a-one-live-variant", live.Length == 1, $"count={live.Length}");
        Check("api-isolated-a-sku-agrees", live.Length == 1 && FindString(live[0], "sku") == sku256, live.Length == 1 ? FindString(live[0], "sku") : "");
    }
    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*) FILTER (WHERE "IsDeleted" = FALSE),
                   COUNT(*) FILTER (WHERE "IsDeleted" = TRUE),
                   COUNT(*) FILTER (WHERE "IsDeleted" = FALSE AND "SKU" = @sku)
            FROM "ProductVariants" WHERE "ProductId" = @productId
            """, connection);
        command.Parameters.AddWithValue("productId", Guid.Parse(productAId));
        command.Parameters.AddWithValue("sku", sku256);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        Check("db-clean-delete-soft-deleted", reader.GetInt64(0) == 1 && reader.GetInt64(1) == 1,
            $"live={reader.GetInt64(0)} deleted={reader.GetInt64(1)}");
        Check("db-live-sku-agrees", reader.GetInt64(2) == 1);
    }
}
catch (Exception exception)
{
    Check("harness-exception", false, exception.ToString());
}
finally
{
    try { await CleanupProduct(productBId, "b"); } catch (Exception exception) { Check("cleanup-b", false, exception.Message); }
    try { await CleanupProduct(productAId, "a"); } catch (Exception exception) { Check("cleanup-a", false, exception.Message); }

    evidence["stamp"] = stamp;
    evidence["tenantId"] = tenantId;
    evidence["productAId"] = productAId;
    evidence["productBId"] = productBId;
    evidence["failureCount"] = failures.Count;
    evidence["failures"] = failures;
    var path = Path.Combine(evidenceDirectory, "wave4-independent-evidence.json");
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"EVIDENCE {path}");
}

return failures.Count == 0 ? 0 : 2;

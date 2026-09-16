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
const string storefrontBase = "http://localhost:5174";
const decimal posPrice = 100m;
const decimal purchasePrice = 60m;
const decimal onlinePrice = 83.25m;

var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var isolatedName = $"WAVE3 CERT {stamp}";
var isolatedNameAr = $"WAVE3 AR {stamp}";
var sku = "W3C" + stamp.Replace("-", string.Empty).Replace(":", string.Empty);
var evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "evidence");
Directory.CreateDirectory(evidenceDirectory);
var evidence = new Dictionary<string, object?>();
var failures = new List<string>();
string productId = string.Empty;
string tenantId = string.Empty;
string tenantSlug = string.Empty;
string storefrontOrigin = storefrontBase;
string slug = string.Empty;
string lastStorefrontProbe = "not-run";

void Check(string name, bool ok, string detail = "")
{
    evidence[name] = new { result = ok ? "PASS" : "FAIL", detail };
    if (!ok)
    {
        failures.Add(name);
    }
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} {detail}".TrimEnd());
}

static string Truncate(string value, int length = 500) =>
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
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }

            var nested = FindString(property.Value, names);
            if (!string.IsNullOrEmpty(nested))
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
            if (!string.IsNullOrEmpty(nested))
            {
                return nested;
            }
        }
    }

    return string.Empty;
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

static decimal ReadDecimal(JsonElement element, params string[] path)
{
    var current = element;
    foreach (var segment in path)
    {
        if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
        {
            return decimal.MinValue;
        }
    }

    return current.ValueKind == JsonValueKind.Number && current.TryGetDecimal(out var value)
        ? value
        : decimal.MinValue;
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
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
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
    cookies.Clear();
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new { username = email, password })
    };
    using var response = await SendApi(request);
    return response.IsSuccessStatusCode;
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

object ProductBody(string categoryId, string unitId, bool hasVariants, decimal? storefrontPrice) => new
{
    name = isolatedName,
    nameAr = isolatedNameAr,
    description = "Isolated Wave 3 certification product",
    descriptionAr = "Wave 3 isolated certification",
    categoryId,
    sku = hasVariants ? null : sku,
    purchasePrice,
    salePrice = posPrice,
    onlinePrice = hasVariants ? null : storefrontPrice,
    priceIncludesTax = false,
    taxRate = 0,
    isActive = true,
    trackStock = false,
    allowBackorder = false,
    hasVariants,
    publishStatus = hasVariants ? 0 : 1,
    isOnlineVisible = !hasVariants,
    isSearchable = !hasVariants,
    isFeatured = false,
    productUnits = new object[]
    {
        new
        {
            unitId,
            conversionFactor = 1,
            purchasePrice,
            sellingPrice = posPrice,
            isDefault = true,
            isBaseUnit = true
        }
    },
    variants = hasVariants
        ? new object[]
        {
            new
            {
                variantId = (string?)null,
                sku = sku + "-FLIP",
                price = posPrice,
                onlinePrice,
                costPrice = purchasePrice,
                isActive = true,
                isDefault = true,
                attributes = new Dictionary<string, string> { ["Style"] = "Default" }
            }
        }
        : null
};

async Task<(int status, string body)> ReadStorefrontProduct()
{
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/storefront/products/by-id/{productId}");
    request.Headers.Host = new Uri(storefrontOrigin).Authority;
    request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId);
    using var response = await api.SendAsync(request);
    return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
}

async Task<JsonDocument?> WaitForStorefrontPrice(decimal expected, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var (status, body) = await ReadStorefrontProduct();
        lastStorefrontProbe = $"status={status} body={Truncate(body, 350)}";
        if (status == 200)
        {
            var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (ReadDecimal(root, "price") == expected
                && ReadDecimal(root, "effectivePrice") == expected
                && ReadDecimal(root, "pricing", "currentPrice") == expected)
            {
                return document;
            }
            document.Dispose();
        }

        await Task.Delay(500);
    }

    return null;
}

async Task LoginBrowser(IPage page)
{
    await page.GotoAsync(webBase + "/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.Locator("input[type=text], input[type=email]").First.FillAsync(email);
    await page.Locator("input[type=password]").FillAsync(password);
    await page.Locator("button[type=submit]").ClickAsync();
    await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
        new PageWaitForURLOptions { Timeout = 30_000 });
}

async Task<decimal> InputValue(IPage page, string label)
{
    var input = page.Locator("label").Filter(new LocatorFilterOptions
        {
            HasTextRegex = new Regex($"^{Regex.Escape(label)}$")
        }).First
        .Locator("..").Locator("input").First;
    return decimal.Parse(await input.InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture);
}

async Task<Dictionary<string, object?>> ReadSqlEvidence()
{
    var settings = await File.ReadAllTextAsync("PointOfSale.API/appsettings.Development.local.json");
    var connectionString = JsonDocument.Parse(settings).RootElement
        .GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()
        ?? throw new InvalidOperationException("DefaultConnection is missing.");
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT p."TenantId", p."HasVariants", p."IsDeleted",
               pu."PurchasePrice", pu."SellingPrice",
               pv."Price", pv."OnlinePrice", pv."CostPrice", pv."IsDefault", pv."IsDeleted"
        FROM "Products" p
        JOIN "ProductUnits" pu ON pu."ProductId" = p."Id" AND pu."IsDefault" = TRUE AND pu."IsDeleted" = FALSE
        JOIN "ProductVariants" pv ON pv."ProductId" = p."Id" AND pv."IsDefault" = TRUE
        WHERE p."Id" = @productId
        """, connection);
    command.Parameters.AddWithValue("productId", Guid.Parse(productId));
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("Isolated product was not found in SQL.");
    }

    return new Dictionary<string, object?>
    {
        ["tenantId"] = reader.GetGuid(0),
        ["hasVariants"] = reader.GetBoolean(1),
        ["productIsDeleted"] = reader.GetBoolean(2),
        ["unitPurchasePrice"] = reader.GetDecimal(3),
        ["unitSellingPrice"] = reader.GetDecimal(4),
        ["variantPrice"] = reader.GetDecimal(5),
        ["variantOnlinePrice"] = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
        ["variantCostPrice"] = reader.GetDecimal(7),
        ["variantIsDefault"] = reader.GetBoolean(8),
        ["variantIsDeleted"] = reader.GetBoolean(9)
    };
}

async Task<(string slug, string? baseUrl)> ReadTenantStorefrontIdentity()
{
    var settings = await File.ReadAllTextAsync("PointOfSale.API/appsettings.Development.local.json");
    var connectionString = JsonDocument.Parse(settings).RootElement
        .GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()
        ?? throw new InvalidOperationException("DefaultConnection is missing.");
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        "SELECT \"Slug\", \"StorefrontBaseUrl\" FROM \"Tenants\" WHERE \"Id\" = @tenantId",
        connection);
    command.Parameters.AddWithValue("tenantId", Guid.Parse(tenantId));
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("Authenticated tenant was not found in SQL.");
    }

    return (
        reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1));
}

try
{
    Check("api-login", await LoginApi());
    if (failures.Count > 0)
    {
        throw new InvalidOperationException("API login failed.");
    }

    var session = await ApiJson(HttpMethod.Get, "/api/auth/session");
    using (var sessionDocument = JsonDocument.Parse(session.body))
    {
        tenantId = FindString(sessionDocument.RootElement, "tenantId");
    }
    Check("tenant-ahrams", tenantId.Equals("c9c5c1ae-949e-498e-bc63-07242daeb49d", StringComparison.OrdinalIgnoreCase), tenantId);
    var tenantStorefront = await ReadTenantStorefrontIdentity();
    tenantSlug = tenantStorefront.slug;
    storefrontOrigin = !string.IsNullOrWhiteSpace(tenantStorefront.baseUrl)
        ? tenantStorefront.baseUrl.TrimEnd('/')
        : $"http://{tenantSlug}.localhost:5174";
    Check("tenant-storefront-slug", !string.IsNullOrWhiteSpace(tenantSlug), tenantSlug);
    Check("tenant-storefront-origin", Uri.TryCreate(storefrontOrigin, UriKind.Absolute, out _), storefrontOrigin);

    var categories = await ApiJson(HttpMethod.Get, "/api/categories");
    var units = await ApiJson(HttpMethod.Get, "/api/units");
    string categoryId;
    string unitId;
    using (var categoriesDocument = JsonDocument.Parse(categories.body))
    using (var unitsDocument = JsonDocument.Parse(units.body))
    {
        var categoryArray = FindArray(categoriesDocument.RootElement);
        var unitArray = FindArray(unitsDocument.RootElement);
        categoryId = categoryArray.ValueKind == JsonValueKind.Array && categoryArray.GetArrayLength() > 0
            ? FindString(categoryArray[0], "id")
            : string.Empty;
        unitId = unitArray.ValueKind == JsonValueKind.Array && unitArray.GetArrayLength() > 0
            ? FindString(unitArray[0], "id")
            : string.Empty;
    }
    Check("reference-category", Guid.TryParse(categoryId, out _), categoryId);
    Check("reference-unit", Guid.TryParse(unitId, out _), unitId);

    var create = await ApiJson(
        HttpMethod.Post,
        "/api/products",
        ProductBody(categoryId, unitId, hasVariants: false, storefrontPrice: null),
        "wave3-create-" + stamp);
    Check("api-create-isolated-simple", create.status is HttpStatusCode.OK or HttpStatusCode.Created,
        $"{(int)create.status} {Truncate(create.body, 250)}");
    using (var createDocument = JsonDocument.Parse(create.body))
    {
        productId = FindString(createDocument.RootElement, "id", "productId");
        slug = FindString(createDocument.RootElement, "slug");
    }
    Check("api-create-id", Guid.TryParse(productId, out _), productId);

    if (string.IsNullOrWhiteSpace(slug))
    {
        var current = await ApiJson(HttpMethod.Get, $"/api/products/{productId}");
        using var currentDocument = JsonDocument.Parse(current.body);
        slug = FindString(currentDocument.RootElement, "slug");
    }
    Check("server-slug", !string.IsNullOrWhiteSpace(slug), slug);

    var fallbackDocument = await WaitForStorefrontPrice(posPrice, TimeSpan.FromSeconds(12));
    var createProjectionRepairNeeded = fallbackDocument == null;
    evidence["createProjectionRepairNeeded"] = createProjectionRepairNeeded;
    if (createProjectionRepairNeeded)
    {
        var repair = await ApiJson(HttpMethod.Post, $"/api/admin/sync/product-variants/{productId}");
        Check("create-projection-repair-endpoint", repair.status == HttpStatusCode.OK,
            $"{(int)repair.status} {Truncate(repair.body, 250)}");
        fallbackDocument = await WaitForStorefrontPrice(posPrice, TimeSpan.FromSeconds(45));
    }
    using (fallbackDocument)
    {
        Check("storefront-fallback-projected", fallbackDocument != null,
            $"expected={posPrice}; last={lastStorefrontProbe}");
        if (fallbackDocument != null)
        {
            Check("storefront-fallback-default-unit", ReadDecimal(fallbackDocument.RootElement, "defaultUnit", "salePrice") == posPrice);
        }
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = "msedge",
        Headless = false,
        SlowMo = 35
    });

    var desktopContext = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        Locale = "en-US"
    });
    var desktop = await desktopContext.NewPageAsync();
    var desktopFailures = new ConcurrentBag<string>();
    desktop.Response += (_, response) =>
    {
        if (response.Status >= 400
            && !response.Url.Contains("/api/auth/session", StringComparison.OrdinalIgnoreCase)
            && !response.Url.Contains("/api/admin/ai/products/generate", StringComparison.OrdinalIgnoreCase))
        {
            desktopFailures.Add($"{response.Status} {response.Url}");
        }
    };
    await LoginBrowser(desktop);
    await desktop.GotoAsync($"{webBase}/products/{productId}/edit", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    var storefrontInput = desktop.Locator("#product-workspace-form-storefront-price");
    await storefrontInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    Check("desktop-purchase-price", await InputValue(desktop, "Purchase Price") == purchasePrice);
    Check("desktop-pos-price", await InputValue(desktop, "Selling Price") == posPrice);
    Check("desktop-storefront-fallback-empty", string.IsNullOrEmpty(await storefrontInput.InputValueAsync()));
    var typeRadios = desktop.Locator("input[name=product-type]");
    Check("desktop-two-product-types", await typeRadios.CountAsync() == 2);
    Check("desktop-type-locked", await typeRadios.Nth(0).IsDisabledAsync() && await typeRadios.Nth(1).IsDisabledAsync());
    Check("desktop-type-lock-reason", await desktop.Locator("[role=note]").Filter(new() { HasText = "fixed after creation" }).CountAsync() > 0);

    await storefrontInput.FillAsync(onlinePrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
    var updateResponseTask = desktop.WaitForResponseAsync(
        response => response.Request.Method == "PUT" && response.Url.Contains($"/api/products/{productId}"),
        new PageWaitForResponseOptions { Timeout = 45_000 });
    await desktop.Locator("button[type=submit][form=product-workspace-form]").ClickAsync();
    var updateResponse = await updateResponseTask;
    Check("desktop-ui-price-save", updateResponse.Ok, updateResponse.Status.ToString());
    await desktop.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await storefrontInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    Check("desktop-storefront-price-reload", decimal.Parse(await storefrontInput.InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture) == onlinePrice);
    Check("desktop-pos-price-unchanged", await InputValue(desktop, "Selling Price") == posPrice);
    Check("desktop-no-horizontal-overflow", !await desktop.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"));
    var desktopShot = Path.Combine(evidenceDirectory, "wave3-admin-desktop.png");
    await desktop.ScreenshotAsync(new PageScreenshotOptions { Path = desktopShot, FullPage = true });
    evidence["desktopScreenshot"] = desktopShot;

    await desktop.GotoAsync(webBase + "/products/new/edit", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    var createTypeRadios = desktop.Locator("input[name=product-type]");
    await createTypeRadios.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    Check("create-two-product-types", await createTypeRadios.CountAsync() == 2);
    Check("create-product-types-enabled", !await createTypeRadios.Nth(0).IsDisabledAsync() && !await createTypeRadios.Nth(1).IsDisabledAsync());
    await desktop.Locator("button", new PageLocatorOptions { HasText = "Create Product with AI" }).ClickAsync();
    var aiDialog = desktop.Locator("[role=dialog]");
    await aiDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    var aiRequestCount = 0;
    var aiRequestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseAi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await desktop.RouteAsync("**/api/admin/ai/products/generate", async route =>
    {
        Interlocked.Increment(ref aiRequestCount);
        aiRequestSeen.TrySetResult();
        await releaseAi.Task;
        await route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                name = "WAVE3 AI Draft",
                slug = "wave3-ai-draft",
                shortDescription = "Draft",
                description = "Generated draft content",
                brand = "Cert Brand",
                manufacturer = "Cert Manufacturer",
                tags = new[] { "wave3" },
                metaTitle = "Wave3 AI Draft",
                metaDescription = "Draft metadata",
                metaKeywords = new[] { "wave3" },
                features = new[] { "Feature one" },
                faq = new[] { new { question = "Question?", answer = "Answer." } }
            })
        });
    });
    await aiDialog.Locator("textarea").FillAsync("Isolated certification draft");
    await aiDialog.Locator("form").EvaluateAsync("form => { form.requestSubmit(); form.requestSubmit(); }");
    await aiRequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
    await Task.Delay(350);
    Check("ai-same-tick-single-request", aiRequestCount == 1, $"count={aiRequestCount}");
    Check("ai-loading-status", await aiDialog.Locator("[role=status]").IsVisibleAsync());
    Check("ai-controls-locked", await aiDialog.Locator("textarea").IsDisabledAsync()
        && await aiDialog.Locator("select").First.IsDisabledAsync()
        && await aiDialog.Locator("button[type=submit]").IsDisabledAsync());
    releaseAi.TrySetResult();
    await aiDialog.Locator("button", new LocatorLocatorOptions { HasText = "Apply to Product Form" })
        .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    var previewFocused = await desktop.EvaluateAsync<bool>("() => document.activeElement?.textContent?.includes('Generated Product Draft') === true");
    Check("ai-success-focus", previewFocused);
    await aiDialog.Locator("button", new LocatorLocatorOptions { HasText = "Apply to Product Form" }).ClickAsync();
    Check("ai-apply-closes", await desktop.Locator("[role=dialog]").CountAsync() == 0);
    Check("ai-applies-draft-without-save", await desktop.Locator("#product-workspace-form-name").InputValueAsync() == "WAVE3 AI Draft");

    await desktop.UnrouteAsync("**/api/admin/ai/products/generate");
    await desktop.Locator("button", new PageLocatorOptions { HasText = "Create Product with AI" }).ClickAsync();
    await aiDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    await desktop.RouteAsync("**/api/admin/ai/products/generate", async route =>
    {
        await Task.Delay(300);
        await route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 500,
            ContentType = "application/json",
            Body = "{\"message\":\"Controlled certification failure\"}"
        });
    });
    await aiDialog.Locator("textarea").FillAsync("Controlled failure");
    await aiDialog.Locator("button[type=submit]").ClickAsync();
    var alert = aiDialog.Locator("[role=alert]");
    await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    Check("ai-failure-focus", await alert.EvaluateAsync<bool>("element => document.activeElement === element"));
    Check("ai-failure-restores-controls", !await aiDialog.Locator("textarea").IsDisabledAsync()
        && !await aiDialog.Locator("button[type=submit]").IsDisabledAsync());
    var aiFailureShot = Path.Combine(evidenceDirectory, "wave3-ai-failure.png");
    await desktop.ScreenshotAsync(new PageScreenshotOptions { Path = aiFailureShot, FullPage = true });
    evidence["aiFailureScreenshot"] = aiFailureShot;
    await desktop.UnrouteAsync("**/api/admin/ai/products/generate");

    Check("desktop-no-unexpected-http-failures", desktopFailures.IsEmpty, string.Join("; ", desktopFailures));

    Check("api-relogin-after-ui", await LoginApi());
    var currentProduct = await ApiJson(HttpMethod.Get, $"/api/products/{productId}");
    using var currentProductDocument = JsonDocument.Parse(currentProduct.body);
    var currentRoot = currentProductDocument.RootElement;
    if (currentRoot.TryGetProperty("data", out var currentData))
    {
        currentRoot = currentData;
    }
    var rowVersion = FindString(currentRoot, "rowVersion", "version", "projectionMutationVersion");
    var adminVariants = await ApiJson(HttpMethod.Get, $"/api/products/{productId}/variants");
    using var adminVariantsDocument = JsonDocument.Parse(adminVariants.body);
    var adminVariantArray = FindArray(adminVariantsDocument.RootElement);
    var defaultAdminVariant = adminVariantArray.ValueKind == JsonValueKind.Array
        ? adminVariantArray.EnumerateArray().FirstOrDefault(variant =>
            FindString(variant, "isDefault").Equals("True", StringComparison.OrdinalIgnoreCase))
        : default;
    var adminVariantOnlinePrice = defaultAdminVariant.ValueKind == JsonValueKind.Object
        ? ReadDecimal(defaultAdminVariant, "onlinePrice")
        : decimal.MinValue;
    Check("admin-api-online-price", adminVariantOnlinePrice == onlinePrice,
        adminVariantOnlinePrice.ToString());
    Check("admin-api-has-variants-false", FindString(currentRoot, "hasVariants").Equals("False", StringComparison.OrdinalIgnoreCase));

    var typeFlip = await ApiJson(
        HttpMethod.Put,
        $"/api/products/{productId}",
        ProductBody(categoryId, unitId, hasVariants: true, storefrontPrice: null),
        "wave3-type-flip-" + stamp,
        rowVersion);
    Check("backend-type-flip-rejected", !typeFlip.status.IsSuccess(), $"{(int)typeFlip.status} {Truncate(typeFlip.body, 300)}");
    Check("backend-type-flip-code", typeFlip.body.Contains("PRODUCT_TYPE_CONVERSION_UNSUPPORTED", StringComparison.Ordinal));

    var sql = await ReadSqlEvidence();
    evidence["sql"] = sql;
    Check("sql-tenant", string.Equals(sql["tenantId"]?.ToString(), tenantId, StringComparison.OrdinalIgnoreCase));
    Check("sql-product-simple", sql["hasVariants"] is false);
    Check("sql-unit-pos-price", Convert.ToDecimal(sql["unitSellingPrice"]) == posPrice);
    Check("sql-unit-purchase-price", Convert.ToDecimal(sql["unitPurchasePrice"]) == purchasePrice);
    Check("sql-variant-pos-mirror", Convert.ToDecimal(sql["variantPrice"]) == posPrice);
    Check("sql-variant-online-price", Convert.ToDecimal(sql["variantOnlinePrice"]) == onlinePrice);
    Check("sql-variant-cost-mirror", Convert.ToDecimal(sql["variantCostPrice"]) == purchasePrice);

    var explicitDocument = await WaitForStorefrontPrice(onlinePrice, TimeSpan.FromSeconds(12));
    var updateProjectionRepairNeeded = explicitDocument == null;
    evidence["updateProjectionRepairNeeded"] = updateProjectionRepairNeeded;
    if (updateProjectionRepairNeeded)
    {
        var repair = await ApiJson(HttpMethod.Post, $"/api/admin/sync/product-variants/{productId}");
        Check("update-projection-repair-endpoint", repair.status == HttpStatusCode.OK,
            $"{(int)repair.status} {Truncate(repair.body, 250)}");
        explicitDocument = await WaitForStorefrontPrice(onlinePrice, TimeSpan.FromSeconds(45));
    }
    using (explicitDocument)
    {
        Check("storefront-explicit-price-projected", explicitDocument != null,
            $"expected={onlinePrice}; last={lastStorefrontProbe}");
        if (explicitDocument != null)
        {
            Check("storefront-pos-unit-remains-independent", ReadDecimal(explicitDocument.RootElement, "defaultUnit", "salePrice") == posPrice);
            evidence["storefrontDto"] = JsonSerializer.Deserialize<object>(explicitDocument.RootElement.GetRawText());
        }
    }

    async Task RunAdminMobileArabic()
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            Locale = "ar-EG"
        });
        var page = await context.NewPageAsync();
        var failed = new ConcurrentBag<string>();
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && !response.Url.Contains("/api/auth/session"))
            {
                failed.Add($"{response.Status} {response.Url}");
            }
        };
        await LoginBrowser(page);
        await page.EvaluateAsync("() => localStorage.setItem('i18nextLng', 'ar')");
        await page.GotoAsync($"{webBase}/products/{productId}/edit", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var input = page.Locator("#product-workspace-form-storefront-price");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        Check("mobile-ar-storefront-price", decimal.Parse(await input.InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture) == onlinePrice);
        Check("mobile-ar-rtl", await page.EvaluateAsync<string>("() => document.documentElement.dir") == "rtl");
        var radios = page.Locator("input[name=product-type]");
        Check("mobile-ar-type-locked", await radios.CountAsync() == 2 && await radios.Nth(0).IsDisabledAsync() && await radios.Nth(1).IsDisabledAsync());
        Check("mobile-ar-no-horizontal-overflow", !await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"));
        Check("mobile-ar-no-http-failures", failed.IsEmpty, string.Join("; ", failed));
        var shot = Path.Combine(evidenceDirectory, "wave3-admin-mobile-ar.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = shot, FullPage = true });
        evidence["mobileArabicScreenshot"] = shot;
        await context.CloseAsync();
    }

    await RunAdminMobileArabic();

    async Task RunStorefrontBrowser(int width, int height, string tag, bool arabic)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = arabic ? "ar-EG" : "en-US"
        });
        var page = await context.NewPageAsync();
        var failed = new ConcurrentBag<string>();
        page.Response += (_, response) =>
        {
            if (response.Status >= 400
                && !response.Url.Contains("/api/storefront/auth/me", StringComparison.OrdinalIgnoreCase)
                && !response.Url.Contains("/api/storefront/auth/refresh", StringComparison.OrdinalIgnoreCase))
            {
                failed.Add($"{response.Status} {response.Url}");
            }
        };
        await page.GotoAsync(storefrontOrigin + "/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (arabic)
        {
            await page.EvaluateAsync("() => localStorage.setItem('i18nextLng', 'ar')");
        }
        await page.GotoAsync($"{storefrontOrigin}/products/{Uri.EscapeDataString(slug)}", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(5000);
        var body = await page.Locator("body").InnerTextAsync();
        Check($"storefront-{tag}-product-visible", body.Contains(isolatedName, StringComparison.OrdinalIgnoreCase)
            || body.Contains(isolatedNameAr, StringComparison.OrdinalIgnoreCase), $"len={body.Length}");
        Check($"storefront-{tag}-online-price-visible", body.Contains("83.25", StringComparison.Ordinal)
            || body.Contains("83,25", StringComparison.Ordinal), Truncate(body, 250));
        Check($"storefront-{tag}-no-horizontal-overflow", !await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"));
        if (arabic)
        {
            Check($"storefront-{tag}-rtl", await page.EvaluateAsync<string>("() => document.documentElement.dir") == "rtl");
        }
        Check($"storefront-{tag}-no-http-failures", failed.IsEmpty, string.Join("; ", failed));
        var shot = Path.Combine(evidenceDirectory, $"wave3-storefront-{tag}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = shot, FullPage = true });
        evidence[$"storefront{tag}Screenshot"] = shot;
        await context.CloseAsync();
    }

    await RunStorefrontBrowser(1440, 1000, "desktop-en", arabic: false);
    await RunStorefrontBrowser(390, 844, "mobile-ar", arabic: true);
    await desktopContext.CloseAsync();
}
catch (Exception exception)
{
    Check("harness-exception", false, exception.ToString());
}
finally
{
    if (Guid.TryParse(productId, out _))
    {
        try
        {
            await LoginApi();
            var current = await ApiJson(HttpMethod.Get, $"/api/products/{productId}");
            using var currentDocument = JsonDocument.Parse(current.body);
            var rowVersion = FindString(currentDocument.RootElement, "rowVersion", "version", "projectionMutationVersion");
            var cleanup = await ApiJson(
                HttpMethod.Delete,
                $"/api/products/{productId}",
                idempotencyKey: "wave3-cleanup-" + stamp,
                rowVersion: rowVersion);
            Check("cleanup-isolated-product", cleanup.status is HttpStatusCode.OK or HttpStatusCode.NoContent,
                $"{(int)cleanup.status} {Truncate(cleanup.body, 200)}");
            if (cleanup.status is HttpStatusCode.OK or HttpStatusCode.NoContent)
            {
                var cleanupProjection = await ApiJson(HttpMethod.Post, $"/api/admin/sync/product-variants/{productId}");
                Check("cleanup-projection-tombstone", cleanupProjection.status == HttpStatusCode.OK,
                    $"{(int)cleanupProjection.status} {Truncate(cleanupProjection.body, 200)}");
                var (storefrontStatus, _) = await ReadStorefrontProduct();
                Check("cleanup-storefront-removed", storefrontStatus == 404, $"status={storefrontStatus}");
            }
        }
        catch (Exception cleanupException)
        {
            Check("cleanup-isolated-product", false, cleanupException.Message);
        }
    }

    evidence["stamp"] = stamp;
    evidence["productId"] = productId;
    evidence["tenantId"] = tenantId;
    evidence["tenantSlug"] = tenantSlug;
    evidence["storefrontOrigin"] = storefrontOrigin;
    evidence["slug"] = slug;
    evidence["failureCount"] = failures.Count;
    evidence["failures"] = failures;
    var evidencePath = Path.Combine(evidenceDirectory, "wave3-evidence.json");
    await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"EVIDENCE {evidencePath}");
}

return failures.Count == 0 ? 0 : 2;

static class HttpStatusCodeExtensions
{
    public static bool IsSuccess(this HttpStatusCode statusCode) => (int)statusCode is >= 200 and <= 299;
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Npgsql;

var email = Environment.GetEnvironmentVariable("WAVE2_EMAIL")
    ?? throw new InvalidOperationException("WAVE2_EMAIL is required.");
var password = Environment.GetEnvironmentVariable("WAVE2_PASSWORD")
    ?? throw new InvalidOperationException("WAVE2_PASSWORD is required.");

var apiBase = "http://localhost:5010";
var webBase = "http://localhost:5173";
if (Environment.GetEnvironmentVariable("WAVE2_AR_ONLY") == "1")
{
    using var pw = await Playwright.CreateAsync();
    await using var br = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = false, SlowMo = 60 });
    var ctx = await br.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }, Locale = "ar-EG" });
    var pg = await ctx.NewPageAsync();
    var failedAr = new List<string>();
    pg.Response += (_, r) => { if (r.Status >= 400 && !r.Url.Contains("/api/auth/session")) failedAr.Add($"{r.Status} {r.Url}"); };
    await pg.GotoAsync(webBase + "/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await pg.EvaluateAsync("() => localStorage.setItem('i18nextLng', 'ar')");
    await pg.GotoAsync(webBase + "/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await pg.Locator("input[type=text]").FillAsync(email);
    await pg.Locator("input[type=password]").FillAsync(password);
    await pg.Locator("button[type=submit]").ClickAsync();
    await pg.WaitForURLAsync(u => !u.Contains("/login", StringComparison.OrdinalIgnoreCase), new PageWaitForURLOptions { Timeout = 30_000 });
    await pg.GotoAsync(webBase + "/products", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await pg.WaitForTimeoutAsync(2000);
    var dir = await pg.EvaluateAsync<string>("() => document.documentElement.getAttribute('dir') ?? document.body.getAttribute('dir') ?? ''");
    var hasTableAr = await pg.Locator("table").CountAsync() > 0;
    var overflowX = await pg.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
    Console.WriteLine($"AR dir={dir} table={hasTableAr} overflowX={overflowX} failed={string.Join(";", failedAr)}");
    await pg.ScreenshotAsync(new PageScreenshotOptions { Path = System.IO.Path.Combine(AppContext.BaseDirectory, "wave2-ar-desktop.png"), FullPage = true });
    bool arOk = hasTableAr && failedAr.Count == 0 && dir.Contains("rtl", StringComparison.OrdinalIgnoreCase);
    Console.WriteLine(arOk ? "PASS ui-ar-rtl" : "FAIL ui-ar-rtl");
    return arOk ? 0 : 2;
}
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var isolatedName = $"WAVE2 CERT {stamp}";
var evidence = new Dictionary<string, object?>();
var failures = new List<string>();
void Check(string name, bool ok, string detail = "")
{
    evidence[name] = ok ? $"PASS {detail}".Trim() : $"FAIL {detail}".Trim();
    if (!ok) failures.Add(name);
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} {detail}");
}

// ---------- API client with manual cookies (Secure cookies over http://localhost) ----------
var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = false };
using var api = new HttpClient(handler) { BaseAddress = new Uri(apiBase), Timeout = TimeSpan.FromSeconds(30) };
var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var cookieJar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
void HarvestCookies(HttpResponseMessage resp)
{
    if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        foreach (var sc in setCookies)
        {
            var semi = sc.IndexOf(';');
            var pair = semi >= 0 ? sc[..semi] : sc;
            var eq = pair.IndexOf('=');
            if (eq > 0) cookieJar[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
        }
}
async Task<HttpResponseMessage> SendApi(HttpRequestMessage req)
{
    if (cookieJar.Count > 0)
        req.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookieJar.Select(kv => $"{kv.Key}={kv.Value}")));
    if ((req.Method == HttpMethod.Post || req.Method == HttpMethod.Put || req.Method == HttpMethod.Patch || req.Method == HttpMethod.Delete)
        && cookieJar.TryGetValue("csrf_token", out var csrf) && !string.IsNullOrEmpty(csrf)
        && !req.Headers.Contains("X-CSRF-Token"))
        req.Headers.TryAddWithoutValidation("X-CSRF-Token", csrf);
    var resp = await api.SendAsync(req);
    HarvestCookies(resp);
    return resp;
}
async Task<string> GetApiString(string url)
{
    using var req = new HttpRequestMessage(HttpMethod.Get, url);
    using var resp = await SendApi(req);
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadAsStringAsync();
}
async Task<HttpResponseMessage> PostApiJson(string url, object body)
{
    using var req = new HttpRequestMessage(HttpMethod.Post, url);
    req.Content = JsonContent.Create(body);
    return await SendApi(req);
}

var loginResp = await api.PostAsJsonAsync("/api/auth/login", new { username = email, password });
HarvestCookies(loginResp);
Check("api-login", loginResp.IsSuccessStatusCode, ((int)loginResp.StatusCode).ToString());
if (!loginResp.IsSuccessStatusCode) { Console.WriteLine(await loginResp.Content.ReadAsStringAsync()); return 1; }
var sessionJson = await GetApiString("/api/auth/session");
Check("api-session", sessionJson.Contains("user", StringComparison.OrdinalIgnoreCase), $"len={sessionJson.Length}");

var catsJson = await GetApiString("/api/categories");
using var catsDoc = JsonDocument.Parse(catsJson);
string categoryId = "";
if (catsDoc.RootElement.ValueKind == JsonValueKind.Array && catsDoc.RootElement.GetArrayLength() > 0)
    categoryId = catsDoc.RootElement[0].GetProperty("id").GetString() ?? "";
else if (catsDoc.RootElement.TryGetProperty("data", out var catsData) && catsData.ValueKind == JsonValueKind.Array && catsData.GetArrayLength() > 0)
    categoryId = catsData[0].GetProperty("id").GetString() ?? "";
Check("api-category", Guid.TryParse(categoryId, out _), categoryId);

var unitsJson = await GetApiString("/api/units");
using var unitsDoc = JsonDocument.Parse(unitsJson);
string unitId = "";
if (unitsDoc.RootElement.ValueKind == JsonValueKind.Array && unitsDoc.RootElement.GetArrayLength() > 0)
    unitId = unitsDoc.RootElement[0].GetProperty("id").GetString() ?? "";
Check("api-unit", Guid.TryParse(unitId, out _), unitId);

static string GetProp(JsonElement el, params string[] names)
{
    foreach (var n in names)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(n, out var v))
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
    }
    return "";
}

// ---------- Create isolated variant product via API ----------
string skuBase = "W2C" + stamp.Replace("-", "").Replace(":", "");
var createBody = new Dictionary<string, object?>
{
    ["name"] = isolatedName,
    ["categoryId"] = categoryId,
    ["purchasePrice"] = 5,
    ["salePrice"] = 12,
    ["onlinePrice"] = 12,
    ["priceIncludesTax"] = false,
    ["taxRate"] = 0,
    ["isActive"] = true,
    ["trackStock"] = true,
    ["hasVariants"] = true,
    ["publishStatus"] = 0,
    ["isOnlineVisible"] = false,
    ["isSearchable"] = false,
    ["isFeatured"] = false,
    ["productUnits"] = new object[] { new Dictionary<string, object?> { ["unitId"] = unitId, ["conversionFactor"] = 1, ["purchasePrice"] = 5, ["sellingPrice"] = 12, ["isDefault"] = true, ["isBaseUnit"] = true } },
    ["variants"] = new object[]
    {
        new Dictionary<string, object?> { ["variantId"] = null, ["sku"] = skuBase + "-PLAIN", ["barcode"] = (object?)null, ["price"] = 12, ["onlinePrice"] = 12, ["costPrice"] = 5, ["isActive"] = true, ["isDefault"] = true, ["attributes"] = new Dictionary<string,string> { ["Style"] = "Plain" } },
        new Dictionary<string, object?> { ["variantId"] = null, ["sku"] = skuBase + "-MEDIUM", ["barcode"] = (object?)null, ["price"] = 12, ["onlinePrice"] = 12, ["costPrice"] = 5, ["isActive"] = true, ["isDefault"] = false, ["attributes"] = new Dictionary<string,string> { ["Style"] = "Medium" } },
        new Dictionary<string, object?> { ["variantId"] = null, ["sku"] = skuBase + "-DARK", ["barcode"] = (object?)null, ["price"] = 12, ["onlinePrice"] = 12, ["costPrice"] = 5, ["isActive"] = true, ["isDefault"] = false, ["attributes"] = new Dictionary<string,string> { ["Style"] = "Dark" } },
    },
};
using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/products");
createReq.Headers.Add("Idempotency-Key", "w2cert-create-" + stamp);
createReq.Content = JsonContent.Create(createBody);
var createResp = await SendApi(createReq);
var createText = await createResp.Content.ReadAsStringAsync();
Check("api-create-variant-product", createResp.IsSuccessStatusCode, $"{(int)createResp.StatusCode} {createText[..Math.Min(300, createText.Length)]}");
if (!createResp.IsSuccessStatusCode) return 1;
using var createDoc = JsonDocument.Parse(createText);
var created = createDoc.RootElement;
string productId = GetProp(created, "id", "Id");
if (string.IsNullOrEmpty(productId) && created.TryGetProperty("data", out var cd)) productId = GetProp(cd, "id", "Id");
Check("api-create-id", Guid.TryParse(productId, out _), productId);
string rowVersion = GetProp(created, "rowVersion", "RowVersion", "version", "Version", "projectionMutationVersion", "ProjectionMutationVersion");
evidence["create-rowVersion"] = rowVersion;
Console.WriteLine($"PRODUCT {productId} RV={rowVersion}");

// Variants via API
var variantsText = await GetApiString($"/api/products/{productId}/variants");
using var variantsDoc = JsonDocument.Parse(variantsText);
var apiVariants = variantsDoc.RootElement.ValueKind == JsonValueKind.Array ? variantsDoc.RootElement.EnumerateArray().ToList() : new List<JsonElement>();
Check("api-variants-count-3", apiVariants.Count == 3, $"count={apiVariants.Count}");
var apiSkus = apiVariants.Select(v => GetProp(v, "sku", "Sku", "SKU")).OrderBy(s => s).ToList();
evidence["api-skus-initial"] = string.Join(",", apiSkus);
var variantIds = apiVariants.Select(v => GetProp(v, "id", "Id", "variantId", "VariantId")).ToList();
var darkVariant = apiVariants.FirstOrDefault(v => GetProp(v, "sku", "Sku", "SKU").EndsWith("DARK", StringComparison.OrdinalIgnoreCase));
string darkId = darkVariant.ValueKind != JsonValueKind.Undefined ? GetProp(darkVariant, "id", "Id") : "";

// can-delete clean variant should allow
if (!string.IsNullOrEmpty(darkId))
{
    var cdText = await GetApiString($"/api/products/{productId}/variants/{darkId}/can-delete");
    Check("api-can-delete-clean-variant", cdText.Contains("true", StringComparison.OrdinalIgnoreCase), cdText[..Math.Min(200, cdText.Length)]);
    evidence["can-delete-clean"] = cdText[..Math.Min(500, cdText.Length)];
}

// ---------- Finding 4 live: remove Dark, add Mojahed via PUT ----------
var prodText = await GetApiString($"/api/products/{productId}");
using var prodDoc = JsonDocument.Parse(prodText);
var prodEl = prodDoc.RootElement;
if (prodEl.TryGetProperty("data", out var pd)) prodEl = pd;
rowVersion = GetProp(prodEl, "rowVersion", "RowVersion", "version", "Version", "projectionMutationVersion", "ProjectionMutationVersion");
if (string.IsNullOrEmpty(rowVersion)) rowVersion = "1";
var plainV = apiVariants.First(v => GetProp(v, "sku", "Sku", "SKU").EndsWith("PLAIN", StringComparison.OrdinalIgnoreCase));
var medV = apiVariants.First(v => GetProp(v, "sku", "Sku", "SKU").EndsWith("MEDIUM", StringComparison.OrdinalIgnoreCase));
static Dictionary<string,string> Attrs(JsonElement v)
{
    var d = new Dictionary<string,string>();
    if (v.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Array)
        foreach (var it in a.EnumerateArray())
            d[GetProp(it, "name", "Name", "key", "Key")] = GetProp(it, "value", "Value");
    else if (v.TryGetProperty("attributes", out var o) && o.ValueKind == JsonValueKind.Object)
        foreach (var p in o.EnumerateObject()) d[p.Name] = p.Value.ToString();
    return d;
}
var updateBody = new Dictionary<string, object?>
{
    ["name"] = isolatedName,
    ["categoryId"] = categoryId,
    ["purchasePrice"] = 5,
    ["salePrice"] = 12,
    ["onlinePrice"] = 12,
    ["priceIncludesTax"] = false,
    ["taxRate"] = 0,
    ["isActive"] = true,
    ["trackStock"] = true,
    ["hasVariants"] = true,
    ["publishStatus"] = 0,
    ["isOnlineVisible"] = false,
    ["isSearchable"] = false,
    ["isFeatured"] = false,
    ["variants"] = new object[]
    {
        new Dictionary<string, object?> { ["variantId"] = GetProp(plainV, "id", "Id"), ["sku"] = GetProp(plainV, "sku", "Sku", "SKU"), ["price"] = 12, ["onlinePrice"] = 12, ["costPrice"] = 5, ["isActive"] = true, ["isDefault"] = true, ["attributes"] = Attrs(plainV) },
        new Dictionary<string, object?> { ["variantId"] = GetProp(medV, "id", "Id"), ["sku"] = GetProp(medV, "sku", "Sku", "SKU"), ["price"] = 12, ["onlinePrice"] = 12, ["costPrice"] = 5, ["isActive"] = true, ["isDefault"] = false, ["attributes"] = Attrs(medV) },
        new Dictionary<string, object?> { ["variantId"] = null, ["sku"] = skuBase + "-MOJAHED", ["price"] = 12, ["onlinePrice"] = 12, ["costPrice"] = 5, ["isActive"] = true, ["isDefault"] = false, ["attributes"] = new Dictionary<string,string> { ["Style"] = "Mojahed" } },
    },
};
using var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{productId}");
putReq.Headers.Add("Idempotency-Key", "w2cert-update-" + stamp);
putReq.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
putReq.Content = JsonContent.Create(updateBody);
var putResp = await SendApi(putReq);
var putText = await putResp.Content.ReadAsStringAsync();
Check("api-finding4-update", putResp.IsSuccessStatusCode, $"{(int)putResp.StatusCode} {putText[..Math.Min(300, putText.Length)]}");

var variants2Text = await GetApiString($"/api/products/{productId}/variants");
using var v2Doc = JsonDocument.Parse(variants2Text);
var v2 = v2Doc.RootElement.EnumerateArray().ToList();
var v2Skus = v2.Select(x => GetProp(x, "sku", "Sku", "SKU")).OrderBy(s => s).ToList();
bool darkGone = v2Skus.All(s => !s.EndsWith("DARK", StringComparison.OrdinalIgnoreCase));
bool mojahedPresent = v2Skus.Any(s => s.EndsWith("MOJAHED", StringComparison.OrdinalIgnoreCase));
Check("api-finding4-dark-gone", darkGone, string.Join(",", v2Skus));
Check("api-finding4-mojahed-present", mojahedPresent, string.Join(",", v2Skus));
Check("api-finding4-count-3", v2.Count == 3, $"count={v2.Count}");
evidence["api-skus-after"] = string.Join(",", v2Skus);

// ---------- Finding 2 live: duplicate SKU must 409 ----------
var prodText2 = await GetApiString($"/api/products/{productId}");
using var prodDoc2 = JsonDocument.Parse(prodText2);
var prodEl2 = prodDoc2.RootElement;
if (prodEl2.TryGetProperty("data", out var pd2)) prodEl2 = pd2;
var rv2 = GetProp(prodEl2, "rowVersion", "RowVersion", "version", "Version", "projectionMutationVersion", "ProjectionMutationVersion");
if (string.IsNullOrEmpty(rv2)) rv2 = rowVersion;
var dupSku = v2Skus.First();
var dupBody = new Dictionary<string, object?>
{
    ["name"] = isolatedName,
    ["categoryId"] = categoryId,
    ["purchasePrice"] = 5,
    ["salePrice"] = 12,
    ["onlinePrice"] = 12,
    ["priceIncludesTax"] = false,
    ["taxRate"] = 0,
    ["isActive"] = true,
    ["trackStock"] = true,
    ["hasVariants"] = true,
    ["publishStatus"] = 0,
    ["isOnlineVisible"] = false,
    ["isSearchable"] = false,
    ["isFeatured"] = false,
    ["variants"] = v2.Select(x => (object)new Dictionary<string, object?> { ["variantId"] = GetProp(x, "id", "Id"), ["sku"] = dupSku, ["price"] = 12, ["onlinePrice"] = 12, ["costPrice"] = 5, ["isActive"] = true, ["isDefault"] = GetProp(x, "isDefault", "IsDefault") == "True", ["attributes"] = Attrs(x) }).ToArray(),
};
using var dupReq = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{productId}");
dupReq.Headers.Add("Idempotency-Key", "w2cert-dup-" + stamp);
dupReq.Headers.TryAddWithoutValidation("If-Match", $"\"{rv2}\"");
dupReq.Content = JsonContent.Create(dupBody);
var dupResp = await SendApi(dupReq);
var dupText = await dupResp.Content.ReadAsStringAsync();
Check("api-finding2-dup-sku-409", dupResp.StatusCode == HttpStatusCode.Conflict, $"{(int)dupResp.StatusCode} {dupText[..Math.Min(400, dupText.Length)]}");
evidence["dup-response"] = dupText[..Math.Min(600, dupText.Length)];

// verify no mutation after dup rejection
var variants3Text = await GetApiString($"/api/products/{productId}/variants");
using var v3Doc = JsonDocument.Parse(variants3Text);
var v3Skus = v3Doc.RootElement.EnumerateArray().Select(x => GetProp(x, "sku", "Sku", "SKU")).OrderBy(s => s).ToList();
Check("api-finding2-no-mutation", v3Skus.SequenceEqual(v2Skus), string.Join(",", v3Skus));

// ---------- Legacy direct mutation must be 410 ----------
var legacyResp = await PostApiJson($"/api/products/{productId}/variants", new { sku = "X" });
Check("api-legacy-post-410", legacyResp.StatusCode == HttpStatusCode.Gone, ((int)legacyResp.StatusCode).ToString());

// ---------- DB read-only reconciliation ----------
try
{
    var cs = JsonDocument.Parse(await File.ReadAllTextAsync("PointOfSale.API/appsettings.Development.local.json"))
        .RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
    using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();
    using var cmd = new NpgsqlCommand("SELECT \"Id\", \"SKU\", \"IsDeleted\", \"IsActive\", \"AttributeCombinationKey\" FROM \"ProductVariants\" WHERE \"ProductId\" = @p ORDER BY \"SKU\"", conn);
    cmd.Parameters.AddWithValue("p", Guid.Parse(productId));
    using var rdr = await cmd.ExecuteReaderAsync();
    var dbRows = new List<string>();
    var dbLive = new List<string>();
    string? darkDel = null;
    while (await rdr.ReadAsync())
    {
        var sku = rdr.GetValue(1)?.ToString() ?? "";
        var isDel = rdr.GetBoolean(2);
        var isActive = rdr.GetBoolean(3);
        dbRows.Add($"{rdr.GetGuid(0)}|{sku}|del={isDel}|active={isActive}");
        if (!isDel) dbLive.Add(sku);
        if (sku.EndsWith("DARK", StringComparison.OrdinalIgnoreCase)) darkDel = $"del={isDel}|active={isActive}";
    }
    Check("db-live-matches-api", dbLive.OrderBy(s => s).SequenceEqual(v2Skus), string.Join(";", dbLive));
    Check("db-dark-soft-deleted", darkDel == "del=True|active=False", darkDel ?? "DARK row missing");
    evidence["db-rows"] = string.Join(";", dbRows);
    await rdr.CloseAsync();
    using var idx = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE indexname='IX_ProductVariants_SKU'", conn);
    var def = (await idx.ExecuteScalarAsync())?.ToString() ?? "";
    Check("db-sku-index", def.Contains("TenantId") && def.Contains("SKU") && def.Contains("IsDeleted") && def.Contains("SemanticLifecycleState"), def);
    evidence["sku-index"] = def;
}
catch (Exception ex) { Check("db-reads", false, ex.Message); }

// ---------- Headed Edge desktop + mobile ----------
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = false, SlowMo = 60 });

async Task<(string failed, string consoleErr, string skuCells, string enterResult, string shot)> RunViewport(int w, int h, string tag)
{
    var ctx = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = w, Height = h } });
    var page = await ctx.NewPageAsync();
    var failed = new List<string>();
    var cerr = new List<string>();
    page.Response += (_, r) => { if (r.Status >= 400 && !r.Url.Contains("/api/auth/session")) failed.Add($"{r.Status} {r.Url}"); };
    page.Console += (_, m) => { if (m.Type == "error") cerr.Add(m.Text[..Math.Min(200, m.Text.Length)]); };
    await page.GotoAsync(webBase + "/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("input[type=text]").FillAsync(email);
    await page.Locator("input[type=password]").FillAsync(password);
    await page.Locator("button[type=submit]").ClickAsync();
    await page.WaitForURLAsync(u => !u.Contains("/login", StringComparison.OrdinalIgnoreCase), new PageWaitForURLOptions { Timeout = 30_000 });
    await page.GotoAsync(webBase + "/products", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    var hasTable = await page.Locator("table").CountAsync() > 0;
    Check($"ui-{tag}-products-table", hasTable, $"w={w} h={h}");
    // open isolated product edit
    await page.GotoAsync($"{webBase}/products/{productId}/edit?focus=variants", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.WaitForTimeoutAsync(2500);
    var bodyText = await page.Locator("body").InnerTextAsync();
    bool showsMojahed = bodyText.Contains("Mojahed", StringComparison.OrdinalIgnoreCase);
    bool showsDark = bodyText.Contains("Dark", StringComparison.OrdinalIgnoreCase) && bodyText.Contains(skuBase + "-DARK");
    Check($"ui-{tag}-mojahed-visible", showsMojahed, $"len={bodyText.Length}");
    Check($"ui-{tag}-dark-not-resurrected", !showsDark, showsDark ? "DARK STILL VISIBLE" : "dark gone");
    // collect SKU cells
    string skus = "";
    try
    {
        var inputs = page.Locator("table input[type=text]");
        var n = Math.Min(await inputs.CountAsync(), 12);
        var vals = new List<string>();
        for (int i = 0; i < n; i++) vals.Add(await inputs.Nth(i).InputValueAsync());
        skus = string.Join(",", vals);
    }
    catch (Exception ex) { skus = "ERR " + ex.Message; }
    evidence[$"ui-{tag}-skus"] = skus;
    // Option Enter test on the isolated product's own variant builder (no save click, no mutation persisted)
    string enterResult = "SKIPPED";
    try
    {
        var optName = page.Locator("[data-option-group] input[type=text]").First;
        if (await optName.CountAsync() == 0)
        {
            var btns = (await page.Locator("button").AllTextContentsAsync()).Where(s => !string.IsNullOrWhiteSpace(s)).Take(40).ToList();
            evidence[$"ui-{tag}-buttons-edit"] = string.Join(" | ", btns);
            enterResult = "NO-OPTION-INPUT";
            Check($"ui-{tag}-option-enter-present", false, enterResult);
        }
        else
        {
            var urlBefore = page.Url;
            await optName.ClickAsync();
            await optName.FillAsync("Style");
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForTimeoutAsync(800);
            var urlAfter = page.Url;
            var focused = await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('placeholder') ?? document.activeElement?.tagName");
            enterResult = $"urlSame={urlBefore == urlAfter} focus={focused}";
            Check($"ui-{tag}-option-enter-no-submit", urlBefore == urlAfter, enterResult);
            Check($"ui-{tag}-option-enter-focus-value", (focused ?? "").Contains("Type a value", StringComparison.OrdinalIgnoreCase), focused ?? "");
            // value-Enter must-not-break: type draft value, Enter adds chip without submit
            var valInput = page.Locator("[data-option-value-input]").First;
            if (await valInput.CountAsync() > 0)
            {
                await valInput.ClickAsync();
                await valInput.FillAsync("W2Probe");
                await page.Keyboard.PressAsync("Enter");
                await page.WaitForTimeoutAsync(800);
                var afterVal = await page.Locator("body").InnerTextAsync();
                bool chipAdded = afterVal.Contains("W2Probe");
                bool stillSameUrl = page.Url == urlAfter;
                Check($"ui-{tag}-option-value-enter-adds", chipAdded && stillSameUrl, $"chip={chipAdded} urlSame={stillSameUrl}");
                enterResult += $" valueChip={chipAdded}";
            }
        }
    }
    catch (Exception ex) { enterResult = "ERR " + ex.Message; Check($"ui-{tag}-option-enter", false, enterResult); }
    var shotPath = System.IO.Path.Combine(AppContext.BaseDirectory, $"wave2-{tag}.png");
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = shotPath, FullPage = true });
    evidence[$"ui-{tag}-shot"] = shotPath;
    await ctx.CloseAsync();
    return (string.Join(";", failed), string.Join(";", cerr), skus, enterResult, shotPath);
}

var desk = await RunViewport(1440, 1000, "desktop");
Check("ui-desktop-no-failed-api", string.IsNullOrEmpty(desk.failed), desk.failed);
evidence["desktop-console-errors"] = desk.consoleErr;
var mob = await RunViewport(390, 844, "mobile");
Check("ui-mobile-no-failed-api", string.IsNullOrEmpty(mob.failed), mob.failed);
evidence["mobile-console-errors"] = mob.consoleErr;

// ---------- Re-login: headed UI logins rotate the POS session, so refresh API auth ----------
try
{
    cookieJar.Clear();
    using var relogReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
    relogReq.Content = JsonContent.Create(new { username = email, password });
    using var relogResp = await api.SendAsync(relogReq);
    HarvestCookies(relogResp);
    Check("api-relogin", relogResp.IsSuccessStatusCode, ((int)relogResp.StatusCode).ToString());
}
catch (Exception ex) { Check("api-relogin", false, ex.Message); }

// ---------- Historic protection read-only probe (no mutation) ----------
try
{
    using var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/products?page=1&pageSize=50");
    using var listResp = await SendApi(listReq);
    var listText = await listResp.Content.ReadAsStringAsync();
    if (!listResp.IsSuccessStatusCode) { Check("api-historic-can-delete-shape", false, $"{(int)listResp.StatusCode} {listText[..Math.Min(300, listText.Length)]}"); listText = "[]"; }
    else
    {
    using var listDoc = JsonDocument.Parse(listText);
    var arr = listDoc.RootElement.ValueKind == JsonValueKind.Array ? listDoc.RootElement : (listDoc.RootElement.TryGetProperty("data", out var ld) ? ld : default);
    string otherId = "";
    if (arr.ValueKind == JsonValueKind.Array)
        foreach (var it in arr.EnumerateArray())
        {
            var cid = GetProp(it, "id", "Id");
            if (!string.IsNullOrEmpty(cid) && cid != productId) { otherId = cid; break; }
        }
    if (!string.IsNullOrEmpty(otherId))
    {
        using var probeReq = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{otherId}/can-delete");
        if (cookieJar.Count > 0) probeReq.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookieJar.Select(kv => $"{kv.Key}={kv.Value}")));
        using var probeResp = await api.SendAsync(probeReq);
        var probeText = await probeResp.Content.ReadAsStringAsync();
        Check("api-historic-can-delete-shape", probeResp.IsSuccessStatusCode && probeText.Contains("canDelete", StringComparison.OrdinalIgnoreCase), $"{(int)probeResp.StatusCode} {probeText[..Math.Min(300, probeText.Length)]}");
        evidence["historic-can-delete"] = probeText[..Math.Min(600, probeText.Length)];
    }
    else Check("api-historic-can-delete-shape", true, "no other product to probe");
    }
}
catch (Exception ex) { Check("api-historic-can-delete-shape", false, ex.Message); }

// ---------- Cleanup isolated product (clean delete should succeed) ----------
try
{
    using var finReq = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId}");
    using var finResp = await SendApi(finReq);
    var prodFinal = await finResp.Content.ReadAsStringAsync();
    if (!finResp.IsSuccessStatusCode) { Check("api-cleanup-delete", false, $"GET {(int)finResp.StatusCode} {prodFinal[..Math.Min(300, prodFinal.Length)]}"); }
    else
    {
    using var pfDoc = JsonDocument.Parse(prodFinal);
    var pfEl = pfDoc.RootElement;
    if (pfEl.TryGetProperty("data", out var pfd)) pfEl = pfd;
    var rvFinal = GetProp(pfEl, "rowVersion", "RowVersion", "version", "Version", "projectionMutationVersion", "ProjectionMutationVersion");
    using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{productId}");
    delReq.Headers.Add("Idempotency-Key", "w2cert-del-" + stamp);
    delReq.Headers.TryAddWithoutValidation("If-Match", $"\"{rvFinal}\"");
    var delResp = await SendApi(delReq);
    var delText = await delResp.Content.ReadAsStringAsync();
    Check("api-cleanup-delete", delResp.IsSuccessStatusCode, $"{(int)delResp.StatusCode} {delText[..Math.Min(200, delText.Length)]}");
    }
    // sweep earlier isolated leftovers from rotated-session runs (same isolated prefix, never business data)
    try
    {
        using var sweepReq = new HttpRequestMessage(HttpMethod.Get, "/api/products?search=WAVE2%20CERT&page=1&pageSize=50");
        using var sweepResp = await SendApi(sweepReq);
        var sweepText = await sweepResp.Content.ReadAsStringAsync();
        if (sweepResp.IsSuccessStatusCode)
        {
            using var sweepDoc = JsonDocument.Parse(sweepText);
            var sarr = sweepDoc.RootElement.ValueKind == JsonValueKind.Array ? sweepDoc.RootElement : (sweepDoc.RootElement.TryGetProperty("data", out var sd) ? sd : default);
            int swept = 0;
            if (sarr.ValueKind == JsonValueKind.Array)
                foreach (var it in sarr.EnumerateArray())
                {
                    var sid = GetProp(it, "id", "Id");
                    var sname = GetProp(it, "name", "Name");
                    if (string.IsNullOrEmpty(sid) || !sname.StartsWith("WAVE2 CERT", StringComparison.Ordinal)) continue;
                    try
                    {
                        var gText = await GetApiString($"/api/products/{sid}");
                        using var gDoc = JsonDocument.Parse(gText);
                        var gEl = gDoc.RootElement;
                        if (gEl.TryGetProperty("data", out var gd)) gEl = gd;
                        var grv = GetProp(gEl, "rowVersion", "RowVersion", "version", "Version", "projectionMutationVersion", "ProjectionMutationVersion");
                        using var sdel = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{sid}");
                        sdel.Headers.Add("Idempotency-Key", "w2cert-sweep-" + stamp + "-" + swept);
                        sdel.Headers.TryAddWithoutValidation("If-Match", $"\"{grv}\"");
                        using var sdelResp = await SendApi(sdel);
                        if (sdelResp.IsSuccessStatusCode) swept++;
                    }
                    catch { }
                }
            evidence["cleanup-swept"] = swept.ToString();
        }
    }
    catch (Exception ex) { evidence["cleanup-sweep-err"] = ex.Message; }
}
catch (Exception ex) { Check("api-cleanup-delete", false, ex.Message); }

await File.WriteAllTextAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"FAILURES: {failures.Count}");
return failures.Count == 0 ? 0 : 2;

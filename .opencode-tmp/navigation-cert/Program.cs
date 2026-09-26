using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Playwright;

const string AdminBaseUrl = "http://localhost:5173";
const string StorefrontBaseUrl = "http://ahramstore.localhost:5174";
const string NavigationPath = "/api/admin/cms/navigation";
const string CertLabel = "Navigation Cert";
const string CertLabelAr = "اختبار التنقل";

var username = Environment.GetEnvironmentVariable("NAV_CERT_USER")
    ?? throw new InvalidOperationException("NAV_CERT_USER missing");
var password = Environment.GetEnvironmentVariable("NAV_CERT_PASS")
    ?? throw new InvalidOperationException("NAV_CERT_PASS missing");

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true
});
await using var adminContext = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
});

var admin = await adminContext.NewPageAsync();
var browserErrors = new List<string>();
admin.Console += (_, message) =>
{
    if (message.Type == "error" &&
        !message.Text.Contains("401 (Unauthorized)", StringComparison.Ordinal) &&
        !message.Text.Contains("Failed to load resource", StringComparison.Ordinal))
    {
        browserErrors.Add($"console: {message.Text}");
    }
};
admin.PageError += (_, error) => browserErrors.Add($"page: {error}");

JsonNode? originalSnapshot = null;
JsonNode? originalPublicNavigation = null;
JsonNode? originalStoreConfig = null;
JsonNode? originalFooter = null;
var mayNeedRestore = false;

try
{
    await LoginAsync(admin, username, password);
    await OpenNavigationEditorAsync(admin);

    originalSnapshot = ParseJson((await BrowserFetchAsync(admin, NavigationPath)).Text);
    var baselineRowVersion = originalSnapshot["rowVersion"]?.GetValue<string>();
    Check(uint.TryParse(baselineRowVersion, out var parsedBaselineVersion) && parsedBaselineVersion > 0, $"Baseline RowVersion is valid ({baselineRowVersion})");
    Check(originalSnapshot["hasPendingChanges"]?.GetValue<bool>() == false, "Original navigation has no pending changes");

    await using var initialPublicContext = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
    });
    var initialPublic = await initialPublicContext.NewPageAsync();
    var initialDocument = await initialPublic.GotoAsync(StorefrontBaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Check(initialDocument?.Status == 200, "Initial storefront document returns 200");
    originalPublicNavigation = ParseJson((await BrowserFetchAsync(initialPublic, "/api/storefront/navigation?lang=en")).Text);
    originalStoreConfig = ParseJson((await BrowserFetchAsync(initialPublic, "/api/storefront/store-config")).Text);
    originalFooter = ParseJson((await BrowserFetchAsync(initialPublic, "/api/storefront/footer?lang=en")).Text);

    Console.WriteLine("PHASE create-edit-disable-reorder-save-reload");
    await admin.GetByRole(AriaRole.Button, new() { Name = "Add Link", Exact = true }).ClickAsync();
    var addDialog = admin.GetByRole(AriaRole.Dialog, new() { Name = "Add Link", Exact = true });
    await addDialog.Locator("select").Nth(0).SelectOptionAsync("cms");
    await addDialog.Locator("select").Nth(1).SelectOptionAsync(new SelectOptionValue { Label = "About Us" });
    await addDialog.Locator("input[type=text]").Nth(0).FillAsync(CertLabelAr);
    await addDialog.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

    await admin.GetByRole(AriaRole.Button, new() { Name = "Edit navigation link" }).Last.ClickAsync();
    await admin.GetByLabel("Link label").FillAsync(CertLabel);
    await admin.GetByLabel("Arabic label").FillAsync(CertLabelAr);
    await admin.GetByLabel("Target").SelectOptionAsync(new SelectOptionValue { Label = "Contact Us" });
    await admin.GetByLabel("Enabled").UncheckAsync();
    await admin.GetByRole(AriaRole.Button, new() { Name = "Save navigation link edits" }).ClickAsync();
    await admin.GetByRole(AriaRole.Button, new() { Name = "Move navigation link up" }).Last.ClickAsync();

    mayNeedRestore = true;
    var disabledSaveResponse = await admin.RunAndWaitForResponseAsync(
        async () => await admin.GetByRole(AriaRole.Button, new() { Name = "Save Draft" }).ClickAsync(),
        response => response.Request.Method == "PUT" && IsNavigationUrl(response.Url));
    Check(disabledSaveResponse.Status == 200, "Disabled/reordered draft save returns 200");
    var disabledSnapshot = ParseJson(await disabledSaveResponse.TextAsync());
    AssertCertLink(disabledSnapshot, enabled: false, expectedIndex: 1, expectedSlug: "contact");

    await admin.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await admin.GetByRole(AriaRole.Heading, new() { Name = "Navigation", Exact = true }).WaitForAsync();
    Check(await admin.GetByText(CertLabel, new() { Exact = true }).IsVisibleAsync(), "Saved disposable link survives reload");
    Check(await admin.GetByText("Disabled", new() { Exact = true }).IsVisibleAsync(), "Disabled state survives reload");
    var reloadedSnapshot = ParseJson((await BrowserFetchAsync(admin, NavigationPath)).Text);
    AssertCertLink(reloadedSnapshot, enabled: false, expectedIndex: 1, expectedSlug: "contact");

    Console.WriteLine("PHASE enable-save-publish");
    await admin.GetByRole(AriaRole.Button, new() { Name = "Edit navigation link" }).Nth(1).ClickAsync();
    await admin.GetByLabel("Enabled").CheckAsync();
    await admin.GetByRole(AriaRole.Button, new() { Name = "Save navigation link edits" }).ClickAsync();
    var enabledSaveResponse = await admin.RunAndWaitForResponseAsync(
        async () => await admin.GetByRole(AriaRole.Button, new() { Name = "Save Draft" }).ClickAsync(),
        response => response.Request.Method == "PUT" && IsNavigationUrl(response.Url));
    Check(enabledSaveResponse.Status == 200, "Enabled draft save returns 200");

    await admin.GetByRole(AriaRole.Button, new() { Name = "Publish Header and Footer Navigation", Exact = true }).ClickAsync();
    var publishDialog = admin.GetByRole(AriaRole.Dialog, new() { Name = "Publish Header and Footer Navigation?", Exact = true });
    var publishResponse = await admin.RunAndWaitForResponseAsync(
        async () => await publishDialog.GetByRole(AriaRole.Button, new() { Name = "Publish Header and Footer Navigation", Exact = true }).ClickAsync(),
        response => response.Request.Method == "POST" && response.Url.EndsWith("/api/admin/cms/navigation/publish", StringComparison.Ordinal));
    Check(publishResponse.Status == 200, "Navigation publication returns 200");
    var publishedSnapshot = ParseJson(await publishResponse.TextAsync());
    Check(publishedSnapshot["hasPendingChanges"]?.GetValue<bool>() == false, "Publication clears pending changes");
    AssertCertLink(publishedSnapshot, enabled: true, expectedIndex: 1, expectedSlug: "contact", inspectLive: true);
    await admin.GetByText("Navigation published", new() { Exact = true }).WaitForAsync();
    await admin.ScreenshotAsync(new PageScreenshotOptions { Path = "04-admin-published-cert-link.png", FullPage = true });

    var secondAdminTab = await adminContext.NewPageAsync();
    await secondAdminTab.GotoAsync($"{AdminBaseUrl}/storefront/navigation?view=header", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Check(await secondAdminTab.GetByText(CertLabel, new() { Exact = true }).IsVisibleAsync(), "Published state converges in a second authenticated tab");
    await secondAdminTab.CloseAsync();
    await admin.BringToFrontAsync();

    Console.WriteLine("PHASE public-en-ar-mobile-cache-footer-theme");
    await using var publishedPublicContext = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
    });
    var publishedPublic = await publishedPublicContext.NewPageAsync();
    var publishedDocument = await publishedPublic.GotoAsync(StorefrontBaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Check(publishedDocument?.Status == 200, "Published storefront document returns 200");
    var publicCertLink = publishedPublic.GetByRole(AriaRole.Link, new() { Name = CertLabel, Exact = true });
    await publicCertLink.WaitForAsync();
    Check(await publicCertLink.GetAttributeAsync("href") == "/contact", "CMS target resolves to the current published /contact route");
    var publishedPublicNavigation = ParseJson((await BrowserFetchAsync(publishedPublic, "/api/storefront/navigation?lang=en")).Text);
    Check(FindLink(publishedPublicNavigation["header"]?["links"], CertLabel) is not null, "Public navigation API exposes the published link immediately");
    Check(JsonNode.DeepEquals(originalPublicNavigation["footer"], publishedPublicNavigation["footer"]), "Header publication leaves public Footer links unchanged");

    var publishedFooter = ParseJson((await BrowserFetchAsync(publishedPublic, "/api/storefront/footer?lang=en")).Text);
    AssertFooterContentUnchanged(originalFooter, publishedFooter);
    var publishedStoreConfig = ParseJson((await BrowserFetchAsync(publishedPublic, "/api/storefront/store-config")).Text);
    AssertPresentationUnchanged(originalStoreConfig, publishedStoreConfig);

    var contactNavigation = await publishedPublic.RunAndWaitForNavigationAsync(
        async () => await publicCertLink.ClickAsync(),
        new PageRunAndWaitForNavigationOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Check(contactNavigation?.Status == 200 && publishedPublic.Url.EndsWith("/contact", StringComparison.Ordinal), "Published CMS navigation link opens the live Contact page with 200");

    await publishedPublic.GotoAsync(StorefrontBaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await publishedPublic.GetByRole(AriaRole.Button, new() { Name = "Switch to Arabic" }).ClickAsync();
    await publishedPublic.WaitForFunctionAsync("() => document.documentElement.dir === 'rtl'");
    Check(await publishedPublic.GetByRole(AriaRole.Link, new() { Name = CertLabelAr, Exact = true }).IsVisibleAsync(), "Arabic label is rendered in RTL mode");
    var arabicNavigation = ParseJson((await BrowserFetchAsync(publishedPublic, "/api/storefront/navigation?lang=ar")).Text);
    Check(FindLinkByArabicLabel(arabicNavigation["header"]?["links"], CertLabelAr) is not null, "Arabic public navigation API returns the explicit labelAr value");

    await publishedPublic.SetViewportSizeAsync(390, 844);
    await publishedPublic.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await publishedPublic.GetByRole(AriaRole.Button, new() { Name = "Open menu" }).ClickAsync();
    var mobileDialog = publishedPublic.GetByRole(AriaRole.Dialog, new() { Name = "Navigation menu" });
    await mobileDialog.WaitForAsync();
    var mobileBox = await mobileDialog.BoundingBoxAsync();
    Check(mobileBox is not null && mobileBox.X <= 2, "RTL mobile drawer is anchored to the left edge");
    var mobileDialogText = await mobileDialog.InnerTextAsync();
    Check(mobileDialogText.Contains(CertLabelAr, StringComparison.Ordinal), "Arabic disposable link is visible in the mobile drawer");
    await publishedPublic.ScreenshotAsync(new PageScreenshotOptions { Path = "05-public-arabic-mobile-published.png", FullPage = true });

    Console.WriteLine("PHASE ui-delete-save-publish-restore");
    await admin.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await admin.GetByRole(AriaRole.Heading, new() { Name = "Navigation", Exact = true }).WaitForAsync();
    var certRow = admin.GetByText(CertLabel, new() { Exact = true })
        .Locator("xpath=ancestor::div[.//button[@aria-label='Delete navigation link']][1]");
    await certRow.GetByRole(AriaRole.Button, new() { Name = "Delete navigation link" }).ClickAsync();
    var deleteDialog = admin.GetByRole(AriaRole.Dialog, new() { Name = "Delete Navigation Link?", Exact = true });
    await deleteDialog.GetByRole(AriaRole.Button, new() { Name = "Delete Link", Exact = true }).ClickAsync();
    var restoreSaveResponse = await admin.RunAndWaitForResponseAsync(
        async () => await admin.GetByRole(AriaRole.Button, new() { Name = "Save Draft" }).ClickAsync(),
        response => response.Request.Method == "PUT" && IsNavigationUrl(response.Url));
    Check(restoreSaveResponse.Status == 200, "Restoration draft save returns 200");
    await admin.GetByRole(AriaRole.Button, new() { Name = "Publish Header and Footer Navigation", Exact = true }).ClickAsync();
    var restorePublishDialog = admin.GetByRole(AriaRole.Dialog, new() { Name = "Publish Header and Footer Navigation?", Exact = true });
    var restorePublishResponse = await admin.RunAndWaitForResponseAsync(
        async () => await restorePublishDialog.GetByRole(AriaRole.Button, new() { Name = "Publish Header and Footer Navigation", Exact = true }).ClickAsync(),
        response => response.Request.Method == "POST" && response.Url.EndsWith("/api/admin/cms/navigation/publish", StringComparison.Ordinal));
    Check(restorePublishResponse.Status == 200, "Restoration publication returns 200");

    var restoredSnapshot = ParseJson((await BrowserFetchAsync(admin, NavigationPath)).Text);
    AssertRestored(originalSnapshot, restoredSnapshot);
    mayNeedRestore = false;

    await using var restoredPublicContext = await browser.NewContextAsync();
    var restoredPublic = await restoredPublicContext.NewPageAsync();
    await restoredPublic.GotoAsync(StorefrontBaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    var restoredPublicNavigation = ParseJson((await BrowserFetchAsync(restoredPublic, "/api/storefront/navigation?lang=en")).Text);
    Check(FindLink(restoredPublicNavigation["header"]?["links"], CertLabel) is null, "Restored public navigation removes the disposable link immediately");
    Check(JsonNode.DeepEquals(originalPublicNavigation["header"], restoredPublicNavigation["header"]), "Restored public Header exactly matches the original Header");
    Check(JsonNode.DeepEquals(originalPublicNavigation["footer"], restoredPublicNavigation["footer"]), "Restored public Footer exactly matches the original Footer");

    Console.WriteLine("PHASE error-contracts-and-network-recovery");
    await AssertHttpFailureContractsAsync(playwright, admin, restoredSnapshot);

    Check(browserErrors.Count == 0, $"No unexpected browser errors before the deliberate network failure: {string.Join(" || ", browserErrors)}");
    await admin.RouteAsync("**/api/admin/cms/navigation", route => route.AbortAsync("failed"));
    await admin.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Check(await admin.GetByText("Navigation authoring unavailable", new() { Exact = true }).IsVisibleAsync(), "Network failure renders the fail-closed unavailable state");
    Check(browserErrors.Count > 0 && browserErrors.All(error => error.Contains("Failed to load navigation", StringComparison.Ordinal)), "Deliberate network failure emits only the expected load error");
    browserErrors.Clear();
    await admin.UnrouteAsync("**/api/admin/cms/navigation");
    await admin.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Check(await admin.GetByRole(AriaRole.Heading, new() { Name = "Navigation", Exact = true }).IsVisibleAsync(), "Navigation editor recovers after the network failure is removed");
    var recoverySnapshot = ParseJson((await BrowserFetchAsync(admin, NavigationPath)).Text);
    AssertRestored(originalSnapshot, recoverySnapshot);

    await admin.ScreenshotAsync(new PageScreenshotOptions { Path = "06-admin-final-restored.png", FullPage = true });
    Check(browserErrors.Count == 0, $"No unexpected browser errors: {string.Join(" || ", browserErrors)}");
    Console.WriteLine("CERTIFICATION PASS");
}
finally
{
    if (mayNeedRestore && originalSnapshot is not null)
    {
        Console.WriteLine("FAILSAFE restoration started");
        try
        {
            await admin.UnrouteAsync("**/api/admin/cms/navigation");
            await RestoreOriginalAsync(admin, originalSnapshot);
            var finalSnapshot = ParseJson((await BrowserFetchAsync(admin, NavigationPath)).Text);
            AssertRestored(originalSnapshot, finalSnapshot);
            Console.WriteLine("FAILSAFE restoration passed");
        }
        catch (Exception restorationError)
        {
            Console.Error.WriteLine($"FAILSAFE RESTORATION FAILED: {restorationError}");
        }
    }
}

static async Task LoginAsync(IPage page, string username, string password)
{
    await page.GotoAsync($"{AdminBaseUrl}/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByLabel("Username or Email").FillAsync(username);
    await page.GetByLabel("Password").FillAsync(password);
    await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
    await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"LOGIN {page.Url}");
}

static async Task OpenNavigationEditorAsync(IPage page)
{
    await page.GotoAsync($"{AdminBaseUrl}/storefront/navigation?view=header", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByRole(AriaRole.Heading, new() { Name = "Navigation", Exact = true }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Check(!body.Contains("Navigation authoring unavailable", StringComparison.Ordinal), "Navigation editor is available");
    Check(!body.Contains("Failed to load navigation", StringComparison.Ordinal), "Navigation editor loads without an error");
}

static async Task<HttpResult> BrowserFetchAsync(
    IPage page,
    string url,
    string method = "GET",
    JsonNode? body = null,
    Dictionary<string, string>? headers = null)
{
    return await page.EvaluateAsync<HttpResult>(
        """
        async request => {
            const headers = { ...(request.headers || {}) };
            const method = request.method || 'GET';
            if (request.body != null) headers['Content-Type'] = 'application/json';
            if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method.toUpperCase())) {
                const csrf = document.cookie.split(';').map(value => value.trim()).find(value => value.startsWith('csrf_token='));
                if (csrf) headers['X-CSRF-Token'] = decodeURIComponent(csrf.substring('csrf_token='.length));
            }
            try {
                const response = await fetch(request.url, {
                    method,
                    headers,
                    body: request.body,
                    credentials: 'include',
                    cache: 'no-store'
                });
                return { status: response.status, text: await response.text() };
            } catch (error) {
                return { status: 0, text: String(error) };
            }
        }
        """,
        new
        {
            url,
            method,
            body = body?.ToJsonString(),
            headers
        });
}

static JsonNode ParseJson(string text) =>
    JsonNode.Parse(text) ?? throw new InvalidOperationException($"Expected JSON but received: {text}");

static bool IsNavigationUrl(string url) =>
    url.EndsWith("/api/admin/cms/navigation", StringComparison.Ordinal);

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {message}");
    }

    Console.WriteLine($"PASS {message}");
}

static JsonNode? FindLink(JsonNode? links, string label)
{
    return links?.AsArray().FirstOrDefault(link =>
        string.Equals(link?["label"]?.GetValue<string>(), label, StringComparison.Ordinal));
}

static JsonNode? FindLinkByArabicLabel(JsonNode? links, string label)
{
    return links?.AsArray().FirstOrDefault(link =>
        string.Equals(link?["labelAr"]?.GetValue<string>(), label, StringComparison.Ordinal));
}

static void AssertCertLink(
    JsonNode snapshot,
    bool enabled,
    int expectedIndex,
    string expectedSlug,
    bool inspectLive = false)
{
    var scope = inspectLive ? "live" : "draft";
    var links = snapshot[scope]?["header"]?.AsArray()
        ?? throw new InvalidOperationException($"Missing {scope} header");
    Check(links.Count == 3, $"{scope} Header contains the disposable third link");
    var link = links[expectedIndex] ?? throw new InvalidOperationException("Disposable link missing");
    Check(link["label"]?.GetValue<string>() == CertLabel, $"{scope} disposable link has the edited English label");
    Check(link["labelAr"]?.GetValue<string>() == CertLabelAr, $"{scope} disposable link has the explicit Arabic label");
    Check(link["enabled"]?.GetValue<bool>() == enabled, $"{scope} disposable link enabled state is {enabled}");
    Check(link["type"]?.GetValue<string>() == "cms", $"{scope} disposable link remains a CMS target");
    Check(link["slug"]?.GetValue<string>() == expectedSlug, $"{scope} CMS target resolves to {expectedSlug}");
}

static void AssertPresentationUnchanged(JsonNode before, JsonNode after)
{
    foreach (var property in new[] { "storefrontTheme", "themeId", "theme", "behavior", "pages", "productTemplates" })
    {
        Check(JsonNode.DeepEquals(before[property], after[property]), $"StoreConfig {property} is unchanged by Navigation publication");
    }
}

static void AssertFooterContentUnchanged(JsonNode before, JsonNode after)
{
    foreach (var property in new[] { "footerSource", "socialSource", "sections", "socialLinks" })
    {
        Check(JsonNode.DeepEquals(before[property], after[property]), $"Dedicated Footer {property} is unchanged");
    }
}

static void AssertRestored(JsonNode original, JsonNode restored)
{
    Check(JsonNode.DeepEquals(original["draft"], restored["draft"]), "Restored Admin draft exactly matches the original draft");
    Check(JsonNode.DeepEquals(original["live"], restored["live"]), "Restored Admin live snapshot exactly matches the original live snapshot");
    Check(restored["hasPendingChanges"]?.GetValue<bool>() == false, "Restored Admin navigation has no pending changes");
    Check(restored["validationWarnings"]?.AsArray().Count == 0, "Restored Admin navigation has no validation warnings");
    Check(restored["brokenLinks"]?.AsArray().Count == 0, "Restored Admin navigation has no broken links");
}

static JsonObject BuildRequest(JsonNode snapshot, string rowVersion)
{
    return new JsonObject
    {
        ["rowVersion"] = rowVersion,
        ["header"] = BuildLinks(snapshot["draft"]?["header"]),
        ["footer"] = BuildLinks(snapshot["draft"]?["footer"])
    };
}

static JsonArray BuildLinks(JsonNode? source)
{
    var result = new JsonArray();
    if (source is not JsonArray links)
    {
        return result;
    }

    for (var index = 0; index < links.Count; index++)
    {
        var link = links[index];
        if (link is null)
        {
            continue;
        }

        result.Add(new JsonObject
        {
            ["label"] = link["label"]?.DeepClone(),
            ["labelAr"] = link["labelAr"]?.DeepClone(),
            ["type"] = link["type"]?.DeepClone(),
            ["targetId"] = link["targetId"]?.DeepClone(),
            ["collectionKey"] = link["collectionKey"]?.DeepClone(),
            ["path"] = link["path"]?.DeepClone(),
            ["openInNewTab"] = link["openInNewTab"]?.DeepClone() ?? false,
            ["order"] = index,
            ["enabled"] = link["enabled"]?.DeepClone() ?? true
        });
    }

    return result;
}

static async Task RestoreOriginalAsync(IPage admin, JsonNode original)
{
    var latest = ParseJson((await BrowserFetchAsync(admin, NavigationPath)).Text);
    var rowVersion = latest["rowVersion"]?.GetValue<string>()
        ?? throw new InvalidOperationException("Current RowVersion missing during restoration");
    var request = BuildRequest(original, rowVersion);
    var save = await BrowserFetchAsync(
        admin,
        NavigationPath,
        "PUT",
        request,
        new Dictionary<string, string> { ["If-Match"] = $"\"{rowVersion}\"" });
    Check(save.Status == 200, $"Failsafe restoration save returns 200 (actual {save.Status})");
    var saved = ParseJson(save.Text);
    var savedVersion = saved["rowVersion"]?.GetValue<string>()
        ?? throw new InvalidOperationException("Saved RowVersion missing during restoration");
    var publish = await BrowserFetchAsync(
        admin,
        $"{NavigationPath}/publish",
        "POST",
        headers: new Dictionary<string, string> { ["If-Match"] = $"\"{savedVersion}\"" });
    Check(publish.Status == 200, $"Failsafe restoration publish returns 200 (actual {publish.Status})");
}

static async Task AssertHttpFailureContractsAsync(IPlaywright playwright, IPage admin, JsonNode currentSnapshot)
{
    await using var noTenant = await playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
    {
        BaseURL = "http://localhost:5010"
    });
    var badRequest = await noTenant.GetAsync(NavigationPath);
    Check(badRequest.Status == 400, "Untrusted host without tenant context returns 400");

    await using var anonymousTenant = await playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
    {
        BaseURL = "http://localhost:5010",
        ExtraHTTPHeaders = new Dictionary<string, string> { ["Host"] = "ahramstore.localhost" }
    });
    var unauthorized = await anonymousTenant.GetAsync(NavigationPath);
    Check(unauthorized.Status == 401, "Trusted tenant host without authentication returns 401");

    var forbidden = await BrowserFetchAsync(
        admin,
        NavigationPath,
        headers: new Dictionary<string, string> { ["X-Tenant-Id"] = "99999999-9999-4999-8999-999999999999" });
    Check(forbidden.Status == 403, "Forged tenant header returns 403");

    var notFound = await BrowserFetchAsync(admin, "/api/admin/cms/navigation/not-found");
    Check(notFound.Status == 404, "Unknown authenticated Navigation endpoint returns 404");

    var withoutVersion = BuildRequest(currentSnapshot, currentSnapshot["rowVersion"]?.GetValue<string>() ?? "1");
    withoutVersion.Remove("rowVersion");
    var precondition = await BrowserFetchAsync(admin, NavigationPath, "PUT", withoutVersion);
    Check(precondition.Status == 428, "Navigation mutation without RowVersion or If-Match returns 428");

    var stale = BuildRequest(currentSnapshot, "1");
    var conflict = await BrowserFetchAsync(
        admin,
        NavigationPath,
        "PUT",
        stale,
        new Dictionary<string, string> { ["If-Match"] = "\"1\"" });
    Check(conflict.Status == 409, "Navigation mutation with a stale RowVersion returns 409");

    var currentVersion = currentSnapshot["rowVersion"]?.GetValue<string>()
        ?? throw new InvalidOperationException("Current RowVersion missing for admission test");
    var invalidTarget = BuildRequest(currentSnapshot, currentVersion);
    invalidTarget["header"]!.AsArray().Add(new JsonObject
    {
        ["label"] = "Invalid target",
        ["labelAr"] = null,
        ["type"] = "cms",
        ["targetId"] = "99999999-9999-4999-8999-999999999999",
        ["collectionKey"] = null,
        ["path"] = null,
        ["openInNewTab"] = false,
        ["order"] = invalidTarget["header"]!.AsArray().Count,
        ["enabled"] = true
    });
    var rejected = await BrowserFetchAsync(
        admin,
        NavigationPath,
        "PUT",
        invalidTarget,
        new Dictionary<string, string> { ["If-Match"] = $"\"{currentVersion}\"" });
    Check(rejected.Status == 422, "Unknown backend entity target returns 422");
    var rejectionBody = ParseJson(rejected.Text);
    Check(rejectionBody["code"]?.GetValue<string>()?.StartsWith("NAVIGATION_", StringComparison.Ordinal) == true, "422 response uses the Navigation admission error contract");
}

internal sealed class HttpResult
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

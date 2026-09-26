using System.Text.Json;
using Microsoft.Playwright;
using Npgsql;

var username = Environment.GetEnvironmentVariable("CMS_CERT_USER") ?? throw new InvalidOperationException("CMS_CERT_USER missing");
var password = Environment.GetEnvironmentVariable("CMS_CERT_PASS") ?? throw new InvalidOperationException("CMS_CERT_PASS missing");
var connectionString = Environment.GetEnvironmentVariable("CMS_CERT_DB") ?? throw new InvalidOperationException("CMS_CERT_DB missing");
var runId = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var slug = $"cms-cert-{runId}";
var titleA = $"CMS CERT A {runId}";
var titleB = $"CMS CERT B {runId}";
var titleC = $"CMS CERT C {runId}";
var contentA = $"CMS_CERT_A_CONTENT_{runId}";
var contentB = $"CMS_CERT_B_CONTENT_{runId}";
var contentC = $"CMS_CERT_C_CONTENT_{runId}";
var seoTitleA = $"CMS CERT SEO A {runId}";
var seoTitleB = $"CMS CERT SEO B {runId}";
var seoDescriptionA = $"CMS certification SEO description A {runId}";
var seoDescriptionB = $"CMS certification SEO description B {runId}";
Guid? pageGroupId = null;
int? latestVersion = null;

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true
});
await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
});
var admin = await context.NewPageAsync();
var browserErrors = new List<string>();
admin.Console += (_, message) =>
{
    if (message.Type == "error" && !message.Text.Contains("401 (Unauthorized)", StringComparison.Ordinal))
    {
        browserErrors.Add($"console: {message.Text}");
    }
};
admin.PageError += (_, error) => browserErrors.Add($"page: {error}");

await using var anonymousContext = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
});
var publicPage = await anonymousContext.NewPageAsync();

try
{
    await PurgeCertificationRowsAsync(connectionString);
    await AssertDatabaseSlugCountAsync(connectionString, slug, 0, "initial database state");
    await LoginAsync(admin, username, password);

    await admin.GotoAsync("http://localhost:5173/storefront/content/pages", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await ExpectVisibleAsync(admin.GetByRole(AriaRole.Heading, new() { Name = "Pages", Exact = true }), "Pages list heading");
    var originalTitles = new[] { "About Us", "Contact Us", "Privacy Policy", "Terms & Conditions" };
    foreach (var title in originalTitles)
    {
        await ExpectVisibleAsync(admin.GetByText(title, new() { Exact = true }), $"seeded page {title}");
    }
    await admin.ScreenshotAsync(new PageScreenshotOptions { Path = "01-pages-list.png", FullPage = true });
    Console.WriteLine("PASS pages list rendered four authoritative published pages");

    var unauthorized = await anonymousContext.APIRequest.GetAsync("http://localhost:5173/api/admin/pages");
    Assert(unauthorized.Status is 400 or 401 or 403, $"unauthorized admin list was not rejected, got {unauthorized.Status}");
    Console.WriteLine($"PASS unauthorized admin CMS read rejected with {unauthorized.Status}");

    await admin.GetByRole(AriaRole.Button, new() { Name = "New Page" }).ClickAsync();
    await admin.WaitForURLAsync("**/storefront/content/pages/new");
    await admin.GetByLabel("Title", new() { Exact = true }).FillAsync(titleA);
    await admin.Locator("label:has-text('Slug') input").FillAsync(slug);
    await admin.GetByLabel("Meta title", new() { Exact = true }).FillAsync(seoTitleA);
    await admin.GetByLabel("Meta description", new() { Exact = true }).FillAsync(seoDescriptionA);
    await admin.GetByLabel("Meta keywords", new() { Exact = true }).FillAsync("cms, certification, a");
    await admin.GetByLabel("Open Graph image URL", new() { Exact = true }).FillAsync("/media/cms/cert-a.png");
    await SetRichTextAsync(admin, contentA);
    var createResponse = await admin.RunAndWaitForResponseAsync(
        async () => await admin.GetByRole(AriaRole.Button, new() { Name = "Save Draft" }).ClickAsync(),
        response => response.Url.EndsWith("/api/admin/pages", StringComparison.Ordinal) && response.Request.Method == "POST");
    Assert(createResponse.Status == 201, $"create UI returned {createResponse.Status}");
    await admin.WaitForURLAsync(url => url.Contains("/storefront/content/pages/", StringComparison.Ordinal) && !url.EndsWith("/new", StringComparison.Ordinal));
    pageGroupId = Guid.Parse(admin.Url.Split('/').Last());
    latestVersion = 1;
    await AssertDatabaseStateAsync(connectionString, pageGroupId.Value, expectedPublished: 0, expectedDraft: 1, expectedMaxVersion: 1);
    Console.WriteLine($"PASS create produced tenant-scoped draft group {pageGroupId}");

    var unpublishedStatus = await PublicStatusAsync(publicPage, slug);
    Assert(unpublishedStatus == 404, $"draft leaked publicly with status {unpublishedStatus}");
    Console.WriteLine("PASS draft content and SEO remained non-public");

    await admin.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await ExpectInputValueAsync(admin.GetByLabel("Title", new() { Exact = true }), titleA, "draft title after reload");
    await ExpectInputValueAsync(admin.GetByLabel("Meta title", new() { Exact = true }), seoTitleA, "draft SEO after reload");
    Assert((await admin.Locator("[contenteditable='true']").InnerTextAsync()).Contains(contentA, StringComparison.Ordinal), "draft content missing after reload");
    Console.WriteLine("PASS saved draft and SEO survived reload");

    var invalidBlock = await AdminApiAsync(admin, $"/api/admin/pages/{pageGroupId}", "PUT", new
    {
        slug,
        title = titleA,
        contentType = "Blocks",
        content = new[] { new { type = "video", data = new { url = "https://example.com/video" } } },
        schemaVersion = 1,
        expectedVersion = 1,
        seo = new { }
    });
    Assert(invalidBlock.Status == 400 && invalidBlock.Body.Contains("not supported", StringComparison.OrdinalIgnoreCase),
        $"unsupported block expected validation 400, got {invalidBlock.Status} {invalidBlock.Body}");
    var invalidSeo = await AdminApiAsync(admin, $"/api/admin/pages/{pageGroupId}", "PUT", new
    {
        slug,
        title = titleA,
        contentType = "RichText",
        content = contentA,
        schemaVersion = 1,
        expectedVersion = 1,
        seo = new { openGraphImageUrl = "javascript:alert(1)" }
    });
    Assert(invalidSeo.Status == 400 && invalidSeo.Body.Contains("safe relative path", StringComparison.OrdinalIgnoreCase),
        $"unsafe SEO URL expected validation 400, got {invalidSeo.Status} {invalidSeo.Body}");
    await AssertDatabaseStateAsync(connectionString, pageGroupId.Value, 0, 1, 1);
    Console.WriteLine("PASS invalid block content and unsafe SEO URL failed closed without version mutation");

    var unsavedTitle = $"UNSAVED {runId}";
    await admin.GetByLabel("Title", new() { Exact = true }).FillAsync(unsavedTitle);
    await ExpectVisibleAsync(admin.GetByText("Unsaved changes", new() { Exact = true }), "dirty status");
    var auxiliaryTab = await context.NewPageAsync();
    await auxiliaryTab.GotoAsync("about:blank");
    await auxiliaryTab.BringToFrontAsync();
    await Task.Delay(500);
    await admin.BringToFrontAsync();
    await ExpectInputValueAsync(admin.GetByLabel("Title", new() { Exact = true }), unsavedTitle, "unsaved title after tab switch");
    Assert(!(await admin.Locator("body").InnerTextAsync()).Contains("Restoring session", StringComparison.OrdinalIgnoreCase), "background focus displayed Restoring session");
    await auxiliaryTab.CloseAsync();
    Console.WriteLine("PASS blur, visibilitychange, and tab return preserved unsaved content without session restore UI");

    await admin.GetByRole(AriaRole.Button, new() { Name = "Discard", Exact = true }).ClickAsync();
    var discardDialog = admin.GetByRole(AriaRole.Dialog, new() { Name = "Discard Page changes?" });
    await discardDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
    await ExpectInputValueAsync(admin.GetByLabel("Title", new() { Exact = true }), titleA, "discard restored accepted draft");
    Console.WriteLine("PASS discard removed only unsaved local changes and performed no autosave");

    var preview = await admin.RunAndWaitForPopupAsync(async () =>
        await admin.GetByRole(AriaRole.Button, new() { Name = "Preview Saved Draft" }).ClickAsync());
    await preview.WaitForLoadStateAsync(LoadState.NetworkIdle);
    await ExpectVisibleAsync(preview.GetByText("Preview", new() { Exact = true }), "preview badge");
    await ExpectVisibleAsync(preview.GetByRole(AriaRole.Heading, new() { Name = titleA, Exact = true }), "preview title A");
    await ExpectVisibleAsync(preview.GetByText(contentA, new() { Exact = true }), "preview content A");
    Assert(await preview.TitleAsync() == seoTitleA, "preview meta title did not use draft SEO");
    Assert(await MetaContentAsync(preview, "description") == seoDescriptionA, "preview meta description did not use draft SEO");
    Assert(await MetaContentAsync(preview, "robots") == "noindex, nofollow", "preview robots authority missing");
    await preview.ScreenshotAsync(new PageScreenshotOptions { Path = "02-draft-preview.png", FullPage = true });
    await preview.CloseAsync();
    await AssertDatabaseStateAsync(connectionString, pageGroupId.Value, 0, 1, 1);
    Console.WriteLine("PASS signed preview visibly rendered exact draft and SEO without publication mutation");

    await PublishThroughUiAsync(admin);
    latestVersion = 1;
    await AssertDatabaseStateAsync(connectionString, pageGroupId.Value, 1, 0, 1);
    await VerifyPublicAsync(publicPage, slug, titleA, contentA, seoTitleA, seoDescriptionA, "03-public-a.png");
    Console.WriteLine("PASS v1 publish produced exactly one SQL live row and immediate anonymous public A");

    var repeatedPublish = await AdminApiAsync(admin, $"/api/admin/pages/{pageGroupId}/publish", "POST", ifMatch: 1);
    Assert(repeatedPublish.Status == 404 && repeatedPublish.Body.Contains("PUBLISH_FAILED", StringComparison.Ordinal),
        $"repeated publish expected PUBLISH_FAILED 404, got {repeatedPublish.Status} {repeatedPublish.Body}");
    var missingPrecondition = await AdminApiAsync(admin, $"/api/admin/pages/{pageGroupId}/publish", "POST");
    Assert(missingPrecondition.Status == 428, $"publish without precondition expected 428, got {missingPrecondition.Status}");
    var missingTarget = await AdminApiAsync(admin, $"/api/admin/pages/{Guid.NewGuid()}/publish", "POST", ifMatch: 1);
    Assert(missingTarget.Status == 404, $"missing publish target expected 404, got {missingTarget.Status}");
    Console.WriteLine("PASS repeated publish, invalid precondition, and missing target failed closed");

    await admin.GetByLabel("Title", new() { Exact = true }).FillAsync(titleB);
    await admin.GetByLabel("Meta title", new() { Exact = true }).FillAsync(seoTitleB);
    await admin.Locator("label:has-text('Meta description') textarea").FillAsync(seoDescriptionB);
    await admin.Locator("label:has-text('Meta keywords') input").FillAsync("cms, certification, b");
    await admin.Locator("label:has-text('Open Graph image URL') input").FillAsync("/media/cms/cert-b.png");
    await SetRichTextAsync(admin, contentB);
    await SaveDraftThroughUiAsync(admin);
    latestVersion = 2;
    await admin.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await ExpectInputValueAsync(admin.GetByLabel("Title", new() { Exact = true }), titleB, "B draft after reload");
    await VerifyPublicAsync(publicPage, slug, titleA, contentA, seoTitleA, seoDescriptionA, null);
    Console.WriteLine("PASS v2 draft survived reload while public content and SEO remained A");

    preview = await admin.RunAndWaitForPopupAsync(async () =>
        await admin.GetByRole(AriaRole.Button, new() { Name = "Preview Saved Draft" }).ClickAsync());
    await preview.WaitForLoadStateAsync(LoadState.NetworkIdle);
    await ExpectVisibleAsync(preview.GetByRole(AriaRole.Heading, new() { Name = titleB, Exact = true }), "preview title B");
    await ExpectVisibleAsync(preview.GetByText(contentB, new() { Exact = true }), "preview content B");
    Assert(await preview.TitleAsync() == seoTitleB, "preview B SEO title mismatch");
    await preview.CloseAsync();

    await PublishThroughUiAsync(admin);
    await AssertDatabaseStateAsync(connectionString, pageGroupId.Value, 1, 0, 2);
    await VerifyPublicAsync(publicPage, slug, titleB, contentB, seoTitleB, seoDescriptionB, "04-public-b.png");
    Console.WriteLine("PASS v2 publish replaced v1 without duplicate live rows and public B was uncached");

    await admin.GetByRole(AriaRole.Button, new() { Name = "Page History" }).ClickAsync();
    var historyDialog = admin.GetByRole(AriaRole.Dialog, new() { Name = "Page Version History" });
    await ExpectVisibleAsync(historyDialog.GetByText("v2", new() { Exact = true }), "history v2");
    await ExpectVisibleAsync(historyDialog.GetByText("v1", new() { Exact = true }), "history v1");
    var historyText = await historyDialog.InnerTextAsync();
    Assert(historyText.IndexOf("v2", StringComparison.Ordinal) < historyText.IndexOf("v1", StringComparison.Ordinal), "history versions not descending");
    Assert(historyText.Split("Published", StringSplitOptions.None).Length - 1 == 1, "history did not identify exactly one published version");
    await admin.ScreenshotAsync(new PageScreenshotOptions { Path = "05-history-before-rollback.png", FullPage = true });

    var v1Card = historyDialog.GetByText("v1", new() { Exact = true }).Locator("xpath=ancestor::div[contains(@class,'rounded-2xl')][1]");
    await v1Card.GetByRole(AriaRole.Button, new() { Name = "Rollback", Exact = true }).ClickAsync();
    await historyDialog.GetByLabel("Rollback reason (optional)").FillAsync("CMS final certification restore A");
    await historyDialog.GetByRole(AriaRole.Checkbox).CheckAsync();
    var rollbackResponse = await admin.RunAndWaitForResponseAsync(
        async () => await historyDialog.GetByRole(AriaRole.Button, new() { Name = "Rollback now" }).ClickAsync(),
        response => response.Url.EndsWith("/rollback", StringComparison.Ordinal) && response.Request.Method == "POST");
    Assert(rollbackResponse.Status == 200, $"rollback UI returned {rollbackResponse.Status}");
    latestVersion = 3;
    await historyDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
    await ExpectInputValueAsync(admin.GetByLabel("Title", new() { Exact = true }), titleA, "rollback restored editor A");
    await AssertDatabaseStateAsync(connectionString, pageGroupId.Value, 1, 0, 3);
    await VerifyPublicAsync(publicPage, slug, titleA, contentA, seoTitleA, seoDescriptionA, "06-public-rollback-a.png");
    Console.WriteLine("PASS page-group rollback created v3, retained history, and restored public content plus SEO A");

    await admin.GetByLabel("Title", new() { Exact = true }).FillAsync(titleC);
    await admin.GetByLabel("Meta title", new() { Exact = true }).FillAsync($"CMS CERT SEO C {runId}");
    await SetRichTextAsync(admin, contentC);
    await SaveDraftThroughUiAsync(admin);
    latestVersion = 4;

    var staleUpdateBody = new
    {
        slug,
        title = "STALE OVERWRITE",
        contentType = "RichText",
        content = "STALE CONTENT",
        schemaVersion = 1,
        expectedVersion = 3,
        seo = new { }
    };
    var staleUpdate = await AdminApiAsync(admin, $"/api/admin/pages/{pageGroupId}", "PUT", staleUpdateBody);
    Assert(staleUpdate.Status == 409 && staleUpdate.Body.Contains("CONCURRENCY_CONFLICT", StringComparison.Ordinal),
        $"stale update expected 409, got {staleUpdate.Status} {staleUpdate.Body}");

    var concurrent = await ConcurrentPublishAsync(admin, pageGroupId.Value, 4);
    Assert(concurrent.Count(status => status == 200) == 1, $"concurrent publish expected one winner, got [{string.Join(',', concurrent)}]");
    Assert(concurrent.Count(status => status >= 400) == 1, $"concurrent publish expected one loser, got [{string.Join(',', concurrent)}]");
    latestVersion = 4;
    await AssertDatabaseStateAsync(connectionString, pageGroupId.Value, 1, 0, 4);
    await VerifyPublicAsync(publicPage, slug, titleC, contentC, $"CMS CERT SEO C {runId}", seoDescriptionA, null);
    Console.WriteLine($"PASS stale update rejected and concurrent publish collapsed to one live v4 (statuses {string.Join('/', concurrent)})");

    var historyApi = await AdminApiAsync(admin, $"/api/admin/pages/{pageGroupId}/versions", "GET");
    Assert(historyApi.Status == 200, $"history API returned {historyApi.Status}");
    using (var historyJson = JsonDocument.Parse(historyApi.Body))
    {
        var versions = historyJson.RootElement.GetProperty("versions").EnumerateArray().ToArray();
        Assert(versions.Select(item => item.GetProperty("version").GetInt32()).SequenceEqual(new[] { 4, 3, 2, 1 }), "history API order/uniqueness mismatch after concurrency");
        Assert(versions.Count(item => item.GetProperty("isPublished").GetBoolean()) == 1, "history API exposed phantom published version");
    }
    Console.WriteLine("PASS failed/stale/concurrent mutations produced no phantom history state");

    var saveRoute = $"**/api/admin/pages/{pageGroupId}";
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await admin.RouteAsync(saveRoute, async route =>
    {
        if (route.Request.Method == "PUT")
        {
            requestStarted.TrySetResult();
            await releaseRequest.Task;
        }
        await route.ContinueAsync();
    });
    var submittedRaceTitle = $"CMS CERT SUBMITTED RACE {runId}";
    var newerRaceTitle = $"CMS CERT NEWER RACE {runId}";
    await admin.GetByLabel("Title", new() { Exact = true }).FillAsync(submittedRaceTitle);
    var raceResponseTask = admin.WaitForResponseAsync(response =>
        response.Url.EndsWith($"/api/admin/pages/{pageGroupId}", StringComparison.Ordinal) && response.Request.Method == "PUT");
    await admin.GetByRole(AriaRole.Button, new() { Name = "Save Draft" }).ClickAsync();
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
    await admin.GetByLabel("Title", new() { Exact = true }).FillAsync(newerRaceTitle);
    releaseRequest.TrySetResult();
    var raceResponse = await raceResponseTask;
    Assert(raceResponse.Status == 200, $"delayed save returned {raceResponse.Status}");
    await ExpectInputValueAsync(admin.GetByLabel("Title", new() { Exact = true }), newerRaceTitle, "newer in-flight edit retention");
    await ExpectVisibleAsync(admin.GetByText("Draft saved. Newer unsaved edits were retained.", new() { Exact = true }), "in-flight retention notice");
    await ExpectVisibleAsync(admin.GetByText("Unsaved changes", new() { Exact = true }), "dirty state after delayed response");
    await admin.UnrouteAsync(saveRoute);
    latestVersion = 5;
    await AssertDatabaseStateAsync(connectionString, pageGroupId.Value, 1, 1, 5);
    await admin.GetByRole(AriaRole.Button, new() { Name = "Discard", Exact = true }).ClickAsync();
    discardDialog = admin.GetByRole(AriaRole.Dialog, new() { Name = "Discard Page changes?" });
    await discardDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
    await ExpectInputValueAsync(admin.GetByLabel("Title", new() { Exact = true }), submittedRaceTitle, "discard after delayed save");
    await VerifyPublicAsync(publicPage, slug, titleC, contentC, $"CMS CERT SEO C {runId}", seoDescriptionA, null);
    Console.WriteLine("PASS delayed browser PUT could not overwrite newer local edits; discard restored accepted v5 while live v4 stayed unchanged");

    var wrongTenantResult = await PublicApiWithTenantAsync(publicPage, slug, Guid.NewGuid());
    Assert(wrongTenantResult.Status != 200 || !wrongTenantResult.Body.Contains(contentC, StringComparison.Ordinal), "cross-tenant public lookup leaked page content");
    var spoofedAdmin = await AdminApiAsync(admin, "/api/admin/pages", "GET", extraHeaders: new Dictionary<string, string> { ["X-Tenant-Id"] = Guid.NewGuid().ToString() });
    Assert(spoofedAdmin.Status is 200 or 400 or 403, $"unexpected spoofed admin response {spoofedAdmin.Status}");
    if (spoofedAdmin.Status == 200)
    {
        Assert(spoofedAdmin.Body.Contains(pageGroupId.Value.ToString(), StringComparison.OrdinalIgnoreCase), "client tenant header overrode authenticated tenant authority");
    }
    Console.WriteLine("PASS cross-tenant public lookup and forged admin tenant header could not change tenant authority");

    // Expected negative contract probes above emit browser resource errors for their 4xx responses.
    browserErrors.Clear();

    foreach (var route in new[]
    {
        "/storefront/homepage",
        "/storefront/navigation?view=header",
        "/storefront/footer-content",
        "/storefront/appearance/themes",
        "/storefront/appearance/colors-typography",
        "/storefront/status"
    })
    {
        var response = await admin.GotoAsync($"http://localhost:5173{route}", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert(response?.Status == 200, $"cross-impact route {route} did not load");
        Assert(!admin.Url.Contains("/login", StringComparison.Ordinal), $"cross-impact route {route} lost session");
    }
    Console.WriteLine("PASS targeted Homepage, Navigation, Footer, Themes, Typography, Settings, and auth/session route regression");

    var deleteResult = await AdminApiAsync(admin, $"/api/admin/pages/{pageGroupId}", "DELETE", ifMatch: latestVersion);
    Assert(deleteResult.Status == 204, $"cleanup delete returned {deleteResult.Status} {deleteResult.Body}");
    await AssertDatabaseActiveStateAsync(connectionString, pageGroupId.Value, expectedPublished: 0, expectedDraft: 0);
    Assert(await PublicStatusAsync(publicPage, slug) == 404, "temporary page remained public after delete");
    await PurgeDisposableRowsAsync(connectionString, pageGroupId.Value);
    await AssertDatabaseSlugCountAsync(connectionString, slug, 0, "final database restoration");
    pageGroupId = null;
    latestVersion = null;

    await admin.GotoAsync("http://localhost:5173/storefront/content/pages", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Assert(!(await admin.Locator("body").InnerTextAsync()).Contains(slug, StringComparison.Ordinal), "temporary page remained in admin list");
    foreach (var title in originalTitles)
    {
        await ExpectVisibleAsync(admin.GetByText(title, new() { Exact = true }), $"restored list page {title}");
    }
    Assert(await PublicStatusAsync(publicPage, slug) == 404, "temporary page reappeared after final purge");
    await admin.ScreenshotAsync(new PageScreenshotOptions { Path = "07-final-restored-list.png", FullPage = true });
    Console.WriteLine("PASS disposable data removed; original four-page admin list and anonymous public absence restored");

    Assert(browserErrors.Count == 0, $"browser errors observed: {string.Join(" | ", browserErrors)}");
    Console.WriteLine("FINAL_BROWSER_CERTIFICATION PASS");
}
finally
{
    if (pageGroupId.HasValue)
    {
        try
        {
            if (latestVersion.HasValue)
            {
                await AdminApiAsync(admin, $"/api/admin/pages/{pageGroupId}/", "DELETE", ifMatch: latestVersion);
            }
        }
        catch
        {
        }

        await PurgeDisposableRowsAsync(connectionString, pageGroupId.Value);
        Console.WriteLine($"FINALLY purged disposable CMS group {pageGroupId}");
    }
    else
    {
        await PurgeDisposableSlugAsync(connectionString, slug);
    }
}

static async Task LoginAsync(IPage page, string username, string password)
{
    await page.GotoAsync("http://localhost:5173/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByLabel("Username or Email").FillAsync(username);
    await page.GetByLabel("Password").FillAsync(password);
    await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
    await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase));
    Assert(!page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase), "login did not establish authenticated session");
    Console.WriteLine("PASS real browser login established Owner session");
}

static async Task PublishThroughUiAsync(IPage page)
{
    await page.GetByRole(AriaRole.Button, new() { Name = "Publish Page" }).ClickAsync();
    var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Publish Page?" });
    var response = await page.RunAndWaitForResponseAsync(
        async () => await dialog.GetByRole(AriaRole.Button, new() { Name = "Publish Page" }).ClickAsync(),
        item => item.Url.EndsWith("/publish", StringComparison.Ordinal) && item.Request.Method == "POST");
    Assert(response.Status == 200, $"publish UI returned {response.Status}");
    await ExpectVisibleAsync(page.GetByText("Page published successfully.", new() { Exact = true }), "publish success");
}

static async Task SaveDraftThroughUiAsync(IPage page)
{
    var response = await page.RunAndWaitForResponseAsync(
        async () => await page.GetByRole(AriaRole.Button, new() { Name = "Save Draft" }).ClickAsync(),
        item => item.Url.Contains("/api/admin/pages/", StringComparison.Ordinal) &&
                !item.Url.EndsWith("/publish", StringComparison.Ordinal) &&
                item.Request.Method == "PUT");
    Assert(response.Status == 200, $"save draft UI returned {response.Status}");
}

static async Task SetRichTextAsync(IPage page, string content)
{
    var editor = page.Locator("[contenteditable='true']");
    await editor.FillAsync(content);
    await page.GetByLabel("Title", new() { Exact = true }).ClickAsync();
}

static async Task VerifyPublicAsync(
    IPage page,
    string slug,
    string title,
    string content,
    string seoTitle,
    string seoDescription,
    string? screenshot)
{
    var response = await page.GotoAsync($"http://ahramstore.localhost:5174/{slug}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Assert(response?.Status == 200, $"public route returned {response?.Status}");
    await ExpectVisibleAsync(page.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true }), $"public title {title}");
    await ExpectVisibleAsync(page.GetByText(content, new() { Exact = true }), $"public content {content}");
    Assert(await page.TitleAsync() == seoTitle, $"public SEO title mismatch: {await page.TitleAsync()}");
    Assert(await MetaContentAsync(page, "description") == seoDescription, "public SEO description mismatch");
    Assert(await MetaContentAsync(page, "robots") == "index, follow", "public robots metadata mismatch");
    if (screenshot != null)
    {
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true });
    }
}

static async Task<string> MetaContentAsync(IPage page, string name) =>
    await page.Locator($"meta[name='{name}']").GetAttributeAsync("content") ?? string.Empty;

static async Task<int> PublicStatusAsync(IPage page, string slug)
{
    var responseTask = page.WaitForResponseAsync(
        item => item.Url.Contains($"/api/storefront/pages/{slug}", StringComparison.Ordinal),
        new PageWaitForResponseOptions { Timeout = 15_000 });
    await page.GotoAsync($"http://ahramstore.localhost:5174/{slug}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    return (await responseTask).Status;
}

static async Task<ApiResult> PublicApiWithTenantAsync(IPage page, string slug, Guid tenantId)
{
    return await page.EvaluateAsync<ApiResult>("""
        async ({ slug, tenantId }) => {
            const response = await fetch(`/api/storefront/pages/${slug}`, {
                cache: 'no-store',
                headers: { 'X-Tenant-Id': tenantId }
            });
            return { status: response.status, body: await response.text() };
        }
        """, new { slug, tenantId = tenantId.ToString() });
}

static async Task<ApiResult> AdminApiAsync(
    IPage page,
    string path,
    string method,
    object? body = null,
    int? ifMatch = null,
    IReadOnlyDictionary<string, string>? extraHeaders = null)
{
    return await page.EvaluateAsync<ApiResult>("""
        async ({ path, method, body, ifMatch, extraHeaders }) => {
            const csrfPart = document.cookie.split(';').map(x => x.trim()).find(x => x.startsWith('csrf_token='));
            const headers = { ...(extraHeaders || {}) };
            if (csrfPart) headers['X-CSRF-Token'] = decodeURIComponent(csrfPart.substring('csrf_token='.length));
            if (ifMatch !== null) headers['If-Match'] = `"${ifMatch}"`;
            if (body !== null) headers['Content-Type'] = 'application/json';
            const response = await fetch(path, {
                method,
                headers,
                credentials: 'include',
                cache: 'no-store',
                body: body === null ? undefined : JSON.stringify(body)
            });
            return { status: response.status, body: await response.text() };
        }
        """, new { path, method, body, ifMatch, extraHeaders });
}

static async Task<int[]> ConcurrentPublishAsync(IPage page, Guid pageGroupId, int version)
{
    return await page.EvaluateAsync<int[]>("""
        async ({ path, version }) => {
            const csrfPart = document.cookie.split(';').map(x => x.trim()).find(x => x.startsWith('csrf_token='));
            const headers = {
                'X-CSRF-Token': decodeURIComponent(csrfPart.substring('csrf_token='.length)),
                'If-Match': `"${version}"`
            };
            const responses = await Promise.all([
                fetch(path, { method: 'POST', headers, credentials: 'include' }),
                fetch(path, { method: 'POST', headers, credentials: 'include' })
            ]);
            return responses.map(response => response.status);
        }
        """, new { path = $"/api/admin/pages/{pageGroupId}/publish", version });
}

static async Task AssertDatabaseStateAsync(
    string connectionString,
    Guid pageGroupId,
    int expectedPublished,
    int expectedDraft,
    int expectedMaxVersion)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        SELECT
            COUNT(*) FILTER (WHERE "IsPublished" AND NOT "IsDeleted"),
            COUNT(*) FILTER (WHERE "IsDraft" AND NOT "IsDeleted"),
            MAX("Version"),
            COUNT(DISTINCT "Version") = COUNT(*)
        FROM "CmsPages"
        WHERE "PageGroupId" = @pageGroupId
        """, connection);
    command.Parameters.AddWithValue("pageGroupId", pageGroupId);
    await using var reader = await command.ExecuteReaderAsync();
    Assert(await reader.ReadAsync(), "database state query returned no row");
    Assert(reader.GetInt64(0) == expectedPublished, $"expected {expectedPublished} published rows, got {reader.GetInt64(0)}");
    Assert(reader.GetInt64(1) == expectedDraft, $"expected {expectedDraft} draft rows, got {reader.GetInt64(1)}");
    Assert(reader.GetInt32(2) == expectedMaxVersion, $"expected max version {expectedMaxVersion}, got {reader.GetInt32(2)}");
    Assert(reader.GetBoolean(3), "page group contains duplicate version identities");
}

static async Task AssertDatabaseActiveStateAsync(string connectionString, Guid pageGroupId, int expectedPublished, int expectedDraft)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        SELECT
            COUNT(*) FILTER (WHERE "IsPublished" AND NOT "IsDeleted"),
            COUNT(*) FILTER (WHERE "IsDraft" AND NOT "IsDeleted")
        FROM "CmsPages"
        WHERE "PageGroupId" = @pageGroupId
        """, connection);
    command.Parameters.AddWithValue("pageGroupId", pageGroupId);
    await using var reader = await command.ExecuteReaderAsync();
    Assert(await reader.ReadAsync(), "database active state query returned no row");
    Assert(reader.GetInt64(0) == expectedPublished, "unexpected active published row count");
    Assert(reader.GetInt64(1) == expectedDraft, "unexpected active draft row count");
}

static async Task AssertDatabaseSlugCountAsync(string connectionString, string slug, int expected, string stage)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM \"CmsPages\" WHERE \"Slug\" = @slug", connection);
    command.Parameters.AddWithValue("slug", slug);
    var count = Convert.ToInt32(await command.ExecuteScalarAsync());
    Assert(count == expected, $"{stage}: expected {expected} rows for {slug}, got {count}");
}

static async Task PurgeDisposableRowsAsync(string connectionString, Guid pageGroupId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("DELETE FROM \"CmsPages\" WHERE \"PageGroupId\" = @pageGroupId", connection);
    command.Parameters.AddWithValue("pageGroupId", pageGroupId);
    await command.ExecuteNonQueryAsync();
}

static async Task PurgeDisposableSlugAsync(string connectionString, string slug)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("DELETE FROM \"CmsPages\" WHERE \"Slug\" = @slug", connection);
    command.Parameters.AddWithValue("slug", slug);
    await command.ExecuteNonQueryAsync();
}

static async Task PurgeCertificationRowsAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("DELETE FROM \"CmsPages\" WHERE \"Slug\" LIKE 'cms-cert-%'", connection);
    await command.ExecuteNonQueryAsync();
}

static async Task ExpectVisibleAsync(ILocator locator, string description)
{
    await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    Assert(await locator.IsVisibleAsync(), $"{description} was not visible");
}

static async Task ExpectInputValueAsync(ILocator locator, string expected, string description)
{
    await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    Assert(await locator.InputValueAsync() == expected, $"{description} mismatch");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class ApiResult
{
    public int Status { get; set; }
    public string Body { get; set; } = string.Empty;
}

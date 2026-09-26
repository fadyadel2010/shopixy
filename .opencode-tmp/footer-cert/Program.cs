using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Npgsql;

const string TenantIdText = "c9c5c1ae-949e-498e-bc63-07242daeb49d";
const string IsolationTenantHost = "shopixy.localhost:5174";
const string AdminUrl = "http://localhost:5173";
const string StorefrontUrl = "http://ahramstore.localhost:5174";
const string StorefrontTransportUrl = "http://localhost:5174";
const string AdminUsername = "ahramco.software@gmail.com";
const string UiDraftTitle = "Footer Certification UI Draft";
const string ConcurrentWinnerTitle = "Footer Certification Concurrent Winner";
const string CertifiedCompanyTitle = "Certified Company Links";
const string CertifiedSupportTitle = "Certified Support Links";
const string CertifiedCompanyTitleAr = "روابط الشركة المعتمدة";
const string CertifiedSupportTitleAr = "روابط الدعم المعتمدة";
const string CertifiedSocialPlatform = "Certification Social";
const string CertifiedLongLabel = "A deliberately long certification footer label that must wrap safely without clipping or creating horizontal overflow on narrow storefront screens";

var mode = args.FirstOrDefault() ?? "baseline";
var harnessDirectory = Directory.GetCurrentDirectory();
var repositoryRoot = Path.GetFullPath(Path.Combine(harnessDirectory, "..", ".."));
var settingsPath = Path.Combine(repositoryRoot, "PointOfSale.API", "appsettings.Development.local.json");
var baselinePath = Path.Combine(harnessDirectory, "baseline.json");
var evidenceDirectory = Path.Combine(harnessDirectory, "evidence");
var resultPath = Path.Combine(harnessDirectory, "certification-result.json");

using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
var connectionString = settings.RootElement
    .GetProperty("ConnectionStrings")
    .GetProperty("DefaultConnection")
    .GetString() ?? throw new InvalidOperationException("DefaultConnection is missing.");
var tenantId = Guid.Parse(TenantIdText);

if (string.Equals(mode, "baseline", StringComparison.OrdinalIgnoreCase))
{
    var snapshot = await CaptureSnapshotAsync(connectionString, tenantId, includePublicState: true);
    await File.WriteAllTextAsync(baselinePath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"BASELINE_CAPTURED sections={snapshot["sections"]!.AsArray().Count} links={snapshot["links"]!.AsArray().Count} social={snapshot["socialLinks"]!.AsArray().Count}");
    return;
}

if (!string.Equals(mode, "certify", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Unknown mode '{mode}'. Use baseline or certify.");
}

var password = Environment.GetEnvironmentVariable("FOOTER_CERT_PASSWORD");
if (string.IsNullOrWhiteSpace(password))
{
    throw new InvalidOperationException("FOOTER_CERT_PASSWORD must be supplied in process memory for certify mode.");
}

var baseline = JsonNode.Parse(await File.ReadAllTextAsync(baselinePath))?.AsObject()
    ?? throw new InvalidOperationException("baseline.json is missing or invalid.");
Directory.CreateDirectory(evidenceDirectory);

var result = new JsonObject
{
    ["startedAtUtc"] = DateTime.UtcNow,
    ["status"] = "RUNNING",
    ["tenantId"] = TenantIdText,
    ["checks"] = new JsonArray(),
    ["themes"] = new JsonArray(),
    ["screenshots"] = new JsonArray(),
};
var checks = result["checks"]!.AsArray();
var screenshots = result["screenshots"]!.AsArray();
var mutationsStarted = false;
var restorationSucceeded = false;
IBrowser? browser = null;
IBrowserContext? adminContext = null;
IPlaywright? playwright = null;

try
{
    var current = await CaptureSnapshotAsync(connectionString, tenantId, includePublicState: true);
    RequireSnapshotBaseline(current, baseline);
    AddCheck(checks, "Pre-mutation SQL and public state match the immutable baseline");

    var otherTenantFingerprintBefore = await ReadOtherTenantFooterFingerprintAsync(connectionString, tenantId);
    var unrelatedBefore = current["isolationFingerprint"]!.DeepClone();

    playwright = await Playwright.CreateAsync();
    browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = "msedge",
        Headless = true,
    });
    adminContext = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        IgnoreHTTPSErrors = true,
    });
    var adminPage = await adminContext.NewPageAsync();
    var adminPageErrors = new List<string>();
    adminPage.PageError += (_, error) => adminPageErrors.Add(error);

    await LoginAsync(adminPage, AdminUsername, password);
    password = null;
    AddCheck(checks, "Real POS Admin Owner login succeeded without persisted credentials");

    await adminPage.GotoAsync($"{AdminUrl}/storefront/footer-content", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await adminPage.GetByRole(AriaRole.Heading, new() { Name = "Footer Content" }).WaitForAsync();
    await adminPage.GetByText("Live", new() { Exact = true }).WaitForAsync();
    await SaveScreenshotAsync(adminPage, Path.Combine(evidenceDirectory, "01-admin-baseline.png"), screenshots);

    await RequireAdminAccessibilityAsync(adminPage);
    AddCheck(checks, "Footer editor exposes labelled localized, target, activation, ordering, and deletion controls");

    var loadedConfiguration = await BrowserJsonAsync(adminPage, "/api/admin/footer");
    var staleRevision = RequiredString(loadedConfiguration, "revision");
    await adminPage.GetByRole(AriaRole.Textbox, new() { Name = "Section title EN" }).First.FillAsync(UiDraftTitle);
    await adminPage.GetByText("Unsaved changes", new() { Exact = true }).WaitForAsync();

    var concurrentRequest = BuildRequestFromConfiguration(loadedConfiguration, staleRevision, ConcurrentWinnerTitle);
    var concurrentWinner = await BrowserRequestAsync(adminPage, "PUT", "/api/admin/footer", concurrentRequest);
    Require(concurrentWinner.Status == 200, $"Concurrent winner returned {concurrentWinner.Status}: {concurrentWinner.Body}");
    mutationsStarted = true;

    await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save live Footer", Exact = true }).First.ClickAsync();
    var saveDialog = adminPage.GetByRole(AriaRole.Dialog, new() { Name = "Save Footer live?" });
    await saveDialog.GetByRole(AriaRole.Button, new() { Name = "Save live Footer", Exact = true }).ClickAsync();
    await adminPage.GetByText("Footer configuration was updated by another author", new() { Exact = false }).WaitForAsync();
    Require(await adminPage.GetByRole(AriaRole.Textbox, new() { Name = "Section title EN" }).First.InputValueAsync() == UiDraftTitle,
        "The Admin draft was lost after the concurrency conflict.");
    await SaveScreenshotAsync(adminPage, Path.Combine(evidenceDirectory, "02-admin-concurrency-conflict.png"), screenshots);

    await adminPage.GetByRole(AriaRole.Button, new() { Name = "Retry My Draft" }).ClickAsync();
    await adminPage.GetByText("Footer saved live", new() { Exact = true }).WaitForAsync();
    var afterRetry = await BrowserJsonAsync(adminPage, "/api/admin/footer");
    Require(RequiredString(afterRetry["sections"]![0]!.AsObject(), "title") == UiDraftTitle,
        "Retry My Draft did not preserve and save the retained browser candidate.");
    AddCheck(checks, "Stale UI save returned conflict, retained the draft, fetched latest revision, and retried successfully");

    var certificationRequest = BuildCertificationRequest(RequiredString(afterRetry, "revision"));
    var certificationSave = await BrowserRequestAsync(adminPage, "PUT", "/api/admin/footer", certificationRequest);
    Require(certificationSave.Status == 200, $"Certification Footer save returned {certificationSave.Status}: {certificationSave.Body}");
    var acceptedCertification = JsonNode.Parse(certificationSave.Body)?.AsObject()
        ?? throw new InvalidOperationException("Certification save returned invalid JSON.");
    Require(RequiredString(acceptedCertification["sections"]![0]!.AsObject(), "title") == CertifiedCompanyTitle,
        "The accepted certification response did not contain the submitted first section.");

    await adminPage.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await adminPage.GetByText("Live", new() { Exact = true }).WaitForAsync();
    Require(await adminPage.GetByRole(AriaRole.Textbox, new() { Name = "Section title EN" }).First.InputValueAsync() == CertifiedCompanyTitle,
        "The accepted Footer did not survive an Admin reload.");
    await adminPage.GetByRole(AriaRole.Main).GetByRole(AriaRole.Button, new() { Name = "AR", Exact = true }).ClickAsync();
    Require(await adminPage.GetByRole(AriaRole.Textbox, new() { Name = "Section title AR" }).First.InputValueAsync() == CertifiedCompanyTitleAr,
        "The Arabic Footer title was not admitted back into the editor.");
    await SaveScreenshotAsync(adminPage, Path.Combine(evidenceDirectory, "03-admin-certified-ar.png"), screenshots);
    AddCheck(checks, "Accepted bilingual multi-section Footer persists through Admin reload");

    var forgedTenant = await BrowserRequestAsync(
        adminPage,
        "GET",
        "/api/admin/footer",
        null,
        new Dictionary<string, string> { ["X-Tenant-Id"] = "83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5" });
    Require(forgedTenant.Status == 403 && forgedTenant.Body.Contains("TENANT_FORBIDDEN", StringComparison.Ordinal),
        $"A forged tenant header did not fail closed: {forgedTenant.Status} {forgedTenant.Body}");
    AddCheck(checks, "Authenticated tenant authority rejects a forged X-Tenant-Id header with 403 TENANT_FORBIDDEN");

    var publicEn = await ReadPublicJsonAsync("/api/storefront/footer?lang=en", "ahramstore.localhost:5174");
    var publicAr = await ReadPublicJsonAsync("/api/storefront/footer?lang=ar", "ahramstore.localhost:5174");
    RequirePublicCertification(publicEn, publicAr);
    var isolationFooter = await ReadPublicJsonAsync("/api/storefront/footer?lang=en", IsolationTenantHost);
    Require(!isolationFooter.ToJsonString().Contains(CertifiedCompanyTitle, StringComparison.Ordinal),
        "Certification Footer content leaked to the isolation tenant host.");
    AddCheck(checks, "Anonymous English/Arabic Footer reads are immediate, localized, filtered, and tenant-isolated");

    var duringMutation = await CaptureSnapshotAsync(connectionString, tenantId, includePublicState: true);
    RequireUnrelatedFingerprintsUnchanged(unrelatedBefore!.AsObject(), duringMutation["isolationFingerprint"]!.AsObject());
    Require(JsonNode.DeepEquals(otherTenantFingerprintBefore, await ReadOtherTenantFooterFingerprintAsync(connectionString, tenantId)),
        "Another tenant's Footer rows changed during certification.");
    Require(JsonNode.DeepEquals(baseline["publicNavigation"], duringMutation["publicNavigation"]),
        "Navigation changed during Footer certification.");
    Require(JsonNode.DeepEquals(baseline["publicStoreConfig"], duringMutation["publicStoreConfig"]),
        "StoreConfig changed during Footer certification.");
    AddCheck(checks, "Navigation, StoreConfig, Homepage, CMS Pages, Products, and other-tenant Footer state remained unchanged");

    await CertifyStorefrontAsync(browser, StorefrontUrl, evidenceDirectory, screenshots);
    AddCheck(checks, "Desktop, mobile, and RTL Storefront Footer rendering passed without horizontal Footer overflow");

    var officialThemes = ReadOfficialThemes(Path.Combine(repositoryRoot, "theme-authority", "shopixy-theme-authority.json"));
    foreach (var themeId in officialThemes)
    {
        var previewResponse = await BrowserRequestAsync(
            adminPage,
            "POST",
            $"/api/admin/store-config/theme/preview-link/{Uri.EscapeDataString(themeId)}");
        Require(previewResponse.Status == 200, $"Theme preview link for {themeId} returned {previewResponse.Status}: {previewResponse.Body}");
        var preview = JsonNode.Parse(previewResponse.Body)?.AsObject()
            ?? throw new InvalidOperationException($"Theme preview link for {themeId} returned invalid JSON.");
        var previewUrl = RequiredString(preview, "previewUrl");
        await CertifyThemePreviewAsync(browser, themeId, previewUrl, evidenceDirectory, screenshots, result["themes"]!.AsArray());
    }
    AddCheck(checks, $"All {officialThemes.Count} official runtime themes rendered both authored Footer sections through signed read-only previews");

    Require(adminPageErrors.Count == 0, $"Admin page errors occurred: {string.Join(" | ", adminPageErrors)}");
    result["status"] = "PASS_PENDING_RESTORATION";
}
catch (Exception exception)
{
    result["status"] = "FAIL";
    result["failure"] = exception.Message;
    Console.Error.WriteLine($"CERTIFICATION_FAILURE {exception.Message}");
}
finally
{
    password = null;
    try
    {
        if (mutationsStarted && adminContext != null)
        {
            var page = adminContext.Pages.FirstOrDefault(item => item.Url.StartsWith(AdminUrl, StringComparison.OrdinalIgnoreCase))
                ?? await adminContext.NewPageAsync();
            if (!page.Url.StartsWith(AdminUrl, StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync($"{AdminUrl}/storefront/footer-content", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            }

            var currentConfiguration = await BrowserJsonAsync(page, "/api/admin/footer");
            var restoreRequest = BuildRequestFromBaseline(baseline, RequiredString(currentConfiguration, "revision"));
            var restoreResponse = await BrowserRequestAsync(page, "PUT", "/api/admin/footer", restoreRequest);
            Require(restoreResponse.Status == 200, $"Logical Footer restoration returned {restoreResponse.Status}: {restoreResponse.Body}");
        }

        if (mutationsStarted)
        {
            await RestoreExactFooterRowsAsync(connectionString, tenantId, baseline);
        }

        var restored = await CaptureSnapshotAsync(connectionString, tenantId, includePublicState: true);
        RequireSnapshotBaseline(restored, baseline);
        restorationSucceeded = true;
        result["restoration"] = "PASS_EXACT_SQL_AND_PUBLIC_BASELINE";
        AddCheck(checks, "Original Footer IDs, timestamps, content, public reads, and unrelated fingerprints were restored exactly");
        if (string.Equals(result["status"]?.GetValue<string>(), "PASS_PENDING_RESTORATION", StringComparison.Ordinal))
        {
            result["status"] = "PASS";
        }
    }
    catch (Exception restoreException)
    {
        result["restoration"] = "FAIL";
        result["restorationFailure"] = restoreException.Message;
        result["status"] = "FAIL";
        Console.Error.WriteLine($"RESTORATION_FAILURE {restoreException.Message}");
    }

    result["completedAtUtc"] = DateTime.UtcNow;
    result["restorationSucceeded"] = restorationSucceeded;
    await File.WriteAllTextAsync(resultPath, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    if (adminContext != null) await adminContext.DisposeAsync();
    if (browser != null) await browser.DisposeAsync();
    playwright?.Dispose();
}

if (!string.Equals(result["status"]?.GetValue<string>(), "PASS", StringComparison.Ordinal))
{
    Environment.ExitCode = 1;
    Console.WriteLine("FINAL_BROWSER_CERTIFICATION FAIL");
}
else
{
    Console.WriteLine("FINAL_BROWSER_CERTIFICATION PASS");
}

static void AddCheck(JsonArray checks, string value) => checks.Add(value);

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string RequiredString(JsonObject value, string property)
{
    return value[property]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Required property '{property}' is missing.");
}

static async Task LoginAsync(IPage page, string username, string password)
{
    await page.GotoAsync("http://localhost:5173/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.Locator("#pos-login-username").FillAsync(username);
    await page.Locator("#pos-login-password").FillAsync(password);
    await page.Locator("button[type=submit]").ClickAsync();
    await page.WaitForURLAsync(url => !new Uri(url).AbsolutePath.Equals("/login", StringComparison.OrdinalIgnoreCase),
        new PageWaitForURLOptions { Timeout = 30_000 });
}

static async Task RequireAdminAccessibilityAsync(IPage page)
{
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Section title EN" }).First.WaitForAsync();
    await page.GetByRole(AriaRole.Combobox, new() { Name = "Link 1 type" }).First.WaitForAsync();
    await page.GetByRole(AriaRole.Combobox, new() { Name = "Link 1 CMS page" }).First.WaitForAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "Link 1 opens in new tab" }).First.WaitForAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Move link 1 up" }).First.WaitForAsync();
    await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("Delete section", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).First.WaitForAsync();
}

static async Task<BrowserResponse> BrowserRequestAsync(
    IPage page,
    string method,
    string url,
    JsonNode? body = null,
    IReadOnlyDictionary<string, string>? extraHeaders = null)
{
    var responseText = await page.EvaluateAsync<string>("""
        async ({ method, url, bodyJson, extraHeaders }) => {
            const csrf = document.cookie
                .split(';')
                .map(value => value.trim())
                .find(value => value.startsWith('csrf_token='))
                ?.substring('csrf_token='.length) || '';
            const headers = { 'Content-Type': 'application/json', ...(extraHeaders || {}) };
            if (csrf && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
                headers['X-CSRF-Token'] = decodeURIComponent(csrf);
            }
            const response = await fetch(url, {
                method,
                headers,
                credentials: 'include',
                body: bodyJson == null ? undefined : bodyJson,
            });
            return JSON.stringify({ status: response.status, body: await response.text() });
        }
        """, new
    {
        method,
        url,
        bodyJson = body?.ToJsonString(),
        extraHeaders,
    });
    var response = JsonNode.Parse(responseText)?.AsObject()
        ?? throw new InvalidOperationException($"Browser request {method} {url} returned an invalid harness envelope.");
    return new BrowserResponse(response["status"]!.GetValue<int>(), response["body"]?.GetValue<string>() ?? string.Empty);
}

static async Task<JsonObject> BrowserJsonAsync(IPage page, string url)
{
    var response = await BrowserRequestAsync(page, "GET", url);
    Require(response.Status == 200, $"GET {url} returned {response.Status}: {response.Body}");
    return JsonNode.Parse(response.Body)?.AsObject()
        ?? throw new InvalidOperationException($"GET {url} returned invalid JSON.");
}

static JsonObject BuildRequestFromConfiguration(JsonObject configuration, string revision, string? firstTitleOverride = null)
{
    var sections = new JsonArray();
    foreach (var sectionNode in configuration["sections"]!.AsArray())
    {
        var source = sectionNode!.AsObject();
        var links = new JsonArray();
        foreach (var linkNode in source["links"]!.AsArray())
        {
            var link = linkNode!.AsObject();
            links.Add(new JsonObject
            {
                ["label"] = link["label"]?.DeepClone(),
                ["labelLocalized"] = link["labelLocalized"]?.DeepClone(),
                ["type"] = link["type"]?.DeepClone(),
                ["url"] = link["url"]?.DeepClone(),
                ["cmsPageSlug"] = link["cmsPageSlug"]?.DeepClone(),
                ["openInNewTab"] = link["openInNewTab"]?.DeepClone(),
                ["order"] = link["order"]?.DeepClone(),
                ["isActive"] = link["isActive"]?.DeepClone(),
            });
        }

        var title = sections.Count == 0 && firstTitleOverride != null ? firstTitleOverride : RequiredString(source, "title");
        var localized = source["titleLocalized"]?.DeepClone()?.AsObject() ?? new JsonObject();
        localized["en"] = title;
        sections.Add(new JsonObject
        {
            ["title"] = title,
            ["titleLocalized"] = localized,
            ["order"] = source["order"]?.DeepClone(),
            ["isActive"] = source["isActive"]?.DeepClone(),
            ["links"] = links,
        });
    }

    var socialLinks = new JsonArray();
    foreach (var socialNode in configuration["socialLinks"]!.AsArray())
    {
        var social = socialNode!.AsObject();
        socialLinks.Add(new JsonObject
        {
            ["platform"] = social["platform"]?.DeepClone(),
            ["url"] = social["url"]?.DeepClone(),
            ["icon"] = social["icon"]?.DeepClone(),
            ["order"] = social["order"]?.DeepClone(),
            ["isActive"] = social["isActive"]?.DeepClone(),
        });
    }

    return new JsonObject
    {
        ["expectedRevision"] = revision,
        ["sections"] = sections,
        ["socialLinks"] = socialLinks,
    };
}

static JsonObject BuildRequestFromBaseline(JsonObject baseline, string revision)
{
    var linksBySection = baseline["links"]!.AsArray()
        .Select(item => item!.AsObject())
        .GroupBy(item => RequiredString(item, "SectionId"))
        .ToDictionary(group => group.Key, group => group.OrderBy(item => item["SortOrder"]!.GetValue<int>()).ToList());
    var sections = new JsonArray();
    foreach (var sectionNode in baseline["sections"]!.AsArray().OrderBy(item => item!["SortOrder"]!.GetValue<int>()))
    {
        var section = sectionNode!.AsObject();
        var sectionLinks = new JsonArray();
        if (linksBySection.TryGetValue(RequiredString(section, "Id"), out var links))
        {
            foreach (var link in links)
            {
                sectionLinks.Add(new JsonObject
                {
                    ["label"] = link["Label"]?.DeepClone(),
                    ["labelLocalized"] = new JsonObject { ["en"] = link["Label"]?.DeepClone(), ["ar"] = link["LabelAr"]?.DeepClone() },
                    ["type"] = link["LinkType"]?.DeepClone(),
                    ["url"] = link["Url"]?.DeepClone(),
                    ["cmsPageSlug"] = link["CmsPageSlug"]?.DeepClone(),
                    ["openInNewTab"] = link["OpenInNewTab"]?.DeepClone(),
                    ["order"] = link["SortOrder"]?.DeepClone(),
                    ["isActive"] = link["IsActive"]?.DeepClone(),
                });
            }
        }

        sections.Add(new JsonObject
        {
            ["title"] = section["Title"]?.DeepClone(),
            ["titleLocalized"] = new JsonObject { ["en"] = section["Title"]?.DeepClone(), ["ar"] = section["TitleAr"]?.DeepClone() },
            ["order"] = section["SortOrder"]?.DeepClone(),
            ["isActive"] = section["IsActive"]?.DeepClone(),
            ["links"] = sectionLinks,
        });
    }

    var socialLinks = new JsonArray();
    foreach (var socialNode in baseline["socialLinks"]!.AsArray().OrderBy(item => item!["SortOrder"]!.GetValue<int>()))
    {
        var social = socialNode!.AsObject();
        socialLinks.Add(new JsonObject
        {
            ["platform"] = social["Platform"]?.DeepClone(),
            ["url"] = social["Url"]?.DeepClone(),
            ["icon"] = social["Icon"]?.DeepClone(),
            ["order"] = social["SortOrder"]?.DeepClone(),
            ["isActive"] = social["IsActive"]?.DeepClone(),
        });
    }

    return new JsonObject { ["expectedRevision"] = revision, ["sections"] = sections, ["socialLinks"] = socialLinks };
}

static JsonObject BuildCertificationRequest(string revision)
{
    return new JsonObject
    {
        ["expectedRevision"] = revision,
        ["sections"] = new JsonArray
        {
            new JsonObject
            {
                ["title"] = CertifiedCompanyTitle,
                ["titleLocalized"] = new JsonObject { ["en"] = CertifiedCompanyTitle, ["ar"] = CertifiedCompanyTitleAr },
                ["order"] = 0,
                ["isActive"] = true,
                ["links"] = new JsonArray
                {
                    FooterLink(CertifiedLongLabel, "رابط اعتماد طويل يثبت التفاف نصوص التذييل بصورة سليمة على الشاشات الصغيرة", "Cms", null, "about", true, 0, true),
                    FooterLink("Shop all products", "تسوق كل المنتجات", "Internal", "/products", null, false, 1, true),
                    FooterLink("External certification reference", "مرجع الاعتماد الخارجي", "External", "https://example.com/footer-certification", null, true, 2, true),
                },
            },
            new JsonObject
            {
                ["title"] = CertifiedSupportTitle,
                ["titleLocalized"] = new JsonObject { ["en"] = CertifiedSupportTitle, ["ar"] = CertifiedSupportTitleAr },
                ["order"] = 1,
                ["isActive"] = true,
                ["links"] = new JsonArray
                {
                    FooterLink("Contact support", "تواصل مع الدعم", "Cms", null, "contact", false, 0, true),
                    FooterLink("Privacy policy", "سياسة الخصوصية", "Cms", null, "privacy", false, 1, true),
                },
            },
            new JsonObject
            {
                ["title"] = "Hidden Certification Section",
                ["titleLocalized"] = new JsonObject { ["en"] = "Hidden Certification Section", ["ar"] = "قسم اعتماد مخفي" },
                ["order"] = 2,
                ["isActive"] = false,
                ["links"] = new JsonArray
                {
                    FooterLink("Hidden terms", "شروط مخفية", "Cms", null, "terms", false, 0, true),
                },
            },
        },
        ["socialLinks"] = new JsonArray
        {
            new JsonObject
            {
                ["platform"] = CertifiedSocialPlatform,
                ["url"] = "https://example.com/footer-social-certification",
                ["icon"] = "link",
                ["order"] = 0,
                ["isActive"] = true,
            },
        },
    };
}

static JsonObject FooterLink(string label, string labelAr, string type, string? url, string? cmsPageSlug, bool openInNewTab, int order, bool isActive)
{
    return new JsonObject
    {
        ["label"] = label,
        ["labelLocalized"] = new JsonObject { ["en"] = label, ["ar"] = labelAr },
        ["type"] = type,
        ["url"] = url,
        ["cmsPageSlug"] = cmsPageSlug,
        ["openInNewTab"] = openInNewTab,
        ["order"] = order,
        ["isActive"] = isActive,
    };
}

static void RequirePublicCertification(JsonObject english, JsonObject arabic)
{
    Require(RequiredString(english, "footerSource") == "Tenant", "English public Footer did not use tenant authority.");
    Require(RequiredString(english, "socialSource") == "Tenant", "English public social links did not use tenant authority.");
    var sections = english["sections"]!.AsArray();
    Require(sections.Count == 2, $"English public Footer exposed {sections.Count} sections instead of 2 active sections.");
    Require(RequiredString(sections[0]!.AsObject(), "title") == CertifiedCompanyTitle, "English first section title is incorrect.");
    Require(RequiredString(sections[1]!.AsObject(), "title") == CertifiedSupportTitle, "English second section title is incorrect.");
    var about = sections[0]!["links"]!.AsArray()[0]!.AsObject();
    Require(RequiredString(about, "url") == "/about" && about["openInNewTab"]!.GetValue<bool>(),
        "CMS URL projection did not preserve open-in-new-tab intent.");
    Require(english["socialLinks"]!.AsArray().Count == 1 && RequiredString(english["socialLinks"]![0]!.AsObject(), "platform") == CertifiedSocialPlatform,
        "The active social link is missing from the public Footer.");
    Require(!english.ToJsonString().Contains("Hidden Certification Section", StringComparison.Ordinal),
        "An inactive Footer section leaked into the public response.");

    var arabicSections = arabic["sections"]!.AsArray();
    Require(RequiredString(arabicSections[0]!.AsObject(), "title") == CertifiedCompanyTitleAr, "Arabic first section title is incorrect.");
    Require(RequiredString(arabicSections[1]!.AsObject(), "title") == CertifiedSupportTitleAr, "Arabic second section title is incorrect.");
}

static async Task CertifyStorefrontAsync(IBrowser browser, string storefrontUrl, string evidenceDirectory, JsonArray screenshots)
{
    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
    });
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    await page.GotoAsync(storefrontUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    var footer = page.Locator("footer");
    await footer.GetByText(CertifiedCompanyTitle, new() { Exact = true }).WaitForAsync();
    await footer.GetByText(CertifiedSupportTitle, new() { Exact = true }).WaitForAsync();
    Require(await footer.GetByText("Hidden Certification Section", new() { Exact = true }).CountAsync() == 0,
        "Inactive section rendered in the desktop Storefront Footer.");
    var about = footer.GetByRole(AriaRole.Link, new() { Name = CertifiedLongLabel, Exact = true });
    Require(await about.GetAttributeAsync("target") == "_blank", "CMS link lost target=_blank in the rendered Footer.");
    Require((await about.GetAttributeAsync("rel"))?.Contains("noopener", StringComparison.Ordinal) == true,
        "New-tab CMS link is missing rel=noopener.");
    await SaveScreenshotAsync(page, Path.Combine(evidenceDirectory, "04-storefront-desktop-en.png"), screenshots);

    await page.SetViewportSizeAsync(390, 844);
    await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.Locator("footer").GetByText(CertifiedCompanyTitle, new() { Exact = true }).WaitForAsync();
    Require(await FooterFitsViewportAsync(page), "English mobile Footer has horizontal overflow.");
    await SaveScreenshotAsync(page, Path.Combine(evidenceDirectory, "05-storefront-mobile-en.png"), screenshots);

    await page.GotoAsync($"{storefrontUrl}/ar", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.Locator("footer").GetByText(CertifiedCompanyTitleAr, new() { Exact = true }).WaitForAsync();
    Require(await page.Locator("html").GetAttributeAsync("dir") == "rtl", "Arabic route did not set document direction to RTL.");
    Require(await FooterFitsViewportAsync(page), "Arabic mobile Footer has horizontal overflow.");
    await SaveScreenshotAsync(page, Path.Combine(evidenceDirectory, "06-storefront-mobile-ar.png"), screenshots);
    Require(pageErrors.Count == 0, $"Storefront page errors occurred: {string.Join(" | ", pageErrors)}");
}

static async Task CertifyThemePreviewAsync(
    IBrowser browser,
    string themeId,
    string previewUrl,
    string evidenceDirectory,
    JsonArray screenshots,
    JsonArray themes)
{
    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
    });
    var page = await context.NewPageAsync();
    var configResponseTask = page.WaitForResponseAsync(response => response.Url.Contains("/api/storefront/theme-preview/config", StringComparison.Ordinal));
    await page.GotoAsync(previewUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    var configResponse = await configResponseTask;
    Require(configResponse.Status == 200, $"Theme {themeId} preview config returned {configResponse.Status}.");
    var config = JsonNode.Parse(await configResponse.TextAsync())?.AsObject()
        ?? throw new InvalidOperationException($"Theme {themeId} preview config returned invalid JSON.");
    Require(RequiredString(config, "storefrontTheme") == themeId, $"Signed preview resolved {RequiredString(config, "storefrontTheme")} instead of {themeId}.");
    var footer = page.Locator("footer");
    await footer.GetByText(CertifiedCompanyTitle, new() { Exact = true }).WaitForAsync();
    await footer.GetByText(CertifiedSupportTitle, new() { Exact = true }).WaitForAsync();
    Require(await FooterFitsViewportAsync(page), $"Theme {themeId} Footer has horizontal overflow.");
    var screenshotPath = Path.Combine(evidenceDirectory, $"theme-{themeId}.png");
    await SaveScreenshotAsync(page, screenshotPath, screenshots);
    themes.Add(new JsonObject { ["themeId"] = themeId, ["status"] = "PASS", ["screenshot"] = Path.GetFileName(screenshotPath) });
}

static Task<bool> FooterFitsViewportAsync(IPage page)
{
    return page.EvaluateAsync<bool>("""
        () => {
            const footer = document.querySelector('footer');
            if (!footer) return false;
            const bounds = footer.getBoundingClientRect();
            return footer.scrollWidth <= footer.clientWidth + 1
                && bounds.left >= -1
                && bounds.right <= window.innerWidth + 1;
        }
        """);
}

static async Task SaveScreenshotAsync(IPage page, string path, JsonArray screenshots)
{
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
    screenshots.Add(Path.GetFileName(path));
}

static List<string> ReadOfficialThemes(string authorityPath)
{
    var authority = JsonNode.Parse(File.ReadAllText(authorityPath))!.AsObject();
    return authority["themes"]!.AsArray()
        .Select(item => item!.AsObject())
        .Where(theme => RequiredString(theme, "officialStatus") == "official"
            && RequiredString(theme, "activationStatus") == "active"
            && theme["runtimeAvailability"]!.GetValue<bool>())
        .Select(theme => RequiredString(theme, "id"))
        .ToList();
}

static async Task<JsonObject> CaptureSnapshotAsync(string connectionString, Guid tenantId, bool includePublicState)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
    {
        await readOnly.ExecuteNonQueryAsync();
    }

    var snapshot = new JsonObject
    {
        ["capturedAtUtc"] = DateTime.UtcNow,
        ["environment"] = "MAIN/PRIMARY development",
        ["tenantId"] = tenantId.ToString(),
        ["database"] = connection.Database,
        ["databaseHost"] = connection.Host,
        ["tenant"] = await ReadSingleAsync(connection, transaction, """
            SELECT "Id", "Name", "Slug", "IsActive", "Status", "DeletedAtUtc"
            FROM "Tenants" WHERE "Id" = @tenantId
            """, tenantId),
        ["storeConfig"] = await ReadSingleAsync(connection, transaction, """
            SELECT "Id", "TenantId", "StoreName", "StorefrontStatus", "ThemeId",
                   "ThemePresetKey", "ThemeLivePresetKey", "ThemeDraftOverridesJson", "ThemeLiveOverridesJson",
                   xmin::text AS "RowVersion", md5(COALESCE("CustomizationJson", '')) AS "CustomizationHash"
            FROM "StoreConfigs" WHERE "TenantId" = @tenantId
            """, tenantId),
        ["sections"] = await ReadManyAsync(connection, transaction, """
            SELECT "Id", "TenantId", "Title", "TitleAr", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt"
            FROM "StorefrontFooterSections" WHERE "TenantId" = @tenantId ORDER BY "SortOrder", "Id"
            """, tenantId),
        ["links"] = await ReadManyAsync(connection, transaction, """
            SELECT links."Id", links."SectionId", links."Label", links."LabelAr", links."LinkType",
                   links."Url", links."CmsPageSlug", links."OpenInNewTab", links."SortOrder", links."IsActive",
                   links."CreatedAt", links."UpdatedAt"
            FROM "StorefrontFooterLinks" AS links
            INNER JOIN "StorefrontFooterSections" AS sections ON sections."Id" = links."SectionId"
            WHERE sections."TenantId" = @tenantId
            ORDER BY sections."SortOrder", sections."Id", links."SortOrder", links."Id"
            """, tenantId),
        ["socialLinks"] = await ReadManyAsync(connection, transaction, """
            SELECT "Id", "TenantId", "Platform", "Url", "Icon", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt"
            FROM "StorefrontSocialLinks" WHERE "TenantId" = @tenantId ORDER BY "SortOrder", "Id"
            """, tenantId),
        ["publishedCmsPages"] = await ReadManyAsync(connection, transaction, """
            SELECT "PageGroupId", "Slug", "Title", "Version", "IsDraft", "IsPublished", "PublishedAt", "UpdatedAt"
            FROM "CmsPages"
            WHERE "TenantId" = @tenantId AND NOT "IsDeleted" AND "IsPublished" AND NOT "IsDraft" AND "PublishedAt" IS NOT NULL
            ORDER BY "Slug", "Version"
            """, tenantId),
        ["isolationFingerprint"] = await ReadSingleAsync(connection, transaction, """
            SELECT
              md5(COALESCE((SELECT string_agg("Id"::text || ':' || "Title" || ':' || "SortOrder"::text || ':' || "IsActive"::text, '|' ORDER BY "Id") FROM "StorefrontFooterSections" WHERE "TenantId" = @tenantId), '')) AS "FooterSections",
              md5(COALESCE((SELECT string_agg(links."Id"::text || ':' || links."Label" || ':' || links."LinkType" || ':' || COALESCE(links."Url", '') || ':' || COALESCE(links."CmsPageSlug", '') || ':' || links."SortOrder"::text || ':' || links."IsActive"::text, '|' ORDER BY links."Id") FROM "StorefrontFooterLinks" links INNER JOIN "StorefrontFooterSections" sections ON sections."Id" = links."SectionId" WHERE sections."TenantId" = @tenantId), '')) AS "FooterLinks",
              md5(COALESCE((SELECT string_agg("Id"::text || ':' || "Platform" || ':' || "Url" || ':' || "SortOrder"::text || ':' || "IsActive"::text, '|' ORDER BY "Id") FROM "StorefrontSocialLinks" WHERE "TenantId" = @tenantId), '')) AS "SocialLinks",
              md5(COALESCE((SELECT "CustomizationJson" FROM "StoreConfigs" WHERE "TenantId" = @tenantId), '')) AS "StoreConfig",
              md5(COALESCE((SELECT string_agg("Id"::text || ':' || "Version"::text || ':' || "IsDraft"::text || ':' || "IsPublished"::text || ':' || "UpdatedAt"::text, '|' ORDER BY "Id") FROM "HomepageSections" WHERE "TenantId" = @tenantId), '')) AS "Homepage",
              md5(COALESCE((SELECT string_agg("Id"::text || ':' || "Version"::text || ':' || "IsDraft"::text || ':' || "IsPublished"::text || ':' || "UpdatedAt"::text, '|' ORDER BY "Id") FROM "CmsPages" WHERE "TenantId" = @tenantId), '')) AS "CmsPages",
              md5(COALESCE((SELECT string_agg("Id"::text || ':' || "UpdatedAt"::text, '|' ORDER BY "Id") FROM "Products" WHERE "TenantId" = @tenantId), '')) AS "Products"
            """, tenantId),
    };
    await transaction.RollbackAsync();

    if (includePublicState)
    {
        snapshot["publicFooterEn"] = await ReadPublicJsonAsync("/api/storefront/footer?lang=en", "ahramstore.localhost:5174");
        snapshot["publicFooterAr"] = await ReadPublicJsonAsync("/api/storefront/footer?lang=ar", "ahramstore.localhost:5174");
        snapshot["publicNavigation"] = await ReadPublicJsonAsync("/api/storefront/navigation?lang=en", "ahramstore.localhost:5174");
        snapshot["publicStoreConfig"] = await ReadPublicJsonAsync("/api/storefront/store-config", "ahramstore.localhost:5174");
    }
    return snapshot;
}

static async Task<JsonObject> ReadOtherTenantFooterFingerprintAsync(string connectionString, Guid tenantId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        SELECT
          md5(COALESCE((SELECT string_agg("Id"::text || ':' || COALESCE("TenantId"::text, '') || ':' || "Title" || ':' || "SortOrder"::text || ':' || "IsActive"::text || ':' || "UpdatedAt"::text, '|' ORDER BY "Id") FROM "StorefrontFooterSections" WHERE "TenantId" IS DISTINCT FROM @tenantId), '')) AS "Sections",
          md5(COALESCE((SELECT string_agg(links."Id"::text || ':' || links."SectionId"::text || ':' || links."Label" || ':' || links."SortOrder"::text || ':' || links."IsActive"::text || ':' || links."UpdatedAt"::text, '|' ORDER BY links."Id") FROM "StorefrontFooterLinks" links INNER JOIN "StorefrontFooterSections" sections ON sections."Id" = links."SectionId" WHERE sections."TenantId" IS DISTINCT FROM @tenantId), '')) AS "Links",
          md5(COALESCE((SELECT string_agg("Id"::text || ':' || COALESCE("TenantId"::text, '') || ':' || "Platform" || ':' || "Url" || ':' || "UpdatedAt"::text, '|' ORDER BY "Id") FROM "StorefrontSocialLinks" WHERE "TenantId" IS DISTINCT FROM @tenantId), '')) AS "SocialLinks"
        """, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return new JsonObject
    {
        ["Sections"] = reader.GetString(0),
        ["Links"] = reader.GetString(1),
        ["SocialLinks"] = reader.GetString(2),
    };
}

static async Task<JsonObject?> ReadSingleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid tenantId)
{
    var rows = await ReadManyAsync(connection, transaction, sql, tenantId);
    return rows.Count == 0 ? null : rows[0]!.AsObject().DeepClone().AsObject();
}

static async Task<JsonArray> ReadManyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid tenantId)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("tenantId", tenantId);
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new JsonArray();
    while (await reader.ReadAsync())
    {
        var row = new JsonObject();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var value = reader.IsDBNull(index) ? null : reader.GetValue(index);
            row[reader.GetName(index)] = JsonValue.Create(value switch
            {
                Guid guid => guid.ToString(),
                DateTime dateTime => dateTime.ToUniversalTime(),
                _ => value,
            });
        }
        rows.Add(row);
    }
    return rows;
}

static async Task<JsonObject> ReadPublicJsonAsync(string path, string host)
{
    using var http = new HttpClient { BaseAddress = new Uri(StorefrontTransportUrl) };
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    request.Headers.Host = host;
    request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    Require(response.IsSuccessStatusCode, $"GET {host}{path} returned {(int)response.StatusCode}: {text}");
    return JsonNode.Parse(text)?.AsObject() ?? throw new InvalidOperationException($"GET {host}{path} returned invalid JSON.");
}

static void RequireSnapshotBaseline(JsonObject actual, JsonObject baseline)
{
    foreach (var property in new[] { "tenant", "storeConfig", "sections", "links", "socialLinks", "publishedCmsPages", "isolationFingerprint", "publicNavigation", "publicStoreConfig" })
    {
        Require(JsonNode.DeepEquals(actual[property], baseline[property]), $"Current {property} does not match the immutable baseline.");
    }
    Require(JsonNode.DeepEquals(WithoutGeneratedAt(actual["publicFooterEn"]!), WithoutGeneratedAt(baseline["publicFooterEn"]!)),
        "Current English public Footer does not match the immutable baseline.");
    Require(JsonNode.DeepEquals(WithoutGeneratedAt(actual["publicFooterAr"]!), WithoutGeneratedAt(baseline["publicFooterAr"]!)),
        "Current Arabic public Footer does not match the immutable baseline.");
}

static JsonNode WithoutGeneratedAt(JsonNode node)
{
    var clone = node.DeepClone().AsObject();
    clone.Remove("generatedAtUtc");
    return clone;
}

static void RequireUnrelatedFingerprintsUnchanged(JsonObject before, JsonObject after)
{
    foreach (var property in new[] { "StoreConfig", "Homepage", "CmsPages", "Products" })
    {
        Require(RequiredString(before, property) == RequiredString(after, property), $"Unrelated {property} fingerprint changed.");
    }
}

static async Task RestoreExactFooterRowsAsync(string connectionString, Guid tenantId, JsonObject baseline)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
    try
    {
        await ExecuteAsync(connection, transaction, """
            DELETE FROM "StorefrontFooterLinks"
            WHERE "SectionId" IN (SELECT "Id" FROM "StorefrontFooterSections" WHERE "TenantId" = @tenantId)
            """, ("tenantId", tenantId));
        await ExecuteAsync(connection, transaction, "DELETE FROM \"StorefrontFooterSections\" WHERE \"TenantId\" = @tenantId", ("tenantId", tenantId));
        await ExecuteAsync(connection, transaction, "DELETE FROM \"StorefrontSocialLinks\" WHERE \"TenantId\" = @tenantId", ("tenantId", tenantId));

        foreach (var node in baseline["sections"]!.AsArray())
        {
            var row = node!.AsObject();
            await ExecuteAsync(connection, transaction, """
                INSERT INTO "StorefrontFooterSections"
                    ("Id", "TenantId", "Title", "TitleAr", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES (@id, @tenantId, @title, @titleAr, @sortOrder, @isActive, @createdAt, @updatedAt)
                """,
                ("id", Guid.Parse(RequiredString(row, "Id"))),
                ("tenantId", tenantId),
                ("title", RequiredString(row, "Title")),
                ("titleAr", DbValue(row["TitleAr"])),
                ("sortOrder", row["SortOrder"]!.GetValue<int>()),
                ("isActive", row["IsActive"]!.GetValue<bool>()),
                ("createdAt", DateTime.Parse(RequiredString(row, "CreatedAt")).ToUniversalTime()),
                ("updatedAt", DateTime.Parse(RequiredString(row, "UpdatedAt")).ToUniversalTime()));
        }

        foreach (var node in baseline["links"]!.AsArray())
        {
            var row = node!.AsObject();
            await ExecuteAsync(connection, transaction, """
                INSERT INTO "StorefrontFooterLinks"
                    ("Id", "SectionId", "Label", "LabelAr", "LinkType", "Url", "CmsPageSlug", "OpenInNewTab", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES (@id, @sectionId, @label, @labelAr, @linkType, @url, @cmsPageSlug, @openInNewTab, @sortOrder, @isActive, @createdAt, @updatedAt)
                """,
                ("id", Guid.Parse(RequiredString(row, "Id"))),
                ("sectionId", Guid.Parse(RequiredString(row, "SectionId"))),
                ("label", RequiredString(row, "Label")),
                ("labelAr", DbValue(row["LabelAr"])),
                ("linkType", RequiredString(row, "LinkType")),
                ("url", DbValue(row["Url"])),
                ("cmsPageSlug", DbValue(row["CmsPageSlug"])),
                ("openInNewTab", row["OpenInNewTab"]!.GetValue<bool>()),
                ("sortOrder", row["SortOrder"]!.GetValue<int>()),
                ("isActive", row["IsActive"]!.GetValue<bool>()),
                ("createdAt", DateTime.Parse(RequiredString(row, "CreatedAt")).ToUniversalTime()),
                ("updatedAt", DateTime.Parse(RequiredString(row, "UpdatedAt")).ToUniversalTime()));
        }

        foreach (var node in baseline["socialLinks"]!.AsArray())
        {
            var row = node!.AsObject();
            await ExecuteAsync(connection, transaction, """
                INSERT INTO "StorefrontSocialLinks"
                    ("Id", "TenantId", "Platform", "Url", "Icon", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES (@id, @tenantId, @platform, @url, @icon, @sortOrder, @isActive, @createdAt, @updatedAt)
                """,
                ("id", Guid.Parse(RequiredString(row, "Id"))),
                ("tenantId", tenantId),
                ("platform", RequiredString(row, "Platform")),
                ("url", RequiredString(row, "Url")),
                ("icon", DbValue(row["Icon"])),
                ("sortOrder", row["SortOrder"]!.GetValue<int>()),
                ("isActive", row["IsActive"]!.GetValue<bool>()),
                ("createdAt", DateTime.Parse(RequiredString(row, "CreatedAt")).ToUniversalTime()),
                ("updatedAt", DateTime.Parse(RequiredString(row, "UpdatedAt")).ToUniversalTime()));
        }
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

static object DbValue(JsonNode? node) => node == null ? DBNull.Value : node.GetValue<string>();

static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }
    await command.ExecuteNonQueryAsync();
}

internal sealed record BrowserResponse(int Status, string Body);

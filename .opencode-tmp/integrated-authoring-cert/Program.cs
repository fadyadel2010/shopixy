using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Npgsql;

const string TenantId = "c9c5c1ae-949e-498e-bc63-07242daeb49d";
const string IsolationTenantId = "83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5";
const string AdminUrl = "http://localhost:5173";
const string StorefrontUrl = "http://ahramstore.localhost:5174";
const string IsolationStorefrontUrl = "http://shopixy.localhost:5174";

var mode = args.FirstOrDefault() ?? "baseline";
var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));
var evidenceDirectory = Path.Combine(Directory.GetCurrentDirectory(), "evidence");
var baselinePath = Path.Combine(evidenceDirectory, "immutable-baseline.json");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var email = Environment.GetEnvironmentVariable("INTEGRATED_CERT_EMAIL")
    ?? throw new InvalidOperationException("INTEGRATED_CERT_EMAIL is required in process memory.");
var password = Environment.GetEnvironmentVariable("INTEGRATED_CERT_PASSWORD")
    ?? throw new InvalidOperationException("INTEGRATED_CERT_PASSWORD is required in process memory.");
var connectionString = ReadConnectionString(Path.Combine(root, "PointOfSale.API", "appsettings.Development.local.json"));

Directory.CreateDirectory(evidenceDirectory);
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Channel = "msedge",
    Headless = true,
});
await using var adminContext = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
});
var admin = await adminContext.NewPageAsync();
await LoginAsync(admin, email, password);
password = string.Empty;

if (mode.Equals("baseline", StringComparison.OrdinalIgnoreCase))
{
    if (File.Exists(baselinePath))
    {
        throw new InvalidOperationException($"Immutable baseline already exists: {baselinePath}");
    }

    var baseline = await CaptureBaselineAsync(admin, browser, connectionString);
    await File.WriteAllTextAsync(baselinePath, baseline.ToJsonString(jsonOptions));
    File.SetAttributes(baselinePath, File.GetAttributes(baselinePath) | FileAttributes.ReadOnly);
    var hash = Sha256(await File.ReadAllBytesAsync(baselinePath));
    Console.WriteLine($"INTEGRATED_BASELINE_CAPTURED path={baselinePath} sha256={hash}");
    return;
}

if (mode.Equals("certify", StringComparison.OrdinalIgnoreCase))
{
    if (!File.Exists(baselinePath)) throw new InvalidOperationException("Immutable baseline is required.");
    var baselineBytes = await File.ReadAllBytesAsync(baselinePath);
    var baselineHash = Sha256(baselineBytes);
    var baseline = JsonNode.Parse(baselineBytes)?.AsObject()
        ?? throw new InvalidOperationException("Immutable baseline is invalid.");
    await RunCertificationAsync(admin, adminContext, browser, connectionString, root, evidenceDirectory, baseline, baselineHash);
    return;
}

throw new InvalidOperationException($"Unsupported mode '{mode}'.");

static async Task RunCertificationAsync(
    IPage admin,
    IBrowserContext adminContext,
    IBrowser browser,
    string connectionString,
    string root,
    string evidenceDirectory,
    JsonObject baseline,
    string baselineHash)
{
    const string marker = "Integrated Cert 20260926";
    const string markerAr = "اعتماد متكامل";
    var runId = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var cmsSlug = $"integrated-cert-{runId}";
    var renamedSlug = $"integrated-cert-renamed-{runId}";
    var resultPath = Path.Combine(evidenceDirectory, "final-audit.json");
    var screenshots = Path.Combine(evidenceDirectory, "screenshots");
    Directory.CreateDirectory(screenshots);
    var checks = new JsonArray();
    var crossSurface = new JsonArray();
    var themeMatrix = new JsonArray();
    var blockers = new JsonArray();
    var result = new JsonObject
    {
        ["startedAtUtc"] = DateTime.UtcNow,
        ["baselineSha256"] = baselineHash,
        ["checks"] = checks,
        ["crossSurfaceMatrix"] = crossSurface,
        ["themeMatrix"] = themeMatrix,
        ["blockers"] = blockers,
        ["status"] = "RUNNING",
    };

    var baselineAdmin = baseline["admin"]!.AsObject();
    var baselineConfig = baselineAdmin["storeConfig"]!.AsObject();
    var baselineNavigation = baselineAdmin["navigation"]!.AsObject();
    var baselineFooter = baselineAdmin["footer"]!.AsObject();
    var baselinePublic = baseline["publicTenantA"]!.AsObject();
    Guid? disposablePageId = null;
    var disposableVersion = 0;
    var mutationsStarted = false;
    var restored = false;

    try
    {
        Ensure(Sha256(await File.ReadAllBytesAsync(Path.Combine(evidenceDirectory, "immutable-baseline.json"))) == baselineHash,
            "Immutable baseline hash changed before certification.");
        await AssertCurrentContentMatchesBaselineAsync(admin, browser, baseline);
        checks.Add("Pre-mutation Admin/public content matches immutable baseline");

        var config = (await AdminJsonAsync(admin, "/api/admin/store-config")).AsObject();
        var originalTheme = RequiredString(config, "storefrontTheme");
        var candidateTheme = originalTheme == "modern" ? "minimal" : "modern";
        var originalAppearance = config["liveTheme"]!.DeepClone();
        var originalHomepage = config["pages"]!["home"]!.DeepClone();
        var originalNavigationPublic = baselinePublic["navigationEn"]!.DeepClone();
        var originalFooterPublic = NormalizeFooterPublic(baselinePublic["footerEn"]!);
        var originalCmsList = baselineAdmin["cmsPages"]!.DeepClone();

        var themeResponse = await BrowserRequestAsync(admin, "POST", "/api/admin/store-config/theme/activate",
            new JsonObject { ["themeId"] = candidateTheme, ["rowVersion"] = RequiredString(config, "rowVersion") }, RequiredString(config, "rowVersion"));
        Ensure(themeResponse.Status == 200, $"Theme activation returned {themeResponse.Status}: {themeResponse.Body}");
        mutationsStarted = true;
        config = ParseObject(themeResponse.Body);
        var afterThemePublic = await ReadFreshPublicAsync(browser);
        Ensure(RequiredString(afterThemePublic["storeConfig"]!.AsObject(), "storefrontTheme") == candidateTheme, "Theme did not converge publicly.");
        Ensure(JsonNode.DeepEquals(originalAppearance, config["liveTheme"]), "Theme activation changed Appearance.");
        Ensure(JsonNode.DeepEquals(originalHomepage, config["pages"]!["home"]), "Theme activation changed Homepage.");
        Ensure(JsonNode.DeepEquals(NavigationPublicContent(originalNavigationPublic), NavigationPublicContent(afterThemePublic["navigation"]!)), "Theme activation changed Navigation.");
        Ensure(JsonNode.DeepEquals(originalFooterPublic, NormalizeFooterPublic(afterThemePublic["footer"]!)), "Theme activation changed Footer.");
        crossSurface.Add(Matrix("Theme", true, "Only storefrontTheme changed; Appearance, Homepage, Navigation, Footer preserved"));

        var appearanceCandidate = AppearanceRequest(config["draftTheme"]!.AsObject());
        appearanceCandidate["colors"]!["primary"] = "#7c3aed";
        var appearanceSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/store-config/theme/overrides",
            appearanceCandidate, RequiredString(config, "rowVersion"));
        Ensure(appearanceSave.Status == 200, $"Appearance save returned {appearanceSave.Status}: {appearanceSave.Body}");
        config = ParseObject(appearanceSave.Body);
        var draftIsolationPublic = await ReadFreshPublicAsync(browser);
        Ensure(draftIsolationPublic["storeConfig"]!["theme"]!["colors"]!["primary"]!.GetValue<string>() == "#2563eb", "Appearance draft leaked publicly.");
        var appearancePublish = await BrowserRequestAsync(admin, "POST", "/api/admin/store-config/theme/publish", ifMatch: RequiredString(config, "rowVersion"));
        Ensure(appearancePublish.Status == 200, $"Appearance publish returned {appearancePublish.Status}: {appearancePublish.Body}");
        config = ParseObject(appearancePublish.Body);
        var afterAppearancePublic = await ReadFreshPublicAsync(browser);
        Ensure(RequiredString(afterAppearancePublic["storeConfig"]!.AsObject(), "storefrontTheme") == candidateTheme, "Appearance changed theme identity.");
        Ensure(afterAppearancePublic["storeConfig"]!["theme"]!["colors"]!["primary"]!.GetValue<string>() == "#7c3aed", "Published Appearance did not converge.");
        crossSurface.Add(Matrix("Colors/Typography", true, "Draft stayed private; publish changed CSS authority without changing theme identity"));

        var homepageSections = baselineConfig["pages"]!["home"]!["sections"]!.DeepClone().AsArray();
        homepageSections[0]!["settings"]!["title"] = marker;
        var staleBeforeHomepage = RequiredString(config, "rowVersion");
        var homepageSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/store-config/pages/home",
            new JsonObject { ["sections"] = homepageSections }, staleBeforeHomepage);
        Ensure(homepageSave.Status == 200, $"Homepage save returned {homepageSave.Status}: {homepageSave.Body}");
        config = ParseObject(homepageSave.Body);
        var beforeHomepagePublish = await ReadFreshPublicAsync(browser);
        Ensure(beforeHomepagePublish["storeConfig"]!["pages"]!["home"]!["sections"]![0]!["settings"]!["title"]!.GetValue<string>() != marker,
            "Homepage draft leaked publicly.");
        var homepagePublish = await BrowserRequestAsync(admin, "POST", "/api/admin/store-config/pages/home/publish", ifMatch: RequiredString(config, "rowVersion"));
        Ensure(homepagePublish.Status == 200, $"Homepage publish returned {homepagePublish.Status}: {homepagePublish.Body}");
        config = ParseObject(homepagePublish.Body);
        var afterHomepagePublic = await ReadFreshPublicAsync(browser);
        Ensure(afterHomepagePublic["storeConfig"]!["pages"]!["home"]!["sections"]![0]!["settings"]!["title"]!.GetValue<string>() == marker,
            "Homepage did not converge publicly.");
        Ensure(JsonNode.DeepEquals(NavigationPublicContent(originalNavigationPublic), NavigationPublicContent(afterHomepagePublic["navigation"]!)), "Homepage changed Navigation.");
        Ensure(JsonNode.DeepEquals(originalFooterPublic, NormalizeFooterPublic(afterHomepagePublic["footer"]!)), "Homepage changed Footer.");
        Ensure(JsonNode.DeepEquals(originalCmsList, await AdminJsonAsync(admin, "/api/admin/pages")), "Homepage changed CMS Pages.");
        crossSurface.Add(Matrix("Homepage", true, "Draft/live worked and Navigation, Footer, CMS remained unchanged"));

        var staleSharedWrite = await BrowserRequestAsync(admin, "POST", "/api/admin/store-config/theme/activate",
            new JsonObject { ["themeId"] = originalTheme, ["rowVersion"] = staleBeforeHomepage }, staleBeforeHomepage);
        Ensure(staleSharedWrite.Status == 409, $"Shared StoreConfig stale write returned {staleSharedWrite.Status} instead of 409.");
        checks.Add("Theme/Homepage shared StoreConfig RowVersion rejected stale cross-surface write with 409");

        var navigation = (await AdminJsonAsync(admin, "/api/admin/cms/navigation")).AsObject();
        var navigationRequest = BuildNavigationRequest(navigation["draft"]!.AsObject(), RequiredString(navigation, "rowVersion"));
        navigationRequest["header"]!.AsArray().Add(new JsonObject
        {
            ["label"] = marker,
            ["labelAr"] = markerAr,
            ["type"] = "cms",
            ["targetId"] = "04899853-5c84-5dab-b25a-f56bd7d6f6b2",
            ["path"] = null,
            ["openInNewTab"] = false,
            ["order"] = navigationRequest["header"]!.AsArray().Count,
            ["enabled"] = true,
        });
        var navigationTargets = (await AdminJsonAsync(admin, "/api/admin/cms/navigation/targets")).AsObject();
        var productTarget = navigationTargets["products"]?.AsArray().FirstOrDefault();
        var categoryTarget = navigationTargets["categories"]?.AsArray().FirstOrDefault();
        if (productTarget is not null)
        {
            navigationRequest["header"]!.AsArray().Add(EntityNavigationLink($"{marker} Product", "product", productTarget,
                navigationRequest["header"]!.AsArray().Count));
        }
        if (categoryTarget is not null)
        {
            navigationRequest["header"]!.AsArray().Add(EntityNavigationLink($"{marker} Category", "category", categoryTarget,
                navigationRequest["header"]!.AsArray().Count));
        }
        navigationRequest["header"]!.AsArray().Add(new JsonObject
        {
            ["label"] = $"{marker} External",
            ["type"] = "external",
            ["path"] = "https://example.com/integrated-certification",
            ["openInNewTab"] = true,
            ["order"] = navigationRequest["header"]!.AsArray().Count,
            ["enabled"] = true,
        });
        var navigationSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/cms/navigation", navigationRequest, RequiredString(navigation, "rowVersion"));
        Ensure(navigationSave.Status == 200, $"Navigation save returned {navigationSave.Status}: {navigationSave.Body}");
        navigation = ParseObject(navigationSave.Body);
        var navigationPublish = await BrowserRequestAsync(admin, "POST", "/api/admin/cms/navigation/publish", ifMatch: RequiredString(navigation, "rowVersion"));
        Ensure(navigationPublish.Status == 200, $"Navigation publish returned {navigationPublish.Status}: {navigationPublish.Body}");
        navigation = ParseObject(navigationPublish.Body);
        var afterNavigationPublic = await ReadFreshPublicAsync(browser);
        Ensure(FindPublicLink(afterNavigationPublic["navigation"]!["header"]!["links"], marker) is not null, "Navigation link did not converge.");
        if (productTarget is not null)
        {
            Ensure(HasResolvedTarget(FindPublicLink(afterNavigationPublic["navigation"]!["header"]!["links"], $"{marker} Product")),
                "Product Navigation target did not resolve canonically.");
        }
        if (categoryTarget is not null)
        {
            Ensure(HasResolvedTarget(FindPublicLink(afterNavigationPublic["navigation"]!["header"]!["links"], $"{marker} Category")),
                "Category Navigation target did not resolve canonically.");
        }
        var externalLink = FindPublicLink(afterNavigationPublic["navigation"]!["header"]!["links"], $"{marker} External");
        Ensure(externalLink?["path"]?.GetValue<string>() == "https://example.com/integrated-certification"
                && externalLink?["openInNewTab"]?.GetValue<bool>() == true,
            "External Navigation target/new-tab intent did not project correctly.");
        Ensure(JsonNode.DeepEquals(originalFooterPublic, NormalizeFooterPublic(afterNavigationPublic["footer"]!)), "Navigation changed Dedicated Footer.");
        crossSurface.Add(Matrix("Navigation", true, "Header Navigation changed; Dedicated Footer remained exact"));

        var footer = (await AdminJsonAsync(admin, "/api/admin/footer")).AsObject();
        var footerRequest = BuildFooterRequest(footer, RequiredString(footer, "revision"));
        footerRequest["sections"]![0]!["title"] = marker;
        footerRequest["sections"]![0]!["titleLocalized"]!["en"] = marker;
        var footerSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/footer", footerRequest);
        Ensure(footerSave.Status == 200, $"Footer save returned {footerSave.Status}: {footerSave.Body}");
        footer = ParseObject(footerSave.Body);
        var afterFooterPublic = await ReadFreshPublicAsync(browser);
        Ensure(afterFooterPublic["footer"]!["sections"]![0]!["title"]!.GetValue<string>() == marker, "Footer did not converge immediately.");
        Ensure(FindPublicLink(afterFooterPublic["navigation"]!["header"]!["links"], marker) is not null, "Footer write changed Header Navigation.");
        crossSurface.Add(Matrix("Dedicated Footer", true, "Immediate-live Footer changed; Header Navigation remained exact"));

        var createBody = new JsonObject
        {
            ["slug"] = cmsSlug,
            ["title"] = marker,
            ["contentType"] = "RichText",
            ["content"] = $"{marker} body",
            ["schemaVersion"] = 1,
            ["seo"] = new JsonObject { ["metaTitle"] = marker, ["metaDescription"] = $"{marker} description" },
        };
        var cmsCreate = await BrowserRequestAsync(admin, "POST", "/api/admin/pages", createBody);
        Ensure(cmsCreate.Status == 201, $"CMS create returned {cmsCreate.Status}: {cmsCreate.Body}");
        var cmsPage = ParseObject(cmsCreate.Body);
        disposablePageId = Guid.Parse(RequiredString(cmsPage, "id"));
        disposableVersion = cmsPage["draft"]!["version"]!.GetValue<int>();
        Ensure((await ReadPublicPageAsync(browser, cmsSlug))["status"]!.GetValue<int>() == 404, "CMS draft leaked publicly.");
        var cmsPublish = await BrowserRequestAsync(admin, "POST", $"/api/admin/pages/{disposablePageId}/publish", ifMatch: disposableVersion.ToString());
        Ensure(cmsPublish.Status == 200, $"CMS publish returned {cmsPublish.Status}: {cmsPublish.Body}");
        cmsPage = ParseObject(cmsPublish.Body);
        Ensure((await ReadPublicPageAsync(browser, cmsSlug))["status"]!.GetValue<int>() == 200, "Published CMS page did not converge.");
        checks.Add("Disposable CMS draft stayed private and publication converged immediately");

        navigation = (await AdminJsonAsync(admin, "/api/admin/cms/navigation")).AsObject();
        navigationRequest = BuildNavigationRequest(navigation["draft"]!.AsObject(), RequiredString(navigation, "rowVersion"));
        var navCert = navigationRequest["header"]!.AsArray().First(link => link!["label"]!.GetValue<string>() == marker)!;
        navCert["targetId"] = disposablePageId.ToString();
        navCert["openInNewTab"] = true;
        navigationSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/cms/navigation", navigationRequest, RequiredString(navigation, "rowVersion"));
        Ensure(navigationSave.Status == 200, $"CMS-dependent Navigation save returned {navigationSave.Status}: {navigationSave.Body}");
        navigation = ParseObject(navigationSave.Body);
        navigationPublish = await BrowserRequestAsync(admin, "POST", "/api/admin/cms/navigation/publish", ifMatch: RequiredString(navigation, "rowVersion"));
        Ensure(navigationPublish.Status == 200, "CMS-dependent Navigation publish failed.");

        footer = (await AdminJsonAsync(admin, "/api/admin/footer")).AsObject();
        footerRequest = BuildFooterRequest(footer, RequiredString(footer, "revision"));
        footerRequest["sections"]![0]!["links"]!.AsArray().Add(new JsonObject
        {
            ["label"] = marker,
            ["labelLocalized"] = new JsonObject { ["en"] = marker, ["ar"] = markerAr },
            ["type"] = "Cms",
            ["cmsPageSlug"] = cmsSlug,
            ["openInNewTab"] = true,
            ["order"] = footerRequest["sections"]![0]!["links"]!.AsArray().Count,
            ["isActive"] = true,
        });
        footerSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/footer", footerRequest);
        Ensure(footerSave.Status == 200, $"CMS-dependent Footer save returned {footerSave.Status}: {footerSave.Body}");
        var dependentPublic = await ReadFreshPublicAsync(browser);
        result["cmsDependencyPublic"] = dependentPublic.DeepClone();
        var navDependent = FindPublicLink(dependentPublic["navigation"]!["header"]!["links"], marker);
        var footerDependent = FindFooterLink(dependentPublic["footer"]!, marker);
        Ensure(navDependent?["slug"]?.GetValue<string>() == cmsSlug || navDependent?["path"]?.GetValue<string>() == $"/{cmsSlug}",
            "Navigation CMS dependency did not resolve canonically.");
        Ensure(footerDependent?["url"]?.GetValue<string>() == $"/{cmsSlug}" && footerDependent?["openInNewTab"]?.GetValue<bool>() == true,
            "Footer CMS dependency did not resolve with new-tab intent.");
        checks.Add("Published CMS page resolved from Navigation and Dedicated Footer with new-tab intent");

        var updateBody = createBody.DeepClone().AsObject();
        updateBody["slug"] = renamedSlug;
        updateBody["title"] = $"{marker} renamed";
        updateBody["expectedVersion"] = disposableVersion;
        var cmsUpdate = await BrowserRequestAsync(admin, "PUT", $"/api/admin/pages/{disposablePageId}", updateBody);
        Ensure(cmsUpdate.Status == 200, $"CMS rename draft returned {cmsUpdate.Status}: {cmsUpdate.Body}");
        cmsPage = ParseObject(cmsUpdate.Body);
        disposableVersion = cmsPage["draft"]!["version"]!.GetValue<int>();
        Ensure((await ReadPublicPageAsync(browser, cmsSlug))["status"]!.GetValue<int>() == 200, "CMS rename draft replaced live page.");
        Ensure((await ReadPublicPageAsync(browser, renamedSlug))["status"]!.GetValue<int>() == 404, "CMS rename draft leaked.");
        cmsPublish = await BrowserRequestAsync(admin, "POST", $"/api/admin/pages/{disposablePageId}/publish", ifMatch: disposableVersion.ToString());
        Ensure(cmsPublish.Status == 200, $"CMS rename publish returned {cmsPublish.Status}: {cmsPublish.Body}");
        var renamedPublic = await ReadFreshPublicAsync(browser);
        var navRenamed = FindPublicLink(renamedPublic["navigation"]!["header"]!["links"], marker);
        var footerRenamed = FindFooterLink(renamedPublic["footer"]!, marker);
        var navigationFollowedRename = navRenamed?["slug"]?.GetValue<string>() == renamedSlug;
        var footerFollowedRename = footerRenamed?["url"]?.GetValue<string>() == $"/{renamedSlug}";
        crossSurface.Add(new JsonObject
        {
            ["surface"] = "CMS canonical rename",
            ["navigationFollowedCurrentPageIdentity"] = navigationFollowedRename,
            ["footerFollowedCurrentPageIdentity"] = footerFollowedRename,
            ["footerFailedClosed"] = footerRenamed is null,
        });
        Ensure(navigationFollowedRename, "Navigation did not canonically recompose the renamed CMS target.");
        Ensure(footerFollowedRename, "Dedicated Footer did not canonically recompose the renamed CMS target.");
        checks.Add("Navigation and Dedicated Footer followed the current slug through stable CMS PageGroupId authority");

        await RunSessionStabilityAsync(adminContext, disposablePageId.Value, marker, checks);
        await RunThemeMatrixAsync(admin, browser, root, marker, themeMatrix, screenshots);
        await RunCacheAndTenantChecksAsync(admin, browser, baseline, marker, checks);

        var staleFooterRequest = BuildFooterRequest(baselineFooter, RequiredString(baselineFooter, "revision"));
        var staleFooter = await BrowserRequestAsync(admin, "PUT", "/api/admin/footer", staleFooterRequest);
        Ensure(staleFooter.Status == 409, $"Stale Footer revision returned {staleFooter.Status} instead of 409.");
        var staleCmsBody = updateBody.DeepClone().AsObject();
        staleCmsBody["expectedVersion"] = 1;
        var staleCms = await BrowserRequestAsync(admin, "PUT", $"/api/admin/pages/{disposablePageId}", staleCmsBody);
        Ensure(staleCms.Status == 409, $"Stale CMS version returned {staleCms.Status} instead of 409.");
        checks.Add("StoreConfig, Footer, and CMS stale revisions all failed closed; independent Footer/CMS writes survived StoreConfig mutations");

        var deleteCms = await BrowserRequestAsync(admin, "DELETE", $"/api/admin/pages/{disposablePageId}", ifMatch: disposableVersion.ToString());
        Ensure(deleteCms.Status == 204, $"CMS unpublication/delete returned {deleteCms.Status}: {deleteCms.Body}");
        var afterUnpublish = await ReadFreshPublicAsync(browser);
        Ensure(FindPublicLink(afterUnpublish["navigation"]!["header"]!["links"], marker) is null,
            "Navigation CMS link did not fail closed after CMS deletion.");
        Ensure(FindFooterLink(afterUnpublish["footer"]!, marker) is null,
            "Footer CMS link did not fail closed after CMS deletion.");
        Ensure((await ReadPublicPageAsync(browser, renamedSlug))["status"]!.GetValue<int>() == 404,
            "Deleted CMS page remained public.");
        checks.Add("CMS deletion unpublished the page and Navigation/Footer CMS references failed closed; this CMS contract has no republish-after-delete operation");

        await admin.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(screenshots, "admin-integrated-final-state.png"), FullPage = true });
    }
    catch (Exception exception)
    {
        result["executionFailure"] = exception.ToString();
        blockers.Add(exception.Message);
    }
    finally
    {
        try
        {
            if (mutationsStarted)
            {
                await RestoreThroughApplicationAsync(admin, baselineConfig, baselineNavigation, baselineFooter);
                if (disposablePageId.HasValue)
                {
                    var currentPage = await BrowserRequestAsync(admin, "GET", $"/api/admin/pages/{disposablePageId}");
                    if (currentPage.Status == 200)
                    {
                        var page = ParseObject(currentPage.Body);
                        var currentVersion = Math.Max(
                            page["draft"]?["version"]?.GetValue<int>() ?? 0,
                            page["published"]?["version"]?.GetValue<int>() ?? 0);
                        var delete = await BrowserRequestAsync(admin, "DELETE", $"/api/admin/pages/{disposablePageId}", ifMatch: currentVersion.ToString());
                        Ensure(delete.Status == 204, $"Disposable CMS cleanup returned {delete.Status}: {delete.Body}");
                    }
                }
            }

            await AssertCurrentContentMatchesBaselineAsync(admin, browser, baseline);
            var finalDatabaseA = await CaptureDatabaseAuthorityAsync(connectionString, TenantId);
            var finalDatabaseB = await CaptureDatabaseAuthorityAsync(connectionString, IsolationTenantId);
            var baselineDatabaseB = baseline["database"]!["tenantB"]!;
            Ensure(JsonNode.DeepEquals(finalDatabaseB, baselineDatabaseB), "Isolation tenant database fingerprint changed.");
            var baselineProducts = baseline["database"]!["tenantA"]!["products"]!["sha256"]!.GetValue<string>();
            var baselineCategories = baseline["database"]!["tenantA"]!["categories"]!["sha256"]!.GetValue<string>();
            Ensure(finalDatabaseA["products"]!["sha256"]!.GetValue<string>() == baselineProducts, "Tenant A products changed.");
            Ensure(finalDatabaseA["categories"]!["sha256"]!.GetValue<string>() == baselineCategories, "Tenant A categories changed.");
            Ensure(Sha256(await File.ReadAllBytesAsync(Path.Combine(evidenceDirectory, "immutable-baseline.json"))) == baselineHash,
                "Immutable baseline hash changed.");
            restored = true;
            result["restoration"] = "PASS_EXACT_CONTENT_AUTHORITY";
        }
        catch (Exception restoreException)
        {
            result["restoration"] = "FAIL";
            result["restorationFailure"] = restoreException.ToString();
            blockers.Add($"Restoration: {restoreException.Message}");
        }
    }

    result["completedAtUtc"] = DateTime.UtcNow;
    result["restored"] = restored;
    result["status"] = blockers.Count == 0 && restored ? "PASS" : "FAIL";
    await File.WriteAllTextAsync(resultPath, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(blockers.Count == 0 && restored
        ? "FINAL_INTEGRATED_BROWSER_CERTIFICATION PASS"
        : "FINAL_INTEGRATED_BROWSER_CERTIFICATION FAIL");
}

static async Task<JsonObject> CaptureBaselineAsync(IPage admin, IBrowser browser, string connectionString)
{
    var adminConfig = await AdminJsonAsync(admin, "/api/admin/store-config");
    var cmsPages = await AdminJsonAsync(admin, "/api/admin/pages");
    var navigation = await AdminJsonAsync(admin, "/api/admin/cms/navigation");
    var footer = await AdminJsonAsync(admin, "/api/admin/footer");

    var publicA = await CapturePublicAsync(browser, StorefrontUrl);
    var publicB = await CapturePublicAsync(browser, IsolationStorefrontUrl);
    var database = new JsonObject
    {
        ["tenantA"] = await CaptureDatabaseAuthorityAsync(connectionString, TenantId),
        ["tenantB"] = await CaptureDatabaseAuthorityAsync(connectionString, IsolationTenantId),
    };

    return new JsonObject
    {
        ["capturedAtUtc"] = DateTime.UtcNow,
        ["tenant"] = new JsonObject
        {
            ["id"] = TenantId,
            ["host"] = "ahramstore.localhost",
            ["adminStoreConfigId"] = adminConfig["id"]?.DeepClone(),
        },
        ["isolationTenant"] = new JsonObject
        {
            ["id"] = IsolationTenantId,
            ["host"] = "shopixy.localhost",
        },
        ["admin"] = new JsonObject
        {
            ["storeConfig"] = adminConfig,
            ["cmsPages"] = cmsPages,
            ["navigation"] = navigation,
            ["footer"] = footer,
        },
        ["publicTenantA"] = publicA,
        ["publicTenantB"] = publicB,
        ["database"] = database,
        ["protectedFingerprints"] = new JsonObject
        {
            ["tenantAStoreConfig"] = database["tenantA"]?["storeConfig"]?["sha256"]?.DeepClone(),
            ["tenantACms"] = database["tenantA"]?["cmsPages"]?["sha256"]?.DeepClone(),
            ["tenantAProducts"] = database["tenantA"]?["products"]?["sha256"]?.DeepClone(),
            ["tenantACategories"] = database["tenantA"]?["categories"]?["sha256"]?.DeepClone(),
            ["tenantAFooter"] = database["tenantA"]?["footer"]?["sha256"]?.DeepClone(),
            ["tenantBAll"] = Sha256(Encoding.UTF8.GetBytes(database["tenantB"]!.ToJsonString())),
            ["publicTenantA"] = Sha256(Encoding.UTF8.GetBytes(publicA.ToJsonString())),
            ["publicTenantB"] = Sha256(Encoding.UTF8.GetBytes(publicB.ToJsonString())),
        },
    };
}

static JsonObject Matrix(string surface, bool passed, string evidence) => new()
{
    ["surface"] = surface,
    ["passed"] = passed,
    ["evidence"] = evidence,
};

static JsonObject EntityNavigationLink(string label, string type, JsonNode target, int order) => new()
{
    ["label"] = label,
    ["labelAr"] = null,
    ["type"] = type,
    ["targetId"] = target["targetId"]?.DeepClone(),
    ["path"] = null,
    ["openInNewTab"] = false,
    ["order"] = order,
    ["enabled"] = true,
};

static JsonObject AppearanceRequest(JsonObject theme) => new()
{
    ["presetKey"] = theme["presetKey"]?.DeepClone() ?? "default",
    ["colors"] = theme["colors"]?.DeepClone(),
    ["typography"] = new JsonObject
    {
        ["fontFamily"] = theme["font"]?.DeepClone(),
        ["baseSize"] = theme["baseSize"]?.DeepClone(),
    },
    ["borderRadius"] = theme["borderRadius"]?.DeepClone(),
};

static async Task AssertCurrentContentMatchesBaselineAsync(IPage admin, IBrowser browser, JsonObject baseline)
{
    var expectedAdmin = baseline["admin"]!.AsObject();
    var currentConfig = (await AdminJsonAsync(admin, "/api/admin/store-config")).AsObject();
    var expectedConfig = expectedAdmin["storeConfig"]!.AsObject();
    Ensure(JsonNode.DeepEquals(StoreConfigContent(expectedConfig), StoreConfigContent(currentConfig)), "StoreConfig content does not match baseline.");

    var currentNavigation = (await AdminJsonAsync(admin, "/api/admin/cms/navigation")).AsObject();
    var expectedNavigation = expectedAdmin["navigation"]!.AsObject();
    Ensure(JsonNode.DeepEquals(currentNavigation["draft"], expectedNavigation["draft"]), "Navigation draft does not match baseline.");
    Ensure(JsonNode.DeepEquals(currentNavigation["live"], expectedNavigation["live"]), "Navigation live does not match baseline.");
    Ensure(currentNavigation["hasPendingChanges"]?.GetValue<bool>() == false, "Navigation has pending changes.");

    var currentFooter = (await AdminJsonAsync(admin, "/api/admin/footer")).AsObject();
    Ensure(JsonNode.DeepEquals(FooterContent(currentFooter), FooterContent(expectedAdmin["footer"]!.AsObject())), "Footer content does not match baseline.");
    Ensure(JsonNode.DeepEquals(await AdminJsonAsync(admin, "/api/admin/pages"), expectedAdmin["cmsPages"]), "CMS Page list does not match baseline.");

    var publicNow = await ReadFreshPublicAsync(browser);
    var expectedPublic = baseline["publicTenantA"]!.AsObject();
    Ensure(JsonNode.DeepEquals(PublicConfigContent(publicNow["storeConfig"]!), PublicConfigContent(expectedPublic["storeConfig"]!)), "Public StoreConfig does not match baseline.");
    Ensure(JsonNode.DeepEquals(NavigationPublicContent(publicNow["navigation"]!), NavigationPublicContent(expectedPublic["navigationEn"]!)), "Public Navigation does not match baseline.");
    Ensure(JsonNode.DeepEquals(NormalizeFooterPublic(publicNow["footer"]!), NormalizeFooterPublic(expectedPublic["footerEn"]!)), "Public Footer does not match baseline.");
}

static JsonObject StoreConfigContent(JsonObject config) => new()
{
    ["id"] = config["id"]?.DeepClone(),
    ["tenantId"] = config["tenantId"]?.DeepClone(),
    ["storeName"] = config["storeName"]?.DeepClone(),
    ["storeDescription"] = config["storeDescription"]?.DeepClone(),
    ["storefrontTheme"] = config["storefrontTheme"]?.DeepClone(),
    ["themeId"] = config["themeId"]?.DeepClone(),
    ["theme"] = config["theme"]?.DeepClone(),
    ["behavior"] = config["behavior"]?.DeepClone(),
    ["commerceAi"] = config["commerceAi"]?.DeepClone(),
    ["storefrontStatus"] = config["storefrontStatus"]?.DeepClone(),
    ["maintenanceMessage"] = config["maintenanceMessage"]?.DeepClone(),
    ["pages"] = config["pages"]?.DeepClone(),
    ["productTemplates"] = config["productTemplates"]?.DeepClone(),
    ["themePresetKey"] = config["themePresetKey"]?.DeepClone(),
    ["themeLivePresetKey"] = config["themeLivePresetKey"]?.DeepClone(),
    ["themeDraftOverridesJson"] = config["themeDraftOverridesJson"]?.DeepClone(),
    ["themeLiveOverridesJson"] = config["themeLiveOverridesJson"]?.DeepClone(),
    ["draftTheme"] = config["draftTheme"]?.DeepClone(),
    ["liveTheme"] = config["liveTheme"]?.DeepClone(),
};

static JsonObject PublicConfigContent(JsonNode config) => new()
{
    ["tenantId"] = config["tenantId"]?.DeepClone(),
    ["storeName"] = config["storeName"]?.DeepClone(),
    ["storefrontTheme"] = config["storefrontTheme"]?.DeepClone(),
    ["themeId"] = config["themeId"]?.DeepClone(),
    ["theme"] = config["theme"]?.DeepClone(),
    ["behavior"] = config["behavior"]?.DeepClone(),
    ["storefrontStatus"] = config["storefrontStatus"]?.DeepClone(),
    ["pages"] = config["pages"]?.DeepClone(),
    ["productTemplates"] = config["productTemplates"]?.DeepClone(),
    ["currency"] = config["currency"]?.DeepClone(),
    ["defaultCountryCode"] = config["defaultCountryCode"]?.DeepClone(),
};

static JsonObject NavigationPublicContent(JsonNode navigation) => new()
{
    ["header"] = navigation["header"]?.DeepClone(),
    ["footer"] = navigation["footer"]?.DeepClone(),
};

static JsonObject NormalizeFooterPublic(JsonNode footer) => new()
{
    ["footerSource"] = footer["footerSource"]?.DeepClone(),
    ["socialSource"] = footer["socialSource"]?.DeepClone(),
    ["sections"] = footer["sections"]?.DeepClone(),
    ["socialLinks"] = footer["socialLinks"]?.DeepClone(),
};

static JsonObject FooterContent(JsonObject footer) => new()
{
    ["scope"] = footer["scope"]?.DeepClone(),
    ["sections"] = StripIds(footer["sections"]),
    ["socialLinks"] = StripIds(footer["socialLinks"]),
};

static JsonNode? StripIds(JsonNode? node)
{
    if (node is JsonArray array)
    {
        return new JsonArray(array.Select(StripIds).ToArray());
    }
    if (node is JsonObject value)
    {
        var result = new JsonObject();
        foreach (var property in value)
        {
            if (property.Key is "id" or "updatedAtUtc" or "createdAtUtc") continue;
            result[property.Key] = StripIds(property.Value);
        }
        return result;
    }
    return node?.DeepClone();
}

static JsonObject BuildNavigationRequest(JsonObject snapshot, string rowVersion)
{
    JsonArray Links(JsonNode? source)
    {
        var links = new JsonArray();
        foreach (var item in source?.AsArray() ?? [])
        {
            if (item is null) continue;
            links.Add(new JsonObject
            {
                ["label"] = item["label"]?.DeepClone(),
                ["labelAr"] = item["labelAr"]?.DeepClone(),
                ["type"] = item["type"]?.DeepClone(),
                ["targetId"] = item["targetId"]?.DeepClone(),
                ["collectionKey"] = item["collectionKey"]?.DeepClone(),
                ["path"] = item["path"]?.DeepClone(),
                ["openInNewTab"] = item["openInNewTab"]?.DeepClone() ?? false,
                ["order"] = links.Count,
                ["enabled"] = item["enabled"]?.DeepClone() ?? true,
            });
        }
        return links;
    }
    return new JsonObject
    {
        ["rowVersion"] = rowVersion,
        ["header"] = Links(snapshot["header"]),
        ["footer"] = Links(snapshot["footer"]),
    };
}

static JsonObject BuildFooterRequest(JsonObject footer, string revision)
{
    var sections = new JsonArray();
    foreach (var sectionNode in footer["sections"]?.AsArray() ?? [])
    {
        if (sectionNode is null) continue;
        var links = new JsonArray();
        foreach (var link in sectionNode["links"]?.AsArray() ?? [])
        {
            if (link is null) continue;
            links.Add(new JsonObject
            {
                ["label"] = link["label"]?.DeepClone(),
                ["labelLocalized"] = link["labelLocalized"]?.DeepClone(),
                ["type"] = link["type"]?.DeepClone(),
                ["url"] = link["url"]?.DeepClone(),
                ["cmsPageSlug"] = link["cmsPageSlug"]?.DeepClone(),
                ["openInNewTab"] = link["openInNewTab"]?.DeepClone() ?? false,
                ["order"] = link["order"]?.DeepClone(),
                ["isActive"] = link["isActive"]?.DeepClone() ?? true,
            });
        }
        sections.Add(new JsonObject
        {
            ["title"] = sectionNode["title"]?.DeepClone(),
            ["titleLocalized"] = sectionNode["titleLocalized"]?.DeepClone(),
            ["order"] = sectionNode["order"]?.DeepClone(),
            ["isActive"] = sectionNode["isActive"]?.DeepClone() ?? true,
            ["links"] = links,
        });
    }
    var social = new JsonArray();
    foreach (var link in footer["socialLinks"]?.AsArray() ?? [])
    {
        if (link is null) continue;
        social.Add(new JsonObject
        {
            ["platform"] = link["platform"]?.DeepClone(),
            ["url"] = link["url"]?.DeepClone(),
            ["icon"] = link["icon"]?.DeepClone(),
            ["order"] = link["order"]?.DeepClone(),
            ["isActive"] = link["isActive"]?.DeepClone() ?? true,
        });
    }
    return new JsonObject { ["expectedRevision"] = revision, ["sections"] = sections, ["socialLinks"] = social };
}

static JsonNode? FindPublicLink(JsonNode? links, string label) => links?.AsArray().FirstOrDefault(link =>
    string.Equals(link?["label"]?.GetValue<string>(), label, StringComparison.Ordinal));

static bool HasResolvedTarget(JsonNode? link) => link is not null &&
    (!string.IsNullOrWhiteSpace(link["path"]?.GetValue<string>()) || !string.IsNullOrWhiteSpace(link["slug"]?.GetValue<string>()));

static JsonNode? FindFooterLink(JsonNode footer, string label)
{
    foreach (var section in footer["sections"]?.AsArray() ?? [])
    {
        var link = section?["links"]?.AsArray().FirstOrDefault(item =>
            string.Equals(item?["label"]?.GetValue<string>(), label, StringComparison.Ordinal));
        if (link is not null) return link;
    }
    return null;
}

static async Task<JsonObject> ReadFreshPublicAsync(IBrowser browser)
{
    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
    });
    var page = await context.NewPageAsync();
    await page.GotoAsync(StorefrontUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
    return new JsonObject
    {
        ["storeConfig"] = await PublicJsonAsync(page, "/api/storefront/store-config"),
        ["navigation"] = await PublicJsonAsync(page, "/api/storefront/navigation?lang=en"),
        ["footer"] = await PublicJsonAsync(page, "/api/storefront/footer?lang=en"),
    };
}

static async Task<JsonObject> ReadPublicPageAsync(IBrowser browser, string slug)
{
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    await page.GotoAsync(StorefrontUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
    return await PublicResultAsync(page, $"/api/storefront/pages/{slug}");
}

static async Task RestoreThroughApplicationAsync(IPage admin, JsonObject baselineConfig, JsonObject baselineNavigation, JsonObject baselineFooter)
{
    var navigation = (await AdminJsonAsync(admin, "/api/admin/cms/navigation")).AsObject();
    var navigationRestore = BuildNavigationRequest(baselineNavigation["draft"]!.AsObject(), RequiredString(navigation, "rowVersion"));
    var navigationSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/cms/navigation", navigationRestore, RequiredString(navigation, "rowVersion"));
    Ensure(navigationSave.Status == 200, $"Navigation restoration save returned {navigationSave.Status}: {navigationSave.Body}");
    navigation = ParseObject(navigationSave.Body);
    var navigationPublish = await BrowserRequestAsync(admin, "POST", "/api/admin/cms/navigation/publish", ifMatch: RequiredString(navigation, "rowVersion"));
    Ensure(navigationPublish.Status == 200, $"Navigation restoration publish returned {navigationPublish.Status}: {navigationPublish.Body}");

    var footer = (await AdminJsonAsync(admin, "/api/admin/footer")).AsObject();
    var footerRestore = BuildFooterRequest(baselineFooter, RequiredString(footer, "revision"));
    var footerSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/footer", footerRestore);
    Ensure(footerSave.Status == 200, $"Footer restoration returned {footerSave.Status}: {footerSave.Body}");

    var config = (await AdminJsonAsync(admin, "/api/admin/store-config")).AsObject();
    var homepageRestore = new JsonObject { ["sections"] = baselineConfig["pages"]!["home"]!["sections"]!.DeepClone() };
    var homepageSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/store-config/pages/home", homepageRestore, RequiredString(config, "rowVersion"));
    Ensure(homepageSave.Status == 200, $"Homepage restoration save returned {homepageSave.Status}: {homepageSave.Body}");
    config = ParseObject(homepageSave.Body);
    var homepagePublish = await BrowserRequestAsync(admin, "POST", "/api/admin/store-config/pages/home/publish", ifMatch: RequiredString(config, "rowVersion"));
    Ensure(homepagePublish.Status == 200, $"Homepage restoration publish returned {homepagePublish.Status}: {homepagePublish.Body}");
    config = ParseObject(homepagePublish.Body);

    var appearanceRestore = JsonNode.Parse(RequiredString(baselineConfig, "themeDraftOverridesJson"))?.AsObject()
        ?? throw new InvalidOperationException("Baseline draft Appearance JSON is invalid.");
    var appearanceSave = await BrowserRequestAsync(admin, "PUT", "/api/admin/store-config/theme/overrides", appearanceRestore, RequiredString(config, "rowVersion"));
    Ensure(appearanceSave.Status == 200, $"Appearance restoration save returned {appearanceSave.Status}: {appearanceSave.Body}");
    config = ParseObject(appearanceSave.Body);
    var appearancePublish = await BrowserRequestAsync(admin, "POST", "/api/admin/store-config/theme/publish", ifMatch: RequiredString(config, "rowVersion"));
    Ensure(appearancePublish.Status == 200, $"Appearance restoration publish returned {appearancePublish.Status}: {appearancePublish.Body}");
    config = ParseObject(appearancePublish.Body);

    var themeRestore = await BrowserRequestAsync(admin, "POST", "/api/admin/store-config/theme/activate",
        new JsonObject { ["themeId"] = baselineConfig["storefrontTheme"]!.DeepClone(), ["rowVersion"] = RequiredString(config, "rowVersion") },
        RequiredString(config, "rowVersion"));
    Ensure(themeRestore.Status == 200, $"Theme restoration returned {themeRestore.Status}: {themeRestore.Body}");
}

static async Task RunCacheAndTenantChecksAsync(IPage admin, IBrowser browser, JsonObject baseline, string marker, JsonArray checks)
{
    var first = await ReadFreshPublicAsync(browser);
    var second = await ReadFreshPublicAsync(browser);
    Ensure(JsonNode.DeepEquals(PublicConfigContent(first["storeConfig"]!), PublicConfigContent(second["storeConfig"]!)), "Fresh public contexts disagreed on StoreConfig.");
    Ensure(JsonNode.DeepEquals(NavigationPublicContent(first["navigation"]!), NavigationPublicContent(second["navigation"]!)), "Fresh public contexts disagreed on Navigation.");
    Ensure(JsonNode.DeepEquals(NormalizeFooterPublic(first["footer"]!), NormalizeFooterPublic(second["footer"]!)), "Fresh public contexts disagreed on Footer.");

    var secondAdmin = (await AdminJsonAsync(admin, "/api/admin/store-config")).AsObject();
    Ensure(secondAdmin["pages"]!["home"]!["sections"]![0]!["settings"]!["title"]!.GetValue<string>() == marker,
        "Second Admin read did not converge on Homepage.");
    var forged = await BrowserRequestAsync(admin, "GET", "/api/admin/store-config", extraHeaders:
        new Dictionary<string, string> { ["X-Tenant-Id"] = IsolationTenantId });
    Ensure(forged.Status == 403, $"Forged tenant header returned {forged.Status} instead of 403.");

    await using var isolationContext = await browser.NewContextAsync();
    var isolationPage = await isolationContext.NewPageAsync();
    await isolationPage.GotoAsync(IsolationStorefrontUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
    var isolationConfig = await PublicJsonAsync(isolationPage, "/api/storefront/store-config");
    Ensure(isolationConfig["tenantId"]!.GetValue<string>() == IsolationTenantId, "Isolation host returned wrong tenant.");
    Ensure(!isolationConfig.ToJsonString().Contains(marker, StringComparison.Ordinal), "Tenant A marker leaked to tenant B StoreConfig.");
    var isolationNavigation = await PublicJsonAsync(isolationPage, "/api/storefront/navigation?lang=en");
    var isolationFooter = await PublicJsonAsync(isolationPage, "/api/storefront/footer?lang=en");
    Ensure(!isolationNavigation.ToJsonString().Contains(marker, StringComparison.Ordinal), "Tenant A Navigation leaked to tenant B.");
    Ensure(!isolationFooter.ToJsonString().Contains(marker, StringComparison.Ordinal), "Tenant A Footer leaked to tenant B.");
    checks.Add("Two fresh public contexts and a second Admin read converged; forged header failed 403; trusted isolation host remained tenant B");
}

static async Task RunSessionStabilityAsync(IBrowserContext context, Guid pageId, string marker, JsonArray checks)
{
    var homepage = await context.NewPageAsync();
    var cms = await context.NewPageAsync();
    var navigation = await context.NewPageAsync();
    var appearance = await context.NewPageAsync();
    try
    {
        await homepage.GotoAsync($"{AdminUrl}/storefront/homepage", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await homepage.GetByTitle("Edit").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await homepage.GetByTitle("Edit").First.ClickAsync();
        var homepageInput = homepage.Locator("label").Filter(new LocatorFilterOptions { HasTextString = "Title" }).First.Locator("xpath=following-sibling::input");
        await homepageInput.WaitForAsync();
        var homepageUnsaved = $"{marker} unsaved homepage";
        await homepageInput.FillAsync(homepageUnsaved);

        await cms.GotoAsync($"{AdminUrl}/storefront/content/pages/{pageId}", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await cms.GetByLabel("Title", new() { Exact = true }).WaitForAsync();
        var cmsUnsaved = $"{marker} unsaved CMS";
        await cms.GetByLabel("Title", new() { Exact = true }).FillAsync(cmsUnsaved);

        await navigation.GotoAsync($"{AdminUrl}/storefront/navigation?view=header", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await navigation.GetByText(marker, new() { Exact = true }).First.WaitForAsync();
        var row = navigation.GetByText(marker, new() { Exact = true }).First.Locator("xpath=ancestor::div[.//button[@aria-label='Edit navigation link']][1]");
        await row.GetByRole(AriaRole.Button, new() { Name = "Edit navigation link" }).ClickAsync();
        var navigationUnsaved = $"{marker} unsaved nav";
        await navigation.GetByLabel("Link label").FillAsync(navigationUnsaved);

        await appearance.GotoAsync($"{AdminUrl}/storefront/appearance/colors-typography", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        var appearanceInput = appearance.Locator("#appearance-primary");
        const string appearanceUnsaved = "#345678";
        await appearanceInput.WaitForAsync();
        await appearanceInput.FillAsync(appearanceUnsaved);
        await appearance.GetByText("Unsaved changes", new() { Exact = true }).WaitForAsync();
        var appearanceAfterFill = await appearanceInput.InputValueAsync();
        Ensure(appearanceAfterFill == appearanceUnsaved,
            $"Appearance edit was not admitted immediately after FillAsync (actual '{appearanceAfterFill}').");

        foreach (var page in new[] { homepage, cms, navigation, appearance })
        {
            await page.BringToFrontAsync();
            await page.EvaluateAsync("""
                () => {
                    window.dispatchEvent(new Event('blur'));
                    document.dispatchEvent(new Event('visibilitychange'));
                    window.dispatchEvent(new Event('focus'));
                    document.dispatchEvent(new ClipboardEvent('copy'));
                    document.dispatchEvent(new ClipboardEvent('paste'));
                }
                """);
            await page.WaitForTimeoutAsync(250);
            var body = await page.Locator("body").InnerTextAsync();
            Ensure(!body.Contains("Restoring session", StringComparison.OrdinalIgnoreCase), $"{page.Url} displayed destructive Restoring session UI.");
        }

        var appearanceAfterLifecycle = await appearanceInput.InputValueAsync();

        await appearance.RouteAsync("**/api/auth/session", route => route.AbortAsync("failed"));
        await appearance.EvaluateAsync("() => { window.dispatchEvent(new Event('blur')); window.dispatchEvent(new Event('focus')); document.dispatchEvent(new Event('visibilitychange')); }");
        await appearance.WaitForTimeoutAsync(500);
        await appearance.UnrouteAsync("**/api/auth/session");
        var appearanceAfterFailedRevalidation = await appearanceInput.InputValueAsync();

        Ensure(await homepageInput.InputValueAsync() == homepageUnsaved, "Homepage unsaved edit was lost.");
        Ensure(await cms.GetByLabel("Title", new() { Exact = true }).InputValueAsync() == cmsUnsaved, "CMS unsaved edit was lost.");
        Ensure(await navigation.GetByLabel("Link label").InputValueAsync() == navigationUnsaved, "Navigation unsaved edit was lost.");
        Ensure(appearanceAfterFailedRevalidation == appearanceUnsaved,
            $"Appearance unsaved edit was lost: afterFill='{appearanceAfterFill}', afterLifecycle='{appearanceAfterLifecycle}', afterFailedRevalidation='{appearanceAfterFailedRevalidation}'.");

        foreach (var page in new[] { homepage, cms, navigation, appearance })
        {
            var unsafeStorage = await page.EvaluateAsync<bool>("""
                marker => [...Object.entries(localStorage), ...Object.entries(sessionStorage)]
                    .some(([key, value]) => key.toLowerCase().includes('draft') || String(value).includes(marker))
                """, marker);
            Ensure(!unsafeStorage, $"{page.Url} persisted draft data in browser storage.");
        }
        checks.Add("Homepage, CMS, Navigation, and Appearance retained unsaved edits across tab/focus/visibility/copy/paste/network-revalidation events without Restoring session UI or unsafe draft storage");
    }
    finally
    {
        await homepage.CloseAsync(new PageCloseOptions { RunBeforeUnload = false });
        await cms.CloseAsync(new PageCloseOptions { RunBeforeUnload = false });
        await navigation.CloseAsync(new PageCloseOptions { RunBeforeUnload = false });
        await appearance.CloseAsync(new PageCloseOptions { RunBeforeUnload = false });
    }
}

static async Task RunThemeMatrixAsync(IPage admin, IBrowser browser, string root, string marker, JsonArray matrix, string screenshots)
{
    using var authority = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "theme-authority", "shopixy-theme-authority.json")));
    var themes = authority.RootElement.GetProperty("themes").EnumerateArray()
        .Where(theme => theme.GetProperty("officialStatus").GetString() == "official"
            && theme.GetProperty("activationStatus").GetString() == "active"
            && theme.GetProperty("backendAvailability").GetBoolean()
            && theme.GetProperty("runtimeAvailability").GetBoolean())
        .Select(theme => theme.GetProperty("id").GetString()!)
        .ToArray();
    Ensure(themes.SequenceEqual(new[] { "modern", "minimal", "fashion", "pearl", "phoenix", "hype", "alizon", "sharp", "cosmetics", "seven" }),
        $"Active theme authority mismatch: {string.Join(',', themes)}");

    foreach (var theme in themes)
    {
        var previewResponse = await BrowserRequestAsync(admin, "POST", $"/api/admin/store-config/theme/preview-link/{theme}");
        Ensure(previewResponse.Status == 200, $"Preview link for {theme} returned {previewResponse.Status}: {previewResponse.Body}");
        var previewUrl = RequiredString(ParseObject(previewResponse.Body), "previewUrl");
        foreach (var scenario in new[]
        {
            new { Name = "en-desktop", Lang = "en", Locale = "en-US", Width = 1440, Height = 1000, Dir = "ltr" },
            new { Name = "ar-mobile", Lang = "ar", Locale = "ar-EG", Width = 390, Height = 844, Dir = "rtl" },
        })
        {
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = scenario.Locale,
                ViewportSize = new ViewportSize { Width = scenario.Width, Height = scenario.Height },
            });
            var page = await context.NewPageAsync();
            var consoleErrors = new List<string>();
            var failedRequests = new List<string>();
            var httpErrors = new List<string>();
            page.Console += (_, message) => { if (message.Type == "error") consoleErrors.Add(message.Text); };
            page.RequestFailed += (_, request) => failedRequests.Add($"{request.Method} {request.Url}: {request.Failure}");
            page.Response += (_, response) =>
            {
                if (response.Status >= 400 && !response.Url.Contains("/api/storefront/auth/", StringComparison.OrdinalIgnoreCase))
                    httpErrors.Add($"{response.Status} {response.Url}");
            };
            var separator = previewUrl.Contains('?') ? "&" : "?";
            var document = await page.GotoAsync($"{previewUrl}{separator}lang={scenario.Lang}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 120_000,
            });
            Ensure(document?.Status == 200, $"{theme}/{scenario.Name} document returned {document?.Status}.");
            await page.Locator("header").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
            await page.Locator("footer").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
            await page.Locator($"[data-product-card-theme='{theme}']").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
            await page.WaitForFunctionAsync("expected => document.documentElement.dir === expected", scenario.Dir);
            var metricsJson = await page.EvaluateAsync<string>("""
                () => JSON.stringify({
                    lang: document.documentElement.lang,
                    dir: document.documentElement.dir,
                    headerCount: document.querySelectorAll('header').length,
                    footerCount: document.querySelectorAll('footer').length,
                    overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
                    productTheme: document.querySelector('[data-product-card-theme]')?.getAttribute('data-product-card-theme'),
                    primary: getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim(),
                    font: getComputedStyle(document.documentElement).getPropertyValue('--font-family').trim(),
                    body: document.body.innerText,
                })
                """);
            var metrics = JsonNode.Parse(metricsJson)!.AsObject();
            Ensure(metrics["headerCount"]!.GetValue<int>() == 1, $"{theme}/{scenario.Name} header count mismatch.");
            Ensure(metrics["footerCount"]!.GetValue<int>() == 1, $"{theme}/{scenario.Name} footer count mismatch.");
            Ensure(!metrics["overflow"]!.GetValue<bool>(), $"{theme}/{scenario.Name} has horizontal overflow.");
            Ensure(metrics["productTheme"]!.GetValue<string>() == theme, $"{theme}/{scenario.Name} product theme mismatch.");
            Ensure(metrics["primary"]!.GetValue<string>().Equals("#7c3aed", StringComparison.OrdinalIgnoreCase), $"{theme}/{scenario.Name} primary CSS variable mismatch.");
            Ensure(metrics["font"]!.GetValue<string>().Contains("Inter", StringComparison.OrdinalIgnoreCase), $"{theme}/{scenario.Name} font CSS variable mismatch.");
            Ensure(metrics["body"]!.GetValue<string>().Contains(marker, StringComparison.OrdinalIgnoreCase), $"{theme}/{scenario.Name} integrated authored content missing.");
            Ensure(consoleErrors.Count == 0, $"{theme}/{scenario.Name} console errors: {string.Join(" | ", consoleErrors)}");
            Ensure(failedRequests.Count == 0, $"{theme}/{scenario.Name} failed requests: {string.Join(" | ", failedRequests)}");
            Ensure(httpErrors.Count == 0, $"{theme}/{scenario.Name} HTTP errors: {string.Join(" | ", httpErrors)}");
            var screenshot = Path.Combine(screenshots, $"theme-{theme}-{scenario.Name}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true });
            matrix.Add(new JsonObject
            {
                ["theme"] = theme,
                ["scenario"] = scenario.Name,
                ["result"] = "PASS",
                ["metrics"] = metrics,
                ["screenshot"] = screenshot,
            });
        }
    }
}

static JsonObject ParseObject(string json) => JsonNode.Parse(json)?.AsObject()
    ?? throw new InvalidOperationException("Expected a JSON object response.");

static string RequiredString(JsonObject value, string property) => value[property]?.GetValue<string>()
    ?? throw new InvalidOperationException($"Required property '{property}' is missing.");

static async Task<JsonObject> CapturePublicAsync(IBrowser browser, string baseUrl)
{
    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
    });
    var page = await context.NewPageAsync();
    var document = await page.GotoAsync(baseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
    await page.Locator("header").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
    await page.Locator("footer").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
    var result = new JsonObject
    {
        ["documentStatus"] = document?.Status,
        ["url"] = page.Url,
        ["storeConfig"] = await PublicJsonAsync(page, "/api/storefront/store-config"),
        ["navigationEn"] = await PublicJsonAsync(page, "/api/storefront/navigation?lang=en"),
        ["navigationAr"] = await PublicJsonAsync(page, "/api/storefront/navigation?lang=ar"),
        ["footerEn"] = await PublicJsonAsync(page, "/api/storefront/footer?lang=en"),
        ["footerAr"] = await PublicJsonAsync(page, "/api/storefront/footer?lang=ar"),
        ["products"] = await PublicJsonAsync(page, "/api/storefront/products?page=1&pageSize=12"),
        ["categories"] = await PublicJsonAsync(page, "/api/storefront/categories"),
        ["about"] = await PublicResultAsync(page, "/api/storefront/pages/about"),
        ["runtime"] = JsonNode.Parse(await page.EvaluateAsync<string>("""
            () => JSON.stringify({
                lang: document.documentElement.lang,
                dir: document.documentElement.dir,
                headerCount: document.querySelectorAll('header').length,
                footerCount: document.querySelectorAll('footer').length,
                horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
                theme: document.querySelector('[data-product-card-theme]')?.getAttribute('data-product-card-theme') || null,
                primary: getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim(),
                font: getComputedStyle(document.documentElement).getPropertyValue('--font-family').trim(),
            })
            """)),
    };
    return result;
}

static async Task<JsonObject> CaptureDatabaseAuthorityAsync(string connectionString, string tenantId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    var tenant = Guid.Parse(tenantId);
    var storeConfig = await FingerprintRowsAsync(connection,
        "SELECT to_jsonb(x)::text FROM (SELECT * FROM \"StoreConfigs\" WHERE \"TenantId\" = @tenant ORDER BY \"Id\") x", tenant);
    var cmsPages = await FingerprintRowsAsync(connection,
        "SELECT to_jsonb(x)::text FROM (SELECT * FROM \"CmsPages\" WHERE \"TenantId\" = @tenant ORDER BY \"PageGroupId\", \"Version\", \"Id\") x", tenant);
    var products = await FingerprintRowsAsync(connection,
        "SELECT to_jsonb(x)::text FROM (SELECT * FROM \"Products\" WHERE \"TenantId\" = @tenant ORDER BY \"Id\") x", tenant);
    var categories = await FingerprintRowsAsync(connection,
        "SELECT to_jsonb(x)::text FROM (SELECT * FROM \"Categories\" WHERE \"TenantId\" = @tenant ORDER BY \"Id\") x", tenant);
    var footerSections = await ReadRowsAsync(connection,
        "SELECT to_jsonb(x)::text FROM (SELECT * FROM \"StorefrontFooterSections\" WHERE \"TenantId\" = @tenant ORDER BY \"SortOrder\", \"Id\") x", tenant);
    var footerLinks = await ReadRowsAsync(connection,
        "SELECT to_jsonb(x)::text FROM (SELECT l.* FROM \"StorefrontFooterLinks\" l JOIN \"StorefrontFooterSections\" s ON s.\"Id\" = l.\"SectionId\" WHERE s.\"TenantId\" = @tenant ORDER BY s.\"SortOrder\", l.\"SortOrder\", l.\"Id\") x", tenant);
    var socialLinks = await ReadRowsAsync(connection,
        "SELECT to_jsonb(x)::text FROM (SELECT * FROM \"StorefrontSocialLinks\" WHERE \"TenantId\" = @tenant ORDER BY \"SortOrder\", \"Id\") x", tenant);
    var footerCanonical = new JsonArray(footerSections.Concat(footerLinks).Concat(socialLinks).Select(row => JsonNode.Parse(row)).ToArray());
    return new JsonObject
    {
        ["tenant"] = await FingerprintRowsAsync(connection,
            "SELECT to_jsonb(x)::text FROM (SELECT * FROM \"Tenants\" WHERE \"Id\" = @tenant) x", tenant),
        ["storeConfig"] = storeConfig,
        ["cmsPages"] = cmsPages,
        ["products"] = products,
        ["categories"] = categories,
        ["footer"] = new JsonObject
        {
            ["rowCount"] = footerCanonical.Count,
            ["sha256"] = Sha256(Encoding.UTF8.GetBytes(footerCanonical.ToJsonString())),
            ["sections"] = new JsonArray(footerSections.Select(row => JsonNode.Parse(row)).ToArray()),
            ["links"] = new JsonArray(footerLinks.Select(row => JsonNode.Parse(row)).ToArray()),
            ["socialLinks"] = new JsonArray(socialLinks.Select(row => JsonNode.Parse(row)).ToArray()),
        },
    };
}

static async Task<JsonObject> FingerprintRowsAsync(NpgsqlConnection connection, string sql, Guid tenant)
{
    var rows = await ReadRowsAsync(connection, sql, tenant);
    var canonical = string.Join("\n", rows);
    var evidenceRows = rows.Select(RedactEvidenceEmails).ToList();
    return new JsonObject
    {
        ["rowCount"] = rows.Count,
        ["sha256"] = Sha256(Encoding.UTF8.GetBytes(canonical)),
        ["rows"] = new JsonArray(evidenceRows.Select(row => JsonNode.Parse(row)).ToArray()),
    };
}

static string RedactEvidenceEmails(string row)
{
    return Regex.Replace(
        row,
        @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}",
        "[REDACTED_EMAIL]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

static async Task<List<string>> ReadRowsAsync(NpgsqlConnection connection, string sql, Guid tenant)
{
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenant", tenant);
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<string>();
    while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
    return rows;
}

static async Task LoginAsync(IPage page, string email, string password)
{
    await page.GotoAsync($"{AdminUrl}/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
    await page.Locator("#pos-login-username").FillAsync(email);
    await page.Locator("#pos-login-password").FillAsync(password);
    await page.Locator("button[type=submit]").ClickAsync();
    await page.WaitForURLAsync(url => !new Uri(url).AbsolutePath.Equals("/login", StringComparison.OrdinalIgnoreCase));
}

static async Task<JsonNode> AdminJsonAsync(IPage page, string path)
{
    var result = await BrowserRequestAsync(page, "GET", path);
    Ensure(result.Status == 200, $"GET {path} returned {result.Status}: {result.Body}");
    return JsonNode.Parse(result.Body) ?? throw new InvalidOperationException($"GET {path} returned invalid JSON.");
}

static async Task<JsonNode> PublicJsonAsync(IPage page, string path)
{
    var result = await PublicResultAsync(page, path);
    Ensure(result["status"]!.GetValue<int>() == 200, $"GET {path} returned {result["status"]}: {result["body"]}");
    return JsonNode.Parse(result["body"]!.GetValue<string>()) ?? throw new InvalidOperationException($"GET {path} returned invalid JSON.");
}

static async Task<JsonObject> PublicResultAsync(IPage page, string path)
{
    var result = await page.EvaluateAsync<BrowserResponse>("""
        async path => {
            const response = await fetch(path, { cache: 'no-store', credentials: 'omit' });
            return { status: response.status, body: await response.text() };
        }
        """, path);
    return new JsonObject { ["status"] = result.Status, ["body"] = result.Body };
}

static async Task<BrowserResponse> BrowserRequestAsync(IPage page, string method, string path, JsonNode? body = null, string? ifMatch = null, Dictionary<string, string>? extraHeaders = null)
{
    return await page.EvaluateAsync<BrowserResponse>("""
        async request => {
            const csrf = document.cookie.split(';').map(x => x.trim()).find(x => x.startsWith('csrf_token='));
            const headers = { ...(request.extraHeaders || {}) };
            if (csrf && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method)) headers['X-CSRF-Token'] = decodeURIComponent(csrf.substring('csrf_token='.length));
            if (request.ifMatch) headers['If-Match'] = `"${request.ifMatch}"`;
            if (request.body) headers['Content-Type'] = 'application/json';
            const response = await fetch(request.path, {
                method: request.method,
                headers,
                credentials: 'include',
                cache: 'no-store',
                body: request.body,
            });
            return { status: response.status, body: await response.text() };
        }
        """, new { method, path, body = body?.ToJsonString(), ifMatch, extraHeaders });
}

static string ReadConnectionString(string settingsPath)
{
    using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
    return settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()
        ?? throw new InvalidOperationException("DefaultConnection is missing.");
}

static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct BrowserResponse(int Status, string Body);

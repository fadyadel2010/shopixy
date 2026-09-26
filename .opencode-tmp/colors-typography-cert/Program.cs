using System.Data;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Playwright;
using Npgsql;

const string TenantIdText = "c9c5c1ae-949e-498e-bc63-07242daeb49d";
const string AdminUrl = "http://localhost:5173";
const string StorefrontUrl = "http://ahramstore.localhost:5174";
const string StorefrontTransportUrl = "http://localhost:5174";
const string StorefrontHost = "ahramstore.localhost:5174";

var mode = args.FirstOrDefault() ?? "baseline";
var harnessDirectory = Directory.GetCurrentDirectory();
var repositoryRoot = Path.GetFullPath(Path.Combine(harnessDirectory, "..", ".."));
var settingsPath = Path.Combine(repositoryRoot, "PointOfSale.API", "appsettings.Development.local.json");
var baselinePath = Path.Combine(harnessDirectory, "baseline.json");
var connectionString = await ReadConnectionStringAsync(settingsPath);
var tenantId = Guid.Parse(TenantIdText);

if (string.Equals(mode, "baseline", StringComparison.OrdinalIgnoreCase))
{
    await CaptureImmutableBaselineAsync(connectionString, tenantId, baselinePath);
    return;
}

if (!string.Equals(mode, "certify", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Supported modes are baseline and certify.");
}

await RunCertificationAsync(connectionString, tenantId, baselinePath, repositoryRoot);

static async Task CaptureImmutableBaselineAsync(string connectionString, Guid tenantId, string baselinePath)
{
    if (File.Exists(baselinePath))
    {
        throw new InvalidOperationException("Immutable baseline already exists; refusing to overwrite it.");
    }

    var snapshot = await CaptureBaselineAsync(connectionString, tenantId);
    await File.WriteAllTextAsync(baselinePath, Serialize(snapshot));
    File.SetAttributes(baselinePath, File.GetAttributes(baselinePath) | FileAttributes.ReadOnly);

    Console.WriteLine(
        $"BASELINE_CAPTURED tenant={tenantId} rowVersion={snapshot["storeConfig"]?["RowVersion"]} " +
        $"draft={snapshot["storeConfig"]?["ThemeDraftOverridesJson"]} " +
        $"live={snapshot["storeConfig"]?["ThemeLiveOverridesJson"]}");
}

static async Task RunCertificationAsync(
    string connectionString,
    Guid tenantId,
    string baselinePath,
    string repositoryRoot)
{
    if (!File.Exists(baselinePath))
    {
        throw new InvalidOperationException("Immutable baseline is required before certification.");
    }

    var email = Environment.GetEnvironmentVariable("POS_CERT_EMAIL");
    var password = Environment.GetEnvironmentVariable("POS_CERT_PASSWORD");
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException("POS_CERT_EMAIL and POS_CERT_PASSWORD must be supplied through the process environment.");
    }

    var evidenceDirectory = Environment.GetEnvironmentVariable("POS_CERT_EVIDENCE_DIR");
    if (string.IsNullOrWhiteSpace(evidenceDirectory))
    {
        evidenceDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "colors-typography-final-evidence");
    }

    Directory.CreateDirectory(evidenceDirectory);
    var screenshotsDirectory = Path.Combine(evidenceDirectory, "screenshots");
    Directory.CreateDirectory(screenshotsDirectory);

    var baselineBytes = await File.ReadAllBytesAsync(baselinePath);
    var baselineHash = Convert.ToHexString(SHA256.HashData(baselineBytes));
    var baseline = JsonNode.Parse(baselineBytes)?.AsObject()
        ?? throw new InvalidOperationException("Baseline JSON is invalid.");
    var baselineStoreConfig = baseline["storeConfig"]?.AsObject()
        ?? throw new InvalidOperationException("Baseline StoreConfig is missing.");
    var baselinePublic = baseline["publicStoreConfig"]?.AsObject()
        ?? throw new InvalidOperationException("Baseline public StoreConfig is missing.");

    var candidate = new ThemeCandidate(
        "#7c3aed",
        "#0f766e",
        "#fff7ed",
        "#1c1917",
        "#78716c",
        "Cairo",
        "18px",
        14);
    var firstSavePrimary = "#1d4ed8";

    var authorityPath = Path.Combine(repositoryRoot, "theme-authority", "shopixy-theme-authority.json");
    using var authorityDocument = JsonDocument.Parse(await File.ReadAllTextAsync(authorityPath));
    var activeThemes = authorityDocument.RootElement.GetProperty("themes")
        .EnumerateArray()
        .Where(theme => theme.GetProperty("officialStatus").GetString() == "official"
            && theme.GetProperty("activationStatus").GetString() == "active"
            && theme.GetProperty("backendAvailability").GetBoolean()
            && theme.GetProperty("runtimeAvailability").GetBoolean())
        .Select(theme => theme.GetProperty("id").GetString()!)
        .ToArray();
    Ensure(activeThemes.Length == 10, $"Expected 10 active official themes, found {activeThemes.Length}.");
    Ensure(!activeThemes.Contains("electronics", StringComparer.OrdinalIgnoreCase), "Disabled electronics theme entered the active matrix.");

    var beforeDatabase = await CaptureVerificationAsync(connectionString, tenantId);
    AssertRestoredAuthority(beforeDatabase, baselineStoreConfig, baseline["authorityFingerprints"]!.AsObject());

    var evidence = new JsonObject
    {
        ["startedAtUtc"] = DateTime.UtcNow,
        ["scope"] = "MAIN/PRIMARY Storefront Colors & Typography",
        ["tenantId"] = TenantIdText,
        ["baselineSha256"] = baselineHash,
        ["activeThemes"] = new JsonArray(activeThemes.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
        ["candidate"] = candidate.ToJson(),
        ["apiHealth"] = await CaptureHealthAsync(),
        ["security"] = new JsonObject(),
        ["adminWorkflow"] = new JsonObject(),
        ["previewMatrix"] = new JsonArray(),
        ["liveChecks"] = new JsonArray(),
    };

    var mutationAccepted = false;
    var restored = false;
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Headless = true,
    });
    var adminContext = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        BaseURL = AdminUrl,
        ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        Locale = "en-US",
    });
    var adminPage = await adminContext.NewPageAsync();
    var adminConsoleErrors = new List<string>();
    var adminFailedRequests = new List<string>();
    var adminHttpErrors = new List<string>();
    var configReadCount = 0;
    AttachNetworkAudit(adminPage, adminConsoleErrors, adminFailedRequests, adminHttpErrors, response =>
    {
        if (response.Request.Method == "GET" && response.Url.Contains("/api/admin/store-config", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref configReadCount);
        }
    });

    try
    {
        await adminPage.GotoAsync(
            $"{AdminUrl}/storefront/appearance/colors-typography",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await adminPage.Locator("#pos-login-username").WaitForAsync();
        await adminPage.Locator("#pos-login-username").FillAsync(email);
        await adminPage.Locator("#pos-login-password").FillAsync(password);
        await adminPage.Locator("button[type='submit']").ClickAsync();
        await adminPage.GetByRole(AriaRole.Heading, new() { Name = "Colors & Typography", Exact = true }).WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30_000 });

        // The initial protected-route challenge is expected to return 401 before login.
        // Audit cleanliness from the point the authenticated authoring surface is ready.
        adminConsoleErrors.Clear();
        adminFailedRequests.Clear();
        adminHttpErrors.Clear();

        var adminWorkflow = evidence["adminWorkflow"]!.AsObject();
        adminWorkflow["loginSucceeded"] = true;
        adminWorkflow["initialControls"] = await ReadAdminControlsAsync(adminPage);
        AssertAdminControlsMatchBaseline(adminWorkflow["initialControls"]!.AsObject(), baselinePublic);
        await adminPage.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(screenshotsDirectory, "admin-initial.png"),
            FullPage = true,
        });

        var readsAfterInitialLoad = configReadCount;
        await adminPage.Locator("#appearance-primary").FillAsync("#345678");
        await adminPage.EvaluateAsync("""
            () => {
                window.dispatchEvent(new Event('blur'));
                window.dispatchEvent(new Event('focus'));
                document.dispatchEvent(new Event('visibilitychange'));
                document.dispatchEvent(new Event('copy'));
            }
            """);
        await adminPage.WaitForTimeoutAsync(500);
        Ensure(await adminPage.Locator("#appearance-primary").InputValueAsync() == "#345678", "Unsaved edit was lost during focus/visibility/copy events.");
        Ensure(configReadCount == readsAfterInitialLoad, "Session events caused an unexpected StoreConfig reload.");
        adminWorkflow["sessionStability"] = new JsonObject
        {
            ["unsavedEditRetained"] = true,
            ["unexpectedConfigReloads"] = configReadCount - readsAfterInitialLoad,
        };

        var writesBeforeInvalid = 0;
        void CountInvalidWrites(object? _, IResponse response)
        {
            if (response.Request.Method == "PUT" && response.Url.Contains("/api/admin/store-config/theme/overrides", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref writesBeforeInvalid);
            }
        }
        adminPage.Response += CountInvalidWrites;
        await adminPage.Locator("#appearance-primary").FillAsync("red; background:url(javascript:alert(1))");
        await adminPage.GetByRole(AriaRole.Alert).WaitForAsync();
        Ensure(await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save Draft", Exact = true }).IsDisabledAsync(), "Malformed color did not disable Save Draft.");
        Ensure(await adminPage.GetByRole(AriaRole.Button, new() { Name = "Publish Colors & Typography", Exact = true }).IsDisabledAsync(), "Malformed color did not disable Publish.");
        await adminPage.WaitForTimeoutAsync(250);
        Ensure(writesBeforeInvalid == 0, "Client validation allowed a malformed color mutation.");
        adminPage.Response -= CountInvalidWrites;
        adminWorkflow["clientValidation"] = new JsonObject
        {
            ["malformedColorBlocked"] = true,
            ["mutationCount"] = writesBeforeInvalid,
        };

        await SetAdminCandidateAsync(adminPage, candidate with { Primary = firstSavePrimary });
        var localPreview = await ReadLocalPreviewAsync(adminPage);
        Ensure(localPreview["fontFamily"]?.GetValue<string>().Contains(candidate.Font, StringComparison.OrdinalIgnoreCase) == true, "Local preview font did not update.");
        Ensure(localPreview["fontSize"]?.GetValue<string>() == candidate.BaseSize, "Local preview base size did not update.");

        await adminPage.RouteAsync("**/api/admin/store-config/theme/overrides", async route =>
        {
            await Task.Delay(800);
            await route.ContinueAsync();
        });
        var firstSaveResponseTask = adminPage.WaitForResponseAsync(response =>
            response.Request.Method == "PUT" && response.Url.Contains("/api/admin/store-config/theme/overrides", StringComparison.OrdinalIgnoreCase));
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save Draft", Exact = true }).ClickAsync();
        await adminPage.WaitForTimeoutAsync(100);
        await adminPage.Locator("#appearance-primary").FillAsync(candidate.Primary);
        var firstSaveResponse = await firstSaveResponseTask;
        Ensure(firstSaveResponse.Status == 200, $"First delayed draft save returned {firstSaveResponse.Status}.");
        mutationAccepted = true;
        await adminPage.UnrouteAsync("**/api/admin/store-config/theme/overrides");
        await adminPage.GetByText("Newer unsaved edits were retained", new() { Exact = false }).First.WaitForAsync();
        Ensure(await adminPage.Locator("#appearance-primary").InputValueAsync() == candidate.Primary, "Newer in-flight edit was overwritten by the save response.");

        var secondSaveResponseTask = adminPage.WaitForResponseAsync(response =>
            response.Request.Method == "PUT" && response.Url.Contains("/api/admin/store-config/theme/overrides", StringComparison.OrdinalIgnoreCase));
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save Draft", Exact = true }).ClickAsync();
        var secondSaveResponse = await secondSaveResponseTask;
        Ensure(secondSaveResponse.Status == 200, $"Final draft save returned {secondSaveResponse.Status}.");
        var savedConfig = ParseObject(await secondSaveResponse.TextAsync());
        AssertAdminTheme(savedConfig["draftTheme"]!.AsObject(), candidate);
        AssertAdminThemeMatchesPublic(savedConfig["liveTheme"]!.AsObject(), baselinePublic);
        adminWorkflow["draftSave"] = new JsonObject
        {
            ["status"] = secondSaveResponse.Status,
            ["rowVersion"] = savedConfig["rowVersion"]?.GetValue<string>(),
            ["newerInFlightEditRetained"] = true,
            ["draftAccepted"] = true,
            ["liveStillBaseline"] = true,
        };
        await adminPage.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(screenshotsDirectory, "admin-candidate-saved.png"),
            FullPage = true,
        });

        var publicBeforePublish = await ReadPublicConfigAsync(browser);
        AssertPublicTheme(publicBeforePublish, baselinePublic);
        adminWorkflow["draftLiveIsolation"] = true;

        var security = evidence["security"]!.AsObject();
        var anonymousContext = await browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = AdminUrl });
        var anonymousPage = await anonymousContext.NewPageAsync();
        await anonymousPage.GotoAsync($"{AdminUrl}/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var unauthorized = await BrowserFetchAsync(anonymousPage, "PUT", "/api/admin/store-config/theme/overrides", candidate.ToRequest(), "1");
        Ensure(unauthorized.Status is 400 or 401 or 403, $"Anonymous mutation was not denied; HTTP {unauthorized.Status}.");
        security["anonymousMutationStatus"] = unauthorized.Status;
        security["anonymousMutationDenied"] = true;
        await anonymousContext.CloseAsync();

        var securityPage = await adminContext.NewPageAsync();
        await securityPage.GotoAsync($"{AdminUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var missingPrecondition = await BrowserFetchAsync(securityPage, "PUT", "/api/admin/store-config/theme/overrides", candidate.ToRequest(), null);
        Ensure(missingPrecondition.Status == 428, $"Missing If-Match returned {missingPrecondition.Status} instead of 428.");
        security["missingIfMatchStatus"] = missingPrecondition.Status;

        var stalePrecondition = await BrowserFetchAsync(securityPage, "PUT", "/api/admin/store-config/theme/overrides", candidate.ToRequest(), "1");
        Ensure(stalePrecondition.Status == 409, $"Stale If-Match returned {stalePrecondition.Status} instead of 409.");
        security["staleIfMatchStatus"] = stalePrecondition.Status;

        var currentRowVersion = savedConfig["rowVersion"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Saved response omitted rowVersion.");
        var malformedRequest = candidate.ToRequest();
        malformedRequest["colors"]!["primary"] = "javascript:alert(1)";
        var malformed = await BrowserFetchAsync(securityPage, "PUT", "/api/admin/store-config/theme/overrides", malformedRequest, currentRowVersion);
        Ensure(malformed.Status == 400, $"Malformed backend mutation returned {malformed.Status} instead of 400.");
        security["malformedPayloadStatus"] = malformed.Status;
        await securityPage.CloseAsync();

        foreach (var themeId in activeThemes)
        {
            var previewLinkResponse = await BrowserFetchAsync(
                adminPage,
                "POST",
                $"/api/admin/store-config/theme/preview-link/{Uri.EscapeDataString(themeId)}",
                new JsonObject(),
                null);
            Ensure(previewLinkResponse.Status == 200, $"Preview link creation for {themeId} returned {previewLinkResponse.Status}.");
            var previewLink = ParseObject(previewLinkResponse.Text);
            var previewUrl = previewLink["previewUrl"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"Preview link for {themeId} omitted previewUrl.");
            var previewToken = previewLink["previewToken"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"Preview link for {themeId} omitted previewToken.");

            if (themeId == activeThemes[0])
            {
                var tamperedToken = previewToken[..^1] + (previewToken[^1] == 'a' ? "b" : "a");
                var tamperedContext = await browser.NewContextAsync();
                var tamperedPage = await tamperedContext.NewPageAsync();
                await tamperedPage.GotoAsync($"{StorefrontUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                var tampered = await BrowserFetchAsync(
                    tamperedPage,
                    "GET",
                    $"/api/storefront/theme-preview/config?themePreviewToken={Uri.EscapeDataString(tamperedToken)}",
                    null,
                    null);
                Ensure(tampered.Status == 401, $"Tampered preview token returned {tampered.Status} instead of 401.");
                security["tamperedPreviewTokenStatus"] = tampered.Status;
                await tamperedContext.CloseAsync();
            }

            foreach (var scenario in new[]
            {
                new PreviewScenario("en-desktop", "en", "en-US", 1440, 1000, "ltr"),
                new PreviewScenario("ar-mobile", "ar", "ar-EG", 390, 844, "rtl"),
            })
            {
                var result = await AuditPreviewAsync(
                    browser,
                    previewUrl,
                    themeId,
                    scenario,
                    candidate,
                    screenshotsDirectory);
                evidence["previewMatrix"]!.AsArray().Add(result);
            }
        }

        Ensure(evidence["previewMatrix"]!.AsArray().Count == 20, "Preview matrix did not produce 20 theme/language/viewport checks.");

        var publishResponseTask = adminPage.WaitForResponseAsync(response =>
            response.Request.Method == "POST" && response.Url.Contains("/api/admin/store-config/theme/publish", StringComparison.OrdinalIgnoreCase));
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Publish Colors & Typography", Exact = true }).ClickAsync();
        var publishDialog = adminPage.GetByRole(AriaRole.Dialog, new() { Name = "Publish Colors & Typography?", Exact = true });
        await publishDialog.WaitForAsync();
        await publishDialog.GetByRole(AriaRole.Button, new() { Name = "Publish Colors & Typography", Exact = true }).ClickAsync();
        var publishResponse = await publishResponseTask;
        Ensure(publishResponse.Status == 200, $"Theme publish returned {publishResponse.Status}.");
        var publishedConfig = ParseObject(await publishResponse.TextAsync());
        AssertAdminTheme(publishedConfig["liveTheme"]!.AsObject(), candidate);
        adminWorkflow["publish"] = new JsonObject
        {
            ["status"] = publishResponse.Status,
            ["rowVersion"] = publishedConfig["rowVersion"]?.GetValue<string>(),
            ["liveAccepted"] = true,
        };

        var publicAfterPublish = await ReadPublicConfigAsync(browser);
        AssertPublicCandidate(publicAfterPublish, candidate);
        evidence["liveChecks"]!.AsArray().Add(await AuditLiveAsync(
            browser,
            new PreviewScenario("en-desktop", "en", "en-US", 1440, 1000, "ltr"),
            candidate,
            screenshotsDirectory));
        evidence["liveChecks"]!.AsArray().Add(await AuditLiveAsync(
            browser,
            new PreviewScenario("ar-mobile", "ar", "ar-EG", 390, 844, "rtl"),
            candidate,
            screenshotsDirectory));

        adminWorkflow["consoleErrors"] = new JsonArray(adminConsoleErrors.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        adminWorkflow["failedRequests"] = new JsonArray(adminFailedRequests.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        adminWorkflow["httpErrors"] = new JsonArray(adminHttpErrors.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        Ensure(adminConsoleErrors.Count == 0, $"Admin emitted {adminConsoleErrors.Count} console errors: {string.Join(" | ", adminConsoleErrors)}");
        Ensure(adminFailedRequests.Count == 0, $"Admin emitted {adminFailedRequests.Count} failed requests: {string.Join(" | ", adminFailedRequests)}");
        Ensure(adminHttpErrors.Count == 0, $"Admin emitted {adminHttpErrors.Count} unexpected HTTP errors: {string.Join(" | ", adminHttpErrors)}");
    }
    finally
    {
        if (mutationAccepted)
        {
            try
            {
                await RestoreBaselineThroughApiAsync(adminPage, baselineStoreConfig);
                restored = true;
            }
            catch (Exception restoreError)
            {
                evidence["restoration"] = new JsonObject
                {
                    ["succeeded"] = false,
                    ["error"] = restoreError.Message,
                };
            }
        }

        await adminContext.CloseAsync();
    }

    Ensure(!mutationAccepted || restored, "Certification mutation was accepted but exact API restoration failed.");

    var afterDatabase = await CaptureVerificationAsync(connectionString, tenantId);
    AssertRestoredAuthority(afterDatabase, baselineStoreConfig, baseline["authorityFingerprints"]!.AsObject());
    var restoredPublic = await ReadPublicConfigAsync(browser);
    AssertPublicTheme(restoredPublic, baselinePublic);
    var finalBaselineHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(baselinePath)));
    Ensure(finalBaselineHash == baselineHash, "Immutable baseline file changed during certification.");

    evidence["restoration"] = new JsonObject
    {
        ["succeeded"] = true,
        ["draftOverridesExact"] = true,
        ["liveOverridesExact"] = true,
        ["legacyAppearanceExact"] = true,
        ["publicAppearanceExact"] = true,
        ["crossImpactFingerprintsExact"] = true,
        ["baselineSha256Unchanged"] = true,
        ["finalRowVersion"] = afterDatabase["storeConfig"]?["RowVersion"]?.GetValue<string>(),
        ["finalLiveVersion"] = afterDatabase["storeConfig"]?["LiveVersion"]?.GetValue<long>(),
    };
    evidence["completedAtUtc"] = DateTime.UtcNow;
    evidence["verdict"] = "PASS";

    var evidencePath = Path.Combine(evidenceDirectory, "final-audit.json");
    await File.WriteAllTextAsync(evidencePath, Serialize(evidence));
    Console.WriteLine($"CERTIFICATION_PASS evidence={evidencePath} previews=20 restoration=exact");
}

static async Task<JsonObject> AuditPreviewAsync(
    IBrowser browser,
    string previewUrl,
    string themeId,
    PreviewScenario scenario,
    ThemeCandidate candidate,
    string screenshotsDirectory)
{
    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        Locale = scenario.Locale,
        ViewportSize = new ViewportSize { Width = scenario.Width, Height = scenario.Height },
    });
    var page = await context.NewPageAsync();
    var consoleErrors = new List<string>();
    var failedRequests = new List<string>();
    var httpErrors = new List<string>();
    var previewConfigStatuses = new List<int>();
    var authRequests = new List<string>();
    AttachNetworkAudit(page, consoleErrors, failedRequests, httpErrors, response =>
    {
        if (response.Url.Contains("/api/storefront/theme-preview/config", StringComparison.OrdinalIgnoreCase))
        {
            previewConfigStatuses.Add(response.Status);
        }
        if (response.Url.Contains("/api/auth/", StringComparison.OrdinalIgnoreCase))
        {
            authRequests.Add(SanitizeUrl(response.Url));
        }
    });

    try
    {
        var separator = previewUrl.Contains('?') ? "&" : "?";
        await page.GotoAsync(
            $"{previewUrl}{separator}lang={scenario.Language}",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        await page.Locator("header").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await page.Locator("footer").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await page.Locator($"[data-product-card-theme='{themeId}']").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await WaitForThemeVariablesAsync(page, candidate);

        var measurements = await ReadRuntimeMeasurementsAsync(page);
        AssertRuntimeMeasurements(measurements, candidate, scenario.Direction);
        Ensure(measurements["headerCount"]!.GetValue<int>() == 1, $"{themeId}/{scenario.Name} rendered an unexpected header count.");
        Ensure(measurements["footerCount"]!.GetValue<int>() == 1, $"{themeId}/{scenario.Name} rendered an unexpected footer count.");
        Ensure(measurements["productTheme"]!.GetValue<string>() == themeId, $"{themeId}/{scenario.Name} resolved the wrong product-card theme.");
        Ensure(previewConfigStatuses.Contains(200), $"{themeId}/{scenario.Name} did not load preview config with HTTP 200.");
        Ensure(authRequests.Count == 0, $"{themeId}/{scenario.Name} preview made auth requests.");
        Ensure(consoleErrors.Count == 0, $"{themeId}/{scenario.Name} emitted console errors: {string.Join(" | ", consoleErrors)}");
        Ensure(failedRequests.Count == 0, $"{themeId}/{scenario.Name} emitted failed requests: {string.Join(" | ", failedRequests)}");
        Ensure(httpErrors.Count == 0, $"{themeId}/{scenario.Name} emitted HTTP errors: {string.Join(" | ", httpErrors)}");

        var screenshotName = $"preview-{themeId}-{scenario.Name}.png";
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(screenshotsDirectory, screenshotName),
            FullPage = true,
        });

        return new JsonObject
        {
            ["themeId"] = themeId,
            ["scenario"] = scenario.Name,
            ["language"] = scenario.Language,
            ["direction"] = scenario.Direction,
            ["viewportWidth"] = scenario.Width,
            ["previewConfigStatus"] = 200,
            ["cssVariablesExact"] = true,
            ["noHorizontalOverflow"] = true,
            ["headerCount"] = 1,
            ["footerCount"] = 1,
            ["productCardTheme"] = themeId,
            ["authRequestCount"] = 0,
            ["consoleErrorCount"] = 0,
            ["failedRequestCount"] = 0,
            ["httpErrorCount"] = 0,
            ["screenshot"] = screenshotName,
        };
    }
    finally
    {
        await context.CloseAsync();
    }
}

static async Task<JsonObject> AuditLiveAsync(
    IBrowser browser,
    PreviewScenario scenario,
    ThemeCandidate candidate,
    string screenshotsDirectory)
{
    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        Locale = scenario.Locale,
        ViewportSize = new ViewportSize { Width = scenario.Width, Height = scenario.Height },
    });
    var page = await context.NewPageAsync();
    var consoleErrors = new List<string>();
    var failedRequests = new List<string>();
    var httpErrors = new List<string>();
    AttachNetworkAudit(page, consoleErrors, failedRequests, httpErrors);

    try
    {
        await page.GotoAsync(
            $"{StorefrontUrl}/?lang={scenario.Language}",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        await page.Locator("header").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await page.Locator("footer").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await WaitForThemeVariablesAsync(page, candidate);
        var measurements = await ReadRuntimeMeasurementsAsync(page);
        AssertRuntimeMeasurements(measurements, candidate, scenario.Direction);
        var expectedGuestAuthHttpErrors = httpErrors
            .Where(IsExpectedGuestAuthObservation)
            .ToArray();
        var unexpectedHttpErrors = httpErrors
            .Where(error => !IsExpectedGuestAuthObservation(error))
            .ToArray();
        var expectedGuestAuthConsoleErrors = consoleErrors
            .Where(error => IsExpectedGuestAuthObservation(error)
                || error.StartsWith("Failed to load resource: the server responded with a status of 401", StringComparison.Ordinal))
            .ToArray();
        var unexpectedConsoleErrors = consoleErrors
            .Except(expectedGuestAuthConsoleErrors, StringComparer.Ordinal)
            .ToArray();
        Ensure(unexpectedConsoleErrors.Length == 0, $"Live {scenario.Name} emitted unexpected console errors: {string.Join(" | ", unexpectedConsoleErrors)}");
        Ensure(failedRequests.Count == 0, $"Live {scenario.Name} emitted failed requests: {string.Join(" | ", failedRequests)}");
        Ensure(unexpectedHttpErrors.Length == 0, $"Live {scenario.Name} emitted unexpected HTTP errors: {string.Join(" | ", unexpectedHttpErrors)}");

        var screenshotName = $"live-{scenario.Name}.png";
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(screenshotsDirectory, screenshotName),
            FullPage = true,
        });
        return new JsonObject
        {
            ["scenario"] = scenario.Name,
            ["language"] = scenario.Language,
            ["direction"] = scenario.Direction,
            ["viewportWidth"] = scenario.Width,
            ["cssVariablesExact"] = true,
            ["noHorizontalOverflow"] = true,
            ["expectedGuestAuthConsoleObservationCount"] = expectedGuestAuthConsoleErrors.Length,
            ["unexpectedConsoleErrorCount"] = 0,
            ["failedRequestCount"] = 0,
            ["expectedGuestAuthHttpStatusCount"] = expectedGuestAuthHttpErrors.Length,
            ["unexpectedHttpErrorCount"] = 0,
            ["screenshot"] = screenshotName,
        };
    }
    finally
    {
        await context.CloseAsync();
    }
}

static async Task RestoreBaselineThroughApiAsync(IPage adminPage, JsonObject baselineStoreConfig)
{
    if (!adminPage.Url.StartsWith(AdminUrl, StringComparison.OrdinalIgnoreCase))
    {
        await adminPage.GotoAsync($"{AdminUrl}/storefront/appearance/colors-typography", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    var current = await BrowserFetchAsync(adminPage, "GET", "/api/admin/store-config", null, null);
    Ensure(current.Status == 200, $"Restoration could not read current StoreConfig: HTTP {current.Status}.");
    var currentJson = ParseObject(current.Text);
    var rowVersion = currentJson["rowVersion"]?.GetValue<string>()
        ?? throw new InvalidOperationException("Restoration StoreConfig read omitted rowVersion.");

    var restoreRequest = new JsonObject
    {
        ["presetKey"] = baselineStoreConfig["ThemePresetKey"]?.GetValue<string>() ?? "default",
        ["colors"] = new JsonObject
        {
            ["primary"] = baselineStoreConfig["Theme_Colors_Primary"]?.GetValue<string>(),
            ["secondary"] = baselineStoreConfig["Theme_Colors_Secondary"]?.GetValue<string>(),
            ["background"] = baselineStoreConfig["Theme_Colors_Background"]?.GetValue<string>(),
            ["text"] = baselineStoreConfig["Theme_Colors_Text"]?.GetValue<string>(),
            ["muted"] = baselineStoreConfig["Theme_Colors_Muted"]?.GetValue<string>(),
        },
        // Preserve the baseline's legacy normalized JSON shape exactly.
        ["font"] = baselineStoreConfig["Theme_Font_Family"]?.GetValue<string>(),
        ["borderRadius"] = ParsePixelNumber(baselineStoreConfig["Theme_Ui_BorderRadius"]?.GetValue<string>()),
    };

    var draftRestore = await BrowserFetchAsync(adminPage, "PUT", "/api/admin/store-config/theme/overrides", restoreRequest, rowVersion);
    Ensure(draftRestore.Status == 200, $"Draft restoration returned HTTP {draftRestore.Status}: {draftRestore.Text}");
    var draftRestoreJson = ParseObject(draftRestore.Text);
    var publishRowVersion = draftRestoreJson["rowVersion"]?.GetValue<string>()
        ?? throw new InvalidOperationException("Draft restoration response omitted rowVersion.");
    var publishRestore = await BrowserFetchAsync(adminPage, "POST", "/api/admin/store-config/theme/publish", new JsonObject(), publishRowVersion);
    Ensure(publishRestore.Status == 200, $"Live restoration returned HTTP {publishRestore.Status}: {publishRestore.Text}");
}

static async Task SetAdminCandidateAsync(IPage page, ThemeCandidate candidate)
{
    await page.Locator("#appearance-primary").FillAsync(candidate.Primary);
    await page.Locator("#appearance-secondary").FillAsync(candidate.Secondary);
    await page.Locator("#appearance-background").FillAsync(candidate.Background);
    await page.Locator("#appearance-text").FillAsync(candidate.Text);
    await page.Locator("#appearance-muted").FillAsync(candidate.Muted);
    await page.Locator("#appearance-font-family").SelectOptionAsync(candidate.Font);
    await SetRangeValueAsync(page.Locator("#appearance-base-size"), ParsePixelNumber(candidate.BaseSize));
    await SetRangeValueAsync(page.Locator("#appearance-border-radius"), candidate.BorderRadius);
}

static async Task SetRangeValueAsync(ILocator locator, int value)
{
    await locator.EvaluateAsync(
        """
        (element, nextValue) => {
            const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
            valueSetter.call(element, String(nextValue));
            element.dispatchEvent(new Event('input', { bubbles: true }));
            element.dispatchEvent(new Event('change', { bubbles: true }));
        }
        """,
        value);
}

static async Task<JsonObject> ReadAdminControlsAsync(IPage page)
{
    return new JsonObject
    {
        ["primary"] = await page.Locator("#appearance-primary").InputValueAsync(),
        ["secondary"] = await page.Locator("#appearance-secondary").InputValueAsync(),
        ["background"] = await page.Locator("#appearance-background").InputValueAsync(),
        ["text"] = await page.Locator("#appearance-text").InputValueAsync(),
        ["muted"] = await page.Locator("#appearance-muted").InputValueAsync(),
        ["font"] = await page.Locator("#appearance-font-family").InputValueAsync(),
        ["baseSize"] = $"{await page.Locator("#appearance-base-size").InputValueAsync()}px",
        ["borderRadius"] = int.Parse(await page.Locator("#appearance-border-radius").InputValueAsync()),
    };
}

static async Task<JsonObject> ReadLocalPreviewAsync(IPage page)
{
    var result = await page.EvaluateAsync<JsonElement>("""
        () => {
            const preview = document.querySelector('[aria-label="Appearance candidate preview"] > div');
            const styles = getComputedStyle(preview);
            return { fontFamily: styles.fontFamily, fontSize: styles.fontSize };
        }
        """);
    return JsonNode.Parse(result.GetRawText())!.AsObject();
}

static async Task WaitForThemeVariablesAsync(IPage page, ThemeCandidate candidate)
{
    await page.WaitForFunctionAsync(
        "expected => getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim().toLowerCase() === expected",
        candidate.Primary.ToLowerInvariant(),
        new PageWaitForFunctionOptions { Timeout = 30_000 });
}

static async Task<JsonObject> ReadRuntimeMeasurementsAsync(IPage page)
{
    var result = await page.EvaluateAsync<JsonElement>("""
        () => {
            const styles = getComputedStyle(document.documentElement);
            const productCard = document.querySelector('[data-product-card-theme]');
            return {
                primary: styles.getPropertyValue('--color-primary').trim(),
                secondary: styles.getPropertyValue('--color-secondary').trim(),
                background: styles.getPropertyValue('--color-background').trim(),
                text: styles.getPropertyValue('--color-text').trim(),
                muted: styles.getPropertyValue('--color-muted').trim(),
                primaryRgb: styles.getPropertyValue('--color-primary-rgb').trim(),
                secondaryRgb: styles.getPropertyValue('--color-secondary-rgb').trim(),
                textMuted: styles.getPropertyValue('--color-text-muted').trim(),
                fontFamily: styles.getPropertyValue('--font-family').trim(),
                baseSize: styles.getPropertyValue('--font-base-size').trim(),
                borderRadius: styles.getPropertyValue('--border-radius').trim(),
                lang: document.documentElement.lang,
                direction: document.documentElement.dir,
                horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
                headerCount: document.querySelectorAll('header').length,
                footerCount: document.querySelectorAll('footer').length,
                productTheme: productCard?.getAttribute('data-product-card-theme') || '',
            };
        }
        """);
    return JsonNode.Parse(result.GetRawText())!.AsObject();
}

static void AssertRuntimeMeasurements(JsonObject actual, ThemeCandidate expected, string expectedDirection)
{
    Ensure(EqualsIgnoreCase(actual, "primary", expected.Primary), "Runtime primary CSS variable mismatch.");
    Ensure(EqualsIgnoreCase(actual, "secondary", expected.Secondary), "Runtime secondary CSS variable mismatch.");
    Ensure(EqualsIgnoreCase(actual, "background", expected.Background), "Runtime background CSS variable mismatch.");
    Ensure(EqualsIgnoreCase(actual, "text", expected.Text), "Runtime text CSS variable mismatch.");
    Ensure(EqualsIgnoreCase(actual, "muted", expected.Muted), "Runtime muted CSS variable mismatch.");
    Ensure(actual["primaryRgb"]?.GetValue<string>() == HexToRgb(expected.Primary), "Runtime primary RGB CSS variable mismatch.");
    Ensure(actual["secondaryRgb"]?.GetValue<string>() == HexToRgb(expected.Secondary), "Runtime secondary RGB CSS variable mismatch.");
    Ensure(EqualsIgnoreCase(actual, "textMuted", expected.Muted), "Runtime muted alias CSS variable mismatch.");
    Ensure(actual["fontFamily"]?.GetValue<string>() == expected.Font, "Runtime font CSS variable mismatch.");
    Ensure(actual["baseSize"]?.GetValue<string>() == expected.BaseSize, "Runtime base-size CSS variable mismatch.");
    Ensure(actual["borderRadius"]?.GetValue<string>() == $"{expected.BorderRadius}px", "Runtime radius CSS variable mismatch.");
    Ensure(actual["direction"]?.GetValue<string>() == expectedDirection, "Runtime document direction mismatch.");
    Ensure(actual["horizontalOverflow"]?.GetValue<bool>() == false, "Runtime page has horizontal overflow.");
}

static void AssertAdminControlsMatchBaseline(JsonObject controls, JsonObject baselinePublic)
{
    var theme = baselinePublic["theme"]!.AsObject();
    var colors = theme["colors"]!.AsObject();
    Ensure(EqualsIgnoreCase(controls, "primary", colors["primary"]!.GetValue<string>()), "Initial Admin primary differs from baseline.");
    Ensure(EqualsIgnoreCase(controls, "secondary", colors["secondary"]!.GetValue<string>()), "Initial Admin secondary differs from baseline.");
    Ensure(EqualsIgnoreCase(controls, "background", colors["background"]!.GetValue<string>()), "Initial Admin background differs from baseline.");
    Ensure(EqualsIgnoreCase(controls, "text", colors["text"]!.GetValue<string>()), "Initial Admin text differs from baseline.");
    Ensure(EqualsIgnoreCase(controls, "muted", colors["muted"]!.GetValue<string>()), "Initial Admin muted differs from baseline.");
    Ensure(controls["font"]!.GetValue<string>() == theme["font"]!["family"]!.GetValue<string>(), "Initial Admin font differs from baseline.");
    Ensure(controls["baseSize"]!.GetValue<string>() == theme["font"]!["baseSize"]!.GetValue<string>(), "Initial Admin base size differs from baseline.");
    Ensure($"{controls["borderRadius"]!.GetValue<int>()}px" == theme["ui"]!["borderRadius"]!.GetValue<string>(), "Initial Admin radius differs from baseline.");
}

static void AssertAdminTheme(JsonObject theme, ThemeCandidate expected)
{
    var colors = theme["colors"]!.AsObject();
    Ensure(EqualsIgnoreCase(colors, "primary", expected.Primary), "Admin theme primary mismatch.");
    Ensure(EqualsIgnoreCase(colors, "secondary", expected.Secondary), "Admin theme secondary mismatch.");
    Ensure(EqualsIgnoreCase(colors, "background", expected.Background), "Admin theme background mismatch.");
    Ensure(EqualsIgnoreCase(colors, "text", expected.Text), "Admin theme text mismatch.");
    Ensure(EqualsIgnoreCase(colors, "muted", expected.Muted), "Admin theme muted mismatch.");
    Ensure(theme["font"]?.GetValue<string>() == expected.Font, "Admin theme font mismatch.");
    Ensure(theme["baseSize"]?.GetValue<string>() == expected.BaseSize, "Admin theme base-size mismatch.");
    Ensure(theme["borderRadius"]?.GetValue<int>() == expected.BorderRadius, "Admin theme radius mismatch.");
}

static void AssertAdminThemeMatchesPublic(JsonObject adminTheme, JsonObject expectedPublic)
{
    var theme = expectedPublic["theme"]!.AsObject();
    var colors = theme["colors"]!.AsObject();
    Ensure(EqualsIgnoreCase(adminTheme["colors"]!.AsObject(), "primary", colors["primary"]!.GetValue<string>()), "Admin live primary differs from public baseline.");
    Ensure(adminTheme["font"]!.GetValue<string>() == theme["font"]!["family"]!.GetValue<string>(), "Admin live font differs from public baseline.");
    Ensure(adminTheme["baseSize"]!.GetValue<string>() == theme["font"]!["baseSize"]!.GetValue<string>(), "Admin live base size differs from public baseline.");
}

static void AssertPublicTheme(JsonObject actual, JsonObject expectedPublic)
{
    var expectedTheme = expectedPublic["theme"]!.AsObject();
    var actualTheme = actual["theme"]!.AsObject();
    Ensure(JsonNode.DeepEquals(actualTheme["colors"], expectedTheme["colors"]), "Public colors differ from baseline.");
    Ensure(JsonNode.DeepEquals(actualTheme["font"], expectedTheme["font"]), "Public typography differs from baseline.");
    Ensure(actualTheme["ui"]!["borderRadius"]!.GetValue<string>() == expectedTheme["ui"]!["borderRadius"]!.GetValue<string>(), "Public radius differs from baseline.");
}

static void AssertPublicCandidate(JsonObject actual, ThemeCandidate expected)
{
    var theme = actual["theme"]!.AsObject();
    var colors = theme["colors"]!.AsObject();
    Ensure(EqualsIgnoreCase(colors, "primary", expected.Primary), "Public primary mismatch.");
    Ensure(EqualsIgnoreCase(colors, "secondary", expected.Secondary), "Public secondary mismatch.");
    Ensure(EqualsIgnoreCase(colors, "background", expected.Background), "Public background mismatch.");
    Ensure(EqualsIgnoreCase(colors, "text", expected.Text), "Public text mismatch.");
    Ensure(EqualsIgnoreCase(colors, "muted", expected.Muted), "Public muted mismatch.");
    Ensure(theme["font"]!["family"]!.GetValue<string>() == expected.Font, "Public font mismatch.");
    Ensure(theme["font"]!["baseSize"]!.GetValue<string>() == expected.BaseSize, "Public base-size mismatch.");
    Ensure(theme["ui"]!["borderRadius"]!.GetValue<string>() == $"{expected.BorderRadius}px", "Public radius mismatch.");
}

static async Task<JsonObject> ReadPublicConfigAsync(IBrowser browser)
{
    var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    try
    {
        await page.GotoAsync($"{StorefrontUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var response = await BrowserFetchAsync(page, "GET", "/api/storefront/store-config", null, null);
        Ensure(response.Status == 200, $"Public StoreConfig returned HTTP {response.Status}.");
        return ParseObject(response.Text);
    }
    finally
    {
        await context.CloseAsync();
    }
}

static async Task<BrowserFetchResult> BrowserFetchAsync(
    IPage page,
    string method,
    string path,
    JsonNode? body,
    string? ifMatch)
{
    var argument = new Dictionary<string, object?>
    {
        ["method"] = method,
        ["path"] = path,
        ["body"] = body?.ToJsonString(),
        ["ifMatch"] = ifMatch,
    };
    var result = await page.EvaluateAsync<JsonElement>("""
        async argument => {
            const headers = { 'Content-Type': 'application/json' };
            const csrfPart = document.cookie.split(';').map(value => value.trim()).find(value => value.startsWith('csrf_token='));
            if (csrfPart) headers['X-CSRF-Token'] = decodeURIComponent(csrfPart.substring('csrf_token='.length));
            if (argument.ifMatch) headers['If-Match'] = `"${argument.ifMatch.replaceAll('"', '')}"`;
            const response = await fetch(argument.path, {
                method: argument.method,
                credentials: 'include',
                headers,
                body: argument.method === 'GET' || argument.method === 'HEAD' ? undefined : (argument.body || '{}'),
            });
            return { status: response.status, text: await response.text() };
        }
        """, argument);
    return new BrowserFetchResult(
        result.GetProperty("status").GetInt32(),
        result.GetProperty("text").GetString() ?? string.Empty);
}

static void AttachNetworkAudit(
    IPage page,
    List<string> consoleErrors,
    List<string> failedRequests,
    List<string> httpErrors,
    Action<IResponse>? onResponse = null)
{
    page.Console += (_, message) =>
    {
        if (message.Type == "error")
        {
            consoleErrors.Add(message.Text);
        }
    };
    page.RequestFailed += (_, request) => failedRequests.Add($"{request.Method} {SanitizeUrl(request.Url)}: {request.Failure}");
    page.Response += (_, response) =>
    {
        onResponse?.Invoke(response);
        if (response.Status >= 400)
        {
            httpErrors.Add($"{response.Status} {response.Request.Method} {SanitizeUrl(response.Url)}");
        }
    };
}

static string SanitizeUrl(string rawUrl)
{
    return Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
        ? uri.GetLeftPart(UriPartial.Path)
        : rawUrl.Split('?', 2)[0];
}

static bool IsExpectedGuestAuthObservation(string value)
{
    return value.Contains("/api/storefront/auth/me", StringComparison.OrdinalIgnoreCase)
        || value.Contains("/api/storefront/auth/refresh", StringComparison.OrdinalIgnoreCase);
}

static async Task<JsonObject> CaptureHealthAsync()
{
    using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5010") };
    var liveResponse = await http.GetAsync("/health/live");
    var readyResponse = await http.GetAsync("/health/ready");
    var readyText = await readyResponse.Content.ReadAsStringAsync();
    var readyJson = JsonNode.Parse(readyText)?.AsObject();
    var unhealthy = readyJson?["entries"]?.AsObject()
        .Where(entry => entry.Value?["status"]?.GetValue<string>() == "Unhealthy")
        .Select(entry => entry.Key)
        .ToArray() ?? Array.Empty<string>();
    return new JsonObject
    {
        ["liveStatus"] = (int)liveResponse.StatusCode,
        ["readyStatus"] = (int)readyResponse.StatusCode,
        ["readyOverall"] = readyJson?["status"]?.GetValue<string>(),
        ["unhealthyEntries"] = new JsonArray(unhealthy.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
    };
}

static async Task<JsonObject> CaptureVerificationAsync(string connectionString, Guid tenantId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
    {
        await readOnly.ExecuteNonQueryAsync();
    }

    var result = new JsonObject
    {
        ["storeConfig"] = await ReadSingleAsync(connection, transaction, """
            SELECT "ThemeId", "Theme_Colors_Primary", "Theme_Colors_Secondary", "Theme_Colors_Background",
                   "Theme_Colors_Text", "Theme_Colors_Muted", "Theme_Font_Family", "Theme_Font_BaseSize",
                   "Theme_Ui_BorderRadius", "Theme_Ui_ButtonStyle", "Theme_Layout_ProductCardVariant",
                   "Theme_Layout_HeaderVariant", "ThemePresetKey", "ThemeLivePresetKey",
                   "ThemeDraftOverridesJson", "ThemeLiveOverridesJson", "CustomizationJson", "LiveVersion",
                   xmin::text AS "RowVersion"
            FROM "StoreConfigs" WHERE "TenantId" = @tenantId
            """, tenantId),
        ["authorityFingerprints"] = await ReadAuthorityFingerprintsAsync(connection, transaction, tenantId),
    };
    await transaction.RollbackAsync();
    return result;
}

static void AssertRestoredAuthority(JsonObject actual, JsonObject baselineStoreConfig, JsonObject baselineFingerprints)
{
    var storeConfig = actual["storeConfig"]!.AsObject();
    var exactColumns = new[]
    {
        "ThemeId", "Theme_Colors_Primary", "Theme_Colors_Secondary", "Theme_Colors_Background",
        "Theme_Colors_Text", "Theme_Colors_Muted", "Theme_Font_Family", "Theme_Font_BaseSize",
        "Theme_Ui_BorderRadius", "Theme_Ui_ButtonStyle", "Theme_Layout_ProductCardVariant",
        "Theme_Layout_HeaderVariant", "ThemePresetKey", "ThemeLivePresetKey", "ThemeDraftOverridesJson",
        "ThemeLiveOverridesJson", "CustomizationJson",
    };
    foreach (var column in exactColumns)
    {
        Ensure(JsonNode.DeepEquals(storeConfig[column], baselineStoreConfig[column]), $"Restoration mismatch in StoreConfig.{column}.");
    }

    var fingerprints = actual["authorityFingerprints"]!.AsObject();
    foreach (var baselineFingerprint in baselineFingerprints)
    {
        Ensure(
            JsonNode.DeepEquals(fingerprints[baselineFingerprint.Key], baselineFingerprint.Value),
            $"Cross-impact fingerprint changed: {baselineFingerprint.Key}.");
    }
}

static async Task<JsonObject?> ReadAuthorityFingerprintsAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid tenantId)
{
    return await ReadSingleAsync(connection, transaction, """
        SELECT
          md5(COALESCE((SELECT "CustomizationJson"::jsonb ->> 'storefrontTheme' FROM "StoreConfigs" WHERE "TenantId" = @tenantId), '')) AS "ThemeIdentity",
          md5(COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "HomepageSections" row WHERE row."TenantId" = @tenantId), '')) AS "Homepage",
          md5(COALESCE((SELECT "CustomizationJson"::jsonb #>> '{navigation}' FROM "StoreConfigs" WHERE "TenantId" = @tenantId), '')) AS "Navigation",
          md5(COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "StorefrontFooterSections" row WHERE row."TenantId" = @tenantId), '') || '|' ||
              COALESCE((SELECT string_agg(to_jsonb(link)::text, '|' ORDER BY link."Id") FROM "StorefrontFooterLinks" link INNER JOIN "StorefrontFooterSections" section ON section."Id" = link."SectionId" WHERE section."TenantId" = @tenantId), '') || '|' ||
              COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "StorefrontSocialLinks" row WHERE row."TenantId" = @tenantId), '')) AS "Footer",
          md5(COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "CmsPages" row WHERE row."TenantId" = @tenantId), '')) AS "CmsPages",
          md5(COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "Products" row WHERE row."TenantId" = @tenantId), '')) AS "Products",
          md5(COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "StoreConfigs" row WHERE row."TenantId" <> @tenantId), '')) AS "OtherTenantStoreConfigs",
          md5(COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "HomepageSections" row WHERE row."TenantId" <> @tenantId), '')) AS "OtherTenantHomepage",
          md5(COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "CmsPages" row WHERE row."TenantId" <> @tenantId), '')) AS "OtherTenantCmsPages",
          md5(COALESCE((SELECT string_agg(to_jsonb(row)::text, '|' ORDER BY row."Id") FROM "Products" row WHERE row."TenantId" <> @tenantId), '')) AS "OtherTenantProducts"
        """, tenantId);
}

static async Task<JsonObject> CaptureBaselineAsync(string connectionString, Guid tenantId)
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
            SELECT "Id", "Name", "Slug", "IsActive", "Status", "DeletedAtUtc", "Currency", "Country"
            FROM "Tenants" WHERE "Id" = @tenantId
            """, tenantId),
        ["storeConfig"] = await ReadSingleAsync(connection, transaction, """
            SELECT "Id", "TenantId", "StoreName", "StoreDescription", "ThemeId",
                   "Theme_Colors_Primary", "Theme_Colors_Secondary", "Theme_Colors_Background",
                   "Theme_Colors_Text", "Theme_Colors_Muted", "Theme_Font_Family", "Theme_Font_BaseSize",
                   "Theme_Ui_BorderRadius", "Theme_Ui_ButtonStyle", "Theme_Layout_ProductCardVariant",
                   "Theme_Layout_HeaderVariant", "StorefrontStatus", "CustomizationJson",
                   "ThemePresetKey", "ThemeLivePresetKey", "ThemeDraftOverridesJson", "ThemeLiveOverridesJson",
                   "VersionsJson", "LiveVersion", "CreatedAtUtc", "UpdatedAtUtc", xmin::text AS "RowVersion",
                   md5(to_jsonb("StoreConfigs")::text) AS "CompleteRowHash"
            FROM "StoreConfigs" WHERE "TenantId" = @tenantId
            """, tenantId),
        ["themePresets"] = await ReadManyAsync(connection, transaction, """
            SELECT "Id", "Key", "Name", "BaseThemeJson", "IsRtl", "PreviewImageUrl", "CreatedAtUtc"
            FROM "ThemePresets" ORDER BY "Key", "Id"
            """, tenantId, includeTenantParameter: false),
        ["authorityFingerprints"] = await ReadAuthorityFingerprintsAsync(connection, transaction, tenantId),
    };

    await transaction.RollbackAsync();
    snapshot["publicStoreConfig"] = await ReadPublicJsonAsync("/api/storefront/store-config");
    snapshot["publicNavigation"] = await ReadPublicJsonAsync("/api/storefront/navigation?lang=en");
    snapshot["publicFooterEn"] = await ReadPublicJsonAsync("/api/storefront/footer?lang=en");
    snapshot["publicFooterAr"] = await ReadPublicJsonAsync("/api/storefront/footer?lang=ar");
    return snapshot;
}

static async Task<JsonObject?> ReadSingleAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string sql,
    Guid tenantId)
{
    var rows = await ReadManyAsync(connection, transaction, sql, tenantId);
    return rows.Count == 0 ? null : rows[0]!.AsObject().DeepClone().AsObject();
}

static async Task<JsonArray> ReadManyAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string sql,
    Guid tenantId,
    bool includeTenantParameter = true)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    if (includeTenantParameter)
    {
        command.Parameters.AddWithValue("tenantId", tenantId);
    }

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

static async Task<JsonObject> ReadPublicJsonAsync(string path)
{
    using var http = new HttpClient { BaseAddress = new Uri(StorefrontTransportUrl) };
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    request.Headers.Host = StorefrontHost;
    request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"GET {StorefrontHost}{path} returned {(int)response.StatusCode}: {text}");
    }

    return ParseObject(text);
}

static async Task<string> ReadConnectionStringAsync(string settingsPath)
{
    using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
    return settings.RootElement
        .GetProperty("ConnectionStrings")
        .GetProperty("DefaultConnection")
        .GetString() ?? throw new InvalidOperationException("DefaultConnection is missing.");
}

static JsonObject ParseObject(string json)
{
    return JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidOperationException("Expected a JSON object response.");
}

static string Serialize(JsonNode node)
{
    return node.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    });
}

static int ParsePixelNumber(string? value)
{
    return int.Parse((value ?? string.Empty).Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase));
}

static string HexToRgb(string color)
{
    var hex = color.TrimStart('#');
    if (hex.Length == 3)
    {
        hex = string.Concat(hex.Select(character => $"{character}{character}"));
    }
    return $"{Convert.ToInt32(hex[..2], 16)}, {Convert.ToInt32(hex[2..4], 16)}, {Convert.ToInt32(hex[4..6], 16)}";
}

static bool EqualsIgnoreCase(JsonObject value, string property, string expected)
{
    return string.Equals(value[property]?.GetValue<string>(), expected, StringComparison.OrdinalIgnoreCase);
}

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record BrowserFetchResult(int Status, string Text);

internal sealed record PreviewScenario(
    string Name,
    string Language,
    string Locale,
    int Width,
    int Height,
    string Direction);

internal sealed record ThemeCandidate(
    string Primary,
    string Secondary,
    string Background,
    string Text,
    string Muted,
    string Font,
    string BaseSize,
    int BorderRadius)
{
    public JsonObject ToRequest() => new()
    {
        ["presetKey"] = "default",
        ["colors"] = new JsonObject
        {
            ["primary"] = Primary,
            ["secondary"] = Secondary,
            ["background"] = Background,
            ["text"] = Text,
            ["muted"] = Muted,
        },
        ["typography"] = new JsonObject
        {
            ["fontFamily"] = Font,
            ["baseSize"] = BaseSize,
        },
        ["borderRadius"] = BorderRadius,
    };

    public JsonObject ToJson() => new()
    {
        ["colors"] = new JsonObject
        {
            ["primary"] = Primary,
            ["secondary"] = Secondary,
            ["background"] = Background,
            ["text"] = Text,
            ["muted"] = Muted,
        },
        ["font"] = Font,
        ["baseSize"] = BaseSize,
        ["borderRadius"] = BorderRadius,
    };
}

using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using System.Collections.Concurrent;
using System.Timers;

namespace PointOfSale.Infrastructure.WhatsApp.Engine;

/// <summary>
/// IMPROVED VERSION with better reliability and error handling
/// Key improvements:
/// 1. Simplified message sending flow
/// 2. Better selector fallbacks
/// 3. More lenient verification
/// 4. Detailed error logging
/// </summary>
public class PlaywrightWhatsAppEngine : IWhatsAppWebEngine, IAsyncDisposable
{
    private readonly IWhatsAppSessionManager _sessionManager;
    private readonly ILogger<PlaywrightWhatsAppEngine> _logger;
    private readonly string _userDataBaseDir;
    
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly ConcurrentDictionary<Guid, IBrowserContext> _contexts = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastActivity = new();
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private readonly System.Timers.Timer _evictionTimer;

    private const int MaxActiveContexts = 100;
    private static readonly TimeSpan IdleEvictionTimeout = TimeSpan.FromMinutes(5);  // Reduced from 10 to 5 minutes to save memory
    private static readonly TimeSpan EvictionCheckInterval = TimeSpan.FromMinutes(1); // Check every minute

    // UPDATED SELECTORS (January 2026)
    private static readonly string[] QrCanvasSelectors = 
    {
        "canvas[aria-label*='Scan']",
        "div[data-ref] canvas",
        "canvas",
    };
    
    // More comprehensive search box selectors
    private static readonly string[] SearchBoxSelectors = 
    {
        "div[contenteditable='true'][data-tab='3']",
        "div[title='Search input textbox']",
        "div[role='textbox'][data-tab='3']",
        "p[class*='selectable-text copyable-text']"
    };
    
    // Message input selectors
    private static readonly string[] MessageInputSelectors = 
    {
        "div[contenteditable='true'][data-tab='10']",
        "div[contenteditable='true'][role='textbox']",
        "footer div[contenteditable='true']",
        "div[data-tab='10']"
    };
    
    // Send button selectors
    private static readonly string[] SendButtonSelectors =
    {
        "button[aria-label*='Send']",
        "span[data-icon='send']",
        "button[data-tab='11']",
        "[data-testid='compose-btn-send']"
    };

    public PlaywrightWhatsAppEngine(
        IWhatsAppSessionManager sessionManager,
        ILogger<PlaywrightWhatsAppEngine> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
        
        _userDataBaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PointOfSale",
            "WhatsAppSessions"
        );
        
        Directory.CreateDirectory(_userDataBaseDir);
        _logger.LogInformation("WhatsApp session data directory: {Dir}", _userDataBaseDir);
        
        _evictionTimer = new System.Timers.Timer(EvictionCheckInterval.TotalMilliseconds);
        _evictionTimer.Elapsed += async (s, e) => await EvictIdleContextsAsync();
        _evictionTimer.AutoReset = true;
        _evictionTimer.Start();
    }

    private async Task<IPage> GetOrCreatePageAsync(Guid accountId)
    {
        // Check if we have an existing context for this account
        if (_contexts.TryGetValue(accountId, out var existingContext))
        {
            try
            {
                // Try to use the existing context
                if (existingContext.Pages.Count > 0)
                    return existingContext.Pages[0];
                    
                return await existingContext.NewPageAsync();
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("closed"))
            {
                // Context was closed/crashed - remove it and create a new one
                _logger.LogWarning("Account {AccountId} context was closed, removing and recreating", accountId);
                _contexts.TryRemove(accountId, out _);
                _lastActivity.TryRemove(accountId, out _);
            }
        }

        if (_playwright == null)
        {
            await _browserLock.WaitAsync();
            try
            {
                if (_playwright == null)
                {
                    _playwright = await Playwright.CreateAsync();
                    _logger.LogInformation("Playwright initialized");
                }
            }
            finally
            {
                _browserLock.Release();
            }
        }

        _logger.LogInformation("Creating persistent browser context for Account {AccountId}", accountId);
        
        var userDataDir = Path.Combine(_userDataBaseDir, accountId.ToString());
        Directory.CreateDirectory(userDataDir);
        
        if (_contexts.Count >= MaxActiveContexts)
        {
            _logger.LogWarning("Context limit reached, evicting oldest");
            await EvictOldestContextAsync();
        }
        
        // Minimal Chromium flags - only safe ones that don't cause crashes
        // Note: --disable-gpu and --single-process cause crashes on Windows with visible browser
        var chromiumArgs = new[]
        {
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-blink-features=AutomationControlled",
            
            // Safe memory optimizations only
            "--disable-extensions",                    // No extensions needed
            "--disable-sync",                          // Disable Chrome Sync
            "--disable-translate",                     // No translation needed
            "--no-first-run",                          // Skip first run
            "--disable-default-apps",                  // No default apps
            
            // Cache optimizations (safe)
            "--disk-cache-size=52428800",              // 50MB disk cache
            "--media-cache-size=52428800",             // 50MB media cache
        };
        
        var context = await _playwright.Chromium.LaunchPersistentContextAsync(
            userDataDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false, // WhatsApp requires visible browser for QR scanning
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
                Args = chromiumArgs
            });
        
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        
        _contexts.TryAdd(accountId, context);
        _lastActivity[accountId] = DateTime.UtcNow;
        
        _logger.LogInformation("Context created | Active: {Count}/{Max}", _contexts.Count, MaxActiveContexts);
        
        return page;
    }

    public async Task<QrCodeResult> GenerateQrCodeAsync(Guid accountId, CancellationToken ct = default)
    {
        try
        {
            var page = await GetOrCreatePageAsync(accountId);
            await page.GotoAsync("https://web.whatsapp.com", new PageGotoOptions { Timeout = 60000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 30000 });

            // Check if already logged in
            try 
            {
                var chatList = await page.WaitForSelectorAsync("#pane-side", new PageWaitForSelectorOptions { Timeout = 5000 });
                if (chatList != null)
                {
                    return new QrCodeResult(false, null, "Already logged in", 0);
                }
            }
            catch { }

            // Look for QR
            IElementHandle? qrElement = null;
            foreach (var selector in QrCanvasSelectors)
            {
                try
                {
                    qrElement = await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = 5000 });
                    if (qrElement != null)
                    {
                        _logger.LogInformation("Found QR with selector: {Selector}", selector);
                        break;
                    }
                }
                catch { }
            }

            if (qrElement == null)
            {
                return new QrCodeResult(false, null, "QR Code element not found", 0);
            }

            var qrBytes = await qrElement.ScreenshotAsync(new ElementHandleScreenshotOptions { Type = ScreenshotType.Png });
            var base64 = $"data:image/png;base64,{Convert.ToBase64String(qrBytes)}";

            return new QrCodeResult(true, base64, null, 60);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating QR for Account {AccountId}", accountId);
            return new QrCodeResult(false, null, ex.Message, 0);
        }
    }

    public async Task<ConnectionStatus> CheckConnectionAsync(Guid accountId, CancellationToken ct = default)
    {
        try
        {
            IPage page;
            if (!_contexts.TryGetValue(accountId, out var context))
            {
                page = await GetOrCreatePageAsync(accountId);
                
                var currentUrl = page.Url;
                if (string.IsNullOrEmpty(currentUrl) || !currentUrl.Contains("web.whatsapp.com"))
                {
                    await page.GotoAsync("https://web.whatsapp.com", new PageGotoOptions { Timeout = 60000 });
                }
                
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 15000 });
            }
            else
            {
                page = context.Pages.FirstOrDefault()!;
                if (page == null) 
                {
                    page = await context.NewPageAsync();
                    await page.GotoAsync("https://web.whatsapp.com", new PageGotoOptions { Timeout = 60000 });
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 15000 });
                }
            }

            // Check if logged in
            var isLoggedIn = await page.EvaluateAsync<bool>(@"() => {
                return document.querySelector('#pane-side') !== null || 
                       document.querySelector('[data-testid=""chat-list""]') !== null;
            }");
            
            if (isLoggedIn)
            {
                TouchActivity(accountId);
                return new ConnectionStatus(
                    true,
                    WhatsAppAccountStatus.Connected,
                    DateTime.UtcNow,
                    await TryGetConnectedPhoneAsync(page));
            }

            // Check if QR is visible
            var hasQR = await page.EvaluateAsync<bool>(@"() => {
                return document.querySelector('canvas') !== null;
            }");
            
            if (hasQR)
            {
                return new ConnectionStatus(false, WhatsAppAccountStatus.PendingQR, DateTime.UtcNow);
            }

            return new ConnectionStatus(false, WhatsAppAccountStatus.Disconnected, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking connection for Account {AccountId}", accountId);
            throw;
        }
    }

    private async Task<string?> TryGetConnectedPhoneAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string?>(@"() => {
                const digits = (value) => String(value || '').replace(/\D/g, '');
                const candidates = [];

                if (window.Store?.Conn?.wid?.user) {
                    candidates.push(window.Store.Conn.wid.user);
                }

                if (window.Store?.Conn?.me?.user) {
                    candidates.push(window.Store.Conn.me.user);
                }

                if (window.Store?.MaybeMeUser?.getMaybeMeUser) {
                    candidates.push(window.Store.MaybeMeUser.getMaybeMeUser());
                }

                for (const candidate of candidates) {
                    const value = digits(candidate);
                    if (value) {
                        return '+' + value;
                    }
                }

                return null;
            }");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to attest connected WhatsApp phone from Playwright session");
            return null;
        }
    }

    public async Task<SendResult> SendMessageAsync(Guid accountId, string phoneNumber, string message, CancellationToken ct = default)
    {
        IPage? page = null;
        try
        {
            // Step 1: Verify connection
            var connectionStatus = await CheckConnectionAsync(accountId, ct);
            if (!connectionStatus.IsConnected)
            {
                return new SendResult(false, null, "Account is not connected");
            }

            page = await GetOrCreatePageAsync(accountId);
            
            // Step 2: Navigate to WhatsApp if not already there
            var currentUrl = page.Url;
            if (string.IsNullOrEmpty(currentUrl) || !currentUrl.Contains("web.whatsapp.com"))
            {
                _logger.LogDebug("Navigating to WhatsApp Web");
                await page.GotoAsync("https://web.whatsapp.com", new PageGotoOptions { Timeout = 60000 });
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(2000); // Give UI time to render
            }

            // Step 3: Dismiss any popups
            await DismissAllPopupsAsync(page);
            
            // Step 4: Clean phone number
            var cleanPhone = phoneNumber.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();
            _logger.LogInformation("Sending message to {Phone}", cleanPhone);

            // Step 5: SIMPLIFIED APPROACH - Use search box to find contact
            var searchSuccess = await OpenChatViaSearchAsync(page, cleanPhone);
            
            if (!searchSuccess)
            {
                // Fallback: Try direct URL approach
                _logger.LogWarning("Search failed, trying direct URL");
                await page.GotoAsync($"https://web.whatsapp.com/send?phone={cleanPhone}", 
                    new PageGotoOptions { Timeout = 30000, WaitUntil = WaitUntilState.DOMContentLoaded });
                await Task.Delay(3000);
            }
            
            // Step 6: Wait for message input to appear
            var messageInput = await WaitForMessageInputAsync(page);
            if (messageInput == null)
            {
                await SaveDiagnosticScreenshotAsync(page, accountId, "no_message_input");
                return new SendResult(false, null, "Could not find message input box");
            }

            // Step 7: Type the message
            _logger.LogDebug("Typing message");
            await messageInput.ClickAsync();
            await Task.Delay(500);
            await messageInput.TypeAsync(message, new ElementHandleTypeOptions { Delay = 50 });
            await Task.Delay(1000);

            // Step 8: Send the message
            var sent = await SendMessageViaMultipleMethods(page);
            
            if (!sent)
            {
                await SaveDiagnosticScreenshotAsync(page, accountId, "send_failed");
                return new SendResult(false, null, "Failed to send message");
            }

            // Step 9: Wait a bit and verify (lenient verification)
            await Task.Delay(2000);
            
            var verified = await VerifyMessageSentAsync(page);
            if (verified)
            {
                _logger.LogInformation("✅ Message sent and verified for {Phone}", cleanPhone);
            }
            else
            {
                _logger.LogWarning("⚠️ Message sent but could not verify delivery for {Phone}", cleanPhone);
            }

            TouchActivity(accountId);
            return new SendResult(true, Guid.NewGuid().ToString(), null);
        }
        catch (Exception ex)
        {
            if (page != null)
            {
                try { await SaveDiagnosticScreenshotAsync(page, accountId, "error"); } catch { }
            }
            
            _logger.LogError(ex, "Error sending message to {Phone}", phoneNumber);
            return new SendResult(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Open a chat using the search box (more reliable than direct URL)
    /// </summary>
    private async Task<bool> OpenChatViaSearchAsync(IPage page, string phoneNumber)
    {
        try
        {
            _logger.LogDebug("Opening chat via search for {Phone}", phoneNumber);
            
            // Find and click the search box
            IElementHandle? searchBox = null;
            foreach (var selector in SearchBoxSelectors)
            {
                try
                {
                    searchBox = await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = 5000 });
                    if (searchBox != null && await searchBox.IsVisibleAsync())
                    {
                        _logger.LogDebug("Found search box: {Selector}", selector);
                        break;
                    }
                }
                catch { }
            }

            if (searchBox == null)
            {
                _logger.LogWarning("Could not find search box");
                return false;
            }

            // Click and type phone number
            await searchBox.ClickAsync();
            await Task.Delay(500);
            await searchBox.TypeAsync(phoneNumber, new ElementHandleTypeOptions { Delay = 100 });
            await Task.Delay(2000); // Wait for search results

            // Press Enter to open first result
            await page.Keyboard.PressAsync("Enter");
            await Task.Delay(2000);

            // Check if message input appeared
            var messageInputVisible = await page.EvaluateAsync<bool>(@"() => {
                const selectors = [
                    'div[contenteditable=""true""][data-tab=""10""]',
                    'footer div[contenteditable=""true""]'
                ];
                return selectors.some(sel => {
                    const el = document.querySelector(sel);
                    return el && el.offsetParent !== null;
                });
            }");

            return messageInputVisible;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error in OpenChatViaSearchAsync");
            return false;
        }
    }

    /// <summary>
    /// Wait for message input box to appear
    /// </summary>
    private async Task<IElementHandle?> WaitForMessageInputAsync(IPage page, int timeoutMs = 15000)
    {
        var startTime = DateTime.UtcNow;
        
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            foreach (var selector in MessageInputSelectors)
            {
                try
                {
                    var element = await page.QuerySelectorAsync(selector);
                    if (element != null && await element.IsVisibleAsync())
                    {
                        _logger.LogDebug("Found message input: {Selector}", selector);
                        return element;
                    }
                }
                catch { }
            }
            
            await Task.Delay(500);
        }
        
        _logger.LogWarning("Message input not found after {Timeout}ms", timeoutMs);
        return null;
    }

    /// <summary>
    /// Try multiple methods to send the message
    /// </summary>
    private async Task<bool> SendMessageViaMultipleMethods(IPage page)
    {
        // Method 1: Click send button
        _logger.LogDebug("Trying to click send button");
        foreach (var selector in SendButtonSelectors)
        {
            try
            {
                var button = await page.QuerySelectorAsync(selector);
                if (button != null && await button.IsVisibleAsync())
                {
                    await button.ClickAsync(new ElementHandleClickOptions { Force = true });
                    _logger.LogDebug("Clicked send button: {Selector}", selector);
                    return true;
                }
            }
            catch { }
        }

        // Method 2: JavaScript click
        _logger.LogDebug("Trying JavaScript click");
        var jsClicked = await page.EvaluateAsync<bool>(@"() => {
            const sendBtn = document.querySelector('span[data-icon=""send""]') ||
                            document.querySelector('button[aria-label*=""Send""]');
            if (sendBtn) {
                const btn = sendBtn.closest('button') || sendBtn;
                btn.click();
                return true;
            }
            return false;
        }");
        
        if (jsClicked)
        {
            _logger.LogDebug("Sent via JavaScript");
            return true;
        }

        // Method 3: Press Enter key
        _logger.LogDebug("Trying Enter key");
        try
        {
            await page.Keyboard.PressAsync("Enter");
            _logger.LogDebug("Sent via Enter key");
            return true;
        }
        catch { }

        _logger.LogError("All send methods failed");
        return false;
    }

    /// <summary>
    /// Verify message was sent (lenient check)
    /// </summary>
    private async Task<bool> VerifyMessageSentAsync(IPage page)
    {
        try
        {
            // Check for ANY indication of sent message
            var verified = await page.EvaluateAsync<bool>(@"() => {
                // Check for tick marks
                const ticks = document.querySelectorAll('span[data-icon*=""msg-""]');
                if (ticks.length > 0) return true;
                
                // Check for outgoing message bubbles
                const messages = document.querySelectorAll('[class*=""message-out""]');
                if (messages.length > 0) return true;
                
                // Check if message input is now empty (message was sent)
                const input = document.querySelector('div[contenteditable=""true""][data-tab=""10""]');
                if (input && input.textContent.trim() === '') return true;
                
                return false;
            }");

            return verified;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Dismiss ALL popups - simplified version
    /// </summary>
    private async Task DismissAllPopupsAsync(IPage page)
    {
        try
        {
            // Press Escape multiple times
            for (int i = 0; i < 3; i++)
            {
                await page.Keyboard.PressAsync("Escape");
                await Task.Delay(300);
            }

            // Click any "OK" or "Got it" buttons
            var dismissed = await page.EvaluateAsync<bool>(@"() => {
                const buttons = document.querySelectorAll('button, div[role=""button""]');
                const dismissTexts = ['ok', 'got it', 'close', 'dismiss', 'not now', 'cancel'];
                
                for (const btn of buttons) {
                    const text = (btn.textContent || '').toLowerCase().trim();
                    if (dismissTexts.some(dt => text.includes(dt))) {
                        const rect = btn.getBoundingClientRect();
                        if (rect.width > 0 && rect.height > 0) {
                            btn.click();
                            return true;
                        }
                    }
                }
                return false;
            }");
            
            if (dismissed)
            {
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error dismissing popups (non-fatal)");
        }
    }

    private async Task SaveDiagnosticScreenshotAsync(IPage page, Guid accountId, string reason)
    {
        try
        {
            var screenshotDir = Path.Combine(_userDataBaseDir, "diagnostics");
            Directory.CreateDirectory(screenshotDir);
            
            var filename = $"{accountId:N}_{reason}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
            var filepath = Path.Combine(screenshotDir, filename);
            
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = filepath, FullPage = true });
            
            _logger.LogWarning("Screenshot saved: {Path}", filepath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to save screenshot");
        }
    }

    private void TouchActivity(Guid accountId)
    {
        _lastActivity[accountId] = DateTime.UtcNow;
    }

    private async Task EvictIdleContextsAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow - IdleEvictionTimeout;
            var toEvict = _lastActivity
                .Where(kv => kv.Value < cutoff && _contexts.ContainsKey(kv.Key))
                .Select(kv => kv.Key)
                .ToList();
            
            if (toEvict.Count > 0)
            {
                _logger.LogInformation("[WhatsApp] Evicting {Count} idle contexts (timeout: {Timeout} min)", 
                    toEvict.Count, IdleEvictionTimeout.TotalMinutes);
            }
            
            foreach (var accountId in toEvict)
            {
                await EvictContextAsync(accountId);
                _logger.LogInformation("[WhatsApp] Context evicted for idle timeout | AccountId: {AccountId}", accountId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during eviction");
        }
    }

    private async Task EvictOldestContextAsync()
    {
        try
        {
            var oldest = _lastActivity
                .Where(kv => _contexts.ContainsKey(kv.Key))
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .FirstOrDefault();
            
            if (oldest != Guid.Empty)
            {
                await EvictContextAsync(oldest);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evicting oldest context");
        }
    }

    private async Task EvictContextAsync(Guid accountId)
    {
        if (_contexts.TryRemove(accountId, out var context))
        {
            _lastActivity.TryRemove(accountId, out _);
            
            try
            {
                await context.CloseAsync();
                await context.DisposeAsync();
                _logger.LogDebug("Context evicted for {AccountId}", accountId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing context");
            }
        }
    }

    public async Task DisconnectAsync(Guid accountId, CancellationToken ct = default)
    {
        if (_contexts.TryRemove(accountId, out var context))
        {
            try 
            {
                await context.CloseAsync();
                await context.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing context");
            }
        }
        
        await _sessionManager.DeleteSessionAsync(accountId, ct);
    }

    public WhatsAppEngineDiagnostics GetDiagnostics()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        
        return new WhatsAppEngineDiagnostics
        {
            ActiveContextCount = _contexts.Count,
            MaxRecommendedContexts = MaxActiveContexts,
            ActiveAccountIds = _contexts.Keys.ToList(),
            ProcessMemoryMB = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
            IsNearMemoryLimit = _contexts.Count >= MaxActiveContexts,
            UserDataDirectory = _userDataBaseDir,
            IdleTimeoutMinutes = (int)IdleEvictionTimeout.TotalMinutes
        };
    }

    public bool CanCreateNewContext()
    {
        return _contexts.Count < MaxActiveContexts;
    }

    public async ValueTask DisposeAsync()
    {
        _evictionTimer.Stop();
        _evictionTimer.Dispose();
        
        foreach (var context in _contexts.Values)
        {
            await context.CloseAsync();
        }
        _contexts.Clear();
        _lastActivity.Clear();

        if (_browser != null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }
        
        if (_playwright != null)
        {
            _playwright.Dispose();
        }
    }
}

public class WhatsAppEngineDiagnostics
{
    public int ActiveContextCount { get; set; }
    public int MaxRecommendedContexts { get; set; }
    public List<Guid> ActiveAccountIds { get; set; } = new();
    public double ProcessMemoryMB { get; set; }
    public bool IsNearMemoryLimit { get; set; }
    public string UserDataDirectory { get; set; } = string.Empty;
    public int IdleTimeoutMinutes { get; set; }
}

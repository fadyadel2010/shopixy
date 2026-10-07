using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Infrastructure.Data;

// Read-only forensic trace. Never writes SQL/Mongo or acknowledges broker messages.
var root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var detailed = !args.Contains("--checkpoint");
var configuration = new ConfigurationBuilder()
    .SetBasePath(Path.Combine(root, "PointOfSale.API"))
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "UserSecrets", "bbdf2e74-8790-4753-8874-f20fcfbdbbd5", "secrets.json"), optional: true)
    .AddJsonFile("appsettings.Development.local.json", optional: true)
    .AddEnvironmentVariables().Build();
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(configuration.GetConnectionString("DefaultConnection")).Options;
await using var db = new ApplicationDbContext(options);
var configs = await db.StoreConfigs.IgnoreQueryFilters().AsNoTracking()
    .Where(c => c.StoreName == "AhramStore").ToListAsync();
if (args.Contains("--foreign-media"))
{
    var tenant = configs.Single().TenantId;
    var foreign = await db.HomepageMediaAssets.IgnoreQueryFilters().AsNoTracking()
        .Where(a => a.TenantId != tenant).Select(a => new { a.Id, a.TenantId }).FirstOrDefaultAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { source = "foreign-media-read-only", foreign }));
}
var checkpoints = new List<object>();
foreach (var c in configs)
{
    using var state = JsonDocument.Parse(c.CustomizationJson);
    var home = state.RootElement.GetProperty("pages").GetProperty("home");
    if (detailed) Console.WriteLine(JsonSerializer.Serialize(new { source = "SQL", c.Id, c.TenantId, c.LiveVersion, c.UpdatedAtUtc,
        live = home.GetProperty("live"), draft = home.GetProperty("draft"), navigation = state.RootElement.GetProperty("navigation") }));
    var processed = await db.ProcessedEvents.IgnoreQueryFilters().AsNoTracking()
        .Where(p => p.TenantId == c.TenantId && p.ConsumerName == "StorefrontConfigProjectionConsumer")
        .OrderByDescending(p => p.ProcessedAtUtc).Take(8).ToListAsync();
    if (detailed) Console.WriteLine(JsonSerializer.Serialize(new { source = "fences", processed = processed.Select(p => new { p.EventId, p.EventType, p.ProcessedAtUtc, p.EntityId, p.TenantId }) }));
    var mongo = new MongoClient(configuration["MongoDB:ConnectionString"])
        .GetDatabase(configuration["MongoDB:DatabaseName"]);
    var documents = await mongo.GetCollection<BsonDocument>("storefront_configs")
        .Find(new BsonDocument("TenantId", c.TenantId.ToString())).ToListAsync();
    if (args.Contains("--wait"))
    {
        var expectedImage = home.GetProperty("live").GetProperty("sections")[0].GetProperty("settings").GetProperty("backgroundImage").GetString();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (true)
        {
            var document = documents.SingleOrDefault();
            var image = document?["Pages"]["Home"]["Sections"][0]["Settings"]["backgroundImage"].AsString;
            if (document != null && document.GetValue("LiveVersion", -1).AsInt32 == c.LiveVersion && image == expectedImage) break;
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Real StoreConfig consumer did not converge within 45 seconds.");
            await Task.Delay(500);
            documents = await mongo.GetCollection<BsonDocument>("storefront_configs").Find(new BsonDocument("TenantId", c.TenantId.ToString())).ToListAsync();
        }
    }
    if (detailed) Console.WriteLine("Mongo database=" + mongo.DatabaseNamespace.DatabaseName);
    foreach (var document in documents) if (detailed) Console.WriteLine(document.ToJson());
    foreach (var document in await mongo.GetCollection<BsonDocument>("storefront_navigation").Find(new BsonDocument("TenantId", c.TenantId.ToString())).ToListAsync())
        if (detailed) Console.WriteLine("Mongo navigation: " + document.ToJson());
    Console.WriteLine(JsonSerializer.Serialize(new { source = "convergence-checkpoint", sqlVersion = c.LiveVersion,
        mongoVersion = documents.SingleOrDefault()?.GetValue("LiveVersion", -1).AsInt32,
        eventId = documents.SingleOrDefault()?.GetValue("LastEventId").AsString,
        mongoUpdatedAt = documents.SingleOrDefault()?.GetValue("UpdatedAt").ToUniversalTime() }));
    checkpoints.Add(new { sql = new { c.Id, c.TenantId, c.LiveVersion, c.UpdatedAtUtc, customization = state.RootElement.Clone() },
        mongo = documents.Select(d => JsonDocument.Parse(d.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson })).RootElement.Clone()).ToArray(),
        processed = processed.Select(p => new { p.EventId, p.EventType, p.ProcessedAtUtc }).ToArray() });
}
var checkpointIndex = Array.IndexOf(args, "--checkpoint");
if (checkpointIndex >= 0)
{
    var name = args[checkpointIndex + 1];
    if (name.Any(c => !char.IsLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid checkpoint name");
    await File.WriteAllTextAsync(Path.Combine(root, ".artifacts", $"storeconfig-trace-{name}.json"), JsonSerializer.Serialize(checkpoints, new JsonSerializerOptions { WriteIndented = true }));
}
using var http = new HttpClient { BaseAddress = new Uri("http://" + configuration["RabbitMQ:Host"] + ":15672/"), Timeout = TimeSpan.FromSeconds(20) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
    Encoding.UTF8.GetBytes(configuration["RabbitMQ:Username"] + ":" + configuration["RabbitMQ:Password"])));
foreach (var endpoint in new[] { "api/queues", "api/bindings", "api/consumers" })
{
    using var data = JsonDocument.Parse(await http.GetStringAsync(endpoint));
    var rows = data.RootElement.EnumerateArray().Where(e => e.ToString().Contains("storefront-config-projection", StringComparison.OrdinalIgnoreCase));
    foreach (var row in rows)
    {
        if (endpoint == "api/queues") Console.WriteLine(JsonSerializer.Serialize(new { source = endpoint,
            name = row.GetProperty("name").GetString(), vhost = row.GetProperty("vhost").GetString(),
            messages = row.GetProperty("messages").GetInt32(), consumers = row.GetProperty("consumers").GetInt32() }));
        else if (detailed) Console.WriteLine(endpoint + ": " + row);
    }
}
if (args.Contains("--peek-legacy"))
{
    using var response = await http.PostAsJsonAsync("api/queues/%2F/storefront-config-projection/get", new { count = 300, ackmode = "ack_requeue_true", encoding = "auto", truncate = 50000 });
    response.EnsureSuccessStatusCode();
    using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    foreach (var row in data.RootElement.EnumerateArray())
    {
        using var envelope = JsonDocument.Parse(row.GetProperty("payload").GetString()!);
        var message = envelope.RootElement.GetProperty("message");
        Console.WriteLine(JsonSerializer.Serialize(new { source = "legacy-preserved-peek", types = envelope.RootElement.GetProperty("messageType"),
            eventId = message.TryGetProperty("eventId", out var id) ? id.ToString() : null,
            tenant = message.TryGetProperty("tenantId", out var tenant) ? tenant.ToString() : null,
            occurred = message.TryGetProperty("occurredAtUtc", out var at) ? at.ToString() : null,
            schema = message.TryGetProperty("eventVersion", out var version) ? version.ToString() : null }));
    }
}

using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Tests.Diagnostics;

public sealed class NpgsqlLifecycleDiagnosticActivityTests
{
    private const string SourceName = "PointOfSale.Npgsql.Diagnostics";
    private const string SourceVersion = "1.0.0";
    private const string CommandActivityName = "Npgsql.Command.Execute";

    [Fact]
    public void ReaderCommand_EmitsNpgsqlCommandActivityWithExpectedNameKindAndTags()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(
            telemetry,
            new NpgsqlDiagnosticsOptions
            {
                CaptureCommandTextInActivities = true,
                CaptureConnectionIdentityInActivities = true
            });
        using var capture = new ActivityCapture();
        var command = NewNpgsqlCommand("SELECT id, name\nFROM products");
        var commandId = Guid.NewGuid();

        interceptor.ReaderExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteReader), default);
        interceptor.ReaderExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(120), DbCommandMethod.ExecuteReader),
            null!);
        interceptor.DataReaderDisposing(command, NpgsqlDiagnosticEventData.Disposing(command, commandId), default);

        var activity = Assert.Single(capture.Stopped);
        Assert.Equal(CommandActivityName, activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal(SourceName, activity.Source.Name);
        Assert.Equal(SourceVersion, activity.Source.Version);
        Assert.Equal("postgresql", Tag(activity, "db.system"));
        Assert.Equal("unknown", Tag(activity, "db.connection.id"));
        Assert.Equal("SELECT", Tag(activity, "db.operation"));
        Assert.Equal("SELECT id, name FROM products", Tag(activity, "db.statement"));
        Assert.Equal(120d, Tag(activity, "db.command.duration_ms"));
        Assert.Equal(false, Tag(activity, "db.command.error"));
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public void NonReaderCommand_StopsItsActivityAndReleasesTheExecutingCommandCount()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(telemetry);
        using var capture = new ActivityCapture();
        var command = NewNpgsqlCommand("UPDATE products SET name = 'x'");
        var commandId = Guid.NewGuid();

        interceptor.NonQueryExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteNonQuery), default);
        Assert.Equal(1, telemetry.GetSnapshot().ExecutingCommands);

        interceptor.NonQueryExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(37), DbCommandMethod.ExecuteNonQuery),
            1);

        var activity = Assert.Single(capture.Stopped);
        Assert.Equal("UPDATE", Tag(activity, "db.operation"));
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal(0, telemetry.GetSnapshot().ExecutingCommands);
    }

    [Fact]
    public void SlowCommand_AddsSlowCommandTagsToTheActivity()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(telemetry);
        using var capture = new ActivityCapture();
        var command = NewNpgsqlCommand("SELECT count(*) FROM orders");
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        interceptor.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(2500), DbCommandMethod.ExecuteScalar),
            42);

        var activity = Assert.Single(capture.Stopped);
        Assert.Equal(true, Tag(activity, "db.command.slow"));
        Assert.Equal(2500d, Tag(activity, "db.command.total_duration_ms"));
        Assert.Equal(2500d, Tag(activity, "db.command.duration_ms"));
        Assert.Equal(false, Tag(activity, "db.command.error"));
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public void FastCommand_DoesNotAddSlowCommandTags()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(telemetry);
        using var capture = new ActivityCapture();
        var command = NewNpgsqlCommand("SELECT 1");
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        interceptor.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(1500), DbCommandMethod.ExecuteScalar),
            1);

        var activity = Assert.Single(capture.Stopped);
        Assert.Null(Tag(activity, "db.command.slow"));
        Assert.Null(Tag(activity, "db.command.total_duration_ms"));
    }

    [Fact]
    public void FailedCommand_SetsActivityStatusToErrorWithoutRecordingExceptionDetails()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(telemetry);
        using var capture = new ActivityCapture();
        var command = NewNpgsqlCommand("SELECT * FROM missing_table");
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        interceptor.CommandFailed(
            command,
            NpgsqlDiagnosticEventData.Failed(
                command,
                commandId,
                TimeSpan.FromMilliseconds(90),
                DbCommandMethod.ExecuteScalar,
                new InvalidOperationException("relation missing_table does not exist")));

        var activity = Assert.Single(capture.Stopped);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Null(activity.StatusDescription);
        Assert.Equal(true, Tag(activity, "db.command.error"));
        Assert.Equal(90d, Tag(activity, "db.command.duration_ms"));
    }

    [Fact]
    public void DefaultOptions_DoNotAttachPotentiallySensitiveCommandOrConnectionValues()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(telemetry);
        using var capture = new ActivityCapture();
        var command = NewNpgsqlCommand("SELECT * FROM customers WHERE email = 'secret@example.com'");
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        interceptor.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(4), DbCommandMethod.ExecuteScalar),
            null);

        var activity = Assert.Single(capture.Stopped);
        Assert.Null(Tag(activity, "db.statement"));
        Assert.Null(Tag(activity, "db.connection.id"));
    }

    [Fact]
    public void FailedCommand_DoesNotEmitAnExceptionEvent_BecauseRecordExceptionIsUnresolved()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(telemetry);
        using var capture = new ActivityCapture();
        var command = NewNpgsqlCommand("SELECT 1");
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        interceptor.CommandFailed(
            command,
            NpgsqlDiagnosticEventData.Failed(
                command,
                commandId,
                TimeSpan.FromMilliseconds(10),
                DbCommandMethod.ExecuteScalar,
                new InvalidOperationException("boom")));

        Assert.DoesNotContain(Assert.Single(capture.Stopped).Events, e => e.Name == "exception");
    }

    [Fact]
    public void NonNpgsqlCommand_IsIgnored_AndProducesNoActivity()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(telemetry);
        using var capture = new ActivityCapture();
        DbCommand command = new SqliteCommand("SELECT 1");
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        interceptor.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(5), DbCommandMethod.ExecuteScalar),
            1);

        Assert.Empty(capture.Started);
        Assert.Empty(capture.Stopped);
        Assert.Equal(0, telemetry.GetSnapshot().ExecutingCommands);
    }

    [Fact]
    public void CompletionForAnUnknownCommandId_IsANoOp()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = CreateInterceptor(telemetry);
        using var capture = new ActivityCapture();
        var command = NewNpgsqlCommand("SELECT 1");

        interceptor.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, Guid.NewGuid(), TimeSpan.FromMilliseconds(5), DbCommandMethod.ExecuteScalar),
            1);

        Assert.Empty(capture.Started);
        Assert.Empty(capture.Stopped);
        Assert.Equal(0, telemetry.GetSnapshot().ExecutingCommands);
    }

    [Fact]
    public void Interceptor_RemainsScoped_SoEachScopeGetsItsOwnInstance()
    {
        using var provider = BuildProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();
        var b = second.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();

        Assert.NotSame(a, b);
        Assert.Same(a, first.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>());
    }

    [Fact]
    public void CommandTimingState_IsNotSharedBetweenScopes()
    {
        using var provider = BuildProvider();
        var commandsField = typeof(NpgsqlLifecycleDiagnosticInterceptor)
            .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(commandsField);

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();
        var b = second.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();

        Assert.NotSame(commandsField.GetValue(a), commandsField.GetValue(b));
    }

    [Fact]
    public void ACommandStartedInOneScope_CannotBeCompletedByAnotherScope()
    {
        using var provider = BuildProvider();
        var telemetry = provider.GetRequiredService<NpgsqlDiagnosticsTelemetry>();
        using var capture = new ActivityCapture();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();
        var b = second.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();
        var command = NewNpgsqlCommand("SELECT 1");
        var commandId = Guid.NewGuid();

        a.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        b.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(5), DbCommandMethod.ExecuteScalar),
            1);

        Assert.Equal(1, telemetry.GetSnapshot().ExecutingCommands);
        Assert.Empty(capture.Stopped);

        a.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(5), DbCommandMethod.ExecuteScalar),
            1);

        Assert.Equal(0, telemetry.GetSnapshot().ExecutingCommands);
        Assert.Single(capture.Stopped);
    }

    private static NpgsqlLifecycleDiagnosticInterceptor CreateInterceptor(
        NpgsqlDiagnosticsTelemetry telemetry,
        NpgsqlDiagnosticsOptions? options = null) =>
        new(
            NullLogger<NpgsqlLifecycleDiagnosticInterceptor>.Instance,
            telemetry,
            options is null ? null : Microsoft.Extensions.Options.Options.Create(options));

    private static ServiceProvider BuildProvider() => new ServiceCollection()
        .AddLogging()
        .AddSingleton<NpgsqlDiagnosticsTelemetry>()
        .AddScoped<NpgsqlLifecycleDiagnosticInterceptor>()
        .BuildServiceProvider();

    internal static NpgsqlCommand NewNpgsqlCommand(string commandText) =>
        new(commandText, (NpgsqlConnection)null!);

    internal static object? Tag(Activity activity, string key) =>
        activity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, key, StringComparison.Ordinal)).Value;

    internal sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivityCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity => Started.Add(activity),
                ActivityStopped = activity => Stopped.Add(activity)
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Started { get; } = [];
        public List<Activity> Stopped { get; } = [];

        public void Dispose() => _listener.Dispose();
    }
}

internal static class NpgsqlDiagnosticEventData
{
    private static readonly Func<EventDefinitionBase, EventData, string> MessageGenerator = (_, _) => "m46";

    public static CommandEventData Start(DbCommand command, Guid commandId, DbCommandMethod method) => new(
        null!, MessageGenerator, null!, command, null!, method, commandId, Guid.NewGuid(), false, false,
        DateTimeOffset.UtcNow, CommandSource.LinqQuery);

    public static CommandExecutedEventData Executed(
        DbCommand command,
        Guid commandId,
        TimeSpan duration,
        DbCommandMethod method) => new(
        null!, MessageGenerator, null!, command, null!, method, commandId, Guid.NewGuid(), null, false, false,
        DateTimeOffset.UtcNow, duration, CommandSource.LinqQuery);

    public static CommandErrorEventData Failed(
        DbCommand command,
        Guid commandId,
        TimeSpan duration,
        DbCommandMethod method,
        Exception exception) => new(
        null!, MessageGenerator, null!, command, null!, method, commandId, Guid.NewGuid(), exception, false, false,
        DateTimeOffset.UtcNow, duration, CommandSource.LinqQuery);

    public static CommandEndEventData Cancelled(
        DbCommand command,
        Guid commandId,
        TimeSpan duration,
        DbCommandMethod method) => new(
        null!, MessageGenerator, null!, command, null!, method, commandId, Guid.NewGuid(), false, false,
        DateTimeOffset.UtcNow, duration, CommandSource.LinqQuery);

    public static DataReaderDisposingEventData Disposing(DbCommand command, Guid commandId) => new(
        null!, MessageGenerator, command, null!, null!, commandId, Guid.NewGuid(), 0, 0,
        DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(1));
}

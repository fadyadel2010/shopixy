using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Npgsql;

namespace PointOfSale.API.Instrumentation;

/// <summary>
/// Renders diagnostics without Exception.ToString, a virtual StackTrace getter,
/// or the CLR's reflection-heavy stack signature/async-attribute renderer.
/// A native CLR formatting failure cannot be recovered with try/catch. Keep
/// original causes and provider codes; formatting is not exception handling.
/// Redaction is applied by the console formatter to the entire resulting text.
/// </summary>
internal static class ExceptionDiagnosticFormatter
{
    private const int MaximumExceptions = 256;
    private const int MaximumFrames = 256;

    internal static string Format(Exception exception)
    {
        var output = new StringBuilder();
        var pending = new Stack<(Exception Exception, string Relation)>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push((exception, "Exception"));
        while (pending.Count > 0 && seen.Count < MaximumExceptions)
        {
            var (current, relation) = pending.Pop();
            output.Append(relation).Append(": ").Append(current.GetType().FullName);
            if (!seen.Add(current))
            {
                output.AppendLine(" [already rendered]");
                continue;
            }
            output.Append(" (HRESULT 0x").Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture)).Append(")");
            AppendValue(output, "Message", () => current.Message);
            if (current is PostgresException postgres)
            {
                AppendValue(output, "SQLSTATE", () => postgres.SqlState);
                AppendValue(output, "Severity", () => postgres.InvariantSeverity);
            }
            else if (current is SocketException socket)
            {
                output.Append("; SocketError=").Append(socket.SocketErrorCode)
                    .Append("; NativeError=").Append(socket.NativeErrorCode);
            }
            output.AppendLine();
            AppendFrames(output, current);

            // AggregateException.InnerException is only the first cause. Render
            // every branch exactly once, iteratively, rather than flatten/rewrite.
            if (current is AggregateException aggregate)
            {
                for (var i = aggregate.InnerExceptions.Count - 1; i >= 0; i--)
                    pending.Push((aggregate.InnerExceptions[i], $"InnerException[{i}]"));
            }
            else if (current is System.Reflection.ReflectionTypeLoadException load)
            {
                for (var i = load.LoaderExceptions.Length - 1; i >= 0; i--)
                    if (load.LoaderExceptions[i] is { } cause) pending.Push((cause, $"LoaderException[{i}]"));
                if (current.InnerException is { } inner) pending.Push((inner, "InnerException"));
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push((inner, "InnerException"));
            }
        }
        if (pending.Count > 0) output.AppendLine("[exception diagnostic limit reached; remaining causes not rendered]");
        return output.ToString();
    }

    private static void AppendValue(StringBuilder output, string name, Func<string?> read)
    {
        output.Append("; ").Append(name).Append('=');
        try { output.Append(read()); }
        catch (Exception error) { output.Append("[unavailable: ").Append(error.GetType().FullName).Append(']'); }
    }

    private static void AppendFrames(StringBuilder output, Exception exception)
    {
        try
        {
            // Do not call StackTrace.ToString(), MethodBase.ToString(), inspect
            // custom attributes, resolve generic signatures or read source PDBs.
            // Simple metadata names + original IL/native offsets retain useful
            // thrown locations, including async MoveNext and dynamic methods.
            var frames = new StackTrace(exception, fNeedFileInfo: false).GetFrames();
            if (frames == null) return;
            foreach (var frame in frames.Take(MaximumFrames))
            {
                output.Append("   at ");
                try
                {
                    var method = frame.GetMethod();
                    output.Append(method?.DeclaringType?.FullName).Append('.').Append(method?.Name ?? "[unknown method]");
                }
                catch (Exception error) { output.Append("[method metadata unavailable: ").Append(error.GetType().FullName).Append(']'); }
                output.Append(" [IL ").Append(frame.GetILOffset()).Append(", native ").Append(frame.GetNativeOffset()).AppendLine("]");
            }
            if (frames.Length > MaximumFrames) output.AppendLine("   [stack frame diagnostic limit reached]");
        }
        catch (Exception error)
        {
            output.Append("   [stack frames unavailable: ").Append(error.GetType().FullName).AppendLine("]");
        }
    }
}

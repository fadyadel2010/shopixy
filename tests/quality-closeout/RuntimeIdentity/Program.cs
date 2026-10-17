using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Windows x64, read-only PEB inspection. Never persist environment values or secrets.
static object Artifact(string path)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    return new { path, mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), modifiedUtc = File.GetLastWriteTimeUtc(path) };
}
if (args[0] == "artifacts")
{
    var artifacts = new[] { "PointOfSale.API.dll", "PointOfSale.Core.dll", "PointOfSale.Infrastructure.dll" }.Select(name => Artifact(Path.Combine(args[1], name)));
    var manifest = JsonSerializer.Serialize(new { capturedUtc = DateTime.UtcNow, artifacts }, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(args[2], manifest);
    Console.WriteLine(manifest);
    return;
}
var pid = int.Parse(args[1]);
using var process = Process.GetProcessById(pid);
var handle = Native.OpenProcess(0x410, false, pid);
if (handle == IntPtr.Zero) throw new InvalidOperationException("Cannot inspect API process.");
try
{
    if (Native.NtQueryInformationProcess(handle, 0, out var info, Marshal.SizeOf<Native.BasicInfo>(), out _) != 0)
        throw new InvalidOperationException("Cannot read API PEB.");
    byte[] Read(long address, int length)
    {
        var bytes = new byte[length];
        if (!Native.ReadProcessMemory(handle, (IntPtr)address, bytes, bytes.Length, out var read) || read.ToInt64() != length)
            throw new InvalidOperationException("Cannot read API process parameters.");
        return bytes;
    }
    var parameters = BitConverter.ToInt64(Read(info.PebBaseAddress.ToInt64() + 0x20, 8));
    string ReadUnicode(long address)
    {
        var value = Read(address, 16);
        return Encoding.Unicode.GetString(Read(BitConverter.ToInt64(value, 8), BitConverter.ToUInt16(value)));
    }
    var cwd = ReadUnicode(parameters + 0x38);
    var envAddress = BitConverter.ToInt64(Read(parameters + 0x80, 8));
    var envBytes = new List<byte>();
    for (var offset = 0; offset < 1024 * 1024; offset += 2)
    {
        var pair = Read(envAddress + offset, 2);
        envBytes.AddRange(pair);
        if (envBytes.Count >= 4 && envBytes.TakeLast(4).All(b => b == 0)) break;
    }
    var environment = Encoding.Unicode.GetString(envBytes.ToArray()).Split('\0', StringSplitOptions.RemoveEmptyEntries)
        .Where(s => s.IndexOf('=') > 0)
        .Select(s => (Key: s[..s.IndexOf('=')], Value: s[(s.IndexOf('=') + 1)..]))
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    var executable = process.MainModule!.FileName;
    var identity = new
    {
        capturedUtc = DateTime.UtcNow,
        pid,
        startUtc = process.StartTime.ToUniversalTime(),
        executable,
        workingDirectory = cwd,
        environment = environment.Where(p => p.Key is "ASPNETCORE_ENVIRONMENT" or "DOTNET_ENVIRONMENT" or "ASPNETCORE_URLS" or "DOTNET_LAUNCH_PROFILE").ToDictionary(),
        environmentFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(environment.OrderBy(p => p.Key))))),
        configurationFiles = Directory.GetFiles(cwd, "appsettings*.json").Select(path => new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }),
        assemblies = process.Modules.Cast<ProcessModule>().Where(m => m.ModuleName is "PointOfSale.API.dll" or "PointOfSale.Core.dll" or "PointOfSale.Infrastructure.dll").Select(m => Artifact(m.FileName)).ToArray()
    };
    var json = JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true });
    if (args.Length > 2) File.WriteAllText(args[2], json);
    Console.WriteLine(json);
}
finally { Native.CloseHandle(handle); }

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicInfo { public IntPtr Reserved1, PebBaseAddress, Reserved2A, Reserved2B, UniqueProcessId, ParentProcessId; }
    [DllImport("kernel32.dll")] internal static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int length, out IntPtr read);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll")] internal static extern int NtQueryInformationProcess(IntPtr process, int informationClass, out BasicInfo information, int length, out int returned);
}

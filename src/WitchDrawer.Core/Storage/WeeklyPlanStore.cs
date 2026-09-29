using System.Text.Json;
using System.Text.Json.Serialization;

namespace WitchDrawer.Core.Storage;

/// <summary>Serializes metadata writes and atomically replaces the previous complete document.</summary>
public sealed class WeeklyPlanStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly SemaphoreSlim _writes = new(1, 1);
    public WeeklyPlanStore(string dataDirectory) => Path = System.IO.Path.Combine(System.IO.Path.GetFullPath(dataDirectory), "weekly-plan.json");
    public string Path { get; }
    public Task<T> LoadAsync<T>(CancellationToken cancellationToken = default) where T : new() => Task.Run(async () =>
    {
        if (!File.Exists(Path)) return new T();
        await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true);
        try { return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken) ?? throw new JsonException("Empty document"); }
        catch (JsonException ex) { throw new InvalidDataException($"周计划数据无法读取，原文件已保留：{Path}", ex); }
    }, cancellationToken);
    public async Task SaveAsync<T>(T value, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var temporaryPath = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await stream.WriteAsync(bytes, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporaryPath, Path, overwrite: true);
                }
                finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }
}

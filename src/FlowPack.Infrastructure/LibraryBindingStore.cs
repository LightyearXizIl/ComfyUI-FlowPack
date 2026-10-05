using System.Text.Json;

namespace FlowPack.Infrastructure;

/// <summary>Stores only the selected metadata-library location, never ComfyUI paths or credentials.</summary>
public sealed class LibraryBindingStore
{
    private readonly string? _portableRoot;
    public LibraryBindingStore(string? filePath = null, string? portableRoot = null)
    {
        _portableRoot = portableRoot is null ? null : Path.GetFullPath(portableRoot);
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComfyUI FlowPack",
            "library-binding.json");
    }

    public string FilePath { get; }

    // Startup runs on the WPF dispatcher. Never block it waiting for an async continuation.
    public LibraryBinding? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            return Resolve(JsonSerializer.Deserialize<LibraryBinding>(File.ReadAllText(FilePath))
                ?? throw new InvalidDataException("资源库关联文件为空或格式无效。"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("资源库关联文件不是有效的 JSON。", exception);
        }
    }

    public async Task<LibraryBinding?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath)) return null;
        await using var stream = File.OpenRead(FilePath);
        return Resolve(await JsonSerializer.DeserializeAsync<LibraryBinding>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("资源库关联文件为空或格式无效。"));
    }

    public async Task SaveAsync(LibraryBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (_portableRoot is not null)
        {
            var relative = Path.GetRelativePath(_portableRoot, Path.GetFullPath(binding.LibraryPath));
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
                binding = binding with { LibraryPath = relative };
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, binding, cancellationToken: cancellationToken);
            }
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private LibraryBinding Resolve(LibraryBinding binding) => _portableRoot is null
        ? binding
        : binding with { LibraryPath = Path.GetFullPath(binding.LibraryPath, _portableRoot) };
}

public sealed record LibraryBinding(string LibraryPath, DateTimeOffset BoundAt);

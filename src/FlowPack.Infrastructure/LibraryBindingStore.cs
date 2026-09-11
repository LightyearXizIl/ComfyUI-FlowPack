using System.Text.Json;

namespace FlowPack.Infrastructure;

/// <summary>Stores only the selected metadata-library location, never ComfyUI paths or credentials.</summary>
public sealed class LibraryBindingStore
{
    public LibraryBindingStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComfyUI FlowPack",
            "library-binding.json");
    }

    public string FilePath { get; }

    public async Task<LibraryBinding?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath)) return null;
        await using var stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<LibraryBinding>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("资源库关联文件为空或格式无效。");
    }

    public async Task SaveAsync(LibraryBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
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
}

public sealed record LibraryBinding(string LibraryPath, DateTimeOffset BoundAt);

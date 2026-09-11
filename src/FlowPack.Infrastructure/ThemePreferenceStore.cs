using System.Text.Json;
using System.Text.Json.Serialization;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>
/// Stores only the user's FlowPack theme preference. Package data and resource-library
/// state intentionally remain outside this file so appearance changes cannot reset them.
/// </summary>
public sealed class ThemePreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ThemePreferenceStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComfyUI FlowPack",
            "theme.flowpack-theme.json");
    }

    public string FilePath { get; }

    public async Task<ThemeDefinition?> LoadAsync(CancellationToken cancellationToken = default) =>
        File.Exists(FilePath)
            ? await ReadAsync(FilePath, cancellationToken)
            : null;

    public async Task SaveAsync(ThemeDefinition theme, CancellationToken cancellationToken = default)
    {
        EnsureValid(theme);
        await WriteAsync(FilePath, theme, cancellationToken);
    }

    public async Task<ThemeDefinition> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return await ReadAsync(filePath, cancellationToken);
    }

    public async Task ExportAsync(string filePath, ThemeDefinition theme, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        EnsureValid(theme);
        await WriteAsync(filePath, theme, cancellationToken);
    }

    private static async Task<ThemeDefinition> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var theme = await JsonSerializer.DeserializeAsync<ThemeDefinition>(stream, SerializerOptions, cancellationToken);
            if (theme is null) throw new InvalidDataException("主题文件为空或格式无效。");
            EnsureValid(theme);
            return theme;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("主题文件不是有效的 JSON。", exception);
        }
    }

    private static async Task WriteAsync(string filePath, ThemeDefinition theme, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        Directory.CreateDirectory(directory!);
        var temporaryPath = Path.Combine(directory!, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, theme, SerializerOptions, cancellationToken);
            }
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void EnsureValid(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var validation = ThemeValidator.Validate(theme);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
    }
}

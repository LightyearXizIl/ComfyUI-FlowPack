using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowPack.Infrastructure;

public enum LogIntensity { Off, Errors, Standard, Detailed }
public enum LogSeverity { Error, Information, Detail }
public sealed record LogPreferences(LogIntensity Intensity = LogIntensity.Standard, int RetentionDays = 7);
public sealed record LogCleanupResult(int DeletedFiles, int FailedFiles);

/// <summary>Daily application logs, separate from library/task data. Only our own log files are cleaned.</summary>
public sealed class ApplicationLog : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private Timer? _cleanupTimer;
    private bool _started;
    public static IReadOnlyList<int> SupportedRetentionDays { get; } = Array.AsReadOnly(new[] { 7, 10, 15, 30 });

    public ApplicationLog(string settingsPath, string directoryPath, Func<DateTimeOffset>? clock = null)
    {
        SettingsPath = Path.GetFullPath(settingsPath);
        DirectoryPath = Path.GetFullPath(directoryPath);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Preferences = Load();
    }

    public string SettingsPath { get; }
    public string DirectoryPath { get; }
    public LogPreferences Preferences { get; private set; }
    public string? LastError { get; private set; }

    private LogPreferences Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var settings = JsonSerializer.Deserialize<LogPreferences>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? throw new InvalidDataException("日志配置为空。");
            Validate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LastError = "日志配置读取失败，暂用标准记录和 7 天保留。";
            return new();
        }
    }

    private static void Validate(LogPreferences settings)
    {
        if (!Enum.IsDefined(settings.Intensity) || !SupportedRetentionDays.Contains(settings.RetentionDays))
            throw new ArgumentException("日志强度或保留时间无效。");
    }

    public void SetPreferences(LogPreferences settings)
    {
        Validate(settings);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporary, SettingsPath, overwrite: true);
                Preferences = settings;
                LastError = null;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            CleanupExpired();
        }
    }

    public void StartSession()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            CleanupExpired();
            _cleanupTimer = new Timer(_ => CleanupExpired(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            Write(LogSeverity.Information, "app.started");
        }
    }

    // Call sites use fixed event names and non-secret summaries. Do not pass raw payloads, URLs, IPC secrets or file contents.
    public void Write(LogSeverity severity, string eventName, string? summary = null)
    {
        lock (_gate)
        {
            if (!_started || Preferences.Intensity == LogIntensity.Off
                || (Preferences.Intensity == LogIntensity.Errors && severity != LogSeverity.Error)
                || (Preferences.Intensity != LogIntensity.Detailed && severity == LogSeverity.Detail)) return;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var now = _clock().ToUniversalTime();
                var record = JsonSerializer.Serialize(new { time = now.ToString("O"), level = severity.ToString(), eventName, summary });
                File.AppendAllText(Path.Combine(DirectoryPath, "flowpack-" + now.ToString("yyyy-MM-dd") + ".jsonl"), record + Environment.NewLine);
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = "无法写入日志，请检查保存目录的权限或磁盘空间。"; }
        }
    }

    public void WriteError(string eventName, Exception exception) =>
        Write(LogSeverity.Error, eventName, $"{exception.GetType().Name}; HResult={exception.HResult}"
            + (Preferences.Intensity == LogIntensity.Detailed ? "; " + exception.StackTrace : string.Empty));

    public LogCleanupResult CleanupExpired() => Cleanup(clearAll: false);
    public LogCleanupResult Clear() => Cleanup(clearAll: true);
    private LogCleanupResult Cleanup(bool clearAll)
    {
        lock (_gate)
        {
            var deleted = 0;
            var failed = 0;
            try
            {
                if (!Directory.Exists(DirectoryPath)) return new(0, 0);
                var cutoff = _clock().UtcDateTime - TimeSpan.FromDays(Preferences.RetentionDays);
                foreach (var file in Directory.EnumerateFiles(DirectoryPath, "flowpack-*.jsonl", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!DateOnly.TryParseExact(name["flowpack-".Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                        if (!clearAll && File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; }
                }
                LastError = failed == 0 ? null : "部分日志文件无法清理，请稍后重试。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; LastError = "日志目录不可访问，无法完成清理。"; }
            return new(deleted, failed);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_started) Write(LogSeverity.Information, "app.closed");
            _started = false;
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
        }
    }
}

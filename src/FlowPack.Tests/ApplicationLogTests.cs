using System.IO;
using System.Text.Json;
using FlowPack.App;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class ApplicationLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowpack-log-test-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private ApplicationLog Create() => new(Path.Combine(_root, "preferences", "logging.json"), Path.Combine(_root, "logs"), () => _now);

    [Theory]
    [InlineData(LogIntensity.Off, false, false, false)]
    [InlineData(LogIntensity.Errors, true, false, false)]
    [InlineData(LogIntensity.Standard, true, true, false)]
    [InlineData(LogIntensity.Detailed, true, true, true)]
    public void Intensity_filters_real_records_and_off_creates_no_log_file(LogIntensity intensity, bool error, bool information, bool detail)
    {
        using var logger = Create();
        logger.SetPreferences(new(intensity, 7));
        logger.StartSession();
        logger.Write(LogSeverity.Error, "test.error");
        logger.Write(LogSeverity.Information, "test.information");
        logger.Write(LogSeverity.Detail, "test.detail");
        var entries = Directory.Exists(logger.DirectoryPath)
            ? Directory.GetFiles(logger.DirectoryPath).SelectMany(File.ReadAllLines).Select(line => JsonDocument.Parse(line)).ToArray() : [];
        try
        {
            Assert.Equal(error, entries.Any(entry => entry.RootElement.GetProperty("eventName").GetString() == "test.error"));
            Assert.Equal(information, entries.Any(entry => entry.RootElement.GetProperty("eventName").GetString() == "test.information"));
            Assert.Equal(detail, entries.Any(entry => entry.RootElement.GetProperty("eventName").GetString() == "test.detail"));
            if (intensity == LogIntensity.Off) Assert.Empty(entries);
        }
        finally { foreach (var entry in entries) entry.Dispose(); }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(30)]
    public void Retention_removes_only_older_logs_and_preserves_boundary_and_other_data(int days)
    {
        using var logger = Create();
        logger.SetPreferences(new(LogIntensity.Off, days));
        Directory.CreateDirectory(logger.DirectoryPath);
        var old = Path.Combine(logger.DirectoryPath, "flowpack-2026-01-01.jsonl");
        var boundary = Path.Combine(logger.DirectoryPath, "flowpack-2026-01-02.jsonl");
        var unrelated = Path.Combine(logger.DirectoryPath, "flowpack.db");
        var unknown = Path.Combine(logger.DirectoryPath, "flowpack-not-a-date.jsonl");
        foreach (var file in new[] { old, boundary, unrelated, unknown }) File.WriteAllText(file, "protected");
        File.SetLastWriteTimeUtc(old, _now.UtcDateTime.AddDays(-days).AddSeconds(-1));
        File.SetLastWriteTimeUtc(boundary, _now.UtcDateTime.AddDays(-days));
        File.SetLastWriteTimeUtc(unrelated, _now.UtcDateTime.AddDays(-365));
        File.SetLastWriteTimeUtc(unknown, _now.UtcDateTime.AddDays(-365));
        logger.StartSession(); // Retention runs at startup even when logging is off.
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(boundary));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(unknown));
        Assert.Equal(new LogCleanupResult(0, 0), logger.CleanupExpired());
    }

    [Fact]
    public void Preferences_survive_restart_and_clear_retains_configuration_and_library_files()
    {
        using var logger = Create();
        var vm = new ShellViewModel(themeStore: new ThemePreferenceStore(Path.Combine(_root, "preferences", "theme.json")),
            libraryBindingStore: new LibraryBindingStore(Path.Combine(_root, "preferences", "library.json")), applicationLog: logger);
        vm.SelectedLogIntensity = LogIntensity.Detailed;
        vm.SelectedLogRetentionDays = 15;
        using var reloaded = Create();
        Assert.Equal(new LogPreferences(LogIntensity.Detailed, 15), reloaded.Preferences);
        logger.StartSession();
        logger.Write(LogSeverity.Error, "failure.fixture");
        var protectedData = Path.Combine(_root, "library", "state", "flowpack.db");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedData)!);
        File.WriteAllText(protectedData, "user-data");
        vm.ClearLogsCommand.Execute(null);
        Assert.Empty(Directory.GetFiles(logger.DirectoryPath, "*.jsonl"));
        Assert.Equal("user-data", File.ReadAllText(protectedData));
        Assert.True(File.Exists(logger.SettingsPath));
        Assert.Contains("已清理 1", vm.LoggingNotice);
        logger.Write(LogSeverity.Information, "test.after_clear");
        Assert.Single(Directory.GetFiles(logger.DirectoryPath, "*.jsonl"));
    }

    [Fact]
    public void Broken_preferences_fall_back_and_invalid_new_settings_are_rejected()
    {
        Directory.CreateDirectory(Path.Combine(_root, "preferences"));
        File.WriteAllText(Path.Combine(_root, "preferences", "logging.json"), "{bad");
        using var logger = Create();
        Assert.Equal(new LogPreferences(), logger.Preferences);
        Assert.NotNull(logger.LastError);
        Assert.Throws<ArgumentException>(() => logger.SetPreferences(new(LogIntensity.Standard, 0)));
        Assert.Throws<ArgumentException>(() => logger.SetPreferences(new((LogIntensity)999, 7)));
        logger.SetPreferences(new(LogIntensity.Errors, 30));
        Assert.Null(logger.LastError);
        using var restored = Create();
        Assert.Equal(new LogPreferences(LogIntensity.Errors, 30), restored.Preferences);
    }

    [Fact]
    public void Logging_failure_is_reported_without_breaking_application_operations()
    {
        Directory.CreateDirectory(_root);
        var blockedDirectory = Path.Combine(_root, "blocked");
        File.WriteAllText(blockedDirectory, "not-a-directory");
        using var logger = new ApplicationLog(Path.Combine(_root, "logging.json"), blockedDirectory);
        logger.StartSession();
        logger.Write(LogSeverity.Error, "test.error");
        Assert.NotNull(logger.LastError);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

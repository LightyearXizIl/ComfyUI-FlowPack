using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using FlowPack.App.Services;
using FlowPack.Core;
using FlowPack.Infrastructure;
using Microsoft.Win32;

namespace FlowPack.App;

public sealed partial class ShellViewModel
{
    private UpdateInfo? _availableUpdate;
    private bool _updating;
    private bool _themeSubscribed;
    private string? _updateHelperRoot;
    private bool _updateHandoffConfirmed;
    public bool HasUpdate => _availableUpdate is not null && !_updating;
    public ICommand InstallUpdateCommand { get; private set; } = null!;
    private async Task CheckForUpdatesAutomaticallyAsync()
    {
        if (!_themeSubscribed && Application.Current is not null)
        { SystemEvents.UserPreferenceChanged += OnSystemThemeChanged; _themeSubscribed = true; }
        var path = Path.Combine(Path.GetDirectoryName(_themeStore.FilePath)!, "update-check.json");
        try
        {
            if (File.Exists(path) && DateTimeOffset.TryParse(await File.ReadAllTextAsync(path), out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, DateTimeOffset.UtcNow.ToString("O"));
            await CheckForUpdatesAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { UpdateNotice = "自动更新检查记录不可用，可手动检查。"; }
    }
    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is null || _updating) return;
        _updating = true; OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(CoreReady)); CommandManager.InvalidateRequerySuggested();
        try
        {
            await EnsureCoreLibraryAsync();
            var update = _availableUpdate;
            UpdateNotice = "正在下载并校验更新安装器…";
            var downloaded = await _libraryDatabase!.RunAsync<DownloadResult>("task.download", new DownloadTaskPayload(update.InstallerUri.ToString(), update.Sha256, update.InstallerFileName));
            var helper = await UpdateBootstrapLauncher.StartAsync(Path.Combine(AppContext.BaseDirectory, "worker", "ComfyUI.FlowPack.Worker.exe"), downloaded.StagingPath, update.Sha256);
            _updateHelperRoot = helper.Root; _updateHandoffConfirmed = false;
            using var helperProcess = helper.Process;
            while (true)
            {
                var statusPath = Path.Combine(helper.Root, "update-status.json");
                if (File.Exists(statusPath))
                {
                    await using var statusStream = new FileStream(statusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
                    var status = await JsonSerializer.DeserializeAsync<UpdateBootstrapStatus>(statusStream);
                    if (status is not null) UpdateNotice = status.Message;
                    if (status?.State is "Failed" or "Cancelled") throw new IOException(status.Message);
                    if (status?.State == "ReadyToExit")
                    {
                        await _libraryDatabase.StopWhenIdleAsync();
                        await File.WriteAllTextAsync(Path.Combine(helper.Root, "proceed.txt"), helper.Id);
                        _updateHandoffConfirmed = true;
                        Application.Current?.Shutdown(); return;
                    }
                }
                if (helperProcess.HasExited) throw new IOException("更新引导程序已退出，请检查更新记录：" + helper.Root);
                await Task.Delay(250);
            }
        }
        catch (Exception ex) { UpdateNotice = "更新失败：" + ex.Message; }
        finally
        {
            if (!_updateHandoffConfirmed && _updateHelperRoot is not null)
                try { await File.WriteAllTextAsync(Path.Combine(_updateHelperRoot, "cancel.txt"), "cancel"); } catch (IOException) { }
            _updateHelperRoot = null;
            _updating = false; OnPropertyChanged(nameof(HasUpdate)); OnPropertyChanged(nameof(CoreReady)); CommandManager.InvalidateRequerySuggested();
        }
    }
    private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_appliedTheme.Base != ThemeBase.System && e.Category is not (UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color or UserPreferenceCategory.General)) return;
        Application.Current?.Dispatcher.InvokeAsync(() => ApplyThemeToResources(_appliedTheme));
    }
    public void DetachWindow()
    {
        _taskMonitorLifetime.Cancel();
        if (_updating && !_updateHandoffConfirmed && _updateHelperRoot is not null)
            try { File.WriteAllText(Path.Combine(_updateHelperRoot, "cancel.txt"), "cancel"); } catch (IOException) { }
        if (_themeSubscribed) { SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged; _themeSubscribed = false; }
    }
}

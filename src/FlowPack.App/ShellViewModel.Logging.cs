using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using FlowPack.Infrastructure;

namespace FlowPack.App;

public sealed partial class ShellViewModel
{
    private readonly ApplicationLog _applicationLog;
    private string _loggingNotice = string.Empty;
    public IReadOnlyList<LogIntensityOption> LogIntensityOptions { get; } =
    [new(LogIntensity.Off, "关闭"), new(LogIntensity.Errors, "仅错误"), new(LogIntensity.Standard, "标准"), new(LogIntensity.Detailed, "详细")];
    public IReadOnlyList<int> LogRetentionOptions => ApplicationLog.SupportedRetentionDays;
    public string LogDirectory => _applicationLog.DirectoryPath;
    public string LogIntensityDescription => SelectedLogIntensity switch
    {
        LogIntensity.Off => "停止写入新日志，已有日志仍按保留时间自动清理。",
        LogIntensity.Errors => "只记录操作失败、连接错误和未处理异常。",
        LogIntensity.Detailed => "记录标准事件、后台请求和页面切换，适合排查问题。",
        _ => "记录启动、退出、更新检查、任务开始与完成，以及错误。"
    };
    public string LoggingNotice { get => _loggingNotice; private set { _loggingNotice = value; OnPropertyChanged(); } }
    public LogIntensity SelectedLogIntensity
    {
        get => _applicationLog.Preferences.Intensity;
        set { if (value != SelectedLogIntensity) SaveLogPreferences(new(value, SelectedLogRetentionDays)); }
    }
    public int SelectedLogRetentionDays
    {
        get => _applicationLog.Preferences.RetentionDays;
        set { if (value != SelectedLogRetentionDays) SaveLogPreferences(new(SelectedLogIntensity, value)); }
    }
    public ICommand ClearLogsCommand { get; private set; } = null!;
    public ICommand OpenLogsFolderCommand { get; private set; } = null!;

    private void InitializeLoggingCommands()
    {
        LoggingNotice = _applicationLog.LastError ?? string.Empty;
        ClearLogsCommand = new RelayCommand(_ =>
        {
            var result = _applicationLog.Clear();
            LoggingNotice = result.FailedFiles == 0
                ? $"已清理 {result.DeletedFiles} 个日志文件。后续操作会按当前强度继续记录。"
                : $"已清理 {result.DeletedFiles} 个日志文件，{result.FailedFiles} 项未能清理，请稍后重试。";
        });
        OpenLogsFolderCommand = new RelayCommand(_ =>
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                Process.Start(new ProcessStartInfo(LogDirectory) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { LoggingNotice = "无法打开日志目录：" + ex.Message; }
        });
    }

    private void SaveLogPreferences(LogPreferences preferences)
    {
        try
        {
            _applicationLog.SetPreferences(preferences);
            LoggingNotice = _applicationLog.LastError ?? "设置已保存并生效；启动时和运行期间自动清理超期日志。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { LoggingNotice = "日志设置未保存：" + ex.Message; }
        OnPropertyChanged(nameof(SelectedLogIntensity));
        OnPropertyChanged(nameof(SelectedLogRetentionDays));
        OnPropertyChanged(nameof(LogIntensityDescription));
    }

    internal void StartLogging() => _applicationLog.StartSession();
    internal void StopLogging() => _applicationLog.Dispose();
    internal void LogUnhandledException(Exception exception) => _applicationLog.WriteError("app.unhandled", exception);
}

public sealed record LogIntensityOption(LogIntensity Value, string Label);

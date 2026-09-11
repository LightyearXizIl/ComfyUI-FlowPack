using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace FlowPack.App.Services;

public enum AppLanguage
{
    System,
    ZhCn,
    EnUs
}

public interface ILocalizationService : INotifyPropertyChanged
{
    AppLanguage Language { get; }
    string this[string key] { get; }
    void SetLanguage(AppLanguage language);
}

/// <summary>
/// Small, dependency-free localization service for application chrome and view-model strings.
/// XAML can bind through <c>Text[Some.Key]</c>; raising Item[] refreshes all indexed bindings.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private const string PreferenceFileName = "language.json";
    private readonly string _preferencePath;
    private AppLanguage _language;

    private static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>
    {
        ["Nav.Home"] = "首页",
        ["Nav.Library"] = "资源库",
        ["Nav.Packaging"] = "打包",
        ["Nav.Install"] = "安装",
        ["Nav.Tasks"] = "任务",
        ["Nav.Settings"] = "设置",
        ["Page.Home.Title"] = "让 ComfyUI 资源井然有序",
        ["Page.Home.Subtitle"] = "导入、安装和分享工作流需要的全部资源。",
        ["Page.Library.Title"] = "资源库",
        ["Page.Library.Subtitle"] = "集中管理工作流、模型和自定义节点。",
        ["Page.Packaging.Title"] = "创建资源包",
        ["Page.Packaging.Subtitle"] = "固定四步创建一个可分享的资源包。",
        ["Page.Install.Title"] = "安装资源包",
        ["Page.Install.Subtitle"] = "查看已导入资源、依赖与安全安装预览。",
        ["Page.Tasks.Title"] = "任务中心",
        ["Page.Tasks.Subtitle"] = "这里显示真实下载和安装任务，不生成示例进度。",
        ["Page.Settings.Title"] = "设置",
        ["Page.Settings.Subtitle"] = "环境、资源库、下载、更新、语言、外观和诊断。",
        ["Settings.Language"] = "语言",
        ["Settings.Update"] = "更新",
        ["Language.System"] = "跟随系统",
        ["Language.ZhCn"] = "简体中文",
        ["Language.EnUs"] = "English",
        ["Update.Check"] = "检查更新",
        ["Update.None"] = "尚未检查更新。",
        ["Update.Latest"] = "当前已是最新版本。",
        ["Update.Available"] = "发现可用更新。",
        ["Update.Failed"] = "检查更新失败，可稍后重试。",
        ["Install.Disabled"] = "真实 Desktop 安全验证完成前，安装保持禁用。"
    };

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["Nav.Home"] = "Home",
        ["Nav.Library"] = "Library",
        ["Nav.Packaging"] = "Package",
        ["Nav.Install"] = "Install",
        ["Nav.Tasks"] = "Tasks",
        ["Nav.Settings"] = "Settings",
        ["Page.Home.Title"] = "Keep ComfyUI resources organized",
        ["Page.Home.Subtitle"] = "Import, install, and share every resource a workflow needs.",
        ["Page.Library.Title"] = "Library",
        ["Page.Library.Subtitle"] = "Manage workflows, models, and custom nodes in one place.",
        ["Page.Packaging.Title"] = "Create a resource package",
        ["Page.Packaging.Subtitle"] = "Build a shareable package in four deliberate steps.",
        ["Page.Install.Title"] = "Install a resource package",
        ["Page.Install.Subtitle"] = "Review imported resources, dependencies, and the safe install plan.",
        ["Page.Tasks.Title"] = "Task center",
        ["Page.Tasks.Subtitle"] = "Only real download and installation tasks appear here.",
        ["Page.Settings.Title"] = "Settings",
        ["Page.Settings.Subtitle"] = "Environment, library, downloads, updates, language, appearance, and diagnostics.",
        ["Settings.Language"] = "Language",
        ["Settings.Update"] = "Updates",
        ["Language.System"] = "Use system setting",
        ["Language.ZhCn"] = "Simplified Chinese",
        ["Language.EnUs"] = "English",
        ["Update.Check"] = "Check for updates",
        ["Update.None"] = "Updates have not been checked yet.",
        ["Update.Latest"] = "You are up to date.",
        ["Update.Available"] = "An update is available.",
        ["Update.Failed"] = "Update check failed. Try again later.",
        ["Install.Disabled"] = "Installation remains disabled until real Desktop safety validation is complete."
    };

    public LocalizationService(string? preferencePath = null)
    {
        _preferencePath = preferencePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComfyUI FlowPack",
            PreferenceFileName);
        _language = LoadPreference(_preferencePath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language => _language;

    public string this[string key]
    {
        get
        {
            var dictionary = ResolveLanguage() == AppLanguage.EnUs ? English : Chinese;
            return dictionary.TryGetValue(key, out var value) ? value : key;
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language) return;
        _language = language;
        PersistPreference(_preferencePath, language);
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged("Item[]");
    }

    private AppLanguage ResolveLanguage()
    {
        if (_language != AppLanguage.System) return _language;
        return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.ZhCn
            : AppLanguage.EnUs;
    }

    private static AppLanguage LoadPreference(string path)
    {
        try
        {
            if (!File.Exists(path)) return AppLanguage.System;
            var preference = JsonSerializer.Deserialize<LanguagePreference>(File.ReadAllText(path));
            return preference?.Language is { } language && Enum.IsDefined(language) ? language : AppLanguage.System;
        }
        catch (IOException) { return AppLanguage.System; }
        catch (JsonException) { return AppLanguage.System; }
        catch (UnauthorizedAccessException) { return AppLanguage.System; }
    }

    private static void PersistPreference(string path, AppLanguage language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new LanguagePreference(language)));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record LanguagePreference(AppLanguage Language);
}

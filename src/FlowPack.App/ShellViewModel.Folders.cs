using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using FlowPack.Core;

namespace FlowPack.App;

public sealed partial class ShellViewModel
{
    private ICommand? _openResourceFolderCommand;
    public ICommand OpenResourceFolderCommand => _openResourceFolderCommand ??= new RelayCommand(
        value => OpenResourceFolder(value as ResourceFolderLocation),
        value => value is ResourceFolderLocation location && IsCurrentFolder(location));

    public IReadOnlyList<ResourceFolderLocation> GetResourceFolders(ResourceKind kind) =>
        ResourceFolderNavigation.GetLocations(SelectedInstance, kind);

    private bool IsCurrentFolder(ResourceFolderLocation location) => GetResourceFolders(location.Kind)
        .Any(current => string.Equals(current.Path, location.Path, StringComparison.OrdinalIgnoreCase));

    private void OpenResourceFolder(ResourceFolderLocation? location)
    {
        if (location is null || !IsCurrentFolder(location))
        {
            CoreNotice = "实例已变化，请重新选择文件夹入口。";
            return;
        }
        if (!Directory.Exists(location.Path))
        {
            CoreNotice = "目录不存在或无法访问：" + location.Path;
            return;
        }
        try { Process.Start(new ProcessStartInfo(location.Path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { CoreNotice = "无法打开目录：" + ex.Message; }
    }
}

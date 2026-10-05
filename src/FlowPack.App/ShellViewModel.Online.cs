using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using FlowPack.Core;
using FlowPack.Infrastructure;
using Microsoft.Win32;

namespace FlowPack.App;

public sealed class OnlineResourceRow(ResourceEntry resource) : INotifyPropertyChanged
{
    public ResourceEntry Resource { get; } = resource;
    public string Name => Resource.Name;
    public string SourceUrlDraft { get; set; } = resource.SourceUrl ?? "";
    public string Source => Resource.SourceUrl ?? "清单未提供来源，可选择本地文件。";
    public string HashNotice => Resource.Sha256 is null ? "未提供来源 SHA-256，下载后仅记录计算哈希。" : "来源 SHA-256：" + Resource.Sha256;
    public string? DownloadJobId { get; set; }
    private string _status = "待补全";
    public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public bool CanDownload => DownloadFileName is not null;
    public string? DownloadFileName
    {
        get
        {
            if (!Uri.TryCreate(Resource.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return null;
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (path.Contains("/blob/", StringComparison.OrdinalIgnoreCase) || path.Contains("/tree/", StringComparison.OrdinalIgnoreCase)) return null;
            var name = Path.GetFileName(path);
            var fixedArchive = false;
            if (uri.Host == "codeload.github.com" && path.Split('/') is ["", _, _, "zip", { Length: 40 } commit] && commit.All(Uri.IsHexDigit))
            { name = "node-" + commit + ".zip"; fixedArchive = true; }
            if (uri.Host == "github.com" && !(path.Contains("/archive/", StringComparison.Ordinal) && Path.GetFileNameWithoutExtension(name) is { Length: 40 } sha && sha.All(Uri.IsHexDigit))) return null;
            if (uri.Host == "github.com") fixedArchive = true;
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (Resource.Kind == ResourceKind.CustomNode && extension == ".zip" && Resource.Sha256 is null &&
                !fixedArchive) return null;
            if (extension is not (".zip" or ".cpack" or ".json" or ".py" or ".safetensors" or ".ckpt" or ".pt" or ".pth" or ".bin" or ".gguf" or ".onnx" or ".sft" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".wav" or ".mp3")) return null;
            try { PlannedZipExportService.ValidateRelative(name); } catch (InvalidDataException) { return null; }
            return name;
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class ShellViewModel
{
    private long _importSessionRevision;
    private bool _importChoicesDirty;
    public ObservableCollection<OnlineResourceRow> OnlineResources { get; } = [];
    public bool HasOnlineResources => OnlineResources.Count > 0;
    public ICommand DownloadOnlineCommand { get; private set; } = null!;
    public ICommand ChooseOnlineLocalCommand { get; private set; } = null!;
    public ICommand SaveOnlineSourceCommand { get; private set; } = null!;
    private void InitializeOnlineCommands()
    {
        SaveOnlineSourceCommand = new RelayCommand(p => _ = ExecuteCoreAsync(async () =>
        {
            if (p is not OnlineResourceRow row || _activeImport is null) return;
            try
            {
                var updated = await _libraryDatabase!.CallAsync<RestoredImportSession>("online.source",
                    new OnlineSourceInput(_activeImport.Id, row.Resource.Id, row.SourceUrlDraft, _importSessionRevision));
                _importSessionRevision = updated.State.Revision;
                await ApplyOnlineResultAsync(updated.Plan);
                foreach (var next in OnlineResources)
                    if (updated.State.DownloadJobs.TryGetValue(next.Resource.Id, out var job)) next.DownloadJobId = job;
                CoreNotice = "来源已保存，尚未下载。请点击下载并检查；最终仍需验证实际文件内容。";
            }
            catch (Exception ex) { row.Status = ex.Message; throw; }
        }), p => CoreReady && p is OnlineResourceRow { DownloadJobId: null });
        DownloadOnlineCommand = new RelayCommand(p => _ = ExecuteCoreAsync(() => DownloadOnlineAsync(p as OnlineResourceRow)),
            p => CoreReady && (p is OnlineResourceRow row ? row.CanDownload : OnlineResources.Any(x => x.CanDownload)));
        ChooseOnlineLocalCommand = new RelayCommand(p => _ = ExecuteCoreAsync(async () =>
        {
            if (p is not OnlineResourceRow row || _activeImport is null) return;
            var dialog = new OpenFileDialog { Title = "选择清单资源的本地载荷", Filter = "资源文件|*.*" };
            if (dialog.ShowDialog() != true) return;
            try { await MaterializeOnlineLocalAsync(row, dialog.FileName); }
            catch (Exception ex) { row.Status = ex.Message; throw; }
        }), p => CoreReady && p is OnlineResourceRow);
    }

    private void RefreshOnlineRows(bool reset = false)
    {
        var prior = reset ? [] : OnlineResources.ToDictionary(x => x.Resource.Id);
        OnlineResources.Clear();
        foreach (var resource in _activeImport?.PendingDownloads ?? [])
            OnlineResources.Add(prior.TryGetValue(resource.Id, out var row) && row.Resource == resource ? row : new(resource));
        OnPropertyChanged(nameof(HasOnlineResources));
    }

    private async Task DownloadOnlineAsync(OnlineResourceRow? single)
    {
        await EnsureCoreLibraryAsync();
        foreach (var row in single is null ? OnlineResources.Where(x => x.CanDownload).ToArray() : new[] { single })
        {
            if (_activeImport is null || !_activeImport.PendingDownloads.Any(x => x.Id == row.Resource.Id)) continue;
            try
            {
                if (await TryReuseOnlineLocalAsync(row)) continue;
                if (row.DownloadFileName is not { } name) throw new InvalidDataException("来源不是已确认的文件链接，请选择本地文件或补全清单来源。");
                row.Status = "正在下载并校验…";
                if (row.DownloadJobId is null)
                {
                    var queued = await _libraryDatabase!.CallAsync<WorkerJob>("online.download", new OnlineDownloadInput(_activeImport.Id, row.Resource.Id, name));
                    row.DownloadJobId = queued.Id;
                }
                await _libraryDatabase!.WaitForJobAsync(row.DownloadJobId, JobProgress());
                var result = await _libraryDatabase.RunAsync<ImportPlan>("resource.materialize", new OnlineMaterializeInput(_activeImport.Id, row.Resource.Id, row.DownloadJobId), JobProgress());
                await ApplyOnlineResultAsync(result);
            }
            catch (Exception ex) { row.Status = ex.Message + "；可在本页选择本地文件，或重新导入后下载。"; CoreNotice = row.Status; if (single is not null) throw; }
        }
    }

    private async Task MaterializeOnlineLocalAsync(OnlineResourceRow row, string path)
    {
        var result = await _libraryDatabase!.RunAsync<ImportPlan>("resource.materialize-local", new OnlineLocalInput(_activeImport!.Id, row.Resource.Id, path), JobProgress());
        await ApplyOnlineResultAsync(result);
    }

    private async Task<bool> TryReuseOnlineLocalAsync(OnlineResourceRow row)
    {
        if (_activeImport is null || _inventory is null || row.DownloadJobId is not null ||
            SelectedInstance?.Id != _inventory.Instance.Id ||
            SelectedInstance.ConfigurationFingerprint != _inventory.Instance.ConfigurationFingerprint ||
            row.Resource.DeploymentPurpose is not { } target) return false;
        string localPath;
        try { localPath = ResourceInstallationService.ResolveTarget(_inventory.Instance, target); }
        catch (InvalidDataException) { return false; }
        var matches = _inventory.Resources.Where(x => x.Kind == row.Resource.Kind && File.Exists(x.SourcePath) &&
            (string.Equals(Path.GetFullPath(x.SourcePath), localPath, StringComparison.OrdinalIgnoreCase) ||
             OnlineLocalResourceMatcher.MatchesModelReference(target, x)))
            .DistinctBy(x => Path.GetFullPath(x.SourcePath), StringComparer.OrdinalIgnoreCase).ToArray();
        if (matches.Length == 0) return false;
        var boundInstance = SelectedInstance;
        var boundPlanId = _activeImport.Id;
        var candidatePath = matches[0].SourcePath;
        if (matches.Length > 1)
        {
            if (row.Resource.Kind != ResourceKind.Model || row.Resource.Sha256 is null) return false;
            var matched = await _libraryDatabase!.RunAsync<OnlineLocalMatchResult>("resource.match-local",
                new OnlineLocalMatchInput(_activeImport.Id, row.Resource.Id, matches.Select(x => x.SourcePath).ToArray()), JobProgress());
            if (!ReferenceEquals(boundInstance, SelectedInstance) || boundPlanId != _activeImport?.Id) return false;
            row.Status = matched.Evidence;
            if (matched.SourcePath is null) return false;
            candidatePath = matched.SourcePath;
        }
        ImportPlan result;
        try
        {
            result = await _libraryDatabase!.RunAsync<ImportPlan>("resource.materialize-local",
                new OnlineLocalInput(_activeImport.Id, row.Resource.Id, candidatePath), JobProgress());
        }
        catch (IOException ex)
        {
            row.Status = "本地候选未通过核验，仍需补全：" + ex.Message;
            return false;
        }
        if (!ReferenceEquals(boundInstance, SelectedInstance) || boundPlanId != _activeImport?.Id) return false;
        await ApplyOnlineResultAsync(result);
        return true;
    }

    private async Task RefreshImportedDependenciesForInstanceAsync()
    {
        if (_activeImport is null || _inventory is null) return;
        foreach (var row in OnlineResources.ToArray()) await TryReuseOnlineLocalAsync(row);
        _analyzedWorkflows.Clear(); _analyzedWorkflows.AddRange(_activeImport.Workflows);
        await PopulateDependenciesAsync();
        if (SelectedInstance is not null && ImportResources.Any(x => x.IsSelected)) await PrepareDeploymentAsync();
    }

    private async Task ApplyOnlineResultAsync(ImportPlan result)
    {
        _activeImport = result; _deployment = null;
        OnPropertyChanged(nameof(ImportSourceLabel)); OnPropertyChanged(nameof(HasImportSource));
        foreach (var resource in result.Resources)
            if (!ImportResources.Any(x => x.Resource.Id == resource.Id && x.Resource.SourcePath == resource.SourcePath)) ImportResources.Add(new(resource));
        RefreshOnlineRows();
        await SaveImportSessionAsync();
        _analyzedWorkflows.Clear(); _analyzedWorkflows.AddRange(result.Workflows);
        OnPropertyChanged(nameof(DeploymentGateNotice)); OnPropertyChanged(nameof(DeploymentSummary));
        await PopulateDependenciesAsync();
        if (SelectedInstance is not null && ImportResources.Any(x => x.IsSelected)) await PrepareDeploymentAsync();
        CoreNotice = "载荷已核对并加入安装预览；待补全 " + OnlineResources.Count + " 项。";
    }

    private async Task SaveImportSessionAsync()
    {
        if (_activeImport is null || _libraryDatabase is null) return;
        _importChoicesDirty = false;
        var saved = await _libraryDatabase.CallAsync<ImportSessionState>("import.session.save", new ImportSessionState(_activeImport.Id,
            OnlineResources.Where(x => x.DownloadJobId is not null).ToDictionary(x => x.Resource.Id, x => x.DownloadJobId!))
        {
            Revision = _importSessionRevision,
            ResourceChoices = ImportResources.Where(row => _activeImport.Resources.Any(x => x.Id == row.Resource.Id && x.SourcePath == row.Resource.SourcePath))
                .Select(row => new ImportResourceChoice(row.Resource.Id, row.Resource.SourcePath, row.IsSelected, row.TargetRelativePath)).ToArray()
        });
        _importSessionRevision = saved.Revision;
        foreach (var row in OnlineResources)
            if (saved.DownloadJobs.TryGetValue(row.Resource.Id, out var id)) row.DownloadJobId = id;
    }

    private async Task RestoreImportSessionAsync()
    {
        if (_libraryDatabase is null || _activeImport is not null) return;
        var restored = await _libraryDatabase.CallAsync<RestoredImportSession?>("import.session.load", new { }, allowEmptyResponse: true);
        if (restored is null) return;
        _importSessionRevision = restored.State.Revision;
        _activeImport = restored.Plan; _deployment = null;
        OnPropertyChanged(nameof(ImportSourceLabel)); OnPropertyChanged(nameof(HasImportSource));
        RefreshOnlineRows(reset: true);
        foreach (var row in OnlineResources)
            if (restored.State.DownloadJobs.TryGetValue(row.Resource.Id, out var id))
            {
                row.DownloadJobId = id;
                var job = await _libraryDatabase.CallAsync<WorkerJob>("job.get", id);
                row.Status = job.Error ?? job.Stage;
            }
        ImportResources.Clear();
        foreach (var resource in restored.Plan.Resources)
        {
            var choice = restored.State.ResourceChoices?.SingleOrDefault(x => x.ResourceId == resource.Id && x.SourcePath == resource.SourcePath);
            ImportResources.Add(new(resource) { IsSelected = choice?.IsSelected ?? false, TargetRelativePath = choice?.TargetRelativePath ?? resource.TargetRelativePath });
        }
        _analyzedWorkflows.Clear(); _analyzedWorkflows.AddRange(restored.Plan.Workflows);
        OnPropertyChanged(nameof(DeploymentGateNotice)); OnPropertyChanged(nameof(DeploymentSummary));
        OnPropertyChanged(nameof(DeploymentFiles));
        await RefreshImportedDependenciesForInstanceAsync();
        CoreNotice = "已恢复导入内容、资源选择、目标草稿和下载任务。请核对实例并重新检查安装计划；修改过的目标仍需确认，未恢复任何安装权限。";
    }
}

using System.Windows.Input;
using FlowPack.Infrastructure;

namespace FlowPack.App;

public sealed partial class ShellViewModel
{
    private WorkerJob? _progressJob;
    private int _progressRevision;
    private bool _operationVisible, _operationFinished, _operationError;
    private string _operationMessage = "正在准备";
    public bool OperationVisible => _operationVisible;
    public bool OperationIndeterminate => !_operationFinished && (_progressJob?.TotalUnits ?? _progressJob?.TotalBytes) is not > 0;
    public double OperationPercent => _operationFinished && !_operationError ? 100 :
        (_progressJob?.TotalUnits ?? _progressJob?.TotalBytes) is > 0 and var total ? Math.Clamp(100.0 * (_progressJob?.CompletedUnits ?? _progressJob?.CompletedBytes ?? 0) / total, 0, 100) : 0;
    public string OperationText => _operationFinished ? _operationMessage : _progressJob is { } job ?
        HomeOperationLabel(job.Operation) + " · " + job.Stage +
        (OperationIndeterminate ? (job.CompletedUnits is { } count ? $" · 已处理 {count:N0} {job.ProgressUnit}" : "") : $" · {OperationPercent:0}%") : _operationMessage;
    public bool OperationCanCancel => !_operationFinished && _progressJob?.CanControl("task.cancel") == true;
    public ICommand CancelOperationCommand => new RelayCommand(_ => _ = ExecuteTaskControlAsync("task.cancel", _progressJob), _ => OperationCanCancel);
    public ICommand DismissOperationCommand => new RelayCommand(_ => { _operationVisible = false; NotifyOperationProgress(); }, _ => _operationFinished);
    private void BeginOperationProgress()
    {
        ++_progressRevision; _progressJob = null; _operationVisible = true; _operationFinished = false; _operationError = false; _operationMessage = "正在准备"; NotifyOperationProgress();
    }
    private void SetOperationProgress(WorkerJob job) { _progressJob = job; NotifyOperationProgress(); }
    private void FailOperationProgress(string error) { _operationError = true; _operationMessage = "操作未完成 · " + error; }
    private void EndOperationProgress()
    {
        _operationFinished = true;
        if (!_operationError) _operationMessage = "操作完成";
        NotifyOperationProgress();
        if (!_operationError) _ = HideCompletedProgressAsync(_progressRevision);
    }
    private async Task HideCompletedProgressAsync(int revision)
    {
        await Task.Delay(3000);
        if (revision == _progressRevision && _operationFinished && !_operationError) { _operationVisible = false; NotifyOperationProgress(); }
    }
    private void NotifyOperationProgress()
    {
        foreach (var name in new[] { nameof(OperationVisible), nameof(OperationIndeterminate), nameof(OperationPercent), nameof(OperationText), nameof(OperationCanCancel) }) OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}


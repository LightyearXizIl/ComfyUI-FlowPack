namespace FlowPack.Infrastructure;

/// <summary>Per-user cross-process admission and update barrier. Kernel file leases survive UI disconnects, not process death.</summary>
public sealed class GlobalWorkCoordinator(string? root = null)
{
    private readonly string _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyUI FlowPack", "coordination");

    public Task<WorkLease> EnterWorkAsync(CancellationToken token = default) => EnterAsync(false, token);
    public Task<WorkLease> EnterTaskControlAsync(CancellationToken token = default) => EnterAsync(true, token);
    private async Task<WorkLease> EnterAsync(bool existingTaskControl, CancellationToken token)
    {
        using var gate = await LockAsync("admission.lock", token);
        using var pending = existingTaskControl ? null : TryLock("update-intent.lock") ?? throw new IOException("软件更新正在等待后台任务结束，暂不接受新任务。");
        var active = new FileStream(Path.Combine(_root, "active-work.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        return new(active);
    }

    public async Task<UpdateLease> BeginUpdateAsync(IProgress<string>? progress = null, CancellationToken token = default)
    {
        FileStream intent;
        using (await LockAsync("admission.lock", token))
            intent = TryLock("update-intent.lock") ?? throw new IOException("另一个窗口正在处理更新。");
        try
        {
            progress?.Report("更新准备中，已阻止所有资源库的新任务；等待后台任务安全结束…");
            var active = await LockAsync("active-work.lock", token);
            return new(intent, active);
        }
        catch { intent.Dispose(); throw; }
    }

    private FileStream? TryLock(string file)
    {
        Directory.CreateDirectory(_root);
        try { return new FileStream(Path.Combine(_root, file), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33) { return null; }
    }
    private async Task<FileStream> LockAsync(string file, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (TryLock(file) is { } lease) return lease;
            await Task.Delay(100, token);
        }
    }

    public sealed class WorkLease : IDisposable
    {
        private readonly FileStream _stream;
        private int _references = 1;
        internal WorkLease(FileStream stream) => _stream = stream;
        public WorkLease Retain()
        {
            lock (_stream)
            {
                if (_references == 0) throw new ObjectDisposedException(nameof(WorkLease));
                _references++; return this;
            }
        }
        public void Dispose()
        {
            lock (_stream) { if (_references > 0 && --_references == 0) _stream.Dispose(); }
        }
    }
    public sealed class UpdateLease : IDisposable
    {
        private readonly FileStream _intent;
        private readonly FileStream _active;
        internal UpdateLease(FileStream intent, FileStream active) { _intent = intent; _active = active; }
        public void Dispose() { _active.Dispose(); _intent.Dispose(); }
    }
}

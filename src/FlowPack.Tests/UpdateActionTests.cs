using System.Net.Http;
using FlowPack.App;
using FlowPack.App.Services;

namespace FlowPack.Tests;

public sealed class UpdateActionTests
{
    [Theory]
    [InlineData("available")]
    [InlineData("latest")]
    [InlineData("failed")]
    public async Task One_action_checks_then_downloads_only_when_a_verified_update_exists(string result)
    {
        var service = new PendingUpdateService();
        var vm = new ShellViewModel(updateService: service);
        Assert.Equal(vm.Text["Update.Check"], vm.UpdateActionLabel);
        Assert.Same(vm.CheckForUpdatesCommand, vm.UpdateActionCommand);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.UpdateActionLabel) && vm.UpdateActionLabel != vm.Text["Update.Checking"])
                finished.TrySetResult();
        };

        vm.UpdateActionCommand.Execute(null);
        Assert.Equal(vm.Text["Update.Checking"], vm.UpdateActionLabel);
        Assert.False(vm.UpdateActionCommand.CanExecute(null));
        Assert.False(vm.InstallUpdateCommand.CanExecute(null));
        vm.CheckForUpdatesCommand.Execute(null);
        Assert.Equal(1, service.Calls);

        if (result == "failed") service.Completion.SetException(new HttpRequestException("fixture"));
        else service.Completion.SetResult(result == "available"
            ? new UpdateInfo(new Version(9, 0, 0), new Uri("https://example.invalid/setup.exe"), "setup.exe", new string('A', 64), new Uri("https://example.invalid/SHA256SUMS.txt"))
            : null);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.UpdateActionCommand.CanExecute(null));
        Assert.Equal(result == "available", vm.HasUpdate);
        Assert.Equal(vm.Text[result == "available" ? "Update.Download" : "Update.Check"], vm.UpdateActionLabel);
        Assert.Same(result == "available" ? vm.InstallUpdateCommand : vm.CheckForUpdatesCommand, vm.UpdateActionCommand);
        if (result == "available") Assert.Contains(vm.Text["Update.InstallHint"], vm.UpdateNotice);
        else Assert.Equal(vm.Text[result == "failed" ? "Update.Failed" : "Update.Latest"], vm.UpdateNotice);
    }

    private sealed class PendingUpdateService : IUpdateService
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<UpdateInfo?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Completion.Task;
        }
    }
}

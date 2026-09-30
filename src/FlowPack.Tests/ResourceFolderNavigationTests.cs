using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class ResourceFolderNavigationTests
{
    private static InstanceDescriptor Instance(string root) => new(
        root, "fixture", "desktop-2", null, root, root + "\\ComfyUI", root, root + "\\user",
        root + "\\user\\default\\workflows", root + "\\custom_nodes", null,
        [root + "\\models", root + "\\MODELS\\", "relative-models"], root + "\\models\\",
        [new("loras", root + "\\shared-loras"), new("custom_nodes", root + "\\shared-nodes")], "fixture", []);

    [Fact]
    public void Shortcuts_use_registered_paths_and_keep_shared_model_and_node_locations()
    {
        var instance = Instance("E:\\FolderFixture");
        Assert.Equal(instance.WorkflowsDirectory, Assert.Single(ResourceFolderNavigation.GetLocations(instance, ResourceKind.Workflow)).Path);
        var models = ResourceFolderNavigation.GetLocations(instance, ResourceKind.Model);
        Assert.Equal(2, models.Count);
        Assert.Equal("E:\\FolderFixture\\models", models[0].Path);
        Assert.Contains(models, p => p.Path == "E:\\FolderFixture\\shared-loras");
        Assert.DoesNotContain(models, p => p.Path.Contains("shared-nodes") || p.Path.Contains("relative-models"));
        var nodes = ResourceFolderNavigation.GetLocations(instance, ResourceKind.CustomNode);
        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, p => p.Path == "E:\\FolderFixture\\shared-nodes");
        Assert.Empty(ResourceFolderNavigation.GetLocations(null, ResourceKind.Model));
    }

    [Fact]
    public void Switching_instances_invalidates_old_folder_actions_and_missing_folders_are_not_created()
    {
        // Set the same scan guard used during discovery; this fixture must not attach a real Worker.
        var vm = new ShellViewModel();
        var scanGuard = typeof(ShellViewModel).GetField("_settingInstance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        scanGuard.SetValue(vm, true);
        var missingRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flowpack-folder-" + Guid.NewGuid().ToString("N"));
        vm.DesktopInstances.Add(Instance(missingRoot));
        vm.SelectedInstance = vm.DesktopInstances[0];
        scanGuard.SetValue(vm, false);
        var folder = Assert.Single(vm.GetResourceFolders(ResourceKind.Workflow));
        Assert.True(vm.OpenResourceFolderCommand.CanExecute(folder));
        vm.OpenResourceFolderCommand.Execute(folder);
        Assert.Contains("目录不存在或无法访问", vm.CoreNotice);
        Assert.False(System.IO.Directory.Exists(missingRoot));
        vm.SelectedInstance = null;
        Assert.False(vm.OpenResourceFolderCommand.CanExecute(folder));
        vm.OpenResourceFolderCommand.Execute(folder);
        Assert.Contains("实例已变化", vm.CoreNotice);
        Assert.False(System.IO.Directory.Exists(missingRoot));
    }
}

using System.IO;
using FlowPack.ComfyUI;
using Xunit;

namespace FlowPack.Tests;

public class ComfyUiInstallServiceTests
{
    private static string MakePackage(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "flowpack_pkg_" + Guid.NewGuid().ToString("N"));
        WriteFile(Path.Combine(root, "workflows", "demo.json"), "{}");
        WriteFile(Path.Combine(root, "models", "checkpoints", "sd_xl.safetensors"), "M");
        WriteFile(Path.Combine(root, "custom_nodes", "MyNode", "__init__.py"), "print('hi')");
        return root;
    }

    private static ComfyDesktopLocation MakeDesktop(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "flowpack_desktop_" + Guid.NewGuid().ToString("N"));
        var wf = Path.Combine(root, "user", "default", "workflows");
        var models = Path.Combine(root, "models");
        var nodes = Path.Combine(root, "custom_nodes");
        Directory.CreateDirectory(wf);
        Directory.CreateDirectory(models);
        Directory.CreateDirectory(nodes);
        return new ComfyDesktopLocation("test", root, root, models, nodes, wf, null, DateTimeOffset.UtcNow);
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Plan_covers_workflow_model_and_node()
    {
        var package = MakePackage(out var packageRoot);
        var desktop = MakeDesktop(out var desktopRoot);
        try
        {
            var steps = new ComfyUiInstallService().Plan(desktop, packageRoot);
            Assert.Contains(steps, s => s.Kind == DeployStepKind.Workflow);
            Assert.Contains(steps, s => s.Kind == DeployStepKind.Model);
            Assert.Contains(steps, s => s.Kind == DeployStepKind.CustomNode);
        }
        finally
        {
            Directory.Delete(packageRoot, true);
            Directory.Delete(desktopRoot, true);
        }
    }

    [Fact]
    public async Task Apply_copies_into_desktop_directories_and_is_idempotent()
    {
        var package = MakePackage(out var packageRoot);
        var desktop = MakeDesktop(out var desktopRoot);
        try
        {
            var first = await new ComfyUiInstallService().ApplyAsync(desktop, packageRoot);
            Assert.Equal(3, first.InstalledPaths.Count);
            Assert.Empty(first.Errors);
            Assert.True(File.Exists(Path.Combine(desktop.WorkflowsDirectory!, "demo.json")));
            Assert.True(File.Exists(Path.Combine(desktop.ModelsDirectory!, "checkpoints", "sd_xl.safetensors")));
            Assert.True(File.Exists(Path.Combine(desktop.CustomNodesDirectory!, "MyNode", "__init__.py")));

            var second = await new ComfyUiInstallService().ApplyAsync(desktop, packageRoot);
            Assert.Empty(second.InstalledPaths);
            Assert.Equal(3, second.SkippedPaths.Count);
        }
        finally
        {
            Directory.Delete(packageRoot, true);
            Directory.Delete(desktopRoot, true);
        }
    }
}

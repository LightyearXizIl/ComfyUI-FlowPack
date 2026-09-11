using System.IO;
using FlowPack.ComfyUI;

namespace FlowPack.Tests;

public sealed class ComfyUiInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FlowPack.ComfyInspect.{Guid.NewGuid():N}");

    [Fact]
    public async Task Known_layout_with_cross_evidence_is_reported_read_only()
    {
        Directory.CreateDirectory(Path.Combine(_root, "user"));
        Directory.CreateDirectory(Path.Combine(_root, "venv", "Scripts"));
        await File.WriteAllTextAsync(Path.Combine(_root, "main.py"), "# ComfyUI");
        await File.WriteAllTextAsync(Path.Combine(_root, "venv", "Scripts", "python.exe"), string.Empty);

        var instance = await new ComfyUiInspector().InspectAsync(_root);

        Assert.NotNull(instance);
        Assert.Equal(Path.GetFullPath(_root), instance.InstancePath);
        Assert.Equal(Path.Combine(_root, "venv", "Scripts", "python.exe"), instance.PythonPath);
        Assert.Equal(Path.Combine(_root, "user"), instance.UserDirectory);
    }

    [Fact]
    public async Task A_random_nested_python_executable_is_not_mistaken_for_an_instance()
    {
        Directory.CreateDirectory(Path.Combine(_root, "some", "deep", "directory"));
        await File.WriteAllTextAsync(Path.Combine(_root, "some", "deep", "directory", "python.exe"), string.Empty);

        var instance = await new ComfyUiInspector().InspectAsync(_root);

        Assert.Null(instance);
    }

    [Fact]
    public async Task Ambiguous_known_interpreters_are_not_bound_automatically()
    {
        Directory.CreateDirectory(Path.Combine(_root, "user"));
        Directory.CreateDirectory(Path.Combine(_root, "venv", "Scripts"));
        Directory.CreateDirectory(Path.Combine(_root, "python_embeded"));
        await File.WriteAllTextAsync(Path.Combine(_root, "main.py"), "# ComfyUI");
        await File.WriteAllTextAsync(Path.Combine(_root, "venv", "Scripts", "python.exe"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(_root, "python_embeded", "python.exe"), string.Empty);

        Assert.Null(await new ComfyUiInspector().InspectAsync(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

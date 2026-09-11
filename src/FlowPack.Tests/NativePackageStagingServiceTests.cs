using System;
using System.IO;
using System.IO.Compression;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class NativePackageStagingServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowpack-stage-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StageAsync_extracts_native_layout_inside_a_new_private_directory()
    {
        var zip = CreateZip(("workflows/demo.json", "{}"), ("models/checkpoints/demo.safetensors", "model"));

        var staged = await new NativePackageStagingService().StageAsync(zip, Path.Combine(_root, "staging"));

        Assert.Equal(2, staged.FileCount);
        Assert.True(File.Exists(Path.Combine(staged.RootPath, "workflows", "demo.json")));
        Assert.True(File.Exists(Path.Combine(staged.RootPath, "models", "checkpoints", "demo.safetensors")));
        Assert.DoesNotContain(".incoming-", staged.RootPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("C:/escape.txt")]
    [InlineData("models/CON.txt")]
    public async Task StageAsync_rejects_unsafe_paths(string entry)
    {
        var zip = CreateZip((entry, "blocked"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new NativePackageStagingService().StageAsync(zip, Path.Combine(_root, "staging")));
    }

    [Fact]
    public async Task StageAsync_cleans_incomplete_directory_when_size_limit_is_exceeded()
    {
        var zip = CreateZip(("models/checkpoints/large.bin", "12345"));
        var staging = Path.Combine(_root, "staging");

        await Assert.ThrowsAsync<InvalidDataException>(() => new NativePackageStagingService().StageAsync(zip, staging, maximumUncompressedBytes: 4));

        Assert.Empty(Directory.EnumerateDirectories(staging));
    }

    private string CreateZip(params (string Path, string Contents)[] entries)
    {
        Directory.CreateDirectory(_root);
        var zip = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (path, contents) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(contents);
        }
        return zip;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

using System.Net;
using System.Net.Http;
using FlowPack.ComfyUI;
using Xunit;

namespace FlowPack.Tests;

public class ComfyUiManagerNodeSourceTests
{
    private const string ListJson = """
    {
      "custom_nodes": [
        { "author": "foo", "title": "ComfyUI-Bar", "reference": "https://github.com/foo/ComfyUI-Bar", "install_type": "git-clone", "description": "demo" },
        { "author": "baz", "title": "ComfyUI-Baz", "reference": "https://github.com/baz/ComfyUI-Baz", "install_type": "git-clone" }
      ]
    }
    """;

    [Fact]
    public async Task GetSource_returns_repository_for_matching_node_type()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(ListJson)));
        using var source = new ComfyUiManagerNodeSource(client);

        var result = await source.GetSourceAsync("ComfyUI-Bar");

        Assert.NotNull(result);
        Assert.Equal("https://github.com/foo/ComfyUI-Bar", result!.RepositoryUrl);
        Assert.Equal("ComfyUI-Bar", result.PackageFolderHint);
        Assert.Contains(result.SearchLinks, l => l.Url.Contains("github.com/search"));
    }

    [Fact]
    public async Task GetSource_returns_search_only_when_no_match()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(ListJson)));
        using var source = new ComfyUiManagerNodeSource(client);

        var result = await source.GetSourceAsync("NonexistentNode");

        Assert.NotNull(result);
        Assert.Null(result!.RepositoryUrl);
        Assert.Contains(result.SearchLinks, l => l.Url.Contains("github.com/search"));
    }

    [Fact]
    public void ResolveInstalledPackage_returns_folder_for_match_else_null()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(ListJson)));
        using var source = new ComfyUiManagerNodeSource(client);

        Assert.Equal("ComfyUI-Baz", source.ResolveInstalledPackage("ComfyUI-Baz"));
        Assert.Null(source.ResolveInstalledPackage("Unknown"));
    }
}

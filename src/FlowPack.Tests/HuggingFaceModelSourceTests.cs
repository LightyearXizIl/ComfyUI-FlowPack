using System.Net;
using System.Net.Http;
using FlowPack.ComfyUI;
using Xunit;

namespace FlowPack.Tests;

public class HuggingFaceModelSourceTests
{
    [Fact]
    public async Task GetSource_returns_direct_url_and_size_for_known_repo()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            "[{\"path\":\"sd_xl_base_1.0.safetensors\",\"size\":6732978453}]"));
        using var client = new HttpClient(handler);
        var source = new HuggingFaceModelSource(client, new Dictionary<string, string>
        {
            ["sd_xl_base_1.0.safetensors"] = "stabilityai/sdxl-vae"
        });

        var result = await source.GetSourceAsync("sd_xl_base_1.0.safetensors", "checkpoints");

        Assert.NotNull(result);
        Assert.Equal("https://huggingface.co/stabilityai/sdxl-vae/resolve/main/sd_xl_base_1.0.safetensors", result!.DirectUrl);
        Assert.Equal(6732978453L, result.SizeBytes);
        Assert.Contains(result.SearchLinks, l => l.Url.Contains("huggingface.co/models?search="));
        Assert.Contains(result.SearchLinks, l => l.Url.Contains("modelscope.cn/models?name="));
    }

    [Fact]
    public async Task GetSource_only_search_links_for_unknown_repo()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("[]"));
        using var client = new HttpClient(handler);
        var source = new HuggingFaceModelSource(client);

        var result = await source.GetSourceAsync("mystery_model.safetensors");

        Assert.NotNull(result);
        Assert.Null(result!.DirectUrl);
        Assert.Null(result.SizeBytes);
        Assert.Equal(2, result.SearchLinks.Count);
    }

    [Fact]
    public async Task GetSource_returns_null_for_empty_file_name()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("[]")));
        var source = new HuggingFaceModelSource(client);
        Assert.Null(await source.GetSourceAsync("  "));
    }
}

using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class OnlineLocalResourceMatcherTests
{
    [Theory]
    [InlineData("models/checkpoints/sub/a.pth", "checkpoints/sub/a.pth", "checkpoints", true)]
    [InlineData("models/checkpoints/sub/a.pth", "loras/sub/a.pth", "loras", false)]
    [InlineData("models/checkpoints/sub/a.pth", "checkpoints/a.pth", "checkpoints", false)]
    [InlineData("models/clip/sub/a.safetensors", "text_encoders/sub/a.safetensors", "text_encoders", true)]
    [InlineData("models/unet/a.safetensors", "diffusion_models/a.safetensors", "diffusion_models", true)]
    [InlineData("models/checkpoints/../a.pth", "checkpoints/a.pth", "checkpoints", false)]
    [InlineData("models/checkpoints/a.pth", "checkpoints/a.pth", "loras", false)]
    public void Matches_full_category_and_relative_reference(string target, string relative, string category, bool expected)
    {
        var model = new LocalResource("id", ResourceKind.Model, "a", "E:/shared/weights/a.pth", relative, category);
        Assert.Equal(expected, OnlineLocalResourceMatcher.MatchesModelReference(target, model));
        Assert.False(OnlineLocalResourceMatcher.MatchesModelReference(target, model with { Kind = ResourceKind.CustomNode }));
    }
}

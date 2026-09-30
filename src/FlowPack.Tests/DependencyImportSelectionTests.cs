using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class DependencyImportSelectionTests
{
    [Fact]
    public void Directory_dependency_preserves_members_and_requires_complete_selection()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-directory-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "config.json"), "{\"model_type\":\"fixture\"}");
            File.WriteAllText(Path.Combine(root, "weights.safetensors"), "fixture");
            var config = Resource("models/diffusers/bundle/config.json") with { SourcePath = Path.Combine(root, "config.json") };
            var weight = Resource("models/diffusers/bundle/weights.safetensors") with { SourcePath = Path.Combine(root, "weights.safetensors") };
            var selected = DependencyImportSelection.Model(Plan(config, weight, Resource("models/loras/unrelated.safetensors")), "diffusers", "bundle", null);
            Assert.Equal(2, selected.Count);
            Assert.Contains(selected, x => x.TargetRelativePath.EndsWith("/config.json"));
            Assert.Contains(selected, x => x.TargetRelativePath.EndsWith("/weights.safetensors"));
            Assert.Throws<InvalidDataException>(() => DependencyImportSelection.Model(Plan(config), "diffusers", "bundle", null));
        }
        finally { Directory.Delete(root, true); }
    }

    private static ImportResource Resource(string path, ResourceKind kind = ResourceKind.Model, string? hash = null) =>
        new(path, Path.Combine(Path.GetTempPath(), "selection-fixture", path), path, kind, path, 1, hash ?? new string('A', 64), RecognitionState.Confirmed, "fixture");
    private static ImportPlan Plan(params ImportResource[] files) => new("fixture", Path.Combine(Path.GetTempPath(), "bundle.zip"), Path.GetTempPath(), files, [], []);

    [Fact]
    public void Zip_selects_only_exact_category_and_relative_reference_not_other_members()
    {
        var expected = Resource("models/checkpoints/sub/model.safetensors");
        var plan = Plan(expected, Resource("models/loras/sub/model.safetensors"), Resource("payload/notes.json", ResourceKind.Asset));
        var result = DependencyImportSelection.Model(plan, "checkpoints", "sub/model.safetensors", null);
        Assert.Equal(expected.SourcePath, Assert.Single(result).SourcePath);
        Assert.Equal(expected.TargetRelativePath, result[0].TargetRelativePath);
        Assert.Throws<InvalidDataException>(() => DependencyImportSelection.Model(plan, "checkpoints", "other.safetensors", null));
    }
    [Fact]
    public void Explicit_local_file_still_requires_model_type_and_workflow_hash()
    {
        var file = Resource("renamed.safetensors"); var plan = Plan(file) with { Source = file.SourcePath };
        Assert.Equal("models/loras/required.safetensors", Assert.Single(DependencyImportSelection.Model(plan, "loras", "required.safetensors", file.Sha256)).TargetRelativePath);
        Assert.Throws<InvalidDataException>(() => DependencyImportSelection.Model(plan, "loras", "required.safetensors", new string('B', 64)));
        var json = Resource("settings.json", ResourceKind.Asset);
        Assert.Throws<InvalidDataException>(() => DependencyImportSelection.Model(Plan(json) with { Source = json.SourcePath }, "loras", "required.safetensors", null));
    }
    [Fact]
    public void Ambiguity_and_path_escape_are_not_resolved_by_renaming()
    {
        var file = Resource("models/clip/encoder.safetensors");
        var plan = Plan(file, file with { Id = "duplicate", SourcePath = file.SourcePath + ".safetensors" });
        Assert.Throws<InvalidDataException>(() => DependencyImportSelection.Model(plan, "text_encoders", "encoder.safetensors", null));
        Assert.Throws<InvalidDataException>(() => DependencyImportSelection.Model(Plan(file), "clip", "../../escape.safetensors", null));
        Assert.Equal("models/text_encoders/encoder.safetensors", Assert.Single(DependencyImportSelection.Model(Plan(file), "text_encoders", "clip/encoder.safetensors", null)).TargetRelativePath);
    }
}

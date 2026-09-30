using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class WorkflowFormatEvidenceTests
{
    [Theory]
    [InlineData("{\"1\":{\"class_type\":\"SaveImage\",\"inputs\":{}}}", true)]
    [InlineData("{\"version\":0.4,\"nodes\":[],\"links\":[]}", false)]
    public async Task Import_explains_format_without_rewriting_workflow(string raw, bool api)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-format-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "workflow.json");
            await File.WriteAllTextAsync(source, raw);
            var hash = await ResourceImportService.HashAsync(source);
            var plan = await new ResourceImportService().ImportAsync(source, Path.Combine(root, "staging"));
            var resource = Assert.Single(plan.Resources);
            Assert.Equal(ResourceKind.Workflow, resource.Kind);
            Assert.Equal(RecognitionState.Confirmed, resource.State);
            Assert.Equal(hash, resource.Sha256);
            Assert.Equal(raw, Assert.Single(plan.Workflows).RawJson);
            Assert.Equal(raw, await File.ReadAllTextAsync(resource.SourcePath));
            Assert.Equal(raw, await File.ReadAllTextAsync(source));
            if (api)
            {
                Assert.Contains("API 格式", resource.Evidence);
                Assert.Contains("文件打开入口", resource.Evidence);
            }
            else
            {
                Assert.Contains("UI 格式", resource.Evidence);
                Assert.DoesNotContain("侧栏打开为空", resource.Evidence);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}

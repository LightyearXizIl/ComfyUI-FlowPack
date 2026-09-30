using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class OnlineLocalHashMatcherTests
{
    private sealed class ProgressRecorder(Action<LocalHashProgress> report) : IProgress<LocalHashProgress>
    { public void Report(LocalHashProgress value) => report(value); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reports_streamed_bytes_and_releases_file_when_cancelled(bool cancel)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-hash-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "candidate.pth");
            await File.WriteAllBytesAsync(file, new byte[524288]);
            var declaration = new ResourceEntry("r", "candidate.pth", ResourceKind.Model, 524288, await ResourceImportService.HashAsync(file), null);
            using var cancellation = new CancellationTokenSource();
            var updates = new List<LocalHashProgress>();
            var progress = new ProgressRecorder(value => { updates.Add(value); if (cancel && value.CompletedBytes > 0) cancellation.Cancel(); });
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OnlineLocalHashMatcher.MatchAsync(declaration, [file], cancellation.Token, progress));
            else Assert.Equal(file, (await OnlineLocalHashMatcher.MatchAsync(declaration, [file], cancellation.Token, progress)).SourcePath);
            Assert.Equal(0, updates[0].CompletedBytes);
            Assert.All(updates, value => { Assert.Equal(1, value.CandidateIndex); Assert.Equal(1, value.CandidateCount); Assert.Equal(524288, value.TotalBytes); });
            if (cancel) Assert.InRange(updates[^1].CompletedBytes, 1, 131072);
            else Assert.Equal(524288, updates[^1].CompletedBytes);
            using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(524288, exclusive.Length);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Multiple_candidates_reuse_only_with_source_hash(bool suppliedHash)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-local-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wrong = Path.Combine(root, "a.pth"); var correct = Path.Combine(root, "b.pth");
            await File.WriteAllTextAsync(wrong, "wrong"); await File.WriteAllTextAsync(correct, "right");
            var hash = await ResourceImportService.HashAsync(correct);
            var declaration = new ResourceEntry("r", "model.pth", ResourceKind.Model, 5, suppliedHash ? hash.ToLowerInvariant() : null, null);
            var result = await OnlineLocalHashMatcher.MatchAsync(declaration, [Path.Combine(root, "missing.pth"), wrong, correct, correct]);
            Assert.Equal(suppliedHash ? correct : null, result.SourcePath);
            var noMatch = await OnlineLocalHashMatcher.MatchAsync(declaration with { SizeBytes = 6 }, [wrong, correct]);
            Assert.Null(noMatch.SourcePath);
            Assert.Null((await OnlineLocalHashMatcher.MatchAsync(declaration with { Kind = ResourceKind.CustomNode }, [correct])).SourcePath);
        }
        finally { Directory.Delete(root, true); }
    }
}

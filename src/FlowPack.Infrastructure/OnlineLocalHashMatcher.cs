using FlowPack.Core;

namespace FlowPack.Infrastructure;

public sealed record OnlineLocalMatchInput(string PlanId, string ResourceId, IReadOnlyList<string> CandidatePaths);
public sealed record OnlineLocalMatchResult(string? SourcePath, string Evidence);
public sealed record LocalHashProgress(string SourcePath, int CandidateIndex, int CandidateCount, long CompletedBytes, long TotalBytes);

public static class OnlineLocalHashMatcher
{
    public static async Task<OnlineLocalMatchResult> MatchAsync(ResourceEntry declaration, IReadOnlyList<string> paths, CancellationToken token = default,
        IProgress<LocalHashProgress>? progress = null)
    {
        if (declaration.Kind != ResourceKind.Model || declaration.Sha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit))
            return new(null, "多个本地候选需要有效的来源 SHA-256，不能仅按名称复用。");
        var candidates = paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < candidates.Length; index++)
        {
            var path = candidates[index];
            token.ThrowIfCancellationRequested();
            try
            {
                ResourceInstallationService.EnsureNoLinks(path);
                await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (source.Length != declaration.SizeBytes) continue;
                using var hasher = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                var buffer = new byte[131072]; long completed = 0; int read;
                progress?.Report(new(path, index + 1, candidates.Length, 0, source.Length));
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    hasher.AppendData(buffer, 0, read); completed += read;
                    progress?.Report(new(path, index + 1, candidates.Length, completed, source.Length));
                }
                token.ThrowIfCancellationRequested();
                if (completed == declaration.SizeBytes && string.Equals(Convert.ToHexString(hasher.GetHashAndReset()), hash, StringComparison.OrdinalIgnoreCase))
                    return new(path, "本地候选大小和来源 SHA-256 匹配；仍需载荷内容核验。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable candidate cannot prove a match; continue checking the others.
            }
        }
        return new(null, "本地候选均未通过大小和来源 SHA-256 核对，仍需补全。");
    }
}

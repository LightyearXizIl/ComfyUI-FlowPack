using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public static class OnlineLocalResourceMatcher
{
    // Inventory paths include category; matching a basename alone would cross model namespaces.
    public static bool MatchesModelReference(string target, LocalResource candidate)
    {
        if (candidate.Kind != ResourceKind.Model || candidate.Category is null) return false;
        try
        {
            PlannedZipExportService.ValidateRelative(target);
            PlannedZipExportService.ValidateRelative(candidate.RelativePath);
        }
        catch (InvalidDataException) { return false; }
        var expected = target.Replace('\\', '/').Split('/');
        var actual = candidate.RelativePath.Replace('\\', '/').Split('/');
        return expected.Length >= 3 && expected[0] == "models" && actual.Length >= 2 &&
            ResourceFiles.NormalizeCategory(expected[1]) == ResourceFiles.NormalizeCategory(candidate.Category) &&
            ResourceFiles.NormalizeCategory(expected[1]) == ResourceFiles.NormalizeCategory(actual[0]) &&
            string.Equals(string.Join('/', expected.Skip(2)), string.Join('/', actual.Skip(1)), StringComparison.OrdinalIgnoreCase);
    }
}

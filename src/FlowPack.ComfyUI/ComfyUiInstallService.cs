using System.IO;

namespace FlowPack.ComfyUI;

public enum DeployStepKind { Workflow, Model, CustomNode }

/// <summary>
/// One planned copy from a staged native package into a ComfyUI Desktop directory.
/// </summary>
public sealed record DeployStep(DeployStepKind Kind, string SourcePath, string TargetPath);

/// <summary>
/// Outcome of an install pass: which files were written, which were skipped (already present),
/// and any errors encountered (recorded, not thrown, so a partial install still reports cleanly).
/// </summary>
public sealed record DeployResult(
    IReadOnlyList<string> InstalledPaths,
    IReadOnlyList<string> SkippedPaths,
    IReadOnlyList<string> Errors);

/// <summary>
/// Deploys a staged ComfyUI native package (requirement 3 install half) into a detected ComfyUI
/// Desktop instance. This is the inverse of <see cref="ResourcePackageExportService"/>: it walks the
/// native layout (workflows/ + models/&lt;cat&gt;/ + custom_nodes/&lt;name&gt;/) and copies files into the
/// matching Desktop asset directories. Existing target files are skipped (never overwritten) so a
/// re-install is idempotent and never clobbers user data.
///
/// Safety boundary (HANDOFF / phase-F): this service performs file writes and must be invoked only
/// after a real-machine validation of the target Desktop instance. The App keeps StartInstallCommand
/// disabled until that evidence exists; this class is the implementation that command will call.
/// </summary>
public sealed class ComfyUiInstallService
{
    public IReadOnlyList<DeployStep> Plan(ComfyDesktopLocation desktop, string packageRoot)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        var steps = new List<DeployStep>();
        var root = Path.GetFullPath(packageRoot);

        if (desktop.WorkflowsDirectory is { } wfDir && Directory.Exists(Path.Combine(root, "workflows")))
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "workflows"), "*.json", SearchOption.TopDirectoryOnly))
            {
                steps.Add(new DeployStep(DeployStepKind.Workflow, file, Path.Combine(wfDir, Path.GetFileName(file))));
            }
        }

        if (desktop.ModelsDirectory is { } modelsDir && Directory.Exists(Path.Combine(root, "models")))
        {
            foreach (var category in Directory.EnumerateDirectories(Path.Combine(root, "models")))
            {
                var categoryName = Path.GetFileName(category)!;
                foreach (var file in Directory.EnumerateFiles(category, "*", SearchOption.TopDirectoryOnly))
                {
                    steps.Add(new DeployStep(DeployStepKind.Model, file, Path.Combine(modelsDir, categoryName, Path.GetFileName(file))));
                }
            }
        }

        if (desktop.CustomNodesDirectory is { } nodesDir && Directory.Exists(Path.Combine(root, "custom_nodes")))
        {
            foreach (var nodeDir in Directory.EnumerateDirectories(Path.Combine(root, "custom_nodes")))
            {
                var name = Path.GetFileName(nodeDir)!;
                steps.Add(new DeployStep(DeployStepKind.CustomNode, nodeDir, Path.Combine(nodesDir, name)));
            }
        }

        return steps;
    }

    public async Task<DeployResult> ApplyAsync(ComfyDesktopLocation desktop, string packageRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var installed = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        foreach (var step in Plan(desktop, packageRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(step.TargetPath) || Directory.Exists(step.TargetPath))
                {
                    skipped.Add(step.TargetPath);
                    continue;
                }

                if (step.Kind == DeployStepKind.CustomNode)
                {
                    CopyDirectory(step.SourcePath, step.TargetPath, cancellationToken);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(step.TargetPath))!);
                    File.Copy(step.SourcePath, step.TargetPath);
                }

                installed.Add(step.TargetPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                errors.Add($"{step.TargetPath}: {exception.Message}");
            }
        }

        await Task.CompletedTask;
        return new DeployResult(installed, skipped, errors);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)!), cancellationToken);
        }
    }
}

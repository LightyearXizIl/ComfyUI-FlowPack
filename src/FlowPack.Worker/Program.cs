using FlowPack.Infrastructure;
using FlowPack.ComfyUI;

if (args is ["--install-update", var updateRequest]) return await new UpdateBootstrap().RunAsync(updateRequest);

var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
if (args.Length % 2 != 0) return 2;
for (var i = 0; i < args.Length; i += 2)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal)) return 2;
    var key = args[i][2..].ToLowerInvariant();
    if (key is not ("pipe" or "secret" or "library" or "desktop-profile") || !arguments.TryAdd(key, args[i + 1])) return 2;
}
if (!arguments.TryGetValue("pipe", out var pipe) || !arguments.TryGetValue("secret", out var secret) || !arguments.TryGetValue("library", out var library)) return 2;
string? desktopProfile;
try { desktopProfile = DesktopProfileOptions.FromArguments(args); }
catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
{ Console.Error.WriteLine(ex.Message); return 2; }
library = Path.GetFullPath(library);
Directory.CreateDirectory(Path.Combine(library, "state"));
FileStream lease;
try { lease = new FileStream(Path.Combine(library, "state", "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
catch (IOException) { return 4; }
using (lease)
using (var cancellation = new CancellationTokenSource())
{
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var discovery = new DesktopInstanceDiscovery(configurationRoot: desktopProfile);
    await using var service = new PersistentWorkerService(new ResourceLibraryDatabase(library), discovery.DiscoverAsync,
        associate: discovery.AssociateAsync);
    try
    {
        await service.InitializeAsync(cancellation.Token);
        var server = new NamedPipeWorkerServer(pipe, secret);
        while (!cancellation.IsCancellationRequested && !service.ShutdownRequested)
        {
            try { await server.ServeOnceAsync(service.HandleAsync, cancellation.Token); }
            catch (IOException) { /* UI disconnection must not kill its background tasks. */ }
        }
        return 0;
    }
    catch (OperationCanceledException) { return 0; }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 3; }
}

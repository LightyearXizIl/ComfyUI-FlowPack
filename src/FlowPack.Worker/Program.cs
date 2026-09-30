using FlowPack.Infrastructure;

if (args is ["--install-update", var updateRequest]) return await new UpdateBootstrap().RunAsync(updateRequest);

var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i + 1 < args.Length; i += 2) arguments[args[i].TrimStart('-')] = args[i + 1];
if (!arguments.TryGetValue("pipe", out var pipe) || !arguments.TryGetValue("secret", out var secret) || !arguments.TryGetValue("library", out var library)) return 2;
library = Path.GetFullPath(library);
Directory.CreateDirectory(Path.Combine(library, "state"));
FileStream lease;
try { lease = new FileStream(Path.Combine(library, "state", "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
catch (IOException) { return 4; }
using (lease)
using (var cancellation = new CancellationTokenSource())
{
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    await using var service = new PersistentWorkerService(new ResourceLibraryDatabase(library));
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

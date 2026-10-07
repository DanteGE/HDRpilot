using HdrPilot.Cli;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Native;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var app = new CliApp(new SystemCliServices(), Console.Out, Console.Error);
return await app.RunAsync(args, cts.Token);

internal sealed class SystemCliServices : ICliServices
{
    public IHdrController CreateHdrController(HdrControllerOptions options) =>
        new DisplayConfigHdrController(
            Win32DisplayConfigApi.Instance,
            SystemEnvironment.Instance,
            SystemClock.Instance,
            options,
            new Win32HdrHotkeySender());

    public IReadOnlyList<IGameScanner> CreateScanners() =>
        ScannerCatalog.CreateAll(PhysicalFileSystem.Instance, WindowsRegistry.Instance, SystemEnvironment.Instance);
}

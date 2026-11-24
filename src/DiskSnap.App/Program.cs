using System.Runtime.InteropServices;
using System.Security.Principal;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Storage;

namespace DiskSnap.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        AttachConsole(unchecked((uint)-1));
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (!string.Equals(args[0], "--scan", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Usage: DiskSnap.exe --scan C: D: [--quiet]");
                return 1;
            }

            var quiet = false;
            var drives = new List<string>();
            foreach (var argument in args.Skip(1))
            {
                if (string.Equals(argument, "--quiet", StringComparison.OrdinalIgnoreCase) && !quiet)
                {
                    quiet = true;
                }
                else if (argument.StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Unknown or repeated option: {argument}");
                    return 1;
                }
                else
                {
                    drives.Add(argument);
                }
            }

            if (!IsElevated())
            {
                Console.Error.WriteLine("Warning: running without administrator privileges; some paths may be skipped.");
            }

            return new ScanRunner(new SnapshotStore()).RunAsync(drives, quiet, Console.Out, Console.Error, cancellation.Token).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"DiskSnap failed: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}

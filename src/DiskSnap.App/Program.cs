using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using DiskSnap.Core.Reporting;
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
            if (!IsElevated())
            {
                try
                {
                    var startInfo = new ProcessStartInfo(Environment.ProcessPath
                        ?? throw new InvalidOperationException("The executable path is unavailable."))
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    startInfo.ArgumentList.Add("--gui");
                    using var elevatedProcess = Process.Start(startInfo);
                    if (elevatedProcess is not null)
                    {
                        return 0;
                    }

                    return RunGui("Administrator restart did not start; continuing without elevation.");
                }
                catch (Win32Exception exception)
                {
                    var warning = exception.NativeErrorCode == 1223
                        ? "Administrator privileges were declined; continuing without elevation. Some paths may be skipped."
                        : $"Could not restart as administrator; continuing without elevation. {exception.Message}";
                    return RunGui(warning);
                }
            }

            return RunGui();
        }

        if (args.Length == 1 && string.Equals(args[0], "--gui", StringComparison.OrdinalIgnoreCase))
        {
            return RunGui(IsElevated() ? null : "DiskSnap is running without administrator privileges. Some paths may be skipped.");
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
            if (string.Equals(args[0], "--compare", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseCompareArguments(args, out var drive, out var aPath, out var bPath, out var outputPath, out var parseError))
                {
                    Console.Error.WriteLine(parseError);
                    return 1;
                }

                return new ComparisonReportRunner(new SnapshotStore())
                    .RunAsync(drive, aPath, bPath, outputPath, Console.Out, Console.Error, cancellation.Token)
                    .GetAwaiter().GetResult();
            }

            if (!string.Equals(args[0], "--scan", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Usage: DiskSnap.exe --scan C: D: [--quiet] | --compare C: [--a <file> --b <file>] --out <file>");
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

    private static bool TryParseCompareArguments(
        IReadOnlyList<string> args,
        out string drive,
        out string? aPath,
        out string? bPath,
        out string? outputPath,
        out string error)
    {
        drive = args.Count > 1 ? args[1] : string.Empty;
        aPath = null;
        bPath = null;
        outputPath = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(drive))
        {
            error = "Usage: DiskSnap.exe --compare C: [--a <file> --b <file>] --out <file>";
            return false;
        }

        for (var index = 2; index < args.Count; index++)
        {
            var option = args[index];
            if (!string.Equals(option, "--a", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(option, "--b", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(option, "--out", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Unknown or repeated option: {option}";
                return false;
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                error = $"An output value is required after {option}.";
                return false;
            }

            var value = args[++index];
            if (string.Equals(option, "--a", StringComparison.OrdinalIgnoreCase))
            {
                if (aPath is not null)
                {
                    error = "Unknown or repeated option: --a";
                    return false;
                }
                aPath = value;
            }
            else if (string.Equals(option, "--b", StringComparison.OrdinalIgnoreCase))
            {
                if (bPath is not null)
                {
                    error = "Unknown or repeated option: --b";
                    return false;
                }
                bPath = value;
            }
            else
            {
                if (outputPath is not null)
                {
                    error = "Unknown or repeated option: --out";
                    return false;
                }
                outputPath = value;
            }
        }

        if (outputPath is null)
        {
            error = "--out is required.";
            return false;
        }
        if ((aPath is null) != (bPath is null))
        {
            error = "--a and --b must be supplied together.";
            return false;
        }
        return true;
    }

    private static int RunGui(string? warning = null)
    {
        var app = new App { StartupWarning = warning };
        app.InitializeComponent();
        return app.Run();
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
